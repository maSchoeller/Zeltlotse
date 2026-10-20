---
title: 'Story 5-1: Kalkulation und Beitragsrechner'
type: 'feature'
created: '2026-10-19'
baseline_revision: 'd3145bbdf75acc972f3a00fc1e0dc5a94a7d24fc'
status: 'in-review'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/4-1-rezepte-mit-skalierung.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Das Modul Finanzen ist ein leeres Gerüst. Die Leitung kann eine Freizeit nicht kalkulieren und sieht nicht, welcher Teilnehmerbeitrag sich ergibt (CAP-32, FR-54 bis FR-57).

**Approach:** Je Freizeit gibt es eine Kalkulation. Sie besteht aus Planzahlen (Teilnehmende, Mitarbeitende), sechs festen Kategorien mit Fixkosten und Kosten pro Person und Tag sowie freien Einnahmeposten. Der Server rechnet daraus Kosten je Kategorie, Gesamtkosten, Einnahmen und Teilnehmerbeitrag. Die Felder speichern per Autosave nach AD-19. Der Beitragsrechner ist eine reine Was-wäre-wenn-Ansicht: Schieberegler für Teilnehmende, Mitarbeitende und Verpflegungs-Tagessatz verändern den Beitrag sofort als Balken. Gespeichert wird dabei nichts.

## Boundaries & Constraints

**Always:**
- Es gibt genau sechs Kategorien in fester Reihenfolge: Unterkunft, Verpflegung, Fahrt, Programm und Material, Mitarbeitende, Sonstiges (Roadmap V5). Jede Kategorie hat dieselben zwei Werte: Fixkosten und Kosten pro Person und Tag. Bei Verpflegung heißt der zweite Wert in der Oberfläche „Tagessatz pro Person und Tag“ (FR-55).
- Rechenregel (eine reine Funktion, von Server und Client gemeinsam genutzt): Personen = Teilnehmende + Mitarbeitende; Tage = Anzahl Kalendertage der Freizeit (erster bis letzter Tag, beide eingeschlossen); Kosten der Kategorie = Fixkosten + Kosten pro Person und Tag × Personen × Tage; Gesamtkosten = Summe der Kategorien; Einnahmen = Summe der Einnahmeposten; Teilnehmerbeitrag = (Gesamtkosten − Einnahmen) ÷ Teilnehmende, kaufmännisch auf Cent gerundet (`MidpointRounding.AwayFromZero`), nie unter 0 €. Bei 0 Teilnehmenden gibt es keinen Beitrag (`null`). So werden die Kosten der Mitarbeitenden auf die Teilnehmerbeiträge umgelegt (FR-54).
- Planzahlen sind Eingaben der Kalkulation (ganze Zahlen 0 bis 1000, Start 0). Beträge sind Euro, 0 bis 1.000.000, höchstens zwei Nachkommastellen, als Text mit Komma oder Punkt als Dezimaltrennzeichen. Tausenderpunkte wie „1.250“ werden abgelehnt (wie `KuecheWerte`). Die Antwort normalisiert den Wert, damit der Vergleich mit `alterWert` stabil bleibt.
- Einnahmeposten: Bezeichnung (Pflicht, höchstens 200 Zeichen) und Betrag (Start 0). Rabatte der Organisation sind ebenfalls Einnahmen (positiver Betrag).
- Alle Routen liegen unter `/api/freizeiten/{freizeitId}/finanzen/…`, erlauben Leitung und Team (`rollen-und-rechte.md`: Team darf Finanzen), tragen `.GehoertZuSchalter(FunktionsSchalter.Finanzen)`, und ihre Commands tragen `ISchalterCommand`. Nach „gelaufen“ wird jeder Schreibzugriff mit 409 `freizeit-abgeschlossen` abgelehnt. Es gibt kein `[ErlaubtNachAbschluss]`.
- Autosave nach AD-19: PATCH mit genau einem Feld als Text, `{version, alterWert, <feld>}`. Weicht `alterWert` ab, folgt 409 `konflikt`. Noch nicht vorhandene Zeilen (Kalkulation, Kategorie) entstehen beim ersten PATCH per `INSERT … ON CONFLICT DO NOTHING` und werden danach mit `FOR UPDATE` gesperrt.
- Der Teilnehmerbeitrag und die Kategorie-Anteile erscheinen als Balken mit Zahl und Wort (NFR-16). Der neue Baustein `Schieberegler` entsteht in `Zeltlotse.Ui.Komponenten`, ist mit Pfeiltasten in festen Schritten bedienbar und zeigt seinen Wert als Zahl daneben.

