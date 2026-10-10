---
title: Abgleich PRD vs. Brainstorming-Gesprächsprotokoll
created: 2026-10-10
quellen:
  - prd.md (Stand 2026-10-10)
  - .memlog.md der PRD-Sitzung (hat Vorrang)
  - brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/.memlog.md (Zeilenangaben = "B<Zeile>")
---

# Abgleich: Brainstorming-Protokoll → PRD

Geprüft wurden alle Einträge „by user“ sowie Coach-Ideen, die der Nutzer ausdrücklich bestätigt hat (B42, B128, B158, B167, B196, B218, B220, B229, B251, Leitplanken B254). Punkte, die im PRD-.memlog bewusst geändert oder verworfen wurden (z. B. Warteliste, Tagesleitung, Mengenfaktoren, Essensplan-Kopplung, DSG-EKD, Zelteinteilung, 2-Klick-Regel, Herkunftsvermerk), sind **nicht** aufgeführt.

Vorschlag: **aufnehmen** = ins PRD einarbeiten · **addendum** = als kurze Ergänzung/Klarstellung oder Rückfrage an Marvin · **verwerfen** = bewusst nicht aufnehmen (kurz im .memlog vermerken)

## A. Haltung, Ton, Leitprinzipien (qualitativ)

| # | Protokoll | Was fehlt im PRD | Vorschlag |
|---|---|---|---|
| 1 | B11 | Leitprinzip „**Analoge Prozesse, die funktionieren, bleiben analog**; nicht mehr Planungsaufwand als nötig“. Das .memlog (Z. 14) hat nur die *Chaos-Rhetorik* entschärft, nicht dieses Auswahlkriterium. Es steckt implizit in vielen „außerhalb von Zeltlotse“-Entscheidungen (Absage, Inventar-Konflikt, Tagesleitung, Rückmeldung an Eltern), wird aber nirgends als Prinzip benannt. | **aufnehmen** als Leitprinzip 6 („Was analog gut läuft, bleibt analog“) |
| 2 | B103, B104, B105 | Worst-Case-Bilder des Nutzers: „Informationsdichte zerstört Übersicht und Fokus“ und „Verwaltungstool wird zum **Selbstzweck** hochgezüchtet“. PRD nennt nur „Gegenteil eines Verwaltungsmolochs“; bei den Gegensignalen fehlt das Selbstzweck-Signal. | **aufnehmen** als Gegensignal (z. B. „Das Team verbringt mehr Zeit mit Zeltlotse als vorher mit Excel“ / „Funktionen ohne erkennbaren Freizeit-Nutzen“) |
| 3 | B108 (Leitplanke B254) | „Alles außer 1–2 Pflichtfeldern ist optional **und eingeklappt**“ – NFR-12 nennt nur die Pflichtfeld-Grenze, nicht das Einklappen/Ausblenden optionaler Felder (Fokus-Gedanke aus B103). | **aufnehmen** in NFR-12 |
| 4 | B114, B115, B117 | **ChurchTools als konkretes Negativbeispiel** (Pop-ups, zig Gruppen/Unterkalender, schlechter Überblick) und die Aussage „hohe Konfigurierbarkeit fühlt sich schwerfällig an“ / „Meinung statt Optionen“. PRD übernimmt Einzelverbote, aber nicht die Haltung. Spannung: Das PRD führt inzwischen mehrere Org-Einstellungen ein (Freigabefenster, Löschfristen, Nachweis-Richtlinie, Module, Fragenbaukasten). | **addendum**: Leitprinzip „Feste Struktur, wenige Einstellungen; jede neue Einstellung braucht Begründung“; ChurchTools in Vision als Abgrenzung nennen |
| 5 | B107 | „Team ist **technisch versiert**“ – relevante Nutzerannahme (wenig Hand-Holding, PWA-Installation zumutbar). Fehlt im PRD. | **aufnehmen** (Kontext unter Rollen oder NFR-15) |
| 6 | B233 | „Kalkulierende sind **keine BWL-Menschen**“, „großer Wert auf Darstellung, sehr gute UX“. NFR-16 deckt „grafisch statt Zahlenkolonnen“ ab, die Begründung und Gewichtung (hohe Priorität für Finanz-UX) fehlt. | **addendum** in NS-8 (Miriam ohne BWL-Hintergrund) bzw. NFR-16 |

## B. Funktionale Lücken

