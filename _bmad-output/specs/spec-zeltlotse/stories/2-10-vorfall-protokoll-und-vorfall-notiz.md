---
title: 'Vorfall-Protokoll und Vorfall-Notiz'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '0947e314b9d30b5aee86aec5350997c9d374e0b9'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-9-schutzkonzept-bestaetigen.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-8-notfall-sicherheitskette-und-schnellsuche.md'
warnings: ['oversized']
deferred:
  - summary: >-
      FreizeitAbgeschlossen wird noch nirgends ausgelöst; das Archiv der Vorfall-Notizen bleibt bis dahin leer.
    evidence: |-
      Story 2-10 baut Ereignis-Typ und Empfänger VorfaelleArchivieren (Spine, Ereignis-Fluss). Auslösen und den Durchlauf-Test (AD-28, AD-30) liefert die Story „Freizeit abschließen“; dort auch Demo-Daten mit archivierten Notizen und den Doku-Satz „bis dahin bleibt das Archiv leer“ entfernen.
    location: >-
      src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/FreizeitAbgeschlossen.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** Mitarbeitende können nicht festhalten, was bei einem Vorfall passiert ist, welches Medikament gegeben und was aus dem Erste-Hilfe-Kasten entnommen wurde. Die Leitung sieht davon nichts, und nach dem Abschluss gibt es kein Archiv in der Organisation (CAP-17, FR-31).

**Approach:**
- **Modul Sicherheit** besitzt den Vorfall-Eintrag (Datenbesitz).
  - Ein Eintrag gehört zu genau einer betroffenen Person der Freizeit.
  - Er hat eine von drei Arten: Geschehen, Medikament, Entnahme.
  - Der Text ist verschlüsselt mit dem Schlüssel der betroffenen Person (AD-14).
  - Einträge sind unveränderlich; eine Korrektur ist ein neuer Eintrag (AD-19).
  - Die Kennung kommt vom Gerät, und doppeltes Senden zählt einmal (AD-18).
- **„Vorfall-Notiz“ ist die Sicht der Leitung** auf diese Einträge: auf der Startseite, neueste oben.
- **Archiv:**
  - Der Empfänger des Ereignisses `FreizeitAbgeschlossen` (Spine-Tabelle *Ereignis-Fluss*) markiert die Einträge der Freizeit als archiviert.
  - Org-Admins sehen archivierte Einträge im Archiv der Organisation.
  - Der Löschlauf löscht die Einträge nach der Frist für Freizeit-Daten.
- **Web-App:**
  - Seite „Vorfall-Protokoll“ je Person. Erreichbar über den Freizeitpass; die Sicherheitskette zeigt den Weg dorthin.
  - Card „Vorfall-Notizen“ für die Leitung auf der Startseite.
  - Archiv-Seite der Organisation.
- Dazu Tests, Demo-Daten, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Datenformen** in `src/Zeltlotse.Api.Contracts/Sicherheit/Vorfaelle.cs` (nur additiv):
  - `enum VorfallArt { Geschehen = 1, Medikament = 2, Entnahme = 3 }`.
  - `VorfallEintragNeu(Guid Id, Guid PersonId, VorfallArt Art, string? Text)`.
  - `VorfallEintrag(Guid Id, Guid PersonId, string? Vorname, string? Nachname, VorfallArt Art, string? Text, DateTimeOffset ErfasstAm, string? VerfasserVorname, string? VerfasserNachname)`. `Text` ist `null`, wenn der Schlüssel der Person vernichtet ist.
  - `VorfallArchivFreizeit(Guid FreizeitId, string Name, IReadOnlyList<VorfallEintrag> Eintraege)`.
  - `VorfallFelder` mit den JSON-Namen `id`, `personId`, `art`, `text` und `TextMaximaleLaenge = 2000`.
  - Bei gelöschten Personen gilt `Vorname` = „Gelöschte Person“ und `Nachname` = `null` (Muster aus `TeamNotizBearbeiten.cs`). Das gilt für die betroffene Person und für Verfassende.
- **Neues Ereignis** `FreizeitAbgeschlossen(Guid FreizeitId)` in `Zeltlotse.Freizeiten.Contracts/Events/FreizeitAbgeschlossen.cs`.
  - Es steht schon in der Spine und wird in dieser Story nicht ausgelöst. Auslösen wird es die Story „Freizeit abschließen“.
- **Neue Leseschnittstellen** in `Zeltlotse.Freizeiten.Contracts`. Sie stehen unter dem Mandantenfilter, Umsetzung und Registrierung nach dem Muster `IFreizeitTeamLesen`:
  - `Personen/IFreizeitPersonenLesen.GehoertDazuAsync(Guid freizeitId, Guid personId)` → `bool`. Wahr ist es, wenn die Person in dieser Freizeit eine Anmeldung hat, die nicht abgesagt ist, oder eine Freizeit-Rolle Leitung oder Team. `GeloeschtePerson.Id` ergibt immer falsch.
  - `Namen/IFreizeitNamenLesen.NamenAsync(IReadOnlyCollection<Guid> freizeitIds)` → `IReadOnlyDictionary<Guid, string>`.
- **Endpunkte** (Modul Sicherheit):

  | # | Endpunkt | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /freizeiten/{freizeitId}/sicherheit/vorfaelle[?personId=]` | `SicherheitRechte.Lesen` | `200 IReadOnlyList<VorfallEintrag>`, nach `ErfasstAm` absteigend, dann nach `Id` absteigend; mit `personId` nur diese Person |
  | 2 | `POST /freizeiten/{freizeitId}/sicherheit/vorfaelle` (Body `VorfallEintragNeu`) | `SicherheitRechte.Lesen` | `201` mit `Location`; die verfassende Person kommt aus `IAngemeldet.PersonId`, `ErfasstAm` aus `TimeProvider` |
  | 3 | `GET /organisationen/{orgId}/sicherheit/vorfall-archiv` | `SicherheitRechte.Archiv = [OrgAdmin]` (`NurOrgRolle`) | `200 IReadOnlyList<VorfallArchivFreizeit>`: nur archivierte Einträge, je Freizeit gruppiert, Freizeiten nach dem neuesten Eintrag absteigend |

