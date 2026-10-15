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
    /// </summary>
    public class GhostRaceSession : IRaceSession
    {
        class Ghost
        {
            public string name;
            public readonly List<RaceProgress> byTurn = new List<RaceProgress>();
            public RaceProgress current;
        }

        readonly List<Ghost> ghosts = new List<Ghost>();

        public int Seed { get; }
        public string LocalName { get; }
        public IReadOnlyList<RaceProgress> Rivals => ghosts.Select(g => g.current).ToList();
        public event Action Changed;

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
            player.TurnTaken += run =>
            {
                var p = RaceProgress.From(run, name);
                // 同じターンのうちに何度か呼ばれたら（層を移ったときなど）、最後の様子で上書きする
                if (ghost.byTurn.Count > 0 && ghost.byTurn[ghost.byTurn.Count - 1].turn == p.turn) ghost.byTurn[ghost.byTurn.Count - 1] = p;
                else ghost.byTurn.Add(p);
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
            // あなたのターン数のときのゴーストの様子を見せる（それより先へは進まない）
            foreach (var g in ghosts)
            {
                var at = g.byTurn.LastOrDefault(p => p.turn <= local.turn);
                g.current = at.name != null ? at : g.byTurn[0];
            }
            Changed?.Invoke();
        }

        public void Leave() { }

        /// <summary>テスト用：ゴーストが最後にどうなったか（踏破・倒れた）と、そのターン数。</summary>
        public IReadOnlyList<RaceProgress> FinalResults => ghosts.Select(g => g.byTurn[g.byTurn.Count - 1]).ToList();
    }
}
