---
title: 'Story 6-3: Ansagen-Zettel und Besprechungsnotizen'
type: 'feature'
created: '2026-10-20'
baseline_revision: '00dd06049e5fcf029a5226ccc4867a0fb3a714f2'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/6-1-kanaele-team-und-alle-mit-push.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Nach einem gescheiterten Tipp zeigt das Kästchen wieder den Serverstand (_runde im @key), das prüft aber kein Test.
    evidence: |-
      bUnit ändert den DOM-Zustand checked bei Change() nicht; nur ein Playwright-Test mit erzwungenem Serverfehler kann das Zurücksetzen beobachten.
    location: >-
      src/Zeltlotse.App/Features/Kanaele/KanalNotizen.razor
    severity: low
---

<intent-contract>

## Intent

**Problem:** Ansagen fürs nächste Essen und Punkte für die Mitarbeiterbesprechung landen auf Zetteln oder in WhatsApp. In der Besprechung fehlt eine gemeinsame Liste, die man abhaken kann (CAP-37, FR-63, Glossar „Ansagen-Zettel“).

**Approach:** Das Modul Kanäle bekommt je Freizeit zwei Notiz-Listen: den Ansagen-Zettel und die Besprechungspunkte. Leitung und Team legen dort einen Punkt mit nur einem Text an und haken ihn mit einem Tipp ab oder öffnen ihn wieder. In der Oberfläche sind es eigene Reiter unter „Kanäle“ (EXPERIENCE.md, Kanal-Nachrichten).

## Boundaries & Constraints

**Always:**
- Entität `Notiz : IOrganisationsEigen` im Modul Kanäle mit `Id`, `OrganisationId`, `Laufnummer` (identity), `FreizeitId`, `Art` (`Ansage = 1`, `Besprechung = 2`), `Text`, `AngelegtAm` und `ErledigtAm?`. Die Notiz hat **keine Personen-Kennung**: kein Autor, kein „abgehakt von“ (FR-60 verlangt das nur für Beiträge).
- Endpunkte unter `/api/freizeiten/{freizeitId}/kanaele/notizen`, alle mit `.NurFreizeitRolle(Mitarbeit)` und ohne Funktions-Schalter (wie 6-1):
  - `GET /notizen/{art}` mit `art` = `ansagen` | `besprechung` liefert `NotizListe(Art, Offen, Abgehakt)`.
    - `Offen` enthält alle offenen Punkte, älteste zuerst.
    - `Abgehakt` enthält die zuletzt abgehakten, höchstens 50, zuletzt abgehakte zuerst.
  - `POST /notizen/{art}` mit `{text}` → 201 und `KanalNotiz`.
  - `POST /notizen/{notizId}/erledigt` und `POST /notizen/{notizId}/offen` → 200 und `KanalNotiz`. Beide sind idempotent; ein wiederholtes Abhaken behält das erste `ErledigtAm`.
- Schreibende Commands tragen `IBenutzerCommand` und `IFreizeitCommand`. Bei *gelaufen* kommt deshalb aus der `SchreibschutzStufe` 409 `freizeit-abgeschlossen`. `ToString()` gibt keinen Text aus (AD-22).
- Text: Pflicht, nach Trim nicht leer, höchstens 500 Zeichen. Feldfehler `text`.
- Datenschutz nach AD-16: Notizen sind Arbeitsdaten der Freizeit und werden im `KanaeleLoeschlauf` mit der Freizeit gelöscht. Weil die Notiz keine Personen-Kennung hat, braucht sie keine Datenauskunft und keinen Empfänger für `PersonGeloescht`.
- Oberfläche nach EXPERIENCE.md und DESIGN.md (Liste mit Checkbox, Abhaken ist ein Tipp, Bearbeiten an Ort und Stelle, keine Pop-ups):
  - Reiter „Nachrichten · Ansagen-Zettel · Besprechung“ auf der Kanal-Seite und auf der neuen Notiz-Seite.
  - Die Notiz-Seite enthält ein Eingabefeld mit „Hinzufügen“, offene Punkte als `Ankreuzfeld`-Liste und darunter „Abgehakt“ mit gesetzten Kästchen. Ein Tipp öffnet einen Punkt wieder.
  - Bei *gelaufen* steht eine Meldung da, das Eingabefeld fehlt, und die Kästchen sind deaktiviert.

