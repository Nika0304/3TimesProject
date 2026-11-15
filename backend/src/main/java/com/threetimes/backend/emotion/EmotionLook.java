package com.threetimes.backend.emotion;

import java.util.List;
import java.util.Locale;
import java.util.regex.Pattern;

/**
 * Cum arată o emoție: numele (liber, în română), intensitatea și forma aleasă de AI dintr-un set de forme de bază,
 * cu parametri. Aceleași câmpuri ca EmotionLook / SavedTrace din aplicația Unity.
 */
public record EmotionLook(
        String emotion,       // „dor”, „recunoștință”…
        int intensity,        // 0 = încet, 1 = mediu, 2 = puternic
        String shape,         // una din SHAPES
        String color,         // #RRGGBB
        String accentColor,   // #RRGGBB
        int petals,           // 3–12: petale / raze / vârfuri / brațe
        float sharpness,      // 0 = rotund, 1 = ascuțit
        String motion         // una din MOTIONS
) {
    public static final List<String> SHAPES = List.of(
            "flower", "drop", "crescent", "heart", "star", "spiral", "wave", "cloud", "flame", "sprout", "burst", "ring");
    public static final List<String> MOTIONS = List.of("pulse", "sway", "drift", "flicker", "breathe", "spin");
    private static final Pattern HEX = Pattern.compile("^#[0-9A-Fa-f]{6}$");

    /** Curăță ce a trimis AI-ul (sau telefonul): valori necunoscute devin unele implicite, numerele se limitează. */
    public EmotionLook sanitized() {
        String name = emotion == null ? "" : emotion.trim().toLowerCase(Locale.ROOT);
        if (name.length() > 40) name = name.substring(0, 40);
        if (name.isEmpty()) name = "emoție";
        return new EmotionLook(
                name,
                Math.max(0, Math.min(2, intensity)),
                SHAPES.contains(shape) ? shape : "flower",
                hexOr(color, "#F5B83D"),
                hexOr(accentColor, hexOr(color, "#E8961A")),
                Math.max(3, Math.min(12, petals == 0 ? 6 : petals)),
                Float.isNaN(sharpness) ? 0.3f : Math.max(0f, Math.min(1f, sharpness)),
                MOTIONS.contains(motion) ? motion : "breathe");
    }

    public static boolean isHex(String value) {
        return value != null && HEX.matcher(value.trim()).matches();
    }

    private static String hexOr(String value, String fallback) {
        return isHex(value) ? value.trim().toUpperCase(Locale.ROOT) : fallback;
    }
}
