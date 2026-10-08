using System;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>持っているお守り（最大3枠）。クリックで使う。使えない場面では暗くする（マップ・戦闘で共通）。</summary>
    public class CharmBar : MonoBehaviour
    {
        const float SlotSize = 56f;
        const float Gap = 8f;
        static readonly Color BackColor = new Color(0.08f, 0.05f, 0.04f, 0.75f);
        static readonly Color EmptyColor = new Color(0.3f, 0.25f, 0.2f, 0.5f);
        static readonly Color CharmColor = new Color(0.85f, 0.55f, 0.5f);
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);

        RectTransform slotsRoot;
        RectTransform tooltip;
        TextMeshProUGUI tooltipText;
        Func<CharmData, bool> usable;

        /// <summary>お守りがクリックされた（使えるときだけ）。</summary>
        public event Action<CharmData> Clicked;

        /// <summary>leftTop：バーの左上の位置（親の中心が原点）。</summary>
        public static CharmBar Create(Transform parent, Vector2 leftTop)
        {
            var root = UIFactory.Rect("CharmBar", parent, new Vector2(SlotSize, SlotSize), leftTop);
            root.pivot = new Vector2(0, 1);
            var bar = root.gameObject.AddComponent<CharmBar>();
            bar.slotsRoot = UIFactory.Rect("Slots", root, Vector2.zero, Vector2.zero);
            bar.slotsRoot.anchorMin = bar.slotsRoot.anchorMax = new Vector2(0, 1);

            bar.tooltip = UIFactory.Panel("Tooltip", root, new Vector2(420, 130), Vector2.zero, new Color(0.08f, 0.05f, 0.04f, 0.95f)).rectTransform;
            bar.tooltip.anchorMin = bar.tooltip.anchorMax = new Vector2(0, 1);
            bar.tooltip.pivot = new Vector2(0, 1);
            bar.tooltip.GetComponent<Image>().raycastTarget = false;
            bar.tooltipText = UIFactory.Text("Text", bar.tooltip, "", 24, PaperColor, new Vector2(396, 120), Vector2.zero, TextAlignmentOptions.TopLeft);
            bar.tooltip.gameObject.SetActive(false);
            return bar;
        }

        /// <summary>持っているお守りに合わせて並べ直す。usable が true のお守りだけクリックで使える。</summary>
        public void Refresh(RunState run, Func<CharmData, bool> usable)
        {
            this.usable = usable;
            UIFactory.ClearChildren(slotsRoot);
            tooltip.gameObject.SetActive(false);
            for (int i = 0; i < RunState.MaxCharms; i++)
            {
                var pos = new Vector2(SlotSize / 2 + i * (SlotSize + Gap), -SlotSize / 2);
                if (i >= run.Charms.Count)
                {
                    var empty = UIFactory.Panel($"Empty{i}", slotsRoot, new Vector2(SlotSize, SlotSize), pos, EmptyColor);
                    empty.raycastTarget = false;
                    continue;
                }
                var charm = run.Charms[i];
                bool canUse = usable != null && usable(charm);
                var button = UIFactory.Button($"Charm_{i}", slotsRoot, new Vector2(SlotSize, SlotSize), pos, BackColor, "", 20, out _);
                var icon = UIFactory.Picture("Icon", button.transform, charm.icon, new Vector2(SlotSize - 6, SlotSize - 6), Vector2.zero, canUse ? CharmColor : CharmColor * 0.5f);
                icon.raycastTarget = false;
                if (charm.icon == null)
                {
                    var t = UIFactory.Text("Name", icon.transform, charm.displayName.Substring(0, 1), 28, canUse ? Color.black : new Color(0.2f, 0.2f, 0.2f), new Vector2(SlotSize, SlotSize), Vector2.zero);
                    t.raycastTarget = false;
                }
                button.interactable = canUse;
                button.onClick.AddListener(() =>
                {
                    tooltip.gameObject.SetActive(false);
                    Clicked?.Invoke(charm);
                });
                var hover = button.gameObject.AddComponent<HoverRelay>();
                int index = i;
                hover.Entered += () => ShowTooltip(charm, index, canUse);
                hover.Exited += () => tooltip.gameObject.SetActive(false);
            }
        }

        void ShowTooltip(CharmData charm, int index, bool canUse)
        {
            tooltipText.text = $"<b><color=#F2A99E>{charm.displayName}</color></b>　<size=20>お守り</size>\n{charm.description}\n"
                + (canUse ? "<color=#FFD24D>クリックで使う</color>" : $"<color=#A0A0A0>{WhenUsable(charm)}</color>");
            // 画面の右端に置くので、説明は左へ寄せて出す
            tooltip.anchoredPosition = new Vector2(-230, -SlotSize - 8);
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
        }

        /// <summary>使えないときに見せる、使える場面の説明。</summary>
        static string WhenUsable(CharmData charm)
        {
            switch (charm.kind)
            {
                case CharmKind.MoveForward:
                case CharmKind.MoveBack: return "移動でダイスを振ったあと、進む前に使える";
                case CharmKind.RerollDie: return "ダイスを振ったあとに使える（移動・戦闘）";
                case CharmKind.Smoke: return "通常戦の最中に使える";
                case CharmKind.Heal: return "HP が減っているときに使える";
                case CharmKind.Unseal: return "封印されたダイスがあるときに使える";
                case CharmKind.ReturnUsed: return "使用済みのダイスがあるときに使える";
                case CharmKind.WeakenEnemy:
                case CharmKind.VulnerableEnemy:
                case CharmKind.PoisonEnemy: return "戦闘中に、狙っている敵に投げる";
                default: return "今は使えない";
            }
        }
    }
}
