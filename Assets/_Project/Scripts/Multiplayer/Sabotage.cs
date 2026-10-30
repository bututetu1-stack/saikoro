using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>妨害札の種類（対戦「賽レース」で、先頭の相手に送る。Tetris 99 の攻撃のようなもの）。</summary>
    public enum SabotageKind
    {
        Slow,    // 足止めの札：次の移動の出目 −2
        Curse,   // 呪いの札：呪いのダイスが1個入る
    }

    /// <summary>妨害札のルール。強敵・ボスを倒すと1枚もらえる（最大3枚）。</summary>
    public static class Sabotage
    {
        // TODO(仕様): 枚数の上限・足止めの量は仮
        public const int MaxCards = 3;
        public const int SlowAmount = 2;

        public static readonly SabotageKind[] All = { SabotageKind.Slow, SabotageKind.Curse };

        public static string Name(SabotageKind kind) => kind == SabotageKind.Slow ? "足止めの札" : "呪いの札";

        public static string Description(SabotageKind kind) => kind == SabotageKind.Slow
            ? $"相手の次の移動の出目 −{SlowAmount}（最低1）"
            : "相手の賽袋に呪いのダイスを1個入れる（満杯なら入らない）";

        /// <summary>送られてきた妨害札を、自分のランに効かせる。起きたことの説明を返す。</summary>
        public static string Apply(RunState run, SabotageKind kind)
        {
            switch (kind)
            {
                case SabotageKind.Slow:
                    run.AddMovePenalty(SlowAmount);
                    return $"次の移動の出目 −{SlowAmount}";
                case SabotageKind.Curse:
                    var die = run.ForceCurse();
                    return die != null ? $"{die.DisplayName}を押し付けられた" : "賽袋が満杯で、呪いは入らなかった";
                default:
                    return "";
            }
        }
    }
}
