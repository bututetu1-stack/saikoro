using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>旅の得点（STS の得点画面風）。</summary>
    public class RunScoreTests
    {
        TestDice factory;
        GameConfig config;
        readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            config.relicPool = new List<RelicData> { Relic("a", Rarity.Common), Relic("b", Rarity.Uncommon), Relic("c", Rarity.Rare) };
            config.charmPool = new List<CharmData> { Charm("heal"), Charm("smoke") };
            var uncommon = factory.Data("uncommon", 2, 3, 4, 5, 6, 7);
            uncommon.rarity = Rarity.Uncommon;
            config.rewardDicePool = new List<DiceData> { factory.Data("common", 1, 1, 2, 2, 3, 3), uncommon };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) UnityEngine.Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        RelicData Relic(string id, Rarity rarity)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.id = id;
            r.displayName = id;
            r.rarity = rarity;
            created.Add(r);
            return r;
        }

        CharmData Charm(string id)
        {
            var c = ScriptableObject.CreateInstance<CharmData>();
            c.id = id;
            c.displayName = id;
            c.kind = CharmKind.Heal;
            c.amount = 5;
            created.Add(c);
            return c;
        }

        [Test]
        public void Breakdown_CountsProgressAndBattles()
        {
            var run = new RunState(config, 5);
            run.stats.battlesWon = 4;
            run.stats.elitesWon = 1;
            run.stats.bossesWon = 1;
            run.stats.goldEarned = 95;
            var lines = RunScore.Breakdown(run, false);
            int expected = 1 * RunScore.PerLayer + 4 * RunScore.PerBattle + 1 * RunScore.PerElite + 1 * RunScore.PerBoss + 95 / RunScore.GoldDivisor;
            Assert.AreEqual(expected, RunScore.Total(run, false));
            Assert.IsTrue(lines.All(l => l.points > 0), "0点の行は出さない");
            Assert.IsFalse(lines.Any(l => l.name == "踏破"), "踏破していなければ踏破の点はない");
        }

        [Test]
        public void Clear_AddsClearFastAndHpBonus()
        {
            var run = new RunState(config, 5);
            int failed = RunScore.Total(run, false);
            int cleared = RunScore.Total(run, true);
            int fast = System.Math.Max(0, RunScore.FastTurns - run.Turn) * RunScore.FastPerTurn;
            Assert.AreEqual(failed + RunScore.ClearBonus + fast + run.player.hp * RunScore.PerHpLeft, cleared);
        }

        [Test]
        public void Ascension_MultipliesTheScore()
        {
            var plain = new RunState(config, 5);
            var hard = new RunState(config, 5, null, 3);
            hard.player.hp = plain.player.hp; // 段位3は最大HPが減るので、そろえる
            hard.player.maxHp = plain.player.maxHp;
            int baseScore = RunScore.Total(plain, false);
            var lines = RunScore.Breakdown(hard, false);
            Assert.AreEqual(baseScore * 3 * RunScore.AscensionPercent / 100, lines.Last().points, "段位1つにつき+10%");
            Assert.AreEqual(baseScore + baseScore * 3 * RunScore.AscensionPercent / 100, RunScore.Total(hard, false));
        }
    }
}
