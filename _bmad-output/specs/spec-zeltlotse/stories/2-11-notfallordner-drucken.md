---
title: 'Notfallordner drucken'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '783ed0f3faaac09fe17d3e259ea288453086c5bf'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-8-notfall-sicherheitskette-und-schnellsuche.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Ohne Netz gibt es keine Rückfallebene: Zeltlotse kann den Notfallordner (Notfallkontakte und relevante Freizeitpass-Angaben je Kind) nicht drucken, obwohl Notfall-Box und Doku schon auf ihn verweisen (CAP-18, FR-33, NFR-11).

**Approach:**
- Modul Personen (Besitzer der Pässe, AD-8) liefert gebündelt die Pässe einer Menge von Personen einer Freizeit, nur für bestätigte, sichtbare Freigaben, mit Einsicht-Protokoll je Person.
- Web-App: Druckansicht `/freizeiten/{id}/notfallordner`, je bestätigtem Kind eine A4-Seite über Druck-CSS (keine PDF-Bibliothek), erreichbar von „Sicherheitskette“ und „Freizeitpässe“.
- Dazu HTTP-, bUnit-, E2E-Tests (Druckmedium, PDF-Seitenzahl), Demo-Ergänzung, Doku und Screenshots.

## Boundaries & Constraints

**Always:**
- **Datenform** `src/Zeltlotse.Api.Contracts/Personen/Notfallordner.cs`: `NotfallordnerPerson(Guid PersonId, string Vorname, string Nachname, IReadOnlyList<FreizeitpassFrage> Fragen)`.
- **Endpunkt** (Modul Personen, Datei `Freizeitpass/Notfallordner.cs`, registriert neben der Einsicht in `PersonenModul.cs`): `GET /api/freizeiten/{freizeitId}/personen/notfallordner?person={id}&person={id}…`, Recht `.NurFreizeitRolle(FreizeitpassRechte.Einsicht)` (Leitung, Team).
  - Rolle der lesenden Person aus `db.FreizeitRollen` wie `FreizeitpassEinsichtEndpunkt` (keine Zeile → `404`).
  - Kein `person` oder mehr als 200 Kennungen → `400`. Doppelte Kennungen zählen einmal.
  - Je Kennung zählt die jüngste Freigabe (gleiche Sortierung wie die Einsicht). Aufgenommen wird eine Person nur, wenn diese Freigabe Stufe `Team` hat (= bestätigt) und `GueltigBis > jetzt`. Alle anderen Kennungen fallen still weg (kein Fehler).
  - `Fragen` = nur die Fragen der Freigabe, in Katalogreihenfolge, über `FreizeitpassAbfrage.FragenAsync`.
  - Je aufgenommener Person `EinsichtProtokollieren(…, EinsichtZweck.FreizeitpassEingesehen)`, außer für die lesende Person selbst. Kein neuer Zweck.
  - `200` mit Liste, sortiert nach Nachname, Vorname; leer ist erlaubt.
  - Die Rechte-Logik (Rolle, jüngste Freigabe, Zustand) wird mit der Einsicht geteilt (gemeinsame Hilfsmethode), nicht kopiert.
- **Ui-Client:** `PersonenApiClient.NotfallordnerLadenAsync(freizeitId, personIds)` (403/404 → `null`).
- **Komponenten (Ui.Komponenten, bUnit-Tests):**
  - `DruckSeite.razor` (`Dachzeile`, `Titel`, `ChildContent`): `<section class="zl-druck-seite">` mit `Dachzeile` und `<h2>`.
  - `Infokasten.razor` (`Titel`, `ChildContent`): umrandeter Kasten mit `<h3>`, Information nie nur über Farbe.
- **CSS (`zeltlotse.css`, Abschnitt 5 und Bildschirm-Regeln dazu):**
  - `@page { size: A4; margin: 15mm; }`.
  - Druck: `.zl-nur-bildschirm` ausgeblendet; `.zl-druck-seite` `break-inside: avoid`, ab der zweiten `break-before: page`; Schrift 10,5 pt, Titel 26 pt, Kastentitel 15 pt (DESIGN.md `print-*`); schwarz auf weiß, Umrandung schwarz.
