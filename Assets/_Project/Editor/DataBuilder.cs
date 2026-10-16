using System.Collections.Generic;
using System.Linq;
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
    static class EffectExtensions
    {
        /// <summary>効果のきっかけを変える（攻撃に乗る効果を、戦闘開始時に使うレリックなど）。</summary>
        public static T WithTrigger<T>(this T effect, Trigger trigger) where T : EffectSO
        {
            effect.trigger = trigger;
            EditorUtility.SetDirty(effect);
            return effect;
        }
    }

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
            BuildCharms();
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.useBranchingBoard = true;
                // 盤面の設定（骨組み・出現率など）はコードの既定値に揃える（分岐3回・横道・空白マスなし）
                config.layerBoard = new Board.LayerBoardSettings();
                // イベントの種類も既定（12種）に揃える。数値はアセットで調整できるよう残す
                config.events.kinds = new Run.EventSettings().kinds;
                config.events.oncePerRun = new Run.EventSettings().oncePerRun;
                config.events.oncePerRun = new Run.EventSettings().oncePerRun;
                // 敵の強さの倍率（フェーズ2 手順13。1ラウンド3個振れるようにしたので全体に強くする。自動プレイで調整した値）
                // TODO(仕様): 目標は 通常戦 3〜4ラウンド・最大HPの10〜20%、ボス戦 7〜10ラウンド
                // レア度の配分（開発者の判断：コモンが多すぎた。レリック・刻印は均等だったのでレア度で重み付け）
                // 2026-10-16 開発者の要望で下げた（前は 55・35・10。3個から選ぶので、レアが毎回のように並んでいた）
                config.rewards.normalRarityWeights = new[] { 65, 32, 3 };
                config.rewards.eliteRarityWeights = new[] { 50, 40, 10 };
                config.shop.diceRarityWeights = new[] { 60, 33, 7 };
                config.rewards.relicRarity = new Run.RarityByLayer();       // 第1層 72・25・3 → 第2層 64・30・6 → 第3層 56・34・10
                config.rewards.engravingRarity = new Run.RarityByLayer();
                // 2026-10-16 基本の HP を 40 → 75 に（STS2 に寄せる）。ボスを倒すと全回復。HP に関わる数値もおよそ1.8倍にそろえる
                config.playerMaxHp = 75;
                config.layerClearHealPercent = 100;
                config.tiles.trapDamage = 9;
                config.tiles.checkpointDamage = 9;
                config.tiles.teahouseHeal = 5;
                config.events.shrineHpCost = 14;
                config.events.shrinePrayHeal = 9;
                config.events.pitfallDamage = 11;
                config.events.startOverMaxHp = 18;
                config.events.sumoDamage = 12;
                config.events.offeringMaxHp = 8;
                config.events.samuraiHpCost = 12;
                config.events.footBathHeal = 9;
                // 第2層・第3層は、古の神のレリック（2026-10-16）のぶん強くする（LaterHp・LaterAttack）
                config.enemyBalance = new EnemyBalance
                {
                    normalHpPercent = Later(new[] { 225, 195, 145 }, LaterHp),
                    // 攻撃は HP を 75 にしたのに合わせて EnemyAttackScale% 倍（自動プレイで踏破率がそろうように決めた）
                    normalAttackPercent = Later(Scale(new[] { 265, 200, 215 }), LaterAttack),
                    eliteHpPercent = Later(new[] { 170, 155, 125 }, LaterHp),
                    eliteAttackPercent = Later(Scale(new[] { 150, 140, 130 }), LaterAttack),
                    bossHpPercent = Later(new[] { 115, 120, 90 }, LaterHp),
                    bossAttackPercent = Later(Scale(new[] { 140, 130, 140 }), LaterAttack),
                };
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[賽ノ道] すべてのデータを作成・更新し、分岐する盤面を有効にしました。");
        }

        // TODO(仕様): HP を 75 にしたときの敵の攻撃の倍率（%）。自動プレイで踏破率が前（段位0で約38%）とそろうように決めた（190%だと1%、130%で36%）
        const int EnemyAttackScale = 129;
        static int[] Scale(int[] values) => values.Select(v => v * EnemyAttackScale / 100).ToArray();

        // TODO(仕様): 古の神のレリックを足したので、第2層・第3層の敵の HP と攻撃を上げる（%）。自動プレイで踏破率を見て決めた（2026-10-16 夕方、イベント・お守りが増えたぶん HP を 115 → 113 に。ダイスの見直しで報酬のダイスが少し弱くなったぶん）
        const int LaterHp = 113;
        const int LaterAttack = 110;
        static int[] Later(int[] values, int percent) => values.Select((v, i) => i == 0 ? v : v * percent / 100).ToArray();

        [MenuItem("SaiNoMichi/Data/Build Dice")]
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
            // 開発者の判断：始めから持っているダイスなのでアンコモンに（前はレア）
            var shigoroku = Dice("shigoroku", "四五六賽", Rarity.Common, 50, "近くには止まれない", new[] { 4, 4, 5, 5, 6, 6 });
            // ---- 追加分（開発者の要望で、仕様書 第4章の一覧から） ----
            var golden = Effect<GainGoldByValueEffect>("Fx_Ougon_Gold", Trigger.OnMoveRolled, "黄金賽：移動で使うと出目と同じゴールド");
            golden.percent = 100;
            EditorUtility.SetDirty(golden);
            var goldenAtk = AddValue("Fx_Ougon_Attack", Trigger.OnAssignAttack, -1, "黄金賽：戦闘では出目−1（攻撃）");
            var goldenBlk = AddValue("Fx_Ougon_Block", Trigger.OnAssignDefense, -1, "黄金賽：戦闘では出目−1（防御）");
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

            var niren = Dice("niren", "二連賽", Rarity.Common, 50, "中央寄りで安定", new[] { 2, 3, 3, 4, 4, 5 });
            var saiku = Dice("saiku", "細工賽", Rarity.Common, 50, "6が出やすい", new[] { 1, 2, 3, 4, 6, 6 });
            var ougon = Dice("ougon", "黄金賽", Rarity.Uncommon, 80, "移動で出目ぶんのG／戦闘では−1", new[] { 1, 2, 3, 4, 5, 6 }, golden, goldenAtk, goldenBlk);
            var doku = Dice("doku", "毒賽", Rarity.Uncommon, 80, "攻撃0・出目と同じ毒を与える", new[] { 1, 2, 3, 4, 5, 6 }, poisonNoAttack, poison);
            var pinzoro = Dice("pinzoro", "ピンゾロ賽", Rarity.Uncommon, 80, "戦闘では使用済みにならない／鍛冶不可", new[] { 1, 1, 1, 1, 1, 1 });
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
            // 開発者の要望：敵に脱力・弱体を与えるダイス（STS の「弱らせる」役）。毒賽と同じく、出目の数だけ与えて攻撃値は0
            // TODO(仕様): 萎え賽・砕き賽の出目・レア度・値段は仮
            var debuffNoAttack = Effect<ScaleEffect>("Fx_Debuff_NoAttack", Trigger.OnAssignAttack, "萎え賽・砕き賽：攻撃値は0（脱力・弱体だけを与える）");
            debuffNoAttack.target = ScaleTarget.Value;
            debuffNoAttack.percent = 0;
            debuffNoAttack.condition = default;
            EditorUtility.SetDirty(debuffNoAttack);
            var naeRider = Effect<AttackRiderEffect>("Fx_Nae_Weak", Trigger.OnAttackResolve, "萎え賽：攻撃に置くと、狙った敵に出目と同じ脱力");
            naeRider.rider = AttackRider.Weak;
            naeRider.amount = 0;
            naeRider.perPip = 1;
            EditorUtility.SetDirty(naeRider);
            var nae = Dice("nae", "萎え賽", Rarity.Uncommon, 80, "攻撃に置くと出目と同じ脱力（ダメージ0）", new[] { 1, 1, 2, 2, 3, 3 }, naeRider, debuffNoAttack);
            var kudakiRider = Effect<AttackRiderEffect>("Fx_Kudaki_Vulnerable", Trigger.OnAttackResolve, "砕き賽：攻撃に置くと、狙った敵に出目と同じ弱体");
            kudakiRider.rider = AttackRider.Vulnerable;
            kudakiRider.amount = 0;
            kudakiRider.perPip = 1;
            EditorUtility.SetDirty(kudakiRider);
            var kudaki = Dice("kudaki", "砕き賽", Rarity.Uncommon, 80, "攻撃に置くと出目と同じ弱体（ダメージ0）", new[] { 1, 1, 2, 2, 3, 3 }, kudakiRider, debuffNoAttack);

            // 開発者の要望で追加（種類を増やす）。TODO(仕様): 出目・レア度・値段は仮
            var hokorobiRider = Effect<AttackRiderEffect>("Fx_Hokorobi_Frail", Trigger.OnAttackResolve, "綻び賽：攻撃に置くと、狙った敵に出目と同じ脆弱");
            hokorobiRider.rider = AttackRider.Frail;
            hokorobiRider.amount = 0;
            hokorobiRider.perPip = 1;
            EditorUtility.SetDirty(hokorobiRider);
            var hokorobi = Dice("hokorobi", "綻び賽", Rarity.Uncommon, 80, "攻撃に置くと出目と同じ脆弱（ダメージ0）", new[] { 1, 1, 2, 2, 3, 3 }, hokorobiRider, debuffNoAttack);
            var chisuiRider = Effect<AttackRiderEffect>("Fx_Chisui_Lifesteal", Trigger.OnAttackResolve, "血吸い賽：攻撃に置くと、そのラウンドに与えたダメージの50%を回復");
            chisuiRider.rider = AttackRider.Lifesteal;
            // TODO(仕様): 10% では弱すぎた（1ラウンド15ダメージでも1回復）。出目を下げて（1〜3）、50% にした。値は仮
            // （血吸い賽を最初のダイスにした自動プレイ200回で、0%: 24.5%・30%: 32.0%・50%: 33.5%。ふつうの始まりの平均 39% より低い）
            // （出目1〜6で 50% だと、自動プレイのクリア率が 63% → 73% に跳ね上がった）
            chisuiRider.percent = 50;
            EditorUtility.SetDirty(chisuiRider);
            var chisui = Dice("chisui", "血吸い賽", Rarity.Rare, 130, "攻撃に置くと、そのラウンドに与えたダメージの50%回復", new[] { 1, 1, 2, 2, 3, 3 }, chisuiRider);
            var omoteuraFlag = Effect<FlagEffect>("Fx_Omoteura_Both", Trigger.OnRoll, "表裏賽：戦闘で1〜3が出たら攻撃にも防御にも使える");
            omoteuraFlag.flag = RollFlag.BothSides;
            omoteuraFlag.condition = new EffectCondition { minValue = 1, maxValue = 3, scene = SceneCondition.Battle };
            EditorUtility.SetDirty(omoteuraFlag);
            var omoteura = Dice("omoteura", "表裏賽", Rarity.Uncommon, 80, "1〜3が出たら攻撃にも防御にも", new[] { 1, 2, 3, 4, 5, 6 }, omoteuraFlag);


            // 開発者の要望で追加（2026-10-16）。TODO(仕様): 出目・レア度・値段はすべて仮
            var tengu = Dice("tengu", "天狗賽", Rarity.Rare, 130, "どの目も普通の賽より1つ大きい", new[] { 2, 3, 4, 5, 6, 7 });
            var oniAttack = AddValue("Fx_Oni_Attack", Trigger.OnAssignAttack, +1, "鬼賽：攻撃に回すと+1");
            var oni = Dice("oni", "鬼賽", Rarity.Rare, 130, "0か大きな目（攻撃+1）", new[] { 0, 0, 6, 7, 8, 9 }, oniAttack);
            var manekiGold = Effect<GainGoldByValueEffect>("Fx_Maneki_Gold", Trigger.OnMoveRolled, "招福賽：移動で使うと出目の半分のゴールド");
            manekiGold.percent = 50;
            EditorUtility.SetDirty(manekiGold);
            var maneki = Dice("maneki", "招福賽", Rarity.Common, 50, "移動で出目の半分のG（戦闘の弱みなし）", new[] { 1, 2, 3, 4, 5, 6 }, manekiGold);
            // 2026-10-16 開発者の要望（似た効果のダイスを減らす・6面以外のダイス）：霧賽（守り刀賽とほぼ同じだった）は八面賽に作り替え。id はそのまま（セーブのため）
            var kiri = Dice("kiri", "八面賽", Rarity.Uncommon, 80, "八面（1〜8）", new[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            kiri.cannotForge = true; // 鍛冶の画面は6面を前提にしている
            EditorUtility.SetDirty(kiri);
            var kaminari = Dice("kaminari", "雷賽", Rarity.Rare, 130, "攻撃に置くと全部の敵に当たり、6で振り足す", new[] { 1, 1, 2, 3, 4, 6 });
            kaminari.hitsAll = true;
            kaminari.explodeOn = 6;
            EditorUtility.SetDirty(kaminari);
            var hariRider = Effect<AttackRiderEffect>("Fx_Hari_Vulnerable", Trigger.OnAttackResolve, "針賽：攻撃に置くと、狙った敵に弱体1（ダメージもそのまま）");
            hariRider.rider = AttackRider.Vulnerable;
            hariRider.amount = 1;
            hariRider.perPip = 0;
            EditorUtility.SetDirty(hariRider);
            var hari = Dice("hari", "針賽", Rarity.Uncommon, 80, "攻撃に置くと弱体1も与える", new[] { 1, 2, 3, 3, 4, 5 }, hariRider);
            // 2026-10-16 追加（夜の作業の続き）。TODO(仕様): 出目・レア度・値段はすべて仮
            // 2026-10-16：盾賽（防御+2／攻撃−1）と似ていたので、2倍・0の極端なダイスに
            var kame = Dice("kame", "亀賽", Rarity.Uncommon, 80, "防御に置くと出目の2倍／攻撃に置くと0", new[] { 1, 2, 3, 4, 5, 6 },
                Fx<ScaleEffect>("Fx_Kame_Double", Trigger.OnAssignDefense, "亀賽：防御に置くと出目の2倍", e => { e.target = ScaleTarget.Value; e.percent = 200; e.condition = default; }),
                Fx<ScaleEffect>("Fx_Kame_NoAttack", Trigger.OnAssignAttack, "亀賽：攻撃に置くと0", e => { e.target = ScaleTarget.Value; e.percent = 0; e.condition = default; }));
            var yakushiHeal = Effect<HealEffect>("Fx_Yakushi_Heal", Trigger.OnMoveRolled, "薬師賽：移動で使うとHP2回復");
            yakushiHeal.heal = 2;
            yakushiHeal.condition = default;
            EditorUtility.SetDirty(yakushiHeal);
            var yakushi = Dice("yakushi", "薬師賽", Rarity.Common, 50, "移動で使うとHP2回復", new[] { 1, 2, 3, 4, 5, 6 }, yakushiHeal);
            var korobi = Dice("korobi", "転び賽", Rarity.Common, 50, "1が出たら振り足す", new[] { 1, 1, 3, 4, 5, 6 });
            korobi.explodeOn = 1;
            EditorUtility.SetDirty(korobi);
            var midare = Dice("midare", "乱れ賽", Rarity.Common, 50, "1か6しか出ない", new[] { 1, 1, 1, 6, 6, 6 });
            var kasaneKeep = Effect<KeepAvailableEffect>("Fx_Kasane_Keep", Trigger.OnRoll, "重ね賽：1が出たら使用済みにならない");
            kasaneKeep.condition = new EffectCondition { minValue = 1, maxValue = 1 };
            EditorUtility.SetDirty(kasaneKeep);
            var kasane = Dice("kasane", "重ね賽", Rarity.Uncommon, 80, "1が出たら使用済みにならない", new[] { 1, 1, 2, 3, 4, 5 }, kasaneKeep);
            // 旅人専用のダイス（その旅人のはじめのダイスにだけ入る。報酬・ショップには出ない）。TODO(仕様): 出目・効き目は仮
            var tabisaiMove = AddValue("Fx_Tabisai_Move", Trigger.OnRoll, +1, "旅賽：移動で使うと出目+1");
            tabisaiMove.condition = new EffectCondition { scene = SceneCondition.Map };
            EditorUtility.SetDirty(tabisaiMove);
            var tabisai = Dice("tabisai", "旅賽", Rarity.Uncommon, 0, "賽振り専用：移動で使うと出目+1", new[] { 1, 2, 3, 4, 5, 6 }, tabisaiMove);
            var ikasama = Dice("ikasama", "イカサマ賽", Rarity.Uncommon, 0, "博徒専用：大きい目が出やすい（鍛冶で改造できない）", new[] { 2, 3, 4, 5, 6, 6 });
            ikasama.cannotForge = true;
            EditorUtility.SetDirty(ikasama);
            var karakuri = Dice("karakuri", "からくり賽", Rarity.Uncommon, 0, "細工師専用：はじめから6の面に「崩し」、1の面に「堅」の刻印", new[] { 1, 2, 3, 4, 5, 6 });
            var shakujouBlock = AddValue("Fx_Shakujou_Block", Trigger.OnAssignDefense, +2, "錫杖賽：防御に回すと+2");
            var shakujouHeal = Effect<HealEffect>("Fx_Shakujou_Heal", Trigger.OnMoveRolled, "錫杖賽：移動で使うとHP1回復");
            shakujouHeal.heal = 1;
            shakujouHeal.condition = default;
            EditorUtility.SetDirty(shakujouHeal);
            var shakujou = Dice("shakujou", "錫杖賽", Rarity.Uncommon, 0, "山伏専用：防御に回すと+2・移動で使うとHP1回復", new[] { 1, 2, 3, 4, 5, 6 }, shakujouBlock, shakujouHeal);
            // レリック「式神の札」が戦闘のあいだだけ加える賽（報酬・ショップには出ない）
            var shikigami = Dice("shikigami", "式神賽", Rarity.Common, 0, "式神の札・分身賽：戦闘のあいだだけ加わる", new[] { 2, 2, 3, 3, 4, 4 });
            // みんなで遊ぶとき専用：国めぐりのサイコロ屋・協力の報酬にだけ出る
            var shoubu = Dice("shoubu", "勝負賽", Rarity.Uncommon, 80, "国めぐりの対決で+3", new[] { 1, 2, 3, 4, 5, 6 });
            var kizuna = Dice("kizuna", "絆賽", Rarity.Uncommon, 80, "防御に回すと+2（協力の報酬にだけ出る）", new[] { 1, 2, 3, 4, 5, 6 }, AddValue("Fx_Kizuna_Block", Trigger.OnAssignDefense, +2, "絆賽：防御に回すと+2"));
            // 2026-10-16 開発者の要望：連撃賽（出目の数だけ1ダメージ。1発ごとに筋力が乗る）と、自分を強くする賽。TODO(仕様): 出目・レア度・値段は仮
            var rengeki = Dice("rengeki", "連撃賽", Rarity.Uncommon, 80, "攻撃に置くと出目の数だけ1ダメージ（1発ごとに筋力が乗る）", new[] { 1, 2, 2, 3, 3, 4 });
            rengeki.multiHit = true;
            EditorUtility.SetDirty(rengeki);
            var kiaiFx = Effect<GainStrengthEffect>("Fx_Kiai_Strength", Trigger.OnAttackResolve, "気合賽：攻撃に使うと、そのあと筋力+1（この戦闘のあいだ）");
            kiaiFx.strength = 1;
            kiaiFx.condition = default;
            EditorUtility.SetDirty(kiaiFx);
            var kiai = Dice("kiai", "気合賽", Rarity.Uncommon, 80, "攻撃に使うと、そのあと筋力+1（この戦闘のあいだ）", new[] { 1, 1, 2, 2, 3, 3 }, kiaiFx);
            var kamaeFx = Effect<GainBlockEffect>("Fx_Kamae_Fortify", Trigger.OnBlockResolve, "構え賽：防御に使うと堅守1（ラウンドの終わりに防御が半分残る）");
            kamaeFx.block = 0;
            kamaeFx.fortify = 1;
            kamaeFx.condition = default;
            EditorUtility.SetDirty(kamaeFx);
            var kamae = Dice("kamae", "構え賽", Rarity.Common, 50, "防御に使うと堅守1（ラウンドの終わりに防御が半分残る）", new[] { 1, 2, 3, 3, 4, 5 }, kamaeFx);
            // ---- 2026-10-16 開発者の要望「ダイスを 100 個くらいに」で追加（優先度いちばん）。TODO(仕様): 出目・レア度・値段・効果の量はすべて仮 ----
            var C = Rarity.Common; var U = Rarity.Uncommon; var R = Rarity.Rare;
            var mapOnly = new EffectCondition { scene = SceneCondition.Map };
            var battleOnly = new EffectCondition { scene = SceneCondition.Battle };
            var added = new List<DiceData>
            {
                // 出目の形だけが違う賽
                // 2026-10-16：偶数賽・奇数賽は丁賽・半賽とまったく同じだったので作り替え（開発者の要望「出た目の数だけダイスを使用可能にする」）。id はそのまま
                Dice("guusu", "呼び戻し賽", R, 130, "振ると、出た目の数だけ使用済みのダイスを戻す（出目は0〜3）", new[] { 0, 0, 1, 1, 2, 3 },
                    Fx<ReturnUsedDieEffect>("Fx_Yobimodoshi_Return", Trigger.OnRoll, "呼び戻し賽：振ると、出た目の数だけ使用済みのダイスを戻す", e => { e.perPip = true; e.count = 0; })),
                Dice("kisuu", "育ち賽", C, 50, "振るたびに、いちばん小さい面+1（最大6。ずっと残る）", new[] { 1, 1, 2, 2, 3, 3 },
                    Fx<GrowFaceEffect>("Fx_Sodachi_Grow", Trigger.OnRoll, "育ち賽：振るたびに、いちばん小さい面+1", e => { e.amount = 1; e.max = 6; })),
                Dice("sanzan", "三三賽", C, 50, "いつも3。移動の狙いがつけやすい", new[] { 3, 3, 3, 3, 3, 3 }),
                Dice("ichishi", "一四賽", C, 50, "1か4", new[] { 1, 1, 4, 4, 4, 4 }),
                Dice("niroku", "二六賽", C, 50, "2か6", new[] { 2, 2, 2, 6, 6, 6 }),
                Dice("daishou", "大小賽", U, 80, "大きいか小さいか（1・2か8・9）", new[] { 1, 1, 2, 8, 9, 9 }),
                Dice("hebi", "蛇賽", U, 80, "1,1,2,3,5,8（数がうねるように伸びる）", new[] { 1, 1, 2, 3, 5, 8 }),
                Dice("hagane", "鋼賽", U, 80, "3〜5で安定（鍛冶で改造できない）", new[] { 3, 3, 4, 4, 5, 5 }),
                // 攻撃・防御で値が変わる賽
                Dice("yari", "槍賽", C, 50, "偶数の目で攻撃すると+2", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Yari_Even", Trigger.OnAssignAttack, +2, "槍賽：偶数の目で攻撃すると+2", new EffectCondition { parity = ParityCondition.Even })),
                Dice("yumi", "弓賽", C, 50, "奇数の目で攻撃すると+2", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Yumi_Odd", Trigger.OnAssignAttack, +2, "弓賽：奇数の目で攻撃すると+2", new EffectCondition { parity = ParityCondition.Odd })),
                Dice("kodachi", "小太刀賽", C, 50, "3以下の目で攻撃すると+2", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Kodachi_Small", Trigger.OnAssignAttack, +2, "小太刀賽：3以下の目で攻撃すると+2", new EffectCondition { maxValue = 3 })),
                Dice("tatami", "畳賽", C, 50, "3以下の目で防御すると+2", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Tatami_Small", Trigger.OnAssignDefense, +2, "畳賽：3以下の目で防御すると+2", new EffectCondition { maxValue = 3 })),
                Dice("tsuchi", "土賽", C, 50, "2〜4で、防御に置くと+1", new[] { 2, 2, 3, 3, 4, 4 },
                    AddValue("Fx_Tsuchi_Block", Trigger.OnAssignDefense, +1, "土賽：防御に置くと+1")),
                // 2026-10-16：剣賽（攻撃+2／防御−1）と似ていたので、2倍・0の極端なダイスに（亀賽の逆）
                Dice("gou", "剛賽", U, 80, "攻撃に置くと出目の2倍／防御に置くと0", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<ScaleEffect>("Fx_Gou_Double", Trigger.OnAssignAttack, "剛賽：攻撃に置くと出目の2倍", e => { e.target = ScaleTarget.Value; e.percent = 200; e.condition = default; }),
                    Fx<ScaleEffect>("Fx_Gou_NoBlock", Trigger.OnAssignDefense, "剛賽：防御に置くと0", e => { e.target = ScaleTarget.Value; e.percent = 0; e.condition = default; })),
                Dice("tessen", "鉄扇賽", U, 80, "攻撃にも防御にも+1", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Tessen_Attack", Trigger.OnAssignAttack, +1, "鉄扇賽：攻撃+1"), AddValue("Fx_Tessen_Block", Trigger.OnAssignDefense, +1, "鉄扇賽：防御+1")),
                Dice("oozuchi", "大槌賽", U, 80, "5以上の目で攻撃すると+3", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Oozuchi_Big", Trigger.OnAssignAttack, +3, "大槌賽：5以上の目で攻撃すると+3", new EffectCondition { minValue = 5 })),
                // 攻撃に乗る効果（弱体・脱力・脆弱・毒・足枷）
                Dice("shibire", "痺れ賽", U, 80, "攻撃に置くと脱力1も与える", new[] { 1, 2, 3, 3, 4, 5 },
                    Rider("Fx_Shibire_Weak", AttackRider.Weak, 1, 0, "痺れ賽：攻撃に置くと、狙った敵に脱力1")),
                Dice("nibu", "鈍り賽", U, 80, "攻撃に置くと脆弱1も与える", new[] { 1, 2, 3, 3, 4, 5 },
                    Rider("Fx_Nibu_Frail", AttackRider.Frail, 1, 0, "鈍り賽：攻撃に置くと、狙った敵に脆弱1")),
                Dice("kiba", "毒牙賽", U, 80, "攻撃に置くと毒2も与える（攻撃−1）", new[] { 1, 2, 3, 4, 5, 6 },
                    Poison("Fx_Kiba_Poison", 2, 0, "毒牙賽：攻撃に置くと、狙った敵に毒2"), AddValue("Fx_Kiba_Attack", Trigger.OnAssignAttack, -1, "毒牙賽：攻撃−1")),
                Dice("ashidome", "足止め賽", U, 80, "攻撃に置くと、狙った敵の予告した攻撃−2", new[] { 1, 2, 3, 3, 4, 5 },
                    Rider("Fx_Ashidome_Reduce", AttackRider.ReduceIntent, 2, 0, "足止め賽：攻撃に置くと、狙った敵の予告した攻撃−2")),
                // 自分を強くする賽
                Dice("tame", "溜め賽", U, 80, "防御に使うと、そのあと筋力+1（この戦闘のあいだ）", new[] { 1, 1, 2, 2, 3, 3 },
                    Fx<GainStrengthEffect>("Fx_Tame_Strength", Trigger.OnBlockResolve, "溜め賽：防御に使うと筋力+1", e => { e.strength = 1; e.condition = default; })),
                Dice("yoroi", "鎧賽", U, 80, "防御に置くと+1・使うと堅守1", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Yoroi_Block", Trigger.OnAssignDefense, +1, "鎧賽：防御に置くと+1"),
                    Fx<GainBlockEffect>("Fx_Yoroi_Fortify", Trigger.OnBlockResolve, "鎧賽：防御に使うと堅守1", e => { e.block = 0; e.fortify = 1; e.condition = default; })),
                Dice("iyashi", "癒し賽", C, 50, "防御に使うと HP1 回復", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<HealEffect>("Fx_Iyashi_Heal", Trigger.OnBlockResolve, "癒し賽：防御に使うと HP1 回復", e => { e.heal = 1; e.condition = default; })),
                Dice("ikari", "怒り賽", U, 80, "攻撃+3。ただし攻撃に使うと HP1 を失う", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Ikari_Attack", Trigger.OnAssignAttack, +3, "怒り賽：攻撃+3"),
                    Fx<SelfDamageEffect>("Fx_Ikari_Hurt", Trigger.OnAttackResolve, "怒り賽：攻撃に使うと HP1 を失う", e => e.damage = 1)),
                // お金・移動
                Dice("zeni", "銭賽", C, 50, "攻撃に使うと 2G", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<GainGoldEffect>("Fx_Zeni_Gold", Trigger.OnAttackResolve, "銭賽：攻撃に使うと 2G", e => { e.gold = 2; e.condition = default; })),
                Dice("senryou", "千両賽", U, 80, "6が出たら 10G（移動でも戦闘でも）", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<GainGoldEffect>("Fx_Senryou_Gold", Trigger.OnRoll, "千両賽：6が出たら 10G", e => { e.gold = 10; e.condition = new EffectCondition { minValue = 6 }; })),
                Dice("hayaashi", "早足賽", C, 50, "移動で出目+1／戦闘で−1", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Hayaashi_Move", Trigger.OnRoll, +1, "早足賽：移動で出目+1", mapOnly), Cond("Fx_Hayaashi_Battle", Trigger.OnRoll, -1, "早足賽：戦闘で出目−1", battleOnly)),
                // 2026-10-16：早足賽（移動+1）の強い版になっていたので、「移動で2倍」に
                Dice("idaten", "韋駄天賽", U, 80, "移動で使うと出目の2倍進む（出目は1〜3）／戦闘で−1", new[] { 1, 1, 2, 2, 3, 3 },
                    Fx<ScaleEffect>("Fx_Idaten_Double", Trigger.OnRoll, "韋駄天賽：移動で出目の2倍", e => { e.target = ScaleTarget.Value; e.percent = 200; e.condition = mapOnly; }),
                    Cond("Fx_Idaten_Battle", Trigger.OnRoll, -1, "韋駄天賽：戦闘で出目−1", battleOnly)),
                Dice("shippuu", "疾風賽", U, 80, "移動で使うと、止まるマスを出目±1から選べる", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<MoveAdjustEffect>("Fx_Shippuu_Wind", Trigger.OnMoveRolled, "疾風賽：移動で使うと出目±1から選べる", e => { e.range = 1; e.label = "疾風"; })),
                Dice("shinobi", "忍び賽", U, 80, "移動で1か2が出たら使用済みにならない", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<KeepAvailableEffect>("Fx_Shinobi_Keep", Trigger.OnRoll, "忍び賽：移動で1か2が出たら使用済みにならない", e => e.condition = new EffectCondition { scene = SceneCondition.Map, maxValue = 2 })),
                // しくみの違う賽
                Dice("kaiten", "回転賽", U, 80, "6が出たら使用済みにならない", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<KeepAvailableEffect>("Fx_Kaiten_Keep", Trigger.OnRoll, "回転賽：6が出たら使用済みにならない", e => e.condition = new EffectCondition { minValue = 6 })),
                Dice("futago", "双子賽", U, 80, "戦闘ではいつも攻撃と防御の両方に効く（出目は小さめ）", new[] { 1, 1, 2, 2, 2, 3 },
                    Fx<FlagEffect>("Fx_Futago_Both", Trigger.OnRoll, "双子賽：攻撃と防御の両方に効く", e => { e.flag = RollFlag.BothSides; e.condition = battleOnly; })),
                Dice("tsukuyomi", "月読賽", R, 130, "偶数の目は攻撃と防御の両方に効く", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<FlagEffect>("Fx_Tsukuyomi_Both", Trigger.OnRoll, "月読賽：偶数なら攻撃と防御の両方に効く", e => { e.flag = RollFlag.BothSides; e.condition = new EffectCondition { parity = ParityCondition.Even, scene = SceneCondition.Battle }; })),
                Dice("houou", "鳳凰賽", R, 130, "防御に使うと HP3 回復", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<HealEffect>("Fx_Houou_Heal", Trigger.OnBlockResolve, "鳳凰賽：防御に使うと HP3 回復", e => { e.heal = 3; e.condition = default; })),
                // 2026-10-16：大賽（3〜8・移動に使えない）とまったく同じだったので、十二面に
                Dice("ryuu", "龍賽", R, 130, "十二面（1〜12）。移動には使えない", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }),
                // 100 個めざして、もう少し
                Dice("mame", "豆賽", C, 50, "1か2（移動を細かく刻める）", new[] { 1, 1, 1, 2, 2, 2 }),
                Dice("nishiki", "錦賽", C, 50, "振るたびに 1G", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<GainGoldEffect>("Fx_Nishiki_Gold", Trigger.OnRoll, "錦賽：振るたびに 1G", e => { e.gold = 1; e.condition = default; })),
                Dice("shirokuro", "白黒賽", U, 80, "奇数の目は攻撃+2、偶数の目は防御+2", new[] { 1, 2, 3, 4, 5, 6 },
                    Cond("Fx_Shirokuro_Odd", Trigger.OnAssignAttack, +2, "白黒賽：奇数の目で攻撃すると+2", new EffectCondition { parity = ParityCondition.Odd }),
                    Cond("Fx_Shirokuro_Even", Trigger.OnAssignDefense, +2, "白黒賽：偶数の目で防御すると+2", new EffectCondition { parity = ParityCondition.Even })),
                Dice("onibi", "鬼火賽", U, 80, "攻撃に置くと毒1と弱体1も与える", new[] { 1, 2, 2, 3, 3, 4 },
                    Poison("Fx_Onibi_Poison", 1, 0, "鬼火賽：攻撃に置くと毒1"), Rider("Fx_Onibi_Vulnerable", AttackRider.Vulnerable, 1, 0, "鬼火賽：攻撃に置くと弱体1")),
                Dice("fukuwarai", "福笑い賽", U, 80, "0〜4か12（大当たり）", new[] { 0, 1, 2, 3, 4, 12 }),
                Dice("mamorigatana", "守り刀賽", R, 130, "5以上の目は攻撃と防御の両方に効く", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<FlagEffect>("Fx_Mamorigatana_Both", Trigger.OnRoll, "守り刀賽：5以上の目は攻撃と防御の両方に効く", e => { e.flag = RollFlag.BothSides; e.condition = new EffectCondition { minValue = 5, scene = SceneCondition.Battle }; })),
                Dice("suzunone", "鈴音賽", R, 130, "防御に使うと、使用済みのダイス1個を戻す", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<ReturnUsedDieEffect>("Fx_Suzunone_Return", Trigger.OnBlockResolve, "鈴音賽：防御に使うと使用済みのダイス1個を戻す", e => e.count = 1)),
            };
            // 2026-10-16：稲妻賽（全部の敵に当たる）は薙ぎ賽とほぼ同じだったので、四面賽に作り替え。id はそのまま
            var inazumaDie = Dice("inazumasai", "四面賽", U, 80, "四面（1〜4）。移動で使っても使用済みにならない", new[] { 1, 2, 3, 4 },
                Fx<KeepAvailableEffect>("Fx_Shimen_Keep", Trigger.OnRoll, "四面賽：移動で使っても使用済みにならない", e => e.condition = mapOnly));
            inazumaDie.cannotForge = true; // 鍛冶の画面は6面を前提にしている
            EditorUtility.SetDirty(inazumaDie);
            added.Add(inazumaDie);
            // 2026-10-16：爆賽（6で振り足し）とほぼ同じだったので、「全部の敵に連撃」に
            var hanabi = Dice("hanabi", "花火賽", U, 80, "攻撃に置くと、出目の数だけ全部の敵に1ダメージ（1発ごとに筋力が乗る）", new[] { 1, 2, 2, 3, 3, 4 });
            hanabi.hitsAll = true;
            hanabi.multiHit = true;
            EditorUtility.SetDirty(hanabi);
            added.Add(hanabi);
            var hagane = added.Find(d => d.id == "hagane");
            hagane.cannotForge = true;
            EditorUtility.SetDirty(hagane);
            var ryuu = added.Find(d => d.id == "ryuu");
            ryuu.cannotMove = true;
            ryuu.cannotForge = true; // 鍛冶の画面は6面を前提にしている
            EditorUtility.SetDirty(ryuu);
            var mukade = Dice("mukade", "百足賽", R, 130, "連撃（値の数だけ1ダメージ、1発ごとに筋力）・攻撃のあと毒1", new[] { 1, 2, 2, 3, 3, 4 },
                Poison("Fx_Mukade_Poison", 1, 0, "百足賽：攻撃に置くと、狙った敵に毒1"));
            mukade.multiHit = true;
            EditorUtility.SetDirty(mukade);
            added.Add(mukade);
            var hyakki = Dice("hyakki", "百鬼賽", R, 130, "連撃（値の数だけ1ダメージ、1発ごとに筋力）。出目が大きめ", new[] { 2, 3, 3, 4, 4, 5 });
            hyakki.multiHit = true;
            EditorUtility.SetDirty(hyakki);
            added.Add(hyakki);
            // 2026-10-16：全部の敵に当たる賽が多すぎた（稲妻・薙ぎ・雷・雷電・狐火）ので、雷電賽は二十面賽に作り替え。id はそのまま
            var raiden = Dice("raiden", "二十面賽", R, 130, "二十面（1〜20）。戦闘では出目−5（最低0）。移動には使えない",
                new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 },
                Cond("Fx_Nijuumen_Battle", Trigger.OnRoll, -5, "二十面賽：戦闘では出目−5", battleOnly));
            raiden.cannotMove = true;
            raiden.cannotForge = true;
            EditorUtility.SetDirty(raiden);
            added.Add(raiden);
            var kitsunebi = Dice("kitsunebi", "狐火賽", R, 130, "攻撃に置くと全部の敵に当たる・狙った敵に毒2", new[] { 1, 1, 2, 2, 3, 3 },
                Poison("Fx_Kitsunebi_Poison", 2, 0, "狐火賽：攻撃に置くと、狙った敵に毒2"));
            kitsunebi.hitsAll = true;
            EditorUtility.SetDirty(kitsunebi);
            added.Add(kitsunebi);
            // 2026-10-16 開発者の要望「サイコロの枠から外れたサイコロ」。TODO(仕様): 出目・レア度・値段は仮
            added.Add(Dice("bunshin", "分身賽", R, 130, "戦闘で振ると、式神賽（2〜4）が1個加わる（この戦闘のあいだ）", new[] { 1, 2, 2, 3, 3, 4 },
                Fx<TempDiceEffect>("Fx_Bunshin_Temp", Trigger.OnRoll, "分身賽：戦闘で振ると、式神賽が1個加わる", e => e.dice = shikigami)));
            added.Add(Dice("nidofuri", "二度振り賽", U, 80, "出目を見てから、1回だけ振り直せる（移動でも戦闘でも）", new[] { 1, 2, 3, 4, 5, 6 },
                Fx<FlagEffect>("Fx_Nidofuri_Reroll", Trigger.OnRoll, "二度振り賽：1回だけ振り直せる", e => { e.flag = RollFlag.CanReroll; e.condition = default; })));

            // ---- イベントでしか手に入らない賽（報酬・ショップには出ない。STS2 のイベント限定のカードのように） ----
            var eventDice = new List<DiceData>
            {
                Dice("youtou", "妖刀賽", R, 0, "イベント限定：攻撃+4。ただし振るたびに HP1 を失う", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Youtou_Attack", Trigger.OnAssignAttack, +4, "妖刀賽：攻撃+4"),
                    Fx<SelfDamageEffect>("Fx_Youtou_Hurt", Trigger.OnRoll, "妖刀賽：振るたびに HP1 を失う", e => e.damage = 1)),
                Dice("kyoumon", "経文賽", R, 0, "イベント限定：防御に使うと HP2 回復・攻撃に置くと脱力1", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<HealEffect>("Fx_Kyoumon_Heal", Trigger.OnBlockResolve, "経文賽：防御に使うと HP2 回復", e => { e.heal = 2; e.condition = default; }),
                    Rider("Fx_Kyoumon_Weak", AttackRider.Weak, 1, 0, "経文賽：攻撃に置くと脱力1")),
                Dice("tennyo", "天女賽", R, 0, "イベント限定：防御に置くと+2・移動で使うと HP1 回復", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Tennyo_Block", Trigger.OnAssignDefense, +2, "天女賽：防御に置くと+2"),
                    Fx<HealEffect>("Fx_Tennyo_Heal", Trigger.OnMoveRolled, "天女賽：移動で使うと HP1 回復", e => { e.heal = 1; e.condition = default; })),
                Dice("zashiki", "座敷童子賽", R, 0, "イベント限定：移動で使うと 3G・6が出たら使用済みにならない", new[] { 1, 2, 3, 4, 5, 6 },
                    Fx<GainGoldEffect>("Fx_Zashiki_Gold", Trigger.OnMoveRolled, "座敷童子賽：移動で使うと 3G", e => { e.gold = 3; e.condition = default; }),
                    Fx<KeepAvailableEffect>("Fx_Zashiki_Keep", Trigger.OnRoll, "座敷童子賽：6が出たら使用済みにならない", e => e.condition = new EffectCondition { minValue = 6 })),
                Dice("hoshifuri", "星降る賽", R, 0, "イベント限定：6が出たら振り足して加算・攻撃に置くと全部の敵に当たる", new[] { 1, 2, 3, 4, 5, 6 }),
                Dice("kanabou", "鬼の金棒賽", R, 0, "イベント限定：4〜9の大きな目。移動には使えない・鍛冶で改造できない", new[] { 4, 5, 6, 7, 8, 9 }),
                Dice("kappazara", "河童の皿賽", R, 0, "イベント限定：防御に置くと+3・攻撃に置くと毒1", new[] { 1, 2, 3, 4, 5, 6 },
                    AddValue("Fx_Kappazara_Block", Trigger.OnAssignDefense, +3, "河童の皿賽：防御に置くと+3"),
                    Poison("Fx_Kappazara_Poison", 1, 0, "河童の皿賽：攻撃に置くと毒1")),
                Dice("tamamushi", "玉虫賽", R, 0, "イベント限定：振るたびに 2G・いつも攻撃と防御の両方に効く（出目は小さめ）", new[] { 1, 1, 2, 2, 3, 3 },
                    Fx<GainGoldEffect>("Fx_Tamamushi_Gold", Trigger.OnRoll, "玉虫賽：振るたびに 2G", e => { e.gold = 2; e.condition = default; }),
                    Fx<FlagEffect>("Fx_Tamamushi_Both", Trigger.OnRoll, "玉虫賽：攻撃と防御の両方に効く", e => { e.flag = RollFlag.BothSides; e.condition = battleOnly; })),
            };
            var hoshifuri = eventDice.Find(d => d.id == "hoshifuri");
            hoshifuri.explodeOn = 6;
            hoshifuri.hitsAll = true;
            EditorUtility.SetDirty(hoshifuri);
            var kanabouDie = eventDice.Find(d => d.id == "kanabou");
            kanabouDie.cannotMove = true;
            kanabouDie.cannotForge = true;
            EditorUtility.SetDirty(kanabouDie);

            // 呪い：罠やイベントで押し付けられる。報酬・ショップには出ない
            var kake = Dice("kake", "欠け賽", Rarity.Curse, 0, "呪い：手放せない", new[] { 0, 0, 1, 1, 2, 2 });
            var sabi = Dice("sabi", "錆び賽", Rarity.Curse, 0, "呪い：振るたびに1ダメージ", new[] { 1, 2, 3, 4, 5, 6 }, rust);
            // 2026-10-16 開発者の要望で呪いを増やす。TODO(仕様): 量は仮
            var ekibyou = Dice("ekibyou", "疫病賽", Rarity.Curse, 0, "呪い：戦闘で振るたびに、自分に毒1", new[] { 1, 2, 3, 4, 5, 6 },
                Fx<CurseEffect>("Fx_Curse_Ekibyou", Trigger.OnRoll, "疫病賽：戦闘で振るたびに、自分に毒1", e => { e.kind = CurseKind.SelfPoison; e.amount = 1; }));
            var binbou = Dice("binbougami", "貧乏神の賽", Rarity.Curse, 0, "呪い：振るたびに 3G 失う", new[] { 1, 2, 3, 4, 5, 6 },
                Fx<CurseEffect>("Fx_Curse_Binbougami", Trigger.OnRoll, "貧乏神の賽：振るたびに 3G 失う", e => { e.kind = CurseKind.LoseGold; e.amount = 3; }));
            var jubaku = Dice("jubaku", "呪縛賽", Rarity.Curse, 0, "呪い：戦闘で振ると縛り（このラウンドはもう振れず、次のラウンドも1個だけ）", new[] { 1, 1, 2, 2, 3, 3 },
                Fx<CurseEffect>("Fx_Curse_Jubaku", Trigger.OnRoll, "呪縛賽：戦闘で振ると縛り", e => { e.kind = CurseKind.SelfBind; e.amount = 1; }));
            var urami = Dice("urami", "怨み賽", Rarity.Curse, 0, "呪い：戦闘で振るたびに、狙っている敵の筋力+1", new[] { 1, 2, 3, 4, 5, 6 },
                Fx<CurseEffect>("Fx_Curse_Urami", Trigger.OnRoll, "怨み賽：戦闘で振るたびに、狙っている敵の筋力+1", e => { e.kind = CurseKind.EnemyStrength; e.amount = 1; }));

            // 初期構成：普通の賽・一二三賽・四五六賽 ＋ スターター（剣賽・盾賽・博打賽から1つ）
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                // 開発者の判断：最初から近く（一二三）・遠く（四五六）を選べるようにする
                // 開発者の判断：始めに持つダイスはスターターを入れて5個（普通の賽を2個に）
                config.startingDice = new List<DiceData> { normal, normal, hifumi, shigoroku };
                config.starterChoices = new List<DiceData> { ken, tate, bakuchi };
                config.rewardDicePool = new List<DiceData>
                {
                    // 開発者の判断：普通の賽は報酬・ショップに出さない（目新しさがないため）
                    hifumi, tate, ken, cho, han, bakuchi, shigoroku,
                    niren, saiku, ougon, doku, pinzoro, baku, kagami, oo, nagi, nae, kudaki,
                    hokorobi, chisui, omoteura,
                    tengu, oni, maneki, kiri, kaminari, hari,
                    kame, yakushi, korobi, midare, kasane,
                    rengeki, kiai, kamae,
                };
                config.rewardDicePool.AddRange(added);
                // 2026-10-16 開発者の要望：アンコモンがコモンより多かったので、効果が素直なアンコモンをコモンに（コモン＞アンコモン＞レア）
                var demote = new HashSet<string> { "daishou", "hebi", "hagane", "shibire", "hari", "nibu", "kasane", "omoteura", "kiba", "ashidome", "shinobi", "tessen" };
                foreach (var d in config.rewardDicePool.Where(d => d != null && demote.Contains(d.id)))
                {
                    d.rarity = Rarity.Common;
                    d.price = 50;
                    EditorUtility.SetDirty(d);
                }
                config.eventDicePool = eventDice;
                config.travelerDicePool = new List<DiceData> { tabisai, ikasama, karakuri, shakujou };
                // みんなで遊ぶとき専用のダイス（国めぐりのサイコロ屋・協力の報酬にだけ出る）
                config.multiplayerDicePool = new List<DiceData> { shoubu, kizuna };
                config.curseDice = kake;
                config.curseDicePool = new List<DiceData> { kake, sabi, ekibyou, binbou, jubaku, urami };
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
                Engraving("kezuri", "削り", "−", Rarity.Common, 60, "面の数値−1（移動の調整用）", EngravingKind.Numeric, NumericOp.Add, -1),
                Engraving("yaiba", "刃", "刃", Rarity.Common, 60, "攻撃に置くと攻撃値+3", EngravingKind.Effect, NumericOp.Add, 0, blade),
                Engraving("kata", "堅", "堅", Rarity.Common, 60, "防御に置くと防御値+3", EngravingKind.Effect, NumericOp.Add, 0, guard),
                Engraving("koban", "小判", "金", Rarity.Common, 60, "この面が出たら3Gを得る（移動でも戦闘でも）", EngravingKind.Effect, NumericOp.Add, 0, koban),
                Engraving("kaze", "風", "風", Rarity.Uncommon, 90, "移動で出たら、止まるマスを出目±1から選べる", EngravingKind.Effect, NumericOp.Add, 0, wind),
            };
            list.AddRange(BuildPhase2Engravings());

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.engravingPool = list;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] 刻印{list.Count}種のデータを作成・更新しました。");
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
            var suzuMap = Effect<HealEffect>("Fx_Relic_Suzu_Map", Trigger.OnRefresh, "鈴：移動中にリフレッシュしたらHP5回復");
            suzuMap.heal = 5;
            suzuMap.condition = new EffectCondition { scene = SceneCondition.Map };

            var saitou = Effect<DicePerRoundEffect>("Fx_Relic_Furuisaitou", Trigger.OnBattleStart, "古い賽筒：戦闘で1ラウンドに振れるダイス+1（最大4個）");
            saitou.add = 1;
            var hayauma = Effect<ScaleEffect>("Fx_Relic_Hayauma", Trigger.OnMoveRolled, "早馬：各層の最初の移動は出目×2");
            hayauma.target = ScaleTarget.Value;
            hayauma.percent = 200;
            hayauma.condition = new EffectCondition { firstMoveOfLayer = true };

            foreach (var e in new EffectSO[] { waraji, zeni, tate, cho, han, koishi, toishiBlade, toishiGuard, kinchaku, suzuBattle, suzuMap, hayauma, saitou })
            {
                EditorUtility.SetDirty(e);
            }

            // ---- フェーズ2で足すレリック（仕様書 第10章） ----
            var yakusou = Effect<StatBonusEffect>("Fx_Relic_Yakusoubukuro", Trigger.OnAcquire, "薬草袋：休憩の回復量+50%");
            yakusou.stat = RunStat.RestHealPercent;
            yakusou.amount = 50;
            var zorome = Effect<PairBothSidesEffect>("Fx_Relic_Zoromenomamori", Trigger.OnRoll, "ゾロ目の守り：同じラウンドに同じ出目が2つ出たら、両方を攻撃にも防御にも使える");
            var tsumiishi = Effect<ScaleEffect>("Fx_Relic_Tsumiishi", Trigger.OnRoll, "積み石：1か2の出目は2倍");
            tsumiishi.target = ScaleTarget.Value;
            tsumiishi.percent = 200;
            tsumiishi.condition = new EffectCondition { minValue = 1, maxValue = 2 };
            var dokutsubo = Effect<StatBonusEffect>("Fx_Relic_Dokutsubo", Trigger.OnAcquire, "毒壺：与える毒+1");
            dokutsubo.stat = RunStat.PoisonBonus;
            dokutsubo.amount = 1;
            var tenbin = Effect<BalanceHealEffect>("Fx_Relic_Tenbin", Trigger.OnRoundEnd, "天秤：攻撃値と防御値が同じラウンドの終わりにHP5回復");
            tenbin.heal = 5;
            var yatate = Effect<RevealMapEffect>("Fx_Relic_Chizushinoyatate", Trigger.OnAcquire, "地図師の矢立：戦闘マスの敵が見える");
            var fusha = Effect<GainGoldEffect>("Fx_Relic_Fusha", Trigger.OnRefresh, "風車：リフレッシュするたびに5G");
            fusha.gold = 5;
            fusha.condition = default;
            var chokin = Effect<StepGoldEffect>("Fx_Relic_Chokinbako", Trigger.OnStep, "貯金箱：10マス進むごとに8G");
            chokin.steps = 10;
            chokin.gold = 8;
            var unmei = Effect<FateThreadEffect>("Fx_Relic_Unmeinoito", Trigger.OnBattleStart, "運命の糸：1戦闘に1回、振ったダイス1個の出目を好きな値（1〜6）にできる");
            unmei.maxValue = 6;
            var roku = Effect<FlagEffect>("Fx_Relic_Rokunokago", Trigger.OnRoll, "六の加護：戦闘で6が出たダイスは攻撃にも防御にも使える");
            roku.flag = RollFlag.BothSides;
            roku.condition = new EffectCondition { minValue = 6, maxValue = 6, scene = SceneCondition.Battle };
            var kanazuchi = Effect<StatBonusEffect>("Fx_Relic_Kajinokanazuchi", Trigger.OnAcquire, "鍛冶の金槌：鍛冶で刻印をもう1つ付けられる");
            kanazuchi.stat = RunStat.ForgeExtraEngravings;
            kanazuchi.amount = 1;
            var senrigan = Effect<ForesightEffect>("Fx_Relic_Senrigan", Trigger.OnMoveRolled, "千里眼：移動の前に、次に振るダイスの出目が見える");

            foreach (var e in new EffectSO[] { yakusou, zorome, tsumiishi, dokutsubo, tenbin, yatate, fusha, chokin, unmei, roku, kanazuchi, senrigan })
            {
                EditorUtility.SetDirty(e);
            }

            // 開発者の要望で追加（種類を増やす）。TODO(仕様): 数値・レア度は仮
            var chikaramizu = Effect<GainStrengthEffect>("Fx_Relic_Chikaramizu", Trigger.OnBattleStart, "力水：戦闘開始時に筋力+1");
            chikaramizu.strength = 1;
            chikaramizu.condition = default;
            var juzu = Effect<GainBlockEffect>("Fx_Relic_Juzu", Trigger.OnRefresh, "数珠：戦闘中にリフレッシュしたら防御6");
            juzu.block = 6;
            juzu.condition = new EffectCondition { scene = SceneCondition.Battle };
            var nigirimeshi = Effect<MaxHpEffect>("Fx_Relic_Nigirimeshi", Trigger.OnAcquire, "握り飯：最大HP+14");
            nigirimeshi.amount = 14;
            var mitsudomoe = AddValue("Fx_Relic_Mitsudomoe", Trigger.OnRoll, +3, "三つ巴：3が出たら出目+3");
            mitsudomoe.condition = new EffectCondition { minValue = 3, maxValue = 3 };
            foreach (var e in new EffectSO[] { chikaramizu, juzu, nigirimeshi, mitsudomoe })
            {
                EditorUtility.SetDirty(e);
            }

            // 開発者の要望で追加（2026-10-16）。TODO(仕様): 数値・レア度は仮
            var kusari = Effect<GainBlockEffect>("Fx_Relic_Kusarikatabira", Trigger.OnBattleStart, "鎖帷子：戦闘開始時に防御8");
            kusari.block = 8;
            kusari.condition = default;
            var goNoMamori = AddValue("Fx_Relic_Gonomamori", Trigger.OnRoll, +2, "五の守り：5が出たら出目+2");
            goNoMamori.condition = new EffectCondition { minValue = 5, maxValue = 5 };
            var ichiNoKami = AddValue("Fx_Relic_Ichinokami", Trigger.OnRoll, +5, "一の神様：1が出たら出目+5");
            ichiNoKami.condition = new EffectCondition { minValue = 1, maxValue = 1 };
            var oonigiri = Effect<MaxHpEffect>("Fx_Relic_Oonigiri", Trigger.OnAcquire, "大きな握り飯：最大HP+25");
            oonigiri.amount = 25;
            var kozuchi = Effect<ScaleEffect>("Fx_Relic_Kozuchi", Trigger.OnGoldGain, "打ち出の小槌：得るゴールド+15%（戦闘以外も）");
            kozuchi.target = ScaleTarget.Amount;
            kozuchi.percent = 115;
            kozuchi.condition = default;
            foreach (var e in new EffectSO[] { kusari, goNoMamori, ichiNoKami, oonigiri, kozuchi }) EditorUtility.SetDirty(e);
            // 2026-10-16 追加（Slay the Spire の燃える血・火打ち石風）。TODO(仕様): 数値・レア度は仮
            var chinohai = Effect<HealEffect>("Fx_Relic_Chinohai", Trigger.OnBattleEnd, "血の盃：戦闘に勝つとHP6回復");
            chinohai.heal = 6;
            chinohai.condition = default;
            var hiuchi = Effect<ApplyPoisonEffect>("Fx_Relic_Hiuchiishi", Trigger.OnBattleStart, "火打ち石：戦闘開始時、狙っている敵に毒3");
            hiuchi.flat = 3;
            hiuchi.perPip = 0;
            hiuchi.condition = default;
            EditorUtility.SetDirty(chinohai);
            EditorUtility.SetDirty(hiuchi);
            // 2026-10-16 追加（夜の作業の続き）。TODO(仕様): 数値・レア度は仮
            var shikigami = Effect<TempDiceEffect>("Fx_Relic_Shikigami", Trigger.OnBattleStart, "式神の札：戦闘開始時に式神賽が1個加わる（戦闘後に消える）");
            shikigami.dice = AssetDatabase.LoadAssetAtPath<DiceData>($"{DiceDir}/Dice_shikigami.asset");
            var jizou = Effect<GainBlockEffect>("Fx_Relic_Jizou", Trigger.OnRoundStart, "石地蔵：毎ラウンド開始時に防御2");
            jizou.block = 2;
            jizou.condition = default;
            var takarabune = Effect<GainGoldEffect>("Fx_Relic_Takarabune", Trigger.OnLayerStart, "宝船：次の層に着くたびに40G");
            takarabune.gold = 40;
            takarabune.condition = default;
            var hoozuki = Effect<HealEffect>("Fx_Relic_Hoozuki", Trigger.OnLayerStart, "鬼灯：次の層に着くたびにHP20回復");
            hoozuki.heal = 20;
            hoozuki.condition = default;
            var ninotsuzumi = AddValue("Fx_Relic_Ninotsuzumi", Trigger.OnRoll, +3, "二の鼓：2が出たら出目+3");
            ninotsuzumi.condition = new EffectCondition { minValue = 2, maxValue = 2 };
            foreach (var e in new EffectSO[] { shikigami, jizou, takarabune, hoozuki, ninotsuzumi }) EditorUtility.SetDirty(e);


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
                Relic("suzu", "鈴", Rarity.Uncommon, "リフレッシュしたとき、戦闘中なら筋力+1、移動中ならHP5回復", suzuBattle, suzuMap),
                Relic("hayauma", "早馬", Rarity.Uncommon, "各層の最初の移動は出目×2", hayauma),
                Relic("furuisaitou", "古い賽筒", Rarity.Rare, "戦闘で1ラウンドに振れるダイス+1（最大4個）", saitou),
                Relic("yakusoubukuro", "薬草袋", Rarity.Common, "休憩の回復量+50%", yakusou),
                Relic("zoromenomamori", "ゾロ目の守り", Rarity.Uncommon, "同じラウンドに同じ出目が2つ出たら、両方を攻撃にも防御にも使える", zorome),
                Relic("tsumiishi", "積み石", Rarity.Uncommon, "1か2の出目は2倍", tsumiishi),
                Relic("dokutsubo", "毒壺", Rarity.Uncommon, "与える毒+1", dokutsubo),
                Relic("tenbin", "天秤", Rarity.Uncommon, "攻撃値と防御値が同じラウンドの終わりにHP5回復", tenbin),
                Relic("chizushinoyatate", "地図師の矢立", Rarity.Common, "戦闘マス・強敵マスにマウスを乗せると、待っている敵が見える", yatate),
                Relic("fusha", "風車", Rarity.Common, "リフレッシュするたびに5G", fusha),
                Relic("chokinbako", "貯金箱", Rarity.Common, "10マス進むごとに8G", chokin),
                Relic("unmeinoito", "運命の糸", Rarity.Rare, "1戦闘に1回、振ったダイス1個の出目を好きな値（1〜6）にできる", unmei),
                Relic("rokunokago", "六の加護", Rarity.Rare, "戦闘で6が出たダイスは攻撃にも防御にも使える", roku),
                Relic("kajinokanazuchi", "鍛冶の金槌", Rarity.Rare, "鍛冶で刻印をもう1つ付けられる", kanazuchi),
                Relic("senrigan", "千里眼", Rarity.Rare, "移動の前に、次に振るダイスの出目が見える", senrigan),
                Relic("chikaramizu", "力水", Rarity.Common, "戦闘開始時に筋力+1", chikaramizu),
                Relic("nigirimeshi", "握り飯", Rarity.Common, "最大HP+14（HPも14回復）", nigirimeshi),
                Relic("juzu", "数珠", Rarity.Uncommon, "戦闘中にリフレッシュしたら防御6", juzu),
                Relic("mitsudomoe", "三つ巴", Rarity.Common, "3が出たら出目+3（移動でも戦闘でも）", mitsudomoe),
                Relic("kusarikatabira", "鎖帷子", Rarity.Uncommon, "戦闘開始時に防御8", kusari),
                Relic("gonomamori", "五の守り", Rarity.Uncommon, "5が出たら出目+2（移動でも戦闘でも）", goNoMamori),
                Relic("ichinokami", "一の神様", Rarity.Rare, "1が出たら出目+5（移動でも戦闘でも）", ichiNoKami),
                Relic("oonigirimeshi", "大きな握り飯", Rarity.Uncommon, "最大HP+25（HPも25回復）", oonigiri),
                Relic("kozuchi", "打ち出の小槌", Rarity.Common, "得るゴールド+15%（戦闘以外も）", kozuchi),
                Relic("chinohai", "血の盃", Rarity.Common, "戦闘に勝つとHP6回復", chinohai),
                Relic("hiuchiishi", "火打ち石", Rarity.Uncommon, "戦闘開始時、狙っている敵に毒3", hiuchi),
                Relic("shikigaminofuda", "式神の札", Rarity.Rare, "戦闘開始時に式神賽（2〜4）が1個加わる（戦闘後に消える）", shikigami),
                Relic("ishijizou", "石地蔵", Rarity.Uncommon, "毎ラウンド開始時に防御2", jizou),
                Relic("takarabune", "宝船", Rarity.Common, "次の層に着くたびに40G", takarabune),
                Relic("hoozuki", "鬼灯", Rarity.Common, "次の層に着くたびにHP20回復", hoozuki),
                Relic("ninotsuzumi", "二の鼓", Rarity.Common, "2が出たら出目+3（移動でも戦闘でも）", ninotsuzumi),
                // 2026-10-16：山伏の能力だった「休憩で両方」をレリックに（開発者の要望：能力にしては強すぎる）。TODO(仕様): レア度は仮
                Relic("gyoujanooi", "行者の笈", Rarity.Rare, "休憩マスで「休む」と「鍛える」の両方ができる", RestBothRule()),
                // 2026-10-16 開発者の要望「レリックを増やして」で追加。TODO(仕様): 数値・レア度は仮
                Relic("tetsugeta", "鉄下駄", Rarity.Common, "戦闘開始時に防御4",
                    Fx<GainBlockEffect>("Fx_Relic_Tetsugeta", Trigger.OnBattleStart, "鉄下駄：戦闘開始時に防御4", e => { e.block = 4; e.fortify = 0; e.condition = default; })),
                Relic("karakasa", "唐傘", Rarity.Common, "毎ラウンド開始時に防御1",
                    Fx<GainBlockEffect>("Fx_Relic_Karakasa", Trigger.OnRoundStart, "唐傘：毎ラウンド開始時に防御1", e => { e.block = 1; e.fortify = 0; e.condition = default; })),
                Relic("kumade", "熊手", Rarity.Common, "戦闘で得るゴールド+40%",
                    Fx<ScaleEffect>("Fx_Relic_Kumade", Trigger.OnGoldGain, "熊手：戦闘で得るゴールド+40%", e => { e.target = ScaleTarget.Amount; e.percent = 140; e.condition = new EffectCondition { battleGoldOnly = true }; })),
                Relic("komainu", "狛犬", Rarity.Common, "1か2の目を防御に置くと+2",
                    Cond("Fx_Relic_Komainu", Trigger.OnAssignDefense, +2, "狛犬：1か2の目を防御に置くと+2", new EffectCondition { maxValue = 2 })),
                Relic("yotsuba", "四つ葉", Rarity.Common, "4が出たら出目+2（移動でも戦闘でも）",
                    Cond("Fx_Relic_Yotsuba", Trigger.OnRoll, +2, "四つ葉：4が出たら出目+2", new EffectCondition { minValue = 4, maxValue = 4 })),
                Relic("rokumonsen", "六文銭", Rarity.Common, "6が出たら 6G（移動でも戦闘でも）",
                    Fx<GainGoldEffect>("Fx_Relic_Rokumonsen", Trigger.OnRoll, "六文銭：6が出たら 6G", e => { e.gold = 6; e.condition = new EffectCondition { minValue = 6 }; })),
                Relic("sakebyoutan", "酒瓢箪", Rarity.Common, "戦闘に勝つとHP3回復",
                    Fx<HealEffect>("Fx_Relic_Sakebyoutan", Trigger.OnBattleEnd, "酒瓢箪：戦闘に勝つとHP3回復", e => { e.heal = 3; e.condition = default; })),
                Relic("minogasa", "蓑笠", Rarity.Common, "偶数の目を防御に置くと+1",
                    Cond("Fx_Relic_Minogasa", Trigger.OnAssignDefense, +1, "蓑笠：偶数の目を防御に置くと+1", new EffectCondition { parity = ParityCondition.Even })),
                Relic("senbazuru", "千羽鶴", Rarity.Common, "休憩の回復量+30%",
                    Fx<StatBonusEffect>("Fx_Relic_Senbazuru", Trigger.OnAcquire, "千羽鶴：休憩の回復量+30%", e => { e.stat = RunStat.RestHealPercent; e.amount = 30; })),
                Relic("taketonbo", "竹とんぼ", Rarity.Uncommon, "移動の出目+1",
                    Cond("Fx_Relic_Taketonbo", Trigger.OnRoll, +1, "竹とんぼ：移動の出目+1", new EffectCondition { scene = SceneCondition.Map })),
                Relic("daruma", "達磨", Rarity.Uncommon, "最大HP+8（HPも8回復）・戦闘に勝つとHP2回復",
                    Fx<MaxHpEffect>("Fx_Relic_Daruma_Hp", Trigger.OnAcquire, "達磨：最大HP+8", e => e.amount = 8),
                    Fx<HealEffect>("Fx_Relic_Daruma_Heal", Trigger.OnBattleEnd, "達磨：戦闘に勝つとHP2回復", e => { e.heal = 2; e.condition = default; })),
                Relic("hannyanomen", "般若の面", Rarity.Uncommon, "6の目で攻撃すると+3",
                    Cond("Fx_Relic_Hannya", Trigger.OnAssignAttack, +3, "般若の面：6の目で攻撃すると+3", new EffectCondition { minValue = 6 })),
                Relic("horagai", "法螺貝", Rarity.Uncommon, "戦闘開始時、狙っている敵に脱力2",
                    Rider("Fx_Relic_Horagai", AttackRider.Weak, 2, 0, "法螺貝：戦闘開始時、狙っている敵に脱力2").WithTrigger(Trigger.OnBattleStart)),
                Relic("warainingyou", "藁人形", Rarity.Uncommon, "戦闘開始時、狙っている敵に弱体2",
                    Rider("Fx_Relic_Warainingyou", AttackRider.Vulnerable, 2, 0, "藁人形：戦闘開始時、狙っている敵に弱体2").WithTrigger(Trigger.OnBattleStart)),
                Relic("naruko", "鳴子", Rarity.Uncommon, "戦闘開始時、狙っている敵に脆弱2",
                    Rider("Fx_Relic_Naruko", AttackRider.Frail, 2, 0, "鳴子：戦闘開始時、狙っている敵に脆弱2").WithTrigger(Trigger.OnBattleStart)),
                Relic("fuurin", "風鈴", Rarity.Common, "リフレッシュしたときHP2回復",
                    Fx<HealEffect>("Fx_Relic_Fuurin", Trigger.OnRefresh, "風鈴：リフレッシュしたときHP2回復", e => { e.heal = 2; e.condition = default; })),
                Relic("kamifuusen", "紙風船", Rarity.Common, "最大HP+5（HPも5回復）",
                    Fx<MaxHpEffect>("Fx_Relic_Kamifuusen", Trigger.OnAcquire, "紙風船：最大HP+5", e => e.amount = 5)),
                Relic("netsuke", "根付", Rarity.Common, "戦闘に勝つと 6G",
                    Fx<GainGoldEffect>("Fx_Relic_Netsuke", Trigger.OnBattleEnd, "根付：戦闘に勝つと 6G", e => { e.gold = 6; e.condition = default; })),
                // 2026-10-16 さらに追加（100 個に近づける）。TODO(仕様): 数値・レア度は仮
                Relic("tengunouchiwa", "天狗の羽根", Rarity.Common, "移動で1が出たら出目+3",
                    Cond("Fx_Relic_Tengunouchiwa", Trigger.OnRoll, +3, "天狗の羽根：移動で1が出たら出目+3", new EffectCondition { maxValue = 1, scene = SceneCondition.Map })),
                Relic("oninotsuno", "鬼の角", Rarity.Uncommon, "5以上の目で攻撃すると+2",
                    Cond("Fx_Relic_Oninotsuno", Trigger.OnAssignAttack, +2, "鬼の角：5以上の目で攻撃すると+2", new EffectCondition { minValue = 5 })),
                Relic("kamenokoura", "亀の甲羅", Rarity.Uncommon, "3以下の目を防御に置くと+2",
                    Cond("Fx_Relic_Kamenokoura", Trigger.OnAssignDefense, +2, "亀の甲羅：3以下の目を防御に置くと+2", new EffectCondition { maxValue = 3 })),
                Relic("dokujanokiba", "毒蛇の牙", Rarity.Uncommon, "奇数の目で攻撃すると、狙った敵に毒1",
                    Fx<ApplyPoisonEffect>("Fx_Relic_Dokujanokiba", Trigger.OnAttackResolve, "毒蛇の牙：奇数の目で攻撃すると毒1", e => { e.flat = 1; e.perPip = 0; e.condition = new EffectCondition { parity = ParityCondition.Odd }; })),
                Relic("kinnosuzu", "金の鈴", Rarity.Common, "リフレッシュしたとき 8G",
                    Fx<GainGoldEffect>("Fx_Relic_Kinnosuzu", Trigger.OnRefresh, "金の鈴：リフレッシュしたとき 8G", e => { e.gold = 8; e.condition = default; })),
                Relic("yagen", "薬研", Rarity.Common, "戦闘に勝つとHP2回復・休憩の回復量+20%",
                    Fx<HealEffect>("Fx_Relic_Yagen_Heal", Trigger.OnBattleEnd, "薬研：戦闘に勝つとHP2回復", e => { e.heal = 2; e.condition = default; }),
                    Fx<StatBonusEffect>("Fx_Relic_Yagen_Rest", Trigger.OnAcquire, "薬研：休憩の回復量+20%", e => { e.stat = RunStat.RestHealPercent; e.amount = 20; })),
                Relic("kitsunebinochouchin", "狐火の提灯", Rarity.Uncommon, "戦闘開始時、狙っている敵に毒4",
                    Fx<ApplyPoisonEffect>("Fx_Relic_Kitsunebi", Trigger.OnBattleStart, "狐火の提灯：戦闘開始時に毒4", e => { e.flat = 4; e.perPip = 0; e.condition = default; })),
                Relic("dango", "団子", Rarity.Common, "最大HP+4・戦闘に勝つとHP1回復",
                    Fx<MaxHpEffect>("Fx_Relic_Dango_Hp", Trigger.OnAcquire, "団子：最大HP+4", e => e.amount = 4),
                    Fx<HealEffect>("Fx_Relic_Dango_Heal", Trigger.OnBattleEnd, "団子：戦闘に勝つとHP1回復", e => { e.heal = 1; e.condition = default; })),
                Relic("shouginokoma", "将棋の駒", Rarity.Uncommon, "偶数の目で攻撃すると+1・奇数の目で防御すると+1",
                    Cond("Fx_Relic_Shougi_Atk", Trigger.OnAssignAttack, +1, "将棋の駒：偶数の目の攻撃+1", new EffectCondition { parity = ParityCondition.Even }),
                    Cond("Fx_Relic_Shougi_Def", Trigger.OnAssignDefense, +1, "将棋の駒：奇数の目の防御+1", new EffectCondition { parity = ParityCondition.Odd })),
                Relic("onigawara", "鬼瓦", Rarity.Uncommon, "戦闘開始時に防御6",
                    Fx<GainBlockEffect>("Fx_Relic_Onigawara", Trigger.OnBattleStart, "鬼瓦：戦闘開始時に防御6", e => { e.block = 6; e.fortify = 0; e.condition = default; })),
                Relic("mamorikagami", "守り鏡", Rarity.Rare, "戦闘開始時に防御5と堅守2",
                    Fx<GainBlockEffect>("Fx_Relic_Mamorikagami", Trigger.OnBattleStart, "守り鏡：戦闘開始時に防御5と堅守2", e => { e.block = 5; e.fortify = 2; e.condition = default; })),
                Relic("shamisen", "三味線", Rarity.Rare, "戦闘で3が出たダイスは攻撃にも防御にも使える",
                    Fx<FlagEffect>("Fx_Relic_Shamisen", Trigger.OnRoll, "三味線：3の目は攻撃と防御の両方に", e => { e.flag = RollFlag.BothSides; e.condition = new EffectCondition { minValue = 3, maxValue = 3, scene = SceneCondition.Battle }; })),
                Relic("hakujanonukegara", "白蛇の抜け殻", Rarity.Rare, "最大HP+12・戦闘に勝つとHP3回復",
                    Fx<MaxHpEffect>("Fx_Relic_Hakuja_Hp", Trigger.OnAcquire, "白蛇の抜け殻：最大HP+12", e => e.amount = 12),
                    Fx<HealEffect>("Fx_Relic_Hakuja_Heal", Trigger.OnBattleEnd, "白蛇の抜け殻：戦闘に勝つとHP3回復", e => { e.heal = 3; e.condition = default; })),
                Relic("takenosuitou", "竹の水筒", Rarity.Common, "休憩の回復量+20%",
                    Fx<StatBonusEffect>("Fx_Relic_Takenosuitou", Trigger.OnAcquire, "竹の水筒：休憩の回復量+20%", e => { e.stat = RunStat.RestHealPercent; e.amount = 20; })),
                Relic("sugorokunokoma", "双六の駒", Rarity.Uncommon, "移動で6が出たダイスは使用済みにならない",
                    Fx<KeepAvailableEffect>("Fx_Relic_Sugorokunokoma", Trigger.OnRoll, "双六の駒：移動で6が出たら使用済みにならない", e => e.condition = new EffectCondition { minValue = 6, scene = SceneCondition.Map })),
            };

            // 早馬は第3層では出さない（開発者の要望：層の最初の移動を倍にしても、もう得が少ない）
            var hayaumaRelic = list.Find(r => r.id == "hayauma");
            hayaumaRelic.maxLayer = 2;
            EditorUtility.SetDirty(hayaumaRelic);


            // ---- みんなで遊ぶとき専用のレリック（効き目はレリックの id で、協力の戦闘・国めぐりが見る） ----
            // TODO(仕様): 数値・値段は仮
            var multi = new List<RelicData>
            {
                Relic("ryodannohata", "旅団の旗", Rarity.Uncommon, "協力の戦闘の始めに、仲間1人につき筋力+1"),
                Relic("yuihimo", "結い紐", Rarity.Uncommon, "協力の戦闘の始めに、仲間1人につき防御+3"),
            };
            var tourRelics = new List<RelicData>
            {
                Relic("yakuyokenosuzu", "厄除けの鈴", Rarity.Uncommon, "国めぐり：厄神のいたずらを半分の確率で防ぐ"),
                Relic("shouninnosoroban", "商人の算盤", Rarity.Uncommon, "国めぐり：決算の名物の収入+25%"),
                Relic("hayakago", "早駕籠", Rarity.Uncommon, "国めぐり：移動の出目+1"),
            };
            var bossRelics = BuildBossRelics();

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.relicPool = list;
                config.multiplayerRelicPool = multi;
                config.tourRelicPool = tourRelics;
                config.bossRelicPool = bossRelics;
                config.ancientRelicPool = BuildAncientRelics();
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] レリック{list.Count}種・ボスレリック{bossRelics.Count}種のデータを作成・更新しました。");
        }

        /// <summary>ボスレリック（仕様書 第10章）。良い効果と悪い効果がある。</summary>
        /// <summary>
        /// 古の神のレリック（2026-10-16 開発者の要望：STS2 のエンシェントのように、第2層・第3層の始めに3つから1つ選ぶ）。
        /// ふつうのレリックより強い。代償のあるものもある。報酬・ショップには出ない。TODO(仕様): 数値は仮
        /// </summary>
        static List<RelicData> BuildAncientRelics()
        {
            var noCond = default(EffectCondition);
            return new List<RelicData>
            {
                Relic("kusanagi", "草薙の剣", Rarity.Rare, "古の神：攻撃に置いたダイスすべて+1",
                    AddValue("Fx_Ancient_Kusanagi", Trigger.OnAssignAttack, +1, "草薙の剣：攻撃に置いたダイス+1")),
                Relic("yatanokagami", "八咫鏡", Rarity.Rare, "古の神：毎ラウンド開始時に防御3",
                    Fx<GainBlockEffect>("Fx_Ancient_Yata", Trigger.OnRoundStart, "八咫鏡：毎ラウンド開始時に防御3", e => { e.block = 3; e.fortify = 0; e.condition = noCond; })),
                Relic("yasakani", "八尺瓊勾玉", Rarity.Rare, "古の神：最大HP+20（HPも20回復）",
                    Fx<MaxHpEffect>("Fx_Ancient_Yasakani", Trigger.OnAcquire, "八尺瓊勾玉：最大HP+20", e => e.amount = 20)),
                Relic("kongousho", "金剛杵", Rarity.Rare, "古の神：防御に置いたダイスすべて+1",
                    AddValue("Fx_Ancient_Kongousho", Trigger.OnAssignDefense, +1, "金剛杵：防御に置いたダイス+1")),
                Relic("ryuujinnohoju", "竜神の宝珠", Rarity.Rare, "古の神：戦闘で1ラウンドに振れるダイス+1（最大4個）。ただし最大HP−8",
                    Fx<DicePerRoundEffect>("Fx_Ancient_Hoju", Trigger.OnBattleStart, "竜神の宝珠：振れるダイス+1", e => e.add = 1),
                    Fx<MaxHpEffect>("Fx_Ancient_Hoju_Hp", Trigger.OnAcquire, "竜神の宝珠：最大HP−8", e => e.amount = -8)),
                Relic("fuujinnofukuro", "風神の袋", Rarity.Rare, "古の神：移動ではいつも、止まるマスを出目±1から選べる",
                    Fx<MoveAdjustEffect>("Fx_Ancient_Fuujin", Trigger.OnMoveRolled, "風神の袋：移動で出目±1から選べる", e => { e.range = 1; e.label = "風神"; })),
                Relic("raijinnobachi", "雷神の撥", Rarity.Rare, "古の神：戦闘開始時、狙っている敵に毒6",
                    Fx<ApplyPoisonEffect>("Fx_Ancient_Raijin", Trigger.OnBattleStart, "雷神の撥：戦闘開始時に毒6", e => { e.flat = 6; e.perPip = 0; e.condition = noCond; })),
                Relic("tamatebako", "玉手箱", Rarity.Rare, "古の神：手に入れたとき 250G。ただし最大HP−10",
                    Fx<GainGoldEffect>("Fx_Ancient_Tamatebako_Gold", Trigger.OnAcquire, "玉手箱：250G", e => { e.gold = 250; e.condition = noCond; }),
                    Fx<MaxHpEffect>("Fx_Ancient_Tamatebako_Hp", Trigger.OnAcquire, "玉手箱：最大HP−10", e => e.amount = -10)),
                Relic("oninokubi", "鬼の首", Rarity.Rare, "古の神：攻撃に置いたダイスすべて+2。ただし最大HP−15",
                    AddValue("Fx_Ancient_Oninokubi", Trigger.OnAssignAttack, +2, "鬼の首：攻撃に置いたダイス+2"),
                    Fx<MaxHpEffect>("Fx_Ancient_Oninokubi_Hp", Trigger.OnAcquire, "鬼の首：最大HP−15", e => e.amount = -15)),
                Relic("fushichounohane", "不死鳥の羽", Rarity.Rare, "古の神：休憩の回復量+100%・戦闘に勝つとHP4回復",
                    Fx<StatBonusEffect>("Fx_Ancient_Fushichou_Rest", Trigger.OnAcquire, "不死鳥の羽：休憩の回復量+100%", e => { e.stat = RunStat.RestHealPercent; e.amount = 100; }),
                    Fx<HealEffect>("Fx_Ancient_Fushichou_Heal", Trigger.OnBattleEnd, "不死鳥の羽：戦闘に勝つとHP4回復", e => { e.heal = 4; e.condition = noCond; })),
                Relic("zashikinomari", "座敷童子の鞠", Rarity.Rare, "古の神：得るゴールド+40%",
                    Fx<ScaleEffect>("Fx_Ancient_Mari", Trigger.OnGoldGain, "座敷童子の鞠：得るゴールド+40%", e => { e.target = ScaleTarget.Amount; e.percent = 140; e.condition = noCond; })),
                Relic("tsukuyominokushi", "月読の櫛", Rarity.Rare, "古の神：1か2の出目は+3（移動でも戦闘でも）",
                    Cond("Fx_Ancient_Kushi", Trigger.OnRoll, +3, "月読の櫛：1か2の出目は+3", new EffectCondition { maxValue = 2 })),
                Relic("inarinokitsunemen", "稲荷の狐面", Rarity.Rare, "古の神：戦闘開始時に筋力+2。ただし最大HP−8",
                    Fx<GainStrengthEffect>("Fx_Ancient_Kitsune", Trigger.OnBattleStart, "稲荷の狐面：戦闘開始時に筋力+2", e => { e.strength = 2; e.condition = noCond; }),
                    Fx<MaxHpEffect>("Fx_Ancient_Kitsune_Hp", Trigger.OnAcquire, "稲荷の狐面：最大HP−8", e => e.amount = -8)),
            };
        }

        static List<RelicData> BuildBossRelics()
        {
            var kake = AssetDatabase.LoadAssetAtPath<DiceData>($"{DiceDir}/Dice_kake.asset");

            var cupDice = Effect<DicePerRoundEffect>("Fx_Boss_GoldenCup_Dice", Trigger.OnBattleStart, "黄金の賽筒：戦闘で振れるダイス+1");
            cupDice.add = 1;
            var cupPouch = Effect<PouchCapacityEffect>("Fx_Boss_GoldenCup_Pouch", Trigger.OnAcquire, "黄金の賽筒：ポーチの容量−1");
            cupPouch.amount = -1;

            var crownValue = AddValue("Fx_Boss_Crown_Value", Trigger.OnRoll, +1, "重い王冠：全ダイスの全面+1（振ったときの出目+1）");
            crownValue.condition = default;
            var crownRule = Effect<RuleEffect>("Fx_Boss_Crown_NoRest", Trigger.OnAcquire, "重い王冠：休憩マスで回復できない");
            crownRule.rule = RunRule.NoRestHeal;

            var boardGold = Effect<ScaleEffect>("Fx_Boss_Board_Gold", Trigger.OnGoldGain, "呪われた双六盤：得るゴールド×2");
            boardGold.target = ScaleTarget.Amount;
            boardGold.percent = 200;
            boardGold.condition = default;
            var boardCurse = Effect<TempDiceEffect>("Fx_Boss_Board_Curse", Trigger.OnBattleStart, "呪われた双六盤：戦闘開始時に欠け賽が1個加わる（戦闘後に消える）");
            boardCurse.dice = kake;

            var sandReturn = Effect<ReturnUsedDieEffect>("Fx_Boss_Sand_Return", Trigger.OnRoundStart, "時の砂：毎ラウンド開始時、使用済みのダイス1個を戻す");
            sandReturn.count = 1;
            var sandHp = Effect<MaxHpEffect>("Fx_Boss_Sand_MaxHp", Trigger.OnAcquire, "時の砂：最大HP−18");
            sandHp.amount = -18;

            var bracerEngrave = Effect<RuleEffect>("Fx_Boss_Bracer_Engrave", Trigger.OnAcquire, "縛りの腕輪：購入したダイスにランダムな刻印が1つ付く");
            bracerEngrave.rule = RunRule.EngraveBoughtDice;
            var bracerShop = Effect<RuleEffect>("Fx_Boss_Bracer_Shop", Trigger.OnAcquire, "縛りの腕輪：ショップの品数が半分");
            bracerShop.rule = RunRule.HalfShopStock;

            // デメリットのないボスレリック（開発者の要望：通常のレリックより強いが、癖の少ないもの）
            // TODO(仕様): 数値は仮
            var kanabou = Effect<GainStrengthEffect>("Fx_Boss_Kanabou", Trigger.OnBattleStart, "鬼の金棒：戦闘開始時に筋力+2");
            kanabou.strength = 2;
            kanabou.condition = default;
            var ootate = Effect<GainBlockEffect>("Fx_Boss_Ootate", Trigger.OnRoundStart, "鉄の大盾：毎ラウンド開始時に防御4");
            ootate.block = 4;
            ootate.condition = default;
            var momo = Effect<MaxHpEffect>("Fx_Boss_Momo", Trigger.OnAcquire, "長寿の桃：最大HP+35");
            momo.amount = 35;
            var neko = Effect<ScaleEffect>("Fx_Boss_Neko", Trigger.OnGoldGain, "招き猫：得るゴールド+50%");
            neko.target = ScaleTarget.Amount;
            neko.percent = 150;
            neko.condition = default;

            foreach (var e in new EffectSO[] { cupDice, cupPouch, crownValue, crownRule, boardGold, boardCurse, sandReturn, sandHp, bracerEngrave, bracerShop, kanabou, ootate, momo, neko })
            {
                EditorUtility.SetDirty(e);
            }

            var list = new List<RelicData>
            {
                Relic("ougonnosaitou", "黄金の賽筒", Rarity.Rare, "良い：戦闘で振れるダイス+1（最大4個）\n悪い：ポーチの容量−1", cupDice, cupPouch),
                Relic("omoioukan", "重い王冠", Rarity.Rare, "良い：全ダイスの全面+1（振った出目+1）\n悪い：休憩マスで回復できない", crownValue, crownRule),
                Relic("norowaresugoroku", "呪われた双六盤", Rarity.Rare, "良い：得るゴールド×2\n悪い：戦闘開始時に欠け賽が1個加わる（戦闘後に消える）", boardGold, boardCurse),
                Relic("tokinosuna", "時の砂", Rarity.Rare, "良い：毎ラウンド開始時、使用済みのダイス1個を戻す\n悪い：最大HP−18", sandReturn, sandHp),
                Relic("shibarinoudewa", "縛りの腕輪", Rarity.Rare, "良い：購入したダイスにランダムな刻印が1つ付く\n悪い：ショップの品数が半分", bracerEngrave, bracerShop),
                Relic("oninokanabou", "鬼の金棒", Rarity.Rare, "戦闘開始時に筋力+2", kanabou),
                Relic("tetsunoootate", "鉄の大盾", Rarity.Rare, "毎ラウンド開始時に防御4", ootate),
                Relic("choujunomomo", "長寿の桃", Rarity.Rare, "最大HP+35（HPも35回復）", momo),
                Relic("manekineko", "招き猫", Rarity.Rare, "得るゴールド+50%", neko),
            };
            foreach (var r in list)
            {
                r.isBoss = true;
                EditorUtility.SetDirty(r);
            }
            return list;
        }

        // ---- ダイスの効果を短く作る補助（2026-10-16 ダイスを増やしたとき） ----

        static AddValueEffect Cond(string fileName, Trigger trigger, int add, string note, EffectCondition condition)
        {
            var e = AddValue(fileName, trigger, add, note);
            e.condition = condition;
            EditorUtility.SetDirty(e);
            return e;
        }

        static AttackRiderEffect Rider(string fileName, AttackRider rider, int amount, int perPip, string note) => RiderFx(fileName, rider, amount, perPip, note);

        static AttackRiderEffect RiderFx(string fileName, AttackRider rider, int amount, int perPip, string note)
        {
            var e = Effect<AttackRiderEffect>(fileName, Trigger.OnAttackResolve, note);
            e.rider = rider;
            e.amount = amount;
            e.perPip = perPip;
            EditorUtility.SetDirty(e);
            return e;
        }

        static ApplyPoisonEffect Poison(string fileName, int flat, int perPip, string note)
        {
            var e = Effect<ApplyPoisonEffect>(fileName, Trigger.OnAttackResolve, note);
            e.flat = flat;
            e.perPip = perPip;
            e.condition = default;
            EditorUtility.SetDirty(e);
            return e;
        }

        static T Fx<T>(string fileName, Trigger trigger, string note, System.Action<T> set) where T : EffectSO
        {
            var e = Effect<T>(fileName, trigger, note);
            set(e);
            EditorUtility.SetDirty(e);
            return e;
        }

        static RuleEffect RestBothRule()
        {
            var rule = Effect<RuleEffect>("Fx_Rule_RestBoth", Trigger.OnAcquire, "行者の笈：休憩マスで休むと鍛えるの両方ができる");
            rule.rule = RunRule.RestBoth;
            EditorUtility.SetDirty(rule);
            return rule;
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
            data.isBoss = false;
            data.maxLayer = 0;
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{RelicArtDir}/relic_{id}.png");
            if (icon != null) data.icon = icon;
            else Debug.LogWarning($"[賽ノ道] レリックの絵 {RelicArtDir}/relic_{id}.png がありません。");
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>フェーズ2で足す刻印12種（仕様書 第5章）。値段はレア度で コモン60・アンコモン90・レア140。</summary>
        static List<EngravingData> BuildPhase2Engravings()
        {
            var heal = Effect<HealEffect>("Fx_Engr_Kusuri", Trigger.OnRoll, "刻印「薬」：この面が出たらHP2回復");
            heal.heal = 2;
            heal.condition = default;
            var needle = Effect<ApplyPoisonEffect>("Fx_Engr_Dokubari", Trigger.OnAttackResolve, "刻印「毒針」：攻撃に置くと毒3");
            needle.flat = 3;
            needle.perPip = 0;
            needle.condition = new EffectCondition { assignment = Battle.Assignment.Attack };
            var vampire = Effect<AttackRiderEffect>("Fx_Engr_Kyuuketsu", Trigger.OnAttackResolve, "刻印「吸血」：与えたダメージの半分を回復");
            vampire.rider = AttackRider.Lifesteal;
            vampire.percent = 50;
            var breaker = Effect<AttackRiderEffect>("Fx_Engr_Kuzushi", Trigger.OnAttackResolve, "刻印「崩し」：敵に弱体1");
            breaker.rider = AttackRider.Vulnerable;
            breaker.amount = 1;
            var shackle = Effect<AttackRiderEffect>("Fx_Engr_Ashikase", Trigger.OnAttackResolve, "刻印「足枷」：敵の予告した攻撃値−3");
            shackle.rider = AttackRider.ReduceIntent;
            shackle.amount = 3;
            var reroll = Effect<FlagEffect>("Fx_Engr_Saiten", Trigger.OnRoll, "刻印「再転」：この面が出たら振り直してもよい（1回）");
            reroll.flag = RollFlag.CanReroll;
            reroll.condition = default;
            var chain = Effect<ReturnUsedDieEffect>("Fx_Engr_Rensa", Trigger.OnRoll, "刻印「連鎖」：使用済みのダイス1個を使用可能に戻す");
            chain.count = 1;
            var shadow = Effect<KeepAvailableEffect>("Fx_Engr_Kagenui", Trigger.OnRoll, "刻印「影縫い」：このダイスを使用済みにしない");
            shadow.condition = default;
            var bothEdge = Effect<FlagEffect>("Fx_Engr_Moroha", Trigger.OnRoll, "刻印「両刃」：戦闘で攻撃と防御の両方に使える");
            bothEdge.flag = RollFlag.BothSides;
            bothEdge.condition = new EffectCondition { scene = SceneCondition.Battle };
            var homeward = Effect<FlagEffect>("Fx_Engr_Kaerimichi", Trigger.OnMoveRolled, "刻印「帰り道」：次の休憩マスかショップまで一気に進む");
            homeward.flag = RollFlag.WarpToRestOrShop;
            homeward.condition = default;
            foreach (var e in new EffectSO[] { heal, needle, vampire, breaker, shackle, reroll, chain, shadow, bothEdge, homeward }) EditorUtility.SetDirty(e);

            return new List<EngravingData>
            {
                // TODO(仕様): 写しのコピー元はプレイヤーに選ばせず、同じダイスの一番大きい面にする
                Engraving("utsushi", "写し", "写", Rarity.Uncommon, 90, "同じダイスの一番大きい面の数値をコピーする", EngravingKind.Numeric, NumericOp.CopyFace, 0),
                Engraving("kongou", "金剛", "剛", Rarity.Rare, 140, "面の数値を9にする", EngravingKind.Numeric, NumericOp.Set, 9),
                Engraving("kusuri", "薬", "薬", Rarity.Common, 60, "この面が出たらHPを2回復（移動でも戦闘でも）", EngravingKind.Effect, NumericOp.Add, 0, heal),
                Engraving("dokubari", "毒針", "針", Rarity.Uncommon, 90, "攻撃に置くと、狙った敵に毒3", EngravingKind.Effect, NumericOp.Add, 0, needle),
                Engraving("kyuuketsu", "吸血", "血", Rarity.Uncommon, 90, "攻撃に置くと、そのラウンドに与えたダメージの半分を回復", EngravingKind.Effect, NumericOp.Add, 0, vampire),
                Engraving("kuzushi", "崩し", "崩", Rarity.Uncommon, 90, "攻撃に置くと、狙った敵に弱体1（受けるダメージ150%）", EngravingKind.Effect, NumericOp.Add, 0, breaker),
                Engraving("ashikase", "足枷", "枷", Rarity.Uncommon, 90, "攻撃に置くと、狙った敵の予告した攻撃値−3", EngravingKind.Effect, NumericOp.Add, 0, shackle),
                Engraving("saiten", "再転", "転", Rarity.Uncommon, 90, "この面が出たら、振り直してもよい（1回）", EngravingKind.Effect, NumericOp.Add, 0, reroll),
                Engraving("rensa", "連鎖", "鎖", Rarity.Rare, 140, "この面が出たら、使用済みのダイス1個を使用可能に戻す", EngravingKind.Effect, NumericOp.Add, 0, chain),
                Engraving("kagenui", "影縫い", "影", Rarity.Rare, 140, "この面が出たら、このダイスを使用済みにしない", EngravingKind.Effect, NumericOp.Add, 0, shadow),
                Engraving("moroha", "両刃", "両", Rarity.Rare, 140, "戦闘でこの面が出たら、攻撃と防御の両方に使える", EngravingKind.Effect, NumericOp.Add, 0, bothEdge),
                Engraving("kaerimichi", "帰り道", "帰", Rarity.Rare, 140, "移動でこの面が出たら、次の休憩マスかショップまで一気に進む（ボスマスは越えない）", EngravingKind.Effect, NumericOp.Add, 0, homeward),
            };
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

        // ---- お守り（仕様書 第10章） ----

        const string CharmDir = "Assets/_Project/Data/Charms";
        const string CharmArtDir = "Assets/_Project/Art/Charm";

        [MenuItem("SaiNoMichi/Data/Build Charm")]
        public static void BuildCharms()
        {
            EnsureFolder("Assets/_Project/Data", "Charms");
            var list = new List<Run.CharmData>
            {
                Charm("furinaoshi", "振り直し御札", Run.CharmKind.RerollDie, 0, 25, "振ったダイス1個を振り直す（移動・戦闘）"),
                Charm("susumi", "進み御札", Run.CharmKind.MoveForward, 2, 25, "移動の出目+2"),
                Charm("tomari", "止まり御札", Run.CharmKind.MoveBack, 2, 25, "移動の出目−2（最低1）"),
                Charm("kizugusuri", "傷薬", Run.CharmKind.Heal, 20, 35, "HPを20回復"),
                Charm("kemuridama", "煙玉", Run.CharmKind.Smoke, 0, 40, "通常戦から逃げる（報酬なし）"),
                Charm("oshiirenokagi", "押し入れの鍵", Run.CharmKind.Unseal, 0, 40, "封印されたダイスをすべて解除"),
                Charm("nokorifuku", "残り福", Run.CharmKind.ReturnUsed, 0, 50, "使用済みのダイスをすべて戻す"),
                // 開発者の要望：敵に投げる薬（STS のポーション）。TODO(仕様): 量と値段は仮
                Charm("datsuryokunokusuri", "脱力の薬", Run.CharmKind.WeakenEnemy, 2, 35, "狙っている敵に脱力2（与えるダメージ75%）"),
                Charm("jakutainokusuri", "弱体の薬", Run.CharmKind.VulnerableEnemy, 2, 35, "狙っている敵に弱体2（受けるダメージ150%）"),
                // TODO(仕様): 毒6（合計21ダメージ）は強すぎたので毒4（合計10）にした。値は仮
                Charm("dokunokusuri", "毒の薬", Run.CharmKind.PoisonEnemy, 4, 40, "狙っている敵に毒4"),
                // 開発者の要望で追加。TODO(仕様): 数値・値段は仮
                Charm("chikaranofuda", "力の札", Run.CharmKind.GainStrength, 2, 35, "戦闘中に使う：この戦闘の間、筋力+2"),
                Charm("mamorinofuda", "守りの札", Run.CharmKind.GainBlock, 10, 30, "戦闘中に使う：防御+10（このラウンド）"),
                // 開発者の要望で追加（2026-10-16）。TODO(仕様): 数値・値段は仮
                Charm("raifu", "雷符", Run.CharmKind.ThunderAll, 8, 45, "戦闘中に使う：全部の敵に8ダメージ（とどめは刺せない）"),
                Charm("senryoubako", "千両箱", Run.CharmKind.GainGold, 60, 90, "ゴールドを60得る（いつでも使える）"),
                Charm("ookizugusuri", "大傷薬", Run.CharmKind.Heal, 40, 60, "HPを40回復"),
                Charm("tetsunomamori", "鉄の守り", Run.CharmKind.GainBlock, 18, 45, "戦闘中に使う：防御+18（このラウンド）"),
                // 2026-10-16 追加（雷符・力の札の強い版）。TODO(仕様): 数値・値段は仮
                Charm("raijinnotaiko", "雷神の太鼓", Run.CharmKind.ThunderAll, 14, 70, "戦闘中に使う：全部の敵に14ダメージ（とどめは刺せない）"),
                Charm("tengunohiyaku", "天狗の秘薬", Run.CharmKind.GainStrength, 4, 65, "戦闘中に使う：この戦闘の間、筋力+4"),
                // 2026-10-16 開発者の要望「お守りを増やして」で追加。TODO(仕様): 数値・値段は仮
                Charm("kogizugusuri", "小傷薬", Run.CharmKind.Heal, 10, 20, "HPを10回復"),
                Charm("toodenofuda", "遠出の札", Run.CharmKind.MoveForward, 4, 40, "移動の出目+4"),
                Charm("ashibuminofuda", "足踏みの札", Run.CharmKind.MoveBack, 4, 30, "移動の出目−4（最低1）"),
                Charm("moudokunokusuri", "猛毒の薬", Run.CharmKind.PoisonEnemy, 8, 60, "狙っている敵に毒8"),
                Charm("kobanzutsumi", "小判包み", Run.CharmKind.GainGold, 30, 45, "ゴールドを30得る（いつでも使える）"),
                Charm("kongounomamori", "金剛の守り", Run.CharmKind.GainBlock, 25, 60, "戦闘中に使う：防御+25（このラウンド）"),
                Charm("inazumafu", "稲妻符", Run.CharmKind.ThunderAll, 5, 25, "戦闘中に使う：全部の敵に5ダメージ（とどめは刺せない）"),
                Charm("oninohiyaku", "鬼の秘薬", Run.CharmKind.GainStrength, 3, 50, "戦闘中に使う：この戦闘の間、筋力+3"),
                Charm("zeijakunokusuri", "脆弱の薬", Run.CharmKind.FrailEnemy, 2, 35, "狙っている敵に脆弱2（作れる防御75%）"),
                Charm("choujunogan", "長寿の丸薬", Run.CharmKind.MaxHp, 6, 70, "最大HPを6増やす（HPも6回復。いつでも使える）"),
                Charm("kenshunofuda", "堅守の札", Run.CharmKind.Fortify, 2, 40, "戦闘中に使う：堅守2（ラウンドの終わりに防御が半分残る）"),
                Charm("kiyomenoshio", "清めの塩", Run.CharmKind.Cleanse, 0, 35, "戦闘中に使う：自分の脱力・弱体・脆弱・毒・縛りを消す"),
                Charm("chuugizugusuri", "中傷薬", Run.CharmKind.Heal, 30, 45, "HPを30回復"),
                Charm("sanponofuda", "三歩の札", Run.CharmKind.MoveForward, 3, 30, "移動の出目+3"),
                Charm("ipponofuda", "一歩戻りの札", Run.CharmKind.MoveBack, 1, 15, "移動の出目−1（最低1）"),
                Charm("dokugirinokusuri", "毒霧の薬", Run.CharmKind.PoisonEnemy, 6, 50, "狙っている敵に毒6"),
                Charm("ooban", "大判", Run.CharmKind.GainGold, 45, 70, "ゴールドを45得る（いつでも使える）"),
                Charm("yoroinofuda", "鎧の札", Run.CharmKind.GainBlock, 14, 40, "戦闘中に使う：防御+14（このラウンド）"),
                Charm("ooraifu", "大雷符", Run.CharmKind.ThunderAll, 20, 100, "戦闘中に使う：全部の敵に20ダメージ（とどめは刺せない）"),
                Charm("kiainofuda", "気合の札", Run.CharmKind.GainStrength, 1, 20, "戦闘中に使う：この戦闘の間、筋力+1"),
                Charm("oodatsuryoku", "大脱力の薬", Run.CharmKind.WeakenEnemy, 4, 55, "狙っている敵に脱力4"),
                Charm("oojakutai", "大弱体の薬", Run.CharmKind.VulnerableEnemy, 4, 55, "狙っている敵に弱体4"),
                Charm("oozeijaku", "大脆弱の薬", Run.CharmKind.FrailEnemy, 4, 55, "狙っている敵に脆弱4"),
                Charm("hourainogan", "蓬莱の丸薬", Run.CharmKind.MaxHp, 12, 120, "最大HPを12増やす（HPも12回復。いつでも使える）"),
                Charm("ookenshu", "大堅守の札", Run.CharmKind.Fortify, 4, 70, "戦闘中に使う：堅守4（ラウンドの終わりに防御が半分残る）"),
                // 2026-10-16 その2（新しい効き方：全部の敵に・呪い払い・面を鍛える）。TODO(仕様): 数値・値段は仮
                Charm("dokugiridama", "毒霧玉", Run.CharmKind.PoisonAll, 3, 45, "戦闘中に使う：全部の敵に毒3"),
                Charm("oodokugiridama", "大毒霧玉", Run.CharmKind.PoisonAll, 6, 75, "戦闘中に使う：全部の敵に毒6"),
                Charm("datsuryokunokemuri", "脱力の煙", Run.CharmKind.WeakenAll, 2, 50, "戦闘中に使う：全部の敵に脱力2"),
                Charm("jakutainokemuri", "弱体の煙", Run.CharmKind.VulnerableAll, 2, 50, "戦闘中に使う：全部の敵に弱体2"),
                Charm("yakubarainofuda", "厄払いの札", Run.CharmKind.RemoveCurse, 0, 40, "呪いのダイスを1つ捨てる（戦闘の外で使う）"),
                Charm("toishi", "荒砥", Run.CharmKind.ForgeFace, 1, 35, "いちばん弱いダイスの、いちばん小さい面+1（戦闘の外で使う）"),
                Charm("meikounotoishi", "名工の砥石", Run.CharmKind.ForgeFace, 3, 85, "いちばん弱いダイスの、いちばん小さい面+1 を3回（戦闘の外で使う）"),
                Charm("jizounomamori", "地蔵の守り", Run.CharmKind.GainBlock, 7, 20, "戦闘中に使う：防御+7（このラウンド）"),
                Charm("fudounofuda", "不動の札", Run.CharmKind.Fortify, 3, 55, "戦闘中に使う：堅守3（ラウンドの終わりに防御が半分残る）"),
                Charm("kintokinohiyaku", "金時の秘薬", Run.CharmKind.GainStrength, 5, 90, "戦闘中に使う：この戦闘の間、筋力+5"),
                Charm("mannenyaku", "万能薬", Run.CharmKind.Heal, 60, 85, "HPを60回復"),
                Charm("raijuunotsume", "雷獣の爪", Run.CharmKind.ThunderAll, 11, 55, "戦闘中に使う：全部の敵に11ダメージ（とどめは刺せない）"),
                Charm("kozeniire", "小銭入れ", Run.CharmKind.GainGold, 20, 30, "ゴールドを20得る（いつでも使える）"),
                Charm("jiyounogan", "滋養の丸薬", Run.CharmKind.MaxHp, 3, 35, "最大HPを3増やす（HPも3回復。いつでも使える）"),
                Charm("hikyakunofuda", "飛脚の札", Run.CharmKind.MoveForward, 5, 50, "移動の出目+5"),
                // 2026-10-16 その3（新しい効き方：敵の筋力を下げる・ダイスを化けさせる）。TODO(仕様): 数値・値段は仮
                Charm("bakefuda", "化け札", Run.CharmKind.Transform, 0, 90, "弱いダイスを1段上のレア度に化けさせる（戦闘の外で）"),
                Charm("chikaranukinokou", "力抜きの香", Run.CharmKind.SapStrength, 2, 45, "狙っている敵の筋力−2（攻撃が弱くなる）"),
                Charm("oochikaranukinokou", "大力抜きの香", Run.CharmKind.SapStrength, 4, 75, "狙っている敵の筋力−4（攻撃が弱くなる）"),
                Charm("yakusou", "薬草", Run.CharmKind.Heal, 15, 25, "HPを15回復"),
                Charm("nigirimeshi", "塩むすび", Run.CharmKind.Heal, 25, 40, "HPを25回復"),
                Charm("moushinnofuda", "猛進の札", Run.CharmKind.MoveForward, 6, 60, "移動の出目+6"),
                Charm("koutainofuda", "後退の札", Run.CharmKind.MoveBack, 3, 25, "移動の出目−3（最低1）"),
                Charm("teppekinofuda", "鉄壁の札", Run.CharmKind.GainBlock, 35, 85, "戦闘中に使う：防御+35（このラウンド）"),
                Charm("kishinnohiyaku", "鬼神の秘薬", Run.CharmKind.GainStrength, 7, 130, "戦闘中に使う：この戦闘の間、筋力+7"),
                Charm("raimeifu", "雷鳴符", Run.CharmKind.ThunderAll, 30, 140, "戦闘中に使う：全部の敵に30ダメージ（とどめは刺せない）"),
                Charm("oodatsuryokunokemuri", "大脱力の煙", Run.CharmKind.WeakenAll, 4, 85, "戦闘中に使う：全部の敵に脱力4"),
                Charm("oojakutainokemuri", "大弱体の煙", Run.CharmKind.VulnerableAll, 4, 85, "戦闘中に使う：全部の敵に弱体4"),
                Charm("sennennogan", "千年の丸薬", Run.CharmKind.MaxHp, 20, 180, "最大HPを20増やす（HPも20回復。いつでも使える）"),
                Charm("kinnobebou", "金の延べ棒", Run.CharmKind.GainGold, 100, 140, "ゴールドを100得る（いつでも使える）"),
                Charm("kajinokozuchi", "鍛冶の小槌", Run.CharmKind.ForgeFace, 2, 60, "いちばん弱いダイスの、いちばん小さい面+1 を2回（戦闘の外で使う）"),
                // 2026-10-16 その4（新しい効き方：全部振り直す・最大HPの割合で回復・狙った敵にダメージ・敵の防御を消す）。TODO(仕様): 数値・値段は仮
                Charm("furinaoshinomakimono", "振り直しの巻物", Run.CharmKind.RerollAll, 0, 50, "戦闘中に使う：まだ使っていないダイスを全部振り直す"),
                Charm("youjouyaku", "養生薬", Run.CharmKind.HealPercent, 30, 45, "HPを最大の30%回復"),
                Charm("reiyaku", "霊薬", Run.CharmKind.HealPercent, 60, 95, "HPを最大の60%回復"),
                Charm("hamaya", "破魔矢", Run.CharmKind.Hamaya, 12, 45, "戦闘中に使う：狙っている敵に12ダメージ（とどめは刺せない）"),
                Charm("oohamaya", "大破魔矢", Run.CharmKind.Hamaya, 24, 85, "戦闘中に使う：狙っている敵に24ダメージ（とどめは刺せない）"),
                Charm("kabutowarinofuda", "兜割りの札", Run.CharmKind.BreakBlock, 0, 40, "戦闘中に使う：全部の敵の防御を消す"),
                Charm("jouhekinofuda", "城壁の札", Run.CharmKind.GainBlock, 50, 120, "戦闘中に使う：防御+50（このラウンド）"),
                Charm("kongoufuenofuda", "金剛不壊の札", Run.CharmKind.Fortify, 6, 100, "戦闘中に使う：堅守6（ラウンドの終わりに防御が半分残る）"),
                Charm("dokujanokusuri", "毒蛇の薬", Run.CharmKind.PoisonEnemy, 12, 85, "狙っている敵に毒12"),
                Charm("datsuryokunohiyaku", "脱力の秘薬", Run.CharmKind.WeakenEnemy, 6, 80, "狙っている敵に脱力6"),
                Charm("jakutainohiyaku", "弱体の秘薬", Run.CharmKind.VulnerableEnemy, 6, 80, "狙っている敵に弱体6"),
                Charm("zeijakunohiyaku", "脆弱の秘薬", Run.CharmKind.FrailEnemy, 6, 80, "狙っている敵に脆弱6"),
                Charm("dokunoame", "毒の雨", Run.CharmKind.PoisonAll, 9, 110, "戦闘中に使う：全部の敵に毒9"),
                Charm("chikarafuujinokou", "力封じの香", Run.CharmKind.SapStrength, 6, 105, "狙っている敵の筋力−6（攻撃が弱くなる）"),
                // 2026-10-16 その5（これで 100 種。新しい効き方：振れるダイス+1・防御2倍・毒2倍）。TODO(仕様): 数値・値段は仮
                Charm("mitsumenosaizutsu", "三つ目の賽筒", Run.CharmKind.MoreDice, 1, 90, "戦闘中に使う：この戦闘の間、1ラウンドに振れるダイス+1（最大4個）"),
                Charm("kagaminotatenofuda", "鏡の盾の札", Run.CharmKind.DoubleBlock, 0, 60, "戦闘中に使う：今の防御を2倍にする（防御のダイスを使ったあとに）"),
                Charm("dokutsubo", "蠱毒の壺", Run.CharmKind.DoublePoison, 0, 60, "戦闘中に使う：全部の敵の毒を2倍にする"),
                Charm("myouyaku", "妙薬", Run.CharmKind.HealPercent, 100, 150, "HPを全回復"),
                Charm("kohamaya", "小破魔矢", Run.CharmKind.Hamaya, 6, 25, "戦闘中に使う：狙っている敵に6ダメージ（とどめは刺せない）"),
                Charm("kodokugiridama", "小毒霧玉", Run.CharmKind.PoisonAll, 2, 25, "戦闘中に使う：全部の敵に毒2"),
                Charm("tabinobentou", "旅の弁当", Run.CharmKind.Heal, 12, 20, "HPを12回復"),
                Charm("kashiwadenofuda", "柏手の札", Run.CharmKind.GainBlock, 5, 15, "戦闘中に使う：防御+5（このラウンド）"),
                Charm("chototsunofuda", "猪突の札", Run.CharmKind.MoveForward, 8, 75, "移動の出目+8"),
            };
            // みんなで遊ぶとき専用（協力の報酬・ショップにも出る）。TODO(仕様): 数値・値段は仮
            var multi = new List<Run.CharmData>
            {
                Charm("wakeainokusuri", "分け合いの薬", Run.CharmKind.HealParty, 16, 50, "戦闘中に使う：自分と仲間全員のHPを16回復（みんなで遊ぶとき専用）"),
                Charm("musubinomamori", "結びの守り", Run.CharmKind.BlockParty, 8, 45, "戦闘中に使う：自分と仲間全員に防御+8（このラウンド。みんなで遊ぶとき専用）"),
                Charm("oowakeainokusuri", "大分け合いの薬", Run.CharmKind.HealParty, 30, 90, "戦闘中に使う：自分と仲間全員のHPを30回復（みんなで遊ぶとき専用）"),
                Charm("oomusubinomamori", "大結びの守り", Run.CharmKind.BlockParty, 15, 80, "戦闘中に使う：自分と仲間全員に防御+15（このラウンド。みんなで遊ぶとき専用）"),
            };
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.charmPool = list;
                config.multiplayerCharmPool = multi;
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] お守り{list.Count}種のデータを作成・更新しました。");
        }

        static Run.CharmData Charm(string id, string name, Run.CharmKind kind, int amount, int price, string description)
        {
            var path = $"{CharmDir}/Charm_{id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<Run.CharmData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<Run.CharmData>();
                AssetDatabase.CreateAsset(data, path);
            }
            data.id = id;
            data.displayName = name;
            data.kind = kind;
            data.amount = amount;
            data.price = price;
            data.description = description;
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{CharmArtDir}/charm_{id}.png");
            if (icon != null) data.icon = icon;
            else Debug.LogWarning($"[賽ノ道] お守りの絵 {CharmArtDir}/charm_{id}.png がありません。");
            EditorUtility.SetDirty(data);
            return data;
        }

        // ---- 敵（仕様書 第7章 第1層） ----

        const string EnemyDir = "Assets/_Project/Data/Enemies";

        static Intent Atk(int v) => new Intent(IntentType.Attack, v);
        static Intent Blk(int v) => new Intent(IntentType.Block, v);
        // 攻撃＋防御（STS と同じ。行動のあとに防御を得て、次のラウンドの攻撃を防ぐ）
        static Intent AtkBlk(int attack, int block) => new Intent(IntentType.Attack, attack) { block = block };
        static Intent Buff(int v) => new Intent(IntentType.Buff, v);
        static Intent Multi(int v, int hits) => new Intent(IntentType.MultiAttack, v, hits);
        static Intent Weak(int v) => new Intent(IntentType.Debuff, v);
        static Intent Frail(int v) => new Intent(IntentType.Frail, v);
        static Intent Seal(int count = 0) => new Intent(IntentType.Seal, count);
        static Intent Poison(int v) => new Intent(IntentType.Poison, v);
        static Intent Charge(int staggerAt = 0) => new Intent(IntentType.Charge, staggerAt);
        static Intent Mirror() => new Intent(IntentType.MirrorAttack, 0);
        static Intent Curse() => new Intent(IntentType.Curse, 0);
        /// <summary>裏返し：このラウンドに振った出目が 7−出目 になる。block があれば行動のあとに防御も得る。</summary>
        static Intent Invert(int block = 0) => new Intent(IntentType.Invert, 0) { block = block };
        static Intent RollAtk(int multiplier, int faces) => new Intent(IntentType.RollAttack, multiplier) { maxValue = faces };
        static Intent RollBlk(int multiplier, int faces) => new Intent(IntentType.RollBlock, multiplier) { maxValue = faces };
        static Intent Rewrite() => new Intent(IntentType.RewriteFate, 0);

        [MenuItem("SaiNoMichi/Data/Build Enemy")]
        public static void BuildEnemies()
        {
            // 開発者の要望：雑魚にも筋力の強化を入れる（長引くと痛くなる）。TODO(仕様): 強化の量と順番は仮
            // 開発者の要望：防御だけのラウンドは退屈なので、ほとんどを「攻撃＋防御」に（STS と同じ）。TODO(仕様): 攻撃＋防御の値は仮
            var slime = Enemy("slime", "スライム", EnemyKind.Normal, 12, EnemyBehavior.Sequence, true, Atk(5), Buff(1), Atk(5), AtkBlk(3, 4));
            // TODO(仕様): 野ウサギは本来2体で出る想定。フェーズ1は1体なので HP を 8 → 14 に上げる
            // 行動はランダムをやめて交互に（行動を読めるように。開発者の方針）
            var usagi = Enemy("usagi", "野ウサギ", EnemyKind.Normal, 14, EnemyBehavior.Sequence, true, Multi(2, 2), Atk(4), Buff(1));
            var koni = Enemy("koni", "小鬼", EnemyKind.Normal, 15, EnemyBehavior.Sequence, false, Buff(1), Atk(5), Atk(5));
            var kinoko = Enemy("kinoko", "化け茸", EnemyKind.Normal, 14, EnemyBehavior.Sequence, true, Weak(1), Atk(4), Atk(4));
            var thief = Enemy("sainusubito", "賽盗人", EnemyKind.Elite, 32, EnemyBehavior.Sequence, false, Seal(), Atk(7), Multi(3, 3));
            // 賽振りはやめ、決まった行動の繰り返しに（開発者の判断）。4ラウンドごとの「振り出しに戻れ」は残す
            // TODO(仕様): 攻撃8 → 防御10 → 攻撃12 の値は仮
            var banjin = Enemy("banjin", "双六の番人", EnemyKind.Boss, 55, EnemyBehavior.Sequence, false,
                Atk(8), AtkBlk(5, 10), Atk(12), new Intent(IntentType.ResetDice, 0));

            // ---- 第2層：鍾乳洞（仕様書 第7章） ----
            var koumori = Enemy("koumori", "大蝙蝠", EnemyKind.Normal, 16, EnemyBehavior.Sequence, true, Multi(3, 2), Multi(3, 2), Buff(1));
            var gaikotsu = Enemy("gaikotsu", "骸骨兵", EnemyKind.Normal, 24, EnemyBehavior.Sequence, true, AtkBlk(5, 8), Atk(9), Buff(2));
            var dokugumo = Enemy("dokugumo", "毒蜘蛛", EnemyKind.Normal, 20, EnemyBehavior.Sequence, true, Poison(3), Atk(6), Buff(1));
            // 溜めは止められない（数字なし）。攻撃＋防御12の次に溜め、そのあと大攻撃10（層の倍率で約18。16だと負け率70%だった）
            var iwa = Enemy("iwaningyou", "岩の人形", EnemyKind.Normal, 28, EnemyBehavior.Sequence, false, AtkBlk(5, 12), Charge(), Atk(10));
            // 前のラウンドのプレイヤーの攻撃値をそのまま返す（大きく攻めた次は守る）
            var utsushi = Enemy("utsushikagami", "写し鏡", EnemyKind.Elite, 50, EnemyBehavior.Sequence, false, Mirror());
            // 攻撃10 → 封印（ランダムに1個）→ 溜め（12以上で怯む）→ 攻撃25 の4ラウンド周期
            var ooago = Enemy("ooago", "大顎", EnemyKind.Boss, 100, EnemyBehavior.Sequence, false, Atk(10), Seal(), Charge(12), Atk(25));

            // ---- 第3層：鬼の城（仕様書 第7章） ----
            var jujutsushi = Enemy("jujutsushi", "呪術師", EnemyKind.Normal, 36, EnemyBehavior.Sequence, true, Frail(2), Atk(9), Curse());
            var onimusha = Enemy("onimusha", "鬼武者", EnemyKind.Normal, 40, EnemyBehavior.Sequence, false, Buff(2), Atk(12), AtkBlk(7, 12));
            // 22×2体。片方を倒すと、残った方が筋力+2（自動プレイで負け率が高かったので弱めた）
            // TODO(仕様): 双子鬼・石の守護者・首狩りの行動は仕様書にないので仮
            var futago = Enemy("futagooni", "双子鬼", EnemyKind.Normal, 22, EnemyBehavior.Sequence, true, Atk(4), Multi(2, 2), AtkBlk(3, 6));
            futago.count = 2;
            futago.allyDefeatedStrength = 2;
            var shugosha = Enemy("ishinoshugosha", "石の守護者", EnemyKind.Normal, 36, EnemyBehavior.Sequence, true, AtkBlk(5, 8), Atk(10), Buff(2));
            shugosha.damageCapPerRound = 10;
            var kubikari = Enemy("kubikari", "首狩り", EnemyKind.Elite, 80, EnemyBehavior.Sequence, false, Atk(9), Multi(4, 2), AtkBlk(6, 10));
            kubikari.enrageHpPercent = 50;
            kubikari.enrageAttackPercent = 175;
            // 第1形態：8面ダイスの出目×2の攻撃と出目×3の防御を交互（出目は予告で見える）
            // 第2形態（HP 半分以下）：運命の書き換え → 3ラウンドごとに繰り返す
            var hachimen = Enemy("hachimen", "賽の神・八面", EnemyKind.Boss, 165, EnemyBehavior.Sequence, false, RollAtk(3, 8), RollBlk(3, 8));
            hachimen.phase2HpPercent = 50;
            hachimen.phase2Pattern = new List<Intent> { Rewrite(), RollAtk(3, 8), RollBlk(3, 8) };
            foreach (var e in new[] { futago, shugosha, kubikari, hachimen }) EditorUtility.SetDirty(e);

            // 開発者の要望で追加（種類を増やす）。各層に通常の敵を1体ずつ。TODO(仕様): HP・行動は仮
            var kappa = Enemy("kappa", "河童", EnemyKind.Normal, 18, EnemyBehavior.Sequence, true, Frail(1), Atk(4), AtkBlk(2, 5));
            var yukionna = Enemy("yukionna", "雪女", EnemyKind.Normal, 24, EnemyBehavior.Sequence, true, Weak(2), Multi(3, 2), AtkBlk(4, 8));
            var oomukade = Enemy("oomukade", "大百足", EnemyKind.Normal, 32, EnemyBehavior.Sequence, true, Poison(4), Multi(2, 3), Buff(2));

            // 開発者の要望：エリートとボスを各層1体ずつ増やす。ボスはランごとにどちらかが出る。TODO(仕様): HP・行動は仮
            var sanzoku = Enemy("sanzoku", "山賊の頭", EnemyKind.Elite, 32, EnemyBehavior.Sequence, false, Buff(1), Atk(6), AtkBlk(4, 8));
            var gama = Enemy("gama", "大蝦蟇", EnemyKind.Boss, 60, EnemyBehavior.Sequence, false, Poison(2), Atk(9), AtkBlk(6, 10), Multi(2, 3));
            var kamaitachi = Enemy("kamaitachi", "鎌鼬", EnemyKind.Elite, 46, EnemyBehavior.Sequence, false, Multi(3, 3), Frail(2), Multi(4, 3));
            var tsuchigumo = Enemy("tsuchigumo", "土蜘蛛", EnemyKind.Boss, 95, EnemyBehavior.Sequence, false, Seal(), Poison(3), Atk(11), AtkBlk(7, 14));
            var hannya = Enemy("hannya", "般若", EnemyKind.Elite, 85, EnemyBehavior.Sequence, false, Atk(13), Curse(), Buff(3), AtkBlk(10, 12));
            var daitengu = Enemy("daitengu", "大天狗", EnemyKind.Boss, 165, EnemyBehavior.Sequence, false, Weak(2), Multi(3, 3), AtkBlk(10, 16), Atk(18));
            foreach (var e in new[] { sanzoku, gama, kamaitachi, tsuchigumo, hannya, daitengu }) EditorUtility.SetDirty(e);

            // 開発者の要望：トリッキーな敵（各層エリート3・ボス3に）。TODO(仕様): HP・行動・棘の値は仮
            // 裏返し：その予告のラウンドは出目が 7−出目。出目を上げすぎたダイスが裏目に出る
            // 棘：大きい出目で攻撃すると、ダイス1個ごとに自分がダメージ
            var amanojaku = Enemy("amanojaku", "天邪鬼", EnemyKind.Elite, 34, EnemyBehavior.Sequence, false, Invert(4), Atk(8), Invert(4), Multi(3, 3));
            var yamaoroshi = Enemy("yamaoroshi", "山颪", EnemyKind.Boss, 62, EnemyBehavior.Sequence, false, Atk(8), AtkBlk(5, 10), Multi(2, 3), AtkBlk(4, 8));
            yamaoroshi.thorns = 2;
            yamaoroshi.thornsMinValue = 6;
            var harionago = Enemy("harionago", "針女", EnemyKind.Elite, 46, EnemyBehavior.Sequence, false, Multi(3, 3), Frail(2), AtkBlk(6, 10));
            harionago.thorns = 2;
            harionago.thornsMinValue = 5;
            var nue = Enemy("nue", "鵺", EnemyKind.Boss, 100, EnemyBehavior.Sequence, false, Invert(8), Multi(4, 3), Seal(), Invert(8), Atk(15));
            var satori = Enemy("satori", "覚", EnemyKind.Elite, 78, EnemyBehavior.Sequence, false, Invert(10), Atk(14), Invert(10), Buff(2));
            var shuten = Enemy("shuten", "酒呑童子", EnemyKind.Boss, 155, EnemyBehavior.Sequence, false, Weak(2), Atk(13), AtkBlk(10, 15), Multi(3, 3));
            shuten.thorns = 3;
            shuten.thornsMinValue = 7; // 鍛冶で6より上にした面だけが刺さる（出目を上げすぎると裏目）
            foreach (var e in new[] { amanojaku, yamaoroshi, harionago, nue, satori, shuten }) EditorUtility.SetDirty(e);
            // 2026-10-16 追加：第3層のボス「九尾の狐」。化けている間は惑わし（脱力・あべこべ）、HP 半分で本性を現して大技（力をためる→大攻撃）
            // TODO(仕様): HP・行動は仮
            var kyuubi = Enemy("kyuubi", "九尾の狐", EnemyKind.Boss, 170, EnemyBehavior.Sequence, false, Weak(2), Multi(3, 3), Invert(10), AtkBlk(9, 12));
            kyuubi.phase2HpPercent = 50;
            kyuubi.phase2Pattern = new List<Intent> { Charge(16), Atk(28), Multi(4, 3), Seal() };
            EditorUtility.SetDirty(kyuubi);
            // 2026-10-16 追加：第1層のボス「雷獣」（すばやい連続攻撃。怒ると攻撃が強くなる）・第2層のボス「海坊主」（大波で封印・一度に受けるダメージに上限）
            // TODO(仕様): HP・行動は仮
            var raijuu = Enemy("raijuu", "雷獣", EnemyKind.Boss, 58, EnemyBehavior.Sequence, false, Multi(2, 4), Weak(1), Atk(9), AtkBlk(5, 8));
            raijuu.enrageHpPercent = 40;
            raijuu.enrageAttackPercent = 150;
            var umibouzu = Enemy("umibouzu", "海坊主", EnemyKind.Boss, 105, EnemyBehavior.Sequence, false, AtkBlk(7, 12), Seal(), Atk(13), Multi(3, 4));
            umibouzu.damageCapPerRound = 22;
            EditorUtility.SetDirty(raijuu);
            EditorUtility.SetDirty(umibouzu);
            // 2026-10-16 追加：各層の強敵を1体ずつ（大猪・濡れ女・牛鬼）。TODO(仕様): HP・行動は仮
            var ooinoshishi = Enemy("ooinoshishi", "大猪", EnemyKind.Elite, 34, EnemyBehavior.Sequence, false, Atk(6), Buff(1), Atk(8), AtkBlk(4, 6));
            var nureonna = Enemy("nureonna", "濡れ女", EnemyKind.Elite, 46, EnemyBehavior.Sequence, false, Poison(2), Multi(3, 3), Weak(1), AtkBlk(6, 8));
            var ushioni = Enemy("ushioni", "牛鬼", EnemyKind.Elite, 78, EnemyBehavior.Sequence, false, Poison(2), AtkBlk(9, 12), Atk(13), Multi(4, 3));
            ushioni.enrageHpPercent = 50;
            ushioni.enrageAttackPercent = 140;
            foreach (var e in new[] { ooinoshishi, nureonna, ushioni }) EditorUtility.SetDirty(e);

            // 開発者の要望で追加（2026-10-16。絵は ChatGPT）：各層の通常の敵を2体ずつ。行動は決まった順（予告で読める）。TODO(仕様): HP・行動の値は仮
            var chouchin = Enemy("chouchin", "提灯お化け", EnemyKind.Normal, 13, EnemyBehavior.Sequence, true, Poison(2), Atk(4), AtkBlk(3, 4));
            var tanuki = Enemy("tanuki", "化け狸", EnemyKind.Normal, 14, EnemyBehavior.Sequence, true, Atk(3), Invert(2), Atk(5));
            var karakasa = Enemy("karakasa", "唐傘お化け", EnemyKind.Normal, 20, EnemyBehavior.Sequence, true, Multi(2, 3), AtkBlk(4, 5), Buff(1));
            // 塗り壁：1ラウンドに受けるダメージに上限がある（大きな一撃より、毎ラウンド削る）
            var nurikabe = Enemy("nurikabe", "塗り壁", EnemyKind.Normal, 30, EnemyBehavior.Sequence, false, AtkBlk(4, 8), Atk(8), AtkBlk(4, 8), Buff(1));
            nurikabe.damageCapPerRound = 12;
            var rokurokubi = Enemy("rokurokubi", "ろくろ首", EnemyKind.Normal, 32, EnemyBehavior.Sequence, true, Weak(2), Atk(10), Multi(5, 2));
            // がしゃどくろ：溜めてから大きな一撃（溜めのラウンドに14以上与えると怯む。防御で受けてもよい）
            var gashadokuro = Enemy("gashadokuro", "がしゃどくろ", EnemyKind.Normal, 42, EnemyBehavior.Sequence, false, AtkBlk(6, 10), Charge(14), Atk(20));
            foreach (var e in new[] { chouchin, tanuki, karakasa, nurikabe, rokurokubi, gashadokuro }) EditorUtility.SetDirty(e);

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null)
            {
                config.battleEnemies = new List<EnemyData> { slime, usagi, koni, kinoko };
                config.eliteEnemies = new List<EnemyData> { thief };
                config.boss = banjin;
                config.earlyBattleCount = 3;

                // 3層（仕様書 第2章・第8章）。第2・第3層の敵はフェーズ2の手順3・5で作るまで、第1層の敵で仮に埋める
                var layer1 = new List<EnemyData> { slime, usagi, koni, kinoko, kappa, chouchin, tanuki };
                var layer2 = new List<EnemyData> { koumori, gaikotsu, dokugumo, iwa, yukionna, karakasa, nurikabe };
                var layer3 = new List<EnemyData> { jujutsushi, onimusha, futago, shugosha, oomukade, rokurokubi, gashadokuro };
                config.layers = new List<LayerData>
                {
                    Layer("野原の街道", LayerWeights(18, 7, 10, 3), layer1, new[] { thief, sanzoku, amanojaku, ooinoshishi }, new[] { banjin, gama, yamaoroshi, raijuu }),
                    // 第2層（仕様書 第7章「第2層：鍾乳洞」）
                    Layer("鍾乳洞", LayerWeights(16, 10, 9, 4), layer2, new[] { utsushi, kamaitachi, harionago, nureonna }, new[] { ooago, tsuchigumo, nue, umibouzu }),
                    // 第3層（仕様書 第7章「第3層：鬼の城」）
                    Layer("鬼の城", LayerWeights(14, 11, 8, 5), layer3, new[] { kubikari, hannya, satori, ushioni }, new[] { hachimen, daitengu, shuten, kyuubi }),
                };
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[賽ノ道] 敵のデータ（第1〜第3層 39体）を作成・更新しました。");
        }

        /// <param name="bosses">ボスの候補（ランごとに1体）。最初のものを boss にも入れる（古い設定との互換）。</param>
        static LayerData Layer(string name, List<TileWeight> weights, List<EnemyData> enemies, EnemyData[] elites, EnemyData[] bosses)
        {
            var board = new LayerBoardSettings { weights = weights };
            return new LayerData
            {
                displayName = name,
                board = board,
                battleEnemies = new List<EnemyData>(enemies),
                eliteEnemies = new List<EnemyData>(elites),
                boss = bosses[0],
                bossChoices = new List<EnemyData>(bosses),
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
                new TileWeight(TileType.Battle, 38),
                new TileWeight(TileType.Event, evt),
                new TileWeight(TileType.Trap, trap),
                new TileWeight(TileType.Rest, rest),
                new TileWeight(TileType.Treasure, 12),
                new TileWeight(TileType.Shop, 7),
                new TileWeight(TileType.Forge, 5),
                new TileWeight(TileType.Elite, elite),
            };
        }

        /// <summary>図鑑に出す敵の言い伝え（Hades の図鑑風のひとこと）。TODO(仕様): 文は仮</summary>
        static readonly Dictionary<string, string> EnemyLore = new Dictionary<string, string>
        {
            { "slime", "街道のぬかるみに溜まった泥が、旅人の足音に誘われて動き出したもの。" },
            { "usagi", "野原を跳ねる気の荒い兎。月の兎の遠い親戚だと言い張っている。" },
            { "koni", "鬼の子分。親分に見せる手柄を探して、旅人の荷を狙う。" },
            { "kinoko", "古い切り株に生えた茸が、雨の夜に化けたもの。胞子で人を惑わす。" },
            { "sainusubito", "旅人の賽ばかりを狙う盗人。盗んだ賽で一山当てるのが夢らしい。" },
            { "banjin", "賽ノ道の最初の関所を守る番人。旅人を振り出しに戻すのが何より好き。" },
            { "koumori", "鍾乳洞の天井に群れる大きな蝙蝠。灯りを見ると襲ってくる。" },
            { "gaikotsu", "洞窟で倒れた兵の骨。まだ戦が終わっていないと信じて歩き回る。" },
            { "dokugumo", "岩の隙間に巣を張る蜘蛛。牙の毒がじわじわと体を蝕む。" },
            { "iwaningyou", "洞窟の岩が人の形に積み上がったもの。硬いが、動きは鈍い。" },
            { "utsushikagami", "覗いた者の姿を写し取る古い鏡。こちらの手まで真似てくる。" },
            { "ooago", "鍾乳洞の奥に潜む巨大な顎。賽ごと旅人を噛み砕こうとする。" },
            { "jujutsushi", "鬼の城に仕える術者。呪いのダイスを押し付け、体を脆くする。" },
            { "onimusha", "鬼に魂を売った侍。刀の腕は確かで、一太刀が重い。" },
            { "futagooni", "いつも二人で現れる鬼の兄弟。片方が倒れると、もう片方が怒り狂う。" },
            { "ishinoshugosha", "城の門を守る石像。守りを固めながら、じわじわと力を増す。" },
            { "kubikari", "鬼の城の見回り役。大鎌を何度も振り回して首を狙う。" },
            { "hachimen", "八つの顔を持つ賽の神。出目そのものを操り、道の果てで旅人を試す。" },
            { "kappa", "川辺に住む妖怪。頭の皿の水が乾くと力が抜けるらしい。相撲が大好き。" },
            { "yukionna", "吹雪の夜に現れる白い女。凍える息で旅人の力を奪う。" },
            { "oomukade", "山を七巻き半するという大百足の、そのまた子孫。" },
            { "sanzoku", "街道を荒らす山賊の親分。気合いを入れるたびに手強くなる。" },
            { "gama", "沼の主の大蝦蟇。毒の油を吐き、長い舌で何度も打ちすえる。" },
            { "kamaitachi", "つむじ風に乗って現れる三匹の鼬。気づいた時には切られている。" },
            { "tsuchigumo", "山の洞に棲む大蜘蛛。糸で賽を封じ、毒で弱らせてから襲う。" },
            { "hannya", "嫉妬と怒りで鬼となった女の面。呪いをかけ、怒るほどに力が増す。" },
            { "daitengu", "山の天狗の頭領。羽団扇の一振りで嵐を起こす。" },
            { "amanojaku", "何でも逆さにしたがるへそ曲がり。旅人の出目をあべこべにする。" },
            { "yamaoroshi", "山から吹き下ろす冷たい風が、獣の姿をとったもの。" },
            { "harionago", "髪の先が鉤針になった女妖。笑いかけた者を髪で絡め取る。" },
            { "nue", "猿の顔・狸の体・虎の手足・蛇の尾を持つ怪物。夜に不吉な声で鳴く。" },
            { "satori", "人の心を読む山の妖怪。読んだ心をあべこべに返してくる。" },
            { "shuten", "大江山の鬼の頭領。酒を好み、鬼の城の主として旅人を待つ。" },
            { "kyuubi", "千年を生きた妖狐。美しい姿で人を惑わし、追いつめられると九本の尾をあらわにして本性を見せる。" },
            { "raijuu", "雷とともに天から落ちてくる獣。すばしこく、傷を負うといっそう荒れ狂う。" },
            { "umibouzu", "嵐の夜の海に現れる巨大な坊主頭。大波で旅人を呑みこみ、どれだけ叩いても一度には沈まない。" },
            { "ooinoshishi", "山の主の大猪。鼻息を荒くするたびに力が増し、まっすぐ突っ込んでくる。" },
            { "nureonna", "水辺に現れる、蛇の体をした女の妖怪。長い尾で何度も打ちすえ、毒で弱らせる。" },
            { "ushioni", "牛の頭に蜘蛛の脚を持つ大妖怪。毒の息を吐き、傷を負うと猛り狂う。" },
            { "chouchin", "古くなった提灯が化けたもの。破れ目が口になって笑う。" },
            { "tanuki", "人を化かすのが得意な狸。木の葉の小判をつかませてくる。" },
            { "karakasa", "百年使われた傘が化けた付喪神。一本足で跳ねて体当たりする。" },
            { "nurikabe", "夜道に突然現れる壁。一度にどれだけ叩いても、少ししか崩れない。" },
            { "rokurokubi", "夜になると首が伸びる女。伸びた首で遠くから噛みついてくる。" },
            { "gashadokuro", "野ざらしの骨が集まってできた巨大な骸骨。力をためて大きく振り下ろす。" },
        };

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
            data.lore = EnemyLore.TryGetValue(id, out var lore) ? lore : "";
            data.kind = kind;
            data.maxHp = hp;
            data.behavior = behavior;
            data.earlyOk = earlyOk;
            data.pattern = new List<Intent>(pattern);
            // 特性は呼び出し側で必要なときだけ付ける（作り直すたびに戻す）
            data.damageCapPerRound = 0;
            data.enrageHpPercent = 0;
            data.enrageAttackPercent = 200;
            data.thorns = 0;
            data.thornsMinValue = 5;
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
            data.multiHit = false;
            data.hitsAll = false;
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
