using System;
using System.Collections.Generic;
using System.Linq;

namespace Qiling
{
    [Serializable] public class CardTraits
    {
        public string cardId;
        public List<string> traits = new List<string>();
        public CardTraits Copy() => new CardTraits { cardId = cardId, traits = new List<string>(traits) };
    }
    [Serializable] public class DebugLoadout
    {
        public string weaponId;
        public List<CardTraits> cards = new List<CardTraits>();
        public List<string> sequence = new List<string>();
        public string target;
        public DebugLoadout Copy() => new DebugLoadout { weaponId = weaponId, cards = cards.Select(c => c.Copy()).ToList(), sequence = new List<string>(sequence), target = target };
        public static DebugLoadout Initial(BattleCatalog data, string weaponId)
        {
            var weapon = data.weapons.Find(w => w.id == weaponId) ?? throw new ArgumentException("未知武器");
            return new DebugLoadout { weaponId = weaponId, cards = weapon.cards.Select(id => new CardTraits { cardId = id, traits = new List<string>(data.Card(id).traits) }).ToList() };
        }
        public string Validate(BattleCatalog data)
        {
            var weapon = data.weapons.Find(w => w.id == weaponId);
            if (weapon == null || cards == null || cards.Count != 9 || cards.Any(c => c == null) || cards.Select(c => c.cardId).Distinct().Count() != 9 || cards.Any(c => !weapon.cards.Contains(c.cardId))) return "配置必须对应当前武器的九张不同实体卡。";
            foreach (var card in cards)
            {
                if (card.traits == null || card.traits.Count < 1 || card.traits.Count > 3) return "每张卡必须有1～3条词条。";
                foreach (var id in card.traits)
                {
                    var trait = data.traits.Find(t => t.id == id);
                    // The initial spear placeholder may be retained/restored, but cannot be newly assigned.
                    if (!TraitSupport.Enabled(trait) && !(trait != null && trait.pending && card.traits.SequenceEqual(data.Card(card.cardId).traits))) return "不能配置词条：" + id + "。" + TraitSupport.Reason(trait);
                }
            }
            if (sequence == null || sequence.Count > 5 || sequence.Distinct().Count() != sequence.Count || sequence.Any(id => !weapon.cards.Contains(id))) return "排谱必须为该武器的1～5张不同实体卡，或空排谱。";
            return null;
        }
        public string SetTrait(BattleCatalog data, string cardId, int index, string traitId)
        {
            var trait = data.traits.Find(t => t.id == traitId);
            if (!TraitSupport.Enabled(trait)) return TraitSupport.Status(trait) + "：" + TraitSupport.Reason(trait);
            var card = cards.Find(c => c.cardId == cardId);
            if (card == null || index < 0 || index > 2 || index > card.traits.Count) return "词条槽不能留空，请先添加前一条词条。";
            if (index == card.traits.Count) card.traits.Add(traitId); else card.traits[index] = traitId;
            return null;
        }
        public string RemoveTrait(string cardId, int index)
        {
            var card = cards.Find(c => c.cardId == cardId);
            if (card == null || index < 0 || index >= card.traits.Count) return "没有该词条。";
            if (card.traits.Count == 1) return "每张卡至少保留1条词条，可直接替换。";
            card.traits.RemoveAt(index); return null;
        }
        public string Batch(BattleCatalog data, IReadOnlyList<string> order, string traitId, int index, bool replaceAll)
        {
            return BatchLists(data, order, replaceAll ? new[] { traitId } : null, traitId, index);
        }
        public string Preset(BattleCatalog data, IReadOnlyList<string> order, params string[] traits) => BatchLists(data, order, traits, null, 0);
        string BatchLists(BattleCatalog data, IReadOnlyList<string> order, IReadOnlyList<string> replacement, string traitId, int index)
        {
            if (order == null || order.Count != 5 || order.Distinct().Count() != 5 || order.Any(id => !cards.Any(c => c.cardId == id))) return "请先排满五张卡；批量配置不会自动改变排谱。";
            var copy = Copy();
            foreach (var id in order)
            {
                if (replacement != null) copy.cards.Find(c => c.cardId == id).traits = replacement.ToList();
                else { string error = copy.SetTrait(data, id, index, traitId); if (error != null) return error; }
            }
            var validation = copy.Validate(data); if (validation != null) return validation;
            cards = copy.cards; return null;
        }
    }
    public static class TraitSupport
    {
        static readonly HashSet<string> implemented = new HashSet<string> { "追击", "锋锐", "穿甲", "壁障", "增幅", "灵动", "深化", "延续", "强化", "破势", "顽强", "破魔", "连锁反应", "专注", "镜像", "压制", "终结", "乘隙", "裂隙", "贯通", "连锁收益", "连锁蓄势", "连锁协奏", "同调", "交织" };
        public static bool Enabled(TraitDef trait) => trait != null && !trait.pending && implemented.Contains(trait.id);
        public static string Category(TraitDef trait) => trait == null ? "已定义但未实现" : trait.pending ? "规则待定" : Enabled(trait) ? "已实现且可装备" : "已定义但未实现";
        public static string Status(TraitDef trait) => Category(trait) + (Enabled(trait) ? " · 调试可配置" : " · 不可配置");
        public static string Reason(TraitDef trait)
        {
            if(trait==null) return "数据库未注册该词条。";
            if(trait.pending) return string.IsNullOrEmpty(trait.description) ? "规则仍待定，尚无可执行定义。" : trait.description;
            if(Enabled(trait)) return trait.id=="镜像" ? "已接入基础复制；资源消耗、非数值控制、部分持续/离散数值缩放组合单独提示限制，不禁用其他镜像组合。" : "效果已接入；自由重铸不检查正式稀有度、获取条件或掉落概率。是否产生收益仍按主技能匹配类型、触发条件与效果字段判定。";
            return "效果已定义，但没有注册可执行的结算实现。";
        }
    }
    [Serializable] public class DebugSession
    {
        public int version = 1, selectedWeapon, encounter, seed = 20261020;
        public List<DebugLoadout> weapons = new List<DebugLoadout>();
        public DebugLoadout ForWeapon(BattleCatalog data, string id)
        {
            var profile = weapons.Find(w => w.weaponId == id);
            if (profile == null) { profile = DebugLoadout.Initial(data, id); weapons.Add(profile); }
            return profile;
        }
        public void Save(DebugLoadout draft)
        {
            weapons.RemoveAll(w => w.weaponId == draft.weaponId); weapons.Add(draft.Copy());
        }
    }
    [Serializable] public class ReplayStep
    {
        public string kind, target;
        public int action;
        public List<string> cards = new List<string>();
        public DebugLoadout loadout;
    }
    [Serializable] public class BattleReplay
    {
        public int seed, encounter;
        public string weaponId;
        public DebugLoadout initial;
        public List<ReplayStep> steps = new List<ReplayStep>();
    }
    [Serializable] public class DamageRecord
    {
        public int action, calculated, absorbed, hpLost, overkill;
        public string source, target, label, skill;
        public double attackAttribute, defense, penetration, effectiveDefense, coefficient, bonus, resource, damageUp, critRate, critMultiplier, unrounded, bypassRatio;
        public int bypass, bypassHpLost;
        public bool mirror;
        public bool critical, dodged, blocked;
    }
    [Serializable] public class TargetDamageSummary
    {
        public string target;
        public int calculated, absorbed, hpLost, overkill;
    }
}
