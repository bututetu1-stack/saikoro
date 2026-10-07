using System;
using System.Linq;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 戦闘の報酬画面。ゴールドを見せ、ダイス3つから1つを選ぶかスキップする。
    /// ポーチが満杯なら、入れ替えるダイスを選ぶ画面に切り替わる。
    /// </summary>
    public class RewardView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.8f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);

        public event Action<DiceData> DiceChosen;
        public event Action Skipped;
        public event Action<DiceInstance> ReplaceChosen;
        public event Action ReplaceCancelled;

        UIArt art;
        BattleReward reward;
        int goldGained;
        int skipGold;
        TextMeshProUGUI titleText;
        TextMeshProUGUI subText;
        RectTransform content;

        public static RewardView Create(Transform canvas, UIArt art, BattleReward reward, int goldGained, int skipGold)
        {
            var root = UIFactory.Stretch("RewardView", canvas);
            var view = root.gameObject.AddComponent<RewardView>();
            view.art = art;
            view.reward = reward;
            view.goldGained = goldGained;
            view.skipGold = skipGold;

            UIFactory.Background(root, art != null ? art.battleBackground : null, new Color(0.25f, 0.18f, 0.15f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.45f));
            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(1200, 170), new Vector2(0, 340), ShadeColor);
            view.titleText = UIFactory.Text("Title", titlePanel.transform, "", 56, GoldColor, new Vector2(1100, 80), new Vector2(0, 32));
            view.titleText.fontStyle = FontStyles.Bold;
            view.subText = UIFactory.Text("Sub", titlePanel.transform, "", 30, PaperColor, new Vector2(1100, 60), new Vector2(0, -42));
            view.content = UIFactory.Rect("Content", root, new Vector2(1900, 600), new Vector2(0, -80));

            // エリートのレリック（もう手に入っている）
            if (reward.relic != null)
            {
                var relicPanel = UIFactory.Panel("RelicPanel", root, new Vector2(1000, 84), new Vector2(0, 205), ShadeColor);
                UIFactory.Picture("RelicIcon", relicPanel.transform, reward.relic.icon, new Vector2(72, 72), new Vector2(-450, 0), GoldColor);
                UIFactory.Text("RelicText", relicPanel.transform,
                    $"<color=#FFD24D>レリック「{reward.relic.displayName}」</color>を手に入れた：{reward.relic.description}",
                    26, PaperColor, new Vector2(880, 80), new Vector2(40, 0), TextAlignmentOptions.Left);
                view.StartCoroutine(UIAnim.Punch(relicPanel.transform, 0.15f, 0.35f));
            }

            view.ShowChoices();
            return view;
        }

        public void ShowChoices()
        {
            UIFactory.ClearChildren(content);
            titleText.text = "勝利！";
            subText.text = $"<color=#FFD24D>+{goldGained} G</color> を手に入れた。ダイスを1つ選んでください。";
            StartCoroutine(UIAnim.Punch(titleText.transform, 0.2f, 0.35f));

            // 選ぶ → 「受け取る」で決定（ワンクリックで決まると押し間違えやすいため）
            var choices = reward.diceChoices;
            var instances = choices.Select(d => new DiceInstance(d)).ToList();
            var picker = DicePicker.Create(content, new Vector2(0, 120), art, instances, new Vector2(400, 200), 40,
                i => $"{RarityLabel(choices[i])}　クリックで選ぶ");
            var take = UIFactory.Button("TakeButton", content, new Vector2(420, 84), new Vector2(-230, -120), new Color(1f, 0.78f, 0.3f), "受け取る", 32, out var takeLabel);
            take.interactable = false;
            picker.SelectionChanged += i =>
            {
                take.interactable = true;
                takeLabel.text = $"{choices[i].displayName} を受け取る";
            };
            take.onClick.AddListener(() =>
            {
                if (picker.Selected >= 0) DiceChosen?.Invoke(choices[picker.Selected]);
            });
            choicePicker = picker;

            var skip = UIFactory.Button("SkipButton", content, new Vector2(420, 84), new Vector2(230, -120), ButtonColor,
                $"スキップ（+{skipGold} G）", 32, out _);
            skip.onClick.AddListener(() => Skipped?.Invoke());
        }

        DicePicker choicePicker;
        DicePicker replacePicker;

        /// <summary>テスト・自動操作用：index 番目を選んで決定する。</summary>
        public void ChooseForTest(int index)
        {
            if (replacePicker != null && replacePicker.gameObject.activeInHierarchy)
            {
                replacePicker.Select(index);
                if (replacePicker.SelectedDie != null) ReplaceChosen?.Invoke(replacePicker.SelectedDie);
                return;
            }
            choicePicker.Select(index);
            DiceChosen?.Invoke(reward.diceChoices[index]);
        }

        public void ShowReplace(DicePouch pouch, DiceData incoming)
        {
            UIFactory.ClearChildren(content);
            titleText.text = "ポーチが満杯です";
            subText.text = $"「{incoming.displayName}」と入れ替えるダイスを選んでください。";

            replacePicker = BuildReplace(content, art, pouch, incoming, die => ReplaceChosen?.Invoke(die), () => ReplaceCancelled?.Invoke(), "やめる（選び直す）");
        }

        /// <summary>
        /// 満杯のポーチから手放すダイスを選ぶ部品（報酬・宝箱・イベントで共通）。選ぶ → 「入れ替える」で決定。
        /// 呪いのダイスは入れ替えの対象にならない（仕様書 第4章）。
        /// </summary>
        public static DicePicker BuildReplace(Transform parent, UIArt art, DicePouch pouch, DiceData incoming,
            Action<DiceInstance> onReplace, Action onCancel, string cancelLabel)
        {
            var dice = pouch.All.ToList();
            bool Cursed(int i) => dice[i].data != null && dice[i].data.rarity == Core.Rarity.Curse;
            var picker = DicePicker.Create(parent, new Vector2(0, 120), art, dice, new Vector2(300, 170), 24,
                i => Cursed(i) ? "呪い：手放せない" : "クリックで選ぶ", i => !Cursed(i));
            var replace = UIFactory.Button("ReplaceButton", parent, new Vector2(460, 84), new Vector2(-250, -120), new Color(1f, 0.78f, 0.3f), "入れ替える", 30, out var replaceLabel);
            replace.interactable = false;
            picker.SelectionChanged += i =>
            {
                replace.interactable = true;
                replaceLabel.text = $"{dice[i].DisplayName} を手放して {incoming.displayName} を入れる";
            };
            replace.onClick.AddListener(() =>
            {
                if (picker.SelectedDie != null) onReplace(picker.SelectedDie);
            });
            var back = UIFactory.Button("BackButton", parent, new Vector2(420, 84), new Vector2(250, -120), ButtonColor, cancelLabel, 30, out _);
            back.onClick.AddListener(() => onCancel());
            return picker;
        }

        static string RarityLabel(DiceData data)
        {
            switch (data.rarity)
            {
                case Core.Rarity.Uncommon: return "<color=#2E6FB0>アンコモン</color>";
                case Core.Rarity.Rare: return "<color=#B8860B>レア</color>";
                default: return "コモン";
            }
        }
    }
}
