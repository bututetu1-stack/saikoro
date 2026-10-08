using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    public class RewardTests
    {
        TestDice factory;
        GameConfig config;
        List<DiceData> pool;
        RewardSettings settings;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            settings = new RewardSettings();
            pool = new List<DiceData>
            {
                Rare("c1", Rarity.Common), Rare("c2", Rarity.Common), Rare("c3", Rarity.Common), Rare("c4", Rarity.Common),
                Rare("u1", Rarity.Uncommon), Rare("u2", Rarity.Uncommon),
                Rare("r1", Rarity.Rare),
            };
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.startingDice = new List<DiceData> { pool[0], pool[1], pool[2], pool[3] };
            config.rewardDicePool = pool;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
            factory.DestroyAll();
        }

        DiceData Rare(string id, Rarity rarity)
        {
            var d = factory.Data(id, 1, 2, 3, 4, 5, 6);
            d.rarity = rarity;
            return d;
        }

        // ---- RewardGenerator ----

        [Test]
        public void Gold_InRangeByKind()
        {
            var rng = new System.Random(1);
            for (int i = 0; i < 200; i++)
            {
                int normal = RewardGenerator.ForBattle(RewardKind.Normal, rng, settings, pool).gold;
                int elite = RewardGenerator.ForBattle(RewardKind.Elite, rng, settings, pool).gold;
                Assert.That(normal, Is.InRange(12, 18));
                Assert.That(elite, Is.InRange(30, 40));
            }
            Assert.AreEqual(60, RewardGenerator.ForBattle(RewardKind.Boss, rng, settings, pool).gold);
        }

        [Test]
        public void DiceChoices_ThreeDistinctFromPool([NUnit.Framework.Range(0, 49)] int seed)
        {
            var reward = RewardGenerator.ForBattle(RewardKind.Normal, new System.Random(seed), settings, pool);

            Assert.AreEqual(3, reward.diceChoices.Count);
            Assert.AreEqual(3, reward.diceChoices.Distinct().Count(), "同じダイスは並ばない");
            CollectionAssert.IsSubsetOf(reward.diceChoices, pool);
        }

        [Test]
        public void SameSeed_SameReward()
        {
            var a = RewardGenerator.ForBattle(RewardKind.Normal, new System.Random(7), settings, pool);
            var b = RewardGenerator.ForBattle(RewardKind.Normal, new System.Random(7), settings, pool);

            Assert.AreEqual(a.gold, b.gold);
            CollectionAssert.AreEqual(a.diceChoices, b.diceChoices);
        }

        [Test]
        public void RarityWeights_NormalIs70_25_5()
        {
            var rng = new System.Random(3);
            var counts = new Dictionary<Rarity, int> { [Rarity.Common] = 0, [Rarity.Uncommon] = 0, [Rarity.Rare] = 0 };
            const int n = 5000;
            for (int i = 0; i < n; i++) counts[RewardGenerator.RollRarity(rng, settings.normalRarityWeights)]++;

            Assert.AreEqual(0.70, counts[Rarity.Common] / (double)n, 0.03);
            Assert.AreEqual(0.25, counts[Rarity.Uncommon] / (double)n, 0.03);
            Assert.AreEqual(0.05, counts[Rarity.Rare] / (double)n, 0.015);
        }

        [Test]
        public void Boss_OnlyOneRare_FillsWithNextBestRarity()
        {
            var reward = RewardGenerator.ForBattle(RewardKind.Boss, new System.Random(1), settings, pool);

            Assert.AreEqual(3, reward.diceChoices.Count);
            Assert.AreEqual(Rarity.Rare, reward.diceChoices[0].rarity, "レアは1つしかないので最初にそれ");
            Assert.IsTrue(reward.diceChoices.Skip(1).All(d => d.rarity == Rarity.Uncommon), "残りはレアに近いアンコモン");
        }

        [Test]
        public void CursedDiceNeverOffered()
        {
            var cursed = Rare("curse", Rarity.Curse);
            var withCurse = new List<DiceData>(pool) { cursed };
            for (int seed = 0; seed < 50; seed++)
            {
                CollectionAssert.DoesNotContain(RewardGenerator.ForBattle(RewardKind.Normal, new System.Random(seed), settings, withCurse).diceChoices, cursed);
            }
        }

        // ---- RunState ----

        [Test]
        public void AddDice_UntilFull_ThenReplace()
        {
            var run = new RunState(config, 1);
            run.pouch.Capacity = 5;
            Assert.IsTrue(run.CanAddDice);

            run.AddDice(pool[4]);
            Assert.AreEqual(5, run.pouch.All.Count);
            Assert.IsFalse(run.CanAddDice, "5枠で満杯");

            var old = run.pouch.All[0];
            run.ReplaceDice(old, pool[6]);
            Assert.AreEqual(5, run.pouch.All.Count);
            CollectionAssert.DoesNotContain(run.pouch.All, old);
            Assert.IsTrue(run.pouch.All.Any(d => d.data == pool[6]));
        }

        [Test]
        public void SkipDiceReward_GivesTenGold()
        {
            var run = new RunState(config, 1);

            Assert.AreEqual(10, run.SkipDiceReward());
            Assert.AreEqual(60, run.Gold);
        }

        [Test]
        public void CreateBattleReward_UsesRewardStream()
        {
            var a = new RunState(config, 42);
            var b = new RunState(config, 42);
            for (int i = 0; i < 5; i++) b.random.Battle.Next(); // 戦闘の乱数を余計に使っても

            CollectionAssert.AreEqual(a.CreateBattleReward(RewardKind.Normal).diceChoices, b.CreateBattleReward(RewardKind.Normal).diceChoices);
        }

        [Test]
        public void PouchRemove_LastAvailable_Refreshes()
        {
            var pouchDice = new DicePouch();
            var a = new DiceInstance(pool[0]);
            var b = new DiceInstance(pool[1]);
            pouchDice.Add(a);
            pouchDice.Add(b);
            pouchDice.Use(a);

            pouchDice.Remove(b);

            Assert.AreEqual(DiceState.Available, a.state, "使用可能が0個になったのでリフレッシュ");
        }
    }
}
