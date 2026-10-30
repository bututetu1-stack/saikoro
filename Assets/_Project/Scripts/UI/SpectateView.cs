using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Multiplayer;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 対戦「賽レース」の観戦：先に終わった人が、まだ走っている相手の旅を見る。
    /// 同じシードなので、相手のいる層の盤面は自分の端末で作れる（RunState.BoardOfLayer）。送られてくるのは、
    /// 位置・HP・いま何をしているか・賽袋の中身だけ（RaceProgress）。上の札で、見る相手を切り替える（Tab キーでも）。
    /// </summary>
    public class SpectateView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color InkColor = new Color(0.18f, 0.12f, 0.08f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ShadeColor = new Color(0.1f, 0.07f, 0.05f, 0.85f);

        public event Action BackClicked;

        IRaceSession session;
        GameConfig config;
        UIArt art;

        string watchedName;
        MapView map;
        int mapLayer = -1;
        int shownTile = -1;
        bool wasBattle;
        Coroutine moving;
        TextMeshProUGUI nameTag;
        RectTransform tabs;
        RectTransform dicePanel;

        public static SpectateView Create(Transform canvas, UIArt art, GameConfig config, IRaceSession session)
        {
            var root = UIFactory.Stretch("SpectateView", canvas);
            var view = root.gameObject.AddComponent<SpectateView>();
            view.session = session;
            view.config = config;
            view.art = art;
            // 盤面がまだないあいだ（相手がいない）の地
            UIFactory.Background(root, art != null ? art.MapBackgroundFor(0) : null, new Color(0.85f, 0.8f, 0.65f));

            view.tabs = UIFactory.Rect("Tabs", root, new Vector2(700, 46), new Vector2(-560, 440));
            var back = UIFactory.Button("BackButton", root, new Vector2(220, 46), new Vector2(830, 440), new Color(0.93f, 0.87f, 0.72f), "結果に戻る", 24, out _);
            back.onClick.AddListener(() => view.BackClicked?.Invoke());

            view.dicePanel = UIFactory.Panel("DicePanel", root, new Vector2(1800, 170), new Vector2(0, -440), ShadeColor).rectTransform;
            view.dicePanel.GetComponent<Image>().raycastTarget = false;

            // はじめは、いちばん前を走っている相手を見る
            var leader = session.Rivals.Where(r => !string.IsNullOrEmpty(r.name))
                .OrderByDescending(r => r.state == RaceState.Running).ThenByDescending(r => r.RankKey).FirstOrDefault();
            view.watchedName = leader.name;
            view.Refresh();
            return view;
        }

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.tabKey.wasPressedThisFrame) Cycle();
            else if (keyboard.escapeKey.wasPressedThisFrame) BackClicked?.Invoke();
        }

        /// <summary>見る相手を次の人に替える。</summary>
        void Cycle()
        {
            var rivals = Watchable();
            if (rivals.Count == 0) return;
            int i = rivals.FindIndex(r => r.name == watchedName);
            Watch(rivals[(i + 1) % rivals.Count].name);
        }

        List<RaceProgress> Watchable() => session.Rivals.Where(r => !string.IsNullOrEmpty(r.name)).ToList();

        void Watch(string name)
        {
            if (name == watchedName) return;
            watchedName = name;
            mapLayer = -1; // 盤面を作り直して、駒を跳ねさせずに置く
            Sfx.Play(SoundId.Button);
            Refresh();
        }

        /// <summary>相手の進み具合が変わったら呼ぶ（GameController が session.Changed で呼ぶ）。</summary>
        public void Refresh()
        {
            if (this == null) return;
            var rivals = session.Rivals.ToList();
            int index = rivals.FindIndex(r => r.name == watchedName && !string.IsNullOrEmpty(r.name));
            if (index < 0 && Watchable().Count > 0)
            {
                watchedName = Watchable()[0].name;
                index = rivals.FindIndex(r => r.name == watchedName);
            }
            BuildTabs(rivals);
            if (index < 0)
            {
                BuildDice(null);
                return;
            }
            var p = rivals[index];

            // 相手が層を移ったら（見る相手を替えたら）、その層の盤面を作り直す
            if (map == null || p.layer != mapLayer)
            {
                if (map != null) Destroy(map.gameObject);
                mapLayer = p.layer;
                var board = RunState.BoardOfLayer(config, session.Seed, p.layer);
                map = MapView.Create(transform, board, art, p.layer);
                map.transform.SetSiblingIndex(1); // 地の上、札・ボタンより奥に
                map.SetSpectatorMode();
                nameTag = UIFactory.Text("NameTag", map.PlayerMarker, "", 26, Color.white, new Vector2(300, 40), new Vector2(0, 80));
                nameTag.fontStyle = FontStyles.Bold;
                nameTag.outlineWidth = 0.16f;
                nameTag.outlineColor = new Color32(30, 15, 5, 255);
                nameTag.raycastTarget = false;
                shownTile = -1;
                wasBattle = false;
            }

            var layerData = config.layers.Count > 0 ? config.layers[Mathf.Clamp(p.layer, 0, config.layers.Count - 1)] : null;
            map.SetLayerText($"第{p.layer + 1}層　{(layerData != null ? layerData.displayName : "")}");
            map.SetStatusText($"観戦中：<b>{p.name}</b>　　HP {p.hp}/{p.maxHp}　　ターン {p.turn}　　ボスまで最短 {p.tilesToGoal} マス");
            nameTag.text = p.name;
            map.PlaceRivalMarkers(p.layer, rivals, index);

            // 駒：前に見たマスから歩いてくる（道がたどれないときは、その場に置く）
            var tile = map.TileById(p.tileId);
            if (tile != null && tile.id != shownTile)
            {
                var from = shownTile >= 0 ? map.TileById(shownTile) : null;
                var path = from != null ? PathBetween(from, tile) : null;
                if (moving != null) StopCoroutine(moving);
                moving = null;
                if (path != null && path.Count > 0 && path.Count <= 12 && isActiveAndEnabled)
                {
                    moving = StartCoroutine(map.PlayMove(path));
                }
                else
                {
                    map.SetPlayerTile(tile);
                    map.ScrollTo(tile, false);
                }
                shownTile = tile.id;
            }

            // いま何をしているか（知らせの欄）。戦闘に入ったら、駒の上に「戦闘！」
            string[] lines = (p.activity ?? "").Split('\n');
            string head = p.state == RaceState.Cleared ? $"踏破した！（{p.turn} ターン）"
                : p.state == RaceState.Dead ? $"第{p.layer + 1}層で倒れた……"
                : lines[0];
            string detail = lines.Length > 1 ? lines[1] : "";
            if (string.IsNullOrEmpty(head)) head = $"第{p.layer + 1}層を進行中"; // まだ何も届いていない
            map.SetMessage(string.IsNullOrEmpty(head) ? detail : $"<color=#FFD24D>{head}</color>\n{detail}");
            bool inBattle = p.state == RaceState.Running && head.StartsWith("戦闘");
            if (inBattle && !wasBattle && tile != null)
            {
                map.PopupAtTile(tile, "戦闘！", new Color(1f, 0.4f, 0.3f));
                Sfx.Play(SoundId.Charge);
            }
            wasBattle = inBattle;

            BuildDice(p);
        }

        /// <summary>from から to まで、道（next）をたどった道筋（from は入らない）。たどれなければ null。</summary>
        static List<TileNode> PathBetween(TileNode from, TileNode to)
        {
            var previous = new Dictionary<TileNode, TileNode> { [from] = null };
            var queue = new Queue<TileNode>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (t == to) break;
                foreach (var n in t.next)
                {
                    if (previous.ContainsKey(n)) continue;
                    previous[n] = t;
                    queue.Enqueue(n);
                }
            }
            if (!previous.ContainsKey(to)) return null;
            var path = new List<TileNode>();
            for (var t = to; t != from; t = previous[t]) path.Add(t);
            path.Reverse();
            return path;
        }

        /// <summary>上の札：見られる相手の名前（押すとその人を見る）。</summary>
        void BuildTabs(List<RaceProgress> rivals)
        {
            UIFactory.ClearChildren(tabs);
            const float w = 220f, gap = 10f;
            int n = 0;
            for (int i = 0; i < rivals.Count; i++)
            {
                var r = rivals[i];
                if (string.IsNullOrEmpty(r.name)) continue;
                bool watching = r.name == watchedName;
                string mark = r.state == RaceState.Cleared ? "（踏破）" : r.state == RaceState.Dead ? "（倒れた）" : "";
                var color = MapView.RivalColors[i % MapView.RivalColors.Length];
                var button = UIFactory.Button($"Tab{i}", tabs, new Vector2(w, 46), new Vector2(-350 + w / 2f + n * (w + gap), 0),
                    watching ? GoldColor : new Color(0.93f, 0.87f, 0.72f), $"<color=#{ColorUtility.ToHtmlStringRGB(color * 0.7f)}>■</color>{r.name}{mark}", 22, out var label);
                label.enableAutoSizing = true;
                label.fontSizeMin = 14;
                label.fontSizeMax = 22;
                string name = r.name;
                button.onClick.AddListener(() => Watch(name));
                n++;
            }
            if (n == 0)
            {
                UIFactory.Text("None", tabs, "見られる相手がいません", 24, PaperColor, new Vector2(700, 46), Vector2.zero, TextAlignmentOptions.Left);
            }
        }

        /// <summary>下の帯：相手の賽袋（使用可能・使用済み・封印が色でわかる）。</summary>
        void BuildDice(RaceProgress? p)
        {
            UIFactory.ClearChildren(dicePanel);
            var dice = p.HasValue ? RaceProgress.DecodeDice(p.Value.dice) : new List<(string name, DiceState state)>();
            int available = dice.Count(d => d.state == DiceState.Available);
            UIFactory.Text("Title", dicePanel, dice.Count == 0 ? "賽袋（まだ届いていません）" : $"賽袋　使用可能 {available} / {dice.Count} 個",
                22, GoldColor, new Vector2(1700, 32), new Vector2(0, 62), TextAlignmentOptions.Left);
            const int perRow = 10;
            const float w = 168f, gap = 8f;
            for (int i = 0; i < dice.Count; i++)
            {
                var (name, state) = dice[i];
                int row = i / perRow, col = i % perRow;
                var pos = new Vector2(-(perRow * (w + gap) - gap) / 2f + w / 2f + col * (w + gap), 15 - row * 62);
                var back = state == DiceState.Used ? new Color(0.36f, 0.33f, 0.3f)
                    : state == DiceState.Sealed ? new Color(0.45f, 0.3f, 0.55f)
                    : new Color(0.93f, 0.87f, 0.72f);
                var chip = UIFactory.Panel($"Die{i}", dicePanel, new Vector2(w, 54), pos, back);
                chip.raycastTarget = false;
                string suffix = state == DiceState.Used ? "<size=70%>（済）</size>" : state == DiceState.Sealed ? "<size=70%>（封）</size>" : "";
                var text = UIFactory.Text("Name", chip.transform, name + suffix, 22, state == DiceState.Available ? InkColor : PaperColor, new Vector2(w - 12, 50), Vector2.zero);
                text.enableAutoSizing = true;
                text.fontSizeMin = 14;
                text.fontSizeMax = 22;
                text.raycastTarget = false;
            }
        }
    }
}
