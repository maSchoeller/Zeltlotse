---
title: 'Story 4-2: Gerichte vormerken und Einkaufsliste'
type: 'feature'
created: '2026-10-17'
status: 'done'
baseline_revision: '5be62b3529864fe9e41838617cd2fab6a52689ac'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/4-1-rezepte-mit-skalierung.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Nach dem Verschieben der Freizeit-Daten fehlen Mahlzeiten an neuen Tagen, alte Mahlzeiten samt Gerichten erscheinen weiter in Essensplan und Einkaufsliste.
    evidence: |-
      FreizeitMahlzeitenLesen und WochenrasterLaden (Story 3-1) zählen alle Mahlzeiten der Freizeit gegen 3 × Tage, ohne nach dem aktuellen Zeitraum zu filtern; vorbestehende Zählregel aus Programm.
    location: >-
      src/Modules/Programm/Zeltlotse.Programm/Programme/FreizeitMahlzeitenLesen.cs
    severity: medium
  - summary: >-
      Jeder Aufruf von Essensplan oder Einkaufsliste entschlüsselt je Person die Allergie-Antwort und schickt je Person einen Protokoll-Command.
    evidence: |-
      Gleiches Muster wie der Notfallordner. Ob es bei 100+ Personen spürbar langsam wird, klärt nur eine Messung mit großer Freizeit.
    location: >-
      src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassAllergene.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Rezepte gibt es seit 4-1. Die Küche kann sie aber noch nicht an Mahlzeiten vormerken, und es gibt keine Einkaufsliste mit Allergen-Abgleich und Ersatzportionen (CAP-29, FR-47 bis FR-50). Uwe müsste die Liste weiter von Hand rechnen.

**Approach:**
- Im Modul Küche entstehen Gerichte, also Vormerkungen eines Rezepts an einem Mahlzeiten-Programmpunkt. Die Mahlzeiten kommen über eine neue Leseschnittstelle in `Programm.Contracts`.
- Die Einkaufsliste wird bei jedem Abruf aus den Gerichten berechnet. Gespeichert werden nur Hand-Mengen und Hand-Posten.
- Für den Allergen-Abgleich gibt es eine neue zweckgebundene Leseschnittstelle in `Personen.Contracts`. Sie liefert nur anonyme Allergen-Profile und protokolliert den Zweck ohne lesende Person (AD-8).
- Für einen Gerichttausch zeigt die Seite zuerst eine Vorher/Nachher-Vorschau. Erst „Übernehmen“ oder „Anpassen“ ändert etwas.
- Neue Seiten „Essensplan“ und „Einkaufsliste“ und Küche-Reiter. Dazu Doku, Demo-Daten und Screenshots.

## Boundaries & Constraints

**Always:**

*Endpunkte und Rechte (wie 4-1)*
- Alle neuen Routen liegen unter `/api/freizeiten/{freizeitId}/kueche/…` und tragen `.NurFreizeitRolle(Leitung, Team).GehoertZuSchalter(Verpflegung)`.
- Benutzer-Commands tragen `IBenutzerCommand`, `ISchalterCommand` (Verpflegung) und `IModulCommand<KuecheDbContext>`.
- In der Phase Gelaufen liefern Schreibvorgänge 409 `freizeit-abgeschlossen` (Handler prüft die Phase zusätzlich). Lesen, auch die Tausch-Vorschau, bleibt 200.
- Autosave nach AD-19: PATCH mit genau einem Feld, Werte als Text, `alterWert` desselben Felds weicht ab → 409 `konflikt`. PATCH, DELETE und Tauschen sperren die Zeile mit `FOR UPDATE`.

*Routen*

| Route | Zweck |
|---|---|
| `GET kueche/essensplan` | Tage, Mahlzeiten mit ihren Gerichten, Rezeptauswahl, Abgleich je Gericht |
| `POST kueche/gerichte` | `{programmpunktId, rezeptId}` vormerken, 201 |
| `DELETE kueche/gerichte/{gerichtId}` | entfernen, 204 |
| `GET kueche/gerichte/{gerichtId}/tausch?rezeptId=` | Vorschau Vorher/Nachher, schreibt nichts |
| `POST kueche/gerichte/{gerichtId}/tauschen` | `{version, rezeptId}` übernehmen; Version veraltet → 409 `konflikt` |
| `GET kueche/einkaufsliste` | berechnete Liste |
| `PATCH kueche/einkaufsliste/gerichte/{gerichtId}/zutaten/{zutatId}` | Feld `menge` von Hand; leer = zurück zur berechneten Menge |
| `POST kueche/einkaufsliste/posten` | Hand-Posten: `bezeichnung` Pflicht; `menge`, `einheit`, `tag` optional |
| `PATCH` und `DELETE kueche/einkaufsliste/posten/{postenId}` | Felder `bezeichnung`, `menge`, `einheit`, `tag` |

*Rechnen*
- Berechneter Posten = je Gericht × Zutat des Rezepts: `KuecheWerte.Hochrechnen(menge, basis, personenzahl)`. Die Personenzahl ist dieselbe wie in 4-1, ohne Abzug für Ersatzportionen.
- Eine Hand-Menge ersetzt die berechnete Menge. Sie bleibt bei geänderter Personenzahl und geändertem Rezept erhalten.
- Keine Zusammenfassung über Gerichte hinweg. Gruppiert wird nach Tag der Mahlzeit, darin nach Mahlzeit-Reihenfolge, Hand-Posten ohne Tag stehen zuletzt.
- Tausch: Hand-Mengen wandern auf die Zutat des neuen Rezepts mit gleicher Bezeichnung (ohne Groß/Klein, getrimmt) und gleicher Einheit. Übrige Hand-Mengen des Gerichts entfallen. Die Vorschau zeigt genau das Ergebnis, das „Übernehmen“ speichert, und markiert geänderte Zeilen.

