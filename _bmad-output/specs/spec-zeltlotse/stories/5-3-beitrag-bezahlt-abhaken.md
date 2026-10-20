---
title: 'Story 5-3: Beitrag bezahlt abhaken'
type: 'feature'
created: '2026-10-20'
baseline_revision: '5b8e55e69a31feab1918733ed4d95b4e3dc28e09'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/5-2-belege-soll-ist-und-erstattung.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Team weiß nicht, welche Teilnehmenden ihren Beitrag schon bezahlt haben, und niemand sieht, wer einen Haken gesetzt hat (CAP-34, FR-60, NS-8).

**Approach:** Im Modul Finanzen gibt es einen neuen Reiter „Beiträge“. Er listet jede bestätigte teilnehmende Person der Freizeit mit einem Haken „bezahlt“. Ein Tipp setzt oder entfernt den Haken. Zeltlotse speichert dazu einen Beitrag-bezahlt-Vermerk mit der Person, die abgehakt hat, und dem Zeitpunkt. Ein Balken zeigt „x von y bezahlt“.

## Boundaries & Constraints

**Always:**
- **Vermerk** (`BeitragVermerk`, Tabelle `finanzen.beitrag_vermerk`): `Id`, `OrganisationId`, `FreizeitId`, `PersonId` (die teilnehmende Person, AD-7), `AbgehaktVon` (PersonId, wer abgehakt hat), `AbgehaktAm` (Uhr). Es gibt höchstens einen Vermerk je Freizeit und Person: ein eindeutiger Index auf (`FreizeitId`, `PersonId`) mit Filter „`PersonId` ≠ `GeloeschtePerson.Id`“. Es gibt keinen Vermerk für „offen“. „Wieder offen“ löscht den Vermerk endgültig. Kein Schein-Löschen, kein `Version`, denn ein Vermerk wird nie bearbeitet.
- **Wer gilt als Teilnehmende:** Neue Leseschnittstelle `IFreizeitTeilnehmendeLesen.TeilnehmendeAsync(freizeitId)` in `Freizeiten.Contracts/Personen`. Sie liefert die verschiedenen `PersonId` aus Anmeldungen mit Art Teilnehmende und Status bestätigt, ohne den Platzhalter, unter dem Mandantenfilter. Mitarbeitende, eingegangene und abgesagte Anmeldungen gehören nicht dazu.
- **Routen** unter `/api/freizeiten/{freizeitId}/finanzen/beitraege`: `GET ""`, `POST /{personId:guid}/bezahlt`, `POST /{personId:guid}/offen` (Statuswechsel nach AD-19/AD-28, kein PATCH). Alle Routen sind für Leitung und Team freigegeben (`FinanzenModul.Mitarbeit`) und tragen `.GehoertZuSchalter(FunktionsSchalter.Finanzen)`. Die Commands tragen `IBenutzerCommand` und `ISchalterCommand`, aber **kein** `[ErlaubtNachAbschluss]`, denn AD-17 nennt den Vermerk nicht. Nach „gelaufen“ antworten beide POSTs mit 409 `freizeit-abgeschlossen` (zweite Prüfung über `IFreizeitZeitpunkte` wie in 5-1/5-2).
- **GET** liefert `Beitraege(Zeilen, Bezahlt, Gesamt)`. Je Person aus `TeilnehmendeAsync` gibt es eine `BeitragZeile(PersonId, Bezahlt, AbgehaktVon?, AbgehaktAm?)`. Vermerke von Personen, die nicht mehr bestätigte Teilnehmende sind, bleiben gespeichert, stehen aber nicht in der Liste und zählen nicht mit.
- **POST bezahlt:** Ist `personId` keine bestätigte Teilnehmende dieser Freizeit, kommt 404. Sonst wird der Vermerk mit `AbgehaktVon` = angemeldete Person (`IAngemeldet.PersonId`) angelegt. Ist schon einer da, bleibt er unverändert (wiederholt unschädlich, der erste Vermerk zählt). Zwei gleichzeitige Haken ergeben genau einen Vermerk: Eine Verletzung des eindeutigen Index wird als „schon bezahlt“ beantwortet. Antwort 200 mit der `BeitragZeile`.
- **POST offen:** Bei unbekannter Person kommt 404 wie oben. Sonst wird der Vermerk gelöscht, falls einer da ist. Antwort 200 mit `BeitragZeile(…, Bezahlt=false, null, null)`.
- **AD-16, Klasse aufbewahrungspflichtig:** `FinanzenPersonLoeschen` setzt `PersonId` und `AbgehaktVon` der gelöschten Person auf `GeloeschtePerson.Id`. `FinanzenAuskunft` bekommt den Abschnitt „Beiträge“. Er nennt den eigenen Beitrag („Beitrag bezahlt“, vermerkt am …) und die eigenen Haken für andere ohne deren Namen („Beitrag einer teilnehmenden Person als bezahlt abgehakt“, am …). `FinanzenLoeschlauf` löscht die Vermerke nach der Org-Frist mit.
- **Oberfläche** (`…/finanzen/beitraege`, Reiter „Beiträge“): Card „Beiträge“ mit `Balken` „1 von 2 bezahlt“. Das Statuswort ist „Alle bezahlt“ (Normal) oder „Offen“ (Warnung). Darunter steht je Person ein `Ankreuzfeld` mit dem Namen. Ist der Haken gesetzt, steht als Hinweis „Abgehakt von Lea Feuerstein am 20.10.2026“. Ein Tipp sendet sofort, ohne Bestätigung, und lädt neu. Fehler erscheinen an Ort und Stelle. Ohne bestätigte Teilnehmende zeigt `LeerZustand` „Noch keine bestätigten Teilnehmenden.“. Nach „gelaufen“ sind die Felder deaktiviert. Namen der Teilnehmenden kommen aus `GET personenliste` (Art Teilnehmende, Status bestätigt, Schlüssel `PersonId`), die Namen derer, die abgehakt haben, aus `FinanzenTexte.NamenLadenAsync`. Fehlt ein Name, steht dort „Ohne Namen“, beim Platzhalter „Gelöschte Person“.
- Commands mit `PersonId` überschreiben `ToString()` ohne Personenkennung (AD-22).

