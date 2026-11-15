package com.threetimes.backend.common;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.stereotype.Component;

/**
 * Telefonul se identifică prin antetul X-Device-Id: un șir aleatoriu generat de aplicație la prima
 * pornire. Nu există cont. În bază păstrăm doar amprenta lui (SHA-256 cu sare), nu șirul în sine.
 */
@Component
public class DeviceIds {

    public static final String HEADER = "X-Device-Id";

    private final String salt;

    public DeviceIds(@Value("${threetimes.device-salt}") String salt) {
        this.salt = salt;
    }

    /** Amprenta telefonului, sau excepție dacă lipsește ori arată ciudat. */
    public String require(String deviceId) {
        if (deviceId == null || deviceId.isBlank()) {
            throw ApiException.badRequest("Lipsește antetul " + HEADER + ".");
        }
        String id = deviceId.trim();
        if (id.length() < 16 || id.length() > 128) {
            throw ApiException.badRequest("Antetul " + HEADER + " nu e valid.");
        }
        return hash(id);
    }

    /** La fel, dar pentru cereri care merg și fără telefon cunoscut (întoarce null). */
    public String optional(String deviceId) {
        return deviceId == null || deviceId.isBlank() ? null : require(deviceId);
    }

    private String hash(String id) {
        try {
            MessageDigest sha = MessageDigest.getInstance("SHA-256");
            byte[] digest = sha.digest((salt + ":" + id).getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(digest);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
