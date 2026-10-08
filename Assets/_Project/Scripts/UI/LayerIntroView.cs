using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>層を移るときの画面：「第2層 鍾乳洞」の名前と、回復したことを見せて「進む」で次へ。</summary>
    public class LayerIntroView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);

        public event Action Continued;

        public static LayerIntroView Create(Transform canvas, UIArt art, int layer, string layerName, string body)
        {
            var root = UIFactory.Stretch("LayerIntroView", canvas);
            var view = root.gameObject.AddComponent<LayerIntroView>();
            UIFactory.Background(root, art != null ? art.MapBackgroundFor(layer) : null, new Color(0.15f, 0.12f, 0.1f));
            var shade = UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f));

            var number = UIFactory.Text("Number", root, $"第{layer + 1}層", 56, PaperColor, new Vector2(1200, 80), new Vector2(0, 200));
            var title = UIFactory.Text("Title", root, layerName, 110, GoldColor, new Vector2(1600, 150), new Vector2(0, 90));
            title.fontStyle = FontStyles.Bold;
            title.outlineWidth = 0.2f;
            title.outlineColor = new Color32(30, 15, 5, 255);
            UIFactory.Text("Body", root, body, 34, PaperColor, new Vector2(1400, 140), new Vector2(0, -80));

            var go = UIFactory.Button("GoButton", root, new Vector2(420, 96), new Vector2(0, -290), new Color(1f, 0.78f, 0.3f), "進む", 38, out _);
            go.onClick.AddListener(() => view.Continued?.Invoke());

            // 文字がふわっと出てくる
            var group = root.gameObject.AddComponent<CanvasGroup>();
            view.StartCoroutine(view.FadeIn(group, title.transform));
            return view;
        }

        IEnumerator FadeIn(CanvasGroup group, Transform title)
        {
            group.alpha = 0f;
            yield return UIAnim.Tween(0.6f, t => group.alpha = t);
            StartCoroutine(UIAnim.Punch(title, 0.08f, 0.4f));
        }

        /// <summary>テスト・自動操作用。</summary>
        public void ContinueForTest() => Continued?.Invoke();
    }
}
