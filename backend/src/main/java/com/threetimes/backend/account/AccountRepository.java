package com.threetimes.backend.account;

import java.sql.Types;
import java.time.OffsetDateTime;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Repository;

import com.threetimes.backend.account.AccountDtos.User;

/** Interogările SQL pentru conturi și sesiuni. */
@Repository
public class AccountRepository {

    /** Cât ține o sesiune (aplicația rămâne autentificată), dacă nu se deconectează. */
    static final int SESSION_DAYS = 365;

    private final NamedParameterJdbcTemplate jdbc;

    public AccountRepository(NamedParameterJdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    /** Adaugă contul. Întoarce false dacă emailul e deja folosit. */
    public boolean insert(UUID id, String firstName, String lastName, String email, String passwordHash) {
        try {
            jdbc.update("""
                    INSERT INTO app_user (id, first_name, last_name, email, password_hash)
                    VALUES (:id, :first, :last, :email, :hash)
                    """,
                    new MapSqlParameterSource()
                            .addValue("id", id)
                            .addValue("first", firstName)
                            .addValue("last", lastName)
                            .addValue("email", email)
                            .addValue("hash", passwordHash));
            return true;
        } catch (DuplicateKeyException e) {
            return false;
        }
    }

    public Optional<User> byEmail(String email) {
        return one("SELECT * FROM app_user WHERE email = :v", email);
    }

    public Optional<User> byId(UUID id) {
        return one("SELECT * FROM app_user WHERE id = :v", id);
    }

    private Optional<User> one(String sql, Object value) {
        List<User> rows = jdbc.query(sql, new MapSqlParameterSource("v", value), (rs, n) -> new User(
                rs.getObject("id", UUID.class),
                rs.getString("first_name"),
                rs.getString("last_name"),
                rs.getString("email"),
                rs.getString("password_hash"),
                rs.getString("google_sub"),
                rs.getObject("created_at", OffsetDateTime.class),
                rs.getBoolean("email_verified")));
        return rows.stream().findFirst();
    }

    /** Un cont încă neverificat primește alt nume / altă parolă (cineva se înregistrează din nou cu același email). */
    public void updateUnverified(UUID id, String firstName, String lastName, String passwordHash) {
        jdbc.update("""
                UPDATE app_user SET first_name = :first, last_name = :last, password_hash = :hash
                WHERE id = :id AND NOT email_verified
                """, new MapSqlParameterSource()
                .addValue("id", id).addValue("first", firstName).addValue("last", lastName).addValue("hash", passwordHash));
    }

    public void markVerified(UUID id) {
        jdbc.update("UPDATE app_user SET email_verified = TRUE WHERE id = :id", new MapSqlParameterSource("id", id));
    }

    public void setPassword(UUID id, String passwordHash) {
        jdbc.update("UPDATE app_user SET password_hash = :hash WHERE id = :id",
                new MapSqlParameterSource().addValue("id", id).addValue("hash", passwordHash));
    }

    /** Închide toate sesiunile contului (după parola nouă: telefoanele vechi trebuie să intre din nou). */
    public void deleteSessions(UUID user) {
        jdbc.update("DELETE FROM user_session WHERE user_id = :u", new MapSqlParameterSource("u", user));
    }

    // ---------------------------------------------------------------- codurile trimise pe email

    /** Salvează codul nou (îl înlocuiește pe cel vechi, cu încercările de la zero). */
    public void saveCode(UUID user, String purpose, String codeHash, int minutes) {
        jdbc.update("""
                INSERT INTO email_code (user_id, purpose, code_hash, expires_at)
                VALUES (:u, :p, :h, now() + make_interval(mins => :m))
                ON CONFLICT (user_id, purpose) DO UPDATE
                SET code_hash = EXCLUDED.code_hash, expires_at = EXCLUDED.expires_at, attempts = 0, created_at = now()
                """, new MapSqlParameterSource().addValue("u", user).addValue("p", purpose).addValue("h", codeHash)
                .addValue("m", minutes));
    }

    public Optional<OffsetDateTime> codeSentAt(UUID user, String purpose) {
        List<OffsetDateTime> rows = jdbc.query("SELECT created_at FROM email_code WHERE user_id = :u AND purpose = :p",
                new MapSqlParameterSource().addValue("u", user).addValue("p", purpose),
                (rs, n) -> rs.getObject("created_at", OffsetDateTime.class));
        return rows.stream().findFirst();
    }

    public Optional<AccountDtos.StoredCode> code(UUID user, String purpose) {
        List<AccountDtos.StoredCode> rows = jdbc.query(
                "SELECT code_hash, expires_at, attempts FROM email_code WHERE user_id = :u AND purpose = :p",
                new MapSqlParameterSource().addValue("u", user).addValue("p", purpose),
                (rs, n) -> new AccountDtos.StoredCode(rs.getString("code_hash"),
                        rs.getObject("expires_at", OffsetDateTime.class), rs.getInt("attempts")));
        return rows.stream().findFirst();
    }

    public void codeFailed(UUID user, String purpose) {
        jdbc.update("UPDATE email_code SET attempts = attempts + 1 WHERE user_id = :u AND purpose = :p",
                new MapSqlParameterSource().addValue("u", user).addValue("p", purpose));
    }

    public void deleteCode(UUID user, String purpose) {
        jdbc.update("DELETE FROM email_code WHERE user_id = :u AND purpose = :p",
                new MapSqlParameterSource().addValue("u", user).addValue("p", purpose));
    }

    public void createSession(String tokenHash, UUID user) {
        jdbc.update("INSERT INTO user_session (token_hash, user_id) VALUES (:t, :u)",
                new MapSqlParameterSource().addValue("t", tokenHash).addValue("u", user));
    }

    /** Contul sesiunii, dacă sesiunea există și n-a expirat. */
    public Optional<UUID> sessionUser(String tokenHash) {
        List<UUID> rows = jdbc.query("""
                SELECT user_id FROM user_session
                WHERE token_hash = :t AND created_at > now() - make_interval(days => :days)
                """, new MapSqlParameterSource().addValue("t", tokenHash).addValue("days", SESSION_DAYS),
                (rs, n) -> rs.getObject("user_id", UUID.class));
        return rows.stream().findFirst();
    }

    public void deleteSession(String tokenHash) {
        jdbc.update("DELETE FROM user_session WHERE token_hash = :t", new MapSqlParameterSource("t", tokenHash));
    }

    /** Urmele lăsate de pe acest telefon fără cont trec în cont (ca să apară în „Harta mea”). */
    public int claimDeviceTraces(String deviceHash, UUID user) {
        if (deviceHash == null) return 0;
        var p = new MapSqlParameterSource().addValue("u", user).addValue("d", deviceHash, Types.CHAR);
        // Și capsulele lăsate de pe telefon trec în cont (rămân „ale mele” și de pe alt telefon).
        jdbc.update("UPDATE capsule SET user_id = :u WHERE owner_hash = :d AND user_id IS NULL", p);
        jdbc.update("UPDATE capsule_item SET user_id = :u WHERE owner_hash = :d AND user_id IS NULL", p);
        // Notificările telefonului (urme lăsate fără cont) trec și ele în cont.
        jdbc.update("UPDATE notification SET user_id = :u WHERE device_hash = :d AND user_id IS NULL", p);
        // Și capsulele deschise fără cont de pe telefon rămân deschise pentru cont.
        jdbc.update("""
                UPDATE capsule_open o SET user_id = :u WHERE o.device_hash = :d AND o.user_id IS NULL
                  AND NOT EXISTS (SELECT 1 FROM capsule_open x WHERE x.capsule_id = o.capsule_id AND x.user_id = :u)
                """, p);
        return jdbc.update("UPDATE trace SET user_id = :u WHERE owner_hash = :d AND user_id IS NULL", p);
    }

    public int traceCount(UUID user) {
        Integer n = jdbc.queryForObject("SELECT count(*) FROM trace WHERE user_id = :u",
                new MapSqlParameterSource("u", user), Integer.class);
        return n == null ? 0 : n;
    }

    /**
     * Șterge contul, urmele lui și urmele lăsate fără cont de pe telefonul acesta (cu pozele și vocile lor).
     * Urmele altor conturi lăsate de pe același telefon rămân.
     * Sesiunile se șterg odată cu contul (ON DELETE CASCADE). Întoarce câte urme s-au șters.
     */
    public int deleteAccount(UUID user, String deviceHash) {
        var p = new MapSqlParameterSource().addValue("u", user).addValue("d", deviceHash, Types.CHAR);
        int traces = jdbc.update("DELETE FROM trace WHERE user_id = :u OR (user_id IS NULL AND owner_hash = :d)", p);
        jdbc.update("DELETE FROM capsule WHERE user_id = :u OR (user_id IS NULL AND owner_hash = :d)", p);
        jdbc.update("DELETE FROM capsule_item WHERE user_id = :u OR (user_id IS NULL AND owner_hash = :d)", p);
        jdbc.update("DELETE FROM notification WHERE user_id IS NULL AND device_hash = :d", p);
        jdbc.update("DELETE FROM app_user WHERE id = :u", new MapSqlParameterSource("u", user));
        return traces;
    }
}