**Never:**
- Keine Kopplung an Anmeldungen (Live-Kalkulation ist „bewusst nie“) und keine an Essensplan oder Küche (FR-55).
- Kein Speichern der Reglerstellungen, keine Szenarien, kein Kosten-Donut, keine Vorjahreskalkulation (Ausbau-Pool).
- Keine Belege, kein Soll-Ist, keine Erstattung und kein „Beitrag bezahlt“ (Stories 5-2, 5-3). Kein Budget-Balken im Cockpit.
- Keine neuen Events, Aktionsschlüssel, Problemcodes oder Spine-Änderungen. Keine neue Freizeiten-Schnittstelle: Die Tage kommen aus dem vorhandenen `IFreizeitTageLesen`.
- Keine Pop-ups. Löschen nur über `InlineBestaetigung`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Beitrag rechnen | 12 Tage; 40 TN, 10 MA; Verpflegung 7 €/P/T; Unterkunft 2.000 € fix + 10 €/P/T; Einnahme 1.200 € | Verpflegung 4.200 €, Unterkunft 8.000 €, gesamt 12.200 €, Beitrag (12.200 − 1.200) ÷ 40 = 275,00 € | – |
| Rundung | Rest 1.000 € bei 3 TN | Beitrag 333,33 € | – |
| Keine Teilnehmenden | TN = 0 | Beitrag `null`; Oberfläche: „Ohne Teilnehmende lässt sich kein Beitrag berechnen.“ | – |
| Einnahmen über Kosten | Einnahmen > Gesamtkosten | Beitrag 0,00 € | – |
| Neue Freizeit | noch nichts gespeichert | GET liefert Planzahlen 0, sechs Kategorien mit 0 €, Version 0, keine Einnahmen | – |
| Betrag mit Komma | `"7,5"` | gespeichert 7,50; Antwort `"7,5"` | – |
| Ungültiger Betrag | `"1.250"`, `"-3"`, `"abc"`, `"1,234"`, `"1000001"` | 400 `validierung` am Feld | keine Änderung |
| Veralteter Wert | `alterWert` weicht ab | 409 `konflikt` | Autosave zeigt „Inzwischen geändert – neu laden?“ |
| Gleichzeitiger erster PATCH | zwei PATCHes auf dieselbe neue Kategorie | genau eine Zeile; 200 und 200 oder 409, nie 500 | – |
| Unbekannte Kategorie | `/kategorien/xyz` | 404 | – |
| Modul aus | Schalter Finanzen aus | jede Route 404; nach dem Einschalten sind die Daten wieder da | – |
| Gelaufen | Freizeit abgeschlossen | GET 200; jeder Schreibzugriff 409 `freizeit-abgeschlossen` | Seite nur lesbar |
| Rechte | Org-Admin ohne Freizeitrolle, fremde Organisation, unbekannte Freizeit / ohne Sitzung | 404 / 401 | – |

</intent-contract>

## Code Map

