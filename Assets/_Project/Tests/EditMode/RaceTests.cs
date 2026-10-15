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
        public void Ghosts_SameSeed_SameResult()
        {
            var config = LoadConfig();
            var a = new GhostRaceSession(config, 33, "あなた", 1).FinalResults[0];
            var b = new GhostRaceSession(config, 33, "あなた", 1).FinalResults[0];
            Assert.AreEqual(a.turn, b.turn);
            Assert.AreEqual(a.state, b.state);
            Assert.AreEqual(a.layer, b.layer);
        }
    }
}
