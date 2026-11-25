using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using ThreeTimes.Data;

namespace ThreeTimes.Past
{
    /// <summary>
    /// „Cerul pictat”: Opera de azi e mai înaltă (și la mijloc mai lată) decât cea din 1900, iar AR-ul nu poate șterge
    /// realitatea. Așa că, cât timp se vede fațada veche, desenăm peste clădirea reală o suprafață de culoarea cerului
    /// (ThreeTimes/SkyMask), iar fațada veche se desenează peste ea. Ce iese din conturul fațadei vechi pare cer.
    ///
    /// Forma clădirii de azi vine din ARCore (Streetscape Geometry: blocurile 3D ale clădirilor din jur); folosim
    /// clădirile atinse de fațada veche. Dacă ARCore nu dă clădirile în câteva secunde, folosim o cutie de rezervă
    /// cu dimensiunile estimate ale părții de mijloc de azi (mai înaltă și mai lată).
    /// Culoarea cerului urmează lumina reală (PastLook.Brightness); filtrul sepia o face să se potrivească.
    /// </summary>
    public class PastSkyMask : MonoBehaviour
    {
        [SerializeField] PastManager past;
        [SerializeField] PastLook look;
        [SerializeField] ARStreetscapeGeometryManager streetscape;
        [Tooltip("AR Session Origin: clădirile din ARCore se pun sub Trackables-ul lui")]
#pragma warning disable CS0618 // ARSessionOrigin e cerut de ARCore Extensions 1.57
        [SerializeField] ARSessionOrigin sessionOrigin;
#pragma warning restore CS0618
        [SerializeField] Material maskMaterial;

        [Tooltip("O clădire ARCore e „Opera” dacă baza ei e la cel mult atâția metri de mijlocul fațadei")]
        [SerializeField] float pickRadius = 8f;
        [Tooltip("După câte secunde fără clădiri ARCore folosim cutia de rezervă")]
        [SerializeField] float fallbackAfterSeconds = 6f;

        [Header("Cutia de rezervă (în coordonatele fațadei, metri)")]
        [SerializeField] Vector3 fallbackCenter = new Vector3(0f, 26f, -1f);
        [SerializeField] Vector3 fallbackSize = new Vector3(23f, 10f, 11f);

        [Header("Culorile cerului")]
        [SerializeField] Color dayTop = new Color(0.86f, 0.88f, 0.90f);
        [SerializeField] Color dayHorizon = new Color(0.94f, 0.93f, 0.90f);
        [SerializeField] Color nightTop = new Color(0.16f, 0.18f, 0.25f);
        [SerializeField] Color nightHorizon = new Color(0.36f, 0.33f, 0.34f);

        static readonly int TopColor = Shader.PropertyToID("_TopColor");
        static readonly int HorizonColor = Shader.PropertyToID("_HorizonColor");

        class BuildingMask
        {
            public GameObject go;
            public Bounds worldBounds;
            public bool nearFacade;
        }

        readonly Dictionary<TrackableId, BuildingMask> buildings = new Dictionary<TrackableId, BuildingMask>();
        GameObject fallback;
        Transform fallbackOwner;
        bool loggedSource;
        float lastReveal = -1f;
        int lastNear = -1;

        void OnEnable()
        {
            if (streetscape != null) streetscape.StreetscapeGeometriesChanged += OnGeometriesChanged;
        }

        void OnDisable()
        {
            if (streetscape != null) streetscape.StreetscapeGeometriesChanged -= OnGeometriesChanged;
        }

        void OnGeometriesChanged(ARStreetscapeGeometriesChangedEventArgs args)
        {
            if (args.Added != null) foreach (var g in args.Added) Upsert(g);
            if (args.Updated != null) foreach (var g in args.Updated) Upsert(g);
            if (args.Removed != null)
                foreach (var g in args.Removed)
                {
                    if (!buildings.TryGetValue(g.trackableId, out var b)) continue;
                    Destroy(b.go);
                    buildings.Remove(g.trackableId);
                }
        }

