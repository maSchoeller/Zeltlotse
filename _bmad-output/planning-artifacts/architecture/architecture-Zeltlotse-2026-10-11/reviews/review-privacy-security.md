---
review: privacy-security
target: ../ARCHITECTURE-SPINE.md
context: ../../../prds/prd-Zeltlotse-2026-10-10/prd.md (Rollen, NFR-1..7)
date: '2026-10-11'
lens: 'Datenschutz und Sicherheit (Art. 9 DSGVO, Minderjährige, Führungszeugnisse, EU-Betrieb)'
---

# Prüfbericht Datenschutz und Sicherheit – Architektur-Gerüst Zeltlotse

## Urteil

Das Gerüst hat die richtigen Grundpfeiler (Datenbesitz bei der Person, Freigabe per Durchgriff, ein Token-Muster, Cookie statt Tokens im Browser, Verschlüsselung der Kernfelder, keine Personendaten in Logs). **Für den bekannten Kreis ist es mit den Korrekturen der Befunde K-1 bis K-3 und H-1 bis H-5 tragfähig; vor dem Start als Dienst für fremde Organisationen fehlen noch mehrere Invarianten.** Die größten Lücken: Das Modul Personen hat keinen Mandantenfilter und die Freigabeprüfung kennt die Rolle der lesenden Person nicht (K-1). Außerdem deckt AD-14 nur einen Teil der Gesundheitsdaten ab (K-2).

**Schwere-Skala:** *Kritisch* = Art.-9-Daten oder Führungszeugnisse können bei normalem Bau unbefugt abfließen. *Hoch* = realistischer Missbrauch oder Verstoß gegen Löschpflicht. *Mittel* = Härtung, die alle Module betrifft. *Niedrig* = gehört als Zurückgestellt-Zeile oder offene Frage ins Gerüst.

**Kurzbegriffe:** *CSRF* = Angriff, bei dem eine fremde Webseite den Browser einer angemeldeten Person unbemerkt Aktionen auslösen lässt. *CSP (Content Security Policy)* = Browser-Regel, welche Skripte und Quellen eine Seite laden darf. *Crypto-Shredding* = Daten unlesbar machen, indem man ihren Schlüssel löscht. *DEK (Data Encryption Key)* = Schlüssel, der Daten verschlüsselt und selbst mit einem Hauptschlüssel verschlüsselt ist.

## Übersicht

| # | Befund | Ort | Schwere |
| --- | --- | --- | --- |
| K-1 | Personen ohne Mandantenfilter; Freigabeprüfung kennt die Rolle der lesenden Person nicht | AD-6, AD-7, AD-8, AD-2 | Kritisch |
| K-2 | Verschlüsselung deckt Vorfall-Protokoll, Vorfall-Notiz, Team-Notizen, Kanäle, Belege nicht ab | AD-14 | Kritisch |
| K-3 | Offline-Speicher und Service Worker: Klartext-Cache, geteilte Handys, Löschzeitpunkt | AD-18 | Kritisch |
| H-1 | Zugangslinks: Zweck-Matrix fehlt (Einmal/Mehrfach, Bindung, Widerruf, Rate-Limit) | AD-11 | Hoch |
| H-2 | Login-Link wird von Mail-Scannern vorab verbraucht oder ausgelöst | AD-11, AD-12 | Hoch |
| H-3 | Mitarbeiter-Link und Org-Einladung: Weitergabe führt zu Team-/Admin-Rechten | AD-11, AD-12 | Hoch |
| H-4 | Backups und S3-Versionen widersprechen dem endgültigen Löschen | AD-16, AD-14, AD-23 | Hoch |
| H-5 | Durchgriffs-Ausnahmen und Massenlesen ohne Protokollregel | AD-8 | Hoch |
| H-6 | Cookie, CSRF und Antiforgery für Minimal APIs nicht festgelegt | AD-12 | Hoch |
| M-1 | Datei-Uploads: Typprüfung, Größe, Metadaten, Auslieferung; Widerspruch „öffentliche Dokumente“ | AD-15 | Mittel |
| M-2 | Push-Inhalte: Freitext im Kanal ist potenziell Gesundheitsdatum | AD-21 | Mittel |
| M-3 | Global Admin und Betreiber: Zugriffsgrenzen nicht festgelegt | AD-12, Konten | Mittel |
| M-4 | Kein Sicherheits-Ereignisprotokoll (Anmeldungen, Rollenwechsel, Exporte) | AD-22 | Mittel |
| M-5 | Logs: Tokens in URLs, EF-Parameterwerte, Telemetrie-Ziel | AD-22 | Mittel |
| M-6 | Zweiter Elternteil, Profil-Übergabe, Gast-Profile: Rechte und Zusammenführen | AD-7 | Mittel |
| M-7 | Sicherheits-Header und CSP fehlen als Konvention | Konventionen | Mittel |
| M-8 | Datenauskunft und Lösch-Handler brauchen mandantenübergreifenden Zugriff (Widerspruch zu AD-6) | AD-6, AD-16 | Mittel |
| N-1 | Abhängigkeits- und Container-Updates ohne Regel; CI/CD zu spät ausgelöst | Zurückgestellt | Niedrig |
| N-2 | Schlüsselrotation braucht jetzt schon Schlüssel-Kennung im Chiffrat | AD-14, Zurückgestellt | Niedrig |
| N-3 | Mail-Inhalte und Outbox-Zeilen mit Personendaten | AD-4, AD-13 | Niedrig |
| N-4 | Offene Fragen für OP-6 (Vorfall-Archiv, Einsicht-Protokoll, Malware-Scan, Betreiberzugriff) | Zurückgestellt | Niedrig |

