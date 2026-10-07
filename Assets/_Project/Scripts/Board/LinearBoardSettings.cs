using System;

namespace SaiNoMichi.Board
{
    /// <summary>
    /// フェーズ0の直線盤面の設定（Docs/tasks/phase0-prototype.md）。
    /// フェーズ1で分岐マップと LayerConfig（ScriptableObject）に置き換える。
    /// </summary>
    [Serializable]
    public class LinearBoardSettings
    {
        public int goalIndex = 20;          // マス0がスタート、マス20がゴール（ボス）
        public int leadingEmptyCount = 3;   // マス1〜3は空白だけ
        public int battleCount = 6;
        public int restCount = 3;
        public int guaranteedRestMin = 14;  // マス14〜17のどこかに休憩を1つ必ず置く
        public int guaranteedRestMax = 17;
        public int maxSameRun = 2;          // 同じ種類のマスは2つまで連続
        public int maxEmptyRun = 3;         // 空白は3つまで連続
        public int maxAttempts = 10000;
    }
}