        void Upsert(ARStreetscapeGeometry g)
        {
            if (g.streetscapeGeometryType != StreetscapeGeometryType.Building) return;
            if (!buildings.TryGetValue(g.trackableId, out var b))
            {
                var mesh = g.mesh;
                if (mesh == null || mesh.vertexCount == 0) return;
                var go = new GameObject("Cer pictat (clădire ARCore)");
                var parent = sessionOrigin != null ? (sessionOrigin.trackablesParent != null ? sessionOrigin.trackablesParent : sessionOrigin.transform) : null;
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = maskMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                b = new BuildingMask { go = go };
                buildings[g.trackableId] = b;
            }
            // Poza e în spațiul sesiunii AR, adică relativ la Trackables (copilul lui AR Session Origin).
            var pose = g.pose;
            b.go.transform.localPosition = pose.position;
            b.go.transform.localRotation = pose.rotation;
            b.worldBounds = WorldBounds(b.go.transform, b.go.GetComponent<MeshFilter>().sharedMesh.bounds);
        }

        void Update()
        {
            var facade = past != null ? past.Facade : null;
            bool on = past != null && past.FacadeVisible && facade != null;

            int near = 0;
            foreach (var b in buildings.Values)
            {
                b.nearFacade = on && IsNear(b.worldBounds, facade.position);
                if (b.nearFacade) near++;
                if (b.go.activeSelf != b.nearFacade) b.go.SetActive(b.nearFacade);
            }

            bool useFallback = on && near == 0 && past.VisibleFor >= fallbackAfterSeconds;
            if (useFallback) EnsureFallback(facade);
            if (fallback != null && fallback.activeSelf != useFallback) fallback.SetActive(useFallback);

            // Cerul se developează odată cu fațada (aceeași granulație), ca să nu apară dintr-odată.
            if (on)
            {
                float reveal = past.RiseProgress;
                if (reveal != lastReveal || near != lastNear)
                {
                    foreach (var b in buildings.Values) if (b.nearFacade) PastReveal.Apply(b.go, reveal);
                    if (fallback != null && useFallback) PastReveal.Apply(fallback, reveal);
                    lastReveal = reveal;
                    lastNear = near;
                }
            }
            else lastReveal = -1f;

            if (on && !loggedSource && (near > 0 || useFallback))
            {
                loggedSource = true;
                TestLog.Add(near > 0 ? $"Cer pictat: {near} clădiri din ARCore" : "Cer pictat: cutia de rezervă (fără clădiri ARCore)");
            }
            if (!on) loggedSource = false;

            if (on && maskMaterial != null)
            {
                // Ziua (și cea înnorată) dă cer deschis; culorile de amurg vin doar când chiar e întuneric.
                float b = look != null ? look.Brightness : 0.7f;
                float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.35f, b));
                maskMaterial.SetColor(TopColor, Color.Lerp(nightTop, dayTop, day));
                maskMaterial.SetColor(HorizonColor, Color.Lerp(nightHorizon, dayHorizon, day));
            }
        }

        /// <summary>Clădirea e „sub” fațadă: distanța pe orizontală de la mijlocul fațadei la cutia clădirii e mică.</summary>
        bool IsNear(Bounds bounds, Vector3 point)
        {
            var p = new Vector3(point.x, bounds.center.y, point.z);
            return bounds.SqrDistance(p) <= pickRadius * pickRadius;
        }

        void EnsureFallback(Transform facade)
        {
            if (fallback != null && fallbackOwner == facade) return;
            if (fallback != null) Destroy(fallback);
            fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallback.name = "Cer pictat (cutia de rezervă)";
            Destroy(fallback.GetComponent<Collider>());
            var renderer = fallback.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = maskMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            fallback.transform.SetParent(facade, false);
            fallback.transform.localPosition = fallbackCenter;
            fallback.transform.localRotation = Quaternion.identity;
            fallback.transform.localScale = fallbackSize;
            fallbackOwner = facade;
        }

        static Bounds WorldBounds(Transform t, Bounds local)
        {
            var result = new Bounds(t.TransformPoint(local.center), Vector3.zero);
            var e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(t.TransformPoint(corner));
            }
            return result;
        }
    }
}
