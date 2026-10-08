using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>ゾロ目の守り：同じラウンドに振った出目が同じダイスは、攻撃と防御の両方に効く。印として持つだけ（BattleState が調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Pair Both Sides", fileName = "Fx_PairBothSides")]
    public class PairBothSidesEffect : EffectSO
    {
        public override void Apply(EffectContext ctx) { }
    }
}
