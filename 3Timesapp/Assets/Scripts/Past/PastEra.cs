using System.Collections;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using ThreeTimes.AR;
using ThreeTimes.Data;
using ThreeTimes.UI;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Anul Trecutului, după direcția în care te uiți. Trecutul începe la 1900, cu Opera. Când privești un palat
    /// (cam o secundă), dacă palatul e mai nou decât anul de acum, anul se derulează până la anul lui și palatul
    /// apare „îmbrăcat în epocă” (vitrine, copertine, firme, felinare, pavaj), cu toate celelalte palate deja
    /// terminate până atunci. Apare și o notiță despre el. Unde nu era încă nimic (restul laturii de est)
    /// apare doar notița. Spre Catedrală, cele trei momente ale ei se derulează singure: temelia (1936), schelele
    /// (1938), sfințirea (1946); de la 1920 încolo fațada Operei din 1900 dispare (Opera arăta ca azi). Atingerea
    /// anului de sus te întoarce la 1900.
    ///
    /// Fiecare palat are ancora lui pe teren (pe mijlocul marginii lui), în cadrul est-sus-nord al ARCore: o
    /// eroare de orientare a telefonului strică doar puțin fiecare clădire, nu se adună pe toată piața.
    /// </summary>
    public class PastEra : MonoBehaviour
    {
        [SerializeField] PastManager past;
        [SerializeField] PastTransition transition;
        [SerializeField] MainScreenView mainView;
        [SerializeField] UIRouter router;
        [SerializeField] GeospatialTracker tracker;
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] Camera arCamera;

        [Header("Materiale")]
        [Tooltip("Materialele fațadei Operei, în ordinea din FacadeMaterial (7)")]
        [SerializeField] Material[] bodyMaterials = new Material[FacadeMesh.MaterialCount];
        [SerializeField] Material awningMaterial;
        [SerializeField] Material signMaterial;
        [SerializeField] Material pavingMaterial;
        [SerializeField] Material glowMaterial;
        [Tooltip("Țigla smălțuită a Catedralei")]
        [SerializeField] Material tilesMaterial;
        [SerializeField] Material flagMaterial;
        [Tooltip("Materialul „cerului pictat” (cel din pasul 7): ascunde Catedrala de azi în 1936 și 1938")]
        [SerializeField] Material skyMaterial;

        [Header("Privirea")]
        [SerializeField] int startYear = 1900;
        [Tooltip("Cât timp trebuie să privești un loc ca să se schimbe anul (s)")]
        [SerializeField] float dwellSeconds = 1.0f;
        [Tooltip("Cât de departe de centrul privirii poate fi locul (grade)")]
        [SerializeField] float lookAngle = 16f;
        [SerializeField] float maxDistance = 240f;
        [Tooltip("Cât durează derularea anului spre un palat (s)")]
        [SerializeField] float rollSeconds = 1.3f;
        [Tooltip("Cât se developează un palat din granulație (s)")]
        [SerializeField] float riseSeconds = 2.8f;
        [SerializeField] float noteSeconds = 7f;
        [Tooltip("Cât stă fiecare moment al Catedralei (1936, 1938) înainte de următorul (s)")]
        [SerializeField] float cathedralStepSeconds = 4.5f;

        /// <summary>Anul arătat acum în Trecut.</summary>
        public int Year { get; private set; }

        class Entry
        {
            public PalaceSpec Spec;
            public PalaceGeometry Geometry;
            public double Lat, Lon;
            public GameObject Anchor;
            public PalaceView View;
            public float ShownAt = -1f;
            public float Reveal = -1f;
            public bool NoteShown;
        }

        readonly List<Entry> entries = new List<Entry>();

        // Catedrala: trei momente (temelia 1936, schelele 1938, sfințirea 1946) și „cerul pictat” peste cea de azi.
        const int CathedralId = -1;
        GameObject cathedralAnchor, cathedralMask;
        readonly GameObject[] cathedralPhases = new GameObject[3];
        PalaceGeometry[] cathedralGeometry;
        int cathedralShown = -1;
        float cathedralReveal;
        float cathedralShownAt;
        int cathedralStage;
        float cathedralNextAt;
        bool operaNotePending;
        bool[] emptyNoteShown;
        bool active, resolving;
        float nextResolve;
        int focus = int.MinValue, handled = int.MinValue;
        float focusSince;

        void Start()
        {
            Year = startYear;
            foreach (var spec in PalaceCatalog.Palaces)
            {
                PalaceCatalog.Origin(spec, out double lat, out double lon);
                entries.Add(new Entry { Spec = spec, Geometry = PalaceBuilder.Build(spec), Lat = lat, Lon = lon });
            }
            emptyNoteShown = new bool[PalaceCatalog.Empty.Length];
            cathedralGeometry = new[]
            {
                CathedralBuilder.Build(CathedralPhase.Foundation),
                CathedralBuilder.Build(CathedralPhase.Scaffold),
                CathedralBuilder.Build(CathedralPhase.Complete),
            };
            if (mainView != null) mainView.YearTapped += BackToStart;
            if (arCamera == null) arCamera = Camera.main;
        }

        void OnDestroy()
        {
            if (mainView != null) mainView.YearTapped -= BackToStart;
        }

        void Update()
        {
            if (past == null || tracker == null) return;
            if (!tracker.Running)
            {
                Forget();
                return;
            }

            bool on = past.Active;
            if (on && !active)
            {
                // Trecerea în timp (PastTransition) derulează anul până la 1900.
                active = true;
                Year = startYear;
                if (mainView != null) mainView.SetPastEra(true);
                focus = handled = int.MinValue;
            }
            else if (!on && active)
            {
                active = false;
                if (mainView != null) mainView.SetPastEra(false);
                foreach (var e in entries) Show(e, false);
                ShowCathedral(-1);
                cathedralStage = 0;
                past.OperaHidden = false;
                for (int i = 0; i < emptyNoteShown.Length; i++) emptyNoteShown[i] = false;
                foreach (var e in entries) e.NoteShown = false;
            }
            if (!active) return;

            if (!resolving && Time.realtimeSinceStartup >= nextResolve && (cathedralAnchor == null || entries.Exists(e => e.Anchor == null)))
                StartCoroutine(ResolveAll());

            foreach (var e in entries) Show(e, e.Anchor != null && e.Spec.Year <= Year);
            CathedralSequence();
            ShowCathedral(cathedralAnchor == null ? -1 : Year >= 1946 ? 2 : Year >= 1938 ? 1 : Year >= 1936 ? 0 : -1);
            Look();
        }

        // ---------------------------------------------------------------- privirea

        void Look()
        {
            if (arCamera == null) return;
            var cam = arCamera.transform;
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) { focus = int.MinValue; return; }

            int best = int.MinValue;
            float bestAngle = lookAngle;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Anchor == null) continue;
                Consider(e.Anchor.transform.TransformPoint(e.Geometry.Focus), i, cam.position, forward, ref best, ref bestAngle);
            }
            if (cathedralAnchor != null)
                Consider(cathedralAnchor.transform.TransformPoint(cathedralGeometry[2].Focus), CathedralId, cam.position, forward, ref best, ref bestAngle);
            var reference = ReferenceAnchor();
            if (reference != null)
            {
                for (int k = 0; k < PalaceCatalog.Empty.Length; k++)
                {
                    var spot = PalaceCatalog.Empty[k];
                    var local = PalaceCatalog.ToLocal(spot.Lat, spot.Lon, reference.Lat, reference.Lon) + Vector3.up * 10f;
                    Consider(reference.Anchor.transform.TransformPoint(local), -(k + 2), cam.position, forward, ref best, ref bestAngle);
                }
            }

            if (best == int.MinValue) { focus = int.MinValue; return; }
            if (best != focus)
            {
                focus = best;
                focusSince = Time.realtimeSinceStartup;
                return;
            }
            if (handled == focus || Time.realtimeSinceStartup - focusSince < dwellSeconds) return;
            handled = focus;

            if (focus == CathedralId)
            {
                if (Year < 1946 && cathedralStage == 0) StartCathedral();
            }
            else if (focus >= 0)
            {
                var e = entries[focus];
                if (e.Spec.Year > Year) AdvanceTo(e.Spec.Year);
                if (!e.NoteShown)
                {
                    e.NoteShown = true;
                    Note(e.Spec.Name, e.Spec.Note, noteSeconds);
                }
            }
            else
            {
                int k = -focus - 2;
                var spot = PalaceCatalog.Empty[k];
                if (Year < spot.UntilYear && !emptyNoteShown[k])
                {
                    emptyNoteShown[k] = true;
                    Note("Piața Victoriei", spot.Note, noteSeconds);
                }
            }
        }

        void Consider(Vector3 target, int id, Vector3 eye, Vector3 forward, ref int best, ref float bestAngle)
        {
            var to = target - eye;
            if (to.magnitude > maxDistance) return;
            var flat = Vector3.ProjectOnPlane(to, Vector3.up);
            if (flat.sqrMagnitude < 1f) return;
            float angle = Vector3.Angle(forward, flat);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = id;
            }
        }

        Entry ReferenceAnchor()
        {
            foreach (var e in entries) if (e.Anchor != null) return e;
            return null;
        }

        void AdvanceTo(int year)
        {
            if (mainView != null) mainView.RollYear(Year, year, rollSeconds);
            TestLog.Add($"Trecut: {Year} → {year}");
            if (Year < 1920 && year >= 1920)
            {
                // După incendiul din 1920, Opera are fațada de azi: o lăsăm pe cea reală, fără suprapunere.
                past.OperaHidden = true;
                operaNotePending = true;
            }
            Year = year;
            if (transition != null) transition.Pulse(0.55f);
        }

        void BackToStart()
        {
            if (!active || Year == startYear) return;
            if (mainView != null) mainView.RollYear(Year, startYear, rollSeconds);
            Year = startYear;
            handled = int.MinValue;
            cathedralStage = 0;
            past.OperaHidden = false;
            if (transition != null) transition.Pulse(0.4f);
        }

        void Note(string title, string text, float seconds)
        {
            if (mainView != null) mainView.ShowPostcard(title, Year, text, seconds);
            else if (router != null) router.Toast(text, seconds);
        }

        // ---------------------------------------------------------------- Catedrala

        void StartCathedral()
        {
            if (Year < 1936)
            {
                AdvanceTo(1936);
                cathedralStage = 1;
                CathedralNote(CathedralData.Note1936);
                cathedralNextAt = Time.realtimeSinceStartup + cathedralStepSeconds;
            }
            else if (Year < 1938)
            {
                cathedralStage = 1;
                cathedralNextAt = Time.realtimeSinceStartup;
            }
            else
            {
                cathedralStage = 2;
                cathedralNextAt = Time.realtimeSinceStartup;
            }
        }

        // Momentele Catedralei se derulează singure, unul după altul, ca un film accelerat.
        void CathedralSequence()
        {
            if (cathedralStage == 0 || Time.realtimeSinceStartup < cathedralNextAt) return;
            if (cathedralStage == 1)
            {
                AdvanceTo(1938);
                CathedralNote(CathedralData.Note1938);
                cathedralStage = 2;
                cathedralNextAt = Time.realtimeSinceStartup + cathedralStepSeconds;
            }
            else
            {
                AdvanceTo(1946);
                cathedralStage = 0;
                CathedralNote(CathedralData.Note1946);
            }
        }

        void CathedralNote(string text)
        {
            if (operaNotePending)
            {
                text += CathedralData.NoteOpera1920;
                operaNotePending = false;
            }
            Note("Catedrala Mitropolitană", text, cathedralStage == 0 ? noteSeconds : cathedralStepSeconds);
        }

        void ShowCathedral(int phase)
        {
            if (phase != cathedralShown)
            {
                for (int i = 0; i < cathedralPhases.Length; i++)
                    if (cathedralPhases[i] != null) cathedralPhases[i].SetActive(false);
                if (phase >= 0 && cathedralPhases[phase] == null) cathedralPhases[phase] = CreateCathedral(phase);
                if (phase >= 0 && cathedralPhases[phase] != null) cathedralPhases[phase].SetActive(true);
                if (cathedralMask == null && phase >= 0 && phase < 2) cathedralMask = CreateMask();
                if (cathedralMask != null) cathedralMask.SetActive(phase == 0 || phase == 1);
                cathedralShown = phase;
                cathedralShownAt = Time.realtimeSinceStartup;
                cathedralReveal = 0f;
            }
            if (phase < 0 || cathedralPhases[phase] == null) return;

            // Fiecare moment se developează din granulație, de jos în sus; „cerul pictat” odată cu temelia.
            float t = Mathf.Clamp01((Time.realtimeSinceStartup - cathedralShownAt) / Mathf.Max(0.05f, riseSeconds));
            float eased = PastReveal.Ease(t);
            if (t < 1f || cathedralReveal < 1f)
            {
                cathedralReveal = eased;
                PastReveal.Apply(cathedralPhases[phase], eased, phase == 0 ? 3f : phase == 1 ? 40f : 80f);
                if (cathedralMask != null && phase == 0) PastReveal.Apply(cathedralMask, eased);
            }
            if (t < 1f && transition != null)
            {
                var geometry = cathedralGeometry[phase];
                var anchor = cathedralAnchor.transform;
                float y = eased * (phase == 0 ? 2f : phase == 1 ? 36f : 50f);
                for (int s = 0; s < geometry.SegmentStarts.Count; s++)
                {
                    var a = anchor.TransformPoint(geometry.SegmentStarts[s] + Vector3.up * y);
                    var b = anchor.TransformPoint(geometry.SegmentEnds[s] + Vector3.up * y);
                    transition.EmitDustAlong(a, b, Vector3.Distance(a, b) * 6f * Time.deltaTime);
                }
            }
        }

        GameObject CreateCathedral(int phase)
        {
            if (cathedralAnchor == null) return null;
            var go = new GameObject(phase == 0 ? "Catedrala 1936" : phase == 1 ? "Catedrala 1938" : "Catedrala 1946");
            go.transform.SetParent(cathedralAnchor.transform, false);
            go.SetActive(false);
            var spec = new PalaceSpec
            {
                Name = go.name,
                Wall = new Color(0.89f, 0.84f, 0.72f), Trim = new Color(0.60f, 0.30f, 0.20f),
                RoofColor = new Color(0.45f, 0.50f, 0.42f), Ornament = new Color(0.83f, 0.69f, 0.29f),
            };
            go.AddComponent<PalaceView>().Build(spec, cathedralGeometry[phase], bodyMaterials,
                new[] { awningMaterial, signMaterial, pavingMaterial, glowMaterial, tilesMaterial, flagMaterial });
            // Betonul temeliei, gri (în corpul Catedralei, „bosajele” sunt betonul).
            var renderer = go.GetComponentInChildren<MeshRenderer>(true);
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(0.62f, 0.61f, 0.58f));
                renderer.SetPropertyBlock(block, (int)FacadeMaterial.Rustic);
                block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(0.42f, 0.32f, 0.22f));
                renderer.SetPropertyBlock(block, (int)FacadeMaterial.Shadow);
            }
            return go;
        }

        GameObject CreateMask()
        {
            if (cathedralAnchor == null || skyMaterial == null) return null;
            var data = CathedralBuilder.BuildMask();
            var all = new List<int>();
            foreach (var list in data.Triangles) all.AddRange(list);
            var mesh = new Mesh { name = "Catedrala (cer pictat)" };
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetTriangles(all, 0, true);
            var go = new GameObject("Catedrala de azi, ascunsă");
            go.transform.SetParent(cathedralAnchor.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = skyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---------------------------------------------------------------- afișarea

        void Show(Entry e, bool on)
        {
            if (on && e.View == null) Create(e);
            if (e.View == null) return;
            var go = e.View.gameObject;
            if (on && !go.activeSelf)
            {
                go.SetActive(true);
                e.ShownAt = Time.realtimeSinceStartup;
            }
            else if (!on && go.activeSelf)
            {
                go.SetActive(false);
            }
            if (!on) return;

            // Se developează din granulație, de jos în sus; praful auriu urcă odată cu el.
            float t = Mathf.Clamp01((Time.realtimeSinceStartup - e.ShownAt - 0.25f) / Mathf.Max(0.05f, riseSeconds));
            float eased = PastReveal.Ease(t);
            if (eased != e.Reveal || t <= 0f)
            {
                e.Reveal = eased;
                PastReveal.Apply(go, eased, e.Spec.Height);
            }
            if (t > 0f && t < 1f && transition != null)
            {
                var anchor = e.Anchor.transform;
                float y = eased * e.Spec.Height;
                for (int s = 0; s < e.Geometry.SegmentStarts.Count; s++)
                {
                    var a = anchor.TransformPoint(e.Geometry.SegmentStarts[s] + Vector3.up * y);
                    var b = anchor.TransformPoint(e.Geometry.SegmentEnds[s] + Vector3.up * y);
                    transition.EmitDustAlong(a, b, Vector3.Distance(a, b) * 6f * Time.deltaTime);
                }
            }
        }

        void Create(Entry e)
        {
            if (e.Anchor == null) return;
            var go = new GameObject(e.Spec.Name);
            go.transform.SetParent(e.Anchor.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(false);
            var view = go.AddComponent<PalaceView>();
            view.Build(e.Spec, e.Geometry, bodyMaterials,
                new[] { awningMaterial, signMaterial, pavingMaterial, glowMaterial, tilesMaterial, flagMaterial });
            e.View = view;
        }

        // ---------------------------------------------------------------- ancorele

        IEnumerator ResolveAll()
        {
            resolving = true;
            foreach (var e in entries)
            {
                if (e.Anchor != null) continue;
                if (!tracker.Running || !tracker.Fix.HasValue) break;
                var fix = tracker.Fix.Value;
                var promise = anchorManager.ResolveAnchorOnTerrainAsync(e.Lat, e.Lon, 0, Quaternion.identity);
                yield return promise;

                GameObject result = null;
                var r = promise.State == PromiseState.Done ? promise.Result : null;
                if (r != null && r.TerrainAnchorState == TerrainAnchorState.Success && r.Anchor != null)
                {
                    result = r.Anchor.gameObject;
                }
                else
                {
                    var fallback = anchorManager.AddAnchor(e.Lat, e.Lon, fix.Altitude - 1.4, Quaternion.identity);
                    result = fallback != null ? fallback.gameObject : null;
                }
                if (!tracker.Running)
                {
                    if (result != null) Destroy(result);
                    break;
                }
                e.Anchor = result;
            }
            if (cathedralAnchor == null && tracker.Running && tracker.Fix.HasValue)
            {
                var fix = tracker.Fix.Value;
                var promise = anchorManager.ResolveAnchorOnTerrainAsync(CathedralData.Lat, CathedralData.Lon, 0, Quaternion.identity);
                yield return promise;
                var r = promise.State == PromiseState.Done ? promise.Result : null;
                GameObject result = null;
                if (r != null && r.TerrainAnchorState == TerrainAnchorState.Success && r.Anchor != null) result = r.Anchor.gameObject;
                else
                {
                    var fallback = anchorManager.AddAnchor(CathedralData.Lat, CathedralData.Lon, fix.Altitude - 1.4, Quaternion.identity);
                    result = fallback != null ? fallback.gameObject : null;
                }
                if (tracker.Running) cathedralAnchor = result;
                else if (result != null) Destroy(result);
            }
            int ok = entries.FindAll(x => x.Anchor != null).Count;
            TestLog.Add($"Palatele din 1912–1914: {ok} din {entries.Count} ancorate; Catedrala: {(cathedralAnchor != null ? "ancorată" : "neancorată")}");
            resolving = false;
            // Ce n-a mers se mai încearcă peste câteva secunde, nu la fiecare cadru.
            nextResolve = Time.realtimeSinceStartup + 5f;
        }

        void Forget()
        {
            foreach (var e in entries)
            {
                if (e.Anchor != null) Destroy(e.Anchor);
                e.Anchor = null;
                e.View = null;
                e.NoteShown = false;
            }
            if (cathedralAnchor != null) Destroy(cathedralAnchor);
            cathedralAnchor = null;
            cathedralMask = null;
            for (int i = 0; i < cathedralPhases.Length; i++) cathedralPhases[i] = null;
            cathedralShown = -1;
            cathedralStage = 0;
            operaNotePending = false;
            if (past != null) past.OperaHidden = false;
            if (mainView != null) mainView.SetPastEra(false);
            active = false;
            resolving = false;
            StopAllCoroutines();
            Year = startYear;
        }
    }
}
