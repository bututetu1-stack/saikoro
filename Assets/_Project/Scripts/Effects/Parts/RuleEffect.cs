using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>レリックが変える「決まりごと」。効果のタイミングではなく、持っているあいだずっと効く（ボスレリックの悪い効果など）。</summary>
    public enum RunRule
    {
        NoRestHeal,          // 休憩マスで回復できない（重い王冠）
        EngraveBoughtDice,   // 買ったダイスにランダムな刻印が1つ付く（縛りの腕輪）
        HalfShopStock,       // ショップの品数が半分（縛りの腕輪）
    }

    /// <summary>持っているあいだ、決まりごとを変える。Apply では何もしない（RunState.HasRule で調べる）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Rule", fileName = "Fx_Rule")]
    public class RuleEffect : EffectSO
    {
        public RunRule rule;

        public override void Apply(EffectContext ctx) { }
    }
}