**Never:**
- Kein Betrag, keine Teilzahlung, kein Status „erlassen“, keine Zahlungsinfo für Eltern, kein Soll-Ist mit Beiträgen (Ausbau-Pool). Keine Mail, keine Erinnerung.
- Kein Pfeil Finanzen→Personen, keine Spine-Änderung, keine neuen Problemcodes, kein `[ErlaubtNachAbschluss]` für den Vermerk.
- Keine Beiträge für Mitarbeitende. Kein Vermerk-Verlauf, nur der aktuelle Vermerk.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Liste | X: Mia bestätigt, Emma eingegangen, Tobias Mitarbeitende | GET 200: nur Mia, `bezahlt` false, `gesamt` 1, `bezahlt`-Zahl 0 | – |
| Abhaken | Tobias: POST `…/{Mia}/bezahlt` | 200, `bezahlt` true, `abgehaktVon` = Tobias, `abgehaktAm` = Uhr | – |
| Doppelt | Miriam hakt danach Mia erneut ab | 200, `abgehaktVon` bleibt Tobias | – |
| Wieder offen | POST `…/{Mia}/offen` (auch zweimal) | 200, `bezahlt` false, Vermerk gelöscht | – |
| Keine Teilnehmende | `personId` = Emma (eingegangen), Tobias, unbekannt, Kind aus Z | 404 | nichts gespeichert |
| Abgesagt danach | Mia abgehakt, dann abgesagt | GET ohne Mia; Vermerk bleibt in der Tabelle | – |
| Gelaufen | Freizeit abgeschlossen | GET 200; beide POSTs 409 `freizeit-abgeschlossen` | Haken deaktiviert |
| Rechte | Org-Admin ohne Freizeitrolle, fremde Organisation, Leitung von Z auf X, ohne Sitzung | 404 / 404 / 404 / 401 | – |
| Modul aus | Schalter Finanzen aus | jede Route 404; nach dem Einschalten ist der Vermerk wieder da | – |
| Person gelöscht | Konto von Mia oder Tobias gelöscht | Vermerk bleibt, betroffene Spalte = Platzhalter; keine uuid-Spalte in `finanzen` trägt mehr die Person | – |
| Frist abgelaufen | Löschlauf mit Freizeit X | Vermerke in X weg, in Z bleiben | – |

