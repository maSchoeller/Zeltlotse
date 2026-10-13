---
title: 'Dokumente und Elterninfo'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: 'e10308bc6f196328a3156b461a10416be821f934'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-6-leitungs-cockpit-und-erinnerungen.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Eine Freizeit kann keine Dokumente ablegen. Org-Dokumente lassen sich nicht einbinden, und Eltern finden Infobrief, Packliste und Notfallnummern weder im Konto noch über den Gast-Link (CAP-25, CAP-10, FR-43, FR-68).

**Approach:**
- **Freizeiten** besitzt die Dokumente einer Freizeit. Jedes hat eine Sichtbarkeit: Team, Eltern und Teilnehmende oder öffentlich. Ein Dokument ist entweder eine eigene Datei oder eine Einbindung eines Org-Dokuments per Kennung.
- **Organisationen** besitzt die Org-Dokumente und bietet eine Leseschnittstelle an.
- **Web-App:** Seite „Dokumente“ mit Suche, Detailseite zum Bearbeiten, Seite „Org-Dokumente“ für Org-Admins, Elterninfo je Anmeldung in der Familienübersicht.
- **Öffentliche Seiten:** öffentliche Dokumente auf der Freizeitseite, Elterninfo je Anmeldung über den Gast-Link.
- Dazu Tests, Demo-Daten, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Datenformen** (API nur additiv):
  - `src/Zeltlotse.Api.Contracts/Freizeiten/Dokumente.cs`:
    - `DokumentSichtbarkeit { Team, ElternUndTeilnehmende, Oeffentlich }` mit `JsonStringEnumConverter`.
    - `FreizeitDokument(Guid Id, string Titel, DokumentSichtbarkeit Sichtbarkeit, string Dateiart, long Groesse, DateTimeOffset HochgeladenAm, bool AusOrganisation, uint Version)`.
    - `FreizeitDokumente(IReadOnlyList<FreizeitDokument> Dokumente)`.
    - `OrgDokumentAuswahl(Guid Id, string Titel)`.
    - `OrgDokumenteAuswahl(IReadOnlyList<OrgDokumentAuswahl> Dokumente)`.
    - `DokumentEinbinden(Guid? OrgDokumentId)`.
    - `DokumentVerweis(Guid Id, string Titel, string Dateiart)`.
    - `Elterninfo(string FreizeitName, IReadOnlyList<DokumentVerweis> Dokumente)`.
    - `DokumentFelder` mit den JSON-Namen `datei`, `sichtbarkeit`, `titel`.
    - `Freizeitseite` bekommt das optionale Feld `IReadOnlyList<DokumentVerweis>? Dokumente`.
  - `src/Zeltlotse.Api.Contracts/Organisationen/OrgDokumente.cs`:
    - `OrgDokument(Guid Id, string Titel, string Dateiart, long Groesse, DateTimeOffset HochgeladenAm)`.
    - `OrgDokumente(IReadOnlyList<OrgDokument> Dokumente)`.
  - `Dateiart` ist „PDF“, „JPEG“, „PNG“ oder „WebP“, je nach dem am Inhalt erkannten Typ.
- **Endpunkte.** Alle Listen sind nach `Titel` sortiert (ohne Groß- und Kleinschreibung). Datei-Antworten kommen über `DateiAuslieferung` mit Content-Type aus der Inhaltsprüfung, `nosniff` und immer `Content-Disposition: attachment` (der Helfer bekommt dafür einen optionalen Parameter). Der Dateiname ist der Titel ohne Pfad- und Steuerzeichen plus Endung.

  | # | Endpunkt | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/organisationen/{orgId:guid}/dokumente` | `OrgRechte.Dokumente = [OrgAdmin]` | `200 OrgDokumente` |
  | 2 | `POST …/dokumente` (multipart, Feld `datei`) | dito | `201 OrgDokument`; der Titel ist der Dateiname ohne Endung, gekürzt auf 200 Zeichen, sonst „Dokument“ |
  | 3 | `DELETE …/dokumente/{dokumentId:guid}` | dito | `204`; die Datei wird gelöscht |
  | 4 | `GET …/dokumente/{dokumentId:guid}/datei` | dito | Datei |
  | 5 | `GET /api/freizeiten/{freizeitId:guid}/dokumente` | `FreizeitRechte.Mitarbeit` | `200 FreizeitDokumente` mit allen Sichtbarkeiten, eigenen und eingebundenen |
  | 6 | `POST …/dokumente` (multipart: `datei`, optional `sichtbarkeit`, Vorgabe `Team`) | dito | `201 FreizeitDokument`, Titel wie bei Nr. 2 |
  | 7 | `PATCH …/dokumente/{dokumentId:guid}` (AD-19, genau ein Feld `titel` oder `sichtbarkeit`) | dito | `200 {version, <feld>}` |
  | 8 | `DELETE …/dokumente/{dokumentId:guid}` | dito | `204`; bei eigener Datei wird auch die Datei gelöscht |
  | 9 | `GET …/dokumente/{dokumentId:guid}/datei` | dito | Datei, eingebundene über die Leseschnittstelle |
  | 10 | `GET …/dokumente/org` | dito | `200 OrgDokumenteAuswahl`: Org-Dokumente der eigenen Organisation, die noch nicht eingebunden sind |
  | 11 | `POST …/dokumente/einbindungen {orgDokumentId}` | dito | `201 FreizeitDokument` mit Sichtbarkeit `Team` |
  | 12 | `GET /api/freizeiten/oeffentlich/{token}/dokumente/{dokumentId:guid}` | Anmeldelink-Token | Datei, nur bei Sichtbarkeit `Oeffentlich` |
  | 13 | `GET /api/freizeiten/gast/{gastToken}/anmeldungen/{anmeldungId:guid}/elterninfo` | Gast-Link | `200 Elterninfo` |
  | 14 | `GET …/elterninfo/{dokumentId:guid}` (zu Nr. 13) | Gast-Link | Datei |
  | 15 | `GET /api/ich/anmeldungen/{anmeldungId:guid}/elterninfo` | eigene Person oder Familie (AD-26) | `200 Elterninfo` |
  | 16 | `GET …/elterninfo/{dokumentId:guid}` (zu Nr. 15) | dito | Datei |

  - Nr. 12 bis 14 sind `AllowAnonymous().Zugriffsbegrenzt()`. Nr. 15 und 16 hängen an der vorhandenen Gruppe `/ich/anmeldungen` (`.Organisationsuebergreifend()`).
  - `GET /api/freizeiten/oeffentlich/{token}` füllt `Dokumente` mit den Dokumenten der Sichtbarkeit `Oeffentlich`.
