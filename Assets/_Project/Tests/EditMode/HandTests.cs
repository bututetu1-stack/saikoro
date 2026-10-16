using System.Collections.Generic;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>役（チンチロ風の出目の組み合わせ）の判定と、戦闘での効果。</summary>
    public class HandTests
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

        static HandKind Kind(params int[] values) => DiceHands.Evaluate(values).kind;

        // ---- 判定 ----

        [Test]
        public void Evaluate_ThreeDice()
        {
            Assert.AreEqual(HandKind.Pinzoro, Kind(1, 1, 1));
            Assert.AreEqual(HandKind.Triple, Kind(3, 3, 3));
            Assert.AreEqual(HandKind.Shigoro, Kind(6, 4, 5), "並びは問わない");
            Assert.AreEqual(HandKind.Straight, Kind(3, 2, 4));
            Assert.AreEqual(HandKind.Straight, Kind(5, 6, 7), "6より大きい目でも続いていれば連番");
            Assert.AreEqual(HandKind.Hifumi, Kind(2, 3, 1));
            Assert.AreEqual(HandKind.Pair, Kind(5, 2, 5));
            Assert.AreEqual(HandKind.None, Kind(1, 3, 5));
        }

        [Test]
        public void Evaluate_TwoDice_OnlyPair()
        {
            Assert.AreEqual(HandKind.Pair, Kind(4, 4));
            Assert.AreEqual(HandKind.None, Kind(4, 5));
            Assert.AreEqual(HandKind.None, Kind(6));
        }

        [Test]
        public void Evaluate_FourDice_TakesStrongest()
        {
            Assert.AreEqual(HandKind.Straight, Kind(1, 2, 3, 4), "連番（2・3・4）はヒフミより強い");
            Assert.AreEqual(HandKind.Shigoro, Kind(4, 6, 6, 5), "シゴロはゾロ目より強い");
            Assert.AreEqual(HandKind.Triple, Kind(2, 2, 2, 2));
            var pair = DiceHands.Evaluate(new[] { 2, 6, 2, 6 });
            Assert.AreEqual(HandKind.Pair, pair.kind);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, pair.indices, "ゾロ目が2組なら大きいほう");
        }

        [Test]
        public void Evaluate_IgnoresUncountedValues()
        {
            var result = DiceHands.Evaluate(new[] { 1, 1, 1 }, new[] { true, false, false });
            Assert.AreEqual(HandKind.None, result.kind);
        }

        // ---- 戦闘での効果 ----

        BattleState Battle(DicePouch pouch, int enemyHp = 60, params EffectSO[] relicEffects)
        {
            var enemy = factory.Enemy(enemyHp, new Intent(IntentType.Attack, 5));
            EffectBus bus = null;
            if (relicEffects.Length > 0)
            {
                var relic = ScriptableObject.CreateInstance<RelicData>();
                relic.effects = new List<EffectSO>(relicEffects);
                created.Add(relic);
                bus = new EffectBus();
                bus.Register(relic);
            }
            return new BattleState(new Combatant(40), enemy, pouch, new System.Random(0), bus);
        }

        T Fx<T>(Trigger trigger) where T : EffectSO
        {
            var e = ScriptableObject.CreateInstance<T>();
            e.trigger = trigger;
            created.Add(e);
            return e;
        }

        HandModifierEffect Modifier(HandKind hand, int multiplier, int addBonus)
        {
            var e = Fx<HandModifierEffect>(Trigger.OnRoll);
            e.hand = hand;
            e.multiplier = multiplier;
            e.addBonus = addBonus;
            return e;
        }

        DicePouch Pouch(params DiceInstance[] dice)
        {
            var pouch = new DicePouch();
            foreach (var d in dice) pouch.Add(d);
            pouch.Add(factory.Normal()); // 振ったあとにリフレッシュが起きないように、1個余分に入れておく
            return pouch;
        }

        [Test]
        public void Shigoro_DoublesThreeDice()
        {
            var a = factory.Fixed(4);
            var b = factory.Fixed(5);
            var c = factory.Fixed(6);
            var battle = Battle(Pouch(a, b, c));
            battle.Roll(a);
            battle.Roll(b);
            var last = battle.Roll(c);

            Assert.AreEqual(HandKind.Shigoro, battle.Hand);
            Assert.AreEqual(12, battle.EffectiveValue(last, Assignment.Attack), "6 の 2 倍");
            Assert.AreEqual(30, battle.AttackValue, "(4+5+6)×2");
            battle.Use();
            Assert.AreEqual(30, battle.enemy.hp, "60 − 30");
        }

        [Test]
        public void Pair_AddsTwoToBothDice()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var battle = Battle(Pouch(a, b));
            var ra = battle.Roll(a);
            var rb = battle.Roll(b);
            battle.Assign(rb, Assignment.Block);

            Assert.AreEqual(HandKind.Pair, battle.Hand);
            Assert.AreEqual(5, battle.EffectiveValue(ra, Assignment.Attack));
            Assert.AreEqual(5, battle.BlockValue, "防御に置いても +2");
        }

        [Test]
        public void Pinzoro_FiveTimes()
        {
            var a = factory.Fixed(1);
            var b = factory.Fixed(1);
            var c = factory.Fixed(1);
            var battle = Battle(Pouch(a, b, c));
            battle.Roll(a);
            battle.Roll(b);
            battle.Roll(c);
            Assert.AreEqual(HandKind.Pinzoro, battle.Hand);
            Assert.AreEqual(15, battle.AttackValue);
        }

        [Test]
        public void Hifumi_ReturnsDiceToAvailable()
        {
            var a = factory.Fixed(1);
            var b = factory.Fixed(2);
            var c = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, c, factory.Normal()));
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(DiceState.Used, a.state, "役ができるまでは使用済み");
            battle.Roll(c);

            Assert.AreEqual(HandKind.Hifumi, battle.Hand);
            Assert.AreEqual(DiceState.Available, a.state);
            Assert.AreEqual(DiceState.Available, b.state);
            Assert.AreEqual(DiceState.Available, c.state);
            Assert.AreEqual(6, battle.AttackValue, "出目はそのまま");
        }

        [Test]
        public void SameDieTwice_DoesNotMakeHand()
        {
            // ピンゾロ賽のように戦闘では使用済みにならないダイスを、1ラウンドに3回振っても役にならない
            var data = factory.Data("pin", 1, 1, 1, 1, 1, 1);
            data.keepAvailable = true;
            var pin = new DiceInstance(data);
            var battle = Battle(Pouch(pin));
            battle.Roll(pin);
            battle.Roll(pin);
            battle.Roll(pin);
            Assert.AreEqual(HandKind.None, battle.Hand);
            Assert.AreEqual(3, battle.AttackValue);
        }

        [Test]
        public void UsedDie_KeepsValueWhenHandUpgrades()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var c = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, c));
            battle.Roll(a);
            battle.Roll(b);
            var u = battle.Use();
            Assert.AreEqual(10, u.attack, "ゾロ目で (3+2)×2");

            var rc = battle.Roll(c);
            Assert.AreEqual(HandKind.Triple, battle.Hand, "3個目でアラシになる");
            Assert.AreEqual(6, battle.EffectiveValue(rc, Assignment.Attack), "まだ使っていない3個目だけが 2 倍");
            Assert.AreEqual(6, battle.AttackValue);
        }

        [Test]
        public void Hand_AppliesAfterDiceEffects()
        {
            // 攻撃+2 の剣賽（出目4）でシゴロ：(4+2)×2 = 12。丁半などの出目の条件は、役の前の出目で見る
            var add = ScriptableObject.CreateInstance<AddValueEffect>();
            add.trigger = Trigger.OnAssignAttack;
            add.add = 2;
            created.Add(add);
            var kenData = factory.Data("ken", 4, 4, 4, 4, 4, 4);
            kenData.effects = new List<EffectSO> { add };
            var ken = new DiceInstance(kenData);
            var b = factory.Fixed(5);
            var c = factory.Fixed(6);
            var battle = Battle(Pouch(ken, b, c));
            var rk = battle.Roll(ken);
            battle.Roll(b);
            battle.Roll(c);
            Assert.AreEqual(12, battle.EffectiveValue(rk, Assignment.Attack));
        }

        [Test]
        public void HandsDisabled_NoBonus()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var battle = Battle(Pouch(a, b));
            battle.HandsEnabled = false;
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(HandKind.None, battle.Hand);
            Assert.AreEqual(6, battle.AttackValue);
        }

        // ---- リーチ ----

        [Test]
        public void HandIfNext_ShowsStrongerHandOnly()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(4);
            var battle = Battle(Pouch(a, b, factory.Normal()));
            battle.Roll(a);
            Assert.AreEqual(HandKind.Pair, battle.HandIfNext(3), "1個目が3なら、3でゾロ目");
            Assert.AreEqual(HandKind.None, battle.HandIfNext(5));
            battle.Roll(b);
            Assert.AreEqual(HandKind.Straight, battle.HandIfNext(5), "3・4 なら 5 で連番");
            Assert.AreEqual(HandKind.Straight, battle.HandIfNext(2));
            Assert.AreEqual(HandKind.Pair, battle.HandIfNext(4));
            Assert.AreEqual(HandKind.None, battle.HandIfNext(6));
        }

        [Test]
        public void HandReachFaces_CountsFaces()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(4);
            var normal = factory.Normal();
            var battle = Battle(Pouch(a, b, normal));
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(4, battle.HandReachFaces(normal), "普通の賽なら 2・3・4・5 の4面で役");
            Assert.AreEqual(0, battle.HandReachFaces(a), "このラウンドにもう振ったダイスは役に数えない");
        }

        // ---- 役を育てるレリック ----

        [Test]
        public void Tsubofuri_PairPlusFour()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var battle = Battle(Pouch(a, b), 60, Modifier(HandKind.Pair, 0, 2));
            var ra = battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(7, battle.EffectiveValue(ra, Assignment.Attack), "3 + 4");
            Assert.AreEqual(4, battle.HandBonus);
        }

        [Test]
        public void HifumiFuda_TripleAndStillReturns()
        {
            var a = factory.Fixed(1);
            var b = factory.Fixed(2);
            var c = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, c), 60, Modifier(HandKind.Hifumi, 3, 0));
            battle.Roll(a);
            battle.Roll(b);
            battle.Roll(c);
            Assert.AreEqual(18, battle.AttackValue, "(1+2+3)×3");
            Assert.AreEqual(DiceState.Available, a.state, "使用可能に戻るのはそのまま");
        }

        [Test]
        public void Arashidaiko_TripleThreeTimes_NotLowerThanBase()
        {
            var a = factory.Fixed(2);
            var b = factory.Fixed(2);
            var c = factory.Fixed(2);
            var battle = Battle(Pouch(a, b, c), 60, Modifier(HandKind.Triple, 3, 0), Modifier(HandKind.Shigoro, 1, 0));
            battle.Roll(a);
            battle.Roll(b);
            battle.Roll(c);
            Assert.AreEqual(18, battle.AttackValue, "2×3 を3個");
        }

        [Test]
        public void Yakuchou_BlockOncePerRound()
        {
            var block = Fx<GainBlockEffect>(Trigger.OnHandFormed);
            block.block = 4;
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var c = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, c), 60, block);
            battle.Roll(a);
            Assert.AreEqual(0, battle.player.block, "役ができるまでは何もない");
            battle.Roll(b);
            Assert.AreEqual(4, battle.player.block, "ゾロ目ができて防御4");
            battle.Roll(c);
            Assert.AreEqual(4, battle.player.block, "アラシに強くなっても1ラウンドに1回だけ");
        }

        [Test]
        public void Kiseru_OneMoreDieAfterHand()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, factory.Normal(), factory.Normal()), 60, Fx<HandExtraDieEffect>(Trigger.OnRoll));
            Assert.AreEqual(3, battle.MaxDicePerRound);
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(4, battle.MaxDicePerRound, "ゾロ目ができたので、もう1個");
        }

        [Test]
        public void NewRound_ClearsHand()
        {
            var a = factory.Fixed(3);
            var b = factory.Fixed(3);
            var battle = Battle(Pouch(a, b, factory.Normal(), factory.Normal()), 200);
            battle.Roll(a);
            battle.Roll(b);
            Assert.AreEqual(HandKind.Pair, battle.Hand);
            battle.Resolve();
            Assert.AreEqual(HandKind.None, battle.Hand);
        }
    }
}
