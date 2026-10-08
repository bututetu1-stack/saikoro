using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SaiNoMichi.Tests
{
    /// <summary>セーブと続きから。</summary>
    public class SaveTests
    {
        TestDice factory;
        GameConfig config;
        RelicData kinchaku, waraji;
        EngravingData engraving;
        CharmData charm;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            created.Add(config);
            config.useBranchingBoard = true;
            config.startingDice = new List<DiceData> { factory.Data("normal", 1, 2, 3, 4, 5, 6), factory.Data("low", 1, 1, 2, 2, 3, 3), factory.Data("high", 4, 4, 5, 5, 6, 6) };
            var reward = factory.Data("reward", 2, 3, 4, 5, 6, 7);
            config.rewardDicePool = new List<DiceData> { reward };
            var enemy = factory.Enemy(20, new Intent(IntentType.Attack, 3));
            enemy.id = "slime";
            enemy.earlyOk = true;
            config.layers = new List<LayerData>
            {
                new LayerData { displayName = "一", battleEnemies = new List<EnemyData> { enemy }, eliteEnemies = new List<EnemyData> { enemy }, boss = enemy },
                new LayerData { displayName = "二", battleEnemies = new List<EnemyData> { enemy }, eliteEnemies = new List<EnemyData> { enemy }, boss = enemy },
            };

            var capacity = Fx<PouchCapacityEffect>(Trigger.OnAcquire);
            capacity.amount = 1;
            kinchaku = Relic("kinchaku", capacity);
            var adjust = Fx<ChargedMoveAdjustEffect>(Trigger.OnMoveRolled);
            adjust.range = 1;
            adjust.chargesPerLayer = 3;
            waraji = Relic("waraji", adjust);
            config.relicPool = new List<RelicData> { kinchaku, waraji };

            engraving = ScriptableObject.CreateInstance<EngravingData>();
            engraving.id = "yaiba";
            created.Add(engraving);
            config.engravingPool = new List<EngravingData> { engraving };

            charm = ScriptableObject.CreateInstance<CharmData>();
            charm.id = "kizugusuri";
            charm.kind = CharmKind.Heal;
            charm.amount = 12;
            created.Add(charm);
            config.charmPool = new List<CharmData> { charm };
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

        static RunState RoundTrip(GameConfig config, RunState run) => RunState.Restore(config, RunSave.FromJson(run.CreateSave().ToJson()));

        static void Walk(RunState run, int dieIndex)
        {
            var die = run.pouch.All[dieIndex];
            var move = run.BeginMove(die);
            while (!move.Done) run.StepMove(move, run.NeedsBranchChoice(move) ? run.Current.next.Last() : null);
            run.FinishMove(move);
        }

        // ---- 乱数 ----

        [Test]
        public void SeededRandom_SameValuesAsSystemRandom()
        {
            var a = new System.Random(123);
            var b = new SeededRandom(123);
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(a.Next(), b.Next());
                Assert.AreEqual(a.Next(6), b.Next(6));
                Assert.AreEqual(a.Next(1, 7), b.Next(1, 7));
                Assert.AreEqual(a.NextDouble(), b.NextDouble());
            }
        }

        [Test]
        public void SeededRandom_RestoreFromCount()
        {
            var a = new SeededRandom(77);
            for (int i = 0; i < 50; i++) { a.Next(); a.Next(10); a.NextDouble(); }
            var b = new SeededRandom(77, a.Count);
            for (int i = 0; i < 20; i++) Assert.AreEqual(a.Next(100), b.Next(100));
        }

        [Test]
        public void RunRandom_RestoreFromCounts()
        {
            var a = new RunRandom(5);
            a.Battle.Next(6); a.Battle.Next(6); a.Reward.Next(100); a.Event.Next();
            var b = new RunRandom(5, a.Counts);
            Assert.AreEqual(a.Map.Next(), b.Map.Next());
            Assert.AreEqual(a.Battle.Next(6), b.Battle.Next(6));
            Assert.AreEqual(a.Reward.Next(100), b.Reward.Next(100));
            Assert.AreEqual(a.Move.Next(6), b.Move.Next(6));
            Assert.AreEqual(a.Event.Next(), b.Event.Next());
        }

        // ---- ランの中身 ----

        [Test]
        public void RoundTrip_KeepsEverything()
        {
            var run = new RunState(config, 42);
            Walk(run, 0);
            Walk(run, 2);
            run.AddRelic(kinchaku);
            run.AddRelic(waraji);
            run.AddCharm(charm);
            run.GainGold(37);
            run.player.LoseHp(9);
            run.ApplyEngraving(run.pouch.All[1], 3, engraving);
            run.AddDice(config.rewardDicePool[0]);

            var r = RoundTrip(config, run);

            Assert.AreEqual(run.Gold, r.Gold);
            Assert.AreEqual(run.Turn, r.Turn);
            Assert.AreEqual(run.player.hp, r.player.hp);
            Assert.AreEqual(run.player.maxHp, r.player.maxHp);
            Assert.AreEqual(run.pouch.Capacity, r.pouch.Capacity, "大きな巾着の容量は、もう一度は足さない");
            Assert.AreEqual(run.LayerIndex, r.LayerIndex);
            Assert.AreEqual(run.Current.id, r.Current.id);
            Assert.AreEqual(run.TotalSteps, r.TotalSteps);
            CollectionAssert.AreEqual(run.Relics, r.Relics);
            CollectionAssert.AreEqual(run.Charms, r.Charms);
            Assert.AreEqual(run.ChargesOf(waraji), r.ChargesOf(waraji));
            Assert.AreEqual(run.pouch.All.Count, r.pouch.All.Count);
            for (int i = 0; i < run.pouch.All.Count; i++)
            {
                var a = run.pouch.All[i];
                var b = r.pouch.All[i];
                Assert.AreSame(a.data, b.data);
                Assert.AreEqual(a.state, b.state);
                for (int f = 0; f < a.faces.Length; f++)
                {
                    Assert.AreEqual(a.faces[f].value, b.faces[f].value);
                    Assert.AreSame(a.faces[f].engraving, b.faces[f].engraving);
                }
            }
            Assert.AreEqual(run.board.tiles.Count, r.board.tiles.Count);
            for (int i = 0; i < run.board.tiles.Count; i++)
            {
                Assert.AreEqual(run.board.tiles[i].type, r.board.tiles[i].type);
                CollectionAssert.AreEqual(run.board.tiles[i].next.Select(n => n.id), r.board.tiles[i].next.Select(n => n.id));
            }
            Assert.AreEqual(run.board.Goal.id, r.board.Goal.id);
            Assert.AreEqual(run.TilesToGoal, r.TilesToGoal);
        }

        [Test]
        public void Restored_RunContinuesExactlyTheSame()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                var run = new RunState(config, seed);
                Walk(run, 0);
                Walk(run, 1);
                var r = RoundTrip(config, run);

                // 同じ操作をすれば、同じ出目・同じマス・同じ報酬・同じ敵・同じイベント
                Walk(run, 2);
                Walk(r, 2);
                Assert.AreEqual(run.Current.id, r.Current.id, $"seed {seed}");
                Assert.AreEqual(run.LastRolledValue, r.LastRolledValue);
                var t = run.board.tiles.First(x => x.type == TileType.Battle);
                Assert.AreSame(run.PickEnemy(t), r.PickEnemy(r.board.tiles.First(x => x.id == t.id)));
                Assert.AreEqual(run.PickEvent(), r.PickEvent());
                Assert.AreEqual(run.CreateBattleReward(RewardKind.Normal).gold, r.CreateBattleReward(RewardKind.Normal).gold);
            }
        }

        [Test]
        public void Restored_AfterLayerAdvance()
        {
            var run = new RunState(config, 9);
            run.AdvanceLayer();
            Walk(run, 1);
            var r = RoundTrip(config, run);
            Assert.AreEqual(1, r.LayerIndex);
            Assert.AreEqual(run.Current.id, r.Current.id);
            Walk(run, 0);
            Walk(r, 0);
            Assert.AreEqual(run.Current.id, r.Current.id);
        }

        [Test]
        public void CannotSaveDuringBattle()
        {
            var run = new RunState(config, 1);
            var battle = new BattleState(run.player, config.layers[0].boss, run.pouch, run.random.Battle, run.effects, run);
            Assert.IsNotNull(run.CurrentBattle);
            Assert.Throws<InvalidOperationException>(() => run.CreateSave());
        }

        [Test]
        public void UnknownData_Throws()
        {
            var run = new RunState(config, 1);
            var save = run.CreateSave();
            save.relics.Add("nothing");
            Assert.Throws<InvalidOperationException>(() => RunState.Restore(config, save));
        }

        [Test]
        public void SaveFile_WriteReadDelete()
        {
            string folder = Path.Combine(Path.GetTempPath(), "sainomichi_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var run = new RunState(config, 3);
                Assert.IsFalse(RunSaveFile.Exists(folder));
                var save = run.CreateSave();
                save.runId = "abc";
                RunSaveFile.Write(folder, save);
                Assert.IsTrue(RunSaveFile.Exists(folder));
                var loaded = RunSaveFile.Read(folder, out var error);
                Assert.IsNull(error);
                Assert.AreEqual("abc", loaded.runId);
                Assert.AreEqual(run.Gold, RunState.Restore(config, loaded).Gold);
                RunSaveFile.Delete(folder);
                Assert.IsFalse(RunSaveFile.Exists(folder));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
