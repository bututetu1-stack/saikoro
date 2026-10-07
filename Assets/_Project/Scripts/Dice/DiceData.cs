using UnityEngine;

namespace SaiNoMichi.Dice
{
    /// <summary>ダイスの種類ごとの定義データ。改造後の面は DiceInstance が個別に持つ。</summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/Dice Data", fileName = "Dice_")]
    public class DiceData : ScriptableObject
    {
        public const int FaceCount = 6;

        public string id;
        public string displayName;
        public int[] faceValues = { 1, 2, 3, 4, 5, 6 };

        void OnValidate()
        {
            if (faceValues == null || faceValues.Length != FaceCount)
            {
                System.Array.Resize(ref faceValues, FaceCount);
            }
            for (int i = 0; i < faceValues.Length; i++)
            {
                faceValues[i] = Mathf.Clamp(faceValues[i], Face.MinValue, Face.MaxValue);
            }
        }
    }
}
