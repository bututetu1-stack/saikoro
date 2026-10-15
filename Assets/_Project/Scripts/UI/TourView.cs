using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Dice;
using SaiNoMichi.Tour;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 国めぐりの画面：巨大マップ（ドラッグ・ホイールで動かす。「全体」で縮める）、旅人の駒、目的地、厄神、
    /// 上の帯（ターン・勝ち方・目的地）、右の帯（旅人ごとのゴールド・資産・到着・札）、下の帯（知らせ・ダイス・札）。
    /// </summary>
    public class TourView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ShadeColor = new Color(0.1f, 0.07f, 0.05f, 0.85f);
        static readonly Color RoadColor = new Color(0.55f, 0.4f, 0.25f, 0.85f);
        public static readonly Color[] PlayerColors =
        {
            new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.35f), new Color(0.45f, 0.75f, 1f), new Color(0.6f, 0.9f, 0.45f),
        };

        const float Unit = 130f;        // 盤面の 1 を何ピクセルにするか
        const float TileSize = 46f;
        const float TownSize = 88f;
        const float Margin = 260f;

        TourState tour;
        UIArt art;
        ScrollRect scroll;
        RectTransform content;
        Vector2 contentSize;
        Vector2 origin;               // 盤面の最小の位置
        readonly Dictionary<TourNode, RectTransform> tiles = new Dictionary<TourNode, RectTransform>();
        readonly Dictionary<TourNode, Image> glows = new Dictionary<TourNode, Image>();
        readonly Dictionary<TourNode, TextMeshProUGUI> labels = new Dictionary<TourNode, TextMeshProUGUI>();
        readonly Dictionary<TourTown, RectTransform> ownership = new Dictionary<TourTown, RectTransform>();
        readonly List<RectTransform> pieces = new List<RectTransform>();
        RectTransform destinationMark;
        RectTransform yakugamiMark;
        TextMeshProUGUI topLeft, topCenter;
        RectTransform playersPanel;
        TextMeshProUGUI messageText;
        RectTransform messagePanel;
        RectTransform trayRoot;
        RectTransform cardsRoot;
        Button rollButton;
        TextMeshProUGUI rollLabel;
        DiceFaceView[] rollFaces;
        bool overview;

        public event Action<DiceInstance> DieClicked;
        public event Action RollClicked;
        public event Action<int> CardClicked;
        public event Action QuitClicked;
        public event Action PouchClicked;

        public static TourView Create(Transform canvas, UIArt art, TourState tour)
        {
            var root = UIFactory.Stretch("TourView", canvas);
            var view = root.gameObject.AddComponent<TourView>();
            view.tour = tour;
            view.art = art;
            view.Build();
            return view;
        }

        void Build()
        {
            UIFactory.Background(transform, art != null ? art.mapBackground : null, new Color(0.85f, 0.8f, 0.65f));
            BuildBoard();

            // 上の帯
            var bar = UIFactory.Panel("TopBar", transform, new Vector2(1920, 70), new Vector2(0, 505), ShadeColor);
            topLeft = UIFactory.Text("Left", bar.transform, "", 30, PaperColor, new Vector2(900, 60), new Vector2(-470, 0), TextAlignmentOptions.Left);
            topCenter = UIFactory.Text("Center", bar.transform, "", 32, GoldColor, new Vector2(700, 60), new Vector2(140, 0));
            topCenter.fontStyle = FontStyles.Bold;
            var all = UIFactory.Button("OverviewButton", bar.transform, new Vector2(120, 48), new Vector2(620, 0), new Color(0.75f, 0.68f, 0.58f), "全体", 24, out _);
            all.onClick.AddListener(ToggleOverview);
            var pouch = UIFactory.Button("PouchButton", bar.transform, new Vector2(120, 48), new Vector2(745, 0), new Color(0.75f, 0.68f, 0.58f), "ポーチ", 24, out _);
            pouch.onClick.AddListener(() => PouchClicked?.Invoke());
            var quit = UIFactory.Button("QuitButton", bar.transform, new Vector2(120, 48), new Vector2(870, 0), new Color(0.75f, 0.68f, 0.58f), "やめる", 24, out _);
            quit.onClick.AddListener(() => QuitClicked?.Invoke());

            // 右の帯：旅人たち
            playersPanel = UIFactory.Panel("Players", transform, new Vector2(400, 300), Vector2.zero, ShadeColor).rectTransform;
            playersPanel.pivot = new Vector2(0.5f, 1f);
            playersPanel.anchoredPosition = new Vector2(745, 455);
            playersPanel.GetComponent<Image>().raycastTarget = false;

            // 下：知らせ・ダイス・札・振る
            messagePanel = UIFactory.Panel("MessagePanel", transform, new Vector2(1300, 90), new Vector2(-120, -290), ShadeColor).rectTransform;
            messageText = UIFactory.Text("Message", messagePanel, "", 28, PaperColor, new Vector2(1240, 84), Vector2.zero);
            messageText.enableAutoSizing = true;
            messageText.fontSizeMin = 16;
            messageText.fontSizeMax = 28;
            trayRoot = UIFactory.HorizontalScroll("DiceTray", transform, new Vector2(1500, 184), new Vector2(-190, -425));
            cardsRoot = UIFactory.Rect("Cards", transform, new Vector2(300, 400), new Vector2(790, -200)); // 右の帯（旅人たち）の下
            rollButton = UIFactory.Button("RollButton", transform, new Vector2(260, 90), new Vector2(790, -425), new Color(1f, 0.78f, 0.3f), "振る", 34, out rollLabel);
            rollButton.onClick.AddListener(() => RollClicked?.Invoke());
            rollButton.gameObject.SetActive(false);
            rollFaces = new[]
            {
                DiceFaceView.Create("Roll0", transform, art, 120, new Vector2(-80, 300)),
                DiceFaceView.Create("Roll1", transform, art, 120, new Vector2(80, 300)),
            };
            foreach (var f in rollFaces) f.gameObject.SetActive(false);
            BuildMinimap();
            Refresh();
        }

        // ---- 盤面 ----

        Vector2 Pos(TourNode n) => (n.position - origin) * Unit + new Vector2(Margin, Margin);

        void BuildBoard()
        {
            var viewport = UIFactory.Stretch("BoardViewport", transform);
            viewport.offsetMax = new Vector2(0, -70); // 上の帯の下から
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            origin = new Vector2(tour.map.nodes.Min(n => n.position.x), tour.map.nodes.Min(n => n.position.y));
            var max = new Vector2(tour.map.nodes.Max(n => n.position.x), tour.map.nodes.Max(n => n.position.y));
            contentSize = (max - origin) * Unit + new Vector2(Margin * 2, Margin * 2 + 300); // 下の帯に隠れないよう、下を広めに
            content = UIFactory.Rect("Content", viewport, contentSize, Vector2.zero);
            content.anchorMin = content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 1);
            content.anchoredPosition = Vector2.zero;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;

            // 盤面の y は下向きに増やす（上の行から並べる）
            var roads = Layer("Roads");
            var glowLayer = Layer("Glows");
            var bodies = Layer("Tiles");
            var labelLayer = Layer("Labels");

            foreach (var n in tour.map.nodes)
            {
                foreach (var m in n.neighbors)
                {
                    if (m.id < n.id) continue;
                    Road(roads, P(n), P(m));
                }
            }
            foreach (var n in tour.map.nodes)
            {
                bool town = n.kind == TourTileKind.Town;
                float size = town ? TownSize : TileSize;
                var glow = UIFactory.Panel($"Glow{n.id}", glowLayer, Vector2.one * (size + 18), P(n), new Color(1f, 0.82f, 0.25f));
                glow.raycastTarget = false;
                glow.enabled = false;
                glows[n] = glow;

                var sprite = SpriteFor(n.kind);
                // 青・赤は、木の台座の絵に色を付けて、＋・−の印を重ねる
                bool tinted = sprite == null && art != null && art.tourTileBase != null;
                if (tinted) sprite = art.tourTileBase;
                var body = UIFactory.Picture($"Tile{n.id}", bodies, sprite, Vector2.one * size, P(n), ColorFor(n.kind));
                if (tinted) body.color = Color.Lerp(ColorFor(n.kind), Color.white, 0.25f);
                UISkin.AddShadow(body, new Vector2(2f, -4f), new Color(0.15f, 0.1f, 0.05f, 0.4f));
                if (sprite == null || tinted)
                {
                    var mark = UIFactory.Text("Mark", body.transform, MarkFor(n.kind), town ? 34 : 24, Color.white, Vector2.one * size, Vector2.zero);
                    mark.fontStyle = FontStyles.Bold;
                    mark.outlineWidth = 0.14f;
                    mark.outlineColor = new Color32(30, 15, 5, 255);
                }
                body.raycastTarget = true;
                var captured = n;
                var button = body.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnTileClicked(captured));
                var hover = body.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => ShowTileInfo(captured);
                hover.Exited += () => ApplyMessage(baseMessage);
                tiles[n] = body.rectTransform;

                // 札（「ここへ」など）
                var label = UIFactory.Text($"Label{n.id}", labelLayer, "", 22, Color.white, new Vector2(160, 30), P(n) + new Vector2(0, -size / 2f - 16));
                label.fontStyle = FontStyles.Bold;
                label.outlineWidth = 0.2f;
                label.outlineColor = new Color32(60, 10, 5, 255);
                labels[n] = label;

                if (town)
                {
                    var t = tour.map.towns[n.town];
                    var name = UIFactory.Text("TownName", labelLayer, t.name + (t.diceShop ? "<size=70%>（賽）</size>" : ""), 28, InkColor, new Vector2(200, 36), P(n) + new Vector2(0, size / 2f + 18));
                    name.fontStyle = FontStyles.Bold;
                    name.outlineWidth = 0.18f;
                    name.outlineColor = new Color32(250, 240, 220, 255);
                    var dots = UIFactory.Rect("Owners", labelLayer, new Vector2(120, 16), P(n) + new Vector2(0, -size / 2f - 10));
                    ownership[t] = dots;
                }
            }
            destinationMark = UIFactory.Panel("Destination", glowLayer, Vector2.one * (TownSize + 44), Vector2.zero, new Color(1f, 0.85f, 0.3f, 0.9f)).rectTransform;
            UISkin.ApplyRounded(destinationMark.GetComponent<Image>(), UISkin.Outline, Vector2.one * (TownSize + 44));
            var destLabel = UIFactory.Text("Label", destinationMark, "目的地", 26, GoldColor, new Vector2(160, 34), new Vector2(0, TownSize / 2f + 52));
            destLabel.fontStyle = FontStyles.Bold;
            destLabel.outlineWidth = 0.2f;
            destLabel.outlineColor = new Color32(40, 20, 5, 255);
            AnchorTopLeft(destinationMark);

            // 駒
            for (int i = 0; i < tour.players.Count; i++)
            {
                var p = tour.players[i];
                var img = UIFactory.Picture($"Piece{i}", content, art != null ? art.player : null, new Vector2(92, 92), Vector2.zero, PlayerColors[i % PlayerColors.Length]);
                img.color = i == 0 || art == null || art.player == null ? Color.white : Color.Lerp(Color.white, PlayerColors[i % PlayerColors.Length], 0.55f);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                AnchorTopLeft(rt);
                rt.pivot = new Vector2(0.5f, 0f);
                var tag = UIFactory.Panel("Tag", rt, new Vector2(120, 30), new Vector2(0, 62), PlayerColors[i % PlayerColors.Length]);
                tag.raycastTarget = false;
                var tagText = UIFactory.Text("Name", tag.transform, p.name, 20, InkColor, new Vector2(116, 28), Vector2.zero);
                tagText.fontStyle = FontStyles.Bold;
                tagText.enableAutoSizing = true;
                tagText.fontSizeMin = 12;
                tagText.fontSizeMax = 20;
                pieces.Add(rt);
            }
            if (art != null && art.yakugami != null)
            {
                // 厄神の絵（とりついた人の駒のそばに浮かぶ）
                var img = UIFactory.Picture("Yakugami", content, art.yakugami, new Vector2(84, 84), Vector2.zero);
                img.raycastTarget = false;
                yakugamiMark = img.rectTransform;
                AnchorTopLeft(yakugamiMark);
            }
            else
            {
                yakugamiMark = UIFactory.Panel("Yakugami", content, new Vector2(54, 54), Vector2.zero, new Color(0.45f, 0.2f, 0.55f, 0.95f)).rectTransform;
                AnchorTopLeft(yakugamiMark);
                var yk = UIFactory.Text("Text", yakugamiMark, "厄", 34, Color.white, new Vector2(54, 54), Vector2.zero);
                yk.fontStyle = FontStyles.Bold;
                yk.raycastTarget = false;
                yakugamiMark.GetComponent<Image>().raycastTarget = false;
            }
            yakugamiMark.gameObject.AddComponent<IdleBreath>();
        }

        // 盤面の上で y を下向きに（上の帯に近いほうが盤面の上）
        Vector2 P(TourNode n)
        {
            var p = Pos(n);
            return new Vector2(p.x, -p.y);
        }

        RectTransform Layer(string name)
        {
            var layer = UIFactory.Rect(name, content, Vector2.zero, Vector2.zero);
            AnchorTopLeft(layer);
            return layer;
        }

        static void AnchorTopLeft(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        }

        static void Road(RectTransform parent, Vector2 from, Vector2 to)
        {
            var road = UIFactory.Panel("Road", parent, new Vector2(Vector2.Distance(from, to), 12), from, RoadColor);
            road.raycastTarget = false;
            var rt = road.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = from;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
        }

        Sprite SpriteFor(TourTileKind kind)
        {
            if (art == null) return null;
            switch (kind)
            {
                case TourTileKind.Battle: return art.tileBattle;
                case TourTileKind.Shrine: return art.tileShrine;
                case TourTileKind.Event: return art.tileEvent;
                case TourTileKind.Yellow: return art.tileTreasure;
                case TourTileKind.Town: return art.tourTown ?? art.tileTeahouse ?? art.tileRest;
                default: return null; // 青・赤は色の丸
            }
        }

        static Color ColorFor(TourTileKind kind)
        {
            switch (kind)
            {
                case TourTileKind.Blue: return new Color(0.3f, 0.55f, 0.95f);
                case TourTileKind.Red: return new Color(0.9f, 0.3f, 0.25f);
                case TourTileKind.Yellow: return new Color(0.95f, 0.8f, 0.25f);
                case TourTileKind.Battle: return new Color(0.6f, 0.2f, 0.2f);
                case TourTileKind.Shrine: return new Color(0.5f, 0.75f, 0.5f);
                case TourTileKind.Event: return new Color(0.75f, 0.55f, 0.85f);
                default: return new Color(0.85f, 0.7f, 0.45f);
            }
        }

        static string MarkFor(TourTileKind kind)
        {
            switch (kind)
            {
                case TourTileKind.Blue: return "+";
                case TourTileKind.Red: return "−";
                case TourTileKind.Yellow: return "札";
                case TourTileKind.Battle: return "戦";
                case TourTileKind.Shrine: return "祠";
                case TourTileKind.Event: return "？";
                default: return "町";
            }
        }

        public static string KindName(TourTileKind kind)
        {
            switch (kind)
            {
                case TourTileKind.Blue: return "青のマス（ゴールドがもらえる）";
                case TourTileKind.Red: return "赤のマス（ゴールドを取られる）";
                case TourTileKind.Yellow: return "黄のマス（札がもらえる）";
                case TourTileKind.Battle: return "戦闘のマス（魔物と戦う）";
                case TourTileKind.Shrine: return "祠（HP が回復し、ときどき札）";
                case TourTileKind.Event: return "出来事のマス（何かが起きる）";
                default: return "町";
            }
        }

        void ShowTileInfo(TourNode n)
        {
            string text = KindName(n.kind);
            if (n.kind == TourTileKind.Town)
            {
                var t = tour.map.towns[n.town];
                var props = string.Join("　", t.properties.Select(p => $"{p.name} {p.price}G" + (p.owner >= 0 ? $"<color=#{ColorUtility.ToHtmlStringRGB(PlayerColors[p.owner % PlayerColors.Length])}>（{tour.players[p.owner].name}）</color>" : "")));
                text = $"{t.name}" + (t.diceShop ? "（サイコロ屋あり）" : "（札屋あり）") + (tour.Destination == t ? "　<color=#FFD24D>目的地</color>" : "") + $"\n{props}";
            }
            var here = tour.players.Where(p => p.node == n).Select(p => p.name).ToList();
            if (here.Count > 0) text += $"\nここにいる：{string.Join("・", here)}";
            ApplyMessage(text);
        }

        // ---- 小さな地図（左上。町・目的地・旅人の位置。押すとそこへ移る） ----

        static readonly Vector2 MinimapSize = new Vector2(330, 200);
        RectTransform minimapDots;

        void BuildMinimap()
        {
            var panel = UIFactory.Panel("Minimap", transform, MinimapSize + new Vector2(20, 20), new Vector2(-775, 355), ShadeColor);
            panel.raycastTarget = true;
            var click = panel.gameObject.AddComponent<MinimapClick>();
            click.Clicked += OnMinimapClicked;
            // 道と町（動かないので、はじめに1回だけ描く）
            var fixedDots = UIFactory.Rect("Fixed", panel.transform, MinimapSize, Vector2.zero);
            foreach (var n in tour.map.nodes)
            {
                bool town = n.kind == TourTileKind.Town;
                var c = town ? new Color(0.95f, 0.85f, 0.6f) : new Color(0.6f, 0.5f, 0.4f, 0.6f);
                var dot = UIFactory.Panel("N", fixedDots, Vector2.one * (town ? 9f : 3f), MiniPos(n), c);
                dot.raycastTarget = false;
            }
            minimapDots = UIFactory.Rect("Dots", panel.transform, MinimapSize, Vector2.zero);
            var title = UIFactory.Text("Title", panel.transform, "地図（押すとそこへ）", 16, PaperColor, new Vector2(300, 22), new Vector2(0, MinimapSize.y / 2f + 4));
            title.raycastTarget = false;
        }

        Vector2 MiniPos(TourNode n)
        {
            var p = P(n);
            float nx = p.x / contentSize.x, ny = -p.y / contentSize.y;
            return new Vector2(-MinimapSize.x / 2f + nx * MinimapSize.x, MinimapSize.y / 2f - ny * MinimapSize.y);
        }

        void RefreshMinimap()
        {
            if (minimapDots == null) return;
            UIFactory.ClearChildren(minimapDots);
            if (tour.Destination != null)
            {
                var dest = UIFactory.Text("Dest", minimapDots, "★", 26, new Color(1f, 0.8f, 0.2f), new Vector2(30, 30), MiniPos(tour.Destination.node));
                dest.outlineWidth = 0.2f;
                dest.outlineColor = new Color32(40, 20, 5, 255);
                dest.raycastTarget = false;
            }
            for (int i = 0; i < tour.players.Count; i++)
            {
                var p = tour.players[i];
                var dot = UIFactory.Panel($"P{i}", minimapDots, Vector2.one * (i == tour.TurnIndex ? 16f : 12f), MiniPos(p.node) + new Vector2((i % 2) * 4f - 2f, (i / 2) * 4f - 2f), PlayerColors[i % PlayerColors.Length]);
                dot.raycastTarget = false;
                if (tour.YakugamiHolder == i)
                {
                    var ring = UIFactory.Panel("Yaku", dot.transform, Vector2.one * 22f, Vector2.zero, new Color(0.6f, 0.3f, 0.75f, 0.9f));
                    UISkin.ApplyRounded(ring, UISkin.Outline, Vector2.one * 22f);
                    ring.raycastTarget = false;
                }
            }
        }

        void OnMinimapClicked(Vector2 local)
        {
            // 小さな地図の位置 → 盤面の位置
            float nx = (local.x + MinimapSize.x / 2f) / MinimapSize.x;
            float ny = (MinimapSize.y / 2f - local.y) / MinimapSize.y;
            if (overview) ToggleOverview();
            var viewport = (RectTransform)scroll.viewport;
            var size = viewport.rect.size;
            var target = new Vector2(nx * contentSize.x, ny * contentSize.y);
            float x = Mathf.Clamp(-target.x + size.x * 0.5f, -(contentSize.x - size.x), 0f);
            float y = Mathf.Clamp(target.y - size.y * 0.5f, 0f, contentSize.y - size.y);
            content.anchoredPosition = new Vector2(x, y);
            Sfx.Play(SoundId.Button);
        }

        // ---- 表示の更新 ----

        public void Refresh()
        {
            string goal = tour.goal == TourGoal.Assets ? "資産" : tour.goal == TourGoal.Arrivals ? "目的地の一番乗り" : "大ボス";
            topLeft.text = $"賽の国めぐり　<color=#FFD24D>ターン {Mathf.Min(tour.Round, tour.rounds)} / {tour.rounds}</color>　勝ち方：{goal}";
            int far = tour.Destination != null ? tour.map.Distance(tour.Current.node, tour.Destination.node) : 0;
            topCenter.text = tour.Destination != null ? $"目的地：{tour.Destination.name}　<size=75%>（{tour.Current.name}からあと {far} マス）</size>" : "";

            // 目的地の印
            if (tour.Destination != null) destinationMark.anchoredPosition = P(tour.Destination.node);

            // 名物の持ち主（町の下の点）
            foreach (var kv in ownership)
            {
                UIFactory.ClearChildren(kv.Value);
                var props = kv.Key.properties;
                for (int i = 0; i < props.Count; i++)
                {
                    var c = props[i].owner >= 0 ? PlayerColors[props[i].owner % PlayerColors.Length] : new Color(0.4f, 0.35f, 0.3f, 0.7f);
                    var dot = UIFactory.Panel($"Dot{i}", kv.Value, new Vector2(18, 18), new Vector2(-(props.Count - 1) * 11f + i * 22f, 0), c);
                    dot.raycastTarget = false;
                }
            }
            PlacePieces();
            RefreshPlayers();
            RefreshMinimap();
        }

        void PlacePieces()
        {
            for (int i = 0; i < tour.players.Count; i++)
            {
                var p = tour.players[i];
                // 同じマスに何人かいるときは、2列に並べる。名札はいまの手番の人（とひとりでいる人）だけ
                var here = tour.players.Where(o => o.node == p.node).ToList();
                int k = here.IndexOf(p);
                var offset = here.Count == 1 ? new Vector2(0, -18) : new Vector2(-26 + (k % 2) * 52, -6 - (k / 2) * 34);
                pieces[i].anchoredPosition = P(p.node) + offset;
                var tag = pieces[i].Find("Tag");
                if (tag != null) tag.gameObject.SetActive(here.Count == 1 || i == tour.TurnIndex);
            }
            if (tour.TurnIndex < pieces.Count) pieces[tour.TurnIndex].SetAsLastSibling();
            yakugamiMark.gameObject.SetActive(tour.YakugamiHolder >= 0);
            if (tour.YakugamiHolder >= 0)
            {
                yakugamiMark.anchoredPosition = pieces[tour.YakugamiHolder].anchoredPosition + new Vector2(40, 66);
                yakugamiMark.SetAsLastSibling();
            }
        }

        /// <summary>いくつかのマスが見えるようにする（入りきれば、そのまん中へ。入らなければ全体を見る）。</summary>
        public void FocusOnAll(ICollection<TourNode> nodes)
        {
            if (nodes == null || nodes.Count == 0) return;
            var viewport = (RectTransform)scroll.viewport;
            var size = viewport.rect.size - new Vector2(500, 420); // 右の帯・下の帯のぶん
            var ps = nodes.Select(P).ToList();
            var min = new Vector2(ps.Min(p => p.x), ps.Min(p => p.y));
            var max = new Vector2(ps.Max(p => p.x), ps.Max(p => p.y));
            if (max.x - min.x > size.x || max.y - min.y > size.y)
            {
                if (!overview) ToggleOverview();
                return;
            }
            if (overview) ToggleOverview();
            var center = (min + max) / 2f;
            var full = viewport.rect.size;
            float x = Mathf.Clamp(-center.x + full.x * 0.42f, -(contentSize.x - full.x), 0f);
            float y = Mathf.Clamp(-center.y - full.y * 0.4f, 0f, contentSize.y - full.y);
            content.anchoredPosition = new Vector2(x, y);
        }

        void RefreshPlayers()
        {
            UIFactory.ClearChildren(playersPanel);
            float y = -18f;
            var standings = tour.Standings();
            for (int i = 0; i < tour.players.Count; i++)
            {
                var p = tour.players[i];
                bool now = i == tour.TurnIndex;
                int rank = standings.IndexOf(p) + 1;
                var row = UIFactory.Panel($"Row{i}", playersPanel, new Vector2(380, 92), new Vector2(0, y - 46), now ? new Color(0.35f, 0.25f, 0.12f, 0.95f) : new Color(0.12f, 0.08f, 0.06f, 0.6f));
                row.raycastTarget = false;
                var rt = row.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                string yaku = tour.YakugamiHolder == i ? "　<color=#C58BFF>厄</color>" : "";
                string score = tour.goal == TourGoal.Arrivals ? $"到着 {p.arrivals}" : tour.goal == TourGoal.FinalBoss ? $"資産 {tour.Assets(p)}" : $"資産 {tour.Assets(p)}";
                UIFactory.Text("Name", row.transform, $"{rank}. <color=#{ColorUtility.ToHtmlStringRGB(PlayerColors[i % PlayerColors.Length])}>■</color><b>{p.name}</b>{(p.isAI ? "<size=70%>（AI）</size>" : "")}{yaku}",
                    24, PaperColor, new Vector2(360, 32), new Vector2(0, 24), TextAlignmentOptions.Left);
                UIFactory.Text("Info", row.transform, $"<size=90%>{p.Gold} G　{score}　HP {p.run.player.hp}/{p.run.player.maxHp}\n到着 {p.arrivals}　札 {p.cards.Count}　ダイス {p.run.pouch.AvailableCount}/{p.run.pouch.All.Count}</size>",
                    20, PaperColor, new Vector2(360, 56), new Vector2(0, -16), TextAlignmentOptions.Left);
                y -= 98f;
            }
            playersPanel.sizeDelta = new Vector2(400, -y + 8f);
        }

        string baseMessage = "";

        public void SetMessage(string message)
        {
            baseMessage = message ?? "";
            ApplyMessage(baseMessage);
        }

        void ApplyMessage(string message)
        {
            messageText.text = message;
            float height = Mathf.Clamp(messageText.GetPreferredValues(message ?? "", 1240f, 0f).y + 20f, 90f, 220f);
            messagePanel.sizeDelta = new Vector2(1300, height);
            messagePanel.anchoredPosition = new Vector2(-120, -335 + height / 2f);
        }

        // ---- ダイスと札（いまの手番の人が人のとき） ----

        public event Action<DiceInstance> DieHovered;
        public event Action DieUnhovered;
        readonly List<DiceInstance> trayOrder = new List<DiceInstance>();

        /// <summary>トレイに並んでいる順のダイス（キーで選ぶとき）。</summary>
        public IReadOnlyList<DiceInstance> TrayOrder => trayOrder;

        public void ShowTray(TourPlayer p, bool interactable, ICollection<DiceInstance> selected)
        {
            UIFactory.ClearChildren(trayRoot);
            trayOrder.Clear();
            // 使用可能を左、使用済みを右に寄せる（マップと同じ）
            var ordered = p.run.pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float h = 160f, gap = 20f;
            float w = Mathf.Clamp((1500f - gap * (ordered.Count - 1)) / Mathf.Max(1, ordered.Count), 220f, 280f);
            UIFactory.SetScrollWidth(trayRoot, ordered.Count * (w + gap) - gap);
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool canMove = available && Run.RunState.CanMoveWith(die);
                string state = !available ? (die.state == DiceState.Sealed ? "封印中" : "使用済み") : !canMove ? "移動に使えない" : interactable ? "クリックで振る" : "使用可能";
                var card = DiceCard.Create($"Die{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0), state, !canMove, selected != null && selected.Contains(die));
                card.Button.interactable = interactable && canMove;
                if (canMove)
                {
                    card.SetHotkey(trayOrder.Count);
                    trayOrder.Add(die);
                }
                card.TooltipEnabled = false; // 吹き出しは盤面を隠すので、説明は知らせの欄に
                var captured = die;
                card.Button.onClick.AddListener(() => DieClicked?.Invoke(captured));
                var hover = card.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () =>
                {
                    ApplyMessage($"<b>{captured.DisplayName}</b>　目：{string.Join("・", captured.faces.Select(f => f.value))}" + (captured.data != null && !string.IsNullOrEmpty(captured.data.description) ? $"\n{captured.data.description}" : ""));
                    DieHovered?.Invoke(captured);
                };
                hover.Exited += () =>
                {
                    ApplyMessage(baseMessage);
                    DieUnhovered?.Invoke();
                };
            }
        }

        public void ShowCards(TourPlayer p, bool interactable)
        {
            UIFactory.ClearChildren(cardsRoot);
            var title = UIFactory.Text("Title", cardsRoot, p.cards.Count > 0 ? $"札（{p.cards.Count}）" : "札はない", 24, GoldColor, new Vector2(280, 34), new Vector2(0, 180));
            title.outlineWidth = 0.18f;
            title.outlineColor = new Color32(30, 15, 5, 255);
            for (int i = 0; i < p.cards.Count; i++)
            {
                var k = p.cards[i];
                bool usable = interactable && tour.CanUseCard(k);
                var b = UIFactory.Button($"Card{i}", cardsRoot, new Vector2(280, 58), new Vector2(0, 130 - i * 64), new Color(0.98f, 0.9f, 0.6f), TourCards.Name(k), 24, out _);
                b.interactable = usable;
                int index = i;
                b.onClick.AddListener(() => CardClicked?.Invoke(index));
                var hover = b.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => ApplyMessage($"{TourCards.Name(k)}：{TourCards.Description(k)}");
                hover.Exited += () => ApplyMessage(baseMessage);
            }
        }

        public RectTransform PieceOf(int index) => pieces[index];

        public void ShowRollButton(bool show, string label = "振る")
        {
            rollButton.gameObject.SetActive(show);
            rollLabel.text = label;
        }

        public void HideTray()
        {
            UIFactory.ClearChildren(trayRoot);
            ShowRollButton(false);
        }

        // ---- 演出 ----

        public IEnumerator PlayRoll(List<(DiceInstance die, int value)> rolls)
        {
            for (int i = 0; i < rollFaces.Length; i++) rollFaces[i].gameObject.SetActive(i < rolls.Count);
            var routines = new List<Coroutine>();
            for (int i = 0; i < rolls.Count && i < rollFaces.Length; i++)
            {
                rollFaces[i].GetComponent<RectTransform>().anchoredPosition = new Vector2(rolls.Count == 1 ? 0 : -80 + i * 160, 300);
                routines.Add(StartCoroutine(rollFaces[i].PlayRoll(rolls[i].die, rolls[i].value, 0.6f)));
            }
            foreach (var r in routines) yield return r;
            yield return UIAnim.Wait(0.2f);
        }

        /// <summary>
        /// 対決の演出：左に挑んだ人、右に挑まれた人。おたがいのダイスが一斉に転がり、合計が数え上がって、勝った人に光。
        /// waitForClick なら、押すまで（クリック・Space）止まる。
        /// </summary>
        public IEnumerator PlayDuel(TourDuelResult r, bool waitForClick)
        {
            // 飾りの付かない、ただの暗い幕（UIFactory.Panel は黒漆の枠などに飾られることがある）
            var shadeRect = UIFactory.Stretch("DuelShade", transform);
            var shade = shadeRect.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.7f);
            shade.raycastTarget = true;
            var title = UIFactory.Text("Title", shade.transform, "対決！", 72, new Color(1f, 0.45f, 0.3f), new Vector2(800, 100), new Vector2(0, 330));
            title.fontStyle = FontStyles.Bold;
            title.outlineWidth = 0.18f;
            title.outlineColor = new Color32(30, 10, 5, 255);
            StartCoroutine(UIAnim.Punch(title.transform, 0.4f, 0.35f));
            Sfx.Play(SoundId.Charge);

            var sides = new[] { (r.attacker, r.attackerDice, r.attackerRolls, r.attackerTotal, -420f), (r.defender, r.defenderDice, r.defenderRolls, r.defenderTotal, 420f) };
            var totals = new List<TextMeshProUGUI>();
            var routines = new List<Coroutine>();
            foreach (var (p, dice, rolls, total, x) in sides)
            {
                var name = UIFactory.Text("Name", shade.transform, p.name, 44, PlayerColors[p.index % PlayerColors.Length], new Vector2(600, 64), new Vector2(x, 200));
                name.fontStyle = FontStyles.Bold;
                name.outlineWidth = 0.18f;
                name.outlineColor = new Color32(30, 15, 5, 255);
                for (int i = 0; i < dice.Count; i++)
                {
                    var face = DiceFaceView.Create($"Die{i}", shade.transform, art, 110, new Vector2(x - (dice.Count - 1) * 65f + i * 130f, 40));
                    routines.Add(StartCoroutine(face.PlayRoll(dice[i], rolls[i].value, 0.8f + 0.15f * i)));
                }
                if (dice.Count == 0) UIFactory.Text("None", shade.transform, "振れるダイスがない", 30, PaperColor, new Vector2(500, 50), new Vector2(x, 40));
                var t = UIFactory.Text("Total", shade.transform, "", 64, Color.white, new Vector2(400, 90), new Vector2(x, -110));
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.2f;
                t.outlineColor = new Color32(30, 15, 5, 255);
                totals.Add(t);
            }
            var vs = UIFactory.Text("Vs", shade.transform, "VS", 56, GoldColor, new Vector2(200, 80), new Vector2(0, 40));
            vs.fontStyle = FontStyles.Bold;
            foreach (var c in routines) yield return c;

            // 合計を数え上げる（バラトロ風）
            int a = r.attackerTotal, b = r.defenderTotal;
            yield return UIAnim.Tween(0.6f, k =>
            {
                totals[0].text = Mathf.RoundToInt(a * k).ToString();
                totals[1].text = Mathf.RoundToInt(b * k).ToString();
            });
            totals[0].text = a.ToString();
            totals[1].text = b.ToString();
            Sfx.Play(SoundId.Hit);

            string verdict;
            if (r.winner == null) verdict = "引き分け！";
            else
            {
                int w = r.winner == r.attacker ? 0 : 1;
                totals[w].color = GoldColor;
                StartCoroutine(UIAnim.Punch(totals[w].transform, 0.5f, 0.4f));
                verdict = $"{r.winner.name} の勝ち！　{r.stolen} G を手に入れた";
                Sfx.Play(SoundId.Victory);
            }
            var result = UIFactory.Text("Verdict", shade.transform, verdict, 44, GoldColor, new Vector2(1400, 70), new Vector2(0, -240));
            result.fontStyle = FontStyles.Bold;
            result.outlineWidth = 0.18f;
            result.outlineColor = new Color32(30, 15, 5, 255);
            if (waitForClick)
            {
                var hint = UIFactory.Text("Hint", shade.transform, "クリックで続ける", 24, PaperColor, new Vector2(400, 40), new Vector2(0, -320));
                hint.raycastTarget = false;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                yield return null;
                while (!(mouse != null && mouse.leftButton.wasPressedThisFrame) && !(keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))) yield return null;
            }
            else
            {
                yield return UIAnim.Wait(1.8f);
            }
            Destroy(shade.gameObject);
        }

        public void HideRoll()
        {
            foreach (var f in rollFaces) f.gameObject.SetActive(false);
        }

        public IEnumerator PlayMove(TourPlayer p, List<TourNode> path)
        {
            var piece = pieces[p.index];
            foreach (var n in path)
            {
                FocusOn(n, false);
                yield return UIAnim.Hop(piece, P(n) + new Vector2(0, -18), 40f, 0.2f);
                Sfx.Play(SoundId.Step);
            }
            PlacePieces();
        }

        /// <summary>そのマスが画面のまん中あたりに来るようにスクロールする。</summary>
        public void FocusOn(TourNode n, bool smooth)
        {
            if (overview) ToggleOverview();
            var viewport = (RectTransform)scroll.viewport;
            var size = viewport.rect.size;
            var p = P(n);
            float x = Mathf.Clamp(-p.x + size.x * 0.42f, -(contentSize.x - size.x), 0f);
            float y = Mathf.Clamp(-p.y - size.y * 0.42f, 0f, contentSize.y - size.y);
            content.anchoredPosition = new Vector2(x, y);
        }

        public void ToggleOverview()
        {
            var viewport = (RectTransform)scroll.viewport;
            var size = viewport.rect.size;
            if (overview)
            {
                overview = false;
                content.localScale = Vector3.one;
                FocusOn(tour.Current.node, false);
                return;
            }
            overview = true;
            float s = Mathf.Min(size.x / contentSize.x, size.y / contentSize.y);
            content.localScale = new Vector3(s, s, 1f);
            content.anchoredPosition = Vector2.zero;
            Sfx.Play(SoundId.Button);
        }

        /// <summary>画面のまん中に大きな文字を少しのあいだ出す（「○○の番」など）。</summary>
        /// <summary>
        /// 決算の演出（桃鉄風）：それぞれの名物の収入を数え上げ、資産の順に並べる。
        /// incomes は席の順の収入、assets は資産を出す関数。
        /// </summary>
        public IEnumerator PlaySettlement(IReadOnlyList<int> incomes, Func<TourPlayer, int> assets, bool waitForClick)
        {
            var shadeRect = UIFactory.Stretch("SettlementShade", transform);
            var shade = shadeRect.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.7f);
            shade.raycastTarget = true;
            var title = UIFactory.Text("Title", shade.transform, "決　算", 80, GoldColor, new Vector2(800, 110), new Vector2(0, 360));
            title.fontStyle = FontStyles.Bold;
            title.outlineWidth = 0.18f;
            title.outlineColor = new Color32(30, 15, 5, 255);
            StartCoroutine(UIAnim.Punch(title.transform, 0.3f, 0.35f));
            UIFactory.Text("Sub", shade.transform, "名物の収入（町を独占していれば2倍）", 28, PaperColor, new Vector2(900, 44), new Vector2(0, 285));
            Sfx.Play(SoundId.Gold);

            // 資産の順（決算のあと）に並べる
            var order = tour.players.OrderByDescending(assets).ThenBy(p => p.index).ToList();
            var rows = new List<(TourPlayer p, TextMeshProUGUI income, TextMeshProUGUI total)>();
            for (int i = 0; i < order.Count; i++)
            {
                var p = order[i];
                float y = 170 - i * 120;
                var color = PlayerColors[p.index % PlayerColors.Length];
                var row = UIFactory.Panel($"Row{i}", shade.transform, new Vector2(1100, 100), new Vector2(0, y), new Color(0.12f, 0.08f, 0.06f, 0.95f));
                row.raycastTarget = false;
                UIFactory.Text("Rank", row.transform, $"{i + 1}位", 40, i == 0 ? GoldColor : PaperColor, new Vector2(120, 80), new Vector2(-480, 0)).fontStyle = FontStyles.Bold;
                var name = UIFactory.Text("Name", row.transform, p.name, 38, color, new Vector2(320, 80), new Vector2(-270, 0));
                name.fontStyle = FontStyles.Bold;
                var income = UIFactory.Text("Income", row.transform, "", 38, Color.white, new Vector2(300, 80), new Vector2(60, 0));
                var total = UIFactory.Text("Total", row.transform, "", 30, PaperColor, new Vector2(300, 80), new Vector2(380, 0));
                rows.Add((p, income, total));
                StartCoroutine(UIAnim.Punch(row.transform, 0.08f, 0.25f));
                yield return UIAnim.Wait(0.12f);
            }

            // 収入を数え上げる（バラトロ風）
            yield return UIAnim.Tween(0.8f, k =>
            {
                foreach (var (p, income, total) in rows)
                {
                    int gained = p.index < incomes.Count ? incomes[p.index] : 0;
                    income.text = gained > 0 ? $"+{Mathf.RoundToInt(gained * k)} G" : "収入なし";
                    total.text = $"資産 {assets(p) - gained + Mathf.RoundToInt(gained * k)} G";
                }
            });
            foreach (var (p, income, total) in rows)
            {
                int gained = p.index < incomes.Count ? incomes[p.index] : 0;
                income.text = gained > 0 ? $"+{gained} G" : "収入なし";
                if (gained > 0) income.color = GoldColor;
                total.text = $"資産 {assets(p)} G";
            }
            Sfx.Play(SoundId.Victory);

            if (waitForClick)
            {
                var hint = UIFactory.Text("Hint", shade.transform, "クリックで続ける", 24, PaperColor, new Vector2(400, 40), new Vector2(0, -400));
                hint.raycastTarget = false;
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                yield return null;
                while (!(mouse != null && mouse.leftButton.wasPressedThisFrame) && !(keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))) yield return null;
            }
            else
            {
                yield return UIAnim.Wait(2.2f);
            }
            Destroy(shade.gameObject);
        }

        public IEnumerator ShowBanner(string text, Color color)
        {
            var panel = UIFactory.Panel("Banner", transform, new Vector2(1920, 130), new Vector2(0, 120), new Color(0.05f, 0.03f, 0.02f, 0.85f));
            panel.raycastTarget = false;
            var label = UIFactory.Text("Text", panel.transform, text, 56, color, new Vector2(1800, 120), Vector2.zero);
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.16f;
            label.outlineColor = new Color32(20, 10, 5, 255);
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            yield return UIAnim.Tween(0.15f, t => group.alpha = t);
            yield return UIAnim.Wait(0.8f);
            yield return UIAnim.Tween(0.2f, t => group.alpha = 1f - t);
            Destroy(panel.gameObject);
        }

        // ---- マスを選ぶ ----

        HashSet<TourNode> choosing;
        TourNode chosen;

        public IEnumerator ChooseTile(IReadOnlyCollection<TourNode> options, Action<TourNode> onChosen, Func<TourNode, string> label = null)
        {
            chosen = null;
            choosing = new HashSet<TourNode>(options);
            foreach (var n in options)
            {
                glows[n].enabled = true;
                labels[n].text = label != null ? label(n) : "ここへ";
            }
            float t = 0f;
            while (chosen == null)
            {
                t += Time.unscaledDeltaTime;
                float a = 0.55f + 0.45f * Mathf.Sin(t * 6f);
                foreach (var n in choosing) glows[n].color = new Color(1f, 0.82f, 0.25f, a);
                yield return null;
            }
            foreach (var n in options)
            {
                glows[n].enabled = false;
                labels[n].text = "";
            }
            var result = chosen;
            choosing = null;
            onChosen(result);
        }

        void OnTileClicked(TourNode n)
        {
            if (choosing != null && choosing.Contains(n)) chosen = n;
        }

        /// <summary>テスト・自動操作用：マスを選んだことにする。</summary>
        public void ChooseTileForTest(TourNode n) => OnTileClicked(n);

        /// <summary>行けるマスを光らせる（ダイスにマウスを乗せたとき）。null なら消す。</summary>
        public void ShowReach(IEnumerable<TourNode> nodes)
        {
            if (choosing != null) return;
            foreach (var g in glows.Values) g.enabled = false;
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                glows[n].enabled = true;
                glows[n].color = new Color(1f, 0.82f, 0.25f, 0.8f);
            }
        }

        // ---- 小窓 ----

        RectTransform dialog;
        public bool DialogOpen => dialog != null;

        /// <summary>選択肢の小窓。選ばれた番号を onChosen に渡す。enabled が false の選択肢は押せない。</summary>
        public IEnumerator ShowDialog(string title, string body, IReadOnlyList<(string label, bool enabled)> options, Action<int> onChosen)
        {
            int choice = -1;
            var shadeRect = UIFactory.Stretch("DialogShade", transform);
            var shade = shadeRect.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.45f);
            shade.raycastTarget = true;
            dialog = shadeRect;
            float height = 230 + options.Count * 76;
            var box = UIFactory.Panel("Dialog", shade.transform, new Vector2(1000, height), new Vector2(0, 40), new Color(0.12f, 0.08f, 0.06f, 0.97f));
            UIFactory.Text("Title", box.transform, title, 40, GoldColor, new Vector2(920, 56), new Vector2(0, height / 2f - 50)).fontStyle = FontStyles.Bold;
            var bodyText = UIFactory.Text("Body", box.transform, body, 26, PaperColor, new Vector2(900, 110), new Vector2(0, height / 2f - 140));
            bodyText.enableAutoSizing = true;
            bodyText.fontSizeMin = 16;
            bodyText.fontSizeMax = 26;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var b = UIFactory.Button($"Option{i}", box.transform, new Vector2(820, 64), new Vector2(0, height / 2f - 230 - i * 76), new Color(0.93f, 0.87f, 0.72f), options[i].label, 26, out _);
                b.interactable = options[i].enabled;
                b.onClick.AddListener(() => choice = index);
            }
            while (choice < 0) yield return null;
            Destroy(shade.gameObject);
            dialog = null;
            onChosen(choice);
        }

        /// <summary>テスト・自動操作用：小窓の選択肢を選んだことにする。</summary>
        public void ChooseDialogForTest(int index)
        {
            if (dialog == null) return;
            var b = dialog.Find("Dialog/Option" + index);
            if (b != null) b.GetComponent<Button>().onClick.Invoke();
        }
    }
}
