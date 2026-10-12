---
title: 'Nachweise mit einmaliger Einsicht'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: '4f03d7c18682bc29ce5199e7dcddc8eb68d70c3b'
review_loop_iteration: 1
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-4-familienkonto-und-uebergabe-mit-18.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Mitarbeitende können keine Nachweise (Führungszeugnis, JuLeiCa, Erste Hilfe, Präventionsschulung) am Profil ablegen. Organisationen haben keine Richtlinie für Pflicht und Gültigkeit. Es gibt weder die einmalige Einsicht mit Bestätigen oder Ablehnen noch die Bestätigung bei der Organisation (CAP-4, FR-8 bis FR-10, FR-12, AD-9, NS-3). Die Spine-Zeile `NachweisEingesehen` (Personen → Organisationen) hat noch keinen Code.

**Approach:**
- **Personen** bekommt Nachweise mit verschlüsselter Datei im Dateispeicher und die Einsicht als Aggregat. Eine Einsicht entsteht automatisch mit jeder Freizeit-Rolle oder auf Anfrage von Leitung bzw. Org-Admin. Die Person gibt frei, genau eine prüfende Person übernimmt die Prüfung und bestätigt oder lehnt je Nachweis ab.
- **Organisationen** bekommt die Nachweis-Richtlinie mit Vorgabewerten und speichert als Empfänger von `NachweisEingesehen` nur die Bestätigung, mit berechnetem „gültig bis“.
- **Web-App:** Seite „Nachweise“ im Konto, Prüfseiten für Leitung und Org-Admin, Richtlinie auf der Organisationsseite.
- Testgetrieben, mit Durchlauf-Test, Doku und Screenshots.

## Boundaries & Constraints

**Always:**

- **Nachweisarten (fester Katalog, Besitzer Personen):** `NachweisArt { Fuehrungszeugnis = 1, Juleica = 2, ErsteHilfe = 3, Praeventionsschulung = 4 }`.
  - Liegt in `Personen.Contracts/Events/` (Unter-Typ des Ereignisses, AD-4); Organisationen nutzt ihn intern.
  - In `Api.Contracts` hat jedes Modul seinen eigenen gleichnamigen Enum im eigenen Ordner (`Personen/`, `Organisationen/`, AD-5). Werte stehen im JSON als camelCase-String (`"fuehrungszeugnis"`, `"juleica"`, `"ersteHilfe"`, `"praeventionsschulung"`), im Pfad ohne Rücksicht auf Groß- und Kleinschreibung.
  - Ein Test prüft, dass die drei Enums dieselben Namen und Werte haben.
  - Anzeigetexte über `Anzeige.Nachweis(...)`: „Führungszeugnis“, „JuLeiCa“, „Erste Hilfe“, „Präventionsschulung“.
- **Verschlüsselte Dateien (SharedKernel, AD-14, AD-15):** Neuer Dienst `IGeschuetzteDateien` mit `SpeichernAsync(modulPraefix, kennung, personId, byte[] inhalt, ct)`, `LesenAsync(modulPraefix, kennung, ct) → byte[]?` (entschlüsselt) und `LoeschenAsync`.
  - Verschlüsselt wird mit `IVerschluesselung` (Bytes) und dem Schlüssel der Person. Gespeichert wird über `IDateiSpeicher` als `application/octet-stream`; `Geschuetzt.AusBytes` bleibt intern.
  - Nachweis-Dateien gehen nur über diesen Dienst, mit Präfix `personen` und Kennung = `NachweisId`.
- **Nachweis (Personen, Tabelle `nachweis`, nicht `IOrganisationsEigen`):**
  - Felder: `Id`, `PersonId`, `Art`, `Ausstellungsdatum` (`date`), `HochgeladenAm` (`timestamptz?`, setzt nur der Datei-Upload), `Ablehnungshinweis` (`[Besonders] Geschuetzt?`), `Version` (xmin).
  - Eindeutig (PersonId, Art): eine Person hat je Art höchstens einen Nachweis.
  - Eine neue Datei ersetzt die alte unter derselben Kennung.
  - Eine neue Datei oder ein geändertes Ausstellungsdatum löscht den Ablehnungshinweis.
- **Feldregeln Ausstellungsdatum:** Pflicht („Ausstellungsdatum fehlt.“), nicht in der Zukunft (Berlin-Kalendertag; „Das Ausstellungsdatum liegt in der Zukunft.“), Jahr ab 1900 („Das Ausstellungsdatum ist ungültig.“). Art fehlt oder unbekannt → „Art fehlt.“. Art schon vorhanden → `409 konflikt`.
- **Datei:** Multipart-Feld `datei`, geprüft mit `DateiPruefung.LesenUndPruefenAsync` (JPEG, PNG, WebP, PDF, höchstens 10 MB, Prüfung am Inhalt). Fehlertexte am Feld `datei`:
  - fehlt → „Datei fehlt.“
  - leer → „Die Datei ist leer.“
  - zu groß → „Die Datei ist größer als 10 MB.“
  - falscher Typ → „Erlaubt sind nur Fotos (JPEG, PNG, WebP) und PDF.“
- **Einsicht (`NachweisEinsicht`, Personen, erbt `Aggregat`, `IOrganisationsEigen`):**
  - Felder: `Id`, `OrganisationId`, `PersonId`, `FreizeitId?`, `AngefragtVon?` (null = automatisch), `PruefendePersonId?`, `Status { Angefragt = 1, Freigegeben = 2, Abgeschlossen = 3 }` (nur über Methoden), `AngefragtAm`, `FreigegebenAm?`, `FreigegebenBis?`, `AbgeschlossenAm?`, `Version`.
  - Posten in `nachweis_einsicht_posten`: `EinsichtId`, `NachweisId`, `Art`, `Ergebnis { Offen = 0, Bestaetigt = 1, Abgelehnt = 2 }`, `EntschiedenAm?`.
  - Zusätzlich `NurArten` (`short[]?`): nur bei der Neu-Anfrage nach einer Ablehnung gesetzt, sonst null.
  - **Offen** heißt: Status `Angefragt` oder `Freigegeben`, auch mit abgelaufener Freigabe. Erst `Abgeschlossen` beendet eine Einsicht. Je (Person, Organisation) gibt es höchstens eine offene Einsicht. Dafür gibt es einen partiellen eindeutigen Index auf (PersonId, OrganisationId) mit `Status <> 3`.
  - **Gültige Freigabe** heißt: Status `Freigegeben` und `FreigegebenBis` ≥ jetzt.
  - **Anfragen** ist idempotent: Gibt es schon eine offene Einsicht, kommt sie zurück und es entsteht keine neue.
  - **Automatisch nur bei der ersten Mitarbeit:** Der bestehende Empfänger `FreizeitRolleUebernehmen` legt nach dem Upsert der Rolle eine Einsicht an (Organisation aus dem Umschlag, `FreizeitId` aus dem Ereignis, `AngefragtVon` und `PruefendePersonId` = null). Das geschieht nur, wenn es für (Person, Organisation) noch gar keine Einsicht gibt, egal in welchem Status. Später fragt die Organisation von Hand an (NS-3). Die Ereignis-Fluss-Tabelle ändert sich nicht.
  - **Von Hand:**
    - Leitung im Freizeit-Kontext: nur für Personen mit Zeile in `personen.freizeit_rolle` dieser Freizeit, nicht für sich selbst. Es gilt `PruefendePersonId` = `AngefragtVon` = die Leitung, `FreizeitId` = diese Freizeit.
    - Org-Admin: nur für Personen mit einer `freizeit_rolle` in einer Freizeit der Organisation, nicht für sich selbst. Es gilt `FreizeitId` = null, `PruefendePersonId` = `AngefragtVon` = der Org-Admin.
    - Sonst `404`.
  - **Freigeben** (nur die Person selbst): erlaubt bei Status `Angefragt` oder bei abgelaufener Freigabe derselben Einsicht. Bei gültiger Freigabe oder `Abgeschlossen` → `404` bzw. `409` (siehe Endpunkt 5).
    - Setzt `Freigegeben`, `FreigegebenBis` = jetzt + 14 Tage.
    - Legt Posten an für jeden eigenen Nachweis mit Datei, dessen Art in dieser Einsicht noch nicht entschieden ist und, falls `NurArten` gesetzt ist, in `NurArten` steht. Offene Posten einer früheren Freigabe werden vorher entfernt.
    - Kein passender Nachweis mit Datei → `409`.
  - **Prüfen übernehmen:** Bindet die Einsicht an genau eine prüfende Person (AD-9). Ist sie schon an die anfragende Person gebunden, ändert sich nichts. Ist `PruefendePersonId` null, wird sie gesetzt, aber nur so:
    - über die Freizeit-Route, wenn die Person der Einsicht eine Zeile in `personen.freizeit_rolle` dieser Freizeit hat (die Leitung prüft der Host) und die anfragende Person nicht die Person selbst ist;
    - über die Org-Route (Org-Admin prüft der Host), wenn die anfragende Person nicht die Person selbst ist.
    - Sonst `404`, ebenso wenn schon jemand anderes gebunden ist. Ohne gültige Freigabe → `409`.
  - **Keine Änderung während der Prüfung:** Datum ändern (Endpunkt 3) und Datei hochladen (Endpunkt 4) antworten mit `409 konflikt`, solange der Nachweis in einem offenen Posten einer Einsicht mit gültiger Freigabe steht. So bestätigt die prüfende Person genau, was sie gesehen hat.
  - **Bestätigen / Ablehnen:**
    - Nur die gebundene prüfende Person, nur bei gültiger Freigabe, nur für offene Posten, sonst `404`. Nicht freigegeben, abgelaufen oder schon abgeschlossen → `404`.
    - Bestätigen legt `Events.NachweisEingesehen(Guid PersonId, Guid NachweisId, NachweisArt Art, DateOnly Ausstellungsdatum, DateTimeOffset EingesehenAm, Guid PruefendePersonId)` ab.
    - Ablehnen verlangt `hinweis` (1–500 Zeichen, getrimmt; „Hinweis fehlt.“) und speichert ihn verschlüsselt mit dem Schlüssel der Person am Nachweis.
    - Sind alle Posten entschieden, wird die Einsicht `Abgeschlossen`. Wurde mindestens ein Posten abgelehnt, entsteht in derselben Transaktion eine neue offene Einsicht mit derselben Organisation, Freizeit und prüfenden Person (`AngefragtVon` = prüfende Person, FR-10 „neu angefordert“). Deren `NurArten` sind die abgelehnten Arten; schon Bestätigtes wird nicht erneut gezeigt.
  - Zeilensperre (`FOR UPDATE`) auf die Einsicht bei Freigeben, Übernehmen und Entscheiden.
