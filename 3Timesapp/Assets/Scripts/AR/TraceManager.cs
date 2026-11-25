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

        [Header("Server")]
        [Tooltip("Cât de des (s) cerem de la server urmele din jur.")]
        [SerializeField] float fetchInterval = 30f;
        [Tooltip("Cerem din nou mai devreme dacă te-ai deplasat cu atâția metri.")]
        [SerializeField] float refetchDistance = 75f;
        [Tooltip("Câte urme cerem cel mult de la server (din ele se arată cele mai apropiate maxVisibleTraces).")]
        [SerializeField] int fetchLimit = 100;
        [Tooltip("Cât așteptăm (s) prima listă de la server înainte să readucem urmele oricum.")]
        [SerializeField] float firstFetchTimeout = 6f;

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
            public CapsuleVisual capsuleVisual; // doar la capsulele încă sigilate
            public List<Collider> extraTouches;  // urmele dintr-o capsulă deschisă (fiecare se atinge separat)
        }

        readonly Dictionary<string, LiveTrace> live = new Dictionary<string, LiveTrace>();
        readonly Dictionary<string, ResolveAnchorOnTerrainPromise> pending = new Dictionary<string, ResolveAnchorOnTerrainPromise>();
        readonly Dictionary<Collider, LiveTrace> byCollider = new Dictionary<Collider, LiveTrace>();
        readonly HashSet<string> failed = new HashSet<string>();
        List<SavedTrace> saved = new List<SavedTrace>();                                  // urmele mele, salvate pe telefon
        readonly Dictionary<string, SavedTrace> remote = new Dictionary<string, SavedTrace>(); // urmele din jur, de la server
        readonly Dictionary<string, SavedTrace> capsules = new Dictionary<string, SavedTrace>(); // capsulele din jur, de la server
        bool opening, creatingCapsule;
        readonly HashSet<string> emergeNext = new HashSet<string>();
        readonly HashSet<string> justOpened = new HashSet<string>(); // deschise acum: rămân în Viitor până se schimbă modul // capsule chiar acum deschise: urmele ies din centru

        bool fetching, fetchedOnce, serverWarned;
        float nextFetch;
        double lastFetchLat, lastFetchLng;

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
            mainView.EnsureMedia = EnsureMedia;
            mainView.MapTraces = TracesForMap;
            mainView.MapMyTraces = MyTracesForMap;
            mainView.AccountDeleted += ForgetMyTraces;
            mainView.VisibleTraces = VisibleTraces;
            mainView.CapsuleSubmitted += PlaceCapsule;
            mainView.CapsuleSubmitCheck = CapsuleBlockReason;
            mainView.OpenCapsule = OpenCapsule;
            mainView.ModeChanged += OnModeChanged;
            mainView.SetTraceCount(saved.Count);
            AccountSession.Changed += OnAccountChanged;
            TestClock.Changed += OnTestClockChanged;
            Debug.Log($"[3Times] Urme salvate: {saved.Count}");
        }

        void OnDestroy()
        {
            AccountSession.Changed -= OnAccountChanged;
            TestClock.Changed -= OnTestClockChanged;
        }

        /// <summary>
        /// S-a deschis sau s-a închis contul: cerem din nou urmele din jur, ca serverul să spună din nou care sunt ale mele
        /// (cu cont, și cele lăsate de pe alte telefoane).
        /// </summary>
        /// <summary>S-a mutat ceasul de test: cerem din nou urmele și capsulele și refacem ce se vede.</summary>
        void OnTestClockChanged()
        {
            nextFetch = 0f;
            // Capsulele anului vin din nou de la server (cu ora mutată): altă stare, alt loc.
            foreach (var c in new List<SavedTrace>(capsules.Values))
            {
                if (!c.IsYearCapsule) continue;
                capsules.Remove(c.id);
                RemoveLive(c.id);
            }
            OnModeChanged(mainView.Mode);
        }

        void OnAccountChanged()
        {
            nextFetch = 0f;
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
                fetchedOnce = false;
                serverWarned = false;
                return;
            }
            if (runningSince < 0f) runningSince = Time.realtimeSinceStartup;

            // În simulare (Editor) nu există AR: desenăm urmele peste ilustrație.
            if (tracker.Simulated)
            {
                if (!simShown)
                {
                    simShown = true;
                    mainView.ShowSimulatedTraces(ShownForSim());
                }
                MaybeFetch();
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
                    StartCoroutine(RestoreAfterFirstFetch());
                    nextPerimeterCheck = Time.realtimeSinceStartup + perimeterCheckInterval;
                }
                else if (!hintShown && saved.Count + remote.Count > 0 && Time.realtimeSinceStartup - runningSince > hintAfterSeconds)
                {
                    hintShown = true;
                    router.Toast("Urmele apar când localizarea e precisă. Îndreaptă telefonul spre clădiri câteva secunde.", 5f);
                }
                return;
            }

            MaybeFetch();
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
            foreach (var t in Shown())
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

            int total = saved.Count + remote.Count;
            if (total > 0)
            {
                string msg = $"Urme readuse: {ok} din {shown}";
                if (candidates.Count > shown)
                    msg += $" (cele mai apropiate {maxVisibleTraces}, până la {candidates[shown - 1].distance:0} m)";
                else
                    msg += $" aflate în {perimeterMeters:0} m";
                int hidden = Shown().Count - shown;
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
            foreach (var t in Shown())
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
            float reach = l.trace.IsYearCapsule ? 25f : openDistanceMeters;   // sfera anului e mare
            if (distance <= reach)
            {
                mainView.OpenTrace(l.trace);
                return;
            }

            int shown = RoundTo(distance, distance < 50f ? 5 : 10);
            int remaining = Mathf.Max(5, RoundTo(distance - reach + 2.5f, 5));
            string noun = l.trace.IsYearCapsule ? "Capsula anului" : l.trace.IsCapsule ? "Capsula" : "Urma";
            router.Toast($"{noun} e la vreo {shown} m, {Direction(cam.transform, toTrace)}. " +
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
            if (trace.IsCapsuleItem)
            {
                ReportCapsuleItem(trace);
                return;
            }
            if (trace.mine)
            {
                router.Toast("Asta e urma ta. O poți șterge, dacă vrei.");
                return;
            }
            trace.reported = true;
            Debug.Log($"[3Times] Urmă raportată: {trace.id}");
            if (!ServerSettings.IsConfigured)
            {
                router.Toast("Mulțumim. Raportarea ajunge la moderatori când aplicația e legată de server.", 4f);
                return;
            }
            mainView.StartCoroutine(TraceApi.Report(trace.id, null, (ok, error) =>
            {
                if (ok)
                {
                    TestLog.Add("Urmă raportată");
                    router.Toast("Mulțumim. Urma a fost trimisă la moderare.", 4f);
                }
                else
                {
                    trace.reported = false;
                    router.Toast($"Raportarea nu a mers: {error}", 4f);
                }
            }));
        }

        void Delete(SavedTrace trace)
        {
            if (trace.IsCapsuleItem)
            {
                DeleteCapsuleItem(trace);
                return;
            }
            if (trace.IsCapsule)
            {
                DeleteCapsule(trace);
                return;
            }
            if (!trace.mine)
            {
                router.Toast("Poți șterge doar urmele lăsate de tine.");
                return;
            }
            if (TraceTime.LockedInYearCapsule(trace))
            {
                router.Toast("Urma e în capsula anului, care s-a sigilat pe 1 ianuarie: nu se mai poate scoate.", 5f);
                return;
            }
            saved.Remove(trace);
            remote.Remove(trace.id);
            MediaStore.Delete(trace.mediaFile);
            // Și de pe server; dacă acum nu răspunde, ștergerea se reîncearcă la următoarea legătură.
            if (ServerSettings.IsConfigured && !TraceStore.PendingDeletes.Contains(trace.id))
                TraceStore.PendingDeletes.Add(trace.id);
            TraceStore.Save(saved);
            if (ServerSettings.IsConfigured) mainView.StartCoroutine(DeletePending());

            RemoveLive(trace.id);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(ShownForSim());
            Debug.Log($"[3Times] Urmă ștearsă: {trace.id}");
            TestLog.Add("Urmă ștearsă");
            router.Toast("Urma a fost ștearsă.");
        }

        // ---------------------------------------------------------------- server

        /// <summary>
        /// Urmele care se văd acum în Prezent: ale mele (de pe telefon) și cele din jur (de la server), fără dubluri.
        /// O urmă se vede două săptămâni, apoi doar la aniversare, o săptămână (TraceTime).
        /// </summary>
        List<SavedTrace> AllTraces()
        {
            var all = new List<SavedTrace>();
            var ids = new HashSet<string>();
            foreach (var t in saved)
            {
                ids.Add(t.id);
                if (TraceTime.InPresent(t)) all.Add(t);
            }
            foreach (var t in remote.Values)
                if (!ids.Contains(t.id) && TraceTime.InPresent(t)) all.Add(t);
            return all;
        }

        bool IsKnown(SavedTrace t) =>
            saved.Contains(t) || (remote.TryGetValue(t.id, out var r) && r == t) ||
            (capsules.TryGetValue(t.id, out var c) && c == t);

        /// <summary>
        /// Ce se vede acum, după modul de pe cursor: în Prezent urmele (și capsulele pe care le-am deschis, care au
        /// devenit urme), în Viitor doar capsulele încă nedeschise (plus cele deschise chiar acum, cât ies urmele).
        /// </summary>
        List<SavedTrace> Shown()
        {
            bool future = mainView.Mode == TimeMode.Future;
            var list = future ? new List<SavedTrace>() : AllTraces();
            foreach (var c in capsules.Values)
                if (future ? (c.@sealed || justOpened.Contains(c.id)) : !c.@sealed) list.Add(c);
            return list;
        }

        /// <summary>În simulare (Editor), o capsulă deschisă se desenează prin urmele ei.</summary>
        List<SavedTrace> ShownForSim()
        {
            var list = new List<SavedTrace>();
            foreach (var t in Shown())
            {
                if (t.IsCapsule && !t.@sealed) list.AddRange(t.items);
                else list.Add(t);
            }
            return list;
        }

        /// <summary>S-a schimbat modul: scoatem ce nu mai trebuie văzut și aducem ce lipsește.</summary>
        void OnModeChanged(TimeMode mode)
        {
            justOpened.Clear();
            if (mode == TimeMode.Future) nextFetch = 0f; // capsulele din jur, cât mai repede
            if (!tracker.Running) return;
            var keep = new HashSet<string>();
            foreach (var t in Shown()) keep.Add(t.id);
            var current = new List<string>(live.Keys);
            current.AddRange(pending.Keys);
            foreach (var id in current)
                if (!keep.Contains(id)) RemoveLive(id);
            if (tracker.Simulated)
            {
                if (simShown) mainView.ShowSimulatedTraces(ShownForSim());
            }
            else if (active)
            {
                RefreshPerimeter();
            }
        }

        /// <summary>Cere de la server urmele din jur, la fiecare fetchInterval secunde sau după o deplasare.</summary>
        void MaybeFetch()
        {
            if (!ServerSettings.IsConfigured || fetching || !tracker.Fix.HasValue) return;
            var f = tracker.Fix.Value;
            float now = Time.realtimeSinceStartup;
            bool moved = fetchedOnce &&
                         DistanceMeters(lastFetchLat, lastFetchLng, f.Latitude, f.Longitude) > refetchDistance;
            if (fetchedOnce && now < nextFetch && !moved) return;

            fetching = true;
            nextFetch = now + fetchInterval;
            lastFetchLat = f.Latitude;
            lastFetchLng = f.Longitude;
            // Pe MainScreenView, nu aici: corutinele de rețea nu trebuie oprite când se șterg urmele din scenă.
            mainView.StartCoroutine(SyncAndFetch(f.Latitude, f.Longitude));
        }

        /// <summary>Trimite ce a rămas netrimis, apoi ia urmele din jur.</summary>
        IEnumerator SyncAndFetch(double lat, double lng)
        {
            yield return UploadPending();
            yield return DeletePending();
            yield return RemoveDeletedByModerators();
            yield return TraceApi.Nearby(lat, lng, perimeterMeters, fetchLimit, (list, error) =>
            {
                if (list == null)
                {
                    if (!serverWarned)
                    {
                        serverWarned = true;
                        TestLog.Add($"Server: {error}");
                        router.Toast($"{error} Vezi doar urmele tale de pe telefon.", 4f);
                    }
                    return;
                }
                if (!fetchedOnce || serverWarned) TestLog.Add($"Server: {list.Count} urme în {perimeterMeters:0} m");
                serverWarned = false;
                ApplyRemote(list);
            });
            yield return CapsuleApi.Nearby(lat, lng, perimeterMeters, fetchLimit, (list, error) =>
            {
                if (list != null) ApplyCapsules(list, "CAPSULE");
            });
            yield return CapsuleApi.YearNearby(lat, lng, perimeterMeters, (list, error) =>
            {
                if (list != null) ApplyCapsules(list, "YEAR_CAPSULE");
            });
            fetchedOnce = true;
            fetching = false;
        }

        void ApplyRemote(List<SavedTrace> list)
        {
            var fresh = new Dictionary<string, SavedTrace>();
            foreach (var t in list)
            {
                t.id = TraceStore.NormalizeId(t.id);
                // Păstrăm obiectul deja cunoscut (urmele din scenă îl folosesc), doar actualizăm ce se schimbă.
                if (remote.TryGetValue(t.id, out var known))
                {
                    known.reported = t.reported;
                    fresh[t.id] = known;
                }
                else
                {
                    fresh[t.id] = t;
                }
                // O urmă de-a mea pe care serverul o are deja: e trimisă.
                if (t.mine)
                {
                    var mineLocal = saved.Find(x => x.id == t.id);
                    if (mineLocal != null && !mineLocal.synced)
                    {
                        mineLocal.synced = true;
                        TraceStore.Save(saved);
                    }
                }
            }
            remote.Clear();
            foreach (var pair in fresh) remote[pair.Key] = pair.Value;
            if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
        }

        /// <summary>Capsulele de un fel (CAPSULE sau YEAR_CAPSULE) primite de la server; celelalte rămân.</summary>
        void ApplyCapsules(List<SavedTrace> list, string kind)
        {
            var fresh = new Dictionary<string, SavedTrace>();
            foreach (var c in list)
            {
                if (capsules.TryGetValue(c.id, out var known))
                {
                    if (known.IsYearCapsule && known.opened)
                    {
                        // Capsula anului deschisă pe telefonul acesta: serverul nu ține minte, păstrăm ce s-a deschis.
                        known.itemCount = c.itemCount;
                        known.myItems = c.myItems;
                    }
                    else
                    {
                        // Deschisă între timp din contul meu, de pe alt telefon: urmele ies și aici.
                        bool reveal = known.@sealed && !c.@sealed;
                        CopyCapsule(known, c);
                        if (reveal) RemoveLive(known.id);
                    }
                    fresh[c.id] = known;
                }
                else
                {
                    fresh[c.id] = c;
                }
            }
            foreach (var pair in new List<KeyValuePair<string, SavedTrace>>(capsules))
                if (pair.Value.kind == kind) capsules.Remove(pair.Key);
            foreach (var pair in fresh) capsules[pair.Key] = pair.Value;
            if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
        }

        /// <summary>Copiază starea unei capsule venite de la server peste obiectul deja cunoscut.</summary>
        static void CopyCapsule(SavedTrace target, SavedTrace source)
        {
            target.createdAt = source.createdAt;
            target.mine = source.mine;
            target.visibility = source.visibility;
            target.unlockAt = source.unlockAt;
            target.ready = source.ready;
            target.opened = source.opened;
            target.@sealed = source.@sealed;
            target.collecting = source.collecting;
            target.itemCount = source.itemCount;
            target.myItems = source.myItems;
            target.maxItems = source.maxItems;
            target.items = source.items ?? new List<SavedTrace>();
            target.city = source.city;
            target.year = source.year;
            target.yearEmotions = source.yearEmotions ?? new List<YearEmotion>();
        }

        /// <summary>Capsula primită de la server: actualizează obiectul cunoscut sau o adaugă (și o arată).</summary>
        SavedTrace StoreCapsule(SavedTrace server)
        {
            if (capsules.TryGetValue(server.id, out var known))
            {
                CopyCapsule(known, server);
                if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
                return known;
            }
            capsules[server.id] = server;
            if (tracker.Simulated)
            {
                if (simShown) mainView.ShowSimulatedTraces(ShownForSim());
            }
            else if (tracker.Running && mainView.Mode == TimeMode.Future)
            {
                StartCoroutine(SpawnRoutine(server, true, null));
            }
            return server;
        }

        IEnumerator RestoreAfterFirstFetch()
        {
            float started = Time.realtimeSinceStartup;
            MaybeFetch();
            while (ServerSettings.IsConfigured && !fetchedOnce &&
                   Time.realtimeSinceStartup - started < firstFetchTimeout)
                yield return null;
            yield return RestoreNearby();
        }

        IEnumerator UploadPending()
        {
            foreach (var t in saved.ToArray())
                if (!t.synced && string.IsNullOrEmpty(t.syncError))
                    yield return Upload(t);
        }

        IEnumerator Upload(SavedTrace t)
        {
            if (!ServerSettings.IsConfigured || t.synced) yield break;
            yield return TraceApi.Create(t, (server, error, permanent, refused) =>
            {
                if (refused)
                {
                    // Moderarea n-a acceptat textul (de obicei prins deja în foaie; aici, dacă urma a plecat fără verificare).
                    RemoveRefused(t, error);
                }
                else if (server != null)
                {
                    t.synced = true;
                    t.hasMedia = server.hasMedia;
                    TraceStore.Save(saved);
                    TestLog.Add("Urmă trimisă la server");
                }
                else if (permanent)
                {
                    t.syncError = error;
                    TraceStore.Save(saved);
                    TestLog.Add($"Serverul a refuzat urma: {error}");
                }
                else
                {
                    Debug.Log($"[3Times] Urma rămâne de trimis: {error}");
                }
            });
        }

        /// <summary>O urmă refuzată de moderare nu rămâne nici pe telefon: o scoatem și îi spunem utilizatorului de ce.</summary>
        void RemoveRefused(SavedTrace t, string reason)
        {
            RemoveLocal(new List<SavedTrace> { t });
            TestLog.Add($"Urmă refuzată de moderare: {reason}");
            router.Toast($"Urma ta nu a fost publicată. {reason}", 6f);
        }

        /// <summary>
        /// Urmele mele trimise deja la server, dar care nu mai există acolo, au fost șterse de moderatori (admin):
        /// le scoatem și de pe telefon. Dacă serverul nu răspunde, nu ștergem nimic.
        /// </summary>
        IEnumerator RemoveDeletedByModerators()
        {
            if (!ServerSettings.IsConfigured) yield break;
            HashSet<string> onServer = null;
            yield return TraceApi.MyIds(ids => onServer = ids);
            if (onServer == null) yield break;

            var gone = saved.FindAll(t => t.synced && !onServer.Contains(TraceStore.NormalizeId(t.id)));
            if (gone.Count == 0) yield break;
            RemoveLocal(gone);
            TestLog.Add($"Urme șterse de moderatori: {gone.Count}");
            router.Toast(gone.Count == 1
                ? "Una dintre urmele tale a fost ștearsă de moderatori."
                : $"{gone.Count} dintre urmele tale au fost șterse de moderatori.", 5f);
        }

        void RemoveLocal(List<SavedTrace> traces)
        {
            foreach (var t in traces)
            {
                saved.Remove(t);
                remote.Remove(t.id);
                MediaStore.Delete(t.mediaFile);
                RemoveLive(t.id);
            }
            TraceStore.Save(saved);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(ShownForSim());
        }

        IEnumerator DeletePending()
        {
            foreach (var id in TraceStore.PendingDeletes.ToArray())
            {
                bool done = false;
                yield return TraceApi.Delete(id, ok => done = ok);
                if (done)
                {
                    TraceStore.PendingDeletes.Remove(id);
                    TraceStore.Save(saved);
                }
            }
        }

        /// <summary>Urmele care se văd acum (în AR sau, în Editor, peste ilustrație), pentru cartea poștală.</summary>
        List<SavedTrace> VisibleTraces()
        {
            if (tracker.Simulated) return ShownForSim();
            var list = new List<SavedTrace>();
            foreach (var l in live.Values)
            {
                if (!l.trace.IsCapsule) list.Add(l.trace);
                else if (!l.trace.@sealed) list.AddRange(l.trace.items);
            }
            return list;
        }

        /// <summary>
        /// Urmele pentru hartă, în jurul unui punct: ale mele de pe telefon plus cele de pe server.
        /// Fără server (sau dacă nu răspunde), doar ale mele.
        /// </summary>
        void TracesForMap(double lat, double lng, float radius, Action<List<SavedTrace>> done)
        {
            var mineNear = saved.FindAll(t => TraceTime.InPresent(t) && DistanceMeters(lat, lng, t.latitude, t.longitude) <= radius);
            if (!ServerSettings.IsConfigured)
            {
                done(mineNear);
                return;
            }
            mainView.StartCoroutine(TraceApi.Nearby(lat, lng, radius, 200, (list, error) =>
            {
                if (list == null)
                {
                    done(mineNear);
                    return;
                }
                var all = new List<SavedTrace>(mineNear);
                var ids = new HashSet<string>();
                foreach (var t in mineNear) ids.Add(t.id);
                foreach (var t in list)
                {
                    t.id = TraceStore.NormalizeId(t.id);
                    if (ids.Contains(t.id)) continue;
                    // Dacă urma e deja cunoscută (din AR), folosim același obiect.
                    all.Add(remote.TryGetValue(t.id, out var known) ? known : t);
                }
                done(all);
            }));
        }

        /// <summary>
        /// „Harta mea”: urmele contului, de pe orice telefon. Cele lăsate de pe telefonul acesta le luăm de aici
        /// (au poza / vocea deja pe telefon).
        /// </summary>
        void MyTracesForMap(Action<List<SavedTrace>, string> done)
        {
            if (!ServerSettings.IsConfigured)
            {
                done(null, "„Harta mea” are nevoie de server.");
                return;
            }
            mainView.StartCoroutine(AccountApi.MyTraces((list, error, expired) =>
            {
                if (list == null)
                {
                    done(null, expired ? "Sesiunea a expirat. Intră din nou în cont." : error);
                    return;
                }
                var result = new List<SavedTrace>(list.Count);
                foreach (var t in list)
                {
                    var local = saved.Find(x => x.id == t.id);
                    if (local != null) result.Add(local);
                    else result.Add(remote.TryGetValue(t.id, out var known) ? known : t);
                }
                done(result, null);
            }));
        }

        /// <summary>
        /// Contul a fost șters pe server împreună cu urmele lui și cu cele lăsate de pe telefonul acesta:
        /// le scoatem și de pe telefon (fișiere, scenă), fără să mai cerem ștergerea lor la server.
        /// </summary>
        void ForgetMyTraces()
        {
            int count = saved.Count;
            RemoveLocal(new List<SavedTrace>(saved));
            foreach (var t in new List<SavedTrace>(remote.Values))
            {
                if (!t.mine) continue;
                remote.Remove(t.id);
                RemoveLive(t.id);
                count++;
            }
            foreach (var c in new List<SavedTrace>(capsules.Values))
            {
                if (!c.mine) continue;
                capsules.Remove(c.id);
                RemoveLive(c.id);
            }
            TraceStore.PendingDeletes.Clear();
            TraceStore.Save(saved);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(ShownForSim());
            TestLog.Add($"Cont șters: {count} urme scoase de pe telefon");
        }

        /// <summary>
        /// Înainte să se deschidă o urmă cu poză sau voce de la altcineva, îi descărcăm fișierul.
        /// </summary>
        void EnsureMedia(SavedTrace trace, Action open)
        {
            bool needsFile = trace.type != "TEXT" && !MediaStore.Exists(trace.mediaFile);
            if (!needsFile || !trace.hasMedia || !ServerSettings.IsConfigured)
            {
                open();
                return;
            }
            router.Toast("Se încarcă urma…", 1.5f);
            mainView.StartCoroutine(TraceApi.DownloadMedia(trace, file =>
            {
                if (file != null) trace.mediaFile = file;
                else router.Toast("Fișierul urmei nu s-a putut descărca.");
                open();
            }));
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

            var trace = FromDraft(draft);

            if (tracker.Simulated)
            {
                Commit(trace);
                return;
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

        /// <summary>Urma (sau capsula) completată în foaie, la `distanceAhead` metri în fața telefonului.</summary>
        SavedTrace FromDraft(TraceDraft draft)
        {
            var fix = tracker.Fix.Value;
            var look = draft.Look ?? TraceStyle.Legacy("bucurie");
            var trace = new SavedTrace
            {
                id = Guid.NewGuid().ToString(),
                type = string.IsNullOrEmpty(draft.Type) ? "TEXT" : draft.Type,
                content = draft.Text ?? "",
                mediaFile = draft.MediaFile,
                photoSource = draft.PhotoSource,
                durationSeconds = draft.DurationSeconds,
                emotion = look.emotion,
                shape = look.shape,
                color = look.color,
                accentColor = look.accentColor,
                petals = look.petals,
                sharpness = look.sharpness,
                motion = look.motion,
                intensity = (draft.Intensity + 1) / 3f,
                latitude = fix.Latitude,
                longitude = fix.Longitude,
                altitude = fix.Altitude - heightBelowCamera,
                heightAboveTerrain = heightAboveTerrain,
                accuracyMeters = (float)fix.HorizontalAccuracy,
                yawAccuracyDegrees = (float)fix.YawAccuracy,
                createdAt = DateTime.UtcNow.ToString("o"),
                yearCapsule = draft.YearCapsule
            };
            if (tracker.Simulated) return trace;

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
            return trace;
        }

        // ---------------------------------------------------------------- capsulele timpului (Viitor)

        /// <summary>De ce nu se poate închide acum o capsulă, sau null.</summary>
        string CapsuleBlockReason()
        {
            if (!ServerSettings.IsConfigured)
                return "Capsulele se păstrează pe server: leagă aplicația de server (Meniu → Server) și mai încearcă.";
            if (creatingCapsule) return "Se trimite capsula de dinainte. Mai încearcă într-o clipă.";
            return PlaceBlockReason();
        }

        /// <summary>
        /// Capsula pleacă direct la server (acolo se verifică data și locul la deschidere). Pe telefon nu păstrăm
        /// conținutul: nici cine a lăsat-o nu o poate citi înainte de dată.
        /// </summary>
        void PlaceCapsule(TraceDraft draft)
        {
            string blocked = CapsuleBlockReason();
            if (blocked != null)
            {
                router.Toast(blocked, 4f);
                MediaStore.Delete(draft.MediaFile);
                return;
            }
            var item = FromDraft(draft);
            item.kind = "CAPSULE_ITEM";
            creatingCapsule = true;

            // O urmă în plus, într-o capsulă care mai primește urme.
            if (!string.IsNullOrEmpty(draft.CapsuleId))
            {
                router.Toast("Se pune urma în capsulă…", 2f);
                mainView.StartCoroutine(CapsuleApi.AddItem(draft.CapsuleId, item, (server, error, refused) =>
                {
                    creatingCapsule = false;
                    MediaStore.Delete(item.mediaFile);
                    if (server == null)
                    {
                        TestLog.Add($"Urmă nepusă în capsulă: {error}");
                        router.Toast(refused ? $"Urma nu a fost pusă. {error}" : $"Urma nu s-a putut pune în capsulă. {error}", 6f);
                        return;
                    }
                    var stored = StoreCapsule(server);
                    TestLog.Add($"Urmă pusă în capsulă ({stored.itemCount} urme)");
                    router.Toast($"Urma ta e în capsulă ({stored.itemCount} din {(stored.maxItems > 0 ? stored.maxItems : 10)}).", 4f);
                }));
                return;
            }

            // Capsulă nouă, cu prima urmă.
            var place = new SavedTrace
            {
                id = Guid.NewGuid().ToString(),
                kind = "CAPSULE",
                latitude = item.latitude,
                longitude = item.longitude,
                altitude = item.altitude,
                heightAboveTerrain = item.heightAboveTerrain,
                accuracyMeters = item.accuracyMeters,
                yawAccuracyDegrees = item.yawAccuracyDegrees,
                visibility = string.IsNullOrEmpty(draft.Visibility) ? "PUBLIC" : draft.Visibility,
                unlockAt = draft.UnlockAt
            };
            router.Toast("Se face capsula…", 2f);
            mainView.StartCoroutine(CapsuleApi.Create(place, item, (server, error, refused) =>
            {
                creatingCapsule = false;
                MediaStore.Delete(item.mediaFile);
                if (server == null)
                {
                    TestLog.Add($"Capsulă netrimisă: {error}");
                    router.Toast(refused ? $"Capsula nu a fost păstrată. {error}" : $"Capsula nu s-a putut trimite. {error}", 6f);
                    return;
                }
                var stored = StoreCapsule(server);
                TestLog.Add($"Capsulă creată ({stored.visibility}, se deschide {stored.unlockAt}) · " +
                            $"±{place.accuracyMeters:0.0} m · {stored.latitude:F6}, {stored.longitude:F6}");
                router.Toast(stored.visibility == "PERSONAL"
                    ? "Capsula ta e aici, cu prima urmă. Atinge sfera ca să mai pui urme, apoi sigileaz-o."
                    : "Capsula e aici, cu prima ta urmă. Oricine trece pe aici își poate pune urma, până o sigilezi tu.", 6f);
            }));
        }

        /// <summary>Atingerea unei capsule sigilate: serverul verifică data și locul și, dacă e bine, trimite conținutul.</summary>
        void OpenCapsule(SavedTrace capsule)
        {
            if (opening) return;
            if (!ServerSettings.IsConfigured)
            {
                router.Toast("Capsulele se deschid prin server: Meniu → Server.", 4f);
                return;
            }
            if (capsule.IsYearCapsule && !CapsuleText.IsReady(capsule))
            {
                mainView.ShowYearCard(capsule);
                return;
            }
            if (!CapsuleText.IsReady(capsule))
            {
                bool canAdd = capsule.collecting && (capsule.visibility != "PERSONAL" || capsule.mine);
                mainView.ShowCapsuleCard(capsule,
                    canAdd ? () => mainView.OpenAddToCapsule(capsule) : (Action)null,
                    capsule.collecting && capsule.mine ? () => SealCapsule(capsule) : (Action)null,
                    capsule.mine ? () => DeleteCapsule(capsule) : (Action)null,
                    capsule.collecting && capsule.myItems > 0 ? () => RemoveMyCapsuleItems(capsule) : (Action)null);
                return;
            }
            if (!tracker.Fix.HasValue)
            {
                router.Toast("Așteaptă să se termine localizarea, apoi atinge din nou capsula.", 4f);
                return;
            }
            var f = tracker.Fix.Value;
            opening = true;
            router.Toast("Se deschide capsula…", 1.5f);
            Func<string, double, double, float, Action<SavedTrace, string>, IEnumerator> open =
                capsule.IsYearCapsule ? CapsuleApi.YearOpen : CapsuleApi.Open;
            mainView.StartCoroutine(open(capsule.id, f.Latitude, f.Longitude, (float)f.HorizontalAccuracy,
                (opened, error) =>
                {
                    opening = false;
                    if (opened == null)
                    {
                        router.Toast(error, 5f);
                        return;
                    }
                    CopyCapsule(capsule, opened);
                    justOpened.Add(capsule.id);
                    TestLog.Add("Capsulă deschisă");
                    RevealCapsule(capsule);
                }));
        }

        /// <summary>Sticla se sparge și urmele ies din ea; o singură urmă se deschide direct.</summary>
        void RevealCapsule(SavedTrace capsule)
        {
            int n = capsule.items?.Count ?? 0;
            string hint = n == 0 ? "Capsula s-a deschis, dar urmele din ea au fost scoase."
                        : n == 1 ? null
                        : $"Capsula s-a deschis: {n} urme. Atinge-le ca să le deschizi.";
            if (live.TryGetValue(capsule.id, out var l) && l.capsuleVisual != null && l.anchor != null)
            {
                if (l.touch != null)
                {
                    byCollider.Remove(l.touch);
                    Destroy(l.touch);
                }
                var anchor = l.anchor;
                l.capsuleVisual.Shatter(() =>
                {
                    if (anchor == null || !tracker.Running) return;
                    emergeNext.Add(capsule.id);
                    BuildVisual(anchor, capsule);
                }, l.root != null ? l.root.gameObject : null);
                StartCoroutine(AfterReveal(capsule, hint, 1.8f));
                return;
            }
            if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
            StartCoroutine(AfterReveal(capsule, hint, 0.2f));
        }

        IEnumerator AfterReveal(SavedTrace capsule, string hint, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (capsule.IsYearCapsule) mainView.ShowYearCard(capsule);
            else if (hint != null) router.Toast(hint, 5f);
            else if (capsule.items != null && capsule.items.Count == 1) mainView.OpenTrace(capsule.items[0]);
        }

        void SealCapsule(SavedTrace capsule)
        {
            mainView.StartCoroutine(CapsuleApi.Seal(capsule.id, (server, error) =>
            {
                if (server == null)
                {
                    router.Toast($"Capsula nu s-a putut sigila. {error}", 5f);
                    return;
                }
                var stored = StoreCapsule(server);
                TestLog.Add($"Capsulă sigilată ({stored.itemCount} urme)");
                router.Toast($"Capsula e sigilată, cu {stored.itemCount} {(stored.itemCount == 1 ? "urmă" : "urme")}. " +
                             $"Se deschide {CapsuleText.When(stored)}.", 5f);
            }));
        }

        void ReportCapsuleItem(SavedTrace item)
        {
            if (item.mine)
            {
                router.Toast("Asta e urma ta. O poți șterge, dacă vrei.");
                return;
            }
            item.reported = true;
            mainView.StartCoroutine(CapsuleApi.ReportItem(item.id, null, (ok, error) =>
            {
                if (ok)
                {
                    TestLog.Add("Urmă din capsulă raportată");
                    router.Toast("Mulțumim. Urma a fost trimisă la moderare.", 4f);
                }
                else
                {
                    item.reported = false;
                    router.Toast($"Raportarea nu a mers: {error}", 4f);
                }
            }));
        }

        /// <summary>Scoate urmele mele dintr-o capsulă de grup care încă se umple.</summary>
        void RemoveMyCapsuleItems(SavedTrace capsule)
        {
            mainView.StartCoroutine(CapsuleApi.DeleteMyItems(capsule.id, (ok, error) =>
            {
                if (!ok)
                {
                    router.Toast($"Urmele nu s-au putut scoate. {error}", 5f);
                    return;
                }
                int removed = capsule.myItems;
                capsule.itemCount = Mathf.Max(0, capsule.itemCount - removed);
                capsule.myItems = 0;
                if (capsule.itemCount == 0)
                {
                    capsules.Remove(capsule.id);
                    RemoveLive(capsule.id);
                }
                if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
                TestLog.Add($"Urme scoase din capsulă: {removed}");
                router.Toast(removed == 1 ? "Urma ta a fost scoasă din capsulă." : $"Cele {removed} urme ale tale au fost scoase din capsulă.");
            }));
        }

        void DeleteCapsuleItem(SavedTrace item)
        {
            if (!item.mine)
            {
                router.Toast("Poți șterge doar urmele puse de tine.");
                return;
            }
            mainView.StartCoroutine(CapsuleApi.DeleteItem(item.id, (ok, error) =>
            {
                if (!ok)
                {
                    router.Toast($"Urma nu s-a putut șterge: {error}", 4f);
                    return;
                }
                if (item.capsuleId != null && capsules.TryGetValue(item.capsuleId, out var capsule))
                {
                    capsule.items.RemoveAll(x => x.id == item.id);
                    capsule.itemCount = Mathf.Max(0, capsule.itemCount - 1);
                    capsule.myItems = Mathf.Max(0, capsule.myItems - 1);
                    RemoveLive(capsule.id); // se reface la următoarea verificare a perimetrului
                }
                if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
                TestLog.Add("Urmă din capsulă ștearsă");
                router.Toast("Urma a fost ștearsă.");
            }));
        }

        void DeleteCapsule(SavedTrace capsule)
        {
            if (!capsule.mine)
            {
                router.Toast("Poți șterge doar capsulele lăsate de tine.");
                return;
            }
            mainView.StartCoroutine(CapsuleApi.Delete(capsule.id, (ok, error) =>
            {
                if (!ok)
                {
                    router.Toast($"Capsula nu s-a putut șterge: {error}", 4f);
                    return;
                }
                capsules.Remove(capsule.id);
                RemoveLive(capsule.id);
                if (tracker.Simulated && simShown) mainView.ShowSimulatedTraces(ShownForSim());
                TestLog.Add("Capsulă ștearsă");
                router.Toast("Capsula a fost ștearsă.");
            }));
        }

        void Commit(SavedTrace trace)
        {
            trace.mine = true;
            trace.synced = false;
            saved.Add(trace);
            TraceStore.Save(saved);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(ShownForSim());
            if (ServerSettings.IsConfigured) mainView.StartCoroutine(Upload(trace));
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
            if (!tracker.Running || (!isNew && !IsKnown(t)))
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
            CapsuleVisual capsuleVisual = null;
            List<(Collider touch, SavedTrace item)> itemTouches = null;
            if (particleMaterial != null && t.IsCapsule && t.@sealed)
            {
                // Capsula încă închisă: sfera de sticlă cu lumină (a anului, mai mare).
                float size = t.IsYearCapsule ? 2.2f : 1f;
                var holder = new GameObject("Sphere").transform;
                holder.SetParent(appear, false);
                holder.localScale = Vector3.one * size;
                capsuleVisual = CapsuleVisual.Build(t, particleMaterial, holder);
                touch.radius = CapsuleVisual.TouchRadius * size;
            }
            else if (particleMaterial != null && t.IsCapsule)
            {
                // Capsula deschisă: urmele ei, în cerc; fiecare se atinge separat, capsula însăși nu.
                itemTouches = CapsuleVisual.BuildOpened(t, particleMaterial, appear, emergeNext.Remove(t.id));
                Destroy(touch);
                touch = null;
            }
            else if (particleMaterial != null)
            {
                // Forma din particule: floare, picătură sau semilună, după emoție și intensitate.
                var style = TraceStyle.Of(t);
                TraceShapeBuilder.Build(style, t.id, particleMaterial, appear, t.type);
                touch.radius = TraceShapeBuilder.Diameter(style.Level) * 0.6f;
                // „Acum un an, aici”: urma revenită la aniversare are câteva scântei aurii în jur.
                if (TraceTime.AnniversaryYears(t) > 0) CapsuleVisual.AddShards(t.id, particleMaterial, appear);
            }
            else if (markerPrefab != null)
            {
                var marker = Instantiate(markerPrefab, appear, false);
                // Mărimea crește cu intensitatea, culoarea vine din emoție.
                float size = Mathf.Lerp(0.7f, 1.3f, t.intensity);
                marker.transform.localScale = Vector3.one * size;
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColor, TraceStyle.Of(t).Color);
                foreach (var r in marker.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(block);
                touch.radius = 0.35f * size;
            }

            root.gameObject.SetActive(tracesVisible);
            var l = new LiveTrace { trace = t, anchor = anchor, root = root, touch = touch, capsuleVisual = capsuleVisual };
            live[t.id] = l;
            if (touch != null) byCollider[touch] = l;
            if (itemTouches != null)
            {
                l.extraTouches = new List<Collider>();
                foreach (var (itemTouch, item) in itemTouches)
                {
                    l.extraTouches.Add(itemTouch);
                    byCollider[itemTouch] = new LiveTrace { trace = item, anchor = anchor, root = root, touch = itemTouch };
                }
            }
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
                if (l.extraTouches != null)
                    foreach (var c in l.extraTouches)
                        if (c != null) byCollider.Remove(c);
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


        // ---------------------------------------------------------------- previzualizare (doar Editor)

        /// <summary>
        /// În Play, în Editor: arată cele 12 forme de bază și, dedesubt, cele trei tipuri
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
            float distance = Mathf.Max(3.3f / tanHalf, 1.95f / (tanHalf * Mathf.Max(0.1f, cam.aspect)));
            cam.transform.position = new Vector3(0f, -0.55f, 4.6f - distance);

            // Cele 12 forme de bază (nivel „mediu”), câte 4 pe rând, fiecare în altă culoare.
            for (int i = 0; i < TraceStyle.Shapes.Length; i++)
            {
                var look = TraceStyle.FromWord("preview-" + i);
                look.shape = TraceStyle.Shapes[i];
                look.petals = 6;
                look.sharpness = 0.4f;
                look.intensity = 1;
                var holder = new GameObject($"Preview {look.shape}");
                holder.transform.position = new Vector3((i % 4 - 1.5f) * 1.1f, 1.6f - (i / 4) * 1.05f, 4.6f);
                TraceShapeBuilder.Build(TraceStyle.Of(look), $"preview-{i}", particleMaterial, holder.transform);
            }

            // Ultimul rând: cele trei tipuri de urmă (text, poză, voce).
            string[] keys = { "joy", "sadness", "longing" };
            string[] types = { "TEXT", "PHOTO", "VOICE" };
            for (int i = 0; i < 3; i++)
            {
                var holder = new GameObject($"Preview {types[i]}");
                holder.transform.position = new Vector3((i - 1) * 1.4f, -1.75f, 4.6f);
                TraceShapeBuilder.Build(keys[i], 1, $"preview-type-{i}", particleMaterial, holder.transform, types[i]);
            }

            // Rândul capsulelor: închisă (lumină slabă) și gata de deschis (lumină puternică, inel).
            for (int i = 0; i < 2; i++)
            {
                var capsule = new SavedTrace
                {
                    id = "preview-capsule-" + i,
                    kind = "CAPSULE",
                    @sealed = true,
                    ready = i == 1,
                    unlockAt = DateTime.UtcNow.AddDays(i == 1 ? -1 : 30).ToString("o")
                };
                var holder = new GameObject($"Preview capsule {i}");
                holder.transform.position = new Vector3((i - 0.5f) * 1.6f, -2.9f, 4.6f);
                CapsuleVisual.Build(capsule, particleMaterial, holder.transform);
            }
        }

        // ---------------------------------------------------------------- ștergere

        void ClearAll()
        {
            int count = saved.Count;
            foreach (var t in saved)
            {
                MediaStore.Delete(t.mediaFile);
                remote.Remove(t.id);
                if (ServerSettings.IsConfigured && !TraceStore.PendingDeletes.Contains(t.id))
                    TraceStore.PendingDeletes.Add(t.id);
            }
            saved.Clear();
            TraceStore.Save(saved);
            if (ServerSettings.IsConfigured) mainView.StartCoroutine(DeletePending());
            DestroyAll();
            mainView.SetTraceCount(0);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(ShownForSim());
            Debug.Log($"[3Times] Urme șterse: {count}");
            if (count > 0) TestLog.Add($"Toate urmele de test șterse ({count})");
            router.Toast(count == 0 ? "Nu ai urme de test." : $"Am șters {count} urme de test.");
        }
    }
}
