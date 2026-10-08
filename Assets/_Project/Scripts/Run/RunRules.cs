using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    /// <summary>レリックが変える決まりごとと、ボスレリック（仕様書 第10章）。</summary>
    public partial class RunState
    {
        /// <summary>持っているレリックが、その決まりごとを変えているか（重い王冠の「休憩で回復できない」など）。</summary>
        public bool HasRule(RunRule rule) =>
            relics.Any(r => r.effects.Any(e => e is RuleEffect re && re.rule == rule));

        /// <summary>ボスを倒したときに並べるボスレリック（まだ持っていないものから count 個。報酬用の乱数）。</summary>
        public List<RelicData> CreateBossRelicOffer(int count = 3)
        {
            var pool = config.bossRelicPool.Where(r => r != null && !relics.Contains(r)).ToList();
            var offer = new List<RelicData>();
            while (offer.Count < count && pool.Count > 0)
            {
                int i = random.Reward.Next(pool.Count);
                offer.Add(pool[i]);
                pool.RemoveAt(i);
            }
            return offer;
        }
    }
}
