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
    /// マップ画面。盤面・自分の駒・ポーチのダイスを表示し、ダイスにマウスを乗せると止まりうるマスと確率を光らせる。
    /// 表示・演出とクリックの通知だけを行い、ルールは RunState に任せる。
    /// </summary>
    public class MapView : MonoBehaviour
    {
        const float TileSize = 80f;
        const float TileGap = 8f;
        const float BoardY = 40f;

        static readonly Color ReachColor = new Color(1f, 0.82f, 0.25f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.1f, 0.07f, 0.05f, 0.72f);

        public event Action<DiceInstance> DiceHovered;
        public event Action DiceUnhovered;
        public event Action<DiceInstance> DiceClicked;

        class TileWidget
        {
            public Image glow;
            public TextMeshProUGUI probability;
            public RectTransform rect;
        }

        UIArt art;
        readonly Dictionary<TileNode, TileWidget> tiles = new Dictionary<TileNode, TileWidget>();
        readonly List<DiceCard> trayDice = new List<DiceCard>();
        TextMeshProUGUI statusText;
        TextMeshProUGUI messageText;
        TextMeshProUGUI refreshText;
        RectTransform player;
        RectTransform trayRoot;
        DiceFaceView rollDie;
        bool interactable = true;
        bool reachShown;

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

            // マスをつなぐ道
            float width = board.tiles.Count * (TileSize + TileGap) - TileGap;
            UIFactory.Panel("Road", transform, new Vector2(width, 18), new Vector2(0, BoardY), new Color(0.55f, 0.4f, 0.25f, 0.85f));

            float left = -width / 2f + TileSize / 2f;
            foreach (var tile in board.tiles)
            {
                var pos = new Vector2(left + tile.id * (TileSize + TileGap), BoardY);
                var glow = UIFactory.Panel($"Glow{tile.id}", transform, Vector2.one * (TileSize + 14), pos, ReachColor);
                glow.enabled = false;
                var body = UIFactory.Picture($"Tile{tile.id}", transform, art != null ? art.TileSprite(tile) : null, Vector2.one * TileSize, pos, FallbackTileColor(tile));
                if (art == null || art.TileSprite(tile) == null)
                {
                    UIFactory.Text("Label", body.transform, TileLabel(tile), 30, Color.black, Vector2.one * TileSize, Vector2.zero);
                }
                UIFactory.Text("Id", transform, tile.id.ToString(), 18, InkColor, new Vector2(TileSize, 22), pos + new Vector2(0, -TileSize / 2f - 12));
                var prob = UIFactory.Text("Probability", transform, "", 28, new Color(1f, 0.92f, 0.55f), new Vector2(TileSize + TileGap, 36), pos + new Vector2(0, -TileSize / 2f - 42));
                prob.fontStyle = FontStyles.Bold;
                prob.outlineWidth = 0.35f;
                prob.outlineColor = new Color32(90, 20, 10, 255);
                tiles[tile] = new TileWidget { glow = glow, probability = prob, rect = body.rectTransform };
            }

            // 駒（足元がマスの上端に来るように下端を基準にする）
            var playerImage = UIFactory.Picture("Player", transform, art != null ? art.player : null, new Vector2(130, 130), Vector2.zero, new Color(0.4f, 0.75f, 1f));
            player = playerImage.rectTransform;
            player.pivot = new Vector2(0.5f, 0f);

            // 移動の出目を見せるダイス
            rollDie = DiceFaceView.Create("RollDie", transform, art, 120, new Vector2(0, 300));
            rollDie.gameObject.SetActive(false);

            var messagePanel = UIFactory.Panel("MessagePanel", transform, new Vector2(1500, 90), new Vector2(0, -150), ShadeColor);
            messageText = UIFactory.Text("Message", messagePanel.transform, "", 28, PaperColor, new Vector2(1460, 84), Vector2.zero);
            trayRoot = UIFactory.Rect("DiceTray", transform, new Vector2(1800, 170), new Vector2(0, -375));

            rollDie.transform.SetAsLastSibling();
        }

        void Update()
        {
            if (!reachShown) return;
            // 止まりうるマスをゆっくり明滅させる
            float a = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
            foreach (var w in tiles.Values)
            {
                if (w.glow.enabled) w.glow.color = new Color(ReachColor.r, ReachColor.g, ReachColor.b, a);
            }
        }

        // ---- 表示の更新 ----

        public void Refresh(RunState run)
        {
            RefreshStatus(run);
            SetPlayerTile(run.Current);
            RefreshTray(run.pouch);
        }

        public void RefreshStatus(RunState run)
        {
            statusText.text = $"HP {run.player.hp}/{run.player.maxHp}　　ターン {run.Turn}　　ボスまで残り {run.TilesToGoal} マス";
            int available = run.pouch.AvailableCount;
            refreshText.text = $"使用可能 {available} 個（あと {available} 個使うとリフレッシュ）";
        }

        public void SetPlayerTile(TileNode tile)
        {
            player.anchoredPosition = PlayerPositionOn(tile);
        }

        Vector2 PlayerPositionOn(TileNode tile) => tiles[tile].rect.anchoredPosition + new Vector2(0, TileSize / 2f - 18);

        public void SetMessage(string message) => messageText.text = message;

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
                w.probability.text = $"{kv.Value * 100f:0}%";
            }
            reachShown = true;
        }

        public void ClearReach()
        {
            foreach (var w in tiles.Values)
            {
                w.glow.enabled = false;
                w.probability.text = "";
            }
            reachShown = false;
        }

        // ---- 演出 ----

        /// <summary>移動の出目を転がして見せる。</summary>
        public IEnumerator PlayRoll(DiceInstance die, int value)
        {
            rollDie.gameObject.SetActive(true);
            yield return rollDie.PlayRoll(die, value, 0.6f);
            yield return UIAnim.Wait(0.15f);
        }

        public void HideRoll() => rollDie.gameObject.SetActive(false);

        /// <summary>駒を1マスずつ跳ねさせて進める。</summary>
        public IEnumerator PlayMove(IEnumerable<TileNode> path)
        {
            foreach (var tile in path)
            {
                yield return UIAnim.Hop(player, PlayerPositionOn(tile), 46f, 0.2f);
                StartCoroutine(UIAnim.Punch(tiles[tile].rect, 0.12f, 0.15f));
            }
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
                default: return new Color(0.8f, 0.8f, 0.8f);
            }
        }
    }
}
