-- 3Times, migrarea 7: o capsulă conține mai multe urme (cel mult 10).
-- Capsula publică e „de grup”: oricine ajunge la ea își poate pune urma, până o sigilează cine a creat-o
-- (sau singură: după 7 zile, la 10 urme ori la data deschiderii). Cea personală o umple doar contul ei.
-- Capsulele de test din V6 (o singură urmă) se șterg: forma lor nu se mai potrivește.
DROP TABLE IF EXISTS capsule_report;
DROP TABLE IF EXISTS capsule_open;
DROP TABLE IF EXISTS capsule_media;
DROP TABLE IF EXISTS capsule;

-- Capsula: locul, data deschiderii, cine a creat-o și dacă mai primește urme.
CREATE TABLE capsule (
    id                    UUID PRIMARY KEY,
    location              GEOGRAPHY(POINT, 4326) NOT NULL,
    altitude              DOUBLE PRECISION NOT NULL,
    height_above_terrain  REAL         NOT NULL DEFAULT 1.2,
    accuracy_meters       REAL         NOT NULL,
    yaw_accuracy_degrees  REAL         NOT NULL DEFAULT 0,
    visibility            VARCHAR(10)  NOT NULL DEFAULT 'PUBLIC' CHECK (visibility IN ('PUBLIC', 'PERSONAL')),
    unlock_at             TIMESTAMPTZ  NOT NULL,
    owner_hash            CHAR(64)     NOT NULL,
    user_id               UUID REFERENCES app_user (id) ON DELETE CASCADE,
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    sealed_at             TIMESTAMPTZ,              -- sigilată de creator; până atunci primește urme
    CHECK (visibility = 'PUBLIC' OR user_id IS NOT NULL)
);
CREATE INDEX capsule_location_idx ON capsule USING GIST (location);
CREATE INDEX capsule_owner_idx ON capsule (owner_hash);
CREATE INDEX capsule_user_idx ON capsule (user_id) WHERE user_id IS NOT NULL;

-- O urmă din capsulă: ca o urmă obișnuită (text, poză, voce, emoția și forma ei), cu locul unde a fost pusă.
CREATE TABLE capsule_item (
    id                 UUID PRIMARY KEY,
    capsule_id         UUID         NOT NULL REFERENCES capsule (id) ON DELETE CASCADE,
    type               VARCHAR(10)  NOT NULL CHECK (type IN ('TEXT', 'PHOTO', 'VOICE')),
    content            VARCHAR(280) NOT NULL DEFAULT '',
    emotion            VARCHAR(40)  NOT NULL,
    shape              VARCHAR(12),
    color              VARCHAR(7),
    accent_color       VARCHAR(7),
    petals             SMALLINT,
    sharpness          REAL,
    motion             VARCHAR(12),
    intensity          REAL         NOT NULL CHECK (intensity >= 0 AND intensity <= 1),
    location           GEOGRAPHY(POINT, 4326) NOT NULL,
    photo_source       VARCHAR(20)  CHECK (photo_source IN ('SELFIE', 'PLACE', 'PLACE_WITH_TRACES')),
    duration_seconds   REAL         NOT NULL DEFAULT 0,
    owner_hash         CHAR(64)     NOT NULL,
    user_id            UUID REFERENCES app_user (id) ON DELETE CASCADE,
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT now(),
    moderation         VARCHAR(10)  NOT NULL DEFAULT 'ok' CHECK (moderation IN ('ok', 'review', 'approved')),
    moderation_reason  VARCHAR(200),
    report_count       INT          NOT NULL DEFAULT 0,
    hidden             BOOLEAN      NOT NULL DEFAULT FALSE
);
CREATE INDEX capsule_item_capsule_idx ON capsule_item (capsule_id);
CREATE INDEX capsule_item_owner_idx ON capsule_item (owner_hash);

CREATE TABLE capsule_item_media (
    item_id       UUID PRIMARY KEY REFERENCES capsule_item (id) ON DELETE CASCADE,
    content_type  VARCHAR(40) NOT NULL,
    size_bytes    INT         NOT NULL,
    data          BYTEA       NOT NULL
);

CREATE TABLE capsule_item_report (
    item_id        UUID        NOT NULL REFERENCES capsule_item (id) ON DELETE CASCADE,
    reporter_hash  CHAR(64)    NOT NULL,
    reason         VARCHAR(200),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    dismissed      BOOLEAN     NOT NULL DEFAULT FALSE,
    PRIMARY KEY (item_id, reporter_hash)
);

-- Cine a deschis capsula (telefon + cont): pentru el, urmele din ea au devenit urme obișnuite.
CREATE TABLE capsule_open (
    capsule_id   UUID        NOT NULL REFERENCES capsule (id) ON DELETE CASCADE,
    device_hash  CHAR(64)    NOT NULL,
    user_id      UUID REFERENCES app_user (id) ON DELETE CASCADE,
    opened_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (capsule_id, device_hash)
);
CREATE INDEX capsule_open_user_idx ON capsule_open (user_id) WHERE user_id IS NOT NULL;
