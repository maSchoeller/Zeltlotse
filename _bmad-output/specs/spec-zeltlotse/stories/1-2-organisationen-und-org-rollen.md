---
title: 'Organisationen und Org-Rollen'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: '9cd8cff761a63e4b55f435d3f996ad8d48a29bf5'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
warnings: [oversized]
deferred:
  - summary: >-
      Angenommene, verworfene und abgelaufene Org-Einladungen (mit Mail-Adresse) werden nie gelöscht.
    evidence: |-
      organisationen.org_einladung hält die Mail-Adresse auch von Personen, die nie ein Konto angelegt haben. Der tägliche Lösch-Dienst mit Fristen (AD-16) gehört laut stories.yaml zu Story 1-7; dort Einladungen nach Ablauf plus kurzer Frist löschen und daten.md um die Frist ergänzen (wie die Login-Anfragen aus 1-1).
    location: >-
      src/Modules/Organisationen/Zeltlotse.Organisationen/Domaene/OrgEinladung.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Es gibt weder Organisationen noch Org-Rollen noch eine Rechteprüfung über Rollen. Ohne sie kann kein Org-Admin Freizeiten anlegen (1-3), und FR-5 (Global Admin legt Organisationen an und lädt den ersten Org-Admin ein; Org-Admins ernennen Org-Admins und Org-Überblick) fehlt ganz.

**Approach:** Konten bekommt das Global-Admin-Kennzeichen (aus der Konfiguration abgeglichen), Organisationen bekommt Organisation, Org-Rolle und Org-Einladung (Zugangslink nach AD-11, an die Mail-Adresse gebunden). Der API-Host bekommt die zentrale Rechteprüfung nach AD-12 (`IKontextRollen`, Mandanten-Kontext aus der Route nach AD-6). Die Web-App bekommt die Seiten „Organisationen“ (Global Admin), „Organisation“ (Rollen und Einladen) und „Einladung annehmen“. Testgetrieben mit HTTP-Tests gegen echtes PostgreSQL inklusive der AD-30-Pflichtfälle, Doku-Seiten mit Screenshots.

## Boundaries & Constraints

**Always:**
- **Global Admin (Konten):** `Konto` bekommt `GlobalAdmin` (bool, Migration). Command `GlobalAdminsAbgleichen(mails)` (kein `IBenutzerCommand`) setzt das Kennzeichen genau für die normalisierten Mails aus `Zeltlotse:GlobalAdmins` (Liste) und, nur bei aktiven Demo-Daten, zusätzlich für die Demo-Person mit Rolle `GlobalAdmin`; alle anderen Konten verlieren es. Fehlt zu einer Mail das Konto, legt der Abgleich es an (neue `PersonId`, wie beim ersten Login-Link). Ablauf beim Start: Migrationen → Demo-Daten → Abgleich. Startprüfung: jede Mail in `Zeltlotse:GlobalAdmins` muss gültig sein, sonst Abbruch mit Nennung des Schlüssels; eine leere Liste ist erlaubt.
- **Konten.Contracts:** Leseschnittstelle `IKontoLesen` mit `IstGlobalAdminAsync(kontoId)` und `MailAsync(kontoId)` (normalisierte Mail oder `null`).
- **Personen.Contracts:** Leseschnittstelle `IPersonenNamen.NamenAsync(IReadOnlyCollection<Guid> personIds)` → Vor- und Nachname je vorhandener Person (Menge statt Einzelabruf, AD-8).
- **Gemeinsame Zugangslink-Bausteine:** Token-Erzeugung, Hash mit Zweck (`SHA-256("<zweck>:" + token)`), Token-Formprüfung und die Mail-Prüfung/-Normalisierung aus Konten wandern unverändert in SharedKernel (`Zugangslinks/`); Konten nutzt sie von dort. Zweck der Org-Einladung: `org-einladung`.
- **Organisationen (Besitzer laut Spine):**
  - `Organisation` (nicht `IOrganisationsEigen`, sie ist der Mandant selbst): `Id`, `Name` (getrimmt, 1–100 Zeichen), `AngelegtAm`, `EingerichtetAm?` (gesetzt, wenn der erste Org-Admin eine Einladung annimmt).
  - `OrgRolle` (`IOrganisationsEigen`): `OrganisationId`, `PersonId`, `Rolle` (`OrgAdmin` | `OrgUeberblick`), `VergebenAm`; eindeutig je (Organisation, Person, Rolle). Eine Person kann beide Rollen haben.
  - `OrgEinladung` (`IOrganisationsEigen`): `Id`, `OrganisationId`, `Mail` (normalisiert), `Rolle`, `TokenHash`, `GueltigBis` (+7 Tage), `EingeladenVon` (`PersonId`), Status offen/angenommen/verworfen nur über Methoden (AD-28). Eine neue Einladung verwirft ältere offene Einladungen derselben Mail für dieselbe Organisation und Rolle.
  - Annehmen: Token gültig, offen, nicht abgelaufen **und** die Mail des angemeldeten Kontos (über `IKontoLesen`) gleich der Einladungs-Mail; sonst `404`, die Einladung bleibt dann unverändert. Annehmen vergibt die Rolle (schon vorhanden → keine zweite Zeile), setzt den Status und bei `OrgAdmin` ggf. `EingerichtetAm`. Einlösen unter Zeilensperre (`FOR UPDATE`, Muster `AnfrageSperre`). Die Suche per Token-Hash ist die einzige Stelle, die den Mandantenfilter umgeht (Kontext-Quelle Token, AD-6).
  - Einladungs-Mail geht direkt nach dem erfolgreichen Command aus dem Endpunkt raus (AD-13-Ausnahme „Einladungen“, Muster Login-Mail), Deutsch ohne Anrede: Name der Organisation, Rolle („Org-Admin“ bzw. „Org-Überblick“), Link `<OeffentlicheAdresse>/app/einladung/<token>`, Gültigkeit 7 Tage, Satz „Zum Annehmen mit dieser Mail-Adresse anmelden.“ Mail-Adressen und Token nie in Logs.