- `src/Modules/Finanzen/Zeltlotse.Finanzen/FinanzenModul.cs` -- Gerüst; `MapFinanzenEndpoints` ist leer. Muster: `src/Modules/Kueche/Zeltlotse.Kueche/KuecheModul.cs:49-66` (`MapGroup($"/freizeiten/{{{Rechtemerkmale.FreizeitRouteWert}:guid}}/kueche/…")`, `.NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(...)`, ILoeschlauf-Registrierung).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Daten/FinanzenDbContext.cs` -- `FinanzenModell.Konfigurieren` ist leer; DbSets in Schreib- und Lese-Kontext, Sperr-Helfer wie `KuecheDbContext.MahlzeitStandSperrenAsync` (INSERT ON CONFLICT + FOR UPDATE).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Zeltlotse.Finanzen.csproj` -- es fehlen ProjectReferences auf `Freizeiten.Contracts` und `Api.Contracts` (wie Kueche.csproj, mit Kommentar „Pfeil aus AD-2“). Der Pfeil Finanzen→Freizeiten ist in `tests/Zeltlotse.ArchitectureTests/Loesung.cs` schon erlaubt. `InternalsVisibleTo Zeltlotse.Finanzen.Tests` ist schon da.
- `src/Modules/Kueche/Zeltlotse.Kueche/Rezepte/{RezeptAnlegen,RezeptBearbeiten,RezeptLoeschen,KuecheWerte}.cs` -- Muster für Slices: Command + Ergebnis-Union + Validator + Handler + Endpunkt. `KuecheWerte.cs:159` `KuecheProbleme`, `:175` `FeldPatch` (pro Modul kopieren), `MengeLesen` (Zahlen-Parser).
- `src/Modules/Kueche/Zeltlotse.Kueche/Einkauf/{MahlzeitStand,EinkaufRechnen}.cs` -- Muster für Zeilen, die beim ersten PATCH entstehen, und für reine Rechnung.
- `src/Modules/Kueche/Zeltlotse.Kueche/Datenschutz/KuecheLoeschlauf.cs` -- Muster `ILoeschlauf` (Daten nach der Org-Frist löschen).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Zeitpunkte/IFreizeitTageLesen.cs` -- `FreizeitTage(ErsterTag, LetzterTag)` liefert die Tage. `IFreizeitZeitpunkte` liefert die Phase für die zweite Gelaufen-Prüfung im Handler.
- `src/Zeltlotse.SharedKernel/Rechte/FunktionsSchalter.cs` -- `Finanzen = 2` existiert. `SharedKernel/Commands/IModulCommand.cs` -- `IBenutzerCommand`, `ISchalterCommand`, `IModulCommand<T>`.
- `src/Zeltlotse.Api.Contracts/Kueche/Rezepte.cs` -- Muster für DTO-Records, Feld-Enum und `…Felder`-Klasse. Neu: `src/Zeltlotse.Api.Contracts/Finanzen/`.
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs`, `src/Zeltlotse.Ui/UiRegistrierung.cs:28` -- typisierter Client, Registrierung. `src/Zeltlotse.Ui/Texte/Anzeige.cs` -- Platz für einen Euro-Formatierer (de-DE).
- `src/Zeltlotse.Ui/Autosave/AutosaveFeld.razor` -- Autosave-Baustein (Werte als Text, `@key` mit Ladezähler).
- `src/Zeltlotse.Ui.Komponenten/{Balken,Segmente,Reiter,Card}.razor` -- Balken (SVG-Breite, kein style-Attribut wegen CSP), Muster für `[NurInteraktiv]` (Segmente), Reiter. CSS in `Ui.Komponenten/wwwroot/css`.
- `src/Zeltlotse.App/Features/Kueche/{Rezepte.razor,KuecheReiter.razor,KuecheTexte.cs}` -- Seitenmuster (NichtVerfuegbar, Gelaufen nur lesbar, Ladefehler, LeerZustand).
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor:202` -- Knopf „Küche“ nur mit Schalter. Gleiches für „Finanzen“.
- `tests/Zeltlotse.Api.Tests/{RezepteTests.cs:577-621,KuecheTestHelfer.cs,FreizeitpassSzenario.cs,EndpunktListeTests.cs,DemoDatenTests.cs,AbschlussTests.cs}` -- AD-30-Fälle über `StatusAllerRoutenAsync`, Schalten (Feld `finanzen`), Routenliste mit Zähler, Demo-Prüfung, Freizeit abschließen. Datenbank über `postgres.NeueMigrierteDatenbankAsync()`.
- `tests/Zeltlotse.Kueche.Tests/{Zeltlotse.Kueche.Tests.csproj,AssemblyInfo.cs}` -- Vorlage für das neue `tests/Zeltlotse.Finanzen.Tests`. Dieses kommt in `Zeltlotse.slnx` unter `/tests/`.
- `tests/Zeltlotse.Ui.Tests/{RezeptSeitenTests,KomponentenTests,FreizeitenSeitenTests}.cs` -- bUnit-Muster (`NachgebildeterHandler`).
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs:32,DemoDaten.cs:604}` -- Demo „Zeltlager Sommer 2027“ (02.08.–13.08.2027, 12 Tage, Finanzen an). Schritt `KuecheAsync` ist das Muster (idempotent, nur Commands).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:2078-2135,2484` -- Abschnitt Küche, `BildAsync`, `Geraete` (Handy 375×812, Laptop 1280×800), `E2EHilfe.AxeAsync`.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- `RollenOrdner`, `PflichtSeiten`. `doku/astro.config.mjs:26` -- Sidebar. `doku/src/content/docs/leitung/module-schalten.md` -- Abschnitt „Noch nicht dabei“ nennt Finanzen.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Finanzen/Kalkulation.cs` -- DTOs: `Kalkulation(Version, Teilnehmende, Mitarbeitende, Tage, Kategorien, Einnahmen, Ergebnis)`, `KalkulationKategorie(Kategorie, Version, Fixkosten, ProPersonUndTag, Kosten)`, `Einnahmeposten(Id, Version, Bezeichnung, Betrag)`, `EinnahmepostenNeu`, Enum `Kostenkategorie` (Reihenfolge wie oben, als Name serialisiert), Feld-Enums mit `…Felder`-Klassen. Dazu die reine statische Funktion `Beitragsrechnung.Rechnen(...)` -- eine Rechenregel für Server und Beitragsrechner im Client.
- `tests/Zeltlotse.Finanzen.Tests/` (neu, in `Zeltlotse.slnx`) -- zuerst die Tests: Rechenregel mit allen Matrixzeilen zur Rechnung, Wert-Parser, Mandantenfilter.
- `tests/Zeltlotse.Api.Tests/KalkulationTests.cs` -- vor dem Code: HTTP-Tests für alle Routen (Leitung und Team 2xx, AD-30-Fälle 404/401, Schalter aus 404 und Daten danach wieder da, gelaufen 409/200, Validierung 400, Konflikt 409, gleichzeitiger erster PATCH, Löschlauf). `EndpunktListeTests` erweitern.
- `src/Modules/Finanzen/Zeltlotse.Finanzen/Kalkulation/` -- Entitäten `Kalkulation` (je Freizeit eine), `KategoriePosten` (eindeutig je Freizeit und Kategorie), `Einnahmeposten`; `FinanzenWerte` (Parser, Normalisierung, `FinanzenProbleme`, `FeldPatch`); Slices `KalkulationLaden` (GET), `KalkulationBearbeiten` (PATCH teilnehmende|mitarbeitende), `KategorieBearbeiten` (PATCH fixkosten|proPersonUndTag), `EinnahmeAnlegen` (POST), `EinnahmeBearbeiten` (PATCH bezeichnung|betrag), `EinnahmeLoeschen` (DELETE).
- `src/Modules/Finanzen/Zeltlotse.Finanzen/{FinanzenModul.cs,Daten/FinanzenDbContext.cs,Datenschutz/FinanzenLoeschlauf.cs,Zeltlotse.Finanzen.csproj}` und die Migration `Kalkulation` -- Routen: `GET|PATCH /finanzen/kalkulation`, `PATCH /finanzen/kalkulation/kategorien/{kategorie}`, `POST /finanzen/kalkulation/einnahmen`, `PATCH|DELETE /finanzen/kalkulation/einnahmen/{id:guid}`. Beträge `HasPrecision(12, 2)`.
- `src/Zeltlotse.Ui.Komponenten/Schieberegler.razor` (+ CSS, bUnit-Test in `KomponentenTests`) -- `<input type="range">` mit Label, Min, Max, Schritt, Wert als Text daneben, `[NurInteraktiv]`.
- `src/Zeltlotse.Ui/Api/FinanzenApiClient.cs`, `UiRegistrierung.cs`, `Texte/Anzeige.cs` (`Euro`) -- Client und Formatierung „1.234,50 €“.
- `src/Zeltlotse.App/Features/Finanzen/{Kalkulation.razor,Beitragsrechner.razor,FinanzenReiter.razor,FinanzenTexte.cs}` -- Kalkulation: Card „Planzahlen“ (zwei Autosave-Felder, Tage als Text), Card je Kategorie mit zwei Autosave-Feldern und Kosten, Card „Einnahmen“ (Liste, Anlegen-Formular, Löschen per InlineBestaetigung), Card „Ergebnis“ mit Gesamtkosten, Einnahmen, Teilnehmerbeitrag und je Kategorie einem Balken (Anteil an den Gesamtkosten). Beitragsrechner: drei Schieberegler (Teilnehmende 0–max(200, Planzahl), Mitarbeitende 0–max(100, Planzahl), Tagessatz Verpflegung 0–max(30 €, Wert) in 0,50-€-Schritten), Start auf den gespeicherten Werten, Knopf „Zurücksetzen“, zwei Balken „Laut Kalkulation“ und „Mit Reglern“ (der größere Wert = 100 %) mit Statuswort „Günstiger“, „Gleich“ oder „Teurer“ und Differenz in Euro. Beide Seiten zeigen NichtVerfuegbar, Ladefehler und bei gelaufen nur lesbar (Regler bleiben bedienbar, weil sie nichts speichern).
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- Knopf „Finanzen“ nur mit Schalter Finanzen.
- `tests/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs` -- vor dem Code: Laden, Autosave-Felder, Einnahme anlegen und löschen, Regler ändern den Beitrag sofort, Zurücksetzen, 0 Teilnehmende, gelaufen ohne Eingaben, Ladefehler, nicht verfügbar, Knopf „Finanzen“ auf der Startseite.
- `src/Zeltlotse.Api/Demo/{DemoKatalog.cs,DemoDaten.cs}` + `DemoDatenTests` -- Schritt `FinanzenAsync`: 40 Teilnehmende, 10 Mitarbeitende, Werte in allen Kategorien (Verpflegung 7 €), Einnahmen „Zuschuss Landesjugendplan“ und „Rabatt der Organisation“.
- `doku/src/content/docs/finanzen/{index,kalkulation,beitragsrechner}.md`, `doku/astro.config.mjs`, `DokuRegeln.cs` (Ordner `finanzen` und Pflichtseiten), `leitung/module-schalten.md` und weitere Seiten, die Finanzen als „noch nicht dabei“ nennen -- Doku mit Wer, Weg, Rechenregel, Rundung, Grenzen, nach dem Abschluss, noch nicht dabei (Belege 5-2, Beitrag bezahlt 5-3), Screenshots.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Abschnitt „Story 5-1“: `kalkulation` und `beitragsrechner`, je Handy und Laptop, mit Axe-Prüfung.

