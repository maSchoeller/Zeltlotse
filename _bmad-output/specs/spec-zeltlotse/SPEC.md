---
id: SPEC-zeltlotse
companions:
  - anforderungen.md
  - rollen-und-rechte.md
  - glossar.md
  - nutzungsszenarien.md
  - umfangsgrenzen.md
  - ../../planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/DESIGN.md
  - ../../planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md
  - ../../planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md
sources:
  - ../../planning-artifacts/prds/prd-Zeltlotse-2026-10-10/prd.md
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate. Source documents listed in frontmatter are for traceability — consult them only if you need narrative rationale or prose color this contract intentionally omits.

# Zeltlotse – MVP (Bausteine V1–V6)

## Why

**Vision und Schmerz.** Kirchliche Jugendverbände wie CVJM und EJW planen ihre Freizeiten heute mit Excel, WhatsApp und Google Drive. Chaotisch ist das nicht, aber es fehlt eine gemeinsame Stelle. Zeltlotse wird diese Stelle: von Kalkulation über Anmeldung und Freizeitpass bis Programm, Küche, Finanzen und Kommunikation auf der Freizeit. Der Nutzen entsteht aus der Summe vieler kleiner Dinge und dem Gefühl, auch während der Freizeit den Überblick zu haben. Leitgedanke: einfach, sorglos, schnell an die Information, das Gegenteil eines Verwaltungsmolochs. Zeltlotse startet als Hobbyprojekt für einen bekannten Kreis von Organisationen; Ziel ist ein Dienst, den andere Organisationen nutzen und kaufen. Betroffen sind Leitungen, Teams, Eltern, Teilnehmende und Organisationen (Rollen: `rollen-und-rechte.md`, Begriffe: `glossar.md`).

## Capabilities

Die MVP umfasst alle sechs Bausteine; sie legen nur die Bau-Reihenfolge fest (V1 → V6). Detailregeln je FR stehen in `anforderungen.md`.

### V1 – Menschen und Organisationen

- **CAP-1**
  - **intent:** Jede Person hat genau ein eigenes Profil über alle Rollen und Organisationen hinweg und meldet sich per Login-Link aus einer Mail an, auf Wunsch zusätzlich mit Passwort. (FR-1, FR-2)
  - **success:** Eine Person mit Rollen in zwei Organisationen hat ein Profil; Anmeldung gelingt per Mail-Link ohne Passwort und, nach Festlegen, per Passwort.
- **CAP-2**
  - **intent:** Ein Elternteil verwaltet im Familienkonto die Profile seiner Kinder unter 18, holt optional einen zweiten Elternteil hinzu und übergibt ab dem 18. Geburtstag das Profil an das Kind. (FR-3, FR-4)
  - **success:** Nach der Übergabe besitzt das volljährige Kind Profil, Freizeitpass und Verlauf; die Eltern sehen es nicht mehr. Vor dem 18. Geburtstag gibt es keinen Übergabe-Knopf.
- **CAP-3**
  - **intent:** Global Admin legt Organisationen an und lädt den ersten Org-Admin ein; Org-Admins ernennen weitere Org-Admins und Org-Überblick, legen Freizeiten an und setzen Leitungen mit gleichen Rechten ein. (FR-5, FR-6, FR-7)
  - **success:** Mitarbeitende sehen nur Freizeiten, in denen sie mitarbeiten, und werden mit der ersten bestätigten Mitarbeit automatisch Mitarbeitende der Organisation; eine weitere Freizeit derselben Organisation verlangt nur noch die Bestätigung der Leitung.
- **CAP-4**
  - **intent:** Mitarbeitende laden Nachweise einmal an ihrem Profil hoch; Organisationen erhalten nur eine einmalige Einsicht und speichern nur die Bestätigung. (FR-8, FR-9, FR-10, FR-12)
  - **success:** Nach der Einsicht liegt bei der Organisation nur „welcher Nachweis, eingesehen am, gültig bis, von wem“, das Ablaufdatum ist aus Ausstellungsdatum und Org-Richtlinie berechnet, der Zugriff endet; dieselben Dateien lassen sich einer zweiten Organisation ohne neuen Upload freigeben.
- **CAP-5**
  - **intent:** Die Leitung sieht fehlende und ablaufende Nachweise ihres Teams und erinnert per Knopfdruck. (FR-11)
  - **success:** Ein Nachweis, dessen Bestätigung abläuft, erscheint als Warnhinweis; eine Erinnerung geht erst nach Knopfdruck raus.
