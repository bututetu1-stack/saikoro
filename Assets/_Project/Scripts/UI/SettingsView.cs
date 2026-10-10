using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>設定の小窓：効果音の音量・演出の速さ・初回の案内をもう一度。始めの画面・マップ・戦闘から開く。</summary>
    public class SettingsView : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.96f, 0.92f, 0.82f);
        static readonly Color GoldColor = new Color(1f, 0.82f, 0.3f);
        static readonly Color ButtonColor = new Color(0.93f, 0.87f, 0.72f);
        static readonly Color ChosenColor = new Color(1f, 0.78f, 0.3f);

        public event Action Closed;
        public event Action HowToClicked;   // 遊び方を開く（プレイ中にも読めるように）

        TextMeshProUGUI volumeText;
        readonly Button[] speedButtons = new Button[GameSettings.Speeds.Length];
        TextMeshProUGUI hintText;
        TextMeshProUGUI bgmText;

        public static SettingsView Create(Transform canvas)
        {
            var root = UIFactory.Stretch("SettingsView", canvas);
            var view = root.gameObject.AddComponent<SettingsView>();
            UIFactory.Panel("Shade", root, new Vector2(1920, 1080), Vector2.zero, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
            var box = UIFactory.Panel("Box", root, new Vector2(1000, 650), Vector2.zero, new Color(0.12f, 0.08f, 0.06f, 1f)).transform;
            UIFactory.Text("Title", box, "設定", 48, GoldColor, new Vector2(900, 70), new Vector2(0, 270)).fontStyle = FontStyles.Bold;

            // 効果音の音量
            UIFactory.Text("VolumeLabel", box, "効果音の音量", 32, PaperColor, new Vector2(300, 60), new Vector2(-300, 170), TextAlignmentOptions.Left);
            var down = UIFactory.Button("VolumeDown", box, new Vector2(90, 70), new Vector2(0, 170), ButtonColor, "−", 40, out _);
            view.volumeText = UIFactory.Text("Volume", box, "", 34, PaperColor, new Vector2(160, 60), new Vector2(130, 170));
            var up = UIFactory.Button("VolumeUp", box, new Vector2(90, 70), new Vector2(260, 170), ButtonColor, "+", 40, out _);
            down.onClick.AddListener(() => view.ChangeVolume(-10));
            up.onClick.AddListener(() => view.ChangeVolume(+10));

            // BGM の音量
            UIFactory.Text("BgmLabel", box, "BGM の音量", 32, PaperColor, new Vector2(300, 60), new Vector2(-300, 80), TextAlignmentOptions.Left);
            var bgmDown = UIFactory.Button("BgmDown", box, new Vector2(90, 70), new Vector2(0, 80), ButtonColor, "−", 40, out _);
            view.bgmText = UIFactory.Text("BgmVolume", box, "", 34, PaperColor, new Vector2(160, 60), new Vector2(130, 80));
            var bgmUp = UIFactory.Button("BgmUp", box, new Vector2(90, 70), new Vector2(260, 80), ButtonColor, "+", 40, out _);
            bgmDown.onClick.AddListener(() => view.ChangeBgm(-10));
            bgmUp.onClick.AddListener(() => view.ChangeBgm(+10));

            // 演出の速さ
            UIFactory.Text("SpeedLabel", box, "演出の速さ", 32, PaperColor, new Vector2(300, 60), new Vector2(-300, -10), TextAlignmentOptions.Left);
            for (int i = 0; i < GameSettings.Speeds.Length; i++)
            {
                int index = i;
                view.speedButtons[i] = UIFactory.Button($"Speed{i}", box, new Vector2(190, 70), new Vector2(-40 + i * 205, -10), ButtonColor, GameSettings.Speeds[i].label, 28, out _);
                view.speedButtons[i].onClick.AddListener(() =>
                {
                    GameSettings.SpeedIndex = index;
                    view.Refresh();
                });
            }

            // 初回の案内
            var hint = UIFactory.Button("ResetHints", box, new Vector2(420, 70), new Vector2(-220, -115), ButtonColor, "はじめての案内をもう一度見る", 26, out _);
            var howTo = UIFactory.Button("HowTo", box, new Vector2(400, 70), new Vector2(220, -115), ButtonColor, "遊び方を見る", 28, out _);
            howTo.onClick.AddListener(() => view.HowToClicked?.Invoke());
            view.hintText = UIFactory.Text("HintNote", box, "", 22, PaperColor, new Vector2(900, 34), new Vector2(0, -170));
            hint.onClick.AddListener(() =>
            {
                Hints.ResetAll();
                view.hintText.text = "次から、はじめての案内がもう一度出ます。";
            });

            var close = UIFactory.Button("Close", box, new Vector2(320, 80), new Vector2(0, -255), GoldColor, "閉じる", 32, out _);
            close.onClick.AddListener(() => view.Closed?.Invoke());

            view.Refresh();
            return view;
        }

        void ChangeVolume(int delta)
        {
            GameSettings.SeVolume += delta;
            Refresh();
            Sfx.Play(SoundId.Buy); // 音の大きさを確かめられるように
        }

        void ChangeBgm(int delta)
        {
            GameSettings.BgmVolume += delta;
            Refresh();
        }

        void Refresh()
        {
            volumeText.text = $"{GameSettings.SeVolume}%";
            bgmText.text = $"{GameSettings.BgmVolume}%";
            for (int i = 0; i < speedButtons.Length; i++)
            {
                speedButtons[i].GetComponent<Image>().color = i == GameSettings.SpeedIndex ? ChosenColor : ButtonColor;
            }
        }
    }
}
