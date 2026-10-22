---
title: 'Story 7-2: Abnahmebericht für die MVP'
type: 'chore'
created: '2026-10-22'
baseline_revision: '5db66e4ab05ac25d237bee25a443b9c23b1e46f6'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Alle 43 Stories sind `done`, aber es gibt keine Stelle, an der Marvin je Capability sieht, ob ihr Erfolgskriterium aus `SPEC.md` erreicht ist und womit das belegt ist. Ohne diesen Bericht ist die Abnahme der MVP Handarbeit über 43 Story-Specs.

**Approach:** Ein Bericht `_bmad-output/specs/spec-zeltlotse/abnahmebericht.md` nennt je CAP-1 bis CAP-40 das Erfolgskriterium wörtlich, die Belege (Test, Screenshot, Doku-Seite) und ein Urteil `erfüllt`, `teilweise` oder `offen`, dazu die Stories mit Status `blocked` samt Grund. Testgetrieben: Ein neuer Architekturtest prüft zuerst (rot), dass der Bericht vollständig ist, die Kriterien wörtlich zu `SPEC.md` passen und jeder genannte Beleg wirklich existiert; danach entsteht der Bericht (grün).

## Boundaries & Constraints

**Always:** Bericht auf Deutsch, Klartext für Marvin, Begriffe laut `glossar.md`. Urteile ehrlich nach den Regeln in Design Notes; im Zweifel `teilweise` mit Begründung statt `erfüllt`. Jeder Test-Beleg ist ein tatsächlich vorhandener Test, den der Implementierer gelesen hat und der den genannten Teil des Kriteriums prüft. Die Story-Stände (`status:`) werden aus den Dateien in `stories/` gelesen, nicht angenommen. Erst Regeln und Tests (rot), dann Bericht (grün).

**Never:** Keine Änderung an App-, API-, Datenbank- oder Doku-Code; keine neuen Fach-Tests, Screenshots oder Demo-Daten, um Lücken zu schließen (Lücken werden als `teilweise`/`offen` berichtet); keine Node-Abhängigkeit in den Architekturtests; kein Parser für Freitext-Begründungen; keine erfundenen Belege oder Testnamen.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vollständig | Bericht mit Abschnitt je CAP-1…40, Kriterium wörtlich, ein gültiges Urteil, ≥1 Test-Beleg, alle Belege vorhanden, Blocked-Abschnitt passend | keine Verstöße | – |
| CAP fehlt | kein Abschnitt `## CAP-7 …` | „CAP-7 fehlt im Abnahmebericht.“ | Test rot |
| Kriterium abweichend | `**Erfolgskriterium:**` ≠ success-Text aus `SPEC.md` | „CAP-n: Erfolgskriterium weicht von SPEC.md ab.“ | Test rot |
| Urteil fehlt/ungültig | kein oder anderes Wort als erfüllt/teilweise/offen | „CAP-n: Urteil fehlt oder ist ungültig.“ | Test rot |
| Kein Test-Beleg | Abschnitt ohne `- Test:`-Zeile, Urteil nicht `offen` | „CAP-n: kein Test-Beleg.“ | Test rot |
| Teilweise/offen ohne Grund | Urteil teilweise/offen, keine `**Begründung:**` | „CAP-n: Urteil … ohne Begründung.“ | Test rot |
| Datei fehlt | Beleg `` `pfad/x.md` `` existiert nicht | „CAP-n: Beleg pfad/x.md fehlt.“ | Test rot |
| Testmethode fehlt | `` `datei.cs::Methode` ``, Datei ohne `Methode(` | „CAP-n: Test Methode nicht in datei.cs.“ | Test rot |
| Blocked-Liste falsch | Story mit `status: blocked` fehlt im Abschnitt oder eine genannte ist nicht blocked; ohne blocked-Story fehlt „Keine.“ | „Story x-y: Status blocked nicht berichtet.“ bzw. „Story x-y ist nicht blocked.“ | Test rot |

</intent-contract>

## Code Map

