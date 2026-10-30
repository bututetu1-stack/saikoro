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

        /// <summary>演出の速さの倍率（設定の「演出の速さ」。1 が普通）。待ち時間と動きのすべてにかかる。</summary>
        public static float Speed { get => baseSpeed * Boost; set => baseSpeed = value; }
        static float baseSpeed = 1f;

        /// <summary>早送りの倍率（演出中にクリックすると上がり、操作できるようになったら 1 に戻る）。</summary>
        public static float Boost { get; set; } = 1f;

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
                time += Time.unscaledDeltaTime * Speed;
            }
            step(1f);
        }

        public static IEnumerator Wait(float seconds)
        {
            float time = 0f;
            while (time < seconds)
            {
                yield return null;
                time += Time.unscaledDeltaTime * Speed;
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

        /// <summary>一瞬大きくして元に戻す。途中で tr が消えたら（札の作り直しなど）そこで止める。</summary>
        public static IEnumerator Punch(Transform tr, float amount, float duration)
        {
            if (tr == null) yield break;
            Vector3 baseScale = tr.localScale;
            float time = 0f;
            while (time < duration)
            {
                if (tr == null) yield break;
                tr.localScale = baseScale * (1f + amount * Mathf.Sin(Mathf.Clamp01(time / duration) * Mathf.PI));
                yield return null;
                time += Time.unscaledDeltaTime * Speed;
            }
            if (tr != null) tr.localScale = baseScale;
        }

        /// <summary>画面の揺れの強さ（設定の「画面の揺れ」。0 で揺れない）。</summary>
        public static float ShakeScale { get; set; } = 1f;

        /// <summary>小刻みに揺らして元の位置に戻す。</summary>
        public static IEnumerator Shake(RectTransform rt, float magnitude, float duration)
        {
            Vector2 origin = rt.anchoredPosition;
            magnitude *= ShakeScale;
            if (magnitude <= 0f) yield break;
            yield return Tween(duration, t =>
            {
                if (rt == null) return; // 揺らしている途中で画面が消えた
                float m = magnitude * (1f - t);
                rt.anchoredPosition = origin + new Vector2((float)(Cosmetic.NextDouble() * 2 - 1) * m, (float)(Cosmetic.NextDouble() * 2 - 1) * m);
            });
            if (rt != null) rt.anchoredPosition = origin;
        }

        /// <summary>色を flash に変えてから元の色に戻す。</summary>
        public static IEnumerator Flash(Graphic g, Color flash, float duration)
        {
            Color baseColor = g.color;
            yield return Tween(duration, t => g.color = Color.Lerp(flash, baseColor, t));
            g.color = baseColor;
        }

        /// <summary>画面を出すとき、透明からさっと浮かび上がらせる（少し下から上がってくる）。</summary>
        public static void FadeIn(MonoBehaviour view, float duration = 0.2f)
        {
            if (view == null || !view.isActiveAndEnabled) return;
            var group = view.GetComponent<CanvasGroup>();
            if (group == null) group = view.gameObject.AddComponent<CanvasGroup>();
            var rt = view.transform as RectTransform;
            // 浮かび上がりの途中で画面が隠されると（すぐ戦闘が始まった・層を移ったなど）、演出が止まって透明のまま残り、
            // 次に見せたとき真っ暗になっていた。隠されたら、見えている状態に戻す
            var guard = view.GetComponent<FadeInGuard>();
            if (guard == null) guard = view.gameObject.AddComponent<FadeInGuard>();
            if (!guard.fading) guard.home = rt != null ? rt.anchoredPosition : Vector2.zero; // 2回続けて呼ばれたら、最初の位置のまま
            guard.group = group;
            guard.rt = rt;
            guard.fading = true;
            view.StartCoroutine(FadeInRoutine(group, rt, duration, guard));
        }

        static IEnumerator FadeInRoutine(CanvasGroup group, RectTransform rt, float duration, FadeInGuard guard)
        {
            Vector2 home = guard.home;
            group.alpha = 0f;
            yield return Tween(duration, t =>
            {
                group.alpha = t;
                if (rt != null) rt.anchoredPosition = home + new Vector2(0f, -12f * (1f - t));
            }, EaseOutQuad);
            guard.Finish();
        }

        /// <summary>浮かび上がりの途中で隠されたら、見えている状態（不透明・元の位置）に戻す。</summary>
        public class FadeInGuard : MonoBehaviour
        {
            public CanvasGroup group;
            public RectTransform rt;
            public Vector2 home;
            public bool fading;

            public void Finish()
            {
                fading = false;
                if (group != null) group.alpha = 1f;
                if (rt != null) rt.anchoredPosition = home;
            }

            /// <summary>隠されたとき（OnDisable）：浮かび上がりの途中なら、見えている状態に戻す。</summary>
            public void RestoreIfFading()
            {
                if (fading) Finish();
            }

            void OnDisable() => RestoreIfFading();
        }

        public static IEnumerator Fade(CanvasGroup group, float to, float duration)
        {
            float from = group.alpha;
            yield return Tween(duration, t => group.alpha = Mathf.Lerp(from, to, t));
        }
    }
}
