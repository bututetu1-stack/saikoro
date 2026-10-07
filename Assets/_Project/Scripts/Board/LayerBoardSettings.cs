using System;
using System.Collections.Generic;

namespace SaiNoMichi.Board
{
    [Serializable]
    public struct TileWeight
    {
        public TileType type;
        public int weight;

        public TileWeight(TileType type, int weight)
        {
            this.type = type;
            this.weight = weight;
        }
    }

    /// <summary>
    /// 分岐する盤面の設定（仕様書 第8章「盤面の形」「生成の手順」「配置ルール」）。既定値は第1層。
    /// </summary>
    [Serializable]
    public class LayerBoardSettings
    {
        [UnityEngine.Header("骨組み")]
        // スタートからボスまで、標準の道を通ったときの歩数
        public int minLength = 36;
        public int maxLength = 44;
        // 最初の分岐点（スタートから何マス目か）
        public int firstBranchMin = 4;
        public int firstBranchMax = 7;
        public int firstBranchPathsMin = 2;
        public int firstBranchPathsMax = 3;
        // 1つ目の分岐区間の、標準の道の長さ（分岐点と合流点は含まない）
        public int section1Min = 7;
        public int section1Max = 9;
        // 短い道は標準より何マス短く、長い道は何マス長いか
        public int shortPathDelta = 2;
        public int longPathDelta = 3;
        // 合流から2つ目の分岐点まで（2つ目の分岐点を含む）
        public int middleTrunkMin = 3;
        public int middleTrunkMax = 5;
        // 2つ目の分岐区間（上の道・下の道）の長さ。もう一方は section2Delta だけ長い
        public int section2Min = 5;
        public int section2Max = 7;
        public int section2Delta = 2;
        // 2つ目の合流からボスの手前まで（休憩かショップをボスの3〜6マス手前に置くため、7以上）
        public int finalTrunkMin = 7;

        [UnityEngine.Header("固定マス")]
        public int restOrShopBeforeBossMin = 3;
        public int restOrShopBeforeBossMax = 6;

        [UnityEngine.Header("出現率（第1層）")]
        public List<TileWeight> weights = new List<TileWeight>
        {
            new TileWeight(TileType.Empty, 25),
            new TileWeight(TileType.Battle, 25),
            new TileWeight(TileType.Event, 15),
            new TileWeight(TileType.Trap, 6),
            new TileWeight(TileType.Rest, 9),
            new TileWeight(TileType.Treasure, 7),
            new TileWeight(TileType.Shop, 6),
            new TileWeight(TileType.Forge, 5),
            new TileWeight(TileType.Elite, 2),
        };
        // 短い道は戦闘・罠が多く、長い道はショップ・宝箱が多い（重みを percent% にする）
        public int pathBiasPercent = 200;

        [UnityEngine.Header("配置ルール")]
        public int safeStartTiles = 5;       // 最初の5マスは戦闘1つまで、エリートと罠なし
        public int maxBattlesAtStart = 1;
        public int maxSameRun = 2;           // 同じ種類は2つまで連続
        public int maxEmptyRun = 3;          // 空白は3つまで
        public int minShopDistance = 6;      // ショップ同士は6マス以上離す

        [UnityEngine.Header("検証")]
        public int validationRuns = 400;
        // 普通の賽だけで進んで、ショップか休憩に1回も止まれない割合の上限
        public float maxMissShopRestRate = 0.3f;
        public int maxAttempts = 300;
        public int maxRedrawPasses = 60;
    }
}
