---
title: 'Story 3-7: Module pro Freizeit schalten'
type: 'feature'
created: '2026-10-14'
status: 'done'
baseline_revision: 'f74f0ab46be374e1c018f4fbd3b06076a6dc2ae4'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-6-programmbibliothek.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Leitung kann nicht festlegen, welche Module (Verpflegung, Finanzen, Teilnehmer-Zugang) ihre Freizeit nutzt; `FreizeitKontext.Schalter` ist immer leer, und es gibt keinen serverseitigen Mechanismus, der Endpunkte und Benutzer-Commands eines abgeschalteten Moduls sperrt (CAP-26, FR-44, AD-20).

**Approach:** Freizeiten speichert drei Schalter an der Freizeit (Vorgabe aus) und bietet sie über `GET/PATCH /api/freizeiten/{freizeitId}/module` (nur Leitung, Autosave) an; der Kontext-Endpunkt liefert die eingeschalteten Schalter. Der API-Host erzwingt AD-20 zentral: Endpunkte erklären ihren Schalter mit `.GehoertZuSchalter(...)` und antworten bei ausgeschaltetem Schalter `404`; eine Pipeline-Stufe lehnt Benutzer-Commands eines ausgeschalteten Schalters ebenfalls mit `404` ab, Ereignis-Empfänger laufen weiter. Web-App: Card „Module“ in den Einstellungen der Freizeit mit drei Ankreuzfeldern.

## Boundaries & Constraints

**Always:**
- **Datenformen** in `src/Zeltlotse.Api.Contracts/Freizeiten/Module.cs` (neu): `FreizeitModule(uint Version, bool Verpflegung, bool Finanzen, bool TeilnehmerZugang)`, `enum ModulFeld { Verpflegung, Finanzen, TeilnehmerZugang }`, `static class ModulFelder` (JSON-Namen `verpflegung`, `finanzen`, `teilnehmerZugang`; Muster `FragenFelder`). `FunktionsSchalter` in `Gemeinsam/FreizeitKontext.cs` bleibt unverändert. Kein neuer Aktionsschlüssel: Schalten gehört zu `freizeit-einstellungen`.
- **Endpunkte (Modul Freizeiten):**

  | # | Route | Recht | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId}/module` | `FreizeitRechte.Einstellungen` (Leitung) | `200 FreizeitModule`, auch in Gelaufen |
  | 2 | `PATCH /api/freizeiten/{freizeitId}/module` | Einstellungen | AD-19-Autosave mit genau einem Feld, Werte `"true"`/`"false"` (wie `AutosaveAnkreuzfeld`): `200 {version, <feld>: "true"\|"false"}`; Altwert weicht ab → `409 konflikt`; Gelaufen → `409 freizeit-abgeschlossen`; kein/zwei Felder, unbekanntes Feld, Wert nicht `true`/`false` → `400` am Feld |
  | 3 | `GET /api/freizeiten/{freizeitId}/kontext` (bestehend) | Leitung, Team | `schalter` = eingeschaltete Schalter in Enum-Reihenfolge (Verpflegung, Finanzen, TeilnehmerZugang) |
- **Speicher:** drei `bool`-Spalten an `Freizeit` (Vorgabe `false`, auch für bestehende Zeilen), Methode `ModulAendern(ModulFeld, alterWert, neuerWert)` nach dem Muster `FeldAendern`/`FragenAendern` (Altwert-Vergleich, Zeilensperre im Handler). Migration `Module`. Abschalten ändert nur den Schalter; keine Daten werden gelöscht oder verändert.
- **Durchsetzung (AD-20, AD-17 „einmal pro Anfrage“):**
  - SharedKernel: `enum FunktionsSchalter` in `Rechte/` (serverintern, Werte `Verpflegung = 1, Finanzen = 2, TeilnehmerZugang = 3`, Muster `Rechte/FreizeitRolle.cs`); Endpunkt-Merkmal `.GehoertZuSchalter(FunktionsSchalter)` in `Rechtemerkmale.cs` (Metadaten `GehoertZuSchalterMetadaten`); Schnittstelle `ISchalterStand` mit `Task<bool> IstAnAsync(Guid freizeitId, FunktionsSchalter schalter, CancellationToken)`; Command-Merkmal `ISchalterCommand { Guid FreizeitId; FunktionsSchalter Schalter; }`; Ausnahme `SchalterAusAusnahme`.
  - Freizeiten.Contracts: `Schalter/IFreizeitSchalterLesen` mit `Task<IReadOnlyCollection<FunktionsSchalter>> SchalterAsync(Guid freizeitId, CancellationToken)` (Lese-Kontext unter Mandantenfilter), registriert in `FreizeitenModul`; auch der Kontext-Endpunkt nutzt sie.
  - API-Host: `SchalterStand : ISchalterStand` (scoped, Ergebnis je Freizeit pro Anfrage zwischengespeichert, Muster `KontextRollen`). `RechtePruefung`: Nach bestandener Freizeit-Rollenprüfung und gesetztem Mandanten, wenn `GehoertZuSchalterMetadaten` vorhanden und der Schalter aus ist → `404`. `GehoertZuSchalter` ohne `NurFreizeitRolle` am selben Endpunkt → `InvalidOperationException` (Konfigurationsfehler, Muster Zeile 35-38).
  - Pipeline: neue Stufe `SchalterStufe<,>` in `SharedKernel/Pipeline`, als **erste** Stufe in `Program.cs`: Für Nachrichten, die `IBenutzerCommand` **und** `ISchalterCommand` tragen, wirft sie `SchalterAusAusnahme`, wenn der Schalter aus ist; alle anderen Nachrichten (insb. Ereignis-Empfänger) laufen ungeprüft durch. `AusnahmeBehandlung` bildet `SchalterAusAusnahme` auf `404` ohne Details ab.
