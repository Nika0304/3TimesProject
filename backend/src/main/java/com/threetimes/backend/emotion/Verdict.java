package com.threetimes.backend.emotion;

/**
 * Verdictul AI-ului pentru textul unei urme:
 * ok = se publică; review = se publică, dar ajunge la admin; block = nu se publică (reason îi spune utilizatorului de ce).
 */
public record Verdict(String verdict, String reason) {

    public static final String OK = "ok";
    public static final String REVIEW = "review";
    public static final String BLOCK = "block";

    public static Verdict ok() {
        return new Verdict(OK, "");
    }

    /** AI-ul nu a răspuns: urma se publică, dar o verifică adminul. */
    public static Verdict unchecked() {
        return new Verdict(REVIEW, "Neverificată de AI (nu a răspuns).");
    }

    public boolean blocked() {
        return BLOCK.equals(verdict);
    }

    /** Curăță ce a răspuns modelul. */
    public static Verdict of(String verdict, String reason) {
        String v = verdict == null ? "" : verdict.trim().toLowerCase(java.util.Locale.ROOT);
        if (!v.equals(OK) && !v.equals(REVIEW) && !v.equals(BLOCK)) v = REVIEW;
        String r = reason == null ? "" : reason.trim();
        if (r.length() > 200) r = r.substring(0, 200);
        if (v.equals(BLOCK) && r.isEmpty()) r = "Mesajul nu respectă regulile aplicației.";
        return new Verdict(v, v.equals(OK) ? "" : r);
    }
}
