using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>天秤：攻撃と防御が同じ値（0 より大きい）になったラウンドの終わりに HP を回復。OnRoundEnd（ctx.value=攻撃値、ctx.amount=防御値）で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Balance Heal", fileName = "Fx_BalanceHeal")]
    public class BalanceHealEffect : EffectSO
    {
        public int heal = 3;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            if (target != null && ctx.value > 0 && ctx.value == ctx.amount) target.Heal(heal);
        }
    }
}
