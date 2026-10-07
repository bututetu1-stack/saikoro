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
        public List<EffectSO> effects = new List<EffectSO>();

        public EffectSourceKind Kind => EffectSourceKind.Relic;
        public IReadOnlyList<EffectSO> Effects => effects;
    }
}
