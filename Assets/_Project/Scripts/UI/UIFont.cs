using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 日本語を表示できる TMP フォント。プロジェクトに入れたフォント（Resources/SaiNoMichiFont。Zen角ゴシックNew）を使う。
    /// ブラウザ（WebGL）では OS のフォントが使えないため（前は OS のフォントから作っていて、unityroom で □ になった）。
    /// 見つからないときだけ OS のフォントから作る。
    /// </summary>
    public static class UIFont
    {
        const string ResourceName = "SaiNoMichiFont";
        static readonly string[] Candidates = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Noto Sans JP", "Hiragino Sans" };
        static TMP_FontAsset cached;

        public static TMP_FontAsset Japanese
        {
            get
            {
                if (cached != null) return cached;
                cached = Resources.Load<TMP_FontAsset>(ResourceName);
                if (cached != null) return cached;
                Debug.LogWarning("日本語フォント（Resources/SaiNoMichiFont）がありません。SaiNoMichi → Build Font を実行してください。OS のフォントを使います。");
                foreach (var family in Candidates)
                {
                    cached = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (cached != null) return cached;
                }
                cached = TMP_Settings.defaultFontAsset;
                return cached;
            }
        }
    }
}
