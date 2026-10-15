using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>協力「賽の旅団」の通信の部分：仲間の様子（PartyState）・行き先の投票・AI の仲間。</summary>
    public class PartySessionTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        static PartyCommand Cmd(int slot, PartyCommandType type, int a = 0, int b = -1) => new PartyCommand { slot = slot, type = type, a = a, b = b };

        [Test]
        public void State_ReadyGate_StartsClosed_OpensWhenEveryoneIsReady()
        {
            var s = new PartyState(3, 0);
            Assert.IsFalse(s.AllReady(0), "はじめの「準備よし」が来るまでは、そろっていない");
            s.Apply(Cmd(0, PartyCommandType.Ready, 0));
            s.Apply(Cmd(1, PartyCommandType.Ready, 0));
            Assert.IsFalse(s.AllReady(0));
            s.Apply(Cmd(2, PartyCommandType.Down));
            Assert.IsTrue(s.AllReady(0), "抜けた人は待たない");
            Assert.IsFalse(s.AllReady(1));
        }

        [Test]
        public void State_DecidesByMajority_ThenLeader_AndKeepsTheRolls()
        {
            var s = new PartyState(3, 1);
            int decided = 0;
            s.Decided += () => decided++;
            s.Apply(Cmd(0, PartyCommandType.Rolled, 3));
            s.Apply(Cmd(1, PartyCommandType.Rolled, 5));
            s.Apply(Cmd(2, PartyCommandType.Rolled, -1));
            Assert.AreEqual(3, s.RolledCount);
            s.Apply(Cmd(0, PartyCommandType.Vote, 10));
            s.Apply(Cmd(1, PartyCommandType.Vote, 12));
            Assert.AreEqual(0, decided, "全員の票がそろうまで決まらない");
            s.Apply(Cmd(2, PartyCommandType.Vote, 10));
            Assert.AreEqual(1, decided);
            Assert.AreEqual(10, s.TakeDecided(), "票の多いマス");
            CollectionAssert.AreEqual(new[] { 3, 5, -1 }, s.lastRolled, "決まったときの出目は残る");
            Assert.AreEqual(0, s.RolledCount, "次のターンのために消える");

            // 同じ票数なら代表（1番）の票
            s.Apply(Cmd(0, PartyCommandType.Rolled, 2));
            s.Apply(Cmd(1, PartyCommandType.Rolled, 2));
            s.Apply(Cmd(2, PartyCommandType.Down));
            s.Apply(Cmd(0, PartyCommandType.Vote, 7));
            s.Apply(Cmd(1, PartyCommandType.Vote, 8));
            Assert.AreEqual(8, s.TakeDecided());
        }

        [Test]
        public void VoteOptions_ListWhoCanReachEachTile_AndValueToReachUsesTheFirst()
        {
            var config = LoadConfig();
            var run = new RunState(config, 31);
            var from = run.Current;
            var values = new[] { 2, 4, -1, 2 };
            var options = PartyRun.VoteOptions(from, values, new[] { -1, -1, -1, -1 });
            var reach2 = ReachCalculator.Compute(from, new[] { 2 }).Keys.ToList();
            var reach4 = ReachCalculator.Compute(from, new[] { 4 }).Keys.ToList();
            CollectionAssert.AreEquivalent(reach2.Union(reach4), options.Keys);
            foreach (var t in reach2) CollectionAssert.IsSubsetOf(new[] { 0, 3 }, options[t], "2 で届くマスには 0 番と 3 番");
            var only4 = reach4.Except(reach2).FirstOrDefault();
            if (only4 != null) Assert.AreEqual(4, PartyRun.ValueToReach(from, values, new[] { -1, -1, -1, -1 }, only4.id));
            Assert.AreEqual(2, PartyRun.ValueToReach(from, values, new[] { -1, -1, -1, -1 }, reach2[0].id));
            Assert.AreEqual(-1, PartyRun.ValueToReach(from, values, new[] { -1, -1, -1, -1 }, -99), "ないマス");
        }

        [Test]
        public void Bots_RollVoteWalk_AndStayOnTheDecidedTile()
        {
            var config = LoadConfig();
            var session = new BotPartySession(config, 77, "あなた", 2);
            var seen = new PartyState(3, 0);
            session.CommandReceived += seen.Apply;
            var you = new RunState(config, 77);
            float time = 0f;
            void Run(float seconds) { for (float t = 0; t < seconds; t += 0.1f) session.Tick(time += 0.1f); }

            session.Send(PartyCommandType.Ready, 0);
            Run(3f);
            Assert.AreEqual(2, seen.RolledCount, "全員そろったら、AI の仲間が振る");

            var die = you.pouch.Available.First(RunState.CanMoveWith);
            var move = you.BeginMove(die);
            session.Send(PartyCommandType.Rolled, move.remaining, -1, die.DisplayName);
            Run(3f);
            Assert.AreEqual(0, seen.VotedCount, "AI の仲間は、あなたの票を見てから票を入れる");

            var options = PartyRun.VoteOptions(you.Current, Enumerable.Range(0, 3).Select(i => seen.rolled[i] ?? -1).ToList(), seen.forcedTile);
            var target = options.Keys.First();
            session.Send(PartyCommandType.Vote, target.id);
            Run(5f);
            Assert.AreEqual(0, seen.VotedCount, "全員の票がそろって決まった（票は次のターンのために消える）");
            int decided = seen.TakeDecided();
            Assert.IsTrue(options.Keys.Any(t => t.id == decided), "みんなの出目で止まれるマスに決まる");
            Assert.IsTrue(seen.ReadyTurn(1) >= 1 || seen.down[1], "AI の仲間は、マスの出来事を済ませて準備よし");
            Assert.IsTrue(seen.ReadyTurn(2) >= 1 || seen.down[2]);
        }
    }
}
