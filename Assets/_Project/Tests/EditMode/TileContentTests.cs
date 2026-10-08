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
    /// <summary>マスの中身（通過マス・罠・宝箱・休憩）。</summary>
    public class TileContentTests
    {
        TestDice factory;
        GameConfig config;
        DiceData normal, high, kake;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            normal = factory.Data("normal", 1, 2, 3, 4, 5, 6);
            high = factory.Data("high", 4, 4, 5, 5, 6, 6);
            kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
            var uncommon = factory.Data("u", 1, 2, 3, 4, 5, 6);
            uncommon.rarity = Rarity.Uncommon;

            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { normal, normal, high };
            config.curseDice = kake;
            config.rewardDicePool = new List<DiceData> { normal, uncommon };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        RunState Run(int seed = 1) => new RunState(config, seed);

        // ---- 通過マスの配置 ----

        [Test]
        public void PassTiles_TwoOrThreeDistinctPerBoard([NUnit.Framework.Range(0, 49)] int seed)
        {
            var board = BranchBoardGenerator.Generate(new System.Random(seed), new LayerBoardSettings());
            var pass = board.tiles.Where(t => t.type.IsPassTile()).ToList();

            Assert.That(pass.Count, Is.InRange(2, 3));
            Assert.AreEqual(pass.Count, pass.Select(t => t.type).Distinct().Count(), "種類はすべて違う");
            Assert.IsTrue(pass.All(t => t.passEffect));
        }

        // ---- 通過マスの効果 ----

        [Test]
        public void Shrine_FiveGold_DoubleWhenStopped()
        {
            var run = Run();
            Assert.AreEqual(5, run.ApplyPassTile(new TileNode(1, TileType.Shrine), false).gold);
            Assert.AreEqual(10, run.ApplyPassTile(new TileNode(1, TileType.Shrine), true).gold);
            Assert.AreEqual(65, run.Gold);
        }

        [Test]
        public void Checkpoint_PaysTen_OrTakesFiveDamage()
        {
            var run = Run();
            var tile = new TileNode(1, TileType.Checkpoint);

            Assert.AreEqual(-10, run.ApplyPassTile(tile, false).gold);
            Assert.AreEqual(40, run.Gold);

            run.SpendGold(35); // 残り5G
            var r = run.ApplyPassTile(tile, true); // 20G は払えない
            Assert.AreEqual(10, r.damage, "止まったので 5×2");
            Assert.AreEqual(5, run.Gold, "払えないときはゴールドは減らない");
            Assert.AreEqual(30, run.player.hp);
        }

        [Test]
        public void Teahouse_HealsThree_CappedAtMax()
        {
            var run = Run();
            run.player.hp = 30;
            Assert.AreEqual(3, run.ApplyPassTile(new TileNode(1, TileType.Teahouse), false).healed);
            Assert.AreEqual(6, run.ApplyPassTile(new TileNode(1, TileType.Teahouse), true).healed);
            run.player.hp = 39;
            Assert.AreEqual(1, run.ApplyPassTile(new TileNode(1, TileType.Teahouse), false).healed);
        }

        [Test]
        public void DiceHall_ReturnsBestUsedDie()
        {
            var run = Run();
            var dice = run.pouch.All.ToList();
            run.pouch.Use(dice[0]); // 普通
            run.pouch.Use(dice[2]); // 四五六

            var r = run.ApplyPassTile(new TileNode(1, TileType.DiceHall), false);

            CollectionAssert.AreEqual(new[] { dice[2] }, r.returnedDice, "出目の平均が高い四五六賽から戻す");
            Assert.AreEqual(DiceState.Available, dice[2].state);
            Assert.AreEqual(DiceState.Used, dice[0].state);
        }

        // ---- 罠 ----

        [Test]
        public void Trap_AllThreeKindsHappen()
        {
            var kinds = new HashSet<TrapKind>();
            for (int seed = 0; seed < 60; seed++) kinds.Add(Run(seed).TriggerTrap().kind);
            CollectionAssert.AreEquivalent(new[] { TrapKind.Damage, TrapKind.Seal, TrapKind.Curse }, kinds);
        }

        [Test]
        public void Trap_EachKindHasItsEffect([NUnit.Framework.Range(0, 29)] int seed)
        {
            var run = Run(seed);
            var r = run.TriggerTrap();
            switch (r.kind)
            {
                case TrapKind.Damage:
                    Assert.AreEqual(5, r.damage);
                    Assert.AreEqual(35, run.player.hp);
                    break;
                case TrapKind.Seal:
                    Assert.IsNotNull(r.sealedDie, "ランダムに1個");
                    Assert.AreEqual(DiceState.Sealed, r.sealedDie.state);
                    Assert.AreEqual(1, run.pouch.All.Count(d => d.state == DiceState.Sealed), "封印されるのは1個だけ");
                    break;
                case TrapKind.Curse:
                    Assert.AreEqual(4, run.pouch.All.Count);
                    Assert.AreSame(kake, run.pouch.All.Last().data);
                    break;
            }
        }

        [Test]
        public void Trap_FullPouch_NeverCurses()
        {
            config.startingDice = new List<DiceData> { normal, normal, normal, normal, high };
            for (int seed = 0; seed < 40; seed++)
            {
                var run = Run(seed);
                run.pouch.Capacity = 5;
                Assert.AreNotEqual(TrapKind.Curse, run.TriggerTrap().kind);
                Assert.AreEqual(5, run.pouch.All.Count);
            }
        }

        [Test]
        public void TrapSeal_LiftsWhenNextBattleEnds()
        {
            RunState run = null;
            TrapResult r = null;
            for (int seed = 0; seed < 60 && (r == null || r.kind != TrapKind.Seal); seed++)
            {
                run = Run(seed);
                r = run.TriggerTrap();
            }
            Assert.AreEqual(TrapKind.Seal, r.kind);

            var enemy = factory.Enemy(1, new Intent(IntentType.Attack, 1));
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0));
            Assert.AreEqual(DiceState.Sealed, r.sealedDie.state, "戦闘中も封印されたまま");
            battle.Roll(run.pouch.Available.First());
            battle.Resolve();

            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(DiceState.Available, r.sealedDie.state, "戦闘が終わると解ける");
        }

        [Test]
        public void CursedDie_CannotBeReplaced()
        {
            var run = Run();
            var curse = run.AddDice(kake);
            Assert.Throws<InvalidOperationException>(() => run.ReplaceDice(curse, normal));
        }

        // ---- 宝箱 ----

        [Test]
        public void Treasure_GoldWhenNoRelics([NUnit.Framework.Range(0, 19)] int seed)
        {
            var r = Run(seed).OpenTreasure();
            Assert.IsNull(r.relic);
            Assert.That(r.gold, Is.InRange(30, 50));
        }

        [Test]
        public void Treasure_RelicAboutHalf_DiceOfferSometimes()
        {
            var relic = ScriptableObject.CreateInstance<RelicData>();
            relic.id = "r";
            created.Add(relic);
            config.relicPool = new List<RelicData> { relic };

            int relics = 0, offers = 0;
            const int n = 400;
            for (int seed = 0; seed < n; seed++)
            {
                var run = Run(seed);
                var r = run.OpenTreasure();
                if (r.relic != null)
                {
                    relics++;
                    Assert.IsFalse(run.HasRelic("r"), "受け取るかは選ぶので、まだ持っていない");
                    run.AddRelic(r.relic);
                    Assert.IsTrue(run.HasRelic("r"));
                }
                if (r.diceOffer != null)
                {
                    offers++;
                    Assert.AreNotEqual(Rarity.Common, r.diceOffer.rarity, "アンコモン以上");
                }
            }
            Assert.AreEqual(0.5, relics / (double)n, 0.08, "レリックは半分くらい");
            Assert.AreEqual(0.1, offers / (double)n, 0.05, "ダイスは10%くらい");
        }

        // ---- 休憩 ----

        [Test]
        public void RestHealAmount_IsThirtyPercent()
        {
            Assert.AreEqual(12, Run().RestHealAmount);
        }
    }
}
