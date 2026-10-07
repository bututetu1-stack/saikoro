using System;
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
    /// マップ画面。盤面・自分の位置・ポーチのダイスを表示し、ダイスにマウスを乗せると止まりうるマスと確率を光らせる。
    /// 表示とクリックの通知だけを行い、ルールは RunState に任せる。
    /// </summary>
    public class MapView : MonoBehaviour
    {
        const float TileSize = 76f;
        const float TileGap = 12f;
        const float BoardY = 60f;

        static readonly Color ReachColor = new Color(1f, 0.85f, 0.2f);
        static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f);

        public event Action<DiceInstance> DiceHovered;
        public event Action DiceUnhovered;
        public event Action<DiceInstance> DiceClicked;

        class TileWidget
        {
            public Image highlight;
            public TextMeshProUGUI probability;
            public RectTransform rect;
        }

        readonly Dictionary<TileNode, TileWidget> tiles = new Dictionary<TileNode, TileWidget>();
        TextMeshProUGUI statusText;
        TextMeshProUGUI messageText;
        TextMeshProUGUI refreshText;
        RectTransform playerMarker;
        RectTransform trayRoot;
        bool interactable = true;

        public static MapView Create(Transform canvas, BoardData board)
        {
            var root = UIFactory.Stretch("MapView", canvas);
            var view = root.gameObject.AddComponent<MapView>();
            view.Build(board);
            return view;
        }

        void Build(BoardData board)
        {
            statusText = UIFactory.Text("Status", transform, "", 34, TextColor, new Vector2(1800, 60), new Vector2(0, 470), TextAlignmentOptions.Left);

            float width = board.tiles.Count * (TileSize + TileGap) - TileGap;
            float left = -width / 2f + TileSize / 2f;
            foreach (var tile in board.tiles)
            {
                var pos = new Vector2(left + tile.id * (TileSize + TileGap), BoardY);
                var highlight = UIFactory.Panel($"Highlight{tile.id}", transform, Vector2.one * (TileSize + 10), pos, ReachColor);
                highlight.enabled = false;
                var body = UIFactory.Panel($"Tile{tile.id}", transform, Vector2.one * TileSize, pos, TileColor(tile));
                UIFactory.Text("Label", body.transform, TileLabel(tile), 30, Color.black, Vector2.one * TileSize, new Vector2(0, 6));
                UIFactory.Text("Id", body.transform, tile.id.ToString(), 16, new Color(0, 0, 0, 0.6f), new Vector2(TileSize, 20), new Vector2(0, -TileSize / 2f + 12));
                var prob = UIFactory.Text("Probability", transform, "", 24, ReachColor, new Vector2(TileSize + TileGap, 40), pos + new Vector2(0, -70));
                tiles[tile] = new TileWidget { highlight = highlight, probability = prob, rect = body.rectTransform };
            }

            playerMarker = UIFactory.Text("PlayerMarker", transform, "▼\n自分", 26, new Color(0.4f, 0.8f, 1f), new Vector2(TileSize, 80), Vector2.zero).rectTransform;
            messageText = UIFactory.Text("Message", transform, "", 30, TextColor, new Vector2(1600, 100), new Vector2(0, -110));
            refreshText = UIFactory.Text("RefreshInfo", transform, "", 26, new Color(0.8f, 0.8f, 0.8f), new Vector2(1200, 40), new Vector2(0, -200));
            trayRoot = UIFactory.Rect("DiceTray", transform, new Vector2(1800, 180), new Vector2(0, -350));
        }

        public void Refresh(RunState run)
        {
            statusText.text = $"HP {run.player.hp}/{run.player.maxHp}　　ターン {run.Turn}　　ボスまで残り {run.TilesToGoal} マス";
            playerMarker.anchoredPosition = tiles[run.Current].rect.anchoredPosition + new Vector2(0, 90);

            int available = run.pouch.AvailableCount;
            refreshText.text = $"使用可能 {available} 個（あと {available} 個使うとリフレッシュ）";
            RebuildTray(run.pouch);
        }

        public void SetMessage(string message) => messageText.text = message;

        public void SetInteractable(bool value)
        {
            interactable = value;
            foreach (var button in trayRoot.GetComponentsInChildren<Button>()) button.interactable = value && IsAvailable(button);
        }

        public void ShowReach(Dictionary<TileNode, float> reach)
        {
            ClearReach();
            foreach (var kv in reach)
            {
                var w = tiles[kv.Key];
                w.highlight.enabled = true;
                w.probability.text = $"{kv.Value * 100f:0}%";
            }
        }

        public void ClearReach()
        {
            foreach (var w in tiles.Values)
            {
                w.highlight.enabled = false;
                w.probability.text = "";
            }
        }

        readonly Dictionary<Button, DiceInstance> trayDice = new Dictionary<Button, DiceInstance>();

        bool IsAvailable(Button b) => trayDice.TryGetValue(b, out var d) && d.state == DiceState.Available;

        void RebuildTray(DicePouch pouch)
        {
            UIFactory.ClearChildren(trayRoot);
            trayDice.Clear();

            // 使用可能を左、使用済みを右に寄せる
            var ordered = pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float w = 240f, gap = 24f;
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                string faces = string.Join(" ", die.faces.Select(f => f.value));
                string label = $"<size=30><b>{die.DisplayName}</b></size>\n{faces}" + (available ? "" : "\n<size=20>使用済み</size>");
                var button = UIFactory.Button($"Dice{i}", trayRoot, new Vector2(w, 150), new Vector2(left + i * (w + gap), 0),
                    available ? new Color(0.95f, 0.92f, 0.8f) : new Color(0.35f, 0.35f, 0.35f), label, 24, out var labelText);
                if (!available) labelText.color = new Color(0, 0, 0, 0.6f);
                button.interactable = interactable && available;
                trayDice[button] = die;

                button.onClick.AddListener(() => DiceClicked?.Invoke(die));
                var hover = button.gameObject.AddComponent<HoverRelay>();
                hover.Entered += () => DiceHovered?.Invoke(die);
                hover.Exited += () => DiceUnhovered?.Invoke();
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

        static Color TileColor(TileNode tile)
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
