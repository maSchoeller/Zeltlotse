---
project: Zeltlotse
name: Zeltlotse
status: final
created: 2026-10-10
updated: 2026-10-11
sources:
  - _bmad-output/planning-artifacts/prds/prd-Zeltlotse-2026-10-10/prd.md
  - _bmad-output/brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/brainstorm-intent.md   # bei Widerspruch gilt das PRD
  - _bmad-output/brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/roadmap.md             # bei Widerspruch gilt das PRD
inherits: >-
  Verhalten der ELKW-Designbibliothek (Skill elkw-design v4.0.0, references/apps.md) im Stil „nahbar“,
  Farbwelt 09 Blaugrün, Dichte „Arbeiten“; visuelle Werte in DESIGN.md.
---

# Zeltlotse – Erlebnis-Spine

Diese Datei ist das Grundgerüst („Spine“) für das Erlebnis und beschreibt, **wie sich Zeltlotse verhält**: Aufbau, Wege, Zustände, Sprache, Bedienregeln. Wie es aussieht, steht in `DESIGN.md`. Rollen, Begriffe, Nutzungsszenarien (NS) und Anforderungen (FR = funktionale Anforderung, NFR = übergreifende Anforderung) stehen im PRD und werden hier nur mit ihrer Kennung genannt.

Die Mocks in `mockups/` sind Referenz; bei Widerspruch gelten DESIGN.md und EXPERIENCE.md. Sie sind an den passenden Stellen unten verlinkt: [richtung-elkw-farbwelten.html](mockups/richtung-elkw-farbwelten.html) (Handy-Startseite während der Freizeit, Leitungs-Cockpit am Laptop), [navigation-laptop.html](mockups/navigation-laptop.html) (gewählt ist Variante 2: Seitennavigation am Laptop und Seite „Schäden & Verluste“) und vier Schlüsselansichten. Wie man die Mocks öffnet, steht in [mockups/README.md](mockups/README.md).

## Foundation

- **Form:** Web-App im Browser, auf dem Handy als installierbare Web-App (PWA: eine Website, die sich wie eine App auf den Home-Bildschirm legen lässt und teilweise offline läuft). Keine native App in der ersten vollständigen Version (PRD NFR-8).
- **Geräte-Vorrang nach Bereich:** siehe Tabelle unter dieser Liste. Alles funktioniert auf beiden Geräten; „zuerst“ heißt: dort wird die Ansicht zuerst entworfen und geprüft.
- **UI-System:** ELKW-Designbibliothek (Stil „nahbar“, Farbwelt 09, Dichte „Arbeiten“), Werte und Ergänzungen in `DESIGN.md`. Ein React-Baukasten oder anderes Framework ist **nicht gewählt**; das entscheidet die Architektur. Die ELKW-Klassen sind frameworkneutral.
- **Dunkelmodus:** folgt der Geräteeinstellung, kein eigener Schalter (`DESIGN.md` → Colors → Dunkelmodus).
- **Grundhaltung:** einfach, sorglos, schnell an die Information. Zeltlotse verschickt nichts und ändert keine Planung ohne Bestätigung (PRD Leitprinzip 6), ist sonst still (NFR-10) und kennt keine Pop-up-Fenster (NFR-13).

Geräte-Vorrang nach Bereich:

| Laptop zuerst | Handy zuerst |
|---|---|
| Planung, Leitungs-Cockpit, Kalkulation und Finanzen, Fragenbaukasten, Einstellungen der Organisation | Anmeldung, Notfall, Kanäle, Küche (Tagesansicht, Einkauf), Teilnehmer-Zugang per QR-Code |

## Information Architecture

### Ebenen

**Organisation › Freizeit › Bereiche.** Eine Person sieht nur, was ihre Rollen erlauben (PRD „Rollen und Rechte“). Wer in mehreren Organisationen oder Freizeiten ist, wechselt am Laptop oben in der App-Leiste, am Handy ganz oben unter „Mehr“. Der Wechsel öffnet eine eigene Seite mit der Liste der Organisationen und Freizeiten, kein Aufklappmenü über dem Inhalt (NFR-13).

**Konto:** eigene Seite mit Profil und Nachweisen, Zugriffsprotokoll und Meine Daten, Wechsel von Organisation und Freizeit sowie „Abmelden“; Zugänge siehe Navigation und Flächen.

### Einstieg nach Rolle

| Rolle (PRD) | Startseite nach dem Login |
|---|---|
| Eltern | **Familienübersicht:** je Kind eine Card mit seinen Freizeiten, Anmeldestatus und dem Hinweis „Freizeitpass prüfen“, wenn etwas fehlt oder veraltet ist ([key-familienuebersicht.html](mockups/key-familienuebersicht.html)) |
| Volljährige Teilnehmende | Familienübersicht mit nur einer Card (die eigene Person) |
| Team, Leitung | Startseite der zuletzt geöffneten Freizeit; bei mehreren Freizeiten zuerst „Meine Freizeiten“ |
| Org-Admin | Übersicht der Freizeiten der Organisation (schlichte Liste, siehe „Verwaltungsflächen“) |
| Org-Überblick | **Org-Überblick:** Übersicht der Freizeiten mit Statusfilter, Anschaffungswünsche, Inventar – keine Freizeit-Inhalte (PRD NS-13) |
| Global Admin | Liste der Organisationen |
| Teilnehmende mit Teilnehmer-Zugang | Öffentliche Wochenübersicht (kein Konto, kein Login) |

### Startseite der Freizeit wechselt mit der Phase

Die Phase folgt dem Status der Freizeit (PRD FR-45) und ihrem Start- und Enddatum. FR-45 kennt drei Status; „läuft“ teilt sich für die Startseite in zwei Phasen: die Tage der Freizeit und die **Nachbereitung** ab dem Tag nach dem Enddatum. Erst „Freizeit abschließen“ setzt den Status auf „gelaufen“.

| Phase | Wann | Startseite zeigt |
|---|---|---|
| Vorbereitung | Status „in Vorbereitung“ (vor dem Startdatum) | **Leitungs-Cockpit:** Anmeldungen als Balken gegen die Plätze, Warnhinweise (Freizeitpass fehlt, Nachweis läuft ab, Aufgabe unbesetzt, Sicherheitskette nicht eingerichtet), nächste Schritte, Budget-Balken, wenn das Modul Finanzen an ist. Das **Team** (nicht die Leitung) sieht das Cockpit ohne Leitungs-Warnhinweise, dafür mit „Meine Aufgaben“, dem Stand des Programms und dem Kanal „Team“. |
| Während der Freizeit | Status „läuft“, vom Start- bis zum Enddatum | **Jetzt und als Nächstes:** große Notfall-Box, Schnellsuche nach Personen, laufender und nächster Programmpunkt, „Meine Aufgaben“ mit dem Kurzlink „Schaden melden“ darunter, letzte Nachricht aus den Kanälen. Die **Leitung** sieht zusätzlich die Liste „Vorfall-Notizen“ der Freizeit. Mock: [key-dunkelmodus.html](mockups/key-dunkelmodus.html) (hell und dunkel). |
| Nachbereitung | Status „läuft“, ab dem Tag nach dem Enddatum bis zum Abschluss | **Abschluss:** Schäden und Verluste (die Einträge werden beim Abschluss zu Anschaffungswünschen, NS-12), Belege, offene Punkte (Nachbereitungs-Aufgaben), Knopf „Freizeit abschließen“ (nur Leitung). Notfall-Box und Notfall-Eintrag verschwinden. |
| Abgeschlossen | Status „gelaufen“ | Startseite der Nachbereitung, nur lesbar, mit dem Hinweis „Diese Freizeit ist abgeschlossen und nur lesbar.“ |

### Navigation

Die Seitennavigation am Laptop hat je Freizeit vier Gruppen, die Gruppentitel stehen als Dachzeile:

| Gruppe | Punkte |
|---|---|
| Planen | Übersicht · Programm · Aufgaben · Material · Inventar |
| Menschen | Anmeldungen · Freizeitpass · Kanäle |
| Versorgen | Küche · Finanzen |
| Ablage | Dokumente · Schäden & Verluste |

