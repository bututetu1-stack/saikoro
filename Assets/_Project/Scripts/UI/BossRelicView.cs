using System;
using System.Collections.Generic;
using SaiNoMichi.Effects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>ボスを倒したあと、ボスレリックを3つから1つ選ぶ画面（受け取らなくてもよい）。選ぶ → 「受け取る」で決定。</summary>
    public class BossRelicView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color CardColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color SelectedColor = new Color(1f, 0.78f, 0.3f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);

        /// <summary>選んだボスレリック（受け取らないなら null）。</summary>
        public event Action<RelicData> Chosen;

        UIArt art;
        IReadOnlyList<RelicData> offer;
        RectTransform content;
        int selected = -1;
        Button takeButton;
        TextMeshProUGUI takeLabel;

        public static BossRelicView Create(Transform canvas, UIArt art, IReadOnlyList<RelicData> offer)
        {
            var root = UIFactory.Stretch("BossRelicView", canvas);
            var view = root.gameObject.AddComponent<BossRelicView>();
            view.art = art;
            view.offer = offer;

            UIFactory.Background(root, art != null ? art.battleBackground : null, new Color(0.2f, 0.12f, 0.1f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f));
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1300, 160), new Vector2(0, 380), new Color(0.08f, 0.05f, 0.04f, 0.9f));
            UIFactory.Text("Title", titlePanel.transform, "ボスレリック", 56, GoldColor, new Vector2(1240, 76), new Vector2(0, 30)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, "強い代わりに悪い効果もある。1つ選ぶか、受け取らずに進む。", 30, PaperColor, new Vector2(1240, 50), new Vector2(0, -38));
            view.content = UIFactory.Rect("Content", root, new Vector2(1900, 520), new Vector2(0, 20));

            view.takeButton = UIFactory.Button("TakeButton", root, new Vector2(460, 90), new Vector2(-260, -380), SelectedColor, "ボスレリックを選んでください", 30, out view.takeLabel);
            view.takeButton.interactable = false;
            view.takeButton.onClick.AddListener(() =>
            {
                if (view.selected >= 0) view.Chosen?.Invoke(view.offer[view.selected]);
            });
            var skip = UIFactory.Button("SkipButton", root, new Vector2(420, 90), new Vector2(260, -380), CardColor, "受け取らない", 30, out _);
            skip.onClick.AddListener(() => view.Chosen?.Invoke(null));

            view.Rebuild();
            return view;
        }

        void Rebuild()
        {
            UIFactory.ClearChildren(content);
            const float w = 520f, h = 460f, gap = 40f;
            float left = -(offer.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < offer.Count; i++)
            {
                var relic = offer[i];
                int index = i;
                var button = UIFactory.Button($"Relic{i}", content, new Vector2(w, h), new Vector2(left + i * (w + gap), 0),
                    i == selected ? SelectedColor : CardColor, "", 1, out var unused);
                Destroy(unused.gameObject);
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.55f, 0.25f, 0.6f);
                outline.effectDistance = new Vector2(6, -6);
                if (i == selected) button.transform.localScale = Vector3.one * 1.05f;
                button.onClick.AddListener(() => Select(index));

                UIFactory.Picture("Icon", button.transform, relic.icon, new Vector2(130, 130), new Vector2(0, 140), new Color(0.6f, 0.4f, 0.7f));
                UIFactory.Text("Name", button.transform, relic.displayName, 40, InkColor, new Vector2(w - 30, 56), new Vector2(0, 40)).fontStyle = FontStyles.Bold;
                UIFactory.Text("Description", button.transform, relic.description, 26, InkColor, new Vector2(w - 50, 200), new Vector2(0, -100), TextAlignmentOptions.Top);
                UIFactory.Text("State", button.transform, i == selected ? "<b>選択中</b>" : "クリックで選ぶ", 22, new Color(0.45f, 0.3f, 0.2f), new Vector2(w - 20, 30), new Vector2(0, -h / 2f + 22));
            }
        }

        void Select(int index)
        {
            selected = index;
            takeButton.interactable = true;
            takeLabel.text = $"「{offer[index].displayName}」を受け取る";
            Rebuild();
        }

        /// <summary>テスト・自動操作用（-1 で受け取らない）。</summary>
        public void ChooseForTest(int index) => Chosen?.Invoke(index >= 0 ? offer[index] : null);
    }
}
