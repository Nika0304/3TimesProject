package com.threetimes.backend.account;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.ArrayDeque;
import java.util.Base64;
import java.util.Deque;
import java.util.HexFormat;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.regex.Pattern;

import org.springframework.http.HttpStatus;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.threetimes.backend.account.AccountDtos.AccountView;
import com.threetimes.backend.account.AccountDtos.AuthResponse;
import com.threetimes.backend.account.AccountDtos.Credentials;
import com.threetimes.backend.account.AccountDtos.RegisterRequest;
import com.threetimes.backend.account.AccountDtos.User;
import com.threetimes.backend.common.ApiException;

/**
 * Conturile (opționale): înregistrare și autentificare cu email + parolă, sesiuni, ștergerea contului.
 * Parola se păstrează doar ca bcrypt. Sesiunea e un token aleatoriu; în bază stă doar amprenta lui.
 */
@Service
public class AccountService {

    static final int MIN_PASSWORD = 8;
    static final int MAX_PASSWORD_BYTES = 72;   // bcrypt nu folosește mai mult
    static final int MAX_FAILURES = 10;         // încercări greșite la același email…
    static final long FAILURE_WINDOW_MS = 15 * 60 * 1000L; // …într-un sfert de oră

    private static final Pattern EMAIL = Pattern.compile("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$");
    // Litere (și cu diacritice), spațiu, cratimă, apostrof: „Ana-Maria”, „O'Neill”, „Tîrsîna”.
    private static final Pattern NAME = Pattern.compile("^[\\p{L}][\\p{L} '’-]{0,49}$");

    private final AccountRepository repository;
    private final BCryptPasswordEncoder bcrypt = new BCryptPasswordEncoder();
    private final SecureRandom random = new SecureRandom();
    private final Map<String, Deque<Long>> failures = new ConcurrentHashMap<>();
    // Hash folosit când emailul nu există, ca răspunsul să dureze la fel (să nu se poată ghici ce emailuri au cont).
    private final String dummyHash;

    public AccountService(AccountRepository repository) {
        this.repository = repository;
        this.dummyHash = bcrypt.encode("3times-cont-inexistent");
    }

    @Transactional
    public AuthResponse register(RegisterRequest r, String deviceHash) {
        if (r == null) throw ApiException.badRequest("Lipsesc datele contului.");
        String firstName = name(r.firstName(), "prenumele");
        String lastName = name(r.lastName(), "numele");
        String email = email(new Credentials(r.email(), r.password()));
        String password = r.password() == null ? "" : r.password();
        if (password.getBytes(StandardCharsets.UTF_8).length > MAX_PASSWORD_BYTES)
            throw ApiException.badRequest("Parola e prea lungă.");
        var strength = PasswordStrength.evaluate(password, email, firstName, lastName);
        if (strength.level() == PasswordStrength.Level.WEAK) throw ApiException.badRequest(strength.reason());

        UUID id = UUID.randomUUID();
        if (!repository.insert(id, firstName, lastName, email, bcrypt.encode(password)))
            throw new ApiException(HttpStatus.CONFLICT, "Există deja un cont cu acest email. Autentifică-te.");
        return startSession(id, deviceHash);
    }

    @Transactional
    public AuthResponse login(Credentials c, String deviceHash) {
        String email = email(c);
        String password = c.password() == null ? "" : c.password();
        if (tooManyFailures(email))
            throw new ApiException(HttpStatus.TOO_MANY_REQUESTS,
                    "Prea multe încercări greșite. Mai încearcă peste un sfert de oră.");

        User user = repository.byEmail(email).orElse(null);
        String hash = user == null || user.passwordHash() == null ? dummyHash : user.passwordHash();
        boolean ok = bcrypt.matches(password, hash) && user != null && user.passwordHash() != null;
        if (!ok) {
            noteFailure(email);
            if (user != null && user.passwordHash() == null)
                throw new ApiException(HttpStatus.UNAUTHORIZED, "Contul acesta se deschide cu Google.");
            throw new ApiException(HttpStatus.UNAUTHORIZED, "Email sau parolă greșită.");
        }
        failures.remove(email);
        return startSession(user.id(), deviceHash);
    }

