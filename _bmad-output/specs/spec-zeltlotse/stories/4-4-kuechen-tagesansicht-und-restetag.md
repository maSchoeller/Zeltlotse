---
title: 'Story 4-4: Küchen-Tagesansicht und Restetag'
type: 'feature'
created: '2026-10-19'
status: 'done'
baseline_revision: 'ad9aabf991ee9b40461406dbc242fd1a4e0703b4'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/4-3-einkauf-freigeben-und-abhaken.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Die Küche hat Rezepte, Essensplan, Einkaufsliste und Einkauf (4-1 bis 4-3). Es fehlt die Sicht für den Kochtag: was heute ansteht und welche Allergien und Sonderkost zu beachten sind (FR-52). Außerdem kann die Küche eine Mahlzeit nicht spontan als Restetag markieren (FR-53, CAP-31).

**Approach:**
- Neue Lese-Route `GET kueche/tagesansicht?tag=` für einen Tag. Sie liefert:
  - die Mahlzeiten des Tags mit ihren Gerichten und deren Ersatzportionen;
  - je Mahlzeit den Restetag;
  - die Allergen-Übersicht und die neue Sonderkost-Übersicht, nur als Anzahlen.
- Sonderkost ist die Auswahl der Katalogfrage `ernaehrung` (vegetarisch, vegan, kein Schweinefleisch). Personen liefert sie über einen neuen zweckgebundenen Lesepfad als anonyme Profile, mit dem neuen Protokoll-Zweck „Sonderkost-Abgleich der Küche“.
- Der Restetag ist ein Ja/Nein-Feld je Mahlzeit in der neuen Küche-Entität `MahlzeitStand`. Er ändert sich per Autosave-PATCH (AD-19) und ist eine reine Markierung.
- Neue Seite „Tagesansicht“ als erster Küche-Reiter. Der Essensplan zeigt den Restetag als Text. Dazu Doku, Demo-Daten und Screenshots.

## Boundaries & Constraints

**Always:**

*Endpunkte und Rechte (wie 4-1 bis 4-3)*
- Neue Routen liegen unter `/api/freizeiten/{freizeitId}/kueche/…` und tragen `.NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(Verpflegung)`.
- Commands tragen `IBenutzerCommand`, `ISchalterCommand` (Verpflegung) und `IModulCommand<KuecheDbContext>`.
- In der Phase Gelaufen gibt Schreiben 409 `freizeit-abgeschlossen`, Lesen bleibt 200.

*Routen*

| Route | Zweck |
|---|---|
| `GET kueche/tagesansicht?tag=JJJJ-MM-TT` | Tagesansicht eines Tags; ohne `tag`: heute (Europe/Berlin), wenn heute ein Freizeittag ist, sonst der erste Tag |
| `PATCH kueche/mahlzeiten/{programmpunktId}` | Feld `restetag`, Werte `"true"`/`"false"`, Körper `{version, alterWert, restetag}`, Antwort `{version, restetag}` |

*Tagesansicht (Antwort `Tagesansicht`)*
- `Tag`, `Tage` (alle Freizeittage für die Tag-Reiter), `Personenzahl`, `AllergienAbgefragt`, `Allergene` (wie die Einkaufsliste: `AllergenAnzahl` je Allergen), `SonderkostAbgefragt` und `Sonderkost` (`SonderkostAnzahl(Sonderkost, Anzahl)` je Option in Katalogreihenfolge, nur Anzahl > 0).
- `Mahlzeiten` des Tags in Tagesreihenfolge mit Programmpunkt-Id, Beginn, Titel, Art, `Restetag`, `Version` (des Stands, ohne Zeile 0) und `Gerichte` (wie im Essensplan: Gericht mit Rezept-Id/-Name und `Ersatzportionen`).
- Die Allergene kommen über den bestehenden `EinkaufAbfrage.AbgleichAsync`. Die Sonderkost kommt über den neuen Pfad und nur, wenn die Freizeit `ernaehrung` abfragt. Beides wird für dieselben Personen gelesen wie die Personenzahl.
- `tag` in falscher Form oder außerhalb der Freizeit ergibt 400 am Feld `tag`.

*Sonderkost-Lesepfad (Personen)*
- Neu `IFreizeitpassSonderkost.ProfileAsync(freizeitId, personIds)`, gleiche Regeln wie `IFreizeitpassAllergene`: Stufe Team, Freigabe gültig, Frage `ernaehrung` freigegeben. Es kommt nur die Auswahl, nie „Sonstiges“, keine PersonId; die Profile sind sortiert und ohne leere.
- Neuer Zweck `SonderkostFuerKueche = 4` (Spiegel `ZugriffZweck`), ohne lesende Person, höchstens einmal je Person, Zweck und Tag.
- Anzeige im Zugriffsprotokoll und in der Datenauskunft: „Sonderkost-Abgleich der Küche“.
- Allergen- und Sonderkost-Pfad teilen sich eine Implementierung, die Kennung und Zweck als Parameter nimmt.

