---
title: 'Warnung und Erinnerung bei Nachweisen'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: 'd5a8c388461f5632fddf88edf50ba337ad2bbbd4'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-5-nachweise-mit-einmaliger-einsicht.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Leitung sieht nicht, wem im Team ein Pflicht-Nachweis fehlt oder bei wem er vor dem Ende der Freizeit abläuft, und kann niemanden erinnern (CAP-5, FR-11). „Pflicht“ aus der Org-Richtlinie (1-5) wirkt noch nirgends. Mails gehen bisher nur direkt aus Endpunkten raus (selbst angeforderte Mails); für eine ausgelöste Erinnerung verlangt AD-13 den Weg über die Outbox.

**Approach:**
- **SharedKernel:** Mail-Aufträge über die Outbox. Ein Aggregat merkt einen Mail-Auftrag (nur Kennungen) vor; die Zustellung baut die Mail erst beim Versand.
- **Organisationen:** lesende Schnittstelle „Nachweis-Stand“ (Pflicht-Arten, „gültig bis“ der Bestätigungen).
- **Freizeiten:** Endpunkt „Nachweis-Warnungen“ für die Leitung.
- **Personen:** Endpunkt „Erinnern“. Er stellt eine offene Einsicht sicher und merkt die Erinnerungs-Mail vor.
- **Web-App:** Warnhinweise und „Erinnern“ in der Prüfliste, Warnmeldung auf der Freizeit-Startseite.
- Dazu Tests, Demo, Doku und Screenshots.

## Boundaries & Constraints

**Always:**

- **Mail-Aufträge (SharedKernel, AD-13, AD-4):**
  - Neu in `SharedKernel/Mail/`: Marker `IMailAuftrag`, `IMailAufbau<TAuftrag>` mit `Task<MailInhalt?> AufbauenAsync(TAuftrag auftrag, CancellationToken)`, `record MailInhalt(string An, string Betreff, string Text)` und die Registrierung `services.VerschicktMail<TAuftrag, TAufbau>()`.
  - `Aggregat` bekommt `protected void MailVormerken(IMailAuftrag auftrag)`. Wie ein Ereignis erhöht er die Folgenummer und landet über die Outbox-Stufe im selben `SaveChanges` als Outbox-Zeile, mit genau einer Zustellung an den festen Empfänger-Namen `Zeltlotse.Mail`.
  - Die Zustellung erkennt diesen Empfänger. Sie setzt den Mandanten aus dem Umschlag, baut die Mail mit dem registrierten `IMailAufbau` im eigenen DI-Scope und verschickt sie über `IMailVersand`.
    - `null` heißt: nichts zu senden, die Zustellung ist erledigt.
    - Ein Fehler wird mit der vorhandenen Wartezeit wiederholt.
    - Kein Ereignis-Empfänger, keine Inbox, keine Zeile in der Tabelle Ereignis-Fluss.
  - Mail-Aufträge bestehen nur aus `Guid`, Zahlen, Datumswerten und Enums. Adresse, Namen und Text entstehen erst beim Versand, nichts davon steht in der Outbox. Ein neuer Architekturtest prüft das für alle `IMailAuftrag`-Typen der Module.
- **Nachweis-Stand (Organisationen.Contracts, neuer Ordner `Nachweise/`):**
  - `INachweisStandLesen.StandAsync(IReadOnlyCollection<Guid> personIds, ct) → NachweisStand(IReadOnlyList<NachweisArt> PflichtArten, IReadOnlyList<NachweisGueltigkeit> Bestaetigungen)` mit `NachweisGueltigkeit(Guid PersonId, NachweisArt Art, DateOnly GueltigBis)`.
  - Läuft unter dem Mandantenfilter (Organisation der Freizeit). Pflicht-Arten kommen aus den gespeicherten Richtlinien-Zeilen mit `Pflicht = true`, in Enum-Reihenfolge.
  - Contracts dürfen keine anderen Contracts referenzieren. Darum bekommt `Organisationen.Contracts.Nachweise` einen eigenen `enum NachweisArt` mit denselben Namen und Werten. Der Enum-Gleichheitstest aus 1-5 wird um ihn und um den neuen Enum in `Api.Contracts/Freizeiten/` erweitert.
- **Warnregel (Freizeiten, eine Stelle, mit `FakeTimeProvider` getestet):**
  - Je Person und Pflicht-Art: keine Bestätigung → `fehlt`.
  - Bestätigung mit `GueltigBis` vor dem letzten Freizeittag → `abgelaufen`, wenn `GueltigBis` vor heute liegt (Kalendertag Europe/Berlin), sonst `laeuftAb`.
  - Sonst keine Warnung. Der Nachweis gilt einschließlich `GueltigBis`.
