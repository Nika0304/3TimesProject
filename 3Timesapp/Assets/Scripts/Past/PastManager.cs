using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;
using ThreeTimes.AR;
using ThreeTimes.Core;
using ThreeTimes.Data;
using ThreeTimes.UI;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Modul Trecut în AR: când cursorul e pe „Trecut”, camera e pornită și localizarea e precisă, fațada Operei din
    /// 1900 apare peste clădirea de azi, ancorată pe teren în fața Operei (Piața Victoriei). Ancora se face o singură
    /// dată pe pornire de cameră; fațada se ascunde când pleci din Trecut și reapare când revii.
    ///
    /// Poziția implicită vine din OpenStreetMap (clădirea Teatrului Național, way 194401985): mijlocul liniei dintre
    /// colțurile din față ale aripilor, orientarea perpendiculară pe ea, spre piață. Se reglează fin pe loc (pasul
    /// următor: modul de aliniere).
    /// </summary>
    public class PastManager : MonoBehaviour
    {
        [SerializeField] ARAnchorManager anchorManager;
        [SerializeField] GeospatialTracker tracker;
        [SerializeField] MainScreenView mainView;
        [SerializeField] UIRouter router;
        [Tooltip("Prefab-ul fațadei (Tools → 3Times → 5)")]
        [SerializeField] GameObject facadePrefab;

        [Header("Unde stă fațada")]
        [Tooltip("Mijlocul fațadei, în planul aripilor, la nivelul trotuarului")]
        [SerializeField] double latitude = 45.75409965;
        [SerializeField] double longitude = 21.2259413;
        [Tooltip("Încotro privește fațada (grade față de nord, în sensul acelor de ceasornic): spre piață")]
        [SerializeField] float heading = 195.3f;
        [Tooltip("Cât de mare e fața de model (modelul are 42 m; clădirea are ~43,5 m între colțuri)")]
        [SerializeField] float scale = 1.035f;
        [Tooltip("Ridicare față de teren (m), dacă baza pare îngropată sau suspendată")]
        [SerializeField] float heightOffset = 0f;

        [Header("Când apare")]
        [Tooltip("Fațada apare doar dacă ești la cel mult atâția metri de ea")]
        [SerializeField] float visibleRadius = 400f;
        [Tooltip("Cât durează apariția (s): fațada crește din pământ")]
        [SerializeField] float riseSeconds = 1.6f;

        GameObject anchor;
        GameObject facade;
        bool resolving;
        bool farHintShown, waitHintShown;
        float shownAt = -1f;
        float wantedSince = -1f;

        void Update()
        {
            if (tracker == null || mainView == null) return;

            // Camera oprită: renunțăm la ancoră (la repornire se face alta).
            if (!tracker.Running)
            {
                Forget();
                return;
            }

            bool want = mainView.Mode == TimeMode.Past && !tracker.Simulated && tracker.Fix.HasValue;
            if (!want)
            {
                Show(false);
                wantedSince = -1f;
                farHintShown = waitHintShown = false;
                return;
            }
            if (wantedSince < 0) wantedSince = Time.realtimeSinceStartup;

            var fix = tracker.Fix.Value;
            float distance = Distance(fix.Latitude, fix.Longitude, latitude, longitude);
            if (distance > visibleRadius)
            {
                Show(false);
                if (!farHintShown)
                {
                    farHintShown = true;
                    router.Toast("Opera din 1900 se vede în Piața Victoriei, în fața Operei. " +
                                 $"Acum ești la {FormatDistance(distance)} de ea.", 5f);
                }
                return;
            }

            if (anchor == null)
            {
                if (!tracker.IsPrecise)
                {
                    if (!waitHintShown && Time.realtimeSinceStartup - wantedSince > 1f)
                    {
                        waitHintShown = true;
                        router.Toast("Îndreaptă telefonul spre Operă și clădirile din jur câteva secunde.", 4f);
                    }
                    return;
                }
                if (!resolving) StartCoroutine(Resolve(fix));
                return;
            }
            Show(true);
        }

        IEnumerator Resolve(GeoFix fix)
        {
            resolving = true;
            var rotation = Quaternion.Euler(0f, heading, 0f);
            var promise = anchorManager.ResolveAnchorOnTerrainAsync(latitude, longitude, heightOffset, rotation);
            yield return promise;

            GameObject result = null;
            string how;
            var r = promise.State == PromiseState.Done ? promise.Result : null;
            if (r != null && r.TerrainAnchorState == TerrainAnchorState.Success && r.Anchor != null)
            {
                result = r.Anchor.gameObject;
                how = "pe teren";
            }
            else
            {
                // Rezervă: altitudinea telefonului minus înălțimea la care e ținut (stai în piață, la nivelul Operei).
                string why = r != null ? r.TerrainAnchorState.ToString() : promise.State.ToString();
                Debug.LogWarning($"[3Times] Fațada: ancora pe teren a eșuat ({why}); folosim altitudinea telefonului");
                var fallback = anchorManager.AddAnchor(latitude, longitude, fix.Altitude - 1.4 + heightOffset, rotation);
                result = fallback != null ? fallback.gameObject : null;
                how = $"după altitudinea telefonului ({why})";
            }
            resolving = false;

            if (result == null)
            {
                TestLog.Add("Fațada 1900: ancora a eșuat");
                router.Toast("Nu am putut așeza fațada. Mai încearcă din Prezent în Trecut.", 4f);
                yield break;
            }
            if (!tracker.Running)
            {
                Destroy(result);
                yield break;
            }

            anchor = result;
            facade = Instantiate(facadePrefab, anchor.transform);
            facade.name = "Fatada Opera 1900";
            facade.transform.localPosition = Vector3.zero;
            facade.transform.localRotation = Quaternion.identity;
            facade.transform.localScale = new Vector3(scale, 0.001f, scale);
            facade.SetActive(false);
            float distance = Distance(fix.Latitude, fix.Longitude, latitude, longitude);
            TestLog.Add($"Fațada 1900 ancorată {how}, la {distance:0} m · ±{fix.HorizontalAccuracy:0.0} m · ±{fix.YawAccuracy:0}°");
            Debug.Log($"[3Times] Fațada 1900 ancorată {how}, la {distance:0} m");
        }

        void Show(bool on)
        {
            if (facade == null) return;
            if (on && !facade.activeSelf)
            {
                facade.SetActive(true);
                shownAt = Time.realtimeSinceStartup;
            }
            else if (!on && facade.activeSelf)
            {
                facade.SetActive(false);
            }
            if (on)
            {
                // Fațada „crește” din pământ, cu încetinire la final.
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - shownAt) / Mathf.Max(0.01f, riseSeconds));
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                facade.transform.localScale = new Vector3(scale, scale * Mathf.Max(0.001f, eased), scale);
            }
        }

        void Forget()
        {
            if (anchor != null) Destroy(anchor);
            anchor = null;
            facade = null;
            resolving = false;
            wantedSince = -1f;
            farHintShown = waitHintShown = false;
        }

        static float Distance(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Mathf.Deg2Rad, dLon = (lon2 - lon1) * Mathf.Deg2Rad;
            double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                       System.Math.Cos(lat1 * Mathf.Deg2Rad) * System.Math.Cos(lat2 * Mathf.Deg2Rad) *
                       System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
            return (float)(6371000 * 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a)));
        }

        static string FormatDistance(float m) => m < 1000 ? $"{m:0} m" : $"{m / 1000:0.0} km";
    }
}
