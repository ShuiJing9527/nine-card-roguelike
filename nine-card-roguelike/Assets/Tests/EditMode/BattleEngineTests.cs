using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qiling.Tests
{
    public class BattleEngineTests
    {
        BattleCatalog data;
        [SetUp] public void Setup() { data = AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); Assert.NotNull(data); }
        BattleEngine New(int weapon = 0, int encounter = 0, int seed = 42)
        {
            var e = new BattleEngine(data, seed); e.Start(weapon, encounter); return e;
        }
        static void Turn(BattleEngine e)
        {
            e.ActiveId = null; e.Waiting.Clear(); foreach (var u in e.Units) u.progress = 0; e.Player.progress = 100; e.Advance(0); Assert.AreSame(e.Player, e.Active);
        }
        List<string> Cards(int w, params int[] numbers) => numbers.Select(n => data.weapons[w].cards[n - 1]).ToList();
        [Test] public void DataHasFiveCorrectPanelsAndNineDistinctEntitiesEach()
        {
            Assert.AreEqual(5, data.weapons.Count); Assert.AreEqual(45, data.cards.Count); Assert.AreEqual(440, data.monsters.Find(m => m.id == "彩虹史莱姆").panel.hp);
            foreach (var w in data.weapons)
            {
                Assert.AreEqual(9, w.cards.Distinct().Count()); Assert.AreEqual(3, w.cards.Count(id => data.Card(id).match == MatchType.Offense));
                Assert.AreEqual(3, w.cards.Count(id => data.Card(id).match == MatchType.Survival)); Assert.AreEqual(3, w.cards.Count(id => data.Card(id).match == MatchType.Status));
                foreach (var c in w.cards) { Assert.NotNull(data.Skill(data.Card(c).skill)); foreach (var t in data.Card(c).traits) Assert.NotNull(data.Trait(t)); }
            }
            Assert.AreEqual(55, data.skills.Count); Assert.AreEqual(25, data.traits.Count(TraitSupport.Enabled)); Assert.AreEqual(.5, data.Trait("追击").rate);
        }
        [Test] public void OneMainSkillMismatchOccupiesSlotAndTotalChainUsesAllCards()
        {
            var e = New(); var p = e.Plan(e.Player, Cards(0, 7, 2, 4, 8, 9));
            Assert.AreEqual("毒箭矢", p.skill.id); Assert.AreEqual(5, p.chain); Assert.AreEqual(.4, p.damageUp, 1e-9); // Universal enhancement still strengthens the poison arrow's direct hit.
            Assert.AreEqual(1, p.extraStacks); Assert.IsTrue(p.traitHints[1].Contains("类型不匹配")); Assert.IsTrue(p.traitHints[2].Contains("类型不匹配"));
            Assert.IsTrue(p.traitHints[3].Contains("无持续回合字段")); Assert.IsTrue(p.traitHints[4].Contains("有效"));
        }
        [Test] public void MainCardsOwnTraitUsesSlotOneAndActualSequenceLength()
        {
            var e = New(); var p = e.Plan(e.Player, Cards(0, 1)); Assert.AreEqual(1, p.chain); Assert.AreEqual(3, p.nativeSegments);
            Assert.AreEqual(1, p.pursuits[0].count); Assert.AreEqual(.5, p.pursuits[0].coefficient, 1e-9);
        }
        [Test] public void PursuitCoefficientIsIndependentOfSlotAndCount()
        {
            var e = New(); var p = e.Plan(e.Player, Cards(0, 2, 3, 4, 6, 1));
            Assert.AreEqual(5, p.pursuits[0].count); Assert.AreEqual(5, p.pursuits[0].slot); Assert.AreEqual(.5, p.pursuits[0].coefficient, 1e-9);
            Assert.AreEqual(.4, p.physicalPen, 1e-9); // 30% native plus 10% slot2.
        }
        [Test] public void PursuitIgnoresNativeCoefficientsAndDuplicateGroupsUseConstantCoefficient()
        {
            var profile=DebugLoadout.Initial(data,data.weapons[0].id);
            var cards=Cards(0,2,1,3,4,5); profile.Preset(data,cards,new[]{"锋锐"});
            profile.SetTrait(data,cards[2],0,"追击"); profile.SetTrait(data,cards[2],1,"追击");
            var e=new BattleEngine(data,42,profile); e.Start(0,0); var p=e.Plan(e.Player,cards);
            Assert.AreEqual(2,p.pursuits.Count);
            foreach(var group in p.pursuits)
            {
                Assert.AreEqual(3,group.count); Assert.AreEqual(.5,group.baseCoefficient);
                Assert.AreEqual(.5,group.CoefficientAt(0),1e-9);
                Assert.AreEqual(.5,group.CoefficientAt(1),1e-9);
                Assert.AreEqual(.5,group.CoefficientAt(2),1e-9);
            }
            var rapid=e.Plan(e.Player,Cards(0,1,2,3,4,5));
            Assert.AreEqual(7,rapid.nativeSegments); Assert.AreEqual(.5,rapid.pursuits[0].baseCoefficient,1e-9);
            var target=e.Units[1]; target.hp=target.panel.hp=10000; p.crit=0;
            e.Execute(e.Player,p,target.id);
            var hits=e.Damage.Where(d=>d.label.StartsWith("追击[")).ToArray();
            CollectionAssert.AreEqual(new[]{.5,.5,.5,.5,.5,.5}.Select(v=>Math.Round(v,6)),hits.Select(d=>Math.Round(d.coefficient,6)));
        }
        [Test] public void PursuitConstantBaseIsIndependentOfBlockCriticalDefenseAndShield()
        {
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); var cards=Cards(0,2,1,3);
            foreach(var card in cards) Assert.IsNull(profile.SetTrait(data,card,0,"锋锐")); Assert.IsNull(profile.SetTrait(data,cards[2],0,"追击"));
            var e=new BattleEngine(data,42,profile); e.Start(0,0); var p=e.Plan(e.Player,cards);
            p.damageUp=0; p.physicalPen=0; p.crit=1; p.critDamage=2;
            var target=e.Units[1]; target.hp=target.panel.hp=10000; target.panel.defense=10; target.shield=3;
            target.states.Add(new BattleState { kind=EffectKind.Block,value=2 });
            e.Execute(e.Player,p,target.id);
            var hits=e.Damage.Where(d=>d.label.StartsWith("追击[")).ToArray();
            Assert.AreEqual(3,hits.Length); Assert.IsTrue(hits[0].blocked);
            // The blocked native and first pursuit do not affect either later 50% segment.
            Assert.AreEqual(.5,hits[1].coefficient,1e-9); Assert.AreEqual(.5,hits[2].coefficient,1e-9);
            Assert.IsTrue(hits[1].critical && hits[2].critical);
            Assert.AreEqual(8,hits[1].calculated); Assert.AreEqual(3,hits[1].absorbed); Assert.AreEqual(5,hits[1].hpLost);
            Assert.AreEqual(8,hits[2].calculated); Assert.AreEqual(0,hits[2].absorbed);
        }
        [Test] public void PursuitRetargetReadsNewDefenseWithSameCoefficient()
        {
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); var cards=Cards(0,2,1,3);
            foreach(var card in cards) Assert.IsNull(profile.SetTrait(data,card,0,"锋锐")); Assert.IsNull(profile.SetTrait(data,cards[2],0,"追击"));
            var e=new BattleEngine(data,42,profile); e.Start(0,0); var p=e.Plan(e.Player,cards); p.crit=0; p.damageUp=0; p.physicalPen=0;
            var front=e.Units[1]; front.panel.defense=0; front.hp=55; // Native 49.5 rounds to 50; first pursuit kills front.
            e.Execute(e.Player,p,front.id); var hits=e.Damage.Where(d=>d.label.StartsWith("追击[")).ToArray();
            CollectionAssert.AreEqual(new[]{"enemy0","enemy1","enemy1"},hits.Select(d=>d.target));
            Assert.AreEqual(.5,hits[1].coefficient,1e-9); Assert.AreEqual(15,hits[1].defense);
            Assert.AreEqual(3,hits[1].calculated); Assert.AreEqual(3,hits[2].calculated);
        }
        [Test] public void FivePursuitsAreFifteenSegmentsAnd750PercentRegardlessOfMainSkill()
        {
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); var cards=Cards(0,1,2,3,4,5);
            Assert.IsNull(profile.Preset(data,cards,new[]{"追击"})); var e=new BattleEngine(data,42,profile); e.Start(0,0);
            foreach(var order in new[]{cards,Cards(0,2,1,3,4,5)})
            {
                var p=e.Plan(e.Player,order); Assert.AreEqual(15,p.pursuits.Sum(g=>g.count));
                Assert.AreEqual(7.5,p.pursuits.Sum(g=>g.count*g.coefficient),1e-9);
                CollectionAssert.AreEqual(new[]{1,2,3,4,5},p.pursuits.Select(g=>g.currentChain));
                for(int i=0;i<5;i++) Assert.AreEqual(.5,p.pursuits[i].coefficient,1e-9);
                var modified=p.pursuits[2]; modified.currentChain=8; modified.count=8;
                Assert.AreEqual(3,modified.slot);
                Assert.AreEqual(.5,modified.CoefficientAt(7),1e-9);
            }
        }
        [Test] public void PursuitReadsMainSkillAttributeIncludingDefenseAndCombinedAttack()
        {
            foreach(var attribute in new[]{Attribute.Attack,Attribute.SpecialAttack,Attribute.Defense,Attribute.BothAttack})
            {
                var card=data.cards.First(c=>c.match==MatchType.Offense && data.Skill(c.skill).attribute==attribute && data.Skill(c.skill).coefficient.basis>0);
                int weapon=data.weapons.FindIndex(w=>w.cards.Contains(card.id));
                var cards=new[]{card.id}.Concat(data.weapons[weapon].cards.Where(id=>id!=card.id).Take(4)).ToList();
                var profile=DebugLoadout.Initial(data,data.weapons[weapon].id); Assert.IsNull(profile.Preset(data,cards,new[]{"追击"}));
                var e=new BattleEngine(data,42,profile); e.Start(weapon,0); foreach(var unit in e.Units.Where(u=>!u.player)) unit.hp=unit.panel.hp=100000;
                var p=e.Plan(e.Player,cards); p.crit=0; e.Execute(e.Player,p,"enemy0");
                var hit=e.Damage.First(d=>d.label.StartsWith("追击["));
                Assert.AreEqual(.5,hit.coefficient,1e-9); Assert.AreEqual(e.Player.Stat(attribute),hit.attackAttribute,1e-9);
                Assert.AreEqual(BattleMath.Quote(data.rules,e.Player,e.Units.First(u=>u.id==hit.target),p,.5,0,0,false).damage,hit.calculated);
            }
        }
        [Test] public void CounterPursuitsUseFixedCoefficientsAndRecordSkillInheritance()
        {
            var cards=Cards(1,3,1,2,4,5); var profile=DebugLoadout.Initial(data,data.weapons[1].id);
            Assert.IsNull(profile.Preset(data,cards,new[]{"追击"})); var e=new BattleEngine(data,42,profile); e.Start(1,0);
            e.Player.panel.crit=0; e.Units[1].hp=e.Units[1].panel.hp=100000; Turn(e); Assert.IsNull(e.Confirm(cards,"player"));
            e.Execute(e.Units[1],e.Plan(e.Units[1],Array.Empty<string>(),data.Skill("弹跳撞击")),"player");
            var hits=e.Damage.Where(d=>d.source=="player" && d.label.StartsWith("反击追击[")).ToArray();
            Assert.AreEqual(15,hits.Length); Assert.AreEqual(7.5,hits.Sum(d=>d.coefficient),1e-9);
            Assert.IsTrue(hits.All(d=>d.bonus==0 && d.resource==0));
            Assert.IsTrue(e.Log.Any(l=>l.Contains("追击继承："))); Assert.IsTrue(e.Log.Any(l=>l.Contains("当前连锁5") && l.Contains("每段固定系数50.00")));
        }
        [Test] public void BonusIsSingleShareAcrossNativeSegmentsAndNotCoefficientPoints()
        {
            var e = New(); var p = e.Plan(e.Player, Cards(0, 1, 2, 3, 4, 6));
            Assert.AreEqual(7, p.nativeSegments); Assert.AreEqual(3 * 4 + 8 * 5 * .6, p.bonus, 1e-9);
            Turn(e); e.Execute(e.Player, p, "enemy0");
            Assert.IsTrue(e.Log.Any(x => x.Contains("原生") && x.Contains("加值5.14")));
            Assert.IsTrue(e.Log.Any(x => x.Contains("追击[") && x.Contains("加值0.00")));
        }
        [Test] public void SweepSplitsBonusAcrossActualNativeTargets()
        {
            var e = New(); var p = e.Plan(e.Player, Cards(0, 3, 4)); Assert.AreEqual(6, p.bonus, 1e-9);
            e.Execute(e.Player, p, "enemy0"); Assert.AreEqual(2, e.Log.Count(x => x.Contains("原生 →") && x.Contains("加值3.00")));
        }
        [Test] public void HalfUpRoundAndDefenseFormulaArePerSegment()
        {
            Assert.AreEqual(3, BattleMath.Round(2.5)); Assert.AreEqual(2, BattleMath.Round(2.49));
            var e = New(); var p = e.Plan(e.Player, Cards(0, 2)); p.damageUp = 0; p.physicalPen = 0; p.critDamage = 1.5;
            var target = e.Units[1]; target.panel.defense = 10;
            Assert.AreEqual(26, BattleMath.Quote(data.rules, e.Player, target, p, 3.5, 0, 0, false).damage);
            Assert.AreEqual(39, BattleMath.Quote(data.rules, e.Player, target, p, 3.5, 0, 0, true).damage);
            target.panel.defense = 1e9; Assert.AreEqual(1, BattleMath.Quote(data.rules, e.Player, target, p, .1, 0, 0, false).damage);
        }
        [Test] public void MeleeFrontRestrictionAndRangedBackSelection()
        {
            var e = New(1); var melee = data.Skill("劈山斩"); Assert.AreEqual("enemy0", e.LegalTargets(e.Player, melee).Single().id);
            Assert.AreEqual(2, e.LegalTargets(e.Player, data.Skill("破风箭")).Count); e.Units[1].hp = 0;
            Assert.AreEqual("enemy1", e.LegalTargets(e.Player, melee).Single().id);
        }
        [Test] public void DeadTargetTransfersMeleeSegmentsToBackAndReadsNewDefense()
        {
            var e = New(3); e.Units[1].hp = 1; e.Player.panel.crit = 0;
            e.Execute(e.Player, e.Plan(e.Player, Cards(3, 2, 1, 3, 4, 5)), "enemy0");
            Assert.IsTrue(e.Log.Any(x => x.Contains("死亡转火"))); Assert.IsTrue(e.Log.Any(x => x.Contains("原生 → 彩虹史莱姆") && x.Contains("防御15.00")));
            Assert.Less(e.Units[2].hp, 440);
        }
        [Test] public void FlyingShieldStopsWithOneLegalTarget()
        {
            var e = New(4, 1); var p = e.Plan(e.Player, Cards(4, 2, 1, 3, 4, 6));
            Assert.AreEqual(1, p.nativeSegments); Assert.AreEqual(1.5, p.nativeTotal); e.Execute(e.Player, p, "enemy0"); Assert.IsFalse(e.Log.Any(x => x.Contains("弹射换目标")));
        }
        [Test] public void FlyingShieldAlwaysSwitchesAndCanReturnToEarlierTarget()
        {
            var e = New(4); e.Units[1].hp = 10000; e.Units[2].hp = 10000;
            var p = e.Plan(e.Player, Cards(4, 2, 1, 3, 4, 6)); Assert.AreEqual(6.75, p.nativeTotal, 1e-9);
            e.Execute(e.Player, p, "enemy0"); var swaps = e.Log.Where(x => x.Contains("弹射换目标")).ToList();
            Assert.AreEqual(5, swaps.Count); Assert.IsTrue(swaps[0].Contains("彩虹")); Assert.IsTrue(swaps[1].Contains("普通"));
        }
        [Test] public void ShieldBreakConsumesOnceAndNoPursuitResourceBonus()
        {
            var e = New(4); e.Player.shield = 72; var p = e.Plan(e.Player, Cards(4, 3, 1, 2, 4, 6));
            e.Execute(e.Player, p, "enemy0"); Assert.AreEqual(36, e.Player.shield); Assert.AreEqual(1, e.Log.Count(x => x.Contains("一次消耗36")));
            Assert.IsTrue(e.Log.Any(x => x.Contains("资源108.00")));
        }
        [Test] public void SimultaneousReadyOrderReproducibleAndWaitingPushRemovesQualification()
        {
            var a = New(seed: 9); var b = New(seed: 9);
            foreach (var u in a.Units) u.progress = 100; foreach (var u in b.Units) u.progress = 100;
            a.Advance(0); b.Advance(0); Assert.AreEqual(a.ActiveId, b.ActiveId); CollectionAssert.AreEqual(a.Waiting, b.Waiting);
            var waiting = a.Units.Find(x => x.id == a.Waiting[0]); a.Push(waiting, -20); Assert.IsFalse(a.Waiting.Contains(waiting.id)); Assert.AreEqual(80, waiting.progress);
        }
        [Test] public void TimelinePausesWhilePlayerComposesAndPreservesOtherProgress()
        {
            var e = New(); Turn(e); double t = e.NaturalTime; var values = e.Units.Select(x => x.progress).ToArray();
            e.Advance(1000); Assert.AreEqual(t, e.NaturalTime); CollectionAssert.AreEqual(values, e.Units.Select(x => x.progress).ToArray());
        }
        [Test] public void SpeedNaturallyDeterminesFirstActor()
        {
            var e = New(); e.Advance(100); Assert.AreEqual("player", e.ActiveId); Assert.AreEqual(12.5, e.NaturalTime, 1e-9); Assert.AreEqual(75, e.Units[1].progress, 1e-9);
        }
        [Test] public void PoisonReadsInstantStacksAbsorbsShieldThenLosesOne()
        {
            var e = New(); e.Player.shield = 5; e.Player.states.Add(new BattleState { kind = EffectKind.Poison, value = 4, negative = true });
            Turn(e); e.EndAction(e.Player); Assert.AreEqual(97, e.Player.hp); Assert.AreEqual(0, e.Player.shield); Assert.AreEqual(3, e.Player.State(EffectKind.Poison).value);
        }
        [Test] public void SelfAppliedDurationSkipsCurrentActionAndOtherUnitsDoNotTickIt()
        {
            var e = New(); Turn(e); e.Confirm(Cards(0, 5), "player"); var state = e.Player.State(EffectKind.Evade); Assert.AreEqual(1, state.remaining);
            e.ActiveId = "enemy0"; e.ActionSerial++; e.EndAction(e.Units[1]); Assert.AreEqual(1, state.remaining);
            Turn(e); e.EndAction(e.Player); Assert.IsNull(e.Player.State(EffectKind.Evade));
        }
        [Test] public void BurnTicksOnTargetAndExpiresAfterTwoTargetActions()
        {
            var e = New(0, 2); var monster = e.Units[1]; monster.phase = 1; e.ActiveId = monster.id; e.ActionSerial++;
            e.EnemyAction(); Assert.AreEqual(2, e.Player.State(EffectKind.Burn).remaining);
            int hp = e.Player.hp; Turn(e); e.EndAction(e.Player); Assert.AreEqual(hp - 5, e.Player.hp); Assert.AreEqual(1, e.Player.State(EffectKind.Burn).remaining);
            Turn(e); e.EndAction(e.Player); Assert.IsNull(e.Player.State(EffectKind.Burn));
        }
        [Test] public void StackableWeaknessAddsAndRefreshesInsteadOfMultiplyingDuration()
        {
            var e = New(1); var target = e.Units[1]; var p = e.Plan(e.Player, Cards(1, 8));
            e.Execute(e.Player, p, target.id); e.Execute(e.Player, p, target.id); Assert.AreEqual(4, target.State(EffectKind.Weakness).value); Assert.AreEqual(2, target.State(EffectKind.Weakness).remaining);
            Assert.AreEqual(4.8, target.Stat(Attribute.Attack), 1e-9);
        }
        [Test] public void BlockIsPerSegmentRangedOnlyAndPreventsPoisonOnHit()
        {
            var e = New(); var target = e.Units[1]; target.states.Add(new BattleState { kind = EffectKind.Block, value = 1, rangedOnly = true });
            e.Execute(e.Player, e.Plan(e.Player, Cards(0, 7)), target.id); Assert.AreEqual(60, target.hp); Assert.IsNull(target.State(EffectKind.Poison)); Assert.IsNull(target.State(EffectKind.Block));
            target.states.Add(new BattleState { kind = EffectKind.Block, value = 5, rangedOnly = true });
            var p = e.Plan(e.Player, Array.Empty<string>(), data.Skill("弹跳撞击")); e.Execute(e.Player, p, target.id); Assert.AreEqual(5, target.State(EffectKind.Block).value); Assert.Less(target.hp, 60);
        }
        [Test] public void DodgeHappensBeforeBlockAndConsumesNeitherBlockNorShield()
        {
            int seed = Enumerable.Range(0, 100).First(s => new BattleRandom(s).Next() < .75);
            var e = New(seed: seed); var target = e.Units[1]; target.shield = 20;
            target.states.Add(new BattleState { kind = EffectKind.Evade, value = 1 }); target.states.Add(new BattleState { kind = EffectKind.Block, value = 5 });
            e.Execute(e.Player, e.Plan(e.Player, Cards(0, 7)), target.id);
            Assert.AreEqual(1, e.Dodges); Assert.AreEqual(5, target.State(EffectKind.Block).value); Assert.AreEqual(20, target.shield); Assert.IsNull(target.State(EffectKind.Poison));
        }
        [Test] public void SevenNativeSegmentsConsumeFiveBlocksAndEachRemainingSegmentRounds()
        {
            var e = New(); var target = e.Units[1]; target.hp = target.panel.hp = 1000; e.Player.panel.crit = 0;
            target.states.Add(new BattleState { kind = EffectKind.Block, value = 5 }); e.Execute(e.Player, e.Plan(e.Player, Cards(0, 1, 2, 3, 4, 6)), target.id);
            Assert.AreEqual(5, e.Blocks); Assert.AreEqual(2, e.Log.Count(x => x.Contains("原生 →") && x.Contains("系数"))); Assert.IsNull(target.State(EffectKind.Block));
        }
        [Test] public void AmplificationIsRelativeAndDoesNotIncreaseShieldCoefficientsOrNegativeCosts()
        {
            var e = New(); var dodge = e.Plan(e.Player, Cards(0, 5)); Assert.AreEqual(.05, dodge.amplify, 1e-9);
            Turn(e); e.Confirm(Cards(0, 5), "player"); Assert.AreEqual(.105, e.Player.State(EffectKind.Evade).value, 1e-9);
            var shield = e.Plan(e.Player, Cards(0, 4, 5)); Assert.AreEqual(0, shield.amplify); Assert.IsTrue(shield.traitHints[1].Contains("无概率"));
        }
        [Test] public void ReflectionReadsAbsorbedShieldPlusActualHpAndCannotReflectRecursively()
        {
            var e = New(); var target = e.Units[1]; target.shield = 3; e.Player.panel.crit = 0;
            target.states.Add(new BattleState { kind = EffectKind.Reflect, value = .5 }); e.Player.states.Add(new BattleState { kind = EffectKind.Reflect, value = 1 });
            int before = e.Player.hp; var p = e.Plan(e.Player, Cards(0, 7)); var quote = BattleMath.Quote(data.rules, e.Player, target, p, .5, 0, 0, false);
            e.Execute(e.Player, p, target.id); Assert.AreEqual(before - BattleMath.Round(quote.damage * .5), e.Player.hp); Assert.AreEqual(1, e.Log.Count(x => x.Contains("棘甲反伤（")));
        }
        [Test] public void CounterOnlyOncePerCastAndRespectsFrontRow()
        {
            var e = New(1); Turn(e); e.Confirm(Cards(1, 3), "player");
            var boss = e.Units[2]; e.Execute(boss, e.Plan(boss, Array.Empty<string>(), data.Skill("虹光喷射")), "player"); Assert.IsFalse(e.Log.Any(x => x.Contains("发动反击"))); Assert.IsTrue(e.Log.Any(x => x.Contains("不符合近战")));
            var front = e.Units[1]; e.Execute(front, e.Plan(front, Array.Empty<string>(), data.Skill("弹跳撞击")), "player"); Assert.AreEqual(1, e.Log.Count(x => x.Contains("发动反击")));
        }
        [Test] public void SelfPullSurvivesActionResetAndAlreadyStartedActionIsNotCancelledByPush()
        {
            var e = New(); Turn(e); e.Push(e.Player, 30); e.Push(e.Player, -20); Assert.AreSame(e.Player, e.Active);
            e.EndAction(e.Player); Assert.AreEqual(30, e.Player.progress);
        }
        [Test] public void BossCyclesDefensesAndIntentsAfterCompleteActions()
        {
            var e = New(0, 1); var boss = e.Units[1];
            Assert.AreEqual(15, boss.Stat(Attribute.Defense));
            e.ActiveId = boss.id; e.ActionSerial++; e.EnemyAction(); Assert.AreEqual(1, boss.phase); Assert.AreEqual(25, boss.Stat(Attribute.Defense)); Assert.AreEqual(5, boss.Stat(Attribute.SpecialDefense)); Assert.IsTrue(e.Intent(boss).Contains("虹光凝聚"));
            int before = e.Player.hp; e.ActiveId = boss.id; e.ActionSerial++; e.EnemyAction(); Assert.AreEqual(before, e.Player.hp); Assert.AreEqual(2, boss.phase); Assert.AreEqual(5, boss.Stat(Attribute.Defense));
            e.ActiveId = boss.id; e.ActionSerial++; e.EnemyAction(); Assert.AreEqual(0, boss.phase); Assert.AreEqual(15, boss.Stat(Attribute.Defense)); Assert.Less(e.Player.hp, before);
        }
        [Test] public void GuardCannotSelectSelfButItsTraitWorksInLaterSlot()
        {
            var e = New(4); Turn(e); Assert.IsTrue(e.Validate(e.Player, Cards(4, 5), "player").Contains("没有另一名"));
            var p = e.Plan(e.Player, Cards(4, 7, 5)); Assert.IsTrue(p.traitHints[1].Contains("无概率")); // fixed defense is not a percentage.
            p = e.Plan(e.Player, Cards(4, 5, 1)); Assert.AreEqual(.05, p.amplify, 1e-9);
        }
        [Test] public void PreviewUsesSameDamageButNeverMutatesBattleOrRandom()
        {
            var e = New(); Turn(e); var cards = Cards(0, 2, 1, 3, 4, 6); string before = string.Join(";", e.Units.Select(x => x.hp + "," + x.shield)); long draws = e.Rng.Draws; int logs = e.Log.Count;
            string a = e.Preview(cards, "enemy0"), b = e.Preview(cards, "enemy0"); Assert.AreEqual(a, b); Assert.AreEqual(draws, e.Rng.Draws); Assert.AreEqual(logs, e.Log.Count); Assert.AreEqual(before, string.Join(";", e.Units.Select(x => x.hp + "," + x.shield)));
            Assert.IsTrue(a.Contains("不保证")); Assert.IsTrue(a.Contains("示例扣血"));
        }
        [Test] public void DuplicateAndCrossWeaponEntitiesRejected()
        {
            var e = New(); Turn(e); Assert.IsTrue(e.Validate(e.Player, Cards(0, 1, 1), "enemy0").Contains("不能重复"));
            Assert.IsTrue(e.Validate(e.Player, Cards(1, 1), "enemy0").Contains("其他武器"));
        }
        [Test] public void DurationExtensionSumsRelativeThenCeilsOnce()
        {
            var p = new ActionPlan { extend = 1.5 }; Assert.AreEqual(3, p.Duration(1)); Assert.AreEqual(5, p.Duration(2));
        }
        [Test] public void FreezeResilienceAndSourceNextTurnUnfreezePreserveProgress()
        {
            var e = New(); var u = e.Units[2]; u.progress = 65;
            u.states.Add(new BattleState { kind = EffectKind.Freeze, source = "player", appliedAction = 0 });
            e.ActionSerial = 1; e.EndAction(e.Units[1]); Assert.AreEqual(1, u.State(EffectKind.Freeze).resilience);
            Turn(e); Assert.IsNull(u.State(EffectKind.Freeze)); Assert.IsTrue(u.freezeProtection); Assert.AreEqual(0, u.progress); // Turn helper reset progress deliberately.
            u.progress = 65; u.freezeProtection = false; u.states.Add(new BattleState { kind = EffectKind.Freeze, source = "missing", appliedAction = 0 });
            for (int i = 0; i < 3; i++) { e.ActionSerial++; e.EndAction(e.Player); }
            Assert.IsNull(u.State(EffectKind.Freeze)); Assert.AreEqual(65, u.progress); Assert.IsTrue(u.freezeProtection);
        }
        [Test] public void EveryWeaponCanFinishAllFourEncountersWithoutInfiniteLoop()
        {
            for (int w = 0; w < 5; w++) for (int encounter = 0; encounter < 4; encounter++)
            {
                var e = New(w, encounter, 20261020); int actions = 0;
                while (!e.Finished && actions++ < 500)
                {
                    if (e.Active == null) e.Advance(1000);
                    if (e.Active == null) Assert.Fail("Timeline stalled");
                    if (e.Active.player)
                    {
                        var cards = Cards(w, 1, 2, 3, 4, 5); if (w == 0) cards = Cards(w, 2, 1, 3, 4, 6);
                        string target = e.LegalTargets(e.Player, data.Skill(data.Card(cards[0]).skill))[0].id;
                        Assert.IsNull(e.Confirm(cards, target));
                    }
                    else e.EnemyAction();
                }
                Assert.IsTrue(e.Finished, $"weapon={w} encounter={encounter}"); Assert.Less(actions, 500);
            }
        }
    }
}
