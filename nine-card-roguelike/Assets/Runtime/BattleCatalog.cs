using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qiling
{
    public enum MatchType { Offense, Survival, Status, Universal, OffenseSurvival, Pending }
    public enum DamageType { Physical, Special, Change }
    public enum Reach { None, Melee, Ranged }
    public enum TargetMode { Enemy, FrontRow, AllEnemies, Self, Ally, OtherAlly }
    public enum Attribute { Attack, SpecialAttack, Defense, SpecialDefense, Speed, MaxHp, LostHp, Shield, BothAttack }
    public enum Pattern { Single, Multi, Sweep, Bounce, Counter }
    public enum EffectKind { Shield, Heal, Cleanse, Poison, Burn, Bleed, Weakness, AttackUp, SpecialAttackUp, DefenseUp, SpecialDefenseUp, SpeedUp, DefensePercent, SpecialDefensePercent, SpeedPercent, Evade, CritUp, DebuffImmune, Block, MeleeReduction, Reflect, ShieldBoost, Push, Guard, Freeze }
    [Serializable] public class Linear
    {
        public double basis, perChain;
        public double At(int chain) => basis + perChain * chain;
    }
    [Serializable] public class Panel
    {
        public int hp;
        public double attack, specialAttack, defense, specialDefense, speed, crit = .05, critDamage = 1.5;
        public Panel Copy() => (Panel)MemberwiseClone();
    }
    [Serializable] public class EffectDef
    {
        public EffectKind kind;
        public Attribute attribute;
        public Linear value = new Linear();
        public int duration;
        public bool onHit, stacking, percent, rangedOnly, negative, pending;
        public string note;
    }
    [Serializable] public class SkillDef
    {
        public Sprite icon;
        public SkillPresentation presentation;
        public string id, title, description, note;
        public DamageType damageType;
        public Reach reach;
        public TargetMode target;
        public Attribute attribute;
        public Pattern pattern;
        public Linear coefficient = new Linear(), segments = new Linear { basis = 1 }, penetration = new Linear(), critBonus = new Linear();
        public double bounceCoefficient, landingCoefficient, shieldCost, shieldResource;
        public int duration;
        public List<EffectDef> effects = new List<EffectDef>();
    }
    [Serializable] public class TraitDef
    {
        public Sprite icon;
        public string id, title, description, note;
        public MatchType match;
        public double rate;
        public bool pending;
    }
    [Serializable] public class CardDef
    {
        public Sprite frame, art, typeIcon;
        public string id, skill;
        public MatchType match;
        public List<string> traits = new List<string>();
    }
    [Serializable] public class WeaponDef
    {
        public Sprite portrait;
        public string id, title, note;
        public Panel panel = new Panel();
        public List<string> cards = new List<string>();
        public GameObject visualPrefab;
    }
    [Serializable] public class PhaseDef
    {
        public string title, skill;
        public double defense, specialDefense;
    }
    [Serializable] public class MonsterDef
    {
        public Sprite portrait;
        public string id, title;
        public Panel panel = new Panel();
        public List<PhaseDef> phases = new List<PhaseDef>();
        public GameObject visualPrefab;
    }
    [Serializable] public class SpawnDef { public string monster; public bool backRow; }
    [Serializable] public class EncounterDef { public string title; public List<SpawnDef> enemies = new List<SpawnDef>(); }
    [Serializable] public class RuleSettings
    {
        public double defenseConstant = 10, evadeCap = .75, penetrationCap = 1, critCap = 1, sustainedReductionCap = .75, pushCap = .8;
        [Tooltip("预留给扩展卡的额外攻击/重复概率上限。初始卡池没有概率续射或概率再动。")]
        public double repeatCap = .8;
        [Tooltip("预留给扩展卡的一次性与持续减伤组合。初始单人卡池只有拒敌架势的近战持续减伤。")]
        public double combinedReductionCap = .9;
        public double[] pursuitRetention = { .9, .8, .7, .6, .5 };
        public int poisonPerStack = 2, burnDamage = 5, freezeResilience = 3;
        [Tooltip("未定的致死反伤先后。关闭时不执行倒下后的反伤；UI始终标注测试策略。")]
        public bool reflectAfterDeath = true;
        [Tooltip("挑空刺的第二段存在165%与150%冲突：默认使用第13页卡面165%，作为测试值展示。")]
        public bool enableLandingTest = true;
    }
    [CreateAssetMenu(menuName = "器灵战斗/战斗数据库")]
    public class BattleCatalog : ScriptableObject
    {
        public string sourceVersion;
        public RuleSettings rules = new RuleSettings();
        public List<WeaponDef> weapons = new List<WeaponDef>();
        public List<CardDef> cards = new List<CardDef>();
        public List<SkillDef> skills = new List<SkillDef>();
        public List<TraitDef> traits = new List<TraitDef>();
        public List<MonsterDef> monsters = new List<MonsterDef>();
        public List<EncounterDef> encounters = new List<EncounterDef>();
        public List<string> pending = new List<string>();
        public SkillDef Skill(string id) => skills.Find(x => x.id == id) ?? throw new InvalidOperationException("Missing skill " + id);
        public CardDef Card(string id) => cards.Find(x => x.id == id) ?? throw new InvalidOperationException("Missing card " + id);
        public TraitDef Trait(string id) => traits.Find(x => x.id == id) ?? throw new InvalidOperationException("Missing trait " + id);
    }
}