- **Rechteprüfung im Host (AD-6, AD-12):** SharedKernel bekommt `IKontextRollen` (`IstGlobalAdminAsync`, `OrgRollenAsync(organisationId)`), ein serverinternes Enum `OrgRolle` und die Endpunkt-Merkmale `.NurGlobalAdmin()` und `.NurOrgRolle(params OrgRolle[])`. Der Host setzt nach Routing und Authentifizierung, vor der Modellbindung, den Mandanten-Kontext aus dem Routenwert `orgId` und prüft die Merkmale: Global Admin fehlt → `403`; keine Org-Rolle in dieser Organisation (auch unbekannte Organisation, auch Global Admin ohne eigene Rolle) → `404`; Rolle vorhanden, aber nicht die verlangte → `403`. Beides als ProblemDetails. Rollen werden je Anfrage gelesen, nie in der Sitzung gespeichert. Jeder Endpunkt mit `{orgId}` in der Route trägt `.NurOrgRolle(...)` (Test zählt nach); der Routenwert hat die Einschränkung `{orgId:guid}`, damit `/api/organisationen/einladungen/annehmen` nicht kollidiert.
- **Erlaubte Aktionen (AD-29):** `Aktionsschluessel` bekommt `OrganisationenAnlegen = "organisationen-anlegen"` und `OrgRollenVergeben = "org-rollen-vergeben"`. Die Oberfläche entscheidet nur über `erlaubt`, berechnet aus denselben Rollenlisten wie die Merkmale der Endpunkte.
- **Endpunkte (Datenformen in `Api.Contracts/Organisationen/`, Enum `OrgRolle` als JSON-Name in `Gemeinsam/`):**

  | Endpunkt | Recht | Ergebnis |
  |---|---|---|
  | `GET /api/ich/konto` | angemeldet | wie bisher + `erlaubt` (enthält `organisationen-anlegen` nur für Global Admins) |
  | `GET /api/organisationen` | Global Admin | `200 [{id, name, eingerichtet}]`, nach Name sortiert |
  | `POST /api/organisationen` `{name, mail}` | Global Admin | `201 {id}`; legt Organisation + Einladung `OrgAdmin` an, schickt die Mail; `400` je Feld |
  | `GET /api/organisationen/{orgId}` | Org-Admin, Org-Überblick | `200 {id, name, rollen, erlaubt}` (`rollen` = eigene Rollen) |
  | `GET /api/organisationen/{orgId}/rollen` | Org-Admin | `200 {personen: [{personId, vorname, nachname, rolle}], einladungen: [{id, mail, rolle, gueltigBis}]}`; nur offene, nicht abgelaufene Einladungen |
  | `POST /api/organisationen/{orgId}/einladungen` `{mail, rolle}` | Org-Admin | `202 {id}`, schickt die Mail; `400` je Feld |
  | `POST /api/organisationen/einladungen/annehmen` `{token}` | angemeldet, zugriffsbegrenzt | `200 {organisationId}`; sonst `404` |
  | `GET /api/ich/organisationen` | angemeldet, `.Organisationsuebergreifend()` | `200 [{organisationId, name, rollen}]` nur der eigenen Person |

- **Sicherheitsprotokoll:** `Rollenvergabe` beim Annehmen (Person, Organisation, Bezug = Einladung); `LinkEinloesungFehlgeschlagen` bei jedem gescheiterten Annehmen.
- **Demo-Daten (nur über Commands, idempotent):** `DemoKatalog` bekommt die Rolle `GlobalAdmin` und die Person „Gerd Betreiber“ (`gerd.betreiber@example.org`). Fehlt die Demo-Organisation „Jugendwerk Musterstadt“, wird sie angelegt mit Einladung für Andrea Beispiel, die Andrea per `EinladungAnnehmen`-Command annimmt (Org-Admin), plus eine offene Einladung `OrgUeberblick` an `vorstand@example.org` (von Andrea).
- **Web-App (`/app`, nur Ui.Komponenten-Bausteine):**
  - Neue Bausteine in `Ui.Komponenten` mit bUnit-Tests: `Datentabelle` (ELKW `elkw-table elkw-table--stack` in `elkw-table-wrap`, Beschriftung, Spaltenköpfe, Zellen mit Spaltenbezeichnung für die gestapelte Handy-Ansicht) und `Auswahl` (Radiogruppe als `fieldset`/`legend`, ELKW `elkw-choice`, `[NurInteraktiv]`).
  - `/` (Start): unter „Hallo, …“ für Global Admins der Knopf „Organisationen verwalten“ → `organisationen`; je eigener Organisation eine Card mit Name, Rollen und Knopf „Organisation öffnen“. „Noch keine Freizeit geöffnet.“ bleibt.
  - `/organisationen`: h1 „Organisationen“; Datentabelle (Name, Stand: Status „Eingerichtet“ bzw. „Einladung offen“) oder Leer-Zustand „Noch keine Organisation angelegt.“; Card „Organisation anlegen“ mit Feldern „Name der Organisation“ und „Mail-Adresse des ersten Org-Admins“ (`type=email`) und Knopf „Anlegen und einladen“; danach „Gespeichert: Die Organisation ist angelegt, die Einladung ist verschickt.“ und neu geladene Liste.
  - `/organisationen/{orgId}`: h1 = Name; „Rolle: …“; nur mit `org-rollen-vergeben`: Card „Rollen“ (Datentabelle Name, Rolle; fehlender Name → „Name fehlt“), Card „Offene Einladungen“ (Mail, Rolle, „gültig bis <Datum>“) und Card „Person einladen“ (Feld „Mail-Adresse“, Auswahl „Rolle“ mit Org-Admin/Org-Überblick, Knopf „Einladung schicken“, danach „Gespeichert: Die Einladung ist verschickt.“ und neu geladene Listen).
  - `/einladung/{token}`: h1 „Einladung annehmen“, kurzer Erklärtext, Knopf „Einladung annehmen“ (POST erst beim Klick); Erfolg → `organisationen/{organisationId}`; `404` → „Fehler: Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.“ Ohne Sitzung merkt sich die App den Pfad im Speicher (`AppSitzung`, nie Browser-Speicher, nie Query-String) und kehrt nach Anmeldung per Code oder Passwort dorthin zurück; ausdrückliches Abmelden verwirft ihn.
  - `403`/`404` beim Laden einer Organisationsseite → „Diese Seite ist nicht verfügbar.“; Aktionsfehler wie in 1-1 („Fehler: Das hat nicht geklappt. …“).
- **Tests (AD-30), Test vor dem Code:** `Zeltlotse.Organisationen.Tests` (neu) für Fachlogik und Mandantenfilter je organisationseigener Entität; HTTP-Tests gegen echtes PostgreSQL für jede Matrix-Zeile; bUnit für Bausteine, Clients und Seitenlogik; Doku-Screenshots (Handy, Laptop) `organisationen` (Gerd), `organisation` (Andrea), `einladung` (Seite vor dem Klick), `app-start-org-admin` (Andrea).
- **Doku:** neue Rollen-Gruppe `betreiber/` (`index.md`: Global Admin, Organisation anlegen, mit Bild `organisationen`), `org-admin/einladung-annehmen.md` (Bild `einladung`, Hinweis: am einfachsten mit Login-Code anmelden, Link danach erneut öffnen, falls die Startseite erscheint), `org-admin/rollen-vergeben.md` (Bilder `organisation`, `app-start-org-admin`); `org-admin/index.md`, `index.md` (Rollentabelle), `astro.config.mjs` (Gruppe „Betreiber“), `daten.md` (Einladung speichert die Mail-Adresse; Org-Admin sieht Namen, keine Profile; Global Admin sieht keine Personendaten). `DokuRegeln.RollenOrdner` um `betreiber`.

