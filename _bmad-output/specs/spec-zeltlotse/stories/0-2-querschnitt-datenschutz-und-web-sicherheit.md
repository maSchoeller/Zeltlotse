---
title: 'Querschnitt Datenschutz und Web-Sicherheit'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: 'b6ba22533a938771886a2948837f263524744712'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
warnings: [oversized]
deferred:
  - summary: >-
      Personen-Schlüssel anlegen und im selben Command verschlüsseln scheitert vermutlich.
    evidence: |-
      PersonenSchluesselQuelle liest über PersonenLeseDbContext (eigene, schreibgeschützte Verbindung) und sieht eine noch nicht festgeschriebene Schlüsselzeile nicht; VerschluesselnAsync würde PersonenSchluesselFehltAusnahme werfen. Klärt sich, sobald 1-1 oder 2-2 (Gast-Anmeldung) Person und [Besonders]-Feld in einem Command anlegen; dann Test „Schlüssel anlegen + verschlüsseln in einer Transaktion“ und ggf. Schlüssel vorab festschreiben.
    location: >-
      src/Modules/Personen/Zeltlotse.Personen/Schluessel/PersonenSchluesselQuelle.cs
    severity: medium (unverified)
  - summary: >-
      Rate-Limit-Topf kann von vielen Besuchern geteilt werden (NAT im Lager-WLAN, Dienst-Kennung ohne Besucher-IP).
    evidence: |-
      20 Anfragen je Minute und IP gelten auch für GET /api/betrieb/antiforgery; hinter einem gemeinsamen WLAN teilen sich alle Geräte einen Topf. Mit gültiger Dienst-Kennung, aber fehlender Besucher-IP fällt die Partition auf die Prozess-IP der öffentlichen Seiten zurück. Klären mit 0-3: wie oft die App das Token holt, ob Public die Besucher-IP immer mitschickt; ggf. Antiforgery-Abruf ohne Begrenzung oder Ablehnung bei fehlender Besucher-IP.
    location: >-
      src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs
    severity: medium (unverified)
  - summary: >-
      „Eigene Transaktion“ des Sicherheitsprotokolls ist nicht getestet.
    evidence: |-
      Kein Test schreibt innerhalb einer äußeren Transaktion, die zurückrollt. Mit dem ersten Aufrufer (1-1) einen Test ergänzen: Command schreibt ins Sicherheitsprotokoll und wirft, die Zeile bleibt.
    location: >-
      src/Zeltlotse.Api/Betrieb/BetriebSicherheitsprotokoll.cs
    severity: low
  - summary: >-
      Crypto-Shredding wirkt in Backups nur, wenn der umhüllte Personen-Schlüssel nicht mit dem unveränderten Hauptschlüssel zurückkommt.
    evidence: |-
      personen.personen_schluessel liegt in derselben Datenbank wie die Daten. Ein zurückgespieltes älteres Backup bringt die Schlüsselzeile zurück, und der Hauptschlüssel (ohne Rotation) entschlüsselt sie wieder. Die Spine stellt Backups zurück („getrennte Sicherung des Hauptschlüssels“) und nimmt an, Crypto-Shredding decke das Löschen in Backups ab. Klären vor der ersten echten Freizeit: Schlüsseltabelle getrennt sichern, Löschungen nach dem Zurückspielen erneut anwenden (Löschliste) oder Hauptschlüssel rotieren.
    location: >-
      src/Modules/Personen/Zeltlotse.Personen/Schluessel/PersonenSchluessel.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Alle Fach-Stories ab 1-1 brauchen dieselben Schutzbausteine: verschlüsselte `[Besonders]`-Felder mit Schlüssel pro Person, Logs und Telemetrie ohne Personendaten, ein Sicherheitsprotokoll, Cookie-, Antiforgery-, Header- und Rate-Limit-Regeln sowie einen Datei-Adapter mit Typprüfung. Ohne gemeinsame Grundlage baut jede Story das anders oder vergisst es.

**Approach:** SharedKernel liefert Attribut, Datentyp und Dienst für die zentrale Verschlüsselung (AD-14), Datenklassifizierung, `ISicherheitsprotokoll`, `IDateiSpeicher` mit Upload-Prüfung und Auslieferungs-Helfer (AD-15). Der API-Host verdrahtet Redaction und Telemetrie-Bereinigung (AD-22), Fehlerantworten, Schema `betrieb`, Antiforgery-Zwang, Header, Cookie-Regeln und Rate-Limiting (AD-25) und den S3-Adapter. Testgetrieben: Architektur-, Unit- und HTTP-Tests gegen echtes PostgreSQL und echten S3-kompatiblen Speicher entstehen vor dem Code.

## Boundaries & Constraints

