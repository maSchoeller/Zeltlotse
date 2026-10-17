---
title: 'Story 4-1: Rezepte mit Skalierung'
type: 'feature'
created: '2026-10-17'
status: 'done'
baseline_revision: 'c6e9a9933db999756bd2ae267e1c183b61064e89'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-2-material-am-programmpunkt.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-7-module-pro-freizeit-schalten.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Abschluss der Freizeit zwischen Phasenprüfung und Commit könnte einen Schreibvorgang in einer gerade abgeschlossenen Freizeit durchlassen.
    evidence: |-
      Edge-Case-Review: SchreibschutzStufe und Handler prüfen die Phase vor dem Schreiben, ohne die Freizeit-Zeile zu sperren; gleiches Muster in allen Modulen seit 3-8. Klären würde ein Gleichzeitigkeitstest „Freizeit abschließen“ gegen einen Küche-Schreibvorgang.
    location: >-
      src/Modules/Kueche/Zeltlotse.Kueche/Rezepte/RezeptAnlegen.cs (gleiches Muster in allen Benutzer-Commands mit Freizeitbezug)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Die Küche kann in Zeltlotse noch keine Rezepte anlegen. Ohne Rezepte mit Basis-Personenzahl gibt es keine Hochrechnung auf die Freizeit und keine Grundlage für Essensplan und Einkaufsliste aus 4-2 (CAP-28, FR-46).

**Approach:**
- Das Modul Küche bekommt Rezepte je Freizeit: Name, Basis-Personenzahl, Zubereitung als Freitext und Zutaten mit Menge, Einheit, Bezeichnung und Allergenen aus den 14 EU-Hauptallergenen.
- Der Server rechnet jede Zutatenmenge auf die Personenzahl der Freizeit hoch. Diese Zahl liefert eine neue Leseschnittstelle in `Freizeiten.Contracts`.
- Die Web-App zeigt eine Rezeptliste und eine Rezeptseite mit Autosave. Der Zugang ist der Knopf „Küche“ auf der Startseite der Freizeit, sichtbar nur bei eingeschaltetem Modul Verpflegung.
- Dazu kommen Doku-Seite, Demo-Daten und Screenshots.

## Boundaries & Constraints

**Always:**

*Endpunkte und Rechte*
- Alle Endpunkte liegen unter `/api/freizeiten/{freizeitId}/kueche/rezepte…`.
- Jeder trägt `.NurFreizeitRolle(Leitung, Team).GehoertZuSchalter(FunktionsSchalter.Verpflegung)`. Ist das Modul aus, antworten sie mit 404.
- Jeder Benutzer-Command trägt `IBenutzerCommand`, `IFreizeitCommand`, `ISchalterCommand` (Verpflegung) und `IModulCommand<KuecheDbContext>`.
- Ist die Freizeit gelaufen, liefern alle schreibenden Endpunkte 409 `freizeit-abgeschlossen`, Lesen bleibt 200. Der Handler prüft zusätzlich die Phase wie in Programm.

*Speichern und Bearbeiten*
- Autosave nach AD-19: `PATCH` mit genau einem Feld. Werte reisen als Text. Bei abweichendem `alterWert` desselben Felds kommt 409 `konflikt`. PATCH und DELETE sperren die Zeile mit `FOR UPDATE`.
- Löschen in der Oberfläche nur per `InlineBestaetigung`.

*Rechnen*
- Personenzahl = Anzahl verschiedener Personen aus bestätigten Teilnehmenden-Anmeldungen ∪ Personen mit Freizeit-Rolle (Leitung oder Team). Jede Person zählt einmal und gleich.
- Hochgerechnete Menge = Menge × Personenzahl ÷ Basis-Personenzahl, auf 2 Nachkommastellen gerundet (`MidpointRounding.AwayFromZero`).

*Allergene*
- Allergen-Werte sind die Schlüssel aus `Personen.Contracts` `Fragenkatalog.Allergene`. Gespeichert werden sie als Komma-Liste in Katalogreihenfolge, ohne Doppelte.
- Der Client nutzt eine Kopie in `Api.Contracts/Kueche`. Ein Test sichert, dass die Kopie mit dem Katalog übereinstimmt.

*Oberfläche und Doku*
- Oberfläche nur aus `Ui.Komponenten`/`Ui`, Deutsch ohne Anrede, Handy und Laptop.
- Testgetrieben: Der Test entsteht vor dem Code.

