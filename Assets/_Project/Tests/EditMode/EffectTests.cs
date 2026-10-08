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
    public class EffectTests
    {
        /// <summary>テスト用の効果の持ち主。</summary>
        class Source : IEffectSource
        {
            public EffectSourceKind Kind { get; }
            public IReadOnlyList<EffectSO> Effects { get; }

            public Source(EffectSourceKind kind, params EffectSO[] effects)
            {
                Kind = kind;
                Effects = effects;
            }
        }

        readonly List<Object> created = new List<Object>();
        TestDice factory;

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
            factory.DestroyAll();
        }

        T Make<T>(Trigger trigger) where T : EffectSO
        {
            var e = ScriptableObject.CreateInstance<T>();
            e.trigger = trigger;
            created.Add(e);
            return e;
        }

        AddValueEffect Add(Trigger trigger, int add, EffectCondition condition = default)
        {
            var e = Make<AddValueEffect>(trigger);
            e.add = add;
            e.condition = condition;
            return e;
        }

        ScaleEffect Scale(Trigger trigger, ScaleTarget target, int percent)
        {
            var e = Make<ScaleEffect>(trigger);
            e.target = target;
            e.percent = percent;
            return e;
        }

        RelicData Relic(string id, params EffectSO[] effects)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.id = id;
            r.effects = new List<EffectSO>(effects);
            created.Add(r);
            return r;
        }

        // ---- EffectBus ----

        [Test]
        public void Fire_RunsOnlyMatchingTrigger()
        {
            var bus = new EffectBus();
            bus.Register(new Source(EffectSourceKind.Relic, Add(Trigger.OnAssignAttack, 3), Add(Trigger.OnAssignDefense, 100)));

            var ctx = bus.Fire(new EffectContext(Trigger.OnAssignAttack) { value = 5 });

            Assert.AreEqual(8, ctx.value);
        }

        [Test]
        public void Fire_OrderIsDiceEngravingRelicStatus()
        {
            // 状態異常（×2）→ レリック（+1）→ 刻印（+3）の順に登録しても、実行は 刻印 → レリック → 状態異常
            var bus = new EffectBus();
            bus.Register(new Source(EffectSourceKind.Status, Scale(Trigger.OnAssignAttack, ScaleTarget.Value, 200)));
            bus.Register(new Source(EffectSourceKind.Relic, Add(Trigger.OnAssignAttack, 1)));
            var engraving = new Source(EffectSourceKind.Engraving, Add(Trigger.OnAssignAttack, 3));

            var ctx = bus.Fire(new EffectContext(Trigger.OnAssignAttack) { value = 5 }, engraving);

            Assert.AreEqual((5 + 3 + 1) * 2, ctx.value);
        }

        [Test]
        public void Fire_LocalSourcesDoNotStayRegistered()
        {
            var bus = new EffectBus();
            var engraving = new Source(EffectSourceKind.Engraving, Add(Trigger.OnAssignAttack, 3));

            bus.Fire(new EffectContext(Trigger.OnAssignAttack), engraving);
            var ctx = bus.Fire(new EffectContext(Trigger.OnAssignAttack) { value = 5 });

            Assert.AreEqual(5, ctx.value);
        }

        // ---- 部品 ----

        [Test]
        public void AddValue_ConditionByAssignment()
        {
            // 盾賽：防御に回すと+2
            var shield = Add(Trigger.OnAssignDefense, 2, new EffectCondition { assignment = Assignment.Block });
            var bus = new EffectBus();

            var asBlock = bus.Fire(new EffectContext(Trigger.OnAssignDefense) { value = 4, assignment = Assignment.Block }, new Source(EffectSourceKind.Dice, shield));
            var asAttack = bus.Fire(new EffectContext(Trigger.OnAssignDefense) { value = 4, assignment = Assignment.Attack }, new Source(EffectSourceKind.Dice, shield));

            Assert.AreEqual(6, asBlock.value);
            Assert.AreEqual(4, asAttack.value);
        }

        [Test]
        public void AddValue_ConditionByParity()
        {
            // 丁の札：偶数の出目を攻撃に置くと+1
            var even = Add(Trigger.OnAssignAttack, 1, new EffectCondition { parity = ParityCondition.Even });
            var bus = new EffectBus();
            bus.Register(new Source(EffectSourceKind.Relic, even));

            Assert.AreEqual(5, bus.Fire(new EffectContext(Trigger.OnAssignAttack) { value = 4 }).value);
            Assert.AreEqual(3, bus.Fire(new EffectContext(Trigger.OnAssignAttack) { value = 3 }).value);
        }

        [Test]
        public void AddValue_ConditionByRange()
        {
            var lowOnly = Add(Trigger.OnRoll, 10, new EffectCondition { maxValue = 2 });
            var bus = new EffectBus();
            bus.Register(new Source(EffectSourceKind.Relic, lowOnly));

            Assert.AreEqual(11, bus.Fire(new EffectContext(Trigger.OnRoll) { value = 1 }).value);
            Assert.AreEqual(3, bus.Fire(new EffectContext(Trigger.OnRoll) { value = 3 }).value);
        }

        [Test]
        public void Scale_FloorsTheResult()
        {
            var bus = new EffectBus();
            bus.Register(new Source(EffectSourceKind.Relic, Scale(Trigger.OnGoldGain, ScaleTarget.Amount, 125)));

            Assert.AreEqual(16, bus.Fire(new EffectContext(Trigger.OnGoldGain) { amount = 13 }).amount); // 16.25 → 16
        }

        [Test]
        public void Heal_HealsPlayerUpToMax()
        {
            var heal = Make<HealEffect>(Trigger.OnRefresh);
            heal.heal = 3;
            var player = new Combatant(38, 40);

            new EffectBus().Fire(new EffectContext(Trigger.OnRefresh) { player = player }, new Source(EffectSourceKind.Relic, heal));

            Assert.AreEqual(40, player.hp);
        }

        // ---- RunState のゴールドとレリック ----

        RunState NewRun()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            return new RunState(config, 1);
        }

        [Test]
        public void Run_StartsWithFiftyGold()
        {
            Assert.AreEqual(50, NewRun().Gold);
        }

        [Test]
        public void GainGold_AppliesRelicOnGoldGain()
        {
            var run = NewRun();
            Assert.AreEqual(15, run.GainGold(15));

            run.AddRelic(Relic("zeni", Scale(Trigger.OnGoldGain, ScaleTarget.Amount, 125))); // 銭袋
            Assert.AreEqual(18, run.GainGold(15)); // 18.75 → 18

            Assert.AreEqual(50 + 15 + 18, run.Gold);
            Assert.IsTrue(run.HasRelic("zeni"));
        }

        [Test]
        public void GainGoldEffect_AddsGoldThroughRun()
        {
            var run = NewRun();
            var koban = Make<GainGoldEffect>(Trigger.OnRoll);
            koban.gold = 3;

            run.effects.Fire(new EffectContext(Trigger.OnRoll) { run = run }, new Source(EffectSourceKind.Engraving, koban));

            Assert.AreEqual(53, run.Gold);
        }

        [Test]
        public void SpendGold_FailsWhenNotEnough()
        {
            var run = NewRun();

            Assert.IsFalse(run.SpendGold(51));
            Assert.AreEqual(50, run.Gold);
            Assert.IsTrue(run.SpendGold(50));
            Assert.AreEqual(0, run.Gold);
        }
    }
}
