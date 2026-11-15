using System.Collections.Generic;
using UnityEngine;
using ThreeTimes.AR;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Iconița unei forme pentru interfață (foaia „Lasă o urmă”, ecranul urmei, harta, urmele din Editor): aceleași
    /// puncte ca în AR, desenate într-o textură, cu margini estompate. Iconițele se păstrează în memorie după stil.
    /// </summary>
    public static class TraceIcon
    {
        const int Size = 160;
        const int MaxCached = 80;

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly LinkedList<string> order = new LinkedList<string>();

        public static Texture2D Get(TraceStyle style)
        {
            string key = style.Key + "|" + style.Shape;
            if (cache.TryGetValue(key, out var cached))
            {
                order.Remove(key);
                order.AddLast(key);
                return cached;
            }

            var texture = Render(style);
            cache[key] = texture;
            order.AddLast(key);
            while (cache.Count > MaxCached && order.First != null)
            {
                string old = order.First.Value;
                order.RemoveFirst();
                if (cache.TryGetValue(old, out var t)) Object.Destroy(t);
                cache.Remove(old);
            }
            return texture;
        }

        static Texture2D Render(TraceStyle style)
        {
            var pixels = new Color[Size * Size]; // transparent
            // Aceeași sămânță pentru același stil, ca iconița să fie stabilă.
            var dots = TraceShapeBuilder.Dots(style, TraceShapeBuilder.Seed(style.Key));
            float scale = Size / 100f;

            foreach (var dot in dots)
            {
                float cx = dot.Position.x * scale, cy = (100f - dot.Position.y) * scale; // textura are Y în sus
                float radius = dot.Radius * scale * 1.3f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - radius)), x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cx + radius));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - radius)), y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cy + radius));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                        if (d >= 1f) continue;
                        float a = dot.Color.a * (1f - d * d);            // estompat spre margine
                        int i = y * Size + x;
                        var under = pixels[i];
                        float outA = a + under.a * (1f - a);              // „peste” (alpha compositing)
                        if (outA <= 0f) continue;
                        var c = (dot.Color * a + under * under.a * (1f - a)) / outA;
                        c.a = outA;
                        pixels[i] = c;
                    }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
