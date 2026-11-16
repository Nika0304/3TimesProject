package com.threetimes.backend.common;

import java.util.UUID;

/**
 * Cine face cererea: amprenta telefonului (X-Device-Id, poate lipsi la citire) și, dacă e autentificat,
 * contul. O urmă e „a mea” dacă a lăsat-o telefonul acesta sau dacă e în contul meu (de pe orice telefon).
 */
public record Viewer(String device, UUID user) {

    public static Viewer anonymous(String device) {
        return new Viewer(device, null);
    }

    public boolean owns(String ownerHash, UUID traceUser) {
        return (device != null && device.equals(ownerHash)) || (user != null && user.equals(traceUser));
    }
}
