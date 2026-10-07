using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>自分の筋力を得る（戦闘が終わると消える）。例：レリック「鈴」（戦闘中のリフレッシュで筋力+1）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Gain Strength", fileName = "Fx_GainStrength")]
    public class GainStrengthEffect : EffectSO
    {
        public int strength;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            if (target != null && condition.Matches(ctx)) target.strength += strength;
        }
    }
}
