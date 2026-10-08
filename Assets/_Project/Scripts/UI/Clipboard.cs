using System.Runtime.InteropServices;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 文字をクリップボードにコピーする。WebGL では GUIUtility.systemCopyBuffer がブラウザに届かないので、
    /// Plugins/WebGL/Clipboard.jslib でブラウザのクリップボードに書く。
    /// </summary>
    public static class Clipboard
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern int SaiNoMichi_CopyToClipboard(string text);
#endif

        /// <summary>コピーできたら true（WebGL では、ブラウザが許可しないと false）。</summary>
        public static bool Copy(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return SaiNoMichi_CopyToClipboard(text) != 0; }
            catch { return false; }
#else
            GUIUtility.systemCopyBuffer = text;
            return true;
#endif
        }
    }
}
