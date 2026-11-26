# 3Times: serverul

Java 17, Spring Boot 3, PostgreSQL 16 + PostGIS 3.4. Deocamdată: urmele din modul Prezent.

## Pornire (pe laptop, cu Docker Desktop)

Din folderul `backend/`:

```
docker compose up --build
```

Prima dată durează câteva minute (descarcă imaginile și compilează). Serverul e gata când apare
`Started ThreeTimesApplication`. Verificare: http://localhost:8080/actuator/health → `{"status":"UP"}`.

Oprire: `Ctrl+C`, apoi `docker compose down`. Datele rămân. Ca să ștergi și datele: `docker compose down -v`.

După modificări în cod: din nou `docker compose up --build`.

## Testul afară, cu hotspot

1. Telefonul face hotspot; laptopul se conectează la el.
2. Windows: rețeaua hotspotului trebuie să fie **Privată** (Setări → Rețea → Wi-Fi → rețeaua → Tip profil de rețea).
3. Prima dată, Windows poate întreba dacă Docker are voie în rețea: **Permite** (bifat „Rețele private”).
4. IP-ul laptopului în hotspot: în Git Bash sau PowerShell, `ipconfig` → „Wireless LAN adapter Wi-Fi” → IPv4 (de ex. `192.168.43.57`).
5. În aplicație, adresa serverului: `http://<IP>:8080`.

## API

Telefonul se identifică prin antetul `X-Device-Id` (un șir aleatoriu generat de aplicație; obligatoriu la scriere).
În bază se păstrează doar amprenta lui (SHA-256 cu sare). Erorile vin ca `{"error": "mesaj"}`.

| Metodă | Cale | Ce face |
|---|---|---|
| GET | `/api/v1/traces/nearby?lat=&lng=&radius=300&limit=50` | urmele din jur, de la cea mai apropiată: `{"traces": [...]}` |
| POST | `/api/v1/traces` (multipart: `trace` = JSON, `media` = fișier) | lasă o urmă; răspunde 201 cu urma |
| GET | `/api/v1/traces/{id}` | o urmă |
| GET | `/api/v1/traces/{id}/media` | poza (JPEG) sau vocea (WAV) |
| POST | `/api/v1/traces/{id}/report` (`{"reason": "..."}`, opțional) | raportează (o dată pe telefon) |
| DELETE | `/api/v1/traces/{id}` | șterge urma mea |

Câmpurile unei urme (aceleași nume ca `SavedTrace` din Unity): `id, type (TEXT/PHOTO/VOICE), content, emotion
(JOY/SADNESS/LONGING), intensity (0–1), latitude, longitude, altitude, heightAboveTerrain, accuracyMeters,
yawAccuracyDegrees, photoSource (SELFIE/PLACE/PLACE_WITH_TRACES), durationSeconds, createdAt`.
În răspuns mai apar `hasMedia`, `mine`, `reported`, `distanceMeters`.

Reguli:
- Text: mesaj obligatoriu (max 280 de caractere), fără fișier. Poză: JPEG, max 4 MB. Voce: WAV, max 3 MB, max 30 s.
- Tipul fișierului se verifică după conținut, nu după ce spune telefonul.
- Dacă telefonul retrimite aceeași urmă (același `id`), serverul nu o dublează.
- După 3 raportări de la telefoane diferite, urma e ascunsă (o mai vede doar cine a lăsat-o).

Încercare rapidă (Git Bash):

```
curl "http://localhost:8080/api/v1/traces/nearby?lat=45.7527&lng=21.2253"

curl -X POST http://localhost:8080/api/v1/traces \
  -H "X-Device-Id: test-laptop-1234567890" \
  -F 'trace={"type":"TEXT","content":"Prima urmă","emotion":"JOY","intensity":0.6,"latitude":45.7527,"longitude":21.2253,"altitude":90}'
```

## Propunerea emoției (LLM, Google Gemini)

Emoțiile sunt libere: AI-ul propune trei emoții potrivite textului, fiecare cu forma ei (aleasă din 12 forme de bază:
flower, drop, crescent, heart, star, spiral, wave, cloud, flame, sprout, burst, ring), culori, număr de petale,
ascuțime și mișcare. Utilizatorul poate alege alta sau scrie el o emoție; atunci AI-ul alege doar forma.

`POST /api/v1/emotion`:
- `{"text": "..."}` → `{"suggestions": [{"emotion": "dor", "intensity": 1, "shape": "crescent", "color": "#9C78E3",
  "accentColor": "#C9B4FF", "petals": 3, "sharpness": 0.4, "motion": "sway"}, ...]}` (3 propuneri)
