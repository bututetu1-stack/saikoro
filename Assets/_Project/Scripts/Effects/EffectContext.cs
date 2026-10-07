using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 効果に渡す「いま何が起きているか」。効果は value や amount を書き換えて結果を変える。
    /// その場面に関係のない欄は null や初期値のまま。
    /// </summary>
    public class EffectContext
    {
        public Trigger trigger;

        public RunState run;
        public Combatant player;
        public EnemyState enemy;
        public BattleState battle;

        // ダイスに関わる場面（振った・割り振った）
        public DiceInstance dice;
        public int faceIndex = -1;
        public int value;
        public Assignment assignment;

        // マスに関わる場面（通過・停止）
        public TileNode tile;

        // 量に関わる場面（ゴールドを得る など）
        public int amount;

        public EffectContext(Trigger trigger)
        {
            this.trigger = trigger;
        }
    }
}
