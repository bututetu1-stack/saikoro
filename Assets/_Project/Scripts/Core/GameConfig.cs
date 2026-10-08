using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Core
{
    /// <summary>ゲーム全体で使う数値とデータ（ダイス・敵・盤面・報酬など）。Build All Data で作る。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public int playerMaxHp = 40;
        public int startingGold = 50;
        [Tooltip("休憩マスで最大HPの何%を回復するか（切り捨て）")]
        public int restHealPercent = 30;
        public List<DiceData> startingDice = new List<DiceData>();
        [Tooltip("開始時に1つ選ぶスターターダイス（仕様書 第3章：一二三賽・盾賽・博打賽）")]
        public List<DiceData> starterChoices = new List<DiceData>();
        [Tooltip("戦闘マスではこの中から選ぶ（同じ敵は2戦続けない）")]
        public List<EnemyData> battleEnemies = new List<EnemyData>();
        [Tooltip("最初の何戦を「弱めの敵（earlyOk）」だけにするか")]
        public int earlyBattleCount = 3;
        public List<EnemyData> eliteEnemies = new List<EnemyData>();
        public EnemyData boss;
        [Header("層（フェーズ2）")]
        [Tooltip("層ごとの名前・盤面・敵。空なら上の battleEnemies などと layerBoard で1層だけのランになる")]
        public List<LayerData> layers = new List<LayerData>();
        [Tooltip("層をクリアしたとき、最大HPの何%を回復するか（仕様書 第2章）")]
        public int layerClearHealPercent = 30;
        [Tooltip("前の層の敵が出る確率（%）。出たときは HP が previousLayerEnemyHpPercent% になる（仕様書 第7章）")]
        public int previousLayerEnemyPercent = 10;
        public int previousLayerEnemyHpPercent = 150;

        [Header("盤面")]
        [Tooltip("オンなら分岐する盤面（フェーズ1）、オフならフェーズ0の直線20マス")]
        public bool useBranchingBoard;
        public LayerBoardSettings layerBoard = new LayerBoardSettings();
        public LinearBoardSettings board = new LinearBoardSettings();

        [Header("マスの中身")]
        public TileSettings tiles = new TileSettings();
        [Tooltip("罠で押し付けられる呪いのダイス（curseDicePool が空のときに使う）")]
        public DiceData curseDice;
        [Tooltip("罠で押し付けられる呪いのダイスの候補（欠け賽・錆び賽）")]
        public List<DiceData> curseDicePool = new List<DiceData>();
        [Tooltip("宝箱・エリート・ショップで出てくるレリック（ステップ9で入れる）")]
        public List<SaiNoMichi.Effects.RelicData> relicPool = new List<SaiNoMichi.Effects.RelicData>();

        [Tooltip("鍛冶で提示される刻印")]
        public List<SaiNoMichi.Effects.EngravingData> engravingPool = new List<SaiNoMichi.Effects.EngravingData>();

        [Header("報酬")]
        public RewardSettings rewards = new RewardSettings();
        public ShopSettings shop = new ShopSettings();
        public EventSettings events = new EventSettings();
        [Tooltip("報酬・ショップに出てくるダイス")]
        public List<DiceData> rewardDicePool = new List<DiceData>();
    }
}
