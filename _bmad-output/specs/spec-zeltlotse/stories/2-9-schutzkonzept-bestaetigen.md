---
title: 'Schutzkonzept bestätigen'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: 'b147138164b30412e3dc68d9da8acd5989c02755'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-8-notfall-sicherheitskette-und-schnellsuche.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-7-dokumente-und-elterninfo.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Leitung kann kein Schutzkonzept an die Freizeit hängen, und niemand sieht, wer aus dem Team es gelesen hat (CAP-16, FR-30).

**Approach:**
- Das Schutzkonzept ist eines der vorhandenen Dokumente der Freizeit (Freizeiten besitzt Dokumente, AD-15). Es kann eine eigene Datei oder ein eingebundenes Org-Dokument sein.
- **Modul Sicherheit** besitzt den Verweis „welches Dokument ist das Schutzkonzept“ und die Bestätigungen „gelesen“ (Datenbesitz: Schutzkonzept-Bestätigung).
  - Die Leitung wählt das Dokument per Autosave.
  - Jedes Teammitglied (Freizeit-Rolle Leitung oder Team) bestätigt „gelesen“ für genau dieses Dokument.
  - Alle im Team sehen, wer gelesen hat.
- **Web-App:** Seite „Schutzkonzept“, Hinweis auf der Startseite für alle, die noch nicht bestätigt haben, Card „Schutzkonzept“ in den Einstellungen.
- Dazu Tests, Demo-Daten, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Datenformen** in `src/Zeltlotse.Api.Contracts/Sicherheit/Schutzkonzept.cs` (nur additiv):
  - `Schutzkonzept(Guid? DokumentId, string? Titel, string? Dateiart, DateTimeOffset? SelbstGelesenAm, IReadOnlyList<SchutzkonzeptLeser> Team, uint Version)`.
  - `SchutzkonzeptLeser(Guid PersonId, string? Vorname, string? Nachname, bool Leitung, DateTimeOffset? GelesenAm)`.
  - `SchutzkonzeptGelesen(Guid DokumentId)`.
  - `enum SchutzkonzeptFeld { DokumentId }` und `SchutzkonzeptFelder` mit dem JSON-Namen `dokumentId` und `JsonName(feld)` (Muster `SicherheitsketteFelder`).
- **Neue Leseschnittstellen in `Zeltlotse.Freizeiten.Contracts`** (unter dem Mandantenfilter, Pfeil Sicherheit → Freizeiten; Umsetzung und Registrierung nach dem Muster `IFreizeitZeitpunkte`/`FreizeitZeitpunkteLesen`):
  - `Team/IFreizeitTeamLesen.TeamAsync(Guid freizeitId)` → `IReadOnlyList<FreizeitTeamMitglied(Guid PersonId, bool Leitung)>`: je Person mit Freizeit-Rolle Leitung oder Team genau ein Eintrag. `Leitung` ist wahr, wenn eine der Rollen Leitung ist.
  - `Dokumente/IFreizeitDokumenteLesen.InfoAsync(Guid freizeitId, Guid dokumentId)` → `FreizeitDokumentInfo(Guid Id, string Titel, string Dateiart)?`. Titel und Art eingebundener Dokumente kommen über `DokumentAnsicht.LesenAsync` aus Organisationen. Ein gelöschtes oder fremdes Dokument ergibt `null`.
- **Endpunkte (Modul Sicherheit, Gruppe `/freizeiten/{freizeitId:guid}/sicherheit`):**

  | # | Endpunkt | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET …/schutzkonzept` | `SicherheitRechte.Lesen` | `200 Schutzkonzept`; ohne Zeile `DokumentId=null`, `Version=0` |
  | 2 | `PATCH …/schutzkonzept` (AD-19, genau das Feld `dokumentId`, Wert Guid-Text oder `null`) | `SicherheitRechte.Einrichten` | `200 {version, dokumentId}`; der erste PATCH legt die Zeile an (Muster Kette: `INSERT … ON CONFLICT DO NOTHING`, dann `FOR UPDATE`) |
  | 3 | `POST …/schutzkonzept/gelesen` (Body `SchutzkonzeptGelesen`) | `SicherheitRechte.Lesen` | `204`; die Person kommt aus `IAngemeldet.PersonId` |

- **Wirksames Schutzkonzept (eine Regel an einer Stelle):** Ein gespeicherter `DokumentId`, zu dem `InfoAsync` `null` liefert, gilt überall als „kein Schutzkonzept“. Das betrifft die GET-Antwort (`DokumentId`, `Titel`, `Dateiart` = `null`), den Vergleich mit `alterWert` im PATCH und die Prüfung im POST.
- **Regeln:**
  - PATCH:
    - Unbekannte Kennung oder Dokument einer anderen Freizeit → `400` am Feld `dokumentId` mit „Dieses Dokument gibt es in der Freizeit nicht.“
    - Kein Guid-Text → `400`. Kein Feld, mehr als ein Feld oder ein unbekanntes Feld → `400`.
    - `alterWert` weicht vom wirksamen Wert ab → `409 konflikt`.
  - POST:
    - `dokumentId` fehlt → `400`.
    - Kein wirksames Schutzkonzept oder ein anderes Dokument als das wirksame → `409 konflikt`.
    - Schon bestätigt → `204`, keine zweite Zeile. Eindeutig je (`FreizeitId`, `DokumentId`, `PersonId`); auch zwei gleichzeitige POSTs ergeben eine Zeile.
  - Phase `Gelaufen` (über `IFreizeitZeitpunkte`) → PATCH und POST `409 freizeit-abgeschlossen`; GET `200`.
  - Ein neues Dokument als Schutzkonzept verlangt eine neue Bestätigung: Es zählen nur Bestätigungen des wirksamen Dokuments.
