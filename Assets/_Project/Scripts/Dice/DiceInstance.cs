using System;
using System.Collections.Generic;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Dice
{
    /// <summary>ポーチに入っているダイス1個。面は DiceData からコピーし、鍛冶で個別に書き換わる。</summary>
    public class DiceInstance : IEffectSource
    {
        public DiceData data;
        public Face[] faces = new Face[DiceData.FaceCount];
        public DiceState state = DiceState.Available;

        public string DisplayName => data != null ? data.displayName : "?";

        // ダイスそのものの特徴を効果として出す（刻印は面ごとに別の持ち主として扱う）
        public EffectSourceKind Kind => EffectSourceKind.Dice;
        public IReadOnlyList<EffectSO> Effects => data != null ? data.effects : null;

        public DiceInstance(DiceData data)
        {
            this.data = data;
            for (int i = 0; i < DiceData.FaceCount; i++)
            {
                faces[i] = new Face(data.faceValues[i]);
            }
        }

        /// <summary>出た面の番号（0〜5）を返す。状態は変えない（使用済みにするのは DicePouch.Use）。</summary>
        public int RollFaceIndex(Random rng)
        {
            return rng.Next(DiceData.FaceCount);
        }

        public int Roll(Random rng)
        {
            return faces[RollFaceIndex(rng)].value;
        }
    }
}
