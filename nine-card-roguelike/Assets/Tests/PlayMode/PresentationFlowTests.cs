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
    public class PresentationFlowTests
    {
        BattleDemo demo;
        [UnitySetUp] public IEnumerator Setup()
        {
            DebugFiles.RootOverride=Path.GetFullPath("../Validation/DebugTestFiles/"+System.Guid.NewGuid().ToString("N"));
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null; demo=Object.FindFirstObjectByType<BattleDemo>();
        }
        [UnityTearDown] public IEnumerator Cleanup() { DebugFiles.RootOverride=null; yield return null; }
        [UnityTest] public IEnumerator SceneEditsSurviveBindingAndRetryAndSingleBossHidesUnusedViews()
        {
            var face=demo.authoredBattle.transform.Find("Hand Faces").GetChild(0).GetComponent<RectTransform>();
            var label=face.GetComponentsInChildren<Text>(true).First(t=>t.transform.parent==face); face.anchoredPosition+=new Vector2(19,0); var position=face.anchoredPosition; label.fontSize=29;
            var confirm=demo.authoredBattle.GetComponentsInChildren<Button>(true).First(b=>b.name=="确认出招").GetComponent<RectTransform>(); confirm.anchoredPosition+=new Vector2(-12,0); var confirmPosition=confirm.anchoredPosition;
            DemoFlowTests.Find("单Boss").onClick.Invoke(); DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null;
            Assert.AreEqual(0,demo.authoredBattle.transform.Find("Hand Hit Regions").GetComponentsInChildren<Text>(true).Length);
            Assert.AreEqual(2,demo.Battle.Units.Count); Assert.AreEqual(position,face.anchoredPosition); Assert.AreEqual(29,label.fontSize); Assert.AreEqual(confirmPosition,confirm.anchoredPosition);
            Assert.IsFalse(demo.authoredBattle.transform.Find("Unit Info 2").gameObject.activeSelf);
            demo.Retry(false); yield return null; Assert.AreEqual(position,face.anchoredPosition); Assert.AreEqual(29,label.fontSize);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ConfiguredProjectileAndImpactPrefabActuallyPlayAndFinish()
        {
            DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null; demo.clockScale=0;
            demo.Battle.Waiting.Clear(); demo.Battle.ActiveId=null; foreach(var u in demo.Battle.Units) u.progress=0; demo.Battle.Player.progress=100; demo.Battle.Advance(0);
            DemoFlowTests.Find("1. 连射").onClick.Invoke(); DemoFlowTests.Find("确认出招").onClick.Invoke();
            long draws=demo.Battle.Rng.Draws; int damage=demo.Battle.TotalCalculatedDamage; bool projectile=false,impact=false;
            yield return null;
            for(int i=0;i<600 && (demo.PresentationBusy || demo.PresentationTrace.Count<demo.Battle.VisualEvents.Count);i++)
            {
                projectile|=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t=>t.name==demo.viewConfig.defaultSkill.projectile.name+"(Clone)");
                impact|=Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Any(p=>p.name==demo.viewConfig.defaultSkill.impactEffect.name+"(Clone)");
                yield return null;
            }
            Assert.IsTrue(projectile,"Configured projectile must be instantiated"); Assert.IsTrue(impact,"Configured impact effect must be instantiated"); Assert.IsFalse(demo.PresentationBusy);
            Assert.AreEqual(draws,demo.Battle.Rng.Draws); Assert.AreEqual(damage,demo.Battle.TotalCalculatedDamage);
            Assert.AreEqual(demo.Battle.VisualEvents.Count,demo.PresentationTrace.Count); LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MissingAnimationsEffectsAndNullCurvesCannotBlockSegmentPlayback()
        {
            var config=Object.Instantiate(demo.viewConfig); var empty=ScriptableObject.CreateInstance<SkillPresentation>();
            empty.windup=empty.travel=empty.hitDuration=empty.recovery=empty.deathDuration=0; empty.shakeAmplitude=0; empty.moveCurve=empty.projectileCurve=empty.shakeCurve=null;
            config.defaultSkill=empty; demo.viewConfig=config;
            var data=Object.Instantiate(demo.catalog); data.skills=demo.catalog.skills.Select(s=>JsonUtility.FromJson<SkillDef>(JsonUtility.ToJson(s))).ToList(); foreach(var skill in data.skills) skill.presentation=empty; demo.catalog=data;
            DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null; demo.clockScale=0;
            demo.Battle.Waiting.Clear(); demo.Battle.ActiveId=null; foreach(var u in demo.Battle.Units) u.progress=0; demo.Battle.Player.progress=100; demo.Battle.Advance(0);
            foreach(var caption in new[]{"1. 连射","2. 破风箭","3. 多重扫射","4. 疾风护盾","5. 灵巧闪避"}) DemoFlowTests.Find(caption).onClick.Invoke();
            DemoFlowTests.Find("自由重铸").onClick.Invoke(); yield return null; DemoFlowTests.Find("五卡：追击＋锋锐＋穿甲").onClick.Invoke(); yield return null; DemoFlowTests.Find("保存并应用当前排谱").onClick.Invoke(); yield return null;
            demo.Battle.Units[1].hp=1; demo.Battle.Units[2].hp=100000; demo.Battle.Units[2].panel.hp=100000;
            DemoFlowTests.Find("确认出招").onClick.Invoke(); long draws=demo.Battle.Rng.Draws; int damage=demo.Battle.TotalCalculatedDamage;
            yield return null;
            for(int i=0;i<600 && (demo.PresentationBusy || demo.PresentationTrace.Count<demo.Battle.VisualEvents.Count);i++) yield return null;
            Assert.IsFalse(demo.PresentationBusy); Assert.AreEqual(demo.Battle.VisualEvents.Count,demo.PresentationTrace.Count);
            CollectionAssert.AreEqual(demo.Battle.VisualEvents.Select(e=>e.target),demo.PresentationTrace.Select(e=>e.target));
            Assert.IsTrue(demo.PresentationTrace.Any(e=>e.segment!=null && e.segment.StartsWith("追击"))); Assert.AreEqual(draws,demo.Battle.Rng.Draws); Assert.AreEqual(damage,demo.Battle.TotalCalculatedDamage);
            Assert.IsNull(GameObject.Find("普通史莱姆 [Visual Replacement]"));
            Object.Destroy(config); Object.Destroy(data); Object.Destroy(empty); LogAssert.NoUnexpectedReceived();
        }
    }
}
