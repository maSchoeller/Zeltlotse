# Abgleich Architektur-Gerüst ↔ PRD

- **Gerüst:** `ARCHITECTURE-SPINE.md` (2026-10-11, draft)
- **Quelle:** `prds/prd-Zeltlotse-2026-10-10/prd.md` (final)
- **Datum:** 2026-10-11
- **Prüffokus:** PRD-Inhalte mit architektonischer Folge, die von keiner AD (Architekturentscheidung), Konvention oder Zurückgestellt-Zeile abgedeckt sind oder ihnen widersprechen; Plausibilität der Modul-Zuordnung je FR (funktionale Anforderung). Reine UI-/Feature-Details sind ausgeklammert.

## Urteil

Das Gerüst trägt die großen Linien des PRD (Datenbesitz der Person, Freigabe per Durchgriff, Mandantentrennung, keine Nachricht ohne Menschen, Offline-Grenzen) sauber. Es hat aber **eine strukturelle Lücke**: Der Abhängigkeitsgraph aus AD-2 lässt Küche, Inventar und Sicherheit nicht an Daten heran, die AD-17, AD-20 und einzelne FRs von ihnen verlangen. Dazu kommen mehrere **leise Datenschutz-Anforderungen** (Datensparsamkeit beim Lesen, Gast ohne Konto, Konto-Löschung gegen Aufbewahrung, Gesundheitsdaten im Vorfall-Protokoll), die architektonisch noch nicht entschieden sind. Vor dem ersten Bau-Epic nachschärfen.

| Schwere | Anzahl |
| --- | --- |
| kritisch | 1 |
| hoch | 6 |
| mittel | 7 |
| niedrig | 6 |

---

## Befunde

### B-1 – Abhängigkeitsgraph verhindert Pflichtprüfungen aus AD-17/AD-20 und mehrere FRs · **kritisch**

**PRD-Stelle:** FR-44 (Module pro Freizeit schaltbar), FR-45 (gelaufen = nur lesbar), FR-46 (Skalierung auf Personenzahl der Freizeit), FR-32 (Entnahmen → Schadens- und Verlustliste), FR-38 (Schäden aus der Freizeit), FR-40 (Aufgaben-Vorlagen der Organisation werden übernommen; Aufgaben lassen sich in eine Vorlage übernehmen), Rollentabelle (Team darf Material, Schäden erfassen).

**Was widerspricht:**
- AD-17 verlangt, dass *jedes* Modul Schreibzugriffe auf gelaufene Freizeiten ablehnt, und die Phase kommt *ausschließlich* aus `Freizeiten.Contracts`. AD-20 verlangt, dass Küche und Finanzen bei abgeschaltetem Modul `404` liefern; den Schalter besitzt Freizeiten. **Küche (`KUE → PRG, PER`) und Inventar (`INV → ORG`) haben aber keinen Pfeil zu Freizeiten** und dürfen laut AD-2 nicht synchron dort nachfragen. Dasselbe gilt für die Rollenprüfung „ist Team dieser Freizeit“ (AD-12) in Küche und Inventar, und für die Personenzahl der Freizeit (FR-46) in Küche.
- FR-32: Sicherheit muss Entnahmen auf die Verlustliste in Inventar bringen. Es gibt keinen Pfeil `SIC → INV`; der Ereignisweg scheitert an AD-4/Konvention „Ereignisse tragen nur Kennungen“, weil Inventar die Entnahme-Details dann bei Sicherheit nachlesen müsste (`INV → SIC` gibt es ebenfalls nicht).
- FR-40: Aufgaben-Vorlagen gehören laut Datenbesitz zu **Organisationen**, Aufgaben zu **Programm**. Programm hat keinen Pfeil zu Organisationen, kann also weder Vorlagen lesen (Übernahme bei neuer Freizeit) noch eine Aufgabe als Vorlage zurückschreiben.

