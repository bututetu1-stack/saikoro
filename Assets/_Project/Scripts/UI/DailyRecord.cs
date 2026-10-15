using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>今日の旅の記録（日ごとの最高と挑戦の回数）。この端末・ブラウザに保存する（WebGL は PlayerPrefs）。</summary>
    public static class DailyRecord
    {
        const string Prefix = "SaiNoMichi.Daily.";
        const int ClearBase = 5000;
        const int MaxTurns = 4000;

        /// <summary>その日の最高の点（まだなければ 0）。踏破は早いほど高く、踏破しなければ着いた層で決まる。</summary>
        public static int Best(string key) => PlayerPrefs.GetInt(Prefix + key, 0);

        /// <summary>その日に挑んだ回数。</summary>
        public static int Tries(string key) => PlayerPrefs.GetInt(Prefix + key + ".tries", 0);

        public static int ScoreOf(bool cleared, int layer, int turns) =>
            cleared ? ClearBase + Mathf.Max(0, MaxTurns - turns) : Mathf.Max(1, layer) * 100;

        /// <summary>点を「踏破 42 ターン」「第2層まで」の形にする。</summary>
        public static string Describe(int score) =>
            score <= 0 ? "まだ" : score >= ClearBase ? $"踏破 {MaxTurns - (score - ClearBase)} ターン" : $"第{score / 100}層まで";

        public static void CountTry(string key)
        {
            PlayerPrefs.SetInt(Prefix + key + ".tries", Tries(key) + 1);
            PlayerPrefs.Save();
        }

        /// <summary>記録する。その日の最高を更新したら true。</summary>
        public static bool Submit(string key, int score)
        {
            if (score <= Best(key)) return false;
            PlayerPrefs.SetInt(Prefix + key, score);
            PlayerPrefs.Save();
            return true;
        }
    }
}
