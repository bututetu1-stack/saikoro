using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// マップ画面。分岐する盤面を横スクロールで表示し、駒・ポーチのダイスを並べる。
    /// ダイスにマウスを乗せると止まりうるマスと確率を光らせる（分岐をまたぐときは両方の道）。
    /// 分かれ道では、進む先のマスをクリックして道を選ぶ。
    /// 表示・演出とクリックの通知だけを行い、ルールは RunState に任せる。
    /// </summary>
    public class MapView : MonoBehaviour
    {
        const float TileSize = 72f;
        const float ColumnWidth = 92f;     // 盤面の x 1つぶんの幅
        const float LaneHeight = 125f;     // 道の段の間隔
        const float BoardMarginX = 140f;
        const float ViewportHeight = 620f;
        const float ViewportY = 80f;
        const float BoardOffsetY = -40f;   // 駒の絵が上の段からはみ出さないよう、盤面を少し下げる
        const float FollowX = 560f;        // 駒を画面の左から何 px に置くか

        static readonly Color ReachColor = new Color(1f, 0.82f, 0.25f);
        static readonly Color ChoiceColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.1f, 0.07f, 0.05f, 0.72f);
        static readonly Color RoadColor = new Color(0.55f, 0.4f, 0.25f, 0.85f);

        public event Action<DiceInstance> DiceHovered;
        public event Action DiceUnhovered;
        public event Action<DiceInstance> DiceClicked;

        class TileWidget
        {
            public Image glow;
            public TextMeshProUGUI probability;
            public Image probabilityBack;
            public RectTransform rect;
            public Button button;
        }

        UIArt art;
        readonly Dictionary<TileNode, TileWidget> tiles = new Dictionary<TileNode, TileWidget>();
        readonly List<DiceCard> trayDice = new List<DiceCard>();
        TextMeshProUGUI statusText;
        TextMeshProUGUI messageText;
        TextMeshProUGUI refreshText;
        TextMeshProUGUI remainingText;
        ScrollRect scroll;
        RectTransform content;
        float contentWidth;
        RectTransform player;
        RectTransform trayRoot;
        DiceFaceView rollDie;
        bool interactable = true;
        bool reachShown;
        HashSet<TileNode> choiceTiles = new HashSet<TileNode>();
        TileNode chosenTile;
        Coroutine follow;

        public static MapView Create(Transform canvas, BoardData board, UIArt art)
        {
            var root = UIFactory.Stretch("MapView", canvas);
            var view = root.gameObject.AddComponent<MapView>();
            view.art = art;
            view.Build(board);
            return view;
        }

        void Build(BoardData board)
        {
            UIFactory.Background(transform, art != null ? art.mapBackground : null, new Color(0.85f, 0.8f, 0.65f));

            var bar = UIFactory.Panel("StatusBar", transform, new Vector2(1920, 70), new Vector2(0, 505), ShadeColor);
            statusText = UIFactory.Text("Status", bar.transform, "", 34, PaperColor, new Vector2(1800, 60), Vector2.zero, TextAlignmentOptions.Left);
            refreshText = UIFactory.Text("RefreshInfo", bar.transform, "", 30, new Color(1f, 0.85f, 0.45f), new Vector2(1800, 60), Vector2.zero, TextAlignmentOptions.Right);

            BuildBoard(board);

            rollDie = DiceFaceView.Create("RollDie", transform, art, 120, new Vector2(0, 330));
            rollDie.gameObject.SetActive(false);
            remainingText = UIFactory.Text("Remaining", transform, "", 34, InkColor, new Vector2(300, 50), new Vector2(170, 330));
            remainingText.fontStyle = FontStyles.Bold;
            remainingText.outlineWidth = 0.2f;
            remainingText.outlineColor = new Color32(250, 240, 220, 255);

            var messagePanel = UIFactory.Panel("MessagePanel", transform, new Vector2(1500, 84), new Vector2(0, -282), ShadeColor);
            messageText = UIFactory.Text("Message", messagePanel.transform, "", 28, PaperColor, new Vector2(1460, 80), Vector2.zero);
            trayRoot = UIFactory.Rect("DiceTray", transform, new Vector2(1800, 160), new Vector2(0, -425));
        }

        /// <summary>盤面：横にスクロールできる枠の中に、道・マス・確率・駒を並べる。</summary>
        void BuildBoard(BoardData board)
        {
            var viewport = UIFactory.Rect("BoardViewport", transform, new Vector2(1920, ViewportHeight), new Vector2(0, ViewportY));
            viewport.gameObject.AddComponent<RectMask2D>();
            // スクロールの操作を受けるための透明な面
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            float maxX = board.tiles.Max(t => t.position.x);
            contentWidth = Mathf.Max(1920f, BoardMarginX * 2 + maxX * ColumnWidth);
            content = UIFactory.Rect("Content", viewport, new Vector2(contentWidth, ViewportHeight), Vector2.zero);
            content.anchorMin = content.anchorMax = new Vector2(0, 0.5f);
            content.pivot = new Vector2(0, 0.5f);
            content.anchoredPosition = Vector2.zero;

            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 60f;
            scroll.inertia = true;

            var roads = UIFactory.Rect("Roads", content, Vector2.zero, Vector2.zero);
            var glows = UIFactory.Rect("Glows", content, Vector2.zero, Vector2.zero);
            var bodies = UIFactory.Rect("Tiles", content, Vector2.zero, Vector2.zero);
            var labels = UIFactory.Rect("Labels", content, Vector2.zero, Vector2.zero);
            foreach (var layer in new[] { roads, glows, bodies, labels })
            {
                layer.anchorMin = layer.anchorMax = layer.pivot = new Vector2(0, 0.5f);
            }

            foreach (var tile in board.tiles)
            {
                var pos = TilePosition(tile);
                foreach (var next in tile.next) Road(roads, pos, TilePosition(next));

                var glow = UIFactory.Panel($"Glow{tile.id}", glows, Vector2.one * (TileSize + 16), pos, ReachColor);
                glow.enabled = false;

                var sprite = art != null ? art.TileSprite(tile) : null;
                var body = UIFactory.Picture($"Tile{tile.id}", bodies, sprite, Vector2.one * TileSize, pos, FallbackTileColor(tile));
                body.raycastTarget = true;
                if (sprite == null)
                {
                    UIFactory.Text("Label", body.transform, TileLabel(tile), 28, Color.black, Vector2.one * TileSize, Vector2.zero);
                }
                var button = body.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.interactable = false;
                var captured = tile;
                button.onClick.AddListener(() => OnTileClicked(captured));

                // 確率や「ここへ」の札：背景と似た色だと見づらいので、濃い地に白い太字
                var probBack = UIFactory.Panel("ProbabilityBack", labels, new Vector2(ColumnWidth - 4, 34), pos + new Vector2(0, -TileSize / 2f - 20), new Color(0.35f, 0.08f, 0.05f, 0.92f));
                probBack.raycastTarget = false;
                var prob = UIFactory.Text("Probability", probBack.transform, "", 26, Color.white, new Vector2(ColumnWidth, 34), Vector2.zero);
                prob.fontStyle = FontStyles.Bold;
                probBack.gameObject.SetActive(false);

                var hover = body.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => ShowTileInfo(captured);
                hover.Exited += HideTileInfo;

                tiles[tile] = new TileWidget { glow = glow, probability = prob, probabilityBack = probBack, rect = body.rectTransform, button = button };
            }

            // 駒（足元がマスの上端に来るように下端を基準にする）
            var playerImage = UIFactory.Picture("Player", content, art != null ? art.player : null, new Vector2(120, 120), Vector2.zero, new Color(0.4f, 0.75f, 1f));
            player = playerImage.rectTransform;
            player.anchorMin = player.anchorMax = new Vector2(0, 0.5f);
            player.pivot = new Vector2(0.5f, 0f);
        }

        static void SetLabel(TileWidget w, string text)
        {
            w.probability.text = text;
            w.probabilityBack.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        // ---- マスの説明（マウスを乗せたとき） ----

        RectTransform tileInfo;
        TextMeshProUGUI tileInfoText;
        TileNode currentTile;
        Dictionary<TileNode, int> distanceFromCurrent = new Dictionary<TileNode, int>();

        void ShowTileInfo(TileNode tile)
        {
            if (tileInfo == null)
            {
                tileInfo = UIFactory.Panel("TileInfo", content, new Vector2(380, 130), Vector2.zero, new Color(0.1f, 0.07f, 0.05f, 0.95f)).rectTransform;
                tileInfo.anchorMin = tileInfo.anchorMax = new Vector2(0, 0.5f);
                tileInfo.GetComponent<Image>().raycastTarget = false;
                tileInfoText = UIFactory.Text("Text", tileInfo, "", 22, PaperColor, new Vector2(360, 120), Vector2.zero, TextAlignmentOptions.TopLeft);
            }
            string distance;
            if (tile == currentTile) distance = "いまいるマス";
            else if (distanceFromCurrent.TryGetValue(tile, out int d)) distance = $"ここから最短 {d} マス";
            else distance = "もう行けない（通り過ぎた・別の道）";

            tileInfoText.text = $"<b><size=28>{TileName(tile)}</size></b>　<color=#FFD24D>{distance}</color>\n{TileDescription(tile)}";
            var pos = tiles[tile].rect.anchoredPosition;
            // 上の段では下に、それ以外は上に出す（盤面の外にはみ出さないように）
            float dy = pos.y > 20 ? -125 : 125;
            tileInfo.anchoredPosition = pos + new Vector2(0, dy);
            tileInfo.gameObject.SetActive(true);
            tileInfo.SetAsLastSibling();
        }

        void HideTileInfo()
        {
            if (tileInfo != null) tileInfo.gameObject.SetActive(false);
        }

        /// <summary>今いるマスから前向きにたどった最短の歩数を求め直す。</summary>
        void ComputeDistances(TileNode from)
        {
            currentTile = from;
            distanceFromCurrent = new Dictionary<TileNode, int> { [from] = 0 };
            var queue = new Queue<TileNode>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var next in n.next)
                {
                    if (distanceFromCurrent.ContainsKey(next)) continue;
                    distanceFromCurrent[next] = distanceFromCurrent[n] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        public static string TileName(TileNode tile)
        {
            if (tile.id == 0) return "スタート";
            switch (tile.type)
            {
                case TileType.Battle: return "戦闘";
                case TileType.Rest: return "休憩";
                case TileType.Boss: return "ボス";
                case TileType.Event: return "イベント";
                case TileType.Trap: return "罠";
                case TileType.Treasure: return "宝箱";
                case TileType.Shop: return "ショップ";
                case TileType.Forge: return "鍛冶";
                case TileType.Elite: return "強敵";
                case TileType.Shrine: return "祠";
                case TileType.Checkpoint: return "関所";
                case TileType.Teahouse: return "茶屋";
                case TileType.DiceHall: return "賽場";
                default: return "空白";
            }
        }

        public static string TileDescription(TileNode tile)
        {
            if (tile.id == 0) return "旅の始まり。";
            switch (tile.type)
            {
                case TileType.Battle: return "敵と戦う。勝つとゴールドとダイスがもらえる。";
                case TileType.Rest: return "休む（HP 回復）か、鍛える（刻印を付ける）。";
                case TileType.Boss: return "層の最後。出目が余っても必ず止まる。";
                case TileType.Event: return "何かが起きる。";
                case TileType.Trap: return "ダメージ・封印・呪いのどれか。";
                case TileType.Treasure: return "ゴールドかレリック。まれにダイスも。";
                case TileType.Shop: return "ダイス・レリック・刻印を買う。ダイスの削除も。";
                case TileType.Forge: return "刻印を1つ付ける。";
                case TileType.Elite: return "強敵と戦う。勝つとレリックが確定。";
                case TileType.Shrine: return "通るだけで5G。止まると2倍。";
                case TileType.Checkpoint: return "通るだけで10G払う（払えなければ5ダメージ）。止まると2倍。";
                case TileType.Teahouse: return "通るだけでHP3回復。止まると2倍。";
                case TileType.DiceHall: return "通るだけで使用済みのダイスが1個戻る。止まると2倍。";
                default: return "何も起きない。";
            }
        }

        static Vector2 TilePosition(TileNode tile)
        {
            return new Vector2(BoardMarginX + tile.position.x * ColumnWidth, tile.position.y * LaneHeight + BoardOffsetY);
        }

        static void Road(RectTransform parent, Vector2 from, Vector2 to)
        {
            var road = UIFactory.Panel("Road", parent, new Vector2(Vector2.Distance(from, to), 14), from, RoadColor);
            road.raycastTarget = false;
            var rt = road.rectTransform;
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = from;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
        }

        void Update()
        {
            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var kv in tiles)
            {
                var w = kv.Value;
                if (!w.glow.enabled) continue;
                var c = choiceTiles.Contains(kv.Key) ? ChoiceColor : destinationTiles.Contains(kv.Key) ? DestinationColor : ReachColor;
                w.glow.color = new Color(c.r, c.g, c.b, (reachShown || choiceTiles.Count > 0) ? a : 1f);
            }
        }

        // ---- 表示の更新 ----

        public void Refresh(RunState run)
        {
            RefreshStatus(run);
            SetPlayerTile(run.Current);
            ScrollTo(run.Current, false);
            RefreshTray(run.pouch);
            SetSkipTurn(run.MustSkipTurn);
        }

        public void RefreshStatus(RunState run)
        {
            statusText.text = $"HP {run.player.hp}/{run.player.maxHp}　　{run.Gold} G　　ターン {run.Turn}　　ボスまで最短 {run.TilesToGoal} マス";
            int available = run.pouch.AvailableCount;
            refreshText.text = $"使用可能 {available} 個（あと {available} 個使うとリフレッシュ）";
        }

        public void SetPlayerTile(TileNode tile)
        {
            player.anchoredPosition = PlayerPositionOn(tile);
            ComputeDistances(tile);
        }

        // 駒の足元がマスの中ほどに来るように（マスの上端に乗せると宙に浮いて見えるため）
        Vector2 PlayerPositionOn(TileNode tile) => tiles[tile].rect.anchoredPosition + new Vector2(0, -TileSize * 0.22f);

        /// <summary>駒が画面の左寄りに来るように盤面をスクロールする。</summary>
        public void ScrollTo(TileNode tile, bool smooth)
        {
            float target = Mathf.Clamp(FollowX - tiles[tile].rect.anchoredPosition.x, 1920f - contentWidth, 0f);
            if (follow != null) StopCoroutine(follow);
            if (!smooth || !isActiveAndEnabled)
            {
                content.anchoredPosition = new Vector2(target, 0);
                return;
            }
            scroll.StopMovement();
            follow = StartCoroutine(UIAnim.MoveTo(content, new Vector2(target, 0), 0.25f));
        }

        public void SetMessage(string message) => messageText.text = message;

        public event Action SkipTurnClicked;
        Button skipButton;

        /// <summary>移動に使えるダイスがないとき（大賽だけなど）に「1回休み」ボタンを出す。</summary>
        public void SetSkipTurn(bool show)
        {
            if (skipButton == null)
            {
                skipButton = UIFactory.Button("SkipTurnButton", transform, new Vector2(300, 80), new Vector2(760, -200),
                    new Color(1f, 0.78f, 0.3f), "1回休み", 32, out _);
                skipButton.onClick.AddListener(() => SkipTurnClicked?.Invoke());
            }
            skipButton.gameObject.SetActive(show);
            skipButton.interactable = interactable;
        }

        public void SetRemaining(int remaining) => remainingText.text = remaining > 0 ? $"あと {remaining} 歩" : "";

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var card in trayDice) card.Button.interactable = value && card.Die.state == DiceState.Available && RunState.CanMoveWith(card.Die);
            if (skipButton != null) skipButton.interactable = value;
        }

        public void ShowReach(Dictionary<TileNode, float> reach)
        {
            ClearReach();
            foreach (var kv in reach)
            {
                var w = tiles[kv.Key];
                w.glow.enabled = true;
                SetLabel(w, $"{Mathf.Min(1f, kv.Value) * 100f:0}%");
            }
            reachShown = true;
        }

        static readonly Color DestinationColor = new Color(1f, 0.45f, 0.25f);
        readonly HashSet<TileNode> destinationTiles = new HashSet<TileNode>();

        /// <summary>出目が決まったあと、行き先のマスを光らせる（分岐の先が決まっていなければ候補すべて）。</summary>
        public void ShowDestinations(IEnumerable<TileNode> destinations)
        {
            ClearReach();
            foreach (var t in destinations)
            {
                destinationTiles.Add(t);
                tiles[t].glow.enabled = true;
                SetLabel(tiles[t], "ここへ");
            }
            reachShown = true;
        }

        public void ClearReach()
        {
            destinationTiles.Clear();
            foreach (var kv in tiles)
            {
                if (choiceTiles.Contains(kv.Key)) continue;
                kv.Value.glow.enabled = false;
                SetLabel(kv.Value, "");
            }
            reachShown = false;
        }

        // ---- 分かれ道の選択 ----

        /// <summary>options のマスを光らせ、どれかがクリックされるまで待つ。選ばれたマスを onChosen に渡す。</summary>
        public IEnumerator ChooseBranch(IReadOnlyList<TileNode> options, Action<TileNode> onChosen)
        {
            ClearReach();
            chosenTile = null;
            choiceTiles = new HashSet<TileNode>(options);
            foreach (var t in options)
            {
                tiles[t].glow.enabled = true;
                tiles[t].button.interactable = true;
                SetLabel(tiles[t], "ここへ");
            }
            while (chosenTile == null) yield return null;

            foreach (var t in options)
            {
                tiles[t].glow.enabled = false;
                tiles[t].button.interactable = false;
                SetLabel(tiles[t], "");
            }
            choiceTiles.Clear();
            onChosen(chosenTile);
        }

        void OnTileClicked(TileNode tile)
        {
            if (choiceTiles.Contains(tile)) chosenTile = tile;
        }

        /// <summary>テスト・自動操作用：分かれ道で tile を選んだことにする。</summary>
        public void ChooseTileForTest(TileNode tile) => OnTileClicked(tile);

        // ---- 演出 ----

        /// <summary>移動の出目を転がして見せる。</summary>
        public IEnumerator PlayRoll(DiceInstance die, int value, Effects.EngravingData engraving = null)
        {
            rollDie.gameObject.SetActive(true);
            yield return rollDie.PlayRoll(die, value, 0.6f, engraving);
            yield return UIAnim.Wait(0.15f);
        }

        public void HideRoll()
        {
            rollDie.gameObject.SetActive(false);
            SetRemaining(0);
        }

        /// <summary>駒を1マス跳ねさせ、盤面を駒に合わせてスクロールする。</summary>
        public IEnumerator PlayHop(TileNode tile)
        {
            ScrollTo(tile, true);
            yield return UIAnim.Hop(player, PlayerPositionOn(tile), 46f, 0.26f);
            ComputeDistances(tile);
            StartCoroutine(UIAnim.Punch(tiles[tile].rect, 0.12f, 0.15f));
            yield return UIAnim.Wait(0.04f);
        }

        // ---- 進む数を選ぶ（刻印「風」など） ----

        RectTransform valueBar;
        int chosenValue = -1;

        /// <summary>
        /// 画面下の帯に進む数のボタンを並べ、選ばれるまで待つ（盤面を隠さないよう小窓は使わない）。
        /// ボタンにマウスを乗せると、その数で止まるマスが光る。
        /// </summary>
        public IEnumerator ChooseValue(string title, IReadOnlyList<int> values, int current,
            Func<int, IEnumerable<TileNode>> destinationsFor, Action<int> onChosen)
        {
            chosenValue = -1;
            valueBar = UIFactory.Panel("ValueBar", transform, new Vector2(1500, 84), new Vector2(0, -282), new Color(0.12f, 0.08f, 0.06f, 0.95f)).rectTransform;
            UIFactory.Text("Title", valueBar, title, 28, new Color(1f, 0.82f, 0.3f), new Vector2(420, 80), new Vector2(-520, 0), TextAlignmentOptions.Left);
            const float w = 220f, gap = 20f;
            float left = -(values.Count * (w + gap) - gap) / 2f + w / 2f + 160f;
            for (int i = 0; i < values.Count; i++)
            {
                int v = values[i];
                var b = UIFactory.Button($"Value{v}", valueBar, new Vector2(w, 64), new Vector2(left + i * (w + gap), 0),
                    v == current ? new Color(1f, 0.85f, 0.5f) : new Color(0.93f, 0.87f, 0.72f), v == current ? $"{v}（そのまま）" : $"{v} 歩", 28, out _);
                b.onClick.AddListener(() => chosenValue = v);
                var hover = b.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => ShowDestinations(destinationsFor(v));
            }
            ShowDestinations(destinationsFor(current));

            while (chosenValue < 0) yield return null;
            valueBar.gameObject.SetActive(false);
            Destroy(valueBar.gameObject);
            onChosen(chosenValue);
        }

        /// <summary>テスト・自動操作用：進む数を選んだことにする。</summary>
        public void ChooseValueForTest(int value) => chosenValue = value;

        /// <summary>駒を1マスずつ跳ねさせて進める（分岐のない移動）。</summary>
        public IEnumerator PlayMove(IEnumerable<TileNode> path)
        {
            foreach (var tile in path) yield return PlayHop(tile);
        }

        /// <summary>マスの上に文字を浮かべる（通過マスの効果など）。</summary>
        public void PopupAtTile(TileNode tile, string text, Color color)
        {
            var pos = tiles[tile].rect.anchoredPosition + new Vector2(0, 150);
            var label = UIFactory.Text("Popup", content, text, 30, color, new Vector2(420, 50), pos);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 0.5f);
            label.rectTransform.anchoredPosition = pos;
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.3f;
            label.outlineColor = new Color32(40, 20, 10, 255);
            StartCoroutine(PopupRoutine(label, pos));
        }

        IEnumerator PopupRoutine(TextMeshProUGUI label, Vector2 pos)
        {
            var baseColor = label.color;
            yield return UIAnim.Tween(1.2f, t =>
            {
                label.rectTransform.anchoredPosition = pos + new Vector2(0, 50f * UIAnim.EaseOutQuad(t));
                label.color = new Color(baseColor.r, baseColor.g, baseColor.b, t < 0.6f ? 1f : (1f - t) / 0.4f);
            });
            Destroy(label.gameObject);
        }

        public IEnumerator ShakeBoard()
        {
            yield return UIAnim.Shake((RectTransform)transform, 12f, 0.3f);
        }

        // ---- 選択肢のある小窓（休憩・宝箱・イベントなど） ----

        public struct DialogOption
        {
            public string label;
            public bool enabled;

            public DialogOption(string label, bool enabled = true)
            {
                this.label = label;
                this.enabled = enabled;
            }
        }

        RectTransform dialog;
        int dialogChoice = -1;

        /// <summary>小窓を出し、どれかのボタンが押されるまで待つ。押されたボタンの番号を onChosen に渡す。</summary>
        public IEnumerator ShowDialog(string title, string body, IReadOnlyList<DialogOption> options, Action<int> onChosen)
        {
            CloseDialog();
            dialogChoice = -1;
            dialog = UIFactory.Stretch("Dialog", transform);
            UIFactory.Panel("Shade", dialog, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.45f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", dialog, new Vector2(1000, 440), new Vector2(0, 40), new Color(0.12f, 0.08f, 0.06f, 1f));
            UIFactory.Text("Title", box.transform, title, 44, new Color(1f, 0.82f, 0.3f), new Vector2(940, 70), new Vector2(0, 165)).fontStyle = FontStyles.Bold;
            UIFactory.Text("Body", box.transform, body, 30, PaperColor, new Vector2(920, 180), new Vector2(0, 40));

            const float w = 300f, gap = 30f;
            float left = -(options.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var button = UIFactory.Button($"Option{i}", box.transform, new Vector2(w, 90), new Vector2(left + i * (w + gap), -140),
                    options[i].enabled ? new Color(0.93f, 0.87f, 0.72f) : new Color(0.45f, 0.42f, 0.38f), options[i].label, 26, out _);
                button.interactable = options[i].enabled;
                button.onClick.AddListener(() => dialogChoice = index);
            }
            StartCoroutine(UIAnim.Punch(box.transform, 0.08f, 0.25f));

            while (dialogChoice < 0) yield return null;
            int chosen = dialogChoice;
            CloseDialog();
            onChosen(chosen);
        }

        void CloseDialog()
        {
            if (dialog == null) return;
            dialog.gameObject.SetActive(false);
            Destroy(dialog.gameObject);
            dialog = null;
        }

        /// <summary>テスト・自動操作用：小窓の index 番目のボタンを押したことにする。</summary>
        public void ChooseDialogForTest(int index) => dialogChoice = index;

        /// <summary>リフレッシュ：ダイスが一斉に光る。</summary>
        public IEnumerator PlayRefresh()
        {
            foreach (var card in trayDice) StartCoroutine(card.PlayFlash());
            yield return UIAnim.Wait(0.5f);
        }

        // ---- ダイスのトレイ ----

        public void RefreshTray(DicePouch pouch)
        {
            UIFactory.ClearChildren(trayRoot);
            trayDice.Clear();

            // 使用可能を左、使用済みを右に寄せる
            var ordered = pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float w = 300f, h = 160f, gap = 24f;
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool movable = RunState.CanMoveWith(die);

                string state = !available ? (die.state == DiceState.Sealed ? "封印中" : "使用済み") : movable ? "クリックで振る" : "戦闘専用（移動に使えない）";
                var card = DiceCard.Create($"Dice{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0),
                    state, !available || !movable, false);
                card.Button.interactable = interactable && available && movable;

                card.Button.onClick.AddListener(() => DiceClicked?.Invoke(die));
                var hover = card.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => DiceHovered?.Invoke(die);
                hover.Exited += () => DiceUnhovered?.Invoke();

                trayDice.Add(card);
            }
        }

        public static string TileLabel(TileNode tile)
        {
            if (tile.id == 0) return "始";
            switch (tile.type)
            {
                case TileType.Battle: return "戦";
                case TileType.Rest: return "休";
                case TileType.Boss: return "ボス";
                case TileType.Event: return "？";
                case TileType.Trap: return "罠";
                case TileType.Treasure: return "宝";
                case TileType.Shop: return "店";
                case TileType.Forge: return "鍛";
                case TileType.Elite: return "強";
                case TileType.Shrine: return "祠";
                case TileType.Checkpoint: return "関";
                case TileType.Teahouse: return "茶";
                case TileType.DiceHall: return "賽";
                default: return "空";
            }
        }

        static Color FallbackTileColor(TileNode tile)
        {
            if (tile.id == 0) return new Color(0.6f, 0.8f, 0.95f);
            switch (tile.type)
            {
                case TileType.Battle: return new Color(0.9f, 0.45f, 0.45f);
                case TileType.Rest: return new Color(0.5f, 0.85f, 0.55f);
                case TileType.Boss: return new Color(0.7f, 0.45f, 0.9f);
                case TileType.Event: return new Color(0.95f, 0.85f, 0.4f);
                case TileType.Trap: return new Color(0.45f, 0.35f, 0.3f);
                case TileType.Treasure: return new Color(0.95f, 0.7f, 0.3f);
                case TileType.Shop: return new Color(0.4f, 0.65f, 0.9f);
                case TileType.Forge: return new Color(0.75f, 0.5f, 0.35f);
                case TileType.Elite: return new Color(0.85f, 0.25f, 0.25f);
                case TileType.Shrine: return new Color(0.9f, 0.4f, 0.25f);
                case TileType.Checkpoint: return new Color(0.4f, 0.4f, 0.45f);
                case TileType.Teahouse: return new Color(0.6f, 0.8f, 0.45f);
                case TileType.DiceHall: return new Color(0.95f, 0.92f, 0.8f);
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