*Abgleich*
- Betrachtete Personen sind dieselbe Menge wie bei der Personenzahl. Neu ist `PersonenAsync` an `IFreizeitPersonenzahlLesen`; gelöschte Personen fallen heraus.
- Personen liefert je Person mit gültiger Freigabe der Stufe Team, deren Fragen `allergien` enthalten, die gewählten Allergen-Schlüssel. Es gibt keine PersonId, keinen Freitext „Sonstiges“, sortiert und ohne leere Profile.
- Ersatzportionen eines Gerichts = Anzahl der Profile, die ein Allergen des Rezepts (Vereinigung der Zutaten) enthalten. Jede Person zählt einmal.
- Fragt die Freizeit `allergien` weder für Teilnehmende noch für Mitarbeitende ab, gilt `allergienAbgefragt = false`. Die Oberfläche zeigt dann „Diese Freizeit fragt keine Allergien ab.“

*Protokoll*
- Jeder Abgleich protokolliert je betroffener Person den neuen Zweck `AllergeneFuerKueche` ohne lesende Person, höchstens einmal je Person, Zweck und Tag. Das Zugriffsprotokoll der Person zeigt „Allergen-Abgleich der Küche“.

*Oberfläche und Doku*
- Bausteine nur aus `Ui.Komponenten`/`Ui`, Deutsch ohne Anrede, Handy und Laptop, keine Pop-ups.
- Der neue Baustein `Vergleich` steht in `Ui.Komponenten`: zwei Spalten „Vorher“/„Nachher“, am Handy untereinander. Geänderte Mengen sind fett, nicht nur farbig markiert.
- Testgetrieben: Der Test entsteht vor dem Code.

**Never:**
- Kein Einkauf freigeben oder abhaken, keine Tagesansicht und kein Restetag. Das kommt mit 4-3 und 4-4.
- Kein Zusammenfassen gleicher Zutaten, keine Einheiten-Umrechnung, keine Hand-Überschreibung der Personenzahl.
- Kein Antworttext und keine PersonId aus Personen an Küche. Küche speichert keine Allergie-Daten.
- Kein neuer Problemcode und kein neuer Aktionsschlüssel, keine Ereignisse, kein `[ErlaubtNachAbschluss]`, keine Eigenschaft `PersonId` in Küche-Entitäten.
- Kein Fremdschlüssel über Schemagrenzen (Gericht → Programmpunkt nur per Kennung). Seed-Daten nie per Tabellen-Insert.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Vormerken | Pfannkuchen (Basis 4, Mehl 400 g) an Di-Frühstück, 8 Personen | Einkaufsliste Tag Di: Mehl 800 g „Pfannkuchen · Frühstück“ | – |
| Kein Mahlzeit-Punkt | `programmpunktId` eines normalen Programmpunkts, einer anderen Freizeit oder unbekannt; `rezeptId` fremd | 400 am Feld `programmpunktId` bzw. `rezeptId` | Fehler am Feld |
| Mahlzeiten fehlen noch | Programm nie geöffnet | Essensplan legt die Mahlzeiten an und zeigt 3 je Tag | – |
| Hand-Menge | PATCH Mehl `menge` „1“ (Einheit kg), `alterWert` = aktueller Text | Posten zeigt 1 und `vonHand`; nach weiterer Bestätigung bleibt 1 | anderer `alterWert` → 409 `konflikt` |
| Hand-Menge zurücknehmen | PATCH `menge` leer | berechnete Menge, `vonHand` false | – |
| Tausch-Vorschau | Pfannkuchen → Chili, Hand-Menge an Mehl | Vorher: Pfannkuchen-Posten; Nachher: Chili-Posten, alle neu; Datenbank unverändert | gleiches Rezept → Vorschau ohne Änderung |
| Tausch mit gleicher Zutat | Hand-Menge „Zwiebeln 3 kg“, neues Rezept hat „zwiebeln“/„kg“ | Nachher: Zwiebeln 3 kg `vonHand` | – |
| Tausch veraltet | `version` alt (jemand hat inzwischen getauscht) | 409 `konflikt`, nichts geändert | Oberfläche: „Inzwischen geändert – neu laden?“ |
| Abgleich | Mia (gluten, bestätigt), Emma (milch, nur eingegangen), Pfannkuchen enthält gluten, milch | Ersatzportionen 1; Allergen-Übersicht Gluten 1 | – |
| Freigabe abgelaufen | Freigabefenster vorbei | Profil zählt nicht | – |
| Nicht abgefragt | Freizeit fragt `allergien` nicht ab | `allergienAbgefragt` false, Ersatzportionen 0 | – |
| Protokoll | zwei Abrufe am selben Tag | genau ein Eintrag je betroffener Person, ohne lesende Person | – |
| Hand-Posten | POST ohne Bezeichnung; `tag` außerhalb der Freizeit oder kein Datum; `menge` „x“ | 400 am Feld | – |
| Rezept gelöscht | Rezept ist an 2 Mahlzeiten vorgemerkt | Gerichte und deren Hand-Mengen sind weg (Cascade) | – |
| Modul aus, Gelaufen, fremd | wie 4-1 | 404, 409 bei Schreiben, 404 | – |
| Gleichzeitig | zwei Tausch-POST mit gleicher Version; Hand-Posten löschen gleichzeitig | genau einer 200, der andere 409; 204/404, kein 500 | – |

