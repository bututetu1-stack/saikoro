using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>戦闘で1ラウンドに振れるダイスを増やす（最大4個）。例：レリック「古い賽筒」（+1）。OnBattleStart で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Dice Per Round", fileName = "Fx_DicePerRound")]
    public class DicePerRoundEffect : EffectSO
    {
        public int add = 1;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.battle != null) ctx.battle.BaseDicePerRound += add;
        }
    }
}
