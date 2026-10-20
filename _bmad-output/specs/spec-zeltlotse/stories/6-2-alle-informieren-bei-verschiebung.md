---
title: 'Story 6-2: „Alle informieren?“ bei Verschiebung'
type: 'feature'
created: '2026-10-20'
baseline_revision: '975eac124ed2fbd075bd38be37d6042bf60a44b3'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/6-1-kanaele-team-und-alle-mit-push.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Verschiebt jemand einen Programmpunkt (z. B. Mittagessen 13:00 → 13:30), erfährt es niemand, und es bleibt auch nicht sichtbar, dass die Änderung noch nicht mitgeteilt wurde (CAP-36, FR-62, NS-9, AD-13).

**Approach:**
- Programm merkt sich am Programmpunkt die zuletzt mitgeteilte Uhrzeit, sobald sich `Beginn` ändert (`MitgeteilterBeginn`). Ein gesetzter Wert bedeutet „Geändert, noch nicht mitgeteilt“.
- Nach dem Speichern der Uhrzeit klappt am Programmpunkt die Inline-Bestätigung „Alle informieren?“ mit vorbereitetem Text auf.
- „Ja, informieren“: Der Client schreibt die Nachricht über den vorhandenen Kanäle-Endpunkt in „Alle“ (`bestaetigt: true`, Push über die Outbox aus 6-1). Danach meldet er dem Programm „mitgeteilt“.
- „Nein“ oder Verlassen der Seite: Es wird nichts verschickt, und der Hinweis „Geändert, noch nicht mitgeteilt“ bleibt mit „Jetzt informieren“ und „Ausblenden“.

## Boundaries & Constraints

**Always:**
- Regel in der Entität Programmpunkt, angewendet bei jeder gelungenen Änderung von `Beginn` über `FeldAendern`:
  - Ist `MitgeteilterBeginn` leer und war der alte `Beginn` gesetzt, wird `MitgeteilterBeginn` = alter `Beginn`.
  - Ist `MitgeteilterBeginn` gesetzt und gleicht der neue `Beginn` diesem Wert, wird `MitgeteilterBeginn` geleert (zurückgeschoben, nichts mitzuteilen).
  - Sonst bleibt `MitgeteilterBeginn` unverändert. Titeländerungen und das Anlegen setzen nie etwas.
- Neuer Endpunkt `POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/mitgeteilt` mit Körper `ProgrammpunktMitgeteilt(string? Beginn)`. Er leert `MitgeteilterBeginn`, wenn `Beginn` der aktuellen Uhrzeit des Punkts entspricht. Damit dienen „Ja, informieren“ und „Ausblenden“ demselben Endpunkt.
  - Der Endpunkt trägt `.NurFreizeitRolle(ProgrammRechte.Mitarbeit)`. Der Command hat `IBenutzerCommand` und `IFreizeitCommand`. Bei *gelaufen* antwortet er 409 `freizeit-abgeschlossen`, nach dem Muster von `ProgrammpunktFeldAendernCommand`.
- Das Ändern der Uhrzeit verschickt selbst nichts: keine Nachricht, kein Push, keine Mail (AD-13). Versendet wird nur über den Kanäle-Endpunkt mit `bestaetigt: true`.
- Der Vertrag `Programmpunkt` bekommt additiv `string? MitgeteilterBeginn = null` am Ende (AD-5).
- Vorbereiteter Text, erzeugt im Client (`ProgrammTexte`): `„{Titel} am {Wochentag kurz} {TT.MM.}: {neu} statt {alt}“`, z. B. „Mittagessen am Di 03.08.: 13:30 statt 13:00“. Ohne neue Uhrzeit steht dort „ohne Uhrzeit statt {alt}“.
  - Satz der Bestätigung: `Alle informieren? „{Text}“`.
- Oberfläche nach DESIGN.md (Inline-Bestätigung, Hinweis „nicht mitgeteilt“) und EXPERIENCE.md:
  - Die Hinweiszeile ist klein, `text-muted`, ohne Statusrot.
  - Der Fokus springt auf „Nein“, Escape klappt zu.
  - Sichtbar ist das auf der Detailseite des Programmpunkts und am Termin im Wochenraster. Bei *gelaufen* gibt es keine Knöpfe.
- Neue Darstellungsbausteine entstehen zuerst in `Zeltlotse.Ui.Komponenten`:
  - `InlineBestaetigung` bekommt einen Parameter `Offen`, mit dem sie aufgeklappt beginnt bzw. aufklappt, sobald er auf `true` wechselt.
  - Neu ist `Hinweiszeile` (Text plus ChildContent für Textknöpfe).

**Never:**
- Kein neues Ereignis, kein neuer Pfeil, kein Aktionsschlüssel, keine Änderung an ARCHITECTURE-SPINE.md. Programm ruft Kanäle nicht auf, Kanäle liest Programm nicht.
- Kein automatischer Versand, kein Bearbeiten des vorbereiteten Texts in der Bestätigung (eigene Texte schreibt man im Kanal), kein Hinweis für Titeländerungen.
- Kein Teilnehmer-Zugang oder QR (6-4), kein Ansagen-Zettel (6-3).

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehler |
|---|---|---|---|
| Verschieben | PATCH `beginn` 13:00 → 13:30 | 200; GET liefert `mitgeteilterBeginn: "13:00"`; in `kanaele` keine Nachricht, in den Outboxen kein Push | – |
| Erneut verschieben | 13:30 → 14:00 bei offenem Hinweis | `mitgeteilterBeginn` bleibt `"13:00"` | – |
| Zurückschieben | 14:00 → 13:00 | `mitgeteilterBeginn: null` | – |
| Erste Uhrzeit | ohne Uhrzeit → 15:00 | `mitgeteilterBeginn: null` | – |
| Uhrzeit entfernt | 13:00 → leer | `mitgeteilterBeginn: "13:00"` | – |
| Titel geändert | PATCH `titel` | `mitgeteilterBeginn` unverändert | – |
| Mitgeteilt | POST `.../mitgeteilt` `{beginn:"13:30"}` bei aktuell 13:30 | 204, Hinweis weg; auch ohne offenen Hinweis 204 | – |
| Inzwischen verschoben | `{beginn:"13:30"}`, aktuell 14:00 | 409 `konflikt`, Hinweis bleibt | – |
| Ungültige Uhrzeit | `{beginn:"25:99"}` | 400, Feldfehler `beginn` | Validierung |
| Rechte | Leitung/Team 204; Org-Admin ohne Rolle, fremde Organisation, Punkt einer anderen Freizeit, unbekannter Punkt 404; ohne Sitzung 401 | wie angegeben | – |
| Gelaufen | Freizeit abgeschlossen | 409 `freizeit-abgeschlossen` | – |