- **GET-Inhalt:**
  - `Team` sind alle aus `TeamAsync`, mit Namen aus `IPersonenNamen` (Pfeil Sicherheit → Personen; ProjectReference auf `Zeltlotse.Personen.Contracts` ergänzen).
  - `GelesenAm` ist die Bestätigung des wirksamen Dokuments, sonst `null`.
  - Sortiert nach Nachname, dann Vorname (ohne Groß- und Kleinschreibung).
  - `SelbstGelesenAm` gilt für die anfragende Person.
- **Entitäten** (Schema `sicherheit`, `IOrganisationsEigen`, Migration `Schutzkonzept`):
  - `schutzkonzept`: `Id`, `OrganisationId`, `FreizeitId` (eindeutig), `DokumentId?`, `uint Version` (`IsRowVersion`, xmin). Änderung nur über `FeldAendern`.
  - `schutzkonzept_bestaetigung`: `Id`, `OrganisationId`, `FreizeitId`, `DokumentId`, `PersonId`, `GelesenAm` (aus `TimeProvider`). Unveränderlich, keine Version, kein `[Besonders]`.
  - Commands `SchutzkonzeptFeldAendernCommand` und `SchutzkonzeptGelesenCommand` (`internal`, `IModulCommand<SicherheitDbContext>`, `IBenutzerCommand`) mit Validator. Lesen im Endpunkt über `SicherheitLeseDbContext`.
- **Datenschutz (AD-16, Lösch-Klasse „Arbeitsdaten der Freizeit“):**
  - `SicherheitPersonLoeschen` als Empfänger von `PersonGeloescht`, registriert mit `EmpfaengtEreignis`. Er löscht die Bestätigungen der Person.
  - `SicherheitAuskunft : IDatenauskunft`: Abschnitt „Schutzkonzept“ mit je Bestätigung der Zeile „Als gelesen bestätigt“ (Zeitpunkt).
  - `SicherheitLoeschlauf : ILoeschlauf`: löscht die Bestätigungen von `FreizeitenMitAbgelaufenenDaten`.
  - `LoeschPflichtTests.cs:25` nimmt `"Sicherheit"` in die Liste auf.
- **Ui-Client `SicherheitApiClient`:**
  - `SchutzkonzeptPfad(freizeitId)`.
  - `SchutzkonzeptLadenAsync`: 403/404 → `null`.
  - `GelesenAsync(freizeitId, dokumentId)` → Ergebnis Erfolg, Konflikt oder Fehler.
  - Für den Datei-Link ein Pfad-Helfer auf die vorhandene Dokument-Datei (Nr. 9 aus 2-7) im `FreizeitenApiClient`, falls noch nicht vorhanden.
- **Seite `Features/Sicherheit/Schutzkonzept.razor` (`/freizeiten/{FreizeitId:guid}/schutzkonzept`, Leitung und Team, alle Phasen):**
  - Dachzeile „Sicherheit“, h1 „Schutzkonzept“. Lädt Kontext und Schutzkonzept; `null` → `NichtVerfuegbar`.
  - Ohne Dokument: `LeerZustand` „Noch kein Schutzkonzept angehängt.“ Mit `Darf(FreizeitEinstellungen)` kommt der Link „In den Einstellungen anhängen“ auf `freizeiten/{id}/einstellungen#schutzkonzept` dazu.
  - Mit Dokument:
    - Card „Dokument“: Link „<Titel> (<Dateiart>)“ auf die Datei.
    - Darunter, solange nicht selbst bestätigt und nicht `Gelaufen`: der Satz „Nach dem Lesen hier bestätigen.“ und der Knopf (Primär) „Als gelesen bestätigen“.
      - Erfolg: Meldung „Als gelesen bestätigt.“, die Seite lädt neu.
      - 409 `konflikt`: Meldung „Das Schutzkonzept wurde inzwischen ausgetauscht. Bitte das neue lesen.“, die Seite lädt neu.
    - Schon bestätigt: Meldung (Erfolg) „Gelesen am <Datum>.“
    - Card „Team“:
      - `Balken` „<n> von <m> gelesen“.
      - `PersonenListe` mit Initialen und Name. Die Zeile lautet „Leitung · gelesen am <Datum>“ bzw. „Team · noch nicht gelesen“.
- **Startseite `FreizeitStart.razor`:**
  - Knopf „Schutzkonzept“ (`Ansichtswechsel`) für alle Rollen in allen Phasen.
  - Card „Schutzkonzept“ mit dem Satz „Das Schutzkonzept ist noch nicht als gelesen bestätigt.“ und dem Knopf „Schutzkonzept lesen“. Sie steht für alle Rollen, wenn ein wirksames Schutzkonzept da ist, `SelbstGelesenAm` `null` ist und die Phase nicht `Gelaufen` ist. Ihr Abruf scheitert still (keine Card).
  - Die Card ist kein Cockpit-Warnhinweis und zählt nicht in `AnzahlZeilen`.
