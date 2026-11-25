using System;
using ThreeTimes.UI;
using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>
    /// „Trecerea în timp”: de fiecare dată când intri în Trecut, pornește proiectorul. Un bliț cald, imaginea
    /// pâlpâie tare și anul de sus se derulează de la anul de azi la 1900. Opera de azi se dizolvă în particule
    /// (o „mască de cer” peste fațada ei apare din granulație, cu praf auriu care se ridică de pe ea), apoi fațada
    /// din 1900 se developează din granulație, de jos în sus (PastManager + shaderul PastLit), cu praf pe linia care
    /// urcă. Apoi rămâne filmul vechi liniștit (PastFilm) și sunetul de proiector în surdină.
    /// </summary>
    public class PastTransition : MonoBehaviour
    {
        [SerializeField] PastManager past;
        [SerializeField] PastFilm film;
        [SerializeField] MainScreenView mainView;
        [Tooltip("Materialul prafului auriu (shader ThreeTimes/GoldDust, îl creează Tools → 3Times → 8)")]
        [SerializeField] Material dustMaterial;
        [Tooltip("Materialul „cerului pictat” (pasul 7): acoperă Opera de azi când se dizolvă")]
        [SerializeField] Material skyMaterial;

        [Header("Opera de azi se dizolvă")]
        [Tooltip("Volumul fațadei de azi, în coordonatele ancorei Operei (m): x de-a lungul fațadei, z spre piață")]
        [SerializeField] Vector3 modernMin = new Vector3(-23f, -0.5f, -3f);
        [SerializeField] Vector3 modernMax = new Vector3(23f, 34f, 5.5f);
        [SerializeField] float dissolveDelay = 0.25f;
        [SerializeField] float dissolveSeconds = 1.3f;

        [Header("Sunet (opțional)")]
        [Tooltip("Proiectorul pornește (se aude o dată, la trecere)")]
        [SerializeField] AudioClip projectorStart;
        [Tooltip("Proiectorul merge (buclă, în surdină, cât stai în Trecut)")]
        [SerializeField] AudioClip projectorLoop;
        [Range(0f, 1f)] [SerializeField] float startVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] float loopVolume = 0.12f;

        [Header("Cum arată")]
        [SerializeField] int pastYear = 1900;
        [Tooltip("Cât urcă blițul (s)")]
        [SerializeField] float flashSeconds = 0.35f;
        [Tooltip("Cât de tare e blițul (0..1)")]
        [Range(0f, 1f)] [SerializeField] float flashStrength = 0.75f;
        [Tooltip("Cât durează derularea anului (s)")]
        [SerializeField] float rollSeconds = 2.2f;
        [Tooltip("Lățimea și înălțimea fațadei modelului (m), pentru praf")]
        [SerializeField] Vector2 facadeSize = new Vector2(43f, 26f);
        [Tooltip("Câte particule de praf pe secundă, cât crește fațada")]
        [SerializeField] float dustPerSecond = 320f;

        ParticleSystem dust;
        AudioSource startSource, loopSource;
        bool wasVisible;
        float startedAt = -1f;
        float dustBudget, lineBudget;
        float pulseAt = -1f, pulseStrength;
        GameObject modernMask;
        float modernReveal = -1f, areaBudget;

        void Start()
        {
            CreateDust();
            startSource = gameObject.AddComponent<AudioSource>();
            startSource.playOnAwake = false;
            startSource.spatialBlend = 0f;
            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
            loopSource.loop = true;
            loopSource.spatialBlend = 0f;
            loopSource.volume = 0f;
        }

        void Update()
        {
            bool visible = past != null && past.Active;
            if (visible && !wasVisible) Begin();
            wasVisible = visible;

            // Sunetul de proiector în surdină cât se vede fațada.
            if (loopSource != null && loopSource.clip != null)
            {
                float target = visible ? loopVolume : 0f;
                loopSource.volume = Mathf.MoveTowards(loopSource.volume, target, Time.deltaTime * 0.5f);
                if (loopSource.volume <= 0f && loopSource.isPlaying) loopSource.Stop();
            }

            float mainFlash = 0f, mainFlicker = 0f;
            if (startedAt >= 0f)
            {
                float t = Time.realtimeSinceStartup - startedAt;
                float flash = t < flashSeconds
                    ? Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, flashSeconds))
                    : Mathf.Exp(-(t - flashSeconds) * 3.5f);
                mainFlash = flashStrength * flash;
                mainFlicker = Mathf.Clamp01(1f - t / (rollSeconds + 0.6f));
                if (visible && past.FacadeVisible) EmitDust();
                if (t > rollSeconds + 3f || !visible) startedAt = -1f;
            }

            // Un „puls” mai mic: când se schimbă anul spre un palat.
            float pulseFlash = 0f, pulseFlicker = 0f;
            if (pulseAt >= 0f)
            {
                float tp = Time.realtimeSinceStartup - pulseAt;
                pulseFlash = pulseStrength * (tp < 0.12f ? tp / 0.12f : Mathf.Exp(-(tp - 0.12f) * 4f));
                pulseFlicker = pulseStrength * Mathf.Clamp01(1f - tp / 1.3f);
                if (tp > 2f) pulseAt = -1f;
            }

            ModernOpera(visible);

            if (film != null)
            {
                film.Flash = visible ? Mathf.Max(mainFlash, pulseFlash) : 0f;
                film.Flicker = visible ? Mathf.Max(mainFlicker, pulseFlicker) : 0f;
            }
        }

        /// <summary>Un bliț mic de proiector (0..1), de exemplu când anul sare la un palat.</summary>
        public void Pulse(float strength)
        {
            pulseAt = Time.realtimeSinceStartup;
            pulseStrength = Mathf.Clamp01(strength);
            if (startSource != null && projectorStart != null) startSource.PlayOneShot(projectorStart, startVolume * 0.5f);
        }

        /// <summary>Praf auriu de-a lungul liniei a–b (în lume), cam <paramref name="count"/> fire (se adună între cadre).</summary>
        public void EmitDustAlong(Vector3 a, Vector3 b, float count)
        {
            if (dust == null) return;
            lineBudget += count;
            int n = Mathf.Min(120, Mathf.FloorToInt(lineBudget));
            if (n <= 0) return;
            lineBudget -= n;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                p.position = Vector3.Lerp(a, b, UnityEngine.Random.value) + UnityEngine.Random.insideUnitSphere * 0.4f;
                p.velocity = new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f), UnityEngine.Random.Range(0.1f, 0.6f), UnityEngine.Random.Range(-0.15f, 0.15f));
                p.startSize = UnityEngine.Random.Range(0.06f, 0.22f);
                p.startLifetime = UnityEngine.Random.Range(1.2f, 2.6f);
                p.startColor = new Color(1f, UnityEngine.Random.Range(0.72f, 0.9f), UnityEngine.Random.Range(0.35f, 0.6f),
                    UnityEngine.Random.Range(0.5f, 1f));
                dust.Emit(p, 1);
            }
        }

        // Opera de azi se dizolvă în particule: masca de cer apare din granulație peste fațada ei, cu praf care se
        // ridică de pe ea. Rămâne pusă cât se vede fațada din 1900 (acoperă și partea de sus, mai înaltă, a Operei de azi).
        void ModernOpera(bool visible)
        {
            bool show = visible && past.FacadeVisible && past.Anchor != null && skyMaterial != null;
            if (show && modernMask == null) modernMask = CreateModernMask();
            if (modernMask == null) return;
            if (modernMask.activeSelf != show) modernMask.SetActive(show);
            if (!show) return;

            float reveal = 1f;
            if (startedAt >= 0f)
                reveal = PastReveal.Ease((Time.realtimeSinceStartup - startedAt - dissolveDelay) / Mathf.Max(0.05f, dissolveSeconds));
            if (reveal != modernReveal)
            {
                PastReveal.Apply(modernMask, reveal);
                if (reveal > 0f && reveal < 1f) EmitDustArea(past.Anchor, reveal - Mathf.Max(0f, modernReveal));
                modernReveal = reveal;
            }
        }

        GameObject CreateModernMask()
        {
            var f = new FacadeMesh();
            f.Box(modernMin, modernMax, FacadeMaterial.Stone, true);
            var all = new System.Collections.Generic.List<int>();
            foreach (var list in f.Triangles) all.AddRange(list);
            var mesh = new Mesh { name = "Opera de azi (cer)" };
            mesh.SetVertices(f.Vertices);
            mesh.SetNormals(f.Normals);
            mesh.SetTriangles(all, 0, true);
            var go = new GameObject("Opera de azi, dizolvată");
            go.transform.SetParent(past.Anchor, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = skyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        /// <summary>Praf care se ridică de pe fața Operei de azi, pe măsură ce se dizolvă.</summary>
        void EmitDustArea(Transform anchor, float amount)
        {
            if (dust == null || amount <= 0f) return;
            areaBudget += amount * 1400f;
            int n = Mathf.Min(250, Mathf.FloorToInt(areaBudget));
            if (n <= 0) return;
            areaBudget -= n;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                var local = new Vector3(UnityEngine.Random.Range(modernMin.x, modernMax.x), UnityEngine.Random.Range(0f, modernMax.y * 0.9f),
                                        modernMax.z + UnityEngine.Random.Range(-0.5f, 0.3f));
                p.position = anchor.TransformPoint(local);
                p.velocity = anchor.TransformDirection(new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.4f, 1.4f), UnityEngine.Random.Range(0.2f, 1.0f)));
                p.startSize = UnityEngine.Random.Range(0.08f, 0.28f);
                p.startLifetime = UnityEngine.Random.Range(1.4f, 3.0f);
                p.startColor = new Color(1f, UnityEngine.Random.Range(0.78f, 0.95f), UnityEngine.Random.Range(0.5f, 0.75f), UnityEngine.Random.Range(0.4f, 0.9f));
                dust.Emit(p, 1);
            }
        }

        void Begin()
        {
            modernReveal = -1f;
            startedAt = Time.realtimeSinceStartup;
            dustBudget = 0f;
            if (mainView != null) mainView.RollYear(DateTime.Now.Year, pastYear, rollSeconds);
            if (startSource != null && projectorStart != null) startSource.PlayOneShot(projectorStart, startVolume);
            if (loopSource != null && projectorLoop != null && !loopSource.isPlaying)
            {
                loopSource.clip = projectorLoop;
                loopSource.volume = 0f;
                loopSource.Play();
            }
        }

        // ---------------------------------------------------------------- praful auriu

        void CreateDust()
        {
            if (dustMaterial == null)
            {
                Debug.LogWarning("[3Times] Praful auriu: lipsește materialul (Tools → 3Times → 8)");
                return;
            }
            var go = new GameObject("Praf auriu");
            go.transform.SetParent(transform, false);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            main.startSpeed = 0f;
            main.gravityModifier = -0.02f;

            var emission = dust.emission;
            emission.enabled = false;
            var shape = dust.shape;
            shape.enabled = false;

            // Fiecare fir de praf se aprinde repede și se stinge încet.
            var color = dust.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var size = dust.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));

            // Se mișcă puțin în aer, ca praful într-o rază de lumină.
            var noise = dust.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.3f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            dust.Play();
        }

        void EmitDust()
        {
            if (dust == null || past.Anchor == null) return;
            float progress = past.RiseProgress;
            float t = Time.realtimeSinceStartup - startedAt;
            // Mult praf pe „linia de țesut” cât crește fațada, apoi câteva fire care mai plutesc.
            float rate = progress < 0.999f ? dustPerSecond : (t < rollSeconds + 1.5f ? dustPerSecond * 0.12f : 0f);
            if (progress <= 0f && t < flashSeconds) rate = 0f;
            dustBudget += rate * Time.deltaTime;
            int count = Mathf.Min(200, Mathf.FloorToInt(dustBudget));
            if (count <= 0) return;
            dustBudget -= count;

            var anchor = past.Anchor;
            float s = past.Scale;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float x = UnityEngine.Random.Range(-0.5f, 0.5f) * facadeSize.x;
                float front = progress < 0.999f ? progress : UnityEngine.Random.value;
                float y = Mathf.Clamp01(front + UnityEngine.Random.Range(-0.03f, 0.03f)) * facadeSize.y;
                float z = UnityEngine.Random.Range(-0.5f, 2f);
                p.position = anchor.TransformPoint(new Vector3(x * s, y * s, z * s));
                p.velocity = anchor.TransformDirection(new Vector3(
                    UnityEngine.Random.Range(-0.15f, 0.15f), UnityEngine.Random.Range(0.1f, 0.6f), UnityEngine.Random.Range(0f, 0.4f)));
                p.startSize = UnityEngine.Random.Range(0.06f, 0.22f);
                p.startLifetime = UnityEngine.Random.Range(1.2f, 2.8f);
                p.startColor = new Color(1f, UnityEngine.Random.Range(0.72f, 0.9f), UnityEngine.Random.Range(0.35f, 0.6f),
                    UnityEngine.Random.Range(0.5f, 1f));
                dust.Emit(p, 1);
            }
        }
    }
}