- **Regeln für den POST:**
  - Formfehler ergeben `400` am Feld:
    - `id` ist leer (`Guid.Empty`).
    - `art` ist unbekannt.
    - `text` ist leer oder nur Leerzeichen; sonst wird getrimmt.
    - `text` hat mehr als 2000 Zeichen.
  - `personId` gehört nicht zur Freizeit, oder der Schlüssel der Person fehlt → `400` am Feld `personId` mit „Diese Person gehört nicht zur Freizeit.“
  - Phase `Gelaufen` (über `IFreizeitZeitpunkte`) → `409 freizeit-abgeschlossen`. Alle anderen Phasen sind erlaubt.
  - **Idempotent:** Gibt es die `Id` schon in dieser Freizeit, antwortet der Endpunkt `201` und legt keine zweite Zeile an. Das gilt auch für zwei gleichzeitige POSTs. Gibt es die `Id` in einer anderen Freizeit, antwortet er `409 konflikt`.
  - Der Command überschreibt `ToString()` ohne Text (AD-22).
- **Entität `VorfallEintrag`:**
  - Tabelle `sicherheit.vorfall_eintrag`, `IOrganisationsEigen`, Migration `VorfallProtokoll`.
  - Felder: `Id` (`ValueGeneratedNever`), `OrganisationId`, `FreizeitId`, `PersonId`, `VerfasstVon`, `Art`, `[Besonders] Geschuetzt Text`, `ErfasstAm`, `ArchiviertAm?`.
  - Keine Version. Es gibt nur `Anlegen(...)`, das prüft, dass `text.PersonId == personId` (Muster `TeamNotiz.Anlegen`), und `Archivieren(jetzt)`. Beide Methoden setzen nur, was leer ist.
- **Empfänger `VorfaelleArchivieren`** (`FreizeitAbgeschlossen`, `EmpfaengtEreignis`): Er setzt `ArchiviertAm` bei allen Einträgen der Freizeit, bei denen es noch fehlt. Wiederholt zugestellt ändert er nichts.
- **Datenschutz** (AD-16, Lösch-Klasse „aufbewahrungspflichtig für die Organisation“):
  - Der vorhandene Empfänger `SicherheitPersonLoeschen` setzt `PersonId` und `VerfasstVon` der gelöschten Person auf `GeloeschtePerson.Id`. Die Zeile bleibt, der Text wird mit dem vernichteten Schlüssel unlesbar.
  - `SicherheitAuskunft` bekommt den Abschnitt „Vorfall-Protokoll“:
    - Einträge über die Person: Zeitpunkt, Art und entschlüsselter Text.
    - Von ihr verfasste Einträge: „Eintrag verfasst“ mit Zeitpunkt.
  - `SicherheitLoeschlauf` löscht zusätzlich die Einträge von `FreizeitenMitAbgelaufenenDaten`.
- **Ui-Client `SicherheitApiClient`:**
  - `VorfaelleLadenAsync(freizeitId, personId?)`: 403/404 → `null`.
  - `VorfallAnlegenAsync(freizeitId, VorfallEintragNeu)`: Ergebnis Erfolg, Abgeschlossen oder Fehler.
  - `VorfallArchivLadenAsync(orgId)`: 403/404 → `null`.
- **Aktionsschlüssel** `VorfallArchivSehen = "vorfall-archiv-sehen"`:
  - Er steht in `Aktionsschluessel` (Gemeinsam).
  - `OrgRechte.Erlaubt` liefert ihn für `[OrgAdmin]` über die Liste `OrgRechte.VorfallArchiv`.
  - Ein Gleichheitstest stellt sicher, dass `SicherheitRechte.Archiv` dieser Liste entspricht (Muster der Gleichheitstests aus 2-8).
- **Seite `Features/Sicherheit/VorfallProtokoll.razor`** (`/freizeiten/{FreizeitId:guid}/vorfall/{PersonId:guid}`, Leitung und Team):
  - Dachzeile „Notfall“, h1 „Vorfall-Protokoll“, darunter der Name der Person aus der Personenliste; ohne Treffer entfällt die Zeile.
  - Kontext oder Einträge `null` → `NichtVerfuegbar`.
  - Meldung (Hinweis): „Jeder Eintrag ist sofort für die Leitung sichtbar. Einträge lassen sich nicht ändern; eine Korrektur ist ein neuer Eintrag.“
  - Drei mehrzeilige `Feld`er ohne Senden-Knopf:
    - „Was ist passiert?“ (Geschehen);
    - „Medikament gegeben (welches, wie viel)“ (Medikament);
    - „Aus dem Erste-Hilfe-Kasten entnommen“ (Entnahme).
  - **Speichern:** `@onchange`, also beim Verlassen des Felds mit geändertem Text. Ist der Text nach dem Trimmen nicht leer, wird mit neuer `Guid.CreateVersion7()` gePOSTet.
    - Erfolg: Das Feld leert sich, `SpeicherHinweis` zeigt „gespeichert“, und die Liste lädt neu.
    - Fehler: Der Text bleibt im Feld, und die Meldung erscheint am Feld.
  - In Phase `Gelaufen` fehlen die Felder; stattdessen steht die Meldung „Diese Freizeit ist abgeschlossen und nur lesbar.“
  - Card „Protokoll“: je Eintrag „<Datum Uhrzeit> · <Art-Text>“, der Text und „von <Name>“. Fehlt der Text: „Inhalt nicht mehr lesbar.“ Ohne Einträge: `LeerZustand` „Noch keine Einträge.“
  - Link „Zum Freizeitpass“ auf `freizeiten/{id}/freizeitpaesse/{personId}`.
