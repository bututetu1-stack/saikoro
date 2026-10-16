using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 協力「賽の旅団」の練習：AI の仲間と旅をする（通信なし）。あなたは 0 番で、行き先が同じ票数のときはあなたの票で決まる。
    /// AI の仲間は、それぞれ自分のラン（同じシードの盤面）を自動プレイの考え方で進める。止まったマスの出来事（戦闘も）は、
    /// 仲間ごとにすぐに済ませる。知らせは Tick で少し間をおいて届ける（いっぺんに届くと、何が起きたかわかりにくいため）。
    /// </summary>
    public class BotPartySession : IPartySession
    {
        class Bot
        {
            public int slot;
            public AutoPlayer player;
            public RunState.MoveInProgress move;   // 振ったダイスの移動（振れなかったら null）
            public bool rolledThisTurn;
            public bool votedThisTurn;
            public bool down;
            public TileNode battleTile;  // 一緒に戦う戦闘のマス（終わるまで「準備よし」を出さない）
            public int rolledRound, readyRound, rollDelivered, charmRound;  // 戦闘：振った・決定したラウンド、振ったのが届いたラウンド
        }

        // TODO(仕様): AI の仲間が考える時間（秒）。仮
        const float ThinkSeconds = 0.6f;
        const float TileSeconds = 1.2f;
        const float FollowMargin = 2f;

        readonly List<Bot> bots = new List<Bot>();
        readonly PartyState state;   // AI の仲間から見た、みんなの様子
        readonly List<(float due, PartyCommand command)> outbox = new List<(float, PartyCommand)>();
        readonly List<string> names = new List<string>();
        readonly Random think;
        readonly Random chatter;   // 合図の返事（考え方の乱数とは別にする。合図を使っても AI の動きは変わらない）
        float now;
        int seq;

        public int Seed { get; }
        public int LocalSlot => 0;
        public IReadOnlyList<string> Names => names;
        public int LeaderSlot => 0;

        public event Action<PartyCommand> CommandReceived;
        public event Action<int> MemberLeft;

        /// <param name="botCount">AI の仲間の数（1〜3）。</param>
        public BotPartySession(GameConfig config, int seed, string localName, int botCount = 2)
        {
            Seed = seed;
            think = new Random(seed);
            chatter = new Random(seed ^ 0x3C1);
            names.Add(localName);
            string[] botNames = { "AI・壱", "AI・弐", "AI・参" };
            var starters = RunState.StarterOptions(config, seed);
            int n = Math.Max(1, Math.Min(3, botCount));
            for (int i = 0; i < n; i++)
            {
                DiceData starter = starters.Count > 0 ? starters[(i + 1) % starters.Count] : null;
                var bot = new Bot { slot = i + 1, player = new AutoPlayer(config, seed, starter) };
                bot.player.run.multiplayer = true; // 協力では出ないイベント（振り出しの札・竜宮城）を AI の仲間にも出さない
                bots.Add(bot);
                names.Add(botNames[i]);
            }
            state = new PartyState(n + 1, 0);
            foreach (var bot in bots)
            {
                Post(bot.slot, PartyCommandType.Status, bot.player.run.player.hp, bot.player.run.player.maxHp, "旅の支度", 0f);
                Post(bot.slot, PartyCommandType.Ready, 0, 0, null, 0f);
            }
            // はじめのあいさつ
            if (bots.Count > 0) Post(bots[0].slot, PartyCommandType.Emote, 0, 0, null, 1.5f);
        }

        public void Send(PartyCommandType type, int a = 0, int b = 0, string text = null) => Post(0, type, a, b, text, 0f);

        public void Leave() { }

        void Post(int slot, PartyCommandType type, int a, int b, string text, float delay)
        {
            outbox.Add((now + delay, new PartyCommand { slot = slot, type = type, a = a, b = b, text = text }));
        }

        /// <summary>毎フレーム呼ぶ（time は経った秒数）。時間が来た知らせを、送った順に届ける。</summary>
        public void Tick(float time)
        {
            now = time;
            // 送った順（同じ時刻なら、先に送ったもの）に、時間が来たものだけ
            while (true)
            {
                int index = -1;
                for (int i = 0; i < outbox.Count; i++)
                {
                    if (outbox[i].due > now) continue;
                    if (index < 0 || outbox[i].due < outbox[index].due) index = i;
                }
                if (index < 0) break;
                var c = outbox[index].command;
                outbox.RemoveAt(index);
                c.seq = seq++;
                state.Apply(c);
                // 受け取った側（画面）で例外が出ても、AI の仲間は止めない
                try { CommandReceived?.Invoke(c); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
                // 戦闘：自分の「振る」が届いた（GameController が振った）ら、割り振りに進める
                // あなたの合図に、AI の仲間がときどき返事をする
                if (c.type == PartyCommandType.Emote && c.slot == LocalSlot) ReplyToEmote(c.a);
                if (c.type == PartyCommandType.BattleRoll)
                {
                    var who = bots.FirstOrDefault(b => b.slot == c.slot);
                    if (who != null) who.rollDelivered = Math.Max(who.rollDelivered, c.a);
                }
                React();
            }
        }

        /// <summary>
        /// あなたの合図への返事（言葉の番号は GameController.EmotePhrases の順：よろしく・ありがとう・ナイス・ごめん・まかせて・待って）。
        /// 倒れていない AI の仲間のうち1人が、少し間をおいて返す。
        /// </summary>
        void ReplyToEmote(int phrase)
        {
            int[] replies = { 0, 4, 1, 4, 2, -1 };
            if (phrase < 0 || phrase >= replies.Length || replies[phrase] < 0) return;
            var alive = bots.Where(b => !b.down).ToList();
            if (alive.Count == 0 || chatter.NextDouble() > 0.75) return;
            var bot = alive[chatter.Next(alive.Count)];
            Post(bot.slot, PartyCommandType.Emote, replies[phrase], 0, null, 0.6f + (float)chatter.NextDouble());
        }

        /// <summary>みんなの様子を見て、AI の仲間が次にすることを決める。</summary>
        void React()
        {
            ReactInBattle();
            foreach (var bot in bots)
            {
                if (bot.down) continue;
                var run = bot.player.run;
                if (bot.battleTile != null) continue; // 一緒に戦っているあいだは動かない
                // 全員がそろったら、移動のダイスを振る
                if (!bot.rolledThisTurn && state.AllReady(run.Turn) && state.RolledCount < state.AliveCount && !run.ReachedGoal)
                {
                    bot.rolledThisTurn = true;
                    bot.votedThisTurn = false;
                    Roll(bot);
                }
            }
            // 票は、あなたの票を見てから入れる（あなたが倒れていれば、すぐ）。たいていはあなたについていく
            if (state.RolledCount == state.AliveCount && state.AliveCount > 0 && (state.votes[0].HasValue || state.down[0]))
            {
                foreach (var bot in bots)
                {
                    if (bot.down || bot.votedThisTurn || !bot.rolledThisTurn) continue;
                    bot.votedThisTurn = true;
                    Post(bot.slot, PartyCommandType.Vote, ChooseTile(bot), 0, null, ThinkSeconds * (0.5f + (float)think.NextDouble()));
                }
            }
            if (state.DecidedTile >= 0)
            {
                int tile = state.TakeDecided();
                foreach (var bot in bots)
                {
                    if (bot.down || !bot.rolledThisTurn) continue;
                    bot.rolledThisTurn = false;
                    Walk(bot, tile);
                }
            }
        }

        void Roll(Bot bot)
        {
            var run = bot.player.run;
            bot.player.PrepareTurn();
            bot.move = null;
            if (run.MustSkipTurn)
            {
                run.SkipTurn();
                Post(bot.slot, PartyCommandType.Rolled, -1, -1, null, ThinkSeconds);
                return;
            }
            bot.move = bot.player.BeginBestMove();
            if (bot.move == null)
            {
                Post(bot.slot, PartyCommandType.Rolled, -1, -1, null, ThinkSeconds);
                return;
            }
            Post(bot.slot, PartyCommandType.Rolled, bot.move.remaining, bot.move.forcedTarget != null ? bot.move.forcedTarget.id : -1,
                bot.move.dice != null ? bot.move.dice.DisplayName : "", ThinkSeconds * (0.5f + (float)think.NextDouble()));
        }

        /// <summary>みんなの出目で止まれるマスのうち、この AI がいちばん行きたいマス。</summary>
        int ChooseTile(Bot bot)
        {
            var run = bot.player.run;
            var options = PartyRun.VoteOptions(run.Current, state.rolled.Select(v => v ?? -1).ToList(), state.forcedTile);
            if (options.Count == 0) return run.Current.id;
            var best = options.Keys.OrderByDescending(t => bot.player.TileScore(t)).ThenBy(t => t.id).First();
            // あなたの選んだマスが、自分の一番よりそれほど悪くなければ、あなたについていく（いやなマスなら自分の票）
            // TODO(仕様): ついていくかの差は仮（2点）
            var yours = state.votes[0].HasValue ? options.Keys.FirstOrDefault(t => t.id == state.votes[0].Value) : null;
            if (yours != null && bot.player.TileScore(yours) >= bot.player.TileScore(best) - FollowMargin) return yours.id;
            return best.id;
        }

        void Walk(Bot bot, int tileId)
        {
            var run = bot.player.run;
            int value = PartyRun.ValueToReach(run.Current, state.lastRolled, state.lastForced, tileId);
            var target = run.board.tiles.FirstOrDefault(t => t.id == tileId);
            if (value < 0 || target == null) value = 0;
            var move = bot.move ?? run.BeginForcedMove(value);
            move.value = value;
            move.remaining = value;
            move.adjust = 0;
            move.forcedTarget = null;
            bool alive = bot.player.WalkTo(move, target);
            bool cleared = false, battle = false;
            if (alive)
            {
                var result = run.FinishMove(move);
                // あなたが旅にいるあいだは、戦闘はあなたと一緒に（GameController が PartyBattle を作って JoinBattle を呼ぶ）
                alive = result.to == result.from || bot.player.LandOn(result.to, out cleared, out battle, !state.down[0]);
                if (battle) bot.battleTile = result.to;
            }
            alive &= !run.player.IsDead;
            float delay = TileSeconds * (0.6f + (float)think.NextDouble());
            if (battle)
            {
                Post(bot.slot, PartyCommandType.Status, Math.Max(0, run.player.hp), run.player.maxHp, "戦闘の準備", delay * 0.5f);
                return; // 一緒に戦い終わったら「準備よし」（LeaveBattle）
            }
            FinishTile(bot, alive, cleared, delay);
        }

        void FinishTile(Bot bot, bool alive, bool cleared, float delay)
        {
            var run = bot.player.run;
            string activity = cleared ? "踏破した！" : alive ? $"第{run.LayerIndex + 1}層" : "倒れた";
            Post(bot.slot, PartyCommandType.Status, Math.Max(0, run.player.hp), run.player.maxHp, activity, delay);
            if (!alive || cleared)
            {
                bot.down = true;
                Post(bot.slot, PartyCommandType.Down, 0, 0, null, delay);
                return;
            }
            Post(bot.slot, PartyCommandType.Ready, run.Turn, 0, null, delay);
        }

        // ---- 一緒に戦う（協力の戦闘） ----

        PartyBattle battle;
        List<int> battleSeats;

        /// <summary>AI の仲間のラン（協力の戦闘では、写しではなく本物を使う。同じ端末なので）。</summary>
        public RunState RunOf(int slot) => bots.First(b => b.slot == slot).player.run;

        /// <summary>協力の戦闘が始まった（seats は席の順の仲間の番号）。AI の仲間も、振って・割り振って・決定する。</summary>
        public void JoinBattle(PartyBattle partyBattle, List<int> seats)
        {
            battle = partyBattle;
            battleSeats = seats;
            foreach (var bot in bots)
            {
                bot.rolledRound = 0;
                bot.readyRound = 0;
                bot.rollDelivered = 0;
                bot.charmRound = 0;
            }
            React();
        }

        /// <summary>協力の戦闘が終わった：勝てば報酬を受け取って「準備よし」、負ければ旅から抜ける。</summary>
        public void LeaveBattle(BattleOutcome outcome)
        {
            battle = null;
            battleSeats = null;
            foreach (var bot in bots)
            {
                if (bot.down || bot.battleTile == null) continue;
                var tile = bot.battleTile;
                bot.battleTile = null;
                bool alive = outcome == BattleOutcome.Victory && !bot.player.run.player.IsDead;
                bool cleared = alive && bot.player.AfterSharedBattle(tile);
                FinishTile(bot, alive, cleared, TileSeconds * (0.6f + (float)think.NextDouble()));
            }
        }

        /// <summary>ラウンドが進んだときなど、知らせが来なくても AI の仲間に考えさせる。</summary>
        public void Nudge() => React();

        /// <summary>戦闘中：ラウンドごとに、振る → （届いたら）割り振って決定。</summary>
        void ReactInBattle()
        {
            if (battle == null || battle.Outcome != BattleOutcome.Ongoing) return;
            int round = battle.Round;
            foreach (var bot in bots)
            {
                int seat = battleSeats.IndexOf(bot.slot);
                if (seat < 0 || bot.down) continue;
                var m = battle.Members[seat];
                if (m.Outcome != BattleOutcome.Ongoing) continue;
                if (bot.rolledRound < round)
                {
                    bot.rolledRound = round;
                    // お守り（1ラウンドに1つまで。振る前に使う。強敵・ボスなら薬も）
                    if (bot.charmRound < round)
                    {
                        bot.charmRound = round;
                        bool bigFight = bot.battleTile != null && (bot.battleTile.type == TileType.Elite || bot.battleTile.type == TileType.Boss);
                        var charm = bot.player.ChooseBattleCharm(m, bigFight);
                        if (charm != null)
                        {
                            int target = m.Target != null ? battle.enemies.IndexOf(m.Target) : -1;
                            Post(bot.slot, PartyCommandType.BattleAction, round, 0, $"{bot.player.run.Charms.ToList().IndexOf(charm)},{target}", ThinkSeconds * 0.4f);
                        }
                    }
                    // 振るダイス：平均の出目が高いもの（呪いは最後）から、1ラウンドに振れるだけ
                    var all = m.pouch.All.ToList();
                    var dice = m.pouch.Available
                        .OrderBy(d => d.data != null && d.data.rarity == Rarity.Curse ? 1 : 0)
                        .ThenByDescending(d => d.faces.Average(f => f.value))
                        .Take(m.MaxDicePerRound)
                        .Select(d => all.IndexOf(d));
                    Post(bot.slot, PartyCommandType.BattleRoll, round, 0, string.Join(",", dice), ThinkSeconds * (0.5f + (float)think.NextDouble()));
                }
                else if (bot.rollDelivered >= round && bot.readyRound < round)
                {
                    bot.readyRound = round;
                    AutoPlayer.AssignBest(m);
                    int target = m.Target != null ? battle.enemies.IndexOf(m.Target) : -1;
                    string assignments = string.Join(",", m.Rolled.Select(r => ((int)r.assignment).ToString()));
                    float delay = ThinkSeconds * (0.5f + (float)think.NextDouble());
                    // 「使う」（3）を先に送って攻撃を見せてから、ターン終了（数：狙う敵, 使うダイスの割り振り…）
                    if (m.HasPending)
                        Post(bot.slot, PartyCommandType.BattleAction, round, 3, target + "," + string.Join(",", m.Pending.Select(r => ((int)r.assignment).ToString())), delay);
                    Post(bot.slot, PartyCommandType.BattleReady, round, target, assignments, delay + 0.6f);
                }
            }
        }
    }
}
