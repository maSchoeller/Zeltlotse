---
title: 'Story 5-2: Belege, Soll-Ist und Erstattung'
type: 'feature'
created: '2026-10-20'
baseline_revision: 'cefbad7c6ce9480065abdee1b57ecf054d318bd8'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/5-1-kalkulation-und-beitragsrechner.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Team kann Ausgaben einer Freizeit nicht festhalten. Niemand sieht, ob die Freizeit im Plan liegt und wer noch Geld bekommt (CAP-33, FR-58, FR-59, NS-8).

**Approach:** Jedes Teammitglied erfasst Belege im Modul Finanzen: von Hand (Betrag, Kategorie, optional Bezeichnung) und auf Wunsch mit Foto oder PDF. Dazu kommt, wer bezahlt hat und ob erstattet wurde. Eine Soll-Ist-Seite vergleicht je Kategorie das Soll aus der Kalkulation (5-1) mit der Summe der Belege. Sie zeigt außerdem je Person, wie viel noch zu erstatten ist. Beides wird bei jedem Aufruf neu gerechnet, ohne Abrechnungsabschluss.

## Boundaries & Constraints

**Always:**
- **Beleg:** `Id`, `OrganisationId`, `FreizeitId`, `Version` (xmin), `ErfasstAm` (Uhr), `Kategorie` (eine der sechs `Kostenkategorie`, Pflicht), `Betrag` (Pflicht, Regeln und Parser wie 5-1 `FinanzenWerte.BetragLesen`), `Bezeichnung` (optional, höchstens 200 Zeichen), `PersonId` = wer bezahlt hat (Vorgabe: die angemeldete Person), `Erstattet` (Start `false`) und optional die Datei (`DateiArt?`, `Dateiname?`). Das sind höchstens zwei Pflichtfelder (NFR-12).
- **„Bezahlt von“** muss beim Anlegen und Ändern eine Person aus `IFreizeitTeamLesen.TeamAsync` sein (Leitung oder Team), sonst 400 am Feld `bezahltVon`. Die Namen setzt der Client zusammen, wie `ProgrammTexte.Namen` es tut (AD-2, kein Pfeil Finanzen→Personen). Ist die Person gelöscht (`GeloeschtePerson.Id`), steht dort „Gelöschte Person“. Ist kein Name bekannt, steht dort „Ohne Namen“.
- **Datei:** Upload separat als `POST …/belege/{id}/datei` (multipart, Feld `datei`), damit ein gescheiterter Upload den Beleg nicht verliert. Erlaubt sind nur JPEG, PNG, WebP und PDF bis 10 MB, geprüft am Inhalt (`DokumentDateien.LesenAsync`, `DateiPruefung`). Gespeichert wird per `IDateiSpeicher` unter `finanzen/<belegId>`. Ein neuer Upload ersetzt die alte Datei. Ausgeliefert wird per `DateiAuslieferung.Ausliefern` (Bilder inline, PDF als Anhang). Das Löschen eines Belegs löscht auch seine Datei.
- **Routen** unter `/api/freizeiten/{freizeitId}/finanzen/`: `GET belege`, `POST belege` (JSON), `PATCH belege/{id}` (AD-19, Felder `bezeichnung|betrag|kategorie|bezahltVon`), `DELETE belege/{id}`, `PATCH belege/{id}/erstattet` (AD-19, Feld `erstattet`, `"true"|"false"`), `POST|GET|DELETE belege/{id}/datei`, `GET soll-ist`. Alle Routen sind für Leitung und Team freigegeben (`Mitarbeit`) und tragen `.GehoertZuSchalter(FunktionsSchalter.Finanzen)`. Ihre Commands tragen `ISchalterCommand`.
- **Nach „gelaufen“** (FR-45, AD-17): Nur der Command hinter `PATCH …/erstattet` trägt `[ErlaubtNachAbschluss]` und prüft im Handler keine Phase. Jeder andere Schreibzugriff gibt 409 `freizeit-abgeschlossen`.
- **Soll-Ist:** Soll je Kategorie = Kosten laut Kalkulation (gleiche Rechnung wie `GET kalkulation`, fehlende Zeilen zählen als 0). Ist = Summe der Belege der Kategorie. Dazu kommt eine Zeile „Gesamt“. Status als reine Funktion in `Api.Contracts/Finanzen`: Ist ≤ 90 % vom Soll → „Im Plan“; über 90 % bis 100 % → „Knapp“; über Soll → „Überschritten“. Soll 0 mit Ist 0 ist „Im Plan“, Soll 0 mit Ist > 0 ist „Überschritten“. Jede Zeile wird ein `Balken` mit Zahl „Ist von Soll“, Statuswort und Art Normal/Warnung/Fehler (NFR-16, EXPERIENCE „Budget knapp/überschritten“).
- **Erstattungsübersicht:** je zahlender Person `Offen` (Summe nicht erstatteter Belege) und `Erstattet`, sortiert nach `Offen` absteigend. Im Client wird jede Person ein Balken: Anteil erstattet, Statuswort „Bekommt noch Geld“ (Warnung) oder „Alles erstattet“, Zahl „noch 45,80 €“.
- **AD-16, Klasse aufbewahrungspflichtig:** Finanzen implementiert `IDatenauskunft` (Abschnitt „Belege“: Kategorie, Betrag, Bezeichnung, erfasst am, erstattet) und empfängt `PersonGeloescht`. Dabei ersetzt `PersonId` den Personenbezug durch `GeloeschtePerson.Id`, der Rest bleibt. `FinanzenLoeschlauf` löscht nach der Org-Frist (`FreizeitenMitAbgelaufenenDaten`) zuerst die Dateien und dann die Belege.
- Commands mit Dateiinhalt oder `PersonId` überschreiben `ToString()` ohne diese Werte (AD-22). Bearbeiten nur an Ort und Stelle, Löschen nur über `InlineBestaetigung`, keine Pop-ups.