- **Freizeitpass** (`FreizeitpassEinsicht.razor`): Nach dem Zurück-Link steht der Link „Vorfall-Protokoll“ auf die Seite.
- **Sicherheitskette** (`Sicherheitskette.razor`): Vor „Wen anrufen“ steht die Card „Vorfall-Protokoll“ mit dem Satz „Betroffene Person oben suchen und im Freizeitpass „Vorfall-Protokoll“ wählen. Jeder Eintrag ist sofort für die Leitung sichtbar.“ Der Kommentar zu 2-10 wird angepasst.
- **Startseite (`FreizeitStart.razor`):**
  - Bei `DarfEinstellungen` (Leitung) werden die Einträge über `SicherAsync` mitgeladen.
  - Ab einem Eintrag erscheint die Card „Vorfall-Notizen“, neueste oben. Je Eintrag:
    - „<Datum Uhrzeit> · <Vorname Nachname>“ als Link auf das Vorfall-Protokoll der Person;
    - die Zeile „<Art-Text>: <Text>“;
    - „von <Name>“.
  - Die Card ist kein Warnhinweis und zählt nicht in `AnzahlZeilen`. Das Team sieht sie nicht und lädt die Einträge nicht.
- **Organisation:**
  - In `OrganisationAnsicht.razor` steht bei `Darf(VorfallArchivSehen)` die Card „Vorfall-Notizen“ mit dem Satz „Nach dem Abschluss einer Freizeit liegen ihre Vorfall-Notizen hier, bis die Frist für Freizeit-Daten abläuft.“ und dem Knopf „Archiv öffnen“.
  - Neue Seite `Features/Sicherheit/VorfallArchiv.razor` (`/organisationen/{OrgId:guid}/vorfall-archiv`):
    - Dachzeile „Organisation“, h1 „Archiv der Vorfall-Notizen“.
    - Je Freizeit eine Card mit den Einträgen wie oben.
    - Leer: `LeerZustand` „Noch keine archivierten Vorfall-Notizen.“
    - `null` → `NichtVerfuegbar`.
- Art-Texte stehen an einer Stelle in `Anzeige.cs`: „Was passiert ist“, „Medikament gegeben“, „Aus dem Erste-Hilfe-Kasten entnommen“.
- **Demo** (Schritt `VorfaelleAsync` nach `SchutzkonzeptAsync`, idempotent über die Kennungen, über den Command; feste Kennungen in `DemoKatalog`): drei Einträge zu Mia Probst.
  - Lea, Geschehen: „Beim Geländespiel am linken Knie geschnitten. Wunde gereinigt und verbunden.“
  - Lea, Entnahme: „2 Pflaster, 1 sterile Kompresse“.
  - Jonas, Medikament: „Kühlpack, kein Medikament“.
  - Archiviert ist nichts.
- Test vor dem Code. Neue Routen kommen in die Listen von `EndpunktListeTests`, neue Doku-Seiten in `DokuRegeln.PflichtSeiten`.

**Never:**
- Kein Ereignis `ErsteHilfeEntnommen` und kein Eintrag auf der Verlustliste (Story 3-4).
- Kein Offline-Schreiben, keine Warteschlange, keine Offline-Leiste mit Zählung (V6).
- Kein Abschluss-Endpunkt und kein Auslösen von `FreizeitAbgeschlossen` (Story „Freizeit abschließen“).
- Kein Bearbeiten oder Löschen einzelner Einträge, kein „gesehen“-Status, kein Zähler, keine Mail, kein Push.
- Keine neue Löschfrist.
- Kein Zugriff auf Inhalte über das Zugriffsprotokoll der Person.
- Kein neues Modul, keine Änderung an `ARCHITECTURE-SPINE.md`. Keine Pop-ups, keine eigenen `elkw-`/`zl-`-Klassen außerhalb `Ui.Komponenten`.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Schreiben | Emma in X angemeldet; Tobias POST `{id, Emma, Geschehen, " Knie "}` | `201`; Miriam GET liefert den Eintrag mit Text „Knie“, Name Emma und Verfasser Tobias; in der DB steht kein Klartext | – |
| Doppelt | derselbe POST zweimal, auch gleichzeitig | beide `201`, eine Zeile | – |
| Fremde Id | `id` existiert schon in Z | `409 konflikt` | – |
| Person fremd | `personId` = Mia (nicht in X), unbekannte Guid, `GeloeschtePerson.Id` | je `400` am Feld `personId` | – |
| Team als Person | `personId` = Miriam (Leitung, ohne Anmeldung) | `201` | – |
| Abgesagt | Emmas Anmeldung abgesagt | `400` am Feld `personId` | – |
| Formfehler | `text` leer; 2001 Zeichen; `art` 9; `id` leer | je `400` | – |
| Filter | Einträge zu Emma und Tobias; GET `?personId=Emma` | nur Emma, neueste oben | – |
| Abgeschlossen | X `Gelaufen` (SQL) | POST `409 freizeit-abgeschlossen`; GET `200` | – |
| Rechte X | Miriam; Tobias; Andrea (Org-Admin ohne Rolle); fremde Organisation; ohne Sitzung | GET `200/200/404/404/401`; POST `201/201/404/404/401` | – |
| Archiv | Einträge in X, einer mit `ArchiviertAm` (SQL); Andrea GET Archiv A | nur der archivierte, gruppiert unter „Zeltlager Kapf 2027“ | – |
| Rechte Archiv | Andrea; Org-Überblick von A; Miriam (keine Org-Rolle); Lea auf A; ohne Sitzung | `200/403/404/404/401` | – |
| Archivieren | Empfänger `VorfaelleArchivieren` für X, zweimal | Einträge von X archiviert, die von Z nicht; zweiter Lauf ändert `ArchiviertAm` nicht | – |
| Mandant | Einträge in X und Z | der Lese-Kontext je Mandant zeigt nur die eigenen | – |
| Person gelöscht | `PersonGeloescht` für Emma; für Tobias | Zeile bleibt, `PersonId` bzw. `VerfasstVon` wird zum Platzhalter; GET zeigt „Gelöschte Person“ | – |
| Schlüssel weg | Schlüssel der Person vernichtet | GET `200` mit `text null` | – |
| Frist | Löschlauf mit X in `FreizeitenMitAbgelaufenenDaten` | Einträge von X weg, die von Z bleiben | – |
| Auskunft | Emma hat einen Eintrag, Tobias hat einen verfasst | Abschnitt „Vorfall-Protokoll“ mit Text (Emma) bzw. „Eintrag verfasst“ (Tobias) | – |
| Seite | Lea, Demo, Vorfall-Protokoll von Mia | drei Felder, Hinweis, drei Einträge, neueste oben | – |
| Blur | Text in „Was ist passiert?“, dann Feld verlassen | POST mit Art Geschehen; Feld leer; Liste lädt neu | Fehler: Text bleibt, Meldung am Feld |
| Startseite | Jonas, Demo; Lea, Demo | Card „Vorfall-Notizen“ mit drei Einträgen / keine Card, kein Abruf | Abruf scheitert: keine Card |
| Archiv-Seite | Andrea, Demo | Card auf der Org-Seite; Archiv leer mit „Noch keine archivierten Vorfall-Notizen.“ | – |

