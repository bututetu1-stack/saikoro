using UnityEngine;

namespace SaiNoMichi.Run
{
    /// <summary>お守りの効果の種類（仕様書 第10章）。</summary>
    public enum CharmKind
    {
        RerollDie,    // 振り直し御札：振ったダイス1個を振り直す
        MoveForward,  // 進み御札：移動の出目+amount
        MoveBack,     // 止まり御札：移動の出目−amount（最低1）
        Heal,         // 傷薬：HP を amount 回復
        Smoke,        // 煙玉：通常戦から逃げる（報酬なし）
        Unseal,       // 押し入れの鍵：封印されたダイスをすべて解除
        ReturnUsed,   // 残り福：使用済みのダイスをすべて戻す
        WeakenEnemy,     // 脱力の薬：狙っている敵に脱力 amount
        VulnerableEnemy, // 弱体の薬：狙っている敵に弱体 amount
        PoisonEnemy,     // 毒の薬：狙っている敵に毒 amount
        GainStrength,    // 力の札：この戦闘の間、筋力+amount
        GainBlock,       // 守りの札：防御+amount（このラウンド）
        ThunderAll,      // 雷符：全部の敵に amount ダメージ（とどめは刺せない）
        GainGold,        // 千両箱：ゴールドを amount 得る（いつでも）
        HealParty,       // 分け合いの薬（みんなで遊ぶとき専用）：自分と仲間全員の HP を amount 回復（戦闘中）
        BlockParty,      // 結びの守り（みんなで遊ぶとき専用）：自分と仲間全員に防御+amount（このラウンド）
        // 2026-10-16 追加
        FrailEnemy,      // 脆弱の薬：狙っている敵に脆弱 amount
        MaxHp,           // 長寿の丸薬：最大HP+amount（いつでも）
        Fortify,         // 堅守の札：堅守 amount（戦闘中）
        Cleanse,         // 清めの塩：自分の脱力・弱体・脆弱・毒・縛りを消す（戦闘中）
        // 2026-10-16 追加（その2）
        PoisonAll,       // 毒霧玉：全部の敵に毒 amount（戦闘中）
        WeakenAll,       // 脱力の煙：全部の敵に脱力 amount（戦闘中）
        VulnerableAll,   // 弱体の煙：全部の敵に弱体 amount（戦闘中）
        RemoveCurse,     // 厄払いの札：呪いのダイスを1つ捨てる（戦闘の外で）
        ForgeFace,       // 砥石：いちばん弱いダイスの、いちばん小さい面を+1 を amount 回（戦闘の外で）
        // 2026-10-16 追加（その3）
        SapStrength,     // 力抜きの香：狙っている敵の筋力−amount（戦闘中）
        Transform,       // 化け札：いちばん弱いダイス（呪いがあれば呪い）を、1段上のレア度のダイスに化けさせる（戦闘の外で）
        // 2026-10-16 追加（その4）
        RerollAll,       // 振り直しの巻物：まだ使っていないダイスを全部振り直す（戦闘中）
        HealPercent,     // 養生薬：HP を最大の amount% 回復
        Hamaya,          // 破魔矢：狙っている敵に amount ダメージ（とどめは刺せない。戦闘中）
        BreakBlock,      // 兜割りの札：全部の敵の防御を消す（戦闘中）
        // 2026-10-16 追加（その5。これで 100 種）
        MoreDice,        // 三つ目の賽筒：この戦闘の間、1ラウンドに振れるダイス+amount（最大4個。戦闘中）
        DoubleBlock,     // 鏡の盾の札：今の防御を2倍に（戦闘中。防御のダイスを使ったあとに）
        DoublePoison,    // 毒壺：全部の敵の毒を2倍に（戦闘中）
    }

    /// <summary>お守り。1回だけ使える消耗品（最大3個）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Charm", fileName = "Charm")]
    public class CharmData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea]
        public string description;
        public Sprite icon;
        public CharmKind kind;
        [Tooltip("回復量・出目を変える数など")]
        public int amount;
        public int price = 25;

        /// <summary>移動でダイスを振ったあとに使うお守りか。</summary>
        public bool UsedOnMove => kind == CharmKind.MoveForward || kind == CharmKind.MoveBack || kind == CharmKind.RerollDie;

        /// <summary>戦闘で敵に投げるお守り（薬）か。</summary>
        public bool ThrownAtEnemy => kind == CharmKind.WeakenEnemy || kind == CharmKind.VulnerableEnemy || kind == CharmKind.PoisonEnemy || kind == CharmKind.FrailEnemy || kind == CharmKind.SapStrength || kind == CharmKind.Hamaya;

        /// <summary>協力の戦闘で、仲間にも効くお守りか。</summary>
        public bool AffectsParty => kind == CharmKind.HealParty || kind == CharmKind.BlockParty;
    }
}
