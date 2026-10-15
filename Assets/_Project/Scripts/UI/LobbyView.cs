using System;
using SaiNoMichi.Multiplayer;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// みんなで遊ぶ入口：対戦「賽レース」と協力「賽の旅団」を上の札で切り替え、AI と練習・だれかと・部屋を作る・合言葉で入る。
    /// 部屋に入ったら、集まった人と「開始」を待つ。
    /// </summary>
    public class LobbyView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color RaceColor = new Color(1f, 0.6f, 0.45f);
        static readonly Color PartyColor = new Color(0.55f, 0.8f, 0.95f);

        public enum Mode { Race, Party }

        /// <summary>いま選んでいる遊び方（対戦・協力）。</summary>
        public Mode CurrentMode { get; private set; }

        public event Action PracticeClicked;          // AI と練習
        public event Action<string> RoomRequested;    // 合言葉（作るときは新しい合言葉。だれかとなら null）で部屋に入る
        public event Action BackClicked;
        public event Action StartClicked;             // 代表が「開始」
        public event Action LeaveClicked;             // 部屋から抜ける

        RectTransform box;
        TMP_InputField codeInput;
        TMP_InputField nameInput;
        TextMeshProUGUI roomText;
        TextMeshProUGUI membersText;
        TextMeshProUGUI statusText;
        Button startButton;

        public static LobbyView Create(Transform canvas, UIArt art, Mode mode = Mode.Race)
        {
            var root = UIFactory.Stretch("LobbyView", canvas);
            var view = root.gameObject.AddComponent<LobbyView>();
            view.CurrentMode = mode;
            UIFactory.Background(root, art != null ? art.titleBackground : null, new Color(0.2f, 0.15f, 0.12f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.55f)).raycastTarget = true;
            view.box = UIFactory.Panel("Box", root, new Vector2(1100, 800), new Vector2(0, 0), new Color(0.12f, 0.08f, 0.06f, 0.96f)).rectTransform;
            view.ShowMenu();
            return view;
        }

        bool Online => CurrentMode == Mode.Race ? OnlineRace.Available : OnlineParty.Available;
        Color ModeColor => CurrentMode == Mode.Race ? RaceColor : PartyColor;

        // ---- 入口：練習・だれかと・作る・入る ----

        public void ShowMenu()
        {
            UIFactory.ClearChildren(box);
            // 上の札：対戦・協力
            var raceTab = UIFactory.Button("RaceTab", box, new Vector2(440, 70), new Vector2(-235, 330),
                CurrentMode == Mode.Race ? RaceColor : new Color(0.4f, 0.35f, 0.3f), "対戦「賽レース」", 30, out var raceLabel);
            raceTab.onClick.AddListener(() => SwitchMode(Mode.Race));
            var partyTab = UIFactory.Button("PartyTab", box, new Vector2(440, 70), new Vector2(235, 330),
                CurrentMode == Mode.Party ? PartyColor : new Color(0.4f, 0.35f, 0.3f), "協力「賽の旅団」", 30, out var partyLabel);
            partyTab.onClick.AddListener(() => SwitchMode(Mode.Party));
            if (CurrentMode != Mode.Race) raceLabel.color = PaperColor;
            if (CurrentMode != Mode.Party) partyLabel.color = PaperColor;

            string rule = CurrentMode == Mode.Race
                ? "同じ盤面（同じシード）の旅を、みんなで同時に遊びます。\n少ないターンで第3層のボスを倒した人の勝ち。マップに相手の駒と順位が出ます。"
                : "同じ盤面を、仲間と同じマスにそろって進みます。毎ターン全員が振り、\nみんなの出目で止まれるマスから、行き先に票を入れます。HP・ダイス・レリックは各自。";
            UIFactory.Text("Rule", box, rule, 26, PaperColor, new Vector2(980, 90), new Vector2(0, 225));

            bool online = Online;
            var practice = UIFactory.Button("Practice", box, new Vector2(400, 76), new Vector2(-220, 100), ButtonColor,
                CurrentMode == Mode.Race ? "AI と練習する" : "AI の仲間と旅する", 30, out _);
            practice.onClick.AddListener(() => PracticeClicked?.Invoke());

            // だれかと（合言葉なしで、開いている部屋に入る。なければ作って待つ）
            var quick = UIFactory.Button("Quick", box, new Vector2(400, 76), new Vector2(220, 100), ModeColor,
                CurrentMode == Mode.Race ? "だれかと対戦" : "だれかと協力", 30, out _);
            quick.interactable = online;
            quick.onClick.AddListener(() => RoomRequested?.Invoke(null));

            // 名前（10文字まで。ブラウザでは Unity の入力欄で日本語を変換できないので、押したらブラウザの入力欄を重ねて出す）
            UIFactory.Text("NameLabel", box, "名前", 26, PaperColor, new Vector2(300, 40), new Vector2(-250, 10));
            nameInput = CodeInput(box, new Vector2(300, 64), new Vector2(10, 10));
            nameInput.contentType = TMP_InputField.ContentType.Standard;
            nameInput.lineType = TMP_InputField.LineType.SingleLine;
            nameInput.characterLimit = 10;
            nameInput.text = GameSettings.PlayerName;
            ((TextMeshProUGUI)nameInput.placeholder).text = "旅人";
            nameInput.onEndEdit.AddListener(v => GameSettings.PlayerName = v.Trim());
            if (WebTextInput.Supported)
            {
                var field = nameInput;
                field.readOnly = true;
                field.onSelect.AddListener(_ =>
                {
                    field.DeactivateInputField();
                    WebTextInput.Open("名前（10文字まで）", field.text, 10, v =>
                    {
                        if (field == null) return;
                        field.text = v.Trim();
                        GameSettings.PlayerName = field.text;
                    });
                });
            }

            var create = UIFactory.Button("Create", box, new Vector2(400, 70), new Vector2(-220, -85), ModeColor, "友だちと：部屋を作る", 28, out _);
            create.interactable = online;
            create.onClick.AddListener(() => RoomRequested?.Invoke(NewCode()));

            UIFactory.Text("CodeLabel", box, "合言葉", 26, PaperColor, new Vector2(120, 40), new Vector2(60, -85));
            codeInput = CodeInput(box, new Vector2(150, 64), new Vector2(190, -85));
            codeInput.interactable = online;
            var join = UIFactory.Button("Join", box, new Vector2(150, 64), new Vector2(370, -85), ModeColor, "入る", 28, out _);
            join.interactable = online;
            join.onClick.AddListener(() =>
            {
                string code = codeInput.text.Trim();
                if (code.Length == 4) RoomRequested?.Invoke(code);
                else statusText.text = "<color=#FF8A6A>合言葉は数字4けたです。</color>";
            });

            statusText = UIFactory.Text("Status", box, online ? "" : "オンラインで遊ぶのは、この版では使えません（通信の準備がありません）。", 24, PaperColor, new Vector2(960, 40), new Vector2(0, -170));

            var back = UIFactory.Button("Back", box, new Vector2(300, 70), new Vector2(0, -310), ButtonColor, "戻る", 30, out _);
            back.onClick.AddListener(() => BackClicked?.Invoke());
        }

        void SwitchMode(Mode mode)
        {
            if (mode == CurrentMode) return;
            CurrentMode = mode;
            Sfx.Play(SoundId.Button);
            ShowMenu();
        }

        static string NewCode() => new System.Random().Next(1000, 10000).ToString();

        // ---- 部屋：集まった人と開始 ----

        public void ShowRoom(IOnlineRoom room)
        {
            UIFactory.ClearChildren(box);
            string title = CurrentMode == Mode.Race ? "対戦「賽レース」の部屋" : "協力「賽の旅団」の部屋";
            UIFactory.Text("Title", box, title, 48, GoldColor, new Vector2(1000, 80), new Vector2(0, 320)).fontStyle = FontStyles.Bold;
            roomText = UIFactory.Text("Room", box, "", 40, PaperColor, new Vector2(1000, 60), new Vector2(0, 240));
            string hint = room.RoomCode == "だれかと" || room.RoomCode == null ? "ほかの人が入ってくるのを待っています。2人以上そろったら、部屋の代表が「開始」を押せます。" : "友だちに合言葉を伝えて、合言葉の欄から入ってもらってください。";
            UIFactory.Text("Hint", box, hint, 24, PaperColor, new Vector2(1000, 40), new Vector2(0, 190));
            membersText = UIFactory.Text("Members", box, "", 30, PaperColor, new Vector2(700, 220), new Vector2(0, 40), TextAlignmentOptions.Top);
            statusText = UIFactory.Text("Status", box, "", 24, PaperColor, new Vector2(1000, 40), new Vector2(0, -120));
            startButton = UIFactory.Button("Start", box, new Vector2(400, 84), new Vector2(-220, -260), ModeColor, "開始", 34, out _);
            startButton.onClick.AddListener(() => StartClicked?.Invoke());
            var leave = UIFactory.Button("Leave", box, new Vector2(300, 84), new Vector2(220, -260), ButtonColor, "抜ける", 30, out _);
            leave.onClick.AddListener(() => LeaveClicked?.Invoke());
            Refresh(room);
        }

        public void Refresh(IOnlineRoom room)
        {
            if (roomText == null) return;
            roomText.text = room.RoomCode == null || room.RoomCode == "だれかと"
                ? (CurrentMode == Mode.Race ? "だれかと対戦" : "だれかと協力")
                : $"合言葉　<color=#FFD24D><size=130%>{room.RoomCode}</size></color>";
            var members = room.Members;
            int max = CurrentMode == Mode.Race ? OnlineRace.MaxPlayers : OnlineParty.MaxPlayers;
            membersText.text = $"集まった人（{members.Count} / {max}）\n" + string.Join("\n", members);
            bool canStart = room.IsHost && members.Count >= 2;
            startButton.interactable = canStart;
            statusText.text = !room.IsConnected ? room.Status
                : room.IsHost ? (members.Count >= 2 ? "そろったら「開始」を押してください。" : "相手が入ってくるのを待っています……")
                : "部屋の代表が「開始」を押すのを待っています……";
        }

        /// <summary>数字だけを打てる入力欄（TextMeshPro）。</summary>
        static TMP_InputField CodeInput(Transform parent, Vector2 size, Vector2 position)
        {
            var back = UIFactory.Panel("CodeInput", parent, size, position, new Color(0.96f, 0.92f, 0.82f));
            back.raycastTarget = true;
            var area = UIFactory.Rect("TextArea", back.transform, size - new Vector2(20, 10), Vector2.zero);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = UIFactory.Text("Placeholder", area, "1234", 36, new Color(0.5f, 0.45f, 0.4f), size - new Vector2(20, 10), Vector2.zero);
            var text = UIFactory.Text("Text", area, "", 36, new Color(0.18f, 0.12f, 0.08f), size - new Vector2(20, 10), Vector2.zero);
            var input = back.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 4;
            input.targetGraphic = back;
            return input;
        }
    }
}