**Never:**
- Keine Gerichte an Mahlzeiten, keine Einkaufsliste, kein Allergen-Abgleich gegen Freizeitpässe, keine Tagesansicht und kein Restetag. Das kommt mit 4-2 bis 4-4.
- Keine organisationsweiten Rezepte und kein Kopieren von Rezepten zwischen Freizeiten.
- Keine Einheiten-Umrechnung und keine generierten Rezepte.
- Kein neuer Problemcode und kein neuer Aktionsschlüssel. Keine Ereignisse und keine Spine-Änderung.
- Kein `[ErlaubtNachAbschluss]` und keine Eigenschaft namens `PersonId` in Küche-Entitäten.
- Keine Pop-ups. Seed-Daten nie per Tabellen-Insert.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Hochrechnung | Rezept Basis 3, Zutat 300 g; Freizeit mit 60 Personen | `hochgerechneteMenge` 6000, `personenzahl` 60 | – |
| Ungerade Hochrechnung | Basis 4, Menge 1,5; 6 Personen | 2,25 | – |
| Zutat ohne Menge | „Salz“, Menge leer | Menge und hochgerechnete Menge `null`, Anzeige nur Bezeichnung | – |
| Keine Personen | Freizeit ohne Rolle und ohne bestätigte Anmeldung | `personenzahl` 0, hochgerechnete Menge `null`; Oberfläche: „Noch niemand bestätigt – Mengen gelten für die Basis.“ | – |
| Doppelte Person | Person mit bestätigter Teilnehmenden-Anmeldung und Team-Rolle | zählt einmal | – |
| Ungültige Zahl | Basis „0“, „-2“, „abc“, „1001“; Menge „0“, „-1“, „1,2345“, „x“ | 400 mit Feldfehler am JSON-Feld | Wert bleibt stehen, Fehler am Feld |
| Dezimalformat | Menge „1.5“ oder „1,5“ | gespeichert und zurückgegeben als „1,5“; nachfolgender `alterWert` „1,5“ passt | – |
| Unbekanntes Allergen | `allergene: "milch,kaese"` | 400 am Feld `allergene` | – |
| Allergene normalisieren | `"senf,milch,milch"` | gespeichert `"milch,senf"` | – |
| Pflichtfeld fehlt | POST Rezept ohne Name oder Basis; Zutat ohne Bezeichnung | 400 am Feld | Fehlerübersicht + Feld |
| Modul aus | Verpflegung aus, Leitung/Team | jede Küche-Route 404; Daten bleiben, nach Einschalten wieder da | – |
| Fremde Zuordnung | Zutat-Id eines anderen Rezepts oder Rezept aus Freizeit Z unter X | 404 | – |
| Gleichzeitig | zwei PATCH desselben Felds mit gleichem `alterWert`; Zutat wird gelöscht, während das Rezept gelöscht wird | genau einer 200, der andere 409 `konflikt`; kein 500, Ergebnis 204/404 | – |

</intent-contract>

## Code Map

**Modul Küche (Gerüst aus 0-1)**
- `src/Modules/Kueche/Zeltlotse.Kueche/KuecheModul.cs` – `AddKuecheModul`, leerer Stub `MapKuecheEndpoints`. Hier kommen Routen, Löschlauf und die Lese-Abfrage hinzu.
- `src/Modules/Kueche/Zeltlotse.Kueche/Daten/KuecheDbContext.cs` – `KuecheDbContext`, `KuecheLeseDbContext`, leeres `KuecheModell.Konfigurieren` und die Design-Zeit-Fabrik. Schema `kueche`.
- `src/Modules/Kueche/Zeltlotse.Kueche/Zeltlotse.Kueche.csproj` – Referenzen auf `Freizeiten.Contracts`, `Personen.Contracts` und `Api.Contracts` ergänzen (Vorlage `Programm.csproj`). `InternalsVisibleTo` ist schon gesetzt.

**Vorlagen aus Programm**
- `src/Modules/Programm/Zeltlotse.Programm/Materialien/Material.cs` und `MaterialAnlegen.cs`, `MaterialBearbeiten.cs`, `MaterialLoeschen.cs` – Vorlage für Entität (`Anlegen`, `FeldAendern`, `Wert`, `uint Version`) und für die Slices: Command, Ergebnis, Validator, Handler und Endpunkt in einer Datei.
- `src/Modules/Programm/Zeltlotse.Programm/Programme/FeldPatch.cs` – PATCH-Leser; je Modul eine eigene Kopie.
- `src/Modules/Programm/Zeltlotse.Programm/Programme/WochenrasterLaden.cs` – `ProgrammProbleme` für die 409-Antworten.
- `src/Modules/Programm/Zeltlotse.Programm/Daten/ProgrammDbContext.cs` – `MaterialSperrenAsync` (`FOR UPDATE`) und die Konfiguration: `IsRowVersion`, Cascade, Indizes.
- `src/Modules/Programm/Zeltlotse.Programm/Datenschutz/ProgrammDatenschutz.cs:89` – Vorlage `ILoeschlauf` mit `…Loeschen(FreizeitIds)`.

