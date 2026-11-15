using System;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Adresa serverului și identificatorul anonim al telefonului, păstrate în PlayerPrefs.
    /// Adresa se schimbă din Meniu → Server (la hotspot, IP-ul laptopului se poate schimba).
    /// Fără adresă, aplicația merge ca înainte: doar cu urmele proprii, salvate pe telefon.
    /// </summary>
    public static class ServerSettings
    {
        const string UrlKey = "server-url";
        const string DeviceKey = "device-id";

        /// <summary>În Editor, serverul pornit cu Docker pe același laptop.</summary>
        public const string EditorDefault = "http://localhost:8080";

        public static string Url
        {
            get
            {
                string url = PlayerPrefs.GetString(UrlKey, Application.isEditor ? EditorDefault : "");
                return Normalize(url);
            }
            set
            {
                PlayerPrefs.SetString(UrlKey, Normalize(value));
                PlayerPrefs.Save();
            }
        }

        public static bool IsConfigured => Url.Length > 0;

        /// <summary>Un șir aleatoriu, generat o singură dată pe telefon. Serverul păstrează doar o amprentă a lui.</summary>
        public static string DeviceId
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceKey, "");
                if (id.Length < 16)
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(DeviceKey, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        /// <summary>„192.168.43.57” → „http://192.168.43.57:8080”; fără „/” la final.</summary>
        public static string Normalize(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return "";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            url = url.TrimEnd('/');
            // Fără port, la adresele din rețeaua locală punem portul serverului din Docker.
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.Substring(7).Contains(":"))
                url += ":8080";
            return url;
        }
    }
}
