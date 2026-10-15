using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Run;
using SaiNoMichi.Tour;
using UnityEditor;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>国めぐり（桃鉄風の巨大マップのモード）。</summary>
    public class TourTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        static TourState NewTour(int seed, int players = 4, TourGoal goal = TourGoal.Assets, int rounds = 20)
        {
            var setups = Enumerable.Range(0, players).Select(i => new TourPlayerSetup { name = $"旅人{i}", isAI = true }).ToList();
            return new TourState(LoadConfig(), null, seed, goal, rounds, setups);
        }

        [Test]
        public void Map_IsConnected_SameSeedSameMap()
        {
            var s = TourSettings.OrDefault(null);
            var a = TourMapGenerator.Generate(new System.Random(5), s);
            var b = TourMapGenerator.Generate(new System.Random(5), s);
            Assert.AreEqual(s.townColumns * s.townRows, a.towns.Count);
            Assert.AreEqual(a.nodes.Count, b.nodes.Count, "同じシードなら同じ盤面");
            CollectionAssert.AreEqual(a.nodes.Select(n => n.kind), b.nodes.Select(n => n.kind));
            Assert.AreEqual(a.nodes.Count, a.DistancesFrom(a.nodes[0]).Count, "どのマスへも行ける");
            Assert.IsTrue(a.towns.All(t => t.properties.Count >= s.propertiesPerTown), "町には名物がある");
            Assert.AreEqual(a.towns.Count, a.towns.Select(t => t.name).Distinct().Count(), "町の名前は重ならない");
            Assert.Greater(a.nodes.Count, 150, "巨大マップ");
        }

        [Test]
        public void Reach_ExactSteps_NoImmediateBacktrack_StopsAtDestination()
        {
            // 一本道：0-1-2-3-4-5
            var nodes = Enumerable.Range(0, 6).Select(i => new TourNode { id = i, kind = TourTileKind.Blue }).ToList();
            for (int i = 0; i + 1 < nodes.Count; i++) { nodes[i].neighbors.Add(nodes[i + 1]); nodes[i + 1].neighbors.Add(nodes[i]); }
            var map = new TourMap(nodes, new List<TourTown>());
            var reach = map.Reach(nodes[2], nodes[1], 2);
            CollectionAssert.AreEquivalent(new[] { nodes[4] }, reach.Keys, "来た道（1）へは戻らない");
            CollectionAssert.AreEqual(new[] { nodes[3], nodes[4] }, reach[nodes[4]], "道筋");
            var both = map.Reach(nodes[2], null, 2);
            CollectionAssert.AreEquivalent(new[] { nodes[0], nodes[4] }, both.Keys, "来た道がなければ、どちらへも");
            var stop = map.Reach(nodes[2], nodes[1], 3, nodes[3]);
            CollectionAssert.AreEquivalent(new[] { nodes[3] }, stop.Keys, "目的地を通るときはそこで止まる");
            var deadEnd = map.Reach(nodes[5], nodes[4], 1);
            CollectionAssert.AreEquivalent(new[] { nodes[4] }, deadEnd.Keys, "行き止まりなら戻れる");
        }

        [Test]
        public void AIOnlyGame_PlaysToTheEnd()
        {
            foreach (var goal in new[] { TourGoal.Assets, TourGoal.Arrivals, TourGoal.FinalBoss })
            {
                var s = NewTour(1234 + (int)goal, 4, goal, 12);
                int guard = 0;
                while (!s.IsOver && guard++ < 1000)
                {
                    TourAI.PlayTurn(s);
                    s.EndTurn();
                }
                Assert.IsTrue(s.IsOver, $"{goal}：最後まで進む");
                if (goal == TourGoal.FinalBoss)
                {
                    foreach (var p in s.players) p.bossDamage = TourAI.FightBoss(p, s.FinalBoss, 100 * s.settings.finalBossHpMultiplier, s.settings.finalBossRounds);
                    Assert.IsTrue(s.players.Any(p => p.bossDamage > 0), "大ボスにダメージを与える");
                }
                var standings = s.Standings();
                Assert.AreEqual(4, standings.Count);
                Assert.IsTrue(s.players.All(p => p.Gold >= 0));
            }
        }

        [Test]
        public void BuyProperty_Settlement_PaysIncome_MonopolyDoubles()
        {
            var s = NewTour(77, 2, TourGoal.Assets, 20);
            var p = s.Current;
            var town = s.map.towns.First(t => t.node == p.node);
            p.run.GainGold(5000);
            foreach (var prop in town.properties) Assert.IsTrue(s.Buy(prop), "止まっている町の名物は買える");
            Assert.AreEqual(p.index, town.Monopolist, "全部買うと独占");
            int income = town.properties.Sum(x => x.Income) * 2;
            int before = p.Gold;
            // 決算のターンまで進める（移動はしない）
            for (int r = 0; r < s.settings.settlementEvery; r++)
            {
                s.EndTurn();
                s.EndTurn();
            }
            Assert.AreEqual(before + income, p.Gold, "決算で独占の町は2倍");
            Assert.AreEqual(p.Gold + town.properties.Sum(x => x.price), s.Assets(p));
        }

        [Test]
        public void Duel_WinnerTakesGold_DiceUsed()
        {
            var s = NewTour(91, 2);
            var a = s.players[0];
            var b = s.players[1];
            int total = a.Gold + b.Gold;
            var r = s.Duel(b);
            Assert.AreEqual(total, a.Gold + b.Gold, "ゴールドは移るだけ");
            if (r.winner != null) Assert.Greater(r.stolen, 0);
            int used = a.run.pouch.All.Count(d => d.state == Dice.DiceState.Used);
            Assert.IsTrue(used == r.attackerRolls.Count || used == 0, "振ったダイスは使用済み（全部使えばリフレッシュ）");
        }

        [Test]
        public void Arrival_GivesBonus_YakugamiGoesToFarthest_NewDestination()
        {
            var s = NewTour(31, 3);
            var p = s.Current;
            var dest = s.Destination;
            // ほかの2人のうち1人を遠くへ
            var far = s.map.towns.OrderByDescending(t => s.map.Distance(t.node, dest.node)).First();
            s.players[2].node = far.node;
            int before = p.Gold;
            var landing = s.WarpTo(dest);
            Assert.IsTrue(landing.arrived);
            Assert.AreEqual(1, p.arrivals);
            Assert.Greater(p.Gold, before);
            Assert.AreEqual(2, s.YakugamiHolder, "目的地からいちばん遠い人に厄神");
            Assert.AreNotEqual(dest, s.Destination, "次の目的地");
        }

        [Test]
        public void Cards_Thief_Stall_Scapegoat()
        {
            var s = NewTour(55, 2);
            var p = s.Current;
            var o = s.players[1];
            o.run.GainGold(1000);
            p.cards.Add(TourCardKind.Thief);
            int og = o.Gold;
            Assert.IsNotNull(s.UseCard(0, 1));
            Assert.AreEqual(og - og * TourCards.ThiefPercent / 100, o.Gold);
            p.cards.Add(TourCardKind.Stall);
            s.UseCard(0, 1);
            Assert.AreEqual(TourCards.StallAmount, o.movePenalty);
            p.cards.Add(TourCardKind.Scapegoat);
            Assert.IsNull(s.UseCard(0, 1), "厄神がついていないと身代わり札は使えない");
            Assert.AreEqual(1, p.cards.Count, "使えなかった札は残る");
        }

        [Test]
        public void Awards_GoToTheBest_TiesGetNothing()
        {
            var s = NewTour(12, 3, TourGoal.Arrivals);
            s.players[0].duelWins = 2;
            s.players[1].duelWins = 1;
            s.players[0].battleWins = 1;
            s.players[1].battleWins = 1; // 同じ数なので武勇賞はなし
            s.players[2].cardsUsed = 3;
            var awards = s.GiveAwards();
            CollectionAssert.AreEquivalent(new[] { "勝負師賞", "策士賞" }, awards.Select(a => a.title));
            Assert.AreEqual(1, s.players[0].arrivals, "目的地で勝負なら一番乗り +1");
            Assert.AreEqual(1, s.players[2].arrivals);
            Assert.AreEqual(0, s.players[1].arrivals);
        }

        [Test]
        public void TourRelics_BuyOnce_HayakagoAddsOne()
        {
            var config = LoadConfig();
            if (config.tourRelicPool.Count == 0) Assert.Ignore("国めぐりの宝物がデータにありません。");
            var s = NewTour(41, 2);
            var p = s.Current;
            p.run.GainGold(1000);
            var hayakago = config.tourRelicPool.First(r => r.id == TourState.HayakagoRelicId);
            Assert.IsTrue(s.BuyRelic(hayakago));
            Assert.IsFalse(s.BuyRelic(hayakago), "同じ宝物は2つ持てない");
            Assert.IsTrue(TourState.HasRelic(p, TourState.HayakagoRelicId));
            // 早駕籠：出目 +1（出目が6だけのダイスなら7歩）
            var six = ScriptableObject.CreateInstance<Dice.DiceData>();
            six.id = "six"; six.displayName = "六"; six.faceValues = new[] { 6, 6, 6, 6, 6, 6 };
            var die = new Dice.DiceInstance(six);
            p.run.pouch.ForceAdd(die);
            Assert.AreEqual(7, s.RollMove(new[] { die }, out _));
            Object.DestroyImmediate(six);
        }

        [Test]
        public void Cards_Swap_Gust_Smith()
        {
            var s = NewTour(56, 2);
            var p = s.Current;
            var o = s.players[1];
            o.node = s.map.towns[3].node;
            var mine = p.node;
            var theirs = o.node;
            p.cards.Add(TourCardKind.Swap);
            Assert.IsNotNull(s.UseCard(0, 1));
            Assert.AreEqual(theirs, p.node, "入れ替え札：居場所が入れ替わる");
            Assert.AreEqual(mine, o.node);

            p.cards.Add(TourCardKind.Gust);
            var before = o.node;
            Assert.IsNotNull(s.UseCard(0, 1));
            Assert.AreNotEqual(before, o.node, "天狗の団扇：ほかの町へ飛ぶ");
            Assert.IsTrue(s.map.towns.Any(t => t.node == o.node), "飛んだ先は町");
            Assert.AreNotEqual(s.Destination.node, o.node, "目的地には飛ばさない");

            var target = TourState.SmithTarget(p);
            Assert.IsNotNull(target);
            int sum = target.faces.Sum(f => f.value);
            p.cards.Add(TourCardKind.Smith);
            Assert.IsTrue(s.CanUseCard(TourCardKind.Smith));
            Assert.IsNotNull(s.UseCard(0));
            Assert.AreEqual(sum + 1, target.faces.Sum(f => f.value), "鍛冶札：いちばん小さい面が +1");
            Assert.AreEqual(0, p.cards.Count);
        }

        [Test]
        public void GreatYakugami_AfterStayingWithSamePerson_ResetsOnNextArrival()
        {
            var s = NewTour(31, 3);
            var dest = s.Destination;
            var far = s.map.towns.OrderByDescending(t => s.map.Distance(t.node, dest.node)).First();
            s.players[2].node = far.node;
            s.WarpTo(dest);
            Assert.AreEqual(2, s.YakugamiHolder);
            s.EndTurn();
            int holderTurns = 0;
            bool became = false;
            for (int i = 0; i < 3 * (s.settings.greatYakugamiAfter + 3) && !s.GreatYakugami; i++)
            {
                s.BeginTurn();
                if (s.TurnIndex == 2)
                {
                    holderTurns++;
                    became |= s.JustBecameGreat;
                }
                s.EndTurn();
            }
            Assert.IsTrue(s.GreatYakugami, "同じ人に続けてとりつくと大厄神に化ける");
            Assert.IsTrue(became);
            Assert.AreEqual(s.settings.greatYakugamiAfter + 1, holderTurns);

            // 次の一番乗りで、ほかの人につき直すと厄神に戻る
            while (s.TurnIndex != 0) s.EndTurn();
            var dest2 = s.Destination;
            s.players[1].node = s.map.towns.OrderByDescending(t => s.map.Distance(t.node, dest2.node)).First().node;
            s.players[2].node = dest2.node;
            s.WarpTo(dest2);
            Assert.AreEqual(1, s.YakugamiHolder);
            Assert.IsFalse(s.GreatYakugami);
        }
    }
}
