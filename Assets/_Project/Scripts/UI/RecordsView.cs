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
    /// 記録：ラン履歴と図鑑（出会ったダイス・レリック・お守り・イベント・敵。まだ会っていないものは「？？？」）。始めの画面から開く。
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

        static readonly string[] TabNames = { "ラン履歴", "ダイス", "レリック", "お守り", "イベント", "敵", "実績" };

        public static RecordsView Create(Transform canvas, UIArt art, GameConfig config)
        {
            var root = UIFactory.Stretch("RecordsView", canvas);
            var view = root.gameObject.AddComponent<RecordsView>();
            view.config = config;
            view.art = art;
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.9f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", root, new Vector2(1500, 900), new Vector2(0, 0), new Color(0.12f, 0.08f, 0.06f, 1f));
            UIFactory.Text("Title", box.transform, "記録", 48, GoldColor, new Vector2(400, 70), new Vector2(-640, 395)).fontStyle = FontStyles.Bold;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = UIFactory.Button($"Tab{i}", box.transform, new Vector2(140, 56), new Vector2(-480 + i * 150, 395), ButtonColor, TabNames[i], 26, out _); // 7つ並べても「閉じる」と重ならない幅
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
                case 1: ShowCollection(RunHistory.Category.Dice, AllDice()); break;
                case 2: ShowCollection(RunHistory.Category.Relic, AllRelics()); break;
                case 3: ShowCollection(RunHistory.Category.Charm, AllCharms()); break;
                case 4: ShowCollection(RunHistory.Category.Event, AllEvents(), new Vector2(186, 124)); break; // 挿絵は 3:2
                case 5: ShowCollection(RunHistory.Category.Enemy, AllEnemies()); break;
                default: ShowAchievementList(); break;
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
                string line = $"<color=#B8AC98>{e.date}</color>　{e.mode}　{result}" + (e.score > 0 ? $"　得点 {e.score:N0}" : "") + (string.IsNullOrEmpty(e.starter) ? "" : $"　<size=85%>{e.starter}</size>");
                var t = UIFactory.Text($"Entry{i}", body, line, 22, PaperColor, new Vector2(700, 40), new Vector2(col == 0 ? -360 : 360, 300 - row * 44), TextAlignmentOptions.Left);
                t.enableAutoSizing = true;
                t.fontSizeMax = 22;
                t.fontSizeMin = 14;
            }
        }


        // ---- 実績 ----

        void ShowAchievementList()
        {
            var all = Achievements.All;
            UIFactory.Text("Summary", body, $"取った実績　{Achievements.UnlockedCount} / {all.Length}", 28, GoldColor, new Vector2(1400, 44), new Vector2(0, 360));
            int rows = (all.Length + 1) / 2;
            // 実績が増えても枠に収まるように、行の高さを数に合わせて詰める
            const float w = 690f;
            float step = Mathf.Min(86f, 620f / Mathf.Max(1, rows - 1));
            float h = step - 8f;
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                bool got = Achievements.IsUnlocked(a.id);
                int col = i / rows, row = i % rows;
                var cell = UIFactory.Panel($"Achievement{i}", body, new Vector2(w, h), new Vector2(col == 0 ? -360 : 360, 290 - row * step),
                    got ? new Color(0.93f, 0.87f, 0.72f, 0.18f) : new Color(0, 0, 0, 0.35f));
                cell.raycastTarget = false;
                var mark = UIFactory.Text("Mark", cell.transform, got ? "済" : "・", 34, got ? GoldColor : new Color(0.45f, 0.4f, 0.35f), new Vector2(60, h), new Vector2(-w / 2f + 40, 0));
                mark.fontStyle = FontStyles.Bold;
                var t = UIFactory.Text("Text", cell.transform, $"<b>{a.name}</b>　<size=80%>{a.description}</size>", 26,
                    got ? GoldColor : new Color(0.7f, 0.65f, 0.58f), new Vector2(w - 100, h - 6), new Vector2(30, 0), TextAlignmentOptions.Left);
                t.enableAutoSizing = true;
                t.fontSizeMax = 26;
                t.fontSizeMin = 16;
            }
        }

        // ---- 図鑑（Slay the Spire の図鑑風：絵を並べ、まだ会っていないものは影だけ。マウスを乗せると説明） ----

        struct Entry
        {
            public string name;
            public Sprite icon;
            public int[] faces;     // ダイス（絵の代わりに面の値を出す）
            public string detail;
            public Color nameColor;
        }

        TextMeshProUGUI detailText;

        void ShowCollection(RunHistory.Category category, List<Entry> entries, Vector2? iconSize = null)
        {
            var seen = RunHistory.Seen(category);
            int found = entries.Count(e => seen.Contains(e.name));
            UIFactory.Text("Summary", body, $"出会った数　{found} / {entries.Count}", 28, GoldColor, new Vector2(1400, 44), new Vector2(0, 360));

            // 縦にスクロールする枠
            var viewport = UIFactory.Rect("Viewport", body, new Vector2(1440, 590), new Vector2(0, 45));
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            const int columns = 6;
            const float cellW = 220f, cellH = 210f, gap = 14f;
            int rows = (entries.Count + columns - 1) / columns;
            float contentH = Mathf.Max(590f, rows * (cellH + gap) + gap);
            var content = UIFactory.Rect("Content", viewport, new Vector2(1440, contentH), Vector2.zero);
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 50f;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bool known = seen.Contains(e.name);
                int col = i % columns, row = i / columns;
                var pos = new Vector2(-(columns - 1) * (cellW + gap) / 2f + col * (cellW + gap), contentH / 2f - gap - cellH / 2f - row * (cellH + gap)); // 中身の真ん中からの位置
                var cell = UIFactory.Panel($"Cell{i}", content, new Vector2(cellW, cellH), pos, known ? new Color(0.93f, 0.87f, 0.72f, 0.12f) : new Color(0, 0, 0, 0.35f));
                cell.raycastTarget = true; // マウスを乗せたときの説明のため
                if (e.icon != null)
                {
                    var icon = UIFactory.Picture("Icon", cell.transform, e.icon, iconSize ?? new Vector2(140, 140), new Vector2(0, 22));
                    // まだ会っていないものは影だけ
                    if (!known)
                    {
                        // 絵の形で切り抜いた、ただの色の板（形だけ見せて中身は見せない）
                        icon.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                        var fill = UIFactory.Stretch("Silhouette", icon.transform).gameObject.AddComponent<Image>();
                        fill.color = new Color(0.32f, 0.25f, 0.2f, 1f);
                        fill.raycastTarget = false;
                    }
                    icon.raycastTarget = false;
                }
                else if (e.faces != null)
                {
                    // 全角の「？」を空白で並べると幅が足りず2行に折り返していたので、半角で詰める（知っているダイスの目と同じ並び方）
                    string faces = !known ? "? ? ? ? ? ?"
                        : e.faces.Length > 8 ? $"{DiceCard.FaceCountName(e.faces.Length)}\n{e.faces.Min()}〜{e.faces.Max()}" // 十二面・二十面は並べると収まらない
                        : string.Join(" ", e.faces);
                    var f = UIFactory.Text("Faces", cell.transform, faces, 34, known ? Color.white : new Color(0.4f, 0.36f, 0.32f), new Vector2(cellW - 16, 120), new Vector2(0, 22));
                    f.fontStyle = FontStyles.Bold;
                }
                var t = UIFactory.Text("Name", cell.transform, known ? e.name : "？？？", 24, known ? e.nameColor : new Color(0.5f, 0.45f, 0.4f), new Vector2(cellW - 12, 44), new Vector2(0, -cellH / 2f + 26));
                t.enableAutoSizing = true;
                t.fontSizeMax = 24;
                t.fontSizeMin = 14;
                var hover = cell.gameObject.AddComponent<HoverRelay>();
                string detail = known ? $"<b><color=#FFD24D>{e.name}</color></b>　{e.detail}" : "まだ出会っていない。";
                hover.Entered += () => { if (detailText != null) detailText.text = detail; };
            }

            // 下の説明の欄
            var box = UIFactory.Panel("Detail", body, new Vector2(1440, 120), new Vector2(0, -320), new Color(0.06f, 0.04f, 0.03f, 0.95f));
            box.raycastTarget = false;
            detailText = UIFactory.Text("Text", box.transform, "絵にマウスを乗せると説明が出ます。", 24, PaperColor, new Vector2(1360, 100), Vector2.zero, TextAlignmentOptions.Left); // 角の飾りの内側
            detailText.enableAutoSizing = true;
            detailText.fontSizeMax = 24;
            detailText.fontSizeMin = 16;
        }

        // ---- 図鑑に載るもの（データの一覧から。同じ名前は1つにまとめる） ----

        List<Entry> AllDice() => config.startingDice.Concat(config.starterChoices).Concat(config.rewardDicePool).Concat(config.eventDicePool).Concat(config.multiplayerDicePool).Concat(config.curseDicePool)
            .Append(config.curseDice).Where(d => d != null).GroupBy(d => d.displayName).Select(g => g.First())
            .Select(d => new Entry
            {
                name = d.displayName,
                faces = d.faceValues,
                detail = $"<size=85%><color=#B8AC98>{RelicBar.RarityName(d.rarity)}</color></size>　{d.description}",
                nameColor = HexColor(RelicBar.RarityHex(d.rarity)),
            }).ToList();

        List<Entry> AllRelics() => config.relicPool.Concat(config.bossRelicPool).Concat(config.ancientRelicPool).Concat(config.multiplayerRelicPool).Concat(config.tourRelicPool)
            .Where(r => r != null).GroupBy(r => r.displayName).Select(g => g.First())
            .Select(r => new Entry
            {
                name = r.displayName,
                icon = r.icon,
                detail = $"<size=85%><color=#B8AC98>{RelicBar.RarityName(r.rarity)}</color></size>　{r.description}",
                nameColor = HexColor(RelicBar.RarityHex(r.rarity)),
            }).ToList();

        List<Entry> AllCharms() => config.charmPool.Concat(config.multiplayerCharmPool)
            .Where(c => c != null).GroupBy(c => c.displayName).Select(g => g.First())
            .Select(c => new Entry { name = c.displayName, icon = c.icon, detail = $"{c.description}　<size=85%><color=#B8AC98>{c.price} G</color></size>", nameColor = PaperColor })
            .ToList();

        /// <summary>イベント：挿絵と、1ランに何回出るか。</summary>
        List<Entry> AllEvents() => ((Run.EventKind[])Enum.GetValues(typeof(Run.EventKind)))
            .Select(k => new Entry
            {
                name = Run.RunState.EventName(k),
                icon = art != null ? art.EventSpriteFor(k) : null,
                detail = $"<size=85%><color=#B8AC98>{(config.events.oncePerRun.Contains(k) ? "1回の旅に1度だけ" : "何度でも出る")}</color></size>",
                nameColor = PaperColor,
            })
            .Where(e => !string.IsNullOrEmpty(e.name)).GroupBy(e => e.name).Select(g => g.First()).ToList();

        List<Entry> AllEnemies()
        {
            var enemies = config.battleEnemies.Concat(config.eliteEnemies);
            foreach (var layer in config.layers.Where(l => l != null))
                enemies = enemies.Concat(layer.battleEnemies).Concat(layer.eliteEnemies).Concat(layer.bossChoices);
            return enemies.Where(e => e != null).GroupBy(e => e.displayName).Select(g => g.First())
                .Select(e => new Entry
                {
                    name = e.displayName,
                    icon = art != null ? art.EnemySpriteFor(e) : null,
                    detail = $"<size=85%><color=#B8AC98>{KindName(e.kind)}　HP {e.maxHp}{(e.count > 1 ? $"×{e.count}体" : "")}</color></size>　"
                        + string.Join(" → ", e.pattern.Select(p => SaiNoMichi.UI.BattleView.IntentLabel(p, 0)))
                        + (string.IsNullOrEmpty(e.lore) ? "" : $"\n<size=90%><color=#D8C8A8>{e.lore}</color></size>"),
                    nameColor = e.kind == Battle.EnemyKind.Boss ? new Color(1f, 0.55f, 0.45f) : e.kind == Battle.EnemyKind.Elite ? GoldColor : PaperColor,
                }).ToList();
        }

        static string KindName(Battle.EnemyKind kind) => kind == Battle.EnemyKind.Boss ? "ボス" : kind == Battle.EnemyKind.Elite ? "強敵" : "通常";

        static Color HexColor(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : PaperColor;
    }
}
