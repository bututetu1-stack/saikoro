using System;
using System.Collections.Generic;
using SaiNoMichi.Dice;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ダイスの札を横に並べ、クリックで「選択」だけする部品（決定は呼び出し側のボタンで）。
    /// ワンクリックで決まると押し間違えやすいので、報酬・スターター・入れ替えはこれを使う。
    /// </summary>
    public class DicePicker : MonoBehaviour
    {
        UIArt art;
        IReadOnlyList<DiceInstance> dice;
        Vector2 cardSize;
        float gap;
        Func<int, string> stateFor;
        Func<int, bool> enabledFor;

        public int Selected { get; private set; } = -1;
        public DiceInstance SelectedDie => Selected >= 0 ? dice[Selected] : null;
        public event Action<int> SelectionChanged;

        RectTransform Rect => (RectTransform)transform;

        public static DicePicker Create(Transform parent, Vector2 position, UIArt art, IReadOnlyList<DiceInstance> dice,
            Vector2 cardSize, float gap, Func<int, string> stateFor, Func<int, bool> enabledFor = null)
        {
            // 画面に収まらないほど多いときは札を細くする
            const float maxWidth = 1860f;
            if (dice.Count * (cardSize.x + gap) - gap > maxWidth) cardSize.x = (maxWidth - gap * (dice.Count - 1)) / dice.Count;
            float width = dice.Count * (cardSize.x + gap) - gap;
            var rect = UIFactory.Rect("DicePicker", parent, new Vector2(width, cardSize.y), position);
            var picker = rect.gameObject.AddComponent<DicePicker>();
            picker.art = art;
            picker.dice = dice;
            picker.cardSize = cardSize;
            picker.gap = gap;
            picker.stateFor = stateFor;
            picker.enabledFor = enabledFor ?? (_ => true);
            picker.Rebuild();
            return picker;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= dice.Count || !enabledFor(index)) return;
            Selected = index;
            Rebuild();
            SelectionChanged?.Invoke(index);
        }

        void Rebuild()
        {
            UIFactory.ClearChildren(transform);
            float left = -(dice.Count * (cardSize.x + gap) - gap) / 2f + cardSize.x / 2f;
            for (int i = 0; i < dice.Count; i++)
            {
                int index = i;
                bool enabled = enabledFor(i);
                string state = i == Selected ? "<b>選択中</b>" : stateFor(i);
                var card = DiceCard.Create($"Card{i}", transform, dice[i], art, cardSize, new Vector2(left + i * (cardSize.x + gap), 0),
                    state, !enabled, i == Selected);
                card.Button.interactable = enabled;
                card.Button.onClick.AddListener(() => Select(index));
                if (i == Selected) card.transform.localScale = Vector3.one * 1.06f;
            }
        }
    }
}
