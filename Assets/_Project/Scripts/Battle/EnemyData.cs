using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.Battle
{
    public enum EnemyKind
    {
        Normal,
        Elite,
        Boss,
    }

    public enum EnemyBehavior
    {
        Sequence,   // pattern を先頭から順に繰り返す
        Random,     // 毎ラウンド pattern から1つを選ぶ（敵の行動はなるべく読めるようにする方針なので、使うときは慎重に）
    }

    /// <summary>敵の定義データ（仕様書 第7章）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Enemy Data", fileName = "Enemy_")]
    public class EnemyData : ScriptableObject
    {
        public string id;
        public string displayName;
        public EnemyKind kind;
        public int maxHp = 10;
        public EnemyBehavior behavior;
        public List<Intent> pattern = new List<Intent>();

        [Tooltip("各層の最初の数戦に出してよい「弱めの敵」か")]
        public bool earlyOk = true;
    }
}
