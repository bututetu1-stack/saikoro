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
    /// <summary>フェーズ2の状態異常（脆弱・堅守・縛り）と敵の行動・特性（仕様書 第6章・第7章）。</summary>
    public class StatusTests
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
            var pouch = new DicePouch();
            foreach (var d in dice) pouch.Add(d);
            return pouch;
        }

        BattleState Battle(EnemyData enemy, DicePouch pouch, Combatant player = null) =>
            new BattleState(player ?? new Combatant(40), enemy, pouch, new System.Random(0));

        // ---- 状態異常 ----

        [Test]
        public void Vulnerable_TakesOneAndHalf_BeforeBlock()
        {
            var c = new Combatant(40) { block = 3 };
            c.ApplyVulnerable(1);
            Assert.AreEqual(12, c.TakeAttack(10), "10×1.5=15、防御3で12");
        }

        [Test]
        public void Vulnerable_LastsAndTicks()
        {
            var c = new Combatant(40);
            c.ApplyVulnerable(2);
            c.TickStatuses(); // 受けたラウンドは減らない
            Assert.AreEqual(2, c.vulnerable);
            c.TickStatuses();
            Assert.AreEqual(1, c.vulnerable);
        }

        [Test]
        public void Fortify_KeepsHalfBlock()
        {
            var c = new Combatant(40) { block = 9 };
            c.ApplyFortify(1);
            c.EndRoundBlock();
            Assert.AreEqual(4, c.block);
            var plain = new Combatant(40) { block = 9 };
            plain.EndRoundBlock();
            Assert.AreEqual(0, plain.block);
        }

        [Test]
        public void Bind_NextRoundOnlyOneDie()
        {
            var enemy = factory.Enemy(50, new Intent(IntentType.Bind, 0), new Intent(IntentType.Attack, 1), new Intent(IntentType.Attack, 1));
            var battle = Battle(enemy, Pouch(factory.Fixed(1), factory.Fixed(1), factory.Fixed(1), factory.Fixed(1), factory.Fixed(1)));
            Assert.AreEqual(2, battle.MaxDicePerRound);
            battle.Resolve(); // 縛りを受ける
            Assert.AreEqual(1, battle.MaxDicePerRound, "次のラウンドは1個");
            battle.Resolve();
            Assert.AreEqual(2, battle.MaxDicePerRound, "1ラウンドで解除");
        }

        // ---- 敵の行動 ----

        [Test]
        public void PoisonIntent_GivesPoison()
        {
            var enemy = factory.Enemy(50, new Intent(IntentType.Poison, 3), new Intent(IntentType.Attack, 6));
            var player = new Combatant(40);
            var battle = Battle(enemy, Pouch(factory.Fixed(1), factory.Fixed(1)), player);
            var r = battle.Resolve();
            Assert.AreEqual(3, r.playerPoisonDamage, "受けたラウンドの終わりに毒3");
            Assert.AreEqual(2, player.poison);
        }

        [Test]
        public void Charge_StaggeredByEnoughDamage_SkipsBigAttack()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.Charge, 12), new Intent(IntentType.Attack, 25), new Intent(IntentType.Attack, 10));
            var player = new Combatant(40);
            var battle = Battle(enemy, Pouch(factory.Fixed(6), factory.Fixed(6), factory.Fixed(1)), player);
            battle.Roll(battle.pouch.All[0]);
            battle.Roll(battle.pouch.All[1]);
            var r = battle.Resolve(); // 12 ダメージ
            Assert.IsTrue(r.staggered);
            Assert.AreEqual(IntentType.Stunned, battle.EnemyIntent.type, "大攻撃の代わりに怯み");
            battle.Resolve();
            Assert.AreEqual(40, player.hp);
            Assert.AreEqual(IntentType.Attack, battle.EnemyIntent.type);
            Assert.AreEqual(10, battle.EnemyIntent.value, "怯みのあとは次の行動へ");
        }

        [Test]
        public void Charge_NotEnoughDamage_BigAttackComes()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.Charge, 12), new Intent(IntentType.Attack, 25));
            var battle = Battle(enemy, Pouch(factory.Fixed(5), factory.Fixed(6)));
            battle.Roll(battle.pouch.All[0]);
            battle.Roll(battle.pouch.All[1]);
            var r = battle.Resolve(); // 11 ダメージ
            Assert.IsFalse(r.staggered);
            Assert.AreEqual(25, battle.EnemyIntent.value);
        }

        [Test]
        public void SealWithValueTwo_StillSealsOnlyOne()
        {
            // 封印されるのはいつも1個だけ（開発者の判断：何個も封印されると辛すぎる）
            var enemy = factory.Enemy(100, new Intent(IntentType.Seal, 2));
            var pouch = Pouch(factory.Fixed(6), factory.Fixed(5), factory.Fixed(1));
            var battle = Battle(enemy, pouch);
            var r = battle.Resolve();
            Assert.AreEqual(1, r.sealedCount);
            Assert.AreEqual(1, pouch.All.Count(d => d.state == DiceState.Sealed));
        }

        [Test]
        public void Frail_ReducesPlayerBlock()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.Frail, 2), new Intent(IntentType.Attack, 10));
            var pouch = Pouch(factory.Fixed(6), factory.Fixed(5), factory.Fixed(1));
            var battle = Battle(enemy, pouch);
            battle.Resolve(); // 脆弱2を受ける
            Assert.AreEqual(2, battle.player.frail);
            var r = battle.Roll(pouch.All[0]);
            battle.Assign(r, Assignment.Block);
            Assert.AreEqual(4, battle.BlockValue, "6×0.75=4.5→4");
        }

        [Test]
        public void DebuffDice_GiveStatusEqualToRoll()
        {
            // 萎え賽・砕き賽：出目の数だけ脱力・弱体
            var weak = ScriptableObject.CreateInstance<AttackRiderEffect>();
            weak.rider = AttackRider.Weak;
            weak.amount = 0;
            weak.perPip = 1;
            var vulnerable = ScriptableObject.CreateInstance<AttackRiderEffect>();
            vulnerable.rider = AttackRider.Vulnerable;
            vulnerable.amount = 0;
            vulnerable.perPip = 1;
            var enemy = new EnemyState(factory.Enemy(30, new Intent(IntentType.Attack, 1)));
            weak.Apply(new EffectContext(Trigger.OnAttackResolve) { enemy = enemy, value = 3 });
            vulnerable.Apply(new EffectContext(Trigger.OnAttackResolve) { enemy = enemy, value = 2 });
            Assert.AreEqual(3, enemy.weak);
            Assert.AreEqual(2, enemy.vulnerable);
            Object.DestroyImmediate(weak);
            Object.DestroyImmediate(vulnerable);
        }

        [Test]
        public void Frail_ReducesEnemyBlock()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.Attack, 1), new Intent(IntentType.Block, 10));
            var pouch = Pouch(factory.Fixed(6), factory.Fixed(5), factory.Fixed(1));
            var battle = Battle(enemy, pouch);
            battle.enemy.ApplyFrail(2);
            battle.Resolve(); // 次のラウンドの予告は防御10
            Assert.AreEqual(7, battle.enemy.block, "10×0.75=7.5→7");
        }

        [Test]
        public void MirrorAttack_ReturnsLastPlayerAttack()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.MirrorAttack, 0));
            var player = new Combatant(40);
            var battle = Battle(enemy, Pouch(factory.Fixed(6), factory.Fixed(3), factory.Fixed(1)), player);
            Assert.AreEqual(0, battle.EnemyIntent.value, "最初のラウンドは0");
            battle.Roll(battle.pouch.All[0]);
            battle.Roll(battle.pouch.All[1]);
            battle.Resolve(); // 攻撃9
            Assert.AreEqual(IntentType.MirrorAttack, battle.EnemyIntent.type);
            Assert.AreEqual(9, battle.EnemyIntent.value);
        }

        [Test]
        public void CurseIntent_PushesCurseDie()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6) };
            var kake = factory.Data("kake", 0, 0, 1, 1, 2, 2);
            kake.rarity = Rarity.Curse;
            config.curseDice = kake;
            var run = new RunState(config, 1);
            var enemy = factory.Enemy(100, new Intent(IntentType.Curse, 0));
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
            var r = battle.Resolve();

            Assert.IsNotNull(r.curseDie);
            Assert.IsTrue(run.pouch.All.Any(d => d.data == kake));
        }

        // ---- 敵の特性 ----

        [Test]
        public void DamageCap_LimitsDamagePerRound()
        {
            var enemy = factory.Enemy(100, new Intent(IntentType.Attack, 1));
            enemy.damageCapPerRound = 10;
            var battle = Battle(enemy, Pouch(factory.Fixed(9), factory.Fixed(9), factory.Fixed(1)));
            battle.Roll(battle.pouch.All[0]);
            battle.Roll(battle.pouch.All[1]);
            Assert.AreEqual(10, battle.Preview().dealt);
            var r = battle.Resolve();
            Assert.AreEqual(10, r.dealt);
            Assert.AreEqual(90, battle.enemy.hp);
        }

        [Test]
        public void Enrage_DoublesAttackAtHalfHp()
        {
            var enemy = factory.Enemy(80, new Intent(IntentType.Attack, 10));
            enemy.enrageHpPercent = 50;
            var battle = Battle(enemy, Pouch(factory.Fixed(9), factory.Fixed(9), factory.Fixed(9), factory.Fixed(9), factory.Fixed(9)), new Combatant(200));
            Assert.AreEqual(10, battle.EnemyIntent.value);
            battle.enemy.hp = 40;
            battle.Resolve();
            Assert.AreEqual(20, battle.EnemyIntent.value, "HP が半分以下になった次の予告から2倍");
            Assert.IsTrue(battle.enemy.Enraged);
        }
    }
}
