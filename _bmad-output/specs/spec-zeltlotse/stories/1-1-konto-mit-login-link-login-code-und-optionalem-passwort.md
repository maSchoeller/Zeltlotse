---
title: 'Konto mit Login-Link, Login-Code und optionalem Passwort'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: '290876f74c208a8ba2dbec38692c59fa8944cdda'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
warnings: [oversized]
deferred:
  - summary: >-
      Login-Link-Anfragen sind nur je IP begrenzt, nicht je Mail-Adresse.
    evidence: |-
      Jede Anfrage für eine fremde Mail-Adresse verwirft deren offenen Link, schickt eine Mail und gibt 5 neue Code-Versuche. Verteilt über viele IPs ist das Mail-Flut und ein (langsamer) Rateversuch auf den 6-stelligen Code. AD-25 legt die Zugriffsbegrenzung je Besucher-IP fest; eine Sperre je Mail-Adresse (z. B. höchstens eine Login-Mail je Adresse und Minute) verändert das Verhalten von „Neuen Link schicken“ und die E2E-Abläufe und braucht eine bewusste Entscheidung vor dem Start als Dienst.
    location: >-
      src/Modules/Konten/Zeltlotse.Konten/Anmelden/LoginAnfrageAnlegen.cs
    severity: medium
  - summary: >-
      Abgelaufene und eingelöste Login-Anfragen (mit Mail-Adresse) werden nie gelöscht.
    evidence: |-
      konten.login_anfrage wächst unbegrenzt und hält Mail-Adressen auch von Personen ohne Konto. Der tägliche Lösch-Dienst mit Fristen gehört laut stories.yaml zu Story 1-7 (AD-16); dort Anfragen nach Ablauf plus kurzer Frist löschen und die Seite „Was passiert mit meinen Daten“ ergänzen.
    location: >-
      src/Modules/Konten/Zeltlotse.Konten/Domaene/LoginAnfrage.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Es gibt weder Konten noch Personen noch eine Sitzung. Ohne Anmeldung kann keine Fach-Story ab 1-2 Rechte prüfen, und FR-1/FR-2 (eine Person, ein Profil; Login-Link aus der Mail, optional Passwort) fehlen ganz.

**Approach:** Modul Konten bekommt Konto und Login-Anfrage (Link + Code, AD-11), der API-Host eine Cookie-Sitzung (AD-12, AD-25) und einen Mail-Adapter. Modul Personen bekommt Person mit Profil (Vorname, Nachname) samt Personen-Schlüssel. Die Web-App bekommt Seiten für Anmelden (Link, Code, Passwort) und „Konto“ (Profil per Autosave, Passwort festlegen, Abmelden). Testgetrieben mit HTTP-Tests gegen echtes PostgreSQL, Playwright-Login-Test mit axe und Doku-Seite mit Screenshots.

## Boundaries & Constraints

**Always:**
- **Konten (Besitzer laut Spine):** `Konto` (Aggregat, nicht organisationseigen): `Id`, `Mail` (normalisiert: getrimmt, klein, eindeutig), `PersonId` (beim Anlegen von Konten vergeben, `Kennung.Neu`), `PasswortHash?`, `SitzungsStempel` (Guid, neu bei jedem Festlegen des Passworts). `LoginAnfrage`: `Id`, `Mail`, `TokenHash`, `CodeHash`, `GueltigBis` (+15 Min.), `Fehlversuche`, Status offen/eingeloest/verworfen nur über Methoden (AD-28). Eine neue Anfrage verwirft ältere offene Anfragen derselben Mail.
- **Token und Code (AD-11):** Token 32 Zufallsbytes, base64url (43 Zeichen); gespeichert nur `SHA-256("login:" + token)` – der Zweck steckt im Hash. Code 6 Ziffern, gespeichert nur als Hash. Einlösen nur per POST. Nach 5 falschen Codes ist die Anfrage verworfen. Link und Code lösen dieselbe Anfrage ein (einmal). Gibt es zur Mail noch kein Konto, legt das Einlösen Konto (mit neuer `PersonId`) an – Mail-Besitz ist damit belegt.
- **Keine Konto-Aufzählung:** `POST /api/konten/login-link` antwortet für bekannte und unbekannte Mail gleich (`202 { anfrageId }`) und schickt immer eine Mail. Falsches Passwort und Konto ohne Passwort antworten gleich.
- **Endpunkte (Datenformen in `Api.Contracts/Konten/` bzw. `Personen/`, camelCase):**

  | Endpunkt | Zugang | Ergebnis |
  |---|---|---|
  | `POST /api/konten/login-link` `{mail}` | anonym, zugriffsbegrenzt | `202 {anfrageId}`; ungültige Mail → `400` je Feld |
  | `POST /api/konten/login-link/einloesen` `{token}` | anonym, zugriffsbegrenzt | `204` + Sitzungs-Cookie; ungültig/abgelaufen/benutzt/verworfen → `404` |
  | `POST /api/konten/login-code/einloesen` `{anfrageId, code}` | anonym, zugriffsbegrenzt | `204` + Cookie; falsch/abgelaufen/benutzt → `404` |
  | `POST /api/konten/anmelden` `{mail, passwort}` | anonym, zugriffsbegrenzt | `204` + Cookie; sonst `401` ProblemDetails |
  | `POST /api/konten/abmelden` | angemeldet | `204`, Cookie gelöscht |
  | `GET /api/ich/konto` | angemeldet | `200 {mail, hatPasswort}` |
  | `PUT /api/ich/konto/passwort` `{passwort}` | angemeldet | `204`; 10–200 Zeichen, sonst `400` je Feld; neuer Sitzungsstempel, eigene Sitzung wird neu ausgestellt |
  | `POST /api/ich/profil` | angemeldet | `200 {version, vorname, nachname}`; legt Person + Personen-Schlüssel an, falls sie fehlt (idempotent) |
  | `GET /api/ich/profil` | angemeldet | `200` wie oben; Person fehlt → `404` |
  | `PATCH /api/ich/profil` | angemeldet | AD-19: `{version, alterWert, vorname|nachname}` → `200 {version, <feld>}`; anderer Wert desselben Felds → `409` `konflikt`; je Feld höchstens 100 Zeichen |

  Ohne Sitzung antwortet jeder angemeldete Endpunkt `401` ProblemDetails (keine Weiterleitung). Keine neuen Problemcodes in `Gemeinsam/` (AD-5); 401/404 tragen keinen eigenen Code.
