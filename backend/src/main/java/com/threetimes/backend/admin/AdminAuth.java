package com.threetimes.backend.admin;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.Base64;

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
 * Pagina de admin (/admin) și API-ul ei (/api/v1/admin/...) cer parola din ADMIN_PASSWORD (backend/.env).
 * Folosim autentificarea simplă a browserului (HTTP Basic): browserul cere parola o dată și o ține minte.
 * Numele de utilizator nu contează. Fără ADMIN_PASSWORD, adminul e oprit.
 * Atenție: pe hotspot serverul e HTTP simplu, deci parola trece necriptată prin rețeaua locală.
 */
@Configuration
public class AdminAuth {

    @Bean
    public FilterRegistrationBean<OncePerRequestFilter> adminFilter(@Value("${threetimes.admin-password:}") String password) {
        byte[] expected = password == null ? new byte[0] : password.trim().getBytes(StandardCharsets.UTF_8);
        var filter = new OncePerRequestFilter() {
            @Override
            protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
                    throws ServletException, IOException {
                if (expected.length == 0) {
                    send(response, 503, "Adminul e oprit: pune ADMIN_PASSWORD în backend/.env și repornește serverul.");
                    return;
                }
                if (!matches(request.getHeader("Authorization"), expected)) {
                    response.setHeader("WWW-Authenticate", "Basic realm=\"3Times admin\", charset=\"UTF-8\"");
                    send(response, 401, "Parola de admin lipsește sau e greșită.");
                    return;
                }
                chain.doFilter(request, response);
            }
        };
        var registration = new FilterRegistrationBean<OncePerRequestFilter>(filter);
        registration.addUrlPatterns("/admin", "/admin/*", "/api/v1/admin/*");
        registration.setName("adminAuth");
        return registration;
    }

    private static boolean matches(String header, byte[] expected) {
        if (header == null || !header.regionMatches(true, 0, "Basic ", 0, 6)) return false;
        try {
            String decoded = new String(Base64.getDecoder().decode(header.substring(6).trim()), StandardCharsets.UTF_8);
            int colon = decoded.indexOf(':');
            byte[] given = (colon < 0 ? decoded : decoded.substring(colon + 1)).getBytes(StandardCharsets.UTF_8);
            return MessageDigest.isEqual(given, expected);
        } catch (IllegalArgumentException e) {
            return false;
        }
    }

    private static void send(HttpServletResponse response, int status, String message) throws IOException {
        response.setStatus(status);
        response.setContentType("application/json;charset=UTF-8");
        response.getWriter().write("{\"error\":\"" + message.replace("\"", "'") + "\"}");
    }
}
