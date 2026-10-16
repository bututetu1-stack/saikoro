using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 協力「賽の旅団」の、仲間の様子（届いた知らせを順に Apply して作る。全員が同じ順で受け取るので、全員同じになる）。
    /// 1ターンの流れ：全員が Ready（前のマスの出来事を終えた）→ 全員が Rolled（移動のダイスを振った）→ 全員が Vote（行き先のマス）
    /// → 票の多いマスへ全員で進む（Decided）→ それぞれ止まったマスの出来事 → また Ready。
    /// </summary>
    public class PartyState
    {
        public readonly int count;
        public readonly int leader;

        public readonly int[] hp;
        public readonly int[] maxHp;
        public readonly string[] activity;
        public readonly bool[] down;      // 倒れた・抜けた（数に入れない）
        readonly int[] readyTurn;

        // いまのターンの移動
        public readonly int?[] rolled;      // 出目（振れなかったら −1）。まだなら null
        public readonly int[] forcedTile;   // 行き先が決まっている（帰り道）ならそのマス。なければ −1
        public readonly string[] diceName;
        public readonly int?[] votes;       // 票を入れたマス。まだなら null

        // 行き先が決まったときの、みんなの出目と帰り道のマス（決まった出目で進むために使う。次に決まるまで残る）
        public int[] lastRolled = new int[0];
        public int[] lastForced = new int[0];

        /// <summary>全員の票がそろって決まった行き先のマス（Decided のときに入る。次の移動で消える）。</summary>
        public int DecidedTile { get; private set; } = -1;

        /// <summary>いちばん新しい決定が抽選だったときの、同じ票で並んだマス（抽選でなければ空）。抽選の演出に使う。</summary>
        public List<int> LotteryTiles { get; private set; } = new List<int>();

        int decisions; // 決めた回数（抽選の種に混ぜる。全員の端末で同じ）

        /// <summary>行き先が決まった（全員が票を入れた）。</summary>
        public event Action Decided;
        /// <summary>何か変わった（表示の更新用）。</summary>
        public event Action Changed;

        public PartyState(int count, int leader)
        {
            this.count = count;
            this.leader = leader;
            hp = new int[count];
            maxHp = new int[count];
            activity = new string[count];
            down = new bool[count];
            readyTurn = Enumerable.Repeat(-1, count).ToArray(); // はじめの「準備よし」（ターン 0）が来るまでは、そろっていない
            rolled = new int?[count];
            forcedTile = Enumerable.Repeat(-1, count).ToArray();
            diceName = new string[count];
            votes = new int?[count];
        }

        IEnumerable<int> Alive => Enumerable.Range(0, count).Where(i => !down[i]);

        /// <summary>slot の人が「準備よし」を送った、いちばん新しいターン（まだなら −1）。</summary>
        public int ReadyTurn(int slot) => readyTurn[slot];

        public bool AllReady(int turn) => Alive.All(i => readyTurn[i] >= turn);
        public int ReadyCount(int turn) => Alive.Count(i => readyTurn[i] >= turn);
        public bool AllRolled => Alive.All(i => rolled[i].HasValue);
        public int RolledCount => Alive.Count(i => rolled[i].HasValue);
        public int VotedCount => Alive.Count(i => votes[i].HasValue);
        public int AliveCount => Alive.Count();
        public bool AllDown => !Alive.Any();

        public void Apply(PartyCommand c)
        {
            if (c.slot < 0 || c.slot >= count) return;
            switch (c.type)
            {
                case PartyCommandType.Ready:
                    readyTurn[c.slot] = Math.Max(readyTurn[c.slot], c.a);
                    break;
                case PartyCommandType.Rolled:
                    rolled[c.slot] = c.a;
                    forcedTile[c.slot] = c.b;
                    diceName[c.slot] = c.text;
                    break;
                case PartyCommandType.Vote:
                    votes[c.slot] = c.a;
                    break;
                case PartyCommandType.Status:
                    hp[c.slot] = c.a;
                    maxHp[c.slot] = c.b;
                    activity[c.slot] = c.text;
                    break;
                case PartyCommandType.Down:
                    down[c.slot] = true;
                    break;
            }
            CheckDecided();
            Changed?.Invoke();
        }

        /// <summary>仲間が抜けた（倒れたのと同じに数えない）。</summary>
        public void MarkLeft(int slot)
        {
            if (slot < 0 || slot >= count) return;
            down[slot] = true;
            CheckDecided();
            Changed?.Invoke();
        }

        void CheckDecided()
        {
            if (!AllRolled || AllDown || Alive.Any(i => !votes[i].HasValue)) return;
            // 票の多いマス。同じ票数なら抽選（種は、決めた回数・票・出目から作るので、全員の端末で同じ結果になる）
            var list = Enumerable.Range(0, count).Select(i => down[i] || !votes[i].HasValue ? -1 : votes[i].Value).ToList();
            int seed = unchecked(decisions * 92821 + 17);
            for (int i = 0; i < count; i++) seed = unchecked(seed * 31 + list[i] * 7 + (rolled[i] ?? -1));
            decisions++;
            DecidedTile = PartyRun.Draw(list, seed, out var tied);
            LotteryTiles = tied.Count > 1 ? tied : new List<int>();
            lastRolled = rolled.Select(v => v ?? -1).ToArray();
            lastForced = (int[])forcedTile.Clone();
            for (int i = 0; i < count; i++)
            {
                rolled[i] = null;
                votes[i] = null;
            }
            Decided?.Invoke();
        }

        /// <summary>決まった行き先を受け取って消す（次のターンのため）。</summary>
        public int TakeDecided()
        {
            int tile = DecidedTile;
            DecidedTile = -1;
            return tile;
        }
    }
}
