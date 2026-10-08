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

        [Tooltip("一度に何体で出るか（双子鬼は2）。同じ敵が並ぶ")]
        public int count = 1;
        [Tooltip("仲間が倒れたとき、残った自分が得る筋力（双子鬼は3）")]
        public int allyDefeatedStrength;

        [Tooltip("各層の最初の数戦に出してよい「弱めの敵」か")]
        public bool earlyOk = true;

        [Header("特性（フェーズ2）")]
        [Tooltip("1ラウンドに受けるダメージの上限（0 なら上限なし）。石の守護者など")]
        public int damageCapPerRound;
        [Tooltip("HP がこの割合（%）以下のとき、攻撃の予告が enrageAttackPercent% になる（0 なら無効）。首狩りなど")]
        public int enrageHpPercent;
        public int enrageAttackPercent = 200;
    }
}