</intent-contract>

## Code Map

- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Personen/IFreizeitPersonenzahlLesen.cs` -- Muster für die neue Schnittstelle `IFreizeitTeilnehmendeLesen.cs` daneben (gleicher Doku-Stil, „Story 5-3 … Pfeil Finanzen → Freizeiten“).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitPersonenLesen.cs:~35-60` -- `FreizeitPersonenzahlLesen.KennungenAsync` filtert schon `Art == Teilnehmende && Status == Bestaetigt`; neue Klasse `FreizeitTeilnehmendeLesen(FreizeitenLeseDbContext)` daneben, ohne Platzhalter, distinct. Registrierung in `FreizeitenModul.cs:~55-59`.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/FinanzenModul.cs` -- Gruppe `finanzen`; neue Untergruppe `beitraege` mit drei Routen; XML-Kommentare um Story 5-3 ergänzen. `Zeltlotse.Finanzen.csproj` referenziert `Freizeiten.Contracts` schon; Kommentar um „bestätigte Teilnehmende für Beiträge“ ergänzen.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Belege/{Beleg.cs,BelegErstattet.cs,BelegSchritte.cs,BelegAnlegen.cs}` -- Muster für Entität (`IOrganisationsEigen`), Command/Handler/Endpunkt und Antwortabbildung; `BelegAnlegen` zeigt, wie `IAngemeldet.PersonId` in den Command kommt.
- `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/AufgabeUebergang.cs` -- Muster für Statuswechsel per POST (Enum `Uebergang`, ein Command, zwei Endpunkt-Methoden, Phase-Prüfung über `IFreizeitZeitpunkte`, `ToString()` ohne Person).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Kalkulation/{FinanzenWerte.cs,KalkulationBearbeiten.cs:57-82}` -- `FinanzenProbleme.Abgeschlossen`, zweite Gelaufen-Prüfung.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Daten/FinanzenDbContext.cs` -- `FinanzenModell.Konfigurieren`: Tabelle snake_case, `HasIndex(OrganisationId)`, neuer gefilterter Unique-Index (`HasFilter`). Neues `DbSet<BeitragVermerk>` in Schreib- und Lese-Context. Danach Migration `Beitraege` (`dotnet ef migrations add` wie bei `20261020075807_Belege`).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Datenschutz/{FinanzenDatenschutz.cs,FinanzenLoeschlauf.cs}` -- Empfänger, Auskunft (Abschnitt „Beiträge“ als zweiter Abschnitt), Löschlauf erweitern.
- `src/Zeltlotse.Api.Contracts/Finanzen/Belege.cs` -- Stil für neue DTOs in `Finanzen/Beitraege.cs`.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Personenliste.cs` -- `PersonenlisteZeile(AnmeldungId, PersonId, Vorname, Nachname, Art, Status)`.
- `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs` -- neue Methoden `BeitraegeLadenAsync`, `BeitragBezahltAsync`, `BeitragOffenAsync` (Ergebnis-Records mit Fehlerfällen wie die Beleg-Methoden). `FreizeitenApiClient.PersonenlisteLadenAsync` (l.~314).
- `src/Zeltlotse.App/Features/Finanzen/{FinanzenReiter.razor,FinanzenTexte.cs:~118-160,SollIst.razor,Belege.razor}` -- Reiter, `Namen`/`NamenLadenAsync`/`Zahler`/`Leise`, Seitenmuster (NichtVerfuegbar, Ladefehler, gelaufen). Neue Funktion `TeilnehmendeNamen(personenliste)` in `FinanzenTexte`.
- `src/Zeltlotse.Ui.Komponenten/{Ankreuzfeld,Balken,LeerZustand,Card}.razor` -- Bausteine; `Ankreuzfeld` hat `Hinweis`, `Fehler`, `Deaktiviert`, Attribute für `@onchange`.
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs:~185-190,872ff,DemoKatalog.cs:~120,149-154}` -- neuer idempotenter Schritt `BeitraegeAsync` nach `BelegeAsync`: Lea hakt Mia als bezahlt ab (per Command). Die Anmeldungen bleiben, wie sie sind.
- `tests/Zeltlotse.Api.Tests/{FreizeitpassSzenario.cs,BelegeTests.cs,EndpunktListeTests.cs:317-360,DemoDatenTests.cs,DatenauskunftTests.cs,PersonGeloeschtTests.cs,KontoLoeschenTests.cs}` -- `AufbauenAsync`, `KindAnmeldenAsync(Mia)` + `BestaetigenAsync`, `AbsagenAsync`, Schalter, Abschluss per SQL, `AlleRouten`/Zähler (15 → 18), Löschlauf-Aufruf.
- `tests/Zeltlotse.Finanzen.Tests/BelegeTests.cs` -- Muster für Mandantenfilter-Test und Attribut-Prüfungen.
- `tests/Zeltlotse.ArchitectureTests/{TypRegelnTests.cs:65-85,DokuRegeln.cs}` -- Liste `[ErlaubtNachAbschluss]` bleibt unverändert; Pflichtseite `finanzen/beitraege` ergänzen.
- `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs` -- bUnit mit `NachgebildeterHandler`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:2375ff` -- Abschnitt 5-2 als Muster (`LeaStartseiteAsync`, `BildAsync`, `Geraete`, `E2EHilfe.AxeAsync`).
- `doku/src/content/docs/{finanzen/*.md,daten.md:~84,mitarbeitende/index.md,leitung/freizeit-abschliessen.md,leitung/module-schalten.md}` -- „Noch nicht dabei“-Stellen zu „Beitrag bezahlt“ in `index.md:22`, `kalkulation.md:68`, `belege.md:72`, `beitragsrechner.md:47`, `soll-ist.md:46`.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Finanzen/Beitraege.cs` -- DTOs `Beitraege(IReadOnlyList<BeitragZeile> Zeilen, int Bezahlt, int Gesamt)`, `BeitragZeile(Guid PersonId, bool Bezahlt, Guid? AbgehaktVon, DateTimeOffset? AbgehaktAm)`.
- `tests/Zeltlotse.Api.Tests/BeitraegeTests.cs` (PostgreSQL) -- zuerst: alle Matrixzeilen über HTTP, AD-30-Fälle auf allen drei Routen (404/401; eine falsche Rolle innerhalb der Freizeit gibt es nicht, Token-Routen gibt es nicht), Schalter aus, gelaufen, gleichzeitiges Abhaken (zwei parallele POSTs → ein Vermerk), Löschlauf, `PersonGeloescht` für beide Spalten. Dazu `EndpunktListeTests` (drei Routen, Zähler 18), `DatenauskunftTests` (Abschnitt „Beiträge“), `KontoLoeschenTests`/`PersonGeloeschtTests` falls dort Spalten gezählt werden.
- `tests/Zeltlotse.Finanzen.Tests/BeitraegeTests.cs` -- zuerst: Mandantenfilter für `BeitragVermerk`, kein `[ErlaubtNachAbschluss]` an den neuen Commands, `ToString()` ohne Personenkennung.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Personen/IFreizeitTeilnehmendeLesen.cs`, `…/Zeltlotse.Freizeiten/Rechte/FreizeitPersonenLesen.cs`, `FreizeitenModul.cs` -- Schnittstelle, Umsetzung, Registrierung.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Beitraege/{BeitragVermerk.cs,BeitraegeLaden.cs,BeitragUebergang.cs}` -- Entität und Slices.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/{FinanzenModul.cs,Daten/FinanzenDbContext.cs,Datenschutz/FinanzenDatenschutz.cs,Datenschutz/FinanzenLoeschlauf.cs,Zeltlotse.Finanzen.csproj}` und Migration `Beitraege` -- Routen, Tabelle, Index, Empfänger, Auskunft, Löschlauf.
- `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs` -- Laden, Bezahlt, Offen.
- `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs` -- vor dem Code: Liste mit Namen und Balken, Hinweis „Abgehakt von …“, Tipp setzt und entfernt (POST an den richtigen Pfad, danach neu laden), Fehler an Ort und Stelle, Leer-Zustand, gelaufen (deaktiviert), nicht verfügbar, Ladefehler, fünfter Reiter.
- `src/Zeltlotse.App/Features/Finanzen/{Beitraege.razor,FinanzenReiter.razor,FinanzenTexte.cs}` -- Seite und Reiter „Beiträge“.
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs,DemoKatalog.cs}` + `DemoDatenTests` -- Schritt `BeitraegeAsync`.
- `doku/src/content/docs/finanzen/beitraege.md` (neu), die fünf „Noch nicht dabei“-Stellen, `finanzen/index.md` (Aufzählung), `daten.md#finanzen`, `mitarbeitende/index.md`, `leitung/freizeit-abschliessen.md`, `DokuRegeln.cs` -- Wer, Weg, Abhaken und wieder öffnen, wer abgehakt hat, wer als Teilnehmende zählt, nach dem Abschluss nur lesbar, Datenschutz und Löschfrist, Screenshots.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Abschnitt „Story 5-3“: `beitraege` je Handy und Laptop, mit Axe.

