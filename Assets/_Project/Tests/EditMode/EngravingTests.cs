using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>鍛冶と刻印（仕様書 第5章）。</summary>
    public class EngravingTests
    {
        TestDice factory;
        Phase0Config config;
        readonly List<Object> created = new List<Object>();
        EngravingData zoukyou, kezuri, yaiba, kata, koban, kaze;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<Phase0Config>();
            created.Add(config);

            zoukyou = Numeric("zoukyou", +2);
            kezuri = Numeric("kezuri", -1);
            yaiba = Effect("yaiba", Add(Trigger.OnAssignAttack, 3));
            kata = Effect("kata", Add(Trigger.OnAssignDefense, 3));
            var gold = Make<GainGoldEffect>(Trigger.OnRoll);
            gold.gold = 3;
            koban = Effect("koban", gold);
            var wind = Make<MoveAdjustEffect>(Trigger.OnMoveRolled);
            wind.range = 1;
            kaze = Effect("kaze", wind);
            config.engravingPool = new List<EngravingData> { zoukyou, kezuri, yaiba, kata, koban, kaze };
        }

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

        AddValueEffect Add(Trigger trigger, int add)
        {
            var e = Make<AddValueEffect>(trigger);
            e.add = add;
            return e;
        }

        EngravingData Numeric(string id, int amount)
        {
            var e = ScriptableObject.CreateInstance<EngravingData>();
            e.id = id;
            e.displayName = id;
            e.kind = EngravingKind.Numeric;
            e.op = NumericOp.Add;
            e.amount = amount;
            created.Add(e);
            return e;
        }

        EngravingData Effect(string id, params EffectSO[] effects)
        {
            var e = ScriptableObject.CreateInstance<EngravingData>();
            e.id = id;
            e.displayName = id;
            e.kind = EngravingKind.Effect;
            e.effects = new List<EffectSO>(effects);
            created.Add(e);
            return e;
        }

        RunState RunWith(params DiceData[] dice)
        {
            config.startingDice = new List<DiceData>(dice);
            return new RunState(config, 1);
        }

        // ---- 数値刻印 ----

        [Test]
        public void Numeric_ChangesValue_ClampedZeroToNine()
        {
            var run = RunWith(factory.Data("n", 0, 2, 3, 4, 5, 8));
            var die = run.pouch.All[0];

            run.ApplyEngraving(die, 1, zoukyou);
            Assert.AreEqual(4, die.faces[1].value, "2+2");
            run.ApplyEngraving(die, 5, zoukyou);
            Assert.AreEqual(9, die.faces[5].value, "8+2 は 9 まで");
            run.ApplyEngraving(die, 0, kezuri);
            Assert.AreEqual(0, die.faces[0].value, "0−1 は 0 まで");
            Assert.IsNull(die.faces[1].engraving, "数値刻印は面に残らない");
        }

        [Test]
        public void Numeric_FaceAboveNine_IsNotRaisedFurther()
        {
            var run = RunWith(factory.Data("bakuchi", 0, 0, 0, 10, 10, 10));
            var die = run.pouch.All[0];

            run.ApplyEngraving(die, 3, zoukyou);
            Assert.AreEqual(10, die.faces[3].value);
            run.ApplyEngraving(die, 3, kezuri);
            Assert.AreEqual(9, die.faces[3].value);
        }

        [Test]
        public void Numeric_DoesNotAffectOtherDiceOfSameKind()
        {
            var data = factory.Data("n", 1, 2, 3, 4, 5, 6);
            var run = RunWith(data, data);

            run.ApplyEngraving(run.pouch.All[0], 0, zoukyou);

            Assert.AreEqual(3, run.pouch.All[0].faces[0].value);
            Assert.AreEqual(1, run.pouch.All[1].faces[0].value, "面は個体ごと");
            Assert.AreEqual(1, data.faceValues[0], "元のデータは変わらない");
        }

        // ---- 効果刻印 ----

        [Test]
        public void Effect_IsStoredOnFace_AndOverwritten_CombinesWithNumeric()
        {
            var run = RunWith(factory.Data("n", 1, 2, 3, 4, 5, 6));
            var die = run.pouch.All[0];

            run.ApplyEngraving(die, 5, yaiba);
            Assert.AreSame(yaiba, die.faces[5].engraving);
            run.ApplyEngraving(die, 5, kata);
            Assert.AreSame(kata, die.faces[5].engraving, "効果刻印は1面に1つ、上書き");
            run.ApplyEngraving(die, 5, zoukyou);
            Assert.AreEqual(8, die.faces[5].value);
            Assert.AreSame(kata, die.faces[5].engraving, "数値刻印と併用できる");
        }

        [Test]
        public void Yaiba_OnlyWhenThatFaceIsRolled_AndOnlyForAttack()
        {
            var die = new DiceInstance(factory.Data("n", 1, 2, 3, 4, 5, 6));
            die.faces[5] = new Face(6, yaiba);
            var pouch = new DicePouch();
            pouch.Add(die);
            pouch.Add(factory.Normal());
            var enemy = factory.Enemy(999, new Intent(IntentType.Attack, 1));

            bool sawSix = false, sawOther = false;
            for (int seed = 0; seed < 60 && !(sawSix && sawOther); seed++)
            {
                die.state = DiceState.Available;
                var battle = new BattleState(new Combatant(40), enemy, pouch, new System.Random(seed));
                var r = battle.Roll(die);
                int attack = battle.EffectiveValue(r, Assignment.Attack);
                int block = battle.EffectiveValue(r, Assignment.Block);
                if (r.value == 6)
                {
                    sawSix = true;
                    Assert.AreEqual(9, attack, "刃の面：攻撃+3");
                    Assert.AreEqual(6, block, "防御には効かない");
                }
                else
                {
                    sawOther = true;
                    Assert.AreEqual(r.value, attack, "刃のない面");
                }
            }
            Assert.IsTrue(sawSix && sawOther);
        }

        [Test]
        public void Koban_GivesGoldWhenRolled_InBattleAndOnMove()
        {
            var data = factory.Data("k", 1, 1, 1, 1, 1, 1);
            var run = RunWith(data, data);
            for (int f = 0; f < 6; f++) run.ApplyEngraving(run.pouch.All[0], f, koban);

            run.Move(run.pouch.All[0]);
            Assert.AreEqual(53, run.Gold, "移動で出ても3G");

            var enemy = factory.Enemy(999, new Intent(IntentType.Attack, 1));
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
            battle.Roll(run.pouch.Available.First(d => d == run.pouch.All[1]));
            battle.Resolve();
            battle.Roll(run.pouch.All[0]);
            Assert.AreEqual(56, run.Gold, "戦闘で出ても3G");
        }

        [Test]
        public void Kaze_LetsYouAdjustMoveByOne()
        {
            var data = factory.Data("w", 3, 3, 3, 3, 3, 3);
            config.useBranchingBoard = false;
            var run = RunWith(data, data);
            for (int f = 0; f < 6; f++) run.ApplyEngraving(run.pouch.All[0], f, kaze);

            var move = run.BeginMove(run.pouch.All[0]);
            Assert.IsTrue(move.Adjustable);
            Assert.AreEqual(1, move.adjust);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => run.AdjustMove(move, 5));

            run.AdjustMove(move, 4);
            Assert.IsFalse(move.Adjustable, "選び直しは1回だけ");
            while (!move.Done) run.StepMove(move);
            Assert.AreEqual(4, run.FinishMove(move).to.id);

            var plain = run.BeginMove(run.pouch.All[1]);
            Assert.IsFalse(plain.Adjustable, "風のないダイスは選び直せない");
        }

        // ---- 鍛冶の提示 ----

        [Test]
        public void ForgeOffer_ThreeDistinct_FromPool()
        {
            var run = RunWith(factory.Data("n", 1, 2, 3, 4, 5, 6));
            for (int i = 0; i < 20; i++)
            {
                var offer = run.CreateForgeOffer();
                Assert.AreEqual(3, offer.Count);
                Assert.AreEqual(3, offer.Distinct().Count());
                CollectionAssert.IsSubsetOf(offer, config.engravingPool);
            }
        }
    }
}
