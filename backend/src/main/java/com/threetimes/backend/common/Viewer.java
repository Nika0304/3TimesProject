package com.threetimes.backend.common;

import java.util.UUID;

/**
 * Cine face cererea: amprenta telefonului (X-Device-Id, poate lipsi la citire) și, dacă e autentificat,
 * contul. O urmă lăsată cu un cont e doar a acelui cont (de pe orice telefon); una lăsată fără cont e a telefonului
 * care a lăsat-o (decis 26 nov: alt cont deschis pe același telefon nu vede urmele primului ca fiind ale lui).
 */
public record Viewer(String device, UUID user) {

    public static Viewer anonymous(String device) {
        return new Viewer(device, null);
    }

    public boolean owns(String ownerHash, UUID traceUser) {
        if (traceUser != null) return traceUser.equals(user);
        return device != null && device.equals(ownerHash);
    }

    /**
     * Aceeași regulă în SQL: coloanele contului și telefonului, parametrii telefonului și contului (fără „:”).
     * De ex. owned("t.user_id", "t.owner_hash", "device", "user").
     */
    public static String owned(String userColumn, String deviceColumn, String deviceParam, String userParam) {
        return ("COALESCE(CASE WHEN %1$s IS NULL THEN %2$s = :%3$s ELSE %1$s = CAST(:%4$s AS uuid) END, FALSE)")
                .formatted(userColumn, deviceColumn, deviceParam, userParam);
    }
}
