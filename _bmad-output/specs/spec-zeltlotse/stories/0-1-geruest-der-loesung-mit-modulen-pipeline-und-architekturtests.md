---
title: 'Gerüst der Lösung mit Modulen, Pipeline und Architekturtests'
type: 'chore'
created: '2026-10-11'
status: 'done'
baseline_revision: '5295fec4a76de29daa8143dd7a8d059bfe393d06'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
warnings: [oversized]
deferred:
  - summary: >-
      Pipeline-Reihenfolge des API-Hosts ist nur über eine Kopie in TestUmgebung geprüft.
    evidence: |-
      Program.cs und TestUmgebung.cs listen die Stufen getrennt; kein Test sendet einen Command über den Host. Mit dem ersten Host-Command (Story 1-1) per HTTP prüfen oder eine gemeinsame Stufenliste nutzen.
    location: >-
      src/Zeltlotse.Api/Program.cs
    severity: medium
  - summary: >-
      Mehrere Architekturregeln haben noch keine Negativ-Fixtures und laufen leer.
    evidence: |-
      ContractsNurEntlangDerPfeile, ApiContractsNurEigenerOrdner, HandlerOhnePublishUndOutbox, IModulCommand mit eigenem DbContext und die Ereignis-Fluss-Tests finden in 0-1 keine Typen. Mit den ersten echten Contracts-Typen, Handlern und Empfängern Negativ-Fixtures ergänzen.
    location: >-
      tests/Zeltlotse.ArchitectureTests/AbhaengigkeitsRegeln.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Das Repository enthält noch keinen Code. Alle folgenden Stories brauchen dieselbe Grundlage: Lösung, zehn Module mit eigenem Schema, gemeinsamen Kern, Command-Pipeline mit Outbox/Inbox und Architekturtests, die Regelverstöße beim Bauen sofort melden.

**Approach:** Lösung nach dem Quellbaum der ARCHITECTURE-SPINE.md anlegen (ohne Clients, die kommen mit 0-3). SharedKernel liefert Kennungen, Uhr, Mandanten-Kontext, `Aggregat`, Umschlag, Outbox/Inbox und Pipeline-Stufen. Jedes Modul bekommt Registrierung, Schreib- und Lese-`DbContext`, eigenes Schema und erste Migration. Testgetrieben: Architektur-, Pipeline- und HTTP-Tests gegen echtes PostgreSQL 18 entstehen vor dem Code.

## Boundaries & Constraints

**Always:**
- Genau die zehn Module aus AD-1 (Konten, Personen, Organisationen, Freizeiten, Sicherheit, Programm, Inventar, Küche, Finanzen, Kanäle). Jedes Modul ist ein Paar `Zeltlotse.<Modul>` + `Zeltlotse.<Modul>.Contracts` unter `src/Modules/<Modul>/`. Schemas: `konten`, `personen`, `organisationen`, `freizeiten`, `sicherheit`, `programm`, `inventar`, `kueche`, `finanzen`, `kanaele`.
- Paketversionen ausschließlich zentral in `Directory.Packages.props`, Versionen laut Stack-Tabelle der Spine. `net10.0`, C# 14, Nullable an, Warnungen als Fehler.
- Module referenzieren nur `Mediator.Abstractions`; `Mediator.SourceGenerator` und genau ein `AddMediator` (Scoped) nur im API-Host. Commands, Handler, Validatoren, DbContexts sind `internal`; öffentlich sind nur `Add<Modul>Modul(...)` und `Map<Modul>Endpoints(...)`. `InternalsVisibleTo` für `Zeltlotse.Api` und das eigene Testprojekt.
- Pipeline-Reihenfolge nach AD-27: Validierung (FluentValidation) → Inbox (nur Ereignis-Empfänger) → Transaktion auf dem `TDbContext` aus `IModulCommand<TDbContext>` → Outbox-Stufe sammelt die `Ereignisse` aller geänderten `Aggregat`-Instanzen und schreibt sie im selben `SaveChanges`. Handler öffnen keine Transaktion.
- Umschlag nach AD-4: Ereignis-Kennung, Zeitpunkt, `OrganisationId?`, Datensatz-Kennung, `Folgenummer`. Kennungen per `Guid.CreateVersion7(timeProvider.GetUtcNow())`, Uhr nur über `TimeProvider`.
- Lese-`DbContext` ohne Änderungsverfolgung, `SaveChanges` wirft.
- Organisationseigene Entitäten werden im Basis-`DbContext` global nach dem Mandanten-Kontext gefiltert; fehlt der Kontext, liefert der Filter nichts (AD-6).
- Migrationen je Modul mit eigener Historientabelle im eigenen Schema; der API-Host wendet sie beim Start in fester Reihenfolge an: Konten, Personen, Organisationen, Freizeiten, Inventar, Sicherheit, Programm, Küche, Finanzen, Kanäle.
- Test vor dem Code (AD-30). xUnit v3 auf Microsoft.Testing.Platform, Testcontainers `postgres:18`, ein Container je Testassembly.

