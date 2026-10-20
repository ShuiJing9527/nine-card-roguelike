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
    public class DemoFlowTests
    {
        static readonly string captureDirectory = Path.GetFullPath("../Validation/BattleLayout/" + System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
        [UnitySetUp] public IEnumerator IsolateSavedBuilds()
        {
            DebugFiles.RootOverride = Path.GetFullPath("../Validation/DebugTestFiles/" + System.Guid.NewGuid().ToString("N"));
            yield return null;
        }
        [UnityTearDown] public IEnumerator ResetStorage() { DebugFiles.RootOverride = null; yield return null; }
        internal static Button Find(string text)
        {
            if (text.Length > 2 && char.IsDigit(text[0]) && text[1] == '.')
            {
                int index = text[0] - '1';
                Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.gameObject.activeInHierarchy && t.text.StartsWith(text)));
                return GameObject.Find("Hand Card " + index).GetComponent<Button>();
            }
            return Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>() != null && b.GetComponentInChildren<Text>().text.Contains(text));
        }
        static void Settings() { Find("设置").onClick.Invoke(); }
        static void Reforge() { Find(Object.FindFirstObjectByType<BattleDemo>().Battle == null ? "词条测试" : "自由重铸").onClick.Invoke(); }
        internal static void Capture(string name, int width = 1920, int height = 1080, System.Action<Canvas> inspect = null)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var camera = Camera.main; var canvas = Object.FindFirstObjectByType<Canvas>();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); rt.Create();
            var uiTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); uiTarget.Create();
            Rect oldRect = camera.rect;
            var uiCamera = new GameObject("Capture UI Camera", typeof(Camera)).GetComponent<Camera>();
            uiCamera.targetTexture = uiTarget; uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = Color.clear;
            uiCamera.allowHDR = false; uiCamera.allowMSAA = false; uiCamera.cullingMask = 1 << 5; uiCamera.orthographic = true; uiCamera.transform.position = new Vector3(0,0,-100);
            camera.targetTexture = rt; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1;
            canvas.GetComponent<CanvasScaler>().SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            foreach (var label in canvas.GetComponentsInChildren<Text>()) label.SetAllDirty();
            Canvas.ForceUpdateCanvases(); Object.FindFirstObjectByType<BattleDemo>().SendMessage("LateUpdate"); camera.Render(); uiCamera.Render(); inspect?.Invoke(canvas);
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            var worldPixels = image.GetPixels32(); RenderTexture.active = uiTarget;
            var overlay = new Texture2D(width, height, TextureFormat.RGBA32, false); overlay.ReadPixels(new Rect(0, 0, width, height), 0, 0); overlay.Apply();
            var uiPixels = overlay.GetPixels32();
            for (int i = 0; i < worldPixels.Length; i++)
            {
                float remaining = 1 - uiPixels[i].a / 255f;
                worldPixels[i] = new Color32((byte)Mathf.Clamp(uiPixels[i].r + worldPixels[i].r * remaining, 0, 255), (byte)Mathf.Clamp(uiPixels[i].g + worldPixels[i].g * remaining, 0, 255), (byte)Mathf.Clamp(uiPixels[i].b + worldPixels[i].b * remaining, 0, 255), 255);
            }
            image.SetPixels32(worldPixels); image.Apply();
            Directory.CreateDirectory(captureDirectory); File.WriteAllBytes(Path.Combine(captureDirectory, name + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null; camera.rect = oldRect; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            canvas.GetComponent<CanvasScaler>().SendMessage("Update", SendMessageOptions.DontRequireReceiver);
            foreach (var label in canvas.GetComponentsInChildren<Text>()) label.SetAllDirty();
            Canvas.ForceUpdateCanvases(); Object.FindFirstObjectByType<BattleDemo>().SendMessage("LateUpdate");
            Object.Destroy(uiCamera.gameObject); Object.Destroy(overlay); Object.Destroy(image); Object.Destroy(uiTarget); Object.Destroy(rt);
        }
        [UnityTest] public IEnumerator CanChooseWeaponComposeTargetConfirmRetryAndReturn()
        {
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null;
            var demo = Object.FindFirstObjectByType<BattleDemo>(); Assert.NotNull(demo); Assert.AreEqual(5, demo.catalog.weapons.Count);
            Capture("selection");
            Object.FindFirstObjectByType<InputField>().text = "12345";
            Find("开始战斗").onClick.Invoke(); yield return null;
            Assert.NotNull(demo.Battle); Assert.AreEqual(3, demo.Battle.Units.Count); Assert.AreEqual(12345, demo.Battle.Rng.Seed);
            demo.clockScale = 1000; demo.enemyActionDelay = .01f;
            for (int i = 0; i < 120 && demo.Battle.Active != demo.Battle.Player; i++) yield return null;
            Assert.AreSame(demo.Battle.Player, demo.Battle.Active);
            foreach (var text in new[] { "2. 破风箭", "1. 连射", "3. 多重扫射", "4. 疾风护盾", "6. 风之驱散" }) { Find(text).onClick.Invoke(); yield return null; }
            Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("总连锁：5")));
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.transform.parent.name == "Slot 0" && b.GetComponentInChildren<Text>().text == "→").onClick.Invoke(); yield return null;
            Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.StartsWith("1号位") && t.text.Contains("连射")));
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.transform.parent.name == "Slot 1" && b.GetComponentInChildren<Text>().text == "←").onClick.Invoke(); yield return null;
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.transform.parent.name == "Slot 4" && b.GetComponentInChildren<Text>().text == "移除").onClick.Invoke(); yield return null;
            Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("总连锁：4")));
            Find("6. 风之驱散").onClick.Invoke(); yield return null;
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.transform.parent.name == "Unit 2").onClick.Invoke(); yield return null;
            Assert.IsTrue(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.StartsWith("当前目标：彩虹史莱姆")));
            Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b => b.transform.parent.name == "Unit 1").onClick.Invoke(); yield return null;
            // Optional visual artifacts, captured only in a graphics-enabled PlayMode run.
            Capture("battle-demo");
            Find("确认出招").onClick.Invoke(); yield return null;
            Assert.AreEqual(1, demo.Battle.Player.actions); Assert.IsTrue(demo.Battle.Log.Any(l => l.Contains("排谱：")));
            Find("查看完整").onClick.Invoke(); yield return null; Find("返回战斗").onClick.Invoke(); yield return null;
            Settings(); yield return null; Find("同种子重试").onClick.Invoke(); yield return null; Assert.AreEqual(0, demo.Battle.Player.actions); Assert.AreEqual(100, demo.Battle.Player.hp);
            Settings(); yield return null; Find("更换武器").onClick.Invoke(); yield return null; Assert.IsNull(demo.Battle); Assert.NotNull(Find("开始战斗"));
        }
        [UnityTest] public IEnumerator FreeReforgePreservesOrderRetriesProfilesAndExportsReplay()
        {
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null;
            var demo = Object.FindFirstObjectByType<BattleDemo>(); Find("开始战斗").onClick.Invoke(); yield return null;
            demo.clockScale = 1000;
            for (int i = 0; i < 120 && demo.Battle.Active != demo.Battle.Player; i++) yield return null;
            foreach (var text in new[] { "1. 连射", "2. 破风箭", "3. 多重扫射", "4. 疾风护盾", "5. 灵巧闪避" }) { Find(text).onClick.Invoke(); yield return null; }
            var order = demo.CurrentSequence.ToArray(); int seed = demo.Battle.Rng.Seed; long draws = demo.Battle.Rng.Draws;
            Reforge(); yield return null;
            Find("五卡：追击＋锋锐＋穿甲").onClick.Invoke(); yield return null;
            Assert.AreEqual(1, demo.Battle.Card(order[0]).traits.Count); // Draft is isolated until applied.
            Canvas.ForceUpdateCanvases();
            var canvasRect = Object.FindFirstObjectByType<Canvas>().GetComponent<RectTransform>();
            foreach (string caption in new[] { "保存，下次开战生效", "保存并应用当前排谱", "返回（不保存）", "恢复本武器初始预组词条" })
            {
                var corners = new Vector3[4]; Find(caption).GetComponent<RectTransform>().GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector3 local = canvasRect.InverseTransformPoint(corner);
                    Assert.IsTrue(canvasRect.rect.Contains(new Vector2(local.x, local.y)), caption + "必须完整位于可见画布内");
                }
            }
            Capture("trait-test-editor-16x9", 1920, 1080);
            Find("保存并应用当前排谱").onClick.Invoke(); yield return null;
            CollectionAssert.AreEqual(order, demo.CurrentSequence); Assert.AreEqual("连射", demo.Battle.Plan(demo.Battle.Player, order).skill.id);
            Assert.AreEqual(15, demo.Battle.Plan(demo.Battle.Player, order).pursuits.Sum(g => g.count)); Assert.AreEqual(draws, demo.Battle.Rng.Draws);
            foreach (var id in order) CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, demo.Battle.Card(id).traits);
            Find("确认出招").onClick.Invoke(); yield return null;
            string json = demo.ExportTestBundle(); Assert.IsTrue(File.Exists(json)); Assert.IsTrue(File.Exists(Path.ChangeExtension(json, ".txt")));
            var bundle = JsonUtility.FromJson<DebugTestBundle>(File.ReadAllText(json)); Assert.AreEqual(seed, bundle.replay.seed); Assert.AreEqual(1, bundle.replay.steps.Count(s => s.kind == "action")); Assert.Greater(bundle.damage.Count, 0); Assert.IsTrue(bundle.damage.All(d => d.calculated == d.absorbed + d.hpLost + d.overkill));
            Settings(); yield return null; Find("同种子重试").onClick.Invoke(); yield return null;
            Assert.AreEqual(seed, demo.Battle.Rng.Seed); Assert.AreEqual(100, demo.Battle.Player.hp); Assert.AreEqual(0, demo.Battle.Player.shield); Assert.AreEqual(0, demo.Battle.Player.states.Count); Assert.AreEqual(440, demo.Battle.Units[2].hp); Assert.AreEqual(0, demo.Battle.Player.actions); CollectionAssert.AreEqual(order, demo.CurrentSequence);
            Settings(); yield return null; Find("更换种子重试").onClick.Invoke(); yield return null; Assert.AreNotEqual(seed, demo.Battle.Rng.Seed); CollectionAssert.AreEqual(order, demo.CurrentSequence); CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, demo.Battle.Card(order[0]).traits);
            Settings(); yield return null; Find("更换武器").onClick.Invoke(); yield return null; Find("大剑").onClick.Invoke(); yield return null;
            Reforge(); yield return null;
            Find("锋锐〔攻势〕").onClick.Invoke(); yield return null; Find("填入卡1的词条1").onClick.Invoke(); yield return null; Find("保存并返回准备界面").onClick.Invoke(); yield return null;
            Find("猎弓").onClick.Invoke(); yield return null; Find("开始战斗").onClick.Invoke(); yield return null;
            CollectionAssert.AreEqual(order, demo.CurrentSequence); CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, demo.Battle.Card(order[0]).traits);
            Reforge(); yield return null; Find("恢复本武器初始预组词条").onClick.Invoke(); yield return null;
            // Restore is also a draft; cancel keeps the test build.
            Find("返回（不保存）").onClick.Invoke(); yield return null; Assert.AreEqual(3, demo.Battle.Card(order[0]).traits.Count);
            // A fresh scene demonstrates persistence beyond a live UI instance.
            SceneManager.LoadScene("BattleDemo"); yield return null; yield return null; demo = Object.FindFirstObjectByType<BattleDemo>();
            CollectionAssert.AreEqual(new[] { "追击", "锋锐", "穿甲" }, demo.SavedProfile("猎弓").cards[0].traits);
        }
    }
}
