using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    public class PlayLogTests
    {
        TestDice factory;
        GameConfig config;
        string path;

        [SetUp]
        public void SetUp()
        {
            factory = new TestDice();
            config = ScriptableObject.CreateInstance<GameConfig>();
            config.startingDice = new List<DiceData>
            {
                factory.Data("three", 3, 3, 3, 3, 3, 3),
                factory.Data("six", 6, 6, 6, 6, 6, 6),
            };
            var enemy = factory.Enemy(5, new Intent(IntentType.Attack, 4));
            config.battleEnemies = new List<EnemyData> { enemy };
            config.boss = enemy;
            path = Path.Combine(Path.GetTempPath(), $"sainomichi_playlog_{Guid.NewGuid():N}.csv");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(path)) File.Delete(path);
            UnityEngine.Object.DestroyImmediate(config);
            factory.DestroyAll();
        }

        static Dictionary<string, string> Parse(string line)
        {
            var values = line.Split(',');
            return PlayLog.Columns.Select((c, i) => (c, v: values[i])).ToDictionary(x => x.c, x => x.v);
        }

        [Test]
        public void RecordMove_WritesAvailableDiceChosenDiceAndRoll()
        {
            var run = new RunState(config, 7);
            var log = new PlayLog("r1", 7);

            var move = run.Move(run.pouch.All[0]);
            log.RecordMove(run.Turn, move);
            var row = Parse(log.TakePendingLines().Single());

            Assert.AreEqual("r1", row["run_id"]);
            Assert.AreEqual("7", row["seed"]);
            Assert.AreEqual("move", row["event"]);
            Assert.AreEqual("1", row["turn"]);
            Assert.AreEqual("2", row["available_count"]);
            Assert.AreEqual("three|six", row["available_dice"]);
            Assert.AreEqual("three", row["chosen_dice"]);
            Assert.AreEqual("3", row["roll"]);
            Assert.AreEqual("0", row["from"]);
            Assert.AreEqual("3", row["to"]);
        }

        [Test]
        public void RecordBattle_WritesRoundsDiceUsedAndDamage()
        {
            var run = new RunState(config, 7);
            var log = new PlayLog("r1", 7);
            var battle = new BattleState(run.player, config.battleEnemies[0], run.pouch, new System.Random(0));

            battle.Resolve();                       // パス：4ダメージ
            battle.Roll(run.pouch.All[1]);          // 6で倒す
            battle.Resolve();
            log.RecordBattle(run.Turn, battle);
            var row = Parse(log.TakePendingLines().Single());

            Assert.AreEqual("battle", row["event"]);
            Assert.AreEqual("Victory", row["battle_result"]);
            Assert.AreEqual("2", row["rounds"]);
            Assert.AreEqual("six", row["dice_used"]);
            Assert.AreEqual("4", row["damage_taken"]);
            Assert.AreEqual("4|0", row["damage_by_round"]);
            Assert.AreEqual("36", row["hp"]);
        }

        [Test]
        public void AppendTo_WritesHeaderOnceWithBom()
        {
            var log = new PlayLog("r1", 7);
            log.RecordResult(5, true, 20, 40);
            log.AppendTo(path);
            log.RecordResult(6, false, 0, 40);
            log.AppendTo(path);

            var bytes = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "BOM 付き UTF-8");

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(PlayLog.Header, lines[0]);
            Assert.AreEqual("clear", Parse(lines[1])["result"]);
            Assert.AreEqual("gameover", Parse(lines[2])["result"]);
        }

        [Test]
        public void AppendTo_OldHeader_StartsNewFileAndKeepsOld()
        {
            File.WriteAllText(path, "run_id,seed,event\nold,1,move\n", new UTF8Encoding(true));
            var log = new PlayLog("r1", 7);
            log.RecordResult(5, true, 20, 40);
            log.AppendTo(path);

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(PlayLog.Header, lines[0], "新しい見出しで書き始める");
            Assert.AreEqual(2, lines.Length);
            var dir = Path.GetDirectoryName(path);
            var olds = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(path) + "_old_*");
            Assert.AreEqual(1, olds.Length, "古いファイルは別名で残る");
            foreach (var f in olds) File.Delete(f);
        }

        [Test]
        public void TakePendingLines_EmptiesBuffer()
        {
            var log = new PlayLog("r1", 7);
            log.RecordResult(1, true, 1, 40);

            Assert.AreEqual(1, log.TakePendingLines().Count);
            Assert.AreEqual(0, log.TakePendingLines().Count);
        }

        [Test]
        public void Escape_QuotesCommasAndQuotes()
        {
            Assert.AreEqual("abc", PlayLog.Escape("abc"));
            Assert.AreEqual("\"a,b\"", PlayLog.Escape("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", PlayLog.Escape("say \"hi\""));
        }
    }
}
