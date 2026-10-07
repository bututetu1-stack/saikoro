using System.Collections.Generic;
using SaiNoMichi.Core;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    public enum EngravingKind
    {
        Numeric,  // 面の数値を変える（増強・削り・写し・金剛）。効果刻印と併用できる
        Effect,   // 面に残り、その面が出たときに効く（刃・堅・風・小判 など）。1面に1つ、上書き
    }

    public enum NumericOp
    {
        Add,       // value に amount を足す
        Set,       // value を amount にする
        CopyFace,  // 同じダイスの別の面の数値をコピーする（写し。フェーズ1では未使用）
    }

    /// <summary>刻印（仕様書 第5章）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Engraving Data", fileName = "Engraving_")]
    public class EngravingData : ScriptableObject, IEffectSource
    {
        public string id;
        public string displayName;
        [Tooltip("ダイスの面に重ねて出す1文字（例：刃）")]
        public string badge;
        public Rarity rarity;
        public int price;
        [TextArea]
        public string description;
        public EngravingKind kind;

        [Header("数値刻印")]
        public NumericOp op;
        public int amount;

        [Header("効果刻印")]
        public List<EffectSO> effects = new List<EffectSO>();

        public EffectSourceKind Kind => EffectSourceKind.Engraving;
        public IReadOnlyList<EffectSO> Effects => effects;
    }
}