- **CAP-6**
  - **intent:** Jede Person sieht, wer wann Einsicht in ihre Daten hatte, und kann per Klick Auskunft erhalten, ihre Daten als PDF oder CSV exportieren und ihr Konto löschen. (FR-13, FR-14)
  - **success:** Jede Einsicht einer Freizeit oder Organisation steht im Zugriffsprotokoll der Person; Export und Löschung funktionieren ohne Kontakt zum Betreiber.

### V2 – Anmeldung und Freizeitpass

- **CAP-7**
  - **intent:** Jede Freizeit hat eine öffentliche Freizeitseite mit Beschreibung und Team und eine aus den Einstellungen erzeugte Vertrauensseite, die in Klartext erklärt, welche Daten erhoben werden, wer sie sieht und wann sie verschwinden. (FR-15, FR-66, FR-67)
  - **success:** Ist die Höchstzahl erreicht, zeigt die Freizeitseite „ausgebucht“ und nimmt Anmeldungen ohne Aussicht auf Zusage weiter an; die Vertrauensseite ändert sich mit den Einstellungen von Freizeit und Organisation.
- **CAP-8**
  - **intent:** Eltern und Volljährige melden über den Anmeldelink für Teilnehmende bzw. für Mitarbeitende ohne Installation an, auch als Gast ohne Konto, Geschwister nacheinander, mit digitaler Bestätigung der Teilnahmebedingungen. (FR-16, FR-17, FR-18, FR-19)
  - **success:** Eine Gast-Anmeldung am Handy gelingt ohne Konto; danach bietet Zeltlotse an, mit einem Klick ein Konto aus den Daten zu erstellen. Eine Wiederanmeldung mit Konto dauert unter 3 Minuten (NFR-17).
- **CAP-9**
  - **intent:** Die Leitung bestätigt Anmeldungen oder sagt ab; die Person bekommt Mail und Status, Absagen laufen nur über die Leitung, die selbst bestimmt, wer nachrückt. (FR-20, FR-21)
  - **success:** Nach dem Absenden steht „Anmeldung eingegangen“; nach Bestätigung kommt eine Mail und der Status ändert sich. Wer absagen will, sieht nur Mail und Telefon der Leitung. Es gibt keine Warteliste.
- **CAP-10**
  - **intent:** Eltern sehen Infobrief, Packliste und Notfallnummern der Freizeit in ihrem Konto; Gast-Anmeldende bekommen sie per Link in der Mail. (FR-68)
  - **success:** Ein Dokument mit Sichtbarkeit „Eltern und Teilnehmende“ ist im Elternkonto und über den Elterninfo-Link lesbar.
- **CAP-11**
  - **intent:** Die Leitung stellt den Freizeitpass aus dem Fragenbaukasten zusammen, getrennt für Teilnehmende und Mitarbeitende, ohne Pflichtkern. (FR-22)
  - **success:** Nur die gewählten Fragen werden erhoben und sind für die Freizeit sichtbar; fragt die Leitung keine Allergien ab, bleibt der Allergen-Abgleich der Küche leer.
- **CAP-12**
  - **intent:** Antworten im Freizeitpass gehören der Person und werden bei der nächsten Freizeit vorausgefüllt. (FR-23)
  - **success:** Bei einer zweiten Freizeit sind bekannte Antworten vorausgefüllt; Angaben älter als 12 Monate tragen „Stimmt das noch?“.
- **CAP-13**
  - **intent:** Die Freizeit sieht Freizeitpässe nur befristet: die Leitung ab Eingang der Anmeldung, das Team ab Bestätigung bis zum Ende des Freigabefensters; Team-Notizen zu Teilnehmenden bleiben in der Freizeit. (FR-24, FR-26)
  - **success:** Nach Ende des Freigabefensters (Org-Intervall ab Ende des letzten Freizeittages, beim Anlegen der Freizeit fixiert) sieht die Freizeit keinen Freizeitpass mehr; Eltern sehen, bis wann die Einsicht läuft; Team-Notizen erscheinen nie im Freizeitpass.
- **CAP-14**
  - **intent:** Die Leitung sieht im Leitungs-Cockpit den Stand der Freizeit und Warnhinweise und löst Erinnerungen per Knopfdruck aus. (FR-25, FR-27)
  - **success:** Das Cockpit zeigt Anmeldungen (eingegangen, bestätigt) im Verhältnis zu den Plätzen sowie Warnhinweise zu fehlenden Freizeitpässen, fehlenden oder abgelaufenen Nachweisen, unbesetzten Aufgaben und fehlender Sicherheitskette auf einer Seite.
