using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qiling
{
    public partial class BattleDemo
    {
        sealed class UnitView
        {
            public RectTransform body, below, above, avatar;
            public Image hp;
            public Text name, life, state, intent, progress;
            public RectTransform statusIcons;
            public GameObject selected;
        }
        readonly List<UnitView> unitViews = new List<UnitView>();
        readonly List<HandCardInteraction> handInteractions = new List<HandCardInteraction>();
        readonly List<RectTransform> slotRoots = new List<RectTransform>();
        readonly List<Text> cardKinds = new List<Text>(), cardTraits = new List<Text>();
        GameObject hoverPanel, settingsPanel, previewPanel;
        Text hoverText, settingsSummary;
        Button confirmButton;
        Func<string> hoverContent;
        bool hoverInside, showSettings, showPreview, battlePaused;
        float hideHoverAt;
        const float RailStart = 390, RailLength = 1030;

        RectTransform CenterBox(Transform parent, string title, float width, float height, Color color)
        {
            var rt = Box(parent, title, 0, 0, width, height, color);
            if (!Fresh(rt)) return rt;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = Vector2.zero; return rt;
        }
        RectTransform ModalShade(string title)
        {
            HideHover();
            var page = uiPage.GetComponent<RectTransform>();
            return CreateAuthoredModal(page, title);
        }
        static void Decorative(Transform parent)
        {
            foreach (var graphic in parent.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
        }
        void Border(Transform parent, float width, float height, Color color, float thickness = 2)
        {
            Box(parent, "Border Top", 0, 0, width, thickness, color);
            Box(parent, "Border Bottom", 0, height - thickness, width, thickness, color);
            Box(parent, "Border Left", 0, 0, thickness, height, color);
            Box(parent, "Border Right", width - thickness, 0, thickness, height, color);
        }
        void BuildBattlePage()
        {
            NewPage(1920, 1080); var page = uiPage.transform;
            unitViews.Clear(); unitLabels.Clear(); unitBars.Clear(); targetButtons.Clear(); cardButtons.Clear(); cardLabels.Clear(); slotLabels.Clear(); slotRoots.Clear(); cardKinds.Clear(); cardTraits.Clear(); handInteractions.Clear();
            detailText = previewText = null; showSettings = showPreview = battlePaused = false;
            Box(page, "Battle Header", 0, 0, 1920, 142, new Color(.025f, .045f, .075f, .97f));
            Label(page, "器灵战斗", 28, 17, 285, 50, 36, gold);
            Label(page, catalog.encounters[selectedEncounter].title, 30, 80, 285, 32, 22, muted);
            Label(page, "公共行动轴", 355, 11, 220, 29, 21, gold);
            var rail = Box(page, "Shared Action Rail", RailStart, 76, RailLength, 4, gold); rail.GetComponent<Image>().raycastTarget = false;
            for (int i = 0; i <= 4; i++)
            {
                Box(page, "Progress Tick", RailStart + RailLength * i / 4, 69, 2, 18, muted).GetComponent<Image>().raycastTarget = false;
                Label(page, i == 4 ? "100% 行动" : (i * 25) + "%", RailStart - 18 + RailLength * i / 4, 110, 130, 28, 18, muted);
            }
            Button(page, "自由重铸", 1515, 25, 180, 54, OpenTraitEditor, new Color(.23f, .2f, .31f), 22);
            Button(page, "设置", 1708, 25, 102, 54, OpenSettings, null, 22);
            var pause = Button(page, "暂停", 1822, 25, 76, 54, () => { battlePaused = !battlePaused; dirty = true; }, null, 19);
            pause.gameObject.name = "Pause Battle";

            for (int i = 0; i < battle.Units.Count; i++) BuildUnitView(i);

            Box(page, "Hand Backdrop", 0, 816, 1920, 264, new Color(.025f, .045f, .075f, .95f));
            turnLabel = Label(page, "", 470, 615, 980, 38, 27, gold); if (Fresh(turnLabel.transform)) turnLabel.alignment = TextAnchor.MiddleCenter;
            phaseLabel = Label(page, "", 490, 651, 940, 30, 19, muted); if (Fresh(phaseLabel.transform)) phaseLabel.alignment = TextAnchor.MiddleCenter;
            Label(page, "排谱", 330, 709, 100, 37, 25, gold);
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                var slot = Box(page, "Slot " + i, 450 + i * 202, 690, 190, 120, new Color(.065f, .115f, .17f, .98f)); slotRoots.Add(slot);
                Border(slot, 190, 120, i == 0 ? gold : new Color(.24f, .37f, .48f));
                slotLabels.Add(Label(slot, "", 10, 8, 170, 68, 20, i == 0 ? gold : ink));
                Button(slot, "←", 8, 84, 39, 28, () => Move(index, -1), null, 17);
                Button(slot, "移除", 53, 84, 84, 28, () => Remove(index), null, 17);
                Button(slot, "→", 143, 84, 39, 28, () => Move(index, 1), null, 17);
                slot.gameObject.GetOrAdd<BattleHover>().Changed = entered =>
                {
                    if (entered && index < sequence.Count) ShowHover(() => CardDescription(sequence[index]) + "\n\n" + battle.Preview(sequence, targetId), 460 + index * 165, 278);
                    else ScheduleHideHover();
                };
            }
            BuildFanHand(page);
            Button(page, "查看完整战斗日志", 22, 1004, 195, 50, ShowLog, null, 19);
            Button(page, "出招预览", 1680, 864, 215, 48, OpenPreview, null, 22);
            confirmButton = Button(page, "确认出招", 1665, 928, 235, 70, Confirm, new Color(.32f, .39f, .22f), 29);
            Button(page, "清空", 1700, 1012, 165, 43, ClearComposition, null, 22);
            notice = Label(page, "", 260, 1055, 1355, 25, 17, gold); if (Fresh(notice.transform)) notice.alignment = TextAnchor.MiddleCenter;
            CreateHoverPanel(page);
            Refresh();
        }
        void ClearComposition() { sequence.Clear(); targetId = null; RememberComposition(); message = ""; HideHover(); dirty = true; }
        void BuildUnitView(int index)
        {
            var page = uiPage.transform; var unit = battle.Units[index]; int id = index;
            var view = new UnitView(); unitViews.Add(view);
            view.body = Box(page, "Unit " + index, 0, 0, 240, 225);
            var select = Button(view.body, "选择", 0, 0, 240, 225, () => { targetId = battle.Units[id].id; RememberComposition(); message = ""; dirty = true; }, new Color(.3f, .52f, .65f, .015f), 1);
            select.GetComponentInChildren<Text>().enabled = false; targetButtons.Add(select);
            var frame = Box(view.body, "Selected Target", 0, 0, 240, 225); Border(frame, 240, 225, gold, 2); Decorative(frame); view.selected = frame.gameObject;
            view.below = Box(page, "Unit Info " + index, 0, 0, 320, 126, new Color(.025f, .045f, .075f, .86f));
            view.name = Label(view.below, "", 8, 3, 304, 30, 22); if (Fresh(view.name.transform)) view.name.alignment = TextAnchor.MiddleCenter;
            var health = Box(view.below,"Health Bar",10,37,300,26);
            Box(health, "Life Track", 0, 0, 300, 22, new Color(.2f, .11f, .12f));
            view.hp = Box(health, "Life Fill", 0, 0, 300, 22, new Color(.19f, .66f, .4f)).GetComponent<Image>();
            view.life = Label(health, "", 0, -2, 300, 26, 20); if (Fresh(view.life.transform)) view.life.alignment = TextAnchor.MiddleCenter;
            view.state = Label(view.below, "", 10, 65, 300, 56, 18, muted); if (Fresh(view.state.transform)) view.state.alignment = TextAnchor.UpperCenter;
            view.statusIcons=Box(view.below,"Status Icons",6,128,310,28);
            Decorative(view.below);
            // The info panel remains a hover target; no invisible rectangle blocks selecting the unit.
            view.below.GetComponent<Image>().raycastTarget = true;
            view.below.gameObject.GetOrAdd<BattleHover>().Changed = entered => { if (entered) ShowHover(() => UnitDescription(unit), unit.player ? 60 : 1380, 220); else ScheduleHideHover(); };
            view.above = Box(page, "Intent " + index, 0, 0, 320, 66, new Color(.025f, .045f, .075f, .87f));
            view.intent = Label(view.above, "", 10, 6, 300, 58, 20, unit.player ? blue : gold); if (Fresh(view.intent.transform)) view.intent.alignment = TextAnchor.MiddleCenter; Decorative(view.above);
            view.avatar = Box(page, "Action Avatar " + index, 0, 0, 44, 44);
            var portrait = unit.player ? catalog.weapons[selectedWeapon].portrait : catalog.monsters.Find(m => m.id == unit.monsterId).portrait;
            var picture = Box(view.avatar,"Portrait",0,0,44,44).gameObject.GetOrAdd<Image>(); if (portrait != null) picture.sprite = portrait; picture.preserveAspect = true; picture.enabled = picture.sprite != null;
            var fallback = view.avatar.gameObject.GetOrAdd<BattlePortrait>(); fallback.color = UnitColor(unit); fallback.enabled = !picture.enabled;

            var mark = Label(view.avatar, unit.player ? catalog.weapons[selectedWeapon].title.Last().ToString() : unit.title.Substring(0, 1), 0, 0, 44, 44, 21); if (Fresh(mark.transform)) mark.alignment = TextAnchor.MiddleCenter;
            view.progress = Label(view.avatar, "", -28, -22, 110, 22, 15); if (Fresh(view.progress.transform)) view.progress.alignment = TextAnchor.MiddleCenter;
            view.avatar.gameObject.GetOrAdd<BattleHover>().Changed = entered => { if (entered) ShowHover(() => UnitDescription(unit), 720, 165); else ScheduleHideHover(); };
        }
        Color UnitColor(Combatant u) => viewConfig != null && viewConfig.units.Exists(v=>v.id==(u.player?u.title:u.monsterId)) ? viewConfig.units.Find(v=>v.id==(u.player?u.title:u.monsterId)).color : u.player ? blue : u.monsterId == "彩虹史莱姆" ? new Color(.67f, .48f, .85f) : u.monsterId == "熔岩史莱姆" ? new Color(.88f, .36f, .18f) : new Color(.3f, .72f, .48f);
        void BuildFanHand(Transform page)
        {
            var faces = Box(page, "Hand Faces", 0, 0, 1920, 1080);
            var hits = Box(page, "Hand Hit Regions", 0, 0, 1920, 1080);
            for (int i = 0; i < 9; i++)
            {
                int index = i; var card = CurrentCard(catalog.weapons[selectedWeapon].cards[i]); var skill = catalog.Skill(card.skill);
                float x = 310 + i * 155, y = 945 + Mathf.Abs(i - 4) * 3, angle = (4 - i) * 2;
                var face = Box(faces, "Card Face " + i, x, y, 170, 220, new Color(.065f, .12f, .18f));
                if (Fresh(face)) face.pivot = new Vector2(.5f, .5f); Border(face, 170, 220, TypeColor(card.match));
                var title = Label(face, "", 9, 8, 152, 50, 21); if (Fresh(title.transform)) title.alignment = TextAnchor.UpperCenter; cardLabels.Add(title);
                var kind = Label(face, "", 7, 57, 156, 27, 16, TypeColor(card.match)); if (Fresh(kind.transform)) kind.alignment = TextAnchor.MiddleCenter; cardKinds.Add(kind);
                var original = catalog.Card(card.id);
                var icon = Box(face, "Replaceable Skill Icon", 36, 89, 98, 68).gameObject.GetOrAdd<Image>();
                var art = original.art != null ? original.art : skill.icon;
                if (art != null) icon.sprite = art; icon.preserveAspect = true; icon.enabled = icon.sprite != null;
                var placeholder = Box(face, "Placeholder Art", 34, 89, 102, 66, new Color(.13f, .22f, .3f)); placeholder.gameObject.SetActive(!icon.enabled);
                var glyph = Label(placeholder, skill.title.Substring(0, 1), 1, 2, 100, 62, 40, TypeColor(card.match)); if (Fresh(glyph.transform)) glyph.alignment = TextAnchor.MiddleCenter;
                if (original.frame != null) face.GetComponent<Image>().sprite = original.frame;
                else if (viewConfig != null && viewConfig.cardFrame != null) face.GetComponent<Image>().sprite = viewConfig.cardFrame;
                var typeIcon = Box(face,"Type Icon",142,60,22,22).gameObject.GetOrAdd<Image>();
                var typeStyle = viewConfig != null ? viewConfig.types.Find(t=>t.id == card.match.ToString()) : null;
                if(original.typeIcon != null) typeIcon.sprite = original.typeIcon; else if(typeStyle?.icon != null) typeIcon.sprite = typeStyle.icon;
                typeIcon.enabled = typeIcon.sprite != null;

                var traits = Label(face, "", 10, 164, 150, 31, 16); if (Fresh(traits.transform)) traits.alignment = TextAnchor.MiddleCenter; cardTraits.Add(traits);
                var traitIcons=Box(face,"Trait Icons",46,198,80,20); RefreshTraitIcons(traitIcons,card); Decorative(face);
                var hit = Box(hits, "Hand Card " + i, x, y, i == 0 || i == 8 ? 210 : 155, 380, Color.clear); if (Fresh(hit)) hit.pivot = new Vector2(.5f, .5f);
                var button = hit.gameObject.GetOrAdd<Button>(); button.onClick.RemoveAllListeners(); button.targetGraphic = hit.GetComponent<Image>(); button.transition = Selectable.Transition.None;
                var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
                button.onClick.AddListener(() => AddCard(index)); cardButtons.Add(button);
                var interaction = hit.gameObject.GetOrAdd<HandCardInteraction>(); interaction.face = face; interaction.index = i; interaction.config = viewConfig; interaction.angle = Fresh(face) ? angle : face.localEulerAngles.z;
                if (Fresh(hit)) { if (i == 0) interaction.bandMin = -105; if (i == 8) interaction.bandMax = 105; }
                interaction.Initialize(); handInteractions.Add(interaction);
                interaction.Hovered = entered => { if (entered) ShowHover(() => CardDescription(catalog.weapons[selectedWeapon].cards[index]), Mathf.Clamp(x - 230, 28, 1410), 360); else ScheduleHideHover(); };
            }
        }
        Color TypeColor(MatchType type) => viewConfig != null && viewConfig.types.Exists(t=>t.id == type.ToString()) ? viewConfig.types.Find(t=>t.id == type.ToString()).color : type == MatchType.Offense ? gold : type == MatchType.Survival ? new Color(.31f, .79f, .72f) : new Color(.74f, .52f, .95f);
        string CardDescription(string id)
        {
            var c = CurrentCard(id); var s = catalog.Skill(c.skill);
            int slot = sequence.IndexOf(id);
            string role = slot == 0 ? "本次1号位：执行此主技能，自身词条也参与。" : slot > 0 ? $"本次{slot + 1}号位：仅贡献词条，下面的卡面主技能不施放。" : "尚未排入，本次不参与结算。";
            string validity = slot >= 0 ? string.Join("\n", battle.Plan(battle.Player, sequence).traitHints.Where(h => h.StartsWith((slot + 1) + "号位"))) : "";
            return $"{s.title}\n{MatchName(c.match)} · {DamageName(s.damageType)} · {ReachName(s.reach)}\n{role}\n{validity}\n\n{s.description}\n\n" + string.Join("\n\n", c.traits.Select(t => { var trait = catalog.Trait(t); return $"{trait.title}〔{MatchName(trait.match)}〕\n{trait.description}\n{trait.note}"; })) + "\n\n" + s.note;
        }
        string UnitDescription(Combatant u)
        {
            int waiting = battle.Waiting.IndexOf(u.id);
            string order = !u.Alive ? "已倒下" : battle.Active == u ? "正在行动" : waiting >= 0 ? "实际等待队列第" + (waiting + 1) + "位" : "自然跑条中";
            return $"{u.title} [{u.id}] {(u.backRow ? "后排" : "前排")}\n生命 {u.hp}/{u.panel.hp} / 护盾 {u.shield}\n行动进度 {u.progress:F2}% / 速度 {u.Stat(Attribute.Speed):F2}\n{order}（头像竖向错位仅用于避让）\n物攻 {u.Stat(Attribute.Attack):F2} / 特攻 {u.Stat(Attribute.SpecialAttack):F2}\n物防 {u.Stat(Attribute.Defense):F2} / 特防 {u.Stat(Attribute.SpecialDefense):F2}\n{battle.Intent(u)}\n\n" + string.Join("\n", u.states.Select(s => $"{KindName(s.kind)} {s.value:F2} 剩余{s.remaining}回合" + (s.pending ? "（待定）" : "")));
        }
        void CreateHoverPanel(Transform page)
        {
            var panel = Box(page, "Hover Details", 30, 300, 475, 350, new Color(.025f, .045f, .075f, .99f)); hoverPanel = panel.gameObject;
            Border(panel, 475, 350, new Color(.42f, .56f, .65f));
            hoverText = ScrollText(panel, 8, 8, 459, 333, 19);
            panel.gameObject.GetOrAdd<BattleHover>().Changed = entered => { hoverInside = entered; if (!entered) ScheduleHideHover(); };
            hoverPanel.SetActive(false);
        }
        void ShowHover(Func<string> content, float x, float y)
        {
            if (hoverPanel == null || showAllLogs || showTraitEditor || showSettings || showPreview) return;
            hoverContent = content; hideHoverAt = float.PositiveInfinity; hoverInside = false;
            hoverPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(Mathf.Clamp(x, 15, 1430), -Mathf.Clamp(y, 150, 680));
            hoverPanel.SetActive(true); hoverPanel.transform.SetAsLastSibling(); SetScroll(hoverText, content());
        }
        void ScheduleHideHover() { hideHoverAt = Time.unscaledTime + .15f; }
        void HideHover()
        {
            if (hoverPanel != null) hoverPanel.SetActive(false); hoverContent = null; hoverInside = false;
            foreach (var hand in handInteractions) if (hand != null) hand.Restore();
        }
        void UpdateHover()
        {
            if (hoverPanel == null || !hoverPanel.activeSelf) return;
            if (!hoverInside && Time.unscaledTime >= hideHoverAt) { HideHover(); return; }
            if (hoverContent != null) SetScroll(hoverText, hoverContent());
        }
        void LateUpdate()
        {
            if (battle == null || unitViews.Count != battle.Units.Count || uiPage == null) return;
            var camera = Camera.main; if (camera == null) return;
            var page = uiPage.GetComponent<RectTransform>();
            // Keep the world camera in the same fitted 16:9 frame as the UI at every aspect ratio.
            // Projecting the world into the whole window would make unit labels drift on 4:3.
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, page.TransformPoint(page.rect.min));
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, page.TransformPoint(page.rect.max));
            Vector2 pixels = canvas.renderingDisplaySize;
            if (pixels.x > 0 && pixels.y > 0)
                camera.rect = new Rect(bottomLeft.x / pixels.x, bottomLeft.y / pixels.y, (topRight.x - bottomLeft.x) / pixels.x, (topRight.y - bottomLeft.y) / pixels.y);
            camera.cullingMask &= ~(1 << 5);
            for (int i = 0; i < unitViews.Count; i++)
            {
                var u = battle.Units[i]; var view = unitViews[i];
                if (i < stageObjects.Count && stageObjects[i] != null)
                {
                    var renderers = stageObjects[i].GetComponentsInChildren<Renderer>();
                    var bounds = new Bounds(stageObjects[i].transform.position, Vector3.one);
                    if (renderers.Length > 0) { bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); }
                    var rig = stageObjects[i].GetComponent<UnitPresentation>();
                    Vector2 foot = Project(rig != null && rig.healthAnchor != null ? rig.healthAnchor.position : bounds.center - Vector3.up * bounds.extents.y, page, camera);
                    Vector2 head = Project(bounds.center + Vector3.up * bounds.extents.y, page, camera);
                    float x = Mathf.Clamp(foot.x, 180, 1740);
                    view.below.anchoredPosition = new Vector2(x - 160, -Mathf.Clamp(foot.y + (viewConfig != null ? viewConfig.unitInfoOffset : 12), viewConfig != null ? viewConfig.infoVerticalRange.x : 440, viewConfig != null ? viewConfig.infoVerticalRange.y : 480));
                    view.above.anchoredPosition = new Vector2(x - 160, -Mathf.Clamp(head.y - (viewConfig != null ? viewConfig.intentOffset : 82), viewConfig != null ? viewConfig.intentVerticalRange.x : 160, viewConfig != null ? viewConfig.intentVerticalRange.y : 330));
                    if(rig!=null && rig.statusAnchor!=null) { var status=Project(rig.statusAnchor.position,page,camera); view.statusIcons.position=page.TransformPoint(new Vector3(status.x-page.rect.width/2,page.rect.height/2-status.y,0)); }
                    view.body.anchoredPosition = new Vector2(x - 120, -Mathf.Clamp(head.y - 8, 220, 330));
                }
                float progress = (float)BattleMath.Clamp(u.progress, 0, 100); int lane = 0;
                for (int j = 0; j < i; j++) if (battle.Units[j].Alive && Mathf.Abs((float)battle.Units[j].progress - progress) < (viewConfig != null ? viewConfig.avatarSeparation : 7)) lane++;
                float offset = lane == 0 ? 0 : lane % 2 == 1 ? -(viewConfig != null ? viewConfig.avatarLaneHeight : 44) : (viewConfig != null ? viewConfig.avatarLaneHeight : 44);
                view.avatar.anchoredPosition = new Vector2(ActionRail.anchoredPosition.x + ActionRail.rect.width * progress / 100 - view.avatar.rect.width / 2, -(54 + offset));
                view.avatar.gameObject.SetActive(u.Alive); view.body.gameObject.SetActive(u.Alive); view.below.gameObject.SetActive(true); view.above.gameObject.SetActive(u.Alive);
                view.progress.text = u.title + " " + progress.ToString("F0") + "%";
                view.progress.gameObject.SetActive(!battle.Units.Any(other => other != u && other.Alive && Math.Abs(other.progress - u.progress) < 7));
            }
        }
        Vector2 Project(Vector3 world, RectTransform page, Camera camera)
        {
            Vector2 screen = camera.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(page, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out Vector2 local);
            return new Vector2(local.x + page.rect.width * .5f, page.rect.height * .5f - local.y);
        }
        void Refresh()
        {
            if (battle == null || turnLabel == null) return;
            turnLabel.text = battle.Finished ? battle.Won ? "胜利 · 全部敌人倒下" : "失败 · 器灵倒下" : battlePaused ? "战斗已暂停" : battle.Active == null ? "按速度自然跑条中" : battle.Active.player ? $"你的行动 · 总连锁：{sequence.Count} / 5" : battle.Active.title + "行动 · 全场暂停";
            var pause = uiPage.transform.Find("Pause Battle"); if (pause != null) pause.GetComponentInChildren<Text>().text = battlePaused ? "继续" : "暂停";
            List<Combatant> legal = sequence.Count > 0 ? battle.LegalTargets(battle.Player, catalog.Skill(CurrentCard(sequence[0]).skill)) : new List<Combatant>();
            for (int i = 0; i < unitViews.Count; i++)
            {
                var u = battle.Units[i]; var view = unitViews[i];
                view.name.text = u.title + " · " + (u.backRow ? "后排" : "前排") + (!u.Alive ? " [倒下]" : "");
                view.life.text = $"{u.hp} / {u.panel.hp}"; view.hp.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, view.hp.transform.parent.GetComponent<RectTransform>().rect.width * (float)u.hp / u.panel.hp);
                view.state.text = $"护盾 {u.shield}  ·  双防 {u.Stat(Attribute.Defense):F1}/{u.Stat(Attribute.SpecialDefense):F1}\n" + (u.states.Count == 0 ? "无状态" : string.Join("  ", u.states.Select(s => KindName(s.kind) + (s.reaction == null ? s.value.ToString("0.#") : "") + (s.remaining > 0 ? $"({s.remaining})" : ""))));
                RefreshStatusIcons(view.statusIcons,u);
                view.intent.text = u.player ? $"速度 {u.Stat(Attribute.Speed):F1} · {(battle.Active == u ? "正在排谱" : "等待行动")}" : battle.Intent(u);
                targetButtons[i].interactable = legal.Contains(u) && !battle.Finished; view.selected.SetActive(targetId == u.id && u.Alive);
                if (i < stageObjects.Count && !PresentationBusy && presentationCursor >= battle.VisualEvents.Count) stageObjects[i].SetActive(u.Alive);
            }
            for (int i = 0; i < 5; i++)
            {
                slotLabels[i].text = i < sequence.Count ? $"{i + 1}号位{(i == 0 ? " · 主技能" : "")}\n{CurrentCard(sequence[i]).skill}\n{string.Join(" / ", CurrentCard(sequence[i]).traits)}" : $"{i + 1}号位{(i == 0 ? " · 主技能" : "")}\n点击手牌加入";
            }
            for (int i = 0; i < 9; i++)
            {
                var c = CurrentCard(catalog.weapons[selectedWeapon].cards[i]); var skill = catalog.Skill(c.skill); int slot = sequence.IndexOf(c.id);
                cardLabels[i].text = $"{i + 1}. {c.skill}" + (slot >= 0 ? "\n[" + (slot + 1) + "号位]" : "");
                cardKinds[i].text = MatchName(c.match) + " · " + DamageName(skill.damageType) + " · " + ReachName(skill.reach);
                cardTraits[i].text = string.Join(" / ", c.traits);
                RefreshTraitIcons(handInteractions[i].face.Find("Trait Icons") as RectTransform,c);
                handInteractions[i].SetSelected(slot >= 0);
            }
            string validation = sequence.Count > 0 ? battle.Validate(battle.Player, sequence, targetId) : null;
            notice.text = !string.IsNullOrEmpty(message) ? message : validation ?? "点击单位选择目标；悬停卡牌查看技能与词条。";
            confirmButton.interactable = validation == null && sequence.Count > 0 && !battle.Finished && battle.Active == battle.Player && !PresentationBusy;
            phaseLabel.text = "当前目标：" + (battle.Units.Find(u => u.id == targetId)?.title ?? "未选") + " · 近战优先前排，远程可选后排";
            if (settingsSummary != null) SetScroll(settingsSummary, BattleStatistics());
        }
        string BattleStatistics() => $"seed={battle.Rng.Seed}  随机抽样{battle.Rng.Draws}  行动{battle.ActionSerial}\n计算伤害{battle.TotalCalculatedDamage} / 护盾吸收{battle.TotalShieldAbsorbed}\n实际扣血{battle.TotalHpDamage} / 过量{battle.TotalOverkill}\n暴击{battle.Criticals} / 格挡{battle.Blocks} / 闪避{battle.Dodges}\n\n按目标汇总\n{battle.DamageSummary()}";
        void OpenSettings()
        {
            if (showSettings) return; showSettings = true;
            var shade = ModalShade("Settings Overlay"); settingsPanel = shade.gameObject;
            var panel = CenterBox(shade, "Settings Panel", 1040, 760, new Color(.04f, .075f, .12f));
            Label(panel, "战斗设置与统计", 30, 22, 650, 46, 30, gold);
            Button(panel, "返回战斗", 810, 22, 200, 48, CloseSettings, null, 21);
            Label(panel, "界面打开期间暂停战斗。速度只改变自然跑条等待时间。", 30, 88, 970, 32, 20, muted);
            Label(panel, "等待速度", 30, 142, 150, 35, 22);
            var speed = Label(panel, clockScale.ToString("F1") + "×", 265, 145, 120, 35, 23, gold);
            Button(panel, "−", 190, 137, 58, 45, () => { clockScale = Mathf.Max(.5f, clockScale - .5f); speed.text = clockScale.ToString("F1") + "×"; });
            Button(panel, "＋", 390, 137, 58, 45, () => { clockScale = Mathf.Min(20, clockScale + .5f); speed.text = clockScale.ToString("F1") + "×"; });
            Button(panel, "同种子重试", 30, 215, 300, 55, () => Retry(false));
            Button(panel, "更换种子重试", 350, 215, 300, 55, () => Retry(true));
            Button(panel, "更换武器", 670, 215, 340, 55, () => { RememberComposition(); showSettings = false; ShowSelection(); });
            Text exportNotice = null;
            Button(panel, "导出配置/日志", 30, 290, 300, 52, () => { ExportLog(); exportNotice.text = message; });
            Button(panel, "自由重铸", 350, 290, 300, 52, () => { CloseSettings(); OpenTraitEditor(); });
            Button(panel, battlePaused ? "结束暂停" : "保持暂停", 670, 290, 340, 52, () => { battlePaused = !battlePaused; CloseSettings(); });
            exportNotice = Label(panel, "", 30, 350, 980, 44, 17, gold);
            settingsSummary = ScrollText(panel, 30, 400, 980, 330, 21); SetScroll(settingsSummary, BattleStatistics());
        }
        void CloseSettings() { if (settingsPanel != null) { settingsPanel.SetActive(false); DisposeView(settingsPanel); } settingsSummary = null; showSettings = false; dirty = true; }
        void OpenPreview()
        {
            if (showPreview) return; showPreview = true;
            var shade = ModalShade("Preview Overlay"); previewPanel = shade.gameObject;
            var panel = CenterBox(shade, "Preview Panel", 1200, 830, new Color(.04f, .075f, .12f));
            Label(panel, "出招预览 · 计算示例", 25, 20, 800, 45, 30, gold);
            Button(panel, "返回战斗", 970, 20, 200, 47, ClosePreview);
            previewText = ScrollText(panel, 25, 85, 1145, 660, 20); SetScroll(previewText, battle.Preview(sequence, targetId));
            var execute = Button(panel, "确认此排谱", 890, 765, 280, 48, () => { ClosePreview(); Confirm(); }, new Color(.25f, .4f, .3f), 22);
            execute.interactable = sequence.Count > 0 && battle.Validate(battle.Player, sequence, targetId) == null;
            Label(panel, "随机转火与暴击不保证发生；详细逐段示例可滚动查看。", 25, 773, 850, 35, 19, muted);
        }
        void ClosePreview() { if (previewPanel != null) { previewPanel.SetActive(false); DisposeView(previewPanel); } previewText = null; showPreview = false; dirty = true; }
        void ShowLog()
        {
            if (showAllLogs) return; showAllLogs = true;
            var shade = ModalShade("Log Overlay");
            var panel = CenterBox(shade, "Log Panel", 1640, 930, new Color(.035f, .065f, .1f));
            Label(panel, "详细战斗日志与规则边界", 30, 20, 1000, 45, 30, gold);
            Button(panel, "返回战斗", 1410, 20, 200, 47, () => { shade.gameObject.SetActive(false); DisposeView(shade.gameObject); showAllLogs = false; dirty = true; });
            Text exportNotice = null;
            Button(panel, "导出全部日志", 1195, 20, 200, 47, () => { ExportLog(); exportNotice.text = message; });
            logText = ScrollText(panel, 25, 85, 1050, 815, 19); SetScroll(logText, "按目标汇总\n" + battle.DamageSummary() + "\n\n" + string.Join("\n", battle.Log), true);
            var ruleText = ScrollText(panel, 1090, 85, 520, 815, 19); SetScroll(ruleText, "待定/测试项\n\n" + string.Join("\n\n", catalog.pending));
            exportNotice = Label(panel, "", 25, 903, 1580, 24, 16, gold);
        }
    }
}
