using System;
using System.Collections.Generic;

namespace SaiNoMichi.Board
{
    /// <summary>1層ぶんの盤面。</summary>
    public class BoardData
    {
        public readonly List<TileNode> tiles;
        public TileNode Start => tiles[0];
        public TileNode Goal { get; }

        public BoardData(List<TileNode> tiles, TileNode goal)
        {
            this.tiles = tiles;
            Goal = goal;
        }

        /// <summary>
        /// from から steps 歩進んだ先のマスを返す。ゴール（行き止まり）やボスマスでは歩数が余っても止まる。
        /// 分岐では chooseBranch で進む先を選ぶ（省略時は最初の道）。分岐点で選ぶこと自体は歩数を使わない。
        /// passed には通過したマス（出発マスと止まったマスは含まない）を入れる。
        /// </summary>
        public static TileNode Advance(TileNode from, int steps, List<TileNode> passed = null,
            Func<TileNode, IReadOnlyList<TileNode>, TileNode> chooseBranch = null)
        {
            var current = from;
            for (int i = 0; i < steps; i++)
            {
                if (current.IsEnd) break;

                if (current != from) passed?.Add(current);
                current = current.IsBranch && chooseBranch != null
                    ? chooseBranch(current, current.next)
                    : current.next[0];

                if (current.type == TileType.Boss) break;
            }
            return current;
        }
    }
}
