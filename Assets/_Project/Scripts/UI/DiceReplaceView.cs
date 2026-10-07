using System;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ポーチが満杯のときに、新しいダイスと入れ替えるダイスを選ぶ画面（宝箱・イベント・ショップで共通）。
    /// </summary>
    public class DiceReplaceView : MonoBehaviour
    {
        public event Action<DiceInstance> Replaced;
        public event Action Cancelled;
        DicePicker picker;

        public static DiceReplaceView Create(Transform canvas, UIArt art, DicePouch pouch, DiceData incoming)
        {
            var root = UIFactory.Stretch("DiceReplaceView", canvas);
            var view = root.gameObject.AddComponent<DiceReplaceView>();
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.7f));

            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1200, 170), new Vector2(0, 340), new Color(0.08f, 0.05f, 0.04f, 0.9f));
            UIFactory.Text("Title", titlePanel.transform, "ポーチが満杯です", 50, new Color(1f, 0.82f, 0.3f), new Vector2(1100, 80), new Vector2(0, 32)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, $"「{incoming.displayName}」と入れ替えるダイスを選んでください。", 30, new Color(0.96f, 0.92f, 0.82f), new Vector2(1100, 60), new Vector2(0, -42));

            var content = UIFactory.Rect("Content", root, new Vector2(1900, 600), new Vector2(0, -80));
            view.picker = RewardView.BuildReplace(content, art, pouch, incoming,
                die => view.Replaced?.Invoke(die), () => view.Cancelled?.Invoke(), "受け取らない");
            return view;
        }

        /// <summary>テスト・自動操作用。</summary>
        public void ReplaceForTest(int index)
        {
            picker.Select(index);
            if (picker.SelectedDie != null) Replaced?.Invoke(picker.SelectedDie);
        }
    }
}
