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

namespace SaiNoMichi.Tests
{
    /// <summary>フェーズ2で足したレリック（仕様書 第10章）。</summary>
    public class RelicPhase2Tests
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
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("two", 2, 2, 2, 2, 2, 2), factory.Data("two2", 2, 2, 2, 2, 2, 2) };
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

        RelicData Relic(params EffectSO[] effects)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.effects = new List<EffectSO>(effects);
            created.Add(r);
            return r;
        }

        BattleState Battle(RunState run) =>
            new BattleState(run.player, factory.Enemy(50, new Intent(IntentType.Attack, 1)), run.pouch, new System.Random(0), run.effects, run);

        DiceInstance Die(RunState run, string id) => run.pouch.All.First(d => d.data.id == id);

        [Test]
        public void Yakusoubukuro_RestHealPlus50Percent()
        {
            var run = new RunState(config, 1);
            Assert.AreEqual(12, run.RestHealAmount, "最大HP40の30%");
            var bonus = Fx<StatBonusEffect>(Trigger.OnAcquire);
            bonus.stat = RunStat.RestHealPercent;
            bonus.amount = 50;
            run.AddRelic(Relic(bonus));
            Assert.AreEqual(18, run.RestHealAmount, "30%×1.5＝45% → 18");
        }

        [Test]
        public void Dokutsubo_PoisonPlusOne()
        {
            var run = new RunState(config, 1);
            var bonus = Fx<StatBonusEffect>(Trigger.OnAcquire);
            bonus.stat = RunStat.PoisonBonus;
            bonus.amount = 1;
            run.AddRelic(Relic(bonus));

            var poison = Fx<ApplyPoisonEffect>(Trigger.OnAssignAttack);
            poison.flat = 2;
            var enemy = new EnemyState(factory.Enemy(30, new Intent(IntentType.Attack, 1)));
            poison.Apply(new EffectContext(Trigger.OnAssignAttack) { run = run, enemy = enemy });
            Assert.AreEqual(3, enemy.poison);
        }

        [Test]
        public void Tenbin_HealsOnlyWhenAttackEqualsBlock()
        {
            var run = new RunState(config, 1);
            var fx = Fx<BalanceHealEffect>(Trigger.OnRoundEnd);
            run.player.LoseHp(10);
            fx.Apply(new EffectContext(Trigger.OnRoundEnd) { run = run, player = run.player, value = 5, amount = 4 });
            Assert.AreEqual(30, run.player.hp, "違う値なら回復しない");
            fx.Apply(new EffectContext(Trigger.OnRoundEnd) { run = run, player = run.player, value = 0, amount = 0 });
            Assert.AreEqual(30, run.player.hp, "両方0なら回復しない");
            fx.Apply(new EffectContext(Trigger.OnRoundEnd) { run = run, player = run.player, value = 5, amount = 5 });
            Assert.AreEqual(33, run.player.hp);
        }

        [Test]
        public void Tenbin_FiresAtRoundEndInBattle()
        {
            var run = new RunState(config, 1);
            run.AddRelic(Relic(Fx<BalanceHealEffect>(Trigger.OnRoundEnd)));
            run.player.LoseHp(10);
            var battle = Battle(run);
            var a = battle.Roll(Die(run, "two"));
            var b = battle.Roll(Die(run, "two2"));
            battle.Assign(a, Assignment.Attack);
            battle.Assign(b, Assignment.Block);
            battle.Resolve();
            // 攻撃2・防御2 → 3回復。敵の攻撃1は防御で受ける
            Assert.AreEqual(33, run.player.hp);
        }

        [Test]
        public void Chokinbako_GoldEveryTenSteps()
        {
            var run = new RunState(config, 1);
            var fx = Fx<StepGoldEffect>(Trigger.OnStep);
            int gold = run.Gold;
            fx.Apply(new EffectContext(Trigger.OnStep) { run = run, amount = 9 });
            Assert.AreEqual(gold, run.Gold);
            fx.Apply(new EffectContext(Trigger.OnStep) { run = run, amount = 10 });
            Assert.AreEqual(gold + 8, run.Gold);
            fx.Apply(new EffectContext(Trigger.OnStep) { run = run, amount = 20 });
            Assert.AreEqual(gold + 16, run.Gold);
        }

        [Test]
        public void TotalSteps_CountsEachStep()
        {
            var run = new RunState(config, 1);
            var move = run.BeginMove(Die(run, "two"));
            while (move.remaining > 0) run.StepMove(move, run.NeedsBranchChoice(move) ? run.Current.next.First() : null);
            Assert.AreEqual(2, run.TotalSteps);
        }

        [Test]
        public void Zoromenomamori_PairBecomesBothSides()
        {
            var run = new RunState(config, 1);
            var battle = Battle(run);
            var a = battle.Roll(Die(run, "two"));
            var b = battle.Roll(Die(run, "two2"));
            Assert.IsFalse(a.bothSides || b.bothSides, "レリックなしでは両方には効かない");

            var run2 = new RunState(config, 1);
            run2.AddRelic(Relic(Fx<PairBothSidesEffect>(Trigger.OnRoll)));
            var battle2 = Battle(run2);
            var c = battle2.Roll(Die(run2, "two"));
            Assert.IsFalse(c.bothSides, "1個だけではゾロ目でない");
            var d = battle2.Roll(Die(run2, "two2"));
            Assert.IsTrue(c.bothSides && d.bothSides);
        }

        [Test]
        public void Unmeinoito_SetValueOncePerBattle()
        {
            var run = new RunState(config, 1);
            var battle = Battle(run);
            Assert.IsFalse(battle.CanUseFate);

            var run2 = new RunState(config, 1);
            run2.AddRelic(Relic(Fx<FateThreadEffect>(Trigger.OnBattleStart)));
            var battle2 = Battle(run2);
            Assert.IsTrue(battle2.CanUseFate);
            var r = battle2.Roll(Die(run2, "two"));
            battle2.UseFate(r, 6);
            Assert.AreEqual(6, r.value);
            Assert.IsFalse(battle2.CanUseFate, "1戦闘に1回");
            Assert.Throws<System.InvalidOperationException>(() => battle2.UseFate(r, 5));
        }

        [Test]
        public void Senrigan_ForeseenValueIsRolled()
        {
            var run = new RunState(config, 7);
            var normal = Die(run, "normal");
            Assert.IsNull(run.ForeseeRoll(normal), "千里眼なしでは見えない");

            for (int seed = 0; seed < 20; seed++)
            {
                var r = new RunState(config, seed);
                r.AddRelic(Relic(Fx<ForesightEffect>(Trigger.OnMoveRolled)));
                var die = Die(r, "normal");
                int? seen = r.ForeseeRoll(die);
                Assert.IsNotNull(seen);
                Assert.AreEqual(seen, r.ForeseeRoll(die), "何度見ても同じ");
                var move = r.BeginMove(die);
                Assert.AreEqual(seen.Value, move.value, $"seed {seed}");
            }
        }

        [Test]
        public void Yatate_PeekedEventHappens()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var run = new RunState(config, seed);
                run.AddRelic(Relic(Fx<RevealMapEffect>(Trigger.OnAcquire)));
                Assert.IsTrue(run.CanSeeContents);
                var tile = run.board.tiles.FirstOrDefault(t => t.type == TileType.Event);
                if (tile == null) continue;
                var seen = run.PeekEvent(tile);
                Assert.IsNotNull(seen);
                Assert.AreEqual(seen.Value, run.PickEvent(tile), $"seed {seed}");
            }
        }

        [Test]
        public void Rokunokago_SixIsBothSidesInBattle()
        {
            var flag = Fx<FlagEffect>(Trigger.OnRoll);
            flag.flag = RollFlag.BothSides;
            flag.condition = new EffectCondition { minValue = 6, maxValue = 6, scene = SceneCondition.Battle };
            var six = factory.Data("six", 6, 6, 6, 6, 6, 6);
            config.startingDice = new List<DiceData> { six, factory.Data("two", 2, 2, 2, 2, 2, 2) };
            var run = new RunState(config, 1);
            run.AddRelic(Relic(flag));
            var battle = Battle(run);
            Assert.IsTrue(battle.Roll(Die(run, "six")).bothSides);
            Assert.IsFalse(battle.Roll(Die(run, "two")).bothSides);
        }
    }
}
