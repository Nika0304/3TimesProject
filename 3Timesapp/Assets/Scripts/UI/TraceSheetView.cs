using System;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using ThreeTimes.Core;
using ThreeTimes.Data;
using ThreeTimes.Media;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul „Lasă o urmă”: o foaie care urcă de jos. Urma poate fi Text, Poză sau Voce.
    /// Poza se face pe loc (selfie, locul, sau locul cu urmele), fără galerie, ca să aibă legătură
    /// cu locul. Vocea are cel mult 30 de secunde. La poză și voce, mesajul scris e opțional.
    /// Forma din previzualizare se schimbă pe loc după emoție și intensitate.
    /// </summary>
    public class TraceSheetView
    {
        enum TraceKind { Text, Photo, Voice }

        readonly UIRouter router;
        readonly MonoBehaviour host;
        readonly Func<string> submitCheck;
        Button submitButton;
        bool checking;   // verificarea textului (AI) e în curs

        // Capsula timpului (modul Viitor): aceeași foaie, cu data deschiderii și cine o poate deschide.
        bool capsuleMode;
        string addingTo;       // id-ul capsulei în care se pune urma (gol = capsulă nouă)
        Label title;
        ScrollView scroll;
        Button yearRow;
        VisualElement yearCheck;
        Label yearLabel;
        bool yearOn = true;
        VisualElement capsulePanel, preview;
        Label capsuleDay, capsuleMonth, capsuleYear, capsuleNote;
        readonly Button[] presets = new Button[4];
        Button presetTest, visPublic, visPersonal;
        DateTime unlockDate;   // data aleasă (ora României)
        bool personal, testMinute;
        static readonly int[] PresetMonths = { 1, 12, 60, 120 };

        /// <summary>La capsule: de ce nu se poate trimite acum (mesaj), sau null. Îl setează TraceManager.</summary>
        public Func<string> CapsuleCheck { get; set; }

        /// <summary>Doar în Editor și în build-urile de test: „peste 1 minut”, ca deschiderea să se poată încerca.</summary>
        static bool AllowTestMinute => Application.isEditor || Debug.isDebugBuild;
        int openCount;   // crește la fiecare deschidere: un răspuns venit după închidere se ignoră
        readonly VoicePlayer player;
        readonly PhotoCaptureView capture;
        readonly VoiceRecorder recorder = new VoiceRecorder();

        readonly VisualElement overlay, sheet, shape, dim;
        readonly Label suggestionLabel, textLabel;
        readonly TextField text;
        readonly Button[] chips = new Button[3];
        readonly VisualElement[] chipIcons = new VisualElement[3];
        readonly Label[] chipLabels = new Label[3];
        readonly TextField customEmotion;
        readonly Button[] intensities = new Button[3];
        readonly Button[] kinds = new Button[3];

        readonly VisualElement photoPanel, photoPick, photoDone, photoThumb;
        readonly Label photoLabel;
        readonly VisualElement voicePanel, voiceDone;
        readonly Button voiceRecord, voicePlay;
        readonly Label voiceDuration;

        static readonly string[] OldChipClasses = { "chip--joy", "chip--sadness", "chip--longing" };
        const string SuggestionText = "Scrie mesajul și îți propunem o emoție.";
        const string SuggestionMedia = "Alege emoția sau scrie o descriere și îți propunem una.";

        // Dacă utilizatorul alege singur, propunerea automată nu-i mai schimbă alegerea.
        bool manualEmotion, manualIntensity;
        IVisualElementScheduledItem pendingSuggestion, recordTimer, playWatcher;

        VisualElement previewBackdrop;
        // Cele trei emoții propuse (de AI sau, fără server, cele de bază) și cea aleasă.
        List<EmotionLook> options = TraceStyle.DefaultOptions();
        int selected;
        int suggestionRequest;   // crește la fiecare cerere către server; răspunsurile vechi se ignoră
        int customRequest;
        int intensity = 1;
        TraceKind kind = TraceKind.Text;

        string photoFile;
        PhotoSource photoSource;
        Texture2D photoTexture;
        string voiceFile;
        float voiceSeconds;

        public bool IsOpen { get; private set; }

        /// <summary>Utilizatorul a apăsat „Lasă urma aici” cu urma completată.</summary>
        public event Action<TraceDraft> Submitted;

        public TraceSheetView(VisualElement root, UIRouter router, MonoBehaviour host,
                              Func<string> submitCheck, VoicePlayer player)
        {
            this.router = router;
            this.host = host;
            this.submitCheck = submitCheck;
            this.player = player;
            capture = new PhotoCaptureView(root, router, host);

            overlay = root.Q("trace-overlay");
            sheet = root.Q("trace-sheet");
            shape = root.Q("trace-shape");
            text = root.Q<TextField>("trace-text");
            textLabel = root.Q<Label>("trace-text-label");
            dim = root.Q("trace-dim");
            suggestionLabel = root.Q<Label>("trace-suggestion");

            photoPanel = root.Q("trace-photo-panel");
            photoPick = root.Q("trace-photo-pick");
            photoDone = root.Q("trace-photo-done");
            photoThumb = root.Q("trace-photo-thumb");
            photoLabel = root.Q<Label>("trace-photo-label");
            voicePanel = root.Q("trace-voice-panel");
            voiceDone = root.Q("trace-voice-done");
            voiceRecord = root.Q<Button>("voice-record");
            voicePlay = root.Q<Button>("voice-play");
            voiceDuration = root.Q<Label>("voice-duration");

            // Propunem emoția la jumătate de secundă după ce utilizatorul se oprește din scris.
            text.RegisterValueChangedCallback(_ =>
            {
                pendingSuggestion?.Pause();
                pendingSuggestion = text.schedule.Execute(Suggest).StartingIn(500);
            });

            root.Q<Button>("trace-close").clicked += () => Close();
            root.Q<Button>("trace-dim").clicked += () => Close();
            submitButton = root.Q<Button>("trace-submit");
            submitButton.clicked += Submit;
            SetupCapsule(root);

            kinds[0] = root.Q<Button>("trace-type-text");
            kinds[1] = root.Q<Button>("trace-type-photo");
            kinds[2] = root.Q<Button>("trace-type-voice");
            for (int i = 0; i < 3; i++)
            {
                var k = (TraceKind)i;
                kinds[i].clicked += () => SelectKind(k);
            }

            root.Q<Button>("photo-selfie").clicked += () => TakePhoto(PhotoSource.Selfie);
            root.Q<Button>("photo-place").clicked += () => TakePhoto(PhotoSource.Place);
            root.Q<Button>("photo-traces").clicked += () => TakePhoto(PhotoSource.PlaceWithTraces);
            root.Q<Button>("trace-photo-redo").clicked += () => TakePhoto(photoSource);

            voiceRecord.clicked += ToggleRecording;
            voicePlay.clicked += TogglePlayback;
            root.Q<Button>("voice-redo").clicked += () =>
            {
                player.Stop();
                DeleteVoice();
                RefreshMedia();
            };

            for (int i = 0; i < 3; i++)
            {
                int index = i; // copie pentru lambda
                chips[i] = root.Q<Button>("chip-" + i);
                chipIcons[i] = chips[i].Q(className: "chip__icon");
                chipLabels[i] = chips[i].Q<Label>(className: "chip__label");
                foreach (var old in OldChipClasses) chips[i].RemoveFromClassList(old);
                chips[i].clicked += () =>
                {
                    selected = index;
                    manualEmotion = true;
                    suggestionLabel.text = "Ai ales tu emoția.";
                    Refresh();
                };
                intensities[i] = root.Q<Button>("intensity-" + i);
                intensities[i].clicked += () => { intensity = index; manualIntensity = true; Refresh(); };
            }

            // Altă emoție, scrisă de utilizator: AI-ul îi alege forma.
            customEmotion = root.Q<TextField>("emotion-custom");
            root.Q<Button>("emotion-custom-apply").clicked += ApplyCustomEmotion;

            Refresh();
            RefreshMedia();
        }

        /// <param name="capsule">true = „Creează o capsulă” (Viitor); false = „Lasă o urmă” (Prezent).</param>
        /// <param name="addTo">Id-ul unei capsule existente în care se pune urma (fără dată și tip).</param>
        /// <param name="addPersonal">Capsula existentă e personală (urma nu trece prin moderare).</param>
        public void Open(bool capsule = false, string addTo = null, bool addPersonal = false)
        {
            if (IsOpen) return;
            IsOpen = true;
            openCount++;
            addingTo = addTo;
            capsuleMode = capsule || addTo != null;
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
            if (yearRow != null)
            {
                int year = RomaniaTime.Today.Year;
                yearLabel.text = $"Pune-o și în capsula anului {year}. Se deschide pe 1 ianuarie {year + 1}, " +
                                 "cu toate urmele din oraș.";
                yearOn = true;
                yearCheck.EnableInClassList("year-check--on", true);
                UIRouter.SetVisible(yearRow, !(capsule || addTo != null));
            }
            title.text = addTo != null ? "Pune o urmă în capsulă" : capsule ? "Creează o capsulă" : "Lasă o urmă";
            UIRouter.SetVisible(capsulePanel, capsule && addTo == null);
            UIRouter.SetVisible(preview, !(capsule && addTo == null));   // la capsula nouă, data ia locul previzualizării
            submitButton.text = SubmitText;
            personal = addTo != null && addPersonal;
            if (capsule && addTo == null)
            {
                personal = false;
                testMinute = false;
                SetUnlock(RomaniaTime.Today.AddMonths(PresetMonths[1]));
                RefreshCapsule();
            }
            text.value = "";
            options = TraceStyle.DefaultOptions();
            selected = 0;
            customEmotion.value = "";
            intensity = 1;
            manualEmotion = manualIntensity = false;
            SelectKind(TraceKind.Text);
            Refresh();

            UIRouter.SetVisible(overlay, true);
            // Scoatem clasele după un cadru, ca tranzițiile să pornească din starea închisă.
            sheet.schedule.Execute(() =>
            {
                sheet.RemoveFromClassList("trace-sheet--closed");
                dim.RemoveFromClassList("sheet-dim--hidden");
            }).StartingIn(30);
        }

        /// <summary>Închide foaia. Poza și vocea nefolosite se șterg (în afară de keepFile, trimisă cu urma).</summary>
        public void Close(string keepFile = null)
        {
            if (!IsOpen) return;
            IsOpen = false;
            suggestionRequest++;
            if (capture.IsOpen) capture.Cancel();
            text.Blur(); // închide tastatura
            pendingSuggestion?.Pause();
            StopRecordingTimer();
            recorder.Cancel();
            player.Stop();
            playWatcher?.Pause();
            if (photoFile != keepFile) MediaStore.Delete(photoFile);
            if (voiceFile != keepFile) MediaStore.Delete(voiceFile);
            photoFile = voiceFile = null;
            ClearThumb();

            sheet.AddToClassList("trace-sheet--closed");
            dim.AddToClassList("sheet-dim--hidden");
            overlay.schedule.Execute(() => { if (!IsOpen) UIRouter.SetVisible(overlay, false); }).StartingIn(600);
        }

        /// <summary>Butonul „Înapoi”: întâi închide ecranul de fotografiat, dacă e deschis.</summary>
        public bool HandleBack()
        {
            if (!capture.IsOpen) return false;
            capture.Cancel();
            return true;
        }

        void Submit()
        {
            string message = (text.value ?? "").Trim();
            if (recorder.IsRecording) FinishRecording();

            var look = options[selected].Copy();
            look.intensity = intensity;
            var draft = new TraceDraft { Text = message, Look = look, Intensity = intensity };
            draft.YearCapsule = !capsuleMode && yearOn;
            if (capsuleMode)
            {
                if (addingTo == null && personal && !AccountSession.IsSignedIn)
                {
                    router.Toast("O capsulă doar pentru tine cere cont: Meniu → Cont.", 4f);
                    return;
                }
                draft.Capsule = true;
                draft.CapsuleId = addingTo;
                draft.Visibility = personal ? "PERSONAL" : "PUBLIC";
                draft.UnlockAt = RomaniaTime.Iso(testMinute ? TestClock.UtcNow.AddSeconds(150) : RomaniaTime.MidnightUtc(unlockDate));
            }
            switch (kind)
            {
                case TraceKind.Text:
                    if (message.Length == 0) { router.Toast("Scrie câteva cuvinte mai întâi."); return; }
                    draft.Type = "TEXT";
                    break;
                case TraceKind.Photo:
                    if (photoFile == null) { router.Toast("Fă o poză mai întâi."); return; }
                    draft.Type = "PHOTO";
                    draft.MediaFile = photoFile;
                    draft.PhotoSource = photoSource == PhotoSource.Selfie ? "SELFIE"
                                      : photoSource == PhotoSource.Place ? "PLACE" : "PLACE_WITH_TRACES";
                    break;
                default:
                    if (voiceFile == null) { router.Toast("Înregistrează mesajul mai întâi."); return; }
                    draft.Type = "VOICE";
                    draft.MediaFile = voiceFile;
                    draft.DurationSeconds = voiceSeconds;
                    break;
            }

            // Verificăm înainte să închidem, ca poza sau vocea să nu se piardă dacă trebuie să mai aștepți.
            string blocked = Check();
            if (blocked != null)
            {
                router.Toast(blocked, 4f);
                return;
            }

            // Textul trece întâi prin moderare (AI, pe server). Dacă e refuzat, foaia rămâne deschisă și se poate corecta.
            // Capsulele „doar pentru mine” nu le citește nimeni altcineva, deci nu trec prin moderare.
            if (message.Length > 0 && ServerSettings.IsConfigured && !(capsuleMode && personal))
            {
                if (checking) return;
                SetChecking(true);
                int session = openCount;
                host.StartCoroutine(TraceApi.Moderate(message, (verdict, reason) =>
                {
                    SetChecking(false);
                    if (!IsOpen || session != openCount) return; // foaia s-a închis între timp
                    if (verdict == "block")
                    {
                        router.Toast(string.IsNullOrEmpty(reason)
                            ? "Mesajul nu respectă regulile aplicației. Schimbă-l puțin și încearcă din nou."
                            : $"{reason} Schimbă mesajul și încearcă din nou.", 6f);
                        return;
                    }
                    Finish(draft);
                }));
                return;
            }
            Finish(draft);
        }

        void Finish(TraceDraft draft)
        {
            // Precizia se poate schimba cât așteptăm verificarea: mai întrebăm o dată.
            string blocked = Check();
            if (blocked != null)
            {
                router.Toast(blocked, 4f);
                return;
            }
            Close(draft.MediaFile);
            Submitted?.Invoke(draft);
        }

        void SetChecking(bool on)
        {
            checking = on;
            submitButton.text = on ? "Se verifică…" : SubmitText;
            submitButton.SetEnabled(!on);
        }

        string SubmitText => addingTo != null ? "Pune urma în capsulă"
                           : capsuleMode ? "Creează capsula aici"
                           : "Lasă urma aici";

        string Check() => capsuleMode ? (CapsuleCheck ?? submitCheck)?.Invoke() : submitCheck?.Invoke();

        // ---------------------------------------------------------------- capsula: data și cine o deschide

        void SetupCapsule(VisualElement root)
        {
            title = root.Q<Label>("trace-title");
            scroll = root.Q<ScrollView>("trace-scroll");
            yearRow = root.Q<Button>("trace-year");
            yearCheck = root.Q("trace-year-check");
            yearLabel = root.Q<Label>("trace-year-label");
            yearRow.clicked += () =>
            {
                yearOn = !yearOn;
                yearCheck.EnableInClassList("year-check--on", yearOn);
            };
            capsulePanel = root.Q("capsule-panel");
            preview = root.Q("trace-preview");
            capsuleDay = root.Q<Label>("capsule-day");
            capsuleMonth = root.Q<Label>("capsule-month");
            capsuleYear = root.Q<Label>("capsule-year");
            capsuleNote = root.Q<Label>("capsule-note");
            visPublic = root.Q<Button>("capsule-public");
            visPersonal = root.Q<Button>("capsule-personal");
            presetTest = root.Q<Button>("capsule-preset-test");
            UIRouter.SetVisible(presetTest, AllowTestMinute);
            presetTest.clicked += () => { testMinute = true; RefreshCapsule(); };

            for (int i = 0; i < presets.Length; i++)
            {
                int months = PresetMonths[i];
                presets[i] = root.Q<Button>("capsule-preset-" + i);
                presets[i].clicked += () =>
                {
                    testMinute = false;
                    SetUnlock(RomaniaTime.Today.AddMonths(months));
                    RefreshCapsule();
                };
            }

            root.Q<Button>("capsule-day-down").clicked += () => Step(d => d.AddDays(-1));
            root.Q<Button>("capsule-day-up").clicked += () => Step(d => d.AddDays(1));
            root.Q<Button>("capsule-month-down").clicked += () => Step(d => d.AddMonths(-1));
            root.Q<Button>("capsule-month-up").clicked += () => Step(d => d.AddMonths(1));
            root.Q<Button>("capsule-year-down").clicked += () => Step(d => d.AddYears(-1));
            root.Q<Button>("capsule-year-up").clicked += () => Step(d => d.AddYears(1));

            visPublic.clicked += () => { personal = false; RefreshCapsule(); };
            visPersonal.clicked += () =>
            {
                if (!AccountSession.IsSignedIn)
                {
                    router.Toast("O capsulă doar pentru tine cere cont: Meniu → Cont.", 4f);
                    return;
                }
                personal = true;
                RefreshCapsule();
            };
        }

        void Step(Func<DateTime, DateTime> change)
        {
            testMinute = false;
            SetUnlock(change(unlockDate));
            RefreshCapsule();
        }

        /// <summary>Data deschiderii: cel mai devreme mâine, cel mai târziu peste 10 ani.</summary>
        void SetUnlock(DateTime date)
        {
            var min = RomaniaTime.Today.AddDays(1);
            var max = RomaniaTime.Today.AddYears(10);
            unlockDate = date.Date < min ? min : date.Date > max ? max : date.Date;
        }

        void RefreshCapsule()
        {
            capsuleDay.text = unlockDate.Day.ToString();
            capsuleMonth.text = RomaniaTime.Months[unlockDate.Month - 1];
            capsuleYear.text = unlockDate.Year.ToString();

            presetTest.EnableInClassList("seg--active", testMinute);
            for (int i = 0; i < presets.Length; i++)
                presets[i].EnableInClassList("seg--active",
                    !testMinute && unlockDate == RomaniaTime.Today.AddMonths(PresetMonths[i]).Date);
            visPublic.EnableInClassList("seg--active", !personal);
            visPersonal.EnableInClassList("seg--active", personal);

            string when = testMinute
                ? "peste două minute (test)"
                : $"pe {RomaniaTime.DateText(unlockDate)} ({RomaniaTime.Until(RomaniaTime.MidnightUtc(unlockDate))})";
            capsuleNote.text = personal
                ? $"Se deschide doar aici, {when}. O poți deschide doar tu, din contul tău."
                : $"Se deschide doar aici, {when}. Până atunci nimeni nu vede ce ai scris, nici tu.";
        }

        // ---------------------------------------------------------------- tipul urmei

        void SelectKind(TraceKind k)
        {
            if (k != TraceKind.Voice && recorder.IsRecording)
            {
                StopRecordingTimer();
                recorder.Cancel();
            }
            player.Stop();
            kind = k;
            for (int i = 0; i < 3; i++) kinds[i].EnableInClassList("seg--active", (int)k == i);

            textLabel.text = k == TraceKind.Text ? "Mesajul tău" : "Descriere (opțional)";
            text.EnableInClassList("trace-input--short", k != TraceKind.Text);
            Refresh();
            if (!manualEmotion && (text.value ?? "").Trim().Length == 0)
                suggestionLabel.text = k == TraceKind.Text ? SuggestionText : SuggestionMedia;
            RefreshMedia();
        }

        void RefreshMedia()
        {
            UIRouter.SetVisible(photoPanel, kind == TraceKind.Photo);
            UIRouter.SetVisible(photoPick, photoFile == null);
            UIRouter.SetVisible(photoDone, photoFile != null);

            UIRouter.SetVisible(voicePanel, kind == TraceKind.Voice);
            UIRouter.SetVisible(voiceRecord, voiceFile == null);
            UIRouter.SetVisible(voiceDone, voiceFile != null);
            if (!recorder.IsRecording) voiceRecord.text = $"Înregistrează (cel mult {VoiceRecorder.MaxSeconds} s)";
            voiceRecord.EnableInClassList("voice-record--on", recorder.IsRecording);
            voiceDuration.text = MediaStore.FormatSeconds(voiceSeconds);
            voicePlay.text = player.IsPlaying(voiceFile) ? "Oprește" : "Ascultă";
        }

        // ---------------------------------------------------------------- poză

        void TakePhoto(PhotoSource from)
        {
            text.Blur();
            // Foaia se ascunde cât timp fotografiezi, dar rămâne completată.
            UIRouter.SetVisible(overlay, false);
            capture.Open(from, file =>
            {
                if (!IsOpen) { MediaStore.Delete(file); return; }
                UIRouter.SetVisible(overlay, true);
                if (file == null) return;

                MediaStore.Delete(photoFile);
                photoFile = file;
                photoSource = from;
                ClearThumb();
                photoTexture = MediaStore.LoadPhoto(file);
                if (photoTexture != null) photoThumb.style.backgroundImage = new StyleBackground(photoTexture);
                photoLabel.text = from == PhotoSource.Selfie ? "Selfie"
                                : from == PhotoSource.Place ? "Locul" : "Locul, cu urmele";
                RefreshMedia();
            });
        }

        void ClearThumb()
        {
            photoThumb.style.backgroundImage = StyleKeyword.None;
            if (photoTexture != null) UnityEngine.Object.Destroy(photoTexture);
            photoTexture = null;
        }

        // ---------------------------------------------------------------- voce

        void ToggleRecording()
        {
            if (recorder.IsRecording)
            {
                FinishRecording();
                return;
            }
            player.Stop();
            host.StartCoroutine(VoiceRecorder.RequestPermission(granted =>
            {
                if (!IsOpen || kind != TraceKind.Voice) return;
                if (!granted)
                {
                    router.Toast("Fără acces la microfon nu putem înregistra. Îl poți permite din Setări → Aplicații → 3Times.", 5f);
                    return;
                }
                if (!recorder.Start())
                {
                    router.Toast("Microfonul nu a pornit. Mai încearcă.");
                    return;
                }
                recordTimer = voiceRecord.schedule.Execute(UpdateRecordingTimer).Every(100);
                UpdateRecordingTimer();
                RefreshMedia();
            }));
        }

        void UpdateRecordingTimer()
        {
            if (!recorder.IsRecording) return;
            float elapsed = recorder.Elapsed;
            voiceRecord.text = $"Oprește · {MediaStore.FormatSeconds(elapsed)} / {MediaStore.FormatSeconds(VoiceRecorder.MaxSeconds)}";
            if (elapsed >= VoiceRecorder.MaxSeconds) FinishRecording();
        }

        void FinishRecording()
        {
            StopRecordingTimer();
            var result = recorder.Stop();
            if (result == null)
            {
                router.Toast("Înregistrarea e prea scurtă. Mai încearcă și vorbește puțin mai mult.");
            }
            else
            {
                DeleteVoice();
                voiceFile = result.Value.file;
                voiceSeconds = result.Value.seconds;
            }
            RefreshMedia();
        }

        void StopRecordingTimer()
        {
            recordTimer?.Pause();
            recordTimer = null;
        }

        void DeleteVoice()
        {
            MediaStore.Delete(voiceFile);
            voiceFile = null;
            voiceSeconds = 0f;
        }

        void TogglePlayback()
        {
            if (voiceFile == null) return;
            if (player.IsPlaying(voiceFile))
            {
                player.Stop();
            }
            else if (player.Play(voiceFile))
            {
                playWatcher?.Pause();
                playWatcher = voicePlay.schedule.Execute(() =>
                {
                    if (!player.IsPlaying(voiceFile)) { playWatcher?.Pause(); RefreshMedia(); }
                }).Every(200);
            }
            RefreshMedia();
        }

        // ---------------------------------------------------------------- emoție

        /// <summary>
        /// Citește mesajul și propune emoția și intensitatea (utilizatorul le poate schimba). Întâi local, din
        /// cuvinte-cheie (imediat, doar bucurie / tristețe / dor); apoi, dacă serverul răspunde, AI-ul propune trei
        /// emoții libere, fiecare cu forma ei.
        /// </summary>
        void Suggest()
        {
            if (!IsOpen) return;
            AskServer(text.value);
            if (manualEmotion) return;

            var suggestion = EmotionSuggester.Suggest(text.value);
            if (!suggestion.HasValue)
            {
                suggestionLabel.text = kind == TraceKind.Text ? SuggestionText : SuggestionMedia;
                return;
            }
            if (!manualIntensity) intensity = suggestion.Value.Intensity;
            options = TraceStyle.DefaultOptions();
            selected = (int)suggestion.Value.Emotion; // ordinea: bucurie, tristețe, dor
            suggestionLabel.text = $"Propus după mesajul tău: {options[selected].emotion}. O poți schimba.";
            Refresh();
        }

        void AskServer(string message)
        {
            message = (message ?? "").Trim();
            int request = ++suggestionRequest;
            if (message.Length < 3 || !ServerSettings.IsConfigured || manualEmotion) return;

            host.StartCoroutine(TraceApi.SuggestEmotion(message, null, looks =>
            {
                // Între timp textul s-a schimbat, foaia s-a închis sau utilizatorul a ales singur: ignorăm.
                if (looks == null || looks.Count == 0 || request != suggestionRequest || !IsOpen || manualEmotion) return;
                var fresh = new List<EmotionLook>(looks);
                foreach (var extra in TraceStyle.DefaultOptions())
                    if (fresh.Count < 3 && !fresh.Exists(l => l.emotion == extra.emotion)) fresh.Add(extra);
                options = fresh.GetRange(0, 3);
                selected = 0;
                if (!manualIntensity) intensity = Mathf.Clamp(options[0].intensity, 0, 2);
                suggestionLabel.text = $"Propus după mesajul tău: {options[0].emotion}. Poți alege alta sau scrie tu una.";
                Refresh();
            }));
        }

        /// <summary>Utilizatorul scrie o emoție: o punem pe locul al treilea, iar AI-ul îi alege forma.</summary>
        void ApplyCustomEmotion()
        {
            string word = (customEmotion.value ?? "").Trim().ToLowerInvariant();
            customEmotion.Blur();
            if (word.Length == 0)
            {
                router.Toast("Scrie emoția pe care o simți.");
                return;
            }
            if (word.Length > 30) word = word.Substring(0, 30);

            manualEmotion = true;
            suggestionRequest++; // propunerile în curs nu mai contează
            options[2] = TraceStyle.FromWord(word); // o formă provizorie, până răspunde AI-ul
            selected = 2;
            customEmotion.value = "";
            Refresh();

            if (!ServerSettings.IsConfigured)
            {
                suggestionLabel.text = "Ai ales tu emoția.";
                return;
            }
            suggestionLabel.text = $"Căutăm o formă pentru „{word}”…";
            int request = ++customRequest;
            host.StartCoroutine(TraceApi.SuggestEmotion((text.value ?? "").Trim(), word, looks =>
            {
                if (request != customRequest || !IsOpen) return;
                if (looks != null && looks.Count > 0)
                {
                    var look = looks[0];
                    look.emotion = word; // numele rămâne cel scris de utilizator
                    options[2] = look;
                }
                suggestionLabel.text = "Ai ales tu emoția.";
                Refresh();
            }));
        }

        void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                var style = TraceStyle.Of(WithLevel(options[i], 1));
                chipLabels[i].text = options[i].emotion;
                chipIcons[i].style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                bool active = selected == i;
                chips[i].style.borderTopColor = chips[i].style.borderRightColor =
                    chips[i].style.borderBottomColor = chips[i].style.borderLeftColor =
                        active ? new StyleColor(style.Color) : new StyleColor(StyleKeyword.Null);
                chips[i].style.backgroundColor = active ? new StyleColor(style.Soft) : new StyleColor(StyleKeyword.Null);

                intensities[i].EnableInClassList("seg--active", intensity == i);
                shape.EnableInClassList("trace-shape--" + i, intensity == i);
            }

            // Previzualizarea: forma emoției alese, la intensitatea aleasă.
            var chosen = TraceStyle.Of(WithLevel(options[selected], intensity));
            foreach (var k in TraceLook.Keys)
                for (int i = 0; i < 3; i++) shape.RemoveFromClassList($"shape--{k}-{i}");
            shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(chosen));

            // În spatele formei: polaroid (poză) sau microfon (voce), ca în AR.
            previewBackdrop?.RemoveFromHierarchy();
            string type = kind == TraceKind.Photo ? "PHOTO" : kind == TraceKind.Voice ? "VOICE" : "TEXT";
            float size = intensity == 0 ? 72f : intensity == 1 ? 100f : 128f;
            previewBackdrop = TraceTypeBackdrop.Create(type, chosen.Color, size);
            if (previewBackdrop != null && shape.parent != null)
            {
                // Centrat în previzualizare, exact sub formă.
                previewBackdrop.style.left = Length.Percent(50);
                previewBackdrop.style.top = Length.Percent(50);
                previewBackdrop.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                shape.parent.Insert(shape.parent.IndexOf(shape), previewBackdrop);
            }
        }

        static EmotionLook WithLevel(EmotionLook look, int level)
        {
            var copy = look.Copy();
            copy.intensity = level;
            return copy;
        }
    }
}
