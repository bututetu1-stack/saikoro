using System.Linq;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>旅人の解放（仕様書 第12章）と、前に選んだ旅人。この端末・ブラウザに保存する（WebGL は PlayerPrefs）。</summary>
    public static class TravelerRecord
    {
        const string LayerKey = "SaiNoMichi.MaxLayerReached";
        const string EngravingKey = "SaiNoMichi.TotalEngravings";
        const string LastKey = "SaiNoMichi.TravelerLast";

        /// <summary>着いたことのあるいちばん深い層（1〜。前の版で遊んだ分は、ラン履歴からも見る）。</summary>
        public static int MaxLayer
        {
            get
            {
                int fromHistory = RunHistory.Entries.Count > 0 ? RunHistory.Entries.Max(e => e.layer) : 1;
                return Mathf.Max(PlayerPrefs.GetInt(LayerKey, 1), fromHistory, BestRecord.BestTurns > 0 ? 3 : 1);
            }
        }

        /// <summary>これまでに付けた刻印の数（すべての旅の合計）。</summary>
        public static int TotalEngravings => PlayerPrefs.GetInt(EngravingKey, 0);

        public static void NoteLayer(int layer)
        {
            if (layer <= PlayerPrefs.GetInt(LayerKey, 1)) return;
            PlayerPrefs.SetInt(LayerKey, layer);
            PlayerPrefs.Save();
        }

        public static void AddEngraving()
        {
            PlayerPrefs.SetInt(EngravingKey, TotalEngravings + 1);
            PlayerPrefs.Save();
        }

        public static bool IsUnlocked(TravelerKind k)
        {
            switch (k)
            {
                case TravelerKind.Bakuto: return MaxLayer >= Travelers.BakutoUnlockLayer;
                case TravelerKind.Saikushi: return TotalEngravings >= Travelers.SaikushiUnlockEngravings;
                case TravelerKind.Yamabushi: return BestRecord.BestTurns > 0;
                default: return true;
            }
        }

        /// <summary>前に選んだ旅人（解放されていなければ賽振り）。</summary>
        public static TravelerKind Last
        {
            get
            {
                var k = (TravelerKind)PlayerPrefs.GetInt(LastKey, 0);
                return System.Enum.IsDefined(typeof(TravelerKind), k) && IsUnlocked(k) ? k : TravelerKind.Saifuri;
            }
            set
            {
                PlayerPrefs.SetInt(LastKey, (int)value);
                PlayerPrefs.Save();
            }
        }
    }
}
