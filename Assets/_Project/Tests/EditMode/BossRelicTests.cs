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
    /// <summary>ボスレリック5種（仕様書 第10章）。</summary>
    public class BossRelicTests
    {
        TestDice factory;
        GameConfig config;
        DiceData kake;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("two", 2, 2, 2, 2, 2, 2) };
            kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        T Fx<T>(Trigger trigger) where T : EffectSO
        {
            var e = ScriptableObject.CreateInstance<T>();
            e.trigger = trigger;
            created.Add(e);
            return e;
        }

        RelicData Relic(params EffectSO[] effects)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.isBoss = true;
            r.effects = new List<EffectSO>(effects);
            created.Add(r);
            return r;
        }

        BattleState Battle(RunState run) =>
            new BattleState(run.player, factory.Enemy(50, new Intent(IntentType.Attack, 1)), run.pouch, new System.Random(0), run.effects, run);

        [Test]
        public void GoldenCup_WhenPouchWasFull_CanStillReplace()
        {
            var pouch = Fx<PouchCapacityEffect>(Trigger.OnAcquire);
            pouch.amount = -1;
            var run = new RunState(config, 1);
            while (run.CanAddDice) run.AddDice(config.startingDice[0]);
            run.AddRelic(Relic(pouch));
            Assert.Greater(run.pouch.All.Count, run.pouch.Capacity, "容量より多く持っている");

            int count = run.pouch.All.Count;
            var got = run.ReplaceDice(run.pouch.All[0], config.startingDice[1]);
            Assert.IsTrue(run.pouch.All.Contains(got));
            Assert.AreEqual(count, run.pouch.All.Count, "入れ替えでは数は変わらない");
            Assert.IsFalse(run.CanAddDice, "増やすことはできない");
        }

        [Test]
        public void GoldenCup_MoreDice_SmallerPouch()
        {
            var dice = Fx<DicePerRoundEffect>(Trigger.OnBattleStart);
            var pouch = Fx<PouchCapacityEffect>(Trigger.OnAcquire);
            pouch.amount = -1;
            var run = new RunState(config, 1);
            run.AddRelic(Relic(dice, pouch));

            Assert.AreEqual(DicePouch.DefaultCapacity - 1, run.pouch.Capacity);
            Assert.AreEqual(4, Battle(run).MaxDicePerRound);
        }

        [Test]
        public void HeavyCrown_AllRollsPlusOne_NoRestHeal()
        {
            var plus = Fx<AddValueEffect>(Trigger.OnRoll);
            plus.add = 1;
            var rule = Fx<RuleEffect>(Trigger.OnAcquire);
            rule.rule = RunRule.NoRestHeal;
            var run = new RunState(config, 1);
            run.AddRelic(Relic(plus, rule));

            Assert.AreEqual(3, run.Move(run.pouch.All[1]).value, "出目2 + 1");
            run.player.hp = 10;
            Assert.AreEqual(0, run.RestHealAmount);
            Assert.AreEqual(0, run.Rest());
        }

        [Test]
        public void CursedBoard_DoubleGold_TempCurseInBattle()
        {
            var gold = Fx<ScaleEffect>(Trigger.OnGoldGain);
            gold.target = ScaleTarget.Amount;
            gold.percent = 200;
            var curse = Fx<TempDiceEffect>(Trigger.OnBattleStart);
            curse.dice = kake;
            var run = new RunState(config, 1);
            run.AddRelic(Relic(gold, curse));

            Assert.AreEqual(20, run.GainGold(10));
            var battle = Battle(run);
            Assert.AreEqual(3, run.pouch.All.Count, "戦闘中は欠け賽が加わる");
            Assert.AreEqual(1, battle.TemporaryDice.Count);

            battle.Roll(run.pouch.All[0]);
            battle.enemy.hp = 1;
            battle.Resolve();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(2, run.pouch.All.Count, "戦闘が終わると消える");
            Assert.IsFalse(run.pouch.All.Any(d => d.data == kake));
        }

        [Test]
        public void SandOfTime_ReturnsUsedDieEachRound_MaxHpMinus10()
        {
            var ret = Fx<ReturnUsedDieEffect>(Trigger.OnRoundStart);
            var hp = Fx<MaxHpEffect>(Trigger.OnAcquire);
            hp.amount = -10;
            config.startingDice.Add(factory.Data("three", 3, 3, 3, 3, 3, 3));
            var run = new RunState(config, 1);
            run.AddRelic(Relic(ret, hp));
            Assert.AreEqual(30, run.player.maxHp);
            Assert.AreEqual(30, run.player.hp);

            var battle = Battle(run);
            battle.Roll(run.pouch.All[0]);
            battle.Roll(run.pouch.All[1]);
            Assert.AreEqual(1, run.pouch.AvailableCount);
            battle.Resolve();
            Assert.AreEqual(2, run.pouch.AvailableCount, "次のラウンドの始めに1個戻る");
        }

        [Test]
        public void Bracer_BoughtDiceEngraved_ShopHalved()
        {
            var engraving = ScriptableObject.CreateInstance<EngravingData>();
            engraving.kind = EngravingKind.Numeric;
            engraving.op = NumericOp.Add;
            engraving.amount = 2;
            engraving.displayName = "増強";
            created.Add(engraving);
            config.engravingPool = new List<EngravingData> { engraving };
            var dice = factory.Data("c", 1, 1, 1, 1, 1, 1);
            dice.price = 50;
            config.rewardDicePool = new List<DiceData> { dice, factory.Data("c2", 1, 2, 3, 4, 5, 6), factory.Data("c3", 1, 2, 3, 4, 5, 6) };
            var a = Fx<RuleEffect>(Trigger.OnAcquire);
            a.rule = RunRule.EngraveBoughtDice;
            var b = Fx<RuleEffect>(Trigger.OnAcquire);
            b.rule = RunRule.HalfShopStock;
            var run = new RunState(config, 1);
            run.GainGold(500);
            run.AddRelic(Relic(a, b));

            var shop = run.CreateShop();
            Assert.AreEqual(2, shop.items.Count(i => i.kind == ShopItemKind.Dice), "3 → 2");
            Assert.AreEqual(1, shop.items.Count(i => i.kind == ShopItemKind.Engraving), "2 → 1");

            var item = shop.items.First(i => i.kind == ShopItemKind.Dice);
            var bought = shop.BuyDice(item);
            Assert.AreEqual(engraving, shop.LastAutoEngraving);
            Assert.AreEqual(bought.data.faceValues.Sum() + 2, bought.faces.Sum(f => f.value), "どこか1面に増強（+2）");
        }

        [Test]
        public void BossOffer_ThreeNotOwned()
        {
            config.bossRelicPool = Enumerable.Range(0, 5).Select(_ => Relic()).ToList();
            var run = new RunState(config, 1);
            run.AddRelic(config.bossRelicPool[0]);
            var offer = run.CreateBossRelicOffer();

            Assert.AreEqual(3, offer.Count);
            CollectionAssert.DoesNotContain(offer, config.bossRelicPool[0]);
            Assert.AreEqual(3, offer.Distinct().Count());
        }
    }
}
