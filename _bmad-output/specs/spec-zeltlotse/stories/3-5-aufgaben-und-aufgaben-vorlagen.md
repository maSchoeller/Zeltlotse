---
title: 'Story 3-5: Aufgaben und Aufgaben-Vorlagen'
type: 'feature'
created: '2026-10-14'
status: 'done'
baseline_revision: '349af4735407c5f51f5eff5412c18c6c229d8dce'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-1-wochenraster-und-programmpunkte.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Team kann keine Aufgaben festhalten, niemand sieht „Meine Aufgaben“, die Organisation kann keine wiederkehrenden Aufgaben als Vorlage weitergeben, und das Leitungs-Cockpit hat keinen Warnhinweis zu unbesetzten Aufgaben (CAP-23, FR-39, FR-40, FR-27, NS-4, NS-5, NS-12).

**Approach:** Programm bekommt `Aufgabe` (Freizeit; Titel, Phase, optional Programmpunkt, optional übernehmende Person, erledigt, optional Kennung der Vorlage) und `AufgabenVorlage` (Organisation; Titel, Phase). Leitung und Team pflegen, übernehmen und haken Aufgaben ab unter `/api/freizeiten/{freizeitId}/programm/aufgaben`; der Org-Admin pflegt Vorlagen unter `/api/organisationen/{orgId}/aufgaben-vorlagen`. Beim ersten Laden der Aufgaben einer Freizeit übernimmt Programm einmalig alle Vorlagen der Organisation (Muster Mahlzeiten aus 3-1). Eine Aufgabe lässt sich per Knopf als Vorlage speichern. Die Web-App zeigt eine Aufgaben-Seite mit Reiter „Meine Aufgaben“, eine Detailseite, die Card „Aufgaben“ am Programmpunkt, die Card „Meine Aufgaben“ und den Cockpit-Warnhinweis auf der Startseite sowie die Org-Seite „Aufgaben-Vorlagen“.

## Boundaries & Constraints

**Always:**
- Datenformen in `src/Zeltlotse.Api.Contracts/Programm/Aufgaben.cs` (additiv): `enum AufgabenPhase { Vorbereitung = 1, Waehrend = 2, Nachbereitung = 3 }` (JSON als Text wie `VorfallArt`); `Aufgabe(Guid Id, uint Version, string Titel, AufgabenPhase Phase, Guid? ProgrammpunktId, Guid? UebernommenVon, bool Meins, bool Erledigt, bool Vorlage)` (`Vorlage` = es gibt die verknüpfte Vorlage noch); `AufgabeNeu(string? Titel, AufgabenPhase? Phase, Guid? ProgrammpunktId)`; `AufgabenVorlage(Guid Id, uint Version, string Titel, AufgabenPhase Phase)`; `AufgabenVorlageNeu(string? Titel, AufgabenPhase? Phase)`; `enum AufgabeFeld { Titel, Phase }` und `enum AufgabenVorlageFeld { Titel, Phase }`; `AufgabenFelder` (JSON-Namen `titel`, `phase`, `programmpunktId`; Titel 1–120 Zeichen; Phase als Text des Enum-Namens; `JsonName`). Aktionsschlüssel `AufgabenVorlagenVerwalten = "aufgaben-vorlagen-verwalten"` in `Gemeinsam/FreizeitKontext.cs`.
- Endpunkte (Modul Programm):

  | # | Route | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId}/programm/aufgaben` | `ProgrammRechte.Mitarbeit` | `200 IReadOnlyList<Aufgabe>`; vorher interner Command `AufgabenVorlagenUebernehmen`; sortiert nach Phase, Titel, Id |
  | 2 | `POST …/programm/aufgaben` (`AufgabeNeu`) | Mitarbeit | `201 Aufgabe`; Phase fehlt → Vorbereitung; `programmpunktId` unbekannt oder andere Freizeit → `404` |
  | 3 | `PATCH …/programm/aufgaben/{aufgabeId}` | Mitarbeit | AD-19 wie `ProgrammpunktFeldAendern`: `200 {version, titel|phase}`, `409 konflikt` nur bei abweichendem Altwert desselben Felds |
  | 4 | `DELETE …/programm/aufgaben/{aufgabeId}` | Mitarbeit | `204` |
  | 5/6 | `POST …/aufgaben/{aufgabeId}/uebernehmen` bzw. `/abgeben` | Mitarbeit | `200 Aufgabe`, Regeln wie `Programmpunkt.Uebernehmen/Abgeben` (nur für sich selbst, idempotent, Platzhalter `GeloeschtePerson.Id` gilt als frei); andere Person → `409 konflikt` |
  | 7/8 | `POST …/aufgaben/{aufgabeId}/erledigt` bzw. `/offen` | Mitarbeit | `200 Aufgabe`, idempotent; jede Person der Freizeit darf abhaken |
  | 9 | `POST …/aufgaben/{aufgabeId}/vorlage` | Mitarbeit | `200 Aufgabe` mit `Vorlage = true`: legt `AufgabenVorlage` (Titel, Phase) in der Organisation der Freizeit an und merkt `VorlageId`; gibt es die verknüpfte Vorlage schon, nichts Neues |
  | 10 | `GET /api/organisationen/{orgId}/aufgaben-vorlagen` | `ProgrammRechte.Vorlagen` = `[OrgAdmin]` | `200 IReadOnlyList<AufgabenVorlage>`, nach Phase, Titel |
  | 11 | `POST /api/organisationen/{orgId}/aufgaben-vorlagen` | Vorlagen | `201 AufgabenVorlage`; Phase fehlt → Vorbereitung |
  | 12 | `PATCH …/aufgaben-vorlagen/{vorlageId}` | Vorlagen | AD-19 wie #3 |
  | 13 | `DELETE …/aufgaben-vorlagen/{vorlageId}` | Vorlagen | `204`; Aufgaben mit dieser `VorlageId` bleiben, ihr `Vorlage` wird `false` |
