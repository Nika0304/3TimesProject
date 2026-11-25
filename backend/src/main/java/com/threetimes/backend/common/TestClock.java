package com.threetimes.backend.common;

import java.io.IOException;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeParseException;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.filter.OncePerRequestFilter;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;

/**
 * Ceasul de test: aplicația (Meniu → Jurnal de test) poate trimite antetul X-Test-Now, ca să vedem acum cum arată
 * peste un an (urmele la aniversare, capsula anului deschisă). Serverul îl folosește DOAR dacă e pornit cu
 * TEST_CLOCK=true (implicit în docker-compose, pe serverul local); altfel îl ignoră și folosește ora reală.
 * Ora contează doar pentru ce se vede și ce se poate deschide; datele salvate (created_at) au mereu ora reală.
 */
@Configuration
public class TestClock {

    public static final String HEADER = "X-Test-Now";
    private static final ThreadLocal<Instant> NOW = new ThreadLocal<>();

    /** Ora cererii: cea de test (dacă e permisă și trimisă), altfel ora reală. */
    public static Instant now() {
        Instant test = NOW.get();
        return test != null ? test : Instant.now();
    }

    /** Pentru parametrul :now din SQL. */
    public static OffsetDateTime nowForSql() {
        return OffsetDateTime.ofInstant(now(), ZoneOffset.UTC);
    }

    @Bean
    public FilterRegistrationBean<OncePerRequestFilter> testClockFilter(@Value("${threetimes.test-clock:false}") boolean enabled) {
        var filter = new OncePerRequestFilter() {
            @Override
            protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
                    throws ServletException, IOException {
                String header = enabled ? request.getHeader(HEADER) : null;
                if (header != null && !header.isBlank()) {
                    try {
                        NOW.set(Instant.parse(header.trim()));
                    } catch (DateTimeParseException ignored) {
                        // antet greșit: ora reală
                    }
                }
                try {
                    chain.doFilter(request, response);
                } finally {
                    NOW.remove();
                }
            }
        };
        var registration = new FilterRegistrationBean<OncePerRequestFilter>(filter);
        registration.addUrlPatterns("/api/*");
        registration.setName("testClock");
        return registration;
    }
}
