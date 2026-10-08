using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>2体の敵との戦闘（フェーズ2 手順4。開発者の判断で入れた形）。</summary>
    public class MultiEnemyTests
    {
        TestDice factory;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        DicePouch Pouch(params DiceInstance[] dice)
        {
            var pouch = new DicePouch { Capacity = 10 };
            foreach (var d in dice) pouch.Add(d);
            return pouch;
        }

        BattleState Battle(DicePouch pouch, params EnemyData[] enemies) =>
            new BattleState(new Combatant(40), enemies.ToList(), pouch, new System.Random(0));

        EnemyData Enemy(int hp, int attack) => factory.Enemy(hp, new Intent(IntentType.Attack, attack));

        [Test]
        public void TwoEnemies_CanRollOneMore_UntilOneFalls()
        {
            var battle = Battle(Pouch(factory.Fixed(9), factory.Fixed(1), factory.Fixed(1), factory.Fixed(1)), Enemy(5, 1), Enemy(30, 1));
            Assert.AreEqual(3, battle.MaxDicePerRound, "2体なら +1");
            battle.Roll(battle.pouch.All[0]);
            battle.Resolve(); // 1体目を倒す
            Assert.AreEqual(1, battle.AliveEnemies.Count());
            Assert.AreEqual(2, battle.MaxDicePerRound, "1体になったら元に戻る");
        }

        [Test]
        public void Count_SpawnsSameEnemyTwice()
        {
            var twin = Enemy(22, 4);
            twin.count = 2;
            var battle = new BattleState(new Combatant(40), twin, Pouch(factory.Fixed(1)), new System.Random(0));
            Assert.AreEqual(2, battle.enemies.Count);
        }

        [Test]
        public void Attack_HitsTarget_OverflowGoesToNext()
        {
            var battle = Battle(Pouch(factory.Fixed(8), factory.Fixed(1)), Enemy(5, 1), Enemy(10, 1));
            battle.Roll(battle.pouch.All[0]);
            Assert.AreEqual(8, battle.Preview().dealt);
            var r = battle.Resolve();

            Assert.IsTrue(battle.enemies[0].IsDead);
            Assert.AreEqual(7, battle.enemies[1].hp, "余った3が次の敵へ");
            Assert.AreEqual(8, r.dealt);
            Assert.IsTrue(r.enemies[0].killedByAttack);
        }

        [Test]
        public void SetTarget_AttackGoesToChosenEnemy()
        {
            var battle = Battle(Pouch(factory.Fixed(6), factory.Fixed(1)), Enemy(20, 1), Enemy(20, 1));
            battle.SetTarget(battle.enemies[1]);
            battle.Roll(battle.pouch.All[0]);
            battle.Resolve();
            Assert.AreEqual(20, battle.enemies[0].hp);
            Assert.AreEqual(14, battle.enemies[1].hp);
        }

        [Test]
        public void SweepDie_HitsAllEnemies()
        {
            var sweep = factory.Fixed(4);
            sweep.data.hitsAll = true;
            var battle = Battle(Pouch(sweep, factory.Fixed(3), factory.Fixed(1)), Enemy(20, 1), Enemy(20, 1));
            battle.Roll(sweep);
            battle.Roll(battle.pouch.All[1]);
            var r = battle.Resolve();
            Assert.AreEqual(13, battle.enemies[0].hp, "薙ぎ4＋通常3");
            Assert.AreEqual(16, battle.enemies[1].hp, "薙ぎ4だけ");
            Assert.AreEqual(11, r.dealt);
        }

        [Test]
        public void EnemyAttacks_AddUp_BlockAppliesToTotal()
        {
            var player = new Combatant(40);
            var battle = new BattleState(player, new List<EnemyData> { Enemy(30, 5), Enemy(30, 5) }, Pouch(factory.Fixed(6), factory.Fixed(1)), new System.Random(0));
            var die = battle.Roll(battle.pouch.All[0]);
            battle.Assign(die, Assignment.Block);
            Assert.AreEqual(4, battle.Preview().taken, "5+5 − 防御6");
            var r = battle.Resolve();
            Assert.AreEqual(4, r.taken);
            Assert.AreEqual(36, player.hp);
        }

        [Test]
        public void Twins_SurvivorGainsStrength()
        {
            var twin = Enemy(5, 4);
            twin.count = 2;
            twin.allyDefeatedStrength = 3;
            var player = new Combatant(40);
            var battle = new BattleState(player, twin, Pouch(factory.Fixed(5), factory.Fixed(1)), new System.Random(0));
            battle.Roll(battle.pouch.All[0]);
            var r = battle.Resolve();

            Assert.AreEqual(3, battle.enemies[1].strength);
            Assert.AreEqual(3, r.enemies[1].strengthGained);
            Assert.AreEqual(7, r.taken, "残った方は筋力+3でそのラウンドから攻撃4+3");
        }

        [Test]
        public void Victory_OnlyWhenAllDefeated()
        {
            var battle = Battle(Pouch(factory.Fixed(9), factory.Fixed(9), factory.Fixed(9), factory.Fixed(1)), Enemy(9, 1), Enemy(9, 1));
            battle.Roll(battle.pouch.All[0]);
            battle.Resolve();
            Assert.AreEqual(BattleOutcome.Ongoing, battle.Outcome);
            battle.Roll(battle.pouch.All[1]);
            battle.Resolve();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
        }

        [Test]
        public void OldDiceCup_AddsOne_CappedAtThree()
        {
            var fx = ScriptableObject.CreateInstance<DicePerRoundEffect>();
            fx.trigger = Trigger.OnBattleStart;
            var relic = ScriptableObject.CreateInstance<RelicData>();
            relic.effects = new List<EffectSO> { fx };
            created.Add(fx);
            created.Add(relic);
            var bus = new EffectBus();
            bus.Register(relic);

            var single = new BattleState(new Combatant(40), Enemy(10, 1), Pouch(factory.Fixed(1)), new System.Random(0), bus);
            Assert.AreEqual(3, single.MaxDicePerRound);
            var pair = new BattleState(new Combatant(40), new List<EnemyData> { Enemy(10, 1), Enemy(10, 1) }, Pouch(factory.Fixed(1)), new System.Random(0), bus);
            Assert.AreEqual(3, pair.MaxDicePerRound, "最大3個");
        }
    }
}
