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

    /** Răspunsul la înregistrare / autentificare: tokenul sesiunii (se trimite ca „Authorization: Bearer ...”). */
    public record AuthResponse(String token, AccountView account) {
    }

    /** Un cont din bază. */
    record User(java.util.UUID id, String firstName, String lastName, String email, String passwordHash, String googleSub,
                java.time.OffsetDateTime createdAt) {
    }
}
