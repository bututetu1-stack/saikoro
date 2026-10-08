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
    }

    public struct DamagePreview
    {
        public int dealt;      // 敵の HP に通るダメージ
        public int taken;      // 自分の HP に通るダメージ（賽振りのように値が隠れているときは最大の場合）
        public int takenMin;   // 値が隠れているときの最小の場合（隠れていなければ taken と同じ）
        public bool TakenIsRange => takenMin != taken;
    }

    public struct RoundResult
    {
        public int round;
        public int dealt;
        public int taken;
        public Intent enemyIntent;
        public IReadOnlyList<RolledDie> rolled;
        public DiceInstance sealedDie;   // 封印されたダイス（なければ null。2個封印したときは最初の1個）
        public int sealedCount;          // 封印したダイスの数
        public DiceInstance curseDie;    // 呪いで押し付けられたダイス（なければ null）
        public bool staggered;           // 溜めを止めた（敵は次のラウンド怯む）
        public int enemyPoisonDamage;    // ラウンド終了時の毒で敵が受けたダメージ
        public int playerPoisonDamage;
    }

    /// <summary>
    /// 1回の戦闘（敵1体）。ラウンドの流れ：
    /// StartRound（予告。防御の予告はここで敵の防御値になる）→ Roll を0〜2回 → Assign → Resolve（攻撃の解決 → 敵の行動 → ラウンド終了）。
    /// 振ったダイスはその場で使用済みになり、使用可能が0個ならリフレッシュする。戦闘が終わっても使用済みのまま。
    /// </summary>
    public class BattleState
    {
        // TODO(仕様): 1ラウンドに振れる数はレリックで3まで増える（フェーズ1）
        public const int DefaultMaxDicePerRound = 2;

        public readonly Combatant player;
        public readonly EnemyState enemy;
        public readonly DicePouch pouch;
        readonly Random rng;
        readonly EffectBus effects;
        readonly Run.RunState run;

        readonly List<RolledDie> rolled = new List<RolledDie>();
        readonly List<RoundResult> history = new List<RoundResult>();

        public int Round { get; private set; }
        /// <summary>1ラウンドに振れる数（縛りのラウンドは1個）。代入すると基本の数が変わる（古い賽筒など）。</summary>
        public int MaxDicePerRound
        {
            get => player.bind > 0 ? Math.Min(1, BaseDicePerRound) : BaseDicePerRound;
            set => BaseDicePerRound = value;
        }
        public int BaseDicePerRound { get; set; } = DefaultMaxDicePerRound;

        // 前のラウンドにプレイヤーが出した攻撃値（写し鏡が返す）
        int lastPlayerAttack;

        /// <summary>敵の「1ラウンドに受けるダメージの上限」をかける。</summary>
        int CapDamage(int damage) => enemy.data.damageCapPerRound > 0 ? Math.Min(damage, enemy.data.damageCapPerRound) : damage;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
        public IReadOnlyList<RolledDie> Rolled => rolled;
        public IReadOnlyList<RoundResult> History => history;
        public Intent EnemyIntent => enemy.CurrentIntent;
        public bool CanRollMore => Outcome == BattleOutcome.Ongoing && rolled.Count < MaxDicePerRound && pouch.AvailableCount > 0;

        /// <param name="effects">レリックなどが登録された EffectBus。省略するとダイスそのものの特徴だけが効く。</param>
        /// <param name="run">ゴールドを得る効果（刻印「小判」など）のためのラン。省略可。</param>
        /// <param name="enemyHpPercent">敵の HP の倍率（%）。前の層の敵が出たときなど。</param>
        public BattleState(Combatant player, EnemyData enemyData, DicePouch pouch, Random rng, EffectBus effects = null, Run.RunState run = null, int enemyHpPercent = 100)
        {
            this.player = player;
            this.pouch = pouch;
            this.rng = rng;
            this.effects = effects ?? new EffectBus();
            this.run = run;
            enemy = new EnemyState(enemyData, enemyHpPercent);

            player.ClearBattleStatuses();
            if (run != null) run.CurrentBattle = this;
            // 戦闘開始時の効果（木の盾の防御など）。防御は1ラウンド目の終わりまで残る
            this.effects.Fire(new EffectContext(Trigger.OnBattleStart) { player = player, enemy = enemy, battle = this, run = run });
            StartRound();
        }

        void StartRound()
        {
            Round++;
            rolled.Clear();
            enemy.PrepareIntent(Round, rng, lastPlayerAttack);
            if (EnemyIntent.type == IntentType.Block)
            {
                enemy.block += EnemyIntent.value;
            }
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
                enemy = enemy,
                battle = this,
                run = run,
                dice = die,
                faceIndex = faceIndex,
                value = value,
                assignment = assignment,
            };
        }

        int CurrentAttack() => BattleResolver.PlayerAttack(
            rolled.Where(r => r.assignment == Assignment.Attack).Select(r => EffectiveValue(r, Assignment.Attack)), player.strength, player.weak);

        int CurrentBlock() => BattleResolver.PlayerBlock(
            rolled.Where(r => r.assignment == Assignment.Block).Select(r => EffectiveValue(r, Assignment.Block)));

        /// <summary>今の割り振りでの攻撃値（筋力込み）と防御値。演出や表示に使う。</summary>
        public int AttackValue => CurrentAttack();
        public int BlockValue => CurrentBlock();

        /// <summary>今の割り振りで「与えるダメージ／受けるダメージ」がいくつになるか。賽振りは値が隠れているので範囲で返す。</summary>
        public DamagePreview Preview()
        {
            int attack = CurrentAttack();
            int dealt = Math.Min(enemy.hp, CapDamage(BattleResolver.DamageAfterBlock(BattleResolver.ApplyVulnerable(attack, enemy.vulnerable), enemy.block)));
            if (dealt >= enemy.hp) return new DamagePreview { dealt = dealt };

            int block = player.block + CurrentBlock();
            var intent = EnemyIntent;
            int Taken(int value)
            {
                var shown = intent;
                shown.value = value;
                return Math.Min(player.hp, BattleResolver.DamageAfterBlock(BattleResolver.ApplyVulnerable(BattleResolver.EnemyAttack(shown, enemy.strength, enemy.weak), player.vulnerable), block));
            }

            if (intent.type == IntentType.DiceRoll && intent.minValue < intent.maxValue)
            {
                return new DamagePreview { dealt = dealt, taken = Taken(intent.maxValue), takenMin = Taken(intent.minValue) };
            }
            int taken = Taken(intent.value);
            return new DamagePreview { dealt = dealt, taken = taken, takenMin = taken };
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

        /// <summary>割り振りを確定してラウンドを進める。ダイスを1個も振っていなければパス。</summary>
        public RoundResult Resolve()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");

            var intent = EnemyIntent;
            player.block += CurrentBlock();

            // 攻撃の解決
            int hpBefore = enemy.hp;
            int attackValue = CurrentAttack();
            lastPlayerAttack = attackValue;
            enemy.TakeAttack(attackValue);
            // 1ラウンドに受けるダメージの上限（石の守護者）
            int capped = CapDamage(hpBefore - enemy.hp);
            enemy.hp = hpBefore - capped;
            int dealt = hpBefore - enemy.hp;

            // 攻撃に置いたダイスごとの「攻撃したとき」の効果（毒賽の毒など）
            foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack))
            {
                effects.Fire(NewContext(Trigger.OnAttackResolve, r.dice, r.faceIndex, r.value, Assignment.Attack), r.dice, r.dice.faces[r.faceIndex].engraving);
            }

            // 溜めのラウンドに十分なダメージを与えたら怯む（次の大攻撃が止まる。大顎）
            bool staggered = intent.type == IntentType.Charge && intent.value > 0 && dealt >= intent.value && !enemy.IsDead;
            if (staggered) enemy.Staggered = true;

            // 敵の行動
            int taken = 0;
            DiceInstance sealedDie = null;
            int sealedCount = 0;
            DiceInstance curseDie = null;
            if (enemy.IsDead)
            {
                Outcome = BattleOutcome.Victory;
            }
            else
            {
                switch (intent.type)
                {
                    case IntentType.Attack:
                    case IntentType.MultiAttack:
                    case IntentType.DiceRoll:
                    case IntentType.MirrorAttack:
                        taken = player.TakeAttack(BattleResolver.EnemyAttack(intent, enemy.strength, enemy.weak)); // 脆弱は TakeAttack の中で
                        break;
                    case IntentType.Buff:
                        enemy.strength += intent.value;
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
                        curseDie = run?.ForceCurse();
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
                            if (sealedDie == null) sealedDie = target;
                            sealedCount++;
                        }
                        if (sealedCount > 0) pouch.RefreshIfEmpty();
                        break;
                    case IntentType.ResetDice:
                        // 全ダイスを使用済みにする。その瞬間に使用可能が0個になるのでリフレッシュが起きる（仕様書 第7章）
                        foreach (var d in pouch.All)
                        {
                            if (d.state == DiceState.Available) d.state = DiceState.Used;
                        }
                        pouch.RefreshIfEmpty();
                        break;
                    case IntentType.Block:
                        break; // 予告の時点で反映済み
                }
                if (player.IsDead) Outcome = BattleOutcome.Defeat;
            }

            var result = new RoundResult
            {
                round = Round,
                dealt = dealt,
                taken = taken,
                enemyIntent = intent,
                rolled = rolled.ToList(),
                sealedDie = sealedDie,
                sealedCount = sealedCount,
                curseDie = curseDie,
                staggered = staggered,
            };

            // ラウンド終了の毒（防御無視）。敵が先
            if (Outcome == BattleOutcome.Ongoing)
            {
                result.enemyPoisonDamage = enemy.TickPoison();
                if (enemy.IsDead) Outcome = BattleOutcome.Victory;
            }
            if (Outcome == BattleOutcome.Ongoing)
            {
                result.playerPoisonDamage = player.TickPoison();
                if (player.IsDead) Outcome = BattleOutcome.Defeat;
            }
            history.Add(result);

            // ラウンド終了：状態異常を処理し、双方の防御値を0に戻す
            player.TickStatuses();
            enemy.TickStatuses();
            player.EndRoundBlock();
            enemy.EndRoundBlock(); // 堅守があれば半分残る

            if (Outcome == BattleOutcome.Ongoing)
            {
                enemy.AdvancePattern();
                StartRound();
            }
            else
            {
                EndBattle();
            }
            return result;
        }

        /// <summary>戦闘終了の後片付け。封印は解除するが、使用済みはそのまま残す。</summary>
        void EndBattle()
        {
            if (run != null && run.CurrentBattle == this) run.CurrentBattle = null;
            if (run != null && Outcome == BattleOutcome.Victory) run.stats.CountVictory(enemy.data.kind);
            player.ClearBattleStatuses();
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) d.state = DiceState.Available;
            }
        }
    }
}