- **Laptop:** Seitennavigation mit den vier Gruppen der Tabelle. Ganz unten, abgesetzt: **Einstellungen** (Einstellungen der Freizeit: Module, Fragenbaukasten, Schutzkonzept, Sicherheitskette, Teilnehmer-Zugang, Anmeldelinks; nur Leitung). Oben in der App-Leiste der Wechsel von Organisation und Freizeit und der Name, der zur Seite „Konto“ führt. Die Startseite („Übersicht“) wechselt weiter mit der Phase (siehe oben). Mock: [navigation-laptop.html](mockups/navigation-laptop.html), Variante 2.
- **Handy, Team in einer Freizeit:** untere Navigation mit fünf Punkten **Start · Programm · Personen · Kanäle · Mehr**. „Mehr“ zeigt ganz oben „Konto“ und „Organisation oder Freizeit wechseln“, darunter dieselben Gruppen wie die Seitennavigation am Laptop, ohne die Punkte, die schon in der unteren Leiste stehen: Planen (Aufgaben, Material, Inventar), Versorgen (Küche, Finanzen), Ablage (Dokumente, Schäden & Verluste), darunter Einstellungen.
- **Eltern und volljährige Teilnehmende:** keine untere Navigation und keine Seitennavigation. Alles geht von der Familienübersicht aus über die Cards; oben steht „Konto“, jede Unterseite hat einen Zurück-Link zur Familienübersicht.
- „Personen“ (Handy) bündelt Anmeldungen und Freizeitpässe als eine Personenliste mit Suche; am Laptop sind es die zwei Punkte „Anmeldungen“ und „Freizeitpass“.
- **Schäden & Verluste** ist während der ganzen Freizeit erreichbar, weil Schäden während der Freizeit entstehen. Eintragen geht jederzeit; erst beim Abschluss (NS-12) werden die Einträge zu Anschaffungswünschen. Zugänge siehe Flächen „Schäden & Verluste“.
- Notfall und Vorfall-Protokoll erreicht man über die Notfall-Box der Startseite und über den kleinen Eintrag „Notfall“ in der App-Leiste jeder Seite. Beide gibt es nur vom Start- bis zum Enddatum; in der Nachbereitung verschwinden sie. Das Vorfall-Protokoll liegt auf der Seite „Sicherheitskette“ (auch offline) und am Freizeitpass einer Person. Die Notfall-Box setzt den Notfall-Button aus FR-29 um.
- Abgeschaltete Module (PRD FR-44) erscheinen in keiner Navigation.

### Öffentliche Seiten

Freizeitseite, Anmeldung über die Anmeldelinks, Vertrauensseite und Teilnehmer-Zugang sind **schlanke Einspalten-Seiten** im gleichen Stil, ohne App-Navigation, oben Name (und, falls vorhanden, Logo) der Organisation, fürs Handy gebaut. Sie funktionieren ohne Installation (NFR-9). Mock: [key-gast-anmeldung.html](mockups/key-gast-anmeldung.html).

### Flächen

| Fläche | Erreicht über | Zweck | FR | Flow |
|---|---|---|---|---|
| Freizeitseite (öffentlich) | Anmeldelink | Beschreibung, Team mit Bild und Herkunft, „ausgebucht“, öffentliche Dokumente | FR-15, FR-67, FR-43 | NS-1 |
| Anmeldung Teilnehmende (öffentlich) | Freizeitseite | Gast-Anmeldung, Geschwister nacheinander, Teilnahmebedingungen, „Anmeldung eingegangen“, Angebot „Konto aus diesen Daten erstellen?“, „Weiteres Kind anmelden“ (übernimmt die Elternangaben nur in derselben Sitzung) | FR-16, FR-17, FR-18, FR-19, FR-20 | NS-1 |
| Anmeldung Mitarbeitende (öffentlich) | Anmeldelink für Mitarbeitende | Anmeldung für das Team | FR-16 | NS-3 |
| Vertrauensseite (öffentlich) | Link auf Freizeitseite und im Anmeldeformular | Welche Daten, wer sieht sie, wann verschwinden sie | FR-66 | NS-1 |
| Login | Mail-Link, App-Leiste | Login-Link aus Mail, optional Passwort | FR-2 | NS-1 |
| Familienübersicht | Login (Eltern, volljährige Teilnehmende) | Cards je Kind, Status (kein eigener Anmelden-Knopf; Anmeldung nur über den Anmeldelink der Freizeit), Elterninfo, zweiten Elternteil hinzufügen, „Profil an … übergeben“; oben „Konto“; Ausgangspunkt aller Unterseiten (Zurück-Link) | FR-3, FR-4, FR-20, FR-68 | NS-1, NS-2, NS-11 |
| Elterninfo per Link | Mail an Gast-Anmeldende | Infobrief, Packliste, Notfallnummern ohne Konto | FR-68 | NS-1 |
| Freizeitpass ausfüllen | Familienübersicht, Aufforderung nach Anmeldung, eigenes Profil | Fragen der Freizeit, Vorausfüllung, „Stimmt das noch?“, Freigabefenster; Abschnitt „Einwilligungen“ je Kind (z. B. Fotos) mit Schalter je Einwilligung | FR-22, FR-23, FR-24, NFR-6 | NS-2, NS-3 |
| Konto | Name in der App-Leiste (Laptop), ganz oben in „Mehr“ (Handy), oben auf der Familienübersicht | Profil und Nachweise, Zugriffsprotokoll und Meine Daten, Wechsel Organisation/Freizeit, Abmelden | FR-1, FR-2, FR-13 | NS-2, NS-3 |
| Profil und Nachweise | Konto | Nachweise hochladen, Ausstellungsdatum, Einsicht freigeben, Status der Bestätigungen | FR-1, FR-8, FR-10, FR-12 | NS-3 |
| Zugriffsprotokoll und Meine Daten | Konto | Wer hatte wann Einsicht; Auskunft, Export (PDF, CSV), Konto löschen | FR-13, FR-14 | NS-2 |
| Profil übernehmen | Link in Mail an das volljährige Kind | Profil mit Freizeitpass und Verlauf übernehmen | FR-4 | NS-11 |
| Meine Freizeiten | Login (Team), Brotkrumen | Freizeiten, in denen die Person mitarbeitet | FR-7 | NS-3, NS-4 |
| Freizeit-Start (phasenabhängig) | Navigation „Start“/„Übersicht“ | siehe oben: Vorbereitung, während der Freizeit, Nachbereitung, abgeschlossen | FR-27, FR-29, FR-31, FR-39, FR-45 | NS-4, NS-6, NS-12 |
| Anmeldungen | Navigation „Anmeldungen“ (Menschen), Personen (Handy) | Liste Teilnehmende und Mitarbeitende, bestätigen, Absage eintragen, Nachrücken bestätigen, Nachweis-Status (Leitung) | FR-11, FR-20, FR-21, FR-67, FR-7 | NS-1, NS-3, NS-4 |
| Freizeitpässe / Personen | Navigation „Freizeitpass“ (Menschen), Personen (Handy), Schnellsuche | Pässe aller Personen, „Freizeitpass fehlt noch“, Erinnerung per Knopfdruck, Team-Notizen | FR-24, FR-25, FR-26, FR-28 | NS-2, NS-4, NS-6 |
| Einstellungen der Freizeit (Leitung) | Navigation „Einstellungen“ (unten), Mehr | Module, Fragenbaukasten, Schutzkonzept anhängen, Sicherheitskette einrichten (was zu tun ist, Ansprechperson am Heimatort), Teilnehmer-Zugang mit QR-Code, Anmeldelinks | FR-16, FR-22, FR-29, FR-30, FR-44, FR-64 | NS-4, NS-10 |
| Schutzkonzept lesen | Hinweis auf Startseite | Lesen und „gelesen“ bestätigen | FR-30 | NS-4 |
| Wochenraster | Navigation „Programm“ (Planen; Handy: untere Navigation) | Programmpunkte, feste Mahlzeiten, verschieben, „Ich übernehme“ | FR-34, FR-35, FR-62 | NS-5, NS-9 |
| Programmpunkt | Wochenraster | Material mit vier Zuständen, „Ich bringe das mit“, Inventar-Reservierung, Aufgaben, Dokumente, in Bibliothek ablegen | FR-36, FR-37, FR-39, FR-41, FR-43 | NS-5 |
| Aufgaben / Meine Aufgaben | Navigation „Aufgaben“ (Planen), Mehr, Startseite | Aufgaben nach Phase, selbst übernehmen, Vorlagen | FR-39, FR-40 | NS-4, NS-5, NS-12 |
| Gesamt-Materialliste | Navigation „Material“ (Planen), Mehr | Material aller Programmpunkte mit Menge und Status | FR-69 | NS-5 |
| Inventar | Navigation „Inventar“ (Planen), Mehr | Inventar der Organisation, Reservierungen für die Freizeit | FR-37 | NS-5 |
| Programmbibliothek | Programm, Organisation | Abgelegte Programmpunkte übernehmen | FR-41 | NS-5 |
| Küche: Rezepte, Essensplan, Einkaufsliste, Tagesansicht | Navigation „Küche“ (Versorgen), Mehr | Rezepte skalieren, Gerichte vormerken und tauschen, Einkaufsliste mit Allergen-Abgleich, freigeben, abhaken, Restetag | FR-46 bis FR-53 | NS-7 |
| Finanzen: Kalkulation, Beitragsrechner, Belege, Soll-Ist, Erstattung, Beiträge | Navigation „Finanzen“ (Versorgen), Mehr | siehe FR | FR-54 bis FR-60 | NS-8 |
| Dokumente | Navigation „Dokumente“ (Ablage), Mehr | Dokumente am Kontext, drei Sichtbarkeiten, Suche | FR-43 | NS-4, NS-10 |
| Kanäle „Team“ und „Alle“, Ansagen-Zettel, Besprechungsnotizen | Navigation „Kanäle“ (Menschen) | Kurznachrichten, Push, Ansagen, Besprechungspunkte abhaken | FR-61, FR-62, FR-63 | NS-9, NS-10 |
| Notfall: Sicherheitskette | Notfall-Box, Eintrag „Notfall“ | Was jetzt zu tun ist, wen anrufen, Weg zum Vorfall-Protokoll (Reihenfolge siehe Component Patterns „Sicherheitskette“). Eingerichtet von der Leitung in den Einstellungen der Freizeit; offline lesbar | FR-28, FR-29 | NS-6 |
| Vorfall-Protokoll | Sicherheitskette (auch offline), Freizeitpass einer Person | Vorfall, Medikamente, Entnahmen. Jeder Eintrag ist für die Leitung sofort sichtbar, ohne Senden-Knopf (interne Sichtbarkeit, kein Versand nach außen); offline beschreibbar | FR-31, FR-32 | NS-6 |
| Vorfall-Notizen (Leitung) | Startseite während der Freizeit (Leitung) | Alle Vorfall-Notizen der Freizeit, neueste oben; nach dem Abschluss im Archiv der Organisation | FR-31 | NS-6, NS-12 |
| Notfallordner (Druck) | Notfall, Freizeitpässe | A4-Ausdruck, je Kind eine Seite | FR-33 | NS-6 |
| Notfallkarte (offline) | Notfall | Verschlüsselte Offline-Ansicht der Notfallangaben | FR-65 | NS-6 |
| Schäden & Verluste | Navigation „Schäden & Verluste“ (Ablage), Mehr, Kurzlink „Schaden melden“ unter „Meine Aufgaben“ auf der Startseite (Handy, während der Freizeit), Startseite der Nachbereitung | Schäden, Verluste, Erste-Hilfe-Entnahmen jederzeit während der Freizeit eintragen; beim Abschluss → Anschaffungswünsche | FR-32, FR-38 | NS-6, NS-12 |
| Teilnehmer-Zugang (öffentlich, QR) | QR-Code am Infobrett | Wochenübersicht ohne Details, allgemeine Infos, Kanal „Alle“ lesen | FR-43, FR-61, FR-64 | NS-10 |
| Org-Überblick | Login (Rolle Org-Überblick) | Freizeiten mit Filter „gelaufen“, Anschaffungswünsche, Inventar | FR-37, FR-38, FR-45 | NS-13 |
| Organisation verwalten (Org-Admin) | Organisationswechsel in der App-Leiste | Freizeiten anlegen, Leitung einsetzen, Inventar, Aufgaben-Vorlagen, Org-Dokumente, Richtlinie für Nachweise, Freigabefenster, Löschfristen, Rollen vergeben, Archiv der Vorfall-Notizen | FR-5, FR-6, FR-9, FR-31, FR-37, FR-40, FR-43, NFR-5 | NS-4 (nur Anlegen der Freizeit); sonst kein eigener Flow (bewusste Lücke, siehe unten) |
| Organisationen (Global Admin) | Login | Organisation anlegen, ersten Org-Admin einladen | FR-5 | kein eigener Flow (bewusste Lücke, siehe unten) |

