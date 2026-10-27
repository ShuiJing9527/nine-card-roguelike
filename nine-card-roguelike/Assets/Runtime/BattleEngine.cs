using System;
using System.Collections.Generic;
using System.Linq;

namespace Qiling
{
    public sealed class BattleRandom
    {
        public readonly int Seed;
        public long Draws { get; private set; }
        readonly Random random;
        public BattleRandom(int seed) { Seed = seed; random = new Random(seed); }
        public double Next() { Draws++; return random.NextDouble(); }
        public int Index(int count) { if (count < 1) throw new ArgumentOutOfRangeException(nameof(count)); return Math.Min(count - 1, (int)(Next() * count)); }
        public void Shuffle<T>(List<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = Index(i + 1); T v = list[i]; list[i] = list[j]; list[j] = v; } }
    }
    public sealed class BattleState
    {
        public EffectKind kind;
        public string key, source, note;
        public double value;
        public int remaining, appliedAction, resilience;
        public bool negative, rangedOnly, pending;
        public ActionPlan reaction;
        public BattleState Copy() => (BattleState)MemberwiseClone();
    }
    public sealed class Combatant
    {
        public string id, title, monsterId;
        public bool player, backRow, freezeProtection;
        public Panel panel;
        public int hp, shield, phase, actions;
        public double progress, selfProgress;
        public List<BattleState> states = new List<BattleState>();
        public bool Alive => hp > 0;
        public BattleState State(EffectKind k) => states.Find(x => x.kind == k);
        public double Sum(EffectKind k) => states.Where(x => x.kind == k).Sum(x => x.value);
        public double Stat(Attribute a)
        {
            double weak = Math.Max(0, 1 - Sum(EffectKind.Weakness) * .05);
            switch (a)
            {
                case Attribute.Attack: return Math.Max(0, panel.attack + Sum(EffectKind.AttackUp)) * weak;
                case Attribute.SpecialAttack: return Math.Max(0, panel.specialAttack + Sum(EffectKind.SpecialAttackUp)) * weak;
                case Attribute.Defense: return Math.Max(0, (panel.defense + Sum(EffectKind.DefenseUp)) * (1 + Sum(EffectKind.DefensePercent)));
                case Attribute.SpecialDefense: return Math.Max(0, (panel.specialDefense + Sum(EffectKind.SpecialDefenseUp)) * (1 + Sum(EffectKind.SpecialDefensePercent)));
                case Attribute.Speed: return Math.Max(0, (panel.speed + Sum(EffectKind.SpeedUp)) * (1 + Sum(EffectKind.SpeedPercent)));
                case Attribute.MaxHp: return panel.hp;
                case Attribute.LostHp: return panel.hp - hp;
                case Attribute.Shield: return shield;
                case Attribute.BothAttack: return Stat(Attribute.Attack) + Stat(Attribute.SpecialAttack);
                default: return 0;
            }
        }
        public Combatant Copy()
        {
            var u = (Combatant)MemberwiseClone(); u.panel = panel.Copy(); u.states = states.Select(s => s.Copy()).ToList(); return u;
        }
    }
    public sealed class PursuitGroup
    {
        public int count, slot, currentChain;
        public double baseCoefficient;
        public double coefficient => baseCoefficient;
        // Every segment in this source group uses the same fixed coefficient, independent of the source slot.
        public double CoefficientAt(int segment) => coefficient;
        public string Description() => $"来源{slot}号位，当前连锁{currentChain}，追加{count}段；每段固定系数{coefficient:P2}，不受槽位伤害衰减影响，组内不递减；组总系数{coefficient * count:P2}；无属性/资源加值。";
    }
    public sealed class ActionPlan
    {
        public SkillDef skill;
        public int chain, nativeSegments;
        public double nativeCoefficient, nativeTotal, bonus, damageUp, physicalPen, specialPen, crit, critDamage, amplify, fixedUp, extend;
        public double criticalPen, bypass;
        public bool isMirror;
        public List<ConditionalBonus> conditionalBonuses = new List<ConditionalBonus>();
        public List<MirrorGroup> mirrors = new List<MirrorGroup>();
        public int extraStacks;
        public List<PursuitGroup> pursuits = new List<PursuitGroup>();
        public List<string> sourceCards = new List<string>();
        public DebugLoadout sourceLoadout;
        public List<int> currentChains = new List<int>();
        public List<string> traitHints = new List<string>();
        public List<string> durationSources = new List<string>();
        public int Duration(int basis) => basis + (int)Math.Ceiling(basis * extend - 1e-10);
        public string DurationDescription(int basis) => $"持续时间：基础{basis}回合；各条延续[{(durationSources.Count == 0 ? "无" : string.Join("；", durationSources))}]；累计延长比例{extend:P2}；最终{basis}＋ceil({basis}×{extend:F4})＝{Duration(basis)}回合（汇总后只向上取整一次；强化不延长持续时间）。";
    }
    public sealed class DamageQuote
    {
        public int damage;
        public double defense, effectiveDefense, raw, reduction, penetration, damageUp;
    }
    public static class BattleMath
    {
        public static string AttributeName(Attribute a)
        {
            switch (a)
            {
                case Attribute.Attack: return "物攻"; case Attribute.SpecialAttack: return "特攻"; case Attribute.Defense: return "物防"; case Attribute.SpecialDefense: return "特防";
                case Attribute.Speed: return "速度"; case Attribute.MaxHp: return "最大生命"; case Attribute.LostHp: return "已损失生命"; case Attribute.Shield: return "当前护盾"; default: return "物攻＋特攻";
            }
        }
        public static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
        public static int Round(double v) => (int)Math.Floor(v + .5 + 1e-9);
        public static bool Matches(MatchType trait, MatchType main) => trait == MatchType.Universal || trait == main || trait == MatchType.OffenseSurvival && (main == MatchType.Offense || main == MatchType.Survival);
        public static bool IsFixed(EffectDef e) => e.kind == EffectKind.Shield || e.kind == EffectKind.Heal || !e.percent && !e.negative && (e.kind == EffectKind.AttackUp || e.kind == EffectKind.SpecialAttackUp || e.kind == EffectKind.DefenseUp || e.kind == EffectKind.SpecialDefenseUp || e.kind == EffectKind.SpeedUp);
        public static DamageQuote Quote(RuleSettings rules, Combatant actor, Combatant target, ActionPlan plan, double coefficient, double allocatedBonus, double resource, bool critical)
        {
            double defense = target.Stat(plan.skill.damageType == DamageType.Special ? Attribute.SpecialDefense : Attribute.Defense);
            double pen = (plan.skill.damageType == DamageType.Special ? plan.specialPen : plan.physicalPen) + (critical ? plan.criticalPen : 0);
            pen = Clamp(pen,0,rules.penetrationCap);
            double damageUp = plan.damageUp + plan.conditionalBonuses.Where(c=>c.Applies(target)).Sum(c=>c.amount);
            double effective = defense * (1 - Clamp(pen, 0, rules.penetrationCap));
            double reduction = plan.skill.reach == Reach.Melee ? Clamp(target.Sum(EffectKind.MeleeReduction), 0, rules.sustainedReductionCap) : 0;
            double raw = (actor.Stat(plan.skill.attribute) * coefficient + allocatedBonus + resource) * rules.defenseConstant / (rules.defenseConstant + effective) * (1 + damageUp) * (critical ? plan.critDamage : 1) * (1 - reduction);
            return new DamageQuote { damage = Math.Max(1, Round(raw)), defense = defense, effectiveDefense = effective, raw = raw, reduction = reduction, penetration = pen, damageUp = damageUp };
        }
    }
    public enum VisualNode { CastStart, Launch, Impact, CastEnd, Death, Heal, Shield, StateChanged, ActionEnd }
    [Serializable] public sealed class VisualStateFrame
    {
        public EffectKind kind; public string key, source; public double value;
        public int remaining; public bool pending, negative, reaction;
    }
    [Serializable] public sealed class VisualUnitFrame
    {
        public string id; public int hp, shield, phase, actions; public double progress;
        public Panel panel; public List<VisualStateFrame> states;
        public static VisualUnitFrame Capture(Combatant u) => new VisualUnitFrame { id=u.id,hp=u.hp,shield=u.shield,phase=u.phase,actions=u.actions,progress=u.progress,panel=u.panel.Copy(),
            states=u.states.Select(s=>new VisualStateFrame { kind=s.kind,key=s.key,source=s.source,value=s.value,remaining=s.remaining,pending=s.pending,negative=s.negative,reaction=s.reaction!=null }).ToList() };
        public Combatant Apply(Combatant source)
        {
            var u=source.Copy(); u.hp=hp; u.shield=shield; u.phase=phase; u.actions=actions; u.progress=progress; u.panel=panel.Copy();
            u.states=states.Select(s=>new BattleState { kind=s.kind,key=s.key,source=s.source,value=s.value,remaining=s.remaining,pending=s.pending,negative=s.negative,reaction=s.reaction?new ActionPlan():null }).ToList(); return u;
        }
    }
    [Serializable] public sealed class BattleVisualEvent
    {
        public VisualNode node;
        public string actor, target, skill, segment, flightFrom;
        public int action, damage, shield, calculated, overkill, amount, eventIndex, playbackGroup, damageIndex=-1;
        public bool critical, dodged, blocked;
        public List<VisualUnitFrame> units;
    }
    public sealed partial class BattleEngine
    {
        int nextPlaybackGroup;
        public readonly List<BattleVisualEvent> VisualEvents = new List<BattleVisualEvent>();
        void Visual(VisualNode node, string actor, string target, string skill, string segment = "", DamageRecord record = null, int amount=0, string flightFrom=null)
        {
            if (PreviewMode) return;
            VisualEvents.Add(new BattleVisualEvent { node=node, actor=actor, target=target, skill=skill, segment=segment, action=ActionSerial,
                damage=record?.hpLost ?? 0, shield=record?.absorbed ?? 0, calculated=record?.calculated ?? 0, overkill=record?.overkill ?? 0, amount=amount,
                damageIndex=record!=null?Damage.IndexOf(record):-1,eventIndex=VisualEvents.Count,flightFrom=flightFrom??actor,
                critical=record?.critical ?? false, dodged=record?.dodged ?? false, blocked=record?.blocked ?? false,
                units=Units.Select(VisualUnitFrame.Capture).ToList() });
        }
        public readonly BattleCatalog Data;
        public readonly BattleRandom Rng;
        public List<Combatant> Units = new List<Combatant>();
        public List<string> Log = new List<string>();
        public List<string> Waiting = new List<string>();
        public string ActiveId;
        public int ActionSerial, TotalHpDamage, TotalShieldAbsorbed, Criticals, Dodges, Blocks;
        public int TotalCalculatedDamage, TotalOverkill;
        public List<DamageRecord> Damage = new List<DamageRecord>();
        public List<TargetDamageSummary> TargetTotals = new List<TargetDamageSummary>();
        public BattleReplay Replay { get; private set; }
        DebugLoadout loadout;
        public DebugLoadout Loadout => loadout?.Copy();
        public bool IsResolving { get; private set; }
        public double NaturalTime;
        public bool PreviewMode;
        public Combatant Player => Units.Find(x => x.player);
        public Combatant Active => Units.Find(x => x.id == ActiveId);
        public bool Finished => !Units.Any(x => x.player && x.Alive) || !Units.Any(x => !x.player && x.Alive);
        public bool Won => Units.Any(x => x.player && x.Alive) && !Units.Any(x => !x.player && x.Alive);
        public BattleEngine(BattleCatalog data, int seed, DebugLoadout configuration = null)
        {
            Data = data; Rng = new BattleRandom(seed);
            if (configuration != null) { string error = configuration.Validate(data); if (error != null) throw new ArgumentException(error); loadout = configuration.Copy(); }
        }
        public CardDef Card(string id, DebugLoadout configuration = null)
        {
            var original = Data.Card(id); var entry = (configuration ?? loadout)?.cards.Find(c => c.cardId == id);
            return new CardDef { id = original.id, skill = original.skill, match = original.match, traits = new List<string>(entry?.traits ?? original.traits) };
        }
        public string ApplyLoadout(DebugLoadout configuration)
        {
            if (IsResolving || Active != null && !Active.player && !Finished) return "仅开战前、玩家排谱时或战斗结束后可应用；本次可保存供下次开战使用。";
            string error = configuration.Validate(Data); if (error != null) return error;
            if (configuration.weaponId != Player.title) return "不能应用其他武器的配置。";
            loadout = configuration.Copy(); Replay?.steps.Add(new ReplayStep { kind = "loadout", action = ActionSerial, loadout = loadout.Copy() });
            Write("应用调试词条快照：从下一次确认出招生效；已有姿态保留施放时的词条。"); return null;
        }
        public void Write(string text) { Log.Add($"[{ActionSerial:000} t={NaturalTime:F3}] {text}"); }
        public void Start(int weapon, int encounter)
        {
            var w = Data.weapons[weapon];
            if (loadout != null && loadout.weaponId != w.id) throw new ArgumentException("配置与开战武器不一致");
            loadout = loadout ?? DebugLoadout.Initial(Data, w.id);
            Replay = new BattleReplay { seed = Rng.Seed, encounter = encounter, weaponId = w.id, initial = loadout.Copy() };
            Units.Add(new Combatant { id = "player", title = w.title, player = true, panel = w.panel.Copy(), hp = w.panel.hp });
            int i = 0;
            foreach (var spawn in Data.encounters[encounter].enemies)
            {
                var m = Data.monsters.Find(x => x.id == spawn.monster);
                var u = new Combatant { id = "enemy" + i++, title = m.title, monsterId = m.id, backRow = spawn.backRow, panel = m.panel.Copy(), hp = m.panel.hp };
                Units.Add(u); ApplyPhase(u, false);
            }
            Write($"开战：{w.title} / {Data.encounters[encounter].title} / seed={Rng.Seed} / {Data.sourceVersion}");
            Write("测试参数：中毒每层2；灼伤5。待定项见界面。所有随机由同一seed管理。");
        }
        public List<Combatant> LegalTargets(Combatant actor, SkillDef skill)
        {
            var alive = Units.Where(x => x.Alive).ToList();
            if (skill.target == TargetMode.Self) return actor.Alive ? new List<Combatant> { actor } : new List<Combatant>();
            if (skill.target == TargetMode.Ally || skill.target == TargetMode.OtherAlly || skill.target == TargetMode.AllAllies) return alive.Where(x => x.player == actor.player && (skill.target != TargetMode.OtherAlly || x != actor)).ToList();
            var enemies = alive.Where(x => x.player != actor.player).ToList();
            if (skill.reach == Reach.Melee || skill.target == TargetMode.FrontRow)
            {
                if (enemies.Any(x => !x.backRow)) enemies.RemoveAll(x => x.backRow);
            }
            return enemies;
        }
        public string Validate(Combatant actor, IReadOnlyList<string> cards, string target)
        {
            if (actor == null || !actor.Alive || actor.id != ActiveId) return "尚未轮到玩家行动。";
            if (cards.Count < 1 || cards.Count > 5) return "请选择1～5张连续排谱，推荐五张。";
            if (cards.Distinct().Count() != cards.Count) return "同一实体卡不能重复使用。";
            var weapon = Data.weapons.Find(x => x.id == actor.title);
            if (weapon == null || cards.Any(c => !weapon.cards.Contains(c))) return "不能使用其他武器的实体卡。";
            var skill = Data.Skill(Data.Card(cards[0]).skill);
            var legal = LegalTargets(actor, skill);
            if (legal.Count == 0) return "无法施放：没有另一名己方单位。此卡仍可在后续槽位提供词条。";
            if (!legal.Any(x => x.id == target)) return "请选择合法目标（近战优先前排）。";
            // Unspecified repeat magnitudes must not silently become official stacking rules.
            if (skill.pattern == Pattern.Counter && actor.states.Any(x => x.reaction != null)) return "待定：反击姿态重复处理未确认，请等待原姿态结束。";
            foreach (var e in skill.effects)
            {
                if (!e.HasDuration || e.stacking || e.kind == EffectKind.Evade || e.kind == EffectKind.CritUp || e.kind == EffectKind.Burn || e.kind == EffectKind.DebuffImmune) continue;
                var receiver = legal.Find(x => x.id == target);
                if (receiver.states.Any(s => s.key == skill.id + ":" + e.kind)) return "待定：该限时效果重复幅度未确认，请等待原效果结束。";
            }
            return null;
        }
        public ActionPlan Plan(Combatant actor, IReadOnlyList<string> cards, SkillDef overrideSkill = null, DebugLoadout configuration = null)
        {
            var main = cards.Count > 0 ? Card(cards[0], configuration) : null;
            var skill = overrideSkill ?? Data.Skill(main.skill);
            int n = cards.Count; // Enemies pass no cards; explicit skill simulations retain their actual composition length.
            var p = new ActionPlan { skill = skill, chain = n, nativeCoefficient = skill.coefficient.At(n), nativeSegments = Math.Max(1, (int)skill.segments.At(n)), crit = actor.player ? actor.panel.crit + actor.Sum(EffectKind.CritUp) + skill.critBonus.At(n) : 0, critDamage = actor.panel.critDamage };
            p.sourceCards = cards.ToList();
            p.sourceLoadout = (configuration ?? loadout)?.Copy();
            if (skill.pattern == Pattern.Bounce) p.nativeSegments = LegalTargets(actor, skill).Count >= 2 ? 1 + n : 1;
            p.nativeTotal = skill.pattern == Pattern.Bounce ? p.nativeCoefficient + (p.nativeSegments - 1) * skill.bounceCoefficient : p.nativeCoefficient * p.nativeSegments;
            if (skill.damageType == DamageType.Physical) p.physicalPen = skill.penetration.At(n); else p.specialPen = skill.penetration.At(n);
            for (int i = 0; i < cards.Count; i++)
            {
                var card = Card(cards[i], configuration); int slot = i + 1;
                // Resolve only this card's chain modifiers before reading any other trait, independent of trait order.
                int chainReactions = card.traits.Select(id => Data.Trait(id)).Count(t => t.id == "连锁反应" && TraitSupport.Enabled(t) && BattleMath.Matches(t.match, main.match));
                int x = slot * (1 + chainReactions); p.currentChains.Add(x);
                if(chainReactions > 0) p.traitHints.Add($"{slot}号位 当前连锁修正：基础{slot} × (1＋{chainReactions}条连锁反应) = {x}；仅本卡，实际槽位和总连锁不变，不受收益放大。");
                double gain = TraitGain(card, i, x, cards, main.match, p);
                double z = x * gain;
                for (int traitIndex = 0; traitIndex < card.traits.Count; traitIndex++)
                {
                    var tid = card.traits[traitIndex];
                    var t = Data.Trait(tid); bool matched = BattleMath.Matches(t.match, main.match);
                    string reason = !TraitSupport.Enabled(t) ? TraitSupport.Status(t) + "：" + TraitSupport.Reason(t) : !matched ? "类型不匹配" : null;
                    bool attack = p.nativeCoefficient > 0;
                    bool amount = skill.effects.Any(e => e.kind == EffectKind.Shield || e.kind == EffectKind.Heal);
                    if (reason == null)
                    {
                        switch (t.id)
                        {
                            case "连锁收益": case "连锁蓄势": case "连锁协奏": case "同调": case "交织":
                                reason = GainActive(t.id,i,x,cards) ? "有效（只放大本卡其他词条）" : "条件未触发，不提供放大"; break;
                            case "连锁反应": break; // Already resolved in the isolated pre-pass; never amplified.
                            case "锋锐": if (attack) p.damageUp += z * t.rate; else reason = "无直接伤害字段"; break;
                            case "强化":
                                if (attack) p.damageUp += z * t.rate;
                                if (skill.effects.Any(BattleMath.IsFixed)) p.fixedUp += z * .1;
                                if (!attack && !skill.effects.Any(BattleMath.IsFixed)) reason = "无直接伤害或固定量字段";
                                break;
                            case "穿甲": if (attack && skill.damageType == DamageType.Physical) p.physicalPen += z * t.rate; else reason = "无物理伤害字段"; break;
                            case "破魔": if (attack && skill.damageType == DamageType.Special) p.specialPen += z * t.rate; else reason = "无特殊伤害字段"; break;
                            case "追击": if (attack) p.pursuits.Add(new PursuitGroup { slot = slot, currentChain = x, count = BattleMath.Round(z), baseCoefficient = t.rate }); else reason = "无直接伤害字段"; break;
                            case "专注": if(attack) p.crit += z*t.rate; else reason="无直接伤害字段"; break;
                            case "压制": case "终结": case "乘隙":
                                if(attack) p.conditionalBonuses.Add(new ConditionalBonus { id=t.id,slot=slot,currentChain=x,amount=z*t.rate,threshold=t.threshold }); else reason="无直接伤害字段"; break;
                            case "裂隙": if(attack) p.criticalPen+=z*t.rate; else reason="无直接伤害字段"; break;
                            case "贯通": if(attack) p.bypass+=z*t.rate; else reason="无直接伤害字段"; break;
                            case "镜像":
                                double strength=z*t.rate; string boundary=MirrorRestriction(skill,n,strength);
                                if(boundary==null) p.mirrors.Add(new MirrorGroup {slot=slot,currentChain=x,strength=strength});
                                else reason="镜像不执行："+boundary+"；主技能照常结算";
                                break;
                            case "破势": if (attack) p.critDamage += z * t.rate; else reason = "无暴击字段"; break;
                            case "壁障": if (attack || amount) p.bonus += actor.Stat(Attribute.Defense) * z * t.rate; else reason = "无伤害/护盾/恢复字段"; break;
                            case "灵动": if (attack || amount) p.bonus += actor.Stat(Attribute.Speed) * z * t.rate; else reason = "无伤害/护盾/恢复字段"; break;
                            case "顽强": if (attack || amount) p.bonus += actor.panel.hp * z * t.rate; else reason = "无伤害/护盾/恢复字段"; break;
                            case "深化": if (skill.effects.Any(e => e.stacking)) p.extraStacks += BattleMath.Round(z); else reason = "无可叠层状态字段"; break;
                            case "增幅": if (skill.effects.Any(e => e.percent && !e.negative) || skill.critBonus.At(n) > 0) p.amplify += z * t.rate; else reason = "无概率/比例/推条字段"; break;
                            case "延续":
                                if (skill.HasDuration || skill.effects.Any(e => e.HasDuration))
                                {
                                    double contribution = z * t.rate; p.extend += contribution;
                                    p.durationSources.Add($"{slot}号位·词条{traitIndex + 1} 当前连锁{x}×{t.rate:P0}×收益倍率{gain:F2}＝{contribution:P2}");
                                }
                                else reason = "无持续回合字段";
                                break;
                            default: reason = "待接入词条"; break;
                        }
                    }
                    p.traitHints.Add($"{slot}号位·词条{traitIndex + 1} {t.title}〔当前连锁{x}；本卡收益倍率{gain:F2}〕：{reason ?? "有效"}");
                }
            }
            p.bypass = BattleMath.Clamp(p.bypass,0,1);
            p.crit = BattleMath.Clamp(p.crit + skill.critBonus.At(n) * p.amplify, 0, Data.rules.critCap);
            p.physicalPen = BattleMath.Clamp(p.physicalPen, 0, Data.rules.penetrationCap); p.specialPen = BattleMath.Clamp(p.specialPen, 0, Data.rules.penetrationCap);
            return p;
        }
        string PursuitInheritance(ActionPlan p) => $"追击继承：计算属性[{BattleMath.AttributeName(p.skill.attribute)}]、{p.skill.damageType}伤害与{p.skill.reach}射程；主技能内置穿透{p.skill.penetration.At(p.chain):P2}、内置暴击率加值{p.skill.critBonus.At(p.chain):P2}作用于追击；最终增伤{p.damageUp:P2}、物穿{p.physicalPen:P2}、特穿{p.specialPen:P2}、暴击率{p.crit:P2}、暴击倍率{p.critDamage:F3}。不继承主技能伤害系数/段数、附带状态/控制、词条属性加值或消耗资源加值，不再生成追击。";
        public void Advance(double dt)
        {
            if (Active != null || Finished) return;
            PruneWaiting(); EnqueueReady();
            if (Waiting.Count > 0) { BeginNext(); return; }
            double until = double.PositiveInfinity;
            foreach (var u in Units.Where(u => u.Alive && u.State(EffectKind.Freeze) == null && u.Stat(Attribute.Speed) > 0)) until = Math.Min(until, (100 - u.progress) / u.Stat(Attribute.Speed));
            if (double.IsInfinity(until)) return;
            double step = Math.Min(Math.Max(0, dt), until); NaturalTime += step;
            foreach (var u in Units.Where(u => u.Alive && u.State(EffectKind.Freeze) == null)) u.progress = Math.Min(100, u.progress + u.Stat(Attribute.Speed) * step);
            EnqueueReady(); if (Waiting.Count > 0) BeginNext();
        }
        void PruneWaiting() => Waiting.RemoveAll(id => !Units.Any(u => u.id == id && u.Alive && u.progress >= 100 - 1e-7 && u.State(EffectKind.Freeze) == null));
        void EnqueueReady()
        {
            var ready = Units.Where(u => u.Alive && u.id != ActiveId && u.progress >= 100 - 1e-7 && u.State(EffectKind.Freeze) == null && !Waiting.Contains(u.id)).ToList();
            if (ready.Count == 0) return;
            if (!PreviewMode) Rng.Shuffle(ready);
            Waiting.AddRange(ready.Select(x => x.id)); Write("到条实际排序：" + string.Join(" → ", ready.Select(x => x.title + "(" + x.id + ")")));
        }
        void BeginNext()
        {
            PruneWaiting(); if (Waiting.Count == 0) return;
            ActiveId = Waiting[0]; Waiting.RemoveAt(0); ActionSerial++;
            foreach (var u in Units)
            {
                var freeze = u.State(EffectKind.Freeze);
                if (freeze != null && freeze.source == ActiveId) Unfreeze(u);
            }
            Write("开始行动：" + Active.title + "；全场自然跑条暂停");
        }
        public void EnemyAction()
        {
            var a = Active; if (a == null || a.player || Finished) return;
            var def = Data.monsters.Find(m => m.id == a.monsterId);
            var skill = Data.Skill(def.phases[a.phase].skill);
            var targets = LegalTargets(a, skill);
            IsResolving = true;
            try { if (targets.Count > 0) Execute(a, Plan(a, Array.Empty<string>(), skill), targets[0].id); EndAction(a); }
            finally { IsResolving = false; }
        }
        public string Confirm(IReadOnlyList<string> cards, string target)
        {
            string error = Validate(Player, cards, target); if (error != null) return error;
            if (IsResolving) return "出招结算中，不能再次确认。";
            Replay.steps.Add(new ReplayStep { kind = "action", action = ActionSerial, cards = cards.ToList(), target = target });
            Write("排谱：" + string.Join(" → ", cards.Select(id => Data.Skill(Data.Card(id).skill).title)) + "；目标=" + Units.Find(x => x.id == target).title);
            IsResolving = true;
            try { Execute(Player, Plan(Player, cards), target); EndAction(Player); return null; }
            finally { IsResolving = false; }
        }
        sealed class CastContext
        {
            public string bounceFrom;
            public HashSet<string> reacted = new HashSet<string>(), effects = new HashSet<string>();
            public Dictionary<string, double> pushed = new Dictionary<string, double>();
        }
        Combatant RandomTarget(List<Combatant> targets) => targets.Count == 0 ? null : targets[PreviewMode ? 0 : Rng.Index(targets.Count)];
        public void Execute(Combatant actor, ActionPlan plan, string targetId)
        { ExecuteCore(actor,plan,targetId,null); }
        void ExecuteCore(Combatant actor, ActionPlan plan, string targetId, CastContext context)
        {
            var skill = plan.skill; var ctx = context ?? new CastContext();
            Visual(VisualNode.CastStart,actor.id,targetId,skill.id,plan.isMirror?"镜像":"");
            try {
            Write(actor.title + " 施放 " + skill.title + $"，总连锁{plan.chain}；加值{plan.bonus:F2}，增伤{plan.damageUp:P0}");
            foreach (var hint in plan.traitHints) Write(hint);
            Write($"原生计划{plan.nativeSegments}段/每目标，总系数{plan.nativeTotal:P3}；追击{plan.pursuits.Sum(g => g.count)}段；攻击属性[{BattleMath.AttributeName(skill.attribute)}]={actor.Stat(skill.attribute):F3}；暴击率{plan.crit:P2} 暴击倍率{plan.critDamage:F3}");
            foreach (var g in plan.pursuits) Write("追击组：" + g.Description());
            if (plan.pursuits.Count > 0) Write(PursuitInheritance(plan));
            if (skill.pattern == Pattern.Counter)
            {
                Write($"主技能总连锁{plan.chain}，基础持续公式{skill.duration}＋{skill.durationPerChain}×总连锁；"+plan.DurationDescription(skill.BaseDuration(plan.chain)));
                actor.states.Add(new BattleState { kind = EffectKind.AttackUp, key = skill.id + ":counter", source = actor.id, remaining = plan.Duration(skill.BaseDuration(plan.chain)), appliedAction = ActionSerial, reaction = plan });
                Write("进入反击姿态；合法近战目标，每次敌方出招最多反击一次；词条随姿态保存。"); return;
            }
            var legal = LegalTargets(actor, skill); var target = legal.Find(x => x.id == targetId);
            if (target == null) return;
            bool damaging = plan.nativeCoefficient > 0;
            var nativeTargets = skill.pattern == Pattern.Sweep || skill.target == TargetMode.AllAllies ? new List<Combatant>(legal) : new List<Combatant> { target };
            int total = plan.nativeSegments * nativeTargets.Count;
            double allocation = total > 0 ? plan.bonus / total : 0;
            if (damaging) Write($"属性加值分摊：整次{plan.bonus:F3} ÷（实际原生目标{nativeTargets.Count} × 每目标原生段数{plan.nativeSegments}）= {plan.bonus:F3} ÷ {total} = 每段{allocation:F4}（不含追击/落地）。");
            double resource = 0;
            if (skill.shieldCost > 0)
            {
                int consumed = BattleMath.Round(actor.shield * skill.shieldCost); actor.shield -= consumed; resource = consumed * skill.shieldResource;
                Visual(VisualNode.Shield,actor.id,actor.id,skill.id,"碎盾消耗",amount:-consumed);
                Write($"碎盾一次消耗{consumed}护盾，原生段资源加值{resource:F2}；追击不复制。");
            }
            if (damaging)
            {
                foreach (var original in nativeTargets)
                {
                    int nativeGroup = ++nextPlaybackGroup;
                    var current = original;
                    for (int i = 0; i < plan.nativeSegments && actor.Alive; i++)
                    {
                        var available = LegalTargets(actor, skill);
                        if (skill.pattern == Pattern.Bounce && i > 0)
                        {
                            available.RemoveAll(x => x == current); current = RandomTarget(available);
                            if (current == null) { Write("弹射停止：没有另一名合法目标。"); break; }
                            Write("弹射换目标 → " + current.title);
                        }
                        else if (current == null || !current.Alive || !available.Contains(current))
                        {
                            current = RandomTarget(available); if (current == null) break;
                            Write("死亡转火 → " + current.title + "；重新读取防御");
                        }
                        double coeff = skill.pattern == Pattern.Bounce && i > 0 ? skill.bounceCoefficient : plan.nativeCoefficient;
                        bool hit = Hit(actor, current, plan, coeff, allocation, i == 0 ? resource : 0, i == 0 ? "原生" : skill.pattern == Pattern.Bounce ? "弹射" : "原生", ctx, nativeGroup);
                        if (hit && current.Alive)
                        {
                            foreach (var e in skill.effects.Where(e => e.onHit))
                            {
                                string key = current.id + ":" + e.kind;
                                if (ctx.effects.Add(key)) ApplyEffect(actor, current, plan, e, ctx);
                            }
                            if (i == 0 && skill.landingCoefficient > 0 && Data.rules.enableLandingTest && current.Alive)
                            {
                                Write($"挑飞落地测试：第二段{skill.landingCoefficient:P2}，独立命中/暴击/格挡/闪避，不模拟碰撞另一敌人；不分配词条加值。");
                                Hit(actor, current, plan, skill.landingCoefficient, 0, 0, "落地测试", ctx);
                            }
                        }
                    }
                    target = current;
                }
                foreach (var group in plan.pursuits)
                {
                    int pursuitGroup = ++nextPlaybackGroup;
                    for (int i = 0; i < group.count && actor.Alive; i++)
                    {
                        legal = LegalTargets(actor, skill);
                        if (target == null || !target.Alive || !legal.Contains(target))
                        {
                            target = RandomTarget(legal); if (target == null) break; Write("追击死亡转火 → " + target.title);
                        }
                        Hit(actor, target, plan, group.CoefficientAt(i), 0, 0, $"追击[{group.slot}号位 {i + 1}/{group.count}]", ctx, pursuitGroup);
                    }
                }
            }
            else foreach (var recipient in nativeTargets) foreach (var e in skill.effects.Where(e => !e.onHit)) ApplyEffect(actor, recipient, plan, e, ctx);
            ExecuteMirrors(actor,plan,targetId,ctx);
            } finally { Visual(VisualNode.CastEnd,actor.id,targetId,skill.id,plan.isMirror?"镜像":""); }
        }
        bool Hit(Combatant actor, Combatant target, ActionPlan plan, double coefficient, double bonus, double resource, string segment, CastContext ctx, int playbackGroup = 0)
        {
            if (!target.Alive) return false;
            if(plan.isMirror) segment="镜像·"+segment;
            var record = new DamageRecord { action = ActionSerial, source = actor.id, target = target.id, label = segment, skill = plan.skill.id, mirror=plan.isMirror, bypassRatio=plan.bypass, attackAttribute = actor.Stat(plan.skill.attribute), coefficient = coefficient, bonus = bonus, resource = resource, damageUp = plan.damageUp, critRate = plan.crit, critMultiplier = plan.critDamage, defense = target.Stat(plan.skill.damageType == DamageType.Special ? Attribute.SpecialDefense : Attribute.Defense), penetration = plan.skill.damageType == DamageType.Special ? plan.specialPen : plan.physicalPen };
            record.effectiveDefense = record.defense * (1 - record.penetration);
            foreach(var condition in plan.conditionalBonuses) Write($"{segment} 条件词条{condition.id}[{condition.slot}号位/当前连锁{condition.currentChain}] → {target.title} HP{target.hp}/{target.panel.hp}：{(condition.Applies(target)?"生效":"未触发")} 增伤{(condition.Applies(target)?condition.amount:0):P2}");
            int visualStart = VisualEvents.Count;
            try {
            Visual(VisualNode.Launch,actor.id,target.id,plan.skill.id,segment,flightFrom:plan.skill.pattern==Pattern.Bounce && segment.EndsWith("弹射")?ctx.bounceFrom:actor.id);
            if(plan.skill.pattern==Pattern.Bounce) ctx.bounceFrom=target.id;
            double evade = BattleMath.Clamp(target.Sum(EffectKind.Evade), 0, Data.rules.evadeCap);
            if (!PreviewMode && evade > 0 && Rng.Next() < evade) { Dodges++; record.dodged = true; Damage.Add(record); Visual(VisualNode.Impact,actor.id,target.id,plan.skill.id,segment,record); Write($"{segment} → {target.title} 闪避；不消耗格挡/护盾。"); return false; }
            var block = target.states.Find(s => s.kind == EffectKind.Block && s.value > 0 && (!s.rangedOnly || plan.skill.reach == Reach.Ranged));
            if (block != null && !PreviewMode)
            {
                block.value--; Blocks++; record.blocked = true; Damage.Add(record); Write($"{segment} → {target.title} 格挡，剩余{block.value}；未命中，无附带效果。");
                if (block.value <= 0) target.states.Remove(block); Visual(VisualNode.Impact,actor.id,target.id,plan.skill.id,segment,record); React(actor, target, ctx); return false;
            }
            bool critical = !PreviewMode && plan.crit > 0 && Rng.Next() < plan.crit;
            if (critical) Criticals++;
            var q = BattleMath.Quote(Data.rules, actor, target, plan, coefficient, bonus, resource, critical);
            record.critical = critical; record.unrounded = q.raw; record.penetration=q.penetration; record.damageUp=q.damageUp; record.effectiveDefense=q.effectiveDefense; record.defense=q.defense;
            if(plan.criticalPen>0) Write($"裂隙：{(critical?"暴击生效":"非暴击不生效")}，对应额外穿透{(critical?plan.criticalPen:0):P2}；最终对应穿透{q.penetration:P2}（共用上限{Data.rules.penetrationCap:P0}）");
            Write($"{segment} → {target.title} 系数{coefficient:P2} 加值{bonus:F2} 资源{resource:F2} 防御{q.defense:F2}→{q.effectiveDefense:F2} {(critical ? "暴击 " : "")}减伤{q.reduction:P0} 伤害{q.damage}（原值{q.raw:F3}）");
            Write($"逐段核对：攻击属性{record.attackAttribute:F3} 穿透{record.penetration:P2} 有效防御{q.effectiveDefense:F3} 增伤{q.damageUp:P2} 暴击率{plan.crit:P2} 倍率{(critical ? plan.critDamage : 1):F3}");
            int actual = ApplyDamage(target, q.damage, segment, record);
            var reflect = target.State(EffectKind.Reflect);
            if (reflect != null && actual > 0 && actor.Alive && (target.Alive || Data.rules.reflectAfterDeath))
            {
                Write("棘甲反伤（致死先后为显式测试策略）"); ApplyDamage(actor, Math.Max(0, BattleMath.Round(actual * reflect.value)), "间接反伤", new DamageRecord { action = ActionSerial, source = target.id, target = actor.id, label = "间接反伤" });
            }
            React(actor, target, ctx); return true;
            } finally
            {
                // Tag only this real hit's launch/impact. Nested reactions retain their own groups.
                if (!PreviewMode && playbackGroup > 0)
                {
                    int damageIndex = Damage.IndexOf(record);
                    for (int v = visualStart; v < VisualEvents.Count; v++)
                        if (v == visualStart && VisualEvents[v].node == VisualNode.Launch || VisualEvents[v].node == VisualNode.Impact && VisualEvents[v].damageIndex == damageIndex)
                            VisualEvents[v].playbackGroup = playbackGroup;
                }
            }
        }
        void React(Combatant attacker, Combatant defender, CastContext ctx)
        {
            var stance = defender.states.Find(s => s.reaction != null);
            if (stance == null || !defender.Alive || !attacker.Alive || ctx.reacted.Contains(defender.id)) return;
            var p = Plan(defender, stance.reaction.sourceCards, null, stance.reaction.sourceLoadout);
            // Counter skill targets self while setting stance; reaction explicitly uses melee enemy selection.
            var melee = new SkillDef { target = TargetMode.Enemy, reach = Reach.Melee };
            if (!LegalTargets(defender, melee).Contains(attacker)) { Write("反击未触发：攻击者不符合近战前排限制。"); return; }
            ctx.reacted.Add(defender.id); ctx.reacted.Add(attacker.id);
            Write(defender.title + " 发动反击（本次敌方出招一次）");
            Hit(defender, attacker, p, p.nativeCoefficient, p.bonus, 0, "反击", ctx);
            if (p.pursuits.Count > 0) { Write(PursuitInheritance(p)); foreach (var group in p.pursuits) Write("反击追击组：" + group.Description()); }
            Combatant current = attacker;
            foreach (var g in p.pursuits)
            {
                int pursuitGroup = ++nextPlaybackGroup;
                for (int i = 0; i < g.count && defender.Alive; i++)
            {
                if (!current.Alive)
                {
                    current = RandomTarget(LegalTargets(defender, melee)); if (current == null) break;
                    Write("反击追击死亡转火 → " + current.title);
                }
                Hit(defender, current, p, g.CoefficientAt(i), 0, 0, $"反击追击[{g.slot}号位 {i + 1}/{g.count}]", ctx, pursuitGroup);
            }
        }
        }
        public int ApplyDamage(Combatant target, int amount, string label, DamageRecord record = null)
        {
            amount = Math.Max(0, amount);
            record = record ?? new DamageRecord { action = ActionSerial, target = target.id, label = label, source = "状态/间接" };
            record.bypass = BattleMath.Round(amount * BattleMath.Clamp(record.bypassRatio,0,1)); record.bypassHpLost = Math.Min(target.hp,record.bypass);
            int absorbed = Math.Min(target.shield, amount-record.bypass); target.shield -= absorbed;
            int lost = Math.Min(target.hp, amount - absorbed); target.hp -= lost;
            TotalHpDamage += lost; TotalShieldAbsorbed += absorbed;
            int excess = amount - absorbed - lost; TotalCalculatedDamage += amount; TotalOverkill += excess;
            record.calculated = amount; record.absorbed = absorbed; record.hpLost = lost; record.overkill = excess; Damage.Add(record);
            if(record.bypassRatio>0) Write($"贯通：修正后伤害{amount} × {record.bypassRatio:P2} 四舍五入={record.bypass}直达生命（实际{record.bypassHpLost}）；剩余{amount-record.bypass}正常扣盾；总伤害不增加");
            Visual(VisualNode.Impact,record.source,target.id,record.skill,label,record);
            if(!target.Alive) Visual(VisualNode.Death,record.source,target.id,record.skill,label);
            var total = TargetTotals.Find(s => s.target == target.id);
            if (total == null) { total = new TargetDamageSummary { target = target.id }; TargetTotals.Add(total); }
            total.calculated += amount; total.absorbed += absorbed; total.hpLost += lost; total.overkill += excess;
            Write($"{label} → {target.title} 计算伤害{amount} 护盾吸收{absorbed} 实际扣除生命{lost} 过量伤害{excess}；HP {target.hp}/{target.panel.hp} 盾{target.shield}");
            if (!target.Alive) { Waiting.Remove(target.id); Write(target.title + " 倒下"); }
            return absorbed + lost;
        }
        void ApplyEffect(Combatant actor, Combatant target, ActionPlan plan, EffectDef e, CastContext ctx)
        {
            try {
            double value = e.value.At(plan.chain);
            if (e.percent && !e.negative) value *= 1 + plan.amplify;
            if (BattleMath.IsFixed(e)) value *= 1 + plan.fixedUp;
            if (e.stacking) value += plan.extraStacks;
            if (e.kind == EffectKind.Shield || e.kind == EffectKind.Heal)
            {
                value = actor.Stat(e.attribute) * value + plan.bonus;
                if (e.kind == EffectKind.Shield)
                {
                    int shield = Math.Max(0, BattleMath.Round(value * (1 + target.Sum(EffectKind.ShieldBoost) * .1)));
                    target.shield += shield; Visual(VisualNode.Shield,actor.id,target.id,plan.skill.id,"获得护盾",amount:shield); Write(target.title + " 获得护盾 " + shield + "，合计" + target.shield);
                }
                else
                {
                    int heal = Math.Max(0, BattleMath.Round(value)); int actual = Math.Min(target.panel.hp - target.hp, heal); target.hp += actual; Visual(VisualNode.Heal,actor.id,target.id,plan.skill.id,"治疗",amount:actual);
                    Write(target.title + $" 恢复{actual}（计算{heal}，溢出不转化；溢出规则待定）");
                }
                return;
            }
            if (e.kind == EffectKind.Cleanse)
            {
                var debuffs = target.states.Where(s => s.negative).ToList(); if (!PreviewMode) Rng.Shuffle(debuffs);
                foreach (var state in debuffs.Take((int)value)) { target.states.Remove(state); Write(target.title + " 移除减益全部层数：" + state.kind); }
                return;
            }
            if (e.kind == EffectKind.Push || e.kind == EffectKind.Advance)
            {
                double used; ctx.pushed.TryGetValue(target.id, out used);
                double allowed = Math.Min(value, Math.Max(0, Data.rules.pushCap - used)); ctx.pushed[target.id] = used + allowed;
                Write($"{e.kind}即时行动条：基础{e.value.At(plan.chain):P2}，增幅{plan.amplify:P2}，上限内实际{allowed:P2}；与速度百分比/持续时间分开。");
                Push(target, (e.kind == EffectKind.Advance ? 1 : -1) * allowed * 100); return;
            }
            if (e.kind == EffectKind.Guard) { Write("无法施放：没有其他己方单位；守护多单位接口待接入。"); return; }
            if (e.negative && target.State(EffectKind.DebuffImmune) != null) { Write(target.title + " 免疫减益：" + e.kind); return; }
            if (e.kind == EffectKind.Freeze && target.freezeProtection) { Write(target.title + " 解控保护，免疫冻结"); return; }
            var key = plan.skill.id + ":" + e.kind;
            int basisDuration = e.BaseDuration(plan.chain);
            int duration = plan.Duration(basisDuration);
            if (e.HasDuration) Write(target.title + " " + e.kind + $" 主技能总连锁{plan.chain}，基础持续公式{e.duration}＋{e.durationPerChain}×总连锁；" + plan.DurationDescription(basisDuration));
            if (e.kind == EffectKind.SpeedPercent)
                Write($"速度百分比增益：总连锁{plan.chain}，基础比例{e.value.At(plan.chain):P2} × (1＋增幅{plan.amplify:P2})＝{value:P2}；按(基础速度＋点数增益)×(1＋百分比增益总和)计算，不增加速度点数。");
            // Same status from different sources shares one poison/burn/weakness pool.
            var existing = target.states.Find(s => e.stacking || e.kind == EffectKind.Burn || e.kind == EffectKind.Evade || e.kind == EffectKind.CritUp ? s.kind == e.kind : s.key == key);
            if (existing != null)
            {
                if (e.kind == EffectKind.Freeze) return;
                existing.value = e.stacking ? existing.value + value : Math.Max(existing.value, value);
                int previousDuration = existing.remaining;
                existing.remaining = Math.Max(existing.remaining, duration);
                // Refreshing during the owner's action does not consume the refreshed duration immediately.
                existing.appliedAction = ActionSerial;
                Write(target.title + $" 刷新{e.kind}：值{existing.value:F2}，剩余max(旧{previousDuration},本次{duration})＝{existing.remaining}回合，不累加回合；幅度沿用既有叠层/取较大值规则。");
            }
            else
            {
                target.states.Add(new BattleState { kind = e.kind, key = key, source = actor.id, value = value, remaining = duration, appliedAction = ActionSerial, negative = e.negative, rangedOnly = e.rangedOnly, pending = e.pending, note = e.note });
                Write(target.title + $" 获得{e.kind}：值{value:F2} 持续{duration}" + (e.pending ? " 【待定：仅记录】" : ""));
            }
            if (e.kind == EffectKind.SpeedPercent) Write(target.title + $" 速度面板：基础{target.panel.speed:F3}，点数增益{target.Sum(EffectKind.SpeedUp):F3}，百分比增益总和{target.Sum(EffectKind.SpeedPercent):P2} → 实际速度{target.Stat(Attribute.Speed):F3}");
            } finally { Visual(VisualNode.StateChanged,actor.id,target.id,plan.skill.id,e.kind.ToString()); }
        }
        public void Push(Combatant target, double points)
        {
            if (target.State(EffectKind.Freeze) != null) { Write("冻结中：不接受推拉条"); return; }
            double before = target.progress; target.progress = BattleMath.Clamp(before + points, 0, 100);
            if (target.id == ActiveId && points > 0) target.selfProgress = BattleMath.Clamp(target.selfProgress + points, 0, 100);
            Write(target.title + $" 行动条{before:F2}%→{target.progress:F2}%（实际位移{target.progress - before:F2}%）");
            if (target.State(EffectKind.Bleed) != null) Write("流血位移触发：伤害系数待定，本版不扣血。");
            if (target.progress < 100 && Waiting.Remove(target.id)) Write(target.title + " 被击退，退出等待队列");
        }
        public void EndAction(Combatant actor)
        {
            if (actor.Alive)
            {
                var poison = actor.State(EffectKind.Poison);
                if (poison != null)
                {
                    ApplyDamage(actor, BattleMath.Round(poison.value * Data.rules.poisonPerStack), "中毒即时层数跳伤");
                    poison.value--; Write(actor.title + " 中毒先跳伤后减1层，剩余" + poison.value);
                    if (poison.value <= 0) actor.states.Remove(poison);
                }
                var burn = actor.State(EffectKind.Burn);
                if (actor.Alive && burn != null) ApplyDamage(actor, Data.rules.burnDamage, "灼伤");
            }
            foreach (var s in actor.states.ToArray())
            {
                if (s.remaining <= 0) continue;
                if (s.appliedAction == ActionSerial) { Write(actor.title + " 状态计时 " + s.key + $"：持有者行动结束，施加/刷新当次跳过扣减，剩余{s.remaining}回合"); continue; }
                int before = s.remaining;
                s.remaining--; Write(actor.title + " 状态计时 " + s.key + $"：持有者[{actor.id}]行动结束 {before}→{s.remaining}回合");
                if (s.remaining == 0) { actor.states.Remove(s); Write(actor.title + " 状态解除 " + s.key); }
            }
            actor.actions++; actor.progress = actor.selfProgress; actor.selfProgress = 0; actor.freezeProtection = false;
            if (!actor.player && actor.Alive)
            {
                var monster = Data.monsters.Find(x => x.id == actor.monsterId); actor.phase = (actor.phase + 1) % monster.phases.Count; ApplyPhase(actor, true);
            }
            foreach (var other in Units.Where(u => u != actor && u.Alive))
            {
                var frozen = other.State(EffectKind.Freeze);
                if (frozen != null && frozen.appliedAction != ActionSerial)
                {
                    frozen.resilience++; Write(other.title + " 冻结韧性" + frozen.resilience);
                    if (frozen.resilience >= Data.rules.freezeResilience) Unfreeze(other);
                }
            }
            Write(actor.title + " 行动结束，进度归零后保留自拉条" + actor.progress.ToString("F2") + "%"); ActiveId = null; PruneWaiting();
            if (Finished) Write(Won ? "战斗胜利" : "战斗失败");
            Visual(VisualNode.ActionEnd,actor.id,actor.id,null,"行动结束");
        }
        void Unfreeze(Combatant u) { u.states.RemoveAll(s => s.kind == EffectKind.Freeze); u.freezeProtection = true; Write(u.title + " 解除冻结，保留进度并获得下一次正常行动前免控保护"); }
        void ApplyPhase(Combatant u, bool log)
        {
            var p = Data.monsters.Find(x => x.id == u.monsterId).phases[u.phase]; u.panel.defense = p.defense; u.panel.specialDefense = p.specialDefense;
            if (log) Write(u.title + $" 切换相位={p.title} 双防={p.defense}/{p.specialDefense} 下次意图={p.skill}");
        }
        public string Intent(Combatant u)
        {
            if (u.player) return ActiveId == u.id ? "排谱中（全场暂停）" : "等待自然跑条";
            var m = Data.monsters.Find(x => x.id == u.monsterId); var phase = m.phases[u.phase]; return phase.title + " / 下次：" + phase.skill;
        }
        public string Preview(IReadOnlyList<string> cards, string target)
        {
            if (cards.Count == 0) return "先点击一张卡作为主技能，继续添加至五张。每次行动九张实体卡重新可选。";
            var plan = Plan(Player, cards); string validation = Validate(Player, cards, target);
            var clone = new BattleEngine(Data, Rng.Seed, loadout) { PreviewMode = true, ActiveId = ActiveId, ActionSerial = ActionSerial, Units = Units.Select(x => x.Copy()).ToList() };
            var before = clone.Units.ToDictionary(x => x.id, x => x.hp);
            if (validation == null) clone.Execute(clone.Player, clone.Plan(clone.Player, cards), target);
            var lines = new List<string> { $"主技能：{plan.skill.title}  总连锁：{plan.chain} / 5", "示例口径：不暴击、不闪避、不格挡；计入护盾。随机弹射/死亡转火仅示例，不保证伤害。预览不消耗正式随机数。", plan.skill.description, $"攻击属性[{BattleMath.AttributeName(plan.skill.attribute)}]={Player.Stat(plan.skill.attribute):F3}\n原生计划：{plan.nativeSegments}段/每目标；单目标总系数{plan.nativeTotal:P3}；属性加值整次{plan.bonus:F3}\n追击合计{plan.pursuits.Sum(g => g.count)}段", $"增伤+{plan.damageUp:P2} 物穿{plan.physicalPen:P2} 特穿{plan.specialPen:P2} 暴击率{plan.crit:P2} / 暴击倍率{plan.critDamage:F3}", string.Join("\n", plan.traitHints) };
            double ordinaryPen=plan.skill.damageType==DamageType.Special?plan.specialPen:plan.physicalPen;
            foreach (int basis in plan.skill.effects.Where(e => e.HasDuration).Select(e => e.BaseDuration(plan.chain)).Concat(plan.skill.HasDuration ? new[]{plan.skill.BaseDuration(plan.chain)} : Array.Empty<int>()).Distinct())
                lines.Add(plan.DurationDescription(basis));
            if (plan.skill.landingCoefficient > 0) lines.Add($"落地追加段（不含在上方原生计划系数中）：{(Data.rules.enableLandingTest ? "开启" : "关闭")}，系数{plan.skill.landingCoefficient:P2}；首段命中且目标存活时独立结算一次，不提高首段系数补偿；碰撞另一个敌人的交互仍待定。");
            lines.Add($"专注等修正后的最终暴击率{plan.crit:P2}，暴击倍率{plan.critDamage:F3}；裂隙仅暴击段额外对应穿透{plan.criticalPen:P2}，普通段穿透{ordinaryPen:P2} → 暴击段{BattleMath.Clamp(ordinaryPen+plan.criticalPen,0,Data.rules.penetrationCap):P2}；贯通分流{plan.bypass:P2}。以下扣血例保持不暴击，因此不触发裂隙；正式各段独立判定。");
            foreach (var g in plan.pursuits) lines.Add("追击：" + g.Description());
            if (plan.pursuits.Count > 0) lines.Add(PursuitInheritance(plan));
            if(plan.mirrors.Count>0) lines.Add("镜像计划："+string.Join("；",plan.mirrors.Select(m=>$"{m.slot}号位/当前连锁{m.currentChain} 强度{m.strength:P2}，只复制基础效果")));
            if (validation != null) lines.Add(validation);
            else
            {
                lines.Add("按目标汇总（不暴击命中示例）：\n" + clone.DamageSummary());
                foreach (var u in clone.Units)
                {
                    if (before[u.id] > u.hp) lines.Add(u.title + $"：示例扣血{before[u.id] - u.hp}，剩余{u.hp}，盾{u.shield}");
                    else if (before[u.id] < u.hp) lines.Add(u.title + $"：示例恢复{u.hp - before[u.id]}，生命{u.hp}，盾{u.shield}");
                }
                lines.AddRange(clone.Log.Where(s => s.Contains("获得护盾") || s.Contains("恢复") || s.Contains("速度百分比增益") || s.Contains("获得") && !s.Contains("免控")).Take(8));
                lines.Add("逐段示例：\n" + string.Join("\n", clone.Damage.Select(d => $"{Units.Find(u => u.id == d.target)?.title} {d.label} 属性{d.attackAttribute:F2} 防御{d.defense:F2} 穿透{d.penetration:P1} 有效防御{d.effectiveDefense:F2} 系数{d.coefficient:P3} 加值{d.bonus:F3} 实际增伤{d.damageUp:P2} 贯通{d.bypassRatio:P2}/直达{d.bypass} 计算{d.calculated} 吸盾{d.absorbed} 扣血{d.hpLost} 过量{d.overkill}")));
                lines.AddRange(clone.Log.Where(s => s.Contains("转火") || s.Contains("分摊") || s.Contains("条件词条") || s.Contains("贯通：") || s.Contains("镜像")));
            }
            lines.Add("预览共用正式计算器；按不暴击、不闪避、不格挡示例，计入护盾与当前防御/减伤。随机弹射/死亡转火仅取首个合法目标示例，反击按该示例模拟；实际命中、暴击与转火独立判定，不保证上述扣血。预览不消耗战斗随机数。");
            if (!string.IsNullOrEmpty(plan.skill.note)) lines.Add(plan.skill.note);
            return string.Join("\n\n", lines);
        }
        public string DamageSummary() => TargetTotals.Count == 0 ? "尚无伤害记录。" : string.Join("\n", TargetTotals.Select(s => $"{Units.Find(u => u.id == s.target)?.title} [{s.target}]：计算伤害{s.calculated} / 护盾吸收{s.absorbed} / 实际扣除生命{s.hpLost} / 过量伤害{s.overkill}"));
    }
}
