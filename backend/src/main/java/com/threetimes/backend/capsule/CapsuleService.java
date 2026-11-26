package com.threetimes.backend.capsule;

import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.format.DateTimeParseException;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Locale;
import java.util.UUID;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import com.threetimes.backend.capsule.CapsuleDtos.CapsuleView;
import com.threetimes.backend.capsule.CapsuleDtos.Media;
import com.threetimes.backend.capsule.CapsuleDtos.OpenRequest;
import com.threetimes.backend.capsule.CapsuleDtos.PlaceRequest;
import com.threetimes.backend.capsule.CapsuleDtos.Stored;
import com.threetimes.backend.capsule.CapsuleDtos.StoredItem;
import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.TestClock;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos;
import com.threetimes.backend.trace.TraceService;

/**
 * Regulile capsulelor:
 * <ul>
 * <li>data: minim mâine, maxim 10 ani, deschidere la miezul nopții (ora României); verificată cu ceasul serverului;</li>
 * <li>cel mult 10 urme; capsula personală (doar cu cont) o umple doar contul ei, cea publică e de grup: oricine e la
 * cel mult 15 m își poate pune urma, până o sigilează creatorul (sau singură: 7 zile, 10 urme, data deschiderii);</li>
 * <li>deschiderea: doar în locul ei (15 m) și după dată; abia atunci pleacă urmele spre telefon.</li>
 * </ul>
 */
@Service
public class CapsuleService {

    public static final ZoneId ZONE = ZoneId.of("Europe/Bucharest");
    private static final String[] MONTHS = { "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie",
            "august", "septembrie", "octombrie", "noiembrie", "decembrie" };

    private final CapsuleRepository repository;
    private final int reportsToHide;
    private final int maxRadius;
    private final int maxLimit;
    private final double openRadius;
    private final boolean testUnlock;

    public CapsuleService(CapsuleRepository repository,
                          @Value("${threetimes.reports-to-hide}") int reportsToHide,
                          @Value("${threetimes.max-radius-meters}") int maxRadius,
                          @Value("${threetimes.max-limit}") int maxLimit,
                          @Value("${threetimes.capsules.open-radius-meters:15}") double openRadius,
                          @Value("${threetimes.capsules.test-unlock:false}") boolean testUnlock) {
        this.repository = repository;
        this.reportsToHide = reportsToHide;
        this.maxRadius = maxRadius;
        this.maxLimit = maxLimit;
        this.openRadius = openRadius;
        this.testUnlock = testUnlock;
    }

    /** Capsulele mele (create de mine sau cu urmele mele), pentru „Harta mea”. */
    public List<CapsuleView> mine(Viewer me) {
        return repository.mine(me);
    }

    public List<CapsuleView> nearby(double lat, double lng, double radius, int limit, Viewer me) {
        checkCoordinates(lat, lng);
        double r = Math.max(1, Math.min(radius, maxRadius));
        int n = Math.max(1, Math.min(limit, maxLimit));
        return repository.nearby(lat, lng, r, n, me);
    }

    /** Creează capsula cu prima ei urmă. Retrimiterea (același id) nu o dublează. */
    @Transactional
    public CapsuleView create(PlaceRequest place, TraceDtos.CreateRequest item, byte[] media, Viewer me, Verdict verdict) {
        if (place == null || place.latitude() == null || place.longitude() == null)
            throw ApiException.badRequest("Lipsește locul capsulei.");
        checkCoordinates(place.latitude(), place.longitude());
        String visibility = place.visibility() == null ? "PUBLIC" : place.visibility().trim().toUpperCase(Locale.ROOT);
        if (!visibility.equals("PUBLIC") && !visibility.equals("PERSONAL"))
            throw ApiException.badRequest("Tipul capsulei trebuie să fie PUBLIC sau PERSONAL.");
        if (visibility.equals("PERSONAL") && me.user() == null)
            throw new ApiException(HttpStatus.UNAUTHORIZED, "O capsulă personală cere cont. Intră în cont și mai încearcă.");
        Instant unlockAt = checkUnlock(place.unlockAt());

        UUID id = TraceService.parseOrNew(place.id());
        if (!repository.insertCapsule(id, place, visibility, unlockAt, me)) {
            Stored existing = repository.find(id, me).orElseThrow(ApiException::notFound);
            if (!me.owns(existing.ownerHash(), existing.userId()))
                throw new ApiException(HttpStatus.CONFLICT, "Există deja o capsulă cu acest id.");
            return existing.view();
        }
        insertItem(id, item, media, me, verdict, visibility.equals("PERSONAL"));
        return repository.find(id, me).orElseThrow().view();
    }

