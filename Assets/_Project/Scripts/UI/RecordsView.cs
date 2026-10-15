using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 記録：ラン履歴と図鑑（出会ったダイス・レリック・敵。まだ会っていないものは「？？？」）。始めの画面から開く。
    /// </summary>
    public class RecordsView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color ChosenColor = new Color(1f, 0.78f, 0.3f);

        public event Action Closed;

        GameConfig config;
        UIArt art;
        RectTransform body;
        readonly List<Button> tabs = new List<Button>();

        static readonly string[] TabNames = { "ラン履歴", "ダイス", "レリック", "敵" };

        public static RecordsView Create(Transform canvas, UIArt art, GameConfig config)
        {
            var root = UIFactory.Stretch("RecordsView", canvas);
            var view = root.gameObject.AddComponent<RecordsView>();
            view.config = config;
            view.art = art;
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.9f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", root, new Vector2(1500, 900), new Vector2(0, 0), new Color(0.12f, 0.08f, 0.06f, 1f));
            UIFactory.Text("Title", box.transform, "記録", 48, GoldColor, new Vector2(400, 70), new Vector2(-600, 395)).fontStyle = FontStyles.Bold;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = UIFactory.Button($"Tab{i}", box.transform, new Vector2(200, 56), new Vector2(-330 + i * 215, 395), ButtonColor, TabNames[i], 26, out _);
                tab.onClick.AddListener(() => view.Show(index));
                view.tabs.Add(tab);
            }
            var close = UIFactory.Button("Close", box.transform, new Vector2(160, 56), new Vector2(640, 395), ButtonColor, "閉じる", 26, out _);
            close.onClick.AddListener(() => view.Closed?.Invoke());
            view.body = UIFactory.Rect("Body", box.transform, new Vector2(1440, 780), new Vector2(0, -40));
            view.Show(0);
            return view;
        }

        void Show(int tab)
        {
            for (int i = 0; i < tabs.Count; i++) tabs[i].GetComponent<Image>().color = i == tab ? ChosenColor : ButtonColor;
            UIFactory.ClearChildren(body);
            switch (tab)
            {
                case 0: ShowHistory(); break;
                case 1: ShowCollection(RunHistory.Category.Dice, AllDiceNames()); break;
                case 2: ShowCollection(RunHistory.Category.Relic, AllRelicNames()); break;
                default: ShowCollection(RunHistory.Category.Enemy, AllEnemyNames()); break;
            }
        }

        void ShowHistory()
        {
            var entries = RunHistory.Entries;
            int cleared = entries.Count(e => e.cleared);
            string best = BestRecord.BestTurns > 0 ? $"自己ベスト {BestRecord.BestTurns} ターン" : "まだ踏破していない";
            UIFactory.Text("Summary", body, $"最近 {entries.Count} 回の旅：踏破 {cleared} 回　{best}", 28, GoldColor, new Vector2(1400, 44), new Vector2(0, 360));
            if (entries.Count == 0)
            {
                UIFactory.Text("Empty", body, "まだ記録がありません。旅を終えると、ここに残ります。", 28, PaperColor, new Vector2(1400, 60), new Vector2(0, 200));
                return;
            }
            // 新しい順に、1行に1回（多いときは2列）
            int perColumn = 15;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int col = i / perColumn, row = i % perColumn;
                string result = e.cleared ? $"<color=#FFD24D>踏破 {e.turns}ターン</color>" : $"第{e.layer}層で倒れた（{e.turns}ターン{(string.IsNullOrEmpty(e.cause) ? "" : "・" + e.cause)}）";
                string line = $"<color=#B8AC98>{e.date}</color>　{e.mode}　{result}" + (string.IsNullOrEmpty(e.starter) ? "" : $"　<size=85%>{e.starter}</size>");
                var t = UIFactory.Text($"Entry{i}", body, line, 22, PaperColor, new Vector2(700, 40), new Vector2(col == 0 ? -360 : 360, 300 - row * 44), TextAlignmentOptions.Left);
                t.enableAutoSizing = true;
                t.fontSizeMax = 22;
                t.fontSizeMin = 14;
            }
        }

        void ShowCollection(RunHistory.Category category, List<string> names)
        {
            var seen = RunHistory.Seen(category);
            int found = names.Count(seen.Contains);
            UIFactory.Text("Summary", body, $"出会った数　{found} / {names.Count}", 28, GoldColor, new Vector2(1400, 44), new Vector2(0, 360));
            // 5列で並べる。まだ会っていないものは「？？？」
            const int columns = 5;
            const float w = 270f, h = 50f;
            for (int i = 0; i < names.Count; i++)
            {
                int col = i % columns, row = i / columns;
                bool known = seen.Contains(names[i]);
                var pos = new Vector2(-(columns - 1) * (w + 10) / 2f + col * (w + 10), 300 - row * (h + 8));
                var cell = UIFactory.Panel($"Cell{i}", body, new Vector2(w, h), pos, known ? new Color(0.93f, 0.87f, 0.72f, 0.15f) : new Color(0, 0, 0, 0.3f));
                cell.raycastTarget = false;
                var t = UIFactory.Text("Name", cell.transform, known ? names[i] : "？？？", 24, known ? PaperColor : new Color(0.5f, 0.45f, 0.4f), new Vector2(w - 12, h - 6), Vector2.zero);
                t.enableAutoSizing = true;
                t.fontSizeMax = 24;
                t.fontSizeMin = 14;
            }
        }

        // ---- 図鑑に載るもの（データの一覧から。同じ名前は1つにまとめる） ----

        List<string> AllDiceNames() => config.startingDice.Concat(config.starterChoices).Concat(config.rewardDicePool).Concat(config.curseDicePool)
            .Append(config.curseDice).Where(d => d != null).Select(d => d.displayName).Distinct().ToList();

        List<string> AllRelicNames() => config.relicPool.Concat(config.bossRelicPool)
            .Where(r => r != null).Select(r => r.displayName).Distinct().ToList();

        List<string> AllEnemyNames()
        {
            var enemies = config.battleEnemies.Concat(config.eliteEnemies);
            foreach (var layer in config.layers.Where(l => l != null))
                enemies = enemies.Concat(layer.battleEnemies).Concat(layer.eliteEnemies).Concat(layer.bossChoices);
            return enemies.Where(e => e != null).Select(e => e.displayName).Distinct().ToList();
        }
    }
}
