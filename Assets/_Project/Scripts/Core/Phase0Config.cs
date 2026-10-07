using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
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
        [Tooltip("戦闘マスではこの中から等確率で選ぶ")]
        public List<EnemyData> battleEnemies = new List<EnemyData>();
        public EnemyData boss;
        public LinearBoardSettings board = new LinearBoardSettings();
    }
}
