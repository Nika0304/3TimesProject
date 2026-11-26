package com.threetimes.backend.account;

/** Formatele JSON pentru conturi. Numele câmpurilor sunt aceleași ca în aplicația Unity (AccountApi). */
public final class AccountDtos {

    private AccountDtos() {
    }

    /** Autentificare cu email și parolă. */
    public record Credentials(String email, String password) {
    }

    /** Înregistrare: prenumele, numele, emailul și parola. */
    public record RegisterRequest(String firstName, String lastName, String email, String password) {
    }

    /** Contul, așa cum îl vede aplicația. */
    public record AccountView(
            String firstName,       // gol la conturile făcute înainte de nume
            String lastName,
            String email,
            String createdAt,       // ISO 8601, UTC
            int traceCount,         // câte urme sunt în cont
            boolean hasPassword,
            boolean hasGoogle) {
    }

    /**
     * Răspunsul la înregistrare / autentificare: tokenul sesiunii (se trimite ca „Authorization: Bearer ...”).
     * Dacă emailul nu e încă verificat: fără token, verificationRequired = true și emailul unde s-a trimis codul.
     */
    public record AuthResponse(String token, AccountView account, boolean verificationRequired, String email) {

        static AuthResponse session(String token, AccountView account) {
            return new AuthResponse(token, account, false, account.email());
        }

        static AuthResponse needsCode(String email) {
            return new AuthResponse(null, null, true, email);
        }
    }

    /** Doar emailul: cere alt cod de verificare / cod pentru parola nouă. */
    public record EmailRequest(String email) {
    }

    /** Codul de 6 cifre primit pe email, pentru activarea contului. */
    public record VerifyRequest(String email, String code) {
    }

    /** Parola nouă, cu codul primit pe email. */
    public record ResetRequest(String email, String code, String password) {
    }

    /** Un cont din bază. */
    record User(java.util.UUID id, String firstName, String lastName, String email, String passwordHash, String googleSub,
                java.time.OffsetDateTime createdAt, boolean emailVerified) {
    }

    /** Un cod trimis pe email, din bază (doar amprenta lui). */
    record StoredCode(String codeHash, java.time.OffsetDateTime expiresAt, int attempts) {
    }
}