**SharedKernel und Freizeiten**
- `src/Zeltlotse.SharedKernel/Rechte/Rechtemerkmale.cs`, `Rechte/FunktionsSchalter.cs`, `Commands/IModulCommand.cs` – `GehoertZuSchalter`, `ISchalterCommand`, `IFreizeitCommand`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/` – Muster `IFreizeitTeamLesen` und `IFreizeitSchalterLesen`. Neu kommt `IFreizeitPersonenzahlLesen` dazu.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitPersonenLesen.cs` – Muster für die Implementierung über `FreizeitenLeseDbContext` (`Anmeldungen`, `Rollen`). Die Registrierung steht in `FreizeitenModul.cs`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Anmeldung.cs` – `PersonId`, `AnmeldeArt.Teilnehmende`, `AnmeldeStatus.Bestaetigt`.

**Allergene, API-Verträge, Client**
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs:77` – `Allergene` (14 × `FrageOption(Wert, Text)`).
- `src/Zeltlotse.Api.Contracts/Programm/Material.cs` – Muster für DTOs, Feld-Enum und `…Felder`. Neuer Ordner `Kueche/`.
- `src/Zeltlotse.Ui/Api/ProgrammApiClient.cs` und `src/Zeltlotse.Ui/UiRegistrierung.cs` – Muster für den Client und dessen Registrierung.
- `src/Zeltlotse.Ui/Autosave/AutosaveFeld.razor` (`Mehrzeilig`) und `AutosaveAuswahlgruppe.razor` (`Mehrfach`) – Autosave-Bausteine für Text und Allergen-Auswahl.
- `src/Zeltlotse.App/Features/Programm/Materialliste.razor` – Seitenmuster: `NichtVerfuegbar`, Hinweis „nur lesbar“, `LeerZustand`.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` (Knopf-Liste ca. Z.58–78) – neuer Knopf „Küche“, nur wenn `_kontext.Schalter` `Verpflegung` enthält.

**Tests**
- `tests/Zeltlotse.Api.Tests/MaterialTests.cs` und `FreizeitpassSzenario.cs` – Muster für HTTP-Tests: Rechtematrix, Abschluss per SQL `AbgeschlossenAm`, Gleichzeitigkeit.
- `tests/Zeltlotse.Api.Tests/ModuleTests.cs` – `SchaltenAsync`, um Verpflegung einzuschalten.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` (Routenliste Z.220–293, Schalter-Leitplanke Z.305) – neue Routen eintragen.
- `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` – Zähler und Test für den Küche-Schritt; Namen aus `DemoKatalog`.
- `tests/Zeltlotse.Programm.Tests/` – Vorlage für das neue Projekt `tests/Zeltlotse.Kueche.Tests`: csproj, `AssemblyInfo.cs` und Mandantenfilter-Test. Das Projekt kommt in `Zeltlotse.slnx`.
- `tests/Zeltlotse.Ui.Tests/MaterialSeitenTests.cs` – bUnit-Muster.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Materialliste` ca. Z.1685, Helfer ab ca. Z.2087) und `E2EHilfe.AxeAsync`.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` – `PflichtSeiten`.

**Demo und Doku**
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs` und `DemoDaten.cs` (`EinspielenAsync` Z.121, Muster `MaterialAsync` Z.854) – Zeltlager Sommer 2027 hat Verpflegung an.
- `doku/src/content/docs/kueche/index.md`, `leitung/module-schalten.md`, `daten.md`, `mitarbeitende/material.md` (Seitenmuster).

## Tasks & Acceptance

**Execution:**

*Tests zuerst*
- `tests/Zeltlotse.Kueche.Tests/` (neu, in `Zeltlotse.slnx`) – Fachtests für Rezept und Zutat:
  - `FeldAendern`, Konflikt und Normalisierung von Zahlen und Allergenen;
  - Validatoren;
  - Hochrechnung samt Rundung, `null` und 0 Personen;
  - Mandantenfilter;
  - Gleichheit `Api.Contracts.Kueche.Allergene` ↔ `Fragenkatalog.Allergene`.
- `tests/Zeltlotse.Api.Tests/RezepteTests.cs` (neu) – HTTP-Tests für alle 8 Routen:
  - Leitung und Team 2xx;
  - Org-Admin ohne Rolle 404, fremde Organisation 404, unbekannte Freizeit 404, ohne Sitzung 401;
  - Schalter aus 404, Rolle vor Schalter;
  - Gelaufen 409 bei allen Schreib-Routen, GET 200;
  - Zeilen der I/O-Matrix, Gleichzeitigkeit gegen echtes PostgreSQL, Cascade beim Rezept-Löschen, Löschlauf.
- `tests/Zeltlotse.Freizeiten.Tests` oder `Api.Tests` – Test für `IFreizeitPersonenzahlLesen`: bestätigte Teilnehmende plus Rollen, jede Person einmal; eingegangene, abgesagte und Mitarbeitende-Anmeldungen ohne Rolle zählen nicht; fremder Mandant ergibt 0.

*Freizeiten*
- `src/Modules/Freizeiten/…Contracts/` und `Freizeiten/Rechte/` sowie `FreizeitenModul.cs` – `IFreizeitPersonenzahlLesen.AnzahlAsync(Guid freizeitId)` anlegen, implementieren und registrieren.

*Küche: Daten und Slices*
- `src/Modules/Kueche/Zeltlotse.Kueche/` – Entitäten anlegen:
  - `Rezept` (Id, OrganisationId, FreizeitId, Name ≤ 200, BasisPersonen int 1–1000, Zubereitung ≤ 8000, Version);
  - `Zutat` (Id, OrganisationId, FreizeitId, RezeptId mit FK und Cascade, Bezeichnung ≤ 200, Menge `decimal?` > 0 und ≤ 1.000.000 mit höchstens 3 Nachkommastellen, Einheit ≤ 30, Allergene, Version; Reihenfolge nach Id).
- Ebenso: `KuecheModell`, Sperrmethoden, Migration `Rezepte`.
- Slices:
  - `RezepteLaden` (Liste mit Name, Basis, Zutatenzahl, Vereinigung der Allergene, `personenzahl`);
  - `RezeptLaden` (Detail mit Zutaten und `hochgerechneteMenge`, `personenzahl`);
  - `RezeptAnlegen` (Name und Basis Pflicht);
  - `RezeptFeldAendern` (`name`, `basisPersonen`, `zubereitung`);
  - `RezeptLoeschen`;
  - `ZutatAnlegen` (Bezeichnung Pflicht, Menge und Einheit optional);
  - `ZutatFeldAendern` (`bezeichnung`, `menge`, `einheit`, `allergene`);
  - `ZutatLoeschen`.
- Dazu `KuecheLoeschlauf`, registriert und gemappt in `KuecheModul.cs`.

*API-Verträge und Client*
- `src/Zeltlotse.Api.Contracts/Kueche/` – DTOs (`Rezeptliste`, `RezeptKurz`, `Rezept`, `Zutat`, `RezeptNeu`, `ZutatNeu`), `RezeptFeld`/`RezeptFelder`, `ZutatFeld`/`ZutatFelder` und `Allergene` (Wert, Text).
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs`, `UiRegistrierung.cs`, `Ui/Texte` – typisierter Client mit Pfad-Helfern und Aktionsergebnissen nach dem Programm-Muster.