**Always:**
- **Verschlüsselung (AD-14):** `[Besonders]` (SharedKernel) markiert Eigenschaften; sie haben den Typ `Geschuetzt` (unveränderlicher Chiffretext, Konstruktor nur in SharedKernel erreichbar). `ModulDbContextBasis` bildet `Geschuetzt` per Konvention auf `bytea` ab. `IVerschluesselung` (`VerschluesselnAsync(personId, string|byte[])`, `EntschluesselnAsync(Geschuetzt)` bzw. für Bytes) nutzt AES-256-GCM mit dem Personen-Schlüssel; der Chiffretext trägt Formatversion und `PersonId` und bindet sie als zusätzliche Authentifizierungsdaten. Personen-Schlüssel: 32 Zufallsbytes, mit dem Hauptschlüssel (AES-256-GCM) umhüllt, gespeichert in `personen.personen_schluessel` (Besitzer Personen). SharedKernel kennt nur `IPersonenSchluesselQuelle` (umhüllten Schlüssel lesen); Personen setzt sie um und registriert sie. Fehlt der Schlüssel (vernichtet), wirft das Entschlüsseln `PersonenSchluesselFehltAusnahme` – nie Klartext, nie Rückfall.
- **Hauptschlüssel** kommt aus der Konfiguration `Zeltlotse:Verschluesselung:Hauptschluessel` (Base64, genau 32 Bytes); fehlt oder ist er ungültig, bricht der Start ab, ohne den Wert zu nennen.
- **Logs (AD-22):** `Microsoft.Extensions.Compliance.Redaction` + `Microsoft.Extensions.Telemetry` 10.10.0, `EnableRedaction()`, HMAC-Redactor mit Schlüssel aus `Zeltlotse:Protokoll:HmacSchluessel` (fehlt → Start bricht ab). SharedKernel definiert die Klassifizierung `Personenbezogen` als Attribut. Geloggt wird nur über `[LoggerMessage]`. Unerwartete Ausnahmen: Log und Antwort nur mit Ausnahmetyp und Trace-ID; die eingebaute Ausnahme-Protokollierung von ASP.NET Core darf Nachricht und Stacktrace nicht ausgeben.
- **Telemetrie:** Spans tragen `http.route`, nie `url.path`, `url.query` oder `url.full` (eingehend und ausgehend); Ausnahmen werden nicht als Span-Ereignis mit Nachricht aufgezeichnet.
- **Startprüfung:** Außerhalb der Umgebung `Testing` bricht der Start ab, wenn ein registrierter `DbContext` `EnableSensitiveDataLogging` oder `EnableDetailedErrors` nutzt oder die Verbindungszeichenfolge `Include Error Detail=true` enthält.
- **Fehlerform:** ProblemDetails (RFC 9457) mit `traceId` in jeder Fehlerantwort; `ValidierungsAusnahme` → `400` mit Fehlern je JSON-Feldname; Problemcodes als Konstanten in `Zeltlotse.Api.Contracts/Gemeinsam/Problemcodes.cs`.
- **Sicherheitsprotokoll:** `ISicherheitsprotokoll` (SharedKernel) mit `SchreibenAsync(art, personId?, organisationId?, bezugId?)`; Arten mindestens Anmeldung, Anmeldung fehlgeschlagen, Link-Einlösung fehlgeschlagen, Rollenvergabe, Export, Löschung. Nur Kennungen, kein Freitext, keine IP. Umsetzung im API-Host mit eigenem `BetriebDbContext` im Schema `betrieb` (eigene Historientabelle dort, Migration beim Start nach den Modulen), eigene Transaktion. Feste Frist 365 Tage; ein täglicher Hintergrunddienst des Hosts löscht ältere Zeilen endgültig.
- **Antiforgery (AD-25):** Zentral im API-Host: jede Anfrage mit POST/PUT/PATCH/DELETE unter `/api` braucht den gültigen Header `X-Zeltlotse-Antiforgery` (ASP.NET-`IAntiforgery`, Cookie `__Host-Zeltlotse-Antiforgery`), sonst `400` mit Problemcode. Einzige Ausnahme: Header `X-Zeltlotse-Dienst` mit dem prozessinternen Dienst-Geheimnis der öffentlichen Seiten (zufällig je Prozessstart, Vergleich in fester Zeit, als Singleton `PublicSeitenDienstKennung` bereitgestellt). Kein Endpunkt trägt `DisableAntiforgery`.
- **Cookies:** Alle Cookies, die der Host setzt, haben Präfix `__Host-`, `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/` (zentrale Cookie-Richtlinie).
- **Header:** Jede Antwort (auch Fehler und `/health`) trägt `Content-Security-Policy` (`default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`, keine Fremdquellen), `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`; Antworten unter `/api` zusätzlich `Cache-Control: no-store`.
- **Rate-Limiting:** Eine benannte Richtlinie, an Endpunkten per `.Zugriffsbegrenzt()`; Partition je Besucher-IP: `RemoteIpAddress`, bei gültiger Dienst-Kennung die durchgereichte IP aus `X-Zeltlotse-Besucher-Ip`. Grenzen konfigurierbar (Vorgabe 20 Anfragen je Minute); Überschreitung → `429` ProblemDetails mit `Retry-After`. Weitergeleitete Header des Reverse Proxy werden nur von konfigurierten Proxy-Adressen (`Zeltlotse:ReverseProxy:Adressen`) übernommen; Vorgabe: keine.
- **Host-Endpunkte (anonym, Antwortformen in `Zeltlotse.Api.Contracts/Betrieb/`):** `GET /api/betrieb/antiforgery` → `200 { "token": … }` + Antiforgery-Cookie, zugriffsbegrenzt. `POST /api/betrieb/fehler` mit `{ "fehlerart", "seite", "traceId" }` → `204`, zugriffsbegrenzt, Antiforgery-pflichtig; loggt nur Fehlerart, bereinigte Seite und Trace-ID. Bereinigung: Abfrage und Fragment fallen weg; jedes Pfadsegment aus `[A-Za-z0-9_-]` mit mindestens 22 Zeichen, das keine `Guid` ist, wird `***`.
- **Dateien (AD-15):** `IDateiSpeicher` (SharedKernel) mit Speichern/Lesen/Löschen unter `<modul-präfix>/<kennung>`; S3-Adapter im API-Host (einziger Ort mit S3-Code), Konfiguration aus `Zeltlotse:Dateien:*`. `DateiPruefung` erkennt den Typ an den Anfangsbytes (JPEG, PNG, WebP, PDF), Vorgabe-Grenze 10 MB; alles andere wird abgelehnt, die Endung und der vom Client genannte Typ zählen nicht. `DateiAuslieferung` erzeugt das `IResult`: Bilder `inline`, PDF `attachment`, Content-Type aus der Prüfung.
- **Aspire:** AppHost liefert Hauptschlüssel und HMAC-Schlüssel als geheime, generierte Parameter und startet einen S3-kompatiblen Speicher.
- **Architekturtests (AD-24) mit Negativ-Fixture:** `[Besonders]` nur an Eigenschaften vom Typ `Geschuetzt` und jede `Geschuetzt`-Eigenschaft trägt `[Besonders]`; kein Aufruf der `LoggerExtensions.Log*`-Methoden in `src`; kein Aufruf von `EnableSensitiveDataLogging`/`EnableDetailedErrors` in `src`; `BetriebDbContext` nur im Schema `betrieb`. HTTP-Test im Host: jeder registrierte Endpunkt mit ändernder Methode liegt unter `/api` (zentraler Zwang) oder trägt Antiforgery-Metadaten mit Prüfpflicht; keiner trägt eine Ausnahme.
- Test vor dem Code (AD-30); Paketversionen nur in `Directory.Packages.props`; 0 Warnungen.

**Never:**
- Kein Identity, keine Anmeldung, keine Sitzung, keine Rollen, kein `IKontextRollen`, keine erneute Anmeldung vor Export (1-1, 1-7); keine Fachentitäten oder fachlichen Endpunkte; kein Mail-Adapter; keine Clients (0-3); kein Lösch-Dienst und kein `PersonGeloescht` (1-7).
- Keine Suche in verschlüsselten Feldern, kein Klartext-Rückfall, keine Schlüssel oder Geheimnisse im Repository oder in Logs.
- Keine öffentlichen oder vorsignierten Datei-URLs.
- Keine Test-Endpunkte, die im Produktionsbetrieb registriert werden.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Rundlauf verschlüsselt | Testmodul-Entität mit `[Besonders] Geschuetzt`, Person mit Schlüssel | in PostgreSQL steht kein Klartext; Lesen + Entschlüsseln liefert den Text | – |
| Schlüssel vernichtet | Schlüsselzeile der Person gelöscht | Entschlüsseln scheitert | `PersonenSchluesselFehltAusnahme` |
| Manipuliert / falsche Person | Chiffretext verändert oder einer anderen Person untergeschoben | Entschlüsseln scheitert | Ausnahme, kein Klartext |
| Hauptschlüssel fehlt/ungültig | Konfiguration leer oder ≠ 32 Bytes | Start bricht ab | Meldung ohne Schlüsselwert |
| Log mit Personendaten | `[LoggerMessage]` mit `[Personenbezogen]`-Parameter | Log enthält nur den HMAC-Wert, nie den Klartext | – |
| Unerwartete Ausnahme | Endpunkt wirft mit Nachricht „Erika Muster“ | `500` ProblemDetails mit Typ + `traceId`; weder Antwort noch Log enthalten die Nachricht | – |
| Ändern ohne Antiforgery | `POST /api/betrieb/fehler` ohne/mit falschem Header | `400` ProblemDetails | – |
| Ändern mit Dienst-Kennung | gültiges Dienst-Geheimnis, kein Antiforgery-Header | `204` | falsches Geheimnis → `400` |
| Rate-Limit | Grenze 3; 4. Anfrage derselben IP | `429` + `Retry-After` | andere durchgereichte IP (mit Dienst-Kennung) → nicht begrenzt |
| Browser-Fehler | `seite` = `/app/gast/AbCdEf…(32 Zeichen)/pass?code=1#x` | Log: `/app/gast/***/pass` | ungültiger Körper → `400` je Feld |
| Upload | PNG mit Endung `.pdf`; EXE mit Endung `.png`; 11 MB PDF | PNG erkannt; abgelehnt (Typ); abgelehnt (Größe) | Ablehnungsgrund als Ergebnis, keine Ausnahme |
| Sicherheitsprotokoll | Zeile älter als 365 Tage (`FakeTimeProvider`) | Aufräumlauf löscht sie, jüngere bleiben | – |

