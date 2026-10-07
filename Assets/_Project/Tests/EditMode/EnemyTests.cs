using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>第1層の敵の行動（多段・弱体・封印・ランダム・双六の番人）と出現ルール。</summary>
    public class EnemyTests
    {
        TestDice factory;
        DicePouch pouch;
        Combatant player;

        static Intent Atk(int v) => new Intent(IntentType.Attack, v);
        static Intent Multi(int v, int hits) => new Intent(IntentType.MultiAttack, v, hits);
        static Intent Weak(int v) => new Intent(IntentType.Debuff, v);
        static Intent Seal() => new Intent(IntentType.Seal, 0);

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            pouch = new DicePouch();
            player = new Combatant(40);
        }

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        EnemyData Enemy(int hp, EnemyBehavior behavior, params Intent[] pattern)
        {
            var e = factory.Enemy(hp, pattern);
            e.behavior = behavior;
            return e;
        }

        BattleState Start(EnemyData enemy, int seed = 0) => new BattleState(player, enemy, pouch, new System.Random(seed));

        DiceInstance Add(DiceInstance d)
        {
            pouch.Add(d);
            return d;
        }

        // ---- 多段攻撃 ----

        [Test]
        public void MultiAttack_BlockAppliesToTotal()
        {
            var three = Add(factory.Fixed(3));
            Add(factory.Normal());
            var battle = Start(Enemy(30, EnemyBehavior.Sequence, Multi(2, 2)));

            battle.Assign(battle.Roll(three), Assignment.Block);
            Assert.AreEqual(1, battle.Preview().taken, "2×2=4 を防御3で受けて1");
            Assert.AreEqual(1, battle.Resolve().taken);
        }

        [Test]
        public void MultiAttack_StrengthAddedOnceToTotal()
        {
            Assert.AreEqual(3 * 3 + 1, BattleResolver.EnemyAttack(Multi(3, 3), 1));
        }

        // ---- 弱体 ----

        [Test]
        public void Weak_LastsThroughNextRound_ThenWearsOff()
        {
            var eight = Add(factory.Fixed(8));
            var eightB = Add(factory.Fixed(8));
            Add(factory.Normal());
            var battle = Start(Enemy(100, EnemyBehavior.Sequence, Weak(1), Atk(4), Atk(4)));

            battle.Resolve(); // パス：化け茸が弱体1を与える
            Assert.AreEqual(1, player.weak, "受けたラウンドの終わりには減らない");

            battle.Roll(eight);
            Assert.AreEqual(6, battle.AttackValue, "8×0.75=6");
            battle.Resolve();
            Assert.AreEqual(0, player.weak, "次のラウンドの終わりに消える");

            battle.Roll(eightB);
            Assert.AreEqual(8, battle.AttackValue);
        }

        [Test]
        public void Weak_FloorsAfterStrength()
        {
            Assert.AreEqual(7, BattleResolver.PlayerAttack(new[] { 9 }, 1, 1), "(9+1)×0.75=7.5→7");
        }

        // ---- 封印 ----

        [Test]
        public void Seal_TakesHighestAverageAvailableDie_UntilBattleEnd()
        {
            var normal = Add(factory.Normal());
            var high = Add(factory.High());
            var low = Add(factory.Low());
            var battle = Start(Enemy(30, EnemyBehavior.Sequence, Seal(), Atk(7)));

            var result = battle.Resolve(); // パス：賽盗人が封印

            Assert.AreSame(high, result.sealedDie, "平均5の四五六賽");
            Assert.AreEqual(DiceState.Sealed, high.state);
            Assert.AreEqual(2, pouch.AvailableCount);
            Assert.IsFalse(battle.CanRollMore && pouch.Available.Contains(high));

            battle.enemy.hp = 1;
            battle.Roll(normal);
            battle.Resolve();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(DiceState.Available, high.state, "戦闘が終わると封印は解ける");
        }

        [Test]
        public void Seal_LastAvailableDie_RefreshesTheUsedOnes()
        {
            var a = Add(factory.Normal());
            var b = Add(factory.High());
            pouch.Use(a); // 使用可能は b だけ
            var battle = Start(Enemy(30, EnemyBehavior.Sequence, Seal()));

            battle.Resolve();

            Assert.AreEqual(DiceState.Sealed, b.state);
            Assert.AreEqual(DiceState.Available, a.state, "使用可能が0個になったのでリフレッシュ");
        }

        // ---- ランダムな行動 ----

        [Test]
        public void RandomBehavior_PicksFromPattern_DeterministicBySeed()
        {
            Add(factory.Normal());
            var usagi = Enemy(100, EnemyBehavior.Random, Multi(2, 2), Atk(4));

            List<IntentType> Run(int seed)
            {
                player.maxHp = 1000;
                player.hp = 1000;
                var battle = Start(usagi, seed);
                var seen = new List<IntentType>();
                for (int i = 0; i < 12; i++)
                {
                    seen.Add(battle.EnemyIntent.type);
                    battle.Resolve();
                }
                return seen;
            }

            var first = Run(5);
            CollectionAssert.AreEqual(first, Run(5), "同じシードなら同じ並び");
            CollectionAssert.IsSubsetOf(first.Distinct(), new[] { IntentType.MultiAttack, IntentType.Attack });
            Assert.AreEqual(2, first.Distinct().Count(), "12ラウンドあれば両方出る");
        }

        // ---- 双六の番人（決まった行動＋4ラウンドごとの振り出し） ----

        EnemyData Banjin() => Enemy(500, EnemyBehavior.Sequence, Atk(8), new Intent(IntentType.Block, 10), Atk(12), new Intent(IntentType.ResetDice, 0));

        [Test]
        public void Banjin_FixedPattern_EveryFourthRoundResets()
        {
            Add(factory.Normal());
            var battle = Start(Banjin());
            player.hp = 1000;
            player.maxHp = 1000;

            var seen = new List<IntentType>();
            for (int round = 1; round <= 8; round++)
            {
                seen.Add(battle.EnemyIntent.type);
                battle.Resolve();
            }
            CollectionAssert.AreEqual(new[]
            {
                IntentType.Attack, IntentType.Block, IntentType.Attack, IntentType.ResetDice,
                IntentType.Attack, IntentType.Block, IntentType.Attack, IntentType.ResetDice,
            }, seen);
        }

        [Test]
        public void Banjin_ResetDice_MakesEveryDieUsedThenRefreshes()
        {
            var a = Add(factory.Normal());
            var b = Add(factory.Normal());
            var c = Add(factory.Normal());
            Add(factory.Normal());
            var battle = Start(Banjin(), 1);
            player.hp = 1000;

            battle.Roll(a); battle.Resolve();
            battle.Roll(b); battle.Resolve();
            battle.Roll(c); battle.Resolve();
            Assert.AreEqual(IntentType.ResetDice, battle.EnemyIntent.type);
            Assert.AreEqual(1, pouch.AvailableCount, "温存していた1個");

            battle.Resolve();

            Assert.AreEqual(4, pouch.AvailableCount, "全部使用済み → その瞬間にリフレッシュ");
        }

        [Test]
        public void HiddenDiceRollPreview_ShowsRange()
        {
            // 値を隠した賽振り（min < max）は、予測も範囲になる
            Add(factory.Normal());
            var hidden = new Intent(IntentType.DiceRoll, 8) { minValue = 4, maxValue = 12 };
            var battle = Start(Enemy(30, EnemyBehavior.Sequence, hidden));

            var preview = battle.Preview();
            Assert.IsTrue(preview.TakenIsRange);
            Assert.AreEqual(4, preview.takenMin);
            Assert.AreEqual(12, preview.taken);
        }

        // ---- 出現ルール ----

        [Test]
        public void PickEnemy_FirstBattlesEarlyOnly_NoRepeats()
        {
            var config = ScriptableObject.CreateInstance<Phase0Config>();
            config.startingDice = new List<DiceData> { factory.Data("n", 1, 2, 3, 4, 5, 6) };
            var weak1 = factory.Enemy(10, Atk(1));
            var weak2 = factory.Enemy(10, Atk(1));
            var strong = factory.Enemy(10, Atk(1));
            strong.earlyOk = false;
            config.battleEnemies = new List<EnemyData> { weak1, weak2, strong };
            config.earlyBattleCount = 3;

            for (int seed = 0; seed < 30; seed++)
            {
                var run = new RunState(config, seed);
                var tile = new TileNode(1, TileType.Battle);
                EnemyData last = null;
                for (int i = 0; i < 20; i++)
                {
                    var e = run.PickEnemy(tile);
                    if (i < 3) Assert.AreNotSame(strong, e, "最初の3戦は弱めの敵だけ");
                    Assert.AreNotSame(last, e, "同じ敵は2戦続けない");
                    last = e;
                }
            }
            Object.DestroyImmediate(config);
        }
    }
}
