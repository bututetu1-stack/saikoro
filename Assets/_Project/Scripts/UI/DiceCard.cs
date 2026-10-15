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

        public DiceInstance Die { get; private set; }
        public Button Button { get; private set; }
        Image flash;

        public static DiceCard Create(string name, Transform parent, DiceInstance die, UIArt art, Vector2 size, Vector2 position,
            string stateLabel, bool dimmed, bool selected, int mirrorValue = -1)
        {
            // レア度で札の地の色を変える。選択中は内側に朱の枠
            var button = UIFactory.Button(name, parent, size, position, CardColorFor(die.data != null ? die.data.rarity : Rarity.Common), "", 1, out var unusedLabel);
            Destroy(unusedLabel.gameObject);
            // 和紙の札の絵があれば使う（レア度の色で染める）
            if (UISkin.Art != null && UISkin.Art.uiCard != null) UISkin.ApplyRounded((Image)button.targetGraphic, UISkin.Art.uiCard, size);
            var card = button.gameObject.AddComponent<DiceCard>();
            card.Die = die;
            card.Button = button;
            if (selected) AddSelectedFrame(button.transform, size);

            var group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = dimmed ? 0.45f : 1f;

            // 札が狭いとき（ダイスが多いとき）は文字を小さくする
            bool narrow = size.x < 200f;
            // 名前の帯（札の色を少し濃くした角丸の帯。名前が札の中で見出しらしく見えるように）
            var cardColor = CardColorFor(die.data != null ? die.data.rarity : Rarity.Common);
            var band = UIFactory.Panel("NameBand", button.transform, new Vector2(size.x - 12, 40), new Vector2(0, size.y / 2f - 26),
                new Color(cardColor.r * 0.86f, cardColor.g * 0.84f, cardColor.b * 0.8f, 1f));
            band.raycastTarget = false;
            if (UISkin.Art != null && UISkin.Art.uiCard != null) band.gameObject.SetActive(false); // 和紙の札の絵には縁取りがあるので、帯はいらない
            var title = UIFactory.Text("Name", button.transform, die.DisplayName, narrow ? 24 : 30, InkColor, new Vector2(size.x - 12, 40), new Vector2(0, size.y / 2f - 26));
            title.fontStyle = FontStyles.Bold;
            // 説明は札には書かず、マウスを乗せたときの吹き出しで読む（札の小さい字が読みにくかった）
            var hover = button.gameObject.AddComponent<HoverRelay>();
            hover.Entered += () => DiceTooltip.Show(card);
            hover.Exited += DiceTooltip.Hide;

            if (die.data != null && die.data.mirror)
            {
                // 鏡賽：面の数字に意味がないので、面の代わりに説明を出す
                if (mirrorValue >= 0)
                {
                    // 次に出る目（直前の出目）を見せる
                    UIFactory.Text("Mirror", button.transform, "次の出目", 20, InkColor, new Vector2(110, 36), new Vector2(-34, -10));
                    DiceFaceView.Create("MirrorFace", button.transform, art, 40, new Vector2(40, -10)).SetValue(mirrorValue);
                }
                else
                {
                    UIFactory.Text("Mirror", button.transform, "直前の出目を写す", 24, InkColor, new Vector2(size.x - 20, 36), new Vector2(0, -10));
                }
            }
            else
            {
                // 札が狭いとき（ダイスが多いとき）は目を小さくして収める
                // 説明を札から外したぶん、目を少し大きく
                float face = Mathf.Min(40f, (size.x - 44f - (die.faces.Length - 1) * 4f) / die.faces.Length);
                float faceLeft = -(die.faces.Length * (face + 4) - 4) / 2f + face / 2f;
                for (int f = 0; f < die.faces.Length; f++)
                {
                    DiceFaceView.Create($"Face{f}", button.transform, art, face, new Vector2(faceLeft + f * (face + 4), -4)).SetFace(die.faces[f]);
                }
            }
            UIFactory.Text("State", button.transform, stateLabel, narrow ? 16 : 20, InkColor, new Vector2(size.x - 40, 28), new Vector2(0, -size.y / 2f + 28)); // 札の縁の線に重ならないよう、少し内側

            card.flash = UIFactory.Panel("Flash", button.transform, size, Vector2.zero, new Color(1, 1, 1, 0));
            card.flash.raycastTarget = false;
            return card;
        }

        // 札が消えたり隠れたりしたら、吹き出しも消す（クリックで画面が変わったときに残らないように）
        void OnDisable() => DiceTooltip.HideFor(this);


        /// <summary>キーボードで選ぶときの番号（1〜9）を札の左上に出す。</summary>
        public void SetHotkey(int number)
        {
            var size = ((RectTransform)transform).sizeDelta;
            var back = UIFactory.Panel("Hotkey", transform, new Vector2(30, 30), new Vector2(-size.x / 2f + 46, size.y / 2f - 28), new Color(0.18f, 0.12f, 0.08f, 0.85f)); // 角の飾りに重ならないよう、少し内側
            back.raycastTarget = false;
            UIFactory.Text("Label", back.transform, number.ToString(), 20, new Color(0.96f, 0.92f, 0.82f), new Vector2(30, 30), Vector2.zero).raycastTarget = false;
        }
        /// <summary>
        /// レア度ごとの札の地の色（コモンは和紙の色）。前は縁取りで見せていたが、横スクロールの枠で見切れて不格好だったので、
        /// 地の色で見せる（開発者の要望）。どれも淡い色なので、墨色の文字は読める。
        /// </summary>
        public static Color CardColorFor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Uncommon: return new Color(0.76f, 0.85f, 0.97f);  // 淡い藍
                case Rarity.Rare: return new Color(1f, 0.85f, 0.5f);           // 淡い金
                case Rarity.Curse: return new Color(0.78f, 0.7f, 0.82f);      // 淡い紫
                default: return CardColor;
            }
        }

        /// <summary>選択中の印：札の内側に太い朱の枠（外に出さないので、スクロールの枠で見切れない）。</summary>
        public static void AddSelectedFrame(Transform card, Vector2 size)
        {
            // 札と同じ角丸の、太い朱の縁（中は透ける）
            var frame = UIFactory.Panel("SelectedFrame", card, size, Vector2.zero, new Color(0.85f, 0.25f, 0.1f));
            UISkin.ApplyRounded(frame, UISkin.Outline, size);
            frame.raycastTarget = false;
        }

        /// <summary>一瞬明るく光らせる（リフレッシュの演出）。</summary>
        public IEnumerator PlayFlash()
        {
            StartCoroutine(UIAnim.Punch(transform, 0.12f, 0.3f));
            yield return UIAnim.Tween(0.5f, t => flash.color = new Color(1f, 0.95f, 0.6f, 0.9f * (1f - t)));
        }
    }
}