**Fix-Vorschlag:**
1. Neue AD „Freizeit-Kontext am Endpunkt“: Für alle Routen `/api/<modul>/freizeiten/{freizeitId}/...` löst ein zentraler Endpunkt-Filter im Host (`Zeltlotse.Web`) **einmal** über `Freizeiten.Contracts` Phase, Modul-Schalter, Organisation und Rolle des Aufrufers auf und legt sie im Anfrage-Kontext (`SharedKernel`) ab. Module lesen nur diesen Kontext; AD-17 und AD-20 verweisen darauf. Damit braucht kein Fachmodul einen eigenen Pfeil zu Freizeiten nur für Prüfungen.
2. Zusätzlich Pfeil `KUE → FZ` (Personenzahl, Teilnehmenden-/Mitarbeitenden-Liste für FR-46/48) – zyklusfrei.
3. FR-32: Pfeil `SIC → INV` ergänzen (Sicherheit ruft `Inventar.Contracts.EntnahmeErfassen` auf) **oder** Konvention lockern: „Ereignisse tragen Kennungen und nicht-personenbezogene Fachdaten“.
4. FR-40: Aufgaben-Vorlagen in den Datenbesitz von **Programm** verschieben (organisationsweit mit `OrganisationId`, analog Programmbibliothek); Rollenprüfung Org-Admin über den Kontext aus Punkt 1.
5. AD-24 um einen Test ergänzen: Jeder freizeitbezogene Endpunkt ist am Freizeit-Kontext-Filter registriert.

---

### B-2 – Freigabe erlaubt Lesen aller Antworten, nicht nur der abgefragten · **hoch**

**PRD-Stelle:** NFR-7 (Erhoben wird nur, was die Leitung auswählt), FR-22 (Fragen getrennt für Teilnehmende/Mitarbeitende), Leitprinzip 1, NFR-1.

**Was fehlt:** AD-8 definiert die Freigabe als (Person, Freizeit, Stufe, gültig bis). Da die Antworten der Person freizeitübergreifend gespeichert werden (AD-10), könnte eine Freizeit über `Personen.Contracts` auch Antworten lesen, die sie gar nicht abfragt (z. B. Medikamente aus einer früheren Freizeit). Die Einschränkung auf die gewählten Fragen-Kennungen ist nirgends festgelegt; ebenso fehlt, ob die Person als Teilnehmende oder Mitarbeitende freigibt (andere Fragenliste).

**Fix-Vorschlag:** AD-8 ergänzen: „Die Freigabe trägt zusätzlich die Personen-Art (Teilnehmende/Mitarbeitende). `Personen.Contracts` liefert nur Antworten zu den Fragen-Kennungen, die die Freizeit für diese Art abfragt; Freizeiten übergibt die Kennungen beim Anlegen der Freigabe bzw. per Ereignis bei Änderung. Antworten außerhalb dieser Liste sind für die Freizeit unsichtbar.“

---

### B-3 – Gast ohne Konto: kein Zugangsweg und kein Löschzeitpunkt · **hoch**

**PRD-Stelle:** FR-17, NS-1 Schritt 2–5 (Gast füllt danach den Freizeitpass aus, sieht Status), FR-20 (Status in Zeltlotse), FR-68 (Gäste bekommen Elterninfo „per Link in der Mail“), NFR-5.

**Was fehlt:**
- AD-11 zählt sechs Link-Arten auf; der **Gast-Link** (Freizeitpass nachträglich ausfüllen, Status sehen, Elterninfo lesen) ist nicht darunter. Ohne ihn kann ein Gast nach dem Absenden nicht mehr an sein Profil.
- AD-7 erlaubt Profile ohne Konto, aber AD-16 löscht nur über Konto-Löschung oder Org-Fristen – und Personen-Daten tragen keine `OrganisationId`. Ein Gast-Profil, das nie in ein Konto übergeht, bleibt damit **unbegrenzt** liegen (inkl. Gesundheitsdaten).

**Fix-Vorschlag:**
- AD-11: „Gast-Zugang“ als siebte Link-Art aufnehmen (Zweck: eigenes Gast-Profil und Anmeldestatus einer Freizeit, Elterninfo; Ablauf: Ende des Freigabefensters).
- AD-7/AD-16 ergänzen: „Ein Profil ohne Konto wird vom Lösch-Dienst endgültig gelöscht, sobald keine Freigabe mehr gültig ist (spätestens Ende des letzten Freigabefensters), sofern es nicht an ein Konto gehängt wurde.“

