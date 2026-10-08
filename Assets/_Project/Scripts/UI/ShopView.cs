using System;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ショップの画面（仕様書 第11章）。品物を選んで「買う」で決定する（押し間違えを防ぐため）。
    /// 実際の購入（入れ替え先・刻印の面を選ぶ）は GameController が行い、終わったら Refresh を呼ぶ。
    /// </summary>
    public class ShopView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color CardColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color SelectedColor = new Color(1f, 0.78f, 0.3f);
        static readonly Color AccentColor = new Color(1f, 0.78f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.85f);

        public event Action<ShopItem> BuyClicked;
        public event Action RemoveClicked;
        public event Action LeaveClicked;
        /// <summary>持っているお守りがクリックされた（使う・捨てるを選ぶ）。</summary>
        public event Action<CharmData> CharmClicked;

        UIArt art;
        Shop shop;
        RunState run;
        TextMeshProUGUI subText;
        RectTransform content;
        ShopItem selected;
        Button buyButton;
        TextMeshProUGUI buyLabel;
        Button removeButton;
        TextMeshProUGUI removeLabel;
        CharmBar charmBar;

        public static ShopView Create(Transform canvas, UIArt art, Shop shop, RunState run)
        {
            var root = UIFactory.Stretch("ShopView", canvas);
            var view = root.gameObject.AddComponent<ShopView>();
            view.art = art;
            view.shop = shop;
            view.run = run;

            UIFactory.Background(root, art != null ? art.mapBackground : null, new Color(0.3f, 0.22f, 0.18f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.55f));
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1300, 130), new Vector2(0, 450), ShadeColor);
            UIFactory.Text("Title", titlePanel.transform, "ショップ", 50, AccentColor, new Vector2(1240, 64), new Vector2(0, 24)).fontStyle = FontStyles.Bold;
            view.subText = UIFactory.Text("Sub", titlePanel.transform, "", 28, PaperColor, new Vector2(1240, 46), new Vector2(0, -36));
            view.content = UIFactory.Rect("Content", root, new Vector2(1920, 700), new Vector2(0, 40));

            view.buyButton = UIFactory.Button("BuyButton", root, new Vector2(460, 84), new Vector2(-480, -440), AccentColor, "", 30, out view.buyLabel);
            view.buyButton.onClick.AddListener(() =>
            {
                if (view.selected != null && view.shop.CanAfford(view.selected)) view.BuyClicked?.Invoke(view.selected);
            });
            view.removeButton = UIFactory.Button("RemoveButton", root, new Vector2(420, 84), new Vector2(0, -440), ButtonColor, "", 28, out view.removeLabel);
            view.removeButton.onClick.AddListener(() => view.RemoveClicked?.Invoke());
            var leave = UIFactory.Button("LeaveButton", root, new Vector2(340, 84), new Vector2(440, -440), ButtonColor, "立ち去る", 30, out _);
            leave.onClick.AddListener(() => view.LeaveClicked?.Invoke());

            // 持っているお守り（右上）。クリックで使う・捨てる
            view.charmBar = CharmBar.Create(root, new Vector2(700, 505), -230f);
            view.charmBar.Clicked += c => view.CharmClicked?.Invoke(c);
            UIFactory.Text("CharmLabel", root, "お守り（クリックで使う・捨てる）", 18, PaperColor, new Vector2(260, 26), new Vector2(796, 430));

            view.Refresh();
            return view;
        }

        /// <summary>品物・所持金・ボタンを今の状態に合わせて作り直す。</summary>
        public void Refresh()
        {
            if (selected != null && selected.sold) selected = null;
            subText.text = $"所持金 <color=#FFD24D>{run.Gold} G</color>　品物を選んで「買う」。";
            charmBar.Refresh(run, run.CanUseNow, true);
            UIFactory.ClearChildren(content);

            // 上の段：ダイス
            var dice = shop.items.FindAll(i => i.kind == ShopItemKind.Dice);
            const float dw = 380f, dh = 200f, dgap = 40f;
            float dLeft = -(dice.Count * (dw + dgap) - dgap) / 2f + dw / 2f;
            for (int i = 0; i < dice.Count; i++)
            {
                var item = dice[i];
                var card = DiceCard.Create($"Dice{i}", content, new DiceInstance(item.dice), art, new Vector2(dw, dh),
                    new Vector2(dLeft + i * (dw + dgap), 210), StateLabel(item), item.sold, item == selected);
                card.Button.interactable = !item.sold;
                card.Button.onClick.AddListener(() => Select(item));
                if (item == selected) card.transform.localScale = Vector3.one * 1.06f;
                PriceTag(card.transform, item, new Vector2(0, -dh / 2f - 24));
            }

            // 下の段：レリックと刻印
            var others = shop.items.FindAll(i => i.kind != ShopItemKind.Dice);
            // お守りも並ぶので、品数が多いときは幅を詰める
            const float h = 230f, gap = 30f;
            float w = others.Count > 0 ? Mathf.Min(380f, (1860f - gap * (others.Count - 1)) / others.Count) : 380f;
            bool narrow = w < 300f;
            float left = -(others.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < others.Count; i++)
            {
                var item = others[i];
                var button = UIFactory.Button($"Item{i}", content, new Vector2(w, h), new Vector2(left + i * (w + gap), -100),
                    item == selected ? SelectedColor : CardColor, "", 1, out var unused);
                Destroy(unused.gameObject);
                var frame = DiceCard.RarityColor(item.Rarity);
                if (frame.HasValue)
                {
                    var o = button.gameObject.AddComponent<Outline>();
                    o.effectColor = frame.Value;
                    o.effectDistance = new Vector2(6, -6);
                }
                button.gameObject.AddComponent<CanvasGroup>().alpha = item.sold ? 0.45f : 1f;
                button.interactable = !item.sold;
                button.onClick.AddListener(() => Select(item));
                if (item == selected) button.transform.localScale = Vector3.one * 1.06f;

                if (item.kind == ShopItemKind.Charm)
                {
                    var badge = UIFactory.Panel("Badge", button.transform, new Vector2(72, 72), new Vector2(0, 52), new Color(0.85f, 0.55f, 0.5f));
                    if (item.charm.icon != null) UIFactory.Picture("Icon", badge.transform, item.charm.icon, new Vector2(68, 68), Vector2.zero, Color.white);
                    else UIFactory.Text("BadgeText", badge.transform, item.charm.displayName.Substring(0, 1), 40, Color.black, new Vector2(72, 72), Vector2.zero).fontStyle = FontStyles.Bold;
                }
                else if (item.kind == ShopItemKind.Relic)
                {
                    UIFactory.Picture("Icon", button.transform, item.relic.icon, new Vector2(80, 80), new Vector2(0, 50), AccentColor);
                }
                else
                {
                    var badge = UIFactory.Panel("Badge", button.transform, new Vector2(72, 72), new Vector2(0, 52), new Color(0.7f, 0.12f, 0.1f));
                    UIFactory.Text("BadgeText", badge.transform, item.engraving.badge, 46, Color.white, new Vector2(72, 72), Vector2.zero).fontStyle = FontStyles.Bold;
                }
                // 種類は左上に小さく、名前は1行（入らなければ小さく）、説明は名前の下に2行まで（入らなければ小さく）
                // （札が細いと、名前が折り返して説明や「クリックで選ぶ」に重なっていた）
                string kind = item.kind == ShopItemKind.Relic ? "レリック" : item.kind == ShopItemKind.Charm ? "お守り" : "刻印";
                UIFactory.Text("Kind", button.transform, kind, 16, new Color(0.45f, 0.3f, 0.2f), new Vector2(w - 16, 22), new Vector2(0, h / 2f - 14), TextAlignmentOptions.Left);
                var name = UIFactory.Text("Name", button.transform, item.DisplayName, narrow ? 26 : 30, InkColor, new Vector2(w - 16, 36), new Vector2(0, -14));
                name.fontStyle = FontStyles.Bold;
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.enableAutoSizing = true;
                name.fontSizeMax = narrow ? 26 : 30;
                name.fontSizeMin = 14;
                var desc = UIFactory.Text("Description", button.transform, item.Description, narrow ? 16 : 18, InkColor, new Vector2(w - 16, 50), new Vector2(0, -60));
                desc.enableAutoSizing = true;
                desc.fontSizeMax = narrow ? 16 : 18;
                desc.fontSizeMin = 11;
                UIFactory.Text("State", button.transform, StateLabel(item), 16, new Color(0.45f, 0.3f, 0.2f), new Vector2(w - 20, 22), new Vector2(0, -h / 2f + 13));
                PriceTag(button.transform, item, new Vector2(0, -h / 2f - 24));
            }

            // ボタン
            buyButton.interactable = selected != null && shop.CanAfford(selected);
            buyLabel.text = selected == null ? "品物を選んでください"
                : shop.CanAfford(selected) ? $"{selected.DisplayName} を{(selected.kind == ShopItemKind.Charm && !run.CanAddCharm ? "入れ替えて買う" : "買う")}（{selected.price} G）"
                : $"ゴールドが足りない（{selected.price} G）";
            removeButton.interactable = shop.CanRemove;
            removeLabel.text = run.pouch.All.Count <= run.config.shop.minDiceAfterRemove
                ? "ダイス削除（これ以上減らせない）"
                : $"ダイスを削除する（{shop.RemovePrice} G）";
        }

        void Select(ShopItem item)
        {
            if (item.sold) return;
            selected = item;
            Refresh();
        }

        string StateLabel(ShopItem item)
        {
            if (item.sold) return "売り切れ";
            if (item == selected) return "<b>選択中</b>";
            return "クリックで選ぶ";
        }

        void PriceTag(Transform parent, ShopItem item, Vector2 position)
        {
            var back = UIFactory.Panel("Price", parent, new Vector2(200, 40), position, ShadeColor);
            back.raycastTarget = false;
            string text = item.sold ? "売り切れ"
                : item.discounted ? $"<color=#FF8A6A>半額！</color> {item.price} G"
                : $"{item.price} G";
            var color = !item.sold && run.Gold < item.price ? new Color(0.7f, 0.6f, 0.55f) : new Color(1f, 0.85f, 0.4f);
            UIFactory.Text("Text", back.transform, text, 26, color, new Vector2(196, 38), Vector2.zero).fontStyle = FontStyles.Bold;
        }

        /// <summary>テスト・自動操作用：index 番目の品物を選んで「買う」。</summary>
        public void BuyForTest(int index)
        {
            Select(shop.items[index]);
            if (shop.CanAfford(selected)) BuyClicked?.Invoke(selected);
        }
    }
}
