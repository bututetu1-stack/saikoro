using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>ポーチの容量を増やす。例：レリック「大きな巾着」（+1）。OnAcquire で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Pouch Capacity", fileName = "Fx_PouchCapacity")]
    public class PouchCapacityEffect : EffectSO
    {
        public int amount = 1;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.run != null) ctx.run.pouch.Capacity += amount;
        }
    }
}
