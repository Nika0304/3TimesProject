-- 3Times, migrarea 11 (27 nov): notificările din aplicație (Meniu → Notificări).
-- Deocamdată: „urma ta a fost ștearsă de moderatori”, cu motivul ales de admin.
-- O notificare e a contului (user_id) sau, pentru urmele lăsate fără cont, a telefonului (device_hash).
-- La autentificare, cele ale telefonului trec în cont; ștergerea contului le șterge.
CREATE TABLE notification (
    id           UUID PRIMARY KEY,
    user_id      UUID REFERENCES app_user (id) ON DELETE CASCADE,
    device_hash  CHAR(64),
    kind         VARCHAR(20)  NOT NULL,      -- TRACE_REMOVED
    title        VARCHAR(120) NOT NULL,
    body         TEXT         NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    read_at      TIMESTAMPTZ,
    CHECK (user_id IS NOT NULL OR device_hash IS NOT NULL)
);
CREATE INDEX notification_user_idx ON notification (user_id, created_at DESC) WHERE user_id IS NOT NULL;
CREATE INDEX notification_device_idx ON notification (device_hash, created_at DESC) WHERE user_id IS NULL;