---

### B-4 – Konto-Löschung kollidiert mit Freizeit-Bedarf und Aufbewahrungsfristen · **hoch**

**PRD-Stelle:** FR-14 (Konto löschen per Klick), NFR-6 (Freizeitpass für die Freizeit zwingend, nicht widerrufbar), FR-31/NS-6 (Vorfall-Notiz bleibt für Eltern/Versicherung bis zur Org-Löschfrist), NFR-4 (für Freizeit-Daten ist die Organisation verantwortlich), NFR-5.

**Was widerspricht:** AD-16 lässt jedes Modul auf *Person gelöscht* reagieren und schreibt „endgültig gelöscht“. Offen ist, was gilt, wenn die Person in einer laufenden Freizeit angemeldet ist (Notfalldaten weg) oder in einer archivierten Vorfall-Notiz, einem Beleg („wer bezahlt hat“) oder einem Beitrag-bezahlt-Vermerk steht, die die Organisation nach eigener Frist aufbewahren muss. Ohne Regel löscht jedes Modul nach eigenem Gutdünken – genau das, was AD-16 verhindern soll.

**Fix-Vorschlag:** AD-16 ergänzen um eine Vorrangregel: „Daten in Verantwortung des Betreibers (Modul Personen, Konten) werden bei Konto-Löschung sofort gelöscht – **außer** Freizeitpass-Antworten mit aktiver Freigabe; diese bleiben bis Ende des Freigabefensters und werden dann gelöscht. Daten in Verantwortung der Organisation (Freizeiten, Sicherheit, Finanzen, Kanäle, Inventar) bleiben bis zur Org-Löschfrist; der Personenbezug darin besteht nur aus der Kennung, deren Anzeige nach Löschung „gelöschte Person“ ergibt.“ Die Oberfläche weist vor der Löschung darauf hin.

---

### B-5 – Gesundheitsdaten im Vorfall-Protokoll sind nicht verschlüsselt · **hoch**

**PRD-Stelle:** FR-31 / NS-6 (welches Medikament wann gegeben), Glossar „Vorfall-Notiz“ (wird archiviert), NFR-4.

**Was fehlt:** AD-14 verschlüsselt nur Freizeitpass-Antworten und Nachweis-Dateien im Modul Personen. Das Vorfall-Protokoll in Sicherheit enthält ebenfalls Gesundheitsdaten (Verletzung, Medikamentengabe) und lebt nach der Freizeit als archivierte Notiz in der Organisation weiter – im Klartext in DB-Backups. Gleiches, schwächer, für Team-Notizen zu Teilnehmenden (FR-26).

**Fix-Vorschlag:** AD-14 auf „Gesundheitsdaten in jedem Modul“ ausweiten: Freitext und Medikamentenangaben des Vorfall-Protokolls und der archivierten Vorfall-Notiz werden in Sicherheit verschlüsselt gespeichert (gleiches Verfahren, gleiche Schlüsselverwaltung über `SharedKernel`). Binds um Sicherheit und FR-31 erweitern. Team-Notizen (FR-26) mindestens als Kann-Regel aufnehmen.

---

### B-6 – Push für Teilnehmende mit QR-Zugang widerspricht AD-5 · **hoch**

**PRD-Stelle:** FR-61 / NS-10 Schritt 4 (Teilnehmende mit Teilnehmer-Zugang bekommen Push), FR-64, NFR-9.

**Was widerspricht:** AD-5 liefert den QR-Teilnehmerzugang als statisches Server-Rendering **ohne** Interaktivitäts-Laufzeit aus. Ein Push-Abo braucht aber JavaScript (Service Worker, `PushManager.subscribe`) und unter iOS eine installierte PWA (Manifest). Zudem besitzt Kanäle die Push-Abos (AD-21), aber für einen kontolosen QR-Nutzer ist kein Besitzer/Schlüssel des Abos definiert und auch nicht, wann es gelöscht wird.

