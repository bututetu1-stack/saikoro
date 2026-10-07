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

        public RunRandom(int seed)
        {
            Seed = seed;
            // System.Random は近いシード（1, 2, 3…）だと最初のほうの値が似てしまうので、
            // シードと用途の番号をかき混ぜてから子の乱数を作る。番号を変えると既存シードの結果が変わるので、足すときは末尾に。
            Map = new System.Random(Mix(seed, 0));
            Battle = new System.Random(Mix(seed, 1));
            Reward = new System.Random(Mix(seed, 2));
            Move = new System.Random(Mix(seed, 3));
            Event = new System.Random(Mix(seed, 4));
        }

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
