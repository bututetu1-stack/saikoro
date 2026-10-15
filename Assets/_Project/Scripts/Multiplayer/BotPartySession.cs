using System;
using System.Collections.Generic;
using System.Linq;
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
            names.Add(localName);
            string[] botNames = { "AI・壱", "AI・弐", "AI・参" };
            var starters = RunState.StarterOptions(config, seed);
            int n = Math.Max(1, Math.Min(3, botCount));
            for (int i = 0; i < n; i++)
            {
                DiceData starter = starters.Count > 0 ? starters[(i + 1) % starters.Count] : null;
                bots.Add(new Bot { slot = i + 1, player = new AutoPlayer(config, seed, starter) });
                names.Add(botNames[i]);
            }
            state = new PartyState(n + 1, 0);
            foreach (var bot in bots)
            {
                Post(bot.slot, PartyCommandType.Status, bot.player.run.player.hp, bot.player.run.player.maxHp, "旅の支度", 0f);
                Post(bot.slot, PartyCommandType.Ready, 0, 0, null, 0f);
            }
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
                CommandReceived?.Invoke(c);
                React();
            }
        }

        /// <summary>みんなの様子を見て、AI の仲間が次にすることを決める。</summary>
        void React()
        {
            foreach (var bot in bots)
            {
                if (bot.down) continue;
                var run = bot.player.run;
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
            bool cleared = false;
            if (alive)
            {
                var result = run.FinishMove(move);
                alive = result.to == result.from || bot.player.LandOn(result.to, out cleared);
            }
            alive &= !run.player.IsDead;
            float delay = TileSeconds * (0.6f + (float)think.NextDouble());
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
    }
}
