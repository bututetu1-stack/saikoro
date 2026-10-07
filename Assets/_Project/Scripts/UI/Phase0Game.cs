using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>フェーズ0のゲーム進行。RunState を呼び、結果を各画面に表示する。</summary>
    public class Phase0Game : MonoBehaviour
    {
        public Phase0Config config;
        public Canvas canvas;
        [Tooltip("0 なら毎回ランダムなシードで始める")]
        public int fixedSeed;

        RunState run;
        MapView map;

        void Start()
        {
            StartNewRun();
        }

        void StartNewRun()
        {
            int seed = fixedSeed != 0 ? fixedSeed : new System.Random().Next(1, int.MaxValue);
            run = new RunState(config, seed);
            Debug.Log($"[Phase0] 新しいラン seed={seed}");

            if (map != null) Destroy(map.gameObject);
            map = MapView.Create(canvas.transform, run.board);
            map.DiceHovered += OnDiceHovered;
            map.DiceUnhovered += map.ClearReach;
            map.DiceClicked += OnDiceClicked;

            map.Refresh(run);
            map.SetMessage($"シード {seed}　ダイスにマウスを乗せると、止まりうるマスが光ります。クリックで振って進みます。");
        }

        void OnDiceHovered(DiceInstance die)
        {
            if (run.ReachedGoal || die.state != DiceState.Available) return;
            map.ShowReach(ReachCalculator.Compute(run.Current, die));
        }

        void OnDiceClicked(DiceInstance die)
        {
            if (run.ReachedGoal) return;

            var move = run.Move(die);
            map.ClearReach();

            string message = $"{die.DisplayName}で {move.value} → マス{move.to.id}（{MapView.TileLabel(move.to)}）";
            if (move.refreshed) message += "　リフレッシュ！";
            message += "\n" + ResolveTile(move.to);

            map.Refresh(run);
            map.SetMessage(message);
        }

        /// <summary>止まったマスの効果。戦闘とボス戦はステップ6で戦闘画面につなぐ。</summary>
        string ResolveTile(TileNode tile)
        {
            switch (tile.type)
            {
                case TileType.Rest:
                    int healed = run.Rest();
                    return $"休憩：HP を {healed} 回復した。";
                case TileType.Battle:
                    return $"戦闘マス：{run.PickEnemy(tile).displayName} が現れた！（戦闘画面はステップ6で作ります）";
                case TileType.Boss:
                    map.SetInteractable(false);
                    return $"ゴール！ {run.PickEnemy(tile).displayName} が待ち構えている。（ボス戦はステップ6で作ります）";
                default:
                    return "何も起きなかった。";
            }
        }
    }
}
