package com.threetimes.backend.admin;

import java.net.URI;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.Arrays;
import java.util.List;
import java.util.UUID;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.CacheControl;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.notification.NotificationRepository;
import com.threetimes.backend.trace.TraceDtos.Media;

/**
 * Moderarea (protejată de AdminAuth):
 * <pre>
 * GET    /admin                                → pagina de admin (static/admin/index.html)
 * GET    /api/v1/admin/queue                   urmele și capsulele de verificat: raportate, ascunse sau marcate de AI
 * GET    /api/v1/admin/{traces|capsules}/{id}/media   poza sau vocea (și pentru cele ascunse / sigilate)
 * POST   /api/v1/admin/{traces|capsules}/{id}/keep    „Readuce” / „E în regulă”: raportările se închid
 * DELETE /api/v1/admin/{traces|capsules}/{id}?reason=…  șterge (cu poza / vocea); cine a lăsat-o primește o
 *                                                       notificare în aplicație, cu motivul
 * </pre>
 */
@RestController
public class AdminController {

    private static final Logger log = LoggerFactory.getLogger(AdminController.class);

    public record QueueItem(String id, String kind, String type, String content, String emotion, String color,
                            float intensity, double latitude, double longitude, String photoSource, float durationSeconds,
                            String createdAt, boolean hasMedia, boolean hidden, String moderation,
                            String moderationReason, int reportCount, List<String> reportReasons,
                            String visibility, String unlockAt) {
    }

    public record Queue(List<QueueItem> items) {
    }

    /** Tabelele pentru fiecare fel de conținut: urme și capsule au aceeași formă. */
    private record Kind(String name, String table, String media, String report, String key) {
    }

    private static final Kind TRACES = new Kind("TRACE", "trace", "trace_media", "trace_report", "trace_id");
    /** Urmele din capsule (se moderează fiecare urmă, nu capsula întreagă). */
    private static final Kind CAPSULES = new Kind("CAPSULE", "capsule_item", "capsule_item_media", "capsule_item_report", "item_id");

    private static Kind kind(String path) {
        return switch (path) {
            case "traces" -> TRACES;
            case "capsules" -> CAPSULES;
            default -> throw ApiException.notFound();
        };
    }

    private final NamedParameterJdbcTemplate jdbc;
    private final NotificationRepository notifications;

    public AdminController(NamedParameterJdbcTemplate jdbc, NotificationRepository notifications) {
        this.jdbc = jdbc;
        this.notifications = notifications;
    }

    private record Owner(UUID user, String device, String type, String content, OffsetDateTime createdAt) {
    }

    @GetMapping({"/admin", "/admin/"})
    public ResponseEntity<Void> page() {
        return ResponseEntity.status(HttpStatus.FOUND).location(URI.create("/admin/index.html")).build();
    }

    @GetMapping("/api/v1/admin/queue")
    public Queue queue() {
        var items = new java.util.ArrayList<QueueItem>();
        items.addAll(queueOf(TRACES));
        items.addAll(queueOf(CAPSULES));
        items.sort(java.util.Comparator.comparing(QueueItem::hidden).reversed()
                .thenComparing(java.util.Comparator.comparingInt(QueueItem::reportCount).reversed())
                .thenComparing(java.util.Comparator.comparing(QueueItem::createdAt).reversed()));
        return new Queue(items.size() > 200 ? items.subList(0, 200) : items);
    }

    private List<QueueItem> queueOf(Kind k) {
        boolean capsule = k == CAPSULES;
        String extra = capsule
                ? "(SELECT p.visibility FROM capsule p WHERE p.id = t.capsule_id) AS visibility, "
                  + "(SELECT p.unlock_at FROM capsule p WHERE p.id = t.capsule_id) AS unlock_at,"
                : "NULL AS visibility, NULL::timestamptz AS unlock_at,";
        return jdbc.query("""
                SELECT t.id, t.type, t.content, t.emotion, t.color, t.intensity,
                       ST_Y(t.location::geometry) AS lat, ST_X(t.location::geometry) AS lng,
                       t.photo_source, t.duration_seconds, t.created_at, t.hidden, t.moderation, t.moderation_reason,
                       t.report_count, %s
                       EXISTS (SELECT 1 FROM %s m WHERE m.%s = t.id) AS has_media,
                       (SELECT string_agg(COALESCE(NULLIF(r.reason, ''), '(fără motiv)'), E'\\n' ORDER BY r.created_at)
                        FROM %s r WHERE r.%s = t.id AND NOT r.dismissed) AS reasons
                FROM %s t
                WHERE t.moderation = 'review' OR t.report_count > 0 OR t.hidden
                ORDER BY t.created_at DESC
                LIMIT 200
                """.formatted(extra, k.media(), k.key(), k.report(), k.key(), k.table()),
                new MapSqlParameterSource(), (rs, n) -> item(rs, k.name()));
    }

