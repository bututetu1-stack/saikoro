using System;
using System.Linq;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>開始時にスターターダイスを1つ選ぶ画面（仕様書 第3章「初期構成」）。</summary>
    public class StarterView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.78f);

        public event Action<DiceData> Chosen;
        System.Collections.Generic.List<DiceData> starterChoices;

        /// <param name="choices">選べるスターター（ランごとにランダム）。</param>
        /// <param name="unlockedAscension">選べるいちばん上の段位（0 なら段位を選ぶ欄を出さない）。</param>
        /// <param name="showTravelers">旅人を選ぶ欄を出すか（ひとりの旅だけ）。</param>
        public static StarterView Create(Transform canvas, UIArt art, GameConfig config, System.Collections.Generic.List<DiceData> choices,
            int unlockedAscension = 0, int initialAscension = 0, bool showTravelers = false, TravelerKind initialTraveler = TravelerKind.Saifuri, int seed = 0)
        {
            var root = UIFactory.Stretch("StarterView", canvas);
            var view = root.gameObject.AddComponent<StarterView>();
            view.starterChoices = choices;
            view.config = config;
            view.unlockedAscension = unlockedAscension;
            view.Ascension = Mathf.Clamp(initialAscension, 0, unlockedAscension);

            UIFactory.Background(root, art != null ? art.mapBackground : null, new Color(0.85f, 0.8f, 0.65f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.35f));

            // 題字は1行、説明は3行まで（旅人の役割・能力を足したら、題字と説明が重なって枠からはみ出していた）
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1100, 190), new Vector2(0, 395), ShadeColor);
            view.titleText = UIFactory.Text("Title", titlePanel.transform, "スターターダイスを選ぶ", 52, PaperColor, new Vector2(1000, 64), new Vector2(0, 52));
            view.titleText.fontStyle = FontStyles.Bold;
            view.titleText.textWrappingMode = TextWrappingModes.NoWrap;
            view.titleText.enableAutoSizing = true;
            view.titleText.fontSizeMax = 52;
            view.titleText.fontSizeMin = 34;
            view.subText = UIFactory.Text("Sub", titlePanel.transform, "", 24, PaperColor, new Vector2(1000, 104), new Vector2(0, -36));
            view.subText.enableAutoSizing = true;
            view.subText.fontSizeMax = 24;
            view.subText.fontSizeMin = 16;

            const float w = 400f, h = 200f, gap = 50f;
            float left = -(choices.Count * (w + gap) - gap) / 2f + w / 2f;
            // 選ぶ → 「この賽で旅に出る」で決定（ワンクリックで決まると押し間違えやすいため）
            var picker = DicePicker.Create(root, new Vector2(0, 20), art, choices.Select(d => new DiceInstance(d)).ToList(),
                new Vector2(w, h), gap, _ => "クリックで選ぶ");
            picker.NewFor = i => DiceCard.IsNew(choices[i]);
            view.picker = picker;
            for (int i = 0; i < choices.Count; i++)
            {
                var hint = UIFactory.Panel($"HintPanel{i}", root, new Vector2(w, 104), new Vector2(left + i * (w + gap), -158), ShadeColor);
                var hintText = UIFactory.Text("Hint", hint.transform, StarterHint(choices[i]), 24, PaperColor, new Vector2(w - 76, 92), Vector2.zero); // 両端の唐草の内側
                hintText.enableAutoSizing = true; // 説明が長いダイス（花火賽など）でも枠に収める
                hintText.fontSizeMax = 24;
                hintText.fontSizeMin = 14;
                view.starterParts.Add(hint.gameObject);
            }

            var go = UIFactory.Button("GoButton", root, new Vector2(520, 90), new Vector2(0, -300), new Color(1f, 0.78f, 0.3f), "スターターを選んでください", 32, out var goLabel);
            view.goButton = go;
            view.goLabel = goLabel;
            picker.SelectionChanged += _ => view.RefreshGo();
            go.onClick.AddListener(() =>
            {
                // 賽振り以外の旅人は、はじめのダイスが決まっている（スターターなし）
                if (view.Traveler != TravelerKind.Saifuri) view.Chosen?.Invoke(null);
                else if (picker.Selected >= 0) view.Chosen?.Invoke(choices[picker.Selected]);
            });
            view.art = art;
            if (showTravelers)
            {
                view.CreateTravelerPicker(root, initialTraveler);
                view.CreateSeedPanel(root, seed);
            }
            view.SetTraveler(view.Traveler);
            if (unlockedAscension > 0) view.CreateAscensionPicker(root);
            return view;
        }

        GameConfig config;
        TextMeshProUGUI titleText, subText, goLabel;
        UnityEngine.UI.Button goButton;
        readonly System.Collections.Generic.List<GameObject> starterParts = new System.Collections.Generic.List<GameObject>();

        // ---- シード（同じシードなら同じ旅。友だちと同じ旅をくらべられる） ----

        /// <summary>シードを入れて「変える」を押した（そのシードで選び直す）。</summary>
        public event Action<int> SeedChosen;

        void CreateSeedPanel(RectTransform root, int seed)
        {
            var panel = UIFactory.Panel("Seed", root, new Vector2(360, 170), new Vector2(760, 380), ShadeColor);
            UIFactory.Text("Label", panel.transform, $"シード　{seed}", 26, PaperColor, new Vector2(340, 40), new Vector2(0, 52));
            var input = LobbyView.CodeInput(panel.transform, new Vector2(210, 56), new Vector2(-62, -8));
            input.characterLimit = 10;
            input.textComponent.fontSize = 28;
            ((TextMeshProUGUI)input.placeholder).text = "シード";
            ((TextMeshProUGUI)input.placeholder).fontSize = 28;
            var change = UIFactory.Button("Change", panel.transform, new Vector2(110, 56), new Vector2(110, -8), new Color(0.93f, 0.87f, 0.72f), "変える", 26, out _);
            change.onClick.AddListener(() =>
            {
                if (int.TryParse(input.text, out int value) && value > 0) SeedChosen?.Invoke(value);
            });
            UIFactory.Text("Hint", panel.transform, "同じシードなら、だれでも同じ旅", 18, new Color(0.8f, 0.75f, 0.65f), new Vector2(330, 30), new Vector2(0, -62));
        }

        // ---- 旅人（仕様書 第12章） ----

        /// <summary>選んだ旅人。</summary>
        public TravelerKind Traveler { get; private set; }

        /// <summary>旅人を選ぶ欄を出したか。</summary>
        public bool HasTravelers { get; private set; }

        readonly System.Collections.Generic.List<(TravelerKind kind, UnityEngine.UI.Image image)> travelerTabs = new System.Collections.Generic.List<(TravelerKind, UnityEngine.UI.Image)>();
        RectTransform travelerInfo;
        TextMeshProUGUI travelerInfoText;
        UnityEngine.UI.Image travelerPortrait;
        UIArt art;

        void CreateTravelerPicker(RectTransform root, TravelerKind initial)
        {
            HasTravelers = true;
            const float bw = 330f, step = 350f;
            for (int i = 0; i < Travelers.All.Length; i++)
            {
                var k = Travelers.All[i];
                bool unlocked = TravelerRecord.IsUnlocked(k);
                string label = unlocked ? Travelers.Name(k) : $"{Travelers.Name(k)}\n<size=60%>{Travelers.UnlockCondition(k)}</size>";
                var b = UIFactory.Button($"Traveler{i}", root, new Vector2(bw, 70), new Vector2(-(Travelers.All.Length - 1) * step / 2f + i * step, 240), new Color(0.93f, 0.87f, 0.72f), label, unlocked ? 30 : 26, out _);
                b.interactable = unlocked;
                b.onClick.AddListener(() => SetTraveler(k));
                travelerTabs.Add((k, b.GetComponent<UnityEngine.UI.Image>()));
            }
            travelerInfo = UIFactory.Panel("TravelerInfo", root, new Vector2(1300, 280), new Vector2(0, -20), ShadeColor).rectTransform;
            travelerPortrait = UIFactory.Picture("Portrait", travelerInfo, null, new Vector2(250, 250), new Vector2(-490, 0));
            travelerInfoText = UIFactory.Text("Text", travelerInfo, "", 30, PaperColor, new Vector2(900, 244), new Vector2(130, 0));
            travelerInfoText.enableAutoSizing = true; // 能力が2行になって枠からはみ出していた
            travelerInfoText.fontSizeMax = 30;
            travelerInfoText.fontSizeMin = 18;
            Traveler = TravelerRecord.IsUnlocked(initial) ? initial : TravelerKind.Saifuri;
        }

        void SetTraveler(TravelerKind k)
        {
            Traveler = k;
            bool saifuri = k == TravelerKind.Saifuri;
            picker.gameObject.SetActive(saifuri);
            foreach (var part in starterParts) part.SetActive(saifuri);
            foreach (var (kind, image) in travelerTabs) image.color = kind == k ? new Color(1f, 0.78f, 0.3f) : new Color(0.93f, 0.87f, 0.72f);
            if (saifuri)
            {
                titleText.text = $"{Travelers.Name(k)}（{Travelers.Role(k)}）：スターターダイスを選ぶ";
                // 同じダイスは「普通の賽×2」のようにまとめる。能力は2行なので、1行目と分ける
                subText.text = $"{DiceList(config.startingDice)} に、選んだ1個を加えて旅に出ます。\n<color=#FFD24D>能力</color>　{Travelers.Ability(k)}";
                if (travelerInfo != null) travelerInfo.gameObject.SetActive(false);
            }
            else
            {
                titleText.text = $"旅人：{Travelers.Name(k)}（{Travelers.Role(k)}）";
                subText.text = "はじめのダイスは決まっています。";
                var known = config.startingDice.Concat(config.starterChoices).Concat(config.rewardDicePool).Where(d => d != null).ToList();
                var dice = Travelers.StartingDiceIds(k).Select(id => known.FirstOrDefault(d => d.id == id)).Where(d => d != null).ToList();
                string extra = k == TravelerKind.Saikushi ? "（からくり賽の6の面に崩し・1の面に堅の刻印）" : "";
                // 役割は題字に出しているので、ここははじめのダイスと能力だけ
                travelerInfoText.text = $"<color=#FFD24D>はじめのダイス</color>\n{DiceList(dice)}{extra}\n\n<color=#FFD24D>能力</color>\n{Travelers.Ability(k)}";
                travelerInfo.gameObject.SetActive(true);
                if (art != null && art.PlayerFor(k) != null)
                {
                    travelerPortrait.sprite = art.PlayerFor(k);
                    travelerPortrait.type = UnityEngine.UI.Image.Type.Simple;
                    travelerPortrait.pixelsPerUnitMultiplier = 1f;
                    travelerPortrait.preserveAspect = true;
                    travelerPortrait.color = Color.white;
                }
            }
            RefreshGo();
        }

        void RefreshGo()
        {
            if (Traveler != TravelerKind.Saifuri)
            {
                goButton.interactable = true;
                goLabel.text = $"{Travelers.Name(Traveler)}で旅に出る";
            }
            else
            {
                goButton.interactable = picker.Selected >= 0;
                goLabel.text = picker.Selected >= 0 ? $"{starterChoices[picker.Selected].displayName}で旅に出る" : "スターターを選んでください";
            }
        }

        static string DiceList(System.Collections.Generic.IEnumerable<DiceData> dice) =>
            string.Join("・", dice.Where(d => d != null).GroupBy(d => d.displayName).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));

        DicePicker picker;

        // ---- 段位（仕様書 第12章） ----

        int unlockedAscension;
        TextMeshProUGUI ascensionLabel, ascensionText;

        /// <summary>選んだ段位。</summary>
        public int Ascension { get; private set; }

        /// <summary>段位を選ぶ欄を出したか。</summary>
        public bool HasAscension => unlockedAscension > 0;

        void CreateAscensionPicker(RectTransform root)
        {
            var panel = UIFactory.Panel("Ascension", root, new Vector2(560, 150), new Vector2(0, -430), ShadeColor);
            var minus = UIFactory.Button("Down", panel.transform, new Vector2(70, 60), new Vector2(-235, 30), new Color(0.93f, 0.87f, 0.72f), "←", 30, out _);
            minus.onClick.AddListener(() => SetAscension(Ascension - 1));
            var plus = UIFactory.Button("Up", panel.transform, new Vector2(70, 60), new Vector2(235, 30), new Color(0.93f, 0.87f, 0.72f), "→", 30, out _);
            plus.onClick.AddListener(() => SetAscension(Ascension + 1));
            ascensionLabel = UIFactory.Text("Label", panel.transform, "", 38, new Color(1f, 0.82f, 0.3f), new Vector2(380, 60), new Vector2(0, 30));
            ascensionLabel.fontStyle = FontStyles.Bold;
            ascensionText = UIFactory.Text("Text", panel.transform, "", 22, PaperColor, new Vector2(540, 70), new Vector2(0, -38));
            ascensionText.enableAutoSizing = true;
            ascensionText.fontSizeMax = 22;
            ascensionText.fontSizeMin = 14;
            SetAscension(Ascension);
        }

        void SetAscension(int level)
        {
            Ascension = Mathf.Clamp(level, 0, unlockedAscension);
            ascensionLabel.text = Ascension == 0 ? "段位 0（ふつう）" : $"段位 {Ascension}";
            ascensionText.text = Ascension == 0
                ? $"クリアすると、ひとつ上の段位が選べるようになる（いま段位 {unlockedAscension} まで）。"
                : $"＋{Run.AscensionRules.Condition(Ascension)}" + (Ascension > 1 ? "\n<size=85%><color=#B8AC98>それより下の段位の条件もすべて加わる</color></size>" : "");
        }

        /// <summary>テスト・自動操作用：index 番目を選んで決定する。</summary>
        public void ChooseForTest(int index)
        {
            picker.Select(index);
            Chosen?.Invoke(starterChoices[index]);
        }

        /// <summary>仕様書 第3章「スターター」の「向いているプレイ」。</summary>
        static string StarterHint(DiceData data)
        {
            switch (data.id)
            {
                case "ken": return "攻撃で押し切る";
                case "hifumi": return "移動を細かく調整して、狙ったマスに止まる";
                case "tate": return "戦闘を安定させる";
                case "bakuchi": return "一発逆転を狙う";
                default: return data.description;
            }
        }
    }
}
