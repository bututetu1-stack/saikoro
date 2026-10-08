using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>プロトタイプ用に、四角と文字だけの uGUI 部品をコードで作る。座標は親の中心が原点。</summary>
    public static class UIFactory
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return rt;
        }

        /// <summary>親いっぱいに広がる RectTransform。</summary>
        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>子をすべて消す。Destroy はフレームの終わりまで残るので、先に非表示にして押せないようにする。</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        public static Image Panel(string name, Transform parent, Vector2 size, Vector2 position, Color color)
        {
            var image = Rect(name, parent, size, position).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>絵を表示する。sprite が null なら fallback の色の四角になる。</summary>
        public static Image Picture(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 position, Color? fallback = null)
        {
            var image = Panel(name, parent, size, position, sprite != null ? Color.white : (fallback ?? new Color(1, 1, 1, 0.3f)));
            image.sprite = sprite;
            image.preserveAspect = sprite != null;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>画面いっぱいの背景。縦横比を保ったまま、はみ出す側を切って画面を覆う。</summary>
        public static Image Background(Transform parent, Sprite sprite, Color fallback)
        {
            var holder = Stretch("Background", parent);
            var image = holder.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            if (sprite == null)
            {
                image.color = fallback;
                return image;
            }
            image.sprite = sprite;
            var fitter = holder.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            return image;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float fontSize, Color color,
            Vector2 size, Vector2 position, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var tmp = Rect(name, parent, size, position).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = UIFont.Japanese;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            return tmp;
        }

        /// <summary>
        /// 横にスクロールできる枠（ダイスのトレイなど）。中身を並べる content を返す。
        /// 中身の幅は SetScrollWidth で決める（枠より狭ければスクロールしない）。マウスのホイールでも横に動く。
        /// </summary>
        public static RectTransform HorizontalScroll(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var viewport = Rect(name, parent, size, position);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); // ドラッグを受け止めるため（見えない）
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, size, Vector2.zero);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            // はみ出しているときだけ、下に細いスクロールバーを出す（まだ続きがあるとわかるように）
            var bar = Rect("Scrollbar", viewport, new Vector2(0, 10), Vector2.zero);
            bar.anchorMin = new Vector2(0, 0);
            bar.anchorMax = new Vector2(1, 0);
            bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, 10);
            bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.05f, 0.04f, 0.6f);
            var area = Stretch("SlidingArea", bar);
            var handle = Stretch("Handle", area);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(1f, 0.82f, 0.3f, 0.9f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scroll.horizontalScrollbar = scrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        /// <summary>HorizontalScroll の中身の幅を決める。枠に収まるときは真ん中に戻す。</summary>
        public static void SetScrollWidth(RectTransform content, float width)
        {
            var viewport = (RectTransform)content.parent;
            float w = Mathf.Max(width, viewport.sizeDelta.x);
            bool resized = !Mathf.Approximately(content.sizeDelta.x, w);
            content.sizeDelta = new Vector2(w, content.sizeDelta.y);
            if (width <= viewport.sizeDelta.x) content.anchoredPosition = new Vector2(0, content.anchoredPosition.y);
            else if (resized)
            {
                // 幅が変わったら左端から見せる
                content.anchoredPosition = new Vector2((w - viewport.sizeDelta.x) / 2f, content.anchoredPosition.y);
            }
        }

        public static Button Button(string name, Transform parent, Vector2 size, Vector2 position, Color color,
            string label, float fontSize, out TextMeshProUGUI labelText)
        {
            var image = Panel(name, parent, size, position, color);
            var button = image.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => Sfx.Play(SoundId.Button));
            button.targetGraphic = image;
            labelText = Text("Label", image.transform, label, fontSize, Color.black, size - new Vector2(12, 8), Vector2.zero);
            return button;
        }
    }
}
