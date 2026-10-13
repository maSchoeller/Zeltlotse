---
title: 'Notfall: Sicherheitskette und Schnellsuche'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '52cf4b29e43eb8ff7eca73c342fd370cf27d9ca2'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-6-leitungs-cockpit-und-erinnerungen.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Im Notfall gibt es in Zeltlotse weder Notfall-Button noch Sicherheitskette (was jetzt zu tun ist, wen anrufen), und der Freizeitpass einer betroffenen Person ist nur über die Liste der Freizeitpässe erreichbar (CAP-15, FR-28, FR-29). Das Modul Sicherheit ist bisher nur ein Gerüst; das Cockpit kennt den Warnhinweis „Sicherheitskette ist noch nicht eingerichtet“ noch nicht (FR-27).

**Approach:**
- **Modul Sicherheit** besitzt die Sicherheitskette je Freizeit (AD-3, Datenbesitz): Lesen für Leitung und Team, Einrichten per Autosave (AD-19) nur durch die Leitung.
- **Web-App:**
  - Seite „Sicherheitskette“ mit Schnellsuche „Betroffene Person suchen“ ganz oben.
  - Notfall-Box auf der Startseite und Eintrag „Notfall“ in der App-Leiste, beide nur in Phase „läuft“.
  - Card „Sicherheitskette“ in den Einstellungen.
  - Cockpit-Warnhinweis.
  - Schnellsuche auch auf der Seite „Freizeitpässe“.
- Die Schnellsuche filtert die vorhandene Personenliste im Client (AD-2: Freizeiten liefert die Liste, Personen den Pass); ein Treffer öffnet direkt die vorhandene Freizeitpass-Seite.
- Dazu HTTP-, bUnit-, Ende-zu-Ende-Tests (Notfall-Ablauf am Handy mit axe), Demo-Daten, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Datenformen** in `src/Zeltlotse.Api.Contracts/Sicherheit/Sicherheitskette.cs` (neuer Ordner, Namensraum `Zeltlotse.Api.Contracts.Sicherheit`):
  - `Sicherheitskette(string? WasTun, string? LeitungTelefon, string? HeimatName, string? HeimatTelefon, bool Eingerichtet, uint Version)`.
  - `enum SicherheitsketteFeld { WasTun, LeitungTelefon, HeimatName, HeimatTelefon }`.
  - `SicherheitsketteFelder`: JSON-Namen `wasTun`, `leitungTelefon`, `heimatName`, `heimatTelefon`; Höchstlängen 2000 / 40 / 200 / 40; `JsonName(feld)` (Muster `FreizeitseiteFelder`).
- **Endpunkte (Modul Sicherheit, `MapSicherheitEndpoints`, Gruppe `/freizeiten/{freizeitId:guid}/sicherheit`):**

  | # | Endpunkt | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET …/sicherheitskette` | `SicherheitRechte.Lesen = [Leitung, Team]` | `200 Sicherheitskette`; ohne Zeile alle Felder `null`, `Eingerichtet=false`, `Version=0` |
  | 2 | `PATCH …/sicherheitskette` (AD-19, genau ein Feld) | `SicherheitRechte.Einrichten = [Leitung]` | `200 {version, <feld>}`; legt die Zeile beim ersten PATCH an |

  - Gleichheitstests (Muster `FreizeitpassEinsichtTests.cs:183-199`): `Lesen` = `FreizeitRechte.Mitarbeit`, `Einrichten` = `FreizeitRechte.Einstellungen`. Kein neuer Aktionsschlüssel: Die Oberfläche nutzt `freizeit-einstellungen` zum Einrichten; Lesen dürfen alle mit Freizeit-Kontext.
- **Regeln PATCH:**
  - Leerer Text oder nur Leerzeichen setzt das Feld auf `null`, sonst wird getrimmt.
  - Zu lang → `400` am Feld. Telefon ohne Ziffer → `400` (Text wie `FreizeitseiteBearbeiten.cs:76-80`).
  - Kein oder mehr als ein Feld, unbekanntes Feld → `400`.
  - `alterWert` weicht vom aktuellen Wert desselben Felds ab → `409 konflikt`.
  - Phase `Gelaufen` (über `IFreizeitZeitpunkte`) → `409 freizeit-abgeschlossen`.
  - Erste Anlage gleichzeitig: eindeutig je `FreizeitId`. Anlegen per `INSERT … ON CONFLICT DO NOTHING`, dann `SELECT … FOR UPDATE`, damit zwei erste PATCHes beide gelingen.
- **„Eingerichtet“ (eine Stelle, Methode der Entität):** `WasTun`, `LeitungTelefon` und `HeimatTelefon` sind gesetzt. `HeimatName` ist freiwillig.
- **Entität `Sicherheitskette`:**
  - Tabelle `sicherheit.sicherheitskette`, `IOrganisationsEigen`, `uint Version` als `IsRowVersion()` (xmin).
  - Felder: `Id`, `OrganisationId`, `FreizeitId` (eindeutig) und die vier Texte.
  - Feldänderung nur über `FeldAendern(feld, alterWert, neuerWert)` → `bool`.
  - Kein `PersonId`, kein `[Besonders]`. Migration `Sicherheitskette`.
  - Command `SicherheitsketteFeldAendernCommand` (`internal`, `IModulCommand<SicherheitDbContext>`, `IBenutzerCommand`) mit Validator und Ergebnis `Gespeichert | Konflikt | Abgeschlossen`. Lesen über `SicherheitLeseDbContext` im Endpunkt.
- **Projekt:**
  - `Zeltlotse.Sicherheit.csproj` referenziert zusätzlich `Zeltlotse.Api.Contracts` und `Zeltlotse.Freizeiten.Contracts` (Pfeil Sicherheit → Freizeiten erlaubt).
  - Neues Testprojekt `tests/Zeltlotse.Sicherheit.Tests` (in `Zeltlotse.slnx`, Aufbau wie `tests/Zeltlotse.Freizeiten.Tests`).