</intent-contract>

## Code Map

**Küche (Stand 4-1)**
- `src/Modules/Kueche/Zeltlotse.Kueche/KuecheModul.cs:38-50`: Routengruppe für Rezepte, `Mitarbeit` (:22). Neue Gruppen und Registrierungen kommen hierher.
- `Daten/KuecheDbContext.cs`: DbSets, `RezeptSperrenAsync` (:78, `FOR UPDATE`-Muster), `KuecheModell.Konfigurieren` (:111-142), Design-Zeit-Fabrik.
- `Rezepte/KuecheWerte.cs`: `MengeLesen`, `Hochrechnen`, `TextOderNull`, `PflichtFehler`, `KuecheProbleme` (409), `FeldPatch.Lesen`.
- `Rezepte/ZutatBearbeiten.cs`: Slice-Muster (Command, Ergebnis-Union, Validator, Handler mit `IFreizeitZeitpunkte`, Endpunkt).
- `Rezepte/RezepteLaden.cs`: `KuecheLesen` (Vergleich de-DE, `AlsApi`).
- `Datenschutz/KuecheLoeschlauf.cs`: `RezepteLoeschen` per `ExecuteDeleteAsync`. Neue Tabellen kommen davor.
- `Zeltlotse.Kueche.csproj`: braucht neu eine Referenz auf `Programm.Contracts`.
- `src/Zeltlotse.Api.Contracts/Kueche/Rezepte.cs`: Muster für DTOs, `ZutatFelder.ZahlText` und `Allergene.Alle`.

**Programm**
- `src/Modules/Programm/Zeltlotse.Programm.Contracts/`: heute nur `Events/EventsNamensraum.cs`.
- `Zeltlotse.Programm/Programme/Programmpunkt.cs`: `Tag` (:37), `Beginn` (:40), `Titel`, `MahlzeitArt? Mahlzeit` (:46). Mahlzeiten sind nicht löschbar.
- `Programme/MahlzeitenSicherstellen.cs`: Command, der die Mahlzeiten idempotent anlegt. Ausgelöst wird er heute nur lazy in `WochenrasterLaden.cs:20-40`.
- `ProgrammModul.cs`: Registrierung. Muster für eine Leseschnittstelle: `FreizeitenModul.cs:58`.

**Freizeiten**
- `Freizeiten.Contracts/Personen/IFreizeitPersonenzahlLesen.cs` und `Freizeiten/Rechte/FreizeitPersonenLesen.cs:33-48`: Zählregel. `PersonenAsync` kommt dazu.
- `Domaene/Freizeit.cs:80-86`: `FragenTeilnehmende`/`FragenMitarbeitende`. Dazu kommt `IFreizeitFragenLesen` (neu).
- `Zeitpunkte/IFreizeitTageLesen`, `IFreizeitZeitpunkte`.

**Personen**
- `Personen.Contracts/Freizeitpass/Fragenkatalog.cs`: `Allergien`, `Allergene`. Antwort-Wert ist eine Komma-Liste.
- `Personen/Freizeitpass/FreizeitpassEinsicht.cs:92-128`: `FreizeitpassZugriff.JuengsteFreigabenAsync`, `Zustand`.
- `Freizeitpass/Notfallordner.cs:26-78`: Vorbild für das Mengen-Lesen samt Protokoll.
- `FreizeitpassAntwort.cs` und `FreizeitpassRegeln.TextAsync`: Entschlüsseln.
- `Zugriffsprotokoll/EinsichtProtokoll.cs`: `EinsichtZweck`, `EinsichtProtokollieren`, Roh-INSERT `ON CONFLICT`. `LesendePersonId` wird nullable, der Index bekommt `NULLS NOT DISTINCT`.
- `ZugriffsprotokollEndpunkt.cs`, `Api.Contracts/Personen/Zugriffsprotokoll.cs` (`ZugriffZweck`), `Ui/Texte/Anzeige.cs`: Anzeige des Zwecks.
- `PersonenModul.cs:38-47`: Registrierung.