- **CAP-15**
  - **intent:** Im Notfall öffnen Mitarbeitende per Notfall-Button die Sicherheitskette (was tun, wen anrufen) und per Schnellsuche den Freizeitpass der betroffenen Person. (FR-28, FR-29)
  - **success:** Vom Notfall-Button bis zu Allergien, Medikamenten und Notfallkontakten einer Person vergehen Sekunden; alle Mitarbeitenden sehen die Notfallkontakte aller Teilnehmenden.
- **CAP-16**
  - **intent:** Die Leitung hängt das Schutzkonzept an; jedes Teammitglied bestätigt „gelesen“. (FR-30)
  - **success:** Für jedes Teammitglied ist sichtbar, ob es das Schutzkonzept als gelesen bestätigt hat.
- **CAP-17**
  - **intent:** Mitarbeitende führen ein Vorfall-Protokoll (was passiert ist, welches Medikament wann gegeben, was aus dem Erste-Hilfe-Kasten entnommen wurde); daraus entsteht eine Vorfall-Notiz an die Leitung, die nach Abschluss in der Organisation archiviert wird. (FR-31, FR-32)
  - **success:** Jeder Eintrag ist für die Leitung sofort sichtbar; Entnahmen stehen automatisch auf der Schadens- und Verlustliste; die archivierte Notiz wird nach der Löschfrist der Organisation gelöscht.
- **CAP-18**
  - **intent:** Die Leitung druckt den Notfallordner mit Notfallkontakten und relevanten Freizeitpass-Angaben. (FR-33)
  - **success:** Der Ausdruck ist ohne Netz nutzbar (je Kind eine Seite, A4, schwarz-weiß-tauglich) und ist bis zum Offline-Modus die Rückfallebene (NFR-11).

### V3 – Programmpunkt als Knotenpunkt

- **CAP-19**
  - **intent:** Das Team plant den Wochenraster gemeinsam; Frühstück, Mittag- und Abendessen sind feste Programmpunkte, Mitarbeitende übernehmen Programmpunkte selbst. (FR-34, FR-35)
  - **success:** Eine neue Freizeit hat die drei Mahlzeiten je Tag im Raster; ein Programmpunkt lässt sich mit „Ich übernehme“ übernehmen, ohne dass die Leitung zuteilt.
- **CAP-20**
  - **intent:** Material hängt am Programmpunkt mit den Zuständen gebraucht, vorhanden, besorgt, eingepackt, inklusive „Ich bringe das mit“; eine Gesamt-Materialliste führt alles zusammen. (FR-36, FR-69)
  - **success:** Die Gesamt-Materialliste zeigt das Material aller Programmpunkte mit Mengen und Status, ohne zusätzliche Eingabe.
- **CAP-21**
  - **intent:** Freizeiten reservieren Material aus dem Inventar der Organisation für einen Zeitraum. (FR-37)
  - **success:** Überschneiden sich Reservierungen paralleler Freizeiten, erscheint ein Konflikthinweis; Zeltlotse löst den Konflikt nicht.
- **CAP-22**
  - **intent:** Schäden und Verluste werden während der ganzen Freizeit erfasst und werden zu Anschaffungswünschen an die Organisation. (FR-38)
  - **success:** Anschaffungswünsche sind für Org-Admins, Leitungen und Org-Überblick sichtbar, auch nach Abschluss der Freizeit.
- **CAP-23**
  - **intent:** Aufgaben brauchen nur einen Titel, haben die Phasen Vorbereitung, Während und Nachbereitung und werden selbst übernommen; Aufgaben-Vorlagen der Organisation fließen in neue Freizeiten. (FR-39, FR-40)
  - **success:** Eine neue Freizeit enthält die Vorlagen-Aufgaben; übernommene Aufgaben erscheinen unter „Meine Aufgaben“; eine Aufgabe lässt sich in eine Vorlage übernehmen.
- **CAP-24**
  - **intent:** Mitarbeitende legen gelungene Programmpunkte bewusst in der Programmbibliothek der Organisation ab und übernehmen sie samt Material und Aufgaben in andere Freizeiten. (FR-41)
  - **success:** Ohne bewusste Ablage landet nichts in der Bibliothek; eine Übernahme bringt Material und Aufgaben mit.
