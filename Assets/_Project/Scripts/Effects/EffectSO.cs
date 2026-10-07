using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 効果の部品。「いつ（trigger）」「何をするか（Apply）」を持つ。
    /// 部品を組み合わせて、ダイスの特徴・刻印・レリック・状態異常を作る。
    /// </summary>
    public abstract class EffectSO : ScriptableObject
    {
        public Trigger trigger;

        [TextArea]
        public string note;

        public abstract void Apply(EffectContext ctx);
    }
}
