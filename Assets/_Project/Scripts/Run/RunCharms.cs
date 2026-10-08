using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Run
{
    /// <summary>お守り（仕様書 第10章）。1回だけ使える消耗品で、最大3個まで持てる。</summary>
    public partial class RunState
    {
        public const int MaxCharms = 3;

        readonly List<CharmData> charms = new List<CharmData>();

        public IReadOnlyList<CharmData> Charms => charms;
        public bool CanAddCharm => charms.Count < MaxCharms;

        /// <summary>お守りを持つ。いっぱいなら持てず false。</summary>
        public bool AddCharm(CharmData charm)
        {
            if (charm == null || !CanAddCharm) return false;
            charms.Add(charm);
            NotifyAcquired("charm", charm.displayName);
            return true;
        }

        /// <summary>お守りの候補からランダムに1つ（報酬用の乱数）。候補がなければ null。</summary>
        public CharmData PickCharm(Random rng = null)
        {
            var pool = config.charmPool.Where(c => c != null).ToList();
            return pool.Count > 0 ? pool[(rng ?? random.Reward).Next(pool.Count)] : null;
        }

        void Consume(CharmData charm)
        {
            if (!charms.Remove(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            NotifyAcquired("charm_used", charm.displayName);
        }

        // ---- 使える場面 ----

        /// <summary>移動中（振ったあと、歩き始める前）に使えるか。帰り道（行き先が決まっている移動）では使えない。</summary>
        public static bool CanUseOnMove(CharmData charm, MoveInProgress move) =>
            charm != null && charm.UsedOnMove && move != null && move.dice != null && move.forcedTarget == null
            && move.passed.Count == 0 && move.remaining == move.value;

        /// <summary>戦闘中に使えるか。煙玉は通常戦だけ、振り直し御札は振ったダイスがあるときだけ。</summary>
        public bool CanUseInBattle(CharmData charm, BattleState battle)
        {
            if (charm == null || battle == null || battle.Outcome != BattleOutcome.Ongoing) return false;
            switch (charm.kind)
            {
                case CharmKind.RerollDie: return battle.Rolled.Count > 0;
                case CharmKind.Smoke: return battle.CanFlee;
                case CharmKind.WeakenEnemy:
                case CharmKind.VulnerableEnemy:
                case CharmKind.PoisonEnemy: return battle.Target != null && !battle.Target.IsDead;
                case CharmKind.MoveForward:
                case CharmKind.MoveBack: return false;
                default: return CanUseNow(charm);
            }
        }

        /// <summary>移動や戦闘の操作がいらないお守り（傷薬・押し入れの鍵・残り福）を今使えるか。</summary>
        public bool CanUseNow(CharmData charm)
        {
            if (charm == null) return false;
            switch (charm.kind)
            {
                case CharmKind.Heal: return player.hp < player.maxHp;
                case CharmKind.Unseal: return pouch.All.Any(d => d.state == DiceState.Sealed);
                case CharmKind.ReturnUsed: return pouch.All.Any(d => d.state == DiceState.Used);
                default: return false;
            }
        }

        // ---- 使う ----

        /// <summary>傷薬・押し入れの鍵・残り福を使う。回復した HP、または戻したダイスの数を返す。</summary>
        public int UseCharm(CharmData charm)
        {
            if (!charms.Contains(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            if (!CanUseNow(charm)) throw new InvalidOperationException($"{charm.displayName} は今は使えません。");
            int result = 0;
            switch (charm.kind)
            {
                case CharmKind.Heal:
                    int before = player.hp;
                    player.Heal(charm.amount);
                    result = player.hp - before;
                    break;
                case CharmKind.Unseal:
                case CharmKind.ReturnUsed:
                    var from = charm.kind == CharmKind.Unseal ? DiceState.Sealed : DiceState.Used;
                    foreach (var d in pouch.All.Where(d => d.state == from))
                    {
                        d.state = DiceState.Available;
                        result++;
                    }
                    break;
            }
            Consume(charm);
            return result;
        }

        /// <summary>進み御札・止まり御札・振り直し御札を、振ったばかりの移動に使う。</summary>
        public void UseCharmOnMove(CharmData charm, MoveInProgress move)
        {
            if (!charms.Contains(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            if (!CanUseOnMove(charm, move)) throw new InvalidOperationException($"{charm.displayName} は今は使えません。");
            switch (charm.kind)
            {
                case CharmKind.MoveForward:
                    move.value += charm.amount;
                    break;
                case CharmKind.MoveBack:
                    move.value = Math.Max(1, move.value - charm.amount);
                    break;
                case CharmKind.RerollDie:
                    // ダイスはもう使用済みなので、もう一度使用済みにはしない
                    RollForMove(move, false);
                    break;
            }
            move.remaining = move.value;
            Consume(charm);
        }

        /// <summary>振り直し御札を、戦闘で振ったダイス r に使う。</summary>
        public void UseRerollCharm(CharmData charm, BattleState battle, RolledDie r)
        {
            if (!charms.Contains(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            if (charm.kind != CharmKind.RerollDie || !CanUseInBattle(charm, battle)) throw new InvalidOperationException($"{charm.displayName} は今は使えません。");
            battle.Reroll(r, true);
            Consume(charm);
        }

        /// <summary>薬（脱力・弱体・毒）を、狙っている敵に投げる。</summary>
        public void UseEnemyCharm(CharmData charm, BattleState battle)
        {
            if (!charms.Contains(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            if (!charm.ThrownAtEnemy || !CanUseInBattle(charm, battle)) throw new InvalidOperationException($"{charm.displayName} は今は使えません。");
            var enemy = battle.Target;
            switch (charm.kind)
            {
                case CharmKind.WeakenEnemy: enemy.ApplyWeak(charm.amount); break;
                case CharmKind.VulnerableEnemy: enemy.ApplyVulnerable(charm.amount); break;
                case CharmKind.PoisonEnemy: enemy.ApplyPoison(charm.amount); break;
            }
            Consume(charm);
        }

        /// <summary>煙玉：通常戦から逃げる（報酬なし）。</summary>
        public void UseSmoke(CharmData charm, BattleState battle)
        {
            if (!charms.Contains(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            if (charm.kind != CharmKind.Smoke || !CanUseInBattle(charm, battle)) throw new InvalidOperationException($"{charm.displayName} は今は使えません。");
            battle.Flee();
            Consume(charm);
        }
    }
}
