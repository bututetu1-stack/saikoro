using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.Board
{
    /// <summary>盤面の1マス。next が複数なら分岐点、空ならゴール。</summary>
    public class TileNode
    {
        public int id;
        public TileType type;
        public bool passEffect;
        /// <summary>入ったら歩数が残っていても止まる（ボスの手前の休憩。開発者の判断：ボス戦の前に必ず休めるように）。</summary>
        public bool stopHere;
        public List<TileNode> next = new List<TileNode>();
        public Vector2 position;

        public TileNode(int id, TileType type)
        {
            this.id = id;
            this.type = type;
        }

        public bool IsBranch => next.Count > 1;
        public bool IsEnd => next.Count == 0;

        public override string ToString() => $"#{id}:{type}";
    }
}