**Oberfläche**
- `src/Zeltlotse.Ui.Komponenten`: `Reiter` (Links, Auswahl in der URL), `Datentabelle`, `Card`, `InlineBestaetigung`, `Auswahl` (Radio), `Knopf` (`Standard`/`Outline`/`Text`), `Meldung`, `LeerZustand`, `Feld`, `Formular`, `Fehleruebersicht`. Einen Vergleichsbaustein gibt es noch nicht.
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs`: `LadenAsync<T>` (:91), `FehlerAsync` (:105), Pfad-Helfer. `Autosave/AutosaveFeld.razor`.
- `src/Zeltlotse.App/Features/Kueche/Rezepte.razor`, `Rezept.razor`, `KuecheTexte.cs`: Seitenmuster.
- `Features/Programm/Wochenraster.razor`: Tag-Reiter mit `?tag=` (:60, :121, :148, :187).
- `Features/Freizeiten/FreizeitStart.razor:202-206`: Knopf „Küche“.

**Tests, Demo, Doku**
- `tests/Zeltlotse.Api.Tests/RezepteTests.cs`: Rechtematrix (:536-608), Gelaufen per SQL (:505), Löschlauf (:632).
- `FreizeitpassSzenario.cs`: `KindAnmeldenAsync`, `BestaetigenAsync`, `FragenAsync`, `AntwortAsync`; Emma hat bereits `allergien = "milch"`.
- `NotfallordnerTests.cs`, `ZugriffsprotokollTests.cs`: Vorbilder für Freigabe- und Protokollfälle.
- `EndpunktListeTests.cs:292-300` (Küche-Routen) und `:318-320` (Zähler 8).
- `MigrationsReihenfolgeTests.cs`, `tests/Zeltlotse.Kueche.Tests/RezeptTests.cs`, `tests/Zeltlotse.Ui.Tests/RezeptSeitenTests.cs` (`NachgebildeterHandler`).
- `tests/Zeltlotse.ArchitectureTests/Loesung.cs:38-40`: Pfeile Küche → Programm/Freizeiten/Personen sind schon erlaubt.
- `DokuRegeln.cs:16-31` (`PflichtSeiten`) und `:56` (Bild-Verlinkung).
- `src/Zeltlotse.Api/Demo/DemoDaten.cs`: `KuecheAsync` (:594), `ProgrammAsync` mit Mahlzeiten (:866). `DemoKatalog.cs`: Freizeit 02.–13.08.2027, Personenzahl 4, Mia bestätigt mit `gluten`.
- `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs:295-310`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:2078-2160`: Abschnitt 4-1, `KuecheAsync`, `BildAsync`.
- `doku/src/content/docs/kueche/index.md` und `rezepte.md` („Noch nicht dabei“ :75), `daten.md`. Die Sidebar baut sich automatisch.

## Tasks & Acceptance

**Execution:**

*Tests zuerst*
- `tests/Zeltlotse.Kueche.Tests/`: Fachtests für
  - Berechnung der Liste, Hand-Menge vor berechneter Menge, Gruppierung und Reihenfolge;
  - Tausch-Abbildung der Hand-Mengen;
  - Ersatzportionen (Schnittmenge, jede Person einmal);
  - Validatoren und Mandantenfilter der neuen Entitäten.
- `tests/Zeltlotse.Api.Tests/GerichteTests.cs` und `EinkaufslisteTests.cs` (neu): alle 10 Routen mit
  - Leitung/Team 2xx;
  - Org-Admin ohne Rolle, fremde Organisation, unbekannte Freizeit 404; anonym 401;
  - Schalter aus 404; Gelaufen 409 bei Schreiben;
  - Zeilen der I/O-Matrix gegen echtes PostgreSQL, Gleichzeitigkeit, Löschlauf.
- Tests für die neuen Leseschnittstellen:
  - Mahlzeiten samt Sicherstellen;
  - `PersonenAsync`, `IFreizeitFragenLesen`;
  - Allergen-Profile: Stufe, Ablauf, Frage nicht freigegeben, gelöschter Schlüssel → Person übersprungen, Protokoll einmal je Tag ohne lesende Person, Zugriffsprotokoll-Anzeige.
- `tests/Zeltlotse.Ui.Tests`: bUnit für
  - `Vergleich`;
  - Essensplan (Tag-Reiter, Vormerken, Tausch-Vorschau mit Übernehmen/Anpassen/Abbrechen, Konflikt);
  - Einkaufsliste (Abgleich, „fragt keine Allergien ab“, Hand-Menge, Hand-Posten, nur lesbar);
  - Küche-Reiter.

*Server*
- `Programm.Contracts` + `Programm`: `IFreizeitMahlzeitenLesen.MahlzeitenAsync(freizeitId)`. Sie liefert `FreizeitMahlzeit(ProgrammpunktId, Tag, Beginn, Titel, MahlzeitArt)` mit eigenem Enum. Fehlende Mahlzeiten stellt sie vorher über `MahlzeitenSicherstellen` sicher.
- `Freizeiten.Contracts` + `Freizeiten`:
  - `IFreizeitPersonenzahlLesen.PersonenAsync`;
  - `IFreizeitFragenLesen.FragtAbAsync(freizeitId, kennung)` (Teilnehmende oder Mitarbeitende).
- `Personen.Contracts` + `Personen`:
  - `IFreizeitpassAllergene.ProfileAsync(freizeitId, personIds)` → `IReadOnlyList<IReadOnlyList<string>>`;
  - neuer Zweck `AllergeneFuerKueche` samt Spiegel in `ZugriffZweck`;
  - `LesendePersonId` nullable, Migration, Anzeige im Zugriffsprotokoll.
- `Küche`:
  - Entitäten `Gericht` (Id, OrganisationId, FreizeitId, ProgrammpunktId, RezeptId mit FK Cascade, Version);
  - `MengeVonHand` (GerichtId mit FK Cascade, ZutatId mit FK Cascade, Menge, Version; eindeutig je Gericht und Zutat);
  - `Einkaufsposten` (Bezeichnung ≤ 200, Menge, Einheit ≤ 30, `DateOnly? Tag`, Version);
  - Migration `Einkaufsliste`, Sperrmethoden;
  - Slices für alle Routen, gemeinsame Abfrage-Klasse für Liste und Vorschau;
  - Löschlauf erweitert.