- **Seite `Features/Freizeiten/Notfallordner.razor`** (`/freizeiten/{FreizeitId:guid}/notfallordner`, Guard `Darf(Aktionsschluessel.FreizeitpaesseSehen)`, sonst `NichtVerfuegbar`):
  - Lädt Kontext, Freizeit (Name) und Personenliste parallel, filtert `Art` Teilnehmende und `Status` Bestätigt, lädt dann den Notfallordner für diese Kennungen.
  - Kopf in `zl-nur-bildschirm`: Dachzeile „Notfall“, h1 „Notfallordner“, Satz „Je Kind eine Seite mit Notfallkontakten und den Angaben aus dem Freizeitpass. Ausdrucke nach der Freizeit vernichten.“, `Knopf` „Drucken“ (`IBrowser.DruckenAsync`), Link „Zurück zur Sicherheitskette“.
  - Je Person eine `DruckSeite`: Dachzeile = Name der Freizeit, Titel = `Anzeige.Name`. Darin `Infokasten` „Notfallkontakte“ mit `Anzeige.Antwort` der Frage `notfallkontakte` (fehlt die Frage, fehlt der Kasten), darunter `DetailsListe` aller übrigen Fragen (`Frage`, `Anzeige.Antwort`).
  - Keine bestätigten Teilnehmenden oder leere Antwort → `LeerZustand` „Noch keine bestätigten Teilnehmenden mit sichtbarem Freizeitpass.“ ohne Drucken-Knopf.
  - Notfallordner-Abruf scheitert → Fehlermeldung wie auf den Nachbarseiten.
- **Einstiege:** `Sicherheitskette.razor` und `Freizeitpaesse.razor` bekommen je einen `Knopf` „Notfallordner drucken“ (`Ansichtswechsel`) auf `freizeiten/{id}/notfallordner`, nur bei `Darf(FreizeitpaesseSehen)`.
- **Demo:** Mia Probst erhält zusätzlich `medikamente` = „Asthmaspray bei Bedarf, liegt im Kulturbeutel“ (`DemoKatalog`, über den vorhandenen Antwort-Schritt, idempotent).
- Test vor dem Code. API nur additiv. Neue Route in `EndpunktListeTests`.

