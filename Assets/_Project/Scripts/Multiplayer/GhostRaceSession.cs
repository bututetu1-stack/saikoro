using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 対戦の練習相手（AI のゴースト）。同じシードで自動プレイを最後まで走らせて、ターンごとの進み具合を覚えておき、
    /// あなたのターン数に合わせて見せる（あなたが 10 ターン目なら、ゴーストも 10 ターン目の場所にいる）。
    /// 通信がいらないので、レースの画面をいつでも試せる。
    /// 妨害札：ゴーストは強敵・ボスを倒したターンに、あなたへ札を送ってくる。あなたが送った札を受けたゴーストは、1ターン遅れる。
    /// </summary>
    public class GhostRaceSession : IRaceSession
    {
        class Ghost
        {
            public string name;
            public readonly List<RaceProgress> byTurn = new List<RaceProgress>();
            public readonly List<int> cardTurns = new List<int>();   // 強敵・ボスを倒したターン（札を送ってくる）
            public int cardsSent;
            public int delay;                                         // 受けた妨害札のぶん、遅れるターン数
            public RaceProgress current;
        }

        // TODO(仕様): 妨害札を受けたゴーストが遅れるターン数は仮（1枚1ターン）
        const int DelayPerCard = 1;

        readonly List<Ghost> ghosts = new List<Ghost>();
        int lastLocalTurn;

        public int Seed { get; }
        public string LocalName { get; }
        public IReadOnlyList<RaceProgress> Rivals => ghosts.Select(g => g.current).ToList();
        public event Action Changed;
        public event Action<string, SabotageKind> SabotageReceived;

        /// <param name="ghostCount">練習相手の数（1〜3）。</param>
        public GhostRaceSession(GameConfig config, int seed, string localName, int ghostCount = 2)
        {
            Seed = seed;
            LocalName = localName;
            string[] names = { "AI・壱", "AI・弐", "AI・参" };
            var starters = RunState.StarterOptions(config, seed);
            for (int i = 0; i < Math.Max(1, Math.Min(3, ghostCount)); i++)
            {
                // ゴーストごとに違うスターターを持たせる（同じ盤面でも進み方が変わる）
                DiceData starter = starters.Count > 0 ? starters[i % starters.Count] : null;
                ghosts.Add(Simulate(config, seed, starter, names[i]));
            }
        }

        static Ghost Simulate(GameConfig config, int seed, DiceData starter, string name)
        {
            var ghost = new Ghost { name = name };
            var player = new AutoPlayer(config, seed, starter);
            ghost.byTurn.Add(RaceProgress.From(player.run, name));
            int bigWins = 0;
            player.TurnTaken += run =>
            {
                var p = RaceProgress.From(run, name);
                // 同じターンのうちに何度か呼ばれたら（層を移ったときなど）、最後の様子で上書きする
                if (ghost.byTurn.Count > 0 && ghost.byTurn[ghost.byTurn.Count - 1].turn == p.turn) ghost.byTurn[ghost.byTurn.Count - 1] = p;
                else ghost.byTurn.Add(p);
                int wins = run.stats.elitesWon + run.stats.bossesWon;
                for (; bigWins < wins; bigWins++) ghost.cardTurns.Add(run.Turn);
            };
            var record = player.Play();
            // 最後の様子に、踏破したかどうかを入れる
            var last = RaceProgress.From(player.run, name);
            last.state = record.cleared ? RaceState.Cleared : RaceState.Dead;
            if (ghost.byTurn[ghost.byTurn.Count - 1].turn == last.turn) ghost.byTurn[ghost.byTurn.Count - 1] = last;
            else ghost.byTurn.Add(last);
            ghost.current = ghost.byTurn[0];
            return ghost;
        }

        public void Report(RaceProgress local)
        {
            lastLocalTurn = local.turn;
            foreach (var g in ghosts)
            {
                // あなたのターン数のときのゴーストの様子を見せる（それより先へは進まない。妨害札を受けたぶん遅れる）
                int turn = local.turn - g.delay;
                var at = g.byTurn.LastOrDefault(p => p.turn <= turn);
                g.current = at.name != null ? at : g.byTurn[0];

                // 強敵・ボスを倒したターンに来たら、あなたへ妨害札を送ってくる（種類は交互）
                while (g.cardsSent < g.cardTurns.Count && g.cardTurns[g.cardsSent] <= turn && g.current.state != RaceState.Dead)
                {
                    var kind = Sabotage.All[g.cardsSent % Sabotage.All.Length];
                    g.cardsSent++;
                    SabotageReceived?.Invoke(g.name, kind);
                }
            }
            Changed?.Invoke();
        }

        public string SendSabotage(SabotageKind kind)
        {
            var target = ghosts.Where(g => g.current.state == RaceState.Running).OrderByDescending(g => g.current.RankKey).FirstOrDefault();
            if (target == null) return null;
            target.delay += DelayPerCard;
            // 遅れた分を、すぐ見せる
            int turn = lastLocalTurn - target.delay;
            var at = target.byTurn.LastOrDefault(p => p.turn <= turn);
            target.current = at.name != null ? at : target.byTurn[0];
            Changed?.Invoke();
            return target.name;
        }

        public void Leave() { }

        /// <summary>ゴーストが最後にどうなったか（踏破・倒れた）と、そのターン数（受けた妨害札のぶん遅れる）。</summary>
        public IReadOnlyList<RaceProgress> FinalResults => ghosts.Select(g =>
        {
            var last = g.byTurn[g.byTurn.Count - 1];
            last.turn += g.delay;
            return last;
        }).ToList();
    }
}
