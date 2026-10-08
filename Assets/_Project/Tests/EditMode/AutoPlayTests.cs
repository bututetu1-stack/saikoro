using System.IO;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>自動プレイ（本番のデータで回す）。</summary>
    public class AutoPlayTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        [Test]
        public void ManyRuns_FinishWithoutErrors()
        {
            var config = LoadConfig();
            var runs = AutoPlayReport.RunMany(config, 1, 30);
            Assert.AreEqual(30, runs.Count);
            foreach (var r in runs)
            {
                Assert.IsTrue(r.cleared || !string.IsNullOrEmpty(r.deathCause), $"seed {r.seed}：クリアか、倒れた原因がある");
                Assert.That(r.layerReached, Is.InRange(1, 3));
                Assert.Greater(r.battles.Count, 0, $"seed {r.seed}：1回は戦っている");
            }
        }

        [Test]
        public void SameSeed_SameResult()
        {
            var config = LoadConfig();
            var a = new AutoPlayer(config, 7, AutoPlayReport.ChooseStarter(config, 7)).Play();
            var b = new AutoPlayer(config, 7, AutoPlayReport.ChooseStarter(config, 7)).Play();
            Assert.AreEqual(a.cleared, b.cleared);
            Assert.AreEqual(a.turns, b.turns);
            Assert.AreEqual(a.goldEarned, b.goldEarned);
            Assert.AreEqual(a.battles.Count, b.battles.Count);
            Assert.AreEqual(a.deathCause, b.deathCause);
        }

        [Test]
        public void Report_WritesCsvAndSummary()
        {
            var config = LoadConfig();
            var runs = AutoPlayReport.RunMany(config, 100, 5);
            string folder = Path.Combine(Path.GetTempPath(), "sainomichi_autoplay_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                AutoPlayReport.WriteCsv(folder, runs);
                Assert.IsTrue(File.Exists(Path.Combine(folder, "autoplay_runs.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(folder, "autoplay_battles.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(folder, "autoplay_enemies.csv")));
                Assert.AreEqual(6, File.ReadAllLines(Path.Combine(folder, "autoplay_runs.csv")).Length, "見出し＋5ラン");
                StringAssert.Contains("クリア率", AutoPlayReport.Summary(runs));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