**Never:**
- Keine PDF-Bibliothek, kein serverseitiges PDF, keine gespeicherte Kopie der Antworten, kein neues Modul, kein neues Ereignis, kein neuer Aktionsschlüssel, kein neuer Einsicht-Zweck.
- Kein Druck für Mitarbeitende oder nicht bestätigte Anmeldungen; keine Team-Notizen im Ordner.
- Keine weiteren Druckansichten (umfangsgrenzen), keine Notfallkarte/Offline-Speicherung (V6), keine Pop-ups, keine `elkw-`/`zl-`-Klassen außerhalb `Ui.Komponenten`, keine Änderung an `ARCHITECTURE-SPINE.md`. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Leitung druckt | X, Emma bestätigt mit Antworten; Miriam GET `person=Emma` | `200`, ein Eintrag mit Namen und genau den Fragen der Freigabe; ein Protokolleintrag Zweck 2 | – |
| Team druckt | Tobias, Emma bestätigt | `200`, Emma enthalten; Protokoll für Tobias | – |
| Nicht bestätigt | Emma nur eingegangen; Miriam und Tobias | je `200` leer, kein Protokoll | – |
| Gemischt | Emma bestätigt, Mia eingegangen, fremde Kennung; `person` doppelt | nur Emma, einmal | – |
| Fenster vorbei / Absage | Uhr nach `FreigabeEnde`; Emma abgesagt | `200` leer | – |
| Protokoll je Tag | zwei Abrufe am selben Tag | ein Eintrag je Person | – |
| Eigene Person | Leitung fragt eigene Kennung mit an | kein Protokoll für sich selbst | – |
| Eingabefehler | kein `person`; 201 Kennungen; ungültige GUID | `400` | – |
| Rechte | Andrea (Org-Admin ohne Rolle); Leitung nur von Z auf X; fremde Organisation; ohne Sitzung | `404` / `404` / `404` / `401` | – |
| Seite | Lea, Demo | Kopf, Knopf „Drucken“, eine Seite „Mia Probst“ mit Kasten „Notfallkontakte“ „Daniel Probst (Vater), 0123 456781“, Liste mit Allergien „Gluten“, Medikamente | – |
| Seite leer | keine bestätigten Teilnehmenden | Leer-Zustand, kein Drucken-Knopf | – |
| Ohne Recht / Fehler | Kontext ohne `freizeitpaesse-sehen`; Abruf 5xx | `NichtVerfuegbar` / Fehlermeldung | – |
| Drucken | Klick „Drucken“ | `IBrowser.DruckenAsync` einmal | – |
| Druckmedium | Demo im Druckmedium | App-Leiste, Kopf und Knöpfe unsichtbar; PDF (A4) hat genau 1 Seite | – |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs:21-31,43-110` -- `FreizeitpassRechte.Einsicht`, Rollenermittlung Z.48-49, jüngste Freigabe Z.50-56, Zustand Z.66-70, Fragen Z.74-76, Protokoll Z.77-81, `Rolle()`-Abbildung. Vorlage und Quelle der zu teilenden Logik.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEndpunkte.cs:31-56` -- `FreizeitpassAbfrage.FragenAsync`, `PersonId(angemeldet)`.
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:96-104` -- Gruppe `/freizeiten/{freizeit}/personen`, Registrierung der Einsicht mit `NurFreizeitRolle`.
- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs` -- `FreigabeStufe.Team`, `GueltigBis`, `Fragen`.
- `src/Modules/Personen/Zeltlotse.Personen/Zugriffsprotokoll/EinsichtProtokoll.cs:11-17,54-71` -- `EinsichtZweck.FreizeitpassEingesehen`, Command `EinsichtProtokollieren` (Dedup je Tag).
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs:57-116` -- Kennung `notfallkontakte`, Reihenfolge.
- `src/Zeltlotse.Api.Contracts/Personen/{FreizeitpassEinsicht.cs:31-38,Freizeitpass.cs:39-48}` -- `FreizeitpassFrage`.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs:548` -- `FreizeitpassEinsichtLadenAsync` als Muster; `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs:272` -- `PersonenlisteLadenAsync`; `src/Zeltlotse.Api.Contracts/Freizeiten/Personenliste.cs` -- `Art`, `Status`.
- `src/Zeltlotse.Ui/Texte/Anzeige.cs:141-170` -- `Antwort(FreizeitpassFrage)`, `Name`.
- `src/Zeltlotse.App/Features/Freizeiten/{FreizeitpassEinsicht.razor:58-67,119,132-140,Freizeitpaesse.razor:19-53}` -- Guard, paralleles Laden, `DetailsListe`-Abbildung; Einstieg nach Z.20.
- `src/Zeltlotse.App/Features/Sicherheit/Sicherheitskette.razor` -- Einstieg unter Card „Wen anrufen“.
- `src/Zeltlotse.App/Features/Konten/MeineDaten.razor:65,157` und `src/Zeltlotse.App/Browser/IBrowser.cs:24` -- Druck-Knopf-Muster.
- `src/Zeltlotse.Ui.Komponenten/{DetailsListe.razor,Knopf.razor,LeerZustand.razor,Dachzeile.razor,wwwroot/css/zeltlotse.css:167-178}` -- Bausteine; einziger `@media print`-Block.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs:44-61` -- Antworten von Emma/Mia; Mia ist die einzige bestätigte Teilnehmende.
- Tests:
  - `tests/Zeltlotse.Api.Tests/{FreizeitpassEinsichtTests.cs:25-54,157,182,FreizeitpassSzenario.cs}` -- Szenario (Miriam, Tobias, Andrea, Lea von Z, Katrin mit Emma/Mia), `KindAnmeldenAsync`, `BestaetigenAsync`, `AbsagenAsync`, `FreigabeEnde`, `Factory.Uhr`, SQL-Prüfung `personen.einsicht_protokoll`; `EndpunktListeTests.cs` (`freizeitRouten`); `DemoDatenTests.cs`.
  - `tests/Zeltlotse.Ui.Tests/{FreizeitpaesseSeitenTests.cs,SicherheitsketteSeitenTests.cs,MeineDatenSeitenTests.cs:155-168,NachgebildeterBrowser.cs:16,69-73,KomponentenTests.cs}` -- bUnit-Muster, `Gedruckt`.
  - `tests/Zeltlotse.E2E.Tests/{DokuScreenshotTests.cs:21,1019-1070,1524-1560,1639-1660,E2EHilfe.cs:80}` -- Theory Handy/Laptop, `TeamMail` (Lea), `FreizeitpaesseAsync`, `AxeAsync`, `BildAsync`.
- Doku: `doku/src/content/docs/mitarbeitende/{index,notfall,freizeitpaesse}.md`, `leitung/index.md`; `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-24,49,75` (`PflichtSeiten`, jedes PNG verlinkt, keine Anrede).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Api.Tests/NotfallordnerTests.cs` (neu) -- ZUERST: jede API-Zeile der Matrix gegen PostgreSQL (AD-30), Protokoll per SQL; dazu `EndpunktListeTests` und `DemoDatenTests` (Mias Medikamente).
- `tests/Zeltlotse.Ui.Tests/NotfallordnerSeitenTests.cs` (neu) -- ZUERST: Seite, Filter Teilnehmende/Bestätigt (Abruf nur mit diesen Kennungen), Kasten fehlt ohne Frage, leer, `NichtVerfuegbar`, 5xx, Drucken; Einstiege in `SicherheitsketteSeitenTests` und `FreizeitpaesseSeitenTests`; `KomponentenTests` für `DruckSeite`, `Infokasten`.
- `src/Zeltlotse.Api.Contracts/Personen/Notfallordner.cs` -- Datenform.
- `src/Modules/Personen/Zeltlotse.Personen/{Freizeitpass/Notfallordner.cs,Freizeitpass/FreizeitpassEinsicht.cs,PersonenModul.cs}` -- Endpunkt, geteilte Logik, Registrierung.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs` -- Client-Methode.
- `src/Zeltlotse.Ui.Komponenten/{DruckSeite.razor,Infokasten.razor,wwwroot/css/zeltlotse.css}` -- Bausteine und Druck-CSS.
- `src/Zeltlotse.App/Features/{Freizeiten/Notfallordner.razor,Freizeiten/Freizeitpaesse.razor,Sicherheit/Sicherheitskette.razor}` -- Seite und Einstiege.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs` -- Mias Medikamente.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theory `Notfallordner` (Lea, über „Freizeitpässe“ → „Notfallordner drucken“, axe, Bild `notfallordner` je Gerät); danach `EmulateMediaAsync(Print)`: App-Leiste und Knopf „Drucken“ unsichtbar, `PdfAsync` A4 hat genau eine Seite. `freizeitpaesse` und `sicherheitskette` laufen mit.
- Doku: neu `doku/src/content/docs/mitarbeitende/notfallordner.md` („Notfallordner drucken“: Einstiege, je Kind eine Seite, nur bestätigte Teilnehmende mit sichtbarem Pass, Inhalt, Drucken über den Browser, schwarz-weiß, Einsicht steht im Zugriffsprotokoll der Eltern, Ausdrucke nach der Freizeit vernichten; Bild `notfallordner`); in `DokuRegeln.PflichtSeiten`. Anpassen: `mitarbeitende/index.md` (Punkt), `mitarbeitende/notfall.md` und `freizeitpaesse.md` (Verweis), `leitung/index.md` (Link).

