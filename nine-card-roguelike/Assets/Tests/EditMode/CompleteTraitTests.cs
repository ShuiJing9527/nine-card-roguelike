using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Qiling.Tests
{
    public class CompleteTraitTests
    {
        BattleCatalog data;
        [SetUp] public void Setup() { data=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); }
        DebugLoadout Profile(int weapon=0,string main=null)
        {
            var p=DebugLoadout.Initial(data,data.weapons[weapon].id);
            p.sequence=(main==null?data.weapons[weapon].cards.Take(5):new[]{main}.Concat(data.weapons[weapon].cards.Where(id=>id!=main).Take(4))).ToList();
            Assert.IsNull(p.Preset(data,p.sequence,new[]{"同调"})); return p;
        }
        void Set(DebugLoadout p,int slot,params string[] traits) => p.cards.Single(c=>c.cardId==p.sequence[slot-1]).traits=traits.ToList();
        BattleEngine Engine(DebugLoadout p)
        {
            Assert.IsNull(p.Validate(data)); var e=new BattleEngine(data,42,p); e.Start(data.weapons.FindIndex(w=>w.id==p.weaponId),0);
            e.Player.panel.crit=0; e.Units[1].hp=e.Units[1].panel.hp=10000; e.Units[1].panel.defense=e.Units[1].panel.specialDefense=0;
            e.Player.progress=100; e.Advance(0); return e;
        }
        (int weapon,string card) Skill(string id)
        {
            int weapon=data.weapons.FindIndex(w=>w.cards.Any(c=>data.Card(c).skill==id));
            return (weapon,data.weapons[weapon].cards.First(c=>data.Card(c).skill==id));
        }
        [Test] public void AllDefinedTraitsEquipSaveReadAndRejectOnlyPendingPlaceholder()
        {
            Assert.AreEqual(25,data.traits.Count(TraitSupport.Enabled));
            foreach(var trait in data.traits.Where(TraitSupport.Enabled))
            {
                var p=Profile(); Assert.IsNull(p.SetTrait(data,p.sequence[2],0,trait.id)); Assert.IsNull(p.Validate(data));
                var loaded=JsonUtility.FromJson<DebugLoadout>(JsonUtility.ToJson(p)); var e=Engine(loaded); Assert.AreEqual(trait.id,e.Card(p.sequence[2]).traits[0]);
                Assert.IsNotEmpty(e.Plan(e.Player,p.sequence).traitHints);
            }
            var bad=Profile(); Assert.IsNotNull(bad.SetTrait(data,bad.sequence[0],0,"待定"));
        }
        [Test] public void FocusReallyRaisesAndCapsCriticalRateWithoutChangingCoefficient()
        {
            var p=Profile(); Set(p,5,"专注"); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence);
            Assert.AreEqual(.25,plan.crit,1e-9); Assert.AreEqual(.6,plan.nativeCoefficient);
            e.Player.panel.crit=.9; plan=e.Plan(e.Player,p.sequence); Assert.AreEqual(1,plan.crit);
            e.Execute(e.Player,plan,"enemy0"); Assert.IsTrue(e.Damage.All(d=>d.critical)); Assert.IsTrue(e.Log.Any(l=>l.Contains("专注") && l.Contains("有效")));
        }
        [Test] public void SuppressionChecksCurrentHpAtEveryNativeHit()
        {
            var p=Profile(); Set(p,1,"压制"); var e=Engine(p); var t=e.Units[1]; t.hp=t.panel.hp=100;
            e.Execute(e.Player,e.Plan(e.Player,p.sequence),t.id); var hits=e.Damage.Where(d=>d.target==t.id).ToArray();
            CollectionAssert.AreEqual(new[]{11,11,11,9,9,9,9},hits.Select(d=>d.calculated));
            Assert.AreEqual(.2,hits[0].damageUp,1e-9); Assert.AreEqual(0,hits[3].damageUp);
            t.hp=70; Assert.IsFalse(new ConditionalBonus {id="压制",threshold=.7}.Applies(t));
        }
        [Test] public void ExecuteChecksStrictThresholdAndNewTargetAfterKill()
        {
            var p=Profile(); Set(p,1,"终结"); var e=Engine(p); var t=e.Units[1]; t.hp=30; t.panel.hp=100;
            e.Execute(e.Player,e.Plan(e.Player,p.sequence),t.id);
            Assert.AreEqual(9,e.Damage[0].calculated); Assert.AreEqual(11,e.Damage[1].calculated); Assert.AreEqual(.2,e.Damage[1].damageUp,1e-9);
            var transferred=e.Damage.First(d=>d.target=="enemy1"); Assert.AreEqual(0,transferred.damageUp); Assert.AreEqual(15,transferred.defense);
        }
        [Test] public void OpeningDebuffEnablesOpportunityOnlyOnLaterPursuits()
        {
            var p=Profile(); Set(p,2,"追击"); Set(p,3,"乘隙"); var e=Engine(p);
            // Isolate a real on-hit debuff transition without changing the catalog's card definitions.
            var plan=e.Plan(e.Player,p.sequence); var synthetic=JsonUtility.FromJson<SkillDef>(JsonUtility.ToJson(plan.skill));
            synthetic.effects.Add(new EffectDef {kind=EffectKind.Poison,value=new Linear {basis=2},onHit=true,stacking=true,negative=true});
            plan.skill=synthetic; plan.nativeSegments=1;
            e.Execute(e.Player,plan,"enemy0");
            Assert.AreEqual(0,e.Damage[0].damageUp); var pursuit=e.Damage.Where(d=>d.label.StartsWith("追击[")).ToArray();
            Assert.AreEqual(2,pursuit.Length); Assert.IsTrue(pursuit.All(d=>Math.Abs(d.damageUp-.45)<1e-9));
            Assert.IsTrue(e.Log.Any(l=>l.Contains("条件词条乘隙") && l.Contains("未触发"))); Assert.IsTrue(e.Log.Any(l=>l.Contains("条件词条乘隙") && l.Contains("生效")));
        }
        [TestCase(false)] [TestCase(true)] public void RiftOnlyAddsCorrespondingPenetrationOnCriticalSegments(bool special)
        {
            var skill=special?Skill("魔力飞弹"):(weapon:0,card:data.weapons[0].cards[0]); var p=Profile(skill.weapon,skill.card); Set(p,3,"裂隙"); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence);
            plan.physicalPen=.8; plan.specialPen=.9; var target=e.Units[1]; target.panel.defense=20; target.panel.specialDefense=30;
            var normal=BattleMath.Quote(data.rules,e.Player,target,plan,.5,0,0,false); Assert.AreEqual(special?3:4,normal.effectiveDefense,1e-9);
            var critical=BattleMath.Quote(data.rules,e.Player,target,plan,.5,0,0,true); Assert.AreEqual(1,critical.penetration); Assert.AreEqual(0,critical.effectiveDefense);
            plan.crit=1; e.Execute(e.Player,plan,"enemy0"); Assert.IsTrue(e.Damage.All(d=>d.critical && d.penetration==1 && d.effectiveDefense==0));
        }
        [Test] public void ThroughSplitsCorrectedDamageAndConservesTotalWithShieldAndOverkill()
        {
            var p=Profile(); Set(p,5,"贯通"); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence); Assert.AreEqual(.25,plan.bypass);
            var t=e.Units[1]; t.shield=1000; e.Execute(e.Player,plan,t.id);
            Assert.IsTrue(e.Damage.All(d=>d.calculated==9 && d.bypass==2 && d.absorbed==7 && d.hpLost==2));
            Assert.IsTrue(e.Damage.All(d=>d.calculated==d.absorbed+d.hpLost+d.overkill));
            t.hp=1; t.shield=100; e.ApplyDamage(t,20,"贯通致死",new DamageRecord {bypassRatio=.5,target=t.id}); var lethal=e.Damage.Last();
            Assert.AreEqual(10,lethal.absorbed); Assert.AreEqual(1,lethal.hpLost); Assert.AreEqual(9,lethal.overkill);
            t.hp=100; t.shield=10; e.ApplyDamage(t,20,"间接伤害"); Assert.AreEqual(0,e.Damage.Last().bypass); Assert.AreEqual(10,e.Damage.Last().absorbed);
        }
        [TestCase("连锁收益",3,2)] [TestCase("连锁收益",2,1)]
        [TestCase("连锁蓄势",3,1.5)] [TestCase("连锁蓄势",2,1)]
        [TestCase("连锁协奏",2,1.75)] [TestCase("连锁协奏",3,1)]
        [TestCase("同调",2,1.5)] [TestCase("同调",1,1)]
        [TestCase("交织",4,1.5)] [TestCase("交织",1,1)] [TestCase("交织",2,1)]
        public void GainConditionsUseRevisedChainAndAdjacentMainTypes(string gain,int slot,double factor)
        {
            var p=Profile(); Set(p,slot,"锋锐",gain); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence);
            Assert.AreEqual(slot*.1*factor,plan.damageUp,1e-9);
        }
        [Test] public void GainsAddWithoutRecursionAndPursuitCoefficientStaysFiftyPercent()
        {
            var p=Profile(); Set(p,3,"锋锐","连锁收益","连锁蓄势"); var e=Engine(p); Assert.AreEqual(.75,e.Plan(e.Player,p.sequence).damageUp,1e-9);
            Set(p,3,"追击","连锁收益","连锁收益"); var plan=e.Plan(e.Player,p.sequence,configuration:p); Assert.AreEqual(9,plan.pursuits.Single().count); Assert.AreEqual(.5,plan.pursuits.Single().coefficient);
            Set(p,3,"追击","连锁反应","连锁收益"); plan=e.Plan(e.Player,p.sequence,configuration:p); Assert.AreEqual(6,plan.currentChains[2]); Assert.AreEqual(12,plan.pursuits.Single().count); Assert.AreEqual(.5,plan.pursuits.Single().coefficient);
            Set(p,3,"追击","连锁反应","连锁协奏"); plan=e.Plan(e.Player,p.sequence,configuration:p); Assert.AreEqual(11,plan.pursuits.Single().count); Assert.AreEqual(6,plan.currentChains[2]);
            Set(p,3,"追击","连锁反应","连锁蓄势"); plan=e.Plan(e.Player,p.sequence,configuration:p); Assert.AreEqual(6,plan.pursuits.Single().count);
        }
        [Test] public void HarmonyAndWeavingCannotMakeMismatchedBeneficiaryEffective()
        {
            var p=Profile(); Set(p,4,"深化","交织"); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence);
            Assert.AreEqual(0,plan.extraStacks); Assert.IsTrue(plan.traitHints.Any(h=>h.Contains("深化") && h.Contains("类型不匹配")));
        }
        [Test] public void MirrorCopiesNativeSegmentsWithoutTraitsPursuitsBonusOrOtherMirrors()
        {
            var p=Profile(); Set(p,1,"锋锐","壁障","追击"); Set(p,5,"镜像"); var e=Engine(p); var plan=e.Plan(e.Player,p.sequence);
            Assert.AreEqual(1,plan.mirrors.Single().strength); e.Execute(e.Player,plan,"enemy0"); var mirror=e.Damage.Where(d=>d.mirror).ToArray();
            Assert.AreEqual(7,mirror.Length); Assert.IsTrue(mirror.All(d=>d.coefficient==.6 && d.damageUp==0 && d.bonus==0 && d.resource==0 && d.calculated==9));
            Assert.AreEqual(1,e.Damage.Count(d=>d.label.StartsWith("追击["))); Assert.AreEqual(15,e.Damage.Count);
            Assert.AreEqual(2,e.VisualEvents.Count(v=>v.node==VisualNode.CastStart));
        }
        [TestCase("治愈术")] [TestCase("坚盾")]
        public void MirrorCopiesConfirmedHealingAndShieldBase(string name)
        {
            var skill=Skill(name); var p=Profile(skill.weapon,skill.card); Set(p,5,"镜像"); var e=Engine(p); e.Player.hp=1; e.Player.panel.hp=10000;
            e.Execute(e.Player,e.Plan(e.Player,p.sequence),"player"); var kind=name=="治愈术"?VisualNode.Heal:VisualNode.Shield; var events=e.VisualEvents.Where(v=>v.node==kind).ToArray();
            Assert.AreEqual(2,events.Length); Assert.Greater(events[0].amount,0); Assert.AreEqual(events[0].amount,events[1].amount);
        }
        [Test] public void MirrorRestrictionsOnlyBlockSpecificResourceAndControlCombinations()
        {
            var skill=Skill("碎盾冲击"); var p=Profile(skill.weapon,skill.card); Set(p,5,"镜像"); var e=Engine(p); e.Player.shield=20;
            var plan=e.Plan(e.Player,p.sequence); Assert.AreEqual(0,plan.mirrors.Count); Assert.IsTrue(plan.traitHints.Any(h=>h.Contains("镜像不执行") && h.Contains("资源")));
            e.Execute(e.Player,plan,"enemy0"); Assert.AreEqual(20-BattleMath.Round(20*plan.skill.shieldCost),e.Player.shield); Assert.IsFalse(e.Damage.Any(d=>d.mirror)); Assert.IsTrue(TraitSupport.Enabled(data.Trait("镜像")));
        }
        [Test] public void MirrorScalesFractionalDamageAndKeepsIndividualHitsAndLegalRetargets()
        {
            var p=Profile(); Set(p,3,"镜像"); var e=Engine(p); e.Units[1].hp=1; e.Units[2].hp=e.Units[2].panel.hp=10000;
            var plan=e.Plan(e.Player,p.sequence); Assert.AreEqual(.6,plan.mirrors.Single().strength,1e-9);
            e.Execute(e.Player,plan,"enemy0"); var hits=e.Damage.Where(d=>d.mirror).ToArray(); Assert.AreEqual(7,hits.Length);
            Assert.IsTrue(hits.All(d=>d.target=="enemy1" && Math.Abs(d.coefficient-.36)<1e-9 && d.bonus==0 && d.damageUp==0));
        }
        [Test] public void MirrorRejectsOnlyUnconfirmedControlDurationAndFractionalStacks()
        {
            var poison=Skill("毒箭矢"); var p=Profile(poison.weapon,poison.card); var full=p.sequence.ToList(); Set(p,3,"镜像"); p.sequence=p.sequence.Take(3).ToList(); var e=Engine(p);
            var plan=e.Plan(e.Player,p.sequence); Assert.AreEqual(0,plan.mirrors.Count); Assert.IsTrue(plan.traitHints.Any(h=>h.Contains("取整与最低值未定")));
            p.sequence=full; Set(p,3,"同调"); Set(p,5,"镜像"); plan=e.Plan(e.Player,p.sequence,configuration:p); Assert.AreEqual(1,plan.mirrors.Count);
            e.Execute(e.Player,plan,"enemy0"); Assert.IsTrue(e.Damage.Any(d=>d.mirror)); Assert.AreEqual(10,e.Units[1].State(EffectKind.Poison).value);
            var cleanse=Skill("净化术"); p=Profile(cleanse.weapon,cleanse.card); Set(p,5,"镜像"); e=Engine(p); plan=e.Plan(e.Player,p.sequence);
            Assert.AreEqual(0,plan.mirrors.Count); Assert.IsTrue(plan.traitHints.Any(h=>h.Contains("非数值控制") && h.Contains("主技能照常")));
        }
        [Test] public void MirrorBounceKeepsNativePathAndNeverGeneratesPursuit()
        {
            var skill=Skill("回旋飞盾"); var p=Profile(skill.weapon,skill.card); Set(p,5,"镜像"); var e=Engine(p); e.Units[2].hp=e.Units[2].panel.hp=10000;
            e.Execute(e.Player,e.Plan(e.Player,p.sequence),"enemy0"); var launches=e.VisualEvents.Where(v=>v.node==VisualNode.Launch && v.segment.StartsWith("镜像·")).ToArray();
            Assert.AreEqual(6,launches.Length); for(int i=1;i<launches.Length;i++) { Assert.AreEqual(launches[i-1].target,launches[i].flightFrom); Assert.AreNotEqual(launches[i-1].target,launches[i].target); }
            Assert.IsTrue(e.Damage.Where(d=>d.mirror).All(d=>!d.label.Contains("追击")));
        }
        [Test] public void AllNewEffectsInPreviewUseCloneAndPreserveFormalRngAndSnapshots()
        {
            var p=Profile(); Set(p,1,"专注","压制","镜像"); Set(p,2,"裂隙","贯通","追击"); Set(p,3,"追击","连锁反应","连锁收益");
            var e=Engine(p); e.Units[1].shield=30; long draws=e.Rng.Draws; int log=e.Log.Count; string preview=e.Preview(p.sequence,"enemy0");
            Assert.AreEqual(draws,e.Rng.Draws); Assert.AreEqual(0,e.Damage.Count); Assert.AreEqual(log,e.Log.Count); Assert.AreEqual(30,e.Units[1].shield);
            Assert.IsTrue(preview.Contains("镜像")); Assert.IsTrue(preview.Contains("贯通")); Assert.IsTrue(preview.Contains("条件词条"));
        }
    }
}