**Verwaltungsflächen (Org-Admin, Global Admin):** schlicht aus ELKW-Bausteinen – Listen und Detailseiten (Datentabelle, Formular, Details-Liste) –, ohne eigenen Ablauf. Das ist eine **bewusste Lücke**: Für Richtlinie für Nachweise, Freigabefenster, Löschfristen, Aufgaben-Vorlagen, Rollen vergeben und Organisation anlegen gibt es kein Nutzungsszenario und keinen eigenen Flow.

**Sichtbarkeit nach Rolle:** Was eine Rolle nicht darf, wird **nicht gezeigt** – kein gesperrter Knopf, kein „Kein Zugriff“-Bildschirm. Nur-Leitung-Funktionen (Anmeldung bestätigen, Absage eintragen, Erinnerung auslösen, Nachweis-Status, Module und Fragen, Freizeit abschließen) fehlen beim Team. Die Rolle Org-Überblick sieht Freizeiten nur als Zeile in der Übersicht, außer bei eigener Mitarbeit.

**3–4 Klicks:** Jede wichtige Information ist höchstens 3–4 Klicks von der Startseite der Freizeit entfernt (NFR-14). Der Freizeitpass einer Person ist über die Schnellsuche in zwei Schritten offen.

## Voice and Tone

Neutral, sachlich, ruhig, **ohne direkte Anrede** (kein Du, kein Sie). Einzige Ausnahme ist die Begrüßung mit Vornamen („Hallo, Jonas“); auch der Satz darunter ist neutral. Knöpfe sagen, was passiert. Deutsche Typografie wie in `DESIGN.md`. Der Bibliotheksstil „nahbar“ sieht eine persönliche Ansprache vor; Zeltlotse nimmt davon nur den Namen in der Begrüßung.

| So | Nicht so |
|---|---|
| „Anmeldung absenden“ | „Jetzt anmelden!“ / „Melde dich an“ |
| „Anmeldung eingegangen. Die Leitung meldet sich in Kürze.“ | „Super, du bist dabei! 🎉“ |
| „Freizeitpass fehlt noch“ | „Bitte füllen Sie umgehend den Freizeitpass aus.“ |
| „Stimmt das noch?“ | „Achtung: Ihre Daten sind veraltet!“ |
| „Alle informieren?“ | „Möchtest du wirklich alle benachrichtigen?“ |
| „Gespeichert · Rückgängig“ | „Ihre Änderungen wurden erfolgreich gespeichert.“ |
| „Noch keine Programmpunkte. Ersten Programmpunkt anlegen“ | „Hier ist noch nichts. Leg los!“ |
| „3 Punkte sind offen.“ | „Drei Dinge brauchen eure Aufmerksamkeit.“ |
| „Offline – zuletzt aktualisiert 14:32 Uhr“ | „Netzwerkfehler“ |
| „2 Einträge warten auf Verbindung“ | „Synchronisierung fehlgeschlagen“ |
| „Ausgebucht. Anmelden ist trotzdem möglich, eine Zusage gibt es erst, wenn ein Platz frei wird.“ | „Leider voll.“ |

- Meldungen mit Titel setzen den Titel mit Doppelpunkt: „Hinweis:“, „Gespeichert:“, „Achtung:“, „Fehler:“.
- Fehlertexte sagen, was zu tun ist: „Fehler: Geburtsdatum fehlt.“
- Zahlen in Texten der Oberfläche stehen als Ziffern: „3 Punkte sind offen.“
- Keine Ausrufezeichen, keine Emojis, keine Werbe-Superlative.

## Component Patterns

Verhalten. Aussehen in `DESIGN.md` → Components.

