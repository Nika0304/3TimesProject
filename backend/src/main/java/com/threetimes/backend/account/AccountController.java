package com.threetimes.backend.account;

import java.util.UUID;

import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.account.AccountDtos.AccountView;
import com.threetimes.backend.account.AccountDtos.AuthResponse;
import com.threetimes.backend.account.AccountDtos.Credentials;
import com.threetimes.backend.account.AccountDtos.EmailRequest;
import com.threetimes.backend.account.AccountDtos.RegisterRequest;
import com.threetimes.backend.account.AccountDtos.ResetRequest;
import com.threetimes.backend.account.AccountDtos.VerifyRequest;
import com.threetimes.backend.common.DeviceIds;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.trace.TraceDtos.MyTracesResponse;
import com.threetimes.backend.trace.TraceService;

/**
 * Conturile (opționale):
 * <pre>
 * POST   /api/v1/account/register  {"firstName", "lastName", "email", "password"}
 *                                   → 201 {"verificationRequired": true, "email"} (codul pleacă pe email)
 * POST   /api/v1/account/verify    {"email", "code"}       → {"token", "account"} (contul e activ)
 * POST   /api/v1/account/verify/resend {"email"}           → 204, alt cod (după cel puțin un minut)
 * POST   /api/v1/account/login     {"email", "password"}   → {"token", "account"}, sau {"verificationRequired": true}
 * POST   /api/v1/account/password/forgot {"email"}         → 204, cod pe email dacă emailul are cont
 * POST   /api/v1/account/password/reset  {"email", "code", "password"} → {"token", "account"}
 * GET    /api/v1/account                                    → contul (email, câte urme are…)
 * GET    /api/v1/account/traces                             → urmele contului, pentru „Harta mea”
 * POST   /api/v1/account/logout                             → 204
 * DELETE /api/v1/account                                    → 204, șterge contul și urmele lui
 * </pre>
 * Tokenul primit se trimite la fiecare cerere ca „Authorization: Bearer ...”. La înregistrare și autentificare,
 * urmele lăsate fără cont de pe telefonul acesta (X-Device-Id) trec în cont.
 */
@RestController
@RequestMapping("/api/v1/account")
public class AccountController {

    private final AccountService accounts;
    private final DeviceIds devices;
    private final TraceService traces;

    public AccountController(AccountService accounts, DeviceIds devices, TraceService traces) {
        this.accounts = accounts;
        this.devices = devices;
        this.traces = traces;
    }

    @PostMapping("/register")
    public ResponseEntity<AuthResponse> register(@RequestBody(required = false) RegisterRequest body,
                                                 @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        return ResponseEntity.status(HttpStatus.CREATED).body(accounts.register(body, devices.optional(device)));
    }

    @PostMapping("/verify")
    public AuthResponse verify(@RequestBody(required = false) VerifyRequest body,
                               @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        return accounts.verify(body, devices.optional(device));
    }

    @PostMapping("/verify/resend")
    public ResponseEntity<Void> resend(@RequestBody(required = false) EmailRequest body) {
        accounts.resendVerification(body);
        return ResponseEntity.noContent().build();
    }

    @PostMapping("/password/forgot")
    public ResponseEntity<Void> forgot(@RequestBody(required = false) EmailRequest body) {
        accounts.forgotPassword(body);
        return ResponseEntity.noContent().build();
    }

    @PostMapping("/password/reset")
    public AuthResponse reset(@RequestBody(required = false) ResetRequest body,
                              @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        return accounts.resetPassword(body, devices.optional(device));
    }

    @PostMapping("/login")
    public AuthResponse login(@RequestBody(required = false) Credentials body,
                              @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        return accounts.login(body, devices.optional(device));
    }

    @GetMapping
    public AccountView me(@RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        return accounts.view(accounts.requireUser(auth));
    }

    @GetMapping("/traces")
    public MyTracesResponse myTraces(@RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth,
                                     @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        UUID user = accounts.requireUser(auth);
        return new MyTracesResponse(traces.ofUser(new Viewer(devices.optional(device), user)));
    }

    @PostMapping("/logout")
    public ResponseEntity<Void> logout(@RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        accounts.logout(auth);
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping
    public ResponseEntity<Void> delete(@RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth,
                                       @RequestHeader(value = DeviceIds.HEADER, required = false) String device) {
        accounts.delete(accounts.requireUser(auth), devices.optional(device));
        return ResponseEntity.noContent().build();
    }
}
