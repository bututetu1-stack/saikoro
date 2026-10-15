namespace SaiNoMichi.Run
{
    /// <summary>旅人（プレイヤーキャラ。仕様書 第12章）。初期ダイスと固有の能力が違う。</summary>
    public enum TravelerKind
    {
        Saifuri = 0,    // 賽振り：基本の旅人（はじめのダイス＋スターター3択）
        Bakuto = 1,     // 博徒：戦闘に勝つと、持ち金の10%を利息として得る
        Saikushi = 2,   // 細工師：鍛冶で提示される刻印が4つ
        Yamabushi = 3,  // 山伏：休憩で「休む」と「鍛える」の両方ができる
    }

    public static class Travelers
    {
        public static readonly TravelerKind[] All = { TravelerKind.Saifuri, TravelerKind.Bakuto, TravelerKind.Saikushi, TravelerKind.Yamabushi };

        // 仕様書 第12章の値。TODO(仕様): 博徒の利息に上限を付けるかは未定（いまはなし）
        public const int BakutoInterestPercent = 10;
        public const int SaikushiForgeChoices = 4;
        public const string BladeEngravingId = "yaiba";
        public const int BakutoUnlockLayer = 2;
        public const int SaikushiUnlockEngravings = 10;

        public static string Name(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return "博徒";
                case TravelerKind.Saikushi: return "細工師";
                case TravelerKind.Yamabushi: return "山伏";
                default: return "賽振り";
            }
        }

        public static string Ability(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return $"戦闘に勝つと、持ち金の{BakutoInterestPercent}%を利息として得る";
                case TravelerKind.Saikushi: return $"鍛冶で提示される刻印が{SaikushiForgeChoices}つに増える";
                case TravelerKind.Yamabushi: return "休憩で「休む」と「鍛える」の両方ができる";
                default: return "なし（基本の旅人）。スターターダイスを3つから選べる";
            }
        }

        public static string UnlockCondition(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return $"第{BakutoUnlockLayer}層に着くと解放";
                case TravelerKind.Saikushi: return $"刻印を合わせて{SaikushiUnlockEngravings}個付けると解放";
                case TravelerKind.Yamabushi: return "一度クリアすると解放";
                default: return "最初から";
            }
        }

        /// <summary>はじめのダイスの id（賽振りは null：設定の初期構成＋スターター）。</summary>
        public static string[] StartingDiceIds(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return new[] { "normal", "normal", "bakuchi", "cho" };
                case TravelerKind.Saikushi: return new[] { "normal", "normal", "normal" }; // 1個目の6の面に刃
                case TravelerKind.Yamabushi: return new[] { "hifumi", "hifumi", "tate", "tate" };
                default: return null;
            }
        }
    }
}
