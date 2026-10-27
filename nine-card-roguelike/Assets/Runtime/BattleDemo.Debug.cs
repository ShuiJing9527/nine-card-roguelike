using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qiling
{
    public static class DebugFiles
    {
        // Tests can isolate their files without touching the user's saved builds.
        public static string RootOverride;
        public static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "QilingDebug");
    }
    [Serializable] public class DebugTestBundle
    {
        public int version = 1;
        public string sourceVersion, catalogJson, explanation;
        public DebugSession savedProfiles;
        public BattleReplay replay;
        public DebugLoadout applied;
        public List<DamageRecord> damage;
        public List<BattleVisualEvent> presentationNodes;
        public List<TargetDamageSummary> targetTotals;
        public List<string> log;
    }
    public partial class BattleDemo
    {
        DebugSession debugSession;
        DebugLoadout traitDraft;
        GameObject traitOverlay;
        bool showTraitEditor, batchReplaceAll;
        int editCard, editPosition, batchPosition;
        string pickedTrait = "追击", editorMessage = "";
        public IReadOnlyList<string> CurrentSequence => sequence.AsReadOnly();
        DebugLoadout CurrentProfile => debugSession.ForWeapon(catalog, catalog.weapons[selectedWeapon].id);
        public DebugLoadout SavedProfile(string weaponId) => debugSession.ForWeapon(catalog, weaponId).Copy();
        CardDef CurrentCard(string id)
        {
            if (battle != null) return battle.Card(id);
            var original = catalog.Card(id);
            return new CardDef { id = original.id, skill = original.skill, match = original.match, traits = new List<string>(CurrentProfile.cards.Find(c => c.cardId == id).traits) };
        }
        void InitializeDebugSession()
        {
            debugSession = new DebugSession();
            string path = Path.Combine(DebugFiles.Root, "profiles.json");
            if (File.Exists(path))
            {
                try
                {
                    var saved = JsonUtility.FromJson<DebugSession>(File.ReadAllText(path));
                    if (saved == null || saved.version != 1 || saved.weapons == null) throw new InvalidDataException("不支持的配置格式");
                    foreach (var profile in saved.weapons)
                    {
                        string validation = profile?.Validate(catalog);
                        if (profile == null || validation != null) throw new InvalidDataException(validation ?? "空武器配置");
                    }
                    debugSession = saved;
                }
                catch (Exception error) { message = "测试配置加载失败，使用初始配置：" + error.Message; Debug.LogWarning(message); }
            }
            selectedWeapon = Mathf.Clamp(debugSession.selectedWeapon, 0, catalog.weapons.Count - 1);
            selectedEncounter = Mathf.Clamp(debugSession.encounter, 0, catalog.encounters.Count - 1); seedText = debugSession.seed.ToString();
            foreach (var w in catalog.weapons) debugSession.ForWeapon(catalog, w.id);
        }
        bool SaveProfiles()
        {
            debugSession.selectedWeapon = selectedWeapon; debugSession.encounter = selectedEncounter;
            if (int.TryParse(seedText, out int seed)) debugSession.seed = seed;
            try { Directory.CreateDirectory(DebugFiles.Root); File.WriteAllText(Path.Combine(DebugFiles.Root, "profiles.json"), JsonUtility.ToJson(debugSession, true), new System.Text.UTF8Encoding(false)); return true; }
            catch (Exception error) { message = "配置保留在本次运行，写入磁盘失败：" + error.Message; return false; }
        }
        void RememberComposition()
        {
            ValidateHoverTarget();
            if (battle == null) return;
            CurrentProfile.sequence = new List<string>(sequence); CurrentProfile.target = targetId; SaveProfiles();
        }
        public void Retry(bool changeSeed)
        {
            if (battle == null || battle.IsResolving) return;
            RememberComposition();
            int seed = battle.Rng.Seed;
            // This is outside combat RNG. Choosing a new run must not consume the old run's stream.
            if (changeSeed) { int next = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0); seed = next == seed ? unchecked(seed + 1) : next; }
            seedText = seed.ToString(); StartBattle();
        }
        void OpenTraitEditor()
        {
            if (battle != null && (battle.IsResolving || PresentationBusy)) { message = "正在结算，不能修改词条。"; return; }
            RememberComposition(); traitDraft = CurrentProfile.Copy(); editCard = 0; editPosition = 0; batchPosition = 0; batchReplaceAll = false;
            showTraitEditor = true; editorMessage = "免费调试：只修改词条，不修改主技能。草稿在点击保存/应用后生效；编辑时战斗暂停。"; RenderTraitEditor();
        }
        string TraitStatusReport()
        {
            var sections=new List<string>();
            foreach(var category in new[]{"已实现且可装备","已定义但未实现","规则待定"})
            {
                var traits=catalog.traits.Where(t=>TraitSupport.Category(t)==category).ToList();
                sections.Add(category + $"（{traits.Count}）\n" + string.Join("\n\n", traits.Select(t=>t.title+"："+TraitSupport.Reason(t))));
            }
            return "自由重铸不使用正式获取、掉落概率或稀有度过滤。已定义但未实现、规则待定的条目可查看原因，不能配置成无效果词条。\n\n"+string.Join("\n\n",sections);
        }
        void OpenTraitStatus()
        {
            var shade=CreateAuthoredModal(traitOverlay.GetComponent<RectTransform>(),"Trait Status Overlay");
            var panel=CenterBox(shade,"Trait Status Panel",1400,850,new Color(.025f,.045f,.08f,.995f));
            Label(panel,"词条实现状态",25,20,900,45,30,gold);
            Button(panel,"返回词条测试",1120,20,250,46,()=>{ shade.gameObject.SetActive(false); DisposeView(shade.gameObject); });
            var text=ScrollText(panel,25,85,1350,735,21); SetScroll(text,TraitStatusReport());
        }
        void CloseTraitEditor()
        {
            if (traitOverlay != null) { traitOverlay.SetActive(false); DisposeView(traitOverlay); }
            showTraitEditor = false; traitDraft = null; dirty = true;
        }
        void RenderTraitEditor()
        {
            if (traitOverlay != null) { traitOverlay.SetActive(false); DisposeView(traitOverlay); }
            var shade = ModalShade("Trait Test Overlay"); traitOverlay = shade.gameObject;
            var panel = CenterBox(shade, "Trait Test Editor", 1600, 1000, new Color(.025f, .045f, .08f, .995f));
            Label(panel, "自由重铸词条 · " + catalog.weapons[selectedWeapon].title, 25, 17, 475, 48, 31, gold);
            Label(panel, "选择卡片词条槽 → 选择右侧词条 → 添加/替换。每卡1～3条，允许重复与不匹配。", 25, 75, 995, 32, 20);
            Label(panel, $"词条库：已实现{catalog.traits.Count(TraitSupport.Enabled)} / 未实现{catalog.traits.Count(t=>!t.pending && !TraitSupport.Enabled(t))} / 待定{catalog.traits.Count(t=>t.pending)}", 1035, 75, 550, 32, 21, gold);
            for (int i = 0; i < 9; i++)
            {
                int cardIndex = i; var card = traitDraft.cards[i]; var original = catalog.Card(card.cardId); float y = 121 + i * 76;
                var row = Box(panel, "Trait Card " + i, 20, y, 995, 70, i == editCard ? new Color(.12f, .24f, .3f) : new Color(.065f, .11f, .17f));
                Choice(row.gameObject,i==editCard);
                Label(row, $"{i + 1}. {original.skill}\n{MatchName(original.match)}（主技能固定）", 9, 9, 197, 55, 18);
                for (int j = 0; j < 3; j++)
                {
                    int position = j; string text = j < card.traits.Count ? card.traits[j] : "＋添加";
                    var b = Button(row, $"词条{j + 1}：{text}", 210 + j * 260, 10, 208, 49, () => { editCard = cardIndex; editPosition = position; RenderTraitEditor(); }, i == editCard && j == editPosition ? new Color(.28f, .33f, .43f) : (Color?)null, 18);
                    Choice(b.gameObject,i==editCard && j==editPosition); b.interactable = j <= card.traits.Count;
                    var delete = Button(row, "×", 423 + j * 260, 10, 36, 49, () => EditorOperation(traitDraft.RemoveTrait(card.cardId, position))); delete.gameObject.SetActive(j < card.traits.Count);
                }
            }
            for (int i = 0; i < catalog.traits.Count; i++)
            {
                var trait = catalog.traits[i]; float x = 1035 + (i % 2) * 273, y = 120 + (i / 2) * 34;
                bool enabled = TraitSupport.Enabled(trait);
                var option = Button(panel, trait.title + "〔" + MatchName(trait.match) + "〕" + (enabled ? "" : trait.pending ? " 待定" : " 未实现"), x, y, 263, 30, () => { pickedTrait = trait.id; RenderTraitEditor(); }, pickedTrait == trait.id ? new Color(.29f, .35f, .45f) : enabled ? new Color(.12f, .23f, .29f) : new Color(.11f, .12f, .16f), 15); Choice(option.gameObject,pickedTrait==trait.id);
            }
            var selected = catalog.Trait(pickedTrait);
            string combination="";
            if(selected.id=="镜像" && battle!=null && traitDraft.sequence.Count>0)
            {
                var probe=traitDraft.Copy(); string error=probe.SetTrait(catalog,probe.cards[editCard].cardId,editPosition,"镜像");
                int slot=probe.sequence.IndexOf(probe.cards[editCard].cardId);
                if(error==null && slot>=0)
                {
                    var plan=battle.Plan(battle.Player,probe.sequence,configuration:probe);
                    combination="\n\n当前排谱组合检查：\n"+string.Join("\n",plan.traitHints.Where(h=>h.StartsWith((slot+1)+"号位") && h.Contains("镜像")));
                }
            }
            var description = ScrollText(panel, 1035, 578, 540, 220, 18);
            SetScroll(description, $"{selected.title}〔{MatchName(selected.match)}〕\n{TraitSupport.Status(selected)}\n{TraitSupport.Reason(selected)}{combination}\n\n{selected.description}\n\n计算说明：{selected.note}\n\n当前连锁先按本卡连锁反应修正；一条2倍、两条3倍，仅本卡其他词条受益；实际槽位与总连锁不变。匹配检查读取1号位主技能类型，与该卡自身类型无关。");
            var applyOne = Button(panel, $"将“{selected.title}”填入卡{editCard + 1}的词条{editPosition + 1}", 1035, 808, 540, 44, () => EditorOperation(traitDraft.SetTrait(catalog, traitDraft.cards[editCard].cardId, editPosition, pickedTrait)), new Color(.19f, .35f, .31f), 19);
            applyOne.interactable = TraitSupport.Enabled(selected);
            Label(panel, "当前排谱五卡批量：", 25, 817, 240, 32, 20, gold);
            for (int i = 0; i < 3; i++)
            {
                int position = i; var option = Button(panel, "第" + (i + 1) + "条", 257 + i * 80, 813, 73, 38, () => { batchPosition = position; RenderTraitEditor(); }, i == batchPosition ? new Color(.25f, .35f, .43f) : (Color?)null, 17); Choice(option.gameObject,i==batchPosition);
            }
            Button(panel, batchReplaceAll ? "覆盖全部：只留所选1条" : "保留其他词条位置", 505, 813, 235, 38, () => { batchReplaceAll = !batchReplaceAll; RenderTraitEditor(); }, null, 17);
            var fill = Button(panel, "填入所选词条 → 五卡", 750, 813, 260, 38, () => EditorOperation(traitDraft.Batch(catalog, traitDraft.sequence, pickedTrait, batchPosition, batchReplaceAll)), null, 17); fill.interactable = TraitSupport.Enabled(selected);
            string[][] presets = { new[] { "追击" }, new[] { "锋锐" }, new[] { "追击", "锋锐" }, new[] { "追击", "锋锐", "穿甲" } };
            for (int i = 0; i < presets.Length; i++)
            {
                var preset = presets[i]; Button(panel, "五卡：" + string.Join("＋", preset), 25 + i * 248, 863, 237, 42, () => EditorOperation(traitDraft.Preset(catalog, traitDraft.sequence, preset)), null, 17);
            }
            Label(panel, "批量覆盖选定五卡的词条，不改变卡序与1号位；不足五张请先在战斗中排谱。", 1035, 863, 540, 50, 17, muted);
            Label(panel, editorMessage, 25, 914, 1550, 31, 18, gold);
            Button(panel, "保存，下次开战生效", 510, 15, 300, 46, () => SaveTraitDraft(false), null, 18);
            var apply = Button(panel, battle == null ? "保存并返回准备界面" : "保存并应用当前排谱", 825, 15, 390, 46, () => SaveTraitDraft(true), new Color(.2f, .39f, .33f), 18);
            apply.interactable = battle == null || !battle.IsResolving && (battle.Active == null || battle.Active.player || battle.Finished);
            Button(panel, "返回（不保存）", 1230, 15, 345, 46, CloseTraitEditor, null, 18);
            Label(panel, "保存和返回位于顶部；返回不保存会放弃本次未保存的草稿。", 25, 952, 985, 34, 19, muted);
            Button(panel, "恢复本武器初始预组词条", 1035, 951, 360, 40, () =>
            {
                var initial = DebugLoadout.Initial(catalog, traitDraft.weaponId); initial.sequence = new List<string>(traitDraft.sequence); initial.target = traitDraft.target;
                traitDraft = initial; EditorOperation(null, "草稿已恢复初始预组词条；排谱不变，点击保存后生效。");
            }, null, 18);
            Button(panel, "词条实现状态", 1408, 951, 167, 40, OpenTraitStatus, null, 17);
        }
        void EditorOperation(string error, string success = "草稿已修改，点击保存/应用后生效。") { editorMessage = error ?? success; RenderTraitEditor(); }
        void SaveTraitDraft(bool applyNow)
        {
            string error = traitDraft.Validate(catalog);
            if (error == null && applyNow && battle != null) error = battle.ApplyLoadout(traitDraft);
            if (error != null) { EditorOperation(error); return; }
            debugSession.Save(traitDraft); bool saved = SaveProfiles();
            string success = saved ? applyNow && battle != null ? "词条已应用，下一次确认出招生效；已有姿态保留原配置。" : "测试配置已保存，下次开战时生效。" : message;
            CloseTraitEditor(); message = success;
            if (battle == null) ShowSelection();
            else { UpdateDetail(); Refresh(); }
        }
        public string ExportTestBundle(string directory = null)
        {
            RememberComposition(); SaveProfiles();
            directory = directory ?? Path.Combine(DebugFiles.Root, "Exports"); Directory.CreateDirectory(directory);
            int seed = battle?.Rng.Seed ?? debugSession.seed;
            var bundle = new DebugTestBundle { sourceVersion = catalog.sourceVersion, catalogJson = JsonUtility.ToJson(catalog), savedProfiles = debugSession, replay = battle?.Replay, applied = battle?.Loadout, damage = battle?.Damage, presentationNodes = battle?.VisualEvents, targetTotals = battle?.TargetTotals, log = battle?.Log, explanation = "savedProfiles是下次开战配置，applied是当前已应用配置，replay.initial是开战快照。replay.steps依序记录每次成功出招的卡序/目标以及中途应用的词条快照。相同数据、种子与操作顺序可复现；catalogJson保存本次实际数值。" };
            string prefix = Path.Combine(directory, "QilingTest_" + seed + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            File.WriteAllText(prefix + ".json", JsonUtility.ToJson(bundle, true), new System.Text.UTF8Encoding(false));
            File.WriteAllText(prefix + ".txt", "按目标汇总\n" + (battle?.DamageSummary() ?? "尚未开战") + "\n\n" + string.Join("\n", battle?.Log ?? new List<string>()) + "\n\n待定规则\n" + string.Join("\n", catalog.pending), new System.Text.UTF8Encoding(false));
            return prefix + ".json";
        }
    }
}