*Oberfläche*
- `src/Zeltlotse.App/Features/Kueche/Rezepte.razor` (`/freizeiten/{id}/kueche/rezepte`):
  - Liste der Rezepte als Cards: Name, „für N Personen“, Allergene als Text;
  - Satz „Hochgerechnet auf P Personen der Freizeit.“;
  - Formular „Rezept anlegen“;
  - Leer-Zustand; in Gelaufen nur lesbar.
- `src/Zeltlotse.App/Features/Kueche/Rezept.razor` (`/freizeiten/{id}/kueche/rezepte/{rezeptId}`):
  - Autosave-Felder Name, Basis-Personenzahl, Zubereitung (mehrzeilig);
  - `Datentabelle` „Für P Personen“: Menge, Einheit, Zutat, Allergene, hochgerechnete Menge;
  - je Zutat Autosave-Felder samt `AutosaveAuswahlgruppe` Allergene und „Entfernen“ per Inline-Bestätigung;
  - „Zutat hinzufügen“ und „Rezept löschen“ per Inline-Bestätigung;
  - nach jedem gespeicherten Feld neu laden;
  - Ladefehler nicht als „leer“ anzeigen.
- `FreizeitStart.razor` – Knopf „Küche“ für Leitung und Team, nur bei Verpflegung an.
- `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs` (neu) – bUnit-Tests:
  - Liste, leer, Ladefehler;
  - Detail mit hochgerechneter Spalte und Hinweis bei 0 Personen;
  - Gelaufen nur lesbar;
  - Knopf „Küche“ nur bei Verpflegung.

*Demo-Daten*
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs` – Schritt `KuecheAsync` nach `ModuleAsync`, nur über Commands und idempotent. Inhalt:
  - Rezept „Chili sin Carne“: Basis 4, Zutaten mit Sellerie;
  - Rezept „Pfannkuchen“: Basis 4, Mehl mit gluten, Milch mit milch, Eier mit eier, Salz ohne Menge;
  - Zubereitung als Text.
- `DemoDatenTests` anpassen.

*Doku und Screenshots*
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` – Theories `Rezepte` und `Rezept` (Handy und Laptop, `AxeAsync`, Bilder `rezepte-*` und `rezept-*`) im Abschnitt „Story 4-1“.
- `doku/src/content/docs/kueche/rezepte.md` (neu):
  - Wer, Weg, Anlegen, Zutaten und Allergene, Hochrechnung samt Zählregel;
  - Hinweis „nur mit Modul Verpflegung“;
  - Nach dem Abschluss nur lesbar;
  - Freitexte ohne Namen;
  - „Noch nicht dabei“: Gerichte und Einkaufsliste;
  - Screenshots.
- Weitere Doku-Seiten:
  - `kueche/index.md`: Link zur neuen Seite;
  - `leitung/module-schalten.md`: Verpflegung bringt jetzt Rezepte;
  - `daten.md`: Rezepte werden mit den Freizeit-Daten nach der Org-Frist gelöscht;
  - `DokuRegeln.PflichtSeiten`: neue Seite eintragen.

