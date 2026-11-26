package com.threetimes.backend.capsule;

import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Types;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.Optional;
import java.util.UUID;

import org.springframework.jdbc.core.namedparam.MapSqlParameterSource;
import org.springframework.jdbc.core.namedparam.NamedParameterJdbcTemplate;
import org.springframework.stereotype.Repository;

import com.threetimes.backend.capsule.CapsuleDtos.CapsuleView;
import com.threetimes.backend.capsule.CapsuleDtos.Media;
import com.threetimes.backend.capsule.CapsuleDtos.PlaceRequest;
import com.threetimes.backend.capsule.CapsuleDtos.Stored;
import com.threetimes.backend.capsule.CapsuleDtos.StoredItem;
import com.threetimes.backend.common.TestClock;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos;
import com.threetimes.backend.trace.TraceDtos.TraceView;

/** Interogările SQL pentru capsule și urmele din ele. „A mea” = același telefon sau același cont (ca la urme). */
@Repository
public class CapsuleRepository {

    /** „A mea”, pentru tabelul cu alias-ul dat (c = capsula, i = urma din capsulă), cu parametrii :device și :user. */
    private static String mine(String alias) {
        return Viewer.owned(alias + ".user_id", alias + ".owner_hash", "device", "user");
    }

    /** Am deschis-o eu: din contul meu, sau fără cont de pe telefonul acesta. */
    private static final String OPENED_BY_ME = Viewer.owned("o.user_id", "o.device_hash", "device", "user");

    private static final String COLUMNS = """
            c.id, ST_Y(c.location::geometry) AS lat, ST_X(c.location::geometry) AS lng,
            c.altitude, c.height_above_terrain, c.accuracy_meters, c.yaw_accuracy_degrees,
            c.visibility, c.unlock_at, c.created_at, c.owner_hash, c.user_id,
            CAST(:now AS timestamptz) >= c.unlock_at AS ready,
            (SELECT count(*) FROM capsule_item i WHERE i.capsule_id = c.id) AS item_count,
            (SELECT count(*) FROM capsule_item i WHERE i.capsule_id = c.id AND %1$s) AS my_items,
            EXISTS (SELECT 1 FROM capsule_open o WHERE o.capsule_id = c.id AND %2$s) AS opened,
            (SELECT min(o.opened_at) FROM capsule_open o WHERE o.capsule_id = c.id AND %2$s) AS opened_at,
            (c.sealed_at IS NULL AND CAST(:now AS timestamptz) < c.created_at + INTERVAL '7 days'
             AND CAST(:now AS timestamptz) < c.unlock_at
             AND (SELECT count(*) FROM capsule_item i WHERE i.capsule_id = c.id) < %3$d) AS collecting
            """.formatted(mine("i"), OPENED_BY_ME, CapsuleDtos.MAX_ITEMS);

    private static final String ITEM_COLUMNS = """
            i.id, i.type, i.content, i.emotion, i.shape, i.color, i.accent_color, i.petals, i.sharpness, i.motion,
            i.intensity, ST_Y(i.location::geometry) AS lat, ST_X(i.location::geometry) AS lng,
            i.photo_source, i.duration_seconds, i.created_at, i.owner_hash, i.user_id,
            EXISTS (SELECT 1 FROM capsule_item_media m WHERE m.item_id = i.id) AS has_media,
            EXISTS (SELECT 1 FROM capsule_item_report r WHERE r.item_id = i.id AND r.reporter_hash = :device) AS reported
            """;

    /**
     * Acum e în primele `days` zile ale aniversării de 1 an a momentului dat (ora României, din ziua aniversării).
     * Urmele dintr-o capsulă deschisă revin o singură dată, la un an de la deschidere (decis 26 nov).
     */
    static String anniversary(String moment, int days) {
        return """
                EXISTS (SELECT 1 FROM generate_series(1, 1) AS k
                        WHERE CAST(:now AS timestamptz) >= (date_trunc('day', %1$s AT TIME ZONE 'Europe/Bucharest')
                                      + make_interval(years => k)) AT TIME ZONE 'Europe/Bucharest'
                          AND CAST(:now AS timestamptz) < (date_trunc('day', %1$s AT TIME ZONE 'Europe/Bucharest')
                                      + make_interval(years => k) + make_interval(days => %2$d)) AT TIME ZONE 'Europe/Bucharest')
                """.formatted(moment, days);
    }

