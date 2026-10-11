---
title: 'Client-Grundlagen: Komponenten, Autosave, PWA-Hülle'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: '0bd702dc75b482241b5599baedcf4358506a211a'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/DESIGN.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
warnings: [oversized]
deferred:
  - summary: >-
      AntiforgeryZwang setzt den internen Framework-Schlüssel "__AntiforgeryMiddlewareWithEndpointInvoked" von Hand.
    evidence: |-
      Der Schlüssel ist kein öffentlicher Vertrag von ASP.NET Core; benennt ein Framework-Update ihn um, werfen alle Blazor-Formular-Endpunkte. Die Formular-Tests in ClientAuslieferungTests fallen dann um, aber ohne klare Ursache. Stammt aus Story 0-2 (schon in der Baseline vorhanden), nicht aus 0-3.
    location: >-
      src/Zeltlotse.Api/Sicherheit/AntiforgeryZwang.cs:65
    severity: low
---

<intent-contract>

## Intent

**Problem:** Alle Fach-Stories ab 1-1 brauchen dieselben Client-Grundlagen. Dazu gehören ELKW-Darstellungsbausteine, ein einheitlicher Autosave nach AD-19, ein Freizeit-Kontext-Dienst und typisierte API-Clients. Außerdem braucht es eine Web-App unter `/app`, die offline startet und dann die Notfall-Box mit 112 zeigt, sowie öffentliche Seiten unter `/`, die ohne WebAssembly auskommen. Bisher gibt es keinen einzigen Client.

**Approach:** Vier neue Projekte nach dem Quellbaum der Spine: `Zeltlotse.Ui.Komponenten`, `Zeltlotse.Ui`, `Zeltlotse.App` (Blazor-WebAssembly-PWA) und `Zeltlotse.Public` (statisches Server-Rendering). Der API-Host liefert alle unter einer Adresse aus. Testgetrieben: Architektur-, bUnit-, HTTP- (gegen echtes PostgreSQL) und Playwright-Tests entstehen jeweils vor dem Code.

## Boundaries & Constraints

**Always:**
- **Projekte (AD-5, AD-29):**
  - `Zeltlotse.Ui.Komponenten` ist eine Razor Class Library. Sie hat keine Projektreferenz, als Pakete nur `Microsoft.AspNetCore.Components` und `Microsoft.AspNetCore.Components.Web`, und enthält ELKW-CSS und Schrift.
  - `Zeltlotse.Ui` ist eine Razor Class Library und referenziert nur Ui.Komponenten und `Zeltlotse.Api.Contracts`.
  - `Zeltlotse.App` ist eine eigenständige Blazor-WebAssembly-PWA nach der offiziellen PWA-Vorlage, mit Basispfad `/app/`.
  - `Zeltlotse.Public` ist eine Razor Class Library mit statisch servergerenderten Seiten, ohne WebAssembly und ohne JavaScript.
  - Kein Client referenziert ein Modul-Projekt, Modul-Contracts oder SharedKernel. Ausnahme sind die Hosting-Teile, die Public über `FrameworkReference Microsoft.AspNetCore.App` bezieht.
  - Komponenten-Bibliotheken von Drittanbietern sind verboten.
- **Hosting im API-Host:** `/api` bleibt unverändert. `/app/**` liefert die Dateien der Web-App; Pfade ohne Dateiendung fallen auf `/app/index.html` zurück. `/` liefert die öffentlichen Seiten (`MapRazorComponents`, nur statisch). Die gemeinsamen Statikdateien (`_content/...`) werden ausgeliefert. Ein unbekannter Pfad außerhalb von `/api` und `/app` ergibt `404` als HTML-Seite „Diese Seite ist nicht verfügbar.“. Unter `/api` bleibt es bei ProblemDetails.
- **CSP bleibt unverändert** (Wert aus 0-2):
  - keine Inline-Skripte, auch keine Import-Map oder SW-Registrierung im HTML;
  - kein `style=""`-Attribut und kein `<style>`-Block in Razor- oder HTML-Dateien unter `src/`;
  - keine `data:`-Bilder;
  - Balken zeichnen ihre Breite als SVG-Attribut (`<rect width="40%">`).
- **Komponenten (alle in Ui.Komponenten, mit ELKW-Klassen, Wortlaut nach EXPERIENCE.md):** Knopf, Card, Feld, Balken mit Statuswort, Status, Meldung, Inline-Bestätigung, Notfall-Box, App-Leiste mit Wortmarke, App-Rahmen, Seitennavigation, untere Navigation, Offline-Leiste, Offline-Bereich, Leer-Zustand, Speicher-Hinweis, Sprunglink, Ladezustand. Die beobachtbaren Regeln je Baustein stehen in den Design Notes.
  - Jeder Baustein kommt ohne Ereignis-Handler aus (nur HTML-Formulare und Links) oder trägt `[NurInteraktiv]`. Das Attribut liegt in Ui.Komponenten.
  - Ereignisse gehen über Attribut-Weitergabe (`@onclick` am Knopf wird an das `<button>` weitergereicht). Ein Parameter vom Typ `EventCallback` gibt es nur an `[NurInteraktiv]`-Bausteinen.
