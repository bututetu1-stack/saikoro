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
        /// 双六の番人（仕様書 第7章）：resetEvery ラウンドごとに「振り出しに戻れ」。それ以外は賽を振り、
        /// 偶数ならその値×2の攻撃（値は行動まで隠し、予告は偶数の範囲）、奇数ならその値×2の防御。
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
                int maxEven = sides % 2 == 0 ? sides : sides - 1;
                return new Intent(IntentType.DiceRoll, roll * 2) { minValue = 2 * 2, maxValue = maxEven * 2 };
            }
            // TODO(仕様): 奇数（防御）は「防御の予告はすぐ反映」のルールに合わせ、予告の時点で値を見せて防御値にする
            return new Intent(IntentType.Block, roll * 2);
        }

        public void AdvancePattern()
        {
            patternIndex++;
        }
    }
}