**Acceptance Criteria:**
- Given eine Freizeit mit eingeschalteter Verpflegung und 6 Personen, when das Team ein Rezept für 4 Personen mit 1,5 kg Mehl anlegt, then zeigt die Rezeptseite 2,25 kg in der Spalte „Für 6 Personen“ auf Handy und Laptop.
- Given Verpflegung ist aus, when ein Teammitglied die Startseite der Freizeit öffnet, then fehlt der Knopf „Küche“, und ein direkter Aufruf der Rezeptseite zeigt „Diese Seite ist nicht verfügbar.“
- Given eine gelaufene Freizeit, when die Rezeptseite geöffnet wird, then steht oben der Hinweis „nur lesbar“, Anlegen und Löschen fehlen, und Schreibversuche an der API liefern 409 `freizeit-abgeschlossen`.
- Given die Lösch-Frist einer Freizeit ist abgelaufen, when der Lösch-Dienst läuft, then sind ihre Rezepte und Zutaten endgültig gelöscht.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then sind alle Tests grün, die Screenshots `rezepte-*`/`rezept-*` erzeugt und der Doku-Build fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-17 — Review pass
- verdicts: 24 findings — high 0, medium 5, low 13, false 5, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: „1.000“ (Tausenderpunkt) wird still als 1 gespeichert — `MengeLesen` ersetzt Komma durch Punkt; Patch: Tausenderpunkt-Form wird mit 400 am Feld `menge` abgelehnt, Tests ergänzt.
  - `[low]` `[patch]` Blind: „Zutaten für 1 Personen“ / „Für 1 Personen“ in `Rezept.razor:164/176` — Patch: Einzahl-Helfer verwendet, bUnit-Fall Personenzahl 1.
  - `[low]` `[patch]` Blind: Demo-Schritt wirft bei doppeltem Rezeptnamen (`SingleOrDefaultAsync`, `DemoDaten.cs:607`) — Patch: nicht werfende Suche.
  - `[low]` `[reject]` Blind: `AnzahlAsync` lädt Kennungen in den Speicher — eine Freizeit hat höchstens wenige hundert Personen; Umbau auf eine SQL-Abfrage bringt keinen spürbaren Nutzen.
  - `[false]` `[reject]` Blind: `version` im PATCH ungenutzt — AD-19 regelt den Konflikt je Feld über den alten Wert; Version ist Teil der festen Körperform aller Module.
  - `[false]` `[reject]` Blind: `FeldPatch` kopiert statt in SharedKernel — Konvention seit 3-3: Module teilen keinen Code außer SharedKernel-Bausteinen, Kopie je Modul ist so gewollt.
  - `[low]` `[reject]` Blind: Allergene als Komma-Text-Spalte — Entscheidung der Spec (Design Notes), gleiche Form wie die Allergie-Antwort im Freizeitpass; Umbau wäre neue Komplexität ohne gezeigten Schaden.
  - `[low]` `[reject]` Blind: keine Check-Constraints in der Datenbank — Hausstil aller Module (Prüfung per FluentValidation in der Pipeline); kein Schreibweg an ihr vorbei.
  - `[medium]` `[patch]` Blind: 404 beim Entfernen/Löschen zeigt nur allgemeinen Fehler, veraltete Karte bleibt — gleiche Ursache wie Edge 4/5; Patch: neu laden bzw. zur Rezeptliste.
  - `[low]` `[reject]` Blind: Bearbeiten-Karten veralten bei fremden Änderungen, 14 Häkchen je Zutat — AD-19: keine Live-Aktualisierung, Neuladen beim Öffnen; Aufklapp-Baustein existiert nicht, Allergen-Auswahl an der Zutat ist Spec-Vorgabe.
  - `[low]` `[reject]` Blind: fehlende Tests (1.000, Höchstwert über API, Basis-Änderung in bUnit, leerer Löschlauf, Demo nach Umbenennung) — 1.000 kommt mit dem Patch; Rest ist durch Validator- und HTTP-Tests der gleichen Pfade abgedeckt.
  - `[low]` `[patch]` Blind: Doku nennt Grenzen und Rundung nicht — Patch: Satz zu Dezimaltrenner, Tausenderpunkten, Grenzen und Rundung auf 2 Nachkommastellen.
  - `[low]` `[reject]` Blind: `Location` der neuen Zutat zeigt auf das Rezept — dokumentiert, es gibt bewusst keinen Einzel-GET für Zutaten; kein Client wertet `Location` aus.
  - `[low]` `[patch]` Verifikationslücke: deutsche, großschreibungsunabhängige Sortierung nicht durch Test gesichert — Patch: Testfall mit „apfelmus“ und „Äpfel“.
  - `[medium]` `[patch]` Edge: „1.000“ als 1 — gleiche Ursache wie Blind 1.
  - `[low]` `[reject]` Edge: winzige Menge rundet auf 0 (0,001 bei Basis 1000 für 1 Person) — im Küchenalltag praktisch nicht anzutreffen; Abhilfe bräuchte eine Sonderregel.
  - `[low]` `[patch]` Edge: Einzahl „1 Personen“ — gleiche Ursache wie Blind 2.
  - `[medium]` `[patch]` Edge: Zutat schon entfernt → allgemeiner Fehler, Karte bleibt — gleiche Ursache wie Blind 9.
  - `[medium]` `[patch]` Edge: Rezept schon gelöscht → Nutzer bleibt auf gelöschtem Rezept — gleiche Ursache wie Blind 9.
  - `[maybe-false]` `[defer]` Edge: Abschluss zwischen Phasenprüfung und Commit lässt einen Schreibvorgang durch — gleiches Muster in allen Modulen (SchreibschutzStufe prüft vor der Transaktion); klären würde ein Gleichzeitigkeitstest Abschluss gegen Schreiben; wenn wahr medium.
  - `[false]` `[reject]` Intent: kein HTTP-Test mit 403 — auf Routen für Leitung und Team gibt es keine falsche Rolle in der Freizeit; ohne Freizeit-Rolle liefert RechtePruefung 404 (AD-12, Org-Rollen ohne Einsicht); 403 ist hier nicht erreichbar.
  - `[false]` `[reject]` Intent: keine Token-Fälle — die Story hat keine Token-Endpunkte; AD-30 verlangt Token-Fälle nur für Token-Routen.
  - `[low]` `[reject]` Intent: Demo und Screenshots zeigen Faktor 1 (4 Personen, Basis 4) — Rechnung durch Fach-, HTTP- und bUnit-Tests belegt, Doku-Text erklärt die Hochrechnung; Änderung widerspräche der Demo-Vorgabe der Spec. Restrisiko notiert.
  - `[false]` `[reject]` Intent: Personenzahl live statt Höchstzahl oder Handwert — bewusst in den Design Notes entschieden („jede Person zählt gleich“, keine Hand-Überschreibung); kein Fehler.

