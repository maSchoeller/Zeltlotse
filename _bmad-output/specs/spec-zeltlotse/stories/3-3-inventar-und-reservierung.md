---
title: 'Story 3-3: Inventar und Reservierung'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: 'efe71c2b3ba59edddcb5aeff916aa5c5446d76c2'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-2-material-am-programmpunkt.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Modul Inventar ist ein leeres Gerüst. Organisationen können ihr Material nicht als Inventar pflegen, Freizeiten können nichts für einen Zeitraum reservieren, und niemand sieht, wenn parallele Freizeiten dasselbe Stück zur gleichen Zeit brauchen (CAP-21, FR-37, NS-5).

**Approach:** Inventar bekommt `InventarArtikel` (Organisation; Bezeichnung, Menge als Freitext) und `Reservierung` (Artikel, Freizeit, Von, Bis, optional Material). Org-Admins pflegen das Inventar unter `/api/organisationen/{orgId}/inventar`, Org-Überblick liest es. Leitung und Team sehen das Inventar ihrer Organisation unter `/api/freizeiten/{freizeitId}/inventar`, reservieren und heben Reservierungen auf. Überschneidet sich eine Reservierung mit der Reservierung einer anderen Freizeit für denselben Artikel, liefert die API den Namen der anderen Freizeit als Konflikthinweis. Eine Reservierung kann an einem Material eines Programmpunkts hängen; das Ereignis `ReservierungGeaendert` setzt dort über einen Empfänger in Programm den Materialstatus (Spine-Zeile vorhanden).

## Boundaries & Constraints

**Always:**
- Rechte: Org-Routen `GET` mit `.NurOrgRolle(OrgAdmin, OrgUeberblick)`, schreibende Org-Routen `.NurOrgRolle(OrgAdmin)`. Freizeit-Routen `.NurFreizeitRolle(Leitung, Team)`. Rechtelisten in `InventarRechte`, per Test gleich den Listen in Organisationen bzw. Freizeiten. Neue Aktionsschlüssel `inventar-sehen` (Admin, Überblick) und `inventar-verwalten` (Admin) in `Aktionsschluessel` und `OrgRechte.Erlaubt`; die Oberfläche entscheidet nur über `Erlaubt`.
- Artikel: `bezeichnung` Pflicht, 1–120 Zeichen, getrimmt; `menge` optional, höchstens 40 Zeichen, leer = `null`. Autosave nach AD-19: `PATCH .../inventar/{artikelId}` mit `InventarArtikelFeld { Bezeichnung, Menge }`, Laden unter `FOR UPDATE`, `409 konflikt` nur bei abweichendem Altwert desselben Felds. Löschen eines Artikels mit Reservierungen → `409 konflikt` („Noch reserviert“).
- Reservierung: `von`/`bis` optional; fehlen beide, gelten erster und letzter Tag der Freizeit (`IFreizeitTageLesen`). Nur eines angegeben oder `von > bis` → `400` am Feld `bis`. Mehrere Reservierungen desselben Artikels in derselben Freizeit sind erlaubt. Kein Bearbeiten: ändern heißt aufheben und neu reservieren.
- Konflikt: Zwei Reservierungen desselben Artikels aus verschiedenen Freizeiten mit `a.Von <= b.Bis && b.Von <= a.Bis` (Tage einschließlich). Die API liefert je Reservierung die Namen der anderen Freizeiten (`IFreizeitNamenLesen`), sortiert und ohne Doppelte. Zeltlotse verhindert nichts und löst nichts.
- Schreibende Freizeit-Commands prüfen die Phase über `IFreizeitZeitpunkte`: `Gelaufen` → `409 freizeit-abgeschlossen`; GET liefert weiter. Org-Commands hängen an keiner Freizeit.
- `Reservierung` erbt von `Aggregat`. Anlegen legt `ReservierungGeaendert(ReservierungId, FreizeitId, ArtikelId, MaterialId?, Art = Reserviert)` ab, Aufheben dasselbe mit `Art = Aufgehoben` vor dem Entfernen; nur wenn `MaterialId` gesetzt ist. Ereignis nur aus `Guid`, Enums (Typregel; falls `Guid?` dort nicht erlaubt ist, `Guid.Empty` statt `null`).
- Empfänger in Programm (`ProgrammReservierungUebernehmen`, `EmpfaengtEreignis<ReservierungGeaendert, …>`): Material derselben Freizeit suchen (fehlt es, nichts tun). `Reserviert` → `Material.ReservierungZuordnen(reservierungId)`: merkt die Kennung und setzt Status `Vorhanden`. `Aufgehoben` → `Material.ReservierungLoesen(reservierungId)`: nur wenn die Kennung passt, Kennung leeren und Status `Gebraucht`. Neue Eigenschaft `ReservierungId` (Guid?) an Material und in der API-Form `Material`.
- Keine Personenbezüge in Inventar (keine `PersonId`), daher keine Pflichten aus AD-16 für Inventar. Alle Inventar-Entitäten `IOrganisationsEigen`.
- Testgetrieben (AD-30); Oberfläche nur aus Bausteinen von `Zeltlotse.Ui.Komponenten`; Texte Deutsch ohne Anrede.

