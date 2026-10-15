using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Tour
{
    /// <summary>国めぐりの旅人（プレイヤー）。HP・ポーチ・お守り・レリック・ゴールドは、1人用のランの仕組み（RunState）をそのまま使う。</summary>
    public class TourPlayer
    {
        public int index;
        public string name;
        public bool isAI;
        public RunState run;
        public TourNode node;
        public TourNode cameFrom;
        public int arrivals;
        public readonly List<TourCardKind> cards = new List<TourCardKind>();
        public int movePenalty;      // 足止め札（次の移動の出目から引く）
        public int lastRolled = -1;  // 直前の出目（鏡賽）
        public int bossDamage;       // 大ボスに与えたダメージ（勝ち方：大ボス）
        public int duelWins;         // 対決で勝った回数（おまけの賞）
        public int battleWins;       // 魔物に勝った回数（おまけの賞）
        public int cardsUsed;        // 使った札の数（おまけの賞）

        public int Gold => run.Gold;
    }

    /// <summary>はじめの旅人の設定。</summary>
    public struct TourPlayerSetup
    {
        public string name;
        public bool isAI;
        public DiceData starter;
    }

    /// <summary>止まったマスで起きたこと（画面に見せる）。</summary>
    public class TourLanding
    {
        public TourNode node;
        public readonly List<string> messages = new List<string>();
        public bool arrived;            // 目的地に一番乗り
        public bool battle;             // 魔物と戦う（終わったら FinishBattle）
        public EnemyData enemy;
        public TourTown town;           // 町に止まった（名物を買える・サイコロ屋）
        public List<(DiceData dice, int price)> diceOffer;   // 町のサイコロ屋の品（止まったときに決める。全員の端末で同じになるように）
        public List<TourCardKind> cardOffer;                  // 町の札屋の品
        public Effects.RelicData relicOffer;                  // 札屋の宝物（なければ null）
        public readonly List<TourPlayer> others = new List<TourPlayer>();  // 同じマスにいる人（対決を挑める）
        public string Message => string.Join("\n", messages);
    }

    /// <summary>おまけの賞。</summary>
    public class TourAward
    {
        public string title;
        public string reason;
        public TourPlayer player;
    }

    /// <summary>対決の結果。</summary>
    public class TourDuelResult
    {
        public TourPlayer attacker, defender;
        public List<(string die, int value)> attackerRolls = new List<(string, int)>();
        public List<(string die, int value)> defenderRolls = new List<(string, int)>();
        public readonly List<DiceInstance> attackerDice = new List<DiceInstance>();   // 振ったダイス（演出用）
        public readonly List<DiceInstance> defenderDice = new List<DiceInstance>();
        public int attackerTotal, defenderTotal;
        public TourPlayer winner;      // 引き分けなら null
        public int stolen;
    }

    /// <summary>
    /// 国めぐり（桃鉄風）：巨大マップを順番に1人ずつ振って進み、町の名物を買い、目的地に一番乗りを目指す。
    /// 移動のダイスはポーチから選び、使用済みサイクルはいつもどおり。札で相手を妨害でき、同じマスの相手には対決を挑める。
    /// 目的地に一番乗りがあると、いちばん遠い人に厄神がつく（手番のたびに悪さをする。ほかの人とすれ違うと移る）。
    /// 盤面の乱数（青・赤の額、札、出来事、目的地、厄神）は全員の端末で同じにするため、1つの乱数で順に使う。
    /// </summary>
    public class TourState
    {
        public readonly GameConfig config;
        public readonly TourSettings settings;
        public readonly TourMap map;
        public readonly List<TourPlayer> players = new List<TourPlayer>();
        public readonly TourGoal goal;
        public readonly int rounds;
        readonly Random rng;

        public int Round { get; private set; } = 1;
        public int TurnIndex { get; private set; }
        public TourPlayer Current => players[TurnIndex];
        public TourTown Destination { get; private set; }
        public int YakugamiHolder { get; private set; } = -1;
        public bool IsOver => Round > rounds;

        // この手番に使った札の効き目
        public bool ExpressThisTurn { get; private set; }
        public int TailwindThisTurn { get; private set; }

        public TourState(GameConfig config, TourSettings settings, int seed, TourGoal goal, int rounds, IReadOnlyList<TourPlayerSetup> setups)
        {
            this.config = config;
            this.settings = TourSettings.OrDefault(settings);
            this.goal = goal;
            this.rounds = Math.Max(1, rounds);
            rng = new SeededRandom(RunRandom.Mix(seed, 70));
            map = TourMapGenerator.Generate(new SeededRandom(RunRandom.Mix(seed, 71)), this.settings);

            // スタートは盤面のまん中に近い町
            var center = new UnityEngine.Vector2(map.towns.Average(t => t.node.position.x), map.towns.Average(t => t.node.position.y));
            var start = map.towns.OrderBy(t => (t.node.position - center).sqrMagnitude).First();
            for (int i = 0; i < setups.Count; i++)
            {
                var run = new RunState(config, RunRandom.Mix(seed, 80 + i), setups[i].starter);
                // 後の番の人ほど少し多く持って始める（先に動く人が有利だったので）
                int diff = this.settings.startGold + this.settings.laterSeatGold * i - run.Gold;
                if (diff > 0) run.GainGold(diff);
                else if (diff < 0) run.LoseGold(-diff);
                players.Add(new TourPlayer { index = i, name = setups[i].name, isAI = setups[i].isAI, run = run, node = start.node });
            }
            Destination = PickDestination(start);
        }

        // ---- 手番 ----

        /// <summary>手番のはじめ：厄神の悪さ。起きたことを返す（何もなければ null）。</summary>
        public string BeginTurn()
        {
            ExpressThisTurn = false;
            TailwindThisTurn = 0;
            JustBecameGreat = false;
            if (YakugamiHolder != TurnIndex) return null;
            // 同じ人に続けてとりついていると、大厄神に化ける
            YakugamiTurns++;
            string grow = "";
            if (!GreatYakugami && settings.greatYakugamiAfter > 0 && YakugamiTurns > settings.greatYakugamiAfter)
            {
                GreatYakugami = true;
                JustBecameGreat = true;
                grow = $"{Current.name} にとりついた厄神が、大厄神に化けた！\n";
            }
            // 厄除けの鈴：半分の確率で、いたずらを防ぐ
            if (HasRelic(Current, YakuyokeRelicId) && rng.Next(2) == 0) return grow + $"{Current.name} の厄除けの鈴が鳴って、{(GreatYakugami ? "大厄神" : "厄神")}のいたずらを防いだ！";
            return grow + (GreatYakugami ? GreatYakugamiMischief(Current) : YakugamiMischief(Current));
        }

        /// <summary>手番を終える。次の人へ。1巡したらターンが進み、決算のターンなら名物の収入。起きたことを返す。</summary>
        public string EndTurn()
        {
            string message = null;
            TurnIndex++;
            if (TurnIndex >= players.Count)
            {
                TurnIndex = 0;
                if (settings.settlementEvery > 0 && Round % settings.settlementEvery == 0) message = Settlement();
                Round++;
            }
            return message;
        }

        /// <summary>手番の人を i にする（大ボス決戦の順番。ターンは進めない）。</summary>
        public void ForceTurn(int i)
        {
            if (i >= 0 && i < players.Count) TurnIndex = i;
        }

        /// <summary>この手番に振れるダイスの数（急行札なら2個）。</summary>
        public int DiceThisTurn => ExpressThisTurn ? 2 : 1;

        /// <summary>移動のダイスを振る（使用済みになる。足止め札・追い風札の数を足す）。進む数を返す。</summary>
        public int RollMove(IReadOnlyList<DiceInstance> dice, out List<(DiceInstance die, int value)> rolls)
        {
            var p = Current;
            rolls = new List<(DiceInstance, int)>();
            int total = 0;
            foreach (var die in dice)
            {
                if (die == null || die.state != DiceState.Available || !RunState.CanMoveWith(die)) continue;
                int v = DiceRoller.Roll(die, rng, p.lastRolled, out _);
                p.lastRolled = v;
                rolls.Add((die, v));
                total += v;
                p.run.pouch.Use(die, false, false);
            }
            total += TailwindThisTurn - p.movePenalty;
            if (HasRelic(p, HayakagoRelicId)) total += 1; // 早駕籠
            p.movePenalty = 0;
            return Math.Max(1, total);
        }

        /// <summary>動かせるダイスがないとき（大賽だけなど）：使えるダイスを使用済みにして、1歩だけ進む。</summary>
        public int SkipRoll()
        {
            var p = Current;
            foreach (var d in p.run.pouch.Available.ToList()) p.run.pouch.Use(d, false, false);
            p.movePenalty = 0;
            return 1;
        }

        /// <summary>steps 歩で止まれるマスと道。目的地を通るときはそこで止まる。</summary>
        public Dictionary<TourNode, List<TourNode>> MoveOptions(int steps) =>
            map.Reach(Current.node, Current.cameFrom, steps, Destination != null ? Destination.node : null);

        /// <summary>道（path）をたどって進み、止まったマスのことを起こす。</summary>
        public TourLanding Move(List<TourNode> path)
        {
            var p = Current;
            var landing = new TourLanding();
            var prev = p.node;
            foreach (var n in path)
            {
                p.cameFrom = prev;
                p.node = n;
                prev = n;
                // 厄神：すれ違った人に移る（持っている人が通っても、持っていない人が持っている人のところを通っても）
                PassYakugami(p, landing);
            }
            return Land(landing);
        }

        /// <summary>飛脚札などで、ある町へ飛ぶ（その町に止まる）。</summary>
        public TourLanding WarpTo(TourTown town)
        {
            var p = Current;
            p.cameFrom = null;
            p.node = town.node;
            var landing = new TourLanding();
            PassYakugami(p, landing);
            return Land(landing);
        }

        TourLanding Land(TourLanding landing)
        {
            var p = Current;
            var node = p.node;
            landing.node = node;
            landing.others.AddRange(players.Where(o => o != p && o.node == node));

            // 目的地
            if (Destination != null && node == Destination.node)
            {
                landing.arrived = true;
                p.arrivals++;
                int far = map.Distance(map.towns[0].node, Destination.node); // だいたいの広さ
                int bonus = Grow(settings.arrivalBonusBase) + settings.arrivalBonusPerStep * Math.Min(far, 30);
                int got = p.run.GainGold(bonus);
                landing.messages.Add($"目的地「{Destination.name}」に一番乗り！ {got} G をもらった。");
                // 厄神は、目的地からいちばん遠い人につく
                var old = Destination;
                var dist = map.DistancesFrom(old.node);
                var farthest = players.Where(o => o != p).OrderByDescending(o => dist.TryGetValue(o.node, out int d) ? d : 0).ThenBy(o => o.index).FirstOrDefault();
                if (farthest != null && YakugamiHolder != farthest.index)
                {
                    SetYakugamiHolder(farthest.index, true);
                    landing.messages.Add($"いちばん遠くにいた {farthest.name} に、厄神がとりついた！");
                }
                Destination = PickDestination(old);
                landing.messages.Add($"次の目的地は「{Destination.name}」。");
            }

            switch (node.kind)
            {
                case TourTileKind.Town:
                    landing.town = map.towns[node.town];
                    // 店の品は止まったときに決める（乱数を使う順番を、だれの端末でも同じにする）
                    if (landing.town.diceShop) landing.diceOffer = DiceShopOffer();
                    else landing.cardOffer = CardShopOffer();
                    // 札屋には、ときどき国めぐりの宝物（レリック）も並ぶ
                    if (!landing.town.diceShop) landing.relicOffer = RelicShopOffer(p);
                    if (!landing.arrived) landing.messages.Add($"{landing.town.name}に着いた。");
                    break;
                case TourTileKind.Blue:
                {
                    int got = p.run.GainGold(Grow(rng.Next(settings.blueMin, settings.blueMax + 1)));
                    landing.messages.Add($"青のマス：{got} G をもらった。");
                    break;
                }
                case TourTileKind.Red:
                {
                    int lost = p.run.LoseGold(Grow(rng.Next(settings.redMin, settings.redMax + 1)));
                    landing.messages.Add($"赤のマス：{lost} G を取られた。");
                    break;
                }
                case TourTileKind.Yellow:
                {
                    var card = TourCards.Pick(rng);
                    landing.messages.Add(GiveCard(p, card) ? $"黄のマス：{TourCards.Name(card)}を手に入れた。" : $"黄のマス：{TourCards.Name(card)}…… 札がいっぱいで持てなかった。");
                    break;
                }
                case TourTileKind.Shrine:
                {
                    int before = p.run.player.hp;
                    p.run.player.Heal(Math.Max(1, p.run.player.maxHp * settings.shrineHealPercent / 100));
                    string msg = $"祠でお参り：HP が {p.run.player.hp - before} 回復した。";
                    if (rng.Next(100) < settings.shrineCardChance)
                    {
                        var card = TourCards.Pick(rng);
                        if (GiveCard(p, card)) msg += $"　{TourCards.Name(card)}を授かった。";
                    }
                    landing.messages.Add(msg);
                    break;
                }
                case TourTileKind.Battle:
                    landing.battle = true;
                    landing.enemy = PickEnemy();
                    landing.messages.Add($"{landing.enemy.displayName} が現れた！");
                    break;
                case TourTileKind.Event:
                    landing.messages.Add(RunEvent(p));
                    break;
            }
            return landing;
        }

        /// <summary>ターンが進むほど、青・赤・目的地の額を増やす。</summary>
        int Grow(int amount) => amount * (100 + settings.moneyGrowthPercentPerRound * (Round - 1)) / 100;

        TourTown PickDestination(TourTown from)
        {
            // 今の目的地（スタート）から、ある程度遠い町
            var dist = map.DistancesFrom(from.node);
            int max = map.towns.Max(t => dist.TryGetValue(t.node, out int d) ? d : 0);
            // TODO(仕様): 遠すぎると一番乗りが少なかった（20 ターンで1人 0.8 回）ので、盤面の広さの 1/4〜1/2 の距離から選ぶ
            var far = map.towns.Where(t => t != from && dist.TryGetValue(t.node, out int d) && d >= max / 4 && d <= max / 2).ToList();
            if (far.Count == 0) far = map.towns.Where(t => t != from).ToList();
            return far[rng.Next(far.Count)];
        }

        EnemyData PickEnemy()
        {
            // ターンが進むほど、深い層の魔物が出る
            int layers = Math.Max(1, config.layers.Count);
            int stage = Math.Min(layers - 1, (Round - 1) * layers / rounds);
            var pool = config.layers.Count > 0 ? config.layers[stage].battleEnemies.Where(e => e != null).ToList() : config.battleEnemies.Where(e => e != null).ToList();
            if (pool.Count == 0) pool = config.layers.SelectMany(l => l.battleEnemies).Where(e => e != null).ToList();
            return pool[rng.Next(pool.Count)];
        }

        // ---- 名物・サイコロ屋・札屋 ----

        /// <summary>名物を買う（その町に止まっているときだけ）。</summary>
        public bool Buy(TourProperty property)
        {
            var p = Current;
            if (property == null || property.owner >= 0) return false;
            if (p.node.kind != TourTileKind.Town || !map.towns[p.node.town].properties.Contains(property)) return false;
            if (!p.run.SpendGold(property.price)) return false;
            property.owner = p.index;
            return true;
        }

        /// <summary>サイコロ屋の品ぞろえ（3つ。レア度で値段が違う）。</summary>
        public List<(DiceData dice, int price)> DiceShopOffer()
        {
            // みんなで遊ぶとき専用のダイス（勝負賽など）も並ぶ
            var pool = config.rewardDicePool.Concat(config.multiplayerDicePool.Where(d => d != null)).ToList();
            var picked = RewardGenerator.PickDice(rng, config.rewards.normalRarityWeights, pool, 3);
            return picked.Select(d => (d, d.rarity == Rarity.Rare ? settings.diceShopRare : d.rarity == Rarity.Uncommon ? settings.diceShopUncommon : settings.diceShopCommon)).ToList();
        }

        public bool BuyDice(DiceData data, int price)
        {
            var p = Current;
            if (data == null || !p.run.CanAddDice || !p.run.SpendGold(price)) return false;
            p.run.AddDice(data);
            return true;
        }

        // ---- 国めぐりの宝物（みんなで遊ぶとき専用のレリック。効き目は id で見る） ----

        public const string YakuyokeRelicId = "yakuyokenosuzu";
        public const string SorobanRelicId = "shouninnosoroban";
        public const string HayakagoRelicId = "hayakago";
        // TODO(仕様): 宝物の値段・並ぶ確率は仮
        public const int RelicPrice = 180;
        const int RelicShopPercent = 60;

        public static bool HasRelic(TourPlayer p, string id) => p.run.Relics.Any(r => r != null && r.id == id);

        /// <summary>札屋に並ぶ宝物（持っていないもの。並ばなければ null）。</summary>
        public Effects.RelicData RelicShopOffer(TourPlayer p)
        {
            var pool = config.tourRelicPool.Where(r => r != null && !HasRelic(p, r.id)).ToList();
            if (pool.Count == 0 || rng.Next(100) >= RelicShopPercent) return null;
            return pool[rng.Next(pool.Count)];
        }

        public bool BuyRelic(Effects.RelicData relic)
        {
            var p = Current;
            if (relic == null || HasRelic(p, relic.id) || !p.run.SpendGold(RelicPrice)) return false;
            p.run.AddRelic(relic);
            return true;
        }

        /// <summary>札屋の品ぞろえ（2枚）。</summary>
        public List<TourCardKind> CardShopOffer()
        {
            var list = new List<TourCardKind>();
            for (int i = 0; i < 10 && list.Count < 2; i++)
            {
                var k = TourCards.Pick(rng);
                if (!list.Contains(k)) list.Add(k); // 同じ札は並べない
            }
            return list;
        }

        public bool BuyCard(TourCardKind card)
        {
            var p = Current;
            if (p.cards.Count >= settings.maxCards || !p.run.SpendGold(TourCards.Price(card))) return false;
            p.cards.Add(card);
            return true;
        }

        bool GiveCard(TourPlayer p, TourCardKind card)
        {
            if (p.cards.Count >= settings.maxCards) return false;
            p.cards.Add(card);
            return true;
        }

        // ---- 札 ----

        /// <summary>手番のはじめに札を使う。相手を選ぶ札は target（相手の番号）。飛脚札は UseCourier。起きたことを返す（使えなければ null）。</summary>
        public string UseCard(int cardIndex, int target = -1)
        {
            var p = Current;
            if (cardIndex < 0 || cardIndex >= p.cards.Count) return null;
            var card = p.cards[cardIndex];
            TourPlayer t = target >= 0 && target < players.Count && target != p.index ? players[target] : null;
            if (TourCards.NeedsTarget(card) && t == null) return null;
            string msg;
            switch (card)
            {
                case TourCardKind.Express:
                    ExpressThisTurn = true;
                    msg = "急行札：この手番は、ダイスを2個振れる。";
                    break;
                case TourCardKind.Tailwind:
                    TailwindThisTurn += TourCards.TailwindAmount;
                    msg = $"追い風札：この手番の出目 +{TourCards.TailwindAmount}。";
                    break;
                case TourCardKind.Stall:
                    t.movePenalty += TourCards.StallAmount;
                    msg = $"足止め札：{t.name} の次の移動の出目 −{TourCards.StallAmount}。";
                    break;
                case TourCardKind.Thief:
                {
                    int stolen = t.run.LoseGold(t.Gold * TourCards.ThiefPercent / 100);
                    p.run.GainGold(stolen);
                    msg = $"盗人札：{t.name} から {stolen} G 盗んだ！";
                    break;
                }
                case TourCardKind.Curse:
                {
                    var curse = PickCurse();
                    if (curse == null || !t.run.CanAddDice) msg = $"呪い札：{t.name} のポーチがいっぱいで、呪いは届かなかった。";
                    else
                    {
                        t.run.AddDice(curse);
                        msg = $"呪い札：{t.name} のポーチに {curse.displayName} を入れた。";
                    }
                    break;
                }
                case TourCardKind.Scapegoat:
                    if (YakugamiHolder != p.index) return null; // 厄神がついていないと使えない
                    SetYakugamiHolder(t.index, false);
                    msg = $"身代わり札：厄神を {t.name} に押し付けた！";
                    break;
                case TourCardKind.Fortune:
                {
                    int got = p.run.GainGold(TourCards.FortuneBase + TourCards.FortunePerRound * Round);
                    msg = $"福の札：{got} G をもらった。";
                    break;
                }
                case TourCardKind.Swap:
                    (p.node, t.node) = (t.node, p.node);
                    p.cameFrom = null;
                    t.cameFrom = null;
                    msg = $"入れ替え札：{t.name} と居場所を入れ替えた！";
                    break;
                case TourCardKind.Gust:
                {
                    // 目的地と、いまいる町には飛ばさない
                    var towns = map.towns.Where(x => x != Destination && x.node != t.node).ToList();
                    if (towns.Count == 0) return null;
                    var town = towns[rng.Next(towns.Count)];
                    t.node = town.node;
                    t.cameFrom = null;
                    msg = $"天狗の団扇：{t.name} を「{town.name}」まで吹き飛ばした！";
                    break;
                }
                case TourCardKind.Smith:
                {
                    var die = SmithTarget(p);
                    if (die == null) return null;
                    int face = 0;
                    for (int i = 1; i < die.faces.Length; i++) if (die.faces[i].value < die.faces[face].value) face = i;
                    var f = die.faces[face];
                    int before = f.value;
                    f.value = Math.Min(Face.MaxValue, f.value + 1);
                    die.faces[face] = f;
                    msg = $"鍛冶札：{die.DisplayName} の {before} の面を {f.value} にした。";
                    break;
                }
                default:
                    return null; // 飛脚札は UseCourier
            }
            p.cards.RemoveAt(cardIndex);
            p.cardsUsed++;
            return msg;
        }

        /// <summary>札を今使えるか（身代わり札は厄神がついているときだけ）。</summary>
        public bool CanUseCard(TourCardKind card) =>
            card == TourCardKind.Scapegoat ? YakugamiHolder == TurnIndex : card != TourCardKind.Smith || SmithTarget(Current) != null;

        /// <summary>鍛冶札で鍛えるダイス：鍛えられるダイスのうち、面の合計がいちばん小さいもの（呪いのダイスは除く。まだ6より小さい面があるもの）。</summary>
        public static DiceInstance SmithTarget(TourPlayer p) => p.run.pouch.All
            .Where(d => RunState.CanForge(d) && (d.data == null || d.data.rarity != Rarity.Curse) && d.faces.Any(f => f.value < 6))
            .OrderBy(d => d.faces.Sum(f => f.value)).FirstOrDefault();

        /// <summary>飛脚札：町へ飛ぶ（その町に止まる）。</summary>
        public TourLanding UseCourier(int cardIndex, TourTown town)
        {
            var p = Current;
            if (cardIndex < 0 || cardIndex >= p.cards.Count || p.cards[cardIndex] != TourCardKind.Courier || town == null) return null;
            p.cards.RemoveAt(cardIndex);
            p.cardsUsed++;
            var landing = WarpTo(town);
            landing.messages.Insert(0, $"飛脚札：{town.name}へひとっ飛び！");
            return landing;
        }

        DiceData PickCurse()
        {
            var curses = config.curseDicePool.Where(d => d != null).ToList();
            if (curses.Count == 0 && config.curseDice != null) curses.Add(config.curseDice);
            return curses.Count > 0 ? curses[rng.Next(curses.Count)] : null;
        }

        // ---- 大厄神（桃鉄のキングボンビー風）：同じ人に続けてとりついていると、大厄神に化けていたずらが大きくなる ----

        /// <summary>いまの人に続けてとりついている手番の数（人が替わると 0 から）。</summary>
        public int YakugamiTurns { get; private set; }

        /// <summary>大厄神に化けているか（すれ違い・身代わり札で移っても大厄神のまま。目的地に一番乗りがあって、つき直すと戻る）。</summary>
        public bool GreatYakugami { get; private set; }

        /// <summary>この手番のはじめに大厄神に化けたか（画面の演出用）。</summary>
        public bool JustBecameGreat { get; private set; }

        /// <summary>厄神がとりつく人を替える。</summary>
        void SetYakugamiHolder(int index, bool reset)
        {
            if (YakugamiHolder != index) YakugamiTurns = 0;
            YakugamiHolder = index;
            if (reset) GreatYakugami = false;
        }

        /// <summary>大厄神のいたずら：お金をたくさん・いちばん高い名物・呪いのダイス2個・札を全部。できないものはお金にする。</summary>
        string GreatYakugamiMischief(TourPlayer p)
        {
            int roll = rng.Next(100);
            var priciest = map.towns.SelectMany(t => t.properties).Where(x => x.owner == p.index).OrderByDescending(x => x.price).FirstOrDefault();
            if (roll >= 45 && roll < 70 && priciest != null)
            {
                priciest.owner = -1;
                return $"大厄神の大いたずら：{p.name} のいちばん高い名物「{priciest.name}」を売り飛ばした！";
            }
            if (roll >= 70 && roll < 85 && p.run.CanAddDice)
            {
                var names = new List<string>();
                for (int i = 0; i < 2 && p.run.CanAddDice; i++)
                {
                    var curse = PickCurse();
                    if (curse == null) break;
                    p.run.AddDice(curse);
                    names.Add(curse.displayName);
                }
                if (names.Count > 0) return $"大厄神の大いたずら：{p.name} のポーチに {string.Join("と", names)} を押し込んだ！";
            }
            if (roll >= 85 && p.cards.Count > 0)
            {
                int n = p.cards.Count;
                p.cards.Clear();
                return $"大厄神の大いたずら：{p.name} の札 {n} 枚が、ぜんぶ燃やされた！";
            }
            int percent = rng.Next(settings.greatYakugamiGoldLossMin, settings.greatYakugamiGoldLossMax + 1);
            int lost = p.run.LoseGold(p.Gold * percent / 100);
            return $"大厄神の大いたずら：{p.name} の財布から {lost} G をごっそり持っていった！";
        }

        // ---- 厄神 ----

        void PassYakugami(TourPlayer mover, TourLanding landing)
        {
            if (YakugamiHolder < 0) return;
            var here = players.Where(o => o != mover && o.node == mover.node).ToList();
            if (here.Count == 0) return;
            if (YakugamiHolder == mover.index)
            {
                var to = here[0];
                SetYakugamiHolder(to.index, false);
                landing.messages.Add($"すれ違いざまに、厄神が {to.name} に移った！");
            }
        }

        /// <summary>厄神のいたずら（手番のはじめ）：お金・名物・呪い・札のどれか。できないものはお金にする。</summary>
        string YakugamiMischief(TourPlayer p)
        {
            int roll = rng.Next(100);
            var owned = map.towns.SelectMany(t => t.properties).Where(x => x.owner == p.index).OrderBy(x => x.price).FirstOrDefault();
            var curse = roll >= 65 && roll < 85 ? PickCurse() : null;
            if (roll >= 45 && roll < 65 && owned != null)
            {
                owned.owner = -1;
                return $"厄神のいたずら：{p.name} の名物「{owned.name}」が人手に渡った……";
            }
            if (roll >= 65 && roll < 85 && curse != null && p.run.CanAddDice)
            {
                p.run.AddDice(curse);
                return $"厄神のいたずら：{p.name} のポーチに {curse.displayName} が紛れ込んだ……";
            }
            if (roll >= 85 && p.cards.Count > 0)
            {
                int i = rng.Next(p.cards.Count);
                var card = p.cards[i];
                p.cards.RemoveAt(i);
                return $"厄神のいたずら：{p.name} の{TourCards.Name(card)}が破られた……";
            }
            int percent = rng.Next(settings.yakugamiGoldLossMin, settings.yakugamiGoldLossMax + 1);
            int lost = p.run.LoseGold(p.Gold * percent / 100);
            return $"厄神のいたずら：{p.name} の財布から {lost} G が消えた……";
        }

        // ---- 出来事（みんなに関わるものも） ----

        /// <summary>いちばん最近の出来事の種類（0 宴会・1 年貢・2 小判・3 秘湯・4 追い風・5 村祭り・6 関所・7 入れ替わり。画面の絵に使う）。</summary>
        public int LastEventKind { get; private set; } = -1;

        public static readonly string[] EventNames = { "宴会", "年貢", "小判拾い", "秘湯", "旅の風", "村祭り", "関所", "入れ替わり" };

        string RunEvent(TourPlayer p)
        {
            LastEventKind = rng.Next(8);
            switch (LastEventKind)
            {
                case 6:
                {
                    // 関所：いちばん資産の多い人に通行料（自分がいちばんなら、みんなから少しずつ）
                    var richest = players.OrderByDescending(Assets).ThenBy(o => o.index).First();
                    int toll = 20 + 2 * Round;
                    if (richest == p)
                    {
                        int total = 0;
                        foreach (var o in players.Where(o => o != p)) total += o.run.LoseGold(Math.Min(o.Gold, toll / 2));
                        p.run.GainGold(total);
                        return $"関所の主になった！ みんなから通行料を {total} G もらった。";
                    }
                    int paid = p.run.LoseGold(toll);
                    richest.run.GainGold(paid);
                    return $"関所で通行料 {paid} G を払った（いちばんの長者、{richest.name}へ）。";
                }
                case 7:
                {
                    // 入れ替わり：だれかと場所が入れ替わる（桃鉄の「ぶっとび」風）
                    var others = players.Where(o => o != p).ToList();
                    if (others.Count == 0) goto default;
                    var o = others[rng.Next(others.Count)];
                    (p.node, o.node) = (o.node, p.node);
                    p.cameFrom = null;
                    o.cameFrom = null;
                    return $"不思議な霧に包まれて、{o.name} と場所が入れ替わった！";
                }
                case 0:
                {
                    // 宴会：ほかの人みんなから少しずつもらう
                    int total = 0;
                    foreach (var o in players.Where(o => o != p)) total += o.run.LoseGold(Math.Min(o.Gold, 20 + Round));
                    p.run.GainGold(total);
                    return $"宴会を開いた！ みんなからご祝儀を {total} G もらった。";
                }
                case 1:
                {
                    // 年貢：名物をいちばん多く持っている人に、名物の数ぶん払う
                    int count = map.towns.Sum(t => t.properties.Count(x => x.owner == p.index));
                    int toll = p.run.LoseGold(10 * Math.Max(1, count));
                    return $"代官に年貢を取られた。{toll} G 払った。";
                }
                case 2:
                {
                    int got = p.run.GainGold(Grow(40));
                    return $"道端で小判を拾った！ {got} G。";
                }
                case 3:
                {
                    // 温泉：HP がすべて回復
                    p.run.player.Heal(p.run.player.maxHp);
                    return "秘湯を見つけた！ HP がすべて回復した。";
                }
                case 4:
                {
                    // 追い風：次の移動 +2（足止めの反対）
                    p.movePenalty -= 2;
                    return "旅の風が吹いた！ 次の移動の出目 +2。";
                }
                default:
                {
                    // 祭り：みんな少しもらう
                    foreach (var o in players) o.run.GainGold(20 + Round);
                    return $"村祭りだ！ みんな {20 + Round} G もらった。";
                }
            }
        }

        // ---- 戦闘・対決 ----

        /// <summary>魔物との戦闘が終わった：勝てばゴールド（ときどきダイス）、負ければゴールドを落として近くの町へ。</summary>
        public string FinishBattle(BattleOutcome outcome, out DiceData diceReward)
        {
            diceReward = null;
            var p = Current;
            if (outcome == BattleOutcome.Victory)
            {
                p.battleWins++;
                int got = p.run.GainGold(Grow(rng.Next(settings.battleGoldMin, settings.battleGoldMax + 1)), true);
                string msg = $"勝った！ {got} G を手に入れた。";
                if (rng.Next(100) < settings.battleDiceChance)
                {
                    var dice = RewardGenerator.PickDice(rng, config.rewards.normalRarityWeights, config.rewardDicePool, 1);
                    if (dice.Count > 0 && p.run.CanAddDice)
                    {
                        diceReward = dice[0];
                        p.run.AddDice(diceReward);
                        msg += $"　{diceReward.displayName}も手に入れた。";
                    }
                }
                return msg;
            }
            if (outcome == BattleOutcome.Fled) return "逃げ出した。";
            // 負け：ゴールドを落とし、いちばん近い町で手当てを受ける
            int lost = p.run.LoseGold(p.Gold * settings.defeatGoldLossPercent / 100);
            p.run.player.hp = Math.Max(1, p.run.player.maxHp * settings.defeatHpPercent / 100);
            var dist = map.DistancesFrom(p.node);
            var town = map.towns.OrderBy(t => dist.TryGetValue(t.node, out int d) ? d : int.MaxValue).First();
            p.node = town.node;
            p.cameFrom = null;
            return $"倒れてしまった……　{lost} G を落とし、{town.name}で手当てを受けた。";
        }

        /// <summary>
        /// 対決：おたがいに使えるダイスを（平均の高いものから）3個まで振り、合計の大きいほうが勝ち。
        /// 勝った人は、負けた人のゴールドを duelStealPercent% もらう。振ったダイスは使用済み。
        /// </summary>
        public TourDuelResult Duel(TourPlayer defender)
        {
            var attacker = Current;
            var result = new TourDuelResult { attacker = attacker, defender = defender };
            result.attackerTotal = DuelRoll(attacker, result.attackerRolls, result.attackerDice) + attacker.run.player.strength;
            result.defenderTotal = DuelRoll(defender, result.defenderRolls, result.defenderDice) + defender.run.player.strength;
            if (result.attackerTotal != result.defenderTotal)
            {
                result.winner = result.attackerTotal > result.defenderTotal ? attacker : defender;
                result.winner.duelWins++;
                var loser = result.winner == attacker ? defender : attacker;
                result.stolen = loser.run.LoseGold(loser.Gold * settings.duelStealPercent / 100);
                result.winner.run.GainGold(result.stolen);
            }
            return result;
        }

        // TODO(仕様): 勝負賽の対決での加算は仮（+3）
        public const string ShowdownDiceId = "shoubu";
        public const int ShowdownBonus = 3;

        int DuelRoll(TourPlayer p, List<(string, int)> rolls, List<DiceInstance> dice)
        {
            var chosen = p.run.pouch.Available.OrderByDescending(d => d.faces.Average(f => f.value) + (d.data != null && d.data.id == ShowdownDiceId ? ShowdownBonus : 0)).Take(3).ToList();
            int total = 0;
            foreach (var d in chosen)
            {
                int v = DiceRoller.Roll(d, rng, p.lastRolled, out _);
                p.lastRolled = v;
                // 勝負賽（みんなで遊ぶとき専用）：対決では +3
                if (d.data != null && d.data.id == ShowdownDiceId) v += ShowdownBonus;
                rolls.Add((d.DisplayName, v));
                dice.Add(d);
                total += v;
                p.run.pouch.Use(d, false, false);
            }
            return total;
        }

        // ---- 決算・勝ち負け ----

        /// <summary>いちばん最近の決算で、それぞれが得た収入（席の順。画面の演出に使う）。</summary>
        public List<int> LastSettlement { get; } = new List<int>();

        /// <summary>決算：名物の収入（独占している町は2倍）。</summary>
        string Settlement()
        {
            var lines = new List<string>();
            LastSettlement.Clear();
            foreach (var p in players)
            {
                int income = 0;
                foreach (var t in map.towns)
                {
                    int mult = t.Monopolist == p.index ? 2 : 1;
                    income += t.properties.Where(x => x.owner == p.index).Sum(x => x.Income) * mult;
                }
                if (HasRelic(p, SorobanRelicId)) income = income * 125 / 100; // 商人の算盤
                int gained = income > 0 ? p.run.GainGold(income) : 0;
                LastSettlement.Add(gained);
                if (gained > 0) lines.Add($"{p.name} +{gained} G");
            }
            return lines.Count > 0 ? "決算！ 名物の収入：" + string.Join("・", lines) : "決算！ まだ誰も名物を持っていない。";
        }

        // ---- おまけの賞（マリオパーティのボーナススター風） ----

        // TODO(仕様): 賞のごほうびは仮（資産なら +200 G、目的地なら一番乗り +1）
        public const int AwardGold = 200;

        /// <summary>
        /// 最後に、いちばん○○だった人にごほうび（同じ数の人がいれば、その賞はなし）。
        /// 勝ち方が資産なら +200 G、目的地なら一番乗り +1 に数える。大ボスなら飾り（ダメージには入らない）。
        /// </summary>
        public List<TourAward> GiveAwards()
        {
            var list = new List<TourAward>();
            void Award(string title, string what, Func<TourPlayer, int> score)
            {
                int best = players.Max(score);
                if (best <= 0) return;
                var top = players.Where(p => score(p) == best).ToList();
                if (top.Count == 1) list.Add(new TourAward { title = title, reason = $"{what} {best}", player = top[0] });
            }
            Award("勝負師賞", "対決で勝った回数", p => p.duelWins);
            Award("武勇賞", "魔物に勝った回数", p => p.battleWins);
            Award("名物長者賞", "持っている名物の数", p => map.towns.Sum(t => t.properties.Count(x => x.owner == p.index)));
            Award("策士賞", "使った札の数", p => p.cardsUsed);
            foreach (var a in list)
            {
                if (goal == TourGoal.Arrivals) a.player.arrivals++;
                else if (goal == TourGoal.Assets) a.player.run.GainGold(AwardGold);
            }
            return list;
        }

        /// <summary>資産：ゴールド＋持っている名物の値段。</summary>
        public int Assets(TourPlayer p) => p.Gold + map.towns.Sum(t => t.properties.Where(x => x.owner == p.index).Sum(x => x.price));

        /// <summary>順位（勝ち方に合わせて並べる）。</summary>
        public List<TourPlayer> Standings()
        {
            switch (goal)
            {
                case TourGoal.Arrivals: return players.OrderByDescending(p => p.arrivals).ThenByDescending(Assets).ThenBy(p => p.index).ToList();
                case TourGoal.FinalBoss: return players.OrderByDescending(p => p.bossDamage).ThenByDescending(Assets).ThenBy(p => p.index).ToList();
                default: return players.OrderByDescending(Assets).ThenByDescending(p => p.arrivals).ThenBy(p => p.index).ToList();
            }
        }

        /// <summary>大ボス（勝ち方：大ボス）。最後の層のボス。</summary>
        public EnemyData FinalBoss
        {
            get
            {
                var last = config.layers.Count > 0 ? config.layers[config.layers.Count - 1] : null;
                var choices = last != null ? last.bossChoices.Where(b => b != null).ToList() : new List<EnemyData>();
                if (choices.Count > 0) return choices[0];
                return last != null && last.boss != null ? last.boss : config.boss;
            }
        }
    }
}
