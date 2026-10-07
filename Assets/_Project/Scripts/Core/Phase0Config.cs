using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.Core
{
    /// <summary>フェーズ0のプロトタイプで使う数値とデータ（Docs/tasks/phase0-prototype.md）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Phase0 Config", fileName = "Phase0Config")]
    public class Phase0Config : ScriptableObject
    {
        public int playerMaxHp = 40;
        public int startingGold = 50;
        [Tooltip("休憩マスで最大HPの何%を回復するか（切り捨て）")]
        public int restHealPercent = 30;
        public List<DiceData> startingDice = new List<DiceData>();
        [Tooltip("開始時に1つ選ぶスターターダイス（仕様書 第3章：一二三賽・盾賽・博打賽）")]
        public List<DiceData> starterChoices = new List<DiceData>();
        [Tooltip("戦闘マスではこの中から等確率で選ぶ")]
        public List<EnemyData> battleEnemies = new List<EnemyData>();
        public EnemyData boss;
        public LinearBoardSettings board = new LinearBoardSettings();

        [Header("報酬")]
        public RewardSettings rewards = new RewardSettings();
        [Tooltip("報酬・ショップに出てくるダイス")]
        public List<DiceData> rewardDicePool = new List<DiceData>();
    }
}
