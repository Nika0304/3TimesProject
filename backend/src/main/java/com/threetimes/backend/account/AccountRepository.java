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
                rs.getObject("created_at", OffsetDateTime.class)));
        return rows.stream().findFirst();
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
        return jdbc.update("UPDATE trace SET user_id = :u WHERE owner_hash = :d AND user_id IS NULL", p);
    }

    public int traceCount(UUID user) {
        Integer n = jdbc.queryForObject("SELECT count(*) FROM trace WHERE user_id = :u",
                new MapSqlParameterSource("u", user), Integer.class);
        return n == null ? 0 : n;
    }

    /**
     * Șterge contul, urmele lui și urmele lăsate de pe telefonul acesta (cu pozele și vocile lor).
     * Sesiunile se șterg odată cu contul (ON DELETE CASCADE). Întoarce câte urme s-au șters.
     */
    public int deleteAccount(UUID user, String deviceHash) {
        var p = new MapSqlParameterSource().addValue("u", user).addValue("d", deviceHash, Types.CHAR);
        int traces = jdbc.update("DELETE FROM trace WHERE user_id = :u OR owner_hash = :d", p);
        jdbc.update("DELETE FROM capsule WHERE user_id = :u OR owner_hash = :d", p);
        jdbc.update("DELETE FROM capsule_item WHERE user_id = :u OR owner_hash = :d", p);
        jdbc.update("DELETE FROM app_user WHERE id = :u", new MapSqlParameterSource("u", user));
        return traces;
    }
}
