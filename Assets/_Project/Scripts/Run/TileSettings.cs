using System;

namespace SaiNoMichi.Run
{
    /// <summary>マスの中身の数値（仕様書 第8章・第11章）。</summary>
    [Serializable]
    public class TileSettings
    {
        [UnityEngine.Header("罠（ダメージ・封印・呪いのどれか。ランダム）")]
        // TODO(仕様): 罠のダメージ量は仕様書にない。関所と同じ 5 にする
        public int trapDamage = 5;

        [UnityEngine.Header("宝箱")]
        public int treasureGoldMin = 30;
        public int treasureGoldMax = 50;
        [UnityEngine.Tooltip("レリックが出る確率（%）。レリックがまだなければゴールドになる")]
        public int treasureRelicPercent = 50;
        [UnityEngine.Tooltip("アンコモン以上のダイスが付いてくる確率（%）")]
        public int treasureDicePercent = 10;

        [UnityEngine.Header("通過マス（止まると2倍）")]
        public int shrineGold = 5;
        public int checkpointToll = 10;
        public int checkpointDamage = 5;
        public int teahouseHeal = 3;
        public int diceHallReturn = 1;
    }
}