**Never:**
- Kein Push und keine Mail zu Notizen, kein Ereignis, kein neuer Pfeil, keine Änderung an ARCHITECTURE-SPINE.md.
- Kein Bearbeiten oder Löschen einzelner Punkte, keine Zuordnung zu Mahlzeit, Tag oder Person, keine Sortierung von Hand.
- Kein Zugang für Teilnehmende oder QR-Gäste (6-4), nichts offline (6-5).

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehler |
|---|---|---|---|
| Anlegen | Team, POST `.../notizen/ansagen` `{text:"Taschenlampen mitbringen"}` | 201 `KanalNotiz` mit `erledigtAm: null`; GET ansagen zeigt den Punkt unter `offen`, GET besprechung nicht | – |
| Text ungültig | leer, nur Leerzeichen, > 500 Zeichen | 400, Feldfehler `text`, nichts gespeichert | Validierung |
| Unbekannte Art | `.../notizen/foo` (GET, POST) | 404 | – |
| Abhaken | POST `.../notizen/{id}/erledigt` | 200 mit `erledigtAm`; der Punkt steht unter `abgehakt`; wiederholt 200 mit gleichem `erledigtAm` | – |
| Wieder öffnen | POST `.../notizen/{id}/offen` | 200, `erledigtAm: null`, wieder unter `offen` | – |
| Fremde Notiz | `notizId` einer anderen Freizeit (auch derselben Organisation) oder unbekannt | 404 | – |
| Abgehakt-Grenze | 51 abgehakte Punkte | `abgehakt` enthält die 50 zuletzt abgehakten | – |
| Rechte | Leitung, Team → 2xx; Org-Admin ohne Rolle, fremde Organisation, unbekannte Freizeit → 404; ohne Sitzung → 401, auf jeder der vier neuen Routen | wie angegeben | – |
| Gelaufen | Freizeit abgeschlossen; POST anlegen, erledigt, offen | 409 `freizeit-abgeschlossen`, nichts geändert; GET bleibt 200 | – |
| Löschlauf | Freizeit X mit abgelaufener Frist | Notizen von X gelöscht, die von Z bleiben | – |

</intent-contract>

## Code Map

Wurzel: `src/` = S, `tests/` = T, KM = `S/Modules/Kanaele/Zeltlotse.Kanaele`.

- `KM/KanaeleModul.cs` L26 `Mitarbeit`, L48-53: Routengruppe `/freizeiten/{freizeitId:guid}/kanaele`, jede Route mit `.NurFreizeitRolle(Mitarbeit)`. Die neuen Routen kommen in dieselbe Gruppe. `/{kanal}/nachrichten` hat zwei Segmente, ein Literal `notizen` hat bei ASP.NET-Routen Vorrang.
- `KM/Nachrichten/NachrichtSenden.cs`: Vorlage für Command, Validator (Konstanten, `OverridePropertyName(KanalFelder.Text)`), Handler (`IMandantenKontext`, `TimeProvider`, `Kennung.Neu(uhr)`, kein SaveChanges) und Endpunkt (`TypedResults.Created`). `KM/Nachrichten/NachrichtenLaden.cs`: GET über `KanaeleLeseDbContext`.
- `KM/Nachrichten/Nachricht.cs`: Vorlage der Entität (privater Konstruktor, Fabrik). Die Notiz ist kein `Aggregat`, weil sie keinen Push vormerkt.
- Vorlage Abhaken: `S/Modules/Programm/Zeltlotse.Programm/Aufgaben/AufgabeUebergang.cs` (`…/erledigt` und `…/offen`, 200 mit Objekt, idempotent). Die eigene Phasenprüfung dort ist doppelt; in Kanäle genügt die Pipeline (`S/Zeltlotse.SharedKernel/Pipeline/SchreibschutzStufe.cs`).
- `KM/Daten/KanaeleDbContext.cs`: `DbSet` in beiden Kontexten und Konfiguration in `KanaeleModell.Konfigurieren` (Tabelle `notiz`, Index `(FreizeitId, Art)`, `OrganisationId`). Neue Migration `<Zeitstempel>_Notizen` nach `20261020120748_Kanaele` über die vorhandene `KanaeleDbContextFabrik` (`dotnet ef migrations add`).
- `KM/Datenschutz/KanaeleLoeschlauf.cs`: dort eine `ExecuteDeleteAsync`-Zeile für Notizen ergänzen.
- `S/Zeltlotse.Api.Contracts/Kanaele/Kanaele.cs`: Enum `Kanal`, `KanalFelder` (`Text`, `AusRoute`/`InRoute`). Neu kommen hinzu:
  - `NotizArt { Ansage = 1, Besprechung = 2 }` mit `JsonStringEnumConverter`,
  - `KanalNotiz(Guid Id, NotizArt Art, string Text, DateTimeOffset AngelegtAm, DateTimeOffset? ErledigtAm)`,
  - `NotizNeu(string? Text)` und `NotizListe(NotizArt Art, IReadOnlyList<KanalNotiz> Offen, IReadOnlyList<KanalNotiz> Abgehakt)`,
  - in `KanalFelder`: `NotizTextMaximaleLaenge = 500`, `HoechstzahlAbgehakt = 50`, `NotizArtAusRoute` und `NotizArtInRoute`.
