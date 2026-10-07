using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>振ったダイスを使用済みにしない。例：レリック「小石」（1が出たダイス）。OnRoll で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Keep Available", fileName = "Fx_KeepAvailable")]
    public class KeepAvailableEffect : EffectSO
    {
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (condition.Matches(ctx)) ctx.keepAvailable = true;
        }
    }
}
