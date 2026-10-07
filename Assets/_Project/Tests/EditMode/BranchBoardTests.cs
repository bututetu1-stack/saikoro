using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>分岐する盤面の生成（仕様書 第8章）。</summary>
    public class BranchBoardTests
    {
        static readonly LayerBoardSettings Settings = new LayerBoardSettings();

        static BoardData Generate(int seed) => BranchBoardGenerator.Generate(new System.Random(seed), Settings);

        /// <summary>スタートからボスまでのすべての道（各道はマスの並び。スタートとボスを含む）。</summary>
        static List<List<TileNode>> Routes(BoardData board)
        {
            var routes = new List<List<TileNode>>();
            void Walk(TileNode n, List<TileNode> path)
            {
                path.Add(n);
                if (n.IsEnd) routes.Add(new List<TileNode>(path));
                else foreach (var next in n.next) Walk(next, path);
                path.RemoveAt(path.Count - 1);
            }
            Walk(board.Start, new List<TileNode>());
            return routes;
        }

        static Dictionary<TileNode, List<TileNode>> Preds(BoardData board)
        {
            var preds = board.tiles.ToDictionary(t => t, t => new List<TileNode>());
            foreach (var t in board.tiles) foreach (var n in t.next) preds[n].Add(t);
            return preds;
        }

        [Test]
        public void SameSeed_SameBoard()
        {
            var a = Generate(11);
            var b = Generate(11);

            CollectionAssert.AreEqual(a.tiles.Select(t => t.type), b.tiles.Select(t => t.type));
            CollectionAssert.AreEqual(a.tiles.Select(t => string.Join(",", t.next.Select(n => n.id))), b.tiles.Select(t => string.Join(",", t.next.Select(n => n.id))));
        }

        [Test]
        public void Structure([NUnit.Framework.Range(0, 99)] int seed)
        {
            var board = Generate(seed);
            var preds = Preds(board);

            Assert.AreEqual(TileType.Boss, board.Goal.type);
            Assert.AreEqual(1, board.tiles.Count(t => t.type == TileType.Boss), "ボスは1つ");
            Assert.IsTrue(board.tiles.All(t => board.DistanceToGoal(t) >= 0), "どのマスからもボスに行ける");
            Assert.AreEqual(1, board.tiles.Count(t => t.IsEnd), "行き止まりはボスだけ");

            // どの道でも必ず通るマス（一本道の部分）。そこから分かれるのが分岐点で、3つある
            var routes = Routes(board);
            var trunk = new HashSet<TileNode>(board.tiles.Where(t => routes.All(r => r.Contains(t))));
            var branches = trunk.Where(t => t.IsBranch).OrderBy(t => t.position.x).ToList();
            Assert.AreEqual(3, branches.Count);
            Assert.That(branches[0].id, Is.InRange(Settings.firstBranchMin, Settings.firstBranchMax), "最初の一本道のマスは作った順に 0,1,2… なので id が歩数");
            Assert.AreEqual(3, branches[0].next.Count);
            Assert.AreEqual(3, branches[1].next.Count);
            Assert.AreEqual(2, branches[2].next.Count);

            // 1つ目・2つ目の分岐の道は長さを揃えない（next[0] が同じ道の続き。横道はあとから足している）
            foreach (var split in branches.Take(2))
            {
                var lengths = split.next.Select(first =>
                {
                    int len = 0;
                    for (var n = first; !trunk.Contains(n); n = n.next[0]) len++;
                    return len;
                }).ToList();
                Assert.AreEqual(lengths.Count, lengths.Distinct().Count(), "道の長さが全部違う: " + string.Join(",", lengths));
            }

            // 横道と小さな分かれ道：分かれた道の途中にも分かれ道がある（横道は隣り合う道の組ごとに1〜2本。3+3+2本の区間で最低 2+2+1）
            var crossLinks = board.tiles.Where(t => !trunk.Contains(t) && t.IsBranch).ToList();
            Assert.GreaterOrEqual(crossLinks.Count, 5, "横道の数");
            foreach (var c in crossLinks)
            {
                Assert.IsTrue(c.next.All(n => n.position.x > c.position.x), "横道も前にしか進まない");
            }

            // ボスの手前は一本道（7マス以上）
            var n2 = board.Goal;
            for (int i = 0; i < Settings.finalTrunkMin; i++)
            {
                Assert.AreEqual(1, preds[n2].Count, "ボスの手前は合流済み");
                n2 = preds[n2][0];
                Assert.AreEqual(1, n2.next.Count);
            }

            // 道の長さは 36〜44 の前後
            foreach (var route in routes)
            {
                // 短い道や横道で近道すると短く、長い道を通ると長くなる
                Assert.That(route.Count - 1, Is.InRange(Settings.minLength - 10, Settings.maxLength + 10), "歩数");
            }
        }

        [Test]
        public void Diamonds_OnOuterLanes_JoinRightAfter()
        {
            int diamonds = 0;
            for (int seed = 0; seed < 50; seed++)
            {
                var board = Generate(seed);
                var preds = Preds(board);
                // 外へふくらんだマス（外側の道より外）が小さな分かれ道
                foreach (var side in board.tiles.Where(t => Mathf.Abs(t.position.y) > 1.2f))
                {
                    diamonds++;
                    Assert.AreEqual(1, preds[side].Count);
                    Assert.AreEqual(1, side.next.Count);
                    var from = preds[side][0];
                    var to = side.next[0];
                    // 元の道の隣のマスと並び、同じマスに合流する（歩数は変わらない）
                    Assert.IsTrue(from.next.Any(n => n != side && n.next.Contains(to) && Mathf.Approximately(n.position.x, side.position.x)), $"seed {seed}");
                }
            }
            Assert.Greater(diamonds, 0, "小さな分かれ道がどこかに出る");
        }

        [Test]
        public void FixedTiles([NUnit.Framework.Range(0, 99)] int seed)
        {
            var board = Generate(seed);

            Assert.IsTrue(board.tiles.Any(t => (t.type == TileType.Rest || t.type == TileType.Shop) && board.DistanceToGoal(t) >= 3 && board.DistanceToGoal(t) <= 6),
                "ボスの3〜6マス手前に休憩かショップ");
            Assert.IsTrue(board.tiles.Any(t => t.type == TileType.Forge), "鍛冶が1つ以上");
        }

        [Test]
        public void PlacementRules([NUnit.Framework.Range(0, 99)] int seed)
        {
            var board = Generate(seed);
            var preds = Preds(board);

            foreach (var route in Routes(board))
            {
                // 最初の5マス：戦闘は1つまで、エリート・罠なし
                var startZone = route.Skip(1).Take(Settings.safeStartTiles).ToList();
                Assert.IsFalse(startZone.Any(t => t.type == TileType.Elite || t.type == TileType.Trap), "最初の5マスにエリート・罠");
                Assert.LessOrEqual(startZone.Count(t => t.type == TileType.Battle), 1, "最初の5マスの戦闘");

                // 同じ種類の連続（スタートとボスは除く）
                var middle = route.Skip(1).Take(route.Count - 2).ToList();
                int run = 0;
                for (int i = 0; i < middle.Count; i++)
                {
                    run = i > 0 && middle[i].type == middle[i - 1].type ? run + 1 : 1;
                    int limit = middle[i].type == TileType.Empty ? Settings.maxEmptyRun : Settings.maxSameRun;
                    Assert.LessOrEqual(run, limit, $"{middle[i].type} が {run} 連続");
                }

                // ショップ同士は6マス以上離す
                var shops = route.Select((t, i) => (t, i)).Where(x => x.t.type == TileType.Shop).Select(x => x.i).ToList();
                for (int i = 1; i < shops.Count; i++)
                {
                    Assert.GreaterOrEqual(shops[i] - shops[i - 1], Settings.minShopDistance, "ショップの間隔");
                }
            }

            // エリートの前後は戦闘・エリート・罠・ボスにしない
            foreach (var elite in board.tiles.Where(t => t.type == TileType.Elite))
            {
                Assert.IsFalse(preds[elite].Concat(elite.next).Any(nb => BranchBoardGenerator.BlocksElite(nb.type)), "エリートの前後");
            }

            // 空白マスはスタートだけ
            Assert.IsFalse(board.tiles.Any(t => t != board.Start && t.type == TileType.Empty), "空白マス");

            // ボス直前は休憩にしない
            Assert.IsFalse(preds[board.Goal].Any(p => p.type == TileType.Rest));
        }

        [Test]
        public void Validation_NormalDiceUsuallyStopsAtShopOrRest([NUnit.Framework.Range(0, 19)] int seed)
        {
            var board = Generate(seed);
            float miss = BranchBoardGenerator.MissShopRestRate(board.Start, board.Goal, new System.Random(1000 + seed), 2000);

            Assert.LessOrEqual(miss, Settings.maxMissShopRestRate + 0.05f, "生成時の検証（400回）と同じ程度");
        }

        [Test]
        public void TileMix_RoughlyFollowsWeights()
        {
            var counts = new Dictionary<TileType, int>();
            int total = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                foreach (var t in Generate(seed).tiles.Skip(1).Where(t => t.type != TileType.Boss))
                {
                    counts.TryGetValue(t.type, out int c);
                    counts[t.type] = c + 1;
                    total++;
                }
            }
            double Rate(TileType type) => counts.TryGetValue(type, out int c) ? c / (double)total : 0;

            Assert.That(Rate(TileType.Battle), Is.InRange(0.18, 0.4), "戦闘 30%");
            Assert.That(Rate(TileType.Event), Is.InRange(0.15, 0.35), "イベント 25%");
            Assert.That(Rate(TileType.Elite), Is.InRange(0.003, 0.05), "エリート 2%");
        }

        [Test]
        public void Advance_ChoosesBranchWithCallback()
        {
            var board = Generate(3);
            var branch = board.tiles.First(t => t.IsBranch);
            var chosen = branch.next[branch.next.Count - 1];

            var landed = BoardData.Advance(branch, 1, null, (b, nexts) => nexts[nexts.Count - 1]);

            Assert.AreSame(chosen, landed);
        }

        [Test]
        public void RunState_UsesBranchingBoardWhenEnabled()
        {
            var factory = new TestDice();
            var config = ScriptableObject.CreateInstance<Phase0Config>();
            config.startingDice = new List<DiceData> { factory.Data("n", 1, 2, 3, 4, 5, 6) };
            config.useBranchingBoard = true;

            var run = new RunState(config, 5);

            Assert.IsTrue(run.board.tiles.Any(t => t.IsBranch));
            Assert.AreEqual(run.board.DistanceToGoal(run.board.Start), run.TilesToGoal);
            Assert.Greater(run.TilesToGoal, 25);

            Object.DestroyImmediate(config);
            factory.DestroyAll();
        }
    }
}
