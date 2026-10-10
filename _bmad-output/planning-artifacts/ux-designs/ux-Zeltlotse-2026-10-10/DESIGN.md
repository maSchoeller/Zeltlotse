---
project: Zeltlotse
name: Zeltlotse
description: Gemeinsame Plattform, auf der kirchliche Jugendverbände ihre Freizeiten planen und durchführen. Visuelle Basis ist die ELKW-Designbibliothek; diese Datei hält die Werte fest und benennt nur die Zeltlotse-eigenen Ergänzungen.
status: final
created: 2026-10-10
updated: 2026-10-11
sources:
  - _bmad-output/planning-artifacts/prds/prd-Zeltlotse-2026-10-10/prd.md
  - _bmad-output/brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/brainstorm-intent.md   # bei Widerspruch gilt das PRD
  - _bmad-output/brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/roadmap.md             # bei Widerspruch gilt das PRD
inherits: >-
  ELKW-Designbibliothek (Skill elkw-design v4.0.0): Stil „nahbar“ (data-stil="nahbar"),
  Farbwelt 09 Blaugrün (data-farbwelt="09"), Dichte „Arbeiten“ (data-dichte="arbeiten").
  Hell-Werte unten sind aus elkw-tokens.css, elkw-nahbar.css und farbwelten.json übernommen, nicht geschätzt.
  Zeltlotse-eigene Ergänzungen: Dunkelmodus (*-dark), Wortmarke, untere Navigation, Notfall-Box,
  Balken mit Statuswort, Inline-Bestätigung, Offline-Leiste, Speicher-Hinweis, Wochenraster, Kanal-Nachrichten.
  Abweichung für WCAG 2.2 AA: korrigierte Werte für Dachzeile auf dem Grundton und Feldrahmen (siehe Colors).
colors:
  # --- Farbwelt 09 Blaugrün (kräftig, Abdunklung 2 %) ---
  primary: '#307F91'            # Navigationsleisten (untere Navigation, Fuß)
  on-primary: '#FFFFFF'
  action: '#307F91'             # Buttons, Links, Feldrahmen, Checkboxen, Dachzeile (nur auf Weiß)
  on-action: '#FFFFFF'
  akzent: '#318294'             # Balkenfüllung, Status „aktiv“, aktive Markierung
  on-akzent: '#FFFFFF'
  akzent-80: '#5A9BA9'
  akzent-60: '#83B4BF'
  akzent-40: '#ADCDD4'
  akzent-20: '#D6E6EA'
  akzent-10: '#EAF2F4'
  contrast: '#83B4BF'           # Band, Kontrast-Button (Schrift schwarz)
  on-contrast: '#000000'
  # --- Rollen im Stil „nahbar“ (aus elkw-nahbar.css aufgelöst) ---
  ground: '#EAF2F4'             # Grundton hinter Inhalten (= akzent-10)
  surface: '#FFFFFF'            # weiße Flächen, Cards, Meldungen, Tabellen
  light: '#D6E6EA'              # Strukturflächen: Werkzeugleiste, Balkenspur, aktive Seitennavigation (= akzent-20)
  neutral-10: '#EAF2F4'         # Hover, Leer-Zustand (= akzent-10)
  border: '#ADCDD4'             # feine Kanten (= akzent-40)
  table-border: '#D6E6EA'       # waagerechte Haarlinien (= akzent-20)
  line-on-light: '#ADCDD4'
  # --- feste Farben der Bibliothek ---
  text: '#000000'
  text-muted: '#474F60'         # Dunkelgrau: Ladetext, Nebeninfos
  neutral-40: '#B5B9BF'
  neutral-60: '#9195A0'
  focus: '#000000'              # Tastaturfokus 2 px, 4 px Abstand
  focus-on-color: '#FFFF00'     # Fokus auf primary-Flächen
  # --- Statusfarben (fest, nur als Quadrat oder Balken, immer mit Wort) ---
  status-fehler: '#B61231'      # 14 Rot
  status-warnung: '#DC9018'     # 12 Orange
  status-erfolg: '#518471'      # 08 Dunkelgrün
  status-hinweis: '#307F91'     # = action
  status-aktiv: '#318294'       # = akzent
  status-offen: '#9195A0'       # = neutral-60
  error-field: '#FF0000'        # nur Rahmen fehlerhafter Felder
  # --- Dunkelmodus: Zeltlotse-Ergänzung, folgt der Geräteeinstellung ---
  # Jedes helle Token hat ein *-dark-Gegenstück. Alle Kontraste: Tabelle in Colors → Dunkelmodus.
  # Flächen und Kanten
  ground-dark: '#0F1719'         # Grundton
  surface-dark: '#1D2A2E'        # Flächen, Cards: eine Stufe heller als der Grund, immer mit Kante border-dark
  light-dark: '#2B3F44'          # Strukturfläche, Balkenspur, aktive Seitennavigation
  neutral-10-dark: '#253539'     # Hover, Leer-Zustand, Inline-Bestätigung
  border-dark: '#668E97'         # Kante an Flächen, Cards, Meldungen, Outline-Buttons
  table-border-dark: '#2B3F44'   # waagerechte Haarlinien
  line-on-light-dark: '#668E97'  # Trenner auf light-dark
  # Akzent-Stufen (im Dunkeln umgekehrt: 10 = dunkelste, 80 = hellste Stufe)
  akzent-10-dark: '#0F1719'      # = ground-dark
  akzent-20-dark: '#2B3F44'      # = light-dark; Avatar-Fläche
  akzent-40-dark: '#668E97'      # = border-dark
  akzent-60-dark: '#83B4BF'
  akzent-80-dark: '#ADCDD4'
  akzent-dark: '#83B4BF'         # Balkenfüllung, Status „aktiv“
  on-akzent-dark: '#0E1A1D'
  contrast-dark: '#83B4BF'       # unverändert
  on-contrast-dark: '#000000'
  # Text und Bedienung
  text-dark: '#EAF2F4'
  text-muted-dark: '#ADCDD4'
  primary-dark: '#255F6C'        # untere Navigation
  on-primary-dark: '#FFFFFF'
  action-dark: '#83B4BF'         # Links, Feldrahmen, Button-Fläche
  on-action-dark: '#0E1A1D'      # Schrift auf Button
  neutral-40-dark: '#5C616B'     # deaktivierte Rahmen
  neutral-60-dark: '#A3A7B1'     # Brotkrumen-Trenner, Status „offen“
  focus-dark: '#FFFF00'
  # Statusfarben im Dunkeln (Rot aufgehellt, damit es als Notfall-Signal trägt)
  status-fehler-dark: '#FF6B81'
  status-warnung-dark: '#DC9018' # unverändert
  status-erfolg-dark: '#7DB39E'
  status-hinweis-dark: '#83B4BF' # = action-dark
  status-aktiv-dark: '#83B4BF'   # = akzent-dark
  status-offen-dark: '#A3A7B1'   # = neutral-60-dark
  error-field-dark: '#FF6B6B'    # Rahmen fehlerhafter Felder