- Validierung: `titel` Pflicht, getrimmt, 1–120 Zeichen; `phase` bekannt → sonst `400` am Feld.
- Unbekannte Kennung oder Kennung einer anderen Freizeit/Organisation → `404`. Schreibende Freizeit-Commands (#2–#9) prüfen die Phase über `IFreizeitZeitpunkte`: `Gelaufen` → `409 freizeit-abgeschlossen`; #1 liefert weiter. Org-Commands hängen an keiner Freizeit.
- `AufgabenVorlagenUebernehmen(FreizeitId)` (kein `IBenutzerCommand`): Ist die Phase nicht `Gelaufen`, legt es per `INSERT … ON CONFLICT DO NOTHING` eine Zeile `programm.aufgaben_vorlagen_uebernahme` (FreizeitId als Schlüssel, OrganisationId, UebernommenAm) an; nur wenn diese Zeile neu ist, kopiert es alle Vorlagen der Organisation als Aufgaben (Titel, Phase, `VorlageId`). Später angelegte Vorlagen fließen nur in Freizeiten, deren Aufgaben noch niemand geöffnet hat. Zwei gleichzeitige GETs kopieren genau einmal.
- Entitäten `Aufgabe` (Tabelle `aufgabe`, FK auf `programmpunkt` mit Cascade wie Material) und `AufgabenVorlage` (Tabelle `aufgaben_vorlage`): `IOrganisationsEigen`, `uint Version` (xmin), Zeilensperren `AufgabeSperrenAsync`/`VorlageSperrenAsync` (`FOR UPDATE`), Indizes auf OrganisationId, FreizeitId, ProgrammpunktId, PersonId. Statuswechsel nur über Entitätsmethoden (AD-28). Migration `AufgabenUndVorlagen`.
- AD-16: Die Übernahme ist „Arbeitsdaten der Freizeit“: `ProgrammPersonLoeschen` ersetzt `Aufgabe.PersonId` durch den Platzhalter, `ProgrammAuskunft` nennt übernommene Aufgaben, `ProgrammLoeschlauf` löscht Aufgaben und Übernahme-Zeilen der Freizeiten in `FreizeitenMitAbgelaufenenDaten`. Vorlagen haben keinen Personenbezug.
- Rechte: `ProgrammRechte.Vorlagen` gleich `OrgRechte.AufgabenVorlagen` (neu, `[OrgAdmin]`), daraus `Erlaubt` → `aufgaben-vorlagen-verwalten`; ein Test hält beide gleich. Programm referenziert `Organisationen.Contracts` nicht (Org-Rollen kommen aus `SharedKernel.Rechte`).
- Namen der Übernehmenden setzt der Client zusammen wie im Wochenraster (Leitung aus `GET /api/freizeiten/{id}`, Team aus `…/personenliste`); fehlt ein Name, „Übernommen“.
- Testgetrieben (AD-30). Pflichtfälle: Freizeit-Routen Leitung und Team `2xx`, Org-Admin ohne Freizeit-Rolle `404`, fremde Organisation `404`, Aufgabe einer anderen Freizeit `404`, ohne Sitzung `401`. Org-Routen: Org-Admin `2xx`, Org-Überblick `403` (falsche Rolle), Org-Admin einer fremden Organisation `404`, Person ohne Org-Rolle `404`, ohne Sitzung `401`. Token-Fälle entfallen (kein Token-Endpunkt).
- Oberfläche nur aus `Zeltlotse.Ui.Komponenten`, Rechte nur über `Erlaubt`/Aktionsschlüssel bzw. Erfolg der Abrufe; Texte Deutsch ohne Anrede; in Phase Gelaufen nur lesbar.

**Never:** Zuteilen durch die Leitung; personenbezogene Massen-Aufgaben, persönliche Checklisten, feste Startlisten pro Freizeittyp (Non-goals); Fälligkeitsdaten, Erinnerungen, Mails oder Push; Vermerk, wer abgehakt hat; Spine-Änderung oder neues Ereignis (kein `FreizeitAngelegt`); Programm liest Personen oder Organisationen; Programmbibliothek (3-6); Abschluss-Endpunkt und Abschluss-Warnung (3-8); Seiten- oder untere Navigation; neue Problemcodes; Pop-ups; Commit auf `main`, Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vorlagen fließen ein | Org A hat Vorlagen „Zeltplatz-Vertrag prüfen“ (Vorbereitung), „Zeltplatz übergeben“ (Nachbereitung); erstes GET #1 in X | beide als Aufgaben mit `Vorlage = true`; zweites und gleichzeitiges GET kopieren nichts doppelt; danach angelegte Vorlage erscheint in X nicht | X `Gelaufen` → nichts kopiert |
| Anlegen | Lea POST `{titel: " Andacht vorbereiten ", phase: waehrend, programmpunktId: Lagerfeuer}` | `201`, Titel getrimmt, unbesetzt, offen | Titel leer/121 Zeichen, Phase unbekannt → `400` am Feld; Punkt einer anderen Freizeit → `404` |
| Ändern | PATCH `phase` mit passendem Altwert | `200 {version, phase}` | abweichender Altwert → `409 konflikt` |
| Übernehmen | Lea `uebernehmen` / nochmal / Tobias danach | `200 Meins=true` / `200` / `409 konflikt` | `abgeben` durch Tobias → `409` |
| Abhaken | Tobias `erledigt` auf Leas Aufgabe, dann `offen` | `200 Erledigt=true`, dann `false`; Übernahme bleibt | – |
| Als Vorlage | `vorlage` zweimal | eine neue Vorlage in A, beide `200` mit `Vorlage = true` | Vorlage danach gelöscht → `Vorlage = false`, erneutes `vorlage` legt neu an |
| Abgeschlossen | X `Gelaufen` (SQL) | GET `200` | #2–#9 → `409 freizeit-abgeschlossen` |
| Org-Vorlagen | Andrea POST/PATCH/DELETE | `201`/`200`/`204` | Titel leer → `400`; unbekannt → `404` |
| Rechte | siehe Pflichtfälle | wie dort | – |
| Programmpunkt gelöscht | Punkt mit Aufgabe löschen | Aufgabe weg | – |
| Person gelöscht | `PersonGeloescht` für Lea | `PersonId` = Platzhalter, Aufgabe gilt als frei | – |
| Frist | Löschlauf mit X abgelaufen | Aufgaben und Übernahme-Zeile von X weg, Vorlagen bleiben | – |
| Mandant | Vorlagen in A und B | Lese-Kontext je Mandant zeigt nur die eigenen | – |

</intent-contract>

## Code Map

- `src/Modules/Programm/Zeltlotse.Programm/ProgrammModul.cs` -- Freizeit-Gruppe `…/programm` (Routen #1–#9 anhängen), neue Org-Gruppe `api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/aufgaben-vorlagen")` mit `.NurOrgRolle(ProgrammRechte.Vorlagen)` (Muster `InventarModul.cs:39-45`).
- `src/Modules/Programm/Zeltlotse.Programm/Programme/{Programmpunkt.cs (Uebernehmen/Abgeben/Uebernommen/FeldAendern 51-161),ProgrammpunktUebernehmen.cs,ProgrammpunktBearbeiten.cs,ProgrammpunktLoeschen.cs (Phase-Prüfung 194-218),MahlzeitenSicherstellen.cs (INSERT … ON CONFLICT, kein IBenutzerCommand),WochenrasterLaden.cs (ProgrammProbleme),FeldPatch.cs,ProgrammRechte.cs}` -- Vorlagen für Entität, Slices, Übergänge, PATCH, Probleme; `ProgrammRechte` um `Vorlagen` (Org-Rollen aus `SharedKernel.Rechte.OrgRolle`, Muster `InventarRechte.cs`).
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/{Material.cs,MaterialAnlegen.cs}` -- FK-Cascade auf Programmpunkt, Anlegen am Punkt mit 404 für fremden Punkt.
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` -- DbSets, Sperren (`ProgrammpunktSperrenAsync` 320-324), `ProgrammModell` (Material-FK 365-379); Lese-Context ebenso.
- `src/Modules/Programm/Zeltlotse.Programm/Datenschutz/ProgrammDatenschutz.cs` -- `ProgrammPersonLoeschen`, `ProgrammAuskunft`, `ProgrammLoeschlauf` erweitern.
- `src/Modules/Inventar/Zeltlotse.Inventar/{Artikel/ArtikelAnlegen.cs,Artikel/ArtikelBearbeiten.cs}` -- Org-Command-Muster mit `IMandantenKontext` (OrganisationId aus dem Mandanten).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs:44-80` -- neue Liste `AufgabenVorlagen` und Eintrag in `Erlaubt`; `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs` -- Aktionsschlüssel.
- `src/Zeltlotse.Api.Contracts/Programm/Programm.cs` -- `ProgrammpunktFelder` (Muster für `AufgabenFelder`); `Sicherheit/Vorfaelle.cs:6` JSON-Enum-Muster.
- `tests/Zeltlotse.Programm.Tests/` -- Entitäts-, Validator-, Mandantenfilter-, PersonGeloescht-Tests (Muster 3-1/3-2); Rechte-Gleichheitstest wie bei Inventar (`tests/Zeltlotse.Inventar.Tests`).
- `tests/Zeltlotse.Api.Tests/{ProgrammTests.cs,InventarTests.cs (Org-Rechte, Org-Überblick per OrgHelfer.EinladenAsync/AnnehmenAlsAsync),FreizeitpassSzenario.cs (_s.Andrea/Lea/Miriam/Tobias/Gerd/Anonym, Orgs A/B, Freizeiten X/Z, Gelaufen per SQL),EndpunktListeTests.cs (Programm-Liste 251-263, Assert.Equal(29, mitOrg.Count) 294 → 33),DemoDatenTests.cs,DatenauskunftTests.cs,LoeschlaufTests.cs,PersonGeloeschtTests.cs}` + Org-Kontext-Test für `Erlaubt`.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Texte/Anzeige.cs}` -- Client-Muster (GET null bei 403/404, Ergebnis Erfolg/Konflikt/Abgeschlossen/Fehler, Feldfehler, Pfad-Builder für AutosaveFeld).
- `src/Zeltlotse.App/Features/Programm/{Wochenraster.razor (Namen der Übernehmenden, Reiter),ProgrammpunktSeite.razor (Cards Material),Materialliste.razor}` -- Seitenmuster; `src/Zeltlotse.App/Features/Inventar/OrgInventar.razor` -- Org-Seite mit Formular, AutosaveFeld, InlineBestaetigung.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- Warnhinweise-Zeilen (`AnzahlZeilen` 220, `AlleZeilenGeladen` 223, Liste 104-117), Knopf-Liste 148-167, paralleles Laden 297-322; `Features/Organisationen/OrganisationAnsicht.razor:59-71` -- Card-Muster.
- `src/Zeltlotse.Ui.Komponenten/` -- `Reiter`, `Card`, `Formular`, `Feld`, `Auswahl`, `Segmente`, `Knopf`, `Status`, `InlineBestaetigung`, `LeerZustand`, `Meldung`; `src/Zeltlotse.Ui/Autosave/AutosaveFeld.razor` (nur Text).
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs:152-156 (Schrittfolge), 779-814 (ProgrammAsync, KontoSicherstellen für Übernahme)}`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (Muster `Schaeden_Verluste`, `Org_Anschaffungswuensche`, `BildAsync`, `AxeAsync`), `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-27`, `doku/src/content/docs/{mitarbeitende,leitung,org-admin}/` (u. a. `leitung/cockpit.md` mit dem Satz, dass Aufgaben-Hinweise später kommen).

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Programm/Aufgaben.cs` + `Gemeinsam/FreizeitKontext.cs` -- Datenformen und Aktionsschlüssel wie oben.
- `tests/Zeltlotse.Programm.Tests/AufgabeTests.cs` (zuerst) + `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/{Aufgabe.cs,AufgabenVorlage.cs}` -- Entitäten mit `Anlegen`, `AusVorlage`, `FeldAendern`, `Uebernehmen`, `Abgeben`, `Erledigen`, `Oeffnen`, `VorlageMerken`; Tests für Trimmen, Altwert-Konflikt, Übergänge, Platzhalter, Validatoren, Mandantenfilter beider Entitäten, Rechteliste `Vorlagen` gleich `OrgRechte.AufgabenVorlagen` und `Mitarbeit` gleich `FreizeitRechte.Mitarbeit` (Muster `tests/Zeltlotse.Inventar.Tests/InventarTests.cs`, Projektreferenzen im Testprojekt ergänzen).
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` + Migration `AufgabenUndVorlagen` -- Tabellen `aufgabe`, `aufgaben_vorlage`, `aufgaben_vorlagen_uebernahme`, Indizes, Sperren.
- `tests/Zeltlotse.Api.Tests/AufgabenTests.cs` (zuerst) -- jede Matrix-Zeile, alle Rechte-Pflichtfälle der 13 Routen, gleichzeitige GETs (genau eine Kopie), gleichzeitiges Übernehmen (genau einer gewinnt); `EndpunktListeTests.cs` um #1–#9 und Org-Zählung 33; Org-Kontext-Test für `aufgaben-vorlagen-verwalten`; Datenauskunft, Lösch-Lauf, PersonGeloescht ergänzen.
- `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/{AufgabenLaden,AufgabenVorlagenUebernehmen,AufgabeAnlegen,AufgabeBearbeiten,AufgabeLoeschen,AufgabeUebergang,AufgabeAlsVorlage,VorlagenLaden,VorlageAnlegen,VorlageBearbeiten,VorlageLoeschen}.cs` + `ProgrammRechte.cs` + `ProgrammModul.cs` + `Datenschutz/ProgrammDatenschutz.cs` + `Organisationen/…/OrgRechte.cs` -- Slices, Routen, Datenschutz, Rechte.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Texte/Anzeige.cs}` -- `AufgabenAsync` (null bei 403/404), `AufgabeAnlegenAsync` (Feldfehler), `AufgabePhaseAendernAsync`, `AufgabeLoeschenAsync`, `AufgabeUebergangAsync` (uebernehmen/abgeben/erledigt/offen), `AufgabeAlsVorlageAsync`, `AufgabePfad`, `VorlagenAsync`, `VorlageAnlegenAsync`, `VorlageLoeschenAsync`, `VorlagePfad`; `Anzeige.AufgabenPhase` → „Vorbereitung“, „Während der Freizeit“, „Nachbereitung“; `Anzeige.AufgabenUnbesetzt(n)` → „1 Aufgabe ist noch nicht übernommen“ / „n Aufgaben sind noch nicht übernommen“.
- `src/Zeltlotse.App/Features/Programm/{Aufgaben.razor,AufgabeSeite.razor,AufgabeZeile.razor}` + `ProgrammpunktSeite.razor` + `Freizeiten/FreizeitStart.razor` + `Organisationen/OrganisationAnsicht.razor` + `Programm/AufgabenVorlagen.razor` + `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs` --
  - Seite `/freizeiten/{id}/aufgaben` (Knopf „Aufgaben“ auf der Startseite nach „Programm“): Dachzeile „Planen“, H1 „Aufgaben“, `Reiter` „Alle“ / „Meine Aufgaben“ (`?meine=true`), je Phase eine Card („Vorbereitung“, „Während der Freizeit“, „Nachbereitung“; leere Cards entfallen). `AufgabeZeile`: Titel als Link zur Detailseite, bei Programmpunkt „Am Programmpunkt: <Titel>“ als Link, Status „Übernommen: Name“ oder Warnung „Offen“, Status „Erledigt“; Knöpfe „Ich übernehme“/„Abgeben“ und „Erledigt“/„Wieder offen“ (ein Tipp, ohne Bestätigung). Formular „Aufgabe anlegen“ mit Titel und Auswahl Phase (vorbelegt: in Vorbereitung → Vorbereitung, läuft → Während, Nachbereitung → Nachbereitung). Leer: `LeerZustand` „Noch keine Aufgaben.“; Reiter Meine leer: „Keine Aufgaben übernommen.“
  - Detailseite `/freizeiten/{id}/aufgaben/{aufgabeId}`: Dachzeile „Planen“, AutosaveFeld Titel, Phase per `Auswahl` mit Speichern per PATCH (409 → Konflikt-Meldung mit Neuladen), Übernehmen/Abgeben, Erledigt/Wieder offen, Knopf „Als Vorlage speichern“ (nur ohne `Vorlage`; danach „Steht in den Aufgaben-Vorlagen der Organisation.“), Löschen per `InlineBestaetigung`.
  - Programmpunkt: Card „Aufgaben“ mit den Aufgaben des Punkts (`AufgabeZeile`) und Formular „Aufgabe hinzufügen“ (nur Titel; Phase Vorbereitung).
  - Startseite (alle Rollen, nicht in Gelaufen): Card „Meine Aufgaben“ mit eigenen offenen Aufgaben (Titel-Link) oder „Keine Aufgaben übernommen.“ und Link „Alle Aufgaben“; in Phase läuft darunter der Kurzlink „Schaden melden“ (`freizeiten/{id}/schaeden`). Cockpit „Warnhinweise“: neue Zeile `Anzeige.AufgabenUnbesetzt(n)` als Link auf die Aufgaben-Seite, gezählt werden offene, unbesetzte Aufgaben, deren Phase höchstens der aktuellen entspricht (Vorbereitung → nur Vorbereitung, läuft → Vorbereitung und Während, Nachbereitung → alle); nicht in Gelaufen; `AnzahlZeilen` und `AlleZeilenGeladen` einbeziehen. Scheitert der Abruf, entfallen Card und Zeile.
  - Org: Card „Aufgaben-Vorlagen“ bei `aufgaben-vorlagen-verwalten` mit Knopf „Aufgaben-Vorlagen öffnen“; Seite `/organisationen/{OrgId}/aufgaben-vorlagen`: Dachzeile „Organisation“, H1 „Aufgaben-Vorlagen“, Formular „Vorlage hinzufügen“ (Titel, Phase), je Phase Card mit Titel als AutosaveFeld und Löschen per InlineBestaetigung; leer „Noch keine Aufgaben-Vorlagen. Neue Freizeiten übernehmen sie beim ersten Öffnen der Aufgaben.“
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- Schritt `AufgabenAsync` nach `SchaedenAsync`, idempotent nach Titel: Vorlagen (als Org-Commands) „Zeltplatz-Vertrag prüfen“ (Vorbereitung), „Erste-Hilfe-Kasten auffüllen“ (Vorbereitung), „Zeltplatz besenrein übergeben“ (Nachbereitung); dann Übernahme ins Zeltlager; Aufgaben „Andacht am Lagerfeuer vorbereiten“ (Während, am Punkt „Lagerfeuer“, übernommen von Lea), „Fotos sammeln“ (Nachbereitung, offen); Lea übernimmt „Erste-Hilfe-Kasten auffüllen“; Jonas übernimmt „Zeltplatz-Vertrag prüfen“ und hakt ab.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `Aufgaben` (Lea, Freizeit → „Aufgaben“), `Meine_Aufgaben` (Lea, Startseite mit Card) und `Org_Aufgaben_Vorlagen` (Andrea), je Handy und Laptop mit `AxeAsync`, `BildAsync` → `aufgaben-*`, `meine-aufgaben-*`, `aufgaben-vorlagen-*`; durch neuen Knopf, Card oder Warnhinweis geänderte Bilder neu erzeugen, Rauschen per Pixelvergleich zurücksetzen.
- `doku/src/content/docs/mitarbeitende/aufgaben.md` (neu, „Aufgaben übernehmen und abhaken“: Phasen, „Ich übernehme“, „Meine Aufgaben“, Aufgaben am Programmpunkt, „Als Vorlage speichern“, Vorlagen der Organisation sind schon drin) + `org-admin/aufgaben-vorlagen.md` (neu) + Links in `mitarbeitende/index.md`, `mitarbeitende/programm.md`, `leitung/index.md`, `org-admin/index.md`; Satz zu Aufgaben in `leitung/cockpit.md` durch den neuen Warnhinweis ersetzen; `DokuRegeln.cs` PflichtSeiten.

**Acceptance Criteria:**
- Given die Organisation hat die Vorlage „Zeltplatz-Vertrag prüfen“, when ein Teammitglied einer neuen Freizeit „Aufgaben“ öffnet, then steht die Aufgabe dort unter „Vorbereitung“, ohne dass jemand sie anlegt.
- Given Lea tippt bei einer Aufgabe „Ich übernehme“, when sie die Startseite oder den Reiter „Meine Aufgaben“ öffnet, then steht die Aufgabe dort, ohne Bestätigungsschritt und ohne Zutun der Leitung.
- Given eine Aufgabe ohne Vorlage, when jemand „Als Vorlage speichern“ tippt, then erscheint sie auf der Org-Seite „Aufgaben-Vorlagen“ und der Knopf verschwindet.
- Given unbesetzte Vorbereitungs-Aufgaben in einer Freizeit in Vorbereitung, when die Leitung die Startseite öffnet, then nennt „Warnhinweise“ ihre Zahl mit Link; das Team sieht die Zeile nicht.
- Given ein Org-Überblick-Konto, when es die Organisation öffnet, then gibt es keine Card „Aufgaben-Vorlagen“.
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und sechs neue Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 32 findings — high 0, medium 0, low 25, false 7, maybe-false 0
- findings:
  - `[low]` `[patch]` (Blind) Card „Meine Aufgaben“ sagt „Keine Aufgaben übernommen.“, auch wenn alle eigenen Aufgaben erledigt sind — eigener Text „Alle übernommenen Aufgaben sind erledigt.“ plus bUnit-Test.
  - `[low]` `[reject]` (Blind) Kopieren der Vorlagen als Nebenwirkung des GET, spätere Vorlagen erreichen geöffnete Freizeiten nicht — so in Intent und Design Notes festgelegt (Muster Mahlzeiten, kein `FreizeitAngelegt`); ein Knopf „Vorlagen übernehmen“ wäre neue Oberfläche.
  - `[low]` `[patch]` (Blind) Doku `mitarbeitende/aufgaben.md` verschweigt, dass das Öffnen der Startseite zählt — Satz ergänzt.
  - `[low]` `[reject]` (Blind) „Als Vorlage speichern“ übernimmt spätere Änderungen nicht — Intent: „gibt es die verknüpfte Vorlage schon, nichts Neues“; der Text stimmt (eine Vorlage existiert), der Org-Admin pflegt sie.
  - `[low]` `[reject]` (Blind) Keine Dublettenprüfung nach Titel, nach Löschen erscheint der Knopf wieder — `VorlageId` verhindert Doppeltipp; ein Titelabgleich wäre zusätzliche Regel, der Org-Admin räumt auf (Design Notes).
  - `[low]` `[reject]` (Blind) Phase einer Vorlage in der Oberfläche nicht änderbar — Spec sieht nur den Titel als AutosaveFeld vor; Löschen und neu anlegen genügt, selten.
  - `[low]` `[reject]` (Blind) Aufgabe am Programmpunkt immer Phase Vorbereitung — so festgelegt; Phase auf der Detailseite änderbar, der Cockpit-Hinweis zählt eine wirklich offene Aufgabe.
  - `[low]` `[reject]` (Blind) Löschen des Programmpunkts löscht seine Aufgaben ohne Hinweis — so festgelegt (wie Material, Design Notes); Zählung in der Bestätigung wäre zusätzliche Logik.
  - `[low]` `[reject]` (Blind) Jeder darf eine übernommene Aufgabe löschen — Vertrauen statt Rechte-Matrix (Leitprinzip 8), wie Programmpunkte in 3-1.
  - `[low]` `[reject]` (Blind) Datenauskunft ohne Freizeit — carried-Abwägung aus 3-1 (gleiche Form wie die anderen Module).
  - `[low]` `[reject]` (Blind) Aufgabe nicht an einen anderen Programmpunkt verschiebbar — nicht verlangt; löschen und neu anlegen.
  - `[low]` `[reject]` (Blind) Kein Einzel-GET, Detailseite lädt die Liste; Location auf die Liste — carried-Abwägung aus 3-1 (kleine Datenmenge, neue Oberfläche ohne Nutzen); Location zeigt auf eine Adresse mit GET.
  - `[false]` `[reject]` (Blind) `Version` im PATCH ungenutzt — Vertrag aus AD-19, Konfliktregel über den Altwert, in allen Modulen gleich (carried aus 3-1).
  - `[low]` `[reject]` (Edge) Anlegen gelingt, Neuladen scheitert (offline) → Fehler, erneutes Absenden doppelt — seltener Offline-Sonderfall; Abhilfe wäre zusätzlicher Zweig.
  - `[low]` `[reject]` (Edge) Demo: gelöschte Vorlagen-Aufgabe lässt `Single` werfen — Demo läuft frisch (carried aus 3-3/3-4).
  - `[low]` `[reject]` (Edge) Demo: doppelte Programmpunkt-Titel — wie oben, feste Demo-Daten.
  - `[low]` `[reject]` (Edge) Demo: „Lagerfeuer“ gelöscht → KeyNotFound — wie oben.
  - `[low]` `[reject]` (Edge) Erstes Laden erst in Phase läuft kopiert Vorbereitungs-Vorlagen, Cockpit zählt sie — Intent: Kopie in jeder Phase außer Gelaufen; die Aufgaben sind tatsächlich offen.
  - `[low]` `[reject]` (Edge) Detailseite: 404 nach Löschen durch andere zeigt allgemeinen Fehler — carried-Abwägung aus 3-1 bis 3-4.
  - `[low]` `[reject]` (Edge) Neu laden nach Konflikt bei gelöschter Aufgabe hält alte Werte — carried aus 3-1.
  - `[low]` `[reject]` (Edge) Vorlage schon von anderem Admin gelöscht → Fehler, Zeile bleibt — carried-Abwägung aus 3-3/3-4.
  - `[low]` `[reject]` (Edge) Doppelte Vorlagen bei gleichem Titel — wie die Blind-Zeile zur Dublettenprüfung.
  - `[low]` `[patch]` (Verification gap) Kein Test, dass bei gescheitertem Aufgaben-Abruf die Leitung nicht „Keine offenen Punkte.“ sieht — bUnit-Test ergänzt.
  - `[low]` `[patch]` (Verification gap) Team-Test „kein Warnhinweis“ kann nicht fehlschlagen (keine zählbare Aufgabe in der Fixture) — Fixture um eine offene, unbesetzte Vorbereitungs-Aufgabe ergänzt.
  - `[low]` `[reject]` (Intent) Vorlagen beim ersten Laden statt beim Anlegen — beobachtbar gleich (CAP-23 success), Design Notes und Doku nennen die Regel.
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — kein Token-Endpunkt in dieser Story.
  - `[false]` `[reject]` (Intent) 403 nur auf Org-Routen — jede Freizeit-Rolle darf alles; Org-Admin ohne Freizeit-Rolle `404` (wie 3-1 bis 3-4).
  - `[false]` `[reject]` (Intent) Umfang über stories.yaml hinaus — FR-27, NS-5 und EXPERIENCE.md verlangen Warnhinweis, Startseite und Aufgaben am Programmpunkt; Warnhinweis per bUnit getestet.
  - `[low]` `[reject]` (Intent) Phase der Vorlage nur per API änderbar — wie die Blind-Zeile oben.
  - `[false]` `[reject]` (Intent) Doku-Build und Test-zuerst im Diff nicht belegt — `npm run build` lief fehlerfrei (51 Seiten).
  - `[false]` `[reject]` (Intent) Geänderte Bestands-Screenshots evtl. Rauschen — per Pixelvergleich geprüft: behalten nur Bilder mit neuem Knopf oder neuer Card, Rauschen zurückgesetzt.
  - `[false]` `[reject]` (Intent) Spec untracked — wird mit der Story committet.

## Design Notes

- **Vorlagen beim ersten Laden statt beim Anlegen:** Ein Ereignis `FreizeitAngelegt` gibt es nicht (Spine); 3-1 löst dasselbe Problem für Mahlzeiten beim ersten Laden. Die Übernahme-Zeile je Freizeit sorgt dafür, dass genau einmal kopiert wird und gelöschte Aufgaben nicht wiederkommen. Beobachtbar ist das Ergebnis gleich: Wer die Aufgaben einer neuen Freizeit öffnet, findet die Vorlagen.
- **Vorlagen pflegt nur der Org-Admin, „Als Vorlage speichern“ darf das Team:** rollen-und-rechte.md gibt Aufgaben-Vorlagen dem Org-Admin, nennt „in eine Vorlage übernehmen“ aber nicht unter den Leitungs-Vorbehalten; CAP-23 verlangt es aus der Freizeit heraus. Vertrauen statt Rechte-Matrix: Leitung und Team dürfen es, der Org-Admin räumt auf. Org-Überblick sieht Vorlagen nicht (nicht in seiner Liste) → `403`.
- **`VorlageId` statt Titelvergleich:** verhindert doppelte Vorlagen bei Doppeltipp und blendet den Knopf bei Aufgaben aus, die schon aus einer Vorlage stammen. Wird die Vorlage gelöscht, gilt die Verknüpfung als leer.
- **Erledigt ohne Vermerk:** UX nennt Abhaken als Tipp; einen Vermerk verlangt das PRD nur bei Beiträgen (FR-60).
- **Warnhinweis nach Phase:** Nachbereitungs-Aufgaben sind in der Vorbereitung naturgemäß unbesetzt; sie zu zählen, hielte das Cockpit dauerhaft rot. Die Regel ist ein Vergleich der Reihenfolge.
- **Aufgaben am Programmpunkt in dieser Story:** NS-5 und EXPERIENCE.md (Programmpunkt: „Aufgaben“) verlangen es, und 3-6 übernimmt Programmpunkte „samt Aufgaben“. Mit dem Punkt gelöscht wie Material.
- **403 nur auf Org-Routen** wie in 3-3/3-4: Jede Freizeit-Rolle darf alles an Aufgaben.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Aufgaben/Programm/EndpunktListe/Demo/Datenauskunft/Loeschlauf/PersonGeloescht/Kontext), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. E2E) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Modul Programm mit `Aufgabe` (Titel, Phase, optional Programmpunkt, optional übernehmende Person, erledigt, optional Vorlage) und `AufgabenVorlage` (Organisation; Titel, Phase).
- **Freizeit-Endpunkte (Leitung und Team, neun Routen):** Aufgaben lesen, anlegen, Autosave-PATCH (Titel, Phase), löschen, übernehmen/abgeben (nur für sich selbst), erledigt/offen, als Vorlage speichern. In Phase Gelaufen nur lesbar.
- **Org-Endpunkte (nur Org-Admin, vier Routen):** Vorlagen lesen, anlegen, Autosave-PATCH, löschen; Org-Überblick bekommt `403`. Neuer Aktionsschlüssel `aufgaben-vorlagen-verwalten`.
- **Vorlagen fließen ein:** Beim ersten Laden der Aufgaben einer Freizeit kopiert Programm einmalig alle Vorlagen (Übernahme-Zeile mit `ON CONFLICT DO NOTHING`, nicht in Gelaufen).
- **Datenschutz (AD-16):** Platzhalter bei gelöschter Person, Zeile in der Datenauskunft, Lösch-Lauf für Aufgaben und Übernahme-Zeilen.
- **Web-App:**
  - Seite „Aufgaben“ mit Reiter „Alle“/„Meine Aufgaben“, Detailseite, Card „Aufgaben“ am Programmpunkt.
  - Startseite: Card „Meine Aufgaben“ (in Phase läuft mit „Schaden melden“), Knopf „Aufgaben“, Cockpit-Warnhinweis zu unbesetzten Aufgaben nach Phase.
  - Org-Seite „Aufgaben-Vorlagen“ mit Card auf der Organisationsseite.