**Never:**
- Keine Überweisungen, kein Abrechnungsabschluss, keine Abschlussmappe (Non-goals). Kein „Beitrag bezahlt“ (5-3). Teilnehmerbeiträge kommen nicht ins Soll-Ist (Ausbau-Pool). Kein Budget-Balken im Cockpit.
- Kein Pfeil Finanzen→Personen und keine Spine-Änderung. Keine neuen Problemcodes, keine neue Löschfrist-Art.
- Kein `[Besonders]` und keine Verschlüsselung für Belege. Sie sind kein besonders schützenswertes Datum.
- Kein Belegdatum zum Bearbeiten (nur `ErfasstAm`), kein OCR.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Beleg von Hand | Tobias: `{betrag:"23,40", kategorie:"Verpflegung"}` | 201; `bezahltVon` = Tobias, `erstattet` false, ohne Datei | – |
| Pflichtfelder fehlen | ohne Betrag oder Kategorie, Kategorie `"xyz"` | 400 `validierung` am Feld | nichts gespeichert |
| Fremder Zahler | `bezahltVon` = Person außerhalb des Teams oder keine Guid | 400 am Feld `bezahltVon` | – |
| Foto hochladen | PNG an `…/datei` | 201/200; GET `…/datei` liefert das Bild inline mit `nosniff` | – |
| Falsche Datei | Text-Datei, leer, > 10 MB, Feld fehlt | 400 am Feld `datei` mit Text aus `DokumentDateien` | Beleg bleibt ohne Datei |
| Upload scheitert (Netz) | POST `…/datei` bricht ab | Beleg bleibt gespeichert, Meldung „Fehler: Datei nicht hochgeladen.“ mit „Erneut versuchen“ | Wiederholung lädt an denselben Beleg |
| Soll-Ist | Demo: Verpflegung Soll 4.200 €, Belege 3.900 € | 93 %, „Knapp“, Art Warnung | – |
| Überschritten / leer | Fahrt Soll 1.800 €, Ist 1.950 € / Kategorie ohne Belege | „Überschritten“ (Fehler) / „Im Plan“ mit 0,00 € | – |
| Erstattung | Lea 2 Belege offen (45,80 €), Jonas alle erstattet | Lea zuerst mit Offen 45,80 €, Jonas Offen 0 | – |
| Gelaufen | Freizeit abgeschlossen | GETs 200; PATCH `erstattet` 200; POST/PATCH/DELETE sonst 409 `freizeit-abgeschlossen` | Seite nur lesbar bis auf „Erstattet“ |
| Konflikt | `alterWert` veraltet | 409 `konflikt` | „Inzwischen geändert – neu laden?“ |
| Unbekannter Beleg | fremde oder unbekannte `id`, auch aus Freizeit Z | 404 | – |
| Rechte | Org-Admin ohne Freizeitrolle, fremde Organisation, Leitung von Z auf X, ohne Sitzung | 404 / 404 / 404 / 401 | – |
| Modul aus | Schalter Finanzen aus | jede Route 404; nach dem Einschalten sind die Daten wieder da | – |
| Person gelöscht | Konto der zahlenden Person gelöscht | Beleg bleibt, `bezahltVon` = Platzhalter; keine uuid-Spalte in `finanzen` trägt mehr die Person | – |
| Frist abgelaufen | Löschlauf mit Freizeit X | Belege und ihre Dateien in S3 weg, Belege in Z bleiben | – |

</intent-contract>

## Code Map