| Baustein | Einsatz | Verhaltensregeln |
|---|---|---|
| Wortmarke | App-Leiste | Führt zur Startseite der aktuellen Freizeit bzw. zur rollenabhängigen Startseite. |
| App-Leiste | jede Seite nach dem Login | Am Laptop Wechsel Organisation/Freizeit, Suche und Name (führt zur Seite „Konto“); am Handy keine Suche. Vom Start- bis zum Enddatum der Eintrag „Notfall“. |
| Notfall-Eintrag (App-Leiste) | jede Seite, nur vom Start- bis zum Enddatum | Ein Tipp öffnet die Sicherheitskette. |
| Seitennavigation | Laptop | Vier Gruppen (Planen, Menschen, Versorgen, Ablage), Einstellungen unten; `aria-current` am aktiven Punkt. Eine Anzahl steht nur bei Anmeldungen (offene Anmeldungen), Kanäle (neue Nachrichten) und Schäden & Verluste (Einträge), als reiner Text ohne Kreis und Farbe – kein Zähler-Badge. |
| Untere Navigation | Handy, in einer Freizeit | Immer sichtbar, fünf feste Punkte; „Mehr“ öffnet eine eigene Seite (Inhalt siehe Navigation), kein Aufklappmenü über dem Inhalt. Nach Wechsel Fokus auf die Seitenüberschrift. |
| Notfall-Box | Startseite vom Start- bis zum Enddatum | Setzt den Notfall-Button aus FR-29 um. Steht oben unter der Begrüßung, vor allem anderen. Der Knopf „Notfallhilfe öffnen“ führt zur Sicherheitskette; „Notruf 112“ ist ein Telefon-Link. Offline weiter nutzbar. |
| Schnellsuche | Startseite, Personen, App-Leiste (nur Laptop), Sicherheitskette | Sucht Personen der Freizeit ab dem ersten Buchstaben; Treffer öffnet direkt den Freizeitpass (FR-28). Auf der Sicherheitskette steht sie ganz oben mit dem Label „Betroffene Person suchen“. |
| Sicherheitskette | Notfall | Reihenfolge: Suchfeld „Betroffene Person suchen“, was jetzt zu tun ist, wen anrufen (Telefon-Links), Weg zum Vorfall-Protokoll. Ist sie nicht eingerichtet, zeigt sie „Hinweis: Die Sicherheitskette ist noch nicht eingerichtet.“ und trotzdem Suchfeld, Leitung und „Notruf 112“. Offline lesbar. |
| Vorfall-Protokoll | Notfall, Freizeitpass | Speichert jede Angabe beim Verlassen des Feldes; jeder Eintrag ist sofort für die Leitung sichtbar, es gibt keinen Senden-Knopf. Offline beschreibbar (siehe State Patterns). |
| Balken mit Statuswort | Cockpit, Finanzen, Material, Anmeldungen | Zahl und Statuswort stehen immer als Text daneben; Balken sind für Screenreader verborgen. Regler im Beitragsrechner aktualisieren den Balken sofort (FR-57). |
| Status | Listen, Cards | Wort + Quadrat. Zuordnung: eingegangen/gebraucht = offen; läuft = aktiv; bestätigt/erledigt = Erfolg; Frist, „Stimmt das noch?“, Konflikt = Warnung; abgesagt, Überschreitung = Fehler. „Ausgebucht“ = offen (neutral grau: Hinweis, kein Problem). Material: gebraucht = offen, vorhanden und besorgt = aktiv, eingepackt = Erfolg. |
| Meldung | oben im Inhalt | Rückmeldung nach Aktionen und Warnhinweise; bleibt stehen, bis die Ursache behoben ist oder die Seite gewechselt wird. Fehler mit `role="alert"`, sonst `role="status"`. |
| Inline-Bestätigung | Versand, Löschen, Abschließen, Absage eintragen | siehe Interaction Primitives. |
| Speicher-Hinweis | Inline-Bearbeitung | Erscheint am Feld nach dem Speichern; „Rückgängig“ stellt den vorigen Wert wieder her. |
| Offline-Leiste | global | Erscheint sofort beim Verlust der Verbindung. Liegen offline geschriebene Einträge des Vorfall-Protokolls vor, zeigt sie zusätzlich „N Einträge warten auf Verbindung“ (z. B. „2 Einträge warten auf Verbindung“). Verschwindet, wenn die Verbindung zurück ist und alle Einträge übertragen sind. |
| Leer-Zustand | jede Liste | Ein Satz, was hier hinkommt, und ein Knopf für den nächsten Schritt. |
| Knopf | überall | Pfeil-Regel siehe `DESIGN.md` → Components. Deaktiviert erscheint ein Knopf nur offline in ausgegrauten Bereichen und während einer laufenden Übertragung. |
| Eingabefeld | Formulare, Inline-Bearbeitung | Label immer sichtbar; Fehler unter dem Feld mit `aria-invalid` und `aria-describedby`; bei Inline-Bearbeitung Speichern beim Verlassen des Feldes. |
| Wochenraster | Programm | Laptop: alle Tage nebeneinander; Handy: ein Tag, heute vorausgewählt, Tageswechsel über Reiter. Feste Mahlzeiten (FR-34) sind von Anfang an da und lassen sich verschieben, nicht löschen. „Ich übernehme“ direkt am Programmpunkt (FR-35). Verschieben über die Uhrzeit am Programmpunkt, danach „Alle informieren?“ (FR-62). Der Teilnehmer-Zugang zeigt dieselbe Ansicht nur mit Zeiten und Titeln. |
| Kanal-Nachrichten | Kanäle „Team“ und „Alle“ | Umschalten mit Segmenten „Team / Alle“; neueste Nachricht unten, die Seite öffnet am Ende. Im Kanal „Alle“ klappt „Senden“ zuerst die Inline-Bestätigung auf („An 60 Personen senden?“ – „Ja, senden“ / „Nein“); im Kanal „Team“ geht die Nachricht direkt raus. Push nur für Kanal-Nachrichten (NFR-10). Teilnehmende mit Teilnehmer-Zugang lesen „Alle“ ohne Eingabefeld. Ansagen-Zettel und Besprechungspunkte als eigene Reiter: Punkt anlegen, abhaken. Offline ausgegraut. |
| Schieberegler | Beitragsrechner | Mit Pfeiltasten in festen Schritten bedienbar; Wert steht als Zahl daneben; Balken aktualisiert sich sofort (FR-57). |
| Datei hochladen | Belege (FR-58), Nachweise (FR-8) | Auf dem Handy direkt Foto aufnehmen oder Datei wählen; scheitert der Upload, bleibt der Eintrag ohne Datei gespeichert und die Meldung bietet den Knopf „Erneut versuchen“. |
| Material-Status | Programmpunkt, Gesamt-Materialliste | Segment-Auswahl mit vier Optionen „gebraucht / vorhanden / besorgt / eingepackt“ (FR-36); ein Tipp setzt den Status, speichert sofort, „Rückgängig“ über den Speicher-Hinweis. In Listen als Status (Wort + Quadrat). |
| QR-Code (Teilnehmer-Zugang) | Einstellungen der Freizeit | Große Card mit dem QR-Code, Name der Freizeit und dem Link als Text; Knöpfe „Drucken“ (A4-Aushang für das Infobrett) und „Link kopieren“. Am Handy füllt die Card die Breite, zum direkten Zeigen. Erscheint nur, wenn der Teilnehmer-Zugang eingeschaltet ist (FR-64). |
| Gerichttausch | Essensplan, Einkaufsliste | Zwei Spalten „Vorher“ und „Nachher“ mit den noch nicht gekauften Posten, geänderte Mengen als Text hervorgehoben (fett, nicht nur Farbe); darunter „Übernehmen“ und „Anpassen“. Am Handy stehen die Spalten untereinander. Ohne Bestätigung ändert sich nichts (FR-50). |
| Einwilligungen | Freizeitpass je Kind | Ein Schalter je Einwilligung mit Datum der Erteilung. Erteilen geht sofort; Widerruf klappt die Inline-Bestätigung auf („Einwilligung für Fotos widerrufen?“ – „Ja, widerrufen“ / „Nein“), danach „widerrufen am TT.MM.JJJJ“ (NFR-6). |
| Kind-Card | Familienübersicht | Ein Tipp auf eine Freizeit öffnet Status und Freizeitpass des Kindes; „Freizeitpass prüfen“ führt direkt in den Pass. |
| Datentabelle | Anmeldungen, Belege, Einkaufsliste (Laptop) | Sortierbar, Zeile öffnet Detailansicht; auf dem Handy gestapelt. Bearbeiten direkt in der Zelle, wo sinnvoll (NFR-13). |
| Formular | Anmeldung, Freizeitpass, Profil | Höchstens 1–2 Pflichtfelder pro Objekt (NFR-12); Pflichtfeld mit „*“ und Legende; Fehlerübersicht oben mit Sprunglinks. |
| Schritte | Anmeldung, Freizeitpass | Fortschritt bei mehrteiligen Formularen; „Weiter“ mit Pfeil, „Zurück“ Outline, letzter Schritt „Anmeldung absenden“ ohne Pfeil. |
| Segmente | Listenfilter (z. B. „in Vorbereitung / läuft / gelaufen“, Kanal „Team / Alle“) | Schnellfilter mit 2–6 Optionen, sofort wirksam. |
| Reiter | Programm, Finanzen, Küche | Ansichten innerhalb eines Bereichs; Reiterwahl bleibt beim Zurückkehren erhalten. |
| Aufklapp | Freizeitpass-Gruppen, Details der Sicherheitskette | Nur für nachrangige Details. Nie für Allergien, Medikamente und Notfallkontakte: diese stehen immer offen ganz oben im Freizeitpass. |
| Kennzahlen | Org-Überblick | Zahl + Beschriftung, ohne Icons. |
| Avatar | Kanäle, Team | Initialen; Tipp öffnet die Person. |
| Terminliste | „Jetzt und als Nächstes“, Wochenübersicht | Zeit als Dachzeile, Titel, Ort, Status. |
| Notfallordner (Druck) | Notfall | Druckansicht im Browser; je Kind eine Seite. |

