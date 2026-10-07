using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>HP を回復する。例：刻印「薬」（2）、レリック「鈴」（移動中のリフレッシュで3）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Heal", fileName = "Fx_Heal")]
    public class HealEffect : EffectSO
    {
        public int heal;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            if (target != null && condition.Matches(ctx)) target.Heal(heal);
        }
    }
}
