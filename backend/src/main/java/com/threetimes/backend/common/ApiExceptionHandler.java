package com.threetimes.backend.common;

import java.util.Map;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.ErrorResponse;
import org.springframework.web.bind.MissingRequestHeaderException;
import org.springframework.web.bind.MissingServletRequestParameterException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.method.annotation.MethodArgumentTypeMismatchException;
import org.springframework.web.multipart.MaxUploadSizeExceededException;
import org.springframework.web.multipart.support.MissingServletRequestPartException;

/** Toate erorile ies ca JSON: {"error": "mesaj"}, ușor de afișat în aplicație. */
@RestControllerAdvice
public class ApiExceptionHandler {

    private static final Logger log = LoggerFactory.getLogger(ApiExceptionHandler.class);

    @ExceptionHandler(ApiException.class)
    public ResponseEntity<Map<String, String>> api(ApiException e) {
        return body(e.getStatus(), e.getMessage());
    }

    @ExceptionHandler(MaxUploadSizeExceededException.class)
    public ResponseEntity<Map<String, String>> tooBig(MaxUploadSizeExceededException e) {
        return body(HttpStatus.PAYLOAD_TOO_LARGE, "Fișierul e prea mare.");
    }

    @ExceptionHandler({
            MissingRequestHeaderException.class,
            MissingServletRequestParameterException.class,
            MissingServletRequestPartException.class,
            MethodArgumentTypeMismatchException.class
    })
    public ResponseEntity<Map<String, String>> badInput(Exception e) {
        return body(HttpStatus.BAD_REQUEST, "Cerere incompletă: " + e.getMessage());
    }

    /** Corpul cererii nu e JSON valid (sau nu e UTF-8). */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<Map<String, String>> unreadable(HttpMessageNotReadableException e) {
        log.debug("Corp ilizibil: {}", e.getMessage());
        return body(HttpStatus.BAD_REQUEST, "Cererea nu e un JSON valid.");
    }

    @ExceptionHandler(Exception.class)
    public ResponseEntity<Map<String, String>> unexpected(Exception e) {
        // Erorile obișnuite ale Spring (adresă inexistentă, de ex. /favicon.ico cerut de browser,
        // metodă greșită, tip de conținut greșit) își păstrează codul (404, 405, 415) și nu se notează ca erori.
        if (e instanceof ErrorResponse known) {
            HttpStatus status = HttpStatus.resolve(known.getStatusCode().value());
            if (status == null) status = HttpStatus.BAD_REQUEST;
            log.debug("Cerere respinsă ({}): {}", status.value(), e.getMessage());
            return body(status, status == HttpStatus.NOT_FOUND ? "Adresa nu există." : "Cerere nepotrivită: " + e.getMessage());
        }
        log.error("Eroare neașteptată", e);
        return body(HttpStatus.INTERNAL_SERVER_ERROR, "Eroare pe server. Mai încearcă.");
    }

    private static ResponseEntity<Map<String, String>> body(HttpStatus status, String message) {
        return ResponseEntity.status(status).body(Map.of("error", message));
    }
}
