package com.threetimes.backend.emotion;

import java.util.List;

import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.common.ApiException;
import com.threetimes.backend.common.DeviceIds;

/**
 * POST /api/v1/emotion
 * <pre>
 * {"text": "Mi-e dor de bunica…"}                      → {"suggestions": [3 emoții, fiecare cu forma ei]}
 * {"text": "Mi-e dor de bunica…", "emotion": "furie"}  → {"suggestions": [forma aleasă pentru „furie”]}
 * </pre>
 * Emoțiile sunt libere (propuse de AI după text); forma e aleasă tot de AI, din formele de bază (EmotionLook).
 * Dacă LLM-ul nu e configurat sau nu răspunde: 503, iar aplicația folosește propunerea ei locală.
 * Același apel dă și verdictul de moderare (verdict, verdictReason); vezi și /moderation, mai jos.
 */
@RestController
@RequestMapping("/api/v1/emotion")
public class EmotionController {

    public record EmotionRequest(String text, String emotion) {
    }

    public record EmotionResponse(List<EmotionLook> suggestions, String verdict, String verdictReason) {
    }

    public record ModerationRequest(String text) {
    }

    private final EmotionService service;
    private final DeviceIds devices;

    public EmotionController(EmotionService service, DeviceIds devices) {
        this.service = service;
        this.devices = devices;
    }

    @PostMapping
    public EmotionResponse suggest(@RequestBody(required = false) EmotionRequest body,
                                   @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        String text = body == null || body.text() == null ? "" : body.text().trim();
        String emotion = body == null || body.emotion() == null ? "" : body.emotion().trim();
        if (text.isEmpty() && emotion.isEmpty()) throw ApiException.badRequest("Lipsește textul.");
        if (text.codePointCount(0, text.length()) > 280)
            throw ApiException.badRequest("Mesajul are mai mult de 280 de caractere.");
        if (emotion.length() > 40) throw ApiException.badRequest("Numele emoției e prea lung.");

        String who = devices.optional(device);
        var suggestion = service.suggest(text, emotion, who == null ? "anonim" : who)
                .orElseThrow(() -> new ApiException(HttpStatus.SERVICE_UNAVAILABLE,
                        "Propunerea automată nu e disponibilă acum."));
        return new EmotionResponse(suggestion.looks(), suggestion.verdict().verdict(), suggestion.verdict().reason());
    }

    /**
     * POST /api/v1/emotion/moderation {"text": "..."} → {"verdict": "ok|review|block", "reason": "..."}.
     * Aplicația întreabă la „Lasă urma”, ca un mesaj refuzat să poată fi corectat înainte de a închide foaia.
     * Serverul verifică oricum din nou la salvare (din cache, fără alt apel).
     */
    @PostMapping("/moderation")
    public Verdict moderation(@RequestBody(required = false) ModerationRequest body) {
        String text = body == null || body.text() == null ? "" : body.text().trim();
        if (text.codePointCount(0, text.length()) > 280)
            throw ApiException.badRequest("Mesajul are mai mult de 280 de caractere.");
        return service.verdict(text);
    }
}
