using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using UnityEngine;

namespace SaiNoMichi.Run
{
    // ---- セーブの中身（JsonUtility で書けるよう、辞書は使わずリストにする） ----

    [Serializable]
    public class SavedDie
    {
        public string id;
        public int state;
        public int[] values;
        public string[] engravings;   // 面ごとの刻印の id（なければ空文字）
    }

    [Serializable]
    public class SavedTile
    {
        public int id;
        public int type;
        public bool passEffect;
        public bool stopHere;
        public float x, y;
        public int[] next;
    }

    [Serializable]
    public class SavedCharge
    {
        public string relic;
        public int effectIndex;
        public int count;
    }

    [Serializable]
    public class SavedPlan
    {
        public int tile;
        public string enemy;
        public int eventKind;
    }

    [Serializable]
    public class SavedForesight
    {
        public int die;   // ポーチの何番目のダイスか
        public int value;
        public int faceIndex;
    }

    /// <summary>1ランのセーブ。マップで操作を待っているとき（と、戦闘を始める直前）に書く。</summary>
    [Serializable]
    public class RunSave
    {
        // セーブの版。ゲームのデータや仕組みを変えて公開するときに上げると、それより古いセーブは読まずに消す
        // （テストで遊んだ古いセーブが残っていても、最初からの状態で始まる）。2：unityroom で最初に公開した版
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public string runId;          // 記録（CSV）のラン ID
        public string daily;          // 今日の旅なら、その日の印（yyyyMMdd）。ふつうの旅は空
        public int ascension;         // 段位（0〜10）
        public int traveler;          // 旅人（TravelerKind）
        public string savedAt;

        // 乱数
        public int seed;
        public long[] rngCounts;
        public bool hasPlanRng;
        public long planRngCount;

        // 進み具合
        public int layerIndex;
        public int turn;
        public int gold;
        public int currentTile;
        public int movesThisLayer;
        public int removedDiceCount;
        public int lastRolledValue;
        public int lastEnemyHpPercent;
        public int normalBattles;
        public string lastEnemy;
        public int lastEvent = -1;
        public List<int> occurredEvents = new List<int>();
        public bool craneGratitude;   // 鶴の恩返しを待っている
        public int totalSteps;
        public int moveBonusTurns;
        public int moveBonus;
        public int pendingMoveBonus;
        public bool pendingMoveReroll;

        // プレイヤー
        public int hp;
        public int maxHp;
        public int block;
        public int strength;

        // 持ち物
        public int pouchCapacity;
        public List<SavedDie> dice = new List<SavedDie>();
        public List<string> relics = new List<string>();
        public List<SavedCharge> charges = new List<SavedCharge>();
        public List<string> charms = new List<string>();

        // 盤面
        public List<SavedTile> tiles = new List<SavedTile>();
        public int goalTile;
        public List<SavedPlan> plannedEnemies = new List<SavedPlan>();
        public List<SavedPlan> plannedEvents = new List<SavedPlan>();
        public List<SavedForesight> foreseen = new List<SavedForesight>();

        // 成績
        public int goldEarned, battlesWon, elitesWon, bossesWon;
        public int damageDealt, damageTaken; // 古いセーブにはない（0 のまま）
        public List<int> stopTypes = new List<int>();
        public List<int> stopCounts = new List<int>();
        public List<string> diceGained = new List<string>();
        public List<string> diceRemoved = new List<string>();
        public List<string> engravingsLog = new List<string>();
        public List<int> hpByTurn = new List<int>();
        public List<int> maxHpByTurn = new List<int>();
        public List<int> layerByTurn = new List<int>();

        // 戦闘を始める直前のセーブなら、その戦闘（続きからはこの戦闘の最初から）
        public bool hasPendingBattle;
        public string pendingEnemy;
        public bool pendingBoss;
        public int pendingRewardKind;

        public string ToJson() => JsonUtility.ToJson(this);
        public static RunSave FromJson(string json) => JsonUtility.FromJson<RunSave>(json);
    }

    /// <summary>セーブに書いた id から、ダイス・レリックなどのデータを引く（GameConfig に並んでいるものから）。</summary>
    public class SaveRegistry
    {
        readonly Dictionary<string, DiceData> dice = new Dictionary<string, DiceData>();
        readonly Dictionary<string, RelicData> relics = new Dictionary<string, RelicData>();
        readonly Dictionary<string, EngravingData> engravings = new Dictionary<string, EngravingData>();
        readonly Dictionary<string, CharmData> charms = new Dictionary<string, CharmData>();
        readonly Dictionary<string, EnemyData> enemies = new Dictionary<string, EnemyData>();

