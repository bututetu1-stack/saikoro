using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    public class RunStateTests
    {
        TestDice factory;
        GameConfig config;
        EnemyData slime, oni, boss;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            slime = factory.Enemy(12, new Intent(IntentType.Attack, 5));
            oni = factory.Enemy(15, new Intent(IntentType.Attack, 6));
            boss = factory.Enemy(40, new Intent(IntentType.Attack, 8));

            config = ScriptableObject.CreateInstance<GameConfig>();
            config.startingDice = new List<DiceData>
            {
                factory.Data("normal", 1, 2, 3, 4, 5, 6),
                factory.Data("normal", 1, 2, 3, 4, 5, 6),
                factory.Data("456", 4, 4, 5, 5, 6, 6),
                factory.Data("123", 1, 1, 2, 2, 3, 3),
            };
            config.battleEnemies = new List<EnemyData> { slime, oni };
            config.boss = boss;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(config);
            factory.DestroyAll();
        }

        [Test]
        public void NewRun_StartsWithConfiguredPouchAndHp()
        {
            var run = new RunState(config, 1);

            Assert.AreEqual(4, run.pouch.All.Count);
            Assert.AreEqual(4, run.pouch.AvailableCount);
            Assert.AreEqual(40, run.player.hp);
            Assert.AreSame(run.board.Start, run.Current);
            Assert.AreEqual(0, run.Turn);
            Assert.AreEqual(20, run.TilesToGoal);
        }

        [Test]
        public void Move_AdvancesByRollUsesDieAndCountsTurn()
        {
            config.startingDice = new List<DiceData> { factory.Data("three", 3, 3, 3, 3, 3, 3), factory.Data("other", 1, 1, 1, 1, 1, 1) };
            var run = new RunState(config, 1);
            var die = run.pouch.All[0];

            var move = run.Move(die);

            Assert.AreEqual(3, move.value);
            Assert.AreSame(run.board.tiles[3], run.Current);
            CollectionAssert.AreEqual(new[] { run.board.tiles[1], run.board.tiles[2] }, move.passed);
            Assert.AreEqual(DiceState.Used, die.state);
            Assert.AreEqual(1, run.Turn);
            Assert.AreEqual(2, move.availableBefore);
            Assert.IsFalse(move.refreshed);
        }

        [Test]
        public void Move_PastGoal_StopsAtGoalAndThenCannotMove()
        {
            var six = factory.Data("six", 6, 6, 6, 6, 6, 6);
            config.startingDice = new List<DiceData> { six, six };
            var run = new RunState(config, 1);

            for (int i = 0; i < 4; i++) run.Move(run.pouch.Available.First()); // 6,12,18,20

            Assert.IsTrue(run.ReachedGoal);
            Assert.AreEqual(4, run.Turn);
            Assert.Throws<InvalidOperationException>(() => run.Move(run.pouch.Available.First()));
        }

        [Test]
        public void Rest_HealsThirtyPercentFloorAndClampsToMax()
        {
            var run = new RunState(config, 1);
            run.player.hp = 10;

            Assert.AreEqual(12, run.Rest());
            Assert.AreEqual(22, run.player.hp);

            run.player.hp = 35;
            Assert.AreEqual(5, run.Rest(), "最大HPを超えない");
            Assert.AreEqual(40, run.player.hp);
        }

        [Test]
        public void PickEnemy_ByTileType()
        {
            var run = new RunState(config, 1);

            Assert.AreSame(boss, run.PickEnemy(new TileNode(0, TileType.Boss)));
            Assert.IsNull(run.PickEnemy(new TileNode(0, TileType.Empty)));
            Assert.IsNull(run.PickEnemy(new TileNode(0, TileType.Rest)));

            var picks = Enumerable.Range(0, 50).Select(_ => run.PickEnemy(new TileNode(0, TileType.Battle))).ToList();
            Assert.IsTrue(picks.All(p => p == slime || p == oni), "候補の中からだけ選ぶ");
            Assert.IsTrue(picks.Contains(slime) && picks.Contains(oni), "半々で両方出る");
        }

        [Test]
        public void SameSeed_SameBoardAndSameRolls()
        {
            var a = new RunState(config, 99);
            var b = new RunState(config, 99);

            CollectionAssert.AreEqual(a.board.tiles.Select(t => t.type), b.board.tiles.Select(t => t.type));
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(a.Move(a.pouch.All[i]).value, b.Move(b.pouch.All[i]).value);
            }
        }
    }
}