### 2026-10-17 — Review pass (Folgedurchlauf)
- verdicts: 24 findings — high 0, medium 1, low 13, false 9, maybe-false 1
- findings:
  - `[low]` `[reject]` Edge: kleine hochgerechnete Menge rundet auf 0 (`KuecheWerte.Hochrechnen`) — carried: im Küchenalltag praktisch nicht anzutreffen; Abhilfe bräuchte eine Sonderregel.
  - `[false]` `[reject]` Edge: „1.250“ (Punkt mit genau 3 Nachkommastellen) wird mit 400 abgelehnt — gewollte Grenze: die Form ist im Deutschen ein Tausenderpunkt (1250) und damit mehrdeutig; Fehlertext („keine Tausenderpunkte“) und Doku („1000“ statt „1.000“) nennen sie, „1,25“ geht.
  - `[low]` `[patch]` Edge: eine alte Fehlermeldung bleibt nach späterem erfolgreichem Neuladen stehen (`Rezept.razor` `LadenAsync` leert `_meldung` nicht) — Patch: `LadenAsync` leert die Meldung nach erfolgreichem Laden; bUnit-Test `Fehlermeldung_verschwindet_nach_dem_naechsten_erfolgreichen_Neuladen`.
  - `[low]` `[patch]` Edge: Zutat schon von jemand anderem entfernt → trotzdem Fehlermeldung (`Rezept.razor` `ZutatEntfernenAsync`) — Patch: erst neu laden, Meldung nur, wenn die Zutat noch da ist; bUnit-Test um „keine Meldung“ ergänzt.
  - `[low]` `[reject]` Blind: Zustand „0 Personen“ ist mit Leitung/Team praktisch nicht erreichbar, Text „Noch niemand bestätigt …“ — Text und Zustand sind wörtlich Vorgabe der I/O-Matrix im Intent; der Zweig ist harmlos und schützt die Rechnung (Division).
  - `[false]` `[reject]` Blind: keine geplante Personenzahl vor den Bestätigungen — die Zählregel ist im Intent festgelegt, keine Hand-Überschreibung laut Design Notes.
  - `[low]` `[reject]` Blind: kleine Mengen runden auf 0 — gleiche Ursache wie Edge 1, carried.
  - `[false]` `[reject]` Blind: „1,000“ wird als 1 gespeichert — im Deutschen ist „1,000“ die Zahl 1; die Doku nennt Komma als Dezimaltrenner, der Wert wird als „1“ angezeigt und ist sofort sichtbar.
  - `[medium]` `[patch]` Blind: „Allergene: keine“ auf der Rezept-Card liest sich wie „frei von Allergenen“, obwohl nur nichts angekreuzt ist (`Anzeige.Allergene`) — Patch: Text „keine angegeben“, Doku-Satz auf `kueche/rezepte.md`, bUnit-Tests angepasst.
  - `[false]` `[reject]` Blind: `version` im PATCH ungenutzt — carried: AD-19 regelt den Konflikt je Feld über den alten Wert; feste Körperform aller Module.
  - `[low]` `[reject]` Blind: Personenzahl im Speicher entdoppelt, Überschneidung mit `IBestaetigteTeilnehmende` — carried für den Speicher-Teil; der vorhandene Zähler zählt nur Teilnehmende ohne Rollen, andere Bedeutung, kein gemeinsamer Wert, der auseinanderlaufen könnte.
  - `[low]` `[reject]` Blind: Spaltenkopf per `[1..]` aus „für …“ gebildet — heute korrekt, bUnit-Tests prüfen „Für 6 Personen“ und „Für 1 Person“ und fangen eine Textänderung ab.
  - `[low]` `[reject]` Blind: keine Check-Constraints in der Datenbank — carried: Hausstil aller Module, kein Schreibweg an der Pipeline vorbei.
  - `[low]` `[patch]` Blind: Anlege-Formular nennt den Bereich 1 bis 1000 nicht, das Bearbeiten-Feld schon (`Rezepte.razor`) — Patch: Hinweis „…, 1 bis 1000, z. B. 4“, bUnit-Test prüft ihn.
  - `[low]` `[reject]` Blind: Doku nennt feste Zutaten-Reihenfolge und doppelte Rezeptnamen nicht — kein Fehlverhalten; doppelte Namen sind erlaubt, die Reihenfolge ist in der Tabelle sichtbar.
  - `[low]` `[reject]` Blind: fachfremde Sicherheits-Updates in `Directory.Packages.props` — ohne sie scheiterte `dotnet restore` (Auto Run Result); nachträglich trennen bringt keinen Nutzen.
  - `[false]` `[reject]` Intent: kein 403-Fall nach AD-30 — carried: keine falsche Rolle in der Freizeit erreichbar, ohne Rolle 404 (AD-12).
  - `[false]` `[reject]` Intent: keine Token-Fälle — carried: keine Token-Endpunkte.
  - `[false]` `[reject]` Intent: GET liefert Mengen als JSON-Zahl statt „1,5“ — Design Notes: DTOs tragen `decimal?`; „1,5“ gilt an der Autosave-Oberfläche (PATCH-Antwort, `alterWert`, Anzeige), dort durch Tests belegt.
  - `[false]` `[reject]` Intent: Bestätigte Anmeldungen gelöschter Personen zählen einzeln — jede war eine eigene Person; einzeln zählen entspricht „jede Person zählt einmal“.
  - `[low]` `[reject]` Intent: Hinweis „Noch niemand bestätigt“ passt nicht zur Zählung mit Rollen — gleiche Ursache wie Blind 1.
  - `[false]` `[reject]` Intent: Doku-Build im Diff nicht belegt — `npm run build` lief in diesem Durchlauf fehlerfrei (56 Seiten).
  - `[low]` `[reject]` Intent: Löschlauf, Callback `Gespeichert` und Paket-Updates nicht im Intent genannt — Löschlauf folgt aus AD-16 und den Tasks, der Callback trägt „nach jedem gespeicherten Feld neu laden“, Paket-Updates siehe Blind 12.
  - `[maybe-false]` `[reject]` Intent: „Test vor dem Code“ am Diff nicht prüfbar — klären würde nur die Commit-Reihenfolge (ein Story-Commit); wenn wahr nur low, kein Fehler im Ergebnis. In diesem Durchlauf wurden die Tests nachweislich zuerst rot geschrieben.

