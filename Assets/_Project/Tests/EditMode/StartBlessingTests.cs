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
    /// <summary>旅立ちの祝福（ネオーの祝福風）。</summary>
    public class StartBlessingTests
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

        DiceData Curse()
        {
            var c = factory.Data("noroi", 1, 1, 1, 1, 1, 1);
            c.rarity = Rarity.Curse;
            return c;
        }

        [Test]
        public void Offer_IsTwoPlainAndOneTradeoff_SameForSameSeed()
        {
            for (int seed = 1; seed < 60; seed++)
            {
                var offer = StartBlessing.Offer(seed);
                Assert.AreEqual(3, offer.Count);
                Assert.AreEqual(3, offer.Distinct().Count());
                Assert.IsFalse(StartBlessing.IsTradeoff(offer[0]));
                Assert.IsFalse(StartBlessing.IsTradeoff(offer[1]));
                Assert.IsTrue(StartBlessing.IsTradeoff(offer[2]));
                CollectionAssert.AreEqual(offer, StartBlessing.Offer(seed));
            }
            var kinds = Enumerable.Range(1, 200).SelectMany(StartBlessing.Offer).Distinct().Count();
            Assert.AreEqual(Enum.GetValues(typeof(BlessingKind)).Length, kinds, "どの祝福もいつかは出る");
        }

        [Test]
        public void MaxHp_RaisesMaxAndCurrentHp()
        {
            var run = new RunState(config, 5);
            int max = run.player.maxHp, hp = run.player.hp;
            StartBlessing.Apply(run, BlessingKind.MaxHp);
            Assert.AreEqual(max + StartBlessing.MaxHpGain, run.player.maxHp);
            Assert.AreEqual(hp + StartBlessing.MaxHpGain, run.player.hp);
        }

        [Test]
        public void Gold_And_Charms()
        {
            var run = new RunState(config, 5);
            int gold = run.Gold;
            StartBlessing.Apply(run, BlessingKind.Gold);
            Assert.AreEqual(gold + StartBlessing.GoldGain, run.Gold);
            StartBlessing.Apply(run, BlessingKind.Charms);
            Assert.AreEqual(StartBlessing.CharmCount, run.Charms.Count);
        }

        [Test]
        public void Relic_IsCommon_RareRelicCostsMaxHp()
        {
            var a = new RunState(config, 5);
            StartBlessing.Apply(a, BlessingKind.Relic);
            Assert.AreEqual("a", a.Relics.Single().id);

            var b = new RunState(config, 5);
            int max = b.player.maxHp;
            var got = StartBlessing.Apply(b, BlessingKind.RareRelic);
            Assert.AreEqual(max - StartBlessing.RareRelicHpCost, b.player.maxHp);
            Assert.LessOrEqual(b.player.hp, b.player.maxHp);
            Assert.AreEqual("c", b.Relics.Single().id);
            Assert.AreEqual(2, got.Count);
        }

        [Test]
        public void Dice_AddsUncommon_RareDiceTakesAllGold()
        {
            var rare = factory.Data("rare", 4, 4, 5, 5, 6, 6);
            rare.rarity = Rarity.Rare;
            config.rewardDicePool.Add(rare);

            var a = new RunState(config, 5);
            StartBlessing.Apply(a, BlessingKind.Dice);
            Assert.IsTrue(a.pouch.All.Any(d => d.data.id == "uncommon"));

            var b = new RunState(config, 5);
            Assert.Greater(b.Gold, 0);
            StartBlessing.Apply(b, BlessingKind.RareDice);
            Assert.AreEqual(0, b.Gold);
            Assert.IsTrue(b.pouch.All.Any(d => d.data.id == "rare"));
        }

        [Test]
        public void CurseGold_AddsCurseDie_AndGold()
        {
            config.curseDicePool = new List<DiceData> { Curse() };
            var run = new RunState(config, 5);
            int gold = run.Gold;
            StartBlessing.Apply(run, BlessingKind.CurseGold);
            Assert.AreEqual(gold + StartBlessing.CurseGold, run.Gold);
            Assert.IsTrue(run.pouch.All.Any(d => d.data.id == "noroi"));
        }

        [Test]
        public void Apply_DoesNotChangeTheRunsOwnRandom()
        {
            var plain = new RunState(config, 11);
            foreach (BlessingKind kind in Enum.GetValues(typeof(BlessingKind)))
            {
                var blessed = new RunState(config, 11);
                StartBlessing.Apply(blessed, kind);
                CollectionAssert.AreEqual(plain.random.Counts, blessed.random.Counts, $"{kind}：祝福は別の乱数で選ぶ（旅の乱数の順番を変えない）");
            }
        }
    }
}
