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
        public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0,0,1,1), projectileCurve = AnimationCurve.Linear(0,0,1,1);
        public float lungeDistance = .22f, hitDistance = .08f, hitScale = .96f, projectileArc = .3f;
        public Vector3 projectileScale = Vector3.one, effectScale = Vector3.one, projectileRotation, deathScale;
        public Color placeholderProjectile = new Color(1,.75f,.3f), hitTint = new Color(1,.4f,.35f);
        public GameObject castEffect, projectile, impactEffect, endEffect;
        public AudioClip castSound, launchSound, impactSound, endSound;
        public float volume = .6f, shakeAmplitude = .025f, shakeDuration = .08f;
        public AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0,1,1,0);
    }
}