- **Leitplanke für künftige Module:** `EndpunktListeTests` prüft, dass jeder Endpunkt unter `/api/freizeiten/{freizeitId}/kueche…` oder `/finanzen…` `GehoertZuSchalter` trägt (heute ohne Treffer, sichert V4/V5).
- **Testgetrieben (AD-30).** Pflichtfälle #1/#2: Leitung `2xx`, Team `403`, Org-Admin ohne Freizeit-Rolle `404`, fremde Organisation (Lea auf X) `404`, unbekannte Freizeit `404`, ohne Sitzung `401`. Kontext #3: Team sieht dieselben Schalter. Schalter-Wirkung über einen Test-Endpunkt in `TestHelfer.TestEndpunkte` (`/test/schalter/{freizeitId:guid}`, `NurFreizeitRolle(Leitung, Team)`, `GehoertZuSchalter(Verpflegung)`): aus `404`, an `200` (Leitung und Team), wieder aus `404`; ohne Rolle `404` (nicht `403`), fremde Organisation `404`, ohne Sitzung `401`. Pipeline-Stufe in `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs` mit Test-Nachrichten und gefälschtem `ISchalterStand`. Token-Fälle entfallen (kein Token-Endpunkt; der QR-Zugang kommt mit V6).
- **Oberfläche** nur aus `Zeltlotse.Ui.Komponenten`/`Zeltlotse.Ui` (`AutosaveAnkreuzfeld`), Rechte nur über `Darf(…, FreizeitEinstellungen)`, Texte Deutsch ohne Anrede; in Gelaufen nur lesbar (Konflikt-Text wie die übrigen Felder der Seite).

**Never:** Endpunkte, Seiten oder Navigation für Küche, Finanzen oder Teilnehmer-Zugang selbst (kommen mit V4–V6); Löschen oder Verbergen von Daten beim Abschalten; Schalter für Kanal „Team“, Programm oder Mahlzeiten; Schalter-Prüfung für Ereignis-Empfänger oder Lösch-Dienst; Prüfung in einzelnen Modulen statt im Host/der Pipeline; neuer Aktionsschlüssel, neuer Problemcode, Spine-Änderung; Pop-ups; Commit auf `main`, Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Neue Freizeit | X frisch angelegt | #1 `{version, false, false, false}`, #3 `schalter: []` | – |
| Einschalten | Miriam #2 `{version, alterWert:"false", verpflegung:"true"}` | `200 {version', verpflegung:"true"}`; #3 `["Verpflegung"]` (auch für Tobias) | – |
| Konflikt | zweiter PATCH mit `alterWert:"false"` nach dem Einschalten | – | `409 konflikt` |
| Anderes Feld | zweiter PATCH auf `finanzen` mit alter Version | `200` (AD-19: anderes Feld) | – |
| Ungültig | `verpflegung:"ja"`, zwei Felder, leerer Körper | – | `400` am Feld |
| Team | Tobias #1, #2 | – | `403` |
| Abgeschlossen | X `Gelaufen` (SQL) | #1 `200` | #2 `409 freizeit-abgeschlossen` |
| Endpunkt-Wirkung | Test-Endpunkt Verpflegung, X aus → an → aus | `404` → `200` → `404` | ohne Rolle/fremde Org stets `404`, ohne Sitzung `401` |
| Daten bleiben | Freizeitseite/Fragen von X gesetzt, Schalter an → aus | Freizeitseite und Fragen unverändert, Version zählt nur | – |
| Pipeline | Benutzer-Command mit `ISchalterCommand`, Schalter aus | `SchalterAusAusnahme`, Handler nicht aufgerufen | Ereignis-Empfänger-Nachricht mit `ISchalterCommand` (ohne `IBenutzerCommand`) läuft durch |
| Mandant | Schalter in X an, Z (Org B) aus | Kontext von Z zeigt `[]` | – |

