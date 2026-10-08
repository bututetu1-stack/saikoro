using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>お守り（仕様書 第10章）。</summary>
    public class CharmTests
    {
        TestDice factory;
        GameConfig config;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("three", 3, 3, 3, 3, 3, 3), factory.Data("five", 5, 5, 5, 5, 5, 5), factory.Data("normal", 1, 2, 3, 4, 5, 6) };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        CharmData Charm(CharmKind kind, int amount = 0, int price = 25)
        {
            var c = ScriptableObject.CreateInstance<CharmData>();
            c.id = kind.ToString();
            c.displayName = kind.ToString();
            c.kind = kind;
            c.amount = amount;
            c.price = price;
            created.Add(c);
            return c;
        }

        DiceInstance Die(RunState run, string id) => run.pouch.All.First(d => d.data.id == id);

        BattleState Battle(RunState run) =>
            new BattleState(run.player, factory.Enemy(50, new Intent(IntentType.Attack, 1)), run.pouch, new System.Random(0), run.effects, run);

        [Test]
        public void AtMostThreeCharms()
        {
            var run = new RunState(config, 1);
            for (int i = 0; i < RunState.MaxCharms; i++) Assert.IsTrue(run.AddCharm(Charm(CharmKind.Heal, 12)));
            Assert.IsFalse(run.CanAddCharm);
            Assert.IsFalse(run.AddCharm(Charm(CharmKind.Heal, 12)));
            Assert.AreEqual(3, run.Charms.Count);
        }

        [Test]
        public void Heal_RestoresHp_AndIsConsumed()
        {
            var run = new RunState(config, 1);
            var heal = Charm(CharmKind.Heal, 12);
            run.AddCharm(heal);
            Assert.IsFalse(run.CanUseNow(heal), "HP が満タンなら使えない");
            run.player.LoseHp(20);
            Assert.AreEqual(12, run.UseCharm(heal));
            Assert.AreEqual(32, run.player.hp);
            Assert.AreEqual(0, run.Charms.Count);
        }

        [Test]
        public void ReturnUsed_RestoresUsedDice()
        {
            var run = new RunState(config, 1);
            var charm = Charm(CharmKind.ReturnUsed);
            run.AddCharm(charm);
            Assert.IsFalse(run.CanUseNow(charm));
            run.pouch.Use(Die(run, "three"));
            run.pouch.Use(Die(run, "five"));
            Assert.AreEqual(2, run.UseCharm(charm));
            Assert.AreEqual(3, run.pouch.AvailableCount);
        }

        [Test]
        public void Unseal_FreesSealedDice()
        {
            var run = new RunState(config, 1);
            var charm = Charm(CharmKind.Unseal);
            run.AddCharm(charm);
            Die(run, "five").state = DiceState.Sealed;
            Assert.IsTrue(run.CanUseNow(charm));
            Assert.AreEqual(1, run.UseCharm(charm));
            Assert.AreEqual(DiceState.Available, Die(run, "five").state);
        }

        [Test]
        public void MoveForwardAndBack_ChangeMoveValue()
        {
            var run = new RunState(config, 1);
            var forward = Charm(CharmKind.MoveForward, 2);
            var back = Charm(CharmKind.MoveBack, 2);
            run.AddCharm(forward);
            run.AddCharm(back);

            var move = run.BeginMove(Die(run, "three"));
            Assert.IsTrue(RunState.CanUseOnMove(forward, move));
            run.UseCharmOnMove(forward, move);
            Assert.AreEqual(5, move.value);
            Assert.AreEqual(5, move.remaining);
            run.UseCharmOnMove(back, move);
            Assert.AreEqual(3, move.value);
            Assert.AreEqual(0, run.Charms.Count);
        }

        [Test]
        public void MoveBack_AtLeastOne()
        {
            config.startingDice = new List<DiceData> { factory.Data("two", 2, 2, 2, 2, 2, 2), factory.Data("five", 5, 5, 5, 5, 5, 5) };
            var run = new RunState(config, 1);
            var back = Charm(CharmKind.MoveBack, 2);
            run.AddCharm(back);
            var move = run.BeginMove(Die(run, "two"));
            run.UseCharmOnMove(back, move);
            Assert.AreEqual(1, move.value);
        }

        [Test]
        public void MoveCharm_NotAfterWalkingStarted()
        {
            var run = new RunState(config, 1);
            var forward = Charm(CharmKind.MoveForward, 2);
            run.AddCharm(forward);
            var move = run.BeginMove(Die(run, "three"));
            run.StepMove(move, run.NeedsBranchChoice(move) ? run.Current.next.First() : null);
            Assert.IsFalse(RunState.CanUseOnMove(forward, move));
            Assert.Throws<System.InvalidOperationException>(() => run.UseCharmOnMove(forward, move));
        }

        [Test]
        public void RerollCharm_OnMove_DoesNotUseAnotherDie()
        {
            var run = new RunState(config, 1);
            var reroll = Charm(CharmKind.RerollDie);
            run.AddCharm(reroll);
            var move = run.BeginMove(Die(run, "normal"));
            int available = run.pouch.AvailableCount;
            run.UseCharmOnMove(reroll, move);
            Assert.AreEqual(available, run.pouch.AvailableCount);
            Assert.That(move.value, Is.InRange(1, 6));
            Assert.AreEqual(move.value, move.remaining);
        }

        [Test]
        public void RerollCharm_InBattle()
        {
            var run = new RunState(config, 1);
            var reroll = Charm(CharmKind.RerollDie);
            run.AddCharm(reroll);
            var battle = Battle(run);
            Assert.IsFalse(run.CanUseInBattle(reroll, battle), "振る前は使えない");
            var r = battle.Roll(Die(run, "normal"));
            Assert.IsTrue(run.CanUseInBattle(reroll, battle));
            run.UseRerollCharm(reroll, battle, r);
            Assert.That(r.value, Is.InRange(1, 6));
            Assert.AreEqual(0, run.Charms.Count);
        }

        [Test]
        public void Smoke_OnlyNormalBattle_EndsWithoutVictory()
        {
            var run = new RunState(config, 1);
            var smoke = Charm(CharmKind.Smoke);
            run.AddCharm(smoke);
            var elite = Battle(run);
            Assert.IsFalse(run.CanUseInBattle(smoke, elite), "逃げられる戦闘でなければ使えない");

            var battle = Battle(run);
            battle.canFleeBattle = true;
            Assert.IsTrue(run.CanUseInBattle(smoke, battle));
            run.UseSmoke(smoke, battle);
            Assert.AreEqual(BattleOutcome.Fled, battle.Outcome);
            Assert.AreEqual(0, run.stats.battlesWon, "勝ちには数えない");
        }

        [Test]
        public void MoveCharms_NotUsableInBattle()
        {
            var run = new RunState(config, 1);
            var forward = Charm(CharmKind.MoveForward, 2);
            run.AddCharm(forward);
            var battle = Battle(run);
            battle.Roll(Die(run, "normal"));
            Assert.IsFalse(run.CanUseInBattle(forward, battle));
        }

        [Test]
        public void Shop_SellsThreeDifferentCharms()
        {
            config.charmPool = new List<CharmData> { Charm(CharmKind.Heal, 12, 35), Charm(CharmKind.Smoke, 0, 40), Charm(CharmKind.MoveForward, 2), Charm(CharmKind.ReturnUsed, 0, 50) };
            var run = new RunState(config, 1);
            var shop = run.CreateShop();
            var charms = shop.items.Where(i => i.kind == ShopItemKind.Charm).ToList();
            Assert.AreEqual(3, charms.Count);
            Assert.AreEqual(3, charms.Select(i => i.charm).Distinct().Count());

            run.GainGold(200);
            var item = charms.First(i => !i.discounted);
            int gold = run.Gold;
            shop.BuyCharm(item);
            Assert.AreEqual(gold - item.charm.price, run.Gold);
            Assert.AreEqual(1, run.Charms.Count);
        }

        [Test]
        public void Shop_CannotBuyCharmWhenFull()
        {
            config.charmPool = new List<CharmData> { Charm(CharmKind.Heal, 12, 35), Charm(CharmKind.Smoke, 0, 40), Charm(CharmKind.MoveForward, 2) };
            var run = new RunState(config, 1);
            for (int i = 0; i < 3; i++) run.AddCharm(config.charmPool[0]);
            var shop = run.CreateShop();
            run.GainGold(200);
            var item = shop.items.First(i => i.kind == ShopItemKind.Charm);
            Assert.Throws<System.InvalidOperationException>(() => shop.BuyCharm(item));
        }

        [Test]
        public void NormalBattleReward_SometimesHasCharm()
        {
            config.charmPool = new List<CharmData> { Charm(CharmKind.Heal, 12, 35) };
            int withCharm = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var run = new RunState(config, seed);
                if (run.CreateBattleReward(RewardKind.Normal).charm != null) withCharm++;
                Assert.IsNull(run.CreateBattleReward(RewardKind.Elite).charm, "エリートにはお守りはない");
            }
            Assert.That(withCharm, Is.InRange(50, 110), "約40%");
        }

        [Test]
        public void FoxSeeOff_GivesCharm_OrGoldWhenFull()
        {
            var heal = Charm(CharmKind.Heal, 12, 35);
            config.charmPool = new List<CharmData> { heal };
            var run = new RunState(config, 1);
            int gold = run.Gold;
            Assert.AreEqual(0, run.SeeOffFox(out var charm));
            Assert.AreSame(heal, charm);
            Assert.AreEqual(gold, run.Gold);
            Assert.AreEqual(1, run.Charms.Count);

            run.AddCharm(heal);
            run.AddCharm(heal);
            Assert.AreEqual(15, run.SeeOffFox(out charm));
            Assert.IsNull(charm);
        }
    }
}
