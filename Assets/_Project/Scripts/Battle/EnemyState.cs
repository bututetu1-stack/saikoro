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
            Intent intent;
            switch (data.behavior)
            {
                case EnemyBehavior.Random:
                    intent = data.pattern[rng.Next(data.pattern.Count)];
                    break;
                default:
                    intent = data.pattern[patternIndex % data.pattern.Count];
                    break;
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

        public void AdvancePattern()
        {
            patternIndex++;
        }
    }
}
