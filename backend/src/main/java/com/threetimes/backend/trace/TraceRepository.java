package com.threetimes.backend.trace;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Types;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Repository;

import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos.Media;
import com.threetimes.backend.trace.TraceDtos.Stored;
import com.threetimes.backend.trace.TraceDtos.TraceView;

/** Interogările SQL (PostGIS) pentru urme. Distanțele sunt în metri, pe glob (geography). */
@Repository
public class TraceRepository {

    private static final String COLUMNS = """
            t.id, t.type, t.content, t.emotion, t.shape, t.color, t.accent_color, t.petals, t.sharpness, t.motion,
            t.intensity,
            ST_Y(t.location::geometry) AS lat, ST_X(t.location::geometry) AS lng,
            t.altitude, t.height_above_terrain, t.accuracy_meters, t.yaw_accuracy_degrees,
            t.photo_source, t.duration_seconds, t.created_at, t.owner_hash, t.user_id, t.hidden,
            EXISTS (SELECT 1 FROM trace_media m WHERE m.trace_id = t.id) AS has_media,
            EXISTS (SELECT 1 FROM trace_report r WHERE r.trace_id = t.id AND r.reporter_hash = :me) AS reported
            """;

    /**
     * O urmă se vede în Prezent două săptămâni de când e lăsată; apoi se retrage și revine în locul ei la fiecare
     * aniversare, o săptămână („acum un an, aici”), începând din ziua aniversării (ora României).
     * Aceeași regulă e și în aplicație (Core/TraceTime), pentru urmele proprii păstrate pe telefon.
     */
    static final String IN_PRESENT = """
            (t.created_at > CAST(:now AS timestamptz) - INTERVAL '14 days'
             OR EXISTS (SELECT 1 FROM generate_series(1, 30) AS k
                        WHERE CAST(:now AS timestamptz) >= (date_trunc('day', t.created_at AT TIME ZONE 'Europe/Bucharest')
                                        + make_interval(years => k)) AT TIME ZONE 'Europe/Bucharest'
                          AND CAST(:now AS timestamptz) < (date_trunc('day', t.created_at AT TIME ZONE 'Europe/Bucharest')
                                       + make_interval(years => k) + INTERVAL '7 days') AT TIME ZONE 'Europe/Bucharest'))
            """;

    private final NamedParameterJdbcTemplate jdbc;