- **Dateizugriff der prüfenden Person:** nur gebunden, bei gültiger Freigabe und für einen offenen Posten. Ausgeliefert über `DateiAuslieferung.Ausliefern` (Bilder inline, PDF als Anhang, Dateiname `<art>.<endung>`). Nach der Entscheidung, nach Ablauf und nach dem Abschluss gibt es `404`; danach endet der Zugriff.
- **Nachweis-Richtlinie (Organisationen, Tabelle `nachweis_richtlinie`, `IOrganisationsEigen`):**
  - Felder: `OrganisationId`, `Art`, `Pflicht` (bool), `GueltigkeitMonate` (1–120; „Die Gültigkeit muss zwischen 1 und 120 Monaten liegen.“), `Version`.
  - Vorgabewerte: Führungszeugnis Pflicht/60, JuLeiCa nein/36, Erste Hilfe nein/24, Präventionsschulung nein/60.
  - Die vier Zeilen entstehen beim Anlegen einer Organisation im selben Command. Die Migration fügt sie für bestehende Organisationen ein.
  - Bearbeiten nach AD-19 mit `RichtlinieFeld { Pflicht, GueltigkeitMonate }` (JSON `pflicht` mit `"true"`/`"false"`, `gueltigkeitMonate` als Zahl-String, Muster `KindFeld`).
- **Nachweis-Bestätigung (Organisationen, Tabelle `nachweis_bestaetigung`, `IOrganisationsEigen`):**
  - Felder: `OrganisationId`, `PersonId`, `Art`, `EingesehenAm`, `GueltigBis` (`date`), `GeprueftVon` (PersonId). Eindeutig (Organisation, Person, Art), das jüngste `EingesehenAm` gewinnt.
  - Empfänger-Command `NachweisBestaetigungSpeichern` (`EmpfaengtEreignis<NachweisEingesehen, NachweisBestaetigungSpeichern>()`) rechnet `GueltigBis = Ausstellungsdatum.AddMonths(GueltigkeitMonate)` aus der Richtlinie zum Zeitpunkt des Empfangs. Fehlt die Zeile, gilt der Vorgabewert.
  - Gespeichert wird nur das. Keine Datei, kein Ausstellungsdatum.
- **Endpunkte** (Datenformen in `Api.Contracts/Personen/Nachweise.cs` bzw. `Organisationen/Nachweise.cs`; `{e}` = `/einsichten/{einsichtId:guid}`):

  | # | Endpunkt | Zugang | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/ich/nachweise` | angemeldet, `.Organisationsuebergreifend()` | `200 {nachweise: [{id, version, art, ausstellungsdatum, hochgeladenAm?, ablehnungshinweis?}], anfragen: [{id, organisationId, freizeitId?, angefragtVon: {vorname, nachname}?, status (angefragt\|freigegeben), angefragtAm, freigegebenBis?}]}`; nur eigene Nachweise und eigene offene Einsichten aller Organisationen (eine abgelaufene Freigabe erscheint als `angefragt`) |
  | 2 | `POST /api/ich/nachweise` `{art, ausstellungsdatum}` | wie 1 | `201 {nachweisId}`; `400` je Feld; `409` Art vorhanden |
  | 3 | `PATCH /api/ich/nachweise/{nachweisId:guid}` (`ausstellungsdatum`) | wie 1 | AD-19; fremd → `404`; in laufender Prüfung → `409` |
  | 4 | `POST /api/ich/nachweise/{nachweisId:guid}/datei` (multipart) | wie 1 | `200 {hochgeladenAm}`; `400` Feld `datei`; fremd → `404`; in laufender Prüfung → `409` |
  | 5 | `POST /api/ich/nachweise/einsichten/{einsichtId:guid}/freigeben` | wie 1 | `200 {freigegebenBis}`; fremd/abgeschlossen → `404`; gültige Freigabe läuft schon oder kein passender Nachweis mit Datei → `409` |
  | 6–12 | unter `/api/freizeiten/{freizeitId:guid}/personen/nachweise`: `GET ""` (Personen der Freizeit außer der eigenen, je `{personId, vorname, nachname, anfragbar, einsicht?: {id, status, angefragtAm, freigegebenBis?, abgeschlossenAm?, pruefbar}}`, jüngste Einsicht der Person in dieser Organisation; `pruefbar` nach den Regeln für „Prüfen übernehmen“, `status` mit dem zusätzlichen Wert `abgelaufen` für eine abgelaufene Freigabe); `POST /anfragen {personId}` (`201`/`200 {einsichtId}`); `GET {e}` (`{id, personId, vorname, nachname, status, freigegebenBis?, freigabeAbgelaufen, posten: [{nachweisId, art, ausstellungsdatum, hochgeladenAm, ergebnis, entschiedenAm?}]}`, nur wenn gebunden oder übernehmbar, sonst `404`); `POST {e}/pruefen`; `GET {e}/nachweise/{nachweisId:guid}/datei`; `POST {e}/nachweise/{nachweisId:guid}/bestaetigen`; `POST {e}/nachweise/{nachweisId:guid}/ablehnen {hinweis}` | `.NurFreizeitRolle(Leitung)` | wie in den Regeln oben |
  | 13–19 | dieselben sieben unter `/api/organisationen/{orgId:guid}/personen/nachweise` (Personenliste: alle mit `freizeit_rolle` in der Organisation außer der eigenen) | `.NurOrgRolle(OrgAdmin)` | wie 6–12; übernehmbar nach den Regeln für „Prüfen übernehmen“ |
  | 20 | `GET /api/organisationen/{orgId:guid}/nachweis-richtlinie` | `.NurOrgRolle(OrgAdmin)` | `200 [{art, version, pflicht, gueltigkeitMonate}]` in Enum-Reihenfolge, nur gespeicherte Zeilen (keine Ersatzwerte) |
  | 21 | `PATCH /api/organisationen/{orgId:guid}/nachweis-richtlinie/{art}` | wie 20 | AD-19; unbekannte Art → `404` |
  | 22 | `GET /api/ich/organisationen/nachweis-bestaetigungen` (Organisationen) | angemeldet, `.Organisationsuebergreifend()` | `200 [{organisationId, organisation, art, eingesehenAm, gueltigBis, abgelaufen, geprueftVon: {vorname, nachname}}]`, nur eigene; `abgelaufen` rechnet der Server (`gueltigBis` vor heute, Kalendertag Europe/Berlin) |
  | 23 | `GET /api/ich/organisationen/namen?id=…&id=…` (Organisationen) | angemeldet, `.Organisationsuebergreifend()` | `200 [{organisationId, name}]` für die genannten, existierenden Organisationen (höchstens 20 Kennungen, sonst `400`); nur Namen, damit die Person sieht, welche Organisation um Einsicht bittet |

- **Aktionsschlüssel (additiv in `Gemeinsam/`):**
  - `nachweise-pruefen`: im Freizeit-Kontext für Leitung und in `OrganisationAntwort.erlaubt` für Org-Admin.
  - `nachweis-richtlinie-verwalten`: für Org-Admin.
  - Berechnet aus denselben Rollenlisten wie die Endpunkte.
- **Mails:** keine. Der Hinweis erscheint auf der Seite „Nachweise“; Erinnerungen kommen mit 1-6.
- **Demo-Daten (nur Commands, idempotent):**
  - Jonas legt Führungszeugnis (Ausstellung 2025-11-03), JuLeiCa (2024-05-20) und Erste Hilfe (2025-04-15) an, je mit einer kleinen erfundenen Demo-Datei (PDF bzw. PNG, im Code erzeugt).
  - Für Jonas und Miriam wird die automatische Einsicht ausdrücklich sichergestellt.
  - Jonas gibt frei. Miriam übernimmt die Prüfung und bestätigt Erste Hilfe; die übrigen Posten bleiben offen.
  - Idempotent auch nach Tagen: Ist Jonas' Freigabe beim erneuten Einspielen abgelaufen, gibt er erneut frei, bevor Miriam übernimmt; Entschiedenes bleibt entschieden.
  - Demo-Daten brauchen den Dateispeicher. `DemoDatenTests` und der E2E-Host bekommen deshalb einen S3-Container.
