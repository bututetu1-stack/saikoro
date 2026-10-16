using System;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>呪いのダイスの悪い効き目（2026-10-16 追加）。</summary>
    public enum CurseKind
    {
        SelfPoison,     // 疫病賽：戦闘で振ると、自分に毒 amount
        SelfBind,       // 呪縛賽：戦闘で振ると、自分に縛り（このラウンドはもう振れず、次のラウンドも1個だけ）
        LoseGold,       // 貧乏神の賽：振るたびに amount G 失う（足りなければあるだけ）
        EnemyStrength,  // 怨み賽：戦闘で振ると、狙っている敵の筋力+amount
    }

    /// <summary>呪いのダイスの悪い効き目。OnRoll で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Curse", fileName = "Fx_Curse")]
    public class CurseEffect : EffectSO
    {
        public CurseKind kind;
        public int amount = 1;

        public override void Apply(EffectContext ctx)
        {
            var player = ctx.player ?? ctx.run?.player;
            switch (kind)
            {
                case CurseKind.SelfPoison:
                    if (ctx.battle != null && player != null) player.ApplyPoison(amount);
                    break;
                case CurseKind.SelfBind:
                    // 縛りは今のラウンドから効く（このラウンドはもう振れず、次のラウンドも1個だけ）
                    if (ctx.battle != null && player != null) player.ApplyBind();
                    break;
                case CurseKind.LoseGold:
                    if (ctx.run != null) ctx.run.SpendGold(Math.Min(amount, ctx.run.Gold));
                    break;
                case CurseKind.EnemyStrength:
                    var enemy = ctx.battle?.Target;
                    if (enemy != null && !enemy.IsDead) enemy.strength += amount;
                    break;
            }
        }
    }
}
