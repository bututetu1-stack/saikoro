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
    /// <summary>イベント5種（仕様書 第9章）。</summary>
    public class EventTests
    {
        TestDice factory;
        Phase0Config config;
        DiceData kake, common, uncommon;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<Phase0Config>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
            config.curseDice = kake;
            common = factory.Data("c", 1, 2, 3, 4, 5, 6);
            uncommon = factory.Data("u", 1, 2, 3, 4, 5, 6);
            uncommon.rarity = Rarity.Uncommon;
            config.rewardDicePool = new List<DiceData> { common, uncommon };

            var e = ScriptableObject.CreateInstance<EngravingData>();
            e.id = "zoukyou";
            e.kind = EngravingKind.Numeric;
            e.op = NumericOp.Add;
            e.amount = 2;
            created.Add(e);
            config.engravingPool = new List<EngravingData> { e };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        RunState Run(params DiceInstance[] dice)
        {
            var run = new RunState(config, 1);
            if (dice.Length > 0)
            {
                foreach (var d in run.pouch.All.ToList()) run.pouch.Remove(d);
                foreach (var d in dice) run.pouch.Add(d);
            }
            return run;
        }

        [Test]
        public void PickEvent_NeverSameTwiceInARow()
        {
            var run = Run();
            var last = run.PickEvent();
            var seen = new HashSet<EventKind> { last };
            for (int i = 0; i < 200; i++)
            {
                var next = run.PickEvent();
                Assert.AreNotEqual(last, next);
                seen.Add(next);
                last = next;
            }
            Assert.AreEqual(5, seen.Count, "5種すべて出る");
        }

        // ---- 路地裏の賭場 ----

        [TestCase(4, true, true)]
        [TestCase(4, false, false)]
        [TestCase(3, false, true)]
        [TestCase(0, true, true)]
        public void Gamble_EvenOddPayout(int value, bool betEven, bool win)
        {
            var die = factory.Fixed(value);
            var run = Run(die, factory.Fixed(1));
            int gold = run.Gold; // 50
            var r = run.Gamble(die, betEven);

            Assert.AreEqual(win, r.win);
            Assert.AreEqual(win ? gold + 20 : gold - 20, run.Gold, "賭け金20、当たれば40");
            Assert.AreEqual(DiceState.Used, die.state, "振ったダイスは使用済み");
        }

        [Test]
        public void Gamble_NeedsBet()
        {
            var run = Run(factory.Fixed(2), factory.Fixed(1));
            run.SpendGold(run.Gold - 19);
            Assert.IsFalse(run.CanGamble);
            Assert.Throws<InvalidOperationException>(() => run.Gamble(run.pouch.All[0], true));
        }

        [Test]
        public void Gamble_LastDieRefreshes()
        {
            var die = factory.Fixed(2);
            var run = Run(die);
            var r = run.Gamble(die, true);
            Assert.IsTrue(r.refreshed);
            Assert.AreEqual(DiceState.Available, die.state);
        }

        // ---- 落ちている賽 ----

        [Test]
        public void FallenDice_PickUpIsCommon()
        {
            Assert.AreEqual(common, Run().FallenDiceCommon());
        }

        [Test]
        public void FallenDice_ExamineGivesUncommonOrCurse()
        {
            int uncommons = 0, curses = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var run = new RunState(config, seed);
                var found = run.ExamineFallenDice(out var cursed, out _);
                if (found != null)
                {
                    Assert.AreEqual(uncommon, found);
                    Assert.IsNull(cursed);
                    uncommons++;
                }
                else
                {
                    Assert.AreEqual(kake, cursed.data);
                    Assert.IsTrue(run.pouch.All.Contains(cursed), "欠け賽はその場でポーチに入る");
                    curses++;
                }
            }
            Assert.That(uncommons, Is.InRange(30, 70));
            Assert.That(curses, Is.InRange(30, 70));
        }

        [Test]
        public void FallenDice_CurseNotAddedWhenFull()
        {
            config.events.fallenUncommonPercent = 0;
            var run = Run(factory.Normal(), factory.Normal(), factory.Normal(), factory.Normal(), factory.Normal());
            var found = run.ExamineFallenDice(out var cursed, out bool rejected);

            Assert.IsNull(found);
            Assert.IsNull(cursed);
            Assert.IsTrue(rejected);
            Assert.AreEqual(5, run.pouch.All.Count);
        }

        // ---- 古びた祠 ----

        [Test]
        public void OldShrine_EngraveCostsHp()
        {
            var run = Run();
            var e = run.ShrineEngraving();
            var die = run.pouch.All[0];
            run.ShrineEngrave(die, 0, e);

            Assert.AreEqual(3, die.faces[0].value);
            Assert.AreEqual(32, run.player.hp);
        }

        [Test]
        public void OldShrine_CannotPayWhenItWouldKill()
        {
            var run = Run();
            run.player.hp = 8;
            Assert.IsFalse(run.CanPayShrine);
            Assert.Throws<InvalidOperationException>(() => run.ShrineEngrave(run.pouch.All[0], 0, run.ShrineEngraving()));
        }

        [Test]
        public void OldShrine_PrayHeals()
        {
            var run = Run();
            run.player.hp = 30;
            Assert.AreEqual(5, run.ShrinePray());
            run.player.hp = 38;
            Assert.AreEqual(2, run.ShrinePray(), "最大HPを超えない");
        }

        // ---- 狐の嫁入り ----

        [Test]
        public void FoxWedding_NextThreeMovesPlusTwo()
        {
            var run = Run(factory.Fixed(1), factory.Fixed(1), factory.Fixed(1), factory.Fixed(1), factory.Fixed(1));
            run.FollowFox();

            Assert.AreEqual(3, run.Move(run.pouch.Available.First()).value);
            Assert.AreEqual(3, run.Move(run.pouch.Available.First()).value);
            Assert.AreEqual(3, run.Move(run.pouch.Available.First()).value);
            Assert.AreEqual(0, run.MoveBonusTurns);
            Assert.AreEqual(1, run.Move(run.pouch.Available.First()).value);
        }

        [Test]
        public void FoxWedding_SeeOffGivesGold()
        {
            var run = Run();
            int gold = run.Gold;
            run.SeeOffFox();
            Assert.AreEqual(gold + 15, run.Gold);
        }

        // ---- 韋駄天の足跡 ----

        [Test]
        public void Idaten_ForcedMoveThreeStepsWithoutDice()
        {
            var die = factory.Fixed(2);
            var run = Run(die);
            var start = run.Current;
            var move = run.BeginForcedMove(3);
            while (!move.Done) run.StepMove(move);
            var result = run.FinishMove(move);

            Assert.AreEqual(3, run.board.DistanceToGoal(start) - run.board.DistanceToGoal(result.to));
            Assert.AreEqual(DiceState.Available, die.state, "ダイスは使わない");
            Assert.AreEqual(0, run.Turn, "ターンは進まない");
        }
    }
}