- **Web-App** (nur Bausteine aus Ui.Komponenten, Texte ohne Du/Sie, Datum über `Anzeige.Datum`):
  - **Neue Bausteine:**
    - `Ui.Komponenten/DateiFeld` (`[NurInteraktiv]`, `InputFile`, Beschriftung „Foto aufnehmen oder Datei wählen“, `accept` für die vier Typen, Fehlertext am Feld).
    - `Ui.Komponenten/Ankreuzfeld` (statisch tauglich, ELKW-Klasse `elkw-choice`).
    - In `Zeltlotse.Ui/Autosave/` das `AutosaveAnkreuzfeld` mit demselben `AutosaveDienst` (Werte `"true"`/`"false"`).
    - Alle mit bUnit-Tests (AD-30).
  - **`/konto/nachweise`** (Feature `Personen`): h1 „Nachweise“, Link „Zurück zum Konto“.
    - Je offener Anfrage eine Meldung „Hinweis: <Organisation> bittet um Einsicht in Nachweise.“ (Name aus Endpunkt 23), dahinter „Angefragt von <Name>.“ bei einer Anfrage von Hand bzw. „Angefragt wegen der Mitarbeit in „<Freizeit>“.“ bei der automatischen (Freizeitname aus `GET /api/ich/freizeiten`; fehlt er, entfällt der Satz). Dazu `InlineBestaetigung`: Auslöser „Freigeben“, Satz „Nachweise einmal zur Prüfung freigeben? Die prüfende Person sieht die Dateien bis zur Entscheidung, höchstens 14 Tage.“, Knopf „Ja, freigeben“.
    - Freigegebene Anfrage: „Freigegeben bis TT.MM.JJJJ, wartet auf Prüfung.“
    - Je Nachweis eine Card (Titel Art):
      - `AutosaveFeld` „Ausgestellt am“ (`Typ="date"`), dazu „Hochgeladen am TT.MM.JJJJ“ oder „Noch keine Datei.“.
      - `DateiFeld` „Neue Datei hochladen“.
      - Bei Ablehnung Meldung „Fehler: Abgelehnt: <Hinweis>“.
      - Je Bestätigung aus Endpunkt 22: „Bestätigt · gültig bis TT.MM.JJJJ (<Organisation>)“; ist sie `abgelaufen`, stattdessen Meldung (Warnung) „Abgelaufen am TT.MM.JJJJ (<Organisation>)“.
      - Antwortet Datum ändern oder Hochladen mit `409`: „Fehler: Der Nachweis ist gerade zur Prüfung freigegeben. Ändern geht nach der Entscheidung oder nach Ablauf der Freigabe.“ Antwortet das Hochladen mit `404`: „Fehler: Der Nachweis wurde nicht gefunden.“ (ohne „Erneut versuchen“).
    - Card „Nachweis hinzufügen“ (nur solange eine Art fehlt): Auswahl „Art“, Feld „Ausgestellt am“, `DateiFeld`, Knopf „Nachweis hinzufügen“.
      - Ablauf: erst Endpunkt 2, dann 4.
      - Scheitert der Upload: „Fehler: Datei nicht hochgeladen.“ mit Knopf „Erneut versuchen“; der Eintrag bleibt ohne Datei gespeichert (EXPERIENCE).
      - Erfolg: „Gespeichert: Der Nachweis ist hinzugefügt.“ Die Meldung steht oberhalb der Cards, damit sie auch erscheint, wenn die Card „Nachweis hinzufügen“ danach wegfällt.
    - Scheitert eine Zusatzabfrage (Endpunkte 22, 23, Meine Freizeiten), bleibt die Seite nutzbar: Die Nachweise erscheinen, die fehlenden Zusätze entfallen.
    - Ohne Nachweise: `LeerZustand` „Noch keine Nachweise hochgeladen.“
  - **Konto:** Card „Nachweise“ mit Satz und Knopf (Outline) „Nachweise öffnen“ → `konto/nachweise`.
  - **Prüfliste**, eine Komponente für die Routen `/freizeiten/{FreizeitId:guid}/nachweise` und `/organisationen/{OrgId:guid}/nachweise`:
    - h1 „Nachweise prüfen“, Datentabelle mit Name und Status:
      - „Noch nicht angefragt“, „Angefragt am …“, „Freigegeben bis …“, „Freigabe abgelaufen am …“ oder „Geprüft am …“.
      - Knopf „Einsicht anfragen“, wenn `anfragbar`.
      - Knopf „Prüfen“, wenn `pruefbar` und freigegeben (POST `pruefen`, dann zur Detailseite).
    - Leer: „Noch niemand im Team.“ 403/404 → `NichtVerfuegbar`.
  - **Detailseite** `/freizeiten/{FreizeitId:guid}/nachweise/{EinsichtId:guid}` bzw. `/organisationen/{OrgId:guid}/nachweise/{EinsichtId:guid}`:
    - h1 „Nachweise von <Name>“, Satz „Jede Datei lässt sich nur bis zur Entscheidung öffnen. Gespeichert wird nur die Bestätigung.“
    - Je Posten eine Card: Art, „Ausgestellt am …“, „Hochgeladen am …“, Link „Datei öffnen“ (href auf den API-Endpunkt, neuer Tab), Knopf „Bestätigen“, Feld „Hinweis“ mit Knopf (Outline) „Ablehnen“.
    - Entschieden: Status „Bestätigt“ bzw. „Abgelehnt“ ohne Link.
    - Nicht freigegeben: „Noch nicht freigegeben.“; abgelaufen (`freigabeAbgelaufen`): „Die Freigabe ist am TT.MM.JJJJ abgelaufen. Die Person muss erneut freigeben.“ Link „Zurück zur Liste“.
    - Nach jeder erfolgreichen Entscheidung lädt die Seite neu; ein Feldfehler eines früheren „Ablehnen“ verschwindet beim nächsten Versuch.
  - **Freizeit-Startseite:** Knopf „Nachweise prüfen“, wenn der Kontext `nachweise-pruefen` erlaubt.
  - **Organisationsseite** (nur mit `nachweis-richtlinie-verwalten`): Card „Richtlinie für Nachweise“ als Datentabelle je Art mit `AutosaveAnkreuzfeld` „Pflicht“ und `AutosaveFeld` „Gültig (Monate)“ (`Typ="number"`). Mit `nachweise-pruefen` dazu der Knopf „Nachweise prüfen“.
- **Tests (AD-30), Test vor dem Code:**
  - Fachlogik mit `FakeTimeProvider` (Freigabe 14 Tage, Übernehmen, Entscheiden, Neu-Anfrage nach Ablehnung, `GueltigBis`).
  - HTTP-Tests für jede Matrix-Zeile mit S3-Container.
  - Durchlauf-Test `NachweisEingesehen`, bUnit, Doku-Screenshots.
  - Kontext als Team-Mitglied: `erlaubt` ohne `nachweise-pruefen`. Ein Test prüft, dass `FreizeitRechte.NachweisePruefen` bzw. `OrgRechte.NachweisePruefen` dieselben Rollen haben wie die Merkmale der Prüf-Endpunkte.
  - Persönlich, Elternteil: Katrin bekommt für Kennungen von Paul bzw. Emma `404` an den Endpunkten 3, 4 und 5; `GET` (1, 22) liefert ihr nur eigene Daten.
- **Doku:**
  - Neu `mitarbeitende/nachweise.md`: hochladen, Ausstellungsdatum, freigeben, Status, abgelehnt korrigieren, während der Prüfung nicht änderbar, abgelaufene Bestätigung, die erste Mitarbeit in einer Organisation fragt automatisch an (später fragt die Organisation von Hand an), zweite Organisation ohne neuen Upload. Bilder `nachweise`, `konto`.
  - Neu `leitung/nachweise-pruefen.md`: Liste, anfragen, prüfen, bestätigen/ablehnen, Zugriff endet. Ehrlich dazu: Ein PDF wird heruntergeladen; die heruntergeladene Datei nach der Prüfung löschen. Wer die Leitung selbst prüft (die andere Leitung oder die Org-Admin). Bilder `nachweise-pruefen`, `nachweis-einsicht`.
  - Neu `org-admin/nachweis-richtlinie.md`: Pflicht (legt fest, welche Nachweise die Organisation verlangt; keine Behauptung über Warnhinweise), Gültigkeit, Vorgabewerte, gültig bis = Ausstellung + Monate, Prüfen auf Org-Ebene (auch automatische Anfragen übernehmen), heruntergeladene Datei löschen. Bild `nachweis-richtlinie`.
  - Anpassen: `daten.md` (Abschnitt Nachweise: verschlüsselte Datei nur am Profil, Freigabe höchstens 14 Tage für genau eine prüfende Person, Hinweis verschlüsselt, Organisation speichert nur Art, eingesehen am, gültig bis, von wem) und die `index.md` von Mitarbeitende, Leitung und Org-Admin.