- **Fehler:**
  - Upload: Datei fehlt, leer, größer als 10 MB oder kein Bild/PDF → `400` am Feld `datei`, mit `DateiPruefung` und denselben Texten wie bei den Nachweisen.
  - Sichtbarkeit ungültig → `400` am Feld `sichtbarkeit`.
  - PATCH `titel`: leer oder länger als 200 Zeichen → `400`. Bei einer Einbindung → `400` mit dem Text „Den Titel pflegt die Organisation.“ Gleiches Feld zwischenzeitlich geändert → `409 konflikt`.
  - Einbinden: `orgDokumentId` fehlt → `400`. Unbekannt in der Organisation der Freizeit (auch fremde Organisation) → `404`. Schon eingebunden → `409 konflikt`.
  - Phase `Gelaufen`: Nr. 6, 7, 8 und 11 → `409 freizeit-abgeschlossen` (Muster `TeamNotizRegeln.AbgeschlossenAsync`).
  - Unbekanntes Dokument oder Dokument einer anderen Freizeit → `404`.
- **Zugriff Elterninfo (Nr. 13 bis 16, eine Regel an einer Stelle):**
  - Die Anmeldung gehört einer erlaubten Person: beim Gast `ElternPersonId` und `KindPersonIds` aus `IGastZugaenge.PruefenAsync`, im Konto `IVertretung.PersonenAsync(angemeldet.PersonId)`.
  - Sie ist nicht abgesagt.
  - Geliefert werden nur Dokumente ihrer Freizeit mit Sichtbarkeit `ElternUndTeilnehmende`.
  - Sonst `404`.
  - `FreizeitName` kommt aus der Freizeit.
- **Mandant (AD-6):** Jede Anfrage betrifft genau eine Freizeit. Nr. 12 setzt den Mandanten wie vorhanden über `AnmeldelinkSuche`. Nr. 13 bis 16 setzen ihn einmal mit `MandantenKontext.Setzen(freizeit.OrganisationId)`, nachdem der Zugriff geprüft ist und bevor Organisationen gelesen wird. Freizeiten liest dort mit `IgnoreQueryFilters()`, eingeschränkt auf die geprüfte Anmeldung, nach dem Muster von `AnmeldungenDerPersonen`.
- **Leseschnittstelle** `Zeltlotse.Organisationen.Contracts/Dokumente/IOrgDokumenteLesen` (unter dem Mandantenfilter, Pfeil Freizeiten → Organisationen):
  - `ListeAsync()` → `OrgDokumentInfo(Id, Titel, Dateiart, Groesse, HochgeladenAm)`.
  - `InfosAsync(IReadOnlyCollection<Guid> ids)`.
  - `DateiAsync(Guid id)` → Inhalt, Content-Type und Dateiart, sonst `null`.
- **Einbindung ohne Kopie:** Titel, Art, Größe und Datei einer Einbindung kommen bei jedem Lesen aus Organisationen. Löscht der Org-Admin das Org-Dokument, fällt die Einbindung aus allen Listen, und ihre Datei antwortet mit `404`.
- **Entitäten** (je `IOrganisationsEigen`, `uint Version` als `IsRowVersion()`, neue Migration je Modul):
  - `freizeiten.dokument`:
    - Felder: `Id`, `OrganisationId`, `FreizeitId`, `OrgDokumentId?`, `Titel?` (bei Einbindung `null`), `Sichtbarkeit`, `Dateiart?`, `Groesse?`, `HochgeladenAm`.
    - Eindeutig (`FreizeitId`, `OrgDokumentId`), wo `OrgDokumentId` gesetzt ist.
    - Feldänderung über eine Methode der Entität.
  - `organisationen.dokument`: `Id`, `OrganisationId`, `Titel`, `Dateiart`, `Groesse`, `HochgeladenAm`.
  - Dateien liegen unverschlüsselt über `IDateiSpeicher`, unter dem Präfix des Modulschemas, mit der Dokument-Id als Kennung.
  - Keine Personenkennung, also kein `PersonId`, kein `HochgeladenVon`.
  - Schreiben nur über interne Commands mit `IModulCommand<…DbContext>` und `IBenutzerCommand`. Die Bytes tauchen nicht in `ToString()` auf (Muster `NachweisDateiHochladen`).
- **Aktionsschlüssel** `OrgDokumenteVerwalten = "org-dokumente-verwalten"` in `Aktionsschluessel`, berechnet in `OrgRechte.Erlaubt`. Die Freizeit-Seiten brauchen keinen Schlüssel, weil Leitung und Team alles dürfen.
- **Web-App** (nur Bausteine aus `Ui.Komponenten`, API nur über die Ui-Clients, Texte über `Anzeige`):
  - **`Features/Freizeiten/Dokumente.razor` (`/freizeiten/{FreizeitId:guid}/dokumente`):**
    - Oben h1 „Dokumente“, darunter `Feld Typ="search"` mit dem Label „Dokumente suchen“. Es filtert beim Tippen beide Listen nach Titel (enthält, ohne Groß- und Kleinschreibung).
    - Card „Dokumente der Freizeit“ als `Datentabelle` mit den Spalten:
      - Titel als Link „<Titel>“ auf Nr. 9 (`target="_blank" rel="noopener"`).
      - Sichtbarkeit: „Team“, „Eltern und Teilnehmende“ oder „Öffentlich“.
      - Herkunft: „Freizeit“ oder „Organisation“.
      - Link „Bearbeiten“.
    - Ohne Dokumente `LeerZustand` „Noch keine Dokumente.“; ohne Treffer „Keine Dokumente gefunden.“
    - Card „Dokument hochladen“: `Auswahl` „Sichtbarkeit“ (Vorgabe Team) und `DateiFeld`. Die Wahl lädt sofort hoch.
      - Fehlerbild wie in `NachweiseAnsicht`: Feldfehler, Meldung „Fehler: Datei nicht hochgeladen.“ mit „Erneut versuchen“.
      - Erfolg: Meldung „Hochgeladen: <Titel>.“, die Liste lädt neu.
    - Card „Aus der Organisation einbinden“ (nur mit Einträgen): je Org-Dokument der Titel und der Knopf „Einbinden“. Danach Meldung „Eingebunden: <Titel>. Sichtbar für das Team.“, beide Listen laden neu.
    - In Phase `Gelaufen` entfallen Hochladen und Einbinden. Es erscheint die Meldung „Diese Freizeit ist abgeschlossen und nur lesbar.“
  - **`Features/Freizeiten/DokumentBearbeiten.razor` (`/freizeiten/{FreizeitId:guid}/dokumente/{DokumentId:guid}`):**
    - Lädt die Liste Nr. 5 und wählt daraus das Dokument; fehlt es, steht `NichtVerfuegbar`.
    - `AutosaveFeld` „Titel“ mit dem Pfad aus `FreizeitenApiClient.DokumentPfad`. Bei Einbindungen steht der Titel als Text mit dem Hinweis „Titel und Datei pflegt die Organisation.“
    - `AutosaveAuswahlgruppe` „Sichtbarkeit“.
    - Link „Datei öffnen“.
    - `InlineBestaetigung` „Entfernen“ mit dem Satz „Dokument <Titel> aus der Freizeit entfernen?“ und dem Knopf „Ja, entfernen“. Danach geht es zurück zur Liste.
    - Zurück-Link „Zurück zu den Dokumenten“. In Phase `Gelaufen` ist alles nur lesbar.
  - **`FreizeitStart.razor`:** Knopf „Dokumente“ (`Ansichtswechsel`) für alle Rollen.
  - **`Features/Organisationen/OrgDokumente.razor` (`/organisationen/{OrgId:guid}/dokumente`):**
    - h1 „Org-Dokumente“ und der Satz „Freizeiten der Organisation binden diese Dokumente ein, ohne sie zu kopieren.“
    - `Datentabelle` mit Titel-Link auf Nr. 4, „Hochgeladen am“ und `InlineBestaetigung` „Entfernen“. Der Satz dort lautet „Org-Dokument <Titel> entfernen? Es verschwindet auch aus allen Freizeiten.“
    - Upload wie oben, ohne Sichtbarkeit.
    - In `OrganisationAnsicht` steht mit `org-dokumente-verwalten` die Card „Org-Dokumente“ mit dem Knopf „Org-Dokumente öffnen“.
  - **Familie:** In `FamilieAnsicht` trägt jede nicht abgesagte Anmeldung einen Link „Elterninfo“ auf `/familie/elterninfo/{AnmeldungId}`.
    - Seite `Features/Personen/ElterninfoAnsicht.razor`: h1 „Elterninfo“, Dachzeile mit dem Freizeitnamen, Liste der Dokumente als Links auf Nr. 16 mit „<Titel> (<Dateiart>)“.
    - Ohne Dokumente „Noch keine Elterninfo.“, bei `404` `NichtVerfuegbar`.
    - Zurück-Link zur Familienübersicht.
