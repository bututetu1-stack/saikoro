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
        public DiceInstance sealedDie;   // 封印されたダイス（なければ null）
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
        public int MaxDicePerRound { get; set; } = DefaultMaxDicePerRound;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
        public IReadOnlyList<RolledDie> Rolled => rolled;
        public IReadOnlyList<RoundResult> History => history;
        public Intent EnemyIntent => enemy.CurrentIntent;
        public bool CanRollMore => Outcome == BattleOutcome.Ongoing && rolled.Count < MaxDicePerRound && pouch.AvailableCount > 0;

        /// <param name="effects">レリックなどが登録された EffectBus。省略するとダイスそのものの特徴だけが効く。</param>
        /// <param name="run">ゴールドを得る効果（刻印「小判」など）のためのラン。省略可。</param>
        public BattleState(Combatant player, EnemyData enemyData, DicePouch pouch, Random rng, EffectBus effects = null, Run.RunState run = null)
        {
            this.player = player;
            this.pouch = pouch;
            this.rng = rng;
            this.effects = effects ?? new EffectBus();
            this.run = run;
            enemy = new EnemyState(enemyData);

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
            enemy.PrepareIntent(Round, rng);
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
            int dealt = Math.Min(enemy.hp, BattleResolver.DamageAfterBlock(attack, enemy.block));
            if (dealt >= enemy.hp) return new DamagePreview { dealt = dealt };

            int block = player.block + CurrentBlock();
            var intent = EnemyIntent;
            int Taken(int value)
            {
                var shown = intent;
                shown.value = value;
                return Math.Min(player.hp, BattleResolver.DamageAfterBlock(BattleResolver.EnemyAttack(shown, enemy.strength, enemy.weak), block));
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
            enemy.TakeAttack(CurrentAttack());
            int dealt = hpBefore - enemy.hp;

            // 攻撃に置いたダイスごとの「攻撃したとき」の効果（毒賽の毒など）
            foreach (var r in rolled.Where(x => x.assignment == Assignment.Attack))
            {
                effects.Fire(NewContext(Trigger.OnAttackResolve, r.dice, r.faceIndex, r.value, Assignment.Attack), r.dice, r.dice.faces[r.faceIndex].engraving);
            }

            // 敵の行動
            int taken = 0;
            DiceInstance sealedDie = null;
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
                        taken = player.TakeAttack(BattleResolver.EnemyAttack(intent, enemy.strength, enemy.weak));
                        break;
                    case IntentType.Buff:
                        enemy.strength += intent.value;
                        break;
                    case IntentType.Debuff:
                        player.ApplyWeak(intent.value);
                        break;
                    case IntentType.Seal:
                        sealedDie = SealTarget();
                        if (sealedDie != null)
                        {
                            sealedDie.state = DiceState.Sealed;
                            pouch.RefreshIfEmpty();
                        }
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
            player.block = 0;
            enemy.block = 0;

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
            player.ClearBattleStatuses();
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) d.state = DiceState.Available;
            }
        }
    }
}
