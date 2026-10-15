using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 協力「賽の旅団」の戦闘：仲間と同じ敵と戦う（STS2 風。PartyBattle）。
    /// 全員の端末で同じ計算をする（ロックステップ）。戦闘の前に、各自のランの写し（RunSave）を送り合い、
    /// 代表が敵と戦闘のシードを決める。戦闘中は「振った」「割り振りを決めた」だけを送り、全員が決めたらラウンドを進める。
    /// 割り振り・狙いは自分の端末ではすぐに変わり、「決定」のときにまとめて送る（振るのは乱数が絡むので、届いてから振る）。
    /// TODO(仕様): 協力の戦闘では、振り直し・運命の糸・お守りはまだ使えない
    /// </summary>
    public partial class GameController
    {
        PartyBattle partyBattle;
        List<int> partySeats;             // PartyBattle の席の順の、仲間の番号
        int partyLocalSeat = -1;
        readonly Dictionary<int, string[]> partySnapshots = new Dictionary<int, string[]>();
        PartyCommand? partyBattleSetup;
        readonly HashSet<int> partyReadySeats = new HashSet<int>();
        readonly List<PartyCommand> partyBattleBacklog = new List<PartyCommand>();   // 先に届いた戦闘の知らせ
        readonly List<RunState> partyRuns = new List<RunState>();   // 席の順のラン（自分は本物、仲間は写しか AI の本物）
        bool partyActionPending;          // お守りなどを送って、届くのを待っている
        bool partyRollPending;            // 「振る」を送って、届くのを待っている
        bool partyBattleReadySent;              // このラウンドの「決定」を送った

        // TODO(仕様): 写しを分けて送る大きさ（RPC は 512 バイトまで）
        const int SnapshotChunkBytes = 280;

        /// <summary>協力の戦闘の知らせを受け取る（OnPartyCommand から）。</summary>
        void OnPartyBattleCommand(PartyCommand c)
        {
            switch (c.type)
            {
                case PartyCommandType.Snapshot:
                {
                    if (c.b <= 0 || c.a < 0 || c.a >= c.b) return;
                    if (!partySnapshots.TryGetValue(c.slot, out var chunks) || chunks.Length != c.b) partySnapshots[c.slot] = chunks = new string[c.b];
                    chunks[c.a] = c.text ?? "";
                    break;
                }
                case PartyCommandType.BattleSetup:
                    partyBattleSetup = c;
                    break;
                case PartyCommandType.BattleRoll:
                case PartyCommandType.BattleReady:
                case PartyCommandType.BattleAction:
                    // まだ戦闘を作っていない・前のラウンドを解決している途中に、先のラウンドの知らせが来たら、あとで使う
                    if (partyBattle == null || partyResolving || c.a > partyBattle.Round) partyBattleBacklog.Add(c);
                    else ApplyPartyBattleCommand(c);
                    break;
            }
        }

        /// <summary>戦闘の準備をそろえる人（代表。倒れていれば、残っている人で番号の若い人）。</summary>
        int PartySetupSlot => !partyState.down[partyState.leader] ? partyState.leader
            : Enumerable.Range(0, partyState.count).FirstOrDefault(i => !partyState.down[i]);

        IEnumerable<int> PartyAliveSlots => Enumerable.Range(0, partyState.count).Where(i => !partyState.down[i]);

        /// <summary>
        /// 戦闘のマスに止まったとき：自分のランの写しを送り（AI の仲間は同じ端末なので送らない）、
        /// 代表なら敵と戦闘のシードを決めて送る。全員の写しと代表の決めた敵がそろうまで待ち、敵の並びを返す。
        /// </summary>
        IEnumerator PartyBattleSetupRoutine(EnemyData myEnemy, TileNode tile, System.Action<List<EnemyData>, int, int> onReady)
        {
            if (!(party is BotPartySession)) SendPartySnapshot();
            if (party.LocalSlot == PartySetupSlot)
            {
                int seed = unchecked(run.random.Seed * 31 + run.Turn * 7919 + tile.id * 104729) & 0x7FFFFFFF;
                party.Send(PartyCommandType.BattleSetup, seed, run.LastEnemyHpPercent, myEnemy.id);
            }
            while (!PartySetupComplete())
            {
                if (party == null) yield break;
                map.SetMessage($"仲間がそろうのを待っています……（戦闘の準備）");
                yield return null;
            }
            var setup = partyBattleSetup.Value;
            var registry = new SaveRegistry(config);
            var enemies = new List<EnemyData>();
            foreach (var id in (setup.text ?? "").Split(','))
            {
                if (string.IsNullOrEmpty(id)) continue;
                var data = registry.Enemy(id);
                for (int k = 0; k < Mathf.Max(1, data.count); k++) enemies.Add(data);
            }
            if (enemies.Count == 0) enemies.Add(myEnemy);
            onReady(enemies, setup.a, setup.b);
        }

        bool PartySetupComplete()
        {
            if (party == null || partyState == null) return true;
            if (!partyBattleSetup.HasValue) return false;
            if (party is BotPartySession) return true;
            foreach (int slot in PartyAliveSlots)
            {
                if (slot == party.LocalSlot) continue;
                if (!partySnapshots.TryGetValue(slot, out var chunks) || chunks.Any(x => x == null)) return false;
            }
            return true;
        }

        void SendPartySnapshot()
        {
            string json = run.CreateBattleSnapshot();
            var chunks = SplitByBytes(json, SnapshotChunkBytes);
            for (int i = 0; i < chunks.Count; i++) party.Send(PartyCommandType.Snapshot, i, chunks.Count, chunks[i]);
        }

        static List<string> SplitByBytes(string text, int maxBytes)
        {
            var list = new List<string>();
            var sb = new System.Text.StringBuilder();
            int bytes = 0;
            foreach (char ch in text)
            {
                int b = System.Text.Encoding.UTF8.GetByteCount(new[] { ch });
                if (bytes + b > maxBytes && sb.Length > 0 && !char.IsLowSurrogate(ch))
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    bytes = 0;
                }
                sb.Append(ch);
                bytes += b;
            }
            if (sb.Length > 0 || list.Count == 0) list.Add(sb.ToString());
            return list;
        }

        /// <summary>全員の写しから協力の戦闘を作り、自分の戦闘（席）を返す。</summary>
        BattleState BuildPartyBattle(List<EnemyData> enemies, int seed, int hpPercent)
        {
            partySeats = PartyAliveSlots.ToList();
            partyLocalSeat = partySeats.IndexOf(party.LocalSlot);
            partyRuns.Clear();
            var members = new List<PartyMember>();
            foreach (int slot in partySeats)
            {
                RunState r;
                if (slot == party.LocalSlot) r = run;
                else if (party is BotPartySession bots) r = bots.RunOf(slot);
                else r = RunState.RestoreBattleSnapshot(config, string.Concat(partySnapshots[slot]));
                partyRuns.Add(r);
                members.Add(new PartyMember { player = r.player, pouch = r.pouch, effects = r.effects, run = r, rng = r.random.Battle });
            }
            partyBattle = new PartyBattle(members, enemies, seed, hpPercent);
            partySnapshots.Clear();
            partyBattleSetup = null;
            partyReadySeats.Clear();
            partyActionPending = false;
            partyRollPending = false;
            partyBattleReadySent = false;
            if (party is BotPartySession botSession) botSession.JoinBattle(partyBattle, partySeats);
            ReplayPartyBacklog();
            return partyBattle.Members[partyLocalSeat];
        }

        // ---- 戦闘中 ----

        void ApplyPartyBattleCommand(PartyCommand c)
        {
            switch (c.type)
            {
                case PartyCommandType.BattleRoll: ApplyPartyRoll(c); break;
                case PartyCommandType.BattleReady: ApplyPartyReady(c); break;
                case PartyCommandType.BattleAction: ApplyPartyAction(c); break;
            }
        }

        /// <summary>協力の戦闘で使う、お守り・振り直し・運命の糸。</summary>
        public enum PartyBattleAction { Charm = 0, Reroll = 1, Fate = 2 }

        /// <summary>お守りなどを使うと仲間に知らせる（届いてから、全員の端末で同じように使う）。</summary>
        void SendPartyBattleAction(PartyBattleAction kind, params int[] values)
        {
            if (partyBattle == null || partyBattleReadySent || partyActionPending || partyRollPending) return;
            partyActionPending = true;
            busy = true;
            battleView.SetBusy(true);
            party.Send(PartyCommandType.BattleAction, partyBattle.Round, (int)kind, string.Join(",", values));
        }

        void ApplyPartyAction(PartyCommand c)
        {
            if (partyBattle == null || partySeats == null || c.a != partyBattle.Round) return;
            int seat = partySeats.IndexOf(c.slot);
            if (seat < 0 || seat >= partyRuns.Count) return;
            var m = partyBattle.Members[seat];
            var r = partyRuns[seat];
            var v = (c.text ?? "").Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).ToArray();
            int V(int i) => i < v.Length ? v[i] : -1;
            bool local = seat == partyLocalSeat;
            if (local)
            {
                partyActionPending = false;
                busy = false;
            }
            if (m.Outcome != BattleOutcome.Ongoing)
            {
                if (local) RefreshBattle();
                return;
            }
            switch ((PartyBattleAction)c.b)
            {
                case PartyBattleAction.Charm:
                {
                    var charm = V(0) >= 0 && V(0) < r.Charms.Count ? r.Charms[V(0)] : null;
                    if (charm == null) break;
                    // 薬は、そのとき狙っていた敵に（狙いは「決定」まで各自の端末にしかないので、一緒に送る）
                    if (charm.ThrownAtEnemy && V(1) >= 0 && V(1) < partyBattle.enemies.Count && !partyBattle.enemies[V(1)].IsDead) m.SetTarget(partyBattle.enemies[V(1)]);
                    if (!r.CanUseInBattle(charm, m)) break;
                    // 分け合いの薬・結びの守りは仲間にも効く
                    int shared = partyBattle.ShareCharm(seat, charm);
                    if (local) UseBattleCharm(charm, V(1));
                    else UsePartyCharm(r, m, charm, V(1));
                    if (shared > 0 && !local && partyLocalSeat >= 0 && partyLocalSeat < partyBattle.Members.Count && partyBattle.Members[partyLocalSeat].Outcome == BattleOutcome.Ongoing)
                    {
                        Sfx.Play(SoundId.Charm);
                        battleView.SetLog(charm.kind == CharmKind.HealParty ? $"仲間の{charm.displayName}：HP が回復した。" : $"仲間の{charm.displayName}：防御 +{charm.amount}（このラウンド）。");
                    }
                    break;
                }
                case PartyBattleAction.Reroll:
                {
                    var rolled = V(0) >= 0 && V(0) < m.Rolled.Count ? m.Rolled[V(0)] : null;
                    if (rolled == null || !rolled.canReroll || rolled.rerolled) break;
                    if (local) StartCoroutine(BattleRerollRoutine(rolled, true));
                    else m.Reroll(rolled);
                    break;
                }
                case PartyBattleAction.Fate:
                {
                    var rolled = V(0) >= 0 && V(0) < m.Rolled.Count ? m.Rolled[V(0)] : null;
                    if (rolled == null || !m.CanUseFate || V(1) <= 0) break;
                    m.UseFate(rolled, Mathf.Min(V(1), m.FateMaxValue));
                    if (local) battleView.SetLog($"運命の糸：{rolled.dice.DisplayName}の出目を {rolled.value} にした。");
                    break;
                }
            }
            // 錆び賽を振り直して倒れた仲間などは待たない
            if (!CheckPartyAllReady() && !busy) RefreshBattle();
        }

        /// <summary>仲間（写し・AI）のお守りを、こちらの端末でも同じように使う（演出はしない）。</summary>
        static void UsePartyCharm(RunState r, BattleState m, CharmData charm, int param)
        {
            switch (charm.kind)
            {
                case CharmKind.RerollDie:
                    if (param >= 0 && param < m.Rolled.Count) r.UseRerollCharm(charm, m, m.Rolled[param]);
                    break;
                case CharmKind.WeakenEnemy:
                case CharmKind.VulnerableEnemy:
                case CharmKind.PoisonEnemy:
                    r.UseEnemyCharm(charm, m);
                    break;
                case CharmKind.Smoke:
                    break; // 協力の戦闘では逃げられない（CanUseInBattle で弾かれる）
                default:
                    r.UseCharm(charm);
                    break;
            }
        }

        /// <summary>「振る」：選んだダイスを仲間に知らせる（届いてから振る。乱数の順番を全員でそろえるため）。</summary>
        void PartyRoll()
        {
            if (partyRollPending || partyBattleReadySent || selected.Count == 0) return;
            var indices = selected.Select(d => battle.pouch.All.ToList().IndexOf(d)).Where(i => i >= 0);
            partyRollPending = true;
            busy = true;
            battleView.SetBusy(true);
            party.Send(PartyCommandType.BattleRoll, partyBattle.Round, 0, string.Join(",", indices));
        }

        void ApplyPartyRoll(PartyCommand c)
        {
            if (partyBattle == null || partySeats == null || c.a != partyBattle.Round) return;
            int seat = partySeats.IndexOf(c.slot);
            if (seat < 0) return;
            var m = partyBattle.Members[seat];
            var dice = (c.text ?? "").Split(',').Where(s => int.TryParse(s, out _)).Select(int.Parse)
                .Where(i => i >= 0 && i < m.pouch.All.Count).Select(i => m.pouch.All[i]).ToList();
            if (seat == partyLocalSeat)
            {
                // 自分：いつもの「振る」の演出で振る
                partyRollPending = false;
                busy = false;
                selected.Clear();
                selected.AddRange(dice);
                StartCoroutine(RollRoutine());
                return;
            }
            foreach (var die in dice)
            {
                if (m.Outcome != BattleOutcome.Ongoing || !m.CanRollMore || die.state != DiceState.Available) break;
                m.Roll(die);
            }
            // 振っただけで倒れた（錆び賽の自傷など）仲間は待たない
            if (!CheckPartyAllReady() && !busy) RefreshBattle();
        }

        /// <summary>「決定」：割り振りと狙いを仲間に知らせる。全員が決めたらラウンドが進む。</summary>
        void PartyReadyBattle()
        {
            if (partyBattleReadySent || partyRollPending || battle.Outcome != BattleOutcome.Ongoing) return;
            partyBattleReadySent = true;
            int target = battle.Target != null ? partyBattle.enemies.IndexOf(battle.Target) : -1;
            string assignments = string.Join(",", battle.Rolled.Select(r => ((int)r.assignment).ToString()));
            party.Send(PartyCommandType.BattleReady, partyBattle.Round, target, assignments);
            battleView.SetLog("仲間が決めるのを待っています……");
            RefreshBattle();
        }

        void ApplyPartyReady(PartyCommand c)
        {
            if (partyBattle == null || partySeats == null || c.a != partyBattle.Round) return;
            int seat = partySeats.IndexOf(c.slot);
            if (seat < 0) return;
            var m = partyBattle.Members[seat];
            if (seat != partyLocalSeat)
            {
                // 仲間の割り振りと狙いを、こちらの写しにも入れる
                if (c.b >= 0 && c.b < partyBattle.enemies.Count && !partyBattle.enemies[c.b].IsDead) m.SetTarget(partyBattle.enemies[c.b]);
                var parts = (c.text ?? "").Split(',');
                for (int i = 0; i < m.Rolled.Count && i < parts.Length; i++)
                {
                    if (int.TryParse(parts[i], out int a)) m.Assign(m.Rolled[i], (Assignment)a);
                }
            }
            partyReadySeats.Add(seat);
            if (!CheckPartyAllReady() && !busy) RefreshBattle();
        }

        bool partyResolving;

        /// <summary>先に届いていた戦闘の知らせのうち、いまのラウンドのものを使う（届いた順に）。</summary>
        void ReplayPartyBacklog()
        {
            if (partyBattle == null) return;
            var now = partyBattleBacklog.Where(c => c.a == partyBattle.Round).ToList();
            partyBattleBacklog.RemoveAll(c => c.a <= partyBattle.Round);
            foreach (var c in now)
            {
                if (partyBattle == null || partyResolving) { partyBattleBacklog.Add(c); continue; }
                ApplyPartyBattleCommand(c);
            }
        }

        /// <summary>戦っている全員が「決定」したら、ラウンドを進める。進めたら true。</summary>
        bool CheckPartyAllReady()
        {
            if (partyBattle == null || partyResolving || partyBattle.Outcome != BattleOutcome.Ongoing) return false;
            var active = Enumerable.Range(0, partyBattle.Members.Count).Where(i => partyBattle.Members[i].Outcome == BattleOutcome.Ongoing).ToList();
            if (active.Count == 0 || !active.All(partyReadySeats.Contains)) return false;
            partyResolving = true;
            StartCoroutine(PartyResolveRoutine());
            return true;
        }

        IEnumerator PartyResolveRoutine()
        {
            while (busy) yield return null; // 自分の「振る」の演出の途中なら、終わるまで待つ
            busy = true;
            partyResolving = false;
            partyReadySeats.Clear();
            partyBattleReadySent = false;
            var before = new RoundSnapshot
            {
                playerHp = battle.player.hp,
                enemyHp = battle.enemy.hp,
                attack = battle.AttackValue,
                playerBlock = battle.BlockValue,
            };
            bool wasActive = battle.Outcome == BattleOutcome.Ongoing;
            var results = partyBattle.Resolve();
            selected.Clear();
            (party as BotPartySession)?.Nudge(); // AI の仲間は次のラウンドを考える
            ReplayPartyBacklog(); // 解決のあいだに届いた、次のラウンドの知らせ
            var mine = partyLocalSeat >= 0 && partyLocalSeat < results.Count ? results[partyLocalSeat] : null;

            if (partyBattle.Outcome != BattleOutcome.Ongoing)
            {
                playLog.RecordBattle(run.Turn, battle);
                FlushPlayLog();
            }

            // 仲間の与えたダメージ（敵の HP の減りは、自分の演出の中で全員ぶんまとめて見える）
            string allies = string.Join("・", Enumerable.Range(0, results.Count)
                .Where(i => i != partyLocalSeat && results[i].HasValue)
                .Select(i => $"{party.Names[partySeats[i]]} {results[i].Value.dealt}"));
            if (mine.HasValue && wasActive)
            {
                battleView.SetLog(mine.Value.rolled.Count == 0 ? "パス。" : "");
                yield return battleView.PlayResolve(before, mine.Value, battle);
            }
            string log = mine.HasValue ? (mine.Value.rolled.Count == 0 ? "パス。" : "") + $"敵に {mine.Value.dealt} ダメージ、自分は {mine.Value.taken} ダメージ。" : "";
            if (allies.Length > 0) log += $"\n仲間の攻撃：{allies}";
            switch (partyBattle.Outcome)
            {
                case BattleOutcome.Victory:
                    log += $"\n{battle.enemy.data.displayName} を倒した！（{partyBattle.Round} ラウンド）";
                    break;
                case BattleOutcome.Defeat:
                    log += "\n旅団は全員倒れてしまった……";
                    break;
                default:
                    if (battle.Outcome == BattleOutcome.Defeat) log += "\n倒れてしまった……　仲間が勝てば HP 1 で起き上がる。";
                    break;
            }
            battleView.SetLog(log.Trim('\n'));
            RefreshBattle();

            switch (partyBattle.Outcome)
            {
                case BattleOutcome.Victory:
                    battleView.ShowContinue(bossBattle ? "結果へ" : "マップに戻る");
                    break;
                case BattleOutcome.Defeat:
                    battleView.ShowContinue("結果へ");
                    break;
                default:
                    if (battle.Outcome == BattleOutcome.Ongoing) yield return battleView.PlayRoundStart(battle);
                    break;
            }
            busy = false;
        }

        /// <summary>戦闘が終わったあと（勝ち負けにかかわらず）：協力の戦闘をしまう。</summary>
        void EndPartyBattle()
        {
            if (partyBattle == null) return;
            if (party is BotPartySession bots) bots.LeaveBattle(partyBattle.Outcome);
            partyBattle = null;
            partySeats = null;
            partyLocalSeat = -1;
        }

        /// <summary>仲間の様子（戦闘の上の帯）と、敵がだれを狙っているか。</summary>
        void RefreshPartyBattleView()
        {
            if (partyBattle == null || battleView == null) return;
            var allies = new List<BattleView.AllyInfo>();
            int colorIndex = 0;
            for (int seat = 0; seat < partyBattle.Members.Count; seat++)
            {
                if (seat == partyLocalSeat) continue;
                var m = partyBattle.Members[seat];
                string status = m.Outcome == BattleOutcome.Defeat ? "倒れた"
                    : partyReadySeats.Contains(seat) ? "決定"
                    : m.Rolled.Count > 0 ? "出目 " + string.Join("・", m.Rolled.Select(r => r.value))
                    : "考え中";
                allies.Add(new BattleView.AllyInfo
                {
                    name = party.Names[partySeats[seat]],
                    hp = Mathf.Max(0, m.player.hp),
                    maxHp = m.player.maxHp,
                    block = m.player.block,
                    status = status,
                    color = MapView.RivalColors[colorIndex++ % MapView.RivalColors.Length],
                });
            }
            battleView.ShowAllies(allies);
            var targets = partyBattle.enemies.Select(e => e.IsDead ? null
                : e.targetSlot == partyLocalSeat ? "あなたを狙う"
                : e.targetSlot >= 0 && e.targetSlot < partySeats.Count ? $"{party.Names[partySeats[e.targetSlot]]}を狙う" : null).ToList();
            battleView.SetEnemyTargets(targets);
            // 決定したあと・倒れているあいだは、操作できない（仲間を待つ）
            if ((partyBattleReadySent || battle.Outcome == BattleOutcome.Defeat) && partyBattle.Outcome == BattleOutcome.Ongoing) battleView.SetBusy(true);
        }
    }
}
