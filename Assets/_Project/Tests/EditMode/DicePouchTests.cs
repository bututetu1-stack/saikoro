using System;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Tests
{
    public class DicePouchTests
    {
        TestDice factory;
        DicePouch pouch;
        DiceInstance a, b, c, d;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            pouch = new DicePouch();
            // フェーズ0の初期ポーチ：普通×2、四五六×1、一二三×1
            a = factory.Normal();
            b = factory.Normal();
            c = factory.High();
            d = factory.Low();
            pouch.Add(a);
            pouch.Add(b);
            pouch.Add(c);
            pouch.Add(d);
        }

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        [Test]
        public void Pinzoro_RefreshesWhenOnlyPinzoroIsLeft()
        {
            var pinzoroData = factory.Data("pinzoro", 1, 1, 1, 1, 1, 1);
            pinzoroData.keepAvailable = true;
            var pinzoro = new DiceInstance(pinzoroData);
            pouch.Add(pinzoro);

            pouch.Use(a);
            pouch.Use(b);
            pouch.Use(c);
            Assert.AreEqual(1, pouch.UsesUntilRefresh, "ピンゾロ賽は数えない");
            Assert.IsFalse(pouch.Use(pinzoro), "ほかに使えるダイスが残っていればリフレッシュしない");

            // 最後の普通のダイスを使うと、残りがピンゾロ賽だけになるのでリフレッシュ
            Assert.IsTrue(pouch.Use(d));
            Assert.IsTrue(pouch.All.All(x => x.state == DiceState.Available));
        }

        [Test]
        public void Pinzoro_OnlyPinzoroAvailable_UsingItRefreshes()
        {
            var pinzoroData = factory.Data("pinzoro", 1, 1, 1, 1, 1, 1);
            pinzoroData.keepAvailable = true;
            var pinzoro = new DiceInstance(pinzoroData);
            pouch.Add(pinzoro);
            // 封印などで、使用可能なのがピンゾロ賽だけになっていた場合
            foreach (var x in new[] { a, b, c, d }) x.state = DiceState.Used;

            Assert.IsTrue(pouch.Use(pinzoro), "ピンゾロ賽を使えば、いつまでも戻らない状態にならない");
            Assert.AreEqual(5, pouch.AvailableCount);
        }

        [Test]
        public void Use_MarksDiceUsed_WithoutRefreshWhileOthersAvailable()
        {
            bool refreshed = pouch.Use(a);

            Assert.IsFalse(refreshed);
            Assert.AreEqual(DiceState.Used, a.state);
            Assert.AreEqual(3, pouch.AvailableCount);
        }

        [Test]
        public void Use_LastAvailable_RefreshesAllImmediately()
        {
            int refreshEvents = 0;
            pouch.Refreshed += () => refreshEvents++;

            pouch.Use(a);
            pouch.Use(b);
            pouch.Use(c);
            bool refreshed = pouch.Use(d);

            Assert.IsTrue(refreshed);
            Assert.AreEqual(1, refreshEvents);
            Assert.IsTrue(pouch.All.All(x => x.state == DiceState.Available));
        }

        [Test]
        public void Refresh_DoesNotReleaseSealedDice()
        {
            c.state = DiceState.Sealed;

            pouch.Use(a);
            pouch.Use(b);
            bool refreshed = pouch.Use(d);

            Assert.IsTrue(refreshed);
            Assert.AreEqual(DiceState.Sealed, c.state);
            Assert.AreEqual(3, pouch.AvailableCount);
        }

        [Test]
        public void Refresh_AllowsReusingTheDieJustRolled()
        {
            // 仕様書 第3章：最後の1個を振ってリフレッシュしたら、そのダイスもすぐ選び直せる
            pouch.Use(a);
            pouch.Use(b);
            pouch.Use(c);
            pouch.Use(d);

            Assert.DoesNotThrow(() => pouch.Use(d));
        }

        [Test]
        public void Use_UsedDie_Throws()
        {
            pouch.Use(a);
            Assert.Throws<InvalidOperationException>(() => pouch.Use(a));
        }

        [Test]
        public void Add_WhenFull_Throws()
        {
            pouch.Add(factory.Normal()); // 5個目で満杯
            Assert.IsTrue(pouch.IsFull);
            Assert.Throws<InvalidOperationException>(() => pouch.Add(factory.Normal()));
        }
    }
}
