using System;
using UnityEngine.UIElements;
using ThreeTimes.Core;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul „Lasă o urmă”: o foaie care urcă de jos, cu mesajul, emoția și intensitatea.
    /// Forma din previzualizare se schimbă pe loc. Poza și vocea vin într-o etapă următoare.
    /// </summary>
    public class TraceSheetView
    {
        readonly UIRouter router;
        readonly VisualElement overlay, sheet, shape, dim;
        readonly Label suggestionLabel;
        readonly TextField text;
        readonly Button[] chips = new Button[3];
        readonly Button[] intensities = new Button[3];

        static readonly string[] EmotionKeys = { "joy", "sadness", "longing" };
        static readonly string[] EmotionNames = { "bucurie", "tristețe", "dor" };
        const string SuggestionEmpty = "Scrie mesajul și îți propunem o emoție.";

        // Dacă utilizatorul alege singur, propunerea automată nu-i mai schimbă alegerea.
        bool manualEmotion, manualIntensity;
        IVisualElementScheduledItem pendingSuggestion;

        Emotion emotion = Emotion.Joy;
        int intensity = 1;

        public bool IsOpen { get; private set; }

        /// <summary>Utilizatorul a apăsat „Lasă urma aici” cu un mesaj completat.</summary>
        public event Action<TraceDraft> Submitted;

        public TraceSheetView(VisualElement root, UIRouter router)
        {
            this.router = router;
            overlay = root.Q("trace-overlay");
            sheet = root.Q("trace-sheet");
            shape = root.Q("trace-shape");
            text = root.Q<TextField>("trace-text");
            dim = root.Q("trace-dim");
            suggestionLabel = root.Q<Label>("trace-suggestion");

            // Propunem emoția la jumătate de secundă după ce utilizatorul se oprește din scris.
            text.RegisterValueChangedCallback(_ =>
            {
                pendingSuggestion?.Pause();
                pendingSuggestion = text.schedule.Execute(Suggest).StartingIn(500);
            });

            root.Q<Button>("trace-close").clicked += Close;
            root.Q<Button>("trace-dim").clicked += Close;
            root.Q<Button>("trace-type-photo").clicked += () => router.Toast("Urmele cu poză vin într-o etapă următoare.");
            root.Q<Button>("trace-type-voice").clicked += () => router.Toast("Urmele cu voce vin într-o etapă următoare.");
            root.Q<Button>("trace-submit").clicked += Submit;

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
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            text.value = "";
            emotion = Emotion.Joy;
            intensity = 1;
            manualEmotion = manualIntensity = false;
            suggestionLabel.text = SuggestionEmpty;
            Refresh();

            UIRouter.SetVisible(overlay, true);
            // Scoatem clasele după un cadru, ca tranzițiile să pornească din starea închisă.
            sheet.schedule.Execute(() =>
            {
                sheet.RemoveFromClassList("trace-sheet--closed");
                dim.RemoveFromClassList("sheet-dim--hidden");
            }).StartingIn(30);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            text.Blur(); // închide tastatura
            pendingSuggestion?.Pause();
            sheet.AddToClassList("trace-sheet--closed");
            dim.AddToClassList("sheet-dim--hidden");
            overlay.schedule.Execute(() => { if (!IsOpen) UIRouter.SetVisible(overlay, false); }).StartingIn(600);
        }

        void Submit()
        {
            string message = (text.value ?? "").Trim();
            if (message.Length == 0)
            {
                router.Toast("Scrie câteva cuvinte mai întâi.");
                return;
            }

            var draft = new TraceDraft { Text = message, Emotion = emotion, Intensity = intensity };
            Close();
            Submitted?.Invoke(draft);
        }

        /// <summary>Citește mesajul și propune emoția și intensitatea (utilizatorul le poate schimba).</summary>
        void Suggest()
        {
            if (!IsOpen) return;
            var suggestion = EmotionSuggester.Suggest(text.value);
            if (!suggestion.HasValue)
            {
                if (!manualEmotion) suggestionLabel.text = SuggestionEmpty;
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
        }
    }
}