**Acceptance Criteria:**
- Given die Demo-Freizeit mit eingeschaltetem Modul Finanzen, when ein Teammitglied auf der Startseite „Finanzen“ öffnet, then erscheint die Kalkulation mit sechs Kategorien, Einnahmen und dem errechneten Teilnehmerbeitrag als Zahl und Balken.
- Given die Kalkulation, when ein Betrag in einem Feld geändert und das Feld verlassen wird, then speichert Autosave, und das Ergebnis zeigt nach dem Neuladen die neuen Kosten und den neuen Beitrag.
- Given der Beitragsrechner, when ein Regler per Maus oder Pfeiltaste bewegt wird, then ändern sich Zahl, Differenz und Balken „Mit Reglern“ sofort, ohne Serveraufruf und ohne Speichern.
- Given eine Freizeit mit ausgeschaltetem Modul Finanzen, when die Startseite geladen wird, then fehlt der Knopf „Finanzen“.
- Given die fertige Story, when `dotnet test` (alle Projekte), die Doku-Screenshot-Tests und `npm run build` in `doku/` laufen, then ist alles grün, und `kalkulation-{handy,laptop}.png` sowie `beitragsrechner-{handy,laptop}.png` liegen in `doku/src/assets/screenshots/` und sind verlinkt.

