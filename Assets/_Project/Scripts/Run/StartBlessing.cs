using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;

namespace SaiNoMichi.Run
{
    /// <summary>旅立ちの祝福の種類。</summary>
    public enum BlessingKind
    {
        // ふつうの祝福（そのままうれしい）
        MaxHp,      // 最大HP が増える
        Gold,       // ゴールド
        Charms,     // お守り
        Relic,      // コモンのレリック
        Dice,       // 珍しい（アンコモン）ダイス
        // 代償つきの祝福（何かを失って、大きなものを得る）
        RareRelic,  // 最大HP を失って、レアのレリック
        CurseGold,  // 呪いのダイスを押し付けられて、たくさんのゴールド
        RareDice,   // 持っているゴールドを全部失って、レアのダイス
        Engrave,    // いちばん弱いダイスの、いちばん小さい面にランダムな刻印（ふつうの祝福）
    }

    /// <summary>
    /// 旅立ちの祝福（Slay the Spire の「ネオーの祝福」風）。ひとりの旅の始めに、3つの祝福から1つを選ぶ。
    /// 候補は「ふつうの祝福」2つと「代償つきの祝福」1つ。旅のシードから決まるので、同じシードなら同じ候補が出る。
    /// 祝福用の乱数は旅の乱数とは別に作るので、どれを選んでも、そのあとの旅（盤面・戦闘・報酬）は変わらない。
    /// </summary>
    public static class StartBlessing
    {
        // TODO(仕様): 祝福の量はすべて仮
        public const int MaxHpGain = 14;
        public const int GoldGain = 100;
        public const int CharmCount = 2;
        public const int RareRelicHpCost = 14;
        public const int CurseGold = 250;

        static readonly BlessingKind[] Plain = { BlessingKind.MaxHp, BlessingKind.Gold, BlessingKind.Charms, BlessingKind.Relic, BlessingKind.Dice, BlessingKind.Engrave };
        static readonly BlessingKind[] Tradeoff = { BlessingKind.RareRelic, BlessingKind.CurseGold, BlessingKind.RareDice };

        /// <summary>その旅で選べる3つの祝福（ふつう2つ・代償つき1つの順）。</summary>
        public static List<BlessingKind> Offer(int runSeed)
        {
            var rng = new Random(runSeed ^ 0x4E30);
            var plain = Plain.OrderBy(_ => rng.Next()).Take(2).ToList();
            plain.Add(Tradeoff[rng.Next(Tradeoff.Length)]);
            return plain;
        }

        public static bool IsTradeoff(BlessingKind kind) => Array.IndexOf(Tradeoff, kind) >= 0;

        public static string NameOf(BlessingKind kind)
        {
            switch (kind)
            {
                case BlessingKind.MaxHp: return "健やかの祝福";
                case BlessingKind.Gold: return "福の祝福";
                case BlessingKind.Charms: return "守りの祝福";
                case BlessingKind.Relic: return "宝の祝福";
                case BlessingKind.Dice: return "賽の祝福";
                case BlessingKind.RareRelic: return "血の誓い";
                case BlessingKind.CurseGold: return "厄の引き受け";
                case BlessingKind.RareDice: return "無一文の賭け";
                case BlessingKind.Engrave: return "刻印の祝福";
                default: return kind.ToString();
            }
        }

        public static string DescriptionOf(BlessingKind kind)
        {
            switch (kind)
            {
                case BlessingKind.MaxHp: return $"最大HP +{MaxHpGain}";
                case BlessingKind.Gold: return $"{GoldGain} G を得る";
                case BlessingKind.Charms: return $"お守りを {CharmCount} つ得る";
                case BlessingKind.Relic: return "コモンのレリックを1つ得る";
                case BlessingKind.Dice: return "珍しいダイスを1個得る";
                case BlessingKind.RareRelic: return $"最大HP −{RareRelicHpCost}。レアのレリックを1つ得る";
                case BlessingKind.CurseGold: return $"呪いのダイスを1個押し付けられる。{CurseGold} G を得る";
                case BlessingKind.RareDice: return "持っているゴールドを全部失う。レアのダイスを1個得る";
                case BlessingKind.Engrave: return "いちばん弱いダイスの、いちばん小さい面にランダムな刻印を1つ";
                default: return "";
            }
        }

