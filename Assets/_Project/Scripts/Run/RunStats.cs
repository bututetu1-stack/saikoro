using System;
using System.Collections.Generic;
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
    }
}
