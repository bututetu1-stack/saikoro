namespace SaiNoMichi.Dice
{
    /// <summary>ダイスの1面。刻印（EngravingData）はフェーズ1で追加する。</summary>
    public struct Face
    {
        public const int MinValue = 0;
        public const int MaxValue = 9;

        public int value;

        public Face(int value)
        {
            this.value = value;
        }
    }
}
