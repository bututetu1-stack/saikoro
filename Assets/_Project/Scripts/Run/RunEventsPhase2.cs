using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    public struct PitfallResult
    {
        public int value;
        public bool avoided;
        public int damage;
        public bool refreshed;
    }

    public struct OniResult
    {
        public int playerValue;
        public int oniValue;
        public bool win;
        public bool draw;
        public RelicData relic;   // 勝ったときのレリック（候補がなければ null）
        public bool refreshed;
    }

    /// <summary>フェーズ2で足したイベント（仕様書 第9章）。</summary>
    public partial class RunState
    {
        readonly HashSet<EventKind> occurredEvents = new HashSet<EventKind>();

        /// <summary>いまの層で起きてよいイベントか（出る層・1ランに1回）。</summary>
        public bool EventAllowed(EventKind kind)
        {
            var s = config.events;
            int layer = LayerIndex + 1;
            switch (kind)
            {
                case EventKind.TwinStatues: if (layer < s.twinMinLayer) return false; break;
                case EventKind.OniDice: if (layer < s.oniMinLayer) return false; break;
                case EventKind.StartOverCard: if (layer > s.startOverMaxLayer) return false; break;
                case EventKind.Tsukumogami: if (layer < s.tsukumogamiMinLayer) return false; break;
            }
            return !(s.oncePerRun.Contains(kind) && occurredEvents.Contains(kind));
        }

        void RecordEvent(EventKind kind) => occurredEvents.Add(kind);

        // ---- 流しの職人 ----

        public bool CanCraft => Gold >= config.events.craftCost && pouch.All.Any(CanForge);

        /// <summary>お金を払って、die の faceIndex の面の数値を value（1〜6）に変える。刻印はそのまま。</summary>
        public void Craft(DiceInstance die, int faceIndex, int value)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (!CanForge(die)) throw new InvalidOperationException($"{die.DisplayName} は改造できません。");
            if (value < 1 || value > 6) throw new ArgumentOutOfRangeException(nameof(value));
            if (!SpendGold(config.events.craftCost)) throw new InvalidOperationException("ゴールドが足りません。");
            var face = die.faces[faceIndex];
            face.value = value;
            die.faces[faceIndex] = face;
            NotifyAcquired("craft", $"{die.DisplayName}:{faceIndex}={value}");
        }

        // ---- 道祖神の双子像 ----

        /// <summary>複製できるダイス（呪いのダイスは複製できない）。</summary>
        // TODO(仕様): 呪いのダイスを捧げるのはよい（呪いを手放せる）が、複製はできないことにした
        public static bool CanDuplicate(DiceInstance die) => die.data == null || die.data.rarity != Rarity.Curse;

        public bool CanUseTwinStatues => pouch.All.Count >= 2 && pouch.All.Any(CanDuplicate);

        /// <summary>offer を捧げて（ポーチから消える）、copy を刻印ごと複製する。複製したダイスを返す。</summary>
        public DiceInstance OfferAndDuplicate(DiceInstance offer, DiceInstance copy)
        {
            if (offer == copy) throw new ArgumentException("捧げるダイスと複製するダイスは別にしてください。");
            if (!pouch.All.Contains(offer) || !pouch.All.Contains(copy)) throw new ArgumentException("ポーチにないダイスです。");
            if (!CanDuplicate(copy)) throw new InvalidOperationException($"{copy.DisplayName} は複製できません。");
            pouch.Remove(offer);
            NotifyAcquired("discard", offer.DisplayName);
            var twin = AddDice(copy.data, true);
            for (int i = 0; i < twin.faces.Length; i++) twin.faces[i] = copy.faces[i];
            return twin;
        }

        // ---- 落とし穴 ----

        /// <summary>ダイスを振り、しきい値以上なら回避。失敗すると HP が減る。</summary>
        public PitfallResult Pitfall(DiceInstance die)
        {
            var s = config.events;
            var result = new PitfallResult { value = RollForEvent(die, out bool refreshed), refreshed = refreshed };
            result.avoided = result.value >= s.pitfallThreshold;
            if (!result.avoided) result.damage = player.LoseHp(s.pitfallDamage);
            return result;
        }

        // ---- 旅の商人 ----

        /// <summary>商人と交換できるダイス（同じレア度のほかのダイスが報酬の候補にあるもの）。</summary>
        public bool CanTrade(DiceInstance die) => die.data != null && TradeCandidates(die.data).Count > 0;

        List<DiceData> TradeCandidates(DiceData data) =>
            config.rewardDicePool.Where(d => d != null && d != data && d.rarity == data.rarity && d.rarity != Rarity.Curse).Distinct().ToList();

        /// <summary>die を渡して、同じレア度のランダムなダイス（違う種類）をもらう。刻印は消える。</summary>
        public DiceInstance Trade(DiceInstance die)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            var candidates = TradeCandidates(die.data);
            if (candidates.Count == 0) throw new InvalidOperationException($"{die.DisplayName} と交換できるダイスがありません。");
            var data = candidates[random.Event.Next(candidates.Count)];
            pouch.Remove(die);
            NotifyAcquired("discard", die.DisplayName);
            return AddDice(data, true);
        }

        // ---- 鬼の賽勝負 ----

        /// <summary>賭けられるダイス：使用可能で呪いでない。負けてもダイスが最低数より減らないこと。</summary>
        // TODO(仕様): 呪いのダイスは賭けられない（負けると得になるため）
        public bool CanBetOni(DiceInstance die) =>
            die.state == DiceState.Available && (die.data == null || die.data.rarity != Rarity.Curse)
            && pouch.All.Count > config.shop.minDiceAfterRemove;

        public bool CanPlayOni => pouch.All.Any(CanBetOni);

        /// <summary>
        /// die を賭けて鬼と出目を比べる。鬼は普通のダイス（1〜6）を振る。
        /// 大きければ勝ち（レリック）、小さければ負け（die を奪われる）。
        /// TODO(仕様): 同じ出目は引き分けで、何も起きない
        /// </summary>
        public OniResult PlayOni(DiceInstance die)
        {
            if (!CanBetOni(die)) throw new InvalidOperationException($"{die.DisplayName} は賭けられません。");
            var result = new OniResult { playerValue = RollForEvent(die, out bool refreshed), refreshed = refreshed };
            result.oniValue = random.Event.Next(1, 7);
            if (result.playerValue > result.oniValue)
            {
                result.win = true;
                result.relic = PickRelic();
                if (result.relic != null) AddRelic(result.relic);
            }
            else if (result.playerValue == result.oniValue)
            {
                result.draw = true;
            }
            else
            {
                pouch.Remove(die);
                stats.diceRemoved.Add(die.DisplayName);
                NotifyAcquired("lost", die.DisplayName);
            }
            return result;
        }

        // ---- 迷子の子ども ----

        /// <summary>
        /// 送ってあげる：1回休み（ダイスを1個選んで、進まずに使用済みにする）。お礼にゴールドとお守り1個（持てなければゴールドだけ）。
        /// TODO(仕様): 「1回休み」は、ダイス1個を進まずに使う（ターンも1つ進む）こととした
        /// </summary>
        public int GuideLostChild(DiceInstance die, out CharmData charm, out bool refreshed)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");
            refreshed = pouch.Use(die, false, false);
            Turn++;
            charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && !AddCharm(charm)) charm = null;
            return GainGold(config.events.lostChildGold);
        }

        /// <summary>道を教えるだけ：少しのゴールド。</summary>
        public int DirectLostChild() => GainGold(config.events.lostChildDirectionsGold);

        // ---- 振り出しの札 ----

        /// <summary>札を引く：層のスタートに戻る。代わりに最大 HP が増え、全回復する。</summary>
        public void DrawStartOverCard()
        {
            player.maxHp += config.events.startOverMaxHp;
            player.Heal(player.maxHp);
            Current = board.Start;
            NotifyAcquired("start_over", $"{LayerIndex + 1}");
        }

        // ---- 行き倒れの侍 ----

        /// <summary>介抱できるか（HP が手当ての分より多い）。</summary>
        public bool CanHelpSamurai => player.hp > config.events.samuraiHpCost;

        /// <summary>介抱する：HP を減らして、お礼にレリック（もう候補がなければゴールド）。</summary>
        public RelicData HelpSamurai(out int gold)
        {
            if (!CanHelpSamurai) throw new InvalidOperationException("HP が足りません。");
            player.LoseHp(config.events.samuraiHpCost);
            gold = 0;
            var relic = config.relicPool.Exists(RelicCanAppear) ? PickRelic() : null;
            if (relic != null) AddRelic(relic);
            else gold = GainGold(config.events.samuraiNoRelicGold);
            return relic;
        }

        /// <summary>懐を探る：ゴールドを得るが、呪いのダイスを押し付けられる（ポーチが満杯なら入らない）。</summary>
        public int RobSamurai(out DiceInstance curse)
        {
            curse = null;
            var curses = config.curseDicePool.FindAll(d => d != null);
            if (curses.Count > 0 && !pouch.IsFull) curse = AddDice(curses[random.Event.Next(curses.Count)]);
            return GainGold(config.events.samuraiRobGold);
        }

        // ---- 湯治場 ----

        /// <summary>湯に浸かったときに回復する HP（最大 HP の割合）。</summary>
        public int HotSpringHeal => player.maxHp * config.events.hotSpringHealPercent / 100;

        public bool CanBathe => Gold >= config.events.hotSpringCost;

        /// <summary>湯に浸かる：ゴールドを払って大きく回復。回復した量を返す。</summary>
        public int Bathe()
        {
            if (!SpendGold(config.events.hotSpringCost)) throw new InvalidOperationException("ゴールドが足りません。");
            int before = player.hp;
            player.Heal(HotSpringHeal);
            return player.hp - before;
        }

        /// <summary>足湯だけ：ただで少し回復。回復した量を返す。</summary>
        public int FootBath()
        {
            int before = player.hp;
            player.Heal(config.events.footBathHeal);
            return player.hp - before;
        }

        // ---- 賽の付喪神 ----

        /// <summary>付喪神に差し出せるダイス（ピンゾロ賽など、鍛冶で改造できないものも差し出せる）。</summary>
        public bool CanOfferToTsukumogami(DiceInstance die) => die != null && pouch.All.Contains(die);

        /// <summary>差し出したダイスが化けるレア度：呪い・コモンはアンコモンに、アンコモン・レアはレアに。</summary>
        public static Rarity TsukumogamiRarity(DiceInstance die)
        {
            var rarity = die.data != null ? die.data.rarity : Rarity.Common;
            return rarity == Rarity.Uncommon || rarity == Rarity.Rare ? Rarity.Rare : Rarity.Uncommon;
        }

        /// <summary>ダイスを差し出すと、1段上のレア度のダイスに化ける（同じ種類は避ける）。化けたダイスを返す。候補がなければ null で何もしない。</summary>
        public DiceInstance OfferToTsukumogami(DiceInstance die)
        {
            if (!CanOfferToTsukumogami(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            var rarity = TsukumogamiRarity(die);
            var candidates = config.rewardDicePool.Where(d => d != null && d.rarity == rarity && d != die.data).ToList();
            if (candidates.Count == 0) return null;
            var data = candidates[random.Event.Next(candidates.Count)];
            // 呪いのダイスも差し出せる（ReplaceDice は呪いを入れ替えられないので、ここで入れ替える）
            pouch.Remove(die);
            NotifyAcquired("discard", die.DisplayName);
            return AddDice(data, true);
        }
    }
}
