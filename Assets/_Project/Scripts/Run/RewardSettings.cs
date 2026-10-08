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

        // レリック・刻印もレア度で出やすさを変える（コモン・アンコモン・レア）。層が進むほどレアが出やすい
        // 開発者の判断：均等だとレアが出すぎた。第1層でレアが重なるのはおかしいので、層ごとに変える
        // TODO(仕様): 重みは仮
        public RarityByLayer relicRarity = new RarityByLayer();
        public RarityByLayer engravingRarity = new RarityByLayer();
    }

    /// <summary>レア度の重み（コモン・アンコモン・レア）を層ごとに。</summary>
    [Serializable]
    public class RarityByLayer
    {
        public int[] layer1 = { 70, 25, 5 };
        public int[] layer2 = { 60, 30, 10 };
        public int[] layer3 = { 45, 35, 20 };

        public int[] For(int layerIndex) => layerIndex <= 0 ? layer1 : layerIndex == 1 ? layer2 : layer3;
    }
}
