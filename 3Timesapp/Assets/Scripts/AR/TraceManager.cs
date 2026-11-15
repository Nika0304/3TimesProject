using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;
using ThreeTimes.Core;
using ThreeTimes.Data;
using ThreeTimes.Media;
using ThreeTimes.UI;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Urmele din modul Prezent. O urmă nouă se așază la 2 m în fața telefonului, la o înălțime
    /// fixă față de sol (ancoră pe teren), și se salvează local.
    ///
    /// Urmele salvate se readuc abia când localizarea e precisă și stabilă (tracker.IsPrecise),
    /// ca să nu apară în locul greșit și apoi să alunece. Apar toate urmele dintr-o rază
    /// (perimeterMeters), iar cele îndepărtate sunt mărite ca să se vadă. O urmă se poate
    /// deschide doar de aproape (openDistanceMeters); de departe, aplicația spune încotro să mergi.
    /// </summary>
    public class TraceManager : MonoBehaviour
    {
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] AREarthManager earthManager;
        [SerializeField] GeospatialTracker tracker;
        [SerializeField] MainScreenView mainView;
        [SerializeField] UIRouter router;
        [SerializeField] GameObject markerPrefab;
        [Tooltip("Materialul particulelor. Dacă lipsește, urma apare ca marcajul simplu (sfera).")]
        [SerializeField] Material particleMaterial;

        [Header("Plasare")]
        [Tooltip("La ce distanță în fața telefonului apare urma (m).")]
        [SerializeField] float distanceAhead = 2f;
        [Tooltip("La ce înălțime față de sol stă o urmă nouă (m).")]
        [SerializeField] float heightAboveTerrain = 1.2f;
        [Tooltip("Cu cât mai jos decât telefonul (m). Folosit doar pentru altitudinea de rezervă.")]
        [SerializeField] float heightBelowCamera = 0.3f;

        [Header("Zona din jur")]
        [Tooltip("Urmele din această rază (m) apar în AR, chiar dacă sunt departe.")]
        [SerializeField] float perimeterMeters = 300f;
        [Tooltip("De la ce distanță (m) se poate deschide o urmă.")]
        [SerializeField] float openDistanceMeters = 15f;
        [Tooltip("Până la această distanță (m) urma are mărimea reală; mai departe e mărită, ca să se vadă.")]
        [SerializeField] float trueSizeDistance = 10f;
        [Tooltip("De câte ori poate fi mărită cel mult o urmă îndepărtată.")]
        [SerializeField] float maxDistanceScale = 15f;
        [Tooltip("Câte urme se văd cel mult. Dacă în rază sunt mai multe, se arată cele mai apropiate.")]
        [SerializeField] int maxVisibleTraces = 20;
        [Tooltip("O urmă nouă o înlocuiește pe una afișată doar dacă e mai aproape cu acest procent (0,1 = 10%)...")]
        [SerializeField] float switchMarginRatio = 0.1f;
        [Tooltip("...și cu încă atâția metri. Așa urmele de la margine nu clipesc când mergi.")]
        [SerializeField] float switchMarginMeters = 10f;
        [Tooltip("Mărirea maximă a urmelor îndepărtate când în jur sunt mai multe decât maxVisibleTraces.")]
        [SerializeField] float crowdedMaxScale = 7f;
        [Tooltip("Cât de des (s) verificăm ce urme au intrat sau au ieșit din rază.")]
        [SerializeField] float perimeterCheckInterval = 2f;

        [Header("Apariție")]
        [Tooltip("Cât așteptăm (s) după ancorare, ca urma să se așeze, înainte să apară.")]
        [SerializeField] float settleSeconds = 0.5f;
        [Tooltip("Cât durează apariția treptată (s).")]
        [SerializeField] float fadeInSeconds = 0.8f;
        [Tooltip("După câte secunde fără precizie bună arătăm un sfat.")]
        [SerializeField] float hintAfterSeconds = 10f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>O urmă așezată acum în scenă.</summary>
        class LiveTrace
        {
            public SavedTrace trace;
            public GameObject anchor;
            public Transform root;   // se mărește cu distanța
            public Collider touch;
        }

        readonly Dictionary<string, LiveTrace> live = new Dictionary<string, LiveTrace>();
        readonly Dictionary<string, ResolveAnchorOnTerrainPromise> pending = new Dictionary<string, ResolveAnchorOnTerrainPromise>();
        readonly Dictionary<Collider, LiveTrace> byCollider = new Dictionary<Collider, LiveTrace>();
        readonly HashSet<string> failed = new HashSet<string>();
        List<SavedTrace> saved = new List<SavedTrace>();

        bool tracesVisible = true;
        bool crowded;       // în rază sunt mai multe urme decât maxVisibleTraces
        bool active;        // camera e pornită, localizarea a fost precisă, urmele din jur au fost readuse
        bool simShown;
        bool placing;
        bool hintShown;
        float runningSince = -1f;
        float nextPerimeterCheck;

        void Start()
        {
            saved = TraceStore.Load();
            mainView.TraceSubmitted += Place;
            mainView.ClearTracesRequested += ClearAll;
            mainView.WorldTapped += OnWorldTapped;
            mainView.TraceReportRequested += Report;
            mainView.TraceDeleteRequested += Delete;
            mainView.TraceSubmitCheck = PlaceBlockReason;
            mainView.SetTraceCount(saved.Count);
            Debug.Log($"[3Times] Urme salvate: {saved.Count}");
        }

        /// <summary>
        /// De ce nu se poate lăsa acum o urmă (mesaj pentru utilizator), sau null dacă se poate.
        /// Foaia „Lasă o urmă” întreabă înainte să se închidă, ca poza sau vocea să nu se piardă.
        /// </summary>
        public string PlaceBlockReason()
        {
            bool ready = tracker.Running && tracker.Fix.HasValue &&
                         (tracker.Simulated ? tracker.Quality == LocalizationQuality.Good : tracker.IsPrecise);
            if (!ready)
                return "Localizarea nu e încă destul de precisă ca urma să rămână exact aici. " +
                       "Îndreaptă telefonul spre clădiri câteva secunde și mai încearcă.";
            if (placing) return "Se așază urma de dinainte. Mai încearcă într-o clipă.";
            return null;
        }

        /// <summary>Ascunde sau arată urmele din AR (pentru poza locului fără urme).</summary>
        public void SetTracesVisible(bool visible)
        {
            tracesVisible = visible;
            foreach (var l in live.Values)
                if (l.root != null) l.root.gameObject.SetActive(visible);
        }

        void Update()
        {
            // Camera s-a oprit: sesiunea AR se închide, deci urmele dispar până data viitoare.
            if (!tracker.Running)
            {
                if (active || simShown || live.Count > 0 || pending.Count > 0)
                {
                    active = false;
                    simShown = false;
                    DestroyAll();
                }
                runningSince = -1f;
                hintShown = false;
                return;
            }
            if (runningSince < 0f) runningSince = Time.realtimeSinceStartup;

            // În simulare (Editor) nu există AR: desenăm urmele peste ilustrație.
            if (tracker.Simulated)
            {
                if (!simShown)
                {
                    simShown = true;
                    mainView.ShowSimulatedTraces(saved);
                }
                return;
            }

            if (!active)
            {
                if (tracker.IsPrecise && tracker.Fix.HasValue)
                {
                    active = true;
                    var f = tracker.Fix.Value;
                    float waited = Time.realtimeSinceStartup - runningSince;
                    TestLog.Add($"Precizie pentru urme după {waited:0.0} s · ±{f.HorizontalAccuracy:0.0} m · ±{f.YawAccuracy:0}°");
                    StartCoroutine(RestoreNearby());
                    nextPerimeterCheck = Time.realtimeSinceStartup + perimeterCheckInterval;
                }
                else if (!hintShown && saved.Count > 0 && Time.realtimeSinceStartup - runningSince > hintAfterSeconds)
                {
                    hintShown = true;
                    router.Toast("Urmele apar când localizarea e precisă. Îndreaptă telefonul spre clădiri câteva secunde.", 5f);
                }
                return;
            }

            if (Time.realtimeSinceStartup >= nextPerimeterCheck)
            {
                nextPerimeterCheck = Time.realtimeSinceStartup + perimeterCheckInterval;
                RefreshPerimeter();
            }
            UpdateDistanceScale();
        }

        // ---------------------------------------------------------------- urmele din jur

        /// <summary>
        /// Urmele care ar trebui să se vadă acum: cele din rază, de la cea mai apropiată la cea mai îndepărtată.
        /// Dacă sunt mai multe decât maxVisibleTraces, raza se restrânge practic până la a N-a urmă.
        /// </summary>
        List<(SavedTrace trace, float distance)> Candidates(GeoFix f)
        {
            var list = new List<(SavedTrace trace, float distance)>();
            foreach (var t in saved)
            {
                float d = DistanceMeters(f.Latitude, f.Longitude, t.latitude, t.longitude);
                if (d <= perimeterMeters) list.Add((t, d));
            }
            list.Sort((a, b) => a.distance.CompareTo(b.distance));
            crowded = list.Count > maxVisibleTraces;
            return list;
        }

        IEnumerator RestoreNearby()
        {
            var f = tracker.Fix.Value;
            var candidates = Candidates(f);
            int shown = Mathf.Min(candidates.Count, maxVisibleTraces);

            int ok = 0, done = 0;
            for (int i = 0; i < shown; i++)
                StartCoroutine(SpawnRoutine(candidates[i].trace, false, success => { done++; if (success) ok++; }));
            while (done < shown) yield return null;

            if (saved.Count > 0)
            {
                string msg = $"Urme readuse: {ok} din {shown}";
                if (candidates.Count > shown)
                    msg += $" (cele mai apropiate {maxVisibleTraces}, până la {candidates[shown - 1].distance:0} m)";
                else
                    msg += $" aflate în {perimeterMeters:0} m";
                int hidden = saved.Count - shown;
                if (hidden > 0) msg += $" · {hidden} nu se văd";
                Debug.Log($"[3Times] {msg}");
                TestLog.Add(msg);
            }
        }

        /// <summary>
        /// Ține pe ecran cele mai apropiate maxVisibleTraces urme din rază, pe măsură ce mergi.
        /// Setul se schimbă doar când o urmă e clar mai aproape (switchMargin), ca urmele de la margine
        /// să nu tot apară și dispară.
        /// </summary>
        void RefreshPerimeter()
        {
            if (!tracker.Fix.HasValue) return;
            var f = tracker.Fix.Value;
            var candidates = Candidates(f);

            float cutoff = candidates.Count > maxVisibleTraces
                ? candidates[maxVisibleTraces - 1].distance
                : perimeterMeters;
            float keepLimit = Mathf.Min(perimeterMeters * 1.2f, cutoff * (1f + switchMarginRatio) + switchMarginMeters);

            // Distanța urmelor de acum (pentru cele șterse între timp, infinit).
            var distanceOf = new Dictionary<string, float>();
            foreach (var t in saved)
                distanceOf[t.id] = DistanceMeters(f.Latitude, f.Longitude, t.latitude, t.longitude);
            float Dist(string id) => distanceOf.TryGetValue(id, out var d) ? d : float.MaxValue;

            // 1. Scoatem urmele rămase clar în afara setului.
            var current = new List<string>(live.Keys);
            current.AddRange(pending.Keys);
            foreach (var id in current)
                if (Dist(id) > keepLimit) RemoveLive(id);

            // Urme noi ancorăm doar cu precizie bună, ca să nu apară în locul greșit.
            if (!tracker.IsPrecise) return;

            // 2. Adăugăm, în ordinea apropierii, urmele din primele N care lipsesc.
            for (int i = 0; i < candidates.Count && i < maxVisibleTraces; i++)
            {
                var (t, d) = candidates[i];
                if (live.ContainsKey(t.id) || pending.ContainsKey(t.id) || failed.Contains(t.id)) continue;

                if (live.Count + pending.Count >= maxVisibleTraces)
                {
                    // Locuri pline: o înlocuim pe cea mai îndepărtată doar dacă noua e clar mai aproape.
                    string farthest = null;
                    float farDist = -1f;
                    foreach (var id in live.Keys)
                        if (Dist(id) > farDist) { farDist = Dist(id); farthest = id; }
                    if (farthest == null || d > farDist / (1f + switchMarginRatio) - switchMarginMeters) break;
                    RemoveLive(farthest);
                }
                StartCoroutine(SpawnRoutine(t, false, null));
            }
        }

        /// <summary>Urmele îndepărtate cresc, ca să rămână vizibile; de aproape au mărimea reală.</summary>
        void UpdateDistanceScale()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Când sunt multe urme în jur, le mărim mai puțin, ca să nu acopere ecranul.
            float maxScale = crowded ? Mathf.Min(maxDistanceScale, crowdedMaxScale) : maxDistanceScale;
            foreach (var l in live.Values)
            {
                if (l.root == null || l.anchor == null) continue;
                float d = Vector3.Distance(cam.transform.position, l.anchor.transform.position);
                float s = Mathf.Clamp(d / Mathf.Max(0.1f, trueSizeDistance), 1f, maxScale);
                l.root.localScale = Vector3.one * s;
            }
        }

        // ---------------------------------------------------------------- atingere, raportare, ștergere

        /// <summary>
        /// Atingere pe ecran: dacă nimerește o urmă destul de aproape, îi deschidem ecranul;
        /// dacă e prea departe, spunem cât și încotro să mergi.
        /// </summary>
        void OnWorldTapped(Vector2 screenPosition)
        {
            var cam = Camera.main;
            if (cam == null || byCollider.Count == 0) return;
            var ray = cam.ScreenPointToRay(new Vector3(screenPosition.x, screenPosition.y, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, perimeterMeters * 1.5f) ||
                !byCollider.TryGetValue(hit.collider, out var l) || l.anchor == null)
                return;

            Vector3 toTrace = l.anchor.transform.position - cam.transform.position;
            float distance = new Vector2(toTrace.x, toTrace.z).magnitude;
            if (distance <= openDistanceMeters)
            {
                mainView.OpenTrace(l.trace);
                return;
            }

            int shown = RoundTo(distance, distance < 50f ? 5 : 10);
            int remaining = Mathf.Max(5, RoundTo(distance - openDistanceMeters + 2.5f, 5));
            router.Toast($"Urma e la vreo {shown} m, {Direction(cam.transform, toTrace)}. " +
                         $"Mai mergi vreo {remaining} m spre ea ca s-o poți deschide.", 5f);
        }

        /// <summary>Direcția urmei față de cum ține omul telefonul: „drept înainte”, „în dreapta ta”...</summary>
        static string Direction(Transform cam, Vector3 toTrace)
        {
            Vector3 forward = cam.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                // Telefonul e ținut aproape vertical în jos: ne luăm după partea de sus a ecranului.
                forward = cam.up;
                forward.y = 0f;
            }
            toTrace.y = 0f;
            float angle = Vector3.SignedAngle(forward, toTrace, Vector3.up); // pozitiv = spre dreapta
            string side = angle > 0f ? "dreapta" : "stânga";
            float a = Mathf.Abs(angle);
            if (a <= 20f) return "drept înainte";
            if (a <= 65f) return $"înainte, spre {side}";
            if (a <= 115f) return $"în {side} ta";
            return $"în spatele tău, spre {side}";
        }

        static int RoundTo(float value, int step) => Mathf.Max(step, Mathf.RoundToInt(value / step) * step);

        void Report(SavedTrace trace)
        {
            trace.reported = true;
            TraceStore.Save(saved);
            Debug.Log($"[3Times] Urmă raportată: {trace.id}");
            router.Toast("Mulțumim. Raportarea ajunge la moderatori când conectăm serverul.", 4f);
        }

        void Delete(SavedTrace trace)
        {
            saved.Remove(trace);
            MediaStore.Delete(trace.mediaFile);
            TraceStore.Save(saved);
            RemoveLive(trace.id);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            Debug.Log($"[3Times] Urmă ștearsă: {trace.id}");
            TestLog.Add("Urmă ștearsă");
            router.Toast("Urma a fost ștearsă.");
        }

        // ---------------------------------------------------------------- plasare

        void Place(TraceDraft draft)
        {
            string blocked = PlaceBlockReason();
            if (blocked != null)
            {
                router.Toast(blocked, 4f);
                MediaStore.Delete(draft.MediaFile);
                return;
            }

            var fix = tracker.Fix.Value;
            var trace = new SavedTrace
            {
                id = Guid.NewGuid().ToString("N"),
                type = string.IsNullOrEmpty(draft.Type) ? "TEXT" : draft.Type,
                content = draft.Text ?? "",
                mediaFile = draft.MediaFile,
                photoSource = draft.PhotoSource,
                durationSeconds = draft.DurationSeconds,
                emotion = EmotionCode(draft.Emotion),
                intensity = (draft.Intensity + 1) / 3f,
                latitude = fix.Latitude,
                longitude = fix.Longitude,
                altitude = fix.Altitude - heightBelowCamera,
                heightAboveTerrain = heightAboveTerrain,
                accuracyMeters = (float)fix.HorizontalAccuracy,
                yawAccuracyDegrees = (float)fix.YawAccuracy,
                createdAt = DateTime.UtcNow.ToString("o")
            };

            if (tracker.Simulated)
            {
                Commit(trace);
                return;
            }

            // Mutăm punctul la `distanceAhead` metri în direcția în care privește camera.
            // EunRotation: X = est, Y = sus, Z = nord.
            var pose = earthManager.CameraGeospatialPose;
            Vector3 forward = pose.EunRotation * Vector3.forward;
            var flat = new Vector2(forward.x, forward.z);
            if (flat.sqrMagnitude > 0.0001f)
            {
                flat.Normalize();
                const double metersPerDegree = 111320.0;
                trace.latitude += flat.y * distanceAhead / metersPerDegree;
                trace.longitude += flat.x * distanceAhead /
                                   (metersPerDegree * Math.Cos(trace.latitude * Math.PI / 180.0));
            }

            placing = true;
            StartCoroutine(SpawnRoutine(trace, true, ok =>
            {
                placing = false;
                if (ok) Commit(trace);
                else
                {
                    MediaStore.Delete(trace.mediaFile);
                    router.Toast("Urma nu a putut fi așezată. Mai încearcă peste câteva secunde.");
                }
            }));
        }

        void Commit(SavedTrace trace)
        {
            saved.Add(trace);
            TraceStore.Save(saved);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            string kind = trace.type == "PHOTO" ? "poză, " : trace.type == "VOICE" ? "voce, " : "";
            TestLog.Add($"Urmă lăsată ({kind}{TraceLook.Name(trace.emotion)}, nivel {TraceLook.Level(trace.intensity)}) · " +
                        $"±{trace.accuracyMeters:0.0} m · ±{trace.yawAccuracyDegrees:0}° · {trace.latitude:F6}, {trace.longitude:F6}");
            Debug.Log($"[3Times] Urmă lăsată ({trace.emotion}, {trace.intensity:0.00}): {trace.latitude:F6}, {trace.longitude:F6}, " +
                      $"{trace.heightAboveTerrain:0.0} m peste sol, precizie ±{trace.accuracyMeters:0.0} m (total {saved.Count})");
            router.Toast(tracker.Simulated
                ? "Urma ta a rămas aici (simulare în Editor)."
                : $"Urma ta a rămas aici, la {distanceAhead:0} m în față.", 4f);
        }

        // ---------------------------------------------------------------- ancorare și afișare

        /// <summary>
        /// Ancorează urma pe teren (la înălțimea ei față de sol). Dacă ancora pe teren nu merge,
        /// folosește altitudinea salvată. La final urma apare treptat.
        /// </summary>
        IEnumerator SpawnRoutine(SavedTrace t, bool isNew, Action<bool> done)
        {
            if (live.ContainsKey(t.id) || pending.ContainsKey(t.id))
            {
                done?.Invoke(live.ContainsKey(t.id));
                yield break;
            }

            var promise = anchorManager.ResolveAnchorOnTerrainAsync(
                t.latitude, t.longitude, t.heightAboveTerrain, Quaternion.identity);
            pending[t.id] = promise;
            yield return promise;
            pending.Remove(t.id);

            if (promise.State == PromiseState.Cancelled)
            {
                done?.Invoke(false);
                yield break;
            }

            GameObject anchor = null;
            var result = promise.State == PromiseState.Done ? promise.Result : null;
            if (result != null && result.TerrainAnchorState == TerrainAnchorState.Success && result.Anchor != null)
            {
                anchor = result.Anchor.gameObject;
            }
            else
            {
                string why = result != null ? result.TerrainAnchorState.ToString() : promise.State.ToString();
                Debug.LogWarning($"[3Times] Ancora pe teren a eșuat ({why}) pentru urma {t.id}; folosim altitudinea salvată");
                TestLog.Add($"Ancoră pe teren eșuată ({why}), folosită altitudinea salvată");
                var fallback = anchorManager.AddAnchor(t.latitude, t.longitude, t.altitude, Quaternion.identity);
                if (fallback != null) anchor = fallback.gameObject;
            }

            if (anchor == null)
            {
                failed.Add(t.id);
                Debug.LogWarning($"[3Times] Urma {t.id} nu a putut fi ancorată");
                done?.Invoke(false);
                yield break;
            }

            // Între timp camera s-a oprit sau urma a fost ștearsă.
            if (!tracker.Running || (!isNew && !saved.Contains(t)))
            {
                Destroy(anchor);
                done?.Invoke(false);
                yield break;
            }

            BuildVisual(anchor, t);
            done?.Invoke(true);
        }

        void BuildVisual(GameObject anchor, SavedTrace t)
        {
            var root = new GameObject("Trace").transform;
            root.SetParent(anchor.transform, false);
            var appear = new GameObject("Appear").transform;
            appear.SetParent(root, false);
            appear.localScale = Vector3.zero;

            // Zona de atingere stă pe rădăcină, deci crește odată cu urma îndepărtată.
            var touch = root.gameObject.AddComponent<SphereCollider>();
            if (particleMaterial != null)
            {
                // Forma din particule: floare, picătură sau semilună, după emoție și intensitate.
                int level = TraceLook.Level(t.intensity);
                TraceShapeBuilder.Build(TraceLook.Key(t.emotion), level, t.id, particleMaterial, appear, t.type);
                touch.radius = TraceShapeBuilder.Diameter(level) * 0.6f;
            }
            else if (markerPrefab != null)
            {
                var marker = Instantiate(markerPrefab, appear, false);
                // Mărimea crește cu intensitatea, culoarea vine din emoție.
                float size = Mathf.Lerp(0.7f, 1.3f, t.intensity);
                marker.transform.localScale = Vector3.one * size;
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColor, EmotionColor(t.emotion));
                foreach (var r in marker.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(block);
                touch.radius = 0.35f * size;
            }

            root.gameObject.SetActive(tracesVisible);
            var l = new LiveTrace { trace = t, anchor = anchor, root = root, touch = touch };
            live[t.id] = l;
            byCollider[touch] = l;
            StartCoroutine(FadeIn(appear));
        }

        /// <summary>Urma așteaptă puțin să se așeze, apoi crește lin din nimic.</summary>
        IEnumerator FadeIn(Transform appear)
        {
            yield return new WaitForSeconds(settleSeconds);
            float elapsed = 0f;
            while (elapsed < fadeInSeconds)
            {
                if (appear == null) yield break;
                elapsed += Time.deltaTime;
                appear.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, elapsed / fadeInSeconds);
                yield return null;
            }
            if (appear != null) appear.localScale = Vector3.one;
        }

        void RemoveLive(string id)
        {
            if (pending.TryGetValue(id, out var promise))
            {
                promise.Cancel();
                pending.Remove(id);
            }
            if (live.TryGetValue(id, out var l))
            {
                if (l.touch != null) byCollider.Remove(l.touch);
                if (l.anchor != null) Destroy(l.anchor);
                live.Remove(id);
            }
        }

        void DestroyAll()
        {
            StopAllCoroutines();
            foreach (var promise in pending.Values) promise.Cancel();
            pending.Clear();
            foreach (var l in live.Values)
                if (l.anchor != null) Destroy(l.anchor);
            live.Clear();
            byCollider.Clear();
            failed.Clear();
            placing = false;
            tracesVisible = true;
        }

        /// <summary>Distanța aproximativă pe sol între două puncte (m); destul de exactă pentru câțiva km.</summary>
        static float DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadius = 6371000.0;
            double toRad = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * toRad;
            double dLon = (lon2 - lon1) * toRad * Math.Cos((lat1 + lat2) * 0.5 * toRad);
            return (float)(earthRadius * Math.Sqrt(dLat * dLat + dLon * dLon));
        }

        static string EmotionCode(Emotion e) =>
            e == Emotion.Sadness ? "SADNESS" : e == Emotion.Longing ? "LONGING" : "JOY";

        static Color EmotionColor(string code)
        {
            switch (code)
            {
                case "SADNESS": return new Color(0.357f, 0.584f, 0.902f); // #5B95E6
                case "LONGING": return new Color(0.612f, 0.471f, 0.890f); // #9C78E3
                default: return new Color(0.961f, 0.722f, 0.239f);        // #F5B83D
            }
        }

        // ---------------------------------------------------------------- previzualizare (doar Editor)

        /// <summary>
        /// În Play, în Editor: arată cele nouă forme (3 emoții × 3 intensități) și, dedesubt, cele trei tipuri
        /// (text, poză, voce) în fața unei camere,
        /// fără interfață, ca să le putem verifica fără telefon. Meniu: Tools → 3Times → Arată formele 3D.
        /// </summary>
        public void PreviewShapes()
        {
            if (particleMaterial == null)
            {
                Debug.LogWarning("[3Times] Lipsește materialul particulelor: rulează Tools → 3Times → 4.");
                return;
            }
            router.Root.style.display = DisplayStyle.None;

            var cam = new GameObject("Preview Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.83f, 0.90f, 0.97f); // cer deschis, ca în ilustrație
            cam.depth = 10;
            // Încadrăm toată grila (aprox. 3,8 m lățime, 5,4 m înălțime), și pe ecran de telefon, vertical.
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(2.75f / tanHalf, 1.95f / (tanHalf * Mathf.Max(0.1f, cam.aspect)));
            cam.transform.position = new Vector3(0f, 0f, 4.6f - distance);

            string[] keys = { "joy", "sadness", "longing" };
            for (int e = 0; e < 3; e++)
                for (int level = 0; level < 3; level++)
                {
                    var holder = new GameObject($"Preview {keys[e]} {level}");
                    holder.transform.position = new Vector3((level - 1) * 1.1f, 1.6f - e * 1.05f, 4.6f);
                    TraceShapeBuilder.Build(keys[e], level, $"preview-{e}-{level}", particleMaterial, holder.transform);
                }

            // Ultimul rând: cele trei tipuri de urmă (text, poză, voce), la intensitatea „mediu”.
            string[] types = { "TEXT", "PHOTO", "VOICE" };
            for (int i = 0; i < 3; i++)
            {
                var holder = new GameObject($"Preview {types[i]}");
                holder.transform.position = new Vector3((i - 1) * 1.4f, -1.75f, 4.6f);
                TraceShapeBuilder.Build(keys[i], 1, $"preview-type-{i}", particleMaterial, holder.transform, types[i]);
            }
        }

        // ---------------------------------------------------------------- ștergere

        void ClearAll()
        {
            int count = saved.Count;
            foreach (var t in saved) MediaStore.Delete(t.mediaFile);
            saved.Clear();
            TraceStore.Save(saved);
            DestroyAll();
            mainView.SetTraceCount(0);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            Debug.Log($"[3Times] Urme șterse: {count}");
            if (count > 0) TestLog.Add($"Toate urmele de test șterse ({count})");
            router.Toast(count == 0 ? "Nu ai urme de test." : $"Am șters {count} urme de test.");
        }
    }
}
