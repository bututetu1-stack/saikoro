using System;
using SaiNoMichi.Battle;

namespace SaiNoMichi.Core
{
    /// <summary>
    /// 敵の HP・攻撃の倍率（%）。敵の種類（通常・強敵・ボス）と層ごと。バランス調整用のつまみ（フェーズ2 手順13）。
    /// 敵ごとの数値（仕様書 第7章）はそのままにして、ここで全体を上げ下げする。配列は第1層・第2層・第3層の順。
    /// </summary>
    [Serializable]
    public class EnemyBalance
    {
        public int[] normalHpPercent = { 100, 100, 100 };
        public int[] normalAttackPercent = { 100, 100, 100 };
        public int[] eliteHpPercent = { 100, 100, 100 };
        public int[] eliteAttackPercent = { 100, 100, 100 };
        public int[] bossHpPercent = { 100, 100, 100 };
        public int[] bossAttackPercent = { 100, 100, 100 };

        public int HpPercent(EnemyKind kind, int layerIndex) =>
            Pick(kind == EnemyKind.Boss ? bossHpPercent : kind == EnemyKind.Elite ? eliteHpPercent : normalHpPercent, layerIndex);

        public int AttackPercent(EnemyKind kind, int layerIndex) =>
            Pick(kind == EnemyKind.Boss ? bossAttackPercent : kind == EnemyKind.Elite ? eliteAttackPercent : normalAttackPercent, layerIndex);

        static int Pick(int[] values, int layerIndex)
        {
            if (values == null || values.Length == 0) return 100;
            return values[Math.Max(0, Math.Min(layerIndex, values.Length - 1))];
        }
    }
}
