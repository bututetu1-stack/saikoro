using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Run;
using TMPro;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>始めの画面：「続きから」「新しく始める」（セーブがあるときだけ出す）。</summary>
    public class TitleView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color ShadeColor = new Color(0.08f, 0.05f, 0.04f, 0.78f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);

        public event Action ContinueClicked;
        public event Action NewRunClicked;
        public event Action RaceClicked;   // みんなで遊ぶ（対戦・協力）
        public event Action DailyClicked;  // 今日の旅（日ごとに決まったシードとお題）
        public event Action HowToClicked;
        public event Action QuitClicked;
        public event Action SettingsClicked;
        public event Action RecordsClicked;   // 記録（ラン履歴と図鑑）

        RectTransform confirm;

        /// <param name="save">続きのセーブ（中身を少し見せる）。読めなかったときは null で、error に理由。</param>
        public static TitleView Create(Transform canvas, UIArt art, RunSave save, string error)
        {
            var root = UIFactory.Stretch("TitleView", canvas);
            var view = root.gameObject.AddComponent<TitleView>();
            var bg = art != null ? (art.titleBackground != null ? art.titleBackground : art.mapBackground) : null;
            UIFactory.Background(root, bg, new Color(0.85f, 0.8f, 0.65f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.35f));

            if (art != null && art.titleLogo != null)
            {
                // 毛筆の題字の絵（ChatGPT で作った）。まわりをうっすら明るくして、夕焼けの空から浮かせる
                var logo = UIFactory.Picture("TitleLogo", root, art.titleLogo, new Vector2(900, 320), new Vector2(0, 320));
                var halo = logo.gameObject.AddComponent<UnityEngine.UI.Outline>();
                halo.effectColor = new Color(1f, 0.92f, 0.75f, 0.35f);
                halo.effectDistance = new Vector2(3f, -3f);
                UIFactory.Panel("SubBack", root, new Vector2(560, 46), new Vector2(0, 140), new Color(0.08f, 0.05f, 0.04f, 0.6f)).raycastTarget = false; // 明るい空の上でも読めるように
                var sub = UIFactory.Text("Sub", root, "サイコロを育てて、出目で道を選ぶ", 30, PaperColor, new Vector2(900, 44), new Vector2(0, 140));
                sub.outlineWidth = 0.14f;
                sub.outlineColor = new Color32(30, 15, 5, 255);
                view.StartCoroutine(UIAnim.Punch(logo.transform, 0.06f, 0.5f));
            }
            else
            {
                var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(900, 200), new Vector2(0, 260), ShadeColor);
                var title = UIFactory.Text("Title", titlePanel.transform, "賽ノ道", 110, GoldColor, new Vector2(860, 140), new Vector2(0, 15));
                title.fontStyle = FontStyles.Bold;
                UIFactory.Text("Sub", titlePanel.transform, "サイコロを育てて、出目で道を選ぶ", 28, PaperColor, new Vector2(860, 40), new Vector2(0, -70));
            }
            // 自己ベスト（踏破にかかったターン数）・解放した段位・実績の数
            var progress = new List<string>();
            if (BestRecord.BestTurns > 0) progress.Add($"自己ベスト：{BestRecord.BestTurns} ターンで踏破");
            if (AscensionRecord.Unlocked > 0) progress.Add($"段位 {AscensionRecord.Unlocked} まで解放");
            if (Achievements.UnlockedCount > 0) progress.Add($"実績 {Achievements.UnlockedCount}/{Achievements.All.Length}");
            int bestScore = RunHistory.Entries.Count > 0 ? RunHistory.Entries.Max(e => e.score) : 0;
            if (bestScore > 0) progress.Add($"最高得点 {bestScore:N0}");
            if (progress.Count > 0)
            {
                var best = UIFactory.Text("Best", root, string.Join("　", progress), 26, GoldColor, new Vector2(1100, 36), new Vector2(0, art != null && art.titleLogo != null ? 104 : 130));
                best.enableAutoSizing = true; // 並ぶものが増えても1行に収める
                best.fontSizeMax = 26;
                best.fontSizeMin = 18;
                best.outlineWidth = 0.11f;
                best.outlineColor = new Color32(30, 15, 5, 255);
            }

            string info = save != null
                ? $"第{save.layerIndex + 1}層　HP {save.hp}/{save.maxHp}　{save.gold} G　ターン {save.turn}" + (string.IsNullOrEmpty(save.savedAt) ? "" : $"\n<size=22>{save.savedAt} に保存</size>")
                : error != null ? $"続きのデータを読めませんでした。\n<size=22>{error}</size>" : "続きのデータはありません。";
            var infoPanel = UIFactory.Panel("InfoPanel", root, new Vector2(760, 110), new Vector2(0, 40), ShadeColor);
            UIFactory.Text("Info", infoPanel.transform, info, 28, PaperColor, new Vector2(720, 100), Vector2.zero);

            var cont = UIFactory.Button("ContinueButton", root, new Vector2(520, 96), new Vector2(0, -110), GoldColor, "続きから", 38, out _);
            cont.interactable = save != null;
            cont.onClick.AddListener(() => view.ContinueClicked?.Invoke());
            var fresh = UIFactory.Button("NewRunButton", root, new Vector2(280, 84), new Vector2(-178, -230), new Color(0.93f, 0.87f, 0.72f), "新しく始める", 32, out _);
            // みんなで遊ぶ：対戦「賽レース」と協力「賽の旅団」（入口の画面で切り替える）
            var race = UIFactory.Button("RaceButton", root, new Vector2(170, 84), new Vector2(237, -230), new Color(1f, 0.6f, 0.45f), "みんなで", 32, out _);
            race.onClick.AddListener(() => view.RaceClicked?.Invoke());
            // 今日の旅（STS のデイリー風）：押すと、今日のお題と記録を見せてから始める
            var daily = UIFactory.Button("DailyButton", root, new Vector2(170, 84), new Vector2(57, -230), new Color(0.75f, 0.85f, 0.55f), "今日の旅", 32, out _);
            daily.onClick.AddListener(() => view.ShowDaily(save != null));
            // 続きがあるときは、消してよいか一度だけ聞く
            fresh.onClick.AddListener(() =>
            {
                if (save == null) view.NewRunClicked?.Invoke();
                else view.ShowConfirm();
            });
            // ブラウザ（WebGL）ではゲームを終えられないので、「ゲームを終える」は出さない
