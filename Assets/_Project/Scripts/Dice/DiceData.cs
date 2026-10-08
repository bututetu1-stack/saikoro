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

        [Header("特別なルール（仕様書 第4章）")]
        [Tooltip("使っても使用済みにならない（ピンゾロ賽）")]
        public bool keepAvailable;
        [Tooltip("鍛冶で改造できない（ピンゾロ賽）")]
        public bool cannotForge;
        [Tooltip("移動に使えない。戦闘専用（大賽）")]
        public bool cannotMove;
        [Tooltip("この値の面が出たら振り足して加算する。0 ならしない（爆賽は 6）")]
        public int explodeOn;
        [Tooltip("直前に振ったダイスの出目を写す（鏡賽）")]
        public bool mirror;
        [Tooltip("攻撃に置くと、全部の敵に同じ値が当たる（薙ぎ賽）")]
        public bool hitsAll;

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
