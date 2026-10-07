using System.Collections.Generic;
using SaiNoMichi.Core;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>持っているレリックのアイコンを横に並べる。マウスを乗せると名前と説明を出す（マップ・戦闘で共通）。</summary>
    public class RelicBar : MonoBehaviour
    {
        const float IconSize = 64f;
        const float Gap = 10f;
        static readonly Color BackColor = new Color(0.08f, 0.05f, 0.04f, 0.75f);
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);

        RectTransform iconsRoot;
        RectTransform tooltip;
        TextMeshProUGUI tooltipText;
        readonly Dictionary<RelicData, RectTransform> icons = new Dictionary<RelicData, RectTransform>();
        readonly List<RelicData> shown = new List<RelicData>();

        /// <summary>leftTop：バーの左上の位置（親の中心が原点）。</summary>
        public static RelicBar Create(Transform parent, Vector2 leftTop)
        {
            var root = UIFactory.Rect("RelicBar", parent, new Vector2(IconSize, IconSize), leftTop);
            root.pivot = new Vector2(0, 1);
            var bar = root.gameObject.AddComponent<RelicBar>();
            bar.iconsRoot = UIFactory.Rect("Icons", root, Vector2.zero, Vector2.zero);
            bar.iconsRoot.anchorMin = bar.iconsRoot.anchorMax = new Vector2(0, 1);

            bar.tooltip = UIFactory.Panel("Tooltip", root, new Vector2(460, 120), Vector2.zero, new Color(0.08f, 0.05f, 0.04f, 0.95f)).rectTransform;
            bar.tooltip.anchorMin = bar.tooltip.anchorMax = new Vector2(0, 1);
            bar.tooltip.pivot = new Vector2(0, 1);
            bar.tooltip.GetComponent<Image>().raycastTarget = false;
            bar.tooltipText = UIFactory.Text("Text", bar.tooltip, "", 26, PaperColor, new Vector2(436, 110), Vector2.zero, TextAlignmentOptions.TopLeft);
            bar.tooltip.gameObject.SetActive(false);
            return bar;
        }

        /// <summary>持っているレリックに合わせて並べ直す。残り回数（草鞋など）も更新する。</summary>
        public void Refresh(RunState run)
        {
            bool same = shown.Count == run.Relics.Count;
            for (int i = 0; same && i < shown.Count; i++) same = shown[i] == run.Relics[i];
            if (!same)
            {
                // 増えたとき（最初の表示を除く）はレリック入手の音
                if (built && run.Relics.Count > shown.Count) Sfx.Play(SoundId.Relic);
                Rebuild(run.Relics);
            }
            built = true;

            foreach (var kv in icons)
            {
                int charges = run.ChargesOf(kv.Key);
                var label = kv.Value.Find("Charges")?.GetComponent<TextMeshProUGUI>();
                if (label != null) label.text = charges >= 0 ? charges.ToString() : "";
                var image = kv.Value.Find("Icon")?.GetComponent<Image>();
                // 回数を使い切ったら暗くする
                if (image != null) image.color = charges == 0 ? new Color(0.45f, 0.45f, 0.45f) : Color.white;
            }
            currentRun = run;
        }

        RunState currentRun;
        bool built;

        void Rebuild(IReadOnlyList<RelicData> relics)
        {
            UIFactory.ClearChildren(iconsRoot);
            icons.Clear();
            shown.Clear();
            shown.AddRange(relics);
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                var slot = UIFactory.Panel($"Relic_{relic.id}", iconsRoot, new Vector2(IconSize, IconSize),
                    new Vector2(IconSize / 2 + i * (IconSize + Gap), -IconSize / 2), BackColor).rectTransform;
                var icon = UIFactory.Picture("Icon", slot, relic.icon, new Vector2(IconSize - 6, IconSize - 6), Vector2.zero, RarityColor(relic.rarity));
                if (relic.icon == null)
                {
                    UIFactory.Text("Name", icon.transform, relic.displayName.Substring(0, 1), 30, Color.black, new Vector2(IconSize, IconSize), Vector2.zero);
                }
                var charges = UIFactory.Text("Charges", slot, "", 24, new Color(1f, 0.85f, 0.35f), new Vector2(30, 30), new Vector2(IconSize / 2 - 12, -IconSize / 2 + 12));
                charges.fontStyle = FontStyles.Bold;
                charges.outlineWidth = 0.25f;
                charges.outlineColor = new Color32(0, 0, 0, 255);

                var hover = slot.gameObject.AddComponent<HoverRelay>();
                int index = i;
                hover.Entered += () => ShowTooltip(relic, index);
                hover.Exited += () => tooltip.gameObject.SetActive(false);
                icons[relic] = slot;
            }
        }

        void ShowTooltip(RelicData relic, int index)
        {
            string text = $"<b><color={RarityHex(relic.rarity)}>{relic.displayName}</color></b>　<size=20>{RarityName(relic.rarity)}</size>\n{relic.description}";
            int charges = currentRun != null ? currentRun.ChargesOf(relic) : -1;
            if (charges >= 0) text += $"\n<color=#FFD24D>残り {charges} 回</color>";
            tooltipText.text = text;
            tooltip.anchoredPosition = new Vector2(index * (IconSize + Gap), -IconSize - 8);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
        }

        /// <summary>レリックが働いたことを、アイコンを弾ませて知らせる。</summary>
        public void Flash(RelicData relic)
        {
            if (relic != null && icons.TryGetValue(relic, out var slot) && isActiveAndEnabled)
            {
                StartCoroutine(UIAnim.Punch(slot, 0.3f, 0.35f));
            }
        }

        static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return new Color(0.55f, 0.75f, 1f);
                case Rarity.Rare: return new Color(1f, 0.82f, 0.3f);
                default: return new Color(0.9f, 0.88f, 0.8f);
            }
        }

        static string RarityHex(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return "#8FC0FF";
                case Rarity.Rare: return "#FFD24D";
                default: return "#F5EBD1";
            }
        }

        static string RarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return "アンコモン";
                case Rarity.Rare: return "レア";
                default: return "コモン";
            }
        }
    }
}