## State Patterns

| Zustand | Wo | Verhalten |
|---|---|---|
| Leer | jede Liste | Ein sachlicher Satz + ein Knopf, z. B. „Noch keine Programmpunkte.“ → „Ersten Programmpunkt anlegen“; Aufgaben: „Vorlage übernehmen“. |
| Laden | jede Seite | Text „Wird geladen …“ mit `aria-busy`; kein Spinner, keine Platzhalter-Animation. |
| Offline | global | Offline-Leiste „Offline – zuletzt aktualisiert HH:MM“ (`{components.offline-bar}`). **Offline nutzbar sind nur:** Küchen-Tagesansicht (lesen), Notfallkarte (lesen), Notfall-Box, Sicherheitskette (lesen, mit dem Weg zum Vorfall-Protokoll) und Vorfall-Protokoll (schreiben, Warteschlange). Alle anderen Bereiche sind ausgegraut mit dem Satz „Offline nicht verfügbar.“; Eingaben dort sind gesperrt. Alle lokal gespeicherten Daten sind verschlüsselt (FR-65). |
| Offline-Eintrag wartet | Vorfall-Protokoll | Einträge bleiben verschlüsselt auf dem Gerät und werden übertragen, sobald wieder Empfang da ist (Anzeige in der Offline-Leiste). Erst nach der Übertragung sieht die Leitung den Eintrag. |
| Offline vor V6 | Notfall | Bis die Notfallkarte gebaut ist (Ausbaustufe V6 der Roadmap), verweist die Sicherheitskette offline auf den ausgedruckten Notfallordner (NFR-11). |
| Fehler (Feld) | Formulare | Roter Rahmen, Text „Fehler: …“ unter dem Feld, Fehlerübersicht oben; Fokus springt zur Übersicht. |
| Fehler (Aktion) | überall | Meldung „Fehler: …“ oben im Inhalt mit dem nächsten Schritt; Eingaben bleiben erhalten. |
| Gespeichert | Inline-Bearbeitung | „Gespeichert · Rückgängig“ am Feld (`{components.autosave-hint}`). |
| Speichern gescheitert | Inline-Bearbeitung | Am Feld „Nicht gespeichert.“ mit Knopf „Erneut versuchen“; der eingegebene Wert bleibt stehen. |
| Veraltete Angabe | Freizeitpass | Angabe älter als 12 Monate: Status Warnung „Stimmt das noch?“ mit Knöpfen „Stimmt“ (bestätigt, Datum neu) und „Ändern“ (FR-23). |
| Ausgebucht | Freizeitseite, Anmeldung | „Ausgebucht“ als Status „offen“ (neutral grau: Hinweis, kein Problem); Anmelden bleibt möglich, mit Satz „eine Zusage gibt es erst, wenn ein Platz frei wird“ (FR-67). Keine Warteliste. |
| Anmeldung eingegangen / bestätigt / abgesagt | Familienübersicht, Mail | Status offen / Erfolg / Fehler; Absagen nur über angezeigte Mail und Telefon der Leitung (FR-21). |
| Freizeitpass fehlt / unvollständig | Cockpit, Personen | Warnung „Freizeitpass fehlt noch“ mit Knopf „Erinnern“ (nur Leitung). |
| Freizeitpass vor Bestätigung | Personen, Freizeitpässe (Team) | Die Leitung sieht den Pass ab Eingang der Anmeldung; das Team sieht die Person, aber statt des Passes „Freizeitpass sichtbar ab Bestätigung der Anmeldung.“ (FR-24 a, b). |
| Sicherheitskette nicht eingerichtet | Cockpit (Leitung), Sicherheitskette | Warnhinweis im Leitungs-Cockpit „Sicherheitskette ist noch nicht eingerichtet“ mit Link zu den Einstellungen der Freizeit; die Seite selbst zeigt trotzdem Suchfeld, Leitung und „Notruf 112“. |
| Neue Vorfall-Notiz | Startseite während der Freizeit (Leitung) | Neue Einträge erscheinen sofort oben in der Liste „Vorfall-Notizen“; kein Zähler, kein Push, keine „gesehen“-Markierung (die Leitungen sprechen sich selbst ab). |
| Änderung nicht mitgeteilt | Programmpunkt | Nach „Nein“ bei „Alle informieren?“ steht am Programmpunkt dezent „Geändert, noch nicht mitgeteilt“ mit Textbutton „Jetzt informieren“ und „Ausblenden“; der Hinweis bleibt, bis informiert oder ausgeblendet wurde. |
| Einwilligung widerrufen | Freizeitpass | Schalter aus, Text „widerrufen am TT.MM.JJJJ“; erneut erteilen ist möglich. |
| Nachbereitung | Startseite nach dem Enddatum | Siehe Information Architecture → „Startseite der Freizeit wechselt mit der Phase“. |
| Nachweis fehlt / läuft ab / abgelehnt | Cockpit, Anmeldungen (Leitung), Profil (Person) | Warnung mit Datum; bei Ablehnung der Hinweis der prüfenden Person am Profil. |
| Einsicht angefragt | Profil | Meldung „Hinweis: … bittet um Einsicht in Nachweise“ mit „Freigeben“. |
| Freigabefenster läuft / abgelaufen | Freizeitpass (Eltern), Freizeitpässe (Team) | Eltern sehen „Einsicht bis TT.MM.JJJJ“; nach Ablauf zeigt die Freizeit statt des Passes „Einsicht beendet am …“. |
| Allergen-Abgleich leer | Küche | Wenn die Freizeit keine Allergien abfragt: Satz „Diese Freizeit fragt keine Allergien ab.“ statt leerer Liste (FR-22). |
| Inventar-Konflikt | Programmpunkt, Material | Warnung „Zur gleichen Zeit auch für … reserviert. Absprache mit der anderen Leitung nötig.“ (FR-37). |
| Gericht getauscht | Einkaufsliste | Zwei-Spalten-Vergleich Vorher/Nachher der noch nicht gekauften Posten (siehe Component Patterns „Gerichttausch“), „Übernehmen“ oder „Anpassen“; ohne Bestätigung keine Änderung (FR-50). |
| Budget knapp / überschritten | Finanzen, Cockpit | Balken in Warnung bzw. Fehler mit Statuswort „Knapp“ / „Überschritten“ und Betrag. |
| Abgeschlossen („gelaufen“) | ganze Freizeit | Nur lesbar: Bearbeiten-Knöpfe fehlen, Hinweis oben „Diese Freizeit ist abgeschlossen und nur lesbar.“ (FR-45). |
| Modul aus | Navigation | Bereich erscheint nicht. |
| Keine Berechtigung | überall | Nicht gezeigt; direkter Link führt zur eigenen Startseite mit Hinweis „Diese Seite ist nicht verfügbar.“ |
| Login-Link verschickt / abgelaufen | Login | „Hinweis: Login-Link ist verschickt. Bitte im Postfach nachsehen.“; abgelaufen oder schon benutzt: „Dieser Link ist nicht mehr gültig.“ mit Knopf „Neuen Link schicken“. |
| Anmeldelink ungültig / Freizeit gelaufen | Anmeldung (öffentlich) | „Dieser Anmeldelink ist nicht mehr gültig.“ bzw. „Diese Freizeit ist zu Ende.“; Kontakt der Leitung bleibt sichtbar. |
| Übergabe-Link abgelaufen | Profil übernehmen | „Dieser Link ist nicht mehr gültig. Die Eltern können die Übergabe erneut auslösen.“ (FR-4). |
| Upload gescheitert | Belege, Nachweise | „Fehler: Datei nicht hochgeladen.“ mit Knopf „Erneut versuchen“; der Eintrag bleibt ohne Datei gespeichert. |
| Teilnehmer-Zugang beendet | Teilnehmer-Zugang | Seite zeigt „Diese Freizeit ist zu Ende.“ (FR-64). |
| Push nicht möglich | Teilnehmer-Zugang auf dem iPhone | Hinweis mit Schritten zum Home-Bildschirm (FR-61). |
| Push abgelehnt | Team und Teilnehmer-Zugang | Wurde die Erlaubnis im Gerät verweigert: Satz „Benachrichtigungen sind auf diesem Gerät ausgeschaltet.“ mit Schritten zum Einschalten in den Geräte-Einstellungen; die Kanäle bleiben lesbar. |
| Fokus | überall | Sichtbarer Fokusrahmen `{components.focus-ring}`; nach Ansichtswechsel Fokus auf die H1. |

