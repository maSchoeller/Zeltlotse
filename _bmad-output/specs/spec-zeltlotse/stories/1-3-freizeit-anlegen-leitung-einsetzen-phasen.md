---
title: 'Freizeit anlegen, Leitung einsetzen, Phasen'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: 'aa78da6e120eeb9f73a62190dc663987cfb14397'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-2-organisationen-und-org-rollen.md'
warnings: [oversized]
deferred:
  - summary: >-
      Angenommene, verworfene und abgelaufene Leitungs-Einladungen (mit Mail-Adresse) werden nie gelöscht.
    evidence: |-
      freizeiten.leitungs_einladung hält die Mail-Adresse auch von Personen ohne Konto; daten.md nennt keine Frist. Der tägliche Lösch-Dienst (AD-16) kommt laut stories.yaml mit Story 1-7; dort Einladungen nach Ablauf plus kurzer Frist löschen und daten.md ergänzen (wie Org-Einladungen aus 1-2).
    location: >-
      src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/LeitungsEinladung.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Es gibt noch keine Freizeit. Org-Admins können weder Freizeiten anlegen noch Leitungen einsetzen (FR-6), Phasen werden nirgends berechnet (FR-45, AD-17), und der Kontext-Endpunkt je Freizeit (AD-29), den der `FreizeitKontextDienst` der App schon aufruft, fehlt.

**Approach:** Das Modul Freizeiten bekommt die Freizeit (Name, erster und letzter Tag, festgeschriebenes Freigabe-Intervall, Abschluss nur als Feld), die Freizeit-Rolle `Leitung` und die Leitungs-Einladung (Zugangslink nach AD-11, an die Mail-Adresse gebunden). Leitung-Vergaben gehen per Ereignis `FreizeitRolleGeaendert` an Personen (Spine-Tabelle *Ereignis-Fluss*). Der API-Host löst Freizeit-Routen auf (Mandant aus der Freizeit, Freizeit-Rollen). Die Web-App bekommt Freizeiten auf der Organisationsseite, eine Verwaltungsseite der Freizeit (Leitung einladen), die Seite „Leitung übernehmen“, „Meine Freizeiten“ auf der Startseite und eine Freizeit-Startseite. Testgetrieben mit HTTP-Tests gegen echtes PostgreSQL inklusive AD-30-Pflichtfällen, Durchlauf-Test, Doku-Seiten mit Screenshots.

## Boundaries & Constraints

**Always:**
- **Phasen (AD-17, FR-45), nur in Freizeiten berechnet:** Zeitzone Europe/Berlin. *in Vorbereitung* vor 00:00 des ersten Tages; *läuft* ab 00:00 des ersten Tages bis vor dem Freizeitende; *Nachbereitung* ab dem Freizeitende (= 00:00 des Tages nach dem letzten Tag) bis zum Abschluss; *gelaufen*, sobald `AbgeschlossenAm` gesetzt ist (Spalte vorhanden, kein Endpunkt setzt sie in dieser Story). Freigabefenster: gültig bis = Freizeitende + Intervall (Kalendertage in Berlin); als Tag angezeigt = letzter Tag + Intervall. Das Intervall wird beim Anlegen an der Freizeit gespeichert (FR-24d); Quelle ist der Vorgabewert **30 Tage** (Konstante an einer Stelle), bis eine Org-Richtlinie existiert. Uhr nur über `TimeProvider`.
- **`Freizeiten.Contracts`:** `IFreizeitZeitpunkte.ZeitpunkteAsync(IReadOnlyCollection<Guid> freizeitIds)` → je Freizeit `(Phase, Freizeitende, FreigabeGueltigBis)` mit eigenem Enum `FreizeitPhase`; `IFreizeitRollenLesen` mit `OrganisationAsync(freizeitId)` (`Guid?`, die einzige Lesestelle außerhalb des Mandantenfilters für die Kontext-Auflösung, AD-6 „adressierte Ressource“) und `RollenAsync(freizeitId, personId)`. Ereignis `Events.FreizeitRolleGeaendert(Guid FreizeitId, Guid PersonId, FreizeitRolleArt Rolle)` mit Enum `FreizeitRolleArt { Leitung = 1, Team = 2 }` (in dieser Story nur `Leitung`).
- **Freizeiten (Besitzer laut Spine), alles `IOrganisationsEigen`:**
  - `Freizeit` (erbt `Aggregat`): `Id`, `OrganisationId`, `Name` (getrimmt, 1–100), `ErsterTag`/`LetzterTag` (`date`, beide Pflicht, `LetzterTag >= ErsterTag`, Jahre 2000–2100), `FreigabeIntervallTage`, `AngelegtAm`, `AngelegtVon` (`PersonId`), `AbgeschlossenAm?`. `LeitungEinsetzen(personId, jetzt)` legt eine Rolle an und das Ereignis ab; ist die Person schon Leitung, passiert nichts (kein zweites Ereignis).
  - `FreizeitRolle`: `OrganisationId`, `FreizeitId`, `PersonId`, `Rolle` (`Leitung`), `VergebenAm`; eindeutig je (Freizeit, Person).
  - `LeitungsEinladung`: wie `OrgEinladung` (1-2): `Id`, `OrganisationId`, `FreizeitId`, `Mail` (normalisiert), `TokenHash` (Zweck `leitung-einladung`), `GueltigBis` (+7 Tage), `EingeladenVon`, Status offen/angenommen/verworfen nur über Methoden (AD-28). Eine neue Einladung verwirft ältere offene derselben Mail für dieselbe Freizeit.
  - Annehmen: Token gültig, offen, nicht abgelaufen **und** Mail des angemeldeten Kontos gleich der Einladungs-Mail, sonst `404` ohne Änderung. Einladung und Freizeit unter Zeilensperre (`FOR UPDATE`). Token-Suche umgeht den Filter (Kontext-Quelle Token). Erfolg → `LeitungEinsetzen`.
  - Einladungs-Mail direkt aus dem Endpunkt nach dem Command (AD-13-Ausnahme, Muster 1-2), Deutsch ohne Anrede: Name der Freizeit, Rolle „Leitung“, Link `<OeffentlicheAdresse>/app/leitung/<token>`, gültig 7 Tage, „Zum Annehmen mit dieser Mail-Adresse anmelden.“ Mail und Token nie in Logs.
