using System.Collections.Generic;

namespace SaiNoMichi.Effects
{
    /// <summary>効果の持ち主の種類。値の小さい順に実行する（仕様書 第14章「刻印 → レリック → 状態異常」）。</summary>
    public enum EffectSourceKind
    {
        Dice = 0,       // ダイスそのものの特徴（盾賽の防御+2 など）。刻印より先
        Engraving = 1,
        Relic = 2,
        Status = 3,
    }

    public interface IEffectSource
    {
        EffectSourceKind Kind { get; }
        IReadOnlyList<EffectSO> Effects { get; }
    }
}