**Never:**
- Keine Warnhinweise zu fehlenden oder ablaufenden Nachweisen, kein Cockpit, keine Erinnerungen und keine Mails (1-6). Kein Zugriffsprotokoll, keine Datenauskunft und kein Löschen (1-7).
- Kein Löschen einzelner Nachweise, kein Zurückziehen von Anfragen, keine eigenen Nachweisarten.
- Keine Datei und kein Ausstellungsdatum bei Organisationen. Keine Kopie in einem anderen Modul. Keine öffentlichen oder erratbaren Datei-URLs. Kein Klartext der Datei im Dateispeicher.
- Kein neuer Pfeil zwischen Modulen, keine neue Zeile im Ereignis-Fluss, keine neuen Problemcodes (`konflikt` für 409).
- Keine Pop-ups, keine Lockerung von CSP, Antiforgery oder Cookie-Richtlinie. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Nachweis anlegen | Jonas `{fuehrungszeugnis, 2025-11-03}`, dann PDF hochladen | `201`, `200 {hochgeladenAm}`; im Speicher liegt nur Chiffre (Rohbytes ≠ PDF), `LesenAsync` liefert das PDF | Datum fehlt/Zukunft/1899, Art fehlt → `400`; Art doppelt → `409` |
| Datei-Fälle | leer, 10 MB + 1, `.pdf`-Endung mit PNG-Inhalt (angenommen als PNG), Text-Datei | `400` Feld `datei` bzw. `200` | – |
| Persönlich | Lea ruft PATCH/Datei/Freigeben mit Jonas' Kennungen auf | `404`; `GET` liefert nur eigene Daten und Vertragsfelder | – |
| Automatische Einsicht | `FreizeitRolleGeaendert` für Jonas (zweimal zugestellt); danach Rolle in einer zweiten Freizeit derselben Organisation; nach abgeschlossener Einsicht noch eine Rolle | genau eine Einsicht (Org, erste Freizeit, ohne prüfende Person); keine weitere bei der zweiten Rolle und nach dem Abschluss | – |
| Freigabe abgelaufen | Uhr +14 Tage +1 Min. nach der Freigabe; Leitung fragt von Hand an; Jonas gibt erneut frei | Anfragen liefert dieselbe Einsicht (`200`), keine zweite; `GET` zeigt sie Jonas als `angefragt`, die Prüfliste als `abgelaufen`; erneutes Freigeben setzt neue 14 Tage, Entschiedenes bleibt entschieden | Freigeben bei gültiger Freigabe → `409` |
| Änderung während der Prüfung | Nachweis in offenem Posten bei gültiger Freigabe: PATCH Datum, neue Datei; nach der Entscheidung bzw. nach Ablauf erneut | `409` / `409`; danach `200` | – |
| Freigeben | offene Einsicht, 2 Nachweise mit Datei, 1 ohne | `200`, 2 Posten, `freigegebenBis` = +14 Tage | ohne Datei → `409`; fremde/abgeschlossene Einsicht → `404` |
| Übernehmen | Miriam (Leitung derselben Freizeit), danach Tobias (zweite Leitung) | Miriam gebunden; Tobias `404` bei `pruefen`, Detail und Datei | Jonas an eigener Einsicht → `404`; nicht freigegeben → `409` |
| Übernehmen anderswo | ungebundene automatische Einsicht aus Freizeit A: Leitung von Freizeit B derselben Organisation, in der die Person auch eine Rolle hat; Org-Admin über die Org-Route; Leitung einer Freizeit ohne Rolle der Person | gebunden / gebunden / `404` | – |
| Datei ansehen | gebunden, offener Posten / nach Entscheidung / Uhr +14 Tage +1 Min. | `200` PDF `attachment` bzw. PNG `inline` / `404` / `404` | – |
| Bestätigen | Miriam bestätigt Erste Hilfe (2025-04-15), Richtlinie 24 Monate | Ereignis in `personen.outbox`; nach Zustellung Bestätigung mit `gueltigBis` 2027-04-15, `geprueftVon` Miriam; Endpunkt 22 zeigt sie Jonas | zweites Bestätigen → `404` |
| Ablehnen | Hinweis „Datum passt nicht“ zu JuLeiCa, Erste Hilfe bestätigt, letzter offener Posten | Einsicht `Abgeschlossen`, neue offene Einsicht mit gleicher prüfender Person und `NurArten` = JuLeiCa (Freigeben legt nur den JuLeiCa-Posten an); Hinweis verschlüsselt (Roh-SQL ≠ Klartext), `GET` zeigt ihn Jonas; PATCH Datum löscht ihn | Hinweis leer/501 Zeichen → `400` |
| Zwei Organisationen | Jonas bekommt eine Rolle in einer Freizeit von Org B (über das Ereignis), Org-Admin B übernimmt die automatische Einsicht, Jonas gibt frei, Org-Admin B bestätigt | Bestätigung in B ohne neuen Upload; A und B sehen je nur ihre Zeile; Endpunkt 23 liefert Jonas den Namen von B; eine abgelaufene Bestätigung trägt `abgelaufen` | – |
| Anfrage von Hand | Leitung für Teammitglied / für sich selbst / für Person ohne Rolle in der Freizeit; Org-Admin für Person ohne Rolle in der Org | `201` bzw. `200` bei offener / `404` / `404` / `404` | – |
| Rechte Freizeit | Leitung; Team (Zeile per SQL) → alle Freizeit-Endpunkte; fremde Freizeit; Einsicht einer anderen Organisation über eigene Route | `200` / `403` / `404` / `404` | – |
| Rechte Organisation | Org-Admin; Org-Überblick; Org-Admin einer anderen Organisation (Richtlinie, Prüfliste, Einsicht) | `200` / `403` / `404` | – |
| Richtlinie | neue Organisation; PATCH `gueltigkeitMonate` "36" → "0"; `pflicht` mit falschem `alterWert`; Art `xyz` | 4 Vorgabezeilen; `200` / `400` / `409` / `404` | – |
| Durchlauf | Bestätigen, Outbox zweimal zustellen | eine Zeile `organisationen.nachweis_bestaetigung`, Inbox verwirft das zweite | – |
| Demo-Daten | Start mit Demo zweimal | 3 Nachweise mit Datei, 2 Einsichten (Jonas freigegeben, Erste Hilfe bestätigt), 4 Richtlinien-Zeilen, nichts doppelt | – |

</intent-contract>

## Code Map