*Restetag*
- Ein Programmpunkt, der keine Mahlzeit dieser Freizeit ist (normaler Programmpunkt, fremd, unbekannt), ergibt 404.
- Konflikt nach AD-19: `alterWert` ist der aktuelle Wert, ohne Zeile `"false"`. Weicht er ab, gibt es 409 `konflikt`. Ein anderer Wert als `true`/`false` ergibt 400 am Feld `restetag`.
- Höchstens eine Zeile je (Freizeit, Programmpunkt), auch bei gleichzeitigem ersten Setzen. Dazu wird die Zeile per `INSERT … ON CONFLICT DO NOTHING` sichergestellt und dann mit `FOR UPDATE` gesperrt. Es gibt keine 500.
- Der Restetag ändert sonst nichts: Gerichte, Einkaufsliste und Einkauf bleiben gleich. `GET essensplan` liefert je Mahlzeit zusätzlich `Restetag`.

*Oberfläche und Doku*
- Bausteine nur aus `Ui.Komponenten`/`Ui`, Restetag als `AutosaveAnkreuzfeld`. Deutsch ohne Anrede, Handy und Laptop, keine Pop-ups.
- Ein Ladefehler erscheint nicht als leere Seite. Gelaufen ist nur lesbar.
- Testgetrieben: Der Test entsteht vor dem Code.

**Never:**
- Keine Namen, keine PersonId und keine Freitexte („Sonstiges“) in der Tagesansicht. Die Küche speichert keine Allergie- oder Sonderkost-Daten.
- Kein Offline-Modus (V6), keine Druckansicht, kein Resteregister und kein Reste-Vorschlag.
- Keine automatische Änderung von Gerichten oder Einkauf durch den Restetag.
- Kein neuer Problemcode, kein Aktionsschlüssel, keine Ereignisse, kein `[ErlaubtNachAbschluss]`, keine Eigenschaft `PersonId` in Küche. Kein Fremdschlüssel über Schemagrenzen, Seed-Daten nie per Tabellen-Insert.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Tagesansicht | Di 03.08., Pfannkuchen (gluten, milch) am Frühstück; Mia bestätigt mit gluten und vegetarisch | 3 Mahlzeiten; Pfannkuchen mit 1 Ersatzportion (Gluten); Allergene Gluten 1; Sonderkost Vegetarisch 1 | – |
| Ohne `tag` | heute liegt außerhalb der Freizeit | erster Tag der Freizeit | – |
| Falscher Tag | `tag=2027-09-01` oder `tag=morgen` | 400 am Feld `tag` | – |
| Nicht abgefragt | Freizeit fragt `ernaehrung` nicht ab | `SonderkostAbgefragt` false, `Sonderkost` leer, Personen wird für Sonderkost nicht gefragt, kein Sonderkost-Protokoll | – |
| Protokoll | zwei Abrufe am selben Tag | je betroffener Person genau ein Eintrag „Sonderkost-Abgleich der Küche“ ohne lesende Person | – |
| Nur bestätigt | Emma (vegan) nur eingegangen | zählt nicht | – |
| Restetag setzen | PATCH Abendessen `restetag` `"true"`, `alterWert` `"false"` | 200 `{version, restetag:"true"}`; Tagesansicht und Essensplan zeigen Restetag; Einkaufsliste unverändert | `alterWert` `"true"` → 409 `konflikt` |
| Falscher Wert | `restetag` „ja“ | 400 am Feld `restetag` | – |
| Kein Mahlzeit-Punkt | Programmpunkt „Geländespiel“, fremde oder unbekannte Kennung | 404 | – |
| Gleichzeitig | zwei erste PATCH `restetag` | genau eine Zeile; einer 200, der andere 200 oder 409, keine 500 | – |
| Modul aus, Gelaufen, fremd | wie 4-3 | 404; PATCH 409 `freizeit-abgeschlossen`, GET 200; 404 | – |
| Löschlauf | Freizeit-Daten abgelaufen | Stand-Zeilen weg | – |

</intent-contract>

## Code Map