</intent-contract>

## Code Map

- `src/Zeltlotse.Api/Program.cs` -- Host-Aufbau (ca. 65 Z.): `AddServiceDefaults`, `AddSharedKernel`, ein `AddMediator`, `AddZeltlotseModule`, `ModulMigrationen.AnwendenAsync`, `MapDefaultEndpoints`, `MapZeltlotseEndpoints`. Noch keine Middleware: hier Ausnahme-Behandlung, Header, Cookie-Richtlinie, Forwarded Headers, Antiforgery-Zwang, Rate-Limiter einhängen; `betrieb`-Migration nach den Modulen.
- `src/Zeltlotse.Api/ZeltlotseModule.cs` -- `MapZeltlotseEndpoints()` legt `app.MapGroup("/api")` an; Host-Endpunkte `/api/betrieb/...` hier oder in eigenem Ordner `Betrieb/` danebenhängen.
- `src/Zeltlotse.Api/ModulMigrationen.cs` -- feste Modulreihenfolge, `Ordnen()` wirft bei unbekanntem Modul; `BetriebDbContext` ist kein Modul, daher eigener Migrationsschritt statt neuer Eintrag in `Reihenfolge`.
- `src/Zeltlotse.ServiceDefaults/Extensions.cs:38-66` -- `ConfigureOpenTelemetry` (`IncludeFormattedMessage`, `IncludeScopes`, AspNetCore- und HttpClient-Tracing): Ort für Redaction und Tag-Bereinigung.
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContextBasis.cs` -- `OnModelCreating` (Schema, Mandantenfilter, Concurrency-Token); hier `ConfigureConventions` für `Geschuetzt` → `bytea`.
- `src/Zeltlotse.SharedKernel/Daten/ModulRegistrierung.cs` -- `AddModul<TSchreib,TLese>`, `Konfigurieren`, `DesignZeitOptionen`; Lese-Verbindung `default_transaction_read_only=on`. Vorbild für `BetriebDbContext`-Registrierung.
- `src/Zeltlotse.SharedKernel/SharedKernelRegistrierung.cs` -- `AddSharedKernel()` (TryAdd-Stil); neue Dienste hier.
- `src/Zeltlotse.SharedKernel/Commands/ValidierungsAusnahme.cs` -- bisher ohne HTTP-Abbildung; jetzt → `400`.
- `src/Zeltlotse.SharedKernel/Outbox/OutboxZustellung.cs:139` -- Vorbild für `[LoggerMessage]` (nur Typnamen und Kennungen).
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs`, `Daten/PersonenDbContext.cs`, `Migrationen/` -- Registrierung; `PersonenModell.Konfigurieren` ist leer; neue Entität `PersonenSchluessel` + Migration; `IPersonenSchluesselQuelle` liest über `PersonenLeseDbContext`.
- `src/Zeltlotse.Api.Contracts/` -- leer, keine Referenzen erlaubt; neue Ordner `Gemeinsam/`, `Betrieb/`.
- `src/Zeltlotse.AppHost/AppHost.cs` -- nur PostgreSQL 18.6 + API; S3-Speicher und geheime Parameter ergänzen.
- `tests/Zeltlotse.Testing/` -- `PostgresContainer` (`NeueDatenbankAsync`), `ZeltlotseApiFactory(verbindung, mitZustellDienst)` mit `FakeTimeProvider`, `UseEnvironment("Testing")`, `UseSetting`; erweitern um Test-Hauptschlüssel/HMAC-Schlüssel, S3-Container, Client mit `https://localhost` (Secure-Cookies), optionale Zusatz-Registrierungen.
- `tests/Zeltlotse.Api.Tests/StartTests.cs` -- prüft Schemas/Tabellen je Modul; `betrieb` ergänzen.
- `tests/Zeltlotse.ArchitectureTests/` -- `AbhaengigkeitsRegeln.cs` (Regel-Stil `FollowCustomCondition`), `Loesung.cs` (Assemblies aus Ausgabeordner), `Negativ/` (interne Fixture-Klassen, Prüfung über `AbhaengigkeitsRegeln.Negativ`), `SchemaTests` (Modelle nur im eigenen Schema).
- `tests/Zeltlotse.Testmodul/` -- Testmodul (`Notiz`, Schema `testmodul`, eigene Migrationen) für den `[Besonders]`-Rundlauf; nur von Tests referenziert.
- `Directory.Packages.props` -- zentrale Versionen, Transitive Pinning an; neue Pakete eintragen.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.ArchitectureTests/` -- ZUERST: Regeln `[Besonders]`↔`Geschuetzt`, kein `LoggerExtensions.Log*`, kein `EnableSensitiveDataLogging`/`EnableDetailedErrors`, `BetriebDbContext` nur `betrieb`; je Regel Negativ-Fixture unter `Negativ/`.
- `tests/Zeltlotse.SharedKernel.Tests/` -- ZUERST: Verschlüsselung (Rundlauf, Manipulation, falsche Person, fehlender Schlüssel, Hauptschlüssel-Prüfung), `DateiPruefung`, `DateiAuslieferung`, Pfad-Bereinigung, Redaction mit `FakeLogger`; Rundlauf über PostgreSQL mit `[Besonders]`-Eigenschaft im Testmodul (neue Migration).
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST (neues Projekt, erstes Modul-Testprojekt): `personen_schluessel` anlegen/lesen/löschen gegen PostgreSQL, Entschlüsseln nach Löschen scheitert.
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: HTTP-Tests für alle Matrix-Zeilen auf Host-Ebene, Header an `/health`, `/api/...` und Fehlerantworten, Cookie-Attribute, Endpunkt-Aufzählung Antiforgery, Startabbrüche (Hauptschlüssel, HMAC, Sensitive Logging außerhalb `Testing`), Telemetrie ohne Rohpfad (In-Memory-Exporter), Sicherheitsprotokoll schreiben + Frist, S3-Adapter gegen Container (speichern, lesen, löschen, Präfix).
- `tests/Zeltlotse.Testing/` -- Fixture für S3-kompatiblen Container, Factory-Erweiterungen (Schlüssel, Client-Optionen, Zusatz-Registrierungen nur für Tests).
- `src/Zeltlotse.SharedKernel/` -- `Datenschutz/` (`BesondersAttribute`, `Geschuetzt`, `IVerschluesselung`, `Hauptschluessel`, `IPersonenSchluesselQuelle`, `PersonenSchluesselFehltAusnahme`, `Personenbezogen`), `Sicherheitsprotokoll/` (`ISicherheitsprotokoll`, Arten), `Dateien/` (`IDateiSpeicher`, `DateiPruefung`, `DateiAuslieferung`), Konvention in `ModulDbContextBasis`.
- `src/Modules/Personen/Zeltlotse.Personen/` -- `PersonenSchluessel`, Modell, Migration, `IPersonenSchluesselQuelle`-Umsetzung, Registrierung.
- `src/Zeltlotse.Api/` -- `Betrieb/` (`BetriebDbContext`, Migration, Sicherheitsprotokoll, Fristdienst, Endpunkte), `Sicherheit/` (Header, Cookies, Antiforgery-Zwang, Dienst-Kennung, Rate-Limiting, Forwarded Headers), Fehlerbehandlung, Startprüfungen, `Adapter/S3DateiSpeicher`.
- `src/Zeltlotse.ServiceDefaults/Extensions.cs` -- Redaction, Tag-Bereinigung, keine Ausnahme-Nachrichten in Spans.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/Problemcodes.cs`, `Betrieb/` -- Problemcodes und Datenformen der Host-Endpunkte.
- `src/Zeltlotse.AppHost/AppHost.cs`, `Directory.Packages.props`, `Zeltlotse.slnx`, `appsettings*.json` -- S3-Speicher, geheime Parameter, Pakete, neues Testprojekt, Vorgabewerte ohne Geheimnisse.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, SharedKernel, Personen, API) gegen echtes PostgreSQL 18.6 und echten S3-kompatiblen Container.
- Given eine Entität mit einer `string`-Eigenschaft unter `[Besonders]` (Negativ-Fixture), when die Architekturtests laufen, then schlagen sie fehl; ebenso für `LoggerExtensions.LogInformation` und `EnableSensitiveDataLogging` im Fixture.
- Given der Host läuft, when eine beliebige Antwort (200, 400, 404, 429, 500, `/health`) geprüft wird, then trägt sie CSP mit `frame-ancestors 'none'`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, und Antworten unter `/api` `Cache-Control: no-store`.
- Given `GET /api/betrieb/antiforgery` über HTTPS, when die Antwort geprüft wird, then hat jedes `Set-Cookie` Präfix `__Host-`, `secure`, `httponly`, `samesite=lax`, `path=/`, und das Token aus dem Körper macht `POST /api/betrieb/fehler` mit diesem Cookie gültig (`204`).
- Given die Lösung, when `dotnet build Zeltlotse.slnx` läuft, then 0 Warnungen, 0 Fehler, und der AppHost startet API, PostgreSQL und S3-Speicher.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 49 findings — high 0, medium 5, low 31, false 8, maybe-false 5
- findings:
  - `[medium]` `[patch]` (blind) `IFormFile`-Endpunkte werfen ohne `UseAntiforgery()` („anti-forgery metadata, but a middleware was not found“) — `AntiforgeryZwang` markiert die Anfrage nach erfolgreicher Prüfung wie die eingebaute Middleware; HTTP-Test mit Test-Upload-Endpunkt.
  - `[maybe-false]` `[defer]` (blind) Schlüssel anlegen und im selben Command verschlüsseln scheitert, weil `PersonenSchluesselQuelle` über den Lese-Kontext liest — heute ohne Aufrufer; klärt sich, sobald 1-1/2-2 Person und `[Besonders]`-Feld in einem Command anlegen (siehe `deferred`).
  - `[medium]` `[patch]` (blind) `seite` der Browser-Meldung lässt Freitext (Leerzeichen, Namen, `@`) ins Log — Validator auf URL-Pfadzeichen beschränkt, Tests ergänzt.
  - `[medium]` `[patch]` (blind) EF Core und andere Framework-Logger exportieren Ausnahme-Nachricht und Stacktrace über OTLP — Log-Prozessor entfernt die Ausnahme, behält nur den Typ; Test mit In-Memory-Log-Exporter.
  - `[low]` `[patch]` (blind) Client-Abbruch landet als unerwartete 500 im Error-Log; `BadHttpRequestException` ohne `code`; fehlende Abbildungen für Konflikte/Schlüssel — Abbruch ohne Error-Log, `code = validierung` ergänzt; 409/Schlüsselfehler kommen mit ihren ersten Endpunkten (AD-19, 1-x).
  - `[maybe-false]` `[defer]` (blind) Gemeinsamer Rate-Limit-Topf: Dienst-Kennung ohne Besucher-IP fällt auf die Prozess-IP zurück; 20/min je IP hinter Lager-WLAN (NAT) inklusive Antiforgery-Abruf — Rückfall betrifft erst Public (0-3); NAT-Wirkung hängt davon ab, wie oft die App (0-3) das Token holt (siehe `deferred`).
  - `[low]` `[reject]` (blind) Verschlüsselung von `[Besonders]`-Dateien nicht erzwungen, Content-Type vom Aufrufer — `IVerschluesselung` hat die Byte-Variante; erste Nutzung in 1-5; Erzwingen bräuchte neue öffentliche Oberfläche.
  - `[low]` `[reject]` (blind) S3-Konfiguration erst beim ersten Zugriff geprüft, kein Health-Check, Abbruch-Token beim Bucket-Check ignoriert — bewusst, damit Tests ohne S3 starten; Fix wäre ein zusätzlicher Startzweig.
  - `[low]` `[reject]` (blind) Kein Schlüssel-Cache, eine Abfrage je Entschlüsselung — bei den Mengen einer Freizeit vernachlässigbar; Fix wäre ein Cache.
  - `[false]` `[reject]` (blind) Keine Rotation des Hauptschlüssels, keine Schlüssel-ID — die Spine stellt die Rotation ausdrücklich zurück („Rotation von Hauptschlüssel und VAPID-Schlüssel“); das Format hat ein Versionsbyte.
  - `[false]` `[reject]` (blind) Story-Datei ohne Auto Run Result und Triage-Log — wird in diesem Schritt geschrieben.
  - `[low]` `[patch]` (blind) TraceId-Muster lehnt `:` aus `HttpContext.TraceIdentifier` ab — `:` erlaubt.
  - `[low]` `[reject]` (blind) Kein HSTS, keine `Permissions-Policy` — HTTPS übernimmt der Reverse Proxy (AD-23); die Header-Liste der Spine ist erfüllt.
  - `[low]` `[reject]` (blind) Kein Index auf `PersonId` im Sicherheitsprotokoll — noch kein Leser; kommt mit Auskunft/Löschen (1-7).
  - `[medium]` `[patch]` (edge) Ohne Antiforgery-Header liest `IAntiforgery` den Formular-Körper (große anonyme Körper, Token im Formularfeld akzeptiert) — fehlender Header gilt sofort als ungültig; Test ergänzt.
  - `[low]` `[reject]` (edge) Dienst-Kennung ohne gültige Besucher-IP → Prozess-IP — siehe Rate-Limit-Eintrag oben (`deferred`); Fix wäre ein neuer Zweig.
  - `[low]` `[reject]` (edge) `RemoteIpAddress` null → gemeinsame Partition — hinter Kestrel/Proxy immer gesetzt; Fix wäre ein Zweig.
  - `[low]` `[reject]` (edge) IPv6-Adresswechsel im /64 umgeht die Grenze — gezielter Angriff, bekannter Kreis; Fix wäre Präfix-Logik.
  - `[low]` `[reject]` (edge) Ungültige oder CIDR-Proxy-Adresse wirft beim Start — lauter Fehler bei Fehlkonfiguration ist gewollt; CIDR nicht gefordert.
  - `[low]` `[reject]` (edge) Einzelnes UTF-16-Surrogat ergibt 500 statt Validierungsfehler — praktisch nicht erreichbar; Fix wäre ein Zweig.
  - `[maybe-false]` `[defer]` (edge) Schlüssel im selben Command anlegen und nutzen — gleiche Ursache wie blind, siehe `deferred`.
  - `[low]` `[reject]` (edge) Unbekannte Inhaltsart wirft `InvalidOperationException` statt `CryptographicException` — kein Klartext, nur andere Ausnahme; Fix wäre ein Wächter.
  - `[low]` `[reject]` (edge) Defekte `bytea`-Zeile bricht die Abfrage — lauter Fehler bei manipulierten Daten ist gewollt.
  - `[low]` `[patch]` (edge) 32-stelliges Hex-Token gilt als `Guid` und bleibt ungeschwärzt — `Guid.TryParseExact(…, "D")`, Test ergänzt.
  - `[low]` `[reject]` (edge) Token mit `.`, `%`, `+`, `=` werden nicht geschwärzt — Token sind nach AD-11 base64url; Fix wäre weitere Musterlogik.
  - `[medium]` `[patch]` (edge) CR/LF in `seite` — gleiche Ursache wie blind „Freitext in `seite`“, mitbehoben.
  - `[low]` `[patch]` (edge) Client-Abbruch als Error geloggt — gleiche Ursache wie blind, mitbehoben.
  - `[low]` `[patch]` (edge) `BadHttpRequestException` ohne `code` — gleiche Ursache wie blind, mitbehoben.
  - `[false]` `[reject]` (edge) Zwei Instanzen legen den Bucket gleichzeitig an — ein Prozess (AD-1, AD-23).
  - `[low]` `[reject]` (edge) Hängender Bucket-Check hält die Sperre — nur beim ersten Zugriff; Fix wäre Zeitlimit-Logik.
  - `[false]` `[reject]` (edge) Objekt ohne Content-Type → null — der Adapter setzt den Typ immer beim Speichern; S3 liefert sonst einen Vorgabetyp.
  - `[low]` `[reject]` (edge) Leerer Dateiname in `Content-Disposition` — nur bei unsinnigem Aufruf; Fix wäre ein Wächter.
  - `[low]` `[reject]` (edge) Höchstgröße 0 oder negativ — Aufrufer nutzen die Vorgabe; Fix wäre ein Wächter.
  - `[low]` `[reject]` (edge) Von Hand ersetzter 44-Zeichen-Schlüssel in den User-Secrets + `=` → Start bricht mit klarer Meldung ab — nur bei Handarbeit; Fix wäre ein Zweig.
  - `[low]` `[reject]` (edge) Architekturregel übersieht `Geschuetzt` in Feldern oder Listen — EF bildet Eigenschaften ab; Fix wäre Zusatzlogik.
  - `[low]` `[reject]` (edge) Direkter Aufruf von `ILogger.Log` umgeht die Regel — der `[LoggerMessage]`-Generator ruft selbst `ILogger.Log`; der Fix bräuchte eine Ausnahme für generierten Code.
  - `[low]` `[reject]` (edge) Kestrel-eigene Ablehnungen (400/414/431) ohne Sicherheits-Header — Antworten ohne Inhalt; Fix nur außerhalb der Middleware möglich.
  - `[low]` `[reject]` (edge) `IDateiSpeicher` prüft Typ nicht selbst — gleiche Ursache wie blind „Dateien nicht erzwungen“.
  - `[low]` `[patch]` (vgap) Antiforgery-Zwang und `no-store` nicht mit `/API` in anderer Schreibung getestet — Testfälle ergänzt.
  - `[low]` `[patch]` (vgap) `IncludeScopes = false` ungeprüft — Test mit In-Memory-Log-Exporter ergänzt (mit dem Log-Prozessor).
  - `[low]` `[patch]` (vgap) `RecordException = false` für ausgehende Spans ungeprüft — `Events`-Prüfung ergänzt.
  - `[low]` `[defer]` (vgap) „Eigene Transaktion“ des Sicherheitsprotokolls ungetestet — noch kein Aufrufer; Test mit dem ersten Aufrufer (1-1), siehe `deferred`.
  - `[low]` `[patch]` (vgap) Hauptschlüssel in nicht-kanonischem Base64 aus Aspire ungetestet — Test ergänzt.
  - `[maybe-false]` `[defer]` (vgap) Schlüssel im selben Command — gleiche Ursache wie blind, siehe `deferred`.
  - `[maybe-false]` `[reject]` (vgap) Span-Test bewacht `aspNetCore.RecordException` womöglich nicht — wäre nur low; klären durch Umschalten des Werts im Test.
  - `[false]` `[reject]` (intent) AD-30-Pflichtfälle 403/404/Token fehlen — AD-30 bindet sie an die Endpunkt-Art; beide Endpunkte sind anonyme Betriebs-Endpunkte, Rollen und Identity schließt die Story aus (wie 0-1).
  - `[false]` `[reject]` (intent) Verhalten nur an Test-Endpunkten gezeigt — beide echten Endpunkte haben eigene HTTP-Tests; Test-Endpunkte decken nur Fälle ohne echten Auslöser (500, Cookie ohne Präfix).
  - `[false]` `[reject]` (intent) „Test zuerst“ am Diff nicht nachprüfbar — Prozessaussage, kein Defekt.
  - `[false]` `[reject]` (intent) Antiforgery-Token-Fälle sind keine AD-30-Token-Fälle — beschreibend; Token-Endpunkte (öffentlich, QR) entstehen ab 2-2.

