package com.threetimes.backend.year;

import java.time.Instant;
import java.util.List;
import java.util.UUID;

import org.springframework.http.HttpHeaders;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.account.AccountService;
import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.DeviceIds;
import com.threetimes.backend.common.TestClock;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.trace.TraceDtos.TraceView;
import com.threetimes.backend.year.YearCapsuleRepository.EmotionCount;
import com.threetimes.backend.year.YearCapsuleRepository.Row;

/**
 * Capsula anului (făcută de aplicație):
 * <pre>
 * GET  /api/v1/year-capsules/nearby?lat=..&lng=..&radius=300   capsulele anului din jur (câte urme au)
 * POST /api/v1/year-capsules/{id}/open  {"latitude", "longitude", "accuracyMeters"}
 *      → după 1 ianuarie și de aproape: emoțiile anului + câteva urme alese la întâmplare; altfel 403 cu motivul
 * </pre>
 * Câmpurile au aceleași nume ca la capsulele obișnuite, ca aplicația să le afișeze la fel (kind = "YEAR_CAPSULE").
 */
@RestController
@RequestMapping("/api/v1/year-capsules")
public class YearCapsuleController {

    static final double OPEN_RADIUS = 25;   // sfera e mare: se deschide de puțin mai departe
    static final int HIGHLIGHTS = 10;

    public record OpenRequest(Double latitude, Double longitude, Float accuracyMeters) {
    }

    public record YearCapsuleView(String id, String kind, String city, int year, double latitude, double longitude,
                                  double altitude, float heightAboveTerrain, String createdAt, String visibility,
                                  String unlockAt, boolean mine, boolean ready, boolean opened, boolean sealed,
                                  boolean collecting, int itemCount, int myItems, int maxItems,
                                  List<TraceView> items, List<EmotionCount> emotions) {
    }

    public record NearbyResponse(List<YearCapsuleView> capsules) {
    }

    private final YearCapsuleRepository repository;
    private final DeviceIds devices;
    private final AccountService accounts;

    public YearCapsuleController(YearCapsuleRepository repository, DeviceIds devices, AccountService accounts) {
        this.repository = repository;
        this.devices = devices;
        this.accounts = accounts;
    }

    private Viewer viewer(String device, String auth, boolean deviceRequired) {
        String d = deviceRequired ? devices.require(device) : devices.optional(device);
        return new Viewer(d, accounts.userOf(auth));
    }

    @GetMapping("/nearby")
    public NearbyResponse nearby(@RequestParam double lat,
                                 @RequestParam double lng,
                                 @RequestParam(defaultValue = "300") double radius,
                                 @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                 @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        if (!Double.isFinite(lat) || !Double.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180)
            throw ApiException.badRequest("Coordonate invalide.");
        double r = Math.max(1, Math.min(radius, 15000));
        return new NearbyResponse(repository.nearby(lat, lng, r, viewer(device, auth, false)).stream()
                .map(row -> view(row, false, List.of(), List.of())).toList());
    }

    @PostMapping("/{id}/open")
    public YearCapsuleView open(@PathVariable UUID id,
                                @RequestBody(required = false) OpenRequest at,
                                @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        Viewer me = viewer(device, auth, true);
        Row row = repository.find(id, me).orElseThrow(ApiException::notFound);
        if (TestClock.now().isBefore(Instant.parse(row.unlockAt())))
            throw ApiException.forbidden("Capsula anului " + row.year() + " se deschide pe 1 ianuarie " + (row.year() + 1) + ".");
        // Ca la capsulele obișnuite: se poate deschide două săptămâni (1–14 ianuarie), apoi dispare.
        if (!TestClock.now().isBefore(Instant.parse(row.unlockAt()).plus(java.time.Duration.ofDays(14))))
            throw ApiException.forbidden("Capsula anului " + row.year() + " nu mai poate fi deschisă: s-a putut până pe 14 ianuarie.");
        if (at == null || at.latitude() == null || at.longitude() == null)
            throw ApiException.badRequest("Lipsește poziția telefonului.");
        double distance = repository.distance(id, at.latitude(), at.longitude());
        double allowed = OPEN_RADIUS + Math.min(5.0, Math.max(0.0, at.accuracyMeters() == null ? 0 : at.accuracyMeters()));
        if (distance > allowed) {
            long shown = Math.max(5, Math.round(distance / 5.0) * 5);
            long remaining = Math.max(5, Math.round((distance - OPEN_RADIUS + 2.5) / 5.0) * 5);
            throw ApiException.forbidden("Capsula anului e la vreo " + shown + " m. Mai mergi vreo " + remaining
                    + " m spre ea ca s-o poți deschide.");
        }
        return view(row, true, repository.highlights(id, HIGHLIGHTS, me), repository.emotions(id));
    }

    private static YearCapsuleView view(Row r, boolean opened, List<TraceView> items, List<EmotionCount> emotions) {
        // Capsulele anilor trecuți stau puțin mai la est (~4 m pe an), ca să nu se suprapună cu cea de anul acesta.
        int thisYear = TestClock.now().atZone(java.time.ZoneId.of("Europe/Bucharest")).getYear();
        double lng = r.longitude() + Math.max(0, thisYear - r.year()) * 0.00005;
        return new YearCapsuleView(r.id(), "YEAR_CAPSULE", r.city(), r.year(), r.latitude(), lng,
                r.altitude(), r.heightAboveTerrain(), r.startsAt(), "PUBLIC", r.unlockAt(), false, r.ready(),
                opened, !opened, !r.ready(), r.itemCount(), r.myItems(), 0, items, emotions);
    }
}
