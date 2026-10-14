---
title: 'Story 3-4: Schäden, Verluste und Anschaffungswünsche'
type: 'feature'
created: '2026-10-14'
status: 'done'
baseline_revision: '2ad116da879027e9be24c5697399c71e8c2fe14a'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-3-inventar-und-reservierung.md'
warnings: [oversized]
deferred:
  - summary: >-
      Die Anschaffungswünsche-Seite der Organisation zeigt in Demo, E2E-Test und Doku-Screenshot nur den Leer-Zustand.
    evidence: |-
      Keine Demo-Freizeit kann die Phase Gelaufen erreichen, weil „Freizeit abschließen“ erst mit Story 3-8 kommt. Gefüllte Ansicht, „Erledigt“ und die Sicht des Org-Überblicks sind nur per HTTP-Test (Phase per SQL) und bUnit belegt. Story 3-8 sollte eine abgeschlossene Demo-Freizeit mit Einträgen anlegen und die Bilder anschaffungswuensche-* neu erzeugen; dazu die Sätze „bleibt leer, bis …“ in org-admin/anschaffungswuensche.md und schaeden-verluste.md anpassen.
    location: >-
      tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs (Org_Anschaffungswuensche)
    severity: low
---

<intent-contract>

## Intent

**Problem:** Was auf der Freizeit kaputtgeht, verloren geht oder aus dem Erste-Hilfe-Kasten verbraucht wird, kann niemand festhalten, und die Organisation erfährt nach der Freizeit nicht, was angeschafft werden muss (CAP-22, CAP-17, FR-32, FR-38, NS-6, NS-12, NS-13).

**Approach:** Inventar bekommt die Entität `SchadenVerlust` (Freizeit, Art Schaden/Verlust/Entnahme, Bezeichnung, erfasst am, optional Kennung des Vorfall-Eintrags). Leitung und Team pflegen die Schadens- und Verlustliste der Freizeit in allen Phasen. Ein Vorfall-Eintrag der Art Entnahme löst in Sicherheit das Ereignis `ErsteHilfeEntnommen` aus; der Empfänger in Inventar legt daraus einen Eintrag „Erste-Hilfe-Kasten“ an (Spine-Zeile 563 vorhanden). Anschaffungswünsche sind abgeleitet: die Einträge aller Freizeiten in Phase Gelaufen. Org-Admin und Org-Überblick sehen sie in der Organisation, der Org-Admin entfernt erledigte.

## Boundaries & Constraints

**Always:**
- Datenformen in `src/Zeltlotse.Api.Contracts/Inventar/Schaeden.cs` (additiv): `enum SchadenArt { Schaden = 1, Verlust = 2, Entnahme = 3 }` (JSON wie `VorfallArt`); `SchadenEintrag(Guid Id, uint Version, SchadenArt Art, string Bezeichnung, DateTimeOffset ErfasstAm, Guid? VorfallEintragId)`; `SchadenNeu(SchadenArt? Art, string? Bezeichnung)`; `enum SchadenFeld { Bezeichnung }`; `SchadenFelder` (JSON-Namen `art`, `bezeichnung`, Grenze 120, `JsonName`); `AnschaffungswunschFreizeit(Guid FreizeitId, string Name, IReadOnlyList<SchadenEintrag> Eintraege)`.
- Endpunkte (Modul Inventar):

  | # | Route | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId}/inventar/schaeden` | `InventarRechte.Mitarbeit` | `200 IReadOnlyList<SchadenEintrag>`, `ErfasstAm` absteigend, dann `Id` absteigend |
  | 2 | `POST …/inventar/schaeden` (`SchadenNeu`) | Mitarbeit | `201` mit `SchadenEintrag`; `ErfasstAm` aus `TimeProvider` |
  | 3 | `PATCH …/inventar/schaeden/{eintragId}` | Mitarbeit | AD-19 wie `ArtikelBearbeiten`: `200 {version, bezeichnung}`, `409 konflikt` nur bei abweichendem Altwert |
  | 4 | `DELETE …/inventar/schaeden/{eintragId}` | Mitarbeit | `204`; unbekannt oder andere Freizeit → `404` |
  | 5 | `GET /api/organisationen/{orgId}/inventar/anschaffungswuensche` | `InventarRechte.OrgLesen` | `200 IReadOnlyList<AnschaffungswunschFreizeit>`: nur Freizeiten in Phase Gelaufen (`IFreizeitZeitpunkte.ZeitpunkteAsync` über die Freizeit-Kennungen der eigenen Einträge), Namen über `IFreizeitNamenLesen`; Gruppen nach neuestem Eintrag absteigend, Einträge wie in #1 |
  | 6 | `DELETE /api/organisationen/{orgId}/inventar/anschaffungswuensche/{eintragId}` | `InventarRechte.OrgVerwalten` | `204` („erledigt“); Eintrag unbekannt oder seine Freizeit nicht Gelaufen → `404` |