## Interaction Primitives

- **Bearbeiten an Ort und Stelle** (NFR-13), sonst in einer Detailansicht als eigene Seite. **Keine Pop-ups, keine Drawer, keine Overlays.**
- **Automatisch speichern beim Verlassen des Feldes.** Danach kurz „Gespeichert“ mit „Rückgängig“ am Feld. Kein Speichern-Knopf bei Inline-Bearbeitung. Formulare mit Absenden (Anmeldung) speichern erst beim Absenden.
- **Inline-Bestätigung statt Rückfrage-Dialog.** Der auslösende Knopf klappt an derselben Stelle auf: ein kurzer Satz, „Ja, …“ und „Nein“. Fokus springt auf „Nein“; Escape oder „Nein“ klappt zurück. Pflicht bei: Versand an viele („Alle informieren?“, Nachricht im Kanal „Alle“, Erinnerung senden), Löschen, Widerruf einer Einwilligung, Absage eintragen, Freizeit abschließen, Profil übergeben.
- **Nichts geht ohne Menschen raus.** Jede Nachricht und jede Planungsänderung braucht eine Bestätigung (Leitprinzip 6). Ableitungen (Listen, Warnhinweise, Einkaufsliste) passieren ohne Nachfrage.
- **Selbst übernehmen:** „Ich übernehme“ (Programmpunkt, Aufgabe) und „Ich bringe das mit“ (Material) sind ein Tipp, ohne Bestätigung, rückgängig über denselben Knopf.
- **Abhaken** (Einkauf, Beitrag bezahlt, Besprechungspunkt, Aufgabe) ist ein Tipp; Zeltlotse vermerkt, wer abgehakt hat, wo das PRD es verlangt (FR-60).
- **Ruhige Benachrichtigungen:** Push nur für Kanal-Nachrichten (NFR-10). Keine Zähler-Badges (eine Anzahl in der Seitennavigation ist reiner Text, siehe Component Patterns), keine Erinnerungen von selbst, keine Mails außer Login-Link, Anmeldestatus, ausgelöste Erinnerungen und Übergabe.
- **Verschieben im Wochenraster:** über Bearbeiten der Uhrzeit am Programmpunkt; Ziehen mit der Maus ist kein Muss. Gibt es Ziehen, gibt es immer eine Tastatur-Alternative.
- **Gesperrt:** Pop-ups, Toasts, Spinner, Karussells, Einblend-Animationen, Benachrichtigungsflut.

## Accessibility Floor

Untergrenze ist **WCAG 2.2 AA** (Web Content Accessibility Guidelines, Stufe AA). Kontraste stehen in `DESIGN.md`.

- Ganze Bedienung per Tastatur; sichtbarer Fokus `{components.focus-ring}`, auf blaugrünen Flächen in `{colors.focus-on-color}`, im Dunkelmodus `{colors.focus-dark}`.
- Sprunglink „Zum Inhalt springen“ als erstes Element jeder Seite.
- Nach Ansichtswechsel ohne Neuladen Fokus auf die Seitenüberschrift; nach einer Inline-Bestätigung auf das Ergebnis (Meldung oder Speicher-Hinweis); nach „Nein“ zurück auf den Auslöser.
- Speicher-Hinweis und Meldungen werden angesagt (`role="status"`, Fehler `role="alert"`).
- Status nie nur über Farbe: immer ein Wort. Balken haben Zahl und Wort als Text.
- Navigation mit `aria-current`, Tabellen mit `aria-sort`, Fehler mit `aria-invalid` und `aria-describedby`.
- Zielgrößen: mindestens 24 × 24 px (WCAG 2.2, Kriterium 2.5.8); Felder und Knöpfe sind `{spacing.control-h}` hoch. Notfall-Button und Punkte der unteren Navigation mindestens `{spacing.touch-large}` (44 px) hoch, weil sie unter Stress und mit einer Hand bedient werden.
- Text bis 200 % vergrößerbar ohne Verlust; ab 320 px Breite kein seitliches Scrollen (WCAG-Kriterium 1.4.10), Prüfgerät 375 px.
- Keine zeitgesteuerten Abläufe, die Eingaben verwerfen.
- Dunkelmodus und Bewegungsreduktion folgen den Geräteeinstellungen. Im Dunkelmodus erfüllen alle Textpaare und das aufgehellte Statusrot 4,5:1, Balken und Kanten 3:1 (`DESIGN.md` → Colors → Dunkelmodus). Animationen gibt es außer Farbwechseln keine.
- Öffentliche Seiten in einfacher Sprache; Fachbegriffe (Freizeitpass, Freigabefenster) beim ersten Auftreten kurz erklärt.

## Key Flows

### NS-1: Sabine meldet ihren Sohn zum Zeltlager an

Sabine, Mutter von zwei Kindern, Handy, Link aus dem WhatsApp-Status der Gemeinde. Mock: [key-gast-anmeldung.html](mockups/key-gast-anmeldung.html).

1. Sabine tippt auf den Anmeldelink für Teilnehmende. Die Freizeitseite öffnet sich im Browser, einspaltig, ohne Installation: Beschreibung, Team mit Bild und Herkunft, Knopf „Anmelden“.
2. Das Formular fragt nur das Nötigste (NFR-12). Sabine gibt Pauls Daten als Gast ein, ohne Konto. Die Vertrauensseite ist verlinkt.
3. Sie bestätigt die Teilnahmebedingungen mit einem Haken und tippt „Anmeldung absenden“.
4. **Höhepunkt:** „Anmeldung eingegangen. Die Leitung meldet sich in Kürze.“ Darunter das Angebot „Konto aus diesen Daten erstellen?“ – ein Tipp, und das Familienkonto steht.
5. Direkt darunter: „Freizeitpass für Paul ausfüllen“ (→ NS-2).
6. Für Pauls Schwester tippt Sabine auf der Bestätigungsseite „Weiteres Kind anmelden“ (FR-18): Name und Kontakt der Eltern werden übernommen, aber nur in derselben Sitzung; auf dem Gerät wird nichts gespeichert. Ruft sie später den Anmeldelink neu auf, gibt sie die Elternangaben neu ein (oder erstellt vorher das Konto).
7. Die Leitung bestätigt. Sabine bekommt die Mail „Paul ist Teilnehmer“; in der Familienübersicht steht bei Paul „Bestätigt“.
8. Optional holt Sabine Pauls Vater in ihr Familienkonto („Zweiten Elternteil hinzufügen“ auf der Familienübersicht, FR-3).

Fehlerpfade: Freizeit ausgebucht → Status „Ausgebucht“ (neutral grau) auf der Freizeitseite, Anmelden trotzdem möglich mit Erklärsatz. Pflichtangabe fehlt → Fehlerübersicht oben, Eingaben bleiben. Absage gewünscht → die Familienübersicht zeigt Mail und Telefon der Leitung, keinen Absage-Knopf.

**Variante: Wiederanmeldung im nächsten Jahr (NFR-17, Ziel unter 3 Minuten).** Sabine hat seit dem Vorjahr ein Familienkonto. Die Familienübersicht hat keinen eigenen Anmelden-Knopf; Sabine öffnet den neuen Anmeldelink der Freizeit und loggt sich per Login-Link aus der Mail ein (FR-2). Statt der Gast-Eingabe wählt sie Paul aus ihrem Familienkonto; Pauls Daten und der Elternkontakt kommen aus dem Konto, sie bestätigt nur die Teilnahmebedingungen und tippt „Anmeldung absenden“. Den Freizeitpass findet sie vorausgefüllt, ältere Angaben tragen „Stimmt das noch?“ (FR-23, → NS-2).

### NS-2: Sabine hält Pauls Freizeitpass aktuell

Kurzform. Sabine öffnet aus der Familienübersicht Pauls Freizeitpass. Sie sieht nur die Fragen dieser Freizeit, Antworten vom letzten Jahr sind vorausgefüllt; Angaben älter als 12 Monate tragen „Stimmt das noch?“ und lassen sich mit „Stimmt“ bestätigen. Jede Änderung speichert beim Verlassen des Feldes. Oben steht „Einsicht bis TT.MM.JJJJ“; das Zugriffsprotokoll liegt auf der Seite „Konto“ (oben auf der Familienübersicht). Füllt Sabine nichts aus, sieht die Leitung im Cockpit „Freizeitpass fehlt noch“ und kann erinnern.

### NS-3: Jonas arbeitet zum ersten Mal beim Zeltlager mit