**Acceptance Criteria:**
- Given Lea (Team, Demo) auf der Startseite, when sie „Freizeitpässe“ und dann „Notfallordner drucken“ wählt, then sieht sie in zwei Schritten je bestätigtem Kind eine Seite mit Notfallkontakten und den Pass-Angaben.
- Given die Druckansicht, when der Browser druckt, then entsteht je Kind eine A4-Seite ohne App-Leiste, Kopf und Knöpfe, lesbar in Schwarz-Weiß.
- Given ein Teammitglied hat den Notfallordner geladen, when die Eltern ihr Zugriffsprotokoll öffnen, then steht dort die Einsicht in den Freizeitpass ihres Kindes.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün (inklusive Architektur- und E2E-Tests mit axe), `notfallordner-handy.png` und `notfallordner-laptop.png` sind geschrieben und verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 32 findings — high 0, medium 0, low 22, false 7, maybe-false 3
- findings:
  - `[low]` `[patch]` (blind) 200 Kennungen sprengen die Request-Zeile (~8,8 kB > 8192 bei Kestrel/HTTP 1.1) — Patch: `HoechstensKennungen` auf 150, Grenztest 150/151.
  - `[low]` `[reject]` (blind) Client teilt große Listen nicht auf — erst ab über 150 bestätigten Kindern; Aufteilen wäre eine zusätzliche Schleife mit Zusammenführen.
  - `[low]` `[reject]` (blind) Server beschränkt nicht auf Teilnehmende — Personen kennt die Art der Anmeldung nicht (AD-2/AD-3, Design Notes); Mitarbeitende-Pässe sieht dieselbe lesende Person ohnehin über die Einsicht, nur ein direkter API-Aufruf wäre betroffen.
  - `[low]` `[reject]` (blind) N+1 Abfragen und Protokoll-Schreibvorgänge — bei wenigen Dutzend Kindern unkritisch; Bündeln wäre neue Abfragelogik.
  - `[low]` `[reject]` (blind) Globales `@page` A4/15 mm wirkt auch auf die Datenauskunft — A4 mit Rand ist dort ebenso passend; ein benannter Seitentyp wäre zusätzliche Druck-Logik.
  - `[low]` `[patch]` (blind) Seitenumbruch zwischen Kindern nie gerendert geprüft — Patch: E2E klont die Druckseite und erwartet 2 PDF-Seiten.
  - `[low]` `[reject]` (blind) Überlange Angaben eines Kindes passen nicht auf eine Seite — selten; der Browser bricht dann normal um, eine Kürzung wäre Speziallogik.
  - `[low]` `[patch]` (blind) Kein Hinweis, wann gedruckt werden soll; kein Datum — Patch: Doku-Satz „vor der Abfahrt drucken, nach Änderungen neu drucken“; Datum bleibt bewusst weg (Design Notes, Screenshots).
  - `[low]` `[reject]` (blind) Zurück-Link führt immer zur Sicherheitskette — so im Spec festgelegt; Browser-Zurück funktioniert, ein herkunftsabhängiger Link wäre neue Logik.
  - `[low]` `[reject]` (blind) Kein „Erneut versuchen“ nach Ladefehler — gleiches Verhalten wie alle Nachbarseiten; Neuladen behebt es.
  - `[low]` `[reject]` (blind) Kasten zeigt „keine Angabe“ ohne Hervorhebung — Verhalten laut Spec (`Anzeige.Antwort`); eine Warnung wäre neue Oberfläche.
  - `[false]` `[reject]` (blind) Dunkelmodus-Kontrast der Druckseite — `--elkw-white` wird im Dunkelmodus zur dunklen Fläche (`zeltlotse.css:30`), Text-Token folgt; gleiches Muster wie Cards.
  - `[low]` `[reject]` (blind) Sortierung mit `StringComparer.CurrentCulture` — Konvention des ganzen Codes (z. B. `PersonenlisteEndpunkt.cs:49`); eine Abweichung nur hier wäre inkonsistent.
  - `[low]` `[reject]` (intent a) Ausschluss von Mitarbeitenden nur im Client — wie oben (blind, Server beschränkt nicht).
  - `[low]` `[patch]` (intent b) Druck-Treue nur als CSS-Text und mit einem Kind geprüft — wie oben, E2E mit zwei Druckseiten; Schwarz-Weiß bleibt durch CSS-Werte und Kasten-Umrandung belegt.
  - `[false]` `[reject]` (intent c) AD-30 „falsche Rolle 403“ fehlt — es gibt keine weitere Freizeit-Rolle; Leitung und Team dürfen beide (Design Notes, wie 2-5); alle übrigen Fälle sind getestet.
  - `[false]` `[reject]` (intent d) Alle abgefragten Fragen statt Auswahl — bewusste Entscheidung (Design Notes, NFR-7), kein Fehler.
  - `[false]` `[reject]` (intent e) Team darf drucken — `rollen-und-rechte.md` behält den Druck nicht der Leitung vor (Design Notes).
  - `[false]` `[reject]` (intent f) Test zuerst und Doku-Build nicht im Diff sichtbar — Doku-Build läuft in der Verifikation fehlerfrei.
  - `[false]` `[reject]` (intent g) Ordner ist online zu öffnen — Rückfallebene ist der Ausdruck; Offline-Modus ist V6 (Spec).
  - `[low]` `[patch]` (verification-gap) Seitenumbruch zwischen Kindern ungeprüft — wie oben (blind).
  - `[low]` `[patch]` (verification-gap) „Jüngste Freigabe zählt“ am Ordner ungetestet — Patch: API-Test mit älterer Team-Freigabe und neuer Leitung-Freigabe, Person fehlt, kein Protokoll.
  - `[low]` `[reject]` (verification-gap) Über 200 Kinder zeigen nur den Fehler — wie oben (blind, Client teilt nicht auf).
  - `[maybe-false]` `[patch]` (verification-gap) 200-Grenze auf echtem Server nicht erreichbar — mit HTTP/2 vermutlich nicht betroffen, nicht reproduziert; Grenze vorsorglich auf 150 (wie blind).
  - `[low]` `[reject]` (edge) Mehr als 200 bestätigte Teilnehmende — wie oben.
  - `[maybe-false]` `[patch]` (edge) Request-Zeile ab ~185 Kennungen — wie oben, Grenze 150.
  - `[maybe-false]` `[patch]` (edge) Test „200 Kennungen → 200“ nur gegen TestServer — Grenztest auf 150/151 umgestellt.
  - `[low]` `[reject]` (edge) Mitarbeitende-Kennungen liefern Pässe — wie oben (blind).
  - `[low]` `[patch]` (edge) Protokolleinträge vor dem vollständigen Zusammenbau — Patch: erst alle Pässe laden, dann protokollieren.
  - `[low]` `[reject]` (edge) Globales `@page` — wie oben (blind).
  - `[low]` `[reject]` (edge) `break-inside: avoid` bei überlangem Inhalt — wie oben (blind).
  - `[false]` `[reject]` (edge) Doku behauptet „Mitarbeitende fehlen“ — die Doku beschreibt den Notfallordner der Web-App, und der zeigt nur bestätigte Teilnehmende (bUnit `Ruft_den_Ordner_nur_fuer_bestaetigte_Teilnehmende_ab`).

