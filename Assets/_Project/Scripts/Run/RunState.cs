using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    public struct MoveResult
    {
        public DiceInstance dice;
        public int value;
        public TileNode from;
        public TileNode to;
        public IReadOnlyList<TileNode> passed;
        public bool refreshed;
        public int availableBefore;   // 振る前に使用可能だったダイスの数（記録用）
        public IReadOnlyList<DiceInstance> availableDiceBefore;   // 振る前に使用可能だったダイス（記録用）
    }

    /// <summary>1ラン（フェーズ0では1層）の状態。表示には依存しない。</summary>
    public partial class RunState
    {
        public readonly GameConfig config;
        public readonly RunRandom random;
        /// <summary>いまの層の盤面。層が進むと作り直す。</summary>
        public BoardData board { get; private set; }
        public readonly DicePouch pouch = new DicePouch();
        public readonly Combatant player;
        public readonly EffectBus effects = new EffectBus();
        readonly List<RelicData> relics = new List<RelicData>();

        public int Gold { get; private set; }
        public IReadOnlyList<RelicData> Relics => relics;
        public TileNode Current { get; private set; }
        public int Turn { get; private set; }
        public bool ReachedGoal => Current == board.Goal;
        public int TilesToGoal => board.DistanceToGoal(Current);

        /// <summary>
        /// ランの始めに選ぶスターターの候補（count 個）。報酬に出る全部のダイスから、通常戦の報酬と同じレア度の重みで選ぶ
        /// （開発者の判断：決まった3つから選ぶより、毎回ちがう始まり方にする）。シードが同じなら同じ候補。
        /// </summary>
        public static List<DiceData> StarterOptions(GameConfig config, int seed, int count = 3)
        {
            var rng = new SeededRandom(RunRandom.Mix(seed, 60));
            return RewardGenerator.PickDice(rng, config.rewards.normalRarityWeights, config.rewardDicePool, count);
        }

        /// <param name="starter">スターターダイス（初期ポーチの最後に加える）。null なら加えない。</param>
        public RunState(GameConfig config, int seed, DiceData starter = null)
        {
            this.config = config;
            random = new RunRandom(seed);
            board = GenerateBoard();
            player = new Combatant(config.playerMaxHp);
            foreach (var data in config.startingDice) pouch.Add(new DiceInstance(data));
            if (starter != null) pouch.Add(new DiceInstance(starter));
            Gold = config.startingGold;
            Current = board.Start;
            pouch.Refreshed += OnPouchRefreshed;
        }

        /// <summary>いま戦っている戦闘（戦闘中でなければ null）。BattleState が出入りを知らせる。</summary>
        public BattleState CurrentBattle { get; internal set; }

        /// <summary>この層で移動した回数（1回休みは数えない）。レリック「早馬」が見る。</summary>
        public int MovesThisLayer { get; private set; }

        /// <summary>リフレッシュが起きたときの効果（レリック「鈴」など）。戦闘中かどうかは ctx.battle でわかる。</summary>
        void OnPouchRefreshed()
        {
            effects.Fire(new EffectContext(Trigger.OnRefresh) { run = this, player = player, battle = CurrentBattle, enemy = CurrentBattle?.enemy });
            Refreshed?.Invoke(CurrentBattle != null);
        }

        /// <summary>リフレッシュの効果を処理したあと（引数は戦闘中か）。画面の表示用。</summary>
        public event Action<bool> Refreshed;

        // ---- ゴールド ----

        /// <summary>ゴールドを得る。OnGoldGain の効果（銭袋など）で量が変わる。実際に得た量を返す。</summary>
        /// <param name="fromBattle">戦闘の報酬で得るゴールドか（銭袋が効く）。</param>
        public int GainGold(int amount, bool fromBattle = false)
        {
            if (amount <= 0) return 0;
            var ctx = effects.Fire(new EffectContext(Trigger.OnGoldGain) { run = this, player = player, amount = amount, fromBattle = fromBattle });
            int gained = System.Math.Max(0, ctx.amount);
            Gold += gained;
            stats.goldEarned += gained;
            return gained;
        }

        /// <summary>ゴールドを払う。足りなければ払わずに false。</summary>
        public bool SpendGold(int amount)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            return true;
        }

        // ---- 報酬とダイスの出し入れ ----

        /// <summary>戦闘報酬を決める（報酬用の乱数を使う）。ゴールドはまだ受け取らない。</summary>
        public BattleReward CreateBattleReward(RewardKind kind)
        {
            var reward = RewardGenerator.ForBattle(kind, random.Reward, config.rewards, config.rewardDicePool);
            // エリートはレリック確定（仕様書 第11章）
            if (kind == RewardKind.Elite) reward.relic = PickRelic();
            // 通常戦は40%でお守り（お守りの候補がないときは乱数を使わない）
            if (kind == RewardKind.Normal && config.charmPool.Any(c => c != null) && random.Reward.Next(100) < config.rewards.normalCharmPercent)
                reward.charm = PickCharm();
            return reward;
        }

        public bool CanAddDice => !pouch.IsFull;

        /// <summary>ダイスをポーチに加える（使用可能の状態で入る）。満杯なら例外。</summary>
        public DiceInstance AddDice(DiceData data) => AddDice(data, false);

        /// <summary>
        /// replacing：手放したダイスの代わりに入れる（数は増えない）。黄金の賽筒で容量が減って
        /// 容量より多く持っているときでも、入れ替え・複製・交換はできるようにする。
        /// </summary>
        DiceInstance AddDice(DiceData data, bool replacing)
        {
            var die = new DiceInstance(data);
            if (replacing) pouch.ForceAdd(die);
            else pouch.Add(die);
            stats.diceGained.Add(die.DisplayName);
            NotifyAcquired("dice", die.DisplayName);
            return die;
        }

        /// <summary>満杯のとき：old を手放して data を受け取る。</summary>
        public DiceInstance ReplaceDice(DiceInstance old, DiceData data)
        {
            if (old.data != null && old.data.rarity == Rarity.Curse) throw new InvalidOperationException("呪いのダイスは入れ替えられません。");
            pouch.Remove(old);
            NotifyAcquired("discard", old.DisplayName);
            return AddDice(data, true);
        }

        /// <summary>このランでダイスを削除した回数（ショップの削除の値段が上がる）。</summary>
        public int RemovedDiceCount { get; private set; }

        /// <summary>ダイスを削除する（ショップ・イベント）。呪いのダイスも削除できる。</summary>
        public void RemoveDice(DiceInstance die)
        {
            pouch.Remove(die);
            RemovedDiceCount++;
            stats.diceRemoved.Add(die.DisplayName);
            NotifyAcquired("remove", die.DisplayName);
        }

        /// <summary>ポーチの容量より多く持っているか（黄金の賽筒で容量が減ったときなど）。</summary>
        public bool OverCapacity => pouch.All.Count > pouch.Capacity;

        /// <summary>容量を超えた分のダイスを手放す（ショップの削除の回数には数えない）。</summary>
        public void DiscardDice(DiceInstance die)
        {
            pouch.Remove(die);
            stats.diceRemoved.Add(die.DisplayName);
            NotifyAcquired("discard", die.DisplayName);
        }

        /// <summary>ショップの品揃えを決める（報酬用の乱数）。</summary>
        public Shop CreateShop() => new Shop(this, config.shop);

        /// <summary>ダイスの報酬をスキップすると、代わりにゴールドを得る（仕様書 第11章）。</summary>
        public int SkipDiceReward() => GainGold(config.rewards.skipGold);

        // ---- レリック ----

        readonly Dictionary<EffectSO, int> charges = new Dictionary<EffectSO, int>();

        /// <summary>レリックを手に入れる。手に入れたときの効果（大きな巾着など）はここで1回だけ働く。</summary>
        public void AddRelic(RelicData relic)
        {
            if (relic == null || relics.Contains(relic)) return;
            relics.Add(relic);
            effects.Register(relic);
            NotifyAcquired("relic", relic.displayName);
            var ctx = new EffectContext(Trigger.OnAcquire) { run = this, player = player };
            foreach (var effect in relic.effects)
            {
                if (effect is ChargedMoveAdjustEffect charged) charges[effect] = charged.chargesPerLayer;
                if (effect != null && effect.trigger == Trigger.OnAcquire) effect.Apply(ctx);
            }
        }

        public bool HasRelic(string id) => relics.Exists(r => r.id == id);

        /// <summary>回数つきの効果（草鞋など）の残り回数。</summary>
        public int ChargesOf(EffectSO effect) => effect != null && charges.TryGetValue(effect, out int n) ? n : 0;

        /// <summary>持っているレリックの残り回数（回数つきの効果がなければ -1）。表示用。</summary>
        public int ChargesOf(RelicData relic)
        {
            foreach (var effect in relic.effects)
            {
                if (effect is ChargedMoveAdjustEffect) return ChargesOf(effect);
            }
            return -1;
        }

        /// <summary>報酬・宝箱・ショップなどに出せるレリックか（まだ持っていない、今の層で出てよい）。</summary>
        public bool RelicCanAppear(RelicData r) => r != null && !relics.Contains(r) && (r.maxLayer <= 0 || LayerIndex + 1 <= r.maxLayer);

        /// <summary>まだ持っていないレリックを1つ選ぶ（報酬用の乱数）。候補がなければ null。</summary>
        // TODO(仕様): レリックのレア度による出やすさは仮（RewardSettings.relicRarity。層が進むほどレアが出やすい）
        public RelicData PickRelic()
        {
            var candidates = config.relicPool.FindAll(RelicCanAppear);
            // レア度で出やすさを変える（コモンが出やすく、レアは出にくい）
            return RewardGenerator.PickOne(random.Reward, config.rewards.relicRarity.For(LayerIndex), candidates, r => r.rarity);
        }

        /// <summary>ダイスを1個振って進む（1ターン）。ダイスは使用済みになる。</summary>
        /// <summary>
        /// ダイスを1個振って進む（1ターン）。ダイスは使用済みになる。
        /// 分岐では chooseBranch で道を選ぶ（省略時は最初の道）。画面で1歩ずつ見せるときは BeginMove / StepMove / FinishMove を使う。
        /// </summary>
        public MoveResult Move(DiceInstance die, Func<TileNode, IReadOnlyList<TileNode>, TileNode> chooseBranch = null)
        {
            var move = BeginMove(die);
            while (!move.Done)
            {
                TileNode choice = null;
                if (NeedsBranchChoice(move)) choice = chooseBranch != null ? chooseBranch(Current, Current.next) : Current.next[0];
                StepMove(move, choice);
            }
            return FinishMove(move);
        }

        /// <summary>移動の途中の状態。</summary>
        public class MoveInProgress
        {
            public DiceInstance dice;
            public int faceIndex;
            public int value;
            public int remaining;
            // 出目を ±adjust の中から選び直せる（刻印「風」など）。選び直すまでは value のまま
            public int adjust;
            public string adjustLabel;                 // 「風」「草鞋」など
            public ChargedMoveAdjustEffect adjustCharge; // 回数つき（草鞋）なら、変えたときに1回減る
            public bool Adjustable => adjust > 0 && remaining == value && passed.Count == 0;
            public TileNode from;
            public bool refreshed;
            public bool canReroll;          // 再転：振り直してよい（まだ動いていないときだけ）
            public TileNode forcedTarget;   // 帰り道：行き先が決まっている
            public int foxBonus;            // 出目に足した数（狐の嫁入り・進み御札・止まり御札）
            public List<DiceInstance> availableDiceBefore;
            public readonly List<TileNode> passed = new List<TileNode>();
            public bool Done => remaining <= 0;
        }

        /// <summary>移動を始める：ダイスを振って使用済みにし、ターンを進める。まだ1歩も動かない。</summary>
        public MoveInProgress BeginMove(DiceInstance die)
        {
            if (ReachedGoal) throw new InvalidOperationException("ゴールに着いているので進めません。");
            if (!CanMoveWith(die)) throw new InvalidOperationException($"{die.DisplayName} は移動に使えません。");

            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            var move = new MoveInProgress
            {
                dice = die,
                from = Current,
                availableDiceBefore = new List<DiceInstance>(pouch.Available),
            };
            Turn++;
            MovesThisLayer++;
            RollForMove(move, true);
            // 出目に足す数（振り直しても足したまま）：狐の嫁入り（次の数ターン）と、振る前に使った進み御札・止まり御札
            int bonus = 0;
            if (MoveBonusTurns > 0 && move.forcedTarget == null)
            {
                bonus += MoveBonus;
                MoveBonusTurns--;
            }
            if (PendingMoveBonus != 0)
            {
                if (move.forcedTarget == null) bonus += PendingMoveBonus;
                PendingMoveBonus = 0;
            }
            if (bonus != 0)
            {
                move.foxBonus = bonus;
                move.value = MoveValueWithBonus(move.value, bonus);
                move.remaining = move.value;
            }
            ApplyPendingMoveCharms(move); // 振り直し御札：出目を見てから1回振り直せる
            PendingMove = move; // 進み始めるまでは、移動のお守りでこの移動を変えられる
            return move;
        }

        /// <summary>
        /// 移動のダイスを振る（最初の1回と、再転での振り直し）。
        /// 出た面の刻印・ダイスの特徴・レリックが効く：振ったとき（小判・錆び賽・小石など）→ 移動で振ったとき（風・黄金賽・早馬など）。
        /// </summary>
        void RollForMove(MoveInProgress move, bool first)
        {
            var die = move.dice;
            // 鏡賽・爆賽などの特別なルールを含めて振る
            int rolledValue = RollMoveDie(die, out int faceIndex); // 千里眼で決めた出目があればそれ
            var engraving = die.faces[faceIndex].engraving;
            var ctx = new EffectContext(Trigger.OnRoll) { run = this, player = player, dice = die, faceIndex = faceIndex, value = rolledValue };
            effects.Fire(ctx, die, engraving);
            // 使用済みにする（小石なら使用可能のまま。ピンゾロ賽は移動では使用済みになる）。最後の1個ならリフレッシュ。振り直しのときはもう使用済み
            if (first) move.refreshed = pouch.Use(die, ctx.keepAvailable, false);
            ctx.trigger = Trigger.OnMoveRolled;
            effects.Fire(ctx, die, engraving);
            int value = Math.Max(0, ctx.value);
            LastRolledValue = value;

            move.faceIndex = faceIndex;
            move.value = MoveValueWithBonus(value, move.foxBonus);
            move.remaining = move.value;
            move.adjust = ctx.moveAdjust;
            move.adjustLabel = ctx.moveAdjustLabel;
            move.adjustCharge = ctx.moveAdjustCharge;
            move.canReroll = first && ctx.canReroll;
            move.forcedTarget = null;

            // 帰り道：次の休憩マスかショップまで一気に進む（ボスマスは越えない）
            if (ctx.warpToRestOrShop)
            {
                var target = NearestRestOrShop(Current, out int distance);
                if (target != null)
                {
                    move.forcedTarget = target;
                    move.value = distance;
                    move.remaining = distance;
                    move.adjust = 0;
                }
            }
        }

        /// <summary>次の移動で出目に足す数（狐の嫁入り・進み御札・止まり御札）。</summary>
        public int NextMoveBonus => (MoveBonusTurns > 0 ? MoveBonus : 0) + PendingMoveBonus;

        /// <summary>出目に足す数を足した進む数。止まり御札で減らしたときは最低1。</summary>
        static int MoveValueWithBonus(int value, int bonus) => bonus < 0 ? Math.Max(1, value + bonus) : value + bonus;

        /// <summary>再転：出た面を振り直す（移動を始める前に1回だけ）。</summary>
        public void RerollMove(MoveInProgress move)
        {
            if (!move.canReroll || move.passed.Count > 0 || move.remaining != move.value) throw new InvalidOperationException("振り直せません。");
            RollForMove(move, false);
        }

        /// <summary>振り直し御札：進み始める前なら振り直せる（再転とは別）。</summary>
        void RerollMoveByCharm(MoveInProgress move)
        {
            if (move.passed.Count > 0 || move.forcedTarget != null) throw new InvalidOperationException("振り直せません。");
            RollForMove(move, false);
        }

        /// <summary>from から前に進んで一番近い休憩マスかショップ（ボスより先には行かない）。なければ null。</summary>
        public static TileNode NearestRestOrShop(TileNode from, out int distance)
        {
            var dist = new Dictionary<TileNode, int> { [from] = 0 };
            var queue = new Queue<TileNode>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (n != from && (n.type == TileType.Rest || n.type == TileType.Shop))
                {
                    distance = dist[n];
                    return n;
                }
                if (n.type == TileType.Boss) continue;
                foreach (var next in n.next)
                {
                    if (dist.ContainsKey(next)) continue;
                    dist[next] = dist[n] + 1;
                    queue.Enqueue(next);
                }
            }
            distance = 0;
            return null;
        }

        /// <summary>直前に振ったダイスの出目（移動でも戦闘でも）。鏡賽が写す。まだ振っていなければ -1。</summary>
        public int LastRolledValue { get; set; } = -1;

        /// <summary>移動に使えるダイスか（大賽は戦闘専用）。</summary>
        public static bool CanMoveWith(DiceInstance die) => die.data == null || !die.data.cannotMove;

        /// <summary>使用可能なダイスがすべて移動に使えない（大賽だけなど）ので、「1回休み」するしかないか。</summary>
        public bool MustSkipTurn => !ReachedGoal && pouch.AvailableCount > 0 && pouch.Available.All(d => !CanMoveWith(d));

        /// <summary>
        /// 1回休み（仕様書 第4章「設計のメモ」）：移動に使えないダイスを使用済みにしてターンを進める。
        /// 使用可能が0個になるのでリフレッシュが起きる。リフレッシュしたら true。
        /// </summary>
        public bool SkipTurn()
        {
            if (!MustSkipTurn) throw new InvalidOperationException("移動に使えるダイスがあります。");
            bool refreshed = false;
            foreach (var d in pouch.Available.ToList()) refreshed |= pouch.Use(d);
            Turn++;
            return refreshed;
        }

        /// <summary>止まりうるマスの確率（鏡賽・爆賽の特別なルール込み）。千里眼で出目が見えているダイスは、その出目だけ。</summary>
        public Dictionary<TileNode, float> ReachOf(DiceInstance die)
        {
            // TODO(仕様): 千里眼で見える出目は、早馬・狐の嫁入りなどの加算の前の値
            // 次の移動で出目に足す数（狐の嫁入り・進み御札・止まり御札）も込みで見せる
            int bonus = NextMoveBonus;
            if (foreseen.TryGetValue(die, out var roll)) return ReachCalculator.Compute(Current, new[] { MoveValueWithBonus(roll.value, bonus) });
            var dist = DiceRoller.Distribution(die, LastRolledValue);
            if (bonus != 0) dist = dist.Select(x => (MoveValueWithBonus(x.value, bonus), x.probability)).ToList();
            return ReachCalculator.Compute(Current, dist);
        }

        /// <summary>出目を選び直す（刻印「風」など）。まだ1歩も進んでいないときだけ、value ± adjust の範囲で（0未満にはしない）。</summary>
        public void AdjustMove(MoveInProgress move, int newValue)
        {
            if (!move.Adjustable) throw new InvalidOperationException("出目を変えられません。");
            int min = Math.Max(0, move.value - move.adjust);
            int max = move.value + move.adjust;
            if (newValue < min || newValue > max) throw new ArgumentOutOfRangeException(nameof(newValue));
            if (newValue != move.value && move.adjustCharge != null) charges[move.adjustCharge] = ChargesOf(move.adjustCharge) - 1;
            move.value = newValue;
            move.remaining = newValue;
            move.adjust = 0;
        }

        // ---- 鍛冶（仕様書 第5章） ----

        /// <summary>鍛冶で提示する刻印（重ならないよう count 個。報酬用の乱数）。</summary>
        public List<EngravingData> CreateForgeOffer(int count = 3)
        {
            var pool = config.engravingPool.FindAll(e => e != null);
            var offer = new List<EngravingData>();
            while (offer.Count < count && pool.Count > 0)
            {
                // レア度で出やすさを変える（コモンが出やすく、レアは出にくい）
                var e = RewardGenerator.PickOne(random.Reward, config.rewards.engravingRarity.For(LayerIndex), pool, x => x.rarity);
                offer.Add(e);
                pool.Remove(e);
            }
            return offer;
        }

        /// <summary>
        /// 刻印を付ける。数値刻印は面の数値を変え（0〜9。元から9を超える面は、それより大きくはしない）、
        /// 効果刻印は面に付ける（すでにあれば上書き）。
        /// </summary>
        public void ApplyEngraving(DiceInstance die, int faceIndex, EngravingData engraving)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (!CanForge(die)) throw new InvalidOperationException($"{die.DisplayName} は鍛冶で改造できません。");
            die.faces[faceIndex] = EngravedFace(die, faceIndex, engraving);
            string record = $"{engraving.displayName}→{die.DisplayName}";
            stats.engravings.Add(record);
            NotifyAcquired("engraving", record);
        }

        /// <summary>鍛冶で改造できるダイスか（ピンゾロ賽はできない）。</summary>
        public static bool CanForge(DiceInstance die) => die.data == null || !die.data.cannotForge;

        /// <summary>
        /// die の faceIndex の面に engraving を付けたあとの面（画面の予告にも使う）。
        /// 写しは、同じダイスのほかの面のうち一番大きい値をコピーする。
        /// TODO(仕様): 写しのコピー元はプレイヤーに選ばせず、一番大きい面にする
        /// </summary>
        public static Face EngravedFace(DiceInstance die, int faceIndex, EngravingData engraving)
        {
            var face = die.faces[faceIndex];
            if (engraving.kind == EngravingKind.Numeric && engraving.op == NumericOp.CopyFace)
            {
                int best = face.value;
                for (int i = 0; i < die.faces.Length; i++)
                {
                    if (i != faceIndex) best = Math.Max(best, die.faces[i].value);
                }
                face.value = best;
                return face;
            }
            return Engraved(face, engraving);
        }

        /// <summary>face に engraving を付けたあとの面（画面の予告にも使う）。</summary>
        public static Face Engraved(Face face, EngravingData engraving)
        {
            if (engraving.kind == EngravingKind.Numeric)
            {
                int next;
                switch (engraving.op)
                {
                    case NumericOp.Set: next = engraving.amount; break;
                    case NumericOp.CopyFace: next = face.value; break; // 写しは EngravedFace でコピー元を決めてから呼ぶ
                    default: next = face.value + engraving.amount; break;
                }
                // TODO(仕様): 博打賽の10のように元から9を超える面は、増強しても元の値より上げない
                int upper = Math.Max(Face.MaxValue, face.value);
                face.value = Math.Max(Face.MinValue, Math.Min(upper, next));
            }
            else
            {
                face.engraving = engraving;
            }
            return face;
        }

        /// <summary>今いるマスが分岐点で、まだ進む歩数が残っているか（道を選ぶ必要があるか）。</summary>
        public bool NeedsBranchChoice(MoveInProgress move) => !move.Done && Current.IsBranch;

        /// <summary>
        /// 1歩進む。分岐点では next を選ぶ（分岐で選ぶこと自体は歩数を使わない）。
        /// ゴール・ボスマスに入ったら、歩数が余っていてもそこで止まる。
        /// </summary>
        public TileNode StepMove(MoveInProgress move, TileNode next = null)
        {
            if (move.Done) return Current;
            if (Current.IsEnd)
            {
                move.remaining = 0;
                return Current;
            }
            if (next == null || !Current.next.Contains(next)) next = Current.next[0];

            if (PendingMove == move) PendingMove = null; // 進み始めたら、移動のお守りはもう効かない
            if (Current != move.from) move.passed.Add(Current);
            Current = next;
            CountStep(); // 貯金箱
            move.remaining--;
            if (Current.type == TileType.Boss || Current.IsEnd || Current.stopHere) move.remaining = 0; // ボスの手前の休憩でも止まる
            return Current;
        }

        public MoveResult FinishMove(MoveInProgress move)
        {
            if (PendingMove == move) PendingMove = null;
            if (Current != move.from) stats.CountStop(Current.type);
            return new MoveResult
            {
                dice = move.dice,
                value = move.value,
                from = move.from,
                to = Current,
                passed = move.passed,
                refreshed = move.refreshed,
                availableBefore = move.availableDiceBefore.Count,
                availableDiceBefore = move.availableDiceBefore,
            };
        }

        // ---- マスの中身 ----

        /// <summary>休憩の「休む」で回復する量（最大HPの restHealPercent%、切り捨て）。</summary>
        public int RestHealAmount => HasRule(RunRule.NoRestHeal) ? 0 : player.maxHp * (config.restHealPercent * (100 + StatBonus(RunStat.RestHealPercent)) / 100) / 100;

        /// <summary>罠：ダメージ・封印・呪いのどれか（ランダム。開発者の判断でイベント系はランダムでよい）。</summary>
        public TrapResult TriggerTrap()
        {
            var s = config.tiles;
            var kind = (TrapKind)random.Map.Next(3);
            // TODO(仕様): 呪いのダイスを入れられない（ポーチが満杯・データがない）ときや、封印できるダイスがないときはダメージにする
            var curses = config.curseDicePool.FindAll(d => d != null);
            if (curses.Count == 0 && config.curseDice != null) curses.Add(config.curseDice);
            if (kind == TrapKind.Curse && (curses.Count == 0 || pouch.IsFull)) kind = TrapKind.Damage;
            if (kind == TrapKind.Seal && pouch.AvailableCount == 0) kind = TrapKind.Damage;

            var result = new TrapResult { kind = kind };
            switch (kind)
            {
                case TrapKind.Seal:
                    // 封印は次の戦闘が終わるまで（戦闘終了で解除される）。敵の封印と同じく、ランダムに1個だけ（前の封印は解放）
                    result.sealedDie = pouch.SealRandom(random.Map);
                    result.message = $"罠だ！ {result.sealedDie.DisplayName} が封じられた（次の戦闘が終わるまで）。";
                    break;
                case TrapKind.Curse:
                    var curse = curses[random.Map.Next(curses.Count)];
                    result.curseDie = AddDice(curse);
                    result.message = $"罠だ！ 呪いの {curse.displayName} を押し付けられた。";
                    break;
                default:
                    result.damage = player.TakeAttack(s.trapDamage);
                    result.message = $"罠だ！ {result.damage} ダメージを受けた。";
                    break;
            }
            return result;
        }

        /// <summary>宝箱：ゴールドかレリック（半々）。レリックは受け取るか選ぶ（AddRelic で受け取る）。まれにアンコモン以上のダイスが付いてくる（持っていくかは選ぶ）。</summary>
        public TreasureResult OpenTreasure()
        {
            var s = config.tiles;
            var rng = random.Reward;
            var result = new TreasureResult();

            bool anyRelic = config.relicPool.Exists(RelicCanAppear);
            if (anyRelic && rng.Next(100) < s.treasureRelicPercent)
            {
                result.relic = PickRelic();
                // 受け取るかは選ぶ（開発者の要望）。受け取るなら呼び出し側で AddRelic
                result.message = $"宝箱を開けた！ レリック「{result.relic.displayName}」が入っていた。";
            }
            else
            {
                result.gold = GainGold(rng.Next(s.treasureGoldMin, s.treasureGoldMax + 1));
                result.message = $"宝箱を開けた！ {result.gold} G を手に入れた。";
            }

            if (rng.Next(100) < s.treasureDicePercent)
            {
                var offer = RewardGenerator.PickDice(rng, new[] { 0, 75, 25 }, config.rewardDicePool, 1);
                if (offer.Count > 0) result.diceOffer = offer[0];
            }
            return result;
        }

        /// <summary>
        /// 通過マス（祠・関所・茶屋・賽場）の効果。stopped なら2倍（仕様書 第8章）。
        /// 通過・停止のどちらでも、OnPassTile / OnStopTile の効果も発動する。
        /// </summary>
        public PassTileResult ApplyPassTile(TileNode tile, bool stopped)
        {
            var s = config.tiles;
            int m = stopped ? 2 : 1;
            var result = new PassTileResult();
            string verb = stopped ? "に止まった" : "を通った";
            switch (tile.type)
            {
                case TileType.Shrine:
                    result.gold = GainGold(s.shrineGold * m);
                    result.message = $"祠{verb}：{result.gold} G を得た。";
                    break;
                case TileType.Checkpoint:
                    int toll = s.checkpointToll * m;
                    if (SpendGold(toll))
                    {
                        result.gold = -toll;
                        result.message = $"関所{verb}：{toll} G を払った。";
                    }
                    else
                    {
                        result.damage = player.TakeAttack(s.checkpointDamage * m);
                        result.message = $"関所{verb}：払えないので {result.damage} ダメージ。";
                    }
                    break;
                case TileType.Teahouse:
                    int before = player.hp;
                    player.Heal(s.teahouseHeal * m);
                    result.healed = player.hp - before;
                    result.message = $"茶屋{verb}：HP を {result.healed} 回復した。";
                    break;
                case TileType.DiceHall:
                    // TODO(仕様): 戻すダイスは選ばせず、使用済みのうち出目の平均が最も高いものから戻す
                    for (int i = 0; i < s.diceHallReturn * m; i++)
                    {
                        DiceInstance best = null;
                        double bestAverage = double.MinValue;
                        foreach (var d in pouch.All)
                        {
                            if (d.state != DiceState.Used) continue;
                            double average = 0;
                            foreach (var f in d.faces) average += f.value;
                            average /= d.faces.Length;
                            if (average > bestAverage) { best = d; bestAverage = average; }
                        }
                        if (best == null) break;
                        best.state = DiceState.Available;
                        result.returnedDice.Add(best);
                    }
                    result.message = result.returnedDice.Count > 0
                        ? $"賽場{verb}：{string.Join("・", result.returnedDice.ConvertAll(d => d.DisplayName))} が使えるようになった。"
                        : $"賽場{verb}：使用済みのダイスがない。";
                    break;
                default:
                    result.message = "";
                    break;
            }
            effects.Fire(new EffectContext(stopped ? Trigger.OnStopTile : Trigger.OnPassTile) { run = this, player = player, tile = tile });
            return result;
        }

        /// <summary>休憩：最大HPの restHealPercent% を回復（切り捨て）。実際に回復した量を返す。</summary>
        public int Rest()
        {
            int before = player.hp;
            player.Heal(RestHealAmount);
            return player.hp - before;
        }

        int normalBattles;
        EnemyData lastEnemy;

        /// <summary>直前に PickEnemy で選んだ敵の HP の倍率（%）。前の層の敵なら 150 など。</summary>
        public int LastEnemyHpPercent { get; private set; } = 100;

        /// <summary>
        /// そのマスで戦う敵。ボスマスはボス、それ以外は null。
        /// 戦闘マスは仕様書 第7章「敵の出現ルール」に従う：最初の数戦は弱めの敵だけ、同じ敵は2戦続けない。
        /// </summary>
        public EnemyData PickEnemy(TileNode tile)
        {
            LastEnemyHpPercent = 100;
            // 地図師の矢立で前もって決めた敵があれば、それが出る
            if (TakePlannedEnemy(tile, out var planned))
            {
                if (tile.type == TileType.Battle)
                {
                    normalBattles++;
                    lastEnemy = planned;
                }
                return planned;
            }
            var layer = Layer;
            switch (tile.type)
            {
                case TileType.Battle:
                    return PickNormalEnemy();
                case TileType.Elite:
                    return layer.eliteEnemies.Count > 0 ? layer.eliteEnemies[random.Battle.Next(layer.eliteEnemies.Count)] : PickNormalEnemy();
                case TileType.Boss:
                    return LayerBoss;
                default:
                    return null;
            }
        }

        EnemyData PickNormalEnemy()
        {
            var layer = Layer;
            // 層が上がるごとに、前の層の敵もまれに出る（HP 増し。仕様書 第7章）
            if (LayerIndex > 0 && normalBattles >= layer.earlyBattleCount && random.Battle.Next(100) < config.previousLayerEnemyPercent)
            {
                var previous = new List<EnemyData>(LayerAt(LayerIndex - 1).battleEnemies);
                previous.Remove(lastEnemy);
                if (previous.Count > 0)
                {
                    var old = previous[random.Battle.Next(previous.Count)];
                    normalBattles++;
                    lastEnemy = old;
                    LastEnemyHpPercent = config.previousLayerEnemyHpPercent;
                    return old;
                }
            }

            var candidates = new List<EnemyData>(layer.battleEnemies);
            if (normalBattles < layer.earlyBattleCount)
            {
                var early = candidates.FindAll(e => e.earlyOk);
                if (early.Count > 0) candidates = early;
            }
            if (candidates.Count > 1) candidates.Remove(lastEnemy);

            var enemy = candidates[random.Battle.Next(candidates.Count)];
            normalBattles++;
            lastEnemy = enemy;
            return enemy;
        }
    }
}
