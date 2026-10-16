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
    /// 代表が敵と戦闘のシードを決める。戦闘中は「振った」「使った」「ターン終了」だけを送り、全員がターン終了したらラウンドを進める。
    /// 割り振り・狙いは自分の端末ではすぐに変わり、「使う」のときにまとめて送る（振る・使うは、届いてから全員の端末で同じ順に行う）。
    /// 「使う」はすぐ効果が発動するので、仲間が与えた弱体が自分の攻撃に乗る（2026-10-16 開発者の要望。STS2 風）。
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
        bool partyBattleReadySent;              // このラウンドの「ターン終了」を送った
        readonly Dictionary<int, int> partyRoundDealt = new Dictionary<int, int>();   // 席ごとの、このラウンドに「使う」で与えたダメージ
        readonly Dictionary<int, int> partyRoundBlock = new Dictionary<int, int>();   // 席ごとの、このラウンドに「使う」で得た防御

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
            partyRoundDealt.Clear();
            partyRoundBlock.Clear();
            if (party is BotPartySession botSession)
            {
                botSession.HoldBattle = true; // はじめのラウンドの帯が終わってから考える（BattleIntroRoutine で外す）
                botSession.JoinBattle(partyBattle, partySeats);
            }
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

        /// <summary>協力の戦闘で使う、お守り・振り直し・運命の糸・「使う」（数：狙う敵, 使うダイスの割り振り…）。</summary>
        public enum PartyBattleAction { Charm = 0, Reroll = 1, Fate = 2, Use = 3 }

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
                case PartyBattleAction.Use:
                {
                    // 狙いと、まだ使っていないダイスの割り振り（振った順）を入れてから使う
                    if (V(0) >= 0 && V(0) < partyBattle.enemies.Count && !partyBattle.enemies[V(0)].IsDead) m.SetTarget(partyBattle.enemies[V(0)]);
                    var pending = m.Pending.ToList();
                    for (int i = 0; i < pending.Count && i + 1 < v.Length; i++)
                    {
                        if (v[i + 1] > 0) m.Assign(pending[i], (Assignment)v[i + 1]);
                    }
                    if (!m.HasPending) break;
                    int attack = m.AttackValue;
                    bool wasOngoing = partyBattle.Outcome == BattleOutcome.Ongoing;
                    var u = partyBattle.Use(seat);
                    if (u == null) break;
                    bool endedHere = wasOngoing && partyBattle.Outcome != BattleOutcome.Ongoing;
                    partyRoundDealt[seat] = (partyRoundDealt.TryGetValue(seat, out int sofar) ? sofar : 0) + u.Dealt;
                    partyRoundBlock[seat] = (partyRoundBlock.TryGetValue(seat, out int blocked) ? blocked : 0) + u.blockGained;
                    // 演出は順番に（仲間の攻撃が続けて届いても重ならないように）。HP の棒は演出の最後に合わせる
                    var fx = PartyUseFx(u, attack, local ? -1 : AllyIndexOf(seat), seat, endedHere);
                    QueueBattleFx(local ? new LocalFx { routine = fx } : fx);
                    return;
                }
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
                case CharmKind.FrailEnemy:
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

        /// <summary>「使う」：狙いとまだ使っていないダイスの割り振りを仲間に知らせる（届いてから、全員の端末で同じ順に使う）。</summary>
        void PartyUse()
        {
            if (partyBattleReadySent || partyActionPending || partyRollPending || !battle.HasPending) return;
            partyActionPending = true;
            busy = true;
            battleView.SetBusy(true);
            SendPartyUse();
        }

        void SendPartyUse()
        {
            int target = battle.Target != null ? partyBattle.enemies.IndexOf(battle.Target) : -1;
            var values = new List<int> { target };
            values.AddRange(battle.Pending.Select(r => (int)r.assignment));
            party.Send(PartyCommandType.BattleAction, partyBattle.Round, (int)PartyBattleAction.Use, string.Join(",", values));
        }

        /// <summary>左に並べた仲間の中での番号（自分の席を飛ばした順）。</summary>
        int AllyIndexOf(int seat) => seat < partyLocalSeat ? seat : seat - 1;

        readonly Queue<IEnumerator> battleFxQueue = new Queue<IEnumerator>();
        bool battleFxRunning;

        Coroutine battleFxRoutine;

        void QueueBattleFx(IEnumerator fx)
        {
            battleFxQueue.Enqueue(fx);
            if (!battleFxRunning) battleFxRoutine = StartCoroutine(RunBattleFx());
        }

        IEnumerator RunBattleFx()
        {
            battleFxRunning = true;
            while (battleFxQueue.Count > 0)
            {
                // 自分の「振る」や敵の番の演出の途中なら、終わってから（重なって見えないように）
                while (busy && battleFxQueue.Count > 0 && !(battleFxQueue.Peek() is LocalFx)) yield return null;
                if (battleFxQueue.Count == 0) break;
                var fx = battleFxQueue.Dequeue();
                var routine = fx is LocalFx local ? local.routine : fx;
                // 演出は戦闘の画面の上で動かす。画面が閉じたら演出も止まり、ここは次へ進む
                // （前は画面が閉じたあとも動かして例外で止まり、次の戦闘で演出を待ったまま進まなくなっていた）
                var view = battleView;
                if (view == null) continue;
                bool done = false;
                view.StartCoroutine(TrackFx(routine, () => done = true));
                while (!done && view != null) yield return null;
            }
            battleFxRunning = false;
            battleFxRoutine = null;
            // 演出を待っていたあいだ止めていた敵の HP を、いまの値に合わせる
            if (battle != null && battleView != null && !partyResolving && !busy) RefreshBattle();
        }

        static IEnumerator TrackFx(IEnumerator routine, System.Action onDone)
        {
            yield return routine;
            onDone();
        }

        /// <summary>演出の待ち行列を空にする（戦闘が終わったとき）。</summary>
        void ClearBattleFx()
        {
            if (battleFxRoutine != null) StopCoroutine(battleFxRoutine);
            battleFxRoutine = null;
            battleFxQueue.Clear();
            battleFxRunning = false;
        }

        /// <summary>自分の「使う」の演出（自分の操作のあとなので、busy を待たずに流す）。</summary>
        class LocalFx : IEnumerator
        {
            public IEnumerator routine;
            public object Current => null;
            public bool MoveNext() => false;
            public void Reset() { }
        }

        /// <summary>だれかが「使う」をした演出（自分なら自分の姿、仲間なら左の仲間の姿が攻撃に出る）。</summary>
        IEnumerator PartyUseFx(UseResult u, int attack, int allyIndex, int seat, bool ended)
        {
            bool mine = allyIndex < 0;
            if (mine) busy = true;
            if (ended && battle != null)
            {
                playLog.RecordBattle(run.Turn, battle);
                FlushPlayLog();
            }
            if (battleView != null && battle != null) yield return battleView.PlayUse(u, battle, attack, allyIndex);
            if (battleView == null || battle == null || partyBattle == null)
            {
                if (mine) busy = false;
                yield break;
            }
            string who = mine ? "" : (partySeats != null && seat < partySeats.Count ? party.Names[partySeats[seat]] + "：" : "仲間：");
            string log = who + UseLog(u);
            if (ended && partyBattle.Outcome == BattleOutcome.Victory)
            {
                log += $"\n{battle.enemy.data.displayName} を倒した！（{partyBattle.Round} ラウンド）";
                battleView.ShowContinue(bossBattle ? "結果へ" : "マップに戻る", BattleOutcome.Victory);
            }
            else if (ended && partyBattle.Outcome == BattleOutcome.Defeat)
            {
                log += "\n旅団は全員倒れてしまった……";
                battleView.ShowContinue("結果へ", BattleOutcome.Defeat);
            }
            else if (partyBattle.Outcome != BattleOutcome.Ongoing) { } // 前の「使う」で終わっている（勝ち負けは終わらせた「使う」の演出で見せた）
            else if (mine && battle.Outcome == BattleOutcome.Defeat) log += "\n倒れてしまった……　仲間が勝てば HP 1 で起き上がる。";
            else if (mine) log += battle.CanRollMore ? $"\nまだ {battle.MaxDicePerRound - battle.Rolled.Count} 個振れる。終わったら「ターン終了」。" : "\n「ターン終了」で敵が動く。";
            battleView.SetLog(log);
            if (mine) busy = false;
            // 自分が振っている途中などは作り直さない（転がっている出目の絵が消えてしまう。終わったときに作り直す）
            if (!CheckPartyAllReady() && !busy) RefreshBattle();
        }

        /// <summary>「ターン終了」：まだ使っていないダイスを使ってから、仲間に知らせる。全員がターン終了したら敵が動く。</summary>
        void PartyReadyBattle()
        {
            if (partyBattleReadySent || partyRollPending || partyActionPending || battle.Outcome != BattleOutcome.Ongoing) return;
            if (battle.HasPending) SendPartyUse(); // 攻撃が当たるのを全員に見せてから
            partyBattleReadySent = true;
            int target = battle.Target != null ? partyBattle.enemies.IndexOf(battle.Target) : -1;
            string assignments = string.Join(",", battle.Rolled.Select(r => ((int)r.assignment).ToString()));
            party.Send(PartyCommandType.BattleReady, partyBattle.Round, target, assignments);
            battleView.SetLog("仲間のターン終了を待っています……");
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
            while (busy || battleFxRunning) yield return null; // 自分の「振る」や、だれかの「使う」の演出の途中なら、終わるまで待つ
            busy = true;
            partyResolving = false;
            partyReadySeats.Clear();
            partyBattleReadySent = false;
            var before = new RoundSnapshot
            {
                playerHp = battle.player.hp,
                enemyHp = battle.enemy.hp,
                attack = 0,
                playerBlock = battle.player.block,
            };
            bool wasActive = battle.Outcome == BattleOutcome.Ongoing;
            var bots = party as BotPartySession;
            if (bots != null) bots.HoldBattle = true; // 敵の番とラウンドの帯を見せ終わるまで、AI の仲間は次のラウンドを考えない
            var results = partyBattle.Resolve();
            partyRoundDealt.Clear();
            partyRoundBlock.Clear();
            selected.Clear();
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
            string log = mine.HasValue ? (mine.Value.rolled.Count == 0 ? "パス。" : "") + $"このラウンド：敵に {mine.Value.dealt} ダメージ、自分は {mine.Value.taken} ダメージ。" : "";
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
                    battleView.ShowContinue(bossBattle ? "結果へ" : "マップに戻る", BattleOutcome.Victory);
                    break;
                case BattleOutcome.Defeat:
                    battleView.ShowContinue("結果へ", BattleOutcome.Defeat);
                    break;
                default:
                    if (battle.Outcome == BattleOutcome.Ongoing) yield return battleView.PlayRoundStart(battle);
                    break;
            }
            busy = false;
            ReleaseBots();
        }

        /// <summary>AI の仲間に、次のラウンドを考えさせる（演出が終わってから）。</summary>
        void ReleaseBots()
        {
            if (!(party is BotPartySession bots)) return;
            bots.HoldBattle = false;
            bots.Nudge();
        }

        /// <summary>協力の戦闘の始まり：はじめのラウンドの帯を見せてから、AI の仲間に考えさせる。</summary>
        IEnumerator PartyBattleIntroRoutine()
        {
            yield return battleView.PlayRoundStart(battle);
            ReleaseBots();
        }

        /// <summary>戦闘が終わったあと（勝ち負けにかかわらず）：協力の戦闘をしまう。</summary>
        void EndPartyBattle()
        {
            ClearBattleFx();
            if (partyBattle == null) return;
            if (party is BotPartySession bots) bots.LeaveBattle(partyBattle.Outcome);
            partyBattle = null;
            partySeats = null;
            partyLocalSeat = -1;
        }

        /// <summary>仲間の様子（戦闘の左に、仲間の姿・HP・防御・いま何をしているか）と、敵の行動の向き（全員に）。</summary>
        void RefreshPartyBattleView()
        {
            if (partyBattle == null || battleView == null) return;
            var allies = new List<BattleView.AllyInfo>();
            int colorIndex = 0;
            for (int seat = 0; seat < partyBattle.Members.Count; seat++)
            {
                if (seat == partyLocalSeat) continue;
                var m = partyBattle.Members[seat];
                // いま何をしているか：使ったダメージは逐一（「使った：8 ダメージ」）、まだ使っていない出目も出す
                int dealt = partyRoundDealt.TryGetValue(seat, out int d) ? d : 0;
                int blocked = partyRoundBlock.TryGetValue(seat, out int bl) ? bl : 0;
                bool usedAny = m.Rolled.Any(r => r.used);
                var did = new List<string>();
                if (dealt > 0) did.Add($"{dealt} ダメージ");
                if (blocked > 0) did.Add($"防御 {blocked}");
                string didText = string.Join("・", did);
                string status;
                if (m.Outcome == BattleOutcome.Defeat) status = "倒れた";
                else if (partyReadySeats.Contains(seat)) status = did.Count > 0 ? $"ターン終了（{didText}）" : "ターン終了";
                else
                {
                    var parts = new List<string>();
                    if (usedAny) parts.Add(did.Count > 0 ? $"使った：{didText}" : "使った");
                    if (m.HasPending) parts.Add("出目 " + string.Join("・", m.Pending.Select(r => r.value)));
                    status = parts.Count > 0 ? string.Join("　", parts) : "考え中";
                }
                var allyRun = seat < partyRuns.Count ? partyRuns[seat] : null;
                allies.Add(new BattleView.AllyInfo
                {
                    portrait = art != null ? (allyRun != null ? art.PlayerFor(allyRun.Traveler) : art.player) : null,
                    name = party.Names[partySeats[seat]] + (allyRun != null ? "・" + Travelers.Role(allyRun.Traveler) : ""), // 役割（攻撃役など）も名札に
                    hp = Mathf.Max(0, m.player.hp),
                    maxHp = m.player.maxHp,
                    block = m.player.block,
                    status = status,
                    color = MapView.RivalColors[colorIndex++ % MapView.RivalColors.Length],
                });
            }
            battleView.ShowAllies(allies);
            // 敵の行動は全員に届く（STS2 風）
            var targets = partyBattle.enemies.Select(e => e.IsDead ? null : "全員に").ToList();
            battleView.SetEnemyTargets(targets);
            // ターン終了したあと・倒れているあいだは、操作できない（仲間を待つ）
            if ((partyBattleReadySent || battle.Outcome == BattleOutcome.Defeat) && partyBattle.Outcome == BattleOutcome.Ongoing) battleView.SetBusy(true);
        }
    }
}