        public SaveRegistry(GameConfig config)
        {
            AddAll(dice, config.startingDice, d => d.id);
            AddAll(dice, config.starterChoices, d => d.id);
            AddAll(dice, config.rewardDicePool, d => d.id);
            AddAll(dice, config.multiplayerDicePool, d => d.id);
            AddAll(dice, config.travelerDicePool, d => d.id);
            AddAll(dice, config.curseDicePool, d => d.id);
            AddAll(dice, new[] { config.curseDice }, d => d.id);
            AddAll(relics, config.relicPool, r => r.id);
            AddAll(relics, config.multiplayerRelicPool, r => r.id);
            AddAll(relics, config.tourRelicPool, r => r.id);
            AddAll(relics, config.bossRelicPool, r => r.id);
            AddAll(engravings, config.engravingPool, e => e.id);
            AddAll(charms, config.charmPool, c => c.id);
            AddAll(charms, config.multiplayerCharmPool, c => c.id);
            AddAll(enemies, config.battleEnemies, e => e.id);
            AddAll(enemies, config.eliteEnemies, e => e.id);
            AddAll(enemies, new[] { config.boss }, e => e.id);
            foreach (var layer in config.layers)
            {
                if (layer == null) continue;
                AddAll(enemies, layer.battleEnemies, e => e.id);
                AddAll(enemies, layer.eliteEnemies, e => e.id);
                AddAll(enemies, new[] { layer.boss }, e => e.id);
                AddAll(enemies, layer.bossChoices, e => e.id);
            }
        }

