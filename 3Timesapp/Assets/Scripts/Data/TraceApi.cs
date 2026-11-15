using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using ThreeTimes.Media;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Cererile către server (backend/README.md descrie API-ul). Toate sunt corutine: se pornesc cu
    /// StartCoroutine și raportează rezultatul printr-un callback. Telefonul se prezintă cu X-Device-Id.
    /// </summary>
    public static class TraceApi
    {
        const int TimeoutSeconds = 10;
        const int UploadTimeoutSeconds = 30;

        [Serializable]
        class NearbyResponse
        {
            public List<SavedTrace> traces = new List<SavedTrace>();
        }

        [Serializable]
        class ErrorResponse
        {
            public string error;
        }

        [Serializable]
        class ReportBody
        {
            public string reason;
        }

        static string Base => ServerSettings.Url + "/api/v1/traces";

        static void Prepare(UnityWebRequest request, int timeout = TimeoutSeconds)
        {
            request.timeout = timeout;
            request.SetRequestHeader("X-Device-Id", ServerSettings.DeviceId);
        }

        /// <summary>Mesajul de eroare pentru utilizator: cel trimis de server, sau unul despre conexiune.</summary>
        static string ErrorOf(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
                return "Serverul nu răspunde.";
            try
            {
                var e = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler?.text ?? "");
                if (e != null && !string.IsNullOrEmpty(e.error)) return e.error;
            }
            catch (Exception) { /* răspunsul nu e JSON */ }
            return $"Eroare {request.responseCode}.";
        }

        /// <summary>Răspunde serverul? (folosit de Meniu → Server).</summary>
        public static IEnumerator Health(string url, Action<bool> done)
        {
            using var request = UnityWebRequest.Get(ServerSettings.Normalize(url) + "/actuator/health");
            request.timeout = 5;
            yield return request.SendWebRequest();
            done?.Invoke(request.result == UnityWebRequest.Result.Success &&
                         (request.downloadHandler.text ?? "").Contains("UP"));
        }

        /// <summary>Urmele din jur, de la cea mai apropiată. La eroare: listă null și mesajul.</summary>
        public static IEnumerator Nearby(double lat, double lng, float radius, int limit,
                                         Action<List<SavedTrace>, string> done)
        {
            string url = $"{Base}/nearby?lat={lat.ToString("F7", System.Globalization.CultureInfo.InvariantCulture)}" +
                         $"&lng={lng.ToString("F7", System.Globalization.CultureInfo.InvariantCulture)}" +
                         $"&radius={Mathf.RoundToInt(radius)}&limit={limit}";
            using var request = UnityWebRequest.Get(url);
            Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, ErrorOf(request));
                yield break;
            }
            var response = JsonUtility.FromJson<NearbyResponse>(request.downloadHandler.text);
            var traces = response?.traces ?? new List<SavedTrace>();
            foreach (var t in traces) t.synced = true;
            done?.Invoke(traces, null);
        }

        /// <summary>
        /// Trimite o urmă proprie, cu poza sau vocea ei. done(urma de pe server, eroare, definitiv, refuzată):
        /// „definitiv” = serverul a refuzat-o (4xx) și nu are rost s-o retrimitem;
        /// „refuzată” = moderarea (AI) n-a acceptat textul (422), iar eroarea e motivul, pentru utilizator.
        /// </summary>
        public static IEnumerator Create(SavedTrace trace, Action<SavedTrace, string, bool, bool> done)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("trace", System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(trace)),
                                             "application/json")
            };
            if (trace.type != "TEXT")
            {
                if (!MediaStore.Exists(trace.mediaFile))
                {
                    done?.Invoke(null, "Fișierul urmei lipsește de pe telefon.", true, false);
                    yield break;
                }
                byte[] bytes = File.ReadAllBytes(MediaStore.FullPath(trace.mediaFile));
                string contentType = trace.type == "PHOTO" ? "image/jpeg" : "audio/wav";
                form.Add(new MultipartFormFileSection("media", bytes, trace.mediaFile, contentType));
            }

            using var request = UnityWebRequest.Post(Base, form);
            Prepare(request, UploadTimeoutSeconds);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                bool permanent = request.responseCode >= 400 && request.responseCode < 500;
                done?.Invoke(null, ErrorOf(request), permanent, request.responseCode == 422);
                yield break;
            }
            done?.Invoke(JsonUtility.FromJson<SavedTrace>(request.downloadHandler.text), null, false, false);
        }

        [Serializable]
        class MyIdsResponse
        {
            public List<string> ids = new List<string>();
        }

        /// <summary>Id-urile urmelor mele care mai există pe server (normalizate), sau null dacă serverul nu răspunde.</summary>
        public static IEnumerator MyIds(Action<HashSet<string>> done)
        {
            using var request = UnityWebRequest.Get(Base + "/mine");
            Prepare(request);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null);
                yield break;
            }
            MyIdsResponse answer = null;
            try { answer = JsonUtility.FromJson<MyIdsResponse>(request.downloadHandler.text); }
            catch (Exception) { /* răspuns ciudat: nu ștergem nimic */ }
            if (answer?.ids == null)
            {
                done?.Invoke(null);
                yield break;
            }
            var set = new HashSet<string>();
            foreach (var id in answer.ids) set.Add(TraceStore.NormalizeId(id));
            done?.Invoke(set);
        }

        [Serializable]
        class ModerationBody
        {
            public string text;
        }

        [Serializable]
        class ModerationResponse
        {
            public string verdict;
            public string reason;
        }

        /// <summary>
        /// Verificarea textului de către AI, la „Lasă urma”: done(verdict, motiv), verdict = "ok" / "review" / "block".
        /// La orice problemă (fără server, prea lent): done(null, null), iar urma pleacă; serverul o verifică oricum la salvare.
        /// </summary>
        public static IEnumerator Moderate(string text, Action<string, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ModerationBody { text = text ?? "" }));
            using var request = new UnityWebRequest(ServerSettings.Url + "/api/v1/emotion/moderation", "POST",
                                                    new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            Prepare(request, 12);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, null);
                yield break;
            }
            ModerationResponse answer = null;
            try { answer = JsonUtility.FromJson<ModerationResponse>(request.downloadHandler.text); }
            catch (Exception) { /* răspuns ciudat: lăsăm serverul să decidă la salvare */ }
            done?.Invoke(answer?.verdict, answer?.reason);
        }

        /// <summary>Descarcă poza sau vocea unei urme în media/ și întoarce numele fișierului (sau null).</summary>
        public static IEnumerator DownloadMedia(SavedTrace trace, Action<string> done)
        {
            string ext = trace.type == "PHOTO" ? "jpg" : "wav";
            string file = $"cache-{trace.id}.{ext}";
            if (MediaStore.Exists(file))
            {
                done?.Invoke(file);
                yield break;
            }
            using var request = UnityWebRequest.Get($"{Base}/{trace.id}/media");
            Prepare(request, UploadTimeoutSeconds);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[3Times] Nu am putut descărca fișierul urmei {trace.id}: {ErrorOf(request)}");
                done?.Invoke(null);
                yield break;
            }
            try
            {
                File.WriteAllBytes(MediaStore.FullPath(file), request.downloadHandler.data);
                done?.Invoke(file);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva fișierul urmei: {e.Message}");
                done?.Invoke(null);
            }
        }

        [Serializable]
        class EmotionBody
        {
            public string text;
            public string emotion; // gol = AI-ul propune emoțiile; altfel AI-ul alege doar forma pentru acest cuvânt
        }

        [Serializable]
        class EmotionResponse
        {
            public List<EmotionLook> suggestions = new List<EmotionLook>();
        }

        /// <summary>
        /// Emoțiile propuse de AI pentru un mesaj (până la 3, fiecare cu forma ei), sau, dacă se dă emotion, forma
        /// aleasă de AI pentru acea emoție. La orice problemă (fără server, fără cheie, prea lent): null, iar foaia
        /// rămâne cu propunerea locală.
        /// </summary>
        public static IEnumerator SuggestEmotion(string text, string emotion, Action<List<EmotionLook>> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new EmotionBody { text = text ?? "", emotion = emotion ?? "" }));
            using var request = new UnityWebRequest(ServerSettings.Url + "/api/v1/emotion", "POST",
                                                    new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            Prepare(request, 15); // serverul poate reîncerca la Gemini și poate trece pe modelul de rezervă
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null);
                yield break;
            }
            EmotionResponse answer = null;
            try { answer = JsonUtility.FromJson<EmotionResponse>(request.downloadHandler.text); }
            catch (Exception) { /* răspuns ciudat: rămânem cu propunerea locală */ }
            var looks = answer?.suggestions;
            if (looks != null) looks.RemoveAll(l => l == null || string.IsNullOrWhiteSpace(l.emotion));
            done?.Invoke(looks != null && looks.Count > 0 ? looks : null);
        }

        public static IEnumerator Report(string id, string reason, Action<bool, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ReportBody { reason = reason }));
            using var request = new UnityWebRequest($"{Base}/{id}/report", "POST",
                                                    new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            Prepare(request);
            yield return request.SendWebRequest();
            bool ok = request.result == UnityWebRequest.Result.Success;
            done?.Invoke(ok, ok ? null : ErrorOf(request));
        }

        /// <summary>Șterge o urmă proprie. done(gata): adevărat și dacă serverul nu o mai avea (404).</summary>
        public static IEnumerator Delete(string id, Action<bool> done)
        {
            using var request = UnityWebRequest.Delete($"{Base}/{id}");
            request.downloadHandler = new DownloadHandlerBuffer();
            Prepare(request);
            yield return request.SendWebRequest();
            done?.Invoke(request.result == UnityWebRequest.Result.Success || request.responseCode == 404);
        }
    }
}
