using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Construiește forma din particule a unei urme: floare (bucurie), picătură (tristețe),
    /// semilună (dor). Punctele se așază puțin diferit la fiecare urmă (după id-ul ei),
    /// iar forma crește și se îmbogățește cu intensitatea. Aceleași reguli ca în machetă.
    /// </summary>
    public static class TraceShapeBuilder
    {
        struct Dot
        {
            public Vector3 Position; // în unități de machetă: 0–100, centrul în (50, 50)
            public float Radius;
            public Color Color;
        }

        static readonly Color JoyPetal = new Color(0.961f, 0.722f, 0.239f);   // #F5B83D
        static readonly Color JoyCore = new Color(0.910f, 0.588f, 0.102f);    // #E8961A
        static readonly Color Sadness = new Color(0.357f, 0.584f, 0.902f);    // #5B95E6
        static readonly Color Longing = new Color(0.612f, 0.471f, 0.890f);    // #9C78E3
        static readonly Color LongingStar = new Color(0.788f, 0.706f, 1f);    // #C9B4FF

        /// <summary>Diametrul formei în metri, pentru fiecare nivel de intensitate.</summary>
        public static float Diameter(int level) => level == 0 ? 0.45f : level == 1 ? 0.62f : 0.82f;

        /// <param name="emotionKey">"joy", "sadness" sau "longing"</param>
        /// <param name="level">0 = încet, 1 = mediu, 2 = puternic</param>
        /// <param name="traceType">
        /// TEXT, PHOTO sau VOICE. Textul e doar forma emoției; la poză forma stă pe un polaroid,
        /// la voce pe un microfon (amândouă albe, puțin transparente, ca un fundal).
        /// </param>
        public static GameObject Build(string emotionKey, int level, string seedText, Material material, Transform parent,
                                       string traceType = "TEXT")
        {
            int seed = 17;
            foreach (char c in seedText ?? "") seed = unchecked(seed * 31 + c);
            var random = new System.Random(seed);

            List<Dot> dots =
                emotionKey == "sadness" ? Drop(level, random) :
                emotionKey == "longing" ? Crescent(level, random) :
                Flower(level, random);

            float unit = Diameter(level) / 100f; // metri pe unitate de machetă
            var go = CreateSystem("Shape", parent, material, out var system);
            foreach (var dot in dots)
                Emit(system, new Vector3((dot.Position.x - 50f) * unit, (50f - dot.Position.y) * unit, dot.Position.z * unit),
                     dot.Radius * 2f * unit * 1.5f, dot.Color); // textura e estompată spre margini

            // Tipul urmei: în spatele formei, un polaroid (poză) sau un microfon (voce), alb, puțin transparent.
            if (traceType == "PHOTO" || traceType == "VOICE")
            {
                Color tint = emotionKey == "sadness" ? Sadness : emotionKey == "longing" ? Longing : JoyCore;
                var backdrop = TraceBackdropMesh.Create(traceType, Diameter(level), tint, material, go.transform);
                if (traceType == "VOICE") backdrop.AddComponent<TraceTypePulse>();
            }

            var animator = go.AddComponent<TraceShapeAnimator>();
            animator.Setup(emotionKey, level, seed);
            return go;
        }

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
            main.startLifetime = 1000000f; // particulele rămân pe loc; mișcarea vine din animatoare
            main.maxParticles = 400;
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

        // ---------------------------------------------------------------- formele

        static float Range(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);

        static Dot Make(System.Random r, float x, float y, float minRadius, float maxRadius, float minAlpha, Color color)
        {
            color.a = Range(r, minAlpha, 1f);
            return new Dot { Position = new Vector3(x, y, Range(r, -4f, 4f)), Radius = Range(r, minRadius, maxRadius), Color = color };
        }

        /// <summary>Bucurie: petale în cerc (5, 6 sau 8), miez mai închis, iar la „puternic” o aură de puncte mici.</summary>
        static List<Dot> Flower(int level, System.Random r)
        {
            int petals = level == 0 ? 5 : level == 1 ? 6 : 8;
            int perPetal = level == 0 ? 7 : level == 1 ? 9 : 11;
            float distance = level == 0 ? 19f : level == 1 ? 23f : 27f;
            var dots = new List<Dot>();

            for (int i = 0; i < petals; i++)
            {
                float angle = 2f * Mathf.PI * i / petals - Mathf.PI / 2f;
                float cx = 50f + distance * Mathf.Cos(angle);
                float cy = 50f + distance * Mathf.Sin(angle);
                for (int k = 0; k < perPetal; k++)
                {
                    float rr = Range(r, 0f, 9f), t = Range(r, 0f, 2f * Mathf.PI);
                    dots.Add(Make(r, cx + rr * Mathf.Cos(t) * 0.8f, cy + rr * Mathf.Sin(t), 1.6f, 3.6f, 0.65f, JoyPetal));
                }
            }

            int core = level == 0 ? 8 : level == 1 ? 12 : 16;
            for (int k = 0; k < core; k++)
            {
                float rr = Range(r, 0f, 8f), t = Range(r, 0f, 2f * Mathf.PI);
                dots.Add(Make(r, 50f + rr * Mathf.Cos(t), 50f + rr * Mathf.Sin(t), 2f, 4f, 1f, JoyCore));
            }

            if (level == 2)
                for (int k = 0; k < 14; k++)
                {
                    float rr = Range(r, 40f, 47f), t = Range(r, 0f, 2f * Mathf.PI);
                    dots.Add(Make(r, 50f + rr * Mathf.Cos(t), 50f + rr * Mathf.Sin(t), 0.8f, 1.6f, 0.3f, JoyPetal));
                }
            return dots;
        }

        /// <summary>Tristețe: o picătură care se alungește cu intensitatea; de la „mediu” lasă stropi sub ea.</summary>
        static List<Dot> Drop(int level, System.Random r)
        {
            float height = level == 0 ? 24f : level == 1 ? 34f : 40f;
            int count = level == 0 ? 55 : level == 1 ? 80 : 100;
            const float centerY = 62f, radius = 21f;
            float top = 50f - height;
            var dots = new List<Dot>();

            int guard = 0;
            while (dots.Count < count && guard++ < 20000)
            {
                float x = Range(r, 25f, 75f), y = Range(r, 5f, 85f);
                bool inside;
                if (y < centerY)
                    inside = y >= top && Mathf.Abs(x - 50f) <= (y - top) / (centerY - top) * radius;
                else
                    inside = (x - 50f) * (x - 50f) + (y - centerY) * (y - centerY) <= radius * radius;
                if (inside) dots.Add(Make(r, x, y, 1.2f, 3f, 0.55f, Sadness));
            }

            int trail = level == 0 ? 0 : level == 1 ? 4 : 8;
            for (int k = 0; k < trail; k++)
                dots.Add(Make(r, 50f + Range(r, -5f, 5f), 88f + k * 1.6f + Range(r, 0f, 2f), 1f, 2.2f, 0.4f, Sadness));
            return dots;
        }

        /// <summary>Dor: o semilună care se subțiază cu intensitatea; la „puternic” apar câteva stele.</summary>
        static List<Dot> Crescent(int level, System.Random r)
        {
            float outer = level == 0 ? 28f : level == 1 ? 33f : 38f;
            float inner = level == 0 ? 24f : level == 1 ? 28f : 33f;
            float offset = level == 0 ? 14f : 15f;
            int count = level == 0 ? 55 : level == 1 ? 80 : 105;
            var dots = new List<Dot>();

            int guard = 0;
            while (dots.Count < count && guard++ < 40000)
            {
                float x = Range(r, 5f, 95f), y = Range(r, 5f, 95f);
                float dx = x - 50f, dy = y - 50f;
                float ix = dx - offset, iy = dy + offset * 0.6f;
                if (dx * dx + dy * dy <= outer * outer && ix * ix + iy * iy > inner * inner)
                    dots.Add(Make(r, x, y, 1.2f, 3f, 0.55f, Longing));
            }

            if (level == 2)
            {
                dots.Add(Make(r, 78f, 20f, 1.8f, 1.8f, 0.9f, LongingStar));
                dots.Add(Make(r, 86f, 32f, 1.8f, 1.8f, 0.9f, LongingStar));
                dots.Add(Make(r, 70f, 12f, 1.8f, 1.8f, 0.9f, LongingStar));
            }
            return dots;
        }
    }
}
