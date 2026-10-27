using UnityEngine;
namespace Qiling
{
    [CreateAssetMenu(menuName="器灵战斗/技能表现")]
    public sealed class SkillPresentation : ScriptableObject
    {
        [Header("Animator state names (optional; never gate combat)")]
        public string attackState = "Attack", hitState = "Hit", deathState = "Death", idleState = "Idle";
        public AnimationClip attackClip, hitClip, deathClip;
        [Header("Real seconds; bounded by driver, no animation event required")]
        [Min(0)] public float windup = .12f, travel = .1f, hitDuration = .07f, recovery = .12f, deathDuration = .25f, effectLifetime = 1;
        [Header("Per-segment rhythm; one windup per cast")]
        [Min(0)] public float segmentInterval=.08f, projectileSpeed=36, minimumTravel=.06f, maximumTravel=.6f;
        [Header("Grouped segment playback (cosmetic only)")]
        public bool simultaneousSegments = true;
        [Min(1)] public int segmentsPerBatch = 8;
        [Min(0)] public float batchInterval = .08f;
        [Range(.05f, 1)] public float sequentialTimingScale = .4f;
        [Min(0)] public float batchProjectileSpacing = .12f;
        public bool meleeProjectile;
        public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0,0,1,1), projectileCurve = AnimationCurve.Linear(0,0,1,1);
        public float segmentMotionDistance=.06f, segmentSwing=5, attackSwing=9, lungeDistance = .22f, hitDistance = .08f, hitScale = .96f, projectileArc = .3f;
        public Vector3 projectileScale = Vector3.one, effectScale = Vector3.one, projectileRotation, deathScale;
        public float hitShakeFrequency=28;
        public AnimationCurve hitCurve=AnimationCurve.EaseInOut(0,1,1,0);
        public Color healTint=new Color(.3f,1,.5f), shieldTint=new Color(.3f,.75f,1), dodgeTint=new Color(.75f,.85f,1), blockTint=new Color(.6f,.8f,1);
        public GameObject healEffect, shieldEffect, dodgeEffect, blockEffect;
        public AudioClip healSound, shieldSound, dodgeSound, blockSound;
        public Color placeholderProjectile = new Color(1,.75f,.3f), hitTint = new Color(1,.4f,.35f);
        public GameObject castEffect, projectile, impactEffect, endEffect;
        public AudioClip castSound, launchSound, impactSound, endSound;
        public float volume = .6f, shakeAmplitude = .025f, shakeDuration = .08f;
        public AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0,1,1,0);
    }
}
