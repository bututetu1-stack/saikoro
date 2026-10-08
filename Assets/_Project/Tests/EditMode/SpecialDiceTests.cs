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
    /// <summary>追加したダイス（特別なルール・毒・黄金・錆び）。</summary>
    public class SpecialDiceTests
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

        RunState RunWith(params DiceData[] dice)
        {
            config.startingDice = new List<DiceData>(dice);
            return new RunState(config, 1);
        }

        EnemyData Dummy(int hp = 999, int attack = 0) => factory.Enemy(hp, new Intent(IntentType.Attack, attack));

        // ---- ピンゾロ賽 ----

        [Test]
        public void Pinzoro_StaysAvailableInBattle_UsedWhenMoving_AndCannotBeForged()
        {
            var data = factory.Data("pinzoro", 1, 1, 1, 1, 1, 1);
            data.keepAvailable = true;
            data.cannotForge = true;
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6), factory.Data("n2", 1, 2, 3, 4, 5, 6));
            var pin = run.pouch.All[0];

            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);
            battle.Roll(pin);
            Assert.AreEqual(DiceState.Available, pin.state, "戦闘で使っても使用済みにならない");

            run.Move(pin);
            Assert.AreEqual(DiceState.Used, pin.state, "移動では使用済みになる（いつでも1マス進めるのは強すぎる）");
            Assert.IsFalse(RunState.CanForge(pin));
            var e = ScriptableObject.CreateInstance<EngravingData>();
            created.Add(e);
            Assert.Throws<System.InvalidOperationException>(() => run.ApplyEngraving(pin, 0, e));
        }

        // ---- 大賽 ----

        [Test]
        public void Oo_CannotMove_SkipTurnWhenOnlyItIsLeft()
        {
            var oo = factory.Data("oo", 3, 4, 5, 6, 7, 8);
            oo.cannotMove = true;
            var normal = factory.Data("n", 1, 1, 1, 1, 1, 1);
            var run = RunWith(oo, normal);

            Assert.Throws<System.InvalidOperationException>(() => run.BeginMove(run.pouch.All[0]), "移動に使えない");
            Assert.IsFalse(run.MustSkipTurn, "普通の賽が使えるうちは休まない");

            run.Move(run.pouch.All[1]);
            Assert.IsTrue(run.MustSkipTurn, "大賽だけになった");

            bool refreshed = run.SkipTurn();
            Assert.IsTrue(refreshed, "大賽を使用済みにするとリフレッシュ");
            Assert.AreEqual(2, run.Turn);
            Assert.AreEqual(2, run.pouch.AvailableCount);
        }

        [Test]
        public void Oo_UsableInBattle()
        {
            var oo = factory.Data("oo", 8, 8, 8, 8, 8, 8);
            oo.cannotMove = true;
            var run = RunWith(oo, factory.Data("n", 1, 2, 3, 4, 5, 6));
            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);

            Assert.AreEqual(8, battle.Roll(run.pouch.All[0]).value);
        }

        // ---- 爆賽 ----

        [Test]
        public void Baku_SixRollsAgainAndAdds()
        {
            var data = factory.Data("baku", 1, 2, 3, 4, 5, 6);
            data.explodeOn = 6;
            var die = new DiceInstance(data);

            bool sawExplosion = false;
            for (int seed = 0; seed < 300; seed++)
            {
                int v = DiceRoller.Roll(die, new System.Random(seed), -1, out int face);
                if (die.faces[face].value == 6)
                {
                    Assert.Greater(v, 6, "6が出たら必ず振り足す");
                    sawExplosion = true;
                }
                else
                {
                    Assert.AreEqual(die.faces[face].value, v);
                }
            }
            Assert.IsTrue(sawExplosion);
        }

        [Test]
        public void Baku_DistributionSumsToOne_AndReachesBeyondSix()
        {
            var data = factory.Data("baku", 1, 2, 3, 4, 5, 6);
            data.explodeOn = 6;
            var dist = DiceRoller.Distribution(new DiceInstance(data), -1);

            Assert.AreEqual(1f, dist.Sum(d => d.probability), 1e-4, "合計1（3回目の振り足しの先は打ち切り）");
            Assert.IsFalse(dist.Any(d => d.value == 6), "6で止まることはない");
            Assert.IsTrue(dist.Any(d => d.value == 7));
        }

        // ---- 鏡賽 ----

        [Test]
        public void Kagami_CopiesLastRoll_FirstIsThree()
        {
            var kagami = factory.Data("kagami", 3, 3, 3, 3, 3, 3);
            kagami.mirror = true;
            var five = factory.Data("five", 5, 5, 5, 5, 5, 5);
            var run = RunWith(kagami, five, five);

            Assert.AreEqual(3, run.Move(run.pouch.All[0]).value, "そのランで最初なら3");
            run.Move(run.pouch.All[1]);
            Assert.AreEqual(5, run.LastRolledValue);

            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);
            battle.Roll(run.pouch.All[2]);  // 5（リフレッシュで鏡賽も戻る）
            var mirrored = battle.Roll(run.pouch.All[0]);
            Assert.AreEqual(5, mirrored.value, "戦闘でも直前の出目を写す");

            CollectionAssert.AreEqual(new[] { (5, 1f) }, DiceRoller.Distribution(run.pouch.All[0], run.LastRolledValue), "止まりうるマスは1か所");
        }

        // ---- 毒賽と毒 ----

        [Test]
        public void Doku_PoisonsTwicePips_TicksAtRoundEnd()
        {
            var effect = Make<ApplyPoisonEffect>(Trigger.OnAttackResolve);
            effect.perPip = 2;
            effect.condition = new EffectCondition { assignment = Assignment.Attack };
            var data = factory.Data("doku", 3, 3, 3, 3, 3, 3);
            data.effects = new List<EffectSO> { effect };
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6));
            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);

            battle.Roll(run.pouch.All[0]);
            var r = battle.Resolve();

            Assert.AreEqual(3, r.dealt);
            Assert.AreEqual(6, r.enemyPoisonDamage, "出目3×2=毒6。ラウンド終了でそのままダメージ");
            Assert.AreEqual(5, battle.enemy.poison, "毎ラウンド−1");
            Assert.AreEqual(999 - 3 - 6, battle.enemy.hp);

            var r2 = battle.Resolve();
            Assert.AreEqual(5, r2.enemyPoisonDamage);
            Assert.AreEqual(4, battle.enemy.poison);
        }

        [Test]
        public void Doku_PlacedOnBlock_DoesNotPoison()
        {
            var effect = Make<ApplyPoisonEffect>(Trigger.OnAttackResolve);
            effect.perPip = 2;
            effect.condition = new EffectCondition { assignment = Assignment.Attack };
            var data = factory.Data("doku", 3, 3, 3, 3, 3, 3);
            data.effects = new List<EffectSO> { effect };
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6));
            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);

            battle.Assign(battle.Roll(run.pouch.All[0]), Assignment.Block);
            battle.Resolve();

            Assert.AreEqual(0, battle.enemy.poison);
        }

        [Test]
        public void Poison_CanKillEnemy_BeforeItActs()
        {
            var effect = Make<ApplyPoisonEffect>(Trigger.OnAttackResolve);
            effect.perPip = 2;
            var data = factory.Data("doku", 3, 3, 3, 3, 3, 3);
            data.effects = new List<EffectSO> { effect };
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6));
            var battle = new BattleState(run.player, Dummy(hp: 8, attack: 5), run.pouch, new System.Random(0), run.effects, run);

            battle.Roll(run.pouch.All[0]);
            var r = battle.Resolve();

            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome, "攻撃3で残り5、毒6で倒れる");
            Assert.AreEqual(0, r.taken, "毒は攻撃のあと・敵の行動の前なので、毒で倒れた敵は攻撃してこない");
        }

        // ---- 黄金賽 ----

        [Test]
        public void Ougon_GoldOnMove_MinusOneInBattle()
        {
            var gold = Make<GainGoldByValueEffect>(Trigger.OnMoveRolled);
            var atk = Make<AddValueEffect>(Trigger.OnAssignAttack);
            atk.add = -1;
            var blk = Make<AddValueEffect>(Trigger.OnAssignDefense);
            blk.add = -1;
            var data = factory.Data("ougon", 4, 4, 4, 4, 4, 4);
            data.effects = new List<EffectSO> { gold, atk, blk };
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6));

            run.Move(run.pouch.All[0]);
            Assert.AreEqual(54, run.Gold, "移動で出目4 → 4G");

            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);
            battle.Roll(run.pouch.All[1]);
            battle.Resolve();
            var r = battle.Roll(run.pouch.All[0]);
            Assert.AreEqual(3, battle.EffectiveValue(r, Assignment.Attack));
            Assert.AreEqual(3, battle.EffectiveValue(r, Assignment.Block));
            Assert.AreEqual(54, run.Gold, "戦闘ではゴールドを得ない");
        }

        // ---- 錆び賽 ----

        [Test]
        public void Sabi_HurtsYouEveryRoll_IgnoringBlock()
        {
            var hurt = Make<SelfDamageEffect>(Trigger.OnRoll);
            hurt.damage = 1;
            var data = factory.Data("sabi", 2, 2, 2, 2, 2, 2);
            data.effects = new List<EffectSO> { hurt };
            var run = RunWith(data, factory.Data("n", 1, 2, 3, 4, 5, 6));

            run.Move(run.pouch.All[0]);
            Assert.AreEqual(39, run.player.hp, "移動で振っても1ダメージ");

            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);
            run.player.block = 10;
            battle.Roll(run.pouch.All[1]);
            battle.Resolve();
            battle.Roll(run.pouch.All[0]);
            Assert.AreEqual(38, run.player.hp, "防御があっても減る");
        }

        [Test]
        public void Sabi_CanKillYouWhenRolling()
        {
            var hurt = Make<SelfDamageEffect>(Trigger.OnRoll);
            hurt.damage = 1;
            var data = factory.Data("sabi", 2, 2, 2, 2, 2, 2);
            data.effects = new List<EffectSO> { hurt };
            var run = RunWith(data, data);
            run.player.hp = 1;
            var battle = new BattleState(run.player, Dummy(), run.pouch, new System.Random(0), run.effects, run);

            battle.Roll(run.pouch.All[0]);

            Assert.AreEqual(BattleOutcome.Defeat, battle.Outcome);
        }

        // ---- 罠の呪い ----

        [Test]
        public void TrapCurse_PicksFromCursePool()
        {
            var kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
            var sabi = factory.Data("sabi", 1, 2, 3, 4, 5, 6);
            sabi.rarity = Rarity.Curse;
            config.curseDicePool = new List<DiceData> { kake, sabi };
            var seen = new HashSet<DiceData>();
            for (int seed = 0; seed < 200 && seen.Count < 2; seed++)
            {
                config.startingDice = new List<DiceData> { factory.Data("n", 1, 2, 3, 4, 5, 6) };
                var run = new RunState(config, seed);
                var t = run.TriggerTrap();
                if (t.kind == TrapKind.Curse) seen.Add(t.curseDie.data);
            }
            CollectionAssert.AreEquivalent(new[] { kake, sabi }, seen);
        }
    }
}
