using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
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

        /// <summary>今の層のボス（候補が複数あれば、シードと層で1体に決まる。続きからでも同じ）。</summary>
        public EnemyData LayerBoss => BossOf(LayerIndex);

        /// <summary>i 番目の層のボス。候補がなければ LayerData.boss。</summary>
        public EnemyData BossOf(int layerIndex)
        {
            var layer = LayerAt(layerIndex);
            var choices = layer.bossChoices.Where(b => b != null).ToList();
            if (choices.Count == 0) return layer.boss;
            // ほかの乱数（マップ・戦闘・報酬）の進み具合に左右されないよう、シードと層の番号だけで決める
            var rng = new Random(unchecked(random.Seed * 31 + layerIndex * 7919 + 17));
            return choices[rng.Next(choices.Count)];
        }

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

        BoardData GenerateBoard() => GenerateBoard(config, random.Map, Layer, Ascension);

        /// <remarks>段位8：罠マスが1.5倍出る。段位9：ボスの手前の休憩・ショップを固定しない。</remarks>
        static BoardData GenerateBoard(GameConfig config, System.Random map, LayerData layer, int ascension)
        {
            if (!config.useBranchingBoard) return BoardGenerator.GenerateLinear(map, config.board);
            var settings = layer.board;
            if (ascension >= 8)
            {
                // 設定のアセットは変えずに、写しの重みを変える
                settings = UnityEngine.JsonUtility.FromJson<LayerBoardSettings>(UnityEngine.JsonUtility.ToJson(layer.board));
                for (int i = 0; i < settings.weights.Count; i++)
                {
                    var w = settings.weights[i];
                    if (w.type != TileType.Trap) continue;
                    w.weight = w.weight * AscensionRules.TrapWeightPercent / 100;
                    settings.weights[i] = w;
                }
            }
            return BranchBoardGenerator.Generate(map, settings, ascension < 9);
        }

        /// <summary>
        /// シード seed のランの、layerIndex 番目（0から）の層の盤面（対戦の観戦で、相手のいる層の盤面を作る）。
        /// マップの乱数は盤面づくりにしか使わないので、同じシードなら、だれのランでも同じ盤面になる。
        /// </summary>
        public static BoardData BoardOfLayer(GameConfig config, int seed, int layerIndex, int ascension = 0)
        {
            var map = new RunRandom(seed).Map;
            BoardData board = null;
            for (int i = 0; i <= Math.Max(0, layerIndex); i++)
            {
                var layer = config.layers.Count > 0
                    ? config.layers[Math.Min(i, config.layers.Count - 1)]
                    : new LayerData { board = config.layerBoard };
                board = GenerateBoard(config, map, layer, ascension);
            }
            return board;
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
            ResetPlans();
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
