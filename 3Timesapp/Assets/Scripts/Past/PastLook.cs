using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Aspectul „de epocă” al modului Trecut: cât timp se vede fațada din 1900, toată imaginea (camera + fațada)
    /// trece treptat în sepia, cu granulație și margini întunecate (un Volume URP cu Color Adjustments, Film Grain
    /// și Vignette). Lumina scenei urmează lumina reală măsurată de ARCore, ca fațada să nu pară luminată ca la
    /// prânz când afară se înserează. În Prezent, post-procesarea e oprită (nu costă nimic).
    /// </summary>
    public class PastLook : MonoBehaviour
    {
        [SerializeField] PastManager past;
        [SerializeField] ARCameraManager cameraManager;
        [Tooltip("Volume-ul global cu efectele de epocă (îl creează Tools → 3Times → 7)")]
        [SerializeField] Volume volume;
        [Tooltip("Lumina principală a scenei (Directional Light)")]
        [SerializeField] Light sun;

        [Tooltip("Cât durează trecerea în sepia și înapoi (s)")]
        [SerializeField] float fadeSeconds = 1.4f;
        [Tooltip("Luminozitatea medie ARCore considerată „zi normală” (0..1)")]
        [SerializeField] float daylightBrightness = 0.45f;
        [Tooltip("Intensitatea luminii pe întuneric, față de cea de zi")]
        [SerializeField] float nightFactor = 0.3f;

        [Header("Filtrul (peste profilul din pasul 7)")]
        [Tooltip("Expunerea: 0 = ca imaginea camerei; puțin peste 0 pe zi înnorată")]
        [SerializeField] float exposure = 0.1f;
        [Range(0f, 1f)] [SerializeField] float vignette = 0.22f;
        [Range(-100f, 0f)] [SerializeField] float saturation = -70f;
        [Range(0f, 50f)] [SerializeField] float contrast = 6f;
        [Range(0f, 1f)] [SerializeField] float grain = 0.35f;

        /// <summary>Lumina din jur, 0 (întuneric) … 1 (zi), netezită. 0,7 până la prima măsurătoare.</summary>
        public float Brightness { get; private set; } = 0.7f;

        /// <summary>Cât de „vechi” e imaginea acum, 0 (Prezent) … 1 (Trecut).</summary>
        public float Weight => weight;

        float weight;
        float baseIntensity = 1f;
        Camera arCamera;
        UniversalAdditionalCameraData cameraData;
        bool measured;

        void Start()
        {
            if (sun != null) baseIntensity = sun.intensity;
            if (volume != null)
            {
                volume.weight = 0f;
                Tune();
            }
            if (cameraManager != null)
            {
                arCamera = cameraManager.GetComponent<Camera>();
                if (arCamera != null) cameraData = arCamera.GetUniversalAdditionalCameraData();
                cameraManager.requestedLightEstimation |= LightEstimation.AmbientIntensity;
                cameraManager.frameReceived += OnFrame;
            }
        }

        void OnDestroy()
        {
            if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
        }

        void OnFrame(ARCameraFrameEventArgs args)
        {
            float? average = args.lightEstimation.averageBrightness;
            if (!average.HasValue) return;
            float target = Mathf.Clamp01(average.Value / Mathf.Max(0.05f, daylightBrightness));
            Brightness = measured ? Mathf.Lerp(Brightness, target, 0.05f) : target;
            measured = true;
        }

        void Update()
        {
            float target = past != null && past.Active ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, target, Time.deltaTime / Mathf.Max(0.05f, fadeSeconds));

            if (volume != null) volume.weight = weight;
            if (cameraData != null) cameraData.renderPostProcessing = weight > 0.001f;
            if (sun != null)
            {
                float measuredIntensity = baseIntensity * Mathf.Lerp(nightFactor, 1f, Brightness);
                sun.intensity = Mathf.Lerp(baseIntensity, measuredIntensity, weight);
            }
        }

        // Profilul din pasul 7 era prea întunecat pentru o zi înnorată (cerul ieșea maro-cenușiu): îl reglăm aici,
        // pe o copie (volume.profile), ca să nu schimbăm fișierul.
        void Tune()
        {
            var profile = volume.profile;
            if (profile == null) return;
            if (profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments color))
            {
                color.postExposure.Override(exposure);
                color.saturation.Override(saturation);
                color.contrast.Override(contrast);
            }
            if (profile.TryGet(out UnityEngine.Rendering.Universal.Vignette v)) v.intensity.Override(vignette);
            if (profile.TryGet(out UnityEngine.Rendering.Universal.FilmGrain g)) g.intensity.Override(grain);
        }
    }
}
