using System.Collections;
using System.Linq;
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
        Vector3 home, homeScale; Quaternion homeRotation;
        Renderer[] flashing; MaterialPropertyBlock[] priorBlocks; SpriteRenderer[] sprites; Color[] priorColors;
        bool moving;
        PlayableGraph graph;
        void Update() { if(!moving && visualRoot!=null && idlePeriod>0) visualRoot.localPosition=home+Vector3.up*(Mathf.Sin(Time.unscaledTime*Mathf.PI*2/idlePeriod)*idleAmplitude); }
        public void PlayClip(AnimationClip clip,float duration)
        {
            if(clip==null || !gameObject.activeInHierarchy) return;
            if(graph.IsValid()) graph.Destroy();
            if(animator==null) animator=visualRoot.gameObject.GetOrAdd<Animator>();
            graph=PlayableGraph.Create("Qiling Cosmetic Clip"); graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            var playable=AnimationClipPlayable.Create(graph,clip); playable.SetSpeed(clipSpeed); var output=AnimationPlayableOutput.Create(graph,"Visual",animator); output.SetSourcePlayable(playable); graph.Play();
            StartCoroutine(StopClip(graph,Mathf.Clamp(duration,.01f,5)));
        }
        IEnumerator StopClip(PlayableGraph active,float duration) { yield return new WaitForSecondsRealtime(duration); if(active.IsValid()) active.Destroy(); }
        void RestoreTint()
        {
            if(flashing!=null) for(int i=0;i<flashing.Length;i++) if(flashing[i]!=null) flashing[i].SetPropertyBlock(priorBlocks[i]);
            if(sprites!=null) for(int i=0;i<sprites.Length;i++) if(sprites[i]!=null) sprites[i].color=priorColors[i];
            flashing=null; sprites=null;
        }
        public void CancelMotion() { StopAllCoroutines(); if(graph.IsValid()) graph.Destroy(); ResetMotion(); }
        public void ResetMotion() { RestoreTint(); if(visualRoot==null) return; moving=false; visualRoot.localPosition=home; visualRoot.localScale=homeScale; visualRoot.localRotation=homeRotation; }
        public IEnumerator Windup(Vector3 target,SkillPresentation motion,float duration)
        {
            moving=true; Vector3 start=visualRoot.localPosition; Vector3 delta=transform.InverseTransformVector((target-visualRoot.position).normalized*motion.lungeDistance);
            Quaternion from=visualRoot.localRotation,to=homeRotation*Quaternion.Euler(0,0,motion.attackSwing); float time=0;
            while(time<duration) { float t=time/Mathf.Max(.001f,duration); float k=motion.moveCurve!=null?motion.moveCurve.Evaluate(t):t; visualRoot.localPosition=Vector3.LerpUnclamped(start,home+delta,k); visualRoot.localRotation=Quaternion.SlerpUnclamped(from,to,k); time+=Time.unscaledDeltaTime; yield return null; }
            visualRoot.localPosition=home+delta; visualRoot.localRotation=to;
        }
        public IEnumerator Strike(Vector3 target,SkillPresentation motion,float duration)
        {
            Vector3 start=visualRoot.localPosition; Quaternion rotation=visualRoot.localRotation;
            Vector3 delta=transform.InverseTransformVector((target-visualRoot.position).normalized*motion.segmentMotionDistance); float time=0;
            while(time<duration) { float pulse=Mathf.Sin(Mathf.PI*time/Mathf.Max(.001f,duration)); visualRoot.localPosition=start+delta*pulse; visualRoot.localRotation=rotation*Quaternion.Euler(0,0,motion.segmentSwing*pulse); time+=Time.unscaledDeltaTime; yield return null; }
            visualRoot.localPosition=start; visualRoot.localRotation=rotation;
        }
        public IEnumerator OffsetToward(Vector3 target,float distance,float duration,AnimationCurve curve)
        {
            moving=true; Vector3 start=visualRoot.localPosition;
            Vector3 direction=(target-visualRoot.position).normalized; Vector3 delta=transform.InverseTransformVector(direction*distance);
            float t=0; while(t<duration) { visualRoot.localPosition=Vector3.LerpUnclamped(start,home+delta,(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            visualRoot.localPosition=home+delta;
        }
        public IEnumerator Return(float duration,AnimationCurve curve)
        {
            if(visualRoot==null) yield break; Vector3 start=visualRoot.localPosition; Quaternion rotation=visualRoot.localRotation; float t=0;
            while(t<duration) { visualRoot.localRotation=Quaternion.Slerp(rotation,homeRotation,Mathf.Clamp01(t/Mathf.Max(.001f,duration))); visualRoot.localPosition=Vector3.LerpUnclamped(start,home,(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            ResetMotion();
        }
        public IEnumerator Hit(SkillPresentation motion,float duration)
        {
            Vector3 scale=visualRoot.localScale; visualRoot.localScale=scale*motion.hitScale;
            yield return Feedback(motion.hitTint,duration,motion.hitDistance,motion.hitShakeFrequency,motion.hitCurve);
            visualRoot.localScale=scale;
        }
        public IEnumerator Feedback(Color tint,float duration,float distance,float frequency,AnimationCurve curve)
        {
            moving=true; Vector3 start=visualRoot.localPosition; RestoreTint();
            flashing=GetComponentsInChildren<Renderer>(); priorBlocks=new MaterialPropertyBlock[flashing.Length];
            for(int i=0;i<flashing.Length;i++)
            {
                priorBlocks[i]=new MaterialPropertyBlock(); flashing[i].GetPropertyBlock(priorBlocks[i]);
                var block=new MaterialPropertyBlock(); flashing[i].GetPropertyBlock(block); block.SetColor("_BaseColor",tint); block.SetColor("_Color",tint); flashing[i].SetPropertyBlock(block);
            }
            sprites=GetComponentsInChildren<SpriteRenderer>(); priorColors=sprites.Select(s=>s.color).ToArray(); foreach(var sprite in sprites) sprite.color=tint;
            float time=0;
            while(time<duration) { float t=time/Mathf.Max(.001f,duration); float envelope=curve!=null?curve.Evaluate(t):1-t; visualRoot.localPosition=start+Vector3.right*(Mathf.Sin(time*frequency*Mathf.PI*2)*distance*envelope); time+=Time.unscaledDeltaTime; yield return null; }
            RestoreTint(); visualRoot.localPosition=start; moving=false;
        }
        public IEnumerator Die(float duration,AnimationCurve curve,Vector3 scale)
        {
            moving=true; float t=0; Vector3 start=visualRoot.localScale;
            while(t<duration) { visualRoot.localScale=Vector3.LerpUnclamped(start,Vector3.Scale(homeScale,scale),(curve!=null?curve.Evaluate(t/Mathf.Max(.001f,duration)):Mathf.Clamp01(t/Mathf.Max(.001f,duration)))); t+=Time.unscaledDeltaTime; yield return null; }
            gameObject.SetActive(false);
        }
        void OnDisable() { if(graph.IsValid()) graph.Destroy(); RestoreTint(); }
        void OnDestroy() { if(graph.IsValid()) graph.Destroy(); }
        public void Initialize()
        {
            if (visualRoot == null) visualRoot = transform;
            if (spriteRenderer != null && sprite != null) spriteRenderer.sprite = sprite;
            if (materialOverride != null) foreach(var r in GetComponentsInChildren<Renderer>()) r.sharedMaterial = materialOverride;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animatorController != null) animator.runtimeAnimatorController = animatorController;
            visualRoot.localScale = Vector3.Scale(visualRoot.localScale,visualScale);
            home=visualRoot.localPosition; homeScale=visualRoot.localScale; homeRotation=visualRoot.localRotation; Play(motion != null ? motion.idleState : "Idle");
        }
        public Transform Anchor(Transform anchor) => anchor != null ? anchor : visualRoot != null ? visualRoot : transform;
        public void Play(string state)
        {
            if (!gameObject.activeInHierarchy || animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state)) return;
            int hash = Animator.StringToHash(state); if(animator.HasState(0, hash)) animator.CrossFade(hash,Mathf.Max(0,animatorTransition));
        }
    }
}
