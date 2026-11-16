package com.threetimes.backend.trace;

import java.io.IOException;
import java.time.Duration;
import java.util.UUID;

import org.springframework.http.CacheControl;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RequestPart;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.multipart.MultipartFile;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.threetimes.backend.account.AccountService;
import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.DeviceIds;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.EmotionService;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos.CreateRequest;
import com.threetimes.backend.trace.TraceDtos.Media;
import com.threetimes.backend.trace.TraceDtos.NearbyResponse;
import com.threetimes.backend.trace.TraceDtos.ReportRequest;
import com.threetimes.backend.trace.TraceDtos.TraceView;

/**
 * API-ul urmelor (modul Prezent):
 * <pre>
 * GET    /api/v1/traces/nearby?lat=..&lng=..&radius=300&limit=50   urmele din jur, de la cea mai apropiată
 * POST   /api/v1/traces            (multipart: „trace” = JSON, „media” = JPEG/WAV)   lasă o urmă
 *                                   (textul e verificat de AI; refuzat → 422 {"error": motivul})
 * GET    /api/v1/traces/mine                                       id-urile urmelor mele (cele șterse de admin lipsesc)
 * GET    /api/v1/traces/{id}                                       o urmă
 * GET    /api/v1/traces/{id}/media                                 poza sau vocea
 * POST   /api/v1/traces/{id}/report   {"reason": "..."}            raportează
 * DELETE /api/v1/traces/{id}                                       șterge urma mea
 * </pre>
 * Telefonul se identifică prin antetul X-Device-Id (obligatoriu la scriere). Cu contul deschis, aplicația trimite
 * și „Authorization: Bearer ...”: urmele lăsate atunci intră în cont și sunt „ale mele” de pe orice telefon.
 */
@RestController
@RequestMapping("/api/v1/traces")
public class TraceController {

    private final TraceService service;
    private final DeviceIds devices;
    private final ObjectMapper json;
    private final EmotionService ai;
    private final AccountService accounts;

    public TraceController(TraceService service, DeviceIds devices, ObjectMapper json, EmotionService ai,
                           AccountService accounts) {
        this.service = service;
        this.devices = devices;
        this.json = json;
        this.ai = ai;
        this.accounts = accounts;
    }

    /** Cine întreabă: telefonul (obligatoriu la scriere) și contul, dacă sesiunea e validă (altfel fără cont). */
    private Viewer viewer(String device, String auth, boolean deviceRequired) {
        String d = deviceRequired ? devices.require(device) : devices.optional(device);
        return new Viewer(d, accounts.userOf(auth));
    }

    @GetMapping("/nearby")
    public NearbyResponse nearby(@RequestParam double lat,
                                 @RequestParam double lng,
                                 @RequestParam(defaultValue = "300") double radius,
                                 @RequestParam(defaultValue = "50") int limit,
                                 @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                 @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return new NearbyResponse(service.nearby(lat, lng, radius, limit, viewer(device, auth, false)));
    }

    @PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    public ResponseEntity<TraceView> create(@RequestPart("trace") String traceJson,
                                            @RequestPart(value = "media", required = false) MultipartFile media,
                                            @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                            @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth)
            throws IOException {
        Viewer me = viewer(device, auth, true);
        CreateRequest request;
        try {
            request = json.readValue(traceJson, CreateRequest.class);
        } catch (JsonProcessingException e) {
            throw ApiException.badRequest("Urma nu e un JSON valid.");
        }
        byte[] bytes = media == null || media.isEmpty() ? null : media.getBytes();

        // Moderarea textului (AI), înainte de tranzacție: apelul la Gemini poate dura câteva secunde.
        // Refuzată → 422 cu motivul, iar aplicația îi spune utilizatorului și scoate urma.
        String content = request == null || request.content() == null ? "" : request.content().trim();
        Verdict verdict = ai.verdict(content);
        if (verdict.blocked()) throw new ApiException(HttpStatus.UNPROCESSABLE_ENTITY, verdict.reason());

        return ResponseEntity.status(HttpStatus.CREATED).body(service.create(request, bytes, me, verdict));
    }

    public record MyIds(java.util.List<String> ids) {
    }

    /** Aplicația păstrează urmele proprii pe telefon: așa află care au fost șterse de moderatori. */
    @GetMapping("/mine")
    public MyIds mine(@RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                      @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return new MyIds(service.myIds(viewer(device, auth, true)));
    }

    @GetMapping("/{id}")
    public TraceView get(@PathVariable UUID id,
                         @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                         @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return service.get(id, viewer(device, auth, false));
    }

    @GetMapping("/{id}/media")
    public ResponseEntity<byte[]> media(@PathVariable UUID id,
                                        @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                        @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        Media media = service.media(id, viewer(device, auth, false));
        return ResponseEntity.ok()
                .contentType(MediaType.parseMediaType(media.contentType()))
                .cacheControl(CacheControl.maxAge(Duration.ofDays(7)).cachePrivate())
                .body(media.data());
    }

    @PostMapping("/{id}/report")
    public ResponseEntity<Void> report(@PathVariable UUID id,
                                       @RequestBody(required = false) ReportRequest body,
                                       @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                       @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.report(id, viewer(device, auth, true), body == null ? null : body.reason());
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/{id}")
    public ResponseEntity<Void> delete(@PathVariable UUID id,
                                       @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                       @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.delete(id, viewer(device, auth, true));
        return ResponseEntity.noContent().build();
    }
}
