-- 3Times, migrarea 10 (26 nov): o capsulă deschisă dintr-un cont e deschisă doar pentru acel cont, nu pentru tot
-- telefonul. Pe același telefon pot fi deci mai multe deschideri: una fără cont și câte una pentru fiecare cont.
ALTER TABLE capsule_open DROP CONSTRAINT capsule_open_pkey;
CREATE UNIQUE INDEX capsule_open_who_idx
    ON capsule_open (capsule_id, device_hash, COALESCE(user_id, '00000000-0000-0000-0000-000000000000'::uuid));
