using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>O urmă salvată: mesajul, emoția și punctul de pe glob în care a fost lăsată.</summary>
    [Serializable]
    public class SavedTrace
    {
        public string id;
        public string type = "TEXT";  // TEXT, PHOTO, VOICE (ca în diagrama de clase)
        public string content;        // mesajul; la poză și voce e descrierea (opțională)
        public string mediaFile;      // numele fișierului din media/ (poză .jpg sau voce .wav), gol la text
        public string photoSource;    // SELFIE, PLACE, PLACE_WITH_TRACES (doar la poză)
        public float durationSeconds; // durata înregistrării (doar la voce)
        public string emotion;        // JOY, SADNESS, LONGING
        public float intensity;       // 0..1
        public double latitude;
        public double longitude;
        public double altitude;          // altitudinea absolută (WGS84), folosită doar dacă ancora pe teren eșuează
        public float heightAboveTerrain = 1.2f; // înălțimea urmei față de sol (m); urmele vechi primesc 1,2 m
        public float accuracyMeters;  // precizia orizontală din momentul plasării
        public float yawAccuracyDegrees; // precizia orientării din momentul plasării
        public string createdAt;      // ISO 8601, UTC
        public bool reported;         // marcată pentru moderare (până la server, doar local)
    }

    /// <summary>
    /// Ține urmele într-un fișier JSON pe telefon (persistentDataPath/traces.json).
    /// Când serverul e gata, doar clasa asta se înlocuiește cu apeluri la API.
    /// </summary>
    public static class TraceStore
    {
        [Serializable]
        class FileData
        {
            public List<SavedTrace> traces = new List<SavedTrace>();
        }

        static string FilePath => Path.Combine(Application.persistentDataPath, "traces.json");

        public static List<SavedTrace> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<SavedTrace>();
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                return data?.traces ?? new List<SavedTrace>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut citi urmele: {e.Message}");
                return new List<SavedTrace>();
            }
        }

        public static void Save(List<SavedTrace> traces)
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(new FileData { traces = traces }, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva urmele: {e.Message}");
            }
        }
    }
}