- Validierung: `art` Pflicht und bekannt, `bezeichnung` Pflicht, getrimmt, 1–120 Zeichen → sonst `400` am Feld. Alle drei Arten dürfen von Hand eingetragen werden.
- Keine Phasenprüfung an #2–#4: Eintragen geht während der ganzen Freizeit, und nach dem Abschluss sind die Einträge Anschaffungswünsche, die AD-17 ausdrücklich beschreibbar lässt.
- `SchadenVerlust`: Tabelle `inventar.schaden_verlust`, `IOrganisationsEigen`, `uint Version` (xmin), Index auf OrganisationId und FreizeitId, eindeutiger Index auf `VorfallEintragId` (wo gesetzt), Zeilensperre `SchadenSperrenAsync` (`FOR UPDATE`) für PATCH und DELETE. Keine `PersonId`, daher keine Pflichten aus AD-16. Migration `SchadenUndVerlust`.
- Ereignis `ErsteHilfeEntnommen(Guid VorfallEintragId, Guid FreizeitId, DateTimeOffset ErfasstAm)` in `Zeltlotse.Sicherheit.Contracts/Events/`; kein Text, keine Person (AD-4).
- Sicherheit: `VorfallEintrag` erbt von `Aggregat` (Spalte `Folgenummer`, Migration `VorfallEreignis`). Der Handler legt das Ereignis nur bei Art Entnahme und nur, wenn der `INSERT … ON CONFLICT DO NOTHING` eine Zeile anlegt (`angelegt == 1`), ab und hängt den Eintrag danach unverändert an den `DbContext` (`Attach`), damit die Outbox-Stufe ihn einsammelt. `Folgenummer` steht im INSERT. Doppelt gesendete Einträge (AD-18) erzeugen kein zweites Ereignis.
- Empfänger in Inventar `ErsteHilfeEntnahmeEintragen` (`EmpfaengtEreignis<ErsteHilfeEntnommen, …>`): legt `SchadenVerlust` mit Art Entnahme, Bezeichnung „Erste-Hilfe-Kasten“, `VorfallEintragId`, `FreizeitId` und `ErfasstAm` aus dem Ereignis an; gibt es die `VorfallEintragId` schon, nichts tun. Inventar nutzt aus `Sicherheit.Contracts` nur den Namensraum `.Events`.
- `InventarLoeschlauf` löscht zusätzlich die Einträge von `FreizeitenMitAbgelaufenenDaten`.
- Testgetrieben (AD-30). Pflichtfälle: Freizeit-Routen Leitung und Team `2xx`, Org-Admin ohne Freizeit-Rolle `404`, fremde Organisation `404`, Eintrag einer anderen Freizeit `404`, ohne Sitzung `401`. Org-Routen: Org-Admin `2xx`, Org-Überblick GET `200` und DELETE `403`, Org-Admin einer fremden Organisation `404`, Person ohne Org-Rolle `404`, ohne Sitzung `401`. „Falsche Rolle 403“ auf Freizeit-Routen entfällt (jede Freizeit-Rolle darf), Token-Fälle entfallen (kein Token-Endpunkt).
- Oberfläche nur aus `Zeltlotse.Ui.Komponenten`, Rechte nur über `Erlaubt` bzw. Erfolg der Abrufe; vorhandene Aktionsschlüssel `inventar-sehen`/`inventar-verwalten` steuern die Org-Seite. Texte Deutsch ohne Anrede.

