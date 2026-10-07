using System;
using System.Collections.Generic;

namespace SaiNoMichi.Battle
{
    public enum Assignment
    {
        None,
        Attack,
        Block,
    }

    /// <summary>戦闘の計算式（仕様書 第6章）。フェーズ0では弱体・脆弱はまだない。</summary>
    public static class BattleResolver
    {
        /// <summary>攻撃値 = 攻撃に置いた出目の合計 + 筋力（合計に1回だけ）。攻撃に置いたダイスがなければ0。</summary>
        public static int PlayerAttack(IEnumerable<int> attackValues, int strength)
        {
            int sum = 0;
            int count = 0;
            foreach (int v in attackValues)
            {
                sum += v;
                count++;
            }
            return count == 0 ? 0 : Math.Max(0, sum + strength);
        }

        public static int PlayerBlock(IEnumerable<int> blockValues)
        {
            int sum = 0;
            foreach (int v in blockValues) sum += v;
            return sum;
        }

        /// <summary>敵の攻撃値 = 予告の値 + 筋力。</summary>
        public static int EnemyAttack(Intent intent, int strength)
        {
            return intent.type == IntentType.Attack ? Math.Max(0, intent.value + strength) : 0;
        }

        /// <summary>防御値で軽減したあとのダメージ = max(0, 攻撃値 − 防御値)。</summary>
        public static int DamageAfterBlock(int attack, int block)
        {
            return Math.Max(0, attack - block);
        }
    }
}
