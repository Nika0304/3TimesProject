using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Cum arată emoția unei urme: numele emoției (liber, propus de AI după text) și forma aleasă tot de AI,
    /// dintr-un set de forme de bază, cu parametri. Aceleași câmpuri vin de la server (POST /api/v1/emotion).
    /// </summary>
    [Serializable]
    public class EmotionLook
    {
        public string emotion = "bucurie"; // numele emoției, în română, cu litere mici („dor”, „recunoștință”)
        public int intensity = 1;          // 0 = încet, 1 = mediu, 2 = puternic
        public string shape = "flower";    // una din TraceStyle.Shapes
        public string color = "#F5B83D";   // culoarea principală, #RRGGBB
        public string accentColor = "#E8961A";
        public int petals = 6;             // câte petale / raze / vârfuri / brațe (3–12)
        public float sharpness = 0.3f;     // 0 = rotund, moale; 1 = ascuțit, colțuros
        public string motion = "pulse";    // una din TraceStyle.Motions

        public EmotionLook Copy() => (EmotionLook)MemberwiseClone();
    }

    /// <summary>Forma rezolvată a unei urme, gata de desenat (în AR sau în interfață).</summary>
    public struct TraceStyle
    {
        public static readonly string[] Shapes =
            { "flower", "drop", "crescent", "heart", "star", "spiral", "wave", "cloud", "flame", "sprout", "burst", "ring" };
        public static readonly string[] Motions = { "pulse", "sway", "drift", "flicker", "breathe", "spin" };

        public string Label;
        public string Shape;
        public Color Color;
        public Color Accent;
        public int Petals;
        public float Sharpness;
        public string Motion;
        public int Level; // 0 = încet, 1 = mediu, 2 = puternic

        /// <summary>O cheie stabilă pentru cache-ul iconițelor din interfață.</summary>
        public string Key =>
            $"{Shape}|{ColorUtility.ToHtmlStringRGB(Color)}|{ColorUtility.ToHtmlStringRGB(Accent)}|{Petals}|{Sharpness:0.00}|{Level}";

        // ---------------------------------------------------------------- de unde vine forma

        public static int LevelOf(float intensity) => intensity < 0.5f ? 0 : intensity < 0.84f ? 1 : 2;

        /// <summary>Forma unei urme salvate. Urmele vechi (JOY / SADNESS / LONGING, fără formă) primesc forma lor de dinainte.</summary>
        public static TraceStyle Of(SavedTrace t)
        {
            int level = LevelOf(t.intensity);
            if (string.IsNullOrEmpty(t.shape))
            {
                var preset = Legacy(t.emotion) ?? FromWord(LabelOf(t.emotion));
                preset.intensity = level;
                return Of(preset);
            }
            return Of(new EmotionLook
            {
                emotion = LabelOf(t.emotion),
                intensity = level,
                shape = t.shape,
                color = t.color,
                accentColor = t.accentColor,
                petals = t.petals,
                sharpness = t.sharpness,
                motion = t.motion
            });
        }

        public static TraceStyle Of(EmotionLook look)
        {
            var main = ParseColor(look.color, new Color(0.961f, 0.722f, 0.239f));
            return new TraceStyle
            {
                Label = string.IsNullOrWhiteSpace(look.emotion) ? "emoție" : look.emotion.Trim(),
                Shape = Array.IndexOf(Shapes, look.shape) >= 0 ? look.shape : "flower",
                Color = main,
                Accent = ParseColor(look.accentColor, Color.Lerp(main, Color.white, 0.4f)),
                Petals = Mathf.Clamp(look.petals <= 0 ? 6 : look.petals, 3, 12),
                Sharpness = Mathf.Clamp01(look.sharpness),
                Motion = Array.IndexOf(Motions, look.motion) >= 0 ? look.motion : "breathe",
                Level = Mathf.Clamp(look.intensity, 0, 2)
            };
        }

        /// <summary>Numele afișat: codurile vechi devin cuvinte românești.</summary>
        public static string LabelOf(string emotion)
        {
            switch (emotion)
            {
                case "JOY": return "bucurie";
                case "SADNESS": return "tristețe";
                case "LONGING": return "dor";
                default: return string.IsNullOrWhiteSpace(emotion) ? "emoție" : emotion.Trim();
            }
        }

        /// <summary>Cele trei emoții de la început, cu formele din machetă (folosite și de propunerea locală).</summary>
        public static EmotionLook Legacy(string emotion)
        {
            switch (emotion)
            {
                case "JOY": case "bucurie":
                    return new EmotionLook { emotion = "bucurie", shape = "flower", color = "#F5B83D", accentColor = "#E8961A", petals = 6, sharpness = 0.2f, motion = "pulse" };
                case "SADNESS": case "tristețe":
                    return new EmotionLook { emotion = "tristețe", shape = "drop", color = "#5B95E6", accentColor = "#3F78C9", petals = 3, sharpness = 0.3f, motion = "drift" };
                case "LONGING": case "dor":
                    return new EmotionLook { emotion = "dor", shape = "crescent", color = "#9C78E3", accentColor = "#C9B4FF", petals = 3, sharpness = 0.4f, motion = "sway" };
                default:
                    return null;
            }
        }

        /// <summary>Propunerile de pornire (fără AI): cele trei emoții de bază.</summary>
        public static List<EmotionLook> DefaultOptions() =>
            new List<EmotionLook> { Legacy("bucurie"), Legacy("tristețe"), Legacy("dor") };

        /// <summary>
        /// O formă pentru un cuvânt scris de utilizator, când serverul / AI-ul nu e disponibil: aleasă stabil după cuvânt
        /// (același cuvânt dă mereu aceeași formă și culoare).
        /// </summary>
        public static EmotionLook FromWord(string word)
        {
            word = string.IsNullOrWhiteSpace(word) ? "emoție" : word.Trim().ToLowerInvariant();
            var known = Legacy(word);
            if (known != null) return known;

            int hash = 23;
            foreach (char c in word) hash = unchecked(hash * 31 + c);
            var random = new System.Random(hash);
            float hue = (float)random.NextDouble();
            var main = Color.HSVToRGB(hue, 0.45f + 0.2f * (float)random.NextDouble(), 0.9f);
            var accent = Color.HSVToRGB((hue + 0.08f) % 1f, 0.6f, 0.75f);
            return new EmotionLook
            {
                emotion = word,
                shape = Shapes[random.Next(Shapes.Length)],
                color = "#" + ColorUtility.ToHtmlStringRGB(main),
                accentColor = "#" + ColorUtility.ToHtmlStringRGB(accent),
                petals = 3 + random.Next(8),
                sharpness = (float)random.NextDouble(),
                motion = Motions[random.Next(Motions.Length)]
            };
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string h = hex.Trim();
            if (!h.StartsWith("#")) h = "#" + h;
            return ColorUtility.TryParseHtmlString(h, out var c) ? c : fallback;
        }

        /// <summary>O variantă mai deschisă a culorii, pentru fundalurile din interfață (cipuri, carduri).</summary>
        public Color Soft => Color.Lerp(Color, Color.white, 0.82f);

        /// <summary>Culoarea pentru text: aceeași nuanță, mai închisă, ca să se citească pe alb.</summary>
        public Color Ink
        {
            get
            {
                Color.RGBToHSV(Color, out float h, out float s, out float v);
                return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), Mathf.Min(v, 0.62f));
            }
        }
    }
}
