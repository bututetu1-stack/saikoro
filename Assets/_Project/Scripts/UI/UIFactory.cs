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

        public static Image Panel(string name, Transform parent, Vector2 size, Vector2 position, Color color)
        {
            var image = Rect(name, parent, size, position).gameObject.AddComponent<Image>();
            image.color = color;
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

        public static Button Button(string name, Transform parent, Vector2 size, Vector2 position, Color color,
            string label, float fontSize, out TextMeshProUGUI labelText)
        {
            var image = Panel(name, parent, size, position, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            labelText = Text("Label", image.transform, label, fontSize, Color.black, size - new Vector2(12, 8), Vector2.zero);
            return button;
        }
    }
}
