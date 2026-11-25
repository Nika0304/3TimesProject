package com.threetimes.backend.capsule;

import java.util.List;
import java.util.UUID;

import com.threetimes.backend.trace.TraceDtos.TraceView;

/**
 * Formatele JSON ale capsulelor. O capsulă e un loc + o dată, cu cel mult 10 urme înăuntru. Urmele din ea au aceeași
 * formă ca urmele obișnuite (TraceView / SavedTrace din Unity), ca după deschidere să se afișeze la fel.
 */
public final class CapsuleDtos {

    private CapsuleDtos() {
    }

    /** Cel mult atâtea urme într-o capsulă. */
    public static final int MAX_ITEMS = 10;

    /** Unde se lasă capsula și când se deschide (partea „capsule” la creare). */
    public record PlaceRequest(
            String id,
            Double latitude,
            Double longitude,
            Double altitude,
            Float heightAboveTerrain,
            Float accuracyMeters,
            Float yawAccuracyDegrees,
            String visibility,          // PUBLIC (de grup) sau PERSONAL (doar cu cont)
            String unlockAt             // ISO 8601
    ) {
    }

    /** Unde e telefonul când încearcă să deschidă capsula. */
    public record OpenRequest(Double latitude, Double longitude, Float accuracyMeters) {
    }

    /**
     * O capsulă, așa cum o vede telefonul care întreabă. Urmele (items) vin doar după ce a deschis-o (sealed = false);
     * până atunci se vede doar câte sunt (itemCount) și câte sunt ale lui (myItems).
     */
    public record CapsuleView(
            String id,
            String kind,               // "CAPSULE"
            double latitude,
            double longitude,
            double altitude,
            float heightAboveTerrain,
            float accuracyMeters,
            float yawAccuracyDegrees,
            String createdAt,
            boolean mine,              // creată de telefonul / contul care întreabă
            double distanceMeters,
            String visibility,
            String unlockAt,           // ISO 8601, UTC
            boolean ready,             // data a venit (ceasul serverului)
            boolean opened,            // am deschis-o
            boolean sealed,            // conținutul e încă ascuns pentru mine (= !opened)
            boolean collecting,        // mai primește urme (nesigilată, sub 7 zile, sub 10 urme, înainte de dată)
            int itemCount,
            int myItems,
            int maxItems,
            List<TraceView> items      // gol cât e sigilată pentru mine
    ) {
        CapsuleView withItems(List<TraceView> list) {
            return new CapsuleView(id, kind, latitude, longitude, altitude, heightAboveTerrain, accuracyMeters,
                    yawAccuracyDegrees, createdAt, mine, distanceMeters, visibility, unlockAt, ready, opened, sealed,
                    collecting, itemCount, myItems, maxItems, list);
        }
    }

    public record NearbyResponse(List<CapsuleView> capsules) {
    }

    public record Media(String contentType, byte[] data) {
    }

    /** O capsulă din bază, cu ce nu trimitem aplicației. */
    record Stored(CapsuleView view, String ownerHash, UUID userId) {
    }

    /** O urmă din capsulă, cu capsula și autorul ei. */
    record StoredItem(UUID capsuleId, String ownerHash, UUID userId, boolean hidden) {
    }
}
