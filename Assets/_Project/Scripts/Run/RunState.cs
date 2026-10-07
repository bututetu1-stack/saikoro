using System;
using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

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
        public readonly EffectBus effects = new EffectBus();
        readonly List<RelicData> relics = new List<RelicData>();

        public int Gold { get; private set; }
        public IReadOnlyList<RelicData> Relics => relics;
        public TileNode Current { get; private set; }
        public int Turn { get; private set; }
        public bool ReachedGoal => Current == board.Goal;
        public int TilesToGoal => board.DistanceToGoal(Current);

        /// <param name="starter">スターターダイス（初期ポーチの最後に加える）。null なら加えない。</param>
        public RunState(Phase0Config config, int seed, DiceData starter = null)
        {
            this.config = config;
            random = new RunRandom(seed);
            board = config.useBranchingBoard
                ? BranchBoardGenerator.Generate(random.Map, config.layerBoard)
                : BoardGenerator.GenerateLinear(random.Map, config.board);
            player = new Combatant(config.playerMaxHp);
            foreach (var data in config.startingDice) pouch.Add(new DiceInstance(data));
            if (starter != null) pouch.Add(new DiceInstance(starter));
            Gold = config.startingGold;
            Current = board.Start;
        }

        // ---- ゴールド ----

        /// <summary>ゴールドを得る。OnGoldGain の効果（銭袋など）で量が変わる。実際に得た量を返す。</summary>
        public int GainGold(int amount)
        {
            if (amount <= 0) return 0;
            var ctx = effects.Fire(new EffectContext(Trigger.OnGoldGain) { run = this, player = player, amount = amount });
            int gained = System.Math.Max(0, ctx.amount);
            Gold += gained;
            return gained;
        }

        /// <summary>ゴールドを払う。足りなければ払わずに false。</summary>
        public bool SpendGold(int amount)
        {
            if (amount < 0 || Gold < amount) return false;
            Gold -= amount;
            return true;
        }

        // ---- 報酬とダイスの出し入れ ----

        /// <summary>戦闘報酬を決める（報酬用の乱数を使う）。ゴールドはまだ受け取らない。</summary>
        public BattleReward CreateBattleReward(RewardKind kind)
        {
            return RewardGenerator.ForBattle(kind, random.Reward, config.rewards, config.rewardDicePool);
        }

        public bool CanAddDice => !pouch.IsFull;

        /// <summary>ダイスをポーチに加える（使用可能の状態で入る）。満杯なら例外。</summary>
        public DiceInstance AddDice(DiceData data)
        {
            var die = new DiceInstance(data);
            pouch.Add(die);
            return die;
        }

        /// <summary>満杯のとき：old を手放して data を受け取る。</summary>
        public DiceInstance ReplaceDice(DiceInstance old, DiceData data)
        {
            if (old.data != null && old.data.rarity == Rarity.Curse) throw new InvalidOperationException("呪いのダイスは入れ替えられません。");
            pouch.Remove(old);
            return AddDice(data);
        }

        /// <summary>ダイスの報酬をスキップすると、代わりにゴールドを得る（仕様書 第11章）。</summary>
        public int SkipDiceReward() => GainGold(config.rewards.skipGold);

        // ---- レリック ----

        public void AddRelic(RelicData relic)
        {
            if (relic == null) return;
            relics.Add(relic);
            effects.Register(relic);
        }

        public bool HasRelic(string id) => relics.Exists(r => r.id == id);

        /// <summary>ダイスを1個振って進む（1ターン）。ダイスは使用済みになる。</summary>
        /// <summary>
        /// ダイスを1個振って進む（1ターン）。ダイスは使用済みになる。
        /// 分岐では chooseBranch で道を選ぶ（省略時は最初の道）。画面で1歩ずつ見せるときは BeginMove / StepMove / FinishMove を使う。
        /// </summary>
        public MoveResult Move(DiceInstance die, Func<TileNode, IReadOnlyList<TileNode>, TileNode> chooseBranch = null)
        {
            var move = BeginMove(die);
            while (!move.Done)
            {
                TileNode choice = null;
                if (NeedsBranchChoice(move)) choice = chooseBranch != null ? chooseBranch(Current, Current.next) : Current.next[0];
                StepMove(move, choice);
            }
            return FinishMove(move);
        }

        /// <summary>移動の途中の状態。</summary>
        public class MoveInProgress
        {
            public DiceInstance dice;
            public int value;
            public int remaining;
            public TileNode from;
            public bool refreshed;
            public List<DiceInstance> availableDiceBefore;
            public readonly List<TileNode> passed = new List<TileNode>();
            public bool Done => remaining <= 0;
        }

        /// <summary>移動を始める：ダイスを振って使用済みにし、ターンを進める。まだ1歩も動かない。</summary>
        public MoveInProgress BeginMove(DiceInstance die)
        {
            if (ReachedGoal) throw new InvalidOperationException("ゴールに着いているので進めません。");

            var availableDiceBefore = new List<DiceInstance>(pouch.Available);
            int value = die.Roll(random.Move);
            bool refreshed = pouch.Use(die); // 使用可能でなければここで例外
            Turn++;

            return new MoveInProgress
            {
                dice = die,
                value = value,
                remaining = value,
                from = Current,
                refreshed = refreshed,
                availableDiceBefore = availableDiceBefore,
            };
        }

        /// <summary>今いるマスが分岐点で、まだ進む歩数が残っているか（道を選ぶ必要があるか）。</summary>
        public bool NeedsBranchChoice(MoveInProgress move) => !move.Done && Current.IsBranch;

        /// <summary>
        /// 1歩進む。分岐点では next を選ぶ（分岐で選ぶこと自体は歩数を使わない）。
        /// ゴール・ボスマスに入ったら、歩数が余っていてもそこで止まる。
        /// </summary>
        public TileNode StepMove(MoveInProgress move, TileNode next = null)
        {
            if (move.Done) return Current;
            if (Current.IsEnd)
            {
                move.remaining = 0;
                return Current;
            }
            if (next == null || !Current.next.Contains(next)) next = Current.next[0];

            if (Current != move.from) move.passed.Add(Current);
            Current = next;
            move.remaining--;
            if (Current.type == TileType.Boss || Current.IsEnd) move.remaining = 0;
            return Current;
        }

        public MoveResult FinishMove(MoveInProgress move)
        {
            return new MoveResult
            {
                dice = move.dice,
                value = move.value,
                from = move.from,
                to = Current,
                passed = move.passed,
                refreshed = move.refreshed,
                availableBefore = move.availableDiceBefore.Count,
                availableDiceBefore = move.availableDiceBefore,
            };
        }

        // ---- マスの中身 ----

        /// <summary>休憩の「休む」で回復する量（最大HPの restHealPercent%、切り捨て）。</summary>
        public int RestHealAmount => player.maxHp * config.restHealPercent / 100;

        /// <summary>罠：ダメージ・封印・呪いのどれか（ランダム。開発者の判断でイベント系はランダムでよい）。</summary>
        public TrapResult TriggerTrap()
        {
            var s = config.tiles;
            var kind = (TrapKind)random.Map.Next(3);
            // TODO(仕様): 呪いのダイスを入れられない（ポーチが満杯・データがない）ときや、封印できるダイスがないときはダメージにする
            if (kind == TrapKind.Curse && (config.curseDice == null || pouch.IsFull)) kind = TrapKind.Damage;
            if (kind == TrapKind.Seal && SealCandidate() == null) kind = TrapKind.Damage;

            var result = new TrapResult { kind = kind };
            switch (kind)
            {
                case TrapKind.Seal:
                    // 封印は次の戦闘が終わるまで（戦闘終了で解除される）
                    result.sealedDie = SealCandidate();
                    result.sealedDie.state = DiceState.Sealed;
                    pouch.RefreshIfEmpty();
                    result.message = $"罠だ！ {result.sealedDie.DisplayName} が封じられた（次の戦闘が終わるまで）。";
                    break;
                case TrapKind.Curse:
                    result.curseDie = AddDice(config.curseDice);
                    result.message = $"罠だ！ 呪いの {config.curseDice.displayName} を押し付けられた。";
                    break;
                default:
                    result.damage = player.TakeAttack(s.trapDamage);
                    result.message = $"罠だ！ {result.damage} ダメージを受けた。";
                    break;
            }
            return result;
        }

        /// <summary>罠・敵の封印の対象：封印されていないダイスのうち、出目の平均が最も高いもの。</summary>
        DiceInstance SealCandidate()
        {
            DiceInstance best = null;
            double bestAverage = double.MinValue;
            foreach (var d in pouch.All)
            {
                if (d.state == DiceState.Sealed) continue;
                double average = 0;
                foreach (var f in d.faces) average += f.value;
                average /= d.faces.Length;
                if (average > bestAverage)
                {
                    best = d;
                    bestAverage = average;
                }
            }
            return best;
        }

        /// <summary>宝箱：ゴールドかレリック（半々）。まれにアンコモン以上のダイスが付いてくる（持っていくかは選ぶ）。</summary>
        public TreasureResult OpenTreasure()
        {
            var s = config.tiles;
            var rng = random.Reward;
            var result = new TreasureResult();

            var relicCandidates = config.relicPool.FindAll(r => r != null && !relics.Contains(r));
            if (relicCandidates.Count > 0 && rng.Next(100) < s.treasureRelicPercent)
            {
                result.relic = relicCandidates[rng.Next(relicCandidates.Count)];
                AddRelic(result.relic);
                result.message = $"宝箱を開けた！ レリック「{result.relic.displayName}」を手に入れた。";
            }
            else
            {
                result.gold = GainGold(rng.Next(s.treasureGoldMin, s.treasureGoldMax + 1));
                result.message = $"宝箱を開けた！ {result.gold} G を手に入れた。";
            }

            if (rng.Next(100) < s.treasureDicePercent)
            {
                var offer = RewardGenerator.PickDice(rng, new[] { 0, 75, 25 }, config.rewardDicePool, 1);
                if (offer.Count > 0) result.diceOffer = offer[0];
            }
            return result;
        }

        /// <summary>
        /// 通過マス（祠・関所・茶屋・賽場）の効果。stopped なら2倍（仕様書 第8章）。
        /// 通過・停止のどちらでも、OnPassTile / OnStopTile の効果も発動する。
        /// </summary>
        public PassTileResult ApplyPassTile(TileNode tile, bool stopped)
        {
            var s = config.tiles;
            int m = stopped ? 2 : 1;
            var result = new PassTileResult();
            string verb = stopped ? "に止まった" : "を通った";
            switch (tile.type)
            {
                case TileType.Shrine:
                    result.gold = GainGold(s.shrineGold * m);
                    result.message = $"祠{verb}：{result.gold} G を得た。";
                    break;
                case TileType.Checkpoint:
                    int toll = s.checkpointToll * m;
                    if (SpendGold(toll))
                    {
                        result.gold = -toll;
                        result.message = $"関所{verb}：{toll} G を払った。";
                    }
                    else
                    {
                        result.damage = player.TakeAttack(s.checkpointDamage * m);
                        result.message = $"関所{verb}：払えないので {result.damage} ダメージ。";
                    }
                    break;
                case TileType.Teahouse:
                    int before = player.hp;
                    player.Heal(s.teahouseHeal * m);
                    result.healed = player.hp - before;
                    result.message = $"茶屋{verb}：HP を {result.healed} 回復した。";
                    break;
                case TileType.DiceHall:
                    // TODO(仕様): 戻すダイスは選ばせず、使用済みのうち出目の平均が最も高いものから戻す
                    for (int i = 0; i < s.diceHallReturn * m; i++)
                    {
                        DiceInstance best = null;
                        double bestAverage = double.MinValue;
                        foreach (var d in pouch.All)
                        {
                            if (d.state != DiceState.Used) continue;
                            double average = 0;
                            foreach (var f in d.faces) average += f.value;
                            average /= d.faces.Length;
                            if (average > bestAverage) { best = d; bestAverage = average; }
                        }
                        if (best == null) break;
                        best.state = DiceState.Available;
                        result.returnedDice.Add(best);
                    }
                    result.message = result.returnedDice.Count > 0
                        ? $"賽場{verb}：{string.Join("・", result.returnedDice.ConvertAll(d => d.DisplayName))} が使えるようになった。"
                        : $"賽場{verb}：使用済みのダイスがない。";
                    break;
                default:
                    result.message = "";
                    break;
            }
            effects.Fire(new EffectContext(stopped ? Trigger.OnStopTile : Trigger.OnPassTile) { run = this, player = player, tile = tile });
            return result;
        }

        /// <summary>休憩：最大HPの restHealPercent% を回復（切り捨て）。実際に回復した量を返す。</summary>
        public int Rest()
        {
            int before = player.hp;
            player.Heal(RestHealAmount);
            return player.hp - before;
        }

        int normalBattles;
        EnemyData lastEnemy;

        /// <summary>
        /// そのマスで戦う敵。ボスマスはボス、それ以外は null。
        /// 戦闘マスは仕様書 第7章「敵の出現ルール」に従う：最初の数戦は弱めの敵だけ、同じ敵は2戦続けない。
        /// </summary>
        public EnemyData PickEnemy(TileNode tile)
        {
            switch (tile.type)
            {
                case TileType.Battle:
                    return PickNormalEnemy();
                case TileType.Elite:
                    return config.eliteEnemies.Count > 0 ? config.eliteEnemies[random.Battle.Next(config.eliteEnemies.Count)] : PickNormalEnemy();
                case TileType.Boss:
                    return config.boss;
                default:
                    return null;
            }
        }

        EnemyData PickNormalEnemy()
        {
            var candidates = new List<EnemyData>(config.battleEnemies);
            if (normalBattles < config.earlyBattleCount)
            {
                var early = candidates.FindAll(e => e.earlyOk);
                if (early.Count > 0) candidates = early;
            }
            if (candidates.Count > 1) candidates.Remove(lastEnemy);

            var enemy = candidates[random.Battle.Next(candidates.Count)];
            normalBattles++;
            lastEnemy = enemy;
            return enemy;
        }
    }
}
