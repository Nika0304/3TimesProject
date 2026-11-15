using System.Collections.Generic;
using UnityEngine;
using ThreeTimes.Data;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Construiește forma din particule a unei urme, după stilul ales de AI (TraceStyle): una din formele de bază
    /// (floare, picătură, semilună, inimă, stea, spirală, val, nor, flacără, mugure, explozie, inele), cu culoarea,
    /// numărul de petale / raze și cât de ascuțită e. Intensitatea mărește forma și o îmbogățește.
    /// Punctele se așază puțin diferit la fiecare urmă (după id). Aceleași puncte desenează și iconițele din interfață.
    /// </summary>
    public static class TraceShapeBuilder
    {
        /// <summary>Un punct al formei, în unități de machetă: 0–100, centrul în (50, 50), Y în jos.</summary>
        public struct Dot
        {
            public Vector3 Position;
            public float Radius;
            public Color Color;
        }

        /// <summary>Diametrul formei în metri, pentru fiecare nivel de intensitate.</summary>
        public static float Diameter(int level) => level == 0 ? 0.45f : level == 1 ? 0.62f : 0.82f;

        /// <summary>Compatibilitate: forma uneia din cele trei emoții de la început („joy”, „sadness”, „longing”).</summary>
        public static GameObject Build(string emotionKey, int level, string seedText, Material material, Transform parent,
                                       string traceType = "TEXT")
        {
            string code = emotionKey == "sadness" ? "SADNESS" : emotionKey == "longing" ? "LONGING" : "JOY";
            var look = TraceStyle.Legacy(code);
            look.intensity = level;
            return Build(TraceStyle.Of(look), seedText, material, parent, traceType);
        }

        /// <param name="traceType">TEXT, PHOTO sau VOICE: la poză forma stă pe un polaroid, la voce pe un microfon.</param>
        public static GameObject Build(TraceStyle style, string seedText, Material material, Transform parent,
                                       string traceType = "TEXT")
        {
            int seed = Seed(seedText);
            var dots = Dots(style, seed);

            float unit = Diameter(style.Level) / 100f; // metri pe unitate de machetă
            var go = CreateSystem("Shape", parent, material, out var system);
            foreach (var dot in dots)
                Emit(system, new Vector3((dot.Position.x - 50f) * unit, (50f - dot.Position.y) * unit, dot.Position.z * unit),
                     dot.Radius * 2f * unit * 1.5f, dot.Color); // textura e estompată spre margini

            if (traceType == "PHOTO" || traceType == "VOICE")
            {
                var backdrop = TraceBackdropMesh.Create(traceType, Diameter(style.Level), style.Color, material, go.transform);
                if (traceType == "VOICE") backdrop.AddComponent<TraceTypePulse>();
            }

            var animator = go.AddComponent<TraceShapeAnimator>();
            animator.Setup(style.Motion, style.Level, seed);
            return go;
        }

        public static int Seed(string text)
        {
            int seed = 17;
            foreach (char c in text ?? "") seed = unchecked(seed * 31 + c);
            return seed;
        }

        /// <summary>Punctele formei (folosite și pentru iconițele din interfață).</summary>
        public static List<Dot> Dots(TraceStyle style, int seed)
        {
            var r = new System.Random(seed);
            var s = new ShapeContext { Style = style, Random = r, Level = style.Level };
            switch (style.Shape)
            {
                case "drop": return Drop(s);
                case "crescent": return Crescent(s);
                case "heart": return Heart(s);
                case "star": return Star(s);
                case "spiral": return Spiral(s);
                case "wave": return Wave(s);
                case "cloud": return Cloud(s);
                case "flame": return Flame(s);
                case "sprout": return Sprout(s);
                case "burst": return Burst(s);
                case "ring": return Ring(s);
                default: return Flower(s);
            }
        }

        // ---------------------------------------------------------------- particule

        static GameObject CreateSystem(string name, Transform parent, Material material, out ParticleSystem system)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.startLifetime = 1000000f; // particulele rămân pe loc; mișcarea vine din TraceShapeAnimator
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
            return go;
        }

        static void Emit(ParticleSystem system, Vector3 position, float size, Color color)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.zero,
                startSize = size,
                startColor = color,
                startLifetime = 1000000f
            };
            system.Emit(emit, 1);
        }

        // ---------------------------------------------------------------- formele de bază

        class ShapeContext
        {
            public TraceStyle Style;
            public System.Random Random;
            public int Level;
            public readonly List<Dot> Dots = new List<Dot>();

            /// <summary>Câte puncte pentru o formă plină: mai multe la intensitate mare.</summary>
            public int Count(int medium) => Mathf.RoundToInt(medium * (Level == 0 ? 0.7f : Level == 1 ? 1f : 1.3f));
            public float Range(float min, float max) => min + (float)Random.NextDouble() * (max - min);

            public void Add(float x, float y, float minRadius, float maxRadius, float minAlpha, bool accent = false)
            {
                var color = accent ? Style.Accent : Style.Color;
                color.a = Range(minAlpha, 1f);
                Dots.Add(new Dot { Position = new Vector3(x, y, Range(-4f, 4f)), Radius = Range(minRadius, maxRadius), Color = color });
            }

            /// <summary>Umple o formă dată printr-o funcție „e punctul înăuntru?”.</summary>
            public void Fill(System.Func<float, float, bool> inside, int count, float minRadius = 1.2f, float maxRadius = 3f,
                             float minAlpha = 0.55f, bool accent = false)
            {
                int added = 0, guard = 0;
                while (added < count && guard++ < 40000)
                {
                    float x = Range(3f, 97f), y = Range(3f, 97f);
                    if (!inside(x, y)) continue;
                    Add(x, y, minRadius, maxRadius, minAlpha, accent);
                    added++;
                }
            }

            /// <summary>Puncte de-a lungul unei linii (pentru tije, raze, contururi).</summary>
            public void Line(float x1, float y1, float x2, float y2, float step, float minRadius, float maxRadius,
                             float minAlpha, bool accent = false, float jitter = 1f)
            {
                float length = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
                int n = Mathf.Max(1, Mathf.RoundToInt(length / step));
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    Add(Mathf.Lerp(x1, x2, t) + Range(-jitter, jitter), Mathf.Lerp(y1, y2, t) + Range(-jitter, jitter),
                        minRadius, maxRadius, minAlpha, accent);
                }
            }

            /// <summary>Câteva puncte mici, rare, în jur (aura de la intensitatea „puternic”).</summary>
            public void Halo(float from, float to, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    float rr = Range(from, to), t = Range(0f, 2f * Mathf.PI);
                    Add(50f + rr * Mathf.Cos(t), 50f + rr * Mathf.Sin(t), 0.8f, 1.6f, 0.3f);
                }
            }
        }

        /// <summary>Floare: petale în cerc (câte spune AI-ul), miez în culoarea de accent; ascuțimea lungește petalele.</summary>
        static List<Dot> Flower(ShapeContext s)
        {
            int petals = s.Style.Petals;
            int perPetal = s.Count(9);
            float distance = s.Level == 0 ? 19f : s.Level == 1 ? 23f : 27f;
            float stretch = Mathf.Lerp(0.8f, 1.6f, s.Style.Sharpness);
            float width = Mathf.Lerp(0.8f, 0.45f, s.Style.Sharpness);
            for (int i = 0; i < petals; i++)
            {
                float angle = 2f * Mathf.PI * i / petals - Mathf.PI / 2f;
                float cx = 50f + distance * Mathf.Cos(angle), cy = 50f + distance * Mathf.Sin(angle);
                for (int k = 0; k < perPetal; k++)
                {
                    // Un punct în elipsa petalei, orientată spre exterior.
                    float along = s.Range(-9f, 9f) * stretch, across = s.Range(-9f, 9f) * width;
                    if ((along * along) / (81f * stretch * stretch) + (across * across) / (81f * width * width) > 1f) { k--; continue; }
                    float x = cx + along * Mathf.Cos(angle) - across * Mathf.Sin(angle);
                    float y = cy + along * Mathf.Sin(angle) + across * Mathf.Cos(angle);
                    s.Add(x, y, 1.6f, 3.4f, 0.65f);
                }
            }
            s.Fill((x, y) => (x - 50) * (x - 50) + (y - 50) * (y - 50) <= 64f, s.Count(12), 2f, 4f, 1f, accent: true);
            if (s.Level == 2) s.Halo(40f, 47f, 14);
            return s.Dots;
        }

        /// <summary>Picătură: se alungește cu intensitatea; de la „mediu” lasă stropi sub ea.</summary>
        static List<Dot> Drop(ShapeContext s)
        {
            float height = s.Level == 0 ? 24f : s.Level == 1 ? 34f : 40f;
            const float centerY = 62f, radius = 21f;
            float top = 50f - height;
            s.Fill((x, y) => y < centerY
                    ? y >= top && Mathf.Abs(x - 50f) <= (y - top) / (centerY - top) * radius
                    : (x - 50f) * (x - 50f) + (y - centerY) * (y - centerY) <= radius * radius,
                s.Count(80));
            int trail = s.Level == 0 ? 0 : s.Level == 1 ? 4 : 8;
            for (int k = 0; k < trail; k++)
                s.Add(50f + s.Range(-5f, 5f), 88f + k * 1.6f + s.Range(0f, 2f), 1f, 2.2f, 0.4f, accent: true);
            return s.Dots;
        }

        /// <summary>Semilună: se subțiază cu intensitatea; la „puternic” apar câteva stele.</summary>
        static List<Dot> Crescent(ShapeContext s)
        {
            float outer = s.Level == 0 ? 28f : s.Level == 1 ? 33f : 38f;
            float inner = s.Level == 0 ? 24f : s.Level == 1 ? 28f : 33f;
            float offset = s.Level == 0 ? 14f : 15f;
            s.Fill((x, y) =>
            {
                float dx = x - 50f, dy = y - 50f, ix = dx - offset, iy = dy + offset * 0.6f;
                return dx * dx + dy * dy <= outer * outer && ix * ix + iy * iy > inner * inner;
            }, s.Count(80));
            if (s.Level == 2)
            {
                s.Add(78f, 20f, 1.8f, 1.8f, 0.9f, accent: true);
                s.Add(86f, 32f, 1.8f, 1.8f, 0.9f, accent: true);
                s.Add(70f, 12f, 1.8f, 1.8f, 0.9f, accent: true);
            }
            return s.Dots;
        }

        /// <summary>Inimă: forma clasică, plină; ascuțimea face vârful mai lung.</summary>
        static List<Dot> Heart(ShapeContext s)
        {
            float scale = s.Level == 0 ? 30f : s.Level == 1 ? 36f : 42f;
            float tip = Mathf.Lerp(1f, 1.25f, s.Style.Sharpness);
            s.Fill((x, y) =>
            {
                float u = (x - 50f) / scale, v = -(y - 47f) / scale;
                if (v < 0) v /= tip;
                float a = u * u + v * v - 1f;
                return a * a * a - u * u * v * v * v <= 0f;
            }, s.Count(95));
            s.Fill((x, y) => (x - 41f) * (x - 41f) + (y - 40f) * (y - 40f) <= 20f, s.Count(6), 1.4f, 2.6f, 0.9f, accent: true);
            if (s.Level == 2) s.Halo(42f, 48f, 12);
            return s.Dots;
        }

        /// <summary>Stea: câte vârfuri spune AI-ul; ascuțimea face vârfurile mai subțiri.</summary>
        static List<Dot> Star(ShapeContext s)
        {
            int points = Mathf.Max(4, s.Style.Petals);
            float outer = s.Level == 0 ? 30f : s.Level == 1 ? 38f : 45f;
            float inner = outer * Mathf.Lerp(0.6f, 0.32f, s.Style.Sharpness);
            s.Fill((x, y) =>
            {
                float dx = x - 50f, dy = y - 50f;
                float angle = Mathf.Atan2(dy, dx) + Mathf.PI / 2f;
                float sector = 2f * Mathf.PI / points;
                float t = Mathf.Repeat(angle, sector) / sector;          // 0..1 în sectorul unui vârf
                float edge = Mathf.Lerp(outer, inner, Mathf.Abs(t - 0.5f) * 2f);
                return dx * dx + dy * dy <= edge * edge;
            }, s.Count(90));
            s.Fill((x, y) => (x - 50) * (x - 50) + (y - 50) * (y - 50) <= 49f, s.Count(8), 1.6f, 3f, 1f, accent: true);
            return s.Dots;
        }

        /// <summary>Spirală: brațe (câte spune AI-ul, 1–4) care se rotesc din centru.</summary>
        static List<Dot> Spiral(ShapeContext s)
        {
            int arms = Mathf.Clamp(s.Style.Petals / 3, 1, 4);
            float maxRadius = s.Level == 0 ? 30f : s.Level == 1 ? 38f : 45f;
            float turns = Mathf.Lerp(1.2f, 2.2f, s.Style.Sharpness);
            int perArm = s.Count(60 / arms + 10);
            for (int a = 0; a < arms; a++)
                for (int i = 0; i < perArm; i++)
                {
                    float t = i / (float)perArm;
                    float angle = 2f * Mathf.PI * (turns * t + a / (float)arms);
                    float radius = 3f + t * maxRadius;
                    s.Add(50f + radius * Mathf.Cos(angle) + s.Range(-1.5f, 1.5f), 50f + radius * Mathf.Sin(angle) + s.Range(-1.5f, 1.5f),
                          Mathf.Lerp(2.6f, 1.2f, t), Mathf.Lerp(3.4f, 1.8f, t), 0.6f, accent: i % 7 == 0);
                }
            return s.Dots;
        }

        /// <summary>Val: linii ondulate una sub alta; ascuțimea face undele mai dese.</summary>
        static List<Dot> Wave(ShapeContext s)
        {
            int lines = s.Level == 0 ? 2 : s.Level == 1 ? 3 : 4;
            float amplitude = s.Level == 0 ? 6f : s.Level == 1 ? 8f : 10f;
            float frequency = Mathf.Lerp(1.2f, 2.6f, s.Style.Sharpness);
            for (int l = 0; l < lines; l++)
            {
                float baseY = 50f + (l - (lines - 1) / 2f) * 14f;
                for (float x = 12f; x <= 88f; x += 2.2f)
                {
                    float y = baseY + amplitude * Mathf.Sin((x - 12f) / 76f * frequency * 2f * Mathf.PI + l * 0.9f);
                    s.Add(x + s.Range(-0.8f, 0.8f), y + s.Range(-0.8f, 0.8f), 1.3f, 2.6f, 0.55f, accent: l % 2 == 1);
                }
            }
            return s.Dots;
        }

        /// <summary>Nor: câteva cercuri lipite; la ascuțime mare marginile devin zimțate (neliniște).</summary>
        static List<Dot> Cloud(ShapeContext s)
        {
            float k = s.Level == 0 ? 0.8f : s.Level == 1 ? 1f : 1.15f;
            var puffs = new[] { (34f, 56f, 15f), (50f, 46f, 19f), (66f, 56f, 15f), (50f, 62f, 14f), (42f, 50f, 12f) };
            float jag = s.Style.Sharpness * 5f;
            s.Fill((x, y) =>
            {
                foreach (var (px, py, pr) in puffs)
                {
                    float dx = x - (50f + (px - 50f) * k), dy = y - (50f + (py - 50f) * k);
                    float angle = Mathf.Atan2(dy, dx);
                    float edge = pr * k + jag * Mathf.Sin(angle * 9f);
                    if (dx * dx + dy * dy <= edge * edge) return true;
                }
                return false;
            }, s.Count(100));
            for (int i = 0; i < s.Count(8); i++)
                s.Add(s.Range(36f, 64f), s.Range(44f, 60f), 1.4f, 2.4f, 0.7f, accent: true);
            return s.Dots;
        }

        /// <summary>Flacără: limbă care urcă, cu miez în culoarea de accent; ascuțimea o face mai zbuciumată.</summary>
        static List<Dot> Flame(ShapeContext s)
        {
            float height = s.Level == 0 ? 55f : s.Level == 1 ? 68f : 80f;
            float bottom = 50f + height * 0.45f, top = bottom - height;
            float wobble = Mathf.Lerp(1.5f, 6f, s.Style.Sharpness);
            System.Func<float, float, float, bool> inside = (x, y, scale) =>
            {
                if (y < top || y > bottom) return false;
                float t = (bottom - y) / (bottom - top);               // 0 jos, 1 sus
                float width = 22f * scale * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f)) * (1f - 0.35f * t);
                float center = 50f + wobble * Mathf.Sin(t * 7f);
                return Mathf.Abs(x - center) <= width;
            };
            s.Fill((x, y) => inside(x, y, 1f), s.Count(95));
            s.Fill((x, y) => inside(x, y, 0.45f) && y > top + (bottom - top) * 0.35f, s.Count(25), 1.4f, 2.8f, 0.8f, accent: true);
            return s.Dots;
        }

        /// <summary>Mugure: o tijă cu frunze (câte perechi spune AI-ul) și un boboc sus.</summary>
        static List<Dot> Sprout(ShapeContext s)
        {
            float top = s.Level == 0 ? 30f : s.Level == 1 ? 22f : 14f;
            const float bottom = 88f;
            s.Line(50f, bottom, 50f, top + 6f, 2.4f, 1.2f, 2f, 0.7f, accent: true, jitter: 0.5f);
            int pairs = Mathf.Clamp(s.Style.Petals / 3, 1, 3);
            float leaf = Mathf.Lerp(13f, 9f, s.Style.Sharpness);
            for (int p = 0; p < pairs; p++)
            {
                float y = bottom - 14f - p * 16f;
                foreach (int side in new[] { -1, 1 })
                {
                    float cx = 50f + side * leaf, cy = y - leaf * 0.35f;
                    for (int k = 0; k < s.Count(16); k++)
                    {
                        float u = s.Range(-1f, 1f), v = s.Range(-1f, 1f);
                        if (u * u + v * v > 1f) { k--; continue; }
                        s.Add(cx + u * leaf, cy + v * leaf * 0.45f, 1.2f, 2.6f, 0.6f);
                    }
                }
            }
            s.Fill((x, y) => (x - 50f) * (x - 50f) + (y - top) * (y - top) <= 64f, s.Count(18), 1.4f, 3f, 0.8f);
            return s.Dots;
        }

        /// <summary>Explozie: raze din centru (câte spune AI-ul), ca artificiile; ascuțimea le face mai subțiri și lungi.</summary>
        static List<Dot> Burst(ShapeContext s)
        {
            int rays = Mathf.Max(5, s.Style.Petals);
            float length = s.Level == 0 ? 30f : s.Level == 1 ? 38f : 45f;
            float step = Mathf.Lerp(3.2f, 2.2f, s.Style.Sharpness);
            for (int i = 0; i < rays; i++)
            {
                float angle = 2f * Mathf.PI * i / rays + s.Range(-0.08f, 0.08f);
                float len = length * s.Range(0.8f, 1f);
                for (float d = 8f; d <= len; d += step)
                {
                    float t = d / len;
                    s.Add(50f + d * Mathf.Cos(angle), 50f + d * Mathf.Sin(angle), Mathf.Lerp(2.2f, 0.9f, t), Mathf.Lerp(3f, 1.5f, t),
                          0.55f, accent: t > 0.85f);
                }
            }
            s.Fill((x, y) => (x - 50) * (x - 50) + (y - 50) * (y - 50) <= 36f, s.Count(10), 1.6f, 3f, 1f, accent: true);
            return s.Dots;
        }

        /// <summary>Inele: cercuri concentrice, ca pe apă (liniște); mai multe la intensitate mare.</summary>
        static List<Dot> Ring(ShapeContext s)
        {
            int rings = s.Level == 0 ? 2 : s.Level == 1 ? 3 : 4;
            for (int ring = 0; ring < rings; ring++)
            {
                float radius = 12f + ring * 10f;
                int count = Mathf.RoundToInt(radius * 1.6f);
                for (int i = 0; i < count; i++)
                {
                    float angle = 2f * Mathf.PI * i / count + s.Range(-0.05f, 0.05f);
                    float rr = radius + s.Range(-1.2f, 1.2f) * (1f + s.Style.Sharpness);
                    s.Add(50f + rr * Mathf.Cos(angle), 50f + rr * Mathf.Sin(angle), 1.2f, 2.4f, Mathf.Lerp(0.85f, 0.4f, ring / (float)rings),
                          accent: ring % 2 == 1);
                }
            }
            s.Fill((x, y) => (x - 50) * (x - 50) + (y - 50) * (y - 50) <= 25f, s.Count(6), 1.4f, 2.6f, 0.9f, accent: true);
            return s.Dots;
        }
    }
}