**Never:**
- Keine Freizeiten, Leitungen, Mitgliedschaft der Mitarbeitenden (FR-6, FR-7: 1-3 und später), keine Richtlinien, kein Inventar, keine Organisations-Übersicht der Freizeiten.
- Kein Entziehen von Rollen, kein Zurückziehen von Einladungen, kein Umbenennen oder Löschen von Organisationen (nicht in FR-5); kein Löschen abgelaufener Einladungen (Lösch-Dienst 1-7).
- Kein neues Ereignis, keine Änderung der Ereignis-Fluss-Tabelle, keine neuen Problemcodes in `Gemeinsam/`.
- Keine Rollen im Cookie, keine Rechteableitung im Client aus Rollen, keine Weiterleitung auf fremde Adressen, kein Token im Browser-Speicher oder Query-String.
- Kein Zugriff des Global Admins auf Personendaten oder Rollenlisten ohne eigene Org-Rolle; keine Mail-Adressen von Rolleninhabern in Antworten.
- Keine Pop-ups, keine Lockerung von CSP, Antiforgery oder Cookie-Richtlinie. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Organisation anlegen | Global Admin, gültiger Name und Mail | `201 {id}`, eine Mail mit Einladungslink an die Mail; Liste zeigt `eingerichtet=false` | leerer Name, 101 Zeichen, ungültige Mail → `400` je Feld |
| Falsche Rolle global | angemeldet ohne Global Admin: `GET`/`POST /api/organisationen` | `403` (auch bei ungültigem Körper) | – |
| Ohne Sitzung | jeder neue Endpunkt | `401` ProblemDetails | – |
| Org lesen | Org-Admin / Org-Überblick / ohne Rolle / Org-Admin von B für A / Global Admin ohne Rolle / unbekannte `orgId` | `200` mit `erlaubt=[org-rollen-vergeben]` / `200` mit `erlaubt=[]` / `404` / `404` / `404` / `404` | – |
| Rollen und Einladen | Org-Admin / Org-Überblick / Org-Admin von B | `200` bzw. `202` + Mail / `403` / `404` | ungültige Mail, fehlende Rolle → `400` je Feld |
| Annehmen gültig | angemeldet mit eingeladener Mail | `200 {organisationId}`, Rolle vergeben, Einladung angenommen, `Rollenvergabe` protokolliert; erster Org-Admin → `eingerichtet=true` | – |
| Token-Fälle | abgelaufen (+7 Tage +1 Min., `FakeTimeProvider`), zweites Annehmen, durch neue Einladung verworfen, Login-Token (anderer Zweck), unbekannt, Formfehler | jeweils `404`, `LinkEinloesungFehlgeschlagen` | – |
| Bindung an Mail | angemeldet mit anderer Mail | `404`; danach nimmt die richtige Person dieselbe Einladung an | – |
| Bindung an Organisation | Einladung für X angenommen | Rolle nur in X; `GET /api/organisationen/{Y}` → `404` | – |
| Rolle doppelt | zweite Einladung gleicher Rolle angenommen | eine Rollen-Zeile | – |
| Überblick-Einladung | Org-Überblick angenommen | Rolle `OrgUeberblick`, `eingerichtet` unverändert | – |
| Persönlich | Person A, Person B (gleiche Organisation, andere Rolle) | `/api/ich/organisationen` liefert je nur die eigenen Rollen und nur die Vertragsfelder | – |
| Mandantenfilter | Rollen/Einladungen in A, Kontext B bzw. ohne Kontext | nichts | – |
| Global-Admin-Abgleich | Mail in Konfiguration (mit/ohne Konto), Mail aus Konfiguration entfernt | Kennzeichen gesetzt (Konto angelegt) / Kennzeichen entfernt | ungültige Mail → Startabbruch, Meldung nennt `Zeltlotse:GlobalAdmins` |
| Demo-Daten | Start mit Demo zweimal | 9 Konten/Profile, Gerd Global Admin, eine Organisation, Andrea Org-Admin, eine offene Einladung; nichts doppelt | – |

</intent-contract>

## Code Map

