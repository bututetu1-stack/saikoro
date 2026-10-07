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
    public static class Phase0SceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Phase0.unity";
        const string ConfigPath = "Assets/_Project/Data/Phase0Config.asset";
        const string DiceDir = "Assets/_Project/Data/Dice/";
        const string EnemyDir = "Assets/_Project/Data/Enemies/";
        const string ArtPath = "Assets/_Project/Data/UIArt.asset";
        const string ArtDir = "Assets/_Project/Art";

        [MenuItem("SaiNoMichi/Phase0/Build Scene")]
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

            var game = new GameObject("Phase0Game").AddComponent<Phase0Game>();
            game.config = config;
            game.canvas = canvas;
            game.art = UpdateArtAsset();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Phase0] シーンを作成しました: {ScenePath}");
        }

        /// <summary>
        /// Art フォルダの絵を名前で探して UIArt に入れる。すでに入っている絵は変えない（空いている欄だけ埋める）。
        /// 絵を足したら、このメニューを実行すればシーンを作り直さずに反映できる。
        /// </summary>
        [MenuItem("SaiNoMichi/Phase0/Update Art")]
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
            art.player = Find(art.player, "player");
            art.tileStart = Find(art.tileStart, "tile_start");
            art.tileEmpty = Find(art.tileEmpty, "tile_empty");
            art.tileBattle = Find(art.tileBattle, "tile_battle");
            art.tileRest = Find(art.tileRest, "tile_rest");
            art.tileBoss = Find(art.tileBoss, "tile_boss");
            art.faceBlank = Find(art.faceBlank, "face_blank");
            if (art.faces == null || art.faces.Length != 6) art.faces = new Sprite[6];
            for (int i = 0; i < 6; i++) art.faces[i] = Find(art.faces[i], $"face_{i + 1}");
            art.intentAttack = Find(art.intentAttack, "intent_attack");
            art.intentBlock = Find(art.intentBlock, "intent_block");
            art.intentBuff = Find(art.intentBuff, "intent_buff");

            foreach (var guid in AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/_Project/Data" }))
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(guid));
                int index = art.enemies.FindIndex(e => e.enemyId == enemy.id);
                var entry = index >= 0 ? art.enemies[index] : new UIArt.EnemySprite { enemyId = enemy.id };
                entry.sprite = Find(entry.sprite, "enemy_" + enemy.id);
                if (index >= 0) art.enemies[index] = entry;
                else art.enemies.Add(entry);
            }

            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();

            var missing = new List<string>();
            if (art.mapBackground == null) missing.Add("bg_map");
            if (art.battleBackground == null) missing.Add("bg_battle");
            if (art.player == null) missing.Add("player");
            if (art.intentAttack == null) missing.Add("intent_attack");
            if (art.intentBlock == null) missing.Add("intent_block");
            if (art.intentBuff == null) missing.Add("intent_buff");
            missing.AddRange(art.enemies.Where(e => e.sprite == null).Select(e => "enemy_" + e.enemyId));
            Debug.Log("[Phase0] UIArt を更新しました。" + (missing.Count > 0 ? "まだない絵: " + string.Join(", ", missing) : "すべての絵がそろっています。"));
            return art;
        }

        static Phase0Config EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<Phase0Config>(ConfigPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<Phase0Config>();
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
            if (asset == null) Debug.LogError($"[Phase0] アセットが見つかりません: {path}");
            return asset;
        }
    }
}
