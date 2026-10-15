using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// ボタン・枠の見た目（角丸・縁取り・上から下への光沢・影）。絵を用意しなくてよいよう、起動時にコードで絵を作る。
    /// 白を基にした絵なので、Image の色で好きな色に染められる。
    /// </summary>
    public static class UISkin
    {
        const int Size = 64;
        const int Radius = 14;
        const int SliceBorder = 20;

        static Sprite button, panel;

        /// <summary>ボタン用：角丸・濃い縁・上が明るく下が暗い光沢・上の内側に細い光。</summary>
        public static Sprite Button => button != null ? button : (button = Make(true));

        /// <summary>枠用：角丸・明るい縁（暗い色に染めると、縁がうっすら光って見える）。</summary>
        public static Sprite Panel => panel != null ? panel : (panel = Make(false));

        static Sprite Make(bool forButton)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = forButton ? "UISkinButton" : "UISkinPanel" };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = RoundedRectDistance(x + 0.5f, y + 0.5f);   // 内側ほど小さい（負）
                    float alpha = Mathf.Clamp01(0.5f - d);               // 角をなめらかに
                    float shade;
                    if (forButton)
                    {
                        float t = y / (float)(Size - 1);                 // 下 0 → 上 1
                        shade = Mathf.Lerp(0.78f, 1f, t);                // 上が明るい
                        if (d > -2.5f) shade *= 0.6f;                    // 縁取り（濃い）
                        else if (y >= Size - 6 && d < -2.5f && d > -5f) shade = 1f; // 上の内側の光
                    }
                    else
                    {
                        shade = d > -2f ? 1f : 0.86f;                    // 縁がうっすら明るい
                    }
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                    pixels[y * Size + x] = new Color32(v, v, v, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var border = new Vector4(SliceBorder, SliceBorder, SliceBorder, SliceBorder);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        // 角丸の四角の縁からの距離（内側が負）
        static float RoundedRectDistance(float x, float y)
        {
            float half = Size / 2f;
            float qx = Mathf.Abs(x - half) - (half - Radius);
            float qy = Mathf.Abs(y - half) - (half - Radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - Radius;
        }

        /// <summary>Image を角丸にする（小さいものほど角を小さくする）。</summary>
        public static void ApplyRounded(Image image, Sprite sprite, Vector2 size)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            // 9スライスの縁（20px）が大きさの半分を超えないように、絵の密度を上げて角を小さくする
            float minSide = Mathf.Min(size.x, size.y);
            image.pixelsPerUnitMultiplier = Mathf.Max(1f, 2f * SliceBorder / Mathf.Max(1f, minSide) * 1.05f);
        }

        /// <summary>影を付ける（Image の下にずらして描く。UI の Shadow 効果）。</summary>
        public static void AddShadow(Graphic graphic, Vector2 offset, Color color)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = offset;
            shadow.effectColor = color;
            shadow.useGraphicAlpha = true;
        }
    }

    /// <summary>ボタンの手ざわり：乗せると少し大きく、押すと少し縮む。</summary>
    public class ButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        Selectable selectable;
        Vector3 baseScale = Vector3.one;
        float target = 1f, current = 1f;
        bool hovered;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            baseScale = transform.localScale;
        }

        bool Interactable => selectable == null || selectable.IsInteractable();

        public void OnPointerEnter(PointerEventData e) { hovered = true; target = Interactable ? 1.04f : 1f; }
        public void OnPointerExit(PointerEventData e) { hovered = false; target = 1f; }
        public void OnPointerDown(PointerEventData e) { if (Interactable) target = 0.95f; }
        public void OnPointerUp(PointerEventData e) { target = hovered && Interactable ? 1.04f : 1f; }

        void OnDisable()
        {
            target = current = 1f;
            transform.localScale = baseScale;
        }

        void Update()
        {
            if (Mathf.Approximately(current, target)) return;
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 1.2f);
            transform.localScale = baseScale * current;
        }
    }
}
