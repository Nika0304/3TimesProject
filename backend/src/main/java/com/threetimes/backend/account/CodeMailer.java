package com.threetimes.backend.account;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.http.HttpStatus;
import org.springframework.mail.MailException;
import org.springframework.mail.SimpleMailMessage;
import org.springframework.mail.javamail.JavaMailSender;
import org.springframework.stereotype.Service;

import com.threetimes.backend.common.ApiException;

/**
 * Trimite codurile de 6 cifre pe email (verificarea contului, parola nouă), prin Gmail-ul aplicației (SMTP).
 * Adresa și „parola de aplicație” se pun în backend/.env (MAIL_USERNAME, MAIL_PASSWORD), care nu intră în repo.
 * Fără ele, codul se scrie doar în logul serverului (docker compose logs api), ca să se poată testa local.
 */
@Service
public class CodeMailer {

    private static final Logger log = LoggerFactory.getLogger(CodeMailer.class);

    private final ObjectProvider<JavaMailSender> sender;
    private final String username;
    private final String password;

    public CodeMailer(ObjectProvider<JavaMailSender> sender,
                      @Value("${spring.mail.username:}") String username,
                      @Value("${spring.mail.password:}") String password) {
        this.sender = sender;
        this.username = username == null ? "" : username.trim();
        this.password = password == null ? "" : password.trim();
    }

    public boolean isConfigured() {
        return !username.isEmpty() && !password.isEmpty();
    }

    public void send(String to, String firstName, String code, EmailCodeService.Purpose purpose) {
        boolean reset = purpose == EmailCodeService.Purpose.RESET;
        if (!isConfigured()) {
            log.warn("[cont] Emailul nu e configurat (MAIL_USERNAME / MAIL_PASSWORD în backend/.env). "
                    + "Codul {} pentru {} este: {}", reset ? "de resetare" : "de verificare", to, code);
            return;
        }
        String hello = firstName == null || firstName.isBlank() ? "Bună," : "Bună, " + firstName + ",";
        var message = new SimpleMailMessage();
        message.setFrom("3Times <" + username + ">");
        message.setTo(to);
        message.setSubject(reset ? code + " e codul pentru parola nouă 3Times" : code + " e codul tău 3Times");
        message.setText(hello + "\n\n"
                + (reset ? "Ai cerut o parolă nouă pentru contul 3Times. Codul tău este:\n\n"
                         : "Mulțumim că ți-ai făcut cont în 3Times. Ca să-l activezi, scrie în aplicație codul:\n\n")
                + "    " + code + "\n\n"
                + "Codul e valabil " + EmailCodeService.VALID_MINUTES + " minute.\n"
                + (reset ? "Dacă n-ai cerut tu o parolă nouă, ignoră emailul: parola ta rămâne aceeași.\n"
                         : "Dacă nu tu ți-ai făcut contul, ignoră emailul: fără cod, contul nu se poate folosi.\n")
                + "\nEchipa 3Times");
        try {
            sender.getObject().send(message);
        } catch (MailException | IllegalStateException e) {
            log.error("[cont] Nu am putut trimite emailul către {}: {}", to, e.getMessage());
            throw new ApiException(HttpStatus.SERVICE_UNAVAILABLE,
                    "Nu am putut trimite emailul acum. Încearcă din nou peste puțin.");
        }
    }
}
