using System;

namespace SaiNoMichi.Battle
{
    /// <summary>戦闘中の敵1体。毎ラウンドの予告を決める。</summary>
    public class EnemyState : Combatant
    {
        public readonly EnemyData data;
        int patternIndex;

        public EnemyState(EnemyData data) : base(data.maxHp)
        {
            this.data = data;
        }

        public Intent CurrentIntent { get; private set; }

        /// <summary>round ラウンド目の予告を決める。乱数を使う行動（ランダム・賽振り）は rng から。</summary>
        public Intent PrepareIntent(int round, Random rng)
        {
            switch (data.behavior)
            {
                case EnemyBehavior.Random:
                    CurrentIntent = data.pattern[rng.Next(data.pattern.Count)];
                    break;
                case EnemyBehavior.Banjin:
                    CurrentIntent = BanjinIntent(round, rng);
                    break;
                default:
                    CurrentIntent = data.pattern[patternIndex % data.pattern.Count];
                    break;
            }
            return CurrentIntent;
        }

        /// <summary>
        /// 双六の番人（仕様書 第7章）：resetEvery ラウンドごとに「振り出しに戻れ」。それ以外は予告の時点で賽を振り、
        /// 偶数ならその値×2の攻撃、奇数ならその値×2の防御。出目は予告で見せる（行動を読めるように。開発者の方針）。
        /// </summary>
        Intent BanjinIntent(int round, Random rng)
        {
            if (data.resetEvery > 0 && round % data.resetEvery == 0)
            {
                return new Intent(IntentType.ResetDice, 0);
            }
            int sides = Math.Max(2, data.diceSides);
            int roll = rng.Next(1, sides + 1);
            if (roll % 2 == 0)
            {
                return new Intent(IntentType.DiceRoll, roll * 2) { minValue = roll * 2, maxValue = roll * 2 };
            }
            return new Intent(IntentType.Block, roll * 2);
        }

        public void AdvancePattern()
        {
            patternIndex++;
        }
    }
}
