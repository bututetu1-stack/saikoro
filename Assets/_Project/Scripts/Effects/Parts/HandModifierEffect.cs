using SaiNoMichi.Battle;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 役の効果を強くする（壺振りの壺：ゾロ目 +4・嵐太鼓：アラシ 3 倍・ヒフミの札：ヒフミも 3 倍 など）。
    /// 印として持つだけ（BattleState が役を決めるときに調べる）。
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Hand Modifier", fileName = "Fx_HandModifier")]
    public class HandModifierEffect : EffectSO
    {
        public HandKind hand;
        [Tooltip("役に入った出目の倍率をこの値にする（0 なら変えない。元の倍率より小さければ元のまま）")]
        public int multiplier;
        [Tooltip("役に入った出目に足す値を、これだけ増やす")]
        public int addBonus;

        public override void Apply(EffectContext ctx) { }
    }
}