## Design Notes

*Umfang und Zählweise*
- **Rezepte je Freizeit, nicht je Organisation.** Küche ist eine Team-Aufgabe ohne Org-Rolle. Org-Routen können den Freizeit-Schalter Verpflegung nicht tragen, und der Pfeil Küche → Organisationen fehlt (AD-2). Das einfachste Modell ist daher: Rezept gehört der Freizeit und ist Arbeitsdatum der Freizeit (AD-16). Es wird mit dem Löschlauf nach Org-Frist gelöscht.
- **Personenzahl.** „Jede Person zählt gleich“ (FR-46): bestätigte Teilnehmende plus alle Personen mit Freizeit-Rolle. Bestätigte Mitarbeitende haben über `AnmeldungBestaetigt` eine Team-Rolle, darum werden ihre Anmeldungen nicht zusätzlich gezählt. Eingegangene Anmeldungen zählen nicht, weil sie keine Zusage sind.
  - Die Zahl liefert eine schmale Leseschnittstelle in `Freizeiten.Contracts`. Der Küche-Endpunkt bleibt so für das Team nutzbar, denn die Anmeldungen-Liste ist nur für die Leitung.
- **Keine Hand-Überschreibung der Personenzahl** (simpel halten). Die Küche passt bei Bedarf die Basis an.

*Datenformen*
- **Zahlen als Text im PATCH** (AD-19), serverseitig normalisiert: deutsch mit Komma, ohne Tausenderpunkte und ohne Null-Nachkommastellen. So bleibt der `alterWert`-Vergleich stabil. In DTOs stehen Zahlen als `decimal?`/`int`.
- **Allergene als Komma-Liste** mit denselben Schlüsseln wie die Allergie-Frage. 4-2 kann so ohne Umrechnung abgleichen.

