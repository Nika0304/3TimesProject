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
        public string emotion;        // numele emoției, liber („dor”, „recunoștință”); urmele vechi: JOY, SADNESS, LONGING
        // Forma emoției, aleasă de AI (vezi TraceStyle). Goală la urmele vechi: atunci se folosește forma de dinainte.
        public string shape;
        public string color;
        public string accentColor;
        public int petals;
        public float sharpness;
        public string motion;
        public float intensity;       // 0..1
        public double latitude;
        public double longitude;
        public double altitude;          // altitudinea absolută (WGS84), folosită doar dacă ancora pe teren eșuează
        public float heightAboveTerrain = 1.2f; // înălțimea urmei față de sol (m); urmele vechi primesc 1,2 m
        public float accuracyMeters;  // precizia orizontală din momentul plasării
        public float yawAccuracyDegrees; // precizia orientării din momentul plasării
        public string createdAt;      // ISO 8601, UTC
        public bool reported;         // raportată de telefonul acesta

        // Legătura cu serverul
        public bool mine = true;      // lăsată de telefonul acesta (urmele primite de la server spun singure)
        public string owner;          // urmele salvate pe telefon: emailul contului cu care a fost lăsată; gol = fără cont
        public bool ownerGuessed;     // urmă de dinainte de 26 nov (fără owner): pusă pe contul deschis atunci
        public bool synced;           // urma proprie a ajuns pe server
        public bool hasMedia;         // are poză sau voce pe server (la urmele altora, fișierul se descarcă la deschidere)
        public string syncError;      // serverul a refuzat-o (nu se mai retrimite); gol dacă e în regulă

        // Capsula timpului (modul Viitor). La urme, kind e gol.
        public string kind;           // "CAPSULE" = capsula (locul + data); "CAPSULE_ITEM" = o urmă din capsulă
        public string capsuleId;      // la urmele din capsulă: capsula lor
        public string visibility;     // PUBLIC sau PERSONAL
        public string unlockAt;       // ISO 8601, UTC: de când se poate deschide
        public bool ready;            // data a venit (după ceasul serverului, la ultima cerere)
        public bool opened;           // am deschis-o (de pe telefonul sau din contul acesta)
        public string openedAt;       // când am deschis-o (ISO 8601, UTC)
        public bool @sealed;          // încă închisă pentru mine: urmele din ea nu sunt trimise
        public bool collecting;       // mai primește urme (de grup: până o sigilează creatorul)
        public int itemCount;         // câte urme are
        public int myItems;           // câte sunt ale mele
        public int maxItems;          // cel mult (10)
        /// <summary>Urmele din capsulă, după ce am deschis-o (doar în memorie).</summary>
        [NonSerialized] public List<SavedTrace> items = new List<SavedTrace>();

        // Capsula anului (făcută de aplicație): orașul și anul ei; emoțiile anului, după deschidere.
        public string city;
        public int year;
        [NonSerialized] public List<YearEmotion> yearEmotions = new List<YearEmotion>();

        /// <summary>La o urmă: „Pune-o și în capsula anului” (bifat de utilizator).</summary>
        public bool yearCapsule;

        /// <summary>Capsulă (a oamenilor sau a anului): se vede ca sferă, se deschide la dată.</summary>
        public bool IsCapsule => kind == "CAPSULE" || kind == "YEAR_CAPSULE";
        public bool IsYearCapsule => kind == "YEAR_CAPSULE";
        public bool IsCapsuleItem => kind == "CAPSULE_ITEM";
    }

    /// <summary>O emoție a anului: câte urme au avut-o.</summary>
    [Serializable]
    public class YearEmotion
    {
        public string emotion;
        public string color;
        public int count;
    }

    /// <summary>
    /// Ține urmele PROPRII într-un fișier JSON pe telefon (persistentDataPath/traces.json), împreună cu
    /// ștergerile care încă n-au ajuns la server. Urmele altora vin de la server și nu se salvează.
    /// Fiecare urmă ține minte contul cu care a fost lăsată (owner). Load() dă doar urmele contului deschis acum (sau,
    /// fără cont, pe cele lăsate fără cont); celelalte rămân în fișier, neatinse, până intră din nou contul lor.
    /// </summary>
    public static class TraceStore
    {
        [Serializable]
        class FileData
        {
            public List<SavedTrace> traces = new List<SavedTrace>();
            public List<string> pendingDeletes = new List<string>();
        }

        /// <summary>Id-urile urmelor proprii șterse pe telefon, dar încă nu și pe server.</summary>
        public static List<string> PendingDeletes { get; private set; } = new List<string>();

        /// <summary>Urmele altor conturi lăsate de pe telefonul acesta: se păstrează în fișier, dar nu se arată.</summary>
        static List<SavedTrace> others = new List<SavedTrace>();

        /// <summary>Al cui e telefonul acum: emailul contului deschis (litere mici) sau gol, fără cont.</summary>
        public static string CurrentOwner =>
            AccountSession.IsSignedIn ? (AccountSession.Email ?? "").Trim().ToLowerInvariant() : "";

        static string FilePath => Path.Combine(Application.persistentDataPath, "traces.json");

        public static List<SavedTrace> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<SavedTrace>();
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath));
                PendingDeletes = data?.pendingDeletes ?? new List<string>();
                var traces = data?.traces ?? new List<SavedTrace>();
                string me = CurrentOwner;
                var mine = new List<SavedTrace>();
                others = new List<SavedTrace>();
                foreach (var t in traces)
                {
                    // Urmele vechi aveau id-ul fără cratime; serverul le scrie cu cratime. Le aducem la aceeași formă.
                    t.id = NormalizeId(t.id);
                    t.mine = true;
                    if (t.owner == null)
                    {
                        // Lăsate înainte să ținem minte contul: le dăm contului deschis acum.
                        t.owner = me;
                        t.ownerGuessed = true;
                    }
                    else if (t.owner.Length == 0 && me.Length > 0)
                    {
                        // Lăsate fără cont: la autentificare trec în cont (serverul face la fel).
                        t.owner = me;
                    }
                    (t.owner == me ? mine : others).Add(t);
                }
                return mine;
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
                var all = new List<SavedTrace>(traces);
                all.AddRange(others);
                File.WriteAllText(FilePath, JsonUtility.ToJson(new FileData { traces = all, pendingDeletes = PendingDeletes }, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva urmele: {e.Message}");
            }
        }
    
        /// <summary>Scoate din fișier urmele unui cont (șters); le întoarce, ca să li se șteargă și pozele / vocile.</summary>
        public static List<SavedTrace> DropOwner(string owner)
        {
            var dropped = new List<SavedTrace>();
            if (string.IsNullOrEmpty(owner)) return dropped;
            string key = owner.Trim().ToLowerInvariant();
            dropped = others.FindAll(t => t.owner == key);
            others.RemoveAll(t => t.owner == key);
            return dropped;
        }

        /// <summary>Id-ul în forma serverului: GUID cu cratime, litere mici.</summary>
        public static string NormalizeId(string id) =>
            Guid.TryParse(id, out var g) ? g.ToString() : id;
    }
}