- **Sitzung (AD-12, AD-25):** Cookie-Authentifizierung des Frameworks, Cookie `__Host-Zeltlotse` (Richtlinie aus 0-2), gleitend 30 Tage. Claims nur `KontoId`, `PersonId`, `SitzungsStempel`; keine Rollen. Jede Anfrage prüft über `Konten.Contracts`, dass das Konto existiert und der Stempel passt, sonst gilt sie als nicht angemeldet. Passwörter mit `PasswordHasher` aus `Microsoft.Extensions.Identity.Core` (Identity-Hashformat v3). SharedKernel bekommt `IAngemeldet { Guid? KontoId; Guid? PersonId; }`, umgesetzt im Host aus `HttpContext.User`.
- **Autorisierung:** `/api`-Endpunkte verlangen eine Sitzung, außer einer ausdrücklich anonymen Liste (Betriebs-Endpunkte, die vier Anmelde-Endpunkte). Ein HTTP-Test zählt alle registrierten `/api`-Endpunkte auf und vergleicht mit dieser Liste. Endpunkte unter `/api/ich/` tragen `.Organisationsuebergreifend()` (SharedKernel `Web/`) und stehen namentlich in einer Liste im Architektur- bzw. HTTP-Test (AD-26).
- **Mail:** `IMailVersand` (SharedKernel, `SendenAsync(an, betreff, text)`). Adapter im Host: `SmtpMailVersand` (MailKit, aktuelle stabile Version zentral fixiert) und `OrdnerMailVersand` (schreibt `.eml` in `Zeltlotse:Mail:Ordner`; nur in `Development`/`Testing`, sonst Startabbruch). Wahl über `Zeltlotse:Mail:Art` (`Smtp`|`Ordner`); fehlt die Konfiguration, bricht der Start ab. Login-Mail (Deutsch, ohne Anrede) enthält Link `<Zeltlotse:OeffentlicheAdresse>/app/anmelden/link/<token>` und den Code; Pflichtkonfiguration `Zeltlotse:OeffentlicheAdresse` (absolute URL, Startprüfung). Die Login-Mail geht direkt nach dem erfolgreichen Command aus dem Endpunkt raus, nicht über die Outbox (Design Notes). Mail-Adressen, Token und Codes stehen nie in Logs, Ausnahmen oder dem Sicherheitsprotokoll.
- **Sicherheitsprotokoll:** `Anmeldung` (mit `PersonId`) bei jedem erfolgreichen Einlösen bzw. Passwort-Login; `AnmeldungFehlgeschlagen` bei falschem Passwort; `LinkEinloesungFehlgeschlagen` bei ungültigem Link oder Code.
- **Personen:** `Person` (Aggregat, nicht organisationseigen): `Id` = `PersonId` aus der Sitzung, `Vorname?`, `Nachname?`, `uint Version` als `xmin` mit `IsRowVersion()` (AD-19). Anlegen im Command `ProfilSicherstellen` schreibt in derselben Transaktion den Personen-Schlüssel (`PersonenSchluessel.Anlegen`). Feldänderung über Command `ProfilFeldAendern` mit Enum `ProfilFeld { Vorname, Nachname }` (in `Api.Contracts/Personen/`).
- **Demo-Daten:** `DemoDaten` spielt über Mediator-Commands (nicht über Tabellen) für jede Demo-Person mit Mail ein Konto und ein Profil mit Vor- und Nachnamen ein; idempotent (vorhandene Mail → überspringen). Kinder ohne Mail bekommen hier nichts (Familie kommt mit 1-4).
- **Web-App (`/app`, nur Ui.Komponenten-Bausteine, keine eigenen `elkw-`/`zl-`-Klassen in Seiten):**
  - `/anmelden`: h1 „Anmelden“, Feld „Mail-Adresse“ (`type=email`, `autocomplete=email`), Knopf „Login-Link schicken“; danach Meldung „Hinweis: Login-Link ist verschickt. Bitte im Postfach nachsehen.“, Feld „Login-Code“ (`inputmode=numeric`, `autocomplete=one-time-code`) und Knopf „Mit Code anmelden“; Link „Mit Passwort anmelden“. Falscher Code: „Fehler: Der Code stimmt nicht oder ist abgelaufen.“ mit Knopf „Neuen Link schicken“.
  - `/anmelden/link/{token}`: h1 „Anmelden“, Knopf „Anmelden“ (POST erst beim Klick); ungültig: „Dieser Link ist nicht mehr gültig.“ mit Knopf „Neuen Link schicken“.
  - `/anmelden/passwort`: Felder „Mail-Adresse“, „Passwort“ (`autocomplete=current-password`), Knopf „Anmelden“; Fehler: „Fehler: Mail-Adresse oder Passwort stimmt nicht.“
  - Nach jeder erfolgreichen Anmeldung: `POST /api/ich/profil`, dann Startseite `/app/`.
  - `/` (Start): angemeldet h1 „Hallo, <Vorname>“ (ohne Vorname „Hallo“), darunter „Noch keine Freizeit geöffnet.“; nicht angemeldet (`401`) → `/app/anmelden`.
  - `/konto`: h1 „Konto“, Mail als Text, `AutosaveFeld` „Vorname“ und „Nachname“ an `/api/ich/profil`, Abschnitt Passwort („Passwort festlegen“ bzw. „Passwort ändern“, Feld `autocomplete=new-password`, Knopf „Passwort speichern“, danach „Gespeichert: Passwort ist festgelegt.“), Knopf „Abmelden“ → `/app/anmelden`.
  - `AppLeiste` bekommt einen optionalen Link zum Konto (Text = Vorname bzw. „Konto“), gesetzt sobald angemeldet.
  - Ein 401 einer angemeldeten Seite führt zu `/app/anmelden` (zentral über einen Transport-Handler in `Zeltlotse.Ui` und `ISitzung`, nicht je Seite).
  - Nach Anmelden und Abmelden verwirft der `AntiforgeryHandler` sein Token (neue öffentliche Methode), weil ASP.NET das Token an die Identität bindet.
