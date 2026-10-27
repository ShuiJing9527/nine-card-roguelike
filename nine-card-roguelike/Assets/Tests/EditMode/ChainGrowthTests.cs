using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Qiling.Tests
{
    public class ChainGrowthTests
    {
        public static readonly string[] Buffs={"鹰眼校准","磐石之躯","狂战之怒","魔力灌注","轻羽术","迅攻架势","钢铁意志","护盾强化","战意","护身之风","疾风步","坚岩加护","盾墙号令"};
        BattleCatalog data;
        [SetUp] public void Setup() { data=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset")); }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(data); }
        static IEnumerable<TestCaseData> Cases() => Buffs.Select(s=>new TestCaseData(s).SetName("ChainDuration_"+s));
        BattleEngine New(string skillName,int chain,int extension,out string[] cards)
        {
            var w=data.weapons[0]; cards=w.cards.Take(chain).ToArray();
            data.Card(cards[0]).skill=skillName; data.Card(cards[0]).match=MatchType.Status;
            var profile=DebugLoadout.Initial(data,w.id); profile.Preset(data,cards,new[]{"专注"});
            if(extension==1 || extension==2) profile.SetTrait(data,cards.Last(),0,"延续");
            if(extension==2) profile.SetTrait(data,cards.Last(),1,"连锁反应");
            if(extension==3) profile.Preset(data,cards,new[]{"延续"});
            var e=new BattleEngine(data,101,profile); e.Start(0,0); e.Player.progress=100; e.Advance(0); return e;
        }
        [TestCaseSource(nameof(Cases))] public void EveryRegularBuffUsesActualTotalChainThenCombinedContinuation(string skillName)
        {
            foreach(var n in new[]{1,3,5})
            {
                var e=New(skillName,n,0,out var cards); var skill=data.Skill(skillName); var plan=e.Plan(e.Player,cards);
                foreach(var effect in skill.effects.Where(f=>f.HasDuration)) { Assert.AreEqual(0,effect.duration); Assert.AreEqual(1,effect.durationPerChain); Assert.AreEqual(n,effect.BaseDuration(n)); }
                Assert.IsNull(e.Confirm(cards,"player"));
                foreach(var effect in skill.effects.Where(f=>f.HasDuration)) Assert.AreEqual(n,e.Player.State(effect.kind).remaining,skillName+" / "+effect.kind);
                Assert.IsTrue(e.Log.Any(l=>l.Contains("主技能总连锁"+n)));
            }
            int[] durations={5,8,10,13};
            for(int extension=0;extension<4;extension++)
            {
                var e=New(skillName,5,extension,out var cards); var plan=e.Plan(e.Player,cards);
                Assert.AreEqual(5,plan.chain); Assert.AreEqual(extension==2?10:5,plan.currentChains[4]);
                long draws=e.Rng.Draws; StringAssert.Contains("＝"+durations[extension]+"回合",e.Preview(cards,"player")); Assert.AreEqual(draws,e.Rng.Draws);
                Assert.IsNull(e.Confirm(cards,"player"));
                foreach(var effect in plan.skill.effects.Where(f=>f.HasDuration))
                {
                    var state=e.Player.State(effect.kind); Assert.AreEqual(durations[extension],state.remaining,skillName+" / "+effect.kind);
                    double amount=effect.value.At(5); Assert.AreEqual(amount,state.value,1e-9,"Duration must not change the existing strength");
                }
            }
        }
        [Test] public void BerserkBenefitsAndDefensePenaltiesExpireTogetherAndDoNotAmplifyCosts()
        {
            var e=New("狂战之怒",5,2,out var cards); var profile=e.Loadout;
            profile.SetTrait(data,cards[0],0,"增幅"); e.ApplyLoadout(profile); var p=e.Plan(e.Player,cards);
            Assert.AreEqual(5,p.chain); Assert.IsNull(e.Confirm(cards,"player"));
            Assert.AreEqual(10,e.Player.State(EffectKind.AttackUp).remaining);
            Assert.AreEqual(10,e.Player.State(EffectKind.DefensePercent).remaining);
            Assert.AreEqual(10,e.Player.State(EffectKind.SpecialDefensePercent).remaining);
            Assert.AreEqual(-.3,e.Player.State(EffectKind.DefensePercent).value,1e-9);
            Assert.AreEqual(10,e.Player.State(EffectKind.AttackUp).value,1e-9);
        }
        [Test] public void GaleStepSeparatesPercentageSpeedFromImmediateProgressAndKeepsCap()
        {
            var e=New("疾风步",5,0,out var cards); var profile=e.Loadout;
            profile.SetTrait(data,cards[4],0,"增幅"); e.ApplyLoadout(profile);
            double initial=e.Player.progress; var p=e.Plan(e.Player,cards); e.Execute(e.Player,p,"player");
            Assert.AreEqual(initial,e.Player.progress,1e-9,"Already at 100%; speed must not move the existing bar");
            Assert.AreEqual(.1875,e.Player.State(EffectKind.SpeedPercent).value,1e-9);
            Assert.IsNull(e.Player.State(EffectKind.SpeedUp)); Assert.AreEqual(37.5,e.Player.selfProgress,1e-9);
            e.EndAction(e.Player); Assert.AreEqual(37.5,e.Player.progress,1e-9);
            foreach(var enemy in e.Units.Where(u=>!u.player)) enemy.panel.speed=0;
            e.Advance(1); Assert.AreEqual(37.5+e.Player.panel.speed*1.1875,e.Player.progress,1e-9);
            var capped=New("疾风步",5,0,out cards); profile=capped.Loadout; profile.Preset(data,cards,new[]{"增幅","增幅","增幅"}); capped.ApplyLoadout(profile);
            var cp=capped.Plan(capped.Player,cards); capped.Execute(capped.Player,cp,"player"); capped.EndAction(capped.Player);
            Assert.AreEqual(Math.Min(data.rules.pushCap,.3*(1+cp.amplify))*100,capped.Player.progress,1e-9);
        }
        [Test] public void AllAllyBuffAppliesToEachActualRecipientWithoutTurningIntoEnemyEffect()
        {
            var e=New("盾墙号令",5,1,out var cards); var ally=e.Player.Copy(); ally.id="ally"; ally.states.Clear(); e.Units.Add(ally);
            e.Execute(e.Player,e.Plan(e.Player,cards),"player");
            foreach(var u in new[]{e.Player,ally}) { Assert.AreEqual(8,u.State(EffectKind.DefenseUp).remaining); Assert.AreEqual(5,u.State(EffectKind.DefenseUp).value); }
            Assert.IsNull(e.Units[1].State(EffectKind.DefenseUp));
        }
        [Test] public void ExcludedSkillsKeepLiteralDurationAndBlockCountDoesNotReadExtension()
        {
            foreach(var skill in data.skills.Where(s=>!Buffs.Contains(s.id)))
            foreach(var effect in skill.effects) Assert.AreEqual(0,effect.durationPerChain,skill.id+" must not acquire chain duration");
            var e=New("旋矛御箭",5,3,out var cards); e.Execute(e.Player,e.Plan(e.Player,cards),"player");
            Assert.AreEqual(10,e.Player.State(EffectKind.Block).value); Assert.AreEqual(3,e.Player.State(EffectKind.Block).remaining);
            Assert.AreEqual(1,data.Skill("净化术").effects.Single(f=>f.kind==EffectKind.DebuffImmune).duration);
            Assert.AreEqual(2,data.Skill("棘甲壁垒").effects.Single().duration);
        }
        [Test] public void MultipleCastsInsideOneActionDoNotTickUntilTheSingleActionEnd()
        {
            var e=New("鹰眼校准",5,0,out var cards); e.Confirm(cards,"player");
            e.ActiveId=null; e.Waiting.Clear(); foreach(var u in e.Units) u.progress=0; e.Player.progress=100; e.Advance(0);
            var attack=data.Skill("连射"); var plan=e.Plan(e.Player,cards,attack); e.Units[1].hp=e.Units[1].panel.hp=10000;
            e.Execute(e.Player,plan,"enemy0"); e.Execute(e.Player,plan,"enemy0"); Assert.AreEqual(5,e.Player.State(EffectKind.AttackUp).remaining);
            e.EndAction(e.Player); Assert.AreEqual(4,e.Player.State(EffectKind.AttackUp).remaining);
        }
        [Test] public void LandingIsARealSecondIndependentRecordAndPursuitRemainsFixedHalf()
        {
            var e=New("挑空刺",5,0,out var cards); e.Units[1].hp=e.Units[1].panel.hp=10000;
            var p=e.Plan(e.Player,cards); p.crit=0; e.Execute(e.Player,p,"enemy0");
            Assert.AreEqual(2,e.Damage.Count); Assert.AreEqual(2.2,e.Damage[0].coefficient,1e-9); Assert.AreEqual(1.65,e.Damage[1].coefficient,1e-9);
            Assert.AreEqual("落地测试",e.Damage[1].label); Assert.AreEqual(2,e.VisualEvents.Count(v=>v.node==VisualNode.Impact));
            data.Card(cards[0]).match=MatchType.Offense;
            var profile=e.Loadout; profile.Preset(data,cards,new[]{"追击"}); e.ApplyLoadout(profile);
            p=e.Plan(e.Player,cards); Assert.AreEqual(15,p.pursuits.Sum(g=>g.count)); Assert.IsTrue(p.pursuits.All(g=>g.coefficient==.5));
        }
        [Test] public void RuleSnapshotAndInspectorAssetsAgreeOnAllConfirmedGrowthFields()
        {
            var snapshot=ScriptableObject.CreateInstance<BattleCatalog>();
            try
            {
                JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText("Assets/Data/InitialRules.json"),snapshot);
                Assert.AreEqual(data.skills.Count,snapshot.skills.Count); Assert.AreEqual(data.skills.Count,data.skills.Select(s=>s.id).Distinct().Count());
                foreach(var name in Buffs)
                {
                    var actual=data.Skill(name); var source=snapshot.Skill(name); Assert.AreEqual(source.description,actual.description);
                    Assert.AreEqual(source.effects.Count,actual.effects.Count);
                    for(int i=0;i<source.effects.Count;i++) { Assert.AreEqual(source.effects[i].durationPerChain,actual.effects[i].durationPerChain); Assert.AreEqual(source.effects[i].value.perChain,actual.effects[i].value.perChain); Assert.AreEqual(source.effects[i].kind,actual.effects[i].kind); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(snapshot); }
        }
        [Test] public void AllPrebuiltCardsUseOneThreeFiveActualTotalChainAndOneSharedSkillDefinition()
        {
            int casts=0;
            foreach(var w in data.weapons)
            foreach(var main in w.cards)
            foreach(int n in new[]{1,3,5})
            {
                var cards=new[]{main}.Concat(w.cards.Where(c=>c!=main).Take(n-1)).ToArray();
                var profile=DebugLoadout.Initial(data,w.id);
                foreach(var card in profile.cards) card.traits=new List<string>{"同调"}; // No other trait to amplify.
                var e=new BattleEngine(data,17,profile); e.Start(data.weapons.IndexOf(w),0);
                e.Player.progress=100; e.Advance(0); foreach(var enemy in e.Units.Where(u=>!u.player)) enemy.hp=enemy.panel.hp=100000;
                var skill=data.Skill(data.Card(main).skill); var p=e.Plan(e.Player,cards);
                Assert.AreEqual(n,p.chain); CollectionAssert.AreEqual(Enumerable.Range(1,n),p.currentChains);
                Assert.AreSame(skill,p.skill); Assert.AreEqual(skill.coefficient.At(n),p.nativeCoefficient,1e-9);
                int expectedSegments=skill.pattern==Pattern.Bounce?(e.LegalTargets(e.Player,skill).Count>=2?1+n:1):Math.Max(1,(int)skill.segments.At(n));
                Assert.AreEqual(expectedSegments,p.nativeSegments);
                var targets=e.LegalTargets(e.Player,skill);
                if(targets.Count==0) { StringAssert.Contains("没有另一名己方单位",e.Validate(e.Player,cards,"player")); continue; }
                string target=targets[0].id; long draws=e.Rng.Draws; string preview=e.Preview(cards,target); Assert.AreEqual(draws,e.Rng.Draws);
                StringAssert.Contains("总连锁："+n,preview); Assert.IsNull(e.Confirm(cards,target)); casts++;
                if(skill.coefficient.At(n)>0 && skill.pattern!=Pattern.Counter) Assert.IsTrue(e.Damage.Any(d=>d.skill==skill.id),skill.id);
                if(skill.pattern==Pattern.Counter) Assert.NotNull(e.Player.states.Single(s=>s.reaction!=null));
                foreach(var effect in skill.effects.Where(f=>f.HasDuration && f.kind!=EffectKind.Guard && !f.onHit))
                {
                    var receiver=e.Units.Single(u=>u.id==target); var state=receiver.State(effect.kind);
                    Assert.NotNull(state,skill.id+" / "+effect.kind); Assert.AreEqual(effect.BaseDuration(n),state.remaining);
                }
            }
            Assert.AreEqual(132,casts); // 45 cards × 3 lengths, except three no-ally guard casts.
        }
    }
}
