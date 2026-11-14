using System;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Core;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul principal (macheta 1.2, modul Prezent): indicatorul de localizare,
    /// meniul, butoanele de acțiune și cursorul timpului. Deocamdată doar Prezentul
    /// e activ; Trecutul și Viitorul se deblochează în etapele următoare.
    /// </summary>
    public class MainScreenView : MonoBehaviour
    {
        [SerializeField] UIRouter router;
        [SerializeField] GeospatialTracker tracker;

        [Header("Moduri disponibile acum")]
        [SerializeField] bool pastAvailable = false;
        [SerializeField] bool presentAvailable = true;
        [SerializeField] bool futureAvailable = false;

        public TimeMode Mode { get; private set; } = TimeMode.Present;

        /// <summary>Se declanșează când utilizatorul alege un mod disponibil.</summary>
        public event Action<TimeMode> ModeChanged;

        // Acțiunile ecranului. Pașii următori se abonează aici (de ex. „Lasă o urmă”, pasul 4).
        public event Action LeaveTraceRequested;
        public event Action MapRequested;
        public event Action PostcardRequested;

        VisualElement screenMain, screenMenu, screenAbout;
        VisualElement statusDot;
        Label statusLabel, modeHint;
        Button actionPrimary;
        VisualElement slider, track, knob;
        Button labelPast, labelPresent, labelFuture;

        bool dragging;
        bool initialized;

        public void Init()
        {
            if (initialized) return;
            initialized = true;
            router.Init();
            var root = router.Root;

            screenMain = root.Q("screen-main");
            screenMenu = root.Q("screen-menu");
            screenAbout = root.Q("screen-about");

            statusDot = root.Q("status-dot");
            statusLabel = root.Q<Label>("status-label");
            modeHint = root.Q<Label>("mode-hint");
            actionPrimary = root.Q<Button>("action-primary");

            slider = root.Q("time-slider");
            track = root.Q("time-track");
            knob = root.Q("time-knob");
            labelPast = root.Q<Button>("time-past");
            labelPresent = root.Q<Button>("time-present");
            labelFuture = root.Q<Button>("time-future");

            // Butoane
            root.Q<Button>("menu-button").clicked += OpenMenu;
            root.Q<Button>("menu-close").clicked += CloseMenu;
            root.Q<Button>("menu-account").clicked += () => router.Toast("Contul (opțional) vine într-o etapă următoare.");
            root.Q<Button>("menu-about").clicked += OpenAbout;
            root.Q<Button>("about-back").clicked += CloseAbout;

            actionPrimary.clicked += OnPrimaryAction;
            root.Q<Button>("action-map").clicked += () =>
            {
                if (MapRequested != null) MapRequested.Invoke();
                else router.Toast("Harta vine într-o etapă următoare.");
            };
            root.Q<Button>("action-postcard").clicked += () =>
            {
                if (PostcardRequested != null) PostcardRequested.Invoke();
                else router.Toast("Time postcard vine într-o etapă următoare.");
            };

            // Cursorul timpului: tragere pe bară + atingere pe etichete
            slider.RegisterCallback<PointerDownEvent>(OnPointerDown);
            slider.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            slider.RegisterCallback<PointerUpEvent>(OnPointerUp);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            labelPast.clicked += () => TrySelect(TimeMode.Past);
            labelPresent.clicked += () => TrySelect(TimeMode.Present);
            labelFuture.clicked += () => TrySelect(TimeMode.Future);

            // Indicatorul de localizare se actualizează de două ori pe secundă.
            root.schedule.Execute(UpdateStatus).Every(500);

            ApplyMode(Mode);
        }

        void Update()
        {
            // Butonul „Înapoi” de pe Android
            if (!initialized || !Input.GetKeyDown(KeyCode.Escape)) return;
            if (IsVisible(screenAbout)) CloseAbout();
            else if (IsVisible(screenMenu)) CloseMenu();
        }

        // ---------------------------------------------------------------- stare localizare

        void UpdateStatus()
        {
            if (tracker == null || !IsVisible(screenMain)) return;

            var fix = tracker.Fix;
            string accuracy = fix.HasValue ? $" · ±{fix.Value.HorizontalAccuracy:0} m" : "";

            statusDot.EnableInClassList("dot--good", tracker.Quality == LocalizationQuality.Good);
            statusDot.EnableInClassList("dot--low", tracker.Quality == LocalizationQuality.Low);
            statusDot.EnableInClassList("dot--none", tracker.Quality == LocalizationQuality.None);

            switch (tracker.Quality)
            {
                case LocalizationQuality.Good: statusLabel.text = "localizat" + accuracy; break;
                case LocalizationQuality.Low: statusLabel.text = "precizie scăzută" + accuracy; break;
                default: statusLabel.text = "se localizează…"; break;
            }
        }

        // ---------------------------------------------------------------- acțiuni

        void OnPrimaryAction()
        {
            if (Mode == TimeMode.Present)
            {
                if (LeaveTraceRequested != null) LeaveTraceRequested.Invoke();
                else router.Toast("„Lasă o urmă” urmează în pasul 4.");
            }
        }

        void OpenMenu() => UIRouter.SetVisible(screenMenu, true);
        void CloseMenu() => UIRouter.SetVisible(screenMenu, false);
        void OpenAbout() => UIRouter.SetVisible(screenAbout, true);
        void CloseAbout() => UIRouter.SetVisible(screenAbout, false);

        static bool IsVisible(VisualElement e) => e != null && !e.ClassListContains("hidden");

        // ---------------------------------------------------------------- cursorul timpului

        bool IsAvailable(TimeMode m) =>
            m == TimeMode.Past ? pastAvailable :
            m == TimeMode.Future ? futureAvailable : presentAvailable;

        static float StopOf(TimeMode m) =>
            m == TimeMode.Past ? 0f : m == TimeMode.Future ? 1f : 0.5f;

        static TimeMode NearestMode(float t) =>
            t < 0.25f ? TimeMode.Past : t > 0.75f ? TimeMode.Future : TimeMode.Present;

        void OnPointerDown(PointerDownEvent evt)
        {
            dragging = true;
            slider.CapturePointer(evt.pointerId);
            knob.AddToClassList("time-knob--dragging");
            SetKnob(PointerToTrack(evt.position));
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!dragging || !slider.HasPointerCapture(evt.pointerId)) return;
            SetKnob(PointerToTrack(evt.position));
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!dragging) return;
            var target = NearestMode(PointerToTrack(evt.position));
            EndDrag(target); // întâi încheiem tragerea, ca PointerCaptureOut să nu o mai încheie a doua oară
            if (slider.HasPointerCapture(evt.pointerId)) slider.ReleasePointer(evt.pointerId);
        }

        void EndDrag(TimeMode? target = null)
        {
            if (!dragging) return;
            dragging = false;
            knob.RemoveFromClassList("time-knob--dragging");
            TrySelect(target ?? Mode);
        }

        float PointerToTrack(Vector3 panelPosition)
        {
            float width = track.layout.width;
            if (float.IsNaN(width) || width <= 0f) return StopOf(Mode);
            return Mathf.Clamp01(track.WorldToLocal(panelPosition).x / width);
        }

        void SetKnob(float t) => knob.style.left = Length.Percent(t * 100f);

        void TrySelect(TimeMode m)
        {
            if (!IsAvailable(m))
            {
                router.Toast(m == TimeMode.Past
                    ? "Trecutul (fațadele istorice și 1989) vine în etapa următoare."
                    : "Capsulele timpului vin după urme.");
                ApplyMode(Mode); // cursorul revine la modul curent
                return;
            }

            bool changed = m != Mode;
            ApplyMode(m);
            if (changed) ModeChanged?.Invoke(m);
        }

        void ApplyMode(TimeMode m)
        {
            Mode = m;
            SetKnob(StopOf(m));

            labelPast.EnableInClassList("time-label--active", m == TimeMode.Past);
            labelPresent.EnableInClassList("time-label--active", m == TimeMode.Present);
            labelFuture.EnableInClassList("time-label--active", m == TimeMode.Future);
            labelPast.EnableInClassList("time-label--locked", !pastAvailable);
            labelPresent.EnableInClassList("time-label--locked", !presentAvailable);
            labelFuture.EnableInClassList("time-label--locked", !futureAvailable);

            // Textele din machete (1.1, 1.2, 1.3)
            switch (m)
            {
                case TimeMode.Past:
                    modeHint.text = "Mută cursorul ca să vezi alți ani";
                    actionPrimary.text = "Ghid vocal";
                    break;
                case TimeMode.Future:
                    modeHint.text = "Atinge o capsulă ca s-o deschizi (dacă ești în loc și data a venit)";
                    actionPrimary.text = "+ Creează capsulă";
                    break;
                default:
                    modeHint.text = "Atinge o urmă ca s-o deschizi";
                    actionPrimary.text = "+ Lasă o urmă";
                    break;
            }
        }
    }
}
