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
    }

    /// <summary>このラウンドに振ったダイス1個と、その割り振り先。</summary>
    public class RolledDie
    {
        public DiceInstance dice;
        public int faceIndex;
        public int value;   // 出た面の値（OnRoll の効果のあと）。攻撃・防御に置いたときの値は BattleState.EffectiveValue
        public Assignment assignment;

        /// <summary>全部の敵に当たるダイス（薙ぎ賽）か。</summary>
        public bool HitsAll => dice.data != null && dice.data.hitsAll;
    }

    public struct DamagePreview
    {
        public int dealt;      // 敵の HP に通るダメージ（敵が複数なら合計）
        public int taken;      // 自分の HP に通るダメージ（賽振りのように値が隠れているときは最大の場合）
        public int takenMin;   // 値が隠れているときの最小の場合（隠れていなければ taken と同じ）
        public bool TakenIsRange => takenMin != taken;
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
        public int poisonDamage;       // ラウンド終了時の毒
        public bool diedOfPoison;
        public int strengthGained;     // 仲間が倒れて得た筋力（双子鬼）
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
        public int enemyPoisonDamage;    // ラウンド終了時の毒で敵が受けたダメージ（合計）
        public int playerPoisonDamage;
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
        public const int DefaultMaxDicePerRound = 2;
        // 古い賽筒・2体の敵などで増えても、1ラウンドに振れるのはここまで（仕様書 第10章「最大3個」）
        public const int MaxDiceCap = 3;

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
        /// 1ラウンドに振れる数。敵が2体以上いる間は +1（開発者の判断）。最大3個。縛りのラウンドは1個。
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
            foreach (var data in enemyData) enemies.Add(new EnemyState(data, enemyHpPercent));

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
                if (e.CurrentIntent.type == IntentType.Block) e.block += e.CurrentIntent.value;
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
            var r = new RolledDie { dice = die, faceIndex = faceIndex, value = Math.Max(0, ctx.value), assignment = Assignment.Attack };
            rolled.Add(r);
            LastRolledValue = r.value;

            if (player.IsDead)
            {
                Outcome = BattleOutcome.Defeat;
                EndBattle();
            }
            return r;
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

        IEnumerable<RolledDie> AttackDice(bool hitsAll) => rolled.Where(r => r.assignment == Assignment.Attack && r.HitsAll == hitsAll);

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

        int CurrentBlock() => BattleResolver.PlayerBlock(
            rolled.Where(r => r.assignment == Assignment.Block).Select(r => EffectiveValue(r, Assignment.Block)));

        /// <summary>今の割り振りでの攻撃値（筋力込み。薙ぎ賽の分も足した合計）と防御値。演出や表示に使う。</summary>
        public int AttackValue => CurrentAttack();
        public int BlockValue => CurrentBlock();

        /// <summary>
        /// 攻撃を当てたときに、敵ごとに HP がいくつ減るか（実際には減らさない）。
        /// 薙ぎ賽の分を全員に当ててから、残りを狙った敵 → 次の敵…の順に、倒しきって余った分を回す。
        /// 脆弱・防御・1ラウンドのダメージ上限も考える。
        /// </summary>
        int[] PlanAttack(int mainAttack, int sweepAttack) => PlanAttack(mainAttack, sweepAttack, out _);

        /// <param name="blockAfter">当たったあとの、敵ごとの防御値。</param>
        int[] PlanAttack(int mainAttack, int sweepAttack, out int[] blockAfter)
        {
            int n = enemies.Count;
            var hp = enemies.Select(e => e.hp).ToArray();
            var block = enemies.Select(e => e.block).ToArray();
            var dealt = new int[n];
            blockAfter = block;

            int Hit(int i, int attack)
            {
                var e = enemies[i];
                if (hp[i] <= 0 || attack <= 0) return 0;
                int incoming = BattleResolver.ApplyVulnerable(attack, e.vulnerable);
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
            return dealt;
        }

        /// <summary>今の割り振りで「与えるダメージ／受けるダメージ」がいくつになるか。賽振りは値が隠れているので範囲で返す。</summary>
        public DamagePreview Preview()
        {
            var dealtPer = PlanAttack(MainAttack(), SweepAttack());
            int dealt = dealtPer.Sum();

            // 倒しきれない敵の攻撃を合計して、防御を引く
            int block = player.block + CurrentBlock();
            int minTotal = 0, maxTotal = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e.IsDead || e.hp - dealtPer[i] <= 0) continue;
                var intent = e.CurrentIntent;
                int Attack(int value)
                {
                    var shown = intent;
                    shown.value = value;
                    return BattleResolver.ApplyVulnerable(BattleResolver.EnemyAttack(shown, e.strength, e.weak), player.vulnerable);
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
            int taken = Math.Min(player.hp, BattleResolver.DamageAfterBlock(maxTotal, block));
            int takenMin = Math.Min(player.hp, BattleResolver.DamageAfterBlock(minTotal, block));
            return new DamagePreview { dealt = dealt, taken = taken, takenMin = takenMin };
        }

        /// <summary>封印の対象：使用可能なダイスのうち、出目の平均が最も高いもの（同じなら先のもの）。</summary>
        public DiceInstance SealTarget()
        {
            DiceInstance best = null;
            double bestAverage = double.MinValue;
            foreach (var d in pouch.Available)
            {
                double average = d.faces.Average(f => f.value);
                if (average > bestAverage)
                {
                    best = d;
                    bestAverage = average;
                }
            }
            return best;
        }

        // ---- ラウンドの解決 ----

        /// <summary>割り振りを確定してラウンドを進める。ダイスを1個も振っていなければパス。</summary>
        public RoundResult Resolve()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");

            var targetIntent = EnemyIntent;
            var infos = enemies.Select((e, i) => new EnemyRoundInfo { enemy = e, index = i, hpBefore = e.hp, intent = e.CurrentIntent }).ToList();
            player.block += CurrentBlock();

            // 攻撃の解決（薙ぎ賽は全員、残りは狙った敵から順に）
            int main = MainAttack();
            int sweep = SweepAttack();
            lastPlayerAttack = main + sweep;
            var aliveBefore = enemies.Select(e => !e.IsDead).ToArray();
            var plan = PlanAttack(main, sweep, out var blockAfter);
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
            foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack))
            {
                effects.Fire(NewContext(Trigger.OnAttackResolve, r.dice, r.faceIndex, r.value, Assignment.Attack), r.dice, r.dice.faces[r.faceIndex].engraving);
            }

            // 溜めのラウンドに十分なダメージを与えたら怯む（次の大攻撃が止まる。大顎）
            foreach (var info in infos)
            {
                var i = info.intent;
                info.staggered = i.type == IntentType.Charge && i.value > 0 && info.dealt >= i.value && !info.enemy.IsDead;
                if (info.staggered) info.enemy.Staggered = true;
            }

            // 敵の行動（倒れていない敵が、並び順に）
            if (!AliveEnemies.Any())
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

            // ラウンド終了の毒（防御無視）。敵が先
            int playerPoison = 0;
            if (Outcome == BattleOutcome.Ongoing)
            {
                foreach (var info in infos)
                {
                    if (info.enemy.IsDead) continue;
                    info.poisonDamage = info.enemy.TickPoison();
                    info.diedOfPoison = info.enemy.IsDead;
                }
                OnEnemiesDefeated(infos, infos.Where(x => x.diedOfPoison).ToList());
                if (!AliveEnemies.Any()) Outcome = BattleOutcome.Victory;
            }
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
                enemies = infos,
            };
            history.Add(result);

            // ラウンド終了：状態異常を処理し、防御値を0に戻す（堅守なら半分残る）
            player.TickStatuses();
            player.EndRoundBlock();
            foreach (var e in enemies)
            {
                e.TickStatuses();
                e.EndRoundBlock();
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
                case IntentType.Charge:
                case IntentType.Stunned:
                    break; // 何もしない（溜め・怯み）
                case IntentType.Seal:
                    // value 個まで封印（0 以下は1個。大顎の「封印×2」など）
                    for (int i = 0; i < Math.Max(1, intent.value); i++)
                    {
                        var target = SealTarget();
                        if (target == null) break;
                        target.state = DiceState.Sealed;
                        if (info.sealedDie == null) info.sealedDie = target;
                        info.sealedCount++;
                    }
                    if (info.sealedCount > 0) pouch.RefreshIfEmpty();
                    break;
                case IntentType.ResetDice:
                    // 全ダイスを使用済みにする。その瞬間に使用可能が0個になるのでリフレッシュが起きる（仕様書 第7章）
                    foreach (var d in pouch.All)
                    {
                        if (d.state == DiceState.Available) d.state = DiceState.Used;
                    }
                    pouch.RefreshIfEmpty();
                    break;
                case IntentType.RewriteFate:
                    RewriteFate(info);
                    break;
                case IntentType.Block:
                    break; // 予告の時点で反映済み
            }
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
