using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 敵に毒を与える。量は「固定値 + 出目 × perPip」。
    /// 例：毒賽（攻撃に置くと出目×2の毒。開発者の判断で固定2から変更）、刻印「毒針」（固定3）。OnAttackResolve で使う。
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Apply Poison", fileName = "Fx_ApplyPoison")]
    public class ApplyPoisonEffect : EffectSO
    {
        public int flat;
        public int perPip;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.enemy == null || !condition.Matches(ctx)) return;
            ctx.enemy.ApplyPoison(flat + ctx.value * perPip);
        }
    }
}