Jonas, 19, Handy, Anmeldelink für Mitarbeitende von der Leitung.

1. Jonas meldet sich über den Anmeldelink für Mitarbeitende an und legt dabei sein Konto an (Login-Link per Mail).
2. Er füllt seinen kleineren Freizeitpass für Mitarbeitende aus.
3. Zeltlotse zeigt einen kurzen Hinweis mit Schritten, wie er Zeltlotse zum Home-Bildschirm hinzufügt (NFR-8).
4. Unter „Konto“ › „Profil und Nachweise“ lädt er Führungszeugnis, JuLeiCa (Jugendleiter-Card), Erste Hilfe und den Nachweis der Schulung „Menschenskinder“ hoch, je mit Ausstellungsdatum.
5. Die Leitung bestätigt ihn. In „Meine Freizeiten“ erscheint das Zeltlager; er sieht nur Freizeiten, in denen er mitarbeitet.
6. Am Profil erscheint „Hinweis: Die Leitung bittet um Einsicht in Nachweise“. Jonas tippt „Freigeben“.
7. Die prüfende Person sieht die Dokumente einmal und bestätigt.
8. **Höhepunkt:** Bei jedem Nachweis steht „Bestätigt · gültig bis …“ – und die Organisation hat keine Kopie behalten. Für das EJW (Evangelisches Jugendwerk) gibt er dieselben Dokumente später einfach frei.

Fehlerpfade: Datum und Dokument passen nicht → Status „Abgelehnt“ mit Hinweis der prüfenden Person; Jonas korrigiert, die Einsicht wird neu angefragt. Nachweis läuft ab → Warnung bei Jonas und im Cockpit der Leitung.

### NS-4: Miriam und Tobias planen das Zeltlager

Miriam und Tobias, Leitung, Laptop, ein Dreivierteljahr vor dem Lager.

1. Die Org-Admin legt „Zeltlager 2027“ an und setzt beide als Leitung ein. Beim nächsten Login landen sie im Leitungs-Cockpit der Freizeit.
2. In den Einstellungen tragen sie Ort und Höchstzahlen ein, schalten Module an (Kalkulation und Finanzen sind optional) und wählen im Fragenbaukasten die Fragen für Teilnehmende und Mitarbeitende.
3. Sie kopieren den Anmeldelink für Mitarbeitende und schicken ihn Zugesagten persönlich (→ NS-3); später den Anmeldelink für Teilnehmende (→ NS-1).
4. Unter „Anmeldungen“ bestätigen sie Eingänge; der Balken „42 von 60 Plätzen belegt“ wächst.
5. Sie hängen das Schutzkonzept an; das Team sieht es auf seiner Startseite und bestätigt „gelesen“.
6. Unter „Aufgaben“ stehen die Vorlagen der Organisation schon drin; sie ergänzen eigene.
7. **Höhepunkt:** Das Cockpit zeigt auf einer Seite, wo die Freizeit steht: „Achtung: 3 Punkte sind offen“ mit Links zu „3 Freizeitpässe unvollständig“ und „Nachweis von Jonas noch nicht geprüft“. Ein Tipp auf „Erinnern“, Inline-Bestätigung „Erinnerung an 3 Familien senden?“ – „Ja, senden“.

Fehlerpfad: Kein Modul gewählt → die Navigation zeigt nur die Grundbereiche; nichts bricht.

### NS-5: Das Team plant das Geländespiel

Jonas mit dem Team, Laptop bei der Programmplanung.

1. Im Wochenraster stehen Frühstück, Mittag- und Abendessen schon als feste Programmpunkte.
2. Jonas legt „Geländespiel Dienstag“ an (nur Titel nötig) und tippt „Ich übernehme“.
3. Im Programmpunkt fügt er Material hinzu: Fahnen aus dem Inventar (reserviert für den Zeitraum), Seile „gebraucht“; Miriam setzt bei den Taschenlampen „Ich bringe das mit“.
4. Er legt zwei Aufgaben an; Tobias übernimmt eine selbst.
5. **Höhepunkt:** Die Gesamt-Materialliste zeigt das Geländespiel-Material mit Status – ohne dass jemand eine eigene Liste geschrieben hat.
6. Nach dem Lager legt Jonas den Programmpunkt mit „In Bibliothek ablegen“ bewusst in der Programmbibliothek ab.

Fehlerpfad: Fahnen sind zur gleichen Zeit für eine andere Freizeit reserviert → Warnung am Material mit Name der anderen Freizeit; die Leitungen klären das außerhalb von Zeltlotse.

### NS-6: Lea verletzt sich beim Geländespiel

Jonas, Handy, im Wald, Lea (11) hat sich am Knie geschnitten. Mock: [key-notfall.html](mockups/key-notfall.html).

1. Jonas tippt auf der Startseite „Notfallhilfe öffnen“ (oder oben auf „Notfall“).
2. Die Sicherheitskette zeigt ganz oben das Suchfeld „Betroffene Person suchen“, darunter was jetzt zu tun ist, Telefon-Links zur Leitung und zur Ansprechperson am Heimatort, „Notruf 112“.
3. Er tippt direkt dort „Le…“ ein und öffnet Leas Freizeitpass.
4. **Höhepunkt:** In Sekunden sieht er Allergien, Medikamente und Notfallkontakte ganz oben – keine Aufklapp-Bereiche, kein Weiterklicken.
5. Er tippt „Vorfall-Protokoll“ und hält fest, was passiert ist, welches Pflaster und welches Medikament wann gegeben wurde. Jede Angabe speichert beim Verlassen des Feldes; einen Senden-Knopf gibt es nicht.
6. Entnahmen aus dem Erste-Hilfe-Kasten landen von selbst unter „Schäden & Verluste“.
7. Jeder Eintrag ist für die Leitung sofort sichtbar: Die Vorfall-Notiz steht oben in der Liste „Vorfall-Notizen“ auf der Startseite der Leitung; die Leitung entscheidet über den Anruf bei den Eltern.

Fehlerpfade: Kein Empfang → Offline-Leiste. Die Sicherheitskette bleibt lesbar und das Vorfall-Protokoll über sie beschreibbar; die Einträge warten verschlüsselt auf dem Gerät („2 Einträge warten auf Verbindung“) und erscheinen bei der Leitung, sobald Empfang da ist. Ab V6 öffnet sich offline die Notfallkarte; bis dahin verweist die Sicherheitskette auf den ausgedruckten Notfallordner. Die Freizeit fragt keine Allergien ab → die Angabe fehlt im Pass, statt leer zu erscheinen. Sicherheitskette nicht eingerichtet → Hinweis auf der Seite, Suchfeld, Leitung und „Notruf 112“ stehen trotzdem da.

### NS-7: Uwe kocht für 60 Leute

Uwe, Küchenchef, Laptop vor der Freizeit, Handy auf der Freizeit.

1. Vor der Freizeit legt Uwe Rezepte an: Zutaten mit Menge und Einheit, Zubereitung als Freitext, Basis für 3 Personen.
2. Im Essensplan merkt er Gerichte an den Mahlzeiten vor: „Dienstag Mittag: Chili“.
3. Die Einkaufsliste entsteht von selbst, hochgerechnet auf alle Personen, mit Allergen-Abgleich und Ersatzportionen.
4. Er ergänzt Posten und überschreibt eine Menge; dann markiert er Tage und Posten für den Großeinkauf und gibt frei. Wer einkauft, hakt ab.
5. Auf der Freizeit tauscht er Mittwoch Chili gegen Linsen-Dal.
6. **Höhepunkt:** Zeltlotse zeigt offen, wie sich die noch nicht gekauften Posten ändern würden; Uwe tippt „Übernehmen“ – nichts hat sich ohne ihn geändert.
7. Morgens öffnet er die Küchen-Tagesansicht: was heute ansteht, welche Allergien und Sonderkost zu beachten sind – auch offline.
8. Bleibt viel übrig, markiert er das Abendessen als Restetag.

Fehlerpfad: Kein Netz in der Küche → Tagesansicht bleibt nutzbar, Einkaufsliste ist ausgegraut mit Hinweis.

### NS-8: Miriam kalkuliert, das Team sammelt Belege

Miriam, Laptop; Uwe und das Team, Handy.

