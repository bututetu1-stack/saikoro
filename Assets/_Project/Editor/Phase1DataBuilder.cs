using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using UnityEditor;
using UnityEngine;

namespace SaiNoMichi.EditorTools
{
    /// <summary>
    /// フェーズ1のデータ（ダイス8種と、その特徴の効果・第1層の敵）を作る・更新する。
    /// 何度実行してもよい。数値は仕様書 第4章の表に合わせて上書きする（インスペクターで変えた値も戻るので注意）。
    /// </summary>
    public static class Phase1DataBuilder
    {
        const string DiceDir = "Assets/_Project/Data/Dice";
        const string EffectDir = "Assets/_Project/Data/Effects";
        const string ConfigPath = "Assets/_Project/Data/Phase0Config.asset";

        /// <summary>フェーズ1のデータをまとめて作り、分岐する盤面を使う設定にする。</summary>
        [MenuItem("SaiNoMichi/Phase1/Build All Data")]
        public static void BuildAll()
        {
            BuildDice();
            BuildEnemies();
            BuildEngravings();
            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null)
            {
                config.useBranchingBoard = true;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[Phase1] すべてのデータを作成・更新し、分岐する盤面を有効にしました。");
        }

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
            // 呪い：罠やイベントで押し付けられる。報酬・ショップには出ない
            var kake = Dice("kake", "欠け賽", Rarity.Curse, 0, "呪い：手放せない", new[] { 0, 0, 1, 1, 2, 2 });

