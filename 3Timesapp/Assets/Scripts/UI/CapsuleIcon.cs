using System;
using UnityEngine;
using ThreeTimes.Core;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Iconița unei capsule sigilate (sfera de sticlă cu lumină), desenată din cod, pentru interfață:
    /// simularea din Editor și harta. ready = data a venit: lumina e mai puternică și apare inelul.
    /// </summary>
    public static class CapsuleIcon
    {
        const int Size = 160;
        static Texture2D closedIcon, readyIcon;

        public static Texture2D Get(bool ready)
        {
            if (ready) return readyIcon != null ? readyIcon : (readyIcon = Draw(true));
            return closedIcon != null ? closedIcon : (closedIcon = Draw(false));
        }

        static Texture2D Draw(bool ready)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[Size * Size];
            // Lumina: mentă cât e închisă; aurie când data a venit (capsula „te așteaptă”).
            var mint = ready ? new Color(0.96f, 0.77f, 0.32f) : new Color(0.50f, 0.83f, 0.72f);
            var pale = new Color(0.91f, 0.97f, 0.94f);
            float c = Size / 2f;
            float r = Size * (ready ? 0.36f : 0.40f);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                var col = new Color(0, 0, 0, 0);

                // Inelul (doar când capsula se poate deschide)
                if (ready)
                {
                    float ring = Mathf.Abs(d - Size * 0.46f);
                    if (ring < 1.6f) col = Blend(col, new Color(0.85f, 0.60f, 0.15f, 0.85f * (1f - ring / 1.6f)));
                }

                if (d <= r + 1f)
                {
                    float edge = Mathf.Clamp01(r + 1f - d);
                    // Sticla: aproape transparentă, mai deschisă spre margine.
                    col = Blend(col, new Color(pale.r, pale.g, pale.b, 0.45f * edge));
                    // Lumina din mijloc (un pic mai jos), care se estompează spre margini.
                    float cy = c - r * 0.08f;
                    float lightD = Mathf.Sqrt(dx * dx + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                    float glow = Mathf.Clamp01(1f - lightD / (ready ? 0.75f : 0.6f));
                    col = Blend(col, new Color(mint.r, mint.g, mint.b, glow * glow * (ready ? 0.95f : 0.75f)));
                    float core = Mathf.Clamp01(1f - lightD / (ready ? 0.28f : 0.2f));
                    col = Blend(col, new Color(1f, 1f, 1f, core));
                    // Marginea albă a sferei
                    float rim = Mathf.Abs(d - r);
                    if (rim < 2f) col = Blend(col, new Color(1f, 1f, 1f, 0.9f * (1f - rim / 2f)));
                    // Reflexia, sus în stânga
                    float hx = dx + r * 0.42f, hy = dy - r * 0.45f;
                    float h = Mathf.Abs(Mathf.Sqrt(hx * hx + hy * hy) - r * 0.32f);
                    if (h < 3f && dx < -r * 0.05f && dy > r * 0.05f)
                        col = Blend(col, new Color(1f, 1f, 1f, 0.85f * (1f - h / 3f)));
                }
                pixels[y * Size + x] = col;
            }

            // Câteva puncte de lumină în sferă
            var rnd = new System.Random(ready ? 7 : 3);
            for (int i = 0; i < 7; i++)
            {
                float a = (float)(rnd.NextDouble() * Math.PI * 2), rr = (float)(0.25 + rnd.NextDouble() * 0.5) * r;
                Dot(pixels, c + Mathf.Cos(a) * rr, c + Mathf.Sin(a) * rr, 1.6f + (float)rnd.NextDouble() * 1.4f);
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static void Dot(Color[] pixels, float cx, float cy, float radius)
        {
            for (int y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
            for (int x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(radius + 0.5f - d);
                pixels[y * Size + x] = Blend(pixels[y * Size + x], new Color(1f, 1f, 1f, a));
            }
        }

        static Color Blend(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0.0001f) return new Color(0, 0, 0, 0);
            return new Color(
                (over.r * over.a + under.r * under.a * (1f - over.a)) / a,
                (over.g * over.a + under.g * under.a * (1f - over.a)) / a,
                (over.b * over.a + under.b * under.a * (1f - over.a)) / a,
                a);
        }
    }

    /// <summary>Textele despre data unei capsule („se deschide pe 1 ianuarie 2027”, „peste 3 zile”).</summary>
    public static class CapsuleText
    {
        /// <summary>Data a venit (după ultimul răspuns al serverului sau după ceasul telefonului).</summary>
        public static bool IsReady(SavedTrace c) =>
            c.ready || (RomaniaTime.TryParseUtc(c.unlockAt, out var at) && at <= TestClock.UtcNow);

        /// <summary>Sub sferă: „1 ianuarie 2027” sau, în ultima lună, „peste 12 zile”.</summary>
        public static string Short(SavedTrace c)
        {
            if (!RomaniaTime.TryParseUtc(c.unlockAt, out var at)) return "";
            string until = RomaniaTime.Until(at);
            if (until == null) return "Atinge ca s-o deschizi";
            var local = RomaniaTime.FromUtc(at);
            return (local.Date - RomaniaTime.Today).TotalDays <= 31 ? until : RomaniaTime.DateText(local);
        }

        /// <summary>La atingerea unei capsule încă închise: „Capsula se deschide pe 1 ianuarie 2027 (peste 37 de zile).”</summary>
        public static string NotYet(SavedTrace c) => $"Capsula se deschide {When(c)}.";

        /// <summary>„pe 1 ianuarie 2027 (peste 37 de zile)”, „mâine (…)”, „azi la 14:05”, „acum”.</summary>
        public static string When(SavedTrace c)
        {
            if (!RomaniaTime.TryParseUtc(c.unlockAt, out var at)) return "la data aleasă";
            var local = RomaniaTime.FromUtc(at);
            string until = RomaniaTime.Until(at);
            if (until == null) return "acum";
            if (until.StartsWith("azi")) return until;
            if (until == "mâine") return $"mâine ({RomaniaTime.DateText(local)})";
            return $"pe {RomaniaTime.DateText(local)} ({until})";
        }
    }
}
