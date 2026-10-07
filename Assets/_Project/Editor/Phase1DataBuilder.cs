using System.Collections.Generic;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using UnityEditor;
using UnityEngine;

namespace SaiNoMichi.EditorTools
{
    /// <summary>
    /// フェーズ1のデータ（ダイス8種と、その特徴の効果）を作る・更新する。
    /// 何度実行してもよい。数値は仕様書 第4章の表に合わせて上書きする（インスペクターで変えた値も戻るので注意）。
    /// </summary>
    public static class Phase1DataBuilder
    {
        const string DiceDir = "Assets/_Project/Data/Dice";
        const string EffectDir = "Assets/_Project/Data/Effects";
        const string ConfigPath = "Assets/_Project/Data/Phase0Config.asset";

        [MenuItem("SaiNoMichi/Phase1/Build Dice Data")]
        public static void BuildDice()
        {
            EnsureFolder("Assets/_Project/Data", "Effects");

            // 盾賽・剣賽の割り振り補正（仕様書 第4章）
            var tateBlock = AddValue("Fx_Tate_Block", Trigger.OnAssignDefense, +2, "盾賽：防御に回すと+2");
            var tateAttack = AddValue("Fx_Tate_Attack", Trigger.OnAssignAttack, -1, "盾賽：攻撃に回すと−1");
            var kenAttack = AddValue("Fx_Ken_Attack", Trigger.OnAssignAttack, +2, "剣賽：攻撃に回すと+2");
            var kenBlock = AddValue("Fx_Ken_Block", Trigger.OnAssignDefense, -1, "剣賽：防御に回すと−1");

            var normal = Dice("normal", "普通の賽", Rarity.Common, 40, "", new[] { 1, 2, 3, 4, 5, 6 });
            var hifumi = Dice("hifumi", "一二三賽", Rarity.Common, 50, "移動の微調整", new[] { 1, 1, 2, 2, 3, 3 });
            var tate = Dice("tate", "盾賽", Rarity.Common, 50, "防御+2／攻撃−1", new[] { 1, 2, 3, 4, 5, 6 }, tateBlock, tateAttack);
            var ken = Dice("ken", "剣賽", Rarity.Common, 50, "攻撃+2／防御−1", new[] { 1, 2, 3, 4, 5, 6 }, kenAttack, kenBlock);
            var cho = Dice("cho", "丁賽", Rarity.Common, 50, "偶数のみ", new[] { 2, 2, 4, 4, 6, 6 });
            var han = Dice("han", "半賽", Rarity.Common, 50, "奇数のみ", new[] { 1, 1, 3, 3, 5, 5 });
            var bakuchi = Dice("bakuchi", "博打賽", Rarity.Uncommon, 80, "0か10の二択", new[] { 0, 0, 0, 10, 10, 10 });
            var shigoroku = Dice("shigoroku", "四五六賽", Rarity.Rare, 130, "近くには止まれない", new[] { 4, 4, 5, 5, 6, 6 });

            // 初期構成：普通の賽×3 ＋ スターター（一二三賽・盾賽・博打賽から1つ）
            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null)
            {
                config.startingDice = new List<DiceData> { normal, normal, normal };
                config.starterChoices = new List<DiceData> { hifumi, tate, bakuchi };
                config.rewardDicePool = new List<DiceData> { normal, hifumi, tate, ken, cho, han, bakuchi, shigoroku };
                EditorUtility.SetDirty(config);
            }
            else
            {
                Debug.LogWarning($"[Phase1] {ConfigPath} がありません。先に SaiNoMichi/Phase0/Build Scene を実行してください。");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1] ダイス8種のデータを作成・更新しました。");
        }

        static DiceData Dice(string id, string name, Rarity rarity, int price, string description, int[] faces, params EffectSO[] effects)
        {
            var path = $"{DiceDir}/Dice_{id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<DiceData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<DiceData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.id = id;
            data.displayName = name;
            data.rarity = rarity;
            data.price = price;
            data.description = description;
            data.faceValues = faces;
            data.effects = new List<EffectSO>(effects);
            EditorUtility.SetDirty(data);
            return data;
        }

        static AddValueEffect AddValue(string fileName, Trigger trigger, int add, string note)
        {
            var path = $"{EffectDir}/{fileName}.asset";
            var effect = AssetDatabase.LoadAssetAtPath<AddValueEffect>(path);
            if (effect == null)
            {
                effect = ScriptableObject.CreateInstance<AddValueEffect>();
                AssetDatabase.CreateAsset(effect, path);
            }
            effect.trigger = trigger;
            effect.add = add;
            effect.condition = default;
            effect.note = note;
            EditorUtility.SetDirty(effect);
            return effect;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
