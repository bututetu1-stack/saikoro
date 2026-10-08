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
        // 開発者の判断：戦闘を増やすため、約2割長く（36〜44 → 44〜52）
        public int minLength = 44;
        public int maxLength = 52;
        // 最初の分岐点（スタートから何マス目か）
        public int firstBranchMin = 4;
        public int firstBranchMax = 6;
        // 開発者の判断：ルートを選ぶ場面を増やすため、分岐を3回・基本は3本道にする
        // 1つ目の分岐区間（短い・標準・長いの3本）の、標準の道の長さ（分岐点と合流点は含まない）
        public int section1Min = 7;
        public int section1Max = 8;
        // 短い道は標準より何マス短く、長い道は何マス長いか
        public int shortPathDelta = 2;
        public int longPathDelta = 3;
        // 合流から2つ目の分岐点まで（2つ目の分岐点を含む。鍛冶はここに置く）
        public int middleTrunkMin = 2;
        public int middleTrunkMax = 3;
        // 2つ目の分岐区間（短い・標準・長いの3本）の標準の道の長さ
        public int section2Min = 6;
        public int section2Max = 7;
        // 合流から3つ目の分岐点まで（3つ目の分岐点を含む）
        public int middleTrunk2Min = 2;
        public int middleTrunk2Max = 3;
        // 3つ目の分岐区間（上の道・下の道）の長さ。もう一方は section3Delta だけ長い
        public int section3Min = 5;
        public int section3Max = 6;
        public int section3Delta = 2;
        // 3つ目の合流からボスの手前まで（休憩かショップをボスの3〜6マス手前に置くため、7以上）
        public int finalTrunkMin = 7;
        // 並んだ道どうしをつなぐ横道（隣り合う道の組ごとの本数）。途中で道を乗り換えられる
        public int crossLinksMin = 1;
        public int crossLinksMax = 2;
        // 道の中の小さな分かれ道（1マスぶん2つに分かれてすぐ合流する）。外側の道に、この確率（%）で1つ
        public int diamondPercent = 60;
        public int diamondMinLaneLength = 5;

        [UnityEngine.Header("固定マス")]
        public int restOrShopBeforeBossMin = 3;
        public int restOrShopBeforeBossMax = 6;

        [UnityEngine.Header("出現率（第1層）")]
        public List<TileWeight> weights = new List<TileWeight>
        {
            // 空白マスは置かない（何も起きないマスは退屈なため）。空白の25%を他へ振り分けた
            // TODO(仕様): 振り分けは仮。プレイして調整する
            // 開発者の判断：戦闘が少なかったので、戦闘を増やしてイベント・鍛冶を減らした
            new TileWeight(TileType.Battle, 38),
            new TileWeight(TileType.Event, 18),
            new TileWeight(TileType.Trap, 7),
            new TileWeight(TileType.Rest, 10),
            new TileWeight(TileType.Treasure, 12),
            new TileWeight(TileType.Shop, 7),
            new TileWeight(TileType.Forge, 5),
            new TileWeight(TileType.Elite, 3),
        };
        // 短い道は戦闘・罠が多く、長い道はショップ・宝箱が多い（重みを percent% にする）
        public int pathBiasPercent = 200;

        [UnityEngine.Header("通過マス（祠・関所・茶屋・賽場）")]
        public int passTilesMin = 2;
        public int passTilesMax = 3;

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
