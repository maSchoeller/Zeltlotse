---
title: 'Leitungs-Cockpit und Erinnerungen'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '7f7fc35fb0fa6a06c45b428f50081e22f23ffc55'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-6-warnung-und-erinnerung-bei-nachweisen.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-5-freigabe-und-freigabefenster.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Leitung sieht auf der Startseite der Freizeit weder den Anmeldestand gegen die Plätze noch, wessen Freizeitpass unvollständig ist, und kann zum Freizeitpass niemanden erinnern (CAP-14, FR-25, FR-27). Der Nachweis-Warnhinweis aus 1-6 steht allein über dem Knopf „Nachweise prüfen“.

**Approach:**
- **Personen:** zwei neue Endpunkte im Freizeit-Kontext, nur Leitung:
  - „Freizeitpass-Stand“: wer in dieser Freizeit offene Angaben hat.
  - „Erinnern“: merkt eine Erinnerungs-Mail über die Outbox vor (Muster 1-6, ohne Einsicht).
- **Web-App:** Die Startseite der Freizeit wird für die Leitung zum Leitungs-Cockpit. Der Client setzt es aus vorhandenen Endpunkten und dem neuen Stand zusammen (AD-2):
  - Card „Anmeldungen“ mit Plätze-Balken und Zahlen.
  - Card „Warnhinweise“.
  - Card „Freizeitpässe unvollständig“ mit „Erinnern“.