</intent-contract>

## Code Map

- `src/Zeltlotse.Api/Rechte/RechtePruefung.cs:26-89` -- zentrale Prüfung; Freizeit-Zweig 69-86 (Mandant gesetzt Z. 80, Rollenprüfung 81-85) → Schalter-Prüfung direkt danach; Kombinationsprüfung-Muster 35-38; `Kennung` 98.
- `src/Zeltlotse.Api/Rechte/KontextRollen.cs` -- Muster für scoped Auflösung mit Zwischenspeicher je Anfrage (`SchalterStand` daneben, Registrierung wie `IKontextRollen`).
- `src/Zeltlotse.SharedKernel/Rechte/{Rechtemerkmale.cs,FreizeitRolle.cs,IKontextRollen.cs}` -- Merkmal-Erweiterungen (`NurFreizeitRolle`, Metadaten-Records), Enum-Muster, Schnittstellen-Muster.
- `src/Zeltlotse.SharedKernel/Commands/IModulCommand.cs:17` -- `IBenutzerCommand`; `ISchalterCommand` daneben.
- `src/Zeltlotse.SharedKernel/Pipeline/{ValidierungsStufe.cs,TransaktionsStufe.cs}` -- Stufen-Muster (`IPipelineBehavior<TMessage,TResponse>`, Constraints); `src/Zeltlotse.Api/Program.cs:44-70` -- `PipelineBehaviors`-Liste.
- `src/Zeltlotse.Api/Fehler/AusnahmeBehandlung.cs:30` -- Ausnahme → Status (`ValidierungsAusnahme`-Muster).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Zeitpunkte/IFreizeitZeitpunkte.cs` -- Schnittstellen-Muster; Impl.-Muster `FreizeitZeitpunkteLesen` in `Rechte/FreizeitRechte.cs:133`; Registrierung `FreizeitenModul.cs:43-54`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Freizeit.cs` -- Felder, `Version` 87, `FeldAendern` 170-203, `FragenAendern` 224.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{Fragen/FragenAuswahlBearbeiten.cs,Freizeitseite/FreizeitseiteBearbeiten.cs}` -- PATCH-Slice-Muster: Command mit `IBenutzerCommand` 25, Ergebnis `Gespeichert/Konflikt/NichtGefunden/Abgeschlossen` 33-46, Validator 48-94, Handler mit `FOR UPDATE` und Gelaufen 96-121, `FreizeitseiteProbleme` 124-135, Laden 141, Endpunkt mit `JsonElement` 187-248.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{FreizeitenModul.cs:99-108 (Freizeit-Gruppe, /fragen),Startseite/FreizeitEndpunkte.cs:76-88 (Kontext, `[]` in Z. 87),Rechte/FreizeitRechte.cs:40 (Einstellungen),Daten/FreizeitenDbContext.cs:65-81}`; Migration: `dotnet tool restore`, dann `dotnet ef migrations add Module --project src/Modules/Freizeiten/Zeltlotse.Freizeiten --output-dir Migrationen`.
- `src/Zeltlotse.Api.Contracts/Freizeiten/{Fragen.cs,Freizeitseite.cs}` -- `FragenFeld`/`FragenFelder`-Muster.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs` (`FragenPfad` 161, `FragenLadenAsync` 167) -- `ModulePfad`, `ModuleLadenAsync`.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- Kopfkommentar 9-17, Cards 57-130, `OnParametersSetAsync` 170, `NeuLadenAsync` 205; `src/Zeltlotse.Ui/Autosave/AutosaveAnkreuzfeld.razor` (Verwendung `Organisationen/OrganisationAnsicht.razor:228`).
- Tests: `tests/Zeltlotse.Api.Tests/{FreizeitKontextTests.cs:84-95 (leere Schalter → anpassen),TestHelfer.cs:18-36 (TestEndpunkte, `/test/nur-team`),EndpunktListeTests.cs (Freizeit-Routenliste 185, `Endpunkte` 369, 401-Test 340),FreizeitpassSzenario.cs,FragenAuswahlTests…}`; `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs`; `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs` (Einstellungen 487+, Helfer `EinstellungenAntworten` 691 → `/module` nachbilden).
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs (`FreizeitseiteAsync` 437-481 als Muster, Schrittliste ab 116),DemoKatalog.cs:19}`; `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Kontakt_Fuer_Absagen` 997 als Muster, `EinstellungenAsync` 2057, `BildAsync` 2133, `Geraete` 21); `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-29`; `doku/src/content/docs/leitung/{index.md:22,27,schutzkonzept.md (Seitenmuster)}`, `doku/src/content/docs/kueche/index.md:18`.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.Api.Contracts/Freizeiten/Module.cs` -- Datenformen.
- `tests/Zeltlotse.SharedKernel.Tests/PipelineTests.cs` (zuerst) + `src/Zeltlotse.SharedKernel/{Rechte/FunktionsSchalter.cs,Rechte/ISchalterStand.cs,Rechte/Rechtemerkmale.cs,Commands/IModulCommand.cs,Pipeline/SchalterStufe.cs,Pipeline/SchalterAusAusnahme.cs}` -- Enum, Merkmal, Schnittstellen, Stufe; Tests: aus → Ausnahme ohne Handler-Aufruf, an → durch, ohne `IBenutzerCommand` → durch ohne Abfrage, ohne `ISchalterCommand` → durch ohne Abfrage.
- `tests/Zeltlotse.Freizeiten.Tests/…` (zuerst) + `Domaene/Freizeit.cs` -- `ModulAendern` (Altwert, Vorgabe aus, nur Schalter ändert sich).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Daten/FreizeitenDbContext.cs` + Migration `Module` -- Spalten mit Vorgabe `false`.
- `tests/Zeltlotse.Api.Tests/ModuleTests.cs` (zuerst) -- jede Matrix-Zeile, Pflichtfälle #1/#2, Kontext, Test-Endpunkt-Wirkung; `FreizeitKontextTests` (Schalter leer bei neuer Freizeit, Name anpassen), `EndpunktListeTests` (Route `/module`, Leitplanke Küche/Finanzen, Kombinationsregel), `TestHelfer` (Test-Endpunkt).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Schalter/IFreizeitSchalterLesen.cs` + `Zeltlotse.Freizeiten/{Module/ModuleBearbeiten.cs,Rechte/FreizeitRechte.cs,FreizeitenModul.cs,Startseite/FreizeitEndpunkte.cs}` -- Slice (Laden, FeldAendern), Lese-Impl., Routen, Kontext.
- `src/Zeltlotse.Api/{Rechte/SchalterStand.cs,Rechte/RechtePruefung.cs,Fehler/AusnahmeBehandlung.cs,Program.cs}` + Registrierung -- Host-Durchsetzung, Stufe als erste.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs` + `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` + `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs` -- Card „Module“ (nach „Anmeldelinks“): Satz „Abgeschaltete Module erscheinen nirgends in Zeltlotse. Ihre Daten bleiben erhalten und sind nach dem Einschalten wieder da.“, drei `AutosaveAnkreuzfeld` „Verpflegung“, „Finanzen“, „Teilnehmer-Zugang“ (Pfad `ModulePfad`, Feldnamen aus `ModulFelder`); Kopfkommentar ergänzen; bUnit: Card mit Häkchen nach Antwort, Gelaufen mit Konflikt-Text, `EinstellungenAntworten` bildet `/module` nach.
- `src/Zeltlotse.Api/Demo/{DemoDaten.cs,DemoKatalog.cs}` + `DemoDatenTests.cs` -- Schritt `ModuleAsync` nach `FragenAuswahlAsync`: Zeltlager Sommer 2027 Verpflegung und Finanzen an (nur Felder, die noch aus sind), Teilnehmer-Zugang aus; Kinderbibeltage ohne Module.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Theory `Module_Schalten` (Jonas, Einstellungen, Card „Module“, `AxeAsync`, `BildAsync` → `module-schalten-{handy,laptop}`); geänderte Einstellungs-Bilder neu erzeugen, Rauschen per Pixelvergleich zurücksetzen.
- `doku/src/content/docs/leitung/module-schalten.md` (neu: was die drei Module sind, an- und abschalten, abgeschaltet = nirgends sichtbar und auch über Links nicht erreichbar, Daten bleiben, nicht nach dem Abschluss, nur die Leitung; Abschnitt „Noch nicht dabei“: Verpflegung, Finanzen und Teilnehmer-Zugang selbst folgen) + Links in `leitung/index.md` (Zeilen 22 und 27), `kueche/index.md:18`; `DokuRegeln.cs` PflichtSeiten.

**Acceptance Criteria:**
- Given Miriam schaltet in den Einstellungen von X „Verpflegung“ an, when Tobias den Kontext von X lädt, then enthält `schalter` „Verpflegung“, und nach dem Abschalten nicht mehr.
- Given ein Endpunkt mit `.GehoertZuSchalter(Verpflegung)`, when der Schalter der Freizeit aus ist, then antwortet er für Leitung und Team `404` und nach dem Einschalten wieder `2xx`.
- Given das Team-Konto Tobias, when es die Einstellungen von X öffnet, then sieht es keine Card „Module“ (Seite nicht verfügbar).
- Given die fertige Story, when `dotnet test` (ganze Lösung inkl. Architektur, UI und Doku-E2E) und `npm run build` in `doku/` laufen, then sind beide fehlerfrei und `module-schalten-handy.png`/`-laptop.png` liegen unter `doku/src/assets/screenshots/`.

## Spec Change Log

## Review Triage Log

### 2026-10-14 — Review pass
- verdicts: 28 findings — high 0, medium 0, low 17, false 9, maybe-false 2
- findings:
  - `[low]` `[patch]` (Blind) Ankreuzfeld bleibt nach `409` mit `KonfliktText` umgeschaltet, ohne „neu laden“ — `AutosaveAnkreuzfeld` setzt bei Konflikt mit `KonfliktText` die Eingabe auf den gespeicherten Wert zurück; bUnit prüft das Häkchen.
  - `[false]` `[reject]` (Blind) Seite in Gelaufen nicht schreibgeschützt — die Spec verlangt „nur lesbar … Konflikt-Text wie die übrigen Felder der Seite“; alle Felder der Einstellungen verhalten sich so, der Server lehnt mit `409 freizeit-abgeschlossen` ab.
  - `[low]` `[reject]` (Blind) Phasenwechsel bei offener Seite zeigt Konflikt- statt Abschluss-Text — Gelaufen entsteht nur durch den Abschluss der Leitung (3-8); selten, gleiches Verhalten aller Felder; Abhilfe bräuchte Problemcode-Auswertung im Autosave-Baustein.
  - `[low]` `[patch]` (Blind) Reihenfolge Rolle vor Schalter (`403` bei falscher Rolle trotz ausgeschaltetem Schalter) ungetestet — Test-Endpunkt nur für die Leitung mit Schalter ergänzt, Tobias bekommt `403`.
  - `[low]` `[patch]` (Blind) Weg der Pipeline-Ablehnung bis HTTP ungetestet — siehe Verification-gap-Zeile, gleiche Ursache.
  - `[false]` `[reject]` (Blind) Zwei verschiedene `404`-Körper verraten das Modul — beide Wege schreiben über denselben `IProblemDetailsService` nur `Status = 404` (`ClientAuslieferung.UseStatuscodeSeiten`, `AusnahmeBehandlung`), dazu `traceId`; der neue Test vergleicht die Feldnamen.
  - `[low]` `[patch]` (Blind) `PATCH /module` ohne Sitzung ungetestet — Fall `401` ergänzt.
  - `[low]` `[reject]` (Blind) Leitplanke ohne Teilnehmer-Zugang und ohne Command-Regel — Form der QR-Routen (Token, V6) steht noch nicht fest; eine Regel ohne Zielmenge wäre Spekulation.
  - `[low]` `[reject]` (Blind) Kontext liest die Freizeit zweimal — eine kleine zusätzliche Abfrage; die Spec verlangt die gemeinsame Schnittstelle `IFreizeitSchalterLesen` für den Kontext.
  - `[false]` `[reject]` (Blind) `SchalterStufe` scheitert außerhalb des Web-Hosts — `AddMediator` mit der Stufe gibt es nur im API-Host, der `WebSicherheit` immer registriert; die Demo läuft im selben Host.
  - `[false]` `[reject]` (Blind) Domäne arbeitet mit `"true"`/`"false"` — gleiches Muster wie `FeldAendern`/`FragenAendern` (AD-19-Altwert als Text), kein benannter Schaden.
  - `[low]` `[patch]` (Blind) Doku beschreibt noch fehlende Module im Präsens — Hinweis, dass die Module selbst später kommen, an den Anfang der Seite gezogen.
  - `[low]` `[patch]` (Blind) Demo-Test mit festem Namen „Kinderbibeltage 2027“ — nutzt `DemoKatalog.ZweiteFreizeit.Name`.
  - `[false]` `[reject]` (Blind) Zwei alte Screenshots ohne Grund geändert — Pixelvergleich: `freizeitpass-fragen-laptop` 15 535, `schutzkonzept-einrichten-laptop` 3 285 abweichende Pixel (längere Seite), Rauschen (≤ 340 Pixel) zurückgesetzt.
  - `[low]` `[patch]` (Edge) Ankreuzfeld bleibt nach `409` umgeschaltet — gleiche Ursache wie Blind-Zeile 1.
  - `[low]` `[reject]` (Edge) Phasenwechsel bei offener Seite — gleiche Zeile wie Blind oben.
  - `[low]` `[reject]` (Edge) `PATCH` ohne `alterWert` ergibt `409` statt `400` — der Client schickt immer den Altwert; gleiches Verhalten wie die übrigen Autosave-Endpunkte.
  - `[maybe-false]` `[reject]` (Edge) Schalter wird zwischen Stufe und Transaktion abgeschaltet — klärbar nur mit einem echten Modul-Command (V4); wäre höchstens low (ein Schreibvorgang im Augenblick des Abschaltens, Daten bleiben ohnehin).
  - `[maybe-false]` `[reject]` (Edge) `SchalterStand` speichert ein leeres Ergebnis vor gesetztem Mandanten — Benutzer-Commands kommen nur aus Endpunkten nach `RechtePruefung`, die den Mandanten setzt; klärbar mit einem `ISchalterCommand` ohne Freizeit-Route (gibt es nicht); wäre höchstens low.
  - `[low]` `[patch]` (Verification gap) Registrierung der `SchalterStufe` als erste Stufe und Abbildung `SchalterAusAusnahme` → `404` ungetestet — Test-Endpunkt wirft die Ausnahme (`404`, ProblemDetails ohne `ausnahmeTyp`/`errors`, gleiche Felder wie der abgeschaltete Endpunkt); Test prüft im Host, dass `SchalterStufe` die erste Stufe für `ModulAendernCommand` ist.
  - `[low]` `[reject]` (Verification gap, other) `Enum.IsDefined` im Validator vom Endpunkt aus unerreichbar — harmlose Absicherung des Commands, z. B. für Demo-Aufrufe.
  - `[false]` `[reject]` (Intent) Wirkung nur an Test-Endpunkt und Test-Nachrichten, keine echte Modul-Oberfläche — Küche, Finanzen und Teilnehmer-Zugang sind eigene Stories (V4–V6 in stories.yaml); diese Story liefert Schalter und Durchsetzung.
  - `[false]` `[reject]` (Intent) „Daten bleiben“ nur an Freizeitseite und Fragen belegt — Moduldaten gibt es noch nicht; Abschalten ändert nur die Spalte.
  - `[low]` `[patch]` (Intent) Doku im Präsens — gleiche Ursache wie Blind-Zeile 12.
  - `[low]` `[patch]` (Intent) `PATCH` ohne Sitzung ungetestet; Token-Fälle fehlen — `401` ergänzt (gleiche Zeile wie Blind); Token-Fälle entfallen, weil kein Token-Endpunkt existiert.
  - `[false]` `[reject]` (Intent) Test-zuerst im Diff nicht sichtbar — Prozessangabe, kein Fehler im Code.
  - `[low]` `[reject]` (Intent) Screenshot nur statisch — Umschalten ist per bUnit und HTTP belegt; gleiches Muster wie 3-1 bis 3-6.
  - `[false]` `[reject]` (Intent) Test-Endpunkt außerhalb der anonymen Test-Gruppe und zweiter Test-Endpunkt — Abweichung nötig, damit „ohne Sitzung 401“ prüfbar ist; der zweite belegt den Konfigurationsfehler; Verhalten wie verlangt.

## Design Notes

- **Vorgabe aus:** FR-44 nennt Kalkulation und Finanzen optional, NS: „schalten Module an“. Eine neue Freizeit startet schlank; die Leitung schaltet ein, was sie braucht. Für bestehende Freizeiten ändert sich nichts, weil es noch keine Modul-Endpunkte gibt.
- **Drei Spalten statt Liste:** fest drei Schalter laut AD-20; Spalten passen zum AD-19-Feld-Autosave mit Altwert-Vergleich je Feld (Konflikt nur bei demselben Feld).
- **Durchsetzung zweifach:** Der Host prüft am Endpunkt (`404`, AD-20 „Wirkung“; gelesen einmal pro Anfrage, AD-17); die Pipeline-Stufe prüft Benutzer-Commands, die ihren Schalter über `ISchalterCommand` erklären (AD-20 „Daten und Ereignisse“). Ereignis-Empfänger tragen nie `IBenutzerCommand` und laufen weiter. Heute erklärt noch kein Endpunkt und kein Command einen Schalter; die Wirkung belegen Test-Endpunkt und Test-Nachrichten, die Leitplanke in `EndpunktListeTests` sorgt dafür, dass V4/V5 sie nutzen.
- **Reihenfolge Rolle vor Schalter:** Ohne Rolle bleibt es `404`, mit falscher Rolle `403` – auch bei ausgeschaltetem Schalter verrät die Antwort nichts über Module, die die Person nicht sehen darf.
- **Eigenes Enum im SharedKernel:** SharedKernel referenziert `Zeltlotse.Api.Contracts` nicht (Muster `FreizeitRolle`); die Freizeiten-Endpunkte bilden auf `Api.Contracts.Gemeinsam.FunktionsSchalter` ab.
- **Kein Schalter-Verbergen in der Navigation nötig:** Es gibt noch keine Einträge für Küche, Finanzen oder Teilnehmer-Zugang; V4–V6 blenden sie über `Kontext.Schalter` ein.
- **Gelaufen:** Schalten ist eine Einstellung; nach dem Abschluss gilt der Schreibschutz (AD-17, keine Ausnahme).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler
- `dotnet test --project tests/Zeltlotse.SharedKernel.Tests`, `tests/Zeltlotse.Freizeiten.Tests`, `tests/Zeltlotse.Api.Tests` (Filter Module/FreizeitKontext/EndpunktListe/Demo), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- `dotnet test` (ganze Lösung inkl. E2E) einmal am Ende -- grün, Screenshots erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Module pro Freizeit schalten (CAP-26, FR-44, AD-20).
- **Schalter an der Freizeit:** drei Spalten Verpflegung, Finanzen, Teilnehmer-Zugang (Vorgabe aus), `GET/PATCH /api/freizeiten/{freizeitId}/module` nur für die Leitung, Autosave nach AD-19 (Konflikt je Modul, `409 freizeit-abgeschlossen` nach dem Abschluss). Der Kontext-Endpunkt liefert die eingeschalteten Schalter.
- **Durchsetzung im Host:** Endpunkt-Merkmal `.GehoertZuSchalter(...)`; `RechtePruefung` antwortet nach bestandener Rollenprüfung mit `404`, wenn der Schalter aus ist (Stand je Freizeit einmal pro Anfrage über `SchalterStand`). Neue erste Pipeline-Stufe `SchalterStufe` lehnt Benutzer-Commands mit `ISchalterCommand` bei ausgeschaltetem Schalter ab (`SchalterAusAusnahme` → `404`); Ereignis-Empfänger laufen weiter. Leitplanke in `EndpunktListeTests` für künftige Küchen- und Finanzen-Routen.
- **Web-App:** Card „Module“ in den Einstellungen der Freizeit mit drei Ankreuzfeldern; `AutosaveAnkreuzfeld` kann einen eigenen Konflikttext zeigen (abgeschlossene Freizeit) und setzt dann das Häkchen zurück.
- **Demo-Daten:** Zeltlager mit Verpflegung und Finanzen, Kinderbibeltage ohne Module.
- **Doku:** neue Seite `leitung/module-schalten.md`, Links in `leitung/index.md` und `kueche/index.md`, zwei neue Screenshots.

**Dateien (Auswahl):**
- `src/Zeltlotse.Api.Contracts/Freizeiten/Module.cs` -- Datenformen `FreizeitModule`, `ModulFeld`, `ModulFelder`.
- `src/Zeltlotse.SharedKernel/{Rechte/FunktionsSchalter.cs,Rechte/ISchalterStand.cs,Rechte/Rechtemerkmale.cs,Commands/IModulCommand.cs,Pipeline/SchalterStufe.cs,Pipeline/SchalterAusAusnahme.cs}` -- Merkmal, Schnittstellen, Pipeline-Stufe.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Schalter/IFreizeitSchalterLesen.cs` -- lesende Schnittstelle der Schalter.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{Domaene/Freizeit.cs,Module/ModuleBearbeiten.cs,Rechte/FreizeitRechte.cs,FreizeitenModul.cs,Startseite/FreizeitEndpunkte.cs,Daten/FreizeitenDbContext.cs}`, Migration `Module` -- Speicher, Slice, Routen, Kontext.
- `src/Zeltlotse.Api/{Rechte/SchalterStand.cs,Rechte/RechtePruefung.cs,Fehler/AusnahmeBehandlung.cs,Program.cs,Sicherheit/WebSicherheit.cs}` -- Host-Durchsetzung.
- `src/Zeltlotse.Ui/{Api/FreizeitenApiClient.cs,Autosave/AutosaveAnkreuzfeld.razor}`, `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- Oberfläche.
- `src/Zeltlotse.Api/Demo/{DemoDaten,DemoKatalog}.cs` -- Demo-Schritt `ModuleAsync`.
- Tests: neu `tests/Zeltlotse.Api.Tests/ModuleTests.cs`, `tests/Zeltlotse.Freizeiten.Tests/ModuleTests.cs`; ergänzt `PipelineTests`, `EndpunktListeTests`, `FreizeitKontextTests`, `DemoDatenTests`, `TestHelfer`, `FreizeitpassSzenario`, `FreizeitenSeitenTests`, `NachweisSeitenTests`, `DokuRegeln`, `DokuTests`, E2E `Module_Schalten`.
- Doku und Screenshots: `doku/src/content/docs/leitung/module-schalten.md` (neu), `leitung/index.md`, `kueche/index.md`; neu `module-schalten-{handy,laptop}.png`; neu erzeugt `freizeitpass-fragen-laptop`, `schutzkonzept-einrichten-laptop` (Seite länger), Rauschen per Pixelvergleich zurückgesetzt.

