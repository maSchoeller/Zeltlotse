---
title: 'Story 3-2: Material am Programmpunkt'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '59a87e35a2091241de84cafdf90875311381a456'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-1-wochenraster-und-programmpunkte.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Am Programmpunkt lässt sich kein Material festhalten; es gibt weder die vier Zustände noch „Ich bringe das mit“ noch eine Gesamt-Materialliste (CAP-20, FR-36, FR-69, NS-5).

**Approach:** Programm bekommt die Entität `Material` (gehört zu genau einem Programmpunkt; Bezeichnung, optionale Menge als Freitext, Status, optional mitbringende Person) mit Endpunkten unter `/api/freizeiten/{freizeitId:guid}/programm`. Die Programmpunkt-Seite erhält eine Card „Material“ (hinzufügen, an Ort und Stelle bearbeiten, Status per Segmente, „Ich bringe das mit“, löschen); die neue Seite „Materialliste“ zeigt das Material aller Programmpunkte gruppiert nach Programmpunkt mit Menge, Status und einem Balken „eingepackt“.

## Boundaries & Constraints

**Always:**
- Rechte: alle neuen Endpunkte `.NurFreizeitRolle(ProgrammRechte.Mitarbeit)` (Leitung, Team). „Ich bringe das mit“ und Zurücknehmen nur für die angemeldete Person selbst (`IAngemeldet.PersonId`); höchstens eine mitbringende Person je Material.
- `MaterialStatus` = `Gebraucht` (Vorgabe beim Anlegen), `Vorhanden`, `Besorgt`, `Eingepackt`; JSON als Name. Wechsel nur über Entitätsmethoden (AD-28): `StatusSetzen(status)` (jeder Zustand zu jedem, gleicher Zustand = unverändert), `Mitbringen(personId)` / `NichtMitbringen(personId)` mit denselben Regeln wie `Uebernehmen`/`Abgeben` aus 3-1 (Platzhalter `GeloeschtePerson.Id` gilt als frei, Antwort dann `BringtMit = null`). Kein Setter.
- Autosave nach AD-19: `PATCH .../programm/material/{materialId}` mit `MaterialFeld { Bezeichnung, Menge }` (JSON `bezeichnung` Pflicht 1–120 Zeichen getrimmt, `menge` optional ≤ 40 Zeichen, leer = keine Menge); Status steht nicht im Feld-Enum. Laden unter `FOR UPDATE`, `409 konflikt` nur bei abweichendem Altwert desselben Felds.
- Jeder schreibende Handler prüft die Phase über `IFreizeitZeitpunkte`: `Gelaufen` → `409 freizeit-abgeschlossen`; GET liefert weiter. Keine neuen Problemcodes, keine Ereignisse, keine Spine-Änderung.
- Löschen eines Programmpunkts löscht sein Material mit (Fremdschlüssel im eigenen Schema, `ON DELETE CASCADE`; AD-3 verbietet nur Schlüssel über Schemagrenzen).
- AD-16: Mitbringen ist „Arbeitsdaten der Freizeit“ (`PersonId`, nullable). Der bestehende Empfänger `ProgrammPersonLoeschen` ersetzt auch bei Material die Kennung durch den Platzhalter; `ProgrammAuskunft` nennt Material, das die Person mitbringt; der Lösch-Lauf löscht das Material der abgelaufenen Freizeiten.
- Namen der mitbringenden Personen setzt der Client zusammen wie in 3-1 (`ProgrammTexte.Namen`); unbekannt → „Bringt jemand mit“.
- Testgetrieben (AD-30); Oberfläche nur aus Bausteinen von `Zeltlotse.Ui.Komponenten` (neuer Baustein `Segmente` dort zuerst, AD-29); Texte Deutsch ohne Anrede.