**Küche (Stand 4-3)**
- `src/Modules/Kueche/Zeltlotse.Kueche/KuecheModul.cs:46-69`: Routengruppe `kueche`. Die zwei neuen Routen kommen hierher.
- `Einkauf/EinkaufAbfrage.cs`: `MahlzeitenAsync`, `GerichteAsync`, `AbgleichAsync`. Dazu kommt `SonderkostAsync` (Muster `AbgleichAsync`, `IFreizeitFragenLesen.FragtAbAsync(…, Fragenkatalog.Ernaehrung)`).
- `Einkauf/EinkaufLesen.cs:22-52`: `EssensplanLadenEndpunkt`, Muster für `TagesansichtLadenEndpunkt`; `GerichtFehler` (Feldfehler).
- `Einkauf/EinkaufRechnen.cs`: `Ersatz(profile, zutaten)` und `Uebersicht(profile)` wiederverwenden; Sonderkost-Zählung daneben (`SonderkostUebersicht`).
- `Einkauf/PostenStand.cs`: Muster für `MahlzeitStand` (Id, OrganisationId, FreizeitId, ProgrammpunktId, Restetag, Version; `Wert`, `FeldAendern` mit `KuecheWerte.JaNeinGleich`/`JaNeinLesen`).
- `Einkauf/EinkaufslisteSlices.cs`: PATCH-Slices der Stand-Felder als Muster (Validator mit `JaNeinFehlerText`, Handler, Endpunkt mit `FeldPatch.Lesen`).
- `Einkauf/EinkaufslisteSlices.cs:267` `EinkaufTage` und `Rezepte/KuecheWerte.cs:92-106` `TagLesen`, `TagFehlerText`, `TagAusserhalbText`. „Heute“ in Europe/Berlin kommt als kleine Funktion in `KuecheWerte` (wie `Freizeiten/Domaene/FreizeitZeiten.Heute`), mit `TimeProvider`.
- `Daten/KuecheDbContext.cs`: DbSets in beiden Kontexten und `KuecheModell` (eindeutiger Index (FreizeitId, ProgrammpunktId)). Neue Migration `Restetag` (Tabelle `mahlzeit_stand`).
- `Datenschutz/KuecheLoeschlauf.cs`: `MahlzeitStaende` mitlöschen.
- `Zeltlotse.Personen.Contracts`/`Programm.Contracts`: `FreizeitMahlzeit` (ProgrammpunktId, Tag, Beginn, Titel, Art).

**Personen**
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/IFreizeitpassAllergene.cs`: Muster für `IFreizeitpassSonderkost` (neu, gleicher Ordner).
- `Personen/Freizeitpass/FreizeitpassAllergene.cs`: Lesepfad mit der festen Frage `Allergien` und dem Zweck `AllergeneFuerKueche`. Daraus wird ein gemeinsamer Kern mit Parametern; eine Klasse kann beide Schnittstellen tragen.
- `PersonenModul.cs:50`: Registrierung.
- `Zugriffsprotokoll/EinsichtProtokoll.cs:11-20`: `EinsichtZweck` (gespeichert als `short`, keine Migration nötig, prüfen).
- `Datenschutz/PersonenAuskunft.cs:284`: Text des Zwecks.
- `src/Zeltlotse.Api.Contracts/Personen/Zugriffsprotokoll.cs:18`: `ZugriffZweck`.
- `src/Zeltlotse.Ui/Texte/Anzeige.cs:205`: Anzeige des Zwecks.

**Contracts**
- `src/Zeltlotse.Api.Contracts/Kueche/Essensplan.cs`: `EssensplanMahlzeit` bekommt `bool Restetag` (ans Ende).
- Neu `Kueche/Tagesansicht.cs`:
  - `Tagesansicht`, `TagesansichtMahlzeit`, `SonderkostAnzahl`;
  - `MahlzeitFeld` mit `MahlzeitFelder.Restetag = "restetag"`;
  - `Sonderkost.Alle` als Kopie der Ernährungs-Optionen (Muster `Allergene.Alle` in `Rezepte.cs`), mit Test auf Gleichheit zum Katalog.

**Oberfläche**
- `src/Zeltlotse.App/Features/Kueche/Essensplan.razor`: Tag-Reiter und Tag-Wahl (:126-149, :220-237), Mahlzeit-Cards. Muster für die Tagesansicht. Der Restetag erscheint dort als Text „Restetag“ an der Mahlzeit.
- `Einkauf.razor`: Muster für Laden, Fehler, `NichtVerfuegbar`, Gelaufen, `@key` mit Ladezähler bei `AutosaveAnkreuzfeld`.
- `Einkaufsliste.razor`: Card „Allergen-Abgleich“ als Muster für die Übersicht.
- `KuecheReiter.razor`: fünf Einträge, „Tagesansicht“ zuerst.
- `KuecheTexte.cs`: `TagesansichtZiel(freizeitId, tag?)`, `AllergenZeile`, `Ersatzportionen`, `KeineAllergienText`; neu `SonderkostZeile`, `KeineSonderkostText`.
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs`: `TagesansichtLadenAsync(freizeitId, tag?)` (404 → null), `MahlzeitPfad(freizeitId, programmpunktId)`.