- `src/Modules/Finanzen/Zeltlotse.Finanzen/FinanzenModul.cs` -- Gruppe `/freizeiten/{freizeitId:guid}/finanzen/kalkulation`, `Mitarbeit`, `services.AddScoped<ILoeschlauf, FinanzenLoeschlauf>()`. Neue Gruppe `belege` und Route `soll-ist` daneben. Registrierung `IDatenauskunft` und `services.EmpfaengtEreignis<PersonGeloescht, …>()` wie `src/Modules/Programm/Zeltlotse.Programm/ProgrammModul.cs:32-35`.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Daten/FinanzenDbContext.cs` -- `FinanzenModell.Konfigurieren` (Tabellen snake_case, `BetragStellen` (12,2), `IsRowVersion`, `HasIndex(OrganisationId)`). `EinnahmeSperrenAsync` ist das Muster für `BelegSperrenAsync` (FOR UPDATE). Neue Tabelle `finanzen.beleg`.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Kalkulation/{FinanzenWerte,KalkulationBearbeiten,KalkulationLaden,EinnahmeAnlegen,EinnahmeLoeschen}.cs` -- `BetragLesen/BetragFehler/BetragText/BetragVergleichswert`, `FinanzenProbleme.Konflikt/Abgeschlossen`, `FeldPatch.Lesen`, `FinanzenAenderung<T>`, zweite Gelaufen-Prüfung über `IFreizeitZeitpunkte` (`KalkulationBearbeiten.cs:57-82`). Die Soll-Rechnung aus `KalkulationLaden` in einen internen Helfer ziehen, den `GET soll-ist` mitbenutzt.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Datenschutz/FinanzenLoeschlauf.cs` -- `KalkulationenLoeschen` um Belege und Dateien erweitern. Den Kommentar „braucht keine Datenauskunft“ anpassen.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Zeltlotse.Finanzen.csproj` -- `ProjectReference` auf `Zeltlotse.Personen.Contracts` mit dem Kommentar „Nur das Ereignis PersonGeloescht (AD-16)“, wie in Programm.
- `src/Modules/Programm/Zeltlotse.Programm/Datenschutz/ProgrammDatenschutz.cs` -- Muster für `ProgrammAuskunft : IDatenauskunft` und den Empfänger `ProgrammPersonLoeschen` (`ExecuteUpdateAsync` → `GeloeschtePerson.Id`).
- `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/AufgabeUebergang.cs:42-57` -- `IAngemeldet.PersonId` am Endpunkt in den Command geben.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Dokumente/{DokumentEndpunkte.cs:~62-110,DokumentBearbeiten.cs:76-94,~188}` -- Multipart-Lesen ohne `IFormFile` (`anfrage.ReadFormAsync`, `DokumentDateien.LesenAsync`, `ValidierungsAusnahme`), `speicher.SpeichernAsync/LoeschenAsync`, Auslieferung, `ToString()` ohne Inhalt.
- `src/Zeltlotse.SharedKernel/Dateien/{IDateiSpeicher,DateiPruefung,DokumentDateien,DateiAuslieferung}.cs` -- wiederverwenden, nicht ändern.
- `src/Zeltlotse.SharedKernel/Commands/IModulCommand.cs:43` -- `ErlaubtNachAbschlussAttribute`. `SharedKernel/Datenschutz/{IDatenauskunft,GeloeschtePerson,ILoeschlauf}.cs`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Team/IFreizeitTeamLesen.cs` -- `TeamAsync(freizeitId)` → `FreizeitTeamMitglied(PersonId, Leitung)` für die Prüfung von „bezahlt von“.
- `src/Zeltlotse.Api.Contracts/Finanzen/Kalkulation.cs` -- `Kostenkategorie`, `Kostenkategorien.Text/Lesen`, `KategorieFelder.BetragText`, `Beitragsrechnung.Kosten`. Neu ist `Finanzen/Belege.cs`.
- `src/Zeltlotse.Ui/Api/{FinanzenApiClient,FreizeitenApiClient.cs:385-419}.cs` -- Client-Muster, Multipart-Upload, Ergebnis-Records.
- `src/Zeltlotse.App/Features/Finanzen/{Kalkulation.razor,FinanzenReiter.razor,FinanzenTexte.cs}` -- Seitenmuster (NichtVerfuegbar, Ladefehler, gelaufen nur lesbar, `Balken`, `AutosaveFeld`, `InlineBestaetigung`, `Formular`).
- `src/Zeltlotse.App/Features/Freizeiten/Dokumente.razor:60-90,212-285` -- `DateiFeld`, Größenprüfung im Client, Meldung „Datei nicht hochgeladen.“ mit „Erneut versuchen“.
- `src/Zeltlotse.App/Features/Programm/{ProgrammTexte.cs:20-35,AufgabeSeite.razor:~145-157}` -- Namen aus `FreizeitAntwort.Leitung` und `GET personenliste`.
- `src/Zeltlotse.Ui/Autosave/{AutosaveFeld,AutosaveAnkreuzfeld,AutosaveAuswahlgruppe}.razor` -- Autosave für Text, Haken und Auswahl (`Mehrfach=false`). `Ui.Komponenten/{Balken,Datentabelle,DateiFeld}.razor`, `BalkenArt`, `Spalte<T>`. Muster für die Statusarten: `Features/Freizeiten/PlaetzeBalken.razor`.
- `tests/Zeltlotse.ArchitectureTests/{TypRegelnTests.cs:65-85,LoeschPflichtRegeln.cs,DokuRegeln.cs:33}` -- Liste von `[ErlaubtNachAbschluss]`, Löschpflicht (greift bei `PersonId`), Pflichtseiten.
- `tests/Zeltlotse.Finanzen.Tests/KalkulationTests.cs:225-243` -- prüft „kein `ErlaubtNachAbschluss`“ für die fünf 5-1-Commands. Anpassen.
- `tests/Zeltlotse.Api.Tests/{KalkulationTests.cs,DokumentHelfer.cs,DokumenteTests.cs,FreizeitpassSzenario.cs,EndpunktListeTests.cs:317-351,DemoDatenTests.cs:358-371,KontoLoeschenTests.cs:~275-300,DatenauskunftTests.cs}` -- `AufbauenAsync(postgres, s3)`, `AlleRouten`/`StatusAllerRoutenAsync`, `SchaltenAsync`, Abschluss per SQL, Multipart-Helfer, `RohAsync`, Löschlauf-Aufruf, Routenliste und Zähler 6.
- `tests/Zeltlotse.Ui.Tests/{FinanzenSeitenTests,DokumenteSeitenTests}.cs` -- bUnit mit `NachgebildeterHandler` und `InputFile`-Upload.
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs:811-865,925-967,DemoKatalog.cs:~371-392}` -- `FinanzenAsync`, Demo-Upload per Command, `DemoDateien.Png()`. Personen Jonas, Miriam (Leitung) und Lea (Team) in „Zeltlager Sommer 2027“.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:2299-2376,~2560` -- Abschnitt 5-1, `LeaStartseiteAsync`, `BildAsync`, `Geraete`, `E2EHilfe.AxeAsync`.
- `doku/src/content/docs/{finanzen/*.md,daten.md:80-82,leitung/module-schalten.md:15,41,leitung/freizeit-abschliessen.md:54,mitarbeitende/index.md:21}` -- „Noch nicht dabei“-Stellen für Belege. Die Sidebar erzeugt sich selbst (`astro.config.mjs:27`).

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Finanzen/Belege.cs` -- DTOs `Beleg(Id, Version, Kategorie, Betrag, Bezeichnung, BezahltVon, Erstattet, ErfasstAm, Datei?)`, `BelegDatei(Dateiname, IstBild)`, `BelegNeu(Kategorie?, Betrag?, Bezeichnung?, BezahltVon?)`, `SollIst(Zeilen, Gesamt, Erstattungen)`, `SollIstZeile(Kategorie?, Soll, Ist, Status)`, `Erstattung(PersonId, Offen, Erstattet)`, Enum `SollIstStatus`, Feld-Enums mit `…Felder`-Klassen, reine Funktion `SollIstRechnung.Status(soll, ist)`.
- `tests/Zeltlotse.Finanzen.Tests/BelegeTests.cs` -- zuerst: Statusgrenzen (0/0, 0/x, 90 %, 90,01 %, 100 %, darüber), Erstattung summieren und sortieren, Mandantenfilter für `Beleg`, Liste `ErlaubtNachAbschluss` nur am Erstattet-Command. `KalkulationTests.cs:225-243` anpassen.
- `tests/Zeltlotse.Api.Tests/BelegeTests.cs` (PostgreSQL + S3) -- vor dem Code: alle Matrixzeilen über HTTP, AD-30-Fälle auf allen neun Routen (404/401), Schalter aus, gelaufen, Konflikt, Datei-Rundweg mit `RohAsync`, Ersetzen und Löschen der Datei, Löschlauf, `PersonGeloescht`. Dazu `EndpunktListeTests` (neun Routen, Zähler 15), `DatenauskunftTests`, `KontoLoeschenTests`, falls nötig.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Belege/` -- Entität `Beleg` (`DateiPraefix = FinanzenModul.Schema`), Slices `BelegeLaden`, `BelegAnlegen`, `BelegBearbeiten`, `BelegLoeschen`, `BelegErstattet` (`[ErlaubtNachAbschluss]`), `BelegDatei` (Hochladen, Ausliefern, Entfernen), `SollIstLaden`.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/{FinanzenModul.cs,Daten/FinanzenDbContext.cs,Datenschutz/FinanzenLoeschlauf.cs,Datenschutz/FinanzenDatenschutz.cs,Zeltlotse.Finanzen.csproj}` und Migration `Belege` -- Routen, Tabelle, `BelegSperrenAsync`, Auskunft, `PersonGeloescht`-Empfänger, Löschlauf mit Dateien.
- `tests/Zeltlotse.ArchitectureTests/TypRegelnTests.cs` -- `Zeltlotse.Finanzen.Belege.BelegErstattetCommand` in die Liste aufnehmen und die Zusammenfassung anpassen.
- `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs` -- Laden, Anlegen, Löschen, Datei hochladen und entfernen, Soll-Ist laden, Pfade für Autosave und Datei-Link.
- `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs` -- vor dem Code: Liste mit Namen und Haken, Anlegen mit und ohne Foto, Upload-Fehler mit „Erneut versuchen“, Feldfehler, Löschen, Soll-Ist-Balken mit den drei Statuswörtern, Erstattungsübersicht, gelaufen (nur „Erstattet“ bedienbar), nicht verfügbar, Ladefehler.
- `src/Zeltlotse.App/Features/Finanzen/{Belege.razor,BelegSeite.razor,SollIst.razor,FinanzenReiter.razor,FinanzenTexte.cs}` -- Reiter „Belege“ und „Soll-Ist“. **Belege:** Card „Beleg erfassen“ (Betrag, Kategorie, Bezeichnung, Bezahlt von mit Vorgabe „ich“, `DateiFeld`; erst Anlegen, dann Upload). `Datentabelle` mit Beleg (Link zur BelegSeite), Kategorie, Betrag, Bezahlt von, Erstattet (`AutosaveAnkreuzfeld`) und Summe. **BelegSeite** (`…/finanzen/belege/{id}`): Autosave-Felder, Datei ansehen, ersetzen und entfernen (Bild als `<img>` vom API-Pfad), Erstattet, Löschen per `InlineBestaetigung`. **SollIst:** Card „Soll und Ist“ mit Balken je Kategorie und Gesamt. Card „Erstattungen“ mit Balken je Person oder `LeerZustand`.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests` -- Schritt `BelegeAsync` (idempotent, Commands): etwa sechs Belege von Jonas, Miriam und Lea, Verpflegung „Knapp“, Fahrt „Überschritten“, der Rest „Im Plan“, ein Beleg mit Foto (`DemoDateien.Png()`), Lea mit offenen und Jonas mit erstatteten Belegen.
- `doku/src/content/docs/finanzen/{belege,soll-ist}.md`, `finanzen/index.md` und die übrigen „Noch nicht dabei“-Stellen, `daten.md#finanzen`, `DokuRegeln.cs` -- Wer, Weg, Erfassen, Foto, Erstattet auch nach dem Abschluss, Statusgrenzen, Datenschutz und Löschfrist, Screenshots.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Abschnitt „Story 5-2“: `belege`, `beleg`, `soll-ist`, je Handy und Laptop, mit Axe.