**Never:** Inventar, Reservierung, Konflikthinweis und Empfänger `ReservierungGeaendert` (Story 3-3); Mengen zusammenrechnen oder Einheiten-Logik; Status automatisch aus „Ich bringe das mit“ ableiten; Material ohne Programmpunkt; Verschieben von Material zwischen Programmpunkten; Druckansicht der Materialliste (Umfangsgrenzen); Seiten- oder untere Navigation; Zuteilen durch die Leitung.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Liste laden | `GET .../programm/material` | `200 Materialliste(Material)`, je Eintrag Programmpunkt-Kennung, -Titel, Tag, Beginn; sortiert wie der Raster (Tag, Beginn ohne zuletzt, Titel), dann Bezeichnung; leer → leere Liste | – |
| Anlegen | `POST .../programm/punkte/{punktId}/material` `{bezeichnung, menge?}` | `201` Material mit Status `Gebraucht`, Version | Bezeichnung leer/>120, Menge >40 → `400` am Feld; Punkt unbekannt oder andere Freizeit → `404` |
| Feld ändern | PATCH `menge` mit passendem Altwert | `200 {version, menge}` | anderer Altwert desselben Felds → `409 konflikt`; anderes Feld geändert → angewendet; leere Bezeichnung → `400` |
| Status setzen | `POST .../material/{id}/status` `{status:"Eingepackt"}` | `200` Material mit neuem Status und neuer Version | Status fehlt/unbekannt → `400` am Feld `status` |
| Ich bringe das mit | `POST .../material/{id}/mitbringen` | `200`, `Meins=true`; wiederholt → `200` | andere Person bringt es mit → `409 konflikt` |
| Nicht mitbringen | `POST .../material/{id}/nicht-mitbringen` | `200`, `BringtMit=null`; nicht vergeben → `200` | andere Person → `409 konflikt` |
| Löschen | `DELETE .../material/{id}` | `204` | unbekannt → `404` |
| Punkt gelöscht | `DELETE .../punkte/{punktId}` mit Material | `204`, Material verschwindet aus der Liste | – |
| Abgeschlossen | Phase `Gelaufen`, jede Schreibaktion | – | `409 freizeit-abgeschlossen`; GET `200` |
| Rechte | Leitung, Team / Org-Admin ohne Rolle / fremde Organisation / Material einer anderen Freizeit / ohne Sitzung | `200` / `404` / `404` / `404` / `401` | „falsche Rolle 403“ entfällt (jede Freizeit-Rolle darf), Token-Fälle entfallen (kein Token-Endpunkt) |
| Person gelöscht | `PersonGeloescht` für mitbringende Person | `PersonId` = Platzhalter, Antwort `BringtMit=null`, „Ich bringe das mit“ wieder möglich | – |

</intent-contract>

## Code Map

