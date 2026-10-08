using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
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
    public static class DataBuilder
    {
        const string DiceDir = "Assets/_Project/Data/Dice";
        const string EffectDir = "Assets/_Project/Data/Effects";
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        /// <summary>フェーズ1のデータをまとめて作り、分岐する盤面を使う設定にする。</summary>
        [MenuItem("SaiNoMichi/Build All Data")]
        public static void BuildAll()
        {
            BuildDice();
            BuildEnemies();
            BuildEngravings();
            BuildRelics();
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.useBranchingBoard = true;
                // 盤面の設定（骨組み・出現率など）はコードの既定値に揃える（分岐3回・横道・空白マスなし）
                config.layerBoard = new Board.LayerBoardSettings();
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[賽ノ道] すべてのデータを作成・更新し、分岐する盤面を有効にしました。");
        }

        [MenuItem("SaiNoMichi/Data/Build Dice")]
        public static void BuildDice()
        {
            EnsureFolder("Assets/_Project/Data", "Effects");

            // 盾賽・剣賽の割り振り補正（仕様書 第4章）
            var tateBlock = AddValue("Fx_Tate_Block", Trigger.OnAssignDefense, +2, "盾賽：防御に回すと+2");
            var tateAttack = AddValue("Fx_Tate_Attack", Trigger.OnAssignAttack, -1, "盾賽：攻撃に回すと－1");
            var kenAttack = AddValue("Fx_Ken_Attack", Trigger.OnAssignAttack, +2, "剣賽：攻撃に回すと+2");
            var kenBlock = AddValue("Fx_Ken_Block", Trigger.OnAssignDefense, -1, "剣賽：防御に回すと－1");

            var normal = Dice("normal", "普通の賽", Rarity.Common, 40, "", new[] { 1, 2, 3, 4, 5, 6 });
            var hifumi = Dice("hifumi", "一二三賽", Rarity.Common, 50, "移動の微調整", new[] { 1, 1, 2, 2, 3, 3 });
            var tate = Dice("tate", "盾賽", Rarity.Common, 50, "防御+2／攻撃－1", new[] { 1, 2, 3, 4, 5, 6 }, tateBlock, tateAttack);
            var ken = Dice("ken", "剣賽", Rarity.Common, 50, "攻撃+2／防御－1", new[] { 1, 2, 3, 4, 5, 6 }, kenAttack, kenBlock);
            var cho = Dice("cho", "丁賽", Rarity.Common, 50, "偶数のみ", new[] { 2, 2, 4, 4, 6, 6 });
            var han = Dice("han", "半賽", Rarity.Common, 50, "奇数のみ", new[] { 1, 1, 3, 3, 5, 5 });
            var bakuchi = Dice("bakuchi", "博打賽", Rarity.Uncommon, 80, "0か10の二択", new[] { 0, 0, 0, 10, 10, 10 });
            var shigoroku = Dice("shigoroku", "四五六賽", Rarity.Rare, 130, "近くには止まれない", new[] { 4, 4, 5, 5, 6, 6 });
            // ---- 追加分（開発者の要望で、仕様書 第4章の一覧から） ----
            var golden = Effect<GainGoldByValueEffect>("Fx_Ougon_Gold", Trigger.OnMoveRolled, "黄金賽：移動で使うと出目と同じゴールド");
            golden.percent = 100;
            EditorUtility.SetDirty(golden);
            var goldenAtk = AddValue("Fx_Ougon_Attack", Trigger.OnAssignAttack, -1, "黄金賽：戦闘では出目－1（攻撃）");
            var goldenBlk = AddValue("Fx_Ougon_Block", Trigger.OnAssignDefense, -1, "黄金賽：戦闘では出目－1（防御）");
            var poison = Effect<ApplyPoisonEffect>("Fx_Doku_Poison", Trigger.OnAttackResolve, "毒賽：攻撃に置くと出目と同じ毒");
            poison.flat = 0;
            poison.perPip = 1; // 開発者の判断：固定2だとダイスが回らず毒を重ねにくい。×2は強すぎるので出目×1
            poison.condition = new EffectCondition { assignment = Battle.Assignment.Attack };
            EditorUtility.SetDirty(poison);
            // 開発者の判断：毒賽の攻撃値は0（毒だけを与える）
            var poisonNoAttack = Effect<ScaleEffect>("Fx_Doku_NoAttack", Trigger.OnAssignAttack, "毒賽：攻撃値は0（毒だけを与える）");
            poisonNoAttack.target = ScaleTarget.Value;
            poisonNoAttack.percent = 0;
            poisonNoAttack.condition = default;
            EditorUtility.SetDirty(poisonNoAttack);
            var rust = Effect<SelfDamageEffect>("Fx_Sabi_Damage", Trigger.OnRoll, "錆び賽：振るたびに自分に1ダメージ");
            rust.damage = 1;
            EditorUtility.SetDirty(rust);

            var niren = Dice("niren", "二連賽", Rarity.Uncommon, 80, "中央寄りで安定", new[] { 2, 3, 3, 4, 4, 5 });
            var saiku = Dice("saiku", "細工賽", Rarity.Uncommon, 80, "6が出やすい", new[] { 1, 2, 3, 4, 6, 6 });
            var ougon = Dice("ougon", "黄金賽", Rarity.Uncommon, 80, "移動で出目ぶんのG／戦闘では－1", new[] { 1, 2, 3, 4, 5, 6 }, golden, goldenAtk, goldenBlk);
            var doku = Dice("doku", "毒賽", Rarity.Uncommon, 80, "攻撃0・出目と同じ毒を与える", new[] { 1, 2, 3, 4, 5, 6 }, poisonNoAttack, poison);
            var pinzoro = Dice("pinzoro", "ピンゾロ賽", Rarity.Uncommon, 80, "使用済みにならない／鍛冶不可", new[] { 1, 1, 1, 1, 1, 1 });
            pinzoro.keepAvailable = true;
            pinzoro.cannotForge = true;
            var baku = Dice("baku", "爆賽", Rarity.Rare, 130, "6が出たら振り足す", new[] { 1, 2, 3, 4, 5, 6 });
            baku.explodeOn = 6;
            var kagami = Dice("kagami", "鏡賽", Rarity.Rare, 130, "直前の出目を写す（最初は3）", new[] { 3, 3, 3, 3, 3, 3 });
            kagami.mirror = true;
            // フェーズ2：2体の敵と戦うためのダイス（開発者の判断）。全員に当たるぶん出目は小さめ
            // TODO(仕様): 薙ぎ賽の出目とレア度は仮
            var nagi = Dice("nagi", "薙ぎ賽", Rarity.Uncommon, 80, "攻撃に置くと全部の敵に当たる", new[] { 1, 2, 2, 3, 3, 4 });
            nagi.hitsAll = true;
            EditorUtility.SetDirty(nagi);
            var oo = Dice("oo", "大賽", Rarity.Rare, 130, "戦闘専用（移動に使えない）", new[] { 3, 4, 5, 6, 7, 8 });
            oo.cannotMove = true;
            foreach (var d in new[] { pinzoro, baku, kagami, oo }) EditorUtility.SetDirty(d);

            // 呪い：罠やイベントで押し付けられる。報酬・ショップには出ない
            var kake = Dice("kake", "欠け賽", Rarity.Curse, 0, "呪い：手放せない", new[] { 0, 0, 1, 1, 2, 2 });
            var sabi = Dice("sabi", "錆び賽", Rarity.Curse, 0, "呪い：振るたびに1ダメージ", new[] { 1, 2, 3, 4, 5, 6 }, rust);

            // 初期構成：普通の賽・一二三賽・四五六賽 ＋ スターター（剣賽・盾賽・博打賽から1つ）
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                // 開発者の判断：最初から近く（一二三）・遠く（四五六）を選べるようにする
                config.startingDice = new List<DiceData> { normal, hifumi, shigoroku };
                config.starterChoices = new List<DiceData> { ken, tate, bakuchi };
                config.rewardDicePool = new List<DiceData>
                {
                    normal, hifumi, tate, ken, cho, han, bakuchi, shigoroku,
                    niren, saiku, ougon, doku, pinzoro, baku, kagami, oo, nagi,
                };
                config.curseDice = kake;
                config.curseDicePool = new List<DiceData> { kake, sabi };
                EditorUtility.SetDirty(config);
            }
            else
            {
                Debug.LogWarning($"[賽ノ道] {ConfigPath} がありません。先に SaiNoMichi/Phase0/Build Scene を実行してください。");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[賽ノ道] ダイスのデータを作成・更新しました。");
        }

        // ---- 刻印（仕様書 第5章。フェーズ1は6種） ----

        const string EngravingDir = "Assets/_Project/Data/Engravings";

        [MenuItem("SaiNoMichi/Data/Build Engraving")]
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
                Engraving("kezuri", "削り", "－", Rarity.Common, 60, "面の数値－1（移動の調整用）", EngravingKind.Numeric, NumericOp.Add, -1),
                Engraving("yaiba", "刃", "刃", Rarity.Common, 60, "攻撃に置くと攻撃値+3", EngravingKind.Effect, NumericOp.Add, 0, blade),
                Engraving("kata", "堅", "堅", Rarity.Common, 60, "防御に置くと防御値+3", EngravingKind.Effect, NumericOp.Add, 0, guard),
                Engraving("koban", "小判", "金", Rarity.Common, 60, "この面が出たら3Gを得る（移動でも戦闘でも）", EngravingKind.Effect, NumericOp.Add, 0, koban),
                Engraving("kaze", "風", "風", Rarity.Uncommon, 90, "移動で出たら、止まるマスを出目±1から選べる", EngravingKind.Effect, NumericOp.Add, 0, wind),
            };

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.engravingPool = list;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[賽ノ道] 刻印6種のデータを作成・更新しました。");
        }

        // ---- レリック（仕様書 第10章。フェーズ1は10種） ----

        const string RelicDir = "Assets/_Project/Data/Relics";
        const string RelicArtDir = "Assets/_Project/Art/Relic";

        [MenuItem("SaiNoMichi/Data/Build Relic")]
        public static void BuildRelics()
        {
            EnsureFolder("Assets/_Project/Data", "Relics");
            EnsureFolder("Assets/_Project/Data", "Effects");
            var blade = AssetDatabase.LoadAssetAtPath<EngravingData>($"{EngravingDir}/Engraving_yaiba.asset");
            var guard = AssetDatabase.LoadAssetAtPath<EngravingData>($"{EngravingDir}/Engraving_kata.asset");
            if (blade == null || guard == null) Debug.LogWarning("[賽ノ道] 刻印「刃」「堅」がありません。先に Build Engraving Data を実行してください（砥石が効きません）。");

            var waraji = Effect<ChargedMoveAdjustEffect>("Fx_Relic_Waraji", Trigger.OnMoveRolled, "草鞋：移動の出目を±1できる（層ごとに3回）");
            waraji.range = 1;
            waraji.chargesPerLayer = 3;
            waraji.label = "草鞋";

            var zeni = Effect<ScaleEffect>("Fx_Relic_Zenibukuro", Trigger.OnGoldGain, "銭袋：戦闘で得るゴールド+25%");
            zeni.target = ScaleTarget.Amount;
            zeni.percent = 125;
            zeni.condition = new EffectCondition { battleGoldOnly = true };

            var tate = Effect<GainBlockEffect>("Fx_Relic_Kinotate", Trigger.OnBattleStart, "木の盾：戦闘開始時に防御5");
            tate.block = 5;
            tate.condition = default;

            var cho = AddValue("Fx_Relic_Chonofuda", Trigger.OnAssignAttack, +1, "丁の札：偶数の出目を攻撃に置くと+1");
            cho.condition = new EffectCondition { parity = ParityCondition.Even, assignment = Battle.Assignment.Attack };
            var han = AddValue("Fx_Relic_Hannofuda", Trigger.OnAssignDefense, +1, "半の札：奇数の出目を防御に置くと+1");
            han.condition = new EffectCondition { parity = ParityCondition.Odd, assignment = Battle.Assignment.Block };

            var koishi = Effect<KeepAvailableEffect>("Fx_Relic_Koishi", Trigger.OnRoll, "小石：1が出たダイスは使用済みにならない");
            koishi.condition = new EffectCondition { minValue = 1, maxValue = 1 };

            var toishiBlade = Effect<EngravingBonusEffect>("Fx_Relic_Toishi_Blade", Trigger.OnAssignAttack, "砥石：刻印「刃」の効果+1");
            toishiBlade.engraving = blade;
            toishiBlade.add = 1;
            toishiBlade.condition = default;
            var toishiGuard = Effect<EngravingBonusEffect>("Fx_Relic_Toishi_Guard", Trigger.OnAssignDefense, "砥石：刻印「堅」の効果+1");
            toishiGuard.engraving = guard;
            toishiGuard.add = 1;
            toishiGuard.condition = default;

            var kinchaku = Effect<PouchCapacityEffect>("Fx_Relic_Kinchaku", Trigger.OnAcquire, "大きな巾着：ポーチの容量+1");
            kinchaku.amount = 1;

            var suzuBattle = Effect<GainStrengthEffect>("Fx_Relic_Suzu_Battle", Trigger.OnRefresh, "鈴：戦闘中にリフレッシュしたら筋力+1");
            suzuBattle.strength = 1;
            suzuBattle.condition = new EffectCondition { scene = SceneCondition.Battle };
            var suzuMap = Effect<HealEffect>("Fx_Relic_Suzu_Map", Trigger.OnRefresh, "鈴：移動中にリフレッシュしたらHP3回復");
            suzuMap.heal = 3;
            suzuMap.condition = new EffectCondition { scene = SceneCondition.Map };

            var saitou = Effect<DicePerRoundEffect>("Fx_Relic_Furuisaitou", Trigger.OnBattleStart, "古い賽筒：戦闘で1ラウンドに振れるダイス+1（最大3個）");
            saitou.add = 1;
            var hayauma = Effect<ScaleEffect>("Fx_Relic_Hayauma", Trigger.OnMoveRolled, "早馬：各層の最初の移動は出目×2");
            hayauma.target = ScaleTarget.Value;
            hayauma.percent = 200;
            hayauma.condition = new EffectCondition { firstMoveOfLayer = true };

            foreach (var e in new EffectSO[] { waraji, zeni, tate, cho, han, koishi, toishiBlade, toishiGuard, kinchaku, suzuBattle, suzuMap, hayauma, saitou })
            {
                EditorUtility.SetDirty(e);
            }

            var list = new List<RelicData>
            {
                Relic("waraji", "草鞋", Rarity.Common, "移動の出目を±1できる（層ごとに3回）", waraji),
                Relic("zenibukuro", "銭袋", Rarity.Common, "戦闘で得るゴールド+25%", zeni),
                Relic("kinotate", "木の盾", Rarity.Common, "戦闘開始時に防御5", tate),
                Relic("chonofuda", "丁の札", Rarity.Common, "偶数の出目を攻撃に置くと+1", cho),
                Relic("hannofuda", "半の札", Rarity.Common, "奇数の出目を防御に置くと+1", han),
                Relic("koishi", "小石", Rarity.Common, "1が出たダイスは使用済みにならない", koishi),
                Relic("toishi", "砥石", Rarity.Common, "刻印「刃」「堅」の効果+1", toishiBlade, toishiGuard),
                Relic("kinchaku", "大きな巾着", Rarity.Common, "ポーチの容量+1", kinchaku),
                Relic("suzu", "鈴", Rarity.Uncommon, "リフレッシュしたとき、戦闘中なら筋力+1、移動中ならHP3回復", suzuBattle, suzuMap),
                Relic("hayauma", "早馬", Rarity.Uncommon, "各層の最初の移動は出目×2", hayauma),
                Relic("furuisaitou", "古い賽筒", Rarity.Rare, "戦闘で1ラウンドに振れるダイス+1（最大3個）", saitou),
            };

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.relicPool = list;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] レリック{list.Count}種のデータを作成・更新しました。");
        }

        static RelicData Relic(string id, string name, Rarity rarity, string description, params EffectSO[] effects)
        {
            var path = $"{RelicDir}/Relic_{id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<RelicData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<RelicData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.id = id;
            data.displayName = name;
            data.rarity = rarity;
            data.description = description;
            data.effects = new List<EffectSO>(effects);
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{RelicArtDir}/relic_{id}.png");
            if (icon != null) data.icon = icon;
            else Debug.LogWarning($"[賽ノ道] レリックの絵 {RelicArtDir}/relic_{id}.png がありません。");
            EditorUtility.SetDirty(data);
            return data;
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
        static Intent Seal(int count = 0) => new Intent(IntentType.Seal, count);
        static Intent Poison(int v) => new Intent(IntentType.Poison, v);
        static Intent Charge(int staggerAt = 0) => new Intent(IntentType.Charge, staggerAt);
        static Intent Mirror() => new Intent(IntentType.MirrorAttack, 0);
        static Intent Curse() => new Intent(IntentType.Curse, 0);
        static Intent RollAtk(int multiplier, int faces) => new Intent(IntentType.RollAttack, multiplier) { maxValue = faces };
        static Intent RollBlk(int multiplier, int faces) => new Intent(IntentType.RollBlock, multiplier) { maxValue = faces };
        static Intent Rewrite() => new Intent(IntentType.RewriteFate, 0);

        [MenuItem("SaiNoMichi/Data/Build Enemy")]
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

            // ---- 第2層：鍾乳洞（仕様書 第7章） ----
            var koumori = Enemy("koumori", "大蝙蝠", EnemyKind.Normal, 16, EnemyBehavior.Sequence, true, Multi(3, 2));
            var gaikotsu = Enemy("gaikotsu", "骸骨兵", EnemyKind.Normal, 24, EnemyBehavior.Sequence, true, Blk(8), Atk(9));
            var dokugumo = Enemy("dokugumo", "毒蜘蛛", EnemyKind.Normal, 20, EnemyBehavior.Sequence, true, Poison(3), Atk(6));
            // 溜めは止められない（数字なし）。防御12の次に溜め、そのあと大攻撃16
            var iwa = Enemy("iwaningyou", "岩の人形", EnemyKind.Normal, 34, EnemyBehavior.Sequence, false, Blk(12), Charge(), Atk(16));
            // 前のラウンドのプレイヤーの攻撃値をそのまま返す（大きく攻めた次は守る）
            var utsushi = Enemy("utsushikagami", "写し鏡", EnemyKind.Elite, 50, EnemyBehavior.Sequence, false, Mirror());
            // 攻撃10 → 封印×2 → 溜め（12以上で怯む）→ 攻撃25 の4ラウンド周期
            var ooago = Enemy("ooago", "大顎", EnemyKind.Boss, 100, EnemyBehavior.Sequence, false, Atk(10), Seal(2), Charge(12), Atk(25));

            // ---- 第3層：鬼の城（仕様書 第7章） ----
            var jujutsushi = Enemy("jujutsushi", "呪術師", EnemyKind.Normal, 30, EnemyBehavior.Sequence, true, Curse(), Atk(8), Atk(8));
            var onimusha = Enemy("onimusha", "鬼武者", EnemyKind.Normal, 40, EnemyBehavior.Sequence, false, Atk(12), Blk(15));
            // 22×2体。片方を倒すと、残った方が筋力+3
            // TODO(仕様): 双子鬼・石の守護者・首狩りの行動は仕様書にないので仮
            var futago = Enemy("futagooni", "双子鬼", EnemyKind.Normal, 22, EnemyBehavior.Sequence, true, Atk(5), Multi(3, 2), Blk(6));
            futago.count = 2;
            futago.allyDefeatedStrength = 3;
            var shugosha = Enemy("ishinoshugosha", "石の守護者", EnemyKind.Normal, 36, EnemyBehavior.Sequence, true, Blk(8), Atk(10));
            shugosha.damageCapPerRound = 10;
            var kubikari = Enemy("kubikari", "首狩り", EnemyKind.Elite, 80, EnemyBehavior.Sequence, false, Atk(9), Multi(4, 2), Blk(10));
            kubikari.enrageHpPercent = 50;
            kubikari.enrageAttackPercent = 200;
            // 第1形態：8面ダイスの出目×2の攻撃と出目×3の防御を交互（出目は予告で見える）
            // 第2形態（HP 半分以下）：運命の書き換え → 3ラウンドごとに繰り返す
            var hachimen = Enemy("hachimen", "賽の神・八面", EnemyKind.Boss, 180, EnemyBehavior.Sequence, false, RollAtk(2, 8), RollBlk(3, 8));
            hachimen.phase2HpPercent = 50;
            hachimen.phase2Pattern = new List<Intent> { Rewrite(), RollAtk(2, 8), RollBlk(3, 8) };
            foreach (var e in new[] { futago, shugosha, kubikari, hachimen }) EditorUtility.SetDirty(e);

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.battleEnemies = new List<EnemyData> { slime, usagi, koni, kinoko };
                config.eliteEnemies = new List<EnemyData> { thief };
                config.boss = banjin;
                config.earlyBattleCount = 3;

                // 3層（仕様書 第2章・第8章）。第2・第3層の敵はフェーズ2の手順3・5で作るまで、第1層の敵で仮に埋める
                var layer1 = new List<EnemyData> { slime, usagi, koni, kinoko };
                var layer2 = new List<EnemyData> { koumori, gaikotsu, dokugumo, iwa };
                var layer3 = new List<EnemyData> { jujutsushi, onimusha, futago, shugosha };
                config.layers = new List<LayerData>
                {
                    Layer("野原の街道", LayerWeights(25, 7, 10, 2), layer1, thief, banjin),
                    // 第2層（仕様書 第7章「第2層：鍾乳洞」）
                    Layer("鍾乳洞", LayerWeights(22, 10, 9, 3), layer2, utsushi, ooago),
                    // 第3層（仕様書 第7章「第3層：鬼の城」）
                    Layer("鬼の城", LayerWeights(20, 11, 8, 5), layer3, kubikari, hachimen),
                };
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[賽ノ道] 敵のデータ（第1〜第3層 18体）を作成・更新しました。");
        }

        static LayerData Layer(string name, List<TileWeight> weights, List<EnemyData> enemies, EnemyData elite, EnemyData boss)
        {
            var board = new LayerBoardSettings { weights = weights };
            return new LayerData
            {
                displayName = name,
                board = board,
                battleEnemies = new List<EnemyData>(enemies),
                eliteEnemies = new List<EnemyData> { elite },
                boss = boss,
                earlyBattleCount = 3,
            };
        }

        /// <summary>
        /// 層ごとの出現率（仕様書 第8章の表）。戦闘30・宝箱12・ショップ7・鍛冶7 は全層共通。
        /// 空白をなくしたぶんはイベントに寄せている（第1層 25%）。
        /// </summary>
        static List<TileWeight> LayerWeights(int evt, int trap, int rest, int elite)
        {
            return new List<TileWeight>
            {
                new TileWeight(TileType.Battle, 30),
                new TileWeight(TileType.Event, evt),
                new TileWeight(TileType.Trap, trap),
                new TileWeight(TileType.Rest, rest),
                new TileWeight(TileType.Treasure, 12),
                new TileWeight(TileType.Shop, 7),
                new TileWeight(TileType.Forge, 7),
                new TileWeight(TileType.Elite, elite),
            };
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
            // 特性は呼び出し側で必要なときだけ付ける（作り直すたびに戻す）
            data.damageCapPerRound = 0;
            data.enrageHpPercent = 0;
            data.enrageAttackPercent = 200;
            data.count = 1;
            data.allyDefeatedStrength = 0;
            data.phase2HpPercent = 0;
            data.phase2Pattern = new List<Intent>();
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
            // 特別なルールはいったん消し、必要なダイスだけ呼び出し側で付け直す
            data.keepAvailable = false;
            data.cannotForge = false;
            data.cannotMove = false;
            data.explodeOn = 0;
            data.mirror = false;
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
