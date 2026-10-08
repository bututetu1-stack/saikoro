using System;
using System.Linq;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>ダイスを1個選んで削除する画面（ショップ・イベントで共通）。呪いのダイスも選べる。選ぶ → 「削除する」で決定。</summary>
    public class DiceRemoveView : MonoBehaviour
    {
        public event Action<DiceInstance> Removed;
        public event Action Cancelled;
        DicePicker picker;

        /// <param name="cancellable">false なら「やめる」を出さない（黄金の賽筒で容量を超えたときなど、必ず1個手放す場面）。</param>
        public static DiceRemoveView Create(Transform canvas, UIArt art, DicePouch pouch, string subtitle,
            string title = "ダイスを削除する", string verb = "削除する", bool cancellable = true)
        {
            var root = UIFactory.Stretch("DiceRemoveView", canvas);
            var view = root.gameObject.AddComponent<DiceRemoveView>();
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.75f));

            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1200, 170), new Vector2(0, 340), new Color(0.08f, 0.05f, 0.04f, 0.9f));
            UIFactory.Text("Title", titlePanel.transform, title, 50, new Color(1f, 0.82f, 0.3f), new Vector2(1100, 80), new Vector2(0, 32)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, subtitle, 30, new Color(0.96f, 0.92f, 0.82f), new Vector2(1100, 60), new Vector2(0, -42));

            var content = UIFactory.Rect("Content", root, new Vector2(1900, 600), new Vector2(0, -80));
            var dice = pouch.All.ToList();
            view.picker = DicePicker.Create(content, new Vector2(0, 120), art, dice, new Vector2(300, 170), 24, _ => "クリックで選ぶ");
            var remove = UIFactory.Button("RemoveButton", content, new Vector2(460, 84), new Vector2(-250, -120), new Color(1f, 0.55f, 0.4f), verb, 30, out var label);
            remove.interactable = false;
            view.picker.SelectionChanged += i =>
            {
                remove.interactable = true;
                label.text = $"{dice[i].DisplayName} を{verb}";
            };
            remove.onClick.AddListener(() =>
            {
                if (view.picker.SelectedDie != null) view.Removed?.Invoke(view.picker.SelectedDie);
            });
            var back = UIFactory.Button("BackButton", content, new Vector2(420, 84), new Vector2(250, -120), new Color(0.93f, 0.87f, 0.72f), "やめる", 30, out _);
            back.onClick.AddListener(() => view.Cancelled?.Invoke());
            if (!cancellable)
            {
                back.gameObject.SetActive(false);
                remove.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -120);
            }
            return view;
        }

        /// <summary>テスト・自動操作用。</summary>
        public void RemoveForTest(int index)
        {
            picker.Select(index);
            if (picker.SelectedDie != null) Removed?.Invoke(picker.SelectedDie);
        }
    }
}
