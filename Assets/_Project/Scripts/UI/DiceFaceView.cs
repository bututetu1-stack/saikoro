using System.Collections;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>ダイスの面1つの表示。1〜6 は目の絵、それ以外は無地の面に数字を重ねる。</summary>
    public class DiceFaceView : MonoBehaviour
    {
        Image image;
        TextMeshProUGUI number;
        UIArt art;

        public RectTransform Rect => (RectTransform)transform;

        public static DiceFaceView Create(string name, Transform parent, UIArt art, float size, Vector2 position)
        {
            var image = UIFactory.Picture(name, parent, art != null ? art.faceBlank : null, Vector2.one * size, position, new Color(0.95f, 0.9f, 0.78f));
            var view = image.gameObject.AddComponent<DiceFaceView>();
            view.image = image;
            view.art = art;
            view.number = UIFactory.Text("Number", image.transform, "", size * 0.55f, new Color(0.15f, 0.1f, 0.08f), Vector2.one * size, Vector2.zero);
            view.number.fontStyle = FontStyles.Bold;
            return view;
        }

        Image badgeBack;
        TextMeshProUGUI badgeText;

        /// <summary>面の数値と刻印（右上の小さな札）を表示する。</summary>
        public void SetFace(Face face)
        {
            SetValue(face.value);
            SetEngraving(face.engraving);
        }

        public void SetEngraving(Effects.EngravingData engraving)
        {
            if (engraving == null)
            {
                if (badgeBack != null) badgeBack.gameObject.SetActive(false);
                return;
            }
            float size = Rect.sizeDelta.x;
            if (badgeBack == null)
            {
                float b = Mathf.Max(18f, size * 0.46f);
                badgeBack = UIFactory.Panel("Badge", transform, Vector2.one * b, new Vector2(size * 0.36f, size * 0.36f), new Color(0.7f, 0.12f, 0.1f));
                badgeBack.raycastTarget = false;
                badgeText = UIFactory.Text("BadgeText", badgeBack.transform, "", b * 0.75f, Color.white, Vector2.one * b, Vector2.zero);
                badgeText.fontStyle = FontStyles.Bold;
            }
            badgeBack.gameObject.SetActive(true);
            badgeText.text = string.IsNullOrEmpty(engraving.badge) ? engraving.displayName.Substring(0, 1) : engraving.badge;
        }

        public void SetValue(int value)
        {
            var face = art != null ? art.FaceSprite(value) : null;
            if (face != null)
            {
                image.sprite = face;
                image.color = Color.white;
                number.text = "";
            }
            else
            {
                image.sprite = art != null ? art.faceBlank : null;
                image.color = image.sprite != null ? Color.white : new Color(0.95f, 0.9f, 0.78f);
                number.text = value.ToString();
            }
        }

        /// <summary>
        /// 転がる演出：そのダイスの面をばらばらに見せてから、最後に value で止めて弾ませる。
        /// 何が出るかはすでに決まっている（見た目だけの演出）。
        /// </summary>
        public IEnumerator PlayRoll(DiceInstance die, int value, float duration, Effects.EngravingData engraving = null)
        {
            Sfx.Play(SoundId.DiceRoll);
            float time = 0f;
            float interval = 0.05f;
            float next = 0f;
            while (time < duration)
            {
                if (time >= next)
                {
                    SetFace(die.faces[UIAnim.Cosmetic.Next(die.faces.Length)]);
                    transform.localRotation = Quaternion.Euler(0, 0, (float)(UIAnim.Cosmetic.NextDouble() * 40 - 20));
                    next += interval;
                    interval *= 1.12f; // だんだん遅くなる
                }
                yield return null;
                time += Time.unscaledDeltaTime;
            }
            transform.localRotation = Quaternion.identity;
            SetValue(value);
            Sfx.Play(SoundId.DiceLand);
            SetEngraving(engraving);
            yield return UIAnim.Punch(transform, 0.35f, 0.25f);
        }
    }
}
