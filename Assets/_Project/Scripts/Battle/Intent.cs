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
        Debuff,       // 脱力を value 与える（与えるダメージが75%）
        Seal,         // 使用可能なダイスからランダムに1個を封印（前の封印は解放）
        DiceRoll,     // 賽振り：敵が振った賽の出目による攻撃。minValue < maxValue なら値を隠して範囲だけ見せる
        ResetDice,    // 双六の番人「振り出しに戻れ」：使用可能なダイスのうち強い順に半分を使用済みにする（リフレッシュは起こさない）
        // フェーズ2で追加（保存済みのデータの番号がずれないよう末尾に）
        Poison,       // 毒を value 与える
        Charge,       // 溜め：何もしない。value > 0 なら、このラウンドに value 以上のダメージを受けると怯んで次の大攻撃が止まる
        Stunned,      // 怯み：何もしない（溜めを止められた）
        Curse,        // 呪い：呪いのダイス（欠け賽）をポーチに押し付ける
        MirrorAttack, // 写し鏡：前のラウンドにプレイヤーが出した攻撃値で攻撃（value は予告のときに決まる）
        Vulnerable,   // 弱体を value 与える（受けるダメージが150%）
        Bind,         // 縛り：次のラウンド、振れるダイスが1個になる
        RollAttack,   // 予告を出すときに maxValue 面のダイスを振り、出目×value の攻撃になる（予告では値が見える。八面）
        RollBlock,    // 同じく、出目×value の防御になる
        RewriteFate,  // 運命の書き換え：プレイヤーの最も強いダイスの最大の面を、戦闘中だけ1にする（八面）
        Frail,        // 脆弱を value 与える（作れる防御が75%になる）
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
        public bool IsAttack => type == IntentType.Attack || type == IntentType.MultiAttack || type == IntentType.DiceRoll || type == IntentType.MirrorAttack;

        public override string ToString() => type == IntentType.MultiAttack ? $"{type} {value}x{Hits}" : $"{type} {value}";
    }
}
