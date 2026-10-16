using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>勝負師の煙管：役ができたラウンドは、もう1個振れる。印として持つだけ（BattleState が調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Hand Extra Die", fileName = "Fx_HandExtraDie")]
    public class HandExtraDieEffect : EffectSO
    {
        public override void Apply(EffectContext ctx) { }
    }
}
