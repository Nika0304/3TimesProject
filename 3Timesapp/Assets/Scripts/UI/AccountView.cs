using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.Core;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Ecranul „Cont” (wireframe 1.6.1), deschis din Meniu. Contul e opțional.
    /// Fără cont, două file: „Intră în cont” (email, parolă) și „Cont nou” (prenume, nume, email, parolă cu indicatorul
    /// slabă / medie / puternică; parola slabă nu se acceptă), plus „Continuă fără cont”.
    /// Cu cont: numele, emailul, câte urme are, „Deconectează-te” și „Șterge contul și urmele mele” (cu confirmare).
    /// </summary>
    public class AccountView
    {
        readonly UIRouter router;
        readonly MonoBehaviour host;

        readonly VisualElement screen, signedOut, signedIn, confirm, names, strength;
        readonly VisualElement[] strengthBars;
        readonly Label title, intro, serverNote, error, currentEmail, meta, deleteError, menuLabel, strengthLabel;
        readonly TextField firstNameField, lastNameField, emailField, passwordField;
        readonly Button tabLogin, tabRegister, submit, skip, logout, delete, deleteCancel, deleteConfirm;

        bool registerMode;
        bool busy;

        public bool IsOpen => IsVisible(screen);

        /// <summary>Contul a fost șters (cu urmele lui): urmele proprii trebuie scoase și de pe telefon.</summary>
        public event Action AccountDeleted;

        public AccountView(VisualElement root, UIRouter router, MonoBehaviour host)
        {
            this.router = router;
            this.host = host;

            screen = root.Q("screen-account");
            signedOut = root.Q("account-signed-out");
            signedIn = root.Q("account-signed-in");
            confirm = root.Q("account-confirm");
            names = root.Q("account-names");
            strength = root.Q("account-strength");
            strengthBars = new[] { root.Q("strength-1"), root.Q("strength-2"), root.Q("strength-3") };
            title = root.Q<Label>("account-title");
            intro = root.Q<Label>("account-intro");
            serverNote = root.Q<Label>("account-server-note");
            error = root.Q<Label>("account-error");
            currentEmail = root.Q<Label>("account-current-email");
            meta = root.Q<Label>("account-meta");
            deleteError = root.Q<Label>("account-delete-error");
            menuLabel = root.Q<Label>("menu-account-label");
            strengthLabel = root.Q<Label>("strength-label");
            firstNameField = root.Q<TextField>("account-first-name");
            lastNameField = root.Q<TextField>("account-last-name");
            emailField = root.Q<TextField>("account-email");
            passwordField = root.Q<TextField>("account-password");
            tabLogin = root.Q<Button>("account-tab-login");
            tabRegister = root.Q<Button>("account-tab-register");
            submit = root.Q<Button>("account-submit");
            skip = root.Q<Button>("account-skip");
            logout = root.Q<Button>("account-logout");
            delete = root.Q<Button>("account-delete");
            deleteCancel = root.Q<Button>("account-delete-cancel");
            deleteConfirm = root.Q<Button>("account-delete-confirm");

            firstNameField.maxLength = 50;
            lastNameField.maxLength = 50;
            emailField.keyboardType = TouchScreenKeyboardType.EmailAddress;
            emailField.maxLength = 254;
            passwordField.isPasswordField = true;
            passwordField.maskChar = '•';
            passwordField.maxLength = 72;
            passwordField.RegisterValueChangedCallback(_ => UpdateStrength());
            emailField.RegisterValueChangedCallback(_ => UpdateStrength());
            firstNameField.RegisterValueChangedCallback(_ => UpdateStrength());
            lastNameField.RegisterValueChangedCallback(_ => UpdateStrength());

            root.Q<Button>("account-back").clicked += Close;
            skip.clicked += Close;
            tabLogin.clicked += () => SetRegisterMode(false);
            tabRegister.clicked += () => SetRegisterMode(true);
            submit.clicked += Submit;
            logout.clicked += Logout;
            delete.clicked += () =>
            {
                UIRouter.SetVisible(deleteError, false);
                UIRouter.SetVisible(confirm, true);
                UIRouter.SetVisible(delete, false);
            };
            deleteCancel.clicked += HideConfirm;
            deleteConfirm.clicked += DeleteAccount;

            AccountSession.Changed += UpdateMenuLabel;
            UpdateMenuLabel();
            SetRegisterMode(false);
        }

        // ---------------------------------------------------------------- deschidere / închidere

        public void Open()
        {
            UIRouter.SetVisible(screen, true);
            ShowState();
            if (AccountSession.IsSignedIn) Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            BlurAll();
            passwordField.value = "";
            UIRouter.SetVisible(screen, false);
        }

        /// <summary>Butonul „Înapoi” de pe Android: întâi renunță la ștergere, apoi închide ecranul.</summary>
        public void Back()
        {
            if (IsVisible(confirm)) HideConfirm();
            else Close();
        }

        void ShowState()
        {
            bool inAccount = AccountSession.IsSignedIn;
            UIRouter.SetVisible(signedOut, !inAccount);
            UIRouter.SetVisible(signedIn, inAccount);
            HideConfirm();
            UIRouter.SetVisible(error, false);

            bool hasServer = ServerSettings.IsConfigured;
            UIRouter.SetVisible(serverNote, !inAccount && !hasServer);
            submit.SetEnabled(hasServer && !busy);

            if (inAccount)
            {
                string name = AccountSession.Name;
                currentEmail.text = name.Length > 0 ? name : AccountSession.Email;
                if (string.IsNullOrEmpty(meta.text)) meta.text = name.Length > 0 ? AccountSession.Email : "Se încarcă…";
            }
            else
            {
                SetRegisterMode(registerMode);
            }
        }

        /// <summary>„Intră în cont” sau „Cont nou”: alte câmpuri, alt titlu, alt buton.</summary>
        void SetRegisterMode(bool register)
        {
            registerMode = register;
            tabLogin.EnableInClassList("seg--active", !register);
            tabRegister.EnableInClassList("seg--active", register);
            title.text = register ? "Fă-ți un cont" : "Bine ai revenit";
            intro.text = register
                ? "Contul e opțional. Cu el îți vezi pe hartă locurile unde ai lăsat urme, de pe orice telefon."
                : "Intră cu emailul și parola contului tău.";
            UIRouter.SetVisible(names, register);
            UIRouter.SetVisible(strength, register && !string.IsNullOrEmpty(passwordField.value));
            submit.text = register ? "Creează contul" : "Intră în cont";
            UIRouter.SetVisible(error, false);
            UpdateStrength();
        }

        void UpdateMenuLabel()
        {
            if (menuLabel == null) return;
            if (!AccountSession.IsSignedIn) menuLabel.text = "Cont";
            else menuLabel.text = $"Cont · {(AccountSession.Name.Length > 0 ? AccountSession.Name : AccountSession.Email)}";
        }

        // ---------------------------------------------------------------- puterea parolei

        PasswordStrength.Result CurrentStrength() =>
            PasswordStrength.Evaluate(passwordField.value, emailField.value, firstNameField.value, lastNameField.value);

        void UpdateStrength()
        {
            if (!registerMode)
            {
                UIRouter.SetVisible(strength, false);
                return;
            }
            var result = CurrentStrength();
            UIRouter.SetVisible(strength, result.Level != PasswordStrength.Level.Empty);
            int filled = result.Level == PasswordStrength.Level.Strong ? 3 : result.Level == PasswordStrength.Level.Medium ? 2 :
                         result.Level == PasswordStrength.Level.Weak ? 1 : 0;
            string kind = result.Level == PasswordStrength.Level.Strong ? "strong" :
                          result.Level == PasswordStrength.Level.Medium ? "medium" : "weak";
            for (int i = 0; i < strengthBars.Length; i++)
            {
                var bar = strengthBars[i];
                bar.EnableInClassList("strength__bar--weak", i < filled && kind == "weak");
                bar.EnableInClassList("strength__bar--medium", i < filled && kind == "medium");
                bar.EnableInClassList("strength__bar--strong", i < filled && kind == "strong");
            }
            string word = result.Level == PasswordStrength.Level.Strong ? "Puternică" :
                          result.Level == PasswordStrength.Level.Medium ? "Medie" : "Slabă";
            strengthLabel.text = result.Level == PasswordStrength.Level.Strong ? "Parolă puternică." : $"{word}. {result.Hint}";
            strengthLabel.EnableInClassList("strength__label--weak", result.Level == PasswordStrength.Level.Weak);
        }

        // ---------------------------------------------------------------- acțiuni

        void Submit()
        {
            if (busy) return;
            if (!ServerSettings.IsConfigured)
            {
                ShowError("Contul are nevoie de server: pune adresa lui în Meniu, la „Server”.");
                return;
            }
            string first = (firstNameField.value ?? "").Trim();
            string last = (lastNameField.value ?? "").Trim();
            string email = (emailField.value ?? "").Trim();
            string password = passwordField.value ?? "";

            if (registerMode && first.Length == 0) { ShowError("Scrie prenumele."); return; }
            if (registerMode && last.Length == 0) { ShowError("Scrie numele."); return; }
            if (email.Length == 0 || !email.Contains("@") || !email.Contains(".")) { ShowError("Scrie adresa de email."); return; }
            if (password.Length == 0) { ShowError("Scrie parola."); return; }
            if (registerMode)
            {
                var result = CurrentStrength();
                if (result.Level == PasswordStrength.Level.Weak)
                {
                    ShowError("Parola e prea slabă. " + result.Hint);
                    return;
                }
            }

            BlurAll();
            bool register = registerMode;
            SetBusy(true, register ? "Se creează contul…" : "Se verifică…");
            Action<AccountInfo, string> done = (info, message) =>
            {
                SetBusy(false, null);
                if (info == null)
                {
                    ShowError(message);
                    return;
                }
                passwordField.value = "";
                ShowState();
                ShowAccount(info);
                TestLog.Add(register ? "Cont creat" : "Autentificare reușită");
                router.Toast(register ? "Contul e gata. Urmele de pe telefonul acesta au trecut în cont."
                                      : (info.firstName?.Length > 0 ? $"Bine ai revenit, {info.firstName}!" : "Ai intrat în cont."), 4f);
            };
            host.StartCoroutine(register
                ? AccountApi.Register(first, last, email, password, done)
                : AccountApi.Login(email, password, done));
        }

        void Refresh()
        {
            if (!ServerSettings.IsConfigured)
            {
                meta.text = "Fără server nu pot vedea detaliile contului.";
                return;
            }
            host.StartCoroutine(AccountApi.Me((info, message, expired) =>
            {
                if (!IsOpen) return;
                if (expired)
                {
                    ShowState();
                    ShowError("Sesiunea a expirat. Intră din nou în cont.");
                    return;
                }
                if (info == null)
                {
                    meta.text = message;
                    return;
                }
                ShowAccount(info);
            }));
        }

        void ShowAccount(AccountInfo info)
        {
            AccountSession.UpdateProfile(info.email, info.FullName);
            UpdateMenuLabel();
            string name = info.FullName;
            currentEmail.text = name.Length > 0 ? name : info.email;
            string urme = info.traceCount == 0 ? "nicio urmă" : info.traceCount == 1 ? "o urmă" :
                          info.traceCount < 20 ? $"{info.traceCount} urme" : $"{info.traceCount} de urme";
            string since = FormatDate(info.createdAt);
            string details = since.Length > 0 ? $"Cont făcut pe {since} · {urme}" : urme;
            meta.text = name.Length > 0 ? $"{info.email}\n{details}" : details;
        }

        void Logout()
        {
            if (busy) return;
            host.StartCoroutine(AccountApi.Logout(() => { }));
            meta.text = "";
            emailField.value = "";
            SetRegisterMode(false);
            ShowState();
            TestLog.Add("Deconectare");
            router.Toast("Te-ai deconectat. Urmele tale rămân în cont.", 3f);
        }

        void DeleteAccount()
        {
            if (busy) return;
            SetBusy(true, null);
            deleteConfirm.text = "Se șterge…";
            host.StartCoroutine(AccountApi.Delete((ok, message) =>
            {
                SetBusy(false, null);
                deleteConfirm.text = "Da, șterge tot";
                if (!ok)
                {
                    if (!AccountSession.IsSignedIn)
                    {
                        // Sesiunea expirase: nu s-a șters nimic; trebuie să intre din nou în cont.
                        ShowState();
                        ShowError("Sesiunea a expirat. Intră din nou în cont, apoi șterge-l.");
                        return;
                    }
                    deleteError.text = message;
                    UIRouter.SetVisible(deleteError, true);
                    return;
                }
                meta.text = "";
                emailField.value = "";
                firstNameField.value = "";
                lastNameField.value = "";
                TestLog.Add("Cont șters");
                AccountDeleted?.Invoke();
                Close();
                router.Toast("Contul și urmele tale au fost șterse.", 4f);
            }));
        }

        // ---------------------------------------------------------------- ajutătoare

        void SetBusy(bool on, string label)
        {
            busy = on;
            submit.SetEnabled(!on && ServerSettings.IsConfigured);
            tabLogin.SetEnabled(!on);
            tabRegister.SetEnabled(!on);
            logout.SetEnabled(!on);
            deleteConfirm.SetEnabled(!on);
            deleteCancel.SetEnabled(!on);
            if (on && label != null) submit.text = label;
            if (!on) submit.text = registerMode ? "Creează contul" : "Intră în cont";
        }

        void BlurAll()
        {
            firstNameField.Blur();
            lastNameField.Blur();
            emailField.Blur();
            passwordField.Blur();
        }

        void HideConfirm()
        {
            UIRouter.SetVisible(confirm, false);
            UIRouter.SetVisible(delete, true);
        }

        void ShowError(string message)
        {
            error.text = message;
            UIRouter.SetVisible(error, !string.IsNullOrEmpty(message));
        }

        static readonly string[] Months =
        {
            "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie",
            "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie"
        };

        static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
                return "";
            d = d.ToLocalTime();
            return $"{d.Day} {Months[d.Month - 1]} {d.Year}";
        }

        static bool IsVisible(VisualElement e) => e != null && !e.ClassListContains("hidden");
    }
}