- **Einstellungen (`Einstellungen.razor`):** Card „Schutzkonzept“ (`Id="schutzkonzept"`), nach der Card „Sicherheitskette“:
  - Satz „Jedes Teammitglied bestätigt es als gelesen. Wird es ausgetauscht, bestätigen alle neu.“
  - `AutosaveAuswahlgruppe` (`Mehrfach=false`) „Dokument“ auf `SchutzkonzeptPfad`. Optionen sind „Kein Schutzkonzept“ (leer → `null`) und alle Dokumente der Freizeit (Liste aus 2-7), nach Titel.
  - Hinweis „Neue Datei unter „Dokumente“ hochladen oder aus der Organisation einbinden.“ mit Link auf `freizeiten/{id}/dokumente`, dazu der Link „Wer hat gelesen?“ auf die Seite.
  - In `Gelaufen` gilt der vorhandene `KonfliktText`.
- **Demo (Schritt `SchutzkonzeptAsync` nach `SicherheitsketteAsync`, idempotent, über die Commands):**
  - Neues Org-Dokument „Schutzkonzept Jugendwerk Musterstadt“ (PDF), in „Zeltlager Sommer 2027“ eingebunden (Sichtbarkeit Team) und als Schutzkonzept gewählt.
  - Jonas Hüttner hat bestätigt; Miriam Zeltner und Lea Feuerstein noch nicht.
- Test vor dem Code. Neue Routen in `EndpunktListeTests` (`freizeitRouten`). Neue Doku-Seiten in `DokuRegeln.PflichtSeiten`.

**Never:**
- Kein Cockpit-Warnhinweis „Schutzkonzept nicht gelesen“ (Umfangsgrenzen: später). Keine Erinnerung, keine Mail, kein Push.
- Kein eigener Upload in Sicherheit, keine Kopie von Datei oder Titel. Die Datei liefert nur Freizeiten aus.
- Kein Zwang, die Datei vor dem Bestätigen zu öffnen. Kein Widerruf einer Bestätigung.
- Kein neues Ereignis, keine Zeile im Ereignis-Fluss, kein neues Modul, kein neuer Aktionsschlüssel, keine Änderung an `ARCHITECTURE-SPINE.md`.
- Keine Pop-ups, keine eigenen `elkw-`/`zl-`-Klassen außerhalb `Ui.Komponenten`. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Leer | X ohne Schutzkonzept; Miriam GET | `200`, `dokumentId null`, `version 0`, `team` mit Miriam (Leitung) und Tobias (Team), beide `gelesenAm null` | – |
| Anhängen | Miriam PATCH `dokumentId` = eigenes Dokument D1 von X, `alterWert null` | `200 {version, dokumentId}`; GET liefert Titel und Dateiart von D1 | – |
| Eingebunden | PATCH auf eine Einbindung eines Org-Dokuments | `200`; Titel aus Organisationen | – |
| Feldfehler | Dokument aus Z; unbekannte Guid; „abc“; zwei Felder; kein Feld | je `400` | – |
| Konflikt | veralteter `alterWert` | `409 konflikt` | – |
| Gelesen | Tobias POST `{D1}` | `204`; GET: Tobias `gelesenAm` gesetzt, `selbstGelesenAm` für Tobias gesetzt, für Miriam `null` | – |
| Doppelt | Tobias POST `{D1}` zweimal, auch gleichzeitig | beide `204`, eine Zeile | – |
| Falsches Dokument | Tobias POST `{D2}` bei wirksamem D1; POST ohne Schutzkonzept | `409 konflikt` / `409 konflikt` | – |
| Austausch | Miriam PATCH auf D2 | Tobias wieder `gelesenAm null` | – |
| Verschwunden | D1 wird gelöscht (DELETE aus 2-7) | GET `dokumentId null`; PATCH mit `alterWert null` → `200` | – |
| Abgeschlossen | X `Gelaufen` (SQL) | PATCH und POST `409 freizeit-abgeschlossen`; GET `200` | – |
| Rechte | Miriam; Tobias; Andrea (Org-Admin ohne Rolle); fremde Organisation; ohne Sitzung | GET `200/200/404/404/401`; PATCH `200/403/404/404/401`; POST `204/204/404/404/401` | – |
| Mandant | Schutzkonzept und Bestätigungen in X und Z | Lese-Kontext je Mandant nur eigene Zeilen | – |
| Person gelöscht | `PersonGeloescht` für Tobias | seine Bestätigungen sind weg | – |
| Frist | Löschlauf mit X in `FreizeitenMitAbgelaufenenDaten` | Bestätigungen von X sind weg, die von Z bleiben | – |
| Auskunft | Tobias hat bestätigt | Abschnitt „Schutzkonzept“ mit der Zeile „Als gelesen bestätigt“ | – |
| Seite Team | Lea, Demo | Link „Schutzkonzept Jugendwerk Musterstadt (PDF)“, Knopf „Als gelesen bestätigen“, Balken „1 von 3 gelesen“, Jonas „Leitung · gelesen am …“, Miriam und Lea „noch nicht gelesen“ | – |
| Startseite | Lea, Demo; Jonas, Demo | Card „Schutzkonzept“ / keine Card; Knopf „Schutzkonzept“ bei beiden | – |

</intent-contract>

## Code Map

- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/`:
  - `SicherheitModul.cs` (nur `AddModul`; die Gruppe `/freizeiten/{freizeitId:guid}/sicherheit` steht in `MapSicherheitEndpoints`).
  - `Sicherheitskette/{SicherheitRechte.cs,Sicherheitskette.cs,SicherheitsketteBearbeiten.cs}` -- Vorlage: PATCH nach AD-19 mit `INSERT … ON CONFLICT`, `FOR UPDATE`, `IFreizeitZeitpunkte` (Z.86), Ergebnis-Hierarchie, Endpunkt mit `JsonElement`.
  - `Daten/SicherheitDbContext.cs` (`SicherheitModell.Konfigurieren` ~Z.43, Fabrik für Migrationen).
  - Migrationen `20261013113848_Sicherheitskette`.
  - csproj: Freizeiten.Contracts ja, Personen.Contracts fehlt noch.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Datenschutz/FreizeitenDatenschutz.cs` -- Vorlage für Empfänger `FreizeitPersonLoeschen` (Z.~20-48), `FreizeitenAuskunft` (Z.~55-120) und `FreizeitenLoeschlauf` (Z.~155). Registrierung `FreizeitenModul.cs:44-46`.
- `src/Zeltlotse.SharedKernel/Datenschutz/{IDatenauskunft.cs,ILoeschlauf.cs}` -- `AuskunftAbschnitt`, `AuskunftZeile`, `AuskunftFormat`, `LoeschAuftrag.FreizeitenMitAbgelaufenenDaten`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Zeitpunkte/IFreizeitZeitpunkte.cs,Rollen/IFreizeitRollenLesen.cs}` -- Muster der Schnittstellen. Umsetzung `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs:117-148`, Registrierung `FreizeitenModul.cs:40`.
- Weitere Stellen in Freizeiten:
  - Rollen: `Domaene/FreizeitRolle.cs` (`FreizeitId`, `PersonId`, `Rolle`).
  - Dokumente: `Dokumente/Dokument.cs`, `Dokumente/DokumentEndpunkte.cs:28-57` (`DokumentAnsicht.LesenAsync`, `GelesenesDokument`).
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Namen/IPersonenNamen.cs` -- `NamenAsync(ids)` → `PersonenName(Vorname, Nachname)`.
- `src/Zeltlotse.SharedKernel/Anmeldung/IAngemeldet.cs` -- `PersonId`; Muster `Freizeiten/Anmeldungen/AnmeldungenLesen.cs:103-106`.
- Architekturtests:
  - `tests/Zeltlotse.ArchitectureTests/LoeschPflichtTests.cs:25` (feste Modulliste) und `LoeschPflichtRegeln.cs` (verlangt `IDatenauskunft` und einen `PersonGeloescht`-Empfänger).
  - `EreignisFlussTests.cs`: Die Zeile „jedes Modul mit Personenbezug“ deckt Sicherheit ab.
  - `Loesung.cs:33-35`: Pfeile Sicherheit → Freizeiten, Personen.
  - `DokuRegeln.cs:16-22`: `PflichtSeiten`.
- Tests der API (Vorlagen aus 2-8):
  - `tests/Zeltlotse.Api.Tests/SicherheitsketteTests.cs`.
  - `FreizeitpassSzenario.cs` (Org A/B, X/Z, Miriam Leitung, Tobias Team, Andrea, `Factory.Uhr`).
  - `DokumenteTests.cs` (Upload, Einbindung, S3-Fixture, Konstruktor `(PostgresContainer, S3Container)`).
  - `DatenauskunftTests.cs`, `EndpunktListeTests.cs`, `DemoDatenTests.cs`.
- `tests/Zeltlotse.Sicherheit.Tests/` -- Entitäts-, Validator-, Mandantenfilter- und Rechte-Gleichheitstests (Muster aus 2-8).
- Web-App:
  - `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor`: Knopfreihe Z.128-139, Laden ~Z.201-270.
  - `Einstellungen.razor`: Card Sicherheitskette Z.98-113, Laden ~Z.151-165.
  - `Features/Sicherheit/Sicherheitskette.razor` als Seitenvorlage.
- Ui:
  - `src/Zeltlotse.Ui/Api/{SicherheitApiClient.cs,FreizeitenApiClient.cs}` (Dokumentliste aus 2-7, `LadenAsync<T>`).
  - `Zeltlotse.Ui/Autosave/AutosaveAuswahlgruppe.razor` (`Mehrfach`, leer = `null`).
  - `Zeltlotse.Ui.Komponenten/{Balken,PersonenListe,Meldung,LeerZustand,Knopf}.razor`, `Arten.cs` (`PersonenEintrag`, `AuswahlOption`).
  - `Zeltlotse.Ui/Texte/Anzeige.cs` (`Name`, `Initialen`, `Datum`).
- `tests/Zeltlotse.Ui.Tests/{SicherheitsketteSeitenTests.cs,FreizeitenSeitenTests.cs,LeitungsCockpitSeitenTests.cs,NachgebildeterHandler.cs}` -- bUnit-Muster. Die Stubs der Startseite und der Einstellungen brauchen die neue Route.
- Demo: `src/Zeltlotse.Api/Demo/DemoKatalog.cs` (Org-Dokumente Z.70, Kette Z.95, Personen Z.116-130) und `DemoDaten.cs` (`DokumenteAsync` ~Z.580, `SicherheitsketteAsync` ~Z.630, Aufrufe Z.125-126). `DemoDateien.Pdf(...)`.
- E2E: `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`.
  - Muster: Abschnitt „Story 2-8“ ab Z.1228, `BildAsync` Z.1454.
  - `DemoMail` = Jonas, `TeamMail` = Lea.
