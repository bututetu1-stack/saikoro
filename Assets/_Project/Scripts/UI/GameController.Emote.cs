using System.Collections.Generic;
using SaiNoMichi.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 通信で遊んでいるときの「合図」（決まった言葉を仲間に送る。ハースストーンやクラロワのエモート風）。
    /// ブラウザでは日本語を打ちにくいので、押すだけで気持ちを伝えられるようにする。
    /// </summary>
    public partial class GameController
    {
        // TODO(仕様): 言葉の種類は仮
        static readonly string[] EmotePhrases = { "よろしく！", "ありがとう！", "ナイス！", "ごめん！", "まかせて！", "ちょっと待って！" };
        const float EmoteCooldown = 1.5f;

        RectTransform emoteButton;
        RectTransform emoteMenu;
        float nextEmoteTime;

        /// <summary>合図を送る相手の部屋（協力か、通信の国めぐり。AI の仲間も返事をする）。</summary>
        IPartySession EmoteSession => party ?? tourSession;

        /// <summary>Update から：通信で遊んでいるときだけ、右下に「合図」のボタンを出す。</summary>
        void UpdateEmoteButton()
        {
            bool show = EmoteSession != null && canvas != null;
            if (!show)
            {
                if (emoteButton != null) emoteButton.gameObject.SetActive(false);
                CloseEmoteMenu();
                return;
            }
            if (emoteButton == null)
            {
                var button = UIFactory.Button("EmoteButton", canvas.transform, new Vector2(120, 56), new Vector2(890, -505), new Color(1f, 0.78f, 0.45f), "合図", 26, out _);
                button.onClick.AddListener(ToggleEmoteMenu);
                emoteButton = (RectTransform)button.transform;
            }
            emoteButton.gameObject.SetActive(true);
            // あとから出た画面（戦闘・小窓など）の下に隠れないように
            if (emoteButton.GetSiblingIndex() != emoteButton.parent.childCount - 1 && emoteMenu == null) emoteButton.SetAsLastSibling();
        }

        void ToggleEmoteMenu()
        {
            if (emoteMenu != null)
            {
                CloseEmoteMenu();
                return;
            }
            Sfx.Play(SoundId.Button);
            const float rowH = 58f;
            float h = EmotePhrases.Length * rowH + 16f;
            var panel = UIFactory.Panel("EmoteMenu", canvas.transform, new Vector2(300, h), new Vector2(810, -470 + h / 2f), new Color(0.08f, 0.05f, 0.04f, 0.96f));
            emoteMenu = panel.rectTransform;
            for (int i = 0; i < EmotePhrases.Length; i++)
            {
                int index = i;
                var b = UIFactory.Button($"Phrase{i}", emoteMenu, new Vector2(270, rowH - 8), new Vector2(0, h / 2f - 8 - rowH / 2f - i * rowH), new Color(0.93f, 0.87f, 0.72f), EmotePhrases[i], 24, out _);
                b.onClick.AddListener(() => SendEmote(index));
            }
            emoteMenu.SetAsLastSibling();
        }

        void CloseEmoteMenu()
        {
            if (emoteMenu == null) return;
            Destroy(emoteMenu.gameObject);
            emoteMenu = null;
        }

        void SendEmote(int index)
        {
            CloseEmoteMenu();
            var session = EmoteSession;
            if (session == null || Time.unscaledTime < nextEmoteTime) return;
            nextEmoteTime = Time.unscaledTime + EmoteCooldown;
            session.Send(PartyCommandType.Emote, index);
        }

        /// <summary>合図が届いた（自分が送ったものも届く）：画面の上に少しのあいだ出す。</summary>
        void ShowEmote(PartyCommand c, IReadOnlyList<string> names)
        {
            if (c.a < 0 || c.a >= EmotePhrases.Length) return;
            string name = names != null && c.slot >= 0 && c.slot < names.Count ? names[c.slot] : "仲間";
            StartCoroutine(RaceToast($"{name}：「{EmotePhrases[c.a]}」"));
        }
    }
}