typography:
  # Sarabun (SIL Open Font License), lokal eingebunden; Ersatz Arial, sans-serif. Werte für Dichte „Arbeiten“ + Stil „nahbar“.
  body:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  small:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 13px
    fontWeight: '400'
    lineHeight: '1.5'
  h1:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 24px
    fontWeight: '300'
    lineHeight: 32px
    letterSpacing: -0.01em
  h1-emphasis:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontWeight: '600'
  h2:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  h3:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 18px
    fontWeight: '700'
    lineHeight: '1.5'
  kicker:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 13px
    fontWeight: '600'
    lineHeight: '1.5'
    letterSpacing: 0.08em
  stat-number:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 38.4px
    fontWeight: '300'
    lineHeight: '1.2'
  table-head:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 13px
    fontWeight: '600'
    lineHeight: '1.5'
    letterSpacing: 0.06em
  wordmark:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 36px
  print-body:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 10.5pt
  print-h1:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 26pt
    fontWeight: '700'
  print-h2:
    fontFamily: 'Sarabun, Arial, sans-serif'
    fontSize: 15pt
rounded:
  none: 0px      # Balken, Status-Quadrat, Statusbalken der Meldung
  sm: 6px        # Buttons, Segmente, Avatar, Navigationspunkte
  md: 8px        # Eingabefelder
  lg: 12px       # Flächen, Cards, Meldungen, Kennzahlen, Tabellen, Leer-Zustand
spacing:
  xs: 0.5rem
  s: 1rem        # bewusst gleich wie m (Dichte „Arbeiten“ der Bibliothek)
  m: 1rem
  l: 1.5rem
  xl: 2rem
  section: 1.5rem
  gap: 16px
  gutter: 1rem
  margin-mobile: 1rem
  header-pad: 8px
  box-pad: 1.25rem
  control-h: 38px
  touch-large: 44px   # Notfall-Button, Punkte der unteren Navigation (Bedienung unter Stress, mit einer Hand)
  container: 1310px
  measure: 75ch
components:
  wordmark:
    typography: '{typography.wordmark}'
    color: '{colors.text}'
  app-bar:
    background: '{colors.surface}'
    border-bottom: '1px solid {colors.light}'
    padding: '{spacing.header-pad}'
  side-nav-item:
    background: '{colors.surface}'
    radius: '{rounded.sm}'
  side-nav-item-active:
    background: '{colors.light}'
    color: '{colors.text}'
  bottom-nav:
    background: '{colors.primary}'
    foreground: '{colors.on-primary}'
    typography: '{typography.small}'
    columns: '5'
    min-height: '{spacing.touch-large}'
  bottom-nav-item-active:
    fontWeight: '600'
    underline: '1px solid {colors.on-primary}'
  button-primary:
    background: '{colors.action}'
    foreground: '{colors.on-action}'
    radius: '{rounded.sm}'
    padding: '0.3rem 1rem'
  button-outline:
    background: 'transparent'
    border: '1px solid {colors.border}'
    foreground: '{colors.text}'
    radius: '{rounded.sm}'
  button-text:
    background: 'transparent'
    foreground: '{colors.action}'
  input:
    background: '{colors.surface}'
    border: '1px solid {colors.action}'
    radius: '{rounded.md}'
    height: '{spacing.control-h}'
  input-error:
    border: '1px solid {colors.error-field}'
  surface:
    background: '{colors.surface}'
    radius: '{rounded.lg}'
    padding: '{spacing.box-pad}'
    border-dark: '1px solid {colors.border-dark}'
  alert:
    background: '{colors.surface}'
    radius: '{rounded.lg}'
    bar: '11px {colors.status-hinweis}'   # Bibliothek: 3mm
  notfall-box:
    background: '{colors.surface}'
    radius: '{rounded.lg}'
    bar: '11px {colors.status-fehler}'    # Bibliothek: 3mm
    action: '{components.button-primary}'
    action-height: '{spacing.touch-large}'
  status-square:
    size: '0.6em'
    radius: '{rounded.none}'
  bar-track:
    background: '{colors.light}'
    height: '0.75rem'
    height-large: '1.25rem'
    radius: '{rounded.none}'
  bar-fill:
    background: '{colors.akzent}'
  bar-fill-warnung:
    background: '{colors.status-warnung}'
  bar-fill-fehler:
    background: '{colors.status-fehler}'
  stats:
    background: '{colors.surface}'
    number: '{typography.stat-number}'
    number-color: '{colors.action}'
    radius: '{rounded.lg}'
  segments-active:
    background: '{colors.action}'
    foreground: '{colors.on-action}'
    radius: '{rounded.sm}'
  avatar:
    background: '{colors.akzent-20}'
    foreground: '{colors.text}'
    background-dark: '{colors.akzent-20-dark}'
    foreground-dark: '{colors.text-dark}'
    radius: '{rounded.sm}'
  week-grid:
    background: '{colors.surface}'
    day-head: '{typography.table-head}'
    divider: '1px solid {colors.table-border}'
    radius: '{rounded.lg}'
  message:
    sender: '{components.avatar}'
    time: '{typography.small}'
    time-color: '{colors.text-muted}'
    divider: '1px solid {colors.table-border}'
  empty-state:
    background: '{colors.surface}'
    radius: '{rounded.lg}'
  inline-confirm:
    background: '{colors.neutral-10}'
    radius: '{rounded.lg}'
  offline-bar:
    background: '{colors.light}'
    foreground: '{colors.text}'
    typography: '{typography.small}'
  autosave-hint:
    typography: '{typography.small}'
    color: '{colors.text-muted}'
  focus-ring:
    outline: '2px solid {colors.focus}'
    offset: '4px'