</intent-contract>

## Code Map

- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/`:
  - `SicherheitModul.cs`: Registrierung von Empfänger `SicherheitPersonLoeschen` Z.27 und Gruppe `/freizeiten/{freizeitId:guid}/sicherheit`. Eine Org-Gruppe kommt neu dazu, Muster `FreizeitenModul.cs:64-71` (`api.MapGroup($"/organisationen/{{{Rechtemerkmale.OrgRouteWert}:guid}}/sicherheit")` + `NurOrgRolle`).
  - `Sicherheitskette/SicherheitRechte.cs`: Hier kommt `Archiv` dazu.
  - `Schutzkonzept/SchutzkonzeptBearbeiten.cs`: Vorlage für den POST mit „gleichzeitig eine Zeile“ (`SchutzkonzeptGelesenCommand`), für die Phase über `IFreizeitZeitpunkte`, die Ergebnis-Hierarchie und die Probleme.
  - `Datenschutz/SicherheitDatenschutz.cs`: `SicherheitPersonLoeschen` (Z.16-29), `SicherheitAuskunft`, `SicherheitLoeschlauf`.
  - `Daten/SicherheitDbContext.cs` (`SicherheitModell.Konfigurieren`) und die Migrationen (zuletzt `20261013131326_Schutzkonzept`).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/TeamNotizen/{TeamNotiz.cs,TeamNotizBearbeiten.cs}` -- Vorlage:
  - Verschlüsseln im Handler (Z.53-88, `PersonenSchluesselFehltAusnahme`).
  - `ToString()` ohne Text.
  - `TextAsync` beim Entschlüsseln (Z.215-225).
  - Platzhalter-Namen (Z.171-187, `AnmeldungenListeEndpunkt.GeloeschtePersonText`).
  - EF-Konfiguration `FreizeitenDbContext.cs:132-143`. `Geschuetzt` wird über die Konvention als `bytea` abgebildet.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Events/AnmeldungEntschieden.cs,Team/IFreizeitTeamLesen.cs}`:
  - Muster für Ereignis und Schnittstelle.
  - Umsetzung `Rechte/FreizeitTeamLesen.cs`, Registrierung `FreizeitenModul.cs:41-47`.
  - Anmeldungen: `Domaene/Anmeldung` (Status `Abgesagt`), Rollen: `Domaene/FreizeitRolle`.
- Empfänger-Muster: `src/Zeltlotse.SharedKernel/Ereignisse/{IEreignisEmpfaenger.cs,Umschlag.cs}` und `Personen/Freigaben/Freigabe.cs:129-153`.
- `src/Zeltlotse.SharedKernel/Datenschutz/{GeloeschtePerson.cs,IVerschluesselung.cs,ILoeschlauf.cs,IDatenauskunft.cs}`.
- Org-Rechte:
  - `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` (Listen und `Erlaubt`).
  - `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs:55` (`Aktionsschluessel`).
  - `src/Zeltlotse.Api/Rechte/RechtePruefung.cs` (Org ohne Rolle → 404, falsche Rolle → 403).
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Namen/IPersonenNamen.cs` -- liefert keine Einträge für gelöschte Personen.
- Tests:
  - `tests/Zeltlotse.Api.Tests/{FreizeitpassSzenario.cs,SchutzkonzeptTests.cs,TeamNotizenTests.cs,EndpunktListeTests.cs,DemoDatenTests.cs,DatenauskunftTests.cs}`.
    - Szenario: Org A/B, X „Zeltlager Kapf 2027“/Z, Miriam (Leitung X und Z), Tobias (Team X), Andrea (Org-Admin A), Lea (Org B), Katrin mit Emma und Mia (nicht angemeldet; `KindAnmeldenAsync`, `AbsagenAsync`).
    - Gelaufen per SQL: `UPDATE freizeiten.freizeit SET "AbgeschlossenAm" = now()`.
    - Api.Tests sieht keine Internals der Module: Archivierung per SQL, den Empfänger testet `Sicherheit.Tests`.
  - `tests/Zeltlotse.Sicherheit.Tests/` -- Entitäts-, Validator-, Mandantenfilter- und Gleichheitstests (`InternalsVisibleTo` vorhanden).
  - `tests/Zeltlotse.ArchitectureTests/{EreignisFlussTests.cs,LoeschPflichtTests.cs,DokuRegeln.cs}`. Die Zeile `FreizeitAbgeschlossen` → Sicherheit, Kanäle steht schon in der Spine.
  - `tests/Zeltlotse.Ui.Tests/{SicherheitsketteSeitenTests.cs,SchutzkonzeptSeitenTests.cs,LeitungsCockpitSeitenTests.cs,FreizeitpaesseSeitenTests.cs,NachgebildeterHandler.cs}` -- bUnit-Muster. Die Stubs von Startseite, Pass und Kette brauchen die neue Route.
  - Org-Seite: Tests zu `OrganisationAnsicht` (suchen per `grep OrganisationAnsicht tests/Zeltlotse.Ui.Tests`).
- Web-App:
  - `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor`: Cards Z.69-151, Laden Z.245-301, `SicherAsync` Z.311.
  - `FreizeitpassEinsicht.razor`: Kopf Z.26-27.
  - `Features/Sicherheit/Sicherheitskette.razor`: Kommentar Z.14, Cards Z.49-75.
  - `Features/Organisationen/OrganisationAnsicht.razor`: Cards Z.44-110, `Darf` Z.211.
  - `Ui.Komponenten/{Feld,SpeicherHinweis,Card,LeerZustand,Meldung,Knopf}.razor`. `Feld` reicht `@onchange` über `Attribute` durch.
  - `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,Texte/Anzeige.cs}`.
