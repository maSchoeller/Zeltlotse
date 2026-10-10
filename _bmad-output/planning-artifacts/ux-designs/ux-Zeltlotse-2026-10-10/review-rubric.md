# Spine-Paar-Review – Zeltlotse

Prüfer-Linse: Rubric Walker (references/validate.md). Geprüft: `DESIGN.md`, `EXPERIENCE.md` gegen `.memlog.md` (alle [ASSUMPTION]-Punkte gelten laut Memlog als Entscheidung) und PRD `prds/prd-Zeltlotse-2026-10-10/prd.md`. Die Spines wurden nicht verändert.

## Gesamturteil

Das Spine-Paar ist sauber gebaut: Abschnittsreihenfolge stimmt, alle 13 Nutzungsszenarien tragen die PRD-Namen wörtlich, alle FR (außer dem gestrichenen FR-42) sind einer Fläche zugeordnet, und jede `{token}`-Referenz in beiden Dateien löst auf. Zwei Stellen würden aber in der Umsetzung direkt falsch gebaut: Die phasenabhängige Startseite „gelaufen“ widerspricht FR-45 (der Abschluss-Bildschirm wäre nie erreichbar), und im Dunkelmodus fehlen dunkle Gegenstücke für die Akzent-Stufen, sodass z. B. der Avatar unlesbar würde. Daneben gibt es Lücken bei der Offline-Menge, beim Weg zur Schnellsuche im Notfall, bei der Konto-Navigation und bei mehreren eigenen Bausteinen (Wochenraster, Kanal-Nachrichten), die in der ELKW-Bibliothek nicht vorkommen.

**Befunde:** kritisch 2 · hoch 6 · mittel 11 · niedrig 8

---

## 1. Flow-Abdeckung – adequate

Geprüft: alle NS-1 bis NS-13 aus dem PRD gegen EXPERIENCE.md → Key Flows (Hauptperson, nummerierte Schritte, Höhepunkt, Fehlerpfad). Alle 13 vorhanden, Namen wörtlich. Ausführliche Flows (NS-1, 3–10) sind vollständig. Die Kurzformen NS-2, NS-11, NS-12, NS-13 sind laut Memlog bewusst kurz.

### Befunde

