using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling.Tests
{
    public class PresentationAssetTests
    {
        const string Root="Assets/Presentation/";
        [Test] public void TemplatesAndReusablePartsAreSavedAssets()
        {
            var config=AssetDatabase.LoadAssetAtPath<BattleViewConfig>(Root+"BattleViewConfig.asset"); Assert.NotNull(config);
            Assert.NotNull(config.selectionPage); Assert.NotNull(config.battlePage); Assert.AreEqual(4,config.panels.Count);
            foreach(var part in new[]{"Card","CardHitRegion","SequenceSlot","Button","HealthBar","UnitHud","EnemyIntent","ActionAvatar","StatusIcon","FloatingText"}) Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"UI/Components/"+part+".prefab"),part);
            Assert.AreEqual(0,AssetDatabase.LoadAssetAtPath<GameObject>(Root+"UI/Components/CardHitRegion.prefab").GetComponentsInChildren<Text>(true).Length,"Hit regions must not inherit button captions");
            Assert.Greater(config.battlePage.transform.Find("Unit Info 0").GetComponent<RectTransform>().anchoredPosition.x,100,"Editor HUD must have an authored battlefield preview position");
            Assert.AreEqual(9,config.battlePage.transform.Find("Hand Faces").childCount);
            Assert.AreEqual(5,config.battlePage.transform.Cast<Transform>().Count(t=>t.name.StartsWith("Slot ")));
            foreach(var id in new[]{"PlayerPlaceholder","SlimePlaceholder","RainbowBossPlaceholder","LavaPlaceholder","PoisonPlaceholder"})
            {
                var unit=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Units/"+id+".prefab").GetComponent<UnitPresentation>();
                Assert.NotNull(unit.visualRoot); Assert.NotNull(unit.launchAnchor); Assert.NotNull(unit.hitAnchor); Assert.NotNull(unit.healthAnchor); Assert.NotNull(unit.groundAnchor);
            }
            Assert.NotNull(config.font.material); Assert.NotNull(config.font.material.mainTexture);
        }
        [Test] public void PrefabLayoutAndStyleRoundTripOutsidePlay()
        {
            const string temp="Assets/Tests/TemporaryAuthoringCheck.prefab";
            var content=PrefabUtility.LoadPrefabContents(Root+"UI/BattlePage.prefab");
            try
            {
                var card=content.transform.Find("Hand Faces").GetChild(0).GetComponent<RectTransform>(); card.anchoredPosition=new Vector2(333,-944);
                card.GetComponentInChildren<Text>().fontSize=29; PrefabUtility.SaveAsPrefabAsset(content,temp);
            }
            finally { PrefabUtility.UnloadPrefabContents(content); }
            try
            {
                var saved=PrefabUtility.LoadPrefabContents(temp);
                try { var card=saved.transform.Find("Hand Faces").GetChild(0).GetComponent<RectTransform>(); Assert.AreEqual(new Vector2(333,-944),card.anchoredPosition); Assert.AreEqual(29,card.GetComponentInChildren<Text>().fontSize); }
                finally { PrefabUtility.UnloadPrefabContents(saved); }
            }
            finally { AssetDatabase.DeleteAsset(temp); }
        }
        static BattleEngine Cast(BattleCatalog data)
        {
            var build=DebugLoadout.Initial(data,data.weapons[0].id); var cards=data.weapons[0].cards.Take(5).ToArray(); build.Preset(data,cards,new[]{"追击","锋锐","穿甲"});
            var engine=new BattleEngine(data,12345,build); engine.Start(0,0); engine.Units[1].hp=1; engine.Units[2].hp=100000; engine.Units[2].panel.hp=100000;
            engine.Player.progress=100; engine.Advance(0); Assert.IsNull(engine.Confirm(cards,engine.Units[1].id)); return engine;
        }
        [Test] public void VisualNodesTrackActualSegmentsPursuitsAndRetargetWithoutChangingRules()
        {
            var original=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset");
            var plain=Object.Instantiate(original); plain.skills=original.skills.Select(s=>JsonUtility.FromJson<SkillDef>(JsonUtility.ToJson(s))).ToList();
            foreach(var skill in plain.skills) skill.presentation=null;
            try
            {
                var withArt=Cast(original); var noArt=Cast(plain);
                Assert.AreEqual(withArt.Rng.Draws,noArt.Rng.Draws); CollectionAssert.AreEqual(withArt.Log,noArt.Log);
                Assert.AreEqual(withArt.TotalCalculatedDamage,noArt.TotalCalculatedDamage);
                var launch=withArt.VisualEvents.Where(e=>e.node==VisualNode.Launch).ToArray();
                CollectionAssert.AreEqual(withArt.Damage.Select(d=>d.target),launch.Select(e=>e.target));
                Assert.IsTrue(launch.Any(e=>e.segment.StartsWith("追击"))); Assert.AreNotEqual(launch.First().target,launch.Last().target);
                Assert.AreEqual(withArt.Damage.Count,withArt.VisualEvents.Count(e=>e.node==VisualNode.Impact));
                Assert.AreEqual(VisualNode.CastStart,withArt.VisualEvents.First().node); Assert.AreEqual(VisualNode.CastEnd,withArt.VisualEvents.Last().node);
                Assert.IsTrue(withArt.VisualEvents.Any(e=>e.node==VisualNode.Death));
            }
            finally { Object.DestroyImmediate(plain); }
        }
        [Test] public void PreviewDoesNotAppendVisualNodesOrConsumeCombatRandom()
        {
            var data=AssetDatabase.LoadAssetAtPath<BattleCatalog>("Assets/Data/BattleCatalog.asset"); var e=new BattleEngine(data,42); e.Start(0,0); var cards=data.weapons[0].cards.Take(5).ToArray();
            int count=e.VisualEvents.Count; long draws=e.Rng.Draws; e.Preview(cards,e.Units[1].id); Assert.AreEqual(count,e.VisualEvents.Count); Assert.AreEqual(draws,e.Rng.Draws);
        }
    }
}
