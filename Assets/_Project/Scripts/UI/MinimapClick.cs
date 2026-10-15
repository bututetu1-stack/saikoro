using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SaiNoMichi.UI
{
    /// <summary>押された位置（この枠の中の位置）を知らせる（国めぐりの小さな地図）。</summary>
    public class MinimapClick : MonoBehaviour, IPointerClickHandler
    {
        public event Action<Vector2> Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            var rt = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out var local)) Clicked?.Invoke(local);
        }
    }
}