    public TraceRepository(NamedParameterJdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    /** Adaugă urma (și contul, dacă e lăsată cu contul deschis). Întoarce false dacă există deja una cu același id. */
    public boolean insert(UUID id, TraceDtos.CreateRequest r, String content, String ownerHash, UUID user, Verdict verdict) {
        boolean review = Verdict.REVIEW.equals(verdict.verdict());
        var p = new MapSqlParameterSource()
                .addValue("id", id)
                .addValue("type", r.type())
                .addValue("content", content)
                .addValue("emotion", r.emotion())
                .addValue("shape", r.shape(), Types.VARCHAR)
                .addValue("color", r.color(), Types.VARCHAR)
                .addValue("accent", r.accentColor(), Types.VARCHAR)
                .addValue("petals", r.petals(), Types.SMALLINT)
                .addValue("sharpness", r.sharpness(), Types.REAL)
                .addValue("motion", r.motion(), Types.VARCHAR)
                .addValue("intensity", r.intensity())
                .addValue("lat", r.latitude())
                .addValue("lng", r.longitude())
                .addValue("altitude", r.altitude())
                .addValue("height", r.heightAboveTerrain())
                .addValue("accuracy", r.accuracyMeters())
                .addValue("yaw", r.yawAccuracyDegrees())
                .addValue("photoSource", r.photoSource(), Types.VARCHAR)
                .addValue("duration", r.durationSeconds())
                .addValue("owner", ownerHash)
                .addValue("user", user, Types.OTHER)
                .addValue("moderation", review ? Verdict.REVIEW : Verdict.OK)
                .addValue("moderationReason", review ? verdict.reason() : null, Types.VARCHAR)
                .addValue("yearCapsule", Boolean.TRUE.equals(r.yearCapsule()));
        int rows = jdbc.update("""
                INSERT INTO trace (id, type, content, emotion, shape, color, accent_color, petals, sharpness, motion,
                                   intensity, location, altitude, height_above_terrain,
                                   accuracy_meters, yaw_accuracy_degrees, photo_source, duration_seconds, owner_hash, user_id,
                                   moderation, moderation_reason, year_capsule)
                VALUES (:id, :type, :content, :emotion, :shape, :color, :accent, :petals, :sharpness, :motion, :intensity,
                        ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography,
                        :altitude, :height, :accuracy, :yaw, :photoSource, :duration, :owner, :user,
                        :moderation, :moderationReason, :yearCapsule)
                ON CONFLICT (id) DO NOTHING
                """, p);
        return rows == 1;
    }

    public void insertMedia(UUID traceId, String contentType, byte[] data) {
        jdbc.update("""
                INSERT INTO trace_media (trace_id, content_type, size_bytes, data)
                VALUES (:id, :type, :size, :data)
                """, new MapSqlParameterSource()
                .addValue("id", traceId)
                .addValue("type", contentType)
                .addValue("size", data.length)
                .addValue("data", data));
    }

    public Optional<Stored> find(UUID id, Viewer me) {
        var p = viewer(new MapSqlParameterSource().addValue("id", id), me);
        List<Stored> rows = jdbc.query(
                "SELECT " + COLUMNS + ", 0.0 AS distance FROM trace t WHERE t.id = :id",
                p, (rs, n) -> map(rs, me));
        return rows.stream().findFirst();
    }

    /**
     * Urmele din jurul unui punct, de la cea mai apropiată. Cele ascunse (raportate) nu apar,
     * în afară de cele ale telefonului (sau contului) care întreabă.
     */
    public List<TraceView> nearby(double lat, double lng, double radiusMeters, int limit, Viewer me) {
        var p = viewer(new MapSqlParameterSource()
                .addValue("lat", lat)
                .addValue("lng", lng)
                .addValue("radius", radiusMeters)
                .addValue("limit", limit)
                .addValue("now", com.threetimes.backend.common.TestClock.nowForSql()), me);
        return jdbc.query("""
                SELECT %s,
                       ST_Distance(t.location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography) AS distance
                FROM trace t
                WHERE ST_DWithin(t.location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography, :radius)
                  AND (NOT t.hidden OR t.owner_hash = :me OR t.user_id = :uid)
                  AND %s
                ORDER BY distance
                LIMIT :limit
                """.formatted(COLUMNS, IN_PRESENT), p, (rs, n) -> map(rs, me).view());
    }

    /** Urma e în capsula unui an deja încheiat (de la 1 ianuarie anul următor, ora României)? */
    public boolean lockedInYearCapsule(UUID id) {
        Boolean locked = jdbc.queryForObject("""
                SELECT year_capsule AND CAST(:now AS timestamptz) >= make_timestamptz(
                        extract(year FROM created_at AT TIME ZONE 'Europe/Bucharest')::int + 1, 1, 1, 0, 0, 0,
                        'Europe/Bucharest')
                FROM trace WHERE id = :id
                """, new MapSqlParameterSource().addValue("id", id)
                .addValue("now", com.threetimes.backend.common.TestClock.nowForSql()), Boolean.class);
        return Boolean.TRUE.equals(locked);
    }

    public Optional<Media> media(UUID traceId) {
        List<Media> rows = jdbc.query(
                "SELECT content_type, data FROM trace_media WHERE trace_id = :id",
                new MapSqlParameterSource("id", traceId),
                (rs, n) -> new Media(rs.getString("content_type"), rs.getBytes("data")));
        return rows.stream().findFirst();
    }

    /** Id-urile tuturor urmelor mele: ale telefonului și ale contului (și ale celor ascunse). */
    public List<String> idsOf(Viewer me) {
        return jdbc.query("SELECT id FROM trace WHERE owner_hash = :me OR user_id = :uid",
                viewer(new MapSqlParameterSource(), me),
                (rs, n) -> rs.getObject("id", UUID.class).toString());
    }

    /** Toate urmele unui cont, de la cea mai nouă (pentru „Harta mea”). */
    public List<TraceView> ofUser(Viewer me, int limit) {
        return jdbc.query("""
                SELECT %s, 0.0 AS distance
                FROM trace t
                WHERE t.user_id = :uid
                ORDER BY t.created_at DESC
                LIMIT :limit
                """.formatted(COLUMNS), viewer(new MapSqlParameterSource("limit", limit), me), (rs, n) -> map(rs, me).view());
    }

    public void delete(UUID id) {
        jdbc.update("DELETE FROM trace WHERE id = :id", new MapSqlParameterSource("id", id));
    }

    /**
     * Notează raportarea (o dată pe telefon) și ascunde urma când se adună destule.
     * Întoarce false dacă telefonul o raportase deja.
     */
    public boolean report(UUID id, String reporterHash, String reason, int reportsToHide) {
        int added = jdbc.update("""
                INSERT INTO trace_report (trace_id, reporter_hash, reason)
                VALUES (:id, :me, :reason)
                ON CONFLICT DO NOTHING
                """, new MapSqlParameterSource()
                .addValue("id", id)
                .addValue("me", reporterHash)
                .addValue("reason", reason, Types.VARCHAR));
        if (added == 0) return false;
        jdbc.update("""
                UPDATE trace
                SET report_count = report_count + 1,
                    hidden = hidden OR report_count + 1 >= :limit
                WHERE id = :id
                """, new MapSqlParameterSource().addValue("id", id).addValue("limit", reportsToHide));
        return true;
    }

    /** Parametrii :me (amprenta telefonului) și :uid (contul); oricare poate lipsi. */
    private static MapSqlParameterSource viewer(MapSqlParameterSource p, Viewer me) {
        return p.addValue("me", me == null ? null : me.device(), Types.CHAR)
                .addValue("uid", me == null ? null : me.user(), Types.OTHER);
    }

    private static Stored map(ResultSet rs, Viewer me) throws SQLException {
        String owner = rs.getString("owner_hash");
        UUID user = rs.getObject("user_id", UUID.class);
        OffsetDateTime created = rs.getObject("created_at", OffsetDateTime.class);
        var view = new TraceView(
                rs.getObject("id", UUID.class).toString(),
                rs.getString("type"),
                rs.getString("content"),
                rs.getString("emotion"),
                rs.getString("shape"),
                rs.getString("color"),
                rs.getString("accent_color"),
                rs.getInt("petals"),
                rs.getFloat("sharpness"),
                rs.getString("motion"),
                rs.getFloat("intensity"),
                rs.getDouble("lat"),
                rs.getDouble("lng"),
                rs.getDouble("altitude"),
                rs.getFloat("height_above_terrain"),
                rs.getFloat("accuracy_meters"),
                rs.getFloat("yaw_accuracy_degrees"),
                rs.getString("photo_source"),
                rs.getFloat("duration_seconds"),
                created.withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME),
                rs.getBoolean("has_media"),
                me != null && me.owns(owner, user),
                rs.getBoolean("reported"),
                rs.getDouble("distance"));
        return new Stored(view, owner, user, rs.getBoolean("hidden"));
    }
}
