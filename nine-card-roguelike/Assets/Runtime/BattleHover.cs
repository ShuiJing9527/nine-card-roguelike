using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Qiling
{
    public sealed class BattleHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<bool> Changed;
        public void OnPointerEnter(PointerEventData e) => Changed?.Invoke(true);
        public void OnPointerExit(PointerEventData e) => Changed?.Invoke(false);
        void OnDisable() => Changed?.Invoke(false);
        void OnDestroy() { Changed = null; }
    }

}
