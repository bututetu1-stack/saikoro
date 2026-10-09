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
            GameSettings.Apply(); // 保存してある音量・演出の速さ
            ShowTitle();
        }

        // ---- 始めの画面とセーブ ----

        TitleView titleView;

        static string SaveFolder => Application.persistentDataPath;

        /// <summary>始めの画面：「続きから」「新しく始める」。</summary>
        void ShowTitle(string forcedError = null)
        {
            CloseAll();
            string error = forcedError;
            var save = forcedError == null ? RunSaveFile.Read(SaveFolder, out error) : null;
            titleView = TitleView.Create(canvas.transform, art, save, error);
            titleView.ContinueClicked += () => ContinueRun(save);
            titleView.NewRunClicked += () =>
            {
                RunSaveFile.Delete(SaveFolder);
                ShowStarterSelect();
            };
            titleView.HowToClicked += ShowHowTo;
            titleView.SettingsClicked += OpenSettings;
            titleView.QuitClicked += QuitGame;
        }

        HowToView howToView;

        /// <summary>遊び方の説明を開く（閉じると始めの画面に戻る）。</summary>
        // ---- 設定と、はじめての案内 ----

        SettingsView settingsView;

        /// <summary>設定の小窓（始めの画面・マップ・戦闘から）。いちばん手前に出す。</summary>
        void OpenSettings()
        {
            if (settingsView != null) return;
            settingsView = SettingsView.Create(canvas.transform);
            settingsView.Closed += () =>
            {
                DestroyView(settingsView);
                settingsView = null;
            };
        }

        /// <summary>はじめての場面で1回だけ案内を出す（host は小窓を出す親。null ならマップ）。</summary>
        IEnumerator HintRoutine(string id, Transform host = null)
        {
            if (GameSettings.HintSeen(id) || map == null) yield break;
            GameSettings.MarkHintSeen(id);
            var (title, body) = Hints.Text(id);
            yield return map.ShowDialog(title, body, new[] { new MapView.DialogOption("わかった") }, _ => { }, false, host);
        }

        /// <summary>マップで案内を出す間は、ダイスを振れないようにする。</summary>
        IEnumerator MapHintRoutine(string id)
        {
            if (GameSettings.HintSeen(id)) yield break;
            busy = true;
            yield return HintRoutine(id);
            busy = false;
        }

        void ShowHowTo()
        {
            DestroyView(howToView);
            howToView = HowToView.Create(canvas.transform, art);
            howToView.Closed += () =>
            {
                DestroyView(howToView);
                howToView = null;
            };
        }

        static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- 保存して中断 ----

        /// <summary>マップの「中断」：確かめてから、保存して始めの画面に戻る。</summary>
        void OnMapSuspend()
        {
            if (busy || run == null) return;
            StartCoroutine(MapSuspendRoutine());
        }

        IEnumerator MapSuspendRoutine()
        {
            busy = true;
            int choice = -1;
            yield return map.ShowDialog("中断", "保存して、始めの画面に戻りますか？\n「続きから」で、今の場所から遊べます。",
                new[] { new MapView.DialogOption("保存して中断する"), new MapView.DialogOption("続ける") }, c => choice = c);
            busy = false;
            if (choice != 0) yield break;
            SaveRun();
            FlushPlayLog();
            ShowTitle();
        }

        /// <summary>戦闘の「中断」：保存は戦闘を始める直前のもの（続きからは、この戦闘の最初から）。</summary>
        void OnBattleSuspend()
        {
            if (busy || battle == null || battle.Outcome != BattleOutcome.Ongoing) return;
            battleView.ShowChoice("中断しますか？　続きからは、この戦闘の最初からになります。", new[] { "保存して中断する" }, i =>
            {
                if (i != 0 || busy) return;
                FlushPlayLog();
                battle = null;
                ShowTitle();
            });
        }

        /// <summary>
        /// いまの状態を保存する。マップで操作を待っているときに呼ぶ（戦闘・イベントの途中では保存しない）。
        /// pendingEnemy を渡すと「この戦闘を始める直前」として保存し、続きからはその戦闘の最初から始まる。
        /// </summary>
        void SaveRun(EnemyData pendingEnemy = null, bool pendingBoss = false, RewardKind pendingKind = RewardKind.Normal)
        {
            if (run == null || run.CurrentBattle != null || run.player.IsDead) return;
            try
            {
                var save = run.CreateSave();
                save.runId = playLog.runId;
                if (pendingEnemy != null)
                {
                    save.hasPendingBattle = true;
                    save.pendingEnemy = pendingEnemy.id;
                    save.pendingBoss = pendingBoss;
                    save.pendingRewardKind = (int)pendingKind;
                }
                RunSaveFile.Write(SaveFolder, save);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[賽ノ道] セーブできませんでした: {e.Message}");
            }
        }

        /// <summary>続きから：セーブからランを作り直す。戦闘を始める直前のセーブなら、その戦闘から。</summary>
        void ContinueRun(RunSave save)
        {
            if (save == null) return;
            RunState restored;
            EnemyData pending = null;
            try
            {
                restored = RunState.Restore(config, save);
                if (save.hasPendingBattle) pending = new SaveRegistry(config).Enemy(save.pendingEnemy);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[賽ノ道] 続きを読めませんでした: {e.Message}");
                ShowTitle(e.Message);
                return;
            }
            Debug.Log($"[賽ノ道] 続きから seed={save.seed}");
            BeginRun(restored, string.IsNullOrEmpty(save.runId) ? PlayLog.NewRunId() : save.runId);
            if (pending != null)
            {
                StartBattle(pending, save.pendingBoss, (RewardKind)save.pendingRewardKind);
                return;
            }
            map.SetMessage($"続きから：第{run.LayerIndex + 1}層「{run.Layer.displayName}」。ダイスをクリックして進もう。");
        }

        // ---- ラン ----

        /// <summary>スターターダイスを選ぶ画面。選んだらランを始める。</summary>
        int pendingSeed; // スターターを選ぶ画面を出すときに決めたシード（候補もこのシードで決まる）

        void ShowStarterSelect()
        {
            CloseAll();
            // スターターの候補は、報酬に出る全部のダイスからランダムに3つ（シードごとに決まる）
            pendingSeed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            var choices = RunState.StarterOptions(config, pendingSeed);
            if (choices.Count == 0)
            {
                StartNewRun(null);
                return;
            }
            starterView = StarterView.Create(canvas.transform, art, config, choices);
            starterView.Chosen += StartNewRun;
        }

        void StartNewRun(DiceData starter)
        {
            int seed = pendingSeed != 0 ? pendingSeed : fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            pendingSeed = 0;
            Debug.Log($"[賽ノ道] 新しいラン seed={seed}　記録: {PlayLogPath}");
            BeginRun(new RunState(config, seed, starter), PlayLog.NewRunId());
            map.SetMessage($"シード {seed}　ダイスにマウスを乗せると、止まりうるマスが光ります。クリックで振って進みます。");
            StartCoroutine(MapHintRoutine(Hints.FirstMove));
        }

        /// <summary>ラン（新しく始めた・続きから）の画面と記録を用意して、マップを出す。</summary>
        void BeginRun(RunState newRun, string runId)
        {
            run = newRun;
            playLog = new PlayLog(runId, run.random.Seed) { goldSource = () => run.Gold, layerSource = () => run.LayerIndex + 1 };
            run.Acquired += (kind, item) => playLog.RecordAcquire(run.Turn, kind, item);

            CloseAll();
            busy = false;
            CreateMap();
            run.Refreshed += _ =>
            {
                refreshCount++;
                FlashRelics(Trigger.OnRefresh);
            };

            map.Refresh(run);
            SaveRun();
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
            map.CharmUsable = c => !run.ReachedGoal && run.CanUseNow(c);
            map.Charms.Clicked += OnMapCharmClicked;
            map.SuspendClicked += OnMapSuspend;
            map.SettingsClicked += OpenSettings;
            map.MirrorValue = () => DiceRoller.MirrorValue(run.LastRolledValue);
            // 千里眼：振る前に出目が見える
            map.ForeseenValue = die => run.ForeseeRoll(die);
            // 地図師の矢立：マスにマウスを乗せると、敵とイベントの中身が見える
            map.TileExtraInfo = tile =>
            {
                // ボスは誰が待っているか、いつでも見える（STS と同じ）
                if (tile.type == TileType.Boss && run.LayerBoss != null) return $"ボス：{run.LayerBoss.displayName}";
                if (!run.CanSeeContents) return null;
                var enemy = run.PeekEnemy(tile);
                if (enemy != null) return $"地図師の矢立：{enemy.displayName}" + (enemy.count > 1 ? $"×{enemy.count}" : "") + $"（HP {enemy.maxHp}）";
                var ev = run.PeekEvent(tile);
                return ev.HasValue ? $"地図師の矢立：{RunState.EventName(ev.Value)}" : null;
            };
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

            // 黄金の賽筒などで容量が減って、容量より多く持っているときは手放す
            while (run.OverCapacity)
            {
                DiceInstance discarded = null;
                removeView = DiceRemoveView.Create(canvas.transform, art, run.pouch,
                    $"ポーチの容量が {run.pouch.Capacity} 個になった。ダイスを1個手放してください。", "ダイスを手放す", "手放す", false);
                removeView.Removed += die => discarded = die;
                while (discarded == null) yield return null;
                DestroyView(removeView);
                removeView = null;
                run.DiscardDice(discarded);
            }

            var cleared = run.Layer.displayName;
            var result = run.AdvanceLayer();
            playLog.RecordAcquire(run.Turn, "layer", $"{run.LayerIndex + 1}:{run.Layer.displayName}");
            FlushPlayLog();
            CreateMap();
            map.gameObject.SetActive(false);

            bool next = false;
            layerIntro = LayerIntroView.Create(canvas.transform, art, run.LayerIndex, run.Layer.displayName,
                $"「{cleared}」を踏破した。\nHP が {result.healed} 回復し、すべてのダイスが使えるようになった。" + (run.LayerBoss != null ? $"\nこの層のボス：{run.LayerBoss.displayName}" : ""));
            layerIntro.Continued += () => next = true;
            while (!next) yield return null;
            DestroyView(layerIntro);
            layerIntro = null;

            map.gameObject.SetActive(true);
            map.Refresh(run);
            map.SetInteractable(true);
            map.SetMessage($"第{run.LayerIndex + 1}層「{run.Layer.displayName}」。ボスを目指して進もう。");
            SaveRun();
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
            DestroyView(titleView);
            DestroyView(howToView);
            howToView = null;
            titleView = null;
            DestroyView(forgeView);
            forgeView = null;
            DestroyView(replaceView);
            replaceView = null;
            DestroyView(shopView);
            shopView = null;
            DestroyView(removeView);
            removeView = null;
            DestroyView(chooseView);
            DestroyView(craftView);
            craftView = null;
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

        /// <param name="cause">力尽きた原因（敵・マスの名前）。結果のまとめに出す。</param>
        void ShowResult(bool cleared, string cause = null)
        {
            if (!cleared) run.stats.deathCause = cause;
            // ランが終わったら続きは消す
            RunSaveFile.Delete(SaveFolder);
            playLog.RecordResult(run.Turn, cleared, run.player.hp, run.player.maxHp, run.stats);
            FlushPlayLog();
            CloseAll();
            Sfx.StopAll();
            // 自己ベスト（踏破にかかったターン数。少ないほどよい）
            int previousBest = BestRecord.BestTurns;
            bool newBest = cleared && BestRecord.Submit(run.Turn);
            string bestText = !cleared ? null
                : newBest ? (previousBest > 0 ? $"踏破 {run.Turn} ターン　<color=#FFD24D>自己ベスト更新！</color>（前は {previousBest} ターン）" : $"踏破 {run.Turn} ターン　<color=#FFD24D>初めての踏破！</color>")
                : $"踏破 {run.Turn} ターン　（自己ベスト {BestRecord.BestTurns} ターン）";
            // unityroom のランキングに送る（鍵があって、ブラウザで動いているときだけ）
            if (cleared && Ranking.SubmitClearTurns(run.Turn)) bestText += "　<size=75%>ランキングに送りました</size>";
            resultView = ResultView.Create(canvas.transform, art, cleared, run, bestText);
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
            SaveRun();
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
            // 錆び賽の自傷で倒れた（そのまま進むと止まっていた）
            if (run.player.IsDead)
            {
                busy = false;
                ShowResult(false, die.DisplayName);
                yield break;
            }

            // 再転：振り直すか選ぶ（盤面を見ながら選べるよう、行き先を光らせて小窓は下に）
            if (moving.canReroll)
            {
                map.ShowDestinations(DestinationsFrom(run.Current, moving.value));
                int choice = -1;
                yield return map.ShowDialog("振り直し（再転・振り直し御札）", $"出目は {moving.value}。光っているマスに止まれる。振り直しますか？（1回だけ）",
                    new[] { new MapView.DialogOption("振り直す"), new MapView.DialogOption("このまま進む") }, c => choice = c, true);
                map.ClearReach();
                if (choice == 0)
                {
                    run.RerollMove(moving);
                    yield return map.PlayRoll(die, moving.value, die.faces[moving.faceIndex].engraving);
                    map.RefreshStatus(run);
                }
            }

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

            // 振り直し（再転・御札）でも錆び賽の自傷はある
            if (run.player.IsDead)
            {
                busy = false;
                ShowResult(false, die.DisplayName);
                yield break;
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
            yield return WalkRoutine(moving, moveText, d => died = d, true);
            if (died)
            {
                busy = false;
                ShowResult(false, "移動中");
                yield break;
            }
            var move = run.FinishMove(moving);
            playLog.RecordMove(run.Turn, move);
            FlushPlayLog();
            map.HideRoll();

            map.RefreshStatus(run);
            map.RefreshTray(run.pouch);
            if (move.refreshed) yield return map.PlayRefresh();
            if (move.refreshed) yield return HintRoutine(Hints.FirstRefresh);

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
        /// <param name="waitForClick">
        /// true なら、行き先が1つでも自動では進まず、クリックを待つ。その間にお守り（進み御札など）を使える（開発者の要望）。
        /// </param>
        IEnumerator WalkRoutine(RunState.MoveInProgress moving, string moveText, System.Action<bool> onDied, bool waitForClick = false)
        {
            map.SetMessage(moveText);
            map.SetRemaining(moving.remaining);

            // 止まれるマスをクリックで選ぶ（開発者の判断：道が複雑でも、何度も止められないように）
            // 帰り道：行き先（次の休憩かショップ）は決まっている
            List<TileNode> destinations;
            TileNode target = null;
            if (moving.forcedTarget != null)
            {
                destinations = new List<TileNode> { moving.forcedTarget };
                target = moving.forcedTarget;
                map.SetMessage($"帰り道：{MapView.TileName(moving.forcedTarget)}まで一気に進む（{moving.remaining} マス）");
            }
            else
            {
                destinations = DestinationsFrom(run.Current, moving.remaining).ToList();
                if (destinations.Count == 1 && !waitForClick) target = destinations[0];
                // お守りで出目が変わったら、行き先を出し直す（帰り道になったら、そのまま進む）
                choosingDestination = waitForClick;
                while (target == null)
                {
                    if (moving.forcedTarget != null)
                    {
                        target = moving.forcedTarget;
                        break;
                    }
                    destinations = DestinationsFrom(run.Current, moving.remaining).ToList();
                    map.SetRemaining(moving.remaining);
                    map.SetMessage((moving.dice != null ? $"{moving.dice.DisplayName}で {moving.value}　" : "") + "止まるマスをクリックしてください"
                        + (waitForClick && run.Charms.Any(c => run.CanUseNow(c)) ? "（その前にお守りも使えます）" : ""));
                    yield return map.ChooseBranch(destinations, c => target = c);
                    // お守りの小窓が開いている間は待つ
                    while (charmMenuOpen) yield return null;
                    // 振り直し御札で錆び賽を振り直して倒れた
                    if (run.player.IsDead)
                    {
                        choosingDestination = false;
                        onDied(true);
                        yield break;
                    }
                }
                choosingDestination = false;
                if (waitForClick && moving.dice != null) moveText = $"{moving.dice.DisplayName}で {moving.value}";
                map.SetMessage(moveText);
            }
            if (target != null) map.ShowDestinations(new[] { target });
            yield return UIAnim.Wait(waitForClick || destinations.Count > 1 ? 0.2f : 0.6f);

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
                    // 刻印の候補は最初に1回だけ決める（「鍛える」→「やめる」をくり返すと引き直せてしまっていた）
                    List<EngravingData> restOffer = null;
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
                            if (restOffer == null) restOffer = run.CreateForgeOffer();
                            yield return ForgeRoutine(r => forged = r, restOffer);
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
                    // 鍛冶の金槌：もう1つ付けられる（やめてもよい）
                    for (int extra = 0; forged != null && extra < run.StatBonus(RunStat.ForgeExtraEngravings); extra++)
                    {
                        map.SetMessage(message + "\n鍛冶の金槌：もう1つ刻印を付けられる。");
                        string more = null;
                        yield return ForgeRoutine(r => more = r);
                        if (more == null) break;
                        message += "\n" + more;
                    }
                    break;
                }
                case TileType.Treasure:
                {
                    var treasure = run.OpenTreasure();
                    message += "\n" + treasure.message;
                    map.RefreshStatus(run);
                    // レリックは受け取るか選ぶ（デメリットのあるものや、合わないものもあるため）
                    if (treasure.relic != null)
                    {
                        int take = -1;
                        yield return map.ShowDialog("宝箱", $"レリック「{treasure.relic.displayName}」が入っていた。\n{treasure.relic.description}",
                            new[] { new MapView.DialogOption("受け取る"), new MapView.DialogOption("受け取らない") },
                            c => take = c);
                        if (take == 0)
                        {
                            run.AddRelic(treasure.relic);
                            map.RefreshStatus(run);
                            message += "\n" + $"レリック「{treasure.relic.displayName}」を手に入れた。";
                        }
                        else message += "\n" + "レリックは置いていった。";
                    }
                    if (treasure.diceOffer != null)
                    {
                        int choice = -1;
                        // レリックのあとなら、レリックの話はくり返さない
                        string found = treasure.relic != null
                            ? $"宝箱の奥には、もう1つ「{treasure.diceOffer.displayName}」も入っていた。"
                            : $"{treasure.message}\n奥に「{treasure.diceOffer.displayName}」も入っていた。";
                        yield return map.ShowDialog("宝箱", found
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
                    yield return EventRoutine(move.to, r => result = r, f => forced = f);
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
                            ShowResult(false, "移動中");
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
                ShowResult(false, ResultView.TileName(move.to.type));
                yield break;
            }
            SaveRun();
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
        /// <param name="offer">出す刻印（休憩で「やめる」→「鍛える」をくり返しても同じものを出すため）。null なら新しく決める。</param>
        IEnumerator ForgeRoutine(System.Action<string> onDone, List<EngravingData> offer = null)
        {
            offer = offer ?? run.CreateForgeOffer();
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
        CraftView craftView;

        /// <summary>使用可能なダイスを1個選ばせる。やめたら null。</summary>
        IEnumerator ChooseDiceRoutine(string title, string subtitle, string verb, System.Action<DiceInstance> onDone,
            System.Func<DiceInstance, bool> usable = null, string notUsableLabel = null)
        {
            DiceInstance chosen = null;
            bool finished = false;
            chooseView = DiceChooseView.Create(canvas.transform, art, run.pouch, title, subtitle, verb, usable, notUsableLabel);
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
        IEnumerator EventRoutine(TileNode tile, System.Action<string> onDone, System.Action<RunState.MoveInProgress> onForcedMove)
        {
            var s = config.events;
            var kind = run.PickEvent(tile); // 地図師の矢立で前もって見ていたら、そのイベント
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
                                new MapView.DialogOption($"HP−{s.shrineHpCost} で「{engraving?.displayName}」を刻む", canEngrave),
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
                            new MapView.DialogOption(config.charmPool.Count == 0 ? $"見送る（ご祝儀に {s.foxSeeOffGold} G）" : run.CanAddCharm ? "見送る（お礼にお守り1個）" : $"見送る（お守りはいっぱいなので {s.foxSeeOffGold} G）"),
                        }, c => choice = c);
                    if (choice == 0)
                    {
                        run.FollowFox();
                        onDone($"狐の嫁入り：行列についていく。次の {s.foxTurns} ターン、移動の出目+{s.foxMoveBonus}。");
                    }
                    else
                    {
                        int foxGold = run.SeeOffFox(out var foxCharm);
                        onDone(foxCharm != null ? $"狐の嫁入り：行列を見送った。お礼にお守り「{foxCharm.displayName}」をもらった。" : $"狐の嫁入り：行列を見送った。{foxGold} G を得た。");
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

                case EventKind.Craftsman:
                {
                    while (true)
                    {
                        choice = -1;
                        string note = run.CanCraft ? "" : run.Gold < s.craftCost ? $"\n（{s.craftCost} G 持っていない）" : "\n（改造できるダイスがない）";
                        yield return map.ShowDialog("流しの職人", $"「{s.craftCost} G くれりゃ、好きな面を 1〜6 の好きな目に彫り直してやるよ」\n刻印はそのまま残る。{note}",
                            new[] { new MapView.DialogOption($"頼む（{s.craftCost} G）", run.CanCraft), new MapView.DialogOption("立ち去る") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone("流しの職人：頼まずに立ち去った。");
                            yield break;
                        }
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("流しの職人", "彫り直すダイスを選んでください。", "彫り直す", d => die = d, RunState.CanForge, "改造できない");
                        if (die == null) continue;
                        bool finished = false;
                        string result = null;
                        craftView = CraftView.Create(canvas.transform, die, $"{s.craftCost} G で、面を1つ好きな値（1〜6）にする。");
                        craftView.Applied += (face, value) =>
                        {
                            int before = die.faces[face].value;
                            run.Craft(die, face, value);
                            result = $"流しの職人：{s.craftCost} G で {die.DisplayName} の面を {before} → {value} にしてもらった。";
                            finished = true;
                        };
                        craftView.Cancelled += () => finished = true;
                        while (!finished) yield return null;
                        DestroyView(craftView);
                        craftView = null;
                        if (result != null)
                        {
                            onDone(result);
                            yield break;
                        }
                    }
                }

                case EventKind.TwinStatues:
                {
                    while (true)
                    {
                        choice = -1;
                        yield return map.ShowDialog("道祖神の双子像", "二体並んだ道祖神。片方にダイスを捧げると、もう片方がダイスを刻印ごと写してくれるという。",
                            new[] { new MapView.DialogOption("ダイスを捧げる", run.CanUseTwinStatues), new MapView.DialogOption("立ち去る") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone("道祖神の双子像：何もせずに立ち去った。");
                            yield break;
                        }
                        DiceInstance offer = null;
                        yield return ChooseDiceRoutine("道祖神の双子像", "捧げるダイスを選んでください（ポーチから消える）。", "捧げる", d => offer = d, d => true);
                        if (offer == null) continue;
                        DiceInstance copy = null;
                        yield return ChooseDiceRoutine("道祖神の双子像", $"{offer.DisplayName} を捧げる。写すダイスを選んでください（刻印ごと複製）。", "写す", d => copy = d,
                            d => d != offer && RunState.CanDuplicate(d), "選べない");
                        if (copy == null) continue;
                        run.OfferAndDuplicate(offer, copy);
                        map.RefreshTray(run.pouch);
                        onDone($"道祖神の双子像：{offer.DisplayName} を捧げ、{copy.DisplayName} が2つになった。");
                        yield break;
                    }
                }

                case EventKind.Pitfall:
                {
                    yield return map.ShowDialog("落とし穴", $"足もとの地面が崩れかけている！ ダイスを1個振って、{s.pitfallThreshold} 以上なら飛び越えられる。失敗すると HP−{s.pitfallDamage}。\n振ったダイスは使用済みになる。",
                        new[] { new MapView.DialogOption("ダイスを振る") }, c => choice = c);
                    DiceInstance die = null;
                    while (die == null) yield return ChooseDiceRoutine("落とし穴", $"{s.pitfallThreshold} 以上で回避。振るダイスを選んでください。", "振る", d => die = d);
                    var r = run.Pitfall(die);
                    map.RefreshTray(run.pouch);
                    yield return map.PlayRoll(die, r.value, null);
                    yield return UIAnim.Wait(0.4f);
                    map.HideRoll();
                    if (r.refreshed) yield return map.PlayRefresh();
                    if (!r.avoided)
                    {
                        Sfx.Play(SoundId.Trap);
                        StartCoroutine(map.ShakeBoard());
                    }
                    map.RefreshStatus(run);
                    string text = r.avoided ? $"落とし穴：{die.DisplayName}で {r.value}。ひらりと飛び越えた！" : $"落とし穴：{die.DisplayName}で {r.value}。落ちてしまい HP を {r.damage} 失った。";
                    if (r.refreshed) text += "　リフレッシュ！";
                    onDone(text);
                    yield break;
                }

                case EventKind.Merchant:
                {
                    while (true)
                    {
                        choice = -1;
                        bool canTrade = run.pouch.All.Any(run.CanTrade);
                        yield return map.ShowDialog("旅の商人", "「そのダイス、同じくらいの値打ちの別のダイスと取り換えませんか？」\n（刻印は消える。何が出るかはお楽しみ）",
                            new[] { new MapView.DialogOption("取り換える", canTrade), new MapView.DialogOption("立ち去る") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone("旅の商人：取り換えずに立ち去った。");
                            yield break;
                        }
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("旅の商人", "渡すダイスを選んでください（同じレア度のダイスになる）。", "取り換える", d => die = d, run.CanTrade, "取り換えられない");
                        if (die == null) continue;
                        var got = run.Trade(die);
                        map.RefreshTray(run.pouch);
                        onDone($"旅の商人：{die.DisplayName} を渡して {got.DisplayName} をもらった。");
                        yield break;
                    }
                }

                case EventKind.OniDice:
                {
                    while (true)
                    {
                        choice = -1;
                        yield return map.ShowDialog("鬼の賽勝負", "「おう、ダイスを1個賭けて勝負しろ。おれより大きい目なら宝をやる。小さけりゃそのダイスはもらう」\n（同じ目なら引き分け。賭けたダイスは使用済みになる）",
                            new[] { new MapView.DialogOption("勝負する", run.CanPlayOni), new MapView.DialogOption("断る") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone("鬼の賽勝負：断って立ち去った。");
                            yield break;
                        }
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("鬼の賽勝負", "賭けるダイスを選んでください（負けると失う）。", "勝負する", d => die = d, run.CanBetOni, "賭けられない");
                        if (die == null) continue;
                        string dieName = die.DisplayName;
                        var r = run.PlayOni(die);
                        map.RefreshTray(run.pouch);
                        yield return map.PlayRoll(die, r.playerValue, null);
                        yield return UIAnim.Wait(0.4f);
                        map.HideRoll();
                        if (r.refreshed) yield return map.PlayRefresh();
                        string text = $"鬼の賽勝負：{dieName} {r.playerValue} 対 鬼 {r.oniValue}。";
                        if (r.win)
                        {
                            text += r.relic != null ? $"勝った！ レリック「{r.relic.displayName}」を手に入れた。" : "勝った！ ……が、鬼は何も持っていなかった。";
                            map.RefreshStatus(run);
                        }
                        else if (r.draw) text += "引き分け。鬼は笑って去っていった。";
                        else
                        {
                            text += $"負けた……{dieName} を奪われた。";
                            StartCoroutine(map.ShakeBoard());
                        }
                        onDone(text);
                        yield break;
                    }
                }

                case EventKind.LostChild:
                {
                    while (true)
                    {
                        choice = -1;
                        yield return map.ShowDialog("迷子の子ども", $"道の真ん中で子どもが泣いている。家まで送ってあげようか。\n送る：1回休み（ダイスを1個、進まずに使用済みにする）。お礼に {s.lostChildGold} G とお守り1個。",
                            new[] { new MapView.DialogOption("送ってあげる（1回休み）"), new MapView.DialogOption($"道を教えるだけ（{s.lostChildDirectionsGold} G）") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone($"迷子の子ども：道を教えてあげた。{run.DirectLostChild()} G もらった。");
                            yield break;
                        }
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("迷子の子ども", "1回休み：使用済みにするダイスを選んでください。", "休む", d => die = d);
                        if (die == null) continue;
                        int gold = run.GuideLostChild(die, out var charm, out bool refreshed);
                        map.RefreshTray(run.pouch);
                        if (refreshed) yield return map.PlayRefresh();
                        string text = $"迷子の子ども：家まで送った（{die.DisplayName}を使って1回休み）。お礼に {gold} G" + (charm != null ? $"とお守り「{charm.displayName}」をもらった。" : "をもらった。");
                        if (refreshed) text += "　リフレッシュ！";
                        onDone(text);
                        yield break;
                    }
                }

                case EventKind.StartOverCard:
                {
                    yield return map.ShowDialog("振り出しの札", $"古い札が落ちている。「振り出しに戻る」と書いてある……。\n引けば、この層のスタートに戻る代わりに最大 HP+{s.startOverMaxHp} して全回復する。（1ランに1回）",
                        new[] { new MapView.DialogOption("札を引く"), new MapView.DialogOption("引かない") }, c => choice = c);
                    if (choice == 1)
                    {
                        onDone("振り出しの札：引かずにそっとしておいた。");
                        yield break;
                    }
                    run.DrawStartOverCard();
                    Sfx.Play(SoundId.Heal);
                    map.Refresh(run);
                    onDone($"振り出しの札：スタートに戻された！ 最大 HP が {s.startOverMaxHp} 増え、全回復した。");
                    yield break;
                }

                case EventKind.WoundedSamurai:
                {
                    yield return map.ShowDialog("行き倒れの侍", "道ばたに傷だらけの侍が倒れている。懐には重そうな財布がのぞいている……。",
                        new[]
                        {
                            new MapView.DialogOption($"介抱する（HP−{s.samuraiHpCost}、お礼にレリック）", run.CanHelpSamurai),
                            new MapView.DialogOption($"懐を探る（+{s.samuraiRobGold} G、呪いのダイス）"),
                            new MapView.DialogOption("立ち去る"),
                        }, c => choice = c);
                    if (choice == 0)
                    {
                        var relic = run.HelpSamurai(out int gold);
                        map.RefreshStatus(run);
                        onDone(relic != null
                            ? $"行き倒れの侍：手当てをした（HP−{s.samuraiHpCost}）。お礼にレリック「{relic.displayName}」をもらった。"
                            : $"行き倒れの侍：手当てをした（HP−{s.samuraiHpCost}）。お礼に {gold} G をもらった。");
                    }
                    else if (choice == 1)
                    {
                        int gold = run.RobSamurai(out var curse);
                        map.RefreshStatus(run);
                        map.RefreshTray(run.pouch);
                        onDone($"行き倒れの侍：財布から {gold} G を抜き取った。" + (curse != null ? $"……呪いの {curse.DisplayName} がまとわりついてきた。" : ""));
                    }
                    else onDone("行き倒れの侍：見なかったことにして立ち去った。");
                    yield break;
                }

                case EventKind.HotSpring:
                {
                    yield return map.ShowDialog("湯治場", $"山あいに湯けむりが立ちのぼっている。旅の疲れを癒やしていこうか。\n湯に浸かる：{s.hotSpringCost} G で HP を {run.HotSpringHeal} 回復。",
                        new[]
                        {
                            new MapView.DialogOption($"湯に浸かる（{s.hotSpringCost} G）", run.CanBathe),
                            new MapView.DialogOption($"足湯だけ（無料・HP+{s.footBathHeal}）"),
                        }, c => choice = c);
                    int healed = choice == 0 ? run.Bathe() : run.FootBath();
                    Sfx.Play(SoundId.Heal);
                    map.RefreshStatus(run);
                    onDone(choice == 0 ? $"湯治場：湯に浸かって HP が {healed} 回復した（{s.hotSpringCost} G）。" : $"湯治場：足湯で HP が {healed} 回復した。");
                    yield break;
                }

                case EventKind.Tsukumogami:
                {
                    while (true)
                    {
                        choice = -1;
                        yield return map.ShowDialog("賽の付喪神", "古いさいころに宿った神が現れた。「賽をひとつ差し出せ。もっと良いものに変えてやろう」\n（コモン・呪いはアンコモンに、アンコモン・レアはレアに化ける。何に化けるかはわからない）",
                            new[] { new MapView.DialogOption("ダイスを差し出す"), new MapView.DialogOption("断る") }, c => choice = c);
                        if (choice == 1)
                        {
                            onDone("賽の付喪神：丁重に断った。");
                            yield break;
                        }
                        DiceInstance die = null;
                        yield return ChooseDiceRoutine("賽の付喪神", "差し出すダイスを選んでください（呪いのダイスも差し出せる）。", "差し出す", d => die = d,
                            run.CanOfferToTsukumogami, "差し出せない");
                        if (die == null) continue;
                        string before = die.DisplayName;
                        var after = run.OfferToTsukumogami(die);
                        map.RefreshTray(run.pouch);
                        onDone(after != null ? $"賽の付喪神：{before} が {after.DisplayName} に化けた！" : "賽の付喪神：神は首をかしげて消えてしまった。");
                        yield break;
                    }
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
            CharmData charmClicked = null;

            shopView = ShopView.Create(canvas.transform, art, shop, run);
            shopView.BuyClicked += item => { if (!working) buying = item; };
            shopView.RemoveClicked += () => { if (!working) removing = true; };
            shopView.LeaveClicked += () => { if (!working) leave = true; };
            shopView.CharmClicked += c => { if (!working) charmClicked = c; };
            yield return HintRoutine(Hints.FirstShop, shopView.transform);

            while (!leave)
            {
                if (charmClicked != null)
                {
                    // 持っているお守り：使う・捨てる（開発者の要望：ショップでも整理できるように）
                    working = true;
                    string done = null;
                    yield return CharmMenuRoutine(charmClicked, shopView.transform, r => done = r);
                    if (done != null) log.Add(done);
                    charmClicked = null;
                    working = false;
                    shopView.Refresh();
                }
                else if (buying != null)
                {
                    working = true;
                    var item = buying;
                    string result = null;
                    // 入れ替え・面選び・削除の画面を開いている間はショップを隠す（透けて見づらいため）
                    shopView.gameObject.SetActive(item.kind == ShopItemKind.Relic || item.kind == ShopItemKind.Charm);
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
                case ShopItemKind.Charm:
                    if (!run.CanAddCharm)
                    {
                        // いっぱいなら、どれかと入れ替える
                        var owned = run.Charms.ToList();
                        var options = owned.Select(c => new MapView.DialogOption($"「{c.displayName}」を捨てる")).ToList();
                        options.Add(new MapView.DialogOption("やめる"));
                        int choice = -1;
                        yield return map.ShowDialog("お守りがいっぱい", $"お守りは{RunState.MaxCharms}個までしか持てない。\nどれかを捨てて「{item.charm.displayName}」を買う？",
                            options, c => choice = c, false, shopView != null ? shopView.transform : null);
                        if (choice < 0 || choice >= owned.Count)
                        {
                            onDone(null);
                            yield break;
                        }
                        shop.BuyCharm(item, owned[choice]);
                        onDone($"お守り「{owned[choice].displayName}」を捨てて、「{item.charm.displayName}」を買った（{item.price} G）。");
                        yield break;
                    }
                    shop.BuyCharm(item);
                    onDone($"お守り「{item.charm.displayName}」を買った（{item.price} G）。");
                    yield break;

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
            // 続きからは、この戦闘の最初から（戦闘の途中は保存しない）
            SaveRun(enemy, isBoss, rewardKind);
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
            battleView.RerollClicked += r => { if (!busy) StartCoroutine(BattleRerollRoutine(r)); };
            battleView.FateClicked += r =>
            {
                if (busy || battle == null || !battle.CanUseFate) return;
                battleView.ShowFatePicker(r, battle.FateMaxValue, value =>
                {
                    if (value <= 0 || battle == null || !battle.CanUseFate) return;
                    battle.UseFate(r, value);
                    battleView.SetLog($"運命の糸：{r.dice.DisplayName}の出目を {value} にした。");
                    RefreshBattle();
                });
            };
            battleView.RollClicked += OnRollClicked;
            battleView.AssignClicked += OnAssignClicked;
            battleView.ResolveClicked += OnResolveClicked;
            battleView.ContinueClicked += OnBattleContinue;
            battleView.Charms.Clicked += OnBattleCharmClicked;
            battleView.SuspendClicked += OnBattleSuspend;
            battleView.SettingsClicked += OpenSettings;
            StartCoroutine(HintRoutine(Hints.FirstBattle, battleView.transform));
            // 煙玉で逃げられるのは通常戦だけ
            battle.canFleeBattle = !isBoss && rewardKind == RewardKind.Normal;

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
            battleView.Charms.Refresh(run, c => battle != null && run.CanUseInBattle(c, battle));
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

        /// <summary>再転：振ったダイスを振り直す。</summary>
        IEnumerator BattleRerollRoutine(RolledDie r)
        {
            if (battle == null || battle.Outcome != BattleOutcome.Ongoing || !r.canReroll || r.rerolled) yield break;
            busy = true;
            battleView.SetBusy(true);
            int index = battle.Rolled.ToList().IndexOf(r);
            battle.Reroll(r);
            RefreshBattle();
            yield return battleView.PlayRerollAt(battle, index);
            battleView.SetLog($"再転：{r.dice.DisplayName}を振り直して {r.value}。");
            RefreshBattle();
            if (battle.Outcome == BattleOutcome.Defeat) battleView.ShowContinue("結果へ");
            battleView.SetBusy(false);
            busy = false;
        }

        // ---- お守り ----

        /// <summary>傷薬・押し入れの鍵・残り福を使ったときの文。</summary>
        static string CharmMessage(CharmData charm, int result)
        {
            switch (charm.kind)
            {
                case CharmKind.Heal: return $"{charm.displayName}：HP が {result} 回復した。";
                case CharmKind.Unseal: return $"{charm.displayName}：封印されたダイス {result} 個が使えるようになった。";
                case CharmKind.ReturnUsed: return $"{charm.displayName}：使用済みのダイス {result} 個が戻った。";
                case CharmKind.MoveForward: return $"{charm.displayName}：次の移動の出目が +{result} になる。";
                case CharmKind.MoveBack: return $"{charm.displayName}：次の移動の出目が −{result} になる（最低1）。";
                case CharmKind.RerollDie: return $"{charm.displayName}：次の移動で、出目を見てから1回振り直せる。";
                case CharmKind.GainStrength: return $"{charm.displayName}：この戦闘の間、筋力 +{result}。";
                case CharmKind.GainBlock: return $"{charm.displayName}：防御 +{result}（このラウンド）。";
                default: return $"{charm.displayName} を使った。";
            }
        }

        /// <summary>マップでお守りをクリック：使う・捨てるを選ぶ。</summary>
        /// <remarks>移動のダイスを振ったあと、行き先を選んでいる間も使える（進み御札などが今の移動に効く）。</remarks>
        void OnMapCharmClicked(CharmData charm)
        {
            if ((busy && !choosingDestination) || charmMenuOpen || run.ReachedGoal) return;
            StartCoroutine(MapCharmRoutine(charm));
        }

        bool choosingDestination; // 移動のダイスを振って、行き先のクリックを待っている
        bool charmMenuOpen;

        IEnumerator MapCharmRoutine(CharmData charm)
        {
            bool moving = choosingDestination;
            var pending = run.PendingMove;
            bool wasBusy = busy;
            busy = true;
            charmMenuOpen = true;
            string done = null;
            yield return CharmMenuRoutine(charm, null, r => done = r);
            if (moving)
            {
                // 振り直し御札：転がし直して見せる。進み・止まり御札：残りの歩数が変わる
                if (done != null && charm.kind == CharmKind.RerollDie && pending != null)
                {
                    yield return map.PlayRoll(pending.dice, pending.value, pending.dice.faces[pending.faceIndex].engraving);
                }
                map.RefreshStatus(run);
                map.RefreshTray(run.pouch);
                if (done != null) map.CancelChooseBranch(); // 行き先を出し直す
                charmMenuOpen = false;
                busy = wasBusy;
                yield break;
            }
            charmMenuOpen = false;
            busy = wasBusy;
            map.Refresh(run);
            if (done != null)
            {
                map.SetMessage(done);
                SaveRun();
            }
        }

        /// <summary>
        /// 持っているお守りの小窓：使う（今使えるときだけ）・捨てる・やめる（マップ・ショップで共通）。
        /// host は小窓を出す親（null ならマップ）。起きたことの説明を onDone に渡す（やめたら null）。
        /// </summary>
        IEnumerator CharmMenuRoutine(CharmData charm, Transform host, System.Action<string> onDone)
        {
            bool canUse = run.CanUseNow(charm);
            int choice = -1;
            yield return map.ShowDialog($"お守り「{charm.displayName}」", charm.description + (canUse ? "" : "\n（今は使えない）"),
                new[] { new MapView.DialogOption("使う", canUse), new MapView.DialogOption("捨てる"), new MapView.DialogOption("やめる") },
                c => choice = c, false, host);
            if (choice == 0 && run.CanUseNow(charm))
            {
                int result = run.UseCharm(charm);
                map.RefreshStatus(run);
                map.RefreshTray(run.pouch);
                onDone(CharmMessage(charm, result));
            }
            else if (choice == 1)
            {
                run.DiscardCharm(charm);
                map.RefreshStatus(run);
                onDone($"お守り「{charm.displayName}」を捨てた。");
            }
            else onDone(null);
        }

        /// <summary>戦闘でお守りをクリック。</summary>
        void OnBattleCharmClicked(CharmData charm)
        {
            if (busy || battle == null || !run.CanUseInBattle(charm, battle)) return;
            switch (charm.kind)
            {
                case CharmKind.RerollDie:
                    var rolled = battle.Rolled.ToList();
                    if (rolled.Count == 1)
                    {
                        StartCoroutine(BattleCharmRerollRoutine(charm, rolled[0]));
                        return;
                    }
                    battleView.ShowChoice($"{charm.displayName}：振り直すダイスを選ぶ", rolled.Select(r => $"{r.dice.DisplayName} {r.value}").ToList(), i =>
                    {
                        if (i >= 0 && !busy && battle != null && run.CanUseInBattle(charm, battle)) StartCoroutine(BattleCharmRerollRoutine(charm, rolled[i]));
                    });
                    return;
                case CharmKind.Smoke:
                    run.UseSmoke(charm, battle);
                    playLog.RecordBattle(run.Turn, battle);
                    FlushPlayLog();
                    battleView.SetLog($"{charm.displayName}：煙にまぎれて逃げ出した。（報酬なし）");
                    RefreshBattle();
                    battleView.ShowContinue("マップに戻る");
                    return;
                case CharmKind.WeakenEnemy:
                case CharmKind.VulnerableEnemy:
                case CharmKind.PoisonEnemy:
                {
                    string name = battle.Target.data.displayName;
                    run.UseEnemyCharm(charm, battle);
                    string what = charm.kind == CharmKind.WeakenEnemy ? "脱力" : charm.kind == CharmKind.VulnerableEnemy ? "弱体" : "毒";
                    battleView.SetLog($"{charm.displayName}：{name} に{what} {charm.amount} を与えた。");
                    Sfx.Play(charm.kind == CharmKind.PoisonEnemy ? SoundId.Poison : SoundId.Debuff);
                    RefreshBattle();
                    return;
                }
                default:
                    int result = run.UseCharm(charm);
                    battleView.SetLog(CharmMessage(charm, result));
                    RefreshBattle();
                    return;
            }
        }

        /// <summary>振り直し御札：振ったダイスを振り直す。</summary>
        IEnumerator BattleCharmRerollRoutine(CharmData charm, RolledDie r)
        {
            busy = true;
            battleView.SetBusy(true);
            int index = battle.Rolled.ToList().IndexOf(r);
            run.UseRerollCharm(charm, battle, r);
            RefreshBattle();
            yield return battleView.PlayRerollAt(battle, index);
            battleView.SetLog($"{charm.displayName}：{r.dice.DisplayName}を振り直して {r.value}。");
            RefreshBattle();
            if (battle.Outcome == BattleOutcome.Defeat) battleView.ShowContinue("結果へ");
            battleView.SetBusy(false);
            busy = false;
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
                // 錆び賽の自傷で倒れたら、残りは振らない（続けて振ると「戦闘は終わっています」で止まっていた）
                if (battle.Outcome != BattleOutcome.Ongoing || !battle.CanRollMore || die.state != DiceState.Available) break;
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
                log += battle.CanRollMore ? $"　リフレッシュ！ あと {battle.MaxDicePerRound - battle.Rolled.Count} 個振れます。" : "　リフレッシュ！";
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
            string foes = string.Join("・", battle.enemies.Select(e => e.data.displayName).Distinct());
            battle = null;

            if (outcome == BattleOutcome.Defeat)
            {
                ShowResult(false, foes);
                return;
            }
            // 煙玉で逃げた：報酬なしでマップに戻る
            if (outcome == BattleOutcome.Fled)
            {
                DestroyView(battleView);
                battleView = null;
                map.gameObject.SetActive(true);
                map.SetInteractable(true);
                map.Refresh(run);
                map.SetMessage($"煙玉で {enemyName} から逃げた。\n戦闘で使ったダイスは使用済みのままです。");
                SaveRun();
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
            // 通常戦のお守りもその場で手に入る（いっぱいなら持てない）
            if (pendingReward.charm != null)
            {
                if (run.AddCharm(pendingReward.charm)) afterRewardMessage += $"お守り「{pendingReward.charm.displayName}」を手に入れた。";
                else pendingReward.charmRejected = true;
            }

            rewardView = RewardView.Create(canvas.transform, art, pendingReward, rewardGold, config.rewards.skipGold);
            rewardView.DiceChosen += OnRewardDiceChosen;
            rewardView.Skipped += OnRewardSkipped;
            rewardView.ReplaceChosen += OnRewardReplace;
            rewardView.ReplaceCancelled += () => rewardView.ShowChoices();
            rewardView.CharmReplaceClicked += () => { if (!rewardCharmBusy) StartCoroutine(RewardCharmReplaceRoutine()); };
        }

        bool rewardCharmBusy;

        /// <summary>報酬のお守り：いっぱいのとき、持っているお守りを1つ捨てて受け取る。</summary>
        IEnumerator RewardCharmReplaceRoutine()
        {
            var reward = pendingReward;
            if (reward == null || reward.charm == null || !reward.charmRejected) yield break;
            rewardCharmBusy = true;
            var owned = run.Charms.ToList();
            var options = owned.Select(c => new MapView.DialogOption($"「{c.displayName}」を捨てる")).ToList();
            options.Add(new MapView.DialogOption("やめる"));
            int choice = -1;
            yield return map.ShowDialog("お守りを入れ替える", $"どれかを捨てて「{reward.charm.displayName}」を受け取る？\n{reward.charm.description}",
                options, c => choice = c, false, rewardView.transform);
            rewardCharmBusy = false;
            if (choice < 0 || choice >= owned.Count || rewardView == null || pendingReward != reward) yield break;
            run.DiscardCharm(owned[choice]);
            run.AddCharm(reward.charm);
            reward.charmRejected = false;
            afterRewardMessage += $"お守り「{owned[choice].displayName}」を捨てて「{reward.charm.displayName}」を手に入れた。";
            rewardView.SetCharmTaken($"<color=#F2A99E>お守り「{reward.charm.displayName}」</color>を手に入れた（「{owned[choice].displayName}」と入れ替え）");
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
            SaveRun();
        }
    }
}