**Acceptance Criteria:**
- Given die Demo-Freizeit, when Lea „Finanzen“ › „Beiträge“ öffnet, then steht dort Mia Probst mit gesetztem Haken, „Abgehakt von Lea Feuerstein am …“ und der Balken „1 von 1 bezahlt“.
- Given ein Teammitglied tippt auf einen offenen Haken, when die Seite neu lädt, then ist der Haken gesetzt und nennt das Teammitglied als die Person, die abgehakt hat; ein zweiter Tipp macht ihn wieder offen.
- Given eine abgeschlossene Freizeit, when die Seite „Beiträge“ geöffnet wird, then sind die Haken sichtbar, aber deaktiviert.
- Given die fertige Story, when `dotnet test` (alle Projekte), die Doku-Screenshot-Tests und `npm run build` in `doku/` laufen, then ist alles grün, und `beitraege-{handy,laptop}.png` liegen vor und sind verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 31 findings — high 0, medium 0, low 20, false 11, maybe-false 0
- findings:
  - `[low]` `[reject]` (Blind) Gleichzeitiges „offen“ zwischen INSERT und Nachladen lässt `SingleAsync` werfen (500) — zwei Tipps auf dieselbe Person im selben Millisekundenfenster; der Fix bräuchte einen neuen Zweig, ein erneuter Tipp heilt es.
  - `[low]` `[patch]` (Blind) Nach gescheitertem Nachladen zeigt der Haken den alten Stand — Ursache „Banner bleibt“ behoben (`_meldung` wird beim nächsten Tipp geleert); lokales Übernehmen der Zeile verworfen, weil es nur offline nach erfolgreichem POST auftritt.
  - `[low]` `[patch]` (Blind) Fehler-Banner nach gescheitertem Nachladen verschwindet nie — `_meldung = null` am Anfang von `UmschaltenAsync`.
  - `[low]` `[patch]` (Blind) 404 beim Tipp fordert sinnlos zum Wiederholen auf — der Fehlerzweig lädt jetzt die Liste neu, die Person verschwindet.
  - `[low]` `[patch]` (Blind) Doku verspricht „Gelöschte Person“ bei gelöschten Teilnehmenden, die Zeile verschwindet aber — Wortlaut in `beitraege.md` und `daten.md` korrigiert.
  - `[low]` `[patch]` (Blind) Fehlendes „und“ in `mitarbeitende/index.md` — ergänzt.
  - `[low]` `[patch]` (Blind) Kaputter Satz in `freizeit-abschliessen.md` — neu formuliert. Die Rückfrage beim Abschluss nennt offene Beiträge nicht (Freizeiten liest Finanzen nicht, AD-2); keine Ausnahme nach AD-17.
  - `[low]` `[reject]` (Blind) Entfernen des Hakens ohne Bestätigung und ohne Rückgängig — EXPERIENCE: Abhaken ist ein Tipp; ein erneuter Tipp setzt den Haken wieder (mit neuem „abgehakt von“).
  - `[low]` `[reject]` (Blind) Auskunft nennt die Freizeit nicht — wie in 5-2: `IFreizeitNamenLesen` läuft unter dem Mandantenfilter, die Auskunft geht über alle Organisationen.
  - `[false]` `[reject]` (Blind) Designer-Datei und Modell-Snapshot fehlen — sie sind vorhanden und wurden nur aus der Review-Diff-Datei ausgeschlossen; der Build ist sauber.
  - `[low]` `[reject]` (Blind) Roh-SQL mit festem Schema- und Tabellennamen — jede Umbenennung fällt sofort in den API-Tests auf; der Platzhalter ist per Test abgesichert.
  - `[low]` `[reject]` (Blind) Löschlauf ohne passenden Index auf `FreizeitId` — wenige Zeilen je Organisation, ein Index bräuchte eine weitere Migration.
  - `[false]` `[reject]` (Blind) Datum um Mitternacht falsch — `Anzeige.Datum(DateTimeOffset)` und `AuskunftFormat.Datum` rechnen nach Europe/Berlin um.
  - `[low]` `[reject]` (Blind) Fehler beim Laden der Namen bleibt still („Ohne Namen“) — gleiches Muster wie Belege (5-2); der Fix bräuchte einen neuen Zustand.
  - `[low]` `[reject]` (Edge) Race wie der erste Blind-Befund (bezahlt gegen offen) — gleiche Begründung.
  - `[low]` `[reject]` (Edge) Absage zwischen Prüfung und INSERT legt einen Vermerk für eine abgesagte Person an — der Vermerk bleibt unsichtbar und ist aufbewahrungspflichtig wie bei einer späteren Absage; der Fix bräuchte modulübergreifende Sperren.
  - `[low]` `[patch]` (Edge) 404 beim Tipp mit „Bitte noch einmal tippen“ — gleiche Ursache wie Blind, mit Neuladen behoben.
  - `[low]` `[patch]` (Edge) Nach Erfolg und gescheitertem Nachladen alter Stand — gleiche Ursache wie Blind, siehe dort.
  - `[false]` `[reject]` (Edge) Nach 409 bleibt der Kontext womöglich nicht „gelaufen“ — Pipeline und Kontext-Endpunkt nutzen dieselbe Phasenberechnung (AD-17, AD-29); nach 409 liefert der Kontext „gelaufen“.
  - `[false]` `[reject]` (Edge, Claim) `DatenauskunftTests` nicht wie im Spec erweitert — `BeitraegeTests.Auskunft_eigener_Beitrag_und_eigene_Haken_fuer_andere_ohne_deren_Namen` prüft den Abschnitt „Beiträge“ schon über `GET /api/ich/daten`; der allgemeine Test zeigt nur nicht leere Abschnitte.
  - `[low]` `[patch]` (Verification) Sortierung nach Namen nie geprüft — bUnit-Fall mit unsortierter Antwort ergänzt.
  - `[low]` `[patch]` (Verification) Fehlerzweig ohne 409 und gescheitertes Nachladen ungetestet — zwei bUnit-Fälle ergänzt.
  - `[low]` `[patch]` (Verification, Other) Zeile aus der POST-Antwort ungenutzt — gleiche Ursache wie Blind „alter Stand“.
  - `[low]` `[patch]` (Verification, Other) Fehlendes „und“ — gleiche Ursache wie Blind, behoben.
  - `[false]` `[reject]` (Intent) Kein 403-Test — `Mitarbeit` umfasst beide Freizeitrollen, eine falsche Rolle innerhalb der Freizeit gibt es nicht; Org-Admin ohne Rolle gibt 404 (AD-30, wie 5-2).
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — es gibt keine Token-Route.
  - `[false]` `[reject]` (Intent) Test zuerst nicht nachweisbar — Prozessanforderung, die Tests existieren für jede Matrixzeile.
  - `[false]` `[reject]` (Intent) Kein Playwright-Test für das echte Abhaken als Team — AD-30 verlangt Playwright nur für Kernabläufe und Screenshots; HTTP- und bUnit-Ebene decken das ab.
  - `[false]` `[reject]` (Intent) Doku im Ordner `finanzen/` statt unter einer Rolle — Muster aus 5-1/5-2, `mitarbeitende/index.md` verlinkt dorthin.
  - `[false]` `[reject]` (Intent) Kein Nachweis für Doku-Build und Screenshots im Diff — Build und Tests laufen am Ende und stehen unter Auto Run Result.
  - `[false]` `[reject]` (Intent) Fester Datumstext im Screenshot — Absicht, damit das Bild nicht mit dem Datum der Demo-Daten rauscht.

