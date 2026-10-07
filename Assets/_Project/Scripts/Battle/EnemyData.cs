using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.Battle
{
    /// <summary>敵の定義データ。pattern の予告を先頭から順に繰り返す。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Enemy Data", fileName = "Enemy_")]
    public class EnemyData : ScriptableObject
    {
        public string id;
        public string displayName;
        public int maxHp = 10;
        public List<Intent> pattern = new List<Intent>();
    }
}
