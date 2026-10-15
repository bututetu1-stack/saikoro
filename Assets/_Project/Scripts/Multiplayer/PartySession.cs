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
        // ---- 協力の戦闘（全員で同じ敵と戦う） ----
        Snapshot = 6,     // 戦闘の前に、自分のランの写し（RunSave）を分けて送る。a：何番目のかけら、b：かけらの数、text：かけら
        BattleSetup = 7,  // 代表：戦う敵。a：戦闘のシード、b：敵の HP の倍率（%）、text：敵の id
        BattleRoll = 8,   // 戦闘でダイスを振った。a：ラウンド、text：ポーチの何番目のダイスか（, でつなぐ）
        BattleReady = 9,  // 割り振りを決めた。a：ラウンド、b：狙う敵の番号（−1 はそのまま）、text：振ったダイスごとの割り振り（, でつなぐ）
        BattleAction = 10, // 戦闘でお守り・振り直し・運命の糸を使った。a：ラウンド、b：何をしたか（0 お守り・1 振り直し・2 運命の糸）、text：数（, でつなぐ）
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
