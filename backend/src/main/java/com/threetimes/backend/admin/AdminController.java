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
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.trace.TraceDtos.Media;
import com.threetimes.backend.trace.TraceRepository;

/**
 * Moderarea (protejată de AdminAuth):
 * <pre>
 * GET    /admin                                → pagina de admin (static/admin/index.html)
 * GET    /api/v1/admin/queue                   urmele de verificat: raportate, ascunse sau marcate de AI
 * GET    /api/v1/admin/traces/{id}/media       poza sau vocea (și pentru urmele ascunse)
 * POST   /api/v1/admin/traces/{id}/keep        „Readuce” / „E în regulă”: raportările se închid, urma se vede
 * DELETE /api/v1/admin/traces/{id}            șterge urma (cu poza / vocea ei)
 * </pre>
 */
@RestController
public class AdminController {

    private static final Logger log = LoggerFactory.getLogger(AdminController.class);

    public record QueueItem(String id, String type, String content, String emotion, String color, float intensity,
                            double latitude, double longitude, String photoSource, float durationSeconds,
                            String createdAt, boolean hasMedia, boolean hidden, String moderation,
                            String moderationReason, int reportCount, List<String> reportReasons) {
    }

    public record Queue(List<QueueItem> items) {
    }

    private final NamedParameterJdbcTemplate jdbc;
    private final TraceRepository traces;

    public AdminController(NamedParameterJdbcTemplate jdbc, TraceRepository traces) {
        this.jdbc = jdbc;
        this.traces = traces;
    }

    @GetMapping({"/admin", "/admin/"})
    public ResponseEntity<Void> page() {
        return ResponseEntity.status(HttpStatus.FOUND).location(URI.create("/admin/index.html")).build();
    }

    @GetMapping("/api/v1/admin/queue")
    public Queue queue() {
        return new Queue(jdbc.query("""
                SELECT t.id, t.type, t.content, t.emotion, t.color, t.intensity,
                       ST_Y(t.location::geometry) AS lat, ST_X(t.location::geometry) AS lng,
                       t.photo_source, t.duration_seconds, t.created_at, t.hidden, t.moderation, t.moderation_reason,
                       t.report_count,
                       EXISTS (SELECT 1 FROM trace_media m WHERE m.trace_id = t.id) AS has_media,
                       (SELECT string_agg(COALESCE(NULLIF(r.reason, ''), '(fără motiv)'), E'\\n' ORDER BY r.created_at)
                        FROM trace_report r WHERE r.trace_id = t.id AND NOT r.dismissed) AS reasons
                FROM trace t
                WHERE t.moderation = 'review' OR t.report_count > 0 OR t.hidden
                ORDER BY t.hidden DESC, t.report_count DESC, t.created_at DESC
                LIMIT 200
                """, new MapSqlParameterSource(), (rs, n) -> item(rs)));
    }

    @GetMapping("/api/v1/admin/traces/{id}/media")
    public ResponseEntity<byte[]> media(@PathVariable UUID id) {
        Media media = traces.media(id).orElseThrow(ApiException::notFound);
        return ResponseEntity.ok()
                .contentType(MediaType.parseMediaType(media.contentType()))
                .cacheControl(CacheControl.noStore())
                .body(media.data());
    }

    @PostMapping("/api/v1/admin/traces/{id}/keep")
    @Transactional
    public ResponseEntity<Void> keep(@PathVariable UUID id) {
        var p = new MapSqlParameterSource("id", id);
        int rows = jdbc.update("""
                UPDATE trace
                SET hidden = FALSE, report_count = 0, moderation = 'approved', moderation_reason = NULL
                WHERE id = :id
                """, p);
        if (rows == 0) throw ApiException.notFound();
        jdbc.update("UPDATE trace_report SET dismissed = TRUE WHERE trace_id = :id", p);
        log.info("Admin: urma {} păstrată.", id);
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/api/v1/admin/traces/{id}")
    public ResponseEntity<Void> delete(@PathVariable UUID id) {
        int rows = jdbc.update("DELETE FROM trace WHERE id = :id", new MapSqlParameterSource("id", id));
        if (rows == 0) throw ApiException.notFound();
        log.info("Admin: urma {} ștearsă.", id);
        return ResponseEntity.noContent().build();
    }

    private static QueueItem item(ResultSet rs) throws SQLException {
        String reasons = rs.getString("reasons");
        OffsetDateTime created = rs.getObject("created_at", OffsetDateTime.class);
        return new QueueItem(
                rs.getObject("id", UUID.class).toString(),
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
                reasons == null ? List.of() : Arrays.asList(reasons.split("\n")));
    }
}
