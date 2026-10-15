using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>協力「賽の旅団」のラン（同じ盤面をそろって進む・投票）。</summary>
    public class PartyRunTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        [Test]
        public void Tally_Majority_ThenLeader_ThenLowestSlot()
        {
            Assert.AreEqual(1, PartyRun.Tally(new[] { 1, 1, 0 }, 0), "多いほう");
            Assert.AreEqual(2, PartyRun.Tally(new[] { 0, 1, 2, 2 }, 3), "多いほう（代表の票とは関係なく）");
            Assert.AreEqual(1, PartyRun.Tally(new[] { 0, 1 }, 1), "同じ票数なら代表が選んだもの");
            Assert.AreEqual(0, PartyRun.Tally(new[] { 2, 0, -1 }, -1), "代表がいなければ番号の若いほう");
        }

        [Test]
        public void Members_ShareTheSameBoard()
        {
            var config = LoadConfig();
            var party = new PartyRun(config, 99, new DiceData[] { null, null, null });
            Assert.IsTrue(party.InSync);
            var ids = party.members.Select(m => string.Join(",", m.board.tiles.Select(t => $"{t.id}:{t.type}"))).Distinct().Count();
            Assert.AreEqual(1, ids, "全員同じ盤面");
        }

        [Test]
        public void Move_EveryoneRolls_AllMoveByTheChosenValue_AndUseTheirOwnDie()
        {
            var config = LoadConfig();
            var party = new PartyRun(config, 99, new DiceData[] { null, null });
            var dice = party.members.Select(m => m.pouch.Available.First(RunState.CanMoveWith)).ToList();
            var moves = party.RollAll(dice);
            Assert.IsTrue(dice.All(d => d.state != DiceState.Available || d.data.keepAvailable), "全員のダイスが使用済み");

            party.Adopt(moves, 1);
            int value = moves[1].value;
            Assert.IsTrue(moves.All(m => m.value == value), "全員が選ばれた出目に");
            while (!party.AllDone(moves))
            {
                var leader = party.members[0];
                var next = leader.Current.next.Count > 0 ? leader.Current.next[leader.Current.next.Count - 1] : null;
                party.StepAll(moves, next);
            }
            party.FinishAll(moves);
            Assert.IsTrue(party.InSync, "同じマスに止まる");
            Assert.IsTrue(party.members.All(m => m.Turn == 1), "全員1ターン進む");
        }
    }
}
