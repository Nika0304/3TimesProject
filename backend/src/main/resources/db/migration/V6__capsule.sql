-- 3Times, migrarea 6: capsulele timpului (modul Viitor).
-- O capsulă e ca o urmă (text, poză sau voce, cu emoția și forma ei), dar se deschide doar în locul ei
-- și doar după unlock_at. Conținutul nu pleacă de pe server înainte ca telefonul s-o deschidă (verificare aici).
CREATE TABLE capsule (
    id                    UUID PRIMARY KEY,
    type                  VARCHAR(10)  NOT NULL CHECK (type IN ('TEXT', 'PHOTO', 'VOICE')),
    content               VARCHAR(280) NOT NULL DEFAULT '',
    emotion               VARCHAR(40)  NOT NULL,
    shape                 VARCHAR(12),
    color                 VARCHAR(7),
    accent_color          VARCHAR(7),
    petals                SMALLINT,
    sharpness             REAL,
    motion                VARCHAR(12),
    intensity             REAL         NOT NULL CHECK (intensity >= 0 AND intensity <= 1),
    location              GEOGRAPHY(POINT, 4326) NOT NULL,
    altitude              DOUBLE PRECISION NOT NULL,
    height_above_terrain  REAL         NOT NULL DEFAULT 1.2,
    accuracy_meters       REAL         NOT NULL,
    yaw_accuracy_degrees  REAL         NOT NULL DEFAULT 0,
    photo_source          VARCHAR(20)  CHECK (photo_source IN ('SELFIE', 'PLACE', 'PLACE_WITH_TRACES')),
    duration_seconds      REAL         NOT NULL DEFAULT 0,
    visibility            VARCHAR(10)  NOT NULL DEFAULT 'PUBLIC' CHECK (visibility IN ('PUBLIC', 'PERSONAL')),
    unlock_at             TIMESTAMPTZ  NOT NULL,
    owner_hash            CHAR(64)     NOT NULL,
    user_id               UUID REFERENCES app_user (id) ON DELETE CASCADE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    moderation            VARCHAR(10)  NOT NULL DEFAULT 'ok' CHECK (moderation IN ('ok', 'review', 'approved')),
    moderation_reason     VARCHAR(200),
    report_count          INT          NOT NULL DEFAULT 0,
    hidden                BOOLEAN      NOT NULL DEFAULT FALSE,
    CHECK (visibility = 'PUBLIC' OR user_id IS NOT NULL)   -- personală = doar cu cont
);
CREATE INDEX capsule_location_idx ON capsule USING GIST (location);
CREATE INDEX capsule_owner_idx ON capsule (owner_hash);
CREATE INDEX capsule_user_idx ON capsule (user_id) WHERE user_id IS NOT NULL;

CREATE TABLE capsule_media (
    capsule_id    UUID PRIMARY KEY REFERENCES capsule (id) ON DELETE CASCADE,
    content_type  VARCHAR(40) NOT NULL,
    size_bytes    INT         NOT NULL,
    data          BYTEA       NOT NULL
);

-- Cine a deschis capsula: fiecare telefon (și contul, dacă era deschis) o deschide singur, o dată.
-- După deschidere, pentru el capsula devine o urmă (o vede cu emoția ei și o poate redeschide).
CREATE TABLE capsule_open (
    capsule_id   UUID        NOT NULL REFERENCES capsule (id) ON DELETE CASCADE,
    device_hash  CHAR(64)    NOT NULL,
    user_id      UUID REFERENCES app_user (id) ON DELETE CASCADE,
    opened_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (capsule_id, device_hash)
);
CREATE INDEX capsule_open_user_idx ON capsule_open (user_id) WHERE user_id IS NOT NULL;

CREATE TABLE capsule_report (
    capsule_id     UUID        NOT NULL REFERENCES capsule (id) ON DELETE CASCADE,
    reporter_hash  CHAR(64)    NOT NULL,
    reason         VARCHAR(200),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    dismissed      BOOLEAN     NOT NULL DEFAULT FALSE,
    PRIMARY KEY (capsule_id, reporter_hash)
);