- `_bmad-output/specs/spec-zeltlotse/SPEC.md:30-167` -- Quelle der 40 Erfolgskriterien: Zeile `- **CAP-n**`, darunter `  - **success:** <Text>` (eine Zeile je CAP). Nur lesen.
- `_bmad-output/specs/spec-zeltlotse/stories/*.md` -- 43 Story-Specs, alle `status: 'done'` (Stand Planung, keine `blocked`). Code Map, Design Notes, `deferred:` und Auto Run Result jeder Story sind die Fundstellen für Belege und Einschränkungen (z. B. 2-2 doppelte Gast-Anmeldung, 3-4/3-8 Demo-Lücken, 1-7 Auskunft ohne Einladungen). Nur lesen.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:87` -- `CapabilitySeiten`: geprüfte Zuordnung CAP-1…39 → Doku-Seiten mit Handy-/Laptop-Bildpaar; Startpunkt für Doku- und Screenshot-Belege. Vorbild für Regeln als reine Funktionen, die Verstöße als Sätze liefern (`CapabilityVerstoesse`, `LinkVerstoesse`).
- `tests/Zeltlotse.ArchitectureTests/DokuTests.cs` -- Vorbild für Repo-Test plus `Negativ_…`-Tests mit kleinen Eingaben; `Loesung.Wurzel` (`Loesung.cs`) für Pfade.
- `tests/Zeltlotse.*.Tests/` -- Modul-Tests (HTTP gegen PostgreSQL, Domäne), `tests/Zeltlotse.Ablaeufe.Tests` (Durchläufe über Module), `tests/Zeltlotse.Ui.Tests` (bUnit), `tests/Zeltlotse.E2E.Tests` (Playwright; `DokuScreenshotTests.cs` erzeugt `doku/src/assets/screenshots/<name>-handy|laptop.png`).
- `doku/src/content/docs/**` -- Doku-Seiten; `daten.md` für CAP-40.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.ArchitectureTests/AbnahmeTests.cs` -- zuerst: `Abnahmebericht_belegt_jede_Capability_mit_Kriterium_Urteil_und_vorhandenen_Belegen` (echter Bericht gegen `SPEC.md`, `stories/` und Repo) und je Zeile der I/O-Matrix ein `Negativ_…`-Test mit kleinem Bericht-Text -- Test vor dem Code (rot, Bericht fehlt).
- `tests/Zeltlotse.ArchitectureTests/AbnahmeRegeln.cs` -- reine Funktionen: `Erfolgskriterien(specInhalt)` (CAP-Nummer → success-Text), `BerichtVerstoesse(bericht, kriterien, existiert, enthaelt, blockedStories)` nach dem Format in Design Notes; Dateizugriff nur über übergebene Funktionen, damit Negativ-Tests ohne Repo laufen.
- `_bmad-output/specs/spec-zeltlotse/abnahmebericht.md` -- Bericht im Format unten: Kopf (Stand mit vollem Commit-Hash der Basis, Datum, Urteilsregeln in zwei Sätzen), Übersichtstabelle CAP → Urteil, 40 CAP-Abschnitte, Abschnitt „Stories mit Status blocked“, Abschnitt „Offene Punkte aus den Stories“ (die `deferred`-Einträge, die ein Urteil einschränken oder vor dem Start als Dienst eine Entscheidung brauchen, je ein Satz mit Story-Id). Belege durch Lesen der Story-Specs und Tests sammeln; je Teil des Kriteriums den passenden Test.

**Acceptance Criteria:**
- Given der fertige Bericht, when `dotnet test --project tests/Zeltlotse.ArchitectureTests` läuft, then sind alle Tests grün, auch die neuen Abnahme-Regeln.
- Given der Bericht, when Marvin ihn liest, then findet er je CAP-1 bis CAP-40 Kriterium, Urteil und Belege und für jedes `teilweise`/`offen` in einem Satz, was fehlt.
- Given eine später umbenannte oder gelöschte Testmethode, die der Bericht nennt, when die Architekturtests laufen, then werden sie rot und nennen CAP und Methode.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 40 findings — high 0, medium 4, low 31, false 5, maybe-false 0
- findings:
  - `[low]` `[patch]` Blind: Bericht nennt 7-2 `in-progress`, die Frontmatter sagt etwas anderes — Satz nennt keinen wechselnden Status mehr; 7-2 ist der Bericht selbst.
  - `[low]` `[reject]` Blind: Übersichtstabelle und Summe werden nicht gegen die Abschnitte geprüft — stimmen heute überein (25/15 geprüft); ein Tabellen-Parser wäre zusätzliche Logik für ein einmaliges Dokument.
  - `[medium]` `[patch]` Blind: Methodenprüfung ist reine Teilzeichenfolge (Aufruf, Kommentar, längerer Name zählen) — Regel verlangt jetzt die Deklaration `void Name(` bzw. `Task Name(`; Negativ-Tests für Aufruf, Kommentar und Namensende.
  - `[low]` `[patch]` Blind: `- Test:` ohne `pfad::Methode` zählt als Test-Beleg — nur Zeilen mit Backtick-Wert mit `::` zählen; Negativ-Test.
  - `[low]` `[patch]` Blind: doppelter CAP-Abschnitt wird still verworfen — Meldung „CAP-n steht mehrfach im Abnahmebericht.“; Negativ-Test.
  - `[low]` `[reject]` Blind: Zweig „leerer Methodenname“ ohne Test — schützt nur gegen `pfad::` und scheitert laut; ein eigener Test bringt keinen Nutzen.
  - `[low]` `[patch]` Blind: `Existiert` akzeptiert Ordner und absolute Pfade — nur noch relative, vorhandene Dateien.
  - `[low]` `[reject]` Blind: Story-Status-Lesen fällt still aus (kein Präfix, `status:` im Text) — alle 44 Dateien haben Präfix und Frontmatter-Status; Schutz wäre zusätzliche Prüfung für einen nicht vorkommenden Fall.
  - `[medium]` `[patch]` Blind: 4-2 (Mahlzeiten nach Datumsverschiebung) schränkt CAP-19 ein, Urteil blieb erfüllt; 4-1 nicht an CAP-27 gebunden — CAP-19 auf teilweise mit Begründung, Übersicht und Summe (25/15) angepasst, 4-2 und 4-1 markiert.
  - `[low]` `[patch]` Blind: Zugriffsbegrenzung je IP senkt nur CAP-38 ohne Begründung — Satz in „Offene Punkte“ erklärt, warum nur CAP-38.
  - `[low]` `[patch]` Blind: CAP-32 stützt sich auf Architekturtests, die die Urteilsregel nicht nennt — „Architekturtest“ in die Urteilsregeln aufgenommen.
  - `[low]` `[reject]` Blind: Regel „erfüllt braucht Screenshot und Doku“ nicht maschinell geprüft — `DokuRegeln` prüft Seiten und Bildpaare je CAP bereits; eine zweite Regel wäre eine doppelte Quelle.
  - `[low]` `[patch]` Blind: Stand-Zeile und CAP-40-Build beziehen sich auf den Basis-Commit ohne Bericht — Text nennt jetzt den Arbeitsstand von 7-2 auf der Basis.
  - `[low]` `[reject]` Blind: erledigte `deferred`-Einträge in anderen Stories bleiben stehen — `deferred` ist laut Vorlage nur anfügbar; der Bericht vermerkt die Erledigung.
  - `[low]` `[reject]` Blind: Blocked-Zeile prüft das Format `x-y` nicht — ein Tippfehler führt laut zu „ist nicht blocked“ (rot); eine Formatprüfung wäre ein Zusatzzweig.
  - `[low]` `[reject]` Verification-Gap: übersprungener Test (`Skip`, fehlendes `[Fact]`) bleibt „gefunden“ — Umbenennen und Löschen aus AC3 sind über die Deklarationsprüfung abgedeckt; eine Attribut-Analyse wäre zusätzliche Logik, `Skip` kommt im Repo nicht vor.
  - `[low]` `[patch]` Verification-Gap (Other): doppelter CAP-Abschnitt — gleiche Ursache wie die Blind-Zeile, dort behoben.
  - `[low]` `[reject]` Verification-Gap (Other): Tabelle und Summe ungeprüft — wie die Blind-Zeile oben.
  - `[low]` `[patch]` Verification-Gap (Other): `Existiert` akzeptiert Ordner — wie die Blind-Zeile, dort behoben.
  - `[low]` `[patch]` Verification-Gap (Other): veralteter Status im Bericht — wie die Blind-Zeile, dort behoben.
  - `[medium]` `[patch]` Edge: Methode gelöscht, `Name(` bleibt als Aufruf, Kommentar oder Suffix — wie die Blind-Zeile, dort behoben.
  - `[low]` `[patch]` Edge: `- Test:` ohne prüfbaren Test — wie die Blind-Zeile, dort behoben.
  - `[low]` `[patch]` Edge: zwei Abschnitte gleicher CAP — wie die Blind-Zeile, dort behoben.
  - `[low]` `[reject]` Edge: Backticks mit Nicht-Pfad in einer Beleg-Beschreibung — scheitert laut (rot), heute nicht vorhanden.
  - `[low]` `[reject]` Edge: Groß- und Kleinschreibung unter Windows gegenüber Linux-CI — es gibt keine CI, die Pfade stammen aus dem Repo.
  - `[low]` `[reject]` Edge: `status:` nur im Text oder mit Kommentar — kommt nicht vor; Zusatzlogik.
  - `[low]` `[reject]` Edge: Status `Blocked` groß geschrieben — die Vorlage schreibt klein; Zusatzzweig ohne Fall.
  - `[low]` `[reject]` Edge: Story-Datei ohne Slug oder im Unterordner — der Workflow erzeugt immer `id-slug.md` im Ordner.
  - `[low]` `[reject]` Edge: blocked-Stories vorhanden und zusätzlich „Keine.“ — widersprüchlicher Text ohne Wirkung auf die geprüften Kennungen; Zusatzzweig.
  - `[low]` `[reject]` Edge: mehrzeilige Begründung — scheitert laut (rot); das Format ist einzeilig festgelegt.
  - `[low]` `[reject]` Edge: umbrochene success-Zeile in SPEC.md — alle 40 sind einzeilig, der Test prüft genau CAP-1 bis CAP-40.
  - `[low]` `[reject]` Edge: `##` in einem Code-Block des Berichts — der Bericht hat keine Code-Blöcke.
  - `[low]` `[reject]` Edge: Tabelle und Summe — wie die Blind-Zeile oben.
  - `[low]` `[reject]` Edge: erfüllt ohne Screenshot oder Doku — wie die Blind-Zeile oben.
  - `[medium]` `[patch]` Edge (Claim): AC3 nur per Teilzeichenfolge geprüft — wie die Blind-Zeile, dort behoben.
  - `[false]` `[reject]` Edge (Claim): Begründungen länger als ein Satz — der erste Satz nennt, was fehlt; Zusätze schaden nicht.
  - `[false]` `[reject]` Intent: AD-30-Abdeckung je Endpunkt fehlt im Bericht — der Aufrufer-Hinweis ist der allgemeine Bauhinweis; die Story legt keinen Endpunkt an, der Bericht misst die Erfolgskriterien der SPEC.
  - `[false]` `[reject]` Intent: Prüfung führt die Tests nicht aus — der volle Testlauf führt jeden zitierten Test aus (alle grün); die Abdeckung des Kriteriums wurde je Test gelesen.
  - `[false]` `[reject]` Intent: Status im Bericht veraltet — Doppel der Blind-Zeile, dort behoben.
  - `[false]` `[reject]` Intent: Reihenfolge „Test zuerst“ im Diff nicht sichtbar — Prozesshinweis ohne Fehlwirkung; rot vor grün ist im Umsetzungsbericht belegt.

### 2026-10-22 — Review pass (Folgedurchlauf)
- verdicts: 39 findings — high 0, medium 0, low 28, false 9, maybe-false 2
- findings:
  - `[low]` `[reject]` Blind: Übersichtstabelle und Summe werden nicht gegen die Abschnitte geprüft — carried: stimmen überein; ein Tabellen-Parser wäre zusätzliche Logik für ein einmaliges Dokument.
  - `[low]` `[reject]` Blind: Methodenprüfung akzeptiert auskommentierte Deklaration, Helfer ohne `[Fact]` und `Skip` — carried: Umbenennen und Löschen aus AC3 deckt die Deklarationsprüfung ab; Attribut-Analyse wäre Zusatzlogik, `Skip` kommt im Repo nicht vor.
  - `[low]` `[reject]` Blind: nicht lesbare Story-Dateien fallen still heraus, `status:` aus dem Text — carried: alle 44 Dateien haben Präfix und Frontmatter-Status.
  - `[low]` `[reject]` Blind: Satz „Die übrigen 43 Stories sind done“ ist ungeprüft — stimmt heute; eine Zählprüfung aller Stände wäre zusätzliche Logik, die Intent fordert nur die blocked-Liste.
  - `[false]` `[reject]` Blind: CAP-14 „unbesetzte Aufgaben“ auf der falschen Seite belegt — `AufgabenSeitenTests.StartRendern` rendert `FreizeitStart`, also das Cockpit.
  - `[false]` `[reject]` Blind: CAP-17 Löschung der archivierten Notiz nicht belegt — das Archiv sind die Einträge im Modul Sicherheit mit Archiv-Zeitpunkt; `SicherheitLoeschlauf` löscht alle Einträge der abgelaufenen Freizeit per `FreizeitId`, archivierte eingeschlossen; die Frist je Organisation zeigt `LoeschlaufTests`.
  - `[false]` `[reject]` Blind: CAP-15 Einschränkung „bestätigt, soweit freigegeben“ fehlt in der Begründung — Teilnehmende sind bestätigte Anmeldungen, „freigegeben“ ist das Freigabefenster laut Glossar; das ist der Vertrag, keine Lücke.
  - `[false]` `[reject]` Blind: CAP-4 Ablauf nicht aus dem Ausstellungsdatum — der Test rechnet Ausstellung 2025-11-03 plus 12 Monate Richtlinie auf 2026-11-03.
  - `[maybe-false]` `[reject]` Blind: CAP-26 „erscheint nirgends“ nur über die Startseite belegt — es gibt genau drei schaltbare Module (`ModulFeld`), je eins mit UI- und Server-Test; ob ein Modul an weiteren Stellen auftaucht, klärte nur eine Suche über alle Seiten; wäre höchstens low.
  - `[false]` `[reject]` Blind: CAP-34 Beschreibung „neuer Haken, neuer Vermerk“ widerspricht dem Test — der Test endet mit einem neuen Haken durch Miriam (`BeitraegeTests.cs:146-147`).
  - `[maybe-false]` `[reject]` Blind: CAP-8 Wiederanmeldung mit Konto ungetestet — ob `Mit_Konto_meldet_…` das gespeicherte Profil nutzt, klärte nur das Lesen des Tests; das Urteil ist schon teilweise, wäre höchstens low.
  - `[low]` `[reject]` Blind: Bericht nennt keine Testergebnisse — Zahlen hängen vom Lauf ab und stehen im Auto Run Result der Story; im Bericht wären sie sofort veraltet.
  - `[low]` `[reject]` Blind: „Offene Punkte“ werden nicht gegen Urteile und `deferred` geprüft — Freitext; die Story verbietet einen Parser für Freitext.
  - `[low]` `[reject]` Blind: erledigte `deferred`-Einträge anderer Stories bleiben stehen — carried: `deferred` ist nur anfügbar; der Bericht vermerkt die Erledigung.
  - `[low]` `[reject]` Blind: Backticks in der Begründung (z. B. CAP-13) ungeprüft — gewollt: geprüft wird laut Design Notes nur `**Belege:**`; eine zweite Prüfstelle wäre Zusatzlogik.
  - `[low]` `[reject]` Blind: „erfüllt braucht Screenshot und Doku“ nicht maschinell geprüft — carried: `DokuRegeln` prüft Seiten und Bildpaare je CAP.
  - `[low]` `[patch]` Blind: „AD-25“ ohne Erklärung, Zugriffsbegrenzung mehrfach — AD-25 im 6-4-Eintrag als Architekturregel „Web-Sicherheit“ erklärt; die Einträge bleiben je Story getrennt, weil der Abschnitt je Story-Id einen Satz verlangt.
  - `[low]` `[reject]` Blind: `Enthaelt` liest Dateien mehrfach — der ganze Architekturtest-Lauf dauert 17 s; ein Cache wäre Zusatzlogik ohne spürbaren Nutzen.
  - `[low]` `[reject]` Blind: `Existiert` erlaubt `../`-Pfade außerhalb des Repos — die Datei müsste trotzdem existieren und der Bericht liegt im Repo; der Schutz wäre ein zusätzlicher Wächter für einen nicht vorkommenden Fall.
  - `[low]` `[reject]` Edge: auskommentierte Deklaration oder Helfer zählt als Test — carried, wie die Blind-Zeile oben.
  - `[low]` `[reject]` Edge: `**Urteil:**` oder `**Erfolgskriterium:**` doppelt im Abschnitt — kommt nicht vor; Zusatzzweig.
  - `[low]` `[reject]` Edge: zweiter `**Belege:**`-Block oder eingerückte Punkte ungeprüft — das Format ist in den Design Notes fest einzeilig; Zusatzlogik für einen nicht vorkommenden Fall.
  - `[low]` `[reject]` Edge: Tabelle und Summe — carried, wie die Blind-Zeile oben.
  - `[low]` `[reject]` Edge: Dateiname ohne Präfix oder `Blocked` groß — carried.
  - `[low]` `[reject]` Edge: `status:` aus dem Text statt der Frontmatter — carried.
  - `[low]` `[patch]` Edge: blocked-Zeile ohne Grund zählt als berichtet — `BlockedZeile` verlangt jetzt `– Grund`; Negativ-Test `Negativ_blocked_Story_ohne_Grund_gilt_als_nicht_berichtet` (erst rot, dann grün).
  - `[low]` `[reject]` Edge: blocked-Stories vorhanden und zusätzlich „Keine.“ — carried.
  - `[low]` `[reject]` Edge: `../`-Pfad außerhalb des Repos — wie die Blind-Zeile oben.
  - `[low]` `[reject]` Edge: CAP-Nummer jenseits von `int` — scheitert laut mit Ausnahme (rot); kommt nicht vor.
  - `[low]` `[reject]` Edge: umbrochene success-Zeile in SPEC.md — carried.
  - `[low]` `[patch]` Edge (Claim): fehlt die ganze Testdatei, nennt die Meldung die Methode nicht (AC3) — Meldung nennt jetzt den ganzen Beleg `pfad::Methode`; `Negativ_fehlende_Beleg_Datei_wird_erkannt` erwartet das (erst rot, dann grün).
  - `[false]` `[reject]` Edge (Claim): Begründungen länger als ein Satz — carried.
  - `[low]` `[reject]` Intent: Methodenprüfung nur auf Existenz, nicht auf Test-Rolle — carried, wie die Blind-Zeile oben.
  - `[low]` `[reject]` Intent: erfüllt-Regel (Screenshot, Doku, alle Teile) nicht maschinell — carried.
  - `[false]` `[reject]` Intent: Begründung und offen nur auf Vorhandensein geprüft — genau das verlangt die I/O-Matrix; die Story verbietet einen Freitext-Parser.
  - `[low]` `[patch]` Intent: Urteilsregel des Berichts nennt zusätzlich Architekturtests — carried: im ersten Durchlauf bewusst aufgenommen.
  - `[false]` `[reject]` Intent: Teile von CAP-40 (Doku-Build, aktueller Stand) ruhen auf Text — CAP-40 ist deshalb teilweise und die Begründung sagt es.
  - `[low]` `[reject]` Intent: Prüfung sieht nur `blocked`, „43 done“ ist ungeprüfter Text — wie die Blind-Zeile „43 Stories“ oben.
  - `[false]` `[reject]` Intent: SPEC-Seite auf 40 CAPs festgelegt — beschreibend, laut Design Notes gewollt.

## Design Notes

**Format je Capability** (fest, damit die Regel ohne Speziallogik prüfen kann):

```markdown
## CAP-14 – Leitungs-Cockpit

**Erfolgskriterium:** <success-Text aus SPEC.md, wörtlich>

**Urteil:** erfüllt

**Belege:**
- Test: `tests/Zeltlotse.Freizeiten.Tests/CockpitTests.cs::Methode` – was er zeigt
- Screenshot: `doku/src/assets/screenshots/cockpit-handy.png`
- Doku: `doku/src/content/docs/leitung/cockpit.md`

**Begründung:** nur bei teilweise/offen Pflicht: was fehlt.
```

Geprüft wird jeder Backtick-Inhalt in `**Belege:**`-Zeilen: Pfad muss existieren; bei `::Methode` muss die Datei `Methode(` enthalten. Blocked-Abschnitt: `## Stories mit Status blocked`, je Story eine Zeile `- \`x-y\` – Grund`, ohne blocked-Story genau „Keine.“.

**Urteilsregeln:** `erfüllt` = jeder Teil des Kriteriums ist durch einen automatischen Test belegt (HTTP gegen PostgreSQL, Ablauf-, bUnit- oder Playwright-Test); Sichtbares zusätzlich durch Screenshot und Doku-Seite. `teilweise` = ein Teil ist nur von Hand prüfbar (z. B. Zeitangaben wie „unter 3 Minuten“, „Sekunden“, Push-Zustellung auf echten Geräten, Druck auf Papier) oder ein `deferred`-Befund schränkt ihn ein. `offen` = für den Kern des Kriteriums gibt es keinen Beleg.

**Kriterium aus SPEC.md gelesen statt abgeschrieben geprüft:** Die Regel liest die success-Zeilen aus `SPEC.md`; ändert sich die SPEC, wird der Bericht rot. Das ist gewollt: der Bericht gilt nur für den aktuellen Vertrag.

**Bericht im Spec-Ordner, Prüfung in den Architekturtests:** Die Story legt den Ort fest; die Prüfung liegt bei den bestehenden Repo-Scans (`DokuTests`), damit sie im normalen `dotnet test` mitläuft.

**AD-30-Pflichtfälle (Hinweis des Aufrufers):** Diese Story legt keinen Endpunkt an und ändert keinen; falsche Rolle 403, fremde Organisation 404 und Token-Fälle haben keinen Gegenstand. Die bestehenden HTTP-Tests laufen im Abschlusslauf mit und sind im Bericht als Belege genannt, wo sie ein Kriterium tragen.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- expected: vor dem Bericht rot nur bei `Abnahmebericht_belegt_…`, danach alle grün.
- `dotnet test` (ganze Lösung, einmal am Ende, Docker nötig) -- expected: alle grün; die im Bericht genannten Tests laufen damit nachweislich.
- `npm run build` (in `doku/`, einmal am Ende) -- expected: Exit-Code 0 (Beleg für CAP-40).

## Auto Run Result

Status: done

**Umgesetzt:** Abnahmebericht `_bmad-output/specs/spec-zeltlotse/abnahmebericht.md` mit Übersicht und je CAP-1 bis CAP-40 dem Erfolgskriterium wörtlich aus `SPEC.md`, Belegen (Testmethode, Screenshot, Doku-Seite) und Urteil: 25 erfüllt, 15 teilweise, 0 offen. Keine Story steht auf `blocked`. „Offene Punkte aus den Stories“ nennt die zurückgestellten Befunde, die ein Urteil einschränken oder vor dem Start als Dienst eine Entscheidung brauchen. Testgetrieben: Ein Architekturtest prüft den Bericht gegen SPEC.md, die Story-Stände und das Repo (Kriterium wörtlich, gültiges Urteil, Begründung bei teilweise/offen, jede Datei vorhanden, jede Testmethode als Deklaration vorhanden).

**Dateien:**
- `_bmad-output/specs/spec-zeltlotse/abnahmebericht.md` – der Bericht.
- `tests/Zeltlotse.ArchitectureTests/AbnahmeRegeln.cs` – Regeln als reine Funktionen.
- `tests/Zeltlotse.ArchitectureTests/AbnahmeTests.cs` – Prüfung des echten Berichts plus Negativ-Tests je Zeile der I/O-Matrix.

**Review:** 40 Befunde. Korrigiert: 2 Ursachen mit Verdikt medium (Methodenprüfung auf Deklaration, CAP-19 auf teilweise) und 7 mit low. Nichts zurückgestellt. Alle übrigen Befunde sind mit Begründung abgelehnt (siehe Triage-Log).

**Folge-Review empfohlen:** ja – zwei medium-Korrekturen. Ungeprüftes Risiko: Ob die 25 erfüllt-Urteile jeden Teil ihres Kriteriums wirklich abdecken, sichert nur das Lesen der zitierten Tests, kein Test.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.ArchitectureTests`: 179/179 grün (vor dem Bericht rot nur mit „Abnahmebericht fehlt“).
- `dotnet test` (ganze Lösung): 3448 Tests, davon 496 rot. Ursache waren Zeitüberschreitungen von Docker und PostgreSQL beim gleichzeitigen Lauf; keine fachliche Prüfung ist gescheitert. Einzeln wiederholt waren alle grün: Organisationen 58/58, Freizeiten 93/93, Personen 157/157, Api 1081/1081, E2E 208/208.
- `npm run build` in `doku/`: Exit-Code 0, 70 Seiten, nur der bekannte Sitemap-Hinweis.
- Die vom E2E-Lauf neu geschriebenen Screenshots enthielten nur Rendering-Rauschen und wurden zurückgesetzt.

**Restrisiken:** Der Bericht wird rot, sobald SPEC.md ein Kriterium ändert oder ein zitierter Test umbenannt wird (gewollt). Übersichtstabelle und Summe sind nicht maschinell gegen die Abschnitte geprüft. Der Komplettlauf ist auf diesem Rechner nur mit Wiederholungen grün.

### Folgedurchlauf 2026-10-22

**Umgesetzt:** Drei kleine Korrekturen aus dem Folge-Review, testgetrieben (Tests zuerst rot, dann grün): Eine Zeile im Abschnitt „Stories mit Status blocked“ zählt nur mit Grund (`` - `x-y` – Grund ``); fehlt eine ganze Testdatei, nennt die Meldung den Beleg samt Methode; „AD-25“ im Bericht ist in Klartext erklärt.

**Dateien:**
- `tests/Zeltlotse.ArchitectureTests/AbnahmeRegeln.cs` – `BlockedZeile` verlangt einen Grund; Meldung „Beleg … fehlt“ nennt den ganzen Beleg.
- `tests/Zeltlotse.ArchitectureTests/AbnahmeTests.cs` – neuer Negativ-Test für blocked ohne Grund, angepasste Erwartung bei fehlender Datei.
- `_bmad-output/specs/spec-zeltlotse/abnahmebericht.md` – AD-25 erklärt.

**Review:** 39 Befunde (high 0, medium 0, low 28, false 9, maybe-false 2). Korrigiert: 3 mit Verdikt low. Nichts zurückgestellt. Abgelehnt: alle übrigen mit Begründung im Triage-Log; die inhaltlichen Einwände zu CAP-4, CAP-14, CAP-15, CAP-17 und CAP-34 sind am Test widerlegt, die zu CAP-8 und CAP-26 sind ungeklärt, aber höchstens low.

**Folge-Review empfohlen:** nein – kein high korrigiert (0 high, 0 medium, 3 low); die Arbeit ist konvergiert.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.ArchitectureTests --filter-class "*AbnahmeTests"`: vor der Korrektur 2 von 26 rot (genau die neuen Erwartungen).
- `dotnet test --project tests/Zeltlotse.ArchitectureTests`: 180/180 grün.
- Kein Komplettlauf der Lösung und kein `npm run build`: geändert sind nur das Architekturtest-Projekt und der Bericht, kein App-, API- oder Doku-Code; das Architekturtest-Projekt lief vollständig.

**Restrisiken:** Unverändert zum ersten Durchlauf: Ob jedes erfüllt-Urteil jeden Teil seines Kriteriums abdeckt, sichert nur das Lesen der zitierten Tests. Die AD-30-Pflichtfälle aus dem Hinweis des Aufrufers haben in dieser Story keinen Gegenstand (kein Endpunkt).
