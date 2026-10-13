---
title: 'Story 3-1: Wochenraster und Programmpunkte'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: 'df61f611701587efed2c3445cd82ae2ff348cdc0'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Modul Programm ist ein leeres Gerüst. Das Team kann keinen Wochenraster planen, es gibt keine festen Mahlzeiten und kein „Ich übernehme“ (CAP-19, FR-34, FR-35).

**Approach:** Programm bekommt die Entität `Programmpunkt` (Tag, optionale Uhrzeit, Titel, optional Mahlzeit, optional übernehmende Person) mit Endpunkten unter `/api/freizeiten/{freizeitId:guid}/programm`. Frühstück, Mittag- und Abendessen je Freizeittag legt Programm idempotent beim ersten Laden des Rasters an. Die Tage liest Programm über eine neue additive Leseschnittstelle aus `Freizeiten.Contracts`. Die Web-App zeigt den Raster (Laptop: Spalte je Tag; Handy: Reiter je Tag) und eine Detailseite je Programmpunkt mit Autosave.

## Boundaries & Constraints

**Always:**
- Rechte: alle Endpunkte `.NurFreizeitRolle(ProgrammRechte.Mitarbeit)` = `[Leitung, Team]` (gleich `FreizeitRechte.Mitarbeit`). Die Leitung teilt nichts zu: übernehmen und abgeben kann nur die angemeldete Person für sich selbst (`IAngemeldet.PersonId`).
- Ein Programmpunkt hat höchstens eine übernehmende Person. Statuswechsel nur über Entitätsmethoden `Uebernehmen(personId)` / `Abgeben(personId)` (AD-28), kein Setter.
- Mahlzeiten (`Mahlzeit` = Fruehstueck 08:00, Mittagessen 12:30, Abendessen 18:30) gibt es genau einmal je (Freizeit, Tag, Mahlzeit) per eindeutigem Index; sie lassen sich in Titel und Uhrzeit ändern und übernehmen, aber nicht löschen.
- Autosave nach AD-19: `PATCH .../programm/punkte/{punktId}` mit `ProgrammpunktFeld { Titel, Beginn }` (JSON `titel`, `beginn` im Format `HH:mm`, leer = keine Uhrzeit). Laden unter `FOR UPDATE`, `409 konflikt` nur bei abweichendem Altwert desselben Felds.
- Jeder schreibende Handler prüft die Phase über `IFreizeitZeitpunkte`: `Gelaufen` → `409 freizeit-abgeschlossen`. Fachregel-Verstöße → `409 konflikt`. Keine neuen Problemcodes, keine Ereignisse, keine Spine-Änderung.
- AD-16: Die Übernahme ist „Arbeitsdaten der Freizeit“. Eigenschaft heißt `PersonId` (nullable); Programm erfüllt `IDatenauskunft`, Empfänger `PersonGeloescht` (Platzhalter `GeloeschtePerson.Id`) und `ILoeschlauf` (Programmpunkte der Freizeiten in `FreizeitenMitAbgelaufenenDaten` löschen).
- Namen der übernehmenden Personen setzt der Client zusammen (AD-2: Programm darf Personen nicht lesen): Leitung aus `GET /api/freizeiten/{id}` (`Leitung`), Team aus `GET /api/freizeiten/{id}/personenliste` (Art Mitarbeitende). Fehlt ein Name, steht „Übernommen“ ohne Namen. Die Antwort von Programm trägt `UebernommenVon` (Guid?) und `Meins` (bool).
- Testgetrieben (AD-30), Oberfläche nur aus Bausteinen von `Zeltlotse.Ui.Komponenten`, Texte Deutsch ohne Anrede.