- **Tests (AD-30), Test vor dem Code:** HTTP-Tests gegen echtes PostgreSQL für jede Matrix-Zeile; Konten- und Personen-Fachlogik mit `FakeTimeProvider`; bUnit für neue Bausteine, Handler und Seitenlogik; Playwright-Login-Test (Handy 375×812, axe WCAG 2.2 AA, keine CSP-Meldung); Doku-Screenshots `anmelden`, `anmelden-code`, `konto` (Handy, Laptop); `app-start` zeigt die angemeldete Startseite einer Demo-Person.
- **Doku:** neue Seite `doku/src/content/docs/erste-schritte/anmelden.md` (Anmelden per Link, per Code in der installierten App, Passwort festlegen und damit anmelden, Abmelden, Profil im Konto) mit den sechs neuen Bildern; `zeltlotse-oeffnen.md` an die neue Startseite anpassen; Wegweiser in `index.md` und die Rollen-Überblicke Eltern und Mitarbeitende verlinken die Seite; `daten.md` ergänzt einen Satz: zum Konto speichert Zeltlotse Mail-Adresse und ein Passwort nur als Prüfwert. Ohne Anrede. `erste-schritte/anmelden` kommt zu den Pflichtseiten in `DokuRegeln`.

**Never:**
- Keine Rollen, Organisationen, Familien, Global-Admin, Kontext-Endpunkt (1-2 bis 1-4); keine Konto-Löschung, kein Export, keine erneute Anmeldung (1-7); kein Gast-Konto aus Anmeldedaten (2-2); keine Passkeys.
- Kein neues Ereignis in `*.Contracts.Events` und keine Änderung der Ereignis-Fluss-Tabelle; keine Änderung an `Gemeinsam/` außer was AD-5 schon vorsieht.
- Keine Tokens im Browser-Speicher, kein Token in Query-Strings, keine Weiterleitung auf fremde Adressen.
- Kein `UserManager`/`SignInManager`/EF-Identity-Store (Design Notes).
- Keine Lockerung von CSP, Antiforgery oder Cookie-Richtlinie; keine Pop-ups.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Link anfordern | Mail bekannt bzw. unbekannt | beide `202 {anfrageId}`, je genau eine Mail mit Link + Code | Mail ungültig → `400` Feld `mail` |
| Link einlösen, neues Konto | gültiges Token zu unbekannter Mail | `204`, Cookie `__Host-Zeltlotse`; Konto mit `PersonId` angelegt; `GET /api/ich/konto` → `200` | – |
| Link einlösen, bestehendes Konto | gültiges Token zu bekannter Mail | `204`, Sitzung mit derselben `PersonId` | – |
| Token-Fälle | abgelaufen (+16 Min., `FakeTimeProvider`), zweites Einlösen, durch neue Anfrage verworfen, Hash mit anderem Zweck, unbekannt | jeweils `404`, Eintrag `LinkEinloesungFehlgeschlagen` | – |
| Bindung | Token der Anfrage für Mail A | Sitzung gehört Konto A, nie B | – |
| Code | richtiger Code / falscher Code / 6. Versuch mit richtigem Code nach 5 falschen | `204` / `404` / `404` | – |
| Passwort | festgelegt, dann Login mit richtigem / falschem Passwort / Konto ohne Passwort | `204` / `401` / `401` | Passwort < 10 Zeichen → `400` Feld `passwort` |
| Passwort neu festgelegt | zweite Sitzung desselben Kontos besteht | andere Sitzung → `401`; eigene Sitzung bleibt gültig | – |
| Abmelden | `POST /api/konten/abmelden` | `204`, danach `GET /api/ich/konto` → `401` | – |
| Ohne Sitzung | jeder angemeldete Endpunkt | `401` ProblemDetails | – |
| Profil | `POST /api/ich/profil` zweimal | eine Person, ein Schlüssel, gleiche Antwort | `GET` vor dem Anlegen → `404` |
| Autosave | `PATCH` Vorname mit passendem `alterWert` / abweichendem `alterWert` / anderes Feld inzwischen geändert | `200` neue Version / `409` `konflikt` / `200` | 101 Zeichen → `400` |
| Persönlich | Sitzung von Person A | `/api/ich/*` liefert nur Daten von A, nur die Felder der Vertragsform | – |
| Rate-Limit | 21. Anfrage je Minute an einen Anmelde-Endpunkt | `429` | – |
| Demo-Daten | Start mit Demo an, zweimal | 8 Konten und 8 Profile, nicht doppelt | – |
| Mail-Konfiguration | `Ordner` in `Production`; Art fehlt; `OeffentlicheAdresse` fehlt | Start bricht ab, Meldung nennt den Schlüssel | – |

</intent-contract>

## Code Map