- **Mail der angemeldeten Person:** SharedKernel-Schnittstelle `IAngemeldeteMail.MailAsync()` (normalisiert oder `null`), umgesetzt im API-Host aus `IAngemeldet` + `IKontoLesen` – Anfrage-Kontext wie `IKontextRollen`, damit Freizeiten keinen Pfeil zu Konten braucht.
- **Personen (Empfänger, AD-4):** Tabelle `freizeit_rolle` (`OrganisationId`, `FreizeitId`, `PersonId`, `Rolle`) als `IOrganisationsEigen`; Command `FreizeitRolleUebernehmen` (`IEreignisEmpfaenger`, kein `IBenutzerCommand`) legt die Zeile an oder setzt die Rolle; angemeldet mit `EmpfaengtEreignis<FreizeitRolleGeaendert, FreizeitRolleUebernehmen>()`. Noch kein Leser.
- **Rechteprüfung im Host:** `IKontextRollen` bekommt `FreizeitRollenAsync(freizeitId)`; SharedKernel bekommt Enum `FreizeitRolle` und Merkmal `.NurFreizeitRolle(params FreizeitRolle[])` mit Routenwert `freizeitId` (`{freizeitId:guid}`). Middleware: Endpunkt mit `.NurFreizeitRolle` → Organisation über `IFreizeitRollenLesen.OrganisationAsync`; unbekannt → `404`; sonst Mandant setzen; keine Freizeit-Rolle (auch Org-Admin, Global Admin) → `404`; falsche Rolle → `403`. Endpunkte mit `.NurOrgRolle` setzen den Mandanten weiter aus `orgId`; beide Merkmale an einem Endpunkt sind verboten.
- **Erlaubte Aktionen:** `Aktionsschluessel` + `FreizeitenAnlegen = "freizeiten-anlegen"`, `LeitungEinsetzen = "leitung-einsetzen"` (nur Org-Admin), berechnet aus denselben Rollenlisten wie die Merkmale.
- **Endpunkte (Datenformen in `Api.Contracts/Freizeiten/`, Datum als `DateOnly`, Phase als `Gemeinsam.Phase`):**

  | Endpunkt | Recht | Ergebnis |
  |---|---|---|
  | `GET /api/organisationen/{orgId}/freizeiten` | Org-Admin, Org-Überblick | `200 {freizeiten: [{id, name, ersterTag, letzterTag, phase}], erlaubt}` nach erstem Tag absteigend |
  | `POST /api/organisationen/{orgId}/freizeiten` `{name, ersterTag, letzterTag}` | Org-Admin | `201 {id}`; `400` je Feld |
  | `GET /api/organisationen/{orgId}/freizeiten/{freizeitId}` | Org-Admin | `200 {id, name, ersterTag, letzterTag, phase, leitung: [{personId, vorname, nachname}], einladungen: [{id, mail, gueltigBis}]}`; Freizeit einer anderen Organisation → `404` |
  | `POST /api/organisationen/{orgId}/freizeiten/{freizeitId}/leitung/einladungen` `{mail}` | Org-Admin | `202 {id}` + Mail; `400` |
  | `POST /api/freizeiten/leitung/annehmen` `{token}` | angemeldet, zugriffsbegrenzt | `200 {freizeitId}`, sonst `404` |
  | `GET /api/freizeiten/{freizeitId}/kontext` | Leitung, Team | `200 FreizeitKontext` (`rollen`, `phase`, `schalter: []`, `erlaubt: []`) |
  | `GET /api/freizeiten/{freizeitId}` | Leitung, Team | `200 {id, name, ersterTag, letzterTag, phase, freigabeBis, leitung: [{vorname, nachname}]}` |
  | `GET /api/ich/freizeiten` | angemeldet, `.Organisationsuebergreifend()` | `200 [{freizeitId, name, ersterTag, letzterTag, phase, rollen}]` nur eigene Rollen, nach erstem Tag |

