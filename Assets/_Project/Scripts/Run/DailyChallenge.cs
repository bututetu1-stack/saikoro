using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// 今日の旅（Slay the Spire の「デイリー」風）。日付からシードと「今日のお題」が決まり、同じ日なら誰でも同じ旅になる。
    /// お題は旅の始めに一度だけ効く（持ち物が増えるなど。セーブにはその結果が残る）。
    /// </summary>
    public static class DailyChallenge
    {
        /// <summary>今日のお題。</summary>
        // TODO(仕様): お題の種類と量は仮
        public enum Modifier
        {
            Fortune,   // 福の日：はじめのゴールド+150
            Treasure,  // 宝の日：はじめからレリックを1つ
            Charms,    // 御守の日：はじめからお守りを3つ
            ExtraDie,  // 賽の日：はじめから珍しい（アンコモン）ダイスを1個
            Hardship,  // 修行の日：最大HP−15、そのかわりレリック1つとゴールド+100
        }

        public const int FortuneGold = 150;
        public const int HardshipHp = 10;
        public const int HardshipGold = 100;

        /// <summary>日付の印（記録の鍵にも使う）。例：20261016。</summary>
        public static string KeyOf(DateTime date) => date.ToString("yyyyMMdd");

        /// <summary>日付の印から日付に戻す。</summary>
        public static DateTime DateOf(string key) => DateTime.ParseExact(key, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>その日のシード（1 以上）。同じ日なら同じ。</summary>
        public static int SeedOf(DateTime date)
        {
            long key = date.Year * 10000L + date.Month * 100L + date.Day;
            long h = (key * 2654435761L + 97531L) % (int.MaxValue - 1);
            return (int)h + 1;
        }

        /// <summary>その日のお題。</summary>
        public static Modifier ModifierOf(DateTime date)
        {
            var values = (Modifier[])Enum.GetValues(typeof(Modifier));
            return values[new Random(SeedOf(date) ^ 0x5A17).Next(values.Length)];
        }

        public static string NameOf(Modifier m)
        {
            switch (m)
            {
                case Modifier.Fortune: return "福の日";
                case Modifier.Treasure: return "宝の日";
                case Modifier.Charms: return "御守の日";
                case Modifier.ExtraDie: return "賽の日";
                case Modifier.Hardship: return "修行の日";
                default: return m.ToString();
            }
        }

        public static string DescriptionOf(Modifier m)
        {
            switch (m)
            {
                case Modifier.Fortune: return $"はじめのゴールド +{FortuneGold}";
                case Modifier.Treasure: return "はじめからレリックを1つ持っている";
                case Modifier.Charms: return "はじめからお守りを3つ持っている";
                case Modifier.ExtraDie: return "はじめから珍しいダイスを1個持っている";
                case Modifier.Hardship: return $"最大HP −{HardshipHp}。そのかわりレリック1つと {HardshipGold} G";
                default: return "";
            }
        }

        /// <summary>
        /// 旅の始めにお題を効かせる。何を得たかを返す。
        /// 選ぶのはお題用の乱数（旅のシードから作る）なので、同じ日なら誰でも同じものを得て、そのあとの旅の乱数も変わらない。
        /// </summary>
        public static List<string> Apply(RunState run, Modifier m)
        {
            var got = new List<string>();
            var rng = new Random(run.random.Seed ^ 0x2D41);
            switch (m)
            {
                case Modifier.Fortune:
                    run.GainGold(FortuneGold);
                    got.Add($"{FortuneGold} G");
                    break;
                case Modifier.Treasure:
                    AddRelic(run, rng, got);
                    break;
                case Modifier.Charms:
                    for (int i = 0; i < RunState.MaxCharms && run.CanAddCharm; i++)
                    {
                        var c = run.PickCharm(rng);
                        if (c != null && run.AddCharm(c)) got.Add(c.displayName);
                    }
                    break;
                case Modifier.ExtraDie:
                {
                    var pool = run.DicePool.Where(d => d != null && d.rarity == Rarity.Uncommon).OrderBy(d => d.id).ToList();
                    if (pool.Count > 0 && run.CanAddDice)
                    {
                        var d = pool[rng.Next(pool.Count)];
                        run.AddDice(d);
                        got.Add(d.displayName);
                    }
                    break;
                }
                case Modifier.Hardship:
                    run.player.maxHp = Math.Max(1, run.player.maxHp - HardshipHp);
                    run.player.hp = Math.Min(run.player.hp, run.player.maxHp);
                    AddRelic(run, rng, got);
                    run.GainGold(HardshipGold);
                    got.Add($"{HardshipGold} G");
                    break;
            }
            return got;
        }

        static void AddRelic(RunState run, Random rng, List<string> got)
        {
            // ボス・特別なレリックは出さない（ふつうの候補のコモン・アンコモンから）
            var pool = run.RelicPool.Where(r => run.RelicCanAppear(r) && (r.rarity == Rarity.Common || r.rarity == Rarity.Uncommon)).OrderBy(r => r.id).ToList();
            if (pool.Count == 0) return;
            var relic = pool[rng.Next(pool.Count)];
            run.AddRelic(relic);
            got.Add(relic.displayName);
        }
    }
}