- **Ui-Client:**
  - `src/Zeltlotse.Ui/Api/SicherheitApiClient.cs` mit `SicherheitsketteLadenAsync` (403/404 → `null`) und `SicherheitskettePfad(freizeitId)`, registriert in `UiRegistrierung.cs`.
  - In `Anzeige.cs` kommt `TelefonHref(nummer)` hinzu: `tel:` plus nur `+` und Ziffern.
- **Komponenten (Ui.Komponenten, mit bUnit-Tests):**
  - Neu `TelefonLink.razor` (`Nummer`, `Text`): `<a class="elkw-link zl-gross" href="tel:…">`, mindestens 44 px hoch.
  - `PersonenEintrag` (Arten.cs:66) bekommt additiv `string? Href = null`. `PersonenListe` rendert den Namen dann als Link.
- **Schnellsuche (`Features/Freizeiten/Schnellsuche.razor`, Parameter `FreizeitId`, `Label`, `Id`, `Personen`):**
  - `Feld Typ="search" autocomplete="off"`.
  - Treffer erst ab dem ersten Zeichen: `Anzeige.TitelTrifft(Anzeige.Name(vorname, nachname), suche)`.
  - Treffer als `PersonenListe` (Initialen, Name, Zeile „Teilnehmende“/„Mitarbeitende“), `Href` = `freizeiten/{id}/freizeitpaesse/{personId}`.
  - Ohne Treffer der Satz „Keine Person gefunden.“
  - Daten aus `FreizeitenApiClient.PersonenlisteLadenAsync`.
- **Seite `Features/Sicherheit/Sicherheitskette.razor` (`/freizeiten/{FreizeitId:guid}/notfall`, alle Phasen, Leitung und Team):**
  - Lädt Kontext, Freizeit (`FreizeitAntwort.Leitung` für die Namen), Personenliste und Sicherheitskette parallel. Kontext oder Kette `null` → `NichtVerfuegbar`.
  - Reihenfolge:
    1. Dachzeile „Notfall“, h1 „Sicherheitskette“.
    2. Schnellsuche „Betroffene Person suchen“.
    3. Nicht eingerichtet: Meldung (Hinweis) „Hinweis: Die Sicherheitskette ist noch nicht eingerichtet.“
    4. Card „Was jetzt zu tun ist“: `WasTun` als nummerierte Liste, eine Zeile je Schritt; entfällt ohne Text.
    5. Card „Wen anrufen“:
       - `TelefonLink` „Notruf 112“.
       - „Leitung: <Namen>“ mit `TelefonLink` „<LeitungTelefon> anrufen“, wenn gesetzt. Die Namen stehen immer da.
       - „Ansprechperson am Heimatort: <HeimatName>“ mit `TelefonLink`, wenn `HeimatTelefon` gesetzt ist.
  - Scheitert die Personenliste, entfällt nur die Suche.
- **Startseite `FreizeitStart.razor`:**
  - Nur in Phase `Laeuft`, für alle Rollen: direkt unter der h1 die `NotfallBox` mit `Satz` „Was jetzt zu tun ist, wen anrufen und die Freizeitpässe – ein Tipp entfernt.“ und `Ziel` `freizeiten/{id}/notfall`.
  - Darunter `Schnellsuche` mit Label „Schnellsuche“.
  - Zusätzlich, in allen Phasen: Knopf „Sicherheitskette“ (`Ansichtswechsel`) für alle Rollen.
  - **Cockpit:** Bei `Darf(FreizeitEinstellungen)` lädt die Startseite die Kette mit (`SicherAsync`).
    - Ist sie nicht eingerichtet, kommt in die Card „Warnhinweise“ die Zeile „Sicherheitskette ist noch nicht eingerichtet“ als Link auf `freizeiten/{id}/einstellungen#sicherheitskette`.
    - Die Zeile zählt in `AnzahlZeilen`; ihr Abruf zählt in `AlleZeilenGeladen`.
- **App-Leiste (`Layout/MainLayout.razor`):**
  - Beginnt der relative Pfad mit `freizeiten/{guid}`, lädt das Layout bei jedem Wechsel auf eine andere Freizeit-Kennung den Kontext (Fehler → kein Eintrag).
  - In Phase `Laeuft` setzt es `NotfallHref="freizeiten/{id}/notfall"`, sonst `null`. Gemerkt werden nur die zuletzt geladene Kennung und Phase im Layout.
- **Einstellungen (`Einstellungen.razor`):**
  - Card „Sicherheitskette“ (`Id="sicherheitskette"`) mit dem Satz „Erscheint im Notfall bei Leitung und Team.“
  - Vier `AutosaveFeld` auf `SicherheitApiClient.SicherheitskettePfad`:
    - „Was jetzt zu tun ist“ (Mehrzeilig, Hinweis „Ein Schritt je Zeile.“);
    - „Telefon der Leitung im Notfall“ (tel);
    - „Ansprechperson am Heimatort“;
    - „Telefon der Ansprechperson am Heimatort“ (tel).
  - In Phase `Gelaufen` gilt der vorhandene `KonfliktText`.
- **Freizeitpässe (`Freizeitpaesse.razor`):** Schnellsuche „Person suchen“ über den Tabellen.
- **Demo (`DemoKatalog`/`DemoDaten`, Schritt `SicherheitsketteAsync` am Ende, idempotent, nur leere Felder, über den Command):**
  - `WasTun`: „Ruhe bewahren und bei der verletzten Person bleiben.“, „Bei Lebensgefahr sofort den Notruf 112 wählen.“, „Leitung anrufen und Bescheid geben.“, „Erste Hilfe leisten.“, jeweils als eigene Zeile.
  - `LeitungTelefon` „0123 456782“.
  - `HeimatName` „Petra Heimann (Gemeindebüro)“.
  - `HeimatTelefon` „0123 456783“.
- Test vor dem Code. API nur additiv. Neue Routen in `EndpunktListeTests` (Liste `freizeitRouten`).

