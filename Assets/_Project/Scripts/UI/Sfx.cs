using System;
using System.Collections.Generic;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>効果音の種類。ファイル名は se_ ＋ 小文字のスネークケース（DiceRoll → se_dice_roll）。</summary>
    public enum SoundId
    {
        DiceRoll,
        DiceLand,
        Step,
        Hit,
        Block,
        Poison,
        Damage,
        Refresh,
        Victory,
        Defeat,
        Gold,
        Relic,
        Trap,
        Heal,
        Buy,
        Button,
    }

    /// <summary>効果音1つ分の設定（UIArt に並べる）。</summary>
    [Serializable]
    public class SoundEntry
    {
        public SoundId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("0 より大きければ、この秒数で音を止める（長すぎる音を切る。足音など）")]
        public float maxSeconds;
    }

    /// <summary>
    /// 効果音を鳴らす。音が登録されていなければ何もしない（ファイルがそろっていなくても動く）。
    /// 同じ音が一瞬に何度も重なると割れるので、短い間隔の連打は1回にまとめる。
    /// </summary>
    public static class Sfx
    {
        const float MinInterval = 0.05f;

        static AudioSource source;
        static readonly Dictionary<SoundId, SoundEntry> entries = new Dictionary<SoundId, SoundEntry>();
        static readonly Dictionary<SoundId, float> lastPlayed = new Dictionary<SoundId, float>();

        /// <summary>全体の音量（0〜1）。</summary>
        public static float MasterVolume { get; set; } = 1f;

        public static void Init(UIArt art)
        {
            entries.Clear();
            lastPlayed.Clear();
            if (art != null && art.sounds != null)
            {
                foreach (var e in art.sounds)
                {
                    if (e != null && e.clip != null) entries[e.id] = e;
                }
            }
            if (source == null)
            {
                var go = new GameObject("Sfx");
                UnityEngine.Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                // シーンに音を聞く役（AudioListener）がなければ、ここに置く
                if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            }
        }

        public static void Play(SoundId id)
        {
            if (source == null || !entries.TryGetValue(id, out var e)) return;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out float last) && now - last < MinInterval) return;
            lastPlayed[id] = now;
            if (e.maxSeconds > 0f && e.clip.length > e.maxSeconds)
            {
                // 長い音は専用の AudioSource で鳴らし、決めた秒数で止める（短く消えていくように音量も絞る）
                var cut = CutSource(id);
                cut.Stop();
                cut.clip = e.clip;
                cut.volume = e.volume * MasterVolume;
                cut.Play();
                cut.SetScheduledEndTime(AudioSettings.dspTime + e.maxSeconds);
                return;
            }
            source.PlayOneShot(e.clip, e.volume * MasterVolume);
        }

        static readonly Dictionary<SoundId, AudioSource> cutSources = new Dictionary<SoundId, AudioSource>();

        static AudioSource CutSource(SoundId id)
        {
            if (cutSources.TryGetValue(id, out var s) && s != null) return s;
            s = source.gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            cutSources[id] = s;
            return s;
        }

        /// <summary>鳴っている音をすべて止める（画面が切り替わるとき）。</summary>
        public static void StopAll()
        {
            if (source != null) source.Stop();
            foreach (var s in cutSources.Values)
            {
                if (s != null) s.Stop();
            }
        }
    }
}
