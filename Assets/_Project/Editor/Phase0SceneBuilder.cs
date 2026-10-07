using System.Collections.Generic;
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

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Phase0] シーンを作成しました: {ScenePath}");
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