**Never:**
- Kein Vorfall-Protokoll, keine Vorfall-Notizen (2-10), kein Notfallordner (2-11), keine Notfallkarte und keine Offline-Speicherung der Kette (V6, AD-18). Offline bleibt die vorhandene Notfall-Box mit dem Verweis auf den Notfallordner.
- Kein Telefonfeld im Profil, keine Kopie von Freizeitpass-Angaben, keine neue Einsicht-Regel (der Pass bleibt bei `FreizeitpassEinsicht` aus 2-5).
- Kein Such-Endpunkt, keine Suche in der App-Leiste, keine Seitennavigation oder untere Navigation, keine weiteren Phasen-Startseiten (Nachbereitung usw.).
- Kein neues Ereignis, keine Zeile im Ereignis-Fluss, kein neues Modul, kein neuer Aktionsschlüssel, keine Änderung an `ARCHITECTURE-SPINE.md`.
- Keine Pop-ups, keine eigenen `elkw-`/`zl-`-Klassen außerhalb `Ui.Komponenten`. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Leer lesen | X ohne Kette; Miriam GET | `200`, alle Felder `null`, `eingerichtet false`, `version 0` | – |
| Einrichten | Miriam PATCH `wasTun`, `leitungTelefon`, `heimatTelefon` nacheinander | je `200 {version, feld}`; danach GET `eingerichtet true`; Tobias (Team) GET sieht dieselben Werte | – |
| Leeren | PATCH `heimatTelefon` = „  “ | `200`, Feld `null`, `eingerichtet false` | – |
| Feldfehler | `leitungTelefon` „abc“; `wasTun` 2001 Zeichen; zwei Felder; kein Feld; Feld `geheim` | `400` / `400` / `400` / `400` / `400` | – |
| Konflikt | veralteter `alterWert` desselben Felds; anderer Feld-Wert geändert | `409 konflikt` / `200` | – |
| Gleichzeitig zuerst | zwei erste PATCHes auf verschiedene Felder parallel | beide `200`, eine Zeile | – |
| Abgeschlossen | X `Gelaufen` (SQL `AbgeschlossenAm`) | PATCH `409 freizeit-abgeschlossen`; GET `200` | – |
| Rechte GET | Miriam; Tobias; Andrea (Org-Admin ohne Rolle); Miriam auf Freizeit einer fremden Org; Leitung nur von Z auf X | `200` / `200` / `404` / `404` / `404` | – |
| Rechte PATCH | Miriam; Tobias; Andrea; fremde Org; ohne Sitzung | `200` / `403` / `404` / `404` / `401` | – |
| Mandant | Kette in X und Z (verschiedene Orgs) | Lese-Kontext liefert je Mandant nur die eigene | – |
| Seite eingerichtet | Lea öffnet Demo-Sicherheitskette | Suchfeld oben, vier Schritte, „Notruf 112“, „Leitung: Jonas Hüttner, Miriam Zeltner“ mit `tel:0123456782`, „Ansprechperson am Heimatort: Petra Heimann (Gemeindebüro)“ mit `tel:0123456783` | – |
| Seite nicht eingerichtet | Kette leer | Hinweis „Die Sicherheitskette ist noch nicht eingerichtet.“, Suchfeld, Leitungsnamen, „Notruf 112“ | – |
| Schnellsuche | Lea tippt „em“ | Treffer „Emma Muster“ (Teilnehmende) als Link auf ihren Freizeitpass; „xy“ → „Keine Person gefunden.“; leer → keine Treffer | – |
| Phase läuft | Freizeit vom Vortag bis +3 Tage | Startseite: Notfall-Box oben, Schnellsuche; App-Leiste „Notfall“ | – |
| Phase Vorbereitung | Demo-Freizeit | keine Notfall-Box, kein App-Leisten-Eintrag; Knopf „Sicherheitskette“ vorhanden | – |
| Cockpit | Leitung, Kette nicht eingerichtet; eingerichtet; Abruf scheitert | Zeile „Sicherheitskette ist noch nicht eingerichtet“ zählt mit / keine Zeile / Zeile entfällt, „Keine offenen Punkte.“ entfällt | – |
| Team-Startseite | Lea | keine Warnhinweise, kein Kettenabruf | – |

</intent-contract>

## Code Map

- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{SicherheitModul.cs:15-22,Daten/SicherheitDbContext.cs:32-55}` -- leeres Gerüst: `AddModul<SicherheitDbContext, SicherheitLeseDbContext>`, `MapSicherheitEndpoints => api`, `SicherheitModell.Konfigurieren` ohne DbSet. Host bindet beides schon ein (`src/Zeltlotse.Api/ZeltlotseModule.cs:25,46`); Migrationsreihenfolge steht (`ModulMigrationen.cs:14`). csproj: `InternalsVisibleTo` für `Zeltlotse.Sicherheit.Tests` existiert schon.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/FreizeitseiteBearbeiten.cs` -- Vorlage für den PATCH:
  - Command Z.25, Ergebnis-Hierarchie Z.33-46, Validator Z.48 (Telefon Z.76-80).
  - Handler Z.96-120 mit `FOR UPDATE`, Prüfung `Gelaufen` und `FeldAendern`.
  - Probleme Z.124-138.
  - Endpunkt `Aendern(Guid, JsonElement, ISender, …)` Z.187-250.
  - Enum und Felder: `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeitseite.cs:59-67`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Zeitpunkte/IFreizeitZeitpunkte.cs` -- `ZeitpunkteAsync(ids)` → `Phase` (einzige Quelle der Phase für andere Module).
- `src/Zeltlotse.SharedKernel/Rechte/Rechtemerkmale.cs:43` -- `NurFreizeitRolle(...)`, `FreizeitRolle` (in Modulen als `RolleArt`). `src/Zeltlotse.Api/Rechte/RechtePruefung.cs:133-175` -- 404/403-Logik. `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs:28,40` -- `Mitarbeit`, `Einstellungen`.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs:21-31`, `PersonenModul.cs:96-104` -- Vorbild für eine Rechteliste in einem fremden Modul und die Registrierung der Gruppe.
- `src/Zeltlotse.SharedKernel/Daten/{ModulRegistrierung.cs:21-33,ModulDbContextBasis.cs:33-61}` -- Registrierung und Mandantenfilter (`IOrganisationsEigen`).
- Architekturtests, die betroffen sind:
  - `tests/Zeltlotse.ArchitectureTests/{Loesung.cs:33,AbhaengigkeitsRegeln.cs:28,36,44,TypRegelnTests.cs:223-275,SchemaTests.cs:81-139,ProjektDateiTests.cs}`.
  - Kein Schreib-DbContext außerhalb von Handlern; nur der eigene Ordner `Api.Contracts.Sicherheit`; Typen `internal`; Schema `sicherheit`.
  - `LoeschPflichtTests.cs:250-257` erwartet fest nur vier Module mit `PersonId`, darum hat die Entität keine `PersonId`.
