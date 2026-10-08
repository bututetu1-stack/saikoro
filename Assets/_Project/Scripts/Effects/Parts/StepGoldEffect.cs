using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>貯金箱：steps マス進むごとに gold を得る。OnStep（ctx.amount=ランで進んだ歩数）で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Step Gold", fileName = "Fx_StepGold")]
    public class StepGoldEffect : EffectSO
    {
        public int steps = 10;
        public int gold = 8;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.run != null && steps > 0 && ctx.amount > 0 && ctx.amount % steps == 0) ctx.run.GainGold(gold);
        }
    }
}
