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
    /// <summary>今日の旅（日付で決まるシードとお題）。</summary>
    public class DailyChallengeTests
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
        public void Seed_IsSameForTheSameDay_AndDiffersByDay()
        {
            var day = new DateTime(2026, 10, 16, 1, 0, 0);
            Assert.AreEqual(DailyChallenge.SeedOf(day), DailyChallenge.SeedOf(day.AddHours(20)));
            Assert.AreNotEqual(DailyChallenge.SeedOf(day), DailyChallenge.SeedOf(day.AddDays(1)));
            for (int i = 0; i < 400; i++) Assert.Greater(DailyChallenge.SeedOf(day.AddDays(i)), 0);
            Assert.AreEqual("20261016", DailyChallenge.KeyOf(day));
            Assert.AreEqual(day.Date, DailyChallenge.DateOf("20261016"));
        }

        [Test]
        public void Modifier_VariesOverDays()
        {
            var start = new DateTime(2026, 1, 1);
            var seen = Enumerable.Range(0, 60).Select(i => DailyChallenge.ModifierOf(start.AddDays(i))).Distinct().Count();
            Assert.Greater(seen, 2, "日によってお題が変わる");
        }

        [Test]
        public void Fortune_AddsGold()
        {
            var run = new RunState(config, 7);
            int before = run.Gold;
            DailyChallenge.Apply(run, DailyChallenge.Modifier.Fortune);
            Assert.AreEqual(before + DailyChallenge.FortuneGold, run.Gold);
        }

        [Test]
        public void Treasure_AddsCommonOrUncommonRelic_SameForSameSeed()
        {
            var a = new RunState(config, 7);
            var b = new RunState(config, 7);
            var gotA = DailyChallenge.Apply(a, DailyChallenge.Modifier.Treasure);
            var gotB = DailyChallenge.Apply(b, DailyChallenge.Modifier.Treasure);
            Assert.AreEqual(1, a.Relics.Count);
            Assert.AreNotEqual(Rarity.Rare, a.Relics[0].rarity);
            CollectionAssert.AreEqual(gotA, gotB, "同じ日（同じシード）なら同じものを得る");
        }

        [Test]
        public void Charms_FillsCharmSlots()
        {
            var run = new RunState(config, 3);
            DailyChallenge.Apply(run, DailyChallenge.Modifier.Charms);
            Assert.AreEqual(RunState.MaxCharms, run.Charms.Count);
        }

        [Test]
        public void ExtraDie_AddsUncommonDie()
        {
            var run = new RunState(config, 3);
            int before = run.pouch.All.Count();
            DailyChallenge.Apply(run, DailyChallenge.Modifier.ExtraDie);
            Assert.AreEqual(before + 1, run.pouch.All.Count());
            Assert.IsTrue(run.pouch.All.Any(d => d.data.id == "uncommon"));
        }

        [Test]
        public void Hardship_LowersMaxHp_ButGivesRelicAndGold()
        {
            var run = new RunState(config, 3);
            int maxHp = run.player.maxHp;
            int gold = run.Gold;
            DailyChallenge.Apply(run, DailyChallenge.Modifier.Hardship);
            Assert.AreEqual(maxHp - DailyChallenge.HardshipHp, run.player.maxHp);
            Assert.LessOrEqual(run.player.hp, run.player.maxHp);
            Assert.AreEqual(1, run.Relics.Count);
            Assert.AreEqual(gold + DailyChallenge.HardshipGold, run.Gold);
        }

        [Test]
        public void Apply_DoesNotChangeTheRunsOwnRandom()
        {
            var plain = new RunState(config, 11);
            var daily = new RunState(config, 11);
            DailyChallenge.Apply(daily, DailyChallenge.Modifier.Treasure);
            CollectionAssert.AreEqual(plain.random.Counts, daily.random.Counts, "お題は別の乱数で選ぶ（旅の乱数の順番を変えない）");
        }
    }
}