- `src/Zeltlotse.Api.Contracts/Kueche/` (neu: `Essensplan.cs`, `Einkaufsliste.cs`):
  - DTOs für Essensplan, Gericht, Tauschvorschau (Vorher/Nachher-Zeilen mit `geaendert`) und Einkaufsliste (`personenzahl`, `allergienAbgefragt`, Allergen-Übersicht mit Anzahl, Tage mit Posten);
  - Posten mit `gerichtId?`, `zutatId?`, `postenId?`, Bezeichnung, Menge, Einheit, Rezept- und Mahlzeit-Name, `vonHand`, `version`;
  - Ersatzportionen je Gericht;
  - Feld-Enums samt `…Felder`.
- `EndpunktListeTests.cs`: neue Routen und Zähler.

*Oberfläche*
- `Ui.Komponenten/Vergleich.razor` (+CSS nach ELKW-Tokens) und `Ui/Api/KuecheApiClient.cs` erweitern; Mahlzeit-Namen in `Anzeige`.
- `App/Features/Kueche/KuecheReiter.razor` mit „Rezepte · Essensplan · Einkaufsliste“ auf allen drei Küche-Seiten.
- `Essensplan.razor` (`…/kueche/essensplan?tag=`):
  - Tag-Reiter, Vorauswahl heute oder erster Tag;
  - je Mahlzeit eine Card mit Gerichten, Hinweis „N Ersatzportionen einplanen (enthält …)“, „Tauschen“, „Entfernen“ (Inline-Bestätigung);
  - „Gericht vormerken“ per `Auswahl` der Rezepte; ohne Rezepte Leer-Zustand mit Link zu den Rezepten;
  - Tauschen: Auswahl des neuen Rezepts → `Vergleich` → „Übernehmen“ (bleibt), „Anpassen“ (übernimmt und öffnet die Einkaufsliste am Tag), „Abbrechen“.
- `Einkaufsliste.razor` (`…/kueche/einkaufsliste`):
  - Card „Allergen-Abgleich“ (Übersicht oder Satz „fragt keine Allergien ab“);
  - je Tag eine Card mit `Datentabelle` (Zutat, Menge als `AutosaveFeld`, Einheit, Gericht · Mahlzeit, „von Hand“) und Ersatzportionen-Zeilen;
  - Hand-Posten mit Autosave und „Entfernen“; Formular „Posten ergänzen“;
  - Gelaufen nur lesbar, Ladefehler nicht als leer.
- `Rezept.razor`: Löschen-Satz nennt, dass vorgemerkte Gerichte mit entfernt werden.

*Demo, Doku, Screenshots*
- `DemoKatalog.cs`/`DemoDaten.cs`: Schritt `GerichteAsync` nach `ProgrammAsync`, nur über Commands und idempotent. Inhalt:
  - Di 03.08.: Frühstück Pfannkuchen, Mittagessen Chili sin Carne;
  - Hand-Menge Mehl (Pfannkuchen) 500 g;
  - Hand-Posten „Äpfel“ 3 kg am 03.08.
- `DemoDatenTests` entsprechend anpassen.
- `DokuScreenshotTests.cs`: Abschnitt „Story 4-2“ mit `essensplan`, `gericht-tauschen` (Vorschau, ohne Übernehmen) und `einkaufsliste`, je Handy und Laptop mit `AxeAsync`, nur lesend.
- Doku:
  - neu `kueche/essensplan.md` und `kueche/einkaufsliste.md`: Wer, Weg, Vormerken, Tauschen mit Vorschau, Hand-Mengen, Hand-Posten, Abgleich und Ersatzportionen samt Zählregel, Datenschutz (nur Anzahlen, Protokoll), nach dem Abschluss, „Noch nicht dabei“ (Freigeben/Abhaken, Tagesansicht, Restetag), Screenshots;
  - ergänzt: `kueche/index.md`, `kueche/rezepte.md` („Noch nicht dabei“), `daten.md` (Allergen-Abgleich im Zugriffsprotokoll);
  - `DokuRegeln.PflichtSeiten` um die beiden neuen Seiten erweitern.