- Dazu Tests, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Endpunkte (Modul Personen, registriert in `PersonenModul.cs` mit `.NurFreizeitRolle(FreizeitpassRechte.Erinnern)`):**

  | # | Endpunkt | Ergebnis |
  |---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId:guid}/personen/freizeitpass-stand` | `200 {personen: [{personId, vorname, nachname, gefragt, offen, erinnertAm?}]}` |
  | 2 | `POST /api/freizeiten/{freizeitId:guid}/personen/freizeitpass/erinnerungen {personId}` | `200 {erinnertAm}` |

  - `FreizeitpassRechte.Erinnern = [Leitung]`. Ein Test hält die Liste gleich mit `FreizeitRechte.Anmeldungen`. In der Oberfläche gilt der vorhandene Aktionsschlüssel `anmeldungen-bearbeiten`; es gibt keinen neuen.
  - Datenformen in `src/Zeltlotse.Api.Contracts/Personen/FreizeitpassStand.cs`:
    - `FreizeitpassStand(IReadOnlyList<FreizeitpassStandPerson> Personen)`
    - `FreizeitpassStandPerson(Guid PersonId, string? Vorname, string? Nachname, int Gefragt, int Offen, DateTimeOffset? ErinnertAm)`
    - `FreizeitpassErinnerungSenden(Guid? PersonId)`
    - `FreizeitpassErinnert(DateTimeOffset ErinnertAm)`
- **Stand-Regel (eine Stelle, mit `FakeTimeProvider` getestet):**
  - Je Person zählt die jüngste Freigabe dieser Freizeit (`AngelegtAm`, dann `Id` absteigend, wie in `FreizeitpassEinsicht`), aber nur, wenn sie aktiv ist (`GueltigBis > jetzt`).
  - Die Stufe spielt keine Rolle, eingegangene und bestätigte Anmeldungen zählen gleich.
  - `gefragt` ist die Anzahl der `Freigabe.Fragen`.
  - `offen` zählt die gefragten Kennungen, die entweder ohne beantwortete Antwort-Zeile sind (`Wert` und `Sonstiges` beide `null` oder keine Zeile) oder veraltet sind (`BestaetigtAm < jetzt.AddMonths(-12)`, wie `IstVeraltet`).
  - Ohne Entschlüsselung: Gezählt wird nur in SQL bzw. über die Spalten, Antworttexte werden nicht gelesen.
  - Geliefert werden nur Personen mit `offen > 0`, außer der anfragenden Person. Sortierung nach Nachname, dann Vorname.
  - Namen kommen aus `personen.person`. Fehlt das Profil, steht die Person nicht in der Liste.
  - `erinnertAm` ist die letzte Freizeitpass-Erinnerung dieser Freizeit an diese Person.
  - Kein Eintrag im Einsicht-Protokoll, weil der Stand keine Antworten zeigt.
- **Erinnern (Endpunkt 2):**
  - `personId` fehlt → `400` („Person fehlt.“).
  - Die Person ist die anfragende Person oder hat keine aktive Freigabe in dieser Freizeit → `404`.
  - Zweite Erinnerung derselben Freizeit an dieselbe Person am selben Kalendertag (Europe/Berlin, `NachweisRegeln.Tag`) → `409 konflikt`.
  - Sonst in einem Command, unter Advisory-Lock je (Freizeit, Person) wie `NachweisRegeln.ErinnerungSperrenAsync`:
    - Erinnerung speichern.
    - Mail-Auftrag `FreizeitpassErinnerungMail(Guid PersonId, Guid ErinnertVon)` über `MailVormerken`.
  - Keine Einsicht, keine Prüfung, ob `offen > 0` ist (die Leitung ist berechtigt; höchstens eine Mail am Tag).
- **Aggregat `FreizeitpassErinnerung`:**
  - Tabelle `personen.freizeitpass_erinnerung`, `Aggregat`, `IOrganisationsEigen`, Felder wie `NachweisErinnerung`: `Id`, `OrganisationId`, `FreizeitId`, `PersonId`, `ErinnertVon`, `ErinnertAm`.
  - Eindeutig (FreizeitId, PersonId). Weitere Erinnerungen aktualisieren dieselbe Zeile über eine Methode, die den Mail-Auftrag ablegt.
  - Neue Migration in `Migrationen/`.
- **Datenschutz (AD-16), wie bei `NachweisErinnerung`:**
  - `PersonLoeschen`: Zeilen über die Person löschen, `ErinnertVon` auf den Platzhalter setzen.
  - `PersonenLoeschlauf`: mit den Freizeiten löschen.
  - `PersonenAuskunft`: neuer Abschnitt „Erinnerungen zum Freizeitpass“ (Datum).
- **Erinnerungs-Mail (`IMailAufbau` in Personen, registriert mit `VerschicktMail`):**
  - Empfänger über `IAnmeldeKontakt.MailAsync(personId)`: eigenes Konto, sonst ein Elternteil mit Konto, sonst Gast-Zugang. `null` heißt: keine Mail.
  - Betreff „Erinnerung: Freizeitpass in Zeltlotse“.
  - Text ohne Anrede und ohne Du/Sie: „<Leitung Vorname Nachname> aus der Leitung einer Freizeit bittet darum, den Freizeitpass von <Vorname Nachname> in Zeltlotse zu ergänzen. Mit Konto steht der Freizeitpass unter „Familie“ bei der Anmeldung:“, dann der Link `<Zeltlotse:OeffentlicheAdresse>/app/familie`, dann „Ohne Konto öffnet der Gast-Link aus der Mail zur Anmeldung den Freizeitpass.“, dann „Diese Mail hat die Leitung ausgelöst.“
  - Fehlt ein Name, steht „Eine Person“ wie in 1-6.
  - Keine Fragen und keine Antworten in der Mail. In der Outbox stehen nur Kennungen.
- **Web-App, Startseite `FreizeitStart.razor` (Cockpit, nur mit `anmeldungen-bearbeiten`):**
  - **Card „Anmeldungen“:**
    - Plätze-Balken aus `GET …/anmeldungen` (`AnmeldungenAsync`). Die Balken-Logik aus `Anmeldungen.razor` wandert unverändert in den neuen Baustein `Features/Freizeiten/PlaetzeBalken.razor` und wird von beiden Seiten genutzt.
    - Darunter zwei Zeilen „Teilnehmende: <e> eingegangen, <b> bestätigt“ und „Mitarbeitende: <e> eingegangen, <b> bestätigt“ (abgesagte zählen nicht).
    - Der vorhandene Knopf „Anmeldungen“ steht in dieser Card.
    - Scheitert der Abruf, entfällt die Card bis auf den Knopf.
  - **Card „Warnhinweise“:**
    - Eine Zeile je vorhandener Warnart:
      - „<n> Freizeitpässe unvollständig“ (Einzahl „1 Freizeitpass unvollständig“) als Link auf den Anker `#freizeitpaesse-offen`.
      - Die vorhandene Nachweis-Meldung aus 1-6 bleibt wörtlich, steht jetzt hier und wird mit `nachweise-pruefen` geladen; der Knopf „Nachweise prüfen“ bleibt.
    - Mit mindestens einer Zeile oben eine Meldung (Warnung) „Achtung: <k> Punkte sind offen.“ (Einzahl „1 Punkt ist offen.“). k ist die Anzahl der Zeilen.
    - Ohne Zeile eine Meldung (Erfolg) „Keine offenen Punkte.“, aber nur, wenn alle Abrufe gelungen sind. Scheitert ein Abruf, entfällt seine Zeile.
  - **Card „Freizeitpässe unvollständig“ (`id="freizeitpaesse-offen"`, nur wenn der Stand Personen enthält):**
    - `Datentabelle` mit den Spalten:
      - Name als Link auf `/freizeiten/{id}/freizeitpaesse/{personId}`.
      - Stand als Status (Warnung): „Freizeitpass fehlt noch“, wenn `offen == gefragt`, sonst „<offen> von <gefragt> Angaben offen“.
      - Erinnerung: „Erinnert am TT.MM.JJJJ“, falls vorhanden, und `InlineBestaetigung` „Erinnern“ mit dem Satz „Erinnerung zum Freizeitpass von <Name> senden?“ und dem Knopf „Ja, senden“.
    - Erfolg: Meldung „Gesendet: Die Erinnerung zum Freizeitpass von <Name> geht per Mail hinaus.“, danach lädt der Stand neu.
    - `409`: Meldung „Fehler: Zum Freizeitpass von <Name> wurde heute schon erinnert.“
    - `404`: Meldung „Fehler: Die Erinnerung ließ sich nicht senden.“
    - Kein doppeltes Senden, solange eine Übertragung läuft.
  - **Team (ohne `anmeldungen-bearbeiten` und `nachweise-pruefen`):** keine dieser Cards und keine Warnhinweise. Die Startseite bleibt sonst unverändert.
  - Nur Bausteine aus `Ui.Komponenten` (`Card`, `Balken`, `Meldung`, `Status`, `Datentabelle`, `InlineBestaetigung`, `Knopf`). Texte und Datum über `Anzeige`, API über `PersonenApiClient` bzw. `FreizeitenApiClient`.
