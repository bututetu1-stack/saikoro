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
    /// <summary>自動プレイの1戦闘の結果（集計用）。</summary>
    public class AutoBattleRecord
    {
        public int layer;          // 1 から数える
        public string enemy;
        public RewardKind kind;
        public int rounds;
        public int hpBefore;
        public int hpAfter;
        public int damageTaken;
        public BattleOutcome outcome;
    }

    /// <summary>自動プレイの1ランの結果（集計用）。</summary>
    public class AutoRunRecord
    {
        public int seed;
        public string starter;
        public bool cleared;
        public int layerReached;   // 1 から数える
        public string deathCause;  // 倒された敵の id、または trap / pass / event / timeout
        public int turns;
        public int hpEnd;
        public int maxHpEnd;
        public int goldEarned;
        public int goldEnd;
        public int relics;
        public int dice;
        public int[] goldEarnedByLayer = new int[3];
        public int[] turnsByLayer = new int[3];
        public readonly List<AutoBattleRecord> battles = new List<AutoBattleRecord>();
    }

    /// <summary>
    /// ロジックだけで1ランを最後まで進める自動プレイ（フェーズ2 手順12。バランス調整用）。
    /// 考え方は簡単なもの：移動は「止まりたいマス」の点数が高くなるダイスと行き先、
    /// 戦闘は予告を見て「与える − 受ける」が良くなる割り振り、報酬は決まった優先順位。
    /// 画面の GameController の流れ（通過マス・止まったマス・報酬・層の移動）をなぞる。変えたときはこちらも合わせる。
    /// </summary>
    public class AutoPlayer
    {
        public const int MaxTurns = 400;
        public const int MaxRounds = 60;

        public readonly RunState run;
        readonly AutoRunRecord record;
        readonly int seed;

        public AutoPlayer(GameConfig config, int seed, DiceData starter = null)
        {
            this.seed = seed;
            run = new RunState(config, seed, starter);
            record = new AutoRunRecord { seed = seed, starter = starter != null ? starter.id : "" };
        }

        int Layer => run.LayerIndex + 1;
        float HpRatio => run.player.maxHp > 0 ? (float)run.player.hp / run.player.maxHp : 0f;

        /// <summary>1ランを最後（クリア・倒れる・ターン切れ）まで進めて、結果を返す。</summary>
        public AutoRunRecord Play()
        {
            int goldBefore = 0, turnBefore = 0;
            while (true)
            {
                if (run.player.IsDead) break;
                if (run.Turn >= MaxTurns)
                {
                    record.deathCause = "timeout";
                    break;
                }
                int layerBefore = run.LayerIndex;
                var outcome = TakeTurn();
                if (outcome == TurnOutcome.Cleared)
                {
                    record.cleared = true;
                    break;
                }
                if (outcome == TurnOutcome.Died) break;
                // 層を移ったら、その層のゴールド・ターンを記録
                if (run.LayerIndex != layerBefore && layerBefore < 3)
                {
                    record.goldEarnedByLayer[layerBefore] = run.stats.goldEarned - goldBefore;
                    record.turnsByLayer[layerBefore] = run.Turn - turnBefore;
                    goldBefore = run.stats.goldEarned;
                    turnBefore = run.Turn;
                }
            }
            if (run.LayerIndex < 3)
            {
                record.goldEarnedByLayer[run.LayerIndex] = run.stats.goldEarned - goldBefore;
                record.turnsByLayer[run.LayerIndex] = run.Turn - turnBefore;
            }
            record.layerReached = Layer;
            record.turns = run.Turn;
            record.hpEnd = Math.Max(0, run.player.hp);
            record.maxHpEnd = run.player.maxHp;
            record.goldEarned = run.stats.goldEarned;
            record.goldEnd = run.Gold;
            record.relics = run.Relics.Count;
            record.dice = run.pouch.All.Count;
            return record;
        }

        enum TurnOutcome { Continue, Died, Cleared }

        // ---- 移動 ----

        TurnOutcome TakeTurn()
        {
            UseHealCharmIfLow(0.5f);
            if (run.MustSkipTurn)
            {
                run.SkipTurn();
                return TurnOutcome.Continue;
            }
            var die = ChooseMoveDie();
            if (die == null)
            {
                // 動かせるダイスがない（呪いだけなど）。1回休みと同じ
                foreach (var d in run.pouch.Available.ToList()) run.pouch.Use(d);
                return TurnOutcome.Continue;
            }
            var move = run.BeginMove(die);
            if (move.Adjustable)
            {
                int best = move.value;
                float bestScore = float.MinValue;
                for (int v = Math.Max(0, move.value - move.adjust); v <= move.value + move.adjust; v++)
                {
                    float s = BestScore(Destinations(run.Current, v));
                    if (s > bestScore + 0.01f)
                    {
                        bestScore = s;
                        best = v;
                    }
                }
                if (best != move.value) run.AdjustMove(move, best);
            }
            if (!Walk(move))
            {
                record.deathCause = "pass";
                return TurnOutcome.Died;
            }
            var result = run.FinishMove(move);
            return result.to == result.from ? TurnOutcome.Continue : Land(result.to);
        }

        static List<TileNode> Destinations(TileNode from, int steps) => ReachCalculator.Compute(from, new[] { steps }).Keys.ToList();

        static bool CanReach(TileNode from, TileNode target, int steps) => ReachCalculator.Compute(from, new[] { steps }).ContainsKey(target);

        /// <summary>1歩ずつ進む。行き先を決めて、分岐ではそこへ届く道を選ぶ。通過マスで倒れたら false。</summary>
        bool Walk(RunState.MoveInProgress move)
        {
            var target = move.forcedTarget ?? BestTile(Destinations(run.Current, move.remaining));
            while (!move.Done)
            {
                TileNode choice = null;
                if (run.NeedsBranchChoice(move))
                {
                    choice = run.Current.next.FirstOrDefault(n => target == null || CanReach(n, target, move.remaining - 1)) ?? run.Current.next[0];
                }
                run.StepMove(move, choice);
                if (!move.Done && run.Current.type.IsPassTile())
                {
                    run.ApplyPassTile(run.Current, false);
                    if (run.player.IsDead) return false;
                }
            }
            return true;
        }

        /// <summary>移動に使うダイス：止まれそうなマスの点数の期待値が高いもの。同じくらいなら出目の小さいダイス（戦闘用に強いダイスを残す）。</summary>
        DiceInstance ChooseMoveDie()
        {
            DiceInstance best = null;
            float bestScore = float.MinValue;
            foreach (var die in run.pouch.Available.Where(RunState.CanMoveWith))
            {
                var foreseen = run.ForeseeRoll(die);
                var dist = foreseen.HasValue
                    ? new List<(int value, float probability)> { (foreseen.Value, 1f) }
                    : DiceRoller.Distribution(die, run.LastRolledValue);
                float expected = 0f;
                foreach (var (value, p) in dist) expected += p * BestScore(Destinations(run.Current, value));
                expected -= 0.15f * (float)die.faces.Average(f => f.value);
                if (expected > bestScore)
                {
                    bestScore = expected;
                    best = die;
                }
            }
            return best;
        }

        float BestScore(List<TileNode> tiles) => tiles.Count == 0 ? 0f : tiles.Max(TileScore);

        TileNode BestTile(List<TileNode> tiles)
        {
            TileNode best = null;
            float bestScore = float.MinValue;
            foreach (var t in tiles)
            {
                float s = TileScore(t);
                if (s > bestScore)
                {
                    bestScore = s;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>止まりたいマスの点数（HP やゴールドで変わる）。</summary>
        float TileScore(TileNode t)
        {
            float hp = HpRatio;
            switch (t.type)
            {
                case TileType.Battle: return hp > 0.4f ? 2f : -3f;
                case TileType.Elite: return hp > 0.75f ? 3f : -5f;
                case TileType.Boss: return hp > 0.5f ? 1f : -2f;
                case TileType.Rest: return hp < 0.6f ? 6f : 1.5f;
                case TileType.Forge: return 2f;
                case TileType.Treasure: return 4f;
                case TileType.Shop: return run.Gold >= 80 ? 3f : 0f;
                case TileType.Event: return 1.5f;
                case TileType.Trap: return -3f;
                case TileType.Shrine: return 1f;
                case TileType.Checkpoint: return -1f;
                case TileType.Teahouse: return hp < 1f ? 1.5f : 0f;
                case TileType.DiceHall: return 1f;
                default: return 0f;
            }
        }

        // ---- 止まったマス ----

        TurnOutcome Land(TileNode tile)
        {
            switch (tile.type)
            {
                case TileType.Battle:
                case TileType.Elite:
                case TileType.Boss:
                {
                    bool boss = tile.type == TileType.Boss;
                    var kind = boss ? RewardKind.Boss : tile.type == TileType.Elite ? RewardKind.Elite : RewardKind.Normal;
                    var enemy = run.PickEnemy(tile);
                    if (!Fight(enemy, kind)) return TurnOutcome.Died;
                    TakeBattleReward(kind);
                    if (boss)
                    {
                        if (run.IsFinalLayer) return TurnOutcome.Cleared;
                        var offer = run.CreateBossRelicOffer();
                        if (offer.Count > 0) run.AddRelic(offer[0]);
                        // 容量を超えたら一番弱いダイス（呪いを優先）を手放す
                        while (run.OverCapacity) run.DiscardDice(run.pouch.All.OrderBy(d => d.data != null && d.data.rarity == Rarity.Curse ? 0 : 1).ThenBy(Average).First());
                        run.AdvanceLayer();
                    }
                    break;
                }
                case TileType.Rest:
                    if (run.RestHealAmount > 0 && HpRatio < 0.7f) run.Rest();
                    else Forge();
                    break;
                case TileType.Forge:
                    if (Forge())
                    {
                        for (int i = 0; i < run.StatBonus(RunStat.ForgeExtraEngravings); i++) Forge();
                    }
                    break;
                case TileType.Treasure:
                {
                    var t = run.OpenTreasure();
                    if (t.relic != null) run.AddRelic(t.relic);
                    if (t.diceOffer != null && WantDice(t.diceOffer)) GainDice(t.diceOffer);
                    break;
                }
                case TileType.Shop:
                    Shop();
                    break;
                case TileType.Trap:
                    run.TriggerTrap();
                    if (run.player.IsDead)
                    {
                        record.deathCause = "trap";
                        return TurnOutcome.Died;
                    }
                    break;
                case TileType.Shrine:
                case TileType.Checkpoint:
                case TileType.Teahouse:
                case TileType.DiceHall:
                    run.ApplyPassTile(tile, true);
                    if (run.player.IsDead)
                    {
                        record.deathCause = "pass";
                        return TurnOutcome.Died;
                    }
                    break;
                case TileType.Event:
                    return Event(tile);
            }
            return run.player.IsDead ? TurnOutcome.Died : TurnOutcome.Continue;
        }

        // ---- 戦闘 ----

        /// <summary>戦う。勝てば true。</summary>
        bool Fight(EnemyData enemy, RewardKind kind)
        {
            var battle = new BattleState(run.player, enemy, run.pouch, run.random.Battle, run.effects, run, run.LastEnemyHpPercent);
            var rec = new AutoBattleRecord { layer = Layer, enemy = enemy.id, kind = kind, hpBefore = run.player.hp };
            record.battles.Add(rec);
            bool canFlee = kind == RewardKind.Normal;

            while (battle.Outcome == BattleOutcome.Ongoing)
            {
                if (battle.Round >= MaxRounds)
                {
                    // 終わらない戦闘（防御ばかりなど）は負け扱い
                    rec.outcome = BattleOutcome.Defeat;
                    rec.rounds = battle.Round;
                    rec.hpAfter = run.player.hp;
                    rec.damageTaken = rec.hpBefore - run.player.hp;
                    record.deathCause = "timeout";
                    run.CurrentBattle = null;
                    run.player.hp = 0;
                    return false;
                }
                UseBattleCharms(battle, canFlee);
                if (battle.Outcome != BattleOutcome.Ongoing) break;

                while (battle.CanRollMore)
                {
                    var die = ChooseBattleDie(battle);
                    if (die == null) break;
                    var r = battle.Roll(die);
                    if (battle.Outcome != BattleOutcome.Ongoing) break;
                    if (r.canReroll && !r.rerolled && r.value <= 2) battle.Reroll(r);
                }
                if (battle.Outcome != BattleOutcome.Ongoing) break;
                if (battle.CanUseFate && battle.Rolled.Count > 0)
                {
                    var low = battle.Rolled.OrderBy(x => x.value).First();
                    if (low.value < battle.FateMaxValue - 2) battle.UseFate(low, battle.FateMaxValue);
                }
                AssignBest(battle);
                battle.Resolve();
            }

            rec.outcome = battle.Outcome;
            rec.rounds = battle.Round;
            rec.hpAfter = Math.Max(0, run.player.hp);
            rec.damageTaken = rec.hpBefore - rec.hpAfter;
            if (battle.Outcome == BattleOutcome.Defeat || run.player.IsDead)
            {
                record.deathCause = enemy.id;
                return false;
            }
            // 煙玉で逃げたときは報酬なし（勝ちではないが、ランは続く）
            return battle.Outcome == BattleOutcome.Victory || battle.Outcome == BattleOutcome.Fled;
        }

        /// <summary>振るダイス：平均の出目が高いもの。呪いのダイスは最後。</summary>
        static DiceInstance ChooseBattleDie(BattleState battle) =>
            battle.pouch.Available
                .OrderBy(d => d.data != null && d.data.rarity == Rarity.Curse ? 1 : 0)
                .ThenByDescending(d => d.faces.Average(f => f.value))
                .FirstOrDefault();

        /// <summary>攻撃・防御の割り振りを全部ためして、「与える − 受ける×1.3」がいちばん良いものにする。</summary>
        static void AssignBest(BattleState battle)
        {
            var rolled = battle.Rolled.ToList();
            int n = rolled.Count;
            if (n == 0) return;
            int bestMask = 0;
            float bestScore = float.MinValue;
            for (int mask = 0; mask < (1 << n); mask++)
            {
                for (int i = 0; i < n; i++) battle.Assign(rolled[i], (mask & (1 << i)) != 0 ? Assignment.Block : Assignment.Attack);
                var p = battle.Preview();
                bool kills = battle.AliveEnemies.All(e => e.hp <= 0) || p.dealt >= battle.AliveEnemies.Sum(e => e.hp);
                float score = p.dealt - 1.3f * (p.taken + p.takenMin) / 2f + (kills ? 100f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMask = mask;
                }
            }
            for (int i = 0; i < n; i++) battle.Assign(rolled[i], (bestMask & (1 << i)) != 0 ? Assignment.Block : Assignment.Attack);
        }

        void UseBattleCharms(BattleState battle, bool canFlee)
        {
            foreach (var charm in run.Charms.ToList())
            {
                if (!run.CanUseInBattle(charm, battle)) continue;
                switch (charm.kind)
                {
                    case CharmKind.Heal:
                        if (HpRatio < 0.35f) run.UseCharm(charm);
                        break;
                    case CharmKind.WeakenEnemy:
                    case CharmKind.VulnerableEnemy:
                    case CharmKind.PoisonEnemy:
                        if (!canFlee) run.UseEnemyCharm(charm, battle); // 薬は強敵・ボスに取っておく
                        break;
                    case CharmKind.Unseal:
                        run.UseCharm(charm);
                        break;
                    case CharmKind.ReturnUsed:
                        if (run.pouch.AvailableCount < battle.MaxDicePerRound) run.UseCharm(charm);
                        break;
                    case CharmKind.Smoke:
                        if (canFlee && HpRatio < 0.25f && battle.AliveEnemies.Sum(e => e.hp) > 15)
                        {
                            run.UseSmoke(charm, battle);
                            return;
                        }
                        break;
                }
            }
        }

        void UseHealCharmIfLow(float ratio)
        {
            var heal = run.Charms.FirstOrDefault(c => c.kind == CharmKind.Heal);
            if (heal != null && HpRatio < ratio && run.CanUseNow(heal)) run.UseCharm(heal);
        }

        // ---- 報酬 ----

        void TakeBattleReward(RewardKind kind)
        {
            var reward = run.CreateBattleReward(kind);
            run.GainGold(reward.gold, true);
            if (reward.relic != null) run.AddRelic(reward.relic);
            if (reward.charm != null) run.AddCharm(reward.charm);
            var choice = reward.diceChoices.Where(WantDice).OrderByDescending(d => d.rarity).ThenByDescending(Average).FirstOrDefault();
            if (choice != null && GainDice(choice)) return;
            run.SkipDiceReward();
        }

        static float Average(DiceData d) => d.faceValues != null && d.faceValues.Length > 0 ? (float)d.faceValues.Average() : 0f;
        static float Average(DiceInstance d) => (float)d.faces.Average(f => f.value);

        /// <summary>欲しいダイスか：アンコモン以上か、ポーチの平均より出目が大きい。</summary>
        bool WantDice(DiceData data)
        {
            if (data == null || data.rarity == Rarity.Curse) return false;
            if (data.rarity >= Rarity.Uncommon) return true;
            float pouchAverage = run.pouch.All.Count > 0 ? run.pouch.All.Average(Average) : 0f;
            return Average(data) > pouchAverage + 0.3f;
        }

        /// <summary>ダイスを入れる。満杯なら、いちばん弱いダイス（呪い以外）と入れ替える。入れたら true。</summary>
        bool GainDice(DiceData data)
        {
            if (run.CanAddDice)
            {
                run.AddDice(data);
                return true;
            }
            var worst = run.pouch.All.Where(d => d.data == null || d.data.rarity != Rarity.Curse).OrderBy(Average).FirstOrDefault();
            if (worst == null || Average(worst) >= Average(data)) return false;
            run.ReplaceDice(worst, data);
            return true;
        }

        /// <summary>鍛冶：候補の最初の刻印を、いちばん強いダイスのいちばん小さい面に付ける。付けたら true。</summary>
        bool Forge()
        {
            var offer = run.CreateForgeOffer();
            if (offer.Count == 0) return false;
            var die = run.pouch.All.Where(RunState.CanForge).OrderByDescending(Average).FirstOrDefault();
            if (die == null) return false;
            int face = LowestFace(die);
            run.ApplyEngraving(die, face, offer[0]);
            return true;
        }

        static int LowestFace(DiceInstance die)
        {
            int best = 0;
            for (int i = 1; i < die.faces.Length; i++)
            {
                if (die.faces[i].value < die.faces[best].value) best = i;
            }
            return best;
        }

        void Shop()
        {
            var shop = run.CreateShop();
            // 呪いのダイスを消す
            var curse = run.pouch.All.FirstOrDefault(d => d.data != null && d.data.rarity == Rarity.Curse);
            if (curse != null && shop.CanRemove) shop.RemoveDice(curse);
            // 傷薬（HP が少ないとき）
            if (HpRatio < 0.5f)
            {
                var heal = shop.items.FirstOrDefault(i => i.kind == ShopItemKind.Charm && i.charm.kind == CharmKind.Heal && shop.CanAfford(i));
                if (heal != null && run.CanAddCharm) shop.BuyCharm(heal);
            }
            // レリック → アンコモン以上のダイス
            foreach (var item in shop.items.Where(i => i.kind == ShopItemKind.Relic).OrderByDescending(i => i.price).ToList())
            {
                if (shop.CanAfford(item)) shop.BuyRelic(item);
            }
            foreach (var item in shop.items.Where(i => i.kind == ShopItemKind.Dice && i.dice.rarity >= Rarity.Uncommon).ToList())
            {
                if (!shop.CanAfford(item) || !WantDice(item.dice)) continue;
                if (run.CanAddDice) shop.BuyDice(item);
                else
                {
                    var worst = run.pouch.All.Where(d => d.data == null || d.data.rarity != Rarity.Curse).OrderBy(Average).FirstOrDefault();
                    if (worst != null && Average(worst) < Average(item.dice)) shop.BuyDice(item, worst);
                }
            }
        }

        // ---- イベント ----

        DiceInstance WeakestAvailable() => run.pouch.Available.OrderBy(Average).FirstOrDefault();
        DiceInstance StrongestAvailable() => run.pouch.Available.OrderByDescending(Average).FirstOrDefault();

        TurnOutcome Event(TileNode tile)
        {
            var s = run.config.events;
            switch (run.PickEvent(tile))
            {
                case EventKind.Gamble:
                    if (run.CanGamble && run.Gold >= s.gambleBet + 30) run.Gamble(WeakestAvailable(), true);
                    break;
                case EventKind.FallenDice:
                {
                    var common = run.FallenDiceCommon();
                    if (common != null && WantDice(common)) GainDice(common);
                    break;
                }
                case EventKind.OldShrine:
                {
                    var engraving = run.ShrineEngraving();
                    var die = run.pouch.All.Where(RunState.CanForge).OrderByDescending(Average).FirstOrDefault();
                    if (engraving != null && die != null && run.CanPayShrine && HpRatio > 0.6f) run.ShrineEngrave(die, LowestFace(die), engraving);
                    else run.ShrinePray();
                    break;
                }
                case EventKind.FoxWedding:
                    run.FollowFox();
                    break;
                case EventKind.IdatenFootprints:
                {
                    var forced = run.BeginForcedMove(s.idatenSteps);
                    if (!Walk(forced))
                    {
                        record.deathCause = "pass";
                        return TurnOutcome.Died;
                    }
                    var dash = run.FinishMove(forced);
                    if (dash.to != dash.from) return Land(dash.to);
                    break;
                }
                case EventKind.Craftsman:
                {
                    var die = run.pouch.All.Where(RunState.CanForge).OrderByDescending(Average).FirstOrDefault();
                    if (die != null && run.CanCraft && run.Gold >= s.craftCost + 50) run.Craft(die, LowestFace(die), 6);
                    break;
                }
                case EventKind.TwinStatues:
                {
                    if (!run.CanUseTwinStatues) break;
                    var offer = run.pouch.All.OrderBy(d => d.data != null && d.data.rarity == Rarity.Curse ? 0 : 1).ThenBy(Average).First();
                    var copy = run.pouch.All.Where(d => d != offer && RunState.CanDuplicate(d)).OrderByDescending(Average).FirstOrDefault();
                    if (copy != null && Average(copy) > Average(offer)) run.OfferAndDuplicate(offer, copy);
                    break;
                }
                case EventKind.Pitfall:
                    if (StrongestAvailable() != null) run.Pitfall(StrongestAvailable());
                    if (run.player.IsDead)
                    {
                        record.deathCause = "event";
                        return TurnOutcome.Died;
                    }
                    break;
                case EventKind.Merchant:
                    break;
                case EventKind.OniDice:
                {
                    var bet = run.pouch.Available.Where(run.CanBetOni).OrderByDescending(Average).FirstOrDefault();
                    if (bet != null && Average(bet) >= 4.5f) run.PlayOni(bet);
                    break;
                }
                case EventKind.LostChild:
                    if (WeakestAvailable() != null) run.GuideLostChild(WeakestAvailable(), out _, out _);
                    else run.DirectLostChild();
                    break;
                case EventKind.StartOverCard:
                    if (HpRatio < 0.5f) run.DrawStartOverCard();
                    break;
            }
            return run.player.IsDead ? TurnOutcome.Died : TurnOutcome.Continue;
        }
    }
}
