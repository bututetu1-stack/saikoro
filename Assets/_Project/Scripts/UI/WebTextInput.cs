using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ブラウザ（WebGL）で日本語を打つための入力。Unity の WebGL の入力欄は日本語の変換（IME）が使えないので、
    /// Plugins/WebGL/TextInput.jslib で HTML の入力欄を画面に重ねて出し、決定した文字を受け取る。
    /// エディタや Windows では使わない（Unity の入力欄でそのまま日本語を打てる）。
    /// </summary>
    public class WebTextInput : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SaiNoMichi_OpenTextInput(string title, string value, int maxLength);
        [DllImport("__Internal")] static extern int SaiNoMichi_TextInputState();
        [DllImport("__Internal")] static extern string SaiNoMichi_TextInputValue();
#endif

        /// <summary>この入力を使うか（ブラウザで動いているとき）。</summary>
        public static bool Supported => Application.platform == RuntimePlatform.WebGLPlayer;

        static WebTextInput instance;
        Action<string> onDone;
        bool open;

        /// <summary>入力欄を出す。決定したら done に文字を渡す（やめたら呼ばない）。</summary>
        public static void Open(string title, string value, int maxLength, Action<string> done)
        {
            if (!Supported) return;
            if (instance == null)
            {
                var go = new GameObject("WebTextInput");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<WebTextInput>();
            }
            instance.onDone = done;
            instance.open = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 打っている文字がゲームの操作（P でポーチなど）にならないように、キーをゲームで受けない
            WebGLInput.captureAllKeyboardInput = false;
            SaiNoMichi_OpenTextInput(title, value ?? "", maxLength);
#endif
        }

        void Update()
        {
            if (!open) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            int state = SaiNoMichi_TextInputState();
            if (state == 0) return;
            open = false;
            WebGLInput.captureAllKeyboardInput = true;
            if (state == 1) onDone?.Invoke(SaiNoMichi_TextInputValue());
#else
            open = false;
#endif
            onDone = null;
        }
    }
}