#if UNITY_WEBGL && !UNITY_EDITOR
            const bool canQuit = false;
#else
            const bool canQuit = true;
#endif
            // 遊び方・記録・設定・（ゲームを終える）を横に並べる
            var row = new List<(string name, string label, Action action, Color color)>
            {
                ("HowToButton", "遊び方", () => view.HowToClicked?.Invoke(), new Color(0.93f, 0.87f, 0.72f)),
                ("RecordsButton", "記録", () => view.RecordsClicked?.Invoke(), new Color(0.93f, 0.87f, 0.72f)),
                ("SettingsButton", "設定", () => view.SettingsClicked?.Invoke(), new Color(0.93f, 0.87f, 0.72f)),
            };
            if (canQuit) row.Add(("QuitButton", "ゲームを終える", () => view.QuitClicked?.Invoke(), new Color(0.75f, 0.68f, 0.58f)));
            float bw = row.Count >= 4 ? 230f : 300f, step = bw + 20f;
            for (int i = 0; i < row.Count; i++)
            {
                var (name, label, action, color) = row[i];
                var button = UIFactory.Button(name, root, new Vector2(bw, 66), new Vector2(-(row.Count - 1) * step / 2f + i * step, -330), color, label, 30, out _);
                button.onClick.AddListener(() => action());
            }
            // 更新したら一度だけ「新しくなったところ」を見せる（Slay the Spire・Balatro の更新のお知らせ風）
            // 初めて遊ぶ人には出さない（前の版で遊んだことがある人だけ）
            if (PlayerPrefs.GetString(NewsSeenKey, "") != NewsId)
            {
                if (RunHistory.Entries.Count > 0 || BestRecord.BestTurns > 0) view.ShowNews();
                else PlayerPrefs.SetString(NewsSeenKey, NewsId);
            }
            return view;
        }

        /// <summary>今日の旅：お題と今日の記録を見せて、始めるかを聞く。</summary>
        void ShowDaily(bool hasSave)
        {
            if (confirm != null) return;
            var today = DateTime.Now;
            string key = DailyChallenge.KeyOf(today);
            var mod = DailyChallenge.ModifierOf(today);
            confirm = UIFactory.Stretch("Daily", transform);
            UIFactory.Panel("Shade", confirm, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", confirm, new Vector2(900, 470), Vector2.zero, new Color(0.12f, 0.08f, 0.06f, 1f));
            var title = UIFactory.Text("Title", box.transform, $"今日の旅　<size=70%>{today.Month}月{today.Day}日</size>", 44, GoldColor, new Vector2(840, 70), new Vector2(0, 175));
            title.fontStyle = FontStyles.Bold;
            string text = $"今日のお題：<color=#B8E07A>{DailyChallenge.NameOf(mod)}</color>\n<size=85%>{DailyChallenge.DescriptionOf(mod)}</size>\n\n"
                + $"<size=85%>同じ日なら、だれでも同じ盤面・同じはじめのダイスで遊べます。</size>\n"
                + $"今日の最高：{DailyRecord.Describe(DailyRecord.Best(key))}　<size=80%>（挑戦 {DailyRecord.Tries(key)} 回）</size>";
            if (hasSave) text += "\n<size=80%><color=#FF9A7A>始めると、いまの続きは消えます。</color></size>";
            UIFactory.Text("Text", box.transform, text, 30, PaperColor, new Vector2(840, 260), new Vector2(0, 20));
            var yes = UIFactory.Button("Yes", box.transform, new Vector2(300, 80), new Vector2(-170, -175), new Color(0.75f, 0.85f, 0.55f), "始める", 30, out _);
            yes.onClick.AddListener(() => DailyClicked?.Invoke());
            var no = UIFactory.Button("No", box.transform, new Vector2(300, 80), new Vector2(170, -175), new Color(0.93f, 0.87f, 0.72f), "やめる", 28, out _);
            no.onClick.AddListener(() =>
            {
                Destroy(confirm.gameObject);
                confirm = null;
            });
        }

        // ---- 新しくなったところ（更新したら一度だけ） ----

        const string NewsSeenKey = "SaiNoMichi.NewsSeen";
        // TODO(仕様): 更新するたびに NewsId と文を変える
        const string NewsId = "2026-10-16c";
        const string NewsText =
            "・<color=#FFD24D>ダイス 100 種・レリック 100 種・お守り 100 種</color>に（イベントでしか手に入らないダイスも）\n" +
            "・<color=#FFD24D>古の神</color>：第2層・第3層の始めに、強い宝を3つから1つ授かる（STS2 のエンシェント風）\n" +
            "・旅人に<color=#FFD24D>役割</color>：賽振り＝回復・博徒＝攻撃・細工師＝デバフ・山伏＝防御\n" +
            "・新しいイベントが <color=#FFD24D>77 種</color>（全 102 種・挿絵つき。次の層に持ち越すものも）。図鑑にイベントのページ\n" +
            "・戦闘：「<color=#FFD24D>使う</color>」でダイスの効果がすぐ発動、「ターン終了」で敵が動く\n" +
            "・協力：同じ票なら抽選。仲間の攻撃が見え、弱体が自分の攻撃に乗る\n" +
            "・ラウンドの帯・HP の減り跡・勝利の判子、<color=#FFD24D>賽の国めぐり</color>・賽レースも遊べる";

        void ShowNews()
        {
            if (confirm != null) return;
            confirm = UIFactory.Stretch("News", transform);
            UIFactory.Panel("Shade", confirm, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", confirm, new Vector2(1300, 640), Vector2.zero, new Color(0.12f, 0.08f, 0.06f, 1f));
            var title = UIFactory.Text("Title", box.transform, "新しくなったところ", 48, GoldColor, new Vector2(1200, 70), new Vector2(0, 260));
            title.fontStyle = FontStyles.Bold;
            var body = UIFactory.Text("Text", box.transform, NewsText, 28, PaperColor, new Vector2(1180, 420), new Vector2(0, 10), TextAlignmentOptions.Left);
            body.lineSpacing = 12f;
            body.enableAutoSizing = true; // 長くなっても枠からはみ出さないように
            body.fontSizeMax = 28;
            body.fontSizeMin = 18;
            var ok = UIFactory.Button("Ok", box.transform, new Vector2(320, 80), new Vector2(0, -250), GoldColor, "遊んでみる", 30, out _);
            ok.onClick.AddListener(() =>
            {
                PlayerPrefs.SetString(NewsSeenKey, NewsId);
                PlayerPrefs.Save();
                Destroy(confirm.gameObject);
                confirm = null;
            });
        }

        void ShowConfirm()
        {
            if (confirm != null) return;
            confirm = UIFactory.Stretch("Confirm", transform);
            UIFactory.Panel("Shade", confirm, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f));
            var box = UIFactory.Panel("Box", confirm, new Vector2(820, 300), Vector2.zero, new Color(0.12f, 0.08f, 0.06f, 1f));
            UIFactory.Text("Text", box.transform, "新しく始めると、いまの続きは消えます。\nよろしいですか？", 32, PaperColor, new Vector2(760, 120), new Vector2(0, 55));
            var yes = UIFactory.Button("Yes", box.transform, new Vector2(300, 80), new Vector2(-170, -80), new Color(1f, 0.55f, 0.4f), "新しく始める", 28, out _);
            yes.onClick.AddListener(() => NewRunClicked?.Invoke());
            var no = UIFactory.Button("No", box.transform, new Vector2(300, 80), new Vector2(170, -80), new Color(0.93f, 0.87f, 0.72f), "やめる", 28, out _);
            no.onClick.AddListener(() =>
            {
                Destroy(confirm.gameObject);
                confirm = null;
            });
        }
    }
}
