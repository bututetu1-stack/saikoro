using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>ランの数値を増やす（休憩の回復量・毒の量・鍛冶で付けられる数など）。持っているあいだずっと効く。</summary>
    public enum RunStat
    {
        RestHealPercent,       // 休憩の回復量 +amount%（薬草袋 +50）
        PoisonBonus,           // 毒を与えるとき +amount（毒壺 +1）
        ForgeExtraEngravings,  // 鍛冶マスで付けられる刻印 +amount（鍛冶の金槌 +1）
    }

    /// <summary>ランの数値を増やす。Apply では何もしない（RunState.StatBonus で合計する）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Stat Bonus", fileName = "Fx_StatBonus")]
    public class StatBonusEffect : EffectSO
    {
        public RunStat stat;
        public int amount;

        public override void Apply(EffectContext ctx) { }
    }
}
