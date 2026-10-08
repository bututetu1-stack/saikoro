using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaiNoMichi.Tests
{
    /// <summary>層（仕様書 第2章：3層構成、層クリアで回復）。</summary>
    public class LayerTests
    {
        TestDice factory;
        GameConfig config;
        EnemyData a1, a2, b1, b2, boss1, boss2;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.useBranchingBoard = true;
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("two", 2, 2, 2, 2, 2, 2) };
            a1 = Named(factory.Enemy(10, new Intent(IntentType.Attack, 1)), "a1");
            a2 = Named(factory.Enemy(10, new Intent(IntentType.Attack, 1)), "a2");
            b1 = Named(factory.Enemy(20, new Intent(IntentType.Attack, 1)), "b1");
            b2 = Named(factory.Enemy(20, new Intent(IntentType.Attack, 1)), "b2");
            boss1 = Named(factory.Enemy(50, new Intent(IntentType.Attack, 1)), "boss1");
            boss2 = Named(factory.Enemy(80, new Intent(IntentType.Attack, 1)), "boss2");
            foreach (var e in new[] { a1, a2, b1, b2 }) e.earlyOk = true;
            config.layers = new List<LayerData>
            {
                new LayerData { displayName = "一", battleEnemies = new List<EnemyData> { a1, a2 }, boss = boss1 },
                new LayerData { displayName = "二", battleEnemies = new List<EnemyData> { b1, b2 }, boss = boss2 },
            };
        }

        static EnemyData Named(EnemyData e, string id)
        {
            e.id = id;
            return e;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        TileNode Tile(TileType type) => new TileNode(999, type);

        [Test]
        public void NoLayers_FallsBackToSingleLayer()
        {
            config.layers.Clear();
            config.battleEnemies = new List<EnemyData> { a1 };
            config.boss = boss1;
            var run = new RunState(config, 1);

            Assert.AreEqual(1, run.LayerCount);
            Assert.IsTrue(run.IsFinalLayer);
            Assert.AreEqual(boss1, run.PickEnemy(Tile(TileType.Boss)));
            Assert.Throws<InvalidOperationException>(() => run.AdvanceLayer());
        }

        [Test]
        public void Advance_HealsThirtyPercent_RestoresDice_NewBoard()
        {
            var run = new RunState(config, 1);
            var firstBoard = run.board;
            run.player.hp = 10;
            run.Move(run.pouch.All[1]);
            Assert.AreEqual(DiceState.Used, run.pouch.All[1].state);

            var result = run.AdvanceLayer();

            Assert.AreEqual(1, run.LayerIndex);
            Assert.IsTrue(run.IsFinalLayer);
            Assert.AreEqual(12, result.healed, "最大HP40の30%");
            Assert.AreEqual(22, run.player.hp);
            Assert.IsTrue(run.pouch.All.All(d => d.state == DiceState.Available));
            Assert.AreNotSame(firstBoard, run.board);
            Assert.AreEqual(run.board.Start, run.Current);
            Assert.AreEqual(0, run.MovesThisLayer);
        }

        [Test]
        public void Enemies_ComeFromCurrentLayer()
        {
            var run = new RunState(config, 1);
            for (int i = 0; i < 5; i++) CollectionAssert.Contains(new[] { a1, a2 }, run.PickEnemy(Tile(TileType.Battle)));
            Assert.AreEqual(boss1, run.PickEnemy(Tile(TileType.Boss)));

            run.AdvanceLayer();
            config.previousLayerEnemyPercent = 0;
            for (int i = 0; i < 5; i++) CollectionAssert.Contains(new[] { b1, b2 }, run.PickEnemy(Tile(TileType.Battle)));
            Assert.AreEqual(boss2, run.PickEnemy(Tile(TileType.Boss)));
        }

        [Test]
        public void PreviousLayerEnemies_SometimesAppearWithMoreHp()
        {
            config.previousLayerEnemyPercent = 100;
            var run = new RunState(config, 1);
            run.AdvanceLayer();
            for (int i = 0; i < 3; i++) run.PickEnemy(Tile(TileType.Battle)); // 最初の3戦は今の層の弱い敵だけ

            var old = run.PickEnemy(Tile(TileType.Battle));
            CollectionAssert.Contains(new[] { a1, a2 }, old);
            Assert.AreEqual(150, run.LastEnemyHpPercent);
            var battle = new BattleState(run.player, old, run.pouch, new System.Random(0), run.effects, run, run.LastEnemyHpPercent);
            Assert.AreEqual(15, battle.enemy.maxHp, "HP 10 の 150%");
        }

        [Test]
        public void Advance_ResetsWarajiCharges()
        {
            var waraji = ScriptableObject.CreateInstance<ChargedMoveAdjustEffect>();
            waraji.trigger = Trigger.OnMoveRolled;
            waraji.chargesPerLayer = 3;
            var relic = ScriptableObject.CreateInstance<RelicData>();
            relic.effects = new List<EffectSO> { waraji };
            created.Add(waraji);
            created.Add(relic);
            var run = new RunState(config, 1);
            run.AddRelic(relic);
            var move = run.BeginMove(run.pouch.All[1]);
            run.AdjustMove(move, 3);
            Assert.AreEqual(2, run.ChargesOf(relic));

            run.AdvanceLayer();
            Assert.AreEqual(3, run.ChargesOf(relic));
        }
    }
}
