using UnityEngine;

namespace SaiNoMichi.Effects
{
    public enum AttackRider
    {
        Lifesteal,       // 刻印「吸血」：そのラウンドに与えたダメージの percent% を回復（切り捨て）
        Vulnerable,      // 刻印「崩し」：狙った敵に脆弱 amount
        ReduceIntent,    // 刻印「足枷」：狙った敵の予告した攻撃値 −amount
        Weak,            // 狙った敵に弱体 amount
    }

    /// <summary>攻撃に置いたとき、攻撃のあとに起きること（吸血・崩し・足枷）。OnAttackResolve で使う。ctx.amount はそのラウンドに与えたダメージ。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Attack Rider", fileName = "Fx_AttackRider")]
    public class AttackRiderEffect : EffectSO
    {
        public AttackRider rider;
        public int amount = 1;
        public int percent = 50;

        public override void Apply(EffectContext ctx)
        {
            switch (rider)
            {
                case AttackRider.Lifesteal:
                    var target = ctx.player ?? ctx.run?.player;
                    if (target != null) target.Heal(ctx.amount * percent / 100);
                    break;
                case AttackRider.Vulnerable:
                    if (ctx.enemy != null && !ctx.enemy.IsDead) ctx.enemy.ApplyVulnerable(amount);
                    break;
                case AttackRider.Weak:
                    if (ctx.enemy != null && !ctx.enemy.IsDead) ctx.enemy.ApplyWeak(amount);
                    break;
                case AttackRider.ReduceIntent:
                    if (ctx.enemy != null && !ctx.enemy.IsDead) ctx.enemy.ReduceIntent(amount);
                    break;
            }
        }
    }
}