**Fix-Vorschlag:** AD-5 um eine Ausnahme ergänzen: „Die QR-Seite lädt ein kleines Skript für Service Worker, Manifest und Push-Abo, aber keine Blazor-Laufzeit.“ AD-21 ergänzen: „Push-Abos von QR-Teilnehmenden hängen an der Kennung des QR-Zugangs (nicht an einer Person) und werden mit Ende des Zugangs gelöscht.“

---

### B-7 – Autorisierungs-Kontexte jenseits von Organisation/Freizeit fehlen · **hoch**

**PRD-Stelle:** Rollentabelle (Eltern, volljährige Teilnehmende, QR-Teilnehmende, Org-Überblick „keine Freizeit-Inhalte außer bei eigener Mitarbeit“), FR-3/FR-4 (Familie, Übergabe), FR-43/FR-68 (Sichtbarkeit „Eltern und Teilnehmende“), FR-10 (prüfende Person), FR-64 / NS-10 Schritt 2 (QR sieht Wochenübersicht *ohne* Details), Leitprinzip 8.

**Was fehlt:** AD-12 kennt nur „Rolle im konkreten Kontext (Organisation bzw. Freizeit)“. Nicht abgedeckt:
- **Person/Familie:** wer darf Profil X lesen/ändern (selbst; Eltern bei Kindern unter 18; nach Übergabe niemand mehr).
- **Angemeldete und deren Eltern** als Leser einer Freizeit (Elterninfo, Status) – sie sind weder Team noch Org-Rolle.
- **QR-Prinzipal** (kein Konto, nur Lesen, eingeschränkte Projektion in Programm und Kanäle).
- **Prüfende Person** einer Nachweis-Einsicht.
- Klarstellung, dass **Org-Admin und Org-Überblick keine Freizeit-Inhalte** sehen (nur Stammdaten/Übersicht), es sei denn, sie sind dort Leitung/Team.

**Fix-Vorschlag:** AD-12 um eine feste Liste der Kontexte und Prinzipale erweitern: *Konto-Person*, *Familie*, *Organisation (Org-Admin, Org-Überblick, Mitarbeitende)*, *Freizeit (Leitung, Team, Angemeldete inkl. Eltern, QR-Zugang)*, *Einsicht (prüfende Person, befristet)*. Org-Rollen vererben keine Freizeit-Rechte. Endpunkte, die QR-Zugriff zulassen, liefern eine eigene, reduzierte Projektion (Konvention: eigener Endpunkt, nicht derselbe mit ausgeblendeten Feldern).

---

### B-8 – Protokollierung: Nachweis-Einsicht und Sammel-Lesezugriffe ungeregelt · **mittel**

**PRD-Stelle:** NFR-1 („jede Einsicht wird protokolliert“), FR-13, FR-10.

**Was fehlt:** AD-8 protokolliert Lesen von Freizeitpass-Daten. AD-9 sagt nichts darüber, ob die einmalige Nachweis-Einsicht ins Einsicht-Protokoll geht. Ebenfalls offen: wie Sammel-Lesezugriffe protokolliert werden (Allergen-Abgleich der Küche über alle Pässe, Vollständigkeits-Prüfung fürs Cockpit FR-25/27, Notfallordner-Druck, Offline-Synchronisation) – pro Person und Zugriff würde das Protokoll für Eltern unlesbar.

**Fix-Vorschlag:** AD-9 ergänzen: „Jede Nachweis-Einsicht wird im Einsicht-Protokoll vermerkt.“ AD-8 ergänzen: „Sammelzugriffe werden je Person einmal pro Tag und Zweck protokolliert (z. B. ‚Küche – Allergen-Abgleich‘). Reine Vollständigkeits-Abfragen (ausgefüllt ja/nein) liefern keine Inhalte und werden nicht protokolliert.“

---

### B-9 – „Gültig bis“ der Freigabe veraltet bei Datumsänderung oder Absage · **mittel**

**PRD-Stelle:** FR-24b–d (Fenster läuft ab Ende des letzten Freizeittages; Intervall bei Anlage fixiert), FR-21 (Leitung trägt Absagen ein), FR-64 (QR endet mit der Freizeit).

