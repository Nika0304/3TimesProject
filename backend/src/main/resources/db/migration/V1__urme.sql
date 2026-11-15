-- 3Times, migrarea 1: urmele din modul Prezent.
CREATE EXTENSION IF NOT EXISTS postgis;

-- O urmă: mesajul, emoția și punctul de pe glob în care a fost lăsată.
CREATE TABLE trace (
    id                    UUID PRIMARY KEY,
    type                  VARCHAR(10)  NOT NULL CHECK (type IN ('TEXT', 'PHOTO', 'VOICE')),
    content               VARCHAR(280) NOT NULL DEFAULT '',
    emotion               VARCHAR(10)  NOT NULL CHECK (emotion IN ('JOY', 'SADNESS', 'LONGING')),
    intensity             REAL         NOT NULL CHECK (intensity >= 0 AND intensity <= 1),
    location              GEOGRAPHY(POINT, 4326) NOT NULL,
    altitude              DOUBLE PRECISION NOT NULL,           -- WGS84, rezervă dacă ancora pe teren eșuează
    height_above_terrain  REAL         NOT NULL DEFAULT 1.2,   -- înălțimea față de sol, în metri
    accuracy_meters       REAL         NOT NULL,               -- precizia orizontală la plasare
    yaw_accuracy_degrees  REAL         NOT NULL DEFAULT 0,     -- precizia orientării la plasare
    photo_source          VARCHAR(20)  CHECK (photo_source IN ('SELFIE', 'PLACE', 'PLACE_WITH_TRACES')),
    duration_seconds      REAL         NOT NULL DEFAULT 0,     -- doar la voce
    owner_hash            CHAR(64)     NOT NULL,               -- amprenta telefonului care a lăsat urma
    created_at            TIMESTAMPTZ  NOT NULL DEFAULT now(),
    report_count          INT          NOT NULL DEFAULT 0,
    hidden                BOOLEAN      NOT NULL DEFAULT FALSE  -- ascunsă după prea multe raportări
);

CREATE INDEX trace_location_idx ON trace USING GIST (location);
CREATE INDEX trace_created_idx ON trace (created_at);

-- Poza (JPEG) sau vocea (WAV) unei urme. Ținute în bază, ca să meargă la fel local și online.
CREATE TABLE trace_media (
    trace_id      UUID PRIMARY KEY REFERENCES trace (id) ON DELETE CASCADE,
    content_type  VARCHAR(40) NOT NULL,
    size_bytes    INT         NOT NULL,
    data          BYTEA       NOT NULL
);

-- Raportările: un telefon poate raporta o urmă o singură dată.
CREATE TABLE trace_report (
    trace_id       UUID        NOT NULL REFERENCES trace (id) ON DELETE CASCADE,
    reporter_hash  CHAR(64)    NOT NULL,
    reason         VARCHAR(200),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (trace_id, reporter_hash)
);
