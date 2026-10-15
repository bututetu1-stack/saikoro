using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 実績（Balatro や Steam のゲームの実績風）。この端末・ブラウザに保存する（WebGL は PlayerPrefs）。
    /// 記録の画面の「実績」で一覧を見られる。取ったときは画面の上にお知らせを出す。
    /// </summary>
    public static class Achievements
    {
        public struct Def
        {
            public string id, name, description;
            public Def(string id, string name, string description)
            {
                this.id = id;
                this.name = name;
                this.description = description;
            }
        }

        // TODO(仕様): 実績の種類と条件は仮
        public const int FastClearTurns = 40;
        public const int RichGold = 1000;
        public const int ManyEngravings = 8;
        public const int ManyRelics = 10;

        public static readonly Def[] All =
        {
            new Def("clear", "旅の終わり", "一度クリアする"),
            new Def("clear_fast", "韋駄天", $"{FastClearTurns}ターン以内でクリアする"),
            new Def("asc5", "修羅の道", "段位5以上でクリアする"),
            new Def("asc10", "賽の神を越えて", "段位10でクリアする"),
            new Def("bakuto", "勝負師", "博徒でクリアする"),
            new Def("saikushi", "名工", "細工師でクリアする"),
            new Def("yamabushi", "修験者", "山伏でクリアする"),
            new Def("daily", "今日も旅日和", "今日の旅でクリアする"),
            new Def("party", "旅は道連れ", "賽の旅団（協力）でクリアする"),
            new Def("race", "一番乗り", "通信の賽レースで1位になる"),
            new Def("tour", "天下一の長者", "賽の国めぐりで1位になる"),
            new Def("full_pouch", "賽の山", "ポーチを10個のダイスでいっぱいにして旅を終える"),
            new Def("relics", "蒐集家", $"1回の旅でレリックを{ManyRelics}個持つ"),
            new Def("rich", "千両役者", $"1回の旅で {RichGold} G 以上を手に入れる"),
            new Def("engrave", "刻印の達人", $"1回の旅で刻印を{ManyEngravings}個付ける"),
            new Def("enemies", "妖怪博士", "図鑑の敵をすべて見る"),
        };

        const string Prefix = "SaiNoMichi.Achievement.";

        public static bool IsUnlocked(string id) => PlayerPrefs.GetInt(Prefix + id, 0) != 0;

        public static int UnlockedCount => All.Count(a => IsUnlocked(a.id));

        /// <summary>実績を取る。初めて取ったときだけ、その実績を返す（もう取っていれば null）。</summary>
        public static Def? Unlock(string id)
        {
            if (IsUnlocked(id)) return null;
            int index = System.Array.FindIndex(All, a => a.id == id);
            if (index < 0) return null;
            PlayerPrefs.SetInt(Prefix + id, 1);
            PlayerPrefs.Save();
            return All[index];
        }

        /// <summary>いくつかの実績をまとめて取る。初めて取ったものを返す。</summary>
        public static List<Def> UnlockAll(IEnumerable<string> ids)
        {
            var list = new List<Def>();
            foreach (var id in ids)
            {
                var got = Unlock(id);
                if (got.HasValue) list.Add(got.Value);
            }
            return list;
        }
    }
}
