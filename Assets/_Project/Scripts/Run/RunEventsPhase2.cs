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
                case EventKind.Bonfire:
                case EventKind.Feast: if (!multiplayer) return false; break;
                // 合わせ賽の鍛冶師：1ランに1回。合わせられるダイスが2個以上あって、ダイスが減っても少なすぎないとき
                case EventKind.MergeSmith: if (occurredEvents.Contains(kind) || !CanMergeDice) return false; break;
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
            stats.RecordHp(Turn, player.hp, player.maxHp, LayerIndex); // ターンの始めの HP（結果のグラフ用）
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
            var relic = RelicPool.Exists(RelicCanAppear) ? PickRelic() : null;
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

        // ---- 河童の相撲（2026-10-16 追加。TODO(仕様): 数値は仮） ----

        public struct SumoResult
        {
            public int value;
            public bool refreshed;
            public bool won;
            public int gold;
            public CharmData charm;
            public int damage;
        }

        /// <summary>ダイスを振って河童と相撲をとる。しきい値以上なら勝ち（ゴールドとお守り）、負けると HP が減る。振ったダイスは使用済み。</summary>
        public SumoResult Sumo(DiceInstance die)
        {
            var s = config.events;
            var r = new SumoResult { value = RollForEvent(die, out bool refreshed), refreshed = refreshed };
            r.won = r.value >= s.sumoThreshold;
            if (r.won)
            {
                r.gold = GainGold(s.sumoGold);
                var charm = CanAddCharm ? PickCharm(random.Event) : null;
                if (charm != null && AddCharm(charm)) r.charm = charm;
            }
            else r.damage = player.LoseHp(s.sumoDamage);
            return r;
        }

        // ---- 賽銭箱（2026-10-16 追加。TODO(仕様): 数値は仮） ----

        public bool CanOffer => Gold >= config.events.offeringCost;

        /// <summary>お賽銭を投げる：最大HP が増える（HP も同じだけ回復）。増えた量を返す。</summary>
        public int Offer()
        {
            if (!SpendGold(config.events.offeringCost)) throw new InvalidOperationException("ゴールドが足りません。");
            int amount = config.events.offeringMaxHp;
            player.maxHp += amount;
            player.Heal(amount);
            return amount;
        }

        // ---- 合わせ賽の鍛冶師（Inscryption の「合体」風：2個のダイスを1個に打ち合わせる） ----

        // TODO(仕様): 合わせたあとに残すダイスの数は仮（5個以上持っているときだけ。合わせると4個以上）
        public const int MergeMinDice = 5;

        /// <summary>合わせられるダイスか（呪いのダイスと、改造できないダイスは合わせられない）。</summary>
        public bool CanBeMerged(DiceInstance die) =>
            die != null && pouch.All.Contains(die) && CanForge(die) && (die.data == null || die.data.rarity != Rarity.Curse);

        public bool CanMergeDice => pouch.All.Count() >= MergeMinDice && pouch.All.Count(CanBeMerged) >= 2;

        /// <summary>
        /// keep に lose を打ち合わせる：面ごとに数値の大きいほう（刻印ごと）を keep に残し、lose はなくなる。
        /// 同じ数値なら keep の面（keep に刻印がなく lose にあれば lose の面）。
        /// </summary>
        public void MergeDice(DiceInstance keep, DiceInstance lose)
        {
            if (keep == lose || !CanBeMerged(keep) || !CanBeMerged(lose)) throw new ArgumentException("合わせられないダイスです。");
            for (int i = 0; i < keep.faces.Length && i < lose.faces.Length; i++)
            {
                var a = keep.faces[i];
                var b = lose.faces[i];
                if (b.value > a.value || (b.value == a.value && a.engraving == null && b.engraving != null)) keep.faces[i] = b;
            }
            pouch.Remove(lose);
            NotifyAcquired("discard", lose.DisplayName);
            NotifyAcquired("merge", $"{keep.DisplayName}+{lose.DisplayName}");
        }

        // ---- 福引き所（がらがら。Balatro の「運命の輪」のように、たまに大当たり、まれに呪い） ----

        /// <summary>福引きの玉。</summary>
        public enum LotteryBall { White, Red, Blue, Gold, Black }

        public struct LotteryResult
        {
            public LotteryBall ball;
            public int gold;
            public CharmData charm;
            public RelicData relic;
            public DiceInstance curse;
        }

        // TODO(仕様): 玉の出やすさ（白35・赤30・青20・金10・黒5）と中身は仮
        public const int LotteryWhiteGold = 10, LotteryRedGold = 60, LotteryFallbackCharmGold = 40, LotteryFallbackRelicGold = 120;

        public bool CanSpinLottery => Gold >= config.events.lotteryCost;

        /// <summary>
        /// 福引きを1回まわす（イベント用の乱数）。白：残念賞 10 G・赤：60 G・青：お守り（いっぱいなら 40 G）・
        /// 金：レリック（なければ 120 G）・黒：呪いのダイス。
        /// </summary>
        public LotteryResult SpinLottery()
        {
            if (!SpendGold(config.events.lotteryCost)) throw new InvalidOperationException("ゴールドが足りません。");
            int roll = random.Event.Next(100);
            var result = new LotteryResult
            {
                ball = roll < 35 ? LotteryBall.White : roll < 65 ? LotteryBall.Red : roll < 85 ? LotteryBall.Blue : roll < 95 ? LotteryBall.Gold : LotteryBall.Black,
            };
            switch (result.ball)
            {
                case LotteryBall.White:
                    result.gold = GainGold(LotteryWhiteGold);
                    break;
                case LotteryBall.Red:
                    result.gold = GainGold(LotteryRedGold);
                    break;
                case LotteryBall.Blue:
                {
                    var charm = CanAddCharm ? PickCharm(random.Event) : null;
                    if (charm != null && AddCharm(charm)) result.charm = charm;
                    else result.gold = GainGold(LotteryFallbackCharmGold);
                    break;
                }
                case LotteryBall.Gold:
                {
                    var candidates = RelicPool.FindAll(RelicCanAppear);
                    if (candidates.Count > 0)
                    {
                        result.relic = candidates[random.Event.Next(candidates.Count)];
                        AddRelic(result.relic);
                    }
                    else result.gold = GainGold(LotteryFallbackRelicGold);
                    break;
                }
                case LotteryBall.Black:
                    result.curse = ForceCurse();
                    break;
            }
            return result;
        }

        // ---- 旅団の焚き火（みんなで遊ぶとき専用） ----

        // TODO(仕様): 回復の割合と、お守りがいっぱいのときのゴールドは仮
        public const int BonfireHealPercent = 25;
        public const int BonfireGold = 30;

        public int BonfireHeal => player.maxHp * BonfireHealPercent / 100;

        /// <summary>語り明かす：HP を回復。回復した量を返す。</summary>
        public int BonfireRest()
        {
            int before = player.hp;
            player.Heal(BonfireHeal);
            return player.hp - before;
        }

        /// <summary>旅の知恵を分け合う：お守りを1つ（みんなで遊ぶとき専用のものも出る）。持ちきれなければゴールド。得たお守りか null。</summary>
        public CharmData BonfireShare(out int gold)
        {
            gold = 0;
            var charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && AddCharm(charm)) return charm;
            gold = GainGold(BonfireGold);
            return null;
        }

        // ---- 旅団の宴（みんなで遊ぶとき専用） ----

        // TODO(仕様): 宴の回復の割合とご祝儀は仮
        public const int FeastHealPercent = 15;
        public const int FeastGold = 25;

        public int FeastHeal => player.maxHp * FeastHealPercent / 100;

        /// <summary>宴に加わる：HP を少し回復し、ご祝儀のゴールドをもらう。回復した量を返す。</summary>
        public int JoinFeast(out int gold)
        {
            int before = player.hp;
            player.Heal(FeastHeal);
            gold = GainGold(FeastGold);
            return player.hp - before;
        }

        /// <summary>賽を語り明かすときに鍛えるダイス：鍛えられるダイスのうち、面の合計がいちばん小さいもの（呪いのダイスは除く。まだ6より小さい面があるもの）。</summary>
        public DiceInstance FeastTalkTarget => pouch.All
            .Where(d => CanForge(d) && (d.data == null || d.data.rarity != Rarity.Curse) && d.faces.Any(f => f.value < 6))
            .OrderBy(d => d.faces.Sum(f => f.value)).FirstOrDefault();

        /// <summary>賽を語り明かす：いちばん弱いダイスの、いちばん小さい面を +1。鍛えたダイス（なければ null）。</summary>
        public DiceInstance FeastTalk(out int before, out int after)
        {
            before = after = 0;
            var die = FeastTalkTarget;
            if (die == null) return null;
            int face = 0;
            for (int i = 1; i < die.faces.Length; i++) if (die.faces[i].value < die.faces[face].value) face = i;
            var f = die.faces[face];
            before = f.value;
            f.value = Math.Min(6, f.value + 1);
            die.faces[face] = f;
            after = f.value;
            return die;
        }

        // ---- 賽の河原（石を積むたびにごほうびが増えるが、鬼が来ると崩れる。STS・Balatro の運試し風） ----

        // TODO(仕様): 鬼が来る確率・ごほうび・ダメージは仮
        public const int KawaraMaxStones = 3;
        public const int KawaraDamage = 8;
        public const int KawaraGold = 30;

        /// <summary>賽の河原で積んだ石の数（イベントのあいだだけ使う）。</summary>
        public int KawaraStones { get; private set; }

        /// <summary>次に積むとき、鬼が来る確率（6 分の いくつ）。積むほど高い（1/6・2/6・3/6）。</summary>
        public int KawaraOniChance => KawaraStones + 1;

        public bool CanStackStone => KawaraStones < KawaraMaxStones;

        public void BeginKawara() => KawaraStones = 0;

        /// <summary>石を1つ積む。鬼が来たら石は崩れて HP が減り、false（damage に受けたダメージ）。</summary>
        public bool StackStone(out int damage)
        {
            damage = 0;
            if (!CanStackStone) return false;
            if (random.Event.Next(6) < KawaraOniChance)
            {
                damage = player.TakeAttack(KawaraDamage);
                KawaraStones = 0;
                return false;
            }
            KawaraStones++;
            return true;
        }

        /// <summary>積んだ数のごほうびの説明（1：お守り・2：お守りと30 G・3：レリック）。</summary>
        public static string KawaraRewardText(int stones) =>
            stones >= 3 ? "レリック" : stones == 2 ? $"お守りと {KawaraGold} G" : stones == 1 ? "お守り" : "なし";

        /// <summary>やめて、積んだ数のごほうびをもらう。得たものを返す（なければ空）。</summary>
        public List<string> TakeKawaraReward()
        {
            var got = new List<string>();
            int stones = KawaraStones;
            KawaraStones = 0;
            if (stones >= 3)
            {
                var relic = PickRelic();
                if (relic != null)
                {
                    AddRelic(relic);
                    got.Add(relic.displayName);
                }
                else got.Add($"{GainGold(KawaraGold * 3)} G");
                return got;
            }
            if (stones >= 1)
            {
                var charm = CanAddCharm ? PickCharm(random.Event) : null;
                if (charm != null && AddCharm(charm)) got.Add(charm.displayName);
                else got.Add($"{GainGold(KawaraGold)} G"); // お守りがいっぱいならゴールド
            }
            if (stones >= 2) got.Add($"{GainGold(KawaraGold)} G");
            return got;
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
