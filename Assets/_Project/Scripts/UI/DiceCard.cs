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
        /// <summary>マウスを乗せたときに吹き出しを出すか（マップでは盤面が隠れるので出さず、下のメッセージ欄に書く）。</summary>
        public bool TooltipEnabled { get; set; } = true;
        Image flash;

        public static DiceCard Create(string name, Transform parent, DiceInstance die, UIArt art, Vector2 size, Vector2 position,
            string stateLabel, bool dimmed, bool selected, int lastRolled = int.MinValue)
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
            bool sealedDie = die.state == DiceState.Sealed;
            // 封印中は使用済みほど薄くせず、上にお札と鎖を重ねて見分けられるようにする
            // 使用済みは、薄くするだけだと後ろの背景（洞窟や金の雲）が透けて見づらかったので、少しだけ薄くして暗い幕を重ねる
            group.alpha = sealedDie ? 0.92f : dimmed ? 0.8f : 1f;

            // 札が狭いとき（ダイスが多いとき）は文字を小さくする
            bool narrow = size.x < 200f;
            // 名前の帯（札の色を少し濃くした角丸の帯。名前が札の中で見出しらしく見えるように）
            var cardColor = CardColorFor(die.data != null ? die.data.rarity : Rarity.Common);
            // 札の縁取りの上の部分に重ならないよう、上端から 34 下げる（2026-10-16 開発者の要望。前は 26）
            var band = UIFactory.Panel("NameBand", button.transform, new Vector2(size.x - 12, 40), new Vector2(0, size.y / 2f - 34),
                new Color(cardColor.r * 0.86f, cardColor.g * 0.84f, cardColor.b * 0.8f, 1f));
            band.raycastTarget = false;
            if (UISkin.Art != null && UISkin.Art.uiCard != null) band.gameObject.SetActive(false); // 和紙の札の絵には縁取りがあるので、帯はいらない
            var title = UIFactory.Text("Name", button.transform, die.DisplayName, narrow ? 24 : 30, InkColor, new Vector2(size.x - 12, 40), new Vector2(0, size.y / 2f - 34));
            title.fontStyle = FontStyles.Bold;
            // 説明は札には書かず、マウスを乗せたときの吹き出しで読む（札の小さい字が読みにくかった）
            var hover = button.gameObject.AddComponent<HoverRelay>();
            hover.Entered += () => { if (card.TooltipEnabled) DiceTooltip.Show(card); };
            hover.Exited += DiceTooltip.Hide;

            if (die.data != null && die.data.mirror)
            {
                // 鏡賽・梯子賽：面の数字に意味がないので、面の代わりに次の出目か説明を出す
                bool ladder = die.data.mirrorStep != 0;
                int mirrorValue = lastRolled != int.MinValue ? DiceRoller.MirrorValue(die, lastRolled) : -1;
                if (mirrorValue >= 0)
                {
                    // 次に出る目（直前の出目）を見せる
                    UIFactory.Text("Mirror", button.transform, "次の出目", 20, InkColor, new Vector2(110, 36), new Vector2(-34, -10));
                    DiceFaceView.Create("MirrorFace", button.transform, art, 40, new Vector2(40, -10)).SetValue(mirrorValue);
                }
                else
                {
                    UIFactory.Text("Mirror", button.transform, ladder ? "直前の出目 +1" : "直前の出目を写す", 24, InkColor, new Vector2(size.x - 20, 36), new Vector2(0, -10));
                }
            }
            else if (die.faces.Length > 8)
            {
                // 十二面・二十面は目を並べると小さすぎるので、面の数と幅を字で出す
                UIFactory.Text("Faces", button.transform, FaceRangeLabel(die), 28, InkColor, new Vector2(size.x - 30, 44), new Vector2(0, -4)).fontStyle = FontStyles.Bold;
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
            // 札の縁の線・角の飾りに重ならないよう、内側。長い文は折り返さずに字を小さくする（2行になると面の数字に重なっていた）
            var stateText = UIFactory.Text("State", button.transform, stateLabel, narrow ? 18 : 22, InkColor, new Vector2(size.x - 56, 30), new Vector2(0, -size.y / 2f + 30));
            stateText.textWrappingMode = TextWrappingModes.NoWrap;
            stateText.enableAutoSizing = true;
            stateText.fontSizeMax = narrow ? 18 : 22;
            stateText.fontSizeMin = 12;

            if (sealedDie) AddSealMark(button.transform, size);
            else if (dimmed) UIFactory.Panel("UsedVeil", button.transform, size, Vector2.zero, UsedVeilColor).raycastTarget = false;

            card.flash = UIFactory.Panel("Flash", button.transform, size, Vector2.zero, new Color(1, 1, 1, 0));
            card.flash.raycastTarget = false;
            return card;
        }

        /// <summary>面が多いダイス（十二面・二十面）の札に出す字（例：「二十面 1〜20」）。</summary>
        public static string FaceRangeLabel(DiceInstance die)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var f in die.faces) { if (f.value < min) min = f.value; if (f.value > max) max = f.value; }
            return $"{FaceCountName(die.faces.Length)}　{min}〜{max}";
        }

        /// <summary>面の数の呼び名（四面・八面・十二面・二十面など）。</summary>
        public static string FaceCountName(int count)
        {
            switch (count)
            {
                case 2: return "二面";
                case 4: return "四面";
                case 6: return "六面";
                case 8: return "八面";
                case 10: return "十面";
                case 12: return "十二面";
                case 20: return "二十面";
                default: return count + "面";
            }
        }

        static readonly Color UsedVeilColor = new Color(0.1f, 0.08f, 0.06f, 0.5f);
        static readonly Color SealVeilColor = new Color(0.32f, 0.1f, 0.45f, 0.38f);
        static readonly Color ChainColor = new Color(0.22f, 0.2f, 0.26f, 0.9f);
        static readonly Color OfudaColor = new Color(0.98f, 0.94f, 0.8f, 1f);
        static readonly Color SealRed = new Color(0.78f, 0.1f, 0.08f, 1f);

        /// <summary>
        /// 封印中の印：紫の陰り・十字にかかった鎖・「封」のお札を札の上に重ねる（使用済みの薄い札と見分けられるように）。
        /// どれもマウスを遮らない（乗せれば説明が出る）。
        /// </summary>
        public static void AddSealMark(Transform card, Vector2 size)
        {
            var root = UIFactory.Rect("Seal", card, size, Vector2.zero);
            var veil = UIFactory.Panel("Veil", root, size, Vector2.zero, SealVeilColor);
            veil.raycastTarget = false;
            // 鎖：札を斜めに横切る2本
            float chainLength = Mathf.Sqrt(size.x * size.x + size.y * size.y) * 0.92f;
            float angle = Mathf.Atan2(size.y, size.x) * Mathf.Rad2Deg * 0.75f;
            foreach (float a in new[] { angle, -angle })
            {
                var chain = UIFactory.Panel("Chain", root, new Vector2(chainLength, 9f), Vector2.zero, ChainColor);
                chain.raycastTarget = false;
                chain.transform.localRotation = Quaternion.Euler(0, 0, a);
                // 鎖の輪に見えるよう、明るい点を等間隔に
                int links = Mathf.Max(4, Mathf.RoundToInt(chainLength / 22f));
                for (int i = 0; i < links; i++)
                {
                    float x = -chainLength / 2f + (i + 0.5f) * chainLength / links;
                    var link = UIFactory.Panel("Link", chain.transform, new Vector2(12f, 13f), new Vector2(x, 0), new Color(0.55f, 0.53f, 0.6f, 1f));
                    link.raycastTarget = false;
                }
            }
            // お札：まん中に少し傾けて貼る。朱の縁と「封」の字
            float ofudaHeight = size.y * 0.86f, ofudaWidth = Mathf.Clamp(size.x * 0.2f, 40f, 66f);
            var ofuda = UIFactory.Panel("Ofuda", root, new Vector2(ofudaWidth, ofudaHeight), Vector2.zero, OfudaColor);
            ofuda.raycastTarget = false;
            ofuda.transform.localRotation = Quaternion.Euler(0, 0, -8f);
            var outline = ofuda.gameObject.AddComponent<Outline>();
            outline.effectColor = SealRed;
            outline.effectDistance = new Vector2(2.5f, -2.5f);
            var letter = UIFactory.Text("Letter", ofuda.transform, "封", ofudaWidth * 0.72f, SealRed, new Vector2(ofudaWidth, ofudaHeight * 0.5f), new Vector2(0, ofudaHeight * 0.12f));
            letter.fontStyle = FontStyles.Bold;
            letter.raycastTarget = false;
            var line = UIFactory.Panel("Line", ofuda.transform, new Vector2(ofudaWidth * 0.6f, 3f), new Vector2(0, -ofudaHeight * 0.22f), SealRed);
            line.raycastTarget = false;
        }

        // 札が消えたり隠れたりしたら、吹き出しも消す（クリックで画面が変わったときに残らないように）
        void OnDisable() => DiceTooltip.HideFor(this);


        /// <summary>
        /// キーボードで選ぶときのキー（並び順）：1〜9、0、その下の段の Q W E R T Y U I O（P はポーチ、Z は割り振りを戻すので使わない）。
        /// ダイスはレリックで10個より多く持てるので、19個まで割り当てる。
        /// </summary>
        public const string HotkeyLabels = "1234567890QWERTYUIO";

        /// <summary>index 番目（0から）の札のキーの表示。割り当てがなければ null。</summary>
        public static string HotkeyLabel(int index) => index >= 0 && index < HotkeyLabels.Length ? HotkeyLabels[index].ToString() : null;

        /// <summary>キーボードで選ぶときのキー（1〜9・0・Q〜O）を札の左上に出す。index は並び順（0から）。</summary>
        public void SetHotkey(int index)
        {
            string label = HotkeyLabel(index);
            if (label == null) return;
            var size = ((RectTransform)transform).sizeDelta;
            // 札が狭いときは、名前を右へ寄せてキーの札と重ならないようにする
            var name = transform.Find("Name") as RectTransform;
            if (name != null && size.x < 260f)
            {
                name.anchoredPosition += new Vector2(14f, 0f);
                name.sizeDelta -= new Vector2(28f, 0f);
                var nameText = name.GetComponent<TextMeshProUGUI>();
                nameText.enableAutoSizing = true;
                nameText.fontSizeMax = nameText.fontSize;
                nameText.fontSizeMin = 16;
            }
            var back = UIFactory.Panel("Hotkey", transform, new Vector2(30, 30), new Vector2(-size.x / 2f + 46, size.y / 2f - 34), new Color(0.18f, 0.12f, 0.08f, 0.85f)); // 角の飾りに重ならないよう、少し内側（名前と同じ高さ）
            back.raycastTarget = false;
            UIFactory.Text("Label", back.transform, label, 20, new Color(0.96f, 0.92f, 0.82f), new Vector2(30, 30), Vector2.zero).raycastTarget = false;
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

        /// <summary>
        /// まだ手に入れたことのないものに押す朱の「新」の印（Balatro や Hades のように、初めてのものが目に入るように）。
        /// 札の左上の角に、少し傾けてはみ出させる。
        /// </summary>
        public static void AddNewBadge(Transform card, Vector2 size)
        {
            var stamp = UIFactory.Panel("NewBadge", card, new Vector2(44, 44), new Vector2(-size.x / 2f + 14f, size.y / 2f - 14f), new Color(0.8f, 0.16f, 0.1f));
            stamp.raycastTarget = false;
            stamp.transform.localRotation = Quaternion.Euler(0, 0, 12f);
            var text = UIFactory.Text("Text", stamp.transform, "新", 28, Color.white, new Vector2(44, 44), Vector2.zero);
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
        }

        /// <summary>まだ手に入れたことのないダイスか（図鑑に載っていない）。</summary>
        public static bool IsNew(DiceData data) => data != null && !RunHistory.Seen(RunHistory.Category.Dice).Contains(data.displayName);

        /// <summary>一瞬明るく光らせる（リフレッシュの演出）。</summary>
        public IEnumerator PlayFlash()
        {
            StartCoroutine(UIAnim.Punch(transform, 0.12f, 0.3f));
            yield return UIAnim.Tween(0.5f, t => flash.color = new Color(1f, 0.95f, 0.6f, 0.9f * (1f - t)));
        }
    }
}