---

## Kritisch

### K-1 – Personen ohne Mandantenfilter; Freigabeprüfung ohne Rollenwissen

- **Ort:** AD-6, AD-7 („Daten im Modul Personen tragen keine `OrganisationId`“), AD-8, AD-2 (Pfeil `FZ --> PER`).
- **Risiko:**
  1. AD-6 schützt nur organisationseigene Zeilen. Das Modul mit den sensibelsten Daten (Freizeitpässe, Führungszeugnisse) hat damit **keinen** automatischen Schutzfilter. Ein einziger Endpunkt, der die Freigabeprüfung vergisst, gibt Profile aller Organisationen preis.
  2. Die Freigabe kennt die Stufen *Leitung* und *Team*. Ob die lesende Person in dieser Freizeit Leitung oder Team ist, weiß aber nur das Modul Freizeiten. Personen darf Freizeiten laut AD-2 nicht aufrufen. Bleibt nur, dass der Aufrufer die Rolle mitliefert. Dann vertraut Personen einer Behauptung des aufrufenden Moduls. Prüfung und Protokoll (AD-8) wären nur so gut wie jeder einzelne Aufrufer.
  3. Endet die Mitarbeit (Teammitglied entfernt) oder wird eine Anmeldung abgesagt, ist nicht geregelt, dass die Freigabe sofort endet.
  4. Nicht festgelegt ist, dass Org-Admin und Org-Überblick **nie** über ihre Org-Rolle Freizeitpass-Daten sehen (PRD-Rollentabelle: „keine Freizeit-Inhalte“).
- **Fix (AD-Regel, Ergänzung AD-8 und neue Zeile in AD-7):**
  - *AD-7 ergänzen:* „Jede Abfrage im Modul Personen läuft über einen globalen Zugriffsfilter: Standard ist *kein Zugriff*. Zugriff haben nur die Person selbst, ihre Sorgeberechtigten im Familienkonto oder ein Leser mit gültiger Freigabe. Ausnahmen wie in AD-6 nur für Lösch-Dienst und Datenauskunft.“ Architekturtest: Jeder Personen-Endpunkt nutzt die zentrale Zugriffsrichtlinie.
  - *AD-8 ergänzen:* „Personen hält eine eigene Projektion der Freizeit-Rollen (Person X ist Leitung/Team in Freizeit Y, seit/bis). Sie wird über Ereignisse aus `Freizeiten.Contracts.Events` gepflegt (`LeitungEingesetzt`, `MitarbeitBestaetigt`, `MitarbeitBeendet`, `AnmeldungAbgesagt`, `FreizeitAbgeschlossen`). Die Freigabeprüfung nutzt die Kennung des angemeldeten Kontos aus dem Anfrage-Kontext und diese Projektion, **nie eine Rollenangabe des Aufrufers**. `AnmeldungAbgesagt` und `MitarbeitBeendet` beenden die zugehörige Freigabe bzw. Leserechte sofort. Org-Rollen begründen nie eine Freigabe.“
  - Hinweis: Die Projektion liegt in der Gegenrichtung des Pfeils, das ist laut AD-2/AD-4 erlaubt (Ereignisse). Eventual Consistency (Daten werden erst mit kurzer Verzögerung übernommen) gibt es nur beim Gewähren. Für das Entziehen sollte das Ereignis vor dem Bestätigen der Absage zugestellt sein oder die Prüfung einmal synchron nachfragen. Das muss die Regel festlegen.

### K-2 – AD-14 verschlüsselt nur einen Teil der Art.-9-Daten

- **Ort:** AD-14 (Binds nur Personen), Datenbesitz-Tabelle.
- **Risiko:** Gesundheitsdaten Minderjähriger und vergleichbar sensible Daten liegen außerhalb von Personen **im Klartext** in Datenbank und Backups:
  - *Vorfall-Protokoll* (Sicherheit): „welches Medikament wann gegeben“ ist ein Gesundheitsdatum (Art. 9). Es wird zudem offline geschrieben.
  - *Archivierte Vorfall-Notiz* (Sicherheit): lebt nach der Freizeit bis zur Löschfrist der Organisation weiter, also länger als jede Freigabe.
  - *Team-Notizen zu Teilnehmenden* (Freizeiten, FR-26): enthalten in der Praxis Verhalten, Heimweh, Medikamente, familiäre Lage.
  - *Kanal-Nachrichten und Besprechungsnotizen* (Kanäle): Freitext, in dem „Lea hat Fieber“ stehen wird.
  - *Belege* (Finanzen, Fotos): Apothekenquittungen, Namen, ggf. Bankdaten; Foto-Metadaten (Ort).
  - *Küchen-Ableitungen* (Allergien/Sonderkost in Tagesansicht und Einkaufsliste), falls Küche sie zwischenspeichert.
  - *Nachweis-Bestätigung* (Organisationen): „Führungszeugnis eingesehen, gültig bis“ ist für sich keine Straftatdaten-Angabe nach Art. 10, aber „abgelehnt mit Hinweis“ (FR-10) kann es werden.