**Was fehlt:** AD-8 speichert „gültig bis“ in der Freigabe (Modul Personen). Ändert die Leitung das Enddatum der Freizeit, oder wird eine Anmeldung abgesagt bzw. nie bestätigt, ist nicht festgelegt, dass Personen davon erfährt. Ebenso das QR-Ablaufdatum (AD-11). Das fixierte Freigabe-Intervall fehlt außerdem im Datenbesitz von Freizeiten.

**Fix-Vorschlag:** AD-8 ergänzen: „Freizeiten veröffentlicht `FreizeitZeitraumGeaendert` und `AnmeldungAbgesagt`; Personen passt ‚gültig bis‘ an bzw. beendet die Freigabe sofort. Nicht bestätigte Anmeldungen verlieren die Leitungs-Freigabe mit Ende des Freigabefensters.“ Datenbesitz Freizeiten um „fixiertes Freigabe-Intervall“ ergänzen; QR-Ablauf wird aus dem Freizeit-Ende abgeleitet statt gespeichert.

---

### B-10 – Offline-Daten: Löschzeitpunkt weicher als im PRD · **mittel**

**PRD-Stelle:** FR-65 („Offline-Daten werden nach Freizeitende gelöscht“).

**Was widerspricht:** AD-18 löscht „mit dem Ende des Freigabefensters bzw. der Freizeit“. Das Freigabefenster endet *nach* der Freizeit; die Formulierung erlaubt also längere lokale Speicherung als das PRD. Zudem kann ein Gerät nur löschen, wenn die App geöffnet wird.

**Fix-Vorschlag:** AD-18 präzisieren: „Lokale Daten tragen ihr Ablaufdatum (Freizeitende, Europe/Berlin, Tagesende). Die App prüft es bei jedem Start und Service-Worker-Aufruf und löscht Daten und WebCrypto-Schlüssel danach; abgelaufene Daten werden nie angezeigt.“

---

### B-11 – Schnellsuche nach Personen falsch zugeordnet · **mittel**

**PRD-Stelle:** FR-28.

**Was widerspricht:** Die Zuordnungstabelle legt die Suche in Freizeiten. Namen gehören laut AD-7 aber Personen; Freizeiten hält nur Kennungen. Entweder kopiert Freizeiten Namen (Verstoß gegen AD-7) oder die Suche gehört nach Personen (über die Freigaben der Freizeit).

**Fix-Vorschlag:** Zeile ändern: „Suche in Personen (`Personen.Contracts.SucheInFreizeit(freizeitId, text)`), beschränkt auf Personen mit gültiger Freigabe für diese Freizeit.“ Namen und Geburtsdatum sind nicht verschlüsselt (AD-14 betrifft nur Antworten/Dateien) – das explizit in AD-14 festhalten.

---

### B-12 – AD-15 „keine öffentlichen URLs“ gegen öffentliche Inhalte · **mittel**

**PRD-Stelle:** FR-15 (öffentliche Freizeitseite mit Team-Bild und Herkunft), FR-43a (Sichtbarkeit „öffentlich auf der Freizeitseite“), NFR-6 (Einwilligungen einzeln).

**Was widerspricht:** AD-15 verlangt Auslieferung nur mit Rechteprüfung; öffentliche Dokumente und Team-Bilder müssen anonym abrufbar sein. Außerdem ist das Team-Bild auf einer öffentlichen Seite ein Personendatum, für das keine Einwilligungsprüfung festgelegt ist.

**Fix-Vorschlag:** AD-15 ergänzen: „Sichtbarkeit ‚öffentlich‘ ist eine Rechteprüfung, die immer erlaubt; Auslieferung trotzdem nur über den Modul-Endpunkt, Dateikennungen nicht erratbar.“ Konvention bzw. AD-7 ergänzen: „Personendaten von Mitarbeitenden erscheinen öffentlich nur mit Einwilligung (Modul Personen), die die Freizeitseite bei jeder Auslieferung prüft.“

---

### B-13 – Teilnahmebedingungen: Nachweis der Bestätigung hat keinen Besitzer · **mittel**

**PRD-Stelle:** FR-19.

