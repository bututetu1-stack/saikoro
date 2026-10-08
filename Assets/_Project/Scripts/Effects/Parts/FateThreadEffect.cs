using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>運命の糸：1戦闘に1回、振った出目1つを好きな値（1〜maxValue）に変えられる。印として持つだけ（BattleState が調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Fate Thread", fileName = "Fx_FateThread")]
    public class FateThreadEffect : EffectSO
    {
        public int maxValue = 6;

        public override void Apply(EffectContext ctx) { }
    }
}
