using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    /// <summary>フェーズ2のレリックのための仕組み（数値の加算・地図師の矢立・千里眼・貯金箱）。</summary>
    public partial class RunState
    {
        // ---- 数値の加算（薬草袋・毒壺・鍛冶の金槌） ----

        /// <summary>持っているレリックの、その数値の加算の合計。</summary>
        public int StatBonus(RunStat stat) => effects.All<StatBonusEffect>().Where(e => e.stat == stat).Sum(e => e.amount);

        // ---- 貯金箱 ----

        /// <summary>このランで進んだ歩数の合計。</summary>
        public int TotalSteps { get; private set; }

        void CountStep()
        {
            TotalSteps++;
            effects.Fire(new EffectContext(Trigger.OnStep) { run = this, player = player, amount = TotalSteps });
        }

        // ---- 地図師の矢立：イベントと敵の中身がマップで見える ----

        readonly Dictionary<TileNode, EnemyData> plannedEnemies = new Dictionary<TileNode, EnemyData>();
        readonly Dictionary<TileNode, EventKind> plannedEvents = new Dictionary<TileNode, EventKind>();
        System.Random planRng;

        /// <summary>マスの中身（敵・イベント）が見えるか（地図師の矢立）。</summary>
        public bool CanSeeContents => effects.Has<RevealMapEffect>();

        System.Random PlanRng => planRng ?? (planRng = new System.Random(RunRandom.Mix(random.Seed, 50 + LayerIndex)));

        void ResetPlans()
        {
            plannedEnemies.Clear();
            plannedEvents.Clear();
            planRng = null;
        }

        /// <summary>
        /// そのマスで戦う敵を前もって決めて返す（止まったらこの敵が出る）。戦闘・エリートのマス以外は null。
        /// 見るまで決めないので、地図師の矢立を持っていないときの敵の出方は変わらない。
        /// </summary>
        public EnemyData PeekEnemy(TileNode tile)
        {
            if (tile == null || (tile.type != TileType.Battle && tile.type != TileType.Elite)) return null;
            if (plannedEnemies.TryGetValue(tile, out var planned)) return planned;
            var layer = Layer;
            List<EnemyData> candidates;
            if (tile.type == TileType.Elite && layer.eliteEnemies.Count > 0) candidates = layer.eliteEnemies;
            else
            {
                // TODO(仕様): 前もって決める敵は「最初の数戦は弱い敵」「2戦続けない」を、スタートからの近さだけで真似る
                bool early = board.DistanceToGoal(board.Start) - board.DistanceToGoal(tile) <= 12;
                candidates = early ? layer.battleEnemies.Where(e => e.earlyOk).ToList() : layer.battleEnemies;
                if (candidates.Count == 0) candidates = layer.battleEnemies;
            }
            if (candidates.Count == 0) return null;
            planned = candidates[PlanRng.Next(candidates.Count)];
            plannedEnemies[tile] = planned;
            return planned;
        }

        /// <summary>そのマスで起きるイベントを前もって決めて返す。イベントのマス以外は null。</summary>
        public EventKind? PeekEvent(TileNode tile)
        {
            if (tile == null || tile.type != TileType.Event) return null;
            if (plannedEvents.TryGetValue(tile, out var planned)) return planned;
            var kinds = AvailableEvents();
            if (kinds.Count == 0) return null;
            planned = kinds[PlanRng.Next(kinds.Count)];
            plannedEvents[tile] = planned;
            return planned;
        }

        /// <summary>前もって決めた敵があれば取り出す。</summary>
        bool TakePlannedEnemy(TileNode tile, out EnemyData enemy)
        {
            if (tile != null && plannedEnemies.TryGetValue(tile, out enemy))
            {
                plannedEnemies.Remove(tile);
                return true;
            }
            enemy = null;
            return false;
        }

        bool TakePlannedEvent(TileNode tile, out EventKind kind)
        {
            if (tile != null && plannedEvents.TryGetValue(tile, out kind))
            {
                plannedEvents.Remove(tile);
                return true;
            }
            kind = default;
            return false;
        }

        // ---- 千里眼：移動で振る前に出目が見える ----

        readonly Dictionary<DiceInstance, (int value, int faceIndex)> foreseen = new Dictionary<DiceInstance, (int, int)>();

        /// <summary>移動で振る前に出目が見えるか（千里眼）。</summary>
        public bool HasForesight => effects.Has<ForesightEffect>();

        /// <summary>
        /// 千里眼：このダイスを移動で振ったときの出目（面の値。効果の前）を前もって決めて返す。千里眼がなければ null。
        /// 決めた出目は、そのダイスを移動で振ったときにそのまま出る。
        /// </summary>
        public int? ForeseeRoll(DiceInstance die)
        {
            if (!HasForesight || die == null || !pouch.All.Contains(die)) return null;
            if (!foreseen.TryGetValue(die, out var roll))
            {
                int value = DiceRoller.Roll(die, random.Move, LastRolledValue, out int faceIndex);
                roll = (value, faceIndex);
                foreseen[die] = roll;
            }
            return roll.value;
        }

        /// <summary>移動で振る：千里眼で決めた出目があればそれ、なければ今振る。</summary>
        int RollMoveDie(DiceInstance die, out int faceIndex)
        {
            if (foreseen.TryGetValue(die, out var roll))
            {
                foreseen.Remove(die);
                faceIndex = roll.faceIndex;
                return roll.value;
            }
            return DiceRoller.Roll(die, random.Move, LastRolledValue, out faceIndex);
        }
    }
}
