using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    public enum RaceState
    {
        Running,
        Cleared,   // 第3層のボスを倒した
        Dead,      // 倒れた
    }

    /// <summary>
    /// 対戦「賽レース」で送り合う進み具合（同じシードの盤面なので、層とマスの番号で位置がわかる）。
    /// 通信で送る中身はこれだけ。各自のランの計算は各自の端末で行う。
    /// </summary>
    [Serializable]
    public struct RaceProgress
    {
        public string name;
        public int layer;         // 0 から数える
        public int tileId;        // いまいるマス（その層の盤面の番号）
        public int tilesToGoal;   // ボスまで最短何マス
        public int hp;
        public int maxHp;
        public int turn;
        public RaceState state;
        /// <summary>いま何をしているか（観戦用。1行目が見出し、2行目がいまの出来事）。</summary>
        public string activity;
        /// <summary>賽袋のダイスと状態（観戦用。EncodeDice の形）。</summary>
        public string dice;

        public static RaceProgress From(RunState run, string name)
        {
            var state = run.player.IsDead ? RaceState.Dead : RaceState.Running;
            return new RaceProgress
            {
                name = name,
                layer = run.LayerIndex,
                tileId = run.Current != null ? run.Current.id : 0,
                tilesToGoal = run.Current != null ? run.TilesToGoal : 0,
                hp = Math.Max(0, run.player.hp),
                maxHp = run.player.maxHp,
                turn = run.Turn,
                state = state,
                dice = EncodeDice(run.pouch),
            };
        }

        /// <summary>
        /// 順位を決めるための値（大きいほど前）。踏破した人が一番前（少ないターンほど前）、
        /// 次に先の層・ボスに近い人。倒れた人は、倒れた場所の進み具合で並べる。
        /// </summary>
        public long RankKey
        {
            get
            {
                if (state == RaceState.Cleared) return 1_000_000_000L - turn;
                return layer * 10_000L + (1_000 - Math.Min(1_000, tilesToGoal));
            }
        }

        // ---- 観戦用：賽袋の中身を短い文字にする ----

        /// <summary>通信の1回で送れる文字の量（Fusion の RPC は 512 バイトまで。名前などのぶんを残す）。</summary>
        public const int MaxTextBytes = 440;

        const char DiceSeparator = '|';

        /// <summary>賽袋のダイスを「状態の1文字＋名前」を | でつないだ文字にする（A：使用可能、U：使用済み、S：封印）。長すぎたら後ろを省く。</summary>
        public static string EncodeDice(DicePouch pouch)
        {
            var sb = new StringBuilder();
            foreach (var d in pouch.All)
            {
                string item = StateCode(d.state) + d.DisplayName.Replace(DiceSeparator, ' ');
                string next = sb.Length == 0 ? item : sb + DiceSeparator.ToString() + item;
                if (Encoding.UTF8.GetByteCount(next) > MaxTextBytes) break;
                if (sb.Length > 0) sb.Append(DiceSeparator);
                sb.Append(item);
            }
            return sb.ToString();
        }

        /// <summary>EncodeDice の文字を、名前と状態の並びに戻す。</summary>
        public static List<(string name, DiceState state)> DecodeDice(string text)
        {
            var list = new List<(string, DiceState)>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var item in text.Split(DiceSeparator))
            {
                if (item.Length < 2) continue;
                list.Add((item.Substring(1), StateFromCode(item[0])));
            }
            return list;
        }

        static char StateCode(DiceState state) => state == DiceState.Used ? 'U' : state == DiceState.Sealed ? 'S' : 'A';
        static DiceState StateFromCode(char c) => c == 'U' ? DiceState.Used : c == 'S' ? DiceState.Sealed : DiceState.Available;

        /// <summary>UTF-8 で maxBytes バイトに収まるよう、後ろを切る（通信で送る文字用）。</summary>
        public static string Clip(string text, int maxBytes = MaxTextBytes)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes) return text ?? "";
            int length = text.Length;
            while (length > 0 && Encoding.UTF8.GetByteCount(text.Substring(0, length)) > maxBytes - 3) length--;
            if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length) + "…";
        }
    }
}