### 2026-10-11 — Review pass (Folgedurchlauf)
- verdicts: 42 findings — high 0, medium 4, low 25, false 9, maybe-false 4
- findings:
  - `[medium]` `[defer]` (blind) Crypto-Shredding schützt Backups nicht: zurückgespielte Schlüsselzeile + unveränderter Hauptschlüssel macht Daten wieder lesbar — Ursache ist die Spine-Annahme (Backups zurückgestellt), nicht diese Story; siehe `deferred`.
  - `[medium]` `[patch]` (blind) Konsole-, Debug-, EventSource- und EventLog-Provider umgehen `LogBereinigung` und schreiben Framework-Ausnahmen mit Nachricht und Stacktrace — `builder.Logging.ClearProviders()` vor `AddOpenTelemetry` in `ServiceDefaults`; Test `Host_gibt_Logs_nur_ueber_OpenTelemetry_aus` (vorher rot).
  - `[low]` `[reject]` (blind) `PfadBereinigung` übersieht Token mit `.`, `~`, `%` — carried: Token sind nach AD-11 base64url; Fix wäre weitere Musterlogik.
  - `[low]` `[reject]` (blind) Kein HSTS, keine `Permissions-Policy`/COOP — carried: HTTPS übernimmt der Reverse Proxy (AD-23); der Teil „http-Profil“ ist mit edge „HTTP → 500“ gepatcht.
  - `[low]` `[reject]` (blind) IPv6 je Einzeladresse statt /64 — carried: gezielter Angriff, bekannter Kreis; Fix wäre Präfix-Logik.
  - `[maybe-false]` `[defer]` (blind) Dienst-Kennung ohne Besucher-IP → gemeinsamer Topf — carried, siehe `deferred`.
  - `[low]` `[reject]` (blind) Proxy-Adressen ohne Validierung, kein CIDR — carried: lauter Fehler bei Fehlkonfiguration gewollt, CIDR nicht gefordert.
  - `[low]` `[reject]` (blind) Krypto-Ausnahmen → 500; `BadHttpRequestException` mit 413/408 trägt `code = validierung` — Schlüsselfehler carried (kommen mit ihren ersten Endpunkten); 413/408 nur bei übergroßen oder hängenden Anfragen, Fix bräuchte einen neuen Problemcode.
  - `[low]` `[reject]` (blind) Chiffretext nur an die Person gebunden, nicht an Feld/Zeile — Format nach AD-14 (Version, Inhaltsart, PersonId); Vertauschen setzt Schreibzugriff auf die Datenbank voraus; Fix wäre neue öffentliche Oberfläche.
  - `[low]` `[reject]` (blind) Eine Abfrage je Entschlüsselung — carried: Mengen einer Freizeit; Fix wäre ein Cache.
  - `[low]` `[reject]` (blind) S3-Adapter legt Bucket mit Vorgaben an, Abbruch beim Bucket-Check ignoriert, `LesenAsync` ohne Größengrenze — neue Buckets sind privat (Test zeigt 403); Abbruch carried; gespeichert wird nur Geprüftes (≤ 10 MB).
  - `[false]` `[reject]` (blind) `sendBeacon` kann den Antiforgery-Header nicht setzen — die Intent verlangt den Header für jedes ändernde `/api`; der Client holt das Token vorab (0-3).
  - `[low]` `[reject]` (blind) Upload-Grenze greift erst nach dem Einlesen, kein `RequestSizeLimit` — es gibt noch keinen Upload-Endpunkt im Produktivbetrieb; der erste (1-5) setzt die Grenze am Endpunkt.
  - `[low]` `[patch]` (blind) Test „Bucket nicht öffentlich“ akzeptiert jede Nicht-2xx-Antwort — prüft jetzt genau `403 Forbidden`.
  - `[low]` `[patch]` (edge) Über reines HTTP wirft `IAntiforgery` wegen `SecurePolicy=Always` → 500 bei jedem Ändern — HTTP-Profil und HTTP-Adresse aus `launchSettings.json` der API entfernt (nur noch `https://localhost:7180`); ein Proxy ohne konfigurierte Adressen bleibt lauter Konfigurationsfehler.
  - `[maybe-false]` `[defer]` (edge) Dienst-Kennung ohne Besucher-IP — carried, siehe `deferred`.
  - `[maybe-false]` `[defer]` (edge) Schlüssel im selben Command anlegen und nutzen — carried, siehe `deferred`.
  - `[low]` `[reject]` (edge) 413/408/431 mit `code = validierung` — gleiche Ursache wie blind „Ausnahme-Abbildung“.
  - `[low]` `[reject]` (edge) CIDR/ungültige Proxy-Adresse — carried.
  - `[medium]` `[patch]` (edge) Abbruch des Aufrufers (Client trennt) verwirft den Sicherheitsprotokoll-Eintrag — `BetriebSicherheitsprotokoll` nutzt `CancellationToken.None` für Kontext und Speichern, Vertrag dokumentiert; Test `Abbruch_des_Aufrufers_verhindert_den_Eintrag_nicht` (vorher rot).
  - `[false]` `[reject]` (edge) Zwei Instanzen legen den Bucket gleichzeitig an — carried: ein Prozess (AD-1, AD-23).
  - `[low]` `[reject]` (edge) Bucket-Check ignoriert den Abbruch — carried.
  - `[low]` `[reject]` (edge) Objekt ohne Content-Type; `NoSuchBucket` als „Datei fehlt“ — Content-Type carried (`false`); der Bucket wird beim ersten Zugriff sichergestellt, ein später gelöschter Bucket ist Betriebsfehler.
  - `[low]` `[reject]` (edge) Unbekannte Inhaltsart → `InvalidOperationException` — carried.
  - `[low]` `[reject]` (edge) Token mit `.`, `~`, `%` — carried.
  - `[low]` `[reject]` (edge) Leerer Dateiname in `Content-Disposition` — carried.
  - `[low]` `[reject]` (edge) Höchstgröße 0 oder negativ — carried.
  - `[low]` `[reject]` (edge) Startprüfung übersieht `Include Error Detail` bei `NpgsqlDataSource` — alle Kontexte nutzen heute Verbindungszeichenfolgen (`ModulRegistrierung.Konfigurieren`); Fix wäre ein Zweig für einen Fall ohne Aufrufer.
  - `[low]` `[patch]` (vgap) Fehlerpfad des Fristdienstes ungetestet — Test `Fristdienst_laeuft_nach_einem_Fehler_weiter_und_loggt_nur_den_Typ`: erster Lauf scheitert mit Personendaten in der Nachricht, Log nur mit Typ, nächster Tag räumt auf.
  - `[low]` `[reject]` (vgap) Bereinigung nur mit In-Memory-Exporter getestet, nicht mit OTLP — Reihenfolge heute richtig (Bereinigung vor `UseOtlpExporter`); Fix bräuchte einen OTLP-Empfänger oder Reflexion in die Provider-Kette.
  - `[low]` `[reject]` (vgap) AppHost-Verdrahtung (Schlüsselnamen) ohne Test — Tippfehler beim Hauptschlüssel bricht den Start laut ab; Fix bräuchte ein AppHost-Testprojekt.
  - `[maybe-false]` `[defer]` (vgap) Schlüssel im selben Command — carried, siehe `deferred`.
  - `[false]` `[reject]` (intent) AD-30-Pflichtfälle 403/404/Token fehlen — carried: beide Endpunkte sind anonyme Betriebs-Endpunkte, Rollen/Identity schließt die Intent aus.
  - `[false]` `[reject]` (intent) Antiforgery-Regel über Endpunkt-Metadaten statt Anfragen — die Intent verlangt genau die Aufzählung der registrierten Endpunkte und ihrer Metadaten.
  - `[medium]` `[patch]` (intent) Log-Bereinigung erreicht nur Export, nicht andere Provider — gleiche Ursache wie blind „Konsole-Provider“, mitbehoben.
  - `[false]` `[reject]` (intent) Ausnahme-Ereignisse bleiben, nur ohne Nachricht — die Intent verbietet das Ereignis „mit Nachricht“; erfüllt und getestet.
  - `[false]` `[reject]` (intent) GUID nur in Bindestrich-Form ausgenommen — bewusste Entscheidung aus dem ersten Review (32 Hex-Zeichen könnten ein Token sein); Matrix erfüllt.
  - `[low]` `[reject]` (intent) AppHost ohne automatischen Test — gleiche Ursache wie vgap „AppHost-Verdrahtung“.
  - `[false]` `[reject]` (intent) Startprüfung findet nur über `DbContextOptions<>` registrierte Kontexte — so registriert jeder Kontext der Lösung (`AddDbContext`, `AddDbContextFactory`).
  - `[low]` `[defer]` (intent) „Eigene Transaktion“ ohne Rollback-Test — carried, siehe `deferred`.
  - `[false]` `[reject]` (intent) Datei-Prüfung über HTTP nur am Test-Endpunkt — carried: Fachendpunkte schließt die Intent aus.
  - `[false]` `[reject]` (intent) „0 Warnungen“ und „Test zuerst“ am Diff nicht prüfbar — carried: Prozessaussage.

