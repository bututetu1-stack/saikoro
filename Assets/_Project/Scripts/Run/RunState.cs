using System;
using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Run
{
    public struct MoveResult
    {
        public DiceInstance dice;
        public int value;
        public TileNode from;
        public TileNode to;
        public IReadOnlyList<TileNode> passed;
        public bool refreshed;
        public int availableBefore;   // 振る前に使用可能だったダイスの数（記録用）
        public IReadOnlyList<DiceInstance> availableDiceBefore;   // 振る前に使用可能だったダイス（記録用）
    }

    /// <summary>1ラン（フェーズ0では1層）の状態。表示には依存しない。</summary>
    public class RunState
    {
        public readonly Phase0Config config;
        public readonly RunRandom random;
        public readonly BoardData board;
        public readonly DicePouch pouch = new DicePouch();
        public readonly Combatant player;

        public TileNode Current { get; private set; }
        public int Turn { get; private set; }
        public bool ReachedGoal => Current == board.Goal;
        public int TilesToGoal => board.Goal.id - Current.id;

        public RunState(Phase0Config config, int seed)
        {
            this.config = config;
            random = new RunRandom(seed);
            board = BoardGenerator.GenerateLinear(random.Map, config.board);
            player = new Combatant(config.playerMaxHp);
            foreach (var data in config.startingDice) pouch.Add(new DiceInstance(data));
            Current = board.Start;
        }

        /// <summary>ダイスを1個振って進む（1ターン）。ダイスは使用済みになる。</summary>
        public MoveResult Move(DiceInstance die)
        {
            if (ReachedGoal) throw new InvalidOperationException("ゴールに着いているので進めません。");

            var availableDiceBefore = new List<DiceInstance>(pouch.Available);
            int availableBefore = availableDiceBefore.Count;
            int value = die.Roll(random.Move);
            bool refreshed = pouch.Use(die); // 使用可能でなければここで例外

            var passed = new List<TileNode>();
            var from = Current;
            Current = BoardData.Advance(from, value, passed);
            Turn++;

            return new MoveResult
            {
                dice = die,
                value = value,
                from = from,
                to = Current,
                passed = passed,
                refreshed = refreshed,
                availableBefore = availableBefore,
                availableDiceBefore = availableDiceBefore,
            };
        }

        /// <summary>休憩：最大HPの restHealPercent% を回復（切り捨て）。実際に回復した量を返す。</summary>
        public int Rest()
        {
            int before = player.hp;
            player.Heal(player.maxHp * config.restHealPercent / 100);
            return player.hp - before;
        }

        /// <summary>そのマスで戦う敵。戦闘マスは候補から等確率、ボスマスはボス。それ以外は null。</summary>
        public EnemyData PickEnemy(TileNode tile)
        {
            switch (tile.type)
            {
                case TileType.Battle:
                    return config.battleEnemies[random.Battle.Next(config.battleEnemies.Count)];
                case TileType.Boss:
                    return config.boss;
                default:
                    return null;
            }
        }
    }
}