**Never:** Konflikte auflösen, sperren oder Mengen verrechnen; Ansprechperson im Konflikthinweis (Umfangsgrenzen „Später“); Schäden, Verluste, Anschaffungswünsche (Story 3-4); Kisteninhalte einzeln erfassen oder Rückgabe-Check (Non-goals); Inventar schreibt in Programm oder liest Programm (nur über das Ereignis); Programm-Endpunkte, die Reservierungen anlegen; Seiten- oder untere Navigation; Spine-Änderung; neue Problemcodes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Org-Inventar laden | `GET /api/organisationen/{orgId}/inventar` | `200 OrgInventar(Artikel)`: je Artikel Id, Version, Bezeichnung, Menge und alle Reservierungen (Id, FreizeitId, Freizeit-Name, Von, Bis, Konflikte); sortiert nach Bezeichnung, Reservierungen nach Von | – |
| Artikel anlegen | `POST .../organisationen/{orgId}/inventar` `{bezeichnung, menge?}` | `201` Artikel mit Version | Bezeichnung leer/>120, Menge >40 → `400` am Feld |
| Artikel ändern | PATCH `menge` mit passendem Altwert | `200 {version, menge}` | abweichender Altwert desselben Felds → `409 konflikt`; anderes Feld geändert → angewendet |
| Artikel löschen | `DELETE .../inventar/{artikelId}` | `204` | mit Reservierungen → `409 konflikt`; unbekannt → `404` |
| Freizeit-Inventar laden | `GET /api/freizeiten/{freizeitId}/inventar` | `200 FreizeitInventar(ErsterTag, LetzterTag, Artikel)`: alle Artikel der Organisation, je Artikel nur die Reservierungen dieser Freizeit (Id, Von, Bis, MaterialId, Konflikte) | – |
| Reservieren | `POST .../freizeiten/{id}/inventar/{artikelId}/reservierungen` `{von?, bis?, materialId?}` | `201` Reservierung mit Konflikten; ohne Datum Freizeitzeitraum | nur ein Datum oder `von > bis` → `400` am Feld `bis`; Artikel unbekannt → `404` |
| Konflikt | Freizeit X hat Großzelt 02.–13.08., Freizeit Y reserviert 10.–12.08. | Antwort und beide GETs nennen bei der Reservierung den Namen der jeweils anderen Freizeit; 14.–15.08. ergibt keinen Konflikt | kein Fehler, Reservierung entsteht |
| Aufheben | `DELETE .../freizeiten/{id}/inventar/reservierungen/{reservierungId}` | `204` | unbekannt oder andere Freizeit → `404` |
| Material verknüpft | Reservieren mit `materialId`, Outbox zugestellt | Material hat `ReservierungId`, Status `Vorhanden`; nach Aufheben `ReservierungId = null`, Status `Gebraucht` | Material gelöscht oder fremde Freizeit → Empfänger tut nichts |
| Abgeschlossen | Phase `Gelaufen`, Reservieren/Aufheben | – | `409 freizeit-abgeschlossen`; GET `200` |
| Rechte Org | Org-Admin / Org-Überblick / Org-Admin einer anderen Organisation / Person ohne Org-Rolle / ohne Sitzung | GET `200`, Schreiben `2xx` / GET `200`, Schreiben `403` / `404` / `404` / `401` | – |
| Rechte Freizeit | Leitung, Team / Org-Admin ohne Freizeit-Rolle / fremde Organisation / Reservierung einer anderen Freizeit / ohne Sitzung | `2xx` / `404` / `404` / `404` / `401` | „falsche Rolle 403“ entfällt (jede Freizeit-Rolle darf), Token-Fälle entfallen (kein Token-Endpunkt) |

</intent-contract>

## Code Map

