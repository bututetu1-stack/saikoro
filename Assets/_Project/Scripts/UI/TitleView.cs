using System;
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
        public event Action HowToClicked;
        public event Action QuitClicked;

        RectTransform confirm;

        /// <param name="save">続きのセーブ（中身を少し見せる）。読めなかったときは null で、error に理由。</param>
        public static TitleView Create(Transform canvas, UIArt art, RunSave save, string error)
        {
            var root = UIFactory.Stretch("TitleView", canvas);
            var view = root.gameObject.AddComponent<TitleView>();
            var bg = art != null ? (art.titleBackground != null ? art.titleBackground : art.mapBackground) : null;
            UIFactory.Background(root, bg, new Color(0.85f, 0.8f, 0.65f));
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.35f));

            var titlePanel = UIFactory.Panel("TitlePanel", root, new Vector2(900, 200), new Vector2(0, 260), ShadeColor);
            var title = UIFactory.Text("Title", titlePanel.transform, "賽ノ道", 110, GoldColor, new Vector2(860, 140), new Vector2(0, 15));
            title.fontStyle = FontStyles.Bold;
            UIFactory.Text("Sub", titlePanel.transform, "サイコロを育てて、出目で道を選ぶ", 28, PaperColor, new Vector2(860, 40), new Vector2(0, -70));

            string info = save != null
                ? $"第{save.layerIndex + 1}層　HP {save.hp}/{save.maxHp}　{save.gold} G　ターン {save.turn}" + (string.IsNullOrEmpty(save.savedAt) ? "" : $"\n<size=22>{save.savedAt} に保存</size>")
                : error != null ? $"続きのデータを読めませんでした。\n<size=22>{error}</size>" : "続きのデータはありません。";
            var infoPanel = UIFactory.Panel("InfoPanel", root, new Vector2(760, 110), new Vector2(0, 40), ShadeColor);
            UIFactory.Text("Info", infoPanel.transform, info, 28, PaperColor, new Vector2(720, 100), Vector2.zero);

            var cont = UIFactory.Button("ContinueButton", root, new Vector2(520, 96), new Vector2(0, -110), GoldColor, "続きから", 38, out _);
            cont.interactable = save != null;
            cont.onClick.AddListener(() => view.ContinueClicked?.Invoke());
            var fresh = UIFactory.Button("NewRunButton", root, new Vector2(520, 84), new Vector2(0, -230), new Color(0.93f, 0.87f, 0.72f), "新しく始める", 32, out _);
            // 続きがあるときは、消してよいか一度だけ聞く
            fresh.onClick.AddListener(() =>
            {
                if (save == null) view.NewRunClicked?.Invoke();
                else view.ShowConfirm();
            });
            var howTo = UIFactory.Button("HowToButton", root, new Vector2(400, 66), new Vector2(-210, -330), new Color(0.93f, 0.87f, 0.72f), "遊び方", 30, out _);
            howTo.onClick.AddListener(() => view.HowToClicked?.Invoke());
            var quit = UIFactory.Button("QuitButton", root, new Vector2(400, 66), new Vector2(210, -330), new Color(0.75f, 0.68f, 0.58f), "ゲームを終える", 30, out _);
            quit.onClick.AddListener(() => view.QuitClicked?.Invoke());
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
