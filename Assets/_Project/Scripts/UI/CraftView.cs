using System;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>流しの職人：ダイスの面を1つ選び、1〜6の好きな値に変える画面。面 → 値 → 決定。</summary>
    public class CraftView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color SelectedColor = new Color(1f, 0.78f, 0.3f);

        public event Action<int, int> Applied;   // 面の番号, 新しい値
        public event Action Cancelled;

        DiceInstance die;
        int face = -1;
        int value = -1;
        RectTransform faceRow, valueRow;
        Button ok;
        TextMeshProUGUI okLabel;

        public static CraftView Create(Transform canvas, DiceInstance die, string subtitle)
        {
            var root = UIFactory.Stretch("CraftView", canvas);
            var view = root.gameObject.AddComponent<CraftView>();
            view.die = die;
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.85f));
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1200, 170), new Vector2(0, 340), new Color(0.08f, 0.05f, 0.04f, 0.9f));
            UIFactory.Text("Title", titlePanel.transform, $"流しの職人：{die.DisplayName}", 50, new Color(1f, 0.82f, 0.3f), new Vector2(1100, 80), new Vector2(0, 32)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, subtitle, 28, PaperColor, new Vector2(1100, 60), new Vector2(0, -42));

            UIFactory.Text("FaceLabel", root, "変える面", 30, PaperColor, new Vector2(400, 50), new Vector2(0, 175));
            view.faceRow = UIFactory.Rect("Faces", root, new Vector2(1000, 120), new Vector2(0, 90));
            UIFactory.Text("ValueLabel", root, "新しい値", 30, PaperColor, new Vector2(400, 50), new Vector2(0, -15));
            view.valueRow = UIFactory.Rect("Values", root, new Vector2(1000, 120), new Vector2(0, -100));

            view.ok = UIFactory.Button("ConfirmButton", root, new Vector2(460, 84), new Vector2(-250, -300), SelectedColor, "", 30, out view.okLabel);
            view.ok.onClick.AddListener(() => { if (view.face >= 0 && view.value >= 1) view.Applied?.Invoke(view.face, view.value); });
            var back = UIFactory.Button("BackButton", root, new Vector2(420, 84), new Vector2(250, -300), ButtonColor, "やめる", 30, out _);
            back.onClick.AddListener(() => view.Cancelled?.Invoke());
            view.Rebuild();
            return view;
        }

        void Rebuild()
        {
            UIFactory.ClearChildren(faceRow);
            UIFactory.ClearChildren(valueRow);
            for (int i = 0; i < die.faces.Length; i++)
            {
                int index = i;
                var f = die.faces[i];
                string label = f.engraving != null ? $"{f.value}<size=20>\n{f.engraving.badge}</size>" : f.value.ToString();
                var b = UIFactory.Button($"Face{i}", faceRow, new Vector2(110, 110), new Vector2((i - 2.5f) * 130, 0), i == face ? SelectedColor : ButtonColor, label, 44, out _);
                b.onClick.AddListener(() => { face = index; Rebuild(); });
            }
            for (int v = 1; v <= 6; v++)
            {
                int nv = v;
                var b = UIFactory.Button($"Value{v}", valueRow, new Vector2(110, 110), new Vector2((v - 3.5f) * 130, 0), v == value ? SelectedColor : ButtonColor, v.ToString(), 44, out _);
                b.onClick.AddListener(() => { value = nv; Rebuild(); });
            }
            ok.interactable = face >= 0 && value >= 1;
            okLabel.text = face < 0 ? "面を選んでください" : value < 1 ? "値を選んでください" : $"{die.faces[face].value} → {value} にする";
        }

        /// <summary>テスト・自動操作用。</summary>
        public void ApplyForTest(int faceIndex, int newValue) => Applied?.Invoke(faceIndex, newValue);
    }
}
