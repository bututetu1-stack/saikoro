using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SaiNoMichi.EditorTools
{
    /// <summary>
    /// unityroom のサムネイル用 GIF を作る（プレイ中の画面を一定の間隔で録って、正方形に切り抜いて GIF にする）。
    /// 使い方（プレイ中に execute_code などから）：ThumbnailGif.Start(部品, 出力パス, 切り抜き, 大きさ, fps, 秒数)。録り終わると書き出す。
    /// 容量を抑えるため、2コマ目からは前のコマから変わったところだけを書く（変わっていない画素は透明）。
    /// ゲーム側の部品のコルーチンで毎フレーム録る（エディタ用のスクリプトはプレイ中の部品として付けられず、
    /// エディタの更新や onBeforeRender は、エディタからフレームを進めている間は毎フレーム呼ばれないため）。
    /// </summary>
    public static class ThumbnailGif
    {
        static string outputPath;
        static RectInt crop;          // 1920×1080 の画面のどこを切り抜くか（左下が原点）
        static int size;
        static float interval;
        static readonly List<Color32[]> frames = new List<Color32[]>();
        public static string LastResult;

        /// <param name="host">録画のコルーチンを動かすゲーム側の部品（GameController など）。</param>
        public static void Start(MonoBehaviour host, string path, RectInt cropRect, int outputSize = 512, int fps = 10, float seconds = 4f)
        {
            outputPath = path;
            crop = cropRect;
            size = outputSize;
            interval = 1f / fps;
            frames.Clear();
            LastResult = "recording";
            host.StartCoroutine(Record(seconds));
        }

        static IEnumerator Record(float seconds)
        {
            float next = Time.time, end = Time.time + seconds;
            while (Time.time < end)
            {
                if (Time.time >= next)
                {
                    frames.Add(Capture());
                    next += interval;
                }
                yield return null;
            }
            try
            {
                long bytes = Write(outputPath, frames, size, Mathf.RoundToInt(interval * 100));
                LastResult = $"{frames.Count} frames, {bytes / 1024} KB -> {outputPath}";
            }
            catch (Exception e)
            {
                LastResult = "error: " + e.Message;
            }
        }

        static Color32[] Capture()
        {
            var cam = Camera.main;
            var full = RenderTexture.GetTemporary(1920, 1080, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = full;
            cam.Render();
            cam.targetTexture = prev;

            // 切り抜いて、2段階で縮める（一気に縮めるとガタガタになるため）
            var mid = RenderTexture.GetTemporary(size * 2, size * 2, 0);
            Graphics.Blit(full, mid, new Vector2(crop.width / 1920f, crop.height / 1080f), new Vector2(crop.x / 1920f, crop.y / 1080f));
            var small = RenderTexture.GetTemporary(size, size, 0);
            Graphics.Blit(mid, small);

            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            RenderTexture.active = small;
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            var pixels = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            RenderTexture.ReleaseTemporary(full);
            RenderTexture.ReleaseTemporary(mid);
            RenderTexture.ReleaseTemporary(small);
            return pixels;
        }

        // ---- GIF の書き出し ----

        const int Transparent = 255;

        static long Write(string path, List<Color32[]> frames, int size, int delayCs)
        {
            var palette = BuildPalette(frames, 255);
            var cache = new int[32768];
            for (int i = 0; i < cache.Length; i++) cache[i] = -1;

            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
                w.Write((ushort)size);
                w.Write((ushort)size);
                w.Write((byte)0xF7); // 全体の色表あり・256色
                w.Write((byte)0);
                w.Write((byte)0);
                for (int i = 0; i < 256; i++)
                {
                    var c = i < palette.Count ? palette[i] : new Color32(0, 0, 0, 255);
                    w.Write(c.r); w.Write(c.g); w.Write(c.b);
                }
                // くり返し再生
                w.Write(new byte[] { 0x21, 0xFF, 0x0B });
                w.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                w.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

                // 画像は下から上に並んでいるので、上から下に直す
                int[] prev = null;
                for (int f = 0; f < frames.Count; f++)
                {
                    var src = frames[f];
                    var idx = new int[size * size];
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            var c = src[(size - 1 - y) * size + x];
                            int key = ((c.r >> 3) << 10) | ((c.g >> 3) << 5) | (c.b >> 3);
                            if (cache[key] < 0) cache[key] = Nearest(palette, c);
                            idx[y * size + x] = cache[key];
                        }
                    }

                    // 前のコマから変わったところを囲む四角
                    int x0 = 0, y0 = 0, x1 = size - 1, y1 = size - 1;
                    if (prev != null)
                    {
                        x0 = size; y0 = size; x1 = -1; y1 = -1;
                        for (int i = 0; i < idx.Length; i++)
                        {
                            if (idx[i] == prev[i]) continue;
                            int x = i % size, y = i / size;
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                        if (x1 < 0) { x0 = 0; y0 = 0; x1 = 0; y1 = 0; } // 変化なし：1画素だけ（透明）
                    }
                    int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
                    var sub = new byte[rw * rh];
                    for (int y = 0; y < rh; y++)
                    {
                        for (int x = 0; x < rw; x++)
                        {
                            int i = (y + y0) * size + (x + x0);
                            sub[y * rw + x] = (byte)(prev != null && idx[i] == prev[i] ? Transparent : idx[i]);
                        }
                    }

                    // 表示の時間・透明色・前のコマを残す
                    w.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)((1 << 2) | (prev != null ? 1 : 0)) });
                    w.Write((ushort)delayCs);
                    w.Write((byte)Transparent);
                    w.Write((byte)0);
                    w.Write((byte)0x2C);
                    w.Write((ushort)x0); w.Write((ushort)y0); w.Write((ushort)rw); w.Write((ushort)rh);
                    w.Write((byte)0);
                    WriteLzw(w, sub, 8);
                    prev = idx;
                }
                w.Write((byte)0x3B);
                return fs.Length;
            }
        }

        static int Nearest(List<Color32> palette, Color32 c)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                var p = palette[i];
                int dr = p.r - c.r, dg = p.g - c.g, db = p.b - c.b;
                int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // メディアンカット：色を count 色に減らす
        static List<Color32> BuildPalette(List<Color32[]> frames, int count)
        {
            var hist = new Dictionary<int, int>();
            foreach (var f in frames)
            {
                for (int i = 0; i < f.Length; i += 3)
                {
                    var c = f[i];
                    int key = ((c.r >> 3) << 10) | ((c.g >> 3) << 5) | (c.b >> 3);
                    hist.TryGetValue(key, out int n);
                    hist[key] = n + 1;
                }
            }
            var boxes = new List<List<KeyValuePair<int, int>>> { new List<KeyValuePair<int, int>>(hist) };
            while (boxes.Count < count)
            {
                // いちばん幅の広い箱を、その軸の重み付き中央で割る
                int bi = -1, axis = 0, bestRange = 0;
                for (int b = 0; b < boxes.Count; b++)
                {
                    if (boxes[b].Count < 2) continue;
                    for (int a = 0; a < 3; a++)
                    {
                        int min = 31, max = 0;
                        foreach (var kv in boxes[b])
                        {
                            int v = (kv.Key >> (10 - a * 5)) & 31;
                            if (v < min) min = v;
                            if (v > max) max = v;
                        }
                        if (max - min > bestRange) { bestRange = max - min; bi = b; axis = a; }
                    }
                }
                if (bi < 0) break;
                var box = boxes[bi];
                int sh = 10 - axis * 5;
                box.Sort((p, q) => ((p.Key >> sh) & 31).CompareTo((q.Key >> sh) & 31));
                long total = 0;
                foreach (var kv in box) total += kv.Value;
                long acc = 0;
                int cut = 1;
                for (int i = 0; i < box.Count - 1; i++)
                {
                    acc += box[i].Value;
                    if (acc >= total / 2) { cut = i + 1; break; }
                }
                boxes[bi] = box.GetRange(0, cut);
                boxes.Add(box.GetRange(cut, box.Count - cut));
            }
            var palette = new List<Color32>();
            foreach (var box in boxes)
            {
                long r = 0, g = 0, b = 0, n = 0;
                foreach (var kv in box)
                {
                    r += ((kv.Key >> 10) & 31) * kv.Value;
                    g += ((kv.Key >> 5) & 31) * kv.Value;
                    b += (kv.Key & 31) * kv.Value;
                    n += kv.Value;
                }
                if (n == 0) continue;
                palette.Add(new Color32((byte)(r * 255 / (31 * n)), (byte)(g * 255 / (31 * n)), (byte)(b * 255 / (31 * n)), 255));
            }
            return palette;
        }

        // LZW（GIF の標準の圧縮）
        static void WriteLzw(BinaryWriter w, byte[] data, int minCodeSize)
        {
            w.Write((byte)minCodeSize);
            var bytes = new List<byte>();
            int clear = 1 << minCodeSize, eoi = clear + 1;
            int initBits = minCodeSize + 1;
            int codeSize = initBits, maxCode = (1 << codeSize) - 1, next = clear + 2;
            bool clearFlag = false;
            int accum = 0, bits = 0;
            var dict = new Dictionary<int, int>();

            void Output(int code)
            {
                accum |= code << bits;
                bits += codeSize;
                while (bits >= 8)
                {
                    bytes.Add((byte)(accum & 0xFF));
                    accum >>= 8;
                    bits -= 8;
                }
                if (next > maxCode || clearFlag)
                {
                    if (clearFlag)
                    {
                        codeSize = initBits;
                        maxCode = (1 << codeSize) - 1;
                        clearFlag = false;
                    }
                    else
                    {
                        codeSize++;
                        maxCode = codeSize == 12 ? 4096 : (1 << codeSize) - 1;
                    }
                }
            }

            Output(clear);
            int ent = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int c = data[i];
                int key = (ent << 8) | c;
                if (dict.TryGetValue(key, out int code))
                {
                    ent = code;
                    continue;
                }
                Output(ent);
                ent = c;
                if (next < 4096)
                {
                    dict[key] = next++;
                }
                else
                {
                    dict.Clear();
                    next = clear + 2;
                    clearFlag = true;
                    Output(clear);
                }
            }
            Output(ent);
            Output(eoi);
            if (bits > 0) bytes.Add((byte)(accum & 0xFF));

            for (int i = 0; i < bytes.Count; i += 255)
            {
                int n = Math.Min(255, bytes.Count - i);
                w.Write((byte)n);
                for (int j = 0; j < n; j++) w.Write(bytes[i + j]);
            }
            w.Write((byte)0);
        }
    }
}