- `src/Modules/Konten/Zeltlotse.Konten/KontenModul.cs:15-22` -- `AddKontenModul` (nur `AddModul<KontenDbContext, KontenLeseDbContext>`), `MapKontenEndpoints` ist leerer Stub. Einzige öffentliche Typen bleiben `KontenModul` mit genau `Add…`/`Map…` (`TypRegelnTests.cs:55-70`).
- `src/Modules/Konten/Zeltlotse.Konten/Daten/KontenDbContext.cs` -- Schreib-/Lese-Kontext, leeres `KontenModell.Konfigurieren` (l.27-32), Design-Fabrik. csproj hat `InternalsVisibleTo` für `Zeltlotse.Konten.Tests` (Projekt existiert noch nicht).
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:17-25`, `Daten/PersonenDbContext.cs:13-44`, `Schluessel/PersonenSchluessel.cs:20` (`Anlegen(personId, umhuellt, uhr)`), `Schluessel/PersonenSchluesselQuelle.cs` -- Personen-Gerüst; `Hauptschluessel.Umhuellen` in `src/Zeltlotse.SharedKernel/Datenschutz/Hauptschluessel.cs`.
- `tests/Zeltlotse.Testmodul/Notizen/NotizAnlegen.cs`, `NotizAendern.cs`, `Notiz.cs`, `Daten/TestmodulDbContext.cs` -- Vorbild für Command-Record (`ICommand<T>, IModulCommand<Db>, IBenutzerCommand`), Validator, Handler ohne `SaveChanges`, Aggregat mit privatem Konstruktor.
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContextBasis.cs:41-49` -- Concurrency nur `Folgenummer`; `xmin`/`IsRowVersion` gibt es noch nicht (am `Person`-Modell konfigurieren). Mandantenfilter nur für `IOrganisationsEigen` (Konto/Person implementieren es nicht).
- `src/Zeltlotse.SharedKernel/Sicherheitsprotokoll/ISicherheitsprotokoll.cs:4-27` -- Arten `Anmeldung=1`, `AnmeldungFehlgeschlagen=2`, `LinkEinloesungFehlgeschlagen=3`.
- `src/Zeltlotse.SharedKernel/Web/Zugriffsbegrenzung.cs` -- `.Zugriffsbegrenzt()`; hier daneben `.Organisationsuebergreifend()`.
- `src/Zeltlotse.SharedKernel/Datenschutz/ProtokollSchwaerzung.cs:13-20` -- `[Personenbezogen]` für `[LoggerMessage]`-Parameter; `LoggerExtensions` sind verboten.
- `src/Zeltlotse.Api/Program.cs:27-100` -- Registrierung (l.29-37), `AddMediator` (l.41-66), `DemoDaten.EinspielenAsync` (l.76), Middleware l.81-100: `UseAuthentication`/`UseAuthorization` nach `UseRouting` (l.85) und vor `UseWebSicherheitNachRouting` (l.86) bzw. vor dem `AntiforgeryZwang` darin (`Sicherheit/WebSicherheit.cs:62-63`).
- `src/Zeltlotse.Api/ZeltlotseModule.cs:34-46` -- Gruppe `/api`, ruft `MapKontenEndpoints`/`MapPersonenEndpoints`.
- `src/Zeltlotse.Api/Sicherheit/CookieRichtlinie.cs:15-36` -- erzwingt `__Host-`, Secure, HttpOnly, Lax.
- `src/Zeltlotse.Api/Fehler/AusnahmeBehandlung.cs:28-54` -- nur 400/500 aus Ausnahmen; 401/404/409 geben Endpunkte selbst zurück; Statuscode-Seiten unter `/api` liefern ProblemDetails (`Auslieferung/ClientAuslieferung.cs`).
- `src/Zeltlotse.Api/Start/Startpruefung.cs:30-49` -- Ort für Mail- und Adress-Prüfung.
- `src/Zeltlotse.Api/Adapter/S3DateiSpeicher.cs:139-144` -- Vorbild Adapter + `AddOptions().BindConfiguration`.
- `src/Zeltlotse.Api/Demo/DemoDaten.cs:9-41`, `DemoKatalog.cs:18-31` -- Singleton mit `IConfiguration`; braucht `IServiceScopeFactory` für Mediator; 8 Personen mit Mail (`@example.org`), 3 Kinder ohne.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/Problemcodes.cs` -- `Konflikt="konflikt"`; Gemeinsam nicht erweitern. Neue Ordner `Konten/`, `Personen/`.
- `src/Zeltlotse.AppHost/AppHost.cs:23-41` -- Muster `AddContainer(...).WithHttpEndpoint`; Mail-Container (z. B. `axllent/mailpit`, fester Tag) und `Zeltlotse__Mail__*`, `Zeltlotse__OeffentlicheAdresse` ergänzen.
- `Directory.Packages.props` -- fehlt: `Microsoft.Extensions.Identity.Core` (10.0.12), `MailKit`.
- `src/Zeltlotse.App/AppDienste.cs:16-41` -- Handler-Kette `Verbindungs → Versions → Antiforgery → HttpClientHandler`; hier Sitzungs-Handler einhängen. `App.razor:4` `FocusOnNavigate Selector="h1"` (jede Seite braucht h1). `Layout/MainLayout.razor:13` `<AppLeiste StartHref="./" />`; Body steckt im `OfflineBereich`. `Pages/Start.razor` (5 Zeilen).
- `src/Zeltlotse.Ui/UiRegistrierung.cs:16` -- `AddZeltlotseUi(client)`; neue `KontenApiClient`/`PersonenApiClient` nach Muster `new X(client(sp))` (`Api/BetriebApiClient.cs`). `Transport/AntiforgeryHandler.cs:14-43` -- Token-Cache ohne Reset. `Autosave/AutosaveDienst.cs:45-53` -- erwartet `200 {version, <feldname>}` in camelCase, `409` → Konflikt.
- `src/Zeltlotse.Ui.Komponenten/AppLeiste.razor` -- Parameter `StartHref`, `FreizeitName`, `NotfallHref`; kein Konto-Link. `Feld.razor` reicht unbekannte Attribute (`autocomplete`, `inputmode`) ans `<input>` durch. Seiten außerhalb Ui.Komponenten dürfen keine `elkw-`/`zl-`-Klassen setzen (`ClientRegeln.cs:89-118`); Formular-Hülle oder Link-Stil ggf. als neuer Baustein.
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs:31-108` -- `Einstellungen`, `ZusatzDienste`, `Uhr` (`FakeTimeProvider`), `Umgebung`, `https://localhost`. `tests/Zeltlotse.Api.Tests/TestHelfer.cs:60-64` -- `AntiforgeryTokenAsync`, `ProblemAsync`, `Code`.
- `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs:50` -- Kommentar: ab 1-1 Demo-Zeilen in der DB prüfen.
- `tests/Zeltlotse.E2E.Tests/VeroeffentlichterHost.cs:46-55` -- Umgebungsvariablen des veröffentlichten Hosts (`http://127.0.0.1`); hier Mail-Ordner und `OeffentlicheAdresse` setzen. Achtung: `Secure`-Cookies und Antiforgery werfen über reines HTTP (0-2) – der Host braucht für Login-Tests HTTPS (z. B. selbst erzeugtes Testzertifikat über `Kestrel__Certificates__Default__*`, Playwright `IgnoreHTTPSErrors`) oder einen nachgewiesen funktionierenden Weg.
- `tests/Zeltlotse.E2E.Tests/OfflineStartTests.cs:15-97` -- axe-Aufruf und CSP-Mitschnitt (Vorbild); erwartet heute „Noch keine Freizeit geöffnet.“ anonym unter `/app/` (l.36-40) – an Login-Weiterleitung anpassen. `DokuScreenshotTests.cs:114-209` -- `BildAsync`, Namen `<name>-handy|laptop.png`; `App_Start` (l.129-149) anpassen.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-17` (`PflichtSeiten`), `:42-68` (Screenshot-Abgleich, Anrede-Regex) -- jedes Bild muss verlinkt sein.
- `doku/astro.config.mjs:20-29` -- `erste-schritte` per `autogenerate`; `doku/src/content/docs/erste-schritte/zeltlotse-oeffnen.md` -- Bild-Stil `![… am Handy …](../../../assets/screenshots/<name>-handy.png)`.
- `src/Modules/Personen/…/Schluessel/PersonenSchluesselQuelle.cs` -- liest über den Lese-Kontext; deferred aus 0-2 (Schlüssel anlegen + verschlüsseln im selben Command) bleibt offen, weil 1-1 nichts `[Besonders]` verschlüsselt.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Konten.Tests/` (neu, in `Zeltlotse.slnx`) -- ZUERST: Fachlogik `Konto`/`LoginAnfrage` (Ablauf, einmal, Verwerfen, 5 Fehlversuche, Hash mit Zweck, Mail-Normalisierung) mit `FakeTimeProvider` gegen PostgreSQL.
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST: `ProfilSicherstellen` idempotent mit Schlüssel in derselben Transaktion; `ProfilFeldAendern` mit Konfliktregel.
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: `AnmeldungTests`, `ProfilTests`, `MailTests` für jede Matrix-Zeile; Aufzählung aller `/api`-Endpunkte gegen die Anonym-Liste und die `/api/ich`-Liste mit `.Organisationsuebergreifend()`; Pipeline-Reihenfolge des Hosts per HTTP (Validierung → 400 vor dem Handler, Fehler im Handler → nichts gespeichert; deferred aus 0-1); Sicherheitsprotokoll-Eintrag bleibt, wenn der Command danach scheitert (deferred aus 0-2); `DemoDatenTests` prüft Konten/Profile in der DB; SMTP-Adapter gegen Mail-Testcontainer.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: `AppLeiste` mit Konto-Link, Sitzungs-Handler (401 → `ISitzung`), `AntiforgeryHandler`-Reset, `KontenApiClient`/`PersonenApiClient`, Seitenlogik Anmelden/Link/Passwort/Konto/Start mit `NachgebildeterHandler`.
- `tests/Zeltlotse.E2E.Tests/LoginTests.cs` (neu) -- ZUERST: Handy 375×812, hell: Login per Link aus der Mail (Ordner-Adapter), per Code, Passwort festlegen → abmelden → mit Passwort anmelden, Vorname per Autosave ändern („Gespeichert“); axe auf jeder Seite, keine CSP-Meldung. `DokuScreenshotTests.cs` -- neue Bilder, `App_Start` angemeldet; `OfflineStartTests.cs`, `VeroeffentlichterHost.cs` anpassen.
- `tests/Zeltlotse.ArchitectureTests/` -- `DokuRegeln.PflichtSeiten` um `erste-schritte/anmelden`.
- `src/Zeltlotse.SharedKernel/` -- `IAngemeldet`, `IMailVersand`, `.Organisationsuebergreifend()`.
- `src/Modules/Konten/` -- Entitäten, Modell, Migration, Commands (Anfrage anlegen, Link einlösen, Code einlösen, Passwort-Login prüfen, Passwort festlegen), Endpunkte, Mailtext, `Konten.Contracts`-Leseschnittstelle für die Sitzungsprüfung; `Microsoft.Extensions.Identity.Core`.
- `src/Modules/Personen/` -- `Person`, Modell (`xmin`), Migration, Commands, Endpunkte `/api/ich/profil`.
- `src/Zeltlotse.Api/` -- Cookie-Authentifizierung + Autorisierung, `IAngemeldet`, Mail-Adapter, Startprüfung, `DemoDaten` über Mediator, `appsettings*.json`.
- `src/Zeltlotse.Api.Contracts/Konten/`, `Personen/` -- Datenformen und `ProfilFeld`.
- `src/Zeltlotse.Ui.Komponenten/` -- `AppLeiste`-Konto-Link, ggf. Formular-Baustein; `src/Zeltlotse.Ui/` -- Clients, Sitzungs-Handler, `ISitzung`, Antiforgery-Reset, Registrierung; `src/Zeltlotse.App/` -- Seiten, Layout, Handler-Kette.
- `src/Zeltlotse.AppHost/AppHost.cs`, `Directory.Packages.props`, `Zeltlotse.slnx` -- Mail-Container, Konfiguration, Pakete, Testprojekt.
- `doku/` -- `erste-schritte/anmelden.md`, Anpassungen in `zeltlotse-oeffnen.md`, `index.md`, `eltern/index.md`, `mitarbeitende/index.md`, `daten.md`; Screenshots aus den Tests.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, SharedKernel, Konten, Personen, API, Ui, E2E inklusive Login-Test mit axe) und die Screenshots `anmelden`, `anmelden-code`, `konto`, `app-start`, `app-offline`, `oeffentliche-startseite` (Handy, Laptop) sind neu geschrieben.
- Given eine Person hat sich per Link angemeldet, when sie in der Web-App „Konto“ öffnet, ein Passwort festlegt, sich abmeldet und mit Mail und Passwort wieder anmeldet, then landet sie auf der Startseite mit „Hallo, <Vorname>“ und hat weiterhin genau ein Profil (dieselbe `PersonId`).
- Given die Web-App ist angemeldet geöffnet, when eine API-Antwort `401` kommt (Sitzung abgelaufen oder Passwort anderswo geändert), then zeigt die App die Seite „Anmelden“.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler und die Seite „Anmelden“ zeigt die neuen Bilder.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 39 findings — high 0, medium 11, low 23, false 5, maybe-false 0
- findings:
  - `[low]` `[reject]` (verification-gap) Gleichzeitiges Anlegen bei `POST /api/ich/profil`: Rückfallzweig ungetestet — Rennen nur direkt nach dem Login, Folge wäre eine Fehlermeldung statt Startseite (inzwischen abgefangen, Start legt das Profil an); ein deterministischer Test bräuchte zusätzliche Test-Infrastruktur.
  - `[medium]` `[patch]` (verification-gap) Gleichzeitiges Einlösen derselben Anfrage ungetestet — Zeilensperre `FOR UPDATE` (`AnfrageSperre`) und Test „gleichzeitiges Einlösen gelingt genau einmal“ (4 Link + 1 Code parallel → eine Sitzung, ein Konto).
  - `[low]` `[patch]` (verification-gap) Sitzungsdauer „gleitend 30 Tage“ nicht festgenagelt — Cookie-Test prüft `expires` = Uhr + 30 Tage (±1 Min.).
  - `[medium]` `[patch]` (verification-gap) Fünf-Versuche-Grenze per parallelen falschen Codes umgehbar — gleiche Ursache wie blind „parallele Codes“: Zeilensperre, jeder falsche Code zählt; Test mit 4 bzw. 8 parallelen Versuchen.
  - `[low]` `[reject]` (verification-gap) Demo-Daten reparieren einen halb abgebrochenen Lauf nicht — nur bei Absturz zwischen zwei Commands beim Start; Tests nutzen frische Datenbanken; Fix wäre eine Umstrukturierung des Einspielens.
  - `[medium]` `[patch]` (blind) Parallele Code-Rateversuche werden nicht gezählt — gleiche Ursache, mitbehoben.
  - `[medium]` `[defer]` (blind) Keine Begrenzung je Mail-Adresse: Mail-Flut und frische Code-Budgets für fremde Adressen — AD-25 legt die Begrenzung je IP fest; Sperre je Adresse ändert „Neuen Link schicken“ und die E2E-Abläufe; siehe `deferred`.
  - `[medium]` `[patch]` (blind) Mail-Prüfung und Mail-Parser widersprechen sich (`x<other@y.de>`) — Prüfung über `MailAddress.TryCreate` ohne Anzeigenamen und ohne `<>,;"`; Empfänger als `new MailboxAddress("", an)`; fünf neue 400-Fälle.
  - `[medium]` `[defer]` (blind) Login-Anfragen werden nie gelöscht — täglicher Lösch-Dienst ist laut stories.yaml Story 1-7 (AD-16); siehe `deferred`.
  - `[low]` `[reject]` (blind) Passwort ändern ohne altes Passwort, ohne Protokolleintrag, ohne Mail — AD-25 verlangt erneute Anmeldung nur vor Export, Löschung und Übergabe; eine neue Protokoll-Art und eine Mail wären neue Oberfläche bzw. widersprächen AD-13.
  - `[low]` `[reject]` (blind) Abmelden beendet die Sitzung nicht auf dem Server — übliches Verhalten der Cookie-Sitzung; ein neuer Stempel beim Abmelden würde alle Geräte abmelden (Verhaltensänderung).
  - `[low]` `[patch]` (blind) `http`-Adresse im Betrieb erlaubt — außerhalb Development/Testing verlangt die Startprüfung jetzt `https`; Tests für Production/Staging/Testing.
  - `[low]` `[reject]` (blind) Demo-Daten reparieren halb abgebrochenen Lauf nicht — gleiche Ursache wie verification-gap, verworfen.
  - `[low]` `[patch]` (blind) Seiten behandeln 429/500/Netzfehler nicht, Konto lädt endlos — Seiten fangen Aktionsfehler ab und zeigen „Fehler: Das hat nicht geklappt. Bitte gleich noch einmal versuchen.“; bUnit-Tests. (Gruppe mit edge, Gruppen-Verdikt medium.)
  - `[false]` `[reject]` (blind) `version` im PATCH wird nie geprüft — AD-19 entscheidet den Konflikt am Wert desselben Felds; die Version wird zurückgegeben und vom Client übernommen, eine abweichende Version ohne Feldkonflikt muss laut AD-19 angewendet werden.
  - `[low]` `[reject]` (blind) Passwortformular ohne Benutzernamen-Feld für Passwort-Manager — Komfort; Fix wäre ein zusätzliches verstecktes Feld; Anmeldung per Link bleibt immer möglich.
  - `[low]` `[patch]` (blind) Doku-Formulierungen („ohne Mail“, 5 Fehlversuche, Begrüßung neuer Konten, Passwort vergessen) — Texte in `anmelden.md` korrigiert und ergänzt.
  - `[low]` `[reject]` (edge) Zwei offene Anfragen derselben neuen Mail gleichzeitig eingelöst → Unique-Verletzung → 500 — setzt zwei offene Anfragen voraus, die eine neue Anfrage sonst verwirft; nur bei gleichzeitigem Anfordern; Fix wäre zusätzliche Fehlerbehandlung.
  - `[low]` `[reject]` (edge) Zwei gleichzeitige Link-Anforderungen lassen zwei Anfragen offen — beide sind kurzlebig und einmalig; Fix wäre Sperre je Mail-Adresse.
  - `[low]` `[reject]` (edge) Verwerfen trifft eine gleichzeitig eingelöste Anfrage → 500 — Sekundenbruchteil-Rennen derselben Person; erneutes Anfordern hilft.
  - `[low]` `[reject]` (edge) Versand scheitert nach dem Commit, ältere Anfrage schon verworfen — die Person sieht 500 bzw. die Fehlermeldung und fordert neu an; Fix wäre Umbau der Reihenfolge.
  - `[medium]` `[defer]` (edge) Fremde können den offenen Link einer Person laufend verwerfen — gleiche Ursache wie blind „keine Begrenzung je Mail-Adresse“, siehe `deferred`.
  - `[medium]` `[patch]` (edge) Mail mit Anzeigenamen/Sonderzeichen besteht die Prüfung — gleiche Ursache wie blind „Mail-Prüfung“, mitbehoben.
  - `[medium]` `[patch]` (edge) Vom Parser abgelehnte Adresse → Ausnahme nach dem Commit → 500 — gleiche Ursache, mitbehoben.
  - `[false]` `[reject]` (edge) Rehash eines alten Prüfwerts überschreibt gleichzeitig gesetztes Passwort — Prüfwerte im alten Format kann es in diesem System nicht geben; jeder Prüfwert entsteht mit v3.
  - `[low]` `[reject]` (edge) Zwei gleichzeitige Passwortänderungen, letzte gewinnt — dieselbe Person auf zwei Geräten in derselben Sekunde; Fix wäre Versionsprüfung am Konto.
  - `[low]` `[reject]` (edge) Demo-Daten nach Teilabbruch — gleiche Ursache wie verification-gap, verworfen.
  - `[low]` `[patch]` (edge) `Anmelden.razor`: 500/429/Netzfehler im Fehler-Rahmen — Gruppe „Aktionsfehler“, mitbehoben.
  - `[low]` `[patch]` (edge) `AnmeldenLink.razor`: Fehler beim Einlösen/Profil — Gruppe „Aktionsfehler“, mitbehoben.
  - `[low]` `[patch]` (edge) `AnmeldenPasswort.razor`: 429/Netzfehler — Gruppe „Aktionsfehler“, mitbehoben.
  - `[medium]` `[patch]` (edge) `AnmeldungAbschluss`: Profil-Anlage scheitert nach gesetzter Sitzung, Oberfläche hängt — geht jetzt trotzdem zur Startseite, die das Profil anlegt; Gruppe „Aktionsfehler“.
  - `[low]` `[patch]` (edge) `Konto.razor`: Zeitüberschreitung offline nicht abgefangen — Gruppe „Aktionsfehler“, mitbehoben.
  - `[low]` `[patch]` (edge) `Konto.razor`: Passwort/Abmelden bei Fehler — Abmelden räumt lokal trotzdem auf; Gruppe „Aktionsfehler“.
  - `[low]` `[patch]` (edge) Alter Hinweis „Link verschickt“ bleibt nach Mail-Fehler stehen — `_anfrageId`/`_codeFalsch` werden zurückgesetzt; bUnit-Test.
  - `[medium]` `[defer]` (edge) Login-Anfragen wachsen unbegrenzt — gleiche Ursache wie blind „nie gelöscht“, siehe `deferred`.
  - `[false]` `[reject]` (intent) Keine Tests für „falsche Rolle 403“ und „fremde Organisation 404“ — 1-1 legt keinen Endpunkt mit Rolle oder Organisation an; die Token-Fälle und „nur die eigene Person“ sind gegen PostgreSQL getestet (Design Notes, wie 0-1 bis 0-4).
  - `[low]` `[patch]` (intent) axe fehlt auf der Startseite nach Code- und Passwort-Login — `AxeAsync` ergänzt.
  - `[false]` `[reject]` (intent) Doku-Seite liegt nicht in einem Rollen-Ordner — Anmelden betrifft alle Rollen; Seite unter „Erste Schritte“ mit Links aus Startseite, Eltern und Mitarbeitenden erfüllt „Rolle und Aufgabe“.
  - `[false]` `[reject]` (intent) Test zuerst und Doku-Build am Diff nicht sichtbar — Prozessaussage; `dotnet test` (648 grün) und `npm run build` sind gelaufen.

