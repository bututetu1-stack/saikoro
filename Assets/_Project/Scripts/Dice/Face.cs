using SaiNoMichi.Effects;

namespace SaiNoMichi.Dice
{
    /// <summary>
    /// ダイスの1面。数値と、効果刻印（1面に1つまで）を持つ。
    /// 数値刻印（増強・削りなど）は value そのものを書き換えるので、ここには残らない。
    /// </summary>
    public struct Face
    {
        public const int MinValue = 0;
        public const int MaxValue = 9;   // 鍛冶で変えられる範囲の上限（仕様書 第5章）

        public int value;
        public EngravingData engraving;  // null なら刻印なし

        public Face(int value, EngravingData engraving = null)
        {
            this.value = value;
            this.engraving = engraving;
        }
    }
}
