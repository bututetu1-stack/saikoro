using System;

namespace SaiNoMichi.Battle
{
    /// <summary>敵の予告の種類。フェーズ0では攻撃・防御・強化のみ。</summary>
    public enum IntentType
    {
        Attack,
        Block,
        Buff,   // 筋力を得る
    }

    [Serializable]
    public struct Intent
    {
        public IntentType type;
        public int value;

        public Intent(IntentType type, int value)
        {
            this.type = type;
            this.value = value;
        }

        public override string ToString() => $"{type} {value}";
    }
}
