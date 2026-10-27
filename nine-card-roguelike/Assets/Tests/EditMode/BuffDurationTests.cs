using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Qiling.Tests
{
    public class BuffDurationTests
    {
        BattleCatalog data;
        [SetUp] public void Setup() { data=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); }
        BattleEngine SetupSpear(int mode, out string[] cards)
        {
            var w=data.weapons.Find(weapon=>weapon.id=="长矛");
            cards=new[]{w.cards[8]}.Concat(w.cards.Take(4)).ToArray();
            var profile=DebugLoadout.Initial(data,w.id);
            // Focus has no matching buff field; it cannot accidentally extend or strengthen this skill.
            Assert.IsNull(profile.Preset(data,cards,new[]{"专注"}));
            if(mode==1 || mode==2) Assert.IsNull(profile.SetTrait(data,cards[4],0,"延续"));
            if(mode==2) Assert.IsNull(profile.SetTrait(data,cards[4],1,"连锁反应"));
            if(mode==3) Assert.IsNull(profile.Preset(data,cards,new[]{"延续"}));
            var e=new BattleEngine(data,123,profile); e.Start(data.weapons.IndexOf(w),0);
            Turn(e,e.Player); return e;
        }
        static void Turn(BattleEngine e,Combatant actor)
        {
            e.ActiveId=null; e.Waiting.Clear(); foreach(var u in e.Units) u.progress=0;
            actor.progress=100; e.Advance(0); Assert.AreEqual(actor.id,e.ActiveId);
        }
        [TestCase(0,5,0)] [TestCase(1,8,.5)] [TestCase(2,10,1)] [TestCase(3,13,1.5)]
        public void RequestedDurationCasesMatchPreviewCastAndEveryOwnerTick(int mode,int expected,double extension)
        {
            var e=SetupSpear(mode,out var cards); var p=e.Plan(e.Player,cards);
            Assert.AreEqual(extension,p.extend,1e-9); Assert.AreEqual(5,p.skill.effects[0].BaseDuration(p.chain)); Assert.AreEqual(expected,p.Duration(5));
            long draws=e.Rng.Draws; string preview=e.Preview(cards,"player");
            StringAssert.Contains("＝"+expected+"回合",preview); Assert.AreEqual(draws,e.Rng.Draws);
            if(mode==2) { Assert.AreEqual(10,p.currentChains[4]); StringAssert.Contains("当前连锁10",p.DurationDescription(2)); }
            Assert.IsNull(e.Confirm(cards,"player"));
            Assert.AreEqual(expected,e.Player.State(EffectKind.AttackUp).remaining);
            Assert.AreEqual(expected,e.Player.State(EffectKind.SpeedPercent).remaining);
            Assert.AreEqual(.15,e.Player.State(EffectKind.SpeedPercent).value,1e-9);
            Assert.AreEqual(e.Player.panel.speed*1.15,e.Player.Stat(Attribute.Speed),1e-9);
            Assert.AreEqual(e.Player.panel.attack+5,e.Player.Stat(Attribute.Attack),1e-9);
            StringAssert.Contains("累计延长比例",string.Join("\n",e.Log));
            StringAssert.Contains("施加/刷新当次跳过",string.Join("\n",e.Log));
            for(int remaining=expected;remaining>0;remaining--)
            {
                Turn(e,e.Units[1]); e.EndAction(e.Units[1]);
                Assert.AreEqual(remaining,e.Player.State(EffectKind.SpeedPercent).remaining,"Other units must not tick this buff");
                Turn(e,e.Player); e.EndAction(e.Player);
                if(remaining>1) Assert.AreEqual(remaining-1,e.Player.State(EffectKind.SpeedPercent).remaining);
                else { Assert.IsNull(e.Player.State(EffectKind.SpeedPercent)); Assert.IsNull(e.Player.State(EffectKind.AttackUp)); }
            }
            Assert.AreEqual(e.Player.panel.speed,e.Player.Stat(Attribute.Speed),1e-9);
            StringAssert.Contains("持有者[player]行动结束",string.Join("\n",e.Log));
        }
        [Test] public void StrengtheningAffectsAttackPointsButOnlyAmplificationChangesSpeedPercent()
        {
            var e=SetupSpear(0,out var cards); var profile=e.Loadout;
            profile.SetTrait(data,cards[4],0,"强化"); Assert.IsNull(e.ApplyLoadout(profile));
            var p=e.Plan(e.Player,cards); Assert.AreEqual(0,p.extend); Assert.AreEqual(.5,p.fixedUp,1e-9); Assert.AreEqual(0,p.amplify);
            e.Execute(e.Player,p,"player"); Assert.AreEqual(7.5,e.Player.State(EffectKind.AttackUp).value,1e-9);
            Assert.AreEqual(.15,e.Player.State(EffectKind.SpeedPercent).value,1e-9); Assert.AreEqual(5,e.Player.State(EffectKind.AttackUp).remaining);
            var amplified=SetupSpear(0,out cards); profile=amplified.Loadout; profile.SetTrait(data,cards[4],0,"增幅"); amplified.ApplyLoadout(profile);
            p=amplified.Plan(amplified.Player,cards); Assert.AreEqual(.25,p.amplify,1e-9); Assert.AreEqual(0,p.fixedUp); Assert.AreEqual(2,p.Duration(2));
            amplified.Execute(amplified.Player,p,"player"); Assert.AreEqual(.1875,amplified.Player.State(EffectKind.SpeedPercent).value,1e-9);
            Assert.AreEqual(5,amplified.Player.State(EffectKind.AttackUp).value,1e-9);
            Assert.IsNull(amplified.Player.State(EffectKind.SpeedUp)); Assert.AreEqual(.5,data.Trait("追击").rate);
        }
        [Test] public void DuplicateExtensionsUseModifiedChainAndGainBeforeOneFinalCeiling()
        {
            var e=SetupSpear(0,out var cards); var profile=e.Loadout;
            profile.SetTrait(data,cards[4],0,"延续"); profile.SetTrait(data,cards[4],1,"延续"); profile.SetTrait(data,cards[4],2,"连锁反应"); e.ApplyLoadout(profile);
            var p=e.Plan(e.Player,cards); Assert.AreEqual(2,p.extend,1e-9); Assert.AreEqual(6,p.Duration(2)); Assert.AreEqual(2,p.durationSources.Count);
            profile.SetTrait(data,cards[4],1,"连锁蓄势"); profile.RemoveTrait(cards[4],2); e.ApplyLoadout(profile);
            p=e.Plan(e.Player,cards); Assert.AreEqual(.75,p.extend,1e-9); Assert.AreEqual(4,p.Duration(2));
            profile.Preset(data,cards,new[]{"专注"}); profile.SetTrait(data,cards[0],0,"延续"); profile.SetTrait(data,cards[1],0,"延续"); e.ApplyLoadout(profile);
            p=e.Plan(e.Player,cards); Assert.AreEqual(.3,p.extend,1e-9); Assert.AreEqual(3,p.Duration(2),"ceil is applied to .6 once, not to .2 and .4 separately");
        }
        [Test] public void EveryDefinedTimedBuffAndCounterUsesCalculatedDurationAndOwnerClock()
        {
            int checkedEffects=0;
            foreach(var w in data.weapons)
            foreach(var main in w.cards)
            {
                var skill=data.Skill(data.Card(main).skill);
                var timed=skill.effects.Where(f=>f.HasDuration && !f.negative && f.kind!=EffectKind.Guard).ToArray();
                if(timed.Length==0 && skill.pattern!=Pattern.Counter) continue;
                var cards=new[]{main}.Concat(w.cards.Where(c=>c!=main).Take(4)).ToArray();
                var profile=DebugLoadout.Initial(data,w.id); profile.Preset(data,cards,new[]{"延续"});
                var e=new BattleEngine(data,12,profile); e.Start(data.weapons.IndexOf(w),0); Turn(e,e.Player);
                var p=e.Plan(e.Player,cards); e.Execute(e.Player,p,"player"); e.EndAction(e.Player);
                foreach(var effect in timed)
                {
                    var state=e.Player.State(effect.kind); Assert.NotNull(state,skill.title+" / "+effect.kind);
                    int basis=effect.BaseDuration(cards.Length);
                    Assert.AreEqual(basis+(int)Math.Ceiling(basis*1.5),state.remaining,skill.title); checkedEffects++;
                }
                if(skill.pattern==Pattern.Counter) { Assert.AreEqual(3,e.Player.states.Single(s=>s.reaction!=null).remaining); checkedEffects++; }
                var before=e.Player.states.ToDictionary(s=>s.key,s=>s.remaining);
                Turn(e,e.Units[1]); e.EndAction(e.Units[1]);
                foreach(var state in e.Player.states) Assert.AreEqual(before[state.key],state.remaining);
                Turn(e,e.Player); e.EndAction(e.Player);
                foreach(var state in e.Player.states.Where(s=>s.remaining>0)) Assert.AreEqual(before[state.key]-1,state.remaining);
            }
            Assert.GreaterOrEqual(checkedEffects,15);
            TestContext.WriteLine("Checked timed buff / counter fields: "+checkedEffects);
        }
        [Test] public void AllowedRefreshKeepsLongerRemainderAndSkipsRefreshTurnWhileUndefinedRepeatStaysRestricted()
        {
            var w=data.weapons[4]; var cards=new[]{w.cards[8]}.Concat(w.cards.Take(4)).ToArray();
            var profile=DebugLoadout.Initial(data,w.id); profile.Preset(data,cards,new[]{"延续"});
            var e=new BattleEngine(data,1,profile); e.Start(4,0); Turn(e,e.Player);
            Assert.IsNull(e.Confirm(cards,"player")); var s=e.Player.State(EffectKind.ShieldBoost); Assert.AreEqual(13,s.remaining); double value=s.value;
            Turn(e,e.Player); e.EndAction(e.Player); Assert.AreEqual(12,s.remaining);
            Turn(e,e.Player); profile.Preset(data,cards,new[]{"专注"}); e.ApplyLoadout(profile);
            Assert.IsNull(e.Confirm(cards,"player")); Assert.AreEqual(12,s.remaining); Assert.AreEqual(value*2,s.value);
            Turn(e,e.Player); profile.Preset(data,cards,new[]{"延续"}); e.ApplyLoadout(profile);
            Assert.IsNull(e.Confirm(cards,"player")); Assert.AreEqual(13,s.remaining);
            var spear=SetupSpear(0,out cards); Assert.IsNull(spear.Confirm(cards,"player")); Turn(spear,spear.Player);
            StringAssert.Contains("重复幅度未确认",spear.Validate(spear.Player,cards,"player"));
        }
    }
}
