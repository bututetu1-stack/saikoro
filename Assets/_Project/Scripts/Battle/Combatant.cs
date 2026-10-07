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
        /// <summary>弱体：攻撃値×0.75（切り捨て）。毎ラウンド−1。</summary>
        public int weak;
        // そのラウンドに受けた状態異常は、そのラウンドの終わりには減らさない（受けた直後に消えてしまわないように）
        bool weakAppliedThisRound;

        public void ApplyWeak(int amount)
        {
            if (amount <= 0) return;
            weak += amount;
            weakAppliedThisRound = true;
        }

        /// <summary>ラウンド終了時の状態異常の処理（仕様書 第6章「状態異常」の減り方）。</summary>
        public void TickStatuses()
        {
            if (weakAppliedThisRound) weakAppliedThisRound = false;
            else if (weak > 0) weak--;
        }

        public void ClearBattleStatuses()
        {
            block = 0;
            strength = 0;
            weak = 0;
            weakAppliedThisRound = false;
        }

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