**Acceptance Criteria:**
- Given Pfannkuchen ist am Dienstag-Frühstück vorgemerkt, when die Küche im Essensplan „Tauschen“ wählt und Chili sin Carne aussucht, then erscheinen Vorher- und Nachher-Posten nebeneinander (Handy: untereinander), and die Einkaufsliste bleibt unverändert, bis „Übernehmen“ getippt ist.
- Given eine Hand-Menge für Mehl, when sich die Personenzahl ändert, then zeigt die Einkaufsliste weiter die Hand-Menge mit „von Hand“.
- Given Mia hat Gluten im Freizeitpass und ist bestätigt, when das Team die Einkaufsliste öffnet, then steht beim Pfannkuchen-Gericht „1 Ersatzportion einplanen (enthält Gluten)“, and Mias Zugriffsprotokoll zeigt einen Eintrag „Allergen-Abgleich der Küche“.
- Given Verpflegung ist aus oder die Freizeit gelaufen, when Essensplan oder Einkaufsliste geöffnet werden, then „Diese Seite ist nicht verfügbar.“ bzw. der Hinweis „nur lesbar“ ohne Bearbeitungsknöpfe.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then sind alle Tests grün, die Screenshots `essensplan-*`, `gericht-tauschen-*` und `einkaufsliste-*` erzeugt und der Doku-Build fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 29 findings — high 0, medium 4, low 13, false 11, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: Tausch-Vorschau und „Übernehmen“ können auseinanderlaufen, weil eine Hand-Menge die Version des Gerichts nicht ändert — HandMengeAendern erzwingt jetzt ein UPDATE des gesperrten Gerichts (neue xmin); API-Test Vorschau → Hand-Menge → Tauschen = 409. Teil „Zutaten des neuen Rezepts ändern sich zwischendurch“ ist selten und bräuchte Rezept-Versionen im Tausch-Körper (zurückgewiesen, low).
  - `[low]` `[reject]` Blind: Hand-Menge bleibt bei geänderter Einheit/Bezeichnung der Zutat — die Intent verlangt ausdrücklich „bleibt bei geändertem Rezept erhalten“.
  - `[low]` `[reject]` Blind: Neue Personenzahl führt bei berechneten Mengen zu 409 „Inzwischen geändert“; `Version` im PATCH ungenutzt — der angezeigte Wert ist tatsächlich veraltet, Neuladen ist richtig; `version` hält die AD-19-Form einheitlich. Fix bräuchte Sonderlogik.
  - `[false]` `[reject]` Blind/Edge: Einkaufsliste liest Profile auch ohne Gerichte — gewollt: die Card „Allergen-Abgleich“ zeigt die Übersicht unabhängig von Gerichten (Tasks: Allergen-Abgleich-Card), und jeder Abgleich wird protokolliert.
  - `[low]` `[reject]` Blind: Kleine Anzahlen lassen Rückschlüsse zu — Anzahlen sind von FR-48 und der Intent vorgegeben; Schwellwert wäre neue Speziallogik.
  - `[low]` `[patch]` Blind: `meine-daten.md` nennt unter „Was“ nur „Nachweise eingesehen“ — Aufzählung um „Freizeitpass eingesehen“ und „Allergen-Abgleich der Küche“ ergänzt.
  - `[low]` `[reject]` Blind: `Down` der Migration ZugriffOhnePerson macht anonyme Einträge zur Null-Kennung — Rückrollen wird im Betrieb nicht genutzt; Fix bräuchte Datenumbau.
  - `[medium]` `[defer]` Blind/Edge: Nach Verschieben der Freizeit-Daten fehlen Mahlzeiten an neuen Tagen, alte Mahlzeiten samt Gerichten erscheinen weiter — gleiche Zählregel wie das bestehende `WochenrasterLaden` (Story 3-1), also vorbestehend.
  - `[low]` `[patch]` Blind: Einheit beim Tausch exakt statt wie die Bezeichnung verglichen — Einheit getrimmt und ohne Groß/Klein in `GleicheZutat` und `Gleich`; Fachtest ergänzt.
  - `[low]` `[reject]` Blind: Gericht eines Rezepts ohne Zutaten erscheint nicht auf der Liste, Anker von „Anpassen“ fehlt dann — selten; der Browser ignoriert den fehlenden Anker, Fix bräuchte neue Zweige.
  - `[maybe-false]` `[defer]` Blind: Je Seitenaufruf N Entschlüsselungen und N Protokoll-Commands — gleiches Muster wie Notfallordner; ob es bei 100+ Personen spürbar ist, zeigt nur eine Messung (wenn wahr: medium).
  - `[low]` `[patch]` Blind/Verification-Gap: Datenauskunft-Zeile „Niemand persönlich (Abgleich): Allergen-Abgleich der Küche“ ungetestet — Test in `DatenauskunftTests` ergänzt (die übrigen genannten Testlücken gehören zu den Einträgen oben).
  - `[low]` `[reject]` Edge: Zutat wird gleichzeitig zum Setzen einer Hand-Menge gelöscht → FK-Fehler 500 — sehr selten; Fix bräuchte zusätzliche Sperren mit Deadlock-Risiko.
  - `[low]` `[reject]` Edge: Gleiches beim Tausch, wenn eine Zutat des neuen Rezepts gleichzeitig gelöscht wird — wie oben.
  - `[medium]` `[patch]` Edge: Vorschau ≠ Übernehmen (gleiche Ursache wie der erste Eintrag) — siehe dort.
  - `[false]` `[reject]` Edge: Einkaufsliste ohne `profileLesen`-Schutz — siehe vierter Eintrag.
  - `[low]` `[reject]` Edge: Doppelte gleiche Zeilen im Vergleich werden nicht als Multimenge abgeglichen — nur bei doppelten Zutaten in einem Rezept; Fix bräuchte neue Zuordnungslogik.
  - `[medium]` `[defer]` Edge: Mahlzeiten nach Datumsänderung (gleiche Ursache wie oben) — siehe dort.
  - `[false]` `[reject]` Edge: Essensplan ohne Tage zeigt 01.01.0001 — jede Freizeit hat Anfangs- und Enddatum (`FreizeitTage`), eine unbekannte Freizeit endet vorher in 404.
  - `[low]` `[patch]` Edge: Antworten zweier schneller Rezeptwahlen in falscher Reihenfolge setzen die falsche Vorschau — Ergebnis nach dem `await` verworfen, wenn die Wahl inzwischen eine andere ist.
  - `[false]` `[reject]` Edge: „Mahlzeit-Namen in Anzeige“ fehlt — die Namen kommen als `Titel` aus Programm; kein Nutzer sieht eine Lücke.
  - `[false]` `[reject]` Intent R1: „Anpassen“ nur per bUnit geprüft — Lesart (a) ist in Design Notes festgelegt und umgesetzt.
  - `[false]` `[reject]` Intent R2: Matrix „1 (Einheit kg)“ ergibt im Test „1 g“ — die Hand-Menge hat keine eigene Einheit („keine Einheiten-Umrechnung“); erwartet ist „Posten zeigt 1 und vonHand“, das gilt.
  - `[false]` `[reject]` Intent R3: Personen mit leerer Auswahl werden protokolliert — ihre Antwort wurde gelesen, also gehört der Eintrag dazu.
  - `[false]` `[reject]` Intent R5: Ganze Zeile statt nur Menge fett — die geänderte Menge ist fett, die Zeile ebenso; die Intent verlangt nur „nicht nur farbig“.
  - `[false]` `[reject]` Intent R7: Lesen legt in Gelaufen Mahlzeiten an — `MahlzeitenSicherstellen` ist laut Story 3-1 ausdrücklich in jeder Phase erlaubt (keine Planungsänderung).
  - `[low]` `[reject]` Intent: Personenzahl im Test über Team-Rollen statt bestätigte Anmeldung erhöht — gleiche Zählregel, gleiches Ergebnis.
  - `[false]` `[reject]` Intent R8: `IFreizeitFragenLesen` nicht in der Intent genannt — steht in den Tasks des Specs.
  - `[false]` `[reject]` Intent R4: Vorschau ohne Abgleich — die Intent verlangt Ersatzportionen nur in Essensplan und Liste.

