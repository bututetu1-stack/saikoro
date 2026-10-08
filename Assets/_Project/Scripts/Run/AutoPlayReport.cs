using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Run
{
    /// <summary>自動プレイを何ランも回して集計し、CSV に書き出す。</summary>
    public static class AutoPlayReport
    {
        /// <summary>seed から count ラン回す。スターターは順番に替える（ないときは加えない）。</summary>
        public static List<AutoRunRecord> RunMany(GameConfig config, int firstSeed, int count, Action<int> progress = null)
        {
            var records = new List<AutoRunRecord>();
            var starters = config.starterChoices.Where(d => d != null).ToList();
            for (int i = 0; i < count; i++)
            {
                DiceData starter = starters.Count > 0 ? starters[i % starters.Count] : null;
                records.Add(new AutoPlayer(config, firstSeed + i, starter).Play());
                progress?.Invoke(i + 1);
            }
            return records;
        }

        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string P(double v) => (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        /// <summary>要約（クリア率・層ごとの数値・敵ごとの数値）を文字で。</summary>
        public static string Summary(IReadOnlyList<AutoRunRecord> runs)
        {
            var sb = new StringBuilder();
            int n = runs.Count;
            if (n == 0) return "ランがありません。";
            sb.AppendLine($"ラン数 {n}　クリア率 {P(runs.Count(r => r.cleared) / (double)n)}　平均ターン {F(runs.Average(r => r.turns))}　平均獲得ゴールド {F(runs.Average(r => r.goldEarned))}");
            for (int layer = 1; layer <= 3; layer++)
            {
                int reached = runs.Count(r => r.layerReached >= layer);
                if (reached == 0) continue;
                int diedHere = runs.Count(r => r.layerReached == layer && !r.cleared);
                var battles = runs.SelectMany(r => r.battles).Where(b => b.layer == layer).ToList();
                var normal = battles.Where(b => b.kind == RewardKind.Normal).ToList();
                var elite = battles.Where(b => b.kind == RewardKind.Elite).ToList();
                var boss = battles.Where(b => b.kind == RewardKind.Boss).ToList();
                var reachedRuns = runs.Where(r => r.layerReached >= layer).ToList();
                sb.AppendLine($"第{layer}層：到達 {reached}　ここで倒れた {P(diedHere / (double)reached)}　" +
                    $"戦闘 {F(battles.Count / (double)reached)} 回/ラン（通常 {F(normal.Count / (double)reached)}・強敵 {F(elite.Count / (double)reached)}）　" +
                    $"ゴールド {F(reachedRuns.Average(r => r.goldEarnedByLayer[layer - 1]))}　ターン {F(reachedRuns.Average(r => r.turnsByLayer[layer - 1]))}");
                if (normal.Count > 0) sb.AppendLine($"　通常戦：ラウンド {F(normal.Average(b => b.rounds))}　受けたダメージ {F(normal.Average(b => b.damageTaken))}　負け {P(normal.Count(b => b.outcome != BattleOutcome.Victory && b.outcome != BattleOutcome.Fled) / (double)normal.Count)}");
                if (elite.Count > 0) sb.AppendLine($"　強敵：ラウンド {F(elite.Average(b => b.rounds))}　受けたダメージ {F(elite.Average(b => b.damageTaken))}　負け {P(elite.Count(b => b.outcome != BattleOutcome.Victory) / (double)elite.Count)}");
                if (boss.Count > 0) sb.AppendLine($"　ボス：ラウンド {F(boss.Average(b => b.rounds))}　受けたダメージ {F(boss.Average(b => b.damageTaken))}　負け {P(boss.Count(b => b.outcome != BattleOutcome.Victory) / (double)boss.Count)}　挑む前の HP {F(boss.Average(b => b.hpBefore))}");
            }
            var causes = runs.Where(r => !r.cleared).GroupBy(r => r.deathCause ?? "?").OrderByDescending(g => g.Count()).Take(8);
            sb.AppendLine("倒された原因：" + string.Join("、", causes.Select(g => $"{g.Key} {g.Count()}")));
            return sb.ToString();
        }

        /// <summary>CSV を3つ書く：ランごと・戦闘ごと・敵ごとの集計。書いたフォルダのパスを返す。</summary>
        public static string WriteCsv(string folder, IReadOnlyList<AutoRunRecord> runs)
        {
            Directory.CreateDirectory(folder);
            var r = new StringBuilder();
            r.AppendLine("seed,starter,cleared,layer_reached,death_cause,turns,hp_end,max_hp_end,gold_earned,gold_end,relics,dice,battles,gold_l1,gold_l2,gold_l3,turns_l1,turns_l2,turns_l3");
            foreach (var x in runs)
            {
                r.AppendLine(string.Join(",", x.seed, x.starter, x.cleared ? 1 : 0, x.layerReached, x.deathCause ?? "", x.turns, x.hpEnd, x.maxHpEnd,
                    x.goldEarned, x.goldEnd, x.relics, x.dice, x.battles.Count,
                    x.goldEarnedByLayer[0], x.goldEarnedByLayer[1], x.goldEarnedByLayer[2], x.turnsByLayer[0], x.turnsByLayer[1], x.turnsByLayer[2]));
            }
            File.WriteAllText(Path.Combine(folder, "autoplay_runs.csv"), r.ToString(), new UTF8Encoding(true));

            var b = new StringBuilder();
            b.AppendLine("seed,layer,enemy,kind,rounds,hp_before,hp_after,damage_taken,outcome");
            foreach (var x in runs)
            {
                foreach (var y in x.battles)
                {
                    b.AppendLine(string.Join(",", x.seed, y.layer, y.enemy, y.kind, y.rounds, y.hpBefore, y.hpAfter, y.damageTaken, y.outcome));
                }
            }
            File.WriteAllText(Path.Combine(folder, "autoplay_battles.csv"), b.ToString(), new UTF8Encoding(true));

            var e = new StringBuilder();
            e.AppendLine("layer,enemy,kind,battles,avg_rounds,avg_damage_taken,max_damage_taken,loss_rate");
            foreach (var g in runs.SelectMany(x => x.battles).GroupBy(y => (y.layer, y.enemy, y.kind)).OrderBy(g => g.Key.layer).ThenBy(g => g.Key.kind))
            {
                var list = g.ToList();
                e.AppendLine(string.Join(",", g.Key.layer, g.Key.enemy, g.Key.kind, list.Count, F(list.Average(y => y.rounds)), F(list.Average(y => y.damageTaken)),
                    list.Max(y => y.damageTaken), F(list.Count(y => y.outcome == BattleOutcome.Defeat) / (double)list.Count)));
            }
            File.WriteAllText(Path.Combine(folder, "autoplay_enemies.csv"), e.ToString(), new UTF8Encoding(true));
            return folder;
        }
    }
}
