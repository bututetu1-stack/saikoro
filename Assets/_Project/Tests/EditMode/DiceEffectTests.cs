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

        // ---- 「使う」：1個ずつ効果を発動（2026-10-16 開発者の要望） ----

        DiceInstance Rider(int value, AttackRider rider, int amount)
        {
            var e = ScriptableObject.CreateInstance<AttackRiderEffect>();
            e.trigger = Trigger.OnAttackResolve;
            e.rider = rider;
            e.amount = amount;
            created.Add(e);
            var data = factory.Data("rider", value, value, value, value, value, value);
            data.effects = new List<EffectSO> { e };
            return new DiceInstance(data);
        }

        DiceInstance Rengeki(int value)
        {
            var data = factory.Data("rengeki", value, value, value, value, value, value);
            data.multiHit = true;
            return new DiceInstance(data);
        }

        [Test]
        public void Use_DealsDamageNow_AndTurnEndDoesNotHitAgain()
        {
            var pouch = new DicePouch();
            var five = factory.Fixed(5);
            pouch.Add(five);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 8);

            battle.Assign(battle.Roll(five), Assignment.Attack);
            var u = battle.Use();
            Assert.AreEqual(5, u.Dealt);
            Assert.AreEqual(25, battle.enemy.hp, "使った瞬間に敵の HP が減る");
            Assert.IsFalse(battle.HasPending);
            Assert.AreEqual(0, battle.Preview().dealt, "使ったダイスは見込みに入らない");

            var r = battle.Resolve();
            Assert.AreEqual(25, battle.enemy.hp, "ターン終了で同じダイスがもう一度当たらない");
            Assert.AreEqual(5, r.dealt, "ラウンドの記録には使った分も入る");
            Assert.AreEqual(8, r.taken, "ターン終了で敵が行動する");
        }

        [Test]
        public void Use_VulnerableFromFirstDie_BoostsNextUse()
        {
            var pouch = new DicePouch();
            var hari = Rider(2, AttackRider.Vulnerable, 1);
            var six = factory.Fixed(6);
            pouch.Add(hari);
            pouch.Add(six);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 3);

            battle.Assign(battle.Roll(hari), Assignment.Attack);
            battle.Use();
            Assert.AreEqual(28, battle.enemy.hp);
            Assert.AreEqual(1, battle.enemy.vulnerable, "弱体はすぐ入る");

            battle.Assign(battle.Roll(six), Assignment.Attack);
            Assert.AreEqual(9, battle.Preview().dealt, "次に使うダイスには弱体が乗る（6×1.5）");
            battle.Use();
            Assert.AreEqual(19, battle.enemy.hp);
        }

        [Test]
        public void Use_StrengthOncePerRound_EvenAcrossUses()
        {
            var pouch = new DicePouch();
            var a = factory.Fixed(5);
            var b = factory.Fixed(5);
            pouch.Add(a);
            pouch.Add(b);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 3);
            battle.HandsEnabled = false; // 同じ出目2つはゾロ目になるので、役は切って筋力だけを確かめる
            battle.player.strength = 2;

            battle.Assign(battle.Roll(a), Assignment.Attack);
            Assert.AreEqual(7, battle.Use().Dealt, "1回目の攻撃に筋力が乗る");
            battle.Assign(battle.Roll(b), Assignment.Attack);
            Assert.AreEqual(5, battle.Use().Dealt, "筋力は1ラウンドに1回だけ");
        }

        [Test]
        public void Rengeki_StrengthOnEveryHit()
        {
            var pouch = new DicePouch();
            var ren = Rengeki(3);
            var four = factory.Fixed(4);
            pouch.Add(ren);
            pouch.Add(four);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 3);
            battle.player.strength = 2;

            battle.Assign(battle.Roll(ren), Assignment.Attack);
            Assert.AreEqual(9, battle.Preview().dealt, "連撃賽 3 発 ×（1＋筋力2）");
            battle.Use();
            battle.Assign(battle.Roll(four), Assignment.Attack);
            Assert.AreEqual(6, battle.Preview().dealt, "連撃賽は1ラウンド1回の筋力を使わない（4＋2）");
        }

        [Test]
        public void Use_KillingAllEnemies_WinsAtOnce()
        {
            var pouch = new DicePouch();
            var big = factory.Fixed(9);
            pouch.Add(big);
            pouch.Add(factory.Normal());
            var enemy = factory.Enemy(8, new Intent(IntentType.Attack, 20));
            var battle = new BattleState(new Combatant(40), enemy, pouch, new System.Random(0));

            battle.Assign(battle.Roll(big), Assignment.Attack);
            battle.Use();
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome, "ターン終了を待たずに勝つ");
            Assert.AreEqual(40, battle.player.hp, "敵は行動しない");
            Assert.AreEqual(1, battle.History.Count, "ラウンドの記録が残る");
        }

        [Test]
        public void BlockResolve_GivesFortify()
        {
            var pouch = new DicePouch();
            var fx = ScriptableObject.CreateInstance<GainBlockEffect>();
            fx.trigger = Trigger.OnBlockResolve;
            fx.fortify = 1;
            created.Add(fx);
            var data = factory.Data("kamae", 4, 4, 4, 4, 4, 4);
            data.effects = new List<EffectSO> { fx };
            var kamae = new DiceInstance(data);
            pouch.Add(kamae);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 0);

            battle.Assign(battle.Roll(kamae), Assignment.Block);
            var u = battle.Use();
            Assert.AreEqual(4, u.blockGained);
            Assert.AreEqual(1, u.fortifyGained, "防御に使うと堅守");
            Assert.AreEqual(4, battle.player.block);
        }

        // ---- 2026-10-16 ダイスの見直し：6面以外・呼び戻し・育つ面・呪い ----

        T Fx<T>() where T : EffectSO
        {
            var e = ScriptableObject.CreateInstance<T>();
            e.trigger = Trigger.OnRoll;
            created.Add(e);
            return e;
        }

        [Test]
        public void TwelveFacedDie_RollsAllFaces()
        {
            var data = factory.Data("d12", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            var die = new DiceInstance(data);
            Assert.AreEqual(12, die.faces.Length);
            var rng = new System.Random(3);
            var seen = new HashSet<int>();
            for (int i = 0; i < 600; i++)
            {
                int v = die.Roll(rng);
                Assert.That(v, Is.InRange(1, 12));
                seen.Add(v);
            }
            Assert.AreEqual(12, seen.Count, "7〜12 の面も出る");
            var four = new DiceInstance(factory.Data("d4", 1, 2, 3, 4));
            Assert.AreEqual(4, four.faces.Length);
        }

        [Test]
        public void GrowFace_RaisesSmallestFaceEachRoll()
        {
            var data = factory.Data("sodachi", 1, 1, 2, 2, 3, 3);
            var grow = Fx<GrowFaceEffect>();
            grow.amount = 1;
            grow.max = 6;
            data.effects = new List<EffectSO> { grow };
            var die = new DiceInstance(data);
            var pouch = new DicePouch();
            pouch.Add(die);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 1);
            battle.Roll(die);
            int sum = 0;
            foreach (var f in die.faces) sum += f.value;
            Assert.AreEqual(12 + 1, sum, "いちばん小さい面（1）が 2 に");
        }

        [Test]
        public void ReturnUsedPerPip_ReturnsAsManyAsTheValue_NotItself()
        {
            var data = factory.Data("yobimodoshi", 2, 2, 2, 2, 2, 2);
            var ret = Fx<ReturnUsedDieEffect>();
            ret.perPip = true;
            data.effects = new List<EffectSO> { ret };
            var die = new DiceInstance(data);
            var pouch = new DicePouch();
            pouch.Add(die);
            var used = new List<DiceInstance>();
            for (int i = 0; i < 3; i++) { var d = factory.Normal(); pouch.Add(d); d.state = DiceState.Used; used.Add(d); }
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 1);
            battle.Roll(die);
            int back = 0;
            foreach (var d in used) if (d.state == DiceState.Available) back++;
            Assert.AreEqual(2, back, "出目2なので2個戻る");
            Assert.AreEqual(DiceState.Used, die.state, "振ったダイス自身は使用済み");
        }

        [Test]
        public void CurseDice_PoisonSelf_StrengthenEnemy()
        {
            var poisonData = factory.Data("ekibyou", 1, 2, 3, 4, 5, 6);
            var poison = Fx<CurseEffect>();
            poison.kind = CurseKind.SelfPoison;
            poison.amount = 1;
            poisonData.effects = new List<EffectSO> { poison };
            var angerData = factory.Data("urami", 1, 2, 3, 4, 5, 6);
            var anger = Fx<CurseEffect>();
            anger.kind = CurseKind.EnemyStrength;
            anger.amount = 1;
            angerData.effects = new List<EffectSO> { anger };
            var a = new DiceInstance(poisonData);
            var b = new DiceInstance(angerData);
            var pouch = new DicePouch();
            pouch.Add(a);
            pouch.Add(b);
            pouch.Add(factory.Normal());
            var battle = Battle(pouch, 1);
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(1, battle.player.poison, "疫病賽：自分に毒1");
            Assert.AreEqual(1, battle.enemies[0].strength, "怨み賽：敵の筋力+1");
        }
    }
}
