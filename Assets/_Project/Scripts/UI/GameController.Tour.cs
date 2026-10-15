using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using SaiNoMichi.Tour;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 国めぐり（桃鉄風）の進行。順番に1人ずつ：札を使う → ダイスを選んで振る → 止まるマスを選ぶ → マスの出来事
    /// （戦闘・町の名物とサイコロ屋・対決）。人の手番は操作を待ち、AI の手番は少し間をおいて進める。
    /// 1つの端末で何人でも遊べる（人の旅人が交代で操作する）。戦闘は1人用と同じ画面を使う。
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

        /// <summary>国めぐりを始める。humans 人が交代で遊び、ais 人の AI が加わる。</summary>
        void StartTour(TourGoal goal, int rounds, int humans, int ais)
        {
            CloseAll();
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            var starters = RunState.StarterOptions(config, seed, 4);
            var setups = new List<TourPlayerSetup>();
            string[] aiNames = { "AI・壱", "AI・弐", "AI・参" };
            for (int i = 0; i < humans; i++)
            {
                string name = humans == 1 ? (string.IsNullOrWhiteSpace(GameSettings.PlayerName) ? "あなた" : GameSettings.PlayerName.Trim()) : $"旅人{i + 1}";
                setups.Add(new TourPlayerSetup { name = name, isAI = false, starter = starters.Count > 0 ? starters[i % starters.Count] : null });
            }
            for (int i = 0; i < ais; i++)
            {
                setups.Add(new TourPlayerSetup { name = aiNames[i % aiNames.Length], isAI = true, starter = starters.Count > 0 ? starters[(humans + i) % starters.Count] : null });
            }
            tour = new TourState(config, config.tour, seed, goal, rounds, setups);
            Debug.Log($"[賽ノ道] 国めぐり seed={seed}　{goal}　{rounds}ターン　人 {humans}・AI {ais}");
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

        // ---- 手番の流れ ----

        IEnumerator TourLoop()
        {
            busy = true;
            yield return tourView.ShowDialog("賽の国めぐり",
                $"順番に1人ずつ、ポーチのダイスで振って進みます。町で名物を買い、目的地に一番乗りを目指そう。\n{GoalText(tour.goal)}　全 {tour.rounds} ターン。",
                new[] { ("はじめる", true) }, _ => { });
            while (tour != null && !tour.IsOver)
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
                else yield return TourHumanTurn();
                if (tour == null) yield break;
                string end = tour.EndTurn();
                tourView.Refresh();
                if (end != null)
                {
                    Sfx.Play(SoundId.Gold);
                    yield return tourView.ShowDialog("決算", end, new[] { ("つづける", true) }, _ => { });
                }
            }
            if (tour == null) yield break;
            if (tour.goal == TourGoal.FinalBoss) yield return TourFinalBoss();
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

        // ---- 人の手番 ----

        IEnumerator TourHumanTurn()
        {
            var p = tour.Current;
            tourSelected.Clear();
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
                    bool turnDone = false;
                    yield return TourUseCard(tourCardClicked, done => turnDone = done);
                    tourView.Refresh();
                    if (turnDone || tour == null) yield break;
                    continue;
                }
                if (mustSkip || tourSelected.Count >= tour.DiceThisTurn) break;
            }

            // 振る
            List<(DiceInstance die, int value)> rolls = null;
            int steps;
            if (!p.run.pouch.Available.Any(RunState.CanMoveWith)) steps = tour.SkipRoll();
            else steps = tour.RollMove(tourSelected.ToList(), out rolls);
            tourSelected.Clear();
            tourView.ShowTray(p, false, null);
            tourView.ShowRollButton(false);
            if (rolls != null && rolls.Count > 0) yield return tourView.PlayRoll(rolls);
            Sfx.Play(SoundId.DiceLand);
            var options = tour.MoveOptions(steps);
            TourNode end = options.Keys.First();
            if (options.Count > 1)
            {
                tourView.FocusOnAll(options.Keys.Append(p.node).ToList()); // 行けるマスが見えるように
                tourView.SetMessage($"{steps} 歩。止まるマスをクリック（目的地を通るときは、そこで止まる）。");
                busy = false;
                yield return tourView.ChooseTile(options.Keys, n => end = n);
                busy = true;
            }
            tourView.HideRoll();
            var landing = tour.Move(options[end]);
            yield return tourView.PlayMove(p, options[end]);
            yield return TourAfterLanding(landing);
        }

        void OnTourDieClicked(DiceInstance die)
        {
            if (busy || tour == null || tour.Current.isAI) return;
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

        /// <summary>札を使う。飛脚札で町へ飛んだら、手番はそこで終わり（done に true）。</summary>
        IEnumerator TourUseCard(int index, System.Action<bool> done)
        {
            var p = tour.Current;
            if (index < 0 || index >= p.cards.Count) { done(false); yield break; }
            var card = p.cards[index];
            if (card == TourCardKind.Courier)
            {
                int choice = -1;
                yield return tourView.ShowDialog(TourCards.Name(card), "飛んでいく町を、盤面でクリックしてください。", new[] { ("町を選ぶ", true), ("やめる", true) }, c => choice = c);
                if (choice != 0) { done(false); yield break; }
                TourNode chosen = null;
                tourView.ToggleOverview();
                busy = false;
                yield return tourView.ChooseTile(tour.map.towns.Select(t => t.node).ToList(), n => chosen = n, n => tour.map.towns[n.town].name);
                busy = true;
                var town = tour.map.towns[chosen.town];
                var landing = tour.UseCourier(index, town);
                Sfx.Play(SoundId.Charm);
                tourView.Refresh();
                tourView.FocusOn(town.node, false);
                yield return TourAfterLanding(landing);
                done(true);
                yield break;
            }
            int target = -1;
            if (TourCards.NeedsTarget(card))
            {
                var others = tour.players.Where(o => o != p).ToList();
                var options = others.Select(o => ($"{o.name}（{o.Gold} G）", true)).ToList();
                options.Add(("やめる", true));
                int choice = -1;
                yield return tourView.ShowDialog(TourCards.Name(card), $"{TourCards.Description(card)}\nだれに使う？", options, c => choice = c);
                if (choice < 0 || choice >= others.Count) { done(false); yield break; }
                target = others[choice].index;
            }
            string msg = tour.UseCard(index, target);
            if (msg == null)
            {
                yield return TourSay($"{TourCards.Name(card)}は今は使えない。", 1.0f);
                done(false);
                yield break;
            }
            Sfx.Play(card == TourCardKind.Thief || card == TourCardKind.Fortune ? SoundId.Gold : TourCards.NeedsTarget(card) ? SoundId.Debuff : SoundId.Charm);
            tourView.Refresh();
            yield return TourSay(msg, 1.2f);
            done(false);
        }

        // ---- 止まったあと ----

        IEnumerator TourAfterLanding(TourLanding landing)
        {
            var p = tour.Current;
            tourView.Refresh();
            if (landing.arrived)
            {
                Sfx.Play(SoundId.Victory);
                yield return tourView.ShowBanner($"{p.name}、目的地に一番乗り！", new Color(1f, 0.85f, 0.3f));
            }
            if (landing.messages.Count > 0)
            {
                Sfx.Play(landing.node.kind == TourTileKind.Red ? SoundId.Damage : landing.node.kind == TourTileKind.Blue ? SoundId.Gold : SoundId.Button);
                yield return TourSay(landing.Message, 1.2f + 0.4f * landing.messages.Count);
            }
            if (landing.battle) yield return TourBattle(landing.enemy);
            if (tour == null) yield break;
            if (landing.town != null)
            {
                if (p.isAI) yield return TourAITown(landing.town);
                else yield return TourTown(landing.town);
            }
            // 同じマスの相手に対決を挑む
            var others = tour.players.Where(o => o != p && o.node == p.node).ToList();
            if (others.Count > 0)
            {
                TourPlayer foe = null;
                if (p.isAI) foe = TourAI.ChooseDuel(tour, others);
                else
                {
                    var options = others.Select(o => ($"{o.name}に挑む（{o.Gold} G）", true)).ToList();
                    options.Add(("挑まない", true));
                    int choice = -1;
                    yield return tourView.ShowDialog("対決", $"同じマスに {string.Join("・", others.Select(o => o.name))} がいる。対決を挑む？\nおたがいにダイスを3個まで振り、合計が大きいほうが勝ち。勝つと相手のゴールドを {tour.settings.duelStealPercent}% もらえる（振ったダイスは使用済み）。", options, c => choice = c);
                    if (choice >= 0 && choice < others.Count) foe = others[choice];
                }
                if (foe != null) yield return TourDuel(foe);
            }
            tourView.Refresh();
        }

        IEnumerator TourDuel(TourPlayer foe)
        {
            var result = tour.Duel(foe);
            Sfx.Play(SoundId.Charge);
            string Rolls(List<(string die, int value)> r) => r.Count == 0 ? "振れるダイスがない" : string.Join("・", r.Select(x => $"{x.die} {x.value}"));
            string body = $"{result.attacker.name}：{Rolls(result.attackerRolls)}　= <b>{result.attackerTotal}</b>\n{result.defender.name}：{Rolls(result.defenderRolls)}　= <b>{result.defenderTotal}</b>\n"
                + (result.winner == null ? "引き分け！" : $"<color=#FFD24D>{result.winner.name} の勝ち！</color>　{result.stolen} G を手に入れた。");
            Sfx.Play(result.winner == null ? SoundId.Button : SoundId.Victory);
            tourView.Refresh();
            yield return tourView.ShowDialog("対決！", body, new[] { ("つづける", true) }, _ => { });
        }

        /// <summary>町：名物を買う・サイコロ屋（なければ札屋）。</summary>
        IEnumerator TourTown(TourTown town)
        {
            var p = tour.Current;
            // 名物
            while (true)
            {
                var props = town.properties;
                var options = props.Select(x => (x.owner >= 0 ? $"{x.name}　{tour.players[x.owner].name}のもの" : $"{x.name}を買う　{x.price} G（決算で +{x.Income} G）", x.owner < 0 && p.Gold >= x.price)).ToList();
                options.Add(("買わない（次へ）", true));
                int choice = -1;
                string mono = town.Monopolist >= 0 ? $"　<color=#FFD24D>{tour.players[town.Monopolist].name}が独占中（収入2倍）</color>" : "";
                yield return tourView.ShowDialog($"{town.name}の名物", $"所持金 {p.Gold} G。名物は決算（{tour.settings.settlementEvery} ターンごと）に収入がある。町の名物を全部持つと収入2倍。{mono}", options, c => choice = c);
                if (choice < 0 || choice >= props.Count) break;
                if (tour.Buy(props[choice]))
                {
                    Sfx.Play(SoundId.Gold);
                    tourView.Refresh();
                    if (town.Monopolist == p.index) yield return tourView.ShowBanner($"{town.name}を独占！", new Color(1f, 0.85f, 0.3f));
                }
            }
            // サイコロ屋・札屋
            if (town.diceShop)
            {
                var offer = tour.DiceShopOffer();
                while (offer.Count > 0)
                {
                    var options = offer.Select(o => ($"{o.dice.displayName}　{o.price} G　<size=80%>{string.Join("・", o.dice.faceValues)}</size>", p.Gold >= o.price && p.run.CanAddDice)).ToList();
                    options.Add(("買わない", true));
                    int choice = -1;
                    yield return tourView.ShowDialog($"{town.name}のサイコロ屋", $"所持金 {p.Gold} G。" + (p.run.CanAddDice ? "" : "ポーチがいっぱいで買えない。"), options, c => choice = c);
                    if (choice < 0 || choice >= offer.Count) break;
                    if (tour.BuyDice(offer[choice].dice, offer[choice].price))
                    {
                        Sfx.Play(SoundId.Gold);
                        offer.RemoveAt(choice);
                        tourView.Refresh();
                    }
                }
            }
            else
            {
                var offer = tour.CardShopOffer();
                while (offer.Count > 0)
                {
                    var options = offer.Select(k => ($"{TourCards.Name(k)}　{TourCards.Price(k)} G　<size=80%>{TourCards.Description(k)}</size>", p.Gold >= TourCards.Price(k) && p.cards.Count < tour.settings.maxCards)).ToList();
                    options.Add(("買わない", true));
                    int choice = -1;
                    yield return tourView.ShowDialog($"{town.name}の札屋", $"所持金 {p.Gold} G。札は {tour.settings.maxCards} 枚まで持てる。", options, c => choice = c);
                    if (choice < 0 || choice >= offer.Count) break;
                    if (tour.BuyCard(offer[choice]))
                    {
                        Sfx.Play(SoundId.Gold);
                        offer.RemoveAt(choice);
                        tourView.Refresh();
                        tourView.ShowCards(p, false);
                    }
                }
            }
        }

        // ---- AI の手番 ----

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
                yield return TourAfterLanding(warp);
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
            yield return TourAfterLanding(landing);
        }

        IEnumerator TourAITown(TourTown town)
        {
            foreach (var prop in TourAI.ChooseBuys(tour, town))
            {
                if (!tour.Buy(prop)) continue;
                Sfx.Play(SoundId.Gold);
                tourView.Refresh();
                yield return TourSay($"{tour.Current.name}は名物「{prop.name}」を買った（{prop.price} G）。", 0.9f);
            }
            if (town.diceShop)
            {
                var buy = TourAI.ChooseDiceBuy(tour, tour.DiceShopOffer(), out int price);
                if (buy != null && tour.BuyDice(buy, price))
                {
                    tourView.Refresh();
                    yield return TourSay($"{tour.Current.name}は {buy.displayName} を買った。", 0.9f);
                }
            }
        }

        // ---- 戦闘 ----

        IEnumerator TourBattle(EnemyData enemy)
        {
            var p = tour.Current;
            if (p.isAI)
            {
                var outcome = TourAI.Fight(p, enemy);
                Sfx.Play(outcome == BattleOutcome.Victory ? SoundId.Victory : SoundId.Damage);
                string result = tour.FinishBattle(outcome, out _);
                tourView.Refresh();
                yield return TourSay($"{p.name}は {enemy.displayName} と戦った。{result}", 1.6f);
                yield break;
            }
            yield return TourStartBattle(enemy, false, -1, 0);
            if (tour == null) yield break;
            string msg = tour.FinishBattle(tourBattleOutcome ?? BattleOutcome.Defeat, out _);
            tourView.Refresh();
            tourView.FocusOn(p.node, false);
            yield return tourView.ShowDialog("戦闘の結果", msg, new[] { ("つづける", true) }, _ => { });
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
                var p = tour.players[i];
                p.run.player.hp = p.run.player.maxHp;
                foreach (var d in p.run.pouch.All) d.state = DiceState.Available;
                tourView.SetMessage($"{p.name}の番：{boss.displayName}と戦う。");
                yield return tourView.ShowBanner($"{p.name}の決戦", TourView.PlayerColors[i % TourView.PlayerColors.Length]);
                if (p.isAI)
                {
                    p.bossDamage = TourAI.FightBoss(p, boss, hpPercent, rounds);
                    yield return TourSay($"{p.name}は {boss.displayName} に {p.bossDamage} ダメージを与えた！", 1.4f);
                }
                else
                {
                    // 人の決戦：手番の人として戦う
                    SetTourCurrentForBoss(i);
                    yield return TourStartBattle(boss, true, hpPercent, rounds);
                    if (tour == null) yield break;
                    p.bossDamage = Mathf.Max(0, tourBossDealt);
                    yield return tourView.ShowDialog("決戦の結果", $"{p.name}は {boss.displayName} に {p.bossDamage} ダメージを与えた！", new[] { ("つづける", true) }, _ => { });
                }
                tourView.Refresh();
            }
        }

        /// <summary>大ボス決戦の順番（手番の人を i にする。ゲームの終わりなので、ターンは進めない）。</summary>
        void SetTourCurrentForBoss(int i) => tour.ForceTurn(i);

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
            yield return tourView.ShowBanner($"{winner.name}の勝ち！", new Color(1f, 0.85f, 0.3f));
            int choice = -1;
            yield return tourView.ShowDialog("国めぐり　結果", string.Join("\n", lines), new[] { ("もう一度", true), ("始めの画面に戻る", true) }, c => choice = c);
            var goal = tour.goal;
            int rounds = tour.rounds;
            int humans = tour.players.Count(p => !p.isAI);
            int ais = tour.players.Count(p => p.isAI);
            if (choice == 0) StartTour(goal, rounds, humans, ais);
            else ShowTitle();
        }
    }
}
