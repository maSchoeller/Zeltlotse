# Debt

Known debt and learnings not yet ripe, kept across runs — appended by the retro
in phase 4, read by the next maintenance run. One dated line per entry.

- 2026-08-27 — Einrichtungsseite `/einrichtung` ist ungeschützt erreichbar,
  solange kein GlobalAdmin existiert; sie schaltet sich danach ab. Wer eine
  frisch aufgesetzte Instanz zuerst findet, wird ihr Betreiber. Bewusst so
  entschieden (Lauf 2026-08-26-zeltlotse-grundgeruest).
- 2026-08-27 — Anmeldung über Bearer-Token statt Cookie-Sitzung, obwohl bei
  gleicher Domain eine reine Cookie-Anmeldung weniger Teile hätte. Access-Token
  im Arbeitsspeicher, Refresh als HttpOnly-Cookie mildert es.
- 2026-08-27 — EF-Core-Migrationen laufen beim Anwendungsstart. Bei mehr als
  einer Instanz ein Wettlauf; ein Advisory Lock wäre die Absicherung. Tragfähig,
  solange eine Instanz läuft — bricht genau beim ersten Hochskalieren.
- 2026-08-27 — Primärfarbe ist Schmuckfarbe 01 Dunkelblau #29447B statt des
  ELKW-Hausvioletts. Bewusste Abweichung vom Markenkern; Violett bleibt
  ausschließlich im unveränderten Logo.
- 2026-08-27 — Kein E-Mail-Versand. Einladungslinks und Kennwortrücksetzung
  gehen von Hand über fremde Kanäle. „Link kopieren" ist deshalb Kernfunktion.
- 2026-08-27 — Produktiver Betrieb setzt eine eigene Domain voraus
  (app./api. unter derselben registrierbaren Domain), sonst greift das
  Refresh-Cookie nicht. Lokal unkritisch.
- 2026-08-27 — Weiches Löschen wird nicht über einen Query-Filter erzwungen,
  sondern über die Erweiterung `.Aktiv()`. Wer sie vergisst, zeigt gelöschte
  Einträge in einer Liste — kein Datenleck, aber eine stille Falle. Der
  Query-Filter bleibt dem Mandanten vorbehalten.
- 2026-08-27 — Der Betreiber hat auf der Einladungstabelle ein eigenes Recht in
  der Row-Level-Security (`app.betreiber`), weil er zu keiner Organisation
  gehört und trotzdem Leitungen einladen muss. Einzige Ausnahme von „Betreiber
  sieht keine Inhalte"; Freizeiten und Zuordnungen bleiben ihm verschlossen.
- 2026-08-27 — Wird unmittelbar nach programmatischem Tippen `Enter` gedrückt,
  geht der erste Tastendruck gelegentlich verloren (`@bind:event="oninput"`
  baut das Feld neu auf). Mit menschlicher Tippgeschwindigkeit nicht
  reproduzierbar; als Beobachtung festgehalten.
