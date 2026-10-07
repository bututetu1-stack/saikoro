using System.Collections.Generic;
using System.Linq;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 効果の持ち主を集め、発動タイミングごとに効果を実行する。
    /// 常に効いているもの（レリック・状態異常）は Register しておき、
    /// その場面だけのもの（振ったダイス・出た面の刻印）は Fire の local に渡す。
    /// </summary>
    public class EffectBus
    {
        readonly List<IEffectSource> sources = new List<IEffectSource>();

        public IReadOnlyList<IEffectSource> Sources => sources;

        public void Register(IEffectSource source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }

        public void Unregister(IEffectSource source)
        {
            sources.Remove(source);
        }

        /// <summary>trigger の効果を「ダイス → 刻印 → レリック → 状態異常」の順に実行する。同じ種類の中では登録順。</summary>
        public EffectContext Fire(EffectContext ctx, params IEffectSource[] local)
        {
            var all = local.Where(s => s != null).Concat(sources);
            foreach (var source in all.OrderBy(s => (int)s.Kind)) // OrderBy は安定ソート
            {
                if (source.Effects == null) continue;
                foreach (var effect in source.Effects)
                {
                    if (effect != null && effect.trigger == ctx.trigger) effect.Apply(ctx);
                }
            }
            return ctx;
        }
    }
}