    /**
     * Pune încă o urmă într-o capsulă care mai primește urme. Cine pune trebuie să fie acolo (cel mult 15 m);
     * în capsula personală pune doar contul ei.
     */
    @Transactional
    public CapsuleView addItem(UUID capsuleId, TraceDtos.CreateRequest item, byte[] media, Viewer me, Verdict verdict) {
        Stored capsule = visible(capsuleId, me);
        CapsuleView v = capsule.view();
        boolean mine = me.owns(capsule.ownerHash(), capsule.userId());
        if ("PERSONAL".equals(v.visibility()) && !mine)
            throw ApiException.forbidden("În capsula aceasta pune doar cine a creat-o.");
        if (!v.collecting())
            throw ApiException.forbidden(v.itemCount() >= CapsuleDtos.MAX_ITEMS
                    ? "Capsula e plină (" + CapsuleDtos.MAX_ITEMS + " urme)."
                    : "Capsula e deja sigilată.");
        if (item == null || item.latitude() == null || item.longitude() == null)
            throw ApiException.badRequest("Lipsește locul urmei.");
        double distance = repository.distance(capsuleId, item.latitude(), item.longitude());
        double allowed = openRadius + Math.min(5.0, Math.max(0.0, item.accuracyMeters() == null ? 0 : item.accuracyMeters()));
        if (distance > allowed)
            throw ApiException.forbidden(farText(distance) + " ca să-ți pui urma în ea.");
        insertItem(capsuleId, item, media, me, verdict, "PERSONAL".equals(v.visibility()));
        return repository.find(capsuleId, me).orElseThrow().view();
    }

    private void insertItem(UUID capsuleId, TraceDtos.CreateRequest request, byte[] media, Viewer me, Verdict verdict,
                            boolean personal) {
        var r = TraceService.normalize(request);
        // Pozele și vocile din capsulele publice nu le verifică AI-ul și nu le poate raporta nimeni până la deschidere:
        // le vede adminul înainte („Capsule de verificat”). Nu blochează deschiderea dacă adminul nu ajunge la ele.
        if (!personal && !"TEXT".equals(r.type()) && !Verdict.REVIEW.equals(verdict.verdict()))
            verdict = new Verdict(Verdict.REVIEW, ("PHOTO".equals(r.type()) ? "Poză" : "Voce")
                    + " într-o capsulă publică: de verificat înainte de deschidere.");
        String content = r.content() == null ? "" : r.content().trim();
        if (content.codePointCount(0, content.length()) > TraceService.MAX_CONTENT)
            throw ApiException.badRequest("Mesajul are mai mult de " + TraceService.MAX_CONTENT + " de caractere.");
        String mediaType = TraceService.checkMedia(r, media, content);
        UUID itemId = TraceService.parseOrNew(r.id());
        if (!repository.insertItem(itemId, capsuleId, r, content, me, verdict)) return; // retrimitere
        if (mediaType != null) repository.insertItemMedia(itemId, mediaType, media);
    }

    /** Creatorul închide capsula pentru urme noi (cea de grup); de acum așteaptă data deschiderii. */
    @Transactional
    public CapsuleView seal(UUID id, Viewer me) {
        Stored capsule = visible(id, me);
        if (!me.owns(capsule.ownerHash(), capsule.userId()))
            throw ApiException.forbidden("Doar cine a creat capsula o poate sigila.");
        repository.seal(id);
        return repository.find(id, me).orElseThrow().view();
    }

