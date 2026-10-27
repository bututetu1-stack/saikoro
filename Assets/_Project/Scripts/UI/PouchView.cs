using System;
using System.Linq;
using System.Text;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ポーチの中身を詳しく見る小窓（STS の山札一覧のように、マップでも戦闘でも開ける）。
    /// ダイスごとに、札と、面ごとの値・刻印の説明を並べる。見るだけで操作はしない。
    /// </summary>
    public class PouchView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);

        public event Action Closed;

        public static PouchView Create(Transform canvas, UIArt art, DicePouch pouch)
        {
            var root = UIFactory.Stretch("PouchView", canvas);
            var view = root.gameObject.AddComponent<PouchView>();
            var shade = UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.75f));
            shade.raycastTarget = true;
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => view.Closed?.Invoke()); // 外を押しても閉じる
            var box = UIFactory.Panel("Box", root, new Vector2(1760, 960), new Vector2(0, 0), new Color(0.12f, 0.08f, 0.06f, 1f));
            box.raycastTarget = true;

            int available = pouch.All.Count(d => d.state == DiceState.Available);
            int used = pouch.All.Count(d => d.state == DiceState.Used);
            int sealedCount = pouch.All.Count(d => d.state == DiceState.Sealed);
            UIFactory.Text("Title", box.transform, $"ポーチ　{pouch.All.Count} / {pouch.Capacity} 個", 40, GoldColor, new Vector2(1000, 60), new Vector2(0, 438)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Summary", box.transform, $"使用可能 {available}　使用済み {used}" + (sealedCount > 0 ? $"　封印 {sealedCount}" : ""), 26, PaperColor, new Vector2(1000, 36), new Vector2(0, 388)); // 題の字に重ならない高さ
            var close = UIFactory.Button("Close", box.transform, new Vector2(160, 56), new Vector2(780, 438), ButtonColor, "閉じる", 26, out _);
            close.onClick.AddListener(() => view.Closed?.Invoke());

            // 2列 × 5段。使用可能なものを先に（トレイと同じ並び）
            var ordered = pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float cardW = 300f, cardH = 160f, textW = 500f, rowH = 170f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                int col = i % 2, row = i / 2;
                float x = col == 0 ? -430f : 430f;
                float y = 290f - row * rowH;
                string state = die.state == DiceState.Available ? "使用可能" : die.state == DiceState.Sealed ? "<color=#7A1F1F>封印中</color>" : "使用済み";
                var card = DiceCard.Create($"Die{i}", box.transform, die, art, new Vector2(cardW, cardH), new Vector2(x - textW / 2f + 10f, y), state, die.state != DiceState.Available, false);
                card.Button.transition = Selectable.Transition.None; // 押せないが、使用可能なものは明るいままにする
                card.Button.interactable = false;
                var faces = UIFactory.Text($"Faces{i}", box.transform, FaceLines(die), 20, PaperColor, new Vector2(textW, cardH), new Vector2(x + cardW / 2f + 20f, y), TextAlignmentOptions.Left);
                faces.enableAutoSizing = true; // 刻印が多いときは字を小さくして収める
                faces.fontSizeMax = 20;
                faces.fontSizeMin = 12;
            }
            return view;
        }

        /// <summary>面ごとの値と刻印の説明（刻印のない面はまとめて1行）。</summary>
        public static string FaceLines(DiceInstance die)
        {
            var sb = new StringBuilder();
            // 十二面・二十面は全部並べると長すぎて折り返すので、幅だけ出す
            if (die.faces.Length > 8) sb.Append($"面：{die.faces.Min(f => f.value)}〜{die.faces.Max(f => f.value)}（{DiceCard.FaceCountName(die.faces.Length)}）").Append('\n');
            else sb.Append("面：").Append(string.Join("・", die.faces.Select(f => f.value))).Append('\n');
            bool any = false;
            foreach (var face in die.faces.Where(f => f.engraving != null))
            {
                sb.Append($"<color=#FFD24D>{face.value}の面「{face.engraving.displayName}」</color>：{face.engraving.description}\n");
                any = true;
            }
            if (!any) sb.Append("<color=#A89A85>刻印なし</color>");
            return sb.ToString().TrimEnd('\n');
        }
    }
}
