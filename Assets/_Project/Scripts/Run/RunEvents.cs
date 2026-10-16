using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    /// <summary>イベント（仕様書 第9章）。フェーズ1は5種。</summary>
    public enum EventKind
    {
        Gamble,          // 路地裏の賭場
        FallenDice,      // 落ちている賽
        OldShrine,       // 古びた祠
        FoxWedding,      // 狐の嫁入り
        IdatenFootprints, // 韋駄天の足跡
        Craftsman,       // 流しの職人
        TwinStatues,     // 道祖神の双子像（第2層〜）
        Pitfall,         // 落とし穴
        Merchant,        // 旅の商人
        OniDice,         // 鬼の賽勝負（第2層〜）
        LostChild,       // 迷子の子ども
        StartOverCard,   // 振り出しの札（第1・第2層、1ランに1回）
        WoundedSamurai,  // 行き倒れの侍
        HotSpring,       // 湯治場
        Tsukumogami,     // 賽の付喪神（第2層〜）
        Bonfire,         // 旅団の焚き火（みんなで遊ぶとき専用）
        Lottery,         // 福引き所
        MergeSmith,      // 合わせ賽の鍛冶師（1ランに1回）
        KappaSumo,       // 河童の相撲
        Offering,        // 賽銭箱
        Feast,           // 旅団の宴（みんなで遊ぶとき専用）
        Kawara,          // 賽の河原（石を積むか、やめるか）
        Suzume,          // 雀のお宿（小さいつづらか、大きいつづらか）
        Crane,           // 鶴の恩返し（いま助けると、次の層に着いたときに恩返し）
        Ryugu,           // 竜宮城（もてなしを受けると元気になるが、時が過ぎる）
        // 2026-10-16 追加（イベント限定のダイス・次の層に持ち越すイベント）
        Youtou,          // 妖刀の祠（妖刀賽。最大HPが減る）
        OldTemple,       // 古寺の住職（経文賽か、宿坊で休む）
        Tennyo,          // 天女の羽衣（返すと、次の層に着いたとき天女賽と最大HP。持っていくとゴールドと呪い）
        Zashiki,         // 座敷わらし（お菓子をあげると座敷童子賽）
        ShootingStar,    // 流れ星（星降る賽・全回復・ゴールドから1つ願う）
        OniTreasure,     // 鬼の落とし物（第2層〜。鬼の金棒賽と呪い）
        Moneylender,     // 両替商の貸し金（貸すと、次の層に着いたとき倍になって返る）
        Seedling,        // 苗木売り（植えると、次の層に着いたとき最大HP）
        KappaPlate,      // 河童の忘れ物（皿を返すと回復、もらうと河童の皿賽）
        Tamamushi,       // 玉虫の厨子（玉虫賽を買う）
        Fortune,         // 辻占い（大吉：最大HP・吉：ゴールド・凶：呪い）
        Festival,        // 祭り囃子（踊るとお守り、見物すると回復）
        Raikou,          // 雷様の落とし物（太鼓を返すと雷神の太鼓、持っていくとゴールドと雷）
        Yamanba,         // 山姥の宿（泊まると全回復、ただし呪われることも）
        // 2026-10-16 さらに追加（選ぶだけの小さなイベント）
        Tanabata,         // 七夕の短冊（願いを書くとお守り、星を眺めると回復）
        EbisuFishing,     // 恵比寿の釣り堀（10G で釣る：ゴールド・お守り・まれにレリック）
        InariShrine,      // 稲荷の鳥居（油揚げを供える（40G）とレリック）
        Mochitsuki,       // 餅つき（手伝うと最大HP（HP は少し減る）、食べるだけなら回復）
        Sentou,           // 銭湯（15G でHP回復）
        Bakasare,         // 狸の化かし（ついて行くと、ゴールドが増えるか減るか）
        Hanami,           // 花見の宴（加わると大きく回復（ゴールドが減る））
        YakushiDo,        // 薬師堂（祈ると呪いのダイスを1個はらう（なければ回復））
        TenguTrial,       // 天狗の試練（受けると HP が減る代わりにレリック）
        Kiyomizu,         // 清水の舞台（飛び降りると HP が大きく減る代わりに最大HPとゴールド）
        Hotaru,           // 蛍の川（眺めると回復、集めるとお守り）
        Amazake,          // 甘酒屋（10G でHP回復）
        // 2026-10-16 その3（選ぶだけの小さなイベント）
        Kamaitachi,       // 鎌鼬の峠（駆け抜けると HP が減る代わりにゴールド、待つと回復）
        DarumaIchi,       // 達磨市（30G で達磨を買うと最大HP）
        Ushinokoku,       // 丑の刻参り（五寸釘を拾うとゴールド、ただし呪いのダイス）
        KintaroSumo,      // 金太郎の相撲（相撲をとると HP が減る代わりにお守り）
        Bakeneko,        // 化け猫の茶屋（お茶をいただくとレリックか呪い、団子だけなら回復）
        Momijigari,       // 紅葉狩り（眺めると回復、鬼女と飲むとレリックか大けが）
        Ennichi,          // 縁日の射的（10G で撃つと、当たればお守り）
        Kakurezato,       // 隠れ里（泊まると全回復、お礼に持ち金の半分）
        Kagura,           // 神楽殿（舞を奉納するとダイスの面を鍛える）
        ZeniArai,         // 銭洗い弁天（お金を洗うとゴールドが増える）
        YukiOnna,         // 雪女の吹雪（泊まると全回復、ただし最大HPが減る）
        Tsukimi,          // 月見（団子を供えるとお守り、眺めると回復）
        // 2026-10-16 その4
        KasaJizo,         // 笠地蔵（笠をかぶせると次の層でお礼（持ち越し））
        Sanzaru,          // 三猿の祠（見ざる・言わざる・聞かざるから1つ）
        Ochimusha,        // 落ち武者の亡霊（供養すると呪いが消える、刀を持ち去るとレリックと呪い）
        IgoSennin,        // 囲碁打ちの仙人（20G 賭けて一局：勝てば 60G）
        Terakoya,         // 寺子屋（教えるとゴールド、遊ぶと回復）
        Toyama,           // 富山の薬売り（25G で傷薬のお守り）
        Yuudachi,         // 夕立（雨宿りで回復、濡れて走ると次の移動+2）
        TonchiBridge,     // とんち橋（真ん中を渡るとゴールド、休むと回復）
        Biwa,             // 琵琶法師（聞くと回復、お布施でお守り）
        Shishimai,        // 獅子舞（頭をかんでもらうと呪いが消える）
        Hyakki,           // 百鬼夜行（加わると HP が減る代わりにお守り2つ）
    }

    /// <summary>イベントの数値（仕様書 第9章）。</summary>
    [Serializable]
    public class EventSettings
    {
        public List<EventKind> kinds = new List<EventKind>
        {
            EventKind.Gamble, EventKind.FallenDice, EventKind.OldShrine, EventKind.FoxWedding, EventKind.IdatenFootprints,
            EventKind.Craftsman, EventKind.TwinStatues, EventKind.Pitfall, EventKind.Merchant, EventKind.OniDice, EventKind.LostChild, EventKind.StartOverCard,
            EventKind.WoundedSamurai, EventKind.HotSpring, EventKind.Tsukumogami, EventKind.Lottery, EventKind.MergeSmith,
            EventKind.KappaSumo, EventKind.Offering, EventKind.Kawara, EventKind.Suzume, EventKind.Crane, EventKind.Ryugu,
            EventKind.Youtou, EventKind.OldTemple, EventKind.Tennyo, EventKind.Zashiki, EventKind.ShootingStar,
            EventKind.OniTreasure, EventKind.Moneylender, EventKind.Seedling, EventKind.KappaPlate, EventKind.Tamamushi,
            EventKind.Fortune, EventKind.Festival, EventKind.Raikou, EventKind.Yamanba,
            EventKind.Tanabata, EventKind.EbisuFishing, EventKind.InariShrine, EventKind.Mochitsuki, EventKind.Sentou, EventKind.Bakasare, EventKind.Hanami, EventKind.YakushiDo, EventKind.TenguTrial, EventKind.Kiyomizu, EventKind.Hotaru, EventKind.Amazake,
            EventKind.Kamaitachi, EventKind.DarumaIchi, EventKind.Ushinokoku, EventKind.KintaroSumo, EventKind.Bakeneko, EventKind.Momijigari, EventKind.Ennichi, EventKind.Kakurezato, EventKind.Kagura, EventKind.ZeniArai, EventKind.YukiOnna, EventKind.Tsukimi,
            EventKind.KasaJizo, EventKind.Sanzaru, EventKind.Ochimusha, EventKind.IgoSennin, EventKind.Terakoya, EventKind.Toyama, EventKind.Yuudachi, EventKind.TonchiBridge, EventKind.Biwa, EventKind.Shishimai, EventKind.Hyakki,
        };

        [UnityEngine.Header("路地裏の賭場")]
        public int gambleBet = 20;
        // TODO(仕様): 「当たれば40G」は賭け金込みの払い戻しと解釈（差し引き+20G）
        public int gamblePayout = 40;

        [UnityEngine.Header("落ちている賽")]
        public int fallenUncommonPercent = 50;

        [UnityEngine.Header("古びた祠")]
        public int shrineHpCost = 8; // 既定値。ゲームの値は DataBuilder で入れる
        public int shrinePrayHeal = 5; // 既定値。ゲームの値は DataBuilder で入れる

        [UnityEngine.Header("狐の嫁入り")]
        public int foxTurns = 3;
        public int foxMoveBonus = 2;
        // TODO(仕様): 「見送る：お守り1個」で、お守りがいっぱいのときは代わりにゴールド
        public int foxSeeOffGold = 15;

        [UnityEngine.Header("韋駄天の足跡")]
        public int idatenSteps = 3;

        [UnityEngine.Header("流しの職人")]
        public int craftCost = 30;

        [UnityEngine.Header("落とし穴")]
        public int pitfallThreshold = 4;
        public int pitfallDamage = 6; // 既定値。ゲームの値は DataBuilder で入れる

        [UnityEngine.Header("迷子の子ども")]
        public int lostChildGold = 40;
        public int lostChildDirectionsGold = 10;

        [UnityEngine.Header("振り出しの札")]
        public int startOverMaxHp = 10; // 既定値。ゲームの値は DataBuilder で入れる

        // TODO(仕様): 行き倒れの侍・湯治場・賽の付喪神は仕様書にない追加イベント（開発者の要望）。数値は仮
        // TODO(仕様): 福引き所は仕様書にない追加イベント（2026-10-16）。数値は仮
        [UnityEngine.Header("河童の相撲・賽銭箱")]
        public int sumoThreshold = 4;     // この目以上で勝ち
        public int sumoGold = 50;
        public int sumoDamage = 7; // 既定値。ゲームの値は DataBuilder で入れる
        public int offeringCost = 30;
        public int offeringMaxHp = 5; // 既定値。ゲームの値は DataBuilder で入れる

        [UnityEngine.Header("福引き所")]
        public int lotteryCost = 30;

        [UnityEngine.Header("行き倒れの侍")]
        public int samuraiHpCost = 7; // 既定値。ゲームの値は DataBuilder で入れる
        public int samuraiNoRelicGold = 50;
        public int samuraiRobGold = 60;

        [UnityEngine.Header("湯治場")]
        public int hotSpringCost = 25;
        public int hotSpringHealPercent = 40;
        public int footBathHeal = 5; // 既定値。ゲームの値は DataBuilder で入れる

        [UnityEngine.Header("出る層（1から数える）・1ランに1回")]
        public int twinMinLayer = 2;
        public int oniMinLayer = 2;
        public int startOverMaxLayer = 2;
        public int tsukumogamiMinLayer = 2;
        // 賽の付喪神は何度も出るとダイスが強くなりすぎる（自動プレイでクリア率が大きく上がった）ので1ランに1回
        public List<EventKind> oncePerRun = new List<EventKind> { EventKind.StartOverCard, EventKind.Tsukumogami, EventKind.Suzume, EventKind.Crane, EventKind.Ryugu, EventKind.Youtou, EventKind.OldTemple, EventKind.Tennyo, EventKind.Zashiki, EventKind.ShootingStar, EventKind.OniTreasure, EventKind.Moneylender, EventKind.Seedling, EventKind.KappaPlate, EventKind.Tamamushi, EventKind.Festival, EventKind.Raikou, EventKind.Yamanba, EventKind.Tanabata, EventKind.InariShrine, EventKind.Mochitsuki, EventKind.Bakasare, EventKind.Hanami, EventKind.YakushiDo, EventKind.TenguTrial, EventKind.Kiyomizu, EventKind.Hotaru, EventKind.Kamaitachi, EventKind.DarumaIchi, EventKind.Ushinokoku, EventKind.KintaroSumo, EventKind.Bakeneko, EventKind.Momijigari, EventKind.Kakurezato, EventKind.Kagura, EventKind.ZeniArai, EventKind.YukiOnna, EventKind.Tsukimi, EventKind.KasaJizo, EventKind.Sanzaru, EventKind.Ochimusha, EventKind.IgoSennin, EventKind.Terakoya, EventKind.Toyama, EventKind.Yuudachi, EventKind.TonchiBridge, EventKind.Biwa, EventKind.Shishimai, EventKind.Hyakki };
    }

    public struct GambleResult
    {
        public int value;
        public bool even;    // 振った出目が偶数（丁）か
        public bool win;
        public int payout;   // 当たりで得たゴールド
        public bool refreshed;
    }

    public partial class RunState
    {
        EventKind? lastEvent;

        /// <summary>
        /// イベントマスで起きるイベントを選ぶ（イベント用の乱数）。直前と同じイベントは続けない。
        /// TODO(仕様): 同じイベントが1ランに何度も出てよいかは未定。フェーズ1は「2回続けない」だけ
        /// </summary>
        public EventKind PickEvent(TileNode tile = null)
        {
            // 地図師の矢立で前もって決めたイベントがあれば、それが起きる
            // （1ランに1回のイベントがもう起きていたら、決めておいたものは使わない）
            if (TakePlannedEvent(tile, out var planned) && EventAllowed(planned))
            {
                lastEvent = planned;
                RecordEvent(planned);
                return planned;
            }
            var kinds = AvailableEvents();
            if (kinds.Count > 1 && lastEvent.HasValue) kinds.Remove(lastEvent.Value);
            var kind = kinds[random.Event.Next(kinds.Count)];
            lastEvent = kind;
            RecordEvent(kind);
            return kind;
        }

        /// <summary>いま起きうるイベントの種類（重なりなし）。</summary>
        /// <remarks>みんなで遊ぶとき専用のイベント（旅団の焚き火・旅団の宴）は、設定の一覧になくても協力なら加える。</remarks>
        List<EventKind> AvailableEvents() => config.events.kinds.Concat(multiplayer ? new[] { EventKind.Bonfire, EventKind.Feast } : new EventKind[0]).Distinct().Where(EventAllowed).ToList();

        /// <summary>イベントの名前（画面の表示用）。</summary>
        public static string EventName(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Gamble: return "路地裏の賭場";
                case EventKind.FallenDice: return "落ちている賽";
                case EventKind.OldShrine: return "古びた祠";
                case EventKind.FoxWedding: return "狐の嫁入り";
                case EventKind.IdatenFootprints: return "韋駄天の足跡";
                case EventKind.Craftsman: return "流しの職人";
                case EventKind.TwinStatues: return "道祖神の双子像";
                case EventKind.Pitfall: return "落とし穴";
                case EventKind.Merchant: return "旅の商人";
                case EventKind.OniDice: return "鬼の賽勝負";
                case EventKind.LostChild: return "迷子の子ども";
                case EventKind.StartOverCard: return "振り出しの札";
                case EventKind.WoundedSamurai: return "行き倒れの侍";
                case EventKind.HotSpring: return "湯治場";
                case EventKind.Tsukumogami: return "賽の付喪神";
                case EventKind.Bonfire: return "旅団の焚き火";
                case EventKind.Lottery: return "福引き所";
                case EventKind.MergeSmith: return "合わせ賽の鍛冶師";
                case EventKind.KappaSumo: return "河童の相撲";
                case EventKind.Offering: return "賽銭箱";
                case EventKind.Feast: return "旅団の宴";
                case EventKind.Kawara: return "賽の河原";
                case EventKind.Suzume: return "雀のお宿";
                case EventKind.Crane: return "鶴の恩返し";
                case EventKind.Ryugu: return "竜宮城";
                case EventKind.Youtou: return "妖刀の祠";
                case EventKind.OldTemple: return "古寺の住職";
                case EventKind.Tennyo: return "天女の羽衣";
                case EventKind.Zashiki: return "座敷わらし";
                case EventKind.ShootingStar: return "流れ星";
                case EventKind.OniTreasure: return "鬼の落とし物";
                case EventKind.Moneylender: return "両替商の貸し金";
                case EventKind.Seedling: return "苗木売り";
                case EventKind.KappaPlate: return "河童の忘れ物";
                case EventKind.Tamamushi: return "玉虫の厨子";
                case EventKind.Fortune: return "辻占い";
                case EventKind.Festival: return "祭り囃子";
                case EventKind.Raikou: return "雷様の落とし物";
                case EventKind.Yamanba: return "山姥の宿";
                case EventKind.Tanabata: return "七夕の短冊";
                case EventKind.EbisuFishing: return "恵比寿の釣り堀";
                case EventKind.InariShrine: return "稲荷の鳥居";
                case EventKind.Mochitsuki: return "餅つき";
                case EventKind.Sentou: return "銭湯";
                case EventKind.Bakasare: return "狸の化かし";
                case EventKind.Hanami: return "花見の宴";
                case EventKind.YakushiDo: return "薬師堂";
                case EventKind.TenguTrial: return "天狗の試練";
                case EventKind.Kiyomizu: return "清水の舞台";
                case EventKind.Hotaru: return "蛍の川";
                case EventKind.Kamaitachi: return "鎌鼬の峠";
                case EventKind.DarumaIchi: return "達磨市";
                case EventKind.Ushinokoku: return "丑の刻参り";
                case EventKind.KintaroSumo: return "金太郎の相撲";
                case EventKind.Bakeneko: return "化け猫の茶屋";
                case EventKind.Momijigari: return "紅葉狩り";
                case EventKind.Ennichi: return "縁日の射的";
                case EventKind.Kakurezato: return "隠れ里";
                case EventKind.Kagura: return "神楽殿";
                case EventKind.ZeniArai: return "銭洗い弁天";
                case EventKind.YukiOnna: return "雪女の吹雪";
                case EventKind.Tsukimi: return "月見";
                case EventKind.KasaJizo: return "笠地蔵";
                case EventKind.Sanzaru: return "三猿の祠";
                case EventKind.Ochimusha: return "落ち武者の亡霊";
                case EventKind.IgoSennin: return "囲碁打ちの仙人";
                case EventKind.Terakoya: return "寺子屋";
                case EventKind.Toyama: return "富山の薬売り";
                case EventKind.Yuudachi: return "夕立";
                case EventKind.TonchiBridge: return "とんち橋";
                case EventKind.Biwa: return "琵琶法師";
                case EventKind.Shishimai: return "獅子舞";
                case EventKind.Hyakki: return "百鬼夜行";
                case EventKind.Amazake: return "甘酒屋";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// イベントの判定でダイスを1個振る。移動と同じく使用済みになり（最後の1個ならリフレッシュ）、振ったときの効果も働く。ターンは進まない。
        /// </summary>
        public int RollForEvent(DiceInstance die, out bool refreshed)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            int rolled = DiceRoller.Roll(die, random.Event, LastRolledValue, out int faceIndex);
            var ctx = effects.Fire(new EffectContext(Trigger.OnRoll) { run = this, player = player, dice = die, faceIndex = faceIndex, value = rolled },
                die, die.faces[faceIndex].engraving);
            refreshed = pouch.Use(die, ctx.keepAvailable, false);
            int value = Math.Max(0, ctx.value);
            LastRolledValue = value;
            return value;
        }

        // ---- 路地裏の賭場 ----

        public bool CanGamble => Gold >= config.events.gambleBet && pouch.AvailableCount > 0;

        /// <summary>賭け金を払い、丁（偶数）か半（奇数）を宣言してダイスを振る。0 は偶数。</summary>
        public GambleResult Gamble(DiceInstance die, bool betEven)
        {
            var s = config.events;
            if (!CanGamble) throw new InvalidOperationException("賭けられません。");
            SpendGold(s.gambleBet);
            var result = new GambleResult { value = RollForEvent(die, out bool refreshed), refreshed = refreshed };
            result.even = result.value % 2 == 0;
            result.win = result.even == betEven;
            if (result.win) result.payout = GainGold(s.gamblePayout);
            return result;
        }

        // ---- 落ちている賽 ----

        /// <summary>拾う：報酬に出るコモンのダイスから1つ（受け取るかは画面で。満杯なら入れ替え）。</summary>
        public DiceData FallenDiceCommon()
        {
            var commons = config.rewardDicePool.Where(d => d != null && d.rarity == Rarity.Common).ToList();
            return commons.Count > 0 ? commons[random.Event.Next(commons.Count)] : null;
        }

        /// <summary>
        /// よく調べる：50%でアンコモンのダイス（受け取るかは画面で）、50%で欠け賽（呪い。その場でポーチに入る）。
        /// 欠け賽のときは cursed が入り、ポーチが満杯なら入らない（cursed は null のまま、curseRejected が true）。
        /// </summary>
        public DiceData ExamineFallenDice(out DiceInstance cursed, out bool curseRejected)
        {
            cursed = null;
            curseRejected = false;
            if (random.Event.Next(100) < config.events.fallenUncommonPercent)
            {
                var uncommons = config.rewardDicePool.Where(d => d != null && d.rarity == Rarity.Uncommon).ToList();
                if (uncommons.Count > 0) return uncommons[random.Event.Next(uncommons.Count)];
            }
            var curse = config.curseDice;
            if (curse == null) return null;
            // TODO(仕様): ポーチが満杯のときは欠け賽が入らない（罠の呪いと同じ扱い）
            if (pouch.IsFull) curseRejected = true;
            else cursed = AddDice(curse);
            return null;
        }

        /// <summary>呪いのダイス（欠け賽）を押し付ける（敵の「呪い」など）。ポーチが満杯なら入らず null。</summary>
        public DiceInstance ForceCurse()
        {
            var curse = config.curseDice != null ? config.curseDice : config.curseDicePool.FirstOrDefault(d => d != null);
            if (curse == null || pouch.IsFull) return null;
            return AddDice(curse);
        }

        // ---- 古びた祠 ----

        /// <summary>祠で付けられる刻印（ランダム）。HP は付けたときに払う。</summary>
        public EngravingData ShrineEngraving()
        {
            var pool = config.engravingPool.Where(e => e != null).ToList();
            return RewardGenerator.PickOne(random.Event, AscensionRules.RarityWeights(config.rewards.engravingRarity.For(LayerIndex), Ascension), pool, e => e.rarity);
        }

        /// <summary>HP を払える（払っても倒れない）か。</summary>
        public bool CanPayShrine => player.hp > config.events.shrineHpCost;

        public void ShrineEngrave(DiceInstance die, int faceIndex, EngravingData engraving)
        {
            if (!CanPayShrine) throw new InvalidOperationException("HP が足りません。");
            ApplyEngraving(die, faceIndex, engraving);
            player.LoseHp(config.events.shrineHpCost);
        }

        /// <summary>お参り：HP を回復。実際に回復した量を返す。</summary>
        public int ShrinePray()
        {
            int before = player.hp;
            player.Heal(config.events.shrinePrayHeal);
            return player.hp - before;
        }

        // ---- 狐の嫁入り ----

        /// <summary>移動の出目に足す数が残っているターン数（狐の嫁入り）。</summary>
        public int MoveBonusTurns { get; private set; }
        public int MoveBonus { get; private set; }

        public void FollowFox()
        {
            MoveBonusTurns = config.events.foxTurns;
            MoveBonus = config.events.foxMoveBonus;
        }

        public int SeeOffFox() => SeeOffFox(out _);

        /// <summary>見送る：お守り1個（持てないとき・お守りの候補がないときは代わりにゴールド）。得たゴールドを返す。</summary>
        public int SeeOffFox(out CharmData charm)
        {
            charm = CanAddCharm ? PickCharm(random.Event) : null;
            if (charm != null && AddCharm(charm)) return 0;
            charm = null;
            return GainGold(config.events.foxSeeOffGold);
        }

        // ---- 韋駄天の足跡 ----

        /// <summary>ダイスを振らずに steps 歩進む移動を始める（韋駄天の足跡）。進み方は StepMove、終わりは FinishMove。</summary>
        public MoveInProgress BeginForcedMove(int steps)
        {
            return new MoveInProgress
            {
                dice = null,
                faceIndex = -1,
                value = steps,
                remaining = steps,
                from = Current,
                availableDiceBefore = new List<DiceInstance>(pouch.Available),
            };
        }
    }
}