- `{"text": "...", "emotion": "furie"}` → o singură propunere: forma pentru „furie”.

Fără cheie sau dacă Gemini nu răspunde: 503, iar aplicația folosește propunerea ei locală (bucurie / tristețe / dor
din cuvinte-cheie, iar pentru o emoție scrisă de utilizator, o formă aleasă stabil după cuvânt).

Cheia (nivelul gratuit din Google AI Studio) se pune în `backend/.env`, care **nu intră în repo**:

```
cp .env.example .env      # apoi pune cheia în .env
docker compose up --build -d
```

La pornire, logul spune „Propunerea emoției prin Gemini (...) e pornită” sau „GEMINI_API_KEY lipsește”.
Limite: cel mult 20 de cereri pe minut de la același telefon; textele deja văzute se iau din memorie.
Modelul se schimbă cu `GEMINI_MODEL` în `.env` (implicit `gemini-3.5-flash-lite`, cu rezerva `GEMINI_FALLBACK_MODEL` = `gemini-flash-latest`).

Încercare:

```
curl -X POST http://localhost:8080/api/v1/emotion -H "Content-Type: application/json" \
  -d '{"text":"Mi-e dor de bunica, aici mergeam la inghetata."}'   # fără diacritice: Git Bash nu le trimite în UTF-8
```

## Capsulele timpului (modul Viitor)

O capsulă e un loc + o dată, cu **cel mult 10 urme** înăuntru (fiecare: text, poză sau voce, cu emoția și forma ei).
- **Publică = de grup**: oricine ajunge la ea (cel mult 15 m) își pune urma, până o sigilează cine a creat-o; se
  sigilează și singură după 7 zile, la 10 urme sau la data deschiderii. Până la deschidere se vede doar câte urme are.
- **Personală** (doar cu cont): o umple și o deschide doar contul ei.
- **Deschiderea**: doar în locul ei (15 m) și după dată (minim mâine, maxim 10 ani, la miezul nopții, ora României),
  verificat pe server; urmele pleacă spre telefon abia atunci. **După dată se mai poate deschide două săptămâni**
  (în aplicație, lumina devine aurie); apoi dispare pentru totdeauna pentru cine n-a deschis-o. **După deschidere**,
  urmele din ea se văd o săptămână (de când a deschis-o fiecare) și încă o săptămână la un an de la deschidere;
  apoi nu mai apar. Capsula anului se poate deschide tot două săptămâni (1–14 ianuarie).

```
GET    /api/v1/capsules/nearby?lat=..&lng=..&radius=300   capsulele din jur (items doar după deschidere)
POST   /api/v1/capsules              multipart: „capsule” = {id, latitude, longitude, altitude, heightAboveTerrain,
                                     accuracyMeters, yawAccuracyDegrees, visibility, unlockAt}; „item” = prima urmă
                                     (JSON ca la urme); „media” = poza / vocea ei
POST   /api/v1/capsules/{id}/items   multipart „item” + „media”: încă o urmă (de la cel mult 15 m)
POST   /api/v1/capsules/{id}/seal    creatorul o sigilează
POST   /api/v1/capsules/{id}/open    {"latitude", "longitude", "accuracyMeters"} → capsula cu urmele, sau 403 cu motivul
GET    /api/v1/capsules/items/{itemId}/media     după deschidere
POST   /api/v1/capsules/items/{itemId}/report    după deschidere
DELETE /api/v1/capsules/items/{itemId}           urma mea (doar până la sigilare)
DELETE /api/v1/capsules/{id}/items/mine          toate urmele mele din capsulă (doar până la sigilare)
DELETE /api/v1/capsules/{id}                     capsula creată de mine (după sigilare: doar dacă toate urmele sunt ale mele)
```
După sigilare nu se mai schimbă nimic înăuntru (urmele nu se mai scot din aplicație); rămân ștergerea contului
și adminul (la cerere). Urmele din capsulele publice trec prin moderare (AI), ca urmele; adminul le vede ca
„Capsulă · se deschide …”, iar pozele și vocile din capsulele publice apar la „Capsule de verificat”.
Pentru teste, `CAPSULE_TEST_UNLOCK=true` (implicit în docker-compose, doar local) acceptă și „peste 2 minute”.
Pe un server online: `CAPSULE_TEST_UNLOCK=false`.
**Limită:** locul îl spune telefonul; serverul verifică distanța față de poziția primită, deci o aplicație modificată
ar putea trimite o poziție falsă. Data, în schimb, nu poate fi păcălită (e ceasul serverului).

