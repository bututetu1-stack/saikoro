using System;
using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;

namespace SaiNoMichi.Core
{
    /// <summary>1層ぶんの中身（仕様書 第2章・第7章・第8章）。名前・盤面の作り方・敵の顔ぶれ。</summary>
    [Serializable]
    public class LayerData
    {
        public string displayName = "野原の街道";
        public LayerBoardSettings board = new LayerBoardSettings();
        public List<EnemyData> battleEnemies = new List<EnemyData>();
        public List<EnemyData> eliteEnemies = new List<EnemyData>();
        public EnemyData boss;
        // 最初の何戦を「弱めの敵（earlyOk）」だけにするか
        public int earlyBattleCount = 3;
    }
}
