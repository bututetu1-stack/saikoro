namespace SaiNoMichi.Core
{
    /// <summary>
    /// ランのシードから用途別の乱数を作る（仕様書 第14章）。
    /// 戦闘で振り方を変えても、マップや報酬の乱数はずれない。
    /// </summary>
    public class RunRandom
    {
        public int Seed { get; }
        public System.Random Map { get; }
        public System.Random Battle { get; }
        public System.Random Reward { get; }
        // TODO(仕様): 仕様書は3つの乱数だが、移動の出目で戦闘の乱数がずれないよう移動用を分けた
        public System.Random Move { get; }
        // イベントの抽選と、イベントで振るダイス（開発者の判断：イベントはランダムでよい）
        public System.Random Event { get; }

        public RunRandom(int seed) : this(seed, null) { }

        /// <summary>counts（用途ごとに使った回数。セーブの値）だけ進めた乱数を作る（続きから用）。null なら最初から。</summary>
        public RunRandom(int seed, long[] counts)
        {
            Seed = seed;
            // System.Random は近いシード（1, 2, 3…）だと最初のほうの値が似てしまうので、
            // シードと用途の番号をかき混ぜてから子の乱数を作る。番号を変えると既存シードの結果が変わるので、足すときは末尾に。
            Map = Create(seed, 0, counts);
            Battle = Create(seed, 1, counts);
            Reward = Create(seed, 2, counts);
            Move = Create(seed, 3, counts);
            Event = Create(seed, 4, counts);
        }

        static SeededRandom Create(int seed, int stream, long[] counts) =>
            new SeededRandom(Mix(seed, stream), counts != null && stream < counts.Length ? counts[stream] : 0);

        /// <summary>用途ごとに使った回数（Map, Battle, Reward, Move, Event の順）。セーブに書く。</summary>
        public long[] Counts => new[] { Count(Map), Count(Battle), Count(Reward), Count(Move), Count(Event) };

        static long Count(System.Random r) => r is SeededRandom s ? s.Count : 0;

        /// <summary>SplitMix64 でシードと用途の番号から、互いに似ていない子のシードを作る。</summary>
        public static int Mix(int seed, int stream)
        {
            unchecked
            {
                ulong x = ((ulong)(uint)seed << 8) | (uint)stream;
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return (int)(x & 0x7FFFFFFF);
            }
        }
    }
}
