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
        BattleAction = 10, // 戦闘でお守り・振り直し・運命の糸・「使う」をした。a：ラウンド、b：何をしたか（0 お守り・1 振り直し・2 運命の糸・3 使う）、text：数（, でつなぐ）
        // ---- 国めぐり（順番に1人ずつ。手番の人が決めたことを送り、全員の端末で同じように進める） ----
        TourSetup = 20,       // 代表が決めた遊び方。a：勝ち方、b：ターン数、text：AI の数
        TourCard = 21,        // 札を使う。a：札の番号、b：相手（−1 はなし）、text：飛脚札の町の番号
        TourRoll = 22,        // 振る。text：ポーチの何番目のダイスか（, でつなぐ。空なら振れずに1歩）
        TourMove = 23,        // 止まるマス。a：マスの番号
        TourTown = 24,        // 町。a：0 名物を買う・1 ダイスを買う・2 札を買う・3 名物はおしまい・4 店はおしまい、b：番号
        TourDuel = 25,        // 対決。a：挑む相手（−1 は挑まない）
        TourBattleChunk = 26, // 戦闘のあとのランの写し。a：何番目のかけら、b：かけらの数、text：かけら
        TourBattleEnd = 27,   // 戦闘の終わり。a：勝ち負け、b：大ボスに与えたダメージ
        // ---- どのモードでも ----
        Emote = 30,           // 合図（決まった言葉）。a：言葉の番号
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
