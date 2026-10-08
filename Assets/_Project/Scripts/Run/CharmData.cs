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
        public bool ThrownAtEnemy => kind == CharmKind.WeakenEnemy || kind == CharmKind.VulnerableEnemy || kind == CharmKind.PoisonEnemy;
    }
}
