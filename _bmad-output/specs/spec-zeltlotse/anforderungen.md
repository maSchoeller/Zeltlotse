# Anforderungen – Zeltlotse MVP

Detailregeln zu den Capabilities in `SPEC.md`. IDs stammen aus dem PRD und sind dauerhaft: nie neu vergeben, gestrichene IDs bleiben als Platzhalter. Verfeinerungen aus UX- und Architektur-Lauf sind mit *(UX)* bzw. *(Arch)* markiert.

## Leitprinzipien

1. **Ein Mensch, ein Profil, viele Rollen.** Daten gehören der Person; Organisationen bekommen befristete, protokollierte Einsicht.
2. **Der Programmpunkt ist der Knotenpunkt.** Material, Aufgaben und Mahlzeiten hängen am Wochenraster.
3. **Einmal erfassen, alles ableiten.** Eine Angabe wird nur einmal eingegeben; Listen, Hinweise und Übersichten ergeben sich daraus.
4. **Analog bleibt analog.** Zeltlotse erzeugt nicht mehr Planungsaufwand als nötig.
5. **Simpel halten.** Für Sonderfälle die einfachste Lösung, keine Speziallogik.
6. **Kein Versand und keine Planungsänderung ohne Menschen.** Ableitungen wie Listen und Hinweise passieren automatisch.
7. **Jeder pflegt seinen Bereich.** Keine zentrale Datenpflege.
8. **Vertrauen statt Rechte-Matrix.** Das Team sieht und darf fast alles; wenige Dinge sind der Leitung vorbehalten.

## V1 – Menschen und Organisationen

- **FR-1** Jede Person hat genau ein Profil, das ihr gehört, auch bei mehreren Rollen in mehreren Organisationen.
- **FR-2** Anmeldung am Konto standardmäßig per Login-Link aus einer Mail; optional zusätzlich Passwort.
- **FR-3** Ein Elternteil verwaltet im Familienkonto die Profile seiner Kinder unter 18 und kann einen weiteren Elternteil (gleiche Rechte *(Arch)*) hinzufügen. Patchwork ist nicht abgebildet.
- **FR-4** Ab dem 18. Geburtstag übergeben die Eltern das Profil per Knopf an die Mail-Adresse des Kindes; danach kein Zugriff mehr. Ohne Übergabe legt das Kind bei der nächsten Anmeldung ein eigenes Konto an und füllt neu aus.
- **FR-5** Global Admin legt Organisationen an und lädt per Link den ersten Org-Admin ein. Org-Admins ernennen weitere Org-Admins und Org-Überblick.
- **FR-6** Org-Admins legen Freizeiten an und setzen eine oder mehrere Personen als Leitung mit gleichen Rechten ein.
- **FR-7** Mitarbeitende sehen nur Freizeiten, in denen sie mitarbeiten. Mit der ersten bestätigten Mitarbeit werden sie automatisch Mitarbeitende der Organisation; bei weiteren Freizeiten entfällt „Organisation beitreten“, die Leitung bestätigt trotzdem.
- **FR-8** Personen laden Nachweise am Profil hoch und tragen das Ausstellungsdatum ein; das Hochladedatum speichert Zeltlotse.
- **FR-9** Jede Organisation legt per Richtlinie fest, welche Nachweise Pflicht sind und wie lange sie gelten; Zeltlotse berechnet das Ablaufdatum.
- **FR-10** Einsicht in Nachweise ist einmalig: automatisch angefordert bei Mitarbeit an einer Freizeit oder später von Organisation oder Leitung angefragt. Die Person gibt frei; die prüfende Person (wer angefordert hat) bestätigt oder lehnt mit Hinweis ab. Passen Datum und Dokument nicht, korrigiert die Person, und die Einsicht wird neu angefordert. Gespeichert wird nur die Bestätigung (Nachweis, eingesehen am, gültig bis, von wem), keine Kopie. Danach endet der Zugriff.
- **FR-11** Die Leitung sieht fehlende und ablaufende Nachweise ihres Teams und schickt per Knopfdruck eine Erinnerung.
- **FR-12** Dieselben Nachweise lassen sich mehreren Organisationen zur Einsicht freigeben, ohne erneuten Upload.
- **FR-13** Personen sehen im Zugriffsprotokoll, wer wann Einsicht in ihre Daten hatte.
- **FR-14** Jede Person kann per Klick Auskunft über ihre Daten erhalten, ihr Konto löschen und ihre Daten als PDF oder CSV exportieren.

