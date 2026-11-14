using System;
using System.Collections.Generic;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Propune emoția și intensitatea unei urme pornind de la text.
    /// Versiune locală, simplă: caută cuvinte-cheie românești. Când serverul e gata,
    /// propunerea va veni de la serviciul LLM (ca în diagrama de secvență „Lasă o urmă”),
    /// iar clasa asta rămâne varianta de rezervă, fără internet.
    /// </summary>
    public static class EmotionSuggester
    {
        public struct Suggestion
        {
            public Emotion Emotion;
            public int Intensity; // 0 = încet, 1 = mediu, 2 = puternic
        }

        // Rădăcini de cuvinte, fără diacritice. Un cuvânt se potrivește dacă începe cu rădăcina.
        static readonly string[] JoyStems =
        {
            "fericit", "bucur", "minunat", "frumos", "frumoas", "iubesc", "iubire", "iubit", "ras", "rad", "zamb",
            "super", "grozav", "sarbator", "vesel", "distr", "multum", "recunosc", "castig", "reusi",
            "perfect", "incant", "ador", "drag", "sarut", "cerut", "nunt", "prieten", "petrec"
        };
        static readonly string[] SadnessStems =
        {
            "trist", "plang", "plans", "lacrim", "pierd", "doare", "durer", "singur", "inchis", "murit", "moart",
            "greu", "supar", "dezamag", "pacat", "despart", "regret", "rau", "frica", "obosit", "gol"
        };
        static readonly string[] LongingStems =
        {
            "lipse", "lipsi", "amint", "copilari", "odinioara", "bunic", "candva", "nostalg", "inapoi", "demult",
            "altadata", "plecat", "departe", "astept", "revad", "intorc"
        };
        // Expresii întregi (căutate în tot textul).
        static readonly string[] LongingPhrases = { "mi-e dor", "mie dor", "mi e dor", "pe vremuri", "as vrea", "mi-ar fi placut", "ar fi fost" };
        static readonly string[] JoyPhrases = { "prima data", "cea mai frumoasa", "cel mai frumos", "ne-am vazut", "ne-am cunoscut" };
        static readonly string[] SadnessPhrases = { "nu mai e", "nu mai este", "s-a inchis", "imi pare rau" };

        static readonly string[] Stronger = { "foarte", "enorm", "nespus", "cel mai", "cea mai", "atat de", "niciodata", "mereu", "pentru totdeauna" };
        static readonly string[] Softer = { "putin", "un pic", "usor", "parca", "cumva" };

        /// <summary>Întoarce null dacă textul nu spune destul ca să propunem ceva.</summary>
        public static Suggestion? Suggest(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string normalized = Normalize(text);
            var words = normalized.Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\n', '\t', '(', ')', '"' },
                                         StringSplitOptions.RemoveEmptyEntries);

            int joy = Count(words, JoyStems) + CountPhrases(normalized, JoyPhrases) * 2;
            int sadness = Count(words, SadnessStems) + CountPhrases(normalized, SadnessPhrases) * 2;
            int longing = Count(words, LongingStems) + CountPhrases(normalized, LongingPhrases) * 2;

            // „dor” se potrivește doar ca cuvânt întreg (altfel ar prinde „dorm”, „doresc”).
            foreach (var w in words)
                if (w == "dor" || w == "dorul") longing += 2;

            int best = Math.Max(joy, Math.Max(sadness, longing));
            if (best == 0) return null;

            Emotion emotion;
            if (longing == best) emotion = Emotion.Longing;       // la egalitate, dorul e cel mai specific
            else if (sadness == best) emotion = Emotion.Sadness;
            else emotion = Emotion.Joy;

            int intensity = 1;
            int exclamations = text.Length - text.Replace("!", "").Length;
            if (CountPhrases(normalized, Stronger) > 0 || exclamations >= 2 || best >= 4) intensity = 2;
            else if (CountPhrases(normalized, Softer) > 0) intensity = 0;

            return new Suggestion { Emotion = emotion, Intensity = intensity };
        }

        static int Count(string[] words, string[] stems)
        {
            int n = 0;
            foreach (var w in words)
                foreach (var stem in stems)
                    if (w.StartsWith(stem, StringComparison.Ordinal)) { n++; break; }
            return n;
        }

        static int CountPhrases(string text, string[] phrases)
        {
            int n = 0;
            foreach (var p in phrases)
                if (text.Contains(p)) n++;
            return n;
        }

        static readonly Dictionary<char, char> Diacritics = new Dictionary<char, char>
        {
            { 'ă', 'a' }, { 'â', 'a' }, { 'î', 'i' }, { 'ș', 's' }, { 'ş', 's' }, { 'ț', 't' }, { 'ţ', 't' }
        };

        static string Normalize(string text)
        {
            var chars = text.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Diacritics.TryGetValue(chars[i], out char plain)) chars[i] = plain;
            return new string(chars);
        }
    }
}