**Never:** Material, Aufgaben, Bibliothek, Dokumente am Programmpunkt, Druckansicht, „Alle informieren?“ (FR-62, Story 6-2), Teilnehmer-Wochenübersicht (6-4), Verschieben auf einen anderen Tag, Ziehen mit der Maus, Zuteilen durch die Leitung, neues Ereignis `FreizeitAngelegt`, Zugriff von Programm auf `Personen.Contracts`-Leseschnittstellen, Seitennavigation/untere Navigation (gibt es noch nicht; Zugang über Knopf auf der Startseite).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Raster laden | `GET .../programm`, Freizeit 02.–13.08.2027, noch nichts angelegt | `200` `Wochenraster(ErsterTag, LetzterTag, Punkte)` mit 36 Mahlzeiten; zweiter und gleichzeitiger Aufruf legt nichts doppelt an | – |
| Anlegen | `POST .../programm/punkte` `{titel, tag, beginn?}` | `201` mit Programmpunkt (Version) | Titel leer/>120 Zeichen, Tag außerhalb der Freizeit, Beginn kein `HH:mm` → `400` am Feld |
| Uhrzeit ändern | PATCH `beginn` mit passendem Altwert | `200 {version, beginn}` | anderer Altwert desselben Felds → `409 konflikt`; anderes Feld geändert → angewendet |
| Löschen | `DELETE .../programm/punkte/{id}` | `204` | Mahlzeit → `409 konflikt` |
| Ich übernehme | `POST .../punkte/{id}/uebernehmen` | `200` Programmpunkt mit `Meins=true`; nochmal durch dieselbe Person → `200` (idempotent) | von anderer Person übernommen → `409 konflikt` |
| Abgeben | `POST .../punkte/{id}/abgeben` | `200`, `UebernommenVon=null`; nicht übernommen → `200` | von anderer Person übernommen → `409 konflikt` |
| Abgeschlossen | Phase `Gelaufen`, jede Schreibaktion | – | `409 freizeit-abgeschlossen`; GET liefert weiter |
| Rechte | Leitung, Team / Org-Admin ohne Rolle / fremde Organisation / Punkt einer anderen Freizeit / ohne Sitzung | `200` / `404` / `404` / `404` / `401` | „falsche Rolle 403“ entfällt (jede Freizeit-Rolle darf), Token-Fälle entfallen (kein Token-Endpunkt) |
| Person gelöscht | `PersonGeloescht` für übernehmende Person | `PersonId` = `GeloeschtePerson.Id` | – |

</intent-contract>

## Code Map

- `src/Modules/Programm/Zeltlotse.Programm/{ProgrammModul.cs,Daten/ProgrammDbContext.cs,Zeltlotse.Programm.csproj}` -- leeres Gerüst; Modul ist im Host bereits registriert (`ZeltlotseModule.cs:26,47`, `Program.cs:58`, `ModulMigrationen.cs:70`, slnx).
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/SicherheitModul.cs` -- Vorlage Registrierung (`EmpfaengtEreignis`, `IDatenauskunft`, `ILoeschlauf`, `MapGroup` mit `Rechtemerkmale.FreizeitRouteWert`).
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Sicherheitskette/{SicherheitsketteBearbeiten.cs,SicherheitRechte.cs,Sicherheitskette.cs}` -- Vorlage PATCH (Command 23-28, Validator 46-84, Handler mit Phase und `FOR UPDATE` 86-122, Parser 147-205, 409 166-169).
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Schutzkonzept/SchutzkonzeptBearbeiten.cs` -- Vorlage POST-Übergang mit `IAngemeldet.PersonId` und `INSERT … ON CONFLICT DO NOTHING`.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Datenschutz/SicherheitDatenschutz.cs` -- Vorlage PersonLoeschen (20-37), Auskunft (44-84), Loeschlauf (95-135).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Zeitpunkte/IFreizeitZeitpunkte.cs` + Umsetzung `Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` (`FreizeitZeitpunkteLesen`) + Registrierung `FreizeitenModul.cs:42-52` -- Muster für neue `IFreizeitTageLesen`; `Domaene/Freizeit.cs:37-40` hat `ErsterTag`/`LetzterTag` (unveränderlich).
- `src/Zeltlotse.Api.Contracts/Sicherheit/Sicherheitskette.cs`, `Gemeinsam/Problemcodes.cs` -- Feld-Enum + `…Felder`, Problemcodes.
- `tests/Zeltlotse.Api.Tests/{SicherheitsketteTests.cs (Rechte 195-229, Gelaufen 173-182),FreizeitpassSzenario.cs,EndpunktListeTests.cs (Liste freizeitRouten),DemoDatenTests.cs,DatenauskunftTests.cs,PersonGeloeschtTests.cs,LoeschlaufTests.cs}` -- Testmuster und Listen, die zu ergänzen sind.
- `tests/Zeltlotse.Sicherheit.Tests/` -- Vorlage für neues Testprojekt (AssemblyFixture `PostgresContainer`, Mandantenfilter-Test 106-141).
- `tests/Zeltlotse.ArchitectureTests/{LoeschPflichtTests.cs (erwartete Modulliste),DokuRegeln.cs (PflichtSeiten)}` -- müssen Programm aufnehmen.
- `src/Zeltlotse.App/Features/Sicherheit/Sicherheitskette.razor`, `Features/Freizeiten/{FreizeitStart.razor,Einstellungen.razor}` -- Seitenvorlage, Knopf-Zugang, AutosaveFeld-Nutzung.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,UiRegistrierung.cs:25,Autosave/AutosaveFeld.razor,Texte/Anzeige.cs}` -- API-Client-Muster, Registrierung, Autosave, Texte.
- `src/Zeltlotse.Ui.Komponenten/` (Card, Knopf, Status, LeerZustand, InlineBestaetigung, Feld, Formular, Dachzeile; `wwwroot/css/zeltlotse.css`) -- kein Reiter-/Termin-Baustein vorhanden.
- `tests/Zeltlotse.Ui.Tests/{SicherheitsketteSeitenTests.cs,KomponentenTests.cs,NachgebildeterHandler.cs}` -- bUnit-Muster.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs (EinspielenAsync 96-135, SicherheitsketteAsync 638-665, VorfaelleAsync 729-749)}` -- Demo-Schritt per Command.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Sicherheitskette` ~1241, `BildAsync`, `Geraete`) und `doku/src/content/docs/mitarbeitende/{index.md,vorfall-protokoll.md}` -- Screenshot- und Doku-Muster.