- **Fix (AD-14 neu fassen):** „Anwendungsseitig verschlüsselt werden in **jedem Modul** alle Felder der Schutzklasse *besonders* : Freizeitpass-Antworten, Nachweis-Dateien, Vorfall-Protokoll und Vorfall-Notiz, Team-Notizen zu Personen, Kanal-Nachrichten und Besprechungsnotizen, Beleg-Dateien, Ablehnungshinweise bei Nachweisen. Die Verschlüsselung stellt `SharedKernel` als Dienst bereit (ein Verfahren für alle). Felder werden per Attribut `[Besonders]` markiert; ein Architekturtest prüft, dass markierte Felder nur über den verschlüsselnden Wert-Konverter gespeichert werden. Abgeleitete Ansichten speichern keine Kopie (AD-8).“ Binds auf Sicherheit, Freizeiten, Kanäle, Finanzen, Organisationen erweitern.
- **Ergänzung AD-22:** markierte Felder dürfen auch nicht in Fehlermeldungen/ProblemDetails erscheinen (gleiches Attribut nutzen).

### K-3 – Offline-Speicher und Service Worker

- **Ort:** AD-18, AD-5, Quellbaum `wwwroot/service-worker.js`.
- **Risiko:**
  1. **Klartext im Cache:** Ein Service Worker, der für Offline-Fähigkeit API-Antworten in der *Cache Storage* ablegt, umgeht die IndexedDB-Verschlüsselung vollständig. Das ist der übliche Weg, wie Gesundheitsdaten unverschlüsselt auf Geräten landen.
  2. **Geteilte Handys:** Ein nicht exportierbarer WebCrypto-Schlüssel schützt nur gegen Auslesen des Speichers von außen. Wer dasselbe Handy und dasselbe Browser-Profil nutzt (Küchen-Handy, Leitungs-Handy, Handy der Eltern), kann die Daten in der App entschlüsseln lassen. Abmelden löscht laut AD-18 nichts.
  3. **Löschzeitpunkt:** AD-18 sagt „Ende des Freigabefensters bzw. der Freizeit“, FR-65 sagt „nach Freizeitende“. Das Freigabefenster kann Monate länger laufen. Ein Gerät, das offline bleibt oder die App nicht mehr öffnet, bekommt kein Lösch-Signal vom Server.
  4. **Entzug:** Wird ein Teammitglied entfernt oder ein Gerät verloren, behält das Gerät die Notfallkarte bis zum Ablauf. Das ist unvermeidbar, aber nicht als bewusste Entscheidung festgehalten.
  5. Offline-Synchronisation ist ein Massenlesen von Gesundheitsdaten und muss protokolliert werden (siehe H-5).
- **Fix (AD-18 ergänzen):**
  - „Der Service Worker cacht nur statische App-Dateien. Alle `/api/`-Antworten tragen `Cache-Control: no-store` und werden nie in Cache Storage abgelegt. Offline-Daten liegen ausschließlich verschlüsselt in IndexedDB.“
  - „Offline-Daten und Schlüssel sind an das Konto gebunden und werden beim Abmelden, beim Kontowechsel und beim Ende der Sitzung gelöscht.“
  - „Jeder Offline-Datensatz trägt ein lokales Ablaufdatum = Ende der Freizeit (nicht des Freigabefensters). Die App löscht abgelaufene Daten bei jedem Start, auch ohne Netz. Die Notfallkarte enthält nur die für den Notfall nötigen Felder (Datensparsamkeit), nicht den vollständigen Freizeitpass.“
  - Zurückgestellt-Zeile: „Gerätesperre/PIN für Offline-Daten auf geteilten Geräten – Auslöser: Baustein V6.“
  - Restrisiko „verlorenes Gerät behält Daten bis Freizeitende“ auf der Vertrauensseite (FR-66) nennen.

---

## Hoch

### H-1 – Zugangslinks: Zweck-Matrix fehlt

- **Ort:** AD-11.
- **Risiko:** AD-11 legt Zufälligkeit und Hash-Speicherung fest, aber nicht die Eigenschaften, die über Missbrauch entscheiden. Damit baut jeder Baustein sie anders, also genau das, was AD-11 verhindern soll:
  - *Anmeldelinks* sind öffentlich teilbar und mehrfach nutzbar. Wer den Link hat, kann beliebig viele Anmeldungen mit erfundenen Daten absenden. Das sind Spam, Platzblockade bei „ausgebucht“ (FR-67) und Kosten für den Mail-Versand.
  - *QR-Teilnehmerzugang* ist ein Foto, das in Klassenchats landet. Wochenübersicht = **wo und wann sich eine Gruppe Minderjähriger aufhält**. Das ist ein Kinderschutz-Risiko, nicht nur ein Datenschutz-Risiko. AD-11 kennt keinen Widerruf und keine Rotation (Erneuerung des Codes).
  - Nicht im Muster: der *Gast-Link* aus FR-68 (Elterninfo per Mail) und Gast-Zugriff auf Status, Datenauskunft und Löschung eigener Gast-Daten sowie die *Nachweis-Einsicht* (AD-9 „einmalige, befristete Freigabe“).
  - Tokens in URLs gelangen über den `Referer`-Header an fremde Seiten und in Zugriffs-Logs (siehe M-5).
