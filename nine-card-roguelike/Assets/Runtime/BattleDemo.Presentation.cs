using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace Qiling
{
    public partial class BattleDemo
    {
        int presentationCursor;
        float lastTemporaryExpiry;
        Coroutine presentationRoutine;
        AudioSource presentationAudio;
        Camera shakingCamera; Vector3 shakeHome; bool shaking;
        SkillPresentation fallbackMotion;
        string playbackTarget; BattleVisualEvent pendingFlight; Vector3 pendingArrival;
        readonly List<GameObject> transientViews = new List<GameObject>();
        readonly List<Coroutine> cosmeticRoutines = new List<Coroutine>();
        int playbackBatch;
        readonly List<Coroutine> floatingRoutines = new List<Coroutine>();
        readonly Dictionary<string,Combatant> displayedUnits = new Dictionary<string,Combatant>();
        readonly System.Random floatingRandom = new System.Random(); // Cosmetic-only stream; never calls battle.Rng or UnityEngine.Random.
        readonly List<FloatingPlacement> activeFloating = new List<FloatingPlacement>();
        readonly List<FloatingTextSpawn> floatingTrace = new List<FloatingTextSpawn>();
        public IReadOnlyList<FloatingTextSpawn> FloatingTextTrace => floatingTrace;
        sealed class FloatingPlacement { public string target; public RectTransform rect; }
        public sealed class FloatingTextSpawn { public int frame,eventIndex,damageIndex; public string target; public Vector2 anchor,start; public float drift; }
        readonly Dictionary<string,Vector3> impactPositions = new Dictionary<string,Vector3>();
        readonly List<VisualPlaybackTrace> visualTrace = new List<VisualPlaybackTrace>();
        public IReadOnlyList<VisualPlaybackTrace> PresentationTrace => visualTrace;
        public bool PresentationBusy => presentationRoutine != null || battle != null && presentationCursor < battle.VisualEvents.Count;
        public int TemporaryViewCount => transientViews.Count(v=>v!=null);
        [System.Serializable] public class VisualPlaybackTrace
        {
            public VisualNode node; public string actor,target,skill,segment; public int frame,batch,group; public int eventIndex,damageIndex,damage,shield,calculated,overkill,hpAfter,shieldAfter;
            public Vector3 from,to;
        }
        void InitializePresentation() { presentationAudio=gameObject.GetOrAdd<AudioSource>(); presentationAudio.playOnAwake=false; }
        void ClearTransientViews()
        {
            foreach(var routine in cosmeticRoutines) if(routine!=null) StopCoroutine(routine); cosmeticRoutines.Clear();
            foreach(var routine in floatingRoutines) if(routine!=null) StopCoroutine(routine); floatingRoutines.Clear();
            foreach(var obj in transientViews) if(obj!=null) { obj.SetActive(false); DisposeView(obj); } transientViews.Clear();
            lastTemporaryExpiry=0; activeFloating.Clear(); impactPositions.Clear(); pendingFlight=null; playbackTarget=null;
        }
        void ResetPresentation()
        {
            if(presentationRoutine!=null) StopCoroutine(presentationRoutine); presentationRoutine=null;
            if(shaking && shakingCamera!=null) shakingCamera.transform.localPosition=shakeHome; shaking=false;
            if(presentationAudio!=null) presentationAudio.Stop();
            ClearTransientViews(); floatingTrace.Clear(); playbackBatch=0; presentationCursor=0; visualTrace.Clear(); displayedUnits.Clear();
            foreach(var obj in stageObjects) if(obj!=null) obj.GetComponent<UnitPresentation>()?.CancelMotion();
        }
        void PreparePresentation()
        {
            if(battle==null || PresentationBusy) return;
            displayedUnits.Clear(); foreach(var unit in battle.Units) displayedUnits[unit.id]=unit.Copy();
        }
        Combatant DisplayUnit(Combatant unit)
        {
            if(!PresentationBusy) return unit;
            if(displayedUnits.TryGetValue(unit.id,out var shown)) return shown;
            var initial=battle.VisualEvents.Skip(presentationCursor).FirstOrDefault()?.units?.Find(u=>u.id==unit.id);
            shown=initial!=null?initial.Apply(unit):unit.Copy(); displayedUnits[unit.id]=shown; return shown;
        }
        public Combatant DisplayedUnit(string id) { var unit=battle?.Units.Find(u=>u.id==id); return unit==null?null:DisplayUnit(unit).Copy(); }
        void ApplyFrame(BattleVisualEvent e)
        {
            if(e.units==null) return;
            foreach(var frame in e.units) { var unit=battle.Units.Find(u=>u.id==frame.id); if(unit!=null) displayedUnits[unit.id]=frame.Apply(unit); }
        }
        void TickPresentation()
        {
            if(!Application.isPlaying || battle==null || presentationRoutine!=null || presentationCursor>=battle.VisualEvents.Count || showAllLogs || showTraitEditor || showSettings || showPreview || battlePaused) return;
            presentationRoutine=StartCoroutine(PlayVisualQueue());
        }
        UnitPresentation Rig(string id)
        {
            int index=battle.Units.FindIndex(u=>u.id==id);
            return index>=0 && index<stageObjects.Count && stageObjects[index]!=null?stageObjects[index].GetComponent<UnitPresentation>():null;
        }
        SkillPresentation Motion(BattleVisualEvent e)
        {
            var skill=battle.Data.skills.Find(s=>s.id==e.skill);
            var motion=skill?.presentation!=null?skill.presentation:Rig(e.actor)?.motion!=null?Rig(e.actor).motion:viewConfig?.defaultSkill;
            if(motion!=null) return motion;
            if(fallbackMotion==null) fallbackMotion=ScriptableObject.CreateInstance<SkillPresentation>(); return fallbackMotion;
        }
        static float Duration(float value) => float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Clamp(value,0,5);
        IEnumerator Delay(float seconds) { float time=0; while(time<Duration(seconds)) { time+=Time.unscaledDeltaTime; yield return null; } }
        void Sound(AudioClip clip,float volume) { if(clip!=null && presentationAudio!=null) presentationAudio.PlayOneShot(clip,Mathf.Clamp01(volume)); }
        GameObject Effect(GameObject prefab,Vector3 point,SkillPresentation motion)
        {
            if(prefab==null) return null;
            var obj=Instantiate(prefab,point,Quaternion.identity,stage.transform); obj.transform.localScale=motion.effectScale; transientViews.Add(obj);
            float life=Mathf.Max(.05f,Duration(motion.effectLifetime)); lastTemporaryExpiry=Mathf.Max(lastTemporaryExpiry,Time.unscaledTime+life); Destroy(obj,life); return obj;
        }
        IEnumerator PlayVisualQueue()
        {
            yield return null; // assign handle before any zero-duration node completes
            try
            {
                while(battle!=null && presentationCursor<battle.VisualEvents.Count)
                {
                    var e=battle.VisualEvents[presentationCursor];
                    if (CanBatch(e)) { yield return PlayBatch(); continue; }
                    var actor=Rig(e.actor); var target=Rig(e.target); var motion=Motion(e);
                    float rhythm = battle.Data.skills.Find(s=>s.id==e.skill)?.pattern==Pattern.Bounce ? Mathf.Clamp(motion.sequentialTimingScale,.05f,1) : 1;
                    var origin=Rig(e.flightFrom);
                    Vector3 from=actor!=null?actor.Anchor(actor.launchAnchor).position:target!=null?target.Anchor(target.hitAnchor).position:Vector3.zero;
                    if(e.node==VisualNode.Launch && e.flightFrom!=e.actor && origin!=null)
                        from=impactPositions.TryGetValue(e.flightFrom,out var prior)?prior:origin.Anchor(origin.hitAnchor).position;
                    Vector3 to=target!=null?target.Anchor(target.hitAnchor).position:from;
                    if(e.node==VisualNode.Launch)
                    {
                        playbackTarget=e.target; yield return Flight(e,motion,from,to,timing:rhythm); pendingFlight=e; pendingArrival=to;
                    }
                    if(e.node==VisualNode.Impact)
                    {
                        playbackTarget=e.target;
                        if(pendingFlight!=null && pendingFlight.actor==e.actor && pendingFlight.target==e.target && pendingFlight.skill==e.skill && pendingFlight.segment==e.segment) { to=pendingArrival; pendingFlight=null; }
                    }
                    // Damage and state are displayed only at their recorded event, never from final engine HP.
                    RecordVisual(e,from,to);
                    Refresh();
                    switch(e.node)
                    {
                        case VisualNode.CastStart:
                            actor?.Play(motion.attackState); actor?.PlayClip(motion.attackClip,motion.windup+motion.recovery+motion.travel);
                            Effect(motion.castEffect,from,motion); Sound(motion.castSound,motion.volume);
                            if(actor!=null) yield return actor.Windup(to,motion,Duration(motion.windup)); else yield return Delay(motion.windup);
                            break;
                        case VisualNode.Impact:
                            impactPositions[e.target]=to; Floating(e,to);
                            if(!e.dodged && !e.blocked)
                            {
                                Effect(motion.impactEffect,to,motion); Sound(motion.impactSound,motion.volume);
                                var reaction=target!=null && target.motion!=null?target.motion:motion;
                                target?.Play(reaction.hitState); target?.PlayClip(reaction.hitClip,reaction.hitDuration);
                                if(target!=null) yield return target.Hit(reaction,Duration(reaction.hitDuration)*rhythm); else yield return Delay(motion.hitDuration*rhythm);
                                yield return Shake(motion,rhythm);
                            }
                            else
                            {
                                Effect(e.dodged?motion.dodgeEffect:motion.blockEffect,to,motion); Sound(e.dodged?motion.dodgeSound:motion.blockSound,motion.volume);
                                if(target!=null) yield return target.Feedback(e.dodged?motion.dodgeTint:motion.blockTint,Duration(motion.hitDuration)*rhythm,motion.hitDistance*(e.dodged?2:.35f),motion.hitShakeFrequency,motion.hitCurve);
                                else yield return Delay(motion.hitDuration*rhythm);
                            }
                            yield return Delay(motion.segmentInterval*rhythm);
                            break;
                        case VisualNode.Heal: case VisualNode.Shield:
                            Floating(e,to); bool heal=e.node==VisualNode.Heal;
                            Effect(heal?motion.healEffect:motion.shieldEffect,to,motion); Sound(heal?motion.healSound:motion.shieldSound,motion.volume);
                            if(target!=null) yield return target.Feedback(heal?motion.healTint:motion.shieldTint,Duration(motion.hitDuration),0,0,motion.hitCurve);
                            yield return Delay(motion.segmentInterval*rhythm); break;
                        case VisualNode.Death:
                            var death=target!=null && target.motion!=null?target.motion:motion;
                            target?.Play(death.deathState); target?.PlayClip(death.deathClip,death.deathDuration);
                            if(target!=null && target.gameObject.activeInHierarchy) yield return target.Die(Duration(death.deathDuration),death.moveCurve,death.deathScale);
                            break;
                        case VisualNode.CastEnd:
                            Effect(motion.endEffect,actor!=null?actor.Anchor(actor.groundAnchor).position:from,motion); Sound(motion.endSound,motion.volume);
                            if(actor!=null && actor.gameObject.activeInHierarchy) yield return actor.Return(Duration(motion.recovery),motion.moveCurve); else yield return Delay(motion.recovery);
                            actor?.Play(motion.idleState); break;
                    }
                }
                // Let the last real number finish, then clear all temporary objects before unlocking.
                float tail=Mathf.Max(0,lastTemporaryExpiry-Time.unscaledTime);
                yield return Delay(tail); ClearTransientViews();
            }
            finally { presentationRoutine=null; if(message.StartsWith("正在逐段播放")) message=""; dirty=true; }
        }
        void RecordVisual(BattleVisualEvent e,Vector3 from,Vector3 to,int batch=0)
        {
            ApplyFrame(e); presentationCursor++;
            var frame=displayedUnits.TryGetValue(e.target??"",out var shown)?shown:null;
            visualTrace.Add(new VisualPlaybackTrace { node=e.node,actor=e.actor,target=e.target,skill=e.skill,segment=e.segment,eventIndex=e.eventIndex,damageIndex=e.damageIndex,
                damage=e.damage,shield=e.shield,calculated=e.calculated,overkill=e.overkill,hpAfter=frame?.hp??0,shieldAfter=frame?.shield??0,from=from,to=to,
                frame=Time.frameCount,batch=batch,group=e.playbackGroup });
        }
        bool CanBatch(BattleVisualEvent e) => e.node==VisualNode.Launch && e.playbackGroup>0 && Motion(e).simultaneousSegments
            && battle.Data.skills.Find(s=>s.id==e.skill)?.pattern!=Pattern.Bounce;
        float TravelDuration(SkillPresentation motion,Vector3 from,Vector3 to) => motion.projectileSpeed>0
            ? Mathf.Clamp(Vector3.Distance(from,to)/motion.projectileSpeed,Duration(motion.minimumTravel),Mathf.Max(Duration(motion.minimumTravel),Duration(motion.maximumTravel))) : Duration(motion.travel);
        IEnumerator Cosmetic(IEnumerator routine,System.Action complete) { try { yield return routine; } finally { complete(); } }
        IEnumerator Together(List<IEnumerator> routines)
        {
            int pending=routines.Count; var handles=new List<Coroutine>();
            foreach(var routine in routines) { var handle=StartCoroutine(Cosmetic(routine,()=>pending--)); handles.Add(handle); cosmeticRoutines.Add(handle); }
            while(pending>0) yield return null;
            foreach(var handle in handles) cosmeticRoutines.Remove(handle);
        }
        IEnumerator PlayBatch()
        {
            int start=presentationCursor, end=start, count=0; var first=battle.VisualEvents[start]; var motion=Motion(first);
            int limit=Mathf.Clamp(motion.segmentsPerBatch,1,64), batch=++playbackBatch;
            // Keep all intervening state/death/indirect events. A reaction launch is an ordering barrier.
            for(;end<battle.VisualEvents.Count;end++)
            {
                var node=battle.VisualEvents[end];
                if(node.node==VisualNode.CastStart || node.node==VisualNode.CastEnd || node.node==VisualNode.ActionEnd) break;
                if(node.node==VisualNode.Launch)
                {
                    if(node.playbackGroup!=first.playbackGroup || count>=limit) break;
                    count++;
                }
            }
            var coordinates=new Dictionary<int,(Vector3 from,Vector3 to)>(); var launches=new List<BattleVisualEvent>();
            for(int i=start;i<end;i++) if(battle.VisualEvents[i].node==VisualNode.Launch) launches.Add(battle.VisualEvents[i]);
            float flightTime=0;
            for(int i=0;i<launches.Count;i++)
            {
                var launch=launches[i]; var actor=Rig(launch.actor); var target=Rig(launch.target);
                Vector3 from=actor!=null?actor.Anchor(actor.launchAnchor).position:Vector3.zero, to=target!=null?target.Anchor(target.hitAnchor).position:from;
                float spread=(i-(launches.Count-1)*.5f)*Mathf.Max(0,motion.batchProjectileSpacing);
                from+=Vector3.up*spread; to+=Vector3.up*spread;
                coordinates[launch.eventIndex]=(from,to); flightTime=Mathf.Max(flightTime,TravelDuration(motion,from,to));
                int next=end;
                for(int j=launch.eventIndex+1;j<end;j++)
                    if(battle.VisualEvents[j].node==VisualNode.Impact && battle.VisualEvents[j].playbackGroup==launch.playbackGroup && battle.VisualEvents[j].actor==launch.actor && battle.VisualEvents[j].target==launch.target && battle.VisualEvents[j].segment==launch.segment)
                    { next=j; break; }
                if(next<end) coordinates[battle.VisualEvents[next].eventIndex]=(from,to);
            }
            var flights=new List<IEnumerator>(); bool meleeStarted=false;
            foreach(var launch in launches)
            {
                var skill=battle.Data.Skill(launch.skill); var point=coordinates[launch.eventIndex];
                if(skill.reach==Reach.Ranged || motion.meleeProjectile) flights.Add(Flight(launch,motion,point.from,point.to,flightTime));
                else if(!meleeStarted) { flights.Add(Flight(launch,motion,point.from,point.to)); meleeStarted=true; }
            }
            yield return Together(flights);
            var feedback=new Dictionary<string,BattleVisualEvent>(); var deaths=new Dictionary<string,BattleVisualEvent>();
            // All real snapshots are applied exactly once, in original event order, in this same frame.
            while(presentationCursor<end)
            {
                var node=battle.VisualEvents[presentationCursor]; var target=Rig(node.target); var actor=Rig(node.actor); var style=Motion(node);
                var point=coordinates.TryGetValue(node.eventIndex,out var mapped)?mapped:(from:actor!=null?actor.Anchor(actor.launchAnchor).position:Vector3.zero,to:target!=null?target.Anchor(target.hitAnchor).position:Vector3.zero);
                playbackTarget=node.target; RecordVisual(node,point.from,point.to,batch);
                if(node.node==VisualNode.Impact || node.node==VisualNode.Heal || node.node==VisualNode.Shield)
                {
                    string key=node.target??"";
                    Floating(node,point.to); feedback[key]=node;
                    if(node.node==VisualNode.Impact)
                    {
                        impactPositions[key]=point.to;
                        Effect(node.dodged?style.dodgeEffect:node.blocked?style.blockEffect:style.impactEffect,point.to,style);
                        Sound(node.dodged?style.dodgeSound:node.blocked?style.blockSound:style.impactSound,style.volume);
                    }
                    else { bool heal=node.node==VisualNode.Heal; Effect(heal?style.healEffect:style.shieldEffect,point.to,style); Sound(heal?style.healSound:style.shieldSound,style.volume); }
                }
                if(node.node==VisualNode.Death) deaths[node.target]=node;
            }
            Refresh();
            var reactions=new List<IEnumerator>();
            foreach(var pair in feedback)
            {
                if(deaths.ContainsKey(pair.Key)) continue;
                var rig=Rig(pair.Key); if(rig==null || !rig.gameObject.activeInHierarchy) continue;
                var node=pair.Value; var style=Motion(node);
                if(node.node==VisualNode.Impact && !node.dodged && !node.blocked)
                { var reaction=rig.motion!=null?rig.motion:style; rig.Play(reaction.hitState); rig.PlayClip(reaction.hitClip,reaction.hitDuration); reactions.Add(rig.Hit(reaction,Duration(reaction.hitDuration))); }
                else reactions.Add(rig.Feedback(node.node==VisualNode.Heal?style.healTint:node.node==VisualNode.Shield?style.shieldTint:node.dodged?style.dodgeTint:style.blockTint,Duration(style.hitDuration),style.hitDistance,style.hitShakeFrequency,style.hitCurve));
            }
            foreach(var pair in deaths)
            {
                var rig=Rig(pair.Key); if(rig==null || !rig.gameObject.activeInHierarchy) continue;
                var style=rig.motion!=null?rig.motion:Motion(pair.Value); rig.Play(style.deathState); rig.PlayClip(style.deathClip,style.deathDuration);
                reactions.Add(rig.Die(Duration(style.deathDuration),style.moveCurve,style.deathScale));
            }
            if(feedback.Count>0) reactions.Add(Shake(motion));
            yield return Together(reactions); yield return Delay(motion.batchInterval);
        }
        IEnumerator Flight(BattleVisualEvent e,SkillPresentation motion,Vector3 from,Vector3 to,float synchronizedDuration=-1,float timing=1)
        {
            Sound(motion.launchSound,motion.volume);
            var skill=battle.Data.skills.Find(s=>s.id==e.skill);
            bool projectile=skill!=null && (skill.reach==Reach.Ranged || skill.pattern==Pattern.Bounce || motion.meleeProjectile);
            if(!projectile) { var rig=Rig(e.actor); if(rig!=null) yield return rig.Strike(to,motion,Duration(motion.travel)*timing); else yield return Delay(motion.travel*timing); yield break; }
            var prefab=motion.projectile!=null?motion.projectile:viewConfig?.defaultSkill?.projectile;
            var obj=prefab!=null?Instantiate(prefab,from,Quaternion.Euler(motion.projectileRotation),stage.transform):GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if(prefab==null) { obj.name="Projectile Placeholder"; obj.transform.SetParent(stage.transform); var collider=obj.GetComponent<Collider>(); if(collider!=null) Destroy(collider); }
            obj.transform.localScale=motion.projectileScale; transientViews.Add(obj);
            float duration=synchronizedDuration>=0?synchronizedDuration:TravelDuration(motion,from,to)*timing;
            float time=0;
            while(time<duration && obj!=null)
            {
                float t=Mathf.Clamp01(time/Mathf.Max(.001f,duration)); float k=motion.projectileCurve!=null?motion.projectileCurve.Evaluate(t):t;
                obj.transform.position=Vector3.LerpUnclamped(from,to,k)+Vector3.up*(4*t*(1-t)*motion.projectileArc);
                time+=Time.unscaledDeltaTime; yield return null;
            }
            if(obj!=null) { obj.transform.position=to; transientViews.Remove(obj); Destroy(obj); }
        }
        IEnumerator Shake(SkillPresentation motion,float timing=1)
        {
            var camera=Camera.main; if(camera==null || motion.shakeAmplitude<=0) yield break;
            Vector3 original=camera.transform.localPosition; shakingCamera=camera; shakeHome=original; shaking=true; float t=0,duration=Duration(motion.shakeDuration)*timing;
            while(t<duration)
            {
                float phase=t/Mathf.Max(.001f,duration), strength=motion.shakeAmplitude*(motion.shakeCurve!=null?motion.shakeCurve.Evaluate(phase):1-phase);
                camera.transform.localPosition=original+new Vector3(Mathf.Sin(phase*63),Mathf.Sin(phase*91),0)*strength; t+=Time.unscaledDeltaTime; yield return null;
            }
            camera.transform.localPosition=original; shaking=false;
        }
        void Floating(BattleVisualEvent e,Vector3 world)
        {
            if(uiPage==null) return;
            string text; Color color=viewConfig!=null?viewConfig.damageColor:Color.white;
            if(e.node==VisualNode.Heal) { text="治疗 +"+e.amount; color=viewConfig!=null?viewConfig.healColor:Color.green; }
            else if(e.node==VisualNode.Shield) { text="护盾 "+(e.amount>=0?"+":"")+e.amount; color=viewConfig!=null?viewConfig.shieldColor:Color.cyan; }
            else if(e.dodged) { text="闪避"; color=viewConfig!=null?viewConfig.dodgeColor:Color.gray; }
            else if(e.blocked) { text="格挡"; color=viewConfig!=null?viewConfig.blockColor:Color.cyan; }
            else { bool verbose=viewConfig!=null && viewConfig.floatingTextVerbose; text=(e.critical && verbose?"暴击 ":"")+(e.damage>0?(verbose?"生命 −":"−")+e.damage:"")+(e.shield>0?(e.damage>0?"\n":"")+"<color=#"+ColorUtility.ToHtmlStringRGB(viewConfig!=null?viewConfig.shieldColor:Color.cyan)+">护盾 −"+e.shield+"</color>":""); if(text=="") text="0"; if(e.overkill>0) text+="\n过量 "+e.overkill; if(e.critical && viewConfig!=null) color=viewConfig.criticalColor; }
            var obj=viewConfig?.floatingTextPrefab!=null?Instantiate(viewConfig.floatingTextPrefab,uiPage.transform):new GameObject("Floating Text Placeholder",typeof(RectTransform),typeof(Text));
            if(obj.transform.parent==null) obj.transform.SetParent(uiPage.transform,false); obj.layer=5; obj.SetActive(true); transientViews.Add(obj);
            var label=obj.GetComponentInChildren<Text>(); if(label==null) label=obj.AddComponent<Text>(); label.text=text; label.color=color; label.raycastTarget=false;
            label.supportRichText=true; if(label.font==null) label.font=font; if(label.fontSize<18) label.fontSize=26; label.alignment=TextAnchor.MiddleCenter;
            label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,110); label.verticalOverflow=VerticalWrapMode.Overflow;
            var rt=obj.GetComponent<RectTransform>(); rt.anchorMin=rt.anchorMax=new Vector2(0,1); rt.pivot=new Vector2(.5f,.5f); rt.sizeDelta=new Vector2(250,110);
            if(e.critical) rt.localScale*=viewConfig!=null?viewConfig.criticalTextScale:1.2f;
            float lifetime=Duration(viewConfig!=null?viewConfig.floatingTextLifetime:.7f), rise=Mathf.Max(0,viewConfig!=null?viewConfig.floatingTextRise:55);
            var body=FloatingBodyPoint(e.target,world);
            var point=Project(body,uiPage.GetComponent<RectTransform>(),Camera.main);
            Vector2 anchor=ClampFloating(new Vector2(point.x,-point.y),rt,rise);
            rt.anchoredPosition=PlaceFloating(e.target,anchor,rt,rise);
            float drift=((float)floatingRandom.NextDouble()*2-1)*Mathf.Max(0,viewConfig!=null?viewConfig.floatingTextHorizontalDrift:8);
            var placement=new FloatingPlacement {target=e.target,rect=rt}; activeFloating.Add(placement);
            floatingTrace.Add(new FloatingTextSpawn {frame=Time.frameCount,eventIndex=e.eventIndex,damageIndex=e.damageIndex,target=e.target,anchor=anchor,start=rt.anchoredPosition,drift=drift});
            lastTemporaryExpiry=Mathf.Max(lastTemporaryExpiry,Time.unscaledTime+lifetime);
            floatingRoutines.Add(StartCoroutine(FloatAway(obj,rt,label,lifetime,rise,drift,placement)));
        }
        Vector3 FloatingBodyPoint(string targetId,Vector3 fallback)
        {
            var rig=Rig(targetId); if(rig==null) return fallback;
            var renderers=rig.GetComponentsInChildren<Renderer>(true); if(renderers.Length==0) return rig.Anchor(rig.hitAnchor).position;
            var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float height=viewConfig!=null?Mathf.Clamp01(viewConfig.floatingTextBodyHeight):.68f;
            return new Vector3(bounds.center.x,bounds.min.y+bounds.size.y*height,bounds.center.z);
        }
        Vector2 ClampFloating(Vector2 position,RectTransform text,float rise)
        {
            var page=uiPage.GetComponent<RectTransform>();
            float halfHeight=text.rect.height*.5f*Mathf.Abs(text.localScale.y), halfWidth=text.rect.width*.5f*Mathf.Abs(text.localScale.x);
            float top=viewConfig!=null?Mathf.Max(0,viewConfig.floatingTextTopBoundary):170;
            position.x=Mathf.Clamp(position.x,halfWidth,Mathf.Max(halfWidth,page.rect.width-halfWidth));
            position.y=Mathf.Clamp(position.y,-page.rect.height+halfHeight,Mathf.Max(-page.rect.height+halfHeight,-top-rise-halfHeight));
            return position;
        }
        Vector2 PlaceFloating(string targetId,Vector2 anchor,RectTransform text,float rise)
        {
            var range=viewConfig!=null?viewConfig.floatingTextOffsetRange:new Vector2(54,28);
            range=new Vector2(Mathf.Max(0,range.x),Mathf.Max(0,range.y));
            float spacing=Mathf.Max(0,viewConfig!=null?viewConfig.floatingTextAvoidanceSpacing:28), bestScore=-1;
            Vector2 best=anchor; int attempts=Mathf.Clamp(viewConfig!=null?viewConfig.floatingTextPlacementAttempts:24,1,64);
            activeFloating.RemoveAll(p=>p.rect==null);
            // Bounded random candidates only: never grow rings, rows, columns or a radius with attack count.
            for(int i=0;i<attempts;i++)
            {
                var candidate=ClampFloating(anchor+new Vector2(((float)floatingRandom.NextDouble()*2-1)*range.x,((float)floatingRandom.NextDouble()*2-1)*range.y),text,rise);
                float score=float.PositiveInfinity;
                foreach(var placed in activeFloating) if(placed.target==targetId) score=Mathf.Min(score,Vector2.Distance(candidate,placed.rect.anchoredPosition));
                if(score>bestScore) { best=candidate; bestScore=score; }
                if(score>=spacing) break;
            }
            return best;
        }

        IEnumerator FloatAway(GameObject obj,RectTransform rt,Text label,float duration,float rise,float drift,FloatingPlacement placement)
        {
            try
            {
                Vector2 start=rt.anchoredPosition; float time=0;
                var opacity=obj.GetOrAdd<CanvasGroup>(); opacity.alpha=1; opacity.blocksRaycasts=false; opacity.interactable=false;
                float fadeStart=Mathf.Clamp(viewConfig!=null?viewConfig.floatingTextFadeStart:.5f,0,.95f);
                while(obj!=null && time<Duration(duration))
                {
                    float t=time/Mathf.Max(.001f,duration);
                    float k=Mathf.Clamp01(viewConfig?.floatingTextCurve!=null?viewConfig.floatingTextCurve.Evaluate(t):t);
                    rt.anchoredPosition=start+new Vector2(drift*Mathf.Sin(t*Mathf.PI*.5f),rise*k);
                    float alpha=t<=fadeStart?1:1-(t-fadeStart)/(1-fadeStart);
                    opacity.alpha=Mathf.Clamp01(alpha);
                    time+=Time.unscaledDeltaTime; yield return null;
                }
                if(obj!=null) { transientViews.Remove(obj); Destroy(obj); }
            }
            finally { activeFloating.Remove(placement); }
        }
    }
}
