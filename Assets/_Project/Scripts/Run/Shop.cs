using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    public enum ShopItemKind
    {
        Dice,
        Relic,
        Engraving,
        Charm,
    }

    /// <summary>ショップの品物1つ。</summary>
    public class ShopItem
    {
        public ShopItemKind kind;
        public DiceData dice;
        public RelicData relic;
        public EngravingData engraving;
        public CharmData charm;
        public int price;
        public bool discounted;   // 半額の品
        public bool sold;

        public string DisplayName => dice != null ? dice.displayName : relic != null ? relic.displayName : engraving != null ? engraving.displayName : charm != null ? charm.displayName : "";
        public string Description => dice != null ? dice.description : relic != null ? relic.description : engraving != null ? engraving.description : charm != null ? charm.description : "";
        public Rarity Rarity => dice != null ? dice.rarity : relic != null ? relic.rarity : engraving != null ? engraving.rarity : Rarity.Common;
    }

    /// <summary>ショップの数値（仕様書 第11章「ショップ」）。</summary>
    [Serializable]
    public class ShopSettings
    {
        public int diceCount = 3;
        public int relicCount = 2;
        public int engravingCount = 2;
        public int charmCount = 3;
        // ダイスのレア度の重み（コモン・アンコモン・レア）
        // TODO(仕様): ショップのダイスのレア度の出やすさは未定。通常戦の報酬より少し良くした仮の値
        public int[] diceRarityWeights = { 50, 35, 15 };
        // レリックの値段（コモン・アンコモン・レア）
        public int[] relicPrices = { 120, 160, 220 };
        public int discountPercent = 50;    // 1つだけ半額
        public int removeBasePrice = 75;    // ダイス削除
        public int removePriceStep = 25;    // 利用するたびに+25G
        public int minDiceAfterRemove = 2;  // 削除で2個未満にはできない
    }

    /// <summary>
    /// 1回のショップ。品物は止まったときに報酬用の乱数で決まる。
    /// 買う前の選択（ダイスの入れ替え先・刻印を付ける面）は画面側で決めてから呼ぶ。
    /// </summary>
    public class Shop
    {
        readonly RunState run;
        readonly ShopSettings settings;
        public readonly List<ShopItem> items = new List<ShopItem>();

        public Shop(RunState run, ShopSettings settings)
        {
            this.run = run;
            this.settings = settings;
            var rng = run.random.Reward;
            var config = run.config;
            // 縛りの腕輪：品数が半分（TODO(仕様): 割り切れないときは切り上げ）
            bool half = run.HasRule(Effects.RunRule.HalfShopStock);
            int diceCount = half ? (settings.diceCount + 1) / 2 : settings.diceCount;
            int relicCount = half ? (settings.relicCount + 1) / 2 : settings.relicCount;
            int engravingCount = half ? (settings.engravingCount + 1) / 2 : settings.engravingCount;
            int charmCount = half ? (settings.charmCount + 1) / 2 : settings.charmCount;

            foreach (var d in RewardGenerator.PickDice(rng, settings.diceRarityWeights, config.rewardDicePool, diceCount))
            {
                items.Add(new ShopItem { kind = ShopItemKind.Dice, dice = d, price = d.price });
            }
            var relics = config.relicPool.Where(run.RelicCanAppear).ToList();
            for (int i = 0; i < relicCount && relics.Count > 0; i++)
            {
                var r = RewardGenerator.PickOne(rng, config.rewards.relicRarity.For(run.LayerIndex), relics, x => x.rarity);
                relics.Remove(r);
                items.Add(new ShopItem { kind = ShopItemKind.Relic, relic = r, price = RelicPrice(r.rarity) });
            }
            var engravings = config.engravingPool.Where(e => e != null).ToList();
            for (int i = 0; i < engravingCount && engravings.Count > 0; i++)
            {
                var e = RewardGenerator.PickOne(rng, config.rewards.engravingRarity.For(run.LayerIndex), engravings, x => x.rarity);
                engravings.Remove(e);
                items.Add(new ShopItem { kind = ShopItemKind.Engraving, engraving = e, price = e.price });
            }
            // お守り（同じお守りは並べない）
            var charms = config.charmPool.Where(c => c != null).Distinct().ToList();
            for (int i = 0; i < charmCount && charms.Count > 0; i++)
            {
                var c = charms[rng.Next(charms.Count)];
                charms.Remove(c);
                items.Add(new ShopItem { kind = ShopItemKind.Charm, charm = c, price = c.price });
            }

            // 品物の1つは半額（端数切り捨て）
            if (items.Count > 0)
            {
                var sale = items[rng.Next(items.Count)];
                sale.discounted = true;
                sale.price = sale.price * settings.discountPercent / 100;
            }
        }

        int RelicPrice(Rarity rarity)
        {
            int i = Math.Min((int)rarity, settings.relicPrices.Length - 1);
            return i >= 0 ? settings.relicPrices[i] : 0;
        }

        /// <summary>直前に買ったダイスに、縛りの腕輪で付いた刻印（なければ null）。</summary>
        public EngravingData LastAutoEngraving { get; private set; }

        public bool CanAfford(ShopItem item) => !item.sold && run.Gold >= item.price;

        /// <summary>ダイスを買う。ポーチが満杯なら replace と入れ替える（呪いとは入れ替えられない）。</summary>
        public DiceInstance BuyDice(ShopItem item, DiceInstance replace = null)
        {
            Check(item, ShopItemKind.Dice);
            if (!run.CanAddDice && replace == null) throw new InvalidOperationException("ポーチが満杯です。入れ替えるダイスを選んでください。");
            if (replace != null && replace.data != null && replace.data.rarity == Rarity.Curse) throw new InvalidOperationException("呪いのダイスは入れ替えられません。");
            Pay(item);
            var die = replace != null ? run.ReplaceDice(replace, item.dice) : run.AddDice(item.dice);
            // 縛りの腕輪：買ったダイスのランダムな面に、ランダムな刻印を1つ
            LastAutoEngraving = null;
            if (run.HasRule(Effects.RunRule.EngraveBoughtDice) && RunState.CanForge(die))
            {
                var pool = run.config.engravingPool.Where(e => e != null).ToList();
                if (pool.Count > 0)
                {
                    var rng = run.random.Reward;
                    var engraving = RewardGenerator.PickOne(rng, run.config.rewards.engravingRarity.For(run.LayerIndex), pool, x => x.rarity);
                    int face = rng.Next(die.faces.Length);
                    run.ApplyEngraving(die, face, engraving);
                    LastAutoEngraving = engraving;
                }
            }
            return die;
        }

        public void BuyRelic(ShopItem item)
        {
            Check(item, ShopItemKind.Relic);
            Pay(item);
            run.AddRelic(item.relic);
        }

        /// <summary>お守りを買う（いっぱいなら、replacing を捨てて入れ替える）。</summary>
        /// <param name="replacing">いっぱいのとき、代わりに捨てるお守り。</param>
        public void BuyCharm(ShopItem item, CharmData replacing = null)
        {
            Check(item, ShopItemKind.Charm);
            if (replacing != null && !run.Charms.Contains(replacing)) throw new ArgumentException("持っていないお守りです。", nameof(replacing));
            if (!run.CanAddCharm && replacing == null) throw new InvalidOperationException($"お守りは{RunState.MaxCharms}個までしか持てません。");
            Pay(item);
            if (replacing != null) run.DiscardCharm(replacing);
            run.AddCharm(item.charm);
        }

        /// <summary>刻印を買って、die の faceIndex の面に付ける。</summary>
        public void BuyEngraving(ShopItem item, DiceInstance die, int faceIndex)
        {
            Check(item, ShopItemKind.Engraving);
            if (!RunState.CanForge(die)) throw new InvalidOperationException($"{die.DisplayName} は鍛冶で改造できません。");
            Pay(item);
            run.ApplyEngraving(die, faceIndex, item.engraving);
        }

        // ---- ダイス削除（仕様書 第3章「入手と削除」） ----

        /// <summary>ダイス削除の値段（75G、このランで利用するたびに+25G）。</summary>
        public int RemovePrice => settings.removeBasePrice + settings.removePriceStep * run.RemovedDiceCount;

        /// <summary>ポーチが2個未満にならないなら削除できる（呪いのダイスも削除できる）。</summary>
        public bool CanRemove => run.pouch.All.Count > settings.minDiceAfterRemove && run.Gold >= RemovePrice;

        public void RemoveDice(DiceInstance die)
        {
            if (run.pouch.All.Count <= settings.minDiceAfterRemove) throw new InvalidOperationException($"ダイスを{settings.minDiceAfterRemove}個未満にはできません。");
            if (!run.pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (!run.SpendGold(RemovePrice)) throw new InvalidOperationException("ゴールドが足りません。");
            run.RemoveDice(die);
        }

        void Check(ShopItem item, ShopItemKind kind)
        {
            if (!items.Contains(item) || item.kind != kind) throw new ArgumentException("このショップの品物ではありません。", nameof(item));
            if (item.sold) throw new InvalidOperationException("売り切れです。");
            if (run.Gold < item.price) throw new InvalidOperationException("ゴールドが足りません。");
        }

        void Pay(ShopItem item)
        {
            run.SpendGold(item.price);
            item.sold = true;
        }
    }
}
