# Designsystem — Zeltlotse

Verbindlich für jede Oberfläche in diesem Projekt. Grundlage: ELKW-App-Stil
(ruhig, hell, geordnet, editorial). Jeder Abstand im Projekt stammt aus der
Skala unten — freie Pixelwerte sind ein Fehler, kein Stilmittel.

## Farbe

Primärfarbe ist **Schmuckfarbe 01 Dunkelblau `#29447B`**. Sie übernimmt die
Rolle, die im ELKW-Manual das Hausviolett hat: Aktion, Auswahl, Fokus,
Navigation. Das Hausviolett `#8D197C` erscheint ausschließlich im unveränderten
Logo. Genau eine Schmuckfarbe im Produkt — keine zweite Akzentfarbe.

Gemessene Kontraste (WCAG, auf Weiß): `#29447B` = **9,5:1** — trägt Schrift und
Fläche gleichermaßen, weiße Schrift darauf ebenfalls 9,5:1.

```css
:root {
  /* Marke */
  --zl-primary:            #29447B;  /* Schmuckfarbe 01 Dunkelblau */
  --zl-primary-hover:      color-mix(in srgb, var(--zl-primary) 88%, black);
  --zl-primary-tint:       #EAECF2;  /* 10 % auf Weiß — Auswahl, ruhige Fläche */
  --zl-primary-tint-strong:#CAD0DE;  /* 25 % auf Weiß — Linien, Ränder */

  /* Flächen */
  --zl-canvas:             #F5F6F8;
  --zl-surface:            #FFFFFF;
  --zl-surface-quiet:      #EEF1F4;  /* Filterzeile, Werkzeugleiste */
  --zl-border:             #D8DCE2;

  /* Schrift */
  --zl-text:               #000000;
  --zl-text-secondary:     #474F60;
  --zl-text-on-primary:    #FFFFFF;

  /* Rückmeldung — nur Symbol, Rand und Tönung. Nie als Schriftfarbe. */
  --zl-success:            #518471;  /* 4,3:1 */
  --zl-warning:            #DC9018;  /* 2,6:1 */
  --zl-error:              #B61231;  /* 6,7:1 — als einzige auch für Text zulässig */
  --zl-success-tint:       #EDF3F1;
  --zl-warning-tint:       #FBF4E8;
  --zl-error-tint:         #FBECEF;
}
```

**Regel für Rückmeldungen:** Meldungen tragen ihre Bedeutung über Symbol,
farbigen linken Rand und getönte Fläche — der Text bleibt schwarz. Grün und
Orange erreichen als Schriftfarbe die nötigen 4,5:1 nicht.

## Typografie

Sarabun, ausschließlich. Vier Schnitte: Light 300, Regular 400, SemiBold 600,
Bold 700. **Selbst ausgeliefert** aus `wwwroot/fonts/` — keine Einbindung über
den Google-CDN, da das personenbezogene Daten an Dritte überträgt.

| Rolle | Größe / Zeilenhöhe | Schnitt |
|---|---|---|
| Seitentitel | 28 / 36 px | Bold |
| Abschnittstitel | 20 / 28 px | SemiBold |
| Kartentitel | 16 / 24 px | SemiBold |
| Fließtext | 15 / 24 px | Regular |
| Beschriftung, Metadaten | 13 / 20 px | Regular |
| Tabellenkopf | 13 / 20 px | SemiBold |

Keine Versalien in Überschriften, keine zweite Schriftart, keine weiteren Größen.

```css
--zl-font: "Sarabun", "Segoe UI", system-ui, sans-serif;
```

## Abstände

Eine Skala, keine Ausnahmen.

| Token | Wert | Verwendung |
|---|---|---|
| `--zl-space-1` | 4px | innerhalb eines Bedienelements |
| `--zl-space-2` | 8px | zusammengehörige Kleinteile |
| `--zl-space-3` | 12px | kompakter Rhythmus, Tabellenzellen |
| `--zl-space-4` | 16px | Grundabstand |
| `--zl-space-5` | 24px | Karteninnenraum, Feldabstand |
| `--zl-space-6` | 32px | Abschnitte zueinander |
| `--zl-space-7` | 40px | Seitenrand, große Atempausen |