## Spec Change Log

## Review Triage Log

## Design Notes

- **Planzahlen statt Anmeldezahlen:** Live-Kalkulation über Anmeldungen ist „bewusst nie“. Die Höchstzahl Teilnehmende ist nicht über eine Contracts-Schnittstelle erreichbar, und eine Höchstzahl Mitarbeitende gibt es nicht. Darum trägt die Kalkulation beide Zahlen selbst. Das ist einfach und kommt ohne neue Schnittstelle aus.
- **Kategorien:** FR-54 nennt sechs Kategorien ohne Namen. Die Namen stammen aus der Roadmap V5 (`prd/reconcile-roadmap-intent.md` F17). Alle sechs haben denselben Aufbau. Dass Verpflegung nur einen anderen Namen für den zweiten Wert bekommt, vermeidet Speziallogik und erfüllt FR-55 (Tagessatz × alle Personen × Tage).
- **Rechte:** Team und Leitung dürfen alles, weil die Kalkulation nicht auf der Leitungsliste in `rollen-und-rechte.md` steht. Damit gibt es, wie bei der Küche, keinen 403-Fall. Token-Fälle entfallen, weil es keine Token-Route gibt. Der Fall „ohne Sitzung“ gibt 401.
- **Eine Rechenregel:** `Beitragsrechnung` liegt als reine Funktion in `Api.Contracts/Finanzen`. Der Server nutzt sie für das `Ergebnis` im GET, der Beitragsrechner für die Regler. So können die beiden Rechnungen nicht auseinanderlaufen.
- **Beitragsrechner-Balken:** Die zwei Balken haben eine gemeinsame Skala, der größere Beitrag ist 100 %. Das Statuswort beschreibt die Änderung, nicht ein Budget. Damit zeigt der Balken „die Änderung des Teilnehmerbeitrags“ (FR-57).
- **Löschen:** Die Kalkulation enthält keine Personendaten. Sie wird mit den übrigen Freizeitdaten nach der Org-Frist gelöscht (`FinanzenLoeschlauf`, Muster Küche).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test --project tests/Zeltlotse.Finanzen.Tests` -- expected: grün
- `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*KalkulationTests*"` -- expected: grün
- `dotnet test --project tests/Zeltlotse.Ui.Tests` und `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- expected: grün
- einmal am Ende: alle Testprojekte einzeln, darunter `dotnet test --project tests/Zeltlotse.E2E.Tests` (inklusive Kategorie Doku) -- expected: grün, Screenshots erzeugt
- einmal am Ende: `npm run build` in `doku/` -- expected: fehlerfrei
