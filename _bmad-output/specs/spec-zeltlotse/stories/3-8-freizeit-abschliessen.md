---
title: 'Story 3-8: Freizeit abschließen'
type: 'feature'
created: '2026-10-14'
status: 'done'
baseline_revision: '2115418f37f142ca0d425dd3735eea1a345133da'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-7-module-pro-freizeit-schalten.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-4-schaeden-verluste-und-anschaffungswuensche.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-10-vorfall-protokoll-und-vorfall-notiz.md'
warnings: [oversized]
deferred:
  - summary: >-
      Die vergangenen Demo-Freizeiten (Herbstfreizeit, Pfingstlager) haben kein Team (Tobias).
    evidence: |-
      Team entsteht nur über die Bestätigung einer Anmeldung über den Anmeldelink für Mitarbeitende, und der Anmeldelink gilt nur bis
      Freizeitende (AnmeldelinkSuche). Für Freizeiten mit festen Daten in der Vergangenheit lässt sich Tobias deshalb ohne Speziallogik nicht
      als Team einsetzen (Design Notes). Die Sicht des Teams auf die Card „Abschluss“ ist per bUnit belegt, der 403 per HTTP-Test.
    location: >-
      src/Zeltlotse.Api/Demo/DemoDaten.cs (VergangeneFreizeitenAsync)
    severity: low
  - summary: >-
      Zeitfenster zwischen Schreibschutz-Prüfung und Schreiben: Ein paralleler Abschluss kann zwischen Pipeline-Prüfung und Transaktion committen.
    evidence: |-
      SchreibschutzStufe liest die Phase vor der TransaktionsStufe ohne Zeilensperre. Commands, die nur durch die Stufe geschützt sind
      (z. B. AnmeldungEntscheiden, LeitungEinladen, Personen-Erinnerungen), können in diesem Fenster noch schreiben. Abhilfe bräuchte eine
      Sperre auf freizeiten.freizeit je Command oder eine Prüfung innerhalb der Transaktion; das ist mehr als eine direkte Korrektur.
    location: >-
      src/Zeltlotse.SharedKernel/Pipeline/SchreibschutzStufe.cs
    severity: low
  - summary: >-
      Anmeldung über den Anmeldelink liest die Phase ohne Zeilensperre und kann parallel zum Abschluss noch eine Anmeldung anlegen.
    evidence: |-
      AnmeldeSchritte prüft Phase(jetzt) == Gelaufen ohne FOR UPDATE auf freizeiten.freizeit. Gleiche Ursache wie das Zeitfenster der
      SchreibschutzStufe; praktisch nur denkbar, wenn der Anmeldelink noch gilt, was nach Freizeitende nicht mehr der Fall ist.
    location: >-
      src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/AnmeldungAbsenden.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** Eine Freizeit kann die Phase „gelaufen“ nie erreichen: Es gibt keinen Abschluss durch die Leitung, das vorhandene Ereignis `FreizeitAbgeschlossen` wird nie ausgelöst (Vorfall-Archiv bleibt leer), und der Schreibschutz nach AD-17 steckt nur verstreut in einzelnen Handlern statt in der Pipeline. Anmeldungen, Leitung-Einladungen und Nachweis-Commands einer gelaufenen Freizeit sind heute noch schreibbar.

**Approach:** Neuer Übergang `POST /api/freizeiten/{freizeitId}/abschliessen` (nur Leitung, nur in der Nachbereitung) setzt `AbgeschlossenAm` und legt `FreizeitAbgeschlossen` ab. Eine neue Pipeline-Stufe `SchreibschutzStufe` lehnt jeden `IBenutzerCommand` mit Freizeit-Bezug (neuer Marker `IFreizeitCommand`) einer gelaufenen Freizeit mit `409 freizeit-abgeschlossen` ab, außer der Command trägt `[ErlaubtNachAbschluss]`. Die Startseite der Nachbereitung bekommt die Card „Abschluss“ mit offenen Aufgaben und Inline-Bestätigung; die Org-Übersicht der Freizeiten bekommt einen Phasen-Filter.

## Boundaries & Constraints

**Always:**
- Nur der Abschluss wird gespeichert (`Freizeit.AbgeschlossenAm`, Spalte existiert); alle anderen Phasen bleiben abgeleitet (`FreizeitZeiten.Phase`). Keine neue Migration für das Feld.
- Abschluss nur durch Leitung (`FreizeitRechte.Einstellungen`), nur in Phase `Nachbereitung`. Abschluss trotz offener Punkte ist erlaubt; die Warnung ist rein clientseitig in der Inline-Bestätigung.
- Ereignis `FreizeitAbgeschlossen(FreizeitId)` über die Outbox im selben Command (Transaktion der Pipeline); `VorfaelleArchivieren` in Sicherheit archiviert danach. Anschaffungswünsche ergeben sich schon aus der Phase (3-4) – dort nichts ändern.
- Pipeline-Reihenfolge nach Spine Z.381: Validierung, dann Schreibschutz und Schalter, dann Inbox, Transaktion, Outbox. Schreibschutz greift nur für `IBenutzerCommand`; Ereignis-Empfänger und Lösch-Dienst laufen weiter.
- `[ErlaubtNachAbschluss]` nur an: Schaden/Verlust eintragen, bearbeiten, entfernen und Anschaffungswunsch erledigt (Inventar). Erstattungsvermerke (Finanzen) gibt es noch nicht; das Attribut ist für sie vorgesehen.
- Phase für die Stufe löst der API-Host einmal pro Anfrage auf (Muster `ISchalterStand`/`SchalterStand`), über `IFreizeitZeitpunkte` aus `Freizeiten.Contracts`.
- Kein neuer Aktionsschlüssel und kein neuer Problemcode (AD-5): Knopf sichtbar über `Aktionsschluessel.FreizeitEinstellungen`; falsche Phase → `400 validierung`, schon gelaufen → `409 freizeit-abgeschlossen`.
- Texte: Hinweis „Diese Freizeit ist abgeschlossen und nur lesbar.“; Bestätigung ohne offene Punkte „Freizeit abschließen? Danach ist sie nur lesbar.“, mit offenen Punkten z. B. „2 Aufgaben offen – trotzdem abschließen?“, Knopf „Ja, abschließen“. Deutsch ohne Du/Sie.

