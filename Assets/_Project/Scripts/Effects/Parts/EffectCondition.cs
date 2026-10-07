using System;
using SaiNoMichi.Battle;

namespace SaiNoMichi.Effects
{
    public enum ParityCondition
    {
        Any,
        Even,
        Odd,
    }

    /// <summary>効果を発動させる条件。何も指定しなければ常に発動する。</summary>
    [Serializable]
    public struct EffectCondition
    {
        public ParityCondition parity;
        public Assignment assignment;   // None なら割り振り先を問わない
        public int minValue;            // 0 なら下限なし
        public int maxValue;            // 0 なら上限なし

        public bool Matches(EffectContext ctx)
        {
            if (parity == ParityCondition.Even && ctx.value % 2 != 0) return false;
            if (parity == ParityCondition.Odd && ctx.value % 2 == 0) return false;
            if (assignment != Assignment.None && ctx.assignment != assignment) return false;
            if (minValue > 0 && ctx.value < minValue) return false;
            if (maxValue > 0 && ctx.value > maxValue) return false;
            return true;
        }
    }
}