## Tasks & Acceptance

**Execution:**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Zeitpunkte/IFreizeitTageLesen.cs` + Umsetzung in Freizeiten + Registrierung -- `TageAsync(IReadOnlyCollection<Guid>)` → `IReadOnlyDictionary<Guid, FreizeitTage(DateOnly ErsterTag, DateOnly LetzterTag)>`, Mandantenfilter -- Programm braucht die Tage (AD-2-Pfeil Programm → Freizeiten).
- `src/Zeltlotse.Api.Contracts/Programm/Programm.cs` -- `Wochenraster`, `Programmpunkt(Id, Version, Tag, Beginn "HH:mm"?, Titel, Mahlzeit?, UebernommenVon?, Meins)`, `ProgrammpunktNeu`, enum `Mahlzeit`, `ProgrammpunktFeld` + `ProgrammpunktFelder` (Titel max 120) -- API-Datenformen.
- `src/Modules/Programm/Zeltlotse.Programm/` -- csproj-Referenzen (Api.Contracts, Freizeiten.Contracts, Personen.Contracts für `PersonGeloescht`), Entität `Programme/Programmpunkt.cs` (IOrganisationsEigen, `uint Version` xmin), `ProgrammModell` (Tabelle `programmpunkt`, Index OrganisationId, eindeutiger Teilindex (FreizeitId, Tag, Mahlzeit) mit Mahlzeit nicht null), Slices `Programme/{WochenrasterLaden,MahlzeitenSicherstellen,ProgrammpunktAnlegen,ProgrammpunktBearbeiten,ProgrammpunktLoeschen,ProgrammpunktUebernehmen}.cs`, `ProgrammRechte.cs`, `Datenschutz/ProgrammDatenschutz.cs`, Migration `Programmpunkte`, Endpunkte in `MapProgrammEndpoints` -- Kern der Story. `MahlzeitenSicherstellen` ist kein `IBenutzerCommand` und läuft im GET-Endpunkt, wenn weniger als 3 × Tage Mahlzeiten existieren.
- `tests/Zeltlotse.Programm.Tests/` (neu, in `Zeltlotse.slnx`) -- Entitätstests (Übergänge, FeldAendern-Konflikt, Trimmen), Validatoren, Mandantenfilter, `PersonGeloescht`-Empfänger, Rechteliste gleich `[Leitung, Team]`.
- `tests/Zeltlotse.Api.Tests/ProgrammTests.cs` -- HTTP-Tests der I/O-Matrix je Endpunkt inkl. Rechte-Pflichtfälle, Nebenläufigkeit (gleichzeitige GETs, gleichzeitiges Übernehmen → genau einer gewinnt), Datenauskunft und Löschlauf; `EndpunktListeTests.cs` um die 6 Routen ergänzen.
- `tests/Zeltlotse.ArchitectureTests/LoeschPflichtTests.cs`, `DokuRegeln.cs` -- Programm in erwarteter Modulliste; neue Doku-Seite in PflichtSeiten.
- `src/Zeltlotse.Ui.Komponenten/{Reiter.razor,Termin.razor,TagesSpalten.razor}` + CSS -- Reiter als Links mit `aria-current` (statisch), Termin (Uhrzeit als Dachzeile, Titel-Link, Status, Inhalt-Slot), TagesSpalten (Laptop: Spalte je Tag mit Tageskopf, Raster horizontal scrollbar innerhalb des Bereichs; auf Handy ausgeblendet) -- neue Bausteine zuerst in der Bibliothek (AD-29), bUnit in `KomponentenTests.cs`.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs` + `UiRegistrierung.cs` + `Texte/Anzeige.cs` -- typisierter Client (GET null bei 403/404; POST-Ergebnis-Enum Erfolg/Konflikt/Abgeschlossen/Fehler).
- `src/Zeltlotse.App/Features/Programm/{Wochenraster.razor,ProgrammpunktSeite.razor}` + Knopf „Programm“ in `FreizeitStart.razor` -- Raster `/freizeiten/{id}/programm?tag=yyyy-MM-dd` (Handy: Reiter je Tag, heute vorausgewählt, sonst erster Tag; Laptop: alle Tage); Anlegen-Formular (Titel, Tag, Uhrzeit); „Ich übernehme“/„Abgeben“ direkt am Termin; Detailseite `/freizeiten/{id}/programm/{punktId}` mit AutosaveFeld Titel und Uhrzeit, Übernehmen, Löschen per InlineBestaetigung (nicht bei Mahlzeiten). In Phase Gelaufen nur lesbar. bUnit-Tests in `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs`.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- Schritt `ProgrammAsync` am Ende: Mahlzeiten sicherstellen, „Anreise und Zeltaufbau“ (02.08., 15:00, offen), „Lagerfeuer“ (02.08., 20:00, übernommen von Lea), „Geländespiel“ (03.08., 14:30, übernommen von Jonas), idempotent nach Titel.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `Wochenraster` (Jonas, Raster) und `Programmpunkt` (Detailseite „Lagerfeuer“), je Handy und Laptop mit `AxeAsync` und `BildAsync` → `wochenraster-*.png`, `programmpunkt-*.png`.
- `doku/src/content/docs/mitarbeitende/programm.md` (neu) + Link in `mitarbeitende/index.md`, Hinweis in `kueche/index.md` -- Aufgabe „Programm planen und übernehmen“ mit beiden Screenshots.

