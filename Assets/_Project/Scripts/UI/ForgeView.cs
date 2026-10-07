using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 鍛冶の画面（仕様書 第5章）。提示された刻印から1つを選び、付けるダイスと面を選んで「決定」。
    /// 鍛冶マスと、休憩の「鍛える」で使う。
    /// </summary>
    public class ForgeView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color CardColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color AccentColor = new Color(1f, 0.78f, 0.3f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.85f);

        /// <summary>刻印・ダイス・面の番号。</summary>
        public event Action<EngravingData, DiceInstance, int> Applied;
        public event Action Cancelled;

        UIArt art;
        IReadOnlyList<EngravingData> offer;
        DicePouch pouch;
        bool canCancel;
        TextMeshProUGUI titleText;
        TextMeshProUGUI subText;
        RectTransform content;

        EngravingData chosenEngraving;
        DiceInstance chosenDie;
        int chosenFace = -1;

        public static ForgeView Create(Transform canvas, UIArt art, IReadOnlyList<EngravingData> offer, DicePouch pouch, bool canCancel)
        {
            var root = UIFactory.Stretch("ForgeView", canvas);
            var view = root.gameObject.AddComponent<ForgeView>();
            view.art = art;
            view.offer = offer;
            view.pouch = pouch;
            view.canCancel = canCancel;

            UIFactory.Background(root, art != null ? art.mapBackground : null, new Color(0.3f, 0.22f, 0.18f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.55f));
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1300, 140), new Vector2(0, 410), ShadeColor);
            view.titleText = UIFactory.Text("Title", titlePanel.transform, "", 50, AccentColor, new Vector2(1240, 70), new Vector2(0, 26));
            view.titleText.fontStyle = FontStyles.Bold;
            view.subText = UIFactory.Text("Sub", titlePanel.transform, "", 28, PaperColor, new Vector2(1240, 50), new Vector2(0, -38));
            view.content = UIFactory.Rect("Content", root, new Vector2(1920, 760), new Vector2(0, -110));

            view.ShowEngravings();
            return view;
        }

        // ---- 1. 刻印を選ぶ ----

        void ShowEngravings()
        {
            UIFactory.ClearChildren(content);
            chosenEngraving = null;
            titleText.text = "鍛冶";
            subText.text = "付ける刻印を1つ選んでください。";

            const float w = 440f, h = 300f, gap = 40f;
            float left = -(offer.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < offer.Count; i++)
            {
                var e = offer[i];
                var button = UIFactory.Button($"Engraving{i}", content, new Vector2(w, h), new Vector2(left + i * (w + gap), 160), CardColor, "", 1, out var unused);
                Destroy(unused.gameObject);
                var frame = DiceCard.RarityColor(e.rarity);
                if (frame.HasValue)
                {
                    var o = button.gameObject.AddComponent<Outline>();
                    o.effectColor = frame.Value;
                    o.effectDistance = new Vector2(6, -6);
                }
                var badge = UIFactory.Panel("Badge", button.transform, new Vector2(90, 90), new Vector2(0, 80), new Color(0.7f, 0.12f, 0.1f));
                UIFactory.Text("BadgeText", badge.transform, e.badge, 60, Color.white, new Vector2(90, 90), Vector2.zero).fontStyle = FontStyles.Bold;
                UIFactory.Text("Name", button.transform, e.displayName, 40, InkColor, new Vector2(w - 20, 50), new Vector2(0, 0)).fontStyle = FontStyles.Bold;
                UIFactory.Text("Kind", button.transform, e.kind == EngravingKind.Numeric ? "数値刻印（面の数字を変える）" : "効果刻印（その面が出たときに効く）", 20,
                    new Color(0.45f, 0.3f, 0.2f), new Vector2(w - 20, 30), new Vector2(0, -40));
                UIFactory.Text("Description", button.transform, e.description, 24, InkColor, new Vector2(w - 30, 80), new Vector2(0, -95));
                button.onClick.AddListener(() => ShowDice(e));
            }

            if (canCancel) AddButton("やめる", new Vector2(0, -320), () => Cancelled?.Invoke());
        }

        // ---- 2. ダイスと面を選ぶ ----

        void ShowDice(EngravingData engraving)
        {
            UIFactory.ClearChildren(content);
            chosenEngraving = engraving;
            chosenDie = null;
            chosenFace = -1;
            titleText.text = $"「{engraving.displayName}」を付ける面を選ぶ";
            subText.text = engraving.description;

            var dice = pouch.All.ToList();
            const float rowH = 108f, face = 84f;
            float top = 290f;
            for (int d = 0; d < dice.Count; d++)
            {
                var die = dice[d];
                float y = top - d * rowH;
                var row = UIFactory.Panel($"Row{d}", content, new Vector2(1100, rowH - 10), new Vector2(0, y), new Color(0.12f, 0.08f, 0.06f, 0.75f));
                UIFactory.Text("Name", row.transform, die.DisplayName, 30, PaperColor, new Vector2(220, 60), new Vector2(-420, 0), TextAlignmentOptions.Left);
                for (int f = 0; f < die.faces.Length; f++)
                {
                    int faceIndex = f;
                    var fv = DiceFaceView.Create($"Face{f}", row.transform, art, face, new Vector2(-180 + f * (face + 16), 0));
                    fv.SetFace(die.faces[f]);
                    var img = fv.GetComponent<Image>();
                    img.raycastTarget = true;
                    var b = fv.gameObject.AddComponent<Button>();
                    b.targetGraphic = img;
                    b.onClick.AddListener(() => SelectFace(die, faceIndex));
                }
            }

            var previewBack = UIFactory.Panel("PreviewBack", content, new Vector2(1100, 56), new Vector2(0, top - dice.Count * rowH - 10), ShadeColor);
            previewBack.raycastTarget = false;
            previewText = UIFactory.Text("Preview", content, "付けたい面をクリックしてください。", 30, AccentColor, new Vector2(1080, 50), new Vector2(0, top - dice.Count * rowH - 10));
            previewText.outlineWidth = 0.25f;
            previewText.outlineColor = new Color32(30, 15, 5, 255);

            confirmButton = AddButton("決定", new Vector2(160, -320), Confirm);
            confirmButton.interactable = false;
            AddButton("刻印を選び直す", new Vector2(-160, -320), ShowEngravings);
            if (canCancel) AddButton("やめる", new Vector2(480, -320), () => Cancelled?.Invoke());
        }

        TextMeshProUGUI previewText;
        Button confirmButton;
        Image selectionFrame;

        void SelectFace(DiceInstance die, int faceIndex)
        {
            chosenDie = die;
            chosenFace = faceIndex;
            var before = die.faces[faceIndex];
            var after = RunState.Engraved(before, chosenEngraving);
            string change;
            if (chosenEngraving.kind == EngravingKind.Numeric)
            {
                change = before.value == after.value ? $"{before.value} のまま（これ以上変えられない）" : $"{before.value} → {after.value}";
            }
            else
            {
                change = before.engraving != null ? $"「{before.engraving.displayName}」を「{chosenEngraving.displayName}」に上書き" : $"「{chosenEngraving.displayName}」を付ける";
            }
            previewText.text = $"{die.DisplayName} の面（{before.value}）：{change}";
            confirmButton.interactable = true;

            // 選んだ面に枠を付ける
            var row = content.Find($"Row{pouch.All.ToList().IndexOf(die)}");
            var faceRect = (RectTransform)row.Find($"Face{faceIndex}");
            if (selectionFrame != null) Destroy(selectionFrame.gameObject);
            selectionFrame = UIFactory.Panel("Selected", row, faceRect.sizeDelta + new Vector2(14, 14), faceRect.anchoredPosition, AccentColor);
            selectionFrame.raycastTarget = false;
            selectionFrame.transform.SetSiblingIndex(faceRect.GetSiblingIndex());
        }

        void Confirm()
        {
            if (chosenEngraving == null || chosenDie == null || chosenFace < 0) return;
            Applied?.Invoke(chosenEngraving, chosenDie, chosenFace);
        }

        Button AddButton(string label, Vector2 pos, Action onClick)
        {
            var b = UIFactory.Button(label, content, new Vector2(280, 80), pos, CardColor, label, 30, out _);
            b.onClick.AddListener(() => onClick());
            return b;
        }

        // テスト・自動操作用
        public void ChooseForTest(int engravingIndex, DiceInstance die, int faceIndex)
        {
            ShowDice(offer[engravingIndex]);
            SelectFace(die, faceIndex);
            Confirm();
        }
    }
}
