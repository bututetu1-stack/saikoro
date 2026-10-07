using System;

namespace SaiNoMichi.Battle
{
    /// <summary>戦闘に参加する者（プレイヤー・敵）の HP・防御値・筋力。</summary>
    public class Combatant
    {
        public int hp;
        public int maxHp;
        public int block;
        public int strength;

        public Combatant(int maxHp) : this(maxHp, maxHp) { }

        public Combatant(int hp, int maxHp)
        {
            this.hp = hp;
            this.maxHp = maxHp;
        }

        public bool IsDead => hp <= 0;

        /// <summary>攻撃を受ける。防御値を先に削り、残りで HP を減らす。HP に通ったダメージを返す。</summary>
        public int TakeAttack(int amount)
        {
            if (amount <= 0) return 0;
            int absorbed = Math.Min(block, amount);
            block -= absorbed;
            int damage = amount - absorbed;
            hp = Math.Max(0, hp - damage);
            return damage;
        }

        public void Heal(int amount)
        {
            hp = Math.Min(maxHp, hp + Math.Max(0, amount));
        }
    }
}
