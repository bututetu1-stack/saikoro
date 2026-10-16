using System.Collections.Generic;
using System.Linq;

namespace SaiNoMichi.Battle
{
    /// <summary>
    /// 役（チンチロ風）。同じラウンドに振ったダイスの出目の組み合わせ。強い順に並べてある（大きいほど強い）。
    /// 2026-10-17 開発者の要望：「STS2 でよくね？」にならないよう、サイコロならではの「祈って振る」瞬間を作る。
    /// </summary>
    public enum HandKind
    {
        None = 0,
        Pair = 1,       // ゾロ目：同じ目が2つ
        Hifumi = 2,     // ヒフミ：1・2・3
        Straight = 3,   // 連番：続いた目が3つ（1・2・3 と 4・5・6 は別の役）
        Shigoro = 4,    // シゴロ：4・5・6
        Triple = 5,     // アラシ：同じ目が3つ（1 以外）
        Pinzoro = 6,    // ピンゾロ：1・1・1
    }

    /// <summary>役の判定結果。indices は役に入る出目の番号（Evaluate に渡した並びの番号）。</summary>
    public struct HandResult
    {
        public HandKind kind;
        public int[] indices;

        public static readonly HandResult None = new HandResult { kind = HandKind.None, indices = new int[0] };
    }

    /// <summary>役の判定と、役ごとの効果の値。ルールだけの純粋なクラス（表示は UI 側）。</summary>
    public static class DiceHands
    {
        // TODO(仕様): 役の効果の値は仮（チンチロの配当に寄せた。ピンゾロ5倍・シゴロ2倍）
        public const int PinzoroMultiplier = 5;
        public const int TripleMultiplier = 2;
        public const int ShigoroMultiplier = 2;
        public const int StraightBonus = 2;
        public const int PairBonus = 2;

        /// <summary>役に入った出目にかける倍率（ない役は 1）。</summary>
        public static int Multiplier(HandKind kind)
        {
            switch (kind)
            {
                case HandKind.Pinzoro: return PinzoroMultiplier;
                case HandKind.Triple: return TripleMultiplier;
                case HandKind.Shigoro: return ShigoroMultiplier;
                default: return 1;
            }
        }

        /// <summary>役に入った出目に足す値（倍率をかけたあと）。</summary>
        public static int Bonus(HandKind kind)
        {
            switch (kind)
            {
                case HandKind.Straight: return StraightBonus;
                case HandKind.Pair: return PairBonus;
                default: return 0;
            }
        }

        public static string Name(HandKind kind)
        {
            switch (kind)
            {
                case HandKind.Pinzoro: return "ピンゾロ";
                case HandKind.Triple: return "アラシ";
                case HandKind.Shigoro: return "シゴロ";
                case HandKind.Straight: return "連番";
                case HandKind.Hifumi: return "ヒフミ";
                case HandKind.Pair: return "ゾロ目";
                default: return "";
            }
        }

        /// <summary>役のできる条件（一覧に出す）。</summary>
        public static string Condition(HandKind kind)
        {
            switch (kind)
            {
                case HandKind.Pinzoro: return "1・1・1";
                case HandKind.Triple: return "同じ目が3つ（1以外）";
                case HandKind.Shigoro: return "4・5・6";
                case HandKind.Straight: return "続いた目が3つ（2・3・4 など）";
                case HandKind.Hifumi: return "1・2・3";
                case HandKind.Pair: return "同じ目が2つ";
                default: return "";
            }
        }

        /// <summary>役の効果（短く。役ができたときと一覧に出す）。</summary>
        public static string Effect(HandKind kind)
        {
            switch (kind)
            {
                case HandKind.Pinzoro: return $"3個の出目が {PinzoroMultiplier} 倍";
                case HandKind.Triple: return $"3個の出目が {TripleMultiplier} 倍";
                case HandKind.Shigoro: return $"3個の出目が {ShigoroMultiplier} 倍";
                case HandKind.Straight: return $"3個の出目が +{StraightBonus}";
                case HandKind.Hifumi: return "3個とも使用可能に戻る";
                case HandKind.Pair: return $"2個の出目が +{PairBonus}";
                default: return "";
            }
        }

        /// <summary>出目の札に出す短い印（×2・+2・戻）。</summary>
        public static string Badge(HandKind kind)
        {
            int m = Multiplier(kind), b = Bonus(kind);
            if (m > 1) return $"×{m}";
            if (b > 0) return $"+{b}";
            return kind == HandKind.Hifumi ? "戻" : "";
        }

        /// <summary>強い順の一覧（役の一覧の画面に、この順で並べる）。</summary>
        public static readonly HandKind[] All =
        {
            HandKind.Pinzoro, HandKind.Triple, HandKind.Shigoro, HandKind.Straight, HandKind.Hifumi, HandKind.Pair,
        };

        /// <summary>
        /// 出目の並びから、いちばん強い役を1つ選ぶ（1ラウンドに役は1つ）。
        /// counted が false の出目は数えない（同じダイスを2回振ったときの2回目など）。
        /// </summary>
        public static HandResult Evaluate(IReadOnlyList<int> values, IReadOnlyList<bool> counted = null)
        {
            var idx = Enumerable.Range(0, values.Count).Where(i => counted == null || counted[i]).ToList();
            List<int> Of(int v) => idx.Where(i => values[i] == v).ToList();
            HandResult Make(HandKind k, IEnumerable<int> indices) => new HandResult { kind = k, indices = indices.ToArray() };

            // ピンゾロ
            var ones = Of(1);
            if (ones.Count >= 3) return Make(HandKind.Pinzoro, ones.Take(3));
            // アラシ（大きい目を優先）
            foreach (var v in idx.Select(i => values[i]).Distinct().OrderByDescending(v => v))
            {
                if (v == 1) continue;
                var same = Of(v);
                if (same.Count >= 3) return Make(HandKind.Triple, same.Take(3));
            }
            // シゴロ
            var shigoro = Run(idx, values, 4);
            if (shigoro != null) return Make(HandKind.Shigoro, shigoro);
            // 連番（1・2・3 と 4・5・6 は別の役。大きい目を優先）
            foreach (var v in idx.Select(i => values[i]).Distinct().OrderByDescending(v => v))
            {
                if (v == 1 || v == 4) continue;
                var run = Run(idx, values, v);
                if (run != null) return Make(HandKind.Straight, run);
            }
            // ヒフミ
            var hifumi = Run(idx, values, 1);
            if (hifumi != null) return Make(HandKind.Hifumi, hifumi);
            // ゾロ目（大きい目を優先）
            foreach (var v in idx.Select(i => values[i]).Distinct().OrderByDescending(v => v))
            {
                var same = Of(v);
                if (same.Count >= 2) return Make(HandKind.Pair, same.Take(2));
            }
            return HandResult.None;
        }

        /// <summary>start・start+1・start+2 の出目が揃っていれば、その番号（それぞれ最初のもの）。なければ null。</summary>
        static int[] Run(List<int> idx, IReadOnlyList<int> values, int start)
        {
            var result = new int[3];
            for (int k = 0; k < 3; k++)
            {
                int found = idx.FirstOrDefault(i => values[i] == start + k);
                if (!idx.Any(i => values[i] == start + k)) return null;
                result[k] = found;
            }
            return result;
        }
    }
}
