# Prüfbericht – Zeltlotse

- **DESIGN.md:** `_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/DESIGN.md`
- **EXPERIENCE.md:** `_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md`
- **Erstellt:** 2026-10-11T00:08

## Gesamturteil

Das Spine-Paar ist sauber gebaut: Abschnittsreihenfolge stimmt, alle 13 Nutzungsszenarien tragen die PRD-Namen wörtlich, alle FR (außer dem gestrichenen FR-42) sind einer Fläche zugeordnet, und jede `{token}`-Referenz in beiden Dateien löst auf. Zwei Stellen wären in der Umsetzung direkt falsch gebaut worden: Die phasenabhängige Startseite „gelaufen“ widersprach FR-45 (der Abschluss-Bildschirm wäre nie erreichbar gewesen), und im Dunkelmodus fehlten dunkle Gegenstücke für die Akzent-Stufen, sodass z. B. der Avatar unlesbar geworden wäre. Daneben gab es Lücken bei der Offline-Menge, beim Weg zur Schnellsuche im Notfall, bei der Konto-Navigation und bei mehreren eigenen Bausteinen (Wochenraster, Kanal-Nachrichten), die in der ELKW-Bibliothek nicht vorkommen. **Befunde:** kritisch 2 · hoch 6 · mittel 12 · niedrig 9 (die Kopfzeile von `review-rubric.md` nennt mittel 11 · niedrig 8; gezählt sind hier die tatsächlich aufgeführten Befunde).

**Stand nach dem Review:** Alle 2 kritischen und 6 hohen Befunde sind in den Spines behoben – auf Grundlage der Entscheidungen im Memlog (`.memlog.md`, Einträge rund um das Ereignis „Rubric-Review“). Kurz: eigene Phase *Nachbereitung* ab dem Tag nach dem Enddatum, erst „Freizeit abschließen“ setzt „gelaufen“; vollständige `*-dark`-Akzentstufen (Avatar 9,75:1) plus aufgehelltes Statusrot und sichtbare Kartenkanten im Dunkeln; Suchfeld „Betroffene Person suchen“ direkt auf der Sicherheitskette; Wiederanmeldung als Variante von NS-1 über den Anmeldelink mit Login; Wochenraster und Kanal-Nachrichten in beiden Component-Tabellen; abschließende, verschlüsselte Offline-Liste; Sicherheitskette in den Einstellungen der Freizeit mit Warnhinweis und Rückfall (Leitung + Notruf 112); eigene Seite „Konto“ und Eltern-Navigation über die Familienübersicht. Die Stichprobe zeigt, dass auch viele mittlere und niedrige Befunde bereits eingearbeitet sind (u. a. `spacing.touch-large`, Brainstorming in `sources`, alle [ASSUMPTION]-Marken entfernt, Key-Screens in `mockups/`, Status `final`); deren Einzelstand ist hier nicht je Befund nachgeprüft. Offen bleibt laut Memlog nur die Prüfung des Dunkelmodus auf einem echten Handy (kein Blocker).

## Urteile je Kategorie

- Flow-Abdeckung – adequate
- Token-Vollständigkeit – thin
- Komponenten-Abdeckung – thin
- Zustands-Abdeckung – thin
- Abdeckung visueller Referenzen – thin
- Ballast und Überspezifikation – strong
- Vererbungsdisziplin – adequate
- Formpassung – strong

## Befunde nach Schwere

### Kritisch (2)

**[Zustands-Abdeckung]** — Phasenlogik widerspricht FR-45 – Abschluss-Startseite nie erreichbar (§ EXPERIENCE.md → IA „Startseite der Freizeit wechselt mit der Phase“; State Patterns „Abgeschlossen“; NS-12)
Die Abschluss-Startseite mit „Freizeit abschließen“ hing am Status „gelaufen“, der laut FR-45 erst durch genau diesen Knopf gesetzt wird. Nach dem letzten Freizeittag wäre zudem die „läuft“-Startseite mit Notfall-Box stehen geblieben.
Fix: eigene Phase „nach Ende, noch nicht abgeschlossen“ mit der Abschluss-Startseite; Notfall nur zwischen Start- und Enddatum; „gelaufen“ = nur lesbar.
Stand: **behoben.** Memlog „Phase Nachbereitung“: ab dem Tag nach dem Enddatum verschwindet der Notfall, Startseite mit Schäden, Belegen, offenen Punkten und „Freizeit abschließen“; erst danach „gelaufen“. Abschluss trotz offener Punkte mit Inline-Warnung.