1. Miriam öffnet „Finanzen“ › „Kalkulation“ und trägt Fixkosten und Kosten pro Person und Tag in sechs Kategorien ein.
2. Mit Uwe setzt sie den Verpflegungs-Tagessatz auf 7 €; das Budget errechnet sich.
3. Sie trägt Zuschüsse als Einnahme ein.
4. **Höhepunkt:** Im Beitragsrechner schiebt sie einen Regler und sieht den Teilnehmerbeitrag als Balken mitwandern.
5. Während der Freizeit fotografiert Uwe einen Beleg auf dem Handy, wählt die Kategorie und setzt „bezahlt von Uwe“ und „nicht erstattet“.
6. Teammitglieder haken „Beitrag bezahlt“ ab; Zeltlotse vermerkt, wer abgehakt hat.
7. Im Soll-Ist-Vergleich sehen alle Kategorien als Balken mit Statuswort („Im Plan“, „Knapp“, „Überschritten“); Uwe prüft dort die Küche.
8. Die Erstattungsübersicht zeigt pro Person, wer noch Geld bekommt.

Fehlerpfad: Belegfoto lässt sich nicht hochladen → Beleg bleibt als Eintrag von Hand gespeichert, Meldung „Fehler: Datei nicht hochgeladen.“ mit Knopf „Erneut versuchen“.

### NS-9: Das Mittagessen verschiebt sich

Jonas, Handy, Dienstag, das Geländespiel dauert länger.

1. Jonas öffnet „Programm“, tippt auf „Mittagessen“ und ändert die Uhrzeit von 13:00 auf 13:30. Die Änderung speichert beim Verlassen des Feldes.
2. Am Programmpunkt klappt die Inline-Bestätigung auf: „Alle informieren?“ mit vorbereitetem Text „Essen 13:30 statt 13:00“.
3. **Höhepunkt:** Jonas tippt „Ja, informieren“. Die Nachricht steht im Kanal „Alle“ und kommt als Push aufs Handy – an Team und Teilnehmende mit Teilnehmer-Zugang.
4. Absprachen nur fürs Team schreibt er im Kanal „Team“; eine selbst geschriebene Nachricht in „Alle“ bestätigt er vorher inline („An 60 Personen senden?“).
5. Für das Abendessen notiert Miriam einen Punkt auf dem Ansagen-Zettel; Punkte für die Mitarbeiterbesprechung werden dort am Abend abgehakt.

Fehlerpfad: Jonas tippt „Nein“ oder verlässt die Seite ohne Antwort → die Uhrzeit ist geändert, verschickt wird nichts (FR-62). Am Programmpunkt bleibt dezent „Geändert, noch nicht mitgeteilt“ mit „Jetzt informieren“, bis jemand informiert oder den Hinweis ausblendet.

### NS-10: Paul schaut, wann es Essen gibt

Paul, 12, eigenes Handy, erster Abend im Zeltlager.

1. Paul scannt den QR-Code am Infobrett. Der Teilnehmer-Zugang öffnet sich im Browser, ohne Konto.
2. Er sieht die Wochenübersicht: Zeiten und Titel der Programmpunkte und Mahlzeiten, keine Details.
3. Unter „Infos“ findet er die Hausordnung.
4. Im Kanal „Alle“ liest er mit; ein Knopf bietet Push-Nachrichten an. Auf dem iPhone erklärt ein Hinweis, dass das erst nach Hinzufügen zum Home-Bildschirm geht.
5. **Höhepunkt:** Am Dienstag kommt „Essen 13:30 statt 13:00“ als Push (→ NS-9) – Paul weiß Bescheid, ohne dass ihm jemand ein Konto eingerichtet hat.
6. Nach der Freizeit zeigt der Link nur noch „Diese Freizeit ist zu Ende.“

Fehlerpfad: Teilnehmer-Zugang ist nicht eingeschaltet → der QR-Code wird gar nicht erst erzeugt.

### NS-11: Paul wird 18

Kurzform. Ab Pauls 18. Geburtstag zeigt die Familienübersicht bei Paul den Knopf „Profil an Paul übergeben“. Sabine tippt, gibt Pauls Mail-Adresse ein und bestätigt inline. Paul öffnet den Link aus der Mail und übernimmt Profil, Freizeitpass und Verlauf; bei Sabine verschwindet die Card. Übergibt sie nicht, legt Paul bei der nächsten Anmeldung ein eigenes Konto an.

### NS-12: Miriam und Tobias schließen das Zeltlager ab

Kurzform. Ab dem Tag nach dem Enddatum beginnt die Nachbereitung: Notfall-Box und Notfall-Eintrag verschwinden, die Startseite zeigt Schäden und Verluste (während der ganzen Freizeit gefüllt), Belege und offene Punkte. Beim Abschluss werden die Schäden und Verluste zu Anschaffungswünschen. Miriam tippt „Freizeit abschließen“; sind noch Aufgaben offen oder Belege nicht erstattet, nennt die Inline-Bestätigung das („3 Belege nicht erstattet – trotzdem abschließen?“ – „Ja, abschließen“ / „Nein“). Abschließen bleibt erlaubt; erst jetzt wechselt der Status auf „gelaufen“ (FR-45), alles ist nur noch lesbar. Vorfall-Notizen wandern ins Archiv der Organisation; nach Ablauf des Freigabefensters verschwinden die Freizeitpässe aus der Freizeit.

### NS-13: Das EJW-Leitungsgremium nach dem Sommer

Kurzform. Gerd (Org-Überblick) loggt sich ein und landet im Org-Überblick. Er filtert die Freizeiten mit den Segmenten auf „gelaufen“, sieht die gesammelten Anschaffungswünsche und das Inventar. Teilnehmende, Freizeitpässe oder Finanzen erscheinen nicht – die Zeilen der Freizeiten lassen sich nicht öffnen, außer er arbeitet dort selbst mit.

## Responsive & Platform

- **Breakpoints:** unter 1024 px Handy-Aufbau (untere Navigation, einspaltig), ab 1024 px Laptop-Aufbau (Seitennavigation, breites Raster). Untergrenze 320 px, geprüft bei 375 px und 1440 px; Laptop-Ansichten bei 1280–1440 px ohne seitliches Scrollen.
- **PWA installieren:** Das Hinzufügen zum Home-Bildschirm ist Teil des Onboardings für Mitarbeitende (NFR-8, NS-3 Schritt 3), als kurzer Hinweis mit Schritten je Gerät, nicht als Pop-up. Öffentliche Seiten fordern keine Installation.
- **Push:** Für das Team verlässlich. Auf dem iPhone kommen Push-Nachrichten erst nach Hinzufügen zum Home-Bildschirm (FR-61). Die Erlaubnis für Push fragt Zeltlotse erst, wenn jemand auf „Benachrichtigungen einschalten“ tippt, nie beim ersten Seitenaufruf.
- **Offline:** Umfang siehe State Patterns → „Offline“. Zusätzlich: Lokal gespeicherte Daten werden nach Freizeitende gelöscht (FR-65); die Schrift Sarabun ist lokal eingebunden.
- **Druck Notfallordner:** ELKW-Druckbausteine, A4, schwarz-weiß-tauglich, **je Kind eine Seite** (FR-33). Bis zur Notfallkarte ist er die Rückfallebene bei fehlendem Netz (NFR-11). Weitere Druckansichten kommen später (PRD „Später“).
- **Öffentliche Seiten** sind fürs Handy gebaut, funktionieren aber genauso am Laptop.

## Inspiration & Anti-patterns

- **Abgelehnt – ChurchTools (Negativbeispiel aus dem Brainstorming):** ein Verwaltungsmoloch mit vielen Pop-ups beim Bearbeiten, zig Gruppen und Unterkalendern, schlechtem Überblick, schwerfälliger Konfiguration und Daten bei der Organisation statt bei der Person. Zeltlotse setzt dagegen: Bearbeiten an Ort und Stelle ohne Pop-ups, feste Struktur Organisation › Freizeit › Bereiche statt frei baubarer Gruppen, wenige klare Rollen statt Rechte-Matrix, Startseite, die nur das Fehlende zeigt, und Daten, die der Person gehören.
- **Übernommen – ELKW-Bibliothek, Stil „nahbar“:** ruhige, warme Anwendung für Ehrenamt statt Verwaltungsoptik; Status als Wort, Rückmeldung im Inhalt statt Toasts.
- **Abgelehnt – Benachrichtigungsflut:** Zähler, Erinnerungen von selbst, Mails zu jeder Änderung. Zeltlotse ist still, außer in den Kanälen.
- **Abgelehnt – Kanban-Board, Pop-up-Bearbeitung, feingranulare Rechte** (PRD „Bewusst nie“).

## Offene Fragen

Keine blockierenden Fragen. Hinweis für die Umsetzung (kein Blocker): **Dunkelmodus auf einem echten Handy prüfen** – die `*-dark`-Werte sind gerechnet und im Mock [key-dunkelmodus.html](mockups/key-dunkelmodus.html) gerendert, ein Blick bei Sonne und im Dunkeln steht noch aus.
