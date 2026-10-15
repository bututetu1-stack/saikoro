using System;
using System.Collections.Generic;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>協力「賽の旅団」で送り合う知らせの種類。</summary>
    public enum PartyCommandType
    {
        Rolled = 1,    // 移動のダイスを振った。a：出目（進む数。振れなかったら −1）、b：行き先が決まっていればそのマスの番号（なければ −1）、text：ダイスの名前
        Vote = 2,      // 行き先のマスに票を入れた。a：マスの番号
        Ready = 3,     // 止まったマスの出来事を終えて、次の移動を待っている。a：ターン数
        Status = 4,    // HP などが変わった。a：HP、b：最大HP、text：いま何をしているか
        Down = 5,      // 倒れた（この旅から抜ける）
    }

    /// <summary>協力の知らせ1つ。seq は部屋の代表が付けた通し番号（全員が同じ順で受け取る）。</summary>
    [Serializable]
    public struct PartyCommand
    {
        public int seq;
        public int slot;   // 送った人の番号（0 から。部屋に入った順）
        public PartyCommandType type;
        public int a;
        public int b;
        public string text;
    }

    /// <summary>
    /// 協力「賽の旅団」の仲間とのつながり。通信（Photon Fusion）でも、AI の仲間との練習でも、同じ形で使う。
    /// 送った知らせは、部屋の代表が通し番号を付けて全員（送った本人も）に配るので、全員が同じ順で受け取る。
    /// </summary>
    public interface IPartySession
    {
        /// <summary>全員が同じ盤面を遊ぶためのシード。</summary>
        int Seed { get; }

        /// <summary>自分の番号（0 から）。</summary>
        int LocalSlot { get; }

        /// <summary>仲間の名前（番号の順。自分も入る）。</summary>
        IReadOnlyList<string> Names { get; }

        /// <summary>同じ票数のときに行き先を決める人（部屋の代表）の番号。</summary>
        int LeaderSlot { get; }

        /// <summary>知らせが届いた（自分が送ったものも、ここに届いてから使う）。</summary>
        event Action<PartyCommand> CommandReceived;

        /// <summary>仲間が抜けた（番号）。</summary>
        event Action<int> MemberLeft;

        void Send(PartyCommandType type, int a = 0, int b = 0, string text = null);

        void Leave();
    }
}