        static void AddAll<T>(Dictionary<string, T> map, IEnumerable<T> items, Func<T, string> id) where T : class
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                var key = id(item);
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key)) map[key] = item;
            }
        }

        static T Get<T>(Dictionary<string, T> map, string id, string what) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (map.TryGetValue(id, out var item)) return item;
            throw new InvalidOperationException($"セーブにある{what}「{id}」がデータにありません。");
        }

        public DiceData Dice(string id) => Get(dice, id, "ダイス");
        public RelicData Relic(string id) => Get(relics, id, "レリック");
        public EngravingData Engraving(string id) => Get(engravings, id, "刻印");
        public CharmData Charm(string id) => Get(charms, id, "お守り");
        public EnemyData Enemy(string id) => Get(enemies, id, "敵");
    }

    public partial class RunState
    {
        /// <summary>いまの状態をセーブの形にする（マップで操作を待っているときに呼ぶ）。</summary>
        public RunSave CreateSave()
        {
            if (CurrentBattle != null) throw new InvalidOperationException("戦闘中はセーブできません。");
            var s = new RunSave
            {
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                seed = random.Seed,
                rngCounts = random.Counts,
                ascension = Ascension,
                traveler = (int)Traveler,
                hasPlanRng = planRng != null,
                planRngCount = planRng != null ? planRng.Count : 0,
                layerIndex = LayerIndex,
                turn = Turn,
                gold = Gold,
                currentTile = Current.id,
                movesThisLayer = MovesThisLayer,
                removedDiceCount = RemovedDiceCount,
                lastRolledValue = LastRolledValue,
                lastEnemyHpPercent = LastEnemyHpPercent,
                normalBattles = normalBattles,
                lastEnemy = lastEnemy != null ? lastEnemy.id : null,
                lastEvent = lastEvent.HasValue ? (int)lastEvent.Value : -1,
                occurredEvents = occurredEvents.Select(e => (int)e).ToList(),
                craneGratitude = CraneGratitude,
                totalSteps = TotalSteps,
                moveBonusTurns = MoveBonusTurns,
                moveBonus = MoveBonus,
                pendingMoveBonus = PendingMoveBonus,
                pendingMoveReroll = PendingMoveReroll,
                hp = player.hp,
                maxHp = player.maxHp,
                block = player.block,
                strength = player.strength,
                pouchCapacity = pouch.Capacity,
                relics = relics.Select(r => r.id).ToList(),
                charms = charms.Select(c => c.id).ToList(),
                goalTile = board.Goal.id,
                goldEarned = stats.goldEarned,
                battlesWon = stats.battlesWon,
                damageDealt = stats.damageDealt,
                damageTaken = stats.damageTaken,
                elitesWon = stats.elitesWon,
                bossesWon = stats.bossesWon,
                stopTypes = stats.tilesStopped.Keys.Select(k => (int)k).ToList(),
                stopCounts = stats.tilesStopped.Values.ToList(),
                diceGained = new List<string>(stats.diceGained),
                diceRemoved = new List<string>(stats.diceRemoved),
                engravingsLog = new List<string>(stats.engravings),
                hpByTurn = new List<int>(stats.hpByTurn),
                maxHpByTurn = new List<int>(stats.maxHpByTurn),
                layerByTurn = new List<int>(stats.layerByTurn),
            };
            foreach (var d in pouch.All)
            {
                s.dice.Add(new SavedDie
                {
                    id = d.data != null ? d.data.id : null,
                    state = (int)d.state,
                    values = d.faces.Select(f => f.value).ToArray(),
                    engravings = d.faces.Select(f => f.engraving != null ? f.engraving.id : "").ToArray(),
                });
            }
            foreach (var r in relics)
            {
                for (int i = 0; i < r.effects.Count; i++)
                {
                    var e = r.effects[i];
                    if (e != null && charges.TryGetValue(e, out int n)) s.charges.Add(new SavedCharge { relic = r.id, effectIndex = i, count = n });
                }
            }
            s.tiles.AddRange(SavedTilesOf(board));
            foreach (var kv in plannedEnemies) s.plannedEnemies.Add(new SavedPlan { tile = kv.Key.id, enemy = kv.Value != null ? kv.Value.id : null });
            foreach (var kv in plannedEvents) s.plannedEvents.Add(new SavedPlan { tile = kv.Key.id, eventKind = (int)kv.Value });
            var list = pouch.All.ToList();
            foreach (var kv in foreseen) s.foreseen.Add(new SavedForesight { die = list.IndexOf(kv.Key), value = kv.Value.value, faceIndex = kv.Value.faceIndex });
            return s;
        }

        static IEnumerable<SavedTile> SavedTilesOf(BoardData b) => b.tiles.Select(t => new SavedTile
        {
            id = t.id,
            type = (int)t.type,
            passEffect = t.passEffect,
            stopHere = t.stopHere,
            x = t.position.x,
            y = t.position.y,
            next = t.next.Select(n => n.id).ToArray(),
        });

        // ---- 協力の戦闘：仲間と送り合う写し ----

        /// <summary>
        /// 協力の戦闘の前に仲間へ送る、自分のランの写し（JSON）。盤面と成績は省く（盤面はシードと層から作れる）。
        /// 戦闘の前（戦闘のマスに止まって、敵を決めたあと）に呼ぶ。
        /// </summary>
        public string CreateBattleSnapshot()
        {
            var s = CreateSave();
            s.savedAt = "";
            s.runId = null;
            s.tiles.Clear();
            s.plannedEvents.Clear();
            s.stopTypes.Clear();
            s.stopCounts.Clear();
            s.diceGained.Clear();
            s.diceRemoved.Clear();
            s.engravingsLog.Clear();
            s.hpByTurn.Clear();
            s.maxHpByTurn.Clear();
            s.layerByTurn.Clear();
            return s.ToJson();
        }

        /// <summary>仲間の写しから、協力の戦闘に使うランを作る（盤面はシードと層から作り直す）。</summary>
        public static RunState RestoreBattleSnapshot(GameConfig config, string json)
        {
            var s = RunSave.FromJson(json);
            if (s.tiles == null || s.tiles.Count == 0)
            {
                var b = BoardOfLayer(config, s.seed, s.layerIndex);
                s.tiles = SavedTilesOf(b).ToList();
                s.goalTile = b.Goal.id;
            }
            return Restore(config, s);
        }

        /// <summary>セーブから続きのランを作る。データが見つからないなど読めないときは例外。</summary>
        public static RunState Restore(GameConfig config, RunSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (save.version != RunSave.CurrentVersion) throw new InvalidOperationException($"セーブの版（{save.version}）が違います。");
            return new RunState(config, save, new SaveRegistry(config));
        }

        RunState(GameConfig config, RunSave s, SaveRegistry registry)
        {
            this.config = config;
            random = new RunRandom(s.seed, s.rngCounts);
            LayerIndex = s.layerIndex;
            Ascension = Math.Max(0, Math.Min(AscensionRules.Max, s.ascension));
            Traveler = Enum.IsDefined(typeof(TravelerKind), s.traveler) ? (TravelerKind)s.traveler : TravelerKind.Saifuri;

            // 盤面
            var tiles = s.tiles.Select(t => new TileNode(t.id, (TileType)t.type) { passEffect = t.passEffect, stopHere = t.stopHere, position = new Vector2(t.x, t.y) }).ToList();
            var byId = tiles.ToDictionary(t => t.id);
            for (int i = 0; i < tiles.Count; i++)
            {
                foreach (int n in s.tiles[i].next) tiles[i].next.Add(byId[n]);
            }
            board = new BoardData(tiles, byId[s.goalTile]);
            Current = byId[s.currentTile];

            // プレイヤーと持ち物（手に入れたときの効果はもう反映ずみなので、もう一度は働かせない）
            player = new Combatant(s.hp, s.maxHp) { block = s.block, strength = s.strength };
            pouch.Capacity = s.pouchCapacity;
            foreach (var sd in s.dice)
            {
                var die = new DiceInstance(registry.Dice(sd.id)) { state = (DiceState)sd.state };
                for (int i = 0; i < die.faces.Length && i < sd.values.Length; i++)
                {
                    var engraving = sd.engravings != null && i < sd.engravings.Length ? registry.Engraving(sd.engravings[i]) : null;
                    die.faces[i] = new Face(sd.values[i], engraving);
                }
                pouch.ForceAdd(die);
            }
            foreach (var id in s.relics)
            {
                var relic = registry.Relic(id);
                relics.Add(relic);
                effects.Register(relic);
            }
            foreach (var c in s.charges)
            {
                var relic = relics.FirstOrDefault(r => r.id == c.relic);
                if (relic != null && c.effectIndex < relic.effects.Count && relic.effects[c.effectIndex] != null) charges[relic.effects[c.effectIndex]] = c.count;
            }
            foreach (var id in s.charms) charms.Add(registry.Charm(id));

            // 進み具合
            Gold = s.gold;
            Turn = s.turn;
            MovesThisLayer = s.movesThisLayer;
            RemovedDiceCount = s.removedDiceCount;
            LastRolledValue = s.lastRolledValue;
            LastEnemyHpPercent = s.lastEnemyHpPercent;
            normalBattles = s.normalBattles;
            lastEnemy = registry.Enemy(s.lastEnemy);
            lastEvent = s.lastEvent >= 0 ? (EventKind?)s.lastEvent : null;
            foreach (int e in s.occurredEvents) occurredEvents.Add((EventKind)e);
            RestoreCraneGratitude(s.craneGratitude);
            TotalSteps = s.totalSteps;
            MoveBonusTurns = s.moveBonusTurns;
            MoveBonus = s.moveBonus;
            PendingMoveBonus = s.pendingMoveBonus;
            PendingMoveReroll = s.pendingMoveReroll;

            // 地図師の矢立・千里眼で前もって決めたもの
            if (s.hasPlanRng) planRng = new SeededRandom(RunRandom.Mix(random.Seed, 50 + LayerIndex), s.planRngCount);
            foreach (var p in s.plannedEnemies) plannedEnemies[byId[p.tile]] = registry.Enemy(p.enemy);
            foreach (var p in s.plannedEvents) plannedEvents[byId[p.tile]] = (EventKind)p.eventKind;
            var list = pouch.All.ToList();
            foreach (var f in s.foreseen)
            {
                if (f.die >= 0 && f.die < list.Count) foreseen[list[f.die]] = (f.value, f.faceIndex);
            }

            // 成績
            stats.goldEarned = s.goldEarned;
            stats.battlesWon = s.battlesWon;
            stats.damageDealt = s.damageDealt;
            stats.damageTaken = s.damageTaken;
            stats.elitesWon = s.elitesWon;
            stats.bossesWon = s.bossesWon;
            for (int i = 0; i < s.stopTypes.Count && i < s.stopCounts.Count; i++) stats.tilesStopped[(TileType)s.stopTypes[i]] = s.stopCounts[i];
            stats.diceGained.AddRange(s.diceGained);
            stats.diceRemoved.AddRange(s.diceRemoved);
            stats.engravings.AddRange(s.engravingsLog);
            if (s.hpByTurn != null) stats.hpByTurn.AddRange(s.hpByTurn);
            if (s.maxHpByTurn != null) stats.maxHpByTurn.AddRange(s.maxHpByTurn);
            if (s.layerByTurn != null) stats.layerByTurn.AddRange(s.layerByTurn);

            pouch.Refreshed += OnPouchRefreshed;
        }
    }
}