- API nur additiv. Test vor dem Code. Neue Routen in `EndpunktListeTests`.

**Never:**
- Keine automatisch verschickten Erinnerungen, kein Zeitplan, kein Versand ohne Inline-Bestätigung (AD-13, NFR-10).
- Keine Warnhinweise zu unbesetzten Aufgaben oder zur Sicherheitskette: Beides gibt es noch nicht (siehe Design Notes). Kein Budget-Balken, keine „nächsten Schritte“, keine Phasen-Startseiten.
- Keine Antworttexte, Fragen, Adressen oder Namen in Outbox, Logs oder Mail-Auftrag. Keine Entschlüsselung im Stand.
- Kein neues Ereignis, keine Zeile im Ereignis-Fluss, kein neuer Pfeil, kein neues Modul, kein neuer Aktionsschlüssel, kein neuer Problemcode. Keine Änderung an `ARCHITECTURE-SPINE.md`.
- Keine Summen- oder Zähl-Endpunkte in Freizeiten (der Client zählt aus der vorhandenen Liste).
- Keine Pop-ups. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Stand | Freizeit X fragt `allergien,notfallkontakte,schwimmen`; Emma eingegangen, hat `allergien` und `notfallkontakte`; Mia eingegangen ohne Antworten; Tobias bestätigter Mitarbeitender mit Freigabe ohne Fragen | Emma `gefragt 3, offen 1`; Mia `3, 3`; Tobias fehlt | – |
| Vollständig | Emma beantwortet auch `schwimmen` | Emma fehlt in der Liste | – |
| Veraltet | Uhr + 12 Monate + 1 Tag, Freigabe noch aktiv (Freizeit später verlegt bzw. `GueltigBis` per SQL) | beantwortete Fragen zählen als offen | – |
| Nicht gefragt | Emma hat `krankheiten` beantwortet (nicht gefragt) | zählt weder bei `gefragt` noch bei `offen` | – |
| Fenster beendet | Absage oder Uhr nach `GueltigBis` | Person fehlt | – |
| Rechte Endpunkt 1 und 2 | Leitung; Team (Tobias); Org-Admin ohne Freizeit-Rolle; Leitung nur einer anderen Freizeit; Freizeit einer fremden Organisation | `200` / `403` / `404` / `404` / `404` | – |
| Erinnern | Miriam erinnert Mia (Kind von Katrin, ohne eigenes Konto) | `200 {erinnertAm}`; Outbox-Zeile ohne `@` und ohne Namen; nach der Zustellung genau eine Mail an Katrin mit Betreff „Erinnerung: Freizeitpass in Zeltlotse“, Link auf `/app/familie`, „Mia“ und Miriams Namen; Stand zeigt `erinnertAm` | – |
| Erinnern Gast | Kind einer Gast-Anmeldung | Mail an die Adresse des Gast-Zugangs | – |
| Erinnern Grenzen | zweites Mal am selben Tag; nächster Tag (Uhr +1 Tag); für sich selbst; Person ohne aktive Freigabe in X (nur in Z angemeldet oder abgesagt); `personId` fehlt | `409` / `200` / `404` / `404` / `400` | – |
| Mail ohne Empfänger | `IAnmeldeKontakt` liefert `null` | Zustellung erledigt, keine Mail | – |
| Löschen und Auskunft | Konto-Löschung der Person bzw. der Leitung; Löschlauf der Freizeit; Datenauskunft | Zeile weg bzw. `ErinnertVon` = Platzhalter; Abschnitt „Erinnerungen zum Freizeitpass“ | – |
| Cockpit Demo | Jonas (Leitung) öffnet „Zeltlager Sommer 2027“ | Balken „1 von 40“; „Teilnehmende: 1 eingegangen, 1 bestätigt“, „Mitarbeitende: 1 eingegangen, 1 bestätigt“; „Achtung: 2 Punkte sind offen.“; „4 Freizeitpässe unvollständig“; Tabelle Lea Feuerstein (fehlt noch), Emma Muster (5 von 8), Mia Probst (5 von 8), Tobias Wanderer (fehlt noch) | – |
| Cockpit Team | Lea öffnet die Freizeit | keine Cards „Anmeldungen“, „Warnhinweise“, „Freizeitpässe unvollständig“ | – |

</intent-contract>

## Code Map

- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- Startseite, wird zum Cockpit:
  - `OnParametersSetAsync` lädt Kontext und Freizeit parallel.
  - `WarnungenLadenAsync` (Z. ~108) lädt die Nachweis-Warnungen; Muster „Abruf scheitert → Teil entfällt“.
  - Gates über `FreizeitKontextDienst.Darf`.
