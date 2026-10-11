---
title: 'Gerüst der Lösung mit Modulen, Pipeline und Architekturtests'
type: 'chore'
created: '2026-10-11'
status: 'done'
baseline_revision: '5295fec4a76de29daa8143dd7a8d059bfe393d06'
review_loop_iteration: 0
followup_review_recommended: false
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

### 2026-10-11 — Review pass (Folge-Review)
- verdicts: 41 findings — high 0, medium 9, low 18, false 14, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind) Nur-Lesen-Prüfung des Lese-Kontexts ist eine Präfix-Heuristik: `TagWith`-Abfragen werden abgelehnt, beschreibbare CTEs kommen durch — `NurLesenInterceptor` entfernt; die Lese-Verbindung setzt jetzt `default_transaction_read_only=on`, PostgreSQL selbst lehnt jedes Schreiben ab. Tests für `TagWith`, beschreibbare CTE und `ExecuteDelete` ergänzt bzw. angepasst.
  - `[low]` `[patch]` (blind) `ModulMigrationen.Ordnen`: Abbruch bei unbekanntem Modul ungetestet; fehlendes Modul werde still übersprungen — das Fehlen fängt `StartTests` (alle zehn Schemas über den echten Host); Test „Modul ohne Platz in der Reihenfolge bricht den Start ab“ ergänzt.
  - `[false]` `[reject]` (blind) Handler können über `ISender` fremde Module synchron aufrufen — Commands sind `internal`, und Modulprojekte dürfen keine fremden Modulprojekte referenzieren (Architekturtest); ein fremder Command ist nicht erreichbar.
  - `[false]` `[reject]` (blind) `IRequest`/`IQuery` umgehen die Command-Regeln — ein solcher Handler mit schreibendem DbContext verletzt die Regel „nur Command-Handler hängen vom schreibenden DbContext ab“ und fällt auf.
  - `[low]` `[reject]` (blind) Vergessene Empfänger-Registrierung, `IBenutzerCommand` an Empfängern und Empfänger mit Antworttyp sind nicht durch Regeln abgedeckt — fällt beim ersten Zustelltest jedes Empfängers auf; der Fix wären drei neue Architekturregeln.
  - `[low]` `[reject]` (blind) Outbox-, Zustell- und Inbox-Zeilen speichern `FullName` der Typen; Umbenennen blockiert offene Zeilen — Umbenennungen bestehender Ereignis-Typen sind selten, der Fehler wird bei jedem Versuch geloggt; der Fix wäre ein eigener Namensmechanismus.
  - `[medium]` `[reject]` (blind) Mandantenfilter schützt nur Lesen, nicht Schreiben — die Intent schließt Autorisierung in 0-1 ausdrücklich aus; Schreibrechte je Organisation kommen mit den Endpunkten (1-x).
  - `[medium]` `[patch]` (blind) Zustellung zwischen zwei Modulen und der Fehlerfang je Modul sind ungetestet — gleiche Ursache wie vgap „Fehlerfang je Modul“, mitbehoben.
  - `[low]` `[reject]` (blind) Ereignis-Typen in Unter-Namensräumen, nicht öffentliche Typen und als Eigenschaft wiederverwendete Ereignisse entgehen Wert- und Tabellenprüfung — noch gibt es keinen Ereignis-Typ; die Regeln bekommen mit den ersten Ereignissen Negativ-Fixtures (siehe `deferred`); der Fix wäre Zusatzlogik.
  - `[low]` `[patch]` (blind) `Tabelle_wird_aus_der_Spine_gelesen` prüft genau 10 Zeilen und bricht bei jeder neuen Spine-Zeile — auf „mindestens 10“ umgestellt.
  - `[low]` `[reject]` (blind) Generator-Regel liest nur `src/**/*.csproj`, nicht `Directory.*.props`/`.targets` — ein globaler Generator-Verweis wäre eine bewusste, sichtbare Änderung an einer zentralen Datei; der Fix erweitert die Regel um neue Dateiarten.
  - `[low]` `[reject]` (blind) Design-Zeit-Fabrik mit festem `localhost` widerspricht dem Restrisiko-Text; Handgriffe nur in der Story dokumentiert — der Fix wäre eine Spec-Bearbeitung; die Handgriffe erreichen spätere Stories über den Story-übergreifenden Kontext.
  - `[low]` `[patch]` (blind) `ValidierungsStufe.JsonName` für verschachtelte Pfade ungetestet — gleiche Ursache wie vgap, mitbehoben.
  - `[low]` `[reject]` (blind) `Down()` der Initial-Migrationen lässt das Schema stehen — Zurückrollen auf null ist im Betrieb nicht vorgesehen; der Fix wären Handänderungen an elf generierten Migrationen.
  - `[false]` `[reject]` (edge) Giftnachricht wird stündlich endlos wiederholt — carried: gewollt (AD-4, Reihenfolge je Datensatz, Wartezeit gedeckelt).
  - `[low]` `[reject]` (edge) `AllesZustellenAsync` endet, wenn ein ganzer Stapel von 100 scheitert — nur bei 100 gleichzeitig fälligen Fehlschlägen; der nächste Takt des `ZustellDienst` setzt fort; der Fix bräuchte einen zweiten Zähler durch `ZustellenAsync`.
  - `[false]` `[reject]` (edge) zwei gleichzeitige Zustellläufe — carried: ein Prozess (AD-1, AD-23), Inbox-Primärschlüssel verhindert Doppelverarbeitung.
  - `[false]` `[reject]` (edge) Inbox-Prüfung vor der Transaktion ohne Sperre — carried: zweiter Lauf scheitert am Primärschlüssel und rollt zurück; Stufenfolge laut AD-27.
  - `[low]` `[reject]` (edge) `EmpfaengtEreignis` doppelt registriert — carried: nur bei doppeltem Aufruf; der Fix wäre ein Wächter.
  - `[medium]` `[patch]` (edge) `TagWith` bzw. führende Klammer wird im Lese-Kontext abgelehnt — gleiche Ursache wie blind „Präfix-Heuristik“, mitbehoben.
  - `[medium]` `[patch]` (edge) beschreibbare CTE / `SELECT … INTO` im Lese-Kontext — gleiche Ursache, mitbehoben (Datenbank lehnt mit `25006` ab).
  - `[low]` `[reject]` (edge) Ereignisse eines nicht angehängten Aggregats gehen verloren — dann wird auch die Änderung selbst nicht gespeichert, das fällt im ersten Test des Handlers auf; der Fix wäre ein Wächter.
  - `[low]` `[reject]` (edge) `TypeInitializationException` bei falscher Command-Signatur — carried: Architekturregel erzwingt `IModulCommand<>`.
  - `[low]` `[reject]` (edge) Typname über 300 Zeichen — carried: bei den Namenskonventionen praktisch ausgeschlossen.
  - `[false]` `[reject]` (edge) Modul fehlt in der Registrierung oder ist doppelt — Fehlen fängt `StartTests` (alle zehn Schemas über den Host); doppelt ist carried („ModulBeschreibung doppelt“).
  - `[low]` `[reject]` (edge) Ereignis, das zugleich Eigenschaft eines anderen ist, entgeht der Tabellenprüfung — wie blind „Unter-Namensräume“: noch keine Ereignisse, Fix wäre Zusatzlogik.
  - `[medium]` `[patch]` (vgap) Fehlerfang je Modul in `ZustellenAsync` ohne Test — `TestUmgebung` nimmt optional weitere Registrierungen; Test mit kaputtem Modul vor dem Testmodul ergänzt, beide Empfänger werden trotzdem beliefert.
  - `[low]` `[reject]` (vgap) Rückfall auf den Mandanten-Kontext bei nicht organisationseigenen Aggregaten ungetestet — einzeilige Verzweigung; der Test bräuchte ein neues Aggregat mit Migration im Testmodul; mit dem ersten solchen Fach-Aggregat prüfen.
  - `[low]` `[patch]` (vgap) Abbruch für Module ohne Platz in der Migrationsreihenfolge ungetestet — Test ergänzt (gleiche Ursache wie blind `Ordnen`).
  - `[low]` `[patch]` (vgap) `JsonName` für verschachtelte Pfade ungetestet — Theorie mit `Text`, `Adresse.Strasse`, `Posten[0].Name` ergänzt.
  - `[medium]` `[patch]` (vgap) `TagWith`-Abfragen im Lese-Kontext blockiert — gleiche Ursache wie blind, mitbehoben.
  - `[medium]` `[patch]` (vgap) beschreibbare CTE kommt durch die Nur-Lesen-Prüfung — gleiche Ursache wie blind, mitbehoben.
  - `[medium]` `[defer]` (intent) Pipeline-Reihenfolge des Hosts nur über Kopie in `TestUmgebung` geprüft — carried: bereits in `deferred`.
  - `[false]` `[reject]` (intent) „Genau ein `AddMediator`“ nicht per Architekturtest erzwungen — `AddMediator` erzeugt der Generator, und den darf laut Architekturtest nur `Zeltlotse.Api` einbinden; Module können es nicht aufrufen.
  - `[false]` `[reject]` (intent) Generator-Regel nimmt Testprojekte aus — die Intent verlangt den Generator-Test; „nur im API-Host“ betrifft den Produktionscode, `tests/` liefert nichts aus.
  - `[false]` `[reject]` (intent) AD-30-Pflichtfälle 403/404/Token fehlen — carried: in 0-1 gibt es keinen Endpunkt dieser Arten; die Intent schließt Autorisierung aus.
  - `[false]` `[reject]` (intent) `postgres:18.6` statt `postgres:18` — 18.6 ist PostgreSQL 18, bewusst festgelegt (Review-Pass 1).
  - `[false]` `[reject]` (intent) fachähnliche `Notiz`-Typen — liegen nur unter `tests/`; die Never-Liste betrifft die Module unter `src/`.
  - `[false]` `[reject]` (intent) Zusätze über die Intent hinaus (Concurrency-Token, Zustelltabelle, Nur-Lesen-Schutz) — stützen die Matrix-Zeilen Zustellung und Lese-Kontext; kein Defekt.
  - `[false]` `[reject]` (intent) Inbox-Prüfung außerhalb der Transaktion — carried: Stufenfolge laut AD-27, Primärschlüssel schützt.
  - `[false]` `[reject]` (intent) „Test zuerst“ am Diff nicht nachprüfbar — carried: Prozessaussage, kein Defekt.

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