**Never:** Text des Vorfall-Eintrags oder Personen in Inventar speichern; Ereignis-Empfänger für `FreizeitAbgeschlossen` in Inventar oder sonstige Spine-Änderung; Abschluss-Endpunkt (Story 3-8); Seiten- oder untere Navigation, Zähler, „Meine Aufgaben“ (Story 3-5); Verknüpfung mit Inventar-Artikeln, Mengen, Kosten, Status „angeschafft“; neue Aktionsschlüssel oder Problemcodes; Pop-ups; Commit auf `main`, Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Eintragen | Tobias POST `{art: verlust, bezeichnung: " Taschenlampe "}` in X | `201`, Bezeichnung „Taschenlampe“; GET liefert ihn oben | – |
| Formfehler | `bezeichnung` leer / 121 Zeichen; `art` fehlt / 9 | – | je `400` am Feld |
| Ändern | PATCH `bezeichnung` mit passendem Altwert | `200 {version, bezeichnung}` | abweichender Altwert → `409 konflikt`; unbekannt → `404` |
| Entfernen | DELETE eigener Eintrag | `204` | Eintrag aus Z über X → `404` |
| Abgeschlossen | X `Gelaufen` (SQL) | GET `200`, POST `201`, PATCH `200`, DELETE `204` | – |
| Entnahme | Tobias POST Vorfall `{Entnahme, "2 Pflaster"}` zu Emma, Outbox zugestellt | ein Eintrag Art Entnahme, Bezeichnung „Erste-Hilfe-Kasten“, `VorfallEintragId` = Vorfall-Id; kein „Pflaster“ in `inventar.schaden_verlust` | – |
| Kein Ereignis | Vorfall Art Geschehen; derselbe Entnahme-POST zweimal | kein Eintrag bzw. genau ein Ereignis und ein Eintrag | erneute Zustellung legt nichts doppelt an |
| Wünsche | Einträge in X (`Gelaufen`) und Y (läuft) | GET #5 nur Gruppe X mit Name „Zeltlager Kapf 2027“ | ohne Gelaufen → `[]` |
| Erledigt | Andrea DELETE #6 auf Eintrag in X / in Y | `204`, weg auch in GET #1 / `404` | – |
| Rechte | siehe Pflichtfälle oben | wie dort | – |
| Frist | Löschlauf mit X in `FreizeitenMitAbgelaufenenDaten` | Einträge von X weg, andere bleiben | – |
| Mandant | Einträge in A und B | Lese-Kontext je Mandant zeigt nur die eigenen | – |

</intent-contract>

## Code Map

