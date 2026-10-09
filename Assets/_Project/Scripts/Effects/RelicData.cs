using System.Collections.Generic;
using SaiNoMichi.Core;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>レリック：一度手に入れるとずっと効く効果のまとまり（仕様書 第10章）。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Relic Data", fileName = "Relic_")]
    public class RelicData : ScriptableObject, IEffectSource
    {
        public string id;
        public string displayName;
        public Rarity rarity;
        [TextArea]
        public string description;
        public Sprite icon;
        [Tooltip("ボスレリック（ボスを倒したときに3つから選ぶ。良い効果と悪い効果がある）")]
        public bool isBoss;
        [Tooltip("この層まで出る（1から数える）。0 なら全部の層で出る。早馬は第3層では出さない（開発者の要望）")]
        public int maxLayer;
        public List<EffectSO> effects = new List<EffectSO>();

        public EffectSourceKind Kind => EffectSourceKind.Relic;
        public IReadOnlyList<EffectSO> Effects => effects;
    }
}
