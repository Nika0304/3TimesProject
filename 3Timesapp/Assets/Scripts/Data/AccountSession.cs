using System;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Contul deschis pe telefon (opțional): tokenul sesiunii și emailul, păstrate în PlayerPrefs.
    /// Tokenul se trimite la fiecare cerere către server („Authorization: Bearer ...”, vezi TraceApi).
    /// Fără cont, aplicația merge ca înainte, doar cu id-ul anonim al telefonului.
    /// </summary>
    public static class AccountSession
    {
        const string TokenKey = "account-token";
        const string EmailKey = "account-email";
        const string NameKey = "account-name";

        /// <summary>Contul s-a deschis sau s-a închis (autentificare, deconectare, ștergere, sesiune expirată).</summary>
        public static event Action Changed;

        public static string Token => PlayerPrefs.GetString(TokenKey, "");
        public static string Email => PlayerPrefs.GetString(EmailKey, "");
        /// <summary>Prenumele și numele (gol la conturile făcute înainte de nume).</summary>
        public static string Name => PlayerPrefs.GetString(NameKey, "");
        public static bool IsSignedIn => Token.Length > 0;

        /// <summary>Emailul contului închis ultima dată (după deconectare / ștergere), pentru curățenia de pe telefon.</summary>
        public static string PreviousEmail { get; private set; } = "";

        public static void Set(string token, string email, string name)
        {
            PlayerPrefs.SetString(TokenKey, token ?? "");
            PlayerPrefs.SetString(EmailKey, email ?? "");
            PlayerPrefs.SetString(NameKey, name ?? "");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Actualizează emailul și numele păstrate (după ce serverul trimite contul).</summary>
        public static void UpdateProfile(string email, string name)
        {
            if (!IsSignedIn) return;
            PlayerPrefs.SetString(EmailKey, email ?? "");
            PlayerPrefs.SetString(NameKey, name ?? "");
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            bool was = IsSignedIn;
            if (was) PreviousEmail = Email;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(EmailKey);
            PlayerPrefs.DeleteKey(NameKey);
            PlayerPrefs.Save();
            if (was) Changed?.Invoke();
        }
    }
}
