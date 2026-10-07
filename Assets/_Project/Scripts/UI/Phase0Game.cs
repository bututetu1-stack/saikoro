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
        [Tooltip("0 なら毎回ランダムなシードで始める")]
        public int fixedSeed;

        const string PlayLogFileName = "phase0_playlog.csv";

        RunState run;
        PlayLog playLog;
        MapView map;
        BattleView battleView;
        ResultView resultView;

        BattleState battle;
        bool bossBattle;
        // 戦闘で「振る」前に選んでいるダイス（表示上の状態）
        readonly List<DiceInstance> selected = new List<DiceInstance>();

        void Start()
        {
            StartNewRun();
        }

        // ---- ラン ----

        void StartNewRun()
        {
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            run = new RunState(config, seed);
            playLog = new PlayLog(PlayLog.NewRunId(), seed);
            Debug.Log($"[Phase0] 新しいラン seed={seed}　記録: {PlayLogPath}");

            CloseAll();
            map = MapView.Create(canvas.transform, run.board);
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
            map = null;
            battleView = null;
            resultView = null;
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
            resultView.RetryClicked += StartNewRun;
        }

        // ---- マップ ----

        void OnDiceHovered(DiceInstance die)
        {
            if (run.ReachedGoal || die.state != DiceState.Available) return;
            map.ShowReach(ReachCalculator.Compute(run.Current, die));
        }

        void OnMapDiceClicked(DiceInstance die)
        {
            if (run.ReachedGoal || die.state != DiceState.Available) return;

            var move = run.Move(die);
            playLog.RecordMove(run.Turn, move);
            FlushPlayLog();
            map.ClearReach();

            string message = $"{die.DisplayName}で {move.value} → マス{move.to.id}（{MapView.TileLabel(move.to)}）";
            if (move.refreshed) message += "　リフレッシュ！";

            switch (move.to.type)
            {
                case TileType.Battle:
                case TileType.Boss:
                    StartBattle(run.PickEnemy(move.to), move.to.type == TileType.Boss);
                    return;
                case TileType.Rest:
                    message += $"\n休憩：HP を {run.Rest()} 回復した。";
                    break;
                default:
                    message += "\n何も起きなかった。";
                    break;
            }
            map.Refresh(run);
            map.SetMessage(message);
        }

        // ---- 戦闘 ----

        void StartBattle(EnemyData enemy, bool isBoss)
        {
            battle = new BattleState(run.player, enemy, run.pouch, run.random.Battle);
            bossBattle = isBoss;
            selected.Clear();

            map.gameObject.SetActive(false);
            battleView = BattleView.Create(canvas.transform);
            battleView.DieClicked += OnBattleDieClicked;
            battleView.RollClicked += OnRollClicked;
            battleView.AssignClicked += OnAssignClicked;
            battleView.ResolveClicked += OnResolveClicked;
            battleView.ContinueClicked += OnBattleContinue;

            battleView.SetLog($"{enemy.displayName} が現れた！　ダイスを選んで「振る」、出目を攻撃か防御に割り振って「決定」。");
            RefreshBattle();
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
            if (battle.Outcome != BattleOutcome.Ongoing || die.state != DiceState.Available) return;
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
            if (selected.Count == 0 || battle.Outcome != BattleOutcome.Ongoing) return;
            var refreshedNames = new List<string>();
            foreach (var die in selected.ToList())
            {
                if (!battle.CanRollMore || die.state != DiceState.Available) break;
                int availableBefore = run.pouch.AvailableCount;
                battle.Roll(die);
                if (availableBefore == 1) refreshedNames.Add(die.DisplayName);
            }
            selected.Clear();

            string log = "出目：" + string.Join("、", battle.Rolled.Select(r => $"{r.dice.DisplayName} {r.value}"));
            if (refreshedNames.Count > 0) log += battle.CanRollMore ? "　リフレッシュ！ もう1個選べます。" : "　リフレッシュ！";
            battleView.SetLog(log);
            RefreshBattle();
        }

        void OnAssignClicked(RolledDie die, Assignment assignment)
        {
            battle.Assign(die, assignment);
            RefreshBattle();
        }

        void OnResolveClicked()
        {
            if (battle.Outcome != BattleOutcome.Ongoing) return;
            var r = battle.Resolve();
            selected.Clear();

            if (battle.Outcome != BattleOutcome.Ongoing)
            {
                playLog.RecordBattle(run.Turn, battle);
                FlushPlayLog();
            }

            string log = (r.rolled.Count == 0 ? "パス。" : "") + $"敵に {r.dealt} ダメージ、自分は {r.taken} ダメージ。";
            switch (battle.Outcome)
            {
                case BattleOutcome.Victory:
                    log += $"\n{battle.enemy.data.displayName} を倒した！（{battle.Round} ラウンド）";
                    battleView.ShowContinue(bossBattle ? "結果へ" : "マップに戻る");
                    break;
                case BattleOutcome.Defeat:
                    log += "\n倒れてしまった……";
                    battleView.ShowContinue("結果へ");
                    break;
            }
            battleView.SetLog(log);
            RefreshBattle();
        }

        void OnBattleContinue()
        {
            if (battle == null || battle.Outcome == BattleOutcome.Ongoing) return;
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
            map.gameObject.SetActive(true);
            map.Refresh(run);
            map.SetMessage($"{enemyName} に勝った（{rounds} ラウンド）。戦闘で使ったダイスは使用済みのままです。");
        }
    }
}
