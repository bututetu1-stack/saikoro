using System;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>結果画面。クリアかゲームオーバーか、かかったターン数などを表示する。</summary>
    public class ResultView : MonoBehaviour
    {
        public event Action RetryClicked;

        public static ResultView Create(Transform canvas, bool cleared, int turns, int hp, int maxHp, int seed)
        {
            var root = UIFactory.Stretch("ResultView", canvas);
            var view = root.gameObject.AddComponent<ResultView>();

            var white = new Color(0.95f, 0.95f, 0.95f);
            UIFactory.Text("Title", root, cleared ? "クリア！" : "ゲームオーバー", 96,
                cleared ? new Color(1f, 0.85f, 0.3f) : new Color(0.9f, 0.4f, 0.4f), new Vector2(1200, 140), new Vector2(0, 220));
            UIFactory.Text("Detail", root, $"かかったターン数 {turns}\n残りHP {hp}/{maxHp}\nシード {seed}", 40, white,
                new Vector2(1000, 200), new Vector2(0, 20));

            var retry = UIFactory.Button("RetryButton", root, new Vector2(420, 100), new Vector2(0, -220),
                new Color(0.95f, 0.92f, 0.8f), "もう一度遊ぶ", 36, out _);
            retry.onClick.AddListener(() => view.RetryClicked?.Invoke());
            return view;
        }
    }
}
