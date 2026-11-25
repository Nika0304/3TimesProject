-- 3Times, migrarea 4: conturile (opționale).
-- Un cont se face cu email + parolă sau cu Google (mai târziu). Fără cont, telefonul rămâne identificat doar prin
-- amprenta X-Device-Id, ca până acum.
CREATE TABLE app_user (
    id             UUID PRIMARY KEY,
    email          VARCHAR(254),                -- mereu cu litere mici
    password_hash  VARCHAR(100),                -- bcrypt; gol la conturile făcute doar cu Google
    google_sub     VARCHAR(64),                 -- id-ul contului Google; gol la conturile cu parolă
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (password_hash IS NOT NULL OR google_sub IS NOT NULL)
);
CREATE UNIQUE INDEX app_user_email_idx ON app_user (email) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX app_user_google_idx ON app_user (google_sub) WHERE google_sub IS NOT NULL;

-- Sesiunile: aplicația păstrează un token aleatoriu, în bază stă doar amprenta lui (SHA-256).
-- Ștergerea contului le șterge și pe ele.
CREATE TABLE user_session (
    token_hash  CHAR(64) PRIMARY KEY,
    user_id     UUID        NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX user_session_user_idx ON user_session (user_id);

-- Urma poate fi și a unui cont (lăsată cu contul deschis, sau lăsată înainte de pe același telefon și mutată în cont
-- la autentificare). Urmele contului se șterg odată cu el.
ALTER TABLE trace ADD COLUMN user_id UUID REFERENCES app_user (id) ON DELETE CASCADE;
CREATE INDEX trace_user_idx ON trace (user_id) WHERE user_id IS NOT NULL;
CREATE INDEX trace_owner_idx ON trace (owner_hash);