- **Demo-Daten:** drei Vorlagen, zwei weitere Aufgaben, Übernahmen von Lea und Jonas, eine erledigt.
- **Doku:** zwei neue Seiten, sechs neue Screenshots.

**Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Programm/Aufgaben.cs`, `Gemeinsam/FreizeitKontext.cs` -- Datenformen, Aktionsschlüssel.
- `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/*` -- Entitäten, 13 Endpunkte, Vorlagen-Übernahme.
- `src/Modules/Programm/Zeltlotse.Programm/{Daten/ProgrammDbContext.cs,ProgrammModul.cs,Programme/ProgrammRechte.cs,Datenschutz/ProgrammDatenschutz.cs}`, Migration `AufgabenUndVorlagen` -- Tabellen, Routen, Rechte, Datenschutz.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` -- Rechteliste und `Erlaubt`.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Texte/Anzeige.cs}` -- Client und Texte.
- `src/Zeltlotse.App/Features/Programm/{Aufgaben,AufgabeSeite,AufgabeZeile,AufgabenVorlagen}.razor`, `ProgrammpunktSeite.razor`, `ProgrammTexte.cs`, `Freizeiten/FreizeitStart.razor`, `Organisationen/OrganisationAnsicht.razor` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoDaten,DemoKatalog}.cs` -- Demo-Schritt `AufgabenAsync`.
- Tests:
  - neu: `tests/Zeltlotse.Programm.Tests/AufgabeTests.cs`, `tests/Zeltlotse.Api.Tests/AufgabenTests.cs`, `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs`;
  - ergänzt: Endpunktliste (Org 33), Rechte, Demo, Doku-Regeln und Doku-Tests, Cockpit- und Startseiten-Tests, E2E-Screenshots.
- `doku/src/content/docs/{mitarbeitende/aufgaben.md,org-admin/aufgaben-vorlagen.md}` (neu); Links und Sätze in `mitarbeitende/{index,programm}.md`, `leitung/{index,cockpit}.md`, `org-admin/index.md`, `daten.md`.
- Screenshots:
  - neu: `aufgaben-*`, `meine-aufgaben-*`, `aufgaben-vorlagen-*`;
  - neu erzeugt (neuer Knopf bzw. neue Card): `freizeitpass-erinnern-laptop`, `schutzkonzept-hinweis-*`, `loeschfristen-*`, `nachweis-richtlinie-*`.

**Review:** 32 Befunde (high 0, medium 0, low 25, false 7).
- **Gepatcht (4 low):**
  - Leertext der Card „Meine Aufgaben“, wenn alle eigenen Aufgaben erledigt sind;
  - Doku-Satz: auch das Öffnen der Startseite übernimmt die Vorlagen;
  - Test: gescheiterter Aufgaben-Abruf zeigt der Leitung kein „Keine offenen Punkte.“;
  - Team-Test mit zählbarer Aufgabe, damit „kein Warnhinweis“ greift.
- **Zurückgestellt:** keine.
- **Abgelehnt:** 28, Gründe im Review Triage Log:
  - Vorlagen beim ersten Laden laut Intent;
  - keine Dublettenprüfung und kein Einzel-GET (zusätzliche Regeln bzw. Oberfläche);
  - 403 und Token-Fälle laut Matrix;
  - carried-Abwägungen aus 3-1 bis 3-4 (404 bei Gleichzeitigkeit, Demo läuft frisch).

**Folge-Review empfohlen:** nein (kein high oder medium gepatcht).

**Verifikation:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler.
- **Komplettlauf vor dem Review**, wegen des Zeitlimits je Projekt:
  - E2E 149/149 mit Screenshots;
  - Api.Tests 775/775 in drei Teilen;
  - ArchitectureTests 133, Ablaeufe 10, Programm 80, Ui 629, Inventar 34, Freizeiten 81, Konten 26, Organisationen 58, Personen 153, SharedKernel 100, Sicherheit 36 – alle grün.
- **Nach den Patches** (nur Oberfläche, Tests, Doku geändert):
  - Ui.Tests 631/631, ArchitectureTests 133/133;
  - E2E-Theorien `Aufgaben`, `Meine_Aufgaben`, `Org_Aufgaben_Vorlagen` 6/6.
- `npm run build` in `doku/` -- fehlerfrei, 51 Seiten.
- Matrix-Audit: Jede Zeile der I/O-Matrix ist durch gelaufene, grüne Tests in `AufgabenTests` bzw. `AufgabeTests` abgedeckt.
- Screenshots per Pixelvergleich geprüft: Rauschen (Token, Datum, Testreihenfolge) zurückgesetzt, echte Änderungen behalten.

**Restrisiken:**
- Vorlagen fließen nur einmal ein, beim ersten Öffnen der Aufgaben oder der Startseite einer Freizeit. Später angelegte Vorlagen erreichen diese Freizeit nicht.
- Eine unbekannte Phase als Text im POST ergibt einen allgemeinen `400` statt eines Feldfehlers; als Zahl oder im PATCH gibt es den Feldfehler.
- Nach dem Lösch-Lauf würde ein erneutes Öffnen die Vorlagen wieder kopieren, wenn die Freizeit nicht abgeschlossen ist.
- Der Cockpit-Warnhinweis zu unbesetzten Aufgaben ist nur per bUnit belegt. Die Demo hat keine unbesetzte Vorbereitungs-Aufgabe, daher zeigt kein Screenshot diesen Hinweis.
- Doppelte Vorlagen mit gleichem Titel sind möglich, wenn das Team Aufgaben mit gleichem Titel als Vorlage speichert.
