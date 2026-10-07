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

        public RunRandom(int seed)
        {
            Seed = seed;
            // 親の乱数から子のシードを順に取り出す。順番を変えると既存シードの結果が変わるので、足すときは末尾に。
            var root = new System.Random(seed);
            Map = new System.Random(root.Next());
            Battle = new System.Random(root.Next());
            Reward = new System.Random(root.Next());
            Move = new System.Random(root.Next());
        }
    }
}