- **CSS:**
  - Die ELKW-Dateien `elkw-tokens.css`, `elkw-components.css`, `elkw-app.css`, `elkw-nahbar.css` und `elkw-fonts.css` sowie die Sarabun-TTFs mit `OFL.txt` werden unverändert aus `C:\Users\micro\.claude\skills\elkw-design\assets\` übernommen.
  - Dazu kommt eine eigene `zeltlotse.css` mit den `*-dark`-Tokens aus DESIGN.md unter `@media (prefers-color-scheme: dark)`, den Kontrast-Korrekturen (Feldrahmen in `action`) und den Zeltlotse-Ergänzungen mit Präfix `zl-`.
  - Seiten nutzen `<html lang="de" class="elkw" data-farbwelt="09" data-stil="nahbar" data-dichte="arbeiten">`.
- **Autosave (AD-19, AD-29):** Das gilt nur für `AutosaveFeld` (`[NurInteraktiv]`) mit dem Speicher-Dienst in `Zeltlotse.Ui`.
  - Beim Verlassen des Feldes wird gespeichert, und zwar nur, wenn sich der Wert geändert hat.
  - Anfrage: `PATCH <Pfad>` mit dem Körper `{ "version": n, "alterWert": alt, "<feldname>": neu }`.
  - Antwort `200` mit `{ "version": n2, "<feldname>": gespeichert }`: Das Feld übernimmt Wert und Version und zeigt „Gespeichert“ mit dem Textbutton „Rückgängig“. Rückgängig ist ein erneuter PATCH mit dem vorigen Wert und zeigt danach „Gespeichert“ ohne Rückgängig.
  - Antwort `409`: Das Feld zeigt „Inzwischen geändert – neu laden?“ mit dem Textbutton „Neu laden“, der `NeuLaden` auslöst.
  - Netzfehler oder `5xx`: Das Feld zeigt „Nicht gespeichert.“ mit dem Knopf „Erneut versuchen“; der eingegebene Wert bleibt stehen.
  - Der Hinweis verschwindet, sobald ein anderes Autosave-Feld desselben Dienstes den Fokus bekommt oder speichert.
  - Während der Übertragung ist der Knopf deaktiviert.
  - Hinweise haben `role="status"`, Fehler `role="alert"`.
- **Freizeit-Kontext (AD-29):**
  - In `Api.Contracts/Gemeinsam/` liegen `FreizeitKontext(Guid FreizeitId, string Name, Phase Phase, IReadOnlyList<FreizeitRolle> Rollen, IReadOnlyList<FunktionsSchalter> Schalter, IReadOnlyList<string> Erlaubt)`, die Enums `Phase { InVorbereitung, Laeuft, Nachbereitung, Gelaufen }`, `FreizeitRolle { Leitung, Team }` und `FunktionsSchalter { Verpflegung, Finanzen, TeilnehmerZugang }` sowie `Aktionsschluessel` (noch ohne Einträge).
  - `FreizeitKontextDienst.LadenAsync(freizeitId)` ruft `GET /api/freizeiten/{freizeitId}/kontext` auf. Bei `200` liefert er den Kontext, bei `403` oder `404` „nicht verfügbar“.
  - `Darf(kontext, schluessel)` prüft nur `Erlaubt`; der Dienst leitet nie aus Rollen ab.
  - Es gibt keinen Cache und keinen globalen Zustand.
- **API-Clients:**
  - Sie sind handgeschrieben, je Modul, und liegen in `Zeltlotse.Ui`: `BetriebApiClient` (Antiforgery-Token holen, Browser-Fehler melden) und `FreizeitenApiClient` (Kontext).
  - **Transport der App:**
    - Der `AntiforgeryHandler` holt das Token beim ersten ändernden Aufruf einmal je App-Start und setzt `X-Zeltlotse-Antiforgery`. Bei `400` mit `code=antiforgery-ungueltig` holt er es einmal neu und wiederholt die Anfrage.
    - Der Versions-Handler vergleicht `X-Zeltlotse-Version` mit der eigenen Version. Weicht sie ab, zeigt die App die Meldung „Hinweis: Eine neue Version von Zeltlotse ist da.“ mit dem Knopf „Neu laden“.
    - Der Verbindungs-Handler meldet Netzfehler und Erfolg an `IVerbindung`.
  - **Transport von Public:** Ein Handler setzt `X-Zeltlotse-Dienst` (Geheimnis aus `PublicSeitenDienstKennung`, per DI vom Host übergeben) und `X-Zeltlotse-Besucher-Ip` (die `RemoteIpAddress` der laufenden Anfrage). Ohne laufende Anfrage wirft er, statt ohne Besucher-IP zu senden. Basisadresse ist die eigene Adresse des Hosts.
- **Version (AD-5):** `Kopfzeilen.Version = "X-Zeltlotse-Version"`. `ZeltlotseVersion.Aktuell` ist die `AssemblyInformationalVersion` von `Zeltlotse.Api.Contracts`. Der Host setzt den Header an jeder Antwort.
- **PWA-Hülle (AD-18):**
  - Das Manifest hat `start_url` und `scope` = `/app/`.
  - Der Service Worker legt nur App-Dateien in den Cache, nie `/api/**`; er beantwortet `/api/**` nie aus dem Cache. Seine Registrierung steht in einer eigenen JS-Datei.
  - Das Interop-JavaScript liegt in genau einem Modul `wwwroot/js/zeltlotse.js` hinter der C#-Schnittstelle `IBrowser`. Nur deren Umsetzung hängt von `IJSRuntime` ab. Inhalt: Online-Status mit Ereignissen, Zeitpunkt der letzten erfolgreichen API-Antwort in `localStorage` lesen und schreiben, Neu laden mit Aktivieren eines wartenden Service Workers.
  - Offline gilt, wenn `navigator.onLine` falsch ist oder der letzte API-Aufruf mit einem Netzfehler endete (bis zum nächsten Erfolg). Dann zeigt die App:
    - die Offline-Leiste „Offline – zuletzt aktualisiert HH:MM Uhr“ (Europe/Berlin), oder nur „Offline“, wenn es noch keinen Zeitpunkt gibt;
    - die Notfall-Box;
    - die übrigen Inhalte ausgegraut mit „Offline nicht verfügbar.“.
- **Browser-Fehler:**
  - Eine `ErrorBoundary` im Layout der App meldet `POST /api/betrieb/fehler` mit dem Typnamen der Ausnahme, dem Pfad ohne Abfrage und Fragment und einer neu erzeugten Trace-ID aus 32 Hex-Zeichen.
  - Danach zeigt sie „Fehler: Die Ansicht konnte nicht angezeigt werden.“ mit dem Knopf „Neu laden“.
  - Scheitert die Meldung selbst, wird sie verworfen.
- **Antiforgery außerhalb von `/api`:**
  - `AntiforgeryZwang` akzeptiert für ändernde Anfragen an Endpunkte mit Antiforgery-Metadaten außerhalb von `/api` auch das Formularfeld-Token von Blazor (`IAntiforgery.IsRequestValidAsync`).
  - Unter `/api` bleibt es beim reinen Header; es gibt keine weitere Ausnahme.
- Test vor dem Code (AD-30). Paketversionen nur zentral, bUnit 2.11.3, Microsoft.Playwright 1.63.0, Deque.AxeCore.Playwright 4.13.0. 0 Warnungen.

**Never:**
- Kein Login, keine Sitzung, kein Kontext-Endpunkt auf dem Server (1-3), keine Fachseiten oder Fach-Endpunkte (Kontext-Endpunkt und Fachseiten kommen ab 1-1).
- Keine Offline-Daten, kein IndexedDB oder WebCrypto, kein `storage.persist()` und kein Push (V6).
- Kein `doku/` (0-4).
- Keine Dialoge, Pop-ups, Drawer, Toasts oder Spinner; keine Icons in der Navigation; keine Pills oder Badges.
- Kein globaler Zustandsspeicher. Kein Code-Generator für API-Clients.
- Die CSP wird nicht gelockert: kein `'unsafe-inline'`, keine Hashes, keine Fremdquellen.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Öffentliche Startseite | `GET /` | `200` HTML, `lang="de"`, ELKW-Attribute, Wortmarke „Zeltlotse“, Sprunglink, kein `<script`, CSS unter `_content/Zeltlotse.Ui.Komponenten/` liefert `200 text/css` | – |
| Unbekannte Seite | `GET /gibt-es-nicht` | `404` HTML „Diese Seite ist nicht verfügbar.“ mit Sicherheits-Headern | `/api/gibt-es-nicht` → weiter `404` ProblemDetails |
| Web-App | `GET /app/`, `/app/tief/unten` | `200` `index.html`, `<base href="/app/">`, Manifest-Link, nur `<script src=…>` | `/app/fehlt.js` → `404` |
| PWA-Dateien | Manifest, `service-worker.js`, `_framework/…` | `200`; Manifest `start_url`/`scope` = `/app/` | – |
| Version | jede Antwort (`/`, `/app/`, `/api/…`, Fehler) | Header `X-Zeltlotse-Version` = `ZeltlotseVersion.Aktuell` | – |
| Public ruft API | Public-Client aus einer laufenden Anfrage mit IP A, `POST /api/betrieb/fehler` ohne Antiforgery | `204`; die Zugriffsbegrenzung zählt für IP A | ohne laufende Anfrage → Ausnahme, kein Aufruf |
| Formular außerhalb `/api` | Test-Endpunkt (nur `Testing`) mit Formular-Token / ohne Token | `200` / `400` `antiforgery-ungueltig` | `/api` mit Formular-Token ohne Header → `400` |
| Autosave gespeichert | Feld geändert, Server `200` | Wert und Version übernommen, „Gespeichert · Rückgängig“; Rückgängig sendet PATCH mit vertauschten Werten | – |
| Autosave Konflikt | Server `409` ProblemDetails | „Inzwischen geändert – neu laden?“ + „Neu laden“ | – |
| Autosave gescheitert | Netzfehler oder `500` | „Nicht gespeichert.“ + „Erneut versuchen“, Wert bleibt | erneuter Versuch sendet denselben Körper |
| Antiforgery der App | erster PATCH; danach Token abgelaufen (`400 antiforgery-ungueltig`) | ein Token-Abruf je App-Start; bei Ablauf einmal neu holen und wiederholen | zweites `400` → Fehler an den Aufrufer |
| Kontext | `200` / `403` / `404` | Kontext / „nicht verfügbar“ / „nicht verfügbar“ | – |
| Neue Version | API-Antwort mit anderer Version | Meldung „Hinweis: Eine neue Version von Zeltlotse ist da.“ + „Neu laden“ | – |
| Offline-Start | App einmal online geladen, dann Browser offline, Neuladen | Seite lädt aus dem Service Worker; Offline-Leiste, Notfall-Box mit Link `tel:112` „Notruf 112“, Rest „Offline nicht verfügbar.“; keine CSP-Verletzung; axe WCAG 2.2 AA ohne Verstoß | `fetch('/api/…')` scheitert offline (kein Cache) |

</intent-contract>

## Code Map

- `src/Zeltlotse.Api/Program.cs` -- Pipeline:
  - L74 `UseWebSicherheitAmAnfang` (Header, Proxy);
  - L75 `UseFehlerform` (`UseExceptionHandler().UseStatusCodePages()`, liefert für alle Pfade ProblemDetails, auch für 404);
  - L76 `UseRouting`;
  - L77 `UseWebSicherheitNachRouting` (Cookies, RateLimiter, `AntiforgeryZwang`);
  - L79 `MapDefaultEndpoints`, L80 `MapZeltlotseEndpoints` (Gruppe `/api`), L81-87 Test-Endpunkte (`IZusatzEndpunkte`, nur `Testing`).

  Noch nicht vorhanden sind Statikdateien, `MapRazorComponents`, Fallback und Versions-Header. Die neue Auslieferung kommt hierher. Die Statuscode-Seiten müssen für Nicht-`/api`-Pfade HTML liefern.
- `src/Zeltlotse.Api/Sicherheit/AntiforgeryZwang.cs:100-111` -- Prüfregel: ändernd UND (`/api` ODER `IAntiforgeryMetadata.RequiresValidation`); nur Header, Dienst-Kennung als Ausnahme. L134 setzt das Middleware-Flag. Hier kommt der Formular-Token außerhalb `/api` dazu.
- `src/Zeltlotse.Api/Sicherheit/PublicSeitenDienstKennung.cs` -- Singleton, `string Geheimnis`, `Pruefen`; Quelle für den Handler von Public.
- `src/Zeltlotse.Api/Sicherheit/SicherheitsHeader.cs` -- CSP-Text (unverändert lassen), `ApiPfad.Ist`; Ort bzw. Vorbild für den Versions-Header.
- `src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs` -- Partition: Besucher-IP nur bei gültiger Dienst-Kennung.
- `src/Zeltlotse.Api/Betrieb/BetriebEndpunkte.cs` -- `GET /api/betrieb/antiforgery` → `AntiforgeryAntwort(Token)`. `POST /api/betrieb/fehler` mit `BrowserFehler(Fehlerart, Seite, TraceId)`, alle Pflicht: `Seite` muss mit `/` beginnen, `TraceId` `^[A-Za-z0-9:-]+$`.
- `src/Zeltlotse.Api/Fehler/AusnahmeBehandlung.cs` -- ProblemDetails mit `traceId`.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/Kopfzeilen.cs`, `Problemcodes.cs` -- Konstanten (`Antiforgery`, `Dienst`, `BesucherIp`; `antiforgery-ungueltig` usw.); hier `Version`, `ZeltlotseVersion`, Kontext-Typen, `Konflikt = "konflikt"` ergänzen.
- `src/Zeltlotse.Api/Zeltlotse.Api.csproj` -- Web-SDK; hier die Referenzen auf `Zeltlotse.App` und `Zeltlotse.Public` sowie das Paket `Microsoft.AspNetCore.Components.WebAssembly.Server` ergänzen.
- `Directory.Build.props` -- `net10.0`, Warnungen als Fehler (gilt auch für WASM). `Directory.Packages.props` -- zentrale Versionen, transitive Fixierung; hier neue `PackageVersion` eintragen. `Zeltlotse.slnx` -- Ordner `/src/`, `/tests/`.
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` -- `(verbindung, mitZustellDienst)`, `BaseAddress https://localhost`, `Einstellungen`, `ZusatzDienste`, `X-Test-Absender-Ip`. Für den Public-Client im Test den primären Handler auf `TestServer.CreateHandler()` umstellen.
- `tests/Zeltlotse.Api.Tests/` -- Muster `BrowserFehlerTests.cs` (AssemblyFixture `PostgresContainer`, `TestHelfer.AntiforgeryTokenAsync`, `TestEndpunkte` über `IZusatzEndpunkte`).
- `tests/Zeltlotse.ArchitectureTests/` -- `Loesung.cs` lädt nur referenzierte `Zeltlotse.*.dll`, deshalb Projektreferenzen auf die vier Clients ergänzen. `ProjektDateiRegeln.cs`/`ProjektDateiTests.cs` sind der csproj-Scanner mit Fixtures `Negativ/*.csproj.xml`. `AbhaengigkeitsRegeln.cs` folgt dem ArchUnit-Stil `FollowCustomCondition`; C#-Fixtures liegen unter `Negativ/`.
- `C:\Users\micro\.claude\skills\elkw-design\assets\` -- maßgebliche ELKW-CSS und Schriften (die Kopien in `mockups/` sind älter). Klassen: `elkw-appbar`, `elkw-app__*`, `elkw-sidenav`, `elkw-btn` (`--outline`, `--text`, `--noarrow`, `--block`), `elkw-field`, `elkw-field-error`, `elkw-card`, `elkw-surface`, `elkw-alert--*`, `elkw-status--*`, `elkw-empty`, `elkw-skiplink`, `elkw-loading`.
- `_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/mockups/key-dunkelmodus.html` (L84ff., `.zl-dark`), `key-notfall.html` -- Vorbild für Dunkel-Tokens und die `zl-`-Bausteine (`zl-notfall`, `zl-offline`, `zl-bottomnav`, `zl-bar`, `zl-saved`). Nur lesen, nicht 1:1 übernehmen; DESIGN.md hat Vorrang.
- Umgebung geprüft: SDK 10.0.401, wasm-tools installiert, Vorlage `blazorwasm --pwa` vorhanden, Docker läuft, Playwright-Chromium unter `%LOCALAPPDATA%\ms-playwright`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.ArchitectureTests/` -- ZUERST neue Regeln, je mit Negativ-Fixture:
  - Clients referenzieren weder Modul-Projekte noch Modul-Contracts noch SharedKernel.
  - Ui.Komponenten hat nur die zwei erlaubten Pakete und keine Projektreferenz.
  - Clients haben keine Pakete außerhalb von `Microsoft.*`.
  - Public nutzt keinen `[NurInteraktiv]`-Typ.
  - Ein Baustein mit `EventCallback`-Parameter trägt `[NurInteraktiv]`.
  - Nur die Umsetzung von `IBrowser` hängt von `IJSRuntime` ab.
  - Quelltext-Scan in Razor- und HTML-Dateien unter `src/`: kein `style=`, kein `<style`, kein `<script>` ohne `src`; `elkw-`/`zl-`-Klassen nur in Ui.Komponenten.
  - Dazu Projektreferenzen auf die Clients ergänzen.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST (neues Projekt, bUnit): je Baustein aus Ui.Komponenten die Regeln aus den Design Notes; `AutosaveFeld` und Speicher-Dienst für alle Matrix-Zeilen mit einem nachgebildeten `HttpMessageHandler`, der den Körper prüft; `FreizeitKontextDienst`; die drei App-Handler; App-Layout online, offline, Fehlergrenze und neue Version (mit nachgebildetem `IBrowser`, `FakeTimeProvider`).
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: HTTP-Tests gegen echtes PostgreSQL für die Matrix-Zeilen Startseite, unbekannte Seite, Web-App, PWA-Dateien, Version, Public ruft API und Formular außerhalb `/api`; die Sicherheits-Header an `/`, `/app/` und der 404-Seite.
- `tests/Zeltlotse.E2E.Tests/` -- ZUERST (neues Projekt):
  - Eine Fixture veröffentlicht den API-Host einmal je Lauf (`dotnet publish -c Release` in einen Temp-Ordner, weil das Asset-Manifest des Service Workers nur beim Veröffentlichen entsteht) und startet die DLL als Prozess (`Testing`, Kestrel `http://127.0.0.1:<frei>`, PostgreSQL per Testcontainers, Test-Schlüssel per Umgebungsvariablen).
  - Playwright-Test für den Offline-Start in Handygröße 375×812, hell und dunkel (`ColorScheme`): Matrix-Zeile „Offline-Start“ mit axe-Tags wcag2a/2aa/21a/21aa/22aa und Konsole ohne „Content Security Policy“-Meldung.
- `src/Zeltlotse.Ui.Komponenten/` -- Bausteine, `NurInteraktivAttribute`, `wwwroot/css` (ELKW-Kopien und `zeltlotse.css`), `wwwroot/fonts`.
- `src/Zeltlotse.Ui/` -- API-Clients, `AntiforgeryHandler`, Versions- und Verbindungs-Handler, `IVerbindung`, Autosave-Dienst und `AutosaveFeld`, `FreizeitKontextDienst`, Registrierungserweiterung.
- `src/Zeltlotse.App/` -- PWA aus der Vorlage (bereinigt, keine Inline-Skripte, Basispfad `/app/`), Layout mit App-Rahmen, Offline-Leiste, Fehlergrenze und Versions-Meldung, Startseite „Zeltlotse“ mit dem Satz „Noch keine Freizeit geöffnet.“, `IBrowser` mit `zeltlotse.js`, Service Worker ohne `/api`.
- `src/Zeltlotse.Public/` -- Layout (eine Spalte, höchstens 75ch, oben Name der Organisation, Vorgabe „Zeltlotse“), Startseite, Seite „nicht verfügbar“, Transport-Handler, `AddPublicSeiten(...)`/`MapPublicSeiten()`.
- `src/Zeltlotse.Api/` -- Auslieferung von `/app` und `/` samt Fallback und HTML-404 außerhalb `/api`; Versions-Header; Formular-Token in `AntiforgeryZwang`; Registrierung von Public mit der Dienst-Kennung.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/` -- `Kopfzeilen.Version`, `ZeltlotseVersion`, `FreizeitKontext`, Enums, `Aktionsschluessel`, `Problemcodes.Konflikt`.
- `Directory.Packages.props`, `Zeltlotse.slnx` -- neue Pakete und Projekte.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün: Architektur, SharedKernel, Personen, API, Ui (bUnit) und E2E (Playwright gegen den veröffentlichten Host).
- Given ein Client-Fixture mit einer Referenz auf ein Modul-Projekt, ein Public-Typ mit einem `[NurInteraktiv]`-Baustein oder eine Razor-Datei mit `style=`, when die Architekturtests laufen, then schlagen sie fehl.
- Given der Host läuft unter Aspire, when `/`, `/app/` und `/api/betrieb/antiforgery` im Browser geöffnet werden, then laden alle ohne CSP-Meldung in der Konsole, und `dotnet build Zeltlotse.slnx` ergibt 0 Warnungen.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 41 findings — high 0, medium 10, low 26, false 4, maybe-false 1
- findings:
  - `[medium]` `[patch]` (vgap) Handler-Kette aus `AddAppDienste` ungetestet; Wegfall eines Handlers bliebe unbemerkt — innerster Handler injizierbar, Test über den aufgelösten `BetriebApiClient` (offline, neue Version, Antiforgery-Header).
  - `[low]` `[patch]` (vgap) Test zum Vorgabevergleich des `VersionsHandler` baut keinen Handler — Test baut den Handler ohne Version und prüft: gleiche Version meldet nichts.
  - `[low]` `[reject]` (vgap) E2E akzeptiert fehlenden Zeitpunkt, `localStorage`-Pfad ungeprüft — heute ruft keine App-Seite die API auf, der Pfad ist im Produkt noch nicht erreichbar; mit der ersten API-nutzenden Seite (1-1) prüfen; der Fix bräuchte zusätzliche Test-Infrastruktur.
  - `[low]` `[patch]` (blind) Jeder Fehlerstatus außerhalb `/api` wird zur 404-Seite — 500 und 429 schreiben einen Körper und sind nicht betroffen, wohl aber z. B. 405; nur 404 wird neu ausgeführt, sonst ProblemDetails.
  - `[medium]` `[patch]` (blind) Timeout lässt das Autosave-Feld hängen (`TaskCanceledException` ungefangen, kein `finally`) — Timeout als Gescheitert, `finally` setzt die Übertragung zurück, Verbindungs-Handler zählt den Timeout als Netzfehler.
  - `[medium]` `[patch]` (blind) Eingaben während des Speicherns werden überschrieben — Eingabe nur überschreiben, wenn sie dem gesendeten Wert entspricht, sonst danach speichern.
  - `[medium]` `[patch]` (blind) Fehler- und Konflikt-Hinweis verschwinden beim Fokus auf ein anderes Feld — nur „Gespeichert“ verschwindet.
  - `[medium]` `[patch]` (blind) Fehlergrenze ruft nie `Recover()`; nach einem Fehler zeigen alle Seiten die Fehlermeldung — `Recover()` beim Seitenwechsel.
  - `[low]` `[reject]` (blind) Kein `#blazor-error-ui`, Fehler beim Start der Verbindung außerhalb der Fehlergrenze — nur bei scheiterndem Import des eigenen JS-Moduls; Fix bräuchte zusätzliche Zweige und CSS.
  - `[low]` `[reject]` (blind) Versionserkennung hängt am Commit in der `InformationalVersion` (Build ohne `.git`), Startseite ruft keine API — Hosting und Container-Build sind in der Spine zurückgestellt; die erste API-nutzende Seite (1-1) liefert den Vergleich.
  - `[low]` `[reject]` (blind) Jede API-Antwort zeichnet das Layout neu und schreibt `localStorage` — bei den Mengen einer Freizeit vernachlässigbar; Fix wären zusätzliche Zweige.
  - `[low]` `[patch]` (blind) Fehlergrenze verliert die Konsolen-Protokollierung von `ErrorBoundary` — `base.OnErrorAsync` aufgerufen.
  - `[low]` `[patch]` (blind) Fokus geht nach „Ja“ der Inline-Bestätigung verloren — Fokus zurück auf den Auslöser.
  - `[low]` `[reject]` (blind) Feldname `version`/`alterWert` bringt den Körper zum Absturz — AD-19 reserviert diese Namen im Körper; lauter Fehler bei Fehlbenutzung; Fix wäre ein Wächter.
  - `[low]` `[reject]` (blind) App-Pfade mit Punkt im letzten Segment ergeben 404 — Routen nutzen Kennungen ohne Punkt; Fix wäre eine andere Rückfall-Logik.
  - `[low]` `[reject]` (blind) Formular-Token außerhalb `/api` liest anonyme Formularkörper — von der Intent für Blazor-Formulare verlangt; Kestrel-Grenzen gelten; Fix wäre neue Grenzlogik.
  - `[false]` `[reject]` (blind) Keine Behandlung von 401 im Client — es gibt noch keine Sitzung (1-1); kein Endpunkt antwortet mit 401.
  - `[medium]` `[patch]` (edge) Timeout in `AutosaveDienst` ungefangen — gleiche Ursache wie blind „Timeout“, mitbehoben.
  - `[medium]` `[patch]` (edge) `SendenAsync` ohne `try/finally` — gleiche Ursache, mitbehoben.
  - `[medium]` `[patch]` (edge) Eingabe während laufendem PATCH — gleiche Ursache wie blind „Eingaben überschrieben“, mitbehoben.
  - `[low]` `[patch]` (edge) „Erneut versuchen“ sendet nach einer Änderung den alten Wert — Wiederholung nutzt die aktuelle Eingabe.
  - `[medium]` `[patch]` (edge) Gescheitert verschwindet beim Fokus auf ein anderes Feld — gleiche Ursache wie blind, mitbehoben.
  - `[low]` `[reject]` (edge) Konflikt ohne gesetztes `NeuLaden` oder mit gleichen Werten bleibt hängen — Fehlbenutzung durch die Seite; ein Konflikt bringt immer eine neue Version; Fix wäre ein Zweig.
  - `[low]` `[reject]` (edge) Feldname `version`/`alterWert` — gleiche Ursache wie blind, verworfen.
  - `[false]` `[reject]` (edge) 2xx ohne Körper (204) gilt als gescheitert — AD-19 verlangt `200` mit `{ version, <feld> }`.
  - `[low]` `[patch]` (edge) Serverfehler beim Token-Abruf gilt als Netzfehler, App zeigt offline — nur `HttpRequestException` ohne `StatusCode` zählt als Netzfehler.
  - `[medium]` `[patch]` (edge) Timeout schaltet die App nicht offline — gleiche Ursache wie blind „Timeout“, mitbehoben.
  - `[low]` `[reject]` (edge) `ErfolgMeldenAsync` wirft bei JS-Fehler — nur bei kaputtem eigenem JS-Modul; Fix wäre ein Zweig.
  - `[low]` `[reject]` (edge) Gescheiterter Start in `AppVerbindung` bleibt zwischengespeichert — gleiche Ursache wie blind „blazor-error-ui“, verworfen.
  - `[low]` `[reject]` (edge) `StartenAsync` wirft im Layout außerhalb der Fehlergrenze — gleiche Ursache, verworfen.
  - `[low]` `[reject]` (edge) Gescheiterter Modul-Import in `JsBrowser` bleibt zwischengespeichert — gleiche Ursache, verworfen.
  - `[low]` `[reject]` (edge) Andere `JSException` beim Entsorgen der Beobachtung — nur beim Abbau der Seite; Fix wäre ein Zweig.
  - `[low]` `[reject]` (edge) „Neu laden“, während der neue Service Worker noch installiert — ein zweites „Neu laden“ hilft; Fix wäre Warte-Logik.
  - `[low]` `[patch]` (edge) Statuscodes 401/403/405/429 ohne Körper werden zur 404-Seite — gleiche Ursache wie blind, mitbehoben.
  - `[low]` `[reject]` (edge) App-Pfad mit Punkt — gleiche Ursache wie blind, verworfen.
  - `[maybe-false]` `[reject]` (edge) Kaputter Formularkörper außerhalb `/api` ergibt 500 statt 400 — klären mit einem Test mit fehlerhaftem `multipart`-Körper; wäre nur low.
  - `[low]` `[reject]` (edge) Unix-Socket oder nur HTTPS-Bindung für die eigene Adresse von Public — Betrieb nach AD-23 mit HTTP hinter dem Proxy; `Basisadresse` ist konfigurierbar.
  - `[low]` `[reject]` (edge) Deaktivierter Link-Knopf führt `@onclick` aus — kein Aufrufer nutzt deaktivierte Links mit Handler; Fix wäre ein Zweig.
  - `[low]` `[patch]` (edge) Fokus nach „Ja“ — gleiche Ursache wie blind, mitbehoben.
  - `[false]` `[reject]` (intent) AD-30-Pflichtfälle 403/404/Token nur clientseitig, Token-Tests betreffen Antiforgery — 0-3 legt keinen Endpunkt der Arten Organisation/Freizeit, persönlich oder Token an (Kontext-Endpunkt 1-3, `stories.yaml`: keine Fachfunktion); alle neuen Auslieferungen haben HTTP-Tests gegen echtes PostgreSQL.
  - `[false]` `[reject]` (intent) „Test zuerst“ am Diff nicht nachprüfbar — Prozessaussage, kein Defekt.

### 2026-10-11 — Review pass
- verdicts: 42 findings — high 0, medium 1, low 35, false 6, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) Offline-Zustand nach einem Netzfehler bleibt bis zur nächsten Antwort; der ausgegraute Bereich schickt selbst keine Anfrage — so verlangt es die Intent („bis zum nächsten Erfolg“). Heute ruft keine App-Seite regelmäßig die API auf, Neuladen hilft. Ein Probe-Mechanismus wäre neue Logik; mit der ersten API-nutzenden Seite (1-1) erneut prüfen.
  - `[low]` `[reject]` (blind) „Neu laden“ erzwingt kein Service-Worker-Update — carried: wie „Neu laden, während der neue Service Worker noch installiert“ (verworfen).
  - `[low]` `[reject]` (blind) Gescheiterter Interop-Start legt die App ohne Fehleranzeige lahm (kein `#blazor-error-ui`, zwischengespeicherte Tasks) — carried: wie „Kein `#blazor-error-ui`“ (verworfen).
  - `[low]` `[reject]` (blind) `AutosaveFeld` überschreibt ungespeicherte Eingaben, wenn die Seite neue Parameter übergibt — das Feld meldet keine Version nach außen; neue Werte kommen nur über „Neu laden“, wie die Intent es will. Kein Aufrufer existiert; Fix wäre ein zusätzlicher Zustand.
  - `[low]` `[reject]` (blind) Jede API-Antwort zeichnet das Layout neu und schreibt `localStorage` — carried (verworfen).
  - `[low]` `[reject]` (blind) Scheiternder `ErfolgMeldenAsync` macht eine gute Antwort zur Ausnahme — carried: wie „`ErfolgMeldenAsync` wirft bei JS-Fehler“ (verworfen).
  - `[low]` `[reject]` (blind) `X-Zeltlotse-Version` verrät jedem Besucher den genauen Commit — die Intent verlangt den Header an jeder Antwort (AD-5); ein Fix widerspräche ihr.
  - `[low]` `[defer]` (blind) `AntiforgeryZwang` hängt am internen Framework-Schlüssel `__AntiforgeryMiddlewareWithEndpointInvoked` — schon in der Baseline aus 0-2 vorhanden; die Tests würden ein Umbenennen bemerken; zurückgestellt.
  - `[low]` `[reject]` (blind) `AddZeltlotseUi` erzeugt `HttpClient`-Instanzen, die nie entsorgt werden — `disposeHandler: false`, der Primär-Handler gehört dem Singleton; den Rest räumt der GC ohne messbaren Schaden ab; Fix wäre eine andere Registrierung.
  - `[low]` `[reject]` (blind) Loopback-Adresse bricht bei reiner HTTPS-Bindung — carried: wie „Unix-Socket oder nur HTTPS-Bindung“ (verworfen).
  - `[low]` `[patch]` (blind) Kein `<link rel="icon">`: jeder Besuch fragt `/favicon.ico` an und bekommt die 404-Seite — Icon-Link in `index.html` der App und in `Wurzel.razor` von Public; HTTP-Test prüft Link und Auslieferung. Maskable-Icon und `theme-color` sind kosmetisch und verworfen.
  - `[low]` `[patch]` (blind) Wiederholen eines gescheiterten Rückgängig ist ungetestet; Klick auf „Rückgängig“ direkt nach dem Tippen kann verschluckt werden — bUnit-Test für „Erneut versuchen“ nach gescheitertem Rückgängig ergänzt (Verhalten war schon richtig). Das verschluckte Klicken ist verworfen: Der Blur speichert die neue Eingabe, danach steht ein frisches „Rückgängig“ bereit.
  - `[low]` `[reject]` (edge) App-Pfad mit Punkt im letzten Segment ergibt 404 — carried (verworfen).
  - `[low]` `[reject]` (edge) `/APP/…` (andere Schreibweise) liefert `index.html`, `base href` passt nicht — niemand tippt das; die Statikdatei-Routen sind ebenfalls unabhängig von Groß- und Kleinschreibung, ein vollständiger Fix wäre mehr als eine direkte Korrektur.
  - `[low]` `[reject]` (edge) Gescheiterter Start in `AppVerbindung` bleibt zwischengespeichert — carried (verworfen).
  - `[low]` `[patch]` (edge) `ZuletztAktualisiert ??= await …`: Ein Erfolg während des Lesens wird vom älteren gespeicherten Zeitpunkt überschrieben — erst lesen, dann zuweisen; bUnit-Test mit angehaltenem Lesen.
  - `[low]` `[patch]` (edge) Online-Wechsel zwischen `IstOnlineAsync` und dem Start der Beobachtung geht verloren — erst beobachten, dann lesen; Test prüft die Reihenfolge.
  - `[low]` `[reject]` (edge) `ErfolgMeldenAsync` wirft nach erfolgreicher Antwort — carried (verworfen).
  - `[low]` `[reject]` (edge) 502/503/504 vom Proxy gelten als „online“ — die Intent definiert offline über Netzfehler; ein ausgefallenes Backend hinter dem Proxy ist kein Alltag, jede Fachfunktion zeigt dann ihren Fehler; Fix wäre ein zusätzlicher Zweig.
  - `[low]` `[reject]` (edge) Gescheiterter Modul-Import in `JsBrowser` bleibt zwischengespeichert — carried (verworfen).
  - `[low]` `[reject]` (edge) `DotNetObjectReference` bleibt hängen, wenn `onlineBeobachten` wirft — nur bei kaputtem eigenem JS-Modul; Fix wäre ein zusätzlicher try/catch.
  - `[low]` `[reject]` (edge) `DisposeAsync` wirft bei gescheitertem Modul — carried: wie „Andere `JSException` beim Entsorgen“ (verworfen).
  - `[low]` `[reject]` (edge) Neu laden ohne wartenden Service Worker lädt die alte App — carried (verworfen).
  - `[low]` `[reject]` (edge) Offline-Aufruf von `/app` ohne Schrägstrich liegt außerhalb des SW-Bereichs — `start_url` und Weiterleitung führen auf `/app/`; ein größerer Bereich verlangte, den Service Worker zu verlegen.
  - `[low]` `[patch]` (edge) `Seite` der Fehlermeldung enthält Zeichen, die die API ablehnt (z. B. `:`, `;`, `@`, `+`, `!`), die Meldung wird still verworfen — unerlaubte Zeichen werden zu `_`, Länge auf 2000 gekürzt; Theorie-Test.
  - `[low]` `[patch]` (edge) Typname über 100 Zeichen wird von der API abgelehnt — gleiche Ursache (Fehlergrenze hält die Formprüfung der API nicht ein), mitbehoben: auf 100 gekürzt; Test.
  - `[low]` `[reject]` (edge) Feldname `version`/`alterWert` — carried (verworfen).
  - `[false]` `[reject]` (edge) 2xx ohne Körper (204) gilt als gescheitert — carried: AD-19 verlangt `200` mit Körper.
  - `[low]` `[reject]` (edge) Andere 4xx (400, 403, 404, 422) zeigen „Erneut versuchen“ — heute gibt es keinen AD-19-Endpunkt; ein eigener Ergebnistyp wäre neue Logik; mit dem ersten bearbeitbaren Endpunkt festlegen.
  - `[low]` `[reject]` (edge) Neue Parameter während eines laufenden PATCH, die alte Antwort überschreibt sie — kein Aufrufer existiert; Fix wäre ein Generationszähler.
  - `[low]` `[reject]` (edge) Rückgängig oder Erneut versuchen während `_uebertraegt` — der Knopf ist schon deaktiviert gerendert, bevor das nächste Ereignis kommt; ein Wächter schützte einen nicht gezeigten Zustand.
  - `[low]` `[reject]` (edge) Deaktivierter Link-Knopf führt `@onclick` aus — carried (verworfen).
  - `[low]` `[reject]` (edge) HTTPS-Wildcard wird zu `127.0.0.1`, TLS passt nicht — carried (verworfen).
  - `[medium]` `[patch]` (vgap) `PublicApiTransport.EigeneAdresse` läuft in keinem Test — `InternalsVisibleTo` für `Zeltlotse.Api.Tests` in Public; Theorie über `+`, `*`, `0.0.0.0`, `[::]`, `localhost`, HTTP vor HTTPS und leere Adressliste.
  - `[low]` `[patch]` (vgap) Methodenprüfung der Statuscode-Seiten ungetestet — HTTP-Tests: POST/PUT/DELETE an `/gibt-es-nicht` ergeben 405 mit `Allow: GET, HEAD` als ProblemDetails (nicht die HTML-Seite), HEAD ergibt 404 mit Sicherheits-Headern.
  - `[low]` `[reject]` (vgap) Nachricht `aktivieren` zwischen Seite und Service Worker ungetestet — ein echter Test bräuchte zwei veröffentlichte Stände im E2E-Aufbau; gehört zum verworfenen „Neu laden“-Pfad.
  - `[false]` `[reject]` (intent) AD-30-Fälle „falsche Rolle 403“ und „fremde Organisation 404“ fehlen — carried: 0-3 legt keinen Endpunkt mit Rolle oder Organisation an; die Intent verbietet ihn.
  - `[low]` `[patch]` (intent) Die Antiforgery-Kette der App läuft nie gegen den echten Host — zwei HTTP-Tests gegen echtes PostgreSQL: `AntiforgeryHandler` mit Cookies holt das Token einmal, nach Ablauf einmal neu (204); ohne Cookie zweimal 400, der zweite geht als `HttpRequestException` an den Aufrufer. Beide waren sofort grün (Lücke, kein Defekt).
  - `[false]` `[reject]` (intent) Kontext 200/403/404 nur gegen einen nachgebildeten Server — der Kontext-Endpunkt ist laut Intent verboten (kommt mit 1-3); es gibt keinen echten Server, gegen den man testen könnte.
  - `[false]` `[reject]` (intent) Autosave nur gegen einen nachgebildeten Server — die Intent verbietet Fach-Endpunkte; kein PATCH-Endpunkt existiert.
  - `[false]` `[reject]` (intent) Server- und Client-Hälfte der Versionserkennung nie zusammen getestet — der echte Host sendet immer die passende Version; beide Hälften sind je für sich gegen ihren Vertrag getestet.
  - `[false]` `[reject]` (intent) „Test zuerst“ am Diff nicht nachprüfbar — carried: Prozessaussage, kein Defekt.

## Design Notes

- **Baustein-Regeln (bUnit-Prüfpunkte):**
  - **Knopf:** Arten Standard, Outline und Text. Der Pfeil erscheint nur mit `Ansichtswechsel=true`. Mit `Href` wird er ein `<a>`, sonst ein `<button type=…>`. `Deaktiviert` setzt `disabled`. Nicht erkannte Attribute (auch `@onclick`) landen am Element.
  - **Feld:** sichtbares Label über dem Feld, Hinweis darunter; der Fehler „Fehler: …“ setzt `aria-invalid` und `aria-describedby`.
  - **Balken:** Kopfzeile mit Bezeichnung und Zahl, SVG mit `aria-hidden="true"`, Statuswort mit Quadrat, Arten normal, Warnung, Fehler und groß.
  - **Status:** Wort plus Quadrat; Arten offen, aktiv, Erfolg, Warnung, Fehler.
  - **Meldung:** Titel mit Doppelpunkt; `role="alert"` bei Fehler, sonst `role="status"`.
  - **Inline-Bestätigung** (`[NurInteraktiv]`):
    - Der Auslöser klappt an Ort und Stelle auf: Satz, „Ja, …“ ohne Pfeil, „Nein“ als Outline.
    - Der Fokus springt auf „Nein“. „Nein“ oder Escape klappen zu und geben den Fokus zurück an den Auslöser.
  - **Notfall-Box:** roter Balken, Titel „Im Notfall:“, ein Satz, optional der Knopf „Notfallhilfe öffnen“ (nur mit `Ziel`), Link `tel:112` „Notruf 112“; Knopf und Link mindestens 44 px hoch.
  - **App-Leiste:** Wortmarke „Zeltlotse“ als Link, optional der Name der Freizeit, optional der Eintrag „Notfall“ mit rotem Quadrat.
  - **Untere Navigation:** fünf Punkte, nur Text, `aria-current` am aktiven Punkt. Die Seitennavigation hat Gruppen mit Dachzeile, `aria-current` und einen abgesetzten Punkt am Ende.
  - **Offline-Leiste:** Text als Parameter.
  - **Offline-Bereich:** offline mit `inert`, 50 % Deckkraft per Klasse und dem Satz „Offline nicht verfügbar.“.
  - **Leer-Zustand:** Satz plus Knopf.
  - **Speicher-Hinweis** (`[NurInteraktiv]`): Zustände Gespeichert (mit oder ohne Rückgängig), Konflikt und Gescheitert.
  - **Sprunglink:** „Zum Inhalt springen“.
  - **Ladezustand:** „Wird geladen …“ mit `aria-busy`.
- **AD-30-Pflichtfälle:** In 0-3 entsteht kein Endpunkt der Arten Organisation/Freizeit, persönlich oder Token. Neu sind nur anonyme Auslieferungen (`/`, `/app/**`), und der Kontext-Endpunkt kommt mit 1-3. Die Fälle 403/404/Token haben keinen Gegenstand. Stattdessen prüfen HTTP-Tests gegen echtes PostgreSQL jede neue Auslieferung samt Headern, und der Kontext-Dienst ist gegen 200/403/404 getestet.
- **Doku-Pflicht:** Der Ordner `doku/`, die Demo-Daten und die Screenshot-Pipeline entstehen erst in 0-4 (laut `stories.yaml` und ohne Doku-Auftrag für 0-3). 0-3 baut keine Fachoberfläche. Die Screenshots von App-Hülle und Offline-Ansicht nimmt 0-4 mit auf.
- **Offene Frage aus 0-2 (gemeinsamer Rate-Limit-Topf):**
  - Die App holt das Antiforgery-Token einmal je Start, also bleibt im Lager-WLAN je Gerät ein Abruf.
  - Public sendet die Besucher-IP immer und ruft ohne laufende Anfrage gar nicht auf.
  - Damit ist der Rückfall auf die Prozess-IP im eigenen Code unerreichbar; der Server bleibt unverändert.
- **Service Worker:** Die PWA-Vorlage cacht nur Einträge des Asset-Manifests. Zusätzlich darf `fetch` `/api/` nie bedienen. Die SW-Datei und die Registrierungsdatei sind keine Interop-Module; die Regel „ein JS-Modul“ (AD-29) gilt für `zeltlotse.js`.
- **Hosting der App:** Der API-Host referenziert das WASM-Projekt, liefert es unter `/app` aus (Static Web Assets mit Basispfad `app`) und fällt für `/app/{*pfad:nonfile}` auf `app/index.html` zurück. Erfordert die Vorlage für Fingerprinting eine Inline-Import-Map, wird diese abgeschaltet statt die CSP zu öffnen.
- **Version:** Server und App lesen dieselbe Assembly (`Zeltlotse.Api.Contracts`). Weil SourceLink den Commit an die Version hängt, löst jeder neue Stand auf dem Server „Neu laden“ aus.
- **Notfall-Box ohne Freizeit:** Laut AD-18 zeigt die App-Hülle die Notfall-Box mit 112 schon ab V1, wenn sie offline startet. Ohne Sicherheitskette (V2) und Notfallkarte (V6) gilt ihr Satz „Offline gilt der ausgedruckte Notfallordner.“ (EXPERIENCE „Offline vor V6“), ohne den Knopf „Notfallhilfe öffnen“. Online erscheint sie erst mit der Freizeit-Phase „läuft“ (spätere Stories).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, inklusive Architektur-, bUnit- und Playwright-Tests

**Manual checks (if no CLI):**
- Aspire starten, `/`, `/app/` öffnen: Konsole ohne CSP-Meldung; in den DevTools offline schalten und neu laden → Offline-Leiste und Notfall-Box.

## Auto Run Result

Status: done

**Zusammenfassung:** Client-Grundlagen nach AD-5, AD-18, AD-29 und DESIGN.md, ohne Fachfunktion. Vier neue Projekte:
- `Zeltlotse.Ui.Komponenten`: 20 ELKW-Bausteine, `[NurInteraktiv]`, die unveränderten ELKW-CSS-Dateien mit Sarabun und `zeltlotse.css` (Dunkel-Tokens, Kontrast-Korrekturen, `zl-`-Bausteine).
- `Zeltlotse.Ui`: API-Clients `Betrieb` und `Freizeiten`, Autosave-Dienst mit `AutosaveFeld`, Freizeit-Kontext-Dienst sowie die Handler für Antiforgery, Version und Verbindung.
- `Zeltlotse.App`: Blazor-WebAssembly-PWA unter `/app`. Der Service Worker cacht nie `/api`. Offline zeigt sie Offline-Leiste und Notfall-Box mit `tel:112`; dazu kommen Fehlergrenze mit Browser-Fehlermeldung und der Hinweis „Neu laden“.
- `Zeltlotse.Public`: statische Seiten ohne Skript; der Transport sendet Dienst-Kennung und Besucher-IP.

Der API-Host liefert `/app` (mit Rückfall auf `index.html`) und `/` aus. Außerhalb von `/api` gibt es eine HTML-404-Seite, an jeder Antwort steht `X-Zeltlotse-Version`. Außerhalb von `/api` gilt auch das Formularfeld-Token. Die CSP ist unverändert.

**Abweichungen (aus der Umsetzung, begründet):**
- `elkw-fonts.css` liegt in `wwwroot/` statt `wwwroot/css/`, damit die unveränderten relativen Schriftpfade stimmen.
- Der Knopf-Pfeil kommt als `pfeil.svg`, weil die CSP `data:`-Bilder blockiert.
- Der Host leitet `/app/_content/**` auf `/_content/**` um. Grund: Das Asset-Manifest des veröffentlichten Service Workers erwartet die Dateien unter `/app`.
- Zwei zusätzliche Bausteine, `OeffentlicherRahmen` und `Stile`, sind nötig, damit Public keine eigenen ELKW-Klassen braucht.
- Die Testinfrastruktur hat jetzt `IZusatzEndpunkte.AbbildenAusserhalbApi` (nur in `Testing`), und `PostgresContainer` startet mit `max_connections=500`.
- Der Verbindungs-Handler hält jetzt das Zeitlimit selbst (Vorgabe 30 s). Damit lässt sich ein Timeout vom Abbruch durch den Aufrufer unterscheiden. Der `HttpClient` der App hat deshalb kein eigenes Zeitlimit.

**Dateien:**
- `src/Zeltlotse.Ui.Komponenten/` – Bausteine, CSS, Schriften.
- `src/Zeltlotse.Ui/` – API-Clients, Autosave, Kontext, Transport-Handler, Registrierung.
- `src/Zeltlotse.App/` – PWA-Hülle, `IBrowser`/`JsBrowser` mit `js/zeltlotse.js`, Service Worker, Layout, Fehlergrenze, Verbindungszustand.
- `src/Zeltlotse.Public/` – Wurzel, Layout, Startseite, Seite „nicht verfügbar“, Transport.
- `src/Zeltlotse.Api/Auslieferung/ClientAuslieferung.cs`, `Program.cs`, `Sicherheit/AntiforgeryZwang.cs`, `Fehler/AusnahmeBehandlung.cs`, `IZusatzEndpunkte.cs`, `Zeltlotse.Api.csproj` – Auslieferung, Statuscode-Seiten, Versions-Header, Formular-Token.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/` – `FreizeitKontext`, Enums, `Aktionsschluessel`, `ZeltlotseVersion`, `Kopfzeilen.Version`, `Problemcodes.Konflikt`.
- `tests/Zeltlotse.Ui.Tests/` (neu, bUnit), `tests/Zeltlotse.E2E.Tests/` (neu, Playwright und axe gegen den veröffentlichten Host), `tests/Zeltlotse.Api.Tests/ClientAuslieferungTests.cs`, `tests/Zeltlotse.ArchitectureTests/ClientRegeln.cs` und `ClientTests.cs` mit Negativ-Fixtures.
- `Directory.Packages.props`, `Zeltlotse.slnx`.

**Review:** 41 Befunde (high 0, medium 10, low 26, false 4, maybe-false 1). Gepatcht wurden 11 Einträge:
- 5 medium:
  - Autosave bei Timeout und Ausnahmen;
  - Eingaben während des Speicherns;
  - Fehler-Hinweise bleiben beim Fokuswechsel;
  - Fehlergrenze erholt sich beim Seitenwechsel;
  - Test der Handler-Kette aus `AddAppDienste`.
- 6 low:
  - Test des Vorgabe-Versionsvergleichs;
  - HTML-404 nur für 404;
  - Konsolen-Protokoll der Fehlergrenze;
  - Fokus nach „Ja“;
  - „Erneut versuchen“ mit aktueller Eingabe;
  - Serverfehler beim Token-Abruf gilt nicht als offline.

Nichts wurde zurückgestellt. Alle übrigen Befunde sind mit Begründung im Review Triage Log verworfen.

**Folge-Review empfohlen:** ja, weil fünf medium-Einträge gepatcht wurden (gepatcht: high 0, medium 5, low 6). Ungeprüftes Risiko: Die neue Zustandslogik im `AutosaveFeld` und das Zeitlimit im `VerbindungsHandler` sind nur mit bUnit und nachgebildeten Handlern getestet:
- Nachspeichern nach einer Eingabe während der Übertragung;
- Wiederholen mit aktueller Eingabe;
- Zeitlimit statt `HttpClient.Timeout`.

Das Zusammenspiel mit einem echten AD-19-Endpunkt gibt es erst mit der ersten bearbeitbaren Entität.

**AD-30 (Hinweis des Aufrufers):** 0-3 legt keinen Endpunkt der Arten Organisation/Freizeit, persönlich oder Token an; der Kontext-Endpunkt kommt mit 1-3. Die Fälle 403/404/Token haben keinen Gegenstand (Design Notes). Jede neue Auslieferung (`/`, `/app/**`, PWA-Dateien, 404-Seiten, Versions-Header, Formular-Token, Public-Transport) hat HTTP-Tests gegen echtes PostgreSQL. Der Kontext-Dienst ist gegen 200, 403 und 404 getestet.

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` ergibt 0 Warnungen und 0 Fehler. `dotnet test` ergibt 431 von 431 grün: Architektur, SharedKernel, Personen, API, Ui (bUnit) und E2E (Playwright gegen den veröffentlichten Host, offline hell und dunkel, axe WCAG 2.2 AA, keine CSP-Meldung). Jede Zeile der I/O-Matrix hat einen gelaufenen Test.

**Restrisiken:**
- Die manuelle Prüfung unter Aspire fehlt. Die Entwicklungsauslieferung ist nur über die API-Tests abgedeckt, der veröffentlichte Stand über E2E.
- Die PWA-Icons sind noch die der Vorlage.
- Der Zeitpunkt „zuletzt aktualisiert“ über `localStorage` ist im Browser ungeprüft, weil noch keine App-Seite die API aufruft.
- Die Versionserkennung setzt einen Build mit Git-Commit voraus (Container-Build ist offen).
- Doku-Seite, Demo-Daten und Screenshots folgen mit 0-4 (Design Notes).

### Folgedurchlauf 2026-10-11

**Zusammenfassung:** Zweiter Review-Durchlauf auf die fertige Story mit vier Review-Layern. Kein Befund lag in der Intent oder im Spec. Gepatcht wurden kleine Lücken, jeweils mit dem Test zuerst:
- Die Fehlergrenze hält die Formprüfung der API ein (erlaubte Zeichen, Längen).
- Der Start der Verbindung verliert keinen Online-Wechsel und keinen frischen Zeitpunkt mehr.
- App und öffentliche Seiten nennen ein Icon (keine 404 mehr für `/favicon.ico`).
- Neue Tests für die eigene Adresse von Public, für ändernde Methoden und HEAD an unbekannte Pfade, für „Erneut versuchen“ nach gescheitertem Rückgängig und für die Antiforgery-Kette der App gegen den echten Host.

**Dateien:**
- `src/Zeltlotse.App/Layout/MeldendeFehlergrenze.cs` – Seite und Fehlerart auf erlaubte Zeichen und Längen der API gekürzt.
- `src/Zeltlotse.App/Verbindung/AppVerbindung.cs` – erst lesen, dann zuweisen; erst beobachten, dann den Online-Status lesen.
- `src/Zeltlotse.App/wwwroot/index.html`, `src/Zeltlotse.Public/Wurzel.razor` – `<link rel="icon">`.
- `src/Zeltlotse.Public/Zeltlotse.Public.csproj` – `InternalsVisibleTo` für `Zeltlotse.Api.Tests`.
- `tests/Zeltlotse.Api.Tests/ClientAuslieferungTests.cs` – neue HTTP-Tests gegen echtes PostgreSQL (Icon, Methoden an unbekannte Pfade, eigene Adresse, Antiforgery der App mit Token-Abruf, Ablauf und zweitem 400).
- `tests/Zeltlotse.Ui.Tests/AppLayoutTests.cs`, `AutosaveTests.cs`, `NachgebildeterBrowser.cs` – neue bUnit- und Unit-Tests; angehaltenes Lesen und Aufruf-Reihenfolge im nachgebildeten Browser.

**Review:** 42 Befunde (high 0, medium 1, low 35, false 6, maybe-false 0). Gepatcht: 8 Einträge (medium 1, low 7). Zurückgestellt: 1 (interner Antiforgery-Schlüssel aus 0-2). Alle übrigen Befunde sind mit Begründung im Review Triage Log verworfen; ein Teil davon ist aus dem ersten Durchlauf übernommen („carried“).

**AD-30 (Hinweis des Aufrufers):** Die Token-Fälle laufen jetzt auch für die App-Seite gegen den echten Host mit PostgreSQL: Token einmal je Start, nach Ablauf einmal neu holen, zweites 400 an den Aufrufer. Die Fälle „falsche Rolle 403“ und „fremde Organisation 404“ haben in 0-3 weiter keinen Gegenstand, weil kein Endpunkt mit Rolle oder Organisation entsteht.

**Folge-Review empfohlen:** nein. Im Folgedurchlauf wurde kein high-Eintrag gepatcht (gepatcht: high 0, medium 1, low 7).

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` ergibt 0 Warnungen und 0 Fehler. `dotnet test` ergibt 452 von 452 grün, inklusive Architektur-, bUnit- und Playwright-Tests. Die neuen Tests waren vor der Korrektur rot, außer bei den reinen Testlücken (Rückgängig wiederholen, Antiforgery-Kette der App); die waren sofort grün.

**Restrisiken:**
- Die Offline-Anzeige erholt sich nach einem Netzfehler erst mit der nächsten erfolgreichen Antwort. Mit der ersten API-nutzenden Seite (1-1) prüfen.
- Andere 4xx-Antworten im Autosave zeigen „Erneut versuchen“. Mit dem ersten AD-19-Endpunkt festlegen.
- Die Restrisiken aus dem ersten Durchlauf (Aspire-Prüfung, Vorlagen-Icons, `localStorage`-Zeitpunkt im Browser, Container-Build, Doku in 0-4) gelten weiter.