- `S/Zeltlotse.Ui/Api/KanaeleApiClient.cs`: Muster `LadenAsync` (null bei 403/404), `SendenAsync` (Feldfehler über `ApiFehler.AlleFeldFehlerAsync`) und `FehlerAsync` (409 `Problemcodes.FreizeitAbgeschlossen`). Ergebnistypen `KanalAktionErgebnis` und `KanalSendenAktion`.
- `S/Zeltlotse.App/Features/Kanaele/Kanaele.razor` (Klasse `KanaeleSeite`, Route `/freizeiten/{FreizeitId:guid}/kanaele`):
  - L29 Segmente, L137-141 Query und `Abgeschlossen` aus `Kontext.LadenAsync`,
  - L143ff. Laden mit `NichtVerfuegbar` und `AnmeldungAbschluss.IstAktionsfehler`.
  - `KanaeleTexte.cs` enthält Texte und `Ziel(...)`.
- `S/Zeltlotse.Ui.Komponenten/Reiter.razor` (`ReiterEintrag(Href, Aktiv, Text)`; Vorbild `S/Zeltlotse.App/Features/Finanzen/FinanzenReiter.razor`), `Ankreuzfeld.razor` (Vorbild für eine Liste: `S/Zeltlotse.App/Features/Finanzen/Beitraege.razor` L65), `LeerZustand`, `Meldung`, `Formular`, `Feld`, `Knopf`.
- Tests:
  - `T/Zeltlotse.Api.Tests/KanaeleTests.cs`: Setup über `FreizeitpassSzenario` (`_s.X`, `_s.Z`, Miriam Leitung, Tobias Team).
    - Rechte L477-517 (`AlleRouten`, `StatusAllerRoutenAsync`), Gelaufen L428-476 (`UPDATE freizeiten.freizeit SET "AbgeschlossenAm"`).
    - Löschlauf L563ff. (Tabellenliste `nachricht`, `push_abo`).
    - Ein eigener Testfile `KanalNotizenTests.cs` nach demselben Muster ist erlaubt.
  - `T/Zeltlotse.Api.Tests/EndpunktListeTests.cs`: L340-345 (Routenliste), L377-379 (`Assert.Equal(5, kanaele.Count)`, dann 9).
  - `T/Zeltlotse.Ui.Tests/KanaeleSeitenTests.cs`: `Antworten(phase)`, `Rendern`, `Posts`, `Knopf<T>`.
  - `T/Zeltlotse.ArchitectureTests/LoeschPflichtTests.cs` greift nur bei einer Eigenschaft `PersonId`; die Notiz hat keine.
