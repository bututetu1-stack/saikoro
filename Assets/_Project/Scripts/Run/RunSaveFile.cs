using System;
using System.IO;

namespace SaiNoMichi.Run
{
    /// <summary>セーブファイルの読み書き（1ランぶんだけ。ランが終わったら消す）。</summary>
    public static class RunSaveFile
    {
        public const string FileName = "save.json";

        public static string PathIn(string folder) => System.IO.Path.Combine(folder, FileName);

        public static bool Exists(string folder) => File.Exists(PathIn(folder));

        /// <summary>書き込む。途中で止まっても壊れないよう、別のファイルに書いてから置き換える。</summary>
        public static void Write(string folder, RunSave save)
        {
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
            string path = PathIn(folder);
            if (!File.Exists(path)) return null;
            try
            {
                var save = RunSave.FromJson(File.ReadAllText(path));
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
            string path = PathIn(folder);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
