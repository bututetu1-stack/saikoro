using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    /// <summary>層をクリアしたときに起きたこと（画面の表示用）。</summary>
    public struct LayerClearResult
    {
        public int healed;
        public int diceRestored;
    }

    /// <summary>層（仕様書 第2章：3層構成。各層のボスを倒すと次の層へ）。</summary>
    public partial class RunState
    {
        /// <summary>いまの層（0 始まり）。</summary>
        public int LayerIndex { get; private set; }

        public int LayerCount => config.layers.Count > 0 ? config.layers.Count : 1;
        public bool IsFinalLayer => LayerIndex >= LayerCount - 1;

        /// <summary>いまの層の中身。</summary>
        public LayerData Layer => LayerAt(LayerIndex);

        LayerData legacyLayer;

        /// <summary>i 番目の層。層が設定されていなければ、昔の1層ぶんの設定（battleEnemies・layerBoard など）から作る。</summary>
        public LayerData LayerAt(int i)
        {
            if (config.layers.Count > 0) return config.layers[Math.Max(0, Math.Min(i, config.layers.Count - 1))];
            return legacyLayer ?? (legacyLayer = new LayerData
            {
                displayName = "野原の街道",
                board = config.layerBoard,
                battleEnemies = config.battleEnemies,
                eliteEnemies = config.eliteEnemies,
                boss = config.boss,
                earlyBattleCount = config.earlyBattleCount,
            });
        }

        BoardData GenerateBoard()
        {
            return config.useBranchingBoard
                ? BranchBoardGenerator.Generate(random.Map, Layer.board)
                : BoardGenerator.GenerateLinear(random.Map, config.board);
        }

        /// <summary>層を移ったとき（盤面ができたあと）。画面の作り直し用。</summary>
        public event Action LayerStarted;

        /// <summary>
        /// 次の層へ進む：新しい盤面を作ってスタートへ。HP を最大HPの30%回復し、全ダイスを使用可能に戻す（仕様書 第2章）。
        /// 「層ごと」の効果（草鞋の回数・早馬・敵の出方）も戻す。最後の層では何もしない。
        /// </summary>
        public LayerClearResult AdvanceLayer()
        {
            if (IsFinalLayer) throw new InvalidOperationException("最後の層です。");
            var result = new LayerClearResult();

            int before = player.hp;
            player.Heal(player.maxHp * config.layerClearHealPercent / 100);
            result.healed = player.hp - before;
            foreach (var d in pouch.All)
            {
                if (d.state != DiceState.Available)
                {
                    d.state = DiceState.Available;
                    result.diceRestored++;
                }
            }

            LayerIndex++;
            board = GenerateBoard();
            Current = board.Start;
            MovesThisLayer = 0;
            normalBattles = 0;
            lastEnemy = null;
            foreach (var effect in charges.Keys.ToList())
            {
                if (effect is ChargedMoveAdjustEffect charged) charges[effect] = charged.chargesPerLayer;
            }

            effects.Fire(new EffectContext(Trigger.OnLayerStart) { run = this, player = player });
            LayerStarted?.Invoke();
            return result;
        }
    }
}
