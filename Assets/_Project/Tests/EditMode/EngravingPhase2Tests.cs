using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaiNoMichi.Tests
{
    /// <summary>フェーズ2で足した刻印（仕様書 第5章）。</summary>
    public class EngravingPhase2Tests
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

        T Fx<T>(Trigger trigger) where T : EffectSO
        {
            var e = ScriptableObject.CreateInstance<T>();
            e.trigger = trigger;
            created.Add(e);
            return e;
        }

        EngravingData Engraving(EngravingKind kind, NumericOp op = NumericOp.Add, int amount = 0, params EffectSO[] effects)
        {
            var e = ScriptableObject.CreateInstance<EngravingData>();
            e.kind = kind;
            e.op = op;
            e.amount = amount;
            e.effects = new List<EffectSO>(effects);
            created.Add(e);
            return e;
        }

        /// <summary>全部の面が value で、刻印 e が付いたダイス。</summary>
        DiceInstance Engraved(int value, EngravingData e)
        {
            var die = factory.Fixed(value);
            for (int i = 0; i < die.faces.Length; i++) die.faces[i] = new Face(value, e);
            return die;
        }

        DicePouch Pouch(params DiceInstance[] dice)
        {
            var pouch = new DicePouch();
            foreach (var d in dice) pouch.Add(d);
            return pouch;
        }

        BattleState Battle(DicePouch pouch, int enemyAttack, Combatant player = null) =>
            new BattleState(player ?? new Combatant(40), factory.Enemy(50, new Intent(IntentType.Attack, enemyAttack)), pouch, new System.Random(0));

        // ---- 数値刻印 ----

        [Test]
        public void Copy_TakesLargestOtherFace()
        {
            var die = factory.Normal();
            var copy = Engraving(EngravingKind.Numeric, NumericOp.CopyFace);
            Assert.AreEqual(6, RunState.EngravedFace(die, 0, copy).value);
        }

        [Test]
        public void Kongou_SetsNine()
        {
            var die = factory.Normal();
            var kongou = Engraving(EngravingKind.Numeric, NumericOp.Set, 9);
            Assert.AreEqual(9, RunState.EngravedFace(die, 2, kongou).value);
        }

        // ---- 攻撃のあとに効く刻印 ----

        [Test]
        public void Lifesteal_HealsHalfOfDamageDealt()
        {
            var fx = Fx<AttackRiderEffect>(Trigger.OnAttackResolve);
            fx.rider = AttackRider.Lifesteal;
            fx.percent = 50;
            var player = new Combatant(20, 40);
            var battle = Battle(Pouch(Engraved(7, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1)), 0, player);
            battle.Roll(battle.pouch.All[0]);
            battle.Resolve();
            Assert.AreEqual(23, player.hp, "7 ダメージの半分（3）回復");
        }

        [Test]
        public void Kuzushi_GivesVulnerable()
        {
            var fx = Fx<AttackRiderEffect>(Trigger.OnAttackResolve);
            fx.rider = AttackRider.Vulnerable;
            fx.amount = 1;
            var battle = Battle(Pouch(Engraved(3, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1)), 0);
            battle.Roll(battle.pouch.All[0]);
            battle.Resolve();
            Assert.AreEqual(1, battle.enemy.vulnerable);
        }

        [Test]
        public void Ashikase_ReducesThisRoundsAttack()
        {
            var fx = Fx<AttackRiderEffect>(Trigger.OnAttackResolve);
            fx.rider = AttackRider.ReduceIntent;
            fx.amount = 3;
            var player = new Combatant(40);
            var battle = Battle(Pouch(Engraved(2, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1)), 8, player);
            battle.Roll(battle.pouch.All[0]);
            var r = battle.Resolve();
            Assert.AreEqual(5, r.taken, "攻撃8 − 3");
        }

        // ---- 振ったときの刻印 ----

        [Test]
        public void Reroll_OnceInBattle()
        {
            var fx = Fx<FlagEffect>(Trigger.OnRoll);
            fx.flag = RollFlag.CanReroll;
            var battle = Battle(Pouch(Engraved(4, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1)), 0);
            var r = battle.Roll(battle.pouch.All[0]);
            Assert.IsTrue(r.canReroll);
            battle.Reroll(r);
            Assert.IsTrue(r.rerolled);
            Assert.Throws<InvalidOperationException>(() => battle.Reroll(r), "1回だけ");
        }

        [Test]
        public void BothSides_CountsForAttackAndBlock()
        {
            var fx = Fx<FlagEffect>(Trigger.OnRoll);
            fx.flag = RollFlag.BothSides;
            var player = new Combatant(40);
            var battle = Battle(Pouch(Engraved(5, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1)), 8, player);
            battle.Roll(battle.pouch.All[0]);
            Assert.AreEqual(5, battle.AttackValue);
            Assert.AreEqual(5, battle.BlockValue);
            var r = battle.Resolve();
            Assert.AreEqual(5, r.dealt);
            Assert.AreEqual(3, r.taken, "攻撃8 − 防御5");
        }

        [Test]
        public void Chain_ReturnsAUsedDie()
        {
            var fx = Fx<ReturnUsedDieEffect>(Trigger.OnRoll);
            fx.count = 1;
            var other = factory.Fixed(2);
            var pouch = Pouch(other, Engraved(3, Engraving(EngravingKind.Effect, effects: fx)), factory.Fixed(1));
            var battle = Battle(pouch, 0);
            battle.Roll(other);
            Assert.AreEqual(DiceState.Used, other.state);
            battle.Roll(pouch.All[1]);
            Assert.AreEqual(DiceState.Available, other.state, "連鎖で戻る");
        }

        // ---- 移動の刻印 ----

        [Test]
        public void NearestRestOrShop_StopsBeforeBoss()
        {
            var a = new TileNode(0, TileType.Battle);
            var b = new TileNode(1, TileType.Event);
            var c = new TileNode(2, TileType.Shop);
            var d = new TileNode(3, TileType.Rest);
            a.next.Add(b);
            b.next.Add(c);
            c.next.Add(d);
            Assert.AreEqual(c, RunState.NearestRestOrShop(a, out int dist));
            Assert.AreEqual(2, dist);

            var boss = new TileNode(4, TileType.Boss);
            var e = new TileNode(5, TileType.Rest);
            boss.next.Add(e);
            Assert.IsNull(RunState.NearestRestOrShop(boss, out _), "ボスより先には行かない");
        }

        [Test]
        public void RerollMove_Once()
        {
            var fx = Fx<FlagEffect>(Trigger.OnRoll);
            fx.flag = RollFlag.CanReroll;
            var config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            var run = new RunState(config, 1);
            var die = Engraved(2, Engraving(EngravingKind.Effect, effects: fx));
            run.pouch.Add(die);

            var move = run.BeginMove(die);
            Assert.IsTrue(move.canReroll);
            run.RerollMove(move);
            Assert.AreEqual(2, move.value);
            Assert.IsFalse(move.canReroll);
            Assert.Throws<InvalidOperationException>(() => run.RerollMove(move));
        }
    }
}
