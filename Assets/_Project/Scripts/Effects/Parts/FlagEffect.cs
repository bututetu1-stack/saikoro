using UnityEngine;

namespace SaiNoMichi.Effects
{
    public enum RollFlag
    {
        CanReroll,        // 刻印「再転」：この面が出たら振り直してよい（1回）
        BothSides,        // 刻印「両刃」・レリック「六の加護」：攻撃と防御の両方に効く
        WarpToRestOrShop, // 刻印「帰り道」：次の休憩マスかショップまで一気に進む
    }

    /// <summary>振ったときの印を付ける（振り直し・両方に効く・帰り道）。OnRoll / OnMoveRolled で使う。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Roll Flag", fileName = "Fx_RollFlag")]
    public class FlagEffect : EffectSO
    {
        public RollFlag flag;
        public EffectCondition condition;

        public override void Apply(EffectContext ctx)
        {
            if (!condition.Matches(ctx)) return;
            switch (flag)
            {
                case RollFlag.CanReroll: ctx.canReroll = true; break;
                case RollFlag.BothSides: ctx.bothSides = true; break;
                case RollFlag.WarpToRestOrShop: ctx.warpToRestOrShop = true; break;
            }
        }
    }
}