            // 初期構成：普通の賽×3 ＋ スターター（一二三賽・盾賽・博打賽から1つ）
            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null)
            {
                config.startingDice = new List<DiceData> { normal, normal, normal };
                config.starterChoices = new List<DiceData> { hifumi, tate, bakuchi };
                config.rewardDicePool = new List<DiceData> { normal, hifumi, tate, ken, cho, han, bakuchi, shigoroku };
                config.curseDice = kake;
                EditorUtility.SetDirty(config);
            }
            else
            {
                Debug.LogWarning($"[Phase1] {ConfigPath} がありません。先に SaiNoMichi/Phase0/Build Scene を実行してください。");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1] ダイス8種のデータを作成・更新しました。");
        }

        // ---- 刻印（仕様書 第5章。フェーズ1は6種） ----

        const string EngravingDir = "Assets/_Project/Data/Engravings";

        [MenuItem("SaiNoMichi/Phase1/Build Engraving Data")]
        public static void BuildEngravings()
        {
            EnsureFolder("Assets/_Project/Data", "Engravings");
            EnsureFolder("Assets/_Project/Data", "Effects");

            var blade = AddValue("Fx_Engr_Blade", Trigger.OnAssignAttack, +3, "刻印「刃」：攻撃に置くと+3");
            var guard = AddValue("Fx_Engr_Guard", Trigger.OnAssignDefense, +3, "刻印「堅」：防御に置くと+3");
            var koban = Effect<GainGoldEffect>("Fx_Engr_Koban", Trigger.OnRoll, "刻印「小判」：この面が出たら3G");
            koban.gold = 3;
            koban.condition = default;
            var wind = Effect<MoveAdjustEffect>("Fx_Engr_Wind", Trigger.OnMoveRolled, "刻印「風」：移動で出たら、出目±1から止まるマスを選べる");
            wind.range = 1;
            EditorUtility.SetDirty(koban);
            EditorUtility.SetDirty(wind);

            var list = new List<EngravingData>
            {
                Engraving("zoukyou", "増強", "＋", Rarity.Common, 60, "面の数値+2", EngravingKind.Numeric, NumericOp.Add, +2),
                Engraving("kezuri", "削り", "－", Rarity.Common, 60, "面の数値−1（移動の調整用）", EngravingKind.Numeric, NumericOp.Add, -1),
                Engraving("yaiba", "刃", "刃", Rarity.Common, 60, "攻撃に置くと攻撃値+3", EngravingKind.Effect, NumericOp.Add, 0, blade),
                Engraving("kata", "堅", "堅", Rarity.Common, 60, "防御に置くと防御値+3", EngravingKind.Effect, NumericOp.Add, 0, guard),
                Engraving("koban", "小判", "金", Rarity.Common, 60, "この面が出たら3Gを得る（移動でも戦闘でも）", EngravingKind.Effect, NumericOp.Add, 0, koban),
                Engraving("kaze", "風", "風", Rarity.Uncommon, 90, "移動で出たら、止まるマスを出目±1から選べる", EngravingKind.Effect, NumericOp.Add, 0, wind),
            };

            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null)
            {
                config.engravingPool = list;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1] 刻印6種のデータを作成・更新しました。");
        }

        static EngravingData Engraving(string id, string name, string badge, Rarity rarity, int price, string description,
            EngravingKind kind, NumericOp op, int amount, params EffectSO[] effects)
        {
            var path = $"{EngravingDir}/Engraving_{id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<EngravingData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EngravingData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.id = id;
            data.displayName = name;
            data.badge = badge;
            data.rarity = rarity;
            data.price = price;
            data.description = description;
            data.kind = kind;
            data.op = op;
            data.amount = amount;
            data.effects = new List<EffectSO>(effects);
            EditorUtility.SetDirty(data);
            return data;
        }

        static T Effect<T>(string fileName, Trigger trigger, string note) where T : EffectSO
        {
            var path = $"{EffectDir}/{fileName}.asset";
            var effect = AssetDatabase.LoadAssetAtPath<T>(path);
            if (effect == null)
            {
                effect = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(effect, path);
            }
            effect.trigger = trigger;
            effect.note = note;
            EditorUtility.SetDirty(effect);
            return effect;
        }

        // ---- 敵（仕様書 第7章 第1層） ----

        const string EnemyDir = "Assets/_Project/Data/Enemies";

        static Intent Atk(int v) => new Intent(IntentType.Attack, v);
        static Intent Blk(int v) => new Intent(IntentType.Block, v);
        static Intent Buff(int v) => new Intent(IntentType.Buff, v);
        static Intent Multi(int v, int hits) => new Intent(IntentType.MultiAttack, v, hits);
        static Intent Weak(int v) => new Intent(IntentType.Debuff, v);
        static Intent Seal() => new Intent(IntentType.Seal, 0);

        [MenuItem("SaiNoMichi/Phase1/Build Enemy Data")]
        public static void BuildEnemies()
        {
            var slime = Enemy("slime", "スライム", EnemyKind.Normal, 12, EnemyBehavior.Sequence, true, Atk(5), Atk(5), Blk(4));
            // TODO(仕様): 野ウサギは本来2体で出る想定。フェーズ1は1体なので HP を 8 → 14 に上げる
            // 行動はランダムをやめて交互に（行動を読めるように。開発者の方針）
            var usagi = Enemy("usagi", "野ウサギ", EnemyKind.Normal, 14, EnemyBehavior.Sequence, true, Multi(2, 2), Atk(4));
            var koni = Enemy("koni", "小鬼", EnemyKind.Normal, 15, EnemyBehavior.Sequence, false, Buff(1), Atk(6), Atk(6));
            var kinoko = Enemy("kinoko", "化け茸", EnemyKind.Normal, 14, EnemyBehavior.Sequence, true, Weak(1), Atk(4), Atk(4));
            var thief = Enemy("sainusubito", "賽盗人", EnemyKind.Elite, 32, EnemyBehavior.Sequence, false, Seal(), Atk(7), Multi(3, 3));
            // 賽振りはやめ、決まった行動の繰り返しに（開発者の判断）。4ラウンドごとの「振り出しに戻れ」は残す
            // TODO(仕様): 攻撃8 → 防御10 → 攻撃12 の値は仮
            var banjin = Enemy("banjin", "双六の番人", EnemyKind.Boss, 55, EnemyBehavior.Sequence, false,
                Atk(8), Blk(10), Atk(12), new Intent(IntentType.ResetDice, 0));

            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null)
            {
                config.battleEnemies = new List<EnemyData> { slime, usagi, koni, kinoko };
                config.eliteEnemies = new List<EnemyData> { thief };
                config.boss = banjin;
                config.earlyBattleCount = 3;
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Phase1] 第1層の敵6体のデータを作成・更新しました。");
        }

        static EnemyData Enemy(string id, string name, EnemyKind kind, int hp, EnemyBehavior behavior, bool earlyOk, params Intent[] pattern)
        {
            var path = $"{EnemyDir}/Enemy_{id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EnemyData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.id = id;
            data.displayName = name;
            data.kind = kind;
            data.maxHp = hp;
            data.behavior = behavior;
            data.earlyOk = earlyOk;
            data.pattern = new List<Intent>(pattern);
            EditorUtility.SetDirty(data);
            return data;
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
