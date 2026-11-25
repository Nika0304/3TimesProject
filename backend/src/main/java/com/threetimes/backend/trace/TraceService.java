package com.threetimes.backend.trace;

import java.util.List;
import java.util.Set;
import java.util.UUID;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.EmotionLook;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos.CreateRequest;
import com.threetimes.backend.trace.TraceDtos.Media;
import com.threetimes.backend.trace.TraceDtos.Stored;
import com.threetimes.backend.trace.TraceDtos.TraceView;

/** Regulile pentru urme: ce se acceptă la lăsare, cine vede, cine poate șterge, raportarea. */
@Service
public class TraceService {

    public static final int MAX_CONTENT = 280;
    static final int MAX_PHOTO_BYTES = 4 * 1024 * 1024;
    static final int MAX_VOICE_BYTES = 3 * 1024 * 1024;
    static final float MAX_VOICE_SECONDS = 31f;

    private static final Set<String> TYPES = Set.of("TEXT", "PHOTO", "VOICE");
    private static final Set<String> PHOTO_SOURCES = Set.of("SELFIE", "PLACE", "PLACE_WITH_TRACES");

    private final TraceRepository repository;
    private final int reportsToHide;
    private final int maxRadius;
    private final int maxLimit;

    public TraceService(TraceRepository repository,
                        @Value("${threetimes.reports-to-hide}") int reportsToHide,
                        @Value("${threetimes.max-radius-meters}") int maxRadius,
                        @Value("${threetimes.max-limit}") int maxLimit) {
        this.repository = repository;
        this.reportsToHide = reportsToHide;
        this.maxRadius = maxRadius;
        this.maxLimit = maxLimit;
    }

    public List<TraceView> nearby(double lat, double lng, double radius, int limit, Viewer me) {
        checkCoordinates(lat, lng);
        double r = Math.max(1, Math.min(radius, maxRadius));
        int n = Math.max(1, Math.min(limit, maxLimit));
        return repository.nearby(lat, lng, r, n, me);
    }

    /**
     * Lasă o urmă. Dacă telefonul retrimite aceeași urmă (același id), întoarce urma existentă,
     * fără să o dubleze. me.device() e obligatoriu; me.user() e contul, dacă e deschis.
     */
    @Transactional
    public TraceView create(CreateRequest request, byte[] media, Viewer me, Verdict verdict) {
        String owner = me.device();
        var r = normalize(request);
        String content = r.content() == null ? "" : r.content().trim();
        if (content.codePointCount(0, content.length()) > MAX_CONTENT)
            throw ApiException.badRequest("Mesajul are mai mult de " + MAX_CONTENT + " de caractere.");

        String mediaType = checkMedia(r, media, content);
        UUID id = parseOrNew(r.id());

        if (!repository.insert(id, r, content, owner, me.user(), verdict)) {
            Stored existing = repository.find(id, me).orElseThrow(ApiException::notFound);
            if (!me.owns(existing.ownerHash(), existing.userId()))
                throw new ApiException(HttpStatus.CONFLICT, "Există deja o urmă cu acest id.");
            return existing.view(); // retrimitere: urma e deja salvată
        }
        if (mediaType != null) repository.insertMedia(id, mediaType, media);
        return repository.find(id, me).orElseThrow().view();
    }

    public List<String> myIds(Viewer me) {
        return repository.idsOf(me);
    }

    /** Urmele contului (pentru „Harta mea”); cel mult 500, de la cea mai nouă. */
    public List<TraceView> ofUser(Viewer me) {
        if (me.user() == null) return List.of();
        return repository.ofUser(me, 500);
    }

    public TraceView get(UUID id, Viewer me) {
        return visible(id, me).view();
    }

    public Media media(UUID id, Viewer me) {
        visible(id, me);
        return repository.media(id).orElseThrow(ApiException::notFound);
    }

    @Transactional
    public void report(UUID id, Viewer me, String reason) {
        Stored trace = visible(id, me);
        if (me.owns(trace.ownerHash(), trace.userId()))
            throw ApiException.badRequest("Nu îți poți raporta propria urmă.");
        String r = reason == null ? null : reason.trim();
        if (r != null && r.length() > 200) r = r.substring(0, 200);
        repository.report(id, me.device(), r == null || r.isEmpty() ? null : r, reportsToHide);
    }

    @Transactional
    public void delete(UUID id, Viewer me) {
        Stored trace = repository.find(id, me).orElseThrow(ApiException::notFound);
        if (!me.owns(trace.ownerHash(), trace.userId()))
            throw ApiException.forbidden("Poți șterge doar urmele lăsate de tine.");
        // Ca la capsulele sigilate: o urmă din capsula unui an încheiat nu se mai scoate (rămân contul și adminul).
        if (repository.lockedInYearCapsule(id))
            throw ApiException.forbidden("Urma e în capsula anului, care s-a sigilat pe 1 ianuarie: nu se mai poate scoate.");
        repository.delete(id);
    }

    // ---------------------------------------------------------------- reguli

    /** Urmele ascunse după raportări le mai vede doar cine le-a lăsat. */
    private Stored visible(UUID id, Viewer me) {
        Stored trace = repository.find(id, me).orElseThrow(ApiException::notFound);
        if (trace.hidden() && (me == null || !me.owns(trace.ownerHash(), trace.userId()))) throw ApiException.notFound();
        return trace;
    }

