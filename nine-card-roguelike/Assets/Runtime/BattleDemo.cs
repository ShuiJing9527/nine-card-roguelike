using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Qiling
{
    /// <summary>Presentation and input only. All decisions and calculations live in BattleEngine.</summary>
    public partial class BattleDemo : MonoBehaviour
    {
        public BattleCatalog catalog;
        [Tooltip("每真实秒推进的战斗时间，仅影响等待时长。排谱及敌人行动时均暂停。")]
        public float clockScale = 5;
        public float enemyActionDelay = .8f;
        public GameObject backgroundPrefab;
        Canvas canvas;
        RectTransform root;
        Font font;
        BattleEngine battle;
        public BattleEngine Battle => battle;
        int selectedWeapon, selectedEncounter;
        string seedText = "20261020", message = "";
        List<string> sequence = new List<string>();
        string targetId;
        Text summary, phaseLabel, previewText, logText, turnLabel, notice, detailText, selectionNotice;
        List<Text> unitLabels = new List<Text>();
        List<Image> unitBars = new List<Image>();
        List<Button> targetButtons = new List<Button>(), cardButtons = new List<Button>();
        List<Text> cardLabels = new List<Text>(), slotLabels = new List<Text>();
        List<GameObject> stageObjects = new List<GameObject>();
        readonly List<Material> generatedMaterials = new List<Material>(), unitMaterials = new List<Material>();
        GameObject uiPage, stage;
        float nextEnemyTime, refreshAt;
        bool dirty = true, showAllLogs;
        int detailIndex;
        Color ink => viewConfig != null ? viewConfig.text : Color.white;
        Color muted => viewConfig != null ? viewConfig.muted : Color.gray;
        Color gold => viewConfig != null ? viewConfig.gold : Color.yellow;
        Color blue => viewConfig != null ? viewConfig.player : Color.cyan;
        void Awake()
        {
            Application.targetFrameRate = 60;
            font = viewConfig != null && viewConfig.font != null ? viewConfig.font : Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 20);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (catalog == null) { Debug.LogError("BattleDemo needs BattleCatalog. Run 器灵战斗/生成或打开Demo."); return; }
            InitializeDebugSession(); BuildCanvas(); BuildStage(); InitializePresentation(); ShowSelection();
        }
        void BuildCanvas()
        {
            if (authoredCanvas != null) { canvas = authoredCanvas; root = canvas.GetComponent<RectTransform>(); return; }
            var go = new GameObject("Battle UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform); canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = go.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new Vector2(1920, 1080);
            // Fit the entire design rectangle: interpolating width/height clips the footer on 16:9.
            scale.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            root = go.GetComponent<RectTransform>();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("UI EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); events.transform.SetParent(transform);
            }
        }
        void BuildStage()
        {
            if (authoredStage != null)
            {
                stage = authoredStage;
                var environment = backgroundPrefab != null ? backgroundPrefab : viewConfig != null ? viewConfig.backgroundPrefab : null;
                if(environment != null) { var old=stage.transform.Find("3D Background Replacement Anchor"); if(old!=null) old.gameObject.SetActive(false); var obj=Instantiate(environment,stage.transform); obj.name="Environment Override"; }
                return;
            }
            stage = new GameObject("Replaceable 2.5D Stage"); stage.transform.SetParent(transform);
            var camera = Camera.main;
            if (camera == null) camera = new GameObject("Battle Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.transform.SetParent(stage.transform); camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 5.3f;
            camera.transform.position = new Vector3(0, 7, -12); camera.transform.LookAt(new Vector3(0, 0, 0)); camera.backgroundColor = new Color(.025f, .045f, .08f); camera.clearFlags = CameraClearFlags.SolidColor;
            // Keep URP/3D settings. Later art can replace unit prefabs and add a 3D environment here.
            var lamp = new GameObject("Stage Light", typeof(Light)); lamp.transform.SetParent(stage.transform); lamp.transform.rotation = Quaternion.Euler(45, -30, 0);
            lamp.GetComponent<Light>().type = LightType.Directional; lamp.GetComponent<Light>().intensity = 1.8f;
            if (backgroundPrefab != null) Instantiate(backgroundPrefab, stage.transform);
            else
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "3D Background Replacement Anchor"; floor.transform.SetParent(stage.transform); floor.transform.position = new Vector3(0, -.3f, 0); floor.transform.localScale = new Vector3(18, .3f, 12); Paint(floor, new Color(.075f, .115f, .16f));
            }
        }
        void Paint(GameObject go, Color color)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var material = new Material(shader); material.color = color; r.material = material; generatedMaterials.Add(material);
            }
        }
        RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color? color = null)
        {
            var existing = FindAuthored(parent, name);
            if (existing != null)
            {
                if(viewConfig != null && viewConfig.panelSprite != null && existing.GetComponent<BattleUiElement>().useThemeSprites && (name.Contains("Panel") || name.Contains("Overlay") || name.Contains("Backdrop") || name.Contains("Editor") || name=="Selection" || name=="Battle Header" || name=="Scrollable Text"))
                { var image=existing.GetComponent<Image>(); if(image!=null) image.sprite=viewConfig.panelSprite; }
                return existing;
            }
            var go = viewConfig != null && viewConfig.elementPrefab != null && !authoringLayout ? Instantiate(viewConfig.elementPrefab) : new GameObject(name, typeof(RectTransform)); go.name = name; RegisterElement(go, name); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            if (color.HasValue) { var image = Component<Image>(go); image.color = color.Value; if(viewConfig != null && viewConfig.panelSprite != null) image.sprite = viewConfig.panelSprite; }
            return rt;
        }
        Text Label(Transform parent, string value, float x, float y, float w, float h, int size = 20, Color? color = null)
        {
            var rt = Box(parent, "Text", x, y, w, h); var t = Component<Text>(rt.gameObject); t.text = value; if(t.font==null) t.font=font;
            if (rt.GetComponent<BattleUiElement>().useThemeFont && viewConfig != null && viewConfig.font != null) t.font = viewConfig.font;
            if (Fresh(rt)) { t.font = font; t.fontSize = size; t.color = color ?? ink; t.alignment = TextAnchor.UpperLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false; }

            return t;
        }
        Button Button(Transform parent, string value, float x, float y, float w, float h, Action click, Color? color = null, int size = 19)
        {
            var rt = Box(parent, "Button", x, y, w, h, color ?? new Color(.12f, .2f, .29f));
            rt.name = value; var b = Component<Button>(rt.gameObject); b.onClick.RemoveAllListeners(); b.targetGraphic = rt.GetComponent<Image>();
            var colors = b.colors; if (Fresh(rt)) { colors.highlightedColor = new Color(.66f, .85f, 1); colors.pressedColor = new Color(.45f, .68f, .85f); colors.disabledColor = new Color(.35f, .38f, .42f); b.colors = colors; }
            var text = Label(rt, value, 8, 5, w - 16, h - 10, size); if (Fresh(text.transform)) text.alignment = TextAnchor.MiddleCenter;
            if (viewConfig != null && viewConfig.buttonSprite != null && rt.GetComponent<BattleUiElement>().useThemeSprites) b.image.sprite = viewConfig.buttonSprite;
            b.onClick.AddListener(() => click()); return b;
        }
        Text ScrollText(Transform parent, float x, float y, float w, float h, int fontSize)
        {
            var outer = Box(parent, "Scrollable Text", x, y, w, h, new Color(.035f, .065f, .105f, .97f));
            var scroll = outer.gameObject.GetOrAdd<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36;
            var view = Box(outer, "Viewport", 0, 0, w - 16, h); view.gameObject.GetOrAdd<RectMask2D>();
            var content = Box(view, "Content", 0, 0, w - 26, h);
            var text = Label(content, "", 10, 8, w - 46, h - 16, fontSize);
            // Text owns preferred height; content follows it without a layout rebuild every frame.
            var fitter = text.gameObject.GetOrAdd<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = view; scroll.content = content;
            var barRect = Box(outer, "Scrollbar", w - 13, 0, 11, h, new Color(.1f, .15f, .2f));
            var bar = barRect.gameObject.GetOrAdd<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Box(barRect, "Handle", 0, 0, 11, h, muted);
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.offsetMin = Vector2.zero; handle.offsetMax = Vector2.zero;
            bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>(); scroll.verticalScrollbar = bar;
            return text;
        }
        void SetScroll(Text text, string value, bool bottom = false)
        {
            if (text == null || text.text == value) return;
            text.text = value;
            var rt = text.transform.parent.GetComponent<RectTransform>();
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(30, text.preferredHeight + 22));
            if (bottom) rt.parent.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 0;
        }
        void NewPage(float width = 1600, float height = 1000)
        {
            HideHover(); ResetPresentation();
            if (uiPage != null)
            {
                foreach (var panel in new[] { settingsPanel, previewPanel, traitOverlay }) if(panel != null) { panel.SetActive(false); DisposeView(panel); }
                settingsPanel = previewPanel = traitOverlay = null;
                uiPage.SetActive(false);
            }
            bindCounts.Clear();
            var authored = width > 1700 ? authoredBattle : authoredSelection;
            if (!authoringLayout && authored != null) { uiPage = authored; uiPage.SetActive(true); ResetBoundTree(uiPage); return; }
            if (!authoringLayout && viewConfig != null)
            {
                var prefab = width > 1700 ? viewConfig.battlePage : viewConfig.selectionPage;
                if (prefab != null) { uiPage = Instantiate(prefab, root); uiPage.SetActive(true); ResetBoundTree(uiPage); return; }
            }
            var page = Box(root, "Page", 0, 0, width, height);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f); page.pivot = new Vector2(.5f, .5f); page.anchoredPosition = Vector2.zero;
            uiPage = page.gameObject;
        }

        void ShowSelection()
        {
            battle = null; showSettings = showPreview = battlePaused = false; NewPage(); ClearUnitVisuals();
            var page = Box(uiPage.transform, "Selection", 0, 0, 1600, 1000, new Color(.025f, .045f, .08f, .98f));
            Label(page, "器 灵 战 斗", 70, 45, 900, 65, 44, gold);
            Label(page, "规则测试 Demo  /  九张实体卡 · 五槽排谱 · 行动条战斗", 73, 120, 1400, 40, 24, muted);
            Label(page, "01  选择器灵", 70, 195, 1000, 42, 27);
            for (int i = 0; i < catalog.weapons.Count; i++)
            {
                int index = i; var weapon = catalog.weapons[i]; var p = weapon.panel;
                var option = Button(page, weapon.title + (i == selectedWeapon ? "  ✓" : "") + $"\n生命 {p.hp}   速度 {p.speed}\n物攻 {p.attack} / 特攻 {p.specialAttack}\n物防 {p.defense} / 特防 {p.specialDefense}", 70 + i * 295, 250, 275, 150, () => { selectedWeapon = index; SaveProfiles(); ShowSelection(); }, i == selectedWeapon ? new Color(.19f, .36f, .43f) : (Color?)null, 22); Choice(option.gameObject,i==selectedWeapon);
            }
            Label(page, "02  遭遇与复现种子", 70, 435, 1000, 40, 27);
            for (int i = 0; i < catalog.encounters.Count; i++)
            {
                int index = i; var option = Button(page, catalog.encounters[i].title + (i == selectedEncounter ? "  ✓" : ""), 70 + i * 260, 490, 240, 55, () => { selectedEncounter = index; SaveProfiles(); ShowSelection(); }, i == selectedEncounter ? new Color(.19f, .36f, .43f) : (Color?)null); Choice(option.gameObject,i==selectedEncounter);
            }
            Label(page, "随机种子（整数）", 70, 577, 240, 36, 22);
            var fieldRect = Box(page, "Seed", 320, 570, 340, 52, new Color(.12f, .18f, .25f));
            var field = fieldRect.gameObject.GetOrAdd<InputField>(); field.onValueChanged.RemoveAllListeners(); field.textComponent = Label(fieldRect, seedText, 12, 6, 316, 40, 25); field.SetTextWithoutNotify(seedText); field.contentType = InputField.ContentType.IntegerNumber;
            field.onValueChanged.AddListener(v => seedText = v);
            Button(page, "词条测试", 690, 570, 190, 65, OpenTraitEditor, new Color(.28f, .23f, .39f), 24);
            Button(page, "导出测试配置", 900, 570, 195, 65, ExportLog, null, 21);
            Button(page, "开始战斗", 1120, 570, 355, 65, StartBattle, new Color(.23f, .42f, .36f), 27);
            selectionNotice = Label(page, message, 70, 640, 1450, 27, 18, gold);
            var notes = ScrollText(page, 70, 670, 1400, 270, 20);
            SetScroll(notes, "本版测试边界\n" + string.Join("\n", catalog.pending.Select(x => "• " + x)));
        }
        void StartBattle()
        {
            int seed; if (!int.TryParse(seedText, out seed)) { seedText = "20261020"; seed = 20261020; }
            var profile = CurrentProfile;
            battle = new BattleEngine(catalog, seed, profile); battle.Start(selectedWeapon, selectedEncounter);
            sequence = new List<string>(profile.sequence); targetId = profile.target; ChooseDefaultTarget(); detailIndex = 0;
            showAllLogs = false; showTraitEditor = false; message = "已加载测试词条与最近排谱；点击词条测试可免费修改。";
            SaveProfiles(); BuildBattlePage(); SpawnUnits(); dirty = true;
        }
        void SpawnUnits()
        {
            ClearUnitVisuals();
            for (int i = 0; i < battle.Units.Count; i++)
            {
                var u = battle.Units[i]; GameObject prefab = u.player ? catalog.weapons[selectedWeapon].visualPrefab : catalog.monsters.Find(x => x.id == u.monsterId).visualPrefab;
                if (prefab == null && viewConfig != null) prefab = u.player ? viewConfig.playerPlaceholder : viewConfig.enemyPlaceholder;
                var obj = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(u.player ? PrimitiveType.Capsule : PrimitiveType.Sphere);
                obj.name = u.title + " [Visual Replacement]"; obj.transform.SetParent(stage.transform);
                // Place units in the upper third of the view; front/back remain explicit in UI and world depth.
                obj.transform.position = unitPositions != null && i < unitPositions.Length && unitPositions[i] != null ? unitPositions[i].position : u.player ? new Vector3(-5f, 1.1f, 2f) : new Vector3(2.4f + (i - 1) * 3.1f, .8f, u.backRow ? 3f : 2f);
                if (!u.player && prefab == null) obj.transform.localScale = new Vector3(u.monsterId == "彩虹史莱姆" ? 2.2f : 1.45f, u.monsterId == "彩虹史莱姆" ? 1.8f : 1f, 1.4f);
                if (prefab == null)
                {
                    Paint(obj, u.player ? blue : u.monsterId == "彩虹史莱姆" ? new Color(.64f, .5f, .92f) : u.monsterId == "熔岩史莱姆" ? new Color(.95f, .3f, .12f) : new Color(.4f, .78f, .47f));
                    unitMaterials.AddRange(obj.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial));
                }
                var visual = obj.GetComponent<UnitPresentation>(); if (visual == null) visual = obj.AddComponent<UnitPresentation>(); visual.Initialize();
                stageObjects.Add(obj);
            }
        }
        static string MatchName(MatchType type) => type == MatchType.Offense ? "攻势" : type == MatchType.Survival ? "生存" : type == MatchType.Status ? "状态" : type == MatchType.Universal ? "通用" : type == MatchType.Pending ? "待定" : "攻势/生存";
        static string DamageName(DamageType type) => type == DamageType.Physical ? "物理" : type == DamageType.Special ? "特殊" : "变化";
        static string ReachName(Reach reach) => reach == Reach.Melee ? "近战" : reach == Reach.Ranged ? "远程" : "非攻击";
        static string KindName(EffectKind kind)
        {
            switch (kind)
            {
                case EffectKind.Poison: return "中毒"; case EffectKind.Burn: return "灼伤"; case EffectKind.Bleed: return "流血(待定)"; case EffectKind.Block: return "招架"; case EffectKind.Weakness: return "虚弱"; case EffectKind.Evade: return "闪避"; case EffectKind.ShieldBoost: return "护盾强化"; case EffectKind.Reflect: return "棘甲"; case EffectKind.DebuffImmune: return "减益免疫"; case EffectKind.AttackUp: return "物攻↑/反击"; case EffectKind.SpecialAttackUp: return "特攻↑"; case EffectKind.SpeedPercent: case EffectKind.SpeedUp: return "速度↑"; case EffectKind.DefensePercent: case EffectKind.DefenseUp: return "物防变动"; case EffectKind.SpecialDefensePercent: case EffectKind.SpecialDefenseUp: return "特防变动"; case EffectKind.MeleeReduction: return "近战减伤"; default: return kind.ToString();
            }
        }
        void AddCard(int index)
        {
            detailIndex = index; UpdateDetail();
            if (battle.Active != battle.Player || battle.Finished) { message = "尚未轮到玩家；可先查看卡牌详情。"; dirty = true; return; }
            string id = catalog.weapons[selectedWeapon].cards[index];
            if (sequence.Contains(id)) { message = "这张实体卡已在排谱中，可在槽位调整或移除。"; dirty = true; return; }
            if (sequence.Count >= 5) { message = "排谱已满五张，请先移除一张。"; dirty = true; return; }
            sequence.Add(id); ChooseDefaultTarget(); RememberComposition(); message = ""; dirty = true;
        }
        void Move(int index, int direction)
        {
            if (index >= sequence.Count || index + direction < 0 || index + direction >= sequence.Count) return;
            string id = sequence[index]; sequence[index] = sequence[index + direction]; sequence[index + direction] = id; ChooseDefaultTarget(); RememberComposition(); message = ""; dirty = true;
        }
        void Remove(int index) { if (index < sequence.Count) { sequence.RemoveAt(index); ChooseDefaultTarget(); RememberComposition(); message = ""; dirty = true; } }
        void ChooseDefaultTarget()
        {
            if (sequence.Count == 0) { targetId = null; return; }
            var skill = catalog.Skill(catalog.Card(sequence[0]).skill); var legal = battle.LegalTargets(battle.Player, skill);
            if (!legal.Any(u => u.id == targetId)) targetId = legal.FirstOrDefault()?.id;
        }
        void Confirm()
        {
            RememberComposition();
            string result = battle.Confirm(sequence, targetId);
            if (result == null) { message = "出招结算完毕；排谱保留，可再次使用或修改。"; }
            else message = result;
            dirty = true;
        }
        void UpdateDetail()
        {
            var card = CurrentCard(catalog.weapons[selectedWeapon].cards[detailIndex]); var skill = catalog.Skill(card.skill);
            SetScroll(detailText, $"{skill.title}\n{MatchName(card.match)} / {DamageName(skill.damageType)} / {ReachName(skill.reach)}\n\n{skill.description}\n\n" + string.Join("\n\n", card.traits.Select(id => { var t = catalog.Trait(id); return $"词条：{t.title}〔{MatchName(t.match)}〕\n{t.description}\n{t.note}"; })) + "\n\n" + skill.note);
        }
        void Update()
        {
            UpdateHover(); TickPresentation();
            if (battle == null || showAllLogs || showTraitEditor || showSettings || showPreview || battlePaused) { if (battle != null && dirty && !showAllLogs && !showTraitEditor) { Refresh(); dirty = false; } return; }
            string before = battle.ActiveId; int serial = battle.ActionSerial;
            if (!battle.Finished)
            {
                if (!PresentationBusy && battle.Active == null) battle.Advance(Time.unscaledDeltaTime * clockScale);
                if (!PresentationBusy && battle.Active != null && !battle.Active.player)
                {
                    if (before != battle.ActiveId) nextEnemyTime = Time.unscaledTime + enemyActionDelay;
                    if (Time.unscaledTime >= nextEnemyTime) { battle.EnemyAction(); dirty = true; }
                }
            }
            if (serial != battle.ActionSerial || before != battle.ActiveId) { if (battle.Active == battle.Player) { ChooseDefaultTarget(); message = ""; } dirty = true; }
            if (showAllLogs) return;
            if (dirty || Time.unscaledTime >= refreshAt) { Refresh(); dirty = false; refreshAt = Time.unscaledTime + .12f; }
        }
        public void ExportLog()
        {
            try { string path = ExportTestBundle(); message = "已导出配置和日志（路径已复制）：" + path; Debug.Log(message); GUIUtility.systemCopyBuffer = path; }
            catch (Exception error) { message = "导出失败：" + error.Message; }
            if (battle == null && selectionNotice != null) selectionNotice.text = message;
            dirty = true;
        }
        void ClearUnitVisuals()
        {
            foreach (var obj in stageObjects) if (obj != null) DisposeView(obj); stageObjects.Clear();
            foreach (var mat in unitMaterials) { generatedMaterials.Remove(mat); if (mat != null) Destroy(mat); } unitMaterials.Clear();
        }
        void OnDestroy() { foreach (var mat in generatedMaterials) if (mat != null) Destroy(mat); }
    }
}
