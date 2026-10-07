using System;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>移動の出目を ±range の中から選べるようにする。例：刻印「風」（出目±1から止まるマスを選べる）。OnMoveRolled で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Move Adjust", fileName = "Fx_MoveAdjust")]
    public class MoveAdjustEffect : EffectSO
    {
        public int range = 1;

        public override void Apply(EffectContext ctx)
        {
            ctx.moveAdjust = Math.Max(ctx.moveAdjust, range);
        }
    }
}
