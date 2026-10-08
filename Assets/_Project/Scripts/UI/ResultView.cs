using System;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>結果画面。クリア（フェーズ1は第1層のボスを倒したら）かゲームオーバーかと、旅のまとめを表示する。</summary>
    public class ResultView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.88f);

        public event Action RetryClicked;

        public static ResultView Create(Transform canvas, UIArt art, bool cleared, RunState run)
        {
            var root = UIFactory.Stretch("ResultView", canvas);
            var view = root.gameObject.AddComponent<ResultView>();

            UIFactory.Background(root, art != null ? (cleared ? art.mapBackground : art.battleBackground) : null, new Color(0.2f, 0.15f, 0.12f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.45f));

            var title = UIFactory.Text("Title", root, cleared ? "第1層 踏破！" : "旅はここまで……", 88,
                cleared ? GoldColor : new Color(0.95f, 0.5f, 0.45f), new Vector2(1400, 120), new Vector2(0, 410));
            title.fontStyle = FontStyles.Bold;
            title.outlineWidth = 0.2f;
            title.outlineColor = new Color32(30, 15, 5, 255);
            view.StartCoroutine(UIAnim.Punch(title.transform, 0.15f, 0.4f));

            var s = run.stats;
            // 左：数字のまとめ
            var left = UIFactory.Panel("Summary", root, new Vector2(760, 560), new Vector2(-420, 20), ShadeColor);
            string stops = string.Join("　", s.tilesStopped.OrderByDescending(kv => kv.Value).Select(kv => $"{TileName(kv.Key)} {kv.Value}"));
            UIFactory.Text("Numbers", left.transform,
                $"<color=#FFD24D>ターン</color>　{run.Turn}\n" +
                $"<color=#FFD24D>残りHP</color>　{run.player.hp} / {run.player.maxHp}\n" +
                $"<color=#FFD24D>ゴールド</color>　所持 {run.Gold} G（稼いだ合計 {s.goldEarned} G）\n" +
                $"<color=#FFD24D>勝った戦闘</color>　{s.battlesWon} 回（エリート {s.elitesWon}・ボス {s.bossesWon}）\n" +
                $"<color=#FFD24D>止まったマス</color>\n<size=26>{(stops.Length > 0 ? stops : "なし")}</size>\n" +
                (cleared ? "" : $"<color=#FFD24D>ボスまで</color>　あと {run.TilesToGoal} マス\n") +
                $"<size=22><color=#BBAA90>シード {run.random.Seed}</color></size>",
                30, PaperColor, new Vector2(700, 520), Vector2.zero, TextAlignmentOptions.TopLeft);

            // 右：持ち物
            var right = UIFactory.Panel("Items", root, new Vector2(760, 560), new Vector2(420, 20), ShadeColor);
            string dice = string.Join("・", run.pouch.All.Select(d => d.DisplayName));
            string relics = run.Relics.Count > 0 ? string.Join("・", run.Relics.Select(r => r.displayName)) : "なし";
            string engravings = s.engravings.Count > 0 ? string.Join("・", s.engravings) : "なし";
            string removed = s.diceRemoved.Count > 0 ? string.Join("・", s.diceRemoved) : "なし";
            UIFactory.Text("ItemsText", right.transform,
                $"<color=#FFD24D>ポーチ</color>\n<size=26>{dice}</size>\n" +
                $"<color=#FFD24D>レリック</color>\n<size=26>{relics}</size>\n" +
                $"<color=#FFD24D>刻印</color>\n<size=26>{engravings}</size>\n" +
                $"<color=#FFD24D>削除したダイス</color>\n<size=26>{removed}</size>",
                30, PaperColor, new Vector2(700, 520), Vector2.zero, TextAlignmentOptions.TopLeft);

            // レリックのアイコン
            for (int i = 0; i < run.Relics.Count && i < 10; i++)
            {
                UIFactory.Picture($"Relic{i}", right.transform, run.Relics[i].icon, new Vector2(56, 56), new Vector2(-340 + 28 + i * 64, -240), GoldColor);
            }

            var retry = UIFactory.Button("RetryButton", root, new Vector2(420, 96), new Vector2(0, -400),
                new Color(1f, 0.78f, 0.3f), "もう一度遊ぶ", 36, out _);
            retry.onClick.AddListener(() => view.RetryClicked?.Invoke());
            return view;
        }

        static string TileName(TileType type)
        {
            switch (type)
            {
                case TileType.Battle: return "戦闘";
                case TileType.Elite: return "エリート";
                case TileType.Boss: return "ボス";
                case TileType.Event: return "イベント";
                case TileType.Trap: return "罠";
                case TileType.Rest: return "休憩";
                case TileType.Treasure: return "宝箱";
                case TileType.Shop: return "ショップ";
                case TileType.Forge: return "鍛冶";
                case TileType.Shrine: return "祠";
                case TileType.Checkpoint: return "関所";
                case TileType.Teahouse: return "茶屋";
                case TileType.DiceHall: return "賽場";
                default: return "空白";
            }
        }
    }
}