## Design Notes

- **AD-30-Pflichtfälle:** Die Story hat nur zwei anonyme Betriebs-Endpunkte des Hosts. Keiner gehört zu den Arten Organisation/Freizeit, persönlich oder Token; die Fälle „falsche Rolle 403“, „fremde Organisation 404“ und Token-Fälle haben hier keinen Gegenstand. An ihrer Stelle prüfen HTTP-Tests je Endpunkt: Erfolg, fehlender/falscher Antiforgery-Header, Dienst-Kennung, Rate-Limit, Header, Validierung. Rollen und Token kommen mit 1-1 bzw. 2-2.
- **Explizite statt verdeckte Verschlüsselung:** Ein EF-Value-Converter kennt weder DI noch die `PersonId` der Zeile. Darum ist `Geschuetzt` ein eigener Typ, den nur `IVerschluesselung` erzeugt; Handler rufen ihn ausdrücklich auf. Der Architekturtest „`[Besonders]` nur als `Geschuetzt`“ erzwingt so AD-14, ohne Interceptor-Magie.
  ```csharp
  [Besonders] public Geschuetzt Text { get; private set; } = null!;
  notiz.TextSetzen(await verschluesselung.VerschluesselnAsync(personId, "Erdnussallergie", ct));
  ```
- **Schlüssel bei Personen, Zugriff über SharedKernel:** Personen besitzt `personen_schluessel` (Datenbesitz-Tabelle). Andere Module verschlüsseln über `IVerschluesselung` aus SharedKernel, das über `IPersonenSchluesselQuelle` liest – kein direkter Personen-Aufruf, keine neue Pfeil-Ausnahme. Schlüssel anlegen geschieht in 1-1 im Command „Person anlegen“ über den schreibenden Personen-`DbContext`; Vernichten im Lösch-Ablauf (1-7). Bis dahin legen Tests Schlüssel direkt an.
- **Dienst-Kennung:** zufälliges 256-Bit-Geheimnis je Prozess, das Public (0-3) über DI erhält. Damit gilt die Ausnahme nur für Aufrufe aus dem eigenen Prozess (AD-5) und überlebt einen späteren eigenen Container nicht ohne bewusste Änderung.
- **Antiforgery-Token und Anmeldung:** ASP.NET bindet das Token an die Identität; nach der Anmeldung (1-1) muss der Client das Token neu holen.
- **Cookie-Namen:** Das Sitzungs-Cookie aus 1-1 heißt `__Host-Zeltlotse`; die zentrale Cookie-Richtlinie erzwingt die Attribute, der Cookie-Test aus dieser Story deckt es dann mit ab.
- **S3-Container für Tests und Aspire:** Wahl des Images frei, Bedingung: S3-kompatibel, fester Tag, lokal per Testcontainers startbar. Die gewählte Version und das AWSSDK-/Client-Paket (aktuelle stabile Version, zentral fixiert) im Auto Run Result festhalten.
- **Frist Sicherheitsprotokoll 365 Tage:** Die Spine verlangt eine feste Frist und stellt deren Wert bis OP-6 zurück; ein Jahr deckt eine Freizeit-Saison ab. Konstante an einer Stelle.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, inklusive Architekturtests

