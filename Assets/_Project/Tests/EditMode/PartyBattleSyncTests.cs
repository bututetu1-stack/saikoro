using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>
    /// 協力の戦闘を通信で遊ぶとき：仲間のランは写し（RunSave）から作り直して、全員の端末で同じ計算をする。
    /// 本物のランで戦う端末と、写しで戦う端末が、同じ操作で同じ結果になることを確かめる。
    /// </summary>
    public class PartyBattleSyncTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        static PartyMember Member(RunState r) => new PartyMember { player = r.player, pouch = r.pouch, effects = r.effects, run = r, rng = r.random.Battle };

        [Test]
        public void Snapshot_RoundTrip_KeepsDiceRelicsHpAndRandom()
        {
            var config = LoadConfig();
            var starters = RunState.StarterOptions(config, 404);
            var run = new RunState(config, 404, starters.FirstOrDefault());
            run.player.hp -= 7;
            run.random.Battle.Next(); // 乱数を進めておく
            var copy = RunState.RestoreBattleSnapshot(config, run.CreateBattleSnapshot());
            Assert.AreEqual(run.player.hp, copy.player.hp);
            Assert.AreEqual(run.player.maxHp, copy.player.maxHp);
            CollectionAssert.AreEqual(run.pouch.All.Select(d => d.DisplayName), copy.pouch.All.Select(d => d.DisplayName));
            CollectionAssert.AreEqual(run.Relics.Select(r => r.id), copy.Relics.Select(r => r.id));
            CollectionAssert.AreEqual(run.random.Counts, copy.random.Counts, "乱数の進み具合も同じ");
            Assert.AreEqual(run.random.Battle.Next(), copy.random.Battle.Next(), "このあと振る目も同じ");
            Assert.AreEqual(run.Current.id, copy.Current.id);
        }

        [Test]
        public void TwoClients_RealAndRestoredCopies_PlayTheSameBattle()
        {
            var config = LoadConfig();
            const int seed = 909;
            var starters = RunState.StarterOptions(config, seed);
            // 2人のラン（違うスターター）
            var a = new RunState(config, seed, starters.ElementAtOrDefault(0));
            var b = new RunState(config, seed, starters.ElementAtOrDefault(1));
            string snapA = a.CreateBattleSnapshot();
            string snapB = b.CreateBattleSnapshot();

            // 端末1：a は本物、b は写し。端末2：a は写し、b は本物
            var enemy = config.layers[0].battleEnemies[0];
            var client1 = new PartyBattle(new[] { Member(a), Member(RunState.RestoreBattleSnapshot(config, snapB)) }, new[] { enemy }, 77);
            var client2 = new PartyBattle(new[] { Member(RunState.RestoreBattleSnapshot(config, snapA)), Member(b) }, new[] { enemy }, 77);

            for (int round = 0; round < 30 && client1.Outcome == BattleOutcome.Ongoing; round++)
            {
                // 同じ操作：席ごとに、賽袋の先頭から振れるだけ振り、偶数番目は攻撃・奇数番目は防御
                foreach (var pb in new[] { client1, client2 })
                {
                    foreach (var m in pb.Members.Where(m => m.Outcome == BattleOutcome.Ongoing))
                    {
                        var indices = m.pouch.All.Select((d, i) => (d, i)).Where(x => x.d.state == DiceState.Available).Take(m.MaxDicePerRound).Select(x => x.i).ToList();
                        foreach (int i in indices)
                        {
                            if (!m.CanRollMore || m.Outcome != BattleOutcome.Ongoing) break;
                            m.Roll(m.pouch.All[i]);
                        }
                        for (int k = 0; k < m.Rolled.Count; k++) m.Assign(m.Rolled[k], k % 2 == 0 ? Assignment.Attack : Assignment.Block);
                    }
                }
                for (int s = 0; s < 2; s++)
                {
                    CollectionAssert.AreEqual(client1.Members[s].Rolled.Select(r => r.value), client2.Members[s].Rolled.Select(r => r.value), $"ラウンド{round + 1}：席{s}の出目");
                }
                client1.Resolve();
                client2.Resolve();
                for (int s = 0; s < 2; s++)
                {
                    Assert.AreEqual(client1.Members[s].player.hp, client2.Members[s].player.hp, $"ラウンド{round + 1}：席{s}の HP");
                    Assert.AreEqual(client1.Members[s].Outcome, client2.Members[s].Outcome);
                }
                CollectionAssert.AreEqual(client1.enemies.Select(e => e.hp), client2.enemies.Select(e => e.hp), $"ラウンド{round + 1}：敵の HP");
                CollectionAssert.AreEqual(client1.enemies.Select(e => e.targetSlot), client2.enemies.Select(e => e.targetSlot), "敵の狙い");
            }
            Assert.AreEqual(client1.Outcome, client2.Outcome);
            Assert.AreNotEqual(BattleOutcome.Ongoing, client1.Outcome, "30 ラウンドまでに決着");
        }

        [Test]
        public void TwoClients_CharmUsedOnRealAndCopy_GiveTheSameResult()
        {
            var config = LoadConfig();
            var strength = config.charmPool.FirstOrDefault(c => c != null && c.kind == CharmKind.GainStrength);
            var poison = config.charmPool.FirstOrDefault(c => c != null && c.kind == CharmKind.PoisonEnemy);
            if (strength == null || poison == null) Assert.Ignore("力の札・毒の薬がデータにありません。");
            var a = new RunState(config, 515);
            a.AddCharm(strength);
            a.AddCharm(poison);
            string snap = a.CreateBattleSnapshot();
            var enemy = config.layers[0].battleEnemies[0];
            var b = new RunState(config, 515);
            string snapB = b.CreateBattleSnapshot(); // 戦闘を作る前に（戦闘中は写しを作れない）

            var copy = RunState.RestoreBattleSnapshot(config, snap);
            CollectionAssert.AreEqual(a.Charms.Select(c => c.id), copy.Charms.Select(c => c.id), "写しにもお守りがある");
            var client1 = new PartyBattle(new[] { Member(a), Member(b) }, new[] { enemy }, 3);
            var client2 = new PartyBattle(new[] { Member(copy), Member(RunState.RestoreBattleSnapshot(config, snapB)) }, new[] { enemy }, 3);

            // 同じ順に、同じお守りを使う（番号で指す）
            foreach (var (pb, r) in new[] { (client1, a), (client2, copy) })
            {
                var m = pb.Members[0];
                r.UseCharm(r.Charms[0]);              // 力の札
                r.UseEnemyCharm(r.Charms[0], m);      // 毒の薬（狙っている敵に）
            }
            Assert.AreEqual(client1.Members[0].player.strength, client2.Members[0].player.strength);
            CollectionAssert.AreEqual(client1.enemies.Select(e => e.poison), client2.enemies.Select(e => e.poison));
            Assert.AreEqual(0, a.Charms.Count);
            Assert.AreEqual(0, copy.Charms.Count);
        }
    }
}
