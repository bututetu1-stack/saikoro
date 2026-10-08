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

    /// <summary>戦闘の計算式（仕様書 第6章）。</summary>
    public static class BattleResolver
    {
        public const int WeakPercent = 75;

        /// <summary>
        /// 攻撃値 = (攻撃に置いた出目の合計 + 筋力（合計に1回だけ）) × 弱体補正（切り捨て）。攻撃に置いたダイスがなければ0。
        /// </summary>
        public static int PlayerAttack(IEnumerable<int> attackValues, int strength, int weak = 0)
        {
            int sum = 0;
            int count = 0;
            foreach (int v in attackValues)
            {
                sum += v;
                count++;
            }
            return count == 0 ? 0 : ApplyWeak(Math.Max(0, sum + strength), weak);
        }

        public static int PlayerBlock(IEnumerable<int> blockValues)
        {
            int sum = 0;
            foreach (int v in blockValues) sum += v;
            return sum;
        }

        /// <summary>
        /// 敵の攻撃値（合計）= 予告の値 × 回数 + 筋力、に弱体補正。攻撃でない予告は0。
        /// TODO(仕様): 多段攻撃の筋力は、プレイヤーの「合計に1回だけ」に合わせて合計に1回だけ足す
        /// </summary>
        public static int EnemyAttack(Intent intent, int strength, int weak = 0)
        {
            if (!intent.IsAttack) return 0;
            int total = intent.value * intent.Hits + strength;
            return ApplyWeak(Math.Max(0, total), weak);
        }

        /// <summary>防御値で軽減したあとのダメージ = max(0, 攻撃値 − 防御値)。</summary>
        public static int DamageAfterBlock(int attack, int block)
        {
            return Math.Max(0, attack - block);
        }

        public static int ApplyWeak(int attack, int weak) => weak > 0 ? attack * WeakPercent / 100 : attack;

        public const int VulnerablePercent = 150;

        /// <summary>弱体：受けるダメージ×1.5（切り捨て）。防御で減らす前にかける。</summary>
        public static int ApplyVulnerable(int attack, int vulnerable) => vulnerable > 0 ? attack * VulnerablePercent / 100 : attack;

        public const int FrailPercent = 75;

        /// <summary>脆弱：作れる防御値×0.75（切り捨て）。</summary>
        public static int ApplyFrail(int block, int frail) => frail > 0 ? block * FrailPercent / 100 : block;
    }
}
