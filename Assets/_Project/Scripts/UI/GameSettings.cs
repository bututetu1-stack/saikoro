using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 設定（効果音・BGM の音量、演出の速さ、初回の案内）。この端末・ブラウザに保存する（WebGL は PlayerPrefs）。
    /// </summary>
    public static class GameSettings
    {
        const string VolumeKey = "SaiNoMichi.SeVolume";
        const string SpeedKey = "SaiNoMichi.AnimSpeed";
        const string BgmKey = "SaiNoMichi.BgmVolume";
        const string HintPrefix = "SaiNoMichi.HintSeen.";

        /// <summary>演出の速さの段階（表示名と倍率）。</summary>
        public static readonly (string label, float speed)[] Speeds = { ("普通", 1f), ("速い", 1.6f), ("とても速い", 2.5f) };

        /// <summary>効果音の音量（0〜100、10刻み）。</summary>
        public static int SeVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VolumeKey, 80), 0, 100);
            set
            {
                PlayerPrefs.SetInt(VolumeKey, Mathf.Clamp(value, 0, 100));
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>BGM の音量（0〜100、10刻み）。</summary>
        public static int BgmVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(BgmKey, 60), 0, 100);
            set
            {
                PlayerPrefs.SetInt(BgmKey, Mathf.Clamp(value, 0, 100));
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>演出の速さ（Speeds の番号）。</summary>
        public static int SpeedIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 0), 0, Speeds.Length - 1);
            set
            {
                PlayerPrefs.SetInt(SpeedKey, Mathf.Clamp(value, 0, Speeds.Length - 1));
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>保存してある設定を、効果音と演出に反映する（起動時と変更時）。</summary>
        public static void Apply()
        {
            Sfx.MasterVolume = SeVolume / 100f;
            UIAnim.Speed = Speeds[SpeedIndex].speed;
            Bgm.SetVolume(BgmVolume / 100f);
        }

        // ---- 初回だけの案内 ----

        public static bool HintSeen(string id) => PlayerPrefs.GetInt(HintPrefix + id, 0) != 0;

        public static void MarkHintSeen(string id)
        {
            PlayerPrefs.SetInt(HintPrefix + id, 1);
            PlayerPrefs.Save();
        }

        /// <summary>案内をもう一度出すようにする（設定の「案内をもう一度見る」）。</summary>
        public static void ResetHints(params string[] ids)
        {
            foreach (var id in ids) PlayerPrefs.DeleteKey(HintPrefix + id);
            PlayerPrefs.Save();
        }
    }
}
