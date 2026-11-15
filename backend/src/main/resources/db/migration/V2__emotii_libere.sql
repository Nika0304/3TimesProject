-- 3Times, migrarea 2: emoțiile nu mai sunt doar trei. Numele emoției e liber (propus de AI după text, poate fi
-- schimbat de utilizator), iar forma e aleasă tot de AI, dintr-un set de forme de bază, cu parametri.
-- Urmele vechi păstrează JOY / SADNESS / LONGING și nu au formă: aplicația le desenează ca înainte.

ALTER TABLE trace DROP CONSTRAINT IF EXISTS trace_emotion_check;
ALTER TABLE trace ALTER COLUMN emotion TYPE VARCHAR(40);

ALTER TABLE trace ADD COLUMN shape         VARCHAR(12);
ALTER TABLE trace ADD COLUMN color         VARCHAR(7);
ALTER TABLE trace ADD COLUMN accent_color  VARCHAR(7);
ALTER TABLE trace ADD COLUMN petals        SMALLINT;
ALTER TABLE trace ADD COLUMN sharpness     REAL;
ALTER TABLE trace ADD COLUMN motion        VARCHAR(12);