- **[hoch]** NS-6 Schritt 3: Jonas tippt „in die Schnellsuche“ – er ist aber gerade auf der Seite „Sicherheitskette“. Die Schnellsuche steht laut Component Patterns nur auf Startseite, Personen und App-Leiste, und die App-Leiste hat die Suche laut Component Patterns nur am Laptop („Suche (Laptop)“). Am Handy gibt es aus der Sicherheitskette also keinen direkten Weg zur Schnellsuche – genau am Höhepunkt des Notfall-Flows (EXPERIENCE.md → Key Flows NS-6, Component Patterns „App-Leiste“ und „Schnellsuche“; DESIGN.md → Components „App-Leiste“ nennt „Suche“ ohne Geräte-Einschränkung). *Fix:* Schnellsuche fest oben auf der Seite „Sicherheitskette“ einbauen (und in Component Patterns „Schnellsuche → Einsatz“ ergänzen); App-Leiste in beiden Dateien einheitlich festlegen (Suche auf Handy ja/nein).
- **[hoch]** Wiederanmeldung fehlt: Das PRD-Erfolgskriterium von NS-1 („im nächsten Jahr unter 3 Minuten, weil alles vorausgefüllt ist“) und NFR-17 haben keinen Flow und keinen Einstieg. Die Familienübersicht bietet keine Aktion „Für eine Freizeit anmelden“, und es ist offen, ob eine angemeldete Mutter über den Anmeldelink eingeloggt und vorausgefüllt landet (EXPERIENCE.md → Flächen „Familienübersicht“, Key Flows NS-1). *Fix:* NS-1 um einen Fehlerpfad/Abzweig „Sabine ist schon angemeldet (Konto)“ ergänzen: Anmeldelink erkennt Login, Formular ist aus Familienkonto und Freizeitpass vorausgefüllt, Kind wird per Auswahl gewählt; Zweck „Kind für neue Freizeit anmelden“ in der Flächen-Zeile Familienübersicht ergänzen.
- **[mittel]** NS-6 Schritt 7: „Die Vorfall-Notiz erscheint bei der Leitung als Meldung auf deren Startseite“ – die Startseite „läuft“ (IA → Phasen-Tabelle) listet keine Vorfall-Notizen, es gibt keine Fläche „Vorfall-Notizen (Leitung)“, und die Regel für Meldungen („bleibt stehen, bis Ursache behoben oder Seite gewechselt“) passt nicht: nach Seitenwechsel wäre die Notiz weg. *Fix:* Startseite „läuft“ für die Leitung um „Neue Vorfall-Notizen“ (Liste, bleibt bis „gesehen“) ergänzen; Fläche „Vorfall-Notizen“ mit FR-31 in die Flächen-Tabelle aufnehmen; Zustand „Neue Vorfall-Notiz / gesehen“ in State Patterns.
- **[mittel]** NS-1 Schritt 6: „Name und Kontakt der Eltern sind vorausgefüllt“ beim zweiten Aufruf des Links. Hat Sabine in Schritt 4 kein Konto erstellt, ist unklar, woher die Daten kommen (im Browser gespeichert? Auf einem geteilten Gerät heikel für Daten Minderjähriger). *Fix:* Festlegen: Vorausfüllung nur bei eingeloggtem Konto bzw. innerhalb derselben Browsersitzung direkt nach dem Absenden („Weiteres Kind anmelden“-Button auf der Bestätigungsseite), keine dauerhafte Speicherung im Gerät.
- **[niedrig]** Kurz-Flows NS-2, NS-11, NS-12, NS-13 haben keine nummerierten Schritte und keinen markierten Höhepunkt (bewusst so entschieden). NS-12 fehlt ein Fehlerpfad: Was passiert bei „Freizeit abschließen“, wenn noch Nachbereitungs-Aufgaben offen sind oder Belege nicht erstattet sind? *Fix:* einen Satz ergänzen (z. B. Inline-Bestätigung nennt „2 Aufgaben sind noch offen“, Abschluss trotzdem möglich).
- **[niedrig]** NS-1 lässt PRD-Schritt 8 aus (Pauls Vater ins Familienkonto holen, FR-3); in der Flächen-Tabelle ist es vorhanden. *Fix:* als optionalen Schritt nach Schritt 7 ergänzen.
- **[niedrig]** NS-9: Was passiert, wenn Jonas nach dem Speichern der Uhrzeit die Seite verlässt, ohne „Ja“ oder „Nein“ zu tippen? *Fix:* im Fehlerpfad ergänzen: „Seite verlassen = Nein; verschickt wird nichts, Hinweis am Programmpunkt ‚Änderung nicht mitgeteilt – Alle informieren‘ bleibt sichtbar“ (oder bewusst ohne Hinweis).

## 2. Token-Vollständigkeit – thin

Geprüft: alle Frontmatter-Token in DESIGN.md und alle `{pfad.zum.token}`-Verweise in beiden Dateien. Alle Verweise lösen auf (kein toter Verweis). Hell-Werte vollständig mit Hex-Wert. Kontrasttabelle für die wichtigen Paare vorhanden.

### Befunde

