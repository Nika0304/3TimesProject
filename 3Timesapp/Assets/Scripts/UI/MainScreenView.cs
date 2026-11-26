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

        public TimeMode Mode { get; private set; } = TimeMode.Present;

        /// <summary>Se declanșează când utilizatorul schimbă modul de pe cursor.</summary>
        public event Action<TimeMode> ModeChanged;

        // Acțiunile ecranului. Celelalte componente se abonează aici.
        /// <summary>Utilizatorul a completat și a confirmat o urmă nouă.</summary>
        public event Action<TraceDraft> TraceSubmitted;
        /// <summary>Utilizatorul a completat și a confirmat o capsulă a timpului (modul Viitor).</summary>
        public event Action<TraceDraft> CapsuleSubmitted;
        /// <summary>Întrebat înainte să se trimită o capsulă (server, localizare, cont). Îl setează TraceManager.</summary>
        public Func<string> CapsuleSubmitCheck { get; set; }
        public event Action GuideRequested;
        public event Action CreateCapsuleRequested;
        public event Action MapRequested;
        public event Action PostcardRequested;
        public event Action CameraRequested;
        public event Action CameraOffRequested;
        public event Action ClearTracesRequested;
        /// <summary>Atingere în zona liberă a ecranului, în coordonate de ecran (pentru urmele din AR).</summary>
        public event Action<Vector2> WorldTapped;
        public event Action<ThreeTimes.Data.SavedTrace, string> TraceReportRequested;
        public event Action<ThreeTimes.Data.SavedTrace> TraceDeleteRequested;
        /// <summary>Contul a fost șters împreună cu urmele lui (TraceManager le scoate și de pe telefon).</summary>
        public event Action AccountDeleted;

        public bool CameraOn { get; private set; }

        /// <summary>
        /// Întrebat înainte să se trimită o urmă: mesajul care spune de ce nu se poate încă, sau null.
        /// Îl setează TraceManager.
        /// </summary>
        public Func<string> TraceSubmitCheck { get; set; }

        ThreeTimes.Media.VoicePlayer voicePlayer;

        /// <summary>
        /// Înainte de deschiderea unei urme: descarcă poza sau vocea, dacă e de la altcineva.
        /// Primește urma și ce trebuie făcut după. Îl setează TraceManager.
        /// </summary>
        public Action<ThreeTimes.Data.SavedTrace, Action> EnsureMedia { get; set; }

        /// <summary>Urmele din jurul unui punct, pentru hartă (lat, lng, rază în metri, rezultat). Îl setează TraceManager.</summary>
        public Action<double, double, float, Action<List<ThreeTimes.Data.SavedTrace>>> MapTraces { get; set; }

        /// <summary>Urmele contului, pentru „Harta mea” (rezultat: urmele sau null, mesajul de eroare). Îl setează TraceManager.</summary>
        public Action<Action<List<ThreeTimes.Data.SavedTrace>, string>> MapMyTraces { get; set; }

        MapScreenView map;
        PostcardView postcard;
        AccountView account;

        /// <summary>Urmele care se văd acum în jur (pentru cartea poștală). Îl setează TraceManager.</summary>
        public Func<List<ThreeTimes.Data.SavedTrace>> VisibleTraces { get; set; }
        TextField serverField;
        Label serverStatus;

        VisualElement screenLog;
        NotificationsView notifications;
        Label logText;
        VisualElement screenMain, screenMenu, screenAbout, pastTint, cameraPrompt, simTraces;
        Button cameraOff;
        VisualElement statusDot;
        Label statusLabel, modeLabel, modeWord, modeHint;
        Button actionPrimary;
        VisualElement slider, track, knob;
        Button labelPast, labelPresent, labelFuture;

        TraceSheetView traceSheet;
        CapsuleCardView capsuleCard;
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
            account = new AccountView(root, router, this);
            account.AccountDeleted += () => AccountDeleted?.Invoke();
            root.Q<Button>("menu-account").clicked += () => account.Open();
            root.Q<Button>("menu-about").clicked += OpenAbout;
            notifications = new NotificationsView(root, router, this);

            // Serverul: adresa se poate schimba (la hotspot, IP-ul laptopului)
            serverField = root.Q<TextField>("server-url");
            serverStatus = root.Q<Label>("server-status");
            serverField.value = ThreeTimes.Data.ServerSettings.Url;
            root.Q<Button>("server-save").clicked += SaveServer;
            // Jurnalul de test
            screenLog = root.Q("screen-log");
            logText = root.Q<Label>("log-text");
            root.Q<Button>("menu-log").clicked += () =>
            {
                logText.text = ThreeTimes.Data.TestLog.Text();
                UIRouter.SetVisible(screenLog, true);
            };
            root.Q<Button>("log-back").clicked += () => UIRouter.SetVisible(screenLog, false);
            // Ceasul de test: „Peste un an” arată urmele la aniversare și capsula anului deschisă.
            var clockText = root.Q<Label>("log-clock-text");
            var clockButton = root.Q<Button>("log-clock");
            void ShowClock()
            {
                clockText.text = TestClock.Active
                    ? $"Acum aplicația e la {RomaniaTime.DateText(RomaniaTime.Today)}: vezi urmele la aniversare și capsula anului deschisă."
                    : "Azi. „Peste un an” arată cum va fi: urmele la aniversare și capsula anului deschisă.";
                clockButton.text = TestClock.Active ? "Înapoi la azi" : "Peste un an";
            }
            clockButton.clicked += () =>
            {
                TestClock.Years = TestClock.Active ? 0 : 1;
                ThreeTimes.Data.TestLog.Add(TestClock.Active ? "Ceas de test: peste un an" : "Ceas de test: înapoi la azi");
                router.Toast(TestClock.Active
                    ? "Aplicația se poartă ca peste un an (doar până o închizi). Se reîncarcă urmele și capsulele…"
                    : "Înapoi la azi.", 4f);
                ShowClock();
                UpdateStatus();
            };
            ShowClock();
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
            traceDetail.ReportRequested += (t, reason) => TraceReportRequested?.Invoke(t, reason);
            traceDetail.DeleteRequested += t => TraceDeleteRequested?.Invoke(t);

            // Atingerile din zona liberă: le trimitem mai departe în coordonate de ecran (origine jos-stânga).
            root.Q("tap-catcher").RegisterCallback<ClickEvent>(evt =>
            {
                if (!CameraOn || Mode == TimeMode.Past) return;
                float width = root.layout.width;
                if (float.IsNaN(width) || width <= 0f) return;
                float scale = Screen.width / width;
                WorldTapped?.Invoke(new Vector2(evt.position.x * scale, Screen.height - evt.position.y * scale));
            });

            traceSheet = new TraceSheetView(root, router, this, () => TraceSubmitCheck?.Invoke(), voicePlayer);
            traceSheet.CapsuleCheck = () => CapsuleSubmitCheck?.Invoke();
            capsuleCard = new CapsuleCardView(root);
            traceSheet.Submitted += draft =>
            {
                var target = draft.Capsule ? CapsuleSubmitted : TraceSubmitted;
                if (target != null) target.Invoke(draft);
                else router.Toast("Urmele nu sunt încă legate în scenă: rulează Tools → 3Times → 3.");
            };
            root.Q<Button>("about-back").clicked += CloseAbout;
            root.Q<Button>("onboarding-lang").clicked += () => router.Toast("Versiunea în engleză vine mai târziu.");

            // Acțiuni
            actionPrimary.clicked += OnPrimaryAction;
            map = new MapScreenView(root, router, this, tracker,
                                    (lat, lng, radius, done) =>
                                    {
                                        if (MapTraces != null) MapTraces(lat, lng, radius, done);
                                        else done(new List<ThreeTimes.Data.SavedTrace>());
                                    },
                                    OpenTrace,
                                    done =>
                                    {
                                        if (MapMyTraces != null) MapMyTraces(done);
                                        else done(null, "Urmele nu sunt încă legate în scenă.");
                                    });
            root.Q<Button>("action-map").clicked += () => { map.Open(); MapRequested?.Invoke(); };
            postcard = new PostcardView(root, router, this, tracker, () => VisibleTraces?.Invoke());
            root.Q<Button>("action-postcard").clicked += () =>
            {
                if (!CameraOn)
                {
                    router.Toast("Pornește camera ca să faci o carte poștală din ce vezi.");
                    return;
                }
                postcard.Create(Look(Mode).Word);
                PostcardRequested?.Invoke();
            };
            root.Q<Button>("menu-postcards").clicked += () => { CloseMenu(); postcard.OpenList(); };

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
            else if (capsuleCard.IsOpen) capsuleCard.Close();
            else if (postcard.IsOpen) postcard.Close();
            else if (postcard.ListOpen) postcard.CloseList();
            else if (map.IsOpen) map.Back();
            else if (traceSheet.IsOpen) { if (!traceSheet.HandleBack()) traceSheet.Close(); }
            else if (IsVisible(screenLog)) UIRouter.SetVisible(screenLog, false);
            else if (account.IsOpen) account.Back();
            else if (notifications.IsOpen) notifications.Close();
            else if (IsVisible(screenAbout)) CloseAbout();
            else if (IsVisible(screenMenu)) CloseMenu();
            else if (Mode != TimeMode.Present) Select(TimeMode.Present);
        }

        // ---------------------------------------------------------------- server

        void SaveServer()
        {
            string url = ThreeTimes.Data.ServerSettings.Normalize(serverField.value);
            serverField.Blur();
            ThreeTimes.Data.ServerSettings.Url = url;
            serverField.value = url;
            if (url.Length == 0)
            {
                serverStatus.text = "Fără server: vezi doar urmele tale, salvate pe telefon.";
                return;
            }
            serverStatus.text = "Verific…";
            StartCoroutine(ThreeTimes.Data.TraceApi.Health(url, ok =>
            {
                serverStatus.text = ok
                    ? "Conectat. Urmele se trimit și se primesc de la acest server."
                    : "Serverul nu răspunde. Verifică adresa, hotspotul și dacă serverul e pornit.";
                ThreeTimes.Data.TestLog.Add($"Server {url}: {(ok ? "conectat" : "nu răspunde")}");
            }));
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

        /// <summary>
        /// Atingerea unei capsule încă sigilate: încercarea de a o deschide (serverul verifică locul și data).
        /// Îl setează TraceManager.
        /// </summary>
        public Action<ThreeTimes.Data.SavedTrace> OpenCapsule { get; set; }

        /// <summary>Ecranul unei capsule care încă nu se poate deschide (câte urme are, Pune o urmă, Sigilează).</summary>
        public void ShowCapsuleCard(ThreeTimes.Data.SavedTrace capsule, Action addItem, Action seal, Action delete,
                                    Action removeMyItems = null) =>
            capsuleCard.Open(capsule, addItem, seal, delete, removeMyItems);

        /// <summary>Ecranul capsulei anului (înainte de deschidere sau rezumatul anului).</summary>
        public void ShowYearCard(ThreeTimes.Data.SavedTrace yearCapsule) => capsuleCard.OpenYear(yearCapsule);

        /// <summary>Foaia „Pune o urmă în capsulă”, pentru o capsulă care mai primește urme.</summary>
        public void OpenAddToCapsule(ThreeTimes.Data.SavedTrace capsule)
        {
            if (tracker != null && tracker.Quality != LocalizationQuality.Good)
            {
                router.Toast("Localizarea nu e încă destul de precisă. Îndreaptă telefonul spre clădiri.");
                return;
            }
            traceSheet.Open(capsule: true, addTo: capsule.id, addPersonal: capsule.visibility == "PERSONAL");
        }

        /// <summary>Deschide „ecranul unei urme” (sau, la o capsulă sigilată, încearcă s-o deschidă).</summary>
        public void OpenTrace(ThreeTimes.Data.SavedTrace trace)
        {
            if (trace != null && trace.IsCapsule && trace.@sealed)
            {
                if (OpenCapsule != null) OpenCapsule(trace);
                return;
            }
            if (EnsureMedia != null) EnsureMedia(trace, () => traceDetail.Open(trace));
            else traceDetail.Open(trace);
        }

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

                // Capsula încă sigilată: sfera de sticlă, cu data dedesubt.
                if (t.IsCapsule && t.@sealed)
                {
                    simTraces.Add(SimulatedCapsule(t, x, y));
                    continue;
                }

                var style = ThreeTimes.Data.TraceStyle.Of(t);
                int level = style.Level;
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
                var backdrop = TraceTypeBackdrop.Create(t.type, style.Color, size);
                if (backdrop != null) holder.Add(backdrop);

                var shape = new VisualElement { pickingMode = PickingMode.Ignore };
                shape.AddToClassList("sim-trace__shape");
                shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                shape.style.position = Position.Absolute;
                shape.style.left = 0;
                shape.style.top = 0;
                shape.style.width = size;
                shape.style.height = size;
                holder.Add(shape);
                item.Add(holder);

                // „Acum un an, aici”: urma revenită la aniversare (în AR are scântei aurii).
                int years = TraceTime.AnniversaryYears(t);
                string labelText = years == 0 ? style.Label
                                 : years == 1 ? $"{style.Label} · acum un an" : $"{style.Label} · acum {years} ani";
                var label = new Label(labelText) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("sim-trace__label");
                if (years > 0) label.AddToClassList("sim-trace__label--anniversary");
                label.style.marginTop = TraceTypeBackdrop.ExtraBelow(t.type, size);
                item.Add(label);

                simTraces.Add(item);
            }
            UpdateSimTracesVisibility();
        }

        VisualElement SimulatedCapsule(ThreeTimes.Data.SavedTrace t, float x, float y)
        {
            bool ready = CapsuleText.IsReady(t);
            var item = new VisualElement();
            item.AddToClassList("sim-trace");
            var capsule = t; // copie pentru lambda
            item.RegisterCallback<ClickEvent>(_ => OpenTrace(capsule));
            item.style.left = Length.Percent(x);
            item.style.top = Length.Percent(y);

            var orb = new VisualElement { pickingMode = PickingMode.Ignore };
            float orbSize = t.IsYearCapsule ? 110 : 70;   // capsula anului e mai mare
            orb.style.width = orbSize;
            orb.style.height = orbSize;
            orb.AddToClassList("sim-capsule__orb");
            orb.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(ready));
            item.Add(orb);

            int daysLeft = TraceTime.CapsuleDaysLeft(t);
            string left = daysLeft <= 0 ? "" : daysLeft == 1 ? " · ultima zi" : $" · încă {daysLeft} zile";
            string text = ready ? (t.IsYearCapsule ? $"Capsula anului {t.year}: atinge ca s-o deschizi{left}" : $"Atinge ca s-o deschizi{left}")
                        : t.IsYearCapsule ? $"Capsula anului {t.year} · {t.itemCount} urme"
                        : t.collecting ? $"{t.itemCount} din {(t.maxItems > 0 ? t.maxItems : 10)} urme"
                        : CapsuleText.Short(t);
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("sim-trace__label");
            item.Add(label);
            return item;
        }

        void UpdateSimTracesVisibility() =>
            UIRouter.SetVisible(simTraces, CameraOn && Mode != TimeMode.Past && simTraces.childCount > 0);

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
                case LocalizationQuality.Good: statusLabel.text = "Localizat" + accuracy + (TestClock.Active ? " · ceas de test +1 an" : ""); break;
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
                    if (!CameraOn) { router.Toast("Pornește camera ca să lași o capsulă exact aici."); break; }
                    if (tracker != null && tracker.Quality != LocalizationQuality.Good)
                    {
                        router.Toast("Localizarea nu e încă destul de precisă. Îndreaptă telefonul spre clădiri.");
                        break;
                    }
                    traceSheet.Open(capsule: true);
                    CreateCapsuleRequested?.Invoke();
                    break;
            }
        }

        void Raise(Action action, string fallbackMessage)
        {
            if (action != null) action.Invoke();
            else router.Toast(fallbackMessage);
        }

        void OpenMenu()
        {
            serverField.value = ThreeTimes.Data.ServerSettings.Url;
            serverStatus.text = "";
            UIRouter.SetVisible(screenMenu, true);
            notifications.Refresh();
        }
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
