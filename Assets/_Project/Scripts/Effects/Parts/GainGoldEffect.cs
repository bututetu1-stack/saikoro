using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>ゴールドを得る。例：刻印「小判」（3G）。OnGoldGain で使うと無限に増えるので使わないこと。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Gain Gold", fileName = "Fx_GainGold")]
    public class GainGoldEffect : EffectSO
    {
        public int gold;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.trigger == Trigger.OnGoldGain) return;
            if (ctx.run != null && condition.Matches(ctx)) ctx.run.GainGold(gold);
        }
    }
}
