package com.threetimes.backend.emotion;

import java.util.ArrayDeque;
import java.util.Deque;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Service;

/**
 * Propunerea emoției și verdictul de moderare (același apel la Gemini). Cache pentru textele deja văzute
 * (economisim din cota gratuită; verificarea de la „Lasă urma” și cea de la salvare nu mai cer încă un apel)
 * și o limită de cereri pe telefon, ca un singur telefon să nu consume toată cota.
 */
@Service
public class EmotionService {

    private static final int CACHE_SIZE = 500;
    private static final long WINDOW_MS = 60_000;

    private final GeminiClient gemini;
    private final int perDevicePerMinute;

    private final Map<String, Suggestion> cache = lru();
    /** Verdictul depinde doar de text: îl ținem și separat, indiferent ce emoție s-a cerut. */
    private final Map<String, Verdict> verdicts = lru();
    private final Map<String, Deque<Long>> recent = new ConcurrentHashMap<>();

    public EmotionService(GeminiClient gemini,
                          @Value("${threetimes.llm.per-device-per-minute}") int perDevicePerMinute) {
        this.gemini = gemini;
        this.perDevicePerMinute = perDevicePerMinute;
    }

    /**
     * Fără emotion: până la 3 emoții propuse pentru text. Cu emotion: forma pentru acea emoție (o singură propunere).
     */
    public Optional<Suggestion> suggest(String text, String emotion, String device) {
        String key = emotion.toLowerCase(Locale.ROOT) + "\u0000" + normalize(text);
        synchronized (cache) {
            Suggestion cached = cache.get(key);
            if (cached != null) return Optional.of(cached);
        }
        if (!gemini.isConfigured() || !allow(device)) return Optional.empty();
        return ask(key, text, emotion);
    }

    /**
     * Verdictul pentru textul unei urme. Fără text: ok. Dacă AI-ul nu e configurat sau nu răspunde: review
     * (urma se publică, dar o vede adminul). Nu aplicăm limita pe minut: salvarea unei urme nu trebuie să eșueze.
     */
    public Verdict verdict(String text) {
        if (text == null || text.isBlank()) return Verdict.ok();
        String normalized = normalize(text);
        synchronized (verdicts) {
            Verdict cached = verdicts.get(normalized);
            if (cached != null) return cached;
        }
        if (!gemini.isConfigured()) return Verdict.unchecked();
        return ask("\u0000" + normalized, text.trim(), "")
                .map(Suggestion::verdict)
                .orElse(Verdict.unchecked());
    }

    private Optional<Suggestion> ask(String key, String text, String emotion) {
        Optional<Suggestion> result = gemini.suggest(text, emotion);
        result.ifPresent(s -> {
            synchronized (cache) {
                cache.put(key, s);
            }
            synchronized (verdicts) {
                verdicts.put(normalize(text), s.verdict());
            }
        });
        return result;
    }

    private static String normalize(String text) {
        return text == null ? "" : text.trim().toLowerCase(Locale.ROOT);
    }

    /** Cel mult perDevicePerMinute cereri pe minut de la același telefon. */
    private boolean allow(String device) {
        long now = System.currentTimeMillis();
        Deque<Long> times = recent.computeIfAbsent(device, d -> new ArrayDeque<>());
        synchronized (times) {
            while (!times.isEmpty() && now - times.peekFirst() > WINDOW_MS) times.pollFirst();
            if (times.size() >= perDevicePerMinute) return false;
            times.addLast(now);
            return true;
        }
    }

    private static <V> Map<String, V> lru() {
        return new LinkedHashMap<>(64, 0.75f, true) {
            @Override
            protected boolean removeEldestEntry(Map.Entry<String, V> eldest) {
                return size() > CACHE_SIZE;
            }
        };
    }

}