- `src/Modules/Inventar/Zeltlotse.Inventar/InventarModul.cs:21-46` -- Registrierung (`AddModul`, `ILoeschlauf`), Org-Gruppe `/organisationen/{orgId:guid}/inventar` (37-41), Freizeit-Gruppe `/freizeiten/{freizeitId:guid}/inventar` (43-46); hier neue Routen und `services.EmpfaengtEreignis<ErsteHilfeEntnommen, ErsteHilfeEntnahmeEintragen>()`.
- `src/Modules/Inventar/Zeltlotse.Inventar/{InventarRechte.cs,InventarProbleme.cs,FeldPatch.cs}` -- Rechtelisten `OrgLesen`/`OrgVerwalten`/`Mitarbeit`, 409-Helfer, PATCH-Leser `FeldPatch.Lesen`.
- `src/Modules/Inventar/Zeltlotse.Inventar/Artikel/{InventarArtikel.cs,ArtikelAnlegen.cs,ArtikelBearbeiten.cs,ArtikelLoeschen.cs}` -- Vorlagen für Entität (`FeldAendern` mit Altwert), Command/Validator/Handler/Endpunkt, Ergebnis-Records; `InventarTexte` für Fehlertexte.
- `src/Modules/Inventar/Zeltlotse.Inventar/Reservierungen/Reservieren.cs:57-70` -- Nutzung `IFreizeitZeitpunkte`/`IMandantenKontext` im Handler.
- `src/Modules/Inventar/Zeltlotse.Inventar/Daten/InventarDbContext.cs` -- DbSets, `ArtikelSperrenAsync` (27-31, Muster für `SchadenSperrenAsync`), `InventarModell.Konfigurieren` (62-86), Migrationen in `Migrationen/` (zuletzt `20261013215828_InventarUndReservierung`).
- `src/Modules/Inventar/Zeltlotse.Inventar/Datenschutz/InventarLoeschlauf.cs` -- `ReservierungenLoeschen` um die neuen Einträge erweitern.
- `src/Modules/Inventar/Zeltlotse.Inventar/Zeltlotse.Inventar.csproj:9-14` -- ProjectReference auf `Sicherheit.Contracts` ergänzen (erlaubt: `ProjektDateiRegeln.cs:16-31`; Nutzung nur `.Events`, sonst schlägt `AbhaengigkeitsRegeln.cs:122-136` an; keinen Pfeil in `Loesung.cs` ergänzen).
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/ReservierungUebernehmen.cs` + `ProgrammModul.cs:30` -- Empfänger-Muster (`IEreignisEmpfaenger<TSelf,TEreignis>`, `Aus`, Handler ohne Benutzer).
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Vorfaelle/{VorfallEintrag.cs:18-67,VorfaelleBearbeiten.cs:80-136}` -- Entität (noch kein `Aggregat`, eigenes `Id`), Handler mit Roh-SQL-INSERT (122-129) und `angelegt == 1` (130).
- `src/Zeltlotse.SharedKernel/{Aggregat.cs,Daten/ModulDbContext.cs:37-60}` -- `EreignisAblegen`, `Id { protected init }`; Outbox sammelt aus `ChangeTracker.Entries<Aggregat>()`.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit.Contracts/Events/EventsNamensraum.cs` -- Ort für `ErsteHilfeEntnommen`; Vorlage `Inventar.Contracts/Events/ReservierungGeaendert.cs`. `TypRegelnTests` erlaubt `DateTimeOffset`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Zeitpunkte/IFreizeitZeitpunkte.cs:10-25,Namen/IFreizeitNamenLesen.cs:35}` -- Phase und Namen je Menge von Freizeit-Kennungen, unter Mandantenfilter.
- `tests/Zeltlotse.Api.Tests/{InventarTests.cs,FreizeitpassSzenario.cs,EndpunktListeTests.cs,DemoDatenTests.cs,VorfaelleTests.cs}` -- Szenario (`_s.Andrea/Lea/Miriam/Tobias/Gerd/Anonym`, Orgs `A`/`B`, Freizeiten `X`/`Z`, Org-Überblick per `OrgHelfer.EinladenAsync`/`AnnehmenAlsAsync`, Gelaufen per SQL Zeile 436, `_s.ZustellenAsync()`); Freizeit-Routenliste (Inventar 264-267) und `Assert.Equal(27, mitOrg.Count)` (288) → 29.
- `tests/Zeltlotse.Inventar.Tests/` -- Entitäts-, Validator- und Mandantenfilter-Tests; `tests/Zeltlotse.Ablaeufe.Tests/{ReservierungGeaendertTests.cs,AnmeldeAblauf.cs}` -- Durchlauf-Muster (`ZustellenMitWiederholungAsync`, Wiederzustellung per SQL, Inbox-Zählung).
- `src/Zeltlotse.Ui/{Api/InventarApiClient.cs,Api/SicherheitApiClient.cs:105,Texte/Anzeige.cs}` -- Client-Muster; `VorfaelleLadenAsync` für die Anzeige des Entnahme-Texts.
- `src/Zeltlotse.App/Features/{Inventar/FreizeitInventar.razor,Inventar/OrgInventar.razor,Freizeiten/FreizeitStart.razor:148-166,Organisationen/OrganisationAnsicht.razor:59-74}` -- Seitenmuster, Knopf-Liste der Startseite, Org-Cards.
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs:110-140,895-940,DemoKatalog.cs:118-130,161}` -- Schrittfolge, `InventarAsync`, Lea-Entnahme `…f002`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:1679-1740,1924` -- `Org_Inventar`/`Inventar_Reservieren`, `BildAsync`; `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-26`; `doku/src/content/docs/{mitarbeitende,org-admin,leitung}/`, `mitarbeitende/vorfall-protokoll.md:49-51` (Satz „kommt später“ ersetzen).

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Inventar/Schaeden.cs` -- Datenformen wie oben.
- `tests/Zeltlotse.Inventar.Tests/SchadenVerlustTests.cs` (zuerst) + `src/Modules/Inventar/Zeltlotse.Inventar/Schaeden/SchadenVerlust.cs` -- Entität mit `Eintragen`, `AusEntnahme`, `FeldAendern`; Tests für Trimmen, Altwert-Konflikt, Validatoren, Mandantenfilter.
- `src/Modules/Inventar/Zeltlotse.Inventar/Daten/InventarDbContext.cs` + Migration `SchadenUndVerlust` -- Tabelle, Indizes, Sperre.
- `tests/Zeltlotse.Api.Tests/SchaedenTests.cs` (zuerst) -- jede Matrix-Zeile außer Entnahme und Frist, alle Rechte-Pflichtfälle der sechs Routen; `EndpunktListeTests.cs` um vier Freizeit-Routen, Org-Zählung 29.
- `src/Modules/Inventar/Zeltlotse.Inventar/Schaeden/{SchaedenLaden,SchadenEintragen,SchadenBearbeiten,SchadenEntfernen,AnschaffungswuenscheLaden,AnschaffungswunschErledigt,ErsteHilfeEntnahmeEintragen}.cs` + `InventarModul.cs` + `Datenschutz/InventarLoeschlauf.cs` + csproj -- Slices, Routen, Empfänger, Löschlauf.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit.Contracts/Events/ErsteHilfeEntnommen.cs` + `Sicherheit/Vorfaelle/{VorfallEintrag.cs,VorfaelleBearbeiten.cs}` + Migration `VorfallEreignis` -- Ereignis nur bei frischer Entnahme.
- `tests/Zeltlotse.Ablaeufe.Tests/ErsteHilfeEntnommenTests.cs` (zuerst) -- Durchlauf: Entnahme-POST, Zustellung → Eintrag; zweiter gleicher POST → `sicherheit.outbox` bleibt bei 1; Wiederzustellung → Inbox einmal, ein Eintrag; Geschehen → kein Ereignis. Dazu Inventar-Modultest für den Löschlauf.
- `src/Zeltlotse.Ui/{Api/InventarApiClient.cs,Texte/Anzeige.cs}` -- `SchaedenAsync` (null bei 403/404), `SchadenEintragenAsync` (Feldfehler), `SchadenEntfernenAsync`, `SchadenPfad`, `AnschaffungswuenscheAsync`, `ErledigtAsync`; `Anzeige.SchadenArt` → „Schaden“, „Verlust“, „Erste-Hilfe-Entnahme“.
- `src/Zeltlotse.App/Features/Inventar/{SchaedenVerluste.razor,Anschaffungswuensche.razor}` + `FreizeitStart.razor` + `OrganisationAnsicht.razor` + `tests/Zeltlotse.Ui.Tests/SchaedenSeitenTests.cs` -- Freizeit-Seite `/freizeiten/{id}/schaeden` (Knopf „Schäden & Verluste“ nach „Inventar“): Dachzeile „Ablage“, H1 „Schäden & Verluste“, Card „Eintragen“ mit Auswahl Art (Vorgabe Schaden), Feld „Was?“ und Knopf „Eintragen“; Card „Liste“ je Eintrag Art-Text, Datum, Bezeichnung als AutosaveFeld, bei `VorfallEintragId` die Zeile „Laut Vorfall-Protokoll: <Text>“ (aus `VorfaelleLadenAsync`, fehlt er, entfällt die Zeile), Knopf „Entfernen“ mit InlineBestaetigung; leer `LeerZustand` „Noch nichts eingetragen.“; in Phase Gelaufen Meldung „Die Freizeit ist abgeschlossen. Die Einträge sind jetzt Anschaffungswünsche an die Organisation.“. Org: Card „Anschaffungswünsche“ bei `inventar-sehen` mit Knopf „Anschaffungswünsche öffnen“; Seite `/organisationen/{OrgId}/anschaffungswuensche`, Dachzeile „Organisation“, H1 „Anschaffungswünsche“, je Freizeit Card mit Einträgen (Art · Bezeichnung · Datum), Knopf „Erledigt“ mit InlineBestaetigung nur bei `inventar-verwalten`; leer „Noch keine Anschaffungswünsche. Nach dem Abschluss einer Freizeit stehen ihre Schäden und Verluste hier.“
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- Schritt `SchaedenAsync` nach `InventarAsync`, idempotent nach Bezeichnung: „Großzelt: Stange verbogen“ (Schaden, angelegt als Leitung Jonas), „Taschenlampe“ (Verlust); Lea-Entnahme erzeugt per Ereignis „Erste-Hilfe-Kasten“; Test stellt die Outbox zu und prüft drei Einträge.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `Schaeden_Verluste` (Jonas, Freizeit → „Schäden & Verluste“, wartet auf den Entnahme-Eintrag) und `Org_Anschaffungswuensche` (Andrea), je Handy und Laptop mit `AxeAsync`, `BildAsync` → `schaeden-verluste-*`, `anschaffungswuensche-*`; durch den neuen Knopf oder die neue Card geänderte Bilder neu erzeugen.
- `doku/src/content/docs/mitarbeitende/schaeden-verluste.md` (neu, „Schäden und Verluste eintragen“, Entnahmen aus dem Vorfall-Protokoll, nach dem Abschluss Anschaffungswünsche) + `org-admin/anschaffungswuensche.md` (neu, auch für Org-Überblick, „Erledigt“, Löschfrist) + Links in `mitarbeitende/index.md`, `org-admin/index.md`, `leitung/index.md`, Satz in `mitarbeitende/vorfall-protokoll.md` ersetzen; `DokuRegeln.cs` PflichtSeiten.