- **CAP-25**
  - **intent:** Dokumente liegen am Kontext (Freizeit, Programmpunkt) mit den Sichtbarkeiten Team, Eltern und Teilnehmende, öffentlich; Org-Dokumente werden eingebunden statt kopiert. (FR-43)
  - **success:** Ein Dokument erscheint genau für die gewählte Sichtbarkeit; eine Suche findet Dokumente über alle Kontexte.
- **CAP-26**
  - **intent:** Die Leitung schaltet Module pro Freizeit an oder ab, zum Beispiel Verpflegung, Finanzen, Teilnehmer-Zugang. (FR-44)
  - **success:** Ein abgeschaltetes Modul erscheint nirgends und ist auch serverseitig nicht erreichbar; beim Abschalten bleiben seine Daten erhalten.
- **CAP-27**
  - **intent:** Eine Freizeit durchläuft die Phasen in Vorbereitung, läuft, Nachbereitung und gelaufen; die Leitung schließt sie ausdrücklich ab. (FR-45)
  - **success:** Die Phasen bis zur Nachbereitung ergeben sich aus Datum; erst der Abschluss setzt „gelaufen“, danach ist die Freizeit nur lesbar (Ausnahmen: Erstattungsvermerke, Anschaffungswünsche), einsehbar und per Filter ausblendbar; Abschluss trotz offener Punkte ist nach Inline-Warnung möglich.

### V4 – Verpflegung (Modul)

- **CAP-28**
  - **intent:** Die Küche legt Rezepte mit Basis-Personenzahl an (Zutaten mit Menge, Einheit, Allergenen; Zubereitung als Freitext), die Zeltlotse auf die Freizeit hochrechnet. (FR-46)
  - **success:** Ein Rezept für 3 Personen erscheint für eine Freizeit mit 60 Personen mit zwanzigfachen Mengen; jede Person zählt gleich.
- **CAP-29**
  - **intent:** Die Küche merkt Gerichte an Mahlzeiten vor und tauscht sie jederzeit; Zeltlotse berechnet daraus die Einkaufsliste mit Allergen-Abgleich und Ersatzportionen, die Küche ergänzt und überschreibt. (FR-47, FR-48, FR-49, FR-50)
  - **success:** Ein Gerichttausch zeigt die Änderung der noch nicht gekauften Posten als Vorschlag; ohne Bestätigung der Küche ändert sich die Liste nicht; von Hand überschriebene Mengen bleiben erhalten.
- **CAP-30**
  - **intent:** Die Küche gibt frei, für welche Tage und Posten eingekauft wird; wer einkauft, hakt Gekauftes ab. (FR-51)
  - **success:** Nur freigegebene Posten erscheinen zum Einkaufen; Abhaken ist ein Tipp.
- **CAP-31**
  - **intent:** Die Küchen-Tagesansicht zeigt, was heute ansteht und welche Allergien und Sonderkost zu beachten sind; eine Mahlzeit lässt sich spontan als Restetag markieren. (FR-52, FR-53)
  - **success:** Die Tagesansicht zeigt die Gerichte des Tages mit den betroffenen Allergenen auf einen Blick.

### V5 – Finanzen (Modul)

- **CAP-32**
  - **intent:** Die Leitung kalkuliert in sechs Kategorien mit Fixkosten und Kosten pro Person und Tag, Verpflegung als Tagessatz, freien Einnahmeposten, und sieht im Beitragsrechner, wie sich der Teilnehmerbeitrag ändert. (FR-54, FR-55, FR-56, FR-57)
  - **success:** Kosten der Mitarbeitenden werden auf die Teilnehmerbeiträge umgelegt; Verpflegung = Tagessatz × alle Personen × Tage ohne Kopplung an den Essensplan; Regler verändern den Beitrag sichtbar als Balken.
- **CAP-33**
  - **intent:** Jedes Teammitglied erfasst Belege als Foto oder von Hand mit Kategorie, wer bezahlt hat und ob erstattet wurde; alle sehen Soll-Ist-Vergleich und Erstattungsübersicht. (FR-58, FR-59)
  - **success:** Soll-Ist und „wer bekommt noch Geld“ zeigen immer den aktuellen Stand, ohne Abrechnungsabschluss und ohne Zusammenführen von Tabellen.
