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
        readonly Button reportButton, playButton, deleteButton;
        readonly VisualElement photo, voiceRow;
        readonly Label durationLabel;
        readonly VoicePlayer player;

        SavedTrace current;
        Texture2D photoTexture;
        IVisualElementScheduledItem playWatcher;

        public bool IsOpen { get; private set; }

        /// <summary>Raportare, cu motivul ales (ajunge la admin).</summary>
        public event Action<SavedTrace, string> ReportRequested;

        readonly VisualElement actionsRow, reasons;
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
            actionsRow = root.Q("detail-actions");
            reasons = root.Q("detail-reasons");
            // „Raportează” arată întâi motivele; atingerea unui motiv trimite raportarea.
            reportButton.clicked += () =>
            {
                if (current == null) return;
                ShowReasons(true);
            };
            void Reason(string button, string reason) =>
                root.Q<Button>(button).clicked += () =>
                {
                    if (current == null) return;
                    var trace = current;
                    Close();
                    ReportRequested?.Invoke(trace, reason);
                };
            Reason("detail-reason-offensive", "Jignitoare sau urâtă");
            Reason("detail-reason-spam", "Spam sau reclamă");
            Reason("detail-reason-personal", "Date personale despre cineva");
            Reason("detail-reason-other", "Altceva");
            root.Q<Button>("detail-reason-cancel").clicked += () => ShowReasons(false);
            deleteButton = root.Q<Button>("detail-delete");
            deleteButton.clicked += () =>
            {
                if (current == null) return;
                var trace = current;
                Close();
                DeleteRequested?.Invoke(trace);
            };
        }

        void ShowReasons(bool on)
        {
            UIRouter.SetVisible(reasons, on);
            UIRouter.SetVisible(actionsRow, !on);
            if (on) UIRouter.SetVisible(deleteButton, false);
        }

        public void Open(SavedTrace trace)
        {
            if (trace == null) return;
            current = trace;
            IsOpen = true;
            UIRouter.SetVisible(reasons, false);
            UIRouter.SetVisible(actionsRow, true);

            // Forma și culoarea emoției, așa cum le-a ales AI-ul (sau cele vechi, pentru urmele de dinainte).
            var style = TraceStyle.Of(trace);
            foreach (var k in TraceLook.Keys)
            {
                for (int i = 0; i < 3; i++) shape.RemoveFromClassList($"shape--{k}-{i}");
                emotionLabel.RemoveFromClassList("detail-emotion--" + k);
            }
            shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
            emotionLabel.style.color = style.Ink;
            emotionLabel.text = style.Label;
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

            if (trace.IsCapsuleItem)
            {
                // O urmă dintr-o capsulă deschisă: când a fost pusă, de cine și pentru ce zi.
                string who = trace.visibility == "PERSONAL" ? "Din capsula ta personală" : "Dintr-o capsulă a timpului";
                string by = trace.mine ? "de tine " : "";
                string forDay = ThreeTimes.Core.RomaniaTime.TryParseUtc(trace.unlockAt, out var unlock)
                    ? $", pentru {ThreeTimes.Core.RomaniaTime.DateText(ThreeTimes.Core.RomaniaTime.FromUtc(unlock))}"
                    : "";
                metaLabel.text = $"{who}: pusă {by}{When(trace.createdAt)}{forDay}.";
            }
            else if (ThreeTimes.Core.TraceTime.AnniversaryYears(trace) is int years && years > 0
                     && ThreeTimes.Core.RomaniaTime.TryParseUtc(trace.createdAt, out var created))
            {
                // Urma a revenit la aniversare.
                string day = ThreeTimes.Core.RomaniaTime.DateText(ThreeTimes.Core.RomaniaTime.FromUtc(created));
                metaLabel.text = $"{ThreeTimes.Core.TraceTime.AnniversaryText(years)}: lăsată {(trace.mine ? "de tine " : "")}pe {day}.";
            }
            else
            {
                metaLabel.text = (trace.mine ? "Lăsată de tine, " : "Lăsată ") + When(trace.createdAt);
            }
            deleteButton.text = "Șterge urma mea";
            // Urma ta o poți șterge; pe ale altora le poți raporta.
            // Urmele dintr-o capsulă deschisă nu se mai scot (capsula e sigilată); rămân ștergerea contului și adminul.
            UIRouter.SetVisible(deleteButton, trace.mine && !trace.IsCapsuleItem &&
                                              !ThreeTimes.Core.TraceTime.LockedInYearCapsule(trace));
            UIRouter.SetVisible(reportButton, !trace.mine);
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

    /// <summary>Scurtături pentru felul în care arată o urmă (restul e în TraceStyle).</summary>
    public static class TraceLook
    {
        /// <summary>Clasele USS vechi ale celor trei emoții (imaginile fixe), scoase acum de pe elemente.</summary>
        public static readonly string[] Keys = { "joy", "sadness", "longing" };

        /// <summary>Numele emoției pentru afișare (codurile vechi devin cuvinte românești).</summary>
        public static string Name(string emotion) => TraceStyle.LabelOf(emotion);

        /// <summary>0 = încet, 1 = mediu, 2 = puternic.</summary>
        public static int Level(float intensity) => TraceStyle.LevelOf(intensity);
    }
}
