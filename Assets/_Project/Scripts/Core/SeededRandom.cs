using System;

namespace SaiNoMichi.Core
{
    /// <summary>
    /// 使った回数を数える System.Random。セーブでは「シード」と「回数」だけを保存し、
    /// 続きからは同じシードで作り直して同じ回数だけ進めると、乱数の状態が元に戻る。
    /// 出る値は System.Random とまったく同じ（Next() なども内部の値を数えながら同じ計算をする）。
    /// </summary>
    public class SeededRandom : Random
    {
        public int Seed { get; }

        /// <summary>内部の乱数を取り出した回数。</summary>
        public long Count { get; private set; }

        public SeededRandom(int seed) : base(seed)
        {
            Seed = seed;
        }

        /// <summary>seed から作り、count 回ぶん進めた乱数（続きから用）。</summary>
        public SeededRandom(int seed, long count) : this(seed)
        {
            for (long i = 0; i < count; i++) Sample();
        }

        protected override double Sample()
        {
            Count++;
            return base.Sample();
        }

        // System.Random の内部の値（0 以上 int.MaxValue 未満）を、数えながら取り出す
        int InternalSample() => (int)Math.Round(Sample() * int.MaxValue);

        public override int Next() => InternalSample();

        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            long range = (long)maxValue - minValue;
            if (range <= int.MaxValue) return (int)(Sample() * range) + minValue;
            // 幅が広いとき（System.Random と同じ計算）
            int result = InternalSample();
            if (InternalSample() % 2 == 0) result = -result;
            double d = result;
            d += int.MaxValue - 1;
            d /= 2 * (uint)int.MaxValue - 1;
            return (int)((long)(d * range) + minValue);
        }

        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(InternalSample() % (byte.MaxValue + 1));
        }
    }
}
