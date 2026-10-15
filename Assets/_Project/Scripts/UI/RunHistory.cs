using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ラン履歴（終わった旅の記録。新しい順に最大 30 件）と図鑑（出会ったダイス・レリック・敵）。
    /// この端末・ブラウザに保存する（WebGL は PlayerPrefs）。
    /// </summary>
    public static class RunHistory
    {
        const string HistoryKey = "SaiNoMichi.RunHistory";
        const string SeenPrefix = "SaiNoMichi.Seen.";
        public const int MaxEntries = 30;

        [Serializable]
        public class Entry
        {
            public string date;      // 「10/15 18:30」
            public bool cleared;
            public int layer;        // 1 から数える
            public int turns;
            public int seed;
            public string cause;     // 倒された原因（踏破なら空）
            public string mode;      // 「ひとり」「賽レース」など
            public string starter;   // 最初に選んだダイス
            public int score;        // 得点（RunScore。前の版の記録は 0）
        }

        [Serializable]
        class EntryList
        {
            public List<Entry> entries = new List<Entry>();
        }

        public static IReadOnlyList<Entry> Entries => Load().entries;

        public static void Add(Entry entry)
        {
            var list = Load();
            list.entries.Insert(0, entry);
            if (list.entries.Count > MaxEntries) list.entries.RemoveRange(MaxEntries, list.entries.Count - MaxEntries);
            PlayerPrefs.SetString(HistoryKey, JsonUtility.ToJson(list));
            PlayerPrefs.Save();
        }

        static EntryList Load()
        {
            string json = PlayerPrefs.GetString(HistoryKey, "");
            if (string.IsNullOrEmpty(json)) return new EntryList();
            try { return JsonUtility.FromJson<EntryList>(json) ?? new EntryList(); }
            catch (ArgumentException) { return new EntryList(); }
        }

        // ---- 図鑑：出会ったもの ----

        public enum Category { Dice, Relic, Enemy, Charm }

        public static HashSet<string> Seen(Category category)
        {
            string text = PlayerPrefs.GetString(SeenPrefix + category, "");
            return new HashSet<string>(text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>図鑑に載せる（すでに載っていれば何もしない）。</summary>
        public static void MarkSeen(Category category, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var seen = Seen(category);
            if (!seen.Add(id)) return;
            PlayerPrefs.SetString(SeenPrefix + category, string.Join(",", seen.OrderBy(s => s)));
            PlayerPrefs.Save();
        }
    }
}