- **Öffentliche Seiten** (nur statisch taugliche Bausteine):
  - `Freizeitseite.razor`: Card „Dokumente“, nur mit Einträgen, Links auf Nr. 12.
  - `GastLink.razor`: Link „Elterninfo“ je nicht abgesagter Anmeldung auf `/gast/{Token}/elterninfo/{AnmeldungId}`.
  - Neue Seite `GastElterninfo.razor` wie `ElterninfoAnsicht`, mit Links auf Nr. 14, `noindex`. Ungültiges Token: Muster `GastLinkUngueltig`, 429 über `ZuVieleAufrufe`.
- Test vor dem Code. Neue Routen in `EndpunktListeTests`: Anonym, Organisationsübergreifend, die Freizeit-Liste, die Zahl der Org-Routen und die Zahl der öffentlichen Routen.

**Never:**
- Keine Dokumente an Programmpunkten: Programmpunkte gibt es erst ab Story 3-1 (siehe Design Notes). Kein Teilnehmer-Zugang (6-4).
- Keine Kopie von Org-Dokumenten oder ihrer Titel in Freizeiten. Kein Ereignis, keine Zeile im Ereignis-Fluss, kein neuer Pfeil, kein neues Modul, keine Änderung an `ARCHITECTURE-SPINE.md`.
- Keine Volltextsuche in Dateien, kein Such-Endpunkt (die Suche filtert im Client).
- Keine öffentlichen oder signierten Datei-URLs, keine Auslieferung `inline`, keine anderen Dateitypen als Bilder und PDF.
- Keine Dateiinhalte, Titel oder Tokens in Logs.
- Keine Pop-ups. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Hochladen | Tobias (Team, X) lädt `infobrief.pdf` mit `ElternUndTeilnehmende` hoch | `201`, Titel „infobrief“, Dateiart „PDF“; Liste Nr. 5 enthält es; Nr. 9 liefert die Bytes als `attachment` | – |
| Upload-Fehler | ohne Datei; `.pdf` mit Textinhalt; 10 MB + 1 Byte; `sichtbarkeit=Geheim` | `400 datei` / `400 datei` / `400 datei` / `400 sichtbarkeit` | – |
| Rechte Freizeit (Nr. 5–11) | Leitung Miriam; Team Tobias; Org-Admin Andrea ohne Freizeit-Rolle; Leitung nur von Z; Freizeit einer fremden Organisation | `200`/`200`/`404`/`404`/`404` | – |
| Rechte Org (Nr. 1–4) | Andrea (Org-Admin A); Org-Überblick in A; Org-Admin von B auf A; Person ohne Org-Rolle | `200`/`403`/`404`/`404` | – |
| Einbinden | Andrea lädt „Hausordnung“ in A; Miriam bindet sie in X ein | Nr. 10 vorher mit, nachher ohne „Hausordnung“; Nr. 5 mit `AusOrganisation=true`, Sichtbarkeit Team; Nr. 9 liefert die Org-Datei | zweites Einbinden `409`; Org-Dokument von B in X `404` |
| Org löscht | Andrea löscht „Hausordnung“ | fehlt in Nr. 5 und in der Elterninfo; Nr. 9 `404` | – |
| PATCH | `titel` „Infobrief 2027“; `sichtbarkeit` `Oeffentlich`; veralteter `alterWert` desselben Felds; `titel` an einer Einbindung; leerer Titel | `200` / `200` / `409 konflikt` / `400` / `400` | – |
| Abgeschlossen | X ist `Gelaufen` | Nr. 6, 7, 8, 11 `409 freizeit-abgeschlossen`; Nr. 5 und 9 `200` | – |
| Sichtbarkeit genau | X hat je ein Dokument Team (T), Eltern (E), öffentlich (O) und eingebunden mit E (H) | Freizeitseite X: nur O; Nr. 12 für T und E `404`; Elterninfo: H, E (sortiert), ohne T und O | – |
| Elterninfo Konto | Katrin für Emmas Anmeldung in X; Katrin für ihre Anmeldung in einer fremden Organisation; Daniel (andere Familie) für Emmas Anmeldung; Emma abgesagt; Dokument aus Z über Emmas Anmeldung in X | `200` / `200` mit den Dokumenten dieser Freizeit / `404` / `404` / `404` | – |
| Token Gast (Nr. 13, 14) | gültiger Gast-Link; nach Ende des Freigabefensters (Uhr); Anmeldelink-Token statt Gast-Token; unbekanntes Token; Anmeldung einer anderen Familie; Dokument der Freizeit Z | `200` / `404` / `404` / `404` / `404` / `404` | – |
| Token öffentlich (Nr. 12) | Anmeldelink X, O aus X; erneuerter Anmeldelink; Gast-Token; Anmeldelink Z mit O aus X | Datei / `404` / `404` / `404` | – |
| Demo | Lea öffnet „Dokumente“ von „Zeltlager Sommer 2027“ | sechs Dokumente: „Anfahrt zum Zeltplatz“ (Öffentlich), „Hausordnung Zeltplatz“ (Eltern und Teilnehmende, Organisation), „Infobrief Zeltlager Sommer 2027“, „Notfallnummern“, „Packliste“ (je Eltern und Teilnehmende), „Programmideen“ (Team); einbindbar „Leitfaden Aufsichtspflicht“ | – |
| Demo Suche | Lea tippt „pack“ | nur „Packliste“ | – |
| Demo Eltern | Katrin öffnet Elterninfo zu Emma | Hausordnung Zeltplatz, Infobrief Zeltlager Sommer 2027, Notfallnummern, Packliste | – |

