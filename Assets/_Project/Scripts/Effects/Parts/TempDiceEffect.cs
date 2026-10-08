using SaiNoMichi.Dice;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>戦闘のあいだだけ、ダイスをポーチに加える（戦闘が終わると消える）。例：ボスレリック「呪われた双六盤」（欠け賽）。OnBattleStart で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Temp Dice", fileName = "Fx_TempDice")]
    public class TempDiceEffect : EffectSO
    {
        public DiceData dice;

        public override void Apply(EffectContext ctx)
        {
            if (ctx.battle != null && dice != null) ctx.battle.AddTemporaryDie(dice);
        }
    }
}
