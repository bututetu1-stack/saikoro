using System.Collections;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>BGM の場面（場面ごとに UIArt の曲を流す）。</summary>
    public enum BgmScene
    {
        None,
        Title,
        Map,
        Battle,
        Boss,
        Reward,   // 戦闘に勝って報酬を選ぶ画面（ボスレリックを選ぶ画面も）
    }

    /// <summary>
    /// BGM を流す。曲は1つだけをくり返し、場面が変わったらフェードで入れ替える（同じ曲なら流し直さない）。
    /// 曲が入っていない場面は無音にする（ファイルがそろっていなくても動く）。音量は設定の「BGM の音量」。
    /// </summary>
    public static class Bgm
    {
        const float FadeSeconds = 0.8f;

        static UIArt art;
        static AudioSource[] sources;
        static int current;           // いま鳴らしている方（0 か 1）
        static AudioClip playing;
        static MonoBehaviour host;
        static Coroutine fading;

        /// <summary>全体の音量（0〜1）。設定から入る。</summary>
        public static float Volume { get; private set; } = 0.6f;

        public static void Init(UIArt uiArt, MonoBehaviour coroutineHost)
        {
            art = uiArt;
            host = coroutineHost;
            // 部品が残っていればそのまま使う。プレイを止めると部品は消えるが、static の値は次のプレイまで残る
            // （ドメインの再読み込みを省く設定のとき）ので、消えていたら作り直す
            if (sources != null && sources[0] != null && sources[1] != null) return;
            current = 0;
            playing = null;
            fading = null;
            var go = new GameObject("Bgm");
            Object.DontDestroyOnLoad(go);
            sources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                sources[i] = go.AddComponent<AudioSource>();
                sources[i].loop = true;
                sources[i].playOnAwake = false;
                sources[i].volume = 0f;
            }
        }

        public static void SetVolume(float volume)
        {
            Volume = Mathf.Clamp01(volume);
            if (sources != null && fading == null && sources[current] != null) sources[current].volume = Volume;
        }

        /// <summary>場面の曲を流す（第2層・第3層の曲があれば、マップと戦闘はそちら）。</summary>
        public static void Play(BgmScene scene, int layer = 0) => Play(art != null ? art.BgmFor(scene, layer) : null);

        public static void Play(AudioClip clip)
        {
            if (sources == null || sources[0] == null || host == null || clip == playing) return;
            playing = clip;
            if (fading != null) host.StopCoroutine(fading);
            fading = host.StartCoroutine(CrossFade(clip));
        }

        public static void Stop() => Play((AudioClip)null);

        static IEnumerator CrossFade(AudioClip clip)
        {
            var from = sources[current];
            int next = 1 - current;
            var to = sources[next];
            float fromStart = from.volume;
            if (clip != null)
            {
                to.clip = clip;
                to.volume = 0f;
                to.Play();
            }
            // 演出の速さの設定とは関係なく、本当の時間でフェードする
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = t / FadeSeconds;
                from.volume = fromStart * (1f - k);
                if (clip != null) to.volume = Volume * k;
                yield return null;
            }
            from.Stop();
            from.volume = 0f;
            if (clip != null) to.volume = Volume;
            current = next;
            fading = null;
        }
    }
}
