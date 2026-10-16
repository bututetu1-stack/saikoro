using System;
using System.Collections.Generic;

namespace SaiNoMichi.Dice
{
    /// <summary>
    /// ダイスを振る（特別なルールを含む）。移動でも戦闘でも、振るときはここを通す。
    /// - 鏡賽：直前に振ったダイスの出目を写す（そのランで最初なら 3）
    /// - 爆賽：explodeOn の面が出たら振り足して加算（上限なし。念のため 50 回で打ち切る）
    /// </summary>
    public static class DiceRoller
    {
        public const int MirrorFirstValue = 3;

        /// <summary>鏡賽が次に出す目（直前の出目。まだ振っていなければ 3）。</summary>
        public static int MirrorValue(int lastValue) => lastValue >= 0 ? lastValue : MirrorFirstValue;

        /// <summary>
        /// 写すダイスが次に出す目。鏡賽は直前の出目のまま、梯子賽（mirrorStep 1）は直前の出目 +1（6 以上の次は 1、まだ振っていなければ 1）。
        /// </summary>
        public static int MirrorValue(DiceInstance die, int lastValue)
        {
            int step = die != null && die.data != null ? die.data.mirrorStep : 0;
            if (step == 0) return MirrorValue(lastValue);
            if (lastValue < 0) return 1;
            int v = lastValue + step;
            return v > 6 ? 1 : Math.Max(1, v);
        }
        const int MaxExplosions = 50;

        /// <param name="lastValue">直前に振ったダイスの出目（まだなければ負の数）</param>
        public static int Roll(DiceInstance die, Random rng, int lastValue, out int faceIndex)
        {
            faceIndex = die.RollFaceIndex(rng);
            if (die.data != null && die.data.mirror)
            {
                return MirrorValue(die, lastValue);
            }

            int total = die.faces[faceIndex].value;
            int trigger = die.data != null ? die.data.explodeOn : 0;
            if (trigger > 0)
            {
                int current = faceIndex;
                for (int i = 0; i < MaxExplosions && die.faces[current].value == trigger; i++)
                {
                    current = die.RollFaceIndex(rng);
                    total += die.faces[current].value;
                }
            }
            return total;
        }

        /// <summary>
        /// 出目の分布（止まりうるマスの確率に使う）。爆賽は2回目の振り足しまで数え、それより先は切り捨てる。
        /// </summary>
        public static List<(int value, float probability)> Distribution(DiceInstance die, int lastValue)
        {
            var result = new List<(int, float)>();
            if (die.data != null && die.data.mirror)
            {
                result.Add((MirrorValue(die, lastValue), 1f));
                return result;
            }

            int n = die.faces.Length;
            float p = 1f / n;
            int trigger = die.data != null ? die.data.explodeOn : 0;
            foreach (var f in die.faces)
            {
                if (trigger > 0 && f.value == trigger)
                {
                    foreach (var g in die.faces)
                    {
                        if (g.value == trigger)
                        {
                            foreach (var h in die.faces) result.Add((f.value + g.value + h.value, p * p * p));
                        }
                        else
                        {
                            result.Add((f.value + g.value, p * p));
                        }
                    }
                }
                else
                {
                    result.Add((f.value, p));
                }
            }
            return result;
        }
    }
}