- Demo: `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` (`SchutzkonzeptAsync` Z.667, Aufruf Z.130). Mandant setzen und Commands per `ISender` im Scope.
- E2E:
  - `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: Muster Schutzkonzept Z.305-337 und 1341-1390, `BildAsync` Z.1540, `LeaStartseiteAsync` Z.1379, `OrgAdminMail`, `DemoMail`.
  - `NotfallTests.cs`: eigener Ablauf in einer laufenden Freizeit.
- Doku: `doku/src/content/docs/{mitarbeitende,leitung,org-admin}/`, `daten.md`, `mitarbeitende/notfall.md`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Sicherheit.Tests/` -- ZUERST:
  - Entität: Anlegen mit falschem Schlüssel wirft; `Archivieren` setzt nur einmal.
  - Validator.
  - Mandantenfilter.
  - Empfänger `VorfaelleArchivieren` (X ja, Z nein, zweimal).
  - Gleichheit `SicherheitRechte.Archiv` mit der Org-Liste.
- `tests/Zeltlotse.Api.Tests/VorfaelleTests.cs` (neu) -- ZUERST: jede API-Zeile der Matrix bis „Auskunft“, inklusive AD-30. Dazu kommen `EndpunktListeTests` und `DemoDatenTests` (drei Demo-Einträge zu Mia). Die DB-Prüfung „kein Klartext“ läuft per SQL auf `bytea`.
- `tests/Zeltlotse.Ui.Tests/VorfallSeitenTests.cs` (neu) -- ZUERST:
  - Seite Vorfall-Protokoll: Liste, leer, Blur speichert mit richtiger Art, Fehler behält den Text, `Gelaufen` ohne Felder, `NichtVerfuegbar`, unlesbarer Text.
  - Startseiten-Card: Leitung mit und ohne Einträge, Team ohne Abruf, Abruf scheitert.
  - Link im Pass, Card in der Kette.
  - Archiv-Seite und Org-Card.
