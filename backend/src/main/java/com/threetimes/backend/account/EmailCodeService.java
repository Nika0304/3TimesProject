package com.threetimes.backend.account;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.time.OffsetDateTime;
import java.util.HexFormat;
import java.util.UUID;

import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;

import com.threetimes.backend.account.AccountDtos.User;
import com.threetimes.backend.common.ApiException;

/**
 * Codurile de 6 cifre trimise pe email: unul pentru verificarea contului nou, altul pentru parola nouă.
 * Un cod ține 15 minute și are 5 încercări; unul nou se poate cere după un minut și îl înlocuiește pe cel vechi.
 */
@Service
public class EmailCodeService {

    public enum Purpose {
        VERIFY("verify"), RESET("reset");

        final String db;

        Purpose(String db) {
            this.db = db;
        }
    }

    static final int VALID_MINUTES = 15;
    static final int MAX_ATTEMPTS = 5;
    static final int RESEND_SECONDS = 60;

    private final AccountRepository repository;
    private final CodeMailer mailer;
    private final SecureRandom random = new SecureRandom();

    public EmailCodeService(AccountRepository repository, CodeMailer mailer) {
        this.repository = repository;
        this.mailer = mailer;
    }

    /**
     * Trimite un cod nou. Dacă s-a trimis unul acum mai puțin de un minut: cu quiet = true nu face nimic
     * (vechiul cod e încă bun), altfel răspunde 429.
     */
    public void send(User user, Purpose purpose, boolean quiet) {
        OffsetDateTime last = repository.codeSentAt(user.id(), purpose.db).orElse(null);
        if (last != null && last.isAfter(OffsetDateTime.now().minusSeconds(RESEND_SECONDS))) {
            if (quiet) return;
            throw new ApiException(HttpStatus.TOO_MANY_REQUESTS,
                    "Am trimis un cod acum câteva secunde. Mai așteaptă un minut până ceri altul.");
        }
        String code = String.format("%06d", random.nextInt(1_000_000));
        repository.saveCode(user.id(), purpose.db, hash(user.id(), purpose, code), VALID_MINUTES);
        mailer.send(user.email(), user.firstName(), code, purpose);
    }

    /** Verifică codul scris de om. Dacă e bun, îl șterge (nu se poate folosi de două ori); altfel 400 cu motivul. */
    public void check(User user, Purpose purpose, String typed) {
        String code = typed == null ? "" : typed.replaceAll("\\s", "");
        if (!code.matches("\\d{6}")) throw ApiException.badRequest("Scrie cele 6 cifre din email.");
        var stored = repository.code(user.id(), purpose.db).orElse(null);
        if (stored == null || stored.expiresAt().isBefore(OffsetDateTime.now()))
            throw ApiException.badRequest("Codul a expirat. Cere unul nou.");
        if (stored.attempts() >= MAX_ATTEMPTS)
            throw ApiException.badRequest("Prea multe încercări greșite. Cere un cod nou.");
        boolean ok = MessageDigest.isEqual(
                stored.codeHash().getBytes(StandardCharsets.US_ASCII),
                hash(user.id(), purpose, code).getBytes(StandardCharsets.US_ASCII));
        if (!ok) {
            repository.codeFailed(user.id(), purpose.db);
            int left = MAX_ATTEMPTS - stored.attempts() - 1;
            throw ApiException.badRequest(left > 0
                    ? "Codul nu e corect. Mai ai " + (left == 1 ? "o încercare." : left + " încercări.")
                    : "Codul nu e corect. Cere un cod nou.");
        }
        repository.deleteCode(user.id(), purpose.db);
    }

    private static String hash(UUID user, Purpose purpose, String code) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256")
                    .digest((user + ":" + purpose.db + ":" + code).getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