</intent-contract>

## Code Map

- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/FreizeitenModul.cs:58-103` -- Registrierung:
  - Gruppen `oeffentlich` (Z. 71), `gast` (Z. 78), `freizeit` (Z. 80) mit `.NurFreizeitRolle(FreizeitRechte.Mitarbeit)`, `/ich/anmeldungen` (Z. 101).
  - Für Nr. 13 und 14 eine Gruppe `/gast/{gastToken}` neben dem vorhandenen `MapGet`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/TeamNotizen/TeamNotizBearbeiten.cs` -- Vorlage für Liste, Anlegen (201), Löschen (204), `TeamNotizRegeln.AbgeschlossenAsync`, Commands mit `IModulCommand<FreizeitenDbContext>` und `IBenutzerCommand`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/FreizeitseiteBearbeiten.cs` -- Vorlage für den PATCH nach AD-19: `FromSql … FOR UPDATE`, `FeldAendern`, Ergebnis-Hierarchie, Body als `JsonElement`, `FreizeitseiteProbleme`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/OeffentlicheEndpunkte.cs:37-60` -- `FreizeitseiteOeffentlichEndpunkt.Laden`, `AnmeldelinkSuche.FindenAsync(db, mandant, token, jetzt)` (`Anmeldelinks.cs:122`). Dort `Dokumente` füllen und Nr. 12 daneben.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/AnmeldungenLesen.cs` -- `GastLinkEndpunkt.Laden` (`IGastZugaenge.PruefenAsync`), `MeineAnmeldungenEndpunkt.Laden` (`IAngemeldet`, `IVertretung`), `AnmeldungenDerPersonen` (`IgnoreQueryFilters`). Vorlage für die Elterninfo-Regel.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Daten/FreizeitenDbContext.cs` + `FreizeitenModell.Konfigurieren` -- DbSet in Schreib- und Lese-Kontext; letzte Migration `20261013063153_TeamNotizen`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` -- `Mitarbeit = [Leitung, Team]`.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/NachweisEndpunkte.cs:~215` -- Upload-Vorbild: `ReadFormAsync().Files.GetFile("datei")`, `DateiPruefung.LesenUndPruefenAsync`, `ValidierungsAusnahme`, Command ohne Bytes in `ToString()`.
- `src/Zeltlotse.SharedKernel/Dateien/{IDateiSpeicher.cs,DateiPruefung.cs}` und `DateiAuslieferung` -- Speicher (`SpeichernAsync(praefix, id, inhalt, contentType)`, `LesenAsync`, `LoeschenAsync`), Inhaltsprüfung (`DateiArt`), Auslieferung (bisher Bilder `inline`; optionalen Parameter für `attachment` ergänzen).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/{OrganisationenModul.cs,Rollen/OrgRechte.cs,Daten/OrganisationenDbContext.cs}` -- Gruppe `/organisationen/{orgId:guid}` mit `.NurOrgRolle`, `OrgRechte.Erlaubt`. Neue Dateien in `Dokumente/`. Leseschnittstellen-Vorbild: `IVertrauensAngabenLesen` in `Zeltlotse.Organisationen.Contracts`, dazu ihre Registrierung.
- `src/Zeltlotse.SharedKernel/Mandanten/IMandantenKontext.cs` -- `MandantenKontext.Setzen`.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs:55` -- `Aktionsschluessel`.
- `src/Zeltlotse.Api.Contracts/Freizeiten/{Freizeitseite.cs,Anmeldungen.cs}` -- `Freizeitseite`, `AnmeldungZeile` und `GastLinkAnmeldung` (beide mit `AnmeldungId` und `Status`).
- `src/Zeltlotse.Ui/Api/{FreizeitenApiClient,OrganisationenApiClient,PersonenApiClient}.cs` -- Muster: Multipart in `PersonenApiClient.DateiHochladenAsync` (~Z. 340), Pfad-Helfer wie `EinsichtDateiPfad`/`TeamNotizenPfad`, Ergebnis-Records mit Feldfehlern über `ApiFehler`. Registrierung in `UiRegistrierung.cs`.
- `src/Zeltlotse.Ui/Texte/Anzeige.cs` -- neue Texte für die Sichtbarkeit, `Anzahl`, `Datum`.
- `src/Zeltlotse.App/Features/Personen/NachweiseAnsicht.razor:285-340` -- Upload-Ablauf (`UploadStand`, 10 MB, `IstAktionsfehler`, „Erneut versuchen“). `NachweisEinsichtAnsicht.razor:~79` -- Anker auf die API-Datei.
- `src/Zeltlotse.App/Features/{Freizeiten/FreizeitStart.razor,Organisationen/OrganisationAnsicht.razor:~203,Personen/FamilieAnsicht.razor}` -- Einstiege.
- `src/Zeltlotse.Ui.Komponenten/{DateiFeld,Auswahl,Feld,Datentabelle,InlineBestaetigung,LeerZustand,Meldung}.razor`, `src/Zeltlotse.Ui/Autosave/{AutosaveFeld,AutosaveAuswahlgruppe}`.
- `src/Zeltlotse.Public/Seiten/{Freizeitseite,GastLink,GastFreizeitpass,GastLinkUngueltig}.razor` -- statische Muster, `ZuVieleAufrufe`, `Navigation.NotFound()`.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` -- Schritt `DokumenteAsync` am Ende von `EinspielenAsync`, idempotent. Dateien über `DemoDateien.Pdf("…")`.
- Tests:
  - `tests/Zeltlotse.Api.Tests/{FreizeitpassSzenario.cs,NachweisHelfer.cs,GastFreizeitpassTests.cs,EndpunktListeTests.cs:24,101-110,181-223,234-240,DemoDatenTests.cs}` -- Szenario A/B, X/Z, Miriam, Tobias, Katrin, Emma, Mia. `NachweisHelfer.HochladenAsync` und S3-Fixture für Multipart. `GastAsync` und Uhr nach dem Freigabe-Ende. Konstruktor `(PostgresContainer, S3Container)`.
  - Mandantenfilter-Tests: `tests/Zeltlotse.SharedKernel.Tests/MandantenfilterTests.cs`, `tests/Zeltlotse.Freizeiten.Tests/FreizeitenCommandTests.cs` und `tests/Zeltlotse.Organisationen.Tests/OrganisationenCommandTests.cs`. Die neuen Entitäten kommen dort dazu, wo der Filter je Entität geprüft wird.
  - `tests/Zeltlotse.Ui.Tests/{NachgebildeterHandler.cs,NachweisSeitenTests.cs,LeitungsCockpitSeitenTests.cs,FamilieSeitenTests.cs,OrganisationenSeitenTests.cs}` -- Stub- und Upload-Muster (`InputFileContent.CreateFromBinary`). Öffentliche Seiten: das vorhandene Testprojekt der Public-Seiten (Treffer `GastLink` unter `tests/`).
  - `tests/Zeltlotse.E2E.Tests/{DokuScreenshotTests.cs,E2EHilfe.cs}` -- `KontextAsync(geraet)`, `PerLinkAnmeldenAsync`, `AxeAsync`, `BildAsync`. Mails: `TeamMail` (Lea), `ElternMail` (Katrin), `OrgAdminMail` (Andrea), `GastMail` (Lina, Test `Gast_Link`).
  - `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- `PflichtSeiten`, jedes PNG verlinkt, keine Anrede.