**Tests, Demo, Doku**
- `tests/Zeltlotse.Api.Tests/EinkaufFreigabeTests.cs`, `EinkaufslisteTests.cs` (Protokoll-Fall :322), `KuecheTestHelfer.cs`, `FreizeitpassSzenario.cs`: Muster für Rechte, Gelaufen, Gleichzeitigkeit, Löschlauf, Protokoll.
- `EndpunktListeTests.cs:293-333`: Küche-Routen alphabetisch, Zähler 20 → 22.
- `tests/Zeltlotse.Kueche.Tests/EinkaufFreigabeTests.cs`: Muster für Fachtests der Entität und für Mandantenfilter/PersonId-Check.
- `tests/Zeltlotse.Personen.Tests`: Tests von `FreizeitpassAllergene` als Muster.
- `tests/Zeltlotse.Ui.Tests/EinkaufSeitenTests.cs`, `EssensplanSeitenTests.cs`, `MeineDatenSeitenTests.cs:139`.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs:131-137` (`MiasFreizeitpass`), `:88-96` (4-3-Muster); `DemoDaten.cs:172-174`; `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:2230-2260`: Abschnitt 4-3 als Muster, `BildAsync`.
- `doku/src/content/docs/kueche/`: `index.md`, `essensplan.md`, `einkaufsliste.md`, `einkauf.md` (je „Noch nicht dabei“). `doku/src/content/docs/daten.md`, `erste-schritte/meine-daten.md`: Zwecke im Zugriffsprotokoll. `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:31` `PflichtSeiten`.

## Tasks & Acceptance

**Execution:**

*Tests zuerst*
- `tests/Zeltlotse.Kueche.Tests/TagesansichtTests.cs` (neu): Fachtests für
  - `MahlzeitStand.FeldAendern` samt Konflikt;
  - Sonderkost-Zählung;
  - „heute oder erster Tag“;
  - Mandantenfilter und „keine PersonId“ für `MahlzeitStand`.
- `tests/Zeltlotse.Personen.Tests`: Sonderkost-Lesepfad mit Stufe, Ablauf, Frage nicht freigegeben, „Sonstiges“ nie gelesen, Protokoll einmal am Tag ohne lesende Person; Allergen-Pfad bleibt grün.
- `tests/Zeltlotse.Api.Tests/TagesansichtTests.cs` (neu), gegen echtes PostgreSQL:
  - alle Zeilen der I/O-Matrix;
  - Rechte für beide Routen: Leitung/Team 2xx; Org-Admin ohne Rolle, fremde Organisation, fremde und unbekannte Freizeit 404; anonym 401; Schalter aus 404; Gelaufen PATCH 409, GET 200;
  - Zugriffsprotokoll und Datenauskunft mit dem neuen Zweck.
- `EndpunktListeTests.cs`: zwei neue Routen, Zähler 22.
- `tests/Zeltlotse.Ui.Tests/TagesansichtSeitenTests.cs` (neu, bUnit), Muster `EinkaufSeitenTests`:
  - Tag-Reiter, Mahlzeiten mit Gerichten und Ersatzportionen;
  - Übersicht Allergien und Sonderkost samt Sätzen „fragt keine … ab“;
  - Restetag sendet PATCH `restetag`, Gelaufen ohne Ankreuzfeld;
  - Leer-Zustand ohne Gerichte, Ladefehler, nicht verfügbar;
  - Reiter mit fünf Einträgen, Essensplan zeigt „Restetag“, Zugriffsprotokoll-Text.

*Server*
- Personen: `IFreizeitpassSonderkost` samt Implementierung, Zweck und Anzeige-Texte.
- Küche:
  - `MahlzeitStand` mit Modell, DbSets und Migration `Restetag`;
  - Slice `RestetagAendern` (Command, Validator, Handler, Endpunkt) und `TagesansichtLadenEndpunkt`;
  - `EinkaufAbfrage.SonderkostAsync`, Essensplan mit `Restetag`, Löschlauf.
- Contracts wie in der Code Map.

*Oberfläche*
- `Tagesansicht.razor` (neu, `/freizeiten/{id}/kueche/tagesansicht?tag=`):
  - Dachzeile „Küche“, h1 „Tagesansicht“, `KuecheReiter Aktiv="tagesansicht"`, Tag-Reiter (Vorauswahl vom Server), h2 mit dem Tag;
  - Card „Allergien und Sonderkost“ mit Zeilen wie „Gluten: 1 Person“ und „Vegetarisch: 1 Person“, sonst den Sätzen „Diese Freizeit fragt keine Allergien ab.“, „Diese Freizeit fragt keine Sonderkost ab.“, „Keine Sonderkost in den bestätigten Freizeitpässen.“;
  - je Mahlzeit eine Card (Titel, Uhrzeit): Gerichte mit Link zum Rezept und Satz „N Ersatzportionen einplanen (enthält …)“, ohne Gericht „Kein Gericht vorgemerkt.“ mit Link zum Essensplan am Tag;
  - Ankreuzfeld „Restetag“, gesetzt mit dem Hinweis „Restetag: Reste verwerten.“; Gelaufen nur Text.
- `Essensplan.razor` (Text „Restetag“ an der Mahlzeit), `KuecheReiter.razor`, `KuecheTexte.cs`, `KuecheApiClient.cs`.

*Demo, Doku, Screenshots*
- Demo:
  - `MiasFreizeitpass` bekommt `("ernaehrung", "vegetarisch")`;
  - neuer Schritt `RestetagAsync` nach `EinkaufAsync`: Abendessen am 03.08.2027 als Restetag, nur über Commands und idempotent;
  - `DemoDatenTests` anpassen.
- `DokuScreenshotTests.cs`: Abschnitt „Story 4-4“ mit `tagesansicht` (03.08.), je Handy und Laptop, `AxeAsync`, nur lesend. Die übrigen Küche-Screenshots entstehen mit dem fünften Reiter neu.
- Doku:
  - neu `kueche/tagesansicht.md` („Tagesansicht und Restetag“): Wer, Weg, Tag wählen, was die Seite zeigt, Zählregel (bestätigt, freigegeben, nur Auswahl, keine Namen; Einzelheiten über Schnellsuche oder Freizeitpass), Restetag setzen und zurücknehmen (ändert Essensplan und Einkauf nicht), Datenschutz (Zugriffsprotokoll), nach dem Abschluss, „Noch nicht dabei“ (offline), Screenshots;
  - ergänzt: `index.md`, „Noch nicht dabei“ in `essensplan.md`, `einkaufsliste.md`, `einkauf.md`, Zwecke in `daten.md` und `meine-daten.md`;
  - `DokuRegeln.PflichtSeiten` um `kueche/tagesansicht` erweitern.

**Acceptance Criteria:**
- Given Mia ist bestätigt mit Gluten und vegetarisch, when die Küche am 03.08. die Tagesansicht öffnet, then stehen dort Pfannkuchen am Frühstück mit „1 Ersatzportion einplanen (enthält Gluten)“ sowie „Gluten: 1 Person“ und „Vegetarisch: 1 Person“, and Mias Zugriffsprotokoll zeigt „Sonderkost-Abgleich der Küche“.
- Given das Abendessen ist ein normaler Tag, when die Küche „Restetag“ antippt, then zeigt die Tagesansicht „Gespeichert · Rückgängig“ und den Hinweis zum Restetag, and der Essensplan zeigt am Abendessen „Restetag“, and die Einkaufsliste ist unverändert.
- Given Verpflegung ist aus oder die Freizeit gelaufen, when die Tagesansicht geöffnet wird, then „Diese Seite ist nicht verfügbar.“ bzw. „nur lesbar“ ohne Ankreuzfeld.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then sind alle Tests grün, die Screenshots `tagesansicht-*` erzeugt und der Doku-Build fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 28 findings — high 0, medium 0, low 20, false 7, maybe-false 1
- findings:
  - `[low]` `[patch]` Verification-Gap: Restetag-Hinweis nach „Rückgängig“ ungetestet — bUnit-Test erweitert (zweiter PATCH, Rückgängig, Hinweis weg, Kästchen leer).
  - `[low]` `[patch]` Verification-Gap (other): `kueche/index.md` sagt, die Tagesansicht komme später — Satz nennt jetzt nur die Nutzung ohne Netz.
  - `[false]` `[reject]` Intent a: kein 403-Test — `Mitarbeit` umfasst alle Freizeit-Rollen, ohne Rolle gibt es 404 (AD-12, getestet); 403 ist nicht erreichbar (Design Notes).
  - `[false]` `[reject]` Intent b: keine Token-Fälle — keine Token-Route; AD-30 verlangt sie nur für Token-Endpunkte.
  - `[maybe-false]` `[reject]` Intent c: „Test vor dem Code“ am Diff nicht prüfbar — klären würde nur der Sitzungsverlauf; wenn wahr nur low.
  - `[false]` `[reject]` Intent d: Doku-Build nicht belegt — `npm run build` lief nach den Patches fehlerfrei (60 Seiten).
  - `[low]` `[patch]` Intent e: viele Screenshots nur mit Rendering-Rauschen geändert — per Pixelvergleich geprüft; Rauschen auf HEAD zurückgesetzt, behalten nur echte Änderungen.
  - `[false]` `[reject]` Intent f: Doku nicht unter einer Rolle — `kueche/` ist laut NFR-20 der Bereich „Küche“ (Rolle und Aufgabe), wie 4-1 bis 4-3.
  - `[false]` `[reject]` Intent g: Zusätze über die Story-Zeile hinaus — alle von der Spec vorgegeben.
  - `[low]` `[patch]` Blind: `index.md` Widerspruch (gleiche Ursache wie oben) — siehe dort.
  - `[low]` `[patch]` Blind: `daten.md` nennt die Tagesansicht nicht als Anlass des Allergen-Abgleichs — Satz ergänzt.
  - `[low]` `[patch]` Blind: „Noch nicht dabei“ in `einkauf.md`, `einkaufsliste.md`, `essensplan.md` beschreibt heutiges Verhalten; `essensplan.md` nennt den Restetag-Hinweis nicht — eigener Abschnitt „Restetag“ je Seite, „Noch nicht dabei“ nur noch offline.
  - `[false]` `[reject]` Blind: `Version` im Restetag-PATCH ungenutzt — AD-19 regelt den Konflikt über `alterWert`; `version` hält die Körperform aller Autosave-Felder einheitlich (wie 4-2, 4-3).
  - `[low]` `[reject]` Blind: ungültiger `alterWert` gibt 409 statt 400 — gleiches Muster wie `PostenStand` (4-3); der Autosave-Baustein schickt immer den angezeigten Wert, eine eigene Prüfung wäre neuer Zweig.
  - `[low]` `[patch]` Blind: Klasse `FreizeitpassAllergene` bedient auch Sonderkost — umbenannt in `FreizeitpassKuechenProfile` (Datei, Registrierung).
  - `[low]` `[reject]` Blind: `TagesansichtLadenAsync` dupliziert `LadenAsync` und fängt jede 400 ab — die getaggte Anfrage kann nur am Feld `tag` scheitern; Umbau des gemeinsamen Helfers wäre mehr als eine direkte Korrektur.
  - `[low]` `[reject]` Blind: zweite „heute in Berlin“-Rechnung, statische Zeitzone — `FreizeitZeiten` ist intern im Modul Freizeiten (AD-27), gleiche Zeitzonen-Abhängigkeit besteht dort schon; ein gemeinsamer Vertrag wäre neue Fläche.
  - `[low]` `[reject]` Blind: Personenliste dreimal gelesen — kleine Kosten, gleiches Muster wie Einkaufsliste; Bündeln bräuchte neue Parameter.
  - `[low]` `[patch]` Blind: Tag-Wechsel und Konflikt der Tagesansicht ungetestet — bUnit-Test „Tag-Reiter wechseln lädt den anderen Tag“; der Konfliktpfad gehört dem getesteten `AutosaveAnkreuzfeld` (`AutosaveTests`).
  - `[low]` `[patch]` Blind: Restetag-Hinweis unten statt wie im Essensplan oben; „Im Essensplan vormerken“ auch am Restetag — Hinweis jetzt erste Zeile der Card; den Link am Restetag auszublenden wäre ein neuer Zweig (zurückgewiesen).
  - `[low]` `[patch]` Blind: „Nach dem Abschluss“ in `tagesansicht.md` nennt den fehlenden Link nicht — ergänzt.
  - `[low]` `[patch]` Blind: Sidebar-Reihenfolge widerspricht den Reitern — Tagesansicht 1, Rezepte 2, Essensplan 3, Einkaufsliste 4, Einkauf 5.
  - `[low]` `[reject]` Blind: kein API-Test für „heute ist Freizeittag“ — der Endpunkt ruft direkt `KuecheWerte.TagOhneWahl`, beide Zweige sind fachgetestet; eine eingefrorene Uhr im API-Test wäre neue Testinfrastruktur.
  - `[low]` `[reject]` Edge: erster PATCH mit veraltetem `alterWert` hinterlässt eine Zeile `Restetag = false` — verhält sich genau wie „keine Zeile“; Rücknahme bräuchte einen Sonderzweig.
  - `[low]` `[patch]` Edge: `index.md` Widerspruch (gleiche Ursache) — siehe oben.
  - `[low]` `[patch]` Edge (claim): `index.md` widerspricht der Spec (gleiche Ursache) — siehe oben.
  - `[low]` `[reject]` Edge: Tag-Wechsel nutzt den alten Kontext, nach Abschluss bleibt das Kästchen — Abschluss während einer offenen Seite ist selten; der PATCH antwortet dann 409 `freizeit-abgeschlossen` und der Baustein meldet „Nicht gespeichert“, Neuladen zeigt „nur lesbar“; kein falscher Datenstand.
  - `[false]` `[reject]` Edge: `Single()` wirft, wenn der Mandantenfilter die Zeile verbirgt — der Handler prüft vorher, dass die Mahlzeit zur Freizeit im eigenen Mandanten gehört; eine Zeile derselben Freizeit gehört damit derselben Organisation.

## Design Notes

- **Anzahlen statt Namen.** Wie in 4-2 bekommt die Küche nur anonyme Profile (AD-8, Datensparsamkeit). Wer genau betroffen ist, sieht das Team über Schnellsuche und Freizeitpass. Die hat es ohnehin, aber mit eigenem Protokoll-Eintrag.
- **Sonderkost = Frage `ernaehrung`.** Der Katalog hat genau diese Frage. Wie bei Allergien zählt nur die Auswahl, „Sonstiges“ bleibt in Personen. Ein eigener Zweck hält das Zugriffsprotokoll ehrlich, denn „Allergen-Abgleich“ wäre für Ernährungsangaben falsch.
- **Sonderkost nur je Tag, nicht je Gericht.** Rezepte haben kein Merkmal „vegetarisch“. Die Küche sieht die Übersicht und plant selbst.
- **Restetag als reine Markierung.** FR-53 verlangt nur das Markieren. Gerichte automatisch zu entfernen oder Einkauf zurückzunehmen wäre eine Planungsänderung ohne Bestätigung (Leitprinzip 6) und Speziallogik. Gekauftes ist ohnehin da. Will die Küche ein Gericht verschieben, nutzt sie Entfernen oder Tauschen im Essensplan.
- **Stand-Zeile statt Ja/Nein an fremder Tabelle.** Die Mahlzeit gehört zu Programm. Küche speichert ihren Stand selbst und verweist nur per Kennung. Das Muster ist `PostenStand`.
- **Tag-Wahl am Server.** Ohne `tag` wählt der Server heute oder den ersten Tag. So braucht die Seite nur einen Abruf, und der spätere Offline-Modus (V6) kann dieselbe Route nutzen.
- **Rechte.** Wie in 4-1 bis 4-3 gibt es keine 403- und keine Token-Fälle. Alle Routen gelten für Leitung und Team, ohne Rolle gibt es 404 (AD-12).

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.Kueche.Tests`, `… Personen.Tests`, `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*Tagesansicht*" --filter-class "*Essensplan*" --filter-class "*Gerichte*" --filter-class "*Zugriffsprotokoll*" --filter-class "*Datenauskunft*" --filter-class "*EndpunktListe*" --filter-class "*DemoDaten*"`: während der Arbeit grün.
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` und `tests/Zeltlotse.Ui.Tests`: grün.
- Am Ende einmal alle Testprojekte inklusive E2E (Screenshots erzeugt, Rauschen auf HEAD zurückgesetzt): grün.
- `npm run build` in `doku/`: fehlerfrei.

## Auto Run Result

Status: done

**Umgesetzt:** Neue Küchen-Tagesansicht (`GET kueche/tagesansicht?tag=`, ohne Tag heute oder der erste Tag) mit den Mahlzeiten des Tags, ihren Gerichten samt Ersatzportionen, der Allergen-Übersicht und der neuen Sonderkost-Übersicht (Frage `ernaehrung`), nur als Anzahlen. Sonderkost liest Personen über den neuen zweckgebundenen Pfad `IFreizeitpassSonderkost` und protokolliert „Sonderkost-Abgleich der Küche“ ohne lesende Person; Allergen- und Sonderkost-Pfad teilen einen Kern. Der Restetag ist ein Ja/Nein-Feld je Mahlzeit (`MahlzeitStand`, `PATCH kueche/mahlzeiten/{programmpunktId}`, Autosave nach AD-19) und eine reine Markierung; der Essensplan zeigt ihn als Text. „Tagesansicht“ ist der erste von fünf Küche-Reitern.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Kueche/Zeltlotse.Kueche/Einkauf/TagesansichtSlices.cs` (neu): Lese-Endpunkt und Restetag-Slice.
- `Einkauf/MahlzeitStand.cs` (neu), `Daten/KuecheDbContext.cs` (DbSets, Modell, `MahlzeitStandSperrenAsync` mit `INSERT … ON CONFLICT` und `FOR UPDATE`), Migration `20261019125648_Restetag`.
- `Einkauf/EinkaufAbfrage.cs` (`SonderkostAsync`), `EinkaufRechnen.cs` (`SonderkostUebersicht`), `EinkaufLesen.cs` (Essensplan mit Restetag), `Rezepte/KuecheWerte.cs` (heute in Berlin), `KuecheLoeschlauf.cs`, `KuecheModul.cs` (2 Routen).
- `src/Modules/Personen/…/IFreizeitpassSonderkost.cs` (neu), `Freizeitpass/FreizeitpassKuechenProfile.cs` (vorher `FreizeitpassAllergene.cs`, gemeinsamer Kern), `EinsichtZweck.SonderkostFuerKueche`, `PersonenAuskunft`, `PersonenModul`.
- `src/Zeltlotse.Api.Contracts/Kueche/Tagesansicht.cs` (neu), `Essensplan.cs` (`Restetag`), `Personen/Zugriffsprotokoll.cs`.
- `src/Zeltlotse.App/Features/Kueche/Tagesansicht.razor` (neu), `Essensplan.razor`, `KuecheReiter.razor`, `KuecheTexte.cs`; `Ui/Api/KuecheApiClient.cs`; `Ui/Texte/Anzeige.cs`.
- Demo: Mia mit „vegetarisch“, Schritt `RestetagAsync` (Abendessen 03.08.2027).
- Doku: neu `kueche/tagesansicht.md`; ergänzt `kueche/index.md`, `essensplan.md`, `einkaufsliste.md`, `einkauf.md`, `rezepte.md` (Reiter, Abschnitt „Restetag“, Sidebar-Reihenfolge), `daten.md`, `erste-schritte/meine-daten.md`. Screenshots `tagesansicht-*` neu; `rezepte-*`, `essensplan-*`, `einkaufsliste-*`, `einkauf-*` (fünfter Reiter), `freizeitpass-einsicht-*`, `notfallordner-*` („Vegetarisch“) aktualisiert.
- Tests: neu `Api.Tests/TagesansichtTests.cs`, `Kueche.Tests/TagesansichtTests.cs`, `Personen.Tests/KuechenProfileTests.cs`, `Ui.Tests/TagesansichtSeitenTests.cs`; angepasst `EndpunktListeTests` (22 Küche-Routen), `GerichteTests`, `DemoDatenTests`, `RezeptTests`, `EinkaufSeitenTests`, `EssensplanSeitenTests`, `DokuRegeln`/`DokuTests`, E2E-Abschnitt „Story 4-4“ und `Freizeit_Start`.

