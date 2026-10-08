using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ゲームの進行。マップ → （戦闘）→ マップ …… → 結果 を切り替え、RunState / BattleState の結果を各画面に表示する。
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public GameConfig config;
        public Canvas canvas;
        public UIArt art;
        [Tooltip("0 なら毎回ランダムなシードで始める")]
        public int fixedSeed;

        // 演出の再生中は操作を受け付けない
        bool busy;

        // 列が変わったらファイル名を変える（古い見出しのファイルに追記しないため）
        const string PlayLogFileName = "phase1_playlog.csv";

        RunState run;
        PlayLog playLog;
        MapView map;
        BattleView battleView;
        ResultView resultView;
        StarterView starterView;
        RewardView rewardView;

        BattleState battle;
        bool bossBattle;
        // 戦闘で「振る」前に選んでいるダイス（表示上の状態）
        readonly List<DiceInstance> selected = new List<DiceInstance>();

        void Start()
        {
            Sfx.Init(art);
            ShowStarterSelect();
        }

        // ---- ラン ----

        /// <summary>スターターダイスを選ぶ画面。選んだらランを始める。</summary>
        void ShowStarterSelect()
        {
            CloseAll();
            if (config.starterChoices == null || config.starterChoices.Count == 0)
            {
                StartNewRun(null);
                return;
            }
            starterView = StarterView.Create(canvas.transform, art, config);
            starterView.Chosen += StartNewRun;
        }

        void StartNewRun(DiceData starter)
        {
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            run = new RunState(config, seed, starter);
            playLog = new PlayLog(PlayLog.NewRunId(), seed) { goldSource = () => run.Gold, layerSource = () => run.LayerIndex + 1 };
            run.Acquired += (kind, item) => playLog.RecordAcquire(run.Turn, kind, item);
            Debug.Log($"[賽ノ道] 新しいラン seed={seed}　記録: {PlayLogPath}");

            CloseAll();
            busy = false;
            CreateMap();
            run.Refreshed += _ =>
            {
                refreshCount++;
                FlashRelics(Trigger.OnRefresh);
            };

            map.Refresh(run);
            map.SetMessage($"シード {seed}　ダイスにマウスを乗せると、止まりうるマスが光ります。クリックで振って進みます。");
        }

        /// <summary>いまの層の盤面でマップ画面を作る（ランの開始と、層を移ったとき）。</summary>
        void CreateMap()
        {
            DestroyView(map);
            map = MapView.Create(canvas.transform, run.board, art, run.LayerIndex);
            map.DiceHovered += OnDiceHovered;
            map.DiceUnhovered += map.ClearReach;
            map.DiceClicked += OnMapDiceClicked;
            map.SkipTurnClicked += OnSkipTurn;
            map.MirrorValue = () => DiceRoller.MirrorValue(run.LastRolledValue);
        }

        // ---- 層を移る（仕様書 第2章） ----

        LayerIntroView layerIntro;
        BossRelicView bossRelicView;

        /// <summary>ボスを倒したあと：次の層へ。回復などを見せてから、新しい盤面のマップを出す。</summary>
        IEnumerator LayerTransitionRoutine()
        {
            busy = true;

            // ボスレリック（3つから1つ。受け取らなくてもよい）
            var offer = run.CreateBossRelicOffer();
            if (offer.Count > 0)
            {
                bool decided = false;
                bossRelicView = BossRelicView.Create(canvas.transform, art, offer);
                bossRelicView.Chosen += relic =>
                {
                    if (relic != null)
                    {
                        run.AddRelic(relic);
                        Sfx.Play(SoundId.Relic);
                    }
                    decided = true;
                };
                while (!decided) yield return null;
                DestroyView(bossRelicView);
                bossRelicView = null;
            }

            var cleared = run.Layer.displayName;
            var result = run.AdvanceLayer();
            playLog.RecordAcquire(run.Turn, "layer", $"{run.LayerIndex + 1}:{run.Layer.displayName}");
            FlushPlayLog();
            CreateMap();
            map.gameObject.SetActive(false);

            bool next = false;
            layerIntro = LayerIntroView.Create(canvas.transform, art, run.LayerIndex, run.Layer.displayName,
                $"「{cleared}」を踏破した。\nHP が {result.healed} 回復し、すべてのダイスが使えるようになった。");
            layerIntro.Continued += () => next = true;
            while (!next) yield return null;
            DestroyView(layerIntro);
            layerIntro = null;

            map.gameObject.SetActive(true);
            map.Refresh(run);
            map.SetInteractable(true);
            map.SetMessage($"第{run.LayerIndex + 1}層「{run.Layer.displayName}」。ボスを目指して進もう。");
            busy = false;
        }

        /// <summary>trigger で働くレリックのアイコンを弾ませる（いま出ている画面のバーで）。</summary>
        void FlashRelics(Trigger trigger)
        {
            var bar = battleView != null && battleView.isActiveAndEnabled ? battleView.Relics : map != null ? map.Relics : null;
            if (bar == null) return;
            bar.Refresh(run);
            foreach (var relic in run.Relics)
            {
                if (relic.effects.Exists(e => e != null && e.trigger == trigger)) bar.Flash(relic);
            }
        }

        void CloseAll()
        {
            DestroyView(map);
            DestroyView(battleView);
            DestroyView(resultView);
            DestroyView(starterView);
            DestroyView(forgeView);
            forgeView = null;
            DestroyView(replaceView);
            replaceView = null;
            DestroyView(shopView);
            shopView = null;
            DestroyView(removeView);
            removeView = null;
            DestroyView(chooseView);
            DestroyView(layerIntro);
            DestroyView(bossRelicView);
            bossRelicView = null;
            layerIntro = null;
            chooseView = null;
            DestroyView(rewardView);
            rewardView = null;
            map = null;
            battleView = null;
            resultView = null;
            starterView = null;
        }

        static void DestroyView(Component view)
        {
            if (view == null) return;
            view.gameObject.SetActive(false); // Destroy はフレームの終わりまで残るので先に隠す
            Destroy(view.gameObject);
        }

        static string PlayLogPath => System.IO.Path.Combine(Application.persistentDataPath, PlayLogFileName);

        /// <summary>溜まった記録をファイルに追記する。書けなくてもゲームは止めない。</summary>
        void FlushPlayLog()
        {
            try
            {
                playLog.AppendTo(PlayLogPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[賽ノ道] 遊んだ記録を書き込めませんでした: {e.Message}");
            }
        }

        void ShowResult(bool cleared)
        {
            playLog.RecordResult(run.Turn, cleared, run.player.hp, run.player.maxHp, run.stats);
            FlushPlayLog();
            CloseAll();
            Sfx.StopAll();
            resultView = ResultView.Create(canvas.transform, art, cleared, run);
            resultView.RetryClicked += ShowStarterSelect;
        }

        // ---- マップ ----

        void OnDiceHovered(DiceInstance die)
        {
            if (busy || run.ReachedGoal || die.state != DiceState.Available || !RunState.CanMoveWith(die)) return;
            map.ShowReach(run.ReachOf(die)); // 爆賽の振り足し・鏡賽も込み
        }

        /// <summary>移動に使えるダイスがないとき（大賽だけなど）の「1回休み」。</summary>
        void OnSkipTurn()
        {
            if (busy || !run.MustSkipTurn) return;
            StartCoroutine(SkipTurnRoutine());
        }

        IEnumerator SkipTurnRoutine()
        {
            busy = true;
            var names = string.Join("・", run.pouch.Available.Select(d => d.DisplayName));
            bool refreshed = run.SkipTurn();
            map.Refresh(run);
            if (refreshed) yield return map.PlayRefresh();
            map.SetMessage($"1回休み：{names} を使用済みにした。" + (refreshed ? "　リフレッシュ！" : ""));
            busy = false;
        }

        void OnMapDiceClicked(DiceInstance die)
        {
            if (busy || run.ReachedGoal || die.state != DiceState.Available) return;
            StartCoroutine(MoveRoutine(die));
        }

        /// <summary>移動：ルール上の結果を先に確定し、そのあと出目 → 駒の移動 → マスの効果の順に見せる。</summary>
        IEnumerator MoveRoutine(DiceInstance die)
        {
            busy = true;
            map.ClearReach();
            map.SetInteractable(false);
            map.SetMessage($"{die.DisplayName}を振った……");

            var moving = run.BeginMove(die);
            yield return map.PlayRoll(die, moving.value, die.faces[moving.faceIndex].engraving);
            map.RefreshStatus(run); // 小判などでゴールドが増えることがある

            // 刻印「風」など：出目を ±N から選び直せる。盤面を隠さないよう、画面下の帯で選ぶ
            if (moving.Adjustable)
            {
                var values = new List<int>();
                for (int v = Mathf.Max(0, moving.value - moving.adjust); v <= moving.value + moving.adjust; v++) values.Add(v);
                int chosen = moving.value;
                map.SetMessage("");
                string label = moving.adjustLabel ?? "風";
                string title = moving.adjustCharge != null
                    ? $"{label}（残り{run.ChargesOf(moving.adjustCharge)}回）：出目 {moving.value}"
                    : $"{label}：出目 {moving.value}。進む数を選ぶ";
                yield return map.ChooseValue(title, values, moving.value,
                    v => DestinationsFrom(run.Current, v), v => chosen = v);
                if (chosen != moving.value) run.AdjustMove(moving, chosen);
                map.RefreshStatus(run); // 草鞋の残り回数
            }

            // 行き先を光らせて、ひと呼吸おいてから進む
            int faceValue = die.faces[moving.faceIndex].value;
            // 早馬などで出目と進む数が違うときは、理由がわかるよう両方を見せる
            // （鏡賽・爆賽は面の値と出目がもともと違うので除く）
            bool special = die.data != null && (die.data.mirror || die.data.explodeOn > 0);
            string moveText = !special && faceValue != moving.value
                ? $"{die.DisplayName}で {faceValue} → {moving.value}"
                : $"{die.DisplayName}で {moving.value}";
            bool died = false;
            yield return WalkRoutine(moving, moveText, d => died = d);
            if (died)
            {
                busy = false;
                ShowResult(false);
                yield break;
            }
            var move = run.FinishMove(moving);
            playLog.RecordMove(run.Turn, move);
            FlushPlayLog();
            map.HideRoll();

            map.RefreshStatus(run);
            map.RefreshTray(run.pouch);
            if (move.refreshed) yield return map.PlayRefresh();

            string message = $"{die.DisplayName}で {move.value} → {MapView.TileLabel(move.to)}のマス";
            if (move.refreshed) message += "　リフレッシュ！";
            yield return LandRoutine(move, message);
        }

        /// <summary>今いるマスから steps 歩で止まるマス（分岐の先が決まっていなければ候補すべて）。</summary>
        static IEnumerable<TileNode> DestinationsFrom(TileNode from, int steps) => ReachCalculator.Compute(from, new[] { steps }).Keys;

        /// <summary>from からちょうど steps 歩（途中でボスに入ればそこ）で target に止まれるか。</summary>
        static bool CanReach(TileNode from, TileNode target, int steps) => ReachCalculator.Compute(from, new[] { steps }).ContainsKey(target);

        /// <summary>
        /// 1歩ずつ進む（ダイスの移動・韋駄天の足跡で共通）。分かれ道では進む先のマスをクリックして選ぶ。
        /// 通過マスで倒れたら onDied(true)。
        /// </summary>
        IEnumerator WalkRoutine(RunState.MoveInProgress moving, string moveText, System.Action<bool> onDied)
        {
            map.SetMessage(moveText);
            map.SetRemaining(moving.remaining);

            // 止まれるマスが複数あれば、行き先をクリックで選ぶ（開発者の判断：道が複雑でも、何度も止められないように）
            var destinations = DestinationsFrom(run.Current, moving.remaining).ToList();
            TileNode target = destinations.Count == 1 ? destinations[0] : null;
            if (destinations.Count > 1)
            {
                map.SetMessage($"{moveText}　止まるマスをクリックしてください");
                yield return map.ChooseBranch(destinations, c => target = c);
                map.SetMessage(moveText);
            }
            if (target != null) map.ShowDestinations(new[] { target });
            yield return UIAnim.Wait(target != null && destinations.Count > 1 ? 0.2f : 0.6f);

            while (!moving.Done)
            {
                map.SetRemaining(moving.remaining);
                TileNode choice = null;
                if (run.NeedsBranchChoice(moving))
                {
                    // 行き先に届く道だけが候補。1つなら自動、同じマスへ行ける道が複数あるときだけ聞く（通過マスが変わるため）
                    var ways = run.Current.next.Where(n => target == null || CanReach(n, target, moving.remaining - 1)).ToList();
                    if (ways.Count == 0) ways = run.Current.next.ToList();
                    if (ways.Count == 1)
                    {
                        choice = ways[0];
                    }
                    else
                    {
                        map.SetMessage($"どちらの道を通りますか？ 進む先のマスをクリックしてください（あと {moving.remaining} 歩）");
                        yield return map.ChooseBranch(ways, c => choice = c);
                        map.SetMessage(moveText);
                        if (target != null) map.ShowDestinations(new[] { target });
                    }
                }
                run.StepMove(moving, choice);
                yield return map.PlayHop(run.Current);

                // 通過マス（祠・関所・茶屋・賽場）は通るだけで効く。止まったときは LandRoutine で2倍
                if (!moving.Done && run.Current.type.IsPassTile())
                {
                    var pass = run.ApplyPassTile(run.Current, false);
                    map.PopupAtTile(run.Current, pass.message, PassColor(pass));
                    map.RefreshStatus(run);
                    if (pass.returnedDice.Count > 0) map.RefreshTray(run.pouch);
                    yield return UIAnim.Wait(0.35f);
                    if (run.player.IsDead)
                    {
                        map.ClearReach();
                        onDied(true);
                        yield break;
                    }
                }
            }
            map.ClearReach();
            onDied(false);
        }

        /// <summary>止まったマスの効果。終わったらマップを操作できる状態に戻す（戦闘なら戦闘画面へ）。</summary>
        IEnumerator LandRoutine(MoveResult move, string message)
        {
            // TODO(仕様): 出目0で動けなかったときは、今いるマスの効果をもう一度は起こさない
            var landedType = move.to == move.from ? (TileType?)null : move.to.type;
            switch (landedType)
            {
                case null:
                    message += "\n動けなかった。";
                    break;
                case TileType.Battle:
                case TileType.Elite:
                case TileType.Boss:
                    map.SetMessage(message + (move.to.type == TileType.Elite ? "\n強敵が現れた！" : "\n敵が現れた！"));
                    yield return UIAnim.Wait(0.5f);
                    busy = false;
                    StartBattle(run.PickEnemy(move.to), move.to.type == TileType.Boss,
                        move.to.type == TileType.Elite ? RewardKind.Elite : RewardKind.Normal);
                    yield break;
                case TileType.Rest:
                {
                    // 休憩：「休む」か「鍛える」の二択（Slay the Spire の焚き火と同じ形）。鍛えるをやめたら選び直せる
                    map.SetMessage(message);
                    bool done = false;
                    while (!done)
                    {
                        int choice = -1;
                        yield return map.ShowDialog("休憩", $"焚き火で一息つける。どちらか1つを選んでください。\n（いまの HP {run.player.hp}/{run.player.maxHp}）",
                            new[]
                            {
                                run.RestHealAmount > 0
                                    ? new MapView.DialogOption($"休む（HP +{Mathf.Min(run.RestHealAmount, run.player.maxHp - run.player.hp)}）")
                                    : new MapView.DialogOption("休む（重い王冠のせいで回復できない）", false),
                                new MapView.DialogOption("鍛える（刻印を付ける）", config.engravingPool.Count > 0),
                            }, c => choice = c);
                        if (choice == 0)
                        {
                            message += $"\n休んだ：HP を {run.Rest()} 回復した。";
                            done = true;
                        }
                        else
                        {
                            string forged = null;
                            yield return ForgeRoutine(r => forged = r);
                            if (forged != null)
                            {
                                message += "\n" + forged;
                                done = true;
                            }
                        }
                    }
                    break;
                }
                case TileType.Forge:
                {
                    map.SetMessage(message);
                    string forged = null;
                    yield return ForgeRoutine(r => forged = r);
                    message += "\n" + (forged ?? "鍛冶をせずに立ち去った。");
                    break;
                }
                case TileType.Treasure:
                {
                    var treasure = run.OpenTreasure();
                    message += "\n" + treasure.message;
                    map.RefreshStatus(run);
                    if (treasure.diceOffer != null)
                    {
                        int choice = -1;
                        yield return map.ShowDialog("宝箱", $"{treasure.message}\n奥に「{treasure.diceOffer.displayName}」も入っていた。"
                            + (run.CanAddDice ? "" : "\n（ポーチが満杯なので、持っていくなら入れ替える）"),
                            new[] { new MapView.DialogOption("持っていく"), new MapView.DialogOption("置いていく") },
                            c => choice = c);
                        if (choice == 0)
                        {
                            string got = null;
                            yield return GainDiceRoutine(treasure.diceOffer, r => got = r);
                            if (got != null) message += "\n" + got;
                        }
                    }
                    break;
                }
                case TileType.Shop:
                {
                    map.SetMessage(message);
                    string bought = null;
                    yield return ShopRoutine(r => bought = r);
                    message += "\n" + bought;
                    map.RefreshTray(run.pouch);
                    break;
                }
                case TileType.Trap:
                {
                    var trap = run.TriggerTrap();
                    Sfx.Play(SoundId.Trap);
                    message += "\n" + trap.message;
                    StartCoroutine(map.ShakeBoard());
                    map.RefreshTray(run.pouch);
                    break;
                }
                case TileType.Shrine:
                case TileType.Checkpoint:
                case TileType.Teahouse:
                case TileType.DiceHall:
                {
                    var pass = run.ApplyPassTile(move.to, true);
                    message += "\n" + pass.message + "（止まったので2倍）";
                    map.PopupAtTile(move.to, pass.message, PassColor(pass));
                    if (pass.returnedDice.Count > 0) map.RefreshTray(run.pouch);
                    break;
                }
                case TileType.Empty:
                    message += "\n何も起きなかった。";
                    break;
                case TileType.Event:
                {
                    map.SetMessage(message);
                    string result = null;
                    RunState.MoveInProgress forced = null;
                    yield return EventRoutine(r => result = r, f => forced = f);
                    if (result != null) message += "\n" + result;
                    map.RefreshStatus(run);
                    map.RefreshTray(run.pouch);
                    if (forced != null && !run.player.IsDead)
                    {
                        // 韋駄天の足跡：そのまま進み、進んだ先のマスの効果が起きる
                        bool died = false;
                        yield return WalkRoutine(forced, $"韋駄天の足跡：{forced.value} マス進む", d => died = d);
                        if (died)
                        {
                            busy = false;
                            ShowResult(false);
                            yield break;
                        }
                        var dash = run.FinishMove(forced);
                        map.HideRoll();
                        yield return LandRoutine(dash, $"{message}\n→ {MapView.TileLabel(dash.to)}のマス");
                        yield break;
                    }
                    break;
                }
                default:
                    message += "\n（このマスの中身はまだ作っていません）";
                    break;
            }
            map.RefreshStatus(run);
            map.SetMessage(message);
            // 止まったマスと、そこで起きたこと（記録用）
            if (move.to != move.from) playLog.RecordTile(run.Turn, move.to.type, message.Replace("\n", " / "));
            FlushPlayLog();

            if (run.player.IsDead)
            {
                busy = false;
                ShowResult(false);
                yield break;
            }
            map.SetInteractable(true);
            map.SetSkipTurn(run.MustSkipTurn);
            busy = false;
        }

        int refreshCount;   // リフレッシュが起きた回数（戦闘で振ったときに起きたかを知るため）

        DiceReplaceView replaceView;

        /// <summary>
        /// ダイスを手に入れる（宝箱・イベント・ショップで共通）。ポーチが満杯なら入れ替える画面を出す。
        /// 手に入れたら説明文、受け取らなかったら null を onDone に渡す。
        /// </summary>
        IEnumerator GainDiceRoutine(DiceData data, System.Action<string> onDone)
        {
            if (run.CanAddDice)
            {
                run.AddDice(data);
                map.RefreshTray(run.pouch);
                onDone($"{data.displayName} を手に入れた。");
                yield break;
            }

            bool finished = false;
            string result = null;
            replaceView = DiceReplaceView.Create(canvas.transform, art, run.pouch, data);
            replaceView.Replaced += old =>
            {
                run.ReplaceDice(old, data);
                result = $"{old.DisplayName} を手放して {data.displayName} を手に入れた。";
                finished = true;
            };
            replaceView.Cancelled += () => finished = true;
            while (!finished) yield return null;
            DestroyView(replaceView);
            replaceView = null;
            map.RefreshTray(run.pouch);
            onDone(result);
        }

        ForgeView forgeView;

        /// <summary>鍛冶の画面を開き、刻印を付けるかやめるまで待つ。付けたら説明文、やめたら null を onDone に渡す。</summary>
        IEnumerator ForgeRoutine(System.Action<string> onDone)
        {
            var offer = run.CreateForgeOffer();
            bool finished = false;
            string result = null;
            forgeView = ForgeView.Create(canvas.transform, art, offer, run.pouch, true);
            forgeView.Applied += (engraving, die, faceIndex) =>
            {
                int before = die.faces[faceIndex].value;
                run.ApplyEngraving(die, faceIndex, engraving);
                result = engraving.kind == EngravingKind.Numeric
                    ? $"{die.DisplayName} の面を「{engraving.displayName}」で {before} → {die.faces[faceIndex].value} にした。"
                    : $"{die.DisplayName} の {before} の面に「{engraving.displayName}」を刻んだ。";
                finished = true;
            };
            forgeView.Cancelled += () => finished = true;
            while (!finished) yield return null;
            DestroyView(forgeView);
            forgeView = null;
            map.RefreshTray(run.pouch);
            onDone(result);
        }

        // ---- イベント（仕様書 第9章） ----

        DiceChooseView chooseView;

        /// <summary>使用可能なダイスを1個選ばせる。やめたら null。</summary>
        IEnumerator ChooseDiceRoutine(string title, string subtitle, string verb, System.Action<DiceInstance> onDone)
        {
            DiceInstance chosen = null;
            bool finished = false;
            chooseView = DiceChooseView.Create(canvas.transform, art, run.pouch, title, subtitle, verb);
            chooseView.Chosen += d => { chosen = d; finished = true; };
            chooseView.Cancelled += () => finished = true;
            while (!finished) yield return null;
            DestroyView(chooseView);
            chooseView = null;
            onDone(chosen);
        }

        /// <summary>
        /// イベントマス。起きたことの説明を onDone に、韋駄天の足跡でさらに進むときは onForcedMove に渡す。
        /// </summary>
        IEnumerator EventRoutine(System.Action<string> onDone, System.Action<RunState.MoveInProgress> onForcedMove)
        {
            var s = config.events;
            var kind = run.PickEvent();
            int choice = -1;
            switch (kind)
            {
                case EventKind.Gamble:
                {
                    while (true)
                    {
                        choice = -1;
                        string note = run.CanGamble ? "" : run.Gold < s.gambleBet ? $"\n（{s.gambleBet} G 持っていないので賭けられない）" : "\n（使用可能なダイスがない）";
                        yield return map.ShowDialog("路地裏の賭場",
                            $"「{s.gambleBet} G 賭けて、丁か半か。ダイスを1個振って、当たれば {s.gamblePayout} G だ」\n振ったダイスは使用済みになる。{note}",
                            new[]
                            {
                                new MapView.DialogOption("丁（偶数）に賭ける", run.CanGamble),
                                new MapView.DialogOption("半（奇数）に賭ける", run.CanGamble),
                                new MapView.DialogOption("立ち去る"),
                            }, c => choice = c);
                        if (choice == 2)
                        {
                            onDone("路地裏の賭場：賭けずに立ち去った。");
                            yield break;
                        }
                        bool betEven = choice == 0;
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("路地裏の賭場", $"{(betEven ? "丁（偶数）" : "半（奇数）")}に {s.gambleBet} G。振るダイスを選んでください（0 は丁）。", "振る", d => die = d);
                        if (die == null) continue; // 選び直す

                        var r = run.Gamble(die, betEven);
                        map.RefreshStatus(run);
                        map.RefreshTray(run.pouch);
                        yield return map.PlayRoll(die, r.value, null);
                        yield return UIAnim.Wait(0.4f);
                        map.HideRoll();
                        if (r.refreshed) yield return map.PlayRefresh();
                        string parity = r.even ? "丁" : "半";
                        string text = r.win
                            ? $"路地裏の賭場：{die.DisplayName}で {r.value}（{parity}）。当たり！ {r.payout} G を得た。"
                            : $"路地裏の賭場：{die.DisplayName}で {r.value}（{parity}）。外れ……{s.gambleBet} G を失った。";
                        if (r.refreshed) text += "　リフレッシュ！";
                        onDone(text);
                        yield break;
                    }
                }

                case EventKind.FallenDice:
                {
                    yield return map.ShowDialog("落ちている賽", "道ばたにダイスが落ちている。",
                        new[]
                        {
                            new MapView.DialogOption("拾う（コモンのダイス）"),
                            new MapView.DialogOption($"よく調べる（{s.fallenUncommonPercent}%でアンコモン、外れると呪いの欠け賽）"),
                            new MapView.DialogOption("放っておく"),
                        }, c => choice = c);
                    if (choice == 2)
                    {
                        onDone("落ちている賽：放っておいた。");
                        yield break;
                    }
                    if (choice == 0)
                    {
                        var common = run.FallenDiceCommon();
                        string got = null;
                        if (common != null) yield return GainDiceRoutine(common, r => got = r);
                        onDone("落ちている賽：" + (got ?? "拾わずに置いていった。"));
                        yield break;
                    }
                    var found = run.ExamineFallenDice(out var cursed, out bool rejected);
                    if (found != null)
                    {
                        int take = -1;
                        yield return map.ShowDialog("落ちている賽", $"よく見ると「{found.displayName}」だった！（{found.description}）"
                            + (run.CanAddDice ? "" : "\n（ポーチが満杯なので、持っていくなら入れ替える）"),
                            new[] { new MapView.DialogOption("持っていく"), new MapView.DialogOption("置いていく") }, c => take = c);
                        string got = null;
                        if (take == 0) yield return GainDiceRoutine(found, r => got = r);
                        onDone("落ちている賽：" + (got ?? $"{found.displayName} を置いていった。"));
                        yield break;
                    }
                    StartCoroutine(map.ShakeBoard());
                    onDone(cursed != null ? $"落ちている賽：呪われていた！ {cursed.DisplayName} がポーチに入り込んだ。"
                        : rejected ? "落ちている賽：呪われていた！ ……が、ポーチが満杯で入り込めなかった。"
                        : "落ちている賽：ただの石ころだった。");
                    yield break;
                }

                case EventKind.OldShrine:
                {
                    var engraving = run.ShrineEngraving();
                    while (true)
                    {
                        choice = -1;
                        bool canEngrave = engraving != null && run.CanPayShrine;
                        yield return map.ShowDialog("古びた祠", engraving != null
                                ? $"苔むした祠がある。血を捧げれば、ダイスに刻印「{engraving.displayName}」（{engraving.description}）を授かれそうだ。"
                                : "苔むした祠がある。",
                            new[]
                            {
                                new MapView.DialogOption($"HP－{s.shrineHpCost} で「{engraving?.displayName}」を刻む", canEngrave),
                                new MapView.DialogOption($"お参りする（HP+{s.shrinePrayHeal}）"),
                            }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone($"古びた祠：お参りして HP を {run.ShrinePray()} 回復した。");
                            yield break;
                        }
                        // 付ける面を選ぶ（やめたら選び直し）
                        string result = null;
                        bool finished = false;
                        forgeView = ForgeView.Create(canvas.transform, art, new[] { engraving }, run.pouch, true, true);
                        forgeView.Applied += (e, die, faceIndex) =>
                        {
                            int before = die.faces[faceIndex].value;
                            run.ShrineEngrave(die, faceIndex, e);
                            result = e.kind == EngravingKind.Numeric
                                ? $"古びた祠：HP を {s.shrineHpCost} 捧げ、{die.DisplayName} の面を {before} → {die.faces[faceIndex].value} にした。"
                                : $"古びた祠：HP を {s.shrineHpCost} 捧げ、{die.DisplayName} の {before} の面に「{e.displayName}」を刻んだ。";
                            finished = true;
                        };
                        forgeView.Cancelled += () => finished = true;
                        while (!finished) yield return null;
                        DestroyView(forgeView);
                        forgeView = null;
                        if (result != null)
                        {
                            onDone(result);
                            yield break;
                        }
                    }
                }

                case EventKind.FoxWedding:
                {
                    yield return map.ShowDialog("狐の嫁入り", "晴れているのに雨が降り、狐の行列が通りかかった。",
                        new[]
                        {
                            new MapView.DialogOption($"行列についていく（次の {s.foxTurns} ターン、移動の出目+{s.foxMoveBonus}）"),
                            new MapView.DialogOption($"見送る（ご祝儀に {s.foxSeeOffGold} G）"),
                        }, c => choice = c);
                    if (choice == 0)
                    {
                        run.FollowFox();
                        onDone($"狐の嫁入り：行列についていく。次の {s.foxTurns} ターン、移動の出目+{s.foxMoveBonus}。");
                    }
                    else
                    {
                        onDone($"狐の嫁入り：行列を見送った。{run.SeeOffFox()} G を得た。");
                    }
                    yield break;
                }

                case EventKind.IdatenFootprints:
                {
                    // 行き先を盤面で見ながら選べるよう、行き先を光らせて小窓は下に出す
                    var dests = DestinationsFrom(run.Current, s.idatenSteps).ToList();
                    map.ShowDestinations(dests);
                    string where = string.Join("・", dests.Select(MapView.TileName).Distinct());
                    yield return map.ShowDialog("韋駄天の足跡", $"大きな足跡が先へ続いている。たどれば {s.idatenSteps} マス先（{where}）まで一気に行けそうだ。光っているマスが行き先。",
                        new[]
                        {
                            new MapView.DialogOption($"足跡をたどる（{s.idatenSteps} マス進み、そのマスの効果が起きる）"),
                            new MapView.DialogOption("ここに止まる"),
                        }, c => choice = c, true);
                    map.ClearReach();
                    if (choice == 0)
                    {
                        onDone("韋駄天の足跡：足跡をたどった。");
                        onForcedMove(run.BeginForcedMove(s.idatenSteps));
                    }
                    else
                    {
                        onDone("韋駄天の足跡：ここに止まった。");
                    }
                    yield break;
                }
            }
            onDone(null);
        }

        ShopView shopView;
        DiceRemoveView removeView;

        /// <summary>ショップの画面を開き、「立ち去る」まで待つ。買ったものの説明を onDone に渡す。</summary>
        IEnumerator ShopRoutine(System.Action<string> onDone)
        {
            var shop = run.CreateShop();
            var log = new List<string>();
            bool leave = false;
            bool working = false;
            ShopItem buying = null;
            bool removing = false;

            shopView = ShopView.Create(canvas.transform, art, shop, run);
            shopView.BuyClicked += item => { if (!working) buying = item; };
            shopView.RemoveClicked += () => { if (!working) removing = true; };
            shopView.LeaveClicked += () => { if (!working) leave = true; };

            while (!leave)
            {
                if (buying != null)
                {
                    working = true;
                    var item = buying;
                    string result = null;
                    // 入れ替え・面選び・削除の画面を開いている間はショップを隠す（透けて見づらいため）
                    shopView.gameObject.SetActive(item.kind == ShopItemKind.Relic);
                    yield return BuyRoutine(shop, item, r => result = r);
                    shopView.gameObject.SetActive(true);
                    if (result != null)
                    {
                        log.Add(result);
                        Sfx.Play(SoundId.Buy);
                    }
                    buying = null;
                    working = false;
                    shopView.Refresh();
                    map.RefreshStatus(run);
                }
                else if (removing)
                {
                    working = true;
                    bool finished = false;
                    int price = shop.RemovePrice;
                    shopView.gameObject.SetActive(false);
                    removeView = DiceRemoveView.Create(canvas.transform, art, run.pouch, $"{price} G で、ダイスを1個ポーチから取り除きます（呪いのダイスも）。");
                    removeView.Removed += die =>
                    {
                        shop.RemoveDice(die);
                        Sfx.Play(SoundId.Buy);
                        log.Add($"{die.DisplayName} を削除した（{price} G）。");
                        finished = true;
                    };
                    removeView.Cancelled += () => finished = true;
                    while (!finished) yield return null;
                    DestroyView(removeView);
                    removeView = null;
                    shopView.gameObject.SetActive(true);
                    removing = false;
                    working = false;
                    shopView.Refresh();
                    map.RefreshStatus(run);
                }
                yield return null;
            }

            DestroyView(shopView);
            shopView = null;
            onDone(log.Count > 0 ? string.Join("\n", log) : "何も買わずに店を出た。");
        }

        static string AutoEngravingNote(Shop shop) =>
            shop.LastAutoEngraving != null ? $"縛りの腕輪で「{shop.LastAutoEngraving.displayName}」が刻まれた。" : "";

        /// <summary>品物を1つ買う。ダイスはポーチが満杯なら入れ替え、刻印は付ける面を選ぶ。やめたら null。</summary>
        IEnumerator BuyRoutine(Shop shop, ShopItem item, System.Action<string> onDone)
        {
            switch (item.kind)
            {
                case ShopItemKind.Relic:
                    shop.BuyRelic(item);
                    onDone($"レリック「{item.relic.displayName}」を買った（{item.price} G）。");
                    yield break;

                case ShopItemKind.Dice:
                    if (run.CanAddDice)
                    {
                        shop.BuyDice(item);
                        onDone($"{item.dice.displayName} を買った（{item.price} G）。" + AutoEngravingNote(shop));
                        yield break;
                    }
                    else
                    {
                        bool finished = false;
                        string result = null;
                        replaceView = DiceReplaceView.Create(canvas.transform, art, run.pouch, item.dice);
                        replaceView.Replaced += old =>
                        {
                            shop.BuyDice(item, old);
                            result = $"{old.DisplayName} を手放して {item.dice.displayName} を買った（{item.price} G）。" + AutoEngravingNote(shop);
                            finished = true;
                        };
                        replaceView.Cancelled += () => finished = true;
                        while (!finished) yield return null;
                        DestroyView(replaceView);
                        replaceView = null;
                        onDone(result);
                        yield break;
                    }

                case ShopItemKind.Engraving:
                {
                    bool finished = false;
                    string result = null;
                    forgeView = ForgeView.Create(canvas.transform, art, new[] { item.engraving }, run.pouch, true, true);
                    forgeView.Applied += (engraving, die, faceIndex) =>
                    {
                        int before = die.faces[faceIndex].value;
                        shop.BuyEngraving(item, die, faceIndex);
                        result = engraving.kind == EngravingKind.Numeric
                            ? $"刻印「{engraving.displayName}」を買って、{die.DisplayName} の面を {before} → {die.faces[faceIndex].value} にした（{item.price} G）。"
                            : $"刻印「{engraving.displayName}」を買って、{die.DisplayName} の {before} の面に刻んだ（{item.price} G）。";
                        finished = true;
                    };
                    forgeView.Cancelled += () => finished = true;
                    while (!finished) yield return null;
                    DestroyView(forgeView);
                    forgeView = null;
                    onDone(result);
                    yield break;
                }
            }
        }

        static Color PassColor(PassTileResult pass)
        {
            if (pass.damage > 0) return new Color(1f, 0.45f, 0.35f);
            if (pass.gold < 0) return new Color(0.85f, 0.85f, 0.85f);
            if (pass.healed > 0) return new Color(0.55f, 1f, 0.6f);
            return new Color(1f, 0.85f, 0.35f);
        }

        // ---- 戦闘 ----

        RewardKind battleRewardKind;

        void StartBattle(EnemyData enemy, bool isBoss, RewardKind rewardKind = RewardKind.Normal)
        {
            battle = new BattleState(run.player, enemy, run.pouch, run.random.Battle, run.effects, run, run.LastEnemyHpPercent);
            bossBattle = isBoss;
            battleRewardKind = rewardKind;
            selected.Clear();

            Sfx.StopAll(); // 足音などが戦闘画面まで残らないように
            map.gameObject.SetActive(false);
            battleView = BattleView.Create(canvas.transform, art, battle.enemies.Select(e => e.data).ToList(), isBoss, run.LayerIndex);
            battleView.DieClicked += OnBattleDieClicked;
            battleView.DieDropped += OnBattleDieDropped;
            battleView.EnemyClicked += OnEnemyClicked;
            battleView.RollClicked += OnRollClicked;
            battleView.AssignClicked += OnAssignClicked;
            battleView.ResolveClicked += OnResolveClicked;
            battleView.ContinueClicked += OnBattleContinue;

            string intro = $"{enemy.displayName} が現れた！\nダイスを選んで「振る」、出目を攻撃か防御に割り振って「決定」。";
            if (run.player.block > 0) intro = $"{enemy.displayName} が現れた！（防御 {run.player.block} で始まる）\nダイスを選んで「振る」、出目を攻撃か防御に割り振って「決定」。";
            battleView.SetLog(intro);
            RefreshBattle();
            FlashRelics(Trigger.OnBattleStart);
            StartCoroutine(battleView.PlayRoundStart(battle));
        }

        void RefreshBattle(int hiddenRolled = 0)
        {
            // 使用済みになった・数が合わない選択は外す
            selected.RemoveAll(d => d.state != DiceState.Available);
            int slots = battle.MaxDicePerRound - battle.Rolled.Count;
            if (selected.Count > slots) selected.RemoveRange(slots, selected.Count - slots);

            battleView.Refresh(battle, selected, hiddenRolled);
            battleView.Relics.Refresh(run);
        }

        void OnBattleDieClicked(DiceInstance die)
        {
            if (busy || battle.Outcome != BattleOutcome.Ongoing || die.state != DiceState.Available) return;
            if (selected.Contains(die))
            {
                selected.Remove(die);
            }
            else if (selected.Count < battle.MaxDicePerRound - battle.Rolled.Count)
            {
                selected.Add(die);
            }
            RefreshBattle();
        }

        /// <summary>敵をクリック：その敵を狙う（敵が2体以上のとき）。</summary>
        void OnEnemyClicked(int index)
        {
            if (busy || battle == null || battle.Outcome != BattleOutcome.Ongoing || index < 0 || index >= battle.enemies.Count) return;
            battle.SetTarget(battle.enemies[index]);
            RefreshBattle();
        }

        /// <summary>札を振る場所へドラッグして離した：そのダイスを選んで（選択中のダイスと一緒に）振る。</summary>
        void OnBattleDieDropped(DiceInstance die)
        {
            if (busy || battle.Outcome != BattleOutcome.Ongoing || die.state != DiceState.Available || !battle.CanRollMore) return;
            if (!selected.Contains(die))
            {
                int slots = battle.MaxDicePerRound - battle.Rolled.Count;
                // 枠がいっぱいなら、先に選んでいたものを外して、落としたダイスを優先する
                while (selected.Count >= slots && selected.Count > 0) selected.RemoveAt(0);
                selected.Add(die);
            }
            OnRollClicked();
        }

        void OnRollClicked()
        {
            if (busy || selected.Count == 0 || battle.Outcome != BattleOutcome.Ongoing) return;
            StartCoroutine(RollRoutine());
        }

        IEnumerator RollRoutine()
        {
            busy = true;
            battleView.SetBusy(true);

            int rolledBefore = battle.Rolled.Count;
            bool refreshed = false;
            foreach (var die in selected.ToList())
            {
                if (!battle.CanRollMore || die.state != DiceState.Available) break;
                int refreshesBefore = refreshCount;
                battle.Roll(die);
                if (refreshCount != refreshesBefore) refreshed = true;
            }
            selected.Clear();

            battleView.SetLog("ダイスを振った……");
            // 転がっている間は、出目と攻撃・防御の値を伏せておく（止まってから見せる）
            RefreshBattle(battle.Rolled.Count - rolledBefore);
            yield return battleView.PlayRoll(battle, battle.Rolled.Count - rolledBefore);

            string log = "出目：" + string.Join("、", battle.Rolled.Select(r => $"{r.dice.DisplayName} {r.value}"));
            if (refreshed)
            {
                log += battle.CanRollMore ? "　リフレッシュ！ もう1個選べます。" : "　リフレッシュ！";
                Sfx.Play(SoundId.Refresh);
            }
            battleView.SetLog(log + "\n出目ごとに「攻撃」か「防御」を選んで「決定」。");
            RefreshBattle();

            // 錆び賽の自傷などで、振っただけで倒れることがある
            if (battle.Outcome == BattleOutcome.Defeat)
            {
                playLog.RecordBattle(run.Turn, battle);
                FlushPlayLog();
                battleView.SetLog(log + "\n倒れてしまった……");
                battleView.ShowContinue("結果へ");
            }

            battleView.SetBusy(false);
            busy = false;
        }

        void OnAssignClicked(RolledDie die, Assignment assignment)
        {
            if (busy) return;
            battle.Assign(die, assignment);
            RefreshBattle();
        }

        void OnResolveClicked()
        {
            if (busy || battle.Outcome != BattleOutcome.Ongoing) return;
            StartCoroutine(ResolveRoutine());
        }

        IEnumerator ResolveRoutine()
        {
            busy = true;

            var before = new RoundSnapshot
            {
                playerHp = battle.player.hp,
                enemyHp = battle.enemy.hp,
                attack = battle.AttackValue,
                playerBlock = battle.BlockValue,
            };
            var r = battle.Resolve();
            selected.Clear();

            if (battle.Outcome != BattleOutcome.Ongoing)
            {
                playLog.RecordBattle(run.Turn, battle);
                FlushPlayLog();
            }

            battleView.SetLog(r.rolled.Count == 0 ? "パス。" : "");
            yield return battleView.PlayResolve(before, r, battle);

            string log = (r.rolled.Count == 0 ? "パス。" : "") + $"敵に {r.dealt} ダメージ、自分は {r.taken} ダメージ。";
            switch (battle.Outcome)
            {
                case BattleOutcome.Victory:
                    log += $"\n{battle.enemy.data.displayName} を倒した！（{battle.Round} ラウンド）";
                    break;
                case BattleOutcome.Defeat:
                    log += "\n倒れてしまった……";
                    break;
            }
            battleView.SetLog(log);
            RefreshBattle();

            switch (battle.Outcome)
            {
                case BattleOutcome.Victory:
                    battleView.ShowContinue(bossBattle ? "結果へ" : "マップに戻る");
                    break;
                case BattleOutcome.Defeat:
                    battleView.ShowContinue("結果へ");
                    break;
                default:
                    yield return battleView.PlayRoundStart(battle);
                    break;
            }
            busy = false;
        }

        void OnBattleContinue()
        {
            if (busy || battle == null || battle.Outcome == BattleOutcome.Ongoing) return;
            var outcome = battle.Outcome;
            int rounds = battle.Round;
            string enemyName = battle.enemy.data.displayName;
            battle = null;

            if (outcome == BattleOutcome.Defeat)
            {
                ShowResult(false);
                return;
            }
            if (bossBattle && run.IsFinalLayer)
            {
                ShowResult(true);
                return;
            }

            DestroyView(battleView);
            battleView = null;
            // ボスを倒したら、報酬のあと次の層へ
            advanceAfterReward = bossBattle;
            ShowReward(bossBattle ? RewardKind.Boss : battleRewardKind, $"{enemyName} に勝った（{rounds} ラウンド）。");
        }

        bool advanceAfterReward;

        // ---- 報酬 ----

        BattleReward pendingReward;
        int rewardGold;
        string afterRewardMessage;

        /// <summary>報酬画面：ゴールドはここで受け取り、ダイスは選ぶかスキップする。</summary>
        void ShowReward(RewardKind kind, string message)
        {
            pendingReward = run.CreateBattleReward(kind);
            rewardGold = run.GainGold(pendingReward.gold, true);
            afterRewardMessage = message;
            // エリートのレリックはその場で手に入る
            if (pendingReward.relic != null)
            {
                run.AddRelic(pendingReward.relic);
                afterRewardMessage += $"レリック「{pendingReward.relic.displayName}」を手に入れた。";
            }

            rewardView = RewardView.Create(canvas.transform, art, pendingReward, rewardGold, config.rewards.skipGold);
            rewardView.DiceChosen += OnRewardDiceChosen;
            rewardView.Skipped += OnRewardSkipped;
            rewardView.ReplaceChosen += OnRewardReplace;
            rewardView.ReplaceCancelled += () => rewardView.ShowChoices();
        }

        DiceData chosenRewardDice;

        void OnRewardDiceChosen(DiceData data)
        {
            if (!run.CanAddDice)
            {
                chosenRewardDice = data;
                rewardView.ShowReplace(run.pouch, data);
                return;
            }
            run.AddDice(data);
            FinishReward(data.displayName, $"{data.displayName} を手に入れた。");
        }

        void OnRewardReplace(DiceInstance old)
        {
            if (chosenRewardDice == null) return;
            run.ReplaceDice(old, chosenRewardDice);
            FinishReward($"{chosenRewardDice.displayName}>{old.DisplayName}", $"{old.DisplayName} を手放して {chosenRewardDice.displayName} を手に入れた。");
        }

        void OnRewardSkipped()
        {
            int gold = run.SkipDiceReward();
            FinishReward("skip", $"ダイスは受け取らず、{gold} G を得た。");
        }

        void FinishReward(string choiceForLog, string message)
        {
            playLog.RecordReward(run.Turn, pendingReward.kind, rewardGold, choiceForLog, run.Gold);
            FlushPlayLog();

            DestroyView(rewardView);
            rewardView = null;
            pendingReward = null;
            chosenRewardDice = null;

            if (advanceAfterReward)
            {
                advanceAfterReward = false;
                StartCoroutine(LayerTransitionRoutine());
                return;
            }

            map.gameObject.SetActive(true);
            map.SetInteractable(true);
            map.Refresh(run);
            map.SetMessage($"{afterRewardMessage}{message}\n戦闘で使ったダイスは使用済みのままです。");
        }
    }
}