**[Token-Vollständigkeit]** — Dunkelmodus unvollständig – Avatar im Dunkeln unlesbar (§ DESIGN.md → Frontmatter colors; Components „Avatar“; Colors → Dunkelmodus)
Für `akzent-80/60/40/20/10`, `contrast`/`on-contrast`, `neutral-40/60` und `line-on-light` fehlte ein `*-dark`-Gegenstück; `avatar.background = {colors.akzent-20}` mit `text-dark` darauf wäre praktisch unlesbar. Die Do's-and-Don'ts-Regel verbot zugleich eigene Dunkel-Farben.
Fix: für jedes genutzte helle Token ein `*-dark` festlegen (mind. `akzent-20-dark`) oder „bleibt gleich“ vermerken; Avatar-Kontrast dunkel ergänzen.
Stand: **behoben.** Memlog „Dunkelmodus-Fix“ (nach Prüfung der gerenderten Key-Screens: Statusrot 2,30:1, roter Balken 1,88:1, Karte/Grund 1,14:1, Rand 2,65:1): aufgehelltes Statusrot ≥ 4,5:1, Karten heller mit Kante ≥ 3:1, roter Balken ≥ 3:1. DESIGN.md führt `akzent-10-dark` bis `akzent-80-dark`; Avatar 9,75:1. Offen nur: Prüfung auf echtem Handy.

### Hoch (6)

**[Flow-Abdeckung]** — Kein Weg zur Schnellsuche am Höhepunkt des Notfalls (Handy) (§ EXPERIENCE.md → Key Flows NS-6; Component Patterns „App-Leiste“, „Schnellsuche“)
NS-6 Schritt 3 verlangt die Schnellsuche auf der Sicherheitskette; dort gab es sie nicht, und die App-Leiste hatte die Suche nur am Laptop (DESIGN.md ohne Einschränkung).
Fix: Schnellsuche fest oben auf der Sicherheitskette; App-Leiste in beiden Dateien einheitlich festlegen.
Stand: **behoben.** Memlog: Suchfeld „Betroffene Person suchen“ direkt auf der Sicherheitskette, keine Lupe in der Kopfleiste. Beide Spines angeglichen; App-Leiste am Handy ausdrücklich ohne Suche.

**[Flow-Abdeckung]** — Wiederanmeldung im nächsten Jahr ohne Flow und Einstieg (§ EXPERIENCE.md → Flächen „Familienübersicht“, Key Flows NS-1)
NS-1-Erfolgskriterium (unter 3 Minuten) und NFR-17 hatten keinen Flow; offen war, ob eine angemeldete Mutter über den Anmeldelink eingeloggt und vorausgefüllt landet.
Fix: NS-1 um den Abzweig „schon mit Konto“ ergänzen.
Stand: **behoben.** Memlog: Familienübersicht ohne eigenen Anmelden-Knopf, Anmeldung nur über den Anmeldelink. NS-1 hat die Variante „Wiederanmeldung im nächsten Jahr (NFR-17)“: Login-Link, Kind aus dem Familienkonto, nur Teilnahmebedingungen bestätigen.

**[Komponenten-Abdeckung]** — Wochenraster und Kanal-Nachrichten ohne Baustein-Spezifikation (§ EXPERIENCE.md → Flächen „Wochenraster“, „Kanäle“; DESIGN.md → Components)
Beide liefert die ELKW-Bibliothek nicht; keine Zeile in DESIGN.md → Components oder Component Patterns.
Fix: je eine Zeile in beiden Tabellen.
Stand: **behoben.** Beide stehen in DESIGN.md → Components (`week-grid`, `message`) und Component Patterns; Kanal „Alle“ mit Inline-Bestätigung vor dem Senden (Memlog: „An 60 Personen senden? Ja / Nein“), offline ausgegraut.

**[Zustands-Abdeckung]** — Offline-Menge widersprüchlich, lokale Vorfall-Einträge ohne Schutzregel (§ EXPERIENCE.md → State Patterns „Offline“; Responsive & Platform)
Unterschiedliche Listen offline nutzbarer Bereiche; Weg zum Vorfall-Protokoll über den ausgegrauten Freizeitpass; Verschlüsselung lokal wartender Gesundheitsdaten offen.
Fix: abschließende Liste, Weg über die Sicherheitskette, „lokal verschlüsselt, nach Übertragung gelöscht“.
Stand: **behoben.** Memlog: offline nutzbar sind Küchenansicht, Notfallkarte, Sicherheitskette (lesen), Vorfall-Protokoll (schreiben, Warteschlange), alles lokal verschlüsselt. Beide Stellen führen dieselbe abschließende Liste.