- `src/Modules/Organisationen/Zeltlotse.Organisationen/OrganisationenModul.cs:11-22` -- leeres Gerüst (`AddModul<OrganisationenDbContext, OrganisationenLeseDbContext>`, `MapOrganisationenEndpoints => api`); einzige öffentliche Typen bleiben `OrganisationenModul` mit `Add…`/`Map…` (`tests/Zeltlotse.ArchitectureTests/TypRegelnTests.cs:55`). `Daten/OrganisationenDbContext.cs:52` leeres `OrganisationenModell.Konfigurieren`; Migration `20261011110022_Initial` nur Outbox/Inbox. csproj fehlen Verweise auf `Konten.Contracts`, `Personen.Contracts`, `Zeltlotse.Api.Contracts`; `InternalsVisibleTo` für `Zeltlotse.Organisationen.Tests` ist schon da. `Contracts/Events/EventsNamensraum.cs` leer.
- `src/Modules/Konten/Zeltlotse.Konten/Daten/KontenDbContext.cs:331-358` -- Vorbild Entitäts-Modell (`ToTable`, `ValueGeneratedNever`, eindeutiger Index, `Version.IsRowVersion()`, Status als `short`).
- `src/Modules/Konten/Zeltlotse.Konten/Domaene/Konto.cs:7-54` -- `Konto` ohne Global-Admin-Kennzeichen; `Domaene/LoginAnfrage.cs` Vorbild für `OrgEinladung` (Status-Methoden, `IstEinloesbar`, `Verwerfen`); `Domaene/Geheimnisse.cs:12-30` (`NeuesToken`, `TokenHash(zweck, token)`, `KannTokenSein`) → nach SharedKernel; `CodeHash` bleibt in Konten. `Anmelden/LoginAnfrageAnlegen.cs:47` Mail-Prüfung (`MailAddress.TryCreate` ohne Anzeigenamen und `<>,;"`) → nach SharedKernel.
- `src/Modules/Konten/Zeltlotse.Konten/Anmelden/AnfrageSperre.cs` -- `FromSql(... FOR UPDATE)` über das `DbSet` (AD-27). `AnmeldeEndpunkte.cs:20-30` + `LoginMail.cs` -- Mail direkt nach dem Command, `ToString()` der Ergebnisse ohne Token (AD-22). `Anmelden/KontoSicherstellen.cs` -- Demo-Command, Vorbild für Konto-Anlage im Abgleich.
- `src/Modules/Konten/Zeltlotse.Konten/Ich/KontoEndpunkte.cs:16-21` -- `KontoAntwort` bekommt `erlaubt` (`src/Zeltlotse.Api.Contracts/Konten/Anmeldung.cs:25`, additiv). `Konten.Contracts/Sitzungen/IKontoPruefung.cs` + `KontenModul.cs:273` -- Muster für `IKontoLesen`.
- `src/Modules/Personen/Zeltlotse.Personen/Profil/ProfilAbfrage.cs`, `Daten/PersonenDbContext.cs` -- Person mit `Vorname?`/`Nachname?`; `Personen.Contracts` hat noch keine Schnittstelle.
- `src/Zeltlotse.SharedKernel/Mandanten/IMandantenKontext.cs` (`MandantenKontext.Setzen`) -- bisher nur in `Outbox/OutboxZustellung.cs:135` gesetzt; Filter `Daten/ModulDbContextBasis.cs:39-61` (ohne Kontext nichts). `Sicherheitsprotokoll/ISicherheitsprotokoll.cs` -- `Rollenvergabe=4`, `LinkEinloesungFehlgeschlagen=3`, `SchreibenAsync(art, personId?, organisationId?, bezugId?)`. `Web/Zugriffsbegrenzung.cs`, `Web/Organisationsuebergreifend.cs` -- Muster für Endpunkt-Merkmale. `Anmeldung/IAngemeldet.cs`.
- `src/Zeltlotse.Api/Program.cs:70-77` -- Startreihenfolge (Startprüfung, Migrationen, Demo-Daten; Abgleich danach). Middleware nach `UseRouting`/`UseWebSicherheitNachRouting`; Authentifizierung/Autorisierung stecken in `Sicherheit/WebSicherheit.cs`/`SitzungsRegistrierung.cs:178-201` (`AddAuthorization()` ohne Richtlinien, 401/403 statt Weiterleitung). Rechte-Middleware muss vor der Modellbindung laufen.
- `src/Zeltlotse.Api/ZeltlotseModule.cs:126-151` -- `/api` mit `RequireAuthorization()`, Modul-Registrierung; hier `IKontextRollen`-Umsetzung im Host aus `IKontoLesen` + Org-Rollen-Leseschnittstelle (`Organisationen.Contracts`).
- `src/Zeltlotse.Api/Start/Startpruefung.cs` -- Ort für die Prüfung von `Zeltlotse:GlobalAdmins`. `Demo/DemoKatalog.cs`, `Demo/DemoDaten.cs` -- Katalog (8 Personen mit Mail, Andrea = `DemoRolle.OrgAdmin`), Einspielen über `ISender` in eigenem Scope.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs` -- `Aktionsschluessel` (leer, hier Schlüssel eintragen), Muster `JsonStringEnumConverter` für `OrgRolle`.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs:20-62` -- `Anonym`-Liste (bleibt unverändert), `Organisationsuebergreifend`-Liste (+ `GET /api/ich/organisationen`), Zähltest der anonymen Konten-POSTs. `AnmeldeHelfer.cs` -- `Factory`, `AngemeldeterClientAsync(factory, mail)`, `AusfuehrenAsync`/`WertAsync` (Roh-SQL), `ProtokollAsync`. `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` -- `Einstellungen` (z. B. `Zeltlotse:GlobalAdmins:0`), `Uhr`, `Mails` (`MailOrdner`; Link-Muster in `LoginMailMuster` um `/app/einladung/` erweitern). `DemoDatenTests.cs:51-53` -- Zahlen anpassen.
- `tests/Zeltlotse.Konten.Tests/` -- Vorbild für das neue `Zeltlotse.Organisationen.Tests` (csproj, `AssemblyInfo.cs` mit `AssemblyFixture(PostgresContainer)`, `KontenCommandTests.cs:17-35` Fixture, `SendenAsync`/`LesenAsync`). `tests/Zeltlotse.SharedKernel.Tests/MandantenfilterTests.cs` -- Vorbild Filtertest.
- `src/Zeltlotse.App/Pages/Start.razor`, `Features/Konten/Konto.razor` (Muster Formular/Fehler), `Features/Konten/AnmeldenLink.razor` (Muster POST beim Klick), `Features/Konten/AnmeldungAbschluss.cs:27-40` (`NavigateTo("./")` → Rückweg), `Sitzung/AppSitzung.cs` (Rückweg im Speicher; `AbgelaufenAsync` darf ihn nicht verwerfen).
- `src/Zeltlotse.Ui/UiRegistrierung.cs` + `Api/KontenApiClient.cs` -- neuer `OrganisationenApiClient` nach Muster; `ApiFehler.FeldFehlerAsync`.
- `src/Zeltlotse.Ui.Komponenten/` -- `Card`, `Status`, `LeerZustand`, `Feld`, `Formular`, `Knopf`, `Meldung`; ELKW-Klassen `elkw-table`, `elkw-table--stack`, `elkw-table-wrap` (`wwwroot/css/elkw-components.css`), `elkw-choice` (`elkw-app.css`); Skill `elkw-design` für Aufbau. `tests/Zeltlotse.Ui.Tests/KomponentenTests.cs`, `AnmeldeSeitenTests.cs:17-34` (Seiten-Setup mit `NachgebildeterHandler`).
- `tests/Zeltlotse.ArchitectureTests/ClientRegeln.cs` -- keine `elkw-`/`zl-`-Klassen außerhalb Ui.Komponenten, `EventCallback` → `[NurInteraktiv]`, kein `style=`. `DokuRegeln.cs:13-17` -- `RollenOrdner`, `PflichtSeiten`; jedes Bild muss verlinkt sein, keine Anrede.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- `[Trait("Kategorie","Doku")]`, `E2EHilfe.PerLinkAnmeldenAsync(seite, host, mail)`, `BildAsync(seite, name, geraet)`.
- `doku/astro.config.mjs` (Sidebar, `autogenerate` je Ordner), `doku/src/content/docs/org-admin/index.md`, `index.md`, `daten.md`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Organisationen.Tests/` (neu, in `Zeltlotse.slnx`) -- ZUERST: `Organisation`/`OrgEinladung`/`OrgRolle` (Ablauf, einmal, Verwerfen, Bindung an Mail, doppelte Rolle, `EingerichtetAm`) mit `FakeTimeProvider` gegen PostgreSQL; Mandantenfilter für `OrgRolle` und `OrgEinladung`.
- `tests/Zeltlotse.Konten.Tests/` -- ZUERST: `GlobalAdminsAbgleichen` (setzen, entfernen, Konto anlegen); bestehende Token-Tests bleiben grün nach dem Umzug nach SharedKernel.
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: `OrganisationenTests`, `EinladungTests`, `RechteTests` für jede Matrix-Zeile (403/404/401, Token-Fälle, Protokoll, Mail-Inhalt); `EndpunktListeTests` (neue `/api/ich`-Zeile; jede `{orgId}`-Route trägt `.NurOrgRolle`; Annehmen ist zugriffsbegrenzt); Startprüfung `GlobalAdmins`; `DemoDatenTests`; `/api/ich/konto` mit `erlaubt`.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: `Datentabelle`, `Auswahl`, `OrganisationenApiClient`, Seiten Organisationen/Organisation/Einladung/Start, Rückweg nach Anmeldung, Fehlermeldungen.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Bilder `organisationen`, `organisation`, `einladung`, `app-start-org-admin` (Handy, Laptop).
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- `betreiber` in `RollenOrdner`.
- `src/Zeltlotse.SharedKernel/` -- `Zugangslinks/` (Token, Hash, Mail-Prüfung), `IKontextRollen`, `OrgRolle`, Merkmale `.NurGlobalAdmin()`/`.NurOrgRolle()`.
- `src/Modules/Konten/` -- `GlobalAdmin`-Kennzeichen + Migration, `GlobalAdminsAbgleichen`, `IKontoLesen`, `erlaubt` in `/api/ich/konto`, Umstellung auf SharedKernel-Bausteine.
- `src/Modules/Personen/` -- `IPersonenNamen` in Contracts + Umsetzung + Registrierung.
- `src/Modules/Organisationen/` -- Entitäten, Modell, Migration, Commands (Organisation anlegen, Einladung anlegen, Einladung annehmen), Endpunkte, Einladungs-Mail, Org-Rollen-Leseschnittstelle in Contracts.
- `src/Zeltlotse.Api/` -- Rechte-Middleware (Mandanten-Kontext aus Route, Merkmale prüfen), `IKontextRollen`-Umsetzung, Abgleich beim Start, Startprüfung, Demo-Daten.
- `src/Zeltlotse.Api.Contracts/` -- `Organisationen/`-Datenformen, `Gemeinsam/OrgRolle`, Aktionsschlüssel, `KontoAntwort.Erlaubt`.
- `src/Zeltlotse.Ui.Komponenten/`, `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` -- Bausteine, Client, Seiten unter `Features/Organisationen/`, Start, Rückweg.
- `doku/` -- `betreiber/index.md`, `org-admin/einladung-annehmen.md`, `org-admin/rollen-vergeben.md`, Anpassungen `org-admin/index.md`, `index.md`, `daten.md`, `astro.config.mjs`; Screenshots aus den Tests.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, SharedKernel, Konten, Personen, Organisationen, API, Ui, E2E) und die Screenshots `organisationen`, `organisation`, `einladung`, `app-start-org-admin` (Handy, Laptop) sind geschrieben.
- Given Gerd (Global Admin) legt in der Web-App eine Organisation mit der Mail einer neuen Person an, when diese Person den Link aus der Mail öffnet, sich per Code anmeldet und „Einladung annehmen“ tippt, then landet sie auf der Seite ihrer Organisation mit „Rolle: Org-Admin“, und Gerds Liste zeigt „Eingerichtet“.
- Given Andrea (Org-Admin) lädt eine Mail-Adresse als Org-Überblick ein und die Person nimmt an, when die Person die Organisation öffnet, then sieht sie Name und „Rolle: Org-Überblick“, aber keine Rollenliste und kein Einladungsformular.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler und die Gruppe „Betreiber“ und die neuen Org-Admin-Seiten zeigen die Bilder.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 34 findings — high 0, medium 4, low 22, false 8, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) Scheitert der Mail-Versand nach dem Anlegen oder läuft die erste Einladung ab, bleibt die Organisation „Einladung offen“ ohne Weg zum erneuten Einladen — Organisationen anlegt nur der Betreiber, selten; Ausweg: neue Organisation anlegen. Fix wäre ein neuer Endpunkt „erste Einladung erneut schicken“ (neue Oberfläche); unter Restrisiken festgehalten.
  - `[low]` `[reject]` (blind) Keine Sperre gegen doppelte Organisationsnamen — Knopf ist während des Sendens deaktiviert; doppeltes Anlegen nur durch bewusste Wiederholung; Fix wäre eindeutiger Index plus neue Fehlerbehandlung.
  - `[low]` `[patch]` (blind) Doku „Einladung annehmen“: jede neuere Einladung an dieselbe Mail-Adresse mache den Link ungültig, im Code nur bei gleicher Rolle — Satz auf „mit derselben Rolle“ korrigiert.
  - `[low]` `[reject]` (blind) Keine Zugriffsbegrenzung an den Mail-versendenden Endpunkten — nur Global Admin bzw. Org-Admin mit Sitzung; AD-25 nennt Anmeldung, Zugangslinks und Gast-Anmeldung; Missbrauch setzt eine übernommene Admin-Sitzung voraus.
  - `[medium]` `[patch]` (blind) `Zeltlotse:GlobalAdmins` als einzelner Wert (Umgebungsvariable ohne Index) entfernt still alle Global Admins — Startprüfung bricht bei einem Wert statt Liste ab, Test ergänzt.
  - `[medium]` `[patch]` (blind) Vergabe und Entzug von Global Admin stehen nicht im Sicherheitsprotokoll (AD-22 „Rollenvergaben“) — Abgleich schreibt `Rollenvergabe` je geändertem Konto, Test ergänzt.
  - `[medium]` `[defer]` (blind) Einladungen (mit Mail-Adresse) werden nie gelöscht — der tägliche Lösch-Dienst (AD-16) existiert noch nicht und kommt laut stories.yaml mit 1-7, wie bei den Login-Anfragen aus 1-1; siehe `deferred`.
  - `[low]` `[reject]` (blind) Einladungsseite zeigt weder Organisation noch Rolle — die Mail nennt beides; Fix wäre ein neuer lesender Token-Endpunkt (neue Oberfläche).
  - `[low]` `[patch]` (blind) Platzhalter U+FFFF beim Sortieren fehlender Namen kann unter ICU ignoriert werden, „Name fehlt“ stünde vorn — häufig, weil Eingeladene beim ersten Login noch keinen Namen haben; Sortierung zuerst nach „hat Namen“, Test ergänzt.
  - `[low]` `[reject]` (blind) „gültig bis“ zeigt nur das Datum — am letzten Tag ungenau um Stunden; kosmetisch.
  - `[low]` `[reject]` (blind) Demo-Daten reparieren einen halb abgebrochenen Lauf nicht — wie in 1-1 verworfen: nur bei Absturz zwischen Commands beim Start, Tests nutzen frische Datenbanken.
  - `[low]` `[reject]` (blind) Rückweg überlebt den Wechsel der Person auf demselben Gerät — die Mail-Bindung verhindert Schaden; Fix wäre zusätzliche Logik an der Anmeldung.
  - `[low]` `[patch]` (blind) Fehlende Tests für Widerruf, Mail-Fehler und toten `DbUpdateConcurrencyException`-Zweig — der tote Zweig im Annehmen-Endpunkt ist entfernt (Zeilensperre macht ihn unerreichbar); Rollen werden je Anfrage gelesen (Middleware), Mail-Fehler siehe erster Befund.
  - `[medium]` `[patch]` (edge) Skalarer Wert in `Zeltlotse:GlobalAdmins` — gleiche Ursache wie blind „einzelner Wert“, mitbehoben.
  - `[low]` `[reject]` (edge) Zwei gleichzeitig startende Instanzen legen dasselbe Konto an → Startabbruch — AD-1/AD-23: ein Prozess, ein Container.
  - `[low]` `[reject]` (edge) Mail-Fehler nach dem Anlegen, Wiederholung erzeugt Duplikat — gleiche Lage wie blind „Mail-Versand“, verworfen.
  - `[low]` `[reject]` (edge) Mail-Fehler nach dem Einladen: Einladung offen, aber nie zugestellt — der Org-Admin lädt erneut ein, die neue verwirft die alte.
  - `[low]` `[reject]` (edge) Zwei offene Einladungen gleicher Rolle parallel angenommen → Primärschlüssel-Verletzung → 500 — zwei offene Einladungen gleicher Rolle gibt es nur nach gleichzeitigem Ausstellen; Sekundenbruchteil-Rennen.
  - `[low]` `[reject]` (edge) Gleichzeitiges Ausstellen lässt mehrere offene Einladungen bestehen — beide gültig und harmlos; Fix wäre ein Teilindex oder eine Sperre.
  - `[low]` `[reject]` (edge) Demo-Daten nach Teilabbruch — gleiche Ursache wie blind, verworfen.
  - `[low]` `[reject]` (edge) Rückweg nach Personenwechsel — gleiche Ursache wie blind, verworfen.
  - `[low]` `[reject]` (edge) Anmeldung per Login-Link in neuem Tab verliert den Rückweg — bewusste Entscheidung (Design Notes): kein Token in URL oder Browser-Speicher; die Doku erklärt den Weg über den Login-Code.
  - `[low]` `[patch]` (verification-gap) `IstEigenerPfad`: Prüfungen auf `:`, `//` und `\` ungetestet — drei Fälle in der Theorie ergänzt.
  - `[low]` `[patch]` (verification-gap) Rolle außerhalb des Wertebereichs (`"rolle":99`) ungetestet — HTTP-Test: 400 an `rolle`, keine Einladung gespeichert.
  - `[low]` `[patch]` (verification-gap) `DbUpdateConcurrencyException`-Zweig im Annehmen-Endpunkt ungetestet — gleiche Ursache wie blind „toter Zweig“, Zweig entfernt.
  - `[low]` `[reject]` (verification-gap, other) Primärschlüssel-Verletzung bei gleichzeitigem Annehmen zweier Einladungen — gleiche Ursache wie edge, verworfen.
  - `[false]` `[reject]` (intent) Org-Überblick als Rolle statt Ansicht — `rollen-und-rechte.md` und Glossar definieren Org-Überblick als Rolle.
  - `[false]` `[reject]` (intent) Ernennen über Einladung statt direkt — AD-11 legt die Org-Einladung fest; ein Personenverzeichnis gibt es vor FR-7 nicht (Design Notes).
  - `[false]` `[reject]` (intent) Kein Browser-Durchlauf der Abläufe — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; der Aufrufer verlangt HTTP-Tests und Screenshots, beide vorhanden.
  - `[false]` `[reject]` (intent) „Test vor dem Code“ nicht am Diff sichtbar — Prozessaussage.
  - `[false]` `[reject]` (intent) Doku-Build nicht im Diff belegt — `npm run build` ist gelaufen (Exit-Code 0, 13 Seiten).
  - `[false]` `[reject]` (intent) 401 nur über die Schleife aller Endpunkte — die Schleife prüft jeden Endpunkt einzeln mit eingesetzten Routenwerten.
  - `[false]` `[reject]` (intent) Org-Überblick ohne eigene Doku-Seite — beschrieben in `org-admin/index.md` und `rollen-vergeben.md`; die Rolle hat in dieser Story keine eigene Aufgabe.
  - `[false]` `[reject]` (intent) Ergänzungen über die Einzeiler-Absicht hinaus — alle vom Spec und von AD-11/AD-12/AD-29 verlangt.