## V2 – Anmeldung und Freizeitpass

### Anmeldung

- **FR-15** Jede Freizeit hat eine öffentliche Freizeitseite mit Beschreibung und Team (Bild, Herkunft).
- **FR-16** Jede Freizeit hat zwei teilbare Anmeldelinks (Teilnehmende, Mitarbeitende), beide ohne Installation. Mitarbeitende können jederzeit dazukommen, auch wenn die Freizeit voll ist.
- **FR-67** Ist die Höchstzahl an Teilnehmenden erreicht, zeigt die Freizeitseite „ausgebucht“ (neutral, kein Fehler *(UX)*). Anmelden bleibt möglich, ohne Aussicht auf Zusage, bis die Leitung jemanden bestätigt.
- **FR-17** Anmeldung als Gast ohne Konto; danach Angebot, mit einem Klick ein Konto aus den Daten zu erstellen.
- **FR-18** Unter 18 meldet ein Elternteil an, Volljährige melden sich selbst an. Geschwister nacheinander über denselben Link; „Weiteres Kind anmelden“ übernimmt Elternangaben nur in derselben Sitzung *(UX)*.
- **FR-19** Beim Absenden werden die Teilnahmebedingungen digital bestätigt.
- **FR-20** Nach dem Absenden: „Anmeldung eingegangen“. Die Leitung bestätigt oder sagt ab; die Person erhält Bestätigungs- oder Absage-Mail und sieht den Status in Zeltlotse. Keine Warteliste.
- **FR-21** Absagen nicht in Zeltlotse; angezeigt werden Mail und Telefon der Leitung. Die Leitung trägt Absagen ein und bestätigt selbst, wer nachrückt. Eine Absage beendet die Freigabe des Freizeitpasses sofort *(Arch)*.
- **FR-66** Vertrauensseite je Freizeit: in Klartext, welche Daten erhoben werden, wer sie sieht, wann sie verschwinden; erzeugt aus den Einstellungen von Freizeit und Organisation.
- **FR-68** Elterninfo: Infobrief, Packliste, Notfallnummern (Sichtbarkeit „Eltern und Teilnehmende“) im Elternkonto; Gast-Anmeldende per Link in der Mail.

### Freizeitpass

- **FR-22** Die Leitung wählt die Fragen frei aus dem Fragenbaukasten, getrennt für Teilnehmende und Mitarbeitende. Kein Pflichtkern. Fester globaler Katalog mit stabilen Kennungen, keine eigenen Fragen in der MVP; Allergie-Frage = 14 EU-Hauptallergene + Freitext *(Arch)*.
- **FR-23** Antworten gehören der Person und werden bei der nächsten Freizeit vorausgefüllt; Angaben älter als 12 Monate tragen „Stimmt das noch?“.
- **FR-24** Sichtbarkeit:
  - a) Leitung ab Eingang der Anmeldung.
  - b) Ganzes Team: vollständige Freizeitpässe ab Bestätigung, nur bis Ende des Freigabefensters.
  - c) Freigabefenster: festes Intervall der Organisation ab Ende des letzten Freizeittages.
  - d) Beim Anlegen einer Freizeit wird das Intervall fixiert, danach nicht änderbar.
  - e) Eltern sehen, bis wann die Freizeit Einsicht hat.
  - Die Freizeit sieht nur Antworten auf die von ihr abgefragten Fragen *(Arch)*.
