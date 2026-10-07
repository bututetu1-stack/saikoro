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
        // 仕様書の一覧にない追加分（番号がずれないよう末尾に）
        OnAttackResolve,  // 攻撃を解決したとき、攻撃に置いたダイス1個ごと（毒賽・刻印「毒針」など）
        OnAcquire,        // レリックを手に入れたとき、そのレリックだけ（大きな巾着など）
    }
}
