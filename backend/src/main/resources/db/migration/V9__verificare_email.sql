-- 3Times, migrarea 9: verificarea emailului și resetarea parolei (26 nov).
-- Un cont nou cu email + parolă nu se poate folosi până nu se scrie codul de 6 cifre trimis pe email.
-- Conturile de până acum (de test) rămân folosibile. Conturile Google vin deja verificate.
ALTER TABLE app_user ADD COLUMN email_verified BOOLEAN NOT NULL DEFAULT FALSE;
UPDATE app_user SET email_verified = TRUE;

-- Codurile trimise pe email: cel mult unul pe cont și scop (cel nou îl înlocuiește pe cel vechi).
-- În bază stă doar amprenta codului (SHA-256). Codul ține 15 minute și are 5 încercări.
CREATE TABLE email_code (
    user_id     UUID        NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,
    purpose     VARCHAR(10) NOT NULL CHECK (purpose IN ('verify', 'reset')),
    code_hash   CHAR(64)    NOT NULL,
    expires_at  TIMESTAMPTZ NOT NULL,
    attempts    INT         NOT NULL DEFAULT 0,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, purpose)
);
