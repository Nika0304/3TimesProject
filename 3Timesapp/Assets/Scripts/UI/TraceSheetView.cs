using System;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.Core;
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
        readonly VoicePlayer player;
        readonly PhotoCaptureView capture;
        readonly VoiceRecorder recorder = new VoiceRecorder();

        readonly VisualElement overlay, sheet, shape, dim;
        readonly Label suggestionLabel, textLabel;
        readonly TextField text;
        readonly Button[] chips = new Button[3];
        readonly Button[] intensities = new Button[3];
        readonly Button[] kinds = new Button[3];

        readonly VisualElement photoPanel, photoPick, photoDone, photoThumb;
        readonly Label photoLabel;
        readonly VisualElement voicePanel, voiceDone;
        readonly Button voiceRecord, voicePlay;
        readonly Label voiceDuration;

        static readonly string[] EmotionKeys = { "joy", "sadness", "longing" };
        static readonly string[] EmotionNames = { "bucurie", "tristețe", "dor" };
        const string SuggestionText = "Scrie mesajul și îți propunem o emoție.";
        const string SuggestionMedia = "Alege emoția sau scrie o descriere și îți propunem una.";

        // Dacă utilizatorul alege singur, propunerea automată nu-i mai schimbă alegerea.
        bool manualEmotion, manualIntensity;
        IVisualElementScheduledItem pendingSuggestion, recordTimer, playWatcher;

        VisualElement previewBackdrop;
        Emotion emotion = Emotion.Joy;
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
            root.Q<Button>("trace-submit").clicked += Submit;

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
                chips[i] = root.Q<Button>("chip-" + EmotionKeys[i]);
                chips[i].clicked += () =>
                {
                    emotion = (Emotion)index;
                    manualEmotion = true;
                    suggestionLabel.text = "Ai ales tu emoția.";
                    Refresh();
                };
                intensities[i] = root.Q<Button>("intensity-" + i);
                intensities[i].clicked += () => { intensity = index; manualIntensity = true; Refresh(); };
            }

            Refresh();
            RefreshMedia();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            text.value = "";
            emotion = Emotion.Joy;
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

            var draft = new TraceDraft { Text = message, Emotion = emotion, Intensity = intensity };
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
            string blocked = submitCheck?.Invoke();
            if (blocked != null)
            {
                router.Toast(blocked, 4f);
                return;
            }

            Close(draft.MediaFile);
            Submitted?.Invoke(draft);
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

        /// <summary>Citește mesajul și propune emoția și intensitatea (utilizatorul le poate schimba).</summary>
        void Suggest()
        {
            if (!IsOpen) return;
            var suggestion = EmotionSuggester.Suggest(text.value);
            if (!suggestion.HasValue)
            {
                if (!manualEmotion) suggestionLabel.text = kind == TraceKind.Text ? SuggestionText : SuggestionMedia;
                return;
            }

            if (!manualIntensity) intensity = suggestion.Value.Intensity;
            if (!manualEmotion)
            {
                emotion = suggestion.Value.Emotion;
                suggestionLabel.text = $"Propus după mesajul tău: {EmotionNames[(int)emotion]}. O poți schimba.";
            }
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                chips[i].EnableInClassList("chip--active", (int)emotion == i);
                intensities[i].EnableInClassList("seg--active", intensity == i);
                shape.EnableInClassList("trace-shape--" + i, intensity == i);
            }

            // O singură clasă shape--<emoție>-<intensitate> activă: ea alege imaginea formei.
            for (int e = 0; e < 3; e++)
                for (int i = 0; i < 3; i++)
                    shape.EnableInClassList($"shape--{EmotionKeys[e]}-{i}", (int)emotion == e && intensity == i);

            // În spatele formei: polaroid (poză) sau microfon (voce), ca în AR.
            previewBackdrop?.RemoveFromHierarchy();
            string type = kind == TraceKind.Photo ? "PHOTO" : kind == TraceKind.Voice ? "VOICE" : "TEXT";
            float size = intensity == 0 ? 72f : intensity == 1 ? 100f : 128f;
            previewBackdrop = TraceTypeBackdrop.Create(type, EmotionKeys[(int)emotion], size);
            if (previewBackdrop != null && shape.parent != null)
            {
                // Centrat în previzualizare, exact sub formă.
                previewBackdrop.style.left = Length.Percent(50);
                previewBackdrop.style.top = Length.Percent(50);
                previewBackdrop.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                shape.parent.Insert(shape.parent.IndexOf(shape), previewBackdrop);
            }
        }
    }
}
