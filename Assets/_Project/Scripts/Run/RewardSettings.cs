using System;

namespace SaiNoMichi.Run
{
    public enum RewardKind
    {
        Normal,
        Elite,
        Boss,
    }

    /// <summary>戦闘報酬の数値（仕様書 第11章「報酬テーブル」）。</summary>
    [Serializable]
    public class RewardSettings
    {
        public int diceChoiceCount = 3;
        public int skipGold = 10;

        public int normalGoldMin = 12;
        public int normalGoldMax = 18;
        // コモン・アンコモン・レアの重み
        public int[] normalRarityWeights = { 70, 25, 5 };
        public int normalCharmPercent = 40;   // 通常戦で40%でお守り

        public int eliteGoldMin = 30;
        public int eliteGoldMax = 40;
        public int[] eliteRarityWeights = { 40, 45, 15 };

        public int bossGold = 60;
        public int[] bossRarityWeights = { 0, 0, 100 };

        // レリック・刻印もレア度で出やすさを変える（コモン・アンコモン・レア）。開発者の判断：均等だとレアが出すぎる
        // TODO(仕様): 重みは仮（STS に近い 60・30・10）
        public int[] relicRarityWeights = { 60, 30, 10 };
        public int[] engravingRarityWeights = { 60, 30, 10 };
    }
}