## Design Notes

- **Wer druckt:** CAP-18 nennt die Leitung, die der Leitung vorbehaltene Liste (`rollen-und-rechte.md`) nennt den Druck nicht, und das Team sieht ohnehin alle Pässe im Freigabefenster. Deshalb dürfen Leitung und Team drucken (gleiche Rechteliste wie die Einsicht, kein neuer Schlüssel).
- **Gebündelter Abruf mit Kennungen vom Client:** Personen kennt die Art der Anmeldung nicht und darf Freizeiten nicht lesen (AD-2/AD-3). Der Client liefert die Kennungen der bestätigten Teilnehmenden aus der Personenliste; Personen prüft selbst Stufe `Team` und Fenster (AD-8 „Mengen statt Einzelabrufe“). So landen keine Mitarbeitenden-Pässe im Protokoll der falschen Personen.
- **Protokoll je Person:** Der Ausdruck ist eine Einsicht in den Inhalt (AD-8 Protokoll); Papier lässt sich nicht löschen, darum steht die Einsicht im Protokoll der Eltern, dedupliziert je Tag. Kein eigener Zweck (einfachste Lösung).
- **„Relevante Angaben“ = alle abgefragten Fragen:** Die Leitung wählt die Fragen schon nach Bedarf (NFR-7); eine zweite Auswahl wäre Speziallogik. Notfallkontakte stehen als Kasten oben (DESIGN.md).
- **Kein Datum auf dem Ausdruck:** Ein Tagesdatum würde die Screenshots bei jedem Lauf ändern; die Freizeit steht in der Dachzeile.
- **AD-30:** Falsche Rolle `403` hat keinen Gegenstand (Leitung und Team dürfen beide, wie in 2-5); Org-Admin ohne Rolle, andere Freizeit und fremde Organisation `404`; Token-Fälle entfallen, kein Endpunkt nutzt ein Token.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün inklusive Architektur, E2E und Screenshots
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seite `mitarbeitende/notfallordner/` in `doku/dist/`

