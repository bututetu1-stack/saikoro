using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaiNoMichi.Tests
{
    /// <summary>ショップ（仕様書 第11章）。</summary>
    public class ShopTests
    {
        TestDice factory;
        GameConfig config;
        DiceData normal;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            normal = factory.Data("normal", 1, 2, 3, 4, 5, 6);
            normal.price = 50;
            config.startingDice = new List<DiceData> { normal, normal, normal };

            var pool = new List<DiceData>();
            foreach (var (id, rarity, price) in new[] { ("c", Rarity.Common, 50), ("u", Rarity.Uncommon, 80), ("r", Rarity.Rare, 130), ("c2", Rarity.Common, 50) })
            {
                var d = factory.Data(id, 1, 2, 3, 4, 5, 6);
                d.rarity = rarity;
                d.price = price;
                pool.Add(d);
            }
            config.rewardDicePool = pool;

            config.relicPool = new List<RelicData>();
            foreach (var (id, rarity) in new[] { ("r1", Rarity.Common), ("r2", Rarity.Uncommon), ("r3", Rarity.Rare) })
            {
                var r = ScriptableObject.CreateInstance<RelicData>();
                r.id = id;
                r.displayName = id;
                r.rarity = rarity;
                created.Add(r);
                config.relicPool.Add(r);
            }

            config.engravingPool = new List<EngravingData>();
            foreach (var id in new[] { "e1", "e2", "e3" })
            {
                var e = ScriptableObject.CreateInstance<EngravingData>();
                e.id = id;
                e.displayName = id;
                e.kind = EngravingKind.Numeric;
                e.op = NumericOp.Add;
                e.amount = 2;
                e.price = 60;
                created.Add(e);
                config.engravingPool.Add(e);
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        RunState Run(int gold = 1000)
        {
            var run = new RunState(config, 1);
            run.GainGold(gold);
            return run;
        }

        [Test]
        public void Stock_ThreeDiceTwoRelicsTwoEngravings_OneDiscounted([NUnit.Framework.Range(0, 19)] int seed)
        {
            var run = new RunState(config, seed);
            var shop = run.CreateShop();

            Assert.AreEqual(3, shop.items.Count(i => i.kind == ShopItemKind.Dice));
            Assert.AreEqual(2, shop.items.Count(i => i.kind == ShopItemKind.Relic));
            Assert.AreEqual(2, shop.items.Count(i => i.kind == ShopItemKind.Engraving));
            Assert.AreEqual(1, shop.items.Count(i => i.discounted));
            Assert.AreEqual(shop.items.Count, shop.items.Select(i => (object)i.dice ?? (object)i.relic ?? i.engraving).Distinct().Count(), "同じ品物は並ばない");
        }

        [Test]
        public void Prices_FollowSpecTable_DiscountIsHalf([NUnit.Framework.Range(0, 19)] int seed)
        {
            var shop = new RunState(config, seed).CreateShop();
            int[] relicPrices = { 120, 160, 220 };
            foreach (var item in shop.items)
            {
                int full = item.kind == ShopItemKind.Dice ? item.dice.price
                    : item.kind == ShopItemKind.Relic ? relicPrices[(int)item.relic.rarity]
                    : item.engraving.price;
                Assert.AreEqual(item.discounted ? full / 2 : full, item.price, item.DisplayName);
            }
        }

        [Test]
        public void Relics_OwnedOnesAreNotSold()
        {
            var run = Run();
            run.AddRelic(config.relicPool[0]);
            var shop = run.CreateShop();

            CollectionAssert.DoesNotContain(shop.items.Select(i => i.relic).ToList(), config.relicPool[0]);
        }

        [Test]
        public void BuyRelic_PaysAndAdds()
        {
            var run = Run(200);
            var shop = run.CreateShop();
            var item = shop.items.First(i => i.kind == ShopItemKind.Relic);
            int gold = run.Gold;
            shop.BuyRelic(item);

            Assert.AreEqual(gold - item.price, run.Gold);
            Assert.IsTrue(run.Relics.Contains(item.relic));
            Assert.IsTrue(item.sold);
            Assert.Throws<InvalidOperationException>(() => shop.BuyRelic(item), "売り切れ");
        }

        [Test]
        public void Buy_NotEnoughGold_Throws()
        {
            var run = new RunState(config, 1); // 所持金 50G（初期値）
            var shop = run.CreateShop();
            var item = shop.items.First(i => i.kind == ShopItemKind.Relic && i.price > run.Gold);

            Assert.IsFalse(shop.CanAfford(item));
            Assert.Throws<InvalidOperationException>(() => shop.BuyRelic(item));
            Assert.IsFalse(item.sold);
        }

        [Test]
        public void BuyDice_AddsOrReplacesWhenFull()
        {
            var run = Run();
            var shop = run.CreateShop();
            var dice = shop.items.Where(i => i.kind == ShopItemKind.Dice).ToList();
            shop.BuyDice(dice[0]);
            shop.BuyDice(dice[1]);
            Assert.AreEqual(5, run.pouch.All.Count);

            Assert.Throws<InvalidOperationException>(() => shop.BuyDice(dice[2]), "満杯なら入れ替えるダイスが必要");
            var old = run.pouch.All[0];
            shop.BuyDice(dice[2], old);
            Assert.AreEqual(5, run.pouch.All.Count);
            Assert.IsFalse(run.pouch.All.Contains(old));
            Assert.IsTrue(run.pouch.All.Any(d => d.data == dice[2].dice));
        }

        [Test]
        public void BuyEngraving_AppliesToFace()
        {
            var run = Run();
            var shop = run.CreateShop();
            var item = shop.items.First(i => i.kind == ShopItemKind.Engraving);
            var die = run.pouch.All[0];
            shop.BuyEngraving(item, die, 0);

            Assert.AreEqual(3, die.faces[0].value, "1 + 2");
            Assert.IsTrue(item.sold);
        }

        [Test]
        public void Remove_PriceRisesBy25()
        {
            config.startingDice = new List<DiceData> { normal, normal, normal, normal, normal };
            var run = Run(1000);
            var shop = run.CreateShop();

            Assert.AreEqual(75, shop.RemovePrice);
            int gold = run.Gold;
            shop.RemoveDice(run.pouch.All[0]);
            Assert.AreEqual(gold - 75, run.Gold);
            Assert.AreEqual(4, run.pouch.All.Count);
            Assert.AreEqual(100, shop.RemovePrice);
            shop.RemoveDice(run.pouch.All[0]);
            Assert.AreEqual(125, run.CreateShop().RemovePrice, "値段の上がり方はショップをまたいで続く");
        }

        [Test]
        public void Remove_CannotGoBelowTwoDice()
        {
            config.startingDice = new List<DiceData> { normal, normal };
            var run = Run();
            var shop = run.CreateShop();

            Assert.IsFalse(shop.CanRemove);
            Assert.Throws<InvalidOperationException>(() => shop.RemoveDice(run.pouch.All[0]));
        }

        [Test]
        public void Remove_CurseDiceCanBeRemoved()
        {
            var curse = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            curse.rarity = Rarity.Curse;
            var run = Run();
            var die = run.AddDice(curse);
            run.CreateShop().RemoveDice(die);

            Assert.IsFalse(run.pouch.All.Contains(die));
        }
    }
}