- Tests der API:
  - `tests/Zeltlotse.Api.Tests/FreizeitpassSzenario.cs:98-230` -- Szenario mit Org A/B, X/Z, Miriam, Tobias, Andrea, Lea (Leitung von Z), `Factory.Uhr`, PATCH-Muster `FragenAsync`.
  - `FreizeitseiteTests.cs:227` -- `Gelaufen` per SQL.
  - `EndpunktListeTests.cs:185-247` -- Liste `freizeitRouten`.
  - `DemoDatenTests.cs`.
  - Mandantenfilter-Vorbild: `tests/Zeltlotse.Freizeiten.Tests/FreizeitenCommandTests.cs:349`.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- Startseite und Cockpit:
  - Kopf Z.45-53, Knöpfe Z.112-120.
  - Card „Warnhinweise“ Z.70-100.
  - `AnzahlZeilen` Z.147, `AlleZeilenGeladen` Z.150.
  - Paralleles Laden Z.201-225, `SicherAsync` Z.236-247.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor:69-77,128-155` -- Card „Kontakt für Absagen“ als Muster für Telefon-Autosave, Gate und `_abgeschlossen`.
- `src/Zeltlotse.App/Layout/MainLayout.razor:14,30-33,54` -- `AppLeiste` ohne `NotfallHref`, Notfall-Box nur offline. `src/Zeltlotse.Ui.Komponenten/{AppLeiste.razor:10-13,27,NotfallBox.razor}` -- Parameter `NotfallHref` bzw. `Satz`/`Ziel`.
- `src/Zeltlotse.App/Features/Freizeiten/{Freizeitpaesse.razor:39-85,FreizeitpassEinsicht.razor}` -- Personenliste und Pass-Seite; die Route des Passes ist das Ziel der Treffer.
- `src/Zeltlotse.App/Features/Freizeiten/Dokumente.razor:59,137` und `src/Zeltlotse.Ui/Texte/Anzeige.cs:250,254` -- Such-Muster mit `TitelTrifft` sowie `Name`.
- `src/Zeltlotse.Ui.Komponenten/{PersonenListe.razor,Arten.cs:66}` -- Avatar-Liste, `PersonenEintrag`.
- `src/Zeltlotse.Ui.Komponenten/wwwroot/css/zeltlotse.css:73-114` -- `zl-notfall-*`, `.zl-gross` (44 px).
- `src/Zeltlotse.Ui/{Api/FreizeitenApiClient.cs:158,272,458-469,UiRegistrierung.cs}` -- Pfad-Helfer, `PersonenlisteLadenAsync`, `LadenAsync<T>`, Registrierung.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeiten.cs:52-59` -- `FreizeitAntwort` mit `Leitung`.
- Tests der Oberfläche:
  - `tests/Zeltlotse.Ui.Tests/{LeitungsCockpitSeitenTests.cs,FreizeitenSeitenTests.cs:428,KomponentenTests.cs:181,447-505,AppLayoutTests.cs,DokumenteSeitenTests.cs:199-212,FreizeitpaesseSeitenTests.cs,NachgebildeterHandler.cs}` -- bUnit-Muster.
  - Die Stubs der Startseite brauchen die neue Kettenroute.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs:89-116,DemoDaten.cs:87-124,398-441}` -- Demo-Kette, Muster eines idempotenten Command-Schritts.
- E2E:
  - `tests/Zeltlotse.E2E.Tests/{VeroeffentlichterHost.cs,E2EHilfe.cs,GastAnmeldungTests.cs,DokuScreenshotTests.cs:263-333,659-673,1285,1360}`.
  - Der Host läuft mit Systemuhr und gemeinsamer Datenbank je Lauf. Die Demo-Freizeit (2027) ist „in Vorbereitung“.
  - Helfer: `AxeAsync`, `PerLinkAnmeldenAsync`, `AnmeldelinkAsync`, `GastFormularAbsendenAsync`, `KonsoleMitschreibenAsync`/`OhneCspMeldung`, `host.Mails`.
- Doku:
  - `doku/src/content/docs/leitung/{cockpit.md:29,index.md:22}`, `mitarbeitende/{index.md,freizeitpaesse.md}`.
  - `doku/astro.config.mjs` (autogenerate je Ordner).
  - `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:17-23` (`PflichtSeiten`, jedes PNG verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Sicherheit.Tests/` (neu) -- ZUERST:
  - Entität: `FeldAendern`, Konflikt, Trimmen/Leeren, `Eingerichtet`.
  - Validator: Längen, Telefon.
  - Mandantenfilter der Entität.
  - Gleichheit der Rechtelisten.