| # | Protokoll | Was fehlt im PRD | Vorschlag |
|---|---|---|---|
| 7 | B35, B42, B111, Synthese B267, Leitplanke B254 („Export/Druck“) | **Küchen-Tagesansicht ausdruckbar** („zum Aufhängen in der Küche“) und allgemeiner **Export/Druck** (PDF/CSV, Teilnehmerliste usw.). PRD: nur Notfallordner (FR-33) und eigene Daten (FR-14) druck-/exportierbar – obwohl NFR-11 den Ausdruck zur Rückfallebene bis V6 erklärt. Widerspruch: Ohne druckbare Küchenansicht/Teilnehmerliste trägt NFR-11 nicht. | **aufnehmen**: FR-52 „ausdruckbar“; FR für Druck/Export von Freizeit-Listen (Teilnehmende, Einkaufsliste, Wochenraster) |
| 8 | B219 | Teilnehmerzahlungen (vom Nutzer entschieden): **Eltern sehen Betrag, Bankverbindung, Verwendungszweck**; Status **offen/bezahlt/erlassen**; Summe fließt in **Soll-Ist**. PRD hat nur „Beitrag bezahlt abhaken“ (FR-60). Das .memlog (Z. 63/66) holt nur das Abhaken in die MVP, die übrigen Teile sind weder übernommen noch verworfen. | **addendum**/Rückfrage: Elternansicht mit Zahlungsinfos und Status „erlassen“ in MVP? Kopplung an Soll-Ist? |
| 9 | B71, B55 | **Elterninfo** (Infomails, Packlisten, Infobriefe) vom Nutzer als „gute Idee“ bestätigt. PRD hat nur die Dokument-Sichtbarkeit „Eltern“ (FR-43). Offen: Wie erreichen Infobrief/Packliste Eltern, insbesondere **Gast-Anmeldende ohne Konto**? Eine Rundmail an alle Eltern ist weder geplant noch ausgeschlossen. | **addendum**/Rückfrage: Rundmail an Eltern per Knopfdruck (passt zu Leitprinzip 4) oder bewusst verwerfen |
| 10 | B66 vs. FR-11 / NS-4.8 | Nutzer: „Organisation kann Mitarbeiter **benachrichtigen**, wenn ein Dokument fehlt“. .memlog Z. 35 schließt nur *automatische* Nachrichten aus. **Widerspruch im PRD**: FR-11/NS-3.9 „Zeltlotse verschickt dazu keine Nachrichten“, NS-4.8 nennt bei „Nachweis läuft ab“ aber „Erinnerungen per Knopfdruck“. | **aufnehmen**: Erinnerung per Knopfdruck auch für Nachweise (analog FR-25) – oder NS-4.8 korrigieren |
| 11 | B205, B209 | Nutzer: „**Organisationsübersicht aller Freizeiten mit Warnhinweisen: ja**“ und „Org-Rolle bestimmt ‚sehe alle Freizeiten‘ vs. ‚nur zugewiesene‘“. .memlog Z. 78 ändert das nur für **Org-Überblick**. Für **Org-Admin** ist offen, ob er alle Freizeiten (Inhalte) und eine Übersicht mit Warnhinweisen sieht. | **addendum**: Sichtrechte Org-Admin auf Freizeiten klären; Org-Übersicht mit Warnhinweisen für Admins aufnehmen oder bewusst verwerfen |
| 12 | B218, B211, B212 | Dokumente: Nutzer hat **Abgrenzungsregel** (Org = betrifft mehrere/alle Freizeiten; Freizeit = nur diese) und **eine Suche mit standortabhängigem Umfang** angenommen. FR-43 sagt nur „eine Suche über alle Dokumente“. | **aufnehmen** in FR-43 |
| 13 | B193/B196, B100 vs. FR-30 | Org-Dokumente (z. B. Schutzkonzept) laut bestätigter Idee „**in jeder Freizeit automatisch verlinkt**“; FR-30/NS-4.6 lässt die Leitung das Schutzkonzept manuell anhängen. Kleiner Widerspruch. | **addendum**: Schutzkonzept als Org-Dokument automatisch eingebunden, Leitung nur Bestätigung „gelesen“ einfordern |
| 14 | B198, B199 | Sorge des Nutzers: Dokumentenpool darf **keine Müllhalde** werden; kein heimatloser Upload („Freizeit allgemein“ statt „Sonstiges“). Fehlt in FR-43. | **aufnehmen** (ein Satz in FR-43) |
| 15 | B227, B229, B237 | **Belege einer Kalkulationskategorie zuordnen** (Voraussetzung für Soll-Ist pro Kategorie, vom Nutzer bestätigt; Soll-Ist-Balken grün/gelb/rot im Finanz-Schnitt V5). FR-58/59 nennen weder Kategoriezuordnung noch Soll-Ist pro Kategorie. NS-8.7 („Uwe prüft, ob die Küche im Tagessatz bleibt“) setzt sie aber voraus. | **aufnehmen** in FR-58/FR-59 |
| 16 | B87 | Jede Freizeitpass-Angabe zeigt **Datum der Erfassung/letzten Bestätigung** (Aktualität erkennbar, auch für das Team). FR-23 kennt nur die 12-Monats-Markierung. | **aufnehmen** in FR-23 |
| 17 | B88, B220 | **Fragenbaukasten**: Die Organisation pflegt Standardfragen; gleiche Frage = gleiche gespeicherte Antwort am Kind. PRD offen: Wer pflegt den Baukasten (Org-Admin fehlt in der Rollentabelle), darf die Leitung eigene Fragen ergänzen, wie wird über Organisationen hinweg (EJW/CVJM) wiederverwendet? | **addendum** (Rollentabelle + FR-22) |
| 18 | B82 | Freigabefenster **beginnt mit der Anmeldung**. FR-24 legt nur das Ende fest; unklar, ob das Team den Pass schon vor der Bestätigung durch die Leitung sieht. | **addendum**: Startpunkt festlegen (Anmeldung oder Bestätigung) |
| 19 | B80 | **Notfallkontakte pro Familie** im Familienkonto hinterlegt, bei Anmeldung sofort verfügbar. Im PRD liegen sie nur implizit im Freizeitpass (NS-6). | **addendum**: Notfallkontakte als Familienkonto-Daten, die in jeden Freizeitpass übernommen werden |
| 20 | B158 | Vom Nutzer bestätigt: **Tagesansicht des Programms** (neben dem Wochenraster) und Wochenraster „mit Zeitblöcken“. PRD hat nur Wochenraster (FR-34) und Küchen-Tagesansicht (FR-52). | **addendum** (für Mitarbeitende auf dem Handy naheliegend) |
| 21 | B181 / B73 | Aufgaben: **Datum optional** zusätzlich zur Phase; Ansicht „Meine Aufgaben“ **über alle Freizeiten**. FR-39 nennt weder das optionale Datum noch die freizeitübergreifende Ansicht. | **aufnehmen** (Halbsatz in FR-39) |
| 22 | B9 | **Freizeitarten** (Zeltlager, Junge-Erwachsene-Freizeit, Teens-Freizeit im Haus, **Wanderfreizeit**) fehlen. Bei der Wanderfreizeit gilt „ein Ort“ (NS-4.1) nicht, Netzabdeckung und Inventar sind anders. | **addendum**: Freizeitarten in Vision; prüfen, ob Wanderfreizeit mit dem Modell (Ort, Offline) funktioniert |
| 23 | B19 | Technik-Entscheidung des Nutzers „später **Azure-Dienst**“. NFR-18 sagt nur „Cloud“, NFR-3 nur „EU“. | **addendum** (Azure in NFR-18 oder ausdrücklich der Architektur überlassen) |