## Auto Run Result

Status: done

**Umsetzung (CAP-18, FR-33, NFR-11):**
- **Modul Personen:**
  - Neuer Endpunkt `GET /api/freizeiten/{id}/personen/notfallordner?person=…` für Leitung und Team.
  - Er liefert die Pässe der genannten Personen, aber nur bei bestätigter, gültiger jüngster Freigabe und nur mit den abgefragten Fragen.
  - Erst wenn der ganze Ordner geladen ist, entsteht je Person ein Eintrag im Zugriffsprotokoll („Freizeitpass eingesehen“, einmal am Tag).
  - Höchstens 150 Kennungen je Abruf.
  - Die Rechte-Logik teilt der Endpunkt mit der Einsicht (`FreizeitpassZugriff`).
- **Web-App:**
  - Druckansicht `/freizeiten/{id}/notfallordner`: je bestätigtem Kind eine Seite mit Dachzeile (Freizeit), Name, Kasten „Notfallkontakte“ und den übrigen Angaben.
  - Knopf „Drucken“ öffnet den Druckdialog des Browsers.
  - Einstiege „Notfallordner drucken“ auf „Freizeitpässe“ und „Sicherheitskette“.
  - Druck-CSS: A4, 15 mm Rand, ab dem zweiten Kind neue Seite, schwarz auf weiß, 10,5/26/15 pt.