**Acceptance Criteria:**
- Given die Demo-Freizeit, when Lea unter „Finanzen“ › „Belege“ einen Betrag und eine Kategorie einträgt und ein Foto wählt, then steht der Beleg mit „Bezahlt von Lea Feuerstein“ und dem Foto in der Liste.
- Given ein angelegter Beleg, when der Upload scheitert, then bleibt der Beleg gespeichert, und „Erneut versuchen“ lädt die Datei an denselben Beleg.
- Given neue oder geänderte Belege, when „Soll-Ist“ geöffnet wird, then zeigen Balken, Statuswörter und Erstattungsübersicht sofort den neuen Stand.
- Given eine abgeschlossene Freizeit, when ein Teammitglied den Haken „Erstattet“ setzt, then wird er gespeichert, während alle anderen Felder nur lesbar sind.
- Given die fertige Story, when `dotnet test` (alle Projekte), die Doku-Screenshot-Tests und `npm run build` in `doku/` laufen, then ist alles grün, und `belege-*`, `beleg-*` und `soll-ist-{handy,laptop}.png` liegen vor und sind verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 31 findings — high 0, medium 3, low 21, false 7, maybe-false 0
- findings:
  - `[low]` `[reject]` (Blind) Belege werden mit der Freizeit-Frist gelöscht, womöglich vor steuerlichen Aufbewahrungsfristen — AD-16 und NFR-5 legen die Frist in die Hand der Organisation, und sie kann die Frist hoch setzen. Eine eigene Belegfrist wäre eine neue `LoeschfristFeld`-Art mit Oberfläche, Migration und Tests.
  - `[low]` `[reject]` (Blind) Kein Änderungsprotokoll und keine Freigabe für Erstattungen — „Vertrauen statt Rechte-Matrix“ (SPEC Constraints). Der Fix bräuchte ein neues Modell.
  - `[low]` `[reject]` (Blind) Dateispeicher und Datenbank laufen auseinander, wenn der Commit nach dem Speicherzugriff scheitert — tritt nur bei einem Commit-Fehler auf, gleiches Muster wie bei den Dokumenten. Der Fix bräuchte einen Schritt nach dem Commit.
  - `[false]` `[reject]` (Blind) `Version` wird nicht geprüft — so verlangt es AD-19 (allein `alterWert` entscheidet), gleiches Muster wie in 5-1.
  - `[low]` `[reject]` (Blind) Wer das Team verlässt, erscheint als „Ohne Namen“; die Namen des Clients haben eine andere Quelle als die Prüfung des Servers — seltener Fall. Bestätigte Mitarbeitende und Team-Rolle entstehen gemeinsam bei der Bestätigung. Der Fix bräuchte eine neue Namensquelle oder einen Pfeil zu Personen.
  - `[low]` `[reject]` (Blind) Gelöschte Personen fallen in der Erstattung zu einer Zeile zusammen — Folge des Platzhalters aus AD-16, ausdrücklich gewollt.
  - `[low]` `[reject]` (Blind) Die Auskunft nennt die Freizeit nicht — `IFreizeitNamenLesen` läuft unter dem Mandantenfilter, die Auskunft geht über alle Organisationen. Der Fix bräuchte eine neue Schnittstelle.
  - `[low]` `[patch]` (Blind) Die Doku sagt, „PDF“ öffne die Datei, der Server liefert das PDF aber als Anhang — Wortlaut in `belege.md` und auf der Belegseite korrigiert.
  - `[low]` `[reject]` (Blind) Die Belegseite lädt die ganze Liste — es sind wenige Dutzend Belege je Freizeit. Der Fix bräuchte eine zehnte Route.
  - `[false]` `[reject]` (Blind) Unbekannte Freizeit 200 statt 404, 0-€-Beleg, „Gesamt“ verdeckt eine Überschreitung — die Rechteprüfung gibt für eine unbekannte Freizeit auf allen Routen 404 (getestet). 0 € erlaubt die Spec ausdrücklich (Parser aus 5-1). Die Kategorie-Balken stehen direkt über „Gesamt“.
  - `[low]` `[reject]` (Blind) Nach einem Dateifehler fehlt ein direkter Weg zum Beleg — der Beleg steht sofort in der Liste mit Link zu seiner Seite.
  - `[low]` `[reject]` (Blind) Testlücken: Speicherfehler, verwaiste Datei, Zahler außerhalb des Teams, gecachtes Listenbild — ohne benannten Schaden, über die Fälle oben abgedeckt. Die Testlücken der Oberfläche siehe Verification.
  - `[low]` `[patch]` (Blind) UTF-8-BOM in acht Dateien ergänzt, unsortierte Usings, Zahl 5 ohne Erklärung — BOM entfernt und Usings sortiert. Die 5 ist die Länge der Endung und bleibt.
  - `[medium]` `[patch]` (Edge) „Erneut versuchen“ in der Belegliste liest eine Datei aus einem `InputFile`, das neu verschlüsselt und entsorgt wurde — `_dateiFeld` wird jetzt erst hochgezählt, wenn kein Upload mehr offen ist.
  - `[medium]` `[patch]` (Edge) Eine `JSException` beim Lesen wird nicht abgefangen — gleiche Ursache, mit dem Befund davor behoben.
  - `[low]` `[reject]` (Edge) Ersetzen überschreibt die Datei, bevor der Commit scheitert — wie oben: nur bei einem Commit-Fehler.
  - `[low]` `[reject]` (Edge) Löschen im Speicher vor dem Commit — wie oben.
  - `[low]` `[reject]` (Edge) Quelle der Zahler-Optionen im Client und Prüfung im Server — wie der Befund „Ohne Namen“ oben.
  - `[low]` `[patch]` (Edge) Teamprüfung vor der Konfliktprüfung (400 statt 409) — Reihenfolge im Handler getauscht, Test ergänzt.
  - `[medium]` `[patch]` (Edge) Behauptung: Wiederholung lädt an denselben Beleg, scheitert aber im echten Browser — gleiche Ursache wie der erste Edge-Befund.
  - `[low]` `[patch]` (Verification) Haken „Erstattet“ auf der Belegseite nie geklickt — bUnit-Test für den PATCH an `…/erstattet` bei gelaufen ergänzt.
  - `[low]` `[patch]` (Verification) „Datei entfernen“ und „Erneut versuchen“ auf der Belegseite ungetestet — zwei bUnit-Tests ergänzt.
  - `[low]` `[patch]` (Verification) Teilweiser Fehler beim Laden der Namen ungetestet — bUnit-Test ergänzt (Personenliste wirft).
  - `[low]` `[reject]` (Verification, Other) Dateispeicher vor dem Speichern in der Datenbank — wie der Blind-Befund oben.
  - `[low]` `[patch]` (Verification, Other) Konfliktreihenfolge 400 statt 409 — gleiche Ursache wie der Edge-Befund, mit ihm behoben.
  - `[false]` `[reject]` (Intent) Kein 403-Test — `Mitarbeit` umfasst beide Freizeitrollen, eine falsche Rolle innerhalb der Freizeit gibt es nicht. Org-Admin ohne Rolle gibt 404 (AD-30, `RechtePruefung`).
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — es gibt keine Token-Route.
  - `[low]` `[reject]` (Intent) ACs über bUnit und API statt Playwright geprüft — AD-30 verlangt Playwright nur für Kernabläufe und Screenshots. Beide Schichten sind abgedeckt.
  - `[false]` `[reject]` (Intent) Doku im Ordner `finanzen/` statt unter einer Rolle — Muster aus 5-1, `mitarbeitende/index.md` verlinkt dorthin.
  - `[false]` `[reject]` (Intent) Abnahmeschritte außerhalb des Diffs nicht nachgewiesen — Build, Tests und Doku-Build sind gelaufen und unter Auto Run Result festgehalten.
  - `[false]` `[reject]` (Intent) Kalkulations-Screenshots geändert — beabsichtigt, wegen der neuen Reiter.

