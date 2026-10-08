using System.Collections.Generic;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>ダイスそのものの特徴（盾賽・剣賽）が戦闘に効くこと。</summary>
    public class DiceEffectTests
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

        AddValueEffect Add(Trigger trigger, int add)
        {
            var e = ScriptableObject.CreateInstance<AddValueEffect>();
            e.trigger = trigger;
            e.add = add;
            created.Add(e);
            return e;
        }

        /// <summary>全部の面が value の盾賽（防御+2／攻撃−1）。</summary>
        DiceInstance Tate(int value)
        {
            var data = factory.Data("tate", value, value, value, value, value, value);
            data.effects = new List<EffectSO> { Add(Trigger.OnAssignDefense, 2), Add(Trigger.OnAssignAttack, -1) };
            return new DiceInstance(data);
        }

        DiceInstance Ken(int value)
        {
            var data = factory.Data("ken", value, value, value, value, value, value);
            data.effects = new List<EffectSO> { Add(Trigger.OnAssignAttack, 2), Add(Trigger.OnAssignDefense, -1) };
            return new DiceInstance(data);
        }

        BattleState Battle(DicePouch pouch, int enemyAttack, EffectBus bus = null)
        {
            var enemy = factory.Enemy(30, new Intent(IntentType.Attack, enemyAttack));
            return new BattleState(new Combatant(40), enemy, pouch, new System.Random(0), bus);
        }

        // 仕様書 第6章の例：敵が攻撃8。盾賽で4、普通の賽で5。

        [Test]
        public void SpecExample_ShieldToBlock_NormalToAttack()
        {
            var pouch = new DicePouch();
            var tate = Tate(4);
            var normal = factory.Fixed(5);
            pouch.Add(tate);
            pouch.Add(normal);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 8);

            battle.Assign(battle.Roll(tate), Assignment.Block);
            battle.Assign(battle.Roll(normal), Assignment.Attack);

            Assert.AreEqual(6, battle.BlockValue, "盾賽を防御に置くと 4+2=6");
            var preview = battle.Preview();
            Assert.AreEqual(5, preview.dealt, "敵に5ダメージ");
            Assert.AreEqual(2, preview.taken, "自分は2ダメージ");

            var result = battle.Resolve();
            Assert.AreEqual(5, result.dealt);
            Assert.AreEqual(2, result.taken);
        }

        [Test]
        public void SpecExample_BothToAttack()
        {
            var pouch = new DicePouch();
            var tate = Tate(4);
            var normal = factory.Fixed(5);
            pouch.Add(tate);
            pouch.Add(normal);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 8);

            battle.Assign(battle.Roll(tate), Assignment.Attack);
            battle.Assign(battle.Roll(normal), Assignment.Attack);

            Assert.AreEqual(8, battle.AttackValue, "4−1+5=8");
            var result = battle.Resolve();
            Assert.AreEqual(8, result.dealt, "敵に8ダメージ");
            Assert.AreEqual(8, result.taken, "自分は8ダメージ");
        }

        [Test]
        public void Ken_AttackPlusTwo_BlockMinusOne()
        {
            var pouch = new DicePouch();
            var ken = Ken(3);
            pouch.Add(ken);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 5);
            var rolled = battle.Roll(ken);

            Assert.AreEqual(5, battle.EffectiveValue(rolled, Assignment.Attack));
            Assert.AreEqual(2, battle.EffectiveValue(rolled, Assignment.Block));
        }

        [Test]
        public void EffectiveValue_NeverBelowZero()
        {
            var pouch = new DicePouch();
            var ken = Ken(0);
            pouch.Add(ken);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 5);

            Assert.AreEqual(0, battle.EffectiveValue(battle.Roll(ken), Assignment.Block));
        }

        [Test]
        public void RelicEffectsApplyAfterDiceEffects()
        {
            // 盾賽の防御+2 のあとに、レリック「防御+1」が足される
            var relic = ScriptableObject.CreateInstance<RelicData>();
            relic.effects = new List<EffectSO> { Add(Trigger.OnAssignDefense, 1) };
            created.Add(relic);
            var bus = new EffectBus();
            bus.Register(relic);

            var pouch = new DicePouch();
            var tate = Tate(4);
            pouch.Add(tate);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 8, bus);

            Assert.AreEqual(7, battle.EffectiveValue(battle.Roll(tate), Assignment.Block));
        }

        [Test]
        public void BakuchiFacesKeepTen()
        {
            var bakuchi = new DiceInstance(factory.Data("bakuchi", 0, 0, 0, 10, 10, 10));
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 10, 10, 10 }, System.Array.ConvertAll(bakuchi.faces, f => f.value));
        }

        [Test]
        public void Run_StarterIsAddedAfterStartingDice()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            var normal = factory.Data("normal", 1, 2, 3, 4, 5, 6);
            var starter = factory.Data("tate", 1, 2, 3, 4, 5, 6);
            config.startingDice = new List<DiceData> { normal, normal, normal };

            var run = new RunState(config, 1, starter);

            Assert.AreEqual(4, run.pouch.All.Count);
            Assert.AreSame(starter, run.pouch.All[3].data);
            Assert.AreEqual(3, new RunState(config, 1).pouch.All.Count, "スターターなし");
        }
    }
}
