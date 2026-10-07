using UnityEngine;

namespace SaiNoMichi.Effects
{
    public enum ScaleTarget
    {
        Value,   // 出目・攻撃値など
        Amount,  // 得るゴールドなど
    }

    /// <summary>値を percent% にする（端数は切り捨て）。例：レリック「銭袋」（ゴールド125%）、「早馬」（出目200%）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Scale", fileName = "Fx_Scale")]
    public class ScaleEffect : EffectSO
    {
        public ScaleTarget target;
        public int percent = 100;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (!condition.Matches(ctx)) return;
            if (target == ScaleTarget.Value) ctx.value = ctx.value * percent / 100;
            else ctx.amount = ctx.amount * percent / 100;
        }
    }
}