- **Fix (AD-11 erweitern um eine Tabelle, die verbindlich ist):**

  | Zweck | Nutzung | Ablauf | Gebunden an | Widerruf |
  | --- | --- | --- | --- | --- |
  | Login-Link | einmalig | 15 min | Mail-Adresse des Kontos | durch Nutzung / neuen Link |
  | Org-Einladung, Profil-Übergabe, Elternteil-Einladung | einmalig | 7 Tage | eingeladene Mail-Adresse; Annahme nur nach Login mit dieser Adresse | durch Einladende |
  | Anmeldelink TN / MA | mehrfach | Anmeldeschluss | Freizeit | Leitung kann neu erzeugen |
  | Gast-Link (FR-68) | mehrfach | Freigabefenster | eine Anmeldung | mit Freizeit/Löschung |
  | QR-Teilnehmerzugang | mehrfach, nur lesen | Freizeitende | Freizeit | Leitung kann jederzeit neu erzeugen (alter Code ungültig) |
  | Nachweis-Einsicht | gebunden an Konto der prüfenden Person, kein Link | einmalig | Konto | durch Person |

  Zusätzlich als Regel: „Alle Token-Endpunkte und das Anfordern von Login-Links laufen über den ASP.NET-Core-Rate-Limiter (pro IP und pro Ziel). Antworten verraten nicht, ob ein Konto existiert. Token werden beim ersten Aufruf gegen ein Sitzungs-Cookie getauscht und die URL ohne Token neu geladen; `Referrer-Policy: no-referrer`.“
- **Zurückgestellt bzw. offene Frage:** Inhalt des QR-Zugangs: Sollen Ort/Zeit von Programmpunkten außerhalb des Geländes (Ausflug) im QR-Zugang sichtbar sein? Fachliche Entscheidung, Auslöser V6.

### H-2 – Login-Link und Mail-Scanner

- **Ort:** AD-11, AD-12, FR-2.
- **Risiko:** Sicherheits-Scanner in Firmen- und Schul-Mailsystemen (Microsoft Defender „Safe Links“, Proofpoint u. a.) rufen Links vorab auf. Löst ein `GET` die Anmeldung aus, wird der Einmal-Link verbraucht (Nutzer kommt nicht rein) oder, schlimmer, der Scanner erhält eine gültige Sitzung. Dasselbe betrifft Org-Einladung, Profil-Übergabe und die Bestätigung von Anmeldungen per Link.
- **Fix (AD-11 Regel):** „Kein Token-Link verändert bei `GET` einen Zustand. Ein `GET` zeigt eine Bestätigungsseite (statisches SSR); erst ein `POST` mit Antiforgery-Token von dieser Seite verbraucht das Token und setzt die Sitzung.“ Optional (als Zurückgestellt-Zeile): Login-Link nur im selben Browser einlösbar, in dem er angefordert wurde, sonst zusätzlich 6-stelliger Code. Auslöser: erste Nutzerbeschwerden oder Start als Dienst.

### H-3 – Weitergegebene Mitarbeiter-Links und Einladungen

- **Ort:** AD-11, AD-12, FR-5, FR-7, FR-16, FR-24b.
- **Risiko:** Das Team sieht **alle vollständigen Freizeitpässe** (FR-24b). Der Mitarbeiter-Link ist öffentlich teilbar. Ist nicht hart festgelegt, dass eine Mitarbeiter-Anmeldung erst durch Bestätigung der Leitung Team-Rechte erzeugt, öffnet ein weitergeleiteter Link Gesundheitsdaten von Kindern für Unbekannte. Die Org-Einladung macht den Empfänger zum Org-Admin. Wird sie weitergeleitet, wird eine fremde Person Admin.
- **Fix (AD-12 ergänzen):** „Rollen entstehen nie allein durch Einlösen eines mehrfach nutzbaren Links. Team-Rechte entstehen erst mit der Bestätigung der Mitarbeit durch die Leitung (Ereignis `MitarbeitBestaetigt`). Org-Rollen entstehen nur durch eine an eine Mail-Adresse gebundene Einladung (AD-11).“ Offene Frage für das PRD: Soll die Bestätigung einer Mitarbeit gesperrt sein, solange Pflicht-Nachweise (Führungszeugnis) fehlen oder abgelaufen sind? Das hat Folgen für Freigabe und Kinderschutz (§ 72a SGB VIII). Gehört als offene Frage in die Zurückgestellt-Tabelle bzw. als OP ins PRD.

### H-4 – Backups und S3-Versionen gegen endgültiges Löschen

- **Ort:** AD-16 („endgültig gelöscht“), AD-14, AD-23, Zurückgestellt „Backup-Strategie“.
- **Risiko:** Gelöschte Freizeitpässe, Führungszeugnisse und Vorfall-Notizen leben in Datenbank-Backups und, falls Versionierung aktiv ist, in alten S3-Objektversionen weiter. Bei einer Wiederherstellung kommen gelöschte Daten zurück. Bei einem einzigen Schlüssel für alles (AD-14) hilft Verschlüsselung dagegen nicht.
- **Fix:**
  - *AD-14 ergänzen (Entscheidung jetzt, weil das Speicherformat davon abhängt):* „Umschlag-Verschlüsselung: Jede Person (bzw. jeder Freizeit-Datenbestand für Daten der Freizeit) hat einen eigenen Datenschlüssel (DEK), verschlüsselt mit dem Hauptschlüssel aus der Geheimnisverwaltung. Löschen einer Person löscht ihren DEK (Crypto-Shredding). Damit sind Kopien in Backups unlesbar.“
  - *AD-16 ergänzen:* „Der Lösch-Dienst führt ein Lösch-Journal (nur Kennungen und Zeitpunkt). Nach jeder Wiederherstellung aus einem Backup werden alle Einträge des Journals erneut angewandt, bevor die App wieder Anfragen annimmt. Der Dateispeicher läuft ohne Objekt-Versionierung oder mit einer Lebenszyklus-Regel, die alte Versionen nach höchstens der Backup-Aufbewahrung entfernt.“
  - *Zurückgestellt-Zeile präzisieren:* „Backup-Aufbewahrung (z. B. 30 Tage), verschlüsselt, EU; Frist wird auf der Vertrauensseite genannt. Auslöser: vor der ersten echten Freizeit.“

