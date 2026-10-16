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
        public void StartOverCard_NotInCoop()
        {
            // 協力ではみんなで同じマスにいるので、引いた人だけスタートに戻らないように出さない
            var run = new RunState(config, 1) { multiplayer = true };
            Assert.IsFalse(run.EventAllowed(EventKind.StartOverCard));
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

        // ---- 旅団の焚き火（みんなで遊ぶとき専用） ----

        [Test]
        public void Bonfire_OnlyWhenPlayingTogether()
        {
            var solo = new RunState(config, 3);
            Assert.IsFalse(solo.EventAllowed(EventKind.Bonfire));
            for (int i = 0; i < 300; i++) Assert.AreNotEqual(EventKind.Bonfire, solo.PickEvent());
            var together = new RunState(config, 3) { multiplayer = true };
            Assert.IsTrue(together.EventAllowed(EventKind.Bonfire));
            bool seen = false;
            for (int i = 0; i < 300 && !seen; i++) seen = together.PickEvent() == EventKind.Bonfire;
            Assert.IsTrue(seen, "協力なら設定の一覧になくても出る");
        }

        [Test]
        public void Bonfire_RestHeals_ShareGivesCharmOrGold()
        {
            var heal = ScriptableObject.CreateInstance<CharmData>();
            heal.id = "heal";
            heal.kind = CharmKind.Heal;
            created.Add(heal);
            config.charmPool = new List<CharmData> { heal };
            var run = new RunState(config, 3) { multiplayer = true };
            run.player.hp = 10;
            Assert.AreEqual(run.BonfireHeal, run.BonfireRest());
            Assert.AreEqual(heal, run.BonfireShare(out int gold));
            Assert.AreEqual(0, gold);
            while (run.CanAddCharm) run.AddCharm(heal);
            int before = run.Gold;
            Assert.IsNull(run.BonfireShare(out gold), "お守りがいっぱいならゴールド");
            Assert.AreEqual(RunState.BonfireGold, gold);
            Assert.AreEqual(before + RunState.BonfireGold, run.Gold);
        }

        // ---- 旅団の宴（みんなで遊ぶとき専用） ----

        [Test]
        public void Feast_OnlyWhenPlayingTogether()
        {
            var solo = new RunState(config, 3);
            Assert.IsFalse(solo.EventAllowed(EventKind.Feast));
            for (int i = 0; i < 300; i++) Assert.AreNotEqual(EventKind.Feast, solo.PickEvent());
            var together = new RunState(config, 3) { multiplayer = true };
            Assert.IsTrue(together.EventAllowed(EventKind.Feast));
            bool seen = false;
            for (int i = 0; i < 300 && !seen; i++) seen = together.PickEvent() == EventKind.Feast;
            Assert.IsTrue(seen, "協力なら設定の一覧になくても出る");
        }

        [Test]
        public void Feast_JoinHealsAndGivesGold_TalkRaisesSmallestFaceOfWeakestDie()
        {
            var run = new RunState(config, 3) { multiplayer = true };
            run.player.hp = 10;
            int gold = run.Gold;
            Assert.AreEqual(run.FeastHeal, run.JoinFeast(out int got));
            Assert.AreEqual(RunState.FeastGold, got);
            Assert.AreEqual(gold + RunState.FeastGold, run.Gold);

            var weakest = run.FeastTalkTarget;
            Assert.IsNotNull(weakest);
            int sum = weakest.faces.Sum(f => f.value);
            int min = weakest.faces.Min(f => f.value);
            var die = run.FeastTalk(out int before, out int after);
            Assert.AreSame(weakest, die);
            Assert.AreEqual(min, before);
            Assert.AreEqual(min + 1, after);
            Assert.AreEqual(sum + 1, die.faces.Sum(f => f.value));
        }

        // ---- 竜宮城 ----

        [Test]
        public void Ryugu_StayHealsFully_ButTurnsPass()
        {
            var run = new RunState(config, 3);
            run.player.hp = 5;
            int turn = run.Turn;
            string gift = run.StayAtRyugu();
            Assert.AreEqual(run.player.maxHp, run.player.hp, "HP が全回復");
            Assert.AreEqual(turn + RunState.RyuguTurns, run.Turn, "そのあいだにターンが進む");
            Assert.IsFalse(string.IsNullOrEmpty(gift));
            Assert.IsTrue(new EventSettings().oncePerRun.Contains(EventKind.Ryugu), "1ランに1回");
        }

        // ---- 鶴の恩返し ----

        [Test]
        public void Crane_HelpNow_GiftOnNextLayer_NotOnFinalLayer()
        {
            var run = new RunState(config, 3);
            Assert.IsTrue(run.EventAllowed(EventKind.Crane));
            run.GainGold(100);
            int gold = run.Gold;
            Assert.IsTrue(run.HelpCrane());
            Assert.AreEqual(gold - RunState.CraneCost, run.Gold);
            Assert.IsTrue(run.CraneGratitude);
            Assert.IsFalse(run.HelpCrane(), "恩返しを待っている間は、もう助けられない");

            int relics = run.Relics.Count;
            var result = run.AdvanceLayer();
            Assert.IsNotNull(result.craneGift, "次の層に着くと恩返し");
            Assert.AreEqual(relics + (config.relicPool.Count > 0 ? 1 : 0), run.Relics.Count);
            Assert.IsFalse(run.CraneGratitude);
            Assert.IsNull(run.AdvanceLayer().craneGift, "恩返しは1回だけ");
            Assert.IsTrue(run.IsFinalLayer);
            Assert.IsFalse(run.EventAllowed(EventKind.Crane), "最後の層では出ない");
        }

        [Test]
        public void Crane_GratitudeSurvivesSaveAndLoad()
        {
            var run = new RunState(config, 3);
            run.GainGold(100);
            run.HelpCrane();
            var loaded = RunState.Restore(config, RunSave.FromJson(run.CreateSave().ToJson()));
            Assert.IsTrue(loaded.CraneGratitude, "続きから遊んでも恩返しは届く");
        }

        // ---- 雀のお宿 ----

        [Test]
        public void Suzume_SmallGivesCharmAndGold_LargeGivesRelicAndCurse()
        {
            var heal = ScriptableObject.CreateInstance<CharmData>();
            heal.id = "heal";
            heal.displayName = "傷薬";
            heal.kind = CharmKind.Heal;
            created.Add(heal);
            config.charmPool = new List<CharmData> { heal };
            var small = new RunState(config, 3);
            int gold = small.Gold;
            var got = small.OpenSmallBasket();
            Assert.AreEqual(1, small.Charms.Count);
            Assert.AreEqual(gold + RunState.SuzumeSmallGold, small.Gold);
            Assert.AreEqual(2, got.Count);

            var large = new RunState(config, 3);
            int dice = large.pouch.All.Count;
            large.OpenLargeBasket();
            Assert.AreEqual(config.relicPool.Count > 0 ? 1 : 0, large.Relics.Count, "レリック");
            bool hasCurse = config.curseDicePool.Exists(d => d != null) || config.curseDice != null;
            Assert.AreEqual(dice + (hasCurse ? 1 : 0), large.pouch.All.Count, "呪いのダイスも押し付けられる");
        }

        [Test]
        public void Suzume_OncePerRun()
        {
            var run = new RunState(config, 3);
            Assert.IsTrue(run.EventAllowed(EventKind.Suzume));
            Assert.IsTrue(new EventSettings().oncePerRun.Contains(EventKind.Suzume));
        }

        // ---- 賽の河原 ----

        [Test]
        public void Kawara_StackUntilOni_OrTakeReward()
        {
            var heal = ScriptableObject.CreateInstance<CharmData>();
            heal.id = "heal";
            heal.displayName = "傷薬";
            heal.kind = CharmKind.Heal;
            created.Add(heal);
            config.charmPool = new List<CharmData> { heal };
            bool sawOni = false, sawReward = false;
            for (int seed = 1; seed < 60; seed++)
            {
                var run = new RunState(config, seed);
                run.BeginKawara();
                Assert.AreEqual(1, run.KawaraOniChance, "最初は 1/6");
                int hp = run.player.hp;
                if (!run.StackStone(out int damage))
                {
                    sawOni = true;
                    Assert.AreEqual(RunState.KawaraDamage, damage);
                    Assert.AreEqual(hp - damage, run.player.hp);
                    Assert.AreEqual(0, run.KawaraStones, "崩れたら0から");
                    continue;
                }
                Assert.AreEqual(1, run.KawaraStones);
                Assert.AreEqual(2, run.KawaraOniChance, "積むほど鬼が来やすい");
                var got = run.TakeKawaraReward();
                sawReward = true;
                CollectionAssert.AreEqual(new[] { "傷薬" }, got, "1つならお守り");
                Assert.AreEqual(0, run.KawaraStones);
            }
            Assert.IsTrue(sawOni && sawReward);
        }

        [Test]
        public void Kawara_ThreeStonesGiveRelic_CannotStackMore()
        {
            for (int seed = 1; seed < 400; seed++)
            {
                var run = new RunState(config, seed);
                run.BeginKawara();
                if (!run.StackStone(out _) || !run.StackStone(out _) || !run.StackStone(out _)) continue;
                Assert.IsFalse(run.CanStackStone, "3つまで");
                Assert.IsFalse(run.StackStone(out _));
                int relics = run.Relics.Count;
                run.TakeKawaraReward();
                Assert.AreEqual(relics + (config.relicPool.Count > 0 ? 1 : 0), run.Relics.Count, "3つならレリック");
                return;
            }
            Assert.Fail("3つ積めるシードが見つからない");
        }

        // ---- 福引き所 ----

        [Test]
        public void Lottery_CostsGold_AndGivesSomething()
        {
            var heal = ScriptableObject.CreateInstance<CharmData>();
            heal.id = "heal";
            heal.kind = CharmKind.Heal;
            created.Add(heal);
            config.charmPool = new List<CharmData> { heal };
            config.relicPool = new List<RelicData> { Relic("a"), Relic("b") };
            var run = new RunState(config, 3);
            run.LoseGold(run.Gold - (config.events.lotteryCost - 1));
            Assert.IsFalse(run.CanSpinLottery, "お金が足りなければ回せない");
            var balls = new HashSet<RunState.LotteryBall>();
            for (int i = 0; i < 200; i++)
            {
                var r = new RunState(config, 100 + i);
                r.GainGold(100);
                int before = r.Gold;
                var result = r.SpinLottery();
                balls.Add(result.ball);
                if (result.ball == RunState.LotteryBall.Red) Assert.AreEqual(before - config.events.lotteryCost + RunState.LotteryRedGold, r.Gold);
                if (result.ball == RunState.LotteryBall.Gold) Assert.IsNotNull(result.relic);
                if (result.ball == RunState.LotteryBall.Blue) Assert.IsNotNull(result.charm);
            }
            Assert.GreaterOrEqual(balls.Count, 4, "いろいろな玉が出る");
        }

        // ---- 合わせ賽の鍛冶師 ----

        [Test]
        public void MergeSmith_KeepsHigherFaces_AndRemovesTheOther()
        {
            var run = new RunState(config, 3);
            Assert.IsFalse(run.CanMergeDice, "ダイスが少ないうちは出ない");
            Assert.IsFalse(run.EventAllowed(EventKind.MergeSmith));
            run.AddDice(commonA);
            run.AddDice(commonB);
            Assert.IsTrue(run.CanMergeDice);
            Assert.IsTrue(run.EventAllowed(EventKind.MergeSmith));
            var one = Die(run, "one");
            var six = Die(run, "six");
            int count = run.pouch.All.Count();
            run.MergeDice(one, six);
            Assert.AreEqual(count - 1, run.pouch.All.Count());
            Assert.IsTrue(one.faces.All(f => f.value == 6), "面ごとに大きいほう");
            CollectionAssert.DoesNotContain(run.pouch.All.ToList(), six);
            var curse = run.AddDice(kake);
            Assert.IsFalse(run.CanBeMerged(curse), "呪いのダイスは合わせられない");
        }

        [Test]
        public void MergeSmith_OncePerRun()
        {
            var run = new RunState(config, 3);
            run.AddDice(commonA);
            run.AddDice(commonB);
            bool seen = false;
            for (int i = 0; i < 400 && !seen; i++) seen = run.PickEvent() == EventKind.MergeSmith;
            Assert.IsTrue(seen);
            Assert.IsFalse(run.EventAllowed(EventKind.MergeSmith), "1ランに1回");
        }

        // ---- 河童の相撲・賽銭箱 ----

        [Test]
        public void KappaSumo_WinGivesGold_LoseHurts()
        {
            var run = new RunState(config, 3);
            int gold = run.Gold;
            var win = run.Sumo(Die(run, "six"));
            Assert.IsTrue(win.won);
            Assert.AreEqual(gold + config.events.sumoGold, run.Gold);
            int hp = run.player.hp;
            var lose = run.Sumo(Die(run, "one"));
            Assert.IsFalse(lose.won);
            Assert.AreEqual(hp - config.events.sumoDamage, run.player.hp);
            Assert.AreEqual(DiceState.Used, Die(run, "one").state, "振ったダイスは使用済み");
        }

        [Test]
        public void Offering_RaisesMaxHp()
        {
            var run = new RunState(config, 3);
            run.GainGold(100);
            int max = run.player.maxHp;
            Assert.IsTrue(run.CanOffer);
            Assert.AreEqual(config.events.offeringMaxHp, run.Offer());
            Assert.AreEqual(max + config.events.offeringMaxHp, run.player.maxHp);
        }

        // ---- 2026-10-16 追加のイベント（イベント限定のダイス・次の層に持ち越すもの） ----

        void AddEventDice(params string[] ids)
        {
            config.eventDicePool = new List<DiceData>();
            foreach (var id in ids) config.eventDicePool.Add(factory.Data(id, 1, 2, 3, 4, 5, 6));
        }

        [Test]
        public void Moneylender_PaysBackDoubleAtNextLayer_EvenAfterSave()
        {
            var run = new RunState(config, 1);
            run.GainGold(100 - run.Gold);
            Assert.IsTrue(run.Lend());
            Assert.AreEqual(100 - RunState.MoneylenderLoan, run.Gold);
            var restored = RunState.Restore(config, RunSave.FromJson(run.CreateSave().ToJson()));
            CollectionAssert.AreEqual(new[] { "loan" }, restored.LayerPromises, "セーブしても約束は残る");
            var result = restored.AdvanceLayer();
            Assert.AreEqual(100 - RunState.MoneylenderLoan + RunState.MoneylenderReturn, restored.Gold);
            Assert.AreEqual(1, result.promiseGifts.Count);
            Assert.AreEqual(0, restored.LayerPromises.Count, "受け取ったら消える");
        }

        [Test]
        public void Tennyo_ReturnRobe_GivesEventDiceAndMaxHpAtNextLayer()
        {
            AddEventDice("tennyo");
            var run = new RunState(config, 1);
            int maxHp = run.player.maxHp;
            run.ReturnRobe();
            Assert.IsFalse(run.pouch.All.Any(d => d.data.id == "tennyo"), "すぐにはもらえない");
            run.AdvanceLayer();
            Assert.AreEqual(maxHp + RunState.TennyoMaxHp, run.player.maxHp);
            Assert.IsTrue(run.pouch.All.Any(d => d.data.id == "tennyo"), "次の層で天女賽");
        }

        [Test]
        public void Youtou_GivesEventDice_AndCostsMaxHp()
        {
            AddEventDice("youtou");
            var run = new RunState(config, 1);
            int maxHp = run.player.maxHp;
            Assert.IsTrue(run.TakeYoutou());
            Assert.AreEqual(maxHp - RunState.YoutouMaxHpCost, run.player.maxHp);
            Assert.IsTrue(run.pouch.All.Any(d => d.data.id == "youtou"));
            Assert.IsFalse(config.rewardDicePool.Any(d => d.id == "youtou"), "イベント限定のダイスは報酬に出ない");
        }

        [Test]
        public void CarryOverEvents_NotOnFinalLayer()
        {
            var run = new RunState(config, 1);
            Assert.IsTrue(run.EventAllowed(EventKind.Moneylender));
            Assert.IsFalse(run.EventAllowed(EventKind.OniTreasure), "鬼の落とし物は第2層から");
            run.AdvanceLayer();
            run.AdvanceLayer();
            Assert.IsFalse(run.EventAllowed(EventKind.Moneylender), "最後の層では持ち越すイベントは出ない");
            Assert.IsFalse(run.EventAllowed(EventKind.Tennyo));
            Assert.IsTrue(run.EventAllowed(EventKind.OniTreasure));
        }
    }
}
