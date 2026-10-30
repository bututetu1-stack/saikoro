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
                // 振り出しの札：協力ではみんなで同じマスにいるので出さない（引いた人だけスタートに戻り、AI の仲間が動けなくなっていた）
                case EventKind.StartOverCard: if (layer > s.startOverMaxLayer || multiplayer) return false; break;
                case EventKind.Tsukumogami: if (layer < s.tsukumogamiMinLayer) return false; break;
                case EventKind.Bonfire:
                case EventKind.Feast: if (!multiplayer) return false; break;
                // 鶴の恩返し：恩返しは次の層に着いたときなので、最後の層では出さない
                case EventKind.Crane: if (IsFinalLayer) return false; break;
                // 次の層に持ち越すイベントは、最後の層では出さない
                case EventKind.Tennyo:
                case EventKind.Moneylender:
                case EventKind.Seedling:
                case EventKind.KasaJizo: if (IsFinalLayer || multiplayer) return false; break;
                // 夕立：次の移動の出目が変わるので、みんなで同じ出目を使う協力では出さない
                case EventKind.Yuudachi: if (multiplayer) return false; break;
                case EventKind.OniTreasure: if (layer < 2) return false; break;
                // 竜宮城：ターンが進むので、みんなでターンをそろえて進む協力では出さない
                case EventKind.Ryugu: if (multiplayer) return false; break;
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
            if (!pouch.All.Contains(die)) throw new ArgumentException("賽袋にないダイスです。", nameof(die));
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

        /// <summary>offer を捧げて（賽袋から消える）、copy を刻印ごと複製する。複製したダイスを返す。</summary>
        public DiceInstance OfferAndDuplicate(DiceInstance offer, DiceInstance copy)
        {
            if (offer == copy) throw new ArgumentException("捧げるダイスと複製するダイスは別にしてください。");
            if (!pouch.All.Contains(offer) || !pouch.All.Contains(copy)) throw new ArgumentException("賽袋にないダイスです。");
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
            if (!pouch.All.Contains(die)) throw new ArgumentException("賽袋にないダイスです。", nameof(die));
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
            if (!pouch.All.Contains(die)) throw new ArgumentException("賽袋にないダイスです。", nameof(die));
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

        /// <summary>懐を探る：ゴールドを得るが、呪いのダイスを押し付けられる（賽袋が満杯なら入らない）。</summary>
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
        public const int KawaraDamage = 14;
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

        // ---- 雀のお宿（昔話「舌切り雀」。小さいつづらは控えめで安心、大きいつづらは宝と妖怪） ----

        // TODO(仕様): つづらの中身は仮
        public const int SuzumeSmallGold = 40;

        /// <summary>小さいつづら：お守り1つ（いっぱいならゴールド）と 40 G。得たもの。</summary>
        public List<string> OpenSmallBasket()
        {
            var got = new List<string>();
            var charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && AddCharm(charm)) got.Add(charm.displayName);
            got.Add($"{GainGold(SuzumeSmallGold)} G");
            return got;
        }

        /// <summary>大きいつづら：レリック1つ。ただし妖怪も飛び出して、呪いのダイスを押し付けられる。得たもの・押し付けられたもの。</summary>
        public List<string> OpenLargeBasket()
        {
            var got = new List<string>();
            var relic = PickRelic();
            if (relic != null)
            {
                AddRelic(relic);
                got.Add($"レリック「{relic.displayName}」");
            }
            var curses = config.curseDicePool.FindAll(d => d != null);
            if (curses.Count == 0 && config.curseDice != null) curses.Add(config.curseDice);
            if (curses.Count > 0 && CanAddDice)
            {
                var curse = curses[random.Event.Next(curses.Count)];
                AddDice(curse);
                got.Add($"呪いの {curse.displayName}");
            }
            return got;
        }

        // ---- 鶴の恩返し（いま助けると、次の層に着いたときに恩返しが届く。遅れて来るごほうび） ----

        // TODO(仕様): 手当ての代金と恩返しの中身は仮
        public const int CraneCost = 30;

        /// <summary>鶴を助けていて、次の層に着いたら恩返しがある。</summary>
        public bool CraneGratitude { get; private set; }

        public bool CanHelpCrane => Gold >= CraneCost && !CraneGratitude;

        /// <summary>鶴を助ける（手当ての代金を払う）。助けられなければ false。</summary>
        public bool HelpCrane()
        {
            if (!CanHelpCrane || !SpendGold(CraneCost)) return false;
            CraneGratitude = true;
            return true;
        }

        /// <summary>恩返し（次の層に着いたとき）：レリック1つ（なければゴールド）。受け取ったものの名前。</summary>
        string ReceiveCraneGift()
        {
            CraneGratitude = false;
            var relic = PickRelic();
            if (relic != null)
            {
                AddRelic(relic);
                return $"レリック「{relic.displayName}」";
            }
            return $"{GainGold(CraneCost * 4)} G";
        }

        /// <summary>セーブから戻すとき用。</summary>
        void RestoreCraneGratitude(bool value) => CraneGratitude = value;

        // ---- 2026-10-16 追加のイベント（イベント限定のダイス・次の層に持ち越すもの）。TODO(仕様): 数値はすべて仮 ----

        public const int YoutouMaxHpCost = 6;
        public const int YoutouPrayHeal = 6;
        public const int TempleSutraCost = 20;
        public const int TempleRestHeal = 14;
        public const int TennyoKeepGold = 60;
        public const int TennyoMaxHp = 6;
        public const int ZashikiSweetsCost = 15;
        public const int ZashikiLeaveGold = 10;
        public const int StarGold = 50;
        public const int MoneylenderLoan = 40;
        public const int MoneylenderReturn = 90;
        public const int SeedlingCost = 20;
        public const int SeedlingMaxHp = 10;
        public const int KappaReturnHeal = 12;
        public const int TamamushiCost = 30;

        /// <summary>イベントでしか手に入らないダイス（id で探す。なければ null）。</summary>
        public DiceData EventDice(string id) => config.eventDicePool.FirstOrDefault(d => d != null && d.id == id);

        /// <summary>イベント限定のダイスを受け取れるか（データがあり、賽袋に空きがある）。</summary>
        public bool CanTakeEventDice(string id) => EventDice(id) != null && CanAddDice;

        /// <summary>イベント限定のダイスを受け取る。受け取れなければ false。</summary>
        public bool TakeEventDice(string id)
        {
            var d = EventDice(id);
            if (d == null || !CanAddDice) return false;
            AddDice(d);
            return true;
        }

        // 次の層に着いたときに届くもの（天女の羽衣・貸し金・苗木）。セーブにも残す
        readonly List<string> layerPromises = new List<string>();
        public IReadOnlyList<string> LayerPromises => layerPromises;
        void RestoreLayerPromises(IEnumerable<string> ids) { layerPromises.Clear(); if (ids != null) layerPromises.AddRange(ids); }

        /// <summary>次の層に着いたとき：約束のものを受け取る。届いたものの説明（なければ空）。</summary>
        List<string> ReceiveLayerPromises()
        {
            var got = new List<string>();
            foreach (var id in layerPromises)
            {
                switch (id)
                {
                    case "tennyo":
                        player.maxHp += TennyoMaxHp;
                        player.Heal(TennyoMaxHp);
                        got.Add(TakeEventDice("tennyo") ? $"天女が羽衣のお礼に、天女賽と最大HP+{TennyoMaxHp}をくれた" : $"天女が羽衣のお礼に、最大HP+{TennyoMaxHp}をくれた（賽袋が満杯で天女賽は受け取れなかった）");
                        break;
                    case "loan":
                        got.Add($"両替商が貸し金を {GainGold(MoneylenderReturn)} G にして返しに来た");
                        break;
                    case "seedling":
                        player.maxHp += SeedlingMaxHp;
                        player.Heal(SeedlingMaxHp);
                        got.Add($"植えた苗が実をつけた（最大HP+{SeedlingMaxHp}）");
                        break;
                    case "jizo":
                        got.Add($"笠地蔵が夜のうちにお礼を届けてくれた（{GiveCharm()}・{GiveCharm()}）");
                        break;
                }
            }
            layerPromises.Clear();
            return got;
        }

        /// <summary>妖刀の祠：妖刀賽をもらう（最大HPが減る）。</summary>
        public bool TakeYoutou()
        {
            if (!CanTakeEventDice("youtou")) return false;
            player.maxHp = Math.Max(1, player.maxHp - YoutouMaxHpCost);
            player.hp = Math.Min(player.hp, player.maxHp);
            return TakeEventDice("youtou");
        }

        public int PrayAtYoutou() { int before = player.hp; player.Heal(YoutouPrayHeal); return player.hp - before; }

        public bool CanLearnSutra => Gold >= TempleSutraCost && CanTakeEventDice("kyoumon");
        public bool LearnSutra() => CanLearnSutra && SpendGold(TempleSutraCost) && TakeEventDice("kyoumon");
        public int RestAtTemple() { int before = player.hp; player.Heal(TempleRestHeal); return player.hp - before; }

        /// <summary>天女の羽衣：返す（次の層に着いたら天女賽と最大HP）。</summary>
        public void ReturnRobe() => layerPromises.Add("tennyo");
        /// <summary>天女の羽衣：持っていく（ゴールドと呪い）。押し付けられた呪いのダイス（なければ null）。</summary>
        public DiceInstance KeepRobe(out int gold)
        {
            gold = GainGold(TennyoKeepGold);
            return ForceCurse();
        }

        public bool CanGiveSweets => Gold >= ZashikiSweetsCost && CanTakeEventDice("zashiki");
        public bool GiveSweets() => CanGiveSweets && SpendGold(ZashikiSweetsCost) && TakeEventDice("zashiki");
        public int LeaveZashiki() => GainGold(ZashikiLeaveGold);

        /// <summary>流れ星：0 星降る賽・1 全回復・2 ゴールド。起きたことの説明。</summary>
        public string WishOnStar(int wish)
        {
            switch (wish)
            {
                case 0: return TakeEventDice("hoshifuri") ? "星降る賽が空から落ちてきた" : "何も起きなかった";
                case 1: { int before = player.hp; player.Heal(player.maxHp); return $"体が軽くなった（HP +{player.hp - before}）"; }
                default: return $"足もとに {GainGold(StarGold)} G が落ちていた";
            }
        }

        /// <summary>鬼の落とし物：金棒を拾う（鬼の金棒賽と呪い）。押し付けられた呪い（なければ null）。</summary>
        public bool TakeOniClub(out DiceInstance curse)
        {
            curse = null;
            if (!TakeEventDice("kanabou")) return false;
            curse = ForceCurse();
            return true;
        }

        public bool CanLend => Gold >= MoneylenderLoan;
        public bool Lend()
        {
            if (!CanLend || !SpendGold(MoneylenderLoan)) return false;
            layerPromises.Add("loan");
            return true;
        }

        public bool CanPlant => Gold >= SeedlingCost;
        public bool Plant()
        {
            if (!CanPlant || !SpendGold(SeedlingCost)) return false;
            layerPromises.Add("seedling");
            return true;
        }

        public int ReturnKappaPlate() { int before = player.hp; player.Heal(KappaReturnHeal); return player.hp - before; }
        public bool TakeKappaPlate() => TakeEventDice("kappazara");

        public bool CanBuyTamamushi => Gold >= TamamushiCost && CanTakeEventDice("tamamushi");

        // ---- 辻占い・祭り囃子（2026-10-16 追加）。TODO(仕様): 数値・確率は仮 ----
        public const int FortuneCost = 15;
        public const int FortuneMaxHp = 6;
        public const int FortuneGold = 40;
        public const int FestivalDanceHp = 6;
        public const int FestivalWatchHeal = 8;

        public bool CanHaveFortune => Gold >= FortuneCost;

        /// <summary>辻占い：大吉（40%：最大HP）・吉（40%：ゴールド）・凶（20%：呪いのダイス）。結果の説明。</summary>
        public string HaveFortune()
        {
            if (!CanHaveFortune || !SpendGold(FortuneCost)) return "占ってもらえなかった";
            int roll = random.Event.Next(100);
            if (roll < 40)
            {
                player.maxHp += FortuneMaxHp;
                player.Heal(FortuneMaxHp);
                return $"大吉！ 最大HP+{FortuneMaxHp}";
            }
            if (roll < 80) return $"吉。{GainGold(FortuneGold)} G が手に入るという……本当に足もとに落ちていた";
            var curse = ForceCurse();
            return curse != null ? $"凶……{curse.DisplayName}を押し付けられた" : "凶……だが何も起きなかった";
        }

        /// <summary>祭り囃子：踊る（HP を減らしてお守り1つ）。受け取ったお守り（いっぱいなら null）。</summary>
        public CharmData DanceAtFestival()
        {
            player.LoseHp(Math.Min(FestivalDanceHp, Math.Max(0, player.hp - 1)));
            var charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null) AddCharm(charm);
            return charm;
        }

        public int WatchFestival() { int before = player.hp; player.Heal(FestivalWatchHeal); return player.hp - before; }

        // ---- 雷様の落とし物・山姥の宿（2026-10-16 追加）。TODO(仕様): 数値・確率は仮 ----
        public const string RaikouCharmId = "raijinnotaiko";
        public const int RaikouKeepGold = 60;
        public const int RaikouKeepDamage = 8;
        public const int YamanbaCursePercent = 40;

        /// <summary>雷様の落とし物：太鼓を返す（お守り「雷神の太鼓」。いっぱいならゴールド）。受け取ったものの説明。</summary>
        public string ReturnDrum()
        {
            var charm = config.charmPool.FirstOrDefault(c => c != null && c.id == RaikouCharmId);
            if (charm != null && CanAddCharm && AddCharm(charm)) return $"お守り「{charm.displayName}」";
            return $"{GainGold(RaikouKeepGold / 2)} G";
        }

        /// <summary>雷様の落とし物：持っていく（ゴールド。ただし雷が落ちる）。失った HP。</summary>
        public int KeepDrum(out int gold)
        {
            gold = GainGold(RaikouKeepGold);
            return player.LoseHp(Math.Min(RaikouKeepDamage, Math.Max(0, player.hp - 1)));
        }

        // ---- 選ぶだけの小さなイベント（2026-10-16 さらに追加）。どれも起きたことの説明を返す。TODO(仕様): 数値・確率は仮 ----
        public const int FishingCost = 10, InariCost = 40, SentouCost = 15, SentouHeal = 20, AmazakeCost = 10, AmazakeHeal = 15;
        public const int MochiHpCost = 4, MochiMaxHp = 6, MochiEatHeal = 8, HanamiHeal = 12, HanamiCost = 10, HanamiWatchHeal = 4;
        public const int TenguHpCost = 10, KiyomizuHpCost = 15, KiyomizuMaxHp = 8, KiyomizuGold = 80, KiyomizuLookHeal = 5;
        public const int TanabataStarHeal = 10, HotaruHeal = 8, YakushiHeal = 10, BakasareGain = 60, BakasareLoss = 30;

        int HealBy(int amount) { int before = player.hp; player.Heal(amount); return player.hp - before; }
        int HurtBy(int amount) => player.LoseHp(Math.Min(amount, Math.Max(0, player.hp - 1))); // イベントでは倒れない
        string GiveCharm()
        {
            var charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && AddCharm(charm)) return $"お守り「{charm.displayName}」を手に入れた";
            return $"{GainGold(15)} G を手に入れた";
        }
        string GiveRelic()
        {
            var relic = RelicPool.Exists(RelicCanAppear) ? PickRelic() : null;
            if (relic != null) { AddRelic(relic); return $"レリック「{relic.displayName}」を手に入れた"; }
            return $"{GainGold(50)} G を手に入れた";
        }

        public string TanabataWish() => "短冊に願いを書くと、" + GiveCharm();
        public string TanabataStars() => $"天の川を眺めて心が晴れた（HP +{HealBy(TanabataStarHeal)}）";
        public bool CanFish => Gold >= FishingCost;
        public string Fish()
        {
            if (!SpendGold(FishingCost)) return "釣り竿を借りられなかった";
            int roll = random.Event.Next(100);
            if (roll < 60) return $"鯛が釣れて、売ると {GainGold(30)} G になった";
            if (roll < 90) return "針に引っかかってきたのは……" + GiveCharm();
            return "大物の腹から宝が出てきた！ " + GiveRelic();
        }
        public bool CanOfferAburaage => Gold >= InariCost;
        public string OfferAburaage() => SpendGold(InariCost) ? "白狐が現れ、" + GiveRelic() : "お供えできなかった";
        public string HelpMochitsuki() { int lost = HurtBy(MochiHpCost); player.maxHp += MochiMaxHp; player.Heal(MochiMaxHp); return $"杵をふるって汗をかいた（HP −{lost}）。つきたての餅で最大HP+{MochiMaxHp}"; }
        public string EatMochi() => $"お餅をごちそうになった（HP +{HealBy(MochiEatHeal)}）";
        public bool CanUseSentou => Gold >= SentouCost;
        public string UseSentou() => SpendGold(SentouCost) ? $"熱い湯につかった（HP +{HealBy(SentouHeal)}）" : "入れなかった";
        public string FollowTanuki()
        {
            if (random.Event.Next(100) < 50) return $"狸の宝の隠し場所に案内された（{GainGold(BakasareGain)} G）";
            int lost = Math.Min(Gold, BakasareLoss);
            SpendGold(lost);
            return $"気がつくと財布が軽くなっていた（{lost} G 失った）";
        }
        public string JoinHanami() { int heal = HealBy(HanamiHeal); int pay = Math.Min(Gold, HanamiCost); SpendGold(pay); return $"花の下で飲んで歌った（HP +{heal}・{pay} G 払った）"; }
        public string WatchHanami() => $"遠くから桜を眺めた（HP +{HealBy(HanamiWatchHeal)}）";
        public string PrayAtYakushi()
        {
            var curse = pouch.All.FirstOrDefault(d => d.data != null && d.data.rarity == Rarity.Curse);
            if (curse != null) { DiscardDice(curse); return $"薬師如来に祈ると、{curse.DisplayName}が消えた"; }
            return $"薬師如来に祈ると、体が軽くなった（HP +{HealBy(YakushiHeal)}）";
        }
        public string TakeTenguTrial() { int lost = HurtBy(TenguHpCost); return $"天狗と打ち合った（HP −{lost}）。腕を認められ、" + GiveRelic(); }
        public string JumpFromKiyomizu() { int lost = HurtBy(KiyomizuHpCost); player.maxHp += KiyomizuMaxHp; return $"清水の舞台から飛び降りた（HP −{lost}）。度胸がついて最大HP+{KiyomizuMaxHp}、{GainGold(KiyomizuGold)} G を拾った"; }
        public string LookFromKiyomizu() => $"舞台から町を見下ろした（HP +{HealBy(KiyomizuLookHeal)}）";
        public string WatchHotaru() => $"蛍の光に見とれた（HP +{HealBy(HotaruHeal)}）";
        public string CatchHotaru() => "蛍を追いかけると、草むらで" + GiveCharm();
        // ---- 選ぶだけの小さなイベント その3（2026-10-16）。TODO(仕様): 数値・確率は仮 ----
        public const int KamaitachiHpCost = 6, KamaitachiGold = 40, KamaitachiWaitHeal = 5;
        public const int DarumaCost = 30, DarumaMaxHp = 5;
        public const int UshinokokuGold = 80;
        public const int SumoHpCost = 8, SumoCheerHeal = 4;
        public const int BakenekoHeal = 8, BakenekoRelicPercent = 50;
        public const int MomijiHeal = 10, MomijiHpCost = 15, MomijiRelicPercent = 50;
        public const int EnnichiCost = 10, EnnichiHitPercent = 50;
        public const int KaguraHpCost = 5, KaguraForge = 2, KaguraWatchHeal = 6;
        public const int ZeniAraiPercent = 25, ZeniAraiMax = 40, ZeniAraiPrayHeal = 5;
        public const int YukiOnnaMaxHpCost = 4, YukiOnnaHpCost = 6;
        public const int TsukimiCost = 10, TsukimiHeal = 8;

        public string RunThroughKamaitachi() { int lost = HurtBy(KamaitachiHpCost); return $"風の刃に切られながら駆け抜けた（HP −{lost}）。峠の向こうで {GainGold(KamaitachiGold)} G を拾った"; }
        public string WaitKamaitachi() => $"風がやむまで岩陰で休んだ（HP +{HealBy(KamaitachiWaitHeal)}）";
        public bool CanBuyDaruma => Gold >= DarumaCost;
        public string BuyDaruma()
        {
            if (!SpendGold(DarumaCost)) return "買えなかった";
            player.maxHp += DarumaMaxHp;
            player.Heal(DarumaMaxHp);
            return $"達磨に片目を入れた。七転び八起き（最大HP +{DarumaMaxHp}）";
        }
        public string TakeNail() { int gold = GainGold(UshinokokuGold); var curse = ForceCurse(); return $"五寸釘のそばの銭を {gold} G 拾った" + (curse != null ? $"。だが {curse.DisplayName} が賽袋に入っていた" : ""); }
        public string Sumo() { int lost = HurtBy(SumoHpCost); return $"金太郎に投げ飛ばされた（HP −{lost}）。いい勝負だったと、" + GiveCharm(); }
        public string CheerSumo() => $"土俵の外から声援を送った（HP +{HealBy(SumoCheerHeal)}）";
        public string BuyBakenekoDango() => $"団子だけ買って、歩きながら食べた（HP +{HealBy(BakenekoHeal)}）";
        public string DrinkBakenekoTea()
        {
            if (random.Event.Next(100) < BakenekoRelicPercent) return "看板娘はにっこり笑い、お土産に" + GiveRelic().Replace("を手に入れた", "をくれた");
            var curse = ForceCurse();
            return curse != null ? $"化かされた！ 気がつくと {curse.DisplayName} が賽袋に入っていた" : "化かされて、茶屋ごと消えてしまった";
        }
        public string WatchMomiji() => $"真っ赤な紅葉を眺めた（HP +{HealBy(MomijiHeal)}）";
        public string DrinkWithKijo()
        {
            if (random.Event.Next(100) < MomijiRelicPercent) return "鬼女はすっかり上機嫌で、" + GiveRelic();
            return $"鬼女が正体を現した！ 命からがら逃げ出した（HP −{HurtBy(MomijiHpCost)}）";
        }
        public bool CanShootAtEnnichi => Gold >= EnnichiCost;
        public string ShootAtEnnichi()
        {
            if (!SpendGold(EnnichiCost)) return "撃てなかった";
            return random.Event.Next(100) < EnnichiHitPercent ? "見事に当たって、" + GiveCharm() : "惜しくも外れた";
        }
        public string StayAtKakurezato() { int pay = Gold / 2; SpendGold(pay); int before = player.hp; player.Heal(player.maxHp); return $"隠れ里でもてなされた（HP +{player.hp - before}）。お礼に {pay} G 置いてきた"; }
        public string OfferKagura()
        {
            int lost = HurtBy(KaguraHpCost);
            int forged = 0;
            for (int i = 0; i < KaguraForge; i++) if (FeastTalk(out _, out _) != null) forged++;
            return forged > 0 ? $"汗だくで舞った（HP −{lost}）。いちばん弱いダイスの面が +{forged} 鍛えられた" : $"汗だくで舞った（HP −{lost}）";
        }
        public string WatchKagura() => $"神楽を見物した（HP +{HealBy(KaguraWatchHeal)}）";
        public int ZeniAraiGain => Math.Min(ZeniAraiMax, Gold * ZeniAraiPercent / 100);
        public string WashCoins() => $"銭を洗うと、不思議と増えた（{GainGold(ZeniAraiGain)} G）";
        public string PrayAtZeniArai() => $"弁天様に手を合わせた（HP +{HealBy(ZeniAraiPrayHeal)}）";
        public string ShelterWithYukiOnna()
        {
            int lose = Math.Min(YukiOnnaMaxHpCost, player.maxHp - 1);
            player.maxHp -= lose;
            if (player.hp > player.maxHp) player.hp = player.maxHp;
            int before = player.hp;
            player.Heal(player.maxHp);
            return $"雪女の小屋で温まった（HP +{player.hp - before}）。だが体の芯が冷えたまま（最大HP −{lose}）";
        }
        public string WalkThroughBlizzard() => $"吹雪の中を進んだ（HP −{HurtBy(YukiOnnaHpCost)}）";
        public bool CanOfferTsukimi => Gold >= TsukimiCost;
        public string OfferTsukimiDango() => SpendGold(TsukimiCost) ? "月見団子を供えると、月の兎が" + GiveCharm().Replace("を手に入れた", "を届けてくれた") : "供えられなかった";
        public string WatchMoon() => $"満月を眺めた（HP +{HealBy(TsukimiHeal)}）";

        // ---- 選ぶだけの小さなイベント その4（2026-10-16）。TODO(仕様): 数値・確率は仮 ----
        public const int JizoCost = 10, JizoPrayHeal = 3;
        public const int SanzaruHeal = 10, SanzaruGold = 30;
        public const int OchimushaCost = 20, OchimushaHeal = 10;
        public const int IgoBet = 20, IgoWin = 60, IgoWatchHeal = 5;
        public const int TerakoyaHpCost = 4, TerakoyaGold = 40, TerakoyaPlayHeal = 8;
        public const int ToyamaCost = 25;
        public const int YuudachiHeal = 6, YuudachiMove = 2;
        public const int TonchiGold = 40, TonchiRestHeal = 8;
        public const int BiwaHeal = 10, BiwaCost = 15;
        public const int ShishimaiCost = 10, ShishimaiMaxHp = 2, ShishimaiWatchHeal = 4;
        public const int HyakkiHpCost = 12, HyakkiHideHpCost = 3;

        public bool CanCoverJizo => Gold >= JizoCost;
        public string CoverJizo() { if (!SpendGold(JizoCost)) return "笠を買えなかった"; layerPromises.Add("jizo"); return "お地蔵さんに笠をかぶせてあげた。次の層に着くと、お礼が届くかもしれない"; }
        public string PrayToJizo() => $"手を合わせて通り過ぎた（HP +{HealBy(JizoPrayHeal)}）";
        public string SeeNoEvil() => $"見ざる：心が静まった（HP +{HealBy(SanzaruHeal)}）";
        public string SpeakNoEvil() => $"言わざる：口を慎むと、祠の裏に {GainGold(SanzaruGold)} G があった";
        public string HearNoEvil()
        {
            var curse = CurseToRemove;
            if (curse != null) { DiscardDice(curse); return $"聞かざる：呪いの声が聞こえなくなり、{curse.DisplayName}が消えた"; }
            return "聞かざる：静けさの中で、" + GiveCharm();
        }
        public bool CanMournOchimusha => Gold >= OchimushaCost;
        public string MournOchimusha()
        {
            if (!SpendGold(OchimushaCost)) return "供養できなかった";
            var curse = CurseToRemove;
            if (curse != null) { DiscardDice(curse); return $"亡霊は成仏し、{curse.DisplayName}も一緒に消えた"; }
            return $"亡霊は成仏した。心が軽くなった（HP +{HealBy(OchimushaHeal)}）";
        }
        public string TakeOchimushaSword() { string relic = GiveRelic(); var curse = ForceCurse(); return relic + (curse != null ? $"。だが亡霊の恨みで {curse.DisplayName} が賽袋に入った" : ""); }
        public bool CanPlayIgo => Gold >= IgoBet;
        public string PlayIgo()
        {
            if (!SpendGold(IgoBet)) return "賭けられなかった";
            return random.Event.Next(100) < 50 ? $"見事に勝った！ {GainGold(IgoWin)} G を受け取った" : $"仙人にはかなわなかった（{IgoBet} G 失った）";
        }
        public string WatchIgo() => $"仙人の碁を見物した（HP +{HealBy(IgoWatchHeal)}）";
        public string TeachTerakoya() { int lost = HurtBy(TerakoyaHpCost); return $"子どもたちに読み書きを教えた（HP −{lost}）。お礼に {GainGold(TerakoyaGold)} G"; }
        public string PlayAtTerakoya() => $"子どもたちと遊んだ（HP +{HealBy(TerakoyaPlayHeal)}）";
        /// <summary>富山の薬売りが置いていく薬（お守りの中の、回復の薬からランダム）。なければ null。</summary>
        CharmData ToyamaMedicine => config.charmPool.Where(c => c != null && c.kind == CharmKind.Heal).OrderBy(c => c.amount).Take(3).ElementAtOrDefault(random.Event.Next(3));
        public bool CanBuyToyama => Gold >= ToyamaCost && CanAddCharm && config.charmPool.Any(c => c != null && c.kind == CharmKind.Heal);
        public string BuyToyama()
        {
            var charm = ToyamaMedicine ?? config.charmPool.FirstOrDefault(c => c != null && c.kind == CharmKind.Heal);
            if (charm == null || !SpendGold(ToyamaCost)) return "買えなかった";
            AddCharm(charm);
            return $"置き薬の「{charm.displayName}」を買った";
        }
        public string ShelterFromYuudachi() => $"軒下で雨宿りした（HP +{HealBy(YuudachiHeal)}）";
        public string RunInYuudachi() { AddMovePenalty(-YuudachiMove); return $"ずぶぬれで駆け出した。次の移動の出目 +{YuudachiMove}"; }
        public string CrossTonchiBridge() => $"「端ではなく真ん中を渡りました」。番人が感心して {GainGold(TonchiGold)} G くれた";
        public string RestAtTonchiBridge() => $"橋のたもとで休んだ（HP +{HealBy(TonchiRestHeal)}）";
        public string ListenToBiwa() => $"琵琶の音に聞き入った（HP +{HealBy(BiwaHeal)}）";
        public bool CanDonateBiwa => Gold >= BiwaCost;
        public string DonateBiwa() => SpendGold(BiwaCost) ? "お布施をすると、法師が" + GiveCharm().Replace("を手に入れた", "をくれた") : "お布施できなかった";
        public bool CanShishimai => Gold >= ShishimaiCost;
        public string BittenByShishimai()
        {
            if (!SpendGold(ShishimaiCost)) return "頼めなかった";
            var curse = CurseToRemove;
            if (curse != null) { DiscardDice(curse); return $"獅子に頭をかまれて厄が落ち、{curse.DisplayName}が消えた"; }
            player.maxHp += ShishimaiMaxHp;
            player.Heal(ShishimaiMaxHp);
            return $"獅子に頭をかまれて、今年も元気に過ごせそうだ（最大HP +{ShishimaiMaxHp}）";
        }
        public string WatchShishimai() => $"獅子舞を見物した（HP +{HealBy(ShishimaiWatchHeal)}）";
        public string JoinHyakki() { int lost = HurtBy(HyakkiHpCost); return $"妖怪たちにもみくちゃにされた（HP −{lost}）。おみやげに {GiveCharm()}・{GiveCharm()}"; }
        public string HideFromHyakki() => $"草むらに隠れてやり過ごした（HP −{HurtBy(HyakkiHideHpCost)}）";

        // ---- 昔話のイベント その5（2026-10-16）。TODO(仕様): 数値・確率は仮 ----
        public const string IssunRelicId = "kozuchi";
        public const int IssunHpCost = 10, IssunSeeOffHeal = 4;
        public const int KaguyaHpCost = 3, KaguyaGold = 80, KaguyaWatchHeal = 6;
        public const int HanasakaWatchHeal = 8;
        public const int KobutoriMaxHpGain = 6, KobutoriMaxHpLoss = 3, KobutoriHideHeal = 3;
        public const int KachikachiHpCost = 8, KachikachiGold = 60;
        public const int OmusubiGold = 20, OmusubiEatHeal = 8;
        public const int BunbukuShowCost = 15, BunbukuShowHeal = 15, BunbukuKettleCost = 50;
        public const int MomotaroCost = 15, MomotaroEatHeal = 6;
        public const int SarukaniHpCost = 6, SarukaniShareHeal = 8;
        public const int HitotsumeCost = 10, HitotsumeHeal = 12, HitotsumeHpCost = 6;
        public const int NinjaHpCost = 8, NinjaGold = 25;
        public const int AmanojakuHpCost = 5;

        /// <summary>一寸法師：鬼退治を手伝う（HP が減る。打ち出の小槌、もう持っていれば別のレリック）。</summary>
        public string HelpIssunboshi()
        {
            int lost = HurtBy(IssunHpCost);
            var mallet = RelicPool.FirstOrDefault(r => r != null && r.id == IssunRelicId && RelicCanAppear(r));
            if (mallet != null) { AddRelic(mallet); return $"鬼と戦った（HP −{lost}）。鬼が落としたレリック「{mallet.displayName}」を手に入れた"; }
            return $"鬼と戦った（HP −{lost}）。お礼に" + GiveRelic();
        }
        public string SeeOffIssunboshi() => $"お椀の舟を見送った（HP +{HealBy(IssunSeeOffHeal)}）";
        public string CutShiningBamboo()
        {
            int lost = HurtBy(KaguyaHpCost);
            return random.Event.Next(100) < 50 ? $"竹を切ると、中から小判がこぼれた（HP −{lost}・{GainGold(KaguyaGold)} G）" : $"竹を切ると、中に光る包みがあった（HP −{lost}）。" + GiveCharm();
        }
        public string WatchShiningBamboo() => $"光る竹をそっと見守った（HP +{HealBy(KaguyaWatchHeal)}）";
        public string ScatterAsh() { var die = FeastTalk(out int before, out int after); return die != null ? $"枯れ木に灰をまくと花が咲いた。{die.DisplayName}の面が {before} → {after} に" : "灰をまくと花が咲いた"; }
        public string WatchHanasaka() => $"満開の花を眺めた（HP +{HealBy(HanasakaWatchHeal)}）";
        public string DanceWithOni()
        {
            if (random.Event.Next(100) < 50)
            {
                player.maxHp += KobutoriMaxHpGain;
                player.Heal(KobutoriMaxHpGain);
                return $"踊りが大うけで、鬼が体の重いこぶを取ってくれた（最大HP +{KobutoriMaxHpGain}）";
            }
            int lose = Math.Min(KobutoriMaxHpLoss, player.maxHp - 1);
            player.maxHp -= lose;
            if (player.hp > player.maxHp) player.hp = player.maxHp;
            return $"踊りが下手で、鬼にこぶを付けられた（最大HP −{lose}）";
        }
        public string HideFromOniDance() => $"木の陰から宴を眺めた（HP +{HealBy(KobutoriHideHeal)}）";
        public string CarryFirewood() { int lost = HurtBy(KachikachiHpCost); return $"背中の薪がかちかち鳴って燃え出した（HP −{lost}）。それでも運び切って {GainGold(KachikachiGold)} G"; }
        public string RollOmusubi() { string charm = GiveCharm(); return $"おむすびを穴に転がすと、ねずみたちがお礼に{charm.Replace("を手に入れた", "")}と {GainGold(OmusubiGold)} G をくれた"; }
        public string EatOmusubi() => $"おむすびを自分で食べた（HP +{HealBy(OmusubiEatHeal)}）";
        public bool CanSeeBunbukuShow => Gold >= BunbukuShowCost;
        public string SeeBunbukuShow() => SpendGold(BunbukuShowCost) ? $"茶釜の綱渡りに大笑いした（HP +{HealBy(BunbukuShowHeal)}）" : "入れなかった";
        public bool CanBuyBunbuku => Gold >= BunbukuKettleCost;
        public string BuyBunbuku() => SpendGold(BunbukuKettleCost) ? "茶釜を買うと、中から" + GiveRelic().Replace("を手に入れた", "が出てきた") : "買えなかった";
        /// <summary>きびだんごのお礼（力のお守りからランダム）。なければ null。</summary>
        CharmData MomotaroCharm => config.charmPool.Where(c => c != null && c.kind == CharmKind.GainStrength).OrderBy(c => c.amount).Take(3).ElementAtOrDefault(random.Event.Next(3));
        public bool CanShareKibidango => Gold >= MomotaroCost && CanAddCharm;
        public string ShareKibidango()
        {
            if (!SpendGold(MomotaroCost)) return "分けられなかった";
            var charm = MomotaroCharm ?? config.charmPool.FirstOrDefault(c => c != null && c.kind == CharmKind.GainStrength);
            if (charm != null && AddCharm(charm)) return $"犬・猿・雉がお礼に「{charm.displayName}」をくれた";
            return "犬・猿・雉がうれしそうについてきた";
        }
        public string EatKibidango() => $"きびだんごを食べた（HP +{HealBy(MomotaroEatHeal)}）";
        public string HelpCrab() { int lost = HurtBy(SarukaniHpCost); return $"臼と蜂と栗と一緒に猿をこらしめた（HP −{lost}）。お礼に {GiveCharm()}・{GiveCharm()}"; }
        public string SharePersimmons() => $"柿を分け合って食べた（HP +{HealBy(SarukaniShareHeal)}）";
        public bool CanBuyTofu => Gold >= HitotsumeCost;
        public string BuyTofuForHitotsume() => SpendGold(HitotsumeCost) ? $"豆腐を買ってあげると、一つ目小僧はにっこり笑った。一緒に食べた（HP +{HealBy(HitotsumeHeal)}）" : "買えなかった";
        public string StareAtHitotsume()
        {
            if (random.Event.Next(100) < 50) return "にらめっこに勝った！ 一つ目小僧が悔しがって、" + GiveCharm().Replace("を手に入れた", "を置いていった");
            return $"にらめっこに負けて、笑いすぎてお腹が痛い（HP −{HurtBy(HitotsumeHpCost)}）";
        }
        public string TrainAsNinja()
        {
            int lost = HurtBy(NinjaHpCost);
            var smoke = config.charmPool.FirstOrDefault(c => c != null && c.kind == CharmKind.Smoke);
            if (smoke != null && CanAddCharm && AddCharm(smoke)) return $"厳しい修行に耐えた（HP −{lost}）。忍びの「{smoke.displayName}」を授かった";
            return $"厳しい修行に耐えた（HP −{lost}）。" + GiveCharm();
        }
        public string PickUpShuriken() => $"落ちていた手裏剣を拾って売った（{GainGold(NinjaGold)} G）";
        public string RefuseAmanojaku() => "「ごほうびなんかいらない」と言うと、天邪鬼はむきになって" + GiveCharm().Replace("を手に入れた", "を押し付けてきた");
        public string AskAmanojaku() => $"「ごほうびがほしい」と言うと、天邪鬼は石を投げてきた（HP −{HurtBy(AmanojakuHpCost)}）";

        // ---- イベント その6（2026-10-16）。TODO(仕様): 数値・確率は仮 ----
        public const int TsuchinokoHpCost = 4, TsuchinokoGold = 100, TsuchinokoPercent = 30;
        public const int NueHpCost = 8, NueRestHeal = 3;
        public const int MikoshiHpCost = 6, MikoshiMaxHp = 3, MikoshiGold = 20, MikoshiWatchHeal = 5;
        public const int YabusameHpCost = 10, YabusameWatchHeal = 5;
        public const int KemariHpCost = 3, KemariGold = 30, KemariWatchHeal = 4;
        public const int KabukiCost = 20, KabukiHeal = 18, KabukiStandHeal = 5;
        public const int UkiyoeGold = 40, UkiyoeCost = 30;
        public const int GoraikouHpCost = 10, GoraikouMaxHp = 8, GoraikouPrayHeal = 6;
        public const int HoozukiCost = 20, HoozukiWalkHeal = 5;
        public const int KodamaHpCost = 4;
        public const int HitodamaHpCost = 5, HitodamaGold = 40, HitodamaPrayHeal = 4;
        public const int YukigassenHpCost = 5, YukidarumaHpCost = 2, YukidarumaMaxHp = 2;
        public const int GozenHpCost = 12, GozenGold = 100, GozenWatchHeal = 5;
        public const int KintsugiCost = 30;
        public const int TsunahikiHpCost = 5, TsunahikiGold = 30, TsunahikiCheerHeal = 5;
        public const int DaibutsuHeal = 15;

        public string SearchTsuchinoko()
        {
            int lost = HurtBy(TsuchinokoHpCost);
            return random.Event.Next(100) < TsuchinokoPercent ? $"いた！ ツチノコを捕まえて、見世物小屋に売った（HP −{lost}・{GainGold(TsuchinokoGold)} G）" : $"草むらを探し回ったが、見つからなかった（HP −{lost}）";
        }
        public string ShootNue() { int lost = HurtBy(NueHpCost); return $"闇に向けて弓を射ると、鵺が落ちてきた（HP −{lost}）。" + GiveRelic(); }
        public string CoverEarsFromNue() => $"耳をふさいで夜明けを待った（HP +{HealBy(NueRestHeal)}）";
        public string CarryMikoshi() { int lost = HurtBy(MikoshiHpCost); player.maxHp += MikoshiMaxHp; player.Heal(MikoshiMaxHp); return $"「わっしょい！」神輿を担いだ（HP −{lost}・最大HP +{MikoshiMaxHp}）。ご祝儀に {GainGold(MikoshiGold)} G"; }
        public string WatchMikoshi() => $"神輿を見物した（HP +{HealBy(MikoshiWatchHeal)}）";
        public string TryYabusame()
        {
            if (random.Event.Next(100) < 50) return "見事に的を射抜いた！ ほうびに" + GiveRelic().Replace("を手に入れた", "をもらった");
            return $"馬から落ちてしまった（HP −{HurtBy(YabusameHpCost)}）";
        }
        public string WatchYabusame() => $"流鏑馬を見物した（HP +{HealBy(YabusameWatchHeal)}）";
        public string JoinKemari() { int lost = HurtBy(KemariHpCost); return $"鞠を蹴り上げた（HP −{lost}）。貴族たちが喜んで {GainGold(KemariGold)} G くれた"; }
        public string WatchKemari() => $"蹴鞠を見物した（HP +{HealBy(KemariWatchHeal)}）";
        public bool CanSeeKabuki => Gold >= KabukiCost;
        public string SeeKabuki() => SpendGold(KabukiCost) ? $"見事な見得に心が躍った（HP +{HealBy(KabukiHeal)}）" : "入れなかった";
        public string StandAtKabuki() => $"立ち見で少しだけ観た（HP +{HealBy(KabukiStandHeal)}）";
        public string ModelForUkiyoe() => $"絵のモデルになった。お礼に {GainGold(UkiyoeGold)} G";
        public bool CanBuyUkiyoe => Gold >= UkiyoeCost && CanAddCharm;
        public string BuyUkiyoe() => SpendGold(UkiyoeCost) ? "縁起のいい浮世絵を買うと、絵の中から" + GiveCharm().Replace("を手に入れた", "が出てきた") : "買えなかった";
        public string ClimbForGoraikou() { int lost = HurtBy(GoraikouHpCost); player.maxHp += GoraikouMaxHp; player.Heal(GoraikouMaxHp); return $"夜通し登って御来光を拝んだ（HP −{lost}・最大HP +{GoraikouMaxHp}）"; }
        public string PrayAtFujiFoot() => $"ふもとから富士を拝んだ（HP +{HealBy(GoraikouPrayHeal)}）";
        public bool CanBuyHoozuki => Gold >= HoozukiCost && CanAddCharm;
        public string BuyHoozuki() => SpendGold(HoozukiCost) ? "ほおずきの鉢を買うと、" + GiveCharm().Replace("を手に入れた", "がおまけについてきた") : "買えなかった";
        public string WalkHoozuki() => $"ほおずき市を見て回った（HP +{HealBy(HoozukiWalkHeal)}）";
        public string CallToKodama()
        {
            if (random.Event.Next(100) < 50) return "「おーい」と呼ぶと、木霊が答えて" + GiveCharm().Replace("を手に入れた", "を落としてくれた");
            return $"声が森にこだまして、道に迷った（HP −{HurtBy(KodamaHpCost)}）";
        }
        public string ChaseHitodama() { int lost = HurtBy(HitodamaHpCost); return $"人魂を追いかけると、古い墓のそばに銭が埋まっていた（HP −{lost}・{GainGold(HitodamaGold)} G）"; }
        public string PrayToHitodama() => $"人魂に手を合わせると、すうっと消えた（HP +{HealBy(HitodamaPrayHeal)}）";
        public string JoinYukigassen() { int lost = HurtBy(YukigassenHpCost); return $"子どもたちと雪合戦をした（HP −{lost}）。勝ったほうびに " + GiveCharm(); }
        public string BuildYukidaruma() { int lost = HurtBy(YukidarumaHpCost); player.maxHp += YukidarumaMaxHp; player.Heal(YukidarumaMaxHp); return $"大きな雪だるまを作った（HP −{lost}・最大HP +{YukidarumaMaxHp}）"; }
        public string FightGozenJiai() { int lost = HurtBy(GozenHpCost); return $"殿様の前で見事に戦った（HP −{lost}）。ほうびに {GainGold(GozenGold)} G"; }
        public string WatchGozenJiai() => $"御前試合を見物した（HP +{HealBy(GozenWatchHeal)}）";
        public bool CanKintsugi => Gold >= KintsugiCost && TransformTarget != null;
        public string AskKintsugi()
        {
            var oldDie = TransformTarget;
            if (oldDie == null || !SpendGold(KintsugiCost)) return "頼めなかった";
            var newDie = OfferToTsukumogami(oldDie);
            return newDie != null ? $"{oldDie.DisplayName}を金継ぎしてもらうと、{newDie.DisplayName}に生まれ変わった" : "金継ぎしてもらったが、何も変わらなかった";
        }
        public string JoinTsunahiki() { int lost = HurtBy(TsunahikiHpCost); return $"「よいしょ！」綱を引いた（HP −{lost}）。勝って {GainGold(TsunahikiGold)} G"; }
        public string CheerTsunahiki() => $"綱引きを応援した（HP +{HealBy(TsunahikiCheerHeal)}）";
        public string NapOnDaibutsu() => $"大仏様の手のひらで昼寝をした（HP +{HealBy(DaibutsuHeal)}）";
        public string CrawlDaibutsuNose() => "大仏様の鼻の穴をくぐると、願いが叶うという。" + GiveCharm();

        public bool CanDrinkAmazake => Gold >= AmazakeCost;
        public string DrinkAmazake() => SpendGold(AmazakeCost) ? $"温かい甘酒を飲んだ（HP +{HealBy(AmazakeHeal)}）" : "飲めなかった";

        /// <summary>山姥の宿：泊まる（全回復。ただし 40% で呪いのダイス）。押し付けられた呪い（なければ null）。</summary>
        public DiceInstance StayWithYamanba(out int healed)
        {
            int before = player.hp;
            player.Heal(player.maxHp);
            healed = player.hp - before;
            return random.Event.Next(100) < YamanbaCursePercent ? ForceCurse() : null;
        }
        public bool BuyTamamushi() => CanBuyTamamushi && SpendGold(TamamushiCost) && TakeEventDice("tamamushi");

        // ---- 竜宮城（昔話「浦島太郎」。もてなしを受けると元気になるが、そのあいだに時が過ぎる） ----

        // TODO(仕様): 過ぎるターン数とおみやげは仮（ターン数は自己ベスト・ランキング・得点の早足に響く）
        public const int RyuguTurns = 5;
        public const int RyuguGold = 50;

        /// <summary>竜宮城でもてなしを受ける：HP が全回復し、玉手箱（お守り。いっぱいならゴールド）をもらうが、ターンが RyuguTurns 進む。おみやげの名前。</summary>
        public string StayAtRyugu()
        {
            for (int i = 0; i < RyuguTurns; i++)
            {
                stats.RecordHp(Turn, player.hp, player.maxHp, LayerIndex); // 結果のグラフ用
                Turn++;
            }
            player.Heal(player.maxHp);
            var charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && AddCharm(charm)) return charm.displayName;
            return $"{GainGold(RyuguGold)} G";
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
            if (!CanOfferToTsukumogami(die)) throw new ArgumentException("賽袋にないダイスです。", nameof(die));
            var rarity = TsukumogamiRarity(die);
            var candidates = config.rewardDicePool.Where(d => d != null && d.rarity == rarity && d != die.data).ToList();
            if (candidates.Count == 0) return null;
            var data = candidates[random.Event.Next(candidates.Count)];
            // 呪いのダイスも差し出せる（ReplaceDice は呪いを入れ替えられないので、ここで入れ替える）
            pouch.Remove(die);
            NotifyAcquired("discard", die.DisplayName);
            return AddDice(data, true);
        }

        // ---- チンチロ勝負（2026-10-17） ----
        // 親（賭場の親分）が先に振って役を見せる。子（自分）は賽袋の使用可能なダイスを1個ずつ3個振って役を作り、役の強さで勝負する。
        // TODO(仕様): 賭け金・配当は仮

        public const int ChinchiroBet = 30;
        public const int ChinchiroBigWinMultiplier = 2; // シゴロ・アラシ・ピンゾロで勝つと、賭け金の2倍を得る（ふつうの勝ちは賭け金ぶん）

        public bool CanChinchiro => Gold >= ChinchiroBet && pouch.AvailableCount >= 3;

        /// <summary>親が振る（ふつうの賽3個。役が出るまで3回まで振り直す。チンチロの決まり）。</summary>
        public int[] ChinchiroDealerRoll()
        {
            var values = new int[3];
            for (int t = 0; t < 3; t++)
            {
                for (int i = 0; i < 3; i++) values[i] = random.Event.Next(1, 7);
                if (Battle.DiceHands.Evaluate(values).kind != Battle.HandKind.None) break;
            }
            return values;
        }

        public enum ChinchiroOutcome { Win, Lose, Draw }

        /// <summary>役の強さで勝負。同じ役（役なし同士も）なら出目の合計の大きいほう、それも同じなら引き分け。</summary>
        public static ChinchiroOutcome CompareChinchiro(IReadOnlyList<int> dealer, IReadOnlyList<int> mine)
        {
            var d = Battle.DiceHands.Evaluate(dealer).kind;
            var m = Battle.DiceHands.Evaluate(mine).kind;
            if (m != d) return m > d ? ChinchiroOutcome.Win : ChinchiroOutcome.Lose;
            int ds = dealer.Sum(), ms = mine.Sum();
            if (ms != ds) return ms > ds ? ChinchiroOutcome.Win : ChinchiroOutcome.Lose;
            return ChinchiroOutcome.Draw;
        }

        /// <summary>勝負の精算。勝てば賭け金ぶん（大きな役なら2倍）を得て、負ければ賭け金を払う。ゴールドの増減を返す。</summary>
        public int SettleChinchiro(IReadOnlyList<int> dealer, IReadOnlyList<int> mine, out ChinchiroOutcome outcome)
        {
            outcome = CompareChinchiro(dealer, mine);
            switch (outcome)
            {
                case ChinchiroOutcome.Win:
                    bool big = Battle.DiceHands.Evaluate(mine).kind >= Battle.HandKind.Shigoro;
                    return GainGold(ChinchiroBet * (big ? ChinchiroBigWinMultiplier : 1));
                case ChinchiroOutcome.Lose:
                    return -LoseGold(ChinchiroBet);
                default:
                    return 0;
            }
        }
    }
}