- `src/Zeltlotse.SharedKernel/Dateien/IDateiSpeicher.cs` (byte[]-API, `DateiSchluessel.Bilden`), `DateiPruefung.cs` (`LesenUndPruefenAsync(IFormFile)`, `VorgabeHoechstgroesse` 10 MB, `ArtErkennen`, `ContentType`), `DateiAuslieferung.cs` (`Ausliefern(byte[], dateiname)`) -- noch von keinem Modul genutzt; `IGeschuetzteDateien` kommt daneben und wird in `SharedKernelRegistrierung.AddSharedKernel()` registriert.
- `src/Zeltlotse.SharedKernel/Datenschutz/IVerschluesselung.cs` (Bytes-Varianten), `Geschuetzt.cs` (`AlsBytes()` öffentlich, `AusBytes` intern) -- der neue Dienst nutzt `AusBytes` innerhalb von SharedKernel.
- `src/Zeltlotse.Api/Adapter/S3DateiSpeicher.cs` -- einziger Adapter, wirft ohne Konfiguration `Zeltlotse:Dateien:*`.
- `tests/Zeltlotse.Testing/S3Container.cs` (RustFS, `NeuerBucket()`, `Einstellungen(bucket)`), `tests/Zeltlotse.Api.Tests/S3DateiSpeicherTests.cs` (Factory-Einstellungen aus S3), `AssemblyInfo.cs` (Fixture) -- Muster für Upload-Tests. `tests/Zeltlotse.Api.Tests/WebSicherheitTests.cs:151-206` -- Multipart mit Antiforgery-Header. `TestHelfer.cs:35` -- Test-Upload-Endpunkt.
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:26-59` -- Registrierung und Gruppen (`/ich/profil`, `/ich/familie`, `/personen`); hier `/ich/nachweise`, Freizeit- und Org-Gruppen. `Daten/PersonenDbContext.cs` (DbSets Z.89-101 in Schreib- und Lese-Context, `PersonenModell.Konfigurieren` ab Z.134; letzte Migration `20261011201133_FamilieUndUebergabe`). `Freizeitrollen/FreizeitRolleUebernehmen.cs:45-71` -- Empfänger, hier die Einsicht sicherstellen. `Familien/ProfilUebergabe.cs` -- Aggregat mit Status-Methoden und Ereignis. `Profil/ProfilFeldAendern.cs:61-64` -- Zeilensperre und AD-19. `Familien/*Endpunkte*` -- Endpunkt-Stil.
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Events/ProfilUebergabeAngenommen.cs` -- Vorbild für `NachweisEingesehen` und `NachweisArt`. `Namen/IPersonenNamen.cs` -- Namen für Organisationen (Endpunkt 22).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/OrganisationenModul.cs:21-47` -- Registrierung (noch kein Empfänger), `/{orgId:guid}`-Gruppe. `Verwaltung/OrganisationenEndpunkte.cs` (`OrganisationAntwort`, `/api/ich/organisationen` mit `IgnoreQueryFilters()`), `Rollen/OrgRechte.cs` (Rollenlisten, `Erlaubt`). Den Command, der eine Organisation anlegt, in `Verwaltung/` finden; dort die Richtlinien-Zeilen anlegen. `Daten/OrganisationenDbContext.cs:43-72`, letzte Migration `20261011170104_OrganisationRolleEinladung`. csproj referenziert `Personen.Contracts` schon.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Startseite/FreizeitEndpunkte.cs:128-142` -- Kontext mit `Erlaubt: []`; hier `FreizeitRechte.Erlaubt(rollen)` einführen. `FreizeitenModul.cs:36-54`.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs` (`Aktionsschluessel`), `Problemcodes.cs`; `Personen/Familie.cs` (Stil, `KindFeld`), `Gemeinsam/OrgRolle.cs` (`JsonStringEnumConverter`).
- `src/Zeltlotse.Api/Rechte/RechtePruefung.cs` -- 404/403 je Merkmal; Mandant aus `orgId` bzw. Freizeit. Nichts zu ändern.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs` -- Jonas/Miriam Leitung; Commands über `ISender` mit `MandantenKontext.Setzen`.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- `Organisationsuebergreifend`-Liste (+ Endpunkte 1–5, 22, 23), Zähler `mitOrg.Count` (Z.146, +9), feste Liste der Freizeit-Routen (Z.129, +7). `AnmeldeHelfer`, `OrgHelfer`, `FreizeitHelfer` (`LeitungAsync`), `FamilienHelfer` -- Vorbild für einen `NachweisHelfer` (Multipart). `DemoDatenTests.cs:52-103` -- Zählungen ergänzen, S3 einbinden. `FreizeitKontextTests`, `RechteTests` -- `erlaubt` anpassen.
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` (`Uhr`, `Einstellungen`, `OutboxJetztZustellenAsync`). `tests/Zeltlotse.Ablaeufe.Tests/FreizeitRolleGeaendertTests.cs` -- Vorbild Durchlauf; S3-Fixture dort ergänzen. `tests/Zeltlotse.Personen.Tests/`, `tests/Zeltlotse.Organisationen.Tests/` -- Fachlogik.
- `tests/Zeltlotse.ArchitectureTests/EreignisFlussTests.cs` -- die Spine-Zeile `NachweisEingesehen` ist da; Ereignis muss in `Zeltlotse.Personen.Contracts.Events` liegen.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs`, `OrganisationenApiClient.cs`, `FreizeitenApiClient.cs` (`MeineLadenAsync`), `Autosave/AutosaveFeld.razor` + `AutosaveDienst.cs` (Werte als String), `Texte/Anzeige.cs`, `Transport/AntiforgeryHandler.cs` (Header gilt auch für Multipart).
- `src/Zeltlotse.App/Features/Konten/Konto.razor`, `Features/Organisationen/OrganisationAnsicht.razor` (Cards nach `Erlaubt`, `NichtVerfuegbar`), `Features/Freizeiten/FreizeitStart.razor`, `Features/Personen/FamilieAnsicht.razor` (`InlineBestaetigung`, Formular, Meldungen), `_Imports.razor`.
- `src/Zeltlotse.Ui.Komponenten/` (Feld, Auswahl, Card, Datentabelle, Knopf, Meldung, InlineBestaetigung, LeerZustand, Status, `NurInteraktivAttribute`); `tests/Zeltlotse.ArchitectureTests/ClientRegeln.cs` (keine ELKW-Klassen außerhalb, `EventCallback` → `[NurInteraktiv]`).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`BildAsync`, `PerLinkAnmeldenAsync`, `AxeAsync`), `VeroeffentlichterHost.cs:60-70` (Umgebung; hier S3-Container und `Zeltlotse__Dateien__*`).
- `doku/src/content/docs/daten.md:37-47,79`, `mitarbeitende/index.md`, `leitung/index.md`, `org-admin/index.md`; `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (jedes Bild verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.SharedKernel.Tests/` -- ZUERST: `IGeschuetzteDateien` (Rundlauf, Chiffre im Speicher, andere Person nicht lesbar, fehlende Datei → null).
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST: `NachweisTests` (Anlegen, Validierung, Datei ersetzen löscht Hinweis), `NachweisEinsichtTests` (automatisch idempotent, Freigeben, Ablauf, Übernehmen, Entscheiden, Abschluss, Neu-Anfrage, Ereignis genau einmal, gleichzeitiges Entscheiden), Enum-Gleichheit.
- `tests/Zeltlotse.Organisationen.Tests/` -- ZUERST: Richtlinie mit Vorgabewerten beim Anlegen, Feldregeln, Empfänger `NachweisBestaetigungSpeichern` (`GueltigBis`, jüngste gewinnt, idempotent).
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: `NachweiseTests`, `NachweisEinsichtTests` (Freizeit und Organisation), `NachweisRichtlinieTests` für jede Matrix-Zeile; `EndpunktListeTests`, `DemoDatenTests`, Kontext-/Org-`erlaubt`.
- `tests/Zeltlotse.Ablaeufe.Tests/` -- ZUERST: Durchlauf `NachweisEingesehen` über die echten Endpunkte.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: `DateiFeld`, `Ankreuzfeld`, `AutosaveAnkreuzfeld`, Client-Methoden (Multipart), Seiten Nachweise, Prüfliste, Detail, Richtlinien-Card, Konto- und Start-Knopf.
- `tests/Zeltlotse.E2E.Tests/` -- S3 im Host; Bilder `nachweise` (Jonas), `nachweise-pruefen` und `nachweis-einsicht` (Miriam), `nachweis-richtlinie` (Andrea, Organisationsseite), je Handy und Laptop, mit axe.
- `src/Zeltlotse.SharedKernel/` -- `IGeschuetzteDateien` und Registrierung.
- `src/Modules/Personen/` -- Domäne, Modell, Migration, Commands, Endpunkte, Ereignis und Enum in Contracts, Empfänger erweitern.
- `src/Modules/Organisationen/` -- Richtlinie, Bestätigung, Migration (mit Vorgabezeilen), Empfänger, Endpunkte, `OrgRechte`.
- `src/Modules/Freizeiten/` -- `erlaubt` im Kontext.
- `src/Zeltlotse.Api.Contracts/` -- Datenformen, Enums, Felder, Aktionsschlüssel.
- `src/Zeltlotse.Api/Demo/` -- Demo-Nachweise und Einsicht.
- `src/Zeltlotse.Ui.Komponenten/`, `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` -- Bausteine, Clients, Seiten.
- `doku/` -- Seiten und Anpassungen laut Always; Screenshots aus den Tests.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, SharedKernel, Personen, Organisationen, Abläufe, API, Ui, E2E), und die vier neuen Bilder sind je Handy und Laptop geschrieben.
- Given Jonas (Demo) meldet sich an, when er im Konto „Nachweise öffnen“ tippt, then sieht er drei Nachweise; bei Erste Hilfe steht „Bestätigt · gültig bis 15.04.2027 (Jugendwerk Musterstadt)“, und die Anfrage ist bis zum Ende der Freigabe als freigegeben vermerkt.
- Given Miriam (Demo) öffnet in der Freizeit „Nachweise prüfen“, when sie bei Jonas „Prüfen“ tippt und Führungszeugnis bestätigt, then steht dort „Bestätigt“, der Link „Datei öffnen“ ist weg und der Dateiabruf antwortet `404`.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler und die drei neuen Seiten zeigen ihre Bilder.

## Spec Change Log

### 2026-10-12 — Schleife 1 (bad_spec)
- **Auslöser:** Review-Befunde zum Einsicht-Modell (blind, edge):
  - Die Org-Admin kann eine automatische Einsicht nie übernehmen, also bleibt eine einzelne Leitung ungeprüft.
  - Eine abgelaufene Freigabe zählte nicht als offen; dadurch entstanden zwei offene Einsichten je Person und Organisation.
  - Die Einsicht war an die erste Freizeit gebunden, Leitungen anderer Freizeiten derselben Organisation waren blockiert.
  - Ein Nachweis ließ sich während der Prüfung ändern, die Bestätigung beruhte dann auf Ungesehenem.
  - Schon Bestätigtes wurde nach Ablehnung und bei jeder neuen Rolle erneut gezeigt.
  - Die Person sah nicht, welche Organisation anfragt.
  - Abgelaufene Bestätigungen erschienen als „Bestätigt“.
  - Dazu mehrere kleine Mängel in Oberfläche, Demo und Tests.
- **Geändert:**
  - „Offen“ = nicht abgeschlossen, mit partiellem eindeutigem Index. Freigeben nur bei `Angefragt` oder nach Ablauf.
  - Automatische Anfrage nur ohne frühere Einsicht in der Organisation. `NurArten` für die Neu-Anfrage nach Ablehnung.
  - Übernehmen über jede Freizeit mit Rolle der Person und über die Org-Route.
  - `409` für Datum und Upload während einer Prüfung.
  - Endpunkt 22 mit `abgelaufen`, neuer Endpunkt 23 (Organisationsnamen), Detail mit `freigabeAbgelaufen`, Prüfliste mit `abgelaufen`.
  - Richtlinie ohne Ersatzwerte, Demo nach Ablauf erneut freigeben.
  - Oberflächentexte (409/404 beim Hochladen, Erfolgsmeldung, Teilfehler, Neuladen nach Entscheidung) und Doku-Hinweise (PDF-Download löschen, Pflicht, wer die Leitung prüft).
  - Zusätzliche Tests (Team-Kontext, Rollenlisten gleich, Elternteil).
  - Die Änderungen stehen auch im `<intent-contract>`. Der autonome Lauf verlangt, solche Entscheidungen selbst zu treffen statt anzuhalten (Persistent Facts), und der Contract stammt aus dieser Planung, nicht vom Menschen.
- **Vermiedener Zustand:** ungeprüfte Leitungen und Personen mit Rollen; doppelte Hinweise; Bestätigungen für ungesehene Daten; unnötiges erneutes Zeigen von Führungszeugnissen.
- **KEEP:**
  - Die erste Umsetzung liegt als Patch unter `C:\Users\micro\AppData\Local\Temp\claude\C---Data-01-sourcecode-github-Zeltlotse\4bd77883-8621-4056-90aa-ba807e13faff\scratchpad\1-5-versuch-1.patch`. Es lässt sich mit `git apply` auf den Baseline-Stand anwenden und war vollständig grün: 1129 Tests, Doku-Build ok.
  - Mit `git apply` beginnen und nur das Geänderte anpassen. Unverändert behalten:
    - `IGeschuetzteDateien` mit Fabrik-Registrierung und `DateiSpeicherImSpeicher` für Tests.
    - Aufbau `Nachweise/` in Personen und Organisationen, Advisory-Lock je (Person, Organisation), Zeilensperren.
    - `NachweisEinsicht` als Aggregat mit Posten, Enum-Gleichheitstest, Bausteine `DateiFeld`/`Ankreuzfeld`/`AutosaveAnkreuzfeld`.
    - Seiten und Texte außerhalb der geänderten Punkte, Doku-Seiten und Screenshot-Tests, S3 im E2E-Host und in `DemoDatenTests`/Abläufe.
  - Die Migrationen neu erzeugen, wenn sich das Modell ändert (`NurArten`, partieller Index); keine zweite Migration für dieselbe Story.

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 41 findings — high 0, medium 16, low 16, false 9, maybe-false 0
- findings:
  - `[medium]` `[bad_spec]` (blind) Nachweis änderbar während offener Prüfung, Bestätigung beruht auf Ungesehenem — `EinsichtPruefen.cs` liest das Datum beim Entscheiden; Spec: `409` während der Prüfung.
  - `[medium]` `[bad_spec]` (blind) Abgelaufene Freigabe erlaubt eine zweite offene Einsicht — `IstOffen` schloss Abgelaufenes aus; Spec: offen = nicht abgeschlossen, partieller Index.
  - `[medium]` `[bad_spec]` (blind) Einzelne Leitung nie prüfbar, Anfrage der Org-Admin liefert die nicht übernehmbare automatische Einsicht — `EinsichtZugang.Pruefbar` verlangt Freizeit-Route und `einsicht.FreizeitId`; Spec: Übernehmen auch über die Org-Route.
  - `[medium]` `[bad_spec]` (blind) Mehrere Freizeiten einer Organisation: Einsicht an die erste gebunden — gleiche Stelle; Spec: Übernehmen über jede Freizeit mit Rolle der Person.
  - `[low]` `[reject]` (blind) Gebundene prüfende Person lässt sich nicht lösen — Rollen lassen sich im System noch nirgends entziehen (kein Endpunkt); nicht erreichbar, Lösung wäre neue Logik.
  - `[medium]` `[bad_spec]` (blind) Schon Bestätigtes wird erneut gezeigt — `Freigeben` nimmt alle Nachweise mit Datei; Spec: automatische Anfrage nur bei erster Mitarbeit, `NurArten` nach Ablehnung.
  - `[low]` `[bad_spec]` (blind) Doku verspricht Zugriffsende, PDFs werden heruntergeladen — AD-15 verlangt `attachment`; Spec: Doku nennt das Löschen der heruntergeladenen Datei.
  - `[low]` `[bad_spec]` (blind) Abgelaufene Bestätigung erscheint als „Bestätigt“ — Spec: `abgelaufen` vom Server, Warnung in der Oberfläche.
  - `[low]` `[bad_spec]` (blind) „Pflicht“ wirkt nirgends, Doku stellt es als wirksam dar — Spec: Doku beschreibt nur, was Pflicht festlegt.
  - `[medium]` `[bad_spec]` (blind) Person sieht nicht, welche Organisation anfragt — Spec: Endpunkt 23 und Organisationsname im Hinweis.
  - `[medium]` `[bad_spec]` (blind) Test, der die Rollenlisten gleich hält, fehlt — gleiche Ursache wie verification-gap 1; Spec: Team-Kontext und Vergleichstest.
  - `[low]` `[reject]` (blind) Upload schreibt die Datei vor dem Commit — Commit-Fehler nach dem Upload sind selten, die Person lädt erneut hoch; Lösung (eigene Kennung je Upload, Aufräumen) bringt neue Logik und verwaiste Dateien.
  - `[low]` `[bad_spec]` (blind) Erfolgsmeldung verschwindet mit der Card „Nachweis hinzufügen“ — Spec: Meldung oberhalb der Cards.
  - `[low]` `[bad_spec]` (blind) Detail zeigt abgelaufene Freigabe als „Noch nicht freigegeben.“ — Spec: `freigabeAbgelaufen` und eigener Text.
  - `[medium]` `[bad_spec]` (edge) Erneutes Freigeben einer abgelaufenen Einsicht neben einer neueren offenen — gleiche Ursache wie blind „zweite offene Einsicht“.
  - `[medium]` `[bad_spec]` (edge) Abgelaufene Freigabe bleibt ewig `Freigegeben`, Liste zeigt alt und neu — gleiche Ursache.
  - `[medium]` `[bad_spec]` (edge) Neu-Anfrage nach Ablehnung ohne Prüfung auf eine andere offene Einsicht — gleiche Ursache; mit „offen = nicht abgeschlossen“ und Index ausgeschlossen.
  - `[medium]` `[bad_spec]` (edge) Leitung von Freizeit A bekommt `200 Vorhanden`, kann aber nicht prüfen — gleiche Ursache wie blind „mehrere Freizeiten“.
  - `[medium]` `[bad_spec]` (edge) Einzelne Leitung nie prüfbar, Org-Route kann nicht übernehmen — gleiche Ursache wie blind.
  - `[low]` `[reject]` (edge) Datei/Entscheiden ignorieren die Freizeit der Route, Ex-Leitung behält Zugriff — Rollen lassen sich nicht entziehen; nur die gebundene Person kommt an die Datei.
  - `[low]` `[reject]` (edge) Bindung bleibt nach Rollenverlust — gleiche Lage, kein Rollenentzug im System.
  - `[medium]` `[bad_spec]` (edge) Änderung von Datum/Datei während offener Freigabe — gleiche Ursache wie blind.
  - `[false]` `[reject]` (edge) 500 beim Dateiabruf nach vernichtetem Schlüssel oder manipulierter Chiffre — Schlüssel werden erst mit 1-7 vernichtet; Manipulation in S3 ist kein Benutzerweg; eine Ausnahme mit Trace-ID ist dann korrekt.
  - `[false]` `[reject]` (edge) Fehlender Nachweis eines offenen Postens → 500 — Nachweise lassen sich nicht löschen (Never), die Lage ist nicht erreichbar.
  - `[false]` `[reject]` (edge) Detail zeigt 01.01.0001 bei fehlendem Nachweis — gleiche unerreichbare Lage.
  - `[medium]` `[bad_spec]` (edge) Jede Rollenänderung erzeugt eine neue automatische Anfrage — Spec: automatisch nur ohne frühere Einsicht.
  - `[low]` `[reject]` (edge) Gleichzeitige Zustellung zweier `NachweisEingesehen` → Schlüsselverletzung — der Zustelldienst wiederholt je Empfänger; die zweite Zustellung gelingt und die jüngste gewinnt.
  - `[low]` `[bad_spec]` (edge) Seite lädt nach Bestätigen nicht neu, wenn vorher ein Ablehnen-Fehler stand — Spec: Neuladen nach jeder Entscheidung, Feldfehler verschwindet.
  - `[low]` `[bad_spec]` (edge) Upload-404 erscheint als Netzfehler mit „Erneut versuchen“ — Spec: eigener Text ohne Wiederholen.
  - `[low]` `[bad_spec]` (edge) Seite unbrauchbar, wenn eine Zusatzabfrage scheitert — Spec: Zusätze entfallen, Nachweise bleiben.
  - `[medium]` `[bad_spec]` (edge) Demo-Einspielen wirft nach abgelaufener Freigabe — Spec: Demo gibt nach Ablauf erneut frei.
  - `[medium]` `[bad_spec]` (verification-gap) Rollenliste `FreizeitRechte.NachweisePruefen` ungeprüft, kein Team-Kontext-Test — Spec: Team-Kontext und Vergleichstest.
  - `[low]` `[reject]` (verification-gap) Paralleles Anlegen derselben Art ungetestet — der eindeutige Index und der Fang stehen, der Fall braucht einen Doppelklick zweier Geräte; der sequentielle Weg ist getestet.
  - `[low]` `[bad_spec]` (verification-gap, other) Richtlinie-GET liefert Ersatzwerte mit Version 0, die sich nicht speichern lassen — Spec: nur gespeicherte Zeilen.
  - `[false]` `[reject]` (intent) Token-Fälle fehlen — AD-30 ordnet Pflichtfälle nach Art des Endpunkts; kein Endpunkt nutzt ein Token.
  - `[false]` `[reject]` (intent) Keine Oberfläche, in der die Person Organisationen auswählt — FR-12 verlangt nur Freigabe ohne neuen Upload; jede Organisation fragt an, die Person gibt frei.
  - `[false]` `[reject]` (intent) Kein Browser-Test, der „Bestätigen“ klickt — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; API- und bUnit-Tests decken es ab.
  - `[low]` `[bad_spec]` (intent) Elternteil-Fall nur an PATCH und Upload — Spec: Elternteil auch an Freigeben und den GET-Endpunkten.
  - `[false]` `[reject]` (intent) Keine Screenshots der Prüfliste auf Org-Ebene — dieselbe Komponente wie in der Freizeit, deren Bild vorhanden ist.
  - `[false]` `[reject]` (intent) Doku-Build und Test vor dem Code nicht im Diff belegt — `npm run build` lief mit Exit-Code 0; „Test zuerst“ ist eine Prozessaussage.
  - `[false]` `[reject]` (intent) Geänderte bestehende Screenshots ohne eigenes Bild — die geänderten Bilder zeigen die neuen Knöpfe gewollt.

### 2026-10-12 — Review pass (nach Schleife 1)
- verdicts: 32 findings — high 0, medium 0, low 23, false 9, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind) Gebundene prüfende Person lässt sich nie lösen oder ersetzen — carried: Rollen lassen sich im System nirgends entziehen; ein Lösen wäre neue Logik.
  - `[low]` `[reject]` (blind) „Pflicht“ wirkt nirgends — FR-9 verlangt nur das Festlegen; die Wirkung (Warnhinweise, FR-11) gehört zu 1-6; die Doku behauptet keine Wirkung.
  - `[low]` `[reject]` (blind) Freigabe umfasst alle Nachweise mit Datei, Bestätigungssatz nennt weder Nachweise noch prüfende Person, 409-Text fasst zwei Gründe zusammen — Umfang durch „automatisch nur bei erster Mitarbeit“ und `NurArten` begrenzt; der Rest wäre neue Oberfläche ohne gezeigten Schaden.
  - `[false]` `[reject]` (blind) Kein Löschen eigener Nachweise, Löschen-Doku schweigt — FR-8 bis FR-12 verlangen kein Löschen einzelner Nachweise; Konto löschen und Lösch-Dienst sind Story 1-7.
  - `[low]` `[reject]` (blind) Prüfseite zeigt das künftige „gültig bis“ nicht; 01.01.0001 bei fehlendem Nachweis — Anzeige wäre neue Funktion; der fehlende Nachweis ist unerreichbar (carried).
  - `[low]` `[reject]` (blind) Upload und Freigeben gleichzeitig umgehen die Sperre; Datei vor dem Commit geschrieben — ein Mensch lädt kaum im selben Moment hoch und gibt frei; die Lösung wären zusätzliche Sperren; Commit-Teil carried.
  - `[false]` `[reject]` (blind) `Version` Pflicht, aber ungenutzt — AD-19 erkennt Konflikte am selben Feld über `alterWert`; die Version wird zurückgegeben und vom Client übernommen, wie bei Profil und Kind.
  - `[low]` `[reject]` (blind) Org-Route prüft die Rolle der Person nicht; Entscheiden ignoriert die Route — Einsichten entstehen nur für Personen mit Rolle in der Organisation, Rollen lassen sich nicht entziehen; zweiter Teil carried.
  - `[low]` `[patch]` (blind) Ist die einzige Leitung zugleich die einzige Org-Admin, prüft niemand; keine Benachrichtigungen — Doku ergänzt (zweite Org-Admin ernennen); Benachrichtigungen schließt AD-13/1-6 aus.
  - `[false]` `[reject]` (blind) Ausstellungsdatum liegt im Ereignis-Speicher, Doku sagt, die Organisation speichere es nicht — die Outbox liegt im Schema von Personen; die Inbox von Organisationen speichert nur Kennung und Folgenummer.
  - `[low]` `[reject]` (blind) Jeder Angemeldete kann jeden Organisationsnamen abfragen — bewusste Entscheidung (Design Notes): Organisationsnamen sind keine Personendaten und stehen auf öffentlichen Freizeitseiten.
  - `[low]` `[reject]` (blind) Demo-Daten brauchen den Dateispeicher ohne Rückfall — Aspire startet den Dateispeicher lokal immer mit; Demo-Daten gibt es nur in Development und Testing.
  - `[low]` `[patch]` (blind) Fotos lassen sich ebenso speichern, die Doku warnt nur vor PDFs — Hinweis in beiden Seiten auf jede gespeicherte Datei erweitert; API-Antworten tragen ohnehin `Cache-Control: no-store`.
  - `[low]` `[reject]` (edge) Datei vor dem Commit überschrieben — carried.
  - `[low]` `[reject]` (edge) Bindung bleibt nach Rollenverlust — carried.
  - `[low]` `[reject]` (edge) Prüfung „in Prüfung?“ ohne Sperre gegenüber Freigeben — gleiche Lage wie blind „gleichzeitig“, verworfen.
  - `[low]` `[reject]` (edge) 409 beim Datum zeigt immer „gerade zur Prüfung freigegeben“, auch bei echtem Konflikt — ein echter Konflikt am eigenen Datum braucht zwei Geräte gleichzeitig; ein eigener Problemcode ist ausgeschlossen (Never), die Lösung wäre eine neue Unterscheidung.
  - `[low]` `[reject]` (edge) Direkt geöffnetes Detail zeigt einer übernahmeberechtigten, nicht gebundenen Person Knöpfe, die 404 liefern — der Weg über die Liste bindet vorher; nur bei direkt eingetippter Adresse, die Lösung wäre ein neues Feld.
  - `[low]` `[patch]` (edge) Richtlinien-Card bleibt bei `null` im Ladezustand — `null` gilt jetzt wie ein Ladefehler, bUnit-Fall ergänzt.
  - `[low]` `[patch]` (edge) Zustand der Detailseite wandert beim Wechsel zu einer anderen Einsicht mit — Rücksetzen bei geänderter `EinsichtId`, bUnit-Fall ergänzt.
  - `[low]` `[reject]` (edge, claim) Approach sagt „automatisch mit jeder Freizeit-Rolle“, gebaut ist „nur bei erster Mitarbeit“ — die Regeln unter Always gelten (Schleife 1); die Lösung wäre eine Spec-Änderung.
  - `[low]` `[patch]` (verification-gap) `anfragbar = true` nie gegen den echten Server geprüft — Zusicherung nach dem Abschluss auf Org- und Freizeit-Route ergänzt.
  - `[low]` `[reject]` (verification-gap) Aufteilen der Namensabfrage in Blöcke zu 20 ungetestet — mehr als 20 anfragende Organisationen sind unrealistisch; die Folge wären nur fehlende Namen.
  - `[low]` `[reject]` (verification-gap, other) Sortierung der Bestätigungen ungetestet — reine Anzeige-Reihenfolge ohne Fachregel.
  - `[low]` `[reject]` (verification-gap, other) Paralleles Anlegen derselben Art ungetestet — carried.
  - `[false]` `[reject]` (intent) Token-Fälle fehlen — carried.
  - `[false]` `[reject]` (intent) Elternteil als erlaubter Aufrufer gelesen statt abgelehnt — AD-26 erlaubt Elternteile nur für Übersichten der Kinder; Nachweise gehören Mitarbeitenden, die Spec legt `404` fest.
  - `[false]` `[reject]` (intent) Kopie auf dem Gerät der prüfenden Person — AD-15 schreibt `attachment` für PDF vor; AD-9 meint Kopien bei der Organisation in Zeltlotse; die Doku nennt das Löschen.
  - `[false]` `[reject]` (intent) Keine Auswahl von Organisationen durch die Person (FR-12) — carried.
  - `[low]` `[reject]` (intent) „Pflicht“ ohne Wirkung — gleiche Lage wie blind, verworfen.
  - `[false]` `[reject]` (intent) `org-admin/index.md` verspricht ein einstellbares Freigabefenster — der Satz steht unverändert seit 0-4 und meint das Freigabefenster der Freizeitpässe (FR-24), nicht die 14 Tage der Nachweis-Freigabe.
  - `[false]` `[reject]` (intent) Test vor dem Code und Doku-Build nicht im Diff belegt — carried.

## Design Notes

- **Wer prüft eine automatische Einsicht:** FR-10 nennt „wer angefordert hat“, AD-9 „genau eine prüfende Person“. Bei der Anforderung durch Mitarbeit hat kein Mensch angefordert. Darum darf jede Person mit Prüfrecht in der Organisation die Prüfung ausdrücklich übernehmen: jede Leitung einer Freizeit, in der die Person eine Rolle hat, oder eine Org-Admin, nie die Person selbst. Ab dann ist sie die eine prüfende Person, alle anderen bekommen `404`. So lässt sich auch eine einzelne Leitung prüfen (durch die Org-Admin), und mehrere Freizeiten derselben Organisation blockieren sich nicht. Von Hand angefragte Einsichten sind sofort gebunden.
- **Befristung 14 Tage:** AD-9 verlangt eine befristete Freigabe. 14 Tage reichen für eine Prüfung neben dem Ehrenamt. Nach Ablauf bleibt die Einsicht offen; die Person gibt dieselbe Einsicht erneut frei, schon entschiedene Arten bleiben entschieden. Erst der Abschluss beendet eine Einsicht. So gibt es nie zwei offene Einsichten je Person und Organisation.
- **Automatisch nur bei der ersten Mitarbeit:** NS-3 sagt: „Braucht die Organisation später erneut Einsicht, fragt sie an und die Person bestätigt.“ Eine neue Freizeit-Rolle fordert deshalb nur an, wenn es für diese Organisation noch nie eine Einsicht gab. Die Neu-Anfrage nach einer Ablehnung umfasst nur die abgelehnten Arten (`NurArten`). Beides vermeidet, dass schon Bestätigtes ohne Anlass erneut gezeigt wird (Datensparsamkeit).
- **Keine Änderung während der Prüfung:** Sonst könnte die prüfende Person ein Datum oder eine Datei bestätigen, die sie nie gesehen hat. Eine Sperre mit `409` ist einfacher als Schnappschüsse am Posten.
- **Organisationsname für die Person (Endpunkt 23):** Personen kennt keine Organisationsnamen (AD-2). Organisationen liefert nur Namen zu Kennungen, die die Person aus ihren eigenen Einsichten kennt. Namen von Organisationen sind keine Personendaten.
- **Wer anfragen kann:** Ein Personenverzeichnis der Organisation gibt es vor FR-7 (2-3) nicht. Personen kennt nur die eigene Liste der Freizeit-Rollen (mit `OrganisationId`). Darum lassen sich alle Personen mit einer Rolle in einer Freizeit der Organisation anfragen. Mit 2-3 kommen Teammitglieder über dieselbe Liste dazu, ohne Änderung hier.
- **Freizeit- und Org-Routen statt eigenem Kontext „prüfende Person“:** So stammt der Mandant aus der Route (AD-6), die zentrale Rechteprüfung greift, und die AD-30-Pflichtfälle 403/404 gelten wie gewohnt. Personen prüft zusätzlich die Bindung an die prüfende Person.
- **Gültig bis beim Empfang berechnet:** AD-9 zählt „gültig bis“ zur Bestätigung und verbietet nur das Eintragen von Hand. Gerechnet wird mit der Richtlinie zum Zeitpunkt der Bestätigung; eine spätere Änderung der Richtlinie wirkt ab der nächsten Bestätigung. Das Ausstellungsdatum bleibt nur bei der Person (Datensparsamkeit).
- **Name statt Organisation im Hinweis:** Personen darf Organisationen und Freizeiten nicht lesen (AD-2). Die Seite zeigt deshalb den Namen der anfragenden Person bzw. den Freizeitnamen aus `GET /api/ich/freizeiten`; Organisationsnamen der Bestätigungen kommen aus Endpunkt 22 (Organisationen liest Namen über Personen).
- **Zwei Schritte beim Hochladen:** EXPERIENCE verlangt, dass ein Eintrag ohne Datei gespeichert bleibt, wenn der Upload scheitert. Darum Eintrag (JSON) und Datei (Multipart) getrennt; das Hochladedatum setzt nur der Upload (FR-8).
- **Neu-Anfrage nach Ablehnung im selben Modul:** FR-10 „die Einsicht wird neu angefordert“ ist eine Folge innerhalb von Personen und braucht kein Ereignis.
- **Vorgabewerte:** NFR-5 verlangt Vorgaben; Werte aus der verbreiteten Praxis der Jugendarbeit (Führungszeugnis 5 Jahre, JuLeiCa 3 Jahre, Erste Hilfe 2 Jahre). Nur das Führungszeugnis ist von Haus aus Pflicht.
- **Ankreuzfeld:** Der Autosave-Baustein kennt nur Textfelder; „Pflicht“ ist ein Ja/Nein. Ein zweites Feld des gemeinsamen Bausteins (AD-29) mit demselben Speicher-Dienst ist die kleinste Erweiterung.
- **AD-30-Pflichtfälle:** Freizeit: Leitung, Team → `403`, fremde Freizeit → `404`. Organisation: Org-Admin, Org-Überblick → `403`, fremde Organisation → `404`. Persönlich: nur eigene Daten; fremde Person (auch aus derselben Organisation) → `404`; ein Elternteil hat auf Nachweise eines Kindes keinen Zugriff (`404`). Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `mitarbeitende/nachweise/`, `leitung/nachweise-pruefen/`, `org-admin/nachweis-richtlinie/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Nachweise mit einmaliger Einsicht (CAP-4, FR-8 bis FR-10, FR-12, AD-9, NS-3):
- **SharedKernel:** `IGeschuetzteDateien`. Dateien werden mit dem Schlüssel der Person verschlüsselt und im Dateispeicher abgelegt; im Speicher liegt nur Chiffre.
- **Personen:**
  - `Nachweis` (je Art einer, Datei verschlüsselt, Ablehnungshinweis `[Besonders]`).
  - `NachweisEinsicht` als Aggregat mit Posten: offen bis zum Abschluss, partieller eindeutiger Index, Freigabe 14 Tage, `NurArten` nach Ablehnung.
  - Automatische Anfrage nur bei der ersten Mitarbeit in einer Organisation. Übernehmen durch genau eine prüfende Person (Leitung einer Freizeit mit Rolle der Person oder Org-Admin).
  - Bestätigen legt `NachweisEingesehen` ab. Datum und Datei sind während der Prüfung gesperrt (`409`).
  - Endpunkte 1–5 und je sieben Prüf-Endpunkte unter Freizeit- und Org-Route.
- **Organisationen:**
  - Nachweis-Richtlinie mit Vorgabewerten (beim Anlegen und per Migration für bestehende Organisationen), Bearbeiten nach AD-19.
  - Empfänger `NachweisBestaetigungSpeichern` speichert nur Art, eingesehen am, gültig bis (berechnet) und von wem.
  - Endpunkte 20–23, Aktionsschlüssel `nachweise-pruefen` und `nachweis-richtlinie-verwalten`.
- **Freizeiten:** `erlaubt` im Kontext für die Leitung.
- **Web-App:**
  - Bausteine `DateiFeld`, `Ankreuzfeld`, `AutosaveAnkreuzfeld`.
  - Seiten „Nachweise“ (Konto), „Nachweise prüfen“ und Detailseite (Freizeit- und Org-Route).
  - Richtlinien-Card auf der Organisationsseite, Knöpfe auf Konto und Freizeit-Startseite.
- **Demo-Daten:** Jonas mit drei Nachweisen samt erfundenen Dateien, freigegeben. Miriam hat übernommen und Erste Hilfe bestätigt. Nach Ablauf gibt Jonas beim erneuten Einspielen wieder frei.
- **Doku:** neue Seiten `mitarbeitende/nachweise.md`, `leitung/nachweise-pruefen.md`, `org-admin/nachweis-richtlinie.md`; Anpassungen in `daten.md`, den Rollen-Startseiten und `erste-schritte/anmelden.md`. Screenshots `nachweise`, `nachweise-pruefen`, `nachweis-einsicht`, `nachweis-richtlinie` (Handy, Laptop); `konto`, `organisation` und `freizeit-start` zeigen die neuen Knöpfe.

**Dateien:**
- `src/Zeltlotse.SharedKernel/Dateien/IGeschuetzteDateien.cs` – verschlüsselte Dateiablage, Registrierung.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/` – Domäne, Commands, Sperren, Endpunkte. Dazu Migration `NachweiseUndEinsicht`, Erweiterung des Empfängers `FreizeitRolleUebernehmen` sowie `NachweisArt` und `NachweisEingesehen` in Contracts.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/` – Richtlinie, Bestätigung, Empfänger, Endpunkte. Dazu Migration `NachweisRichtlinieUndBestaetigung` und `OrgRechte`.
- `src/Modules/Freizeiten/` – `FreizeitRechte.Erlaubt`, Kontext.
- `src/Zeltlotse.Api.Contracts/` – Datenformen, Enums, Felder, Aktionsschlüssel.
- `src/Zeltlotse.Api/Demo/` – Demo-Nachweise und Einsicht.
- `src/Zeltlotse.Ui.Komponenten/`, `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` – Bausteine, API-Clients, Seiten, `Anzeige.Nachweis`.
- `tests/` – SharedKernel, Personen, Organisationen, API (inkl. S3), Abläufe (Durchlauf `NachweisEingesehen`), Ui (bUnit) und E2E-Screenshots. Dazu `Zeltlotse.Testing/DateiSpeicherImSpeicher` und S3 im E2E-Host.
- `doku/` – Seiten und Screenshots.

**Review:**
- **Pass 1** (41 Befunde: high 0, medium 16, low 16, false 9):
  - bad_spec zum Einsicht-Modell, eine Schleife. Spec überarbeitet (siehe Spec Change Log), Code neu abgeleitet.
  - Verworfen mit Begründung: Lösen gebundener Prüfender, Upload vor dem Commit, unerreichbare Fälle (fehlender Nachweis, vernichteter Schlüssel), gleichzeitige Zustellung, paralleles Anlegen sowie alle `false`.
- **Pass 2** (32 Befunde: high 0, medium 0, low 23, false 9):
  - Gepatcht (5 low): Richtlinien-Card bei `null`, Zurücksetzen der Detailseite, API-Zusicherung `anfragbar = true`, Doku-Hinweis zu gespeicherten Fotos, Doku-Hinweis „einzige Leitung = einzige Org-Admin“.
  - Verworfen: siehe Review Triage Log. Unter anderem Wirkung von „Pflicht“ (1-6), Löschen (1-7), Benachrichtigungen (AD-13), Organisationsnamen für alle Angemeldeten (bewusst), 409-Text bei echtem Datumskonflikt, gleichzeitiges Hochladen und Freigeben.
  - Nichts zurückgestellt.

**Folge-Review empfohlen:** nein. Gepatcht wurden in Pass 2 high 0, medium 0, low 5.

**AD-30 (Hinweis des Aufrufers):**
- Jeder neue Endpunkt hat HTTP-Tests gegen echtes PostgreSQL (mit S3-Container).
- **Freizeit:** Leitung `200`, Team `403`, fremde Freizeit `404`, Einsicht einer anderen Organisation `404`, über alle sieben Prüf-Routen.
- **Organisation:** Org-Admin `200`, Org-Überblick `403`, Org-Admin einer anderen Organisation `404` (Prüf-Routen und Richtlinie).
- **Persönlich:** fremde Person (auch aus derselben Organisation) `404`, Elternteil `404`, Antworten nur mit Vertragsfeldern.
- **Token-Fälle:** keine, weil kein Endpunkt dieser Story ein Token nutzt.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 1150 von 1150 grün (Architektur, SharedKernel, Konten, Personen, Organisationen, Freizeiten, Abläufe, API, Ui, E2E mit Doku-Screenshots).
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 20 Seiten, darunter die drei neuen.
- Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Matrix-Audit).

**Restrisiken:**
- Gebundene prüfende Personen lassen sich nicht lösen. Das wird relevant, sobald Rollen entzogen werden können.
- Die Datei wird vor dem Commit in den Speicher geschrieben. Scheitert der Commit, ist die alte Datei weg; die Person lädt erneut hoch.
- Ein echter Wertkonflikt beim Ausstellungsdatum zeigt den Text „gerade zur Prüfung freigegeben“.
- Gleichzeitiges Hochladen und Freigeben könnte die Sperre umgehen.
- „Pflicht“ wirkt erst mit 1-6. Nachweise, Einsichten und Bestätigungen werden erst mit dem Lösch-Dienst (1-7) gelöscht.
- Die Demo-Daten rufen die Outbox-Zustellung direkt auf und brauchen den Dateispeicher. Die Screenshots laufen gegen die echte Uhr.
