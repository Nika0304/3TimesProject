using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Core;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul principal (macheta „Prezent” din canvas): modul curent mare („acum”, „1989”, „2030”),
    /// indicatorul de localizare, meniul, butoanele de acțiune și cursorul timpului.
    /// Cursorul schimbă deja aspectul celor trei moduri; conținutul Trecutului și Viitorului
    /// vine în etapele următoare, iar până atunci butonul lor principal afișează un mesaj.
    /// </summary>
    public class MainScreenView : MonoBehaviour
    {
        [SerializeField] UIRouter router;
        [SerializeField] GeospatialTracker tracker;

        [Header("Funcții gata (restul afișează „în curând”)")]
        [SerializeField] bool pastReady = false;
        [SerializeField] bool futureReady = false;

        public TimeMode Mode { get; private set; } = TimeMode.Present;

        /// <summary>Se declanșează când utilizatorul schimbă modul de pe cursor.</summary>
        public event Action<TimeMode> ModeChanged;

        // Acțiunile ecranului. Celelalte componente se abonează aici.
        /// <summary>Utilizatorul a completat și a confirmat o urmă nouă.</summary>
        public event Action<TraceDraft> TraceSubmitted;
        public event Action GuideRequested;
        public event Action CreateCapsuleRequested;
        public event Action MapRequested;
        public event Action PostcardRequested;
        public event Action CameraRequested;
        public event Action CameraOffRequested;
        public event Action ClearTracesRequested;
        /// <summary>Atingere în zona liberă a ecranului, în coordonate de ecran (pentru urmele din AR).</summary>
        public event Action<Vector2> WorldTapped;
        public event Action<ThreeTimes.Data.SavedTrace> TraceReportRequested;
        public event Action<ThreeTimes.Data.SavedTrace> TraceDeleteRequested;

        public bool CameraOn { get; private set; }

        /// <summary>
        /// Întrebat înainte să se trimită o urmă: mesajul care spune de ce nu se poate încă, sau null.
        /// Îl setează TraceManager.
        /// </summary>
        public Func<string> TraceSubmitCheck { get; set; }

        ThreeTimes.Media.VoicePlayer voicePlayer;

        VisualElement screenLog;
        Label logText;
        VisualElement screenMain, screenMenu, screenAbout, pastTint, cameraPrompt, simTraces;
        Button cameraOff;
        VisualElement statusDot;
        Label statusLabel, modeLabel, modeWord, modeHint;
        Button actionPrimary;
        VisualElement slider, track, knob;
        Button labelPast, labelPresent, labelFuture;

        TraceSheetView traceSheet;
        TraceDetailView traceDetail;
        int traceCount;
        bool dragging;
        // Nu se serializează: după o recompilare în Play, Init() trebuie să ruleze din nou.
        [System.NonSerialized] bool initialized;

        struct ModeLook
        {
            public string Label, Word, Hint, Primary, LabelClass, KnobClass;
        }

        static ModeLook Look(TimeMode m)
        {
            switch (m)
            {
                case TimeMode.Past:
                    return new ModeLook
                    {
                        Label = "Trecut", Word = "1989",
                        Hint = "Fotografii de arhivă, exact unde au fost făcute.",
                        Primary = "Ascultă ghidul",
                        LabelClass = "mode-label--past", KnobClass = "knob--past"
                    };
                case TimeMode.Future:
                    return new ModeLook
                    {
                        Label = "Viitor", Word = "2030",
                        Hint = "Capsulele se deschid doar aici, la data aleasă.",
                        Primary = "+ Creează o capsulă",
                        LabelClass = "mode-label--future", KnobClass = "knob--future"
                    };
                default:
                    return new ModeLook
                    {
                        Label = "Prezent", Word = "acum",
                        Hint = "Atinge o urmă ca s-o deschizi.",
                        Primary = "+ Lasă o urmă",
                        LabelClass = null, KnobClass = "knob--present"
                    };
            }
        }

        public void Init()
        {
            if (initialized) return;
            initialized = true;
            router.Init();
            var root = router.Root;

            screenMain = root.Q("screen-main");
            screenMenu = root.Q("screen-menu");
            screenAbout = root.Q("screen-about");
            pastTint = root.Q("past-tint");
            cameraPrompt = root.Q("camera-prompt");
            simTraces = root.Q("sim-traces");
            cameraOff = root.Q<Button>("camera-off");
            root.Q<Button>("camera-start").clicked += () => CameraRequested?.Invoke();
            cameraOff.clicked += () => CameraOffRequested?.Invoke();

            statusDot = root.Q("status-dot");
            statusLabel = root.Q<Label>("status-label");
            modeLabel = root.Q<Label>("mode-label");
            modeWord = root.Q<Label>("mode-word");
            modeHint = root.Q<Label>("mode-hint");
            actionPrimary = root.Q<Button>("action-primary");

            slider = root.Q("time-slider");
            track = root.Q("time-track");
            knob = root.Q("time-knob");
            labelPast = root.Q<Button>("time-past");
            labelPresent = root.Q<Button>("time-present");
            labelFuture = root.Q<Button>("time-future");

            // Meniu și ecrane secundare
            root.Q<Button>("menu-button").clicked += OpenMenu;
            root.Q<Button>("menu-close").clicked += CloseMenu;
            root.Q<Button>("menu-account").clicked += () => router.Toast("Contul (opțional) vine într-o etapă următoare.");
            root.Q<Button>("menu-about").clicked += OpenAbout;
            // Jurnalul de test
            screenLog = root.Q("screen-log");
            logText = root.Q<Label>("log-text");
            root.Q<Button>("menu-log").clicked += () =>
            {
                logText.text = ThreeTimes.Data.TestLog.Text();
                UIRouter.SetVisible(screenLog, true);
            };
            root.Q<Button>("log-back").clicked += () => UIRouter.SetVisible(screenLog, false);
            root.Q<Button>("log-copy").clicked += () =>
            {
                GUIUtility.systemCopyBuffer = ThreeTimes.Data.TestLog.Text();
                router.Toast("Jurnalul a fost copiat. Îl poți lipi într-un mesaj sau într-un document.");
            };
            root.Q<Button>("log-clear").clicked += () =>
            {
                ThreeTimes.Data.TestLog.Clear();
                logText.text = ThreeTimes.Data.TestLog.Text();
            };

            root.Q<Button>("menu-clear-traces").clicked += () =>
            {
                CloseMenu();
                Raise(ClearTracesRequested, "Nu ai urme de test.");
            };

            voicePlayer = new ThreeTimes.Media.VoicePlayer(gameObject);
            traceDetail = new TraceDetailView(root, router, voicePlayer);
            traceDetail.ReportRequested += t => TraceReportRequested?.Invoke(t);
            traceDetail.DeleteRequested += t => TraceDeleteRequested?.Invoke(t);

            // Atingerile din zona liberă: le trimitem mai departe în coordonate de ecran (origine jos-stânga).
            root.Q("tap-catcher").RegisterCallback<ClickEvent>(evt =>
            {
                if (!CameraOn || Mode != TimeMode.Present) return;
                float width = root.layout.width;
                if (float.IsNaN(width) || width <= 0f) return;
                float scale = Screen.width / width;
                WorldTapped?.Invoke(new Vector2(evt.position.x * scale, Screen.height - evt.position.y * scale));
            });

            traceSheet = new TraceSheetView(root, router, this, () => TraceSubmitCheck?.Invoke(), voicePlayer);
            traceSheet.Submitted += draft =>
            {
                if (TraceSubmitted != null) TraceSubmitted.Invoke(draft);
                else router.Toast("Urmele nu sunt încă legate în scenă: rulează Tools → 3Times → 3.");
            };
            root.Q<Button>("about-back").clicked += CloseAbout;
            root.Q<Button>("onboarding-lang").clicked += () => router.Toast("Versiunea în engleză vine mai târziu.");

            // Acțiuni
            actionPrimary.clicked += OnPrimaryAction;
            root.Q<Button>("action-map").clicked += () => Raise(MapRequested, "Harta vine într-o etapă următoare.");
            root.Q<Button>("action-postcard").clicked += () => Raise(PostcardRequested, "Time postcard vine într-o etapă următoare.");

            // Cursorul timpului: tragere pe bară + atingere pe etichete
            slider.RegisterCallback<PointerDownEvent>(OnPointerDown);
            slider.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            slider.RegisterCallback<PointerUpEvent>(OnPointerUp);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            labelPast.clicked += () => Select(TimeMode.Past);
            labelPresent.clicked += () => Select(TimeMode.Present);
            labelFuture.clicked += () => Select(TimeMode.Future);

            // Indicatorul de localizare se actualizează de două ori pe secundă.
            root.schedule.Execute(UpdateStatus).Every(500);

            ApplyMode(Mode);
        }

        void Update()
        {
            // Butonul „Înapoi” de pe Android
            // (în Input System, „Înapoi” de pe Android e tasta Escape)
            var keyboard = Keyboard.current;
            if (!initialized || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            if (traceDetail.IsOpen) traceDetail.Close();
            else if (traceSheet.IsOpen) { if (!traceSheet.HandleBack()) traceSheet.Close(); }
            else if (IsVisible(screenLog)) UIRouter.SetVisible(screenLog, false);
            else if (IsVisible(screenAbout)) CloseAbout();
            else if (IsVisible(screenMenu)) CloseMenu();
            else if (Mode != TimeMode.Present) Select(TimeMode.Present);
        }

        // ---------------------------------------------------------------- stare localizare

        /// <summary>Comută ecranul între „fundal + Pornește camera” și vederea prin cameră.</summary>
        public void SetCameraOn(bool on)
        {
            CameraOn = on;
            UIRouter.SetVisible(cameraPrompt, !on);
            UIRouter.SetVisible(cameraOff, on);
            UIRouter.SetVisible(modeHint, on);
            actionPrimary.EnableInClassList("btn--muted", !on);
            UpdateSimTracesVisibility();
            UpdateStatus();
        }

        /// <summary>Câte urme sunt salvate; schimbă mesajul de sub cameră în modul Prezent.</summary>
        public void SetTraceCount(int count)
        {
            traceCount = count;
            if (initialized && Mode == TimeMode.Present) modeHint.text = PresentHint();
        }

        /// <summary>Deschide „ecranul unei urme”.</summary>
        public void OpenTrace(ThreeTimes.Data.SavedTrace trace) => traceDetail.Open(trace);

        /// <summary>
        /// Doar în simulare (Editor): desenează urmele salvate peste ilustrația pieței,
        /// ca să vedem cum arată fără telefon. Pe telefon urmele apar în AR.
        /// </summary>
        public void ShowSimulatedTraces(IList<ThreeTimes.Data.SavedTrace> traces)
        {
            simTraces.Clear();
            foreach (var t in traces)
            {
                // Poziție stabilă, calculată din id: aceeași urmă apare mereu în același loc.
                int hash = 17;
                foreach (char c in t.id) hash = unchecked(hash * 31 + c);
                float x = 12 + Mathf.Abs(hash % 76);          // 12–88 % din lățime
                float y = 32 + Mathf.Abs((hash / 97) % 26);   // 32–58 % din înălțime

                int level = TraceLook.Level(t.intensity);
                string key = TraceLook.Key(t.emotion);
                float size = 44 + level * 22;

                var item = new VisualElement();
                item.AddToClassList("sim-trace");
                var trace = t; // copie pentru lambda
                item.RegisterCallback<ClickEvent>(_ => OpenTrace(trace));
                item.style.left = Length.Percent(x);
                item.style.top = Length.Percent(y);

                // Forma stă pe fundalul tipului (polaroid pentru poză, microfon pentru voce).
                var holder = new VisualElement { pickingMode = PickingMode.Ignore };
                holder.style.width = size;
                holder.style.height = size;
                var backdrop = TraceTypeBackdrop.Create(t.type, key, size);
                if (backdrop != null) holder.Add(backdrop);

                var shape = new VisualElement { pickingMode = PickingMode.Ignore };
                shape.AddToClassList("sim-trace__shape");
                shape.AddToClassList($"shape--{key}-{level}");
                shape.style.position = Position.Absolute;
                shape.style.left = 0;
                shape.style.top = 0;
                shape.style.width = size;
                shape.style.height = size;
                holder.Add(shape);
                item.Add(holder);

                var label = new Label(TraceLook.Name(t.emotion)) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("sim-trace__label");
                label.style.marginTop = TraceTypeBackdrop.ExtraBelow(t.type, size);
                item.Add(label);

                simTraces.Add(item);
            }
            UpdateSimTracesVisibility();
        }

        void UpdateSimTracesVisibility() =>
            UIRouter.SetVisible(simTraces, CameraOn && Mode == TimeMode.Present && simTraces.childCount > 0);

        string PresentHint() =>
            traceCount == 0 ? "Nicio urmă aici încă. Lasă tu prima." :
            traceCount == 1 ? "O urmă lăsată de tine." :
            $"{traceCount} urme lăsate de tine.";

        void UpdateStatus()
        {
            if (tracker == null || !IsVisible(screenMain)) return;

            if (!CameraOn)
            {
                statusDot.EnableInClassList("dot--good", false);
                statusDot.EnableInClassList("dot--low", false);
                statusDot.EnableInClassList("dot--none", true);
                statusLabel.text = "Camera e oprită";
                return;
            }

            var fix = tracker.Fix;
            string accuracy = fix.HasValue ? $", ±{fix.Value.HorizontalAccuracy:0} m" : "";

            statusDot.EnableInClassList("dot--good", tracker.Quality == LocalizationQuality.Good);
            statusDot.EnableInClassList("dot--low", tracker.Quality == LocalizationQuality.Low);
            statusDot.EnableInClassList("dot--none", tracker.Quality == LocalizationQuality.None);

            switch (tracker.Quality)
            {
                case LocalizationQuality.Good: statusLabel.text = "Localizat" + accuracy; break;
                case LocalizationQuality.Low: statusLabel.text = "Precizie scăzută" + accuracy; break;
                default: statusLabel.text = "Se localizează…"; break;
            }
        }

        // ---------------------------------------------------------------- acțiuni

        void OnPrimaryAction()
        {
            switch (Mode)
            {
                case TimeMode.Present:
                    if (!CameraOn) { router.Toast("Pornește camera ca să lași o urmă exact aici."); break; }
                    if (tracker != null && tracker.Quality != LocalizationQuality.Good)
                    {
                        router.Toast("Localizarea nu e încă destul de precisă. Îndreaptă telefonul spre clădiri.");
                        break;
                    }
                    traceSheet.Open();
                    break;
                case TimeMode.Past:
                    Raise(pastReady ? GuideRequested : null, "Ghidul vocal și fațadele vin în etapa următoare.");
                    break;
                case TimeMode.Future:
                    Raise(futureReady ? CreateCapsuleRequested : null, "Capsulele timpului vin după urme.");
                    break;
            }
        }

        void Raise(Action action, string fallbackMessage)
        {
            if (action != null) action.Invoke();
            else router.Toast(fallbackMessage);
        }

        void OpenMenu() => UIRouter.SetVisible(screenMenu, true);
        void CloseMenu() => UIRouter.SetVisible(screenMenu, false);
        void OpenAbout() => UIRouter.SetVisible(screenAbout, true);
        void CloseAbout() => UIRouter.SetVisible(screenAbout, false);

        static bool IsVisible(VisualElement e) => e != null && !e.ClassListContains("hidden");

        // ---------------------------------------------------------------- cursorul timpului

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
            Select(target ?? Mode);
        }

        float PointerToTrack(Vector3 panelPosition)
        {
            float width = track.layout.width;
            if (float.IsNaN(width) || width <= 0f) return StopOf(Mode);
            return Mathf.Clamp01(track.WorldToLocal(panelPosition).x / width);
        }

        void SetKnob(float t) => knob.style.left = Length.Percent(t * 100f);

        public void Select(TimeMode m)
        {
            bool changed = m != Mode;
            ApplyMode(m);
            if (changed) ModeChanged?.Invoke(m);
        }

        void ApplyMode(TimeMode m)
        {
            Mode = m;
            var look = Look(m);

            SetKnob(StopOf(m));
            knob.EnableInClassList("knob--past", m == TimeMode.Past);
            knob.EnableInClassList("knob--present", m == TimeMode.Present);
            knob.EnableInClassList("knob--future", m == TimeMode.Future);

            // Cuvântul mare („acum”, „1989”, „2030”) iese, se schimbă și intră din nou.
            if (modeWord.text != look.Word)
            {
                modeWord.AddToClassList("word-out");
                modeLabel.AddToClassList("word-out");
                modeWord.schedule.Execute(() =>
                {
                    modeLabel.text = look.Label;
                    modeLabel.EnableInClassList("mode-label--past", m == TimeMode.Past);
                    modeLabel.EnableInClassList("mode-label--future", m == TimeMode.Future);
                    modeWord.text = look.Word;
                    modeWord.RemoveFromClassList("word-out");
                    modeLabel.RemoveFromClassList("word-out");
                }).StartingIn(180);
            }
            modeHint.text = m == TimeMode.Present ? PresentHint() : look.Hint;
            actionPrimary.text = look.Primary;

            labelPast.EnableInClassList("time-label--past-active", m == TimeMode.Past);
            labelPresent.EnableInClassList("time-label--active", m == TimeMode.Present);
            labelFuture.EnableInClassList("time-label--future-active", m == TimeMode.Future);

            UIRouter.SetVisible(pastTint, m == TimeMode.Past);
            UpdateSimTracesVisibility();
        }
    }
}
