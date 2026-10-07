namespace SaiNoMichi.Board
{
    /// <summary>マスの種類（仕様書 第8章）。保存済みのデータと番号がずれないよう、追加は末尾に。</summary>
    public enum TileType
    {
        Empty,
        Battle,
        Rest,
        Boss,
        Event,
        Trap,
        Treasure,
        Shop,
        Forge,
        Elite,
        // 通過しても効くマス（仕様書 第8章「通過しても効果があるマス」）。止まると効果2倍
        Shrine,      // 祠：5Gを得る
        Checkpoint,  // 関所：10Gを払う。払えなければ5ダメージ
        Teahouse,    // 茶屋：HP3回復
        DiceHall,    // 賽場：使用済みのダイス1個を使用可能に戻す
    }

    public static class TileTypeExtensions
    {
        public static bool IsPassTile(this TileType type) =>
            type == TileType.Shrine || type == TileType.Checkpoint || type == TileType.Teahouse || type == TileType.DiceHall;
    }
}
