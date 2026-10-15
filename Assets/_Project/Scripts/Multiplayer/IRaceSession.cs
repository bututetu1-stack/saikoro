using System;
using System.Collections.Generic;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 対戦「賽レース」の相手とのつながり。通信（Photon Fusion）でも、AI の練習相手でも、同じ形で使う。
    /// 自分の進み具合を Report で知らせ、相手の進み具合は Rivals で読む（変わったら Changed）。
    /// </summary>
    public interface IRaceSession
    {
        /// <summary>全員が同じ盤面を遊ぶためのシード。</summary>
        int Seed { get; }

        string LocalName { get; }

        /// <summary>相手の名前と進み具合（自分は入らない）。</summary>
        IReadOnlyList<RaceProgress> Rivals { get; }

        event Action Changed;

        /// <summary>自分の進み具合を知らせる（移動・戦闘のあと、層を移ったとき、終わったとき）。</summary>
        void Report(RaceProgress local);

        /// <summary>妨害札を先頭の相手（踏破していない人で一番前）に送る。送った相手の名前を返す（いなければ null）。</summary>
        string SendSabotage(SabotageKind kind);

        /// <summary>相手から妨害札が届いた（送った人の名前, 種類）。</summary>
        event Action<string, SabotageKind> SabotageReceived;

        /// <summary>レースから抜ける（ランが終わった・始めの画面に戻った）。</summary>
        void Leave();
    }
}