- **Sicherheitsprotokoll:** `Rollenvergabe` nach erfolgreichem Annehmen (Person, Organisation, Bezug = Einladung); `LinkEinloesungFehlgeschlagen` bei jedem gescheiterten.
- **Demo-Daten (nur Commands, idempotent):** fehlt in „Jugendwerk Musterstadt“ die Freizeit `DemoKatalog.Freizeit` („Zeltlager Sommer 2027“, 02.–13.08.2027), legt Andrea sie an; Jonas und Miriam (`DemoRolle.Leitung`) nehmen je eine Leitungs-Einladung per Command an. Ereignisse werden vom normalen Zustelldienst zugestellt.
- **Web-App (nur Ui.Komponenten-Bausteine, Texte in `Anzeige`: Phase „in Vorbereitung“/„läuft“/„Nachbereitung“/„gelaufen“ als `Status`, Zeitraum „02.08.2027 – 13.08.2027“):**
  - `/organisationen/{orgId}`: Card „Freizeiten“ für jede Rolle (Datentabelle Name, Zeitraum, Phase; Leer-Zustand „Noch keine Freizeit angelegt.“; Name als Link auf die Verwaltungsseite nur mit `leitung-einsetzen`); nur mit `freizeiten-anlegen` Card „Freizeit anlegen“ (Felder „Name der Freizeit“, „Erster Tag“, „Letzter Tag“ als `type=date`, Knopf „Freizeit anlegen“, danach „Gespeichert: Die Freizeit ist angelegt.“ und neu geladene Liste).
  - `/organisationen/{orgId}/freizeiten/{freizeitId}`: h1 Name, Zeitraum, Phase; Card „Leitung“ (Datentabelle Name oder „Noch keine Leitung eingesetzt.“), Card „Offene Einladungen“, Card „Leitung einladen“ (Feld „Mail-Adresse“, Knopf „Einladung schicken“, danach „Gespeichert: Die Einladung ist verschickt.“); Link „Zur Organisation“.
  - `/leitung/{token}`: h1 „Leitung übernehmen“, Erklärtext, Knopf „Einladung annehmen“ (POST erst beim Klick) → `freizeiten/{freizeitId}`; `404` → „Fehler: Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.“; Rückweg nach Anmeldung wie `/einladung/{token}`.
  - `/` (Start): je eigener Freizeit eine Card (Titel Name, Zeitraum, Phase, „Rolle: Leitung“, Knopf „Freizeit öffnen“) vor den Organisations-Cards; ohne Freizeit bleibt „Noch keine Freizeit geöffnet.“
  - `/freizeiten/{freizeitId}`: lädt Kontext und Freizeit; h1 Name, Phase, „Zeitraum: …“, „Rolle: …“, „Leitung: Jonas Hüttner, Miriam Zeltner“, „Einsicht in Freizeitpässe bis <Datum>“. `403`/`404` → „Diese Seite ist nicht verfügbar.“
- **Tests (AD-30), Test vor dem Code:** neues `Zeltlotse.Freizeiten.Tests` (Phasen und Freigabefenster mit `FakeTimeProvider` an allen Grenzen inkl. Sommerzeit-Wechsel, Validierung, Einladung, `LeitungEinsetzen`, Mandantenfilter je Entität); `Personen.Tests` für den Empfänger; neues `Zeltlotse.Ablaeufe.Tests` (Durchlauf Einladen → Annehmen → Outbox → `personen.freizeit_rolle`); HTTP-Tests für jede Matrix-Zeile; bUnit für Client und Seiten; Doku-Screenshots (Handy, Laptop).
- **Doku:** `org-admin/freizeit-anlegen.md` (Freizeit anlegen, Leitung einladen; Bilder `organisation`, `freizeit-verwalten`), `leitung/freizeit-oeffnen.md` (Einladung annehmen, Startseite, Phasen und Freigabefenster erklärt; Bilder `leitung-einladung`, `app-start`, `freizeit-start`), Anpassungen `org-admin/index.md`, `leitung/index.md`, `org-admin/rollen-vergeben.md` (falls Bild-Text betroffen), `daten.md` (Vorgabe 30 Tage, Leitungs-Einladung speichert die Mail-Adresse, Org-Admin sieht von der Leitung nur Namen).

**Never:**
- Kein Abschließen, kein Schreibschutz-, kein Funktions-Schalter-Pipeline-Schritt (3-8, 3-7); keine Anmeldelinks, keine Team-Rolle aus Anmeldungen (2-x), kein Cockpit (2-6), keine Freizeit-Einstellungen, kein Ort, keine Org-Richtlinien.
- Kein Bearbeiten oder Löschen von Freizeiten, kein Entziehen der Leitung, kein Zurückziehen von Einladungen.
- Kein Zugriff von Org-Admin, Org-Überblick oder Global Admin auf Freizeit-Endpunkte ohne eigene Freizeit-Rolle; keine Mail-Adressen der Leitung in Antworten.
- Keine weiteren Ereignisse, keine Änderung der Ereignis-Fluss-Tabelle, kein Pfeil Freizeiten → Konten, keine neuen Problemcodes.
- Keine Pop-ups, kein Token in Browser-Speicher oder Query-String, keine Lockerung von CSP, Antiforgery oder Cookie-Richtlinie. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Freizeit anlegen | Org-Admin, gültige Angaben | `201 {id}`; Liste zeigt sie mit Phase; Intervall 30 gespeichert | leerer Name, 101 Zeichen, fehlender Tag, letzter vor erstem Tag, Jahr 1999 → `400` je Feld |
| Rollen Org-Routen | Org-Admin / Org-Überblick / ohne Rolle / Org-Admin von B / Global Admin ohne Rolle | Liste `200` (Überblick mit `erlaubt=[]`), Anlegen/Verwalten/Einladen `200`/`201`/`202` bzw. Überblick `403`; sonst `404` | – |
| Fremde Freizeit | `GET …/organisationen/{A}/freizeiten/{FreizeitVonB}` als Org-Admin von A | `404` | – |
| Annehmen gültig | angemeldet mit eingeladener Mail | `200 {freizeitId}`, Leitung gesetzt, `Rollenvergabe` protokolliert, Ereignis in der Outbox | – |
| Token-Fälle | abgelaufen (+7 Tage +1 Min.), zweites Annehmen, verworfen, Org-Einladungs-Token, Login-Token, unbekannt, Formfehler, andere Mail | jeweils `404`, `LinkEinloesungFehlgeschlagen`, nichts geändert; Einladung für Freizeit X macht nicht Leitung von Y | – |
| Doppelt Leitung | zweite Einladung derselben Person angenommen | eine Rolle, ein Ereignis | – |
| Freizeit-Routen | Leitung / Org-Admin ohne Freizeit-Rolle / Leitung von Y für X / unbekannte Freizeit / ohne Sitzung | `200` / `404` / `404` / `404` / `401` | – |
| Phasen | Uhr am Vortag 23:59, erster Tag 00:00, letzter Tag 23:59, Tag danach 00:00 (Berlin, auch über Zeitumstellung); `AbgeschlossenAm` gesetzt | InVorbereitung, Laeuft, Laeuft, Nachbereitung; Gelaufen | – |
| Freigabefenster | letzter Tag 13.08.2027, Intervall 30 | `freigabeBis` 12.09.2027; gültig bis 13.09.2027 00:00 Berlin | – |
| Persönlich | Leitung A, Person ohne Rolle, Leitung in zwei Organisationen | `/api/ich/freizeiten` nur eigene, nur Vertragsfelder; leer für ohne Rolle | – |
| Durchlauf | Einladen, Annehmen, Outbox zustellen (zweimal) | genau eine Zeile `personen.freizeit_rolle` mit Rolle Leitung und `OrganisationId` | – |
| Mandantenfilter | Freizeit/Rolle/Einladung in A, Kontext B bzw. ohne | nichts | – |
| Demo-Daten | Start mit Demo zweimal | eine Freizeit, zwei Leitungen (Jonas, Miriam), nichts doppelt | – |

