using System;

namespace SaiNoMichi.Battle
{
    /// <summary>敵の予告の種類（仕様書 第7章「インテントの種類」）。</summary>
    public enum IntentType
    {
        Attack,
        Block,
        Buff,         // 筋力を得る
        MultiAttack,  // value のダメージを hits 回（防御は合計に対して効く）
        Debuff,       // 弱体を value 与える
        Seal,         // 使用可能なダイスのうち、出目の平均が最も高いものを封印
        DiceRoll,     // 賽振り：敵が振った賽の出目による攻撃。minValue < maxValue なら値を隠して範囲だけ見せる
        ResetDice,    // 双六の番人「振り出しに戻れ」：プレイヤーの全ダイスを使用済みにする
    }

    [Serializable]
    public struct Intent
    {
        public IntentType type;
        public int value;
        public int hits;      // 多段攻撃の回数。0 以下は 1 回として扱う
        public int minValue;  // 賽振りの予告に出す範囲
        public int maxValue;

        public Intent(IntentType type, int value, int hits = 1)
        {
            this.type = type;
            this.value = value;
            this.hits = hits;
            minValue = 0;
            maxValue = 0;
        }

        public int Hits => hits > 0 ? hits : 1;

        /// <summary>プレイヤーの HP を削りにくる予告か。</summary>
        public bool IsAttack => type == IntentType.Attack || type == IntentType.MultiAttack || type == IntentType.DiceRoll;

        public override string ToString() => type == IntentType.MultiAttack ? $"{type} {value}x{Hits}" : $"{type} {value}";
    }
}