- **Demo:** Mia hat zusätzlich Medikamente.

**Dateien:**
- `src/Zeltlotse.Api.Contracts/Personen/Notfallordner.cs` – Datenform und Grenze 150.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/{Notfallordner.cs,FreizeitpassEinsicht.cs}`, `PersonenModul.cs` – Endpunkt, geteilte Rechte-Logik, Registrierung.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs` – `NotfallordnerLadenAsync`.
- `src/Zeltlotse.Ui.Komponenten/{DruckSeite,Infokasten,NurBildschirm}.razor`, `wwwroot/css/zeltlotse.css` – Druckbausteine und Druck-CSS. `NurBildschirm` ist nicht im Spec aufgeführt. Es ist nötig, weil `zl-`-Klassen nur in Ui.Komponenten erlaubt sind.
- `src/Zeltlotse.App/Features/{Freizeiten/Notfallordner,Freizeiten/Freizeitpaesse,Sicherheit/Sicherheitskette}.razor` – Seite und Einstiege.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` – Mias Medikamente.
- Tests:
  - Api: neu `NotfallordnerTests` (13 Fälle), erweitert `EndpunktListeTests` und `DemoDatenTests`.
  - Ui: neu `NotfallordnerSeitenTests`, erweitert `FreizeitpaesseSeitenTests`, `SicherheitsketteSeitenTests` und `KomponentenTests`.
  - E2E: Theory `Notfallordner` mit axe, Bild und Druckmedium, PDF mit 1 bzw. 2 Seiten. `Freizeit_Start` erwartet bei Mia jetzt „4 von 8“.
  - Architektur: `DokuRegeln`, `DokuTests`.
- Doku:
  - Neu `mitarbeitende/notfallordner.md`, Links in `mitarbeitende/{index,notfall,freizeitpaesse}.md` und `leitung/index.md`.
  - Bilder neu: `notfallordner-handy/-laptop`.
  - Mit echter Änderung neu erzeugt: `freizeitpaesse*` (Knopf), `freizeitpass-einsicht*` (Mias Medikamente), `freizeitpass-erinnern-laptop` (Cockpit-Zählung).

**Review (32 Befunde: high 0, medium 0, low 22, false 7, maybe-false 3):**
- Gepatcht (5 Einträge, alle low bzw. maybe-false):
  - Grenze 150 statt 200 wegen der Länge der Anfragezeile.
  - Protokoll erst nach dem vollständigen Zusammenbau.
  - API-Test „jüngste Freigabe zählt“.
  - E2E prüft den Seitenumbruch mit zwei Druckseiten im PDF.
  - Doku-Satz „vor der Abreise drucken, nach Änderungen erneut drucken“.
- Zurückgestellt: nichts.
- Verworfen, die Begründungen stehen im Review Triage Log:
  - Kein Ausschluss von Mitarbeitenden auf dem Server (AD-2/AD-3).
  - Keine Aufteilung der Abrufe bei mehr als 150 Kindern.
  - N+1-Abfragen.
  - Globales `@page`.
  - Überlange Angaben.
  - Kein Datum auf dem Ausdruck.
  - Zurück-Link.
  - Kein „Erneut versuchen“.
  - Kasten mit „keine Angabe“.
  - Dunkelmodus (false).
  - Sortier-Kultur.
  - AD-30 403 ohne Gegenstand (false).
  - Lesarten Fragenumfang, Druckrecht und Offline (false).

**Folge-Review empfohlen:** nein. Gepatcht wurden high 0 und medium 0; low und maybe-false zusammen 5 Einträge.

**AD-30:**
- Leitung und Team erhalten `200`.
- Org-Admin ohne Rolle, Leitung einer anderen Freizeit, fremde Organisation und eine fehlende Zeile in der Rollenliste erhalten `404`. Ohne Sitzung kommt `401`.
- Falsche Rolle (`403`) und Token-Fälle haben keinen Gegenstand.

**Verifikation (nach den Patches):**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test` (ganze Lösung mit Architektur-, E2E- und Screenshot-Tests): 1935 von 1935 bestanden.
- `npm ci` und `npm run build` in `doku/`: fehlerfrei, `dist/mitarbeitende/notfallordner/` ist vorhanden. Es erschien nur die bekannte i18n-Warnung.
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen Test.
  - API-Zeilen: `NotfallordnerTests`.
  - Seite, leer, ohne Recht und Fehler, Drucken: `NotfallordnerSeitenTests`.
  - Druckmedium: E2E `Notfallordner`.
- Zurückgesetzt wurden 25 Screenshots mit reinem Rendering-Rauschen (höchstens 404 Pixel) sowie `freizeitpass-fragen-laptop` und `schutzkonzept-einrichten-laptop`. Die beiden ändern sich nur durch den zufälligen Link-Token.

**Restrisiken:**
- Dass nur Teilnehmende im Ordner stehen, sichert nur der Client. Ein direkter API-Aufruf mit Kennungen bestätigter Mitarbeitender liefert deren Pässe und protokolliert die Einsicht.
- Bei mehr als 150 bestätigten Teilnehmenden zeigt die Seite nur die Fehlermeldung.
- Ausdrucke tragen kein Datum. Die Doku rät, vor der Abreise und nach Änderungen neu zu drucken.
- Tests mit festen Daten (Freizeit 2027) laufen ab Sommer 2027 aus, wie die bestehenden.

