using System;

namespace SaiNoMichi.Battle
{
    /// <summary>戦闘に参加する者（プレイヤー・敵）の HP・防御値・筋力と状態異常（仕様書 第6章）。</summary>
    public class Combatant
    {
        public int hp;
        public int maxHp;
        public int block;
        public int strength;
        /// <summary>脱力（識別子は weak）：攻撃値×0.75（切り捨て）。毎ラウンド−1。</summary>
        public int weak;
        /// <summary>弱体（識別子は vulnerable）：受けるダメージ×1.5（切り捨て）。毎ラウンド−1。</summary>
        public int vulnerable;
        /// <summary>脆弱（識別子は frail）：作れる防御値×0.75（切り捨て）。毎ラウンド−1。</summary>
        public int frail;
        /// <summary>堅守：防御値がラウンド終了で消えず、半分（切り捨て）残る。毎ラウンド−1。</summary>
        public int fortify;
        /// <summary>縛り：振れるダイスが1個になる（プレイヤーのみ）。1ラウンドで解除。</summary>
        public int bind;
        // そのラウンドに受けた状態異常は、そのラウンドの終わりには減らさない（受けた直後に消えてしまわないように）
        bool weakAppliedThisRound;
        bool vulnerableAppliedThisRound;
        bool frailAppliedThisRound;
        bool fortifyAppliedThisRound;
        bool bindAppliedThisRound;

        /// <summary>毒：ラウンド終了時に毒の値だけダメージ（防御無視）。毎ラウンド−1。</summary>
        public int poison;

        public void ApplyPoison(int amount)
        {
            if (amount > 0) poison += amount;
        }

        /// <summary>ラウンド終了時の毒の処理。受けたダメージを返す。</summary>
        public int TickPoison()
        {
            if (poison <= 0) return 0;
            int damage = LoseHp(poison);
            poison--;
            return damage;
        }

        /// <summary>防御を無視して HP を減らす（毒・錆び賽など）。実際に減った量を返す。</summary>
        public int LoseHp(int amount)
        {
            if (amount <= 0) return 0;
            int before = hp;
            hp = Math.Max(0, hp - amount);
            return before - hp;
        }

        public void ApplyWeak(int amount)
        {
            if (amount <= 0) return;
            weak += amount;
            weakAppliedThisRound = true;
        }

        public void ApplyVulnerable(int amount)
        {
            if (amount <= 0) return;
            vulnerable += amount;
            vulnerableAppliedThisRound = true;
        }

        public void ApplyFrail(int amount)
        {
            if (amount <= 0) return;
            frail += amount;
            frailAppliedThisRound = true;
        }

        public void ApplyFortify(int amount)
        {
            if (amount <= 0) return;
            fortify += amount;
            fortifyAppliedThisRound = true;
        }

        /// <summary>縛り：次のラウンド、振れるダイスが1個になる。</summary>
        public void ApplyBind()
        {
            bind = 1;
            bindAppliedThisRound = true;
        }

        /// <summary>ラウンド終了時の状態異常の処理（仕様書 第6章「状態異常」の減り方）。</summary>
        public void TickStatuses()
        {
            Tick(ref weak, ref weakAppliedThisRound);
            Tick(ref vulnerable, ref vulnerableAppliedThisRound);
            Tick(ref frail, ref frailAppliedThisRound);
            Tick(ref fortify, ref fortifyAppliedThisRound);
            Tick(ref bind, ref bindAppliedThisRound);
        }

        static void Tick(ref int value, ref bool appliedThisRound)
        {
            if (appliedThisRound) appliedThisRound = false;
            else if (value > 0) value--;
        }

        /// <summary>ラウンド終了時の防御値：堅守があれば半分残る（切り捨て）、なければ0。</summary>
        public void EndRoundBlock()
        {
            block = fortify > 0 ? block / 2 : 0;
        }

        public void ClearBattleStatuses()
        {
            block = 0;
            strength = 0;
            weak = 0;
            vulnerable = 0;
            frail = 0;
            fortify = 0;
            bind = 0;
            poison = 0;
            weakAppliedThisRound = vulnerableAppliedThisRound = frailAppliedThisRound = fortifyAppliedThisRound = bindAppliedThisRound = false;
        }

        public Combatant(int maxHp) : this(maxHp, maxHp) { }

        public Combatant(int hp, int maxHp)
        {
            this.hp = hp;
            this.maxHp = maxHp;
        }

        public bool IsDead => hp <= 0;

        /// <summary>攻撃を受ける。脆弱なら1.5倍にしてから、防御値を先に削り、残りで HP を減らす。HP に通ったダメージを返す。</summary>
        public int TakeAttack(int amount)
        {
            amount = BattleResolver.ApplyVulnerable(amount, vulnerable);
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