**Review:** 28 Befunde (low 20, false 7, maybe-false 1). Gepatcht (alle low): Doku-Widerspruch in `kueche/index.md`, Anlass des Allergen-Abgleichs in `daten.md`, „Noch nicht dabei“ bereinigt mit eigenem Abschnitt „Restetag“, „Nach dem Abschluss“ und Sidebar-Reihenfolge in `tagesansicht.md`, Umbenennung in `FreizeitpassKuechenProfile`, Restetag-Hinweis oben in der Card, bUnit-Tests für Rückgängig und Tag-Wechsel, Screenshot-Rauschen zurückgesetzt. Zurückgestellt: keine. Zurückgewiesen: alle übrigen mit Begründung im Review Triage Log.

**Folge-Review:** nicht empfohlen (`false`) — gepatcht wurden nur Einträge mit low, kein medium oder high.

**Verifikation:**
- Gezielt nach der Umsetzung: Kueche 119/119, Personen 157/157, Architektur 135/135, Ui 736/736, Api-Filter (Tagesansicht, Gerichte, Zugriffsprotokoll, Datenauskunft, EndpunktListe, DemoDaten, Einkauf) 108/108; Fall-Matrix vollständig durch gelaufene Tests abgedeckt.
- Nach den Review-Patches: Komplettlauf aller Testprojekte; alle Projekte außer Api.Tests grün inklusive E2E (19 min, Screenshots erzeugt). Der Komplettlauf wurde nach 60 min wegen des Zeitlimits gestoppt, als Api.Tests noch lief; Api.Tests danach allein: 910/910 grün (56 min).
- `npm run build` in `doku/`: fehlerfrei, 60 Seiten.
- Screenshots per Pixelvergleich gegen HEAD geprüft: 43 nur mit Rendering-Rauschen (zufällige Ports, Links, Fokuslinien) auf HEAD zurückgesetzt.

**Restrisiken:**
- Api.Tests läuft im Gesamtlauf unter Last sehr lange (vorbestehend, siehe 4-2).
- Ein erster Restetag-PATCH mit veraltetem alten Wert hinterlässt eine Zeile `Restetag = false` (verhält sich wie keine Zeile).
- Wird die Freizeit abgeschlossen, während die Tagesansicht offen ist, zeigt das Kästchen bis zum Neuladen „Nicht gespeichert“ statt „nur lesbar“.
- Der Knopf „Küche“ auf der Startseite der Freizeit öffnet weiter die Rezepte; die Tagesansicht ist der erste Reiter.