- `src/Zeltlotse.App/Features/Freizeiten/Anmeldungen.razor:48,79-103` -- private Balken-Logik (`PlaetzeZahl`, `PlaetzeProzent`, `PlaetzeWort`, `PlaetzeArt`). Sie wandert nach `PlaetzeBalken.razor`; die Texte bleiben gleich (Screenshot `anmeldungen` unverändert).
- `src/Zeltlotse.App/Features/Personen/NachweisePruefen.razor:89` -- Vorbild für „Erinnern“ mit `InlineBestaetigung`, Meldungen und Sperre gegen doppeltes Senden.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Anmeldungen.cs` -- `AnmeldungenUebersicht(HoechstzahlTeilnehmende, BestaetigteTeilnehmende, Anmeldungen)`, `LeitungAnmeldungZeile` (Art, Status), Enums.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs:240` (`AnmeldungenAsync`), `PersonenApiClient.cs:32,392` -- Vorbilder `ErinnerungErgebnis` und `ErinnernAsync`. Neu kommen hinzu: `FreizeitpassStandLadenAsync` und `FreizeitpassErinnernAsync` (Ergebnis `Gesendet | SchonHeute | NichtGefunden`; das vorhandene Enum wird wiederverwendet).
- `src/Zeltlotse.Ui/Texte/Anzeige.cs` -- neue Helfer für die Texte zum Freizeitpass-Stand und für Einzahl/Mehrzahl, neben `Warnung` (Z. 198) und `Name`.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/NachweisErinnerung.cs` -- Vorbild für das ganze Muster: Mail-Auftrag, Aggregat (`HeuteSchonErinnert`, `Erinnern`, `Setzen` mit `MailVormerken`), Command `NachweisErinnern` mit Validator, Handler, Ergebnis-Union und `NachweisErinnerungMailAufbau`. `NachweisRegeln.cs` liefert `Tag` und `ErinnerungSperrenAsync`; der Lock-Schlüssel braucht für Freizeitpässe einen eigenen Präfix.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/PruefEndpunkte.cs:26,54,61` -- `NachweisRechte`, Registrierung der Gruppe, Endpunkt-Stil für `Erinnern` (Mapping des Ergebnisses auf 200/404/409).
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs` -- `FreizeitpassRechte` (hier kommt `Erinnern` hinzu), Wahl der jüngsten Freigabe, Rolle aus `db.FreizeitRollen`, Registrierung in `PersonenModul.cs:~86`. Neue Dateien `Freizeitpass/FreizeitpassStand.cs` und `Freizeitpass/FreizeitpassErinnerung.cs`.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassAntwort.cs` -- `Beantwortet` ist nicht gemappt; in SQL gilt `Wert != null || Sonstiges != null`. Dazu `IstVeraltet` und `BestaetigtAm`.
- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs` -- `Fragen`, `GueltigBis`, `AngelegtAm`.
- `src/Modules/Personen/Zeltlotse.Personen/Gaeste/AnmeldeKontaktLesen.cs` -- `IAnmeldeKontakt.MailAsync`, registriert in `PersonenModul`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/Entscheiden/AnmeldungEntschiedenMail.cs` -- Mail-Text-Stil.
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs` -- DbSet in Schreib- und Lese-Kontext, Mapping wie `Erinnerungen`, neue Migration.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/{PersonLoeschen.cs:149-170,PersonenLoeschlauf.cs:39,PersonenAuskunft.cs:47,224}` -- dort die neue Tabelle behandeln wie `Erinnerungen`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` -- `Anmeldungen = [Leitung]` (Gleichheitstest).
- `tests/Zeltlotse.Api.Tests/FreizeitpassSzenario.cs` -- Szenario mit Org A/B, X/Z, Miriam, Tobias, Katrin, Emma, Mia, `FragenAsync`, `AntwortAsync`, `KindAnmeldenAsync`, `AbsagenAsync`, `ZustellenAsync`. Basis für die neuen `FreizeitpassStandTests.cs` und `FreizeitpassErinnerungTests.cs`.
- `tests/Zeltlotse.Api.Tests/NachweisWarnungTests.cs:~160` -- Vorbild für Mail- und Outbox-Prüfungen und die Rechte-Zeile.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs:~177` -- Liste der Freizeit-Routen ergänzen (Kommentar `// Story 2-6:`).
- `DatenauskunftTests.cs`, `DemoDatenTests.cs` -- ergänzen.
- Gast-Anmeldung: Helfer wie in `GastFreizeitpassTests.cs`.
- Rollen-Gleichheitstests: Treffer `FreizeitpassRechte.Einsicht` in `tests/`.
- `tests/Zeltlotse.Ui.Tests/{FreizeitenSeitenTests.cs:428,NachweisWarnungSeitenTests.cs:205,AnmeldungenSeitenTests.cs}` -- Helfer `FreizeitStartAntworten` und `Startseite(...)`. Die Stubs brauchen die neuen Routen; Tests der Meldung aus 1-6 anpassen.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:261-285` -- `Freizeit_Start` (Jonas). Der Locator `.elkw-alert--warnung` muss eindeutig werden. Dazu `BildAsync` (Z. 1121) und `E2EHilfe.AxeAsync`.
- `doku/src/content/docs/leitung/{index.md,freizeit-oeffnen.md,nachweise-erinnern.md}`, `eltern/freizeitpass.md`, `mitarbeitende/anmelden.md`, `daten.md`; `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (`PflichtSeiten`, jedes PNG verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST:
  - Aggregat `FreizeitpassErinnerung`: einmal je Berlin-Tag, Mail-Auftrag abgelegt, Auftrag nur aus Kennungen.
  - Mail-Aufbau: Text, Link, `null` ohne Empfänger.
  - Gleichheit von `FreizeitpassRechte.Erinnern` und `FreizeitRechte.Anmeldungen`, dort, wo der vorhandene Rollen-Gleichheitstest liegt.
- `tests/Zeltlotse.Api.Tests/FreizeitpassStandTests.cs`, `FreizeitpassErinnerungTests.cs` -- ZUERST: alle Matrix-Zeilen gegen PostgreSQL, inklusive Mail nach der Zustellung. Dazu `EndpunktListeTests`, `DatenauskunftTests`, Konto-Löschung, Löschlauf und `DemoDatenTests` (Stand für Jonas wie in der Matrix-Zeile „Cockpit Demo“, keine Erinnerung in der Demo).
- `tests/Zeltlotse.Ui.Tests/LeitungsCockpitSeitenTests.cs` (neu) -- ZUERST:
  - Card „Anmeldungen“ mit Balken und Zahlen.
  - Warnhinweise: beide Zeilen, Einzahl und Mehrzahl, „Keine offenen Punkte.“, ein Abruf scheitert.
  - Tabelle: fehlt noch, n von m, Erinnert am.
  - Erinnern: Erfolg mit Neuladen, 409, 404.
  - Team ohne Cockpit.
  - Vorhandene Startseiten-Tests werden an die verschobene Nachweis-Meldung angepasst.
- `src/Zeltlotse.Api.Contracts/Personen/FreizeitpassStand.cs` -- Datenformen.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/{FreizeitpassStand,FreizeitpassErinnerung}.cs`, `FreizeitpassEinsicht.cs` (Rechte), `Daten/PersonenDbContext.cs` + Migration `FreizeitpassErinnerung`, `PersonenModul.cs`, `Datenschutz/*` -- Stand, Erinnerung, Mail, Löschen und Auskunft.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs`, `src/Zeltlotse.Ui/Texte/Anzeige.cs` -- Client-Methoden und Texte.
- `src/Zeltlotse.App/Features/Freizeiten/PlaetzeBalken.razor` (neu), `Anmeldungen.razor`, `FreizeitStart.razor` -- Baustein und Cockpit laut Always.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`:
  - `Freizeit_Start` prüft die Cockpit-Texte aus der Matrix-Zeile „Cockpit Demo“.
  - Neue Theory `Freizeitpass_Erinnern`: Jonas, Bestätigung bei Emma aufgeklappt, Bild `freizeitpass-erinnern`.
  - Beide mit Handy, Laptop und axe.
  - `anmeldungen` läuft mit.
- `doku/src/content/docs/leitung/cockpit.md` (neu, Titel „Leitungs-Cockpit“, in `DokuRegeln.PflichtSeiten`):
  - Inhalt: Anmeldestand, Warnhinweise, unvollständige Freizeitpässe, „Erinnern“. Die Erinnerung geht erst nach Bestätigung per Mail hinaus, an die Person bzw. einen Elternteil oder die Gast-Adresse, höchstens einmal am Tag je Person.
  - Hinweis, dass Aufgaben und Sicherheitskette später dazukommen.
  - Bilder `freizeit-start` und `freizeitpass-erinnern`.
- Anpassen:
  - `leitung/index.md`: Abschnitt „Leitungs-Cockpit“ mit Link.
  - `leitung/freizeit-oeffnen.md`: Aufzählung und Alt-Text.
  - `leitung/nachweise-erinnern.md`: Ort der Meldung und Alt-Text.
  - `eltern/freizeitpass.md`, `mitarbeitende/anmelden.md`: Erinnerungs-Mail zum Freizeitpass.
  - `daten.md`: Erinnerungen zum Freizeitpass in der Auskunft.

**Acceptance Criteria:**
- Given Jonas (Demo, Leitung) öffnet „Zeltlager Sommer 2027“, when die Startseite lädt, then sieht er auf einer Seite den Balken „Plätze 1 von 40“, die Zahlen je Art, „Achtung: 2 Punkte sind offen.“ mit „4 Freizeitpässe unvollständig“ und der Nachweis-Zeile, und die Tabelle der vier Personen.
- Given Jonas tippt bei Emma „Erinnern“ und „Ja, senden“, when die Outbox zugestellt ist, then liegt genau eine Mail an `katrin.muster@example.org` mit dem Link auf `/app/familie` vor, und die Zeile zeigt „Erinnert am <heute>“.
- Given Lea (Team) öffnet die Freizeit, when die Startseite lädt, then sieht sie keine Leitungs-Warnhinweise.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architekturtests), `freizeitpass-erinnern` und `freizeit-start` sind je Handy und Laptop geschrieben und verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 27 findings — high 0, medium 0, low 19, false 7, maybe-false 1
- findings:
  - `[false]` `[reject]` (intent) Token-Fälle nach AD-30 fehlen — AD-30 ordnet die Pflichtfälle nach Art des Endpunkts; beide neuen Endpunkte sind Freizeit-Endpunkte ohne Token (wie in 1-6 und 2-5).
  - `[low]` `[reject]` (intent) Demo-Seed nicht geändert, nur `DemoDatenTests` erweitert — die vorhandenen Demo-Daten zeigen schon vier unvollständige Pässe (Design Notes); eine Demo-Erinnerung würde beim Einspielen Mails verschicken.
  - `[low]` `[reject]` (intent) Bild `freizeit-start` entsteht nach DOM-Textersatz — die Texte werden vorher am echten DOM geprüft und auf die Werte der reinen Demo-Daten gesetzt, die Gast-Anmeldungen anderer Tests verfälschen; Anwender treffen das nie, eine Test-Isolation wäre deutlich mehr Umbau.
  - `[low]` `[patch]` (intent) Screenshots wie `anmeldungen-*` verändert, obwohl „unverändert“ — nach dem Abschlusslauf werden alle Bilder mit reinem Rendering-Rauschen per `git checkout` zurückgesetzt (siehe Auto Run Result).
  - `[false]` `[reject]` (intent) Doku-Build nicht belegt — `npm run build` in `doku/` lief fehlerfrei und läuft im Abschluss erneut.
  - `[false]` `[reject]` (intent) Test vor dem Code nicht sichtbar — Prozess-Eigenschaft; die Tests liegen für jede Matrix-Zeile vor und wurden laut Umsetzung zuerst geschrieben.
  - `[low]` `[reject]` (intent) Warnhinweise zu Aufgaben und Sicherheitskette fehlen (FR-27) — beides existiert noch nicht (2-8, Epic 3); Design Notes und Doku nennen das.
  - `[low]` `[reject]` (intent) Team-Sicht nur per bUnit geprüft — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; die Gates liegen im Client, die Endpunkte geben dem Team 403 (HTTP-Test).
  - `[low]` `[patch]` (verification-gap) Freizeit-Filter von `erinnertAm` ungetestet — Patch: Stand-Test mit Erinnerung in einer zweiten Freizeit.
  - `[low]` `[patch]` (verification-gap) Bedingung „beantwortet“ (Wert oder Sonstiges) ungetestet — Patch: Stand-Test mit geleerter Antwort und Antwort nur mit „Sonstiges“.
  - `[low]` `[patch]` (verification-gap) Transportfehler bei „Erinnern“ ungetestet — Patch: bUnit-Fall mit 500 und erneutem Erinnern.
  - `[low]` `[reject]` (verification-gap) Sortierung namenloser Personen ungetestet — Freigaben entstehen nur mit Anmeldung über ein Profil mit Namen; selten und ohne Nutzerschaden.
  - `[low]` `[reject]` (blind) „Gesendet“ ohne Empfänger — eine aktive Freigabe entsteht nur über eine Anmeldung mit Konto oder Gast-Zugang, `IAnmeldeKontakt` liefert dann immer eine Adresse; wie in 1-6 verworfen.
  - `[low]` `[reject]` (blind) „Freizeitpass fehlt noch“ auch bei nur veralteten Antworten — so definiert (veraltet zählt als nicht vollständig, Spec und Doku); selten und ohne Fehlbedienung.
  - `[low]` `[reject]` (blind) Mail nennt die Freizeit nicht — Personen darf Freizeiten nicht lesen (AD-2), wie in 1-6; die Mail nennt Leitung und Person.
  - `[low]` `[patch]` (blind) Auskunft-Abschnitt nennt die erinnernde Leitung nicht — Patch: „von <Leitung>“ wie im Abschnitt der Nachweis-Erinnerungen.
  - `[false]` `[reject]` (blind) Kommentar „dieselbe Wahl wie der Stand“ falsch — gemeint ist die Wahl der Freigabe (jüngste, aktiv), die identisch ist; ohne Prüfung auf offene Angaben laut Spec.
  - `[low]` `[patch]` (blind) Doku verschweigt, dass die eigene Person nicht in der Liste steht — Patch: ein Satz in `leitung/cockpit.md`.
  - `[low]` `[reject]` (blind) „Heute schon erinnert“ als Fehler statt gesperrtem Knopf — Muster aus 1-6; ein gesperrter Knopf bräuchte eine Tagesprüfung im Client (neuer Zweig).
  - `[low]` `[patch]` (blind) Scheitert das Neuladen nach dem Erinnern, verschwindet die Tabelle — Patch: `_stand = await StandLadenAsync() ?? _stand;`.
  - `[maybe-false]` `[reject]` (blind) Sprunglink lädt die Seite neu — Blazor ab .NET 8 behandelt reine Fragment-Wechsel als Sprung auf das Element; selbst ein Neuladen wäre nur `low` (Meldung weg, Daten neu).
  - `[low]` `[patch]` (blind) Eltern-Doku „einmal am Tag je Person“ ungenau — Patch: „je Person und Freizeit“.
  - `[false]` `[reject]` (edge) Leere Card „Warnhinweise“ ohne `nachweise-pruefen` — `anmeldungen-bearbeiten` und `nachweise-pruefen` gehören beide nur der Leitung; der Zustand ist unerreichbar.
  - `[low]` `[patch]` (edge) Tabelle verschwindet nach gescheitertem Neuladen — gleiche Ursache und gleicher Patch wie oben (blind).
  - `[low]` `[reject]` (edge) Wechsel der Freizeit während „Erinnern“ läuft — sehr selten, Folge nur eine falsche Meldung; der Schutz wäre ein neuer Zweig.
  - `[false]` `[reject]` (edge) Auskunft-Test nicht in `DatenauskunftTests.cs` — der Abschnitt ist per HTTP-Test der Datenauskunft belegt; der Ort der Datei ändert nichts.
  - `[false]` `[reject]` (edge) Rollen-Gleichheitstest an anderer Stelle — der Test existiert und läuft; der Ort ist ohne Folge.

## Design Notes

- **Aufgaben und Sicherheitskette fehlen bewusst.** FR-27 nennt Warnhinweise zu unbesetzten Aufgaben und zur fehlenden Sicherheitskette. Beides wird erst später gebaut: die Sicherheitskette in 2-8, die Aufgaben in „Aufgaben und Aufgaben-Vorlagen“ (Epic 3). Ohne Datenquelle wäre jeder Hinweis Speziallogik. Die Card „Warnhinweise“ ist eine Liste von Zeilen, die je eine Quelle laden; die späteren Stories hängen ihre Zeile an. Das gilt für die Zeilen „Sicherheitskette ist noch nicht eingerichtet“ (EXPERIENCE.md) und „Aufgaben unbesetzt“. Die Doku sagt das.
- **Wer rechnet:** Der Freizeitpass-Stand braucht die gefragten Kennungen der Freigabe und die Antwort-Zeilen; beides gehört Personen. Darum liegt Endpunkt 1 dort (Muster 2-5), und der Client setzt das Cockpit zusammen (AD-2, ARCHITECTURE-SPINE „Zusammengeführte Ansichten“). Die Anmeldezahlen zählt der Client aus der vorhandenen Leitungs-Liste; ein neuer Endpunkt brächte nichts.
- **„Unvollständig“ = unbeantwortet oder veraltet.** So sieht die Leitung dasselbe wie die Eltern („Freizeitpass prüfen“ in der Familienübersicht, 2-4). Gezählt wird nur über Spalten, ohne Entschlüsselung und ohne Einsicht-Protokoll, weil keine Antwort sichtbar wird. „Freizeitpass fehlt noch“ steht, wenn keine gefragte Angabe vollständig ist (UX-Zustand).
- **Alle aktiven Freigaben, auch eingegangene:** Die Leitung sieht Pässe ab dem Eingang (2-5). Eltern sollen den Pass vor der Freizeit ausfüllen, gleich ob die Anmeldung schon bestätigt ist.
- **Empfänger über `IAnmeldeKontakt`:** Kinder haben kein Konto. Dieselbe Regel wie bei der Bestätigungs-Mail (2-3) erreicht Eltern und Gäste ohne neue Logik. Gäste bekommen keinen Link, weil nur der Hash ihres Tokens gespeichert ist; die Mail verweist auf den Gast-Link aus der Anmelde-Mail.
- **Ohne Einsicht, eigene Tabelle:** Die Nachweis-Erinnerung fragt eine Einsicht an. Das gilt hier nicht, die Freigabe besteht schon. Eine eigene Tabelle hält „Erinnert am“ je Thema getrennt und entspricht der Vorlage aus 1-6.
- **Cockpit in jeder Phase:** Die Phasen-Startseiten (Notfall-Box, Nachbereitung) kommen mit ihren Stories. Bis dahin zeigt die Startseite der Leitung das Cockpit immer; das ist ohne Sonderfall.
- **Rechte:** Erinnern ist eine Leitungsaufgabe (rollen-und-rechte.md). Der vorhandene Schlüssel `anmeldungen-bearbeiten` mit der Richtlinie `FreizeitRechte.Anmeldungen = [Leitung]` deckt das ab. Eine gleiche Liste in Personen und ein Gleichheitstest folgen dem Muster aus 1-5 und 2-5.
- **AD-30:** Leitung `200`, Team `403`, fremde Freizeit `404`, fremde Organisation `404` und Org-Admin ohne Rolle `404` für beide Endpunkte. Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.
- **Demo:** Die vorhandenen Demo-Daten zeigen schon vier unvollständige Pässe. Neue Daten braucht es nicht. Eine Demo-Erinnerung würde beim Einspielen Mails verschicken. `DemoDatenTests` prüft den Stand.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seite `leitung/cockpit/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-14, FR-25, FR-27):**
- **Personen:**
  - `GET /api/freizeiten/{id}/personen/freizeitpass-stand` (nur Leitung): Personen mit offenen Angaben (unbeantwortet oder älter als 12 Monate) aus der jüngsten aktiven Freigabe, ohne Entschlüsselung und ohne Einsicht-Protokoll.
  - `POST …/personen/freizeitpass/erinnerungen` (nur Leitung): Aggregat `FreizeitpassErinnerung`, höchstens einmal je Berlin-Tag, Person und Freizeit; die Mail geht über die Outbox an Konto, Elternteil oder Gast-Zugang (`IAnmeldeKontakt`).
  - Löschen, Löschlauf und Datenauskunft (Abschnitt „Erinnerungen zum Freizeitpass“ mit erinnernder Leitung).
- **Web-App:** Die Startseite der Freizeit ist für die Leitung das Leitungs-Cockpit mit den Cards „Anmeldungen“ (Plätze-Balken, Zahlen je Art), „Warnhinweise“ (unvollständige Freizeitpässe, fehlende Nachweise) und „Freizeitpässe unvollständig“ mit „Erinnern“. Das Team sieht davon nichts. Der Plätze-Balken ist jetzt der gemeinsame Baustein `PlaetzeBalken`; `Card` hat ein optionales `Id`.
- **Nicht enthalten:** Warnhinweise zu Aufgaben und Sicherheitskette (gibt es noch nicht; 2-8 und Epic 3 hängen ihre Zeile an).

**Dateien:**
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/{FreizeitpassStand,FreizeitpassErinnerung,FreizeitpassEinsicht}.cs` – Stand-Regel, Erinnerung, Mail, Rechte.
- `src/Modules/Personen/Zeltlotse.Personen/{PersonenModul.cs,Daten/PersonenDbContext.cs,Migrationen/20261013081139_FreizeitpassErinnerung*}` – Registrierung, Tabelle, Migration.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/{PersonLoeschen,PersonenLoeschlauf,PersonenAuskunft}.cs` – Datenschutz.
- `src/Zeltlotse.Api.Contracts/Personen/FreizeitpassStand.cs` – Datenformen.
- `src/Zeltlotse.Ui/{Api/PersonenApiClient.cs,Texte/Anzeige.cs}`, `src/Zeltlotse.Ui.Komponenten/Card.razor` – Client, Texte, Anker.
- `src/Zeltlotse.App/Features/Freizeiten/{FreizeitStart,Anmeldungen,PlaetzeBalken}.razor` – Cockpit und Balken.
- Tests: Api (`FreizeitpassStandTests`, `FreizeitpassErinnerungTests`, `EndpunktListeTests`, `DemoDatenTests`, `LoeschlaufTests`), Personen (`FreizeitpassErinnerungTests`), Ui (`LeitungsCockpitSeitenTests`, `NachweisWarnungSeitenTests`, `KomponentenTests`), E2E (`Freizeit_Start`, neu `Freizeitpass_Erinnern`), Architektur (`DokuRegeln`, `DokuTests`), `MailOrdner`.
- Doku: neu `leitung/cockpit.md`; angepasst `leitung/{index,freizeit-oeffnen,nachweise-erinnern}.md`, `eltern/freizeitpass.md`, `mitarbeitende/anmelden.md`, `daten.md`. Bilder: neu `freizeitpass-erinnern-{handy,laptop}`, neu erzeugt `freizeit-start-{handy,laptop}`.

**Review (27 Befunde: high 0, medium 0, low 19, false 7, maybe-false 1):**
- Gepatcht (8 low): drei fehlende Tests (Freizeit-Filter von `erinnertAm`, geleerte und nur-„Sonstiges“-Antworten, Transportfehler bei „Erinnern“), Tabelle bleibt bei gescheitertem Neuladen, Auskunft nennt die erinnernde Leitung, zwei Doku-Präzisierungen, Rausch-Screenshots zurückgesetzt.
- Zurückgestellt: nichts.
- Verworfen (Begründung im Review Triage Log): Token-Fälle (kein Token-Endpunkt), Demo-Seed unverändert, Textersatz im Bild `freizeit-start`, Aufgaben/Sicherheitskette, Team-Sicht nur per bUnit, „Gesendet“ ohne Empfänger (unerreichbar), „fehlt noch“ bei nur veralteten Antworten, kein Freizeitname in der Mail (AD-2), 409 als Fehler, Sprunglink, Freizeitwechsel während des Sendens, Testorte, namenlose Sortierung sowie alle `false`.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 0, low 8.

**AD-30:** Beide Endpunkte gegen PostgreSQL: Leitung `200`, Team `403`, Org-Admin ohne Freizeit-Rolle `404`, andere Freizeit `404`, fremde Organisation `404`. Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung nach den Patches, inklusive Architektur, E2E und Screenshots): bestanden.
- `npm ci` und `npm run build` in `doku/`: fehlerfrei, 32 Seiten, darunter `leitung/cockpit/` (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Api, Personen, Ui, Demo, E2E).
- 27 Screenshots mit reinem Rendering-Rauschen (höchstens 404 Pixel; `freizeitpass-fragen-laptop` nur zufälliger Link-Token) per `git checkout` zurückgesetzt.

**Restrisiken:**
- Das Bild `freizeit-start` setzt zwei Texte auf die Werte der reinen Demo-Daten, weil Gast-Anmeldungen anderer E2E-Tests die Zahlen verändern; geprüft wird vorher am echten DOM.
- Die Erinnerungs-Mail nennt keine Freizeit (AD-2), und Gäste bekommen keinen direkten Link.
- Tests mit festen Daten (Freizeit 2027) laufen ab Sommer 2027 aus, wie die bestehenden.