- **CAP-34**
  - **intent:** Jedes Teammitglied hakt ab, welche Teilnehmenden ihren Beitrag bezahlt haben. (FR-60)
  - **success:** Zu jedem Haken ist vermerkt, wer ihn gesetzt hat.

### V6 – Gemeinsam unterwegs

- **CAP-35**
  - **intent:** Kurznachrichten laufen in den Kanälen „Team“ und „Alle“ und kommen als Push-Nachricht aufs Handy; in „Alle“ schreiben Mitarbeitende, Teilnehmende mit Teilnehmer-Zugang lesen mit. (FR-61)
  - **success:** Push erreicht das Team verlässlich, Teilnehmende nur, wo das Gerät es erlaubt; eine Nachricht in „Alle“ geht erst nach Inline-Bestätigung raus.
- **CAP-36**
  - **intent:** Wird ein Programmpunkt verschoben, fragt Zeltlotse „Alle informieren?“. (FR-62)
  - **success:** Ohne Bestätigung geht nichts raus; nach „Nein“ bleibt am Programmpunkt der Hinweis „Geändert, noch nicht mitgeteilt“ mit „Jetzt informieren“.
- **CAP-37**
  - **intent:** Das Team sammelt Ansagen für das Essen auf dem Ansagen-Zettel und Punkte für die Mitarbeiterbesprechung, die dort abgehakt werden. (FR-63)
  - **success:** Besprechungspunkte des Tages lassen sich in der Besprechung abhaken.
- **CAP-38**
  - **intent:** Die Leitung schaltet den Teilnehmer-Zugang ein; Teilnehmende kommen per QR-Code ohne Konto hinein und sehen Wochenübersicht ohne Programmdetails, allgemeine Infos und den Kanal „Alle“. (FR-64)
  - **success:** Ein Scan genügt; Teilnehmende sehen nichts darüber hinaus; mit dem Ende der Freizeit endet der Zugang.
- **CAP-39**
  - **intent:** Küchen-Tagesansicht, Notfallkarte und Sicherheitskette sind offline verschlüsselt lesbar; das Vorfall-Protokoll ist offline beschreibbar. (FR-65)
  - **success:** Im Flugmodus öffnen sich Notfallkarte und Küchen-Tagesansicht; Offline-Daten werden nach Freizeitende auf dem Gerät gelöscht, auch ohne Netz.

## Constraints

- **Kein Versand, keine Planungsänderung ohne Menschen:** Mails, Push und Änderungen bestehender Planung entstehen nur nach ausdrücklicher Bestätigung; Ableitungen (Listen, Hinweise, Ablaufdaten, Phase) laufen automatisch (Leitprinzip 6, NFR-10). Ausnahme: selbst angeforderte Mails wie Login-Link und Einladungen.
- **Daten gehören der Person:** Organisationen und Freizeiten erhalten nur befristete, protokollierte Einsicht und nur in abgefragte Angaben; niemand sonst hält Kopien (Leitprinzip 1, NFR-1, NFR-7).
- **Mandantentrennung:** Keine Organisation sieht Daten einer anderen, außer die Person gibt sie selbst frei; Org-Rollen und Global Admin sehen keine Freizeit-Inhalte ohne eigene Mitarbeit (NFR-2).
- **Datenschutz:** Datenhaltung ausschließlich in der EU; DSGVO als einziger Rechtsrahmen mit geteilter Verantwortung und AVV-Vorlage; automatisches Löschen nach Org-Fristen mit Vorgabewerten; Freizeitpass nur änderbar, optionale Einwilligungen einzeln widerrufbar (NFR-3 bis NFR-6).
- **Geräte:** Browser am Laptop und installierbare Web-App (PWA) am Handy, keine native App; Freizeitseite, Anmeldelinks und QR-Zugang ohne Installation (NFR-8, NFR-9).
- **Bedienung:** höchstens 1–2 Pflichtfelder pro Objekt; Bearbeiten an Ort und Stelle, keine Pop-ups; wichtige Infos höchstens 3–4 Klicks von der Startseite der Freizeit; neue Mitarbeitende nach 2 Minuten ohne Anleitung arbeitsfähig; Zahlen grafisch (NFR-12 bis NFR-16).
- **Simpel halten:** Für Sonderfälle die einfachste Lösung, keine Speziallogik; was analog gut funktioniert, bleibt analog; jede Angabe wird nur einmal erfasst (Leitprinzipien 3–5).
- **Vertrauen statt Rechte-Matrix:** Innerhalb einer Freizeit darf das Team fast alles; der Leitung vorbehalten ist nur die Liste in `rollen-und-rechte.md`. Jeder pflegt seinen Bereich, keine zentrale Datenpflege (Leitprinzipien 7, 8).
- **Fester Fragenkatalog:** Fragen des Freizeitpasses kommen aus einem globalen Katalog mit stabilen Kennungen; eigene Fragen der Leitung gibt es in der MVP nicht. Die Allergie-Frage ist eine Auswahl aus den 14 EU-Hauptallergenen plus Freitext; nur die Auswahl fließt in den Küchen-Abgleich.
- **Betrieb:** lokal betreib- und testbar, später als Cloud-Dienst; die Architektur verbaut keinen Mehr-Organisationen-Dienst mit Bezahlmodell, gebaut wird beides nicht (NFR-18, NFR-19).
- **Bau-Reihenfolge:** V1 → V6; der Offline-Modus (V6) gehört zur MVP, wird aber zuletzt gebaut.
- **Gestaltung, Verhalten, Technik:** bindend nach `DESIGN.md`, `EXPERIENCE.md` (Oberfläche nur Deutsch ohne Du/Sie, ELKW-Stil, WCAG 2.2 AA) und `ARCHITECTURE-SPINE.md` (AD-1 bis AD-30).