### 2026-10-11 — Review pass (Folgedurchlauf)
- verdicts: 32 findings — high 0, medium 2, low 24, false 6, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) Mail-Fehler nach dem Anlegen, Wiederholung erzeugt doppelte Organisation — carried: gleiche Lage wie im ersten Pass (blind „Mail-Versand“/edge „Duplikat“), Code unverändert.
  - `[low]` `[reject]` (blind) Erster Org-Admin ohne Weg zum erneuten Einladen — carried: wie erster Pass, unter Restrisiken.
  - `[medium]` `[reject]` (blind) Falsche Einladungen und Rollen lassen sich nicht zurücknehmen — außerhalb der Absicht: das Intent-Contract schließt „Entziehen von Rollen, Zurückziehen von Einladungen“ ausdrücklich aus (nicht in FR-5).
  - `[low]` `[reject]` (blind) Keine Zugriffsbegrenzung an den Mail-versendenden Endpunkten — carried: wie erster Pass.
  - `[false]` `[reject]` (blind) Ausstellen von Einladungen und Anlegen von Organisationen fehlen im Sicherheitsprotokoll — `org_einladung` hält Organisation, Mail, Rolle, `EingeladenVon` und Zeitpunkt jeder Einladung fest; das Intent-Contract legt die Protokoll-Ereignisse (`Rollenvergabe` beim Annehmen, `LinkEinloesungFehlgeschlagen`) abschließend fest, beide sind da.
  - `[low]` `[reject]` (blind) `Rollenvergabe` außerhalb der Command-Transaktion und auch bei schon vorhandener Rolle — Protokoll nach dem Annehmen verlangt der Spec bei jedem gültigen Annehmen (Matrix-Zeile, Bezug = Einladung); `ISicherheitsprotokoll` schreibt in eigener Transaktion, ein Verschieben in den Handler machte es nicht atomar. Ausfall genau zwischen zwei Schreibvorgängen ist selten.
  - `[low]` `[reject]` (blind) Keine Fremdschlüssel/Check-Constraints/Teilindex in den neuen Tabellen — carried für den Teil „parallel ausgestellte Einladungen“ (wie erster Pass); verwaiste Zeilen entstehen nicht, weil es keinen Lösch-Pfad für Organisationen gibt und Zeilen nur über Commands entstehen.
  - `[low]` `[reject]` (blind) Start mit mehreren Instanzen ohne Sperre — carried: AD-1/AD-23 ein Prozess, ein Container.
  - `[low]` `[reject]` (blind) Demo-Organisation nach Teilabbruch nicht ergänzt — carried: wie erster Pass.
  - `[medium]` `[defer]` (blind) Einladungen werden nie gelöscht — carried: schon in `deferred` (Lösch-Dienst 1-7), nicht erneut zurückgestellt.
  - `[low]` `[reject]` (blind) „Gespeichert“ und „Fehler“ gleichzeitig, wenn das Neuladen nach erfolgreichem Speichern scheitert — beide Aussagen stimmen (gespeichert ja, Liste nicht aktualisiert); nur bei Netzabbruch genau zwischen zwei Anfragen.
  - `[low]` `[reject]` (blind) Rollen-Texte und Mail-Prüfung doppelt (`OrgRechte.Text`/`Anzeige.Rolle`, `MailRegel`/`LoginAnfrageAnlegenValidator`) — Texte heute gleich, gemeinsame Bausteine (`MailAdresse.IstReineMailAdresse`, `MaximaleLaenge`) werden genutzt; die Zusammenlegung wäre ein Umbau, keine direkte Korrektur. Die „umgekehrte Längenprüfung“ in `MailRegel` ist Absicht (nur eine Meldung je Feld).
  - `[low]` `[reject]` (blind) Sortierung in der Datenbank vs. `CurrentCulture` im Speicher — betrifft zwei verschiedene Seiten; Abweichung nur bei Umlauten/Groß-Klein in Grenzfällen, kosmetisch.
  - `[low]` `[reject]` (blind) `OrgRechte.Server` bildet Unbekanntes auf `(OrgRolle)(-1)` ab — gewollt, damit `IsInEnum` mit Feldfehler antwortet; der Fall `"rolle":99` ist per HTTP-Test belegt. Neue Rollen bringen ohnehin Tests mit.
  - `[false]` `[reject]` (blind) Modell-Snapshot verliert `internal` — eine Klasse auf oberster Ebene ohne Modifizierer ist in C# `internal`; die Sichtbarkeit ändert sich nicht, Architekturtests grün.
  - `[low]` `[reject]` (blind) Einladungsablauf: zweiter Klick nach dem Rückweg, keine Vorschau von Organisation/Rolle, Login-Link verliert den Rückweg — carried: wie erster Pass (Vorschau, Login-Link im neuen Tab); „POST erst beim Klick“ verlangt das Intent-Contract.
  - `[low]` `[reject]` (edge) Mail-Fehler nach Anlegen der Organisation — carried.
  - `[low]` `[reject]` (edge) Mail-Fehler nach dem Einladen — carried.
  - `[low]` `[reject]` (edge) Gleichzeitiges Ausstellen lässt zwei offene Einladungen bestehen — carried.
  - `[low]` `[reject]` (edge) Gleichzeitiges Annehmen zweier Einladungen gleicher Rolle → Primärschlüssel-Verletzung — carried.
  - `[low]` `[reject]` (edge) Protokoll-Schreiben scheitert nach dem Annehmen → 500, Wiederholung 404 — gleiche Ursache wie blind „`Rollenvergabe` außerhalb der Transaktion“, verworfen.
  - `[low]` `[reject]` (edge) Demo-Daten nach Teilabbruch — carried.
  - `[low]` `[reject]` (edge) Zwei Hosts starten gleichzeitig — carried.
  - `[low]` `[reject]` (edge) `JsonException` bei unbekannter Rolle von neuerem Server — Web-App und API werden gemeinsam vom selben Host ausgeliefert; heute nicht erreichbar.
  - `[low]` `[reject]` (edge) Veralteter Rückweg, wenn die Anmeldung erst viel später in derselben App-Sitzung erfolgt — Ziel ist immer ein eigener Pfad (`IstEigenerPfad`), Schaden keiner; Fix wäre zusätzliche Logik.
  - `[low]` `[patch]` (verification-gap) Längenregel der Mail (254/255) für Anlegen und Einladen ungetestet — Test `Mail_mit_254_Zeichen_geht_mit_255_nicht` in `OrganisationenCommandTests` ergänzt (Anlegen und Einladen, beide Grenzen).
  - `[low]` `[patch]` (verification-gap) `GET /api/ich/organisationen` nie mit Rollen in mehreren Organisationen getestet — HTTP-Test `Ich_Organisationen_liefert_bei_mehreren_Organisationen_je_Eintrag_nur_dessen_Rollen_nach_Name` ergänzt (zwei Organisationen, zwei Rollen in einer, Reihenfolge nach Name).
  - `[low]` `[reject]` (verification-gap, other) `Rollenvergabe` außerhalb der Command-Transaktion — gleiche Ursache wie blind, verworfen.
  - `[false]` `[reject]` (intent) Zweite Umgehung des Mandantenfilters in `GET /api/ich/organisationen` widerspreche „einzige Stelle“ — die Endpunkt-Tabelle des Intent-Contracts schreibt diesen Endpunkt mit `.Organisationsuebergreifend()` selbst vor; der Satz „einzige Stelle“ steht beim Annehmen und meint die Token-Suche des Moduls.
  - `[false]` `[reject]` (intent) Kein Browser-Durchlauf der ganzen Kette — carried: AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall.
  - `[false]` `[reject]` (intent) Rückweg gilt für jede Seite mit 401, nicht nur für die Einladung — das Intent-Contract verlangt den Rückweg zur Einladung und verbietet keinen allgemeinen; der allgemeine Mechanismus erfüllt ihn und ist gegen fremde Ziele geschützt.
  - `[false]` `[reject]` (intent) Doku-Build nicht im Diff belegt — carried; in diesem Pass erneut gelaufen (Exit-Code 0, 13 Seiten).