## Cât se vede o urmă (Prezent)

O urmă apare la „urmele din jur” **două săptămâni** de când e lăsată; apoi se retrage și **revine în locul ei la
fiecare aniversare, o săptămână** („acum un an, aici”), din ziua aniversării (ora României). Regula e în
TraceRepository.IN_PRESENT și, pentru urmele proprii păstrate pe telefon, în aplicație (Core/TraceTime).
`/traces/mine` și „Harta mea” arată în continuare toate urmele proprii.

**Ceasul de test:** în aplicație, Meniu → Jurnal de test → „Peste un an”. Aplicația trimite antetul `X-Test-Now`,
pe care serverul îl folosește doar cu `TEST_CLOCK=true` (implicit în docker-compose, local; pe serverul online: false).
Așa se văd acum urmele la aniversare și capsula anului deschisă.

## Capsula anului (făcută de aplicație)

Urmele lăsate într-un oraș într-un an, **cu bifa „Pune-o și în capsula anului”** (bifată implicit în aplicație, se
poate scoate), se strâng într-o sferă mare (Timișoara: Piața Victoriei), care se deschide pe **1 ianuarie** anul
următor, la miezul nopții (ora României), de la cel mult 25 m. La deschidere: emoțiile anului (câte urme a avut
fiecare) și 10 urme alese la întâmplare. Urmele ascunse după raportări nu intră; o urmă ștearsă dispare și din capsulă.
Capsulele sunt în tabelul `year_capsule` (migrarea V8: Timișoara, 2026–2035; orașul = dreptunghiul hărții).

```
GET  /api/v1/year-capsules/nearby?lat=..&lng=..&radius=300   capsulele anului din jur (câte urme au, câte sunt ale mele)
POST /api/v1/year-capsules/{id}/open   {"latitude", "longitude", "accuracyMeters"}
     → după 1 ianuarie și de aproape: "emotions" [{emotion, color, count}] + "items" (urmele alese); altfel 403
```

## Moderarea

**AI-ul verifică textul** în același apel cu emoția (câmpurile `verdict` și `verdictReason` din răspuns):
- `ok`: urma se publică;
- `review` (la limită, sau Gemini n-a răspuns): urma se publică, dar apare la admin ca „De verificat (AI)”;
- `block`: urma nu se salvează. `POST /api/v1/traces` răspunde **422** cu motivul în `error`.

`POST /api/v1/emotion/moderation {"text": "..."}` → `{"verdict": "ok|review|block", "reason": "..."}`: aplicația
întreabă la „Lasă urma”, ca mesajul refuzat să poată fi corectat. La salvare, serverul verifică din nou (din cache).
Pozele și vocea nu sunt verificate de AI: pentru ele rămân raportările și adminul.

**Raportările:** după 3 raportări de la telefoane diferite urma se ascunde, până o vede adminul.

**Pagina de admin:** pune `ADMIN_PASSWORD=...` în `backend/.env`, `docker compose up --build -d`, apoi deschide
**http://localhost:8080/admin**. Browserul cere un nume (oricare) și parola. Pagina arată urmele raportate, ascunse
și cele marcate de AI, cu textul, poza sau vocea, motivele și locul pe hartă:
- **Readuce** / **E în regulă**: raportările se închid, urma se vede din nou (și rămâne marcată ca verificată);
- **Șterge**: urma dispare definitiv, cu poza sau vocea ei.
Pe hotspot serverul e HTTP simplu: parola trece necriptată prin rețeaua locală, deci deschide adminul de pe laptop.

## Conturile (opționale)

Fără cont aplicația merge ca înainte, cu id-ul anonim al telefonului. Cu cont (email + parolă; Google urmează):
urmele lăsate cu contul deschis intră în cont și sunt „ale mele” de pe orice telefon, iar aplicația arată „Harta mea”.
La înregistrare / autentificare, urmele lăsate fără cont de pe telefonul acela trec și ele în cont. O urmă (sau capsulă)
lăsată cu un cont e doar a acelui cont: alt cont deschis pe același telefon n-o vede ca fiind a lui și, la ștergerea
lui, n-o șterge (`Viewer.owns` / `Viewer.owned`). Tot așa, o capsulă deschisă dintr-un cont e deschisă doar pentru el.

