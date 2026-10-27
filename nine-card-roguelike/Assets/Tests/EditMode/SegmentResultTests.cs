using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
namespace Qiling.Tests
{
    public class SegmentResultTests
    {
        BattleCatalog data;
        [SetUp] public void Setup() { data=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); }
        [Test] public void MixedResultsCarryIndependentHpShieldAndImmutableStateFrames()
        {
            var cards=data.weapons[0].cards.Take(5).ToArray(); var profile=DebugLoadout.Initial(data,data.weapons[0].id); profile.Preset(data,cards,new[]{"追击","锋锐","穿甲"});
            var e=new BattleEngine(data,1,profile); e.Start(0,0); var target=e.Units[1]; target.hp=target.panel.hp=10000; target.shield=10;
            target.states.Add(new BattleState { kind=EffectKind.Evade,value=.25 }); target.states.Add(new BattleState { kind=EffectKind.Block,value=1 }); e.Player.panel.crit=.5;
            e.Player.progress=100; e.Advance(0); Assert.IsNull(e.Confirm(cards,target.id));
            var hits=e.VisualEvents.Where(v=>v.node==VisualNode.Impact).ToArray(); Assert.AreEqual(e.Damage.Count,hits.Length);
            Assert.IsTrue(hits.Any(v=>v.dodged)); Assert.IsTrue(hits.Any(v=>v.blocked)); Assert.IsTrue(hits.Any(v=>v.critical)); Assert.IsTrue(hits.Any(v=>!v.dodged && !v.blocked && !v.critical));
            Assert.IsTrue(hits.Any(v=>v.damage>0 && v.shield>0)); Assert.IsTrue(hits.Any(v=>v.damage>0 && v.shield==0));
            int hp=10000,shield=10;
            foreach(var hit in hits)
            {
                var d=e.Damage[hit.damageIndex]; Assert.AreEqual(d.target,hit.target); Assert.AreEqual(d.calculated,hit.calculated); Assert.AreEqual(d.hpLost,hit.damage); Assert.AreEqual(d.absorbed,hit.shield); Assert.AreEqual(d.overkill,hit.overkill);
                hp-=d.hpLost; shield-=d.absorbed; var frame=hit.units.Single(u=>u.id==target.id); Assert.AreEqual(hp,frame.hp); Assert.AreEqual(shield,frame.shield);
            }
            Assert.IsTrue(hits.First().units.Single(u=>u.id==target.id).hp>target.hp);
            int saved=hits.First().units.Single(u=>u.id==target.id).hp; target.hp=0; target.states[0].value=0; Assert.AreEqual(saved,hits.First().units.Single(u=>u.id==target.id).hp);
        }
        [Test] public void LethalRetargetUsesRealPerSegmentResultsAndDoesNotMoveOverkill()
        {
            var e=new BattleEngine(data,42); e.Start(0,0); e.Units[1].hp=1; e.Units[2].hp=e.Units[2].panel.hp=10000;
            var cards=data.weapons[0].cards.Take(5).ToArray(); e.Player.progress=100; e.Advance(0); e.Confirm(cards,e.Units[1].id);
            var hits=e.VisualEvents.Where(v=>v.node==VisualNode.Impact).ToArray(); Assert.AreEqual(e.Units[1].id,hits[0].target); Assert.AreEqual(1,hits[0].damage); Assert.Greater(hits[0].overkill,0);
            Assert.AreEqual(10000,hits[0].units.Single(u=>u.id==e.Units[2].id).hp); Assert.AreEqual(e.Units[2].id,hits[1].target);
            Assert.AreEqual(e.Damage.Count,hits.Length); Assert.AreEqual(e.Units[2].hp,hits.Last().units.Single(u=>u.id==e.Units[2].id).hp);
        }
        [Test] public void BouncePathStartsAtPreviousActualTargetAndChangesTargetEveryTime()
        {
            int weapon=data.weapons.FindIndex(w=>w.cards.Any(id=>data.Skill(data.Card(id).skill).pattern==Pattern.Bounce)); Assert.GreaterOrEqual(weapon,0);
            var e=new BattleEngine(data,12); e.Start(weapon,0); foreach(var u in e.Units.Where(u=>!u.player)) u.hp=u.panel.hp=10000;
            var first=data.weapons[weapon].cards.First(id=>data.Skill(data.Card(id).skill).pattern==Pattern.Bounce); var cards=new[]{first}.Concat(data.weapons[weapon].cards.Where(id=>id!=first).Take(4)).ToArray();
            e.Player.progress=100; e.Advance(0); e.Confirm(cards,e.Units[1].id);
            var launch=e.VisualEvents.Where(v=>v.node==VisualNode.Launch && (v.segment=="原生" || v.segment=="弹射")).ToArray(); Assert.Greater(launch.Length,1); Assert.AreEqual(e.Player.id,launch[0].flightFrom);
            for(int i=1;i<launch.Length;i++) { Assert.AreEqual(launch[i-1].target,launch[i].flightFrom); Assert.AreNotEqual(launch[i-1].target,launch[i].target); }
            var damage=e.VisualEvents.Where(v=>v.node==VisualNode.Impact).ToArray(); CollectionAssert.AreEqual(e.Damage.Select(d=>d.target),damage.Select(d=>d.target));
        }
        [Test] public void HealingAndShieldHaveDedicatedActualValueEvents()
        {
            var heal=data.skills.First(s=>s.target==TargetMode.Self && s.effects.Any(x=>x.kind==EffectKind.Heal));
            var shield=data.skills.First(s=>s.target==TargetMode.Self && s.effects.Any(x=>x.kind==EffectKind.Shield));
            var e=new BattleEngine(data,2); e.Start(0,0); e.Player.hp=1;
            e.Execute(e.Player,e.Plan(e.Player,Array.Empty<string>(),heal),e.Player.id);
            var h=e.VisualEvents.Single(v=>v.node==VisualNode.Heal); Assert.AreEqual(e.Player.hp-1,h.amount); Assert.AreEqual(e.Player.hp,h.units.Single(u=>u.id==e.Player.id).hp);
            e.Execute(e.Player,e.Plan(e.Player,Array.Empty<string>(),shield),e.Player.id);
            var s=e.VisualEvents.Single(v=>v.node==VisualNode.Shield); Assert.AreEqual(e.Player.shield,s.amount); Assert.AreEqual(e.Player.shield,s.units.Single(u=>u.id==e.Player.id).shield);
        }
        [Test] public void PresentationGroupsSeparateNativeAndEveryPursuitWithoutChangingDamageRecords()
        {
            var cards=data.weapons[0].cards.Take(5).ToArray(); var profile=DebugLoadout.Initial(data,data.weapons[0].id); profile.Preset(data,cards,new[]{"追击"});
            var e=new BattleEngine(data,42,profile); e.Start(0,0); e.Units[1].hp=e.Units[1].panel.hp=10000; e.Player.progress=100; e.Advance(0); e.Confirm(cards,"enemy0");
            var launches=e.VisualEvents.Where(v=>v.node==VisualNode.Launch).ToArray();
            CollectionAssert.AreEqual(new[]{7,1,2,3,4,5},launches.GroupBy(v=>v.playbackGroup).Select(g=>g.Count()));
            Assert.IsTrue(launches.All(v=>v.playbackGroup>0));
            foreach(var launch in launches)
            {
                var impact=e.VisualEvents.Skip(launch.eventIndex+1).First(v=>v.node==VisualNode.Impact);
                Assert.AreEqual(launch.playbackGroup,impact.playbackGroup); Assert.AreEqual(launch.target,impact.target);
            }
        }
        [Test] public void StabNativeAndPursuitEachHaveTheirOwnDamageIndex()
        {
            int weapon=data.weapons.FindIndex(w=>w.cards.Any(id=>data.Card(id).skill=="连环突刺")); Assert.GreaterOrEqual(weapon,0);
            string main=data.weapons[weapon].cards.First(id=>data.Card(id).skill=="连环突刺"); var cards=new[]{main}.Concat(data.weapons[weapon].cards.Where(id=>id!=main).Take(4)).ToArray();
            var build=DebugLoadout.Initial(data,data.weapons[weapon].id); build.Preset(data,cards,new[]{"追击"}); var e=new BattleEngine(data,2,build); e.Start(weapon,0); e.Units[1].hp=e.Units[1].panel.hp=10000; e.Player.progress=100; e.Advance(0);
            var plan=e.Plan(e.Player,cards); e.Confirm(cards,e.Units[1].id); var impacts=e.VisualEvents.Where(v=>v.node==VisualNode.Impact).ToArray();
            Assert.AreEqual(plan.nativeSegments+plan.pursuits.Sum(g=>g.count),impacts.Length); Assert.AreEqual(impacts.Length,impacts.Select(v=>v.damageIndex).Distinct().Count());
        }
    }
}
