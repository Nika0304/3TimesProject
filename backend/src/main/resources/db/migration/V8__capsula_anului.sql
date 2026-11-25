-- 3Times, migrarea 8: capsula anului, făcută de aplicație.
-- Urmele lăsate într-un oraș într-un an, cu bifa „Pune-o și în capsula anului”, se strâng într-o sferă mare
-- (Timișoara: Piața Victoriei), care se deschide pe 1 ianuarie anul următor, la miezul nopții (ora României).
-- Urmele de dinainte n-au avut bifa, deci nu intră (fără acordul celui care le-a lăsat).
ALTER TABLE trace ADD COLUMN year_capsule BOOLEAN NOT NULL DEFAULT FALSE;
CREATE INDEX trace_year_capsule_idx ON trace (created_at) WHERE year_capsule;

CREATE TABLE year_capsule (
    id                    UUID PRIMARY KEY,
    city                  VARCHAR(60)  NOT NULL,
    year                  INT          NOT NULL,
    location              GEOGRAPHY(POINT, 4326) NOT NULL,   -- unde stă sfera
    altitude              DOUBLE PRECISION NOT NULL DEFAULT 0,
    height_above_terrain  REAL         NOT NULL DEFAULT 2.0, -- sfera mare plutește mai sus
    min_lat               DOUBLE PRECISION NOT NULL,         -- orașul: urmele din acest dreptunghi
    max_lat               DOUBLE PRECISION NOT NULL,
    min_lng               DOUBLE PRECISION NOT NULL,
    max_lng               DOUBLE PRECISION NOT NULL,
    UNIQUE (city, year)
);

-- Timișoara: sfera în Piața Victoriei, între Operă și Catedrală; orașul = același dreptunghi ca pe hartă.
INSERT INTO year_capsule (id, city, year, location, min_lat, max_lat, min_lng, max_lng)
SELECT gen_random_uuid(), 'Timișoara', y, ST_SetSRID(ST_MakePoint(21.2253, 45.7527), 4326)::geography,
       45.70, 45.81, 21.14, 21.33
FROM generate_series(2026, 2035) AS y;