    /** Completează valorile lipsă și verifică ce a trimis telefonul. */
    public static CreateRequest normalize(CreateRequest r) {
        if (r == null) throw ApiException.badRequest("Lipsește urma.");
        String type = upper(r.type());
        if (!TYPES.contains(type)) throw ApiException.badRequest("Tip necunoscut: " + r.type());
        // Emoția e liberă (propusă de AI sau scrisă de utilizator); urmele vechi trimit JOY / SADNESS / LONGING.
        String emotion = r.emotion() == null ? "" : r.emotion().trim();
        if (emotion.isEmpty()) throw ApiException.badRequest("Lipsește emoția.");
        if (emotion.length() > 40) throw ApiException.badRequest("Numele emoției e prea lung.");
        if (!java.util.List.of("JOY", "SADNESS", "LONGING").contains(emotion)) emotion = emotion.toLowerCase(java.util.Locale.ROOT);

        // Forma: dacă lipsește, aplicația desenează forma veche a emoției; dacă e trimisă, o curățăm.
        String shape = null, color = null, accent = null, motion = null;
        Integer petals = null;
        Float sharpness = null;
        if (r.shape() != null && !r.shape().isBlank()) {
            var look = new EmotionLook(emotion, 1, r.shape().trim(), r.color(), r.accentColor(),
                    r.petals() == null ? 6 : r.petals(), r.sharpness() == null ? 0.3f : r.sharpness(), r.motion()).sanitized();
            shape = look.shape();
            color = look.color();
            accent = look.accentColor();
            petals = look.petals();
            sharpness = look.sharpness();
            motion = look.motion();
        }
        if (r.latitude() == null || r.longitude() == null) throw ApiException.badRequest("Lipsește locul urmei.");
        checkCoordinates(r.latitude(), r.longitude());

        float intensity = r.intensity() == null ? 0.5f : r.intensity();
        if (intensity < 0f || intensity > 1f) throw ApiException.badRequest("Intensitatea trebuie să fie între 0 și 1.");
        double altitude = r.altitude() == null ? 0 : r.altitude();
        if (!Double.isFinite(altitude) || Math.abs(altitude) > 10000) throw ApiException.badRequest("Altitudine invalidă.");
        float height = r.heightAboveTerrain() == null ? 1.2f : r.heightAboveTerrain();
        if (height < 0f || height > 20f) throw ApiException.badRequest("Înălțimea față de sol e invalidă.");
        float accuracy = r.accuracyMeters() == null ? 0f : Math.max(0f, r.accuracyMeters());
        float yaw = r.yawAccuracyDegrees() == null ? 0f : Math.max(0f, r.yawAccuracyDegrees());

        String photoSource = null;
        if (type.equals("PHOTO")) {
            photoSource = upper(r.photoSource());
            if (photoSource.isEmpty()) photoSource = "PLACE";
            if (!PHOTO_SOURCES.contains(photoSource)) throw ApiException.badRequest("Sursă de poză necunoscută.");
        }
        float duration = 0f;
        if (type.equals("VOICE")) {
            duration = r.durationSeconds() == null ? 0f : r.durationSeconds();
            if (duration <= 0f || duration > MAX_VOICE_SECONDS)
                throw ApiException.badRequest("Înregistrarea trebuie să aibă cel mult 30 de secunde.");
        }

        return new CreateRequest(r.id(), type, r.content(), emotion, shape, color, accent, petals, sharpness, motion,
                intensity, r.latitude(), r.longitude(),
                altitude, height, accuracy, yaw, photoSource, duration, Boolean.TRUE.equals(r.yearCapsule()));
    }

    /** Verifică fișierul după conținut (nu după ce pretinde telefonul) și întoarce tipul lui. */
    public static String checkMedia(CreateRequest r, byte[] media, String content) {
        boolean hasMedia = media != null && media.length > 0;
        switch (r.type()) {
            case "TEXT" -> {
                if (content.isEmpty()) throw ApiException.badRequest("Urma text are nevoie de un mesaj.");
                if (hasMedia) throw ApiException.badRequest("Urma text nu are fișier.");
                return null;
            }
            case "PHOTO" -> {
                if (!hasMedia) throw ApiException.badRequest("Lipsește poza.");
                if (media.length > MAX_PHOTO_BYTES) throw ApiException.badRequest("Poza e prea mare.");
                if (!isJpeg(media)) throw ApiException.badRequest("Poza trebuie să fie JPEG.");
                return "image/jpeg";
            }
            default -> {
                if (!hasMedia) throw ApiException.badRequest("Lipsește înregistrarea.");
                if (media.length > MAX_VOICE_BYTES) throw ApiException.badRequest("Înregistrarea e prea mare.");
                if (!isWav(media)) throw ApiException.badRequest("Înregistrarea trebuie să fie WAV.");
                return "audio/wav";
            }
        }
    }

    static boolean isJpeg(byte[] b) {
        return b.length > 3 && (b[0] & 0xFF) == 0xFF && (b[1] & 0xFF) == 0xD8 && (b[2] & 0xFF) == 0xFF;
    }

    static boolean isWav(byte[] b) {
        return b.length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E';
    }

    private static void checkCoordinates(double lat, double lng) {
        if (!Double.isFinite(lat) || !Double.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180)
            throw ApiException.badRequest("Coordonate invalide.");
    }

    public static UUID parseOrNew(String id) {
        if (id == null || id.isBlank()) return UUID.randomUUID();
        String s = id.trim();
        // Aplicația folosește Guid.ToString("N") (fără cratime): îl acceptăm și așa.
        if (s.length() == 32 && !s.contains("-"))
            s = s.substring(0, 8) + "-" + s.substring(8, 12) + "-" + s.substring(12, 16) + "-"
                    + s.substring(16, 20) + "-" + s.substring(20);
        try {
            return UUID.fromString(s);
        } catch (IllegalArgumentException e) {
            throw ApiException.badRequest("Id invalid.");
        }
    }

    private static String upper(String s) {
        return s == null ? "" : s.trim().toUpperCase(java.util.Locale.ROOT);
    }
}