- `src/Modules/Inventar/Zeltlotse.Inventar/{InventarModul.cs,Daten/InventarDbContext.cs,Zeltlotse.Inventar.csproj}` -- leeres Gerüst; `MapInventarEndpoints` (Zeile 21) ist ein Stub, `InventarModell.Konfigurieren` leer, Design-Zeit-Fabrik vorhanden, Migration `Initial` leer. csproj braucht Referenzen auf `Zeltlotse.Api.Contracts`, `Freizeiten.Contracts`, `Organisationen.Contracts` (Vorlage `Programm.csproj` 12-13). `InternalsVisibleTo` für `Zeltlotse.Inventar.Tests` steht schon. Host-Registrierung fertig (`ZeltlotseModule.cs:27,48`, `Program.cs:13,59`, `ModulMigrationen.cs:15`).
- `src/Modules/Inventar/Zeltlotse.Inventar.Contracts/Events/EventsNamensraum.cs` -- Ort für `ReservierungGeaendert` und Enum `ReservierungArt { Reserviert = 1, Aufgehoben = 2 }`. Spine-Zeile 567 existiert schon; `EreignisFlussTests` verlangt nur, dass Code-Paare in der Tabelle stehen. `TypRegelnTests.Ereignis_Typen_bestehen_nur_aus_…` prüft die Feldtypen.
- `src/Zeltlotse.SharedKernel/Aggregat.cs`, `Daten/ModulDbContext.cs:37-60` -- Outbox sammelt Ereignisse aller verfolgten Aggregate, auch gelöschter (`ChangeTracker.Entries<Aggregat>()`). Vorlage für Ereignis-Records: `Freizeiten.Contracts/Events/FreizeitRolleGeaendert.cs`.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/{OrganisationenModul.cs:62-74,Rollen/OrgRechte.cs,Dokumente/OrgDokumente.cs}` -- Org-Gruppe `MapGroup($"/{{{Rechtemerkmale.OrgRouteWert}:guid}}")`, `.NurOrgRolle(...)`, `OrgRechte.Lesen`, `OrgRechte.Erlaubt` (hier die neuen Schlüssel ergänzen); Org-PATCH-Vorlage `Nachweise/NachweisEndpunkte.cs` (`RichtlinieFeldAendernEndpunkt`). `RechtePruefung` (`src/Zeltlotse.Api/Rechte/RechtePruefung.cs`) setzt den Mandanten aus `orgId` bzw. aus der Organisation der Freizeit: fremde Org/keine Rolle `404`, falsche Org-Rolle `403`.
- `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs` (bis Zeile 109) -- `Aktionsschluessel`-Konstanten `InventarSehen`, `InventarVerwalten` additiv.
- `src/Modules/Programm/Zeltlotse.Programm/{ProgrammModul.cs:36-50,Materialien/*,Programme/FeldPatch.cs:28,Programme/WochenrasterLaden.cs:69 (ProgrammProbleme),Daten/ProgrammDbContext.cs:25 (MaterialSperrenAsync)}` -- Muster für Command/Validator/Handler/Endpunkt, Phase-Prüfung, PATCH-Leser, Zeilensperre, Problem-Antworten. `FeldPatch` liegt in Programm; für Inventar eine eigene kleine Kopie im Modul (Module teilen keinen Code außer SharedKernel).
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/Material.cs` + `Datenschutz/ProgrammDatenschutz.cs` (Empfänger-Vorlage `ProgrammPersonLoeschen`) -- `ReservierungId`, Methoden `ReservierungZuordnen`/`ReservierungLoesen`, neuer Empfänger; Programm.csproj bekommt Referenz auf `Inventar.Contracts` (Pfeil Programm → Inventar erlaubt, `Loesung.cs:25-42`). Neue Migration `MaterialReservierung` in Programm.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/{Zeitpunkte/IFreizeitTageLesen.cs,Zeitpunkte/IFreizeitZeitpunkte.cs,Namen/IFreizeitNamenLesen.cs}` -- Tage, Phase, Namen; alle unter dem Mandantenfilter, auf Org- und Freizeit-Routen nutzbar.
- `tests/Zeltlotse.Programm.Tests/` -- Vorlage für neues Testprojekt `tests/Zeltlotse.Inventar.Tests` (csproj, `AssemblyInfo.cs` mit `PostgresContainer`, Eintrag in `Zeltlotse.slnx` unter `/tests/`).
- `tests/Zeltlotse.Api.Tests/{OrgDokumenteTests.cs (Org-Rechte-Muster, Org-Überblick per OrgHelfer.EinladenAsync/AnnehmenAlsAsync),MaterialTests.cs,FreizeitpassSzenario.cs,EndpunktListeTests.cs (freizeitRouten-Liste, Assert.Equal(23, mitOrg.Count)),DemoDatenTests.cs (~400-414),FreizeitKontextTests.cs/Org-Kontext-Tests für Erlaubt}` -- Testmuster und Listen.
- `tests/Zeltlotse.Ablaeufe.Tests/NachweisEingesehenTests.cs` -- Vorlage Durchlauf-Test mit sofort abgearbeiteter Outbox.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Api/OrganisationenApiClient.cs,UiRegistrierung.cs,Texte/Anzeige.cs}` -- Client-Muster (GET null bei 403/404, Aktion-Ergebnis Erfolg/Konflikt/Abgeschlossen/Fehler, Feldfehler, statische Pfad-Builder für AutosaveFeld).
- `src/Zeltlotse.App/Features/Organisationen/{OrganisationAnsicht.razor:43-65,OrgDokumente.razor}` -- Card-Zugang mit `Darf(...)`, Listenseite mit `NichtVerfuegbar`, `Datentabelle`, `LeerZustand`, `InlineBestaetigung`.
- `src/Zeltlotse.App/Features/{Freizeiten/FreizeitStart.razor:148-162,Programm/ProgrammpunktSeite.razor:86,Programm/MaterialEintrag.razor,Programm/ProgrammTexte.cs}` -- Knopf-Zugang, Card „Material“.
- `src/Zeltlotse.Ui.Komponenten/` -- `Meldung` (`MeldungArt.Warnung`), `Status` (`StatusArt.Warnung`), `Card`, `Feld`, `Formular`, `Auswahl`, `Knopf`, `InlineBestaetigung`, `LeerZustand`, `Datentabelle`; kein neuer Baustein nötig.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs (EinspielenAsync 103-142, MaterialAsync 807)}` -- Demo nur mit einer Freizeit; Org-Admin Andrea, Leitung Jonas/Miriam.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Org_Dokumente` ~1189, Material ~1615-1660, `BildAsync` ~1860), `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:18-26`, `doku/src/content/docs/{org-admin/*,mitarbeitende/material.md,leitung/index.md}` -- Sidebar wird je Ordner automatisch erzeugt.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Inventar/Inventar.cs` -- `OrgInventar(IReadOnlyList<InventarArtikel> Artikel)`; `InventarArtikel(Id, Version, Bezeichnung, Menge?, IReadOnlyList<Reservierung> Reservierungen)`; `Reservierung(Id, FreizeitId, Freizeit, Von, Bis, MaterialId?, IReadOnlyList<string> Konflikte)`; `FreizeitInventar(ErsterTag, LetzterTag, IReadOnlyList<InventarArtikel> Artikel)` (Reservierungen nur der eigenen Freizeit); `InventarArtikelNeu(Bezeichnung?, Menge?)`; `ReservierungNeu(DateOnly? Von, DateOnly? Bis, Guid? MaterialId)`; enum `InventarArtikelFeld { Bezeichnung, Menge }`; `InventarFelder` (JSON-Namen, Grenzen 120/40, `JsonName`) -- API-Datenformen; dazu Aktionsschlüssel in `Gemeinsam/FreizeitKontext.cs` und `Programm/Material.cs` um `Guid? ReservierungId` additiv.
- `tests/Zeltlotse.Inventar.Tests/` (neu, in `Zeltlotse.slnx`; zuerst) + `src/Modules/Inventar/Zeltlotse.Inventar/{Artikel/InventarArtikel.cs,Reservierungen/Reservierung.cs}` -- Entitäten (`IOrganisationsEigen`, `uint Version` als xmin am Artikel), `FeldAendern`/Trimmen, `Reservierung.Anlegen` und `Aufheben` legen Ereignisse nur mit Material ab, Überschneidungsregel als statische Methode; Tests für Entitäten, Validatoren, Konfliktregel (Grenztage), Mandantenfilter beider Entitäten, Rechtelisten gleich `OrgRechte`/`FreizeitRechte`.
- `src/Modules/Inventar/Zeltlotse.Inventar/Daten/InventarDbContext.cs` + Migration `InventarUndReservierung` -- Tabellen `inventar_artikel`, `reservierung` (FK auf Artikel im eigenen Schema ohne Cascade), Indizes (OrganisationId, ArtikelId, FreizeitId), `ArtikelSperrenAsync` (`FOR UPDATE`).
- `tests/Zeltlotse.Api.Tests/InventarTests.cs` (zuerst) -- jede Zeile der I/O-Matrix, Rechte-Pflichtfälle für alle sieben Routen, Konflikt zwischen zwei Freizeiten derselben Organisation (zweite Freizeit im Test anlegen) inkl. Grenztag, gleichzeitige PATCHes auf Bezeichnung und Menge (beide gelingen); `EndpunktListeTests.cs` um die drei Freizeit-Routen ergänzen und die Org-Zählung um vier erhöhen; Org-Kontext-Test für `inventar-sehen`/`inventar-verwalten`.
- `src/Modules/Inventar/Zeltlotse.Inventar/{InventarRechte.cs,FeldPatch.cs,InventarProbleme.cs,Artikel/{OrgInventarLaden,ArtikelAnlegen,ArtikelBearbeiten,ArtikelLoeschen}.cs,Reservierungen/{FreizeitInventarLaden,Reservieren,ReservierungAufheben,Konflikte}.cs}` + `InventarModul.cs` -- Slices und Routen: Org `GET inventar`, `POST inventar`, `PATCH inventar/{artikelId:guid}`, `DELETE inventar/{artikelId:guid}`; Freizeit `GET inventar`, `POST inventar/{artikelId:guid}/reservierungen`, `DELETE inventar/reservierungen/{reservierungId:guid}`. `Konflikte` ist eine Abfrage-Klasse, die beide GETs und die POST-Antwort nutzen.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` -- `Erlaubt` um die zwei Schlüssel.
- `src/Modules/Inventar/Zeltlotse.Inventar.Contracts/Events/ReservierungGeaendert.cs` + `src/Modules/Programm/Zeltlotse.Programm/{Materialien/Material.cs,Materialien/ReservierungUebernehmen.cs,ProgrammModul.cs}` + Migration + `tests/Zeltlotse.Programm.Tests/MaterialTests.cs` -- Ereignis, Material-Methoden, Empfänger und Registrierung; Modultests für Zuordnen/Lösen (falsche Kennung ändert nichts).
- `tests/Zeltlotse.Ablaeufe.Tests/ReservierungGeaendertTests.cs` -- Durchlauf: Material anlegen, mit `materialId` reservieren, Outbox abarbeiten → `Vorhanden` + Kennung; aufheben, abarbeiten → `Gebraucht`; Inbox genau einmal.
- `src/Zeltlotse.Ui/{Api/InventarApiClient.cs,UiRegistrierung.cs,Texte/Anzeige.cs}` -- Client: `OrgInventarAsync`, `FreizeitInventarAsync` (null bei 403/404), `ArtikelAnlegenAsync` (Feldfehler), `ArtikelLoeschenAsync`, `ReservierenAsync` (Feldfehler), `AufhebenAsync`, `ArtikelPfad`; `Anzeige.Zeitraum(von, bis)` → „02.08.–13.08.2027“, `Anzeige.Konflikt(namen)` → „Zur gleichen Zeit auch für X reserviert. Absprache mit der anderen Leitung nötig.“ (mehrere Namen mit „und“ verbunden).
- `src/Zeltlotse.App/Features/Inventar/{OrgInventar.razor,FreizeitInventar.razor,ReservierungZeile.razor}` + `Organisationen/OrganisationAnsicht.razor` + `Freizeiten/FreizeitStart.razor` + `Programm/{MaterialEintrag.razor,ProgrammpunktSeite.razor}` + `tests/Zeltlotse.Ui.Tests/InventarSeitenTests.cs` -- Org-Seite `/organisationen/{OrgId}/inventar` (Card „Inventar“ mit Knopf „Inventar öffnen“ bei `inventar-sehen`): H1 „Inventar“, je Artikel eine Card (Bezeichnung und Menge als AutosaveFeld bei `inventar-verwalten`, sonst Text; Reservierungen als Liste „Freizeit · Zeitraum“, bei Konflikt Status Warnung „Konflikt“), Formular „Artikel hinzufügen“ und Löschen per InlineBestaetigung nur bei `inventar-verwalten`; leer → LeerZustand „Noch kein Inventar.“. Freizeit-Seite `/freizeiten/{id}/inventar` (Knopf „Inventar“ auf der Startseite): Dachzeile „Planen“, H1 „Inventar“, je Artikel Card mit eigenen Reservierungen (Zeitraum, Knopf „Reservierung aufheben“), Konflikt als `Meldung` Warnung am Artikel, Formular „Reservieren“ mit Von/Bis (vorbelegt mit den Freizeittagen); leer → „Die Organisation hat noch kein Inventar.“. Programmpunkt: im MaterialEintrag bei verknüpfter Reservierung „Aus dem Inventar: Artikel“ und ggf. Konflikt-Meldung; ohne Verknüpfung Auswahl „Aus dem Inventar“ + Knopf „Reservieren“ (Freizeitzeitraum, `materialId`), danach Material und Inventar neu laden. In Phase Gelaufen nur lesbar.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- zweite Freizeit „Kinderbibeltage 2027“ (10.–12.08.2027, Leitung neue Person „Tim Kröger“, `tim.kroeger@example.org`, keine Anmeldungen); Schritt `InventarAsync` nach `MaterialAsync`, idempotent nach Bezeichnung: Artikel „Fahnen“ (8 Stück), „Großzelt“ (1 Stück), „Kanus“ (6 Stück), „Gaskocher“ (2 Stück); Zeltlager reserviert Fahnen (02.–13.08., verknüpft mit Material „Fahnen“ am Geländespiel) und Großzelt (02.–13.08.); Kinderbibeltage reservieren Großzelt (10.–12.08.) → Konflikt.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `Org_Inventar` (Andrea, Organisation → „Inventar öffnen“) und `Inventar_Reservieren` (Jonas, Freizeit → „Inventar“, Konflikt sichtbar), je Handy und Laptop mit `AxeAsync` und `BildAsync` → `org-inventar-*.png`, `inventar-*.png`; bestehende Bilder, die sich durch den neuen Knopf, die zweite Freizeit oder „Aus dem Inventar“ ändern, neu erzeugen.
- `doku/src/content/docs/org-admin/inventar.md` (neu, „Inventar pflegen“, auch für Org-Überblick) + `mitarbeitende/inventar.md` (neu, „Material aus dem Inventar reservieren“, Konflikthinweis, Absprache außerhalb von Zeltlotse) + Links in `org-admin/index.md`, `mitarbeitende/index.md`, `mitarbeitende/material.md`, `leitung/index.md`; `DokuRegeln.cs` PflichtSeiten -- je Seite zwei Screenshots.

**Acceptance Criteria:**
- Given Andrea als Org-Admin, when sie „Fahnen“ mit Menge „8 Stück“ hinzufügt, then steht der Artikel im Inventar der Organisation und erscheint ohne weitere Eingabe auf der Inventar-Seite der Freizeit.
- Given Großzelt ist für das Zeltlager 02.–13.08. reserviert, when die Kinderbibeltage es für 10.–12.08. reservieren, then zeigt die Inventar-Seite des Zeltlagers „Zur gleichen Zeit auch für Kinderbibeltage 2027 reserviert. Absprache mit der anderen Leitung nötig.“ und beide Reservierungen bleiben bestehen.
- Given das Material „Seile“ am Geländespiel, when Jonas dort „Aus dem Inventar“ einen Artikel reserviert, then steht nach dem Neuladen „Aus dem Inventar: …“ und der Status „vorhanden“.
- Given ein Org-Überblick-Konto, when es die Organisation öffnet, then sieht es das Inventar mit Reservierungen, aber kein Formular und keinen Löschen-Knopf.
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur-, Durchlauf- und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und die vier neuen Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 34 findings — high 0, medium 5, low 24, false 5, maybe-false 0
- findings:
  - `[medium]` `[patch]` (Verification gap) Zeilensperren beim doppelten Aufheben und bei Löschen gegen Reservieren ohne Gleichzeitigkeitstest — zwei Tests mit `Task.WhenAll` ergänzt (genau 204 + 404 bzw. nie 500).
  - `[low]` `[reject]` (Verification gap) Sortierung mehrerer Konfliktnamen ungetestet — rein kosmetisch; Sortierung steht im Code, ein Fehler zeigt nur eine andere Reihenfolge.
  - `[low]` `[reject]` (Verification gap, other) Zweite Reservierung für dasselbe Material überschreibt die Kennung, Aufheben der neueren setzt „gebraucht“ — die Oberfläche bietet „Reservieren“ nur ohne verknüpfte Reservierung an; erreichbar nur per Hand-Anfrage oder zwei gleichzeitige Tipps; ein Wächter wäre zusätzliche Regel.
  - `[false]` `[reject]` (Intent) 403 nur auf Org-Routen, keine Token-Fälle — Matrix der Intent schließt 403 auf Freizeit-Routen (jede Freizeit-Rolle darf) und Token-Fälle (kein Token-Endpunkt) aus.
  - `[false]` `[reject]` (Intent) Test zuerst im Diff nicht belegbar — beschreibend, kein Fehler.
  - `[low]` `[reject]` (Intent) Playwright-Tests lesen nur — Schreibpfade per HTTP, Durchlauf und bUnit; Doku-Screenshots lesen bewusst den Demo-Stand (wie 3-1/3-2).
  - `[low]` `[reject]` (Intent) Programmpunkt-Fläche nur per bUnit — `material-programmpunkt-*` sind neu erzeugt und zeigen „Aus dem Inventar“ an den Demo-Fahnen; Doku `material.md` beschreibt es.
  - `[low]` `[reject]` (Intent) Akzeptanzkriterium „Seile“ nicht als ein Test — die Teile sind abgedeckt (HTTP mit `materialId`, Durchlauf-Test, bUnit „Aus dem Inventar“).
  - `[false]` `[reject]` (Intent) Doku-Build nicht belegt — `npm run build` lief fehlerfrei (47 Seiten).
  - `[false]` `[reject]` (Intent) Fremde Screenshots geändert — per Pixelvergleich geprüft: Rauschen zurückgesetzt; behalten nur Bilder mit neuem Knopf/Card bzw. zweiter Freizeit.
  - `[medium]` `[patch]` (Blind) Artikel mit Reservierungen abgeschlossener Freizeiten nie löschbar — Inventar bekommt einen Lösch-Lauf, der Reservierungen der Freizeiten mit abgelaufenen Daten löscht (Muster `ProgrammLoeschlauf`); bis zur Org-Frist bleibt die Sperre (Restrisiko).
  - `[medium]` `[patch]` (Blind) Reservierungen gelöschter Freizeiten zeigen leeren Namen — gleiche Ursache, gleicher Lösch-Lauf.
  - `[low]` `[reject]` (Blind) `MaterialId` ungeprüft, nach Löschen des Materials verwaist — AD-2 erlaubt Inventar → Programm nicht; Design Notes: Empfänger ignoriert unbekannte Kennungen; Abhilfe bräuchte ein neues Ereignis.
  - `[low]` `[reject]` (Blind) Mehrere Reservierungen je Material — wie die Verification-gap-Zeile oben.
  - `[low]` `[reject]` (Blind) Ereignis überschreibt von Hand gesetzten Status — so in der Intent festgelegt (Reserviert → vorhanden, Aufgehoben → gebraucht); späte Zustellung liegt im Sekundenbereich.
  - `[low]` `[patch]` (Blind) Doku widerspricht sich („ändert den Zeitraum“) — Satz in `mitarbeitende/inventar.md` korrigiert.
  - `[low]` `[patch]` (Blind) `material.md` „Unter jedem Material“ ungenau — Bedingungen in einem Satz ergänzt.
  - `[low]` `[patch]` (Blind) „Reservierung aufheben“ ohne Bestätigung, verknüpftes Material fällt still auf „gebraucht“ — Inline-Bestätigung nach EXPERIENCE.md (Löschen) ergänzt; Anzeige des Materials in der Zeile abgelehnt (bräuchte Programm-Daten auf der Seite).
  - `[low]` `[reject]` (Blind) Von/Bis nicht gegen die Freizeittage geprüft — gewollt (Aufbau vor der Freizeit); die Intent setzt nur `von <= bis`.
  - `[low]` `[reject]` (Blind) Doppelte Bezeichnungen, Demo-Einspielung wirft — Demo läuft frisch (gleiche Abwägung wie 3-1/3-2); doppelte Namen sind nur kosmetisch.
  - `[medium]` `[patch]` (Blind) Gleichzeitigkeitsbehauptungen ungetestet — gleiche Ursache wie die erste Zeile, dort behoben.
  - `[low]` `[reject]` (Blind) `Material.ReservierungId` in der Oberfläche ungenutzt, optionaler Positionsparameter — additive API-Änderung (AD-5), harmlos.
  - `[low]` `[reject]` (Blind) „Artikel löschen“ ohne Übertragungs-Wächter — carried-Abwägung aus 3-2 (Doppeltipp selten, Meldung verschwindet mit dem Eintrag).
  - `[low]` `[reject]` (Blind) Veraltete Einträge nach 404 — carried-Abwägung aus 3-1/3-2 (seltener Gleichzeitigkeitsfall, Neuladen behebt es).
  - `[low]` `[reject]` (Edge) Zweite Reservierung je Material — wie oben.
  - `[low]` `[reject]` (Edge) Status-Überschreiben durch das Ereignis — wie oben, Intent-Regel.
  - `[low]` `[reject]` (Edge) `SingleAsync` nach dem Speichern kann bei gleichzeitigem Aufheben werfen — Zeitfenster von Millisekunden zwischen Commit und Lesen; Abhilfe wäre zusätzlicher Zweig.
  - `[low]` `[reject]` (Edge) Beliebig großer Zeitraum erzeugt Konflikte mit allen — wie Von/Bis oben; Zeltlotse löst Konflikte bewusst nicht.
  - `[low]` `[reject]` (Edge) `MaterialId` fremd oder gelöscht — wie oben (AD-2).
  - `[medium]` `[patch]` (Edge) Leerer Name bei gelöschter Freizeit — gleicher Lösch-Lauf wie oben.
  - `[low]` `[reject]` (Edge) Demo-Einspielung bei doppelten Bezeichnungen — wie oben.
  - `[false]` `[reject]` (Edge) Leere Datumsfelder reservieren still die ganze Freizeit — genau das legt die Intent fest („fehlen beide, gelten erster und letzter Tag“); `input type=date` liefert keinen unlesbaren Text.
  - `[low]` `[reject]` (Edge) Status direkt nach dem Reservieren noch „gebraucht“ — in den Design Notes beschrieben (Outbox im Hintergrund); die Card zeigt „Aus dem Inventar“ sofort.
  - `[low]` `[reject]` (Edge) `Anzeige.ZeitraumKurz` statt `Anzeige.Zeitraum` — der Name `Zeitraum` ist schon mit der Langform auf fünf Seiten belegt; Umbenennen würde sie ändern.

## Design Notes

- **Material-Verknüpfung über das Ereignis:** Die Reservierung gehört Inventar, der Materialstatus Programm (AD-3). Der Knopf am Material schickt die Reservierung an Inventar mit `materialId`; Programm reagiert nur auf `ReservierungGeaendert` (Spine-Zeile 567, AD-4). Inventar kann `materialId` nicht prüfen (kein Pfeil Inventar → Programm); der Empfänger ignoriert unbekannte oder fremde Kennungen. Da die Outbox im Hintergrund zustellt, kann der Status direkt nach dem Reservieren noch alt sein; die Card zeigt „Aus dem Inventar“ aus der Inventar-Antwort.
- **Statusregel:** Reserviert → `Vorhanden` (Stück liegt im Lager bereit), Aufgehoben → `Gebraucht`; keine weiteren Fallunterscheidungen (Leitprinzip simpel). Danach setzt das Team den Status wie gewohnt.
- **Kein Bearbeiten der Reservierung, mehrere je Freizeit erlaubt:** Ein PATCH auf Von/Bis bräuchte eine Regel über zwei Felder; aufheben und neu reservieren ist einfacher. Eine Eindeutigkeit (Artikel, Freizeit) würde die Verknüpfung mit Material blockieren, wenn der Artikel schon reserviert ist.
- **Artikel mit Reservierungen nicht löschbar:** Sonst verschwänden Reservierungen still und verknüpftes Material bliebe „vorhanden“.
- **Zweite Demo-Freizeit mit eigener Leitung:** Jonas und Miriam bleiben bei genau einer Freizeit, damit ihre Startseiten und bestehenden Screenshots gleich bleiben.
- **403 auf Freizeit-Routen entfällt** wie in 3-1/3-2; auf Org-Routen gibt es ihn (Org-Überblick schreibt).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Inventar.Tests`, `tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Inventar/Material/EndpunktListe/Demo/Kontext), `tests/Zeltlotse.Ablaeufe.Tests` (Filter Reservierung), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. `tests/Zeltlotse.E2E.Tests`) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Modul Inventar mit `InventarArtikel` (Bezeichnung, Menge als Freitext) und `Reservierung` (Artikel, Freizeit, Von, Bis, optional Material; Aggregat). Sieben Endpunkte: Org-Inventar lesen (Org-Admin, Org-Überblick), Artikel anlegen, Autosave-PATCH, löschen (nur Org-Admin; mit Reservierungen `409`); Freizeit-Inventar lesen, reservieren (ohne Datum der Freizeitzeitraum), aufheben (Leitung, Team; `Gelaufen` → `409`). Konflikthinweis mit den Namen der anderen Freizeiten bei überschneidenden Tagen, ohne Sperre. Ereignis `ReservierungGeaendert` mit Empfänger in Programm (`ReservierungId` am Material, Status vorhanden/gebraucht). Lösch-Lauf für Reservierungen von Freizeiten mit abgelaufenen Daten. Aktionsschlüssel `inventar-sehen`, `inventar-verwalten`. Web-App: Org-Seite „Inventar“, Freizeit-Seite „Inventar“ mit Reservieren, Aufheben (Inline-Bestätigung) und Konflikt-Meldung, „Aus dem Inventar“ am Material. Demo: zweite Freizeit „Kinderbibeltage 2027“ (Leitung Tim Kröger), vier Artikel, drei Reservierungen mit Großzelt-Konflikt. Doku `org-admin/inventar.md`, `mitarbeitende/inventar.md`, vier neue Screenshots. Test-Container mit 1 GB Shared Memory (sonst „could not resize shared memory segment“ bei vielen parallelen Tests).

**Dateien (Auswahl):**
- `src/Modules/Inventar/Zeltlotse.Inventar/**` -- Entitäten, Slices, Konflikte, Rechte, Lösch-Lauf, Migration `InventarUndReservierung`.
- `src/Modules/Inventar/Zeltlotse.Inventar.Contracts/Events/ReservierungGeaendert.cs` -- Ereignis.
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/{Material.cs,ReservierungUebernehmen.cs}`, Migration `MaterialReservierung` -- Empfänger, Materialstatus.
- `src/Zeltlotse.Api.Contracts/{Inventar/Inventar.cs,Gemeinsam/FreizeitKontext.cs,Programm/Material.cs}`, `Organisationen/.../OrgRechte.cs` -- Datenformen, Aktionsschlüssel.
- `src/Zeltlotse.Ui/{Api/InventarApiClient.cs,Texte/Anzeige.cs,UiRegistrierung.cs}` -- Client, Texte (`ZeitraumKurz`, `Konflikt`).
- `src/Zeltlotse.App/Features/Inventar/*`, `OrganisationAnsicht.razor`, `FreizeitStart.razor`, `MaterialEintrag.razor`, `ProgrammpunktSeite.razor` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/*` -- Demo-Schritt `InventarAsync`, zweite Freizeit.
- `tests/Zeltlotse.Inventar.Tests/*` (neu), `tests/Zeltlotse.Api.Tests/InventarTests.cs`, `tests/Zeltlotse.Ablaeufe.Tests/ReservierungGeaendertTests.cs`, `tests/Zeltlotse.Ui.Tests/InventarSeitenTests.cs`, ergänzt Endpunktliste, Demo, Rechte, Einladung, Doku-Regeln, Screenshots; `tests/Zeltlotse.Testing/PostgresContainer.cs` (Shared Memory).
- `doku/src/content/docs/{org-admin,mitarbeitende}/inventar.md` (neu), Links in `org-admin/index.md`, `mitarbeitende/{index,material}.md`, `leitung/index.md`; Screenshots `org-inventar-*`, `inventar-*` (neu), `material-programmpunkt-*`, `nachweis-richtlinie-*`, `loeschfristen-laptop`, `schutzkonzept-hinweis-*`, `freizeitpass-erinnern-laptop` (neue Card/Knopf bzw. zweite Freizeit).

**Review:** 34 Befunde (high 0, medium 5, low 24, false 5). Gepatcht: 2 Einträge medium (Lösch-Lauf gegen verwaiste Reservierungen und unlöschbare Artikel – drei Befunde, eine Ursache; Gleichzeitigkeitstests für die Zeilensperren – zwei Befunde) und 3 low (Doku-Widerspruch, Bedingungen von „Aus dem Inventar“ in `material.md`, Inline-Bestätigung beim Aufheben). Zurückgestellt: keine. Abgelehnt: 29, Gründe im Review Triage Log (u. a. mehrere Reservierungen je Material, Status-Regel laut Intent, Von/Bis außerhalb der Freizeit gewollt, 403/Token-Fälle laut Matrix, carried-Abwägungen aus 3-1/3-2).

**Folge-Review empfohlen:** ja (zwei medium-Einträge gepatcht). Ungeprüftes Restrisiko: Der neue `InventarLoeschlauf` und die Inline-Bestätigung beim Aufheben sind nur durch Tests, nicht durch eine zweite Review-Runde abgesichert.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI, E2E/Doku-Screenshots) vor dem Review 2144/2144 grün, nach den Patches 2147/2147 grün.
- `npm run build` in `doku/` -- fehlerfrei, 47 Seiten.
- Matrix-Audit: jede Zeile der I/O-Matrix hat mindestens einen gelaufenen, grünen Test.
- Screenshots per Pixelvergleich geprüft: Rauschen (Datum, Testreihenfolge bei `freizeitpass-fragen-laptop`, Token) zurückgesetzt, echte Änderungen behalten.

**Restrisiken:**
- Artikel mit Reservierungen abgeschlossener Freizeiten bleiben bis zum Ablauf der Org-Frist für Freizeit-Daten unlöschbar (Intent: Löschen mit Reservierungen → `409`; Aufheben nach Abschluss → `409`).
- Mehrere Reservierungen für dasselbe Material (nur per Hand-Anfrage oder gleichzeitigen Tipps) können den Materialstatus nach dem Aufheben der neueren auf „gebraucht“ setzen, obwohl die ältere besteht.
- Der Materialstatus folgt der Reservierung erst nach Zustellung der Outbox (Sekunden).
- Wird ein Material gelöscht, behält seine Reservierung die verwaiste `MaterialId` (kein Ereignis von Programm an Inventar, AD-2).