    /** Contul sesiunii din antetul „Authorization: Bearer ...”, sau null (fără antet, sesiune expirată, cont șters). */
    public UUID userOf(String authorization) {
        String token = bearer(authorization);
        return token == null ? null : repository.sessionUser(hash(token)).orElse(null);
    }

    /** La fel, dar cere o sesiune validă (401 altfel). */
    public UUID requireUser(String authorization) {
        UUID user = userOf(authorization);
        if (user == null)
            throw new ApiException(HttpStatus.UNAUTHORIZED, "Sesiunea a expirat. Intră din nou în cont.");
        return user;
    }

    public AccountView view(UUID userId) {
        User user = repository.byId(userId)
                .orElseThrow(() -> new ApiException(HttpStatus.UNAUTHORIZED, "Contul nu mai există."));
        return new AccountView(
                user.firstName(),
                user.lastName(),
                user.email(),
                user.createdAt().withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                repository.traceCount(user.id()),
                user.passwordHash() != null,
                user.googleSub() != null);
    }

    public void logout(String authorization) {
        String token = bearer(authorization);
        if (token != null) repository.deleteSession(hash(token));
    }

    /** Șterge contul, urmele lui și pe cele lăsate de pe telefonul acesta. Întoarce câte urme s-au șters. */
    @Transactional
    public int delete(UUID user, String deviceHash) {
        return repository.deleteAccount(user, deviceHash);
    }

    // ---------------------------------------------------------------- ajutătoare

    private AuthResponse startSession(UUID user, String deviceHash) {
        byte[] bytes = new byte[32];
        random.nextBytes(bytes);
        String token = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);
        repository.createSession(hash(token), user);
        repository.claimDeviceTraces(deviceHash, user);
        return new AuthResponse(token, view(user));
    }

    private static String email(Credentials c) {
        if (c == null) throw ApiException.badRequest("Lipsesc emailul și parola.");
        String email = c.email() == null ? "" : c.email().trim().toLowerCase(Locale.ROOT);
        if (email.isEmpty()) throw ApiException.badRequest("Scrie adresa de email.");
        if (email.length() > 254 || !EMAIL.matcher(email).matches())
            throw ApiException.badRequest("Adresa de email nu pare corectă.");
        return email;
    }

    private static String name(String value, String what) {
        String v = value == null ? "" : value.trim().replaceAll("\\s+", " ");
        if (v.isEmpty()) throw ApiException.badRequest("Scrie " + what + ".");
        if (!NAME.matcher(v).matches()) throw ApiException.badRequest("Verifică " + what + ": doar litere, spațiu sau cratimă.");
        return v;
    }

    private static String bearer(String authorization) {
        if (authorization == null || !authorization.regionMatches(true, 0, "Bearer ", 0, 7)) return null;
        String token = authorization.substring(7).trim();
        return token.length() < 20 || token.length() > 100 ? null : token;
    }

    private static String hash(String token) {
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(token.getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    private boolean tooManyFailures(String email) {
        Deque<Long> list = failures.get(email);
        if (list == null) return false;
        synchronized (list) {
            long now = System.currentTimeMillis();
            while (!list.isEmpty() && now - list.peekFirst() > FAILURE_WINDOW_MS) list.pollFirst();
            return list.size() >= MAX_FAILURES;
        }
    }

    private void noteFailure(String email) {
        Deque<Long> list = failures.computeIfAbsent(email, k -> new ArrayDeque<>());
        synchronized (list) {
            list.addLast(System.currentTimeMillis());
            while (list.size() > MAX_FAILURES) list.pollFirst();
        }
    }
}
