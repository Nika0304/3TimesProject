-- 3Times, migrarea 5: numele și prenumele contului (cerute la înregistrare; se văd doar în ecranul Cont,
-- urmele rămân anonime pentru ceilalți). Conturile făcute înainte rămân fără nume.
ALTER TABLE app_user
    ADD COLUMN first_name VARCHAR(50),
    ADD COLUMN last_name  VARCHAR(50);
