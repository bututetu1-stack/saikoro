using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.Board
{
    /// <summary>シード付きの乱数から盤面を作る。</summary>
    public static class BoardGenerator
    {
        public static BoardData GenerateLinear(System.Random rng, LinearBoardSettings settings)
        {
            var types = GenerateLinearTypes(rng, settings);

            var tiles = new List<TileNode>(types.Length);
            for (int i = 0; i < types.Length; i++)
            {
                tiles.Add(new TileNode(i, types[i]) { position = new Vector2(i, 0f) });
                if (i > 0) tiles[i - 1].next.Add(tiles[i]);
            }
            return new BoardData(tiles, tiles[tiles.Count - 1]);
        }

        /// <summary>マス0〜ゴールの種類の並び。条件を満たすまで並べ直す（棄却法）。</summary>
        public static TileType[] GenerateLinearTypes(System.Random rng, LinearBoardSettings s)
        {
            int goal = s.goalIndex;
            int firstFree = s.leadingEmptyCount + 1;

            for (int attempt = 0; attempt < s.maxAttempts; attempt++)
            {
                var types = new TileType[goal + 1];
                // TODO(仕様): スタートのマス0は空白扱いとする（連続数の判定には含めない）
                types[0] = TileType.Empty;
                types[goal] = TileType.Boss;

                int fixedRest = rng.Next(s.guaranteedRestMin, s.guaranteedRestMax + 1);
                types[fixedRest] = TileType.Rest;

                var slots = new List<int>();
                for (int i = firstFree; i < goal; i++)
                {
                    if (i != fixedRest) slots.Add(i);
                }

                var bag = new List<TileType>();
                for (int i = 0; i < s.battleCount; i++) bag.Add(TileType.Battle);
                for (int i = 0; i < s.restCount - 1; i++) bag.Add(TileType.Rest);
                while (bag.Count < slots.Count) bag.Add(TileType.Empty);
                Shuffle(bag, rng);

                for (int i = 0; i < slots.Count; i++) types[slots[i]] = bag[i];

                if (SatisfiesRunLimits(types, s)) return types;
            }
            throw new InvalidOperationException("盤面の生成に失敗しました。設定を見直してください。");
        }

        /// <summary>マス1〜ゴール手前で、同じ種類の連続数が上限を超えていないか。</summary>
        public static bool SatisfiesRunLimits(TileType[] types, LinearBoardSettings s)
        {
            int run = 0;
            for (int i = 1; i < types.Length - 1; i++)
            {
                run = (i > 1 && types[i] == types[i - 1]) ? run + 1 : 1;
                int limit = types[i] == TileType.Empty ? s.maxEmptyRun : s.maxSameRun;
                if (run > limit) return false;
            }
            return true;
        }

        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
