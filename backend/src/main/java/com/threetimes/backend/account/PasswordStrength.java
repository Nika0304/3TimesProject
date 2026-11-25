package com.threetimes.backend.account;

import java.util.Locale;
import java.util.Set;

/**
 * Cât de puternică e o parolă. Aceleași reguli ca în aplicație (Core/PasswordStrength.cs), ca ce vede utilizatorul
 * să fie și ce acceptă serverul:
 * - slabă: sub 8 caractere; un singur fel de caractere sub 16 caractere; o parolă foarte des folosită (cu sau fără
 *   cifre / simboluri la final); conține numele, prenumele sau începutul emailului (de cel puțin 4 litere);
 * - puternică: cel puțin 12 caractere și 3 feluri (litere mici, mari, cifre, simboluri), sau cel puțin 16 caractere;
 * - medie: restul.
 */
public final class PasswordStrength {

    public enum Level { WEAK, MEDIUM, STRONG }

    private static final Set<String> COMMON = Set.of(
            "parola", "password", "qwerty", "azerty", "asdfgh", "iloveyou", "teiubesc", "admin", "welcome", "letmein",
            "abc123", "123456", "1234567", "12345678", "123456789", "1234567890", "111111", "000000", "3times",
            "timisoara", "romania", "secret", "dragoste", "football", "fotbal", "monkey", "dragon", "sunshine");

    private static final String TRAILING = "0123456789!@#$%^&*()_-+=.?,;:";

    private PasswordStrength() {
    }

    /** Nivelul și, pentru o parolă slabă, motivul (pe înțelesul utilizatorului). */
    public record Result(Level level, String reason) {
    }

    public static Result evaluate(String password, String email, String firstName, String lastName) {
        String p = password == null ? "" : password;
        int length = p.length();

        boolean lower = false, upper = false, digit = false, other = false;
        for (char c : p.toCharArray()) {
            if (Character.isLowerCase(c)) lower = true;
            else if (Character.isUpperCase(c)) upper = true;
            else if (Character.isDigit(c)) digit = true;
            else if (!Character.isLetterOrDigit(c)) other = true;
        }
        int kinds = (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (other ? 1 : 0);

        String plain = p.toLowerCase(Locale.ROOT);
        int end = plain.length();
        while (end > 0 && TRAILING.indexOf(plain.charAt(end - 1)) >= 0) end--;
        String core = plain.substring(0, end);

        if (length < 8) return weak("Parola trebuie să aibă cel puțin 8 caractere.");
        if (COMMON.contains(plain) || (!core.isEmpty() && COMMON.contains(core)))
            return weak("Parola e prea des folosită. Alege alta.");
        String local = email == null || email.indexOf('@') <= 0 ? null : email.substring(0, email.indexOf('@'));
        if (containsPart(plain, local) || containsPart(plain, firstName) || containsPart(plain, lastName))
            return weak("Nu folosi numele sau emailul în parolă.");
        if (kinds < 2 && length < 16) return weak("Parola e prea slabă: amestecă litere cu cifre sau simboluri.");

        if ((length >= 12 && kinds >= 3) || length >= 16) return new Result(Level.STRONG, null);
        return new Result(Level.MEDIUM, null);
    }

    private static Result weak(String reason) {
        return new Result(Level.WEAK, reason);
    }

    private static boolean containsPart(String plain, String part) {
        if (part == null || part.isBlank()) return false;
        String s = part.trim().toLowerCase(Locale.ROOT);
        return s.length() >= 4 && plain.contains(s);
    }
}
