using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Qiling
{
    /// <summary>Stable hit bands independent of the animated/rotated card face.</summary>
    public sealed class HandCardInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ICanvasRaycastFilter
    {
        public RectTransform face;
        public BattleViewConfig config;
        Vector3 baseScale;
        Vector2 targetPosition; Vector3 targetScale; Quaternion targetRotation;
        bool selected, pulseActive;
        public int index;
        public float angle, bandMin = -77.5f, bandMax = 77.5f;
        public Action<bool> Hovered;
        Vector2 rest;
        bool raised;
        public bool Raised => raised;
        public void Initialize() { rest = face.anchoredPosition; baseScale=face.localScale; ((RectTransform)transform).anchoredPosition=rest; Restore(); }
        public void OnPointerEnter(PointerEventData e)
        {
            raised = true;
            targetPosition = rest + Vector2.up * (config!=null?config.hoverLift:65); targetRotation=Quaternion.identity; targetScale=baseScale*(config!=null?config.hoverScale:1.12f); face.SetAsLastSibling();
            Play(config?.cardHoverSound);
            Hovered?.Invoke(true);
        }
        public void OnPointerExit(PointerEventData e) { Restore(); Hovered?.Invoke(false); }
        public void Restore()
        {
            raised = false; if (face == null) return;
            targetPosition=rest; targetRotation=Quaternion.Euler(0,0,angle); targetScale=baseScale; face.anchoredPosition=rest; face.localRotation=targetRotation; face.localScale=baseScale; face.SetSiblingIndex(index);
        }
        public bool IsRaycastLocationValid(Vector2 screen, Camera camera)
        {
            if (!isActiveAndEnabled) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, camera, out Vector2 local)) return false;
            if (local.x < bandMin || local.x >= bandMax) return false;
            Vector2 basePoint = Quaternion.Euler(0, 0, -angle) * local;
            bool insideBase = Mathf.Abs(basePoint.x) <= face.rect.width/2 && Mathf.Abs(basePoint.y) <= face.rect.height/2;
            return insideBase || raised && Mathf.Abs(local.x) <= face.rect.width/2*(config!=null?config.hoverScale:1.12f) && Mathf.Abs(local.y - (config!=null?config.hoverLift:65)) <= face.rect.height/2*(config!=null?config.hoverScale:1.12f);
        }
        float transition;
        public void SetSelected(bool value)
        {
            if(face==null) return;
            if(value!=selected) { Play(value?config?.cardAddSound:config?.cardRemoveSound); selected=value; transition=0; pulseActive=true; }
            var image=face.GetComponent<Image>(); if(image!=null && config!=null) image.color=selected?config.cardSelected:config.cardIdle;
        }
        void Play(AudioClip clip) { if(clip!=null) { var audio=GetComponentInParent<BattleDemo>().gameObject.GetOrAdd<AudioSource>(); audio.PlayOneShot(clip); } }
        void Update()
        {
            if(face==null) return; float duration=config!=null?config.hoverDuration:.1f;
            float step=duration<=0?1:Mathf.Clamp01(Time.unscaledDeltaTime/duration*4);
            if(raised) { face.anchoredPosition=Vector2.Lerp(face.anchoredPosition,targetPosition,step); face.localRotation=Quaternion.Slerp(face.localRotation,targetRotation,step); face.localScale=Vector3.Lerp(face.localScale,targetScale,step); }
            else if(pulseActive && config!=null) { transition+=Time.unscaledDeltaTime; float t=config.cardArrangeDuration<=0?1:Mathf.Clamp01(transition/config.cardArrangeDuration); face.localScale=baseScale*Mathf.Lerp(config.selectPulse,1,config.cardCurve!=null?config.cardCurve.Evaluate(t):t); if(t>=1) pulseActive=false; }
        }
        void OnDisable() { Restore(); }
    }

}
