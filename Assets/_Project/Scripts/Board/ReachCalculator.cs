using System.Collections.Generic;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Board
{
    /// <summary>
    /// 「このダイスで止まりうるマス」と確率（仕様書 第14章）。
    /// 各面の数値ぶんグラフをたどり、到達したマスに 1/面数 ずつ足す。ボスマスに入ったらそこで止める。
    /// 分岐ではプレイヤーが道を選べるので、どの道の候補にも同じ確率を足す（分岐をまたぐと合計が1を超える）。
    /// </summary>
    public static class ReachCalculator
    {
        public static Dictionary<TileNode, float> Compute(TileNode from, DiceInstance die)
        {
            var values = new int[die.faces.Length];
            for (int i = 0; i < values.Length; i++) values[i] = die.faces[i].value;
            return Compute(from, values);
        }

        public static Dictionary<TileNode, float> Compute(TileNode from, IReadOnlyList<int> faceValues)
        {
            var result = new Dictionary<TileNode, float>();
            float p = 1f / faceValues.Count;
            var reached = new HashSet<TileNode>();

            foreach (int steps in faceValues)
            {
                reached.Clear();
                Walk(from, from, steps, reached);
                foreach (var tile in reached)
                {
                    result.TryGetValue(tile, out float sum);
                    result[tile] = sum + p;
                }
            }
            return result;
        }

        static void Walk(TileNode origin, TileNode current, int remaining, HashSet<TileNode> reached)
        {
            bool stopsHere = remaining == 0 || current.IsEnd || (current.type == TileType.Boss && current != origin);
            if (stopsHere)
            {
                reached.Add(current);
                return;
            }
            foreach (var n in current.next)
            {
                Walk(origin, n, remaining - 1, reached);
            }
        }
    }
}
