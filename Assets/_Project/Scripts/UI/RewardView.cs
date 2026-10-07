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

            view.ShowChoices();
            return view;
        }

        public void ShowChoices()
        {
            UIFactory.ClearChildren(content);
            titleText.text = "勝利！";
            subText.text = $"<color=#FFD24D>+{goldGained} G</color> を手に入れた。ダイスを1つ選んでください。";
            StartCoroutine(UIAnim.Punch(titleText.transform, 0.2f, 0.35f));

            var choices = reward.diceChoices;
            const float w = 400f, h = 200f, gap = 40f;
            float left = -(choices.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < choices.Count; i++)
            {
                var data = choices[i];
                var card = DiceCard.Create($"Choice{i}", content, new DiceInstance(data), art, new Vector2(w, h), new Vector2(left + i * (w + gap), 120),
                    $"{RarityLabel(data)}　クリックで受け取る", false, false);
                card.Button.onClick.AddListener(() => DiceChosen?.Invoke(data));
            }

            var skip = UIFactory.Button("SkipButton", content, new Vector2(420, 84), new Vector2(0, -120), ButtonColor,
                $"スキップ（+{skipGold} G）", 32, out _);
            skip.onClick.AddListener(() => Skipped?.Invoke());
        }

        public void ShowReplace(DicePouch pouch, DiceData incoming)
        {
            UIFactory.ClearChildren(content);
            titleText.text = "ポーチが満杯です";
            subText.text = $"「{incoming.displayName}」と入れ替えるダイスを選んでください。";

            var dice = pouch.All.ToList();
            const float w = 300f, h = 170f, gap = 24f;
            float left = -(dice.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < dice.Count; i++)
            {
                var die = dice[i];
                // 呪いのダイスは入れ替えの対象にならない（仕様書 第4章）
                bool cursed = die.data != null && die.data.rarity == Core.Rarity.Curse;
                var card = DiceCard.Create($"Pouch{i}", content, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 120),
                    cursed ? "呪い：手放せない" : "クリックで手放す", cursed, false);
                card.Button.interactable = !cursed;
                card.Button.onClick.AddListener(() => ReplaceChosen?.Invoke(die));
            }

            var back = UIFactory.Button("BackButton", content, new Vector2(420, 84), new Vector2(0, -120), ButtonColor, "やめる（選び直す）", 32, out _);
            back.onClick.AddListener(() => ReplaceCancelled?.Invoke());
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
