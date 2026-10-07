using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 戦闘画面。自分と敵の状態・予告・ダイス・割り振り・ダメージ予測を表示し、操作を通知する。
    /// ルールは BattleState に任せる。
    /// </summary>
    public class BattleView : MonoBehaviour
    {
        static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color PanelColor = new Color(0.2f, 0.2f, 0.25f);
        static readonly Color ButtonColor = new Color(0.95f, 0.92f, 0.8f);
        static readonly Color SelectedColor = new Color(1f, 0.8f, 0.3f);
        static readonly Color AttackColor = new Color(0.95f, 0.5f, 0.45f);
        static readonly Color BlockColor = new Color(0.5f, 0.7f, 0.95f);
        static readonly Color OffColor = new Color(0.45f, 0.45f, 0.45f);

        public event Action<DiceInstance> DieClicked;
        public event Action RollClicked;
        public event Action<RolledDie, Assignment> AssignClicked;
        public event Action ResolveClicked;
        public event Action ContinueClicked;

        TextMeshProUGUI titleText;
        TextMeshProUGUI playerText;
        TextMeshProUGUI enemyText;
        TextMeshProUGUI intentText;
        TextMeshProUGUI previewText;
        TextMeshProUGUI logText;
        RectTransform rolledRoot;
        RectTransform trayRoot;
        Button rollButton;
        Button resolveButton;
        TextMeshProUGUI resolveLabel;
        Button continueButton;
        TextMeshProUGUI continueLabel;

        public static BattleView Create(Transform canvas)
        {
            var root = UIFactory.Stretch("BattleView", canvas);
            var view = root.gameObject.AddComponent<BattleView>();
            view.Build();
            return view;
        }

        void Build()
        {
            titleText = UIFactory.Text("Title", transform, "", 40, TextColor, new Vector2(1200, 60), new Vector2(0, 470));

            var playerPanel = UIFactory.Panel("PlayerPanel", transform, new Vector2(520, 240), new Vector2(-560, 260), PanelColor);
            playerText = UIFactory.Text("PlayerText", playerPanel.transform, "", 32, TextColor, new Vector2(480, 220), Vector2.zero, TextAlignmentOptions.Left);

            var enemyPanel = UIFactory.Panel("EnemyPanel", transform, new Vector2(520, 240), new Vector2(560, 260), PanelColor);
            enemyText = UIFactory.Text("EnemyText", enemyPanel.transform, "", 32, TextColor, new Vector2(480, 220), Vector2.zero, TextAlignmentOptions.Left);
            intentText = UIFactory.Text("Intent", transform, "", 36, new Color(1f, 0.6f, 0.5f), new Vector2(520, 60), new Vector2(560, 410));

            rolledRoot = UIFactory.Rect("Rolled", transform, new Vector2(900, 160), new Vector2(0, 260));
            previewText = UIFactory.Text("Preview", transform, "", 34, new Color(1f, 0.85f, 0.2f), new Vector2(1400, 50), new Vector2(0, 110));
            logText = UIFactory.Text("Log", transform, "", 28, TextColor, new Vector2(1400, 110), new Vector2(0, 10));

            trayRoot = UIFactory.Rect("DiceTray", transform, new Vector2(1300, 160), new Vector2(-180, -330));

            rollButton = UIFactory.Button("RollButton", transform, new Vector2(260, 80), new Vector2(760, -250), ButtonColor, "振る", 32, out _);
            rollButton.onClick.AddListener(() => RollClicked?.Invoke());
            resolveButton = UIFactory.Button("ResolveButton", transform, new Vector2(260, 80), new Vector2(760, -350), ButtonColor, "", 28, out resolveLabel);
            resolveButton.onClick.AddListener(() => ResolveClicked?.Invoke());
            continueButton = UIFactory.Button("ContinueButton", transform, new Vector2(400, 90), new Vector2(0, -160), SelectedColor, "", 32, out continueLabel);
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            continueButton.gameObject.SetActive(false);
        }

        public void Refresh(BattleState battle, ICollection<DiceInstance> selected)
        {
            bool ongoing = battle.Outcome == BattleOutcome.Ongoing;
            var p = battle.player;
            var e = battle.enemy;

            titleText.text = $"戦闘　ラウンド {battle.Round}";
            playerText.text = $"<b>自分</b>\nHP {p.hp}/{p.maxHp}\n防御 {p.block}　筋力 {p.strength}";
            enemyText.text = $"<b>{e.data.displayName}</b>\nHP {e.hp}/{e.maxHp}\n防御 {e.block}　筋力 {e.strength}";
            intentText.text = ongoing ? "予告：" + IntentLabel(battle.EnemyIntent, e.strength) : "";

            RebuildRolled(battle, ongoing);
            RebuildTray(battle, selected, ongoing);

            if (ongoing)
            {
                var preview = battle.Preview();
                previewText.text = $"与えるダメージ {preview.dealt}　／　受けるダメージ {preview.taken}";
            }
            else
            {
                previewText.text = "";
            }

            rollButton.gameObject.SetActive(ongoing);
            resolveButton.gameObject.SetActive(ongoing);
            rollButton.interactable = selected.Count > 0;
            resolveLabel.text = battle.Rolled.Count == 0 ? "パス" : "決定";
        }

        public void SetLog(string text) => logText.text = text;

        public void ShowContinue(string label)
        {
            continueLabel.text = label;
            continueButton.gameObject.SetActive(true);
        }

        public static string IntentLabel(Intent intent, int strength)
        {
            switch (intent.type)
            {
                case IntentType.Attack:
                    int attack = BattleResolver.EnemyAttack(intent, strength);
                    return strength != 0 ? $"攻撃 {attack}（{intent.value}＋筋力{strength}）" : $"攻撃 {attack}";
                case IntentType.Block:
                    return $"防御 {intent.value}";
                case IntentType.Buff:
                    return $"強化（筋力+{intent.value}）";
                default:
                    return intent.ToString();
            }
        }

        void RebuildRolled(BattleState battle, bool ongoing)
        {
            UIFactory.ClearChildren(rolledRoot);

            for (int i = 0; i < battle.Rolled.Count; i++)
            {
                var r = battle.Rolled[i];
                float y = 40 - i * 80;
                UIFactory.Text($"RolledLabel{i}", rolledRoot, $"{r.dice.DisplayName}：<b>{r.value}</b>", 32, TextColor,
                    new Vector2(230, 70), new Vector2(-165, y), TextAlignmentOptions.Right);

                var atk = UIFactory.Button($"Attack{i}", rolledRoot, new Vector2(130, 64), new Vector2(30, y),
                    r.assignment == Assignment.Attack ? AttackColor : OffColor, "攻撃", 28, out _);
                var blk = UIFactory.Button($"Block{i}", rolledRoot, new Vector2(130, 64), new Vector2(175, y),
                    r.assignment == Assignment.Block ? BlockColor : OffColor, "防御", 28, out _);
                atk.interactable = ongoing;
                blk.interactable = ongoing;
                atk.onClick.AddListener(() => AssignClicked?.Invoke(r, Assignment.Attack));
                blk.onClick.AddListener(() => AssignClicked?.Invoke(r, Assignment.Block));
            }
        }

        void RebuildTray(BattleState battle, ICollection<DiceInstance> selected, bool ongoing)
        {
            UIFactory.ClearChildren(trayRoot);

            var ordered = battle.pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float w = 240f, gap = 24f;
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool isSelected = selected.Contains(die);
                string faces = string.Join(" ", die.faces.Select(f => f.value));
                string label = $"<size=30><b>{die.DisplayName}</b></size>\n{faces}"
                    + (isSelected ? "\n<size=22>選択中</size>" : available ? "" : "\n<size=20>使用済み</size>");
                var color = isSelected ? SelectedColor : available ? ButtonColor : new Color(0.35f, 0.35f, 0.35f);
                var button = UIFactory.Button($"Dice{i}", trayRoot, new Vector2(w, 150), new Vector2(left + i * (w + gap), 0),
                    color, label, 24, out var labelText);
                if (!available) labelText.color = new Color(0, 0, 0, 0.6f);
                button.interactable = ongoing && available && battle.CanRollMore;
                button.onClick.AddListener(() => DieClicked?.Invoke(die));
            }
        }
    }
}
