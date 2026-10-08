using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaiNoMichi.Tests
{
    /// <summary>フェーズ2で足したイベント7種と、出る層・1ランに1回（仕様書 第9章）。</summary>
    public class EventPhase2Tests
    {
        TestDice factory;
        GameConfig config;
        DiceData kake, commonA, commonB, uncommon;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.useBranchingBoard = true;
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("six", 6, 6, 6, 6, 6, 6), factory.Data("one", 1, 1, 1, 1, 1, 1) };
            kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
            config.curseDice = kake;
            commonA = factory.Data("ca", 1, 2, 3, 4, 5, 6);
            commonB = factory.Data("cb", 2, 2, 3, 3, 4, 4);
            uncommon = factory.Data("u", 1, 2, 3, 4, 5, 6);
            uncommon.rarity = Rarity.Uncommon;
            config.rewardDicePool = new List<DiceData> { commonA, commonB, uncommon };
            var e1 = factory.Enemy(10, new Intent(IntentType.Attack, 1));
            var e2 = factory.Enemy(10, new Intent(IntentType.Attack, 1));
            var e3 = factory.Enemy(10, new Intent(IntentType.Attack, 1));
            config.layers = new List<LayerData>
            {
                new LayerData { displayName = "一", battleEnemies = new List<EnemyData> { e1 }, boss = e1 },
                new LayerData { displayName = "二", battleEnemies = new List<EnemyData> { e2 }, boss = e2 },
                new LayerData { displayName = "三", battleEnemies = new List<EnemyData> { e3 }, boss = e3 },
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        RelicData Relic(string id)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.id = id;
            r.displayName = id;
            created.Add(r);
            return r;
        }

        DiceInstance Die(RunState run, string id) => run.pouch.All.First(d => d.data.id == id);

        // ---- 出る層・1ランに1回 ----

        [Test]
        public void LayerRestrictions()
        {
            var run = new RunState(config, 1);
            Assert.IsFalse(run.EventAllowed(EventKind.TwinStatues), "道祖神は第2層から");
            Assert.IsFalse(run.EventAllowed(EventKind.OniDice), "鬼の賽勝負は第2層から");
            Assert.IsTrue(run.EventAllowed(EventKind.StartOverCard));
            run.AdvanceLayer();
            Assert.IsTrue(run.EventAllowed(EventKind.TwinStatues));
            Assert.IsTrue(run.EventAllowed(EventKind.OniDice));
            Assert.IsTrue(run.EventAllowed(EventKind.StartOverCard));
            run.AdvanceLayer();
            Assert.IsFalse(run.EventAllowed(EventKind.StartOverCard), "振り出しの札は第1・第2層だけ");
        }

        [Test]
        public void StartOverCard_OncePerRun()
        {
            config.events.kinds = new List<EventKind> { EventKind.StartOverCard, EventKind.Gamble };
            var run = new RunState(config, 1);
            int count = 0;
            for (int i = 0; i < 30; i++)
            {
                if (run.PickEvent() == EventKind.StartOverCard) count++;
            }
            Assert.AreEqual(1, count);
        }

        [Test]
        public void PickEvent_NeverPicksRestrictedOnLayer1()
        {
            var run = new RunState(config, 3);
            for (int i = 0; i < 200; i++)
            {
                var kind = run.PickEvent();
                Assert.AreNotEqual(EventKind.TwinStatues, kind);
                Assert.AreNotEqual(EventKind.OniDice, kind);
            }
        }

        // ---- 流しの職人 ----

        [Test]
        public void Craftsman_SetsFaceValue_KeepsEngraving()
        {
            var run = new RunState(config, 1);
            run.GainGold(30);
            var die = Die(run, "normal");
            var e = ScriptableObject.CreateInstance<EngravingData>();
            created.Add(e);
            var face = die.faces[0];
            face.engraving = e;
            die.faces[0] = face;
            int gold = run.Gold;
            run.Craft(die, 0, 6);
            Assert.AreEqual(6, die.faces[0].value);
            Assert.AreSame(e, die.faces[0].engraving);
            Assert.AreEqual(gold - 30, run.Gold);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Craft(die, 1, 7));
        }

        [Test]
        public void Craftsman_NeedsGold()
        {
            var run = new RunState(config, 1);
            run.SpendGold(run.Gold);
            Assert.IsFalse(run.CanCraft);
            Assert.Throws<InvalidOperationException>(() => run.Craft(Die(run, "normal"), 0, 6));
        }

        // ---- 道祖神の双子像 ----

        [Test]
        public void TwinStatues_OfferOneDuplicateAnother_WithEngravings()
        {
            var run = new RunState(config, 1);
            var copy = Die(run, "normal");
            var face = copy.faces[2];
            face.value = 9;
            copy.faces[2] = face;
            var twin = run.OfferAndDuplicate(Die(run, "one"), copy);
            Assert.AreEqual(3, run.pouch.All.Count);
            Assert.IsFalse(run.pouch.All.Any(d => d.data.id == "one"));
            Assert.AreEqual(2, run.pouch.All.Count(d => d.data.id == "normal"));
            Assert.AreEqual(9, twin.faces[2].value, "書き換えた面ごと写す");
            Assert.AreNotSame(copy, twin);
        }

        [Test]
        public void TwinStatues_CannotDuplicateCurseOrSelf()
        {
            var run = new RunState(config, 1);
            var curse = run.ForceCurse();
            Assert.Throws<InvalidOperationException>(() => run.OfferAndDuplicate(Die(run, "one"), curse));
            Assert.Throws<ArgumentException>(() => run.OfferAndDuplicate(Die(run, "one"), Die(run, "one")));
        }

        // ---- 落とし穴 ----

        [Test]
        public void Pitfall_AvoidOnFourOrMore()
        {
            var run = new RunState(config, 1);
            var r = run.Pitfall(Die(run, "six"));
            Assert.IsTrue(r.avoided);
            Assert.AreEqual(40, run.player.hp);
            var miss = run.Pitfall(Die(run, "one"));
            Assert.IsFalse(miss.avoided);
            Assert.AreEqual(6, miss.damage);
            Assert.AreEqual(34, run.player.hp);
        }

        // ---- 旅の商人 ----

        [Test]
        public void Merchant_TradesForSameRarityDifferentDice()
        {
            config.startingDice = new List<DiceData> { commonA, uncommon, factory.Data("x", 1, 1, 1, 1, 1, 1) };
            for (int seed = 0; seed < 10; seed++)
            {
                var run = new RunState(config, seed);
                var give = run.pouch.All.First(d => d.data == commonA);
                Assert.IsTrue(run.CanTrade(give));
                var got = run.Trade(give);
                Assert.AreSame(commonB, got.data, "コモンの別のダイス");
                Assert.AreEqual(3, run.pouch.All.Count);
                Assert.IsFalse(run.CanTrade(run.pouch.All.First(d => d.data == uncommon)), "同じレア度のほかのダイスがない");
            }
        }

        // ---- 鬼の賽勝負 ----

        [Test]
        public void Oni_WinGivesRelic_LoseTakesDie()
        {
            var relic = Relic("r1");
            config.relicPool = new List<RelicData> { relic };
            var run = new RunState(config, 1);
            var six = Die(run, "six");
            var r = run.PlayOni(six);
            // 6 は鬼の 1〜5 に勝ち、6 なら引き分け
            if (r.oniValue < 6)
            {
                Assert.IsTrue(r.win);
                Assert.AreSame(relic, r.relic);
                Assert.IsTrue(run.Relics.Contains(relic));
            }
            else Assert.IsTrue(r.draw);
            Assert.IsTrue(run.pouch.All.Contains(six), "勝っても引き分けでもダイスは残る");

            // 1 は鬼の 2〜6 に負ける
            config.startingDice.Add(factory.Data("one2", 1, 1, 1, 1, 1, 1));
            for (int seed = 0; seed < 20; seed++)
            {
                var run2 = new RunState(config, seed);
                var one = Die(run2, "one");
                var r2 = run2.PlayOni(one);
                if (r2.oniValue == 1) Assert.IsTrue(r2.draw);
                else
                {
                    Assert.IsFalse(r2.win);
                    Assert.IsFalse(run2.pouch.All.Contains(one), "負けたらダイスを奪われる");
                    return;
                }
            }
            Assert.Fail("20 回とも引き分けにはならないはず");
        }

        [Test]
        public void Oni_CannotBetWhenPouchAtMinimumOrCurse()
        {
            config.startingDice = new List<DiceData> { factory.Data("a", 1, 2, 3, 4, 5, 6), factory.Data("b", 1, 2, 3, 4, 5, 6) };
            var run = new RunState(config, 1);
            Assert.IsFalse(run.CanPlayOni, "2個しかないときは賭けられない");
            var curse = run.ForceCurse();
            Assert.IsFalse(run.CanBetOni(curse));
            Assert.IsTrue(run.CanBetOni(Die(run, "a")));
        }

        // ---- 迷子の子ども ----

        [Test]
        public void LostChild_GuideUsesDie_GivesGoldAndCharm()
        {
            var charm = ScriptableObject.CreateInstance<CharmData>();
            created.Add(charm);
            config.charmPool = new List<CharmData> { charm };
            var run = new RunState(config, 1);
            int gold = run.Gold;
            int turn = run.Turn;
            var die = Die(run, "normal");
            Assert.AreEqual(40, run.GuideLostChild(die, out var got, out _));
            Assert.AreSame(charm, got);
            Assert.AreEqual(DiceState.Used, die.state);
            Assert.AreEqual(gold + 40, run.Gold);
            Assert.AreEqual(turn + 1, run.Turn);
            Assert.AreEqual(10, run.DirectLostChild());
        }

        // ---- 振り出しの札 ----

        [Test]
        public void StartOverCard_BackToStart_MaxHpUpAndFullHeal()
        {
            var run = new RunState(config, 1);
            var move = run.BeginMove(Die(run, "six"));
            while (move.remaining > 0) run.StepMove(move, run.NeedsBranchChoice(move) ? run.Current.next.First() : null);
            run.FinishMove(move);
            run.player.LoseHp(15);
            Assert.AreNotSame(run.board.Start, run.Current);
            run.DrawStartOverCard();
            Assert.AreSame(run.board.Start, run.Current);
            Assert.AreEqual(50, run.player.maxHp);
            Assert.AreEqual(50, run.player.hp);
        }
    }
}