### H-5 – Durchgriffs-Ausnahmen und Protokollierung

- **Ort:** AD-8 (Ausnahmen Notfallordner, Offline-Notfallkarte, Küchenansicht), FR-48 (Allergen-Abgleich gegen **alle** Freizeitpässe), FR-13, Zurückgestellt „Aufbewahrung des Einsicht-Protokolls“.
- **Risiko:**
  - Die Ausnahmen sind „abgeleitete Ansichten“, aber es ist nicht festgelegt, dass auch sie über `Personen.Contracts` laufen und protokolliert werden. Der Notfallordner-Druck erzeugt Papier, das Zeltlotse nie löschen kann.
  - Küche liest für den Allergen-Abgleich Daten aller Personen. Entweder wird jedes Lesen pro Person protokolliert (dann ist das Protokoll für Eltern nutzlos: „Küche hat 400-mal gelesen“) oder gar nicht (dann Lücke gegen NFR-1).
  - Die Küche bekommt über diesen Weg mehr als nötig, wenn Personen den ganzen Freizeitpass liefert.
  - Protokoll ohne Manipulationsschutz: Wer DB-Schreibrechte hat (Betreiber), kann Einträge entfernen.
- **Fix (AD-8 ergänzen):** „`Personen.Contracts` bietet je Zweck eine eigene Leseschnittstelle mit festem Feldumfang: *Freizeitpass vollständig* (Leitung/Team), *Notfallangaben* (Notfallordner, Notfallkarte), *Allergene und Sonderkost* (Küche, nur Fragen-Kennungen aus AD-10 mit Allergen-Bezug). Jeder Aufruf wird protokolliert mit wer, wessen, wann, welche Freizeit, **Zweck** (ansehen, drucken, offline laden, Küchenabgleich, Export). Massenzugriffe werden als ein Eintrag pro Person und Zweck und Tag zusammengefasst. Das Protokoll ist nur anhängend (kein Update/Delete außer durch den Lösch-Dienst nach Frist).“ Druck des Notfallordners als eigener Protokoll-Zweck; Hinweis auf der Vertrauensseite, dass Ausdrucke nach der Freizeit zu vernichten sind.

### H-6 – Cookie, CSRF, Antiforgery

- **Ort:** AD-12, AD-5, Konvention „Endpunkte: Minimal APIs“.
- **Risiko:** Mit Cookie-Anmeldung sendet der Browser das Cookie bei jeder Anfrage an die eigene Domain mit, auch bei Anfragen, die eine fremde Seite auslöst. Minimal-API-Endpunkte prüfen Antiforgery nur bei Formular-Bindung automatisch, nicht bei JSON. Die öffentlichen SSR-Formulare (Gast-Anmeldung, Token-Bestätigung) brauchen Antiforgery. Ohne Festlegung entscheidet jedes Modul selbst. Weitere offene Punkte: Sitzungsdauer auf geteilten Geräten, Abmelden aller Geräte, erneute Bestätigung vor besonders heiklen Aktionen.
- **Fix (AD-12 ergänzen):**
  - „Sitzungs-Cookie: Name mit Präfix `__Host-`, `Secure`, `HttpOnly`, `SameSite=Lax` (Strict bricht den Login-Link-Ablauf). Sitzung gleitend 14 Tage, absolut 30 Tage; Prüfung des Security-Stamps alle 5 Minuten, damit ‚überall abmelden‘ und Passwortänderung greifen.“
  - „Alle zustandsändernden `/api/`-Endpunkte verlangen einen Antiforgery-Header (`RequestVerificationToken`, vom Client per JS-Interop gesetzt) und `Content-Type: application/json`. Eine gemeinsame Endpunkt-Filter-Gruppe in `Web` erzwingt das für alle `Map<Modul>Endpoints()`; Architekturtest bzw. Integrationstest prüft jeden Nicht-GET-Endpunkt. SSR-Formulare nutzen das Standard-Antiforgery.“
  - „API antwortet bei fehlender Anmeldung mit 401/403 ProblemDetails, nie mit Redirect.“
  - „Erneute Anmeldung (Login-Link oder Passwort, max. 10 Minuten alt) vor: Datenexport, Konto löschen, Profil-Übergabe, Freigabe von Nachweisen, Hinzufügen eines Elternteils, Ernennen von Org-Admins.“
  - Kein CORS (Cross-Origin Resource Sharing, Zugriff fremder Domains) aktivieren; die App ist same-origin.

---

## Mittel

### M-1 – Datei-Uploads

