using System;
using System.Collections.Generic;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Dice
{
    /// <summary>賽袋に入っているダイス1個。面は DiceData からコピーし、鍛冶で個別に書き換わる。</summary>
    public class DiceInstance : IEffectSource
    {
        public DiceData data;
        public Face[] faces;
        public DiceState state = DiceState.Available;

        public string DisplayName => data != null ? data.displayName : "?";

        // ダイスそのものの特徴を効果として出す（刻印は面ごとに別の持ち主として扱う）
        public EffectSourceKind Kind => EffectSourceKind.Dice;
        public IReadOnlyList<EffectSO> Effects => data != null ? data.effects : null;

        public DiceInstance(DiceData data)
        {
            this.data = data;
            // 面の数はダイスごと（ふつうは6。四面・八面・十二面・二十面のダイスもある）
            faces = new Face[data.faceValues.Length];
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = new Face(data.faceValues[i]);
            }
        }

        /// <summary>出た面の番号（0〜面の数−1）を返す。状態は変えない（使用済みにするのは DicePouch.Use）。</summary>
        public int RollFaceIndex(Random rng)
        {
            return rng.Next(faces.Length);
        }

        public int Roll(Random rng)
        {
            return faces[RollFaceIndex(rng)].value;
        }
    }
}
