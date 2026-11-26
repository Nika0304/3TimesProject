package com.threetimes.backend.notification;

import java.util.List;
import java.util.UUID;

import org.springframework.http.HttpHeaders;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

import com.threetimes.backend.account.AccountService;
import com.threetimes.backend.common.DeviceIds;
import com.threetimes.backend.common.Viewer;
import com.threetimes.backend.notification.NotificationRepository.Notification;

/**
 * Notificările din aplicație (Meniu → Notificări):
 * <pre>
 * GET  /api/v1/notifications              → {"notifications": [...], "unread": N} (cele mai noi primele, max 50)
 * POST /api/v1/notifications/{id}/read    → 204, citită
 * POST /api/v1/notifications/read-all     → 204, toate citite
 * </pre>
 * Ale contului (cu „Authorization: Bearer ...”) sau, fără cont, ale telefonului (X-Device-Id).
 */
@RestController
@RequestMapping("/api/v1/notifications")
public class NotificationController {

    public record NotificationList(List<Notification> notifications, int unread) {
    }

    private final NotificationRepository repository;
    private final DeviceIds devices;
    private final AccountService accounts;

    public NotificationController(NotificationRepository repository, DeviceIds devices, AccountService accounts) {
        this.repository = repository;
        this.devices = devices;
        this.accounts = accounts;
    }

    @GetMapping
    public NotificationList list(@RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                 @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        var items = repository.of(viewer(device, auth));
        int unread = (int) items.stream().filter(n -> !n.read()).count();
        return new NotificationList(items, unread);
    }

    @PostMapping("/{id}/read")
    public ResponseEntity<Void> read(@PathVariable UUID id,
                                     @RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                     @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        repository.markRead(viewer(device, auth), id);
        return ResponseEntity.noContent().build();
    }

    @PostMapping("/read-all")
    public ResponseEntity<Void> readAll(@RequestHeader(value = DeviceIds.HEADER, required = false) String device,
                                        @RequestHeader(value = HttpHeaders.AUTHORIZATION, required = false) String auth) {
        repository.markRead(viewer(device, auth), null);
        return ResponseEntity.noContent().build();
    }

    private Viewer viewer(String device, String auth) {
        return new Viewer(devices.require(device), accounts.userOf(auth));
    }
}
