---
title: 'Story 7-1: Anwender-Doku abschließen'
type: 'chore'
created: '2026-10-22'
baseline_revision: '12210eb0285efb107286fc4093ff5f998a5b70c3'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/rollen-und-rechte.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/nutzungsszenarien.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Anwender-Doku ist mit den Fach-Stories gewachsen, aber es fehlt eine Rollenübersicht auf einen Blick, der Einstieg führt nicht über die typischen Aufgaben aus den Nutzungsszenarien, und die Seite „Was passiert mit meinen Daten“ ist lang ohne Kurzfassung. Außerdem prüft bisher nichts automatisch, dass jede Capability mit Oberfläche (CAP-1 bis CAP-39) eine Doku-Seite mit Screenshots hat und jedes Bild von einem Playwright-Test erzeugt wird, also aktuell ist (CAP-40, NFR-20).

**Approach:** Neue Seite `rollen` (Rollen im Überblick) nach `rollen-und-rechte.md`, Einstieg `index` um „Häufige Aufgaben“ nach Nutzungsszenarien ergänzen, `daten` bekommt oben „Auf einen Blick“ zu Freigabefenster, Einsicht und Löschen. Testgetrieben: neue Regeln in den Architekturtests (reiner Repo-Scan) prüfen die Zuordnung CAP → Seite mit Handy- und Laptop-Screenshot und dass jedes Bild einen Erzeuger in den E2E-Tests hat.

## Boundaries & Constraints

**Always:** Sprache wie in der App: Deutsch, ohne Du/Sie, Fachbegriffe laut Glossar (Anrede-Regel muss grün bleiben). Inhalte nur aus SPEC, `rollen-und-rechte.md`, `nutzungsszenarien.md` und den bestehenden Doku-Seiten, nichts erfinden, was die App nicht kann. Links innerhalb der Doku in der bestehenden Form `/ordner/seite/`. Erst die Tests (rot), dann Seiten und Zuordnung (grün).

**Never:** Keine App-, API- oder Datenbank-Änderung; keine neuen Screenshots oder Playwright-Tests (alle Oberflächen haben schon welche); keine Node-Abhängigkeit in den Architekturtests; bestehende Fach-Seiten nicht umschreiben (nur Links ergänzen, wo nötig); keine Personendaten außer den erfundenen Demo-Namen.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vollständig | Jede CAP 1–39 hat Seiten; jede Seite existiert und verlinkt je ein `-handy.png` und `-laptop.png` | Regel liefert keine Verstöße | – |
| CAP ohne Seite | Zuordnung ohne Eintrag für eine CAP-Nummer 1–39 | Verstoß „CAP-n hat keine Doku-Seite.“ | Test rot |
| Seite fehlt | Zuordnung nennt Seite, Datei fehlt | Verstoß „CAP-n: Seite x.md fehlt.“ | Test rot |
| Ohne Bildpaar | Seite ohne `-handy.png` oder ohne `-laptop.png`-Link | Verstoß „CAP-n: Seite x.md hat keinen Handy-Screenshot.“ bzw. „… keinen Laptop-Screenshot.“ | Test rot |
| Bild ohne Erzeuger | `foo-handy.png` im Bildordner, kein `BildAsync(…, "foo", …)` in `tests/Zeltlotse.E2E.Tests/*.cs` | Verstoß „Bild foo-handy.png wird von keinem Playwright-Test erzeugt.“ | Test rot |
| Erzeuger ohne Bildpaar | `BildAsync(…, "foo", …)` vorhanden, aber nur `foo-handy.png` liegt vor | Verstoß „Bild foo-laptop.png fehlt.“ | Test rot |

</intent-contract>

## Code Map

- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- Doku-Regeln als reine Funktionen (Verstöße als Sätze). Erweitern: `PflichtSeiten` um `rollen`; neue `CapabilitySeiten` (CAP-Nummer → Seiten) und Regeln `CapabilityVerstoesse(...)`, `ErzeugerVerstoesse(...)`. Vorbild: `ScreenshotVerstoesse`, `ScreenshotLink()`.
- `tests/Zeltlotse.ArchitectureTests/DokuTests.cs` -- Echte Prüfung gegen das Repo plus `Negativ_…`-Tests mit kleinen Eingaben; `Negativ_fehlende_Pflichtseiten…` erwartet die vollständige Liste und muss `rollen` aufnehmen. Hilfsfunktion `DokuDateien`, `Loesung.Wurzel`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:3139` -- `BildAsync(IPage seite, string name, string geraet)` schreibt `<name>-<geraet>.png`; alle Aufrufe haben den Namen als String-Literal (103 Namen, je Handy und Laptop über `Geraete`). Nur lesen.
- `doku/astro.config.mjs` -- Sidebar; neuer Eintrag „Rollen im Überblick“ (`slug: 'rollen'`) direkt nach „Startseite“.
- `doku/src/content/docs/index.md` -- Einstieg mit „Wegweiser nach Rolle“ und „Erste Schritte“; ergänzen.
- `doku/src/content/docs/daten.md` -- Datenseite; vorne „Auf einen Blick“ einfügen, Rest bleibt.
- `doku/src/content/docs/*/index.md` -- Rollen-Überblicke (Vorbild für Ton und Aufbau); Org-Überblick steht in `org-admin/index.md#org-überblick`, Teilnehmer-Zugang in `leitung/teilnehmer-zugang.md#was-teilnehmende-sehen`.

Zuordnung CAP → Seiten (Stand Prüfung, alle Seiten existieren und haben Handy- und Laptop-Bilder):
1 `erste-schritte/anmelden` · 2 `eltern/familienkonto`, `eltern/profil-uebergeben` · 3 `betreiber/index`, `org-admin/rollen-vergeben`, `org-admin/freizeit-anlegen`, `leitung/freizeit-oeffnen` · 4 `mitarbeitende/nachweise`, `leitung/nachweise-pruefen`, `org-admin/nachweis-richtlinie` · 5 `leitung/nachweise-erinnern` · 6 `erste-schritte/meine-daten` · 7 `eltern/freizeitseite`, `leitung/freizeitseite-einrichten` · 8 `eltern/kind-anmelden`, `mitarbeitende/anmelden` · 9 `leitung/anmeldungen` · 10 `eltern/elterninfo` · 11 `leitung/freizeitpass-fragen` · 12 `eltern/freizeitpass` · 13 `mitarbeitende/freizeitpaesse` · 14 `leitung/cockpit` · 15 `mitarbeitende/notfall`, `leitung/sicherheitskette` · 16 `leitung/schutzkonzept`, `mitarbeitende/schutzkonzept` · 17 `mitarbeitende/vorfall-protokoll`, `leitung/vorfall-notizen`, `org-admin/vorfall-archiv` · 18 `mitarbeitende/notfallordner` · 19 `mitarbeitende/programm` · 20 `mitarbeitende/material` · 21 `org-admin/inventar`, `mitarbeitende/inventar` · 22 `mitarbeitende/schaeden-verluste`, `org-admin/anschaffungswuensche` · 23 `mitarbeitende/aufgaben`, `org-admin/aufgaben-vorlagen` · 24 `mitarbeitende/programmbibliothek`, `org-admin/programmbibliothek` · 25 `mitarbeitende/dokumente`, `org-admin/org-dokumente` · 26 `leitung/module-schalten` · 27 `leitung/freizeit-abschliessen` · 28 `kueche/rezepte` · 29 `kueche/essensplan`, `kueche/einkaufsliste` · 30 `kueche/einkauf` · 31 `kueche/tagesansicht` · 32 `finanzen/kalkulation`, `finanzen/beitragsrechner` · 33 `finanzen/belege`, `finanzen/soll-ist` · 34 `finanzen/beitraege` · 35 `mitarbeitende/kanaele` · 36 `mitarbeitende/programm` · 37 `mitarbeitende/kanaele` · 38 `leitung/teilnehmer-zugang` · 39 `mitarbeitende/offline`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.ArchitectureTests/DokuTests.cs` -- zuerst: Tests `Jede_Capability_mit_Oberflaeche_hat_eine_Doku_Seite_mit_Handy_und_Laptop_Screenshot`, `Jedes_Doku_Bild_wird_von_einem_Playwright_Test_erzeugt` und Negativ-Tests für jede Zeile der I/O-Matrix; `rollen` in der erwarteten Liste des Pflichtseiten-Negativtests -- Test vor dem Code (rot).
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- `CapabilitySeiten` mit der Zuordnung oben, `CapabilityVerstoesse(zuordnung, seiten)`, `ErzeugerVerstoesse(bilder, testQuellen)` (Regex auf `BildAsync(<Ausdruck>, "<name>"`), `rollen` in `PflichtSeiten` -- macht Vollständigkeit prüfbar.
- `doku/src/content/docs/rollen.md` -- neue Seite „Rollen im Überblick“: Tabelle aller Rollen aus `rollen-und-rechte.md` (Global Admin, Org-Admin, Org-Überblick, Leitung, Team, Eltern, volljährige Teilnehmende, Teilnehmende mit Teilnehmer-Zugang) mit „Wer“, „Darf“ und Link zur passenden Doku-Gruppe; Absatz „Aufgaben statt Rollen“ (Küche, Einkauf, Tagesleitung, Finanzen); Absatz „Wie Rechte entstehen“ (Einladung, Bestätigung der Anmeldung, erste bestätigte Mitarbeit); Absatz „Eine Person, mehrere Rollen“ (ein Profil über alle Organisationen); Verweis auf `/daten/`.
- `doku/astro.config.mjs` -- Sidebar-Eintrag „Rollen im Überblick“ nach „Startseite“.
- `doku/src/content/docs/index.md` -- Link auf `/rollen/` im Wegweiser; neuer Abschnitt „Häufige Aufgaben“ als Tabelle „Aufgabe → Seite“ entlang NS-1 bis NS-13 (z. B. Kind anmelden, Freizeitpass aktuell halten, Nachweise hochladen, Freizeit planen/Cockpit, Geländespiel planen, Notfall, Einkaufsliste, Belege, Programm verschieben, Teilnehmer-Zugang, Profil mit 18 übergeben, Freizeit abschließen, Anschaffungswünsche) und Satz zum Aufbau der Anleitung (Rolle → Aufgabe, Bilder für Handy und Laptop aus erfundenen Demo-Daten).
- `doku/src/content/docs/daten.md` -- neuer erster Abschnitt „Auf einen Blick“ mit je einem Klartext-Punkt zu Freigabe, Freigabefenster, Einsicht/Zugriffsprotokoll und Löschen, jeweils mit Anker-Link in die ausführlichen Abschnitte.

**Acceptance Criteria:**
- Given die fertige Doku, when `dotnet test --project tests/Zeltlotse.ArchitectureTests` läuft, then sind alle Doku-Regeln grün, inklusive CAP-Zuordnung, Bild-Erzeuger und Pflichtseite `rollen`.
- Given die gebaute Doku, when die Startseite geöffnet wird, then führt sie per Link zu „Rollen im Überblick“, zu jeder Rollen-Gruppe und über „Häufige Aufgaben“ zu den Aufgaben-Seiten; die Sidebar zeigt „Rollen im Überblick“.
- Given die Seite „Was passiert mit meinen Daten“, when sie geöffnet wird, then erklärt „Auf einen Blick“ Freigabefenster, Einsicht und Löschen in Klartext vor den Details.
- Given `npm run build` in `doku/`, when er läuft, then endet er ohne Fehler, und alle internen Links der neuen und geänderten Seiten zeigen auf vorhandene Seiten.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 26 findings — high 0, medium 5, low 17, false 4, maybe-false 0
- findings:
  - `[medium]` `[patch]` Blind: Leitungs-Zeile in `rollen.md` nennt weniger „nur sie“-Rechte als `leitung/index.md` — Liste an `leitung/index.md` angeglichen (Freizeitseite/Anmeldelinks, Cockpit, Vorfall-Notizen, Nachweise prüfen, Schutzkonzept).
  - `[low]` `[patch]` Blind: Gast-Weg ohne Konto fehlt in `rollen.md` — Eltern-Zeile und „Wie Rechte entstehen“ um Gast-Anmeldung mit Gast-Link ergänzt.
  - `[medium]` `[patch]` Blind: Team-Zeile ohne „ab Bestätigung“, widerspricht `daten.md` — auf „ab Bestätigung der Anmeldung bis zum Ende des Freigabefensters“ korrigiert.
  - `[medium]` `[patch]` Blind: „Auf einen Blick“/Löschen verspricht „alle persönlichen Daten“ und verschweigt Mitlöschen der Kinder — Punkt präzisiert (Kinderprofile bei einzigem Elternteil, Einträge bei anderen ohne Namen).
  - `[false]` `[reject]` Blind: „Jeder Blick … wird festgehalten“ zu stark — der Detailabschnitt derselben Seite sagt wörtlich „jeden Blick von Leitung oder Team auf die Angaben eines Freizeitpasses“; Kurzfassung verspricht nicht mehr.
  - `[low]` `[patch]` Blind: „Auf einen Blick“ ohne Nachweise und Offline — je ein Punkt mit Anker ergänzt.
  - `[low]` `[patch]` Blind: „Häufige Aufgaben“ ohne Meine Daten, Kanal „Alle“, Offline, Freizeit anlegen, Soll-Ist — Zeilen ergänzt.
  - `[low]` `[patch]` Blind: Handy- und Laptop-Bild verschiedener Bildschirme bestehen die CAP-Regel — Regel verlangt jetzt ein Paar gleichen Namens, neue Meldung „hat kein Bildpaar aus Handy und Laptop“, Negativ-Test.
  - `[low]` `[reject]` Blind: CAP 1–39 fest im Code, nicht aus `SPEC.md` gelesen — SPEC ist der feste MVP-Vertrag; ein SPEC-Parser wäre zusätzliche Speziallogik für einen unwahrscheinlichen Fall.
  - `[low]` `[patch]` Blind: auskommentierte `BildAsync`-Aufrufe zählen als Erzeuger — Kommentare werden vor dem Abgleich entfernt, Negativ-Test.
  - `[low]` `[reject]` Blind: Scan nur im Hauptordner, Regex nur eine Klammerebene — scheitert laut (roter Test), nie stilles Durchlassen; heute keine Unterordner.
  - `[low]` `[patch]` Edge: `a-handy.png` + `b-laptop.png` besteht — gleiche Ursache wie Bildpaar-Befund oben, dort behoben.
  - `[low]` `[reject]` Edge: `x.md` und `x.mdx` gleichen Namens — kommt in der Doku nicht vor, Schutz bräuchte zusätzlichen Zweig.
  - `[low]` `[reject]` Edge: Endung `.PNG` groß — würde laut als Verstoß gemeldet, nicht still durchgelassen; Playwright schreibt immer `.png`.
  - `[low]` `[reject]` Edge: Regex erkennt verschachtelte Ausdrücke/Konstanten nicht — laute Fehlmeldung, alle heutigen Aufrufe nutzen Literale.
  - `[low]` `[patch]` Edge: auskommentierte Erzeuger — gleiche Ursache wie oben, dort behoben.
  - `[low]` `[reject]` Edge: E2E-Dateien in Unterordnern übersehen — laute Fehlmeldung, heute keine Unterordner.
  - `[false]` `[reject]` Edge: Aktualität nur als Textabgleich — `BildAsync` prüft bei jedem Lauf, dass die Datei neu geschrieben wurde; die Regel stellt nur sicher, dass es zu jedem Bild diesen Lauf gibt (Design Notes).
  - `[medium]` `[patch]` Verification-Gap: interne Links und Anker prüft nichts, `astro build` scheitert nicht daran — neue Regel `LinkVerstoesse` (Seite und Überschrift-Anker nach Starlight-Slug) mit Repo-Test, Negativ-Tests und Slug-Theorie; keine bestehenden Links kaputt.
  - `[low]` `[patch]` Verification-Gap (Other): auskommentierte `BildAsync` zählen — gleiche Ursache wie oben, dort behoben.
  - `[false]` `[reject]` Intent: „aktuell“ nur als Erzeuger-Existenz — die Screenshots erzeugt der volle Testlauf neu und `BildAsync` prüft das Schreiben; die Story verlangt keinen Pixelvergleich (0-4).
  - `[low]` `[reject]` Intent: Zuordnung fest im Code — wie oben (SPEC-Parser abgelehnt).
  - `[low]` `[reject]` Intent: Prüfung je Seite, nicht je Bildschirm — Zuordnung wurde von Hand geprüft; ein zweites Register Bildschirm→Bild wäre Speziallogik.
  - `[medium]` `[patch]` Intent: Links nur über den Build geprüft — gleiche Ursache wie Verification-Gap-Befund, dort behoben.
  - `[false]` `[reject]` Intent: AD-30-Hinweis unbehandelt — Design Notes halten fest, dass kein Endpunkt entsteht; die HTTP-Tests laufen im Abschlusslauf.
  - `[low]` `[reject]` Intent: Inhalt von `rollen.md` nur per Review gegen `rollen-und-rechte.md` geprüft — Review hat die Abweichungen gefunden und sie sind behoben; ein Textvergleichs-Test wäre Speziallogik.

### 2026-10-22 — Review pass (Folgedurchlauf)
- verdicts: 26 findings — high 0, medium 0, low 21, false 5, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: CAP-Regel prüft nur Bildpaar je Seite, nicht den Abschnitt der Capability (CAP-36/37 teilen Seiten) — carried: wie Zeile „Prüfung je Seite, nicht je Bildschirm“ oben.
  - `[low]` `[reject]` Blind: CAP 1–39 fest im Code, nicht aus `SPEC.md` — carried: SPEC-Parser abgelehnt (siehe oben).
  - `[low]` `[reject]` Blind: Link-Prüfung übersieht Links mit Titel, relative Links, Referenz-Links, `<a href>` — Suche in `doku/src/content/docs` findet keinen solchen Link; zusätzliche Formen bräuchten neue Zweige für einen heute nicht vorkommenden Fall.
  - `[low]` `[reject]` Blind: Code-Block-/Anker-Behandlung einfacher als Starlight (`~~~`, `#` im Frontmatter, github-slugger-Kollision „x-1“) — keine Doku-Seite hat `#`-Zeilen im Frontmatter oder `~~~`-Blöcke; Abdeckung aller Varianten wäre zusätzliche Logik ohne heutigen Fall.
  - `[low]` `[reject]` Blind: keine Prüfung auf Bilder, die keine Seite verwendet — heute keine verwaisten Bilder, nicht Teil der Intent; neue Regel wäre zusätzliche Speziallogik.
  - `[low]` `[reject]` Blind: Erzeuger-Erkennung fragil (interpolierte Namen, Raw-Strings, Unterordner) — carried für Regex/Unterordner; Raw-Strings (`DokuScreenshotTests.cs:186`) führen höchstens zu einer lauten Fehlmeldung, die Regel ist mit ihnen heute grün.
  - `[low]` `[reject]` Blind: `rollen.md` erklärt nicht, wie Rechte enden — `rollen-und-rechte.md` beschreibt das Ende nicht als eigenen Punkt; Ergänzung riskierte „nichts erfinden“, die Fach-Seiten (Freigabefenster, Absagen, Teilnehmer-Zugang endet mit der Freizeit) erklären es schon.
  - `[low]` `[reject]` Blind: „Häufige Aufgaben“ ohne Rollen vergeben, Löschfristen, Inventar, Essensplan, Beiträge — die Tabelle folgt laut Intent NS-1 bis NS-13 und deckt sie ab; die übrigen Aufgaben erreicht der Wegweiser nach Rolle.
  - `[false]` `[reject]` Blind: „Die Bilder zeigen jeden Bildschirm am Handy und am Laptop“ stimmt nicht, weil Übersichtsseiten keine Bilder haben — der Satz meint Bildschirme der App, nicht Doku-Seiten; jede CAP-Seite hat ein Bildpaar (Regel grün).
  - `[low]` `[reject]` Blind: „30 Tage“/„12 Monate“ in „Auf einen Blick“ ohne Test gegen die Code-Konstanten — dieselben Werte stehen schon in den bestehenden Detailabschnitten von `daten.md`, `freizeit-anlegen.md` und `loeschfristen.md`; ein Abgleichstest wäre Speziallogik.
  - `[false]` `[reject]` Blind: „Mitarbeitende der Organisation“ und Gast fehlen in der Rollentabelle — `rollen-und-rechte.md` führt beide nicht als eigene Rolle; `rollen.md` beschreibt beide unter „Wie Rechte entstehen“ und in der Eltern-Zeile.
  - `[low]` `[reject]` Blind: Kleinere Lücken (`.PNG`, Seite ganz ohne Bilder ungetestet, doppelter Link, Bild in HTML-Kommentar) — `.PNG` carried; die übrigen Fälle kommen nicht vor, und ein Schutz bräuchte zusätzliche Zweige.
  - `[low]` `[reject]` Edge: E2E-Dateien in Unterordnern — carried (siehe oben).
  - `[low]` `[reject]` Edge: Raw-String-Literale mit `//` stören `OhneKommentare` — schlägt laut fehl, die Regel ist heute mit den vorhandenen Raw-Strings grün; der Schutz wäre eine zusätzliche Regex-Alternative.
  - `[low]` `[reject]` Edge: Name als `$"…"`, Konstante oder tiefer verschachtelter Ausdruck — carried (siehe oben).
  - `[low]` `[reject]` Edge: Endung `.PNG` — carried (siehe oben).
  - `[low]` `[reject]` Edge: Links mit Titel oder in spitzen Klammern werden still übersprungen — gleiche Ursache wie die Blind-Zeile zu Link-Formen, dort abgelehnt.
  - `[low]` `[reject]` Edge: relative Links, Referenz-Links, `<a href>` — gleiche Ursache wie die Blind-Zeile zu Link-Formen, dort abgelehnt.
  - `[low]` `[reject]` Edge: `~~~`, eingerückte Blöcke, HTML-Kommentare, `#` im Frontmatter — gleiche Ursache wie die Blind-Zeile zu Code-Block/Anker, dort abgelehnt.
  - `[low]` `[reject]` Edge: Screenshot-Link nur im Code-Block/Kommentar zählt für die CAP-Regel — kommt in der Doku nicht vor; selten und nur über zusätzliche Bereinigung lösbar.
  - `[low]` `[reject]` Edge: `x.md` und `x.mdx` gleichen Namens — carried (siehe oben).
  - `[false]` `[reject]` Intent: „aktuell“ nur als Erzeuger-Existenz statt Frische — carried (siehe oben).
  - `[low]` `[reject]` Intent: CAP-Abdeckung strukturell statt inhaltlich — carried (Prüfung je Seite).
  - `[low]` `[reject]` Intent: Inhalte von `rollen`, `index`, `daten` nur über Existenz und Links geprüft — carried (Textvergleichs-Test abgelehnt).
  - `[false]` `[reject]` Intent: Zusätze über die Intent hinaus (Link-Prüfung, Bildpaar-Regel, „Aufbau der Anleitung“, weitere Punkte in „Auf einen Blick“) — keine benannte Wirkung zum Schaden; die Zusätze stammen aus gepatchten Befunden des ersten Reviews, und die Intent verbietet sie nicht.
  - `[false]` `[reject]` Intent: Reihenfolge „Test zuerst“ im Diff nicht erkennbar — das ist keine Fehlwirkung im Code, sondern ein Prozesshinweis.
- Verification-Gap: keine Lücken gemeldet, keine weiteren Befunde.

## Design Notes

- **Zuordnung als Code statt Frontmatter:** Die CAP → Seite-Liste steht in `DokuRegeln`, weil die Doku-Seiten Anwendertext sind und keine Capability-Kennungen tragen sollen. Sie ist zugleich die Grundlage für den Abnahmebericht (Story 7-2).
- **„Aktuell“ heißt „hat einen Erzeuger“:** Jeder Screenshot-Test überschreibt sein Bild bei jedem Lauf und prüft das (`BildAsync`). Ein Bild ohne Erzeuger wäre veraltet; darum verlangt die Regel zu jedem Bild einen `BildAsync`-Aufruf mit dem Namen. Pixelvergleiche gibt es weiterhin nicht.
- **CAP-40 ohne eigene Oberfläche:** CAP-40 ist die Doku selbst; sie ist über Pflichtseiten (`index`, `rollen`, `daten`) und die neuen Regeln abgedeckt, nicht über die Zuordnung.
- **Übersichtsseiten ohne Bilder:** `index`, `rollen`, `daten` und die Rollen-Überblicke erklären Regeln, nicht Bildschirme; sie brauchen keine Screenshots (wie in 0-4 festgelegt).
- **AD-30-Pflichtfälle (Hinweis des Aufrufers):** Diese Story legt keinen HTTP-Endpunkt an und ändert keinen; falsche Rolle 403, fremde Organisation 404 und Token-Fälle haben keinen Gegenstand. Die bestehenden HTTP-Tests gegen PostgreSQL laufen im Abschlusslauf mit.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- expected: alle grün (vor der Umsetzung der Seiten rot bei den neuen Tests).
- `npm run build` (in `doku/`) -- expected: Exit-Code 0, keine Warnung zu kaputten Bildern.
- `dotnet test` (ganze Lösung, einmal am Ende, Docker nötig) -- expected: alle grün, Screenshots neu geschrieben.

## Auto Run Result

Status: done

**Umgesetzt:** Neue Seite „Rollen im Überblick“ (alle acht Rollen aus `rollen-und-rechte.md` mit Link zur Doku-Gruppe, Aufgaben statt Rollen, wie Rechte entstehen inklusive Gast-Link, eine Person mit mehreren Rollen). Der Einstieg verlinkt sie und hat neu „Häufige Aufgaben“ entlang NS-1 bis NS-13 und „Aufbau der Anleitung“. „Was passiert mit meinen Daten“ beginnt mit „Auf einen Blick“ (Freigabe, Freigabefenster, Einsicht, Nachweise, Offline, Löschen). Vollständigkeit ist jetzt automatisch geprüft: CAP-1 bis CAP-39 → Doku-Seiten mit Handy-/Laptop-Bildpaar, jedes Bild hat einen `BildAsync`-Erzeuger (auskommentierte zählen nicht), alle internen Links und Anker zeigen auf vorhandene Seiten und Überschriften.

**Dateien:**
- `doku/src/content/docs/rollen.md` – neue Seite „Rollen im Überblick“.
- `doku/astro.config.mjs` – Sidebar-Eintrag „Rollen im Überblick“.
- `doku/src/content/docs/index.md` – Link auf die Rollenübersicht, „Häufige Aufgaben“, „Aufbau der Anleitung“.
- `doku/src/content/docs/daten.md` – Abschnitt „Auf einen Blick“.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` – Pflichtseite `rollen`, `CapabilitySeiten`, `CapabilityVerstoesse`, `ErzeugerVerstoesse`, `LinkVerstoesse` mit Starlight-Anker.
- `tests/Zeltlotse.ArchitectureTests/DokuTests.cs` – Repo-Tests und Negativ-Tests für jede Zeile der I/O-Matrix, Links und Anker.

**Review:** 26 Befunde (0 hoch, 5 mittel, 17 niedrig, 4 falsch). Patches: 4 mittlere Einträge (Leitungs-Rechte, Team ab Bestätigung, Löschen-Kurzfassung, Link-Prüfung) und 5 niedrige (Gast-Weg, Nachweise/Offline in der Kurzfassung, weitere häufige Aufgaben, Bildpaar gleichen Namens, auskommentierte Erzeuger). Nichts zurückgestellt. Abgelehnt mit Begründung im Triage Log: SPEC-Parser für CAP-Nummern, Prüfung je Bildschirm, Textvergleich mit `rollen-und-rechte.md`, laut scheiternde Randfälle der Regex/Ordner-Suche, `.md`/`.mdx`-Doppelung; falsch: „jeder Blick“, Aktualität, AD-30-Hinweis.

**Folge-Review empfohlen: ja** – mehrere mittlere Befunde gepatcht (4 mittel, 5 niedrig). Ungeprüftes Risiko: Die neue Anker-Berechnung in `LinkVerstoesse` bildet Starlights Slug-Regeln nach; ob sie bei Sonderzeichen in künftigen Überschriften (z. B. „&“, Anführungszeichen, Emojis) genauso rechnet, hat nur der Abgleich mit einigen gebauten Ankern belegt. Die korrigierten Texte hat kein zweiter Review gelesen.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` – 154/154 grün (nach den Patches).
- `dotnet test` (ganze Lösung) – alle Modul-, Api-, Ablauf-, Ui- und Architektur-Projekte grün; das E2E-Projekt scheiterte in diesem Lauf nur beim Veröffentlichen des Hosts (Zeitüberschreitung bei voller Auslastung, alle 208 Tests mit 0 ms) und lief danach allein komplett grün (`dotnet test --project tests/Zeltlotse.E2E.Tests`, failed: 0, inklusive Doku-Screenshots).
- `npm run build` in `doku/` – Exit 0; nur die drei bekannten Konfigurations-Warnungen (i18n, 404, sitemap).
- Die 38 neu geschriebenen Screenshots enthielten nur Rendering-Rauschen (die Story ändert keine Oberfläche) und wurden zurückgesetzt.
- AD-30: Kein Endpunkt angelegt oder geändert, daher keine neuen HTTP-Tests; die bestehenden HTTP-Tests gegen PostgreSQL liefen im vollen Lauf grün.

**Restrisiken:** Wortlaut der neuen Seiten ist nicht von einem Menschen gegengelesen. Die CAP-Zuordnung ist von Hand gepflegt und erkennt neue CAPs in `SPEC.md` nicht von selbst.

### Folgedurchlauf 2026-10-22

**Umfang:** Frischer Review des Diffs seit `12210eb0285efb107286fc4093ff5f998a5b70c3` (Doku-Seiten, Sidebar, `DokuRegeln.cs`, `DokuTests.cs`). Alle vier Layer liefen: Blind Hunter, Edge Case Hunter, Verification Gap und Intent Alignment.

**Befunde:** 26 (0 hoch, 0 mittel, 21 niedrig, 5 falsch). Gepatcht: keine (0 hoch, 0 mittel, 0 niedrig). Zurückgestellt: keine. Abgelehnt sind alle 26 mit Begründung im Triage Log. Elf davon wurden aus dem ersten Durchlauf übernommen (carried). Neu abgelehnt wurden:
- seltene Link- und Codeblock-Formen, die in der Doku nicht vorkommen,
- Raw-Strings in den E2E-Quellen (scheitern laut, heute grün),
- verwaiste Bilder,
- das Ende von Rechten (steht nicht in der Quelle `rollen-und-rechte.md`),
- weitere Zeilen in „Häufige Aufgaben“ (die Tabelle folgt NS-1 bis NS-13),
- Werte in „Auf einen Blick“ (sie stehen schon so in bestehenden Seiten).

Falsch waren: „jeden Bildschirm“, „Mitarbeitende der Organisation“ als Rolle, Zusätze über die Intent hinaus, Testreihenfolge, „aktuell“.

**Restrisiko aus dem ersten Durchlauf:** Zur Anker-Berechnung bei Sonderzeichen hat der Edge Case Hunter nur Varianten gemeldet, die heute nicht vorkommen (`~~~`, `#` im Frontmatter, Slug-Kollision „x-1“). Ein Fehler bei den heutigen Überschriften wurde nicht gefunden.

**Folge-Review empfohlen: nein.** Dieser Durchlauf hat nichts gepatcht, die Arbeit ist konvergiert.

**Verifikation:** Code und Doku sind unverändert, deshalb liefen keine neuen Tests. Es gelten die Nachweise des ersten Durchlaufs. Diese Prüfungen waren Lesen und Suchen ohne Testlauf:
- Suche nach Links mit Titel: keiner gefunden.
- `#`-Zeilen im Frontmatter: keine gefunden.
- Raw-Strings in den E2E-Quellen: vorhanden, Regel trotzdem grün (laut Lauf des ersten Durchlaufs).
- Werte 30 Tage und 12 Monate: stehen gleich in den Detailabschnitten.

Der Verification-Gap-Layer konnte die Architekturtests selbst nicht bauen, weil `Zeltlotse.Api.dll` von einem anderen Prozess gesperrt war. Er hat deshalb nur gelesen.
