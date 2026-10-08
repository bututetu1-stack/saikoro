using System.Linq;
using SaiNoMichi.Dice;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>使用済みのダイスを count 個、使用可能に戻す（出目の平均が高いものから）。例：ボスレリック「時の砂」（毎ラウンド開始時に1個）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Return Used Die", fileName = "Fx_ReturnUsedDie")]
    public class ReturnUsedDieEffect : EffectSO
    {
        public int count = 1;

        public override void Apply(EffectContext ctx)
        {
            var pouch = ctx.battle != null ? ctx.battle.pouch : ctx.run?.pouch;
            if (pouch == null) return;
            // TODO(仕様): 戻すダイスは選ばせず、出目の平均が高いものから
            foreach (var d in pouch.All.Where(d => d.state == DiceState.Used).OrderByDescending(d => d.faces.Average(f => f.value)).Take(count).ToList())
            {
                d.state = DiceState.Available;
            }
        }
    }
}
