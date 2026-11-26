using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeTimes.Data
{
    /// <summary>O notificare din aplicație (Meniu → Notificări), de exemplu „urma ta a fost ștearsă de moderatori”.</summary>
    [Serializable]
    public class AppNotification
    {
        public string id;
        public string kind;         // TRACE_REMOVED
        public string title;
        public string body;
        public string createdAt;    // ISO 8601, UTC
        public bool read;
    }

    /// <summary>
    /// Notificările de la server: ale contului deschis sau, fără cont, ale telefonului (backend/README.md, „Notificările”).
    /// </summary>
    public static class NotificationApi
    {
        [Serializable]
        class ListResponse
        {
            public List<AppNotification> notifications = new List<AppNotification>();
            public int unread;
        }

        static string Base => ServerSettings.Url + "/api/v1/notifications";

        /// <summary>done(notificările, eroare): cele mai noi primele.</summary>
        public static IEnumerator List(Action<List<AppNotification>, string> done)
        {
            using var request = UnityWebRequest.Get(Base);
            TraceApi.Prepare(request, 10);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            ListResponse answer = null;
            try { answer = JsonUtility.FromJson<ListResponse>(request.downloadHandler.text); }
            catch (Exception) { /* tratat mai jos */ }
            done?.Invoke(answer?.notifications, answer == null ? "Răspuns neașteptat de la server." : null);
        }

        /// <summary>Marchează citită o notificare (id) sau toate (id = null).</summary>
        public static IEnumerator MarkRead(string id, Action<bool> done = null)
        {
            string url = id == null ? Base + "/read-all" : $"{Base}/{id}/read";
            using var request = new UnityWebRequest(url, "POST", new DownloadHandlerBuffer(), null);
            TraceApi.Prepare(request, 10);
            yield return request.SendWebRequest();
            done?.Invoke(request.result == UnityWebRequest.Result.Success);
        }
    }
}
