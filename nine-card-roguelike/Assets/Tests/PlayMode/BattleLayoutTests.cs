using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Qiling.Tests
{
    public class BattleLayoutTests
    {
        BattleDemo demo;
        [UnitySetUp] public IEnumerator StartBattle()
        {
            DebugFiles.RootOverride = Path.GetFullPath("../Validation/DebugTestFiles/" + Guid.NewGuid().ToString("N"));
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null;
            demo = UnityEngine.Object.FindFirstObjectByType<BattleDemo>(); DemoFlowTests.Find("开始战斗").onClick.Invoke(); yield return null;
            demo.clockScale = 1000;
            for (int i = 0; i < 120 && demo.Battle.Active != demo.Battle.Player; i++) yield return null;
            Assert.AreSame(demo.Battle.Player, demo.Battle.Active); demo.clockScale = 5;
        }
        [UnityTearDown] public IEnumerator Cleanup() { DebugFiles.RootOverride = null; yield return null; }
        static RectTransform Rect(string name) => GameObject.Find(name).GetComponent<RectTransform>();
        static GameObject Raycast(Vector2 point)
        {
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }
        static Vector2 ScreenPoint(RectTransform rt, Vector2 local)
        {
            var canvas = rt.GetComponentInParent<Canvas>(); return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rt.TransformPoint(local));
        }
        [UnityTest] public IEnumerator FanHoverHasStableDistinctClickableBandsAndScrollableDetails()
        {
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 9; i++) Assert.AreEqual("Hand Card " + i, Raycast(ScreenPoint(Rect("Hand Card " + i), Vector2.zero))?.name);
            var first = Rect("Hand Card 0").GetComponent<HandCardInteraction>(); Vector2 rest = first.face.anchoredPosition;
            first.OnPointerEnter(new PointerEventData(EventSystem.current)); yield return null;
            DemoFlowTests.Capture("card-hover-1920x1080");
            yield return null; // Restore native overlay batches after the offscreen capture before raycasting.
            Assert.IsTrue(first.Raised); Assert.Greater(first.face.anchoredPosition.y, rest.y); Assert.Greater(first.face.localScale.x, 1);
            Assert.NotNull(GameObject.Find("Hover Details"));
            Assert.IsTrue(GameObject.Find("Hover Details").GetComponentsInChildren<Text>().Any(t => t.text.Contains("连射") && t.text.Contains("每箭造成物攻60%")));
            // Enlarged face overlaps the next card visually, but cannot steal its stable band.
            Vector2 boundary = ScreenPoint(Rect("Hand Card 0"), new Vector2(80, -20)); Assert.AreEqual("Hand Card 1", Raycast(boundary)?.name);
            var raisedPoint = ScreenPoint(Rect("Hand Card 0"), new Vector2(0, 165)); Assert.AreEqual("Hand Card 0", Raycast(raisedPoint)?.name);
            first.OnPointerExit(new PointerEventData(EventSystem.current)); yield return null;
            Assert.IsFalse(first.Raised); Assert.AreEqual(rest, first.face.anchoredPosition);
            var card = Raycast(ScreenPoint(Rect("Hand Card 4"), Vector2.zero)).GetComponent<Button>(); card.onClick.Invoke(); yield return null;
            CollectionAssert.AreEqual(new[] { demo.catalog.weapons[0].cards[4] }, demo.CurrentSequence);
        }
        [UnityTest] public IEnumerator SharedRailReadsProgressAndSeparatesCoincidentPortraitsWithoutChangingRules()
        {
            DemoFlowTests.Find("暂停").onClick.Invoke(); yield return null;
            for (int i = 0; i < 3; i++) demo.Battle.Units[i].progress = i * 50;
            yield return null;
            var rail = Rect("Shared Action Rail"); var corners = new Vector3[4]; rail.GetWorldCorners(corners);
            for (int i = 0; i < 3; i++)
            {
                var avatar = Rect("Action Avatar " + i); Vector3 actual = avatar.TransformPoint(avatar.rect.center);
                Assert.AreEqual(Mathf.Lerp(corners[0].x, corners[3].x, i / 2f), actual.x, .5f);
            }
            foreach (var u in demo.Battle.Units) u.progress = 100; yield return null;
            var positions = Enumerable.Range(0, 3).Select(i => Rect("Action Avatar " + i).anchoredPosition).ToArray();
            Assert.AreEqual(3, positions.Distinct().Count()); Assert.AreEqual(0, demo.Battle.Player.actions);
            for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++) Assert.GreaterOrEqual(Mathf.Abs(positions[i].y - positions[j].y), 43.9f);
            demo.Battle.Units[1].hp = 0; yield return null;
            Assert.AreEqual(44, Rect("Action Avatar 2").anchoredPosition.y - Rect("Action Avatar 0").anchoredPosition.y, .1f, "倒下单位不应占用头像避让位置");
        }
        static void Visible(Canvas canvas, RectTransform rt)
        {
            var root = canvas.GetComponent<RectTransform>(); var corners = new Vector3[4]; rt.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                Vector2 p = root.InverseTransformPoint(c);
                Assert.IsTrue(root.rect.Contains(p), rt.name + "超出可见画布：" + p);
            }
        }
        [UnityTest] public IEnumerator LayoutFitsMultipleAspectRatiosAndModalBlocksBattleInput()
        {
            foreach (var card in new[] { "1. 连射", "2. 破风箭", "3. 多重扫射", "4. 疾风护盾", "5. 灵巧闪避" }) { DemoFlowTests.Find(card).onClick.Invoke(); yield return null; }
            foreach (var size in new[] { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1280,800), new Vector2Int(1024,768), new Vector2Int(2560,1080) })
            {
                DemoFlowTests.Capture("battle-layout-" + size.x + "x" + size.y, size.x, size.y, canvas =>
                {
                    foreach (string button in new[] { "确认出招", "清空", "查看完整战斗日志", "自由重铸", "设置" }) Visible(canvas, DemoFlowTests.Find(button).GetComponent<RectTransform>());
                    for (int i = 0; i < 9; i++) Visible(canvas, Rect("Card Face " + i));
                    for (int i = 0; i < 5; i++) Visible(canvas, Rect("Slot " + i));
                    for (int i = 0; i < 3; i++)
                    {
                        Visible(canvas, Rect("Unit Info " + i)); Visible(canvas, Rect("Intent " + i)); Visible(canvas, Rect("Action Avatar " + i));
                        var model = GameObject.Find(demo.Battle.Units[i].title + " [Visual Replacement]"); var bounds = model.GetComponentInChildren<Renderer>().bounds;
                        Vector2 foot = Camera.main.WorldToScreenPoint(bounds.center - Vector3.up * bounds.extents.y);
                        Vector2 head = Camera.main.WorldToScreenPoint(bounds.center + Vector3.up * bounds.extents.y);
                        var info = Rect("Unit Info " + i); var intent = Rect("Intent " + i);
                        Assert.Less(ScreenPoint(info, new Vector2(0, info.rect.yMax)).y, foot.y, "血条必须位于单位下方");
                        Assert.Greater(ScreenPoint(intent, new Vector2(0, intent.rect.yMin)).y, head.y, "意图必须位于单位上方");
                    }
                });
            }
            long draws = demo.Battle.Rng.Draws; DemoFlowTests.Find("出招预览").onClick.Invoke(); yield return null;
            Assert.NotNull(GameObject.Find("Preview Panel")); Assert.AreEqual(draws, demo.Battle.Rng.Draws);
            Assert.AreNotEqual("Hand Card 4", Raycast(ScreenPoint(Rect("Hand Card 4"), Vector2.zero))?.name);
            DemoFlowTests.Find("返回战斗").onClick.Invoke(); yield return null;
            DemoFlowTests.Find("设置").onClick.Invoke(); yield return null;
            Assert.NotNull(GameObject.Find("Settings Panel")); DemoFlowTests.Find("返回战斗").onClick.Invoke(); yield return null;
            DemoFlowTests.Find("自由重铸").onClick.Invoke(); yield return null;
            Assert.NotNull(GameObject.Find("Trait Test Editor")); DemoFlowTests.Find("返回（不保存）").onClick.Invoke(); yield return null;
            DemoFlowTests.Find("确认出招").onClick.Invoke(); yield return null; Assert.AreEqual(1, demo.Battle.Player.actions);
        }
    }
}