    @GetMapping("/api/v1/admin/{kind}/{id}/media")
    public ResponseEntity<byte[]> media(@PathVariable String kind, @PathVariable UUID id) {
        Kind k = kind(kind);
        List<Media> rows = jdbc.query(
                "SELECT content_type, data FROM %s WHERE %s = :id".formatted(k.media(), k.key()),
                new MapSqlParameterSource("id", id),
                (rs, n) -> new Media(rs.getString("content_type"), rs.getBytes("data")));
        Media media = rows.stream().findFirst().orElseThrow(ApiException::notFound);
        return ResponseEntity.ok()
                .contentType(MediaType.parseMediaType(media.contentType()))
                .cacheControl(CacheControl.noStore())
                .body(media.data());
    }

    @PostMapping("/api/v1/admin/{kind}/{id}/keep")
    @Transactional
    public ResponseEntity<Void> keep(@PathVariable String kind, @PathVariable UUID id) {
        Kind k = kind(kind);
        var p = new MapSqlParameterSource("id", id);
        int rows = jdbc.update("""
                UPDATE %s
                SET hidden = FALSE, report_count = 0, moderation = 'approved', moderation_reason = NULL
                WHERE id = :id
                """.formatted(k.table()), p);
        if (rows == 0) throw ApiException.notFound();
        jdbc.update("UPDATE %s SET dismissed = TRUE WHERE %s = :id".formatted(k.report(), k.key()), p);
        log.info("Admin: {} {} păstrată.", k.table(), id);
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/api/v1/admin/{kind}/{id}")
    @Transactional
    public ResponseEntity<Void> delete(@PathVariable String kind, @PathVariable UUID id,
                                       @RequestParam(required = false) String reason) {
        Kind k = kind(kind);
        var p = new MapSqlParameterSource("id", id);
        Owner owner = jdbc.query(
                "SELECT user_id, owner_hash, type, content, created_at FROM %s WHERE id = :id".formatted(k.table()), p,
                (rs, n) -> new Owner(rs.getObject("user_id", UUID.class), rs.getString("owner_hash"),
                        rs.getString("type"), rs.getString("content"), rs.getObject("created_at", OffsetDateTime.class)))
                .stream().findFirst().orElseThrow(ApiException::notFound);
        jdbc.update("DELETE FROM %s WHERE id = :id".formatted(k.table()), p);
        String why = reason == null ? null : reason.trim();
        if (why != null && why.length() > 200) why = why.substring(0, 200);
        notifications.traceRemoved(owner.user(), owner.device(),
                k == CAPSULES, owner.createdAt(), excerpt(owner), why);
        log.info("Admin: {} {} ștearsă (motiv: {}).", k.table(), id, why == null ? "-" : why);
        return ResponseEntity.noContent().build();
    }

    /** Câteva cuvinte din urmă, ca omul să știe care a fost ștearsă. */
    private static String excerpt(Owner o) {
        String text = o.content() == null ? "" : o.content().trim().replaceAll("\\s+", " ");
        if (!text.isEmpty()) return "„" + (text.length() > 60 ? text.substring(0, 57).trim() + "…" : text) + "”";
        if ("PHOTO".equals(o.type())) return "o poză";
        if ("VOICE".equals(o.type())) return "un mesaj vocal";
        return null;
    }

    private static QueueItem item(ResultSet rs, String kind) throws SQLException {
        String reasons = rs.getString("reasons");
        OffsetDateTime created = rs.getObject("created_at", OffsetDateTime.class);
        OffsetDateTime unlock = rs.getObject("unlock_at", OffsetDateTime.class);
        return new QueueItem(
                rs.getObject("id", UUID.class).toString(),
                kind,
                rs.getString("type"),
                rs.getString("content"),
                rs.getString("emotion"),
                rs.getString("color"),
                rs.getFloat("intensity"),
                rs.getDouble("lat"),
                rs.getDouble("lng"),
                rs.getString("photo_source"),
                rs.getFloat("duration_seconds"),
                created.withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                rs.getBoolean("has_media"),
                rs.getBoolean("hidden"),
                rs.getString("moderation"),
                rs.getString("moderation_reason"),
                rs.getInt("report_count"),
                reasons == null ? List.of() : Arrays.asList(reasons.split("\n")),
                rs.getString("visibility"),
                unlock == null ? null : unlock.withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME));
    }
}
