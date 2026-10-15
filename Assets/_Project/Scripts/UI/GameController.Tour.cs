using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using SaiNoMichi.Tour;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 国めぐり（桃鉄風）の進行。順番に1人ずつ：札を使う → ダイスを選んで振る → 止まるマスを選ぶ → マスの出来事
    /// （戦闘・町の名物とサイコロ屋・対決）。人の手番は操作を待ち、AI の手番は少し間をおいて進める。
    /// 1つの端末で交代して遊ぶ（人が何人でも）か、通信で遊ぶ（1人1台。NetParty の部屋）。
    /// 通信では、手番の人が決めたこと（札・振るダイス・止まるマス・買い物・対決・戦闘の結果）だけを送り、
    /// 全員の端末で同じ TourState を同じように進める（乱数も同じ順に使う）。戦闘は手番の人の端末だけで戦い、
    /// 終わったらその人のランの写しを送る。AI の手番は、全員の端末で同じ考え方で進める（送らない）。
    /// </summary>
    public partial class GameController
    {
        TourState tour;
        TourView tourView;
        Coroutine tourLoop;
        readonly List<DiceInstance> tourSelected = new List<DiceInstance>();
        bool tourRollRequested;
        int tourCardClicked = -1;
        BattleOutcome? tourBattleOutcome;
        int tourBossRounds;           // 大ボス戦：このラウンドまで戦う（0 なら制限なし）
        int tourBossStartHp = -1;     // 大ボス戦：始めの敵の HP（与えたダメージを数える）
        int tourBossDealt;

        // 通信の国めぐり
        IPartySession tourSession;
        IOnlineParty pendingTour;
        int tourLocalSlot = -1;
        bool tourDisconnected;
        readonly Queue<PartyCommand> tourInbox = new Queue<PartyCommand>();

        bool TourOnline => tourSession != null;

        /// <summary>この端末で操作する旅人か（1つの端末なら人はみんな。通信なら自分だけ）。</summary>
        bool IsLocal(TourPlayer p) => !p.isAI && (!TourOnline || p.index == tourLocalSlot);

        static readonly string[] TourAINames = { "AI・壱", "AI・弐", "AI・参" };

        /// <summary>国めぐりを始める（1つの端末）。humans 人が交代で遊び、ais 人の AI が加わる。</summary>
        void StartTour(TourGoal goal, int rounds, int humans, int ais)
        {
            CloseAll();
            LeaveTourSession();
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            var names = new List<string>();
            for (int i = 0; i < humans; i++)
                names.Add(humans == 1 ? (string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "あなた" : GameSettings.PlayerName.Trim()) : $"旅人{i + 1}");
            BuildTour(seed, goal, rounds, names, ais);
        }

        void BuildTour(int seed, TourGoal goal, int rounds, IReadOnlyList<string> humanNames, int ais)
        {
            var starters = RunState.StarterOptions(config, seed, 4);
            var setups = new List<TourPlayerSetup>();
            for (int i = 0; i < humanNames.Count; i++)
                setups.Add(new TourPlayerSetup { name = humanNames[i], isAI = false, starter = starters.Count > 0 ? starters[i % starters.Count] : null });
            for (int i = 0; i < ais; i++)
                setups.Add(new TourPlayerSetup { name = TourAINames[i % TourAINames.Length], isAI = true, starter = starters.Count > 0 ? starters[(humanNames.Count + i) % starters.Count] : null });
            tour = new TourState(config, config.tour, seed, goal, rounds, setups);
            Debug.Log($"[賽ノ道] 国めぐり seed={seed}　{goal}　{rounds}ターン　人 {humanNames.Count}・AI {ais}　{(TourOnline ? "通信" : "この端末")}");
            playLog = new PlayLog(PlayLog.NewRunId(), seed) { goldSource = () => tour != null ? tour.Current.Gold : 0, layerSource = () => 0 };
            run = tour.Current.run;
            tourView = TourView.Create(canvas.transform, art, tour);
            UIAnim.FadeIn(tourView);
            tourView.DieClicked += OnTourDieClicked;
            tourView.RollClicked += () => { if (!busy) tourRollRequested = true; };
            tourView.CardClicked += i => { if (!busy) tourCardClicked = i; };
            tourView.DieHovered += OnTourDieHovered;
            tourView.DieUnhovered += () => tourView.ShowReach(null);
            tourView.PouchClicked += TogglePouch;
            tourView.QuitClicked += () => { if (!tourView.DialogOpen) StartCoroutine(TourQuitRoutine()); };
            Bgm.Play(BgmScene.Map, 0);
            tourLoop = StartCoroutine(TourLoop());
        }

        void LeaveTour()
        {
            if (tourLoop != null) StopCoroutine(tourLoop);
            tourLoop = null;
            tour = null;
            DestroyView(tourView);
            tourView = null;
            tourBossRounds = 0;
            tourBossStartHp = -1;
            LeaveTourSession();
        }

        void LeaveTourSession()
        {
            if (pendingTour != null) { pendingTour.Leave(); pendingTour = null; }
            if (tourSession != null)
            {
                tourSession.CommandReceived -= OnTourCommand;
                tourSession.MemberLeft -= OnTourMemberLeft;
                tourSession.Leave();
            }
            tourSession = null;
            tourLocalSlot = -1;
            tourDisconnected = false;
            tourInbox.Clear();
        }

        IEnumerator TourQuitRoutine()
        {
            bool wasBusy = busy;
            busy = true;
            int choice = -1;
            yield return tourView.ShowDialog("やめる", "国めぐりをやめて、始めの画面に戻りますか？（続きからは遊べません）", new[] { ("やめて戻る", true), ("続ける", true) }, c => choice = c);
            busy = wasBusy;
            if (choice == 0) ShowTitle();
        }

        // ---- 通信の国めぐり：部屋 ----

        /// <summary>通信の国めぐりの部屋に入る（code が null なら、だれかと）。</summary>
        void JoinTourRoom(string code)
        {
            if (!OnlineParty.Available) return;
            LeavePendingOnline();
            LeaveTourSession();
            var online = OnlineParty.Factory(code, PlayerNameForOnline(), "tour");
            pendingTour = online;
            online.LobbyChanged += () =>
            {
                if (lobbyView != null && pendingTour == online) lobbyView.Refresh(online);
            };
            online.PartyStarted += () =>
            {
                if (pendingTour != online) return;
                pendingTour = null;
                Sfx.Play(SoundId.Buff);
                BeginOnlineTour(online);
            };
            lobbyView.ShowRoom(online);
        }

        void BeginOnlineTour(IOnlineParty online)
        {
            tourSession = online;
            tourLocalSlot = online.LocalSlot;
            tourDisconnected = false;
            tourInbox.Clear();
            online.CommandReceived += OnTourCommand;
            online.MemberLeft += OnTourMemberLeft;
            StartCoroutine(OnlineTourSetup(online));
        }

        /// <summary>代表が遊び方（勝ち方・ターン数・AI の数）を送り、全員がそれで国めぐりを作る。</summary>
        IEnumerator OnlineTourSetup(IOnlineParty online)
        {
            if (online.LocalSlot == online.LeaderSlot)
            {
                var (goal, rounds, ais) = LobbyView.TourChoice;
                int fill = Mathf.Clamp(ais, 0, OnlineParty.MaxPlayers - online.Names.Count);
                online.Send(PartyCommandType.TourSetup, (int)goal, rounds, fill.ToString());
            }
            PartyCommand setup = default;
            bool got = false;
            while (!got)
            {
                while (tourInbox.Count == 0)
                {
                    if (tourSession == null) yield break;
                    yield return null;
                }
                var c = tourInbox.Dequeue();
                if (c.type == PartyCommandType.TourSetup) { setup = c; got = true; }
            }
            CloseAll();
            int.TryParse(setup.text, out int aiCount);
            BuildTour(online.Seed, (TourGoal)setup.a, setup.b, online.Names, aiCount);
        }

        void OnTourCommand(PartyCommand c)
        {
            if (c.type == PartyCommandType.Emote)
            {
                ShowEmote(c, tourSession?.Names);
                return;
            }
            if ((int)c.type >= (int)PartyCommandType.TourSetup) tourInbox.Enqueue(c);
        }

        void OnTourMemberLeft(int slot)
        {
            // TODO(仕様): 抜けた人を AI に替えて続けるのは、全員の端末で同じ時に替えるのが難しいので、いまはそこで終わる
            tourDisconnected = true;
            if (tourView != null) tourView.SetMessage("仲間が部屋から抜けたので、国めぐりはここで終わります。");
        }

        /// <summary>自分の決めたこと：通信なら送って、届いた（全員と同じ順になった）ときに渡す。通信がなければ、そのまま渡す。</summary>
        IEnumerator TourCommit(PartyCommandType type, int a, int b, string text, System.Action<PartyCommand> done)
        {
            var c = new PartyCommand { slot = tour.TurnIndex, type = type, a = a, b = b, text = text };
            if (!TourOnline)
            {
                done(c);
                yield break;
            }
            tourSession.Send(type, a, b, text);
            yield return TourReceive(t => t == type, done);
        }

        /// <summary>手番の人の決めたことが届くのを待つ（届いた順に1つずつ）。</summary>
        IEnumerator TourReceive(System.Func<PartyCommandType, bool> accept, System.Action<PartyCommand> done)
        {
            while (true)
            {
                while (tourInbox.Count == 0)
                {
                    if (tour == null || tourDisconnected) yield break;
                    yield return null;
                }
                var c = tourInbox.Dequeue();
                if (accept(c.type)) { done(c); yield break; }
                Debug.LogWarning($"[賽ノ道] 国めぐり：思っていない知らせ {c.type}（{c.slot}）を読み飛ばした");
            }
        }

        /// <summary>次の目的地をルーレットで見せて（桃鉄風）、そこへ地図を動かす。</summary>
        IEnumerator TourDestinationRoulette()
        {
            if (tour?.Destination == null) yield break;
            yield return tourView.PlayDestinationRoulette(tour.map.towns.Select(t => t.name).ToList(), tour.Destination.name);
            tourView.FocusOn(tour.Destination.node, false);
            tourView.Refresh();
            yield return UIAnim.Wait(0.6f);
        }

        // ---- 手番の流れ ----

        IEnumerator TourLoop()
        {
            busy = true;
            yield return tourView.ShowDialog("賽の国めぐり",
                $"順番に1人ずつ、ポーチのダイスで振って進みます。町で名物を買い、目的地に一番乗りを目指そう。\n{GoalText(tour.goal)}　全 {tour.rounds} ターン。"
                + (TourOnline ? $"\nあなたは「{tour.players[tourLocalSlot].name}」。" : ""),
                new[] { ("はじめる", true) }, _ => { });
            yield return TourDestinationRoulette();
            while (tour != null && !tour.IsOver && !tourDisconnected)
            {
                var p = tour.Current;
                run = p.run; // ポーチ・戦闘の画面は、いまの手番の人のもの
                tourView.Refresh();
                tourView.FocusOn(p.node, false);
                tourView.HideTray();
                tourView.ShowCards(p, false);
                tourView.SetMessage($"{p.name}の番。");
                yield return tourView.ShowBanner($"{p.name}の番", TourView.PlayerColors[p.index % TourView.PlayerColors.Length]);
                string start = tour.BeginTurn();
                if (start != null)
                {
                    Sfx.Play(SoundId.Debuff);
                    tourView.Refresh();
                    yield return TourSay(start, 1.4f);
                }
                if (p.isAI) yield return TourAITurn();
                else yield return TourHumanTurn(IsLocal(p));
                if (tour == null) yield break;
                if (tourDisconnected) break;
                string end = tour.EndTurn();
                tourView.Refresh();
                if (end != null)
                {
                    // 決算（桃鉄風）：収入を数え上げて、資産の順に並べる。通信ではそれぞれの端末で押して進む
                    yield return tourView.PlaySettlement(tour.LastSettlement, tour.Assets, true);
                }
            }
            if (tour == null) yield break;
            if (!tourDisconnected)
            {
                // おまけの賞（いちばん○○だった人にごほうび）
                var awards = tour.GiveAwards();
                if (awards.Count > 0)
                {
                    Sfx.Play(SoundId.Victory);
                    tourView.Refresh();
                    yield return tourView.ShowBanner("おまけの賞！", new Color(1f, 0.85f, 0.3f));
                    string reward = tour.goal == TourGoal.Assets ? $"ごほうび：それぞれ +{TourState.AwardGold} G"
                        : tour.goal == TourGoal.Arrivals ? "ごほうび：それぞれ一番乗り +1 回に数える" : "（大ボス決戦では飾り）";
                    string body = string.Join("\n", awards.Select(a => $"<color=#FFD24D>{a.title}</color>　{a.player.name}　<size=80%>（{a.reason}）</size>"));
                    yield return tourView.ShowDialog("おまけの賞", $"{body}\n{reward}", new[] { ("つづける", true) }, _ => { });
                }
                if (tour.goal == TourGoal.FinalBoss) yield return TourFinalBoss();
            }
            yield return ShowTourResult();
        }

        static string GoalText(TourGoal goal) =>
            goal == TourGoal.Assets ? "勝ち方：最後に資産（ゴールド＋名物の値段）がいちばん多い人の勝ち。"
            : goal == TourGoal.Arrivals ? "勝ち方：目的地にいちばん多く一番乗りした人の勝ち。"
            : "勝ち方：最後に大ボスと戦い、いちばん多くダメージを与えた人の勝ち。";

        /// <summary>知らせを出して少し待つ（クリックで早送り）。</summary>
        IEnumerator TourSay(string message, float seconds = 1.0f)
        {
            tourView.SetMessage(message);
            yield return UIAnim.Wait(seconds);
        }

        /// <summary>
        /// 知らせ：この端末で操作している人（か、みんなに関わること）なら小窓、通信でほかの人の手番なら少しのあいだ文を出すだけ
        /// （ほかの人の手番で、みんなに押させないように）。
        /// </summary>
        IEnumerator TourNotice(string title, string body, bool everyone = false)
        {
            var p = tour.Current;
            if (!TourOnline || everyone || IsLocal(p)) yield return tourView.ShowDialog(title, body, new[] { ("つづける", true) }, _ => { });
            else yield return TourSay($"{title}：{body}", 2.2f);
        }

        // ---- 人の手番（自分の端末の人か、通信の仲間） ----

        IEnumerator TourHumanTurn(bool local)
        {
            var p = tour.Current;
            tourSelected.Clear();
            while (true)
            {
                PartyCommand act = default;
                bool got = false;
                if (local)
                {
                    int kind = -1, a = -1, b = -1;
                    string text = "";
                    yield return TourPickStart(p, (k, x, y, t) => { kind = k; a = x; b = y; text = t; });
                    if (tour == null) yield break;
                    if (kind < 0) continue; // やめた（札の小窓で「やめる」など）
                    yield return TourCommit(kind == 0 ? PartyCommandType.TourCard : PartyCommandType.TourRoll, a, b, text, c => { act = c; got = true; });
                }
                else
                {
                    tourView.ShowTray(p, false, null);
                    tourView.SetMessage($"{p.name}の番：考えています……");
                    yield return TourReceive(t => t == PartyCommandType.TourCard || t == PartyCommandType.TourRoll, c => { act = c; got = true; });
                }
                if (tour == null || !got) yield break;
                if (act.type == PartyCommandType.TourCard)
                {
                    bool done = false;
                    yield return TourApplyCard(act, local, d => done = d);
                    tourView.Refresh();
                    if (done || tour == null) yield break;
                    continue;
                }
                yield return TourApplyRoll(act, local);
                yield break;
            }
        }

        /// <summary>
        /// 自分の手番のはじめ：札を使うか、ダイスを選んで振るかを画面で選ぶ。
        /// done(種類, a, b, text)：種類 0 は札（a 札の番号・b 相手・text 飛脚の町）、1 は振る（text ダイスの番号）、−1 はやめた。
        /// </summary>
        IEnumerator TourPickStart(TourPlayer p, System.Action<int, int, int, string> done)
        {
            while (true)
            {
                bool mustSkip = !p.run.pouch.Available.Any(RunState.CanMoveWith);
                tourRollRequested = false;
                tourCardClicked = -1;
                tourView.ShowCards(p, true);
                tourView.ShowTray(p, true, tourSelected);
                if (mustSkip) tourView.ShowRollButton(true, "1歩進む");
                else tourView.ShowRollButton(tour.DiceThisTurn >= 2, $"振る（{tourSelected.Count}/2）");
                tourView.SetMessage(mustSkip ? "移動に使えるダイスがない。「1歩進む」で、1歩だけ進む（ポーチはリフレッシュ）。"
                    : tour.DiceThisTurn >= 2 ? "急行札：ダイスを2個選んで「振る」。"
                    : $"{p.name}の番：ダイスを選んで振る（札も使える）。ダイスに乗せると行けるマスが光る。");
                busy = false;
                while (!tourRollRequested && tourCardClicked < 0)
                {
                    if (tour == null) yield break;
                    yield return null;
                }
                busy = true;
                tourView.ShowReach(null);
                if (tourCardClicked >= 0)
                {
                    int index = tourCardClicked;
                    int target = -1, town = -1;
                    bool ok = false;
                    yield return TourPickCard(p, index, (t, w, yes) => { target = t; town = w; ok = yes; });
                    if (!ok) continue;
                    done(0, index, target, town >= 0 ? town.ToString() : "");
                    yield break;
                }
                if (mustSkip || tourSelected.Count >= tour.DiceThisTurn)
                {
                    var all = p.run.pouch.All.ToList();
                    string dice = mustSkip ? "" : string.Join(",", tourSelected.Select(d => all.IndexOf(d)));
                    tourSelected.Clear();
                    done(1, -1, -1, dice);
                    yield break;
                }
            }
        }

        void OnTourDieClicked(DiceInstance die)
        {
            if (busy || tour == null || !IsLocal(tour.Current)) return;
            if (die.state != DiceState.Available || !RunState.CanMoveWith(die)) return;
            if (tour.DiceThisTurn >= 2)
            {
                // 急行札：2個選んでから「振る」
                if (tourSelected.Contains(die)) tourSelected.Remove(die);
                else if (tourSelected.Count < 2) tourSelected.Add(die);
                tourView.ShowTray(tour.Current, true, tourSelected);
                tourView.ShowRollButton(true, $"振る（{tourSelected.Count}/2）");
                return;
            }
            tourSelected.Clear();
            tourSelected.Add(die);
            tourRollRequested = true;
        }

        void OnTourDieHovered(DiceInstance die)
        {
            if (busy || tour == null) return;
            var p = tour.Current;
            var nodes = new HashSet<TourNode>();
            foreach (var (value, _) in DiceRoller.Distribution(die, p.lastRolled))
            {
                int steps = Mathf.Max(1, value + tour.TailwindThisTurn - p.movePenalty);
                foreach (var n in tour.MoveOptions(steps).Keys) nodes.Add(n);
            }
            tourView.ShowReach(nodes);
        }

        /// <summary>札の相手（・飛脚の町）を画面で選ぶ。done(相手, 町, 使うか)。</summary>
        IEnumerator TourPickCard(TourPlayer p, int index, System.Action<int, int, bool> done)
        {
            if (index < 0 || index >= p.cards.Count) { done(-1, -1, false); yield break; }
            var card = p.cards[index];
            if (!tour.CanUseCard(card))
            {
                yield return TourSay($"{TourCards.Name(card)}は今は使えない。", 1.0f);
                done(-1, -1, false);
                yield break;
            }
            if (card == TourCardKind.Courier)
            {
                int choice = -1;
                yield return tourView.ShowDialog(TourCards.Name(card), "飛んでいく町を、盤面でクリックしてください。", new[] { ("町を選ぶ", true), ("やめる", true) }, c => choice = c);
                if (choice != 0) { done(-1, -1, false); yield break; }
                TourNode chosen = null;
                tourView.ToggleOverview();
                busy = false;
                yield return tourView.ChooseTile(tour.map.towns.Select(t => t.node).ToList(), n => chosen = n, n => tour.map.towns[n.town].name);
                busy = true;
                done(-1, chosen.town, true);
                yield break;
            }
            if (TourCards.NeedsTarget(card))
            {
                var others = tour.players.Where(o => o != p).ToList();
                var options = others.Select(o => ($"{o.name}（{o.Gold} G）", true)).ToList();
                options.Add(("やめる", true));
                int choice = -1;
                yield return tourView.ShowDialog(TourCards.Name(card), $"{TourCards.Description(card)}\nだれに使う？", options, c => choice = c);
                if (choice < 0 || choice >= others.Count) { done(-1, -1, false); yield break; }
                done(others[choice].index, -1, true);
                yield break;
            }
            done(-1, -1, true);
        }

        /// <summary>札を使う（全員の端末で同じように）。飛脚札で町へ飛んだら、手番はそこで終わり（done に true）。</summary>
        IEnumerator TourApplyCard(PartyCommand c, bool local, System.Action<bool> done)
        {
            var p = tour.Current;
            int index = c.a;
            if (index < 0 || index >= p.cards.Count) { done(false); yield break; }
            var card = p.cards[index];
            if (card == TourCardKind.Courier && int.TryParse(c.text, out int townIndex) && townIndex >= 0 && townIndex < tour.map.towns.Count)
            {
                var town = tour.map.towns[townIndex];
                var landing = tour.UseCourier(index, town);
                Sfx.Play(SoundId.Charm);
                tourView.Refresh();
                tourView.FocusOn(town.node, false);
                yield return TourAfterLanding(landing, local);
                done(true);
                yield break;
            }
            string msg = tour.UseCard(index, c.b);
            if (msg == null)
            {
                yield return TourSay($"{TourCards.Name(card)}は今は使えない。", 1.0f);
                done(false);
                yield break;
            }
            Sfx.Play(card == TourCardKind.Thief || card == TourCardKind.Fortune ? SoundId.Gold : TourCards.NeedsTarget(card) ? SoundId.Debuff : SoundId.Charm);
            tourView.Refresh();
            yield return TourSay(local ? msg : $"{p.name}は{TourCards.Name(card)}を使った！\n{msg}", 1.3f);
            done(false);
        }

        /// <summary>振って、止まるマスを選んで（届くのを待って）進み、マスの出来事を起こす。</summary>
        IEnumerator TourApplyRoll(PartyCommand c, bool local)
        {
            var p = tour.Current;
            var all = p.run.pouch.All.ToList();
            var dice = (c.text ?? "").Split(',').Where(s => int.TryParse(s, out _)).Select(int.Parse)
                .Where(i => i >= 0 && i < all.Count).Select(i => all[i]).ToList();
            List<(DiceInstance die, int value)> rolls = null;
            int steps = dice.Count > 0 ? tour.RollMove(dice, out rolls) : tour.SkipRoll();
            tourView.ShowTray(p, false, null);
            tourView.ShowRollButton(false);
            if (!local && rolls != null && rolls.Count > 0) tourView.SetMessage($"{p.name}は{string.Join("と", rolls.Select(r => r.die.DisplayName))}を振った。");
            if (rolls != null && rolls.Count > 0) yield return tourView.PlayRoll(rolls);
            Sfx.Play(SoundId.DiceLand);
            var options = tour.MoveOptions(steps);
            TourNode end = options.Keys.First();
            if (options.Count > 1)
            {
                // 止まるマス：自分は画面で選んで送る、仲間は届くのを待つ（1つしかなければ、だれも選ばない）
                tourView.FocusOnAll(options.Keys.Append(p.node).ToList());
                int endId = -1;
                if (local)
                {
                    tourView.SetMessage($"{steps} 歩。止まるマスをクリック（目的地を通るときは、そこで止まる）。");
                    TourNode chosen = end;
                    busy = false;
                    yield return tourView.ChooseTile(options.Keys, n => chosen = n);
                    busy = true;
                    yield return TourCommit(PartyCommandType.TourMove, chosen.id, 0, null, m => endId = m.a);
                }
                else
                {
                    tourView.SetMessage($"{p.name}：{steps} 歩。止まるマスを選んでいます……");
                    yield return TourReceive(t => t == PartyCommandType.TourMove, m => endId = m.a);
                }
                if (tour == null) yield break;
                var match = options.Keys.FirstOrDefault(n => n.id == endId);
                if (match != null) end = match;
                else Debug.LogWarning($"[賽ノ道] 国めぐり：止まるマス {endId} が行き先にない（ずれた？）");
            }
            tourView.HideRoll();
            var landing = tour.Move(options[end]);
            yield return tourView.PlayMove(p, options[end]);
            yield return TourAfterLanding(landing, local);
        }

        // ---- 止まったあと ----

        IEnumerator TourAfterLanding(TourLanding landing, bool local)
        {
            var p = tour.Current;
            tourView.Refresh();
            if (landing.arrived)
            {
                Sfx.Play(SoundId.Victory);
                yield return tourView.ShowBanner($"{p.name}、目的地に一番乗り！", new Color(1f, 0.85f, 0.3f));
                yield return TourDestinationRoulette();
            }
            if (landing.messages.Count > 0)
            {
                Sfx.Play(landing.node.kind == TourTileKind.Red ? SoundId.Damage : landing.node.kind == TourTileKind.Blue ? SoundId.Gold : SoundId.Button);
                yield return TourSay(landing.Message, 1.2f + 0.4f * landing.messages.Count);
            }
            if (landing.battle) yield return TourBattle(landing.enemy, local);
            if (tour == null || tourDisconnected) yield break;
            if (landing.town != null)
            {
                if (p.isAI) yield return TourAITown(landing);
                else yield return TourTown(landing, local);
            }
            if (tour == null || tourDisconnected) yield break;
            // 同じマスの相手に対決を挑む
            var others = tour.players.Where(o => o != p && o.node == p.node).ToList();
            if (others.Count > 0)
            {
                int target = -1;
                if (p.isAI)
                {
                    var foe = TourAI.ChooseDuel(tour, others);
                    target = foe != null ? foe.index : -1;
                }
                else if (local)
                {
                    var options = others.Select(o => ($"{o.name}に挑む（{o.Gold} G）", true)).ToList();
                    options.Add(("挑まない", true));
                    int choice = -1;
                    yield return tourView.ShowDialog("対決", $"同じマスに {string.Join("・", others.Select(o => o.name))} がいる。対決を挑む？\nおたがいにダイスを3個まで振り、合計が大きいほうが勝ち。勝つと相手のゴールドを {tour.settings.duelStealPercent}% もらえる（振ったダイスは使用済み）。", options, c => choice = c);
                    int pick = choice >= 0 && choice < others.Count ? others[choice].index : -1;
                    yield return TourCommit(PartyCommandType.TourDuel, pick, 0, null, c => target = c.a);
                }
                else
                {
                    tourView.SetMessage($"{p.name}：対決を挑むか考えています……");
                    yield return TourReceive(t => t == PartyCommandType.TourDuel, c => target = c.a);
                }
                if (target >= 0 && target < tour.players.Count && target != p.index) yield return TourDuel(tour.players[target]);
            }
            tourView.Refresh();
        }

        IEnumerator TourDuel(TourPlayer foe)
        {
            var result = tour.Duel(foe);
            // 対決は挑まれた人にも関わるので、その人の端末でも押すまで止める（ほかの人は少しのあいだ見せるだけ）
            bool involved = !TourOnline || IsLocal(result.attacker) || IsLocal(result.defender);
            yield return tourView.PlayDuel(result, involved && (IsLocal(result.attacker) || IsLocal(result.defender)));
            tourView.Refresh();
        }

        /// <summary>町：名物を買う・サイコロ屋（なければ札屋）。自分は画面で選んで送り、仲間の分は届いたものを使う。</summary>
        IEnumerator TourTown(TourLanding landing, bool local)
        {
            var town = landing.town;
            var p = tour.Current;
            var diceOffer = landing.diceOffer != null ? new List<(DiceData dice, int price)>(landing.diceOffer) : null;
            var cardOffer = landing.cardOffer != null ? new List<TourCardKind>(landing.cardOffer) : null;
            var relicBox = new[] { landing.relicOffer }; // 札屋の宝物（買ったら null）
            if (!local)
            {
                tourView.SetMessage($"{p.name}は{town.name}で買い物をしています……");
                while (true)
                {
                    PartyCommand c = default;
                    bool got = false;
                    yield return TourReceive(t => t == PartyCommandType.TourTown, x => { c = x; got = true; });
                    if (!got || tour == null) yield break;
                    if (c.a == 4) yield break;
                    string msg = TourApplyTown(town, c.a, c.b, diceOffer, cardOffer, relicBox);
                    if (msg != null)
                    {
                        Sfx.Play(SoundId.Gold);
                        tourView.Refresh();
                        yield return TourSay(msg, 1.0f);
                    }
                }
            }
            // 名物
            while (true)
            {
                var props = town.properties;
                var options = props.Select(x => (x.owner >= 0 ? $"{x.name}　{tour.players[x.owner].name}のもの" : $"{x.name}を買う　{x.price} G（決算で +{x.Income} G）", x.owner < 0 && p.Gold >= x.price)).ToList();
                options.Add(("買わない（次へ）", true));
                int choice = -1;
                string mono = town.Monopolist >= 0 ? $"　<color=#FFD24D>{tour.players[town.Monopolist].name}が独占中（収入2倍）</color>" : "";
                yield return tourView.ShowDialog($"{town.name}の名物", $"所持金 {p.Gold} G。名物は決算（{tour.settings.settlementEvery} ターンごと）に収入がある。町の名物を全部持つと収入2倍。{mono}", options, c => choice = c);
                bool buy = choice >= 0 && choice < props.Count;
                PartyCommand sent = default;
                yield return TourCommit(PartyCommandType.TourTown, buy ? 0 : 3, buy ? choice : -1, null, c => sent = c);
                if (!buy) break;
                if (TourApplyTown(town, 0, choice, diceOffer, cardOffer, relicBox) != null)
                {
                    Sfx.Play(SoundId.Gold);
                    tourView.Refresh();
                    if (town.Monopolist == p.index) yield return tourView.ShowBanner($"{town.name}を独占！", new Color(1f, 0.85f, 0.3f));
                }
            }
            // サイコロ屋・札屋
            while (true)
            {
                List<(string, bool)> options;
                string title;
                if (diceOffer != null)
                {
                    options = diceOffer.Select(o => ($"{o.dice.displayName}　{o.price} G　<size=80%>{string.Join("・", o.dice.faceValues)}</size>", p.Gold >= o.price && p.run.CanAddDice)).ToList();
                    title = $"{town.name}のサイコロ屋";
                }
                else
                {
                    options = (cardOffer ?? new List<TourCardKind>()).Select(k => ($"{TourCards.Name(k)}　{TourCards.Price(k)} G　<size=80%>{TourCards.Description(k)}</size>", p.Gold >= TourCards.Price(k) && p.cards.Count < tour.settings.maxCards)).ToList();
                    if (relicBox[0] != null) options.Add(($"宝物「{relicBox[0].displayName}」　{TourState.RelicPrice} G　<size=80%>{relicBox[0].description}</size>", p.Gold >= TourState.RelicPrice));
                    title = $"{town.name}の札屋";
                }
                int count = options.Count;
                int choice = count;
                if (count > 0)
                {
                    options.Add(("買わない", true));
                    string note = diceOffer != null ? (p.run.CanAddDice ? "" : "ポーチがいっぱいで買えない。") : $"札は {tour.settings.maxCards} 枚まで持てる。";
                    yield return tourView.ShowDialog(title, $"所持金 {p.Gold} G。{note}", options, c => choice = c);
                }
                bool buy = choice >= 0 && choice < count;
                // 札屋の最後の品は宝物（kind 5）
                bool relicPick = buy && diceOffer == null && relicBox[0] != null && choice == count - 1;
                int kind = !buy ? 4 : diceOffer != null ? 1 : relicPick ? 5 : 2;
                PartyCommand sent = default;
                yield return TourCommit(PartyCommandType.TourTown, kind, buy ? choice : -1, null, c => sent = c);
                if (!buy) break;
                if (TourApplyTown(town, kind, choice, diceOffer, cardOffer, relicBox) != null)
                {
                    Sfx.Play(SoundId.Gold);
                    tourView.Refresh();
                    tourView.ShowCards(p, false);
                }
            }
        }

        /// <summary>町での買い物を1つ使う（全員の端末で同じように）。買えたら知らせの文、買えなければ null。店の品は買ったら消す。</summary>
        string TourApplyTown(TourTown town, int kind, int index, List<(DiceData dice, int price)> diceOffer, List<TourCardKind> cardOffer, Effects.RelicData[] relicBox)
        {
            var p = tour.Current;
            switch (kind)
            {
                case 5:
                {
                    var relic = relicBox != null ? relicBox[0] : null;
                    if (relic == null || !tour.BuyRelic(relic)) return null;
                    relicBox[0] = null;
                    return $"{p.name}は宝物「{relic.displayName}」を手に入れた。";
                }
                case 0:
                    if (index >= 0 && index < town.properties.Count && tour.Buy(town.properties[index])) return $"{p.name}は名物「{town.properties[index].name}」を買った。";
                    return null;
                case 1:
                    if (diceOffer == null || index < 0 || index >= diceOffer.Count) return null;
                    var d = diceOffer[index];
                    if (!tour.BuyDice(d.dice, d.price)) return null;
                    diceOffer.RemoveAt(index);
                    return $"{p.name}は {d.dice.displayName} を買った。";
                case 2:
                    if (cardOffer == null || index < 0 || index >= cardOffer.Count) return null;
                    var k = cardOffer[index];
                    if (!tour.BuyCard(k)) return null;
                    cardOffer.RemoveAt(index);
                    return $"{p.name}は{TourCards.Name(k)}を買った。";
                default:
                    return null;
            }
        }

        // ---- AI の手番（全員の端末で同じように進む） ----

        IEnumerator TourAITurn()
        {
            var p = tour.Current;
            tourView.ShowTray(p, false, null);
            yield return UIAnim.Wait(0.4f);
            var (card, target, town) = TourAI.ChooseCard(tour);
            if (card >= 0 && town != null)
            {
                yield return TourSay($"{p.name}は{TourCards.Name(TourCardKind.Courier)}を使った！", 1.0f);
                var warp = tour.UseCourier(card, town);
                Sfx.Play(SoundId.Charm);
                tourView.Refresh();
                tourView.FocusOn(town.node, false);
                yield return TourAfterLanding(warp, false);
                yield break;
            }
            if (card >= 0)
            {
                var kind = p.cards[card];
                string msg = tour.UseCard(card, target);
                if (msg != null)
                {
                    Sfx.Play(TourCards.NeedsTarget(kind) ? SoundId.Debuff : SoundId.Charm);
                    tourView.Refresh();
                    yield return TourSay($"{p.name}は{TourCards.Name(kind)}を使った！\n{msg}", 1.4f);
                }
            }
            var dice = TourAI.ChooseDice(tour);
            List<(DiceInstance die, int value)> rolls = null;
            int steps = dice.Count > 0 ? tour.RollMove(dice, out rolls) : tour.SkipRoll();
            tourView.ShowTray(p, false, null);
            if (rolls != null && rolls.Count > 0)
            {
                tourView.SetMessage($"{p.name}は{string.Join("と", rolls.Select(r => r.die.DisplayName))}を振った。");
                yield return tourView.PlayRoll(rolls);
            }
            var options = tour.MoveOptions(steps);
            var end = TourAI.ChooseEnd(tour, options);
            yield return UIAnim.Wait(0.3f);
            tourView.HideRoll();
            var landing = tour.Move(options[end]);
            yield return tourView.PlayMove(p, options[end]);
            yield return TourAfterLanding(landing, false);
        }

        IEnumerator TourAITown(TourLanding landing)
        {
            foreach (var prop in TourAI.ChooseBuys(tour, landing.town))
            {
                if (!tour.Buy(prop)) continue;
                Sfx.Play(SoundId.Gold);
                tourView.Refresh();
                yield return TourSay($"{tour.Current.name}は名物「{prop.name}」を買った（{prop.price} G）。", 0.9f);
            }
            if (landing.diceOffer != null)
            {
                var buy = TourAI.ChooseDiceBuy(tour, landing.diceOffer, out int price);
                if (buy != null && tour.BuyDice(buy, price))
                {
                    tourView.Refresh();
                    yield return TourSay($"{tour.Current.name}は {buy.displayName} を買った。", 0.9f);
                }
            }
            if (landing.relicOffer != null && TourAI.WantsRelic(tour, landing.relicOffer) && tour.BuyRelic(landing.relicOffer))
            {
                tourView.Refresh();
                yield return TourSay($"{tour.Current.name}は宝物「{landing.relicOffer.displayName}」を手に入れた。", 0.9f);
            }
        }

        // ---- 戦闘 ----

        // TODO(仕様): 写しを分けて送る大きさ（RPC は 512 バイトまで）
        const int TourChunkBytes = 280;

        IEnumerator TourBattle(EnemyData enemy, bool local)
        {
            var p = tour.Current;
            BattleOutcome outcome;
            if (p.isAI)
            {
                outcome = TourAI.Fight(p, enemy);
                Sfx.Play(outcome == BattleOutcome.Victory ? SoundId.Victory : SoundId.Damage);
                string result = tour.FinishBattle(outcome, out _);
                tourView.Refresh();
                yield return TourSay($"{p.name}は {enemy.displayName} と戦った。{result}", 1.6f);
                yield break;
            }
            if (local)
            {
                yield return TourStartBattle(enemy, false, -1, 0);
                if (tour == null) yield break;
                outcome = tourBattleOutcome ?? BattleOutcome.Defeat;
                // 通信：戦ったあとのランの写しを送り、勝ち負けを送る（届いてから、全員の端末で戦闘の結果を使う）
                if (TourOnline) yield return TourSendRun(p, (int)outcome, 0);
            }
            else
            {
                tourView.SetMessage($"{p.name}は {enemy.displayName} と戦っています……");
                int code = -1;
                yield return TourReceiveRun(p, (o, _) => code = o);
                if (tour == null || tourDisconnected) yield break;
                outcome = (BattleOutcome)Mathf.Max(0, code);
            }
            string msg = tour.FinishBattle(outcome, out _);
            tourView.Refresh();
            tourView.FocusOn(p.node, false);
            yield return TourNotice("戦闘の結果", local ? msg : $"{p.name}は {enemy.displayName} と戦った。{msg}");
        }

        /// <summary>戦闘のあとのランの写しを分けて送り、最後に TourBattleEnd（a 勝ち負け・b 大ボスに与えたダメージ）。届くまで待つ。</summary>
        IEnumerator TourSendRun(TourPlayer p, int outcome, int damage)
        {
            string json = p.run.CurrentBattle == null ? p.run.CreateBattleSnapshot() : "";
            var chunks = SplitByBytes(json, TourChunkBytes);
            for (int i = 0; i < chunks.Count; i++) tourSession.Send(PartyCommandType.TourBattleChunk, i, chunks.Count, chunks[i]);
            tourSession.Send(PartyCommandType.TourBattleEnd, outcome, damage, null);
            // 自分の送ったものが届くまで待つ（読み捨てる）
            bool end = false;
            while (!end)
            {
                PartyCommand c = default;
                bool got = false;
                yield return TourReceive(t => t == PartyCommandType.TourBattleChunk || t == PartyCommandType.TourBattleEnd, x => { c = x; got = true; });
                if (!got) yield break;
                end = c.type == PartyCommandType.TourBattleEnd;
            }
        }

        /// <summary>仲間の戦闘のあとのランの写しを受け取って入れ替え、勝ち負けとダメージを渡す。</summary>
        IEnumerator TourReceiveRun(TourPlayer p, System.Action<int, int> done)
        {
            string[] chunks = null;
            while (true)
            {
                PartyCommand c = default;
                bool got = false;
                yield return TourReceive(t => t == PartyCommandType.TourBattleChunk || t == PartyCommandType.TourBattleEnd, x => { c = x; got = true; });
                if (!got) yield break;
                if (c.type == PartyCommandType.TourBattleChunk)
                {
                    if (chunks == null || chunks.Length != c.b) chunks = new string[Mathf.Max(1, c.b)];
                    if (c.a >= 0 && c.a < chunks.Length) chunks[c.a] = c.text ?? "";
                    continue;
                }
                if (chunks != null && chunks.All(x => x != null))
                {
                    try { p.run = RunState.RestoreBattleSnapshot(config, string.Concat(chunks)); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                done(c.a, c.b);
                yield break;
            }
        }

        /// <summary>人の旅人の戦闘（1人用と同じ戦闘の画面）。終わるまで待つ。</summary>
        IEnumerator TourStartBattle(EnemyData enemy, bool boss, int hpPercent, int roundLimit)
        {
            var p = tour.Current;
            run = p.run;
            tourBattleOutcome = null;
            tourBossRounds = roundLimit;
            var transition = BattleTransition.Create(canvas.transform, boss ? BattleTransition.Kind.Boss : BattleTransition.Kind.Normal, enemy.displayName);
            yield return transition.Close(tourView.PieceOf(p.index));
            tourView.gameObject.SetActive(false);
            busy = false;
            StartBattle(enemy, boss, boss ? RewardKind.Boss : RewardKind.Normal, null, hpPercent);
            tourBossStartHp = roundLimit > 0 ? battle.enemies.Sum(e => e.hp) : -1;
            yield return transition.Open();
            while (tourBattleOutcome == null)
            {
                if (tour == null) yield break;
                yield return null;
            }
            busy = true;
            tourBossRounds = 0;
            tourView.gameObject.SetActive(true);
            Bgm.Play(BgmScene.Map, 0);
        }

        /// <summary>戦闘の「続ける」（GameController.OnBattleContinue から）。国めぐりなら結果をしまって戻る。</summary>
        bool TourBattleContinue()
        {
            if (tour == null || battle == null) return false;
            tourBossDealt = tourBossStartHp >= 0 ? tourBossStartHp - battle.enemies.Sum(e => Mathf.Max(0, e.hp)) : 0;
            tourBossStartHp = -1;
            tourBattleOutcome = battle.Outcome;
            playLog.RecordBattle(0, battle);
            FlushPlayLog();
            battle = null;
            DestroyView(battleView);
            battleView = null;
            return true;
        }

        /// <summary>大ボス戦のラウンドの終わり：決まったラウンドを過ぎたら終わる（ResolveRoutine から）。終えたら true。</summary>
        bool TourBossRoundLimit()
        {
            if (tour == null || tourBossRounds <= 0 || battle == null || battle.Outcome != BattleOutcome.Ongoing || battle.Round <= tourBossRounds) return false;
            battle.canFleeBattle = true;
            int dealt = tourBossStartHp - battle.enemies.Sum(e => Mathf.Max(0, e.hp));
            battle.Flee();
            battleView.SetLog($"{tourBossRounds} ラウンドが終わった！　与えたダメージ {dealt}");
            battleView.ShowContinue("結果へ");
            return true;
        }

        // ---- 大ボス決戦（勝ち方：大ボス） ----

        IEnumerator TourFinalBoss()
        {
            var boss = tour.FinalBoss;
            int hpPercent = 100 * tour.settings.finalBossHpMultiplier;
            int rounds = tour.settings.finalBossRounds;
            yield return tourView.ShowDialog("大ボス決戦", $"旅の終わりに {boss.displayName} が現れた！\n1人ずつ {rounds} ラウンド戦い、いちばん多くダメージを与えた人の勝ち。（HP は全快、ダイスはすべて使用可能に戻る）",
                new[] { ("はじめる", true) }, _ => { });
            for (int i = 0; i < tour.players.Count; i++)
            {
                if (tourDisconnected) yield break;
                var p = tour.players[i];
                tour.ForceTurn(i);
                p.run.player.hp = p.run.player.maxHp;
                foreach (var d in p.run.pouch.All) d.state = DiceState.Available;
                tourView.SetMessage($"{p.name}の番：{boss.displayName}と戦う。");
                yield return tourView.ShowBanner($"{p.name}の決戦", TourView.PlayerColors[i % TourView.PlayerColors.Length]);
                if (p.isAI)
                {
                    p.bossDamage = TourAI.FightBoss(p, boss, hpPercent, rounds);
                    yield return TourSay($"{p.name}は {boss.displayName} に {p.bossDamage} ダメージを与えた！", 1.4f);
                }
                else if (IsLocal(p))
                {
                    yield return TourStartBattle(boss, true, hpPercent, rounds);
                    if (tour == null) yield break;
                    p.bossDamage = Mathf.Max(0, tourBossDealt);
                    if (TourOnline)
                    {
                        // ダメージだけを送る（最後なので、ランの写しはいらない）
                        PartyCommand sent = default;
                        yield return TourCommit(PartyCommandType.TourBattleEnd, 0, p.bossDamage, null, c => sent = c);
                    }
                    yield return tourView.ShowDialog("決戦の結果", $"{p.name}は {boss.displayName} に {p.bossDamage} ダメージを与えた！", new[] { ("つづける", true) }, _ => { });
                }
                else
                {
                    tourView.SetMessage($"{p.name}が {boss.displayName} と戦っています……");
                    int damage = 0;
                    yield return TourReceive(t => t == PartyCommandType.TourBattleEnd, c => damage = c.b);
                    p.bossDamage = Mathf.Max(0, damage);
                    yield return TourSay($"{p.name}は {boss.displayName} に {p.bossDamage} ダメージを与えた！", 1.6f);
                }
                tourView.Refresh();
            }
        }

        // ---- 結果 ----

        IEnumerator ShowTourResult()
        {
            var standings = tour.Standings();
            string Score(TourPlayer p) => tour.goal == TourGoal.Arrivals ? $"一番乗り {p.arrivals} 回（資産 {tour.Assets(p)}）"
                : tour.goal == TourGoal.FinalBoss ? $"ダメージ {p.bossDamage}（資産 {tour.Assets(p)}）"
                : $"資産 {tour.Assets(p)}（ゴールド {p.Gold}・名物 {tour.Assets(p) - p.Gold}）";
            var lines = standings.Select((p, i) => $"{i + 1}位　{p.name}　{Score(p)}");
            var winner = standings[0];
            Sfx.Play(winner.isAI ? SoundId.Button : SoundId.Victory);
            yield return tourView.ShowBanner(tourDisconnected ? "国めぐり　おしまい" : $"{winner.name}の勝ち！", new Color(1f, 0.85f, 0.3f));
            int choice = -1;
            string head = tourDisconnected ? "（仲間が抜けたので、ここまでの順位）\n" : "";
            bool canRetry = !TourOnline;
            var options = canRetry ? new[] { ("もう一度", true), ("始めの画面に戻る", true) } : new[] { ("始めの画面に戻る", true) };
            yield return tourView.ShowDialog("国めぐり　結果", head + string.Join("\n", lines), options, c => choice = c);
            var goal = tour.goal;
            int rounds = tour.rounds;
            int humans = tour.players.Count(p => !p.isAI);
            int ais = tour.players.Count(p => p.isAI);
            if (canRetry && choice == 0) StartTour(goal, rounds, humans, ais);
            else ShowTitle();
        }
    }
}
