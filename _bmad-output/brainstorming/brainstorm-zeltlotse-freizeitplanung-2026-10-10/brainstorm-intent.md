---
title: Zeltlotse – Intent aus dem Brainstorming
date: 2026-10-10
source: _bmad-output/brainstorming/brainstorm-zeltlotse-freizeitplanung-2026-10-10/.memlog.md
details: roadmap.md (gleicher Ordner)
status: final – Eingabe für das PRD (Produktanforderungsdokument)
---

# Zeltlotse – Intent

## Produktidee

Zeltlotse ist ein schlichtes Planungswerkzeug für Freizeiten kirchlicher Jugendverbände (CVJM = Christlicher Verein Junger Menschen, EJW = Evangelisches Jugendwerk): Zeltlager, Junge-Erwachsene-Freizeiten, Teens-Freizeiten im Haus, Wanderfreizeiten. Es digitalisiert nur dort, wo heute durch unsaubere Informationsflüsse Chaos entsteht; funktionierende analoge Abläufe bleiben analog. Mehrmandantenfähig (viele Organisationen, je viele Freizeiten), lokal testbar, später als Azure-Dienst in einer EU-Region betrieben.

## Leitprinzipien

**Drei Fundamente** (Maßstab für jede Entscheidung):
1. **Ein Mensch, ein Profil, viele Rollen** – dieselbe Person kann Teilnehmende, Mitarbeitende, Elternteil sein; ein Konto, ein Profil.
2. **Der Programmpunkt ist der Knotenpunkt** – aus Programmpunkten ergeben sich Material, Aufgaben und Dokumente; keine losgelösten Funktionen.
3. **Das Gegenteil eines Verwaltungsmolochs** – Werkzeug ist Mittel zum Zweck (Negativbeispiel: ChurchTools mit Pop-ups, zig Gruppen/Unterkalendern, schlechtem Überblick).

**Leitplanken** (gelten in jeder Stufe):
- Wenige Pflichtfelder: max. 1–2 pro Objekt (z. B. Aufgabe = nur Titel), Rest optional und eingeklappt.
- Inline-Editing: direkt an Ort und Stelle bearbeiten, keine Dialogfenster für normale Änderungen.
- Feste Struktur Organisation > Freizeit > Bereiche statt frei baubarer Gruppen; ein Ablauf pro Freizeit, Filter statt Unterkalender. Meinung statt Optionen.
- Jede wichtige Information max. 2 Klicks von der Freizeit-Startseite entfernt.
- Still by default: nur „betrifft mich“-Hinweise, optional Tageszusammenfassung; keine Benachrichtigungsflut.
- Onboarding per Einladungslink, nach 2 Minuten arbeitsfähig, ohne Anleitung.
- Jeder pflegt seinen Bereich (Küche ihre Rezepte, Programmverantwortliche ihren Programmpunkt), nicht die Leitung alles.
- Module pro Freizeit nur an/aus (z. B. Zahlung, Verpflegung, Teilnehmer-Zugang), keine tiefere Konfiguration.
- Datensparsamkeit: nur abfragen, was die Freizeit wirklich braucht.
- Vertrauen, wenig Hierarchie: Team darf fast alles sehen und tun; Leitung hat nur wenige exklusive Bereiche.
- Alles exportierbar/druckbar (PDF/CSV).
- Gute, grafische UX (besonders Finanzen): Nutzende sind keine BWL-Menschen; Team ist technisch versiert.

## Querschnitts-Regeln (Synthese)

1. **Daten gehören dem Menschen, Organisationen leihen nur.** Freizeitpass liegt im Familienkonto, Nachweise beim Mitarbeitenden-Profil; Organisationen erhalten zeitlich begrenzte, freigegebene Einsicht mit Zugriffsprotokoll; mit 18 wird die Person Eigentümerin ihres Profils. Zugleich Antwort auf die Konto-Hürde (Vertrauen) und Abgrenzung zu org-zentrierten Systemen.
2. **Einmal erfassen, alles ableiten.** Listen werden nie von Hand gepflegt: Material aus Programmpunkt, Einkaufsliste aus Essensplan, Sonderkost aus Freizeitpass, Verpflegungskosten aus Essensplan, Cockpit-Ampeln aus fehlenden Daten, Anschaffungsliste aus Schadensliste. Architekturregel.
3. **Wiederverwendung über Bausteine, nie durch Kopieren ganzer Freizeiten.** Fragenbaukasten, Aufgabe → Vorlage, eingebundene Org-Dokumente, Rezepte, Programmbibliothek, Familienkonto.
4. **Papier ist Rückfallebene, kein Versagen.** Notfallordner, Küchenwand; Ausdruck ersetzt den Offline-Modus bis V6. Druck/Export ist Kernfunktion.
5. **Ein Mechanismus „Angabe mit Stand“** (Datum, Herkunft, Ablauf) für Freizeitpass-Angaben, Nachweise und Veraltet-Markierung – einmal in V1 bauen, V2 profitiert.

