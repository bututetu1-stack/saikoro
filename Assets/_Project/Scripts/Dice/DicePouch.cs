using System;
using System.Collections.Generic;
using System.Linq;

namespace SaiNoMichi.Dice
{
    /// <summary>
    /// ダイスポーチ。移動でも戦闘でも、使ったダイスは使用済みになり、
    /// 使用可能が0個になった瞬間に使用済みが全部戻る（リフレッシュ）。封印は戻らない。
    /// </summary>
    public class DicePouch
    {
        public const int DefaultCapacity = 10; // 開発者の判断：5個ではやりくりが苦しいので10個に

        readonly List<DiceInstance> dice = new List<DiceInstance>();

        public int Capacity { get; set; } = DefaultCapacity;
        public IReadOnlyList<DiceInstance> All => dice;
        public IEnumerable<DiceInstance> Available => dice.Where(d => d.state == DiceState.Available);
        public int AvailableCount => dice.Count(d => d.state == DiceState.Available);
        public bool IsFull => dice.Count >= Capacity;

        /// <summary>あと何個使うとリフレッシュするか（ピンゾロ賽は数えない）。</summary>
        public int UsesUntilRefresh => dice.Count(d => d.state == DiceState.Available && !NeverUsed(d));

        /// <summary>リフレッシュが起きたとき。フェーズ1以降の「リフレッシュしたとき」効果の入口。</summary>
        public event Action Refreshed;

        /// <summary>容量を気にせず加える（戦闘中だけの呪いのダイスなど）。</summary>
        public void ForceAdd(DiceInstance die) => dice.Add(die);

        public void Add(DiceInstance die)
        {
            if (IsFull) throw new InvalidOperationException("ポーチが満杯です。");
            dice.Add(die);
        }

        /// <summary>ダイスを取り除く。使用可能が0個になったらリフレッシュする。</summary>
        public void Remove(DiceInstance die)
        {
            if (!dice.Remove(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            RefreshIfEmpty();
        }

        /// <summary>
        /// ダイスを使用済みにする。keepAvailable なら使用済みにしない（レリック「小石」など）。リフレッシュが起きたら true。
        /// inBattle が false（移動など）なら、ピンゾロ賽も使用済みになる（いつでも1マス進めるのは強すぎるため。開発者の判断）。
        /// </summary>
        public bool Use(DiceInstance die, bool keepAvailable = false, bool inBattle = true)
        {
            if (!dice.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            // ピンゾロ賽：戦闘で使っても使用済みにならない
            bool dieKeeps = inBattle && die.data != null && die.data.keepAvailable;
            // 小石などで使用可能のままにできるのは、ほかに使うダイスが残っているときだけ。
            // 最後の1個まで使用可能のままだと、いつまでもリフレッシュが起きなかった（鏡賽がピンゾロ賽の1を写し続けるなど）
            if (keepAvailable && !dieKeeps && !Available.Any(d => d != die && !NeverUsed(d))) keepAvailable = false;
            if (!keepAvailable && !dieKeeps) die.state = DiceState.Used;
            return RefreshIfEmpty();
        }

        /// <summary>
        /// 封印：使用可能なダイスからランダムに1個を封印する。封印されるのはいつも1個だけで、
        /// 前に封印されていたダイスは解放される（使用可能に戻る）。前のダイスはなるべく選ばない。
        /// 封印したダイスを返す（使用可能なダイスがなければ何もせず null）。使用可能が0個になったらリフレッシュする。
        /// </summary>
        public DiceInstance SealRandom(Random rng)
        {
            var previous = dice.Where(d => d.state == DiceState.Sealed).ToList();
            var candidates = dice.Where(d => d.state == DiceState.Available).ToList();
            if (candidates.Count == 0 && previous.Count == 0) return null;
            foreach (var d in previous) d.state = DiceState.Available;
            if (candidates.Count == 0) candidates = previous;
            var target = candidates[rng.Next(candidates.Count)];
            target.state = DiceState.Sealed;
            RefreshIfEmpty();
            return target;
        }

        /// <summary>使っても使用済みにならないダイス（ピンゾロ賽）か。</summary>
        static bool NeverUsed(DiceInstance d) => d.data != null && d.data.keepAvailable;

        /// <summary>
        /// 使用可能が0個なら、使用済みをすべて使用可能に戻す。戻したら true。
        /// 使用可能なのがピンゾロ賽（使っても使用済みにならない）だけのときも戻す。そうしないといつまでもリフレッシュが起きない。
        /// </summary>
        public bool RefreshIfEmpty()
        {
            if (Available.Any(d => !NeverUsed(d))) return false;

            bool any = false;
            foreach (var d in dice)
            {
                if (d.state == DiceState.Used)
                {
                    d.state = DiceState.Available;
                    any = true;
                }
            }
            if (any) Refreshed?.Invoke();
            return any;
        }
    }
}
