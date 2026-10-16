using System;
using UnityEngine;

namespace SaiNoMichi.Effects
{
    /// <summary>
    /// 育ち賽：振るたびに、そのダイスのいちばん小さい面を +amount（max まで）。鍛冶と同じく、そのダイスにずっと残る。
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Effects/Grow Face", fileName = "Fx_GrowFace")]
    public class GrowFaceEffect : EffectSO
    {
        public int amount = 1;
        public int max = 6;

        public override void Apply(EffectContext ctx)
        {
            var die = ctx.dice;
            if (die == null || die.faces == null) return;
            int face = -1;
            for (int i = 0; i < die.faces.Length; i++)
            {
                if (die.faces[i].value >= max) continue;
                if (face < 0 || die.faces[i].value < die.faces[face].value) face = i;
            }
            if (face < 0) return; // もう全部の面が max
            var f = die.faces[face];
            f.value = Math.Min(max, f.value + amount);
            die.faces[face] = f;
        }
    }
}
