using System;

namespace SaiNoMichi.Battle
{
    /// <summary>戦闘中の敵1体。毎ラウンドの予告を決める。</summary>
    public class EnemyState : Combatant
    {
        public readonly EnemyData data;
        int patternIndex;

        /// <param name="hpPercent">HP の倍率（%）。前の層の敵が出たときなどに 150。</param>
        public EnemyState(EnemyData data, int hpPercent = 100) : base(Math.Max(1, data.maxHp * hpPercent / 100))
        {
            this.data = data;
        }

        public Intent CurrentIntent { get; private set; }

        /// <summary>第2形態に入っている（八面）。</summary>
        public bool InPhase2 { get; private set; }
        /// <summary>このラウンドの予告で第2形態に入った（演出用）。</summary>
        public bool EnteredPhase2ThisRound { get; private set; }
        /// <summary>予告で振ったダイスの出目（八面。表示用）。</summary>
        public int LastRoll { get; private set; }

        /// <summary>溜めを止められた（次の行動は怯み）。</summary>
        public bool Staggered { get; set; }

        /// <summary>HP が減って攻撃が強くなっている（首狩り）。</summary>
        public bool Enraged { get; private set; }

        /// <summary>
        /// round ラウンド目の予告を決める。ランダムな行動は rng から。
        /// 写し鏡は lastPlayerAttack（前のラウンドのプレイヤーの攻撃値）をそのまま予告にする。
        /// 首狩りのように HP が減ると強くなる敵は、予告の時点の HP で決める（予告と実際の行動が食い違わないように）。
        /// </summary>
        public Intent PrepareIntent(int round, Random rng, int lastPlayerAttack = 0)
        {
            // HP が減ったら第2形態（八面）。行動の並びを最初から
            if (!InPhase2 && data.phase2HpPercent > 0 && data.phase2Pattern != null && data.phase2Pattern.Count > 0
                && hp * 100 <= maxHp * data.phase2HpPercent)
            {
                InPhase2 = true;
                EnteredPhase2ThisRound = true;
                patternIndex = 0;
            }
            else
            {
                EnteredPhase2ThisRound = false;
            }
            var pattern = InPhase2 ? data.phase2Pattern : data.pattern;

            Intent intent;
            switch (data.behavior)
            {
                case EnemyBehavior.Random:
                    intent = pattern[rng.Next(pattern.Count)];
                    break;
                default:
                    intent = pattern[patternIndex % pattern.Count];
                    break;
            }

            // 予告を出すときにダイスを振る（出目は予告で見える。八面）
            if (intent.type == IntentType.RollAttack || intent.type == IntentType.RollBlock)
            {
                int faces = Math.Max(1, intent.maxValue);
                int roll = rng.Next(1, faces + 1);
                LastRoll = roll;
                int value = roll * intent.value;
                intent = intent.type == IntentType.RollAttack
                    ? new Intent(IntentType.DiceRoll, value) { minValue = value, maxValue = value }
                    : new Intent(IntentType.Block, value);
            }

            // 溜めを止められたら、次の行動（大攻撃）の代わりに怯む
            if (Staggered)
            {
                Staggered = false;
                intent = new Intent(IntentType.Stunned, 0);
            }
            if (intent.type == IntentType.MirrorAttack) intent.value = Math.Max(0, lastPlayerAttack);

            // TODO(仕様): 首狩りの「HPが半分以下のとき攻撃2倍」は、予告を出す時点の HP で判定する
            Enraged = data.enrageHpPercent > 0 && hp * 100 <= maxHp * data.enrageHpPercent;
            if (Enraged && intent.IsAttack) intent.value = intent.value * data.enrageAttackPercent / 100;

            CurrentIntent = intent;
            return CurrentIntent;
        }

        /// <summary>このラウンドの攻撃の予告を amount 下げる（0 未満にはしない。刻印「足枷」）。攻撃でなければ何もしない。</summary>
        public void ReduceIntent(int amount)
        {
            var intent = CurrentIntent;
            if (!intent.IsAttack || amount <= 0) return;
            intent.value = Math.Max(0, intent.value - amount);
            if (intent.minValue > 0) intent.minValue = Math.Max(0, intent.minValue - amount);
            if (intent.maxValue > 0) intent.maxValue = Math.Max(0, intent.maxValue - amount);
            CurrentIntent = intent;
        }

        public void AdvancePattern()
        {
            patternIndex++;
        }
    }
}