## Auto Run Result

Status: done

**Zusammenfassung:** Querschnitt Datenschutz und Web-Sicherheit nach AD-14, AD-15, AD-22 und AD-25. SharedKernel liefert `[Besonders]`, `Geschuetzt` (nur dort erzeugbar, per Konvention `bytea`) und `IVerschluesselung` (AES-256-GCM, Formatversion, Inhaltsart und `PersonId` als Authentifizierungsdaten). Personen-Schlüssel sind mit dem Hauptschlüssel umhüllt und liegen in `personen.personen_schluessel`. Dazu kommen Datenklassifizierung `[Personenbezogen]` mit HMAC-Schwärzung, `ISicherheitsprotokoll`, `IDateiSpeicher` mit `DateiPruefung` (JPEG/PNG/WebP/PDF an den Anfangsbytes, 10 MB) und `DateiAuslieferung`. Der API-Host hat jetzt:
- Schema `betrieb` mit Sicherheitsprotokoll (eigene Transaktion, Frist 365 Tage, täglicher Fristdienst);
- ProblemDetails mit `traceId`; unerwartete Fehler nur mit Typ;
- Startprüfungen (Hauptschlüssel, HMAC-Schlüssel, Sensitive Logging/Detailed Errors/`Include Error Detail` außerhalb `Testing`);
- Sicherheits-Header und `no-store` unter `/api`, Cookie-Richtlinie `__Host-` (Secure, HttpOnly, Lax, Path=/);
- zentralen Antiforgery-Zwang (nur Header, Ausnahme Dienst-Kennung der öffentlichen Seiten), Rate-Limiting je Besucher-IP, Forwarded Headers nur von konfigurierten Proxys;
- S3-Adapter; Endpunkte `GET /api/betrieb/antiforgery` und `POST /api/betrieb/fehler`.

