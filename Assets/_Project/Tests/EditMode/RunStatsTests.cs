using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>ランの成績（クリア画面と記録用）。</summary>
    public class RunStatsTests
    {
        TestDice factory;
        Phase0Config config;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<Phase0Config>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("two", 2, 2, 2, 2, 2, 2) };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        [Test]
        public void GoldEarned_CountsOnlyGains()
        {
            var run = new RunState(config, 1);
            run.GainGold(30);
            run.SpendGold(20);
            run.GainGold(5);
            Assert.AreEqual(35, run.stats.goldEarned);
        }

        [Test]
        public void Stops_CountedByTileType()
        {
            var run = new RunState(config, 1);
            var two = run.pouch.All[1];
            var move = run.Move(two);
            Assert.AreEqual(1, run.stats.tilesStopped[move.to.type]);
        }

        [Test]
        public void Acquired_ReportsDiceRelicRemove()
        {
            var run = new RunState(config, 1);
            var log = new List<string>();
            run.Acquired += (kind, item) => log.Add($"{kind}:{item}");

            var relic = ScriptableObject.CreateInstance<RelicData>();
            relic.displayName = "鈴";
            created.Add(relic);
            var die = run.AddDice(factory.Data("new", 1, 2, 3, 4, 5, 6));
            run.AddRelic(relic);
            run.RemoveDice(die);

            CollectionAssert.AreEqual(new[] { "dice:new", "relic:鈴", "remove:new" }, log);
            CollectionAssert.AreEqual(new[] { "new" }, run.stats.diceGained);
            CollectionAssert.AreEqual(new[] { "new" }, run.stats.diceRemoved);
        }

        [Test]
        public void Victory_Counted()
        {
            var run = new RunState(config, 1);
            var enemy = factory.Enemy(1, new Intent(IntentType.Attack, 1));
            enemy.kind = EnemyKind.Elite;
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
            battle.Roll(run.pouch.All[1]);
            battle.Resolve();

            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(1, run.stats.battlesWon);
            Assert.AreEqual(1, run.stats.elitesWon);
        }
    }
}
