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
                config.enemyBalance = new EnemyBalance
                {
                    normalHpPercent = new[] { 225, 195, 145 },
                    // 攻撃は HP を 75 にしたのに合わせて EnemyAttackScale% 倍（自動プレイで踏破率がそろうように決めた）
                    normalAttackPercent = Scale(new[] { 265, 200, 215 }),
                    eliteHpPercent = new[] { 170, 155, 125 },
                    eliteAttackPercent = Scale(new[] { 150, 140, 130 }),
                    bossHpPercent = new[] { 115, 120, 90 },
                    bossAttackPercent = Scale(new[] { 140, 130, 140 }),
                };
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            Debug.Log("[賽ノ道] すべてのデータを作成・更新し、分岐する盤面を有効にしました。");
        }

        // TODO(仕様): HP を 75 にしたときの敵の攻撃の倍率（%）。自動プレイで踏破率が前（段位0で約38%）とそろうように決めた（190%だと1%、130%で36%）
        const int EnemyAttackScale = 129;
        static int[] Scale(int[] values) => values.Select(v => v * EnemyAttackScale / 100).ToArray();

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
            var shigoroku = Dice("shigoroku", "四五六賽", Rarity.Uncommon, 80, "近くには止まれない", new[] { 4, 4, 5, 5, 6, 6 });
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

            var niren = Dice("niren", "二連賽", Rarity.Uncommon, 80, "中央寄りで安定", new[] { 2, 3, 3, 4, 4, 5 });
            var saiku = Dice("saiku", "細工賽", Rarity.Uncommon, 80, "6が出やすい", new[] { 1, 2, 3, 4, 6, 6 });
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
            var maneki = Dice("maneki", "招福賽", Rarity.Uncommon, 80, "移動で出目の半分のG（戦闘の弱みなし）", new[] { 1, 2, 3, 4, 5, 6 }, manekiGold);
            var kiriFlag = Effect<FlagEffect>("Fx_Kiri_Both", Trigger.OnRoll, "霧賽：戦闘で4以上が出たら攻撃にも防御にも使える");
            kiriFlag.flag = RollFlag.BothSides;
            kiriFlag.condition = new EffectCondition { minValue = 4, maxValue = 99, scene = SceneCondition.Battle };
            EditorUtility.SetDirty(kiriFlag);
            var kiri = Dice("kiri", "霧賽", Rarity.Rare, 130, "4以上が出たら攻撃にも防御にも", new[] { 1, 2, 3, 4, 4, 5 }, kiriFlag);
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
            var kame = Dice("kame", "亀賽", Rarity.Uncommon, 80, "防御+3／攻撃−2", new[] { 1, 2, 3, 4, 5, 6 },
                AddValue("Fx_Kame_Block", Trigger.OnAssignDefense, +3, "亀賽：防御に回すと+3"), AddValue("Fx_Kame_Attack", Trigger.OnAssignAttack, -2, "亀賽：攻撃に回すと−2"));
            var yakushiHeal = Effect<HealEffect>("Fx_Yakushi_Heal", Trigger.OnMoveRolled, "薬師賽：移動で使うとHP2回復");
            yakushiHeal.heal = 2;
            yakushiHeal.condition = default;
            EditorUtility.SetDirty(yakushiHeal);
            var yakushi = Dice("yakushi", "薬師賽", Rarity.Uncommon, 80, "移動で使うとHP2回復", new[] { 1, 2, 3, 4, 5, 6 }, yakushiHeal);
            var korobi = Dice("korobi", "転び賽", Rarity.Uncommon, 80, "1が出たら振り足す", new[] { 1, 1, 3, 4, 5, 6 });
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
            var karakuri = Dice("karakuri", "からくり賽", Rarity.Uncommon, 0, "細工師専用：はじめから6の面に「刃」、1の面に「堅」の刻印", new[] { 1, 2, 3, 4, 5, 6 });
            var shakujouBlock = AddValue("Fx_Shakujou_Block", Trigger.OnAssignDefense, +2, "錫杖賽：防御に回すと+2");
            var shakujouHeal = Effect<HealEffect>("Fx_Shakujou_Heal", Trigger.OnMoveRolled, "錫杖賽：移動で使うとHP1回復");
            shakujouHeal.heal = 1;
            shakujouHeal.condition = default;
            EditorUtility.SetDirty(shakujouHeal);
            var shakujou = Dice("shakujou", "錫杖賽", Rarity.Uncommon, 0, "山伏専用：防御に回すと+2・移動で使うとHP1回復", new[] { 1, 2, 3, 4, 5, 6 }, shakujouBlock, shakujouHeal);
            // レリック「式神の札」が戦闘のあいだだけ加える賽（報酬・ショップには出ない）
            Dice("shikigami", "式神賽", Rarity.Common, 0, "式神の札：戦闘のあいだだけ加わる", new[] { 2, 2, 3, 3, 4, 4 });
            // みんなで遊ぶとき専用：国めぐりのサイコロ屋・協力の報酬にだけ出る
            var shoubu = Dice("shoubu", "勝負賽", Rarity.Uncommon, 80, "国めぐりの対決で+3", new[] { 1, 2, 3, 4, 5, 6 });
            var kizuna = Dice("kizuna", "絆賽", Rarity.Uncommon, 80, "防御に回すと+2（協力の報酬にだけ出る）", new[] { 1, 2, 3, 4, 5, 6 }, AddValue("Fx_Kizuna_Block", Trigger.OnAssignDefense, +2, "絆賽：防御に回すと+2"));
            // 呪い：罠やイベントで押し付けられる。報酬・ショップには出ない
            var kake = Dice("kake", "欠け賽", Rarity.Curse, 0, "呪い：手放せない", new[] { 0, 0, 1, 1, 2, 2 });
            var sabi = Dice("sabi", "錆び賽", Rarity.Curse, 0, "呪い：振るたびに1ダメージ", new[] { 1, 2, 3, 4, 5, 6 }, rust);

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
                };
                config.travelerDicePool = new List<DiceData> { tabisai, ikasama, karakuri, shakujou };
                // みんなで遊ぶとき専用のダイス（国めぐりのサイコロ屋・協力の報酬にだけ出る）
                config.multiplayerDicePool = new List<DiceData> { shoubu, kizuna };
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
                Relic("chizushinoyatate", "地図師の矢立", Rarity.Uncommon, "戦闘マス・強敵マスにマウスを乗せると、待っている敵が見える", yatate),
                Relic("fusha", "風車", Rarity.Uncommon, "リフレッシュするたびに5G", fusha),
                Relic("chokinbako", "貯金箱", Rarity.Uncommon, "10マス進むごとに8G", chokin),
                Relic("unmeinoito", "運命の糸", Rarity.Rare, "1戦闘に1回、振ったダイス1個の出目を好きな値（1〜6）にできる", unmei),
                Relic("rokunokago", "六の加護", Rarity.Rare, "戦闘で6が出たダイスは攻撃にも防御にも使える", roku),
                Relic("kajinokanazuchi", "鍛冶の金槌", Rarity.Rare, "鍛冶で刻印をもう1つ付けられる", kanazuchi),
                Relic("senrigan", "千里眼", Rarity.Rare, "移動の前に、次に振るダイスの出目が見える", senrigan),
                Relic("chikaramizu", "力水", Rarity.Common, "戦闘開始時に筋力+1", chikaramizu),
                Relic("nigirimeshi", "握り飯", Rarity.Common, "最大HP+14（HPも14回復）", nigirimeshi),
                Relic("juzu", "数珠", Rarity.Uncommon, "戦闘中にリフレッシュしたら防御6", juzu),
                Relic("mitsudomoe", "三つ巴", Rarity.Uncommon, "3が出たら出目+3（移動でも戦闘でも）", mitsudomoe),
                Relic("kusarikatabira", "鎖帷子", Rarity.Uncommon, "戦闘開始時に防御8", kusari),
                Relic("gonomamori", "五の守り", Rarity.Uncommon, "5が出たら出目+2（移動でも戦闘でも）", goNoMamori),
                Relic("ichinokami", "一の神様", Rarity.Rare, "1が出たら出目+5（移動でも戦闘でも）", ichiNoKami),
                Relic("oonigirimeshi", "大きな握り飯", Rarity.Uncommon, "最大HP+25（HPも25回復）", oonigiri),
                Relic("kozuchi", "打ち出の小槌", Rarity.Uncommon, "得るゴールド+15%（戦闘以外も）", kozuchi),
                Relic("chinohai", "血の盃", Rarity.Common, "戦闘に勝つとHP6回復", chinohai),
                Relic("hiuchiishi", "火打ち石", Rarity.Uncommon, "戦闘開始時、狙っている敵に毒3", hiuchi),
                Relic("shikigaminofuda", "式神の札", Rarity.Rare, "戦闘開始時に式神賽（2〜4）が1個加わる（戦闘後に消える）", shikigami),
                Relic("ishijizou", "石地蔵", Rarity.Uncommon, "毎ラウンド開始時に防御2", jizou),
                Relic("takarabune", "宝船", Rarity.Common, "次の層に着くたびに40G", takarabune),
                Relic("hoozuki", "鬼灯", Rarity.Common, "次の層に着くたびにHP20回復", hoozuki),
                Relic("ninotsuzumi", "二の鼓", Rarity.Uncommon, "2が出たら出目+3（移動でも戦闘でも）", ninotsuzumi),
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
                EditorUtility.SetDirty(config);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] レリック{list.Count}種・ボスレリック{bossRelics.Count}種のデータを作成・更新しました。");
        }

        /// <summary>ボスレリック（仕様書 第10章）。良い効果と悪い効果がある。</summary>
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
            };
            // みんなで遊ぶとき専用（協力の報酬・ショップにも出る）。TODO(仕様): 数値・値段は仮
            var multi = new List<Run.CharmData>
            {
                Charm("wakeainokusuri", "分け合いの薬", Run.CharmKind.HealParty, 16, 50, "戦闘中に使う：自分と仲間全員のHPを16回復（みんなで遊ぶとき専用）"),
                Charm("musubinomamori", "結びの守り", Run.CharmKind.BlockParty, 8, 45, "戦闘中に使う：自分と仲間全員に防御+8（このラウンド。みんなで遊ぶとき専用）"),
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
