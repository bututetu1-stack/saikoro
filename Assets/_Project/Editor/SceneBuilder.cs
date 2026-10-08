using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SaiNoMichi.EditorTools
{
    /// <summary>フェーズ0のシーンと設定アセットを組み立てる。メニューから1回実行する（やり直すと上書き）。</summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Phase0.unity";
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";
        const string DiceDir = "Assets/_Project/Data/Dice/";
        const string EnemyDir = "Assets/_Project/Data/Enemies/";
        const string ArtPath = "Assets/_Project/Data/UIArt.asset";
        const string ArtDir = "Assets/_Project/Art";

        [MenuItem("SaiNoMichi/Build Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var config = EnsureConfig();

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Scenes")) AssetDatabase.CreateFolder("Assets/_Project", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0, 0, -10);
            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.15f);

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var game = new GameObject("GameController").AddComponent<GameController>();
            game.config = config;
            game.canvas = canvas;
            game.art = UpdateArtAsset();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[賽ノ道] シーンを作成しました: {ScenePath}");
        }

        /// <summary>
        /// Art フォルダの絵を名前で探して UIArt に入れる。すでに入っている絵は変えない（空いている欄だけ埋める）。
        /// 絵を足したら、このメニューを実行すればシーンを作り直さずに反映できる。
        /// </summary>
        [MenuItem("SaiNoMichi/Update Art")]
        public static UIArt UpdateArtAsset()
        {
            var art = AssetDatabase.LoadAssetAtPath<UIArt>(ArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<UIArt>();
                AssetDatabase.CreateAsset(art, ArtPath);
            }

            var sprites = new Dictionary<string, Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir }))
            {
                foreach (var sprite in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>())
                {
                    sprites[sprite.name] = sprite;
                }
            }
            Sprite Find(Sprite current, string name) => current != null ? current : (sprites.TryGetValue(name, out var s) ? s : null);

            art.mapBackground = Find(art.mapBackground, "bg_map");
            art.battleBackground = Find(art.battleBackground, "bg_battle");
            art.titleBackground = Find(art.titleBackground, "bg_title");
            // 第2層・第3層の背景（bg_map_2・bg_battle_2 など）
            if (art.layerMapBackgrounds == null || art.layerMapBackgrounds.Length < 3) art.layerMapBackgrounds = new Sprite[3];
            if (art.layerBattleBackgrounds == null || art.layerBattleBackgrounds.Length < 3) art.layerBattleBackgrounds = new Sprite[3];
            for (int i = 1; i < 3; i++)
            {
                // bg_map_2 でも bg_map2 でもよい
                art.layerMapBackgrounds[i] = Find(Find(art.layerMapBackgrounds[i], $"bg_map_{i + 1}"), $"bg_map{i + 1}");
                art.layerBattleBackgrounds[i] = Find(Find(art.layerBattleBackgrounds[i], $"bg_battle_{i + 1}"), $"bg_battle{i + 1}");
            }
            art.player = Find(art.player, "player");
            art.tileStart = Find(art.tileStart, "tile_start");
            art.tileEmpty = Find(art.tileEmpty, "tile_empty");
            art.tileBattle = Find(art.tileBattle, "tile_battle");
            art.tileRest = Find(art.tileRest, "tile_rest");
            art.tileBoss = Find(art.tileBoss, "tile_boss");
            art.tileEvent = Find(art.tileEvent, "tile_event");
            art.tileTrap = Find(art.tileTrap, "tile_trap");
            art.tileTreasure = Find(art.tileTreasure, "tile_treasure");
            art.tileShop = Find(art.tileShop, "tile_shop");
            art.tileForge = Find(art.tileForge, "tile_forge");
            art.tileElite = Find(art.tileElite, "tile_elite");
            art.tileShrine = Find(art.tileShrine, "tile_shrine");
            art.tileCheckpoint = Find(art.tileCheckpoint, "tile_checkpoint");
            art.tileTeahouse = Find(art.tileTeahouse, "tile_teahouse");
            art.tileDiceHall = Find(art.tileDiceHall, "tile_dicehall");
            art.faceBlank = Find(art.faceBlank, "face_blank");
            if (art.faces == null || art.faces.Length != 6) art.faces = new Sprite[6];
            for (int i = 0; i < 6; i++) art.faces[i] = Find(art.faces[i], $"face_{i + 1}");
            art.intentAttack = Find(art.intentAttack, "intent_attack");
            art.intentBlock = Find(art.intentBlock, "intent_block");
            art.intentBuff = Find(art.intentBuff, "intent_buff");
            art.intentMulti = Find(art.intentMulti, "intent_multi");
            art.intentDebuff = Find(art.intentDebuff, "intent_debuff");
            art.intentSeal = Find(art.intentSeal, "intent_seal");
            art.intentDice = Find(art.intentDice, "intent_dice");
            art.intentPoison = Find(art.intentPoison, "intent_poison");
            art.intentCharge = Find(art.intentCharge, "intent_charge");
            art.intentCurse = Find(art.intentCurse, "intent_curse");
            art.fxSlash = Find(art.fxSlash, "fx_slash");
            art.fxBlock = Find(art.fxBlock, "fx_block");
            art.fxHit = Find(art.fxHit, "fx_hit");

            foreach (var guid in AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/_Project/Data" }))
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(guid));
                int index = art.enemies.FindIndex(e => e.enemyId == enemy.id);
                var entry = index >= 0 ? art.enemies[index] : new UIArt.EnemySprite { enemyId = enemy.id };
                entry.sprite = Find(entry.sprite, "enemy_" + enemy.id);
                if (index >= 0) art.enemies[index] = entry;
                else art.enemies.Add(entry);
            }

            // 効果音：Audio/SE の se_dice_roll.mp3 などを名前で探す（すでに入っている音と音量は変えない）
            var clips = new Dictionary<string, AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // 初めて鳴らすときに遅れないよう、先に読み込んでおく設定にする
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    var settings = importer.defaultSampleSettings;
                    if (!settings.preloadAudioData || settings.loadType != AudioClipLoadType.DecompressOnLoad)
                    {
                        settings.preloadAudioData = true;
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        importer.defaultSampleSettings = settings;
                        importer.forceToMono = true;
                        importer.SaveAndReimport();
                    }
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null) clips[clip.name] = clip;
            }
            var missingSounds = new List<string>();
            foreach (SoundId id in System.Enum.GetValues(typeof(SoundId)))
            {
                string name = SoundFileName(id);
                var entry = art.sounds.Find(e => e.id == id);
                if (entry == null)
                {
                    // TODO(仕様): 音量は仮。ボタン・足音は小さめ
                    entry = new SoundEntry { id = id, volume = id == SoundId.Button || id == SoundId.Step ? 0.5f : 0.8f };
                    art.sounds.Add(entry);
                }
                // TODO(仕様): 足音のファイルは約9秒あるので、1歩ぶん（0.3秒）で切る。ファイルを短くしたら 0 に戻してよい
                if (id == SoundId.Step && entry.maxSeconds <= 0f) entry.maxSeconds = 0.3f;
                if (entry.clip == null && clips.TryGetValue(name, out var c)) entry.clip = c;
                if (entry.clip == null) missingSounds.Add(name);
            }

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            if (missingSounds.Count > 0) Debug.Log("[賽ノ道] まだない効果音: " + string.Join(", ", missingSounds));

            var missing = new List<string>();
            if (art.mapBackground == null) missing.Add("bg_map");
            if (art.battleBackground == null) missing.Add("bg_battle");
            if (art.titleBackground == null) missing.Add("bg_title");
            if (art.player == null) missing.Add("player");
            if (art.intentAttack == null) missing.Add("intent_attack");
            if (art.intentBlock == null) missing.Add("intent_block");
            if (art.intentBuff == null) missing.Add("intent_buff");
            if (art.intentMulti == null) missing.Add("intent_multi");
            if (art.intentDebuff == null) missing.Add("intent_debuff");
            if (art.intentSeal == null) missing.Add("intent_seal");
            if (art.intentDice == null) missing.Add("intent_dice");
            missing.AddRange(art.enemies.Where(e => e.sprite == null).Select(e => "enemy_" + e.enemyId));
            Debug.Log("[賽ノ道] UIArt を更新しました。" + (missing.Count > 0 ? "まだない絵: " + string.Join(", ", missing) : "すべての絵がそろっています。"));
            return art;
        }

        const string SoundDir = "Assets/_Project/Audio/SE";

        /// <summary>SoundId から効果音のファイル名を作る（DiceRoll → se_dice_roll）。</summary>
        static string SoundFileName(SoundId id)
        {
            var sb = new System.Text.StringBuilder("se");
            foreach (char ch in id.ToString())
            {
                if (char.IsUpper(ch)) sb.Append('_').Append(char.ToLowerInvariant(ch));
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static GameConfig EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<GameConfig>();
            // 初期ポーチ：普通の賽×2、四五六賽×1、一二三賽×1
            config.startingDice = new List<DiceData>
            {
                Load<DiceData>(DiceDir + "Dice_normal.asset"),
                Load<DiceData>(DiceDir + "Dice_normal.asset"),
                Load<DiceData>(DiceDir + "Dice_shigoroku.asset"),
                Load<DiceData>(DiceDir + "Dice_hifumi.asset"),
            };
            config.battleEnemies = new List<EnemyData>
            {
                Load<EnemyData>(EnemyDir + "Enemy_slime.asset"),
                Load<EnemyData>(EnemyDir + "Enemy_koni.asset"),
            };
            config.boss = Load<EnemyData>(EnemyDir + "Enemy_goal_guardian.asset");

            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"[賽ノ道] アセットが見つかりません: {path}");
            return asset;
        }
    }
}
