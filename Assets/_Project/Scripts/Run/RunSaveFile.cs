using System;
using System.IO;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// セーブの読み書き（1ランぶんだけ。ランが終わったら消す）。
    /// ふつうはファイル（folder/save.json）。WebGL（unityroom など）ではファイルが残らないことがあるので、PlayerPrefs に書く。
    /// </summary>
    public static class RunSaveFile
    {
        public const string FileName = "save.json";
        const string PrefsKey = "SaiNoMichi.Save";

#if UNITY_WEBGL && !UNITY_EDITOR
        static readonly bool UsePrefs = true;
#else
        static readonly bool UsePrefs = false;
#endif

        public static string PathIn(string folder) => System.IO.Path.Combine(folder, FileName);

        public static bool Exists(string folder) =>
            UsePrefs ? UnityEngine.PlayerPrefs.HasKey(PrefsKey) : File.Exists(PathIn(folder));

        /// <summary>書き込む。ファイルのときは、途中で止まっても壊れないよう、別のファイルに書いてから置き換える。</summary>
        public static void Write(string folder, RunSave save)
        {
            if (UsePrefs)
            {
                UnityEngine.PlayerPrefs.SetString(PrefsKey, save.ToJson());
                UnityEngine.PlayerPrefs.Save(); // WebGL ではここでブラウザの保存領域に書かれる
                return;
            }
            Directory.CreateDirectory(folder);
            string path = PathIn(folder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, save.ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>読む。ないとき・読めないときは null（error に理由）。</summary>
        public static RunSave Read(string folder, out string error)
        {
            error = null;
            if (!Exists(folder)) return null;
            try
            {
                string json = UsePrefs ? UnityEngine.PlayerPrefs.GetString(PrefsKey) : File.ReadAllText(PathIn(folder));
                var save = RunSave.FromJson(json);
                if (save == null) error = "セーブが空です。";
                return save;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        public static void Delete(string folder)
        {
            if (UsePrefs)
            {
                UnityEngine.PlayerPrefs.DeleteKey(PrefsKey);
                UnityEngine.PlayerPrefs.Save();
                return;
            }
            string path = PathIn(folder);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