**Acceptance Criteria:**
- Given Lea trägt im Vorfall-Protokoll von Mia eine Entnahme ein, when Jonas danach „Schäden & Verluste“ öffnet, then steht dort „Erste-Hilfe-Entnahme · Erste-Hilfe-Kasten“ mit „Laut Vorfall-Protokoll: 2 Pflaster, 1 sterile Kompresse“, ohne dass jemand etwas zweimal eingibt.
- Given die Freizeit ist abgeschlossen, when ein Org-Überblick-Konto „Anschaffungswünsche öffnen“ wählt, then sieht es die Einträge dieser Freizeit, aber keinen Knopf „Erledigt“ und keinen Vorfall-Text.
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und vier neue Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 24 findings — high 0, medium 1, low 17, false 6, maybe-false 0
- findings:
  - `[false]` `[reject]` (Intent) Kein 403 auf den Freizeit-Routen — jede Freizeit-Rolle darf dort schreiben, eine „falsche Rolle“ gibt es nicht; Pflichtfälle laut Intent-Matrix, 403 ist auf den Org-Routen getestet (Org-Überblick erledigt → 403); wie 3-1 bis 3-3.
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — die Story hat keinen Token-Endpunkt.
  - `[low]` `[defer]` (Intent) Gefüllte Anschaffungswünsche nur per SQL-Phase und bUnit, Screenshot nur Leer-Zustand — Abschluss fehlt bis Story 3-8 (bestand vor dieser Story); in `deferred` aufgenommen.
  - `[false]` `[reject]` (Intent) Screenshots und Doku-Build nicht im Patch belegt — vier neue PNG liegen vor, `npm run build` lief fehlerfrei (49 Seiten); die sechs geänderten Bilder sind per Pixelvergleich echte Änderungen (neuer Knopf, neue Card), Rauschen zurückgesetzt.
  - `[false]` `[reject]` (Blind) `VorfallEintragId` in der Org-Antwort führt zur Person — der Org-Admin sieht das Vorfall-Archiv mit Person und Text ohnehin (Story 2-10), dem Org-Überblick nützt die Kennung ohne Archiv-Recht nichts; keine neue Einsicht.
  - `[low]` `[patch]` (Blind) Bearbeitete Bezeichnung eines Entnahme-Eintrags erreicht die Organisation, Doku verspricht „nur Erste-Hilfe-Kasten“ — Bearbeiten bleibt (jede Bezeichnung ist freier Text des Teams); Doku-Satz in `mitarbeitende/schaeden-verluste.md` richtiggestellt.
  - `[low]` `[reject]` (Blind) Die Seite lädt das ganze Vorfall-Protokoll der Freizeit — dieselben Personen dürfen es lesen (Leitung, Team), die Startseite der Leitung lädt es ebenso; ein Filter wäre neue API-Oberfläche.
  - `[low]` `[reject]` (Blind) Keine Nachmeldung für Entnahmen vor dieser Version — es gibt noch keinen Echtbetrieb; eine Daten-Migration wäre zusätzliche Logik ohne Nutzen.
  - `[false]` `[reject]` (Blind) Nach Entfernen kehrt der Eintrag bei erneuter Zustellung zurück — die Inbox verwirft bereits verarbeitete Ereignisse je Empfänger dauerhaft (AD-4), Entfernen ändert die Inbox nicht.
  - `[low]` `[reject]` (Blind) 404 beim Entfernen/Erledigt zeigt Fehler, Zeile bleibt — carried-Abwägung aus 3-1 bis 3-3 (seltene Gleichzeitigkeit, Neuladen behebt es; Abhilfe wäre zusätzlicher Zweig).
  - `[low]` `[reject]` (Blind) Entnahme kann zusätzlich von Hand eingetragen werden — Doku sagt, dass Entnahmen aus dem Protokoll nicht doppelt eingetragen werden müssen; Hand-Eintrag ist für Entnahmen ohne Vorfall gewollt.
  - `[low]` `[reject]` (Blind) Übersichtsseiten nennen Anschaffungswünsche ohne Hinweis auf 3-8 — beschreibt das Verhalten richtig; die Detailseiten tragen den Hinweis, Story 3-8 entfernt ihn.
  - `[low]` `[defer]` (Blind) Doku und E2E zeigen nur den Leer-Zustand — gleiche Ursache wie oben, dort zurückgestellt.
  - `[low]` `[patch]` (Blind) Kommentar am Empfänger nennt den falschen Grund — Kommentar in `ErsteHilfeEntnahmeEintragen.cs` korrigiert.
  - `[low]` `[reject]` (Blind) Demo prüft Doppelte über die Bezeichnung — carried-Abwägung aus 3-3 (Demo läuft frisch).
  - `[low]` `[patch]` (Verification gap) Autosave der Bezeichnung und die mitlaufende Überschrift ungetestet — bUnit-Test `Bezeichnung_speichert_per_Autosave_und_die_Ueberschrift_folgt` ergänzt.
  - `[medium]` `[patch]` (Edge) Zwei „Erledigt“ auf derselben Card gleichzeitig: `IndexOf` -1, Ausnahme, Seite stürzt ab — nach dem Abruf Card über `FreizeitId` in der aktuellen Liste suchen und deren Einträge filtern; bUnit-Test ergänzt.
  - `[low]` `[patch]` (Edge) `_wuensche` null nach Zurücksetzen während des Abrufs — gleiche Ursache, gleiche Korrektur (Null-Prüfung).
  - `[low]` `[reject]` (Edge) 404 bei Erledigt durch andere Org-Admins — wie die 404-Zeile oben (carried-Abwägung).
  - `[low]` `[reject]` (Edge) 404 beim Entfernen in der Freizeit — wie oben.
  - `[false]` `[reject]` (Edge) Leerer Freizeit-Name in der Card — Gelaufen kommt aus `IFreizeitZeitpunkte` unter demselben Mandantenfilter wie `IFreizeitNamenLesen`; eine Freizeit mit Phase hat auch einen Namen.
  - `[low]` `[reject]` (Edge) Keine Nachmeldung alter Entnahmen (Migration) — wie die Blind-Zeile oben.
  - `[low]` `[reject]` (Edge) Bestehende Demo-Datenbank bekommt den Entnahme-Eintrag nicht — Demo läuft frisch (carried aus 3-3).
  - `[low]` `[reject]` (Edge) Demo-Schlüssel Bezeichnung — wie oben.

