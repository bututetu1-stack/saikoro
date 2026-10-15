using System.Collections.Generic;
using System.Linq;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// 旅の得点（Slay the Spire の得点画面風）。結果の画面に出し、ラン履歴にも残す。
    /// 進んだ層・倒した敵・踏破の早さなどを足し、段位が高いほど割り増しする。
    /// </summary>
    public static class RunScore
    {
        // TODO(仕様): 点数の配分はすべて仮
        public const int PerLayer = 100;
        public const int PerBattle = 10;
        public const int PerElite = 25;
        public const int PerBoss = 50;
        public const int ClearBonus = 500;
        public const int FastTurns = 80;       // これより少ないターンで踏破したら、1ターンにつき FastPerTurn 点
        public const int FastPerTurn = 5;
        public const int PerHpLeft = 2;
        public const int GoldDivisor = 10;     // 稼いだゴールド 10 G で1点
        public const int PerEngraving = 5;
        public const int PerRelic = 10;
        public const int AscensionPercent = 10; // 段位1つにつき +10%

        public struct Line
        {
            public string name;
            public int points;
            public Line(string name, int points)
            {
                this.name = name;
                this.points = points;
            }
        }

        /// <summary>得点の内訳（0点の行は出さない）。最後の行は段位の割り増し（段位0なら無し）。</summary>
        public static List<Line> Breakdown(RunState run, bool cleared)
        {
            var s = run.stats;
            int layer = run.LayerIndex + 1;
            var lines = new List<Line>
            {
                new Line($"第{layer}層まで進んだ", layer * PerLayer),
                new Line($"勝った戦闘 {s.battlesWon}", s.battlesWon * PerBattle),
                new Line($"強敵 {s.elitesWon}", s.elitesWon * PerElite),
                new Line($"ボス {s.bossesWon}", s.bossesWon * PerBoss),
            };
            if (cleared)
            {
                lines.Add(new Line("踏破", ClearBonus));
                lines.Add(new Line($"早足（{run.Turn} ターン）", System.Math.Max(0, FastTurns - run.Turn) * FastPerTurn));
                lines.Add(new Line($"残りHP {run.player.hp}", run.player.hp * PerHpLeft));
            }
            lines.Add(new Line($"稼いだゴールド {s.goldEarned} G", s.goldEarned / GoldDivisor));
            lines.Add(new Line($"刻印 {s.engravings.Count}", s.engravings.Count * PerEngraving));
            lines.Add(new Line($"レリック {run.Relics.Count}", run.Relics.Count * PerRelic));
            lines = lines.Where(l => l.points > 0).ToList();
            if (run.Ascension > 0)
            {
                int sub = lines.Sum(l => l.points);
                lines.Add(new Line($"段位{run.Ascension}（+{run.Ascension * AscensionPercent}%）", sub * run.Ascension * AscensionPercent / 100));
            }
            return lines;
        }

        public static int Total(RunState run, bool cleared) => Breakdown(run, cleared).Sum(l => l.points);
    }
}