- `doku/src/content/docs/{mitarbeitende,eltern,org-admin,leitung}/` -- Seitenaufbau mit Frontmatter `sidebar.order`. Bilder als `../../../assets/screenshots/<name>-<geraet>.png`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Api.Tests/DokumenteTests.cs`, `OrgDokumenteTests.cs`, `ElterninfoTests.cs` (neu) -- ZUERST: jede Matrix-Zeile gegen PostgreSQL und S3, inklusive Rechte- und Token-Fälle nach AD-30. Dazu `EndpunktListeTests`, `DemoDatenTests` (Matrix-Zeilen „Demo“ und „Demo Eltern“) und die Mandantenfilter-Tests der neuen Entitäten.
- `tests/Zeltlotse.Freizeiten.Tests`, `tests/Zeltlotse.Organisationen.Tests` -- ZUERST: Titel aus dem Dateinamen, Feldänderung der Entität, Einbindung ohne Titel.
- `tests/Zeltlotse.Ui.Tests/DokumenteSeitenTests.cs`, `OrgDokumenteSeitenTests.cs`, `ElterninfoSeitenTests.cs` (neu) -- ZUERST:
  - Liste, Suche, Leer- und Kein-Treffer-Zustand.
  - Upload: Erfolg, Feldfehler, Erneut versuchen.
  - Einbinden, Phase `Gelaufen`.
  - Bearbeiten: Autosave, Einbindung nur Sichtbarkeit, Entfernen.
  - Org-Seite mit Entfernen, Card in `OrganisationAnsicht`.
  - Elterninfo-Link und Elterninfo-Seite.
  - Dazu Tests der öffentlichen Seiten: Card „Dokumente“, Link und Seite „Elterninfo“ mit Gast-Link.
- `src/Zeltlotse.Api.Contracts/{Freizeiten/Dokumente.cs,Freizeiten/Freizeitseite.cs,Organisationen/OrgDokumente.cs,Gemeinsam/FreizeitKontext.cs}` -- Datenformen und Aktionsschlüssel.
- `src/Zeltlotse.SharedKernel/Dateien/` -- Option `attachment` für `DateiAuslieferung`.
- `src/Modules/Organisationen/…/Dokumente/*`, `Zeltlotse.Organisationen.Contracts/Dokumente/IOrgDokumenteLesen.cs`, `OrgRechte.cs`, DbContext + Migration `OrgDokumente`, `OrganisationenModul.cs` -- Org-Dokumente und Leseschnittstelle.
- `src/Modules/Freizeiten/…/Dokumente/*`, DbContext + Migration `Dokumente`, `FreizeitenModul.cs`, `Freizeitseite/OeffentlicheEndpunkte.cs` -- Dokumente, Einbindung, öffentliche und Elterninfo-Endpunkte.
- `src/Zeltlotse.Ui/{Api/*.cs,Texte/Anzeige.cs}` -- Client-Methoden, Pfad-Helfer, Texte.
- `src/Zeltlotse.App/Features/{Freizeiten/Dokumente.razor,Freizeiten/DokumentBearbeiten.razor,Freizeiten/FreizeitStart.razor,Organisationen/OrgDokumente.razor,Organisationen/OrganisationAnsicht.razor,Personen/FamilieAnsicht.razor,Personen/ElterninfoAnsicht.razor}` -- Oberfläche laut Always.
- `src/Zeltlotse.Public/Seiten/{Freizeitseite,GastLink,GastElterninfo}.razor` -- öffentliche Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs`:
  - Org-Dokumente „Hausordnung Zeltplatz“ und „Leitfaden Aufsichtspflicht“.
  - Freizeit-Dokumente: „Infobrief Zeltlager Sommer 2027“, „Packliste“ und „Notfallnummern“ (Eltern und Teilnehmende), „Anfahrt zum Zeltplatz“ (öffentlich), „Programmideen“ (Team).
  - Die Hausordnung wird eingebunden, mit Sichtbarkeit Eltern und Teilnehmende.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`, je Handy und Laptop mit axe:
  - `Dokumente` (Lea, Bild `dokumente`).
  - `Dokumente_Suche` (Lea tippt „pack“, Bild `dokumente-suche`).
  - `Dokument_Bearbeiten` (Lea, Packliste, Bild `dokument-bearbeiten`).
  - `Org_Dokumente` (Andrea, Bild `org-dokumente`).
  - `Elterninfo` (Katrin, Emma, Bild `elterninfo`).
  - `Gast_Elterninfo`: im Ablauf von `Gast_Link`, Bild `gast-elterninfo`.
  - Das vorhandene Bild der Freizeitseite zeigt die Card „Dokumente“ mit „Anfahrt zum Zeltplatz“.
- Doku-Seiten:
  - Neu `doku/src/content/docs/mitarbeitende/dokumente.md` („Dokumente ablegen“): Sichtbarkeiten, Hochladen, Einbinden, Suchen, Bearbeiten, Entfernen, nur lesbar nach dem Abschluss. Hinweis, dass Dokumente am Programmpunkt später dazukommen. Bilder `dokumente`, `dokumente-suche`, `dokument-bearbeiten`.
  - Neu `org-admin/org-dokumente.md`: Einbinden statt Kopieren, Löschen wirkt in allen Freizeiten. Bild `org-dokumente`.
  - Neu `eltern/elterninfo.md`: im Konto und über den Gast-Link. Bilder `elterninfo`, `gast-elterninfo`.
  - Ergänzen: `eltern/freizeitseite.md` (öffentliche Dokumente), `leitung/index.md` (Link auf Dokumente), `daten.md` (wer welches Dokument sieht).
  - Die drei neuen Seiten in `DokuRegeln.PflichtSeiten`.

**Acceptance Criteria:**
- Given Lea (Demo, Team) öffnet über „Dokumente“ auf der Startseite die Dokumente der Freizeit, when sie „pack“ eintippt, then steht nur „Packliste“ in der Liste, und der Titel öffnet die PDF-Datei als Download.
- Given Katrin (Demo) öffnet in der Familienübersicht bei Emmas Anmeldung „Elterninfo“, when die Seite lädt, then sieht sie genau die vier Dokumente mit Sichtbarkeit „Eltern und Teilnehmende“, darunter die eingebundene „Hausordnung Zeltplatz“.
- Given eine Gast-Anmeldung, when der Gast im Gast-Link „Elterninfo“ öffnet, then sieht er dieselben Dokumente ohne Konto, und nach Ende des Freigabefensters antwortet derselbe Link mit „nicht verfügbar“.
- Given die öffentliche Freizeitseite, when sie lädt, then zeigt sie nur „Anfahrt zum Zeltplatz“.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architekturtests), die Bilder der E2E-Aufgabe sind je Handy und Laptop geschrieben und verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 33 findings — high 0, medium 0, low 25, false 8, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind) „Öffentlich“ fehlt in der Elterninfo, die Doku sagt aber „zusätzlich“ — Die Spec will die Sichtbarkeit „genau“ (CAP-25). Patch: `mitarbeitende/dokumente.md` nennt je Stufe genau den Ort.
  - `[low]` `[reject]` (blind) Org-Dokumente lassen sich nicht pflegen (kein PATCH, kein Ersetzen) — Die Spec hat das bewusst so entschieden. „Pflegt die Organisation“ nennt nur, wer die Hoheit hat. Ein neuer Endpunkt wäre zusätzlicher Umfang.
  - `[low]` `[reject]` (blind) Gelöschtes Org-Dokument lässt Einbindungen ohne Hinweis zurück — Design Notes: Die Einbindung wird beim Lesen übersprungen. Aufräumen bräuchte ein Ereignis, und für eine Zählung beim Löschen wäre eine neue Abfrage nötig.
  - `[false]` `[reject]` (blind) Gelöschte Freizeit lässt Dateien zurück — Kein Code löscht Freizeiten. Der Löschlauf entfernt nur Anmeldungen (geprüft: kein `Freizeiten.Remove`/`ExecuteDelete`).
  - `[low]` `[reject]` (blind) Datei und Datenbank können bei gescheitertem Commit auseinanderlaufen — Das kommt selten vor und folgt demselben Muster wie die Nachweise. Abhilfe wäre ein Hook nach dem Commit, also neue Logik.
  - `[false]` `[reject]` (blind) `version` im PATCH ungenutzt — AD-19 entscheidet über `alterWert` je Feld. Die Version liefert der Server für den Client, so wie bei `FreizeitseiteFeldAendern`.
  - `[low]` `[patch]` (blind) Konfliktprüfung der Sichtbarkeit beachtet Groß- und Kleinschreibung — Patch: Der alte Wert wird geparst verglichen. Test dazu.
  - `[low]` `[patch]` (blind) Titel per PATCH ohne Bereinigung von Steuerzeichen — Patch: dieselbe Bereinigung wie beim Upload. Test dazu.
  - `[low]` `[patch]` (blind) Oberfläche schweigt bei Konflikt beim Einbinden und bei gescheitertem Entfernen von Org-Dokumenten — Patch: je eine Fehlermeldung und Neuladen. bUnit-Fälle dazu.
  - `[low]` `[reject]` (blind) Gewählte Sichtbarkeit bleibt für den nächsten Upload stehen — Die Wahl ist sichtbar und gewollt, wenn mehrere Elterninfo-Dokumente nacheinander hochgeladen werden.
  - `[low]` `[reject]` (blind) Titel-Höchstlänge zweimal definiert — `Api.Contracts` darf nichts referenzieren und SharedKernel kennt die API-Verträge nicht. Eine Quelle bräuchte eine neue Abhängigkeit. Beide Werte sind 200 und durch Tests belegt.
  - `[low]` `[reject]` (blind) Keine Mengen- oder Speichergrenze je Freizeit — Das verlangt keine Anforderung. Der Kreis der Organisationen ist bekannt (NFR-19 erst beim Start als Dienst).
  - `[low]` `[patch]` (blind) Testlücken (gleichzeitiges Einbinden u. a.) — Patch: Tests für gleichzeitiges Einbinden und gleichzeitiges PATCH. Die übrigen Lücken decken die Patches oben.
  - `[low]` `[reject]` (blind) Detailseite lädt die ganze Liste — So steht es in der Spec. Eine Freizeit hat wenige Dokumente; ein Einzel-Endpunkt bringt nichts.
  - `[low]` `[patch]` (verification-gap) Gleichzeitiges Einbinden ungetestet — Patch: `Gleichzeitiges_Einbinden_desselben_Org_Dokuments_gelingt_genau_einmal`.
  - `[low]` `[patch]` (verification-gap) Zeilensperre `FOR UPDATE` im PATCH ungetestet — Patch: `Gleichzeitige_Aenderungen_an_Titel_und_Sichtbarkeit_gelingen_beide`.
  - `[low]` `[reject]` (edge) DELETE gleichzeitig mit PATCH oder zweitem DELETE ergibt 500 — Das ist selten, denn die Oberfläche sperrt während der Übertragung. Folge ist nur ein Fehlerhinweis; der Schutz wäre ein neuer Zweig.
  - `[low]` `[reject]` (edge) Commit scheitert nach dem Löschen der Datei — gleiche Ursache wie oben (Datei/Datenbank), verworfen.
  - `[low]` `[reject]` (edge) Org-Dokument gleichzeitig löschen oder Commit scheitert — wie die beiden Zeilen oben.
  - `[low]` `[reject]` (edge) Verwaiste Datei, wenn der Commit nach dem Speichern scheitert — wie oben, selten, keine Wirkung für Anwender.
  - `[low]` `[patch]` (edge) Einbinden mit 409 zeigt nichts — gleiche Ursache und gleicher Patch wie die Zeile „Oberfläche schweigt“ (blind).
  - `[low]` `[patch]` (edge) Upload gelingt, Neuladen scheitert → „Datei nicht hochgeladen“ — Patch: Fehler beim Neuladen getrennt behandeln. bUnit-Fall dazu.
  - `[low]` `[patch]` (edge) Dasselbe auf der Seite „Org-Dokumente“ — derselbe Patch.
  - `[low]` `[patch]` (edge) Groß- und Kleinschreibung beim alten Wert der Sichtbarkeit — gleicher Patch wie oben (blind).
  - `[false]` `[reject]` (edge) Fehler zur Sichtbarkeit erscheint am Dateifeld — Die Oberfläche schickt nur Werte aus der `Auswahl`. Ein 400 an `sichtbarkeit` ist von dort nicht erreichbar.
  - `[low]` `[reject]` (edge) Demo-Daten mit `SingleAsync` bei doppeltem Titel — Das ist das Muster des ganzen Seeds, nur in Entwicklung und Tests. Ein doppelter Titel braucht einen manuellen Upload.
  - `[low]` `[reject]` (edge) Elterninfo einer beendeten Anmeldung bleibt erreichbar, solange der Gast-Link gilt — Es geht um Dokumente für Eltern derselben Familie. Ein Schaden ist nicht erkennbar, und die Prüfung je Freigabe wäre neue Logik.
  - `[false]` `[reject]` (intent) „Falsche Rolle → 403“ fehlt an den Freizeit-Routen — Leitung und Team dürfen beide. `RechtePruefung` gibt Personen ohne Freizeit-Rolle 404. Die 403 ist an den Org-Routen getestet (Org-Überblick, Design Notes).
  - `[low]` `[reject]` (intent) Token-Varianten nicht wörtlich nach AD-30 — Für Nr. 12 deckt der erneuerte Anmeldelink „abgelaufen oder erneuert“ ab. Der Gast-Link endet mit dem Freigabefenster, das ist getestet; ein Erneuern des Gast-Links gibt es nicht.
  - `[false]` `[reject]` (intent) Programmpunkt, Teilnehmer-Zugang und „Suche über alle Dokumente“ — Programmpunkte und Teilnehmer-Zugang gibt es noch nicht (3-1, 6-4). Die Suche läuft über alle Dokumente der Freizeit und die einbindbaren Org-Dokumente (Design Notes).
  - `[false]` `[reject]` (intent) Keine eigene Doku-Seite unter Leitung — Leitung und Team haben dieselben Rechte. `leitung/index.md` verweist auf die Seite unter Mitarbeitende.
  - `[false]` `[reject]` (intent) Doku-Build im Diff nicht belegt — `npm run build` lief fehlerfrei und läuft im Abschluss erneut.
  - `[false]` `[reject]` (intent) Test vor dem Code nicht sichtbar — Das ist eine Eigenschaft des Vorgehens. Für jede Matrix-Zeile liegt ein gelaufener Test vor.

## Design Notes

- **Nur Kontext Freizeit.** FR-43 nennt auch den Programmpunkt, den gibt es aber erst mit dem Modul Programm (3-1). Freizeiten darf Programm nicht lesen (AD-2). Story 3-1 oder eine spätere Programmpunkt-Story ergänzt dann ein optionales `ProgrammpunktId` an `freizeiten.dokument`. Die Liste Nr. 5 zeigt schon heute alle Dokumente der Freizeit, also auch künftige an Programmpunkten. So bleibt die Suche „über alle Kontexte“, ohne dass es Speziallogik braucht.
- **Suche im Client.** Sie filtert die eine Liste der Freizeit und die einbindbaren Org-Dokumente nach Titel. Zu einer Freizeit gehören wenige Dutzend Dokumente, ein Such-Endpunkt bringt nichts. Eine Volltextsuche steht im Ausbau-Pool.
- **Elterninfo je Anmeldung.** Eine Anfrage betrifft genau eine Freizeit, deshalb wird der Mandant genau einmal gesetzt (AD-6), auch wenn eine Familie bei mehreren Organisationen angemeldet ist. Die Anmeldung trägt Freizeit und Berechtigung, und `AnmeldungId` liegt in beiden vorhandenen Datenformen. An Kontrakten ändert sich nichts. Geschwister in derselben Freizeit sehen dieselbe Elterninfo unter jeder Anmeldung; das ist ohne Sonderfall.
- **„Genau für die gewählte Sichtbarkeit“ (CAP-25):** Elterninfo zeigt nur „Eltern und Teilnehmende“, die Freizeitseite nur „Öffentlich“. Team und Leitung sehen alle Dokumente. Gilt der Gast-Link nicht mehr, gilt auch die Elterninfo nicht mehr (FR-17).
- **Rechte:** Dokumente sind keine Leitungsaufgabe (rollen-und-rechte.md), deshalb gilt `Mitarbeit` und es gibt keinen Freizeit-Schlüssel. Org-Dokumente verwaltet nur der Org-Admin. Für AD-30 heißt das: Bei den Freizeit-Endpunkten gibt es im eigenen Mandanten keine falsche Rolle, die `403` ergäbe, weil ein Org-Admin ohne Freizeit-Rolle `404` bekommt (`RechtePruefung`). `403` prüfen die Org-Endpunkte mit Org-Überblick. Token-Fälle gelten für Nr. 12 bis 14.
- **Keine Personendaten, keine Verschlüsselung.** Dokumente speichern keinen Uploader. Damit entfallen Löschklasse, Datenauskunft und `[Besonders]`. Dokumente sind Arbeitsmaterial der Freizeit (Infobrief, Packliste), keine Personendaten im Sinn von AD-14. Die Doku rät, keine Listen mit Personendaten hochzuladen.
- **Gelöschte Org-Dokumente:** Die Zeile der Einbindung bleibt stehen, wird beim Lesen aber übersprungen. Für ein Aufräumen bräuchte es ein Ereignis mit neuer Zeile im Ereignis-Fluss.
- **Einbinden mit Sichtbarkeit Team:** Das ist die sichere Vorgabe. Freigeben für Eltern ist ein bewusster zweiter Schritt auf der Detailseite. Hochladen hat dieselbe Vorgabe.
- **Navigation:** Seitennavigation und „Mehr“ gibt es noch nicht (MainLayout ohne Nav-Slots). Wie bisher führt ein Knopf auf der Startseite zur neuen Seite. Die Gruppe „Ablage“ kommt mit dem Navigations-Umbau.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `mitarbeitende/dokumente/`, `org-admin/org-dokumente/`, `eltern/elterninfo/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-25, CAP-10, FR-43, FR-68):**
- **Organisationen:**
  - Org-Dokumente mit Hochladen, Liste, Datei und Entfernen (nur Org-Admin, Aktionsschlüssel `org-dokumente-verwalten`).
  - Leseschnittstelle `IOrgDokumenteLesen` unter dem Mandantenfilter.
- **Freizeiten:**
  - Dokumente der Freizeit mit drei Sichtbarkeiten (Team, Eltern und Teilnehmende, öffentlich). Leitung und Team dürfen hochladen, ändern (AD-19), entfernen und Org-Dokumente einbinden; die Einbindung kopiert nichts. Nach dem Abschluss sind die Dokumente nur lesbar.
  - Öffentliche Dokumente auf der Freizeitseite.
  - Elterninfo je Anmeldung im Konto (AD-26) und über den Gast-Link. Es gilt eine Zugriffsregel, und der Mandant wird je Anfrage einmal gesetzt.
- **Web-App:**
  - Seiten „Dokumente“ (mit Suche) und „Dokument bearbeiten“.
  - Seite „Org-Dokumente“ mit Card in der Organisation.
  - „Elterninfo“ in der Familienübersicht.
- **Öffentliche Seiten:** Card „Dokumente“ auf der Freizeitseite, „Elterninfo“ im Gast-Link.
- **Nicht enthalten:** Dokumente an Programmpunkten (ab 3-1) und Teilnehmer-Zugang (6-4); siehe Design Notes.

**Dateien:**
- `src/Zeltlotse.Api.Contracts/{Freizeiten/Dokumente.cs,Freizeiten/Freizeitseite.cs,Organisationen/OrgDokumente.cs,Gemeinsam/FreizeitKontext.cs}` – Datenformen, Aktionsschlüssel.
- `src/Zeltlotse.SharedKernel/Dateien/{DateiAuslieferung.cs,DokumentDateien.cs}` – Auslieferung immer als Anhang, gemeinsame Regeln für Titel, Dateiname und Upload.
- `src/Modules/Organisationen/…/{Dokumente/OrgDokumente.cs,Rollen/OrgRechte.cs,Daten/*,Migrationen/*_OrgDokumente*,OrganisationenModul.cs}`, `Zeltlotse.Organisationen.Contracts/Dokumente/IOrgDokumenteLesen.cs` – Org-Dokumente.
- `src/Modules/Freizeiten/…/{Dokumente/*,Freizeitseite/OeffentlicheEndpunkte.cs,Daten/*,Migrationen/*_Dokumente*,FreizeitenModul.cs}` – Dokumente, Einbindung, öffentliche und Elterninfo-Endpunkte.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` – zwei Org-Dokumente, fünf Freizeit-Dokumente, Hausordnung eingebunden.
- `src/Zeltlotse.Ui/{Api/*,Texte/Anzeige.cs}` – Client-Methoden und Texte.
- `src/Zeltlotse.Ui.Komponenten/wwwroot/css/zeltlotse.css` – Dachzeile im Hauptbereich dunkel (Kontrast, axe).
- `src/Zeltlotse.App/Features/{Freizeiten/Dokumente,Freizeiten/DokumentBearbeiten,Freizeiten/FreizeitStart,Organisationen/OrgDokumente,Organisationen/OrganisationAnsicht,Personen/FamilieAnsicht,Personen/ElterninfoAnsicht}.razor` – Oberfläche.
- `src/Zeltlotse.Public/Seiten/{Freizeitseite,GastLink,GastElterninfo}.razor` – öffentliche Oberfläche.
- Tests:
  - Api: `DokumenteTests`, `OrgDokumenteTests`, `ElterninfoTests`, `DokumenteSeitenTests`, `EndpunktListeTests`, `DemoDatenTests`, `RechteTests`, `FreizeitseiteTests`.
  - Module: `Freizeiten.Tests/DokumentTests`, `Organisationen.Tests/OrgDokumentTests`.
  - Ui: `DokumenteSeitenTests`, `OrgDokumenteSeitenTests`, `ElterninfoSeitenTests`.
  - E2E: `Dokumente`, `Dokumente_Suche`, `Dokument_Bearbeiten`, `Org_Dokumente`, `Elterninfo`, `Gast_Elterninfo` in `Gast_Link`.
  - Architektur: `DokuRegeln`, `DokuTests`.
- Doku:
  - Neu: `mitarbeitende/dokumente.md`, `org-admin/org-dokumente.md`, `eltern/elterninfo.md`.
  - Angepasst: `eltern/{freizeitseite,index,familienkonto,kind-anmelden}.md`, `leitung/index.md`, `mitarbeitende/index.md`, `org-admin/index.md`, `daten.md`.
  - Bilder: zwölf neue Screenshots. Neu erzeugt wurden `familie`, `gast-link`, `loeschfristen` und `nachweis-richtlinie` (je Handy und Laptop) sowie `freizeitpass-erinnern-laptop`.

**Review (33 Befunde: high 0, medium 0, low 25, false 8, maybe-false 0):**
- Gepatcht (low):
  - Konfliktprüfung der Sichtbarkeit ohne Groß- und Kleinschreibung.
  - Steuerzeichen werden aus PATCH-Titeln entfernt.
  - Fehlermeldungen beim Einbinden (409) und beim Entfernen von Org-Dokumenten.
  - Scheitert nach einem Upload nur das Neuladen, bleibt die Erfolgsmeldung stehen.
  - Doku nennt die Sichtbarkeiten genau.
  - Tests für gleichzeitiges Einbinden und gleichzeitiges PATCH.
- Zurückgestellt: nichts.
- Verworfen (Begründung im Review Triage Log):
  - Ändern und Ersetzen von Org-Dokumenten.
  - Aufräumen und Warnhinweis bei gelöschten Org-Dokumenten.
  - Gleichlauf von Datei und Datenbank bei gescheitertem Commit.
  - Gleichzeitiges Löschen.
  - Sichtbarkeit bleibt nach dem Upload stehen.
  - Doppelte Titel-Höchstlänge.
  - Speichergrenze.
  - Detailseite lädt die Liste.
  - `SingleAsync` im Demo-Seed.
  - Elterninfo einer beendeten Anmeldung bei weiter gültigem Gast-Link.
  - Token-Varianten.
  - alle `false`.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 0, low 13.

**AD-30:**
- Freizeit-Endpunkte: Leitung und Team `200`; Org-Admin ohne Freizeit-Rolle, Leitung einer anderen Freizeit und fremde Organisation `404`. Eine falsche Rolle mit `403` gibt es hier nicht, weil beide Rollen dürfen.
- Org-Endpunkte: Org-Überblick `403`, fremde Organisation `404`.
- Token-Fälle für öffentliche Datei und Gast-Link: gültig; erneuert bzw. nach dem Freigabefenster; anderer Zweck; andere Freizeit.
- Persönliche Endpunkte: Person selbst, Elternteil, fremde Familie `404`.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test` nach den Patches (ganze Lösung inklusive Architektur, E2E und Screenshots): 1730 von 1730 bestanden.
- `npm ci` und `npm run build` in `doku/`: fehlerfrei, 35 Seiten, darunter die drei neuen (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen Test.
- 24 Screenshots mit reinem Rendering-Rauschen (höchstens 294 Pixel; `freizeitpass-fragen-laptop` nur zufälliger Link-Token) per `git checkout` zurückgesetzt.

**Restrisiken:**
- Die Regel für die Dachzeile im gemeinsamen CSS gilt jetzt im ganzen Hauptbereich (dunkel), in Cards weiter Blaugrün.
- Einbindungen gelöschter Org-Dokumente bleiben als Zeile stehen und werden nur übersprungen.
- Scheitert ein Commit, können Datei und Zeile auseinanderlaufen (selten).
- Der E2E-Test `GastAnmeldungTests` scheiterte während der Umsetzung einmal an einer CSP-Meldung zum `data:`-Pfeil. Allein und im Abschlusslauf lief er grün.
- Tests mit festen Daten (Freizeit 2027) laufen ab Sommer 2027 aus, wie die bestehenden.
