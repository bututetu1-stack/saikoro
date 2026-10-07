using System.Collections.Generic;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    public enum TrapKind
    {
        Damage,
        Seal,
        Curse,
    }

    public class TrapResult
    {
        public TrapKind kind;
        public int damage;
        public DiceInstance sealedDie;
        public DiceInstance curseDie;
        public string message;
    }

    public class TreasureResult
    {
        public int gold;
        public RelicData relic;
        public DiceData diceOffer;   // 持っていくかは選ぶ
        public string message;
    }

    public class PassTileResult
    {
        public int gold;           // 得た（正）・払った（負）ゴールド
        public int damage;
        public int healed;
        public List<DiceInstance> returnedDice = new List<DiceInstance>();
        public string message;
    }
}