</intent-contract>

## Code Map

- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/FreizeitenModul.cs:15-22` -- leeres Gerüst (`AddModul<FreizeitenDbContext, FreizeitenLeseDbContext>`, `MapFreizeitenEndpoints => api`); einziger öffentlicher Typ bleibt `FreizeitenModul` (`tests/Zeltlotse.ArchitectureTests/TypRegelnTests.cs:55-70`). `Daten/FreizeitenDbContext.cs:27-32` leeres `FreizeitenModell.Konfigurieren`; Migration `20261011110026_Initial` nur Outbox/Inbox. csproj braucht Verweise auf `Personen.Contracts` (Namen) und `Zeltlotse.Api.Contracts`; `InternalsVisibleTo` für `Zeltlotse.Api` und `Zeltlotse.Freizeiten.Tests` ist da. `Contracts/Events/EventsNamensraum.cs` leer.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/` -- Vorbild für alles: `Domaene/OrgEinladung.cs` (Status-Methoden, Ablauf), `Einladungen/EinladungAnlegen.cs:15-47` (Mandant-Prüfung, `MailRegel.cs:8`), `Einladungen/EinladungAnnehmen.cs:24-90` (Token, `FOR UPDATE` per `FromSql`, `MandantenKontext.Setzen`), `EinladungsMail.cs`, `Verwaltung/OrganisationenEndpunkte.cs` (TypedResults, `IKontextRollen`, `/api/ich` mit `IgnoreQueryFilters()`), `Rollen/OrgRechte.cs` (Rollenlisten, `Erlaubt`), `OrganisationenModul.cs:21-47` (Routen-Gruppen, `{orgId:guid}`), `Daten/OrganisationenDbContext.cs:43-72` (Modell).
- `src/Zeltlotse.SharedKernel/Rechte/` -- `IKontextRollen.cs:8-14`, `Rechtemerkmale.cs:13-21` (`OrgRouteWert`, `NurOrgRolle`), `OrgRolle.cs`; hier `FreizeitRolle`, `NurFreizeitRolle`, `FreizeitRouteWert = "freizeitId"`, `IAngemeldeteMail`. `Anmeldung/IAngemeldet.cs` (nur `KontoId`/`PersonId`). `Aggregat.cs:9-33` (`EreignisAblegen`), `Ereignisse/IEreignisEmpfaenger.cs:35` (`EmpfaengtEreignis`), `Umschlag.cs:9`. `Zugangslinks/` (Token, Hash, `MailAdresse`).
- `src/Zeltlotse.Api/Rechte/RechtePruefung.cs:16-59` -- Middleware (Mandant aus `orgId`, 404/403); um Freizeit-Zweig erweitern. `Rechte/KontextRollen.cs:12-35` -- um Freizeit-Rollen aus `IFreizeitRollenLesen` erweitern (Cache je Anfrage). `Sicherheit/WebSicherheit.cs:31` -- Registrierung `IKontextRollen`; hier `IAngemeldeteMail`.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs:15-19,40-54` (Freizeit, `DemoRolle.Leitung` = Jonas, Miriam), `Demo/DemoDaten.cs:42-112` (Muster `OrganisationAsync`, `MandantenKontext.Setzen`, interne Commands über `ISender`).
- `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs:16-62` -- `FreizeitKontext`, `Phase`, `FreizeitRolle`, `FunktionsSchalter`, `Aktionsschluessel` (neue Schlüssel). Neuer Ordner `Freizeiten/`.
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs:53-97` -- neue Tabelle `freizeit_rolle` + Migration; `PersonenModul.cs:25` Registrierung; Vorbild Empfänger `tests/Zeltlotse.Testmodul/Empfang/Empfaenger.cs:10-14`, `TestmodulModul.cs:19-21`. Namen: `Personen.Contracts/Namen/IPersonenNamen.cs:30-34`.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs:30-47` (bisher nur `KontextLadenAsync`), Muster `Api/OrganisationenApiClient.cs` (Ergebnis-Records mit Feldfehlern, `LadenAsync<T>` 403/404 → null). `Kontext/FreizeitKontextDienst.cs`. `Texte/Anzeige.cs` (`Datum`, `Name`, `Rolle`).
- `src/Zeltlotse.App/Pages/Start.razor:13-40`, `Features/Organisationen/OrganisationAnsicht.razor` (Cards, `Spalte<T>`, Formular, `NichtVerfuegbar`), `EinladungOeffnen.razor` (POST beim Klick, Rückweg über `AppSitzung`), `_Imports.razor` (neuer Namensraum `Features.Freizeiten`).
- `src/Zeltlotse.Ui.Komponenten/` -- `Card`, `Datentabelle`, `Feld` (`Typ="date"` möglich), `Formular`, `Knopf`, `Meldung`, `Status`, `LeerZustand`, `Ladezustand`; keine neuen Bausteine nötig.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs:21-97` -- `Organisationsuebergreifend` + `GET /api/ich/freizeiten`; Zähltest `mitOrg.Count == 3` anpassen; neue Regel: jede `freizeitId`-Route trägt `.NurFreizeitRolle` und `{freizeitId:guid}`, keine Route trägt beide Merkmale; Annehmen zugriffsbegrenzt. `AnmeldeHelfer.cs`, `OrgHelfer.cs` (Org mit Org-Admin, Einladen/Annehmen) -- Vorbild `FreizeitHelfer`. `DemoDatenTests.cs:51-65` erweitern.
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` -- `Uhr` (FakeTimeProvider ab 2026-07-01 08:00Z), `Mails`, `OutboxJetztZustellenAsync()`; `MailOrdner`-Linkmuster um `/app/leitung/` erweitern.
- `tests/Zeltlotse.Organisationen.Tests/` -- Vorbild für `Zeltlotse.Freizeiten.Tests` und `Zeltlotse.Ablaeufe.Tests` (csproj, `AssemblyInfo.cs`, Fixture); beide in `Zeltlotse.slnx`. `tests/Zeltlotse.ArchitectureTests/EreignisFlussTests.cs` prüft das neue Paar gegen die Spine (Zeile ist da).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:25-200` -- `App_Start`/`App_Offline` (Jonas) prüfen heute „Noch keine Freizeit geöffnet.“ → auf Freizeit-Card umstellen; `Organisation` (Andrea) erweitern; neue Bilder `freizeit-verwalten`, `leitung-einladung` (Token z. B. per Einladung über die Oberfläche und `host.Mails`), `freizeit-start`. `E2EHilfe.PerLinkAnmeldenAsync`, `BildAsync`. E2E-Host läuft mit echter Uhr → Demo-Freizeit ist „in Vorbereitung“.
- `tests/Zeltlotse.Ui.Tests/OrganisationenSeitenTests.cs`, `KontextUndTransportTests.cs` -- Muster für Seiten- und Client-Tests.
- `doku/src/content/docs/org-admin/index.md`, `leitung/index.md`, `daten.md` (Abschnitte „Freigabefenster“, „Organisationen und Rollen“), `astro.config.mjs` (autogenerate je Ordner, nichts zu ändern). `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (Bilder verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Freizeiten.Tests/` (neu) -- ZUERST: Phasen/Freigabefenster (`FakeTimeProvider`, Berlin-Grenzen, Zeitumstellung), Validierung Anlegen, Leitungs-Einladung (Ablauf, einmal, Verwerfen, Mail-Bindung, Freizeit-Bindung), `LeitungEinsetzen` (Ereignis einmal), Mandantenfilter für `Freizeit`, `FreizeitRolle`, `LeitungsEinladung`.
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST: Empfänger `FreizeitRolleUebernehmen` (anlegen, idempotent, Mandant aus Umschlag).
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: `FreizeitenTests`, `LeitungEinladungTests`, `FreizeitKontextTests` für jede Matrix-Zeile (401/403/404, Token-Fälle, Protokoll, Mail-Inhalt); `EndpunktListeTests`; `DemoDatenTests`.
- `tests/Zeltlotse.Ablaeufe.Tests/` (neu) -- ZUERST: Durchlauf-Test `FreizeitRolleGeaendert`.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: `FreizeitenApiClient`-Methoden, Seiten Organisation (Freizeiten-Cards), Freizeit verwalten, Leitung übernehmen (Rückweg, Fehler), Start mit Freizeiten, Freizeit-Start, `Anzeige.Phase`/`Zeitraum`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Bilder `organisation`, `freizeit-verwalten`, `leitung-einladung`, `app-start`, `freizeit-start` (Handy, Laptop); `App_Offline` anpassen.
- `src/Zeltlotse.SharedKernel/` -- `FreizeitRolle`, `NurFreizeitRolle`, `IKontextRollen.FreizeitRollenAsync`, `IAngemeldeteMail`.
- `src/Modules/Freizeiten/` -- Contracts (Lese-Schnittstellen, Ereignis), Domäne, Modell, Migration, Commands (Freizeit anlegen, Leitung einladen, Einladung annehmen), Endpunkte, Mail, Umsetzungen der Contracts.
- `src/Modules/Personen/` -- Tabelle, Migration, Empfänger, Registrierung.
- `src/Zeltlotse.Api/` -- Freizeit-Zweig in `RechtePruefung`, `KontextRollen`, `IAngemeldeteMail`-Umsetzung, Demo-Daten.
- `src/Zeltlotse.Api.Contracts/` -- `Freizeiten/`-Datenformen, Aktionsschlüssel.
- `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` -- Client, Texte, Seiten unter `Features/Freizeiten/`, Organisationsseite, Start.
- `doku/` -- neue Seiten und Anpassungen laut Always; Screenshots aus den Tests.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (inklusive Architektur, Freizeiten, Personen, Abläufe, API, Ui, E2E) und die Screenshots `organisation`, `freizeit-verwalten`, `leitung-einladung`, `app-start`, `freizeit-start` (Handy, Laptop) sind geschrieben.
- Given Andrea (Org-Admin) legt in der Web-App eine Freizeit an und lädt eine Mail-Adresse als Leitung ein, when die Person den Link öffnet, sich per Code anmeldet und „Einladung annehmen“ tippt, then landet sie auf der Freizeit-Startseite mit „Rolle: Leitung“ und Phase, ihre Startseite zeigt die Freizeit, und Andreas Verwaltungsseite listet sie unter „Leitung“.
- Given Andrea hat keine Freizeit-Rolle, when sie `/app/freizeiten/{id}` öffnet, then sieht sie „Diese Seite ist nicht verfügbar.“
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler und die neuen Seiten unter Org-Admin und Leitung zeigen die Bilder.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 27 findings — high 0, medium 1, low 18, false 8, maybe-false 0
- findings:
  - `[false]` `[reject]` (intent) „Falsche Rolle 403“ an Freizeit-Routen nur über einen Test-Endpunkt — beide echten Freizeit-Endpunkte erlauben Leitung und Team, eine falsche Freizeit-Rolle gibt es dort nicht; der 403-Zweig der Middleware ist per HTTP-Test belegt (Design Notes).
  - `[false]` `[reject]` (intent) Ablauf Anlegen → Einladen → Annehmen nie im Browser — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; HTTP-, Durchlauf- und bUnit-Tests decken die Kette ab.
  - `[false]` `[reject]` (intent) 401 nur geerbt — `Jeder_angemeldete_Endpunkt_antwortet_ohne_Sitzung_mit_401_ProblemDetails` läuft über alle Endpunkte, auch die neuen.
  - `[false]` `[reject]` (intent) Screenshots zeigen nur Zustände vor der Aktion — verlangt sind Screenshots der neuen Oberflächen (Handy, Laptop), alle fünf liegen vor und sind verlinkt.
  - `[false]` `[reject]` (intent) „Test vor dem Code“ nicht im Diff sichtbar — Prozessaussage.
  - `[false]` `[reject]` (intent) Umfang über die Einzeiler-Beschreibung hinaus — alles von Spec und AD-4/AD-8/AD-11/AD-26 verlangt.
  - `[low]` `[reject]` (blind) Rollen als Liste, Schlüssel (Freizeit, Person), `LeitungEinsetzen` prüft nur Leitung — heute gibt es nur die Rolle Leitung, der Fall Team → Leitung ist nicht erreichbar; die Story der Team-Rolle (Anmeldung bestätigen) legt den Wechsel fest.
  - `[false]` `[reject]` (blind) Ereignis kann keine Rolle entfernen — Entziehen ist ausgeschlossen (Never); ein späterer Enum-Wert ist additiv („nur anhängen“).
  - `[medium]` `[defer]` (blind) Leitungs-Einladungen (mit Mail-Adresse) werden nie gelöscht, `daten.md` nennt keine Frist — Lösch-Dienst kommt mit 1-7, wie bei Org-Einladungen aus 1-2; siehe `deferred`.
  - `[low]` `[reject]` (blind) Einladen einer Person, die schon Leitung ist, wird nicht abgefangen — harmlos (Annehmen ändert nichts); Fix wäre neue Fehlerlogik.
  - `[low]` `[reject]` (blind) Seite „Leitung übernehmen“ nennt die Freizeit nicht — die Mail nennt sie; Fix wäre ein neuer lesender Token-Endpunkt (wie in 1-2 verworfen).
  - `[low]` `[reject]` (blind) Mail nennt weder Organisation noch Einladende — Fix bräuchte zusätzliche Lesezugriffe; Name der Freizeit, Rolle und Link genügen.
  - `[low]` `[patch]` (blind) „Meine Freizeiten“ aufsteigend sortiert, alte Freizeiten stehen über der aktuellen — Sortierung absteigend nach erstem Tag, wie die Liste der Organisation.
  - `[low]` `[reject]` (blind) „Einsicht in Freizeitpässe bis …“ auch nach Ablauf — die Aussage bleibt wahr; Fix wäre zusätzliche Verzweigung.
  - `[low]` `[reject]` (blind) Login-Link verliert den Rückweg — bewusste Entscheidung aus 1-2 (kein Token in URL oder Browser-Speicher), Doku nennt den Weg.
  - `[low]` `[reject]` (blind) Mail-Fehler nach dem Speichern — wie in 1-2: Org-Admin lädt erneut ein, die neue verwirft die alte.
  - `[low]` `[reject]` (blind) Keine Zugriffsbegrenzung am Einladen — wie in 1-2: nur Org-Admin mit Sitzung; AD-25 nennt Anmeldung, Zugangslinks, Gast-Anmeldung.
  - `[low]` `[reject]` (blind) Uneinheitliche Namens-Sortierung (Datenbank vs. `CurrentCulture`) — kosmetisch, wie in 1-2.
  - `[low]` `[reject]` (blind) Schwache Datumsprüfung, doppelte Namen — NFR-12 hält Pflichtfelder knapp; Fix wäre neue Fachlogik.
  - `[low]` `[reject]` (edge) Team → Leitung verletzt den Primärschlüssel — gleiche Ursache wie blind „Rollen als Liste“, verworfen.
  - `[low]` `[reject]` (edge) `Rollenvergabe` protokolliert, obwohl die Person schon Leitung war — wie in 1-2 bewusst bei jedem gültigen Annehmen; Fix wäre ein zusätzliches Ergebnisfeld.
  - `[low]` `[reject]` (edge) Gleichzeitiges Einladen lässt zwei offene Einladungen — beide gültig und harmlos, wie in 1-2.
  - `[low]` `[reject]` (edge) Mail-Fehler nach dem Commit — gleiche Lage wie blind, verworfen.
  - `[low]` `[reject]` (edge) Demo-Daten nach Teilabbruch ohne Leitung — wie in 1-1/1-2: nur bei Absturz zwischen Commands beim Start.
  - `[false]` `[reject]` (edge) Fehlender Fremdschlüssel Einladung → Freizeit — Freizeiten lassen sich nicht löschen, Einladungen entstehen nur zu einer vorhandenen Freizeit (Handler lädt sie per `SingleAsync`).
  - `[low]` `[patch]` (verification-gap) Zeilensperre beim gleichzeitigen Annehmen ungetestet — zwei Nebenläufigkeitstests in `FreizeitenCommandTests` ergänzt (gleiches Token viermal, zwei Eingeladene gleichzeitig).
  - `[low]` `[reject]` (verification-gap, other) Team → Leitung verletzt den Primärschlüssel — gleiche Ursache wie blind, verworfen.

