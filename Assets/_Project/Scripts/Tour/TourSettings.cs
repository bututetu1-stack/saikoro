using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.Tour
{
    /// <summary>国めぐりの勝ち方。</summary>
    public enum TourGoal
    {
        Assets,     // 決まったターンのあと、資産（ゴールド＋名物の値段）がいちばん多い人
        Arrivals,   // 決まったターンのあと、目的地にいちばん多く一番乗りした人（同じなら資産）
        FinalBoss,  // 決まったターンのあと、大ボスにいちばん多くダメージを与えた人
    }

    /// <summary>名物の候補（名前・値段・決算の収入の割合）。</summary>
    [Serializable]
    public class TourPropertyTemplate
    {
        public string name;
        public int price;
        public int incomePercent;

        public TourPropertyTemplate(string name, int price, int incomePercent)
        {
            this.name = name;
            this.price = price;
            this.incomePercent = incomePercent;
        }
    }

    /// <summary>
    /// 国めぐり（桃鉄風の巨大マップのモード）の数値。アセットがなければ、この既定の値で遊べる。
    /// TODO(仕様): 数値はすべて仮。遊んで調整する
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Tour Settings", fileName = "TourSettings")]
    public class TourSettings : ScriptableObject
    {
        [Header("盤面")]
        public int townColumns = 6;
        public int townRows = 4;
        public float townSpacingX = 6f;
        public float townSpacingY = 5f;
        [Range(0, 100)] public int removeEdgePercent = 22;
        [Range(0, 100)] public int diagonalPercent = 12;
        [Range(0, 100)] public int diceShopPercent = 30;

        [Header("道のマスの割合（合計で割る）")]
        public int blueWeight = 40;
        public int redWeight = 20;
        public int yellowWeight = 15;
        public int battleWeight = 13;
        public int shrineWeight = 6;
        public int eventWeight = 6;

        [Header("お金")]
        public int startGold = 300;
        [Tooltip("後の番の人ほど、はじめのゴールドを多く（番ごとに）")]
        public int laterSeatGold = 30;
        public int blueMin = 30, blueMax = 60;
        public int redMin = 20, redMax = 50;
        [Tooltip("ターンが進むと、青・赤の額が増える（ターンごとの%）")]
        public int moneyGrowthPercentPerRound = 4;
        public int arrivalBonusBase = 150;
        public int arrivalBonusPerStep = 10;
        [Tooltip("決算（名物の収入）のターンの間隔")]
        public int settlementEvery = 5;

        [Header("戦闘・対決")]
        public int battleGoldMin = 30, battleGoldMax = 60;
        [Range(0, 100)] public int battleDiceChance = 30;
        [Range(0, 100)] public int defeatGoldLossPercent = 25;
        [Range(0, 100)] public int defeatHpPercent = 50;
        [Range(0, 100)] public int duelStealPercent = 20;
        [Tooltip("大ボス（勝ち方：大ボス）の HP の倍率")]
        public int finalBossHpMultiplier = 8;
        public int finalBossRounds = 5;

        [Header("札・祠・厄神")]
        public int maxCards = 5;
        [Range(0, 100)] public int shrineHealPercent = 30;
        [Range(0, 100)] public int shrineCardChance = 50;
        [Range(0, 100)] public int yakugamiGoldLossMin = 10, yakugamiGoldLossMax = 25;
        public int diceShopCommon = 60, diceShopUncommon = 120, diceShopRare = 200;

        [Header("町と名物")]
        public List<string> townNames = new List<string>
        {
            "品川", "小田原", "箱根", "三島", "駿府", "掛川", "浜松", "岡崎", "宮", "桑名", "亀山", "草津",
            "大津", "京", "奈良", "堺", "伊勢", "甲府", "諏訪", "松本", "善光寺", "高崎", "日光", "会津",
            "仙台", "金沢", "福井", "姫路", "岡山", "尾道",
        };

        public List<TourPropertyTemplate> propertyPool = new List<TourPropertyTemplate>
        {
            new TourPropertyTemplate("団子屋", 50, 30), new TourPropertyTemplate("茶屋", 60, 25), new TourPropertyTemplate("蕎麦屋", 70, 25),
            new TourPropertyTemplate("饅頭屋", 80, 20), new TourPropertyTemplate("宿屋", 120, 20), new TourPropertyTemplate("鰻屋", 150, 20),
            new TourPropertyTemplate("酒蔵", 200, 15), new TourPropertyTemplate("呉服屋", 250, 15), new TourPropertyTemplate("薬種問屋", 300, 12),
            new TourPropertyTemplate("刀鍛冶", 350, 12), new TourPropertyTemplate("瀬戸物屋", 180, 15), new TourPropertyTemplate("温泉宿", 400, 10),
            new TourPropertyTemplate("漁港", 220, 18), new TourPropertyTemplate("塩田", 160, 18), new TourPropertyTemplate("金山", 600, 8),
            new TourPropertyTemplate("五重塔", 500, 6), new TourPropertyTemplate("芝居小屋", 280, 14), new TourPropertyTemplate("両替商", 450, 10),
            new TourPropertyTemplate("植木屋", 90, 22), new TourPropertyTemplate("豆腐屋", 55, 28),
        };
        public int propertiesPerTown = 3;

        static TourSettings fallback;

        /// <summary>アセットがなければ、既定の値のもの。</summary>
        public static TourSettings OrDefault(TourSettings settings)
        {
            if (settings != null) return settings;
            if (fallback == null) fallback = CreateInstance<TourSettings>();
            return fallback;
        }
    }
}