- **FR-25** Die Leitung sieht unvollständige Freizeitpässe und löst Erinnerungen per Knopfdruck aus.
- **FR-26** Team-Notizen zu Teilnehmenden bleiben in der Freizeit und fließen nicht in den Freizeitpass; Rückmeldungen an Eltern laufen im persönlichen Gespräch.

### Leitung und Sicherheit

- **FR-27** Leitungs-Cockpit: Anmeldungen (eingegangen, bestätigt) im Verhältnis zu den Plätzen; Warnhinweise zu fehlenden Freizeitpässen, fehlenden oder abgelaufenen Nachweisen, unbesetzten Aufgaben und fehlender Sicherheitskette *(UX)*. Das Team sieht in der Vorbereitung eine Startseite ohne Leitungs-Hinweise *(UX)*.
- **FR-28** Schnellsuche nach Personen der Freizeit; Freizeitpass in Sekunden geöffnet.
- **FR-29** Notfall-Button → Sicherheitskette: was jetzt zu tun ist und wen man anruft (Leitung, Ansprechperson am Heimatort). Die Leitung richtet sie in den Freizeit-Einstellungen ein *(UX)*.
- **FR-30** Die Leitung hängt das Schutzkonzept an; jedes Teammitglied bestätigt „gelesen“.
- **FR-31** Vorfall-Protokoll: was passiert ist, welches Medikament wann gegeben, was aus dem Erste-Hilfe-Kasten entnommen wurde. Einträge sind unveränderlich, Korrektur = neuer Eintrag *(Arch)*, für die Leitung sofort sichtbar *(UX)*. Daraus entsteht eine Vorfall-Notiz an die Leitung; sie bleibt während der Freizeit dort, wird nach Abschluss in der Organisation archiviert und nach der Org-Löschfrist gelöscht (NFR-5).
- **FR-32** Entnahmen aus dem Erste-Hilfe-Kasten landen auf der Schadens- und Verlustliste.
- **FR-33** Notfallordner (Notfallkontakte, relevante Freizeitpass-Angaben) ausdruckbar.

## V3 – Programmpunkt als Knotenpunkt

- **FR-34** Jede Freizeit hat einen Wochenraster; Frühstück, Mittag- und Abendessen sind feste Programmpunkte.
- **FR-35** Das Team plant gemeinsam; Mitarbeitende übernehmen Programmpunkte selbst. Die Leitung teilt nichts zu.
- **FR-36** Material am Programmpunkt mit den Zuständen gebraucht, vorhanden, besorgt, eingepackt; „Ich bringe das mit“.
- **FR-69** Gesamt-Materialliste über alle Programmpunkte mit Mengen und Status.
- **FR-37** Inventar der Organisation; Reservierung für einen Zeitraum; Konflikthinweis bei Überschneidung paralleler Freizeiten; Lösung außerhalb von Zeltlotse.
- **FR-38** Schäden und Verluste werden erfasst (während der ganzen Freizeit erreichbar *(UX)*) und zu Anschaffungswünschen; sichtbar für Org-Admins, Leitungen und Org-Überblick.
- **FR-39** Aufgaben brauchen nur einen Titel; Phasen Vorbereitung, Während, Nachbereitung; selbst übernehmbar; erscheinen unter „Meine Aufgaben“.
- **FR-40** Aufgaben-Vorlagen der Organisation; neue Freizeiten übernehmen sie; Aufgaben lassen sich in eine Vorlage übernehmen.
- **FR-41** Programmbibliothek: bewusstes Ablegen, nie automatisch; Übernahme in eine Freizeit samt Material und Aufgaben.
- **FR-42** *(gestrichen, aufgegangen in FR-41)*
- **FR-43** Dokumente:
  - a) am Kontext (Freizeit, Programmpunkt) mit drei Sichtbarkeiten: Team; Eltern und Teilnehmende (inkl. Teilnehmer-Zugang), z. B. Infobrief, Packliste; öffentlich auf der Freizeitseite.
  - b) Org-Dokumente werden eingebunden statt kopiert.
  - c) Eine Suche läuft über alle Dokumente.
