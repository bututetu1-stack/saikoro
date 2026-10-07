using System.Collections;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>トレイに並べるダイス1個の札。名前・6面の絵・状態を表示する（マップと戦闘で共通）。</summary>
    public class DiceCard : MonoBehaviour
    {
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color CardColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color SelectedColor = new Color(1f, 0.78f, 0.3f);

        public DiceInstance Die { get; private set; }
        public Button Button { get; private set; }
        Image flash;

        public static DiceCard Create(string name, Transform parent, DiceInstance die, UIArt art, Vector2 size, Vector2 position,
            string stateLabel, bool dimmed, bool selected)
        {
            var button = UIFactory.Button(name, parent, size, position, selected ? SelectedColor : CardColor, "", 1, out var unusedLabel);
            Destroy(unusedLabel.gameObject);
            var card = button.gameObject.AddComponent<DiceCard>();
            card.Die = die;
            card.Button = button;

            // レア度で枠の色を変える（コモンは枠なし）
            var frame = RarityColor(die.data != null ? die.data.rarity : Rarity.Common);
            if (frame.HasValue)
            {
                var outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = frame.Value;
                outline.effectDistance = new Vector2(6, -6);
                var outline2 = button.gameObject.AddComponent<Outline>();
                outline2.effectColor = frame.Value;
                outline2.effectDistance = new Vector2(-6, 6);
            }

            var group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dimmed ? 0.45f : 1f;

            var title = UIFactory.Text("Name", button.transform, die.DisplayName, 30, InkColor, new Vector2(size.x - 20, 40), new Vector2(0, size.y / 2f - 26));
            title.fontStyle = FontStyles.Bold;
            string description = die.data != null ? die.data.description : null;
            if (!string.IsNullOrEmpty(description))
            {
                UIFactory.Text("Description", button.transform, description, 18, new Color(0.55f, 0.15f, 0.08f), new Vector2(size.x - 20, 24), new Vector2(0, size.y / 2f - 52));
            }

            const float face = 36f;
            float faceLeft = -(die.faces.Length * (face + 4) - 4) / 2f + face / 2f;
            for (int f = 0; f < die.faces.Length; f++)
            {
                DiceFaceView.Create($"Face{f}", button.transform, art, face, new Vector2(faceLeft + f * (face + 4), -10)).SetValue(die.faces[f].value);
            }
            UIFactory.Text("State", button.transform, stateLabel, 20, InkColor, new Vector2(size.x - 20, 28), new Vector2(0, -size.y / 2f + 20));

            card.flash = UIFactory.Panel("Flash", button.transform, size, Vector2.zero, new Color(1, 1, 1, 0));
            card.flash.raycastTarget = false;
            return card;
        }

        public static Color? RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return new Color(0.2f, 0.4f, 0.75f);   // 藍
                case Rarity.Rare: return new Color(0.85f, 0.65f, 0.15f);     // 金
                case Rarity.Curse: return new Color(0.3f, 0.15f, 0.35f);     // 紫がかった墨
                default: return null;
            }
        }

        /// <summary>一瞬明るく光らせる（リフレッシュの演出）。</summary>
        public IEnumerator PlayFlash()
        {
            StartCoroutine(UIAnim.Punch(transform, 0.12f, 0.3f));
            yield return UIAnim.Tween(0.5f, t => flash.color = new Color(1f, 0.95f, 0.6f, 0.9f * (1f - t)));
        }
    }
}
