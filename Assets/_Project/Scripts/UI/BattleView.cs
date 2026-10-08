using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Battle;
using SaiNoMichi.Dice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>Resolve の直前の値。演出で「どこから減ったか」を見せるのに使う。</summary>
    public struct RoundSnapshot
    {
        public int playerHp;
        public int enemyHp;
        public int attack;        // 攻撃値（筋力込み）
        public int playerBlock;   // ダイスで得た防御値
    }

    /// <summary>
    /// 戦闘画面。自分と敵の絵・HP・予告・ダイス・割り振り・ダメージ予測を表示し、操作を通知する。
    /// ルールは BattleState に任せ、ここでは表示と演出だけを行う。
    /// </summary>
    public class BattleView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.75f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color AccentColor = new Color(1f, 0.78f, 0.3f);
        static readonly Color AttackColor = new Color(0.9f, 0.4f, 0.3f);
        static readonly Color BlockColor = new Color(0.4f, 0.6f, 0.9f);
        static readonly Color OffColor = new Color(0.4f, 0.37f, 0.33f);
        static readonly Color DamageColor = new Color(1f, 0.35f, 0.25f);

        public event Action<DiceInstance> DieClicked;
        public event Action<DiceInstance> DieDropped;   // 札を振る場所へドラッグして離した
        public event Action RollClicked;
        public event Action<RolledDie, Assignment> AssignClicked;
        public event Action ResolveClicked;
        public event Action ContinueClicked;

        class Fighter
        {
            public RectTransform figure;
            public Image image;
            public Image shield;
            public Image hpFill;
            public TextMeshProUGUI hpText;
            public TextMeshProUGUI statusText;
            public Vector2 home;
            public int maxHp;
            public string tip = "";   // 状態異常の説明（マウスを乗せると出る）
        }

        UIArt art;
        RectTransform stage;
        RelicBar relicBar;
        public RelicBar Relics => relicBar;
        CanvasGroup stageGroup;
        Fighter player;
        Fighter enemy;
        Image intentIcon;
        TextMeshProUGUI intentText;
        TextMeshProUGUI titleText;
        TextMeshProUGUI previewText;
        TextMeshProUGUI logText;
        RectTransform rolledRoot;
        readonly List<DiceFaceView> rolledFaces = new List<DiceFaceView>();
        Image dropZone;
        const float TrayWidth = 1540f;
        RectTransform trayRoot;
        Button rollButton;
        Button resolveButton;
        TextMeshProUGUI resolveLabel;
        Button continueButton;
        TextMeshProUGUI continueLabel;
        Intent? shownIntent;

        public static BattleView Create(Transform canvas, UIArt art, EnemyData enemyData, bool isBoss)
        {
            var root = UIFactory.Stretch("BattleView", canvas);
            var view = root.gameObject.AddComponent<BattleView>();
            view.art = art;
            view.Build(enemyData, isBoss);
            return view;
        }

        void Build(EnemyData enemyData, bool isBoss)
        {
            UIFactory.Background(transform, art != null ? art.battleBackground : null, new Color(0.25f, 0.18f, 0.15f));
            stage = UIFactory.Stretch("Stage", transform);
            stageGroup = stage.gameObject.AddComponent<CanvasGroup>();

            var bar = UIFactory.Panel("TitleBar", stage, new Vector2(1920, 50), new Vector2(0, 515), ShadeColor);
            titleText = UIFactory.Text("Title", bar.transform, "", 30, PaperColor, new Vector2(1800, 48), Vector2.zero);

            player = CreateFighter("Player", art != null ? art.player : null, null, new Vector2(-560, 150), 300f);
            float enemySize = isBoss ? 440f : 340f;
            enemy = CreateFighter("Enemy", art != null ? art.EnemySpriteFor(enemyData) : null, enemyData.displayName, new Vector2(560, isBoss ? 190 : 170), enemySize);

            var intentPos = new Vector2(560, 440);
            var intentBack = UIFactory.Panel("IntentBack", stage, new Vector2(220, 76), intentPos, ShadeColor);
            intentIcon = UIFactory.Picture("IntentIcon", intentBack.transform, null, new Vector2(70, 70), new Vector2(-62, 0), AttackColor);
            intentText = UIFactory.Text("IntentText", intentBack.transform, "", 40, PaperColor, new Vector2(140, 80), new Vector2(42, 0));
            intentText.fontStyle = FontStyles.Bold;
            // 予告にマウスを乗せると、何をしてくるかの説明
            AddTip(intentBack.gameObject, () => intentTip, new Vector2(560, 300));

            rolledRoot = UIFactory.Rect("Rolled", stage, new Vector2(700, 260), new Vector2(0, 170));
            // ダイス札をここへドラッグして離すと振る。ドラッグ中だけ枠を見せる
            dropZone = UIFactory.Panel("DropZone", stage, new Vector2(1000, 420), new Vector2(0, 120), new Color(1f, 0.85f, 0.4f, 0.14f));
            dropZone.raycastTarget = false;
            var dropLabel = UIFactory.Text("Label", dropZone.transform, "ここで離すと振る", 40, new Color(1f, 0.9f, 0.6f, 0.9f), new Vector2(900, 60), new Vector2(0, 170));
            dropLabel.fontStyle = FontStyles.Bold;
            dropZone.gameObject.SetActive(false);

            var previewPanel = UIFactory.Panel("PreviewPanel", stage, new Vector2(760, 60), new Vector2(0, -10), ShadeColor);
            previewText = UIFactory.Text("Preview", previewPanel.transform, "", 32, AccentColor, new Vector2(740, 56), Vector2.zero);

            var logPanel = UIFactory.Panel("LogPanel", stage, new Vector2(900, 84), new Vector2(0, -170), ShadeColor);
            logText = UIFactory.Text("Log", logPanel.transform, "", 26, PaperColor, new Vector2(870, 80), Vector2.zero);

            // 左端から「振る」ボタンの手前まで（ダイスが多いときは札を細くして収める）
            trayRoot = UIFactory.Rect("DiceTray", stage, new Vector2(TrayWidth, 160), new Vector2(-180, -375));

            rollButton = UIFactory.Button("RollButton", stage, new Vector2(260, 80), new Vector2(760, -320), ButtonColor, "振る", 34, out _);
            rollButton.onClick.AddListener(() => RollClicked?.Invoke());
            resolveButton = UIFactory.Button("ResolveButton", stage, new Vector2(260, 80), new Vector2(760, -420), AccentColor, "", 32, out resolveLabel);
            resolveButton.onClick.AddListener(() => ResolveClicked?.Invoke());
            continueButton = UIFactory.Button("ContinueButton", stage, new Vector2(420, 96), new Vector2(0, 170), AccentColor, "", 36, out continueLabel);
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            continueButton.gameObject.SetActive(false);
            relicBar = RelicBar.Create(stage, new Vector2(-945, 482));
        }

        Fighter CreateFighter(string name, Sprite sprite, string fallbackName, Vector2 pos, float size)
        {
            var f = new Fighter { home = pos };
            f.shield = UIFactory.Picture(name + "Shield", stage, art != null ? art.fxBlock : null, Vector2.one * size * 0.9f, pos, new Color(0.4f, 0.6f, 0.9f, 0.4f));
            f.shield.gameObject.SetActive(false);

            f.image = UIFactory.Picture(name, stage, sprite, Vector2.one * size, pos, new Color(0.55f, 0.45f, 0.5f));
            f.figure = f.image.rectTransform;
            if (sprite == null && fallbackName != null)
            {
                UIFactory.Text("Name", f.figure, fallbackName, 40, PaperColor, Vector2.one * size, Vector2.zero);
            }

            var barPos = new Vector2(pos.x, -100);
            var back = UIFactory.Panel(name + "HpBack", stage, new Vector2(380, 36), barPos, new Color(0.1f, 0.06f, 0.05f, 0.9f));
            f.hpFill = UIFactory.Panel("Fill", back.transform, Vector2.zero, Vector2.zero, new Color(0.8f, 0.2f, 0.2f));
            var fillRect = f.hpFill.rectTransform;
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(1, 1);
            fillRect.offsetMin = new Vector2(3, 3);
            fillRect.offsetMax = new Vector2(-3, -3);
            f.hpText = UIFactory.Text("HpText", back.transform, "", 26, PaperColor, new Vector2(380, 36), Vector2.zero);
            f.hpText.fontStyle = FontStyles.Bold;
            f.statusText = UIFactory.Text(name + "Status", stage, "", 26, PaperColor, new Vector2(380, 34), barPos + new Vector2(0, -36));
            f.statusText.outlineWidth = 0.25f;
            f.statusText.outlineColor = new Color32(20, 12, 8, 255);
            // 状態異常にマウスを乗せると説明（弱体・毒など）
            f.statusText.raycastTarget = true;
            AddTip(f.statusText.gameObject, () => f.tip, barPos + new Vector2(0, -150));
            return f;
        }

        // ---- 表示の更新 ----

        /// <param name="hiddenRolled">振ったばかりで、まだ転がっている最中のダイスの数（後ろから）。出目や値を「？」にして伏せる。</param>
        public void Refresh(BattleState battle, ICollection<DiceInstance> selected, int hiddenRolled = 0)
        {
            bool ongoing = battle.Outcome == BattleOutcome.Ongoing;

            titleText.text = $"戦闘　ラウンド {battle.Round}　　{battle.enemy.data.displayName}";
            SetFighter(player, battle.player);
            SetFighter(enemy, battle.enemy);

            if (ongoing) SetIntent(battle.EnemyIntent, battle.enemy);
            intentIcon.transform.parent.gameObject.SetActive(ongoing);

            RebuildRolled(battle, ongoing, hiddenRolled);
            RebuildTray(battle, selected, ongoing);

            if (ongoing)
            {
                var preview = battle.Preview();
                string taken = preview.TakenIsRange ? $"{preview.takenMin}〜{preview.taken}" : preview.taken.ToString();
                previewText.text = hiddenRolled > 0
                    ? "与えるダメージ ？　／　受けるダメージ ？"
                    : $"与えるダメージ {preview.dealt}　／　受けるダメージ {taken}";
            }
            previewText.transform.parent.gameObject.SetActive(ongoing);

            rollButton.gameObject.SetActive(ongoing);
            resolveButton.gameObject.SetActive(ongoing);
            rollButton.interactable = selected.Count > 0;
            resolveLabel.text = battle.Rolled.Count == 0 ? "パス" : "決定";
        }

        void SetFighter(Fighter f, Combatant c)
        {
            f.maxHp = c.maxHp;
            SetHp(f, c.hp);
            var parts = new List<string>();
            if (c.block > 0) parts.Add($"<color=#8FB8FF>防御 {c.block}</color>");
            if (c.strength != 0) parts.Add($"<color=#FFD070>筋力 {c.strength:+0;-0}</color>");
            if (c.weak > 0) parts.Add($"<color=#C79BFF>弱体 {c.weak}</color>");
            if (c.poison > 0) parts.Add($"<color=#8BE07A>毒 {c.poison}</color>");
            f.statusText.text = string.Join("　", parts);
            f.tip = StatusTip(c);
            f.shield.gameObject.SetActive(c.block > 0);
        }

        void SetHp(Fighter f, float hp)
        {
            float ratio = f.maxHp > 0 ? Mathf.Clamp01(hp / f.maxHp) : 0f;
            f.hpFill.rectTransform.anchorMax = new Vector2(ratio, 1);
            f.hpText.text = $"HP {Mathf.RoundToInt(hp)}/{f.maxHp}";
        }

        static readonly Color DebuffColor = new Color(0.65f, 0.45f, 0.9f);
        static readonly Color SealColor = new Color(0.3f, 0.3f, 0.3f);

        // ---- 説明（マウスを乗せると出る） ----

        string intentTip = "";
        RectTransform tipPanel;
        TextMeshProUGUI tipText;

        /// <summary>target にマウスを乗せている間、text() の説明を position に出す。説明が空なら出さない。</summary>
        void AddTip(GameObject target, Func<string> text, Vector2 position)
        {
            var hover = target.AddComponent<HoverRelay>();
            hover.Entered += () =>
            {
                string t = text();
                if (string.IsNullOrEmpty(t)) return;
                if (tipPanel == null)
                {
                    tipPanel = UIFactory.Panel("Tip", transform, new Vector2(520, 150), Vector2.zero, new Color(0.08f, 0.05f, 0.04f, 0.95f)).rectTransform;
                    tipPanel.GetComponent<Image>().raycastTarget = false;
                    tipText = UIFactory.Text("Text", tipPanel, "", 24, PaperColor, new Vector2(496, 140), Vector2.zero, TextAlignmentOptions.TopLeft);
                }
                tipText.text = t;
                tipPanel.sizeDelta = new Vector2(520, Mathf.Max(60f, tipText.preferredHeight + 20f));
                tipText.rectTransform.sizeDelta = tipPanel.sizeDelta - new Vector2(24, 10);
                tipPanel.anchoredPosition = position;
                tipPanel.gameObject.SetActive(true);
                tipPanel.SetAsLastSibling();
            };
            hover.Exited += () =>
            {
                if (tipPanel != null) tipPanel.gameObject.SetActive(false);
            };
        }

        /// <summary>防御・筋力・弱体・毒の説明（仕様書 第6章）。何もなければ空。</summary>
        static string StatusTip(Combatant c)
        {
            var lines = new List<string>();
            if (c.block > 0) lines.Add($"<color=#8FB8FF>防御 {c.block}</color>：受けるダメージを {c.block} 減らす。ラウンドの終わりに 0 に戻る。");
            if (c.strength != 0) lines.Add($"<color=#FFD070>筋力 {c.strength:+0;-0}</color>：攻撃するとき、攻撃値に {c.strength} 足す。戦闘が終わると消える。");
            if (c.weak > 0) lines.Add($"<color=#C79BFF>弱体 {c.weak}</color>：攻撃値が {BattleResolver.WeakPercent}% になる（端数切り捨て）。ラウンドが終わるたびに 1 減る。");
            if (c.poison > 0) lines.Add($"<color=#8BE07A>毒 {c.poison}</color>：ラウンドの終わりに、防御を無視して {c.poison} ダメージ。そのあと毒が 1 減る。");
            return string.Join("\n", lines);
        }

        static string IntentExplanation(IntentType type)
        {
            switch (type)
            {
                case IntentType.Attack: return "ラウンドの終わりに攻撃してくる。防御に置いた出目で減らせる。";
                case IntentType.MultiAttack: return "何回かに分けて攻撃してくる。防御は合計のダメージから引かれる。";
                case IntentType.Block: return "このラウンド、敵の防御が増える。攻撃が通りにくい。";
                case IntentType.Buff: return "敵の筋力が上がる。次からの攻撃が強くなる。";
                case IntentType.Debuff: return $"あなたに弱体を与える。弱体の間は攻撃値が {BattleResolver.WeakPercent}% になる。";
                case IntentType.Seal: return "使用可能なダイスのうち一番強いものを封印する。戦闘が終わるまで使えない。";
                case IntentType.DiceRoll: return "サイコロを振って攻撃してくる。値は振るまでわからない（範囲は表示どおり）。";
                case IntentType.ResetDice: return "すべてのダイスを使用済みにする。そのままリフレッシュが起きる。";
                default: return "";
            }
        }

        void SetIntent(Intent intent, EnemyState e)
        {
            var sprite = art != null ? art.IntentSprite(intent.type) : null;
            intentIcon.sprite = sprite;
            intentIcon.color = sprite != null ? Color.white : FallbackIntentColor(intent.type);
            intentText.text = IntentShort(intent, e.strength, e.weak);
            intentTip = $"<b>{IntentLabel(intent, e.strength)}</b>\n{IntentExplanation(intent.type)}";

            bool changed = !shownIntent.HasValue || shownIntent.Value.type != intent.type || shownIntent.Value.value != intent.value;
            shownIntent = intent;
            if (changed && isActiveAndEnabled) StartCoroutine(UIAnim.Punch(intentIcon.transform.parent, 0.25f, 0.3f));
        }

        static Color FallbackIntentColor(IntentType type)
        {
            switch (type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll: return AttackColor;
                case IntentType.Block: return BlockColor;
                case IntentType.Debuff: return DebuffColor;
                case IntentType.Seal:
                case IntentType.ResetDice: return SealColor;
                default: return AccentColor;
            }
        }

        /// <summary>予告アイコンの横に出す短い文字。</summary>
        static string IntentShort(Intent intent, int strength, int weak)
        {
            switch (intent.type)
            {
                case IntentType.Attack: return BattleResolver.EnemyAttack(intent, strength, weak).ToString();
                case IntentType.MultiAttack:
                    int perHit = BattleResolver.ApplyWeak(intent.value, weak);
                    return strength != 0 ? $"{perHit}×{intent.Hits}+{strength}" : $"{perHit}×{intent.Hits}";
                case IntentType.DiceRoll when intent.minValue >= intent.maxValue:
                    return BattleResolver.EnemyAttack(intent, strength, weak).ToString();
                case IntentType.DiceRoll:
                    var min = intent; min.value = intent.minValue;
                    var max = intent; max.value = intent.maxValue;
                    return $"<size=30>{BattleResolver.EnemyAttack(min, strength, weak)}〜{BattleResolver.EnemyAttack(max, strength, weak)}</size>";
                case IntentType.Block: return intent.value.ToString();
                case IntentType.Buff: return $"+{intent.value}";
                case IntentType.Debuff: return $"<size=30>弱体{intent.value}</size>";
                case IntentType.Seal: return "<size=30>封印</size>";
                case IntentType.ResetDice: return "<size=26>振出し</size>";
                default: return "";
            }
        }

        public void SetLog(string text) => logText.text = text;

        public void SetBusy(bool busy) => stageGroup.blocksRaycasts = !busy;

        public void ShowContinue(string label)
        {
            continueLabel.text = label;
            continueButton.gameObject.SetActive(true);
            continueButton.transform.SetAsLastSibling();
            StartCoroutine(UIAnim.Punch(continueButton.transform, 0.15f, 0.3f));
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
                case IntentType.MultiAttack:
                    return $"多段攻撃 {intent.value}×{intent.Hits}";
                case IntentType.Debuff:
                    return $"妨害（弱体{intent.value}）";
                case IntentType.Seal:
                    return "封印";
                case IntentType.DiceRoll:
                    return intent.minValue < intent.maxValue ? $"賽振り（攻撃 {intent.minValue}〜{intent.maxValue}）" : $"賽振り（出目{intent.value / 2}：攻撃 {intent.value}）";
                case IntentType.ResetDice:
                    return "振り出しに戻れ";
                default:
                    return intent.ToString();
            }
        }

        void RebuildRolled(BattleState battle, bool ongoing, int hiddenRolled)
        {
            UIFactory.ClearChildren(rolledRoot);
            rolledFaces.Clear();
            if (!ongoing) return;

            int n = battle.Rolled.Count;
            for (int i = 0; i < n; i++)
            {
                var r = battle.Rolled[i];
                float x = (i - (n - 1) / 2f) * 280f;
                var face = DiceFaceView.Create($"RolledFace{i}", rolledRoot, art, 120, new Vector2(x, 40));
                face.SetValue(r.value);
                face.SetEngraving(r.dice.faces[r.faceIndex].engraving);
                rolledFaces.Add(face);
                UIFactory.Text($"RolledName{i}", rolledRoot, r.dice.DisplayName, 24, PaperColor, new Vector2(240, 30), new Vector2(x, 122)).outlineWidth = 0.25f;

                // 置いたときの実際の値（盾賽なら防御+2 など）をボタンに出す。転がっている最中は伏せる
                bool hidden = i >= n - hiddenRolled;
                string atkValue = hidden ? "？" : battle.EffectiveValue(r, Assignment.Attack).ToString();
                string blkValue = hidden ? "？" : battle.EffectiveValue(r, Assignment.Block).ToString();
                var atk = UIFactory.Button($"Attack{i}", rolledRoot, new Vector2(126, 56), new Vector2(x - 66, -60),
                    r.assignment == Assignment.Attack ? AttackColor : OffColor, $"攻撃 {atkValue}", 26, out var atkLabel);
                var blk = UIFactory.Button($"Block{i}", rolledRoot, new Vector2(126, 56), new Vector2(x + 66, -60),
                    r.assignment == Assignment.Block ? BlockColor : OffColor, $"防御 {blkValue}", 26, out var blkLabel);
                atkLabel.color = PaperColor;
                blkLabel.color = PaperColor;
                atk.interactable = ongoing && !hidden;
                blk.interactable = ongoing && !hidden;
                atk.onClick.AddListener(() => AssignClicked?.Invoke(r, Assignment.Attack));
                blk.onClick.AddListener(() => AssignClicked?.Invoke(r, Assignment.Block));
            }
        }

        void RebuildTray(BattleState battle, ICollection<DiceInstance> selected, bool ongoing)
        {
            UIFactory.ClearChildren(trayRoot);

            var ordered = battle.pouch.All.OrderBy(d => d.state == DiceState.Available ? 0 : 1).ToList();
            const float h = 150f, gap = 16f;
            float w = Mathf.Min(280f, (TrayWidth - gap * (ordered.Count - 1)) / Mathf.Max(1, ordered.Count));
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool isSelected = selected.Contains(die);
                string state = isSelected ? "選択中" : available ? "クリックで選ぶ" : die.state == DiceState.Sealed ? "<color=#7A1F1F>封印中</color>" : "使用済み";
                var card = DiceCard.Create($"Dice{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0), state, !available, isSelected);
                card.Button.interactable = ongoing && available && battle.CanRollMore;
                card.Button.onClick.AddListener(() => DieClicked?.Invoke(die));
                if (card.Button.interactable)
                {
                    var drag = card.gameObject.AddComponent<DragToRoll>();
                    drag.dropZone = dropZone.rectTransform;
                    drag.dragLayer = (RectTransform)transform;
                    drag.Dragging += on => dropZone.gameObject.SetActive(on);
                    drag.Dropped += () => DieDropped?.Invoke(die);
                }
            }
        }

        // ---- 演出 ----

        /// <summary>振ったダイスのうち、後ろから count 個を転がして見せる。</summary>
        public IEnumerator PlayRoll(BattleState battle, int count)
        {
            int start = battle.Rolled.Count - count;
            for (int i = start; i < battle.Rolled.Count; i++)
            {
                var r = battle.Rolled[i];
                StartCoroutine(rolledFaces[i].PlayRoll(r.dice, r.value, 0.6f, r.dice.faces[r.faceIndex].engraving));
            }
            yield return UIAnim.Wait(0.9f);
        }

        /// <summary>ラウンドの始まり：防御の予告なら、盾が張られるのを見せる。</summary>
        public IEnumerator PlayRoundStart(BattleState battle)
        {
            if (battle.Outcome != BattleOutcome.Ongoing) yield break;
            if (battle.EnemyIntent.type == IntentType.Block)
            {
                SpawnEffect(art != null ? art.fxBlock : null, enemy.home, 300f, 0.6f, BlockColor);
                Popup($"防御 +{battle.EnemyIntent.value}", enemy.home + new Vector2(0, 60), new Color(0.6f, 0.8f, 1f));
                yield return UIAnim.Wait(0.4f);
            }
        }

        /// <summary>
        /// 1ラウンドの解決を見せる：自分の攻撃 → （倒していなければ）敵の行動。
        /// before は Resolve の直前の値、result と battle は Resolve のあとの値。
        /// </summary>
        public IEnumerator PlayResolve(RoundSnapshot before, RoundResult result, BattleState battle)
        {
            SetBusy(true);

            // 自分の攻撃
            if (before.attack > 0)
            {
                yield return Lunge(player, +1);
                SpawnEffect(art != null ? art.fxSlash : null, enemy.home, 340f, 0.4f, DamageColor);
                if (result.dealt > 0)
                {
                    StartCoroutine(UIAnim.Shake(enemy.figure, 22f, 0.35f));
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(1f, 0.5f, 0.45f), 0.35f));
                    Sfx.Play(SoundId.Hit);
                    Popup($"-{result.dealt}", enemy.home + new Vector2(0, 80), DamageColor, 64);
                    yield return AnimateHp(enemy, before.enemyHp, before.enemyHp - result.dealt);
                }
                else
                {
                    SpawnEffect(art != null ? art.fxBlock : null, enemy.home, 300f, 0.5f, BlockColor);
                    Sfx.Play(SoundId.Block);
                    Popup("防がれた", enemy.home + new Vector2(0, 80), new Color(0.75f, 0.85f, 1f));
                    yield return UIAnim.Wait(0.35f);
                }
                yield return MoveBack(player);
            }

            // 攻撃で倒したときだけここで終わる（毒で倒れる場合はラウンドの終わりに見せる）
            if (battle.Outcome == BattleOutcome.Victory && before.enemyHp - result.dealt <= 0)
            {
                yield return Defeat(enemy, 1);
                SetBusy(false);
                yield break;
            }

            yield return UIAnim.Wait(0.2f);

            // 敵の行動
            var intent = result.enemyIntent;
            switch (intent.type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll:
                    if (intent.type == IntentType.DiceRoll)
                    {
                        // 賽振り：隠れていた値をここで見せる
                        Popup($"出目 {intent.value / 2} → 攻撃 {intent.value}", enemy.home + new Vector2(0, 120), AccentColor, 40);
                        yield return UIAnim.Punch(enemy.figure, 0.12f, 0.35f);
                    }
                    yield return Lunge(enemy, -1);
                    if (before.playerBlock > 0)
                    {
                        SpawnEffect(art != null ? art.fxBlock : null, player.home, 280f, 0.5f, BlockColor);
                        Sfx.Play(SoundId.Block);
                    }
                    if (result.taken > 0)
                    {
                        int hits = intent.type == IntentType.MultiAttack ? intent.Hits : 1;
                        for (int h = 0; h < hits; h++)
                        {
                            SpawnEffect(art != null ? art.fxHit : null, player.home + new Vector2(h * 30 - 30, h * 20), 280f, 0.35f, DamageColor);
                            StartCoroutine(UIAnim.Shake(player.figure, 24f, 0.2f));
                            if (hits > 1) yield return UIAnim.Wait(0.14f);
                        }
                        StartCoroutine(UIAnim.Shake(stage, 10f, 0.25f));
                        StartCoroutine(UIAnim.Flash(player.image, new Color(1f, 0.4f, 0.35f), 0.4f));
                        Sfx.Play(SoundId.Damage);
                        Popup($"-{result.taken}", player.home + new Vector2(0, 80), DamageColor, 64);
                        yield return AnimateHp(player, before.playerHp, before.playerHp - result.taken);
                    }
                    else
                    {
                        Sfx.Play(SoundId.Block);
                        Popup("防いだ！", player.home + new Vector2(0, 80), new Color(0.75f, 0.85f, 1f));
                        yield return UIAnim.Wait(0.35f);
                    }
                    yield return MoveBack(enemy);
                    break;
                case IntentType.Debuff:
                    yield return Lunge(enemy, -1);
                    StartCoroutine(UIAnim.Flash(player.image, DebuffColor, 0.5f));
                    Popup($"弱体 {intent.value}", player.home + new Vector2(0, 80), DebuffColor);
                    yield return UIAnim.Wait(0.4f);
                    yield return MoveBack(enemy);
                    break;
                case IntentType.Seal:
                    yield return Lunge(enemy, -1);
                    string sealedName = result.sealedDie != null ? result.sealedDie.DisplayName : "なし";
                    Popup($"封印：{sealedName}", new Vector2(0, -250), new Color(1f, 0.6f, 0.5f), 48);
                    StartCoroutine(UIAnim.Shake(trayRoot, 12f, 0.3f));
                    yield return UIAnim.Wait(0.5f);
                    yield return MoveBack(enemy);
                    break;
                case IntentType.ResetDice:
                    Popup("振り出しに戻れ！", new Vector2(0, 60), AccentColor, 64);
                    StartCoroutine(UIAnim.Shake(stage, 14f, 0.4f));
                    StartCoroutine(UIAnim.Shake(trayRoot, 16f, 0.4f));
                    yield return UIAnim.Punch(enemy.figure, 0.15f, 0.5f);
                    yield return UIAnim.Wait(0.3f);
                    break;
                case IntentType.Buff:
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(1f, 0.85f, 0.4f), 0.4f));
                    Popup($"筋力 +{result.enemyIntent.value}", enemy.home + new Vector2(0, 80), AccentColor);
                    yield return UIAnim.Punch(enemy.figure, 0.18f, 0.4f);
                    break;
                case IntentType.Block:
                    yield return UIAnim.Wait(0.2f);
                    break;
            }

            // ラウンド終了の毒
            var poisonColor = new Color(0.55f, 0.9f, 0.45f);
            if (result.enemyPoisonDamage > 0)
            {
                int hp = before.enemyHp - result.dealt;
                StartCoroutine(UIAnim.Flash(enemy.image, poisonColor, 0.4f));
                Sfx.Play(SoundId.Poison);
                Popup($"毒 -{result.enemyPoisonDamage}", enemy.home + new Vector2(0, 80), poisonColor, 52);
                yield return AnimateHp(enemy, hp, hp - result.enemyPoisonDamage);
                if (battle.Outcome == BattleOutcome.Victory) yield return Defeat(enemy, 1);
            }
            if (result.playerPoisonDamage > 0)
            {
                int hp = before.playerHp - result.taken;
                StartCoroutine(UIAnim.Flash(player.image, poisonColor, 0.4f));
                Sfx.Play(SoundId.Poison);
                Popup($"毒 -{result.playerPoisonDamage}", player.home + new Vector2(0, 80), poisonColor, 52);
                yield return AnimateHp(player, hp, hp - result.playerPoisonDamage);
            }

            if (battle.Outcome == BattleOutcome.Defeat)
            {
                yield return Defeat(player, -1);
            }
            yield return UIAnim.Wait(0.15f);
            SetBusy(false);
        }

        IEnumerator Lunge(Fighter f, int direction)
        {
            yield return UIAnim.MoveTo(f.figure, f.home + new Vector2(140f * direction, 10f), 0.14f, UIAnim.EaseInQuad);
        }

        IEnumerator MoveBack(Fighter f)
        {
            yield return UIAnim.MoveTo(f.figure, f.home, 0.2f);
        }

        IEnumerator AnimateHp(Fighter f, int from, int to)
        {
            yield return UIAnim.Tween(0.45f, t => SetHp(f, Mathf.Lerp(from, Mathf.Max(0, to), t)), UIAnim.EaseOutQuad);
        }

        /// <summary>倒れる：傾きながら沈んで消える。</summary>
        IEnumerator Defeat(Fighter f, int direction)
        {
            Sfx.Play(f == enemy ? SoundId.Victory : SoundId.Defeat);
            var group = f.figure.gameObject.AddComponent<CanvasGroup>();
            Vector2 start = f.figure.anchoredPosition;
            yield return UIAnim.Tween(0.6f, t =>
            {
                f.figure.localRotation = Quaternion.Euler(0, 0, -25f * direction * t);
                f.figure.anchoredPosition = start + new Vector2(0, -60f * t);
                group.alpha = 1f - t;
            });
        }

        void SpawnEffect(Sprite sprite, Vector2 pos, float size, float duration, Color fallback)
        {
            var image = UIFactory.Picture("Fx", stage, sprite, Vector2.one * size, pos, new Color(fallback.r, fallback.g, fallback.b, 0.6f));
            StartCoroutine(EffectRoutine(image, duration));
        }

        IEnumerator EffectRoutine(Image image, float duration)
        {
            var baseColor = image.color;
            yield return UIAnim.Tween(duration, t =>
            {
                image.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.15f, UIAnim.EaseOutQuad(t));
                image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (t < 0.5f ? 1f : (1f - t) * 2f));
            });
            Destroy(image.gameObject);
        }

        /// <summary>数字や文字が飛び出して、上に浮かびながら消える。</summary>
        void Popup(string text, Vector2 pos, Color color, float size = 44)
        {
            var label = UIFactory.Text("Popup", stage, text, size, color, new Vector2(400, 90), pos);
            label.fontStyle = FontStyles.Bold;
            label.outlineWidth = 0.3f;
            label.outlineColor = new Color32(30, 10, 5, 255);
            StartCoroutine(PopupRoutine(label, pos));
        }

        IEnumerator PopupRoutine(TextMeshProUGUI label, Vector2 pos)
        {
            var rt = label.rectTransform;
            var baseColor = label.color;
            yield return UIAnim.Tween(0.9f, t =>
            {
                rt.anchoredPosition = pos + new Vector2(0, 80f * UIAnim.EaseOutQuad(t));
                rt.localScale = Vector3.one * (t < 0.15f ? Mathf.Lerp(0.5f, 1.2f, t / 0.15f) : 1f);
                label.color = new Color(baseColor.r, baseColor.g, baseColor.b, t < 0.6f ? 1f : (1f - t) / 0.4f);
            });
            Destroy(label.gameObject);
        }
    }
}
