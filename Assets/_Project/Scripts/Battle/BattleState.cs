using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Battle
{
    public enum BattleOutcome
    {
        Ongoing,
        Victory,
        Defeat,
        Fled,      // 煙玉で逃げた（報酬なし）
    }

    /// <summary>このラウンドに振ったダイス1個と、その割り振り先。</summary>
    public class RolledDie
    {
        public DiceInstance dice;
        public int faceIndex;
        public int value;   // 出た面の値（OnRoll の効果のあと）。攻撃・防御に置いたときの値は BattleState.EffectiveValue
        public Assignment assignment;
        public bool canReroll;   // 振り直してよい（刻印「再転」。1回）
        public bool rerolled;
        public bool bothSides;   // 攻撃と防御の両方に効く（刻印「両刃」・レリック「六の加護」）
        public bool inverted;    // 裏返し（7−出目）になった（天邪鬼などの予告）
        public int rolledValue;  // 裏返す前の出目（裏返しでなければ value と同じ。演出用）
        public bool used;        // 「使う」で効果を発動した（もう割り振りは変えられない）

        /// <summary>全部の敵に当たるダイス（薙ぎ賽）か。</summary>
        public bool HitsAll => dice.data != null && dice.data.hitsAll;

        /// <summary>出目の数だけ1ダメージを与えるダイス（連撃賽）か。1発ごとに筋力が乗る。</summary>
        public bool MultiHit => dice.data != null && dice.data.multiHit;
    }

    /// <summary>「使う」1回の結果（演出用）。</summary>
    public class UseResult
    {
        public List<RolledDie> dice = new List<RolledDie>();   // 使ったダイス（振った順）
        public int attack;            // 攻撃値（筋力込み。薙ぎ賽の分も足した合計）
        public int blockGained;       // 得た防御
        public int hpBefore;          // 使う前の自分の HP
        public int attackHpChange;    // 攻撃したときの効果で増減した HP（吸血など）
        public int thornsTaken;       // 棘で受けたダメージ
        public int strengthGained;    // 自分が得た筋力（気合賽など）
        public bool weakened;         // 脱力のまま攻撃した（演出用：「脱力で 0.75 倍…」）
        public int fortifyGained;     // 自分が得た堅守（構え賽など）
        public int sharedBlock;       // 協力：山伏が仲間全員に分けた防御（1人ぶん）
        /// <summary>敵ごとの結果。hpBefore はこの「使う」の前の HP、dealt はこの「使う」で与えたダメージ。</summary>
        public List<EnemyRoundInfo> enemies = new List<EnemyRoundInfo>();
        public int Dealt => enemies.Sum(x => x.dealt);
    }

    public struct DamagePreview
    {
        public int dealt;      // 敵の HP に通るダメージ（敵が複数なら合計）
        public int taken;      // 自分の HP に通るダメージ（賽振りのように値が隠れているときは最大の場合）
        public int takenMin;   // 値が隠れているときの最小の場合（隠れていなければ taken と同じ）
        public bool TakenIsRange => takenMin != taken;
        // 表示の色分け用：効果（ダイス・刻印・レリック・筋力・脱力・弱体）で元の値より上がったら +1、下がったら −1、同じなら 0
        public int dealtTrend;  // 与えるダメージ（元の値＝攻撃に置いた出目の合計）
        public int takenTrend;  // 受ける攻撃（元の値＝予告の値）
        public bool killsAll;   // この攻撃（とそのあとの敵の毒）で、敵が全員倒れる
        public int[] dealtPerEnemy;  // 敵ごとに HP に通るダメージ（敵の HP の棒に「−8」と出す）
    }

    /// <summary>1ラウンドの、敵1体ぶんの結果（演出用）。</summary>
    public class EnemyRoundInfo
    {
        public EnemyState enemy;
        public int index;
        public int hpBefore;           // ラウンドの始めの HP
        public int dealt;              // 攻撃で受けたダメージ
        public bool killedByAttack;
        public bool acted;             // 行動した（攻撃で倒れていない）
        public Intent intent;
        public int taken;              // この敵の攻撃でプレイヤーが受けたダメージ
        public DiceInstance sealedDie;
        public int sealedCount;
        public DiceInstance curseDie;
        public bool staggered;
        public int poisonDamage;       // 毒で受けたダメージ（プレイヤーの攻撃のあと、敵の行動の前）
        public bool diedOfPoison;
        public int strengthGained;     // 仲間が倒れて得た筋力（双子鬼）
        public List<DiceInstance> resetDice;   // 振り出しに戻れで使用済みにされたダイス（双六の番人）
        public int weakGiven, vulnerableGiven, frailGiven; // プレイヤーの攻撃で与えた脱力・弱体・脆弱（演出用）
        public int blockGained;            // 行動のあとに得た防御
        public DiceInstance rewrittenDie;  // 運命の書き換えで面を1にされたダイス（八面）
        public int rewrittenFrom;          // 書き換えられる前の面の値
        public bool enteredPhase2;         // このラウンドから第2形態（八面）
        public bool vulnerableHit;         // 弱体が乗った攻撃が当たった（演出用：「弱体で 1.5 倍！」）
    }

    public struct RoundResult
    {
        public int round;
        public int dealt;                // 敵全体に与えたダメージ
        public int taken;                // 受けたダメージの合計
        public Intent enemyIntent;       // 狙っていた敵（敵が1体ならその敵）の予告
        public IReadOnlyList<RolledDie> rolled;
        public DiceInstance sealedDie;   // 封印されたダイス（なければ null。2個以上なら最初の1個）
        public int sealedCount;          // 封印したダイスの数
        public DiceInstance curseDie;    // 呪いで押し付けられたダイス（なければ null）
        public bool staggered;           // 溜めを止めた（敵は次のラウンド怯む）
        public int enemyPoisonDamage;    // 毒で敵が受けたダメージ（合計。プレイヤーの攻撃のあと、敵の行動の前）
        public int playerPoisonDamage;
        public int thornsDamage;         // 棘で受けたダメージ（攻撃のあと）
        public int attackHpChange;       // 攻撃したときの効果で増減した自分の HP（血吸い賽・刻印「吸血」など。演出用）
        public int roundEndHpChange;     // ラウンド終了時の効果で増減した自分の HP（天秤など。演出用）
        public IReadOnlyList<EnemyRoundInfo> enemies;   // 敵ごとの結果
    }

    /// <summary>
    /// 1回の戦闘（敵1〜2体）。ラウンドの流れ：
    /// StartRound（予告）→ Roll（1ラウンドに3個まで）→ Assign → Use（「使う」：そのダイスの効果をすぐ発動。何回でも）
    /// → Resolve（「ターン終了」：まだ使っていないダイスを使う → 敵の毒 → 敵の行動 → ラウンド終了）。
    /// 2026-10-17 開発者の要望（STS2 のように1個ずつ効果を発動。協力で「弱体を与えてから仲間が攻撃」ができるように）で Use を分けた。
    /// 振ってすぐ「ターン終了」すれば、前と同じくまとめて解決する。
    /// 振ったダイスはその場で使用済みになり、使用可能が0個ならリフレッシュする。戦闘が終わっても使用済みのまま。
    /// 攻撃は狙っている敵（はじめは先頭）に当たり、倒しきって余ったダメージは次の敵へ。薙ぎ賽は全員に当たる。防御は敵全員の攻撃の合計に効く。
    /// </summary>
    public class BattleState
    {
        // 開発者の判断：ダイスを多く持てるようにしたので、1ラウンドに振れるのは3個に（仕様書は2個）
        public const int DefaultMaxDicePerRound = 3;
        // 古い賽筒・2体の敵などで増えても、1ラウンドに振れるのはここまで（基本が3個になったので4個まで）
        public const int MaxDiceCap = 4;

        public readonly Combatant player;
        public readonly List<EnemyState> enemies;
        // 敵の予告を出し、行動の順番を進めるのはこの戦闘か（協力では先頭の人の戦闘だけ。ひとりなら常に true）
        readonly bool ownsEnemies = true;
        /// <summary>協力の戦闘での自分の席（PartyBattle が入れる）。1人の戦闘では −1。</summary>
        internal int partySeat = -1;
        public readonly DicePouch pouch;
        readonly Random rng;
        readonly EffectBus effects;
        readonly Run.RunState run;

        readonly List<RolledDie> rolled = new List<RolledDie>();
        readonly List<RoundResult> history = new List<RoundResult>();
        EnemyState target;

        public int Round { get; private set; }

        /// <summary>
        /// 1ラウンドに振れる数。敵が2体以上いる間は +1（開発者の判断）。最大4個。縛りのラウンドは1個。
        /// 代入すると基本の数が変わる（古い賽筒など）。
        /// </summary>
        public int MaxDicePerRound
        {
            get
            {
                if (player.bind > 0) return Math.Min(1, BaseDicePerRound);
                int extra = AliveEnemies.Count() >= 2 ? 1 : 0;
                return Math.Min(MaxDiceCap, BaseDicePerRound + extra);
            }
            set => BaseDicePerRound = value;
        }
        public int BaseDicePerRound { get; set; } = DefaultMaxDicePerRound;

        // 前のラウンドにプレイヤーが出した攻撃値（写し鏡が返す）
        int lastPlayerAttack;
        internal int LastAttack => lastPlayerAttack;

        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
        public IReadOnlyList<RolledDie> Rolled => rolled;
        public IReadOnlyList<RoundResult> History => history;
        public IEnumerable<EnemyState> AliveEnemies => enemies.Where(e => !e.IsDead);

        /// <summary>いま狙っている敵（倒れていない敵のうち、選んだもの。選んでいなければ先頭）。敵が1体ならその敵。</summary>
        public EnemyState Target
        {
            get
            {
                if (target != null && !target.IsDead) return target;
                return AliveEnemies.FirstOrDefault() ?? enemies[enemies.Count - 1];
            }
        }

        /// <summary>今までの「敵」。敵が複数いるときは、いま狙っている敵。</summary>
        public EnemyState enemy => Target;
        public Intent EnemyIntent => Target.CurrentIntent;

        public bool CanRollMore => Outcome == BattleOutcome.Ongoing && rolled.Count < MaxDicePerRound && pouch.AvailableCount > 0;

        /// <summary>振ったが、まだ「使う」で効果を発動していないダイス（振った順）。</summary>
        public IEnumerable<RolledDie> Pending => rolled.Where(r => !r.used);
        public bool HasPending => rolled.Any(r => !r.used);

        // このラウンドの途中の値（「使う」たびに足していく。ラウンドの始めに作り直す）
        RoundWork work;
        // このラウンドの筋力はもう攻撃に乗せた（筋力は1ラウンドの攻撃に1回だけ。連撃賽は別）
        bool strengthUsed;
        int RoundStrength => strengthUsed ? 0 : player.strength;

        RoundWork Work => work ?? (work = new RoundWork
        {
            targetIntent = EnemyIntent,
            infos = enemies.Select((e, i) => new EnemyRoundInfo { enemy = e, index = i, hpBefore = e.hp, intent = e.CurrentIntent }).ToList(),
            aliveBefore = enemies.Select(e => !e.IsDead).ToArray(),
        });

        /// <summary>協力：このラウンドの途中の値（PartyBattle が勝ち負けを決めるときに使う）。</summary>
        internal RoundWork CurrentWork => Work;

        /// <param name="effects">レリックなどが登録された EffectBus。省略するとダイスそのものの特徴だけが効く。</param>
        /// <param name="run">ゴールドを得る効果（刻印「小判」など）のためのラン。省略可。</param>
        /// <param name="enemyHpPercent">敵の HP の倍率（%）。前の層の敵が出たときなど。</param>
        public BattleState(Combatant player, EnemyData enemyData, DicePouch pouch, Random rng, EffectBus effects = null, Run.RunState run = null, int enemyHpPercent = 100)
            : this(player, Enumerable.Repeat(enemyData, Math.Max(1, enemyData.count)).ToList(), pouch, rng, effects, run, enemyHpPercent)
        {
        }

        /// <summary>敵を並べて戦う（先頭から順に、左から並ぶ）。</summary>
        public BattleState(Combatant player, IReadOnlyList<EnemyData> enemyData, DicePouch pouch, Random rng, EffectBus effects = null, Run.RunState run = null, int enemyHpPercent = 100)
        {
            if (enemyData == null || enemyData.Count == 0) throw new ArgumentException("敵がいません。", nameof(enemyData));
            enemies = new List<EnemyState>();
            this.player = player;
            this.pouch = pouch;
            this.rng = rng;
            this.effects = effects ?? new EffectBus();
            this.run = run;
            foreach (var data in enemyData)
            {
                // 敵の種類ごとの倍率（バランス調整のつまみ。GameConfig.enemyBalance）
                var balance = run?.config?.enemyBalance;
                int layer = run != null ? run.LayerIndex : 0;
                int hpPercent = balance != null ? enemyHpPercent * balance.HpPercent(data.kind, layer) / 100 : enemyHpPercent;
                // 段位1：強敵の HP +20%・段位4：ボスの HP +15%
                int ascension = run != null ? run.Ascension : 0;
                hpPercent = hpPercent * Run.AscensionRules.HpPercent(data.kind, ascension) / 100;
                var e = new EnemyState(data, hpPercent);
                if (balance != null) e.attackPercent = balance.AttackPercent(data.kind, layer);
                e.attackBonus = Run.AscensionRules.AttackBonus(data.kind, ascension); // 段位7：通常の敵の攻撃 +1
                enemies.Add(e);
            }

            player.ClearBattleStatuses();
            if (run != null) run.CurrentBattle = this;
            // 戦闘開始時の効果（木の盾の防御・古い賽筒など）。防御は1ラウンド目の終わりまで残る
            this.effects.Fire(new EffectContext(Trigger.OnBattleStart) { player = player, enemy = Target, battle = this, run = run });
            TravelerBattleStart();
            StartRound();
        }

        /// <summary>
        /// 協力：ほかの人と同じ敵（sharedEnemies）と戦う。敵の予告を出すのは ownsEnemies の戦闘（先頭の人）だけ。
        /// 敵を作り直さないので、HP の倍率などは PartyBattle が決める。
        /// </summary>
        internal BattleState(Combatant player, List<EnemyState> sharedEnemies, DicePouch pouch, Random rng, EffectBus effects, Run.RunState run, bool ownsEnemies)
        {
            this.player = player;
            this.pouch = pouch;
            this.rng = rng;
            this.effects = effects ?? new EffectBus();
            this.run = run;
            this.ownsEnemies = ownsEnemies;
            enemies = sharedEnemies;
            player.ClearBattleStatuses();
            if (run != null) run.CurrentBattle = this;
            this.effects.Fire(new EffectContext(Trigger.OnBattleStart) { player = player, enemy = Target, battle = this, run = run });
            TravelerBattleStart();
            StartRound();
        }

        /// <summary>旅人の戦闘開始時の能力（いまはなし。山伏の防御4 は強すぎたのでやめた）。</summary>
        void TravelerBattleStart()
        {
        }

        /// <summary>狙う敵を変える（倒れている敵は狙えない）。</summary>
        public void SetTarget(EnemyState e)
        {
            if (e != null && enemies.Contains(e) && !e.IsDead) target = e;
        }

        void StartRound()
        {
            Round++;
            rolled.Clear();
            work = null;
            strengthUsed = false;
            if (ownsEnemies)
            {
                foreach (var e in AliveEnemies)
                {
                    e.PrepareIntent(Round, rng, lastPlayerAttack);
                }
            }
            // ラウンド開始時の効果（時の砂など）
            effects.Fire(new EffectContext(Trigger.OnRoundStart) { player = player, enemy = Target, battle = this, run = run });
        }

        // 戦闘のあいだだけ加えたダイス（戦闘が終わったら消す）
        readonly List<DiceInstance> temporaryDice = new List<DiceInstance>();
        public IReadOnlyList<DiceInstance> TemporaryDice => temporaryDice;

        /// <summary>戦闘のあいだだけダイスを加える（呪われた双六盤の欠け賽など）。容量は気にしない。</summary>
        public DiceInstance AddTemporaryDie(DiceData data)
        {
            var die = new DiceInstance(data);
            pouch.ForceAdd(die);
            temporaryDice.Add(die);
            return die;
        }

        /// <summary>ダイスを1個振って使用済みにする。最後の1個ならここでリフレッシュが起き、同じラウンドでまた選べる。</summary>
        public RolledDie Roll(DiceInstance die)
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");
            if (rolled.Count >= MaxDicePerRound) throw new InvalidOperationException($"1ラウンドに振れるのは{MaxDicePerRound}個までです。");

            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            // 鏡賽・爆賽などの特別なルールを含めて振る
            int rolledValue = DiceRoller.Roll(die, rng, LastRolledValue, out int faceIndex);
            // ダイスそのものの特徴と、出た面の刻印が効く（錆び賽の自傷・小石もここ）
            var ctx = effects.Fire(NewContext(Trigger.OnRoll, die, faceIndex, rolledValue, Assignment.None), die, die.faces[faceIndex].engraving);
            // 使用済みにする（ピンゾロ賽・小石なら使用可能のまま）。最後の1個ならここでリフレッシュ（鈴が効く）
            pouch.Use(die, ctx.keepAvailable);
            var r = new RolledDie { dice = die, faceIndex = faceIndex, value = Flip(Math.Max(0, ctx.value)), rolledValue = Math.Max(0, ctx.value), assignment = Assignment.Attack, canReroll = ctx.canReroll, bothSides = ctx.bothSides, inverted = Inverted };
            rolled.Add(r);
            ApplyPairRule();
            LastRolledValue = r.value;

            if (player.IsDead)
            {
                Outcome = BattleOutcome.Defeat;
                EndBattle();
            }
            return r;
        }

        /// <summary>ゾロ目の守り：同じラウンドに振った出目が同じダイスは、攻撃と防御の両方に効く。</summary>
        void ApplyPairRule()
        {
            if (!effects.Has<PairBothSidesEffect>()) return;
            foreach (var r in rolled)
            {
                if (rolled.Any(o => o != r && o.value == r.value)) r.bothSides = true;
            }
        }

        bool fateUsed;

        /// <summary>裏返し：このラウンドに「裏返し」を予告している敵がいれば、振った出目は 7−出目（最低0）になる。</summary>
        public bool Inverted => AliveEnemies.Any(e => e.CurrentIntent.type == IntentType.Invert);

        int Flip(int value) => Inverted ? Math.Max(0, 7 - value) : value;

        /// <summary>運命の糸：この戦闘でまだ使っていなければ、出目1つを好きな値にできる。</summary>
        public bool CanUseFate => Outcome == BattleOutcome.Ongoing && !fateUsed && effects.Has<FateThreadEffect>();
        public int FateMaxValue => effects.All<FateThreadEffect>().Select(e => e.maxValue).DefaultIfEmpty(6).Max();

        /// <summary>運命の糸：振った出目 r を value にする（1戦闘に1回）。</summary>
        public void UseFate(RolledDie r, int value)
        {
            if (!rolled.Contains(r)) throw new ArgumentException("このラウンドに振ったダイスではありません。", nameof(r));
            if (r.used) throw new InvalidOperationException("もう使ったダイスです。");
            if (!CanUseFate) throw new InvalidOperationException("運命の糸は使えません。");
            if (value < 1 || value > FateMaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            r.value = value;
            r.rolledValue = value;
            r.inverted = false; // 運命の糸で決めた値は裏返さない
            fateUsed = true;
            LastRolledValue = value;
            ApplyPairRule();
        }

        /// <summary>
        /// 振り直す（刻印「再転」。この面が出たときだけ、1回）。ダイスはもう使用済みなので、もう一度使用済みにはしない。
        /// 振ったときの効果（小判など）はもう一度働く。
        /// </summary>
        public void Reroll(RolledDie r, bool byCharm = false)
        {
            if (!rolled.Contains(r)) throw new ArgumentException("このラウンドに振ったダイスではありません。", nameof(r));
            if (r.used) throw new InvalidOperationException("もう使ったダイスは振り直せません。");
            if (!byCharm && (!r.canReroll || r.rerolled)) throw new InvalidOperationException("振り直せません。");
            int value = DiceRoller.Roll(r.dice, rng, LastRolledValue, out int faceIndex);
            var ctx = effects.Fire(NewContext(Trigger.OnRoll, r.dice, faceIndex, value, Assignment.None), r.dice, r.dice.faces[faceIndex].engraving);
            r.faceIndex = faceIndex;
            r.value = Flip(Math.Max(0, ctx.value));
            r.rolledValue = Math.Max(0, ctx.value);
            r.inverted = Inverted;
            r.bothSides = ctx.bothSides;
            // 振り直し御札で振り直したときは、刻印「再転」の1回はそのまま残す
            if (!byCharm)
            {
                r.rerolled = true;
                r.canReroll = false;
            }
            ApplyPairRule();
            LastRolledValue = r.value;
            if (player.IsDead)
            {
                Outcome = BattleOutcome.Defeat;
                EndBattle();
            }
        }

        /// <summary>煙玉で逃げられる戦闘か（通常戦だけ。戦闘を始める側が決める）。</summary>
        public bool canFleeBattle;
        public bool CanFlee => canFleeBattle && Outcome == BattleOutcome.Ongoing;

        /// <summary>煙玉：戦闘から逃げる（報酬なし）。振ったダイスは使用済みのまま。</summary>
        public void Flee()
        {
            if (!CanFlee) throw new InvalidOperationException("この戦闘からは逃げられません。");
            Outcome = BattleOutcome.Fled;
            EndBattle();
        }

        /// <summary>直前に振ったダイスの出目（鏡賽が写す。表示用）。</summary>
        public int LastRolled => LastRolledValue;

        int ownLastRolled = -1;

        /// <summary>直前に振ったダイスの出目（鏡賽が写す）。ランがあればランをまたいで覚えている。</summary>
        int LastRolledValue
        {
            get => run != null ? run.LastRolledValue : ownLastRolled;
            set
            {
                if (run != null) run.LastRolledValue = value;
                else ownLastRolled = value;
            }
        }

        public void Assign(RolledDie die, Assignment assignment)
        {
            if (!rolled.Contains(die)) throw new ArgumentException("このラウンドに振ったダイスではありません。", nameof(die));
            if (die.used) return; // もう使ったダイスの割り振りは変わらない
            die.assignment = assignment;
        }

        /// <summary>
        /// 出目を assignment に置いたときの値。ダイスの特徴（盾賽の防御+2 など）→ 刻印 → レリック → 状態異常 の順に効く。0 未満にはならない。
        /// </summary>
        public int EffectiveValue(RolledDie die, Assignment assignment)
        {
            var trigger = assignment == Assignment.Block ? Trigger.OnAssignDefense : Trigger.OnAssignAttack;
            var ctx = effects.Fire(NewContext(trigger, die.dice, die.faceIndex, die.value, assignment), die.dice, die.dice.faces[die.faceIndex].engraving);
            int v = Math.Max(0, ctx.value);
            // 旅人の能力（2026-10-17 役割分担）：博徒は大きい目の攻撃+2、山伏は防御+1
            if (assignment == Assignment.Attack && run != null && run.HasPerk(Run.TravelerKind.Bakuto) && die.value >= Run.Travelers.BakutoBigValue) v += Run.Travelers.BakutoBigBonus;
            if (assignment == Assignment.Block && run != null && run.HasPerk(Run.TravelerKind.Yamabushi) && die.value <= Run.Travelers.YamabushiSmallValue) v += Run.Travelers.YamabushiBlockBonus;
            return v;
        }

        /// <summary>協力の戦闘（PartyBattle が入れる）。賽振りの回復を仲間に分けるのに使う。</summary>
        internal PartyBattle party;

        /// <summary>賽振り：リフレッシュの回復を、協力の戦闘の仲間全員にも（ひとりの戦闘では何もしない）。</summary>
        public void ShareHeal(int amount) => party?.HealOthers(partySeat, amount);

        /// <summary>この戦闘のラン（協力では、その人のラン）。</summary>
        internal Run.RunState OwnerRun => run;

        EffectContext NewContext(Trigger trigger, DiceInstance die, int faceIndex, int value, Assignment assignment)
        {
            return new EffectContext(trigger)
            {
                player = player,
                enemy = Target,
                battle = this,
                run = run,
                dice = die,
                faceIndex = faceIndex,
                value = value,
                assignment = assignment,
            };
        }

        // ---- 攻撃値と防御値 ----

        // 攻撃・防御の値は、まだ使っていないダイス（Pending）で数える（使ったダイスはもう効果を発動した）
        IEnumerable<RolledDie> AttackDice(bool hitsAll) => Pending.Where(r => (r.assignment == Assignment.Attack || r.bothSides) && r.HitsAll == hitsAll);

        /// <summary>連撃賽：出目（置いたときの値）の数だけ 1 ダメージ。1発ごとに筋力が乗る（2026-10-17 開発者の要望）。</summary>
        int MultiHitValue(RolledDie r) => EffectiveValue(r, Assignment.Attack) * Math.Max(0, 1 + player.strength);

        /// <summary>狙った敵に当たる攻撃値（薙ぎ賽以外。筋力込み）。</summary>
        int MainAttack()
        {
            var normal = AttackDice(false).Where(r => !r.MultiHit).Select(r => EffectiveValue(r, Assignment.Attack)).ToList();
            var multi = AttackDice(false).Where(r => r.MultiHit).Select(MultiHitValue);
            return BattleResolver.PlayerAttack(normal.Concat(multi), normal.Count > 0 ? RoundStrength : 0, player.weak);
        }

        /// <summary>全部の敵に当たる攻撃値（薙ぎ賽）。ほかに攻撃のダイスがなければ筋力もここに足す（筋力は合計に1回だけ）。</summary>
        int SweepAttack()
        {
            var dice = AttackDice(true).ToList();
            int strength = AttackDice(false).Any(r => !r.MultiHit) ? 0 : RoundStrength;
            return BattleResolver.PlayerAttack(dice.Select(r => EffectiveValue(r, Assignment.Attack)), strength, player.weak);
        }

        int CurrentAttack() => MainAttack() + SweepAttack();

        int CurrentBlock() => BattleResolver.ApplyFrail(BattleResolver.PlayerBlock(
            Pending.Where(r => r.assignment == Assignment.Block || r.bothSides).Select(r => EffectiveValue(r, Assignment.Block))), player.frail);

        /// <summary>今の割り振りでの攻撃値（筋力込み。薙ぎ賽の分も足した合計）と防御値。演出や表示に使う。</summary>
        public int AttackValue => CurrentAttack();

        /// <summary>
        /// 表示用：ダイス1個を攻撃・防御に置いたときの値。ダイス・刻印・レリックの効果に、脱力・狙っている敵の弱体（攻撃）や
        /// 脆弱（防御）も1個ずつかけたもの。筋力は合計に1回だけ足すので入れない。
        /// </summary>
        public int ShownValue(RolledDie die, Assignment assignment)
        {
            int v = EffectiveValue(die, assignment);
            if (assignment == Assignment.Block) return BattleResolver.ApplyFrail(v, player.frail);
            if (die.MultiHit) v = MultiHitValue(die); // 連撃賽は1発ごとに筋力が乗るので、ここで足す
            v = BattleResolver.ApplyWeak(v, player.weak);
            if (!die.HitsAll && Target != null) v = BattleResolver.ApplyVulnerable(v, Target.vulnerable);
            return v;
        }

        /// <summary>表示の色分け用：効果を受けた値が元の値より大きければ +1、小さければ −1、同じなら 0。</summary>
        public static int Trend(int shown, int original) => Math.Sign(shown - original);
        public int BlockValue => CurrentBlock();

        /// <summary>
        /// 攻撃を当てたときに、敵ごとに HP がいくつ減るか（実際には減らさない）。
        /// 薙ぎ賽の分を全員に当ててから、残りを狙った敵 → 次の敵…の順に、倒しきって余った分を回す。
        /// 脆弱・防御・1ラウンドのダメージ上限も考える。
        /// </summary>
        /// <summary>
        /// 攻撃を振った順に解決したときの、敵ごとのダメージ（開発者の判断：ダイスは振った順＝左から効果が発動する）。
        /// 弱体を与えるダイス（砕き賽・刻印「崩し」など）があると、そこで区切り、それより後に振ったダイスの攻撃に弱体が乗る。
        /// 筋力は最初に攻撃するまとまりに1回だけ、脱力はまとまりごとにかかる。区切りがなければ今までと同じ計算。
        /// </summary>
        int[] PlanAttackInOrder(out int[] blockAfter)
        {
            int n = enemies.Count;
            var hp = enemies.Select(e => e.hp).ToArray();
            var block = enemies.Select(e => e.block).ToArray();
            var extraVulnerable = new int[n];
            var dealt = new int[n];
            lastVulnerableHit = new bool[n];
            // 1ラウンドのダメージ上限（鉄壁）は、このラウンドにもう与えた分も数える
            var already = work != null ? work.infos.Select(x => x.dealt).ToArray() : new int[n];

            // 振った順に、弱体を与えるダイスのところで区切る
            var segments = new List<List<RolledDie>>();
            var current = new List<RolledDie>();
            foreach (var r in Pending.Where(x => x.assignment == Assignment.Attack || x.bothSides))
            {
                current.Add(r);
                if (VulnerableGiven(r) > 0)
                {
                    segments.Add(current);
                    current = new List<RolledDie>();
                }
            }
            if (current.Count > 0) segments.Add(current);

            bool used = strengthUsed;
            int targetIndex = enemies.IndexOf(Target);
            foreach (var seg in segments)
            {
                var mainValues = seg.Where(r => !r.HitsAll && !r.MultiHit).Select(r => EffectiveValue(r, Assignment.Attack)).ToList();
                var multiValues = seg.Where(r => !r.HitsAll && r.MultiHit).Select(MultiHitValue).ToList();
                var sweepValues = seg.Where(r => r.HitsAll).Select(r => EffectiveValue(r, Assignment.Attack)).ToList();
                // 筋力は合計に1回だけ（攻撃のダイスがあればそちらに、なければ薙ぎ賽に）。連撃賽は1発ごとに乗せてあるので数えない
                int mainStrength = !used && mainValues.Count > 0 ? player.strength : 0;
                int sweepStrength = !used && mainValues.Count == 0 && sweepValues.Count > 0 ? player.strength : 0;
                if (mainValues.Count > 0 || sweepValues.Count > 0) used = true;
                int main = BattleResolver.PlayerAttack(mainValues.Concat(multiValues), mainStrength, player.weak);
                int sweep = BattleResolver.PlayerAttack(sweepValues, sweepStrength, player.weak);
                HitEnemies(main, sweep, hp, block, dealt, extraVulnerable, already);
                // このまとまりの最後のダイスが与える弱体は、次のまとまりから効く（狙っている敵に）
                foreach (var r in seg) extraVulnerable[targetIndex] += VulnerableGiven(r);
            }
            blockAfter = block;
            return dealt;
        }

        /// <summary>
        /// 棘（山颪など）：攻撃に置いたダイスのうち、出目（置いたときの値）が敵の thornsMinValue 以上のもの1個ごとに thorns ダメージ。
        /// 狙った敵（薙ぎ賽は全部の敵）に棘があれば刺さる。大きい出目を出しすぎると裏目に出る。
        /// </summary>
        public int ThornsDamage()
        {
            int total = 0;
            var target = Target;
            foreach (var e in enemies)
            {
                if (e.IsDead || e.data.thorns <= 0) continue;
                foreach (var r in Pending.Where(x => x.assignment == Assignment.Attack || x.bothSides))
                {
                    if ((r.HitsAll || e == target) && EffectiveValue(r, Assignment.Attack) >= e.data.thornsMinValue) total += e.data.thorns;
                }
            }
            return total;
        }

        /// <summary>このダイスが攻撃したときに、狙った敵に与える弱体の量（萎え賽・砕き賽・刻印「崩し」・レリック）。</summary>
        int VulnerableGiven(RolledDie r)
        {
            var sources = new List<EffectSO>();
            if (r.dice.Effects != null) sources.AddRange(r.dice.Effects);
            var engraving = r.dice.faces[r.faceIndex].engraving;
            if (engraving != null && engraving.Effects != null) sources.AddRange(engraving.Effects);
            sources.AddRange(effects.All<AttackRiderEffect>());
            int bonus = run != null ? run.DebuffBonus : 0; // 細工師：与える弱体 +1
            return sources.OfType<AttackRiderEffect>()
                .Where(e => e.trigger == Trigger.OnAttackResolve && e.rider == AttackRider.Vulnerable)
                .Sum(e => { int n = e.amount + r.value * e.perPip; return n > 0 ? n + bonus : 0; });
        }

        /// <summary>薙ぎ賽は全員、残りは狙った敵から順に当てる（hp・block・dealt を書き換える）。</summary>
        void HitEnemies(int mainAttack, int sweepAttack, int[] hp, int[] block, int[] dealt, int[] extraVulnerable, int[] already)
        {
            int n = enemies.Count;
            int Hit(int i, int attack)
            {
                var e = enemies[i];
                if (hp[i] <= 0 || attack <= 0) return 0;
                int incoming = BattleResolver.ApplyVulnerable(attack, e.vulnerable + extraVulnerable[i]);
                if (e.vulnerable + extraVulnerable[i] > 0 && lastVulnerableHit != null) lastVulnerableHit[i] = true;
                int absorbed = Math.Min(block[i], incoming);
                block[i] -= absorbed;
                int damage = incoming - absorbed;
                if (e.data.damageCapPerRound > 0) damage = Math.Min(damage, Math.Max(0, e.data.damageCapPerRound - already[i] - dealt[i]));
                int applied = Math.Min(damage, hp[i]);
                hp[i] -= applied;
                dealt[i] += applied;
                return hp[i] <= 0 ? damage - applied : 0; // 倒しきって余った分
            }

            for (int i = 0; i < n; i++) Hit(i, sweepAttack);

            int start = enemies.IndexOf(Target);
            int remaining = mainAttack;
            for (int k = 0; k < n && remaining > 0; k++)
            {
                int i = (start + k) % n;
                if (hp[i] <= 0) continue;
                remaining = Hit(i, remaining);
            }
        }

        bool[] lastVulnerableHit; // PlanAttackInOrder で、弱体が乗った攻撃が当たった敵（演出用）

        /// <summary>今の割り振りで「与えるダメージ／受けるダメージ」がいくつになるか。賽振りは値が隠れているので範囲で返す。</summary>
        public DamagePreview Preview()
        {
            var dealtPer = PlanAttackInOrder(out _); // 振った順に（弱体を与えるダイスより後のダイスには弱体が乗る）
            int dealt = dealtPer.Sum();

            // 倒しきれない敵の攻撃を合計して、防御を引く
            int block = player.block + CurrentBlock();
            int minTotal = 0, maxTotal = 0, baseTotal = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                // 攻撃か、そのあとの毒で倒れる敵は行動しない
                if (e.IsDead || e.hp - dealtPer[i] - e.poison <= 0) continue;
                var intent = e.CurrentIntent;
                int Attack(int value)
                {
                    var shown = intent;
                    shown.value = value;
                    return BattleResolver.ApplyVulnerable(BattleResolver.EnemyAttack(shown, e.strength, e.weak), player.vulnerable);
                }
                if (intent.IsAttack)
                {
                    int baseValue = intent.type == IntentType.DiceRoll && intent.minValue < intent.maxValue ? intent.maxValue : intent.value;
                    baseTotal += Math.Max(0, baseValue) * intent.Hits;
                }
                if (intent.type == IntentType.DiceRoll && intent.minValue < intent.maxValue)
                {
                    minTotal += Attack(intent.minValue);
                    maxTotal += Attack(intent.maxValue);
                }
                else
                {
                    int a = Attack(intent.value);
                    minTotal += a;
                    maxTotal += a;
                }
            }
            // 棘のダメージも受ける（防御では防げない）
            int thornsPreview = ThornsDamage();
            int taken = Math.Min(player.hp, BattleResolver.DamageAfterBlock(maxTotal, block) + thornsPreview);
            int takenMin = Math.Min(player.hp, BattleResolver.DamageAfterBlock(minTotal, block) + thornsPreview);

            // 色分け：攻撃は「出目の合計」と「効果のあとの攻撃値（狙っている敵の弱体込み。敵の防御の前）」をくらべる
            var attackDice = Pending.Where(r => r.assignment == Assignment.Attack || r.bothSides).ToList();
            int rawAttack = attackDice.Sum(r => r.value);
            int targetVulnerable = Target != null ? Target.vulnerable : 0;
            int shownAttack = BattleResolver.ApplyVulnerable(MainAttack(), targetVulnerable) + SweepAttack();

            return new DamagePreview
            {
                dealt = dealt, taken = taken, takenMin = takenMin,
                dealtPerEnemy = dealtPer,
                dealtTrend = attackDice.Count > 0 ? Trend(shownAttack, rawAttack) : 0,
                takenTrend = Trend(maxTotal, baseTotal),
                killsAll = dealt > 0 && enemies.Select((e, i) => e.IsDead || e.hp - dealtPer[i] - e.poison <= 0).All(x => x),
            };
        }

        // ---- ラウンドの解決 ----

        /// <summary>1ラウンドの解決の途中の値（攻撃 → 敵の毒 → 敵の行動 → ラウンド終了 と、段階に分けて渡す）。</summary>
        internal class RoundWork
        {
            public Intent targetIntent;
            public List<EnemyRoundInfo> infos;
            public bool[] aliveBefore;
            public int diceBlock;
            public int main, sweep;
            public int attackHpChange;
            public int thornsTaken;
        }

        /// <summary>
        /// 「ターン終了」：まだ使っていないダイスを使ってから、敵の毒 → 敵の行動 → ラウンド終了 と進める。
        /// ダイスを1個も振っていなければパス。
        /// </summary>
        public RoundResult Resolve()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");
            var w = AttackPhase();
            EnemyPoisonPhase(w.infos);

            // 敵の行動（倒れていない敵が、並び順に）
            if (player.IsDead)
            {
                Outcome = BattleOutcome.Defeat; // 棘で倒れた
            }
            else if (!AliveEnemies.Any())
            {
                Outcome = BattleOutcome.Victory;
            }
            else
            {
                foreach (var info in w.infos)
                {
                    if (info.enemy.IsDead || !w.aliveBefore[info.index]) continue;
                    info.acted = true;
                    Act(info);
                    if (player.IsDead)
                    {
                        Outcome = BattleOutcome.Defeat;
                        break;
                    }
                }
            }
            var result = FinishPlayerRound(w);
            EndEnemyRound();
            if (Outcome == BattleOutcome.Ongoing) NextRound();
            return result;
        }

        /// <summary>
        /// 「使う」：振って割り振ったダイスのうち、まだ使っていないものの効果を、いま発動する（振った順に）。
        /// 防御はすぐ付き、攻撃はすぐ当たり、弱体などもすぐ入る（このあとに使うダイス・仲間の攻撃に乗る）。
        /// 敵を全員倒したら勝ち（ひとりの戦闘。協力では PartyBattle が決める）。
        /// </summary>
        public UseResult Use()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");
            var u = UsePending();
            if (partySeat >= 0) return u; // 協力：勝ち負けは PartyBattle が決める
            if (player.IsDead) Outcome = BattleOutcome.Defeat; // 棘で倒れた
            else if (!AliveEnemies.Any()) Outcome = BattleOutcome.Victory;
            if (Outcome != BattleOutcome.Ongoing)
            {
                FinishPlayerRound(Work);
                EndEnemyRound();
            }
            return u;
        }

        /// <summary>攻撃の解決（ターン終了のとき）：まだ使っていないダイスを使い、このラウンドの途中の値を返す。</summary>
        internal RoundWork AttackPhase()
        {
            UsePending();
            return Work;
        }

        /// <summary>まだ使っていないダイスの効果を発動する：防御を得て、攻撃を当て、攻撃したときの効果・棘・怯みまで。</summary>
        UseResult UsePending()
        {
            var w = Work;
            var dice = Pending.ToList();
            var infos = enemies.Select((e, i) => new EnemyRoundInfo { enemy = e, index = i, hpBefore = e.hp, intent = e.CurrentIntent }).ToList();
            var u = new UseResult { dice = dice, hpBefore = player.hp, enemies = infos };
            if (dice.Count == 0) return u;

            int diceBlock = CurrentBlock();
            w.diceBlock += diceBlock;
            player.block += diceBlock;
            u.blockGained = diceBlock;

            // 攻撃の解決（薙ぎ賽は全員、残りは狙った敵から順に）
            int main = MainAttack();
            int sweep = SweepAttack();
            u.attack = main + sweep;
            w.main += main;
            w.sweep += sweep;
            lastPlayerAttack = w.main + w.sweep;
            var aliveBefore = enemies.Select(e => !e.IsDead).ToArray();
            int thorns = ThornsDamage(); // 攻撃で倒しても棘は刺さる
            u.weakened = player.weak > 0 && u.attack > 0;
            var plan = PlanAttackInOrder(out var blockAfter);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                e.block = blockAfter[i];
                e.hp -= plan[i];
                infos[i].dealt = plan[i];
                infos[i].vulnerableHit = plan[i] > 0 && lastVulnerableHit != null && lastVulnerableHit[i];
                infos[i].killedByAttack = aliveBefore[i] && e.IsDead;
                w.infos[i].dealt += plan[i];
                if (infos[i].killedByAttack) w.infos[i].killedByAttack = true;
            }
            // 筋力は1ラウンドに1回だけ（連撃賽のほかに攻撃のダイスがあれば、ここで使い切る）
            if (dice.Any(r => (r.assignment == Assignment.Attack || r.bothSides) && !r.MultiHit)) strengthUsed = true;
            OnEnemiesDefeated(infos, infos.Where(x => x.killedByAttack).ToList());
            for (int i = 0; i < enemies.Count; i++) w.infos[i].strengthGained += infos[i].strengthGained;

            // 攻撃に置いたダイスごとの「攻撃したとき」の効果（毒賽の毒・気合賽の筋力など）。狙っていた敵に効く
            int totalDealt = infos.Sum(x => x.dealt);
            var statusBefore = infos.Select(x => (x.enemy.weak, x.enemy.vulnerable, x.enemy.frail)).ToList();
            int hpBeforeRiders = player.hp;
            int strengthBefore = player.strength, fortifyBefore = player.fortify;
            foreach (var r in dice.Where(x => x.assignment == Assignment.Attack || x.bothSides))
            {
                var ctx = NewContext(Trigger.OnAttackResolve, r.dice, r.faceIndex, r.value, Assignment.Attack);
                ctx.amount = totalDealt; // 吸血はこの「使う」で与えたダメージから
                effects.Fire(ctx, r.dice, r.dice.faces[r.faceIndex].engraving);
            }
            // 防御に置いたダイスごとの「防御したとき」の効果（構え賽の堅守など）
            foreach (var r in dice.Where(x => x.assignment == Assignment.Block || x.bothSides))
            {
                effects.Fire(NewContext(Trigger.OnBlockResolve, r.dice, r.faceIndex, r.value, Assignment.Block), r.dice, r.dice.faces[r.faceIndex].engraving);
            }
            u.attackHpChange = player.hp - hpBeforeRiders;
            w.attackHpChange += u.attackHpChange;
            u.strengthGained = player.strength - strengthBefore;
            u.fortifyGained = player.fortify - fortifyBefore;
            // 萎え賽・砕き賽・刻印「崩し」などで、敵に与えた脱力・弱体・脆弱（演出用）
            for (int i = 0; i < infos.Count; i++)
            {
                var e = infos[i].enemy;
                infos[i].weakGiven = e.weak - statusBefore[i].weak;
                infos[i].vulnerableGiven = e.vulnerable - statusBefore[i].vulnerable;
                infos[i].frailGiven = e.frail - statusBefore[i].frail;
                w.infos[i].weakGiven += infos[i].weakGiven;
                w.infos[i].vulnerableGiven += infos[i].vulnerableGiven;
                w.infos[i].frailGiven += infos[i].frailGiven;
            }

            // 棘（山颪など）：大きい出目で攻撃したダイス1個ごとにダメージ（防御無視）
            int thornsTaken = thorns > 0 ? player.LoseHp(thorns) : 0;
            u.thornsTaken = thornsTaken;
            w.thornsTaken += thornsTaken;

            // 溜めのラウンドに十分なダメージを与えたら怯む（次の大攻撃が止まる。大顎）。このラウンドに与えた合計で数える
            for (int i = 0; i < w.infos.Count; i++)
            {
                var info = w.infos[i];
                var intent = info.intent;
                if (info.staggered || intent.type != IntentType.Charge || intent.value <= 0 || info.dealt < intent.value || info.enemy.IsDead) continue;
                info.staggered = true;
                infos[i].staggered = true;
                info.enemy.Staggered = true;
            }
            foreach (var r in dice) r.used = true;
            return u;
        }

        /// <summary>敵の毒（防御無視）：プレイヤーの攻撃のあと、敵が行動する前（開発者の判断）。毒で倒れた敵は行動しない。</summary>
        internal void EnemyPoisonPhase(List<EnemyRoundInfo> infos)
        {
            foreach (var info in infos)
            {
                if (info.enemy.IsDead) continue;
                info.poisonDamage = info.enemy.TickPoison();
                info.diedOfPoison = info.enemy.IsDead;
            }
            OnEnemiesDefeated(infos, infos.Where(x => x.diedOfPoison).ToList());
        }

        /// <summary>
        /// 協力：この人へ敵1体が行動する（STS2 のように、敵の行動は戦っている全員に）。倒れたら負けにする。
        /// enemySide は、敵自身への効果（前の防御が消える・筋力・防御を得る）をこの人の戦闘で行うか（1体につき1回だけ true）。
        /// </summary>
        internal void ActOn(EnemyRoundInfo info, bool enemySide = true)
        {
            info.acted = true;
            Act(info, enemySide);
            if (player.IsDead) Outcome = BattleOutcome.Defeat;
        }

        /// <summary>協力：勝ち負けを外から決める（全員の攻撃で敵が全滅した・倒れた）。</summary>
        internal void SetOutcome(BattleOutcome outcome) => Outcome = outcome;

        /// <summary>ラウンドの終わり（自分の側）：ラウンド終了時の効果・毒・結果の記録・状態異常と防御の後片付け。</summary>
        internal RoundResult FinishPlayerRound(RoundWork work)
        {
            var infos = work.infos;
            int main = work.main, sweep = work.sweep, diceBlock = work.diceBlock;
            var targetIntent = work.targetIntent;
            int attackHpChange = work.attackHpChange;
            int thornsTaken = work.thornsTaken;

            int hpBeforeRoundEnd = player.hp;
            // ラウンド終了時の効果（天秤：攻撃と防御が同じ値なら回復）
            if (Outcome != BattleOutcome.Defeat)
            {
                effects.Fire(new EffectContext(Trigger.OnRoundEnd) { player = player, enemy = Target, battle = this, run = run, value = main + sweep, amount = diceBlock });
            }
            int roundEndHpChange = player.hp - hpBeforeRoundEnd;

            // プレイヤーの毒はラウンド終了（防御無視）
            int playerPoison = 0;
            if (Outcome == BattleOutcome.Ongoing)
            {
                playerPoison = player.TickPoison();
                if (player.IsDead) Outcome = BattleOutcome.Defeat;
            }

            var result = new RoundResult
            {
                round = Round,
                dealt = infos.Sum(x => x.dealt),
                taken = infos.Sum(x => x.taken),
                enemyIntent = targetIntent,
                rolled = rolled.ToList(),
                sealedDie = infos.Select(x => x.sealedDie).FirstOrDefault(d => d != null),
                sealedCount = infos.Sum(x => x.sealedCount),
                curseDie = infos.Select(x => x.curseDie).FirstOrDefault(d => d != null),
                staggered = infos.Any(x => x.staggered),
                enemyPoisonDamage = infos.Sum(x => x.poisonDamage),
                playerPoisonDamage = playerPoison,
                thornsDamage = thornsTaken,
                attackHpChange = attackHpChange,
                roundEndHpChange = roundEndHpChange,
                enemies = infos,
            };
            history.Add(result);
            // 結果画面の「与えた・受けたダメージの合計」（毒・棘も含める）
            if (run != null)
            {
                run.stats.damageDealt += result.dealt + result.enemyPoisonDamage;
                run.stats.damageTaken += result.taken + result.playerPoisonDamage + result.thornsDamage;
            }

            // ラウンド終了：状態異常を処理し、プレイヤーの防御値を0に戻す（堅守なら半分残る）。敵の防御は次のラウンドまで残る
            player.TickStatuses();
            player.EndRoundBlock();
            if (Outcome != BattleOutcome.Ongoing) EndBattle();
            return result;
        }

        /// <summary>ラウンドの終わり（敵の側）：状態異常を処理する。敵の防御は、次の行動の前に消す（Act の初め）。協力では1回だけ呼ぶ。</summary>
        internal void EndEnemyRound()
        {
            foreach (var e in enemies) e.TickStatuses();
        }

        /// <summary>次のラウンドへ：敵の行動の順番を進めて、予告を出す。</summary>
        internal void NextRound()
        {
            if (ownsEnemies)
            {
                foreach (var e in AliveEnemies) e.AdvancePattern();
            }
            StartRound();
        }

        /// <summary>倒れた敵がいたら、残った仲間が強くなる（双子鬼「片方を倒すと残りが筋力+3」）。</summary>
        void OnEnemiesDefeated(List<EnemyRoundInfo> infos, List<EnemyRoundInfo> defeated)
        {
            if (defeated.Count == 0) return;
            foreach (var info in infos)
            {
                var e = info.enemy;
                if (e.IsDead || e.data.allyDefeatedStrength <= 0) continue;
                int gain = e.data.allyDefeatedStrength * defeated.Count;
                e.strength += gain;
                info.strengthGained += gain;
            }
        }

        /// <summary>敵1体の行動。enemySide が false なら、プレイヤーへの効果だけ（協力で2人目以降に行動するとき）。</summary>
        void Act(EnemyRoundInfo info, bool enemySide = true)
        {
            var e = info.enemy;
            var intent = e.CurrentIntent;
            info.intent = intent; // 足枷などで攻撃のあとに変わることがある
            // 前のラウンドに得た防御は、自分の行動の前に消える（堅守なら半分残る）
            if (enemySide) e.EndRoundBlock();
            switch (intent.type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll:
                case IntentType.MirrorAttack:
                    info.taken = player.TakeAttack(BattleResolver.EnemyAttack(intent, e.strength, e.weak)); // 脆弱は TakeAttack の中で
                    break;
                case IntentType.Buff:
                    if (enemySide) e.strength += intent.value;
                    break;
                case IntentType.Debuff:
                    player.ApplyWeak(intent.value);
                    break;
                case IntentType.Poison:
                    // 山伏：受ける毒 −1
                    player.ApplyPoison(Math.Max(0, intent.value - (run != null && run.HasPerk(Run.TravelerKind.Yamabushi) ? Run.Travelers.YamabushiPoisonResist : 0)));
                    break;
                case IntentType.Frail:
                    player.ApplyFrail(intent.value);
                    break;
                case IntentType.Vulnerable:
                    player.ApplyVulnerable(intent.value);
                    break;
                case IntentType.Bind:
                    player.ApplyBind();
                    break;
                case IntentType.Curse:
                    // TODO(仕様): ポーチが満杯なら呪いは入らない（罠と同じ扱い）
                    info.curseDie = run?.ForceCurse();
                    break;
                case IntentType.Invert:
                case IntentType.Charge:
                case IntentType.Stunned:
                    break; // 何もしない（溜め・怯み）
                case IntentType.Seal:
                    // ランダムに1個だけ封印し、前に封印していたダイスは解放する（開発者の判断：何個も封印されると辛すぎるため）
                    info.sealedDie = pouch.SealRandom(rng);
                    info.sealedCount = info.sealedDie != null ? 1 : 0;
                    break;
                case IntentType.ResetDice:
                    // 振り出しに戻れ：使用可能なダイスのうち、強い順に半分（切り上げ）を使用済みにする。
                    // いちばん弱いダイスは残すのでリフレッシュは起きない（開発者の判断：全部を使用済みにすると、
                    // すぐリフレッシュで全部戻ってプレイヤーの得になっていた）
                    info.resetDice = ResetTargets();
                    foreach (var d in info.resetDice) d.state = DiceState.Used;
                    break;
                case IntentType.RewriteFate:
                    RewriteFate(info);
                    break;
                case IntentType.Block:
                    break; // 防御は下でまとめて得る
            }

            // 行動のあとに防御を得る（防御の予告・攻撃＋防御など）。次のラウンドのプレイヤーの攻撃を防ぎ、次の行動の前に消える
            // （開発者の判断：STS と同じに。前は予告を出した瞬間に防御が付いていた）。脆弱なら減る
            int gain = intent.BlockGain;
            if (gain > 0 && enemySide)
            {
                info.blockGained = BattleResolver.ApplyFrail(gain, e.frail);
                e.block += info.blockGained;
            }

            // 段位10：賽の神・八面の第3形態（HP40以下で、毎ラウンドダイス1個を封印）
            if (run != null && run.Ascension >= 10 && e.data.id == Run.AscensionRules.HachimenId && !e.IsDead
                && e.hp <= Run.AscensionRules.HachimenPhase3Hp && info.sealedDie == null)
            {
                info.sealedDie = pouch.SealRandom(rng);
                info.sealedCount = info.sealedDie != null ? 1 : 0;
            }
        }

        /// <summary>
        /// 振り出しに戻れで使用済みにされるダイス（予告の表示にも使う）：使用可能なダイスのうち、
        /// 出目の平均が高い順に半分（切り上げ）。使っても使用済みにならないダイス（ピンゾロ賽）以外で一番弱いものは残す。
        /// </summary>
        public List<DiceInstance> ResetTargets()
        {
            var available = pouch.Available.ToList();
            var keep = available.Where(d => d.data == null || !d.data.keepAvailable).OrderBy(d => d.faces.Average(f => f.value)).FirstOrDefault();
            if (keep == null) return new List<DiceInstance>();
            int count = (available.Count + 1) / 2;
            return available.Where(d => d != keep).OrderByDescending(d => d.faces.Average(f => f.value)).Take(count).ToList();
        }

        // 運命の書き換えで変えた面（戦闘が終わったら戻す）
        readonly List<(DiceInstance die, int face, int value)> rewrites = new List<(DiceInstance, int, int)>();

        /// <summary>
        /// 運命の書き換え（八面）：プレイヤーの最も強いダイス（最大の面が一番大きいもの。同じなら平均が高いもの）の、
        /// 最大の面を戦闘中だけ1にする。
        /// </summary>
        void RewriteFate(EnemyRoundInfo info)
        {
            var best = pouch.All
                .Where(d => d.faces.Max(f => f.value) > 1)
                .OrderByDescending(d => d.faces.Max(f => f.value))
                .ThenByDescending(d => d.faces.Average(f => f.value))
                .FirstOrDefault();
            if (best == null) return;
            int max = best.faces.Max(f => f.value);
            int index = Array.FindIndex(best.faces, f => f.value == max);
            var face = best.faces[index];
            rewrites.Add((best, index, face.value));
            info.rewrittenDie = best;
            info.rewrittenFrom = face.value;
            face.value = 1;
            best.faces[index] = face;
        }

        /// <summary>戦闘終了の後片付け。封印は解除するが、使用済みはそのまま残す。運命の書き換えで変えた面は戻す。</summary>
        void EndBattle()
        {
            for (int i = rewrites.Count - 1; i >= 0; i--)
            {
                var (die, index, value) = rewrites[i];
                var face = die.faces[index];
                face.value = value;
                die.faces[index] = face;
            }
            rewrites.Clear();
            foreach (var d in temporaryDice)
            {
                if (pouch.All.Contains(d)) pouch.Remove(d);
            }
            temporaryDice.Clear();
            if (run != null && run.CurrentBattle == this) run.CurrentBattle = null;
            if (run != null && Outcome == BattleOutcome.Victory) run.stats.CountVictory(enemies[0].data.kind);
            // 勝ったときの効果（血の盃など）
            if (Outcome == BattleOutcome.Victory) effects.Fire(new EffectContext(Trigger.OnBattleEnd) { player = player, battle = this, run = run });
            player.ClearBattleStatuses();
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) d.state = DiceState.Available;
            }
        }
    }
}