    private final NamedParameterJdbcTemplate jdbc;

    public CapsuleRepository(NamedParameterJdbcTemplate jdbc) {
        this.jdbc = jdbc;
    }

    private static MapSqlParameterSource who(Viewer me) {
        return new MapSqlParameterSource()
                .addValue("device", me == null ? null : me.device(), Types.CHAR)
                .addValue("user", me == null ? null : me.user(), Types.OTHER)
                .addValue("now", TestClock.nowForSql());
    }

    // ---------------------------------------------------------------- scriere

    public boolean insertCapsule(UUID id, PlaceRequest p, String visibility, Instant unlockAt, Viewer me) {
        int rows = jdbc.update("""
                INSERT INTO capsule (id, location, altitude, height_above_terrain, accuracy_meters, yaw_accuracy_degrees,
                                     visibility, unlock_at, owner_hash, user_id)
                VALUES (:id, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography, :altitude, :height, :accuracy, :yaw,
                        :visibility, :unlockAt, :owner, :user)
                ON CONFLICT (id) DO NOTHING
                """, new MapSqlParameterSource()
                .addValue("id", id)
                .addValue("lat", p.latitude())
                .addValue("lng", p.longitude())
                .addValue("altitude", p.altitude() == null ? 0.0 : p.altitude())
                .addValue("height", p.heightAboveTerrain() == null ? 1.2f : p.heightAboveTerrain())
                .addValue("accuracy", p.accuracyMeters() == null ? 0f : Math.max(0f, p.accuracyMeters()))
                .addValue("yaw", p.yawAccuracyDegrees() == null ? 0f : Math.max(0f, p.yawAccuracyDegrees()))
                .addValue("visibility", visibility)
                .addValue("unlockAt", OffsetDateTime.ofInstant(unlockAt, ZoneOffset.UTC))
                .addValue("owner", me.device())
                .addValue("user", me.user(), Types.OTHER));
        return rows == 1;
    }

    public boolean insertItem(UUID id, UUID capsuleId, TraceDtos.CreateRequest r, String content, Viewer me, Verdict verdict) {
        boolean review = Verdict.REVIEW.equals(verdict.verdict());
        int rows = jdbc.update("""
                INSERT INTO capsule_item (id, capsule_id, type, content, emotion, shape, color, accent_color, petals,
                                          sharpness, motion, intensity, location, photo_source, duration_seconds,
                                          owner_hash, user_id, moderation, moderation_reason)
                VALUES (:id, :capsule, :type, :content, :emotion, :shape, :color, :accent, :petals, :sharpness, :motion,
                        :intensity, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography, :photoSource, :duration,
                        :owner, :user, :moderation, :moderationReason)
                ON CONFLICT (id) DO NOTHING
                """, new MapSqlParameterSource()
                .addValue("id", id)
                .addValue("capsule", capsuleId)
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
                .addValue("photoSource", r.photoSource(), Types.VARCHAR)
                .addValue("duration", r.durationSeconds())
                .addValue("owner", me.device())
                .addValue("user", me.user(), Types.OTHER)
                .addValue("moderation", review ? Verdict.REVIEW : Verdict.OK)
                .addValue("moderationReason", review ? verdict.reason() : null, Types.VARCHAR));
        return rows == 1;
    }

    public void insertItemMedia(UUID itemId, String contentType, byte[] data) {
        jdbc.update("""
                INSERT INTO capsule_item_media (item_id, content_type, size_bytes, data)
                VALUES (:id, :type, :size, :data)
                """, new MapSqlParameterSource()
                .addValue("id", itemId)
                .addValue("type", contentType)
                .addValue("size", data.length)
                .addValue("data", data));
    }

    public void seal(UUID id) {
        jdbc.update("UPDATE capsule SET sealed_at = COALESCE(sealed_at, now()) WHERE id = :id",
                new MapSqlParameterSource("id", id));
    }

    public void recordOpen(UUID id, Viewer me) {
        // O deschidere pe cont (sau, fără cont, pe telefon); prima rămâne (opened_at = prima deschidere).
        jdbc.update("""
                INSERT INTO capsule_open (capsule_id, device_hash, user_id)
                SELECT :id, :device, CAST(:user AS uuid)
                WHERE NOT EXISTS (SELECT 1 FROM capsule_open o WHERE o.capsule_id = :id AND %s)
                ON CONFLICT DO NOTHING
                """.formatted(OPENED_BY_ME), who(me).addValue("id", id));
    }

