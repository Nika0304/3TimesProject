using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;

namespace ThreeTimes.AR
{
    public enum LocalizationQuality
    {
        None, // nu avem încă poziție
        Low,  // avem poziție, dar prea imprecisă pentru fațade și urme
        Good  // destul de precis ca să ancorăm conținut
    }

    /// <summary>Poziția curentă a telefonului pe glob, cu precizia ei.</summary>
    public struct GeoFix
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double HorizontalAccuracy; // metri
        public double VerticalAccuracy;   // metri
        public double YawAccuracy;        // grade
    }

    /// <summary>
    /// Citește în fiecare cadru poziția din AREarthManager și o transformă într-o
    /// calitate simplă (None / Low / Good) pentru interfață. În Editor poate simula
    /// o localizare în Piața Victoriei, ca să testăm ecranele fără telefon.
    /// </summary>
    public class GeospatialTracker : MonoBehaviour
    {
        [SerializeField] AREarthManager earthManager;

        [Header("Praguri de precizie")]
        [Tooltip("Sub această eroare orizontală (m) considerăm localizarea bună.")]
        [SerializeField] double goodHorizontalAccuracy = 10;
        [Tooltip("Sub această eroare de orientare (grade) considerăm localizarea bună.")]
        [SerializeField] double goodYawAccuracy = 15;
        [Tooltip("Odată bună, localizarea se pierde abia când eroarea crește peste prag × acest factor.")]
        [SerializeField] double hysteresis = 1.5;

        [Header("Simulare în Editor (Piața Victoriei)")]
        [SerializeField] double simLatitude = 45.75270;
        [SerializeField] double simLongitude = 21.22530;
        [SerializeField] double simAltitude = 90;
        [SerializeField] float simSecondsToLow = 1f;
        [SerializeField] float simSecondsToGood = 3f;

        public bool Running { get; private set; }
        public bool Simulated { get; private set; }
        public LocalizationQuality Quality { get; private set; } = LocalizationQuality.None;
        public GeoFix? Fix { get; private set; }

        public event Action<LocalizationQuality> QualityChanged;

        float simStartTime;

        public void Begin(bool simulated)
        {
            Simulated = simulated;
            Running = true;
            simStartTime = Time.realtimeSinceStartup;
            Fix = null;
            SetQuality(LocalizationQuality.None);
        }

        public void Stop()
        {
            Running = false;
            Fix = null;
            SetQuality(LocalizationQuality.None);
        }

        public FeatureSupported GeospatialSupport =>
            Simulated ? FeatureSupported.Supported
                      : earthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);

        public EarthState EarthState =>
            Simulated ? EarthState.Enabled : earthManager.EarthState;

        void Update()
        {
            if (!Running) return;

            if (Simulated)
            {
                UpdateSimulated();
                return;
            }

            if (earthManager == null || !earthManager.isActiveAndEnabled ||
                ARSession.state != ARSessionState.SessionTracking ||
                earthManager.EarthTrackingState != TrackingState.Tracking)
            {
                Fix = null;
                SetQuality(LocalizationQuality.None);
                return;
            }

            var pose = earthManager.CameraGeospatialPose;
            var fix = new GeoFix
            {
                Latitude = pose.Latitude,
                Longitude = pose.Longitude,
                Altitude = pose.Altitude,
                HorizontalAccuracy = pose.HorizontalAccuracy,
                VerticalAccuracy = pose.VerticalAccuracy,
                YawAccuracy = pose.OrientationYawAccuracy
            };
            Fix = fix;
            SetQuality(Evaluate(fix));
        }

        LocalizationQuality Evaluate(GeoFix fix)
        {
            double factor = Quality == LocalizationQuality.Good ? hysteresis : 1.0;
            bool good = fix.HorizontalAccuracy <= goodHorizontalAccuracy * factor &&
                        fix.YawAccuracy <= goodYawAccuracy * factor;
            return good ? LocalizationQuality.Good : LocalizationQuality.Low;
        }

        void UpdateSimulated()
        {
            float t = Time.realtimeSinceStartup - simStartTime;
            if (t < simSecondsToLow)
            {
                Fix = null;
                SetQuality(LocalizationQuality.None);
                return;
            }

            // Precizia se îmbunătățește treptat, ca pe telefon.
            float k = Mathf.Clamp01((t - simSecondsToLow) / Mathf.Max(0.01f, simSecondsToGood - simSecondsToLow));
            var fix = new GeoFix
            {
                Latitude = simLatitude,
                Longitude = simLongitude,
                Altitude = simAltitude,
                HorizontalAccuracy = Mathf.Lerp(25f, 3f, k),
                VerticalAccuracy = Mathf.Lerp(10f, 2f, k),
                YawAccuracy = Mathf.Lerp(40f, 5f, k)
            };
            Fix = fix;
            SetQuality(Evaluate(fix));
        }

        void SetQuality(LocalizationQuality q)
        {
            if (q == Quality) return;
            Quality = q;
            Debug.Log($"[3Times] Localizare: {q}");
            QualityChanged?.Invoke(q);
        }
    }
}
