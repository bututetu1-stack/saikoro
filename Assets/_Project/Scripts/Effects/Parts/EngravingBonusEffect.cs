using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>出た面に engraving が刻まれていれば、値に add を足す。例：レリック「砥石」（刻印「刃」「堅」の効果+1）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Engraving Bonus", fileName = "Fx_EngravingBonus")]
    public class EngravingBonusEffect : EffectSO
    {
        public EngravingData engraving;
        public int add = 1;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.dice == null || ctx.faceIndex < 0 || ctx.faceIndex >= ctx.dice.faces.Length) return;
            if (ctx.dice.faces[ctx.faceIndex].engraving != engraving || engraving == null) return;
            if (condition.Matches(ctx)) ctx.value += add;
        }
    }
}
