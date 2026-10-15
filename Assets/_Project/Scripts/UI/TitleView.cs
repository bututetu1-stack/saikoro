using System;
using System.Collections.Generic;
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
        public event Action RaceClicked;   // 対戦「賽レース」
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
            // 自己ベスト（踏破にかかったターン数）
            if (BestRecord.BestTurns > 0)
            {
                var best = UIFactory.Text("Best", root, $"自己ベスト：{BestRecord.BestTurns} ターンで踏破", 26, GoldColor, new Vector2(860, 36), new Vector2(0, art != null && art.titleLogo != null ? 104 : 130));
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
            var fresh = UIFactory.Button("NewRunButton", root, new Vector2(330, 84), new Vector2(-95, -230), new Color(0.93f, 0.87f, 0.72f), "新しく始める", 32, out _);
            // 対戦「賽レース」（いまは AI の練習相手と。通信の対戦はあとで足す）
            var race = UIFactory.Button("RaceButton", root, new Vector2(180, 84), new Vector2(170, -230), new Color(1f, 0.6f, 0.45f), "対戦", 32, out _);
            race.onClick.AddListener(() => view.RaceClicked?.Invoke());
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
            return view;
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