- Doku: `doku/src/content/docs/{leitung,mitarbeitende}/`, `leitung/index.md`, `mitarbeitende/index.md`, `daten.md`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Sicherheit.Tests/` -- ZUERST:
  - Entität `Schutzkonzept.FeldAendern` mit Konflikt.
  - Validatoren.
  - Mandantenfilter beider Entitäten.
- `tests/Zeltlotse.Api.Tests/SchutzkonzeptTests.cs` (neu) -- ZUERST: jede Matrix-Zeile bis „Auskunft“ gegen PostgreSQL und S3, inklusive AD-30. Dazu `EndpunktListeTests`, `DemoDatenTests` (Demo-Schutzkonzept, Jonas bestätigt) und `LoeschPflichtTests`.
- `tests/Zeltlotse.Ui.Tests/SchutzkonzeptSeitenTests.cs` (neu) -- ZUERST:
  - Seite: leer mit und ohne Einstellungsrecht, mit Dokument, Bestätigen (Erfolg, 409), schon gelesen, `Gelaufen` ohne Knopf, `NichtVerfuegbar`.
  - Startseite: Card und Knopf.
  - Einstellungen-Card: Optionen, Autosave-Pfad.
- `src/Zeltlotse.Api.Contracts/Sicherheit/Schutzkonzept.cs` -- Datenformen.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Team/IFreizeitTeamLesen.cs,Dokumente/IFreizeitDokumenteLesen.cs}` und die Umsetzung in `Zeltlotse.Freizeiten` mit Registrierung -- Leseschnittstellen.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Schutzkonzept/*.cs,Datenschutz/SicherheitDatenschutz.cs,Daten/SicherheitDbContext.cs,Migrationen/*_Schutzkonzept*,SicherheitModul.cs,Zeltlotse.Sicherheit.csproj}` -- Entitäten, Commands, Endpunkte, Datenschutz, Registrierung, Migration.
- `src/Zeltlotse.Ui/Api/{SicherheitApiClient,FreizeitenApiClient}.cs` -- Client.
- `src/Zeltlotse.App/Features/{Sicherheit/Schutzkonzept.razor,Freizeiten/FreizeitStart.razor,Freizeiten/Einstellungen.razor}` -- Oberfläche laut Always.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` -- Demo-Schutzkonzept.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories je Handy und Laptop mit axe, nur lesend (der Demo-Stand bleibt unverändert):
  - `Schutzkonzept` (Lea, Bild `schutzkonzept`).
  - `Schutzkonzept_Hinweis` (Lea, Startseite mit Card, Bild `schutzkonzept-hinweis`).
  - `Schutzkonzept_Einrichten` (Jonas, Einstellungen, Card `#schutzkonzept`, Bild `schutzkonzept-einrichten`).
- Doku:
  - Neu `doku/src/content/docs/mitarbeitende/schutzkonzept.md` („Schutzkonzept lesen“): Hinweis auf der Startseite, Datei öffnen, bestätigen, Austausch verlangt neue Bestätigung, wer gelesen hat. Bilder `schutzkonzept-hinweis` und `schutzkonzept`.
  - Neu `leitung/schutzkonzept.md` („Schutzkonzept anhängen“): Dokument wählen (eigene Datei oder Org-Dokument), Überblick, wer gelesen hat. Bild `schutzkonzept-einrichten`.
  - Beide Seiten in `PflichtSeiten`.
  - Links in `leitung/index.md` und `mitarbeitende/index.md`; `daten.md`: Bestätigungen werden mit der Frist für Freizeit-Daten gelöscht.

**Acceptance Criteria:**
- Given Lea (Demo, Team) sieht auf der Startseite die Card „Schutzkonzept“, when sie „Schutzkonzept lesen“ und dann „Als gelesen bestätigen“ wählt, then steht dort „Gelesen am <heute>“, sie erscheint in der Team-Liste als gelesen, und die Card auf der Startseite ist weg.
- Given die Leitung wählt in den Einstellungen ein anderes Dokument als Schutzkonzept, when ein Teammitglied die Seite „Schutzkonzept“ öffnet, then gilt es wieder als „noch nicht gelesen“ und kann das neue Dokument bestätigen.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architektur- und E2E-Tests mit axe). Die Bilder `schutzkonzept`, `schutzkonzept-hinweis` und `schutzkonzept-einrichten` sind je Handy und Laptop geschrieben und verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 31 findings — high 0, medium 0, low 20, false 9, maybe-false 2
- findings:
  - `[low]` `[patch]` (edge) Bestätigen liest das gespeicherte Dokument ohne Sperre; ein gleichzeitiger Austausch speichert die Bestätigung für das alte Dokument mit `204` — Patch: Lesen mit `FOR SHARE`, damit POST und PATCH (`FOR UPDATE`) nacheinander laufen.
  - `[low]` `[patch]` (edge) Die 409-Meldung „ausgetauscht. Bitte das neue lesen.“ passt nicht, wenn das Schutzkonzept entfernt wurde — Patch: Text „Das Schutzkonzept wurde inzwischen ausgetauscht oder entfernt.“, bUnit und Doku angepasst.
  - `[low]` `[reject]` (edge) Freizeit wird abgeschlossen, während die Seite offen ist; POST zeigt die allgemeine Fehlermeldung — selten (Abschluss bei offener Seite), der Fix wäre ein neuer Zweig; Neuladen zeigt den Stand.
  - `[low]` `[reject]` (edge) Neues Dokument wird zwischen Prüfung und Speichern gelöscht — selten; die Regel „wirksames Schutzkonzept“ behandelt den Verweis danach als keins, ohne Schaden.
  - `[false]` `[reject]` (edge) A → B → A lässt alte Bestätigungen von A wieder zählen — gewollt: Bestätigungen gelten je Dokument, und A ist dasselbe Dokument; Org-Dokumente lassen sich nicht ersetzen (2-7).
  - `[low]` `[reject]` (blind) Datenauskunft nennt weder Freizeit noch Dokument — Spec legt die Zeile so fest; der Name bräuchte einen neuen Lesepfad über Organisationen hinweg (Auskunft liest ohne Mandantenfilter). Restrisiko.
  - `[low]` `[reject]` (blind) Auswahl in abgeschlossener Freizeit klickbar, erst danach „nur lesbar“ — gleiches Muster wie die Card „Sicherheitskette“ (2-8, `KonfliktText`).
  - `[low]` `[reject]` (blind) `KonfliktText` prüft den Problemcode nicht — wirkt nur, wenn die Freizeit bei offener Einstellungsseite abgeschlossen wird; selten, Fix wäre neue Logik im Baustein.
  - `[low]` `[reject]` (blind) Auswahl eines inzwischen gelöschten Dokuments endet in einem Feldfehler ohne eigene Erklärung — selten; „Neu laden“ zeigt die aktuellen Optionen.
  - `[false]` `[reject]` (blind) `version` im PATCH ungenutzt — AD-19 entscheidet über `alterWert` je Feld (wie 2-7, 2-8).
  - `[low]` `[patch]` (blind) Gleichzeitiges Bestätigen und Austauschen — gleiche Ursache und gleicher Patch wie die erste Zeile (edge).
  - `[low]` `[patch]` (blind) Kein Test für ein vom Org-Admin gelöschtes Org-Dokument — Patch: API-Test `Verschwunden_ein_geloeschtes_Org_Dokument_gilt_als_kein_Schutzkonzept`.
  - `[maybe-false]` `[reject]` (blind) „Rückgängig“ mit der leeren Option ungetestet — Rückgängig ist der vorhandene generische PATCH mit altem Wert; zu klären mit einem bUnit-Fall; selbst wenn wahr nur `low`.
  - `[low]` `[reject]` (blind) Abgeschlossene Freizeit ohne eigene Bestätigung zeigt keinen Satz dazu — kosmetisch; die Phase steht auf der Startseite.
  - `[false]` `[reject]` (blind) Doku sagt „lädt herunter“, der Link öffne einen Tab — `DateiAuslieferung` liefert immer `Content-Disposition: attachment` (2-7), die Datei wird heruntergeladen.
  - `[low]` `[reject]` (blind) Zeile `sicherheit.schutzkonzept` wird nie gelöscht — enthält keine Personendaten; gleiches Muster wie die Sicherheitskette, Freizeiten werden nie gelöscht.
  - `[low]` `[reject]` (blind) Demo prüft den gespeicherten statt den wirksamen Verweis — nur nach manuellem Löschen des Demo-Dokuments; Demo-Seed nur für Entwicklung und Tests.
  - `[false]` `[reject]` (blind) Ersetzter Inhalt eines Org-Dokuments behält Bestätigungen — Org-Dokumente lassen sich nicht ersetzen, nur löschen (2-7); ein neues Dokument hat eine neue Kennung.
  - `[low]` `[patch]` (verification-gap) Gelöschtes eingebundenes Org-Dokument ungetestet — gleicher Patch wie oben (blind).
  - `[false]` `[reject]` (verification-gap) `version` ungenutzt — wie oben (AD-19).
  - `[low]` `[reject]` (verification-gap) Demo bei verwaistem Verweis — wie oben (blind).
  - `[low]` `[reject]` (intent) „Anhängen“ als Auswahl eines Dokuments statt eigenem Upload — Design Notes: AD-15 gibt Freizeiten die Dokumente; Hochladen liegt einen Link entfernt.
  - `[low]` `[reject]` (intent) Leitung bestätigt mit und zählt im Balken — „jedes Teammitglied“; Leitung arbeitet in der Freizeit mit (Design Notes).
  - `[low]` `[reject]` (intent) Das ganze Team sieht, wer gelesen hat — Vertrauen statt Rechte-Matrix; nicht in der Leitungsliste von `rollen-und-rechte.md` (Design Notes).
  - `[false]` `[reject]` (intent) Austausch und Löschen verlangen neue Bestätigung — im Spec festgelegt und getestet, kein Fehler.
  - `[false]` `[reject]` (intent) Token-Fälle fehlen — kein Endpunkt nutzt ein Token (wie 2-6, 2-7, 2-8).
  - `[maybe-false]` `[reject]` (intent) „Test vor dem Code“ aus dem Diff nicht belegbar — Eigenschaft des Vorgehens; jede Matrix-Zeile hat einen gelaufenen Test.
  - `[low]` `[reject]` (intent) Kein Browser-Test für das Bestätigen — ein Klick würde den gemeinsamen Demo-Stand der Screenshots ändern (Design Notes); API und bUnit decken es ab, AD-30 verlangt E2E nur für Gast-Anmeldung, Login und Notfall.
  - `[low]` `[reject]` (intent) Screenshot ersetzt Jonas' Datum fest — das Demo-Datum ist der Ladetag; fester Text verhindert Bildrauschen, Muster wie Bild `organisation`.
  - `[false]` `[reject]` (intent) Doku-Build nicht belegt — `npm run build` lief fehlerfrei (Verifikation).
  - `[false]` `[reject]` (intent) Zusätze über FR-30 hinaus (Card, Knopf, Datenschutz) — Card und Knopf setzen EXPERIENCE „Hinweis auf Startseite“ um, Datenschutz ist Pflicht nach AD-16.

## Design Notes

- **Schutzkonzept = Dokument der Freizeit:**
  - AD-15 gibt Freizeiten die Dokumente, die Datenbesitz-Tabelle gibt Sicherheit die Bestätigung. Der Verweis liegt bei Sicherheit, weil dort die Bestätigungen daran hängen.
  - So bleibt Freizeiten unverändert bis auf zwei Leseschnittstellen. Ein Org-weites Schutzkonzept kommt ohne Kopie über die Einbindung aus 2-7.
- **Verschwundenes Dokument:**
  - Löschen in Freizeiten oder Organisationen meldet kein Ereignis.
  - Deshalb gilt die Regel „wirksames Schutzkonzept“ beim Lesen, statt aufzuräumen. Sonst bliebe die Autosave-Auswahl mit `alterWert null` dauerhaft im Konflikt.
- **Bestätigung je Dokument:** Wird das Dokument ausgetauscht, bestätigen alle neu, ohne Sonderlogik: Es zählt nur die Bestätigung des wirksamen Dokuments. Alte Zeilen bleiben bis zur Frist.
- **Team-Liste aus Sicherheit:**
  - Die Personenliste aus 2-6 enthält nur Anmeldungen, eingesetzte Leitungen fehlen dort.
  - Sicherheit darf Freizeiten und Personen lesen (AD-2). Die Liste der Leserinnen und Leser entsteht deshalb dort, mit Namen über `IPersonenNamen`.
  - Sie enthält nur Rollen und Datum, keine Freizeit-Inhalte. Alle im Team sehen sie (Vertrauen statt Rechte-Matrix; nicht in der Leitungsliste von `rollen-und-rechte.md`).
- **Löschen:** Die Bestätigung beschreibt die Person selbst (wie Team-Notizen über eine Person). Bei `PersonGeloescht` wird sie deshalb gelöscht, nicht durch einen Platzhalter ersetzt. Nach der Frist für Freizeit-Daten (`FreizeitDatenMonate`) löscht der Löschlauf alle Bestätigungen der Freizeit.
- **Startseite:** Der Hinweis gilt der Person selbst, nicht der Leitung. Deshalb steht er für alle Rollen und ist kein Cockpit-Warnhinweis. Der Cockpit-Hinweis „nicht gelesen“ steht in den Umfangsgrenzen unter „später“.
- **Screenshots nur lesend:** Ein Klick auf „bestätigen“ würde den gemeinsamen Demo-Stand für spätere Bilder ändern. Das Bestätigen prüfen API- und bUnit-Tests.
- **AD-30:**
  - Falsche Rolle `403` gibt es am PATCH (Team).
  - Org-Admin ohne Rolle und fremde Organisation ergeben `404`.
  - Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün inklusive Architektur, E2E und Screenshots
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `mitarbeitende/schutzkonzept/` und `leitung/schutzkonzept/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-16, FR-30):**
- **Freizeiten:** zwei neue Leseschnittstellen für Sicherheit:
  - `IFreizeitTeamLesen`: wer in der Freizeit Leitung oder Team ist.
  - `IFreizeitDokumenteLesen`: Titel und Art eines Dokuments der Freizeit, auch eines eingebundenen Org-Dokuments.
- **Sicherheit:**
  - Der Verweis „welches Dokument ist das Schutzkonzept“ und die Bestätigungen „gelesen“ je Dokument liegen hier, Migration `Schutzkonzept`.
  - Endpunkte:
    - `GET …/sicherheit/schutzkonzept` (Leitung und Team) mit der Liste, wer gelesen hat.
    - `PATCH` nach AD-19 (nur Leitung).
    - `POST …/schutzkonzept/gelesen` (Leitung und Team).
  - Die Regel „wirksames Schutzkonzept“: Ein gelöschtes Dokument gilt als keins.
  - Datenschutz nach AD-16:
    - Bei `PersonGeloescht` werden die Bestätigungen der Person gelöscht.
    - Die Datenauskunft hat den Abschnitt „Schutzkonzept“.
    - Der Löschlauf löscht die Bestätigungen nach der Frist für Freizeit-Daten.
- **Web-App:**
  - Neue Seite „Schutzkonzept“ mit Datei-Link, „Als gelesen bestätigen“, Balken und Team-Liste.
  - Startseite:
    - die Card „Schutzkonzept“ für alle, die noch nicht bestätigt haben;
    - der Knopf „Schutzkonzept“.
  - Einstellungen: die Card „Schutzkonzept“ mit der Auswahl des Dokuments.
  - `AutosaveAuswahlgruppe` bekam additiv `KonfliktText`, und bei Optionsknöpfen ist die leere Option gewählt.
- **Demo:**
  - Das Org-Dokument „Schutzkonzept Jugendwerk Musterstadt“ ist eingebunden und gewählt.
  - Jonas hat bestätigt, Miriam und Lea noch nicht.
- **Nicht enthalten:** Cockpit-Warnhinweis „nicht gelesen“ und Erinnerungen (Umfangsgrenzen: später).

**Dateien:**
- `src/Zeltlotse.Api.Contracts/Sicherheit/Schutzkonzept.cs` – Datenformen.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Team/IFreizeitTeamLesen,Dokumente/IFreizeitDokumenteLesen}.cs`, `Zeltlotse.Freizeiten/{Rechte/FreizeitTeamLesen,Dokumente/FreizeitDokumenteLesen,FreizeitenModul}.cs` – Leseschnittstellen.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Schutzkonzept/*,Datenschutz/SicherheitDatenschutz.cs,Daten/SicherheitDbContext.cs,Migrationen/20261013131326_Schutzkonzept*,SicherheitModul.cs,Zeltlotse.Sicherheit.csproj}` – Entitäten, Commands, Endpunkte, Datenschutz.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,Autosave/AutosaveAuswahlgruppe.razor}` – Client und Baustein.
- `src/Zeltlotse.App/Features/{Sicherheit/Schutzkonzept,Freizeiten/FreizeitStart,Freizeiten/Einstellungen}.razor` – Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` – Demo-Schutzkonzept.
- Tests:
  - Neu: `Sicherheit.Tests/SchutzkonzeptTests`, `Api.Tests/SchutzkonzeptTests`, `Ui.Tests/SchutzkonzeptSeitenTests`.
  - Angepasst: `EndpunktListeTests`, `DemoDatenTests`, `LoeschPflichtTests`, `DokuRegeln`, `DokuTests`, `PersonGeloeschtTests`, `DokuScreenshotTests`.
- Doku:
  - Neu: `mitarbeitende/schutzkonzept.md`, `leitung/schutzkonzept.md`.
  - Angepasst: `leitung/{index,freizeit-oeffnen}.md`, `mitarbeitende/index.md`, `daten.md`.
  - Sechs neue Bilder: `schutzkonzept`, `schutzkonzept-hinweis` und `schutzkonzept-einrichten`, je Handy und Laptop.
  - Neu erzeugt mit echter Änderung: `dokumente-laptop`, `org-dokumente-*`, `sicherheitskette-einrichten-*` und `freizeitpass-erinnern-laptop`.

**Review (31 Befunde: high 0, medium 0, low 20, false 9, maybe-false 2):**
- Gepatcht (3, alle low):
  - Bestätigen liest das Schutzkonzept mit `FOR SHARE`.
  - Konfliktmeldung „ausgetauscht oder entfernt“, mit Doku.
  - API-Test für ein gelöschtes eingebundenes Org-Dokument.
- Zurückgestellt: nichts.
- Verworfen (Begründung im Review Triage Log):
  - Abschluss bei offener Seite.
  - Dokument wird zwischen Prüfung und Speichern gelöscht.
  - Auskunft ohne Freizeitname.
  - Auswahl in abgeschlossener Freizeit klickbar und `KonfliktText` ohne Problemcode.
  - Feldfehler für ein gelöschtes Dokument.
  - Rückgängig mit der leeren Option.
  - Kein Satz in abgeschlossener Freizeit.
  - Zeile `schutzkonzept` bleibt stehen.
  - Demo mit verwaistem Verweis.
  - Lesarten des Intents (Auswahl statt Upload, Leitung zählt mit, Sichtbarkeit fürs Team).
  - Kein Browser-Test fürs Bestätigen.
  - Festes Datum im Screenshot.
  - Alle `false`.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 0, low 3.

**AD-30:**
- GET und POST: Leitung und Team `200`/`204`.
- PATCH: Leitung `200`, Team `403`.
- Org-Admin ohne Freizeit-Rolle und fremde Organisation: `404`. Ohne Sitzung: `401`.
- Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test --solution Zeltlotse.slnx` nach den Patches (ganze Lösung inklusive Architektur, E2E mit axe und Screenshots): 1845 von 1845 bestanden.
- `npm ci` und `npm run build` in `doku/`: fehlerfrei, 39 Seiten, darunter `mitarbeitende/schutzkonzept/` und `leitung/schutzkonzept/` (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Sicherheit, Api, Ui, E2E-Screenshots).
- Screenshots mit reinem Rendering-Rauschen per `git checkout` zurückgesetzt.

**Restrisiken:**
- Die Datenauskunft nennt je Bestätigung nur den Zeitpunkt, nicht Freizeit oder Dokument.
- Im Bild `schutzkonzept` ersetzt der Test Jonas' Datum durch einen festen Text (Demo-Datum = Ladetag).
- `schutzkonzept-einrichten-laptop` zeigt wie `sicherheitskette-einrichten` den Anmeldelink mit zufälligem Port und Token und ändert sich bei jedem Lauf.
- Ein 5xx des Schutzkonzepts oder der Dokumentliste lässt die ganze Einstellungsseite ausfallen (wie bei der Kette aus 2-8).
- Ein abgelehnter erster PATCH hinterlässt eine leere Zeile; sie gilt als „kein Schutzkonzept“.