## Design Notes

- **Anschaffungswunsch ist abgeleitet, keine eigene Zeile:** UX (EXPERIENCE.md, Nachbereitung) sagt „beim Abschluss werden die Einträge zu Anschaffungswünschen“. Die Spine-Tabelle *Ereignis-Fluss* hat für `FreizeitAbgeschlossen` keinen Empfänger in Inventar; eine Ableitung aus der Phase (Leitprinzip 3) braucht keine Spine-Änderung und wirkt sofort, sobald Story 3-8 den Abschluss setzt. Bis dahin ist die Org-Seite in den Demo-Daten leer; ihr Screenshot zeigt den Leer-Zustand mit Erklärung.
- **Vorfall-Text bleibt in Sicherheit:** Er ist mit dem Schlüssel der Person verschlüsselt (AD-14) und darf nicht ins Ereignis (AD-4). Der Eintrag heißt deshalb „Erste-Hilfe-Kasten“; die Freizeit-Seite setzt den Text aus dem Vorfall-Endpunkt dazu (AD-2: der Client setzt zusammen, Leitung und Team dürfen beide lesen). Die Organisation sieht nur „Erste-Hilfe-Kasten“.
- **Keine Phasensperre:** Schreiben bleibt nach dem Abschluss möglich (AD-17, Ausnahme Anschaffungswünsche); die Org entfernt Erledigtes, das Team ergänzt Vergessenes.
- **„Erledigt“ löscht:** Ein Status „angeschafft“ wäre eine zweite Liste ohne Nutzen; nach dem Abschluss ist der Eintrag nur noch als Wunsch von Belang.
- **403 auf Freizeit-Routen entfällt** wie in 3-1 bis 3-3; auf Org-Routen gibt es ihn (Org-Überblick erledigt nicht).
- **Ereignis per `Attach`:** Der INSERT bleibt Roh-SQL wegen der Idempotenz bei gleichzeitigen Aufrufen; nur ein frisch angelegter Eintrag wird angehängt, damit die Outbox-Stufe genau ein Ereignis schreibt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Inventar.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Schaeden/Vorfaelle/EndpunktListe/Demo), `tests/Zeltlotse.Ablaeufe.Tests` (Filter ErsteHilfe), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. E2E) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Inventar hat jetzt eine Schadens- und Verlustliste je Freizeit (`SchadenVerlust`: Art Schaden/Verlust/Erste-Hilfe-Entnahme, Bezeichnung, erfasst am, optional Kennung des Vorfall-Eintrags).
- **Freizeit-Endpunkte (Leitung und Team, ohne Phasensperre):** Liste lesen, eintragen, Bezeichnung per Autosave ändern, entfernen.
- **Entnahmen aus dem Vorfall-Protokoll:** Ein neuer Vorfall-Eintrag der Art Entnahme löst in Sicherheit `ErsteHilfeEntnommen` aus. `VorfallEintrag` ist dafür jetzt ein `Aggregat`; das Ereignis entsteht nur, wenn der Eintrag frisch angelegt wird. Der Empfänger in Inventar legt „Erste-Hilfe-Kasten“ an, ohne Text und ohne Person.
- **Anschaffungswünsche:** abgeleitet aus den Einträgen der Freizeiten in Phase Gelaufen. Org-Admin und Org-Überblick lesen sie, der Org-Admin markiert sie als „Erledigt“ (der Eintrag wird gelöscht).
- **Lösch-Lauf:** löscht die Einträge mit der Frist für Freizeit-Daten.
- **Web-App:**
  - Seite „Schäden & Verluste“ mit dem Text aus dem Vorfall-Protokoll bei Entnahmen.
  - Org-Seite „Anschaffungswünsche“.
  - Knopf auf der Startseite der Freizeit, Card auf der Org-Seite.