## C. Bewusst nicht als Lücke gewertet (zur Nachvollziehbarkeit)

- B23 Herkunftsvermerk „vom Team ergänzt“ → durch .memlog Z. 26 ersetzt (Team-Notizen bleiben in der Freizeit).
- B25 Essensplan fix → .memlog Z. 55. B29 Mengenfaktoren → Z. 89. B226/B259 Verpflegungskosten aus Essensplan → Z. 61.
- B42 Abwesende/Mitesser/Sonderkost-Karte → Z. 58 (später). B41/B94 Offline → Z. 53 (V6).
- B120 2 Klicks → Z. 97 (3–4). B126/B128 Warteliste → Z. 24/87. B159 Tagesleitung → Z. 68.
- B178 „Teilnehmende mit Konto automatisch Teil der Freizeit“ → Z. 70/81 (Teilnehmer-Ansicht nur per QR).
- B241/B251 DSG-EKD → Z. 94 (DSGVO). B79 getrennte Eltern → Z. 20 (Patchwork ausgeklammert).
- B255 Finanzen/Verpflegung im Kern → Z. 15 (MVP = V1–V6).
- Coach-Synthesen B264–B268 (z. B. „Listen werden nie von Hand gepflegt“) wurden vom Nutzer nicht einzeln bestätigt; „Einmal erfassen, alles ableiten“ steht zudem in Spannung zu FR-49 (Mengen überschreiben). Nicht gewertet, ggf. separat besprechen.
