using System;
using SaiNoMichi.Board;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>遊び方の説明（何ページか。前へ・次へ・閉じる）。始めの画面から開く。</summary>
    public class HowToView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);

        public event Action Closed;

        UIArt art;
        int page;
        RectTransform body;
        TextMeshProUGUI titleText, pageText;
        UnityEngine.UI.Button prevButton, nextButton;

        static readonly (string title, string text)[] Pages =
        {
            ("目的",
                "サイコロを振ってすごろくの道を進み、3つの層を越えていく旅です。\n" +
                "各層の右端にボスがいて、倒すと次の層へ進めます。\n" +
                "第3層「鬼の城」のボス「賽の神・八面」を倒すとクリアです。HP が 0 になると旅は終わります。\n\n" +
                "道の途中で、ダイスを増やしたり、ダイスの面を鍛えたり、レリックを集めたりして、旅のための自分だけのダイスを育てましょう。"),
            ("移動",
                "マップの下に並んだダイス（ポーチ）をクリックすると、そのダイスを振って出目の数だけ進みます。\n" +
                "ダイスにマウスを乗せると、止まりうるマスとその確率が光ります。どのダイスで振るかで、止まるマスを狙えます。\n\n" +
                "・道が分かれていて止まれるマスが複数あるときは、止まりたいマスをクリックします。\n" +
                "・通るだけで効くマス（祠・関所・茶屋・賽場）もあります。\n" +
                "・ボスの手前の休憩マスでは、必ず止まります。"),
            ("使用済みとリフレッシュ",
                "振ったダイスは「使用済み」になり、しばらく使えません。\n" +
                "使用可能なダイスが0個になった瞬間、使用済みのダイスが全部戻ります（リフレッシュ）。\n\n" +
                "移動と戦闘は、同じポーチのダイスを使います。\n" +
                "強いダイスを移動に使うと、次の戦闘では使えません。弱いダイスで移動して、強いダイスを戦闘に取っておく……といった、ダイスのやりくりがこのゲームの中心です。\n\n" +
                "ポーチには最大10個まで入ります。"),
            ("戦闘",
                "敵の頭の上に、次に何をするか（予告）が出ます。予告にマウスを乗せると説明が出ます。\n\n" +
                "1. ダイスを選んで「振る」（1ラウンドに3個まで）。ダイスを上へドラッグして離しても振れます。\n" +
                "2. 出目ごとに「攻撃」か「防御」を選びます。\n" +
                "3. 「決定」で、攻撃と敵の行動が起きます。防御はそのラウンドだけ、受けるダメージを減らします。\n\n" +
                "状態異常：脱力（与えるダメージ75%）・弱体（受けるダメージ150%）・脆弱（作れる防御75%）・毒（ラウンドの終わりにダメージ）など。HP の下の文字にマウスを乗せると説明が出ます。"),
            ("マス",
                null), // マスの一覧は絵つきで別に作る
            ("ダイスを育てる",
                "・報酬やショップで、新しいダイスが手に入ります。\n" +
                "・鍛冶（休憩マスの「鍛える」・鍛冶マス）では、ダイスの面に「刻印」を付けられます。面の数値を変える刻印と、その面が出たときに効果発動する刻印があります。\n" +
                "・レリックは、持っているだけでずっと効く道具です。画面の左上に並びます。\n" +
                "・お守りは1回だけ使える道具です（最大3個）。右上の欄からクリックで使います。進み御札などの移動のお守りは、マップで振る前に使います。\n\n" +
                "どのダイスを持ち、どの面を鍛えるかで、戦い方が大きく変わります。"),
            ("保存と中断",
                "マップで操作を待っているときに、自動で保存されます。\n" +
                "「中断」ボタンで保存して始めの画面に戻り、あとで「続きから」遊べます。\n" +
                "戦闘の途中でやめたときは、続きからその戦闘の最初からやり直しになります。\n\n" +
                "旅が終わる（クリア・倒れる）と、続きのデータは消えます。"),
        };

        static readonly (TileType type, string name, string text)[] Tiles =
        {
            (TileType.Battle, "戦闘", "敵と戦う。勝つとゴールドとダイス"),
            (TileType.Elite, "強敵", "手ごわい敵。勝つとレリック"),
            (TileType.Rest, "休憩", "HP を回復するか、刻印を付ける"),
            (TileType.Forge, "鍛冶", "ダイスの面に刻印を付ける"),
            (TileType.Treasure, "宝箱", "ゴールドかレリック"),
            (TileType.Shop, "ショップ", "ダイス・レリック・刻印・お守り"),
            (TileType.Event, "イベント", "何が起きるかはお楽しみ"),
            (TileType.Trap, "罠", "ダメージ・封印・呪い"),
            (TileType.Shrine, "祠", "通るとゴールド（止まると2倍）"),
            (TileType.Checkpoint, "関所", "通るとゴールドを払う"),
            (TileType.Teahouse, "茶屋", "通ると HP 回復"),
            (TileType.DiceHall, "賽場", "通ると使用済みのダイスが1個戻る"),
        };

        public static HowToView Create(Transform canvas, UIArt art)
        {
            var root = UIFactory.Stretch("HowToView", canvas);
            var view = root.gameObject.AddComponent<HowToView>();
            view.art = art;
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.9f));
            var box = UIFactory.Panel("Box", root, new Vector2(1400, 860), new Vector2(0, 20), new Color(0.12f, 0.08f, 0.06f, 1f));
            view.titleText = UIFactory.Text("Title", box.transform, "", 48, GoldColor, new Vector2(1300, 70), new Vector2(0, 370));
            view.titleText.fontStyle = FontStyles.Bold;
            view.body = UIFactory.Rect("Body", box.transform, new Vector2(1300, 620), new Vector2(0, 10));
            view.pageText = UIFactory.Text("Page", box.transform, "", 26, PaperColor, new Vector2(200, 40), new Vector2(0, -375));

            view.prevButton = UIFactory.Button("Prev", box.transform, new Vector2(220, 64), new Vector2(-420, -375), ButtonColor, "前へ", 28, out _);
            view.prevButton.onClick.AddListener(() => view.Show(view.page - 1));
            view.nextButton = UIFactory.Button("Next", box.transform, new Vector2(220, 64), new Vector2(420, -375), GoldColor, "次へ", 28, out _);
            view.nextButton.onClick.AddListener(() => view.Show(view.page + 1));
            var close = UIFactory.Button("Close", box.transform, new Vector2(160, 56), new Vector2(600, 370), ButtonColor, "閉じる", 26, out _);
            close.onClick.AddListener(() => view.Closed?.Invoke());
            view.Show(0);
            return view;
        }

        void Show(int index)
        {
            page = Mathf.Clamp(index, 0, Pages.Length - 1);
            UIFactory.ClearChildren(body);
            var (title, text) = Pages[page];
            titleText.text = title;
            pageText.text = $"{page + 1} / {Pages.Length}";
            prevButton.interactable = page > 0;
            nextButton.interactable = page < Pages.Length - 1;

            if (text != null)
            {
                UIFactory.Text("Text", body, text, 30, PaperColor, new Vector2(1240, 600), Vector2.zero, TextAlignmentOptions.TopLeft);
                return;
            }

            // マスの一覧（絵つき、2列）
            for (int i = 0; i < Tiles.Length; i++)
            {
                var (type, name, desc) = Tiles[i];
                int col = i % 2, row = i / 2;
                var pos = new Vector2(col == 0 ? -620 : 30, 270 - row * 100);
                var sprite = art != null ? art.TileSprite(new TileNode(1, type)) : null;
                var icon = UIFactory.Picture("Icon", body, sprite, new Vector2(80, 80), pos + new Vector2(40, 0), Color.white);
                icon.raycastTarget = false;
                UIFactory.Text("Name", body, name, 30, GoldColor, new Vector2(130, 50), pos + new Vector2(150, 0), TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;
                UIFactory.Text("Desc", body, desc, 26, PaperColor, new Vector2(420, 80), pos + new Vector2(430, 0), TextAlignmentOptions.Left);
            }
        }
    }
}