Radien: Karten und große Container `12px`, Eingaben und Schaltflächen `10px`,
Abzeichen `999px` nur wenn die Pillenform wirklich etwas bedeutet.

## Layout-Grundregeln

- Seitenbreite höchstens `1200px`, zentriert, Seitenrand `--zl-space-7`
  (ab 768px abwärts `--zl-space-4`).
- Mindestabstand zwischen zwei bedienbaren Elementen: `--zl-space-2`.
- Bedienelemente sind 40px hoch. Bei grobem Zeigegerät (Finger) beträgt die
  Trefferfläche mindestens 44×44px — über Innenabstand, nicht über Schriftgröße.
- Tabellen laufen niemals über den Seitenrand hinaus: Der Tabellenbereich
  scrollt waagerecht in sich selbst, die Seite nicht.
- Lange Namen brechen um statt abzuschneiden; wo abgeschnitten wird, steht der
  vollständige Wert im Titel-Attribut.
- Erst die Arbeitsfläche, dann Beiwerk: Die Liste beginnt im ersten Bildschirm,
  keine großflächige Einleitung darüber.

## Bedienelemente

**Schaltflächen.** Primär: Fläche `--zl-primary`, Schrift weiß, Radius 10px.
Sekundär: weiße Fläche, Rand `--zl-border`, Schrift schwarz. Tertiär: reine
Schrift in `--zl-primary`. Höchstens **eine** primäre Schaltfläche je Ansicht —
der Akzent verliert sonst seine Bedeutung.

**Zustände.** Fokus: 3px Ring in `--zl-primary` mit 2px Abstand, immer sichtbar,
nie entfernt. Hover: `--zl-primary-hover`, sonst eine Stufe mehr Kontrast, keine
Bewegung. Ausgewählt: Fläche `--zl-primary-tint` plus 3px Balken links in
`--zl-primary`. Deaktiviert: 45 % Deckkraft, weiterhin lesbar, Grund erklärt.

**Formulare.** Beschriftung über dem Feld. Hilfetext darunter, `13px`,
`--zl-text-secondary`. Pflichtfelder werden nicht mit Sternchen markiert —
stattdessen tragen optionale Felder den Zusatz „(optional)". Fehler erscheinen
unter dem Feld, ruhig formuliert, mit `--zl-error` als Rand und Symbol.

**Tabellen und Listen.** Mittlere Dichte: Zeilenhöhe 48px, Zellenabstand
`--zl-space-3`. Kopfzeile SemiBold, Trennlinien `--zl-border` als Haarlinie,
keine senkrechten Linien. Ganze Zeile ist anklickbar, wenn sie zu einer Ansicht
führt.

**Leere Zustände.** Jede Liste, die leer sein kann, hat einen: ein Satz, was hier
entstehen wird, und die primäre Schaltfläche, die es entstehen lässt. Nie eine
leere Fläche, nie nur „Keine Einträge".

**Ladezustände.** Platzhalterflächen in `--zl-surface-quiet` in der Form des
erwarteten Inhalts, kein springendes Layout. Unter 300ms wird gar nichts
angezeigt.

## Tastatur

Jede Ansicht ist vollständig ohne Maus bedienbar. Reihenfolge folgt der
Leserichtung. Dialoge fangen den Fokus und geben ihn beim Schließen an das
auslösende Element zurück. `Esc` schließt jeden Dialog.

## Bewegung

Ein- und Ausblenden in 120ms, kleine Positionswechsel in 160ms. Nichts federt,
nichts glänzt, nichts dreht sich.

## Ausdrücklich unerwünscht

Verläufe als Markenmittel, Glaseffekte, Neonakzente, große Schlagschatten,
Werbebanner-Denken in Arbeitsansichten, gedrängte Datenwüsten.
