using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ダイス一覧：ゲームに出てくるダイスを、札（6つの面つき）でまとめて見る。設定の画面から開く。
    /// 図鑑とちがって、まだ手に入れていないものも見せる（どんなダイスがあるか調べられるように）。札にマウスを乗せると説明が出る。
    /// </summary>
    public class DiceListView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color SubColor = new Color(0.73f, 0.67f, 0.56f);

        public event Action Closed;

        /// <summary>一覧に出すダイスと、どこで手に入るか。</summary>
        public static List<(DiceData dice, string where)> Entries(GameConfig config)
        {
            var list = new List<(DiceData, string)>();
            void Add(IEnumerable<DiceData> pool, string where)
            {
                if (pool == null) return;
                foreach (var d in pool)
                {
                    if (d == null || list.Any(x => x.Item1.id == d.id)) continue;
                    list.Add((d, where));
                }
            }
            Add(config.startingDice, "はじめから持っている");
            Add(config.starterChoices, "スターターで選べる");
            Add(config.rewardDicePool, "報酬・ショップ");
            Add(config.travelerDicePool, "旅人専用");
            Add(config.multiplayerDicePool, "みんなで遊ぶとき");
            Add(config.curseDicePool, "呪い（罠などで押し付けられる）");
            if (config.curseDice != null) Add(new[] { config.curseDice }, "呪い（罠などで押し付けられる）");
            // レア度の順（コモン → レア → 呪い）、同じレア度なら名前の順
            return list.OrderBy(x => x.Item1.rarity == Rarity.Curse ? 9 : (int)x.Item1.rarity).ThenBy(x => x.Item1.displayName).ToList();
        }

        public static DiceListView Create(Transform canvas, UIArt art, GameConfig config)
        {
            var root = UIFactory.Stretch("DiceListView", canvas);
            var view = root.gameObject.AddComponent<DiceListView>();
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.75f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", root, new Vector2(1680, 980), Vector2.zero, new Color(0.12f, 0.08f, 0.06f, 1f)).transform;
            var entries = Entries(config);
            UIFactory.Text("Title", box, $"ダイス一覧（{entries.Count}種）", 44, GoldColor, new Vector2(1000, 64), new Vector2(0, 430)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Note", box, "札にマウスを乗せると、説明と面の刻印が出ます。札の下は、どこで手に入るか。", 22, SubColor, new Vector2(1400, 34), new Vector2(0, 382));
            var close = UIFactory.Button("Close", box, new Vector2(200, 60), new Vector2(690, 430), new Color(0.93f, 0.87f, 0.72f), "閉じる", 28, out _);
            close.onClick.AddListener(() => view.Closed?.Invoke());

            // 縦にスクロールする枠
            const int columns = 5;
            const float cellW = 300f, cellH = 196f, gap = 14f, viewW = 1580f, viewH = 790f;
            var viewport = UIFactory.Rect("Viewport", box, new Vector2(viewW, viewH), new Vector2(0, -36));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            int rows = (entries.Count + columns - 1) / columns;
            float contentH = Mathf.Max(viewH, rows * (cellH + gap) + gap);
            var content = UIFactory.Rect("Content", viewport, new Vector2(viewW, contentH), Vector2.zero);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 50f;

            var seen = RunHistory.Seen(RunHistory.Category.Dice);
            for (int i = 0; i < entries.Count; i++)
            {
                var (dice, where) = entries[i];
                int col = i % columns, row = i / columns;
                var pos = new Vector2(-(columns - 1) * (cellW + gap) / 2f + col * (cellW + gap), contentH / 2f - gap - cellH / 2f - row * (cellH + gap));
                var card = DiceCard.Create($"Dice{i}", content, new DiceInstance(dice), art, new Vector2(cellW, cellH - 40f), pos + new Vector2(0, 20f),
                    $"<color=#7A5A3A>{RelicBar.RarityName(dice.rarity)}</color>", false, false);
                if (!seen.Contains(dice.displayName) && dice.rarity != Rarity.Curse) DiceCard.AddNewBadge(card.transform, new Vector2(cellW, cellH - 40f));
                UIFactory.Text("Where", content, where, 20, PaperColor, new Vector2(cellW, 30), pos + new Vector2(0, -cellH / 2f + 16f));
            }
            return view;
        }
    }
}