- **[kritisch]** Dunkelmodus unvollständig: Für `akzent-80`, `akzent-60`, `akzent-40`, `akzent-20`, `akzent-10`, `contrast`/`on-contrast`, `neutral-40`, `neutral-60` und `line-on-light` gibt es kein `*-dark`-Gegenstück. Mindestens eines davon steckt direkt in einem Baustein: `components.avatar.background = {colors.akzent-20}` (#D6E6EA) – im Dunkelmodus läge darauf `text-dark` #EAF2F4, praktisch unlesbar. Die Do's-and-Don'ts-Regel „keine eigenen Dunkel-Farben neben den `*-dark`-Token“ verbietet der Entwicklung zugleich, selbst etwas zu ergänzen (DESIGN.md → Frontmatter `colors`, Components „Avatar“, Colors → Dunkelmodus). *Fix:* Für jedes in Bausteinen oder Prosa genutzte helle Token ein `*-dark` festlegen (mind. `akzent-20-dark` für Avatar, `status-offen`/`neutral-60` im Dunkeln prüfen) oder ausdrücklich je Token „bleibt gleich“ in die Dunkel-Tabelle schreiben; Kontrast Avatar-Schrift auf Avatar-Fläche (dunkel) ergänzen.
- **[mittel]** Zielgröße 44 px (Notfall-Button, Punkte der unteren Navigation) steht nur als Satz in EXPERIENCE.md → Accessibility Floor; DESIGN.md kennt nur `spacing.control-h` 38 px, und `components.notfall-box.action` verweist auf den normalen `button-primary`. *Fix:* Token `spacing.touch-large: 44px` anlegen und in `components.notfall-box` (Button-Höhe) und `components.bottom-nav` (Mindesthöhe) referenzieren.
- **[niedrig]** Kleinigkeiten im Token-Satz: `spacing.s` und `spacing.m` sind beide 1rem (eines davon überflüssig oder bewusst gleich – dann vermerken); `alert.bar` / `notfall-box.bar` nutzen „3mm“ (Druckeinheit) für eine Bildschirm-Komponente; `small`, `h3`, `kicker`, `table-head` ohne `lineHeight`; Kontrast der Balkenfüllungen `status-warnung`/`status-fehler` gegen `light` bzw. `light-dark` und von `error-field` auf `surface-dark` nicht angegeben. *Fix:* 3mm durch px/rem ersetzen, lineHeight ergänzen, fehlende Kontraste in die Tabellen schreiben (bei Balken genügt der Hinweis, dass Zahl und Wort die Information tragen).

## 3. Komponenten-Abdeckung – thin

Geprüft: jeder in beiden Dateien genannte Baustein hat eine Zeile in DESIGN.md → Components (Aussehen) und EXPERIENCE.md → Component Patterns (Verhalten). Die zusammengesetzten Zeltlotse-Bausteine (Notfall-Box, Balken, Inline-Bestätigung, Offline-Leiste, Speicher-Hinweis) sind in beiden Dateien gut beschrieben. Bibliotheksbausteine (Datentabelle, Formular, Schritte, Reiter, Aufklapp, Terminliste) sind in DESIGN.md pauschal als „unverändert aus der Bibliothek“ abgedeckt – vertretbar.

### Befunde

- **[hoch]** Zentrale Ansichten ohne Baustein-Spezifikation, die die ELKW-Bibliothek nicht liefert: **Wochenraster** (Herzstück nach Leitprinzip 2; Aufbau am Laptop als Raster, am Handy als Tagesliste? feste Mahlzeiten, „Ich übernehme“, Verschieben) und **Kanal-Nachrichten** (Nachrichtenliste, Eingabefeld, Absender, Zeit, gelesen/ungelesen ohne Badge; Ansagen-Zettel, Besprechungspunkte). Beide sind Handy-first bzw. täglich genutzt (EXPERIENCE.md → Flächen „Wochenraster“, „Kanäle“; keine Zeile in DESIGN.md → Components oder Component Patterns). *Fix:* je eine Zeile in beiden Tabellen: Wochenraster (Laptop: Spalten je Tag aus Terminliste/Datentabelle; Handy: Terminliste je Tag mit Segmenten für den Tag) und Nachrichtenliste (Terminliste-ähnlich mit Avatar, Eingabe unten über der unteren Navigation, Verhalten offline).
- **[mittel]** Weitere eigene Bedienelemente ohne Spezifikation: Schieberegler im Beitragsrechner (FR-57; Tastaturbedienung, Schrittweite), Foto-/Datei-Hochladen (Belege FR-58, Nachweise FR-8), Material-Statuswahl mit vier Zuständen (FR-36), QR-Code-Anzeige (FR-64), Vorher/Nachher-Ansicht beim Gericht-Tausch (FR-50), Kind-Card der Familienübersicht. *Fix:* in beiden Component-Tabellen je eine kurze Zeile; Material-Status z. B. als Segmente, Regler mit Zahlenfeld als Tastatur-Alternative.
- **[mittel]** Zähler in der Seitennavigation („Anzahl rechts“, DESIGN.md → Components „Seitennavigation“; EXPERIENCE.md → Component Patterns „Seitennavigation“) widersprechen dem Wortlaut von Interaction Primitives „Keine Zähler-Badges“ und DESIGN.md → Shapes „keine Badges“. *Fix:* klarstellen, dass die Anzahl reiner Text in normaler Schrift ist (kein Kreis, keine Farbe) und abschließend aufzählen, welche Punkte eine Anzahl zeigen.
- **[niedrig]** Namensabweichungen: „Avatar“ (DESIGN.md) vs. „Avatar / Person“ (EXPERIENCE.md); „Button“ und „Eingabefeld“ haben keine Zeile in Component Patterns (Verhalten steckt in „Formular“). *Fix:* Namen angleichen; in Component Patterns kurz „Button: Pfeil-Regel siehe DESIGN.md; deaktiviert nur bei …“ ergänzen.

## 4. Zustands-Abdeckung – thin

Geprüft: jede Fläche der IA-Tabelle gegen die nötigen Zustände (leer, Laden, Fokus, Fehler, offline, keine Berechtigung). Allgemeine Zustände (leer, Laden, Fehler, Fokus, keine Berechtigung, gelaufen) sind gut gelöst.

### Befunde

- **[kritisch]** Phasenlogik widerspricht FR-45: Die Phasen-Tabelle (EXPERIENCE.md → IA → „Startseite der Freizeit wechselt mit der Phase“) zeigt beim Status „gelaufen“ die Abschluss-Startseite mit dem Knopf „Freizeit abschließen“. Laut FR-45 (und NS-12 in derselben Datei) wird „gelaufen“ aber erst **durch** „Freizeit abschließen“ gesetzt, und „gelaufen“ ist nur lesbar (State Patterns „Abgeschlossen“). Damit ist der Abschluss-Bildschirm nie erreichbar; zugleich bliebe nach dem letzten Freizeittag bis zum Abschluss die „läuft“-Startseite mit Notfall-Box und Notfall-Eintrag in der App-Leiste stehen. *Fix:* eine eigene Phase „nach Ende, noch nicht abgeschlossen“ einführen (abgeleitet aus Enddatum, Status bleibt „läuft“ im Sinne von FR-45) mit der Abschluss-Startseite; Notfall-Box/-Eintrag nur zwischen Start- und Enddatum; „gelaufen“ = nur lesbar mit Hinweis. Phasen-Tabelle, Notfall-Eintrag („nur bei läuft“), Flächen „Freizeit-Start“ und NS-12 entsprechend angleichen.
- **[hoch]** Offline-Menge widersprüchlich: State Patterns „Offline“ nennt nur Küchen-Tagesansicht und Notfallkarte als nutzbar, „alle anderen Bereiche ausgegraut, Eingaben gesperrt“. Gleichzeitig sind Notfall-Box („Offline weiter nutzbar“), Sicherheitskette („verweist offline auf den Notfallordner“) und Vorfall-Protokoll („offline beschreibbar“) offline nutzbar. Das Vorfall-Protokoll ist zudem nur über Sicherheitskette oder Freizeitpass erreichbar – der Freizeitpass ist offline ausgegraut. Ungeklärt ist auch, ob lokal wartende Vorfall-Einträge (Gesundheitsdaten Minderjähriger) wie FR-65 verschlüsselt und gelöscht werden. Das ist eine direkte Vorgabe für die Architektur (Offline-Zwischenspeicher). *Fix:* in State Patterns und Responsive & Platform eine abschließende Liste „offline nutzbar“ führen (Küchen-Tagesansicht, Notfallkarte, Notfall-Box, Sicherheitskette, Vorfall-Protokoll – neu anlegen), Weg zum Vorfall-Protokoll offline festlegen (über Sicherheitskette), und „lokal wartende Einträge verschlüsselt, nach Übertragung gelöscht“ ergänzen.
- **[hoch]** Sicherheitskette ohne Quelle und ohne Leer-Zustand: Woher kommen „was jetzt zu tun ist“ und die Ansprechperson am Heimatort? Die Einstellungen der Freizeit (IA → Navigation, Flächen „Einstellungen der Freizeit“) nennen Module, Fragenbaukasten, Schutzkonzept, Teilnehmer-Zugang, Anmeldelinks – keine Notfall-Angaben. Ist nichts hinterlegt, wäre die wichtigste Seite im Notfall leer. *Fix:* „Sicherheitskette (Ansprechperson am Heimatort, Ablauf)“ in die Einstellungen der Freizeit oder Organisation aufnehmen; Zustand „Sicherheitskette unvollständig“ als Warnhinweis im Leitungs-Cockpit; Rückfall auf der Seite: mindestens Leitung + „Notruf 112“ immer zeigen.
- **[hoch]** Konto-Bereich und Wechsel ohne Ort: „Profil und Nachweise“, „Zugriffsprotokoll und Meine Daten“ sind „über Konto“ erreichbar, aber weder Laptop- noch Handy-Navigation nennt „Konto“; Eltern und volljährige Teilnehmende haben gar keine Navigation. Der Wechsel von Organisation/Freizeit „oben in der App-Leiste“ ist ohne Aufklappmenü über dem Inhalt (keine Overlays) nicht beschrieben (EXPERIENCE.md → IA „Ebenen“, „Navigation“, Flächen-Tabelle). Betrifft NS-2 (Zugriffsprotokoll) und NS-3 (Nachweise). *Fix:* Name in der App-Leiste führt zu einer eigenen Seite „Konto“ (Profil und Nachweise, Zugriffsprotokoll, Meine Daten, Abmelden, Wechsel Organisation/Freizeit als Liste); Navigation für Eltern/Volljährige festlegen (z. B. nur App-Leiste, keine untere Navigation).
- **[mittel]** Fehlende Zustände an einzelnen Flächen: Login (Mail mit Link verschickt, Link abgelaufen/schon benutzt); Anmeldung (Anmeldelink ungültig, Freizeit bereits gelaufen); Profil übernehmen (Link abgelaufen); Push-Erlaubnis verweigert (Team, nicht nur iPhone-Teilnehmende); Freizeitpass für das Team **vor** Bestätigung der Anmeldung (FR-24 b: Team sieht ihn erst ab Bestätigung, Leitung schon ab Eingang); Nachweis- oder Beleg-Upload fehlgeschlagen. *Fix:* je eine Zeile in State Patterns mit Satz im Ton der Voice-Tabelle.
- **[mittel]** NFR-6 (optionale Einwilligungen, z. B. Fotos, einzeln erteilt und jederzeit widerrufbar) hat weder Fläche noch Zustand. *Fix:* Einwilligungen als Abschnitt im Freizeitpass bzw. in „Meine Daten“ mit Schalter je Einwilligung und Zustand „widerrufen am …“ ergänzen.
- **[mittel]** Aufklapp-Regel zu schmal: Component Patterns „Aufklapp – nie für Notfallkontakte“ und „Freizeitpass-Gruppen“ im Aufklapp; der NS-6-Höhepunkt verlangt aber Allergien, Medikamente **und** Notfallkontakte ohne Aufklapp ganz oben. *Fix:* Regel erweitern: „nie für Allergien, Medikamente, Notfallkontakte; diese stehen immer offen ganz oben im Freizeitpass“.
- **[niedrig]** „Deaktiviert 50 % Deckkraft“ (DESIGN.md → Components „Button“) neben der Regel „kein gesperrter Knopf“ (EXPERIENCE.md → Sichtbarkeit nach Rolle). *Fix:* einen Satz, wann ein Button deaktiviert erscheint (nur offline / bei laufender Übertragung), sonst wird er weggelassen.

## 5. Abdeckung visueller Referenzen – thin

Geprüft: `mockups/`, `wireframes/`, `imports/`. `imports/` ist leer, `mockups/` und `wireframes/` gibt es nicht. Verlinkt sind zwei Dateien aus `.working/elkw/` (beide vorhanden). „Bei Widerspruch gelten die Spines“ steht in beiden Dateien.

### Befunde

- **[mittel]** Laut Memlog sind finale Key-Screen-Mockups entschieden (Dunkelmodus-Check Startseite + Notfall, Gast-Anmeldung NS-1, Notfall-Ablauf NS-6, Familienübersicht Eltern). Es gibt sie nicht, und die Spines verlinken sie nicht – gerade der Dunkelmodus ist laut „Offene Fragen“ ungesehen. *Fix:* Mockups erstellen, nach `mockups/` legen und inline verlinken (DESIGN.md → Colors → Dunkelmodus; EXPERIENCE.md → Key Flows NS-1, NS-6; IA „Einstieg nach Rolle“).
- **[mittel]** Die einzigen Referenzen liegen im Arbeitsordner `.working/elkw/` und hängen an dort kopierten ELKW-CSS-Dateien; `.working/` gilt als Zwischenablage und kann verschwinden. Die Varianten-Datei enthält zudem verworfene Farbwelten und eine Laptop-Variante 1, und der Mock nutzt eine gesperrte Ansprache („Drei Dinge brauchen eure Aufmerksamkeit“). *Fix:* die gültige Ansicht (Farbwelt 09, Variante 2) als feste Datei nach `mockups/` kopieren und dort verlinken; „Spines gewinnen“ steht schon.

## 6. Ballast und Überspezifikation – strong

Geprüft: Pixelangaben statt Token, Wiederholung von Quellen, Prosa statt Tabelle, unnötige Abschnitte. Die Dateien sind dicht und tabellarisch; die Übernahme der Bibliothekswerte ins Frontmatter ist gewollt (Werte statt Schätzungen). Keine nennenswerten Befunde. Hinweis: Die Dunkel-Tabelle in DESIGN.md → Colors wiederholt die Frontmatter-Kommentare – unschädlich.

## 7. Vererbungsdisziplin – adequate

Geprüft: `sources` lösen auf, NS-Namen wörtlich, Begriffe wie im PRD, Bausteinnamen einheitlich, Token-Verweise in EXPERIENCE.md lösen gegen DESIGN.md auf (alle sechs tun es).

### Befunde

- **[mittel]** `sources` nennt nur das PRD. Laut Memlog sind auch `brainstorm-intent.md` und `roadmap.md` Quellen, und EXPERIENCE.md → Inspiration stützt sich ausdrücklich auf das Brainstorming (ChurchTools). *Fix:* beide Brainstorming-Dateien in `sources` beider Dateien aufnehmen (mit Vermerk „bei Widerspruch gilt das PRD“).
- **[mittel]** 21 Markierungen „[ASSUMPTION]“ (8 in DESIGN.md, 13 in EXPERIENCE.md) stehen noch im Text, obwohl der Memlog sie als Entscheidungen übernommen hat. Nachgelagerte Leser (Architektur, Story-Entwicklung) behandeln sie sonst als offen. *Fix:* Markierungen entfernen; nur die echte offene Frage (visuelle Prüfung Dunkelmodus) in „Offene Fragen“ belassen.
- **[niedrig]** Begriffe uneinheitlich: „Kopfleiste“ (Bausteinname „Notfall-Eintrag (Kopfleiste)“) neben „App-Leiste“ überall sonst; PRD-Begriff „Notfall-Button“ (FR-29) wird auf „Notfall-Box“ + „Notfallhilfe öffnen“ + „Notfall-Eintrag“ abgebildet, ohne dass die Zuordnung einmal genannt wird. *Fix:* „Kopfleiste“ durch „App-Leiste“ ersetzen; in Component Patterns „Notfall-Box“ einen Satz „setzt den Notfall-Button aus FR-29 um“.

## 8. Formpassung – strong

Geprüft: DESIGN.md-Abschnitte in kanonischer Reihenfolge (Brand & Style → Colors → Typography → Layout & Spacing → Elevation & Depth → Shapes → Components → Do's and Don'ts) – vollständig und korrekt. EXPERIENCE.md enthält alle Pflichtabschnitte (Foundation, IA, Voice and Tone, Component Patterns, State Patterns, Interaction Primitives, Accessibility Floor, Key Flows) sowie die ausgelösten Zusatzabschnitte Responsive & Platform und Inspiration & Anti-patterns. „Offene Fragen“ ist gerechtfertigt.

### Befunde

- **[niedrig]** Umbruch-Prüfbreite uneinheitlich: DESIGN.md → Layout & Spacing „kein seitliches Scrollen bei 375 px“, EXPERIENCE.md → Accessibility Floor „bei 320 px Breite kein seitliches Scrollen“ (WCAG-Kriterium 1.4.10). *Fix:* überall 320 px als Untergrenze nennen, 375 px als Prüfgerät.
- **[niedrig]** Frontmatter `status: draft` in beiden Dateien, obwohl Discovery und Entscheidungen abgeschlossen sind. *Fix:* nach Einarbeitung der Befunde auf `final` (oder den im Skill üblichen Wert) setzen.

## Mechanische Hinweise

- Alle `{colors.*}`, `{typography.*}`, `{rounded.*}`, `{spacing.*}`, `{components.*}`-Verweise in beiden Dateien lösen auf; keine Mermaid-Diagramme vorhanden.
- Frontmatter beider Dateien vollständig bis auf `sources` (siehe 7).
- NS-Überschriften 1–13 stimmen wörtlich mit dem PRD überein.
- FR-Abdeckung in der Flächen-Tabelle vollständig (FR-1 bis FR-69, FR-42 gestrichen). Nicht abgedeckt: NFR-6 (Einwilligungen), NFR-15 (arbeitsfähig nach 2 Minuten – nur indirekt), NFR-17 (Wiederanmeldung).
- Namensabweichungen: „Kopfleiste“/„App-Leiste“, „Avatar“/„Avatar / Person“.
- Doppelter Wert: `spacing.s` = `spacing.m` = 1rem.
- Referenzen zeigen in `.working/` statt `mockups/`.