- **FR-44** Module pro Freizeit an- oder abschaltbar (Verpflegung, Finanzen, Teilnehmer-Zugang); Kalkulation und Finanzen sind optional.
- **FR-45** Phasen: „in Vorbereitung“ vor Start, „läuft“ ab Startdatum automatisch, „Nachbereitung“ ab dem Tag nach dem Enddatum *(UX/Arch)*, „gelaufen“ erst nach ausdrücklichem Abschluss durch die Leitung. Gelaufen = nur lesbar (Ausnahmen: Erstattungsvermerke, Anschaffungswünsche *(Arch)*), einsehbar, per Filter ausblendbar. Kosten nach Abschluss werden außerhalb geregelt.

## V4 – Verpflegung (Modul)

- **FR-46** Rezepte mit Basis-Personenzahl: Zutaten (Menge, Einheit, Zutat, Allergene), Zubereitung als Freitext; Skalierung auf die Personenzahl der Freizeit, jede Person zählt gleich.
- **FR-47** Gerichte werden an Mahlzeiten-Programmpunkten vorgemerkt (Plan, keine Zusage) und jederzeit getauscht.
- **FR-48** Einkaufsliste aus vorgemerkten Gerichten, mit Allergen-Abgleich gegen alle Freizeitpässe (Teilnehmende und Mitarbeitende) und Ersatzportionen.
- **FR-49** Die Küche ergänzt Posten und überschreibt Mengen von Hand.
- **FR-50** Beim Gerichttausch zeigt Zeltlotse die Änderung der noch nicht gekauften Posten (Vorher/Nachher *(UX)*); die Küche bestätigt oder passt an; ohne Bestätigung ändert sich nichts.
- **FR-51** Die Küche gibt frei, für welche Tage und Posten eingekauft wird; wer einkauft, hakt ab.
- **FR-52** Küchen-Tagesansicht: was heute ansteht, welche Allergien und Sonderkost.
- **FR-53** Mahlzeit spontan als Restetag markierbar.

## V5 – Finanzen (Modul)

- **FR-54** Kalkulation in sechs Kategorien mit Fixkosten und Kosten pro Person und Tag; Kosten der Mitarbeitenden werden auf Teilnehmerbeiträge umgelegt.
- **FR-55** Verpflegung als Tagessatz pro Person und Tag (Tagessatz × alle Personen × Tage), ohne Kopplung an den Essensplan.
- **FR-56** Freie Einnahmeposten, z. B. Zuschüsse, Rabatt der Organisation.
- **FR-57** Beitragsrechner: Schieberegler und Balken zeigen die Änderung des Teilnehmerbeitrags.
- **FR-58** Jedes Teammitglied erfasst Belege als Foto oder von Hand, mit Kategorie (eine der sechs), wer bezahlt hat, ob erstattet. Dient der Dokumentation; überwiesen wird von der Organisation.
- **FR-59** Soll-Ist-Vergleich und Erstattungsübersicht pro Person, immer aktuell; kein Abrechnungsabschluss.
- **FR-60** Jedes Teammitglied kann „Beitrag bezahlt“ abhaken; Zeltlotse vermerkt, wer abgehakt hat.

## V6 – Gemeinsam unterwegs

