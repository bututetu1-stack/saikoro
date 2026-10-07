using System;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Tests
{
    public class BattleTests
    {
        TestDice factory;
        DicePouch pouch;
        Combatant player;

        static Intent Attack(int v) => new Intent(IntentType.Attack, v);
        static Intent Block(int v) => new Intent(IntentType.Block, v);
        static Intent Buff(int v) => new Intent(IntentType.Buff, v);

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            pouch = new DicePouch();
            player = new Combatant(40);
        }

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        BattleState Start(EnemyData enemy) => new BattleState(player, enemy, pouch, new Random(0));

        DiceInstance AddDie(DiceInstance d)
        {
            pouch.Add(d);
            return d;
        }

        // ---- ダメージ計算（phase0-prototype.md の例） ----

        [Test]
        public void Damage_FourToBlockFiveToAttack_DealsFiveTakesFour()
        {
            var four = AddDie(factory.Fixed(4));
            var five = AddDie(factory.Fixed(5));
            AddDie(factory.Normal()); // 振らずに残しておく（リフレッシュさせない）
            var battle = Start(factory.Enemy(30, Attack(8)));

            battle.Assign(battle.Roll(four), Assignment.Block);
            battle.Assign(battle.Roll(five), Assignment.Attack);

            var preview = battle.Preview();
            Assert.AreEqual(5, preview.dealt);
            Assert.AreEqual(4, preview.taken);

            var result = battle.Resolve();
            Assert.AreEqual(5, result.dealt);
            Assert.AreEqual(4, result.taken);
            Assert.AreEqual(25, battle.enemy.hp);
            Assert.AreEqual(36, player.hp);
        }

        [Test]
        public void Damage_BothToAttack_DealsNineTakesEight()
        {
            var four = AddDie(factory.Fixed(4));
            var five = AddDie(factory.Fixed(5));
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(30, Attack(8)));

            battle.Assign(battle.Roll(four), Assignment.Attack);
            battle.Assign(battle.Roll(five), Assignment.Attack);
            var result = battle.Resolve();

            Assert.AreEqual(9, result.dealt);
            Assert.AreEqual(8, result.taken);
        }

        [Test]
        public void EnemyBlockIntent_AppliesBeforePlayerAttack()
        {
            var five = AddDie(factory.Fixed(5));
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(12, Block(4), Attack(5)));

            Assert.AreEqual(4, battle.enemy.block, "予告した時点で防御値になる");

            battle.Assign(battle.Roll(five), Assignment.Attack);
            Assert.AreEqual(1, battle.Preview().dealt);

            var result = battle.Resolve();
            Assert.AreEqual(1, result.dealt);
            Assert.AreEqual(11, battle.enemy.hp);
            Assert.AreEqual(0, battle.enemy.block, "ラウンド終了で0に戻る");
        }

        [Test]
        public void Strength_AddsOnceToTotalAttack()
        {
            Assert.AreEqual(10, BattleResolver.PlayerAttack(new[] { 4, 5 }, 1));
            Assert.AreEqual(0, BattleResolver.PlayerAttack(new int[0], 3), "攻撃に置いたダイスがなければ筋力も乗らない");
        }

        [Test]
        public void EnemyBuff_IncreasesLaterAttacks()
        {
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(15, Buff(1), Attack(6)));

            battle.Resolve(); // パス：小鬼が筋力+1
            Assert.AreEqual(1, battle.enemy.strength);
            Assert.AreEqual(40, player.hp);

            var result = battle.Resolve(); // パス：攻撃6+1
            Assert.AreEqual(7, result.taken);
            Assert.AreEqual(33, player.hp);
        }

        [Test]
        public void Pass_TakesFullDamage()
        {
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(12, Attack(5)));

            var result = battle.Resolve();

            Assert.AreEqual(0, result.dealt);
            Assert.AreEqual(5, result.taken);
            Assert.AreEqual(DiceState.Available, pouch.All[0].state, "パスならダイスは使わない");
        }

        [Test]
        public void Pattern_RepeatsFromTheStart()
        {
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(12, Attack(5), Attack(5), Block(4)));

            var seen = Enumerable.Range(0, 4).Select(_ =>
            {
                var i = battle.EnemyIntent;
                battle.Resolve();
                return i.type;
            }).ToArray();

            CollectionAssert.AreEqual(new[] { IntentType.Attack, IntentType.Attack, IntentType.Block, IntentType.Attack }, seen);
        }

        // ---- ダイスの使用とリフレッシュ ----

        [Test]
        public void BattleDice_StayUsedAfterVictory()
        {
            var a = AddDie(factory.Fixed(6));
            var b = AddDie(factory.Fixed(6));
            var c = AddDie(factory.Normal());
            var d = AddDie(factory.Normal());
            var battle = Start(factory.Enemy(10, Attack(5)));

            battle.Roll(a);
            battle.Roll(b);
            battle.Resolve();

            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(DiceState.Used, a.state);
            Assert.AreEqual(DiceState.Used, b.state);
            Assert.AreEqual(DiceState.Available, c.state);
            Assert.AreEqual(DiceState.Available, d.state);
        }

        [Test]
        public void LastAvailableDie_RefreshesAndCanBeRolledAgainSameRound()
        {
            var a = AddDie(factory.Fixed(3));
            var b = AddDie(factory.Fixed(3));
            pouch.Use(b); // 使用可能は a だけ
            var battle = Start(factory.Enemy(30, Attack(5)));

            battle.Roll(a);
            Assert.AreEqual(2, pouch.AvailableCount, "振った時点でリフレッシュ");
            Assert.IsTrue(battle.CanRollMore);

            Assert.DoesNotThrow(() => battle.Roll(a), "今振ったダイスも選び直せる");
            Assert.IsFalse(battle.CanRollMore, "1ラウンド2個まで");
        }

        [Test]
        public void Roll_MoreThanMaxPerRound_Throws()
        {
            var a = AddDie(factory.Normal());
            var b = AddDie(factory.Normal());
            var c = AddDie(factory.Normal());
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(30, Attack(5)));

            battle.Roll(a);
            battle.Roll(b);

            Assert.Throws<InvalidOperationException>(() => battle.Roll(c));
        }

        [Test]
        public void Defeat_WhenPlayerHpReachesZero()
        {
            player.hp = 5;
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(30, Attack(8)));

            battle.Resolve();

            Assert.AreEqual(BattleOutcome.Defeat, battle.Outcome);
            Assert.AreEqual(0, player.hp);
        }

        [Test]
        public void Victory_EnemyDoesNotActOnKillingRound()
        {
            var six = AddDie(factory.Fixed(6));
            AddDie(factory.Normal());
            var battle = Start(factory.Enemy(6, Attack(8)));

            battle.Roll(six);
            Assert.AreEqual(0, battle.Preview().taken);

            var result = battle.Resolve();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(0, result.taken);
            Assert.AreEqual(40, player.hp);
        }
    }
}
