using System.Collections.Generic;
using SaiNoMichi.Core;
using SaiNoMichi.Effects;
using UnityEngine;

namespace SaiNoMichi.Dice
{
    /// <summary>ダイスの種類ごとの定義データ。改造後の面は DiceInstance が個別に持つ。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Dice Data", fileName = "Dice_")]
    public class DiceData : ScriptableObject
    {
        public const int FaceCount = 6;
        // 鍛冶で変えられるのは 0〜9（Face.MaxValue）だが、博打賽の 10 のように元から大きい面はある
        const int MaxDefinedValue = 20;

        public string id;
        public string displayName;
        public Rarity rarity;
        public int price;
        [Tooltip("ダイスの札に出す短い説明（例：防御+2／攻撃−1）")]
        public string description;
        public int[] faceValues = { 1, 2, 3, 4, 5, 6 };
        [Tooltip("ダイスそのものの特徴（盾賽の「防御に回すと+2」など）")]
        public List<EffectSO> effects = new List<EffectSO>();

        void OnValidate()
        {
            if (faceValues == null || faceValues.Length != FaceCount)
            {
                System.Array.Resize(ref faceValues, FaceCount);
            }
            for (int i = 0; i < faceValues.Length; i++)
            {
                faceValues[i] = Mathf.Clamp(faceValues[i], Face.MinValue, MaxDefinedValue);
            }
        }
    }
}
