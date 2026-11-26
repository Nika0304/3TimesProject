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
            public bool verificationRequired;   // emailul nu e verificat încă: s-a trimis un cod
            public string email;
        }

        [Serializable]
        class EmailBody
        {
            public string email;
        }

        [Serializable]
        class CodeBody
        {
            public string email;
            public string code;
            public string password;             // doar la parola nouă
        }

        [Serializable]
        class TracesResponse
        {
            public List<SavedTrace> traces = new List<SavedTrace>();
        }

        static string Base => ServerSettings.Url + "/api/v1/account";

        /// <summary>
        /// Înregistrare: done(contul, eroare, cere cod). Contul nou se activează cu codul trimis pe email, așa că de
        /// obicei răspunsul e „cere cod” (fără cont încă); apoi Verify.
        /// </summary>
        public static IEnumerator Register(string firstName, string lastName, string email, string password,
                                           Action<AccountInfo, string, bool> done) =>
            Authenticate("/register", JsonUtility.ToJson(new RegisterBody
            {
                firstName = firstName ?? "", lastName = lastName ?? "", email = email ?? "", password = password ?? ""
            }), done);

        /// <summary>
        /// Autentificare: done(contul, eroare, cere cod). La reușită, sesiunea e deja salvată pe telefon. Dacă emailul
        /// nu e verificat încă, serverul trimite un cod și răspunsul e „cere cod”.
        /// </summary>
        public static IEnumerator Login(string email, string password, Action<AccountInfo, string, bool> done) =>
            Authenticate("/login", JsonUtility.ToJson(new Credentials { email = email ?? "", password = password ?? "" }), done);

        /// <summary>Codul de 6 cifre primit pe email activează contul: done(contul, eroare, _).</summary>
        public static IEnumerator Verify(string email, string code, Action<AccountInfo, string, bool> done) =>
            Authenticate("/verify", JsonUtility.ToJson(new CodeBody { email = email ?? "", code = code ?? "", password = null }), done);

        /// <summary>Parola nouă, cu codul primit pe email: done(contul, eroare, _). Contul se deschide pe telefon.</summary>
        public static IEnumerator ResetPassword(string email, string code, string password,
                                                Action<AccountInfo, string, bool> done) =>
            Authenticate("/password/reset",
                         JsonUtility.ToJson(new CodeBody { email = email ?? "", code = code ?? "", password = password ?? "" }), done);

        /// <summary>Alt cod de verificare pe email: done(gata, eroare).</summary>
        public static IEnumerator ResendCode(string email, Action<bool, string> done) => PostEmail("/verify/resend", email, done);

        /// <summary>„Mi-am uitat parola”: cod pe email, dacă emailul are cont: done(gata, eroare).</summary>
        public static IEnumerator ForgotPassword(string email, Action<bool, string> done) =>
            PostEmail("/password/forgot", email, done);

        static IEnumerator PostEmail(string path, string email, Action<bool, string> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new EmailBody { email = email ?? "" }));
            using var request = new UnityWebRequest(Base + path, "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            TraceApi.Prepare(request, 20, withAccount: false);
            yield return request.SendWebRequest();
            bool ok = request.result == UnityWebRequest.Result.Success;
            done?.Invoke(ok, ok ? null : TraceApi.ErrorOf(request));
        }

        static IEnumerator Authenticate(string path, string json, Action<AccountInfo, string, bool> done)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            using var request = new UnityWebRequest(Base + path, "POST", new DownloadHandlerBuffer(), new UploadHandlerRaw(body));
            request.SetRequestHeader("Content-Type", "application/json");
            // Trimiterea emailului cu codul poate dura câteva secunde.
            TraceApi.Prepare(request, 25, withAccount: false);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                done?.Invoke(null, TraceApi.ErrorOf(request), false);
                yield break;
            }
            AuthResponse answer = null;
            try { answer = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text); }
            catch (Exception) { /* tratat mai jos */ }
            if (answer != null && answer.verificationRequired)
            {
                done?.Invoke(null, null, true);
                yield break;
            }
            if (answer == null || string.IsNullOrEmpty(answer.token) || answer.account == null)
            {
                done?.Invoke(null, "Răspuns neașteptat de la server.", false);
                yield break;
            }
            AccountSession.Set(answer.token, answer.account.email, answer.account.FullName);
            done?.Invoke(answer.account, null, false);
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