**[Zustands-Abdeckung]** — Sicherheitskette ohne Quelle und ohne Leer-Zustand (§ EXPERIENCE.md → IA Navigation, Flächen „Einstellungen der Freizeit“, „Notfall: Sicherheitskette“)
Nicht festgelegt, wo Ablauf und Ansprechperson am Heimatort eingerichtet werden; ohne Eintrag wäre die Notfallseite leer.
Fix: Sicherheitskette in die Einstellungen; Warnhinweis im Leitungs-Cockpit; Rückfall mit Leitung + „Notruf 112“.
Stand: **behoben.** Memlog: Einrichtung in den Einstellungen der Freizeit, fehlt sie → Warnhinweis im Leitungs-Cockpit. NS-6 beschreibt den Rückfall.

**[Zustands-Abdeckung]** — Konto-Bereich und Wechsel von Organisation/Freizeit ohne Ort (§ EXPERIENCE.md → IA „Ebenen“, „Navigation“, Flächen-Tabelle)
„Über Konto“ erreichbare Flächen ohne Navigationspunkt; Eltern ohne Navigation; Wechsel ohne Aufklappmenü nicht beschrieben (NS-2, NS-3).
Fix: eigene Seite „Konto“; Navigation für Eltern festlegen.
Stand: **behoben.** Memlog: Konto und Wechsel am Handy ganz oben in „Mehr“; Eltern ohne Leiste, alles von der Familienübersicht aus, oben „Konto“, Zurück-Link auf jeder Unterseite. Fläche „Konto“ mit Einstieg je Gerät und Rolle.

### Mittel (12)

**[Flow-Abdeckung]** — Vorfall-Notiz „erscheint bei der Leitung“ ohne Fläche und passende Regel (§ EXPERIENCE.md → Key Flows NS-6 Schritt 7; IA Phasen-Tabelle)
Startseite „läuft“ listet keine Vorfall-Notizen; Meldungen verschwinden beim Seitenwechsel.
Fix: „Neue Vorfall-Notizen“ auf der Startseite der Leitung; Fläche mit FR-31; Zustand „neu / gesehen“.

**[Flow-Abdeckung]** — Herkunft der vorausgefüllten Elterndaten ohne Konto unklar (§ EXPERIENCE.md → Key Flows NS-1 Schritt 6)
Dauerhafte Speicherung im Gerät wäre auf geteilten Geräten heikel.
Fix: Vorausfüllung nur bei Login oder in derselben Sitzung („Weiteres Kind anmelden“).

**[Token-Vollständigkeit]** — Zielgröße 44 px ohne Token (§ EXPERIENCE.md → Accessibility Floor; DESIGN.md → spacing)
Fix: `spacing.touch-large: 44px` anlegen und in `notfall-box` und `bottom-nav` referenzieren.

**[Komponenten-Abdeckung]** — Weitere eigene Bedienelemente ohne Spezifikation (§ FR-57, FR-8, FR-58, FR-36, FR-64, FR-50, Kind-Card)
Schieberegler, Datei-Hochladen, Material-Status, QR-Anzeige, Vorher/Nachher-Ansicht, Kind-Card.
Fix: je eine kurze Zeile in beiden Component-Tabellen.

**[Komponenten-Abdeckung]** — Zähler in der Seitennavigation widersprechen „keine Badges“ (§ DESIGN.md → Components „Seitennavigation“, Shapes; EXPERIENCE.md → Interaction Primitives)
Fix: Anzahl als reiner Text; abschließend aufzählen, welche Punkte sie zeigen.

**[Zustands-Abdeckung]** — Fehlende Zustände an einzelnen Flächen (§ Login, Anmeldung, Profil übernehmen, Push, Freizeitpass vor Bestätigung FR-24 b, Upload)
Fix: je eine Zeile in State Patterns im Ton der Voice-Tabelle.

**[Zustands-Abdeckung]** — NFR-6 (widerrufbare Einwilligungen) ohne Fläche und Zustand (§ EXPERIENCE.md → Flächen, State Patterns)
Fix: Abschnitt im Freizeitpass bzw. „Meine Daten“ mit Schalter je Einwilligung, Zustand „widerrufen am …“.

**[Zustands-Abdeckung]** — Aufklapp-Regel zu schmal für den Notfall (§ EXPERIENCE.md → Component Patterns „Aufklapp“)
Fix: „nie für Allergien, Medikamente, Notfallkontakte; diese stehen immer offen ganz oben im Freizeitpass“.