## Design Notes

*Rechnen und Speichern*
- **Liste wird berechnet, nicht gespeichert.** Ableitungen laufen automatisch (AD-13). Eine gespeicherte Kopie würde bei neuen Bestätigungen veralten. Gespeichert werden nur Hand-Mengen und Hand-Posten. Damit bleiben Hand-Mengen erhalten (CAP-29), ohne dass eine Sonderlogik für Neuberechnungen nötig ist.
- **Bestätigungspflicht.** Laut FR-50 und AD-13 braucht nur der Tausch eine Bestätigung. Die Vorschau ist ein GET und schreibt nichts. „Übernehmen“ und „Anpassen“ sind die Bestätigung.
  - „Anpassen“ übernimmt den Tausch und führt zur Liste, dort passt die Küche Mengen an.
  - Vormerken und Entfernen sind eigene ausdrückliche Handlungen. Entfernen geht über die Inline-Bestätigung.
- **Ersatzportionen zusätzlich, ohne Abzug.** Die Hauptmenge entspricht der Rezeptseite („Für P Personen“, CAP-28). Die Ersatzportionen nennen nur die Anzahl, denn das Ersatzgericht kennt Zeltlotse nicht. Die Zutaten dafür ergänzt die Küche als Hand-Posten. Etwas zu viel einzukaufen ist hier der sichere Fehler.
- **Keine Zusammenfassung über Gerichte.** Jede Zeile bleibt nachvollziehbar („Pfannkuchen · Frühstück“), und Hand-Mengen haben einen eindeutigen Platz. Eine Summenansicht gehört zum Ausbau.
- **4-3 im Blick.** „Noch nicht gekauft“ betrifft heute alle Posten. 4-3 nimmt gekaufte Posten aus Vorschau und Tausch heraus.

*Daten und Datenschutz*
- **Mahlzeiten.** Programm legt Mahlzeiten lazy an. Die neue Leseschnittstelle stellt sie darum vorher selbst sicher, wie `WochenrasterLaden`. Das ist ein Command im eigenen Modul, kein Schreiben über Modulgrenzen.
- **Allergen-Profile anonym.** Küche braucht die Schnittmenge je Person, eine Zählung je Allergen allein reicht nicht. Darum kommen Profile ohne Kennung und sortiert zurück. Nur Stufe Team zählt (bestätigt). Personen vertraut der übergebenen Menge nicht weiter als bis zur eigenen Freigabe-Prüfung.
- **Protokoll ohne lesende Person.** Es gibt einen neuen Zweck, `LesendePersonId` wird nullable. So steht der Abgleich ehrlich im Zugriffsprotokoll der Person, ohne einem Teammitglied einen Inhaltszugriff zuzuschreiben (AD-8).

