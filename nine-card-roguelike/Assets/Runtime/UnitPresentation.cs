using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
namespace Qiling
{
    /// <summary>Place anchors in Prefab Mode. Motion belongs to visualRoot, anchors follow it.</summary>
    public sealed class UnitPresentation : MonoBehaviour
    {
        public Transform visualRoot, healthAnchor, statusAnchor, launchAnchor, hitAnchor, groundAnchor;
        public Animator animator;
        public SkillPresentation motion;
        public SpriteRenderer spriteRenderer;
        public Sprite sprite;
        public Material materialOverride;
        public RuntimeAnimatorController animatorController;
        [Tooltip("Only applies to visualRoot, never battle stats.")]
        public Vector3 visualScale = Vector3.one;
        public float idleAmplitude=.025f, idlePeriod=2.5f, animatorTransition=.05f, clipSpeed=1;
        Vector3 home, homeScale;
        bool moving;
        PlayableGraph graph;
        void Update() { if(!moving && visualRoot!=null && idlePeriod>0) visualRoot.localPosition=home+Vector3.up*(Mathf.Sin(Time.unscaledTime*Mathf.PI*2/idlePeriod)*idleAmplitude); }
        public void PlayClip(AnimationClip clip,float duration)
        {
            if(clip==null) return;
            if(graph.IsValid()) graph.Destroy();
            if(animator==null) animator=visualRoot.gameObject.GetOrAdd<Animator>();
            graph=PlayableGraph.Create("Qiling Cosmetic Clip"); graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            var playable=AnimationClipPlayable.Create(graph,clip); playable.SetSpeed(clipSpeed); var output=AnimationPlayableOutput.Create(graph,"Visual",animator); output.SetSourcePlayable(playable); graph.Play();
            StartCoroutine(StopClip(graph,Mathf.Clamp(duration,.01f,5)));
        }
        IEnumerator StopClip(PlayableGraph active,float duration) { yield return new WaitForSecondsRealtime(duration); if(active.IsValid()) active.Destroy(); }
        public void ResetMotion() { if(visualRoot==null) return; moving=false; visualRoot.localPosition=home; visualRoot.localScale=homeScale; }
        public IEnumerator OffsetToward(Vector3 target,float distance,float duration,AnimationCurve curve)
        {
            moving=true; Vector3 start=visualRoot.localPosition;
            Vector3 direction=(target-visualRoot.position).normalized; Vector3 delta=transform.InverseTransformVector(direction*distance);
            float t=0; while(t<duration) { visualRoot.localPosition=Vector3.LerpUnclamped(start,home+delta,(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            visualRoot.localPosition=home+delta;
        }
        public IEnumerator Return(float duration,AnimationCurve curve)
        {
            if(visualRoot==null) yield break; Vector3 start=visualRoot.localPosition; float t=0;
            while(t<duration) { visualRoot.localPosition=Vector3.LerpUnclamped(start,home,(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            ResetMotion();
        }
        public IEnumerator Hit(SkillPresentation motion,float duration)
        {
            moving=true; Vector3 start=visualRoot.localPosition;
            var renderers=GetComponentsInChildren<Renderer>(); var block=new MaterialPropertyBlock();
            foreach(var r in renderers) { r.GetPropertyBlock(block); block.SetColor("_BaseColor",motion.hitTint); block.SetColor("_Color",motion.hitTint); r.SetPropertyBlock(block); }
            visualRoot.localScale=homeScale*motion.hitScale; visualRoot.localPosition=start+Vector3.right*motion.hitDistance;
            yield return new WaitForSecondsRealtime(Mathf.Clamp(duration,0,5));
            foreach(var r in renderers) r.SetPropertyBlock(null);
            visualRoot.localScale=homeScale; visualRoot.localPosition=start; moving=false;
        }
        public IEnumerator Die(float duration,AnimationCurve curve,Vector3 scale)
        {
            moving=true; float t=0; Vector3 start=visualRoot.localScale;
            while(t<duration) { visualRoot.localScale=Vector3.LerpUnclamped(start,Vector3.Scale(homeScale,scale),(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            gameObject.SetActive(false);
        }
        void OnDestroy() { if(graph.IsValid()) graph.Destroy(); }
        public void Initialize()
        {
            if (visualRoot == null) visualRoot = transform;
            if (spriteRenderer != null && sprite != null) spriteRenderer.sprite = sprite;
            if (materialOverride != null) foreach(var r in GetComponentsInChildren<Renderer>()) r.sharedMaterial = materialOverride;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animatorController != null) animator.runtimeAnimatorController = animatorController;
            visualRoot.localScale = Vector3.Scale(visualRoot.localScale,visualScale);
            home=visualRoot.localPosition; homeScale=visualRoot.localScale; Play(motion != null ? motion.idleState : "Idle");
        }
        public Transform Anchor(Transform anchor) => anchor != null ? anchor : visualRoot != null ? visualRoot : transform;
        public void Play(string state)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state)) return;
            int hash = Animator.StringToHash(state); if(animator.HasState(0, hash)) animator.CrossFade(hash,Mathf.Max(0,animatorTransition));
        }
    }
}
