using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>段位の解放と、前に選んだ段位（この端末・ブラウザに保存。WebGL は PlayerPrefs）。</summary>
    public static class AscensionRecord
    {
        const string UnlockedKey = "SaiNoMichi.AscensionUnlocked";
        const string LastKey = "SaiNoMichi.AscensionLast";

        /// <summary>選べるいちばん上の段位（はじめは 0。クリアするたびに1つ上が解放される）。</summary>
        public static int Unlocked => Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 0), 0, AscensionRules.Max);

        /// <summary>前に選んだ段位（解放されている範囲で）。</summary>
        public static int Last
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(LastKey, 0), 0, Unlocked);
            set
            {
                PlayerPrefs.SetInt(LastKey, Mathf.Clamp(value, 0, AscensionRules.Max));
                PlayerPrefs.Save();
            }
        }

        /// <summary>段位 level でクリアした：まだなら次の段位を解放する。解放した段位（なければ −1）を返す。</summary>
        public static int OnCleared(int level)
        {
            if (level < Unlocked || Unlocked >= AscensionRules.Max) return -1;
            int next = Mathf.Min(AscensionRules.Max, level + 1);
            PlayerPrefs.SetInt(UnlockedKey, next);
            PlayerPrefs.Save();
            return next;
        }
    }
}
