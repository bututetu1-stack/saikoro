using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// フェーズ0のゲーム進行。マップ → （戦闘）→ マップ …… → 結果 を切り替え、RunState / BattleState の結果を各画面に表示する。
    /// </summary>
    public class Phase0Game : MonoBehaviour
    {
        public Phase0Config config;
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
            playLog = new PlayLog(PlayLog.NewRunId(), seed);
            Debug.Log($"[Phase0] 新しいラン seed={seed}　記録: {PlayLogPath}");

            CloseAll();
            busy = false;
            map = MapView.Create(canvas.transform, run.board, art);
            map.DiceHovered += OnDiceHovered;
            map.DiceUnhovered += map.ClearReach;
            map.DiceClicked += OnMapDiceClicked;

            map.Refresh(run);
            map.SetMessage($"シード {seed}　ダイスにマウスを乗せると、止まりうるマスが光ります。クリックで振って進みます。");
        }

        void CloseAll()
        {
            DestroyView(map);
            DestroyView(battleView);
            DestroyView(resultView);
            DestroyView(starterView);
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
                Debug.LogWarning($"[Phase0] 遊んだ記録を書き込めませんでした: {e.Message}");
            }
        }

        void ShowResult(bool cleared)
        {
            playLog.RecordResult(run.Turn, cleared, run.player.hp, run.player.maxHp);
            FlushPlayLog();
            CloseAll();
            resultView = ResultView.Create(canvas.transform, cleared, run.Turn, run.player.hp, run.player.maxHp, run.random.Seed);
            resultView.RetryClicked += ShowStarterSelect;
        }

        // ---- マップ ----

        void OnDiceHovered(DiceInstance die)
        {
            if (busy || run.ReachedGoal || die.state != DiceState.Available) return;
            map.ShowReach(ReachCalculator.Compute(run.Current, die));
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
            var move = run.Move(die);
            playLog.RecordMove(run.Turn, move);
            FlushPlayLog();

            map.ClearReach();
            map.SetInteractable(false);
            map.SetMessage($"{die.DisplayName}を振った……");

            yield return map.PlayRoll(die, move.value);
            yield return map.PlayMove(move.passed.Append(move.to));
            map.HideRoll();

            map.RefreshStatus(run);
            map.RefreshTray(run.pouch);
            if (move.refreshed) yield return map.PlayRefresh();

            string message = $"{die.DisplayName}で {move.value} → マス{move.to.id}（{MapView.TileLabel(move.to)}）";
            if (move.refreshed) message += "　リフレッシュ！";

            // TODO(仕様): 出目0で動けなかったときは、今いるマスの効果をもう一度は起こさない
            var landedType = move.to == move.from ? (TileType?)null : move.to.type;
            switch (landedType)
            {
                case null:
                    message += "\n動けなかった。";
                    break;
                case TileType.Battle:
                case TileType.Boss:
                    map.SetMessage(message + "\n敵が現れた！");
                    yield return UIAnim.Wait(0.5f);
                    busy = false;
                    StartBattle(run.PickEnemy(move.to), move.to.type == TileType.Boss);
                    yield break;
                case TileType.Rest:
                    message += $"\n休憩：HP を {run.Rest()} 回復した。";
                    break;
                default:
                    message += "\n何も起きなかった。";
                    break;
            }
            map.RefreshStatus(run);
            map.SetMessage(message);
            map.SetInteractable(true);
            busy = false;
        }

        // ---- 戦闘 ----

        void StartBattle(EnemyData enemy, bool isBoss)
        {
            battle = new BattleState(run.player, enemy, run.pouch, run.random.Battle, run.effects);
            bossBattle = isBoss;
            selected.Clear();

            map.gameObject.SetActive(false);
            battleView = BattleView.Create(canvas.transform, art, enemy, isBoss);
            battleView.DieClicked += OnBattleDieClicked;
            battleView.RollClicked += OnRollClicked;
            battleView.AssignClicked += OnAssignClicked;
            battleView.ResolveClicked += OnResolveClicked;
            battleView.ContinueClicked += OnBattleContinue;

            battleView.SetLog($"{enemy.displayName} が現れた！\nダイスを選んで「振る」、出目を攻撃か防御に割り振って「決定」。");
            RefreshBattle();
            StartCoroutine(battleView.PlayRoundStart(battle));
        }

        void RefreshBattle()
        {
            // 使用済みになった・数が合わない選択は外す
            selected.RemoveAll(d => d.state != DiceState.Available);
            int slots = battle.MaxDicePerRound - battle.Rolled.Count;
            if (selected.Count > slots) selected.RemoveRange(slots, selected.Count - slots);

            battleView.Refresh(battle, selected);
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
                int availableBefore = run.pouch.AvailableCount;
                battle.Roll(die);
                if (availableBefore == 1) refreshed = true;
            }
            selected.Clear();

            battleView.SetLog("ダイスを振った……");
            RefreshBattle();
            yield return battleView.PlayRoll(battle, battle.Rolled.Count - rolledBefore);

            string log = "出目：" + string.Join("、", battle.Rolled.Select(r => $"{r.dice.DisplayName} {r.value}"));
            if (refreshed) log += battle.CanRollMore ? "　リフレッシュ！ もう1個選べます。" : "　リフレッシュ！";
            battleView.SetLog(log + "\n出目ごとに「攻撃」か「防御」を選んで「決定」。");
            RefreshBattle();

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
            if (bossBattle)
            {
                ShowResult(true);
                return;
            }

            DestroyView(battleView);
            battleView = null;
            ShowReward(RewardKind.Normal, $"{enemyName} に勝った（{rounds} ラウンド）。");
        }

        // ---- 報酬 ----

        BattleReward pendingReward;
        int rewardGold;
        string afterRewardMessage;

        /// <summary>報酬画面：ゴールドはここで受け取り、ダイスは選ぶかスキップする。</summary>
        void ShowReward(RewardKind kind, string message)
        {
            pendingReward = run.CreateBattleReward(kind);
            rewardGold = run.GainGold(pendingReward.gold);
            afterRewardMessage = message;

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

            map.gameObject.SetActive(true);
            map.SetInteractable(true);
            map.Refresh(run);
            map.SetMessage($"{afterRewardMessage}{message}\n戦闘で使ったダイスは使用済みのままです。");
        }
    }
}