- `src/Zeltlotse.Api.Contracts/{Sicherheit/Vorfaelle.cs,Gemeinsam/FreizeitKontext.cs}` -- Datenformen und Aktionsschlüssel.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Events/FreizeitAbgeschlossen.cs,Personen/IFreizeitPersonenLesen.cs,Namen/IFreizeitNamenLesen.cs}` und die Umsetzung in `Zeltlotse.Freizeiten` mit Registrierung -- Ereignis und Leseschnittstellen.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` -- Liste `VorfallArchiv` und Aktionsschlüssel.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Vorfaelle/*.cs,Datenschutz/SicherheitDatenschutz.cs,Daten/SicherheitDbContext.cs,Migrationen/*_VorfallProtokoll*,SicherheitModul.cs,Sicherheitskette/SicherheitRechte.cs}` -- Entität, Commands, Empfänger, Endpunkte, Datenschutz, Migration.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,Texte/Anzeige.cs}` -- Client und Art-Texte.
- `src/Zeltlotse.App/Features/{Sicherheit/VorfallProtokoll.razor,Sicherheit/VorfallArchiv.razor,Sicherheit/Sicherheitskette.razor,Freizeiten/FreizeitStart.razor,Freizeiten/FreizeitpassEinsicht.razor,Organisationen/OrganisationAnsicht.razor}` -- Oberfläche laut Always.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` -- Demo-Einträge.
- `tests/Zeltlotse.E2E.Tests/NotfallTests.cs` -- Ablauf erweitern: Im Pass „Vorfall-Protokoll“ öffnen, Text in „Was ist passiert?“ tippen und das Feld verlassen. Der Eintrag erscheint in „Protokoll“, das Feld ist leer. Dazu `AxeAsync`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories, je Handy und Laptop mit axe, nur lesend. Feste Texte für Datum und Uhrzeit (Muster 2-9):
  - `Vorfall_Protokoll` (Lea → Freizeitpässe → Mia → „Vorfall-Protokoll“, Bild `vorfall-protokoll`).
  - `Vorfall_Notizen` (Jonas, Startseite, Card, Bild `vorfall-notizen`).
  - `Vorfall_Archiv` (Andrea, Organisation → „Archiv öffnen“, Bild `vorfall-archiv`).
  - Vorhandene Bilder mit fester Uhrzeit (z. B. `freizeit-start`) bleiben ruhig. Erscheint die neue Card dort, wird ihr Datum ebenso fest ersetzt.
- Doku:
  - Neu `mitarbeitende/vorfall-protokoll.md` („Vorfall-Protokoll führen“): Weg über Pass und Sicherheitskette, drei Felder, Speichern beim Verlassen, keine Änderung, Korrektur als neuer Eintrag, sofort für die Leitung sichtbar, Entnahmen auf der Verlustliste folgen später. Bild `vorfall-protokoll`.
  - Neu `leitung/vorfall-notizen.md` (Card auf der Startseite, Archiv nach Abschluss). Bild `vorfall-notizen`.
  - Neu `org-admin/vorfall-archiv.md` (Archiv, Frist für Freizeit-Daten, Löschen). Bild `vorfall-archiv`.
  - Alle drei in `PflichtSeiten`.
  - Links in den drei `index.md` und in `mitarbeitende/notfall.md`.
  - `daten.md`: Vorfall-Einträge sind verschlüsselt, werden nach dem Abschluss archiviert und mit der Frist für Freizeit-Daten gelöscht.

**Acceptance Criteria:**
- Given Lea (Demo, Team) öffnet über „Freizeitpässe“ den Pass von Mia und dann „Vorfall-Protokoll“, when sie Text in „Medikament gegeben“ tippt und das Feld verlässt, then steht der Eintrag ohne Senden-Knopf oben in „Protokoll“. Jonas sieht ihn danach auf der Startseite in „Vorfall-Notizen“.
- Given eine Freizeit wird abgeschlossen und `FreizeitAbgeschlossen` zugestellt, when der Org-Admin das Archiv öffnet, then stehen dort ihre Vorfall-Notizen unter dem Namen der Freizeit, bis der Löschlauf sie nach der Frist für Freizeit-Daten entfernt.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architektur- und E2E-Tests mit axe). Die Bilder `vorfall-protokoll`, `vorfall-notizen` und `vorfall-archiv` sind je Handy und Laptop geschrieben und verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 34 findings — high 0, medium 2, low 24, false 8, maybe-false 0
- findings:
  - `[medium]` `[patch]` (edge) Zweite Eingabe während eines laufenden Speicherns geht verloren (Feld wird nach dem ersten Erfolg leer neu aufgebaut) — Patch: Feld nur leeren, wenn der Text noch dem gesendeten entspricht; bUnit-Fall.
  - `[low]` `[patch]` (edge) Jeder Speicherversuch erzeugt eine neue Kennung; nach verlorener Antwort entsteht ein Duplikat — Patch: eine Kennung je Art bis zum Erfolg oder zur Textänderung; bUnit-Fall.
  - `[low]` `[reject]` (edge) 400 (Länge, Person) erscheint als allgemeine Fehlermeldung — der Hinweis am Feld nennt die Höchstlänge, die Person kommt nur über gültige Pässe; Feldfehler-Abbildung wäre ein neuer Zweig.
  - `[low]` `[reject]` (edge) Wiederholtes Senden einer gespeicherten Kennung nach `Gelaufen` ergibt 409 — nur nach Abschluss während eines Speicherns erreichbar; Abschluss gibt es noch nicht.
  - `[low]` `[reject]` (edge) Eintrag gleichzeitig mit dem Archivieren bleibt unarchiviert — winziges Zeitfenster zwischen Abschluss-Commit und Zustellung; Abschluss gibt es noch nicht, Fix wäre neue Logik.
  - `[low]` `[reject]` (edge) Leerer Card-Titel im Archiv ohne Freizeitname — Freizeiten werden nie gelöscht, der Mandant ist derselbe; unerreichbar.
  - `[false]` `[reject]` (edge) Auskunft lässt unlesbare Einträge aus — unlesbar heißt Schlüssel vernichtet, also Person gelöscht; deren Einträge tragen dann den Platzhalter und gehören zu keiner Auskunft mehr.
  - `[low]` `[patch]` (edge) `GET ?personId=<Platzhalter>` mischt die Einträge aller gelöschten Personen — Patch: leere Liste für den Platzhalter; API-Test.
  - `[low]` `[defer]` (edge) Nichts löst `FreizeitAbgeschlossen` aus, das Archiv bleibt im Produkt leer — gewollt laut Design Notes; das Auslösen und der Durchlauf-Test gehören zur Story „Freizeit abschließen“ (deferred).
  - `[low]` `[patch]` (edge) Behauptung „doppeltes Senden zählt einmal“ gilt in der Seite nicht — gleiche Ursache und gleicher Patch wie die Kennung je Art.
  - `[low]` `[patch]` (blind) Doku beschreibt das Archivieren nach dem Abschluss als heute verfügbar — Patch: Satz „Das Abschließen einer Freizeit kommt mit einer späteren Version; bis dahin bleibt das Archiv leer.“ in den betroffenen Seiten.
  - `[false]` `[reject]` (blind) „Nur lesbar“ und „archiviert“ haben zwei Auslöser, `Gelaufen` könnte per Datum entstehen — `Gelaufen` entsteht nur durch `AbgeschlossenAm` (AD-17), und genau dieser Abschluss löst das Ereignis aus; der Rest ist der Race-Fall oben.
  - `[low]` `[patch]` (blind) Wiederholen erzeugt Duplikate — wie die Kennung je Art (edge).
  - `[medium]` `[patch]` (blind) Zweite Eingabe während des Speicherns geht verloren — wie oben (edge).
  - `[low]` `[patch]` (blind) Nach einem Fehler lässt sich unveränderter Text nicht erneut speichern — Patch: Speichern beim Verlassen des Felds, solange Text drinsteht (mit derselben Kennung).
  - `[false]` `[reject]` (blind) Team liest über die API alle Einträge, die Doku sage anderes — Lesen für Leitung und Team ist Spec-Entscheidung (Design Notes); die Doku-Aussage betrifft nur die Card der Startseite und stimmt.
  - `[low]` `[reject]` (blind) Link der Vorfall-Notiz einer gelöschten Person führt auf ein leeres Protokoll — nach dem Patch zeigt die Seite keine fremden Einträge; den Link auszublenden bräuchte die Platzhalter-Kennung in `Api.Contracts` (neue öffentliche Fläche).
  - `[low]` `[reject]` (blind) Protokoll-Seite prüft die Person nicht vor den Feldern — erreichbar nur über die Links aus Pässen der Freizeit; eine Prüfung wäre ein neuer Lesepfad.
  - `[low]` `[reject]` (blind) Keine Begrenzung oder Seiten für Startseite und Archiv — wenige Einträge je Freizeit; Paging wäre neue Logik ohne Bedarf.
  - `[low]` `[reject]` (blind) Art-Texte doppelt (Auskunft und Ui), Ui wirft bei unbekannter Art — geschlossenes Enum, eine neue Art ändert beide Stellen bewusst; Server und Client teilen keinen Text-Code.
  - `[false]` `[reject]` (blind) Auskunft lässt unlesbare Einträge aus — wie oben (edge).
  - `[low]` `[reject]` (blind) Roh-SQL mit festem Schema; 409 verrät eine Kennung einer anderen Organisation — gleiches Muster wie 2-8 (`ON CONFLICT`); eine fremde UUID v7 zu erraten ist praktisch ausgeschlossen.
  - `[low]` `[reject]` (blind) `Location` zeigt auf die Liste statt auf den Eintrag — es gibt keinen Einzel-Endpunkt; kein Client nutzt den Header.
  - `[low]` `[reject]` (blind) Kein Test für archiviert und danach gelöscht bzw. Löschlauf auf archivierte — Löschen und Platzhalter wirken unabhängig von `ArchiviertAm` (gleiche Abfrage), kein eigener Zweig.
  - `[low]` `[patch]` (verification-gap) Registrierung `FreizeitAbgeschlossen` → `VorfaelleArchivieren` ungetestet — Patch: Test auf das registrierte Empfänger-Paar.
  - `[low]` `[patch]` (verification-gap) Sortierung des Archivs ungetestet — Patch: API-Test mit zwei Freizeiten und je zwei Einträgen.
  - `[low]` `[defer]` (verification-gap) Archiv bis zur Story „Freizeit abschließen“ leer — wie oben (edge), deferred.
  - `[low]` `[defer]` (intent) Archiv-Ablauf nicht über das Ereignis Ende-zu-Ende geprüft — wie oben, deferred an „Freizeit abschließen“.
  - `[low]` `[patch]` (intent) Doku beschreibt Archivieren als heute verfügbar — wie oben (blind).
  - `[low]` `[reject]` (intent) Kreuz-Nutzer „Lea schreibt, Jonas sieht“ nicht in der Oberfläche geprüft — die API prüft Tobias → Miriam, bUnit die Card; ein Browser-Schreiben würde den Demo-Stand ändern (Muster 2-9).
  - `[false]` `[reject]` (intent) „Falsche Rolle 403“ fehlt an den Freizeit-Endpunkten — dort sind beide Freizeit-Rollen berechtigt; 403 ist am Archiv geprüft.
  - `[false]` `[reject]` (intent) Eigene Frist und Zeitpunkt der Medikamentengabe fehlen — Spec-Entscheidung (Design Notes: vorhandene Frist; Zeitpunkt des Eintrags).
  - `[false]` `[reject]` (intent) Keine eigene Vorfall-Notiz als Objekt — Spec-Entscheidung (Design Notes: Sicht der Leitung).
  - `[false]` `[reject]` (intent) Screenshot-Rauschen in fremden Bildern — die behaltenen Bilder zeigen echte Änderungen (Org-Card, Link im Pass, Card in der Kette); reines Rauschen wurde zurückgesetzt.

## Design Notes

- **Ein Eintrag je Angabe:**
  - Die UX verlangt „speichert beim Verlassen des Feldes, kein Senden-Knopf“, die Architektur verlangt unveränderliche Einträge. Beides passt, wenn jedes verlassene Feld genau einen Eintrag einer Art anlegt.
  - Eine Korrektur ist einfach ein neuer Eintrag.
  - „Vorfall-Notiz“ ist keine zweite Tabelle, sondern die Sicht der Leitung auf dieselben Einträge (einfachste Lösung).
- **Betroffene Person als Pflicht:**
  - AD-14 verschlüsselt mit dem Schlüssel der betroffenen Person.
  - Ohne Person gäbe es keinen Schlüssel. Der Weg führt deshalb über den Pass, und die Sicherheitskette verweist auf die Suche.
  - Leitung und Team zählen als Personen der Freizeit, denn auch sie können sich verletzen.
- **Löschen:**
  - Die archivierte Notiz ist aufbewahrungspflichtig (AD-16): Bei `PersonGeloescht` werden die Kennungen zum Platzhalter, die Zeile bleibt.
  - Der Text ist mit dem Schlüssel der Person verschlüsselt und wird mit ihrem Schlüssel unlesbar (Crypto-Shredding, AD-14). So bleibt nachweisbar, dass und wann etwas war, ohne Gesundheitsdaten einer gelöschten Person.
  - Für die Frist reicht die vorhandene Frist für Freizeit-Daten (Leitprinzip 5); eine eigene Frist lässt sich später ergänzen.
- **Archiv über das Spine-Ereignis:**
  - Die Spine legt `FreizeitAbgeschlossen` → Sicherheit „Vorfall-Notizen archivieren“ fest. Diese Story baut Ereignis-Typ und Empfänger, auslösen wird es die Story „Freizeit abschließen“. Den Durchlauf-Test (AD-28) schreibt die Story „Freizeit abschließen“, sobald das Ereignis entsteht.
  - Bis dahin gibt es keine archivierten Einträge. Die Demo zeigt das leere Archiv, und die Screenshots bleiben ehrlich.
- **Lesen für Leitung und Team:**
  - Das Team liest die Einträge einer Person, damit eine zweite Helferin sieht, was schon gegeben wurde.
  - Die Liste aller Notizen auf der Startseite ist eine Leitungs-Ansicht (UX). Sie ist kein Recht, das der Leitung vorbehalten ist (`rollen-und-rechte.md` nennt es nicht).
- **Archiv für Org-Admins:** Nach dem Abschluss gehören die Notizen der Organisation (AD-16, EXPERIENCE „Organisation verwalten“). Org-Überblick erhält `403`.
- **Phasen:**
  - Schreiben geht in allen Phasen außer `Gelaufen`. So bleiben Nachträge in der Nachbereitung möglich, ohne Sonderlogik.
  - Die Demo-Einträge liegen deshalb in der Demo-Freizeit „in Vorbereitung“.
- **AD-30:**
  - Falsche Rolle `403` gibt es am Archiv (Org-Überblick).
  - Org-Admin ohne Freizeit-Rolle und fremde Organisation ergeben `404`.
  - Token-Fälle haben keinen Gegenstand, weil kein Endpunkt ein Token nutzt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test --solution Zeltlotse.slnx` -- expected: alle Tests grün inklusive Architektur, E2E und Screenshots
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `mitarbeitende/vorfall-protokoll/`, `leitung/vorfall-notizen/` und `org-admin/vorfall-archiv/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-17, FR-31):**
- **Sicherheit:**
  - Entität `VorfallEintrag` (Tabelle `sicherheit.vorfall_eintrag`, Migration `VorfallProtokoll`): unveränderlich, Text mit dem Schlüssel der betroffenen Person verschlüsselt, Kennung vom Gerät (idempotent), `ArchiviertAm`.
  - Endpunkte: `GET/POST …/sicherheit/vorfaelle` (Leitung und Team) und `GET /api/organisationen/{orgId}/sicherheit/vorfall-archiv` (Org-Admin).
  - Empfänger `VorfaelleArchivieren` für das neue Ereignis `FreizeitAbgeschlossen` (noch ohne Auslöser).
  - Datenschutz: Platzhalter bei `PersonGeloescht`, Abschnitt „Vorfall-Protokoll“ in der Auskunft, Löschlauf mit der Frist für Freizeit-Daten.
- **Freizeiten:** Ereignis `FreizeitAbgeschlossen`, Leseschnittstellen `IFreizeitPersonenLesen` und `IFreizeitNamenLesen`.
- **Organisationen:** Aktionsschlüssel `vorfall-archiv-sehen` für Org-Admins.
- **Web-App:** Seite „Vorfall-Protokoll“ je Person (drei Felder, Speichern beim Verlassen, Wiederholen mit derselben Kennung), Card „Vorfall-Notizen“ für die Leitung, Link im Freizeitpass, Card in der Sicherheitskette, Archiv-Seite und Org-Card.
- **Demo:** drei Einträge zu Mia; Archiv leer.

**Dateien:**
- `src/Zeltlotse.Api.Contracts/{Sicherheit/Vorfaelle.cs,Gemeinsam/FreizeitKontext.cs}` – Datenformen, Aktionsschlüssel.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Events/FreizeitAbgeschlossen,Personen/IFreizeitPersonenLesen,Namen/IFreizeitNamenLesen}.cs`, `Zeltlotse.Freizeiten/{Rechte/FreizeitPersonenLesen,FreizeitenModul}.cs` – Ereignis und Leseschnittstellen.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` – Recht fürs Archiv.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/{Vorfaelle/*,Datenschutz/SicherheitDatenschutz.cs,Daten/SicherheitDbContext.cs,Migrationen/20261013143645_VorfallProtokoll*,SicherheitModul.cs,Sicherheitskette/SicherheitRechte.cs}` – Fachlogik.
- `src/Zeltlotse.Ui/{Api/SicherheitApiClient.cs,Texte/Anzeige.cs}` – Client, Texte.
- `src/Zeltlotse.App/Features/{Sicherheit/VorfallProtokoll,Sicherheit/VorfallArchiv,Sicherheit/VorfallNotizen,Sicherheit/Sicherheitskette,Freizeiten/FreizeitStart,Freizeiten/FreizeitpassEinsicht,Organisationen/OrganisationAnsicht}.razor` – Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` – Demo.
- Tests: neu `Sicherheit.Tests/VorfaelleTests`, `Api.Tests/VorfaelleTests`, `Ui.Tests/VorfallSeitenTests`; angepasst `EndpunktListeTests`, `RechteTests`, `DemoDatenTests`, `DokuRegeln`, `DokuTests`, `NotfallTests`, `DokuScreenshotTests`.
- Doku: neu `mitarbeitende/vorfall-protokoll.md`, `leitung/vorfall-notizen.md`, `org-admin/vorfall-archiv.md`; angepasst Indexseiten, `notfall.md`, `daten.md`. Sechs neue Bilder `vorfall-protokoll`, `vorfall-notizen`, `vorfall-archiv` (Handy, Laptop); neu erzeugt mit echter Änderung: `freizeit-start-handy`, `freizeitpass-einsicht-*`, `freizeitpass-vor-bestaetigung-*`, `sicherheitskette-*`, `sicherheitskette-suche-*`, `loeschfristen-*`, `nachweis-richtlinie-*`.

**Review (34 Befunde: high 0, medium 2, low 24, false 8, maybe-false 0):**
- Gepatcht (Einträge: medium 1, low 5):
  - Protokoll-Seite: eine Kennung je Feld bis zum Erfolg, Speichern beim Verlassen auch nach einem Fehler, neuere Eingabe während des Speicherns bleibt erhalten.
  - `GET ?personId=<Platzhalter>` liefert eine leere Liste.
  - Tests: Registrierung des Empfängers, Sortierung des Archivs.
  - Doku: „Das Abschließen einer Freizeit kommt mit einer späteren Version; bis dahin bleibt das Archiv leer.“
- Zurückgestellt: Auslösen von `FreizeitAbgeschlossen` und Durchlauf-Test → Story „Freizeit abschließen“ (`deferred`).
- Verworfen (Begründung im Review Triage Log): allgemeine Fehlermeldung bei 400, 409 nach Abschluss, Race beim Archivieren, leerer Archiv-Titel, Link für gelöschte Personen, Personenprüfung der Seite, Paging, doppelte Art-Texte, Roh-SQL, `Location`, Test archiviert+gelöscht, Kreuz-Nutzer im Browser; alle `false`.

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0, medium 1, low 5.

**AD-30:** Freizeit-Endpunkte: Leitung und Team `200`/`201`, Org-Admin ohne Rolle und fremde Organisation `404`, ohne Sitzung `401`. Archiv: Org-Admin `200`, Org-Überblick `403`, ohne Org-Rolle `404`, ohne Sitzung `401`. Token-Fälle haben keinen Gegenstand.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test --solution Zeltlotse.slnx` nach den Patches (inklusive Architektur, E2E mit axe, Screenshots): 1903 von 1903 bestanden.
- `npm run build` in `doku/`: fehlerfrei, 42 Seiten, darunter die drei neuen (nur die bekannten Warnungen). `npm ci` lief nicht erneut, weil ein laufender Doku-Dev-Server Dateien sperrt.
- Matrix-Audit: Jede Zeile hat einen gelaufenen Test (Sicherheit, Api, Ui, E2E).
- Screenshots mit reinem Rendering-Rauschen per `git checkout` zurückgesetzt.

**Restrisiken:**
- Das Archiv bleibt leer, bis „Freizeit abschließen“ das Ereignis auslöst.
- Der Zeitpunkt eines Eintrags ist der Speicherzeitpunkt am Server; offline geschriebene Einträge (V6) brauchen später einen Gerätezeitpunkt.
- Ein laufender `astro dev`-Server verlor beim ersten `npm ci` kurz seine Abhängigkeiten; sie sind per `npm install` wiederhergestellt, ein Neustart des Servers kann nötig sein.