**Acceptance Criteria:**
- Given eine neue Freizeit, when ein Teammitglied das Programm öffnet, then stehen Frühstück, Mittag- und Abendessen an jedem Tag im Raster.
- Given ein offener Programmpunkt, when Lea „Ich übernehme“ tippt, then zeigt der Termin „Übernommen: Lea …“ und „Abgeben“, ohne Bestätigungsschritt und ohne Zutun der Leitung.
- Given eine Mahlzeit, when die Detailseite geöffnet wird, then lässt sich die Uhrzeit ändern, ein Löschen-Knopf fehlt.
- Given das Handy-Format, when der Raster geöffnet wird, then ist ein Tag mit Reitern je Tag sichtbar; im Laptop-Format stehen alle Tage nebeneinander.
- Given die fertige Story, when `dotnet test` (ganze Lösung, inkl. Architektur- und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und die vier neuen Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 25 findings — high 0, medium 6, low 12, false 7, maybe-false 0
- findings:
  - `[medium]` `[patch]` (Blind) Übernommener Punkt einer gelöschten Person bleibt für immer blockiert (Platzhalter `GeloeschtePerson.Id`) — Uebernehmen/Abgeben behandeln den Platzhalter als frei, die Antwort liefert dafür `UebernommenVon = null`.
  - `[low]` `[patch]` (Blind) 201 trägt Location auf eine Adresse ohne GET — Location entfernt bzw. auf vorhandene Adresse gesetzt.
  - `[low]` `[patch]` (Blind) Detailseite zeigt nach Autosave alten Titel/Uhrzeit in Überschrift, Zeitpunkt-Zeile und Löschen-Satz — Stellen wiederholen den editierbaren Wert nicht mehr.
  - `[false]` `[reject]` (Blind) PATCH verlangt ungenutzte `version` — die Form ist der Vertrag aus AD-19 und in allen Modulen gleich; die Konfliktregel arbeitet absichtlich über den Altwert.
  - `[medium]` `[patch]` (Blind) Rechte-Pflichtfälle nur für GET und POST — für PATCH, DELETE, uebernehmen, abgeben ergänzt (Org-Admin 404, fremde Organisation 404, ohne Sitzung 401).
  - `[low]` `[reject]` (Blind) Durchlauf-Test PersonGeloescht prüft keine Programm-Zeile — Empfänger ist in `ProgrammTests.Person_geloescht_…` (HTTP mit Outbox) und im Modultest abgedeckt; der Durchlauf prüft nur die Zustellung an alle Empfänger.
  - `[low]` `[patch]` (Blind) Kein Test, dass erneutes Sicherstellen eine geänderte Mahlzeit nicht überschreibt — Prüfung nach erneutem GET ergänzt.
  - `[low]` `[reject]` (Blind) Löschen/Übergang auf der Detailseite bei inzwischen gelöschtem Punkt zeigt allgemeinen Fehler — seltener Gleichzeitigkeitsfall, Behebung bräuchte zusätzliche Zweige.
  - `[medium]` `[patch]` (Blind) Handy: neuer Punkt an anderem Tag bleibt unsichtbar — nach dem Anlegen wechselt der Reiter auf dessen Tag.
  - `[false]` `[reject]` (Blind) 12 Reiter umbrechen auf dem Handy — Screenshot `wochenraster-handy.png` zeigt ein kompaktes Raster aus vier Zeilen, alle Tage sind erreichbar; kein Fehler.
  - `[false]` `[reject]` (Blind) Tag wird doppelt gerendert — so im Spec gewählt (TagesSpalten auf dem Handy ausgeblendet); `display:none` nimmt die Kopie aus dem Barrierefreiheitsbaum, E2E-Tests mit Rollen-Selektoren sind grün.
  - `[low]` `[reject]` (Blind) Löschen eines übernommenen Punkts ohne Hinweis auf die übernehmende Person — Team plant gleichberechtigt (FR-35); Zusatzlogik ohne erkennbaren Schaden.
  - `[low]` `[patch]` (Edge) Platzhalter blockiert Übernahme — gleiche Ursache wie die erste Zeile, dort behoben.
  - `[low]` `[reject]` (Edge) Nach dem Lösch-Lauf legt ein erneutes Öffnen die Mahlzeiten neu an — Mahlzeiten tragen keine Personendaten; Wächter wäre zusätzliche Logik für einen kaum erreichbaren Fall.
  - `[low]` `[reject]` (Edge) Raster: Übernehmen eines inzwischen gelöschten Punkts zeigt allgemeinen Fehler — seltener Gleichzeitigkeitsfall, neues Laden der Seite behebt es.
  - `[low]` `[reject]` (Edge) Detailseite: NeuLaden nach Konflikt bei gelöschtem Punkt — wie oben.
  - `[medium]` `[patch]` (Verification gap) Detailseite: Klick auf „Ich übernehme“/„Abgeben“ und Konflikt ungetestet — bUnit-Test ergänzt.
  - `[medium]` `[patch]` (Verification gap) Reiterwechsel auf derselben Seiteninstanz ungetestet — bUnit-Test mit `NavigateTo` ergänzt.
  - `[low]` `[patch]` (Verification gap) PATCH ohne `version` bzw. mit Zahl als `alterWert` nicht abgesichert — zwei 400-Fälle ergänzt.
  - `[false]` `[reject]` (Intent) Mahlzeiten erst beim ersten Laden statt beim Anlegen — CAP-19 beobachtet die Mahlzeiten im Raster; ohne Ereignis `FreizeitAngelegt` (Spine) ist das Anlegen beim Laden die einzige regelkonforme Lösung (Design Notes); künftige Leser in Küche gehen über Programm.
  - `[false]` `[reject]` (Intent) Keine 403- und Token-Fälle — es gibt nur die Freizeit-Rollen Leitung und Team, beide dürfen; kein Token-Endpunkt (AD-30 nach Art des Endpunkts).
  - `[medium]` `[patch]` (Intent) Rechteprüfung nicht je Endpunkt — gleiche Ursache wie Rechte-Pflichtfälle oben, dort ergänzt.
  - `[low]` `[reject]` (Intent) E2E klickt „Ich übernehme“ nicht — Klickfluss ist per bUnit und HTTP abgedeckt; E2E-Pflicht laut AD-30 nur für Kernabläufe, Doku-Screenshots lesen nur.
  - `[false]` `[reject]` (Intent) Doku-Build im Diff nicht belegt — kein Code-Fehler; der Build läuft im Abschluss (Auto Run Result).
  - `[false]` `[reject]` (Intent) Bewusste Umfangsgrenzen — beschreibend, keine Abweichung von CAP-19, FR-34, FR-35.

## Design Notes

- **Mahlzeiten beim ersten Laden:** Es gibt kein Ereignis `FreizeitAngelegt` (ein neues bräuchte eine Spine-Zeile), und Freizeitdaten sind nach dem Anlegen unveränderlich. Deshalb legt der GET-Endpunkt fehlende Mahlzeiten über den internen Command `MahlzeitenSicherstellen` an (`INSERT … ON CONFLICT DO NOTHING` auf dem eindeutigen Teilindex). Er ist kein `IBenutzerCommand`, gilt also auch in `Gelaufen`. Ergebnis für jede Freizeit, auch für schon bestehende: drei Mahlzeiten je Tag ab dem ersten Öffnen.
- **Namen im Client:** AD-2 verbietet Programm → Personen. Eine Namens-Schnittstelle in Freizeiten nur für Programm wäre ein Umweg um den Pfeil; AD-2 sagt, zusammengeführte Ansichten setzt der Client zusammen. Leitung- und Mitarbeitenden-Namen liefern vorhandene Endpunkte, beide für Leitung und Team erlaubt.
- **Kein Tageswechsel, kein Pflicht-Beginn:** Verschieben heißt laut UX „Uhrzeit ändern“. Ohne Uhrzeit steht ein Programmpunkt am Ende des Tages. Sortierung: Beginn (ohne zuletzt), dann Titel.
- **403 entfällt:** Jede Freizeit-Rolle (Leitung, Team) darf alles in Programm; Org-Admin ohne Freizeit-Rolle bekommt `404` (Host-Regel AD-12). Begründung wie in 2-8 bis 2-11.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Programm/EndpunktListe/Demo/Datenauskunft/Loeschlauf), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. `tests/Zeltlotse.E2E.Tests`) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Modul Programm mit Entität `Programmpunkt` (Tag, optionale Uhrzeit, Titel, optional Mahlzeit, optional übernehmende Person) und sechs Endpunkten unter `/api/freizeiten/{freizeitId}/programm` (Raster lesen, anlegen, Autosave-PATCH Titel/Uhrzeit, löschen, übernehmen, abgeben; alle für Leitung und Team). Frühstück, Mittag- und Abendessen je Tag entstehen idempotent beim ersten Laden. Neue Leseschnittstelle `IFreizeitTageLesen` in Freizeiten. AD-16: Datenauskunft, `PersonGeloescht`-Empfänger (Platzhalter gilt danach als frei), Lösch-Lauf. Web-App: Wochenraster (Laptop Spalten je Tag, Handy Reiter je Tag) und Detailseite mit Autosave, „Ich übernehme“/„Abgeben“, Löschen per Inline-Bestätigung; Zugang über Knopf „Programm“ auf der Startseite. Neue Bausteine `Reiter`, `Termin`, `TagesSpalten`. Demo-Daten, Doku-Seite `mitarbeitende/programm.md`, vier neue Screenshots.

