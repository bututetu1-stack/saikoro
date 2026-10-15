using System;
using System.Collections.Generic;
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

        TextMeshProUGUI bestLabel;

        /// <summary>上の行（踏破のターン数・自己ベスト・対戦の順位）を書き直す（通信の対戦で、あとから相手が踏破したときなど）。</summary>
        public void SetBestText(string text)
        {
            if (bestLabel != null) bestLabel.text = text;
        }

        /// <param name="bestText">踏破したときの、ターン数と自己ベストの表示（なければ null）。</param>
        public static ResultView Create(Transform canvas, UIArt art, bool cleared, RunState run, string bestText = null)
        {
            var root = UIFactory.Stretch("ResultView", canvas);
            var view = root.gameObject.AddComponent<ResultView>();

            UIFactory.Background(root, art != null ? (cleared ? art.mapBackground : art.battleBackground) : null, new Color(0.2f, 0.15f, 0.12f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.45f));

            string titleText = cleared
                ? (run.LayerCount > 1 ? "賽ノ道 踏破！" : $"第{run.LayerIndex + 1}層 踏破！")
                : $"第{run.LayerIndex + 1}層「{run.Layer.displayName}」で力尽きた……";
            var title = UIFactory.Text("Title", root, titleText, cleared ? 88 : 64,
                cleared ? GoldColor : new Color(0.95f, 0.5f, 0.45f), new Vector2(1400, 120), new Vector2(0, 410));
            title.fontStyle = FontStyles.Bold;
            title.outlineWidth = 0.11f;
            title.outlineColor = new Color32(30, 15, 5, 255);
            view.StartCoroutine(UIAnim.Punch(title.transform, 0.15f, 0.4f));
            if (!string.IsNullOrEmpty(bestText))
            {
                var best = UIFactory.Text("Best", root, bestText, 34, PaperColor, new Vector2(1400, 50), new Vector2(0, 325));
                best.fontStyle = FontStyles.Bold;
                best.outlineWidth = 0.11f;
                best.outlineColor = new Color32(30, 15, 5, 255);
                best.enableAutoSizing = true;
                best.fontSizeMax = 34;
                best.fontSizeMin = 18;
                view.bestLabel = best;
            }

            var s = run.stats;
            // 左：数字のまとめ
            var left = UIFactory.Panel("Summary", root, new Vector2(760, 560), new Vector2(-420, 20), ShadeColor);
            string stops = string.Join("　", s.tilesStopped.OrderByDescending(kv => kv.Value).Select(kv => $"{TileName(kv.Key)} {kv.Value}"));
            var numbers = UIFactory.Text("Numbers", left.transform,
                $"<color=#FFD24D>到達</color>　第{run.LayerIndex + 1}層 / 全{run.LayerCount}層\n" +
                $"<color=#FFD24D>ターン</color>　{run.Turn}\n" +
                $"<color=#FFD24D>残りHP</color>　{run.player.hp} / {run.player.maxHp}\n" +
                $"<color=#FFD24D>ゴールド</color>　所持 {run.Gold} G（稼いだ合計 {s.goldEarned} G）\n" +
                $"<color=#FFD24D>勝った戦闘</color>　{s.battlesWon} 回（エリート {s.elitesWon}・ボス {s.bossesWon}）\n" +
                $"<color=#FFD24D>ダメージ合計</color>　与えた {s.damageDealt}・受けた {s.damageTaken}\n" +
                $"<color=#FFD24D>止まったマス</color>\n<size=87%>{(stops.Length > 0 ? stops : "なし")}</size>\n" +
                (cleared ? "" : $"<color=#FFD24D>ボスまで</color>　あと {run.TilesToGoal} マス\n") +
                $"<size=73%><color=#BBAA90>シード {run.random.Seed}</color></size>",
                30, PaperColor, new Vector2(660, 330), new Vector2(0, 88), TextAlignmentOptions.TopLeft); // 枠の角の飾りの内側
            numbers.enableAutoSizing = true;
            numbers.fontSizeMax = 30;
            numbers.fontSizeMin = 14;
            DrawHpGraph(left.transform, run, new Vector2(660, 140), new Vector2(0, -182));

            // 右：持ち物
            var right = UIFactory.Panel("Items", root, new Vector2(760, 560), new Vector2(420, 20), ShadeColor);
            string dice = RunStats.Grouped(run.pouch.All.Select(d => d.DisplayName));
            string relics = run.Relics.Count > 0 ? string.Join("・", run.Relics.Select(r => r.displayName)) : "なし";
            // 刻印はどのダイスかは省いて、刻印の種類ごとに数える
            string engravings = s.engravings.Count > 0 ? RunStats.Grouped(s.EngravingNames) : "なし";
            string removed = s.diceRemoved.Count > 0 ? RunStats.Grouped(s.diceRemoved) : "なし";
            var itemsText = UIFactory.Text("ItemsText", right.transform,
                $"<color=#FFD24D>ポーチ</color>\n<size=87%>{dice}</size>\n" +
                $"<color=#FFD24D>レリック</color>\n<size=87%>{relics}</size>\n" +
                $"<color=#FFD24D>刻印</color>\n<size=87%>{engravings}</size>\n" +
                $"<color=#FFD24D>削除したダイス</color>\n<size=87%>{removed}</size>",
                30, PaperColor, new Vector2(660, 420), new Vector2(0, 42), TextAlignmentOptions.TopLeft); // 枠の角の飾りの内側
            // 刻印やレリックが多いと枠からはみ出していたので、入りきらなければ文字を小さくする
            itemsText.enableAutoSizing = true;
            itemsText.fontSizeMax = 30;
            itemsText.fontSizeMin = 14;

            // レリックのアイコン（枠の下に1列。入りきらなければ縮める）
            int relicCount = run.Relics.Count;
            float iconStep = relicCount > 0 ? Mathf.Min(60f, 640f / relicCount) : 60f; // 枠の角の飾りの内側
            for (int i = 0; i < relicCount; i++)
            {
                UIFactory.Picture($"Relic{i}", right.transform, run.Relics[i].icon, new Vector2(iconStep - 8, iconStep - 8),
                    new Vector2(-320 + iconStep / 2 + i * iconStep, -228), GoldColor);
            }

            var retry = UIFactory.Button("RetryButton", root, new Vector2(420, 96), new Vector2(0, -400),
                new Color(1f, 0.78f, 0.3f), "もう一度遊ぶ", 36, out _);
            retry.onClick.AddListener(() => view.RetryClicked?.Invoke());

            // 結果のまとめをコピー（試遊の感想と一緒に送ってもらう。シードで同じ盤面を再現できる）
            string summary = run.ShareSummary(cleared, Application.version);
            var copyNote = UIFactory.Text("CopyNote", root, "感想を送るときは「結果をコピー」して貼り付けてください", 24, PaperColor, new Vector2(1500, 40), new Vector2(0, -300));
            var copy = UIFactory.Button("CopyButton", root, new Vector2(340, 96), new Vector2(-420, -400),
                new Color(0.93f, 0.87f, 0.72f), "結果をコピー", 32, out _);
            copy.onClick.AddListener(() =>
            {
                bool ok = Clipboard.Copy(summary);
                copyNote.text = ok ? "<color=#FFD24D>コピーしました。</color>感想と一緒に貼り付けて送ってください"
                    : "<color=#FF8A6A>コピーできませんでした。</color>この画面を撮って送ってください";
            });
            return view;
        }

        /// <summary>「始めの画面へ」（ひとりの旅では「もう一度遊ぶ」がすぐスターター選びになるので、始めの画面へ戻る道も出す）。</summary>
        public event Action TitleClicked;

        public void ShowTitleButton()
        {
            var title = UIFactory.Button("TitleButton", transform, new Vector2(340, 96), new Vector2(420, -400),
                new Color(0.93f, 0.87f, 0.72f), "始めの画面へ", 32, out _);
            title.onClick.AddListener(() => TitleClicked?.Invoke());
        }

        /// <summary>観戦する（通信の対戦で、まだ走っている相手の旅を見る）。</summary>
        public event Action SpectateClicked;
        UnityEngine.UI.Button spectateButton;

        /// <summary>「観戦する」ボタンを出す・隠す（まだ走っている相手がいるときだけ出す）。</summary>
        public void ShowSpectateButton(bool show)
        {
            if (spectateButton == null)
            {
                if (!show) return;
                spectateButton = UIFactory.Button("SpectateButton", transform, new Vector2(340, 96), new Vector2(420, -400),
                    new Color(1f, 0.6f, 0.45f), "観戦する", 32, out _);
                spectateButton.onClick.AddListener(() => SpectateClicked?.Invoke());
            }
            spectateButton.gameObject.SetActive(show);
        }

        // 層ごとの棒の色（第1層・第2層・第3層）
        static readonly Color[] LayerColors = { new Color(0.55f, 0.8f, 0.45f), new Color(0.45f, 0.65f, 0.9f), new Color(0.72f, 0.5f, 0.9f) };

        /// <summary>HP の推移（ターンごとの棒。高さは HP の割合、色は層）。</summary>
        static void DrawHpGraph(Transform parent, RunState run, Vector2 size, Vector2 position)
        {
            var s = run.stats;
            var hp = new List<int>(s.hpByTurn) { Mathf.Max(0, run.player.hp) };   // 最後に、終わったときの HP を足す
            var maxHp = new List<int>(s.maxHpByTurn) { run.player.maxHp };
            var layers = new List<int>(s.layerByTurn) { run.LayerIndex };
            var area = UIFactory.Rect("HpGraph", parent, size, position);
            UIFactory.Panel("Back", area, size, Vector2.zero, new Color(0, 0, 0, 0.3f)).raycastTarget = false;
            UIFactory.Text("Label", area, "HP の推移", 20, new Color(1f, 0.82f, 0.3f), new Vector2(200, 26), new Vector2(-size.x / 2f + 100f, size.y / 2f + 14f), TextAlignmentOptions.Left);
            if (hp.Count < 2) return;
            float inner = size.y - 12f;
            float step = (size.x - 12f) / hp.Count;
            for (int i = 0; i < hp.Count; i++)
            {
                float ratio = maxHp[i] > 0 ? Mathf.Clamp01((float)hp[i] / maxHp[i]) : 0f;
                float h = Mathf.Max(2f, inner * ratio);
                var color = LayerColors[Mathf.Clamp(layers[i], 0, LayerColors.Length - 1)];
                // 半分を切ったら暗く、4分の1を切ったら赤く（危なかったところがわかるように）
                if (ratio < 0.25f) color = new Color(0.95f, 0.3f, 0.25f);
                else if (ratio < 0.5f) color *= 0.75f;
                var bar = UIFactory.Panel($"Bar{i}", area, new Vector2(Mathf.Max(1f, step - 1f), h),
                    new Vector2(-size.x / 2f + 6f + step * (i + 0.5f), -size.y / 2f + 6f + h / 2f), new Color(color.r, color.g, color.b, 1f));
                bar.raycastTarget = false;
            }
        }

        public static string TileName(TileType type)
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