## Design Notes

- **Login-Mail ohne Outbox:** AD-13 nimmt selbst angeforderte Mails (Login-Link) ausdrücklich aus. Über die Outbox ginge es auch nicht: Ereignisse tragen weder Mail-Adresse noch Token (AD-4), und der Hash lässt sich nicht zurückrechnen. Darum verschickt der Endpunkt die Mail direkt nach dem erfolgreichen Command; scheitert der Versand, bleibt die Anfrage bestehen und die Antwort ist `500` (die Person fordert neu an).
- **Konto und Person ohne Ereignis:** Konten vergibt die `PersonId` beim Anlegen des Kontos. Die Person entsteht im Modul Personen, sobald der Client nach der Anmeldung `POST /api/ich/profil` ruft (AD-2: der Client setzt zusammen). So braucht es kein neues Ereignis und keine Änderung der Spine-Tabelle; `ProfilUebergabeAngenommen` (1-4) verbindet später ein Konto mit einem bestehenden Profil.
- **Identity ohne UserManager:** AD-12 nennt ASP.NET Core Identity. Ein EF-Identity-Store bräuchte einen Kontext, der nicht von `ModulDbContext` erbt, und schriebe außerhalb von Commands (AD-27, Architekturtest „nur Handler nutzen den Schreib-Kontext“). Deshalb: Identity-`PasswordHasher` für Passwörter und die Cookie-Authentifizierung des Frameworks für die Sitzung; Konto und Login-Anfrage sind normale Aggregate in Konten.
- **Konto-Anlage per Link:** FR-4 und NS-3 setzen voraus, dass Menschen ein Konto mit ihrer Mail-Adresse anlegen. Das einfachste Verfahren ohne Sonderlogik: Der Login-Link zu einer unbekannten Mail legt beim Einlösen das Konto an; der Besitz der Mail ist damit belegt, und die Antwort verrät nicht, ob es schon ein Konto gab.
- **Code-Sicherheit:** 6 Ziffern reichen nur zusammen mit der `anfrageId`, 15 Minuten Ablauf, 5 Fehlversuchen je Anfrage und der Zugriffsbegrenzung je IP.
- **Passwortregel:** 10–200 Zeichen ohne Zeichenklassen-Zwang (einfach, keine Speziallogik).
- **AD-30-Pflichtfälle:** Token-Fälle gelten für Link und Code (gültig, abgelaufen/verworfen → `404`, anderer Zweck → `404`, Bindung an das Konto statt an eine Freizeit). Die persönlichen Endpunkte haben keine Kennung in der Route; „fremde Person → 404“ ist dadurch strukturell ausgeschlossen und wird als „liefert nur die eigene Person“ geprüft; „Elternteil der Familie“ kommt mit 1-4. „Falsche Rolle 403“ und „fremde Organisation 404“ haben ohne Organisations- oder Freizeit-Endpunkt keinen Gegenstand (kommt mit 1-2/1-3).
- **Offline:** Die Anmelde-Seiten liegen im `OfflineBereich` und sind offline ausgegraut; die Notfall-Box bleibt nutzbar.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seite „Anmelden“ mit Bildern in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Konto mit Login-Link, Login-Code und optionalem Passwort (CAP-1, FR-1, FR-2):
- **Konten:** `Konto` (normalisierte, eindeutige Mail, `PersonId`, Passwort-Prüfwert nach Identity v3, Sitzungsstempel) und `LoginAnfrage` (nur Hashes von Token und Code, 15 Minuten, einmal, 5 Fehlversuche, neue Anfrage verwirft ältere). Einlösen von Link und Code unter Zeilensperre. Der Login-Link zu einer unbekannten Mail legt das Konto an. Endpunkte unter `/api/konten/...` und `/api/ich/konto`.
- **Personen:** `Person` mit Vor- und Nachname, `xmin` als Version; `POST /api/ich/profil` legt Person und Personen-Schlüssel in einer Transaktion an (idempotent), `PATCH` nach AD-19 mit Konfliktregel.
- **API-Host:** Cookie-Sitzung `__Host-Zeltlotse` (gleitend 30 Tage, Stempelprüfung je `/api`-Anfrage, 401 statt Weiterleitung), `/api` verlangt eine Sitzung außer der Anonym-Liste, `.Organisationsuebergreifend()` an `/api/ich/*`, Mail-Adapter SMTP (MailKit 4.18.1) und Ordner (nur Development/Testing), Startprüfungen für Mail und öffentliche Adresse (außerhalb Development/Testing nur `https`), Demo-Daten über Mediator (8 Konten mit Profil). Aspire startet Mailpit (`axllent/mailpit:v1.31.5`).
- **Web-App:** Seiten „Anmelden“ (Link, Code), Login-Link-Seite (Einlösen erst beim Klick), „Mit Passwort anmelden“, „Konto“ (Autosave Vor-/Nachname, Passwort, Abmelden), Startseite „Hallo, <Vorname>“, Konto-Link in der App-Leiste, Sitzungs-Handler (401 → Anmelden), Antiforgery-Token wird nach An- und Abmelden verworfen, Aktionsfehler als Meldung statt Fehlergrenze.
- **Doku:** neue Seite „Erste Schritte → Anmelden“ mit sechs Screenshots, Anpassungen in „Zeltlotse öffnen“, Startseite, Eltern, Mitarbeitende und „Was passiert mit meinen Daten“.

