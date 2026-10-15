using System;
using System.Collections.Generic;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 通信の部屋（対戦・協力で共通）：合言葉かだれかとで部屋に入り、集まった人で代表が「開始」を押す。
    /// </summary>
    public interface IOnlineRoom
    {
        /// <summary>部屋の合言葉（数字）。だれかと遊ぶ部屋は「だれかと」。</summary>
        string RoomCode { get; }

        /// <summary>つながったか・つながっている途中か・失敗したか（画面に出す文）。</summary>
        string Status { get; }

        bool IsConnected { get; }

        /// <summary>部屋の代表か（代表だけが「開始」を押せる）。</summary>
        bool IsHost { get; }

        /// <summary>部屋にいる人の名前（自分も入る）。</summary>
        IReadOnlyList<string> Members { get; }

        /// <summary>部屋の人数や、つながり方が変わった。</summary>
        event Action LobbyChanged;
    }
}