    /**
     * Data deschiderii: minim mâine (la 00:00, ora României), maxim peste 10 ani. Pentru teste
     * (threetimes.capsules.test-unlock), se acceptă și peste un minut.
     */
    Instant checkUnlock(String text) {
        if (text == null || text.isBlank()) throw ApiException.badRequest("Lipsește data deschiderii.");
        Instant unlock;
        try {
            unlock = Instant.parse(text.trim());
        } catch (DateTimeParseException e) {
            throw ApiException.badRequest("Data deschiderii nu e validă.");
        }
        Instant now = TestClock.now();
        Instant tomorrow = LocalDate.ofInstant(now, ZONE).plusDays(1).atStartOfDay(ZONE).toInstant();
        Instant earliest = testUnlock ? now.plusSeconds(30) : tomorrow.minus(Duration.ofMinutes(1));
        if (unlock.isBefore(earliest))
            throw ApiException.badRequest("Capsula se poate deschide cel mai devreme mâine.");
        if (unlock.isAfter(now.atZone(ZONE).plusYears(10).plusDays(1).toInstant()))
            throw ApiException.badRequest("Capsula se poate deschide cel mai târziu peste 10 ani.");
        return unlock;
    }

    /** Deschiderea: data a venit și telefonul e destul de aproape. Altfel 403, cu motivul, fără conținut. */
    @Transactional
    public CapsuleView open(UUID id, OpenRequest at, Viewer me) {
        Stored capsule = visible(id, me);
        if (capsule.view().opened()) return capsule.view(); // redeschidere

        Instant unlock = Instant.parse(capsule.view().unlockAt());
        if (TestClock.now().isBefore(unlock))
            throw ApiException.forbidden("Capsula se deschide " + whenText(unlock) + ".");
        if (!TestClock.now().isBefore(unlock.plus(Duration.ofDays(CapsuleDtos.OPEN_WINDOW_DAYS))))
            throw ApiException.forbidden("Capsula nu mai poate fi deschisă: au trecut cele două săptămâni de după data ei.");

        if (at == null || at.latitude() == null || at.longitude() == null)
            throw ApiException.badRequest("Lipsește poziția telefonului.");
        double distance = repository.distance(id, at.latitude(), at.longitude());
        double allowed = openRadius + Math.min(5.0, Math.max(0.0, at.accuracyMeters() == null ? 0 : at.accuracyMeters()));
        if (distance > allowed) throw ApiException.forbidden(farText(distance) + " ca s-o poți deschide.");
        repository.recordOpen(id, me);
        return repository.find(id, me).orElseThrow().view();
    }

    public Media itemMedia(UUID itemId, Viewer me) {
        StoredItem item = repository.findItem(itemId).orElseThrow(ApiException::notFound);
        Stored capsule = visible(item.capsuleId(), me);
        boolean mineItem = me.owns(item.ownerHash(), item.userId());
        if (!capsule.view().opened() && !mineItem) throw ApiException.forbidden("Capsula nu a fost deschisă încă.");
        if (item.hidden() && !mineItem) throw ApiException.notFound();
        return repository.itemMedia(itemId).orElseThrow(ApiException::notFound);
    }

    @Transactional
    public void reportItem(UUID itemId, Viewer me, String reason) {
        StoredItem item = repository.findItem(itemId).orElseThrow(ApiException::notFound);
        Stored capsule = visible(item.capsuleId(), me);
        if (me.owns(item.ownerHash(), item.userId()))
            throw ApiException.badRequest("Nu îți poți raporta propria urmă.");
        if (!capsule.view().opened()) throw ApiException.forbidden("Poți raporta o urmă doar după ce deschizi capsula.");
        String r = reason == null ? null : reason.trim();
        if (r != null && r.length() > 200) r = r.substring(0, 200);
        repository.reportItem(itemId, me.device(), r == null || r.isEmpty() ? null : r, reportsToHide);
    }

