namespace SaiNoMichi.Effects
{
    /// <summary>効果が発動するタイミング（仕様書 第14章）。</summary>
    public enum Trigger
    {
        OnRoll,
        OnAssignAttack,
        OnAssignDefense,
        OnRoundStart,
        OnRoundEnd,
        OnBattleStart,
        OnBattleEnd,
        OnRefresh,
        OnMoveRolled,
        OnPassTile,
        OnStopTile,
        OnLayerStart,
        OnGoldGain,
    }
}
