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
    }
}
