using System;
using System.Collections.Generic;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 通信でつながる対戦（部屋に集まって、代表が「開始」を押すと全員同時に始まる）。
    /// 実体は通信のアセンブリ（SaiNoMichi.Net。Photon Fusion 2）にあり、起動時に OnlineRace.Factory へ登録される。
    /// ゲーム本体は Fusion を知らずに済む（SDK がなくてもゲーム本体はそのまま動く）。
    /// </summary>
    public interface IOnlineRace : IOnlineRoom, IRaceSession
    {
        /// <summary>代表が「開始」を押した（全員に届く）。</summary>
        event Action RaceStarted;

        /// <summary>代表だけ：レースを始める。</summary>
        void StartRace();
    }

    /// <summary>通信の対戦を作る入口。通信のアセンブリがないとき（SDK がないとき）は Factory が null。</summary>
    public static class OnlineRace
    {
        public const int MaxPlayers = 4;

        /// <summary>（合言葉, 自分の名前）から部屋に入る。通信のアセンブリが起動時に入れる。</summary>
        public static Func<string, string, IOnlineRace> Factory;

        public static bool Available => Factory != null;

        /// <summary>合言葉からシードを決める（同じ合言葉なら全員同じ盤面）。</summary>
        public static int SeedFromCode(string code)
        {
            // FNV-1a（どの端末でも同じ値になるように、string.GetHashCode は使わない）
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in "sainomichi-race-" + code)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF) | 1;
            }
        }
    }
}