*Rechte*
- **Keine 403- und Token-Fälle.** Alle Routen erlauben Leitung und Team, es gibt in der Freizeit keine falsche Rolle; Konvention seit 3-1. Es gibt keine Token-Endpunkte. Die Pflichtfälle nach AD-30 decken 404 bei fremder Organisation und ohne Rolle sowie 401 ab.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.Kueche.Tests` und `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*Rezepte*"` – während der Arbeit grün.
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` – grün.
- Am Ende einmal `dotnet test` (je Projekt, falls Zeitlimit), inklusive `tests/Zeltlotse.E2E.Tests` – alles grün, Screenshots erzeugt, Rauschen zurückgesetzt.
- `npm run build` in `doku/` – fehlerfrei.

## Auto Run Result

Status: done

**Umgesetzt:**
- Rezepte je Freizeit im Modul Küche: Name, Basis-Personenzahl, Zubereitung und Zutaten (Menge, Einheit, Bezeichnung, Allergene aus den 14 EU-Hauptallergenen).
- Hochrechnung auf die Personenzahl der Freizeit: bestätigte Teilnehmende plus Leitung und Team, jede Person einmal. Die Zahl kommt aus der neuen Schnittstelle `IFreizeitPersonenzahlLesen`.
- 8 Routen unter `/api/freizeiten/{id}/kueche/rezepte…`. Sie gelten für Leitung und Team und hängen am Schalter Verpflegung. Nach dem Abschluss sind sie schreibgeschützt.
- Löschlauf nach Org-Frist.
- Oberfläche: Seiten „Rezepte“ und „Rezept“ mit Autosave und Knopf „Küche“ auf der Startseite.
- Dazu Doku-Seite, Demo-Rezepte und Screenshots.

**Dateien (Auswahl):**
- `src/Modules/Kueche/Zeltlotse.Kueche/` – Entitäten `Rezept` und `Zutat`, `KuecheWerte` (Prüfung, Normalisierung, Hochrechnung), 8 Slices, `KuecheLoeschlauf`, Migration `Rezepte`.
- `src/Modules/Freizeiten/…Contracts/Personen/IFreizeitPersonenzahlLesen.cs` und `Freizeiten/Rechte/FreizeitPersonenLesen.cs` – die Personenzahl.
- `src/Zeltlotse.Api.Contracts/Kueche/Rezepte.cs` – Datenformen, Felder und die Kopie der Allergene.
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs` – der Client. `Autosave/AutosaveAuswahlgruppe.razor` hat einen neuen Callback `Gespeichert`.
- `src/Zeltlotse.App/Features/Kueche/Rezepte.razor`, `Rezept.razor`, `KuecheTexte.cs` und `Features/Freizeiten/FreizeitStart.razor` (Knopf „Küche“).
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs` – Chili sin Carne und Pfannkuchen.
- Doku: `doku/src/content/docs/kueche/rezepte.md` ist neu, `kueche/index.md`, `leitung/module-schalten.md` und `daten.md` sind ergänzt. Neue Screenshots `rezepte-*` und `rezept-*`.
- Tests:
  - neues Projekt `tests/Zeltlotse.Kueche.Tests` (59 Tests);
  - neu `tests/Zeltlotse.Api.Tests/RezepteTests.cs` (24) und `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs` (22);
  - erweitert: `EndpunktListeTests`, `DemoDatenTests`, `DokuScreenshotTests`, `DokuRegeln`/`DokuTests`.
- `Directory.Packages.props` – Sicherheits-Updates, ohne die `dotnet restore` scheiterte:
  - Blazor-Pakete auf 10.0.13;
  - `System.Security.Cryptography.Pkcs` 10.0.13 festgelegt;
  - `MessagePack` 2.5.303 festgelegt.

**Review:**
- 24 Funde. Davon sind 6 Patches angewendet:
  - „1.000“ wird als Tausenderpunkt abgelehnt;
  - Einzahl „1 Person“;
  - 404 beim Löschen lädt neu bzw. führt zur Liste;
  - Demo-Suche wirft nicht mehr;
  - Sortiertest mit „apfelmus“ und „Äpfel“;
  - Doku nennt Grenzen und Rundung.
- 1 Fund ist zurückgestellt: Abschluss gegen gleichzeitiges Schreiben (gleiches Muster in allen Modulen).
- Die übrigen sind mit Begründung im Triage-Log verworfen.
- Gepatcht nach Bewertung: medium 2 Einträge, low 4.

**Nachprüfung empfohlen:** ja. Zwei Medium-Patches sind nur gezielt getestet:
- Die Ablehnung der Tausenderpunkt-Form lehnt jetzt auch „1.250“ ab, gemeint als 1,25 mit Punkt. Das ist eine bewusste, aber neue Grenze.
- Der 404-Pfad beim Löschen in `Rezept.razor` ist nur per bUnit belegt.

**Verifikation:**
- Nach den Patches lief alles neu, mit 0 Fehlern:
  - `dotnet build Zeltlotse.slnx`: 0 Fehler;
  - Kueche 59, Ui 685, Architektur 135, Freizeiten 93, SharedKernel 111, Konten 26, Personen 153, Organisationen 58, Programm 91, Inventar 34, Sicherheit 36, Abläufe 10, Api 846 (42 min), E2E inklusive Doku-Screenshots 165.
- 41 neu geschriebene Bilder zeigten im Pixelvergleich nur Rauschen (Ports, Tokens, Kantenglättung). Sie sind auf HEAD zurückgesetzt.
- `npm run build` in `doku/`: 56 Seiten, fehlerfrei.

**Restrisiken:**
- Die Demo verpflegt 4 Personen, beide Rezepte sind für 4. Die Screenshots zeigen darum keine abweichende Hochrechnung. Die Rechnung ist durch Tests belegt.
- Die Paket-Updates heben die Blazor-Pakete eine Patch-Version über die übrigen 10.0.12-Pakete.
- Die Abschluss-Race ist zurückgestellt (siehe `deferred`).

### Folgedurchlauf 2026-10-17

**Umgesetzt (Patches aus dem Folge-Review):**
- Rezept-Card zeigt ohne angekreuzte Allergene „Allergene: keine angegeben“ statt „keine“. So liest sich das nicht wie „frei von Allergenen“. Dazu ein Satz in der Doku.
- Rezeptseite: Eine Fehlermeldung verschwindet nach dem nächsten erfolgreichen Neuladen. Ist eine Zutat beim Entfernen schon weg, erscheint keine Meldung mehr.
- Das Anlege-Formular nennt den Bereich der Basis-Personenzahl („1 bis 1000, z. B. 4“).

**Dateien:**
- `src/Zeltlotse.Ui/Texte/Anzeige.cs` – Text „keine angegeben“.
- `src/Zeltlotse.App/Features/Kueche/Rezept.razor` – Meldung leeren nach erfolgreichem Laden; Meldung beim Entfernen nur, wenn die Zutat noch da ist.
- `src/Zeltlotse.App/Features/Kueche/Rezepte.razor` – Hinweis mit Bereich.
- `doku/src/content/docs/kueche/rezepte.md` – Satz zu „keine angegeben“.
- `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs` – Tests zuerst angepasst (rot), ein neuer Test `Fehlermeldung_verschwindet_nach_dem_naechsten_erfolgreichen_Neuladen`.

**Review:**
- 24 Funde, die Verifikationslücken-Prüfung fand keine.
- 4 Einträge gepatcht: medium 1, low 3.
- Nichts neu zurückgestellt; die übrigen Funde sind mit Begründung im Triage-Log verworfen, 8 davon aus dem ersten Durchlauf übernommen (carried).

**Nachprüfung empfohlen:** nein. Der Folgedurchlauf hat keinen High-Fund gepatcht, die Arbeit ist konvergiert.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.Ui.Tests`: zuerst 5 erwartete Fehlschläge, nach dem Patch 686 grün.
- `dotnet test Zeltlotse.slnx` (alle Projekte inklusive Api, Architektur und E2E mit Doku-Screenshots): 2503 Tests, 0 Fehler.
- `npm run build` in `doku/`: 56 Seiten, fehlerfrei.
- Die `rezepte-*`/`rezept-*`-Bilder sind unverändert, weil der neue Hinweis außerhalb des Bildausschnitts liegt. 40 weitere Bilder anderer Seiten waren reines Rendering-Rauschen und sind auf HEAD zurückgesetzt.

**Restrisiken:** wie im ersten Durchlauf: die Demo-Hochrechnung hat Faktor 1, und das Abschluss-Rennen ist zurückgestellt.
