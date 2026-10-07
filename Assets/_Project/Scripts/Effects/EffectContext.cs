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

        // 移動の出目を前後いくつまで変えてよいか（刻印「風」・レリック「草鞋」など。0 なら変えられない）
        public int moveAdjust;
        // 出目を変えられる理由（「風」「草鞋」など。画面の表示用）と、回数に限りがあるときの持ち主
        public string moveAdjustLabel;
        public ChargedMoveAdjustEffect moveAdjustCharge;

        // 振ったダイスを使用済みにしない（レリック「小石」など）
        public bool keepAvailable;

        // 量に関わる場面（ゴールドを得る など）
        public int amount;
        // 戦闘の報酬で得るゴールドか（レリック「銭袋」）
        public bool fromBattle;

        public EffectContext(Trigger trigger)
        {
            this.trigger = trigger;
        }
    }
}