## Design Notes

- **Übergang statt Autosave:** AD-19 sagt, Statusfelder sind keine Autosave-Felder. „Bezahlt“ ist ein Status ohne Version, und vor dem ersten Haken gibt es keine Zeile. Zwei POST-Übergänge wie bei Aufgaben („erledigt“/„offen“) sind einfach und wiederholbar.
- **Erster Vermerk zählt:** Ein erneuter Haken überschreibt „abgehakt von“ nicht. So bleibt sichtbar, wer zuerst bestätigt hat, und gleichzeitige Tipps sind unschädlich.
- **Offen löscht:** Ein Vermerk „nicht bezahlt“ trägt keine Information. Das endgültige Löschen ist kein Schein-Löschen (AD-16).
- **Wer zählt:** Nur bestätigte Teilnehmende, nicht eingegangene und nicht Mitarbeitende. Vermerke abgesagter Personen bleiben (aufbewahrungspflichtig), stehen aber nicht in der Liste. Rückzahlungen sind kein Teil der MVP.
- **Schlüssel PersonId:** AD-7 verlangt die PersonId außerhalb von Freizeiten; die Kennung der Anmeldung bleibt dort. Die neue Leseschnittstelle liefert nur Kennungen. Namen setzt der Client aus der Personenliste zusammen (AD-2).
- **Nach dem Abschluss gesperrt:** AD-17 nennt als Ausnahmen nur Erstattungsvermerke und Anschaffungswünsche. Darum ist der Beitrag-Vermerk nach „gelaufen“ nur lesbar.
- **Demo ohne neue Anmeldungen:** Eine weitere bestätigte Teilnehmende würde „1 von 40“ und viele Screenshots älterer Stories ändern. Die Demo hakt nur Mia ab.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Finanzen.Tests` und `dotnet test --project tests/Zeltlotse.Freizeiten.Tests` -- expected: grün
- `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*BeitraegeTests*"` (dazu `*EndpunktListeTests*`, `*DatenauskunftTests*`, `*DemoDatenTests*`) -- expected: grün
- `dotnet test --project tests/Zeltlotse.Ui.Tests` und `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- expected: grün
- einmal am Ende: alle Testprojekte einzeln, darunter `dotnet test --project tests/Zeltlotse.E2E.Tests` (inklusive Kategorie Doku) -- expected: grün, Screenshots erzeugt
- einmal am Ende: `npm run build` in `doku/` -- expected: fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Das Modul Finanzen hat einen fünften Reiter „Beiträge“. Er listet jede bestätigte teilnehmende Person der Freizeit mit einem Haken „bezahlt“. Ein Tipp setzt den Haken (`POST …/beitraege/{personId}/bezahlt`) oder entfernt ihn (`…/offen`). Gespeichert wird ein Vermerk mit der teilnehmenden Person, wer abgehakt hat und wann. Der erste Vermerk zählt, auch bei zwei gleichzeitigen Tipps (`INSERT … ON CONFLICT DO NOTHING` auf einen eindeutigen Index ohne Platzhalter). „Offen“ löscht den Vermerk. Ein Balken zeigt „x von y bezahlt“. Nach „gelaufen“ ist alles nur lesbar (409 `freizeit-abgeschlossen`, kein `[ErlaubtNachAbschluss]`). Wer als Teilnehmende zählt, liefert die neue Leseschnittstelle `IFreizeitTeilnehmendeLesen` von Freizeiten. Datenschutz nach AD-16: Platzhalter bei `PersonGeloescht` für beide Personenspalten, Abschnitt „Beiträge“ in der Datenauskunft, Löschlauf nach Org-Frist.

**Geänderte Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Finanzen/Beitraege.cs`: DTOs `Beitraege`, `BeitragZeile`.
- `src/Modules/Freizeiten/…/Contracts/Personen/IFreizeitTeilnehmendeLesen.cs`, `…/Rechte/FreizeitPersonenLesen.cs`, `FreizeitenModul.cs`: neue Leseschnittstelle samt Umsetzung.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Beitraege/*`: Entität `BeitragVermerk`, Slices Laden und Übergang.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/{FinanzenModul.cs,Daten/FinanzenDbContext.cs,Datenschutz/*}`, Migration `Beitraege`: Routen, Tabelle `finanzen.beitrag_vermerk`, Auskunft, Empfänger, Löschlauf.
- `src/Zeltlotse.App/Features/Finanzen/{Beitraege.razor,FinanzenReiter.razor,FinanzenTexte.cs}`, `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs`: Seite, Reiter, Texte, Client.
- `src/Zeltlotse.Api/Demo/*`: Schritt `BeitraegeAsync` (Lea hakt Mia ab).
- Tests: neu `Api.Tests/BeitraegeTests.cs` (alle Matrixzeilen, 404/401 auf drei Routen, gleichzeitige Haken) und `Finanzen.Tests/BeitraegeTests.cs`; erweitert `Ui.Tests/FinanzenSeitenTests.cs`, `EndpunktListeTests`, `DemoDatenTests`, `DokuRegeln`/`DokuTests`, `DokuScreenshotTests` (Abschnitt 5-3).
- Doku: neu `doku/src/content/docs/finanzen/beitraege.md`; „Noch nicht dabei“-Stellen, `daten.md`, `mitarbeitende/index.md`, `leitung/freizeit-abschliessen.md`, `leitung/module-schalten.md` angepasst. Screenshots `beitraege-{handy,laptop}.png` neu; `kalkulation-*`, `beitragsrechner-*`, `belege-handy`, `soll-ist-*` wegen des fünften Reiters und `meine-daten-auskunft-*` wegen des neuen Abschnitts neu.

**Review:** 31 Befunde: 20 low, 11 false, kein medium oder high. Einzelheiten im Review Triage Log.
- Patches (alle low): Fehlerzweig lädt die Liste neu, Banner wird beim nächsten Tipp geleert, drei bUnit-Tests (Sortierung, 404 beim Speichern, gescheitertes Nachladen), Doku-Wortlaut (gelöschte Teilnehmende, fehlendes „und“, Satz zum Abschluss).
- Nichts zurückgestellt.
- Verworfen mit Begründung: Race „bezahlt“ gegen „offen“ (2×), Absage zwischen Prüfung und Schreiben, kein Rückgängig beim Entfernen, Freizeit in der Auskunft, Roh-SQL-Namen, Index für den Löschlauf, stilles „Ohne Namen“. Als false: Designer-Datei fehlt, Datum um Mitternacht, Kontext nach 409, DatenauskunftTests (schon abgedeckt), kein 403, keine Token-Route, Test zuerst, Playwright für das Abhaken, Doku-Ordner, Build-Nachweis, festes Datum im Screenshot.

**Follow-up review recommended:** false. Gepatcht wurden nur Einträge low (high 0, medium 0).

**Verifikation (ein Komplettlauf nach den Patches):**
- `dotnet build Zeltlotse.slnx`: 0 Warnungen, 0 Fehler.
- Alle Testprojekte einzeln grün: Api 979, E2E inklusive Doku-Screenshots 187, Ui 792, Architektur 135, Kueche 119, Personen 157, SharedKernel 111, Freizeiten 93, Programm 91, Finanzen 65, Organisationen 58, Sicherheit 36, Inventar 34, Konten 26, Ablaeufe 10. (`Zeltlotse.Testing` und `Zeltlotse.Testmodul` sind Hilfsprojekte ohne Tests.)
- `npm run build` in `doku/`: fehlerfrei, 66 Seiten, nur der bekannte Sitemap-Hinweis.
- `beitraege-laptop.png` angesehen. Screenshots, die sich nur durch Rendering-Rauschen änderten, auf HEAD zurückgesetzt.

**Restrisiken:**
- Tippen zwei Personen fast gleichzeitig „bezahlt“ und „offen“ für dieselbe Person, kann eine Anfrage mit 500 enden; ein erneuter Tipp heilt das.
- Wird eine Anmeldung zwischen Prüfung und Speichern abgesagt, bleibt ein unsichtbarer Vermerk gespeichert (wie bei einer späteren Absage).
- Die Rückfrage beim Abschluss nennt offene Beiträge nicht, denn Freizeiten liest Finanzen nicht (AD-2). Nach dem Abschluss lässt sich kein Beitrag mehr abhaken.
- Am Handy brechen die fünf Reiter in zwei Zeilen um.