**Was fehlt:** Die digitale Bestätigung ersetzt Papier, ist also ein Nachweis (wer, wann, welche Fassung). Weder Datenbesitz noch Löschfrist ist festgelegt.

**Fix-Vorschlag:** Datenbesitz Freizeiten: „Anmeldung inkl. bestätigter Fassung der Teilnahmebedingungen (Version, Zeitpunkt, bestätigende Person)“. Teilnahmebedingungen werden versioniert, nie überschrieben; Löschung mit der Anmeldung nach Org-Frist.

---

### B-14 – Datenauskunft als PDF gegen Druck-Konvention · **mittel**

**PRD-Stelle:** FR-14 (Export als PDF oder CSV).

**Was widerspricht:** Konvention „Druck: keine PDF-Bibliothek“. Ein PDF-Export ist so nur über „Drucken → Als PDF speichern“ möglich; das ist nicht festgelegt, und wer die modulübergreifende Auskunft zusammensetzt, auch nicht (AD-16 sagt nur „jedes Modul liefert“).

**Fix-Vorschlag:** Konvention ergänzen: „Datenauskunft: CSV/JSON serverseitig je Modul; PDF über Druck-CSS des Browsers.“ AD-16 ergänzen: „Die Gesamtauskunft setzt die Oberfläche bzw. ein Host-Endpunkt aus den Verträgen aller Module zusammen (kein Modul aggregiert, AD-2).“

---

### B-15 – Nachweis-Einsicht „automatisch angefordert“ gegen AD-13 unklar · **niedrig**

**PRD-Stelle:** FR-10, NS-3 Schritt 5.

**Was fehlt:** Die Anforderung entsteht automatisch mit der Mitarbeit. Ob dabei eine Mail rausgeht, ist offen; AD-13 verbietet Mails ohne bestätigte Benutzeraktion.

**Fix-Vorschlag:** AD-13 ergänzen: „Die automatische Einsicht-Anforderung erscheint nur in der App; eine Mail entsteht erst, wenn die Leitung ‚erinnern‘ drückt. Die Bestätigung der Mitarbeit durch die Leitung gilt nicht als Versand-Bestätigung.“

---

### B-16 – Zeitzone für „Ende des letzten Freizeittages“ · **niedrig**

**PRD-Stelle:** FR-24c, FR-45 (ab Startdatum „läuft“), FR-64.

**Was fehlt:** Konvention speichert UTC, Datumsangaben als `date`. Wann ein Tag endet bzw. beginnt (Freigabe-Ende, Phase, QR-Ablauf), ist nicht festgelegt.

**Fix-Vorschlag:** Konvention „Zeit“ ergänzen: „Tagesgrenzen fachlicher Fristen (Phase, Freigabefenster, QR, Offline-Ablauf) gelten in Europe/Berlin; Berechnung nur in `Freizeiten.Contracts` bzw. einer `SharedKernel`-Hilfe.“

---

### B-17 – Endgültiges Löschen gegen Backups · **niedrig**

**PRD-Stelle:** NFR-5, FR-14.

**Was fehlt:** AD-16 verlangt endgültiges Löschen; Backups enthalten gelöschte Daten weiter. Die Zurückgestellt-Zeile „Backup-Strategie“ nennt das nicht.

**Fix-Vorschlag:** Zurückgestellt-Zeile Hosting/Backup ergänzen: „Backup-Aufbewahrung begrenzen (z. B. 30 Tage) und bei Wiederherstellung den Lösch-Dienst sofort laufen lassen.“

---

### B-18 – Push-Transport über Dienste außerhalb der EU · **niedrig**

**PRD-Stelle:** NFR-3, FR-61.

**Was fehlt:** Web-Push läuft über die Push-Dienste von Google, Apple, Mozilla. Die Nutzlast ist nach Standard Ende-zu-Ende verschlüsselt, das ist aber nicht festgehalten; Nachrichtentexte können Namen enthalten.

**Fix-Vorschlag:** AD-21 ergänzen: „Nutzlast immer verschlüsselt (RFC 8291); bei Kanal-Nachrichten nur Kanal und kurzer Text, keine Anhänge.“ Im Datenschutzmodell (OP-6) vermerken.

---

