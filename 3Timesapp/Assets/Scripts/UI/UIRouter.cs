using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Core;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Arată ecranul potrivit stării aplicației, ține zona sigură (notch, bara de jos)
    /// și afișează mesajele scurte (toast). Tot UI-ul e într-un singur App.uxml.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIRouter : MonoBehaviour
    {
        [Tooltip("Marginea de bază a ecranelor, în px de referință (peste ea se adaugă zona sigură).")]
        [SerializeField] float basePadding = 40f;

        public VisualElement Root { get; private set; }

        public event Action OnboardingContinue;
        public event Action LocalizingRetry;
        public event Action LocalizingContinue;
        public event Action ErrorPrimary;
        public event Action ErrorSecondary;

        readonly Dictionary<AppState, VisualElement> screens = new Dictionary<AppState, VisualElement>();
        readonly List<VisualElement> overlays = new List<VisualElement>();
        readonly List<VisualElement> spinners = new List<VisualElement>();

        Label checkingStatus, localizingAccuracy, errorTitle, errorMessage, toast;
        VisualElement localizingTimeout;
        Button errorPrimary, errorSecondary;
        IVisualElementScheduledItem toastHide;

        Rect lastSafeArea;
        Vector2 lastPanelSize;
        float spinnerAngle;
        bool initialized;

        public void Init()
        {
            if (initialized) return;
            initialized = true;

            Root = GetComponent<UIDocument>().rootVisualElement;

            screens[AppState.Splash] = Root.Q("screen-splash");
            screens[AppState.Onboarding] = Root.Q("screen-onboarding");
            screens[AppState.Checking] = Root.Q("screen-checking");
            screens[AppState.Localizing] = Root.Q("screen-localizing");
            screens[AppState.Error] = Root.Q("screen-error");
            screens[AppState.Main] = Root.Q("screen-main");
            overlays.Add(Root.Q("screen-menu"));
            overlays.Add(Root.Q("screen-about"));

            checkingStatus = Root.Q<Label>("checking-status");
            localizingAccuracy = Root.Q<Label>("localizing-accuracy");
            localizingTimeout = Root.Q("localizing-timeout");
            errorTitle = Root.Q<Label>("error-title");
            errorMessage = Root.Q<Label>("error-message");
            errorPrimary = Root.Q<Button>("error-primary");
            errorSecondary = Root.Q<Button>("error-secondary");
            toast = Root.Q<Label>("toast");

            spinners.Add(Root.Q("checking-spinner"));
            spinners.Add(Root.Q("localizing-spinner"));

            Root.Q<Button>("onboarding-continue").clicked += () => OnboardingContinue?.Invoke();
            Root.Q<Button>("localizing-retry").clicked += () => LocalizingRetry?.Invoke();
            Root.Q<Button>("localizing-continue").clicked += () => LocalizingContinue?.Invoke();
            errorPrimary.clicked += () => ErrorPrimary?.Invoke();
            errorSecondary.clicked += () => ErrorSecondary?.Invoke();

            // Rotim indicatoarele de încărcare.
            Root.schedule.Execute(RotateSpinners).Every(16);
        }

        // ---------------------------------------------------------------- ecrane

        public void Show(AppState state)
        {
            foreach (var pair in screens)
                SetVisible(pair.Value, pair.Key == state);
            foreach (var overlay in overlays)
                SetVisible(overlay, false);
        }

        public void SetCheckingStatus(string text) => checkingStatus.text = text;

        public void SetLocalizingTimedOut(bool timedOut) => SetVisible(localizingTimeout, timedOut);

        public void SetLocalizingAccuracy(GeoFix? fix)
        {
            localizingAccuracy.text = fix.HasValue
                ? $"Precizie: ±{fix.Value.HorizontalAccuracy:0} m poziție · ±{fix.Value.YawAccuracy:0}° orientare"
                : "Precizie: încă nu avem semnal";
        }

        public void ShowError(ErrorInfo info)
        {
            errorTitle.text = info.Title;
            errorMessage.text = info.Message;
            errorPrimary.text = info.PrimaryLabel;
            SetVisible(errorSecondary, !string.IsNullOrEmpty(info.SecondaryLabel));
            if (!string.IsNullOrEmpty(info.SecondaryLabel)) errorSecondary.text = info.SecondaryLabel;
        }

        public void Toast(string message, float seconds = 2.5f)
        {
            toast.text = message;
            SetVisible(toast, true);
            toastHide?.Pause();
            toastHide = toast.schedule.Execute(() => SetVisible(toast, false))
                                      .StartingIn((long)(seconds * 1000));
        }

        public static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.EnableInClassList("hidden", !visible);
        }

        // ---------------------------------------------------------------- zona sigură

        void Update()
        {
            if (!initialized || Root.panel == null) return;

            var panelSize = Root.layout.size;
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0f) return;

            if (Screen.safeArea != lastSafeArea || panelSize != lastPanelSize)
            {
                lastSafeArea = Screen.safeArea;
                lastPanelSize = panelSize;
                ApplySafeArea();
            }
        }

        void ApplySafeArea()
        {
            var safe = Screen.safeArea;
            // Screen are originea jos-stânga, panoul UI sus-stânga.
            var topLeft = RuntimePanelUtils.ScreenToPanel(Root.panel,
                new Vector2(safe.xMin, Screen.height - safe.yMax));
            var bottomRight = RuntimePanelUtils.ScreenToPanel(Root.panel,
                new Vector2(safe.xMax, Screen.height - safe.yMin));

            float left = Mathf.Max(0f, topLeft.x);
            float top = Mathf.Max(0f, topLeft.y);
            float right = Mathf.Max(0f, lastPanelSize.x - bottomRight.x);
            float bottom = Mathf.Max(0f, lastPanelSize.y - bottomRight.y);

            Root.Query(className: "screen").ForEach(screen =>
            {
                screen.style.paddingLeft = basePadding + left;
                screen.style.paddingTop = basePadding + top;
                screen.style.paddingRight = basePadding + right;
                screen.style.paddingBottom = basePadding + bottom;
            });
        }

        void RotateSpinners()
        {
            spinnerAngle = (spinnerAngle + 6f) % 360f;
            var rotation = new Rotate(new Angle(spinnerAngle, AngleUnit.Degree));
            foreach (var spinner in spinners)
                if (spinner != null) spinner.style.rotate = rotation;
        }
    }
}
