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

        /// <summary>全部の敵に当たるダイス（薙ぎ賽）か。</summary>
        public bool HitsAll => dice.data != null && dice.data.hitsAll;
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
    /// StartRound（予告。防御の予告はここで敵の防御値になる）→ Roll を0〜2回（敵が2体なら3回）→ Assign → Resolve（攻撃の解決 → 敵の行動 → ラウンド終了）。
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
        public readonly List<EnemyState> enemies = new List<EnemyState>();
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
                var e = new EnemyState(data, hpPercent);
                if (balance != null) e.attackPercent = balance.AttackPercent(data.kind, layer);
                enemies.Add(e);
            }

            player.ClearBattleStatuses();
            if (run != null) run.CurrentBattle = this;
            // 戦闘開始時の効果（木の盾の防御・古い賽筒など）。防御は1ラウンド目の終わりまで残る
            this.effects.Fire(new EffectContext(Trigger.OnBattleStart) { player = player, enemy = Target, battle = this, run = run });
            StartRound();
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
            foreach (var e in AliveEnemies)
            {
                e.PrepareIntent(Round, rng, lastPlayerAttack);
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
            die.assignment = assignment;
        }

        /// <summary>
        /// 出目を assignment に置いたときの値。ダイスの特徴（盾賽の防御+2 など）→ 刻印 → レリック → 状態異常 の順に効く。0 未満にはならない。
        /// </summary>
        public int EffectiveValue(RolledDie die, Assignment assignment)
        {
            var trigger = assignment == Assignment.Block ? Trigger.OnAssignDefense : Trigger.OnAssignAttack;
            var ctx = effects.Fire(NewContext(trigger, die.dice, die.faceIndex, die.value, assignment), die.dice, die.dice.faces[die.faceIndex].engraving);
            return Math.Max(0, ctx.value);
        }

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

        IEnumerable<RolledDie> AttackDice(bool hitsAll) => rolled.Where(r => (r.assignment == Assignment.Attack || r.bothSides) && r.HitsAll == hitsAll);

        /// <summary>狙った敵に当たる攻撃値（薙ぎ賽以外。筋力込み）。</summary>
        int MainAttack()
        {
            var dice = AttackDice(false).ToList();
            return BattleResolver.PlayerAttack(dice.Select(r => EffectiveValue(r, Assignment.Attack)), player.strength, player.weak);
        }

        /// <summary>全部の敵に当たる攻撃値（薙ぎ賽）。ほかに攻撃のダイスがなければ筋力もここに足す（筋力は合計に1回だけ）。</summary>
        int SweepAttack()
        {
            var dice = AttackDice(true).ToList();
            int strength = AttackDice(false).Any() ? 0 : player.strength;
            return BattleResolver.PlayerAttack(dice.Select(r => EffectiveValue(r, Assignment.Attack)), strength, player.weak);
        }

        int CurrentAttack() => MainAttack() + SweepAttack();

        int CurrentBlock() => BattleResolver.ApplyFrail(BattleResolver.PlayerBlock(
            rolled.Where(r => r.assignment == Assignment.Block || r.bothSides).Select(r => EffectiveValue(r, Assignment.Block))), player.frail);

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

            // 振った順に、弱体を与えるダイスのところで区切る
            var segments = new List<List<RolledDie>>();
            var current = new List<RolledDie>();
            foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack || x.bothSides))
            {
                current.Add(r);
                if (VulnerableGiven(r) > 0)
                {
                    segments.Add(current);
                    current = new List<RolledDie>();
                }
            }
            if (current.Count > 0) segments.Add(current);

            bool strengthUsed = false;
            int targetIndex = enemies.IndexOf(Target);
            foreach (var seg in segments)
            {
                var mainValues = seg.Where(r => !r.HitsAll).Select(r => EffectiveValue(r, Assignment.Attack)).ToList();
                var sweepValues = seg.Where(r => r.HitsAll).Select(r => EffectiveValue(r, Assignment.Attack)).ToList();
                // 筋力は合計に1回だけ（攻撃のダイスがあればそちらに、なければ薙ぎ賽に）
                int mainStrength = !strengthUsed && mainValues.Count > 0 ? player.strength : 0;
                int sweepStrength = !strengthUsed && mainValues.Count == 0 && sweepValues.Count > 0 ? player.strength : 0;
                if (mainValues.Count > 0 || sweepValues.Count > 0) strengthUsed = true;
                int main = BattleResolver.PlayerAttack(mainValues, mainStrength, player.weak);
                int sweep = BattleResolver.PlayerAttack(sweepValues, sweepStrength, player.weak);
                HitEnemies(main, sweep, hp, block, dealt, extraVulnerable);
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
                foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack || x.bothSides))
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
            return sources.OfType<AttackRiderEffect>()
                .Where(e => e.trigger == Trigger.OnAttackResolve && e.rider == AttackRider.Vulnerable)
                .Sum(e => e.amount + r.value * e.perPip);
        }

        /// <summary>薙ぎ賽は全員、残りは狙った敵から順に当てる（hp・block・dealt を書き換える）。</summary>
        void HitEnemies(int mainAttack, int sweepAttack, int[] hp, int[] block, int[] dealt, int[] extraVulnerable)
        {
            int n = enemies.Count;
            int Hit(int i, int attack)
            {
                var e = enemies[i];
                if (hp[i] <= 0 || attack <= 0) return 0;
                int incoming = BattleResolver.ApplyVulnerable(attack, e.vulnerable + extraVulnerable[i]);
                int absorbed = Math.Min(block[i], incoming);
                block[i] -= absorbed;
                int damage = incoming - absorbed;
                if (e.data.damageCapPerRound > 0) damage = Math.Min(damage, Math.Max(0, e.data.damageCapPerRound - dealt[i]));
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
            var attackDice = rolled.Where(r => r.assignment == Assignment.Attack || r.bothSides).ToList();
            int rawAttack = attackDice.Sum(r => r.value);
            int targetVulnerable = Target != null ? Target.vulnerable : 0;
            int shownAttack = BattleResolver.ApplyVulnerable(MainAttack(), targetVulnerable) + SweepAttack();

            return new DamagePreview
            {
                dealt = dealt, taken = taken, takenMin = takenMin,
                dealtTrend = attackDice.Count > 0 ? Trend(shownAttack, rawAttack) : 0,
                takenTrend = Trend(maxTotal, baseTotal),
                killsAll = dealt > 0 && enemies.Select((e, i) => e.IsDead || e.hp - dealtPer[i] - e.poison <= 0).All(x => x),
            };
        }

        // ---- ラウンドの解決 ----

        /// <summary>割り振りを確定してラウンドを進める。ダイスを1個も振っていなければパス。</summary>
        public RoundResult Resolve()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");

            var targetIntent = EnemyIntent;
            var infos = enemies.Select((e, i) => new EnemyRoundInfo { enemy = e, index = i, hpBefore = e.hp, intent = e.CurrentIntent }).ToList();
            int diceBlock = CurrentBlock();
            player.block += diceBlock;

            // 攻撃の解決（薙ぎ賽は全員、残りは狙った敵から順に）
            int main = MainAttack();
            int sweep = SweepAttack();
            lastPlayerAttack = main + sweep;
            var aliveBefore = enemies.Select(e => !e.IsDead).ToArray();
            int thorns = ThornsDamage(); // 攻撃で倒しても棘は刺さる
            var plan = PlanAttackInOrder(out var blockAfter);
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                e.block = blockAfter[i];
                e.hp -= plan[i];
                infos[i].dealt = plan[i];
                infos[i].killedByAttack = aliveBefore[i] && e.IsDead;
            }
            OnEnemiesDefeated(infos, infos.Where(x => x.killedByAttack).ToList());

            // 攻撃に置いたダイスごとの「攻撃したとき」の効果（毒賽の毒など）。狙っていた敵に効く
            int totalDealt = infos.Sum(x => x.dealt);
            var statusBefore = infos.Select(x => (x.enemy.weak, x.enemy.vulnerable, x.enemy.frail)).ToList();
            int hpBeforeRiders = player.hp;
            foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack || x.bothSides))
            {
                var ctx = NewContext(Trigger.OnAttackResolve, r.dice, r.faceIndex, r.value, Assignment.Attack);
                ctx.amount = totalDealt; // 吸血はこのラウンドに与えたダメージから
                effects.Fire(ctx, r.dice, r.dice.faces[r.faceIndex].engraving);
            }
            int attackHpChange = player.hp - hpBeforeRiders;
            // 萎え賽・砕き賽・刻印「崩し」などで、敵に与えた脱力・弱体・脆弱（演出用）
            for (int i = 0; i < infos.Count; i++)
            {
                var e = infos[i].enemy;
                infos[i].weakGiven = e.weak - statusBefore[i].weak;
                infos[i].vulnerableGiven = e.vulnerable - statusBefore[i].vulnerable;
                infos[i].frailGiven = e.frail - statusBefore[i].frail;
            }

            // 棘（山颪など）：大きい出目で攻撃したダイス1個ごとにダメージ（防御無視）
            int thornsTaken = thorns > 0 ? player.LoseHp(thorns) : 0;

            // 溜めのラウンドに十分なダメージを与えたら怯む（次の大攻撃が止まる。大顎）
            foreach (var info in infos)
            {
                var i = info.intent;
                info.staggered = i.type == IntentType.Charge && i.value > 0 && info.dealt >= i.value && !info.enemy.IsDead;
                if (info.staggered) info.enemy.Staggered = true;
            }

            // 敵の毒（防御無視）：プレイヤーの攻撃のあと、敵が行動する前（開発者の判断）。毒で倒れた敵は行動しない
            foreach (var info in infos)
            {
                if (info.enemy.IsDead) continue;
                info.poisonDamage = info.enemy.TickPoison();
                info.diedOfPoison = info.enemy.IsDead;
            }
            OnEnemiesDefeated(infos, infos.Where(x => x.diedOfPoison).ToList());

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
                foreach (var info in infos)
                {
                    if (info.enemy.IsDead || !aliveBefore[info.index]) continue;
                    info.acted = true;
                    Act(info);
                    if (player.IsDead)
                    {
                        Outcome = BattleOutcome.Defeat;
                        break;
                    }
                }
            }

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
            foreach (var e in enemies)
            {
                e.TickStatuses();
                // 敵の防御は、次の行動の前に消す（Act の初め）
            }

            if (Outcome == BattleOutcome.Ongoing)
            {
                foreach (var e in AliveEnemies) e.AdvancePattern();
                StartRound();
            }
            else
            {
                EndBattle();
            }
            return result;
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

        /// <summary>敵1体の行動。</summary>
        void Act(EnemyRoundInfo info)
        {
            var e = info.enemy;
            var intent = e.CurrentIntent;
            info.intent = intent; // 足枷などで攻撃のあとに変わることがある
            // 前のラウンドに得た防御は、自分の行動の前に消える（堅守なら半分残る）
            e.EndRoundBlock();
            switch (intent.type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll:
                case IntentType.MirrorAttack:
                    info.taken = player.TakeAttack(BattleResolver.EnemyAttack(intent, e.strength, e.weak)); // 脆弱は TakeAttack の中で
                    break;
                case IntentType.Buff:
                    e.strength += intent.value;
                    break;
                case IntentType.Debuff:
                    player.ApplyWeak(intent.value);
                    break;
                case IntentType.Poison:
                    player.ApplyPoison(intent.value);
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
            if (gain > 0)
            {
                info.blockGained = BattleResolver.ApplyFrail(gain, e.frail);
                e.block += info.blockGained;
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
            player.ClearBattleStatuses();
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) d.state = DiceState.Available;
            }
        }
    }
}
