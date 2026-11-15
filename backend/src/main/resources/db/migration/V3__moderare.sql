-- 3Times, migrarea 3: moderarea.
-- moderation: 'ok' = AI-ul a zis că e în regulă; 'review' = la limită sau AI-ul n-a răspuns (urma se vede, dar apare
-- la admin); 'approved' = adminul a verificat-o și a păstrat-o. Urmele refuzate de AI nu ajung în bază.
ALTER TABLE trace
    ADD COLUMN moderation        VARCHAR(10)  NOT NULL DEFAULT 'ok'
        CHECK (moderation IN ('ok', 'review', 'approved')),
    ADD COLUMN moderation_reason VARCHAR(200);

-- Raportările pe care adminul le-a închis („Păstrează”): nu mai contează și nu mai apar la admin.
ALTER TABLE trace_report ADD COLUMN dismissed BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX trace_moderation_idx ON trace (moderation) WHERE moderation = 'review';
