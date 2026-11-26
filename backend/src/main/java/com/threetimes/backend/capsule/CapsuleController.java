package com.threetimes.backend.capsule;

import java.io.IOException;
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
import com.threetimes.backend.capsule.CapsuleDtos.CapsuleView;
import com.threetimes.backend.capsule.CapsuleDtos.Media;
import com.threetimes.backend.capsule.CapsuleDtos.NearbyResponse;
import com.threetimes.backend.capsule.CapsuleDtos.OpenRequest;
import com.threetimes.backend.capsule.CapsuleDtos.PlaceRequest;
import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.DeviceIds;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.emotion.EmotionService;
import com.threetimes.backend.emotion.Verdict;
import com.threetimes.backend.trace.TraceDtos;

/**
 * API-ul capsulelor timpului (modul Viitor):
 * <pre>
 * GET    /api/v1/capsules/nearby?lat=..&lng=..&radius=300   capsulele din jur (urmele din ele doar după deschidere)
 * GET    /api/v1/capsules/mine         capsulele mele (create de mine sau cu urmele mele), pentru „Harta mea”
 * POST   /api/v1/capsules              multipart: „capsule” = locul + visibility + unlockAt; „item” = prima urmă
 *                                      (JSON ca la urme); „media” = poza / vocea ei
 * POST   /api/v1/capsules/{id}/items   multipart: „item” + „media”   pune încă o urmă (de la cel mult 15 m)
 * POST   /api/v1/capsules/{id}/seal    creatorul o sigilează (nu mai primește urme)
 * POST   /api/v1/capsules/{id}/open    {"latitude", "longitude", "accuracyMeters"}   deschide (loc + dată), sau 403
 * GET    /api/v1/capsules/items/{itemId}/media    poza / vocea unei urme din capsulă (după deschidere)
 * POST   /api/v1/capsules/items/{itemId}/report   raportează o urmă din capsulă (după deschidere)
 * DELETE /api/v1/capsules/items/{itemId}          șterge urma mea din capsulă (doar până la sigilare)
 * DELETE /api/v1/capsules/{id}/items/mine         scoate toate urmele mele (doar până la sigilare)
 * DELETE /api/v1/capsules/{id}                    șterge capsula creată de mine
 * </pre>
 */
@RestController
@RequestMapping("/api/v1/capsules")
public class CapsuleController {

    private final CapsuleService service;
    private final DeviceIds devices;
    private final ObjectMapper json;
    private final EmotionService ai;
    private final AccountService accounts;

    public CapsuleController(CapsuleService service, DeviceIds devices, ObjectMapper json, EmotionService ai,
                             AccountService accounts) {
        this.service = service;
        this.devices = devices;
        this.json = json;
        this.ai = ai;
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
                                 @RequestParam(defaultValue = "50") int limit,
                                 @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                 @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return new NearbyResponse(service.nearby(lat, lng, radius, limit, viewer(device, auth, false)));
    }

    @GetMapping("/mine")
    public NearbyResponse mine(@RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                               @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return new NearbyResponse(service.mine(viewer(device, auth, true)));
    }

    @PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    public ResponseEntity<CapsuleView> create(@RequestPart("capsule") String capsuleJson,
                                              @RequestPart("item") String itemJson,
                                              @RequestPart(value = "media", required = false) MultipartFile media,
                                              @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                              @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth)
            throws IOException {
        Viewer me = viewer(device, auth, true);
        PlaceRequest place;
        try {
            place = json.readValue(capsuleJson, PlaceRequest.class);
        } catch (JsonProcessingException e) {
            throw ApiException.badRequest("Capsula nu e un JSON valid.");
        }
        TraceDtos.CreateRequest item = readItem(itemJson);
        boolean personal = place != null && "PERSONAL".equalsIgnoreCase(place.visibility());
        Verdict verdict = moderate(item, personal);
        return ResponseEntity.status(HttpStatus.CREATED).body(service.create(place, item, bytes(media), me, verdict));
    }

    @PostMapping(value = "/{id}/items", consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
    public CapsuleView addItem(@PathVariable UUID id,
                               @RequestPart("item") String itemJson,
                               @RequestPart(value = "media", required = false) MultipartFile media,
                               @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                               @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth)
            throws IOException {
        Viewer me = viewer(device, auth, true);
        TraceDtos.CreateRequest item = readItem(itemJson);
        // Moderarea se hotărăște după capsula din bază, nu după ce spune telefonul.
        Verdict verdict = moderate(item, service.isPersonal(id, me));
        return service.addItem(id, item, bytes(media), me, verdict);
    }

    @PostMapping("/{id}/seal")
    public CapsuleView seal(@PathVariable UUID id,
                            @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                            @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return service.seal(id, viewer(device, auth, true));
    }

    @PostMapping("/{id}/open")
    public CapsuleView open(@PathVariable UUID id,
                            @RequestBody(required = false) OpenRequest body,
                            @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                            @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return service.open(id, body, viewer(device, auth, true));
    }

    @GetMapping("/items/{itemId}/media")
    public ResponseEntity<byte[]> itemMedia(@PathVariable UUID itemId,
                                            @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                            @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        Media media = service.itemMedia(itemId, viewer(device, auth, true));
        return ResponseEntity.ok()
                .contentType(MediaType.parseMediaType(media.contentType()))
                .cacheControl(CacheControl.noStore())
                .body(media.data());
    }

    @PostMapping("/items/{itemId}/report")
    public ResponseEntity<Void> reportItem(@PathVariable UUID itemId,
                                           @RequestBody(required = false) TraceDtos.ReportRequest body,
                                           @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                           @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.reportItem(itemId, viewer(device, auth, true), body == null ? null : body.reason());
        return ResponseEntity.noContent().build();
    }

    /** Scoate toate urmele mele dintr-o capsulă care încă se umple. */
    @DeleteMapping("/{id}/items/mine")
    public ResponseEntity<Void> deleteMyItems(@PathVariable UUID id,
                                              @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                              @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.deleteMyItems(id, viewer(device, auth, true));
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/items/{itemId}")
    public ResponseEntity<Void> deleteItem(@PathVariable UUID itemId,
                                           @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                           @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.deleteItem(itemId, viewer(device, auth, true));
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/{id}")
    public ResponseEntity<Void> delete(@PathVariable UUID id,
                                       @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                       @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        service.delete(id, viewer(device, auth, true));
        return ResponseEntity.noContent().build();
    }

    private TraceDtos.CreateRequest readItem(String itemJson) {
        try {
            return json.readValue(itemJson, TraceDtos.CreateRequest.class);
        } catch (JsonProcessingException e) {
            throw ApiException.badRequest("Urma nu e un JSON valid.");
        }
    }

    /**
     * Urmele din capsulele publice trec prin moderare (AI), ca urmele obișnuite; cele din capsulele personale le
     * citește doar cine le-a pus. Refuzată → 422 cu motivul.
     */
    private Verdict moderate(TraceDtos.CreateRequest item, boolean personal) {
        if (personal) return Verdict.ok();
        String content = item == null || item.content() == null ? "" : item.content().trim();
        Verdict verdict = ai.verdict(content);
        if (verdict.blocked()) throw new ApiException(HttpStatus.UNPROCESSABLE_ENTITY, verdict.reason());
        return verdict;
    }

    private static byte[] bytes(MultipartFile media) throws IOException {
        return media == null || media.isEmpty() ? null : media.getBytes();
    }
}