### Folge-Review 2026-10-11

**Änderung:** Der Lese-Kontext ist jetzt durch die Datenbank schreibgeschützt: `ModulRegistrierung.NurLesend` setzt in seiner Verbindung `default_transaction_read_only=on`. Damit entfällt der `NurLesenInterceptor` und mit ihm die Heuristik aus dem Restrisiko oben. `TagWith`-Abfragen funktionieren; `ExecuteDelete` und beschreibbare CTEs scheitern mit PostgreSQL-Fehler `25006`.

**Dateien:**
- `src/Zeltlotse.SharedKernel/Daten/ModulRegistrierung.cs` – Lese-Verbindung schreibgeschützt.
- `src/Zeltlotse.SharedKernel/Daten/ModulLeseDbContext.cs` – `NurLesenInterceptor` entfernt.
- `tests/Zeltlotse.SharedKernel.Tests/MandantenfilterTests.cs` – `TagWith`, beschreibbare CTE, `ExecuteDelete` mit `PostgresException`.
- `tests/Zeltlotse.SharedKernel.Tests/TestUmgebung.cs`, `ZustellungTests.cs` – Fehler in einem Modul hält die anderen nicht auf.
- `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs` – `JsonName` für verschachtelte Pfade.
- `tests/Zeltlotse.Api.Tests/MigrationsReihenfolgeTests.cs` – unbekanntes Modul bricht den Start ab.
- `tests/Zeltlotse.ArchitectureTests/EreignisFlussTests.cs` – Spine-Tabelle mit mindestens 10 Zeilen.

**Review:** 41 Befunde. 5 Patch-Einträge (2 medium: Nur-Lesen-Schutz, Fehlerfang je Modul; 3 low: `Ordnen`-Test, `JsonName`-Test, Spine-Zeilenzahl), 1 Eintrag zurückgestellt (bereits in `deferred`), alle übrigen mit Begründung im Review Triage Log verworfen. Die Forderung des Aufrufers nach den AD-30-Pflichtfällen (403, 404, Token) entfällt weiter: 0-1 hat keinen Endpunkt dieser Arten.

**Folge-Review empfohlen:** nein – kein high-Eintrag gepatcht (gepatcht: high 0, medium 2, low 3).

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` → 0 Warnungen, 0 Fehler. `dotnet test` → 102 von 102 grün (Architektur, SharedKernel, API; echtes PostgreSQL per Testcontainers).

**Restrisiken:** Der Rückfall auf den Mandanten-Kontext für nicht organisationseigene Aggregate ist ungetestet (mit dem ersten solchen Aggregat prüfen). Ereignis-, Zustell- und Inbox-Zeilen speichern Typnamen; ein Umbenennen braucht dann eine Datenmigration.