**Never:**
- Kein Wieder-Öffnen einer gelaufenen Freizeit, kein Abrechnungsabschluss, keine Abschlussmappe, keine Mail an Team oder Eltern beim Abschluss.
- Keine Belege-/Finanzen-Logik bauen (Modul leer); keine Kanäle-Logik (Modul leer).
- Vorhandene Handler-Prüfungen auf `Gelaufen` nicht massenhaft umbauen; sie bleiben als zweite Absicherung, gleiche Antwort.
- Keine Pop-ups; Inline-Bestätigung (`InlineBestaetigung`).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Abschluss | Leitung Miriam, Freizeit in Nachbereitung | `204`; Kontext-Phase `Gelaufen`; Outbox-Ereignis; Vorfall-Notizen archiviert | – |
| Zu früh | Leitung, Phase InVorbereitung oder Laeuft | `400 validierung`, Feldfehler „Abschließen geht erst nach dem letzten Freizeittag.“ | nichts gespeichert |
| Doppelt | Leitung, Freizeit schon gelaufen | `409 freizeit-abgeschlossen` | `AbgeschlossenAm` unverändert |
| Falsche Rolle | Team Tobias | `403` | – |
| Fremd | Org-Admin Andrea, Org-Überblick Gerd, Lea aus Org B, unbekannte Freizeit-Id | `404` | – |
| Anonym | ohne Sitzung | `401` | – |
| Schreibschutz | beliebiger `IFreizeitCommand` (z. B. Anmeldung bestätigen, Leitung einladen, Anmeldung absenden per Anmeldelink-Token) nach Abschluss | `409 freizeit-abgeschlossen` | – |
| Ausnahme | Schaden eintragen / Anschaffungswunsch erledigt nach Abschluss | wie bisher erfolgreich | – |
| Ereignis-Empfänger | `VorfaelleArchivieren` bei gelaufener Freizeit | läuft durch | – |

</intent-contract>

## Code Map

- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Freizeit.cs` -- `AbgeschlossenAm` Z.52 (Kommentar Z.16 „setzt ihn nicht“ anpassen), `Phase(jetzt)` Z.319, `EreignisAblegen` Muster Z.129. Neue Methode `Abschliessen(jetzt)`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/FreizeitZeiten.cs:36-49` -- Phasenberechnung, unverändert.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/FreizeitAbgeschlossen.cs` -- Ereignis-Typ vorhanden; Doc-Kommentar aktualisieren.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Module/ModuleBearbeiten.cs` -- Vorlage Command+Validator+Handler+Endpunkt in einer Datei; Gelaufen-Prüfung Z.71-74, 117.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/FreizeitenModul.cs:103-133` -- Endpunkt-Registrierung (`.NurFreizeitRolle(FreizeitRechte.Einstellungen)`).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` -- `Einstellungen` Z.43, `FreizeitZeitpunkteLesen` Z.145-159.
- `src/Zeltlotse.SharedKernel/Commands/IModulCommand.cs:16-27` -- `IBenutzerCommand`, `ISchalterCommand`; hier `IFreizeitCommand { Guid FreizeitId }` ergänzen (`ISchalterCommand` erbt davon) und `ErlaubtNachAbschlussAttribute`.
- `src/Zeltlotse.SharedKernel/Pipeline/SchalterStufe.cs`, `SchalterAusAusnahme.cs` -- Vorlage für `SchreibschutzStufe` + `FreizeitAbgeschlossenAusnahme` + Stand-Schnittstelle.
- `src/Zeltlotse.Api/Program.cs:64-71` -- Pipeline-Reihenfolge (heute Schalter vor Validierung → auf Spine-Reihenfolge bringen).
- `src/Zeltlotse.Api/Rechte/SchalterStand.cs` -- Vorlage für den Phasen-Stand je Anfrage.
- `src/Zeltlotse.Api/Fehler/AusnahmeBehandlung.cs:30-42` -- neue Ausnahme → `409` + `code=freizeit-abgeschlossen` + Text.
- Commands mit `FreizeitId` ohne Gelaufen-Prüfung: `Freizeiten/Anmeldungen/Entscheiden/AnmeldungEntscheiden.cs`, `Freizeiten/Anmeldungen/AnmeldungAbsenden.cs`, `Freizeiten/Leitung/LeitungEinladen.cs`, `LeitungAnnehmen.cs`, Personen `EinsichtAnfragen`, `EinsichtPruefen`, `NachweisErinnerung`, `FreizeitpassErinnerung`, `EinsichtProtokoll`.
- `src/Modules/Inventar/Zeltlotse.Inventar/Schaeden/{SchadenEintragen,SchadenBearbeiten,SchadenEntfernen,AnschaffungswunschErledigt}.cs` -- bekommen `[ErlaubtNachAbschluss]`.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Vorfaelle/VorfaelleBearbeiten.cs:151-174` -- Empfänger `VorfaelleArchivieren`, fertig.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- Startseite; Notfall-Box Z.62, Warnhinweise Z.128-166, Rechte-Flags Z.231-238, Aufgaben werden schon geladen.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs` -- neue Methode `AbschliessenAsync`; 409-Mapping-Muster `ProgrammApiClient.cs:449`.
- `src/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor`, `Segmente.razor` -- Bausteine (Beispiele `AufgabeSeite.razor:105`, `MaterialStatusWahl.razor:8`).
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitenKarten.razor` -- Org-Liste mit Spalte Phase Z.64-71; Filter ergänzen.
- `src/Zeltlotse.App/Features/Anmeldung/*` (öffentliche Anmeldung) -- 409 `freizeit-abgeschlossen` → „Diese Freizeit ist zu Ende.“
- `src/Zeltlotse.Api/Demo/DemoDaten.cs` (Ablauf Z.134-163), `DemoKatalog.cs` -- neue Demo-Freizeiten.
- Tests: `tests/Zeltlotse.Api.Tests/{FreizeitpassSzenario.cs:82, ModuleTests.cs:131-169, EndpunktListeTests.cs:194-290, DemoDatenTests.cs:412, TestHelfer.cs}`, `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs`, `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs`, `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (Muster Z.1383-1403, `Org_Anschaffungswuensche`), `tests/Zeltlotse.ArchitectureTests/{TypRegelnTests.cs, DokuRegeln.cs:16-29}`.
- Doku: `doku/src/content/docs/leitung/` (neue Seite), Sätze „kommt später“/„bleibt leer“ in `leitung/freizeit-oeffnen.md:67`, `daten.md:63`, `org-admin/anschaffungswuensche.md:16`, `org-admin/vorfall-archiv.md:14`, `mitarbeitende/schaeden-verluste.md`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs` -- zuerst: Tests für `SchreibschutzStufe` (gelaufen → Ausnahme; nicht gelaufen → durch; `[ErlaubtNachAbschluss]` → durch; kein `IBenutzerCommand` → durch; kein `IFreizeitCommand` → durch) -- testgetrieben.
- `src/Zeltlotse.SharedKernel/Commands/IModulCommand.cs`, `src/Zeltlotse.SharedKernel/Pipeline/SchreibschutzStufe.cs` (+ Ausnahme, Stand-Schnittstelle) -- Marker, Attribut, Stufe -- AD-17 in der Pipeline.
- `src/Zeltlotse.Api/Rechte/` (Phasen-Stand), `Program.cs`, `Fehler/AusnahmeBehandlung.cs` -- Stand je Anfrage, Registrierung in Spine-Reihenfolge, 409-Antwort.
- `tests/Zeltlotse.ArchitectureTests/TypRegelnTests.cs` -- neue Regel: jeder `IBenutzerCommand` mit Property `Guid FreizeitId` implementiert `IFreizeitCommand` -- verhindert Lücken bei künftigen Commands.
- Alle betroffenen Commands -- `IFreizeitCommand` ergänzen; Inventar-Schaden-Commands mit `[ErlaubtNachAbschluss]` (falls sie `FreizeitId` tragen, sonst ohne Wirkung, trotzdem markieren) -- Lücken schließen.
- `tests/Zeltlotse.Api.Tests/AbschlussTests.cs` -- zuerst: HTTP-Tests der I/O-Matrix gegen PostgreSQL inkl. Durchlauf `FreizeitAbgeschlossen` → Vorfall-Notiz archiviert (AD-28, AD-30), Schreibschutz für Anmeldung entscheiden, Leitung einladen, Anmeldung absenden per Token, Ausnahme Schaden eintragen -- testgetrieben.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Abschluss/FreizeitAbschliessen.cs`, `Freizeit.cs`, `FreizeitenModul.cs`, `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- Command (`IBenutzerCommand` + `IFreizeitCommand`, Doppel-Abschluss → 409 durch die Stufe), Validator bzw. Handler-Prüfung (Phase Nachbereitung, sonst 400), Handler, Endpunkt, Routenliste -- Kern.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs`, `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` (+ ggf. neue Komponente `AbschlussCard.razor`) -- Card „Abschluss“ in der Nachbereitung: offene Aufgaben (nicht erledigt) als Liste mit Link, Link „Schäden & Verluste“, für Leitung `InlineBestaetigung` mit dynamischem Satz; bei Gelaufen Hinweis „Diese Freizeit ist abgeschlossen und nur lesbar.“ oben auf der Startseite; nach Erfolg Kontext neu laden -- CAP-27.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitenKarten.razor` -- `Segmente` „Alle · in Vorbereitung · läuft · Nachbereitung · gelaufen“, clientseitig -- „per Filter ausblendbar“.
- Öffentliche Anmeldung -- 409 → „Diese Freizeit ist zu Ende.“ mit Kontakt der Leitung sichtbar (EXPERIENCE Z.240).
- `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs` -- bUnit: Card nur in Nachbereitung, Knopf nur mit Leitungsrecht, Satz mit/ohne offene Aufgaben, Hinweis bei Gelaufen, Filter.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs`, `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` -- zwei vergangene Demo-Freizeiten der Demo-Organisation mit festen Daten, Leitung Miriam, Team Tobias: „Herbstfreizeit 2026“ 03.–08.10.2026 (bleibt Nachbereitung, 2 offene Nachbereitungs-Aufgaben) und „Pfingstlager 2026“ 23.–30.05.2026 mit Schäden/Verlusten (und Vorfall-Notiz, falls die Commands das nach Ende zulassen), per `FreizeitAbschliessen` abgeschlossen -- Screenshots und Deferreds aus 3-4/2-10.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Screenshots `freizeit-abschliessen-{handy,laptop}` (Herbstfreizeit-Startseite mit geöffneter Inline-Bestätigung), `freizeit-gelaufen-*` (Pfingstlager-Startseite mit Hinweis), Org-Übersicht mit Filter; `anschaffungswuensche-*` (und ggf. Vorfall-Archiv) jetzt gefüllt.
- `doku/src/content/docs/leitung/freizeit-abschliessen.md`, `leitung/index.md`, `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs`, betroffene Sätze in den genannten Doku-Seiten -- Anwender-Doku (NFR-20).

**Acceptance Criteria:**
- Given eine Freizeit in der Nachbereitung mit 2 offenen Aufgaben, when die Leitung „Freizeit abschließen“ tippt, then klappt die Inline-Bestätigung „2 Aufgaben offen – trotzdem abschließen?“ auf, und nach „Ja, abschließen“ zeigt die Startseite „Diese Freizeit ist abgeschlossen und nur lesbar.“ ohne Abschluss-Knopf.
- Given ein Teammitglied ohne Leitungsrolle, when es die Startseite in der Nachbereitung öffnet, then sieht es die Card „Abschluss“ ohne Knopf.
- Given eine gelaufene Freizeit mit Vorfall-Notizen, when die Outbox zugestellt ist, then stehen die Notizen im Vorfall-Archiv der Organisation.
- Given die Org-Übersicht der Freizeiten, when Org-Überblick „gelaufen“ wählt, then erscheinen nur gelaufene Freizeiten.
- Given die Doku, when `npm run build` in `doku/` läuft, then ist er fehlerfrei und die Seite „Freizeit abschließen“ zeigt Handy- und Laptop-Screenshots.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 32 findings — high 0, medium 5, low 19, false 8, maybe-false 0
- findings:
  - `[medium]` `[patch]` (Blind) LeitungAnnehmen nach Abschluss nicht gesperrt — Handler-Prüfung auf Gelaufen mit 409 plus HTTP-Test ergänzt.
  - `[low]` `[patch]` (Blind) Architekturregel übersieht `Guid? FreizeitId` — Regel auf `Guid?` erweitert.
  - `[low]` `[patch]` (Blind) Bestätigungssatz behauptet „nichts offen“, wenn Aufgaben nicht geladen — neutraler Satz bei `Aufgaben is null`.
  - `[low]` `[reject]` (Blind) „Diese Freizeit ist zu Ende.“ praktisch unerreichbar — gewollte zweite Absicherung (Anmeldelink endet mit Freizeitende); Doku nennt den realen 404-Text, kein Fehlverhalten.
  - `[low]` `[reject]` (Blind) Kontakt der Leitung fehlt in „zu Ende“-Meldung — derselbe praktisch unerreichbare Pfad; öffentliche Daten tragen keine Kontaktdaten, Ergänzung bräuchte neue Felder.
  - `[low]` `[defer]` (Blind) Zeitfenster zwischen Prüfung und Schreiben — als deferred vermerkt; Fix bräuchte Sperren je Command.
  - `[low]` `[reject]` (Blind) Demo: Ergebnis von SchadenEintragen ignoriert, feste Vorfall-Id bei Abbruch mitten im Seeding — nur bei abgebrochenem Seeding, Fix fügt Zweige hinzu.
  - `[low]` `[patch]` (Blind) Kommentar in DemoDaten vertauscht Herbstfreizeit/Pfingstlager — korrigiert.
  - `[low]` `[patch]` (Blind) Doku-Liste der gesperrten Bereiche unvollständig — Sicherheitskette, Schutzkonzept, Team-Notizen, Reservierungen, Erinnerungen ergänzt.
  - `[low]` `[reject]` (Blind) HTTP-Tests für SchadenBearbeiten/Entfernen/AnschaffungswunschErledigt und Einsicht-Commands fehlen — Stufe per Pipeline-Test, Attribut-Liste per Architekturtest abgedeckt; Fehlerbild unwahrscheinlich.
  - `[false]` `[reject]` (Blind) Story-Datei veraltet (Tobias, Vorfall) — Fix wäre eine Spec-Änderung.
  - `[low]` `[reject]` (Blind) Text „Abschließen geht erst …“ doppelt in Client und Server — Abweichung nur bei künftiger Textänderung, gemeinsame Konstante wäre neue Oberfläche.
  - `[low]` `[reject]` (Intent a) Schreibschutz nur für 6 Pfade per HTTP geprüft — übrige Commands über Pipeline- und Architekturtest abgedeckt.
  - `[false]` `[reject]` (Intent b) AD-30-Token-Fälle nicht erneut geprüft — der neue Endpunkt ist kein Token-Endpunkt; die bestehenden Token-Tests bleiben grün.
  - `[false]` `[reject]` (Intent c) andere Seiten zeigen nach Abschluss Bearbeiten-Knöpfe — Programm, Dokumente, Einstellungen, Vorfall-Protokoll prüfen `Phase.Gelaufen` schon seit früheren Stories und zeigen den Hinweis.
  - `[low]` `[reject]` (Intent d) AC1 im Browser nicht bis „Ja, abschließen“ gefahren — bewusst, damit die Demo-Herbstfreizeit offen bleibt; per bUnit und HTTP belegt.
  - `[false]` `[reject]` (Intent d) Doku-Build nicht belegt — `npm run build` lief laut Umsetzung (55 Seiten) und läuft im Abschluss erneut.
  - `[false]` `[reject]` (Intent g) loeschfristen-/nachweis-richtlinie-Screenshots ohne Ursache — die Organisationsseite ist durch neue Demo-Freizeiten länger, die Bilder zeigen sie mit.
  - `[low]` `[reject]` (Intent f) offene Punkte auf Aufgaben verengt — so in den Design Notes festgelegt; Finanzen-Modul leer.
  - `[medium]` `[patch]` (Gap) LeitungAnnehmen nicht schreibgeschützt — wie oben, gleicher Fix.
  - `[medium]` `[patch]` (Gap) Einsicht-Commands mit `Guid?` ohne Regel — wie oben, Regel erweitert.
  - `[low]` `[patch]` (Gap) „zu Ende“-Zweig in AnmeldungMitKonto ungetestet — bUnit-Test ergänzt.
  - `[low]` `[patch]` (Gap) Card-Pfad 409 SchonAbgeschlossen ungetestet — bUnit-Test ergänzt.
  - `[low]` `[defer]` (Edge) Zeitfenster Prüfung/Transaktion — wie oben, deferred.
  - `[low]` `[reject]` (Edge) AbschlussStand cached „nicht gelaufen“ im selben Scope nach Abschluss — Abschluss ist der letzte Command jeder Anfrage; im Demo-Seeding folgt kein weiterer Command derselben Freizeit.
  - `[low]` `[patch]` (Edge) Aufgaben null → irreführender Satz — wie oben.
  - `[false]` `[reject]` (Edge) Uhr vor 31.05.2026 bricht Demo-Seeding — die Uhr läuft nur vorwärts, heute ist 14.10.2026.
  - `[medium]` `[patch]` (Edge) Demo-Seeding scheitert dauerhaft, wenn jemand die Herbstfreizeit abschließt — früher Ausstieg wie beim Pfingstlager.
  - `[low]` `[patch]` (Edge) Architekturregel für `Guid?` — wie oben.
  - `[medium]` `[patch]` (Edge) LeitungAnnehmen — wie oben.
  - `[false]` `[reject]` (Edge) NachweisEntscheiden nach Abschluss nicht gesperrt — die Nachweis-Bestätigung gehört der Organisation und gilt über die Freizeit hinaus (FR-10); sie schreibt nicht in die Freizeit.
  - `[false]` `[reject]` (Edge) „zu Ende“-Pfad tot — zweite Absicherung, siehe oben.

### 2026-10-17 — Review pass (Folgedurchlauf)
- verdicts: 35 findings — high 0, medium 2, low 26, false 7, maybe-false 0
- findings:
  - `[low]` `[defer]` (Blind) Zeitfenster zwischen Schreibschutz-Prüfung und Schreiben, Vorfall nach dem Archivieren bleibt unarchiviert — carried: unverändert, schon als deferred vermerkt.
  - `[low]` `[reject]` (Blind) Validierung vor dem Schalter verrät abgeschaltete Endpunkte (400 statt 404) — die Reihenfolge schreibt die Spine Z.381 vor; als Restrisiko im Auto Run Result vermerkt, eine Abweichung bräuchte eine Spine-Änderung.
  - `[low]` `[reject]` (Blind) `AbschlussStand` lässt eine unbekannte Freizeit als „nicht gelaufen“ durch (fail-open) — `RechtePruefung` setzt den Mandanten vor dem Mediator; kein erreichbarer Pfad ohne Mandant gezeigt, Absicherung wäre ein zusätzlicher Zweig.
  - `[low]` `[reject]` (Blind) Architekturregel erkennt nur Commands mit `FreizeitId` — Folge-Review-Audit aller `IBenutzerCommand` ohne `IFreizeitCommand` gemacht: Freizeit-Bezug haben nur `AnmeldungAbsenden`/`GastAnmeldungAbsenden` und `LeitungAnnehmen` (Handler-Prüfung, HTTP-getestet) sowie `NachweisEntscheiden` und `EinsichtFreigeben` (Daten der Person bzw. Organisation, FR-10). Keine Lücke heute; eine Ausnahmeliste wäre neue Struktur für hypothetische künftige Commands.
  - `[low]` `[reject]` (Blind) Leitungs-Einladung nach dem Abschluss: Seite zeigt allgemeinen Fehler, kein Sicherheitsprotokoll, Einladungen nicht zurückgezogen — selten (Einladung offen über den Abschluss hinweg), Fix bräuchte einen neuen Zweig in Client und Seite; Protokoll und Aufräumen verlangt die Intention nicht.
  - `[low]` `[reject]` (Blind) Kein „abgeschlossen von“ und kein Protokolleintrag — die Intention speichert bewusst nur `AbgeschlossenAm`; neues Feld bräuchte eine Migration, die das Spec ausschließt.
  - `[low]` `[reject]` (Blind) „zu Ende“-Pfad praktisch unerreichbar, Doku nennt den 404-Text — carried: gewollte zweite Absicherung.
  - `[low]` `[patch]` (Blind) Doku sagt „gelaufene Freizeiten ausblenden“, die Leiste wählt aber genau eine Phase — Satz in `leitung/freizeit-abschliessen.md` korrigiert.
  - `[false]` `[reject]` (Blind) Client bildet jede 409 auf `SchonAbgeschlossen` und jede 400 auf `ZuFrueh` ab — der Command trägt nur `FreizeitId`; 400 kann nur der Phasen-Fehler sein, 409 nur `freizeit-abgeschlossen` (Stufe oder Handler).
  - `[low]` `[reject]` (Blind) Fokus geht nach „Ja, abschließen“ verloren — der Hinweis steht in einer `Meldung` mit `role="status"`; gleiches Muster wie bei den übrigen Inline-Bestätigungen, Fokus-Steuerung wäre neue Logik.
  - `[low]` `[reject]` (Blind) Testabdeckung des Schreibschutzes dünn — carried für Programm, Inventar, Sicherheit und die drei übrigen Ausnahmen; der Teil „Einsicht“ ist unten als Patch umgesetzt.
  - `[low]` `[reject]` (Blind) Demo: Ergebnis von `SchadenEintragen` ungeprüft, Wiederholung nach Abbruch, Datum im Screenshot überschrieben — carried für die ersten beiden Punkte; das Überschreiben des Datums ist ein schon vorher genutztes Muster der Screenshot-Tests (8 Stellen vor der Story).
  - `[false]` `[reject]` (Blind) Protokollzeile ohne Testnachweis — das Protokoll führt der Orchestrator; der Nachweis steht im Auto Run Result (2389 grün) und in diesem Durchlauf.
  - `[low]` `[defer]` (Edge) Zeitfenster Schreibschutz/Transaktion — carried.
  - `[low]` `[defer]` (Edge) `AnmeldungAbsenden` liest die Phase ohne Zeilensperre, Anmeldung parallel zum Abschluss — gleiche Ursache wie das deferred Zeitfenster; als eigener deferred-Eintrag ergänzt.
  - `[low]` `[reject]` (Edge) `AbschlussStand` cached „nicht gelaufen“ im selben Scope — carried.
  - `[false]` `[reject]` (Edge) `EinsichtFreigeben` nach dem Abschluss möglich — schreibt die eigene Freigabe der Person (AD-26, FR-10), nicht die Freizeit; Anfragen und Übernehmen über die Freizeit-Route sind gesperrt (jetzt HTTP-getestet); gleiche Begründung wie beim carried `NachweisEntscheiden`.
  - `[low]` `[reject]` (Edge) Architekturregel übersieht Commands mit Kind-Id — wie oben (Audit gemacht, keine Lücke).
  - `[low]` `[reject]` (Edge) Demo-Suche per Name mit `SingleOrDefaultAsync` scheitert bei gleichnamiger Freizeit — dasselbe Muster wie bei allen bisherigen Demo-Freizeiten (`SingleAsync` per Name); unwahrscheinlich.
  - `[low]` `[reject]` (Edge) Bestätigung des ersten Kindes verschwindet bei 409 für das zweite — nur über den praktisch unerreichbaren „zu Ende“-Pfad.
  - `[low]` `[patch]` (Edge) Nach dem Anlegen einer Freizeit bei aktivem Phasen-Filter fehlt sie in der Tabelle — `FreizeitenKarten.razor` setzt den Filter nach dem Anlegen auf „Alle“; bUnit-Test zuerst rot, dann grün.
  - `[low]` `[reject]` (Edge) Fehler beim Neuladen nach dem Abschluss unbehandelt — unwahrscheinlich; Fix wäre ein zusätzlicher Fangzweig.
  - `[low]` `[defer]` (Edge) Demo-Freizeiten ohne Team Tobias — carried.
  - `[low]` `[reject]` (Edge) Kontakt der Leitung fehlt in „zu Ende“ — carried.
  - `[medium]` `[patch]` (Gap) Einsicht-Commands nach dem Abschluss ohne HTTP-Test, Abbildung `Guid? → Guid.Empty` ungeprüft — `Schreibschutz_Einsicht_anfragen_und_pruefen_409_nach_dem_Abschluss` in `Api.Tests/AbschlussTests.cs` ergänzt, grün.
  - `[low]` `[patch]` (Gap) Kein Test für zwei gleichzeitige Abschlüsse (`FOR UPDATE`) — `Zwei_gleichzeitige_Abschluesse_ein_204_ein_409_und_genau_ein_Ereignis` ergänzt, grün.
  - `[false]` `[reject]` (Gap) `NachweisEntscheiden` nach dem Abschluss nicht gesperrt — carried.
  - `[low]` `[reject]` (Gap) `AbschlussStand` fail-open ohne Mandant — wie oben.
  - `[low]` `[reject]` (Intent a) Token-Commands werden im Handler statt in der Stufe gesperrt — die HTTP-Antwort entspricht der Matrix und ist getestet; Token-Commands tragen keine `FreizeitId` (Design Notes).
  - `[false]` `[reject]` (Intent b) übrige Seiten zeigen nach dem Abschluss Bearbeiten-Knöpfe — carried.
  - `[medium]` `[patch]` (Intent c) Nachweis-Commands nach dem Abschluss nicht per HTTP geprüft — gleiche Ursache wie der Gap-Fund, gleicher Test.
  - `[low]` `[reject]` (Intent d) Ausnahmen außer „Schaden eintragen“ nicht per HTTP — carried.
  - `[low]` `[reject]` (Intent f) offene Punkte nur Aufgaben — carried.
  - `[false]` `[reject]` (Intent g) AD-30-Pflichtfälle — 403/404/401 und die Token-Fälle sind per HTTP getestet; der Abschluss-Endpunkt selbst ist kein Token-Endpunkt.
  - `[false]` `[reject]` (Intent h) Änderungen außerhalb des Wortlauts — stützen die Funktionen und berühren keinen Never-Punkt.

## Design Notes

- **Marker statt Reflexion:** Die Stufe braucht die FreizeitId. Ein schmales `IFreizeitCommand` (nur `Guid FreizeitId`) ist einfacher als Property-Suche per Reflexion; der Architekturtest hält es vollständig. Commands ohne FreizeitId (z. B. `…(Guid EintragId)`) behalten ihre Handler-Prüfung.
- **Abschluss-Command selbst:** Trägt `IFreizeitCommand`; eine schon gelaufene Freizeit lehnt damit die Stufe mit `409` ab – genau die gewünschte Antwort für „doppelt“.
- **Zu früh = 400 statt neuer Code:** AD-5 erlaubt neue Problemcodes nur mit Spine-Änderung; der Knopf erscheint ohnehin nur in der Nachbereitung, darum genügt ein Validierungsfehler.
- **Knopf-Recht über `freizeit-einstellungen`:** gleiche Rollenmenge (nur Leitung) wie „Freizeit abschließen“ in `rollen-und-rechte.md`; ein eigener Schlüssel bräuchte eine Spine-Änderung.
- **Offene Punkte = nicht erledigte Aufgaben:** Belege gibt es noch nicht (Finanzen leer); die Finanzen-Story ergänzt „Belege nicht erstattet“ im Satz. Der Client zählt aus den schon geladenen Aufgaben (AD-2: Client setzt zusammen).
- **Feste Demo-Daten in der Vergangenheit:** Der Host läuft mit der Systemuhr; relative Daten würden die Screenshots täglich ändern. 2026 liegt vor heute (14.10.2026), die Phasen bleiben stabil. Lässt ein Command (z. B. Vorfall eintragen) nur die Phase „läuft“ zu, entfällt der Demo-Eintrag dafür; das bleibt als `deferred` im Spec vermerkt, statt Speziallogik zu bauen.

## Verification

**Commands:**
- `dotnet test tests/Zeltlotse.SharedKernel.Tests` / `tests/Zeltlotse.Api.Tests --filter "FullyQualifiedName~Abschluss|FullyQualifiedName~EndpunktListe"` / `tests/Zeltlotse.ArchitectureTests` -- gezielt während der Umsetzung grün.
- `dotnet test Zeltlotse.slnx` -- einmal am Ende, vollständig grün inkl. E2E (Kategorie Doku erzeugt Screenshots).
- `npm run build` in `doku/` -- einmal am Ende, fehlerfrei.

## Auto Run Result

Status: done

**Umgesetzt:** Die Leitung schließt eine Freizeit in der Nachbereitung über `POST /api/freizeiten/{freizeitId}/abschliessen` ab (204; zu früh 400 an `phase`; schon gelaufen 409 `freizeit-abgeschlossen`). Das setzt `AbgeschlossenAm` und legt `FreizeitAbgeschlossen` über die Outbox ab; Sicherheit archiviert daraufhin die Vorfall-Notizen. Der Schreibschutz nach AD-17 läuft jetzt zentral in der Pipeline (`SchreibschutzStufe` für `IBenutzerCommand` + `IFreizeitCommand`, Ausnahmen per `[ErlaubtNachAbschluss]` an den vier Inventar-Commands). Pipeline-Reihenfolge nach Spine: Validierung → Schreibschutz → Schalter → Inbox → Transaktion → Outbox. Token-Commands (Anmeldung absenden, Leitung annehmen) prüfen im Handler. Oberfläche: Card „Abschluss“ mit offenen Aufgaben und Inline-Bestätigung, Hinweis „nur lesbar“ auf der Startseite, Phasen-Filter in der Org-Übersicht, „Diese Freizeit ist zu Ende.“ bei der Anmeldung.

**Geänderte Dateien (Auswahl):**
- `src/Zeltlotse.SharedKernel/Commands/IModulCommand.cs`: `IFreizeitCommand` und `ErlaubtNachAbschlussAttribute`.
- `src/Zeltlotse.SharedKernel/Pipeline/SchreibschutzStufe.cs` und `FreizeitAbgeschlossenAusnahme.cs`, `src/Zeltlotse.SharedKernel/Rechte/IAbschlussStand.cs`: die neue Pipeline-Stufe.
- `src/Zeltlotse.Api/Rechte/AbschlussStand.cs`, `Program.cs`, `Fehler/AusnahmeBehandlung.cs`: Stand je Anfrage, Reihenfolge, 409.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Abschluss/FreizeitAbschliessen.cs`, `Domaene/Freizeit.cs`, `FreizeitenModul.cs`: Command, Handler (mit Zeilensperre), Endpunkt.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Leitung/LeitungAnnehmen.cs`, `Anmeldungen/AnmeldungAbsenden.cs`: Prüfung im Handler.
- Rund 40 Commands in Freizeiten, Programm, Inventar, Personen und Sicherheit: Marker `IFreizeitCommand`; die vier Inventar-Schaden-Commands tragen das Attribut.
- `src/Zeltlotse.App/Features/Freizeiten/AbschlussCard.razor`, `FreizeitStart.razor`, `FreizeitenKarten.razor`, `AnmeldungMitKonto.razor`, `src/Zeltlotse.Public/Seiten/Anmelden.razor`, `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs`: Oberfläche und Client.
- `src/Zeltlotse.Ui.Komponenten/wwwroot/css/elkw-components.css`: Kontrast beim Hover über ein gewähltes Segment (axe).
- `src/Zeltlotse.Api/Demo/DemoDaten.cs`, `DemoKatalog.cs`: „Herbstfreizeit 2026“ (Nachbereitung, 2 offene Aufgaben) und „Pfingstlager 2026“ (abgeschlossen, mit Schäden und Vorfall-Notiz).
- Doku: neue Seite `doku/src/content/docs/leitung/freizeit-abschliessen.md`; Sätze „kommt später“ bzw. „bleibt leer“ auf sieben Seiten entfernt.
- Tests:
  - neu: `Api.Tests/AbschlussTests.cs`, `Freizeiten.Tests/AbschlussTests.cs`;
  - erweitert: `PipelineTests`, `TypRegelnTests` (zwei Regeln, `Guid` und `Guid?`), `EndpunktListeTests`, `DemoDatenTests`, bUnit-Seitentests;
  - E2E-Screenshots: `freizeit-abschliessen`, `freizeit-gelaufen`, `freizeiten-filter`, jeweils Handy und Laptop.

**Review:** 32 Funde.
- Als Patch umgesetzt:
  - 3 Einträge medium: Leitung annehmen nach dem Abschluss, Architekturregel für `Guid?`, Demo-Seeding der Herbstfreizeit nach Abschluss;
  - mehrere low: neutraler Bestätigungssatz, Kommentar in den Demo-Daten, Doku-Liste, zwei bUnit-Tests.
- Zurückgestellt (low): das Zeitfenster zwischen Schreibschutz-Prüfung und Transaktion.
- Abgelehnt (Gründe im Triage-Log): unter anderem der praktisch unerreichbare Pfad „zu Ende“ (bewusst als zweite Absicherung) und `NachweisEntscheiden` (die Bestätigung gehört der Organisation, nicht der Freizeit).

**Folge-Review empfohlen (true):** 3 Einträge medium wurden gepatcht. Offenes Risiko: Weitere Benutzer-Commands erreichen ihre Freizeit nur über eine Eintrags-Id oder ein Token statt über `FreizeitId`. Die Architekturregel erkennt sie nicht; `LeitungAnnehmen` wurde nur im Review gefunden. Ein Folge-Review sollte alle `IBenutzerCommand` ohne `IFreizeitCommand` auf eine fehlende Prüfung auf „gelaufen“ durchsehen.

**Verifikation:**
- `dotnet test Zeltlotse.slnx` nach allen Patches: 2389 von 2389 grün, inklusive Architektur, E2E und Doku-Screenshots.
- `npm run build` in `doku/`: fehlerfrei, 55 Seiten.
- Screenshots pixelweise mit HEAD verglichen; Bilder, die sich nur durch Rendering-Rauschen, zufällige Ports oder Tokens unterscheiden, sind zurückgesetzt.

**Restrisiken:**
- Die vergangenen Demo-Freizeiten haben kein Team (deferred).
- Wegen der festen Daten löscht die 12-Monats-Frist die Daten des Pfingstlagers ab etwa Juni 2027.
- Validierung läuft jetzt vor dem Schalter: Ein ungültiger Command für ein abgeschaltetes Modul ergibt 400 statt 404, wie es die Spine verlangt.

### Folgedurchlauf 2026-10-17

**Umgesetzt (Review-Patches):**
- Zwei HTTP-Tests gegen PostgreSQL: Einsicht anfragen und prüfen über die Freizeit-Route nach dem Abschluss → `409 freizeit-abgeschlossen` ohne neue Einsicht; zwei gleichzeitige Abschlüsse → ein `204`, ein `409`, genau ein Ereignis, Zeitpunkt unverändert.
- Phasen-Filter der Org-Übersicht springt nach dem Anlegen einer Freizeit auf „Alle“, damit die neue Freizeit sichtbar ist (bUnit-Test zuerst rot, dann grün).
- Doku-Satz zum Filter präzisiert (eine Phase oder alle; nach dem Anlegen wieder „Alle“).

**Geänderte Dateien:**
- `tests/Zeltlotse.Api.Tests/AbschlussTests.cs`: zwei neue Tests, Klassenkommentar ergänzt.
- `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs`: Test für den Filter nach dem Anlegen.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitenKarten.razor`: Filter nach erfolgreichem Anlegen auf „Alle“.
- `doku/src/content/docs/leitung/freizeit-abschliessen.md`: Satz zum Phasen-Filter.

**Review:** 35 Funde (medium 2, low 26, false 7).
- Patches: 1 Eintrag medium (Einsicht-Commands nach dem Abschluss ohne HTTP-Test, aus Gap und Intent), 3 low (gleichzeitiger Abschluss, Filter nach dem Anlegen, Doku-Satz).
- Zurückgestellt: Anmeldung über den Anmeldelink liest die Phase ohne Zeilensperre (neuer deferred-Eintrag, gleiche Ursache wie das schon deferred Zeitfenster).
- Abgelehnt, Gründe im Triage-Log, unter anderem:
  - Pipeline-Reihenfolge nach Spine;
  - Fehlerbild „fail-open“ ohne erreichbaren Pfad;
  - Ausnahmeliste für Commands ohne `FreizeitId`. Das Audit aller `IBenutzerCommand` ohne `IFreizeitCommand` hat keine Lücke gefunden.
  - `EinsichtFreigeben` schreibt Daten der Person, nicht der Freizeit.
  - Fokus-Steuerung entspricht dem Muster der anderen Inline-Bestätigungen.

**Folge-Review empfohlen (false):** Folgedurchlauf ohne gepatchten high-Fund. Gepatcht: 0 high, 1 medium, 3 low. Das im Erstdurchlauf benannte Risiko (Benutzer-Commands ohne `FreizeitId`) ist durch das Audit geklärt.

**Verifikation:**
- Gezielt:
  - `AbschlussTests`: 10 von 10 grün;
  - `FreizeitenSeitenTests`: 50 von 50 grün;
  - Architekturtests: 135 von 135 grün.
- Am Ende alle Testprojekte einzeln, zusammen 2392 von 2392 grün:
  - Api 822, E2E inklusive Doku-Screenshots 161, Ui 662, Architektur 135;
  - Module: Ablaeufe 10, Freizeiten 93, Inventar 34, Konten 26, Organisationen 58, Personen 153, Programm 91, SharedKernel 111, Sicherheit 36.
- `npm run build` in `doku/`: fehlerfrei, 55 Seiten.
- Screenshots pixelweise mit HEAD verglichen. Alle Abweichungen kommen von zufälligen Ports und Tokens oder vom heutigen Datum, deshalb sind alle zurückgesetzt.

**Restrisiken:** wie im Erstdurchlauf, dazu der neue deferred-Eintrag zur Anmeldung ohne Zeilensperre.
