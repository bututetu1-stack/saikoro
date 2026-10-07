using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>出目 × percent% のゴールドを得る。例：黄金賽（移動で使うと出目と同じゴールド）。OnMoveRolled で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Gain Gold By Value", fileName = "Fx_GainGoldByValue")]
    public class GainGoldByValueEffect : EffectSO
    {
        public int percent = 100;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.trigger == Trigger.OnGoldGain || ctx.run == null) return;
            ctx.run.GainGold(ctx.value * percent / 100);
        }
    }
}
