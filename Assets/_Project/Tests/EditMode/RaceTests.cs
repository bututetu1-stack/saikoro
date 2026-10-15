using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>対戦「賽レース」の進み具合と、練習相手（ゴースト）。</summary>
    public class RaceTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        [Test]
        public void RankKey_ClearedFirst_FewerTurnsFirst_ThenFurtherAhead()
        {
            var clearedFast = new RaceProgress { state = RaceState.Cleared, turn = 30 };
            var clearedSlow = new RaceProgress { state = RaceState.Cleared, turn = 45 };
            var layer3 = new RaceProgress { state = RaceState.Running, layer = 2, tilesToGoal = 40 };
            var layer2Near = new RaceProgress { state = RaceState.Running, layer = 1, tilesToGoal = 3 };
            var layer2Far = new RaceProgress { state = RaceState.Dead, layer = 1, tilesToGoal = 30 };

            var order = new[] { layer2Far, clearedSlow, layer3, clearedFast, layer2Near }.OrderByDescending(p => p.RankKey).ToList();
            Assert.AreEqual(new[] { clearedFast, clearedSlow, layer3, layer2Near, layer2Far }, order);
        }

        [Test]
        public void FromRun_ReadsPositionAndHp()
        {
            var run = new RunState(LoadConfig(), 11);
            var p = RaceProgress.From(run, "あなた");
            Assert.AreEqual("あなた", p.name);
            Assert.AreEqual(0, p.layer);
            Assert.AreEqual(run.Current.id, p.tileId);
            Assert.AreEqual(run.TilesToGoal, p.tilesToGoal);
            Assert.AreEqual(run.player.hp, p.hp);
            Assert.AreEqual(RaceState.Running, p.state);
        }

        [Test]
        public void Ghosts_FollowYourTurnCount_AndFinish()
        {
            var session = new GhostRaceSession(LoadConfig(), 21, "あなた", 2);
            Assert.AreEqual(2, session.Rivals.Count);
            Assert.IsTrue(session.Rivals.All(r => r.turn == 0), "始めはスタート");

            int changed = 0;
            session.Changed += () => changed++;
            session.Report(new RaceProgress { name = "あなた", turn = 5 });
            Assert.AreEqual(1, changed);
            Assert.IsTrue(session.Rivals.All(r => r.turn <= 5), "あなたのターン数より先へは進まない");

            foreach (var final in session.FinalResults)
            {
                Assert.AreNotEqual(RaceState.Running, final.state, $"{final.name}：最後は踏破か倒れたか");
            }

            // ずっと先のターンを知らせたら、ゴーストは最後の様子になる
            session.Report(new RaceProgress { name = "あなた", turn = 10_000 });
            CollectionAssert.AreEqual(session.FinalResults.Select(r => r.state), session.Rivals.Select(r => r.state));
        }

        [Test]
        public void HpHistory_OnePerTurn_FillsGapsAndOverwrites()
        {
            var s = new RunStats();
            s.RecordHp(0, 40, 40, 0);
            s.RecordHp(2, 30, 40, 0);   // 1ターン目が抜けたら、前の値で埋める
            s.RecordHp(2, 28, 40, 1);   // 同じターンは上書き
            CollectionAssert.AreEqual(new[] { 40, 40, 28 }, s.hpByTurn);
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, s.layerByTurn);
        }

        [Test]
        public void HpHistory_RecordedEachMove_AndKeptInSave()
        {
            var config = LoadConfig();
            var run = new AutoPlayer(config, 5).run;
            var player = new AutoPlayer(config, 5);
            player.Play();
            var stats = player.run.stats;
            Assert.AreEqual(player.run.Turn, stats.hpByTurn.Count, "ターンごとに1つ");
            Assert.AreEqual(stats.hpByTurn.Count, stats.maxHpByTurn.Count);

            var save = run.CreateSave();
            Assert.IsNotNull(save.hpByTurn);
        }

        [Test]
        public void Sabotage_SentToLeader_DelaysThatGhost()
        {
            var session = new GhostRaceSession(LoadConfig(), 21, "あなた", 2);
            session.Report(new RaceProgress { name = "あなた", turn = 12 });
            var leaderBefore = session.Rivals.OrderByDescending(r => r.RankKey).First();
            string target = session.SendSabotage(SabotageKind.Slow);
            Assert.AreEqual(leaderBefore.name, target, "先頭の相手に送る");
            var after = session.Rivals.First(r => r.name == target);
            Assert.LessOrEqual(after.turn, 11, "1ターン遅れる");
        }

        [Test]
        public void Ghosts_SendCards_WhenTheyBeatElitesOrBosses()
        {
            var session = new GhostRaceSession(LoadConfig(), 21, "あなた", 2);
            int received = 0;
            session.SabotageReceived += (from, kind) => received++;
            session.Report(new RaceProgress { name = "あなた", turn = 10_000 });
            // ゴーストは少なくとも1体はボスを倒している（第1層のボス）はずなので、札が届く
            Assert.Greater(received, 0);
        }

        [Test]
        public void Sabotage_Apply_SlowLowersNextMove_CurseAddsDie()
        {
            var run = new RunState(LoadConfig(), 3);
            Sabotage.Apply(run, SabotageKind.Slow);
            Assert.AreEqual(-Sabotage.SlowAmount, run.PendingMoveBonus);
            int before = run.pouch.All.Count;
            Sabotage.Apply(run, SabotageKind.Curse);
            Assert.AreEqual(before + 1, run.pouch.All.Count, "呪いのダイスが1個入る");
        }

        [Test]
        public void Ghosts_SameSeed_SameResult()
        {
            var config = LoadConfig();
            var a = new GhostRaceSession(config, 33, "あなた", 1).FinalResults[0];
            var b = new GhostRaceSession(config, 33, "あなた", 1).FinalResults[0];
            Assert.AreEqual(a.turn, b.turn);
            Assert.AreEqual(a.state, b.state);
            Assert.AreEqual(a.layer, b.layer);
        }

        [Test]
        public void BoardOfLayer_SameAsRunBoard_EvenAfterTraps()
        {
            var config = LoadConfig();
            const int seed = 4242;
            var run = new RunState(config, seed);
            for (int layer = 0; layer < run.LayerCount; layer++)
            {
                // 罠を踏んでも（乱数を使っても）、同じシードの盤面は変わらない（対戦で、相手の層の盤面を自分の端末で作れる）
                for (int i = 0; i < 5; i++) run.TriggerTrap();
                AssertSameBoard(run.board, RunState.BoardOfLayer(config, seed, layer), $"第{layer + 1}層");
                if (!run.IsFinalLayer) run.AdvanceLayer();
            }
        }

        static void AssertSameBoard(SaiNoMichi.Board.BoardData a, SaiNoMichi.Board.BoardData b, string label)
        {
            Assert.AreEqual(a.tiles.Count, b.tiles.Count, label);
            for (int i = 0; i < a.tiles.Count; i++)
            {
                Assert.AreEqual(a.tiles[i].id, b.tiles[i].id, label);
                Assert.AreEqual(a.tiles[i].type, b.tiles[i].type, label);
                Assert.AreEqual(a.tiles[i].position, b.tiles[i].position, label);
                CollectionAssert.AreEqual(a.tiles[i].next.Select(t => t.id), b.tiles[i].next.Select(t => t.id), label);
            }
        }

        [Test]
        public void EncodeDice_RoundTrip_WithStates()
        {
            var run = new RunState(LoadConfig(), 5);
            var dice = run.pouch.All;
            dice[0].state = SaiNoMichi.Dice.DiceState.Used;
            if (dice.Count > 1) dice[1].state = SaiNoMichi.Dice.DiceState.Sealed;
            var decoded = RaceProgress.DecodeDice(RaceProgress.EncodeDice(run.pouch));
            CollectionAssert.AreEqual(dice.Select(d => d.DisplayName), decoded.Select(d => d.name));
            CollectionAssert.AreEqual(dice.Select(d => d.state), decoded.Select(d => d.state));
            Assert.AreEqual(0, RaceProgress.DecodeDice("").Count);
        }

        [Test]
        public void Clip_FitsInBytes()
        {
            string longText = new string('賽', 400);
            string clipped = RaceProgress.Clip(longText, 100);
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(clipped), 100);
            Assert.IsTrue(clipped.EndsWith("…"));
            Assert.AreEqual("短い", RaceProgress.Clip("短い", 100));
        }
    }
}
