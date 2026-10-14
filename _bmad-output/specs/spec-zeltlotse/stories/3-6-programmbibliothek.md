---
title: 'Story 3-6: Programmbibliothek'
type: 'feature'
created: '2026-10-14'
status: 'done'
baseline_revision: 'b33f096d45265610993cf24ef214535c0f2789ec'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-5-aufgaben-und-aufgaben-vorlagen.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Gelungene Programmpunkte gehen nach einer Freizeit verloren; es gibt keine Programmbibliothek, in der das Team sie bewusst ablegt und samt Material und Aufgaben in andere Freizeiten der Organisation übernimmt (CAP-24, FR-41, NS-5 Schritt 6).

**Approach:** Programm bekommt die organisationseigene Entität `BibliotheksEintrag` (Titel, abgelegt am, Material-Liste aus Bezeichnung/Menge, Aufgaben-Liste aus Titel/Phase – eine Momentaufnahme ohne Personenbezug). Leitung und Team legen einen Programmpunkt per Knopf ab und übernehmen Einträge in ihre Freizeit (neuer Programmpunkt mit neuem Material im Status „gebraucht“ und neuen, freien Aufgaben); der Org-Admin sieht die Bibliothek seiner Organisation und entfernt Einträge. Web-App: Card „Programmbibliothek“ am Programmpunkt, Seite „Programmbibliothek“ der Freizeit (Link neben „Materialliste“ im Wochenraster), Org-Seite „Programmbibliothek“.

## Boundaries & Constraints

**Always:**
- Datenformen in `src/Zeltlotse.Api.Contracts/Programm/Bibliothek.cs` (additiv): `BibliotheksEintrag(Guid Id, string Titel, DateTimeOffset AbgelegtAm, IReadOnlyList<BibliotheksMaterial> Material, IReadOnlyList<BibliotheksAufgabe> Aufgaben)`, `BibliotheksMaterial(string Bezeichnung, string? Menge)`, `BibliotheksAufgabe(string Titel, AufgabenPhase Phase)`, `BibliotheksUebernahme(DateOnly? Tag, string? Beginn)`. `Programmpunkt` (Contracts/Programm.cs) bekommt am Ende `bool InBibliothek = false` (additiv). Aktionsschlüssel `ProgrammbibliothekVerwalten = "programmbibliothek-verwalten"` in `Gemeinsam/FreizeitKontext.cs`.
- Endpunkte (Modul Programm):

  | # | Route | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `POST /api/freizeiten/{freizeitId}/programm/punkte/{punktId}/bibliothek` | `ProgrammRechte.Mitarbeit` | `200 Programmpunkt` mit `InBibliothek = true`: legt einen Eintrag in der Organisation der Freizeit an (Titel des Punkts, `AbgelegtAm` = jetzt, Material des Punkts nach Bezeichnung, Aufgaben mit dieser `ProgrammpunktId` nach Phase, Titel) und merkt `BibliotheksEintragId` am Punkt; ist der Punkt schon mit einem noch vorhandenen Eintrag verknüpft, nichts Neues (Zeilensperre am Punkt); Mahlzeit → `409 konflikt` |
  | 2 | `GET /api/freizeiten/{freizeitId}/programm/bibliothek` | Mitarbeit | `200 IReadOnlyList<BibliotheksEintrag>` der Organisation der Freizeit, sortiert nach Titel, dann `AbgelegtAm` absteigend, dann Id; auch in Phase Gelaufen |
  | 3 | `POST …/programm/bibliothek/{eintragId}/uebernehmen` (`BibliotheksUebernahme`) | Mitarbeit | `201 Programmpunkt` (Location `…/programm`): neuer Punkt mit Titel des Eintrags am Tag/Beginn, je Material ein neues Material (Status Gebraucht, ohne Mitbringen, ohne Reservierung), je Aufgabe eine neue Aufgabe am Punkt (frei, offen, ohne Vorlage); der neue Punkt ist **nicht** mit dem Eintrag verknüpft (`InBibliothek = false`) |
  | 4 | `GET /api/organisationen/{orgId}/programmbibliothek` | `ProgrammRechte.Bibliothek` = `[OrgAdmin]` | `200 IReadOnlyList<BibliotheksEintrag>`, Sortierung wie #2 |
  | 5 | `DELETE /api/organisationen/{orgId}/programmbibliothek/{eintragId}` | Bibliothek | `204`; verknüpfte Programmpunkte bleiben, ihr `InBibliothek` wird `false` (FK `ON DELETE SET NULL`) |
