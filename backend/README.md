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

## Baza de date

Schema e în `src/main/resources/db/migration` (Flyway; se aplică singură la pornire).
O schimbare de schemă = un fișier nou `V2__...sql`, niciodată modificarea unuia vechi.
De pe laptop, baza se poate deschide (DBeaver, psql) pe `localhost:5433`, utilizator și parolă `threetimes`.