**Abweichungen (begründet):** `Microsoft.Extensions.Identity.Core` kommt mit dem Framework `Microsoft.AspNetCore.App`; ein Paketverweis scheitert an NU1510 (Warnungen als Fehler), daher kein Eintrag in `Directory.Packages.props`. `Konto` und `LoginAnfrage` sind keine `Aggregat`-Ableitungen, weil sie keine Ereignisse erzeugen. Der E2E-Host läuft über HTTPS mit je Lauf erzeugtem Testzertifikat (Secure-Cookies) und hebt die Zugriffsbegrenzung an; die Grenze prüft der HTTP-Test.

**Dateien:**
- `src/Zeltlotse.SharedKernel/Anmeldung/`, `Mail/`, `Web/OeffentlicheAdresse.cs`, `Web/Organisationsuebergreifend.cs` – Sitzungsangaben, `IAngemeldet`, `IMailVersand`, öffentliche Adresse, AD-26-Merkmal.
- `src/Modules/Konten/` – Domäne, Commands, Endpunkte, `AnfrageSperre`, Sitzung, `IKontoPruefung` (Contracts), Migration `KontoUndLoginAnfrage`.
- `src/Modules/Personen/` – `Person`, Profil-Commands und -Endpunkte, Migration `PersonMitProfil`.
- `src/Zeltlotse.Api.Contracts/Konten/`, `Personen/` – Datenformen, `ProfilFeld`.
- `src/Zeltlotse.Api/` – `Sicherheit/SitzungsRegistrierung.cs`, `Adapter/MailVersand.cs`, `Start/Startpruefung.cs`, `Demo/DemoDaten.cs`, `ZeltlotseModule.cs`, `Program.cs`, `launchSettings.json`.
- `src/Zeltlotse.AppHost/AppHost.cs` – Mailpit, Mail- und Adress-Konfiguration.
- `src/Zeltlotse.Ui.Komponenten/` – `AppLeiste` mit Konto-Link, neuer Baustein `Formular`, CSS.
- `src/Zeltlotse.Ui/` – `KontenApiClient`, `PersonenApiClient`, `ApiFehler`, `SitzungsHandler`/`ISitzung`, `AntiforgeryHandler.TokenVerwerfen`.
- `src/Zeltlotse.App/` – Seiten unter `Features/Konten/`, `Sitzung/AppSitzung.cs`, Startseite, Layout, Handler-Kette.
- `tests/` – neues `Zeltlotse.Konten.Tests`; `Api.Tests` (`AnmeldungTests`, `ProfilTests`, `MailTests`, `EndpunktListeTests`, `DemoDatenTests`); `Personen.Tests/ProfilCommandTests`; `Ui.Tests` (`AnmeldeSeitenTests`, `SitzungUndKontenTests`); `E2E.Tests` (`LoginTests`, `E2EHilfe`, Screenshots, HTTPS-Host); `Testing` (`MailOrdner`, `MailpitContainer`, Factory); `ArchitectureTests/DokuRegeln`.
- `doku/` – `erste-schritte/anmelden.md`, geänderte Seiten, Screenshots `anmelden`, `anmelden-code`, `konto`, `app-start`, `app-offline`.