- `src/Modules/Programm/Zeltlotse.Programm/Programme/Programmpunkt.cs` -- Vorlage Entität: `Uebernommen` mit Platzhalter (55-60), `Uebernehmen`/`Abgeben` (85-110), `Wert`/`FeldAendern`/`Normalisieren` (112-161).
- `src/Modules/Programm/Zeltlotse.Programm/Programme/{ProgrammpunktAnlegen.cs,ProgrammpunktBearbeiten.cs,ProgrammpunktLoeschen.cs,ProgrammpunktUebernehmen.cs}` -- Vorlagen für Command/Ergebnis-Record/Validator/Handler/Endpunkt; PATCH-Leser `ProgrammpunktFeldAendernEndpunkt.Lesen` (JSON-Form, Rahmen `version`, `alterWert`); `ToString` ohne Personenkennung (AD-22).
- `src/Modules/Programm/Zeltlotse.Programm/Programme/WochenrasterLaden.cs` -- `ProgrammLesen.Sortiert`/`AlsApi` und `ProgrammProbleme` (wiederverwenden bzw. für Material ergänzen).
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` -- `DbSet`, `ProgrammpunktSperrenAsync` (FOR UPDATE per `FromSql`), `ProgrammModell` (Indizes, `IsRowVersion`, `Ignore`); Migration per `dotnet ef migrations add Material` im Modulprojekt (Design-Zeit-Fabrik vorhanden).
- `src/Modules/Programm/Zeltlotse.Programm/ProgrammModul.cs` -- `MapProgrammEndpoints`: neue Routen hier.
- `src/Modules/Programm/Zeltlotse.Programm/Datenschutz/ProgrammDatenschutz.cs` -- Empfänger, Auskunft, Lösch-Lauf erweitern.
- `src/Zeltlotse.Api.Contracts/Programm/Programm.cs` -- Datenformen und `ProgrammpunktFelder` als Muster für `MaterialFelder`.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs` -- Client erweitern (`ProgrammAktionErgebnis`, `FehlerAsync`, `AktionAsync` wiederverwenden); `src/Zeltlotse.Ui/Texte/Anzeige.cs` (`Uebernommen` als Muster für `BringtMit`).
- `src/Zeltlotse.App/Features/Programm/{ProgrammpunktSeite.razor,Wochenraster.razor,ProgrammTexte.cs}` -- Detailseite (Card „Material“ hinter „Bearbeiten“), Raster (Link „Materialliste“), Namen/Ziele.
- `src/Zeltlotse.Ui.Komponenten/{Auswahlgruppe.razor,SpeicherHinweis.razor,Status.razor,Balken.razor,Arten.cs}`, `wwwroot/css/elkw-components.css:270-274` (`.elkw-segments` mit `aria-pressed`, schon vorhanden) -- Bausteine; `AuswahlOption` als Optionstyp für `Segmente`.
- `src/Zeltlotse.Ui/Autosave/AutosaveFeld.razor` -- Autosave für Bezeichnung und Menge.
- `tests/Zeltlotse.Api.Tests/ProgrammTests.cs` -- HTTP-Testmuster (Szenario `FreizeitpassSzenario`, Rechte 364-441, Gelaufen 332, PersonGeloescht 442, Lösch-Lauf 466, Auskunft 486, Gleichzeitigkeit 186/313); `EndpunktListeTests.cs` (Liste `freizeitRouten`), `DemoDatenTests.cs`.
- `tests/Zeltlotse.Programm.Tests/ProgrammpunktTests.cs` -- Entitäts-, Validator- und Mandantenfilter-Tests (Muster).
- `tests/Zeltlotse.Ui.Tests/{ProgrammSeitenTests.cs,KomponentenTests.cs,NachgebildeterHandler.cs}` -- bUnit-Muster.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs (Programmpunkte, Personen 157-160),DemoDaten.cs (ProgrammAsync 761ff.)}` -- Demo-Schritt.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (Theories `Wochenraster`, `Programmpunkt`), `tests/Zeltlotse.ArchitectureTests/{DokuRegeln.cs (PflichtSeiten),DokuTests.cs}`, `doku/src/content/docs/{mitarbeitende/{index.md,programm.md},leitung/index.md,daten.md}` -- Screenshot- und Doku-Muster.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Programm/Material.cs` -- enum `MaterialStatus` (JsonStringEnumConverter, Werte 1–4); `Material(Id, Version, ProgrammpunktId, Programmpunkt, Tag, Beginn?, Bezeichnung, Menge?, Status, BringtMit?, Meins)`; `Materialliste(IReadOnlyList<Material> Material)`; `MaterialNeu(Bezeichnung?, Menge?)`; `MaterialStatusNeu(MaterialStatus? Status)`; enum `MaterialFeld { Bezeichnung, Menge }`; `MaterialFelder` (JSON-Namen `bezeichnung`, `menge`, `status`; Grenzen 120/40; `JsonName`) -- API-Datenformen.
- `tests/Zeltlotse.Programm.Tests/MaterialTests.cs` (zuerst) + `src/Modules/Programm/Zeltlotse.Programm/Material/Material.cs` -- Entität (IOrganisationsEigen, `FreizeitId`, `ProgrammpunktId`, `uint Version`), `Anlegen`, `StatusSetzen`, `Mitbringen`/`NichtMitbringen`, `BringtMit` mit Platzhalter, `Wert`/`FeldAendern` (Trimmen, leere Menge = `null`); Tests auch für Validatoren und Mandantenfilter.
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` + Migration `Material` -- `DbSet<Material>` in beiden Contexts, Tabelle `material`, FK auf `programmpunkt` mit Cascade, Indizes (FreizeitId, ProgrammpunktId, PersonId, OrganisationId), `MaterialSperrenAsync`.
- `tests/Zeltlotse.Api.Tests/MaterialTests.cs` (zuerst) -- jede Zeile der I/O-Matrix je Endpunkt inkl. Rechte-Pflichtfälle für alle 7 Routen, gleichzeitiges Mitbringen (genau einer gewinnt), gleichzeitige PATCHes auf Bezeichnung und Menge (beide gelingen), Auskunft, Lösch-Lauf, PersonGeloescht; `EndpunktListeTests.cs` um die 7 Routen ergänzen.
- `src/Modules/Programm/Zeltlotse.Programm/Material/{MaterialListeLaden,MaterialAnlegen,MaterialBearbeiten,MaterialLoeschen,MaterialStatusSetzen,MaterialMitbringen}.cs` + `ProgrammModul.cs` -- Slices und Routen: `GET material`, `POST punkte/{punktId:guid}/material`, `PATCH material/{materialId:guid}`, `DELETE material/{materialId:guid}`, `POST material/{materialId:guid}/status`, `POST material/{materialId:guid}/mitbringen`, `POST material/{materialId:guid}/nicht-mitbringen`.
- `src/Modules/Programm/Zeltlotse.Programm/Datenschutz/ProgrammDatenschutz.cs` -- Empfänger, Auskunft (Zeile „Bringt Material mit“: „TT.MM.JJJJ Titel: Bezeichnung“) und Lösch-Lauf um Material erweitern.
- `src/Zeltlotse.Ui.Komponenten/Segmente.razor` + `KomponentenTests.cs` -- `[NurInteraktiv]`, `<div class="elkw-segments" role="group" aria-label=Legende>` mit `button type=button aria-pressed` je `AuswahlOption`, Parameter `Wert`, `Gewaehlt` (EventCallback<string>), `Deaktiviert`; Klick auf den gewählten Wert löst nichts aus.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs` + `Texte/Anzeige.cs` -- `MaterialLadenAsync` (null bei 403/404), `MaterialAnlegenAsync` (Feldfehler), `MaterialStatusAsync`, `MitbringenAsync`, `NichtMitbringenAsync` (Ergebnis `MaterialAktion` analog `ProgrammAktion`), `MaterialLoeschenAsync`, `MaterialPfad`; `Anzeige.BringtMit(name?)`, `Anzeige.MaterialStatus(status)` → „gebraucht“ usw.
- `src/Zeltlotse.App/Features/Programm/{MaterialEintrag.razor,MaterialStatusWahl.razor,Materialliste.razor,ProgrammpunktSeite.razor,Wochenraster.razor,ProgrammTexte.cs}` + `tests/Zeltlotse.Ui.Tests/MaterialSeitenTests.cs` -- `MaterialStatusWahl`: Segmente „gebraucht / vorhanden / besorgt / eingepackt“, ein Tipp speichert, danach `SpeicherHinweis` „Gespeichert · Rückgängig“ (Rückgängig = alter Status erneut senden). `MaterialEintrag`: Bezeichnung und Menge (in der Detailseite als AutosaveFeld, in der Liste als Text), Status-Wahl, „Bringt mit: …“ (Status Erfolg) mit „Ich bringe das mit“/„Doch nicht mitbringen“, in der Detailseite Löschen per InlineBestaetigung. Detailseite: Card „Material“ mit Einträgen, LeerZustand-Satz „Noch kein Material.“ und Formular „Material hinzufügen“ (Bezeichnung *, Menge). `Materialliste` unter `/freizeiten/{id}/programm/material`: Dachzeile „Programm“, H1 „Materialliste“, Balken „N von M eingepackt“ mit Zahl als Text, je Programmpunkt eine Card (Dachzeile Tag und Uhrzeit, Titel-Link zur Detailseite) mit seinen Einträgen; leer → LeerZustand „Noch kein Material. Material hängt an den Programmpunkten.“ mit Knopf „Zum Programm“. Raster: Link „Materialliste“. In Phase Gelaufen nur lesbar (Status als Wort + Quadrat: gebraucht = offen, vorhanden/besorgt = aktiv, eingepackt = Erfolg).
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- Schritt `MaterialAsync` nach `ProgrammAsync`, idempotent nach (Programmpunkt, Bezeichnung): Geländespiel: „Fahnen“ „8 Stück“ vorhanden, „Seile“ „4 × 10 m“ gebraucht, „Taschenlampen“ „10 Stück“ gebraucht, bringt Miriam mit; Lagerfeuer: „Feuerholz“ „2 Kisten“ besorgt, „Stockbrot-Teig“ „3 kg“ eingepackt.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `MaterialAmProgrammpunkt` (Jonas, Detailseite „Geländespiel“) und `Materialliste`, je Handy und Laptop mit `AxeAsync` und `BildAsync` → `material-programmpunkt-*.png`, `materialliste-*.png`.
- `doku/src/content/docs/mitarbeitende/material.md` (neu) + Links in `mitarbeitende/index.md`, `mitarbeitende/programm.md`, `leitung/index.md`; `daten.md` ergänzen, wo Programm-Übernahmen genannt sind; `DokuRegeln.cs` PflichtSeiten -- Aufgabe „Material planen und packen“ mit vier Screenshots.

