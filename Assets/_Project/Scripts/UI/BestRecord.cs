using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 自己ベスト：踏破にかかったターン数（少ないほどよい）。この端末・ブラウザに保存する（WebGL は PlayerPrefs）。
    /// 全員の順位は unityroom のランキング（Ranking）
    /// </summary>
    public static class BestRecord
    {
        const string Key = "SaiNoMichi.BestClearTurns";

        /// <summary>自己ベストのターン数。まだ踏破していなければ 0。</summary>
        public static int BestTurns => PlayerPrefs.GetInt(Key, 0);

        /// <summary>踏破したターン数を記録する。自己ベストを更新したら true。</summary>
        public static bool Submit(int turns)
        {
            int best = BestTurns;
            if (turns <= 0 || (best > 0 && turns >= best)) return false;
            PlayerPrefs.SetInt(Key, turns);
            PlayerPrefs.Save();
            return true;
        }
    }
}
