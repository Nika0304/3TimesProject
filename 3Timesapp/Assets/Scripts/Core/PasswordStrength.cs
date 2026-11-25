using System;
using System.Linq;

namespace ThreeTimes.Core
{
    /// <summary>
    /// Cât de puternică e o parolă: slabă / medie / puternică, plus un sfat scurt. Aceleași reguli sunt și pe server
    /// (backend: account/PasswordStrength.java), care refuză parolele slabe.
    /// - slabă: sub 8 caractere; un singur fel de caractere (doar litere mici, doar cifre...) sub 16 caractere;
    ///   o parolă foarte des folosită (cu sau fără cifre la final); conține numele sau începutul emailului.
    /// - puternică: cel puțin 12 caractere și 3 feluri (litere mici, mari, cifre, simboluri), sau cel puțin 16 caractere.
    /// - medie: restul.
    /// </summary>
    public static class PasswordStrength
    {
        public enum Level { Empty, Weak, Medium, Strong }

        static readonly string[] Common =
        {
            "parola", "password", "qwerty", "azerty", "asdfgh", "iloveyou", "teiubesc", "admin", "welcome", "letmein",
            "abc123", "123456", "1234567", "12345678", "123456789", "1234567890", "111111", "000000", "3times",
            "timisoara", "romania", "secret", "dragoste", "football", "fotbal", "monkey", "dragon", "sunshine"
        };

        public struct Result
        {
            public Level Level;
            public string Hint;
        }

        public static Result Evaluate(string password, string email = null, string firstName = null, string lastName = null)
        {
            password ??= "";
            int length = password.Length;
            if (length == 0) return new Result { Level = Level.Empty, Hint = "" };

            bool lower = password.Any(char.IsLower);
            bool upper = password.Any(char.IsUpper);
            bool digit = password.Any(char.IsDigit);
            bool other = password.Any(c => !char.IsLetterOrDigit(c));
            int kinds = (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (other ? 1 : 0);

            string plain = password.ToLowerInvariant();
            string core = plain.TrimEnd("0123456789!@#$%^&*()_-+=.?,;:".ToCharArray());

            if (length < 8)
                return Weak($"Mai adaugă {8 - length} {(8 - length == 1 ? "caracter" : "caractere")}.");
            if (Common.Contains(plain) || (core.Length > 0 && Common.Contains(core)))
                return Weak("E o parolă foarte des folosită. Alege alta.");
            if (ContainsPart(plain, LocalPart(email)) || ContainsPart(plain, firstName) || ContainsPart(plain, lastName))
                return Weak("Nu folosi numele sau emailul în parolă.");
            if (kinds < 2 && length < 16)
                return Weak("Amestecă litere cu cifre sau simboluri.");

            if ((length >= 12 && kinds >= 3) || length >= 16)
                return new Result { Level = Level.Strong, Hint = "Parolă puternică." };

            string hint = length < 12 ? "Pentru o parolă puternică: cel puțin 12 caractere." :
                          "Pentru o parolă puternică: adaugă litere mari, cifre sau simboluri.";
            return new Result { Level = Level.Medium, Hint = hint };
        }

        static Result Weak(string hint) => new Result { Level = Level.Weak, Hint = hint };

        static string LocalPart(string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            int at = email.IndexOf('@');
            return at > 0 ? email.Substring(0, at) : null;
        }

        /// <summary>Parola conține cuvântul (de cel puțin 4 litere), fără să conteze literele mari.</summary>
        static bool ContainsPart(string plain, string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return false;
            string p = part.Trim().ToLowerInvariant();
            return p.Length >= 4 && plain.Contains(p);
        }
    }
}