**Review:** 39 Befunde (high 0, medium 11, low 23, false 5). Gepatcht wurden 9 Einträge:
- medium: Zeilensperre gegen parallele Code-Versuche und doppeltes Einlösen (mit zwei Nebenläufigkeitstests); Mail-Prüfung ohne Anzeigenamen/Sonderzeichen und Empfänger ohne Parser; Aktionsfehler auf den Seiten als Meldung (inkl. Profil-Fehler nach dem Login, Abmelden bei Fehler).
- low: Sitzungsdauer im Test festgenagelt; `https` außerhalb Development/Testing; Doku-Texte; alter „Link verschickt“-Hinweis nach Mail-Fehler; axe nach Code- und Passwort-Login.

Zurückgestellt (siehe `deferred`): Begrenzung je Mail-Adresse; Löschen alter Login-Anfragen (Story 1-7). Verworfen mit Begründung im Review Triage Log: Rückfall-Test bei gleichzeitigem Profil-Anlegen, Demo-Daten nach Teilabbruch, Passwortänderung ohne altes Passwort/Protokoll/Mail, Abmelden ohne serverseitiges Ende, Benutzernamen-Feld für Passwort-Manager, vier seltene Rennen beim Anfordern und Einlösen, Versandfehler nach dem Commit, gleichzeitige Passwortänderungen sowie alle `false`-Befunde (Version im PATCH, Rehash alter Prüfwerte, AD-30-Fälle 403/404, Doku-Ort, Prozessaussagen).

