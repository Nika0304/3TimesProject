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
    /// Contul nou se activează cu un cod de 6 cifre trimis pe email; „Mi-am uitat parola” trimite tot un cod, cu care
    /// se alege o parolă nouă (panoul „account-code”).
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

        // Codul primit pe email
        enum CodeMode { None, Verify, Reset }
        readonly VisualElement codePanel, newPasswordBox;
        readonly Label codeTitle, codeIntro, codeError, newStrength;
        readonly TextField codeField, newPasswordField;
        readonly Button forgot, codeSubmit, codeResend, codeCancel;
        CodeMode codeMode;
        string codeEmail = "";

        bool registerMode;
        bool busy;

        public bool IsOpen => IsVisible(screen);

        /// <summary>Contul a fost șters (cu urmele lui): urmele proprii trebuie scoase și de pe telefon.</summary>
        public event Action AccountDeleted;

        /// <summary>Contul s-a deschis (autentificare, cont activat, parolă nouă): aplicația se întoarce în Prezent.</summary>
        public event Action SignedIn;

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
            codePanel = root.Q("account-code");
            newPasswordBox = root.Q("account-new-password-box");
            codeTitle = root.Q<Label>("account-code-title");
            codeIntro = root.Q<Label>("account-code-intro");
            codeError = root.Q<Label>("account-code-error");
            newStrength = root.Q<Label>("account-new-strength");
            codeField = root.Q<TextField>("account-code-field");
            newPasswordField = root.Q<TextField>("account-new-password");
            forgot = root.Q<Button>("account-forgot");
            codeSubmit = root.Q<Button>("account-code-submit");
            codeResend = root.Q<Button>("account-code-resend");
            codeCancel = root.Q<Button>("account-code-cancel");

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
            codeField.keyboardType = TouchScreenKeyboardType.NumberPad;
            codeField.maxLength = 7;
            newPasswordField.isPasswordField = true;
            newPasswordField.maskChar = '•';
            newPasswordField.maxLength = 72;
            newPasswordField.RegisterValueChangedCallback(_ => UpdateNewStrength());

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
            forgot.clicked += ForgotPassword;
            PasswordToggle(passwordField, root.Q<Button>("account-password-show"));
            PasswordToggle(newPasswordField, root.Q<Button>("account-new-password-show"));
            // Enter trimite formularul (intră în cont / creează contul / confirmă codul).
            foreach (var field in new[] { firstNameField, lastNameField, emailField, passwordField })
                OnEnter(field, Submit);
            foreach (var field in new[] { codeField, newPasswordField })
                OnEnter(field, SubmitCode);
            codeSubmit.clicked += SubmitCode;
            codeResend.clicked += ResendCode;
            codeCancel.clicked += LeaveCode;

            AccountSession.Changed += UpdateMenuLabel;
            UpdateMenuLabel();
            SetRegisterMode(false);
        }

        /// <summary>„Arată” / „Ascunde” lângă un câmp de parolă.</summary>
        static void PasswordToggle(TextField field, Button button)
        {
            if (field == null || button == null) return;
            button.clicked += () =>
            {
                field.isPasswordField = !field.isPasswordField;
                button.text = field.isPasswordField ? "Arată" : "Ascunde";
            };
        }

        /// <summary>Enter (sau „Gata” pe tastatura telefonului) într-un câmp pornește acțiunea.</summary>
        static void OnEnter(TextField field, Action action)
        {
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.character != '\n') return;
                e.StopPropagation();
                action();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<NavigationSubmitEvent>(e =>
            {
                e.StopPropagation();
                action();
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Parolele se ascund din nou (la închidere).</summary>
        void HidePasswords()
        {
            passwordField.isPasswordField = true;
            newPasswordField.isPasswordField = true;
            var a = screen.Q<Button>("account-password-show");
            var b = screen.Q<Button>("account-new-password-show");
            if (a != null) a.text = "Arată";
            if (b != null) b.text = "Arată";
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
            newPasswordField.value = "";
            HidePasswords();
            UIRouter.SetVisible(screen, false);
        }

        /// <summary>Butonul „Înapoi” de pe Android: întâi renunță la ștergere, apoi închide ecranul.</summary>
        public void Back()
        {
            if (IsVisible(confirm)) HideConfirm();
            else if (codeMode != CodeMode.None) LeaveCode();
            else Close();
        }

        void ShowState()
        {
            bool inAccount = AccountSession.IsSignedIn;
            if (inAccount) codeMode = CodeMode.None;
            bool coding = codeMode != CodeMode.None;
            UIRouter.SetVisible(signedOut, !inAccount && !coding);
            UIRouter.SetVisible(codePanel, coding);
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
            UIRouter.SetVisible(forgot, !register);
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
            Action<AccountInfo, string, bool> done = (info, message, needsCode) =>
            {
                SetBusy(false, null);
                if (needsCode)
                {
                    passwordField.value = "";
                    ShowCode(CodeMode.Verify, email);
                    TestLog.Add(register ? "Cont creat, aștept codul din email" : "Cont neverificat, cod trimis");
                    return;
                }
                if (info == null)
                {
                    ShowError(message);
                    return;
                }
                passwordField.value = "";
                ShowState();
                ShowAccount(info);
                TestLog.Add(register ? "Cont creat" : "Autentificare reușită");
                Close();
                SignedIn?.Invoke();
                router.Toast(register ? "Contul e gata. Urmele de pe telefonul acesta au trecut în cont."
                                      : (info.firstName?.Length > 0 ? $"Bine ai revenit, {info.firstName}!" : "Ai intrat în cont."), 4f);
            };
            host.StartCoroutine(register
                ? AccountApi.Register(first, last, email, password, done)
                : AccountApi.Login(email, password, done));
        }

        // ---------------------------------------------------------------- codul primit pe email

        void ForgotPassword()
        {
            if (busy) return;
            if (!ServerSettings.IsConfigured)
            {
                ShowError("Contul are nevoie de server: pune adresa lui în Meniu, la „Server”.");
                return;
            }
            string email = (emailField.value ?? "").Trim();
            if (email.Length == 0 || !email.Contains("@") || !email.Contains("."))
            {
                ShowError("Scrie mai sus emailul contului, apoi apasă din nou „Mi-am uitat parola”.");
                return;
            }
            BlurAll();
            SetBusy(true, null);
            forgot.text = "Se trimite…";
            host.StartCoroutine(AccountApi.ForgotPassword(email, (ok, message, noAccount) =>
            {
                SetBusy(false, null);
                forgot.text = "Mi-am uitat parola";
                if (noAccount)
                {
                    // Emailul n-are cont: îl trimitem la „Cont nou”, cu emailul deja scris.
                    passwordField.value = "";
                    SetRegisterMode(true);
                    ShowError(message);
                    firstNameField.Focus();
                    return;
                }
                if (!ok)
                {
                    ShowError(message);
                    return;
                }
                passwordField.value = "";
                ShowCode(CodeMode.Reset, email);
                TestLog.Add("Cod pentru parola nouă cerut");
            }));
        }

        void ShowCode(CodeMode mode, string email)
        {
            codeMode = mode;
            codeEmail = email;
            bool reset = mode == CodeMode.Reset;
            codeTitle.text = reset ? "Alege o parolă nouă" : "Verifică-ți emailul";
            codeIntro.text = reset
                ? $"Dacă {email} are cont, am trimis acolo un cod de 6 cifre. Scrie-l aici, împreună cu parola nouă. " +
                  "Nu-l vezi? Uită-te și în Spam."
                : $"Am trimis un cod de 6 cifre pe {email}. Scrie-l aici ca să-ți activezi contul. " +
                  "Nu-l vezi? Uită-te și în Spam.";
            codeSubmit.text = reset ? "Schimbă parola" : "Activează contul";
            codeField.value = "";
            newPasswordField.value = "";
            UIRouter.SetVisible(newPasswordBox, reset);
            UIRouter.SetVisible(newStrength, false);
            ShowCodeError(null);
            ShowState();
        }

        void LeaveCode()
        {
            if (busy) return;
            BlurAll();
            codeMode = CodeMode.None;
            ShowState();
        }

        void UpdateNewStrength()
        {
            var result = PasswordStrength.Evaluate(newPasswordField.value, codeEmail, "", "");
            UIRouter.SetVisible(newStrength, result.Level != PasswordStrength.Level.Empty);
            newStrength.text = result.Level == PasswordStrength.Level.Strong ? "Parolă puternică." :
                               result.Level == PasswordStrength.Level.Medium ? $"Medie. {result.Hint}" : $"Slabă. {result.Hint}";
            newStrength.EnableInClassList("strength__label--weak", result.Level == PasswordStrength.Level.Weak);
        }

        void SubmitCode()
        {
            if (busy || codeMode == CodeMode.None) return;
            string code = "";
            foreach (char c in codeField.value ?? "") if (char.IsDigit(c)) code += c;
            if (code.Length != 6) { ShowCodeError("Scrie cele 6 cifre din email."); return; }

            bool reset = codeMode == CodeMode.Reset;
            string password = newPasswordField.value ?? "";
            if (reset)
            {
                if (password.Length == 0) { ShowCodeError("Scrie parola nouă."); return; }
                var result = PasswordStrength.Evaluate(password, codeEmail, "", "");
                if (result.Level == PasswordStrength.Level.Weak)
                {
                    ShowCodeError("Parola e prea slabă. " + result.Hint);
                    return;
                }
            }

            BlurAll();
            SetBusy(true, null);
            codeSubmit.text = "Se verifică…";
            Action<AccountInfo, string, bool> done = (info, message, _) =>
            {
                SetBusy(false, null);
                codeSubmit.text = reset ? "Schimbă parola" : "Activează contul";
                if (info == null)
                {
                    ShowCodeError(message ?? "Răspuns neașteptat de la server.");
                    return;
                }
                codeMode = CodeMode.None;
                codeField.value = "";
                newPasswordField.value = "";
                ShowState();
                ShowAccount(info);
                TestLog.Add(reset ? "Parolă schimbată" : "Email verificat, cont activ");
                Close();
                SignedIn?.Invoke();
                router.Toast(reset ? "Parola e schimbată. Ai intrat în cont."
                                   : "Emailul e verificat. Contul e gata, iar urmele de pe telefonul acesta au trecut în cont.", 4f);
            };
            host.StartCoroutine(reset
                ? AccountApi.ResetPassword(codeEmail, code, password, done)
                : AccountApi.Verify(codeEmail, code, done));
        }

        void ResendCode()
        {
            if (busy || codeMode == CodeMode.None) return;
            bool reset = codeMode == CodeMode.Reset;
            SetBusy(true, null);
            codeResend.text = "Se trimite…";
            Action<bool, string, bool> done = (ok, message, _) =>
            {
                SetBusy(false, null);
                codeResend.text = "Trimite alt cod";
                if (!ok)
                {
                    ShowCodeError(message);
                    return;
                }
                ShowCodeError(null);
                codeField.value = "";
                router.Toast($"Am trimis un cod nou pe {codeEmail}.", 3f);
            };
            host.StartCoroutine(reset
                ? AccountApi.ForgotPassword(codeEmail, done)
                : AccountApi.ResendCode(codeEmail, (ok, message) => done(ok, message, false)));
        }

        void ShowCodeError(string message)
        {
            codeError.text = message ?? "";
            UIRouter.SetVisible(codeError, !string.IsNullOrEmpty(message));
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
            forgot.SetEnabled(!on);
            codeSubmit.SetEnabled(!on);
            codeResend.SetEnabled(!on);
            codeCancel.SetEnabled(!on);
            if (on && label != null) submit.text = label;
            if (!on) submit.text = registerMode ? "Creează contul" : "Intră în cont";
        }

        void BlurAll()
        {
            firstNameField.Blur();
            lastNameField.Blur();
            emailField.Blur();
            passwordField.Blur();
            codeField.Blur();
            newPasswordField.Blur();
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