*Rechte*
- **Keine 403- und Token-Fälle.** Das gilt wie in 4-1: Alle Routen sind für Leitung und Team, ohne Rolle gibt es 404 (AD-12), Token-Routen gibt es keine.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.Kueche.Tests`, `… Personen.Tests`, `… Freizeiten.Tests`, `… Programm.Tests` und `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*Gerichte*" --filter-class "*Einkaufsliste*" --filter-class "*Zugriffsprotokoll*"`: während der Arbeit grün.
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` und `tests/Zeltlotse.Ui.Tests`: grün.
- Am Ende einmal alle Testprojekte inklusive E2E (Screenshots erzeugt, Rauschen auf HEAD zurückgesetzt): grün.
- `npm run build` in `doku/`: fehlerfrei.

## Auto Run Result

Status: done

**Umgesetzt:** Die Küche merkt Rezepte an Mahlzeiten vor (Essensplan mit Tag-Reitern, Vormerken, Entfernen, Tauschen mit Vorher/Nachher-Vorschau). Die Einkaufsliste wird bei jedem Abruf berechnet, mit Hand-Mengen, Hand-Posten, Allergen-Übersicht und Ersatzportionen je Gericht. Der Allergen-Abgleich läuft über die neue zweckgebundene Schnittstelle `IFreizeitpassAllergene` (nur anonyme Profile) und steht als „Allergen-Abgleich der Küche“ ohne lesende Person im Zugriffsprotokoll. Neue Leseschnittstellen: `IFreizeitMahlzeitenLesen` (Programm), `IFreizeitPersonenzahlLesen.PersonenAsync` und `IFreizeitFragenLesen` (Freizeiten). Neuer Baustein `Vergleich`, Küche-Reiter auf allen drei Küche-Seiten.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Kueche/Zeltlotse.Kueche/Einkauf/*`: Entitäten `Gericht`, `MengeVonHand`, `Einkaufsposten`, Rechenregeln `EinkaufRechnen`, Abfrage `EinkaufAbfrage`, Slices für alle 10 Routen.
- `Kueche/Migrationen/…_Einkaufsliste`, `Personen/Migrationen/…_ZugriffOhnePerson`: neue Tabellen; `LesendePersonId` nullable, Index mit `NULLS NOT DISTINCT`.
- `Personen/Freizeitpass/FreizeitpassAllergene.cs`, `Zugriffsprotokoll/*`, `Datenschutz/PersonenAuskunft.cs`, `PersonLoeschen.cs`: Lesepfad, neuer Zweck, Anzeige ohne lesende Person.
- `Programm/Programme/FreizeitMahlzeitenLesen.cs`, `Freizeiten/Rechte/FreizeitPersonenLesen.cs`: Leseschnittstellen.
- `src/Zeltlotse.Api.Contracts/Kueche/Essensplan.cs`, `Einkaufsliste.cs`, `Rezepte.cs` (Kurznamen der Allergene), `Personen/Zugriffsprotokoll.cs`.
- `src/Zeltlotse.App/Features/Kueche/Essensplan.razor`, `Einkaufsliste.razor`, `KuecheReiter.razor`, `Rezept(e).razor`; `Ui.Komponenten/Vergleich.razor`, `Card.razor` (Überschriftenebene), CSS; `Ui/Api/KuecheApiClient.cs`; `MeineDaten.razor`.
- Demo: `DemoKatalog.cs`/`DemoDaten.cs` Schritt `GerichteAsync`.
- Doku: neu `kueche/essensplan.md`, `kueche/einkaufsliste.md`; ergänzt `kueche/index.md`, `kueche/rezepte.md`, `daten.md`, `erste-schritte/meine-daten.md`, `leitung/module-schalten.md`; Screenshots `essensplan-*`, `gericht-tauschen-*`, `einkaufsliste-*` neu, `rezepte-*` mit Küche-Reitern.
- Tests: `Kueche.Tests/EinkaufTests.cs`, `Api.Tests/GerichteTests.cs`, `EinkaufslisteTests.cs`, `DatenauskunftTests.cs`, `EndpunktListeTests.cs`, `DemoDatenTests.cs`, `Ui.Tests/EssensplanSeitenTests.cs` u. a., E2E-Abschnitt „Story 4-2“, `DokuRegeln.PflichtSeiten`.

**Review:** 29 Befunde. Gepatcht (5 Einträge): Hand-Menge macht eine ältere Tausch-Vorschau veraltet (medium), Einheit beim Tausch ohne Groß/Klein (low), veraltete Vorschau-Antworten verworfen (low), `meine-daten.md` „Was“ (low), Test der Datenauskunft-Zeile (low). Zurückgestellt: 2 (siehe `deferred`). Zurückgewiesen: alle übrigen mit Begründung im Review Triage Log.

**Folge-Review:** nicht empfohlen (`false`) — gepatcht wurden 1 medium und 4 low, kein high.

**Verifikation:**
- Gezielt während der Arbeit: Kueche 88/88, Personen 153/153, Freizeiten 93/93, Programm 91/91, Architektur 135/135, Ui 707/707, Api-Filter (Gerichte, Einkaufsliste, Zugriffsprotokoll, EndpunktListe, DemoDaten, Migrationen) 73/73; Fall-Matrix vollständig durch gelaufene Tests abgedeckt.
- Komplettlauf nach den Patches: alle Testprojekte grün inklusive E2E (Screenshots erzeugt, 19 min). Api.Tests im Gesamtlauf mit Zeitüberschreitungen unter Last, danach allein: 879/880; der eine Fehler `TelemetrieTests.Ausnahmen_erscheinen_in_Spans_nicht_mit_Nachricht` (zwei gleiche Spans bei 18 s Antwortzeit) ist allein grün (3/3) und stammt unverändert aus Story 0-2.
- `npm run build` in `doku/`: fehlerfrei, 58 Seiten.
- 42 Screenshots, die der Lauf nur mit Rendering-Rauschen (zufällige Ports und Links) neu erzeugt hatte, auf HEAD zurückgesetzt.

**Restrisiken:**
- `TelemetrieTests` wackelt unter hoher Last (vorbestehend).
- Gleichzeitiges Löschen einer Zutat beim Setzen einer Hand-Menge oder beim Tausch kann selten einen FK-Fehler (500) auslösen (zurückgewiesen, low).
- Mahlzeiten nach Verschieben der Freizeit-Daten (zurückgestellt) und Kosten des Abgleichs bei großen Freizeiten (zurückgestellt, ungeprüft).