</intent-contract>

## Code Map

Wurzel: `src/` = S, `tests/` = T.

- Server Programm (`S/Modules/Programm/Zeltlotse.Programm/`):
  - `Programme/Programmpunkt.cs`: Entität ohne Aggregat-Basis, `FeldAendern(feld, alterWert, neuerWert)` (false bei abweichendem Altwert, AD-19). Hier entsteht die Regel für `MitgeteilterBeginn` und die Methode `Mitgeteilt(TimeOnly? beginn) → bool`.
  - `Programme/ProgrammpunktBearbeiten.cs`: Muster für den neuen Slice (Command `ProgrammpunktFeldAendernCommand`, Ergebnis `Gespeichert | Konflikt | Abgeschlossen | NichtGefunden`, Handler mit `IFreizeitZeitpunkte` → Phase, `db.ProgrammpunktSperrenAsync`, Endpunkt mit `ProgrammProbleme.Konflikt/Abgeschlossen`).
  - `Programme/ProgrammpunktUebernehmen.cs`: Muster für einen POST-Endpunkt an einem Punkt.
  - `Programme/WochenrasterLaden.cs`: Abbildung auf den Vertrag. Dort kommt `MitgeteilterBeginn` hinzu, ebenso auf der Detailseite, falls sie einen eigenen Lesepfad hat.
  - `Daten/ProgrammDbContext.cs` L116-132 (Modell), L83 `ProgrammpunktSperrenAsync`: Das rohe SQL muss die neue Spalte mitlesen, falls es Spalten aufzählt.
  - `Migrationen/`: neue Migration `<Zeitstempel>_MitgeteilterBeginn` nach `20261014035526_Programmbibliothek`, mit Snapshot.
  - `ProgrammModul.cs`: Routen unter `/freizeiten/{freizeitId:guid}/programm`, `.NurFreizeitRolle(ProgrammRechte.Mitarbeit)`.
- Verträge: `S/Zeltlotse.Api.Contracts/Programm/Programm.cs` L33 `Programmpunkt(...)`, `ProgrammpunktFelder.UhrzeitLesen/UhrzeitText`.
- Kanäle (nur nutzen, nicht ändern):
  - Endpunkt `S/Modules/Kanaele/Zeltlotse.Kanaele/Nachrichten/NachrichtSenden.cs`.
  - `S/Zeltlotse.Ui/Api/KanaeleApiClient.cs` L56 `SendenAsync(freizeitId, Kanal.Alle, new NachrichtNeu(text, true))` → `KanalSendenAktion`.
- Client:
  - `S/Zeltlotse.Ui/Api/ProgrammApiClient.cs`: `PunktPfad`, Muster `UebernehmenAsync`. Neu kommt `MitgeteiltAsync` hinzu.
  - `S/Zeltlotse.App/Features/Programm/ProgrammpunktSeite.razor` L83-91: `AutosaveFeld` für die Uhrzeit (`Id="programmpunkt-beginn"`), noch ohne `Gespeichert=`. `EventCallback<string?> Gespeichert` liefert nur den neuen Wert, der Altwert kommt aus dem neu geladenen `MitgeteilterBeginn`. Vorbild ist `Finanzen/BelegSeite.razor` L67-74.
  - `S/Zeltlotse.App/Features/Programm/Wochenraster.razor` L127-143: `Termin` mit ChildContent („Ich übernehme“). Dort kommt die Hinweiszeile hinzu.
  - `ProgrammTexte.cs` für die Texte.
  - `S/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor`: `_offen` ist privat, „Nein“ ist fest verdrahtet, Fokuslogik steckt in `OnAfterRenderAsync`.
  - `Knopf` mit `KnopfArt.Text`. Stile der Komponenten stehen dort, wo `.zl-bestaetigung` definiert ist (CSS-Datei der Komponentenbibliothek suchen).
- Tests:
  - `T/Zeltlotse.Api.Tests/ProgrammTests.cs`: `FreizeitpassSzenario`, Helfer `PatchAsync`, `Koerper`, `PunkteAsync`, `WertAsync`. Rechte-Tests ab L365/L392, Gelaufen-Test ab L333.
  - `T/Zeltlotse.Api.Tests/EndpunktListeTests.cs` L254-260.
  - `T/Zeltlotse.Programm.Tests/ProgrammpunktTests.cs`.
  - `T/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs` (`NachgebildeterHandler.Bei`, `Anfragen`) und `KomponentenTests.cs`.
  - `T/Zeltlotse.Api.Tests/DemoDatenTests.cs` ca. L539.