## Rollen & Personas

- **Org-Admin** (1–2 pro Organisation): verwaltet Organisation, Inventar, Vorlagen, Org-Dokumente.
- **Org-Überblick** (z. B. Hauptamt): liest alle Freizeiten; Organisationsübersicht mit Warnhinweisen.
- **Freizeitleitung** (Rolle innerhalb einer Freizeit): Leitungs-Cockpit mit Ampeln (Anmeldungen vs. Plätze, fehlende Unterlagen, unbesetzte Aufgaben – offene Zahlungen nicht im Fokus).
- **Team/Mitarbeitende**: sehen nur zugewiesene Freizeiten; während der Freizeit Schnellsuche (Vorname/Spitzname/Foto/Zeltnummer) auf den Freizeitpass, Notfall-Button, Aufgaben „Ich übernehme“, Material „Ich bringe das mit“. Eigenes Profil mit Nachweisen (erweitertes Führungszeugnis, JuLeiCa = Jugendleiter-Card, Erste Hilfe, Präventionsschulung „Menschenskinder“), Ablauf-Erinnerung, organisationsübergreifende Freigabe.
- **Küche**: braucht Informationsübersicht, keine Prozesssteuerung (Tagesansicht, Sonderkost, Portionszahlen).
- **Eltern**: Familienkonto mit Kindern, Sorgeberechtigung pro Kind, getrennte Haushalte, Notfallkontakte; pflegen Freizeitpass; sehen Status und Zugriffsprotokoll.
- **Teilnehmende**: Zugang pro Freizeit optional (eher ältere Gruppen); Gastzugang per QR-Code/Code für die Freizeitdauer ohne Konto; Kind kann ab einstellbarem Alter eigenen Login im Familienkonto bekommen; Volljährige melden sich selbst an.

## Roadmap (Kurzform – Details in `roadmap.md`)

Aufeinander aufbauende Stufen; jede Stufe wird später eigenständig (z. B. von Subagenten) umgesetzt. Vorlage je Stufe: Ziel (welches Chaos verschwindet), Baut auf, Inhalt, Bewusst nicht drin, Fertig wenn (Praxistest), Leitprinzipien-Check.

- **V1 Menschen & Organisationen** – Profil („Ein Mensch, ein Profil“), Familienkonto, Organisationen/Mandanten, Rollen, Nachweise, Mechanismus „Angabe mit Stand“, Datenschutz-Kern.
- **V2 Anmeldung & Freizeitpass** – erste echt nutzbare Stufe: Landingpage, teilbarer Anmeldelink, Gast-Anmeldung → Konto mit einem Klick, Magic-Link (Login-Link per Mail), Wiederanmeldung < 3 Min, Warteliste, Status für Eltern; Freizeitpass mit Fragenbaukasten und Freigabefenster; Leitungs-Cockpit; Notfallkarte, Sicherheitskette, Schutzkonzept, Vorfall-Notiz.
- **V3 Programmpunkt als Knotenpunkt** – Wochenraster/Ablauf, Programmbibliothek, Ideenspeicher, Tagesleitung; daraus Aufgaben (Phasen Vorbereitung/Während/Nachbereitung, Org-Vorlagen) und Material (Org-Inventar mit Konflikthinweis bei Parallel-Freizeiten, Schadens-/Verlustliste → Anschaffungswünsche); Dokumente am Kontext mit drei Sichtbarkeiten, eine Suche, Abschlussmappe.
- **V4 Verpflegung** – Essensplan, Rezepte (Markdown) mit Skalierung, Mengenfaktoren, Einkaufsliste mit Sonderkost-Ersatz, Küchen-Tagesansicht, Sonderkost-Karte, Restetag, Mitesser/Abwesende.
- **V5 Finanzen** – Vorabkalkulation (wenige Kategorien, fix/pro Person, Mitarbeitenden-Umlage, freie Einnahmeposten), Beitragsrechner als Balken + Schieberegler, Verpflegungskosten aus Essensplan, Belege, Erstattungen, Soll-Ist-Balken, Abrechnungs-PDF.
- **V6 Gemeinsam unterwegs** – Kurznachrichten in zwei Kanälen (Team/Alle), Verschieben eines Programmpunkts löst Nachricht aus, Ansagen-Sammlung, Notizen für die Mitarbeiterbesprechung, Teilnehmer-Zugang, verschlüsselter Offline-Modus.
- **Ausbau-Pool (ungeordnet)** – u. a. Einkaufs-Workflow, Lunchpakete, Essensform/Buffet-Schilder, Restevermerk, Open-Food-Anbindung, Kosten-Donut, Szenarienvergleich, Vorjahreskalkulation als Startwert, Teilnehmerzahlungen (pro Freizeit abschaltbar, Status offen/bezahlt/erlassen manuell), Zuschuss-Exportlisten, Volltextsuche/Texterkennung, Jahresrückblick.