    public boolean reportItem(UUID itemId, String reporterHash, String reason, int reportsToHide) {
        int added = jdbc.update("""
                INSERT INTO capsule_item_report (item_id, reporter_hash, reason)
                VALUES (:id, :me, :reason)
                ON CONFLICT DO NOTHING
                """, new MapSqlParameterSource()
                .addValue("id", itemId)
                .addValue("me", reporterHash)
                .addValue("reason", reason, Types.VARCHAR));
        if (added == 0) return false;
        jdbc.update("""
                UPDATE capsule_item
                SET report_count = report_count + 1,
                    hidden = hidden OR report_count + 1 >= :limit
                WHERE id = :id
                """, new MapSqlParameterSource().addValue("id", itemId).addValue("limit", reportsToHide));
        return true;
    }

    public void deleteCapsule(UUID id) {
        jdbc.update("DELETE FROM capsule WHERE id = :id", new MapSqlParameterSource("id", id));
    }

    /** Șterge urmele mele (telefon sau cont) dintr-o capsulă. */
    public void deleteItemsOf(UUID capsuleId, Viewer me) {
        jdbc.update("DELETE FROM capsule_item i WHERE i.capsule_id = :capsule AND " + mine("i"),
                who(me).addValue("capsule", capsuleId));
    }

    public void deleteItem(UUID itemId) {
        jdbc.update("DELETE FROM capsule_item WHERE id = :id", new MapSqlParameterSource("id", itemId));
    }

    // ---------------------------------------------------------------- citire

    public Optional<Stored> find(UUID id, Viewer me) {
        var p = who(me).addValue("id", id);
        List<Stored> rows = jdbc.query(
                "SELECT " + COLUMNS + ", 0.0 AS distance FROM capsule c WHERE c.id = :id",
                p, (rs, n) -> map(rs, me));
        return rows.stream().findFirst().map(s -> new Stored(withItems(s.view(), me), s.ownerHash(), s.userId()));
    }

    /** Capsulele din jur, de la cea mai apropiată: cele publice și cele personale ale mele. */
    public List<CapsuleView> nearby(double lat, double lng, double radiusMeters, int limit, Viewer me) {
        var p = who(me)
                .addValue("lat", lat)
                .addValue("lng", lng)
                .addValue("radius", radiusMeters)
                .addValue("limit", limit);
        // Ce se vede: capsula nedeschisă, până la două săptămâni după dată (apoi dispare pentru cine n-a deschis-o);
        // capsula deschisă de mine (= urmele ei), o săptămână de la deschidere și apoi la fiecare aniversare a ei.
        List<CapsuleView> views = jdbc.query("""
                SELECT * FROM (
                    SELECT %s,
                           ST_Distance(c.location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography) AS distance
                    FROM capsule c
                    WHERE ST_DWithin(c.location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography, :radius)
                      AND (c.visibility = 'PUBLIC' OR %s)
                      AND EXISTS (SELECT 1 FROM capsule_item i WHERE i.capsule_id = c.id)
                ) q
                WHERE (NOT q.opened AND CAST(:now AS timestamptz) < q.unlock_at + make_interval(days => %d))
                   OR (q.opened AND (CAST(:now AS timestamptz) < q.opened_at + make_interval(days => %d)
                                     OR %s))
                ORDER BY q.distance
                LIMIT :limit
                """.formatted(COLUMNS, mine("c"), CapsuleDtos.OPEN_WINDOW_DAYS, CapsuleDtos.ITEMS_VISIBLE_DAYS,
                              anniversary("q.opened_at", CapsuleDtos.ITEMS_VISIBLE_DAYS)),
                p, (rs, n) -> map(rs, me).view());
        return views.stream().map(v -> withItems(v, me)).toList();
    }

    /**
     * „Harta mea”: capsulele create de mine sau în care am pus o urmă, oricare ar fi starea lor (fără urmele din ele).
     */
    public List<CapsuleView> mine(Viewer me) {
        return jdbc.query("""
                SELECT %s, 0.0 AS distance
                FROM capsule c
                WHERE %s OR EXISTS (SELECT 1 FROM capsule_item i WHERE i.capsule_id = c.id AND %s)
                ORDER BY c.created_at DESC
                LIMIT 200
                """.formatted(COLUMNS, mine("c"), mine("i")), who(me), (rs, n) -> map(rs, me).view());
    }