- **Ort:** AD-15, FR-8 (Nachweise), FR-43 (Dokumente, Sichtbarkeit „öffentlich“), FR-58 (Belege als Foto).
- **Risiko:** Hochgeladene HTML-/SVG-Dateien, die über die eigene Domain ausgeliefert werden, führen Skripte im Kontext der App aus (Sitzungsdiebstahl). Fotos tragen Standort und Gerät in den EXIF-Metadaten (Bild-Zusatzdaten), bei Belegen und Dokumenten von Minderjährigen heikel. Ohne Größen- und Mengenlimit füllt eine Person den Speicher. Malware in PDF-Nachweisen landet beim Prüfenden. Außerdem widerspricht AD-15 („keine öffentlichen URLs, nur mit Rechteprüfung“) FR-43 („öffentlich auf der Freizeitseite“).
- **Fix (AD-15 ergänzen, Umsetzung im `SharedKernel`-Datei-Adapter, damit alle Module es erben):** „Erlaubt sind nur PDF, JPEG, PNG, HEIC/WebP (Prüfung am Dateiinhalt, nicht an Endung oder angegebenem Typ). Größe max. 10 MB je Datei; Kontingent je Person und je Freizeit. Bilder werden serverseitig neu kodiert und von Metadaten befreit. Auslieferung mit `Content-Disposition: attachment` (außer Bildvorschau), `X-Content-Type-Options: nosniff`, eigener `Content-Security-Policy: sandbox`. Dateinamen werden nie als Speicherpfad verwendet. Sichtbarkeit ‚öffentlich‘ ist eine Rechteprüfung, die ohne Anmeldung zustimmt; sie bleibt hinter dem App-Endpunkt (keine S3-URL).“
- **Zurückgestellt-Zeile:** „Virenprüfung von Uploads (z. B. ClamAV-Container) – Auslöser: Start als Dienst für fremde Organisationen.“

### M-2 – Push-Inhalte

- **Ort:** AD-21 („Push-Inhalte enthalten keine Gesundheitsdaten“), FR-61.
- **Risiko:** Kanal-Nachrichten sind Freitext von Mitarbeitenden. Die Regel lässt sich technisch nicht einhalten, solange der Nachrichtentext im Push steht. Web-Push-Inhalte sind auf dem Transportweg verschlüsselt, werden aber auf dem **Sperrbildschirm** angezeigt (geteilte Handys, Handys von Teilnehmenden). Push-Abos von Teilnehmenden ohne Konto (QR) sind personenbezogene Daten ohne Datenbesitzer.
- **Fix (AD-21 neu fassen):** „Push für den Kanal *Team* enthält nur Freizeit, Kanal und Absender-Vorname, keinen Nachrichtentext; der Text wird nach dem Öffnen geladen. Push für *Alle* darf den Text enthalten, weil er auch für Teilnehmende sichtbar ist. Push-Abos des QR-Zugangs gehören der Freizeit und werden mit Freizeitende gelöscht.“ Alternativ, falls UX den Text im Team-Push will: als bewusste Entscheidung mit Hinweis im Kanal („Keine Gesundheitsangaben in Kanälen“) festhalten.

### M-3 – Global Admin und Betreiberzugriff

- **Ort:** Rollen (PRD), Konten „Global-Admin-Kennzeichen“, AD-6, AD-12, NFR-4 (Auftragsverarbeitung).
- **Risiko:** Nicht festgelegt ist, dass der Global Admin in der App **keinen** Zugriff auf Freizeit- oder Personendaten hat. Ohne Regel entsteht leicht ein „Admin sieht alles“-Pfad (Support, Fehlersuche). Als Auftragsverarbeiter darf der Betreiber Freizeitdaten nur auf Weisung verarbeiten. Technisch hat der Betreiber über Datenbank plus Hauptschlüssel ohnehin vollen Zugriff; das ist nicht dokumentiert.
- **Fix (AD-12 ergänzen):** „Global Admin ist eine reine Verwaltungsrolle: Organisationen anlegen, erste Einladung. Sie begründet keine Mandanten-Rolle, keine Freigabe und keine Umgehung des Mandantenfilters. Es gibt keinen Endpunkt ‚als Nutzer anmelden‘ (Impersonation). Ist der Betreiber selbst Mitglied einer Organisation, bekommt er dort Rechte nur über sein normales Konto.“
- **Zurückgestellt-Zeile:** „Betreiber-Zugriff auf Produktivdaten (Datenbank, Hauptschlüssel): Notfallzugang mit Vier-Augen-Prinzip und Protokoll; Trennung von DB-Admin und Schlüssel-Admin. Auslöser: Start als Dienst (OP-6). Bis dahin offen im AVV und auf der Vertrauensseite benennen.“

### M-4 – Kein Sicherheits-Ereignisprotokoll

- **Ort:** AD-22 verbietet Personendaten in Logs; ein Prüfprotokoll für sicherheitsrelevante Vorgänge fehlt.
- **Risiko:** Nach einem Vorfall (fremder Org-Admin, Kontoübernahme, Massenexport) lässt sich nicht nachvollziehen, was passiert ist. Meldepflicht nach Art. 33 DSGVO (72 Stunden) ist so kaum erfüllbar.
- **Fix (neue Regel, z. B. AD-25 „Sicherheitsprotokoll“):** „Konten und Organisationen schreiben ein nur anhängendes Sicherheitsprotokoll (nur Kennungen): Anmeldung erfolgreich/fehlgeschlagen, Login-Link angefordert, Passwort gesetzt, Rollen vergeben/entzogen, Einladung angenommen, Elternteil hinzugefügt, Profil übergeben, Datenexport, Konto gelöscht, Freigabe von Nachweisen. Aufbewahrung z. B. 12 Monate (Frist zurückgestellt mit OP-6).“ Das Einsicht-Protokoll (AD-8) bleibt davon getrennt und ist für die Person sichtbar.