- Demo, Doku, E2E:
  - `S/Zeltlotse.Api/Demo/DemoDaten.cs` `ProgrammAsync` L1207 und `DemoKatalog.cs` L274 (Geländespiel 3.8. 14:30, Jonas).
  - `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: `Wochenraster` L1575, `ProgrammAsync` L2593, Muster `Kanaele_Alle` L2548 (prüft `.zl-bestaetigung__satz` und den Fokus auf „Nein“), `BildAsync` L2771.
  - `doku/src/content/docs/mitarbeitende/programm.md` ab L45 („Programmpunkt bearbeiten, verschieben und löschen“), `mitarbeitende/kanaele.md`.
  - `T/Zeltlotse.ArchitectureTests/DokuRegeln.cs`: Jede PNG muss verlinkt sein.

## Tasks & Acceptance

**Execution (testgetrieben: zuerst der Test, dann der Code):**
- `T/Zeltlotse.Programm.Tests/ProgrammpunktTests.cs`, dann `Programme/Programmpunkt.cs`: Unit-Tests für die Regel (alle Matrix-Zeilen von Verschieben bis Titel) und für `Mitgeteilt`. Danach Eigenschaft `TimeOnly? MitgeteilterBeginn` und die Logik in `FeldAendern`.
- `T/Zeltlotse.Api.Tests/ProgrammTests.cs`: HTTP-Tests nach der Matrix gegen PostgreSQL.
  - Rechte-Tabelle und Gelaufen für die neue Route.
  - AD-13-Fall: Nach dem PATCH gibt es keine Zeile in `kanaele` und keinen Push-Auftrag in den Outboxen. Ein aufzeichnender `IPushVersand` wie in `KanaeleTests` dient dem Nachweis über `OutboxJetztZustellenAsync`.
  - Dazu der Eintrag in `EndpunktListeTests.cs`.
- `Daten/ProgrammDbContext.cs` und `Migrationen/*_MitgeteilterBeginn.cs`: Spalte `MitgeteilterBeginn` (time, nullable). Der Snapshot wird über `dotnet ef migrations add` erzeugt, oder von Hand nach dem Muster der vorhandenen Migrationen.
- `Programme/ProgrammpunktMitgeteilt.cs` (neu): Command, Validator (Uhrzeit mit `ProgrammpunktFelder.UhrzeitLesen`), Handler, Endpunkt. Registrierung in `ProgrammModul.cs`.
- `S/Zeltlotse.Api.Contracts/Programm/Programm.cs`: `MitgeteilterBeginn` am Vertrag und `ProgrammpunktMitgeteilt(string? Beginn)`. Abbildung im Lesepfad.
- `S/Zeltlotse.Ui/Api/ProgrammApiClient.cs`: `MitgeteiltAsync(freizeitId, punktId, beginn)` mit Ergebnis Erfolg, Konflikt, Abgeschlossen oder Gescheitert, nach dem Muster der vorhandenen Aktionen.
- `S/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor` (Parameter `Offen`) und `Hinweiszeile.razor` (neu) mit Stil, dazu bUnit-Tests in `KomponentenTests.cs`.
- `S/Zeltlotse.App/Features/Programm/` (Detailseite, Wochenraster, Texte) mit bUnit-Tests in `ProgrammSeitenTests.cs`:
  - Detailseite: Nach dem Speichern der Uhrzeit wird neu geladen. Ist `MitgeteilterBeginn` gesetzt, steht die Hinweiszeile mit aufgeklappter Bestätigung da.
  - „Ja, informieren“: `SendenAsync(Alle, Text, true)`, danach `MitgeteiltAsync`, danach neu laden.
  - Fehler beim Senden: Meldung, Hinweis bleibt.
  - „Nein“: keine Anfrage. „Ausblenden“: nur `MitgeteiltAsync`.
  - Wochenraster: Hinweiszeile am Termin mit denselben Aktionen.
  - Bei *gelaufen* nur der Text, ohne Knöpfe.
- `S/Zeltlotse.Api/Demo/DemoDaten.cs` und `DemoKatalog.cs`, dazu `DemoDatenTests`: Das Geländespiel wird mit 14:00 angelegt und über das PATCH-Command auf 14:30 verschoben, also `MitgeteilterBeginn` 14:00. Das geschieht idempotent.
- `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: Bild `programm-mitteilen` für Handy und Laptop.
  - Im Wochenraster am 3.8. „Jetzt informieren“ am Geländespiel antippen.
  - Prüfen: Der Satz enthält „14:30 statt 14:00“, der Fokus liegt auf „Nein“. Danach die axe-Prüfung.
  - „Ja“ wird nicht geklickt, damit der Demo-Stand bleibt.
- `doku/src/content/docs/mitarbeitende/programm.md` (Abschnitt „Alle informieren“ mit beiden Bildern), `mitarbeitende/kanaele.md` (kurzer Verweis).

**Acceptance Criteria:**
- Gegeben Jonas ändert am Handy die Uhrzeit des Mittagessens von 13:00 auf 13:30: Nach dem Speichern steht am Programmpunkt „Alle informieren? „Mittagessen am … : 13:30 statt 13:00““ mit „Ja, informieren“ und „Nein“, der Fokus liegt auf „Nein“.
- Gegeben die aufgeklappte Bestätigung: „Ja, informieren“ legt genau eine Nachricht in „Alle“ an, die über die Outbox als Push hinausgeht, und der Hinweis verschwindet. „Nein“ schickt nichts, und „Geändert, noch nicht mitgeteilt“ bleibt mit „Jetzt informieren“ und „Ausblenden“, auch nach dem Neuladen und für andere Teammitglieder.
- Gegeben die fertige Story: `dotnet test` inklusive der Architekturtests ist grün, `programm-mitteilen-handy.png` und `-laptop.png` sind erzeugt und in der Doku verlinkt, und `npm run build` in `doku/` läuft fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 35 findings — high 0, medium 5, low 21, false 4, maybe-false 5
- findings:
  - `[medium]` `[patch]` Blind: Der Text wird aus einem womöglich veralteten Punkt gebaut und vor jeder Prüfung gesendet; ein `Konflikt` von „mitgeteilt“ wird ignoriert und als Erfolg gemeldet — Vor dem Senden lädt der Baustein den Punkt neu und sendet bei Abweichung nicht (Meldung „erneut verschoben“, Neuladen); ein `Konflikt` nach dem Senden zeigt die Meldung; bUnit-Tests ergänzt.
  - `[low]` `[reject]` Blind: Nach einem Konflikt bleibt `MitgeteilterBeginn` beim alten Wert, obwohl 13:30 mitgeteilt wurde — Nur bei gleichzeitiger Verschiebung während des Sendens; die Korrektur bräuchte eine zweite Command-Variante.
  - `[low]` `[patch]` Blind: „mitgeteilt“ antwortet ohne offenen Hinweis mit 409, wenn die Uhrzeit veraltet ist, entgegen dem Doku-Kommentar — `Mitgeteilt` liefert ohne offenen Hinweis immer `true`; Unit- und HTTP-Test ergänzt.
  - `[low]` `[patch]` Blind: „Rückgängig“ verschwindet, sobald der Hinweis erscheint — Bewusst (Neuladen des Punkts); in `programm.md` steht jetzt, dass man die alte Uhrzeit wieder einträgt, was auch den Hinweis entfernt.
  - `[low]` `[patch]` Blind: `daten.md` verschweigt, dass die Nachricht in „Alle“ ihre Autorin speichert — Satz mit Verweis auf den Abschnitt Kanäle ergänzt.
  - `[low]` `[reject]` Blind: Gleich lautende Knöpfe je Termin sind für Screenreader nicht unterscheidbar — Die Knöpfe stehen im Termin mit Titel und Uhrzeit (Zweck aus dem Kontext, WCAG 2.4.4); eine Beschriftung je Punkt bräuchte neue Parameter.
  - `[low]` `[patch]` Blind: Fehlende Tests für Fehlerpfade (mitgeteilt 409 nach dem Senden, Abgeschlossen beim Senden, Ausblenden 409, Gleichzeitigkeit in der API) — Tests für 409 nach dem Senden und Ausblenden 409 ergänzt; Abgeschlossen beim Senden und Gleichzeitigkeit verworfen (seltener Zweig bzw. Zeilensperre nach bewährtem Muster).
  - `[low]` `[reject]` Blind: `Wochenraster.MitgeteiltAsync` leert eine frühere Meldung — Die ältere Meldung („angelegt“) ist nach einer neuen Aktion nicht mehr wichtig; kein Schaden.
  - `[low]` `[reject]` Blind: Bestehende Demo-Datenbanken bekommen die Verschiebung nicht — E2E und Demo-Start spielen in eine frische Datenbank ein; das alte Erkennen von 14:30 würde ein „mitgeteilt“ in der Demo wieder rückgängig machen.
  - `[low]` `[reject]` Blind: Nach jedem Speichern der Uhrzeit lädt die Seite den ganzen Wochenraster — Gleiches Muster wie `NeuLadenAsync`; bei rund 40 Punkten unerheblich.
  - `[low]` `[patch]` Blind: Alt-Text des Laptop-Bilds vermischt „Ausblenden“ mit „Abgeben“ — Alt-Text umformuliert.
  - `[low]` `[patch]` Blind: Die Doku erklärt nicht, was tun, wenn „mitgeteilt“ nach dem Senden scheitert — Die Oberfläche nennt jetzt in diesem Fall „gesendet, bitte Ausblenden“ (siehe Edge 1).
  - `[medium]` `[patch]` Edge: Scheitert „mitgeteilt“ nach erfolgreichem Senden mit Ausnahme, steht „nicht gesendet“ da, ein erneuter Versuch schickt doppelt — Der Baustein merkt sich den gelungenen Versand und zeigt dann „gesendet, Hinweis nicht entfernt – bitte Ausblenden“; bUnit-Test ergänzt.
  - `[medium]` `[patch]` Edge: Konflikt bei „mitgeteilt“ nach dem Senden wird übergangen — gleiche Ursache und Korrektur wie Blind 1.
  - `[low]` `[reject]` Edge: Zwei Teammitglieder tippen gleichzeitig „Ja, informieren“ → zwei Nachrichten — Selten; die Prüfung vor dem Senden verkleinert das Fenster, ein Ausschluss bräuchte eine Reservierung auf dem Server.
  - `[maybe-false]` `[reject]` Edge: Neuladen nach dem Speichern liefert nichts, der alte Hinweis bleibt und „Ja“ sendet eine falsche Uhrzeit — Durch die neue Prüfung vor dem Senden abgefangen; ob der Fall ohne Netzfehler eintritt, wäre nur mit Fehlerinjektion zu prüfen.
  - `[maybe-false]` `[reject]` Edge: `Offen` bleibt `true` und die Bestätigung klappt nach einem Neuladen ohne eigene Verschiebung von selbst auf — Nur wenn der Hinweis verschwand und durch ein Neuladen wiederkommt (z. B. Konflikt im Autosave); dann ist das Aufklappen sachlich passend. Klären ließe es sich mit einem Test über diesen Weg; höchstens low.
  - `[low]` `[patch]` Edge: Im Wochenraster kann der Text aus veralteten Daten stammen — gleiche Ursache und Korrektur wie Blind 1.
  - `[low]` `[reject]` Edge: Bestehende Demo-Datenbank ohne Verschiebung — gleich wie Blind 9.
  - `[low]` `[reject]` Edge: Senden antwortet `Abgeschlossen`, Knöpfe bleiben aktiv — Selten (Abschluss während des Tippens); die Meldung erscheint, nach dem nächsten Laden fehlen die Knöpfe.
  - `[low]` `[patch]` Edge (claim): „204, auch ohne offenen Hinweis“ stimmt nicht — gleich wie Blind 3.
  - `[medium]` `[patch]` Edge (claim): Doku sagt „Alle informiert“, bei Konflikt bleibt der Hinweis — gleich wie Blind 1.
  - `[low]` `[patch]` Edge (claim): AC „genau eine Nachricht“ verletzt, wenn nach dem Senden ein Fehler „nicht gesendet“ meldet — gleich wie Edge 1.
  - `[low]` `[patch]` Gap: Zweites Verschieben öffnet die Bestätigung erneut, ungetestet (`@key="_aufklappen"`) — Seitentest ergänzt.
  - `[low]` `[patch]` Gap: Zurückschieben entfernt den Hinweis auf der Detailseite, ungetestet — Seitentest ergänzt.
  - `[low]` `[patch]` Gap: Ausblenden mit 409 (`ErneutVerschobenText`) ungetestet — Seitentest ergänzt.
  - `[low]` `[patch]` Gap (other): „Rückgängig“ verschwindet beim Hinweis, Doku schweigt — gleich wie Blind 4.
  - `[medium]` `[patch]` Gap (other): Ergebnis von `MitgeteiltAsync` wird nach dem Senden ignoriert — gleich wie Blind 1.
  - `[maybe-false]` `[reject]` Intent: Die ganze Kette „Ja“ → Nachricht → Push läuft in keinem Test am Stück — bUnit prüft den Aufruf mit `bestaetigt: true`, der API-Test dieselben zwei Aufrufe mit genau einem Push; ein E2E-Klick auf „Ja“ würde den Demo-Stand der übrigen Bilder ändern. Klären ließe es sich nur mit einem E2E-Test auf eigener Freizeit; höchstens low.
  - `[false]` `[reject]` Intent: „Seite verlassen schickt nichts“ ist nicht getestet — Ohne Klick auf „Ja“ gibt es keinen Codepfad, der sendet; der AD-13-Test zeigt, dass das Verschieben selbst nichts verschickt.
  - `[false]` `[reject]` Intent: Die Rückfrage öffnet nach dem Verschieben nur auf der Detailseite, nicht im Wochenraster — Die Uhrzeit lässt sich nur auf der Detailseite ändern (Story 3-1); im Raster gibt es nichts zu verschieben.
  - `[maybe-false]` `[reject]` Intent: Die Bilder zeigen den Raster, die Doku beschreibt das Aufklappen unter dem Feld — Die Doku beschreibt beide Orte, die Bilder zeigen dieselbe Rückfrage; ein drittes Bild wäre zusätzlicher Pflegeaufwand. Höchstens low.
  - `[false]` `[reject]` Intent: Kein 403- und kein Token-Test — Die Route erlaubt alle Freizeit-Rollen, ohne Rolle kommt 404 (RechtePruefung); 403 ist über `/test/nur-team` abgedeckt, ein Token-Endpunkt entsteht nicht (Design Notes).
  - `[false]` `[reject]` Intent: Testreihenfolge, Komplettlauf und Doku-Build sind im Diff nicht sichtbar — Kein Mangel des Codes; die Läufe stehen unter Auto Run Result.
  - `[maybe-false]` `[reject]` Intent: Demo-Idempotenz bei alter Datenbank — gleich wie Blind 9; höchstens low.

### 2026-10-20 — Review pass (Folgedurchlauf)
- verdicts: 33 findings — high 0, medium 5, low 20, false 6, maybe-false 2
- findings:
  - `[medium]` `[patch]` Blind: Nach gelungenem Senden in „Alle“ werden `Fehler` (500/404) und `Abgeschlossen` von „mitgeteilt“ verschluckt; die Seite meldet „Alle informiert“, der Hinweis bleibt ohne Erklärung, ein zweites Senden liegt nahe — `MitteilungsHinweis.InformierenAsync` setzt bei jedem Ergebnis außer `Erfolg` eine Meldung: `Konflikt` → „erneut verschoben“, sonst `GesendetHinweisBleibtText`; bUnit-Theorie (500, 409 `freizeit-abgeschlossen`) zuerst rot, dann grün; Satz in `programm.md` ergänzt.
  - `[low]` `[reject]` Blind: Nach 409 von „mitgeteilt“ steht neben der Fehlermeldung die grüne Meldung mit der alten Uhrzeit — Beide Aussagen stimmen (gesendet wurde der alte Text, der Punkt ist erneut verschoben); der Hinweis mit „Jetzt informieren“ zur neuen Uhrzeit bleibt als Korrekturweg.
  - `carried` `[low]` `[reject]` Blind: Zwei gleichzeitige „Ja, informieren“ senden doppelt — carried: wie Edge-Zeile „Zwei Teammitglieder tippen gleichzeitig“ im ersten Durchlauf; Code unverändert.
  - `[low]` `[reject]` Blind: Prüfung vor dem Senden meldet bei gelöschtem oder schon mitgeteiltem Punkt „erneut verschoben“ — Nur bei gleichzeitigem Löschen bzw. Informieren durch andere; im zweiten Fall verschwindet der Hinweis, das Ergebnis stimmt. Eigene Texte bräuchten weitere Zweige.
  - `[low]` `[reject]` Blind: Die Prüfung vor dem Senden lädt den ganzen Wochenraster — Einmal pro Klick, rund 40 Punkte; ein Einzelpunkt-Endpunkt wäre neue öffentliche Fläche.
  - `[low]` `[reject]` Blind: Der Server prüft nur `Beginn`, nicht den „statt“-Wert — Abweichung setzt eine Kette aus Verschieben, Informieren und Zurückschieben auf genau die alte Uhrzeit voraus; der Client prüft vor dem Senden beide Werte. Ein zweiter Parameter änderte den Vertrag.
  - `carried` `[low]` `[patch]` Blind: „Rückgängig“ verschwindet nach dem Verschieben — carried: wie Blind 4 im ersten Durchlauf (Doku nennt den Weg); Code unverändert.
  - `[false]` `[reject]` Blind: Der Fokus springt ungefragt auf „Nein“, keine Live-Region — Der Fokus auf „Nein“ nach dem Speichern ist ausdrücklich Teil des Intents (EXPERIENCE.md); mit dem Fokus liest der Screenreader die Bestätigung samt Satz vor.
  - `carried` `[low]` `[reject]` Blind: Gleich lautende Knöpfe im Wochenraster — carried: wie Blind 6 im ersten Durchlauf.
  - `[false]` `[reject]` Blind: Tippziele mit 32 px zu klein — EXPERIENCE.md verlangt mindestens 24 × 24 px (WCAG 2.5.8); 44 px gelten nur für Notfall-Button und untere Navigation.
  - `[low]` `[reject]` Blind: Fehlerpfade im Wochenraster ungetestet — Der Raster nutzt denselben Baustein `MitteilungsHinweis`, dessen Pfade über die Detailseite getestet sind; nur das Neuladen danach unterscheidet sich und ist getestet.
  - `carried` `[low]` `[reject]` Blind: Bestehende Demo-Datenbanken bekommen die Verschiebung nicht — carried: wie Blind 9 im ersten Durchlauf.
  - `[low]` `[reject]` Blind: Konflikttext doppelt in Endpunkt und Baustein — Der Server-Text ist das `detail` der ProblemDetails, die Oberfläche zeigt eigenen Text; Auseinanderlaufen schadet niemandem.
  - `[low]` `[reject]` Blind: Doku schweigt zu Protokoll von „Ausblenden“, Löschfrist, gleichzeitigem Informieren, Push nur mit Abo — `MitgeteilterBeginn` ist keine personenbezogene Angabe; Push-Voraussetzungen stehen in `kanaele.md`, auf das verlinkt ist.
  - `[medium]` `[patch]` Edge: `Fehler`/`Abgeschlossen` von „mitgeteilt“ nach dem Senden ohne Meldung — gleiche Ursache und Korrektur wie Blind 1.
  - `carried` `[low]` `[reject]` Edge: Senden und „mitgeteilt“ nicht atomar → doppelte oder veraltete Nachricht — carried: wie Edge-Zeile „Zwei Teammitglieder“ im ersten Durchlauf.
  - `[low]` `[reject]` Edge: Server ignoriert den angezeigten „statt“-Wert — gleich wie Blind 6 dieses Durchlaufs.
  - `[low]` `[reject]` Edge: „Ausblenden“ aus veralteter Ansicht blendet eine neuere Verschiebung aus — Nur wenn jemand anderes zwischen Anzeige und Klick erneut verschiebt; ausgeblendet wird nichts versendet, und ein Neuladen davor wäre ein weiterer Zweig.
  - `[low]` `[reject]` Edge: Gelöschter oder schon mitgeteilter Punkt → irreführende Meldung — gleich wie Blind 4 dieses Durchlaufs.
  - `[low]` `[reject]` Edge: Text nutzt den lokalen Titel, der inzwischen umbenannt sein kann — Gesendet wird genau der Satz, den die Person bestätigt hat; einen anderen Text zu senden widerspräche der Bestätigung.
  - `carried` `[maybe-false]` `[reject]` Edge: `Offen` klappt nach späterem Neuladen von selbst auf — carried: wie Edge-Zeile zu `Offen` im ersten Durchlauf; Code unverändert.
  - `carried` `[low]` `[reject]` Edge (claim): AC „genau eine Nachricht“ bei gleichzeitigem Informieren — carried: wie Edge-Zeile „Zwei Teammitglieder“ im ersten Durchlauf.
  - `[medium]` `[patch]` Edge (claim): Doku verspricht, der Hinweis verschwinde; bei `Fehler` bleibt er ohne Meldung — gleiche Ursache und Korrektur wie Blind 1; Doku nennt jetzt den Fall.
  - `[medium]` `[patch]` Gap: Kein Test für eine Fehlerantwort (nicht Ausnahme, nicht Konflikt) von „mitgeteilt“ nach dem Senden — Theorie `Detailseite_gesendet_aber_mitgeteilt_antwortet_mit_Fehler_meldet_es_ohne_zweites_Senden_anzubieten` ergänzt.
  - `[medium]` `[patch]` Gap (other): `Fehler`/`Abgeschlossen` werden als Erfolg behandelt — gleich wie Blind 1.
  - `[low]` `[reject]` Gap (other): `aktuell is null` meldet „erneut verschoben“ — gleich wie Blind 4 dieses Durchlaufs.
  - `carried` `[maybe-false]` `[reject]` Intent: Die Kette „Ja“ → Nachricht → Push läuft nicht am Stück — carried: wie Intent-Zeile im ersten Durchlauf.
  - `[low]` `[reject]` Intent: Automatisches Aufklappen nach echtem Speichern nur in bUnit, nicht im E2E — Ein E2E-Speichern veränderte den Demo-Stand der übrigen Bilder; bUnit deckt PATCH, Neuladen, Aufklappen und Fokus ab.
  - `[low]` `[reject]` Intent: Escape bei aufgeklappt beginnender Bestätigung ungetestet — Escape ist vorhandenes Verhalten von `InlineBestaetigung` und unabhängig davon, wie sie aufklappte.
  - `[false]` `[reject]` Intent: „mitgeteilt“ ohne offenen Hinweis 204 auch bei anderer Uhrzeit weicht ab — Die Matrix verlangt ausdrücklich „auch ohne offenen Hinweis 204“.
  - `carried` `[false]` `[reject]` Intent: Kein 403- und kein Token-Test — carried: wie Intent-Zeile im ersten Durchlauf (Design Notes).
  - `[false]` `[reject]` Intent: Gleichen Wert erneut speichern setzt nichts — Ohne neuen Wert gibt es keine Änderung von `Beginn`; die Regel spricht von einer Änderung.
  - `carried` `[false]` `[reject]` Intent: Doku-Build im Diff nicht sichtbar — carried: wie Intent-Zeile im ersten Durchlauf.

## Design Notes

- **Zusammensetzen im Client statt neuem Ereignis:** Programm und Kanäle haben keinen Pfeil zueinander (AD-2). Ein Ereignis „ProgrammpunktVerschoben“ mit Empfänger in Kanäle würde eine neue Zeile in der Spine verlangen und einen Versand aus einem Ereignis statt aus einer bestätigten Aktion erzeugen. Der Client verbindet zwei bestehende Aktionen: Senden in „Alle“, das der Server bestätigt verlangt, und „mitgeteilt“ im Programm. Scheitert der zweite Schritt, bleibt der Hinweis stehen. Das ist harmlos, man kann ausblenden.
- **Ein Feld statt Flag plus Altwert:** `MitgeteilterBeginn` trägt zugleich den Zustand „noch nicht mitgeteilt“ (gesetzt) und den Altwert für „statt 13:00“, auch nach einem Neuladen und auf anderen Geräten.
- **Uhrzeit im Mitgeteilt-Aufruf:** Verschiebt jemand den Punkt zwischen Anzeige und „Ja“ erneut, soll der Hinweis zur neuen Uhrzeit nicht verschwinden. Der Server leert ihn deshalb nur, wenn die gemeldete Uhrzeit die aktuelle ist (sonst 409 `konflikt`).
- **Erste Uhrzeit ist keine Verschiebung:** Ohne alte Uhrzeit gibt es nichts zu korrigieren, das hält die Regel einfach.
- **Tag im Text:** Eine Freizeit hat viele Mittagessen. Der Text nennt deshalb den Tag, abweichend vom kürzeren Beispiel in EXPERIENCE.md („Essen 13:30 statt 13:00“).
- **403 nicht erreichbar, keine Token-Fälle:** wie in 6-1. Die Route erlaubt Leitung und Team; ohne Rolle gibt es 404 (RechtePruefung). Der 403-Zweig ist über `/test/nur-team` abgedeckt. Es entsteht kein Token-Endpunkt.

## Verification

**Commands:**
- `dotnet test tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (gefiltert auf ProgrammTests, EndpunktListeTests, DemoDatenTests), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests`. Erwartet: grün.
- Am Ende einmal `dotnet test` für die ganze Lösung inklusive der E2E-Tests der Kategorie Doku, danach `npm run build` in `doku/`. Erwartet: grün, fehlerfrei, die neuen PNGs liegen in `doku/src/assets/screenshots/`.

## Auto Run Result

Status: done

**Umgesetzt:** „Alle informieren?“ nach dem Verschieben eines Programmpunkts (CAP-36, FR-62, AD-13).
- Programm merkt sich am Punkt `MitgeteilterBeginn`: Die erste Verschiebung speichert die alte Uhrzeit, Zurückschieben leert sie, eine erste Uhrzeit oder eine Titeländerung setzen nichts.
- Neuer Endpunkt `POST …/programm/punkte/{punktId}/mitgeteilt` (Leitung und Team). Er leert den Hinweis, wenn die gemeldete Uhrzeit aktuell ist oder kein Hinweis offen ist, sonst 409 `konflikt`.
- Das Verschieben selbst verschickt nichts. „Ja, informieren“ prüft den Punkt neu, schreibt den vorbereiteten Text („Geländespiel am Di 03.08.: 14:30 statt 14:00“) bestätigt in den Kanal „Alle“ (Push über die Outbox aus 6-1) und meldet danach „mitgeteilt“.
- „Nein“ oder Verlassen schickt nichts. Die Hinweiszeile „Geändert, noch nicht mitgeteilt“ mit „Jetzt informieren“ und „Ausblenden“ steht auf der Detailseite und im Wochenraster, nach dem Abschluss ohne Knöpfe.

**Dateien (Auswahl):**
- `src/Modules/Programm/Zeltlotse.Programm/Programme/Programmpunkt.cs`: Regel `BeginnSetzen`, `Mitgeteilt`.
- `src/Modules/Programm/Zeltlotse.Programm/Programme/ProgrammpunktMitgeteilt.cs` (neu): Command, Validator, Handler, Endpunkt; Registrierung in `ProgrammModul.cs`.
- `Migrationen/20261020152820_MitgeteilterBeginn*` und Snapshot; `WochenrasterLaden.cs` bildet das Feld ab.
- `src/Zeltlotse.Api.Contracts/Programm/Programm.cs`: `MitgeteilterBeginn`, `ProgrammpunktMitgeteilt`.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs`: `MitgeteiltAsync`.
- `src/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor` (Parameter `Offen`), `Hinweiszeile.razor` (neu), `wwwroot/css/zeltlotse.css`.
- `src/Zeltlotse.App/Features/Programm/MitteilungsHinweis.razor` (neu), `ProgrammpunktSeite.razor`, `Wochenraster.razor`, `ProgrammTexte.cs`.
- Demo: `DemoKatalog.cs`/`DemoDaten.cs` (Geländespiel 14:00 → 14:30, nicht mitgeteilt).
- Tests: `ProgrammpunktTests`, `ProgrammTests` (Matrix, Rechte, Gelaufen, AD-13 ohne Versand), `EndpunktListeTests`, `DemoDatenTests`, `KomponentenTests`, `ProgrammSeitenTests`, E2E `Programm_mitteilen` (Handy, Laptop).
- Doku: `mitarbeitende/programm.md` (Abschnitt „Alle informieren“ mit `programm-mitteilen-handy/-laptop.png`), `mitarbeitende/kanaele.md`, `daten.md`. Neu erzeugt mit sichtbarer Änderung: `wochenraster-laptop.png`, `material-programmpunkt-laptop.png`.

**Review:** 35 Funde von vier Prüfern (Details im Triage-Log).
- Patches:
  - Prüfung des Punkts vor dem Senden; Konflikt nach dem Senden wird gemeldet (medium).
  - Gelungener Versand mit späterem Fehler meldet „gesendet, bitte Ausblenden“ statt „nicht gesendet“ (medium).
  - „mitgeteilt“ ohne offenen Hinweis ist immer 204.
  - Drei Seitentests (zweites Verschieben, Zurückschieben, Ausblenden 409) sowie Tests zu den Patches.
  - Doku: Datenschutz-Satz zu Kanal-Nachrichten, Hinweis zu „Rückgängig“, Alt-Text.
- Zurückgestellt: nichts.
- Verworfen: 20 Funde mit Grund im Triage-Log (4 false, 5 maybe-false, Rest low ohne Alltagsrelevanz).

**Folge-Review empfohlen:** ja. Gepatcht wurden 0 high, 2 medium und mehrere low. Offenes Risiko: Senden in „Alle“ und „mitgeteilt“ sind zwei Aufrufe des Clients. Gleichzeitige Verschiebungen oder zwei gleichzeitige „Ja“ können eine veraltete oder doppelte Nachricht erzeugen. Die neue Prüfung vor dem Senden ist nur mit bUnit-Nachbildungen getestet, nicht am Stück gegen den echten Server.

**Verifikation:**
- Nach den Patches `dotnet test` für die ganze Lösung: 3032 von 3032 grün, inklusive Architekturtests und E2E mit Doku-Screenshots.
- `npm run build` in `doku/` fehlerfrei (67 Seiten, nur der bekannte Sitemap-Hinweis).
- 31 ältere Screenshots hatten nur Rendering-Rauschen und sind zurückgesetzt.

**Restrisiken:**
- Eine bestehende Demo-Datenbank aus der Zeit vor dieser Story zeigt den Hinweis nicht (Geländespiel steht dort schon auf 14:30); eine frische Demo-Datenbank schon.
- Nach einer Verschiebung mit Hinweis ist „Rückgängig“ am Feld weg; die Doku nennt den Weg über die alte Uhrzeit.

### Folgedurchlauf 2026-10-20

**Umgesetzt:** Korrektur in `MitteilungsHinweis.InformierenAsync`. Ist die Nachricht in „Alle“ gesendet und antwortet „mitgeteilt“ danach mit einem Fehler (z. B. 500) oder mit `freizeit-abgeschlossen`, steht jetzt „Die Nachricht ist gesendet, der Hinweis ließ sich aber nicht entfernen. Bitte „Ausblenden“ wählen.“ Bisher stand dort nur „Alle informiert“ ohne Erklärung, und ein zweites Senden lag nahe.

**Dateien:**
- `src/Zeltlotse.App/Features/Programm/MitteilungsHinweis.razor`: Jedes Ergebnis von „mitgeteilt“ außer `Erfolg` setzt eine Meldung (`Konflikt` → erneut verschoben, sonst gesendet/Hinweis bleibt).
- `tests/Zeltlotse.Ui.Tests/ProgrammSeitenTests.cs`: Theorie `Detailseite_gesendet_aber_mitgeteilt_antwortet_mit_Fehler_meldet_es_ohne_zweites_Senden_anzubieten` (500 und 409 `freizeit-abgeschlossen`), erst rot, dann grün.
- `doku/src/content/docs/mitarbeitende/programm.md`: Satz zu diesem Fall („nicht erneut informieren, sondern Ausblenden“).

**Review:** 33 Funde von vier Prüfern (Details im Triage-Log, Abschnitt „Folgedurchlauf“).
- Gepatcht: 1 Eintrag mit Verdikt medium (5 Zeilen gleicher Ursache aus allen Prüfern außer Intent). Kein high.
- Zurückgestellt: nichts.
- Verworfen: 6 false (u. a. Tippziele: EXPERIENCE.md verlangt 24 px, vorhanden sind 32 px; Fokus auf „Nein“ ist Intent; 204 ohne offenen Hinweis steht in der Matrix), 2 maybe-false (carried) und 20 low (davon 7 carried aus dem ersten Durchlauf). Alle Gründe stehen im Triage-Log.

**Folge-Review empfohlen:** nein. Im Folgedurchlauf wurde kein high gepatcht (0 high, 1 medium); die Arbeit ist konvergiert.

**Verifikation:**
- Alle Testprojekte einzeln, zusammen 3034 von 3034 grün: Ui 836, Api 1028, E2E 193 (mit Doku-Screenshots Handy und Laptop), Architektur 136 und die Modulprojekte.
- Ein erster Gesamtlauf `dotnet test` meldete einen Verbindungsfehler zur Datenbank in Konten.Tests unter Last; das Projekt allein lief 26/26 grün, und Konten ist von der Story nicht berührt.
- `npm run build` in `doku/` fehlerfrei (67 Seiten, nur der bekannte Sitemap-Hinweis).
- 29 Screenshots, die der E2E-Lauf nur mit Rendering-Rauschen neu erzeugt hatte, sind zurückgesetzt; die Korrektur betrifft keinen abgebildeten Zustand.

**Restrisiken:** unverändert gegenüber dem ersten Durchlauf. Senden und „mitgeteilt“ sind zwei Aufrufe des Clients, sodass zwei gleichzeitige „Ja, informieren“ doppelt senden können (selten, bewusst verworfen). Eine alte Demo-Datenbank zeigt den Hinweis nicht.