---

## Brand & Style

Zeltlotse ist ein Werkzeug für Ehrenamtliche, die ein Zeltlager oder eine Freizeit planen und dann eine Woche lang mit 60 Menschen draußen sind. Der Leitgedanke aus dem PRD (Produktanforderungsdokument) gilt auch für das Aussehen: **einfach, sorglos, schnell an die Information** – das Gegenteil eines Verwaltungsmolochs. Die Oberfläche soll ruhig wirken, auch wenn draußen gerade viel los ist.

Die visuelle Sprache stammt vollständig aus der **ELKW-Designbibliothek** (Komponentenbibliothek im Corporate Design der Evangelischen Landeskirche in Württemberg) im Stil **„nahbar“**: ein zarter Grundton aus der Farbwelt, weiße Flächen darauf, weiche Ecken, große Überschriften in Sarabun Light mit SemiBold-Betonung, keine Schatten, keine Verläufe, Status immer als Wort. Gewählt ist **Farbwelt 09 Blaugrün** – eine einzige Farbe plus Neutraltöne. Anwendungsansichten laufen in der Dichte **„Arbeiten“** (16-px-Grundschrift, kompakte Abstände).

Zeltlotse ist **kein ELKW-Produkt**. Deshalb gibt es kein ELKW-Logo; an seiner Stelle steht die reine Text-Wortmarke „Zeltlotse“ in Sarabun SemiBold. Die Bibliothek liefert nur den Stil.

Diese Datei übernimmt die Werte der Bibliothek und beschreibt **nur die Ergänzungen**, die Zeltlotse braucht, unter anderem Dunkelmodus, untere Navigation fürs Handy, Notfall-Box, Balken mit Statuswort, Inline-Bestätigung, Offline-Leiste und Speicher-Hinweis (vollständig siehe Components). Alle Ergänzungen sind aus vorhandenen Bausteinen zusammengesetzt; nichts ist neu erfunden.

Referenz-Mocks (klickbare HTML-Entwürfe einzelner Ansichten) liegen in `mockups/`: [richtung-elkw-farbwelten.html](mockups/richtung-elkw-farbwelten.html) (Handy-Startseite während der Freizeit und Leitungs-Cockpit am Laptop, voreingestellt auf Farbwelt 09 und Stil „nahbar“), [navigation-laptop.html](mockups/navigation-laptop.html) (Seitennavigation am Laptop, gewählt ist Variante 2) sowie die vier Schlüsselansichten [key-dunkelmodus.html](mockups/key-dunkelmodus.html), [key-gast-anmeldung.html](mockups/key-gast-anmeldung.html), [key-notfall.html](mockups/key-notfall.html) und [key-familienuebersicht.html](mockups/key-familienuebersicht.html). Wie man sie startet, steht in [mockups/README.md](mockups/README.md).

## Colors

Eine Farbe, viele Abstufungen, dazu Grau und Schwarz. Mehr Ruhe entsteht durch Stufen derselben Farbe, nicht durch einen zweiten Farbton.