- **Endpunkte:**

  | # | Endpunkt | Modul | Zugang | Ergebnis |
  |---|---|---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId:guid}/nachweis-warnungen` | Freizeiten | `.NurFreizeitRolle(Leitung)` | `200 {personen: [{personId, vorname, nachname, warnungen: [{art, grund, gueltigBis?}]}]}` |
  | 2 | `POST /api/freizeiten/{freizeitId:guid}/personen/nachweise/erinnerungen {personId}` | Personen | `.NurFreizeitRolle(Leitung)` | `200 {erinnertAm}` |

  - **Endpunkt 1:**
    - Liefert nur Personen mit mindestens einer Warnung: alle mit Freizeit-Rolle in dieser Freizeit außer der anfragenden Person.
    - Sortiert nach Nachname, dann Vorname. `warnungen` in Enum-Reihenfolge.
    - `grund` ist `fehlt`, `laeuftAb` oder `abgelaufen` (camelCase-String). `gueltigBis` fehlt bei `fehlt`.
    - Namen kommen über `IPersonenNamen`.
    - Datenformen in `Api.Contracts/Freizeiten/NachweisWarnungen.cs`, mit eigenem `NachweisArt` und `NachweisWarnungGrund`.
  - **Endpunkt 2:**
    - Die Person braucht eine Zeile in `personen.freizeit_rolle` dieser Freizeit und ist nicht die anfragende Person, sonst `404`. `personId` fehlt → `400` („Person fehlt.“).
    - Eine zweite Erinnerung derselben Freizeit an dieselbe Person am selben Kalendertag (Europe/Berlin) → `409 konflikt`.
    - In einem Command:
      - Offene Einsicht sicherstellen, mit derselben Logik wie „Einsicht anfragen“ (`EinsichtSicherstellen`, 1-5): Eine vorhandene offene Einsicht bleibt unverändert; sonst entsteht eine neue, gebunden an die Leitung, mit `FreizeitId`.
      - Erinnerung speichern.
      - Mail-Auftrag `NachweisErinnerungMail(Guid PersonId, Guid ErinnertVon)` vormerken.
  - Beide Endpunkte gehören zu `Aktionsschluessel.NachweisePruefen` (kein neuer Schlüssel). Der Rollen-Gleichheitstest aus 1-5 umfasst beide.
- **Erinnerung (Personen, Tabelle `nachweis_erinnerung`, `Aggregat`, `IOrganisationsEigen`):**
  - Felder `Id`, `OrganisationId`, `FreizeitId`, `PersonId`, `ErinnertVon`, `ErinnertAm`.
  - Eindeutig (FreizeitId, PersonId). Jede weitere Erinnerung aktualisiert dieselbe Zeile über eine Methode, die den Mail-Auftrag ablegt.
  - Zeilensperre bzw. eindeutiger Index gegen doppeltes Senden.
- **Prüfliste (1-5 Endpunkt 6):**
  - Additiv `erinnertAm?` je Person: die letzte Erinnerung in dieser Freizeit.
  - Auf der Org-Route (Endpunkt 13) immer `null`.
- **Erinnerungs-Mail (`IMailAufbau` in Personen):**
  - Adresse über die neue Methode `IKontoLesen.MailDerPersonAsync(Guid personId)` (Konten.Contracts; Pfeil Personen → Konten). Ohne Konto → `null`, also kein Versand.
  - Betreff „Erinnerung: Nachweise in Zeltlotse“.
  - Text ohne Anrede und ohne Du/Sie: „<Vorname Nachname> aus der Leitung einer Freizeit bittet darum, die Nachweise in Zeltlotse zu ergänzen und die Anfrage zur Einsicht freizugeben:“, dann der Link `<Zeltlotse:OeffentlicheAdresse>/app/konto/nachweise`, dann „Diese Mail hat die Leitung ausgelöst.“
  - Keine Angaben zu einzelnen Nachweisen.
- **Web-App:**
  - **Prüfliste** auf der Freizeit-Route (`NachweisePruefen.razor`):
    - Die Seite lädt zusätzlich Endpunkt 1 und zeigt je Person in einer Spalte „Warnhinweise“ je Warnung einen Status (Art Warnung): „<Art> nicht bestätigt“, „<Art> läuft am TT.MM.JJJJ ab“ bzw. „<Art> abgelaufen am TT.MM.JJJJ“.
    - Bei Personen mit Warnung gibt es den Knopf „Erinnern“ als `InlineBestaetigung`: Satz „Erinnerung an <Vorname Nachname> senden? Dabei wird die Einsicht in die Nachweise angefragt.“, Knopf „Ja, senden“.
    - Mit `erinnertAm` steht dort „Erinnert am TT.MM.JJJJ“.
    - Erfolg: Meldung „Gesendet: Die Erinnerung an <Name> ist verschickt.“, danach lädt die Liste neu.
    - `409`: Meldung „Fehler: <Name> wurde heute schon erinnert.“
    - Scheitert Endpunkt 1, bleibt die Liste ohne Warnhinweise nutzbar.
    - Auf der Org-Route keine Warnhinweise und kein „Erinnern“.
  - **Freizeit-Startseite:**
    - Mit `nachweise-pruefen` lädt sie Endpunkt 1.
    - Gibt es Personen mit Warnung, erscheint eine Meldung (Warnung) „Achtung: Bei <n> Person(en) im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.“ (Einzahl „1 Person“) über dem vorhandenen Knopf „Nachweise prüfen“.
    - Scheitert der Abruf, entfällt die Meldung.
  - Nur Bausteine aus Ui.Komponenten, Datum über `Anzeige.Datum`, Art über `Anzeige.Nachweis`. API-Aufrufe über `FreizeitenApiClient` bzw. `PersonenApiClient`.
- **Demo-Daten (über Commands, idempotent):**
  - Andrea setzt in der Richtlinie „Erste Hilfe“ auf Pflicht, nur wenn sie es noch nicht ist.
  - Damit sieht Miriam bei Jonas „Führungszeugnis nicht bestätigt“ und „Erste Hilfe läuft am 15.04.2027 ab“ (Freizeit 02.–13.08.2027).
  - Keine Demo-Erinnerung.
- **Doku:**
  - Neu `leitung/nachweise-erinnern.md` „Fehlende Nachweise und Erinnerungen“:
    - Warnregel in Klartext: Pflicht laut Organisation, „läuft vor dem Ende der Freizeit ab“.
    - Meldung auf der Startseite, Warnhinweise in der Liste.
    - „Erinnern“ verschickt erst nach Bestätigung eine Mail und fragt die Einsicht an; höchstens einmal am Tag je Person.
    - Was die Person danach tut.
    - Bilder `nachweis-erinnern` (Prüfliste mit aufgeklappter Bestätigung, Miriam) und `freizeit-start`.
  - Anpassen:
    - `org-admin/nachweis-richtlinie.md`: Pflicht wirkt jetzt als Warnhinweis der Leitung.
    - `mitarbeitende/nachweise.md`: Erinnerungs-Mail, was zu tun ist.
    - `leitung/nachweise-pruefen.md`: Verweis auf die neue Seite.
    - `leitung/index.md`.
- **Tests (AD-30), Test vor dem Code:** siehe Matrix. Dazu:
  - HTTP-Tests gegen PostgreSQL mit S3, wo Dateien nötig sind.
  - Architekturtest Mail-Auftrag.
  - bUnit für Prüfliste und Startseite.
  - E2E-Bild `nachweis-erinnern` (Handy, Laptop, axe).
  - Angepasste `EndpunktListeTests`, `DemoDatenTests` und Richtlinien-Screenshot.

**Never:**
- Keine automatisch verschickten Erinnerungen, kein Zeitplan, keine Erinnerungen ohne Inline-Bestätigung (AD-13, Leitprinzip 6).
- Kein Leitungs-Cockpit (2-6), keine Warnhinweise zu Freizeitpässen, Aufgaben oder Sicherheitskette.
- Keine Warnungen für Personen aus anderen Freizeiten, keine Warnungen auf der Org-Route, keine Warnungen bei der Person selbst über 1-5 hinaus.
- Keine Mail-Adresse, kein Name und kein Mail-Text in der Outbox; keine Nachweis-Details in der Mail.
- Kein neues Ereignis in einem `*.Contracts.Events`-Namensraum, keine neue Zeile im Ereignis-Fluss, kein neuer Pfeil, kein neues Modul, kein neuer Problemcode, kein neuer Aktionsschlüssel.
- Keine Pop-ups. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Warnregel | Richtlinie Führungszeugnis + Erste Hilfe Pflicht; Freizeit 02.–13.08.2027; Uhr 2026-10-12. Jonas: Erste Hilfe bestätigt bis 2027-04-15, Führungszeugnis ohne Bestätigung; Person B: Erste Hilfe bis 2027-08-13, Führungszeugnis bis 2026-10-11 | Jonas: `fehlt` (Führungszeugnis), `laeuftAb` 2027-04-15 (Erste Hilfe); B: `abgelaufen` 2026-10-11 (Führungszeugnis), Erste Hilfe ohne Warnung | – |
| Ohne Warnung | alle Pflicht-Arten gültig bis nach dem letzten Tag; JuLeiCa (keine Pflicht) fehlt | Person fehlt in `personen` | – |
| Nach der Freizeit | Uhr nach dem Ende, Bestätigung bis nach dem letzten Tag, aber vor heute | keine Warnung | – |
| Eigene Person, andere Freizeit | anfragende Leitung ohne Bestätigungen; Person mit Rolle nur in einer anderen Freizeit derselben Organisation | beide fehlen | – |
| Mandant | Bestätigung derselben Person in Org B | zählt in Org A nicht | – |
| Rechte Endpunkt 1 | Leitung; Team (Zeile per SQL); Leitung einer fremden Freizeit; Org-Admin ohne Freizeit-Rolle | `200` / `403` / `404` / `404` | – |
| Erinnern | Miriam erinnert Jonas (keine offene Einsicht) | `200 {erinnertAm}`; neue offene Einsicht (gebunden an Miriam, FreizeitId); Outbox-Zeile ohne Adresse oder Namen; nach Zustellung genau eine Mail an Jonas mit Link `/app/konto/nachweise` und Miriams Namen; Prüfliste zeigt `erinnertAm` | – |
| Erinnern mit offener Einsicht | automatische Einsicht offen | dieselbe Einsicht bleibt (keine zweite, Bindung unverändert) | – |
| Erinnern Grenzen | zweites Mal am selben Tag; nächster Tag (Uhr +1 Tag); für sich selbst; Person ohne Rolle in der Freizeit; `personId` fehlt | `409` / `200` / `404` / `404` / `400` | – |
| Rechte Endpunkt 2 | Team; fremde Freizeit | `403` / `404` | – |
| Mail-Auftrag | Aufbau liefert `null`; Aufbau wirft einmal | erledigt ohne Mail; zweiter Versuch nach Wartezeit verschickt genau eine Mail | – |
| Prüfliste Org-Route | Org-Admin nach einer Erinnerung | `erinnertAm` = `null` | – |
| Demo | Start mit Demo zweimal | Richtlinie Erste Hilfe Pflicht; Endpunkt 1 für Miriam: Jonas mit `fehlt` (Führungszeugnis) und `laeuftAb` 2027-04-15 (Erste Hilfe); keine Erinnerung | – |

</intent-contract>

## Code Map

- `src/Zeltlotse.SharedKernel/Aggregat.cs` -- `EreignisAblegen` und Folgenummer; `MailVormerken` daneben.
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContext.cs:41-90` -- `OutboxSchreibenUndSpeichernAsync`: Zustellungen aus `katalog.EmpfaengerFuer(typ)`; hier für `IMailAuftrag` die eine Zustellung `Zeltlotse.Mail`.
- `src/Zeltlotse.SharedKernel/Outbox/OutboxZustellung.cs:131-143` -- `EinzelnZustellenAsync`: Weiche für `Zeltlotse.Mail`, Mandant setzen, `IMailAufbau` auflösen, `IMailVersand`. Die Wartezeit-Logik bleibt.
- `src/Zeltlotse.SharedKernel/Ereignisse/IEreignisEmpfaenger.cs` (`EmpfaengtEreignis`, `EmpfaengerEintrag`) und `EreignisKatalog.cs` -- Vorbild für die Registrierung und den Katalog der Mail-Aufträge.
- `src/Zeltlotse.SharedKernel/Mail/IMailVersand.cs` -- Versand, der Adapter steht im Host (`Adapter/MailVersand.cs`, Ordner-Mail in Tests).
- `tests/Zeltlotse.Testmodul/`, `tests/Zeltlotse.SharedKernel.Tests/` -- Pipeline- und Outbox-Tests mit Testmodul; hier die Mail-Auftrag-Tests (`null`, Fehler und Wiederholung).
- `tests/Zeltlotse.ArchitectureTests/EreignisFlussTests.cs` (prüft nur `IEreignisEmpfaenger`, bleibt unverändert), `TypRegelnTests.cs` (Regel für Ereignis-Typen, Vorbild für die Mail-Auftrag-Regel), `Loesung.cs:24-42` (Pfeile Freizeiten → Organisationen und Personen → Konten vorhanden).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/NachweisRichtlinie.cs`, `NachweisCommands.cs` (Richtlinie, Bestätigung, `RichtlinieFeldAendern`) -- `INachweisStandLesen` umsetzen und in `OrganisationenModul.cs` registrieren, über den Lese-Kontext.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Freizeit.cs:33-36` (`ErsterTag`, `LetzterTag`), `Domaene/FreizeitRolle.cs`, `Startseite/FreizeitEndpunkte.cs` (Endpunkt-Stil, Kontext), `Rechte/FreizeitRechte.cs:34` (`NachweisePruefen`), `FreizeitenModul.cs` -- Endpunkt 1 als neuer Slice `Nachweise/`. Das csproj bekommt `Organisationen.Contracts`.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/EinsichtAnfragen.cs:16-80` (`EinsichtSicherstellen`, Sperre), `PruefEndpunkte.cs` (Prüfliste, Freizeit- und Org-Gruppe, Rollenprüfung über `freizeit_rolle`), `Daten/PersonenDbContext.cs` (DbSets, Modell; neue Migration), `PersonenModul.cs` (Registrierung, `VerschicktMail`), `Familien/FamilienMails.cs` (Mail-Text und Link-Stil mit `IConfiguration`). Das csproj bekommt `Konten.Contracts`.
- `src/Modules/Konten/Zeltlotse.Konten.Contracts/Konten/IKontoLesen.cs`, `src/Modules/Konten/Zeltlotse.Konten/Ich/KontoLesen.cs` -- `MailDerPersonAsync` (Konto hat einen eindeutigen Index auf `PersonId`).
- `src/Zeltlotse.Api.Contracts/Personen/Nachweise.cs:146` (`PruefPerson`; `ErinnertAm` additiv), `Gemeinsam/` (Aktionsschlüssel, Problemcodes) und ein neuer Ordner `Freizeiten/`.
- `src/Zeltlotse.Api/Demo/DemoDaten.cs`, `DemoKatalog.cs` -- Richtlinie Erste Hilfe Pflicht über `RichtlinieFeldAendern` (Andrea).
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs`, `PersonenApiClient.cs`; `src/Zeltlotse.App/Features/Personen/NachweisePruefen.razor`, `Features/Freizeiten/FreizeitStart.razor`; `src/Zeltlotse.Ui.Komponenten/` (Status, Meldung, InlineBestaetigung, Datentabelle).
- `tests/Zeltlotse.Api.Tests/` -- `NachweisEinsichtTests.cs` (Helfer, S3, Rollen), `EndpunktListeTests.cs` (Liste der Freizeit-Routen, Zähler), `DemoDatenTests.cs`, Rollen-Gleichheitstest aus 1-5. Enum-Gleichheitstest in `tests/Zeltlotse.Personen.Tests/` (Treffer `NachweisArt`). `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` (`Uhr`, `Mails`, `OutboxJetztZustellenAsync`), `MailOrdner.cs` (Linkmuster `/app/konto/nachweise`).
- `tests/Zeltlotse.Ui.Tests/NachweisSeitenTests.cs` -- bUnit der Prüfliste. `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- `BildAsync`; neues Bild `nachweis-erinnern`.
- `doku/src/content/docs/leitung/`, `org-admin/nachweis-richtlinie.md`, `mitarbeitende/nachweise.md`; `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (jedes Bild verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.SharedKernel.Tests/` -- ZUERST: Mail-Auftrag in die Outbox (eine Zustellung `Zeltlotse.Mail`, Inhalt nur Kennungen), Zustellung baut und verschickt, `null` ohne Mail, Fehler mit Wiederholung, Mandant aus dem Umschlag.
- `tests/Zeltlotse.ArchitectureTests/` -- ZUERST: Mail-Aufträge nur aus erlaubten Typen.
- `tests/Zeltlotse.Freizeiten.Tests/` -- ZUERST: Warnregel als reine Funktion mit festen Daten (Matrix-Zeilen Warnregel, ohne Warnung, nach der Freizeit).
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST: Erinnerung (eine Zeile je Freizeit und Person, einmal je Tag, Mail-Auftrag abgelegt), Enum-Gleichheit erweitert.
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: HTTP-Tests für Endpunkt 1 und 2 und alle übrigen Matrix-Zeilen inklusive Mail nach `OutboxJetztZustellenAsync`; dazu `EndpunktListeTests`, `DemoDatenTests` und die Rollenlisten.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: Prüfliste mit Warnhinweisen, Erinnern (Erfolg, `409`, Endpunkt 1 scheitert), Org-Route ohne Warnungen, Startseite mit Meldung (Einzahl und Mehrzahl) bzw. ohne Meldung.
- `src/Zeltlotse.SharedKernel/` -- Mail-Aufträge, Registrierung, Zustellung.
- `src/Modules/Konten/` -- `MailDerPersonAsync`.
- `src/Modules/Organisationen/` -- `INachweisStandLesen` mit Enum.
- `src/Modules/Freizeiten/` -- Warnregel und Endpunkt 1.
- `src/Modules/Personen/` -- Erinnerung (Domäne, Migration, Command, Endpunkt 2, `IMailAufbau`), `erinnertAm` in der Prüfliste.
- `src/Zeltlotse.Api.Contracts/` -- Datenformen.
- `src/Zeltlotse.Api/Demo/` -- Richtlinie Erste Hilfe Pflicht.
- `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` -- Clients, Prüfliste, Startseite.
- `tests/Zeltlotse.E2E.Tests/` -- Bild `nachweis-erinnern` (Miriam, Bestätigung bei Jonas aufgeklappt) je Handy und Laptop mit axe. Die vorhandenen Bilder `nachweise-pruefen`, `freizeit-start` und `nachweis-richtlinie` laufen neu.
- `doku/` -- neue Seite und Anpassungen laut Always.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, SharedKernel, Konten, Personen, Organisationen, Freizeiten, Abläufe, API, Ui, E2E), und `nachweis-erinnern` ist je Handy und Laptop geschrieben.
- Given Miriam (Demo) öffnet die Freizeit, when die Startseite lädt, then steht dort „Achtung: Bei 1 Person im Team fehlen Nachweise oder laufen vor dem Ende der Freizeit ab.“, und in „Nachweise prüfen“ zeigt die Zeile von Jonas „Führungszeugnis nicht bestätigt“ und „Erste Hilfe läuft am 15.04.2027 ab“.
- Given Miriam tippt bei Jonas „Erinnern“ und „Ja, senden“, when die Outbox zugestellt ist, then liegt genau eine Mail an `jonas.huettner@example.org` mit dem Link auf `/app/konto/nachweise` vor, und die Zeile zeigt „Erinnert am <heute>“.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler, und die Seite `leitung/nachweise-erinnern/` zeigt ihre Bilder.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 28 findings — high 0, medium 0, low 22, false 6, maybe-false 0
- findings:
  - `[low]` `[patch]` (verification-gap) Erinnerungen je Freizeit nie mit zwei Freizeiten getestet — API-Test ergänzt: Erinnerung in einer zweiten Freizeit, Prüfliste der ersten zeigt `null`, eigene Erinnerung am selben Tag `200`.
  - `[low]` `[patch]` (verification-gap) 404-Zweig von „Erinnern“ in der Web-App ohne Test — bUnit-Fall mit 404 und Fehlermeldung ergänzt.
  - `[low]` `[reject]` (blind) Ohne Konto meldet die Oberfläche „Gesendet“, aber keine Mail geht hinaus — heute unerreichbar: Freizeit-Rollen entstehen nur über angenommene Einladungen und damit mit Konto; ein Schutz wäre ein neuer Zweig samt Ergebnis und Text.
  - `[low]` `[patch]` (blind) „ist verschickt“ stimmt nicht, die Mail ist nur vorgemerkt — Text in Seite, bUnit und Doku zu „geht per Mail hinaus“ geändert.
  - `[low]` `[reject]` (blind) Die Mail nennt weder Freizeit noch Organisation — Personen darf Freizeiten und Organisationen nicht lesen (AD-2, Design Notes); die Mail nennt die Leitung beim Namen.
  - `[low]` `[reject]` (blind) Der Server prüft nicht, ob eine Warnung vorliegt — die Warnregel liegt in Freizeiten, Personen darf sie nicht lesen; die Leitung ist berechtigt (Vertrauen statt Rechte-Matrix), höchstens eine Mail am Tag.
  - `[low]` `[reject]` (blind) Keine Grenze nach Freizeitende, „nicht bestätigt“ bleibt — der Hinweis bleibt wahr (die Person hat ohne Bestätigung mitgearbeitet); Abläufe nach dem Ende verschwinden wie in der Spec; Schreibschutz nach Abschluss kommt mit 3-8.
  - `[low]` `[reject]` (blind) Die erinnerte Person sieht nicht, welche Nachweise verlangt sind — die Mail nennt bewusst keine Nachweise (Spec); eine Pflicht-Anzeige bei der Person wäre neue Oberfläche.
  - `[low]` `[reject]` (blind) Erinnerungs-Zeilen werden nie gelöscht — Löschen und Auskunft gehören für alle Module zu 1-7 (AD-16); bisher löscht kein Modul.
  - `[false]` `[reject]` (blind) `MailDerPersonAsync` ohne eindeutigen Index — `KontenDbContext` hat `HasIndex(k => k.PersonId).IsUnique()`.
  - `[false]` `[reject]` (blind) Kein Test ohne Konto — `NachweisErinnerungTests.Ohne_Konto_baut_die_Erinnerung_keine_Mail` deckt den Aufbau ab; 404-UI-Fall siehe Patch oben; `_meldungTitel` bleibt nicht falsch stehen, weil die übrigen Meldungen der Seite Fehler sind und „Einsicht anfragen“ den Titel setzt.
  - `[low]` `[patch]` (blind) Unbeobachtete Aufgabe in `NeuLadenAsync` — Warnungen werden jetzt immer abgewartet.
  - `[low]` `[patch]` (blind) Leerzeichen vor Komma im Alt-Text von `freizeit-oeffnen.md` — entfernt.
  - `[low]` `[patch]` (blind) Doku verschweigt, dass Erinnern nur in der Freizeit geht und Richtlinien-Änderungen sofort wirken — zwei Sätze in `nachweise-erinnern.md` ergänzt.
  - `[low]` `[reject]` (edge) Ohne Konto 200 ohne Mail — gleiche Lage wie blind, verworfen.
  - `[low]` `[reject]` (edge) Erinnern ohne Warnung über die API — gleiche Lage wie blind, verworfen.
  - `[low]` `[reject]` (edge) Erinnern nach Freizeitende — gleiche Lage wie blind, verworfen.
  - `[low]` `[reject]` (edge) Rollenquelle Freizeiten vs. ereignisgepflegte Kopie in Personen weicht kurz ab — nur bis zur Zustellung der Outbox (Sekunden); eine gemeinsame Quelle verletzte AD-2/AD-8.
  - `[low]` `[patch]` (edge) Doppeltes „Ja, senden“ überschreibt die Erfolgsmeldung mit 409 — frühes Zurückkehren, solange eine Übertragung läuft.
  - `[low]` `[reject]` (edge) `JsonException` auf der Startseite nicht gefangen — gleiches Muster in der ganzen App; Abweichende Versionen fängt „Neu laden“ (AD-5), API-Änderungen sind additiv.
  - `[low]` `[reject]` (edge) `JsonException` in der Prüfliste — gleiche Lage.
  - `[low]` `[patch]` (edge) Unbeobachtete Aufgabe in `NeuLadenAsync` — gleiche Ursache wie blind, gleicher Patch.
  - `[low]` `[reject]` (intent) Warnung nur in der Oberfläche als Bedingung für Erinnern — gleiche Lage wie blind, verworfen.
  - `[low]` `[patch]` (intent) AD-30 „fremde Organisation → 404“ für die neuen Endpunkte nicht getestet — API-Fall mit Freizeit einer anderen Organisation ergänzt.
  - `[false]` `[reject]` (intent) Playwright klickt „Ja, senden“ nie — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; der Versand ist per HTTP und bUnit belegt.
  - `[false]` `[reject]` (intent) Mail-Infrastruktur über die Absicht hinaus — AD-13 verlangt für ausgelöste Mails die Outbox; ohne sie gäbe es keine Erinnerung.
  - `[false]` `[reject]` (intent) Doku-Build nicht belegt — `npm run build` lief mit Exit-Code 0 (21 Seiten).
  - `[false]` `[reject]` (intent) Token-Fälle fehlen — kein Endpunkt der Story nutzt ein Token (AD-30 ordnet nach Art).

## Design Notes

- **Mail über die Outbox ohne neues Ereignis:** AD-13 verlangt für ausgelöste Mails die Outbox. Ein Ereignis mit Empfänger im eigenen Modul bräuchte eine Zeile im Ereignis-Fluss; AD-24 erzwingt die Tabelle für jedes Paar (Ereignis, Empfänger). Ein Mail-Auftrag ist keine Folgeaktion in einem Modul, sondern ein Versand. Darum nutzt er die vorhandene Outbox-Tabelle und die Zustellung mit eigenem Empfänger-Namen, ohne Inbox und ohne Command. Auch 2-3 und 2-6 nutzen diesen Weg. Weil der Auftrag nur Kennungen trägt und die Mail erst beim Versand entsteht, liegen keine Adressen oder Namen dauerhaft in der Outbox (AD-4, AD-22, Löschen in 1-7).
  ```csharp
  erinnerung.Erinnern(leitungId, jetzt); // Methode legt ab:
  MailVormerken(new NachweisErinnerungMail(PersonId, leitungId));
  ```
- **„Ablaufend“ = vor dem Ende der Freizeit:** FR-11 nennt keine Frist. Für die Leitung zählt, ob der Nachweis die ganze Freizeit über gilt. Darum gibt es keine feste Tageszahl, die sich nicht begründen ließe. Nach der Freizeit verschwinden Warnungen zu Abläufen, die die Freizeit nicht mehr betreffen.
- **„Fehlt“ = keine Bestätigung der Organisation:** Ob die Person etwas hochgeladen hat, weiß die Organisation nicht (AD-9: nur die Bestätigung wandert). Darum steht dort „nicht bestätigt“, nicht „nicht hochgeladen“.
- **Wer rechnet:** Freizeiten kennt Team und Zeitraum und darf Organisationen und Personen lesen (AD-2). Organisationen kennt weder Team noch Zeitraum. Der Server rechnet die Warnungen, damit HTTP-Tests CAP-5 belegen; der Client zeigt sie nur an.
- **Erinnern im Modul Personen:** Personen besitzt die Nachweise und die Einsichten und darf Konten lesen (Adresse). Den Namen der Freizeit darf Personen nicht lesen; die Mail nennt deshalb die Leitung beim Namen.
- **Erinnern fragt die Einsicht an:** Ohne offene Anfrage könnte die Person nach der Mail nichts freigeben. Die Erinnerung stellt sie deshalb mit der Logik aus 1-5 sicher, ohne neue Regeln.
- **Einmal am Tag:** NFR-10 verlangt Ruhe statt Flut. Die gespeicherte Erinnerung zeigt beiden Leitungen „Erinnert am …“ und verhindert doppelte Mails am selben Tag. Eine Zeile je Freizeit und Person reicht.
- **Startseite statt Cockpit:** Das Cockpit kommt mit 2-6. CAP-5 verlangt den Warnhinweis schon jetzt. Eine Meldung auf der vorhandenen Startseite (nur Leitung) ist die kleinste Lösung; 2-6 übernimmt sie.
- **Demo:** Erste Hilfe als Pflicht zeigt neben „nicht bestätigt“ auch „läuft ab“ mit den vorhandenen Demo-Nachweisen. Ohne Demo-Erinnerung verschickt das Einspielen keine Mails.
- **AD-30-Pflichtfälle:** Freizeit: Leitung `200`, Team `403`, fremde Freizeit `404` (beide Endpunkte). Persönliche Endpunkte gibt es keine neuen. Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seite `leitung/nachweise-erinnern/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Warnung und Erinnerung bei Nachweisen (CAP-5, FR-11):
- **SharedKernel:** Mail-Aufträge über die Outbox (AD-13). `Aggregat.MailVormerken` legt einen Auftrag nur mit Kennungen ab. Die Zustellung an den festen Empfänger `Zeltlotse.Mail` baut die Mail erst beim Versand über `IMailAufbau` und verschickt sie über `IMailVersand`. `null` heißt: keine Mail. Fehler werden mit Wartezeit wiederholt. Ein Architekturtest erlaubt in Mail-Aufträgen nur Guid, Zahlen, Datumswerte und Enums.
- **Konten:** `IKontoLesen.MailDerPersonAsync`.
- **Organisationen:** `INachweisStandLesen` liefert die Pflicht-Arten und „gültig bis“ der Bestätigungen, mit eigenem Enum.
- **Freizeiten:** Warnregel `NachweisWarnregel` (fehlt, läuft vor dem letzten Freizeittag ab, abgelaufen) und `GET /api/freizeiten/{id}/nachweis-warnungen` für die Leitung.
- **Personen:**
  - Aggregat `NachweisErinnerung`: eine Zeile je Freizeit und Person, höchstens einmal am Tag (Europe/Berlin).
  - `POST …/personen/nachweise/erinnerungen` stellt die offene Einsicht sicher und merkt die Mail vor.
  - Die Prüfliste liefert zusätzlich `erinnertAm`.
- **Web-App:**
  - Prüfliste: Spalte „Warnhinweise“ und „Erinnern“ mit Inline-Bestätigung, nur in der Freizeit.
  - Startseite der Freizeit: Meldung „Achtung: Bei n Person(en) …“.
- **Demo:** Andrea setzt Erste Hilfe auf Pflicht. Miriam sieht bei Jonas „Führungszeugnis nicht bestätigt“ und „Erste Hilfe läuft am 15.04.2027 ab“.
- **Doku:** neue Seite `leitung/nachweise-erinnern.md`. Angepasst: `org-admin/nachweis-richtlinie.md`, `mitarbeitende/nachweise.md`, `leitung/nachweise-pruefen.md`, `leitung/index.md`, `leitung/freizeit-oeffnen.md`. Neues Bild `nachweis-erinnern` (Handy, Laptop); `freizeit-start`, `nachweise-pruefen` und `nachweis-richtlinie` neu erzeugt.

**Dateien:**
- `src/Zeltlotse.SharedKernel/Mail/MailAuftraege.cs`, `Aggregat.cs`, `Daten/ModulDbContext.cs`, `Outbox/OutboxZustellung.cs`, `SharedKernelRegistrierung.cs` – Mail-Aufträge.
- `src/Modules/Konten/` – `MailDerPersonAsync`.
- `src/Modules/Organisationen/Zeltlotse.Organisationen.Contracts/Nachweise/INachweisStandLesen.cs`, `Zeltlotse.Organisationen/Nachweise/NachweisStandLesen.cs` – Nachweis-Stand.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Nachweise/NachweisWarnungen.cs`, `Domaene/FreizeitZeiten.cs`, `FreizeitenModul.cs`, csproj – Warnregel und Endpunkt.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/NachweisErinnerung.cs`, `PruefEndpunkte.cs`, `NachweisRegeln.cs`, Migration `NachweisErinnerung`, `PersonenModul.cs`, csproj – Erinnerung.
- `src/Zeltlotse.Api.Contracts/Freizeiten/NachweisWarnungen.cs`, `Personen/Nachweise.cs` – Datenformen.
- `src/Zeltlotse.Api/Demo/DemoDaten.cs` – Demo-Richtlinie.
- `src/Zeltlotse.Ui/` und `src/Zeltlotse.App/Features/` – Clients, Texte, Prüfliste, Startseite.
- `tests/` – SharedKernel (`MailAuftragTests`), Architektur, Freizeiten (`NachweisWarnregelTests`), Personen (`NachweisErinnerungTests`), Konten, Organisationen, API (`NachweisWarnungTests` u. a.), Ui (`NachweisWarnungSeitenTests`), E2E (`Nachweis_Erinnern`).
- `doku/` – Seiten und Screenshots.

**Review (Pass 1, 28 Befunde: high 0, medium 0, low 22, false 6):**
- **Gepatcht (8 low):**
  - Erfolgstext „geht per Mail hinaus“.
  - `NeuLadenAsync` wartet die Warnungen immer ab.
  - Kein doppeltes Senden.
  - Doku-Tippfehler.
  - Doku-Sätze zur Org-Route und zur Wirkung von Richtlinien-Änderungen.
  - Tests: Erinnerungen je Freizeit, fremde Organisation `404` für beide Endpunkte, 404-Zweig in bUnit.
- **Verworfen** (Begründungen im Review Triage Log):
  - Person ohne Konto (heute unerreichbar).
  - Kein Freizeitname in der Mail (AD-2).
  - Keine serverseitige Warnprüfung vor dem Erinnern.
  - Warnungen nach Freizeitende.
  - Löschen der Erinnerungen (1-7).
  - `JsonException`-Fang.
  - Kurzer Abstand zwischen den Rollenquellen.
  - Alle `false`.
- Nichts zurückgestellt.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 0, low 8.

**AD-30 (Hinweis des Aufrufers):**
- **Freizeit:** Leitung `200`, Team `403`, fremde Freizeit derselben Organisation `404`, Freizeit einer fremden Organisation `404`, Org-Admin ohne Freizeit-Rolle `404`. Das gilt für beide neuen Endpunkte, alle gegen echtes PostgreSQL.
- **Token-Fälle:** keine, weil kein Endpunkt der Story ein Token nutzt.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 1191 von 1191 grün.
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 21 Seiten, darunter `leitung/nachweise-erinnern/`.
- **Matrix-Audit:** Jede Zeile der I/O-Matrix hat einen gelaufenen Test (API, Freizeiten, Personen, SharedKernel, Ui, Demo).
- Die Screenshots `app-offline-*`, die nur durch Rendering-Rauschen neu entstanden waren, sind zurückgesetzt.

**Restrisiken:**
- Hat eine Person mit Freizeit-Rolle kein Konto (heute nicht möglich, später etwa bei Gast-Mitarbeitenden), meldet die Oberfläche „Gesendet“, aber es geht keine Mail hinaus.
- Die Mail wird mindestens einmal verschickt: Scheitert das Speichern nach dem Versand, kann sie doppelt ankommen.
- Die Mail nennt weder Freizeit noch Organisation.
- Tests mit festen Daten (Demo „15.04.2027“, Freizeit 2027) laufen ab Sommer 2027 aus, wie die bestehenden Tests.