### M-5 – Logs

- **Ort:** AD-22, `ServiceDefaults` (Telemetrie).
- **Risiko:** Standard-Request-Logs und Traces enthalten die URL samt Query. Damit landen Zugangs-Tokens (AD-11) im Log und gelten dort als Zugangsdaten. EF Core loggt bei Fehlern Parameterwerte, wenn `EnableSensitiveDataLogging` aktiv ist. OpenTelemetry-Exporter könnten auf einen Nicht-EU-Dienst zeigen. Log-Aufbewahrung ist offen.
- **Fix (AD-22 ergänzen):** „Tokens stehen nie in Query-Strings, die geloggt werden; Pfade mit Token werden vor dem Loggen maskiert (zentraler Filter in `ServiceDefaults`). `EnableSensitiveDataLogging` und detaillierte Fehlerseiten sind außerhalb von lokal verboten (Start-Prüfung). Telemetrie-Ziele liegen in der EU. Log-Aufbewahrung höchstens 30 Tage.“

### M-6 – Zweiter Elternteil, Profil-Übergabe, Gast-Profile

- **Ort:** AD-7, FR-3, FR-4, FR-17.
- **Risiko:**
  - Rechte zwischen zwei Elternteilen sind nicht festgelegt: Kann einer den anderen entfernen? Kann einer allein das Profil übergeben oder löschen? Bei Trennung kann ein Elternteil ohne Sorgerecht weiter Gesundheitsdaten des Kindes sehen. Patchwork ist laut PRD ausgeschlossen, Trennungsfälle aber nicht.
  - Löscht ein Elternteil sein Konto, ist offen, ob die Kinderprofile gelöscht werden (und laufende Anmeldungen damit wegfallen).
  - Gast-Anmeldung erzeugt ein Profil ohne Konto. Bietet Zeltlotse später an, „ein Konto aus den Daten zu erstellen“ (FR-17), oder ordnet es Gast-Profile über die Mail-Adresse bestehenden Familienkonten zu, kann jemand mit fremder Mail-Adresse Daten in ein fremdes Konto einschleusen oder fremde Daten sehen.
  - Übergabe mit 18: Wer die Mail-Adresse des Kindes kontrolliert (z. B. ein Elternteil), bekommt das Profil.
- **Fix (AD-7 ergänzen):** „Ein Kinderprofil hat ein oder mehrere Sorgeberechtigte mit gleichen Rechten. Jede Änderung an der Gruppe der Sorgeberechtigten und jede Übergabe wird allen Sorgeberechtigten angezeigt (Protokoll). Löscht ein Sorgeberechtigter sein Konto, bleiben Kinderprofile bei den übrigen; der letzte Sorgeberechtigte muss vorher übergeben oder löschen. Profile werden nie automatisch über Mail-Adresse oder Name zusammengeführt; ein Gast-Profil wird nur nach Anmeldung mit der beim Gast angegebenen Mail-Adresse an ein Konto gehängt.“
- **Zurückgestellt bzw. offene Frage:** „Entfernen eines Sorgeberechtigten gegen seinen Willen (Trennung, Sorgerechtsentzug) – Verfahren über den Betreiber-Support; Auslöser: erster Fall oder Start als Dienst.“

### M-7 – Sicherheits-Header und CSP

