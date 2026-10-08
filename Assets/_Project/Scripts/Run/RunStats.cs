using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;

namespace SaiNoMichi.Run
{
    /// <summary>1ランの成績（クリア画面と記録用）。</summary>
    public class RunStats
    {
        public int goldEarned;
        public int battlesWon;
        public int elitesWon;
        public int bossesWon;
        public readonly Dictionary<TileType, int> tilesStopped = new Dictionary<TileType, int>();
        public readonly List<string> diceGained = new List<string>();
        public readonly List<string> diceRemoved = new List<string>();
        public readonly List<string> engravings = new List<string>();
        public string deathCause;  // 力尽きた原因（敵の名前・マスの名前）。結果のまとめ用

        /// <summary>同じ名前をまとめて「刃×3・堅」のように並べる。なければ「なし」。</summary>
        public static string Grouped(IEnumerable<string> names)
        {
            var groups = names.GroupBy(n => n).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key).ToList();
            return groups.Count > 0 ? string.Join("・", groups) : "なし";
        }

        /// <summary>刻印の記録（「刃→普通の賽」）から、刻印の名前だけを取り出す。</summary>
        public IEnumerable<string> EngravingNames => engravings.Select(e => e.Split('→')[0]);

        public void CountStop(TileType type)
        {
            tilesStopped.TryGetValue(type, out int n);
            tilesStopped[type] = n + 1;
        }

        public void CountVictory(EnemyKind kind)
        {
            battlesWon++;
            if (kind == EnemyKind.Elite) elitesWon++;
            if (kind == EnemyKind.Boss) bossesWon++;
        }
    }

    public partial class RunState
    {
        public readonly RunStats stats = new RunStats();

        /// <summary>ダイス・レリック・刻印を手に入れた／手放したとき（記録用）。kind は dice / relic / engraving / remove / discard。</summary>
        public event Action<string, string> Acquired;

        void NotifyAcquired(string kind, string name) => Acquired?.Invoke(kind, name);

        /// <summary>
        /// 結果のまとめ（試遊の感想と一緒に送ってもらう文）。シードがあれば同じ盤面を再現できる。
        /// version はゲームの版（Application.version）。
        /// </summary>
        public string ShareSummary(bool cleared, string version)
        {
            var s = stats;
            var lines = new List<string>
            {
                $"【賽ノ道 v{version}】" + (cleared ? "踏破！" : $"第{LayerIndex + 1}層で力尽きた" + (string.IsNullOrEmpty(s.deathCause) ? "" : $"（{s.deathCause}）")),
                $"シード {random.Seed}　ターン {Turn}　HP {player.hp}/{player.maxHp}　勝った戦闘 {s.battlesWon}（エリート {s.elitesWon}・ボス {s.bossesWon}）",
                "ダイス：" + RunStats.Grouped(pouch.All.Select(d => d.DisplayName)),
                "レリック：" + RunStats.Grouped(relics.Select(r => r.displayName)),
                "お守り：" + RunStats.Grouped(charms.Select(c => c.displayName)),
                "刻印：" + RunStats.Grouped(s.EngravingNames),
                "削除：" + RunStats.Grouped(s.diceRemoved),
            };
            return string.Join("\n", lines);
        }
    }
}