| Metodă | Cale | Ce face |
|---|---|---|
| POST | `/api/v1/account/register` (`{"firstName", "lastName", "email", "password"}`) | cont nou, încă neactiv: trimite un cod pe email; 201 `{"verificationRequired": true, "email"}` (409 dacă emailul are deja un cont activ, 400 la parolă slabă) |
| POST | `/api/v1/account/verify` (`{"email", "code"}`) | codul de 6 cifre activează contul: `{"token", "account"}` |
| POST | `/api/v1/account/verify/resend` (`{"email"}`) | alt cod de verificare (429 dacă s-a cerut unul acum mai puțin de un minut) |
| POST | `/api/v1/account/login` (`{"email", "password"}`) | `{"token", "account"}`; dacă emailul nu e verificat, trimite codul și răspunde `{"verificationRequired": true}`; 401 la parolă greșită, 429 după 10 greșeli în 15 min |
| POST | `/api/v1/account/password/forgot` (`{"email"}`) | „Mi-am uitat parola”: cod pe email; 204 și dacă emailul n-are cont (nu se poate afla cine are cont) |
| POST | `/api/v1/account/password/reset` (`{"email", "code", "password"}`) | parola nouă; închide sesiunile vechi și deschide contul: `{"token", "account"}` |
| GET | `/api/v1/account` | contul: `firstName, lastName, email, createdAt, traceCount, hasPassword, hasGoogle` |
| GET | `/api/v1/account/traces` | urmele contului (max 500, cele mai noi primele): `{"traces": [...]}` |
| POST | `/api/v1/account/logout` | închide sesiunea |
| DELETE | `/api/v1/account` | șterge contul, urmele lui și pe cele lăsate de pe telefonul care cere |

Tokenul primit se trimite la orice cerere ca `Authorization: Bearer <token>` (și la `/api/v1/traces/...`); un token
expirat sau necunoscut e ignorat la urme și dă 401 la `/api/v1/account`. Parola se păstrează doar ca bcrypt; la
înregistrare, parola slabă e refuzată (aceleași reguli ca indicatorul din aplicație: `account/PasswordStrength.java`). În bază, tokenul stă doar ca SHA-256; sesiunea ține un an sau până la deconectare.

### Codurile pe email

Un cont nou cu email + parolă se poate folosi doar după ce omul scrie codul de 6 cifre primit pe email (dovada că
adresa există și e a lui). Tot cu un cod se alege o parolă nouă. Un cod ține 15 minute, are 5 încercări și se poate
cere altul după un minut; în bază stă doar amprenta lui (`email_code`, migrarea V9). Conturile făcute înainte de V9
rămân active.

Emailurile pleacă de pe Gmail-ul aplicației. În `backend/.env` (nu intră în repo):

```
MAIL_USERNAME=adresa-aplicatiei@gmail.com
MAIL_PASSWORD=parola-de-aplicatie-de-16-litere
```

„Parola de aplicație” se face din contul Google al acelui Gmail: Securitate → Verificare în doi pași (trebuie
pornită) → Parole pentru aplicații. Fără `MAIL_USERNAME` / `MAIL_PASSWORD`, codul nu se trimite, ci apare în log:
`docker compose logs api | grep Codul`.

Încercare:

```
curl -X POST http://localhost:8080/api/v1/account/register -H "Content-Type: application/json" \
  -H "X-Device-Id: test-laptop-1234567890" \
  -d '{"firstName":"Ana","lastName":"Pop","email":"test@exemplu.ro","password":"Ceai-cu-lamaie7"}'
curl http://localhost:8080/api/v1/account -H "Authorization: Bearer <token-ul primit>"
```

## Notificările

Când adminul șterge o urmă, alege motivul (pagina îl propune pe cel raportat cel mai des), iar cine a lăsat-o
primește o notificare în aplicație (Meniu → Notificări, cu bulină roșie pentru cele necitite). Notificarea e a
contului sau, pentru urmele lăsate fără cont, a telefonului; la autentificare trece în cont (migrarea V11).

| Metodă | Cale | Ce face |
|---|---|---|
| GET | `/api/v1/notifications` | `{"notifications": [{id, kind, title, body, createdAt, read}], "unread"}`, cele mai noi primele (max 50) |
| POST | `/api/v1/notifications/{id}/read` | citită |
| POST | `/api/v1/notifications/read-all` | toate citite |
| DELETE | `/api/v1/admin/{traces\|capsules}/{id}?reason=…` | (admin) șterge și trimite notificarea cu motivul |

## Baza de date

Schema e în `src/main/resources/db/migration` (Flyway; se aplică singură la pornire).
O schimbare de schemă = un fișier nou `V2__...sql`, niciodată modificarea unuia vechi.
De pe laptop, baza se poate deschide (DBeaver, psql) pe `localhost:5433`, utilizator și parolă `threetimes`.