**Never:**
- Keine Fachentitäten, fachlichen Endpunkte, Ereignisse oder Empfänger (kommen mit den Fach-Stories).
- Keine Clients (`App`, `Public`, `Ui`, `Ui.Komponenten`), kein Identity, keine Autorisierung, keine Sicherheits-Header, kein Antiforgery, keine Verschlüsselung, kein Datei-Adapter, kein Schema `betrieb` (Stories 0-2, 0-3, 1-x).
- Keine Schreibschutz- und Schalter-Stufe der Pipeline (Stories „Freizeit abschließen“, „Module pro Freizeit schalten“); nur das Merkmal `IBenutzerCommand` existiert.
- Keine Mediator-Notifications, kein `IMediator.Publish`, keine Ablauf-Klassen, keine Fremdschlüssel über Schemagrenzen.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start gegen leere DB | API-Host startet gegen frisches PostgreSQL 18 | alle zehn Schemas mit Outbox-, Inbox- und Historientabelle existieren; `GET /health` → `200` | – |
| Zweiter Start | Migrationen bereits angewendet | Start ohne Fehler, keine Änderung | – |
| Command gültig, erzeugt Ereignis | Aggregat-Methode legt Ereignis ab | Änderung + Outbox-Zeile (Folgenummer +1) in einer Transaktion | – |
| Command ungültig | Validator schlägt fehl | Handler läuft nicht, nichts gespeichert | Validierungsausnahme mit Fehlern je Feld (JSON-Name) |
| Handler wirft | Ausnahme nach Änderung | weder Änderung noch Outbox-Zeile gespeichert | Ausnahme geht weiter |
| Zustellung | Ereignis mit zwei Empfängern | jeder Empfänger läuft in eigenem Scope mit Mandanten-Kontext aus dem Umschlag; Inbox-Eintrag in derselben Transaktion | Fehler bei Empfänger A hält B nicht auf; A wird später erneut versucht |
| Doppelt/älter | dasselbe Ereignis erneut oder kleinere Folgenummer je (Datensatz, Empfänger) | verworfen, Handler läuft nicht | – |
| Reihenfolge | Empfänger scheitert an Ereignis 1 von Datensatz D | Ereignis 2 von D wird diesem Empfänger erst nach Erfolg von 1 zugestellt | – |
| Mandantenfilter | zwei Organisationen, Kontext = Org A / kein Kontext | nur Zeilen von A / keine Zeilen | – |

</intent-contract>

## Code Map

Greenfield: im Repository liegen nur `_bmad/`, `_bmad-output/`, `scripts/`, `CLAUDE.md`, `README.md`. Kein `global.json`, keine `.gitignore`.

