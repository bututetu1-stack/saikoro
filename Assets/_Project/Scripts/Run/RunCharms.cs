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
            var pool = CharmPool;
            return pool.Count > 0 ? pool[(rng ?? random.Reward).Next(pool.Count)] : null;
        }

        /// <summary>お守りを捨てる（ショップで入れ替えるときなど）。</summary>
        public void DiscardCharm(CharmData charm)
        {
            if (!charms.Remove(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            NotifyAcquired("charm_discarded", charm.displayName);
        }

        void Consume(CharmData charm)
        {
            if (!charms.Remove(charm)) throw new ArgumentException("持っていないお守りです。", nameof(charm));
            NotifyAcquired("charm_used", charm.displayName);
        }

        // ---- 使える場面 ----

        /// <summary>戦闘中に使えるか。煙玉は通常戦だけ、振り直し御札は振ったダイスがあるときだけ。</summary>
        public bool CanUseInBattle(CharmData charm, BattleState battle)
        {
            if (charm == null || battle == null || battle.Outcome != BattleOutcome.Ongoing) return false;
            switch (charm.kind)
            {
                case CharmKind.RerollDie: return battle.HasPending; // もう使ったダイスは振り直せない
                case CharmKind.Smoke: return battle.CanFlee;
                case CharmKind.WeakenEnemy:
                case CharmKind.VulnerableEnemy:
                case CharmKind.FrailEnemy:
                case CharmKind.SapStrength:
                case CharmKind.Hamaya:
                case CharmKind.PoisonEnemy: return battle.Target != null && !battle.Target.IsDead;
                case CharmKind.ThunderAll: return battle.AliveEnemies.Any(e => e.hp > 1);
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
                // 移動のお守りは、マップで振る前（次の移動に効く）か、振ったあと進み始める前（今の移動に効く）に使う。戦闘中は使えない
                // 帰り道（行き先が決まっている）には効かない
                case CharmKind.MoveForward:
                case CharmKind.MoveBack: return CurrentBattle == null && !ReachedGoal && (PendingMove == null || PendingMove.forcedTarget == null);
                // 力の札・守りの札：戦闘中だけ
                case CharmKind.GainStrength:
                case CharmKind.GainBlock:
                case CharmKind.HealParty:
                case CharmKind.BlockParty: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing;
                case CharmKind.ThunderAll: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.AliveEnemies.Any(e => e.hp > 1);
                case CharmKind.GainGold: return true;
                case CharmKind.MaxHp: return true;
                case CharmKind.Fortify: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing;
                case CharmKind.Cleanse: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing
                    && (player.weak > 0 || player.vulnerable > 0 || player.frail > 0 || player.poison > 0 || player.bind > 0);
                case CharmKind.PoisonAll:
                case CharmKind.WeakenAll:
                case CharmKind.VulnerableAll: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.AliveEnemies.Any();
                // 厄払いの札・砥石：ポーチの中身が変わるので、戦闘の外だけ
                case CharmKind.RemoveCurse: return CurrentBattle == null && CurseToRemove != null;
                case CharmKind.ForgeFace: return CurrentBattle == null && FeastTalkTarget != null;
                case CharmKind.Transform: return CurrentBattle == null && TransformTarget != null;
                case CharmKind.RerollAll: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.HasPending;
                case CharmKind.HealPercent: return player.hp < player.maxHp;
                case CharmKind.BreakBlock: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.AliveEnemies.Any(e => e.block > 0);
                case CharmKind.MoreDice: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.BaseDicePerRound < BattleState.MaxDiceCap;
                case CharmKind.DoubleBlock: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && player.block > 0;
                case CharmKind.DoublePoison: return CurrentBattle != null && CurrentBattle.Outcome == BattleOutcome.Ongoing && CurrentBattle.AliveEnemies.Any(e => e.poison > 0);
                case CharmKind.RerollDie:
                    if (CurrentBattle != null || ReachedGoal) return false;
                    return PendingMove != null ? PendingMove.forcedTarget == null : !PendingMoveReroll;
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
                case CharmKind.MoveForward:
                case CharmKind.MoveBack:
                {
                    int delta = charm.kind == CharmKind.MoveForward ? charm.amount : -charm.amount;
                    if (PendingMove != null) ChangePendingMove(delta);
                    else PendingMoveBonus += delta;
                    result = charm.amount;
                    break;
                }
                case CharmKind.GainStrength:
                    player.strength += charm.amount;
                    result = charm.amount;
                    break;
                case CharmKind.GainBlock:
                    player.block += charm.amount;
                    result = charm.amount;
                    break;
                case CharmKind.HealParty:
                {
                    int hpBefore = player.hp;
                    player.Heal(charm.amount);
                    result = player.hp - hpBefore;
                    break;
                }
                case CharmKind.BlockParty:
                    player.block += charm.amount;
                    result = charm.amount;
                    break;
                case CharmKind.ThunderAll:
                    // とどめは刺せない（戦闘の勝ち負けは、ラウンドの解決で決める）
                    foreach (var e in CurrentBattle.AliveEnemies.ToList()) result += e.LoseHp(Math.Min(charm.amount, e.hp - 1));
                    break;
                case CharmKind.GainGold:
                    result = GainGold(charm.amount);
                    break;
                case CharmKind.MaxHp:
                    player.maxHp += charm.amount;
                    player.Heal(charm.amount);
                    result = charm.amount;
                    break;
                case CharmKind.Fortify:
                    player.ApplyFortify(charm.amount);
                    result = charm.amount;
                    break;
                case CharmKind.Cleanse:
                    result = (player.weak > 0 ? 1 : 0) + (player.vulnerable > 0 ? 1 : 0) + (player.frail > 0 ? 1 : 0) + (player.poison > 0 ? 1 : 0) + (player.bind > 0 ? 1 : 0);
                    player.weak = player.vulnerable = player.frail = player.poison = player.bind = 0;
                    break;
                case CharmKind.RerollDie:
                    if (PendingMove != null) RerollPendingMove();
                    else PendingMoveReroll = true;
                    break;
                case CharmKind.PoisonAll:
                    foreach (var e in CurrentBattle.AliveEnemies.ToList()) { e.ApplyPoison(charm.amount); result++; }
                    break;
                case CharmKind.WeakenAll:
                    foreach (var e in CurrentBattle.AliveEnemies.ToList()) { e.ApplyWeak(charm.amount + DebuffBonus); result++; } // 細工師は +1
                    break;
                case CharmKind.VulnerableAll:
                    foreach (var e in CurrentBattle.AliveEnemies.ToList()) { e.ApplyVulnerable(charm.amount + DebuffBonus); result++; }
                    break;
                case CharmKind.RemoveCurse:
                    DiscardDice(CurseToRemove);
                    result = 1;
                    break;
                case CharmKind.ForgeFace:
                    for (int i = 0; i < Math.Max(1, charm.amount); i++)
                        if (FeastTalk(out _, out _) != null) result++;
                    break;
                case CharmKind.RerollAll:
                    foreach (var r in CurrentBattle.Pending.ToList())
                    {
                        CurrentBattle.Reroll(r, true);
                        result++;
                        if (CurrentBattle.Outcome != BattleOutcome.Ongoing) break; // 錆び賽などで倒れたら止める
                    }
                    break;
                case CharmKind.HealPercent:
                {
                    int hpBefore = player.hp;
                    player.Heal(Math.Max(1, player.maxHp * charm.amount / 100));
                    result = player.hp - hpBefore;
                    break;
                }
                case CharmKind.BreakBlock:
                    foreach (var e in CurrentBattle.AliveEnemies) { result += e.block; e.block = 0; }
                    break;
                case CharmKind.MoreDice:
                {
                    int diceBefore = CurrentBattle.BaseDicePerRound;
                    CurrentBattle.BaseDicePerRound = Math.Min(BattleState.MaxDiceCap, diceBefore + Math.Max(1, charm.amount));
                    result = CurrentBattle.BaseDicePerRound - diceBefore;
                    break;
                }
                case CharmKind.DoubleBlock:
                    result = player.block;
                    player.block *= 2;
                    break;
                case CharmKind.DoublePoison:
                    foreach (var e in CurrentBattle.AliveEnemies) { result += e.poison; e.poison *= 2; }
                    break;
                case CharmKind.Transform:
                {
                    var oldDie = TransformTarget;
                    var newDie = OfferToTsukumogami(oldDie);
                    if (newDie != null) { result = 1; LastTransform = $"{oldDie.DisplayName} → {newDie.DisplayName}"; }
                    break;
                }
            }
            Consume(charm);
            return result;
        }

        /// <summary>化け札で化けさせるダイス：呪いのダイスがあればそれ、なければ面の合計がいちばん小さいダイス。化ける先がなければ null。</summary>
        public DiceInstance TransformTarget
        {
            get
            {
                var die = CurseToRemove ?? pouch.All.Where(d => d.data != null).OrderBy(d => d.faces.Sum(f => f.value)).FirstOrDefault();
                if (die == null) return null;
                var rarity = TsukumogamiRarity(die);
                return config.rewardDicePool.Any(d => d != null && d.rarity == rarity && d != die.data) ? die : null;
            }
        }

        /// <summary>最後に化け札で化けたダイス（「○○ → △△」。画面の文に使う）。</summary>
        public string LastTransform { get; private set; }

        /// <summary>厄払いの札で捨てる呪いのダイス（ポーチの先頭から。なければ null）。</summary>
        public DiceInstance CurseToRemove => pouch.All.FirstOrDefault(d => d.data != null && d.data.rarity == Core.Rarity.Curse);

        // ---- 移動のお守り（開発者の判断：振るたびに聞かれると煩わしいので、振る前に使って次の移動に効かせる） ----

        /// <summary>振ったが、まだ1歩も進んでいない移動（開発者の要望：振ったあと、行き先を見てからお守りを使えるように）。なければ null。</summary>
        public MoveInProgress PendingMove { get; private set; }

        /// <summary>今の移動の進む数を増減する（最低1）。振り直しても足したまま。</summary>
        void ChangePendingMove(int delta)
        {
            var move = PendingMove;
            move.foxBonus += delta;
            move.value = Math.Max(1, move.value + delta);
            move.remaining = move.value;
            move.adjust = 0; // 風などの選び直しは、もう使えない
        }

        /// <summary>今の移動のダイスを振り直す（振り直し御札）。足した数はそのまま。</summary>
        void RerollPendingMove() => RerollMoveByCharm(PendingMove);

        /// <summary>次の移動の出目に足す数（進み御札 +、止まり御札 −）。移動を始めたら0に戻る。</summary>
        public int PendingMoveBonus { get; private set; }

        /// <summary>次の移動の出目を減らす（対戦の妨害札「足止めの札」）。最低1は進む。</summary>
        public void AddMovePenalty(int amount) => PendingMoveBonus -= amount;

        /// <summary>次の移動で、出目を見てから1回振り直せる（振り直し御札）。</summary>
        public bool PendingMoveReroll { get; private set; }

        /// <summary>移動を始めるときに、使っておいた移動のお守りを出目に反映する。</summary>
        void ApplyPendingMoveCharms(MoveInProgress move)
        {
            if (PendingMoveReroll)
            {
                move.canReroll = true;
                PendingMoveReroll = false;
            }
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
                case CharmKind.WeakenEnemy: enemy.ApplyWeak(charm.amount + DebuffBonus); break;      // 細工師は +1
                case CharmKind.VulnerableEnemy: enemy.ApplyVulnerable(charm.amount + DebuffBonus); break;
                case CharmKind.PoisonEnemy: enemy.ApplyPoison(charm.amount); break;
                case CharmKind.FrailEnemy: enemy.ApplyFrail(charm.amount + DebuffBonus); break;
                case CharmKind.SapStrength: enemy.strength -= charm.amount + DebuffBonus; break; // 下がった筋力は、敵の攻撃から引く（0 より下にはならない）
                case CharmKind.Hamaya: enemy.LoseHp(Math.Min(charm.amount, enemy.hp - 1)); break; // とどめは刺せない（雷符と同じ）
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