- **Blaugrün (`{colors.primary}` / `{colors.action}` #307F91)** trägt die Bedienung: untere Navigation, Buttons, Links, Feldrahmen, Checkboxen. Weiß darauf erreicht 4,59:1 – knapp über dem Grenzwert von WCAG 2.2 AA (Richtlinien für barrierefreie Webinhalte, Stufe AA: mindestens 4,5:1 für normalen Text). Deshalb: Schrift auf Blaugrün nie kleiner als `{typography.small}` und nie dünner als Regular.
- **Akzent (`{colors.akzent}` #318294)** markiert Aktives und füllt Balken. Sparsam einsetzen; er ist keine Dekoration.
- **Abstufungen** `{colors.akzent-80}` bis `{colors.akzent-10}` bilden im Stil „nahbar“ die Struktur: `{colors.ground}` (10 %) ist der Grundton hinter allem, `{colors.light}` (20 %) die Strukturfläche (Werkzeugleiste, Balkenspur, aktiver Navigationspunkt), `{colors.border}` (40 %) die feine Kante.
- **Weiß (`{colors.surface}`)** für alle Inhaltsflächen: Cards, Meldungen, Tabellen, Leer-Zustand.
- **Schwarz (`{colors.text}`)** für Text; **Dunkelgrau (`{colors.text-muted}`)** für Ladetext und Nebeninfos.
- **Statusfarben** sind fest und gelten in jeder Farbwelt gleich (im Dunkelmodus gelten die `*-dark`-Gegenstücke, siehe unten): `{colors.status-fehler}` (Rot), `{colors.status-warnung}` (Orange), `{colors.status-erfolg}` (Dunkelgrün), `{colors.status-aktiv}`, `{colors.status-offen}`. Sie erscheinen **nur** als Quadrat neben einem Wort oder als Balken, nie als Fläche und nie allein. Farbwelt 09 kollidiert mit keiner Statusfarbe. Farbwelt 08 Dunkelgrün kam nicht infrage, weil sie mit der Statusfarbe Erfolg zusammenfällt.
- **Statuszuordnung** nach Bibliotheksregel: neu/eingegangen = offen, aktiv/läuft = aktiv, bestätigt/erledigt = Erfolg, Frist/Rückfrage nötig = Warnung, abgesagt/abgelehnt = Fehler. „Ausgebucht“ ist ein Hinweis, kein Problem, und steht deshalb neutral grau (offen).
- **Zwei Rottöne:** `{colors.status-fehler}` für Status und Meldungen (auch die Notfall-Box), `{colors.error-field}` nur für den Rahmen fehlerhafter Eingabefelder.

**Kontrast – geprüfte Paare (hell):**

| Paar | Verhältnis | Einsatz |
|---|---|---|
| `{colors.text}` auf `{colors.surface}` | 21:1 | Fließtext |
| `{colors.text}` auf `{colors.ground}` | 18,5:1 | Überschriften auf dem Grundton |
| `{colors.on-action}` auf `{colors.action}` | 4,59:1 | Button, untere Navigation |
| `{colors.action}` auf `{colors.surface}` | 4,59:1 | Links, Textbuttons, Dachzeile |
| `{colors.action}` auf `{colors.ground}` | 4,05:1 | **unter AA** – nicht verwendet, siehe Korrektur unten |
| `{colors.text-muted}` auf `{colors.surface}` | 8,22:1 | Nebeninfos |
| `{colors.akzent}` gegen `{colors.light}` | 3,44:1 | Balkenfüllung gegen Spur |
| `{colors.focus-on-color}` auf `{colors.primary}` | 4,28:1 | Fokus in der unteren Navigation |
| `{colors.status-fehler}` auf `{colors.surface}` | 6,73:1 | Statusquadrat, Balken der Notfall-Box |
| `{colors.status-fehler}` gegen `{colors.light}` | 5,25:1 | Balken „Überschritten“ |
| `{colors.status-erfolg}` gegen `{colors.light}` | 3,35:1 | Balken „Im Plan“ (wenn in Erfolg gefärbt) |
| `{colors.status-warnung}` gegen `{colors.light}` | 2,03:1 | Balken „Knapp“ – unter 3:1; zulässig, weil Zahl und Statuswort die Information tragen |
| `{colors.text}` auf `{colors.akzent-20}` | 16,37:1 | Avatar-Initialen |

**Korrigierte Werte (Entscheidung):** Die ELKW-Bibliothek verfehlt im Stil „nahbar“ an zwei Stellen WCAG 2.2 AA. Zeltlotse löst das selbst mit korrigierten Werten:

- **Blaugrüne Schrift nur auf Weiß.** Links und Dachzeile auf dem Grundton erreichen nur 4,05:1. Blaugrüne Schrift (Links, Textbuttons, Dachzeile) steht deshalb nur auf `{colors.surface}`; auf dem Grundton `{colors.ground}` – etwa die Dachzeile im Begrüßungsblock – wird sie in `{colors.text}` gesetzt.
- **Feldrahmen stehen in `{colors.action}`** (4,59:1), wie im Stil „klassisch“. Der „nahbar“-Rahmen `{colors.akzent-40}` (= `{colors.border}`) erreicht auf Weiß nur 1,69:1 und verfehlt die 3:1, die WCAG 2.2 AA für die Umrisse von Bedienelementen verlangt (Nicht-Text-Kontrast). Für Outline-Buttons bleibt `{colors.border}`, weil dort die Beschriftung das Element erkennbar macht.

### Dunkelmodus

Die ELKW-Bibliothek hat keinen Dunkelmodus. Zeltlotse ergänzt ihn selbst; er **folgt der Geräteeinstellung** (`prefers-color-scheme`), einen eigenen Schalter gibt es nicht. Die dunklen Werte sind aus Farbwelt 09 abgeleitet: Grundton und Flächen sind sehr dunkle Blaugrün-Töne, Text ist die 10-%-Stufe, Bedienfarbe die 60-%-Stufe. **Jedes helle Token hat ein `*-dark`-Gegenstück** (im Kopfbereich der Datei, der Frontmatter, Abschnitt `colors`); die Akzent-Stufen laufen im Dunkeln umgekehrt (`{colors.akzent-10-dark}` ist die dunkelste, `{colors.akzent-80-dark}` die hellste Stufe).

Drei Regeln für das Dunkle:

- **Flächen eine Stufe heller als der Grund, immer mit Kante.** `{colors.surface-dark}` hebt sich nur mit 1,23:1 von `{colors.ground-dark}` ab; deshalb tragen Flächen, Cards und Meldungen im Dunkeln immer die 1-px-Kante `{colors.border-dark}` (5,08:1 gegen den Grund). Im hellen Stil „nahbar“ bleibt die Kante wie in der Bibliothek weg.
- **Rot aufgehellt.** Das Bibliotheksrot `{colors.status-fehler}` erreicht auf dunkler Fläche nur 2,30:1 und als Balken 1,88:1. Im Dunkeln gilt `{colors.status-fehler-dark}`: 5,39:1 auf der Fläche, 4,04:1 als Balken gegen die Spur. Damit trägt der rote Balken der Notfall-Box auch im Dunkeln als Signal.
- **Statusfarben und Feldrahmen** haben eigene `*-dark`-Werte; Orange bleibt unverändert, weil es auf dunklem Grund ausreicht.

| Rolle hell | Dunkel | Geprüft (WCAG-Formel) |
|---|---|---|
| `{colors.ground}` | `{colors.ground-dark}` #0F1719 | Text 16,00:1 |
| `{colors.surface}` | `{colors.surface-dark}` #1D2A2E | gegen Grund 1,23:1, daher mit Kante |
| `{colors.light}` | `{colors.light-dark}` #2B3F44 | Text darauf 9,75:1 |
| `{colors.neutral-10}` | `{colors.neutral-10-dark}` #253539 | Text darauf 11,24:1 |
| `{colors.border}` | `{colors.border-dark}` #668E97 | 5,08:1 gegen ground-dark, 4,13:1 gegen surface-dark |
| `{colors.table-border}` | `{colors.table-border-dark}` #2B3F44 | Haarlinie, dekorativ |
| `{colors.line-on-light}` | `{colors.line-on-light-dark}` #668E97 | 3,10:1 gegen light-dark |
| `{colors.akzent-10}` … `{colors.akzent-80}` | `{colors.akzent-10-dark}` #0F1719, `{colors.akzent-20-dark}` #2B3F44, `{colors.akzent-40-dark}` #668E97, `{colors.akzent-60-dark}` #83B4BF, `{colors.akzent-80-dark}` #ADCDD4 | Avatar: `{colors.text-dark}` auf `{colors.akzent-20-dark}` 9,75:1 |
| `{colors.contrast}` / `{colors.on-contrast}` | `{colors.contrast-dark}` / `{colors.on-contrast-dark}` (unverändert) | 9,25:1 |
| `{colors.text}` | `{colors.text-dark}` #EAF2F4 | 13,01:1 auf surface-dark |
| `{colors.text-muted}` | `{colors.text-muted-dark}` #ADCDD4 | 8,76:1 auf surface-dark |
| `{colors.action}` (Link, Feldrahmen) | `{colors.action-dark}` #83B4BF | 6,50:1 auf surface-dark, 7,99:1 auf ground-dark |
| Button `{colors.action}` / `{colors.on-action}` | `{colors.action-dark}` / `{colors.on-action-dark}` | 7,81:1 |
| `{colors.primary}` (untere Navigation) | `{colors.primary-dark}` #255F6C | Weiß 7,16:1, Fokus-Gelb 6,67:1 |
| `{colors.akzent}` (Balkenfüllung) | `{colors.akzent-dark}` #83B4BF | 4,87:1 gegen light-dark |
| `{colors.neutral-40}` | `{colors.neutral-40-dark}` #5C616B | deaktivierter Rahmen |
| `{colors.neutral-60}` / `{colors.status-offen}` | `{colors.neutral-60-dark}` / `{colors.status-offen-dark}` #A3A7B1 | 6,13:1 auf surface-dark |
| `{colors.status-fehler}` | `{colors.status-fehler-dark}` #FF6B81 | **5,39:1** auf surface-dark, 6,63:1 auf ground-dark, Balken **4,04:1** gegen light-dark |
| `{colors.status-warnung}` | `{colors.status-warnung-dark}` #DC9018 | 5,65:1, Balken 4,24:1 |
| `{colors.status-erfolg}` | `{colors.status-erfolg-dark}` #7DB39E | 6,19:1, Balken 4,65:1 |
| `{colors.status-hinweis}`, `{colors.status-aktiv}` | `{colors.status-hinweis-dark}`, `{colors.status-aktiv-dark}` #83B4BF | 6,50:1 |
| `{colors.error-field}` | `{colors.error-field-dark}` #FF6B6B | 5,32:1 auf surface-dark |
| `{colors.focus}` | `{colors.focus-dark}` #FFFF00 | 13,74:1 |

Hell und dunkel nebeneinander, mit Prüfstreifen: [key-dunkelmodus.html](mockups/key-dunkelmodus.html).

## Typography

Nur **Sarabun**, lokal eingebunden (die Bibliothek liefert die Schriftdateien mit; das ist auch für den Offline-Betrieb wichtig). Keine Zweitschrift.

| Rolle | Token | Einsatz |
|---|---|---|
| Fließtext | `{typography.body}` | 16 px Regular, Zeilenhöhe 24 px |
| Klein | `{typography.small}` | Hinweise, Metadaten, untere Navigation, Speicher-Hinweis |
| Seitentitel | `{typography.h1}` | Light, 24 px; Namen und Betonung darin in `{typography.h1-emphasis}` (SemiBold) |
| Abschnitt | `{typography.h2}` | SemiBold, 20 px |
| Flächentitel | `{typography.h3}` | Bold, 18 px |
| Dachzeile | `{typography.kicker}` | Versalien (Großbuchstaben), gesperrt (mit erweitertem Zeichenabstand); Datum, Phase, „Jetzt · 14:00 Uhr“ |
| Kennzahl | `{typography.stat-number}` | große Light-Zahl in `{colors.action}` |
| Tabellenkopf | `{typography.table-head}` | Versalien, SemiBold |
| Wortmarke | `{typography.wordmark}` | „Zeltlotse“, SemiBold, schwarz |

Deutsche Typografie: „…“, Halbgeviertstrich –, geschützte Leerzeichen (14:00&nbsp;Uhr, 3.&nbsp;August), Ziffern in Tabellen und Balken mit gleicher Breite (tabular-nums).

**Druck (Notfallordner):** ELKW-Druckbausteine, `{typography.print-body}` 10,5 pt, Titel `{typography.print-h1}` und `{typography.print-h2}`.

## Layout & Spacing

Abstände aus der Dichte „Arbeiten“: `{spacing.xs}` 0,5 rem, `{spacing.s}`/`{spacing.m}` 1 rem, `{spacing.l}` 1,5 rem, `{spacing.xl}` 2 rem; Raster-Abstand `{spacing.gap}`, Innenabstand von Flächen `{spacing.box-pad}`. Felder sind `{spacing.control-h}` hoch.

**Handy (unter 1024 px):** einspaltig, Seitenrand `{spacing.margin-mobile}`. Oben die weiße App-Leiste mit Wortmarke und Name der Freizeit, unten die **untere Navigation** mit fünf gleich breiten Punkten. Nur der Inhalt dazwischen scrollt. Flächen stapeln sich mit `{spacing.m}` Abstand auf dem Grundton.

**Laptop (ab 1024 px):** App-Rahmen der Bibliothek – App-Leiste oben, Seitennavigation links, Arbeitsbereich rechts. Im Arbeitsbereich das breite Raster (Inhalt 1,6 : Seitenspalte 1), maximal `{spacing.container}`.

**Öffentliche Seiten** (Freizeitseite, Anmeldung, Teilnehmer-Zugang): eine Spalte, Textbreite höchstens `{spacing.measure}`, ohne App-Navigation, Name der Organisation oben.

Tabellen: am Laptop als Datentabelle; unter 640 px gestapelt (die Zellen einer Zeile untereinander) oder seitlich scrollbar. Kein seitliches Scrollen der ganzen Seite ab 320 px Breite (WCAG-Kriterium 1.4.10); geprüft wird mit 375 px Breite.

## Elevation & Depth

**Keine Schatten, keine Verläufe, keine Transparenz.** Tiefe entsteht nur durch Fläche auf Grundton: weiße `{colors.surface}`-Flächen auf `{colors.ground}` (im Dunkeln `{colors.surface-dark}` auf `{colors.ground-dark}`). Rahmen höchstens 1 px; im Dunkelmodus tragen Flächen immer die Kante `{colors.border-dark}` (siehe Colors → Dunkelmodus). Es gibt keine schwebenden Ebenen, weil es keine Pop-ups gibt (PRD NFR-13, nicht-funktionale Anforderung 13). Bewegung nur als Farbwechsel beim Überfahren (0,3 s); keine Einblend- oder Gleiteffekte.

## Shapes

Rundungen des Stils „nahbar“, nur über die Token:

- `{rounded.lg}` 12 px – Flächen, Cards, Meldungen, Notfall-Box, Kennzahlen, Tabellen, Leer-Zustand, Inline-Bestätigung.
- `{rounded.md}` 8 px – Eingabefelder.
- `{rounded.sm}` 6 px – Buttons, Segmente, Avatar, Punkte der Seitennavigation.
- `{rounded.none}` – Balken, Statusquadrat, Farbbalken links in Meldungen.

**Keine Pills** (vollrunde Formen), keine Badges, keine Chips. Eine Anzahl in der Seitennavigation ist reiner Text in normaler Schrift, ohne Kreis und ohne Farbe.

## Components

Aus der Bibliothek unverändert genutzt (visuelle Spezifikation dort): App-Leiste, Seitennavigation, Brotkrumen, Seitenkopf, Fläche, Card, Begrüßung, Kennzahlen, Werkzeugleiste, Datentabelle (mit gestapelter Variante), Status, Meldung, Formular (Feld, Fieldset, Pflichtfeld, Auswahl), Schritte, Aufklapp, Reiter, Segmente, Person/Avatar, Terminliste, Details-Liste, Leer-Zustand, Ladezustand, Sprunglink, Druckbausteine A4. **Nicht genutzt:** Dialog/Pop-up und Drawer (PRD NFR-13: keine Pop-up-Fenster) sowie das ELKW-Logo.

**Festlegungen zu Bibliotheksbausteinen** – nur das, was Zeltlotse an unverändert genutzten Bausteinen festlegt:

- **Status:** Quadrat `{components.status-square}` + Wort in normaler Schrift; Farben und Zuordnung laut Colors.
- **Meldung:** Titel immer mit Doppelpunkt („Achtung:“, „Fehler:“, „Gespeichert:“); `{components.alert}`, Balken je Art in Statusfarbe.
- **Kennzahlen:** Zahl + Beschriftung, ohne Icons; `{components.stats}`.
- **Leer-Zustand:** ein Satz + ein Standard-Button; `{components.empty-state}`.
- **Avatar:** Initialen; `{components.avatar}`, dunkel `{colors.text-dark}` auf `{colors.akzent-20-dark}` (9,75:1).
- **Segmente:** Schnellfilter mit 2–6 Optionen; aktiv `{components.segments-active}`.

Die folgenden Tabellen beschreiben Zeltlotse-Festlegungen und zusammengesetzte Bausteine (Verhalten in `EXPERIENCE.md` → Component Patterns). Seitennavigation am Laptop: [navigation-laptop.html](mockups/navigation-laptop.html); Handy-Bausteine während der Freizeit: [key-dunkelmodus.html](mockups/key-dunkelmodus.html) und [key-notfall.html](mockups/key-notfall.html); Kind-Card: [key-familienuebersicht.html](mockups/key-familienuebersicht.html).

**Rahmen und Navigation**

| Baustein | Aufbau | Farben und Maße | Zustände |
|---|---|---|---|
| **Wortmarke** | Reiner Text „Zeltlotse“ links in der App-Leiste; daneben der Name der Freizeit klein | `{components.wordmark}` | Fokus `{components.focus-ring}` |
| **App-Leiste** | Weiß; Wortmarke, Freizeitname; rechts am Laptop Suche und Name (führt zur Seite „Konto“), am Handy keine Suche. Zwischen Start- und Enddatum einer Freizeit rechts zusätzlich der Notfall-Eintrag | `{components.app-bar}` | – |
| **Notfall-Eintrag (App-Leiste)** | Textbutton „Notfall“ mit rotem Statusquadrat davor, rechts in der App-Leiste | `{components.button-text}` + `{components.status-square}` in `{colors.status-fehler}` (dunkel `{colors.status-fehler-dark}`) | nur zwischen Start- und Enddatum sichtbar |
| **Seitennavigation** (Laptop) | Liste der Bereiche in vier Gruppen (Planen, Menschen, Versorgen, Ablage), Gruppentitel als Dachzeile; „Einstellungen“ ganz unten, mit Linie abgesetzt. Eine Anzahl rechts nur bei Anmeldungen, Kanäle und Schäden & Verluste, als reiner Text in `{typography.small}`, ohne Kreis und Farbe | `{components.side-nav-item}`; aktiv `{components.side-nav-item-active}` (weich hinterlegt, kein Balken) | Hover `{colors.neutral-10}` |
| **Untere Navigation** (Handy) | Navigationsleiste der Bibliothek am unteren Rand, fünf gleich breite Punkte, **nur Text**, keine Icons | `{components.bottom-nav}`, Punkte mindestens `{spacing.touch-large}` hoch; aktiv `{components.bottom-nav-item-active}`; Dunkel: `{colors.primary-dark}` | Fokus in `{colors.focus-on-color}` |
| **Offline-Leiste** | Schmale Leiste über dem Inhalt, direkt unter der App-Leiste: „Offline – zuletzt aktualisiert HH:MM“. Warten offline geschriebene Einträge (Vorfall-Protokoll) auf Verbindung, steht zusätzlich da: „2 Einträge warten auf Verbindung“ | `{components.offline-bar}`, volle Breite, kleine Schrift, kein Statusrot | nicht nutzbare Bereiche ausgegraut (50 % Deckkraft) mit Satz |
| **Schnellsuche** | Eingabefeld (Suche) mit Label; Treffer als Personenliste (Avatar + Name) darunter im Inhalt, nicht als schwebende Liste. Auf der Seite „Sicherheitskette“ ganz oben mit dem Label „Betroffene Person suchen“ | `{components.input}`, Treffer `{components.avatar}` | Fokus `{components.focus-ring}` |

**Bedienung und Rückmeldung**

| Baustein | Aufbau | Farben und Maße | Zustände |
|---|---|---|---|
| **Button** | Standard mit Pfeil bei Ansichtswechsel, ohne Pfeil bei Aktion an Ort und Stelle; Outline für Nebenaktion; Textbutton für Tertiäres | `{components.button-primary}`, `{components.button-outline}`, `{components.button-text}` | deaktiviert 50 % Deckkraft – nur offline in ausgegrauten Bereichen und während einer laufenden Übertragung; sonst fehlt ein nicht erlaubter Button ganz |
| **Eingabefeld** | Label darüber, Hinweis/Fehler darunter | `{components.input}`; Fehler `{components.input-error}` + Text „Fehler: …“; Dunkel: Rahmen `{colors.action-dark}`, Fehler `{colors.error-field-dark}` | Fokus `{components.focus-ring}` |
| **Speicher-Hinweis** | Kleiner Text unter dem bearbeiteten Feld („Gespeichert“) mit Textbutton „Rückgängig“ | `{components.autosave-hint}` an der Stelle des Feldhinweises; kein Toast (kurz eingeblendete, schwebende Meldung) | verschwindet beim nächsten Feldwechsel |
| **Inline-Bestätigung** | Der auslösende Button klappt an Ort und Stelle auf: kurzer Satz („Alle informieren?“), dahinter Standard-Button „Ja, …“ (ohne Pfeil) und Outline-Button „Nein“ | Fläche `{components.inline-confirm}` in Breite der Zeile, damit der Bereich als zusammengehörig erkennbar ist | – |
| **Notfall-Box** | Meldungs-Box mit rotem Balken links, Titel „Im Notfall:“, ein Satz, Standard-Button in voller Breite („Notfallhilfe öffnen“), darunter Telefon-Link „Notruf 112“ | `{components.notfall-box}`, Button `{spacing.touch-large}` hoch; Button bleibt blaugrün, nicht rot; Balken dunkel `{colors.status-fehler-dark}` | – |
| **Balken mit Statuswort** | Kopfzeile (Bezeichnung links, Zahl rechts), darunter Spur mit Füllung, darunter Statuswort mit Quadrat | Spur `{components.bar-track}`, Füllung `{components.bar-fill}`, bei Warnung `{components.bar-fill-warnung}`, bei Überschreitung `{components.bar-fill-fehler}`; große Variante 1,25 rem für die Hauptzahl einer Fläche | Balken für Screenreader verborgen; Zahl und Wort tragen die Information |
| **Hinweis „nicht mitgeteilt“** | Kleine Zeile am Programmpunkt: „Geändert, noch nicht mitgeteilt“ mit Textbuttons „Jetzt informieren“ und „Ausblenden“ | `{typography.small}`, `{colors.text-muted}`; kein Statusrot | – |
| **Schieberegler** (Beitragsrechner) | Regler mit Label; der Wert steht als Zahl daneben, der Balken darunter wandert mit | Spur `{components.bar-track}`, Griff und Füllung `{colors.action}` | Fokus `{components.focus-ring}` |
| **Datei hochladen** (Belege, Nachweise) | Formularfeld der Bibliothek mit Button „Foto aufnehmen oder Datei wählen“; nach dem Hochladen Dateiname mit Textbutton „Entfernen“ | `{components.input}`, `{components.button-outline}` | Fehler als Meldung „Fehler: …“ mit „Erneut versuchen“ |

**Bausteine einzelner Bereiche**

| Baustein | Aufbau | Farben und Maße | Zustände |
|---|---|---|---|
| **Wochenraster** | Laptop: eine Fläche mit einer Spalte je Tag; Tageskopf als Tabellenkopf, darunter die Programmpunkte des Tages als Terminliste (Uhrzeit als Dachzeile, Titel, Status). Handy: ein Tag als Terminliste, Tageswechsel über Reiter je Tag. Feste Mahlzeiten stehen wie andere Programmpunkte in der Liste | `{components.week-grid}`; Einträge aus der Terminliste der Bibliothek | heutiger Tag als aktiver Reiter oder Spalte mit `{colors.light}` hinterlegt |
| **Kanal-Nachrichten** | Liste der Nachrichten untereinander, je Nachricht Avatar, Vorname, Uhrzeit und Text; Eingabefeld mit Standard-Button „Senden“ unten, auf dem Handy direkt über der unteren Navigation. Ansagen-Zettel und Besprechungspunkte als Liste mit Checkbox | `{components.message}`; Eingabe `{components.input}` | ungelesen ohne Zähler und ohne Farbe; offline ausgegraut |
| **Material-Status** | Segmente der Bibliothek mit vier Optionen „gebraucht / vorhanden / besorgt / eingepackt“; in Listen als Status | aktiv `{components.segments-active}`; Status: gebraucht = offen, vorhanden/besorgt = aktiv, eingepackt = Erfolg | Fokus `{components.focus-ring}` |
| **QR-Code-Card** (Teilnehmer-Zugang) | Große Card: Name der Freizeit als Titel, QR-Code mittig, Link als Text darunter, Buttons „Drucken“ und „Link kopieren“; Druck als A4-Aushang mit den Druckbausteinen | Card der Bibliothek; QR-Code schwarz auf `{colors.surface}` (21:1), auch im Dunkelmodus auf weißer Fläche, damit Kameras ihn lesen | – |
| **Gerichttausch** (Vorher/Nachher) | Zwei Spalten „Vorher“ und „Nachher“ als Datentabelle der Bibliothek, geänderte Mengen fett; am Handy untereinander; darunter Standard-Button „Übernehmen“ und Outline-Button „Anpassen“ | `{components.surface}`, Tabellenkopf `{typography.table-head}` | – |
| **Einwilligung** (Freizeitpass) | Zeile je Einwilligung: Bezeichnung, Schalter (Checkbox der Bibliothek), darunter klein „erteilt am …“ oder „widerrufen am …“; Widerruf über `{components.inline-confirm}` | Rahmen `{colors.action}`, Text klein `{typography.small}` | Fokus `{components.focus-ring}` |
| **Kind-Card** (Familienübersicht) | Card je Kind: Name als Titel, darunter je Freizeit eine Zeile mit Anmeldestatus (kein Anmelden-Knopf); Hinweis „Freizeitpass prüfen“ als Status Warnung, wenn etwas fehlt oder veraltet ist | Card der Bibliothek, `{components.status-square}` | – |
| **Notfallordner (Druck)** | ELKW-Druckbausteine A4, je Kind eine Seite: Name als Titel, Infokasten Notfallkontakte, Freizeitpass-Angaben als Liste | schwarzweiß-tauglich: Information nie nur über Farbe, Akzentflächen dürfen im Graudruck verschwinden | – |

## Do's and Don'ts

| Do | Don't |
|---|---|
| Werte nur über Token und ELKW-Variablen setzen (`data-farbwelt="09"`, `data-stil="nahbar"`, `data-dichte="arbeiten"`) | Bausteine über eigene Selektoren umfärben |
| Status immer als Wort, Farbe nur als Quadrat oder Balken | Ampelflächen, farbige Badges, Farbe ohne Wort |
| Zahlen als Balken mit Statuswort zeigen (PRD NFR-16) | Zahlenkolonnen, Diagramme mit Legende |
| Blaugrüne Schrift nur auf Weiß | Links oder Dachzeile in Blaugrün direkt auf dem Grundton |
| Text-Wortmarke „Zeltlotse“ | ELKW-Logo oder Bild-Marke verwenden |
| Rückfragen inline an Ort und Stelle | Dialog, Pop-up, Drawer, Overlay |
| Rückmeldung als Meldung im Inhalt oder als Speicher-Hinweis am Feld | Toasts, Spinner (drehende Ladesymbole), Einblendungen |
| Untere Navigation nur mit Text | Icons in der Navigation, Deko-Icons, Emojis |
| Dunkelmodus aus der Geräteeinstellung, nur mit den `*-dark`-Token; Flächen im Dunkeln mit Kante | eigene Dunkel-Farben neben den `*-dark`-Token, Flächen ohne Kante im Dunkeln |
| Notfall-Box mit rotem Balken, Button blaugrün | rote Vollflächen oder roter Button |
| Eine Farbe + Neutraltöne | zweite Zusatzfarbe, Schatten, Verläufe, Pills |