- `_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md` -- bindend; Quellbaum (Z. 571–600), Stack (Z. 452–475), Abhängigkeitspfeile (Z. 39–71), Tabelle *Ereignis-Fluss* (Z. 554–569), AD-24-Regelliste (Z. 337–353). Nur lesen.
- `scripts/vorabpruefung.ps1:13` -- erwartet SDK 10.0.401 laut `global.json`. Nur lesen.
- Umgebung geprüft: SDK 10.0.401 installiert, Docker läuft, Node 26 vorhanden.

## Tasks & Acceptance

**Execution:**
- `global.json`, `Zeltlotse.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.config/dotnet-tools.json` (dotnet-ef) -- Grundgerüst; MTP als Test-Runner in `global.json`.
- `tests/Zeltlotse.ArchitectureTests/` -- ZUERST: ArchUnitNET-Regeln (s. Design Notes) gegen alle `Zeltlotse.*`-Assemblies; Ereignis-Fluss wird aus der Spine-Tabelle gelesen.
- `tests/Zeltlotse.Testmodul/` + `tests/Zeltlotse.SharedKernel.Tests/` -- ZUERST: Testmodul mit `internal` Command/Handler/Validator, Aggregat, Empfänger und eigenem Schema; Testprojekt mit eigenem Mediator-Generator deckt die Matrix-Zeilen Command, Validierung, Rollback, Zustellung, Doppelt/älter, Reihenfolge, Mandantenfilter gegen echtes PostgreSQL ab und belegt, dass der Generator `internal`-Handler über `InternalsVisibleTo` findet.
- `tests/Zeltlotse.Testing/` -- gemeinsame Test-Infrastruktur: PostgreSQL-Container-Fixture, `WebApplicationFactory` des API-Hosts mit `FakeTimeProvider` und Aufruf „Outbox jetzt zustellen“ für spätere Durchlauf-Tests.
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: HTTP-Tests Start/Migrationen/`/health` (Matrix-Zeilen 1–2), Migrationsreihenfolge.
- `src/Zeltlotse.SharedKernel/` -- Kennungen, `TimeProvider`-Registrierung, `IMandantenKontext`, `IOrganisationsEigen`, `Aggregat`, `Umschlag`, `IModulCommand<TDbContext>`, `IBenutzerCommand`, `IEreignisEmpfaenger`, Basis-`ModulDbContext`/`ModulLeseDbContext`, Outbox/Inbox-Modell, Pipeline-Stufen, Zustell-Logik, `EmpfaengtEreignis<TEreignis,TCommand>()`.
- `src/Zeltlotse.Api.Contracts/` -- Projekt ohne Projekt- und Paketreferenzen; Ordner je Modul und `Gemeinsam/` entstehen mit den ersten Typen der Fach-Stories.
- `src/Modules/<Modul>/Zeltlotse.<Modul>[.Contracts]/` ×10 -- Registrierung, Schreib-/Lese-`DbContext`, Schema, erste Migration; Contracts mit Namensraum `.Events` vorbereitet.
- `src/Zeltlotse.Api/` -- Host: `AddMediator` mit Pipeline-Stufen, Modul-Registrierung, `/api`-Gruppe mit `Map<Modul>Endpoints()`, Migrationen beim Start, Zustell-Hintergrunddienst, ServiceDefaults.
- `src/Zeltlotse.ServiceDefaults/`, `src/Zeltlotse.AppHost/` -- Aspire 13.6.1: PostgreSQL 18 + API.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` im Repository-Wurzelverzeichnis läuft, then laufen Architektur-, SharedKernel- und API-Tests und sind alle grün.
- Given ein Modul-Projekt referenziert testweise ein fremdes Modul-Projekt (oder `Mediator.SourceGenerator`, oder ein Endpunkt nutzt den schreibenden `DbContext`), when die Architekturtests laufen, then schlagen sie fehl (für diese drei Regeln mit Negativ-Fixture belegt).
- Given der API-Host läuft unter Aspire, when `dotnet build` der Lösung läuft, then baut alles ohne Warnung.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 43 findings — high 3, medium 12, low 17, false 11, maybe-false 0
- findings:
  - `[false]` `[reject]` (intent) AD-30-Pflichtfälle 403/404/Token fehlen — AD-30 bindet die Fälle an die Endpunkt-Art; in 0-1 gibt es keinen Organisations-, Freizeit-, persönlichen oder Token-Endpunkt; der Hinweis ist Vorlagentext für 44 Stories.
  - `[low]` `[patch]` (intent) `/alive` ist gemappt, aber ungetestet — HTTP-Test `GET /alive` → 200 ergänzt.
  - `[false]` `[reject]` (intent) Test zuerst nicht nachprüfbar — Prozessaussage, kein Defekt im Code; jede Matrix-Zeile hat einen laufenden Test.
  - `[medium]` `[patch]` (vgap) Concurrency-Token `Folgenummer` nie geprüft — Test mit zwei Scopes auf dieselbe Notiz ergänzt.
  - `[medium]` `[patch]` (vgap) Quelle der `OrganisationId` im Umschlag ungetestet (Aggregat- und Kontext-Organisation immer gleich) — Fall Aggregat OrgB unter Kontext OrgA ergänzt.
  - `[medium]` `[patch]` (vgap) Registrierung und Schleife des `ZustellDienst` ungetestet — Registrierungstest und Test „nächster Takt läuft nach Fehler“ ergänzt.
  - `[medium]` `[defer]` (vgap) Pipeline-Reihenfolge des Hosts nur über Kopie in `TestUmgebung` geprüft — in 0-1 gibt es keinen Host-Command; Prüfung mit dem ersten Host-Command (1-1), siehe `deferred`.
  - `[low]` `[patch]` (vgap) Wachstum und Obergrenze von `Wartezeit` unbelegt — Unit-Test 10 s / 20 s / 1 h ergänzt.
  - `[medium]` `[patch]` (vgap) Architekturregeln laufen leer ohne Negativ-Fixture — direkte Prüfungen für `IstErlaubterEreignisWert` und `EmpfangeneEreignisse` ergänzt; übrige Regeln siehe `deferred`.
  - `[low]` `[reject]` (vgap) Outbox-Index `(DatensatzId, Folgenummer)` nicht eindeutig — der Concurrency-Token schützt die Invariante; der Fix verlangt neue Migrationen in elf Schemas.
  - `[high]` `[patch]` (edge) `OperationCanceledException` eines Empfängers ohne Shutdown wird nicht als Versuch gezählt — Filter auf `IsCancellationRequested` umgestellt, Test ergänzt.
  - `[high]` `[patch]` (edge) dieselbe Ausnahme beendet den `ZustellDienst` und damit den Host (StopHost) — mit obigem Fix behoben.
  - `[false]` `[reject]` (edge) Giftnachricht wird endlos wiederholt und blockiert Folgeereignisse — gewollt: AD-4 verbietet verlorene Folgeaktionen, die Reihenfolge je Datensatz ist Pflicht, jeder Fehlversuch wird geloggt, die Wartezeit ist gedeckelt.
  - `[medium]` `[patch]` (edge) Fehler in einem Modul überspringt alle späteren Module im Durchgang — Fehler je Modul abgefangen und geloggt.
  - `[false]` `[reject]` (edge) zwei gleichzeitige Zustellläufe — ein Prozess (AD-1, AD-23) mit genau einem `ZustellDienst`, in Tests entfernt; der Inbox-Primärschlüssel verhindert doppelte Verarbeitung.
  - `[false]` `[reject]` (edge) Inbox-Prüfung außerhalb der Transaktion lässt den Handler doppelt laufen — der zweite Lauf scheitert am Primärschlüssel und rollt zurück; die Stufenfolge gibt AD-27 vor.
  - `[low]` `[reject]` (edge) Aggregat ohne `Id` — fällt beim ersten Test eines Aggregats auf; der Fix wäre ein zusätzlicher Wächter.
  - `[low]` `[reject]` (edge) Typname über 300 Zeichen — bei den Namenskonventionen praktisch ausgeschlossen; der Fix wäre eine zusätzliche Prüfung.
  - `[low]` `[reject]` (edge) Empfänger doppelt angemeldet — nur bei doppeltem Modul-Aufruf; der Fix wäre ein zusätzlicher Wächter.
  - `[low]` `[reject]` (edge) `ModulBeschreibung` doppelt — nur bei doppeltem Modul-Aufruf; der Fix wäre ein zusätzlicher Wächter.
  - `[false]` `[reject]` (edge) Lost Update bei Änderungen ohne Ereignis — die Versionsprüfung bearbeitbarer Entitäten ist `xmin` nach AD-19 und kommt mit diesen Entitäten; `Folgenummer` sichert nur die Ereignisfolge.
  - `[low]` `[patch]` (edge) Ereignisse werden vor erfolgreichem Speichern geleert — Leeren nach `SaveChangesAsync` verschoben.
  - `[low]` `[reject]` (edge) `TypeInitializationException` bei falscher Command-Signatur — die Architekturregel erzwingt `IModulCommand<>`; der Fix wäre eine zusätzliche Verzweigung.
  - `[low]` `[reject]` (edge) `PropertyName` null in `JsonName` — nur bei handgebauten Fehlern ohne Feld; der Fix wäre ein zusätzlicher Wächter.
  - `[medium]` `[patch]` (edge) Lese-Kontext schreibt über `ExecuteUpdate/Delete/SqlRaw` — Interceptor lässt nur SELECT/WITH zu, Test ergänzt.
  - `[low]` `[reject]` (edge) doppelte Ereignis-Zeile in der Spine-Tabelle — die Tabelle ist eindeutig gepflegt; der Fix wäre Zusatzlogik.
  - `[low]` `[reject]` (edge) Abschnitt ohne Tabelle liest eine fremde Tabelle — die Spine-Struktur ist fest; der Fix wäre Zusatzlogik.
  - `[low]` `[patch]` (edge) csproj mit xmlns wird nicht erkannt — auf `Name.LocalName` umgestellt.
  - `[medium]` `[patch]` (edge) Lambda in `Map<Modul>Endpoints` darf den schreibenden DbContext nutzen (Ausnahme über `AeussererTyp`) — Ausnahme nur noch für den Registrierungstyp selbst, Negativ-Fixture ergänzt.
  - `[high]` `[patch]` (blind) Abbruch-Ausnahme eines Empfängers stoppt die Zustellung — gleiche Ursache wie edge, mitbehoben.
  - `[medium]` `[patch]` (blind) Datenbankfehler eines Moduls blockiert die anderen — gleiche Ursache wie edge, mitbehoben.
  - `[false]` `[reject]` (blind) Giftnachricht ohne Aufgeben — wie edge: Wiederholen ist gewollt.
  - `[low]` `[reject]` (blind) Outbox wird nie aufgeräumt — beim bekannten Kreis vernachlässigbar; Aufräumen gehört zum Lösch-Dienst (AD-16); der Fix wäre ein neuer Job.
  - `[false]` `[reject]` (blind) nicht sicher mit mehreren Instanzen — ein Prozess laut AD-1/AD-23.
  - `[medium]` `[patch]` (blind) Lese-Kontext kann schreiben — gleiche Ursache wie edge, mitbehoben.
  - `[low]` `[reject]` (blind) `/health` prüft die Datenbank nicht — Überwachung ist in der Spine zurückgestellt; der Fix bringt ein neues Paket.
  - `[false]` `[reject]` (blind) Modul fehlt still in `options.Assemblies` — Mediator wirft beim ersten Senden „kein Handler“, der erste Command-Test fällt sofort auf.
  - `[medium]` `[patch]` (blind) Architekturregeln ohne Negativ-Fixture — gleiche Ursache wie vgap, mitbehoben bzw. `deferred`.
  - `[medium]` `[patch]` (blind) Concurrency-Token ungetestet — gleiche Ursache wie vgap, mitbehoben.
  - `[low]` `[patch]` (blind) PostgreSQL-Tag in Tests schwimmt (`18` statt `18.6`) — auf `postgres:18.6` festgelegt.
  - `[false]` `[reject]` (blind) Story-Datei ohne Ergebnis des Generator-Tests — der Fix wäre eine Spec-Bearbeitung; das Ergebnis steht im Auto Run Result.
  - `[false]` `[reject]` (blind) `JsonName` rät JSON-Namen — Commands mit `[JsonPropertyName]` gibt es noch nicht; die HTTP-Abbildung kommt mit 1-1.
  - `[low]` `[reject]` (blind) `MandantenKontext.Setzen` mehrfach aufrufbar — nur Endpunkt-Rand und Zustellung kennen den konkreten Typ; der Fix wäre ein Wächter ohne nachgewiesenen Missbrauch.

## Design Notes

- **Kein fachlicher Endpunkt in 0-1:** Die AD-30-Pflichtfälle (falsche Rolle 403, fremde Organisation 404, Token-Fälle) entfallen, weil es noch keine Endpunkte dieser Arten gibt. Einziger HTTP-Endpunkt ist `/health` der ServiceDefaults; die Test-Infrastruktur steht für die Pflichtfälle ab 1-1 bereit.
- **Generator-Test (AD-27):** Pipeline-Stufen liegen in SharedKernel, damit ein Testprojekt mit eigenem Generator sie samt `Zeltlotse.Testmodul` echt durchlaufen kann. Findet der Generator `internal`-Handler nicht, gilt der Plan B aus AD-27 (`public` in `*.Intern` + Architekturtest) und wird hier festgehalten.
- **Architekturregeln ab 0-1:** Modulreferenzen nur auf Contracts; Contracts-Aufrufe nur entlang der Pfeile (Events ausgenommen); Event-Typen nur aus `Guid`, Zahlen, Datumswerten, Enums und Typen desselben `.Events`-Namensraums; nur Command-Handler hängen vom schreibenden `DbContext` ab; kein `Mediator.SourceGenerator` in Modulen, kein `IMediator.Publish`/direkter Outbox-Aufruf in Handlern; Commands tragen `IModulCommand<eigener DbContext>`; Commands/Handler/Validatoren `internal`; `Api.Contracts` ohne Referenzen, Module nutzen dort nur eigenen Ordner und `Gemeinsam`; jedes `DbContext`-Modell nur im eigenen Schema; Ereignis-Fluss: jedes Paar (Ereignis, Zielmodul) im Code steht in der Tabelle und jeder `.Events`-Typ steht dort (Tabellenzeilen ohne Code sind erlaubt, bis ihre Story kommt; `PersonGeloescht` gilt für jedes Modul). Regeln zu Clients, Antiforgery, `[Besonders]` und AD-26-Endpunkten kommen mit 0-2, 0-3 bzw. der ersten passenden Story.
- **Zustellung:** einfach halten — periodischer Hintergrunddienst, Wiederholung mit wachsender Wartezeit, Zustellstand je (Ereignis, Empfänger); kein Nachrichtenbroker.
- **Testprojekte:** zusätzlich zur Spine-Liste `Zeltlotse.Testing` (gemeinsame Fixture), `Zeltlotse.Api.Tests` (Host) und `Zeltlotse.Testmodul` (nur Test). Modul-Testprojekte entstehen mit der ersten Fach-Story des Moduls; `Ablaeufe.Tests` mit dem ersten Ablauf.
- **Validierungsfehler als HTTP 400** (ProblemDetails) wird mit dem ersten schreibenden Endpunkt (1-1) verdrahtet; in 0-1 nur die Ausnahme mit Fehlern je JSON-Feldname.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, inklusive Architekturtests

## Auto Run Result

Status: done

**Zusammenfassung:** Gerüst der Lösung nach ARCHITECTURE-SPINE.md: `Zeltlotse.slnx`, zentrale Paketversionen, Aspire (AppHost mit PostgreSQL 18.6), API-Host mit genau einem `AddMediator`, zehn Modulpaare mit Schreib-/Lese-`DbContext`, eigenem Schema und erster Migration, SharedKernel mit Kennungen, Uhr, Mandanten-Kontext und -Filter, `Aggregat`, Umschlag, Outbox/Inbox, Pipeline-Stufen (Validierung → Inbox → Transaktion → Outbox) und Zustell-Hintergrunddienst. Der Mediator-Generator findet `internal`-Handler über `InternalsVisibleTo` (Test `Generator_findet_interne_Handler_ueber_InternalsVisibleTo`), Plan B aus AD-27 entfällt.

**Dateien:**
- `global.json`, `Zeltlotse.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`, `.config/dotnet-tools.json` – Grundgerüst, MTP als Test-Runner.
- `src/Zeltlotse.SharedKernel/` – Kern: Kennung, Mandanten, Aggregat, Umschlag, Commands, Daten (inkl. `NurLesenInterceptor`), Outbox, Pipeline.
- `src/Modules/<Modul>/…` ×10 – Registrierung, DbContexts, Design-Time-Factory, Migration `Initial`, Contracts mit `.Events`.
- `src/Zeltlotse.Api/` – Host, Modul-Registrierung, `ModulMigrationen` (feste Reihenfolge), Zustelldienst.
- `src/Zeltlotse.Api.Contracts/` – leeres Projekt ohne Referenzen.
- `src/Zeltlotse.ServiceDefaults/`, `src/Zeltlotse.AppHost/` – Aspire 13.6.1.
- `tests/Zeltlotse.Testing/` – Container-Fixture (`postgres:18.6`), `ZeltlotseApiFactory` mit `FakeTimeProvider` und `OutboxJetztZustellenAsync`.
- `tests/Zeltlotse.Testmodul/` – nur für Tests: internes Modul mit Commands, Empfängern, Schema.
- `tests/Zeltlotse.SharedKernel.Tests/`, `tests/Zeltlotse.Api.Tests/`, `tests/Zeltlotse.ArchitectureTests/` (mit `Negativ/`-Fixtures).

**Review:** 43 Befunde. 13 Patches (3 high-Zeilen einer Ursache: Abbruch-Ausnahme im Zustelldienst; dazu 7 medium-, 5 low-Einträge), 2 zurückgestellt (`deferred`), alle übrigen mit Begründung im Review Triage Log verworfen.

**Folge-Review empfohlen:** ja – ein high-Eintrag wurde gepatcht. Offenes Risiko: die neue Ausnahmeregel `IstReineModulRegistrierung` (nur `Add*`-Mitglieder dürfen den schreibenden DbContext kennen) und der `NurLesenInterceptor` (nur SELECT/WITH) sind Heuristiken, die mit den ersten echten Endpunkten und Abfragen in 1-1 zu bestätigen sind.

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` → 0 Warnungen, 0 Fehler. `dotnet test` → 95 von 95 grün (Architektur, SharedKernel, API; echtes PostgreSQL per Testcontainers). Jede Zeile der I/O-Matrix hat einen laufenden Test. Aspire-Start lokal vom Umsetzer geprüft (`/health` „Healthy“).

**Restrisiken:** `dotnet ef migrations add` braucht einen Verbindungs-String als App-Argument und zwei Handgriffe (Snapshot nach `Migrationen/` verschieben, Klassen `internal` setzen). Ereignis-Typen erlauben bisher keine Listen und kein `bool`. `/health` prüft die Datenbank nicht (Überwachung in der Spine zurückgestellt). Keine UI → keine Doku-Seite nötig.