Telemetrie und Log-Export tragen weder Rohpfad, Abfrage noch Ausnahme-Nachrichten. Der AppHost erzeugt die Schlüssel als geheime Parameter und startet RustFS als S3-Speicher.

**Gewählte Versionen (Design Notes):** S3-Image `rustfs/rustfs:1.0.1` (MinIO-Images sind nicht mehr beziehbar); `AWSSDK.S3` 4.0.104.2; `Microsoft.Extensions.Compliance.*`, `Telemetry`, `Telemetry.Abstractions`, `Diagnostics.Testing` 10.10.0; `OpenTelemetry.Exporter.InMemory` 1.19.1; `Testcontainers` 4.16.0.

**Dateien:**
- `src/Zeltlotse.SharedKernel/Datenschutz/` – Verschlüsselung, `Geschuetzt`, Hauptschlüssel, Schwärzung, Pfad-Bereinigung.
- `src/Zeltlotse.SharedKernel/Dateien/`, `Sicherheitsprotokoll/`, `Web/` – Datei-Schnittstelle und -Prüfung, Sicherheitsprotokoll-Vertrag, `.Zugriffsbegrenzt()`.
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContextBasis.cs` – `Geschuetzt` → `bytea`.
- `src/Modules/Personen/Zeltlotse.Personen/` – `PersonenSchluessel`, Schlüsselquelle, Migration.
- `src/Zeltlotse.Api/Betrieb/` – `BetriebDbContext`, Migration, Sicherheitsprotokoll, Fristdienst, Betriebs-Endpunkte.
- `src/Zeltlotse.Api/Sicherheit/` – Header, Cookies, Antiforgery-Zwang, Dienst-Kennung, Rate-Limiting, Proxy.
- `src/Zeltlotse.Api/Fehler/`, `Start/`, `Adapter/`, `Program.cs`, `ModulMigrationen.cs`, `IZusatzEndpunkte.cs` – Fehlerform, Startprüfung, S3-Adapter, Verdrahtung, Test-Endpunkte nur in `Testing`.
- `src/Zeltlotse.ServiceDefaults/` – Schwärzung, `TelemetrieBereinigung`, `LogBereinigung`, Ausnahme-Ereignisse ohne Nachricht.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/`, `Betrieb/` – Problemcodes, Kopfzeilen, Datenformen.
- `src/Zeltlotse.AppHost/AppHost.cs`, `Properties/launchSettings.json` – RustFS, geheime Parameter, HTTPS-Profil.
- `tests/…` – Architekturregeln mit Negativ-Fixtures, neues `Zeltlotse.Personen.Tests`, S3-Fixture, Factory-Erweiterungen, `Notiz.Vertraulich` im Testmodul, HTTP-, Unit- und Integrationstests für jede Matrix-Zeile.

