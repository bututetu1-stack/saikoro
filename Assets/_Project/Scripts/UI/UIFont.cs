using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 日本語を表示できる TMP フォント。OS のフォントから動的に作る。
    /// TODO(仕様): 配布用には、ライセンスを確認したフォントをプロジェクトに入れて差し替える（フェーズ4）
    /// </summary>
    public static class UIFont
    {
        static readonly string[] Candidates = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Noto Sans JP", "Hiragino Sans" };
        static TMP_FontAsset cached;

        public static TMP_FontAsset Japanese
        {
            get
            {
                if (cached != null) return cached;
                foreach (var family in Candidates)
                {
                    cached = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (cached != null) return cached;
                }
                Debug.LogWarning("日本語フォントが見つかりませんでした。TMP の既定フォントを使います。");
                cached = TMP_Settings.defaultFontAsset;
                return cached;
            }
        }
    }
}
