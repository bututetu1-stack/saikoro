using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Run
{
    public class BattleReward
    {
        public RewardKind kind;
        public int gold;
        public List<DiceData> diceChoices = new List<DiceData>();
        public Effects.RelicData relic;   // エリートのレリック（確定。候補がなければ null）
        public CharmData charm;           // 通常戦のお守り（40%。なければ null）
        public bool charmRejected;        // お守りがいっぱいで持てなかった
    }

    /// <summary>戦闘報酬を報酬用の乱数で決める（戦闘の振り方を変えても報酬は変わらない）。</summary>
    public static class RewardGenerator
    {
        static readonly Rarity[] Rarities = { Rarity.Common, Rarity.Uncommon, Rarity.Rare };

        public static BattleReward ForBattle(RewardKind kind, Random rng, RewardSettings s, IReadOnlyList<DiceData> pool)
        {
            var reward = new BattleReward { kind = kind };
            int[] weights;
            switch (kind)
            {
                case RewardKind.Elite:
                    reward.gold = rng.Next(s.eliteGoldMin, s.eliteGoldMax + 1);
                    weights = s.eliteRarityWeights;
                    break;
                case RewardKind.Boss:
                    reward.gold = s.bossGold;
                    weights = s.bossRarityWeights;
                    break;
                default:
                    reward.gold = rng.Next(s.normalGoldMin, s.normalGoldMax + 1);
                    weights = s.normalRarityWeights;
                    break;
            }
            reward.diceChoices = PickDice(rng, weights, pool, s.diceChoiceCount);
            return reward;
        }

        /// <summary>
        /// レア度を重みで決めてから、そのレア度のダイスを選ぶ。同じダイスは並べない。
        /// そのレア度に残りがなければ、近いレア度（上→下の順）から選ぶ。
        /// </summary>
        public static List<DiceData> PickDice(Random rng, int[] weights, IReadOnlyList<DiceData> pool, int count)
        {
            var remaining = pool.Where(d => d != null && d.rarity != Rarity.Curse).Distinct().ToList();
            var picked = new List<DiceData>();
            while (picked.Count < count && remaining.Count > 0)
            {
                var rarity = RollRarity(rng, weights);
                var candidates = remaining.Where(d => d.rarity == rarity).ToList();
                if (candidates.Count == 0) candidates = NearestRarity(remaining, rarity);
                var choice = candidates[rng.Next(candidates.Count)];
                picked.Add(choice);
                remaining.Remove(choice);
            }
            return picked;
        }

        public static Rarity RollRarity(Random rng, int[] weights)
        {
            int total = weights.Sum();
            if (total <= 0) return Rarity.Common;
            int roll = rng.Next(total);
            for (int i = 0; i < weights.Length && i < Rarities.Length; i++)
            {
                if (roll < weights[i]) return Rarities[i];
                roll -= weights[i];
            }
            return Rarity.Common;
        }

        static List<DiceData> NearestRarity(List<DiceData> remaining, Rarity wanted)
        {
            int target = (int)wanted;
            int best = remaining.Min(d => Math.Abs((int)d.rarity - target));
            // 同じ距離なら上のレア度を優先（報酬が下がるより上がるほうがよい）
            var nearest = remaining.Where(d => Math.Abs((int)d.rarity - target) == best).ToList();
            int top = nearest.Max(d => (int)d.rarity);
            return nearest.Where(d => (int)d.rarity == top).ToList();
        }
    }
}
