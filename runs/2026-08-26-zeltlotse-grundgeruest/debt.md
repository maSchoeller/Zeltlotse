# Schulden dieses Laufs

- 2026-08-26 — Einrichtungsseite für den ersten GlobalAdmin ist ungeschützt
  erreichbar (nur selbstabschaltend, sobald ein GlobalAdmin existiert).
  Bewusst so entschieden; kein Einmal-Token, kein Netzwerkschutz.
- 2026-08-26 — Bearer-Token statt Cookie-Sitzung gewählt, obwohl bei gleicher
  Herkunft eine reine Cookie-Anmeldung weniger Teile und weniger Angriffsfläche
  hätte. Access-Token im Arbeitsspeicher, Refresh als HttpOnly-Cookie mildert es.
- 2026-08-26 — EF-Core-Migrationen laufen beim Anwendungsstart. Bei mehr als
  einer Instanz ist das ein Wettlauf; tragfähig nur, solange eine Instanz läuft.
- 2026-08-26 — Primärfarbe ist Schmuckfarbe 01 Dunkelblau #29447B statt des
  ELKW-Hausvioletts. Bewusste Abweichung vom Markenkern auf Wunsch; Violett
  bleibt ausschließlich im unveränderten Logo.
- 2026-08-26 — Kein E-Mail-Versand. Einladungslinks und Kennwortrücksetzung
  müssen von Hand über fremde Kanäle weitergegeben werden.
- 2026-08-26 — Getrennte Auslieferung von Oberfläche und Schnittstelle erzwingt
  eine eigene Domain mit Unterdomänen, bevor produktiv angemeldet werden kann.
- 2026-08-26 — `TestinfrastrukturTests` prüft nur Testlauf und Buildkette.
  Fachliche Tests entstehen je Scheibe in Phase 3.

## Abweichungen vom Design (Phase 3)

- 2026-08-26 — Entitäten liegen in `Zeltlotse.Core.Persistenz`, nicht in den
  Scheiben. Andernfalls verweist der Kontext auf die Scheiben und die Scheiben
  auf den Kontext — ein Zirkel. Die Scheiben behalten Endpunkte, Regeln und
  Prüfungen; das Datenmodell ist gemeinsam. design.md ist angepasst.
- 2026-08-26 — `Organisationsaufloeser` wanderte von der Organisationen-Scheibe
  nach `Persistenz`. Sonst hätte Freizeiten die Implementierung von
  Organisationen referenziert statt nur deren Verträge — genau die Grenze, die
  das Preset schützt.
- 2026-08-26 — Der Betreiber gehört zu keiner Organisation und scheiterte
  deshalb an der Richtlinie, als er eine Einladung schreiben wollte. Aufgedeckt
  vom Test, gelöst über ein zweites, benanntes Recht allein auf der
  Einladungstabelle (`app.betreiber`). Freizeiten und Zuordnungen bleiben ihm
  auch in der Datenbank verschlossen.
- 2026-08-26 — Einlösen und Vorschau einer Einladung laufen ohne Anmeldung und
  damit ohne Organisationsliste. Beide schalten für ihre Dauer in den
  Systemkontext (kein SET ROLE). Eng begrenzt, aber ein echter Bypass — wer
  diese beiden Wege erweitert, muss das mitdenken.
- 2026-08-26 — `Meine Freizeiten` zeigt zusätzlich die Freizeiten von
  Organisationen, in denen man OrgAdmin ist. design.md nannte nur Zuordnungen;
  ohne die Ergänzung sähe eine Organisationsleitung ihre eigenen Freizeiten auf
  der Startseite nicht.
- 2026-08-26 — Weiches Löschen wird nicht über einen Query-Filter erzwungen,
  sondern über die Erweiterung `.Aktiv()`. Vergisst jemand sie, erscheinen
  gelöschte Einträge in einer Liste — ärgerlich, aber kein Datenleck. Der
  Query-Filter bleibt für den Mandanten reserviert.

## Aus dem Smoke-Test (Phase 3)

- 2026-08-26 — Der Verweis auf `Zeltlotse.Client.styles.css` fehlte in der
  index.html; das gesamte isolierte Komponenten-CSS blieb wirkungslos. Kein Test
  hätte das gezeigt — nur der Blick in den Browser. Behoben.
- 2026-08-26 — Blazor brauchte `BlazorWebAssemblyLoadAllGlobalizationData`,
  sonst scheitert `de-DE` beim Start. Behoben; kostet Ladegröße, ist für
  deutsche Datumsangaben aber unverzichtbar.
- 2026-08-26 — Der Dialog zog den Fokus an sich, obwohl der Aufrufer ihn ins
  erste Feld setzt; und beim Schließen kehrte er nicht zur auslösenden
  Schaltfläche zurück. Beides behoben (`FokusBeimAufrufer`, `js/dialog.js`).
- 2026-08-26 — Wird unmittelbar nach programmatischem Tippen `Enter` gedrückt,
  geht der erste Tastendruck gelegentlich verloren (Neuaufbau des Feldes durch
  `@bind:event="oninput"`). Mit menschlicher Tippgeschwindigkeit nicht
  reproduzierbar; als Beobachtung festgehalten, nicht behoben.
- 2026-08-26 — Screenshots waren in dieser Sitzung nicht möglich (die
  Browser-Ansicht wird nicht dargestellt). Geprüft wurde stattdessen messend:
  Seitenüberlauf, Elementgrenzen, Kopfhöhe, Trefferflächen, Tabellenscroll bei
  1280px und 375px. Für den nächsten Lauf: Falls Screenshots verfügbar sind,
  zusätzlich optisch prüfen.