**[Abdeckung visueller Referenzen]** — Entschiedene Key-Screen-Mockups fehlen (§ DESIGN.md → Colors → Dunkelmodus; EXPERIENCE.md → NS-1, NS-6, IA „Einstieg nach Rolle“)
Fix: Mockups erstellen, nach `mockups/` legen und inline verlinken.

**[Abdeckung visueller Referenzen]** — Referenzen liegen im flüchtigen Arbeitsordner (§ `.working/elkw/`)
Fix: gültige Ansicht (Farbwelt 09, Variante 2) als feste Datei nach `mockups/` kopieren und verlinken.

**[Vererbungsdisziplin]** — `sources` nennt nur das PRD (§ Frontmatter beider Dateien)
Fix: `brainstorm-intent.md` und `roadmap.md` aufnehmen, Vermerk „bei Widerspruch gilt das PRD“.

**[Vererbungsdisziplin]** — 21 [ASSUMPTION]-Marken stehen noch im Text (§ DESIGN.md 8, EXPERIENCE.md 13)
Fix: Marken entfernen; nur die echte offene Frage (Dunkelmodus) in „Offene Fragen“ lassen.

### Niedrig (9)

**[Flow-Abdeckung]** — NS-12 ohne Fehlerpfad bei offenen Aufgaben oder Belegen (§ EXPERIENCE.md → Key Flows NS-12)
Fix: Inline-Bestätigung nennt offene Punkte, Abschluss trotzdem möglich.

**[Flow-Abdeckung]** — NS-1 lässt PRD-Schritt 8 aus (Vater ins Familienkonto, FR-3) (§ EXPERIENCE.md → Key Flows NS-1)
Fix: als optionalen Schritt nach Schritt 7 ergänzen.

**[Flow-Abdeckung]** — NS-9: Seite verlassen ohne Antwort auf „Alle informieren?“ (§ EXPERIENCE.md → Key Flows NS-9)
Fix: „Seite verlassen = Nein“, Hinweis am Programmpunkt.

**[Token-Vollständigkeit]** — Kleinigkeiten im Token-Satz (§ DESIGN.md → Frontmatter)
`spacing.s` = `spacing.m`; „3mm“ als Bildschirm-Einheit; fehlende lineHeight; fehlende Kontraste.
Fix: 3mm durch px/rem ersetzen, lineHeight und Kontraste ergänzen.

**[Komponenten-Abdeckung]** — Namensabweichungen und fehlende Verhaltenszeilen (§ „Avatar“ vs. „Avatar / Person“; Button, Eingabefeld)
Fix: Namen angleichen; kurze Verhaltenszeilen ergänzen.

**[Zustands-Abdeckung]** — „Deaktiviert 50 % Deckkraft“ neben „kein gesperrter Knopf“ (§ DESIGN.md → Components „Button“; EXPERIENCE.md → Sichtbarkeit nach Rolle)
Fix: deaktiviert nur offline oder bei laufender Übertragung, sonst weglassen.

**[Vererbungsdisziplin]** — Begriffe uneinheitlich: „Kopfleiste“ / „App-Leiste“, „Notfall-Button“ (§ Component Patterns)
Fix: „Kopfleiste“ durch „App-Leiste“ ersetzen; Bezug zu FR-29 nennen.

**[Formpassung]** — Umbruch-Prüfbreite uneinheitlich (375 px vs. 320 px) (§ DESIGN.md → Layout & Spacing; EXPERIENCE.md → Accessibility Floor)
Fix: 320 px als Untergrenze, 375 px als Prüfgerät.

**[Formpassung]** — `status: draft` trotz abgeschlossener Entscheidungen (§ Frontmatter beider Dateien)
Fix: nach Einarbeitung auf `final` setzen.

## Mechanische Hinweise

- Alle Token-Verweise lösen auf; keine Mermaid-Diagramme.
- NS-Überschriften 1–13 wörtlich wie im PRD; FR-1 bis FR-69 abgedeckt (FR-42 gestrichen).
- Zum Review-Zeitpunkt nicht abgedeckt: NFR-6, NFR-15 (nur indirekt), NFR-17.
- Nach dem Review (Stichprobe): [ASSUMPTION]-Marken 0, `sources` mit Brainstorming, `status: final`, `spacing.touch-large` vorhanden, Key-Screens und Navigation in `mockups/`.

## Prüfer-Dateien

- `review-rubric.md`
