using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 回数つきで、移動の出目を ±range から選べるようにする。例：レリック「草鞋」（層ごとに3回）。OnMoveRolled で使う。
    /// 回数は実際に出目を変えたときだけ減る。ほかの効果（刻印「風」など）で同じだけ変えられるなら使わない。
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Charged Move Adjust", fileName = "Fx_ChargedMoveAdjust")]
    public class ChargedMoveAdjustEffect : EffectSO
    {
        public int range = 1;
        public int chargesPerLayer = 3;
        public string label = "草鞋";

        public override void Apply(EffectContext ctx)
        {
            if (ctx.run == null || range <= ctx.moveAdjust || ctx.run.ChargesOf(this) <= 0) return;
            ctx.moveAdjust = range;
            ctx.moveAdjustLabel = label;
            ctx.moveAdjustCharge = this;
        }
    }
}
