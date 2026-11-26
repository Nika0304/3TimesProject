package com.threetimes.backend.notification;

import java.sql.Types;
import java.time.OffsetDateTime;
import java.time.ZoneId;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.UUID;

import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Repository;

import com.threetimes.backend.common.Viewer;

/** Notificările din aplicație: cine le primește (cont sau telefon), citite sau nu. */
@Repository
public class NotificationRepository {

    static final int MAX_LIST = 50;
    private static final String MINE = Viewer.owned("n.user_id", "n.device_hash", "device", "user");
    private static final String[] MONTHS = {
            "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie",
            "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie"};

    public record Notification(String id, String kind, String title, String body, String createdAt, boolean read) {
    }

    private final NamedParameterJdbcTemplate jdbc;

    public NotificationRepository(NamedParameterJdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    /**
     * „Urma ta a fost ștearsă de moderatori”, pentru cine a lăsat-o: contul, sau telefonul dacă a lăsat-o fără cont.
     * excerpt = câteva cuvinte din urmă (sau „o poză”), ca omul să știe care; reason = motivul ales de admin.
     */
    public void traceRemoved(UUID user, String device, boolean inCapsule, OffsetDateTime createdAt, String excerpt,
                             String reason) {
        if (user == null && device == null) return;
        String day = dateText(createdAt);
        String what = inCapsule ? "Urma pe care ai pus-o într-o capsulă pe " + day : "Urma ta din " + day;
        String body = what + (excerpt == null || excerpt.isBlank() ? "" : " (" + excerpt + ")")
                + " a fost ștearsă de moderatori."
                + (reason == null || reason.isBlank() ? "" : "\nMotiv: " + reason.trim() + ".");
        jdbc.update("""
                INSERT INTO notification (id, user_id, device_hash, kind, title, body)
                VALUES (:id, :user, :device, 'TRACE_REMOVED', :title, :body)
                """, new MapSqlParameterSource()
                .addValue("id", UUID.randomUUID())
                .addValue("user", user, Types.OTHER)
                .addValue("device", device, Types.CHAR)
                .addValue("title", "Urmă ștearsă de moderatori")
                .addValue("body", body));
    }

    public List<Notification> of(Viewer me) {
        return jdbc.query("""
                SELECT n.id, n.kind, n.title, n.body, n.created_at, n.read_at IS NOT NULL AS read
                FROM notification n
                WHERE %s
                ORDER BY n.created_at DESC
                LIMIT %d
                """.formatted(MINE, MAX_LIST), who(me), (rs, n) -> new Notification(
                rs.getObject("id", UUID.class).toString(),
                rs.getString("kind"),
                rs.getString("title"),
                rs.getString("body"),
                rs.getObject("created_at", OffsetDateTime.class).withOffsetSameInstant(ZoneOffset.UTC)
                        .format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                rs.getBoolean("read")));
    }

    /** Marchează citită o notificare a mea (sau toate, dacă id e null). */
    public void markRead(Viewer me, UUID id) {
        jdbc.update("""
                UPDATE notification n SET read_at = now()
                WHERE n.read_at IS NULL AND %s %s
                """.formatted(MINE, id == null ? "" : "AND n.id = :id"), who(me).addValue("id", id));
    }

    private static MapSqlParameterSource who(Viewer me) {
        return new MapSqlParameterSource()
                .addValue("device", me.device(), Types.CHAR)
                .addValue("user", me.user(), Types.OTHER);
    }

    /** „25 noiembrie 2026”, ora României. */
    static String dateText(OffsetDateTime t) {
        var local = t.atZoneSameInstant(ZoneId.of("Europe/Bucharest"));
        return local.getDayOfMonth() + " " + MONTHS[local.getMonthValue() - 1] + " " + local.getYear();
    }
}