## Design Notes

- **Leitung per Einladung:** FR-6 sagt nicht, wie eingesetzt wird. Ein Personenverzeichnis der Organisation gibt es vor FR-7 nicht, und Freizeiten darf Konten nicht lesen (AD-2). Darum dasselbe Muster wie die Org-Einladung aus 1-2 (AD-11: einmal, 7 Tage, Mail-Bindung), aber im Besitzer-Modul Freizeiten. Die Mail der angemeldeten Person liefert der Host über `IAngemeldeteMail` (Anfrage-Kontext wie `IKontextRollen`, AD-12); der Pfeil-Test gilt für Module, nicht für den Host.
- **Zwei Pflichtangaben (NFR-12):** Name und Zeitraum (erster/letzter Tag als ein Zeitraum). Ohne Zeitraum gäbe es weder Phase noch Freigabefenster.
- **Freigabe-Intervall 30 Tage:** NFR-5 verlangt Vorgabewerte, eine Org-Richtlinie gibt es noch nicht. 30 Tage decken Nachfragen und Belege der Nachbereitung ab und bleiben datensparsam. Die Zahl wird je Freizeit gespeichert, damit eine spätere Richtlinie bestehende Freizeiten nicht ändert (FR-24d).
- **Ereignis jetzt, Leser später:** `FreizeitRolleGeaendert` steht in der Spine; ohne Zustellung ab der ersten Vergabe fehlten Personen später die Leitungen bestehender Freizeiten (Freigabe 2-5). Datensatz des Umschlags ist die Freizeit (Aggregat), damit die Folgenummer alle Rollenwechsel einer Freizeit ordnet.
- **Kontext ohne Schalter und Aktionen:** Schalter (3-7) und Freizeit-Aktionen (ab 2-x) gibt es noch nicht; `schalter` und `erlaubt` sind leer, die Form steht. Neue Aktionsschlüssel in `Gemeinsam/` sind additiv wie in 1-2.
- **Org-Rollen sehen Freizeiten nur als Zeile:** Liste und Verwaltung laufen über Org-Routen (Mandant aus `orgId`); Freizeit-Routen verlangen eine Freizeit-Rolle (AD-12). Die Verwaltungsseite zeigt nur Namen der Leitung, keine Inhalte.
- **Schreibschutz und Schalter in der Pipeline** kommen mit ihren Stories (3-8 Abschluss, 3-7 Module); vorher gäbe es nichts zu prüfen.
- **AD-30-Pflichtfälle:** Organisation: berechtigt, falsche Rolle `403` (Org-Überblick), fremde Organisation `404`; Freizeit: berechtigt, ohne Freizeit-Rolle `404`, falsche Rolle `403` entfällt (Leitung und Team dürfen beide Endpunkte; der `403`-Zweig der Middleware wird mit einem nur in der Umgebung `Testing` gemappten Test-Endpunkt mit `.NurFreizeitRolle(Team)` geprüft (Leitung → `403`)); persönlich: nur eigene, Elternteil-Fall kommt mit 1-4; Token: gültig, abgelaufen/verworfen/benutzt `404`, anderer Zweck `404`, Einladung für X wirkt nicht in Y.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `org-admin/freizeit-anlegen/`, `leitung/freizeit-oeffnen/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Freizeit anlegen, Leitung einsetzen, Phasen (CAP-3, FR-6, FR-45, AD-17, AD-29):
- **Freizeiten:** `Freizeit` (Aggregat, Name, erster/letzter Tag, festgeschriebenes Freigabe-Intervall 30 Tage, `AbgeschlossenAm` nur als Feld), `FreizeitRolle` (Leitung), `LeitungsEinladung` (AD-11: einmal, 7 Tage, Mail-Bindung, Zweck `leitung-einladung`); Phasen und Freigabefenster in `FreizeitZeiten` mit Tagesgrenzen in Europe/Berlin; Commands Anlegen, Leitung einladen, Annehmen (Zeilensperre, Token-Suche als einzige Umgehung des Filters); acht Endpunkte laut Spec; Contracts `IFreizeitZeitpunkte`, `IFreizeitRollenLesen`, Ereignis `FreizeitRolleGeaendert`.
- **Personen:** Tabelle `freizeit_rolle`, Empfänger `FreizeitRolleUebernehmen` (Ereignis-Fluss-Zeile aus der Spine).
- **API-Host:** Freizeit-Zweig der Rechteprüfung (Mandant aus der Freizeit, ohne Freizeit-Rolle 404, falsche Rolle 403, beide Merkmale verboten), Freizeit-Rollen in `KontextRollen`, `IAngemeldeteMail`, Demo-Freizeit mit Jonas und Miriam als Leitung.
- **Web-App:** Freizeiten-Cards und „Freizeit anlegen“ auf der Organisationsseite, Verwaltungsseite mit „Leitung einladen“, Seite „Leitung übernehmen“, Freizeit-Cards auf der Startseite (neueste zuerst), Freizeit-Startseite mit Phase, Zeitraum, Rolle, Leitung und Freigabefenster.
- **Doku:** neue Seiten `org-admin/freizeit-anlegen.md`, `leitung/freizeit-oeffnen.md`; Anpassungen an `org-admin/index.md`, `leitung/index.md`, `org-admin/rollen-vergeben.md`, `erste-schritte/zeltlotse-oeffnen.md`, `daten.md`; Screenshots `organisation`, `freizeit-verwalten`, `leitung-einladung`, `app-start`, `freizeit-start` (Handy, Laptop).

**Abweichungen (begründet):** `MailRegel` liegt jetzt in SharedKernel (von Organisationen und Freizeiten gemeinsam genutzt, unverändert). Der Annehmen-Command bekommt die Konto-Mail als Feld (Endpunkt aus `IAngemeldeteMail`, Demo aus dem Katalog), damit der Handler ohne HTTP-Kontext auskommt. Regel der Endpunktliste: jede `freizeitId`-Route mit `:guid`; Routen mit `freizeitId` ohne `orgId` tragen `.NurFreizeitRolle`; kein Endpunkt trägt beide Merkmale.

**Dateien:**
- `src/Modules/Freizeiten/` – Contracts, Domäne, Modell, Migration `FreizeitRolleEinladung`, Commands, Endpunkte, Mail, Rechte.
- `src/Modules/Personen/` – Tabelle, Migration `FreizeitRollenListe`, Empfänger, Registrierung.
- `src/Modules/Organisationen/` – nutzt `MailRegel` aus SharedKernel.
- `src/Zeltlotse.SharedKernel/` – `FreizeitRolle`, `.NurFreizeitRolle`, `IKontextRollen.FreizeitRollenAsync`, `IAngemeldeteMail`, `MailRegel`.
- `src/Zeltlotse.Api/` – `Rechte/RechtePruefung.cs`, `Rechte/KontextRollen.cs` (+ `AngemeldeteMail`), `Sicherheit/WebSicherheit.cs`, `Demo/DemoDaten.cs`.
- `src/Zeltlotse.Api.Contracts/` – `Freizeiten/Freizeiten.cs`, Aktionsschlüssel.
- `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` – Client-Methoden, Texte, Seiten unter `Features/Freizeiten/`, Organisationsseite, Start.
- `tests/` – neu `Zeltlotse.Freizeiten.Tests`, `Zeltlotse.Ablaeufe.Tests`; `Api.Tests` (`FreizeitenTests`, `LeitungEinladungTests`, `FreizeitKontextTests`, `FreizeitHelfer`, Test-Endpunkt für 403, Endpunktliste, Demo-Daten); `Personen.Tests/FreizeitRollenTests`; `Ui.Tests/FreizeitenSeitenTests`; `E2E.Tests/DokuScreenshotTests`; `Testing/MailOrdner`.
- `doku/` – neue und geänderte Seiten, Screenshots.

**Review:** 27 Befunde (high 0, medium 1, low 18, false 8). Gepatcht (2, beide low): Nebenläufigkeitstests für die Zeilensperre beim Annehmen; „Meine Freizeiten“ absteigend nach erstem Tag. Zurückgestellt: Löschen alter Leitungs-Einladungen (Lösch-Dienst, Story 1-7). Verworfen mit Begründung im Review Triage Log: Team → Leitung (noch unerreichbar), Einladen bestehender Leitung, Vorschau auf der Einladungsseite, Organisation in der Mail, „Einsicht bis“ nach Ablauf, Rückweg bei Login-Link, Mail-Fehler nach dem Commit, Zugriffsbegrenzung beim Einladen, Sortier-Abweichung, Datums-/Namensprüfung, `Rollenvergabe` bei schon vorhandener Rolle, gleichzeitiges Einladen, Demo-Daten nach Teilabbruch sowie alle `false`-Befunde.

**AD-30 (Hinweis des Aufrufers):** Jeder neue Endpunkt hat HTTP-Tests gegen echtes PostgreSQL: berechtigt; falsche Rolle → 403 (Org-Überblick an Anlegen/Verwalten/Einladen; Freizeit-Middleware über Test-Endpunkt `.NurFreizeitRolle(Team)`); fremde Organisation → 404 (ohne Rolle, Org-Admin von B, Global Admin ohne Rolle, unbekannte Organisation/Freizeit, Freizeit von B auf Route von A, Leitung von Y für X); Token-Fälle (abgelaufen, zweites Annehmen, verworfen, Org-Einladungs-Token, Login-Token, unbekannt, Formfehler, kein Token, andere Mail → 404 mit Protokolleintrag; Einladung für X wirkt nicht in Y); ohne Sitzung → 401; persönlicher Endpunkt nur eigene Freizeiten (Elternteil-Fall kommt mit 1-4).

**Folge-Review empfohlen:** nein – nur zwei low-Einträge gepatcht (gepatcht: high 0, medium 0, low 2).

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 879 von 879 grün (Architektur, SharedKernel, Konten, Personen, Organisationen, Freizeiten, Abläufe, API, Ui, E2E inklusive Doku-Screenshots).
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 15 Seiten, `org-admin/freizeit-anlegen/` und `leitung/freizeit-oeffnen/` gebaut.
- Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Matrix-Audit).

**Restrisiken:**
- Siehe `deferred`: Leitungs-Einladungen werden bis zum Lösch-Dienst (1-7) nicht gelöscht.
- Die Screenshots laufen gegen die echte Uhr; die angezeigte Phase der Demo-Freizeit (August 2027) ändert sich mit der Zeit. Datumsfelder zeigen im Screenshot den englischen Platzhalter des Browsers.
- Auf einem frischen Klon schlägt die Doku-Bildregel der Architekturtests nur fehl, wenn Screenshots fehlen; sie sind mit eingecheckt.
- Rückweg zur Einladung nur nach Anmeldung per Code oder Passwort (wie 1-2).
