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

    /// <summary>効果が働く場面。</summary>
    public enum SceneCondition
    {
        Any,
        Battle,   // 戦闘中（ctx.battle がある）
        Map,      // 移動中・マップ（ctx.battle がない）
    }

    /// <summary>効果を発動させる条件。何も指定しなければ常に発動する。</summary>
    [Serializable]
    public struct EffectCondition
    {
        public ParityCondition parity;
        public Assignment assignment;   // None なら割り振り先を問わない
        public int minValue;            // 0 なら下限なし
        public int maxValue;            // 0 なら上限なし
        public SceneCondition scene;
        public bool battleGoldOnly;     // 戦闘の報酬で得るゴールドだけ（銭袋）
        public bool firstMoveOfLayer;   // その層の最初の移動だけ（早馬）

        public bool Matches(EffectContext ctx)
        {
            if (parity == ParityCondition.Even && ctx.value % 2 != 0) return false;
            if (parity == ParityCondition.Odd && ctx.value % 2 == 0) return false;
            if (assignment != Assignment.None && ctx.assignment != assignment) return false;
            if (minValue > 0 && ctx.value < minValue) return false;
            if (maxValue > 0 && ctx.value > maxValue) return false;
            if (scene == SceneCondition.Battle && ctx.battle == null) return false;
            if (scene == SceneCondition.Map && ctx.battle != null) return false;
            if (battleGoldOnly && !ctx.fromBattle) return false;
            if (firstMoveOfLayer && (ctx.run == null || ctx.run.MovesThisLayer != 1)) return false;
            return true;
        }
    }
}
