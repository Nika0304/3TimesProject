using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.Data;
using ThreeTimes.Media;

namespace ThreeTimes.UI
{
    /// <summary>
    /// „Ecranul unei urme”: se deschide când atingi o urmă. Arată emoția, mesajul și când a fost
    /// lăsată, cu butoanele Raportează și Închide. La urmele cu poză apare poza, la cele cu voce
    /// un buton „Ascultă”. Urmele proprii pot fi și șterse.
    /// </summary>
    public class TraceDetailView
    {
        readonly UIRouter router;
        readonly VisualElement overlay, card, shape, dim;
        readonly Label emotionLabel, textLabel, metaLabel;
        readonly Button reportButton, playButton;
        readonly VisualElement photo, voiceRow;
        readonly Label durationLabel;
        readonly VoicePlayer player;

        SavedTrace current;
        Texture2D photoTexture;
        IVisualElementScheduledItem playWatcher;

        public bool IsOpen { get; private set; }

        public event Action<SavedTrace> ReportRequested;
        public event Action<SavedTrace> DeleteRequested;

        public TraceDetailView(VisualElement root, UIRouter router, VoicePlayer player)
        {
            this.router = router;
            this.player = player;
            photo = root.Q("detail-photo");
            voiceRow = root.Q("detail-voice");
            playButton = root.Q<Button>("detail-play");
            durationLabel = root.Q<Label>("detail-duration");
            playButton.clicked += TogglePlayback;
            overlay = root.Q("detail-overlay");
            card = root.Q("detail-card");
            dim = root.Q("detail-dim");
            shape = root.Q("detail-shape");
            emotionLabel = root.Q<Label>("detail-emotion");
            textLabel = root.Q<Label>("detail-text");
            metaLabel = root.Q<Label>("detail-meta");
            reportButton = root.Q<Button>("detail-report");

            root.Q<Button>("detail-close").clicked += Close;
            root.Q<Button>("detail-dim").clicked += Close;
            reportButton.clicked += () =>
            {
                if (current == null) return;
                var trace = current;
                Close();
                ReportRequested?.Invoke(trace);
            };
            root.Q<Button>("detail-delete").clicked += () =>
            {
                if (current == null) return;
                var trace = current;
                Close();
                DeleteRequested?.Invoke(trace);
            };
        }

        public void Open(SavedTrace trace)
        {
            if (trace == null) return;
            current = trace;
            IsOpen = true;

            string key = TraceLook.Key(trace.emotion);
            int level = TraceLook.Level(trace.intensity);
            foreach (var k in TraceLook.Keys)
            {
                for (int i = 0; i < 3; i++)
                    shape.EnableInClassList($"shape--{k}-{i}", k == key && i == level);
                emotionLabel.EnableInClassList("detail-emotion--" + k, k == key);
            }
            emotionLabel.text = TraceLook.Name(trace.emotion);
            textLabel.text = trace.content ?? "";

            // Poza
            ClearPhoto();
            if (trace.type == "PHOTO")
            {
                photoTexture = MediaStore.LoadPhoto(trace.mediaFile);
                if (photoTexture != null) photo.style.backgroundImage = new StyleBackground(photoTexture);
            }
            UIRouter.SetVisible(photo, photoTexture != null);

            // Vocea
            bool hasVoice = trace.type == "VOICE" && MediaStore.Exists(trace.mediaFile);
            UIRouter.SetVisible(voiceRow, hasVoice);
            durationLabel.text = MediaStore.FormatSeconds(trace.durationSeconds);
            playButton.text = "Ascultă";
            if ((trace.type == "PHOTO" && photoTexture == null) || (trace.type == "VOICE" && !hasVoice))
                textLabel.text = (textLabel.text + "\n(fișierul urmei lipsește de pe telefon)").Trim();
            UIRouter.SetVisible(textLabel, textLabel.text.Length > 0);

            metaLabel.text = "Lăsată de tine, " + When(trace.createdAt);
            reportButton.text = trace.reported ? "Raportată" : "Raportează";
            reportButton.SetEnabled(!trace.reported);

            UIRouter.SetVisible(overlay, true);
            card.schedule.Execute(() =>
            {
                card.RemoveFromClassList("detail-card--closed");
                dim.RemoveFromClassList("sheet-dim--hidden");
            }).StartingIn(30);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            current = null;
            player.Stop();
            playWatcher?.Pause();
            card.schedule.Execute(() => { if (!IsOpen) ClearPhoto(); }).StartingIn(500);
            card.AddToClassList("detail-card--closed");
            dim.AddToClassList("sheet-dim--hidden");
            overlay.schedule.Execute(() => { if (!IsOpen) UIRouter.SetVisible(overlay, false); }).StartingIn(500);
        }

        void ClearPhoto()
        {
            photo.style.backgroundImage = StyleKeyword.None;
            if (photoTexture != null) UnityEngine.Object.Destroy(photoTexture);
            photoTexture = null;
        }

        void TogglePlayback()
        {
            if (current == null) return;
            string file = current.mediaFile;
            if (player.IsPlaying(file))
            {
                player.Stop();
                playButton.text = "Ascultă";
                return;
            }
            if (!player.Play(file))
            {
                router.Toast("Înregistrarea nu a putut fi redată.");
                return;
            }
            playButton.text = "Oprește";
            playWatcher?.Pause();
            playWatcher = playButton.schedule.Execute(() =>
            {
                if (player.IsPlaying(file)) return;
                playWatcher?.Pause();
                playButton.text = "Ascultă";
            }).Every(200);
        }

        /// <summary>„azi”, „ieri”, „acum 3 zile” sau data, pentru urme mai vechi.</summary>
        static string When(string isoUtc)
        {
            if (!DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
                return "de curând";
            int days = (DateTime.Now.Date - created.ToLocalTime().Date).Days;
            if (days <= 0) return "azi";
            if (days == 1) return "ieri";
            if (days < 30) return $"acum {days} zile";
            return "pe " + created.ToLocalTime().ToString("d.MM.yyyy", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Legătura dintre datele unei urme și felul în care arată (clasă USS, nume).</summary>
    public static class TraceLook
    {
        public static readonly string[] Keys = { "joy", "sadness", "longing" };

        public static string Key(string emotionCode) =>
            emotionCode == "SADNESS" ? "sadness" : emotionCode == "LONGING" ? "longing" : "joy";

        public static string Name(string emotionCode) =>
            emotionCode == "SADNESS" ? "tristețe" : emotionCode == "LONGING" ? "dor" : "bucurie";

        /// <summary>0 = încet, 1 = mediu, 2 = puternic.</summary>
        public static int Level(float intensity) => intensity < 0.5f ? 0 : intensity < 0.84f ? 1 : 2;
    }
}
