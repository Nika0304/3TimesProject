package com.threetimes.backend.trace;

import java.util.List;

/**
 * Formatele JSON ale API-ului de urme. Numele câmpurilor sunt aceleași ca în aplicația Unity
 * (SavedTrace), ca JsonUtility să le citească direct.
 */
public final class TraceDtos {

    private TraceDtos() {
    }

    /** Ce trimite aplicația când lasă o urmă (partea „trace” din cererea multipart). */
    public record CreateRequest(
            String id,                  // opțional: id-ul generat de telefon (UUID), ca o retrimitere să nu dubleze urma
            String type,                // TEXT, PHOTO, VOICE
            String content,             // mesajul; la poză și voce e descrierea (opțională)
            String emotion,             // numele emoției, liber („dor”, „recunoștință”); vechi: JOY, SADNESS, LONGING
            String shape,               // forma aleasă de AI (EmotionLook.SHAPES); goală la urmele vechi
            String color,               // #RRGGBB
            String accentColor,         // #RRGGBB
            Integer petals,             // 3–12
            Float sharpness,            // 0–1
            String motion,              // EmotionLook.MOTIONS
            Float intensity,            // 0..1
            Double latitude,
            Double longitude,
            Double altitude,            // WGS84
            Float heightAboveTerrain,   // metri peste sol
            Float accuracyMeters,
            Float yawAccuracyDegrees,
            String photoSource,         // SELFIE, PLACE, PLACE_WITH_TRACES (doar la poză)
            Float durationSeconds       // doar la voce
    ) {
    }

    /** O urmă, așa cum o vede aplicația. */
    public record TraceView(
            String id,
            String type,
            String content,
            String emotion,
            String shape,
            String color,
            String accentColor,
            int petals,
            float sharpness,
            String motion,
            float intensity,
            double latitude,
            double longitude,
            double altitude,
            float heightAboveTerrain,
            float accuracyMeters,
            float yawAccuracyDegrees,
            String photoSource,
            float durationSeconds,
            String createdAt,           // ISO 8601, UTC
            boolean hasMedia,
            boolean mine,               // lăsată de telefonul care întreabă
            boolean reported,           // raportată de telefonul care întreabă
            double distanceMeters       // față de punctul din cerere (0 când nu e cazul)
    ) {
    }

    /** Răspunsul pentru „urmele din jur”, într-un obiect (JsonUtility nu citește liste goale de la rădăcină). */
    public record NearbyResponse(List<TraceView> traces) {
    }

    public record ReportRequest(String reason) {
    }

    /** Poza sau vocea unei urme. */
    public record Media(String contentType, byte[] data) {
    }

    /** O urmă din bază, cu ce nu trimitem aplicației (cine a lăsat-o, dacă e ascunsă). */
    record Stored(TraceView view, String ownerHash, boolean hidden) {
    }
}