**AD-30 (Hinweis des Aufrufers):** Jeder Endpunkt hat HTTP-Tests gegen echtes PostgreSQL. Token-Fälle (gültig, abgelaufen, zweites Einlösen, verworfen, anderer Zweck, unbekannt, Bindung an das Konto) für Link und Code sind abgedeckt; persönliche Endpunkte liefern nur die eigene Person; jeder angemeldete Endpunkt antwortet ohne Sitzung mit 401. „Falsche Rolle 403“ und „fremde Organisation 404“ haben ohne Organisations- oder Freizeit-Endpunkt keinen Gegenstand (kommen mit 1-2/1-3). Playwright-Login-Test in Handygröße mit axe WCAG 2.2 AA und ohne CSP-Meldung.

**Folge-Review empfohlen:** ja – vier medium-Einträge gepatcht (gepatcht: high 0, medium 4, low 5). Ungeprüftes Risiko: Die Zeilensperre `FOR UPDATE` über `FromSql` mit `xmin` ist neu im Projekt und nur mit Nebenläufigkeitstests auf Command-Ebene belegt; die neue Fehlerbehandlung der Seiten ist nur mit bUnit (nachgebildeter Server) geprüft.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 648 von 648 grün (Architektur, SharedKernel, Konten, Personen, API, Ui, E2E inklusive Login-Test mit axe und Doku-Screenshots).
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 10 Seiten, `dist/erste-schritte/anmelden/` vorhanden.
- Jede Zeile der I/O-Matrix hat einen gelaufenen Test.

**Restrisiken:**
- Siehe `deferred`: Begrenzung je Mail-Adresse, Löschen alter Login-Anfragen.
- Data-Protection-Schlüssel sind nicht dauerhaft gespeichert: Ein Neustart des Containers beendet alle Sitzungen (galt schon für Antiforgery).
- Der AppHost mit Mailpit ist nur kompiliert, nicht unter Aspire gestartet.
- Deferred aus 0-2 („Schlüssel anlegen und im selben Command verschlüsseln“) bleibt offen, weil 1-1 kein `[Besonders]`-Feld verschlüsselt.
- `npm audit` meldet weiter moderate Befunde in Build-Abhängigkeiten von Starlight.
