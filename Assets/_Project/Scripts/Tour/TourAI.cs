using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Tour
{
    /// <summary>国めぐりの AI の旅人の考え方。目的地に近づき、青・黄のマスを好み、赤は避ける。</summary>
    public static class TourAI
    {
        /// <summary>移動に振るダイス（急行札なら2個）。止まれそうなマスの点数の期待値がいちばん高いもの。</summary>
        public static List<DiceInstance> ChooseDice(TourState s)
        {
            var p = s.Current;
            var movable = p.run.pouch.Available.Where(RunState.CanMoveWith).ToList();
            if (movable.Count == 0) return new List<DiceInstance>();
            if (s.DiceThisTurn >= 2) return movable.OrderByDescending(d => d.faces.Average(f => f.value)).Take(2).ToList();
            var dist = s.Destination != null ? s.map.DistancesFrom(s.Destination.node) : null;
            DiceInstance best = null;
            float bestScore = float.MinValue;
            foreach (var die in movable)
            {
                float expected = 0f;
                foreach (var (value, prob) in DiceRoller.Distribution(die, p.lastRolled))
                {
                    int steps = Math.Max(1, value + s.TailwindThisTurn - p.movePenalty);
                    var options = s.MoveOptions(steps);
                    expected += prob * (options.Count > 0 ? options.Keys.Max(n => Score(s, p, n, dist)) : 0f);
                }
                expected -= 0.3f * (float)die.faces.Average(f => f.value); // 強いダイスは対決・戦闘に残す
                if (expected > bestScore)
                {
                    bestScore = expected;
                    best = die;
                }
            }
            return new List<DiceInstance> { best };
        }

        /// <summary>止まるマス。</summary>
        public static TourNode ChooseEnd(TourState s, Dictionary<TourNode, List<TourNode>> options)
        {
            var dist = s.Destination != null ? s.map.DistancesFrom(s.Destination.node) : null;
            return options.Keys.OrderByDescending(n => Score(s, s.Current, n, dist)).ThenBy(n => n.id).First();
        }

        /// <summary>止まりたいマスの点数。</summary>
        public static float Score(TourState s, TourPlayer p, TourNode node, Dictionary<TourNode, int> distToDestination)
        {
            if (s.Destination != null && node == s.Destination.node) return 1000f;
            float hp = p.run.player.maxHp > 0 ? (float)p.run.player.hp / p.run.player.maxHp : 0f;
            float score;
            switch (node.kind)
            {
                case TourTileKind.Blue: score = 6f; break;
                case TourTileKind.Red: score = -8f; break;
                case TourTileKind.Yellow: score = p.cards.Count < s.settings.maxCards ? 7f : 1f; break;
                case TourTileKind.Battle: score = hp > 0.5f ? 2f : -6f; break;
                case TourTileKind.Shrine: score = hp < 0.7f ? 6f : 2f; break;
                case TourTileKind.Event: score = 3f; break;
                case TourTileKind.Town:
                {
                    var town = s.map.towns[node.town];
                    score = 3f;
                    if (town.properties.Any(x => x.owner < 0 && x.price <= p.Gold)) score += 5f;
                    if (town.diceShop) score += 1.5f;
                    break;
                }
                default: score = 0f; break;
            }
            // 目的地に近づくほどよい
            if (distToDestination != null && distToDestination.TryGetValue(node, out int d) && distToDestination.TryGetValue(p.node, out int now))
            {
                score += 1.5f * (now - d);
            }
            // 厄神：ついていれば、ほかの人のいるマスへ（移せる）。ついていなければ、厄神のいる人を避ける
            bool others = s.players.Any(o => o != p && o.node == node);
            if (others && s.YakugamiHolder == p.index) score += 10f;
            if (s.YakugamiHolder >= 0 && s.YakugamiHolder != p.index && s.players[s.YakugamiHolder].node == node) score -= 12f;
            return score;
        }

        /// <summary>買う名物（安いものから。手元に少し残す）。</summary>
        public static List<TourProperty> ChooseBuys(TourState s, TourTown town)
        {
            // TODO(仕様): AI が手元に残すゴールドは仮（80）
            const int reserve = 80;
            var list = new List<TourProperty>();
            int gold = s.Current.Gold;
            foreach (var prop in town.properties.Where(x => x.owner < 0).OrderBy(x => x.price))
            {
                if (gold - prop.price < reserve) break;
                list.Add(prop);
                gold -= prop.price;
            }
            return list;
        }

        /// <summary>サイコロ屋で買うもの（1つまで。なければ null）。</summary>
        public static DiceData ChooseDiceBuy(TourState s, List<(DiceData dice, int price)> offer, out int price)
        {
            price = 0;
            var p = s.Current;
            if (!p.run.CanAddDice) return null;
            var pick = offer.Where(o => o.dice.rarity != Core.Rarity.Curse && p.Gold - o.price >= 150)
                .OrderByDescending(o => o.dice.rarity).ThenByDescending(o => o.price).FirstOrDefault();
            if (pick.dice == null) return null;
            price = pick.price;
            return pick.dice;
        }

        /// <summary>宝物を買うか（手元に少し残せるなら）。</summary>
        public static bool WantsRelic(TourState s, Effects.RelicData relic) => relic != null && s.Current.Gold - TourState.RelicPrice >= 150;

        /// <summary>手番のはじめに使う札（番号・相手・飛脚の町）。使わなければ card = −1。</summary>
        public static (int card, int target, TourTown town) ChooseCard(TourState s)
        {
            var p = s.Current;
            var leader = s.Standings().FirstOrDefault(o => o != p);
            int far = s.Destination != null ? s.map.Distance(p.node, s.Destination.node) : 0;
            for (int i = 0; i < p.cards.Count; i++)
            {
                var card = p.cards[i];
                switch (card)
                {
                    case TourCardKind.Fortune:
                        return (i, -1, null);
                    case TourCardKind.Scapegoat:
                        if (s.YakugamiHolder == p.index && leader != null) return (i, leader.index, null);
                        break;
                    case TourCardKind.Thief:
                    {
                        var rich = s.players.Where(o => o != p).OrderByDescending(o => o.Gold).First();
                        if (rich.Gold >= 200) return (i, rich.index, null);
                        break;
                    }
                    case TourCardKind.Stall:
                    case TourCardKind.Curse:
                        if (leader != null && s.Destination != null && s.map.Distance(leader.node, s.Destination.node) <= 8) return (i, leader.index, null);
                        break;
                    case TourCardKind.Smith:
                        if (s.CanUseCard(card)) return (i, -1, null);
                        break;
                    case TourCardKind.Gust:
                        // 目的地の近くにいる先頭の人を吹き飛ばす
                        if (leader != null && s.Destination != null && s.map.Distance(leader.node, s.Destination.node) <= 8) return (i, leader.index, null);
                        break;
                    case TourCardKind.Swap:
                    {
                        // 自分より目的地にずっと近い人と入れ替わる
                        if (s.Destination == null) break;
                        var closer = s.players.Where(o => o != p).OrderBy(o => s.map.Distance(o.node, s.Destination.node)).First();
                        if (s.map.Distance(closer.node, s.Destination.node) + 8 <= far) return (i, closer.index, null);
                        break;
                    }
                    case TourCardKind.Express:
                        if (far > 10) return (i, -1, null);
                        break;
                    case TourCardKind.Tailwind:
                        if (far > 6) return (i, -1, null);
                        break;
                    case TourCardKind.Courier:
                    {
                        // 目的地にいちばん近い町へ
                        if (s.Destination == null || far < 12) break;
                        var dist = s.map.DistancesFrom(s.Destination.node);
                        var town = s.map.towns.Where(t => t != s.Destination).OrderBy(t => dist.TryGetValue(t.node, out int d) ? d : int.MaxValue).First();
                        return (i, -1, town);
                    }
                }
            }
            return (-1, -1, null);
        }

        /// <summary>同じマスの相手に対決を挑むか（勝てそうで、相手がゴールドを持っているとき）。挑む相手か null。</summary>
        public static TourPlayer ChooseDuel(TourState s, List<TourPlayer> others)
        {
            var p = s.Current;
            float mine = Strength(p);
            return others.Where(o => o.Gold >= 100 && mine > Strength(o) + 1f).OrderByDescending(o => o.Gold).FirstOrDefault();
        }

        static float Strength(TourPlayer p) =>
            p.run.pouch.Available.Select(d => (float)d.faces.Average(f => f.value)).OrderByDescending(x => x).Take(3).Sum() + p.run.player.strength;

        /// <summary>
        /// AI の手番をまるごと進める（画面なし。テストや、画面を見せないとき）。起きたことを返す。手番の終わり（EndTurn）は呼ばない。
        /// </summary>
        public static List<string> PlayTurn(TourState s)
        {
            var log = new List<string>();
            void Add(string m) { if (!string.IsNullOrEmpty(m)) log.Add(m); }
            Add(s.BeginTurn());
            var (card, target, town) = ChooseCard(s);
            TourLanding landing;
            if (card >= 0 && town != null)
            {
                landing = s.UseCourier(card, town);
            }
            else
            {
                if (card >= 0) Add(s.UseCard(card, target));
                var dice = ChooseDice(s);
                int steps = dice.Count > 0 ? s.RollMove(dice, out _) : s.SkipRoll();
                var options = s.MoveOptions(steps);
                var end = ChooseEnd(s, options);
                landing = s.Move(options[end]);
            }
            Add(landing.Message);
            AfterLanding(s, landing, log);
            return log;
        }

        /// <summary>止まったあと：戦闘・名物・サイコロ屋・対決。</summary>
        public static void AfterLanding(TourState s, TourLanding landing, List<string> log)
        {
            if (landing.battle)
            {
                var outcome = Fight(s.Current, landing.enemy);
                log.Add(s.FinishBattle(outcome, out _));
            }
            if (landing.town != null)
            {
                foreach (var prop in ChooseBuys(s, landing.town))
                {
                    if (s.Buy(prop)) log.Add($"{s.Current.name} が名物「{prop.name}」を買った。");
                }
                if (landing.diceOffer != null)
                {
                    var buy = ChooseDiceBuy(s, landing.diceOffer, out int price);
                    if (buy != null && s.BuyDice(buy, price)) log.Add($"{s.Current.name} が {buy.displayName} を買った。");
                }
                if (landing.relicOffer != null && WantsRelic(s, landing.relicOffer) && s.BuyRelic(landing.relicOffer)) log.Add($"{s.Current.name} が宝物「{landing.relicOffer.displayName}」を手に入れた。");
            }
            var foe = ChooseDuel(s, landing.others);
            if (foe != null)
            {
                var duel = s.Duel(foe);
                log.Add(duel.winner == null ? "対決は引き分け。" : $"対決！ {duel.winner.name} の勝ち（{duel.stolen} G）。");
            }
        }

        /// <summary>魔物と自動で戦う。</summary>
        public static BattleOutcome Fight(TourPlayer p, EnemyData enemy) => new AutoPlayer(p.run).FightEnemy(enemy);

        /// <summary>大ボスと決まったラウンドだけ自動で戦い、与えたダメージを返す。</summary>
        public static int FightBoss(TourPlayer p, EnemyData boss, int hpPercent, int rounds) => new AutoPlayer(p.run).FightRounds(boss, hpPercent, rounds);
    }
}