## Design Notes

- **Statusgrenzen:** EXPERIENCE nennt „Im Plan“, „Knapp“ und „Überschritten“, aber keine Schwelle. Gewählt ist eine einzige feste Grenze bei 90 % vom Soll, ohne Einstellung. Das ist einfach und reicht, um rechtzeitig zu warnen.
- **Upload getrennt vom Anlegen:** EXPERIENCE verlangt, dass ein gescheiterter Upload den Eintrag ohne Datei behält. Darum sind Anlegen (JSON) und Datei (multipart) zwei Aufrufe, und „Erneut versuchen“ wiederholt nur den zweiten.
- **Erstattet als eigene Route:** `[ErlaubtNachAbschluss]` hängt an einem Command-Typ. Die Ausnahme greift damit genau für den Erstattungsvermerk (AD-17), und die übrigen Felder bleiben gesperrt.
- **Namen im Client:** AD-2 lässt Finanzen nur Freizeiten lesen, und zusammengeführte Ansichten baut der Client. Die Prüfung „gehört zum Team“ macht der Server über `IFreizeitTeamLesen`. Die Namen kommen wie im Programm aus Freizeit und Personenliste.
- **Personenbezug:** Ein Beleg ist aufbewahrungspflichtig (AD-16). Bei `PersonGeloescht` bleibt er mit Platzhalter erhalten. Gelöscht wird er mit der Freizeitfrist der Organisation, denn eine eigene Belegfrist gibt es in `LoeschfristFeld` nicht. Eine solche Frist wäre neue Speziallogik.
- **Kein Belegdatum:** FR-58 verlangt kein Datum. `ErfasstAm` reicht zum Sortieren und spart ein Pflicht- oder Datumsfeld.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Finanzen.Tests` -- expected: grün
- `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*BelegeTests*"` (dazu `*KalkulationTests*`, `*EndpunktListeTests*`) -- expected: grün
- `dotnet test --project tests/Zeltlotse.Ui.Tests` und `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- expected: grün
- einmal am Ende: alle Testprojekte einzeln, darunter `dotnet test --project tests/Zeltlotse.E2E.Tests` (inklusive Kategorie Doku) -- expected: grün, Screenshots erzeugt
- einmal am Ende: `npm run build` in `doku/` -- expected: fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Das Modul Finanzen hat jetzt Belege. Jedes Teammitglied erfasst sie von Hand: Betrag und Kategorie sind Pflicht, die Bezeichnung ist optional. „Bezahlt von“ ist ohne Angabe die angemeldete Person, und der Server prüft, ob sie zum Team gehört. Foto oder PDF kommen in einem eigenen Upload dazu. Scheitert der Upload, bleibt der Beleg gespeichert, und „Erneut versuchen“ lädt die Datei an denselben Beleg. Der Haken „Erstattet“ ist der einzige Schreibzugriff, der auch nach dem Abschluss geht (`[ErlaubtNachAbschluss]`).

Die Seite Soll-Ist vergleicht je Kategorie und gesamt das Soll aus der Kalkulation mit der Summe der Belege. Die Grenzen sind: bis 90 % „Im Plan“, bis 100 % „Knapp“, darüber „Überschritten“. Darunter zeigt die Erstattungsübersicht je Person, wer noch Geld bekommt.

Zum Datenschutz (AD-16) gehören drei Teile: die Datenauskunft mit dem Abschnitt „Belege“, der Platzhalter bei `PersonGeloescht` und der Löschlauf, der auch die Dateien löscht.

**Geänderte Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Finanzen/Belege.cs`: DTOs, Feld-Enums, reine Funktionen `SollIstRechnung` (Status, Zeilen, Erstattungen).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Belege/*`: Entität `Beleg` und die Slices Laden, Anlegen, Bearbeiten, Löschen, Erstattet, Datei (Hochladen, Ausliefern, Entfernen) und Soll-Ist.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/{FinanzenModul.cs,Daten/FinanzenDbContext.cs,Datenschutz/*,Kalkulation/KalkulationLaden.cs,Zeltlotse.Finanzen.csproj}`, Migration `Belege`: Routen, Tabelle `finanzen.beleg`, Sperr-Helfer, Auskunft, Empfänger `PersonGeloescht`, Löschlauf mit Dateien, gemeinsame Soll-Rechnung `KalkulationAbfrage`.
- `src/Zeltlotse.App/Features/Finanzen/{Belege,BelegSeite,SollIst}.razor`, `FinanzenReiter.razor` (vier Reiter), `FinanzenTexte.cs` (Namen, Texte). `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs`.
- `src/Zeltlotse.Api/Demo/*`: Schritt `BelegeAsync` mit 6 Belegen (Verpflegung knapp, Fahrt überschritten, ein Foto, Lea mit offenen Erstattungen).
- Tests: neu `Api.Tests/BelegeTests.cs` (PostgreSQL und S3, alle Matrixzeilen, 404/401 auf neun Routen) und `Finanzen.Tests/BelegeTests.cs`. Erweitert: `Ui.Tests/FinanzenSeitenTests.cs`, `DokuScreenshotTests` (Abschnitt 5-2), `EndpunktListeTests`, `DemoDatenTests`, `TypRegelnTests`, `LoeschPflichtTests`, `DokuTests`, `PersonGeloeschtTests`, `KalkulationTests`.
- Doku: neu `doku/src/content/docs/finanzen/{belege,soll-ist}.md`. „Noch nicht dabei“-Stellen und `daten.md#finanzen` angepasst, `DokuRegeln.PflichtSeiten` ergänzt. Neue Screenshots `belege-*`, `beleg-*`, `soll-ist-*`. `kalkulation-*` und `beitragsrechner-*` sind wegen der neuen Reiter neu.

**Review:** 31 Befunde: 3 medium, 21 low, 7 false. Einzelheiten stehen im Review Triage Log.
- Patches:
  - Medium: „Erneut versuchen“ in der Belegliste las die Datei aus einem bereits ersetzten Dateifeld. Das Feld wird jetzt erst geleert, wenn kein Upload mehr offen ist (3 Befunde, gleiche Ursache).
  - Low: Konfliktprüfung vor der Teamprüfung, PDF-Wortlaut („herunterladen“), BOM entfernt und Usings sortiert, vier bUnit-Tests (Erstattet auf der Belegseite, Datei entfernen, Upload-Wiederholung auf der Belegseite, teilweiser Fehler beim Laden der Namen).
- Nichts zurückgestellt.
- Verworfen mit Begründung:
  - eigene Aufbewahrungsfrist
  - Änderungsprotokoll
  - Speicher vor Commit (3×)
  - Namen ausgeschiedener Personen bzw. andere Namensquelle (2×)
  - zusammengefasste gelöschte Personen
  - Freizeit in der Auskunft
  - Einzel-GET für den Beleg
  - Weg zum Beleg nach Dateifehler
  - weitere Testlücken
  - ACs nicht per Playwright
  - Als false verworfen: `version`, Randfälle (unbekannte Freizeit, 0 €, Gesamt), kein 403, keine Token-Route, Doku-Ordner, Abnahmenachweise, Kalkulations-Screenshots.

**Follow-up review recommended:** false. Gepatcht wurden ein Eintrag medium und fünf Einträge low, high keiner.

**Verifikation (nach den Patches, ein Komplettlauf):**
- `dotnet build Zeltlotse.slnx`: 0 Warnungen, 0 Fehler.
- Alle Testprojekte einzeln grün:
  - Api 965
  - E2E inklusive Doku-Screenshots 185
  - Ui 781
  - Architektur 135
  - Finanzen 61
  - Kueche 119
  - Personen 157
  - SharedKernel 111
  - Freizeiten 93
  - Programm 91
  - Organisationen 58
  - Sicherheit 36
  - Inventar 34
  - Konten 26
  - Ablaeufe 10
- `npm run build` in `doku/`: fehlerfrei, 65 Seiten. Es gibt nur den bekannten Sitemap-Hinweis.
- Alle neuen Screenshots liegen vor und sind verlinkt. Stichprobe `soll-ist-laptop.png` geprüft.
- Screenshots, die nur Rauschen enthalten (zufällige Anmeldelinks, Datumsziffern), wurden auf HEAD zurückgesetzt.

**Restrisiken:**
- Speicherzugriffe (Hochladen, Ersetzen, Löschen) laufen vor dem Commit, wie bei den Dokumenten. Scheitert der Commit, können Datei und Zeile auseinanderlaufen, oder eine Datei bleibt verwaist.
- Belege werden mit der Freizeit-Frist der Organisation gelöscht. Eine eigene, längere Aufbewahrungsfrist gibt es nicht.
- Wer das Team verlässt, erscheint bei „Bezahlt von“ als „Ohne Namen“.
- Die Rückfrage beim Abschluss nennt keine offenen Erstattungen, denn Freizeiten liest Finanzen nicht.
- Am Handy brechen die vier Reiter in zwei Zeilen um.