    /** Urmele capsulei, doar dacă am deschis-o (cele ascunse după raportări nu apar, în afară de ale mele). */
    private CapsuleView withItems(CapsuleView v, Viewer me) {
        if (!v.opened()) return v;
        var p = who(me).addValue("capsule", UUID.fromString(v.id()));
        List<TraceView> items = jdbc.query("""
                SELECT %s FROM capsule_item i
                WHERE i.capsule_id = :capsule AND (NOT i.hidden OR %s)
                ORDER BY i.created_at
                """.formatted(ITEM_COLUMNS, mine("i")), p, (rs, n) -> mapItem(rs, me, v));
        return v.withItems(items);
    }

    public Optional<StoredItem> findItem(UUID itemId) {
        List<StoredItem> rows = jdbc.query(
                "SELECT capsule_id, owner_hash, user_id, hidden FROM capsule_item WHERE id = :id",
                new MapSqlParameterSource("id", itemId),
                (rs, n) -> new StoredItem(rs.getObject("capsule_id", UUID.class), rs.getString("owner_hash"),
                        rs.getObject("user_id", UUID.class), rs.getBoolean("hidden")));
        return rows.stream().findFirst();
    }

    public Optional<Media> itemMedia(UUID itemId) {
        List<Media> rows = jdbc.query(
                "SELECT content_type, data FROM capsule_item_media WHERE item_id = :id",
                new MapSqlParameterSource("id", itemId),
                (rs, n) -> new Media(rs.getString("content_type"), rs.getBytes("data")));
        return rows.stream().findFirst();
    }

    /** Distanța pe glob, în metri, de la capsulă la punctul dat. */
    public double distance(UUID id, double lat, double lng) {
        Double d = jdbc.queryForObject("""
                SELECT ST_Distance(location, ST_SetSRID(ST_MakePoint(:lng, :lat), 4326)::geography)
                FROM capsule WHERE id = :id
                """, new MapSqlParameterSource().addValue("id", id).addValue("lat", lat).addValue("lng", lng),
                Double.class);
        return d == null ? Double.MAX_VALUE : d;
    }

    // ---------------------------------------------------------------- mapare

    private static Stored map(ResultSet rs, Viewer me) throws SQLException {
        String owner = rs.getString("owner_hash");
        UUID user = rs.getObject("user_id", UUID.class);
        boolean opened = rs.getBoolean("opened");
        OffsetDateTime openedAt = rs.getObject("opened_at", OffsetDateTime.class);
        var view = new CapsuleView(
                rs.getObject("id", UUID.class).toString(),
                "CAPSULE",
                rs.getDouble("lat"),
                rs.getDouble("lng"),
                rs.getDouble("altitude"),
                rs.getFloat("height_above_terrain"),
                rs.getFloat("accuracy_meters"),
                rs.getFloat("yaw_accuracy_degrees"),
                iso(rs.getObject("created_at", OffsetDateTime.class)),
                me != null && me.owns(owner, user),
                rs.getDouble("distance"),
                rs.getString("visibility"),
                iso(rs.getObject("unlock_at", OffsetDateTime.class)),
                rs.getBoolean("ready"),
                opened,
                !opened,
                rs.getBoolean("collecting"),
                rs.getInt("item_count"),
                rs.getInt("my_items"),
                CapsuleDtos.MAX_ITEMS,
                openedAt == null ? null : iso(openedAt),
                List.of());
        return new Stored(view, owner, user);
    }

    /** O urmă din capsulă, ca urmă obișnuită (stă în locul capsulei, la înălțimea ei). */
    private static TraceView mapItem(ResultSet rs, Viewer me, CapsuleView capsule) throws SQLException {
        return new TraceView(
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
                capsule.altitude(),
                capsule.heightAboveTerrain(),
                capsule.accuracyMeters(),
                capsule.yawAccuracyDegrees(),
                rs.getString("photo_source"),
                rs.getFloat("duration_seconds"),
                iso(rs.getObject("created_at", OffsetDateTime.class)),
                rs.getBoolean("has_media"),
                me != null && me.owns(rs.getString("owner_hash"), rs.getObject("user_id", UUID.class)),
                rs.getBoolean("reported"),
                0.0);
    }

    private static String iso(OffsetDateTime t) {
        return t.withOffsetSameInstant(ZoneOffset.UTC).format(DateTimeFormatter.ISO_OFFSET_DATE_TIME);
    }
}
