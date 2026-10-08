using unityroom.Api;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// unityroom のランキング（踏破にかかったターン数。少ないほど上）。
    /// 秘密の鍵（HMAC キー）は Resources/Secrets/unityroom_hmac.txt に置く。公開リポジトリなので git には入れない（.gitignore）。
    /// 鍵がないとき・エディタでは送らない。
    /// </summary>
    public static class Ranking
    {
        // TODO(仕様): unityroom のスコアボード番号。ランキングを1つだけ作るなら 1
        const int BoardNo = 1;
        const string KeyPath = "Secrets/unityroom_hmac";

        static bool initialized;
        static bool available;

        /// <summary>ランキングに送れるか（鍵があって、ブラウザで動いている）。</summary>
        public static bool Available
        {
            get
            {
                Initialize();
                return available;
            }
        }

        /// <summary>踏破したターン数を送る。送ったら true。</summary>
        public static bool SubmitClearTurns(int turns)
        {
            if (turns <= 0 || !Available) return false;
            UnityroomApiClient.Instance.SendScore(BoardNo, turns, ScoreboardWriteMode.HighScoreAsc);
            return true;
        }

        static void Initialize()
        {
            if (initialized) return;
            initialized = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            var keyAsset = Resources.Load<TextAsset>(KeyPath);
            string key = keyAsset != null ? keyAsset.text.Trim() : null;
            if (string.IsNullOrEmpty(key)) return;

            // ライブラリはシーンに置いたプレハブで使う作りだが、シーンは変えずに、ここで作って鍵を入れる
            // （鍵を入れてから有効にする。先に有効にすると「鍵がない」とエラーになる）
            var go = new GameObject("UnityroomApiClient");
            go.SetActive(false);
            var client = go.AddComponent<UnityroomApiClient>();
            var field = typeof(UnityroomApiClient).GetField("HmacKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) return;
            field.SetValue(client, key);
            go.SetActive(true);
            available = true;
#endif
        }
    }
}
