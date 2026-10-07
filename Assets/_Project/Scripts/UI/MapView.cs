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

                var prob = UIFactory.Text("Probability", labels, "", 26, new Color(1f, 0.92f, 0.55f), new Vector2(ColumnWidth, 34), pos + new Vector2(0, -TileSize / 2f - 18));
                prob.fontStyle = FontStyles.Bold;
                prob.outlineWidth = 0.35f;
                prob.outlineColor = new Color32(90, 20, 10, 255);

                tiles[tile] = new TileWidget { glow = glow, probability = prob, rect = body.rectTransform, button = button };
            }

            // 駒（足元がマスの上端に来るように下端を基準にする）
            var playerImage = UIFactory.Picture("Player", content, art != null ? art.player : null, new Vector2(120, 120), Vector2.zero, new Color(0.4f, 0.75f, 1f));
            player = playerImage.rectTransform;
            player.anchorMin = player.anchorMax = new Vector2(0, 0.5f);
            player.pivot = new Vector2(0.5f, 0f);
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
                var c = choiceTiles.Contains(kv.Key) ? ChoiceColor : ReachColor;
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
        }

        Vector2 PlayerPositionOn(TileNode tile) => tiles[tile].rect.anchoredPosition + new Vector2(0, TileSize / 2f - 16);

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

        public void SetRemaining(int remaining) => remainingText.text = remaining > 0 ? $"あと {remaining} 歩" : "";

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var card in trayDice) card.Button.interactable = value && card.Die.state == DiceState.Available;
        }

        public void ShowReach(Dictionary<TileNode, float> reach)
        {
            ClearReach();
            foreach (var kv in reach)
            {
                var w = tiles[kv.Key];
                w.glow.enabled = true;
                w.probability.text = $"{Mathf.Min(1f, kv.Value) * 100f:0}%";
            }
            reachShown = true;
        }

        public void ClearReach()
        {
            foreach (var kv in tiles)
            {
                if (choiceTiles.Contains(kv.Key)) continue;
                kv.Value.glow.enabled = false;
                kv.Value.probability.text = "";
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
                tiles[t].probability.text = "ここへ";
            }
            while (chosenTile == null) yield return null;

            foreach (var t in options)
            {
                tiles[t].glow.enabled = false;
                tiles[t].button.interactable = false;
                tiles[t].probability.text = "";
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
        public IEnumerator PlayRoll(DiceInstance die, int value)
        {
            rollDie.gameObject.SetActive(true);
            yield return rollDie.PlayRoll(die, value, 0.6f);
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
            yield return UIAnim.Hop(player, PlayerPositionOn(tile), 46f, 0.2f);
            StartCoroutine(UIAnim.Punch(tiles[tile].rect, 0.12f, 0.15f));
        }

        /// <summary>駒を1マスずつ跳ねさせて進める（分岐のない移動）。</summary>
        public IEnumerator PlayMove(IEnumerable<TileNode> path)
        {
            foreach (var tile in path) yield return PlayHop(tile);
        }

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

                var card = DiceCard.Create($"Dice{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0),
                    available ? "クリックで振る" : "使用済み", !available, false);
                card.Button.interactable = interactable && available;

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
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
