package com.threetimes.backend.year;

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

import com.threetimes.backend.common.TestClock;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.trace.TraceDtos.TraceView;

/**
 * Capsula anului: urmele lăsate în orașul ei, în anul ei, cu bifa „capsula anului” (și neascunse după raportări).
 * Anul începe și se termină la miezul nopții, ora României.
 */
@Repository
public class YearCapsuleRepository {

    /** Urma t intră în capsula y. */
    private static final String IN_YEAR = """
            t.year_capsule AND NOT t.hidden
            AND t.created_at >= make_timestamptz(y.year, 1, 1, 0, 0, 0, 'Europe/Bucharest')
            AND t.created_at < make_timestamptz(y.year + 1, 1, 1, 0, 0, 0, 'Europe/Bucharest')
            AND ST_Y(t.location::geometry) BETWEEN y.min_lat AND y.max_lat
            AND ST_X(t.location::geometry) BETWEEN y.min_lng AND y.max_lng
            """;

    private static final String MINE = Viewer.owned("t.user_id", "t.owner_hash", "device", "user");

    private static final String COLUMNS = """
            y.id, y.city, y.year, ST_Y(y.location::geometry) AS lat, ST_X(y.location::geometry) AS lng,
            y.altitude, y.height_above_terrain,
            make_timestamptz(y.year, 1, 1, 0, 0, 0, 'Europe/Bucharest') AS starts_at,
            make_timestamptz(y.year + 1, 1, 1, 0, 0, 0, 'Europe/Bucharest') AS unlock_at,
            CAST(:now AS timestamptz) >= make_timestamptz(y.year + 1, 1, 1, 0, 0, 0, 'Europe/Bucharest') AS ready,
            (SELECT count(*) FROM trace t WHERE %1$s) AS item_count,
            (SELECT count(*) FROM trace t WHERE %1$s AND %2$s) AS my_items
            """.formatted(IN_YEAR, MINE);

    /** Capsula anului, din bază (fără urmele ei). */
    public record Row(String id, String city, int year, double latitude, double longitude, double altitude,
                      float heightAboveTerrain, String startsAt, String unlockAt, boolean ready, int itemCount,
                      int myItems) {
    }

    public record EmotionCount(String emotion, String color, int count) {
    }

    private final NamedParameterJdbcTemplate jdbc;

    public YearCapsuleRepository(NamedParameterJdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    private static MapSqlParameterSource who(Viewer me) {
        return new MapSqlParameterSource()
                .addValue("device", me == null ? null : me.device(), Types.CHAR)
                .addValue("user", me == null ? null : me.user(), Types.OTHER)
                .addValue("now", TestClock.nowForSql());
    }

    /** Capsulele anului din jur, din anii începuți (anul de acum se umple, cei trecuți se pot deschide). */
    public List<Row> nearby(double lat, double lng, double radius, Viewer me) {
        return jdbc.query("""
                SELECT %s FROM year_capsule y
                WHERE ST_DWithin(y.location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography, :radius)
                  AND make_timestamptz(y.year, 1, 1, 0, 0, 0, 'Europe/Bucharest') <= CAST(:now AS timestamptz)
                  AND CAST(:now AS timestamptz) < make_timestamptz(y.year + 1, 1, 1, 0, 0, 0, 'Europe/Bucharest')
                                                 + INTERVAL '14 days'
                ORDER BY y.year DESC
                """.formatted(COLUMNS),
                who(me).addValue("lat", lat).addValue("lng", lng).addValue("radius", radius),
                (rs, n) -> row(rs));
    }

    public Optional<Row> find(UUID id, Viewer me) {
        return jdbc.query("SELECT " + COLUMNS + " FROM year_capsule y WHERE y.id = :id",
                who(me).addValue("id", id), (rs, n) -> row(rs)).stream().findFirst();
    }

    public double distance(UUID id, double lat, double lng) {
        Double d = jdbc.queryForObject("""
                SELECT ST_Distance(location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography)
                FROM year_capsule WHERE id = :id
                """, new MapSqlParameterSource().addValue("id", id).addValue("lat", lat).addValue("lng", lng),
                Double.class);
        return d == null ? Double.MAX_VALUE : d;
    }

    /** Emoțiile anului, de la cea mai des întâlnită. */
    public List<EmotionCount> emotions(UUID id) {
        return jdbc.query("""
                SELECT t.emotion, max(t.color) AS color, count(*) AS n
                FROM year_capsule y JOIN trace t ON %s
                WHERE y.id = :id
                GROUP BY t.emotion
                ORDER BY n DESC
                LIMIT 8
                """.formatted(IN_YEAR), new MapSqlParameterSource("id", id),
                (rs, n) -> new EmotionCount(rs.getString("emotion"), rs.getString("color"), rs.getInt("n")));
    }

    /** Câteva urme ale anului, alese la întâmplare, care ies din sferă la deschidere. */
    public List<TraceView> highlights(UUID id, int limit, Viewer me) {
        return jdbc.query("""
                SELECT t.id, t.type, t.content, t.emotion, t.shape, t.color, t.accent_color, t.petals, t.sharpness,
                       t.motion, t.intensity, ST_Y(t.location::geometry) AS lat, ST_X(t.location::geometry) AS lng,
                       t.altitude, t.height_above_terrain, t.accuracy_meters, t.yaw_accuracy_degrees, t.photo_source,
                       t.duration_seconds, t.created_at,
                       EXISTS (SELECT 1 FROM trace_media m WHERE m.trace_id = t.id) AS has_media,
                       %s AS mine,
                       EXISTS (SELECT 1 FROM trace_report r WHERE r.trace_id = t.id AND r.reporter_hash = :device) AS reported
                FROM year_capsule y JOIN trace t ON %s
                WHERE y.id = :id
                ORDER BY random()
                LIMIT :limit
                """.formatted(MINE, IN_YEAR), who(me).addValue("id", id).addValue("limit", limit),
                (rs, n) -> new TraceView(
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
                        iso(rs.getObject("created_at", OffsetDateTime.class)),
                        rs.getBoolean("has_media"),
                        rs.getBoolean("mine"),
                        rs.getBoolean("reported"),
                        0.0));
    }

    private static Row row(ResultSet rs) throws SQLException {
        return new Row(
                rs.getObject("id", UUID.class).toString(),
                rs.getString("city"),
                rs.getInt("year"),
                rs.getDouble("lat"),
                rs.getDouble("lng"),
                rs.getDouble("altitude"),
                rs.getFloat("height_above_terrain"),
                iso(rs.getObject("starts_at", OffsetDateTime.class)),
                iso(rs.getObject("unlock_at", OffsetDateTime.class)),
                rs.getBoolean("ready"),
                rs.getInt("item_count"),
                rs.getInt("my_items"));
    }

    private static String iso(OffsetDateTime t) {
        return t.withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME);
    }
}
