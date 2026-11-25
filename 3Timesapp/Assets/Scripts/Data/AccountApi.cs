using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeTimes.Data
{
    /// <summary>Contul, așa cum îl trimite serverul.</summary>
    [Serializable]
    public class AccountInfo
    {
        public string firstName;    // gol la conturile făcute înainte de nume
        public string lastName;
        public string email;
        public string createdAt;    // ISO 8601, UTC
        public int traceCount;      // câte urme sunt în cont
        public bool hasPassword;
        public bool hasGoogle;

        /// <summary>„Prenume Nume”, sau gol dacă contul n-are nume.</summary>
        public string FullName => $"{firstName} {lastName}".Trim();
    }

    /// <summary>
    /// Cererile pentru cont (backend/README.md, „Conturile”). Ca la TraceApi, toate sunt corutine cu callback.
    /// La autentificare, serverul mută în cont și urmele lăsate fără cont de pe telefonul acesta.
    /// </summary>
    public static class AccountApi
    {
        const int TimeoutSeconds = 12;

        [Serializable]
        class Credentials
        {
            public string email;
            public string password;
        }

        [Serializable]
        class RegisterBody
        {
            public string firstName;
            public string lastName;
            public string email;
            public string password;
        }

        [Serializable]
        class AuthResponse
        {
            public string token;
            public AccountInfo account;
        }

        [Serializable]
        class TracesResponse
        {
            public List<SavedTrace> traces = new List<SavedTrace>();
        }

        static string Base => ServerSettings.Url + "/api/v1/account";

        /// <summary>Înregistrare: done(contul, eroare). La reușită, sesiunea e deja salvată pe telefon.</summary>
        public static IEnumerator Register(string firstName, string lastName, string email, string password,
                                           Action<AccountInfo, string> done) =>
            Authenticate("/register", JsonUtility.ToJson(new RegisterBody
            {
                firstName = firstName ?? "", lastName = lastName ?? "", email = email ?? "", password = password ?? ""
            }), done);

        /// <summary>Autentificare: done(contul, eroare). La reușită, sesiunea e deja salvată pe telefon.</summary>
        public static IEnumerator Login(string email, string password, Action<AccountInfo, string> done) =>
            Authenticate("/login", JsonUtility.ToJson(new Credentials { email = email ?? "", password = password ?? "" }), done);

        static IEnumerator Authenticate(string path, string json, Action<AccountInfo, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(Base + path, "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            TraceApi.Prepare(request, TimeoutSeconds, withAccount: false);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request));
                yield break;
            }
            AuthResponse answer = null;
            try { answer = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text); }
            catch (Exception) { /* tratat mai jos */ }
            if (answer == null || string.IsNullOrEmpty(answer.token) || answer.account == null)
            {
                done?.Invoke(null, "Răspuns neașteptat de la server.");
                yield break;
            }
            AccountSession.Set(answer.token, answer.account.email, answer.account.FullName);
            done?.Invoke(answer.account, null);
        }

        /// <summary>
        /// Contul deschis: done(contul, eroare, sesiune expirată). Dacă serverul nu mai recunoaște sesiunea
        /// (cont șters, alt server), închide contul pe telefon.
        /// </summary>
        public static IEnumerator Me(Action<AccountInfo, string, bool> done)
        {
            using var request = UnityWebRequest.Get(Base);
            TraceApi.Prepare(request, TimeoutSeconds);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                bool expired = request.responseCode == 401;
                if (expired) AccountSession.Clear();
                done?.Invoke(null, TraceApi.ErrorOf(request), expired);
                yield break;
            }
            AccountInfo info = null;
            try { info = JsonUtility.FromJson<AccountInfo>(request.downloadHandler.text); }
            catch (Exception) { /* tratat mai jos */ }
            done?.Invoke(info, info == null ? "Răspuns neașteptat de la server." : null, false);
        }

        /// <summary>Urmele contului, de pe orice telefon (pentru „Harta mea”): done(urmele, eroare, sesiune expirată).</summary>
        public static IEnumerator MyTraces(Action<List<SavedTrace>, string, bool> done)
        {
            using var request = UnityWebRequest.Get(Base + "/traces");
            TraceApi.Prepare(request, TimeoutSeconds);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                bool expired = request.responseCode == 401;
                if (expired) AccountSession.Clear();
                done?.Invoke(null, TraceApi.ErrorOf(request), expired);
                yield break;
            }
            TracesResponse answer = null;
            try { answer = JsonUtility.FromJson<TracesResponse>(request.downloadHandler.text); }
            catch (Exception) { /* tratat mai jos */ }
            var traces = answer?.traces;
            if (traces == null)
            {
                done?.Invoke(null, "Răspuns neașteptat de la server.", false);
                yield break;
            }
            foreach (var t in traces)
            {
                t.id = TraceStore.NormalizeId(t.id);
                t.synced = true;
            }
            done?.Invoke(traces, null, false);
        }

        /// <summary>Deconectare: sesiunea se închide pe telefon imediat, iar pe server cât de repede se poate.</summary>
        public static IEnumerator Logout(Action done)
        {
            if (!AccountSession.IsSignedIn)
            {
                done?.Invoke();
                yield break;
            }
            using var request = new UnityWebRequest(Base + "/logout", "POST", new DownloadHandlerBuffer(), null);
            TraceApi.Prepare(request, 5);
            AccountSession.Clear();
            yield return request.SendWebRequest(); // dacă serverul nu răspunde, sesiunea expiră singură
            done?.Invoke();
        }

        /// <summary>Șterge contul și urmele lui (și pe cele lăsate de pe telefonul acesta): done(gata, eroare).</summary>
        public static IEnumerator Delete(Action<bool, string> done)
        {
            using var request = UnityWebRequest.Delete(Base);
            request.downloadHandler = new DownloadHandlerBuffer();
            TraceApi.Prepare(request, 20);
            yield return request.SendWebRequest();
            bool ok = request.result == UnityWebRequest.Result.Success;
            if (ok || request.responseCode == 401) AccountSession.Clear();
            done?.Invoke(ok, ok ? null : TraceApi.ErrorOf(request));
        }
    }
}