## Design Notes

- **Global Admin aus der Konfiguration:** Die Spine legt das Kennzeichen in Konten, sagt aber nicht, wie es entsteht. Selbstregistrierung ist zurückgestellt (NFR-19); eine Oberfläche zum Ernennen gäbe es nur für den Betreiber selbst. Einfachste Lösung ohne Speziallogik: Die Konfiguration ist die Quelle, der Abgleich beim Start schreibt sie ins Kennzeichen. Demo: Gerd Betreiber nur bei aktiven Demo-Daten.
- **Eine Einladung für beide Org-Rollen:** AD-11 nennt die Org-Einladung (einmal, 7 Tage, Mail-Bindung). FR-5 lässt offen, wie Org-Admins ernennen; ein Personenverzeichnis der Organisation gibt es vor FR-7 nicht. Darum ernennt man auch weitere Org-Admins und Org-Überblick per Einladung mit Rolle – ein Mechanismus statt zwei.
- **Bindung an die Mail-Adresse:** Annehmen verlangt eine Sitzung, deren Konto-Mail der Einladung entspricht. Weitergeleitete Links verschaffen so keine Rechte (AD-11). Abweichung → `404` ohne Hinweis auf den Grund.
- **Rechteprüfung zentral im Host:** Merkmale am Endpunkt, Prüfung in einer Middleware vor der Modellbindung, damit `403`/`404` vor `400` kommen und kein Modul selbst Rollen prüft (AD-12). Reihenfolge: keine Rolle in der Organisation → `404` (die Organisation ist für diese Person „fremd“), Rolle vorhanden, aber falsch → `403` (AD-30).
- **Organisation ohne Mandantenfilter:** Die Organisation ist der Mandant selbst; die Liste des Global Admins liest sie ohne Filter. `EingerichtetAm` an der Organisation erspart dafür einen mandantenübergreifenden Blick auf Rollen. Org-Rollen und Einladungen tragen `OrganisationId` und laufen unter dem Filter; Ausnahmen sind nur die Token-Suche (Kontext-Quelle Token, AD-6) und `GET /api/ich/organisationen` (AD-26).
- **Rückweg nach dem Anmelden:** Nur im Speicher der laufenden App. Nach Anmeldung per Code oder Passwort klappt er; ein Login-Link öffnet einen neuen Tab, dann erscheint die Startseite und der Einladungslink wird erneut geöffnet (Doku). Ein Rücksprung-Parameter in der URL würde den Token in den Query-String bringen.
- **AD-30-Pflichtfälle:** Organisation: berechtigt, falsche Rolle `403`, fremde Organisation `404`; global: Nicht-Global-Admin `403`; persönlich: nur eigene Rollen, Elternteil-Fall entfällt bis 1-4; Token: gültig, abgelaufen/verworfen/benutzt `404`, anderer Zweck `404`, Einladung für X wirkt nicht in Y.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `betreiber/`, `org-admin/einladung-annehmen/`, `org-admin/rollen-vergeben/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Organisationen und Org-Rollen (CAP-3, FR-5):
- **Konten:** Global-Admin-Kennzeichen am Konto (Migration `GlobalAdmin`), Abgleich mit `Zeltlotse:GlobalAdmins` beim Start (legt fehlende Konten an, entzieht allen anderen das Kennzeichen, protokolliert jede Änderung als `Rollenvergabe`), Leseschnittstelle `IKontoLesen`, `erlaubt` in `GET /api/ich/konto`.
- **Organisationen:** `Organisation`, `OrgRolle`, `OrgEinladung` (Migration `OrganisationRolleEinladung`); Organisation anlegen mit Einladung des ersten Org-Admins, weitere Org-Admins und Org-Überblick per Einladung; Annehmen nur mit passender Konto-Mail, unter Zeilensperre, Token-Suche als einzige Umgehung des Mandantenfilters; Einladungs-Mail direkt aus dem Endpunkt; acht Endpunkte laut Spec.
- **API-Host:** zentrale Rechteprüfung vor der Modellbindung (Mandanten-Kontext aus `{orgId:guid}`, `.NurGlobalAdmin()` → 403, `.NurOrgRolle()` → 404/403), `IKontextRollen` je Anfrage, Startprüfung für `Zeltlotse:GlobalAdmins` (ungültige Mail oder Einzelwert statt Liste → Abbruch), Demo-Daten mit Gerd Betreiber, „Jugendwerk Musterstadt“, Andrea als Org-Admin und offener Org-Überblick-Einladung.
- **SharedKernel:** Zugangslink-Bausteine und Mail-Prüfung aus Konten hierher verschoben, `IKontextRollen`, `OrgRolle`, Rechte-Merkmale.
- **Web-App:** Bausteine `Datentabelle` und `Auswahl`, `OrganisationenApiClient`, Seiten „Organisationen“, Organisation, „Einladung annehmen“, Startseite mit „Organisationen verwalten“ und Card je Organisation, Rückweg nach Anmeldung per Code oder Passwort (nur im Speicher).
- **Doku:** neue Gruppe „Betreiber“, Seiten „Einladung annehmen“ und „Rollen vergeben“ unter Org-Admin, Anpassungen an Startseite, Org-Admin-Überblick und „Was passiert mit meinen Daten“; Screenshots `organisationen`, `organisation`, `einladung`, `app-start-org-admin` (Handy, Laptop).

**Abweichungen (begründet):** `KontoAntwort.Erlaubt` ist nullbar mit Vorgabe `null` (additive API-Änderung, ältere Stände bleiben lesbar). `Rollenvergabe` beim Annehmen schreibt der Endpunkt nach dem erfolgreichen Command, damit nur gespeicherte Vergaben im Protokoll stehen; Vergaben der Demo-Daten stehen deshalb nicht darin. Ein leerer Einzelwert in `Zeltlotse:GlobalAdmins` gilt als leere Liste (die Konfiguration mit Index hinterlässt einen leeren Elternwert).

**Dateien:**
- `src/Zeltlotse.SharedKernel/Zugangslinks/`, `Rechte/` – Token, Hash, Mail-Prüfung; `IKontextRollen`, `OrgRolle`, Merkmale.
- `src/Modules/Konten/` – Kennzeichen, Migration, `GlobalAdminsAbgleichen`, `IKontoLesen`/`KontoLesen`, `erlaubt` im Konto-Endpunkt, Umstellung auf SharedKernel.
- `src/Modules/Personen/` – `IPersonenNamen` + Umsetzung.
- `src/Modules/Organisationen/` – Domäne, Modell, Migration, Commands, Endpunkte, Einladungs-Mail, `IOrgRollenLesen`.
- `src/Zeltlotse.Api/` – `Rechte/RechtePruefung.cs`, `Rechte/KontextRollen.cs`, `Start/GlobalAdmins.cs`, Startprüfung, Demo-Daten, Middleware-Kette.
- `src/Zeltlotse.Api.Contracts/` – `Organisationen/`, `Gemeinsam/OrgRolle.cs`, Aktionsschlüssel, `KontoAntwort.Erlaubt`.
- `src/Zeltlotse.Ui.Komponenten/`, `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` – Bausteine, CSS, Client, Texte, Seiten, Rückweg.
- `tests/` – neues `Zeltlotse.Organisationen.Tests`; `Api.Tests` (`OrganisationenTests`, `EinladungTests`, `RechteTests`, `OrgHelfer`, Endpunktliste, Startprüfung, Demo-Daten); `Konten.Tests`; `Ui.Tests/OrganisationenSeitenTests`; `E2E.Tests/DokuScreenshotTests`; `Testing/MailOrdner`; `ArchitectureTests/DokuRegeln`.
- `doku/` – `betreiber/index.md`, `org-admin/einladung-annehmen.md`, `org-admin/rollen-vergeben.md`, geänderte Seiten, `astro.config.mjs`, Screenshots.

**Review:** 34 Befunde (high 0, medium 4, low 22, false 8). Gepatcht (7 Einträge): medium – Einzelwert in `Zeltlotse:GlobalAdmins` bricht den Start ab; Global-Admin-Änderungen im Sicherheitsprotokoll. low – Doku-Satz zu ersetzten Einladungen; Sortierung fehlender Namen; toter `DbUpdateConcurrencyException`-Zweig entfernt; Tests für Rückweg-Schutz (`:`, `//`, `\`) und unbekannte Rolle. Zurückgestellt: Löschen alter Einladungen (Lösch-Dienst, Story 1-7). Verworfen mit Begründung im Review Triage Log: kein erneutes Einladen des ersten Org-Admins, doppelte Organisationsnamen, Zugriffsbegrenzung der Mail-Endpunkte, Vorschau auf der Einladungsseite, Datum ohne Uhrzeit, Demo-Daten nach Teilabbruch, Rückweg nach Personenwechsel, mehrere seltene Rennen (gleichzeitiger Start, gleichzeitiges Ausstellen/Annehmen), Mail-Fehler nach dem Commit, Rückweg bei Login-Link im neuen Tab (bewusst) sowie alle `false`-Befunde des Intent-Audits.

