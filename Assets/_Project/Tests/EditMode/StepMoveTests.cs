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
    /// <summary>1歩ずつの移動（分かれ道で道を選ぶ）。</summary>
    public class StepMoveTests
    {
        TestDice factory;
        GameConfig config;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.useBranchingBoard = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            factory.DestroyAll();
        }

        RunState RunWith(int faceValue, int seed = 3)
        {
            var data = factory.Data("fixed", faceValue, faceValue, faceValue, faceValue, faceValue, faceValue);
            config.startingDice = new List<DiceData> { data, data };
            return new RunState(config, seed);
        }

        [Test]
        public void StepMove_StopsAtBranchForChoice_AndChoiceDoesNotCostAStep()
        {
            // 出目10なら、4〜7マス目にある最初の分岐点に必ず歩数を残して着く
            var run = RunWith(10);
            var branch = run.board.tiles.First(t => t.IsBranch);
            var move = run.BeginMove(run.pouch.Available.First());
            while (run.Current != branch)
            {
                Assert.IsFalse(run.NeedsBranchChoice(move));
                run.StepMove(move);
            }

            Assert.IsTrue(run.NeedsBranchChoice(move));
            Assert.AreEqual(10 - branch.id, move.remaining);
            var chosen = branch.next.Last();
            run.StepMove(move, chosen);
            Assert.AreSame(chosen, run.Current, "選んだ道に進む");
            Assert.AreEqual(10 - branch.id - 1, move.remaining, "分岐で選ぶこと自体は歩数を使わない（進んだ1歩だけ減る）");
        }

        [Test]
        public void Move_WithChooser_FollowsChosenPath()
        {
            var run = RunWith(1);
            var branch = run.board.tiles.First(t => t.IsBranch);
            while (run.Current != branch) run.Move(run.pouch.Available.First());

            var last = branch.next.Last();
            var result = run.Move(run.pouch.Available.First(), (b, nexts) => nexts.Last());

            Assert.AreSame(last, result.to);
        }

        [Test]
        public void Move_ZeroValue_DoesNotMove()
        {
            var run = RunWith(0);
            var start = run.Current;

            var result = run.Move(run.pouch.Available.First());

            Assert.AreSame(start, result.to);
            Assert.AreEqual(1, run.Turn);
        }

        [Test]
        public void StepMove_StopsAtBossEvenWithStepsLeft()
        {
            var run = RunWith(10);
            int guard = 0;
            while (!run.ReachedGoal && guard++ < 50) run.Move(run.pouch.Available.First());

            Assert.IsTrue(run.ReachedGoal);
            Assert.AreEqual(TileType.Boss, run.Current.type);
        }

        [Test]
        public void PickEnemy_EliteTileUsesEliteList()
        {
            var elite = factory.Enemy(32, new Battle.Intent(Battle.IntentType.Attack, 7));
            config.eliteEnemies = new List<Battle.EnemyData> { elite };
            var run = RunWith(1);

            Assert.AreSame(elite, run.PickEnemy(new TileNode(0, TileType.Elite)));
        }
    }
}
