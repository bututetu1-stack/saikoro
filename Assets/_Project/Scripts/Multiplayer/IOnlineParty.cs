using System;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 通信でつながる協力「賽の旅団」（部屋に集まって、代表が「開始」を押すと全員同時に始まる）。
    /// 実体は通信のアセンブリ（SaiNoMichi.Net の NetParty）にあり、起動時に OnlineParty.Factory へ登録される。
    /// </summary>
    public interface IOnlineParty : IOnlineRoom, IPartySession
    {
        /// <summary>代表が「開始」を押した（全員に届く）。</summary>
        event Action PartyStarted;

        /// <summary>代表だけ：旅を始める。</summary>
        void StartParty();
    }

    /// <summary>通信の協力を作る入口。通信のアセンブリがないとき（SDK がないとき）は Factory が null。</summary>
    public static class OnlineParty
    {
        public const int MaxPlayers = 4;

        /// <summary>（合言葉, 自分の名前, 遊び方）から部屋に入る。合言葉が null なら、だれかと。遊び方ごとに部屋は別（"party"：協力・"tour"：国めぐり）。</summary>
        public static Func<string, string, string, IOnlineParty> Factory;

        public static bool Available => Factory != null;

        /// <summary>合言葉からシードを決める（同じ合言葉なら全員同じ盤面）。</summary>
        public static int SeedFromCode(string code, string kind = "party")
        {
            // FNV-1a（どの端末でも同じ値になるように、string.GetHashCode は使わない）
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in "sainomichi-" + kind + "-" + code)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF) | 1;
            }
        }
    }
}