    /**
     * Urma mea dintr-o capsulă se poate scoate doar cât capsula mai primește urme. După sigilare nu se mai schimbă
     * nimic înăuntru (cine o deschide peste ani găsește exact ce s-a pus); rămân ștergerea contului și adminul.
     */
    @Transactional
    public void deleteItem(UUID itemId, Viewer me) {
        StoredItem item = repository.findItem(itemId).orElseThrow(ApiException::notFound);
        if (!me.owns(item.ownerHash(), item.userId()))
            throw ApiException.forbidden("Poți șterge doar urmele puse de tine.");
        Stored capsule = repository.find(item.capsuleId(), me).orElseThrow(ApiException::notFound);
        if (!capsule.view().collecting()) throw ApiException.forbidden(SEALED);
        repository.deleteItem(itemId);
    }

    /** Scoate toate urmele mele dintr-o capsulă care încă se umple (până la sigilare). */
    @Transactional
    public void deleteMyItems(UUID id, Viewer me) {
        Stored capsule = visible(id, me);
        if (!capsule.view().collecting()) throw ApiException.forbidden(SEALED);
        repository.deleteItemsOf(id, me);
    }

    /**
     * Creatorul își șterge capsula cât se umple; după sigilare, doar dacă toate urmele din ea sunt ale lui
     * (altfel ar scoate și urmele altora).
     */
    @Transactional
    public void delete(UUID id, Viewer me) {
        Stored capsule = repository.find(id, me).orElseThrow(ApiException::notFound);
        if (!me.owns(capsule.ownerHash(), capsule.userId()))
            throw ApiException.forbidden("Poți șterge doar capsulele create de tine.");
        CapsuleView v = capsule.view();
        if (!v.collecting() && v.itemCount() > v.myItems())
            throw ApiException.forbidden("Capsula e sigilată și are și urmele altora: nu se mai poate șterge.");
        repository.deleteCapsule(id);
    }

    private static final String SEALED = "Capsula e sigilată: urmele din ea nu se mai pot scoate.";

    public boolean isPersonal(UUID id, Viewer me) {
        return "PERSONAL".equals(visible(id, me).view().visibility());
    }

    /** Capsulele personale le vede doar contul lor. */
    private Stored visible(UUID id, Viewer me) {
        Stored capsule = repository.find(id, me).orElseThrow(ApiException::notFound);
        boolean mine = me != null && me.owns(capsule.ownerHash(), capsule.userId());
        if (!mine && "PERSONAL".equals(capsule.view().visibility())) throw ApiException.notFound();
        return capsule;
    }

    private String farText(double distance) {
        long shown = Math.max(5, Math.round(distance / 5.0) * 5);
        long remaining = Math.max(5, Math.round((distance - openRadius + 2.5) / 5.0) * 5);
        return "Capsula e la vreo " + shown + " m. Mai mergi vreo " + remaining + " m spre ea";
    }

    private static void checkCoordinates(double lat, double lng) {
        if (!Double.isFinite(lat) || !Double.isFinite(lng) || Math.abs(lat) > 90 || Math.abs(lng) > 180)
            throw ApiException.badRequest("Coordonate invalide.");
    }

    /** „pe 1 ianuarie 2027 (peste 37 de zile)”, „mâine (…)”, „azi la 14:05”. */
    static String whenText(Instant unlock) {
        ZonedDateTime at = unlock.atZone(ZONE);
        LocalDate today = LocalDate.ofInstant(TestClock.now(), ZONE);
        long days = ChronoUnit.DAYS.between(today, at.toLocalDate());
        if (days <= 0) return "azi la " + String.format("%02d:%02d", at.getHour(), at.getMinute());
        String date = at.getDayOfMonth() + " " + MONTHS[at.getMonthValue() - 1] + " " + at.getYear();
        if (days == 1) return "mâine (" + date + ")";
        return "pe " + date + " (peste " + days + (days % 100 >= 20 || days % 100 == 0 ? " de zile)" : " zile)");
    }
}
