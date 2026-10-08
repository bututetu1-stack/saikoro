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
    /// <summary>フェーズ1のレリック10種（仕様書 第10章）。</summary>
    public class RelicTests
    {
        TestDice factory;
        GameConfig config;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            config.rewardDicePool = new List<DiceData> { factory.Data("normal2", 1, 2, 3, 4, 5, 6) };
        }

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

        RelicData Relic(string id, params EffectSO[] effects)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.id = id;
            r.displayName = id;
            r.effects = new List<EffectSO>(effects);
            created.Add(r);
            return r;
        }

        /// <summary>ポーチが dice だけのラン（直線の盤面）。</summary>
        RunState Run(params DiceInstance[] dice)
        {
            var run = new RunState(config, 1);
            foreach (var d in run.pouch.All.ToList()) run.pouch.Remove(d);
            foreach (var d in dice) run.pouch.Add(d);
            return run;
        }

        BattleState Battle(RunState run, params Intent[] pattern)
        {
            var enemy = factory.Enemy(50, pattern.Length > 0 ? pattern : new[] { new Intent(IntentType.Attack, 8) });
            return new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
        }

        // ---- 草鞋 ----

        RelicData Waraji()
        {
            var e = Fx<ChargedMoveAdjustEffect>(Trigger.OnMoveRolled);
            e.range = 1;
            e.chargesPerLayer = 3;
            e.label = "草鞋";
            return Relic("waraji", e);
        }

        [Test]
        public void Waraji_AdjustUsesChargeOnlyWhenChanged()
        {
            var relic = Waraji();
            var run = Run(factory.Fixed(2), factory.Fixed(2), factory.Fixed(2), factory.Fixed(2), factory.Fixed(2));
            run.AddRelic(relic);
            Assert.AreEqual(3, run.ChargesOf(relic));

            var move = run.BeginMove(run.pouch.All[0]);
            Assert.IsTrue(move.Adjustable);
            Assert.AreEqual("草鞋", move.adjustLabel);
            run.AdjustMove(move, 2); // 変えない
            run.FinishMove(Walk(run, move));
            Assert.AreEqual(3, run.ChargesOf(relic), "出目を変えなければ減らない");

            for (int i = 1; i <= 3; i++)
            {
                move = run.BeginMove(run.pouch.Available.First());
                Assert.IsTrue(move.Adjustable);
                run.AdjustMove(move, 3);
                Assert.AreEqual(3, move.value);
                run.FinishMove(Walk(run, move));
                Assert.AreEqual(3 - i, run.ChargesOf(relic));
            }

            move = run.BeginMove(run.pouch.Available.First());
            Assert.IsFalse(move.Adjustable, "使い切ったら選べない");
        }

        [Test]
        public void Waraji_NotUsedWhenWindEngravingAlreadyAdjusts()
        {
            var wind = Fx<MoveAdjustEffect>(Trigger.OnMoveRolled);
            wind.range = 1;
            var engraving = ScriptableObject.CreateInstance<EngravingData>();
            engraving.kind = EngravingKind.Effect;
            engraving.effects = new List<EffectSO> { wind };
            created.Add(engraving);

            var die = factory.Fixed(2);
            for (int i = 0; i < die.faces.Length; i++) die.faces[i] = new Face(2, engraving);
            var relic = Waraji();
            var run = Run(die, factory.Fixed(2));
            run.AddRelic(relic);

            var move = run.BeginMove(die);
            Assert.AreEqual("風", move.adjustLabel);
            Assert.IsNull(move.adjustCharge);
            run.AdjustMove(move, 1);
            Assert.AreEqual(3, run.ChargesOf(relic), "風で変えたので草鞋は減らない");
        }

        static RunState.MoveInProgress Walk(RunState run, RunState.MoveInProgress move)
        {
            while (!move.Done) run.StepMove(move);
            return move;
        }

        // ---- 銭袋 ----

        [Test]
        public void Zenibukuro_OnlyBattleGoldIsIncreased()
        {
            var e = Fx<ScaleEffect>(Trigger.OnGoldGain);
            e.target = ScaleTarget.Amount;
            e.percent = 125;
            e.condition = new EffectCondition { battleGoldOnly = true };
            var run = Run(factory.Normal());
            run.AddRelic(Relic("zenibukuro", e));

            Assert.AreEqual(25, run.GainGold(20, true));
            Assert.AreEqual(20, run.GainGold(20));
        }

        // ---- 木の盾 ----

        [Test]
        public void Kinotate_BlockAtBattleStartLastsFirstRound()
        {
            var e = Fx<GainBlockEffect>(Trigger.OnBattleStart);
            e.block = 5;
            var run = Run(factory.Fixed(3), factory.Fixed(3));
            run.AddRelic(Relic("kinotate", e));
            var battle = Battle(run, new Intent(IntentType.Attack, 8));

            Assert.AreEqual(5, run.player.block);
            Assert.AreEqual(3, battle.Preview().taken);
            var r = battle.Resolve(); // パス
            Assert.AreEqual(3, r.taken, "攻撃8 − 防御5");
            Assert.AreEqual(0, run.player.block, "2ラウンド目には残らない");
        }

        // ---- 丁の札・半の札 ----

        [TestCase(4, Assignment.Attack, 5)]
        [TestCase(3, Assignment.Attack, 3)]
        [TestCase(4, Assignment.Block, 4)]
        public void Chonofuda_EvenAttackPlusOne(int value, Assignment assignment, int expected)
        {
            var e = Fx<AddValueEffect>(Trigger.OnAssignAttack);
            e.add = 1;
            e.condition = new EffectCondition { parity = ParityCondition.Even, assignment = Assignment.Attack };
            var run = Run(factory.Fixed(value), factory.Fixed(value));
            run.AddRelic(Relic("chonofuda", e));
            var battle = Battle(run);
            var die = battle.Roll(run.pouch.All[0]);

            Assert.AreEqual(expected, battle.EffectiveValue(die, assignment));
        }

        [TestCase(3, Assignment.Block, 4)]
        [TestCase(4, Assignment.Block, 4)]
        [TestCase(3, Assignment.Attack, 3)]
        public void Hannofuda_OddBlockPlusOne(int value, Assignment assignment, int expected)
        {
            var e = Fx<AddValueEffect>(Trigger.OnAssignDefense);
            e.add = 1;
            e.condition = new EffectCondition { parity = ParityCondition.Odd, assignment = Assignment.Block };
            var run = Run(factory.Fixed(value), factory.Fixed(value));
            run.AddRelic(Relic("hannofuda", e));
            var battle = Battle(run);
            var die = battle.Roll(run.pouch.All[0]);

            Assert.AreEqual(expected, battle.EffectiveValue(die, assignment));
        }

        // ---- 小石 ----

        RelicData Koishi()
        {
            var e = Fx<KeepAvailableEffect>(Trigger.OnRoll);
            e.condition = new EffectCondition { minValue = 1, maxValue = 1 };
            return Relic("koishi", e);
        }

        [Test]
        public void Koishi_OneStaysAvailableInBattle()
        {
            var run = Run(factory.Fixed(1), factory.Fixed(2), factory.Fixed(3));
            run.AddRelic(Koishi());
            var battle = Battle(run);
            battle.Roll(run.pouch.All[0]);
            battle.Roll(run.pouch.All[1]);

            Assert.AreEqual(DiceState.Available, run.pouch.All[0].state, "1は使用済みにならない");
            Assert.AreEqual(DiceState.Used, run.pouch.All[1].state);
        }

        [Test]
        public void Koishi_OneStaysAvailableWhenMoving()
        {
            var run = Run(factory.Fixed(1), factory.Fixed(2));
            run.AddRelic(Koishi());
            var move = run.Move(run.pouch.All[0]);

            Assert.AreEqual(1, move.value);
            Assert.AreEqual(DiceState.Available, run.pouch.All[0].state);
            Assert.IsFalse(move.refreshed);
        }

        // ---- 砥石 ----

        [Test]
        public void Toishi_BladeEngravingPlusOne()
        {
            var bladeFx = Fx<AddValueEffect>(Trigger.OnAssignAttack);
            bladeFx.add = 3;
            var blade = ScriptableObject.CreateInstance<EngravingData>();
            blade.kind = EngravingKind.Effect;
            blade.effects = new List<EffectSO> { bladeFx };
            created.Add(blade);

            var bonus = Fx<EngravingBonusEffect>(Trigger.OnAssignAttack);
            bonus.engraving = blade;
            bonus.add = 1;

            var engraved = factory.Fixed(4);
            for (int i = 0; i < engraved.faces.Length; i++) engraved.faces[i] = new Face(4, blade);
            var plain = factory.Fixed(4);
            var run = Run(engraved, plain, factory.Fixed(1));
            run.AddRelic(Relic("toishi", bonus));
            var battle = Battle(run);
            var a = battle.Roll(engraved);
            var b = battle.Roll(plain);

            Assert.AreEqual(8, battle.EffectiveValue(a, Assignment.Attack), "4 + 刃3 + 砥石1");
            Assert.AreEqual(4, battle.EffectiveValue(b, Assignment.Attack), "刻印がなければ何もしない");
            Assert.AreEqual(4, battle.EffectiveValue(a, Assignment.Block), "刃は防御には効かない");
        }

        // ---- 大きな巾着 ----

        [Test]
        public void Kinchaku_PouchCapacityPlusOneOnce()
        {
            var e = Fx<PouchCapacityEffect>(Trigger.OnAcquire);
            e.amount = 1;
            var relic = Relic("kinchaku", e);
            var run = Run(factory.Normal());
            run.AddRelic(relic);
            run.AddRelic(relic); // 同じレリックは2つ持てない

            Assert.AreEqual(DicePouch.DefaultCapacity + 1, run.pouch.Capacity);
            Assert.AreEqual(1, run.Relics.Count);
        }

        // ---- 鈴 ----

        RelicData Suzu()
        {
            var strength = Fx<GainStrengthEffect>(Trigger.OnRefresh);
            strength.strength = 1;
            strength.condition = new EffectCondition { scene = SceneCondition.Battle };
            var heal = Fx<HealEffect>(Trigger.OnRefresh);
            heal.heal = 3;
            heal.condition = new EffectCondition { scene = SceneCondition.Map };
            return Relic("suzu", strength, heal);
        }

        [Test]
        public void Suzu_RefreshInBattleGivesStrength()
        {
            var run = Run(factory.Fixed(3));
            run.AddRelic(Suzu());
            run.player.hp = 20;
            var battle = Battle(run);
            battle.Roll(run.pouch.All[0]); // 最後の1個なのでリフレッシュ

            Assert.AreEqual(1, run.player.strength);
            Assert.AreEqual(20, run.player.hp, "戦闘中は回復しない");
            Assert.AreEqual(4, battle.AttackValue, "出目3 + 筋力1");
        }

        [Test]
        public void Suzu_RefreshWhileMovingHeals()
        {
            var run = Run(factory.Fixed(1));
            run.AddRelic(Suzu());
            run.player.hp = 20;
            var move = run.Move(run.pouch.All[0]);

            Assert.IsTrue(move.refreshed);
            Assert.AreEqual(23, run.player.hp);
            Assert.AreEqual(0, run.player.strength);
        }

        [Test]
        public void Suzu_StrengthGoneAfterBattle()
        {
            var run = Run(factory.Fixed(9));
            run.AddRelic(Suzu());
            var battle = Battle(run, new Intent(IntentType.Attack, 1));
            battle.Roll(run.pouch.All[0]);
            for (int i = 0; i < 10 && battle.Outcome == BattleOutcome.Ongoing; i++)
            {
                if (battle.CanRollMore) battle.Roll(run.pouch.Available.First());
                battle.Resolve();
            }

            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(0, run.player.strength);
            Assert.IsNull(run.CurrentBattle);
        }

        // ---- 早馬 ----

        [Test]
        public void Hayauma_FirstMoveDoubled()
        {
            var e = Fx<ScaleEffect>(Trigger.OnMoveRolled);
            e.target = ScaleTarget.Value;
            e.percent = 200;
            e.condition = new EffectCondition { firstMoveOfLayer = true };
            var run = Run(factory.Fixed(2), factory.Fixed(2), factory.Fixed(2));
            run.AddRelic(Relic("hayauma", e));

            Assert.AreEqual(4, run.Move(run.pouch.All[0]).value);
            Assert.AreEqual(2, run.Move(run.pouch.All[1]).value);
        }

        // ---- 入手 ----

        [Test]
        public void EliteReward_HasRelicNotOwned()
        {
            var a = Relic("a");
            var b = Relic("b");
            config.relicPool = new List<RelicData> { a, b };
            var run = Run(factory.Normal());
            run.AddRelic(a);

            Assert.AreEqual(b, run.CreateBattleReward(RewardKind.Elite).relic);
            Assert.IsNull(run.CreateBattleReward(RewardKind.Normal).relic);
        }

        [Test]
        public void EliteReward_NoRelicWhenAllOwned()
        {
            var a = Relic("a");
            config.relicPool = new List<RelicData> { a };
            var run = Run(factory.Normal());
            run.AddRelic(a);

            Assert.IsNull(run.CreateBattleReward(RewardKind.Elite).relic);
        }
    }
}
