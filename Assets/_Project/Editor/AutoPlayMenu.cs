using System.IO;
using SaiNoMichi.Core;
using SaiNoMichi.Run;
using UnityEditor;
using UnityEngine;

namespace SaiNoMichi.EditorTools
{
    /// <summary>自動プレイを何百ランも回して、結果を CSV に書き出すメニュー（フェーズ2 手順12）。</summary>
    public static class AutoPlayMenu
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        /// <summary>結果を書くフォルダ（プロジェクトの外の AutoPlay フォルダ。Git には入れない）。</summary>
        static string OutputFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AutoPlay");

        [MenuItem("SaiNoMichi/Autoplay/Run 100")]
        static void Run100() => Run(100);

        [MenuItem("SaiNoMichi/Autoplay/Run 500")]
        static void Run500() => Run(500);

        static void Run(int count)
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("[賽ノ道] GameConfig がありません。");
                return;
            }
            try
            {
                var runs = AutoPlayReport.RunMany(config, 1, count,
                    i => EditorUtility.DisplayProgressBar("自動プレイ", $"{i} / {count} ラン", i / (float)count));
                AutoPlayReport.WriteCsv(OutputFolder, runs);
                Debug.Log($"[賽ノ道] 自動プレイ {count} ラン（seed 1〜{count}）\n{AutoPlayReport.Summary(runs)}\nCSV: {OutputFolder}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
