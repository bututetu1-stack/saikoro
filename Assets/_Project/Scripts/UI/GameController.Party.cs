using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 協力「賽の旅団」の進行（GameController の協力の部分）。
    /// 仲間と同じ盤面を、同じマスにそろって進む。毎ターン、全員が移動のダイスを振り、みんなの出目で止まれるマスから
    /// 行き先に票を入れる（同じ票数なら抽選。2026-10-16 開発者の要望で、部屋の代表の票で決めるのをやめた）。止まったマスの出来事（戦闘も）は、いまは各自で済ませ、
    /// 全員が済ませたら次のターン。HP・ポーチ・レリック・ゴールドは各自。
    /// TODO(仕様): 戦闘を仲間と一緒に戦う（PartyBattle）のは、次の段階
    /// </summary>
    public partial class GameController
    {
        IPartySession party;
        PartyState partyState;
        IOnlineParty pendingParty;       // 部屋に入って、開始を待っているあいだ
        IPartySession finishedParty;     // 倒れた・踏破したあと（始めの画面に戻るまでつないだまま）
        int partyReadySent = -1;         // 「準備よし」を送ったターン
        bool partyGated;                 // 仲間を待つために、マップの操作を止めている

        /// <summary>AI の仲間と協力の練習。</summary>
        void StartBotParty()
        {
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            // TODO(仕様): 練習の仲間の数は仮（2人）
            BeginParty(new BotPartySession(config, seed, RaceLocalName, 2));
            ShowStarterSelect(seed);
        }

        /// <summary>通信の協力の部屋に入る（code が null なら、だれかと）。</summary>
        void JoinPartyRoom(string code)
        {
            if (!OnlineParty.Available) return;
            LeavePendingOnline();
            string name = PlayerNameForOnline();
            var online = OnlineParty.Factory(code, name, "party");
            pendingParty = online;
            online.LobbyChanged += () =>
            {
                if (lobbyView != null && pendingParty == online) lobbyView.Refresh(online);
            };
            online.PartyStarted += () =>
            {
                if (pendingParty != online) return;
                pendingParty = null;
                BeginParty(online);
                Sfx.Play(SoundId.Buff);
                ShowStarterSelect(online.Seed);
            };
            lobbyView.ShowRoom(online);
        }

        void BeginParty(IPartySession session)
        {
            party = session;
            partyState = new PartyState(session.Names.Count, session.LeaderSlot);
            partyReadySent = -1;
            partyGated = false;
            session.CommandReceived += OnPartyCommand;
            session.MemberLeft += OnPartyMemberLeft;
        }

        void LeaveParty()
        {
            if (pendingParty != null) { pendingParty.Leave(); pendingParty = null; }
            if (party != null)
            {
                party.CommandReceived -= OnPartyCommand;
                party.MemberLeft -= OnPartyMemberLeft;
                party.Leave();
            }
            party = null;
            partyState = null;
            if (finishedParty != null) { finishedParty.Leave(); finishedParty = null; }
            partyBattle = null;
            partySeats = null;
            partyBattleBacklog.Clear();
            partySnapshots.Clear();
            partyBattleSetup = null;
        }

        void OnPartyCommand(PartyCommand c)
        {
            if (partyState == null) return;
            if (c.type == PartyCommandType.Emote)
            {
                ShowEmote(c, party?.Names);
                return;
            }
            partyState.Apply(c);
            OnPartyBattleCommand(c);
            if (c.type == PartyCommandType.Down && party != null && c.slot != party.LocalSlot)
            {
                StartCoroutine(RaceToast($"{party.Names[c.slot]} が旅から抜けた"));
            }
            ShowPartyOnMap();
        }

        void OnPartyMemberLeft(int slot)
        {
            if (partyState == null) return;
            partyState.MarkLeft(slot);
            if (party != null && slot < party.Names.Count) StartCoroutine(RaceToast($"{party.Names[slot]} が部屋から抜けた"));
            // 一緒に戦っている途中なら、その人は倒れた扱いにして待たない
            int seat = partySeats != null ? partySeats.IndexOf(slot) : -1;
            if (partyBattle != null && seat >= 0 && partyBattle.Members[seat].Outcome == Battle.BattleOutcome.Ongoing)
            {
                partyBattle.Members[seat].SetOutcome(Battle.BattleOutcome.Defeat);
                CheckPartyAllReady();
                if (!busy && battleView != null) RefreshBattle();
            }
            ShowPartyOnMap();
        }

        /// <summary>毎フレーム：AI の仲間を動かし、仲間がそろうまでは次の移動を止める。</summary>
        void UpdateParty()
        {
            if (party is BotPartySession bots) bots.Tick(Time.unscaledTime);
            if (finishedParty is BotPartySession done) done.Tick(Time.unscaledTime);
            if (party == null || partyState == null || run == null || map == null) return;
            if (busy || battle != null || !map.gameObject.activeInHierarchy || map.DialogOpen) return;
            bool waiting = !partyState.AllReady(run.Turn);
            if (waiting && map.Interactable && partyReadySent >= run.Turn)
            {
                partyGated = true;
                map.SetInteractable(false);
                map.SetMessage(PartyWaitingText());
            }
            else if (!waiting && partyGated)
            {
                partyGated = false;
                map.SetInteractable(true);
                Sfx.Play(SoundId.Charge);
                map.SetMessage("仲間がそろった！ 移動のダイスを選んで振ってください。");
            }
        }

        string PartyWaitingText()
        {
            var waitingFor = Enumerable.Range(0, partyState.count)
                .Where(i => !partyState.down[i] && partyState.ReadyTurn(i) < run.Turn)
                .Select(i => party.Names[i] + (string.IsNullOrEmpty(partyState.activity[i]) ? "" : $"（{partyState.activity[i]}）"));
            return "仲間を待っています……　" + string.Join("・", waitingFor);
        }

        /// <summary>マップで操作を待つところ：仲間に「準備よし」と HP を知らせる。</summary>
        void PartyReady()
        {
            if (party == null || run == null) return;
            SendPartyStatus();
            ShowPartyOnMap();
            if (partyReadySent >= run.Turn) return;
            partyReadySent = run.Turn;
            party.Send(PartyCommandType.Ready, run.Turn);
        }

        void SendPartyStatus()
        {
            if (party == null || run == null) return;
            string head = RaceActivity().Split('\n')[0];
            party.Send(PartyCommandType.Status, Mathf.Max(0, run.player.hp), run.player.maxHp, head);
        }

        /// <summary>
        /// 移動の行き先を仲間と決める：自分の出目（moving。振れなかったら null）を知らせ、全員の出目で止まれるマスから
        /// 行き先に票を入れ、決まったマスと、そこへ行く出目を返す。
        /// </summary>
        IEnumerator PartyVoteRoutine(RunState.MoveInProgress moving, System.Action<TileNode, int> onDecided)
        {
            party.Send(PartyCommandType.Rolled, moving != null ? moving.remaining : -1,
                moving != null && moving.forcedTarget != null ? moving.forcedTarget.id : -1,
                moving != null && moving.dice != null ? moving.dice.DisplayName : "");
            // 全員が振るまで待つ
            while (partyState.DecidedTile < 0 && partyState.RolledCount < partyState.AliveCount)
            {
                map.SetMessage($"仲間が振るのを待っています……（{partyState.RolledCount} / {partyState.AliveCount}）");
                yield return null;
            }
            if (partyState.DecidedTile < 0)
            {
                // みんなの出目で止まれるマスに、だれの出目で行けるかを書いて、クリックで票を入れる
                var rolled = Enumerable.Range(0, partyState.count).Select(i => partyState.rolled[i] ?? -1).ToList();
                var options = PartyRun.VoteOptions(run.Current, rolled, partyState.forcedTile);
                string Who(int i) => i == party.LocalSlot ? "あなた" : party.Names[i];
                string rolls = string.Join("・", Enumerable.Range(0, partyState.count).Where(i => !partyState.down[i])
                    .Select(i => rolled[i] < 0 ? $"{Who(i)} 休み" : $"{Who(i)} {rolled[i]}"));
                map.SetMessage($"みんなの出目：{rolls}\n行き先のマスをクリックして、票を入れてください（同じ票なら抽選で決まります）");
                TileNode vote = null;
                if (options.Count > 0)
                {
                    // マスの下の札：だれの出目で行けるか（自分は「自」、仲間は駒と同じ名前の最後の1字）
                    yield return map.ChooseBranch(options.Keys.ToList(), c => vote = c,
                        t => string.Join("・", options[t].Select(i => i == party.LocalSlot ? "自" : LastChar(party.Names[i]))));
                }
                party.Send(PartyCommandType.Vote, vote != null ? vote.id : run.Current.id);
                while (partyState.DecidedTile < 0)
                {
                    map.SetMessage($"仲間の票を待っています……（{partyState.VotedCount} / {partyState.AliveCount}）");
                    yield return null;
                }
            }
            // 同じ票なら抽選：並んだマスを順に光らせて、決まったマスで止める
            if (partyState.LotteryTiles.Count > 1)
            {
                var candidates = partyState.LotteryTiles.Select(id => run.board.tiles.FirstOrDefault(t => t.id == id)).Where(t => t != null).ToList();
                var chosenTile = run.board.tiles.FirstOrDefault(t => t.id == partyState.DecidedTile);
                if (candidates.Count > 1 && chosenTile != null) yield return map.PlayLottery(candidates, chosenTile);
            }
            int tileId = partyState.TakeDecided();
            int value = PartyRun.ValueToReach(run.Current, partyState.lastRolled, partyState.lastForced, tileId);
            var tile = run.board.tiles.FirstOrDefault(t => t.id == tileId);
            if (tile == null || value < 0)
            {
                // 届かないマス（ずれた）：その場にとどまる
                Debug.LogWarning($"[賽ノ道] 協力：行き先 {tileId} に届く出目がない");
                tile = run.Current;
                value = 0;
            }
            Sfx.Play(SoundId.Charge);
            onDecided(tile, value);
        }

        static string LastChar(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(name.Length - 1);

        /// <summary>仲間の様子（右の帯）と、仲間の駒（自分と同じマスに並べる）。</summary>
        void ShowPartyOnMap()
        {
            if (party == null || partyState == null || run == null || map == null || !map.gameObject.activeInHierarchy) return; // 戦闘中（マップを隠している）は、マップに戻ったときに描く
            map.ShowParty(party.Names, partyState, party.LocalSlot, run.Turn);
            var allies = new List<RaceProgress>();
            for (int i = 0; i < party.Names.Count; i++)
            {
                if (i == party.LocalSlot || partyState.down[i]) continue;
                allies.Add(new RaceProgress { name = party.Names[i], layer = run.LayerIndex, tileId = run.Current.id });
            }
            map.PlaceRivalMarkers(run.LayerIndex, allies);
        }

        /// <summary>倒れた・踏破した：仲間に知らせて、旅から抜ける（始めの画面に戻るまでつないだまま）。</summary>
        void FinishParty()
        {
            if (party == null) return;
            party.Send(PartyCommandType.Down);
            party.CommandReceived -= OnPartyCommand;
            party.MemberLeft -= OnPartyMemberLeft;
            finishedParty = party;
            party = null;
            partyState = null;
        }
    }
}