- `tests/Zeltlotse.Api.Tests/SicherheitsketteTests.cs` (neu) -- ZUERST: jede API-Zeile der Matrix gegen PostgreSQL (AD-30 inkl. 403/404, gleichzeitige Erstanlage). Dazu `EndpunktListeTests` und `DemoDatenTests` (Demo-Kette eingerichtet).
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST:
  - `SicherheitsketteSeitenTests.cs` (neu): eingerichtet, nicht eingerichtet, Suche mit Treffer/ohne Treffer/leer, `NichtVerfuegbar`, Personenliste scheitert.
  - `LeitungsCockpitSeitenTests` (Zeile, Zählung, Abruf scheitert, Team ohne Abruf).
  - `FreizeitenSeitenTests` (Notfall-Box und Schnellsuche nur `Laeuft`, Knopf „Sicherheitskette“).
  - `AppLayoutTests` (Eintrag „Notfall“ nur bei `Laeuft`, Wechsel der Freizeit).
  - Einstellungen-Card.
  - `FreizeitpaesseSeitenTests` (Suche).
  - `KomponentenTests` (`TelefonLink`, `PersonenListe` mit `Href`).
- `src/Zeltlotse.Api.Contracts/Sicherheit/Sicherheitskette.cs` -- Datenformen.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Sicherheitskette/*.cs,Daten/SicherheitDbContext.cs,Migrationen/*_Sicherheitskette*,SicherheitModul.cs,Zeltlotse.Sicherheit.csproj}` -- Entität, Rechte, Command, Endpunkte, Registrierung, Migration. Dazu `Zeltlotse.slnx` für das Testprojekt.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,UiRegistrierung.cs,Texte/Anzeige.cs}` -- Client, Registrierung, `TelefonHref`.
- `src/Zeltlotse.Ui.Komponenten/{TelefonLink.razor,PersonenListe.razor,Arten.cs}` -- Bausteine; CSS nur, falls nötig, in `zeltlotse.css`.
- `src/Zeltlotse.App/Features/{Sicherheit/Sicherheitskette.razor,Freizeiten/Schnellsuche.razor,Freizeiten/FreizeitStart.razor,Freizeiten/Einstellungen.razor,Freizeiten/Freizeitpaesse.razor}`, `Layout/MainLayout.razor` -- Oberfläche laut Always.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` -- Demo-Kette.
- `tests/Zeltlotse.E2E.Tests/NotfallTests.cs` (neu, Handy 375×812, hell, de-DE, Europe/Berlin) -- Notfall-Ablauf nach AD-30:
  - Aufbau: Andrea legt in der Demo-Organisation über die API eine eigene Freizeit an, vom Vortag bis +3 Tage, mit eindeutigem Namen. Sie lädt Jonas als Leitung ein; Jonas nimmt über den Link aus der Mail an und richtet die Kette per PATCH ein. Ein Gast meldet über den Anmeldelink dieser Freizeit ein Kind an.
  - Ablauf: Jonas öffnet die Startseite. Notfall-Box und Eintrag „Notfall“ sind sichtbar; „Notfallhilfe öffnen“ führt zu h1 „Sicherheitskette“ mit `tel:112` und dem Leitungstelefon. Die ersten Buchstaben des Kindes in „Betroffene Person suchen“ zeigen den Treffer, der den Freizeitpass öffnet (h1 Name).
  - Auf Startseite, Sicherheitskette und Pass jeweils `AxeAsync`, am Ende `OhneCspMeldung`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories, je Handy und Laptop mit axe:
  - `Sicherheitskette` (Lea, Demo, Bild `sicherheitskette`).
  - `Sicherheitskette_Suche` (Lea tippt „em“, Bild `sicherheitskette-suche`).
  - `Sicherheitskette_Einrichten` (Jonas, Einstellungen, Card „Sicherheitskette“ im Bild, Bild `sicherheitskette-einrichten`).
  - `freizeit-start`, `freizeit-einstellungen` und `freizeitpaesse` laufen mit; die Cockpit-Texte bleiben gleich, weil die Demo-Kette eingerichtet ist.
- Doku:
  - Neu `doku/src/content/docs/mitarbeitende/notfall.md` („Notfall“): Notfall-Box und Eintrag „Notfall“ nur während der Freizeit, Sicherheitskette jederzeit über „Sicherheitskette“ auf der Startseite, Schnellsuche, Notfallangaben ganz oben im Freizeitpass, Team sieht Pässe ab Bestätigung, offline gilt der ausgedruckte Notfallordner. Bilder `sicherheitskette`, `sicherheitskette-suche`.
  - Neu `leitung/sicherheitskette.md` („Sicherheitskette einrichten“): die vier Felder, „eingerichtet“-Regel, Cockpit-Warnhinweis. Bild `sicherheitskette-einrichten`.
  - Beide Seiten in `DokuRegeln.PflichtSeiten`.
  - Anpassen:
    - `leitung/cockpit.md`: Z.29 auf den neuen Warnhinweis, Aufgaben weiter „später“.
    - `leitung/index.md`: Link.
    - `mitarbeitende/index.md`: Punkt „Notfall“.
    - `mitarbeitende/freizeitpaesse.md`: Schnellsuche.

**Acceptance Criteria:**
- Given Lea (Demo, Team) öffnet über „Sicherheitskette“ auf der Startseite die Sicherheitskette, when sie „em“ in „Betroffene Person suchen“ tippt und den Treffer wählt, then öffnet sich Emmas Freizeitpass in zwei Schritten ab der Startseite.
- Given eine Freizeit in Phase „läuft“, when ein Teammitglied am Handy die Startseite öffnet, then steht die Notfall-Box mit „Notfallhilfe öffnen“ und „Notruf 112“ vor allem anderen, und die App-Leiste zeigt „Notfall“ auf jeder Seite dieser Freizeit; in Vorbereitung fehlen beide.
- Given die Leitung hat die Sicherheitskette nicht eingerichtet, when sie das Cockpit öffnet, then steht unter „Warnhinweise“ „Sicherheitskette ist noch nicht eingerichtet“ mit Link zur Card in den Einstellungen; nach dem Einrichten verschwindet die Zeile.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architektur- und E2E-Tests mit axe), die Bilder `sicherheitskette`, `sicherheitskette-suche` und `sicherheitskette-einrichten` sind je Handy und Laptop geschrieben und verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 30 findings — high 0, medium 0, low 22, false 6, maybe-false 2
- findings:
  - `[low]` `[patch]` (blind) Seite „Sicherheitskette“ zeigt bei Ladefehler (5xx) kein „Notruf 112“ — Patch: `TelefonLink` „Notruf 112“ unter der Fehlermeldung, bUnit-Fall.
  - `[false]` `[reject]` (blind) `version` im PATCH geprüft, aber ungenutzt — AD-19 entscheidet über `alterWert` je Feld; die Version liefert der Server dem Client, wie bei `FreizeitseiteFeldAendern` (in 2-7 ebenso verworfen).
  - `[low]` `[patch]` (blind) Telefon erlaubt Buchstaben, `tel:` wählt dann eine zusammengezogene falsche Nummer — Patch: nur Ziffern, Leerzeichen und `+ ( ) / -`, „+“ nur vorne; Unit- und API-Test.
  - `[low]` `[reject]` (blind) Team tippt Treffer mit eingegangener Anmeldung und sieht „ab Bestätigung“ — Regel aus 2-5; während der Freizeit sind Teilnehmende bestätigt, der Fix bräuchte einen neuen Zweig in der Trefferanzeige.
  - `[low]` `[reject]` (blind) `Anzeige.TelefonHref` ohne Aufrufer in `src` — vom Spec verlangt, harmlos; delegiert an eine Stelle.
  - `[low]` `[reject]` (blind) Bild `organisation` per DOM-Eingriff, Notfall-Freizeiten bleiben liegen — Datenbank je Lauf neu (Testcontainers), also kein Anhäufen; Vorbild 2-6; eigene Organisation im Test wäre deutlich mehr Aufbau.
  - `[low]` `[reject]` (blind) Keine Löschregel für Name und Telefon am Heimatort — gleiches Muster wie der Kontakt für Absagen der Freizeit (Design Notes); Freizeiten werden heute nie gelöscht, ein Löschpfad wäre neue Logik.
  - `[maybe-false]` `[reject]` (blind) Sprung `einstellungen#sicherheitskette` scrollt nicht, weil die Card erst nach dem Laden erscheint — zu klären im Browser; selbst wenn wahr nur `low` (Card steht auf derselben Seite).
  - `[low]` `[reject]` (blind) Einstellungen laden die Kette nacheinander, ein 5xx der Kette blendet alle Einstellungen aus — selten (5xx nur eines Moduls), Fix wäre eine zusätzliche Fehlerbehandlung.
  - `[low]` `[reject]` (blind) Notfall-Eintrag merkt die Phase bis zum Freizeitwechsel — Phasenwechsel während einer offenen Sitzung ist selten, Neuladen behebt es; Ablauf wäre neue Logik (Spec: nur Kennung und Phase gemerkt).
  - `[low]` `[patch]` (blind) Name am Heimatort verschwindet ohne Telefon; Link-Text nennt nicht, wessen Nummer — Patch: Zeile mit Name immer, Links „Leitung anrufen: …“ / „Ansprechperson anrufen: …“.
  - `[low]` `[patch]` (blind) Treffer der Schnellsuche werden nicht angesagt — Patch: Statuszeile „<n> Personen gefunden.“ mit `role="status"`.
  - `[low]` `[patch]` (blind) Testlücken (Telefon 41 Zeichen, `alterWert` ohne Zeile, E2E-Autosave, Team im Notfall, 5xx) und Leerzeilen in `DemoDaten.cs` — Patch: API-Fälle 41 Zeichen und `alterWert` ohne Zeile, 5xx-Fall in bUnit, Leerzeilen; E2E-Autosave und Team-Ablauf verworfen (bUnit deckt beides, AD-30 verlangt den Notfall-Ablauf einmal).
  - `[low]` `[reject]` (edge) Einmal gescheiterter Kontext gibt bis zum Freizeitwechsel keinen Eintrag — so im Spec festgelegt; selten.
  - `[low]` `[reject]` (edge) Veraltete Phase in derselben Freizeit — wie oben (blind).
  - `[low]` `[reject]` (edge) „Freizeiten“ in anderer Schreibweise im Pfad — alle Links der App sind klein geschrieben; unerreichbar im Alltag.
  - `[low]` `[patch]` (edge) Abgeschlossene Freizeit ohne Kette behält den Warnhinweis dauerhaft — Patch: `KetteFehlt` nicht in Phase `Gelaufen`, bUnit-Fall.
  - `[low]` `[reject]` (edge) Einstellungen fallen bei Kettenfehler ganz aus — wie oben (blind).
  - `[false]` `[reject]` (edge) Demo-Seed bricht nach August 2027 ab — `Gelaufen` entsteht nur durch Abschluss; die Demo-Freizeit wird nie abgeschlossen, nach dem Ende ist sie in `Nachbereitung`.
  - `[low]` `[patch]` (edge) „+49 (0) 7443 …“ ergibt `tel:+4907443…` — Patch: `(0)` nach führendem „+“ entfällt, Komponenten-Test.
  - `[false]` `[reject]` (edge) Veraltete `version` ohne 409 — wie oben (blind), AD-19.
  - `[low]` `[patch]` (verification-gap) Card „Sicherheitskette“ in abgeschlossener Freizeit ungetestet — Patch: bUnit-Test mit 409 `freizeit-abgeschlossen` und Hinweis „nur lesbar“.
  - `[low]` `[reject]` (verification-gap) Einstellungen fallen bei Kettenfehler ganz aus — wie oben (blind).
  - `[false]` `[reject]` (verification-gap) `version` ungenutzt — wie oben (blind).
  - `[low]` `[reject]` (intent) E2E-Ablauf mit Leitung statt Team und in eigener Freizeit — die Team-Sicht hängt an der Bestätigung (2-5), bUnit deckt Team und Phase ab; AD-30 verlangt den Notfall-Ablauf am Handy mit axe, der läuft.
  - `[low]` `[reject]` (intent) Einrichten im E2E per API statt über die Einstellungen — die Oberfläche ist per bUnit (Autosave-PATCH) und Screenshot mit axe belegt.
  - `[maybe-false]` `[reject]` (intent) „In Sekunden“ nur als Schrittzahl geprüft — eine Zeitmessung wäre unzuverlässig; zwei Schritte ab Startseite sind die prüfbare Form (NFR-14); höchstens `low`.
  - `[false]` `[reject]` (intent) Token-Fälle nach AD-30 fehlen — kein Endpunkt nutzt ein Token (wie 2-6).
  - `[low]` `[reject]` (intent) Kein Screenshot der Notfall-Box in Phase „läuft“ — die Demo-Freizeit liegt 2027, Bilder mit wechselndem Datum wären bei jedem Lauf neu; die Box zeigt das vorhandene Bild `app-offline` (Design Notes).
  - `[false]` `[reject]` (intent) Nur heller Modus in der E2E-Prüfung — der Dunkelmodus der Notfall-Box ist in `OfflineStartTests` mit axe geprüft; die neue Seite nutzt nur vorhandene Bausteine.

## Design Notes

- **Telefon der Leitung als eigenes Feld:**
  - Profile haben kein Telefon.
  - Der Kontakt für Absagen ist für Eltern gedacht (oft ein Büro) und nur mit `freizeit-einstellungen` lesbar.
  - Die Kette führt deshalb eine Notfallnummer der Leitung; die Namen kommen aus `FreizeitAntwort.Leitung`.
  - So wird keine Angabe doppelt erfasst, und es entsteht kein neuer Lesepfad über Module.
- **Phase:**
  - Notfall-Box und App-Leisten-Eintrag gibt es nur in `Laeuft` (EXPERIENCE).
  - Die Seite selbst ist in allen Phasen über den Knopf „Sicherheitskette“ erreichbar. So liest das Team die Kette vor der Freizeit, und die Leitung prüft ihr Ergebnis. Das braucht keinen Sonderfall.
  - Die übrigen Phasen-Startseiten (Nachbereitung) kommen mit ihren Stories; das Cockpit bleibt.
- **Schnellsuche im Client:**
  - Die Personenliste (Freizeiten) enthält die wenigen Dutzend Personen einer Freizeit.
  - Der Pass kommt über `FreizeitpassEinsicht` (Personen) mit Protokoll und Freigabe-Regeln (AD-8). Damit gilt CAP-15 „alle Mitarbeitenden sehen die Notfallkontakte aller Teilnehmenden“ für alle bestätigten Teilnehmenden im Freigabefenster.
  - Allergien, Medikamente und Notfallkontakte stehen nach dem Katalog (2-4) schon oben und offen.
- **App-Leiste aus der Route:**
  - Das Layout kennt die Freizeit sonst nicht, und einen globalen Zustand gibt es nicht (AD-29).
  - Die Kennung kommt aus der adressierten Route (wie AD-6), die Phase aus dem Kontext-Endpunkt.
- **E2E mit eigener Freizeit:**
  - Der veröffentlichte Host läuft mit der Systemuhr, und die Demo-Freizeit liegt 2027.
  - Eine Testuhr im Produktcode würde alle E2E-Tests beeinflussen. Darum legt der Notfall-Test seine Freizeit relativ zu heute über die echten Endpunkte an.
  - Doku-Bilder entstehen nur aus der Demo-Freizeit, ohne Notfall-Box. Die Box zeigt das vorhandene Bild `app-offline`.
- **Datenschutz:**
  - Die Kette hat keine Zeltlotse-Person (`PersonId`) und fällt nicht unter Löschklassen.
  - Name und Telefon der Ansprechperson am Heimatort sind Arbeitsdaten der Freizeit, die die Leitung pflegt, so wie der Kontakt für Absagen.
- **AD-30:** GET: Leitung und Team `200`; Org-Admin ohne Rolle, andere Freizeit und fremde Organisation `404`. PATCH: Team `403`. Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün inklusive Architektur, E2E (`NotfallTests`) und Screenshots
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `mitarbeitende/notfall/` und `leitung/sicherheitskette/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-15, FR-27, FR-28, FR-29):**
- **Modul Sicherheit (erste Fachfunktion):**
  - Entität `Sicherheitskette` je Freizeit (Tabelle `sicherheit.sicherheitskette`, Mandantenfilter, `xmin` als Version).
  - `GET …/sicherheit/sicherheitskette` (Leitung und Team) und `PATCH` nach AD-19 (nur Leitung). Der erste PATCH legt die Zeile an, auch bei gleichzeitigen Aufrufen genau eine.
  - In Phase „gelaufen“ antwortet der PATCH mit `409 freizeit-abgeschlossen` (Phase über `IFreizeitZeitpunkte`).
  - Telefonnummern erlauben nur Ziffern, Leerzeichen und `+ ( ) / -`.
- **Web-App:**
  - Seite „Sicherheitskette“ (`/freizeiten/{id}/notfall`), oben die Schnellsuche „Betroffene Person suchen“; „Notruf 112“ steht auch bei einem Ladefehler da.
  - Notfall-Box und Schnellsuche auf der Startseite sowie der Eintrag „Notfall“ in der App-Leiste, nur in Phase „läuft“. Knopf „Sicherheitskette“ in jeder Phase.
  - Cockpit-Warnhinweis „Sicherheitskette ist noch nicht eingerichtet“ (nicht nach dem Abschluss).
  - Card „Sicherheitskette“ in den Einstellungen und Schnellsuche auf „Freizeitpässe“.
  - Neue Bausteine `TelefonLink` und `PersonenListe` mit Link.
- **Demo:** Die Kette der Demo-Freizeit ist eingerichtet.
- **Nicht enthalten:** Vorfall-Protokoll (2-10), Notfallordner (2-11), Offline-Kette und Notfallkarte (V6).

**Dateien:**
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Sicherheitskette/*,Daten/SicherheitDbContext.cs,Migrationen/20261013113848_Sicherheitskette*,SicherheitModul.cs,Zeltlotse.Sicherheit.csproj}` – Entität, Rechte, Command, Endpunkte, Migration.
- `src/Zeltlotse.Api.Contracts/Sicherheit/Sicherheitskette.cs` – Datenformen.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` – Demo-Kette.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,UiRegistrierung.cs,Texte/Anzeige.cs}` – Client, `TelefonHref`, `Initialen`.
- `src/Zeltlotse.Ui.Komponenten/{TelefonLink.razor,PersonenListe.razor,Arten.cs,wwwroot/css/zeltlotse.css}` – Bausteine und eine Abstandsregel.
- `src/Zeltlotse.App/Features/{Sicherheit/Sicherheitskette,Freizeiten/Schnellsuche,Freizeiten/FreizeitStart,Freizeiten/Einstellungen,Freizeiten/Freizeitpaesse}.razor`, `Layout/MainLayout.razor` – Oberfläche.
- Tests:
  - Neues Projekt `tests/Zeltlotse.Sicherheit.Tests` (in `Zeltlotse.slnx`).
  - Api: `SicherheitsketteTests`, `EndpunktListeTests`, `DemoDatenTests`.
  - Ui: `SicherheitsketteSeitenTests`, Cockpit, Layout, Freizeitpässe, Komponenten.
  - E2E: neu `NotfallTests`, Screenshot-Theories `Sicherheitskette`, `Sicherheitskette_Suche`, `Sicherheitskette_Einrichten`; `Freizeit_Einstellungen` mit exaktem Label.
  - Architektur: `DokuRegeln`, `DokuTests`.
- Doku:
  - Neu: `mitarbeitende/notfall.md`, `leitung/sicherheitskette.md`.
  - Angepasst: `leitung/{cockpit,index,freizeit-oeffnen}.md`, `mitarbeitende/{index,freizeitpaesse}.md`.
  - Bilder: sechs neue (`sicherheitskette`, `sicherheitskette-suche`, `sicherheitskette-einrichten`, je Handy und Laptop).
  - Neu erzeugt mit echter Änderung (Abstandsregel, Schnellsuche, Knopf): `dokumente*`, `dokumente-suche*`, `freizeitpaesse*`, `loeschfristen*`, `nachweis-richtlinie*` und `freizeitpass-erinnern-laptop`.

**Review (30 Befunde: high 0, medium 0, low 22, false 6, maybe-false 2):**
- Gepatcht (8, alle low):
  - „Notruf 112“ auch bei einem Ladefehler.
  - Name am Heimatort auch ohne Telefon; Link-Texte nennen, wen sie anrufen.
  - Zeichenregel für Telefonnummern.
  - `(0)` nach Ländervorwahl entfällt im `tel:`-Link.
  - Trefferzahl der Schnellsuche als Statuszeile.
  - Kein Kettenwarnhinweis nach dem Abschluss.
  - Test der Card in abgeschlossener Freizeit.
  - API-Fälle (41 Zeichen, `alterWert` ohne Zeile), 5xx-Fall und Leerzeilen.
- Zurückgestellt: nichts.
- Verworfen (Begründung im Review Triage Log):
  - `version` ungenutzt (AD-19).
  - Team-Treffer vor Bestätigung.
  - `TelefonHref` ohne Aufrufer.
  - DOM-Eingriff im Bild `organisation`.
  - Löschregel für den Heimatkontakt.
  - Sprung auf die Card.
  - Einstellungen bei Kettenfehler.
  - Phase im Layout gemerkt und kein erneuter Versuch.
  - Schreibweise „Freizeiten“.
  - Demo-Seed nach 2027 (false).
  - E2E mit Leitung und Einrichten per API.
  - „In Sekunden“ als Schrittzahl.
  - Token-Fälle.
  - Kein Screenshot der laufenden Notfall-Box.
  - Dunkelmodus in der E2E-Prüfung.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 0, low 8.

**AD-30:**
- GET: Leitung und Team `200`; Org-Admin ohne Freizeit-Rolle, Leitung nur einer anderen Freizeit und fremde Organisation `404`.
- PATCH: Leitung `200`, Team `403`, Org-Admin `404`, fremde Organisation `404`, ohne Sitzung `401`.
- Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.
- Ende-zu-Ende: `NotfallTests` am Handy (375×812) von der Notfall-Box über die Sicherheitskette und die Suche zum Freizeitpass, mit axe (WCAG 2.2 AA) auf allen drei Seiten und ohne CSP-Meldung.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung nach den Patches, inklusive Architektur, E2E und Screenshots): 1795 von 1795 bestanden.
- `npm ci` und `npm run build` in `doku/`: fehlerfrei, 37 Seiten, darunter `mitarbeitende/notfall/` und `leitung/sicherheitskette/` (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Sicherheit, Api, Ui, E2E).
- 24 Screenshots mit reinem Rendering-Rauschen (höchstens 404 Pixel; `freizeitpass-fragen-laptop` nur zufälliger Link-Token) per `git checkout` zurückgesetzt.

**Restrisiken:**
- Der Notfall-E2E-Test legt eine eigene laufende Freizeit mit neuer Leitung („Nora Notfall“) in der Demo-Organisation an. Das Bild `organisation` blendet diese Zeilen vor der Aufnahme aus; axe prüft vorher die echte Seite.
- Der Eintrag „Notfall“ merkt die Phase bis zum Wechsel der Freizeit. Ein Phasenwechsel bei offener App zeigt sich erst nach dem Neuladen.
- Die Einstellungen-Seite zeigt bei einem 5xx nur der Kette die allgemeine Fehlermeldung.
- Ein abgelehnter erster PATCH (Konflikt) hinterlässt eine leere Zeile; sie gilt als „nicht eingerichtet“.
- Tests mit festen Daten (Freizeit 2027) laufen ab Sommer 2027 aus, wie die bestehenden.
