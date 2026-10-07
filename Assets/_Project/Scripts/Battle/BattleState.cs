using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Dice;

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
        public int value;
        public Assignment assignment;
    }

    public struct DamagePreview
    {
        public int dealt;   // 敵の HP に通るダメージ
        public int taken;   // 自分の HP に通るダメージ
    }

    public struct RoundResult
    {
        public int round;
        public int dealt;
        public int taken;
        public Intent enemyIntent;
        public IReadOnlyList<RolledDie> rolled;
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

        readonly List<RolledDie> rolled = new List<RolledDie>();
        readonly List<RoundResult> history = new List<RoundResult>();

        public int Round { get; private set; }
        public int MaxDicePerRound { get; set; } = DefaultMaxDicePerRound;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;
        public IReadOnlyList<RolledDie> Rolled => rolled;
        public IReadOnlyList<RoundResult> History => history;
        public Intent EnemyIntent => enemy.CurrentIntent;
        public bool CanRollMore => Outcome == BattleOutcome.Ongoing && rolled.Count < MaxDicePerRound && pouch.AvailableCount > 0;

        public BattleState(Combatant player, EnemyData enemyData, DicePouch pouch, Random rng)
        {
            this.player = player;
            this.pouch = pouch;
            this.rng = rng;
            enemy = new EnemyState(enemyData);

            player.block = 0;
            player.strength = 0;
            StartRound();
        }

        void StartRound()
        {
            Round++;
            rolled.Clear();
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

            pouch.Use(die); // 使用可能でなければここで例外
            var r = new RolledDie { dice = die, value = die.Roll(rng), assignment = Assignment.Attack };
            rolled.Add(r);
            return r;
        }

        public void Assign(RolledDie die, Assignment assignment)
        {
            if (!rolled.Contains(die)) throw new ArgumentException("このラウンドに振ったダイスではありません。", nameof(die));
            die.assignment = assignment;
        }

        int CurrentAttack() => BattleResolver.PlayerAttack(
            rolled.Where(r => r.assignment == Assignment.Attack).Select(r => r.value), player.strength);

        int CurrentBlock() => BattleResolver.PlayerBlock(
            rolled.Where(r => r.assignment == Assignment.Block).Select(r => r.value));

        /// <summary>今の割り振りで「与えるダメージ／受けるダメージ」がいくつになるか。</summary>
        public DamagePreview Preview()
        {
            int attack = CurrentAttack();
            int dealt = Math.Min(enemy.hp, BattleResolver.DamageAfterBlock(attack, enemy.block));
            bool enemyDies = dealt >= enemy.hp;
            int taken = enemyDies ? 0 : BattleResolver.DamageAfterBlock(
                BattleResolver.EnemyAttack(EnemyIntent, enemy.strength), player.block + CurrentBlock());
            return new DamagePreview { dealt = dealt, taken = Math.Min(player.hp, taken) };
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

            // 敵の行動
            int taken = 0;
            if (enemy.IsDead)
            {
                Outcome = BattleOutcome.Victory;
            }
            else
            {
                switch (intent.type)
                {
                    case IntentType.Attack:
                        taken = player.TakeAttack(BattleResolver.EnemyAttack(intent, enemy.strength));
                        break;
                    case IntentType.Buff:
                        enemy.strength += intent.value;
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
            };
            history.Add(result);

            // ラウンド終了：双方の防御値を0に戻す
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
            player.block = 0;
            player.strength = 0;
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) d.state = DiceState.Available;
            }
        }
    }
}
