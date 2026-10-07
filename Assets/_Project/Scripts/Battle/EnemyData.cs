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
        Random,     // 毎ラウンド pattern から1つを選ぶ
        Banjin,     // 双六の番人：賽振り（偶数→出目×2の攻撃、奇数→出目×2の防御）と、resetEvery ラウンドごとの「振り出しに戻れ」
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

        [Header("双六の番人")]
        public int diceSides = 6;
        public int resetEvery = 4;
    }
}