- Demo und Doku:
  - `S/Zeltlotse.Api/Demo/DemoDaten.cs` L195 und L951-975 `KanaeleAsync` (idempotent, echte Commands), `DemoKatalog.cs` L144-153 `KanalNachrichten`, `T/Zeltlotse.Api.Tests/DemoDatenTests.cs` L405-414.
  - `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: L2555-2640 `Kanaele_Team`, `Kanaele_Alle`, `KanaeleAsync(seite)` (Lea), `ZeitenFestAsync`, `E2EHilfe.AxeAsync`, `BildAsync`.
  - `doku/src/content/docs/mitarbeitende/kanaele.md` (L65 „Noch nicht dabei“) und `doku/src/content/docs/daten.md` (Abschnitt Kanäle).
  - `T/Zeltlotse.ArchitectureTests/DokuRegeln.cs`: Jedes PNG muss verlinkt sein.

## Tasks & Acceptance

**Execution (testgetrieben: zuerst der Test, dann der Code):**
- `T/Zeltlotse.Api.Tests/KanalNotizenTests.cs` (neu): HTTP-Tests gegen PostgreSQL für jede Matrix-Zeile.
  - Dazu die Rechte-Tabelle über die vier neuen Routen (Leitung und Team 2xx, sonst 404/401), Gelaufen und Löschlauf.
  - In `EndpunktListeTests.cs` die vier Routen eintragen und die Anzahl auf 9 setzen.
- `S/Zeltlotse.Api.Contracts/Kanaele/Kanaele.cs`: die Verträge und Konstanten aus der Code Map.
- `KM/Notizen/Notiz.cs`, `NotizAnlegen.cs`, `NotizenLaden.cs` und `NotizUebergang.cs` (alle neu): Entität mit `Anlegen`, `Abhaken(jetzt)` und `Oeffnen()`, dazu Commands, Validator, Handler und Endpunkte. Registrierung in `KanaeleModul.cs`.
- `KM/Daten/KanaeleDbContext.cs` und `KM/Migrationen/*_Notizen*`: Tabelle `notiz` samt Snapshot.
- `KM/Datenschutz/KanaeleLoeschlauf.cs`: Notizen löschen, im Test `notiz` in die Tabellenliste aufnehmen.
- `S/Zeltlotse.Ui/Api/KanaeleApiClient.cs`: `NotizenLadenAsync`, `NotizAnlegenAsync` (Erfolg, Abgeschlossen oder Fehler, dazu Feldfehler) und `NotizUmschaltenAsync(freizeitId, notizId, erledigt)`.
- `S/Zeltlotse.App/Features/Kanaele/KanalNotizen.razor` (neu, Route `/freizeiten/{FreizeitId:guid}/kanaele/{Art}` für `ansagen` und `besprechung`), dazu `KanaeleReiter.razor` (neu, auch in `Kanaele.razor` eingebunden) und `KanaeleTexte.cs`. Bei unbekannter Art erscheint `NichtVerfuegbar`.
  - bUnit-Tests in `T/Zeltlotse.Ui.Tests/KanalNotizenSeitenTests.cs`:
    - Liste offen und abgehakt; Hinzufügen leert das Feld und lädt neu.
    - Ein Feldfehler steht am Feld.
    - Ein Tipp ruft `erledigt` bzw. `offen` auf.
    - Leerzustand; gelaufen ohne Eingabe und mit deaktivierten Kästchen; Reiter mit `aria-current`.
- `S/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs` und `DemoDatenTests.cs`: idempotent über die echten Commands.
  - Ansagen-Zettel: zwei offene Punkte.
  - Besprechung: drei Punkte, davon einer abgehakt.
- `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: Bilder `kanaele-ansagen` und `kanaele-besprechung` für Handy und Laptop.
  - Lea öffnet die Reiter. Der Test hakt nichts ab, damit der Demo-Stand bleibt.
  - Danach die axe-Prüfung.
- `doku/src/content/docs/mitarbeitende/kanaele.md`: neuer Abschnitt „Ansagen-Zettel und Besprechung“ mit allen vier Bildern; „Noch nicht dabei“ wird angepasst. In `doku/src/content/docs/daten.md` kommt ein Satz zu den Notizen hinzu (keine Personenangabe, Löschen mit der Freizeit).

**Acceptance Criteria:**
- Gegeben Lea im Reiter „Besprechung“ am Handy: Sie tippt einen Punkt ein und wählt „Hinzufügen“. Dann steht der Punkt ohne Pop-up in der offenen Liste und ist auch für Miriam sichtbar.
- Gegeben die Mitarbeiterbesprechung am Abend: Ein Tipp auf das Kästchen eines Punkts schiebt ihn nach „Abgehakt“. Ein weiterer Tipp holt ihn zurück.
- Gegeben die fertige Story: `dotnet test` inklusive der Architekturtests ist grün. `kanaele-ansagen-*.png` und `kanaele-besprechung-*.png` für Handy und Laptop sind erzeugt und in der Doku verlinkt. `npm run build` in `doku/` läuft fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: 26 findings — high 0, medium 1, low 21, false 4, maybe-false 0
- findings:
  - `[low]` `[patch]` (Verification Gap) Keine Seitentests für 409 `freizeit-abgeschlossen` beim Hinzufügen und Abhaken — neue bUnit-Tests `Hinzufuegen_nach_dem_Abschluss_…` und `Abhaken_nach_dem_Abschluss_…` (Kontext erst „läuft“, dann „gelaufen“; Hinweis genau einmal, Eingabe weg, Kästchen deaktiviert).
  - `[low]` `[patch]` (Verification Gap) Reiterwechsel auf derselben Komponente ungetestet — neuer Test `Reiterwechsel_auf_derselben_Seite_leert_den_Entwurf_und_laedt_die_andere_Liste`.
  - `[low]` `[defer]` (Verification Gap) Zurücksetzen des Kästchens nach gescheitertem Tipp (`_runde` im `@key`) beobachtet kein Test — nur mit Browser-Test und erzwungenem Serverfehler prüfbar; laut Disposition des Layers zurückgestellt.
  - `[low]` `[reject]` (Edge Case) Reiterwechsel während des Ladens: ältere Antwort überschreibt `_liste` — braucht sehr schnellen Doppel-Tipp; Fix wäre ein zusätzlicher Wächter, gleiches Muster wie `Kanaele.razor`.
  - `[low]` `[reject]` (Edge Case) Reiterwechsel während Hinzufügen/Umschalten lädt die alte Liste — wie oben, selten, Fix fügt Wächter hinzu.
  - `[low]` `[reject]` (Edge Case) Entwurf bleibt beim Wechsel der Freizeit mit gleicher Liste — es gibt keinen direkten Weg von der Notiz-Seite einer Freizeit zur gleichen Seite einer anderen; Fix ist ein zusätzlicher Wächter.
  - `[low]` `[patch]` (Edge Case) Abschluss-Satz doppelt (Fehler-Meldung und Hinweis) nach 409 beim Hinzufügen — `KanalNotizen.razor`: Meldung bzw. Fehler am Punkt nur noch, wenn der neu geladene Kontext nicht „gelaufen“ meldet.
  - `[false]` `[reject]` (Edge Case) Gleichzeitiges Abhaken/Öffnen: Last-Write-Wins verliert eine Wahl — `erledigt` und `offen` setzen einen Zustand, kein Umschalten; der letzte Tipp gewinnt, genau wie die Person ihn gewählt hat, nichts geht unbemerkt verloren.
  - `[low]` `[reject]` (Edge Case) Gleichstand bei `ErledigtAm` sortiert nach Anlage — Zeitstempel in Mikrosekunden, Gleichstand praktisch nur mit fester Testuhr; Fix bräuchte eine weitere Spalte.
  - `[low]` `[reject]` (Edge Case) Demo-Einspielung nach Abbruch halb fertig — gleiches Muster wie `KanaeleAsync` (Story 6-1); nur bei Abbruch mitten im Einspielen, Demo-Datenbank wird neu aufgesetzt.
  - `[low]` `[patch]` (Blind) Gleicher Satz doppelt nach 409 beim Hinzufügen und Abhaken — gleiche Ursache wie oben, gleiche Korrektur.
  - `[medium]` `[patch]` (Blind) Kein Hinweis gegen Gesundheitsdaten in Notizen — Satz in `kanaele.md` ergänzt: Gesundheitsdaten und vertrauliche Angaben über Personen gehören nicht in die Punkte.
  - `[low]` `[reject]` (Blind) Liste „Offen“ ohne Obergrenze — die Absicht verlangt ausdrücklich „alle offenen Punkte“; nur Leitung und Team können schreiben.
  - `[low]` `[reject]` (Blind) Gleichstand-Sortierung nach Anlage statt Abhak-Reihenfolge — wie Edge-Case-Befund oben.
  - `[false]` `[reject]` (Blind) Keine Konfliktbehandlung bei gleichzeitigem Umschalten — wie Edge-Case-Befund oben: Zustand statt Umschalten, keine verlorene Wahl.
  - `[false]` `[reject]` (Blind) Vertrags-Records geben Text in `ToString` aus — AD-22 betrifft Commands und Entitäten, die die Pipeline protokolliert; die Records werden nirgends geloggt, `KanalNachricht` aus 6-1 genauso.
  - `[low]` `[reject]` (Blind) E2E-Tests fügen nichts hinzu und haken nichts ab — Verdrahtung ist in bUnit getestet, der Server in HTTP-Tests; ein schreibender E2E-Test bräuchte eine eigene Freizeit.
  - `[low]` `[patch]` (Blind) Mehrere UI-Pfade ohne Test (Reiterwechsel, „Alles abgehakt.“, Neuladen scheitert) — Tests für Reiterwechsel und „Alles abgehakt.“ ergänzt; Neuladen-Fehler bleibt ungetestet (gleiches Muster wie 6-1).
  - `[false]` `[reject]` (Blind) „Card“ als unerklärter Begriff in der Doku — „Card“ ist durchgängig in der Anwender-Doku verwendet (z. B. `eltern/familienkonto.md`); keine benannte Schadwirkung.
  - `[low]` `[patch]` (Blind) Seitentitel passt nicht mehr zum Inhalt — `title` in `kanaele.md` zu „Kanäle, Ansagen-Zettel und Besprechung“, Linktexte in `mitarbeitende/index.md`, `leitung/index.md` und `programm.md` angepasst. Die h1 „Team“/„Alle“ der Nachrichtenseite bleibt (Segmente).
  - `[low]` `[reject]` (Blind) Hinweistext spricht nur vom „Tipp“ — Wortwahl der ganzen App und von EXPERIENCE.md; am Laptop verständlich.
  - `[low]` `[reject]` (Blind) Demo-Einspielung halb fertig nach Abbruch — wie Edge-Case-Befund oben.
  - `[low]` `[reject]` (Blind) 500-Zeichen-Grenze in UTF-16 statt Zeichen, kein `maxlength` — trifft nur Texte nahe 500 Zeichen mit Emojis; Feldfehler erklärt die Grenze, gleiches Muster wie Nachrichten.
  - `[low]` `[reject]` (Intent) Offene API-Entscheidungen (Trim vor Längenprüfung, Reihenfolge nach Anlage, Groß/Klein bei `art`, 409 vor 404) ohne eigenen Test — alle sind vertretbare Lesarten; kein Nutzerschaden benannt.
  - `[low]` `[reject]` (Intent) Bedienung nur gegen nachgebildeten Server und lesende E2E-Tests geprüft — wie Blind-Befund zu E2E.
  - `[low]` `[reject]` (Intent) AD-22 (`ToString` ohne Text) und „keine Personen-Kennung“ nicht per Test abgesichert — strukturell umgesetzt (Overrides, Migration ohne Personenspalte), gleiches Vorgehen wie 6-1/6-2.

## Design Notes

- **Ein Typ für beide Listen:** Ansagen-Zettel und Besprechungspunkte verhalten sich gleich (Text anlegen, abhaken). Eine Entität mit `Art` vermeidet doppelten Code. Beide liegen im Modul Kanäle, wie es die Modultabelle der Spine vorsieht.
- **Ohne Personen-Kennung:** Wer einen Punkt notiert oder abgehakt hat, verlangt FR-63 nicht. Ohne Kennung fallen Auskunft und Pseudonymisieren weg, und der Löschlauf genügt (Datensparsamkeit).
- **„Des Tages“ ohne Datumslogik:** Offene Punkte stehen oben, abgehakte verschwinden nach unten. In der Besprechung sieht man so genau das, was noch offen ist. Eine Zuordnung zu einem Tag wäre ein Pflichtfeld mehr (NFR-12).
- **Grenze 50 bei Abgehakt:** Die Liste bleibt bei langen Freizeiten kurz. Ältere Punkte bleiben gespeichert, bis die Freizeit gelöscht wird.
- **403 nicht erreichbar, keine Token-Fälle:** wie in 6-1 und 6-2. Alle Routen erlauben Leitung und Team, ohne Rolle kommt 404 (RechtePruefung). Der 403-Zweig ist über `/test/nur-team` abgedeckt. Es entsteht kein Token-Endpunkt.
- **Eigene Seite statt mehr Segmente:** Vier Segmente sind auf dem Handy zu eng. Die Reiter sind Links, die Wahl steht in der Adresse, und `Kanaele.razor` wächst nicht weiter.

## Verification

**Commands:**
- `dotnet test tests/Zeltlotse.Api.Tests` (gefiltert auf KanalNotizenTests, KanaeleTests, EndpunktListeTests, DemoDatenTests), `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- erwartet: grün.
- Am Ende einmal `dotnet test` für die ganze Lösung inklusive E2E der Kategorie Doku, danach `npm run build` in `doku/` -- erwartet: grün, fehlerfrei, die neuen PNGs liegen in `doku/src/assets/screenshots/`.

## Auto Run Result

Status: done

**Umsetzung:** Das Modul Kanäle hat je Freizeit zwei Notiz-Listen, Ansagen-Zettel und Besprechung. Dazu gehören die Entität `Notiz` ohne Personen-Kennung und vier Endpunkte unter `/api/freizeiten/{id}/kanaele/notizen` (Laden, Anlegen, erledigt, offen). Bei *gelaufen* antworten sie mit 409. Der Löschlauf entfernt die Notizen. In der Web-App gibt es die Reiter „Nachrichten · Ansagen-Zettel · Besprechung“ und die Notiz-Seite mit Kästchen-Liste. Außerdem: Demo-Daten, Screenshots für Handy und Laptop, Anwender-Doku.

**Geänderte Dateien (Kern):**
- `src/Modules/Kanaele/Zeltlotse.Kanaele/Notizen/*` – Entität, Anlegen, Laden, Übergang
- `…/Daten/KanaeleDbContext.cs`, `…/Migrationen/20261020190743_Notizen*` – Tabelle `notiz`
- `…/Datenschutz/KanaeleLoeschlauf.cs` – Notizen mit der Freizeit löschen
- `…/KanaeleModul.cs` – Routen registriert
- `src/Zeltlotse.Api.Contracts/Kanaele/Kanaele.cs` – Verträge und Konstanten
- `src/Zeltlotse.Ui/Api/KanaeleApiClient.cs` – Client-Methoden
- `src/Zeltlotse.App/Features/Kanaele/KanalNotizen.razor`, `KanaeleReiter.razor`, `KanaeleTexte.cs`, `Kanaele.razor` – Oberfläche
- `src/Zeltlotse.Api/Demo/DemoDaten.cs`, `DemoKatalog.cs` – Demo-Punkte
- Tests: `KanalNotizenTests.cs` (HTTP gegen PostgreSQL), `KanalNotizenSeitenTests.cs` (bUnit), `EndpunktListeTests.cs`, `KanaeleTests.cs`, `DemoDatenTests.cs`, `DokuScreenshotTests.cs`
- Doku:
  - `mitarbeitende/kanaele.md` mit neuem Abschnitt und Titel
  - `daten.md`
  - Linktexte in `mitarbeitende/index.md`, `leitung/index.md` und `programm.md`
  - vier neue Screenshots `kanaele-ansagen-*` und `kanaele-besprechung-*`

**Review:** 26 Befunde.
- Patches:
  - Der Abschluss-Satz erscheint nach 409 nur noch einmal (gruppiert, 3 Befunde, low).
  - Neue bUnit-Tests für 409 beim Hinzufügen und Abhaken, für den Reiterwechsel und für „Alles abgehakt.“ (low).
  - In der Doku ein Hinweis gegen Gesundheitsdaten in Punkten (medium).
  - Doku-Titel und Linktexte (low).
- Patch-Zählung nach Urteil: high 0, medium 1, low 5 Einträge.
- Zurückgestellt: ein Eintrag, das Zurücksetzen des Kästchens nach gescheitertem Tipp; nur per Browser-Test prüfbar.
- Abgelehnt: alle übrigen; die Gründe stehen im Triage-Log.

**Folge-Review empfohlen:** false. Es wurde kein high gepatcht, nur ein medium.

**Verifikation:**
- `dotnet build Zeltlotse.slnx`: fehlerfrei.
- `dotnet test` je Testprojekt grün:
  - Ablaeufe 10, Api 1043, E2E 197 (inklusive Doku-Screenshots), Finanzen 65, Freizeiten 93, Inventar 34
  - Konten 26, Kueche 119, Organisationen 58, Personen 157, Programm 109, SharedKernel 134
  - Sicherheit 36, Ui 853, ArchitectureTests 136
- Screenshots fremder Seiten, die der Lauf nur mit Rendering-Rauschen neu erzeugt hat, sind zurückgesetzt.
- `npm run build` in `doku/`: fehlerfrei, 67 Seiten.

**Restrisiken:**
- Die Seite behandelt Rennen beim schnellen Reiterwechsel während des Ladens nicht. Das gleiche Muster hat die Kanal-Seite.
- Die Kästchen-Rücksetzung nach einem Fehler ist nur im Code umgesetzt; kein Test prüft sie.
