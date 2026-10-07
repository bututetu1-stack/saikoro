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
        }

        UIArt art;
        RectTransform stage;
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

            rolledRoot = UIFactory.Rect("Rolled", stage, new Vector2(700, 260), new Vector2(0, 170));

            var previewPanel = UIFactory.Panel("PreviewPanel", stage, new Vector2(760, 60), new Vector2(0, -10), ShadeColor);
            previewText = UIFactory.Text("Preview", previewPanel.transform, "", 32, AccentColor, new Vector2(740, 56), Vector2.zero);

            var logPanel = UIFactory.Panel("LogPanel", stage, new Vector2(900, 84), new Vector2(0, -170), ShadeColor);
            logText = UIFactory.Text("Log", logPanel.transform, "", 26, PaperColor, new Vector2(870, 80), Vector2.zero);

            trayRoot = UIFactory.Rect("DiceTray", stage, new Vector2(1300, 160), new Vector2(-170, -375));

            rollButton = UIFactory.Button("RollButton", stage, new Vector2(260, 80), new Vector2(760, -320), ButtonColor, "振る", 34, out _);
            rollButton.onClick.AddListener(() => RollClicked?.Invoke());
            resolveButton = UIFactory.Button("ResolveButton", stage, new Vector2(260, 80), new Vector2(760, -420), AccentColor, "", 32, out resolveLabel);
            resolveButton.onClick.AddListener(() => ResolveClicked?.Invoke());
            continueButton = UIFactory.Button("ContinueButton", stage, new Vector2(420, 96), new Vector2(0, 170), AccentColor, "", 36, out continueLabel);
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            continueButton.gameObject.SetActive(false);
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
            return f;
        }

        // ---- 表示の更新 ----

        public void Refresh(BattleState battle, ICollection<DiceInstance> selected)
        {
            bool ongoing = battle.Outcome == BattleOutcome.Ongoing;

            titleText.text = $"戦闘　ラウンド {battle.Round}　　{battle.enemy.data.displayName}";
            SetFighter(player, battle.player);
            SetFighter(enemy, battle.enemy);

            if (ongoing) SetIntent(battle.EnemyIntent, battle.enemy.strength);
            intentIcon.transform.parent.gameObject.SetActive(ongoing);

            RebuildRolled(battle, ongoing);
            RebuildTray(battle, selected, ongoing);

            if (ongoing)
            {
                var preview = battle.Preview();
                previewText.text = $"与えるダメージ {preview.dealt}　／　受けるダメージ {preview.taken}";
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
            f.statusText.text = string.Join("　", parts);
            f.shield.gameObject.SetActive(c.block > 0);
        }

        void SetHp(Fighter f, float hp)
        {
            float ratio = f.maxHp > 0 ? Mathf.Clamp01(hp / f.maxHp) : 0f;
            f.hpFill.rectTransform.anchorMax = new Vector2(ratio, 1);
            f.hpText.text = $"HP {Mathf.RoundToInt(hp)}/{f.maxHp}";
        }

        void SetIntent(Intent intent, int strength)
        {
            var sprite = art != null ? art.IntentSprite(intent.type) : null;
            intentIcon.sprite = sprite;
            intentIcon.color = sprite != null ? Color.white : (intent.type == IntentType.Attack ? AttackColor : intent.type == IntentType.Block ? BlockColor : AccentColor);
            switch (intent.type)
            {
                case IntentType.Attack: intentText.text = BattleResolver.EnemyAttack(intent, strength).ToString(); break;
                case IntentType.Block: intentText.text = intent.value.ToString(); break;
                case IntentType.Buff: intentText.text = $"+{intent.value}"; break;
            }

            bool changed = !shownIntent.HasValue || shownIntent.Value.type != intent.type || shownIntent.Value.value != intent.value;
            shownIntent = intent;
            if (changed && isActiveAndEnabled) StartCoroutine(UIAnim.Punch(intentIcon.transform.parent, 0.25f, 0.3f));
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
                default:
                    return intent.ToString();
            }
        }

        void RebuildRolled(BattleState battle, bool ongoing)
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
                rolledFaces.Add(face);
                UIFactory.Text($"RolledName{i}", rolledRoot, r.dice.DisplayName, 24, PaperColor, new Vector2(240, 30), new Vector2(x, 122)).outlineWidth = 0.25f;

                // 置いたときの実際の値（盾賽なら防御+2 など）をボタンに出す
                var atk = UIFactory.Button($"Attack{i}", rolledRoot, new Vector2(126, 56), new Vector2(x - 66, -60),
                    r.assignment == Assignment.Attack ? AttackColor : OffColor, $"攻撃 {battle.EffectiveValue(r, Assignment.Attack)}", 26, out var atkLabel);
                var blk = UIFactory.Button($"Block{i}", rolledRoot, new Vector2(126, 56), new Vector2(x + 66, -60),
                    r.assignment == Assignment.Block ? BlockColor : OffColor, $"防御 {battle.EffectiveValue(r, Assignment.Block)}", 26, out var blkLabel);
                atkLabel.color = PaperColor;
                blkLabel.color = PaperColor;
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
            const float w = 280f, h = 150f, gap = 20f;
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool isSelected = selected.Contains(die);
                string state = isSelected ? "選択中" : available ? "クリックで選ぶ" : "使用済み";
                var card = DiceCard.Create($"Dice{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0), state, !available, isSelected);
                card.Button.interactable = ongoing && available && battle.CanRollMore;
                card.Button.onClick.AddListener(() => DieClicked?.Invoke(die));
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
                StartCoroutine(rolledFaces[i].PlayRoll(r.dice, r.value, 0.6f));
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
                    Popup($"-{result.dealt}", enemy.home + new Vector2(0, 80), DamageColor, 64);
                    yield return AnimateHp(enemy, before.enemyHp, before.enemyHp - result.dealt);
                }
                else
                {
                    SpawnEffect(art != null ? art.fxBlock : null, enemy.home, 300f, 0.5f, BlockColor);
                    Popup("防がれた", enemy.home + new Vector2(0, 80), new Color(0.75f, 0.85f, 1f));
                    yield return UIAnim.Wait(0.35f);
                }
                yield return MoveBack(player);
            }

            if (battle.Outcome == BattleOutcome.Victory)
            {
                yield return Defeat(enemy, 1);
                SetBusy(false);
                yield break;
            }

            yield return UIAnim.Wait(0.2f);

            // 敵の行動
            switch (result.enemyIntent.type)
            {
                case IntentType.Attack:
                    yield return Lunge(enemy, -1);
                    if (before.playerBlock > 0) SpawnEffect(art != null ? art.fxBlock : null, player.home, 280f, 0.5f, BlockColor);
                    if (result.taken > 0)
                    {
                        SpawnEffect(art != null ? art.fxHit : null, player.home, 300f, 0.4f, DamageColor);
                        StartCoroutine(UIAnim.Shake(player.figure, 24f, 0.35f));
                        StartCoroutine(UIAnim.Shake(stage, 10f, 0.25f));
                        StartCoroutine(UIAnim.Flash(player.image, new Color(1f, 0.4f, 0.35f), 0.4f));
                        Popup($"-{result.taken}", player.home + new Vector2(0, 80), DamageColor, 64);
                        yield return AnimateHp(player, before.playerHp, before.playerHp - result.taken);
                    }
                    else
                    {
                        Popup("防いだ！", player.home + new Vector2(0, 80), new Color(0.75f, 0.85f, 1f));
                        yield return UIAnim.Wait(0.35f);
                    }
                    yield return MoveBack(enemy);
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
