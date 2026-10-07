namespace SaiNoMichi.Battle
{
    /// <summary>戦闘中の敵1体。行動パターンのどこにいるかを持つ。</summary>
    public class EnemyState : Combatant
    {
        public readonly EnemyData data;
        int patternIndex;

        public EnemyState(EnemyData data) : base(data.maxHp)
        {
            this.data = data;
        }

        public Intent CurrentIntent => data.pattern[patternIndex];

        public void AdvancePattern()
        {
            patternIndex = (patternIndex + 1) % data.pattern.Count;
        }
    }
}