**Dateien (Auswahl):**
- `src/Modules/Programm/Zeltlotse.Programm/**` -- Entität, Slices, Rechte, Datenschutz, Migration `Programmpunkte`, Registrierung.
- `src/Modules/Freizeiten/…/IFreizeitTageLesen.cs`, `FreizeitRechte.cs`, `FreizeitenModul.cs` -- Tage einer Freizeit für Programm.
- `src/Zeltlotse.Api.Contracts/Programm/Programm.cs`; `Freizeiten/Freizeiten.cs` -- Datenformen; `LeitungsName` additiv um `PersonId` ergänzt (Namen der Übernehmenden im Client).
- `src/Zeltlotse.Ui.Komponenten/{Reiter,Termin,TagesSpalten}.razor`, `zeltlotse.css` -- neue Bausteine.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs`, `UiRegistrierung.cs`, `Texte/Anzeige.cs` -- API-Client.
- `src/Zeltlotse.App/Features/Programm/*`, `Features/Freizeiten/FreizeitStart.razor` -- Seiten und Zugang.
- `src/Zeltlotse.Api/Demo/*` -- Demo-Schritt `ProgrammAsync`.
- `tests/Zeltlotse.Programm.Tests/*` (neu), `tests/Zeltlotse.Api.Tests/ProgrammTests.cs`, `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs`, `KomponentenTests.cs`, `EndpunktListeTests.cs`, `DemoDatenTests.cs`, `FreizeitKontextTests.cs`, `tests/Zeltlotse.Ablaeufe.Tests/PersonGeloeschtTests.cs`, `tests/Zeltlotse.ArchitectureTests/{LoeschPflichtTests,DokuRegeln,DokuTests}.cs`, `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Tests.
- `doku/src/content/docs/{mitarbeitende/programm.md,mitarbeitende/index.md,kueche/index.md,daten.md}`, Screenshots `wochenraster-*`, `programmpunkt-*`, aktualisiert `freizeitpass-erinnern-laptop`, `schutzkonzept-hinweis-*` (neuer Knopf „Programm“).

**Review:** 25 Befunde. Patches: 6 medium, 6 low (Platzhalter gilt als frei; Location-Header; Detailseite ohne veraltete Wiederholung des Titels; Reiterwechsel nach Anlegen; Rechte-Pflichtfälle für alle Endpunkte; Tests zu Mahlzeiten-Neuanlage, PATCH-Form, Detailseiten-Klicks, Reiterwechsel). Zurückgestellt: keine. Abgelehnt: 13 (Gründe im Review Triage Log).

**Folge-Review empfohlen:** ja (6 medium gepatcht). Ungeprüftes Restrisiko: Die Patch-Änderungen (Platzhalter als „frei“ in Entität und Antwort, Navigation nach dem Anlegen auf dem Handy, umgebaute Detailseite) sind nur durch Tests, nicht durch eine zweite Review-Runde abgesichert.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI, E2E/Doku-Screenshots) -- 2011 von 2011 grün, 0 übersprungen.
- `npm run build` in `doku/` -- fehlerfrei, 44 Seiten.
- Screenshots per Pixelvergleich geprüft: nur Rauschen bzw. Zufalls-Token zurückgesetzt, echte Änderungen behalten.

**Restrisiken:**
- Hat eine Person einen Punkt übernommen und verliert später ihre Freizeit-Rolle, bleibt der Punkt übernommen; niemand sonst kann ihn abgeben (bei Mahlzeiten ohne Ausweg). Selten, keine Zuteilung durch die Leitung laut FR-35.
- Das Uhrzeitfeld zeigt im Headless-Chromium der Screenshots ein US-Format („08:00 PM“), wie bei bestehenden Bildern.
- Nach dem Lösch-Lauf entstehen beim erneuten Öffnen wieder Mahlzeiten (ohne Personendaten).
