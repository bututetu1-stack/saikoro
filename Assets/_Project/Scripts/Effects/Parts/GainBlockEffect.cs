using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>自分の防御値を得る。例：レリック「木の盾」（戦闘開始時に防御5）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Gain Block", fileName = "Fx_GainBlock")]
    public class GainBlockEffect : EffectSO
    {
        public int block;
        [Tooltip("堅守も得る（ラウンドの終わりに防御が半分残る。構え賽）")]
        public int fortify;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            if (target == null || !condition.Matches(ctx)) return;
            target.block += block;
            if (fortify > 0) target.ApplyFortify(fortify);
        }
    }
}