**Acceptance Criteria:**
- Given der Programmpunkt „Geländespiel“, when Jonas „Seile“ mit Menge „4 × 10 m“ hinzufügt, then steht es dort mit Status „gebraucht“ und erscheint ohne weitere Eingabe in der Materialliste.
- Given ein Material mit Status „gebraucht“, when ein Teammitglied das Segment „eingepackt“ tippt, then ist es sofort gespeichert, „Gespeichert · Rückgängig“ erscheint, und „Rückgängig“ stellt „gebraucht“ wieder her.
- Given Taschenlampen, when Miriam „Ich bringe das mit“ tippt, then steht „Bringt mit: Miriam Zeltner“ und der Knopf heißt „Doch nicht mitbringen“; bei Jonas fehlt der Knopf.
- Given die Materialliste, when sie geöffnet wird, then zeigt sie das Material aller Programmpunkte je Programmpunkt mit Menge und Status sowie den Balken „N von M eingepackt“.
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur- und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und die vier neuen Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 36 findings — high 0, medium 3, low 26, false 7, maybe-false 0
- findings:
  - `[low]` `[patch]` (Edge) Fehlertext von `status` nennt kleingeschriebene Werte, die der Endpunkt ablehnt — Text auf die angenommenen Namen „Gebraucht, Vorhanden, Besorgt oder Eingepackt“ geändert.
  - `[low]` `[reject]` (Edge) Mitbringen/Löschen eines inzwischen gelöschten Materials zeigt nur allgemeinen Fehler — seltener Gleichzeitigkeitsfall, Behebung bräuchte zusätzliche Zweige; gleiche Abwägung wie in 3-1, neues Laden behebt es.
  - `[low]` `[reject]` (Edge) Status-Tipp auf gelöschtes Material zeigt „Erneut versuchen“ ohne Erfolg — wie oben.
  - `[low]` `[reject]` (Edge) Doppeltipp auf „Ja, löschen“ sendet zwei DELETE — selten, die Fehlermeldung verschwindet mit dem Eintrag; Wächter wäre zusätzliche Logik.
  - `[medium]` `[patch]` (Edge) 403/404 der Materialliste zeigt auf der Detailseite „Noch kein Material.“ — gemeinsam mit dem Blind-Befund unten behoben (Meldung statt Leer-Zustand und Formular).
  - `[low]` `[reject]` (Edge) Materialliste: „Neu laden“ nach Konflikt mit 403/404 lässt alte Liste stehen — nur bei gleichzeitigem Rechteentzug erreichbar.
  - `[low]` `[reject]` (Edge) Demo-Daten: zwei Programmpunkte gleichen Titels lassen `Single` werfen — Demo-Einspielung läuft frisch in Tests und lokal; gleiche Abwägung wie in 3-1.
  - `[low]` `[reject]` (Edge) Demo-Daten: abgebrochener Lauf lässt falschen Status stehen — wie oben (3-1, Folgedurchlauf).
  - `[medium]` `[patch]` (Blind) Fehler beim Laden des Materials bricht die ganze Detailseite, 404 wirkt wie „kein Material“ — Abruf über `Leise`, bei `null` Meldung in der Card „Material“; bUnit-Tests für 500 und 404.
  - `[low]` `[reject]` (Blind) Gelöschtes Material wird in der Oberfläche nicht behandelt — gleiche Ursache wie die Edge-Zeilen oben, dort abgelehnt.
  - `[low]` `[reject]` (Blind) Löschen ohne Übertragungs-Wächter — wie Edge-Zeile oben.
  - `[low]` `[reject]` (Blind) „Bringt mit“ bleibt bei Rollenentzug hängen — echt, gleiches Restrisiko wie die Übernahme in 3-1; Freigeben durch andere wäre neue Fachregel (FR-35-Nähe), steht unter Restrisiken.
  - `[low]` `[reject]` (Blind) `MaterialStatusWahl` behält Rückgängig-Zustand nach Austausch von außen — Rückgängig nimmt bewusst die eigene letzte Änderung zurück; Zurücksetzen bei fremder Version wäre zusätzliche Zustandslogik.
  - `[low]` `[reject]` (Blind) Gespeicherte Bezeichnung erreicht die Liste der Seite nicht — geprüft: `AutosaveFeld` übernimmt nur neue (Wert, Version) von außen, Folge ist nur eine bis zum Neuladen leicht abweichende Sortierung nach dem Hinzufügen.
  - `[low]` `[reject]` (Blind) Sortierung nach dem Hinzufügen dupliziert die Server-Sortierung ohne Kennungs-Tiebreak — nur bei gleicher Bezeichnung sichtbar, rein kosmetisch.
  - `[low]` `[reject]` (Blind) Konfliktmeldung bei „Mitbringen“ bleibt stehen — so wie in 3-1 (Meldung bleibt bis zur nächsten Aktion, EXPERIENCE.md: Meldung bleibt bis Seitenwechsel).
  - `[low]` `[reject]` (Blind) Auskunftszeile für Material ohne Uhrzeit — lesbar und eindeutig genug; Format ist Gestaltungsfrage ohne Schaden.
  - `[low]` `[reject]` (Blind) Gleichzeitigkeit Anlegen/Punkt löschen, „letzter gewinnt“, Platzhalter per HTTP, Material an Mahlzeiten ungetestet — Sperre und Cascade per Datenbank, Platzhalter im Modultest; Material an Mahlzeiten ist gewollt (Material hängt am Programmpunkt).
  - `[low]` `[reject]` (Blind) Doku nennt Sonderfälle nicht (gelöscht, „Bringt jemand mit“, Mahlzeiten) — seltene Fälle; die Seite beschreibt den Normalablauf.
  - `[low]` `[patch]` (Blind) Ordner `Material/` passt nicht zum Namensraum `Materialien` — Ordner in `Materialien/` umbenannt.
  - `[low]` `[reject]` (Blind) `ProgrammpunkteLoeschen` heißt nur nach Programmpunkten, expliziter Material-Löschbefehl doppelt zur Cascade — Zusammenfassung ist aktualisiert; der explizite Befehl macht den Lösch-Lauf unabhängig von der Cascade, ohne Schaden.
  - `[false]` `[reject]` (Blind) `MaterialStatusNeu` vom Server nicht gelesen, `Version` ungenutzt — der Client serialisiert genau diese Form, der Server liest dieselbe JSON-Form (Groß-/Kleinschreibung jetzt im Fehlertext genannt); `version` ist der AD-19-Vertrag (carried aus 3-1).
  - `[low]` `[patch]` (Blind) Fehlende Leerzeilen in `AutosaveTests.cs`/`KomponentenTests.cs` — ergänzt.
  - `[low]` `[patch]` (Verification gap) Detailseite bei nicht ladbarem Material ungetestet — Tests mit 500 und 404 ergänzt (siehe oben).
  - `[low]` `[patch]` (Verification gap) Fehlerzweige (abgeschlossen beim Status-Tipp, gescheitertes Löschen, Konflikt in der Materialliste) ungetestet — drei bUnit-Tests ergänzt.
  - `[low]` `[patch]` (Verification gap) Sortierung nach dem Hinzufügen ungetestet — Test ab Vorgabeliste mit „Kompass“ ergänzt.
  - `[medium]` `[patch]` (Verification gap, other) 403/404 auf der Detailseite wie „kein Material“ — gleiche Ursache wie der Blind-Befund, dort behoben.
  - `[low]` `[reject]` (Verification gap, other) Eigener Vergleicher auf der Detailseite — wie Blind-Zeile zur Sortierung.
  - `[low]` `[reject]` (Verification gap, other) `_material` der Seite nach Autosave veraltet — wie Blind-Zeile zur Bezeichnung.
  - `[false]` `[reject]` (Intent) „Falsche Rolle 403“ nicht getestet — es gibt keine abgelehnte Freizeit-Rolle (Leitung und Team dürfen alles), Org-Rollen ohne Mitarbeit bekommen `404` (AD-12); Matrix der Intent und 3-1.
  - `[false]` `[reject]` (Intent) Token-Fälle fehlen — kein Token-Endpunkt (AD-30 nach Art des Endpunkts).
  - `[false]` `[reject]` (Intent) Test zuerst nicht belegbar — beschreibend; kein Code-Fehler.
  - `[low]` `[reject]` (Intent) Interaktive Abläufe nur per bUnit und HTTP, nicht im Browser — Doku-Screenshots lesen bewusst nur (Demo-Stand für andere Bilder); E2E-Pflicht nach AD-30 nur für Kernabläufe (carried aus 3-1).
  - `[false]` `[reject]` (Intent) Doku-Build im Diff nicht belegt — Build lief fehlerfrei (Auto Run Result).
  - `[false]` `[reject]` (Intent) Bestehende Screenshots geändert, Handy-Bildtext nicht nachgezogen — gewollt (Link und Card); der Handy-Bildtext beschreibt den sichtbaren oberen Teil weiter richtig.
  - `[false]` `[reject]` (Intent) Umfang gegen CAP-20/FR-36/FR-69 — keine Abweichung festgestellt.

