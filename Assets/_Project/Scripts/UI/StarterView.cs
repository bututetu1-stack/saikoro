using System;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>開始時にスターターダイスを1つ選ぶ画面（仕様書 第3章「初期構成」）。</summary>
    public class StarterView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.78f);

        public event Action<DiceData> Chosen;
        System.Collections.Generic.List<DiceData> starterChoices;

        public static StarterView Create(Transform canvas, UIArt art, Phase0Config config)
        {
            var root = UIFactory.Stretch("StarterView", canvas);
            var view = root.gameObject.AddComponent<StarterView>();
            view.starterChoices = config.starterChoices;

            UIFactory.Background(root, art != null ? art.mapBackground : null, new Color(0.85f, 0.8f, 0.65f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.35f));

            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1100, 150), new Vector2(0, 330), ShadeColor);
            UIFactory.Text("Title", titlePanel.transform, "スターターダイスを選ぶ", 56, PaperColor, new Vector2(1000, 80), new Vector2(0, 28)).fontStyle = FontStyles.Bold;
            int normals = config.startingDice.Count;
            UIFactory.Text("Sub", titlePanel.transform, $"普通の賽×{normals} に、選んだ1個を加えて旅に出ます。", 30, PaperColor, new Vector2(1000, 50), new Vector2(0, -40));

            var choices = config.starterChoices;
            const float w = 400f, h = 200f, gap = 50f;
            float left = -(choices.Count * (w + gap) - gap) / 2f + w / 2f;
            // 選ぶ → 「この賽で旅に出る」で決定（ワンクリックで決まると押し間違えやすいため）
            var picker = DicePicker.Create(root, new Vector2(0, 20), art, choices.Select(d => new DiceInstance(d)).ToList(),
                new Vector2(w, h), gap, _ => "クリックで選ぶ");
            view.picker = picker;
            for (int i = 0; i < choices.Count; i++)
            {
                var hint = UIFactory.Panel($"HintPanel{i}", root, new Vector2(w, 90), new Vector2(left + i * (w + gap), -150), ShadeColor);
                UIFactory.Text("Hint", hint.transform, StarterHint(choices[i]), 24, PaperColor, new Vector2(w - 20, 84), Vector2.zero);
            }

            var go = UIFactory.Button("GoButton", root, new Vector2(520, 90), new Vector2(0, -300), new Color(1f, 0.78f, 0.3f), "スターターを選んでください", 32, out var goLabel);
            go.interactable = false;
            picker.SelectionChanged += i =>
            {
                go.interactable = true;
                goLabel.text = $"{choices[i].displayName}で旅に出る";
            };
            go.onClick.AddListener(() =>
            {
                if (picker.Selected >= 0) view.Chosen?.Invoke(choices[picker.Selected]);
            });
            return view;
        }

        DicePicker picker;

        /// <summary>テスト・自動操作用：index 番目を選んで決定する。</summary>
        public void ChooseForTest(int index)
        {
            picker.Select(index);
            Chosen?.Invoke(starterChoices[index]);
        }

        /// <summary>仕様書 第3章「スターター」の「向いているプレイ」。</summary>
        static string StarterHint(DiceData data)
        {
            switch (data.id)
            {
                case "hifumi": return "移動を細かく調整して、狙ったマスに止まる";
                case "tate": return "戦闘を安定させる";
                case "bakuchi": return "一発逆転を狙う";
                default: return data.description;
            }
        }
    }
}