### B-19 – Global Admin ohne Datenzugriff nicht festgehalten · **niedrig**

**PRD-Stelle:** Rollentabelle (Global Admin: Organisationen anlegen, ersten Org-Admin einladen).

**Was fehlt:** AD-6 erlaubt das Umgehen des Mandantenfilters nur im Lösch-Dienst – gut. Aber ob der Global Admin Org-Daten lesen darf, steht nicht da.

**Fix-Vorschlag:** In AD-12 aufnehmen: „Global Admin hat keinen Mandanten-Kontext und sieht nur Organisations-Stammdaten.“

---

### B-20 – Kleinere Datenbesitz-Lücken · **niedrig**

**PRD-Stelle:** FR-23 (Angaben älter als 12 Monate), FR-29 (Ansprechperson am Heimatort), FR-7 (automatische Org-Mitgliedschaft).

**Was fehlt:**
- Der Zeitpunkt „zuletzt bestätigt“ je Antwort muss unverschlüsselt neben der verschlüsselten Antwort liegen (AD-14: keine Suche in verschlüsselten Feldern).
- „Ansprechperson am Heimatort“ hat keinen Besitzer (Sicherheit oder Freizeiten).
- FR-7: ob Freizeiten die Mitgliedschaft per Aufruf (`FZ → ORG`, erlaubt) oder per Ereignis anlegt, ist offen – egal, aber festlegen.

**Fix-Vorschlag:** Datenbesitz ergänzen: Personen „Antwort-Metadaten (bestätigt am) unverschlüsselt“; Sicherheit „Sicherheitskette inkl. Ansprechperson am Heimatort“; FR-7: Ereignis `AnmeldungBestaetigt` → Organisationen legt Mitgliedschaft idempotent an.

---

## Modul-Besitz je FR – Plausibilität

| FR | Zuordnung im Gerüst | Bewertung |
| --- | --- | --- |
| FR-1–4 | Konten, Personen | plausibel |
| FR-5–7 | Organisationen, Freizeiten | plausibel (FR-7 Weg festlegen, B-20) |
| FR-8–13 | Personen, Organisationen | plausibel; Protokoll bei Nachweis-Einsicht fehlt (B-8) |
| FR-14 | alle | plausibel; Aggregation/PDF offen (B-14) |
| FR-15–21, 66–68 | Freizeiten | plausibel; Gast-Link fehlt (B-3), Teilnahmebedingungen (B-13) |
| FR-22–26 | Personen, Freizeiten | plausibel; Einschränkung auf Fragen fehlt (B-2) |
| FR-27 | Oberfläche aus Modul-Endpunkten | plausibel |
| FR-28 | „Suche in Freizeiten“ | **nicht plausibel** → Personen (B-11) |
| FR-29–31, 33 | Sicherheit | plausibel; Verschlüsselung fehlt (B-5) |
| FR-32 | Sicherheit | **Weg zu Inventar fehlt** (B-1) |
| FR-34–36, 39, 41, 69 | Programm | plausibel |
| FR-40 | Programm (Vorlagen aber in Organisationen) | **Besitz-Widerspruch** (B-1) |
| FR-37–38 | Inventar | plausibel; Freizeit-Kontext fehlt (B-1) |
| FR-43–45 | Freizeiten | plausibel; öffentliche Dateien (B-12) |
| FR-46–53 | Küche | plausibel; Zugang zu Freizeiten fehlt (B-1) |
| FR-54–60 | Finanzen | plausibel |
| FR-61, 63 | Kanäle | plausibel; QR-Push (B-6) |
| FR-62 | Kanäle (Oberfläche fragt nach Programm-Änderung) | plausibel, da Oberfläche orchestriert (AD-2) |
| FR-64 | Freizeiten | plausibel; QR-Prinzipal in Programm/Kanäle (B-7) |
| FR-65 | Web.Client, Sicherheit, Küche | plausibel; Löschzeitpunkt (B-10) |

NFR-12 bis NFR-17 (Bedienung) haben keine Architekturfolge über die bestehenden Konventionen (keine Pop-ups, Autosave, Inline-Fehler) hinaus – kein Befund.
