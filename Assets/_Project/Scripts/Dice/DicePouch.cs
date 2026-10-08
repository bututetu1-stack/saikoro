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
        public const int DefaultCapacity = 5;

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

        /// <summary>ダイスを使用済みにする。keepAvailable なら使用済みにしない（レリック「小石」など）。リフレッシュが起きたら true。</summary>
        public bool Use(DiceInstance die, bool keepAvailable = false)
        {
            if (!dice.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            // ピンゾロ賽：使っても使用済みにならない
            if (!keepAvailable && (die.data == null || !die.data.keepAvailable)) die.state = DiceState.Used;
            return RefreshIfEmpty();
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
