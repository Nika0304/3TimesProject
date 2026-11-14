using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Jurnalul de test: aplicația notează singură localizările și urmele, cu oră și precizie,
    /// ca testele de afară să nu trebuiască scrise de mână. Se păstrează pe telefon
    /// (persistentDataPath/test-log.txt) și se vede din Meniu → Jurnal de test.
    /// </summary>
    public static class TestLog
    {
        const int MaxLines = 400;
        static List<string> lines;

        static string FilePath => Path.Combine(Application.persistentDataPath, "test-log.txt");

        static List<string> Lines
        {
            get
            {
                if (lines != null) return lines;
                lines = new List<string>();
                try
                {
                    if (File.Exists(FilePath)) lines.AddRange(File.ReadAllLines(FilePath));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[3Times] Nu am putut citi jurnalul: {e.Message}");
                }
                return lines;
            }
        }

        public static void Add(string message)
        {
            string line = $"{DateTime.Now:dd.MM HH:mm:ss}  {message}";
            Lines.Add(line);
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
            try
            {
                File.WriteAllLines(FilePath, Lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut scrie jurnalul: {e.Message}");
            }
        }

        /// <summary>Tot jurnalul, cele mai noi rânduri primele.</summary>
        public static string Text()
        {
            if (Lines.Count == 0) return "Jurnalul e gol. Pornește camera și lasă o urmă.";
            var copy = new List<string>(Lines);
            copy.Reverse();
            return string.Join("\n", copy);
        }

        public static void Clear()
        {
            Lines.Clear();
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut goli jurnalul: {e.Message}");
            }
        }
    }
}
