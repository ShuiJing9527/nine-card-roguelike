using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling
{
    public partial class BattleDemo
    {
        int presentationCursor;
        Coroutine presentationRoutine;
        AudioSource presentationAudio;
        Camera shakingCamera; Vector3 shakeHome; bool shaking;
        readonly List<GameObject> transientViews = new List<GameObject>();
        readonly List<VisualPlaybackTrace> visualTrace = new List<VisualPlaybackTrace>();
        public IReadOnlyList<VisualPlaybackTrace> PresentationTrace => visualTrace;
        public bool PresentationBusy => presentationRoutine != null;
        [System.Serializable] public class VisualPlaybackTrace { public VisualNode node; public string actor,target,skill,segment; }
        void InitializePresentation() { presentationAudio = gameObject.GetOrAdd<AudioSource>(); presentationAudio.playOnAwake=false; }
        void ResetPresentation()
        {
            if(presentationRoutine != null) StopCoroutine(presentationRoutine);
            if(shaking && shakingCamera!=null) shakingCamera.transform.localPosition=shakeHome; shaking=false;
            presentationRoutine=null; presentationCursor=0; visualTrace.Clear();
            foreach(var obj in transientViews) if(obj != null) DisposeView(obj); transientViews.Clear();
            if(stageObjects.Count > 0) foreach(var obj in stageObjects) if(obj != null) obj.GetComponent<UnitPresentation>()?.ResetMotion();
        }
        void TickPresentation()
        {
            if(!Application.isPlaying || battle == null || PresentationBusy || presentationCursor >= battle.VisualEvents.Count || showAllLogs || showTraitEditor || showSettings || showPreview || battlePaused) return;
            presentationRoutine=StartCoroutine(PlayVisualQueue());
        }
        UnitPresentation Rig(string id)
        {
            int index = battle.Units.FindIndex(u=>u.id==id);
            return index>=0 && index<stageObjects.Count && stageObjects[index]!=null ? stageObjects[index].GetComponent<UnitPresentation>() : null;
        }
        SkillPresentation Motion(BattleVisualEvent e)
        {
            var skill=battle.Data.skills.Find(s=>s.id==e.skill);
            return skill?.presentation != null ? skill.presentation : Rig(e.actor)?.motion != null ? Rig(e.actor).motion : viewConfig?.defaultSkill;
        }
        static float Duration(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value,0,5);
        IEnumerator Delay(float seconds) { float time=0; while(time<Duration(seconds)) { time+=Time.unscaledDeltaTime; yield return null; } }
        void Sound(AudioClip clip, float volume) { if(clip != null && presentationAudio != null) presentationAudio.PlayOneShot(clip,Mathf.Clamp01(volume)); }
        GameObject Effect(GameObject prefab, Vector3 point, SkillPresentation motion)
        {
            if(prefab==null) return null;
            var obj=Instantiate(prefab,point,Quaternion.identity,stage.transform); obj.transform.localScale=motion.effectScale; transientViews.Add(obj);
            Destroy(obj,Mathf.Max(.05f,Duration(motion.effectLifetime))); return obj;
        }
        IEnumerator PlayVisualQueue()
        {
            // Always yield once so the coroutine handle is assigned even with no configured resources.
            yield return null;
            try { while(battle != null && presentationCursor < battle.VisualEvents.Count)
            {
                var e=battle.VisualEvents[presentationCursor++];
                visualTrace.Add(new VisualPlaybackTrace { node=e.node,actor=e.actor,target=e.target,skill=e.skill,segment=e.segment });
                var actor=Rig(e.actor); var target=Rig(e.target); var motion=Motion(e);
                if(motion==null) { yield return null; continue; }
                Vector3 from=actor!=null ? actor.Anchor(actor.launchAnchor).position : target!=null ? target.Anchor(target.hitAnchor).position : Vector3.zero;
                Vector3 to=target!=null ? target.Anchor(target.hitAnchor).position : from;
                switch(e.node)
                {
                    case VisualNode.CastStart:
                        actor?.Play(motion.attackState); actor?.PlayClip(motion.attackClip,motion.windup+motion.recovery+motion.travel);
                        Effect(motion.castEffect,from,motion); Sound(motion.castSound,motion.volume);
                        if(actor!=null) yield return actor.OffsetToward(to,motion.lungeDistance,Duration(motion.windup),motion.moveCurve);
                        else yield return Delay(motion.windup);
                        break;
                    case VisualNode.Launch:
                        Sound(motion.launchSound,motion.volume);
                        if(motion.projectile!=null)
                        {
                            var projectile=Instantiate(motion.projectile,from,Quaternion.Euler(motion.projectileRotation),stage.transform); transientViews.Add(projectile); projectile.transform.localScale=motion.projectileScale;
                            float time=0, duration=Duration(motion.travel);
                            while(time<duration && projectile!=null)
                            {
                                float t=Mathf.Clamp01(time/Mathf.Max(.001f,duration)); float k=(motion.projectileCurve!=null?motion.projectileCurve.Evaluate(t):t);
                                projectile.transform.position=Vector3.LerpUnclamped(from,to,k)+Vector3.up*(4*t*(1-t)*motion.projectileArc);
                                time+=Time.unscaledDeltaTime; yield return null;
                            }
                            if(projectile!=null) { transientViews.Remove(projectile); Destroy(projectile); }
                        }
                        else yield return Delay(motion.travel);
                        break;
                    case VisualNode.Impact:
                        Floating(e,to);
                        if(!e.dodged && !e.blocked)
                        {
                            Effect(motion.impactEffect,to,motion); Sound(motion.impactSound,motion.volume);
                            var reaction=target!=null && target.motion!=null ? target.motion : motion;
                            target?.Play(reaction.hitState); target?.PlayClip(reaction.hitClip,reaction.hitDuration);
                            if(target!=null) yield return target.Hit(reaction,Duration(reaction.hitDuration)); else yield return Delay(motion.hitDuration);
                            yield return Shake(motion);
                        }
                        else yield return Delay(motion.hitDuration);
                        break;
                    case VisualNode.Death:
                        var death=target!=null && target.motion!=null ? target.motion : motion;
                        target?.Play(death.deathState); target?.PlayClip(death.deathClip,death.deathDuration);
                        if(target!=null) yield return target.Die(Duration(death.deathDuration),death.moveCurve,death.deathScale);
                        break;
                    case VisualNode.CastEnd:
                        Effect(motion.endEffect,actor!=null?actor.Anchor(actor.groundAnchor).position:from,motion); Sound(motion.endSound,motion.volume);
                        if(actor!=null) yield return actor.Return( Duration(motion.recovery),motion.moveCurve); else yield return Delay(motion.recovery);
                        actor?.Play(motion.idleState); break;
                }
            }
            } finally { presentationRoutine=null; dirty=true; }
        }
        IEnumerator Shake(SkillPresentation motion)
        {
            var camera=Camera.main; if(camera==null || motion.shakeAmplitude<=0) yield break;
            Vector3 original=camera.transform.localPosition; shakingCamera=camera; shakeHome=original; shaking=true; float t=0, duration=Duration(motion.shakeDuration);
            while(t<duration)
            {
                float phase=t/Mathf.Max(.001f,duration); float strength=motion.shakeAmplitude*(motion.shakeCurve!=null?motion.shakeCurve.Evaluate(phase):1-phase);
                camera.transform.localPosition=original+new Vector3(Mathf.Sin(phase*63),Mathf.Sin(phase*91),0)*strength;
                t+=Time.unscaledDeltaTime; yield return null;
            }
            camera.transform.localPosition=original; shaking=false;
        }
        void Floating(BattleVisualEvent e, Vector3 world)
        {
            if(viewConfig?.floatingTextPrefab==null || uiPage==null) return;
            var obj=Instantiate(viewConfig.floatingTextPrefab,uiPage.transform); obj.SetActive(true); transientViews.Add(obj);
            var label=obj.GetComponentInChildren<Text>(); if(label==null) { Destroy(obj); return; }
            label.text=e.dodged ? "闪避" : e.blocked ? "格挡" : (e.critical?"暴击 ":"")+e.damage+(e.shield>0?" 盾−"+e.shield:"");
            var rt=obj.GetComponent<RectTransform>(); var point=Project(world,uiPage.GetComponent<RectTransform>(),Camera.main); rt.anchoredPosition=new Vector2(point.x,-point.y);
            StartCoroutine(FloatAway(obj,rt,viewConfig.floatingTextLifetime,viewConfig.floatingTextRise));
        }
        IEnumerator FloatAway(GameObject obj, RectTransform rt,float duration,float rise)
        {
            Vector2 start=rt.anchoredPosition; float t=0;
            while(obj!=null && t<Duration(duration)) { rt.anchoredPosition=start+Vector2.up*(rise*t/Mathf.Max(.001f,duration)); t+=Time.unscaledDeltaTime; yield return null; }
            if(obj!=null) { transientViews.Remove(obj); Destroy(obj); }
        }
    }
}
