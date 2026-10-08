using System;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>最大HPを増減する（HP は最大HPを超えないように）。例：ボスレリック「時の砂」（−10）。OnAcquire で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Max Hp", fileName = "Fx_MaxHp")]
    public class MaxHpEffect : EffectSO
    {
        public int amount;

        public override void Apply(EffectContext ctx)
        {
            var target = ctx.player ?? ctx.run?.player;
            if (target == null) return;
            target.maxHp = Math.Max(1, target.maxHp + amount);
            if (amount > 0) target.hp += amount;
            target.hp = Math.Max(1, Math.Min(target.hp, target.maxHp));
        }
    }
}
