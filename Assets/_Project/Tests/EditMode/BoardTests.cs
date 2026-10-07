using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Board;

namespace SaiNoMichi.Tests
{
    public class BoardTests
    {
        static readonly int[] Normal = { 1, 2, 3, 4, 5, 6 };
        static readonly int[] High = { 4, 4, 5, 5, 6, 6 };
        const float Eps = 1e-5f;

        static BoardData Linear(int seed) => BoardGenerator.GenerateLinear(new System.Random(seed), new LinearBoardSettings());

        /// <summary>種類を指定して直線の盤面を作る。</summary>
        static List<TileNode> Chain(params TileType[] types)
        {
            var tiles = types.Select((t, i) => new TileNode(i, t)).ToList();
            for (int i = 1; i < tiles.Count; i++) tiles[i - 1].next.Add(tiles[i]);
            return tiles;
        }

        // ---- 止まりうるマス ----

        [Test]
        public void Reach_NormalDiceFromStart_TilesOneToSixEachOneSixth()
        {
            var board = Linear(1);

            var reach = ReachCalculator.Compute(board.Start, Normal);

            Assert.AreEqual(6, reach.Count);
            for (int i = 1; i <= 6; i++)
            {
                Assert.AreEqual(1f / 6f, reach[board.tiles[i]], Eps, $"マス{i}");
            }
        }

        [Test]
        public void Reach_NormalDiceFromSeventeen_GoalGetsFourSixths()
        {
            var board = Linear(1);

            var reach = ReachCalculator.Compute(board.tiles[17], Normal);

            Assert.AreEqual(3, reach.Count);
            Assert.AreEqual(1f / 6f, reach[board.tiles[18]], Eps);
            Assert.AreEqual(1f / 6f, reach[board.tiles[19]], Eps);
            Assert.AreEqual(4f / 6f, reach[board.tiles[20]], Eps);
        }

        [Test]
        public void Reach_StopsAtBossEvenIfStepsRemain()
        {
            var tiles = Chain(TileType.Empty, TileType.Empty, TileType.Boss, TileType.Empty, TileType.Empty, TileType.Empty, TileType.Empty);

            var reach = ReachCalculator.Compute(tiles[0], High);

            Assert.AreEqual(1, reach.Count);
            Assert.AreEqual(1f, reach[tiles[2]], Eps);
        }

        [Test]
        public void Reach_AtBranch_EachRouteGetsFullProbability()
        {
            // 0 → 1 → (2a or 2b)
            var start = new TileNode(0, TileType.Empty);
            var mid = new TileNode(1, TileType.Empty);
            var a = new TileNode(2, TileType.Battle);
            var b = new TileNode(3, TileType.Rest);
            start.next.Add(mid);
            mid.next.Add(a);
            mid.next.Add(b);

            var reach = ReachCalculator.Compute(start, new[] { 2, 2, 2, 2, 2, 2 });

            Assert.AreEqual(1f, reach[a], Eps);
            Assert.AreEqual(1f, reach[b], Eps);
        }

        // ---- 移動 ----

        [Test]
        public void Advance_OvershootStopsAtGoal()
        {
            var board = Linear(1);
            var passed = new List<TileNode>();

            var end = BoardData.Advance(board.tiles[17], 6, passed);

            Assert.AreSame(board.Goal, end);
            CollectionAssert.AreEqual(new[] { board.tiles[18], board.tiles[19] }, passed);
        }

        [Test]
        public void Advance_ExactSteps()
        {
            var board = Linear(1);

            Assert.AreSame(board.tiles[5], BoardData.Advance(board.Start, 5));
        }

        // ---- 盤面の生成 ----

        [Test]
        public void Generate_SameSeed_SameBoard()
        {
            var a = Linear(42).tiles.Select(t => t.type);
            var b = Linear(42).tiles.Select(t => t.type);

            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Generate_DifferentSeeds_ProduceDifferentBoards()
        {
            var layouts = Enumerable.Range(0, 20)
                .Select(seed => string.Join(",", Linear(seed).tiles.Select(t => (int)t.type)))
                .Distinct()
                .Count();

            Assert.Greater(layouts, 1);
        }

        [Test]
        public void Generate_FollowsPlacementRules([Range(0, 199)] int seed)
        {
            var s = new LinearBoardSettings();
            var types = Linear(seed).tiles.Select(t => t.type).ToArray();

            Assert.AreEqual(21, types.Length, "マス0〜20");
            Assert.AreEqual(TileType.Boss, types[20], "ゴールはボス");
            Assert.AreEqual(1, types.Count(t => t == TileType.Boss));
            for (int i = 1; i <= 3; i++) Assert.AreEqual(TileType.Empty, types[i], $"マス{i}は空白");
            Assert.AreEqual(6, types.Count(t => t == TileType.Battle), "戦闘は6個");
            Assert.AreEqual(3, types.Count(t => t == TileType.Rest), "休憩は3個");
            Assert.IsTrue(Enumerable.Range(14, 4).Any(i => types[i] == TileType.Rest), "マス14〜17に休憩");
            Assert.IsTrue(BoardGenerator.SatisfiesRunLimits(types, s), "連続の上限");
        }

        [Test]
        public void RunLimits_DetectsViolations()
        {
            var s = new LinearBoardSettings();
            var e = TileType.Empty;
            var f = TileType.Battle;

            Assert.IsFalse(BoardGenerator.SatisfiesRunLimits(new[] { e, f, f, f, e, TileType.Boss }, s), "戦闘3連続");
            Assert.IsFalse(BoardGenerator.SatisfiesRunLimits(new[] { e, e, e, e, e, TileType.Boss }, s), "空白4連続");
            Assert.IsTrue(BoardGenerator.SatisfiesRunLimits(new[] { e, e, e, e, f, f, TileType.Boss }, s));
        }
    }
}