- **FR-61** Kanäle „Team“ und „Alle“. In „Alle“ schreiben alle Mitarbeitenden (nach Inline-Bestätigung *(UX)*); Teilnehmende mit Teilnehmer-Zugang lesen mit. Push: für das Team verlässlich, für Teilnehmende nur, wo das Gerät es erlaubt (iPhone erst nach Hinzufügen zum Home-Bildschirm).
- **FR-62** Verschobener Programmpunkt → „Alle informieren?“; ohne Bestätigung wird nichts verschickt. Nach „Nein“ bleibt der Hinweis „Geändert, noch nicht mitgeteilt“ mit „Jetzt informieren“ *(UX)*.
- **FR-63** Ansagen-Zettel für Ansagen beim Essen; Notizen für die Mitarbeiterbesprechung, dort abgehakt.
- **FR-64** Teilnehmer-Zugang pro Freizeit schaltbar; per QR-Code ohne Konto; nur Wochenübersicht (ohne Programmdetails), allgemeine Infos, Kanal „Alle“; endet mit der Freizeit.
- **FR-65** Verschlüsselter Offline-Modus für Küchen-Tagesansicht und Notfallkarte (plus Sicherheitskette lesbar, Vorfall-Protokoll schreibbar *(UX/Arch)*); Offline-Daten werden nach Freizeitende gelöscht.

## Übergreifende Anforderungen

### Datenschutz und Sicherheit

- **NFR-1** Daten gehören der Person; Organisationen und Freizeiten erhalten nur befristete Einsicht; jede Einsicht wird protokolliert.
- **NFR-2** Mandantentrennung: keine Organisation sieht Daten einer anderen, außer die Person gibt sie selbst frei.
- **NFR-3** Datenhaltung ausschließlich in der EU.
- **NFR-4** Gebaut nach DSGVO als gemeinsamer Grundlage für kirchliche und nichtkirchliche Organisationen; keine Auswahl des Rechtsrahmens pro Organisation. Für Profile und Nachweise ist der Betreiber verantwortlich (eigene Datenschutzerklärung); für Freizeit-Daten die Organisation, der Betreiber verarbeitet in ihrem Auftrag (AVV-Vorlage). Fachliche Prüfung vor dem Start als Dienst (OP-6).
- **NFR-5** Automatisches Löschen personenbezogener Daten nach Fristen, die jede Organisation festlegt (z. B. Teilnehmerlisten, Belege, archivierte Vorfall-Notizen); Zeltlotse gibt Vorgabewerte vor.
- **NFR-6** Der Freizeitpass ist für eine Freizeit zwingend und daher nicht widerrufbar, nur änderbar. Optionale Einwilligungen (z. B. Fotos) einzeln erteilt und jederzeit widerrufbar.
- **NFR-7** Datensparsamkeit: erhoben wird nur, was die Leitung im Fragenbaukasten auswählt.

### Geräte und Erreichbarkeit

- **NFR-8** Browser am Laptop (Vorbereitung, Leitung) und Handy (schnelle Handgriffe). Am Handy installierbare Web-App (PWA), keine native App in der MVP; App-Store-Hülle später möglich. Hinzufügen zum Home-Bildschirm ist Teil des Onboardings für Mitarbeitende.
- **NFR-9** Freizeitseite, Anmeldelinks und Teilnehmer-Zugang ohne Installation.
- **NFR-10** Kanal-Nachrichten als Push; sonst ist Zeltlotse still, keine Benachrichtigungsflut.
- **NFR-11** Bis zum Offline-Modus ist der ausgedruckte Notfallordner die Rückfallebene bei fehlendem Netz.

### Bedienung

- **NFR-12** Höchstens 1–2 Pflichtfelder pro Objekt.
- **NFR-13** Bearbeiten an Ort und Stelle, wo sinnvoll; sonst Detailansicht. Keine Pop-up-Fenster.
- **NFR-14** Jede wichtige Information höchstens 3–4 Klicks von der Startseite der Freizeit.
- **NFR-15** Neue Mitarbeitende nach 2 Minuten arbeitsfähig, ohne Anleitung.
- **NFR-16** Zahlen grafisch (z. B. Balken) statt Zahlenkolonnen.
- **NFR-17** Wiederanmeldung eines Kindes unter 3 Minuten.

### Betrieb und Wachstum

- **NFR-18** Lokal betreib- und testbar; später als Dienst in der Cloud.
- **NFR-19** Die Architektur verbaut keinen späteren Dienst für viele Organisationen mit Bezahlmodell; gebaut wird beides in der MVP nicht.