## Design Notes

- **Status per Übergang statt Feld:** AD-19 verbietet Statusfelder im PATCH, AD-28 verlangt Methoden. Die Segmente erlauben jeden Zustand direkt, deshalb ein Übergang `status` mit Zielzustand statt vier Routen; die Methode lehnt nichts ab (jeder Wechsel ist fachlich gültig). Gleichzeitige Statuswechsel: der letzte gewinnt, kein Konflikt (ein Tipp, sofort sichtbar).
- **Menge als Freitext:** „Mit Mengen“ (FR-69) ohne Einheiten-Logik (Leitprinzip simpel); kein Zusammenrechnen über Programmpunkte.
- **Mitbringen ändert den Status nicht:** zwei unabhängige Angaben; eine Kopplung wäre Speziallogik.
- **Eine Lese-Route:** Die Detailseite filtert die Gesamtliste nach Programmpunkt (wie 3-1 den Raster); die Antwort trägt Titel, Tag und Beginn des Programmpunkts, weil beide Tabellen im selben Modul liegen.
- **Zugang zur Materialliste:** Link auf dem Wochenraster (Start → Programm → Materialliste, NFR-14), solange es keine Seitennavigation gibt; kein zusätzlicher Knopf auf der Startseite, damit bestehende Screenshots unverändert bleiben.
- **403 entfällt:** wie 3-1 – jede Freizeit-Rolle darf alles im Programm; Org-Rollen ohne Mitarbeit bekommen `404` (AD-12).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Material/Programm/EndpunktListe/Demo), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. `tests/Zeltlotse.E2E.Tests`) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Modul Programm mit Entität `Material` (genau ein Programmpunkt; Bezeichnung, Menge als Freitext, Status `Gebraucht`/`Vorhanden`/`Besorgt`/`Eingepackt`, höchstens eine mitbringende Person) und sieben Endpunkten unter `/api/freizeiten/{freizeitId}/programm` (Liste, anlegen, Autosave-PATCH Bezeichnung/Menge, löschen, Status setzen, mitbringen, nicht mitbringen; alle für Leitung und Team). Löschen eines Programmpunkts nimmt sein Material mit (`ON DELETE CASCADE`). AD-16: `PersonGeloescht` (Platzhalter gilt als frei), Datenauskunft „Bringt Material mit“, Lösch-Lauf. Web-App: Card „Material“ auf der Programmpunkt-Seite (hinzufügen, Autosave, Segmente mit „Gespeichert · Rückgängig“, „Ich bringe das mit“, löschen), neue Seite „Materialliste“ mit Balken „N von M eingepackt“ und einer Card je Programmpunkt, Link „Materialliste“ im Wochenraster. Neuer Baustein `Segmente`; `Card` um Dachzeile und Titel-Link ergänzt; `AutosaveFeld` um Rückmeldung `Gespeichert`. Demo-Daten, Doku-Seite `mitarbeitende/material.md`, vier neue Screenshots.

**Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Programm/Material.cs` -- Datenformen, `MaterialFelder`.
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/*` -- Entität und sechs Slices.
- `src/Modules/Programm/Zeltlotse.Programm/{ProgrammModul.cs,Daten/ProgrammDbContext.cs,Datenschutz/ProgrammDatenschutz.cs,Programme/FeldPatch.cs,Programme/ProgrammpunktBearbeiten.cs}`, Migration `20261013200038_Material` -- Routen, Tabelle, Zeilensperre, Datenschutz, gemeinsamer PATCH-Leser.
- `src/Zeltlotse.Ui.Komponenten/{Segmente.razor,Card.razor}`, `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Autosave/AutosaveFeld.razor,Texte/Anzeige.cs}` -- Bausteine, Client, Texte.
- `src/Zeltlotse.App/Features/Programm/{MaterialEintrag,MaterialStatusWahl,Materialliste,ProgrammpunktSeite,Wochenraster}.razor`, `ProgrammTexte.cs` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` -- Demo-Schritt `MaterialAsync`.
- Tests: `tests/Zeltlotse.Programm.Tests/MaterialTests.cs`, `tests/Zeltlotse.Api.Tests/MaterialTests.cs`, `tests/Zeltlotse.Ui.Tests/MaterialSeitenTests.cs`, ergänzt `EndpunktListeTests`, `DemoDatenTests`, `KomponentenTests`, `AutosaveTests`, `ProgrammSeitenTests`, `DokuRegeln`, `DokuTests`, `DokuScreenshotTests`.
- Doku: `doku/src/content/docs/mitarbeitende/material.md` (neu), `mitarbeitende/{index,programm}.md`, `leitung/index.md`, `daten.md`; Screenshots `material-programmpunkt-*`, `materialliste-*` (neu), `programmpunkt-*`, `wochenraster-*` (Card „Material“ bzw. Link).

**Review:** 36 Befunde (high 0, medium 3, low 26, false 7). Gepatcht: 1 Eintrag medium (Detailseite bricht bzw. zeigt „Noch kein Material.“, wenn die Materialliste nicht ladbar ist – drei Befunde, eine Ursache) und 6 low (Fehlertext des Status, Ordner `Materialien/`, Leerzeilen, drei Testlücken: Fehlerzweige, Sortierung nach dem Hinzufügen, nicht ladbares Material). Zurückgestellt: keine. Abgelehnt: 29, Gründe im Review Triage Log (u. a. Gleichzeitigkeitsfälle mit gelöschtem Material, Demo-Sonderfälle, 403/Token-Fälle ohne passende Rolle bzw. Token-Endpunkt).

**Folge-Review empfohlen:** nein. Erster Durchlauf ohne high und mit nur einem gepatchten medium-Eintrag.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI, E2E/Doku-Screenshots, nach den Patches) -- 2085 von 2085 grün, 0 übersprungen.
- `npm run build` in `doku/` -- fehlerfrei, 45 Seiten.
- Matrix-Audit: jede Zeile der I/O-Matrix hat mindestens einen gelaufenen, grünen Test.
- Screenshots per Pixelvergleich geprüft: nur Rauschen aus Datum/Uhrzeit und Testreihenfolge zurückgesetzt, echte Änderungen (`programmpunkt-*`, `wochenraster-*`, vier neue) behalten.

**Restrisiken:**
- Bringt eine Person Material mit und verliert später ihre Freizeit-Rolle, bleibt „Bringt mit“ stehen (Name dann „Bringt jemand mit“), niemand sonst kann es zurücknehmen – wie die Übernahme in 3-1.
- Wird ein Material gleichzeitig von anderer Seite gelöscht, zeigen Status-Tipp, Mitbringen und Löschen nur eine allgemeine Fehlermeldung; neues Laden behebt es.
- Interaktive Abläufe (Status tippen, Mitbringen, Hinzufügen) sind per bUnit und HTTP getestet, die Doku-Screenshots lesen nur.
- `freizeitpass-*`-Screenshots hängen von der Testreihenfolge ab (vorbestehend, nicht von dieser Story).
