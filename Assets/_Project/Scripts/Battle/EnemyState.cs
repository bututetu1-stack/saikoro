using System;

namespace SaiNoMichi.Battle
{
    /// <summary>戦闘中の敵1体。毎ラウンドの予告を決める。</summary>
    public class EnemyState : Combatant
    {
        public readonly EnemyData data;
        int patternIndex;

        /// <param name="hpPercent">HP の倍率（%）。前の層の敵が出たときなどに 150。</param>
        public EnemyState(EnemyData data, int hpPercent = 100) : base(Math.Max(1, data.maxHp * hpPercent / 100))
        {
            this.data = data;
        }

        public Intent CurrentIntent { get; private set; }

        /// <summary>round ラウンド目の予告を決める。ランダムな行動は rng から。</summary>
        public Intent PrepareIntent(int round, Random rng)
        {
            switch (data.behavior)
            {
                case EnemyBehavior.Random:
                    CurrentIntent = data.pattern[rng.Next(data.pattern.Count)];
                    break;
                default:
                    CurrentIntent = data.pattern[patternIndex % data.pattern.Count];
                    break;
            }
            return CurrentIntent;
        }

        public void AdvancePattern()
        {
            patternIndex++;
        }
    }
}
