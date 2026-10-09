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
        // 割り振ったボタンの色。赤・青の数（効果で上下した値）が埋もれないように、攻撃・防御の色を濃くしたもの
        static readonly Color ChosenAttackColor = new Color(0.55f, 0.16f, 0.12f);
        static readonly Color ChosenBlockColor = new Color(0.14f, 0.26f, 0.52f);
        static readonly Color HpBarColor = new Color(0.8f, 0.2f, 0.2f);
        static readonly Color BlockBarColor = new Color(0.25f, 0.5f, 0.95f);  // 防御があるあいだの HP バー（STS と同じく青）
        static readonly Color OffColor = new Color(0.4f, 0.37f, 0.33f);
        static readonly Color DamageColor = new Color(1f, 0.35f, 0.25f);

        public event Action<DiceInstance> DieClicked;
        public event Action<DiceInstance> DieDropped;   // 札を振る場所へドラッグして離した
        public event Action RollClicked;
        public event Action<RolledDie, Assignment> AssignClicked;
        public event Action<RolledDie> RerollClicked;   // 再転で振り直す
        public event Action<RolledDie> FateClicked;     // 運命の糸で値を変える
        public event Action SuspendClicked;              // 保存して中断
        public event Action SettingsClicked;
        public event Action ResolveClicked;
        public event Action ContinueClicked;

        class Fighter
        {
            public RectTransform figure;
            public Image image;
            public Image shield;
            public Image hpFill;
            public Image poisonFill;  // 毒で次に減る分（HP の右端を緑に）
            public int poison;
            public TextMeshProUGUI hpText;
            public string statusKey;           // いま並べている印（同じなら作り直さない）
            public RectTransform statusRoot;   // 状態の印を並べる所（HP の下）
            public Vector2 statusPos;
            public float barWidth;
            public GameObject blockBadge;      // 防御の盾（HP バーの左端）
            public TextMeshProUGUI blockText;
            public string blockTip = "";
            public Vector2 home;
            public int maxHp;

        }

        UIArt art;
        int layer;
        RectTransform stage;
        RelicBar relicBar;
        public RelicBar Relics => relicBar;
        CharmBar charmBar;
        public CharmBar Charms => charmBar;
        CanvasGroup stageGroup;
        Fighter player;
        /// <summary>敵1体ぶんの表示（絵・HP・予告・狙いの印）。</summary>
        class EnemySlot
        {
            public Fighter f;
            public Image intentIcon;
            public TextMeshProUGUI intentText;
            public GameObject intentBlockBadge;   // 「攻撃＋防御」などの防御の盾と数（攻撃の数の右）
            public TextMeshProUGUI intentBlockText;
            public RectTransform intentBack;
            public Intent? shown;
            public string tip = "";
            public GameObject targetMark;
            public bool dead;
        }

        readonly List<EnemySlot> slots = new List<EnemySlot>();

        /// <summary>敵の絵をクリックした（狙いを変える。引数は敵の番号）。</summary>
        public event Action<int> EnemyClicked;
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
        TextMeshProUGUI diceCountText;
        Button continueButton;
        TextMeshProUGUI continueLabel;

        public static BattleView Create(Transform canvas, UIArt art, IReadOnlyList<EnemyData> enemyData, bool isBoss, int layer = 0)
        {
            var root = UIFactory.Stretch("BattleView", canvas);
            var view = root.gameObject.AddComponent<BattleView>();
            view.art = art;
            view.layer = layer;
            view.Build(enemyData, isBoss);
            return view;
        }

        void Build(IReadOnlyList<EnemyData> enemyData, bool isBoss)
        {
            UIFactory.Background(transform, art != null ? art.BattleBackgroundFor(layer) : null, new Color(0.25f, 0.18f, 0.15f));
            stage = UIFactory.Stretch("Stage", transform);
            stageGroup = stage.gameObject.AddComponent<CanvasGroup>();

            var bar = UIFactory.Panel("TitleBar", stage, new Vector2(1920, 50), new Vector2(0, 515), ShadeColor);
            titleText = UIFactory.Text("Title", bar.transform, "", 30, PaperColor, new Vector2(1800, 48), Vector2.zero);
            // 保存して中断（続きからは、この戦闘の最初から）
            var suspend = UIFactory.Button("SuspendButton", bar.transform, new Vector2(140, 40), new Vector2(880, 0), new Color(0.75f, 0.68f, 0.58f), "中断", 24, out _);
            suspend.onClick.AddListener(() => SuspendClicked?.Invoke());
            var settings = UIFactory.Button("SettingsButton", bar.transform, new Vector2(140, 40), new Vector2(730, 0), new Color(0.75f, 0.68f, 0.58f), "設定", 24, out _);
            settings.onClick.AddListener(() => SettingsClicked?.Invoke());

            player = CreateFighter("Player", art != null ? art.player : null, null, new Vector2(-560, 150), 300f);
            // 敵：1体なら右のまん中、2体なら左右に並べる（先頭が左）
            int n = enemyData.Count;
            for (int i = 0; i < n; i++)
            {
                var data = enemyData[i];
                float x = n == 1 ? 560f : 380f + i * 380f;
                float size = n == 1 ? (isBoss ? 440f : 340f) : 280f;
                var pos = new Vector2(x, n == 1 && isBoss ? 190 : 170);
                var slot = new EnemySlot
                {
                    f = CreateFighter(n == 1 ? "Enemy" : $"Enemy{i}", art != null ? art.EnemySpriteFor(data) : null, data.displayName, pos, size, n == 1 ? 380f : 320f),
                };

                var intentBack = UIFactory.Panel(n == 1 ? "IntentBack" : $"IntentBack{i}", stage, new Vector2(220, 76), new Vector2(x, 440), ShadeColor);
                slot.intentIcon = UIFactory.Picture("IntentIcon", intentBack.transform, null, new Vector2(70, 70), new Vector2(-62, 0), AttackColor);
                slot.intentText = UIFactory.Text("IntentText", intentBack.transform, "", 40, PaperColor, new Vector2(140, 80), new Vector2(42, 0));
                slot.intentText.fontStyle = FontStyles.Bold;
                slot.intentText.textWrappingMode = TextWrappingModes.NoWrap; // 「裏返し 防8」などが2行に折れないように
                // 「攻撃＋防御」などの防御：攻撃の数の右に、盾と数を同じ大きさで並べる（STS に近い見せ方）。そのときは予告の枠を広げる
                var blockSprite = art != null ? art.intentBlock : null;
                var blockBadge = blockSprite != null
                    ? (Graphic)UIFactory.Picture("IntentBlock", intentBack.transform, blockSprite, new Vector2(64, 64), new Vector2(60, 0), Color.white)
                    : UIFactory.Panel("IntentBlock", intentBack.transform, new Vector2(56, 56), new Vector2(60, 0), BlockBarColor);
                blockBadge.raycastTarget = false;
                slot.intentBlockBadge = blockBadge.gameObject;
                slot.intentBlockText = UIFactory.Text("IntentBlockText", intentBack.transform, "", 40, PaperColor, new Vector2(80, 80), new Vector2(128, 0));
                slot.intentBlockText.fontStyle = FontStyles.Bold;
                slot.intentBlockText.textWrappingMode = TextWrappingModes.NoWrap;
                slot.intentBlockBadge.SetActive(false);
                slot.intentBlockText.gameObject.SetActive(false);
                slot.intentBack = intentBack.rectTransform;
                // 予告にマウスを乗せると、何をしてくるかの説明
                var s = slot;
                AddTip(intentBack.gameObject, () => s.tip, new Vector2(x > 600 ? 560 : x, 300));

                // 敵の特性（棘・鉄壁など）を、HP の上に小さな印で並べる（STS の能力アイコンと同じ見せ方）。マウスを乗せると説明
                float barWidth = n == 1 ? 380f : 320f;
                var traits = Traits(data);
                for (int t = 0; t < traits.Count; t++)
                {
                    var (mark, color, label, desc, icon, value) = traits[t];
                    var badgePos = new Vector2(x - barWidth / 2f + 22f + t * 54f, -60f);
                    var badge = MarkBadge($"Trait{i}_{t}", stage, badgePos, mark, color, icon, value != null);
                    if (value != null) BadgeValue(badge, value); // 鉄壁の「10」、棘の「3」など
                    string tipText = $"<b><color=#{ColorUtility.ToHtmlStringRGB(color)}>{label}</color></b>\n{desc}";
                    AddTip(badge.gameObject, () => tipText, new Vector2(x > 600 ? 560 : x, -230));
                }

                if (n > 1)
                {
                    // 狙っている敵の印。敵の絵をクリックすると狙いを変える
                    var mark = UIFactory.Text("Target", stage, "▼ 狙い", 30, AccentColor, new Vector2(200, 40), new Vector2(x, 375));
                    mark.fontStyle = FontStyles.Bold;
                    mark.outlineWidth = 0.25f;
                    mark.outlineColor = new Color32(30, 15, 5, 255);
                    slot.targetMark = mark.gameObject;
                    slot.f.image.raycastTarget = true;
                    var button = slot.f.image.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    int index = i;
                    button.onClick.AddListener(() => EnemyClicked?.Invoke(index));
                }
                slots.Add(slot);
            }

            rolledRoot = UIFactory.Rect("Rolled", stage, new Vector2(700, 260), new Vector2(0, 170));
            // ダイス札をここへドラッグして離すと振る。ドラッグ中だけ枠を見せる
            dropZone = UIFactory.Panel("DropZone", stage, new Vector2(1000, 420), new Vector2(0, 120), new Color(1f, 0.85f, 0.4f, 0.14f));
            dropZone.raycastTarget = false;
            var dropLabel = UIFactory.Text("Label", dropZone.transform, "ここで離すと振る", 40, new Color(1f, 0.9f, 0.6f, 0.9f), new Vector2(900, 60), new Vector2(0, 170));
            dropLabel.fontStyle = FontStyles.Bold;
            dropZone.gameObject.SetActive(false);

            var previewPanel = UIFactory.Panel("PreviewPanel", stage, new Vector2(760, 60), new Vector2(0, -10), ShadeColor);
            previewText = UIFactory.Text("Preview", previewPanel.transform, "", 32, AccentColor, new Vector2(740, 56), Vector2.zero);

            // 左右の HP の下に状態の印が並ぶので、重ならない幅にする
            float logLeft = -350f, logRight = (n == 1 ? 560f - 190f : 380f - 160f) - 20f;
            var logPanel = UIFactory.Panel("LogPanel", stage, new Vector2(logRight - logLeft, 84), new Vector2((logLeft + logRight) / 2f, -170), ShadeColor);
            logText = UIFactory.Text("Log", logPanel.transform, "", 26, PaperColor, new Vector2(logRight - logLeft - 20f, 80), Vector2.zero);
            logText.enableAutoSizing = true;
            logText.fontSizeMax = 26;
            logText.fontSizeMin = 16;

            // 左端から「振る」ボタンの手前まで（ダイスが多いときは札を細くして収める）
            // ダイスが多いときは札を細くせず、横にスクロールできるようにする（開発者の要望）
            trayRoot = UIFactory.HorizontalScroll("DiceTray", stage, new Vector2(TrayWidth, 174), new Vector2(-180, -375));

            // 振れる数（「振る」の上）
            diceCountText = UIFactory.Text("DiceCount", stage, "", 24, PaperColor, new Vector2(400, 34), new Vector2(740, -262));
            diceCountText.textWrappingMode = TextWrappingModes.NoWrap;
            diceCountText.outlineWidth = 0.25f;
            diceCountText.outlineColor = new Color32(20, 12, 8, 255);
            rollButton = UIFactory.Button("RollButton", stage, new Vector2(260, 80), new Vector2(760, -320), ButtonColor, "振る", 34, out _);
            rollButton.onClick.AddListener(() => RollClicked?.Invoke());
            resolveButton = UIFactory.Button("ResolveButton", stage, new Vector2(260, 80), new Vector2(760, -420), AccentColor, "", 32, out resolveLabel);
            resolveButton.onClick.AddListener(() => ResolveClicked?.Invoke());
            continueButton = UIFactory.Button("ContinueButton", stage, new Vector2(420, 96), new Vector2(0, 170), AccentColor, "", 36, out continueLabel);
            continueButton.onClick.AddListener(() => ContinueClicked?.Invoke());
            continueButton.gameObject.SetActive(false);
            // 敵の予告に重ならないよう、左の敵の予告の手前まで（多いときはアイコンを縮める）
            relicBar = RelicBar.Create(stage, new Vector2(-945, 482), 1180f);
            // 持っているお守り（レリックの下。右上だと2体目の敵の予告に重なっていた）
            charmBar = CharmBar.Create(stage, new Vector2(-945, 408));
            relicBar.transform.SetAsLastSibling(); // レリックの説明がお守りの下に隠れないように
        }

        Fighter CreateFighter(string name, Sprite sprite, string fallbackName, Vector2 pos, float size, float barWidth = 380f)
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
            var back = UIFactory.Panel(name + "HpBack", stage, new Vector2(barWidth, 36), barPos, new Color(0.1f, 0.06f, 0.05f, 0.9f));
            f.hpFill = UIFactory.Panel("Fill", back.transform, Vector2.zero, Vector2.zero, HpBarColor);
            var fillRect = f.hpFill.rectTransform;
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(1, 1);
            fillRect.offsetMin = new Vector2(3, 3);
            fillRect.offsetMax = new Vector2(-3, -3);
            // 次に毒で減る分を、HP の右端に緑で重ねる（STS と同じ見せ方）
            f.poisonFill = UIFactory.Panel("PoisonFill", back.transform, Vector2.zero, Vector2.zero, new Color(0.35f, 0.75f, 0.25f));
            f.poisonFill.raycastTarget = false;
            f.poisonFill.gameObject.SetActive(false);
            f.hpText = UIFactory.Text("HpText", back.transform, "", 26, PaperColor, new Vector2(barWidth, 36), Vector2.zero);
            f.hpText.fontStyle = FontStyles.Bold;
            // 防御（STS と同じ見せ方）：防御があるあいだは HP バーを青くし、バーの左端に盾と数を出す
            // （前は絵の後ろに盾の絵を出していたが、絵に隠れて見づらかった）
            var blockSprite = art != null ? art.intentBlock : null;
            var badge = blockSprite != null
                ? (Graphic)UIFactory.Picture(name + "Block", stage, blockSprite, new Vector2(60, 60), barPos + new Vector2(-barWidth / 2f - 4f, 0), Color.white)
                : UIFactory.Panel(name + "Block", stage, new Vector2(52, 52), barPos + new Vector2(-barWidth / 2f - 4f, 0), BlockBarColor);
            badge.raycastTarget = true;
            f.blockBadge = badge.gameObject;
            f.blockText = UIFactory.Text("Value", badge.transform, "", 26, Color.white, new Vector2(56, 40), new Vector2(0, 2));
            f.blockText.fontStyle = FontStyles.Bold;
            f.blockText.outlineWidth = 0.3f;
            f.blockText.outlineColor = new Color32(10, 20, 50, 255);
            AddTip(f.blockBadge, () => f.blockTip, barPos + new Vector2(0, -150));
            f.blockBadge.SetActive(false);
            // 状態（防御・筋力・毒など）は、HP の下に印で並べる（STS と同じ見せ方。前は「防御 4　筋力 +1」の文だった）
            // 印にマウスを乗せると、その状態の説明が出る
            f.statusRoot = UIFactory.Rect(name + "Status", stage, new Vector2(barWidth, 44), barPos + new Vector2(0, -44));
            f.barWidth = barWidth;
            f.statusPos = barPos + new Vector2(0, -44);
            return f;
        }

        // ---- 表示の更新 ----

        /// <param name="hiddenRolled">振ったばかりで、まだ転がっている最中のダイスの数（後ろから）。出目や値を「？」にして伏せる。</param>
        public void Refresh(BattleState battle, ICollection<DiceInstance> selected, int hiddenRolled = 0)
        {
            bool ongoing = battle.Outcome == BattleOutcome.Ongoing;

            titleText.text = $"戦闘　ラウンド {battle.Round}　　{string.Join("・", battle.enemies.Select(e => e.data.displayName).Distinct())}"
                + (battle.enemies.Count > 1 ? $"×{battle.enemies.Count}　（敵をクリックで狙いを変える）" : "");
            SetFighter(player, battle.player);
            for (int i = 0; i < slots.Count && i < battle.enemies.Count; i++)
            {
                var slot = slots[i];
                var e = battle.enemies[i];
                if (!slot.dead) SetFighter(slot.f, e);
                bool alive = ongoing && !e.IsDead;
                if (alive) SetIntent(slot, e.CurrentIntent, e, battle.player.vulnerable);
                slot.intentIcon.transform.parent.gameObject.SetActive(alive);
                if (slot.targetMark != null) slot.targetMark.SetActive(alive && e == battle.Target);
            }

            RebuildRolled(battle, ongoing, hiddenRolled);
            RebuildTray(battle, selected, ongoing);

            if (ongoing)
            {
                var preview = battle.Preview();
                string taken = preview.TakenIsRange ? $"{preview.takenMin}〜{preview.taken}" : preview.taken.ToString();
                previewText.text = hiddenRolled > 0
                    ? "与えるダメージ ？　／　受けるダメージ ？"
                    : $"与えるダメージ {Tinted(preview.dealt.ToString(), preview.dealtTrend)}　／　受けるダメージ {Tinted(taken, preview.takenTrend)}";
            }
            previewText.transform.parent.gameObject.SetActive(ongoing);

            rollButton.gameObject.SetActive(ongoing);
            resolveButton.gameObject.SetActive(ongoing);
            rollButton.interactable = selected.Count > 0;
            resolveLabel.text = battle.Rolled.Count == 0 ? "パス" : "決定";
            // 1ラウンドに振れる数と、残りの数（開発者の要望：3個振れることを明記）
            int left = Math.Max(0, battle.MaxDicePerRound - battle.Rolled.Count);
            diceCountText.text = ongoing ? $"1ラウンド {battle.MaxDicePerRound} 個まで　<color=#FFD24D>あと {left} 個</color>" : "";
        }

        /// <summary>敵の特性（印・色・名前・説明・絵・右下の数）。HP の上に印で並べる。絵がなければ印の字で出す。数は鉄壁の上限・棘のダメージなど。</summary>
        List<(string mark, Color color, string label, string desc, Sprite icon, string value)> Traits(EnemyData data)
        {
            var list = new List<(string, Color, string, string, Sprite, string)>();
            if (data.thorns > 0)
                list.Add(("棘", new Color(1f, 0.5f, 0.4f), $"棘 {data.thorns}",
                    $"置いたときの値が {data.thornsMinValue} 以上のダイスでこの敵を攻撃すると、ダイス1個ごとに {data.thorns} ダメージを受ける（防御無視）。大きい目は防御に回そう。", art?.traitThorns, data.thorns.ToString()));
            if (data.damageCapPerRound > 0)
                list.Add(("壁", new Color(0.6f, 0.8f, 1f), "鉄壁", $"1ラウンドに {data.damageCapPerRound} までしかダメージを受けない。何ラウンドかに分けて削ろう。", art?.traitWall, data.damageCapPerRound.ToString()));
            if (data.allyDefeatedStrength > 0)
                list.Add(("怒", new Color(1f, 0.6f, 0.3f), "仲間思い", $"仲間が倒れると、残ったほうが筋力 +{data.allyDefeatedStrength}。同じラウンドにまとめて倒すと怒らない。", art?.traitAlly, $"+{data.allyDefeatedStrength}"));
            if (data.enrageHpPercent > 0)
                list.Add(("狂", new Color(1f, 0.35f, 0.35f), "激昂", $"HP が {data.enrageHpPercent}% 以下になると、攻撃が {data.enrageAttackPercent}% になる。", art?.traitEnrage, null));
            if (data.phase2HpPercent > 0)
                list.Add(("変", new Color(0.8f, 0.55f, 1f), "変身", $"HP が {data.phase2HpPercent}% 以下になると、行動が変わる。", art?.traitPhase, null));
            if (data.pattern.Exists(p => p.type == IntentType.Invert))
                list.Add(("裏", new Color(0.85f, 0.45f, 0.85f), "天邪鬼", "ときどき「裏返し」を予告する。そのラウンドに振った出目は「7−出目」になる。低い目のダイスを振ろう。", art?.traitInvert, null));
            return list;
        }

        void SetFighter(Fighter f, Combatant c)
        {
            f.maxHp = c.maxHp;
            f.poison = c.poison;
            SetHp(f, c.hp);

            // 防御：HP バーを青く、左端に盾と数（STS と同じ）
            f.shield.gameObject.SetActive(false);
            f.hpFill.color = c.block > 0 ? BlockBarColor : HpBarColor;
            f.blockBadge.SetActive(c.block > 0);
            f.blockText.text = c.block > 0 ? c.block.ToString() : "";
            f.blockTip = $"<color=#8FB8FF>防御 {c.block}</color>：受けるダメージを {c.block} 減らす。ラウンドの終わりに 0 に戻る（敵は次の行動の前）。";

            // そのほかの状態：HP の下に印を並べる。前と同じなら作り直さない（マウスを乗せている印が消えないように）
            var statuses = Statuses(c);
            string key = string.Join("|", statuses.Select(s => s.mark + s.value));
            if (key == f.statusKey) return;
            f.statusKey = key;
            UIFactory.ClearChildren(f.statusRoot);
            for (int i = 0; i < statuses.Count; i++)
            {
                var (mark, color, value, tip, icon) = statuses[i];
                var badge = MarkBadge($"Status{i}", f.statusRoot, new Vector2(-f.barWidth / 2f + 22f + i * 54f, 0), mark, color, icon, value != null);
                if (value != null) BadgeValue(badge, value);
                AddTip(badge.gameObject, () => tip, f.statusPos + new Vector2(0, -110));
            }
        }

        /// <summary>
        /// 状態・特性の印（42×42）。絵があれば絵、なければ色の枠に漢字1文字。
        /// withValue なら、右下に数を置くので字を少し左上に寄せる。
        /// </summary>
        Image MarkBadge(string name, Transform parent, Vector2 position, string mark, Color color, Sprite icon, bool withValue = false)
        {
            var badge = UIFactory.Panel(name, parent, new Vector2(42, 42), position, new Color(0.1f, 0.06f, 0.05f, 0.92f));
            badge.raycastTarget = true;
            var outline = badge.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2, -2);
            if (icon != null)
            {
                UIFactory.Picture("Icon", badge.transform, icon, new Vector2(40, 40), Vector2.zero, Color.white).raycastTarget = false;
            }
            else
            {
                var m = UIFactory.Text("Mark", badge.transform, mark, withValue ? 22 : 26, color, new Vector2(42, 42), withValue ? new Vector2(-6, 5) : Vector2.zero);
                m.fontStyle = FontStyles.Bold;
            }
            return badge;
        }

        /// <summary>印の右下に小さく数を出す（STS と同じ）。</summary>
        static void BadgeValue(Image badge, string value)
        {
            var v = UIFactory.Text("Value", badge.transform, value, 18, Color.white, new Vector2(30, 20), new Vector2(5, -11), TextAlignmentOptions.Right);
            v.textWrappingMode = TextWrappingModes.NoWrap;
            v.fontStyle = FontStyles.Bold;
            v.outlineWidth = 0.3f;
            v.outlineColor = new Color32(0, 0, 0, 255);
        }

        /// <summary>状態の印（印の字・色・右下の数・説明・絵）。防御は HP バーの盾で見せるので、ここには入れない。</summary>
        List<(string mark, Color color, string value, string tip, Sprite icon)> Statuses(Combatant c)
        {
            var list = new List<(string, Color, string, string, Sprite)>();
            if (c.strength != 0) list.Add(("力", new Color(1f, 0.82f, 0.44f), c.strength.ToString("+0;-0"),
                $"<color=#FFD070>筋力 {c.strength:+0;-0}</color>：攻撃するとき、攻撃値に {c.strength} 足す（多段攻撃は1回ごと）。戦闘が終わると消える。", art?.statusStrength));
            if (c.weak > 0) list.Add(("脱", new Color(0.78f, 0.6f, 1f), c.weak.ToString(),
                $"<color=#C79BFF>脱力 {c.weak}</color>：与えるダメージが {BattleResolver.WeakPercent}% になる（端数切り捨て）。ラウンドが終わるたびに 1 減る。", art?.statusWeak));
            if (c.vulnerable > 0) list.Add(("弱", new Color(1f, 0.6f, 0.48f), c.vulnerable.ToString(),
                $"<color=#FF9A7A>弱体 {c.vulnerable}</color>：受けるダメージが {BattleResolver.VulnerablePercent}% になる（防御で減らす前）。ラウンドが終わるたびに 1 減る。", art?.statusVulnerable));
            if (c.frail > 0) list.Add(("脆", new Color(0.62f, 0.78f, 0.85f), c.frail.ToString(),
                $"<color=#9FC7D9>脆弱 {c.frail}</color>：作れる防御が {BattleResolver.FrailPercent}% になる（端数切り捨て）。ラウンドが終わるたびに 1 減る。", art?.statusFrail));
            if (c.poison > 0) list.Add(("毒", new Color(0.55f, 0.88f, 0.48f), c.poison.ToString(),
                $"<color=#8BE07A>毒 {c.poison}</color>：防御を無視して {c.poison} ダメージ（敵はあなたの攻撃のあと・行動の前、あなたはラウンドの終わり）。そのあと毒が 1 減る。", art?.statusPoison));
            if (c.fortify > 0) list.Add(("堅", new Color(0.66f, 0.85f, 1f), c.fortify.ToString(),
                $"<color=#A8D8FF>堅守 {c.fortify}</color>：ラウンドの終わりに防御が消えず、半分残る。ラウンドが終わるたびに 1 減る。", art?.statusFortify));
            if (c.bind > 0) list.Add(("縛", new Color(0.88f, 0.63f, 1f), null,
                "<color=#E0A0FF>縛り</color>：このラウンドは振れるダイスが 1 個だけ。", art?.statusBind));
            return list;
        }

        void SetHp(Fighter f, float hp)
        {
            float ratio = f.maxHp > 0 ? Mathf.Clamp01(hp / f.maxHp) : 0f;
            f.hpFill.rectTransform.anchorMax = new Vector2(ratio, 1);
            f.hpText.text = $"HP {Mathf.RoundToInt(hp)}/{f.maxHp}";
            // 毒：次に減る分（毒の値。HP より多ければ HP まで）
            bool poisoned = f.poisonFill != null && f.poison > 0 && hp > 0;
            if (f.poisonFill != null) f.poisonFill.gameObject.SetActive(poisoned);
            if (poisoned)
            {
                float from = f.maxHp > 0 ? Mathf.Clamp01((hp - f.poison) / f.maxHp) : 0f;
                var rect = f.poisonFill.rectTransform;
                rect.anchorMin = new Vector2(from, 0);
                rect.anchorMax = new Vector2(ratio, 1);
                rect.offsetMin = new Vector2(from <= 0f ? 3 : 0, 3);
                rect.offsetMax = new Vector2(-3, -3);
            }
        }

        static readonly Color DebuffColor = new Color(0.65f, 0.45f, 0.9f);
        static readonly Color SealColor = new Color(0.3f, 0.3f, 0.3f);

        // ---- 説明（マウスを乗せると出る） ----

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

        static string IntentExplanation(IntentType type)
        {
            switch (type)
            {
                case IntentType.Attack: return "ラウンドの終わりに攻撃してくる。防御に置いた出目で減らせる。";
                case IntentType.MultiAttack: return "何回かに分けて攻撃してくる。筋力は1回ごとに足される。防御は合計のダメージから引かれる。";
                case IntentType.Block: return "行動のときに防御を得る。次のラウンド、あなたの攻撃はまずこの防御で減らされる（敵の次の行動の前に消える）。";
                case IntentType.Buff: return "敵の筋力が上がる。次からの攻撃が強くなる。";
                case IntentType.Debuff: return $"あなたに脱力を与える。脱力の間は攻撃値が {BattleResolver.WeakPercent}% になる。";
                case IntentType.Seal: return "使用可能なダイスからランダムに1個を封印する。封印されるのはいつも1個だけで、前に封印されたダイスは使えるようになる。";
                case IntentType.DiceRoll: return "サイコロを振って攻撃してくる。値は振るまでわからない（範囲は表示どおり）。";
                case IntentType.ResetDice: return "ラウンドの終わりに、使用可能なダイスのうち強いほうから半分を使用済みにする（いちばん弱いダイスは残るので、リフレッシュは起きない）。強いダイスはこのラウンドに使ってしまうのも手。";
                case IntentType.MirrorAttack: return "前のラウンドにあなたが出した攻撃値を、そのまま攻撃として返してくる。大きく攻めた次のラウンドは守りを固めよう。";
                case IntentType.Poison: return "あなたに毒を与える。毒はあなたのラウンドの終わりに、防御を無視してダメージ。";
                case IntentType.Vulnerable: return $"あなたに弱体を与える。弱体の間は受けるダメージが {BattleResolver.VulnerablePercent}% になる。";
                case IntentType.Frail: return $"あなたに脆弱を与える。脆弱の間は作れる防御が {BattleResolver.FrailPercent}% になる。";
                case IntentType.Bind: return "次のラウンド、振れるダイスが1個になる。";
                case IntentType.Curse: return "呪いのダイス（欠け賽）をポーチに押し付けてくる。ショップやイベントで削除するまで残る。";
                case IntentType.Charge: return "力を溜めている。次のラウンドに大攻撃が来る。数字があれば、このラウンドにそれ以上のダメージを与えると怯んで大攻撃が止まる。";
                case IntentType.Stunned: return "怯んでいて、このラウンドは何もできない。";
                case IntentType.RewriteFate: return "あなたの一番強いダイスの、一番大きい面を、この戦闘のあいだだけ 1 にする。";
                case IntentType.Invert: return "このラウンドにあなたが振ったダイスの出目が「7−出目」に裏返る（6なら1、1なら6。7以上の面は0）。出目を上げたダイスほど裏目に出る。";
                default: return "";
            }
        }

        void SetIntent(EnemySlot slot, Intent intent, EnemyState e, int playerVulnerable)
        {
            var sprite = art != null ? art.IntentSprite(intent.type) : null;
            slot.intentIcon.sprite = sprite;
            slot.intentIcon.color = sprite != null ? Color.white : FallbackIntentColor(intent.type);
            slot.intentText.text = IntentShort(intent, e.strength, e.weak, playerVulnerable);
            // 攻撃＋防御などの防御は、盾と数（防御だけの予告は、絵そのものが盾）
            bool extraBlock = intent.type != IntentType.Block && intent.block > 0;
            slot.intentBlockBadge.SetActive(extraBlock);
            slot.intentBlockText.gameObject.SetActive(extraBlock);
            slot.intentBlockText.text = extraBlock ? intent.block.ToString() : "";
            // 防御も並べるときは枠を広げて、左に攻撃（絵と数）、右に防御（盾と数）
            slot.intentBack.sizeDelta = new Vector2(extraBlock ? 340f : 220f, 76f);
            slot.intentIcon.rectTransform.anchoredPosition = new Vector2(extraBlock ? -122f : -62f, 0f);
            slot.intentText.rectTransform.anchoredPosition = new Vector2(extraBlock ? -40f : 42f, 0f);
            slot.intentText.rectTransform.sizeDelta = new Vector2(extraBlock ? 110f : 140f, 80f);
            slot.tip = $"<b>{e.data.displayName}：{IntentLabel(intent, e.strength)}</b>\n{IntentExplanation(intent.type)}"
                + (intent.type != IntentType.Block && intent.block > 0 ? $"\n＋防御 {intent.block}：行動のあとに防御を得る。次のラウンド、あなたの攻撃はまずこの防御で減らされる。" : "");
            if (e.Enraged) slot.tip += "\n<color=#FF8A6A>HP が減って、攻撃が強くなっている！</color>";

            bool changed = !slot.shown.HasValue || slot.shown.Value.type != intent.type || slot.shown.Value.value != intent.value;
            slot.shown = intent;
            if (changed && isActiveAndEnabled) StartCoroutine(UIAnim.Punch(slot.intentIcon.transform.parent, 0.25f, 0.3f));
        }

        static Color FallbackIntentColor(IntentType type)
        {
            switch (type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll:
                case IntentType.MirrorAttack: return AttackColor;
                case IntentType.Poison: return new Color(0.45f, 0.8f, 0.35f);
                case IntentType.Vulnerable:
                case IntentType.Frail:
                case IntentType.Bind: return DebuffColor;
                case IntentType.Curse: return new Color(0.35f, 0.2f, 0.4f);
                case IntentType.Charge: return new Color(0.95f, 0.55f, 0.2f);
                case IntentType.Stunned: return new Color(0.6f, 0.6f, 0.6f);
                case IntentType.RewriteFate: return new Color(0.55f, 0.3f, 0.7f);
                case IntentType.Invert: return new Color(0.75f, 0.35f, 0.75f);
                case IntentType.Block: return BlockColor;
                case IntentType.Debuff: return DebuffColor;
                case IntentType.Seal:
                case IntentType.ResetDice: return SealColor;
                default: return AccentColor;
            }
        }

        /// <summary>予告アイコンの横に出す短い文字。</summary>
        static string IntentShort(Intent intent, int strength, int weak, int playerVulnerable = 0) =>
            IntentShortMain(intent, strength, weak, playerVulnerable); // 「攻撃＋防御」の防御は、予告の右下の盾と数で見せる（前は「防N」の文字）

        // 効果で上がった数は赤、下がった数は青（STS と同じ見せ方）
        const string UpColorTag = "<color=#FF6B5E>";
        const string DownColorTag = "<color=#6EC0FF>";

        static string Tinted(string text, int trend) =>
            trend > 0 ? UpColorTag + text + "</color>" : trend < 0 ? DownColorTag + text + "</color>" : text;

        /// <summary>ダイス1個の攻撃・防御の値（脱力・弱体・脆弱込み）。出目より上がっていれば赤、下がっていれば青。</summary>
        static string ShownValueText(BattleState battle, RolledDie r, Assignment assignment)
        {
            int shown = battle.ShownValue(r, assignment);
            return Tinted(shown.ToString(), BattleState.Trend(shown, r.value));
        }

        /// <summary>予告の攻撃値（敵の筋力・脱力と、あなたの弱体込み）。予告の元の値より上がっていれば赤、下がっていれば青。</summary>
        static string IntentAttackText(Intent intent, int value, int strength, int weak, int playerVulnerable)
        {
            var shownIntent = intent;
            shownIntent.value = value;
            int shown = BattleResolver.ApplyVulnerable(BattleResolver.EnemyAttackPerHit(shownIntent, strength, weak), playerVulnerable);
            return Tinted(shown.ToString(), BattleState.Trend(shown, Math.Max(0, value)));
        }

        static string IntentShortMain(Intent intent, int strength, int weak, int playerVulnerable)
        {
            switch (intent.type)
            {
                case IntentType.Attack: return IntentAttackText(intent, intent.value, strength, weak, playerVulnerable);
                case IntentType.MultiAttack:
                    // 筋力は1回ごとに乗る（(x+筋力)×y）
                    return $"{IntentAttackText(intent, intent.value, strength, weak, playerVulnerable)}×{intent.Hits}";
                case IntentType.DiceRoll when intent.minValue >= intent.maxValue:
                    return IntentAttackText(intent, intent.value, strength, weak, playerVulnerable);
                case IntentType.DiceRoll:
                    return $"<size=30>{IntentAttackText(intent, intent.minValue, strength, weak, playerVulnerable)}〜{IntentAttackText(intent, intent.maxValue, strength, weak, playerVulnerable)}</size>";
                case IntentType.Block: return intent.value.ToString();
                case IntentType.Buff: return $"+{intent.value}";
                case IntentType.Debuff: return $"<size=30>脱力{intent.value}</size>";
                case IntentType.Seal: return "<size=30>封印</size>";
                case IntentType.ResetDice: return "<size=26>振出し</size>";
                case IntentType.MirrorAttack: return IntentAttackText(intent, intent.value, strength, weak, playerVulnerable);
                case IntentType.Poison: return $"<size=30>毒{intent.value}</size>";
                case IntentType.Vulnerable: return $"<size=30>弱体{intent.value}</size>";
                case IntentType.Frail: return $"<size=30>脆弱{intent.value}</size>";
                case IntentType.Bind: return "<size=30>縛り</size>";
                case IntentType.Curse: return "<size=30>呪い</size>";
                case IntentType.Charge: return "<size=30>溜め</size>";
                case IntentType.Stunned: return "<size=30>怯み</size>";
                case IntentType.RewriteFate: return "<size=26>書き換え</size>";
                case IntentType.Invert: return "<size=28>裏返し</size>";
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

        public static string IntentLabel(Intent intent, int strength) =>
            IntentLabelMain(intent, strength) + (intent.type != IntentType.Block && intent.block > 0 ? $" ＋ 防御 {intent.block}" : "");

        static string IntentLabelMain(Intent intent, int strength)
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
                    return strength != 0 ? $"多段攻撃 {intent.value + strength}×{intent.Hits}（{intent.value}＋筋力{strength}）" : $"多段攻撃 {intent.value}×{intent.Hits}";
                case IntentType.Debuff:
                    return $"妨害（脱力{intent.value}）";
                case IntentType.Seal:
                    return "封印（ランダムに1個）";
                case IntentType.DiceRoll:
                    return intent.minValue < intent.maxValue ? $"賽振り（攻撃 {intent.minValue}〜{intent.maxValue}）" : $"賽振り（出目{intent.value / 2}：攻撃 {intent.value}）";
                case IntentType.MirrorAttack:
                    return $"写し：攻撃 {BattleResolver.EnemyAttack(intent, strength)}（前のラウンドのあなたの攻撃値）";
                case IntentType.Poison:
                    return $"毒を与える（毒{intent.value}）";
                case IntentType.Vulnerable:
                    return $"崩し（弱体{intent.value}）";
                case IntentType.Frail:
                    return $"砕き（脆弱{intent.value}）";
                case IntentType.Bind:
                    return "縛り（次のラウンド振れるダイスが1個）";
                case IntentType.Curse:
                    return "呪い（欠け賽を押し付ける）";
                case IntentType.Charge:
                    return intent.value > 0 ? $"溜め（{intent.value} 以上のダメージで怯む）" : "溜め";
                case IntentType.Stunned:
                    return "怯み（何もできない）";
                case IntentType.RewriteFate:
                    return "運命の書き換え";
                case IntentType.Invert:
                    return "裏返し（このラウンドの出目が 7−出目 になる）";
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
                // 敵が2体のときは右に敵が並ぶので、出目の列を左へ寄せる。
                // 攻撃・防御のボタン（2つで幅258）が隣と重ならない間隔にする（3個振ると重なっていた）
                float spacing = n >= 4 ? 275f : 290f;
                float offset = slots.Count > 1 ? (n >= 4 ? -230f : -170f) : 0f;
                float x = offset + (i - (n - 1) / 2f) * spacing;
                var face = DiceFaceView.Create($"RolledFace{i}", rolledRoot, art, 120, new Vector2(x, 40));
                face.SetValue(r.value);
                face.SetEngraving(r.dice.faces[r.faceIndex].engraving);
                rolledFaces.Add(face);
                UIFactory.Text($"RolledName{i}", rolledRoot, r.dice.DisplayName + (r.inverted ? "（裏返し）" : ""), 24, PaperColor, new Vector2(240, 30), new Vector2(x, 122)).outlineWidth = 0.25f;

                // 置いたときの実際の値（盾賽なら防御+2 など）をボタンに出す。転がっている最中は伏せる
                bool hidden = i >= n - hiddenRolled;
                string atkValue = hidden ? "？" : ShownValueText(battle, r, Assignment.Attack);
                string blkValue = hidden ? "？" : ShownValueText(battle, r, Assignment.Block);
                // 再転：振り直せる（1回）／運命の糸：好きな値にできる（1戦闘に1回）
                float extraY = -118;
                if (!hidden && r.canReroll && !r.rerolled)
                {
                    var reroll = UIFactory.Button($"Reroll{i}", rolledRoot, new Vector2(180, 44), new Vector2(x, extraY), AccentColor, "再転：振り直す", 22, out _);
                    reroll.onClick.AddListener(() => RerollClicked?.Invoke(r));
                    extraY -= 50;
                }
                if (!hidden && battle.CanUseFate)
                {
                    var fate = UIFactory.Button($"Fate{i}", rolledRoot, new Vector2(180, 44), new Vector2(x, extraY), new Color(0.75f, 0.6f, 0.95f), "運命の糸：値を選ぶ", 20, out _);
                    fate.onClick.AddListener(() => FateClicked?.Invoke(r));
                }
                // 両刃・六の加護：攻撃と防御の両方に効くので、割り振りはいらない
                if (r.bothSides)
                {
                    var both = UIFactory.Panel($"Both{i}", rolledRoot, new Vector2(258, 56), new Vector2(x, -60), new Color(0.55f, 0.35f, 0.6f));
                    UIFactory.Text("Label", both.transform, $"攻防両方：攻 {atkValue}・防 {blkValue}", 22, PaperColor, new Vector2(250, 52), Vector2.zero);
                    continue;
                }
                var atk = UIFactory.Button($"Attack{i}", rolledRoot, new Vector2(126, 56), new Vector2(x - 66, -60),
                    r.assignment == Assignment.Attack ? ChosenAttackColor : OffColor, $"攻撃 {atkValue}", 26, out var atkLabel);
                var blk = UIFactory.Button($"Block{i}", rolledRoot, new Vector2(126, 56), new Vector2(x + 66, -60),
                    r.assignment == Assignment.Block ? ChosenBlockColor : OffColor, $"防御 {blkValue}", 26, out var blkLabel);
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
            float w = Mathf.Clamp((TrayWidth - gap * (ordered.Count - 1)) / Mathf.Max(1, ordered.Count), 220f, 280f);
            UIFactory.SetScrollWidth(trayRoot, ordered.Count * (w + gap) - gap);
            float left = -(ordered.Count * (w + gap) - gap) / 2f + w / 2f;
            for (int i = 0; i < ordered.Count; i++)
            {
                var die = ordered[i];
                bool available = die.state == DiceState.Available;
                bool isSelected = selected.Contains(die);
                string state = isSelected ? "選択中" : available ? "クリックで選ぶ" : die.state == DiceState.Sealed ? "<color=#7A1F1F>封印中</color>" : "使用済み";
                var card = DiceCard.Create($"Dice{i}", trayRoot, die, art, new Vector2(w, h), new Vector2(left + i * (w + gap), 0), state, !available, isSelected, DiceRoller.MirrorValue(battle.LastRolled));
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
        RectTransform fatePicker;

        /// <summary>運命の糸：1〜max の値を選ぶ小窓。選んだら onChosen(値)、やめたら onChosen(0)。</summary>
        public void ShowFatePicker(RolledDie r, int max, Action<int> onChosen)
        {
            if (fatePicker != null) Destroy(fatePicker.gameObject);
            fatePicker = UIFactory.Panel("FatePicker", transform, new Vector2(Mathf.Max(600, max * 90 + 60), 220), new Vector2(0, 60), new Color(0.12f, 0.08f, 0.14f, 0.97f)).rectTransform;
            UIFactory.Text("Title", fatePicker, $"運命の糸：{r.dice.DisplayName} の出目 {r.value} を変える（1戦闘に1回）", 26, PaperColor, new Vector2(fatePicker.sizeDelta.x - 40, 40), new Vector2(0, 70));
            float left = -(max * 90 - 10) / 2f + 40;
            for (int v = 1; v <= max; v++)
            {
                int value = v;
                var b = UIFactory.Button($"Value{v}", fatePicker, new Vector2(80, 80), new Vector2(left + (v - 1) * 90, 0), new Color(0.85f, 0.75f, 1f), v.ToString(), 40, out _);
                b.onClick.AddListener(() =>
                {
                    Destroy(fatePicker.gameObject);
                    fatePicker = null;
                    onChosen(value);
                });
            }
            var cancel = UIFactory.Button("Cancel", fatePicker, new Vector2(200, 50), new Vector2(0, -80), ButtonColor, "やめる", 24, out _);
            cancel.onClick.AddListener(() =>
            {
                Destroy(fatePicker.gameObject);
                fatePicker = null;
                onChosen(0);
            });
        }

        /// <summary>いくつかの中から1つ選ぶ小窓。選んだら onChosen(番号)、やめたら onChosen(-1)。</summary>
        public void ShowChoice(string title, IReadOnlyList<string> options, Action<int> onChosen)
        {
            if (fatePicker != null) Destroy(fatePicker.gameObject);
            const float w = 260f;
            fatePicker = UIFactory.Panel("ChoicePicker", transform, new Vector2(Mathf.Max(900, options.Count * (w + 20) + 60), 270), new Vector2(0, 60), new Color(0.12f, 0.08f, 0.14f, 1f)).rectTransform;
            UIFactory.Text("Title", fatePicker, title, 26, PaperColor, new Vector2(fatePicker.sizeDelta.x - 60, 80), new Vector2(0, 80));
            float left = -(options.Count - 1) * (w + 20) / 2f;
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var b = UIFactory.Button($"Option{i}", fatePicker, new Vector2(w, 70), new Vector2(left + i * (w + 20), 0), ButtonColor, options[i], 26, out _);
                b.onClick.AddListener(() =>
                {
                    Destroy(fatePicker.gameObject);
                    fatePicker = null;
                    onChosen(index);
                });
            }
            var cancel = UIFactory.Button("Cancel", fatePicker, new Vector2(200, 50), new Vector2(0, -80), ButtonColor, "やめる", 24, out _);
            cancel.onClick.AddListener(() =>
            {
                Destroy(fatePicker.gameObject);
                fatePicker = null;
                onChosen(-1);
            });
        }

        /// <summary>再転で振り直したダイスを転がして見せる。</summary>
        public IEnumerator PlayRerollAt(BattleState battle, int index)
        {
            if (index < 0 || index >= battle.Rolled.Count || index >= rolledFaces.Count) yield break;
            var r = battle.Rolled[index];
            yield return rolledFaces[index].PlayRoll(r.dice, r.value, 0.6f, r.dice.faces[r.faceIndex].engraving);
        }

        public IEnumerator PlayRoll(BattleState battle, int count)
        {
            int start = battle.Rolled.Count - count;
            for (int i = start; i < battle.Rolled.Count; i++)
            {
                // 錆び賽の自傷で倒れたときなど、戦闘が終わっていると出目の絵は作られない
                // （そのまま並びを読むと例外で止まり、画面が固まっていた）
                if (i < 0 || i >= rolledFaces.Count) continue;
                var r = battle.Rolled[i];
                StartCoroutine(rolledFaces[i].PlayRoll(r.dice, r.value, 0.6f, r.dice.faces[r.faceIndex].engraving));
            }
            yield return UIAnim.Wait(0.9f);
        }

        /// <summary>ラウンドの始まり：防御の予告なら、盾が張られるのを見せる。</summary>
        public IEnumerator PlayRoundStart(BattleState battle)
        {
            if (battle.Outcome != BattleOutcome.Ongoing) yield break;
            bool any = false;
            // 第2形態に入った（八面）
            for (int i = 0; i < battle.enemies.Count && i < slots.Count; i++)
            {
                var e = battle.enemies[i];
                if (e.IsDead || !e.EnteredPhase2ThisRound) continue;
                var f = slots[i].f;
                StartCoroutine(UIAnim.Shake(stage, 16f, 0.5f));
                StartCoroutine(UIAnim.Flash(f.image, new Color(0.7f, 0.4f, 1f), 0.8f));
                Popup($"{e.data.displayName} の姿が変わった！", new Vector2(0, 300), new Color(0.85f, 0.65f, 1f), 60);
                yield return UIAnim.Punch(f.figure, 0.2f, 0.6f);
                any = true;
            }
            for (int i = 0; i < battle.enemies.Count && i < slots.Count; i++)
            {
                var e = battle.enemies[i];
                if (e.IsDead || e.CurrentIntent.type != IntentType.Block) continue;
                var home = slots[i].f.home;
                SpawnEffect(art != null ? art.fxBlock : null, home, 300f, 0.6f, BlockColor);
                Popup($"防御 +{e.CurrentIntent.value}", home + new Vector2(0, 60), new Color(0.6f, 0.8f, 1f));
                any = true;
            }
            if (any) yield return UIAnim.Wait(0.4f);
        }

        /// <summary>
        /// 1ラウンドの解決を見せる：自分の攻撃 → （倒していなければ）敵の行動（並び順に）→ 毒。
        /// before は Resolve の直前の値、result と battle は Resolve のあとの値。
        /// </summary>
        public IEnumerator PlayResolve(RoundSnapshot before, RoundResult result, BattleState battle)
        {
            SetBusy(true);
            var infos = result.enemies;

            // 自分の攻撃（当たった敵ごとに同時に見せる）
            if (before.attack > 0)
            {
                yield return Lunge(player, +1);
                bool anyHit = false;
                foreach (var info in infos)
                {
                    if (info.hpBefore <= 0 || info.dealt <= 0) continue;
                    var f = slots[info.index].f;
                    anyHit = true;
                    SpawnEffect(art != null ? art.fxSlash : null, f.home, 340f, 0.4f, DamageColor);
                    StartCoroutine(UIAnim.Shake(f.figure, 22f, 0.35f));
                    StartCoroutine(UIAnim.Flash(f.image, new Color(1f, 0.5f, 0.45f), 0.35f));
                    Popup($"-{info.dealt}", f.home + new Vector2(0, 80), DamageColor, 64);
                    StartCoroutine(AnimateHp(f, info.hpBefore, info.hpBefore - info.dealt));
                }
                if (anyHit)
                {
                    Sfx.Play(SoundId.Hit);
                    yield return UIAnim.Wait(0.4f);
                }
                else
                {
                    var target = TargetSlot(battle, infos);
                    SpawnEffect(art != null ? art.fxBlock : null, target.f.home, 300f, 0.5f, BlockColor);
                    Sfx.Play(SoundId.Block);
                    Popup("防がれた", target.f.home + new Vector2(0, 80), new Color(0.75f, 0.85f, 1f));
                    yield return UIAnim.Wait(0.35f);
                }
                yield return MoveBack(player);
            }

            // 萎え賽・砕き賽などで敵に与えた脱力・弱体・脆弱（攻撃値が0でも出す）
            bool anyDebuff = false;
            foreach (var info in infos)
            {
                var parts = new List<string>();
                if (info.weakGiven > 0) parts.Add($"脱力 +{info.weakGiven}");
                if (info.vulnerableGiven > 0) parts.Add($"弱体 +{info.vulnerableGiven}");
                if (info.frailGiven > 0) parts.Add($"脆弱 +{info.frailGiven}");
                if (parts.Count == 0 || info.enemy.IsDead) continue;
                var f = slots[info.index].f;
                StartCoroutine(UIAnim.Flash(f.image, DebuffColor, 0.5f));
                Popup(string.Join("　", parts), f.home + new Vector2(0, 150), DebuffColor);
                anyDebuff = true;
            }
            if (anyDebuff)
            {
                Sfx.Play(SoundId.Debuff);
                yield return UIAnim.Wait(0.35f);
            }

            // 棘（山颪など）：大きい出目で攻撃した分だけ、自分にダメージ
            if (result.thornsDamage > 0)
            {
                Sfx.Play(SoundId.Damage);
                StartCoroutine(UIAnim.Shake(player.figure, 16f, 0.3f));
                StartCoroutine(UIAnim.Flash(player.image, new Color(1f, 0.5f, 0.45f), 0.3f));
                Popup($"棘 -{result.thornsDamage}", player.home + new Vector2(0, 80), DamageColor, 52);
                yield return AnimateHp(player, before.playerHp, before.playerHp - result.thornsDamage);
            }

            // 溜めを止めた（大顎）
            foreach (var info in infos.Where(x => x.staggered))
            {
                var f = slots[info.index].f;
                Popup("怯んだ！ 大攻撃が止まる", f.home + new Vector2(0, 150), AccentColor, 48);
                yield return UIAnim.Shake(f.figure, 18f, 0.35f);
            }

            // 攻撃で倒した敵
            bool allDown = battle.Outcome == BattleOutcome.Victory && infos.All(x => x.hpBefore <= 0 || x.killedByAttack);
            var killed = infos.Where(x => x.killedByAttack).ToList();
            for (int k = 0; k < killed.Count; k++)
            {
                yield return Defeat(slots[killed[k].index], allDown && k == killed.Count - 1);
            }

            // 敵の毒（自分の攻撃のあと、敵が行動する前）。毒で倒れた敵は行動しない
            var poisonColor = new Color(0.55f, 0.9f, 0.45f);
            foreach (var info in infos.Where(x => x.poisonDamage > 0))
            {
                var f = slots[info.index].f;
                int hp = info.hpBefore - info.dealt;
                StartCoroutine(UIAnim.Flash(f.image, poisonColor, 0.4f));
                Sfx.Play(SoundId.Poison);
                Popup($"毒 -{info.poisonDamage}", f.home + new Vector2(0, 80), poisonColor, 52);
                yield return AnimateHp(f, hp, hp - info.poisonDamage);
            }
            var poisoned = infos.Where(x => x.diedOfPoison).ToList();
            for (int k = 0; k < poisoned.Count; k++)
            {
                yield return Defeat(slots[poisoned[k].index], battle.Outcome == BattleOutcome.Victory && k == poisoned.Count - 1);
            }
            if (poisoned.Count > 0 && battle.Outcome == BattleOutcome.Victory) allDown = true;
            // 仲間が倒れて強くなった（双子鬼）
            foreach (var info in infos.Where(x => x.strengthGained > 0 && !x.enemy.IsDead))
            {
                var f = slots[info.index].f;
                StartCoroutine(UIAnim.Flash(f.image, new Color(1f, 0.4f, 0.3f), 0.5f));
                Popup($"仲間を倒されて怒った！ 筋力 +{info.strengthGained}", f.home + new Vector2(0, 150), AccentColor, 40);
                Sfx.Play(SoundId.Buff);
                yield return UIAnim.Punch(f.figure, 0.18f, 0.4f);
            }
            if (allDown)
            {
                SetBusy(false);
                yield break;
            }

            yield return UIAnim.Wait(0.2f);

            // 敵の行動（並び順に）
            int playerHp = before.playerHp - result.thornsDamage;
            foreach (var info in infos.Where(x => x.acted))
            {
                yield return PlayEnemyAction(slots[info.index].f, info, playerHp, before.playerBlock > 0);
                playerHp -= info.taken;
            }

            // ラウンド終了：自分の毒
            if (result.playerPoisonDamage > 0)
            {
                StartCoroutine(UIAnim.Flash(player.image, poisonColor, 0.4f));
                Sfx.Play(SoundId.Poison);
                Popup($"毒 -{result.playerPoisonDamage}", player.home + new Vector2(0, 80), poisonColor, 52);
                yield return AnimateHp(player, playerHp, playerHp - result.playerPoisonDamage);
            }

            if (battle.Outcome == BattleOutcome.Defeat)
            {
                yield return Defeat(player, -1);
            }
            yield return UIAnim.Wait(0.15f);
            SetBusy(false);
        }

        EnemySlot TargetSlot(BattleState battle, IReadOnlyList<EnemyRoundInfo> infos)
        {
            int i = infos.FirstOrDefault(x => x.enemy == battle.Target)?.index ?? 0;
            return slots[Mathf.Clamp(i, 0, slots.Count - 1)];
        }

        /// <summary>敵1体の行動を見せる。playerHp はこの行動の前の自分の HP。</summary>
        IEnumerator PlayEnemyAction(Fighter enemy, EnemyRoundInfo info, int playerHp, bool hadBlock)
        {
            var intent = info.intent;
            switch (intent.type)
            {
                case IntentType.Attack:
                case IntentType.MultiAttack:
                case IntentType.DiceRoll:
                case IntentType.MirrorAttack:
                    if (intent.type == IntentType.DiceRoll)
                    {
                        // 賽振り：隠れていた値をここで見せる
                        Popup($"出目 {intent.value / 2} → 攻撃 {intent.value}", enemy.home + new Vector2(0, 120), AccentColor, 40);
                        yield return UIAnim.Punch(enemy.figure, 0.12f, 0.35f);
                    }
                    Sfx.Play(SoundId.EnemyAttack); // 振りかぶり
                    yield return Lunge(enemy, -1);
                    if (hadBlock)
                    {
                        SpawnEffect(art != null ? art.fxBlock : null, player.home, 280f, 0.5f, BlockColor);
                        Sfx.Play(SoundId.Block);
                    }
                    if (info.taken > 0)
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
                        Popup($"-{info.taken}", player.home + new Vector2(0, 80), DamageColor, 64);
                        yield return AnimateHp(player, playerHp, playerHp - info.taken);
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
                    Popup($"脱力 {intent.value}", player.home + new Vector2(0, 80), DebuffColor);
                    Sfx.Play(SoundId.Debuff);
                    yield return UIAnim.Wait(0.4f);
                    yield return MoveBack(enemy);
                    break;
                case IntentType.Seal:
                    yield return Lunge(enemy, -1);
                    string sealedName = info.sealedDie != null ? info.sealedDie.DisplayName : "なし";

                    Popup($"封印：{sealedName}", new Vector2(0, -250), new Color(1f, 0.6f, 0.5f), 48);
                    StartCoroutine(UIAnim.Shake(trayRoot, 12f, 0.3f));
                    yield return UIAnim.Wait(0.5f);
                    yield return MoveBack(enemy);
                    break;
                case IntentType.Poison:
                case IntentType.Vulnerable:
                case IntentType.Frail:
                case IntentType.Bind:
                {
                    yield return Lunge(enemy, -1);
                    var color = intent.type == IntentType.Poison ? new Color(0.55f, 0.9f, 0.45f) : DebuffColor;
                    string text = intent.type == IntentType.Poison ? $"毒 {intent.value}" : intent.type == IntentType.Vulnerable ? $"弱体 {intent.value}" : intent.type == IntentType.Frail ? $"脆弱 {intent.value}" : "縛り";
                    StartCoroutine(UIAnim.Flash(player.image, color, 0.5f));
                    Popup(text, player.home + new Vector2(0, 80), color);
                    // 毒は毒の音、脱力・弱体・脆弱・縛りはまとめてデバフの音
                    Sfx.Play(intent.type == IntentType.Poison ? SoundId.Poison : SoundId.Debuff);
                    yield return UIAnim.Wait(0.4f);
                    yield return MoveBack(enemy);
                    break;
                }
                case IntentType.Curse:
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(0.6f, 0.35f, 0.7f), 0.5f));
                    Popup(info.curseDie != null ? $"呪い：{info.curseDie.DisplayName} を押し付けられた" : "呪い：ポーチが満杯で入らなかった",
                        new Vector2(0, -250), new Color(0.85f, 0.6f, 1f), 44);
                    Sfx.Play(SoundId.Trap);
                    StartCoroutine(UIAnim.Shake(trayRoot, 12f, 0.3f));
                    yield return UIAnim.Wait(0.6f);
                    break;
                case IntentType.Charge:
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(1f, 0.6f, 0.2f), 0.5f));
                    Popup("力を溜めている……", enemy.home + new Vector2(0, 120), new Color(1f, 0.7f, 0.3f), 44);
                    Sfx.Play(SoundId.Charge);
                    yield return UIAnim.Punch(enemy.figure, 0.1f, 0.5f);
                    break;
                case IntentType.Stunned:
                    Popup("怯んで動けない！", enemy.home + new Vector2(0, 120), new Color(0.85f, 0.85f, 0.85f), 44);
                    yield return UIAnim.Shake(enemy.figure, 10f, 0.4f);
                    break;
                case IntentType.RewriteFate:
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(0.7f, 0.4f, 1f), 0.6f));
                    yield return UIAnim.Punch(enemy.figure, 0.15f, 0.4f);
                    Popup(info.rewrittenDie != null ? $"運命の書き換え：{info.rewrittenDie.DisplayName} の {info.rewrittenFrom} の面が 1 に！" : "運命の書き換え：書き換える面がない",
                        new Vector2(0, -250), new Color(0.85f, 0.65f, 1f), 42);
                    Sfx.Play(SoundId.Trap);
                    StartCoroutine(UIAnim.Shake(trayRoot, 14f, 0.4f));
                    yield return UIAnim.Wait(0.7f);
                    break;
                case IntentType.ResetDice:
                    Popup("振り出しに戻れ！", new Vector2(0, 60), AccentColor, 64);
                    if (info.resetDice != null && info.resetDice.Count > 0)
                        Popup($"使用済みに：{string.Join("・", info.resetDice.Select(d => d.DisplayName))}", new Vector2(0, -250), new Color(1f, 0.6f, 0.5f), 40);
                    StartCoroutine(UIAnim.Shake(stage, 14f, 0.4f));
                    StartCoroutine(UIAnim.Shake(trayRoot, 16f, 0.4f));
                    yield return UIAnim.Punch(enemy.figure, 0.15f, 0.5f);
                    yield return UIAnim.Wait(0.3f);
                    break;
                case IntentType.Buff:
                    StartCoroutine(UIAnim.Flash(enemy.image, new Color(1f, 0.85f, 0.4f), 0.4f));
                    Popup($"筋力 +{intent.value}", enemy.home + new Vector2(0, 80), AccentColor);
                    Sfx.Play(SoundId.Buff);
                    yield return UIAnim.Punch(enemy.figure, 0.18f, 0.4f);
                    break;
            }

            // 行動のあとに得た防御（防御の予告・攻撃＋防御）。次のラウンドのあなたの攻撃を防ぐ
            if (info.blockGained > 0)
            {
                SpawnEffect(art != null ? art.fxBlock : null, enemy.home, 300f, 0.45f, BlockColor);
                Sfx.Play(SoundId.Block);
                Popup($"防御 +{info.blockGained}", enemy.home + new Vector2(0, 80), new Color(0.6f, 0.8f, 1f));
                yield return UIAnim.Wait(0.35f);
            }
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
        /// <summary>敵が倒れる。最後の1体（勝利）なら勝利の音、そうでなければ攻撃が当たった音。</summary>
        IEnumerator Defeat(EnemySlot slot, bool victory)
        {
            if (slot.dead) yield break;
            slot.dead = true;
            slot.intentIcon.transform.parent.gameObject.SetActive(false);
            if (slot.targetMark != null) slot.targetMark.SetActive(false);
            UIFactory.ClearChildren(slot.f.statusRoot);
            slot.f.statusKey = null;
            slot.f.shield.gameObject.SetActive(false);
            slot.f.blockBadge.SetActive(false);
            if (slot.intentBlockBadge != null) slot.intentBlockBadge.SetActive(false);
            if (slot.intentBlockText != null) slot.intentBlockText.gameObject.SetActive(false);
            yield return Fall(slot.f, 1, victory ? SoundId.Victory : SoundId.Hit);
        }

        IEnumerator Defeat(Fighter f, int direction) => Fall(f, direction, SoundId.Defeat);

        IEnumerator Fall(Fighter f, int direction, SoundId sound)
        {
            Sfx.Play(sound);
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
