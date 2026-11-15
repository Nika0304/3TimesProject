package com.threetimes.backend.emotion;

import java.util.List;

/** Răspunsul AI-ului pentru un text: emoțiile propuse (cu forma lor) și verdictul de moderare. */
public record Suggestion(List<EmotionLook> looks, Verdict verdict) {
}