**AD-30 (Hinweis des Aufrufers):** Jeder neue Endpunkt hat HTTP-Tests gegen echtes PostgreSQL: berechtigt; falsche Rolle → 403 (Nicht-Global-Admin, Org-Überblick an Org-Admin-Endpunkten, auch bei ungültigem Körper); fremde Organisation → 404 (ohne Rolle, Org-Admin von B, Global Admin ohne Rolle, unbekannte Organisation); Token-Fälle (abgelaufen, zweites Annehmen, verworfen, Login-Token, unbekannt, Formfehler, kein Token → 404 mit Protokolleintrag; Einladung für X wirkt nicht in Y; andere Mail → 404); ohne Sitzung → 401; persönlicher Endpunkt liefert nur eigene Rollen (Elternteil-Fall kommt mit 1-4).

**Folge-Review empfohlen:** ja – zwei medium-Einträge gepatcht (gepatcht: high 0, medium 2, low 5). Ungeprüftes Risiko: Der neue Protokolleintrag im Global-Admin-Abgleich läuft beim Start über `ISicherheitsprotokoll` (eigene Transaktion) innerhalb eines Commands, und die Startprüfung unterscheidet jetzt leeren und gesetzten Einzelwert; beides ist nur über Command- und Starttests belegt, nicht in einem echten Betriebsstart mit Umgebungsvariablen.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 766 von 766 grün (Architektur, SharedKernel, Konten, Personen, Organisationen, API, Ui, E2E inklusive Doku-Screenshots mit axe).
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 13 Seiten, `betreiber/`, `org-admin/einladung-annehmen/`, `org-admin/rollen-vergeben/` gebaut.
- Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Matrix-Audit).

