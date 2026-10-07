using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SaiNoMichi.UI
{
    /// <summary>マウスが乗った／離れたことを通知する。</summary>
    public class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action Entered;
        public event Action Exited;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
