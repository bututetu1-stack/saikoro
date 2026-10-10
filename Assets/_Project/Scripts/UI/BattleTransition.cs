using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// マップから戦闘に入るときの演出。主人公の頭に「！」→ 画面が光る → 帯が左右から閉じて暗転 →（戦闘画面を作る）→ 帯が開く。
    /// 強敵は紫、ボスは赤の帯にして、暗転中に相手の名前を出す。
    /// </summary>
    public class BattleTransition
    {
        public enum Kind { Normal, Elite, Boss }

        const int BandCount = 8;
        const float Width = 1920f, Height = 1080f;

        readonly RectTransform root;
        readonly RectTransform[] bands = new RectTransform[BandCount];
        readonly Image flash;
        readonly TextMeshProUGUI nameText;
        readonly Kind kind;

        BattleTransition(Transform canvas, Kind kind, string enemyName)
        {
            this.kind = kind;
            root = UIFactory.Stretch("BattleTransition", canvas);
            root.SetAsLastSibling();
            // 演出の間は、後ろをクリックできないようにする
            var blocker = root.gameObject.AddComponent<Image>();
            blocker.color = new Color(0, 0, 0, 0);

            Color bandColor = kind == Kind.Boss ? new Color(0.35f, 0.03f, 0.03f) : kind == Kind.Elite ? new Color(0.2f, 0.07f, 0.28f) : new Color(0.06f, 0.04f, 0.03f);
            float bandHeight = Height / BandCount;
            for (int i = 0; i < BandCount; i++)
            {
                var band = UIFactory.Panel($"Band{i}", root, new Vector2(Width + 4f, bandHeight + 2f), Vector2.zero, bandColor);
                band.raycastTarget = false;
                bands[i] = band.rectTransform;
                bands[i].anchoredPosition = new Vector2(Offscreen(i), Height / 2f - bandHeight * (i + 0.5f));
            }

            flash = UIFactory.Panel("Flash", root, new Vector2(Width, Height), Vector2.zero, new Color(1f, 1f, 1f, 0f));
            flash.raycastTarget = false;

            string label = kind == Kind.Boss ? "<size=60%>ボス</size>\n" : kind == Kind.Elite ? "<size=60%>強敵</size>\n" : "";
            nameText = UIFactory.Text("Name", root, label + enemyName, 120, new Color(1f, 0.85f, 0.4f), new Vector2(1600, 260), Vector2.zero);
            nameText.fontStyle = FontStyles.Bold;
            nameText.outlineWidth = 0.2f;
            nameText.outlineColor = new Color32(20, 10, 5, 255);
            nameText.alpha = 0f;
        }

        public static BattleTransition Create(Transform canvas, Kind kind, string enemyName) => new BattleTransition(canvas, kind, enemyName);

        // 偶数の帯は左から、奇数の帯は右から入ってくる
        static float Offscreen(int i) => (i % 2 == 0 ? -1f : 1f) * (Width + 40f);

        /// <summary>閉じる（暗転まで）。mark は「！」を出す相手（マップの主人公）。null なら出さない。</summary>
        public IEnumerator Close(RectTransform mark)
        {
            // 「！」
            TextMeshProUGUI bang = null;
            if (mark != null)
            {
                bang = UIFactory.Text("Bang", mark, "！", 150, kind == Kind.Normal ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.35f, 0.25f),
                    new Vector2(180, 180), new Vector2(0, 130));
                bang.fontStyle = FontStyles.Bold;
                bang.outlineWidth = 0.25f;
                bang.outlineColor = new Color32(30, 15, 5, 255);
                Sfx.Play(SoundId.Charge);
                yield return UIAnim.Punch(bang.transform, 0.5f, 0.3f);
                yield return UIAnim.Wait(0.15f);
            }

            // 画面が2回光る
            for (int k = 0; k < 2; k++)
            {
                yield return UIAnim.Tween(0.08f, t => flash.color = new Color(1f, 1f, 1f, 0.75f * t));
                yield return UIAnim.Tween(0.1f, t => flash.color = new Color(1f, 1f, 1f, 0.75f * (1f - t)));
            }

            // 帯が少しずつずれて閉じる
            Sfx.Play(SoundId.EnemyAttack);
            float stagger = 0.035f, slide = 0.22f;
            yield return UIAnim.Tween(slide + stagger * (BandCount - 1), t =>
            {
                float time = t * (slide + stagger * (BandCount - 1));
                for (int i = 0; i < BandCount; i++)
                {
                    float p = Mathf.Clamp01((time - stagger * i) / slide);
                    var pos = bands[i].anchoredPosition;
                    bands[i].anchoredPosition = new Vector2(Mathf.Lerp(Offscreen(i), 0f, UIAnim.EaseOutQuad(p)), pos.y);
                }
            });

            if (bang != null) Object.Destroy(bang.gameObject); // マップに戻ったときに残らないように

            // 暗転中に相手の名前
            yield return UIAnim.Tween(0.15f, t => nameText.alpha = t);
            yield return UIAnim.Wait(kind == Kind.Normal ? 0.25f : 0.5f);
        }

        /// <summary>開く（戦闘画面を作ったあと）。終わったら演出を消す。</summary>
        public IEnumerator Open()
        {
            root.SetAsLastSibling(); // あとから作った戦闘画面より手前に
            yield return UIAnim.Tween(0.12f, t => nameText.alpha = 1f - t);
            float stagger = 0.03f, slide = 0.2f;
            yield return UIAnim.Tween(slide + stagger * (BandCount - 1), t =>
            {
                float time = t * (slide + stagger * (BandCount - 1));
                for (int i = 0; i < BandCount; i++)
                {
                    float p = Mathf.Clamp01((time - stagger * i) / slide);
                    var pos = bands[i].anchoredPosition;
                    // 入ってきたのと反対側へ抜ける
                    bands[i].anchoredPosition = new Vector2(Mathf.Lerp(0f, -Offscreen(i), p * p), pos.y);
                }
            });
            Object.Destroy(root.gameObject);
        }
    }
}
