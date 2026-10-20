using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qiling.Tests
{
    public class DebugLoadoutTests
    {
        BattleCatalog data;
        [SetUp] public void Setup() { data = AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); Assert.NotNull(data); }
        DebugLoadout Profile(int weapon = 0)
        {
            var p = DebugLoadout.Initial(data, data.weapons[weapon].id); p.sequence = data.weapons[weapon].cards.Take(5).ToList(); return p;
        }
        BattleEngine Engine(DebugLoadout profile, int seed = 42)
        {
            var e = new BattleEngine(data, seed, profile); e.Start(data.weapons.FindIndex(w => w.id == profile.weaponId), 0); e.Player.progress = 100; e.Advance(0); return e;
        }
        [Test] public void CanUseDuplicateAndMismatchedTraitsWithoutChangingSkillOrCatalog()
        {
            var p = Profile(); string id = p.cards[0].cardId; var original = data.Card(id).traits.ToArray();
            Assert.IsNull(p.SetTrait(data, id, 0, "深化")); Assert.IsNull(p.SetTrait(data, id, 1, "深化")); Assert.IsNull(p.SetTrait(data, id, 2, "追击"));
            Assert.IsNull(p.Validate(data)); var e = Engine(p); Assert.AreEqual("连射", e.Card(id).skill); CollectionAssert.AreEqual(original, data.Card(id).traits);
            var plan = e.Plan(e.Player, p.sequence); Assert.IsTrue(plan.traitHints[0].Contains("类型不匹配")); Assert.IsTrue(plan.traitHints[1].Contains("类型不匹配"));
            Assert.AreEqual(3, e.Card(id).traits.Count);
        }
        [Test] public void EnforcesOneToThreeAndRejectsUnimplementedOrPendingWithoutMutation()
        {
            var p = Profile(); string id = p.cards[0].cardId; string original = JsonUtility.ToJson(p);
            Assert.NotNull(p.RemoveTrait(id, 0)); Assert.NotNull(p.SetTrait(data, id, 2, "锋锐")); Assert.NotNull(p.SetTrait(data, id, 0, "镜像")); Assert.NotNull(p.SetTrait(data, id, 0, "待定"));
            Assert.AreEqual(original, JsonUtility.ToJson(p));
            Assert.IsNull(p.SetTrait(data, id, 1, "追击")); Assert.IsNull(p.SetTrait(data, id, 2, "追击")); Assert.NotNull(p.SetTrait(data, id, 3, "追击"));
            Assert.IsNull(p.RemoveTrait(id, 1)); Assert.AreEqual(2, p.cards[0].traits.Count);
            p.cards[0].traits.Clear(); Assert.NotNull(p.Validate(data));
        }
        [Test] public void InitialSpearPlaceholderCanBeRestoredButNeverAssignedToOtherCards()
        {
            var p = Profile(3); Assert.IsNull(p.Validate(data)); Assert.NotNull(p.SetTrait(data, p.cards[0].cardId, 0, "待定"));
            Assert.IsTrue(data.traits.Any(t => t.id == "镜像" && !TraitSupport.Enabled(t))); Assert.IsTrue(data.traits.Any(t => t.id == "连锁反应" && !TraitSupport.Enabled(t)));
        }
        [Test] public void BatchKeepsOtherPositionsAndSequenceAndIsAtomicWhenSlotWouldHaveGap()
        {
            var p = Profile(); var order = p.sequence.ToArray();
            Assert.IsNull(p.Batch(data, p.sequence, "追击", 0, false)); Assert.IsNull(p.Batch(data, p.sequence, "锋锐", 1, false)); Assert.IsNull(p.Batch(data, p.sequence, "穿甲", 2, false));
            foreach (var id in order) CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, p.cards.Find(c => c.cardId == id).traits);
            CollectionAssert.AreEqual(order, p.sequence);
            Assert.IsNull(p.Batch(data, p.sequence, "壁障", 1, false)); foreach (var id in order) CollectionAssert.AreEqual(new[] { "追击", "壁障", "穿甲" }, p.cards.Find(c => c.cardId == id).traits);
            Assert.IsNull(p.Batch(data, p.sequence, "锋锐", 2, true)); foreach (var id in order) CollectionAssert.AreEqual(new[] { "锋锐" }, p.cards.Find(c => c.cardId == id).traits);
            string before = JsonUtility.ToJson(p); Assert.NotNull(p.Batch(data, p.sequence, "穿甲", 2, false)); Assert.AreEqual(before, JsonUtility.ToJson(p));
            Assert.NotNull(p.Batch(data, p.sequence.Take(4).ToList(), "追击", 0, false)); Assert.AreEqual(before, JsonUtility.ToJson(p));
        }
        [Test] public void WeaponsAreIndependentAndJsonRoundTripPreservesAllProfiles()
        {
            var s = new DebugSession(); var bow = s.ForWeapon(data, "猎弓").Copy(); bow.sequence = data.weapons[0].cards.Take(5).ToList(); bow.Preset(data, bow.sequence, "追击", "锋锐", "穿甲"); s.Save(bow);
            var sword = s.ForWeapon(data, "大剑"); Assert.AreEqual("锋锐", sword.cards[0].traits[0]);
            bow.cards[0].traits.Clear(); Assert.AreEqual(3, s.ForWeapon(data, "猎弓").cards[0].traits.Count);
            var copy = JsonUtility.FromJson<DebugSession>(JsonUtility.ToJson(s)); CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, copy.ForWeapon(data, "猎弓").cards[0].traits);
            CollectionAssert.AreEqual(data.weapons[0].cards.Take(5), copy.ForWeapon(data, "猎弓").sequence);
        }
        [TestCase(0, 54, 15)]
        [TestCase(1, 56, 0)]
        [TestCase(2, 133, 15)]
        [TestCase(3, 254, 15)]
        public void FourBuildsHaveIndependentGoldenDamageWithoutIncreasingRapidFire(int build, int expected, int pursuitCount)
        {
            string[][] variants = { new[] { "追击" }, new[] { "锋锐" }, new[] { "追击", "锋锐" }, new[] { "追击", "锋锐", "穿甲" } };
            var p = Profile(); Assert.IsNull(p.Preset(data, p.sequence, variants[build])); var e = Engine(p); e.Player.panel.crit = 0;
            var target = e.Units[1]; target.hp = target.panel.hp = 10000; target.panel.defense = 20;
            var plan = e.Plan(e.Player, p.sequence); Assert.AreEqual(7, plan.nativeSegments); Assert.AreEqual(.6, plan.nativeCoefficient); Assert.AreEqual(4.2, plan.nativeTotal, 1e-9); Assert.AreEqual(pursuitCount, plan.pursuits.Sum(g => g.count));
            if (build != 1) for (int i = 0; i < 5; i++) { Assert.AreEqual(i + 1, plan.pursuits[i].count); Assert.AreEqual(.63 * (.9 - i * .1), plan.pursuits[i].coefficient, 1e-9); }
            e.Execute(e.Player, plan, target.id); Assert.AreEqual(expected, e.TotalCalculatedDamage); Assert.AreEqual(expected, e.TotalHpDamage); Assert.AreEqual(7 + pursuitCount, e.Damage.Count); Assert.AreEqual(0, e.TotalShieldAbsorbed);
        }
        [Test] public void RepeatedBonusIsOnePoolSharedAcrossSevenNativeHitsOnly()
        {
            var p = Profile(); p.Preset(data, p.sequence, "追击", "壁障", "壁障"); var e = Engine(p); e.Player.panel.crit = 0;
            var target = e.Units[1]; target.hp = target.panel.hp = 10000;
            var plan = e.Plan(e.Player, p.sequence); Assert.AreEqual(90, plan.bonus, 1e-9); e.Execute(e.Player, plan, target.id);
            Assert.AreEqual(90, e.Damage.Where(d => d.label == "原生").Sum(d => d.bonus), 1e-9); Assert.IsTrue(e.Damage.Where(d => d.label.StartsWith("追击[")).All(d => d.bonus == 0 && d.resource == 0));
        }
        [Test] public void AppliedSnapshotDoesNotFollowDraftAndRefusesEnemyActionWindow()
        {
            var p = Profile(); p.Preset(data, p.sequence, "锋锐"); var e = Engine(p); p.Preset(data, p.sequence, "追击");
            Assert.AreEqual(0, e.Plan(e.Player, p.sequence).pursuits.Count); Assert.IsNull(e.ApplyLoadout(p)); Assert.AreEqual(5, e.Plan(e.Player, p.sequence).pursuits.Count);
            p.Preset(data, p.sequence, "穿甲"); Assert.AreEqual(5, e.Plan(e.Player, p.sequence).pursuits.Count);
            e.ActiveId = "enemy0"; Assert.NotNull(e.ApplyLoadout(p)); Assert.AreEqual(5, e.Plan(e.Player, p.sequence).pursuits.Count);
        }
        [Test] public void ExistingCounterRetainsCastTraitsAfterConfigurationApplied()
        {
            var p = Profile(1); p.sequence = new[] { data.weapons[1].cards[2] }.ToList();
            p.SetTrait(data, p.sequence[0], 0, "锋锐"); var e = Engine(p); e.Player.panel.crit = 0; Assert.IsNull(e.Confirm(p.sequence, "player"));
            var draft = p.Copy(); draft.SetTrait(data, p.sequence[0], 0, "追击"); Assert.IsNull(e.ApplyLoadout(draft));
            var attacker = e.Units[1]; attacker.hp = attacker.panel.hp = 10000;
            e.Execute(attacker, e.Plan(attacker, Array.Empty<string>(), data.Skill("弹跳撞击")), "player");
            Assert.AreEqual(1, e.Damage.Count(d => d.source == "player")); Assert.AreEqual(.1, e.Damage.Single(d => d.source == "player").damageUp, 1e-9);
        }
        [Test] public void DetailedTotalsSeparateCalculatedShieldLifeAndOverkillPerTarget()
        {
            var p = Profile(); var e = Engine(p); var front = e.Units[1]; front.shield = 3; front.hp = 2;
            e.ApplyDamage(front, 20, "人工核对"); e.ApplyDamage(e.Units[2], 7, "另一个目标");
            Assert.AreEqual(27, e.TotalCalculatedDamage); Assert.AreEqual(3, e.TotalShieldAbsorbed); Assert.AreEqual(9, e.TotalHpDamage); Assert.AreEqual(15, e.TotalOverkill);
            var first = e.TargetTotals.Single(t => t.target == "enemy0"); Assert.AreEqual(20, first.calculated); Assert.AreEqual(15, first.overkill);
            Assert.IsTrue(e.Damage.All(d => d.calculated == d.absorbed + d.hpLost + d.overkill));
        }
        [Test] public void FivePursuitPreviewNeverChangesRngRecordsOrReplayAndMatchesResolution()
        {
            var p = Profile(); p.Preset(data, p.sequence, "追击", "锋锐", "穿甲"); var e = Engine(p); e.Player.panel.crit = 0;
            int before = e.Log.Count; long draws = e.Rng.Draws; string preview = e.Preview(p.sequence, "enemy0"); Assert.AreEqual(preview, e.Preview(p.sequence, "enemy0"));
            Assert.AreEqual(before, e.Log.Count); Assert.AreEqual(draws, e.Rng.Draws); Assert.AreEqual(0, e.Damage.Count); Assert.AreEqual(0, e.Replay.steps.Count);
            Assert.IsTrue(preview.Contains("追击合计15段")); Assert.IsTrue(preview.Contains("过量伤害")); Assert.IsTrue(preview.Contains("有效防御"));
            Assert.IsNull(e.Confirm(p.sequence, "enemy0")); Assert.IsTrue(e.Log.Any(l => l.Contains("死亡转火"))); Assert.AreEqual(1, e.Replay.steps.Count);
            CollectionAssert.AreEqual(p.sequence, e.Replay.steps[0].cards); Assert.AreEqual("enemy0", e.Replay.steps[0].target);
        }
        [Test] public void SameSeedAndRecordedActionsReproduceNewRunsIncludingTransferredHits()
        {
            var p = Profile(); p.Preset(data, p.sequence, "追击", "锋锐", "穿甲");
            var first = Engine(p, 127); Assert.IsNull(first.Confirm(p.sequence, "enemy0"));
            var saved = JsonUtility.FromJson<BattleReplay>(JsonUtility.ToJson(first.Replay)); var second = Engine(saved.initial, saved.seed);
            Assert.IsNull(second.Confirm(saved.steps[0].cards, saved.steps[0].target)); Assert.AreEqual(JsonUtility.ToJson(new Records { rows = first.Damage.ToArray() }), JsonUtility.ToJson(new Records { rows = second.Damage.ToArray() }));
            Assert.AreEqual(first.Rng.Draws, second.Rng.Draws); Assert.AreEqual(100, second.Player.panel.hp);
        }
        [Serializable] class Records { public DamageRecord[] rows; }
    }
}
