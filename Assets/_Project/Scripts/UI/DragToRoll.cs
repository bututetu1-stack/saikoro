using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 戦闘のダイス札をドラッグして、振る場所（画面中央）で離すと振る。
    /// 離した場所が振る場所でなければ元の位置に戻る。クリックで選ぶ操作はそのまま使える。
    /// </summary>
    public class DragToRoll : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform dropZone;      // ここに離すと振る
        public RectTransform dragLayer;     // ドラッグ中に札を一番手前に出すための親
        public event Action Dropped;
        public event Action<bool> Dragging; // true：つかんだ／false：離した

        RectTransform rect;
        Transform homeParent;
        int homeIndex;
        Vector2 homePosition;
        CanvasGroup group;

        void Awake()
        {
            rect = (RectTransform)transform;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData e)
        {
            homeParent = rect.parent;
            homeIndex = rect.GetSiblingIndex();
            homePosition = rect.anchoredPosition;
            if (dragLayer != null) rect.SetParent(dragLayer, true);
            group.blocksRaycasts = false; // 下にある振る場所を判定できるように
            rect.localScale = Vector3.one * 0.9f;
            Dragging?.Invoke(true);
        }

        public void OnDrag(PointerEventData e)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rect.parent, e.position, e.pressEventCamera, out var local))
            {
                rect.anchoredPosition = local;
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            bool onZone = dropZone != null && RectTransformUtility.RectangleContainsScreenPoint(dropZone, e.position, e.pressEventCamera);
            group.blocksRaycasts = true;
            rect.localScale = Vector3.one;
            Dragging?.Invoke(false);
            // 元の場所に戻す（振ったあとは札が作り直される）
            rect.SetParent(homeParent, true);
            rect.SetSiblingIndex(homeIndex);
            rect.anchoredPosition = homePosition;
            if (onZone) Dropped?.Invoke();
        }
    }
}