**Review:** 49 Befunde (medium 5, low 31, false 8, maybe-false 5). Gepatcht wurden 4 medium-Einträge: Formular-Endpunkte hinter dem Antiforgery-Zwang, Header-Pflicht ohne Formular-Rückfall, Seitenangabe nur mit URL-Zeichen, Ausnahmen aus dem Log-Export. Dazu 6 low-Einträge: Client-Abbruch und `code` bei 400, TraceId mit `:`, Hex-Token-Schwärzung, drei Testlücken. Zurückgestellt wurden 3 (siehe `deferred`). Alle übrigen Befunde sind mit Begründung im Review Triage Log verworfen.

**Abweichung beim Patch:** Ausgehende HTTP-Spans tragen in .NET 10 immer ein `exception`-Ereignis, auch mit `RecordException = false`. Ein prozessweiter `ActivityListener.ExceptionRecorder` (`AusnahmeEreignisBereinigung`) setzt deshalb nur den Typ ein und leert den Stacktrace. Der Test prüft das Ereignis auf fehlende Nachricht, fehlenden Stacktrace und fehlendes Token.

**Folge-Review empfohlen:** ja, weil vier medium-Einträge gepatcht wurden (gepatcht: high 0, medium 4, low 6). Offenes Risiko: Der Antiforgery-Zwang setzt den Middleware-Schlüssel von ASP.NET selbst und prüft jetzt auch ändernde Anfragen außerhalb von `/api`, sobald der Endpunkt Antiforgery verlangt, und zwar nur mit Header. Die Blazor-Formulare der öffentlichen Seiten (0-3) schicken das Token aber als Formularfeld. 0-3 muss deshalb das Zusammenspiel mit `UseAntiforgery()` klären, ohne die Prüfung zu öffnen. Außerdem greift der `ExceptionRecorder` prozessweit in die Laufzeit ein.

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` ergibt 0 Warnungen und 0 Fehler. `dotnet test` ergibt 252 von 252 grün (Architektur, SharedKernel, Personen, API; echtes PostgreSQL 18.6 und RustFS per Testcontainers). Jede Zeile der I/O-Matrix hat einen laufenden Test. Der Umsetzer hat den Aspire-Start lokal geprüft: `/health` liefert 200 mit Sicherheits-Headern, das Antiforgery-Cookie hat die vorgeschriebenen Attribute.

**Restrisiken:**
- Siehe `deferred`: Schlüssel im selben Command, gemeinsamer Rate-Limit-Topf, Transaktionstest des Sicherheitsprotokolls.
- RustFS 1.0.1 ist jung.
- Die Dateispeicher-Konfiguration wird erst beim ersten Zugriff geprüft.
- `Telemetry.Abstractions` wird mit `PrivateAssets="none"` weitergereicht, damit der schwärzende Log-Generator in allen Modulen greift.
- Lokal liegen erzeugte Geheimnisse in den User-Secrets des AppHost, außerdem gibt es das Docker-Volume `zeltlotse-dateien`.
- Keine Oberfläche, daher keine Doku-Seite.

### Folgedurchlauf 2026-10-11

**Zusammenfassung:** Zweite Review-Runde auf die erledigte Story (vier Layer, 42 Befunde). Gepatcht wurden zwei medium- und drei low-Einträge, testgetrieben (neue Tests zuerst rot, dann Code):
- Logs gehen nur noch über OpenTelemetry: `ServiceDefaults` entfernt die Vorgabe-Provider (Konsole, Debug, EventSource, EventLog), die Ausnahmen der Frameworks mit Nachricht und Stacktrace an der Bereinigung vorbei schrieben (AD-22).
- Das Sicherheitsprotokoll schreibt auch dann, wenn der Aufrufer abbricht; ein Angreifer kann eine fehlgeschlagene Anmeldung nicht durch Verbindungsabbruch verbergen.
- Die API startet lokal nur noch über HTTPS (über HTTP warf der Antiforgery-Dienst wegen `Secure`-Cookies 500).
- Test „Bucket nicht öffentlich“ verlangt genau `403`; neuer Test für den Fehlerpfad des Fristdienstes.

**Dateien:**
- `src/Zeltlotse.ServiceDefaults/Extensions.cs` – `ClearProviders()` vor `AddOpenTelemetry`.
- `src/Zeltlotse.Api/Betrieb/BetriebSicherheitsprotokoll.cs`, `src/Zeltlotse.SharedKernel/Sicherheitsprotokoll/ISicherheitsprotokoll.cs` – Schreiben unabhängig vom Abbruch des Aufrufers, Vertrag dokumentiert.
- `src/Zeltlotse.Api/Properties/launchSettings.json` – nur Profil `https`, ohne HTTP-Adresse.
- `tests/Zeltlotse.Api.Tests/LogExportTests.cs`, `SicherheitsprotokollTests.cs`, `S3DateiSpeicherTests.cs` – drei neue Tests, eine geschärfte Prüfung.

**Review:** high 0, medium 4, low 25, false 9, maybe-false 4. Gepatcht: medium 2 (Log-Provider, Abbruch im Sicherheitsprotokoll), low 3 (HTTP-Profil, 403-Prüfung, Fristdienst-Test). Zurückgestellt: 1 neu (Crypto-Shredding und Backups, siehe `deferred`), dazu die bekannten drei. Alle übrigen Befunde sind mit Begründung im Review Triage Log verworfen.

**AD-30 (Hinweis des Aufrufers):** Auch in diesem Durchlauf gibt es keine Endpunkte mit Rolle, Organisation oder Zugangs-Token; die Fälle 403/404/Token haben keinen Gegenstand (Design Notes). Beide Endpunkte haben HTTP-Tests gegen echtes PostgreSQL für Erfolg, Antiforgery, Dienst-Kennung, Rate-Limit, Header und Validierung.

**Folge-Review empfohlen:** nein. Folgedurchlauf ohne gepatchten high-Eintrag (gepatcht: high 0, medium 2, low 3).

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` ergibt 0 Warnungen und 0 Fehler. `dotnet test` ergibt 255 von 255 grün (Architektur, SharedKernel, Personen, API; PostgreSQL 18.6 und RustFS per Testcontainers).

**Restrisiken:**
- Ohne gesetzten OTLP-Endpunkt gibt es keine Log-Ausgabe mehr (auch nicht auf der Konsole); lokal zeigt das Aspire-Dashboard die Logs. Der Betrieb braucht einen OTLP-Empfänger.
- Ein Reverse Proxy ohne konfigurierte Adresse (`Zeltlotse:ReverseProxy:Adressen`) führt weiter zu 500 bei ändernden Anfragen.
- Siehe `deferred`.
