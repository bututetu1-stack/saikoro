using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>役者賽：このダイスの出目が役に入ったら、さらに add を足す（役の倍率のあと）。印として持つだけ（BattleState が調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Hand Member Bonus", fileName = "Fx_HandMemberBonus")]
    public class HandMemberBonusEffect : EffectSO
    {
        public int add = 3;

        public override void Apply(EffectContext ctx) { }
    }
}
