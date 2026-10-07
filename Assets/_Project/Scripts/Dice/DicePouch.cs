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

        /// <summary>リフレッシュが起きたとき。フェーズ1以降の「リフレッシュしたとき」効果の入口。</summary>
        public event Action Refreshed;

        public void Add(DiceInstance die)
        {
            if (IsFull) throw new InvalidOperationException("ポーチが満杯です。");
            dice.Add(die);
        }

        /// <summary>ダイスを使用済みにする。リフレッシュが起きたら true。</summary>
        public bool Use(DiceInstance die)
        {
            if (!dice.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            die.state = DiceState.Used;
            return RefreshIfEmpty();
        }

        /// <summary>使用可能が0個なら、使用済みをすべて使用可能に戻す。戻したら true。</summary>
        public bool RefreshIfEmpty()
        {
            if (AvailableCount > 0) return false;

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
