using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Tests
{
    /// <summary>ラスボス「賽の神・八面」の仕組み（仕様書 第7章）。</summary>
    public class HachimenTests
    {
        TestDice factory;

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        EnemyData Hachimen()
        {
            var e = factory.Enemy(180,
                new Intent(IntentType.RollAttack, 2) { maxValue = 8 },
                new Intent(IntentType.RollBlock, 3) { maxValue = 8 });
            e.phase2HpPercent = 50;
            e.phase2Pattern = new List<Intent>
            {
                new Intent(IntentType.RewriteFate, 0),
                new Intent(IntentType.RollAttack, 2) { maxValue = 8 },
            };
            return e;
        }

        DicePouch Pouch(params DiceInstance[] dice)
        {
            var pouch = new DicePouch();
            foreach (var d in dice) pouch.Add(d);
            return pouch;
        }

        [Test]
        public void RollIntents_AreVisible_AndAlternate()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var battle = new BattleState(new Combatant(500), Hachimen(), Pouch(factory.Fixed(1), factory.Fixed(1)), new System.Random(seed));
                var first = battle.EnemyIntent;
                Assert.AreEqual(IntentType.DiceRoll, first.type);
                Assert.AreEqual(first.minValue, first.maxValue, "値は予告で見える");
                Assert.That(first.value, Is.InRange(2, 16));
                Assert.AreEqual(0, first.value % 2, "出目×2");

                battle.Resolve();
                var second = battle.EnemyIntent;
                Assert.AreEqual(IntentType.Block, second.type);
                Assert.That(second.value, Is.InRange(3, 24));
                Assert.AreEqual(0, second.value % 3, "出目×3");
            }
        }

        [Test]
        public void Phase2_AtHalfHp_StartsWithRewriteFate()
        {
            var battle = new BattleState(new Combatant(500), Hachimen(), Pouch(factory.Fixed(1), factory.Fixed(1)), new System.Random(0));
            battle.enemy.hp = 90;
            battle.Resolve();
            Assert.IsTrue(battle.enemy.InPhase2);
            Assert.IsTrue(battle.enemy.EnteredPhase2ThisRound);
            Assert.AreEqual(IntentType.RewriteFate, battle.EnemyIntent.type);
        }

        [Test]
        public void RewriteFate_SetsStrongestMaxFaceToOne_RestoredAfterBattle()
        {
            var weak = factory.Normal();
            var strong = new DiceInstance(factory.Data("big", 3, 4, 5, 6, 7, 8));
            var pouch = Pouch(weak, strong, factory.Fixed(1));
            var enemy = factory.Enemy(10, new Intent(IntentType.RewriteFate, 0));
            var player = new Combatant(40);
            var battle = new BattleState(player, enemy, pouch, new System.Random(0));

            var r = battle.Resolve();
            Assert.AreEqual(strong, r.enemies[0].rewrittenDie);
            Assert.AreEqual(8, r.enemies[0].rewrittenFrom);
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 1 }, strong.faces.Select(f => f.value).ToArray());

            // 倒して戦闘を終えると元に戻る
            battle.Roll(pouch.All[0]);
            battle.enemy.hp = 1;
            battle.Resolve();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 6, 7, 8 }, strong.faces.Select(f => f.value).ToArray());
        }
    }
}
