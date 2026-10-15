using SaiNoMichi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ダイスの札にマウスを乗せたときの吹き出し：名前・レア度・説明・面ごとの値と刻印・用語の説明。
    /// 札の上（入らなければ下）に出す。どの画面の札でも同じものを使う（いちばん上の Canvas に1つ）。
    /// </summary>
    public static class DiceTooltip
    {
        const float Width = 520f;
        static RectTransform panel;
        static TextMeshProUGUI text;
        static DiceCard owner;

        public static void Show(DiceCard card)
        {
            if (card == null || card.Die == null) return;
            var canvas = card.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = (RectTransform)canvas.rootCanvas.transform;
            if (panel == null || panel.parent != root)
            {
                if (panel != null) Object.Destroy(panel.gameObject);
                var image = UIFactory.Panel("DiceTooltip", root, new Vector2(Width, 200), Vector2.zero, new Color(0.08f, 0.05f, 0.04f, 0.97f));
                image.raycastTarget = false; // マウスを遮らない（乗せたり外したりがちらつかないように）
                panel = image.rectTransform;
                panel.pivot = new Vector2(0.5f, 0f);
                text = UIFactory.Text("Text", panel, "", 24, new Color(0.96f, 0.92f, 0.82f), new Vector2(Width - 60, 180), Vector2.zero, TextAlignmentOptions.TopLeft);
                text.raycastTarget = false;
            }
            owner = card;

            var die = card.Die;
            var data = die.data;
            var rarity = data != null ? data.rarity : Rarity.Common;
            string head = $"<b><size=120%><color={RelicBar.RarityHex(rarity)}>{die.DisplayName}</color></size></b>　<size=80%><color=#B8AC98>{RelicBar.RarityName(rarity)}</color></size>";
            string desc = data != null && !string.IsNullOrEmpty(data.description) ? $"\n{data.description}" : "";
            text.text = Keywords.AppendForTip(head + desc + "\n<size=90%>" + PouchView.FaceLines(die) + "</size>");

            // 大きさ：文の長さに合わせる
            text.rectTransform.sizeDelta = new Vector2(Width - 60, 1000);
            text.ForceMeshUpdate();
            float height = text.preferredHeight + 50f;
            panel.sizeDelta = new Vector2(Width, height);
            text.rectTransform.sizeDelta = new Vector2(Width - 60, height - 34);
            text.rectTransform.anchoredPosition = new Vector2(0, 0);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            UISkin.Decorate(panel.GetComponent<Image>(), panel.sizeDelta, new Color(0.08f, 0.05f, 0.04f, 0.97f));

            // 位置：札の上。上に入らなければ下。左右は画面の中に収める
            var cardRect = (RectTransform)card.transform;
            Vector2 center = root.InverseTransformPoint(cardRect.TransformPoint(cardRect.rect.center));
            float cardHalf = cardRect.rect.height * cardRect.lossyScale.y / root.lossyScale.y / 2f;
            var bounds = root.rect;
            float y = center.y + cardHalf + 10f;
            if (y + height > bounds.yMax - 10f)
            {
                panel.pivot = new Vector2(0.5f, 1f);
                y = center.y - cardHalf - 10f;
            }
            else panel.pivot = new Vector2(0.5f, 0f);
            float x = Mathf.Clamp(center.x, bounds.xMin + Width / 2f + 10f, bounds.xMax - Width / 2f - 10f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(x, y);
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
        }

        public static void Hide()
        {
            owner = null;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        /// <summary>この札の吹き出しを出していたら消す（札が消えたとき）。</summary>
        public static void HideFor(DiceCard card)
        {
            if (owner == card) Hide();
        }
    }
}