**Restrisiken:**
- Läuft die erste Einladung einer Organisation ab oder scheitert ihr Versand, kann der Global Admin sie nicht erneut schicken; Ausweg ist eine neue Organisation (Doku nennt das).
- Siehe `deferred`: Einladungen werden bis zum Lösch-Dienst (1-7) nicht gelöscht.
- Rückweg zur Einladung nur nach Anmeldung per Code oder Passwort; per Login-Link im neuen Tab muss der Einladungslink erneut geöffnet werden.
- PostgreSQL Row Level Security (AD-6, Stufe 2) bleibt bis zum Start als Dienst zurückgestellt.

### Folgedurchlauf 2026-10-11

**Zusammenfassung:** Erneute Review-Runde auf die fertige Story (4 Layer: Blind, Edge Case, Verification Gap, Intent). Keine Änderung am Produktcode; zwei fehlende Tests ergänzt.

**Geänderte Dateien:**
- `tests/Zeltlotse.Organisationen.Tests/OrganisationenCommandTests.cs` – Test der Mail-Längengrenze (254 geht, 255 nicht) für Anlegen und Einladen.
- `tests/Zeltlotse.Api.Tests/EinladungTests.cs` – HTTP-Test `GET /api/ich/organisationen` mit Rollen in zwei Organisationen (je Eintrag nur eigene Rollen, sortiert nach Name).

**Review:** 32 Befunde (high 0, medium 2, low 24, false 6). Gepatcht: 2 Einträge, beide low (gepatcht: high 0, medium 0, low 2). Zurückgestellt: nichts Neues (Löschen alter Einladungen steht schon in `deferred`). Verworfen mit Begründung im Review Triage Log: Zurücknehmen von Einladungen/Rollen (vom Intent ausgeschlossen), Protokoll-Eintrag außerhalb der Transaktion, fehlende Fremdschlüssel, doppelte Meldung nach gescheitertem Neuladen, doppelte Texte/Prüfregeln, Sortier-Abweichung, `OrgRechte.Server`, `JsonException` bei fremder Rolle, veralteter Rückweg sowie die aus dem ersten Pass übernommenen Befunde; `false`: Sicherheitsprotokoll beim Ausstellen, Snapshot-Sichtbarkeit, zweite Filter-Umgehung, Browser-Durchlauf, allgemeiner Rückweg, Doku-Build.

**Folge-Review empfohlen:** nein – Folgedurchlauf ohne gepatchten high-Befund; die Arbeit ist konvergiert.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 768 von 768 grün.
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 13 Seiten, `betreiber/`, `org-admin/einladung-annehmen/`, `org-admin/rollen-vergeben/` gebaut. Doku, Demo-Daten und Screenshot-Tests waren schon vollständig; in diesem Pass unverändert (nur pixelweise neu erzeugte Offline-Screenshots verworfen).

**Restrisiken:** unverändert wie oben.
