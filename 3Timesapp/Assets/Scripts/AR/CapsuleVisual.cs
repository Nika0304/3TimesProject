using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ThreeTimes.Data;
using ThreeTimes.UI;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Capsula timpului în AR: o sferă de sticlă (puncte albe pe suprafață, aproape transparente) cu o lumină înăuntru.
    /// <list type="bullet">
    /// <item>se umple (mai primește urme): sfera e deschisă sus, iar fiecare urmă pusă e o scânteie care se rotește înăuntru;</item>
    /// <item>sigilată: sfera e întreagă, lumina mentă pulsează încet;</item>
    /// <item>data a venit: lumina devine aurie și apare un inel auriu (capsula „te așteaptă”);</item>
    /// <item>la deschidere sticla se sparge, iar urmele ies din ea și se așază în cerc (BuildOpened).</item>
    /// </list>
    /// Totul e din particule, cu același material ca urmele.
    /// </summary>
    public class CapsuleVisual : MonoBehaviour
    {
        const float Radius = 0.26f;
        static readonly Color Glass = new Color(1f, 1f, 1f, 0.38f);
        static readonly Color GlassLow = new Color(0.82f, 0.95f, 0.90f, 0.32f);
        static readonly Color Mint = new Color(0.50f, 0.83f, 0.72f, 0.85f);
        static readonly Color Gold = new Color(0.97f, 0.78f, 0.33f, 0.9f);
        static readonly Color GoldRing = new Color(0.88f, 0.62f, 0.16f, 0.9f);

        /// <summary>Atingerea se face pe o sferă puțin mai mare decât sticla.</summary>
        public const float TouchRadius = 0.36f;
        /// <summary>Raza cercului în care se așază urmele după deschidere (m).</summary>
        public const float ItemsRadius = 0.85f;

        SavedTrace capsule;
        Material material;
        Transform content, shell, core, motes, facing;
        ParticleSystem shellSystem;
        GameObject ring, highlight;
        bool ready, collecting, shattering;
        int shownItems;
        float nextCheck, seedPhase;

        public static CapsuleVisual Build(SavedTrace capsule, Material material, Transform parent)
        {
            var go = new GameObject("Capsule");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CapsuleVisual>();
            v.capsule = capsule;
            v.material = material;
            v.seedPhase = (TraceShapeBuilder.Seed(capsule.id) & 1023) / 163f;
            v.Rebuild();
            return v;
        }

        /// <summary>Refăcută când se schimbă starea (s-a sigilat, s-a mai pus o urmă, a venit data).</summary>
        void Rebuild()
        {
            if (content != null) Destroy(content.gameObject);
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            ready = CapsuleText.IsReady(capsule);
            collecting = capsule.collecting && !ready;
            shownItems = capsule.itemCount;
            var rnd = new System.Random(TraceShapeBuilder.Seed(capsule.id));
            Color light = ready ? Gold : Mint;

            // Sticla: puncte pe suprafața sferei (spirala Fibonacci); cât se umple, fără „capac”.
            shell = TraceShapeBuilder.CreateSystem("Glass", content, material, out shellSystem).transform;
            const int count = 150;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / count;
                if (collecting && y > 0.72f) continue;
                float r = Mathf.Sqrt(1f - y * y);
                float a = golden * i;
                var p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) * Radius;
                TraceShapeBuilder.Emit(shellSystem, p, 0.018f + (float)rnd.NextDouble() * 0.012f, y < -0.3f ? GlassLow : Glass);
            }

            // Lumina: un miez alb și un nor de puncte (mentă sau auriu), cu câteva scântei.
            core = TraceShapeBuilder.CreateSystem("Light", content, material, out var coreSystem).transform;
            TraceShapeBuilder.Emit(coreSystem, Vector3.zero, ready ? 0.12f : 0.10f, Color.white);
            for (int i = 0; i < 40; i++)
            {
                var dir = UnityEngine.Random.onUnitSphere;
                float dist = Mathf.Abs(Gauss(rnd)) * (ready ? 0.075f : 0.06f);
                TraceShapeBuilder.Emit(coreSystem, dir * dist, 0.03f + (float)rnd.NextDouble() * 0.03f, light);
            }

            // Urmele puse până acum (cât se umple): câte o scânteie care se rotește înăuntru.
            motes = TraceShapeBuilder.CreateSystem("Items", content, material, out var moteSystem).transform;
            if (collecting)
            {
                for (int i = 0; i < Mathf.Min(shownItems, 10); i++)
                {
                    float a = i / (float)Mathf.Max(1, shownItems) * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * 0.15f, ((i % 3) - 1) * 0.05f, Mathf.Sin(a) * 0.15f);
                    TraceShapeBuilder.Emit(moteSystem, p, 0.035f, new Color(1f, 1f, 1f, 0.95f));
                }
            }

            // Ce privește spre cameră: reflexia (un arc sus-stânga) și inelul auriu (data a venit).
            facing = new GameObject("Facing").transform;
            facing.SetParent(content, false);
            highlight = TraceShapeBuilder.CreateSystem("Highlight", facing, material, out var hl);
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.Lerp(105f, 165f, i / 13f) * Mathf.Deg2Rad;
                TraceShapeBuilder.Emit(hl, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * Radius * 0.78f + Vector3.back * 0.01f,
                                       0.026f, new Color(1f, 1f, 1f, 0.9f));
            }
            ring = TraceShapeBuilder.CreateSystem("Ring", facing, material, out var rs);
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                TraceShapeBuilder.Emit(rs, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (Radius + 0.08f), 0.017f, GoldRing);
            }
            ring.SetActive(ready);
        }

        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        void Update()
        {
            if (shattering || content == null) return;
            float t = Time.time + seedPhase;

            // Plutește puțin, sticla se rotește încet, scânteile urmelor se rotesc înăuntru.
            content.localPosition = new Vector3(0f, Mathf.Sin(t * 0.9f) * 0.03f, 0f);
            shell.Rotate(0f, 6f * Time.deltaTime, 0f, Space.Self);
            motes.Rotate(0f, 35f * Time.deltaTime, 0f, Space.Self);

            // Lumina pulsează: încet cât e închisă, mai tare când se poate deschide.
            float amp = ready ? 0.2f : 0.08f, speed = ready ? 3.4f : 1.6f;
            core.localScale = Vector3.one * (1f + amp * Mathf.Sin(t * speed));

            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = facing.position - cam.transform.position;
                if (away.sqrMagnitude > 0.0001f) facing.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
            if (ready) ring.transform.Rotate(0f, 0f, 25f * Time.deltaTime, Space.Self);

            // Starea se poate schimba cât stăm lângă ea (a venit data, s-a sigilat, s-a mai pus o urmă).
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 1f;
                bool nowReady = CapsuleText.IsReady(capsule);
                bool nowCollecting = capsule.collecting && !nowReady;
                if (nowReady != ready || nowCollecting != collecting || (collecting && capsule.itemCount != shownItems))
                    Rebuild();
            }
        }

        /// <summary>
        /// Sticla se sparge: cioburile zboară în afară și cad încet, lumina se aprinde puternic.
        /// halfway e chemat când lumina e la maxim (atunci ies urmele); la final se distruge destroyWhenDone.
        /// </summary>
        public void Shatter(Action halfway, GameObject destroyWhenDone)
        {
            if (shattering) return;
            shattering = true;
            StartCoroutine(ShatterRoutine(halfway, destroyWhenDone));
        }

        IEnumerator ShatterRoutine(Action halfway, GameObject destroyWhenDone)
        {
            ring.SetActive(false);
            highlight.SetActive(false);
            shellSystem.Clear();
            motes.gameObject.SetActive(false);

            TraceShapeBuilder.CreateSystem("Shards", content, material, out var shards);
            var main = shards.main;
            main.gravityModifier = 0.25f;
            var fade = shards.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var rnd = new System.Random(TraceShapeBuilder.Seed(capsule.id) + 1);
            for (int i = 0; i < 80; i++)
            {
                var dir = UnityEngine.Random.onUnitSphere;
                float speed = 0.5f + (float)rnd.NextDouble() * 0.9f;
                shards.Emit(new ParticleSystem.EmitParams
                {
                    position = dir * Radius,
                    velocity = dir * speed + Vector3.up * 0.25f,
                    startSize = 0.02f + (float)rnd.NextDouble() * 0.03f,
                    startColor = i % 4 == 0 ? Gold : new Color(1f, 1f, 1f, 0.9f),
                    startLifetime = 1.0f + (float)rnd.NextDouble() * 0.7f
                }, 1);
            }

            float elapsed = 0f;
            bool shown = false;
            while (elapsed < 1.8f)
            {
                elapsed += Time.deltaTime;
                float k = elapsed < 0.35f ? Mathf.SmoothStep(1f, 2.2f, elapsed / 0.35f)
                                          : Mathf.SmoothStep(2.2f, 0f, (elapsed - 0.35f) / 0.6f);
                core.localScale = Vector3.one * Mathf.Max(0f, k);
                if (!shown && elapsed >= 0.35f)
                {
                    shown = true;
                    halfway?.Invoke();
                }
                yield return null;
            }
            if (destroyWhenDone != null) Destroy(destroyWhenDone);
            else Destroy(gameObject);
        }

        // ---------------------------------------------------------------- după deschidere

        /// <summary>
        /// Capsula deschisă: urmele din ea, fiecare cu forma emoției, așezate în cerc în jurul locului, plus câteva
        /// cioburi de sticlă. emerge = ies din centru (chiar acum s-a deschis). Întoarce zonele de atingere ale urmelor.
        /// </summary>
        public static List<(Collider touch, SavedTrace item)> BuildOpened(SavedTrace capsule, Material material,
                                                                         Transform parent, bool emerge)
        {
            var touches = new List<(Collider, SavedTrace)>();
            var items = capsule.items ?? new List<SavedTrace>();
            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                var item = items[i];
                var holder = new GameObject("Capsule item").transform;
                holder.SetParent(parent, false);
                Vector3 target = n == 1 ? Vector3.zero
                    : new Vector3(Mathf.Cos(i * Mathf.PI * 2f / n) * ItemsRadius, (i % 2) * 0.12f,
                                  Mathf.Sin(i * Mathf.PI * 2f / n) * ItemsRadius);
                holder.localPosition = emerge ? Vector3.zero : target;

                var style = TraceStyle.Of(item);
                TraceShapeBuilder.Build(style, item.id, material, holder, item.type);
                var touch = holder.gameObject.AddComponent<SphereCollider>();
                touch.radius = TraceShapeBuilder.Diameter(style.Level) * 0.6f;
                touches.Add((touch, item));

                if (emerge)
                {
                    var move = holder.gameObject.AddComponent<CapsuleItemEmerge>();
                    move.Target = target;
                    move.Delay = i * 0.12f;
                }
            }
            AddShards(capsule.id, material, parent);
            if (capsule.IsYearCapsule) AddEmotionRing(capsule, material, parent);
            return touches;
        }

        /// <summary>
        /// Capsula anului deschisă: un inel mare de puncte, în culorile emoțiilor anului, fiecare emoție cu atâtea
        /// puncte câte urme a avut (proporțional) — „cum s-a simțit orașul”.
        /// </summary>
        static void AddEmotionRing(SavedTrace capsule, Material material, Transform parent)
        {
            var emotions = capsule.yearEmotions;
            if (emotions == null || emotions.Count == 0) return;
            int total = 0;
            foreach (var e in emotions) total += Mathf.Max(0, e.count);
            if (total == 0) return;

            var go = TraceShapeBuilder.CreateSystem("Year Emotions", parent, material, out var system);
            go.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            const int dots = 72;
            int index = 0;
            foreach (var e in emotions)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(dots * e.count / (float)total));
                var color = TraceStyle.ParseColor(e.color, TraceStyle.Of(TraceStyle.FromWord(e.emotion ?? "")).Color);
                for (int i = 0; i < n && index < dots + emotions.Count; i++, index++)
                {
                    float a = index / (float)dots * Mathf.PI * 2f;
                    TraceShapeBuilder.Emit(system, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f, 0.045f, color);
                }
            }
            go.AddComponent<CapsuleShardsOrbit>();
        }

        /// <summary>Câteva cioburi de sticlă care plutesc încet în jurul locului capsulei deschise.</summary>
        public static GameObject AddShards(string seedText, Material material, Transform parent)
        {
            var go = TraceShapeBuilder.CreateSystem("Capsule Shards", parent, material, out var system);
            var rnd = new System.Random(TraceShapeBuilder.Seed(seedText));
            for (int i = 0; i < 11; i++)
            {
                float a = i / 11f * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.4f;
                float r = 0.3f + (float)rnd.NextDouble() * 0.15f;
                var p = new Vector3(Mathf.Cos(a) * r, ((float)rnd.NextDouble() - 0.5f) * 0.3f, Mathf.Sin(a) * r);
                TraceShapeBuilder.Emit(system, p, 0.02f + (float)rnd.NextDouble() * 0.018f,
                                       i % 3 == 0 ? new Color(0.97f, 0.86f, 0.55f, 0.8f) : new Color(1f, 1f, 1f, 0.75f));
            }
            go.AddComponent<CapsuleShardsOrbit>();
            return go;
        }
    }

    /// <summary>Cioburile rămase după deschidere se rotesc încet în jurul locului.</summary>
    public class CapsuleShardsOrbit : MonoBehaviour
    {
        void Update() => transform.Rotate(0f, 10f * Time.deltaTime, 0f, Space.Self);
    }

    /// <summary>O urmă iese din capsula deschisă și se duce lin la locul ei din cerc.</summary>
    public class CapsuleItemEmerge : MonoBehaviour
    {
        public Vector3 Target;
        public float Delay;
        float elapsed;

        void Update()
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01((elapsed - Delay) / 1.2f);
            transform.localPosition = Vector3.Lerp(Vector3.zero, Target, Mathf.SmoothStep(0f, 1f, k));
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, k);
            if (k >= 1f) Destroy(this);
        }
    }
}
