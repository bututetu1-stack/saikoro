using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// コルーチンで動かす簡単な演出。外部パッケージ（DOTween など）は使わない。
    /// 時間は Time.unscaledDeltaTime で進める。
    /// </summary>
    public static class UIAnim
    {
        // 見た目だけに使う乱数（ゲームの結果には影響しない）。ルール上 UnityEngine.Random は使わない。
        public static readonly System.Random Cosmetic = new System.Random();

        public static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float EaseInQuad(float t) => t * t;
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        /// <summary>0→1 の進み具合を duration 秒かけて step に渡す。</summary>
        public static IEnumerator Tween(float duration, Action<float> step, Func<float, float> ease = null)
        {
            float time = 0f;
            while (time < duration)
            {
                float t = Mathf.Clamp01(time / duration);
                step(ease != null ? ease(t) : t);
                yield return null;
                time += Time.unscaledDeltaTime;
            }
            step(1f);
        }

        public static IEnumerator Wait(float seconds)
        {
            float time = 0f;
            while (time < seconds)
            {
                yield return null;
                time += Time.unscaledDeltaTime;
            }
        }

        /// <summary>放物線を描いて跳ねながら移動する。</summary>
        public static IEnumerator Hop(RectTransform rt, Vector2 to, float height, float duration)
        {
            Vector2 from = rt.anchoredPosition;
            yield return Tween(duration, t =>
            {
                var p = Vector2.Lerp(from, to, t);
                p.y += height * 4f * t * (1f - t);
                rt.anchoredPosition = p;
            });
        }

        public static IEnumerator MoveTo(RectTransform rt, Vector2 to, float duration, Func<float, float> ease = null)
        {
            Vector2 from = rt.anchoredPosition;
            yield return Tween(duration, t => rt.anchoredPosition = Vector2.LerpUnclamped(from, to, t), ease ?? EaseOutQuad);
        }

        /// <summary>一瞬大きくして元に戻す。</summary>
        public static IEnumerator Punch(Transform tr, float amount, float duration)
        {
            Vector3 baseScale = tr.localScale;
            yield return Tween(duration, t => tr.localScale = baseScale * (1f + amount * Mathf.Sin(t * Mathf.PI)));
            tr.localScale = baseScale;
        }

        /// <summary>小刻みに揺らして元の位置に戻す。</summary>
        public static IEnumerator Shake(RectTransform rt, float magnitude, float duration)
        {
            Vector2 origin = rt.anchoredPosition;
            yield return Tween(duration, t =>
            {
                float m = magnitude * (1f - t);
                rt.anchoredPosition = origin + new Vector2((float)(Cosmetic.NextDouble() * 2 - 1) * m, (float)(Cosmetic.NextDouble() * 2 - 1) * m);
            });
            rt.anchoredPosition = origin;
        }

        /// <summary>色を flash に変えてから元の色に戻す。</summary>
        public static IEnumerator Flash(Graphic g, Color flash, float duration)
        {
            Color baseColor = g.color;
            yield return Tween(duration, t => g.color = Color.Lerp(flash, baseColor, t));
            g.color = baseColor;
        }

        public static IEnumerator Fade(CanvasGroup group, float to, float duration)
        {
            float from = group.alpha;
            yield return Tween(duration, t => group.alpha = Mathf.Lerp(from, to, t));
        }
    }
}