- **Ort:** Konsistenz-Konventionen (fehlt), AD-5.
- **Risiko:** Ohne Festlegung fehlen HSTS, Clickjacking-Schutz und CSP. Blazor WebAssembly braucht in der CSP `wasm-unsafe-eval`; wird das nicht früh geregelt, schaltet man später aus Bequemlichkeit `unsafe-eval`/`unsafe-inline` frei. Externe Ressourcen (Web-Fonts, CDNs) übertragen IP-Adressen an Dritte (DSGVO-Problem, vgl. Google-Fonts-Urteil LG München 2022).
- **Fix (neue Konventions-Zeile „Sicherheits-Header“):** „Zentral in `Web` gesetzt: `Strict-Transport-Security` (1 Jahr, includeSubDomains), `Content-Security-Policy: default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self'; img-src 'self' blob: data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, `Permissions-Policy` (Kamera nur `self` für Beleg-Fotos, sonst aus). Keine Ressourcen von Drittanbietern zur Laufzeit; Schriften und ELKW-CSS werden selbst ausgeliefert.“ Ein Integrationstest prüft die Header.

### M-8 – Datenauskunft und Lösch-Handler gegen AD-6

- **Ort:** AD-6 („`IgnoreQueryFilters` nur im Lösch-Dienst“), AD-16 (Datenauskunft und *Person gelöscht* in jedem Modul).
- **Risiko:** Eine Person arbeitet in mehreren Organisationen mit. Die Datenauskunft muss Daten aus allen Organisationen liefern, und der Handler für *Person gelöscht* muss in allen löschen. Beides geht nur ohne Mandantenfilter. Laut AD-6 ist das verboten. Folge: Entweder bleibt die Auskunft unvollständig (DSGVO-Verstoß), oder Module umgehen den Filter an unkontrollierten Stellen.
- **Fix (AD-6 ergänzen):** „Ausnahmen vom Mandantenfilter: Lösch-Dienst, Handler für *Person gelöscht* und die Implementierung des Vertrags *Datenauskunft*. Sie laufen in einem eigenen System-Kontext, filtern ausschließlich nach `PersonId` und sind per Architekturtest die einzigen Aufrufer von `IgnoreQueryFilters`.“ Gleiches gilt mit Row Level Security in Stufe 2 (eigene DB-Rolle für diesen Kontext).

---

## Niedrig (Zurückgestellt-Zeilen und offene Fragen)

### N-1 – Abhängigkeits- und Container-Updates

- **Ort:** Zurückgestellt „CI/CD-Pipeline – Auslöser erster Echtbetrieb“, `Directory.Packages.props`.
- **Risiko:** Ohne Prüfung bekannter Schwachstellen laufen veraltete Pakete und Basis-Abbilder in Produktion. Echte Gesundheitsdaten liegen ab der ersten echten Anmeldung im System, also schon vor dem „Echtbetrieb“ der Freizeit.
- **Fix (Konvention + Zurückgestellt präzisieren):** Konvention „Abhängigkeiten: `NuGetAudit` aktiv, Schwachstellen ab *hoch* brechen den Build; zentrale Versionen in `Directory.Packages.props`; Container-Basisabbild auf aktuellem .NET-Patchstand.“ Zurückgestellt-Zeile: „Automatische Update-Vorschläge (Renovate/Dependabot) und monatlicher Neubau des Container-Abbilds – Auslöser: **vor der ersten echten Anmeldung** (nicht erst Echtbetrieb).“

### N-2 – Schlüsselrotation braucht jetzt ein Format

- **Ort:** AD-14, Zurückgestellt „Schlüsselrotation“.
- **Risiko:** Wird das Chiffrat-Format ohne Schlüssel-Kennung und Versions-Byte gebaut, ist spätere Rotation nur durch Neuverschlüsseln aller Daten in einem Schritt möglich.
- **Fix (AD-14 ergänzen):** „Jedes Chiffrat trägt Formatversion und Schlüssel-Kennung; Verfahren AES-256-GCM.“ Die Rotation selbst darf zurückgestellt bleiben.

### N-3 – Mail-Inhalte und Outbox

- **Ort:** AD-4 (Ereignisse nur Kennungen), AD-13 (Mail über Outbox).
- **Risiko:** Für den Mail-Versand landen Empfänger-Adresse und Text in der Outbox-Tabelle und beim Mail-Anbieter. Erinnerungen (FR-11, FR-25) könnten Details enthalten („Führungszeugnis abgelaufen“, „Allergien fehlen“).
- **Fix (AD-13 ergänzen):** „Mails enthalten keine Freizeitpass-Antworten, keine Nachweis-Details und keine Angaben zu Vorfällen, sondern nur einen Hinweis und einen Link in die App. Zugestellte Outbox-Einträge werden nach 7 Tagen gelöscht.“ Mail-Anbieter: EU, mit AVV (bestehende Zurückgestellt-Zeile um „AVV und EU-Standort“ ergänzen).

### N-4 – Offene Fragen für OP-6

Als Zeilen in „Zurückgestellt“ aufnehmen (Auslöser jeweils Prüfung OP-6 bzw. Start als Dienst):

| Thema | Frage |
| --- | --- |
| Archivierte Vorfall-Notiz | Wer in der Organisation darf das Archiv lesen (nur Org-Admin?), wird jedes Lesen protokolliert, und ist die Person bzw. sind die Eltern informiert, dass Medikamentengaben über das Freigabefenster hinaus gespeichert werden? |
| Einsicht-Protokoll | Aufbewahrung (bereits zurückgestellt), zusätzlich: Ist es in der Datenauskunft enthalten und überlebt es die Kontolöschung des Lesenden? |
| Verantwortlichkeit | Freizeitpass gehört der Person (Betreiber verantwortlich), wird aber für die Freizeit erhoben (Organisation verantwortlich). Gemeinsame Verantwortung nach Art. 26 statt Auftragsverarbeitung prüfen; beeinflusst, wer Auskunft und Löschung schuldet. |
| Datenschutz-Folgenabschätzung | Art. 35 DSGVO ist bei Gesundheitsdaten Minderjähriger in großem Umfang wahrscheinlich Pflicht; vor Start als Dienst durchführen. |
| Art. 10 / Führungszeugnis | Die Datei ist ein Dokument über Straftaten bzw. deren Fehlen. Ist das Hochladen in eine Plattform zulässig, oder sollte nur die Einsicht (Vorlage) protokolliert werden? Auswirkung auf AD-9. |
| Staging und Testdaten | Keine Produktivdaten in lokalen oder Staging-Umgebungen; Testdaten nur synthetisch. |

---

## Was gut gelöst ist

- AD-7/AD-8/AD-9: Datenbesitz bei der Person, Durchgriff statt Kopie, nur Bestätigung bei Nachweisen. Das ist das stärkste Datenschutz-Merkmal des Entwurfs.
- AD-11: ein Token-Muster mit Hash-Speicherung und 128 Bit.
- AD-12: Cookie statt Tokens im Browser, Prüfung im Kontext auf dem Server.
- AD-4/AD-22: Ereignisse und Logs nur mit Kennungen.
- AD-16: Löschen und Auskunft als Pflicht jedes Moduls, endgültiges Löschen.
- AD-6: Row Level Security als Pflicht vor Start als Dienst bereits verankert.
