using System.Collections.Generic;
using SaiNoMichi.Dice;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>テスト用に DiceData を作り、終わったら破棄する。</summary>
    public class TestDice
    {
        readonly List<DiceData> created = new List<DiceData>();

        public DiceData Data(string id, params int[] faces)
        {
            var data = ScriptableObject.CreateInstance<DiceData>();
            data.id = id;
            data.displayName = id;
            data.faceValues = faces;
            created.Add(data);
            return data;
        }

        public DiceInstance Normal() => new DiceInstance(Data("normal", 1, 2, 3, 4, 5, 6));
        public DiceInstance High() => new DiceInstance(Data("456", 4, 4, 5, 5, 6, 6));
        public DiceInstance Low() => new DiceInstance(Data("123", 1, 1, 2, 2, 3, 3));

        public void DestroyAll()
        {
            foreach (var d in created) Object.DestroyImmediate(d);
            created.Clear();
        }
    }
}