## Bewusst nie

- Ganze Freizeit kopieren.
- Resteregister mit Erfassung übriger Gerichte und automatischem Reste-Tag-Vorschlag.
- Einzelprotokoll von Kisteninhalten (Kiste höchstens als ein Gegenstand).
- Formaler Rückgabe-Check nach der Freizeit.
- Live-Kalkulation über Anmeldungen (stattdessen Vorabkalkulation).
- Personenbezogene Massen-Aufgaben und persönliche Vorab-/Nachher-Checklisten.
- Feste Aufgaben-Startlisten pro Freizeittyp (stattdessen Org-Vorlagen).
- Tagesleitungs-Übergabe in der App (läuft über Mitarbeiterbesprechung).
- Mitarbeiterpool/Mitarbeitenden-Anfragen über die Plattform.
- Feingranulare Rechte-Matrix, frei baubare Gruppen, Unterkalender.
- Offline-Anmeldungen (Anmeldung läuft vollständig digital).
- Rezepte generieren (werden selbst eingegeben).

Nicht „nie“, sondern aktuell außerhalb des Umfangs: Anbindung eines Zahlungssystems; Unterorganisationen (Bezirk/Ortsverein).

## Datenschutz-Rahmen

- Strikte Mandantentrennung; Organisationen sehen nur, was Personen freigeben.
- Gesundheitsdaten (Allergien, Medikamente) besonders geschützt: nur im Freigabefenster (ab Anmeldung bis 1–2 Wochen nach Freizeitende) sichtbar, Zugriffe protokolliert.
- Notfallkontakte für alle Mitarbeitenden der Freizeit sichtbar (wie heutiger Notfallordner).
- Automatische Löschfristen; granulare, widerrufbare Einwilligungen (Fotoerlaubnis, Weitergabe an Zuschussgeber, Teilnehmer-Zugang).
- Datenauskunft per Klick und Kontolöschung.
- Offline-Daten auf Geräten verschlüsselt, nach Freizeitende gelöscht.
- Hosting nur in EU-Region (Azure Germany West Central / West Europe).
- Vertrauensseite in Klartext: wer sieht was, wie lange, wann wird gelöscht.

## Offene Punkte vor dem PRD

- Rechtsrahmen je Organisation: DSG-EKD (Datenschutzgesetz der Evangelischen Kirche in Deutschland) vs. DSGVO (Datenschutz-Grundverordnung), v. a. für CVJM-Vereine je nach Zuordnung.
- AVV (Auftragsverarbeitungsvertrag) als Vorlage für Organisationen bei Betrieb als Dienst – Inhalt/Verantwortung klären.
- Konkrete Löschfristen (gesetzliche Aufbewahrung für Teilnehmerlisten/Belege, „Rest nach X Jahren“).
- Welche wenigen Bereiche exklusiv der Freizeitleitung vorbehalten sind.
- Mindestalter für eigenen Login von Kindern im Familienkonto (Beispiel: 14) und Ablauf des Übergangs mit 18.
- Schwelle der Veraltet-Markierung (Beispiel: 12 Monate) und Erinnerungsvorlauf bei Nachweisen (Beispiel: 2 Monate).

Bereits gelöst (nicht erneut öffnen): Teilnehmer-Login/Übergang 18 (über „Ein Mensch, ein Profil“ + Gastzugang), Abbildung von Teilnehmerzahlungen (vereinfacht, Ausbau-Pool), Vorjahr als Vorlage (nur Bausteine), Abgrenzung Org- vs. Freizeit-Dokumente (Org = mehrere/alle Freizeiten, eingebunden statt kopiert; eine Suche mit standortabhängigem Umfang).