        /// <summary>祝福を受ける。何を得た（失った）かを返す。</summary>
        public static List<string> Apply(RunState run, BlessingKind kind)
        {
            var got = new List<string>();
            var rng = new Random(run.random.Seed ^ 0x4E31);
            switch (kind)
            {
                case BlessingKind.MaxHp:
                    run.player.maxHp += MaxHpGain;
                    run.player.hp += MaxHpGain;
                    got.Add($"最大HP +{MaxHpGain}");
                    break;
                case BlessingKind.Gold:
                    got.Add($"{run.GainGold(GoldGain)} G");
                    break;
                case BlessingKind.Charms:
                    for (int i = 0; i < CharmCount && run.CanAddCharm; i++)
                    {
                        var c = run.PickCharm(rng);
                        if (c != null && run.AddCharm(c)) got.Add(c.displayName);
                    }
                    break;
                case BlessingKind.Relic:
                    AddRelic(run, rng, got, Rarity.Common);
                    break;
                case BlessingKind.Dice:
                    AddDice(run, rng, got, Rarity.Uncommon);
                    break;
                case BlessingKind.RareRelic:
                    run.player.maxHp = Math.Max(1, run.player.maxHp - RareRelicHpCost);
                    run.player.hp = Math.Min(run.player.hp, run.player.maxHp);
                    got.Add($"最大HP −{RareRelicHpCost}");
                    AddRelic(run, rng, got, Rarity.Rare);
                    break;
                case BlessingKind.CurseGold:
                {
                    var curses = run.config.curseDicePool.Where(d => d != null).OrderBy(d => d.id).ToList();
                    if (curses.Count == 0 && run.config.curseDice != null) curses.Add(run.config.curseDice);
                    if (curses.Count > 0 && run.CanAddDice)
                    {
                        var curse = curses[rng.Next(curses.Count)];
                        run.AddDice(curse);
                        got.Add($"呪いの {curse.displayName}");
                    }
                    got.Add($"{run.GainGold(CurseGold)} G");
                    break;
                }
                case BlessingKind.Engrave:
                {
                    var die = run.pouch.All
                        .Where(d => RunState.CanForge(d) && (d.data == null || d.data.rarity != Rarity.Curse))
                        .OrderBy(d => d.faces.Sum(f => f.value)).FirstOrDefault();
                    var pool = run.config.engravingPool.Where(e => e != null).OrderBy(e => e.id).ToList();
                    if (die == null || pool.Count == 0) break;
                    var engraving = pool[rng.Next(pool.Count)];
                    int face = 0;
                    for (int i = 1; i < die.faces.Length; i++) if (die.faces[i].value < die.faces[face].value) face = i;
                    run.ApplyEngraving(die, face, engraving);
                    got.Add($"{die.DisplayName} に刻印「{engraving.displayName}」");
                    break;
                }
                case BlessingKind.RareDice:
                    if (run.Gold > 0)
                    {
                        got.Add($"−{run.LoseGold(run.Gold)} G");
                    }
                    AddDice(run, rng, got, Rarity.Rare);
                    break;
            }
            return got;
        }

        static void AddRelic(RunState run, Random rng, List<string> got, Rarity rarity)
        {
            // ボス・特別なレリックは出さない（ふつうの候補から。そのレア度がなければ近いものから）
            var candidates = run.RelicPool.Where(run.RelicCanAppear).OrderBy(r => r.id).ToList();
            var pool = candidates.Where(r => r.rarity == rarity).ToList();
            if (pool.Count == 0) pool = candidates.Where(r => r.rarity == Rarity.Uncommon).ToList();
            if (pool.Count == 0) return;
            var relic = pool[rng.Next(pool.Count)];
            run.AddRelic(relic);
            got.Add(relic.displayName);
        }

        static void AddDice(RunState run, Random rng, List<string> got, Rarity rarity)
        {
            var pool = run.DicePool.Where(d => d != null && d.rarity == rarity).OrderBy(d => d.id).ToList();
            if (pool.Count == 0 || !run.CanAddDice) return;
            var d = pool[rng.Next(pool.Count)];
            run.AddDice(d);
            got.Add(d.displayName);
        }
    }
}
