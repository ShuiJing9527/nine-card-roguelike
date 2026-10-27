using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Qiling.Tests
{
    public class SegmentPlaybackTests
    {
        BattleDemo demo; BattleViewConfig config; BattleCatalog data; SkillPresentation fast;
        [UnitySetUp] public IEnumerator Setup()
        {
            DebugFiles.RootOverride=Path.GetFullPath("../Validation/DebugTestFiles/"+System.Guid.NewGuid().ToString("N"));
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null; demo=Object.FindFirstObjectByType<BattleDemo>();
            Object.FindFirstObjectByType<InputField>().text="1";
            fast=Object.Instantiate(demo.viewConfig.defaultSkill); fast.simultaneousSegments=false; fast.windup=.08f; fast.travel=.025f; fast.projectileSpeed=0; fast.segmentInterval=.04f; fast.hitDuration=.02f; fast.deathDuration=.03f; fast.recovery=.02f; fast.shakeDuration=.01f; fast.effectLifetime=.08f;
            config=Object.Instantiate(demo.viewConfig); config.defaultSkill=fast; config.floatingTextLifetime=.15f; demo.viewConfig=config;
            data=Object.Instantiate(demo.catalog); data.skills=demo.catalog.skills.Select(s=>JsonUtility.FromJson<SkillDef>(JsonUtility.ToJson(s))).ToList(); foreach(var skill in data.skills) skill.presentation=fast; demo.catalog=data;
        }
        [UnityTearDown] public IEnumerator Cleanup() { DebugFiles.RootOverride=null; Object.Destroy(config); Object.Destroy(data); Object.Destroy(fast); yield return null; }
        IEnumerator StartBattle(int weapon=0)
        {
            if(weapon!=0) DemoFlowTests.Find(data.weapons[weapon].title).onClick.Invoke();
            DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null; demo.clockScale=0;
            foreach(var rig in Object.FindObjectsByType<UnitPresentation>(FindObjectsSortMode.None)) rig.motion=fast;
            var e=demo.Battle; e.Waiting.Clear(); e.ActiveId=null; foreach(var u in e.Units) u.progress=0; e.Player.progress=100; e.Advance(0);
        }
        void Compose(string[] cards)
        {
            foreach(var id in cards)
            {
                int index=data.weapons.Single(w=>w.id==demo.Battle.Loadout.weaponId).cards.IndexOf(id);
                GameObject.Find("Hand Card "+index).GetComponent<Button>().onClick.Invoke();
            }
        }
        [UnityTest] public IEnumerator SpearDurationCasesReachVisibleStateAndOwnerClockWithoutDisplayDesync()
        {
            yield return StartBattle(3);
            int[] expected={5,8,10,13};
            for(int mode=0;mode<4;mode++)
            {
                if(mode>0)
                {
                    demo.Retry(false); yield return null; demo.clockScale=0;
                    var reset=demo.Battle; reset.ActiveId=null; reset.Waiting.Clear(); foreach(var u in reset.Units) u.progress=0;
                    reset.Player.progress=100; reset.Advance(0); yield return null;
                }
                var e=demo.Battle; var w=data.weapons[3]; var cards=new[]{w.cards[8]}.Concat(w.cards.Take(4)).ToArray();
                var profile=e.Loadout; profile.Preset(data,cards,new[]{"专注"});
                if(mode==1 || mode==2) profile.SetTrait(data,cards[4],0,"延续");
                if(mode==2) profile.SetTrait(data,cards[4],1,"连锁反应");
                if(mode==3) profile.Preset(data,cards,new[]{"延续"});
                Assert.IsNull(e.ApplyLoadout(profile)); DemoFlowTests.Find("清空").onClick.Invoke(); Compose(cards);
                long draws=e.Rng.Draws; StringAssert.Contains("＝"+expected[mode]+"回合",e.Preview(cards,"player")); Assert.AreEqual(draws,e.Rng.Draws);
                DemoFlowTests.Find("确认出招").onClick.Invoke();
                for(int frame=0;frame<300 && demo.PresentationBusy;frame++) yield return null;
                Assert.IsFalse(demo.PresentationBusy); yield return new WaitForSecondsRealtime(.15f);
                Assert.AreEqual(expected[mode],e.Player.State(EffectKind.SpeedPercent).remaining);
                var visible=demo.DisplayedUnit("player"); Assert.AreEqual(expected[mode],visible.State(EffectKind.SpeedPercent).remaining);
                Assert.AreEqual(.15,visible.State(EffectKind.SpeedPercent).value,1e-9);
                Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>(true).Any(t=>t.text.Contains("速度↑15%("+expected[mode]+")")),"Visible percentage / duration must match the actual state");
                if(mode==3)
                {
                    for(int turn=0;turn<2;turn++)
                    {
                        var actor=turn==0?e.Units[1]:e.Player;
                        e.ActiveId=null; e.Waiting.Clear(); foreach(var u in e.Units) u.progress=0;
                        actor.progress=100; e.Advance(0); e.EndAction(actor);
                        for(int frame=0;frame<300 && demo.PresentationBusy;frame++) yield return null;
                        Assert.AreEqual(turn==0?13:12,e.Player.State(EffectKind.SpeedPercent).remaining);
                        Assert.AreEqual(turn==0?13:12,demo.DisplayedUnit("player").State(EffectKind.SpeedPercent).remaining);
                    }
                }
            }
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator BerserkOneThreeFiveCardCastsKeepVisibleBenefitAndCostsOnOneDuration()
        {
            yield return StartBattle(1);
            foreach(int n in new[]{1,3,5})
            {
                if(n>1)
                {
                    demo.Retry(false); yield return null; demo.clockScale=0;
                    demo.Battle.ActiveId=null; demo.Battle.Waiting.Clear(); foreach(var u in demo.Battle.Units) u.progress=0;
                    demo.Battle.Player.progress=100; demo.Battle.Advance(0); yield return null;
                }
                var e=demo.Battle; var w=data.weapons[1]; var cards=new[]{w.cards[8]}.Concat(w.cards.Take(n-1)).ToArray();
                var profile=e.Loadout; foreach(var card in profile.cards) card.traits=new System.Collections.Generic.List<string>{"专注"};
                Assert.IsNull(e.ApplyLoadout(profile)); DemoFlowTests.Find("清空").onClick.Invoke(); Compose(cards);
                StringAssert.Contains("＝"+n+"回合",e.Preview(cards,"player")); DemoFlowTests.Find("确认出招").onClick.Invoke();
                for(int frame=0;frame<300 && demo.PresentationBusy;frame++) yield return null;
                Assert.IsFalse(demo.PresentationBusy);
                foreach(var kind in new[]{EffectKind.AttackUp,EffectKind.DefensePercent,EffectKind.SpecialDefensePercent})
                {
                    Assert.AreEqual(n,e.Player.State(kind).remaining); Assert.AreEqual(n,demo.DisplayedUnit("player").State(kind).remaining);
                }
                Assert.AreEqual(2*n,e.Player.State(EffectKind.AttackUp).value); Assert.AreEqual(-.3,e.Player.State(EffectKind.DefensePercent).value,1e-9);
            }
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator GaleStepUsesConfiguredExtensionSkillAndUpdatesVisibleSpeedAndSelfAdvanceSeparately()
        {
            data.Card(data.weapons[0].cards[8]).skill="疾风步";
            yield return StartBattle(); var e=demo.Battle; var w=data.weapons[0]; var cards=new[]{w.cards[8]}.Concat(w.cards.Take(4)).ToArray();
            var profile=e.Loadout; profile.Preset(data,cards,new[]{"增幅"}); Assert.IsNull(e.ApplyLoadout(profile)); Compose(cards);
            var enemyProgress=e.Units.Where(u=>!u.player).Select(u=>u.progress).ToArray();
            DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int frame=0;frame<300 && demo.PresentationBusy;frame++) yield return null;
            Assert.IsFalse(demo.PresentationBusy);
            Assert.AreEqual(.2625,e.Player.State(EffectKind.SpeedPercent).value,1e-9); Assert.AreEqual(5,e.Player.State(EffectKind.SpeedPercent).remaining);
            Assert.AreEqual(52.5,e.Player.progress,1e-9); Assert.AreEqual(52.5,demo.DisplayedUnit("player").progress,1e-9);
            Assert.AreEqual(e.Player.Stat(Attribute.Speed),demo.DisplayedUnit("player").Stat(Attribute.Speed),1e-9);
            Assert.IsNull(e.Player.State(EffectKind.SpeedUp)); CollectionAssert.AreEqual(enemyProgress,e.Units.Where(u=>!u.player).Select(u=>u.progress));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator VisibleHpShieldFollowEachMixedResultAndInputsAndClockAreLocked()
        {
            yield return StartBattle(); var e=demo.Battle; var cards=data.weapons[0].cards.Take(5).ToArray(); Compose(cards);
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); profile.Preset(data,cards,new[]{"追击","锋锐","穿甲"}); Assert.IsNull(e.ApplyLoadout(profile));
            var target=e.Units[1]; target.hp=target.panel.hp=10000; target.shield=10; target.states.Add(new BattleState { kind=EffectKind.Evade,value=.25 }); target.states.Add(new BattleState { kind=EffectKind.Block,value=1 }); e.Player.panel.crit=.5;
            demo.SendMessage("Refresh"); DemoFlowTests.Find("确认出招").onClick.Invoke();
            Assert.IsTrue(demo.PresentationBusy); Assert.AreEqual(10000,demo.DisplayedUnit(target.id).hp); Assert.AreEqual(10,demo.DisplayedUnit(target.id).shield); Assert.Less(target.hp,10000);
            var order=demo.CurrentSequence.ToArray(); int action=e.ActionSerial; long draws=e.Rng.Draws; double natural=e.NaturalTime;
            DemoFlowTests.Find("清空").onClick.Invoke(); DemoFlowTests.Find("确认出招").onClick.Invoke(); GameObject.Find("Hand Card 8").GetComponent<Button>().onClick.Invoke();
            DemoFlowTests.Find("自由重铸").onClick.Invoke(); Assert.IsNull(GameObject.Find("Trait Test Overlay")); CollectionAssert.AreEqual(order,demo.CurrentSequence); Assert.AreEqual(action,e.ActionSerial);
            demo.clockScale=1000; bool intermediate=false,shieldSeen=false,lifeSeen=false; int inspected=0; var feedback=new System.Collections.Generic.HashSet<string>(); bool captured=false,criticalStyle=false;
            for(int i=0;i<1500 && demo.PresentationBusy;i++)
            {
                Assert.AreEqual(natural,e.NaturalTime,"Natural bars must pause for the entire playback");
                foreach(var text in demo.authoredBattle.GetComponentsInChildren<Text>()) if(text.transform.parent.name.StartsWith(config.floatingTextPrefab.name)) { feedback.Add(text.text); criticalStyle|=Mathf.Abs(text.color.r-config.criticalColor.r)<.001f && Mathf.Abs(text.color.g-config.criticalColor.g)<.001f; }
                var hits=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray();
                foreach(var hit in hits.Skip(inspected))
                {
                    var d=e.Damage[hit.damageIndex]; Assert.AreEqual(d.hpLost,hit.damage); Assert.AreEqual(d.absorbed,hit.shield); Assert.AreEqual(d.calculated,hit.calculated);
                    shieldSeen|=hit.shield>0; lifeSeen|=hit.damage>0;
                }
                inspected=hits.Length;
                if(hits.Length>0)
                {
                    var last=hits.Last(); var shown=demo.DisplayedUnit(last.target); Assert.AreEqual(last.hpAfter,shown.hp); Assert.AreEqual(last.shieldAfter,shown.shield);
                    var life=GameObject.Find("Unit Info 1").transform.Find("Health Bar").GetComponentInChildren<Text>(); Assert.AreEqual(shown.hp+" / "+shown.panel.hp,life.text);
                    intermediate|=shown.hp>target.hp && shown.hp<10000;
                    if(!captured && hits.Any(h=>h.shield>0)) { DemoFlowTests.Capture("segments-live-hp-shield"); captured=true; }
                }
                if(demo.PresentationTrace.Count==e.VisualEvents.Count) demo.clockScale=0;
                yield return null;
            }
            demo.clockScale=0; Assert.IsFalse(demo.PresentationBusy); Assert.IsTrue(intermediate); Assert.IsTrue(shieldSeen && lifeSeen); Assert.AreEqual(draws,e.Rng.Draws);
            Assert.AreEqual(e.Damage.Count,demo.PresentationTrace.Count(t=>t.node==VisualNode.Impact)); Assert.AreEqual(target.hp,demo.DisplayedUnit(target.id).hp); Assert.AreEqual(0,demo.TemporaryViewCount);
            Assert.IsTrue(criticalStyle); Assert.IsTrue(feedback.Any(t=>t.StartsWith("−")));
            foreach(var word in new[]{"闪避","格挡","护盾 −"}) Assert.IsTrue(feedback.Any(t=>t.Contains(word)),"Actual floating text: "+word);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator BouncePlaybackPathNumbersAndRetargetMatchRecordedEvents()
        {
            fast.simultaneousSegments=true;
            int weapon=data.weapons.FindIndex(w=>w.cards.Any(id=>data.Skill(data.Card(id).skill).pattern==Pattern.Bounce)); yield return StartBattle(weapon);
            var e=demo.Battle; foreach(var u in e.Units.Where(u=>!u.player)) u.hp=u.panel.hp=10000;
            string main=data.weapons[weapon].cards.First(id=>data.Skill(data.Card(id).skill).pattern==Pattern.Bounce); var cards=new[]{main}.Concat(data.weapons[weapon].cards.Where(id=>id!=main).Take(4)).ToArray(); Compose(cards);
            DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<1000 && demo.PresentationBusy;i++) yield return null; Assert.IsFalse(demo.PresentationBusy);
            var launches=demo.PresentationTrace.Where(t=>t.node==VisualNode.Launch && (t.segment=="原生" || t.segment=="弹射")).ToArray(); Assert.Greater(launches.Length,1); Assert.IsTrue(launches.All(t=>t.batch==0));
            for(int i=1;i<launches.Length;i++) Assert.Less(Vector3.Distance(launches[i-1].to,launches[i].from),.001f);
            foreach(var hit in demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact)) { var d=e.Damage[hit.damageIndex]; Assert.AreEqual(d.target,hit.target); Assert.AreEqual(d.hpLost,hit.damage); }
            Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator HealingAndShieldUpdateAtDedicatedEventsAndHaveDistinctVisibleFeedback()
        {
            yield return StartBattle(); var e=demo.Battle; e.Player.hp=1; Compose(new[]{data.weapons[0].cards[5]}); demo.SendMessage("Refresh");
            DemoFlowTests.Find("确认出招").onClick.Invoke(); Assert.AreEqual(1,demo.DisplayedUnit("player").hp); Assert.Greater(e.Player.hp,1);
            bool healText=false,healColor=false;
            for(int i=0;i<500 && demo.PresentationBusy;i++)
            {
                foreach(var text in demo.authoredBattle.GetComponentsInChildren<Text>().Where(t=>t.text.StartsWith("治疗 +"))) { healText=true; healColor|=Mathf.Abs(text.color.g-config.healColor.g)<.001f && Mathf.Abs(text.color.r-config.healColor.r)<.001f; }
                yield return null;
            }
            Assert.IsTrue(healText && healColor); Assert.AreEqual(e.Player.hp,demo.DisplayedUnit("player").hp);
            e.Player.progress=100; e.Advance(0); DemoFlowTests.Find("清空").onClick.Invoke(); Compose(new[]{data.weapons[0].cards[3]}); DemoFlowTests.Find("确认出招").onClick.Invoke();
            Assert.AreEqual(0,demo.DisplayedUnit("player").shield); Assert.Greater(e.Player.shield,0); bool shieldText=false,shieldColor=false;
            for(int i=0;i<500 && demo.PresentationBusy;i++)
            {
                foreach(var text in demo.authoredBattle.GetComponentsInChildren<Text>().Where(t=>t.text.StartsWith("护盾 +"))) { shieldText=true; shieldColor|=Mathf.Abs(text.color.b-config.shieldColor.b)<.001f && Mathf.Abs(text.color.r-config.shieldColor.r)<.001f; }
                yield return null;
            }
            Assert.IsTrue(shieldText && shieldColor); Assert.AreEqual(e.Player.shield,demo.DisplayedUnit("player").shield); Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator LethalSegmentsFlyToBossAndVictoryWaitsForPlaybackAndCleansUp()
        {
            yield return StartBattle(); var e=demo.Battle; Compose(data.weapons[0].cards.Take(5).ToArray()); e.Units[1].hp=1; e.Units[2].hp=e.Units[2].panel.hp=10000;
            DemoFlowTests.Find("确认出招").onClick.Invoke(); bool sawFirst=false;
            for(int i=0;i<1000 && demo.PresentationBusy;i++)
            {
                var hits=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray();
                if(hits.Length==1) { Assert.AreEqual("enemy0",hits[0].target); Assert.AreEqual(10000,demo.DisplayedUnit("enemy1").hp); sawFirst=true; }
                yield return null;
            }
            Assert.IsTrue(sawFirst); var impacts=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray(); Assert.AreEqual("enemy1",impacts[1].target);
            var flights=demo.PresentationTrace.Where(t=>t.node==VisualNode.Launch).ToArray(); Assert.AreEqual(impacts.Length,flights.Length);
            for(int i=0;i<flights.Length;i++) { Assert.AreEqual(impacts[i].target,flights[i].target); Assert.Less(Vector3.Distance(flights[i].to,impacts[i].to),.001f); }
            demo.Retry(false); demo.clockScale=0; yield return null; e=demo.Battle;
            foreach(var rig in Object.FindObjectsByType<UnitPresentation>(FindObjectsSortMode.None)) rig.motion=fast;
            e.Units[1].hp=e.Units[2].hp=1; e.Player.progress=100; e.Advance(0); DemoFlowTests.Find("确认出招").onClick.Invoke(); Assert.IsTrue(e.Finished); Assert.IsTrue(demo.PresentationBusy); Assert.AreEqual(1,demo.DisplayedUnit("enemy0").hp);
            for(int i=0;i<1000 && demo.PresentationBusy;i++) { Assert.IsFalse(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.StartsWith("胜利 ·"))); yield return null; }
            yield return null; Assert.IsFalse(demo.PresentationBusy); Assert.AreEqual(0,demo.TemporaryViewCount); Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.StartsWith("胜利 ·"))); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SkillSpecificEndEffectLifetimeIsHonoredBeforeUnlockAndCleanup()
        {
            var custom=Object.Instantiate(fast); custom.effectLifetime=.45f; custom.endEffect=fast.impactEffect;
            data.skills.Single(s=>s.id==data.Card(data.weapons[0].cards[0]).skill).presentation=custom;
            yield return StartBattle(); Compose(new[]{data.weapons[0].cards[0]}); DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<500 && !demo.PresentationTrace.Any(t=>t.node==VisualNode.CastEnd);i++) yield return null;
            float end=Time.unscaledTime; Assert.IsTrue(demo.PresentationBusy); Assert.Greater(demo.TemporaryViewCount,0);
            for(int i=0;i<500 && demo.PresentationBusy;i++) yield return null;
            Assert.Greater(Time.unscaledTime-end,.35f,"Do not truncate a skill-specific effect to the shared default lifetime"); Assert.AreEqual(0,demo.TemporaryViewCount); Object.Destroy(custom); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator BatchesLaunchTogetherShowDistinctRealNumbersAndPreserveEverySnapshot()
        {
            fast.simultaneousSegments=true; fast.segmentsPerBatch=3; fast.projectileSpeed=0; fast.travel=.15f; fast.batchInterval=.06f;
            config.floatingTextLifetime=.5f;
            yield return StartBattle(); var e=demo.Battle; var cards=data.weapons[0].cards.Take(5).ToArray(); Compose(cards);
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); profile.Preset(data,cards,new[]{"追击","锋锐","穿甲"}); Assert.IsNull(e.ApplyLoadout(profile));
            var target=e.Units[1]; target.hp=target.panel.hp=10000; target.shield=10;
            target.states.Add(new BattleState { kind=EffectKind.Evade,value=.25 }); target.states.Add(new BattleState { kind=EffectKind.Block,value=1 }); e.Player.panel.crit=.5;
            DemoFlowTests.Find("确认出招").onClick.Invoke(); long draws=e.Rng.Draws; double natural=e.NaturalTime;
            var records=JsonUtility.ToJson(new DamageRows { rows=e.Damage.ToArray() }); bool threeProjectiles=false,threeNumbers=false,captured=false; int lastBatch=0;
            for(int i=0;i<1000 && demo.PresentationBusy;i++)
            {
                threeProjectiles|=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t=>t.name==fast.projectile.name+"(Clone)")>=3;
                var newest=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact && t.batch>lastBatch).ToArray();
                if(newest.Length>0)
                {
                    lastBatch=newest.Max(t=>t.batch);
                    var current=newest.Where(t=>t.batch==lastBatch).ToArray();
                    if(current.Length==3)
                    {
                        var labels=demo.authoredBattle.GetComponentsInChildren<Text>().Where(t=>t.transform.parent.name.StartsWith(config.floatingTextPrefab.name)).ToArray();
                        threeNumbers|=labels.Length>=3 && labels.Select(t=>t.transform.position).Distinct().Count()>=3;
                        if(!captured && current.Any(t=>e.Damage[t.damageIndex].critical)) { DemoFlowTests.Capture("grouped-three-real-numbers"); captured=true; }
                    }
                }
                Assert.AreEqual(natural,e.NaturalTime); yield return null;
            }
            Assert.IsFalse(demo.PresentationBusy); Assert.IsTrue(threeProjectiles); Assert.IsTrue(threeNumbers);
            CollectionAssert.AreEqual(e.VisualEvents.Select(v=>v.eventIndex),demo.PresentationTrace.Select(v=>v.eventIndex));
            var impacts=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray(); Assert.AreEqual(e.Damage.Count,impacts.Length);
            foreach(var hit in impacts) { var d=e.Damage[hit.damageIndex]; Assert.AreEqual(d.hpLost,hit.damage); Assert.AreEqual(d.absorbed,hit.shield); Assert.AreEqual(d.calculated,hit.calculated); }
            foreach(var batch in impacts.GroupBy(t=>t.batch)) { Assert.Greater(batch.Key,0); Assert.LessOrEqual(batch.Count(),3); Assert.AreEqual(1,batch.Select(t=>t.frame).Distinct().Count()); Assert.AreEqual(1,batch.Select(t=>t.group).Distinct().Count()); }
            Assert.AreEqual(records,JsonUtility.ToJson(new DamageRows { rows=e.Damage.ToArray() })); Assert.AreEqual(draws,e.Rng.Draws);
            Assert.AreEqual(target.hp,demo.DisplayedUnit(target.id).hp); Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator BatchedRetargetAndRetryNeverUseDeadTargetsOrOldEffects()
        {
            fast.simultaneousSegments=true; fast.segmentsPerBatch=8; fast.travel=.12f; fast.projectileSpeed=0;
            yield return StartBattle(); var e=demo.Battle; Compose(data.weapons[0].cards.Take(5).ToArray()); e.Units[1].hp=1; e.Units[2].hp=e.Units[2].panel.hp=10000;
            DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<1000 && demo.PresentationBusy;i++) yield return null;
            var hits=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray(); Assert.AreEqual("enemy0",hits[0].target); Assert.IsTrue(hits.Skip(1).All(t=>t.target=="enemy1"));
            var flights=demo.PresentationTrace.Where(t=>t.node==VisualNode.Launch).ToArray();
            for(int i=0;i<flights.Length;i++) { Assert.AreEqual(hits[i].target,flights[i].target); Assert.Less(Vector3.Distance(flights[i].to,hits[i].to),.001f); }
            demo.Retry(false); demo.clockScale=0; yield return null; e=demo.Battle;
            foreach(var rig in Object.FindObjectsByType<UnitPresentation>(FindObjectsSortMode.None)) rig.motion=fast;
            e.Player.progress=100; e.Advance(0); DemoFlowTests.Find("确认出招").onClick.Invoke();
            bool flying=false;
            for(int i=0;i<300 && !flying;i++) { flying=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t=>t.name==fast.projectile.name+"(Clone)"); if(!flying) yield return null; }
            Assert.IsTrue(flying); demo.Retry(false); demo.clockScale=0;
            for(int i=0;i<30;i++) yield return null;
            Assert.AreEqual(0,demo.PresentationTrace.Count); Assert.AreEqual(0,demo.TemporaryViewCount); Assert.AreEqual(60,demo.DisplayedUnit("enemy0").hp); Assert.AreEqual(0,demo.Battle.Damage.Count);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator FreeReforgeEquipsChainReactionShowsStatusReasonsAndPlaysSixRealPursuits()
        {
            fast.simultaneousSegments=true;
            yield return StartBattle(); var e=demo.Battle; Compose(data.weapons[0].cards.Take(3).ToArray()); e.Units[1].hp=e.Units[1].panel.hp=10000;
            DemoFlowTests.Find("自由重铸").onClick.Invoke(); yield return null;
            GameObject.Find("Trait Card 2").GetComponentsInChildren<Button>().Single(b=>b.name.StartsWith("词条1")).onClick.Invoke();
            DemoFlowTests.Find("追击〔").onClick.Invoke(); DemoFlowTests.Find("填入卡3的词条1").onClick.Invoke();
            GameObject.Find("Trait Card 2").GetComponentsInChildren<Button>().Single(b=>b.name.StartsWith("词条2")).onClick.Invoke();
            var reaction=DemoFlowTests.Find("连锁反应〔"); Assert.IsFalse(reaction.name.Contains("未实现")); reaction.onClick.Invoke();
            Assert.IsTrue(DemoFlowTests.Find("填入卡3的词条2").interactable); DemoFlowTests.Find("填入卡3的词条2").onClick.Invoke();
            DemoFlowTests.Find("镜像〔").onClick.Invoke(); Assert.IsTrue(DemoFlowTests.Find("填入卡3的词条2").interactable);
            Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("已接入基础复制")));
            DemoFlowTests.Find("词条实现状态").onClick.Invoke(); yield return null;
            Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("已实现且可装备（25）") && t.text.Contains("已定义但未实现（0）") && t.text.Contains("规则待定（1）")));
            DemoFlowTests.Find("返回词条测试").onClick.Invoke(); DemoFlowTests.Find("保存并应用当前排谱").onClick.Invoke(); yield return null;
            var plan=e.Plan(e.Player,demo.CurrentSequence); Assert.AreEqual(3,plan.chain); var group=plan.pursuits.Single(g=>g.slot==3); Assert.AreEqual(6,group.count); Assert.AreEqual(.5,group.coefficient);
            long draws=e.Rng.Draws; DemoFlowTests.Find("出招预览").onClick.Invoke(); yield return null;
            Assert.AreEqual(draws,e.Rng.Draws); Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("来源3号位，当前连锁6，追加6段")));
            DemoFlowTests.Find("返回战斗").onClick.Invoke(); DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<1000 && demo.PresentationBusy;i++) yield return null;
            var hits=e.Damage.Where(d=>d.label.StartsWith("追击[3号位")).ToArray(); Assert.AreEqual(6,hits.Length); Assert.IsTrue(hits.All(d=>d.coefficient==.5));
            Assert.AreEqual(6,demo.PresentationTrace.Count(t=>t.node==VisualNode.Impact && t.segment.StartsWith("追击[3号位"))); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator EveryDefinedExtensionEquipsAppliesPersistsAndStatusShowsTwentyFive()
        {
            yield return StartBattle(); Compose(data.weapons[0].cards.Take(5).ToArray());
            string[] ids={"专注","镜像","压制","终结","乘隙","裂隙","贯通","连锁收益","连锁蓄势","连锁协奏","同调","交织"};
            long draws=demo.Battle.Rng.Draws;
            foreach(var id in ids)
            {
                DemoFlowTests.Find("自由重铸").onClick.Invoke();
                GameObject.Find("Trait Card 2").GetComponentsInChildren<Button>().Single(b=>b.name.StartsWith("词条1")).onClick.Invoke();
                var option=DemoFlowTests.Find(id+"〔"); Assert.IsFalse(option.name.Contains("未实现")); option.onClick.Invoke();
                var apply=DemoFlowTests.Find("填入卡3的词条1"); Assert.IsTrue(apply.interactable); apply.onClick.Invoke();
                DemoFlowTests.Find("保存并应用当前排谱").onClick.Invoke();
                Assert.AreEqual(id,demo.Battle.Card(data.weapons[0].cards[2]).traits[0]); Assert.AreEqual(draws,demo.Battle.Rng.Draws);
                Assert.IsTrue(File.ReadAllText(Path.Combine(DebugFiles.Root,"profiles.json")).Contains(id)); yield return null;
            }
            DemoFlowTests.Find("自由重铸").onClick.Invoke(); DemoFlowTests.Capture("all-defined-traits-equipped");
            DemoFlowTests.Find("词条实现状态").onClick.Invoke(); yield return null; DemoFlowTests.Capture("all-defined-trait-status");
            Assert.IsTrue(demo.authoredBattle.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("已实现且可装备（25）") && t.text.Contains("已定义但未实现（0）") && t.text.Contains("规则待定（1）")));
            DemoFlowTests.Find("返回词条测试").onClick.Invoke(); DemoFlowTests.Find("返回（不保存）").onClick.Invoke();
            demo.Retry(false); demo.clockScale=0; yield return null; Assert.AreEqual("交织",demo.Battle.Card(data.weapons[0].cards[2]).traits[0]); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MirrorAndNewDamageTraitsPlayRecordedResultsWithoutExtraSettlement()
        {
            fast.simultaneousSegments=true; fast.segmentsPerBatch=3;
            yield return StartBattle(); var e=demo.Battle; var cards=data.weapons[0].cards.Take(5).ToArray(); Compose(cards);
            var profile=DebugLoadout.Initial(data,data.weapons[0].id); profile.Preset(data,cards,new[]{"同调"});
            profile.cards.Single(c=>c.cardId==cards[0]).traits=new[]{"专注","压制","镜像"}.ToList();
            profile.cards.Single(c=>c.cardId==cards[1]).traits=new[]{"裂隙","贯通","追击"}.ToList();
            profile.cards.Single(c=>c.cardId==cards[2]).traits=new[]{"追击","连锁反应","连锁收益"}.ToList();
            Assert.IsNull(e.ApplyLoadout(profile)); e.Player.panel.crit=.5; e.Units[1].hp=e.Units[1].panel.hp=10000; e.Units[1].shield=20;
            long before=e.Rng.Draws; string preview=e.Preview(cards,"enemy0"); Assert.AreEqual(before,e.Rng.Draws); Assert.IsTrue(preview.Contains("镜像"));
            DemoFlowTests.Find("确认出招").onClick.Invoke(); string records=JsonUtility.ToJson(new DamageRows { rows=e.Damage.ToArray() }); long draws=e.Rng.Draws;
            for(int i=0;i<1500 && demo.PresentationBusy;i++) yield return null;
            Assert.IsFalse(demo.PresentationBusy); Assert.AreEqual(draws,e.Rng.Draws); Assert.AreEqual(records,JsonUtility.ToJson(new DamageRows { rows=e.Damage.ToArray() }));
            var mirror=e.Damage.Where(d=>d.mirror).ToArray(); Assert.AreEqual(7,mirror.Length); Assert.IsTrue(mirror.All(d=>d.damageUp==0 && d.bonus==0 && d.resource==0 && d.bypassRatio==0));
            Assert.IsTrue(e.Damage.Any(d=>d.bypass>0)); Assert.IsTrue(e.Damage.Any(d=>d.critical && d.penetration>0));
            var impacts=demo.PresentationTrace.Where(t=>t.node==VisualNode.Impact).ToArray(); Assert.AreEqual(e.Damage.Count,impacts.Length);
            foreach(var hit in impacts) { var d=e.Damage[hit.damageIndex]; Assert.AreEqual(d.hpLost,hit.damage); Assert.AreEqual(d.absorbed,hit.shield); Assert.AreEqual(d.calculated,hit.calculated); }
            Assert.AreEqual(7,impacts.Count(t=>t.segment.StartsWith("镜像·"))); Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator OrdinaryFloatingTextIsLocalRandomConcurrentAndDoesNotConsumeCombatRng()
        {
            fast.simultaneousSegments=true; fast.segmentsPerBatch=8; fast.travel=.12f; fast.projectileSpeed=0;
            config.floatingTextLifetime=.7f; config.floatingTextRise=55; config.floatingTextFadeStart=.5f;
            config.floatingTextOffsetRange=new Vector2(54,28); config.floatingTextAvoidanceSpacing=28;
            yield return StartBattle(); var e=demo.Battle; Compose(data.weapons[0].cards.Take(5).ToArray()); e.Units[1].hp=e.Units[1].panel.hp=10000;
            DemoFlowTests.Find("确认出招").onClick.Invoke(); long draws=e.Rng.Draws; string records=JsonUtility.ToJson(new DamageRows {rows=e.Damage.ToArray()});
            for(int i=0;i<500 && demo.FloatingTextTrace.Count<7;i++) yield return null;
            var burst=demo.FloatingTextTrace.Take(7).ToArray(); Assert.AreEqual(7,burst.Length); Assert.AreEqual(1,burst.Select(t=>t.frame).Distinct().Count());
            foreach(var spawn in burst)
            {
                Assert.AreEqual("enemy0",spawn.target); Assert.LessOrEqual(Mathf.Abs(spawn.start.x-spawn.anchor.x),54.01f); Assert.LessOrEqual(Mathf.Abs(spawn.start.y-spawn.anchor.y),28.01f);
                Assert.LessOrEqual(spawn.start.y+55+55,-config.floatingTextTopBoundary+.1f);
                Assert.LessOrEqual(Mathf.Abs(spawn.drift),config.floatingTextHorizontalDrift);
            }
            Assert.AreEqual(7,burst.Select(t=>t.start).Distinct().Count());
            Assert.Greater(burst.Select(t=>t.start.x).Distinct().Count(),3,"No fixed rows/columns");
            var first=demo.authoredBattle.GetComponentsInChildren<CanvasGroup>().First(g=>g.transform.name.StartsWith(config.floatingTextPrefab.name));
            var rt=first.GetComponent<RectTransform>(); Vector2 start=rt.anchoredPosition; float created=Time.unscaledTime;
            yield return new WaitForSecondsRealtime(.15f); Assert.IsTrue(first!=null); Assert.AreEqual(1,first.alpha,.01f); Assert.Greater(rt.anchoredPosition.y,start.y);
            yield return new WaitForSecondsRealtime(.3f); Assert.IsTrue(first!=null); Assert.Less(first.alpha,.9f); Assert.Greater(first.alpha,0);
            DemoFlowTests.Capture("ordinary-random-floating-text");
            for(int i=0;i<500 && demo.PresentationBusy;i++) yield return null;
            Assert.IsFalse(demo.PresentationBusy); Assert.AreEqual(draws,e.Rng.Draws); Assert.AreEqual(records,JsonUtility.ToJson(new DamageRows {rows=e.Damage.ToArray()}));
            Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
        [System.Serializable] class DamageRows { public DamageRecord[] rows; }
        [UnityTest] public IEnumerator RetryDuringImpactCancelsAllOldVisualsAndLaterCallbacks()
        {
            yield return StartBattle(); Compose(data.weapons[0].cards.Take(5).ToArray()); demo.Battle.Units[1].hp=1; demo.Battle.Units[2].hp=demo.Battle.Units[2].panel.hp=10000;
            DemoFlowTests.Find("确认出招").onClick.Invoke();
            for(int i=0;i<300 && !demo.PresentationTrace.Any(t=>t.node==VisualNode.Impact);i++) yield return null;
            Assert.IsTrue(demo.PresentationBusy); Assert.Greater(demo.TemporaryViewCount,0); var old=demo.Battle; demo.Retry(false); demo.clockScale=0; yield return null;
            Assert.AreNotSame(old,demo.Battle); Assert.IsFalse(demo.PresentationBusy); Assert.AreEqual(0,demo.TemporaryViewCount); Assert.AreEqual(60,demo.DisplayedUnit("enemy0").hp);
            for(int i=0;i<30;i++) yield return null;
            Assert.AreEqual(0,demo.Battle.Damage.Count); Assert.AreEqual(0,demo.PresentationTrace.Count);
            demo.Battle.Player.progress=100; demo.Battle.Advance(0); DemoFlowTests.Find("确认出招").onClick.Invoke(); yield return null; Assert.IsTrue(demo.PresentationBusy);
            DemoFlowTests.Find("设置").onClick.Invoke(); DemoFlowTests.Find("更换武器").onClick.Invoke(); yield return null; Assert.IsNull(demo.Battle); Assert.AreEqual(0,demo.TemporaryViewCount); LogAssert.NoUnexpectedReceived();
        }
    }
}
