using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>出目（ctx.value）に足す。例：盾賽「防御に回すと+2」、刻印「刃」、レリック「丁の札」。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Add Value", fileName = "Fx_AddValue")]
    public class AddValueEffect : EffectSO
    {
        public int add;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (condition.Matches(ctx)) ctx.value += add;
        }
    }
}
