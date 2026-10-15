using System;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Tests
{
    /// <summary>協力「賽の旅団」の戦闘（全員で同じ敵と戦う）。</summary>
    public class PartyBattleTests
    {
        TestDice factory;

        static Intent Attack(int v) => new Intent(IntentType.Attack, v);

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        PartyMember Member(int hp, params DiceInstance[] dice)
        {
            var pouch = new DicePouch();
            foreach (var d in dice) pouch.Add(d);
            pouch.Add(factory.Normal()); // 振らずに残しておく（リフレッシュさせない）
            return new PartyMember { player = new Combatant(hp), pouch = pouch, rng = new Random(0) };
        }

        [Test]
        public void EnemyHp_GrowsWithPartySize()
        {
            var enemy = factory.Enemy(100, Attack(5));
            var solo = new PartyBattle(new[] { Member(40) }, new[] { enemy }, 1);
            var duo = new PartyBattle(new[] { Member(40), Member(40) }, new[] { enemy }, 1);
            Assert.AreEqual(100, solo.enemies[0].maxHp);
            Assert.AreEqual(100 * (100 + PartyBattle.HpPercentPerExtraMember) / 100, duo.enemies[0].maxHp);
        }

        [Test]
        public void BothAttack_SameEnemy_EnemyHitsOnlyItsTarget()
        {
            var fiveA = factory.Fixed(5);
            var fiveB = factory.Fixed(5);
            var a = Member(40, fiveA);
            var b = Member(40, fiveB);
            var battle = new PartyBattle(new[] { a, b }, new[] { factory.Enemy(100, Attack(8)) }, 3);
            var enemy = battle.enemies[0];
            int target = enemy.targetSlot;

            battle.Members[0].Assign(battle.Members[0].Roll(fiveA), Assignment.Attack);
            battle.Members[1].Assign(battle.Members[1].Roll(fiveB), Assignment.Attack);
            var results = battle.Resolve();

            Assert.AreEqual(170 - 10, enemy.hp, "2人の攻撃が両方当たる（HP は 2人で 170%）");
            Assert.AreEqual(8, results[target].Value.taken, "狙われた人だけが攻撃を受ける");
            Assert.AreEqual(0, results[1 - target].Value.taken, "狙われていない人は受けない");
            Assert.AreEqual(40 - 8, battle.Members[target].player.hp);
            Assert.AreEqual(40, battle.Members[1 - target].player.hp);
        }

        [Test]
        public void DownedMember_SitsOut_AndGetsUpWithOneHp_OnVictory()
        {
            var bigA = factory.Fixed(9);
            var bigB = factory.Fixed(9);
            var a = Member(5, bigA);
            var b = Member(200, bigB);
            var battle = new PartyBattle(new[] { a, b }, new[] { factory.Enemy(20, Attack(30)) }, 7);
            // 1ラウンド目：誰も攻撃しない → 狙われた人が倒れる（HP 5 の人が狙われたときだけ倒れる）
            battle.Resolve();
            int target = Enumerable.Range(0, 2).First(i => battle.Members[i].History.Count > 0 && battle.Members[i].History[0].taken > 0);
            bool downed = battle.Members[target].player.IsDead;
            if (!downed)
            {
                Assert.Pass("頑丈な人が狙われた（この乱数では倒れなかった）");
                return;
            }
            Assert.AreEqual(BattleOutcome.Defeat, battle.Members[target].Outcome);
            Assert.AreEqual(BattleOutcome.Ongoing, battle.Outcome, "1人残っていれば続く");

            // 残った人が倒しきる
            var other = battle.Members[1 - target];
            var die = 1 - target == 0 ? bigA : bigB;
            other.Assign(other.Roll(die), Assignment.Attack);
            for (int i = 0; i < 10 && battle.Outcome == BattleOutcome.Ongoing; i++)
            {
                battle.Resolve();
                foreach (var d in other.pouch.Available.ToList())
                {
                    if (other.CanRollMore && battle.Outcome == BattleOutcome.Ongoing) other.Assign(other.Roll(d), Assignment.Attack);
                }
            }
            Assert.AreEqual(BattleOutcome.Victory, battle.Outcome);
            Assert.AreEqual(1, battle.Members[target].player.hp, "倒れていた人は HP 1 で起き上がる");
        }

        [Test]
        public void AllDown_PartyLoses()
        {
            var battle = new PartyBattle(new[] { Member(5), Member(5) }, new[] { factory.Enemy(100, Attack(30)), factory.Enemy(100, Attack(30)) }, 11);
            for (int i = 0; i < 10 && battle.Outcome == BattleOutcome.Ongoing; i++) battle.Resolve();
            Assert.AreEqual(BattleOutcome.Defeat, battle.Outcome);
        }

        [Test]
        public void ShareCharm_HelpsOtherActiveMembers()
        {
            var battle = new PartyBattle(new[] { Member(40), Member(40), Member(40) }, new[] { factory.Enemy(100, Attack(5)) }, 5);
            var heal = UnityEngine.ScriptableObject.CreateInstance<SaiNoMichi.Run.CharmData>();
            var block = UnityEngine.ScriptableObject.CreateInstance<SaiNoMichi.Run.CharmData>();
            try
            {
                heal.kind = SaiNoMichi.Run.CharmKind.HealParty;
                heal.amount = 10;
                block.kind = SaiNoMichi.Run.CharmKind.BlockParty;
                block.amount = 8;
                foreach (var m in battle.Members) m.player.hp = 20;
                Assert.AreEqual(2, battle.ShareCharm(0, heal), "使った本人は数えない");
                Assert.AreEqual(20, battle.Members[0].player.hp, "本人の分は RunState.UseCharm で");
                Assert.AreEqual(30, battle.Members[1].player.hp);
                Assert.AreEqual(30, battle.Members[2].player.hp);
                Assert.AreEqual(2, battle.ShareCharm(1, block));
                Assert.AreEqual(8, battle.Members[0].player.block);
                Assert.AreEqual(0, battle.Members[1].player.block);
                Assert.AreEqual(8, battle.Members[2].player.block);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(heal);
                UnityEngine.Object.DestroyImmediate(block);
            }
        }
    }
}
