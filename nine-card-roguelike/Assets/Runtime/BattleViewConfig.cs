using System;
using System.Collections.Generic;
using UnityEngine;
namespace Qiling
{
    [Serializable] public class ViewTemplate { public string id; public GameObject prefab; }
    [Serializable] public class NamedVisual { public string id; public Sprite icon; public Color color = Color.white; }
    [CreateAssetMenu(menuName="器灵战斗/界面与表现配置")]
    public sealed class BattleViewConfig : ScriptableObject
    {
        [Header("UI templates (edit in Prefab Mode)")]
        public GameObject selectionPage, battlePage, elementPrefab;
        public List<ViewTemplate> panels = new List<ViewTemplate>();
        public Font font;
        public Color text = new Color(.89f,.93f,.98f), muted = new Color(.62f,.7f,.8f), gold = new Color(.98f,.75f,.36f), player = new Color(.26f,.72f,.88f);
        public Color cardIdle = new Color(.065f,.12f,.18f), cardSelected = new Color(.16f,.25f,.3f);
        public Sprite cardFrame, buttonSprite, panelSprite, shieldIcon;
        public float choiceBorderWidth = 2;
        public List<NamedVisual> units = new List<NamedVisual>();
        public List<NamedVisual> types = new List<NamedVisual>(), traits = new List<NamedVisual>(), statuses = new List<NamedVisual>();
        [Header("Card motion / hit band follows actual face size")]
        public float hoverLift = 65, hoverScale = 1.12f, hoverDuration = .1f, selectPulse = .98f, cardArrangeDuration = .16f;
        public AnimationCurve cardCurve = AnimationCurve.EaseInOut(0,0,1,1);
        public AudioClip cardHoverSound, cardAddSound, cardRemoveSound;
        [Header("Stage / presentation")]
        public GameObject playerPlaceholder, enemyPlaceholder, backgroundPrefab;
        public SkillPresentation defaultSkill;
        public float avatarSeparation = 7, avatarLaneHeight = 44, unitInfoOffset = 12, intentOffset = 82;
        public Vector2 infoVerticalRange = new Vector2(440,480), intentVerticalRange = new Vector2(160,330);
        [Header("Per-hit floating text")]
        public Color damageColor=new Color(1,.92f,.8f), criticalColor=new Color(1,.65f,.2f), shieldColor=new Color(.3f,.78f,1), healColor=new Color(.3f,1,.5f), dodgeColor=new Color(.75f,.85f,1), blockColor=new Color(.5f,.75f,1);
        public float criticalTextScale=1.2f;
        [Tooltip("默认仅显示扣血数字；开启后恢复生命/暴击文字。数值、颜色和真实记录不变。 ")]
        public bool floatingTextVerbose;
        [Tooltip("目标身体内的生成高度：0为底部，1为顶部。")]
        [Range(0,1)] public float floatingTextBodyHeight=.68f;
        public Vector2 floatingTextOffsetRange=new Vector2(54,28);
        [Min(0)] public float floatingTextAvoidanceSpacing=28;
        [Range(1,64)] public int floatingTextPlacementAttempts=24;
        [Min(0)] public float floatingTextHorizontalDrift=8;
        [Range(0,.95f)] public float floatingTextFadeStart=.5f;
        [Tooltip("顶部禁入区，设计像素；包含公共行动轴。会额外预留上浮和文字高度。")]
        [Min(0)] public float floatingTextTopBoundary=170;
        public AnimationCurve floatingTextCurve=AnimationCurve.EaseInOut(0,0,1,1);
        public float floatingTextLifetime = .7f, floatingTextRise = 55;
        public GameObject floatingTextPrefab, statusIconPrefab;
        public GameObject Panel(string id) => panels.Find(p=>p.id==id)?.prefab;
    }
}
