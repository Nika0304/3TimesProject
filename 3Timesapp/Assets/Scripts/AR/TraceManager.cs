using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;
using ThreeTimes.Core;
using ThreeTimes.Data;
using ThreeTimes.UI;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Urmele din modul Prezent. O urmă nouă se așază la 2 m în fața telefonului, legată de
    /// coordonatele reale (ancoră geospațială), și se salvează local. La următoarea pornire a
    /// camerei, urmele salvate reapar în același loc. Deocamdată urma se vede ca un marcaj
    /// în culoarea emoției; formele din particule vin în pasul următor.
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

        [Tooltip("La ce distanță în fața telefonului apare urma (m).")]
        [SerializeField] float distanceAhead = 2f;
        [Tooltip("Cu cât mai jos decât telefonul apare urma (m).")]
        [SerializeField] float heightBelowCamera = 0.3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly List<GameObject> spawned = new List<GameObject>();
        // Pentru atingeri: ce urmă ține fiecare marcaj din scenă.
        readonly Dictionary<Collider, SavedTrace> byCollider = new Dictionary<Collider, SavedTrace>();
        readonly Dictionary<string, GameObject> byId = new Dictionary<string, GameObject>();
        List<SavedTrace> saved = new List<SavedTrace>();
        bool restored;

        void Start()
        {
            saved = TraceStore.Load();
            mainView.TraceSubmitted += Place;
            mainView.ClearTracesRequested += ClearAll;
            mainView.WorldTapped += OnWorldTapped;
            mainView.TraceReportRequested += Report;
            mainView.TraceDeleteRequested += Delete;
            mainView.SetTraceCount(saved.Count);
            Debug.Log($"[3Times] Urme salvate: {saved.Count}");
        }

        void Update()
        {
            // Camera a pornit și localizarea e bună: readucem urmele salvate.
            if (tracker.Running && !tracker.Simulated && !restored &&
                tracker.Quality == LocalizationQuality.Good)
            {
                restored = true;
                int ok = 0;
                foreach (var t in saved)
                    if (Spawn(t)) ok++;
                if (saved.Count > 0)
                {
                    Debug.Log($"[3Times] Urme readuse: {ok} din {saved.Count}");
                    TestLog.Add($"Urme readuse: {ok} din {saved.Count}");
                }
            }

            // În simulare (Editor) nu există AR: desenăm urmele peste ilustrație.
            if (tracker.Running && tracker.Simulated && !restored)
            {
                restored = true;
                mainView.ShowSimulatedTraces(saved);
            }

            // Camera s-a oprit: sesiunea AR se închide, deci urmele dispar până data viitoare.
            if (!tracker.Running && restored)
            {
                restored = false;
                DestroySpawned();
            }
        }

        // ---------------------------------------------------------------- atingere, raportare, ștergere

        /// <summary>Atingere pe ecran: dacă nimerește o urmă din AR, îi deschidem ecranul.</summary>
        void OnWorldTapped(Vector2 screenPosition)
        {
            var cam = Camera.main;
            if (cam == null || byCollider.Count == 0) return;
            var ray = cam.ScreenPointToRay(new Vector3(screenPosition.x, screenPosition.y, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 200f) &&
                byCollider.TryGetValue(hit.collider, out var trace))
            {
                mainView.OpenTrace(trace);
            }
        }

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
            TraceStore.Save(saved);
            if (byId.TryGetValue(trace.id, out var go))
            {
                foreach (var c in go.GetComponentsInChildren<Collider>()) byCollider.Remove(c);
                byId.Remove(trace.id);
                spawned.Remove(go);
                Destroy(go);
            }
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            Debug.Log($"[3Times] Urmă ștearsă: {trace.id}");
            TestLog.Add("Urmă ștearsă");
            router.Toast("Urma a fost ștearsă.");
        }

        // ---------------------------------------------------------------- plasare

        void Place(TraceDraft draft)
        {
            if (!tracker.Running || !tracker.Fix.HasValue || tracker.Quality != LocalizationQuality.Good)
            {
                router.Toast("Localizarea nu e încă destul de precisă. Îndreaptă telefonul spre clădiri și mai încearcă.");
                return;
            }

            var fix = tracker.Fix.Value;
            var trace = new SavedTrace
            {
                id = Guid.NewGuid().ToString("N"),
                content = draft.Text,
                emotion = EmotionCode(draft.Emotion),
                intensity = (draft.Intensity + 1) / 3f,
                latitude = fix.Latitude,
                longitude = fix.Longitude,
                altitude = fix.Altitude - heightBelowCamera,
                accuracyMeters = (float)fix.HorizontalAccuracy,
                createdAt = DateTime.UtcNow.ToString("o")
            };

            if (!tracker.Simulated)
            {
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

                if (!Spawn(trace))
                {
                    router.Toast("Urma nu a putut fi așezată. Mai încearcă peste câteva secunde.");
                    return;
                }
            }

            saved.Add(trace);
            TraceStore.Save(saved);
            mainView.SetTraceCount(saved.Count);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            TestLog.Add($"Urmă lăsată ({TraceLook.Name(trace.emotion)}, nivel {TraceLook.Level(trace.intensity)}) · " +
                        $"±{trace.accuracyMeters:0.0} m · {trace.latitude:F6}, {trace.longitude:F6}");
            Debug.Log($"[3Times] Urmă lăsată ({trace.emotion}, {trace.intensity:0.00}): {trace.latitude:F6}, {trace.longitude:F6}, " +
                      $"alt {trace.altitude:F1} m, precizie ±{trace.accuracyMeters:0.0} m (total {saved.Count})");
            router.Toast(tracker.Simulated
                ? "Urma ta a rămas aici (simulare în Editor)."
                : $"Urma ta a rămas aici, la {distanceAhead:0} m în față.", 4f);
        }

        bool Spawn(SavedTrace t)
        {
            // Rotația identitate = aliniată cu est / sus / nord.
            var anchor = anchorManager.AddAnchor(t.latitude, t.longitude, t.altitude, Quaternion.identity);
            if (anchor == null)
            {
                Debug.LogWarning($"[3Times] AddAnchor a eșuat pentru urma {t.id}");
                return false;
            }

            if (particleMaterial != null)
            {
                // Forma din particule: floare, picătură sau semilună, după emoție și intensitate.
                int level = TraceLook.Level(t.intensity);
                var holder = new GameObject("Trace");
                holder.transform.SetParent(anchor.transform, false);
                TraceShapeBuilder.Build(TraceLook.Key(t.emotion), level, t.id, particleMaterial, holder.transform);

                // Zonă de atingere cât forma, ca urma să fie ușor de nimerit cu degetul.
                var touchArea = holder.AddComponent<SphereCollider>();
                touchArea.radius = TraceShapeBuilder.Diameter(level) * 0.6f;
                byCollider[touchArea] = t;
            }
            else if (markerPrefab != null)
            {
                var marker = Instantiate(markerPrefab, anchor.transform, false);
                // Mărimea crește cu intensitatea, culoarea vine din emoție.
                marker.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, t.intensity);
                var block = new MaterialPropertyBlock();
                block.SetColor(BaseColor, EmotionColor(t.emotion));
                foreach (var r in marker.GetComponentsInChildren<Renderer>())
                    r.SetPropertyBlock(block);

                // Zonă de atingere mai mare decât sfera, ca urma să fie ușor de nimerit cu degetul.
                var touchArea = marker.AddComponent<SphereCollider>();
                touchArea.radius = 0.35f;
                byCollider[touchArea] = t;
            }
            spawned.Add(anchor.gameObject);
            byId[t.id] = anchor.gameObject;
            return true;
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
        /// În Play, în Editor: arată cele nouă forme (3 emoții × 3 intensități) în fața unei camere,
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
            cam.transform.position = Vector3.zero;

            string[] keys = { "joy", "sadness", "longing" };
            for (int e = 0; e < 3; e++)
                for (int level = 0; level < 3; level++)
                {
                    var holder = new GameObject($"Preview {keys[e]} {level}");
                    holder.transform.position = new Vector3((level - 1) * 1.1f, (1 - e) * 1.0f, 3.5f);
                    TraceShapeBuilder.Build(keys[e], level, $"preview-{e}-{level}", particleMaterial, holder.transform);
                }
        }

        // ---------------------------------------------------------------- ștergere

        void ClearAll()
        {
            int count = saved.Count;
            saved.Clear();
            TraceStore.Save(saved);
            DestroySpawned();
            mainView.SetTraceCount(0);
            if (tracker.Simulated) mainView.ShowSimulatedTraces(saved);
            Debug.Log($"[3Times] Urme șterse: {count}");
            if (count > 0) TestLog.Add($"Toate urmele de test șterse ({count})");
            router.Toast(count == 0 ? "Nu ai urme de test." : $"Am șters {count} urme de test.");
        }

        void DestroySpawned()
        {
            foreach (var go in spawned)
                if (go != null) Destroy(go);
            spawned.Clear();
            byCollider.Clear();
            byId.Clear();
        }
    }
}