## Non-goals

- **Bewusst nie:** ganze Freizeit kopieren; Resteregister mit automatischem Reste-Vorschlag; Kisteninhalte einzeln erfassen oder formaler Rückgabe-Check; Live-Kalkulation über Anmeldungen; personenbezogene Massen-Aufgaben, persönliche Checklisten, feste Aufgaben-Startlisten pro Freizeittyp; Mitarbeiterpool oder Mitarbeiter-Anfragen über die Plattform; feingranulare Rechte-Matrix, frei baubare Gruppen, Unterkalender, Bearbeitung in Pop-ups; Papier- oder Offline-Anmeldung; generierte Rezepte; Kanban-Board.
- **Nicht in dieser Version:** automatische Warteliste und Nachrücken; Absage durch Eltern in Zeltlotse; automatisch verschickte Erinnerungen oder Nachrichten; Patchwork-Familien als eigenes Modell; eigenes Konto für Kinder unter 18; Tagesleitung als Rolle oder Übergabe in der App; Abschlussmappe und Abrechnungsabschluss; Lösung von Inventar-Konflikten in Zeltlotse; Überweisungen (Belege dienen der Dokumentation).
- **Später:** Ausbau-Pool nach der MVP in `umfangsgrenzen.md`; nichts davon wird vorgreifend gebaut.

## Success signal

- Nach dem ersten Sommer wurden 1–2 Freizeiten komplett mit Zeltlotse geplant und durchgeführt, ohne Excel-Tabelle, und die Leitung sagt: „Ich hatte die ganze Zeit den Überblick.“
- Gegensignale: Das Team pflegt Informationen doppelt in Zeltlotse und WhatsApp; Eltern brechen die Anmeldung ab; jemand klagt über „schon wieder ein Formular“; Zeltlotse wächst um Funktionen, die niemand vermisst hat.

## Assumptions

- Eine Spec deckt die ganze MVP (V1–V6) ab; die Bausteine steuern nur die Bau-Reihenfolge, nicht getrennte Specs.
- Verfeinerungen aus UX- und Architektur-Lauf (Phase Nachbereitung, fester Fragenkatalog, Inline-Bestätigung für „Alle“, Schadensliste während der ganzen Freizeit, Cockpit-Warnung bei fehlender Sicherheitskette) gelten als bestätigt, weil Marvin sie dort abgenommen hat; das PRD ist an diesen Stellen noch nicht nachgezogen.

## Open Questions

- Wie füllt ein Gast ohne Konto nach dem Absenden den Freizeitpass aus (NS-1 Schritt 5) und sieht den Status (FR-20)? Die Architektur kennt keinen Gast-Zugangslink, der Elterninfo-Link ist nur lesend (Abgleich PRD, Befund B-3).
- OP-6 (zurückgestellt, blockiert die MVP nicht): Datenschutzmodell (NFR-4: geteilte Verantwortung, AVV-Vorlage, Datenschutzerklärung) fachlich prüfen lassen, bevor Zeltlotse als Dienst für fremde Organisationen startet. Verantwortlich: Marvin.
