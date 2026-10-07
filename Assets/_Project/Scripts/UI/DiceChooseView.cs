using System;
using System.Linq;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>使用可能なダイスを1個選ぶ画面（イベントの「振って判定」など）。選ぶ → 決定ボタン。</summary>
    public class DiceChooseView : MonoBehaviour
    {
        public event Action<DiceInstance> Chosen;
        public event Action Cancelled;
        DicePicker picker;

        public static DiceChooseView Create(Transform canvas, UIArt art, DicePouch pouch, string title, string subtitle, string confirmVerb)
        {
            var root = UIFactory.Stretch("DiceChooseView", canvas);
            var view = root.gameObject.AddComponent<DiceChooseView>();
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.85f));

            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1200, 170), new Vector2(0, 340), new Color(0.08f, 0.05f, 0.04f, 0.9f));
            UIFactory.Text("Title", titlePanel.transform, title, 50, new Color(1f, 0.82f, 0.3f), new Vector2(1100, 80), new Vector2(0, 32)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, subtitle, 28, new Color(0.96f, 0.92f, 0.82f), new Vector2(1100, 60), new Vector2(0, -42));

            var content = UIFactory.Rect("Content", root, new Vector2(1900, 600), new Vector2(0, -80));
            var dice = pouch.All.ToList();
            bool Usable(int i) => dice[i].state == DiceState.Available;
            view.picker = DicePicker.Create(content, new Vector2(0, 120), art, dice, new Vector2(300, 170), 24,
                i => Usable(i) ? "クリックで選ぶ" : dice[i].state == DiceState.Sealed ? "封印中" : "使用済み", Usable);
            var ok = UIFactory.Button("ConfirmButton", content, new Vector2(460, 84), new Vector2(-250, -120), new Color(1f, 0.78f, 0.3f), "ダイスを選んでください", 30, out var label);
            ok.interactable = false;
            view.picker.SelectionChanged += i =>
            {
                ok.interactable = true;
                label.text = $"{dice[i].DisplayName}で{confirmVerb}";
            };
            ok.onClick.AddListener(() =>
            {
                if (view.picker.SelectedDie != null) view.Chosen?.Invoke(view.picker.SelectedDie);
            });
            var back = UIFactory.Button("BackButton", content, new Vector2(420, 84), new Vector2(250, -120), new Color(0.93f, 0.87f, 0.72f), "やめる", 30, out _);
            back.onClick.AddListener(() => view.Cancelled?.Invoke());
            return view;
        }

        /// <summary>テスト・自動操作用。</summary>
        public void ChooseForTest(int index)
        {
            picker.Select(index);
            if (picker.SelectedDie != null) Chosen?.Invoke(picker.SelectedDie);
        }
    }
}