**Review:** 28 Befunde (high 0, medium 0, low 17, false 9, maybe-false 2).
- **Gepatcht (low):** Häkchen nach `409` mit Konflikttext zurückgesetzt; Test „Rolle vor Schalter“ (`403`); Host-Tests für `SchalterAusAusnahme` → `404` und `SchalterStufe` als erste Stufe; `PATCH` ohne Sitzung `401`; Doku-Hinweis oben, dass die Module selbst später kommen; Demo-Test mit Katalog-Konstante.
- **Zurückgestellt:** keine.
- **Abgelehnt:** 20, Gründe im Review Triage Log (Modul-Oberflächen kommen mit V4–V6, gleiche 404-Körper, seltene Phasenwechsel, kein Token-Endpunkt, theoretische Wettläufe ohne echten Modul-Command).

**Folge-Review empfohlen:** nein (gepatcht: high 0, medium 0, low 6 Einträge).

**Verifikation:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler, 0 Warnungen.
- **Komplettlauf vor dem Review** (je Projekt, nach der Umsetzung): E2E 155/155 mit Screenshots, Api 808/808, Architecture 133, Ui 648, SharedKernel 104, Freizeiten 88, Programm 91, Personen 153, Organisationen 58, Inventar 34, Sicherheit 36, Konten 26, Ablaeufe 10 -- alle grün.
- **Nach den Patches:** Ui 648/648, Architecture 133/133, SharedKernel 104/104, Api gefiltert (Module, Fehler, Demo, EndpunktListe, FreizeitKontext) 66/66, E2E 155/155 -- grün.
- `npm run build` in `doku/` -- fehlerfrei, 54 Seiten.
- Matrix-Audit: jede Zeile der I/O-Matrix ist durch grüne Tests in `Api.Tests/ModuleTests`, `Freizeiten.Tests/ModuleTests` und `PipelineTests` abgedeckt.

**Restrisiken:**
- Noch kein echter Endpunkt und kein echter Command erklärt einen Schalter; die Wirkung ist über Test-Endpunkte und Test-Nachrichten belegt. V4–V6 müssen `.GehoertZuSchalter` und `ISchalterCommand` nutzen (Leitplanke nur für Küche- und Finanzen-Routen).
- Die Doku beschreibt die Wirkung abgeschalteter Module vorab; die Module selbst fehlen noch (oben auf der Seite vermerkt).
- In einer abgeschlossenen Freizeit sind die Kästchen klickbar und zeigen erst nach dem Klick „nur lesbar“ (wie die übrigen Felder der Einstellungen).