- Validierung #3 wie `ProgrammpunktAnlegen`: `tag` Pflicht und ein Tag der Freizeit, `beginn` leer oder `HH:mm` → sonst `400` am Feld (`tag`, `beginn`).
- Unbekannte Kennung oder Kennung einer anderen Freizeit/Organisation → `404` (Mandantenfilter: Einträge sind nur in der eigenen Organisation sichtbar). Schreibende Freizeit-Commands (#1, #3) prüfen die Phase über `IFreizeitZeitpunkte`: `Gelaufen` → `409 freizeit-abgeschlossen`.
- Entität `BibliotheksEintrag` (Tabelle `bibliotheks_eintrag`, `IOrganisationsEigen`, Index auf OrganisationId, Material und Aufgaben als JSON-Spalten über `OwnsMany(…).ToJson()`), unveränderlich (kein PATCH, keine Version). `Programmpunkt.BibliotheksEintragId` (Guid?, FK SET NULL, Index), Methode `BibliothekMerken`. `Material`/`Aufgabe`: Fabrikmethoden für die Übernahme vorhandene `Anlegen` nutzen. Migration `Programmbibliothek`.
- AD-16: Einträge haben keinen Personenbezug und sind Org-Daten; der Lösch-Lauf für Freizeit-Daten lässt sie stehen, Datenauskunft und `ProgrammPersonLoeschen` bleiben unverändert.
- Rechte: `ProgrammRechte.Bibliothek` gleich `OrgRechte.Programmbibliothek` (neu, `[OrgAdmin]`), daraus `Erlaubt` → `programmbibliothek-verwalten` (am Ende der Org-Admin-Liste); ein Test hält beide gleich.
- Testgetrieben (AD-30). Pflichtfälle: Freizeit-Routen Leitung und Team `2xx`, Org-Admin ohne Freizeit-Rolle `404`, fremde Organisation `404`, Punkt/Eintrag einer anderen Freizeit/Organisation `404`, ohne Sitzung `401`. Org-Routen: Org-Admin `2xx`, Org-Überblick `403`, Org-Admin fremder Organisation `404`, Person ohne Org-Rolle `404`, ohne Sitzung `401`. Token-Fälle entfallen (kein Token-Endpunkt).
- Oberfläche nur aus `Zeltlotse.Ui.Komponenten`, Rechte nur über `Erlaubt` bzw. Erfolg der Abrufe; Texte Deutsch ohne Anrede; in Phase Gelaufen nur lesbar.

**Never:** Automatisches Ablegen (FR-41); ganze Freizeit kopieren; Bearbeiten von Einträgen (PATCH) oder Versionen eines Eintrags; Übernahme von Personen (übernommen von, bringt mit), Status, Erledigt, Reservierungen oder Dokumenten; Lesen über Organisationen hinweg; neues Ereignis, neues Modul, Spine-Änderung; neue Problemcodes; Seiten- oder untere Navigation; Pop-ups; Commit auf `main`, Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Ablegen | Tobias: #1 auf „Geländespiel“ in X (Material Seile „4 × 10 m“ eingepackt, Fahnen; Aufgabe „Fahnen verstecken“, Waehrend, von Tobias übernommen) | `200 InBibliothek=true`; Eintrag in A mit Titel, Material (Fahnen, Seile mit Menge), Aufgabe (Titel, Waehrend), ohne Status und Personen | zweites #1 → `200`, weiter genau ein Eintrag |
| Mahlzeit | #1 auf „Frühstück“ | – | `409 konflikt` |
| Bibliothek lesen | Miriam #2 in X | Einträge von A; Z (Org B) sieht sie nicht | – |
| Übernehmen | Miriam #3 in X `{tag: 2027-08-05, beginn: "10:00"}` | `201`, neuer Punkt „Geländespiel“ frei, Material „gebraucht“ ohne Mitbringen, Aufgabe frei/offen am neuen Punkt, `InBibliothek=false` | Tag fehlt/außerhalb → `400 tag`; Beginn „25:99“ → `400 beginn`; Eintrag aus B → `404` |
| Abgeschlossen | X `Gelaufen` (SQL) | #2 `200` | #1, #3 → `409 freizeit-abgeschlossen` |
| Org-Ansicht | Andrea #4, #5 | `200` Liste, `204`; danach Punkt in X `InBibliothek=false`, #1 legt neu an | unbekannt/aus B → `404` |
| Punkt gelöscht | abgelegten Punkt löschen | Eintrag bleibt | – |
| Frist | Lösch-Lauf mit X abgelaufen | Punkte von X weg, Eintrag bleibt | – |
| Mandant | Einträge in A und B | Lese-Kontext je Mandant zeigt nur die eigenen | – |

</intent-contract>

## Code Map

- `src/Modules/Programm/Zeltlotse.Programm/ProgrammModul.cs` -- Freizeit-Gruppe `…/programm` (#1–#3 anhängen); neue Org-Gruppe `/organisationen/{orgId}/programmbibliothek` mit `.NurOrgRolle(ProgrammRechte.Bibliothek)` (Muster Vorlagen-Gruppe dort).
- `src/Modules/Programm/Zeltlotse.Programm/Aufgaben/{AufgabeAlsVorlage.cs,VorlageLoeschen.cs,VorlagenLaden.cs,AufgabenLaden.cs}` -- Vorlagen für #1 (Phase-Prüfung, Sperre, Verknüpfung nur wenn leer), #5, #4/#2 (`AufgabenLesen.AlsApi`).
- `src/Modules/Programm/Zeltlotse.Programm/Programme/{Programmpunkt.cs,ProgrammpunktAnlegen.cs (Validator Tag/Beginn, `TagAusserhalb`, `IFreizeitTageLesen`, Endpunkt mit `ValidierungsAusnahme`),WochenrasterLaden.cs (`ProgrammLesen.AlsApi` → `InBibliothek`, `ProgrammProbleme.Konflikt`/`Abgeschlossen`),ProgrammRechte.cs}` -- Entität, Übernahme-Validierung, Antwortform, Rechteliste `Bibliothek`.
- `src/Modules/Programm/Zeltlotse.Programm/{Materialien/Material.cs,Aufgaben/Aufgabe.cs}` -- Fabrikmethoden `Anlegen` für die Kopie.
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` -- DbSet, `ProgrammpunktSperrenAsync`, `ProgrammModell` (Muster FK SET NULL Zeile 157); Lese-Context ebenso; Migration per `dotnet ef migrations add Programmbibliothek` im Modulprojekt.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` -- Liste `Programmbibliothek` und Eintrag in `Erlaubt` (Muster `AufgabenVorlagen`).
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs (Ergebnistypen Z. 10-46, `AufgabeAlsVorlageAsync` 288, `VorlagenAsync` 301, `VorlageLoeschenAsync` 334, `AnlegenAsync` 99),Texte/Anzeige.cs}` -- Client-Muster.
- `src/Zeltlotse.App/Features/Programm/{ProgrammpunktSeite.razor (Cards Material 91-121, Aufgaben 122-151, `Abgeschlossen` 188),Wochenraster.razor (Link „Materialliste“ 59, Tag-Feld mit min/max 66-80),AufgabenVorlagen.razor (Org-Seiten-Muster),ProgrammTexte.cs}`; `Features/Organisationen/OrganisationAnsicht.razor` (Card „Aufgaben-Vorlagen“ 74-81, `Darf` 246).
- `tests/Zeltlotse.Api.Tests/{AufgabenTests.cs (Helfer 31-99, Gelaufen per SQL 92, Freizeit-Rechte 446-491, Org-Rechte inkl. Org-Überblick 515-543, Erlaubt 545-556),RechteTests.cs:63-72,EndpunktListeTests.cs (Programm 249-283, `Assert.Equal(33, mitOrg.Count)` 304 → 35),DemoDatenTests.cs:470-490,LoeschlaufTests.cs}`; `FreizeitpassSzenario` (Andrea Org-Admin A, Lea Org-Admin B, Miriam Leitung X und Z, Tobias Team X, Katrin Elternteil, Gerd, Anonym).
- `tests/Zeltlotse.Programm.Tests/AufgabeTests.cs` (Mandantenfilter 254-281, Rechte-Gleichheit 336-342) -- Muster.
- `tests/Zeltlotse.Ui.Tests/AufgabenSeitenTests.cs` (Aufbau 36-72, `NachgebildeterHandler.Bei`) -- bUnit-Muster.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs (Programmpunkte 135-140, Material 146-153, ZweiteFreizeit 159 „Kinderbibeltage 2027“, Leitung Tim Kröger),DemoDaten.cs (Schritte 132-158, `AufgabenAsync` 974, `ZweiteFreizeitAsync` 875)}`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Programmpunkt` 1592, `ProgrammAsync` 1893, `Org_Aufgaben_Vorlagen` 1863, `BildAsync` 2069, `Geraete` 21); `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-28`; `doku/src/content/docs/{mitarbeitende/{index,programm}.md (Abschnitt „Noch nicht dabei“ nennt die Bibliothek),org-admin/{index,aufgaben-vorlagen}.md,daten.md:66-68}`.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/{Programm/Bibliothek.cs,Programm/Programm.cs,Gemeinsam/FreizeitKontext.cs}` -- Datenformen, `InBibliothek`, Aktionsschlüssel.
- `tests/Zeltlotse.Programm.Tests/BibliothekTests.cs` (zuerst) + `src/Modules/Programm/Zeltlotse.Programm/Bibliothek/BibliotheksEintrag.cs` + `Programmpunkt.cs` -- Entität `Ablegen(id, org, punkt, material, aufgaben, jetzt)` (Material nach Bezeichnung, Aufgaben nach Phase, Titel), `Programmpunkt.BibliothekMerken`; Tests für Momentaufnahme, Mandantenfilter, Rechte-Gleichheit `Bibliothek` = `OrgRechte.Programmbibliothek`.
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` + Migration `Programmbibliothek` -- Tabelle, JSON-Spalten, FK SET NULL, Indizes, `EintragSperrenAsync`.
- `tests/Zeltlotse.Api.Tests/ProgrammbibliothekTests.cs` (zuerst) -- jede Matrix-Zeile, alle Rechte-Pflichtfälle der 5 Routen, gleichzeitiges Ablegen (genau ein Eintrag); `EndpunktListeTests.cs` (#1–#3, Org-Zählung 35), `RechteTests.cs` (Aktionsschlüssel am Ende), Erlaubt-Test, Lösch-Lauf (Eintrag bleibt).
- `src/Modules/Programm/Zeltlotse.Programm/Bibliothek/{ProgrammpunktAblegen,BibliothekLaden,BibliothekUebernehmen,EintragLoeschen}.cs` + `ProgrammRechte.cs` + `ProgrammModul.cs` + `WochenrasterLaden.cs` + `OrgRechte.cs` -- Slices, Routen, `InBibliothek`, Rechte.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Texte/Anzeige.cs}` -- `AblegenAsync` (ProgrammAktion), `BibliothekAsync(freizeitId)` und `OrgBibliothekAsync(orgId)` (null bei 403/404), `BibliothekUebernehmenAsync` (ProgrammAktion mit Feldfehlern), `EintragLoeschenAsync`.
- `src/Zeltlotse.App/Features/Programm/{Programmbibliothek.razor,BibliotheksEintragCard.razor,OrgProgrammbibliothek.razor}` + `ProgrammpunktSeite.razor` + `Wochenraster.razor` + `Organisationen/OrganisationAnsicht.razor` + `tests/Zeltlotse.Ui.Tests/ProgrammbibliothekSeitenTests.cs` --
  - Programmpunkt (nicht bei Mahlzeiten): Card „Programmbibliothek“ mit Satz „Titel, Material und Aufgaben lassen sich für andere Freizeiten der Organisation ablegen.“ und Knopf „In Bibliothek ablegen“ (ein Tipp, nicht in Gelaufen); bei `InBibliothek` stattdessen „Steht in der Programmbibliothek der Organisation.“
  - Wochenraster: Link „Programmbibliothek“ neben „Materialliste“ → `/freizeiten/{id}/programm/bibliothek`: Dachzeile „Programm“, H1 „Programmbibliothek“, je Eintrag `BibliotheksEintragCard` (Titel, „Abgelegt am TT.MM.JJJJ“, Material „Bezeichnung · Menge“, Aufgaben „Titel · Phase“), darunter (nicht in Gelaufen) Formular mit Tag (date, min/max aus dem Wochenraster) und Uhrzeit, Knopf „In diese Freizeit übernehmen“; Erfolg → Navigation zur Seite des neuen Programmpunkts; Feldfehler am Feld. Leer: „Noch nichts in der Programmbibliothek. Programmpunkte lassen sich an ihrer Seite mit „In Bibliothek ablegen“ hier ablegen.“
  - Org: Card „Programmbibliothek“ bei `programmbibliothek-verwalten`, Knopf „Programmbibliothek öffnen“ → `/organisationen/{OrgId}/programmbibliothek`: Dachzeile „Organisation“, H1 „Programmbibliothek“, `BibliotheksEintragCard` mit `InlineBestaetigung` „Aus der Bibliothek entfernen“; leer „Noch nichts in der Programmbibliothek. Das Team legt gelungene Programmpunkte in seiner Freizeit ab.“
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests.cs` -- Schritt `BibliothekAsync` nach `AufgabenAsync`: legt „Lagerfeuer“ und „Geländespiel“ des Zeltlagers ab (idempotent über die Verknüpfung), übernimmt „Lagerfeuer“ in die Kinderbibeltage am 2027-08-11 um 19:30 als Tim Kröger (nur wenn es den Titel dort noch nicht gibt).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theories `Programmbibliothek` (Jonas, Programm → „Programmbibliothek“) und `Org_Programmbibliothek` (Andrea), je Handy und Laptop mit `AxeAsync`, `BildAsync` → `programmbibliothek-*`, `org-programmbibliothek-*`; durch neue Card oder neuen Link geänderte Bilder (u. a. `programmpunkt-*`, `wochenraster-*`, Organisationsseite) neu erzeugen, Rauschen per Pixelvergleich zurücksetzen.
- `doku/src/content/docs/mitarbeitende/programmbibliothek.md` (neu, „Programmpunkte ablegen und übernehmen“: bewusst ablegen, was mitkommt und was nicht, übernehmen mit Tag, nicht nach dem Abschluss) + `org-admin/programmbibliothek.md` (neu) + Links in `mitarbeitende/{index,programm}.md` (Abschnitt „Noch nicht dabei“ anpassen), `leitung/index.md`, `org-admin/index.md`; Satz in `daten.md` (Abschnitt Programm: Bibliothek ohne Personendaten, bleibt); `DokuRegeln.cs` PflichtSeiten.

**Acceptance Criteria:**
- Given Jonas tippt am Programmpunkt „Lagerfeuer“ „In Bibliothek ablegen“, when Lea in einer anderen Freizeit derselben Organisation die Programmbibliothek öffnet, then steht „Lagerfeuer“ dort mit Material und Aufgaben.
- Given kein Programmpunkt wurde abgelegt, when jemand die Programmbibliothek öffnet, then ist sie leer, obwohl die Freizeit Programmpunkte hat.
- Given ein Eintrag mit zwei Materialien und einer Aufgabe, when das Team ihn übernimmt, then zeigt der neue Programmpunkt beide Materialien als „gebraucht“ und die Aufgabe als offen, und die Gesamt-Materialliste enthält sie.
- Given ein Org-Überblick-Konto, when es die Organisation öffnet, then gibt es keine Card „Programmbibliothek“.
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur, Durchlauf, UI und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und vier neue Screenshots liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 26 findings — high 0, medium 0, low 17, false 9, maybe-false 0
- findings:
  - `[low]` `[patch]` (Blind) „Keine Personendaten“ zu stark, Freitexte können Namen enthalten — Doku-Sätze in `daten.md`, `org-admin/programmbibliothek.md`, `mitarbeitende/programmbibliothek.md` präzisiert.
  - `[low]` `[reject]` (Blind) Übernehmen eines inzwischen entfernten Eintrags zeigt allgemeinen Fehler — selten; Abhilfe bräuchte ein neues Ergebnis `NichtGefunden` und einen Zweig (carried-Abwägung aus 3-3 bis 3-5).
  - `[low]` `[reject]` (Blind) Eintrag zeigt nicht, aus welcher Freizeit er stammt — zusätzliches Feld, nicht verlangt; „Abgelegt am“ unterscheidet Fassungen (Design Notes).
  - `[low]` `[patch]` (Blind) Org-Admin-Doku: erneutes Ablegen nach dem Entfernen gilt nicht in abgeschlossenen Freizeiten — Satz ergänzt.
  - `[low]` `[reject]` (Blind) Gleichnamige Knöpfe je Eintrag — gleiches Muster wie Aufgaben-Vorlagen, axe grün, Knopf liegt in der Card mit Titel-Überschrift; Abhilfe bräuchte neuen Parameter am Baustein.
  - `[low]` `[reject]` (Blind) Kein Blättern oder Filter — MVP-Größe (wenige Einträge je Organisation); Filter wäre neue Oberfläche.
  - `[false]` `[reject]` (Blind) Ablegen ändert `xmin` und erzeugt Autosave-Konflikte — AD-19 und `Programmpunkt.FeldAendern` vergleichen den Altwert desselben Felds, nicht die Version; ein geänderter Verweis löst keinen `409` aus.
  - `[low]` `[reject]` (Blind) Keine UI-Tests für fehlgeschlagenes Ablegen (409, Fehler, Doppeltipp) — seltene Pfade, Server per HTTP-Tests belegt, gleiches Muster wie 3-1 bis 3-5.
  - `[low]` `[patch]` (Blind) Ortsangaben der Doku („ganz unten“, „oben steht“) stimmen nicht — an die tatsächliche Reihenfolge angepasst.
  - `[low]` `[reject]` (Blind) Gleichzeitiges Ablegen und Entfernen hinterlässt veraltete Anzeige — sehr selten, Neuladen zeigt den richtigen Stand (AD-19: keine Live-Aktualisierung).
  - `[false]` `[reject]` (Blind) Zurück-Link an unterschiedlicher Stelle — Freizeit-Seiten (`ProgrammpunktSeite`) haben ihn unten, Org-Seiten (`AufgabenVorlagen`) oben; beide neuen Seiten folgen ihrem Bereich.
  - `[low]` `[patch]` (Blind) Demo-Kommentare nennen Tim, der Code setzt keine Person — Kommentare korrigiert.
  - `[false]` `[reject]` (Blind) `loeschfristen-*`, `nachweis-richtlinie-*` geändert ohne Grund — die neue Card auf der Organisationsseite verschiebt diese Ansichten (Pixelvergleich, wie in 3-5); `programmpunkt-*` unverändert, weil die Card außerhalb des sichtbaren Bereichs liegt.
  - `[low]` `[reject]` (Edge) Entfernen eines schon entfernten Eintrags (404) zeigt Fehler, Card bleibt — wie oben, carried aus 3-3 bis 3-5.
  - `[low]` `[reject]` (Edge) Übernehmen nach Entfernen (404) — gleiche Zeile wie Blind oben.
  - `[low]` `[reject]` (Edge) Ablegen während laufendem Titel-Autosave nimmt alten Titel — selten; Abhilfe bräuchte eine Flush-Schnittstelle im Autosave-Baustein.
  - `[false]` `[reject]` (Edge) `xmin`-Änderung durch Ablegen/SET NULL führt zu Konflikten — wie Blind oben widerlegt.
  - `[low]` `[reject]` (Edge) Demo wirft bei umbenannten Demo-Punkten — Demo läuft frisch (carried aus 3-3 bis 3-5).
  - `[low]` `[patch]` (Verification gap) Gelaufen mit `inBibliothek = true` und das Fehlen der Card ohne Ablage ungetestet — bUnit-Test ergänzt.
  - `[low]` `[reject]` (Verification gap) 409-/Fehlerzweige der drei Seiten ungetestet (gemeldet als defer) — Pfade gehören zu dieser Story, also kein defer; selten, Server per HTTP-Tests belegt, gleiches Muster wie frühere Stories.
  - `[false]` `[reject]` (Intent) Kein `403` auf Freizeit-Routen — jede Freizeit-Rolle darf alles im Programm; es gibt keine falsche Rolle im eigenen Mandanten, Org-Rollen ohne Mitarbeit bekommen `404` (AD-12), wie 3-1 bis 3-5.
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — kein Token-Endpunkt in dieser Story.
  - `[false]` `[reject]` (Intent) Test-zuerst im Diff nicht sichtbar — Prozessangabe, kein Fehler im Code.
  - `[false]` `[reject]` (Intent) Doku-Build nicht belegt — `npm run build` lief fehlerfrei und läuft nach den Patches erneut (Verifikation).
  - `[low]` `[reject]` (Intent) Playwright nur lesend, Programmpunkt- und Org-Card in keinem Bild — AD-30 verlangt Screenshot-Tests je Oberfläche (zwei neue Seiten, je Handy/Laptop); Aktionen sind per bUnit und HTTP belegt; ein weiterer Screenshot wäre zusätzlicher Test.
  - `[false]` `[reject]` (Intent) `DemoDatenTests` gelockert — die Zählungen gelten nun je Freizeit, die neuen Zeilen der Kinderbibeltage prüft der Test eigens.

## Design Notes

- **Momentaufnahme statt Verweis:** Der Eintrag kopiert Titel, Material (Bezeichnung, Menge) und Aufgaben (Titel, Phase) zum Zeitpunkt des Ablegens. Verweise auf Programmpunkte gingen mit dem Lösch-Lauf der Freizeit verloren; die Bibliothek muss die Freizeit überdauern. JSON-Spalten, weil der Eintrag unveränderlich ist und nur als Ganzes gelesen wird.
- **Ohne Personen und Zustände:** Wer übernommen hat oder mitbringt, Status, Erledigt und Reservierungen gehören der alten Freizeit; die neue Freizeit plant neu. So hat der Eintrag keinen Personenbezug (keine AD-16-Pflichten).
- **Verknüpfung nur beim Ablegen:** `BibliotheksEintragId` verhindert Doppeleinträge bei Doppeltipp (Muster `VorlageId` aus 3-5). Ein übernommener Punkt ist nicht verknüpft, damit die verbesserte Fassung erneut abgelegt werden kann; mehrere Fassungen unterscheidet „Abgelegt am“, der Org-Admin räumt auf.
- **Kein Bearbeiten, Entfernen nur durch den Org-Admin:** rollen-und-rechte.md gibt organisationsweite Sammlungen (Vorlagen, Inventar) dem Org-Admin; das Team legt ab und übernimmt (wie „Als Vorlage speichern“). Org-Überblick hat die Bibliothek nicht in seiner Liste → `403`.
- **Mahlzeiten nicht ablegbar:** Sie sind feste Punkte jeder Freizeit (FR-34); `409 konflikt`, Knopf fehlt.
- **Nicht nach dem Abschluss:** AD-17 erlaubt in Gelaufen nur zwei Ausnahmen; „Nach dem Lager“ (NS-5) heißt Nachbereitung. Die Doku sagt das.
- **Übernehmen mit Tag:** Wie beim Anlegen braucht ein Programmpunkt einen Tag der Freizeit; die Uhrzeit ist freiwillig.
- **403 nur auf Org-Routen** wie in 3-3 bis 3-5.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler
- `dotnet test --project tests/Zeltlotse.Programm.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Programmbibliothek/Programm/EndpunktListe/Rechte/Demo/Loeschlauf), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. E2E) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Programmbibliothek im Modul Programm (CAP-24, FR-41).
- **Entität `BibliotheksEintrag`:** organisationseigene, unveränderliche Momentaufnahme (Titel, abgelegt am, Material mit Bezeichnung/Menge und Aufgaben mit Titel/Phase als JSON-Spalten), ohne Verweis auf Personen.
- **Freizeit-Endpunkte (Leitung und Team):** Programmpunkt ablegen (idempotent über `BibliotheksEintragId`, Mahlzeit `409 konflikt`), Bibliothek lesen (auch in Gelaufen), Eintrag übernehmen (neuer Punkt, Material „gebraucht“, freie offene Aufgaben, ohne Verknüpfung). Ablegen und Übernehmen in Gelaufen `409 freizeit-abgeschlossen`.
- **Org-Endpunkte (nur Org-Admin):** Bibliothek lesen, Eintrag entfernen (Verknüpfung am Punkt wird per FK geleert). Neuer Aktionsschlüssel `programmbibliothek-verwalten`; Org-Überblick `403`.
- **Programmpunkt-Antwort:** neues Feld `inBibliothek` (additiv).
- **Web-App:** Card „Programmbibliothek“ am Programmpunkt, Seite „Programmbibliothek“ der Freizeit (Link neben „Materialliste“), Org-Seite mit Card auf der Organisationsseite.
- **Demo-Daten:** „Lagerfeuer“ und „Geländespiel“ abgelegt, „Lagerfeuer“ in die Kinderbibeltage übernommen.
- **Doku:** zwei neue Seiten, Links und Sätze in fünf bestehenden Seiten, vier neue Screenshots.

**Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Programm/Bibliothek.cs`, `Programm/Programm.cs`, `Gemeinsam/FreizeitKontext.cs` -- Datenformen, `InBibliothek`, Aktionsschlüssel.
- `src/Modules/Programm/Zeltlotse.Programm/Bibliothek/*` -- Entität und fünf Endpunkte.
- `src/Modules/Programm/Zeltlotse.Programm/{Daten/ProgrammDbContext.cs,ProgrammModul.cs,Programme/{Programmpunkt,ProgrammRechte,WochenrasterLaden}.cs}`, Migration `Programmbibliothek` -- Tabelle, Routen, Rechte, Verknüpfung.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Rollen/OrgRechte.cs` -- Rechteliste und `Erlaubt`.
- `src/Zeltlotse.Ui/{Api/ProgrammApiClient.cs,Texte/Anzeige.cs}` -- Client und Texte.
- `src/Zeltlotse.App/Features/Programm/{Programmbibliothek,OrgProgrammbibliothek,BibliotheksEintragCard}.razor`, `ProgrammpunktSeite.razor`, `Wochenraster.razor`, `ProgrammTexte.cs`, `Organisationen/OrganisationAnsicht.razor` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoDaten,DemoKatalog}.cs` -- Demo-Schritt `BibliothekAsync`.
- Tests: neu `tests/Zeltlotse.Programm.Tests/BibliothekTests.cs`, `tests/Zeltlotse.Api.Tests/ProgrammbibliothekTests.cs`, `tests/Zeltlotse.Ui.Tests/ProgrammbibliothekSeitenTests.cs`; ergänzt Endpunktliste (Org 35), Rechte, Programm-JSON-Felder, Demo, Doku-Regeln, E2E-Screenshots.
- `doku/src/content/docs/{mitarbeitende,org-admin}/programmbibliothek.md` (neu); `mitarbeitende/{index,programm}.md`, `leitung/index.md`, `org-admin/index.md`, `daten.md`.
- Screenshots: neu `programmbibliothek-*`, `org-programmbibliothek-*`; neu erzeugt `wochenraster-*` (neuer Link), `loeschfristen-*`, `nachweis-richtlinie-*` (neue Org-Card verschiebt die Ansicht); Rauschen per Pixelvergleich zurückgesetzt.

**Review:** 26 Befunde (high 0, medium 0, low 17, false 9).
- **Gepatcht (5 low):** Doku-Aussage zu Personendaten präzisiert; Org-Admin-Doku zum erneuten Ablegen nach dem Entfernen; Ortsangaben in der Doku; Demo-Kommentare ohne Person; bUnit-Test für Gelaufen mit und ohne Ablage.
- **Zurückgestellt:** keine.
- **Abgelehnt:** 21, Gründe im Review Triage Log (seltene 404-/409-Pfade wie in 3-3 bis 3-5, keine Freizeit-Rolle für `403`, kein Token-Endpunkt, `xmin` löst keinen Feld-Konflikt aus, Demo läuft frisch).

**Folge-Review empfohlen:** nein (kein high oder medium gepatcht).

**Verifikation:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler.
- **Komplettlauf vor dem Review** (je Projekt): E2E 153/153 mit Screenshots, Api 790/790, Architecture 133, Ui 642, Programm 91, Ablaeufe 10, Freizeiten 81, Inventar 34, Konten 26, Organisationen 58, Personen 153, SharedKernel 100, Sicherheit 36 – alle grün.
- Eigene Nachprüfung: Programm.Tests 91/91, ArchitectureTests 133/133, `ProgrammbibliothekTests` 15/15 gegen PostgreSQL.
- **Nach den Patches** (nur Doku, Kommentare, ein bUnit-Test): Build 0 Fehler, `ProgrammbibliothekSeitenTests` 11/11, Doku-Prüfungen der Architekturtests 18/18.
- `npm run build` in `doku/` -- fehlerfrei, 53 Seiten.
- Matrix-Audit: jede Zeile der I/O-Matrix ist durch grüne Tests in `ProgrammbibliothekTests` bzw. `BibliothekTests` abgedeckt.

**Restrisiken:**
- Freitexte (Titel, Bezeichnung) können Namen enthalten und überdauern die Löschfrist der Freizeit; die Doku weist darauf hin.
- Übernehmen oder Entfernen eines inzwischen entfernten Eintrags zeigt nur eine allgemeine Fehlermeldung.
- „Abgelegt am“ zeigt in den Screenshots ein fest gesetztes Datum (der Test überschreibt das Datum der Demo-Einspielung).
- Die neuen Cards am Programmpunkt und auf der Organisationsseite liegen außerhalb des sichtbaren Bereichs der bestehenden Screenshots.
