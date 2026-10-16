namespace SaiNoMichi.Run
{
    /// <summary>
    /// 旅人（プレイヤーキャラ。仕様書 第12章）。はじめのダイスと固有の能力が違う。
    /// 2026-10-17 開発者の要望で、役割分担（回復・攻撃・デバフ・防御）にした。どれかが上位互換にならないよう、ビルドの向きを変える。
    /// </summary>
    public enum TravelerKind
    {
        Saifuri = 0,    // 賽振り（回復役）：リフレッシュしたとき HP 回復（協力の戦闘では仲間も）。スターターを3つから選ぶ
        Bakuto = 1,     // 博徒（攻撃役）：6の目で攻撃するとそのダイスの攻撃+2
        Saikushi = 2,   // 細工師（デバフ役）：敵に与える弱体・脱力・脆弱 +1
        Yamabushi = 3,  // 山伏（防御役）：2以下の目を防御に置くと+1。協力では得た防御の半分を仲間にも
    }

    public static class Travelers
    {
        public static readonly TravelerKind[] All = { TravelerKind.Saifuri, TravelerKind.Bakuto, TravelerKind.Saikushi, TravelerKind.Yamabushi };

        // TODO(仕様): 能力の数値はすべて仮（2026-10-17 役割分担のときに決めた）
        public const int SaifuriRefreshHeal = 1;     // 賽振り：リフレッシュしたときの回復（3 だと自動プレイの踏破率が 69% と高すぎた）
        public const int BakutoBigValue = 6;         // 博徒：この目以上（6＝大当たり）で攻撃すると
        public const int BakutoBigBonus = 2;         // 　　　そのダイスの攻撃 +2（+3 は踏破率 56% と高め）
        public const int SaikushiDebuffBonus = 1;    // 細工師：与える弱体・脱力・脆弱 +1
        public const int YamabushiBlockBonus = 1;    // 山伏：小さい目（3以下）を防御に置くと +1（どの目でも +1 は踏破率 71% と強すぎた）
        public const int YamabushiSmallValue = 2; // 3以下だと踏破率 57%
        public const int YamabushiSharePercent = 50; // 山伏：協力の戦闘で、得た防御のこの割合を仲間全員にも
        // 2つ目の特性（旅の向き。どれも小さめ）
        public const int SaifuriRestBonusPercent = 25; // 賽振り：休憩の回復量 +25%
        public const int BakutoInterestPercent = 5;    // 博徒：戦闘に勝つと持ち金の 5% の利息
        public const int BakutoInterestMax = 15;       // 　　　（最大 15G）
        public const int SaikushiForgeChoices = 4;     // 細工師：鍛冶で提示される刻印が4つ
        public const int YamabushiPoisonResist = 1;    // 山伏：受ける毒 −1（戦闘開始時の防御4 は踏破率 70% と強すぎた）
        public const string BladeEngravingId = "yaiba";
        public const string CrushEngravingId = "kuzushi";
        public const string GuardEngravingId = "kata";
        /// <summary>旅人専用のダイス（賽振りは、はじめの普通の賽の1個がこれに替わる）。</summary>
        public const string SaifuriDiceId = "tabisai";
        public const string SaikushiDiceId = "karakuri";
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

        /// <summary>役割（協力でのおおまかな役どころ）。</summary>
        public static string Role(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return "攻撃役";
                case TravelerKind.Saikushi: return "デバフ役";
                case TravelerKind.Yamabushi: return "防御役";
                default: return "回復役";
            }
        }

        public static string Ability(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return $"・{BakutoBigValue}の目で攻撃すると、そのダイスの攻撃+{BakutoBigBonus}（大当たり）\n・戦闘に勝つと持ち金の{BakutoInterestPercent}%の利息（最大{BakutoInterestMax}G）";
                case TravelerKind.Saikushi: return $"・敵に与える弱体・脱力・脆弱が+{SaikushiDebuffBonus}（ダイス・刻印・お守り）\n・鍛冶で提示される刻印が{SaikushiForgeChoices}つ";
                case TravelerKind.Yamabushi: return $"・{YamabushiSmallValue}以下の目を防御に置くと+{YamabushiBlockBonus}。協力の戦闘では、得た防御の半分を仲間全員にも\n・受ける毒が{YamabushiPoisonResist}少ない";
                default: return $"・リフレッシュしたとき HP {SaifuriRefreshHeal} 回復（協力の戦闘では仲間全員も）\n・休憩の回復量+{SaifuriRestBonusPercent}%。スターターダイスを3つ（ランダム）から選べる";
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
                case TravelerKind.Bakuto: return new[] { "normal", "normal", "bakuchi", "ikasama" };
                // デバフ役：針賽（攻撃＋弱体1）と、からくり賽（6の面に「崩し」・1の面に「堅」）
                case TravelerKind.Saikushi: return new[] { "normal", "normal", "hari", "karakuri" };
                case TravelerKind.Yamabushi: return new[] { "hifumi", "hifumi", "tate", "shakujou" };
                default: return null;
            }
        }
    }
}
