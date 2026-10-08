using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>千里眼：移動で振る前に出目が見える（その上で振るダイスを変えられる）。印として持つだけ（RunState が調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Foresight", fileName = "Fx_Foresight")]
    public class ForesightEffect : EffectSO
    {
        public override void Apply(EffectContext ctx) { }
    }
}
