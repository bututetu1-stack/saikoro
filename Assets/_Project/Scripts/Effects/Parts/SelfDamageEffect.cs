using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>自分の HP を減らす（防御無視）。例：錆び賽（振るたびに1ダメージ）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Self Damage", fileName = "Fx_SelfDamage")]
    public class SelfDamageEffect : EffectSO
    {
        public int damage = 1;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            target?.LoseHp(damage);
        }
    }
}
