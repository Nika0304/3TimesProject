using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Google.XR.ARCoreExtensions;
using ThreeTimes.AR;
using ThreeTimes.Platform;
using ThreeTimes.UI;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Dirijează deschiderea aplicației:
    /// Splash → Onboarding și permisiuni → Verificări → Localizare → Ecranul principal.
    /// Orice problemă duce la ecranul de eroare, de unde se poate reîncerca.
    /// </summary>
    public class AppController : MonoBehaviour
    {
        [Header("Referințe")]
        [SerializeField] UIRouter ui;
        [SerializeField] MainScreenView mainView;
        [SerializeField] GeospatialTracker tracker;
        [Tooltip("Obiectul-părinte cu AR Session, XR Origin și ARCore Extensions. Se activează după permisiuni.")]
        [SerializeField] GameObject arRoot;

        [Header("Timpi (secunde)")]
        [SerializeField] float splashSeconds = 1f;
        [SerializeField] float locationTimeout = 15f;
        [SerializeField] float vpsTimeout = 10f;
        [SerializeField] float geospatialSupportTimeout = 15f;
        [Tooltip("După cât timp de căutare apar butoanele „Mai încerc” și „Continuă oricum”.")]
        [SerializeField] float localizationTimeout = 60f;
        [Tooltip("Cât timp trebuie să rămână localizarea bună înainte să intrăm pe ecranul principal.")]
        [SerializeField] float stableGoodSeconds = 1f;

        [Header("Dezvoltare")]
        [Tooltip("În Editor sare peste ARCore și simulează localizarea, ca să testăm ecranele fără telefon.")]
        [SerializeField] bool simulateInEditor = true;

        public AppState State { get; private set; }

        bool Simulated => Application.isEditor && simulateInEditor;

        Coroutine flow;
        bool continueRequested;
        bool retryRequested;
        bool vpsUnavailable;
        AppErrorKind? currentError;
        bool waitingForSettings;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (arRoot != null) arRoot.SetActive(false);
        }

        void Start()
        {
            ui.Simulated = Simulated;
            ui.Init();
            mainView.Init();

            ui.OnboardingContinue += () => continueRequested = true;
            ui.LocalizingRetry += () => retryRequested = true;
            ui.LocalizingContinue += () => continueRequested = true;
            ui.ErrorPrimary += OnErrorPrimary;
            ui.ErrorSecondary += Restart;

            flow = StartCoroutine(Startup(showSplash: true));
        }

        void OnApplicationFocus(bool focused)
        {
            // Utilizatorul s-a întors din setări: încercăm din nou automat.
            if (focused && waitingForSettings && State == AppState.Error)
            {
                waitingForSettings = false;
                Restart();
            }
        }

        // ---------------------------------------------------------------- fluxul

        IEnumerator Startup(bool showSplash)
        {
            if (showSplash)
            {
                Go(AppState.Splash);
                yield return new WaitForSecondsRealtime(splashSeconds);
            }

            // 1. Ecranul de pornire apare la fiecare deschidere; „Începe” duce mai departe.
            //    La o reîncercare după o eroare nu-l mai arătăm.
            if (showSplash)
            {
                Go(AppState.Onboarding);
                continueRequested = false;
                yield return new WaitUntil(() => continueRequested);
                AppPrefs.OnboardingSeen = true;
            }

            // Permisiunile se cer doar dacă lipsesc.
            if (!PermissionService.HasAll())
            {
                bool granted = false;
                yield return PermissionService.RequestAll(ok => granted = ok);
                if (!granted) { Fail(AppErrorKind.PermissionsDenied); yield break; }
            }

            // 2. Verificări înainte de AR
            Go(AppState.Checking);
            if (!Simulated)
            {
                ui.SetCheckingStatus("Verificăm conexiunea…");
                if (!ArSupportChecker.HasInternet) { Fail(AppErrorKind.NoInternet); yield break; }

                ui.SetCheckingStatus("Verificăm suportul AR…");
                AppErrorKind? arError = null;
                yield return ArSupportChecker.CheckArCore(e => arError = e);
                if (arError.HasValue) { Fail(arError.Value); yield break; }

                ui.SetCheckingStatus("Pornim locația…");
                bool locationOk = false;
                yield return DeviceLocation.StartService(locationTimeout, ok => locationOk = ok);
                if (!locationOk) { Fail(AppErrorKind.LocationOff); yield break; }
            }
            else
            {
                ui.SetCheckingStatus("Simulare în Editor…");
                yield return new WaitForSecondsRealtime(0.6f);
            }

            // 3. Pornim sesiunea AR
            if (arRoot != null) arRoot.SetActive(true);
            tracker.Begin(Simulated);

            if (!Simulated)
            {
                ui.SetCheckingStatus("Verificăm acoperirea zonei…");
                var gps = Input.location.lastData;
                VpsAvailability? vps = null;
                yield return VpsChecker.Check(gps.latitude, gps.longitude, vpsTimeout, r => vps = r);
                if (vps == VpsAvailability.ErrorNotAuthorized) { Fail(AppErrorKind.NotAuthorized); yield break; }
                if (vps == VpsAvailability.ErrorNetworkConnection) { Fail(AppErrorKind.NoInternet); yield break; }
                vpsUnavailable = vps == VpsAvailability.Unavailable;

                ui.SetCheckingStatus("Pregătim camera…");
                float elapsed = 0f;
                while (tracker.GeospatialSupport == FeatureSupported.Unknown && elapsed < geospatialSupportTimeout)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                var support = tracker.GeospatialSupport;
                if (support == FeatureSupported.Unsupported) { Fail(AppErrorKind.GeospatialUnsupported); yield break; }
                if (support != FeatureSupported.Supported) { Fail(AppErrorKind.LocalizationServiceError); yield break; }
            }

            // 4. Localizare
            yield return Localize();
        }

        IEnumerator Localize()
        {
            Go(AppState.Localizing);
            if (vpsUnavailable)
                ui.Toast("Zona nu are acoperire VPS: localizarea se bazează pe GPS și poate fi mai puțin precisă.", 5f);

            float started = Time.realtimeSinceStartup;
            float goodSince = -1f;
            bool timedOut = false;
            continueRequested = false;
            retryRequested = false;
            ui.SetLocalizingTimedOut(false);

            while (true)
            {
                float now = Time.realtimeSinceStartup;

                if (!Simulated && ARSession.state == ARSessionState.SessionTracking &&
                    IsFatal(tracker.EarthState))
                {
                    Debug.LogWarning($"[3Times] EarthState: {tracker.EarthState}");
                    Fail(tracker.EarthState == EarthState.ErrorNotAuthorized
                        ? AppErrorKind.NotAuthorized
                        : AppErrorKind.LocalizationServiceError);
                    yield break;
                }

                ui.SetLocalizingAccuracy(tracker.Fix);

                if (tracker.Quality == LocalizationQuality.Good)
                {
                    if (goodSince < 0f) goodSince = now;
                    if (now - goodSince >= stableGoodSeconds) break;
                }
                else
                {
                    goodSince = -1f;
                }

                if (!timedOut && now - started > localizationTimeout)
                {
                    timedOut = true;
                    ui.SetLocalizingTimedOut(true);
                }

                if (retryRequested)
                {
                    retryRequested = false;
                    timedOut = false;
                    started = now;
                    ui.SetLocalizingTimedOut(false);
                }

                if (continueRequested) break;

                yield return null;
            }

            float seconds = Time.realtimeSinceStartup - started;
            Debug.Log($"[3Times] Localizat în {seconds:F1} s, calitate {tracker.Quality}");
            Go(AppState.Main);
        }

        // ---------------------------------------------------------------- ajutătoare

        /// <summary>
        /// ErrorEarthNotReady și ErrorSessionNotReady apar normal câteva cadre la pornire,
        /// deci nu sunt erori. Restul înseamnă că Geospatial nu poate funcționa.
        /// </summary>
        static bool IsFatal(EarthState state)
        {
            switch (state)
            {
                case EarthState.ErrorInternal:
                case EarthState.ErrorGeospatialModeDisabled:
                case EarthState.ErrorNotAuthorized:
                case EarthState.ErrorResourcesExhausted:
                case EarthState.ErrorPackageTooOld:
                    return true;
                default:
                    return false;
            }
        }

        void Go(AppState state)
        {
            State = state;
            if (state != AppState.Error) currentError = null;
            ui.Show(state);
            Debug.Log($"[3Times] Stare: {state}");
        }

        void Fail(AppErrorKind kind)
        {
            Debug.LogWarning($"[3Times] Eroare: {kind}");
            currentError = kind;
            ui.ShowError(AppErrors.Describe(kind));
            Go(AppState.Error);
        }

        void OnErrorPrimary()
        {
            if (!currentError.HasValue) { Restart(); return; }

            switch (AppErrors.Describe(currentError.Value).PrimaryAction)
            {
                case ErrorAction.OpenAppSettings:
                    waitingForSettings = true;
                    AndroidIntents.OpenAppSettings();
                    break;
                case ErrorAction.OpenLocationSettings:
                    waitingForSettings = true;
                    AndroidIntents.OpenLocationSettings();
                    break;
                default:
                    Restart();
                    break;
            }
        }

        void Restart()
        {
            if (flow != null) StopCoroutine(flow);
            flow = StartCoroutine(Startup(showSplash: false));
        }

        [ContextMenu("Resetează onboarding-ul")]
        void ResetOnboarding()
        {
            AppPrefs.ResetAll();
            Debug.Log("[3Times] Onboarding resetat. La următoarea pornire apare din nou.");
        }
    }
}