- **Demo-Daten:** „Taschenlampe“ und „Großzelt: Stange verbogen“; Leas Entnahme kommt über das Ereignis dazu.
- **Doku:** zwei neue Seiten, vier neue Screenshots.

**Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Inventar/Schaeden.cs` -- Datenformen.
- `src/Modules/Inventar/Zeltlotse.Inventar/Schaeden/*`, `Daten/InventarDbContext.cs`, Migration `SchadenUndVerlust`, `InventarModul.cs`, `Datenschutz/InventarLoeschlauf.cs`, csproj -- Entität, sechs Endpunkte, Empfänger, Lösch-Lauf.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit.Contracts/Events/ErsteHilfeEntnommen.cs`, `Sicherheit/Vorfaelle/{VorfallEintrag,VorfaelleBearbeiten}.cs`, Migration `VorfallEreignis` -- Ereignis.
- `src/Zeltlotse.Ui/{Api/InventarApiClient.cs,Texte/Anzeige.cs}` -- Client und Texte.
- `src/Zeltlotse.App/Features/Inventar/{SchaedenVerluste,Anschaffungswuensche}.razor`, `FreizeitStart.razor`, `OrganisationAnsicht.razor` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/*` -- Demo-Schritt `SchaedenAsync`.
- Tests:
  - neu: `tests/Zeltlotse.Inventar.Tests/SchadenVerlustTests.cs`, `tests/Zeltlotse.Api.Tests/SchaedenTests.cs`, `tests/Zeltlotse.Ablaeufe.Tests/ErsteHilfeEntnommenTests.cs`, `tests/Zeltlotse.Ui.Tests/SchaedenSeitenTests.cs`;
  - ergänzt: Endpunktliste, Demo, Doku-Regeln und Doku-Tests, Screenshots.
- `doku/src/content/docs/{mitarbeitende/schaeden-verluste.md,org-admin/anschaffungswuensche.md}` (neu) -- Doku.
- Links in den Index-Seiten, `vorfall-protokoll.md`, `org-admin/inventar.md` -- Doku-Verweise.
- Screenshots:
  - neu: `schaeden-verluste-*`, `anschaffungswuensche-*`;
  - neu erzeugt: `loeschfristen-*`, `nachweis-richtlinie-*` (neue Card verschiebt die Org-Seite), `schutzkonzept-hinweis-*` (neuer Knopf).

**Review:** 24 Befunde (high 0, medium 1, low 17, false 6).
- **Gepatcht:** 1 Eintrag medium (Absturz bei zwei gleichzeitigen „Erledigt“ auf derselben Card, mit dem Null-Fall) und 3 low:
  - Autosave-Test der Bezeichnung;
  - Kommentar am Empfänger;
  - Doku-Satz zur Sicht der Organisation.
- **Zurückgestellt:** 1 (Anschaffungswünsche nur im Leer-Zustand zu sehen, bis Story 3-8 den Abschluss bringt).
- **Abgelehnt:** 13. Die Gründe stehen im Review Triage Log:
  - 403 und Token-Fälle laut Matrix;
  - Vorfall-Texte dürfen dieselben Personen ohnehin lesen;
  - keine Nachmeldung ohne Echtbetrieb;
  - carried-Abwägungen aus 3-1 bis 3-3.

**Folge-Review empfohlen:** nein (kein high, nur ein medium gepatcht).

**Verifikation:**
- `dotnet build Zeltlotse.slnx` -- 0 Warnungen.
- **Komplettlauf `dotnet test` vor dem Review:**
  - alle Projekte grün, inklusive E2E mit den Doku-Screenshots;
  - die Api-Tests liefen wegen des Zeitlimits getrennt (754/754);
  - die Architektur-Tests liefen nach Erzeugung der Bilder und der Korrektur der Pflichtseiten-Erwartung erneut (133/133).
- **Nach den Patches gezielt:**
  - Ui.Tests 608/608, Inventar.Tests 34/34, ArchitectureTests 133/133;
  - E2E `Org_Anschaffungswuensche` 2/2.
- `npm run build` in `doku/` -- fehlerfrei, 49 Seiten.
- Matrix-Audit: Jede Zeile der I/O-Matrix ist durch gelaufene, grüne Tests abgedeckt.
- Screenshots per Pixelvergleich geprüft: Rauschen (Datum, Token, Testreihenfolge) zurückgesetzt.

**Restrisiken:**
- Bis Story 3-8 kann keine Freizeit abgeschlossen werden; die Anschaffungswünsche bleiben in der Praxis leer.
- Entnahmen, die vor dieser Version im Vorfall-Protokoll stehen, kommen nicht auf die Liste.
- „Erledigt“ und „Entfernen“ zeigen bei einem schon gelöschten Eintrag einen Fehler statt die Zeile zu entfernen.
- Die Seite lädt das ganze Vorfall-Protokoll der Freizeit, um die Texte der Entnahmen anzuzeigen.
- Das Datum „Eingetragen am“ in `schaeden-verluste-*` ändert sich mit jedem Lauf (Rauschen, wie bei anderen Bildern).
