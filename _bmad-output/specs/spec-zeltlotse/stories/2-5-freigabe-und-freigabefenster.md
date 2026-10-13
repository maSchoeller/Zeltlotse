---
title: 'Freigabe und Freigabefenster'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: '9537bbadc4dcba709392fe5ce6243d117bcaccad'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/DESIGN.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Leitung und Team sehen bisher keinen Freizeitpass anderer Personen. Die Freigabe (`personen.freigabe`) hat Stufe und „gültig bis“, aber keine Fragen-Kennungen. Eltern sehen nicht, bis wann die Freizeit Einsicht hat. Team-Notizen gibt es nicht (CAP-13, FR-24, FR-26, AD-8).

**Approach:**
- Freizeiten gibt die abgefragten Fragen der Zielgruppe in `AnmeldungEingegangen` und `AnmeldungBestaetigt` mit, als Enum-Array im Events-Namensraum. Personen legt sie in der Freigabe ab.
- Ein Personen-Endpunkt im Freizeit-Kontext liefert den Pass einer Person. Er gibt nur die Fragen der Freigabe heraus, prüft Stufe, Fenster und Rolle aus der eigenen Rollenliste und schreibt das Einsicht-Protokoll.
- Freizeiten liefert die Personenliste der Freizeit für Leitung und Team, „Einsicht bis“ an den Anmeldungen der Eltern und Gäste sowie Team-Notizen. Die Notizen sind verschlüsselt mit dem Schlüssel der betroffenen Person.

## Boundaries & Constraints

**Always:**
- **Freigabe:** neue Spalte `Fragen text[]` (Vorgabe leer, Katalog-Kennungen).
  - `FreigabeAnlegen` setzt sie aus `AnmeldungEingegangen.Fragen`.
  - `FreigabeTeamSetzen` ersetzt sie durch `AnmeldungBestaetigt.Fragen`.
  - Bei `null` (alte Outbox-Einträge) bleibt der bisherige Wert.
  - Kein Abgleich bei späteren Änderungen der Auswahl.
- **Ereignisfelder:** Neue Felder hängen additiv hinten an: `FreizeitpassFrage[]? Fragen = null`.
  - `enum FreizeitpassFrage` steht in `Zeltlotse.Freizeiten.Contracts.Events`.
  - Die Mitgliedsnamen sind die PascalCase-Form der Katalog-Kennung (`zecken-entfernen` → `ZeckenEntfernen`), Werte ab 1, nur anhängen.
  - Ein Test prüft die eineindeutige Zuordnung zu `Fragenkatalog.Fragen`.
- **Sicht von Leitung und Team:** Gelesen wird die Freigabe mit dem jüngsten `AngelegtAm` für (Freizeit, Person). Der Zustand ergibt sich so:
  - `beendet`: `GueltigBis <= jetzt` → keine Fragen, `beendetAm = GueltigBis`.
  - `abBestaetigung`: Stufe Leitung und die lesende Person ist nur Team → keine Fragen.
  - `sichtbar`: sonst. Geliefert werden nur die Fragen der Freigabe in Katalogreihenfolge, unbeantwortete mit `wert: null`.
- **Rechte im Personen-Endpunkt:**
  - Die Rolle der lesenden Person kommt nur aus `personen.freizeit_rolle`. Fehlt die Zeile oder die Freigabe → 404.
  - Bei `sichtbar` schreibt der Endpunkt `EinsichtProtokollieren` mit neuem Zweck `FreizeitpassEingesehen = 2`. Den gleichen Wert hat `ZugriffZweck.FreizeitpassEingesehen` (JSON `freizeitpassEingesehen`, Text „Freizeitpass eingesehen“).
  - Kein Protokoll, wenn die lesende gleich der betroffenen Person ist.
- **„Einsicht bis“ für Eltern und Gast:** `AnmeldungZeile.EinsichtBis` und `GastLinkAnmeldung.EinsichtBis` (`DateTimeOffset?`).
  - Wert = `EntschiedenAm` bei Absage, sonst `FreigabeGueltigBis` aus `IFreizeitZeitpunkte`.
  - Anzeige: „Einsicht bis TT.MM.JJJJ“ (`Anzeige.BisEinschliesslich`) bzw. „Einsicht beendet am TT.MM.JJJJ“, wenn vergangen.
- **Team-Notizen:**
  - Gehören Freizeiten. Tabelle `freizeiten.team_notiz`: Id, OrganisationId, FreizeitId, AnmeldungId, PersonId (betroffen), VerfasstVon (PersonId), `[Besonders] Geschuetzt Text` (Schlüssel der betroffenen Person), AngelegtAm.
  - Pflichtfeld nur Text, höchstens 2000 Zeichen.
  - Unveränderlich. Löschen darf jedes Mitglied von Leitung oder Team (Inline-Bestätigung).
  - Erscheinen nie im Freizeitpass und nie in Personen-Endpunkten.
- **Löschen und Auskunft der Team-Notizen:**
  - `PersonGeloescht` löscht Notizen über die Person und setzt `VerfasstVon` auf `GeloeschtePerson.Id`.
  - Der Löschlauf löscht sie mit den Anmeldungen der Freizeit.
  - Die Datenauskunft der betroffenen Person (Familienumfang wie „Anmeldungen“) hat den Abschnitt „Team-Notizen“ (Freizeit, Datum, Text).
- **Neuer Aktionsschlüssel** `freizeitpaesse-sehen` (Leitung und Team). Neue Richtlinie `FreizeitRechte.Freizeitpaesse = [Leitung, Team]`; der Personen-Endpunkt nutzt eine gleiche Liste, die ein Test abgleicht (Vorbild `NachweisRechte`).
- API nur additiv. Test zuerst; jeder neue Endpunkt mit HTTP-Tests gegen PostgreSQL nach AD-30; neue Routen in `EndpunktListeTests`. Durchlauf-Tests für die erweiterten Ereignisse.

**Never:**
- Keine neuen Ereignis-Typen, keine neuen Zeilen in der Tabelle Ereignis-Fluss, keine Änderung an `ARCHITECTURE-SPINE.md`.
- Keine Kopie von Antworten in Freizeiten.
- Keine Antworttexte oder Notiztexte in Logs, Ereignissen, Outbox.
- Kein „Freizeitpass fehlt noch“, keine Erinnerung (2-6). Keine Schnellsuche (2-8). Keine Einwilligungen.
- Keine Pop-ups, keine Drittanbieter-Komponenten, keine eigenen ELKW-Klassen außerhalb `Zeltlotse.Ui.Komponenten`. Keine globale Freizeit-Navigation (nur Knopf auf der Startseite wie bisher).

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Leitung vor Bestätigung | Miriam: GET `/api/freizeiten/{X}/personen/freizeitpass/{emma}`, Anmeldung eingegangen | 200 `sichtbar`, nur Fragen der Freigabe; Protokolleintrag für Emma | – |
| Team vor Bestätigung | Lea, dieselbe Anfrage | 200 `abBestaetigung`, `fragen: []`, kein Protokoll | – |
| Team nach Bestätigung | Lea, Mia bestätigt | 200 `sichtbar`; zweimal am selben Tag = ein Protokolleintrag | – |
| Nicht abgefragt | Emma hat `krankheiten` beantwortet, Freigabe ohne `krankheiten` | Frage fehlt in der Antwort | – |
| Fenster abgelaufen | Uhr nach `GueltigBis` | 200 `beendet`, `beendetAm`, keine Fragen | – |
| Absage | nach Absage | 200 `beendet`, `beendetAm` = Zeitpunkt der Absage | – |
| Person nicht in Freizeit | Freigabe gehört Freizeit Z, Abruf über X (Leitung von X) | 404 | – |
| Org-Admin ohne Rolle / fremde Org / Global Admin | GET Pass, Personenliste, Notizen | 404 | – |
| Notiz anlegen | POST `/api/freizeiten/{X}/anmeldungen/{a}/notizen` `{text}` (Team) | 201; Spalte bytea; GET liefert Text, Verfasser-Name, Datum | leerer Text / > 2000 → 400 Feldfehler `text` |
| Notiz fremder Anmeldung | AnmeldungId einer anderen Freizeit | 404 | – |
| Eltern | `GET /api/ich/anmeldungen` | `einsichtBis` je Anmeldung | – |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs` -- Entität und Empfänger `FreigabeAnlegen`, `FreigabeTeamSetzen`, `FreigabeBeenden`. Hier kommen die Spalte `Fragen` hinzu, `Leitung(..., fragen)` und `ZuTeam(fragen?)`.
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs:246` -- Mapping `personen.freigabe`. Migration in `Migrationen/`; die letzte ist `20261013045314_Freizeitpass`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/{AnmeldungEingegangen,AnmeldungBestaetigt}.cs` -- Feld `Fragen` und neues Enum. `Anmeldung.Eingehen` / `Bestaetigen` in `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Anmeldung.cs` legen die Ereignisse ab. Die Fragen kommen aus `Freizeit.FragenTeilnehmende` / `FragenMitarbeitende` (`Domaene/Freizeit.cs:81,84`); die Auswahl je Art steht in `Anmeldungen/AnmeldungenLesen.cs`.
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs` -- Katalog und `Finden(kennung)`.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEndpunkte.cs` -- `FreizeitpassAbfrage.LadenAsync` mit Entschlüsselung (`FreizeitpassRegeln.TextAsync`). Daraus wird die gefilterte Ansicht.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/PruefEndpunkte.cs` -- Vorbilder:
  - `:26` `NachweisRechte.FreizeitPruefen`;
  - `:155` Rollenprüfung über `db.FreizeitRollen`;
  - `:292` `ProtokollierenAsync` über `ISender`.

  Registriert wird in `PersonenModul.cs` mit `api.MapGroup("/freizeiten/{freizeitId:guid}/personen/...").NurFreizeitRolle(...)`.
- `src/Modules/Personen/Zeltlotse.Personen/Zugriffsprotokoll/{EinsichtProtokoll.cs,ZugriffsprotokollEndpunkt.cs}` -- `EinsichtZweck`. Das Mapping per Zahl-Cast auf `src/Zeltlotse.Api.Contracts/Personen/Zugriffsprotokoll.cs` `ZugriffZweck`. `src/Zeltlotse.Ui/Texte/Anzeige.cs:135` `Zweck`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/Entscheiden/AnmeldungenListe.cs:20` -- Vorbild für die Liste. Namen kommen über `IPersonenNamen`; gelöschte Personen heißen „Gelöschte Person“.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` und `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs:55` (`Aktionsschluessel`) -- neue Richtlinie und neuer Schlüssel. In `Erlaubt` kommt ein `if` dazu.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/FreizeitenModul.cs` -- Routen registrieren.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Datenschutz/FreizeitenDatenschutz.cs` -- `FreizeitPersonLoeschen`, `FreizeitenAuskunft`, `AnmeldungenLoeschen` um Notizen ergänzen.
- `src/Zeltlotse.SharedKernel/Datenschutz/IVerschluesselung.cs` -- `VerschluesselnAsync(personId, text)` / `EntschluesselnAsync`. Steht in jedem Modul per DI zur Verfügung; `Geschuetzt` wird global auf bytea abgebildet.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/FreizeitseiteBearbeiten.cs` -- Vorbild Slice (Command, Validator, Handler, Endpunkt).
- `src/Zeltlotse.Api.Contracts/Freizeiten/Anmeldungen.cs` -- `AnmeldungZeile` (:70), `GastLinkAnmeldung` (:96) additiv erweitern. Neu: `Personenliste.cs`, `TeamNotizen.cs`, `src/Zeltlotse.Api.Contracts/Personen/FreizeitpassEinsicht.cs` (verwendet `FreizeitpassFrage` wieder).
- `src/Zeltlotse.Ui/Api/{FreizeitenApiClient,PersonenApiClient}.cs` -- Muster `LadenAsync<T>` (403/404 → null); Ergebnis-Records bei POST.
- `src/Zeltlotse.App/Features/Freizeiten/{FreizeitStart.razor,Anmeldungen.razor}` -- Knopf-Muster (`Darf(...)`) und Muster für die Listen-Seite (`Datentabelle`, `Status`, `LeerZustand`, `InlineBestaetigung`).
- `src/Zeltlotse.App/Features/Personen/{Freizeitpass.razor,FamilieAnsicht.razor}` -- Zeile „Einsicht bis“. In `Freizeitpass.razor` kommt sie nach dem Kopf (~Z. 46), in `FamilieAnsicht` in `AnmeldungenListe`.
- `src/Zeltlotse.App/Features/Personen/FamilieAnsicht.razor` -- Vorbild für das Formular „Eintrag hinzufügen“ (`Formular`, `Feld`, `Knopf Typ="submit"`, `Meldung`).
- `src/Zeltlotse.Public/Seiten/{GastLink,GastFreizeitpass}.razor` -- Zeile „Einsicht bis“.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs`, `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` -- Demo-Blöcke und Zählungen.
- `tests/Zeltlotse.Api.Tests/` -- Muster `AnmeldungEntscheidenTests.cs` (AD-30-Schleife Z. ~97), Helfer, `EndpunktListeTests.cs`, `DatenauskunftTests.cs`.
- `tests/Zeltlotse.Ablaeufe.Tests/` -- `AnmeldeAblauf`, `AnmeldungBestaetigtTests` u. a.
- `tests/Zeltlotse.Ui.Tests/` -- `NachgebildeterHandler`; Muster `AnmeldungenSeitenTests`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- `BildAsync`, `AxeAsync`, `PerLinkAnmeldenAsync`; `Freizeit_Start` erwartet Texte der Startseite.
- `doku/src/content/docs/` -- `mitarbeitende/`, `leitung/index.md` („Einsicht in Freizeitpässe“), `eltern/freizeitpass.md`, `daten.md` („Freigabe…“, „Freigabefenster…“). Dazu `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (`PflichtSeiten`; jedes PNG braucht einen Link).

## Tasks & Acceptance

**Execution:**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/*` und `Domaene/Anmeldung.cs` (+ Aufrufer) -- Enum `FreizeitpassFrage`, Feld `Fragen` in beiden Ereignissen. Freizeiten bildet die aktuelle Auswahl der Zielgruppe ab. Test der Zuordnung zum Katalog in `tests/Zeltlotse.Freizeiten.Tests`.
- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs` + Mapping + Migration `FreigabeFragen` -- Spalte `Fragen`; Empfänger setzen bzw. ersetzen sie. Die Durchlauf-Tests in `tests/Zeltlotse.Ablaeufe.Tests` prüfen `personen.freigabe."Fragen"` nach Eingang und Bestätigung.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs` (neu) -- `GET /api/freizeiten/{freizeitId}/personen/freizeitpass/{personId}` mit `NurFreizeitRolle(FreizeitpassRechte.Einsicht)`. Antwort `FreizeitpassEinsicht(PersonId, Vorname, Nachname, Zustand, EinsichtBis, BeendetAm?, Fragen)`; Zustand-Enum `Sichtbar/AbBestaetigung/Beendet`. Regeln laut Boundaries und Protokoll.
- `src/Modules/Personen/Zeltlotse.Personen/Zugriffsprotokoll/*`, `Api.Contracts/Personen/Zugriffsprotokoll.cs`, `Ui/Texte/Anzeige.cs` -- neuer Zweck.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Personenliste/` (neu) -- `GET /api/freizeiten/{freizeitId}/personenliste` (Leitung und Team). Liefert Anmeldungen mit Status eingegangen oder bestätigt, ohne abgesagte: `PersonenlisteZeile(AnmeldungId, PersonId, Vorname, Nachname, Art, Status)`, sortiert nach Art, Nachname, Vorname.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/TeamNotizen/` (neu) + Mapping + Migration `TeamNotizen` -- Entität, Commands `TeamNotizAnlegen` und `TeamNotizLoeschen` (Benutzer-Commands, Pipeline-Schreibschutz), Validator. Endpunkte:
  - `GET .../anmeldungen/{anmeldungId}/notizen`: neueste zuerst, mit Verfasser-Name, Datum, Text, Id.
  - `POST .../anmeldungen/{anmeldungId}/notizen`
  - `DELETE .../anmeldungen/{anmeldungId}/notizen/{notizId}`
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Datenschutz/FreizeitenDatenschutz.cs` -- Löschen, Platzhalter, Löschlauf, Auskunft-Abschnitt „Team-Notizen“.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/AnmeldungenLesen.cs` + Contracts -- `EinsichtBis` an `AnmeldungZeile` und `GastLinkAnmeldung`.
- `FreizeitRechte.cs`, `Aktionsschluessel` -- `freizeitpaesse-sehen`. Ein Test hält `FreizeitpassRechte.Einsicht` gleich mit `FreizeitRechte.Freizeitpaesse`.
- `src/Zeltlotse.Ui/Api/*` -- Client-Methoden: `PersonenlisteLadenAsync`, `FreizeitpassEinsichtLadenAsync`, `TeamNotizenLadenAsync`, `TeamNotizAnlegenAsync` (Ergebnis mit Feldfehler), `TeamNotizLoeschenAsync`.
- `src/Zeltlotse.App/Features/Freizeiten/Freizeitpaesse.razor` (neu, `/freizeiten/{FreizeitId}/freizeitpaesse`) -- Gate über `Darf(freizeitpaesse-sehen)`, sonst `NichtVerfuegbar`.
  - `h1` „Freizeitpässe“, je Art ein Abschnitt („Teilnehmende“, „Mitarbeitende“) mit `Datentabelle`: Name als Link auf den Pass, Status.
  - `LeerZustand` „Noch keine Anmeldungen.“
  - `FreizeitStart.razor`: Knopf „Freizeitpässe“ bei `freizeitpaesse-sehen`.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitpassEinsicht.razor` (neu, `/freizeiten/{FreizeitId}/freizeitpaesse/{PersonId:guid}`) -- Kopf mit Name und „Einsicht bis TT.MM.JJJJ“. Danach je nach Zustand:
  - `sichtbar`: Card „Angaben“ als Liste Frage → Antwort, nur lesend. Optionen erscheinen als Texte, Ja/Nein als „Ja“/„Nein“, leer als „keine Angabe“, Datum als TT.MM.JJJJ.
  - `abBestaetigung`: „Freizeitpass sichtbar ab Bestätigung der Anmeldung.“
  - `beendet`: „Einsicht beendet am TT.MM.JJJJ.“

  Darunter Card „Team-Notizen“ mit dem Hinweis „Bleiben in der Freizeit und erscheinen nicht im Freizeitpass.“: Liste neueste zuerst mit Verfasser und Datum, Löschen per `InlineBestaetigung`, Formular mit `Feld Mehrzeilig` „Neue Notiz“ und Knopf „Notiz speichern“. Die Notizen hängen an der Anmeldung; die Seite findet die Anmeldung über die Personenliste.
- `Features/Personen/{Freizeitpass,FamilieAnsicht}.razor`, `Public/Seiten/{GastLink,GastFreizeitpass}.razor` -- Zeile „Einsicht bis …“ bzw. „Einsicht beendet am …“ (nur wenn `EinsichtBis` gesetzt).
- `src/Zeltlotse.Api/Demo/*` -- Reihenfolge umstellen: Die Fragen-Auswahl kommt vor den Anmeldungen, damit die Freigaben Fragen tragen. Daniel beantwortet für Mia `allergien` = `gluten`, `notfallkontakte` = „Daniel Probst (Vater), 0123 456781“, `schwimmen` = `nein` (nur ohne Antwort). Lea schreibt eine Team-Notiz zu Mia: „Hatte letztes Jahr am ersten Abend Heimweh; ein Gespräch am Lagerfeuer hat geholfen.“ (nur ohne Notiz). `DemoDatenTests` anpassen.
- `tests/Zeltlotse.Api.Tests/` -- neu `FreizeitpassEinsichtTests.cs`, `PersonenlisteTests.cs`, `TeamNotizenTests.cs` nach Matrix und AD-30. Erweitert werden:
  - `EndpunktListeTests` und `DatenauskunftTests`;
  - die Tests der Anmeldungs-Lesepfade (`einsichtBis`) und des Gast-Links;
  - Zugriffsprotokoll mit neuem Zweck;
  - Konto-Löschung (Notizen weg, Verfasser-Platzhalter).
- `tests/Zeltlotse.Ui.Tests/` -- Seiten-Tests:
  - Freizeitpässe: Liste, leer, kein Recht;
  - Einsicht: drei Zustände, Notiz anlegen, Feldfehler, löschen;
  - „Einsicht bis“ in Freizeitpass und Familie.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories (Handy, Laptop, axe):
  - `freizeitpaesse`: Lea, Liste;
  - `freizeitpass-einsicht`: Lea, Mias Pass mit Notiz;
  - `freizeitpass-vor-bestaetigung`: Lea, Emma.

  `Freizeit_Start` und betroffene Bilder werden neu erzeugt.
- `doku/src/content/docs/mitarbeitende/freizeitpaesse.md` (neu, in `PflichtSeiten`) -- Pässe ansehen, Zustände, Team-Notizen; alle drei Screenshot-Paare.

  Ergänzt werden:
  - `leitung/index.md`: Einsicht ab Eingang, Link;
  - `mitarbeitende/index.md`;
  - `eltern/freizeitpass.md`: „Einsicht bis“;
  - `daten.md`: nur abgefragte Fragen, Fragen zum Zeitpunkt von Eingang und Bestätigung, Einsicht-Protokoll, Team-Notizen bleiben in der Freizeit und erscheinen nur in der Datenauskunft.

**Acceptance Criteria:**
- Given Emma ist eingegangen, when Miriam (Leitung) ihren Pass in der App öffnet, then sieht sie genau die freigegebenen Fragen mit Antworten, und Katrin sieht im Zugriffsprotokoll „Freizeitpass eingesehen“ von Miriam.
- Given Emma ist eingegangen, when Lea (Team) den Pass öffnet, then steht dort „Freizeitpass sichtbar ab Bestätigung der Anmeldung.“; nach der Bestätigung sieht sie die Angaben.
- Given das Freigabefenster ist abgelaufen oder die Anmeldung abgesagt, when Leitung oder Team den Pass öffnen, then steht „Einsicht beendet am …“ und keine Angabe ist sichtbar.
- Given Katrin am Handy, when sie Emmas Freizeitpass oder die Familienübersicht öffnet, then steht dort „Einsicht bis 12.09.2027“; der Gast sieht dieselbe Zeile auf Gast-Link und Gast-Freizeitpass.
- Given eine Team-Notiz zu Mia, when Katrin, Daniel oder ein Gast ihren Pass oder ihre Anmeldungen lesen, then erscheint die Notiz nirgends außer in Daniels Datenauskunft; in der Datenbank steht der Text nur als bytea.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then ist alles grün inklusive Architekturtests, die drei neuen Screenshot-Paare existieren und sind verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 34 findings — high 0, medium 0, low 23, false 11, maybe-false 0 (Doppel derselben Stelle und desselben Befunds teilen Verdikt und Route)
- findings:
  - Blind Hunter
    - `[false]` `[reject]` Team-Notizen ignorieren Stufe und Fenster der Freigabe — Notizen sind laut FR-26/Spec Arbeitsdaten der Freizeit, nicht Teil des Freizeitpasses; Sichtbarkeit für Leitung und Team ist so festgelegt.
    - `[low]` `[reject]` Lesen von Notizen landet nicht im Zugriffsprotokoll — AD-8 protokolliert Freizeitpass-Inhaltszugriffe; Notizen sind eigene Daten der Freizeit. Fix wäre neue Protokoll-Logik.
    - `[low]` `[reject]` Eltern sehen nach der Bestätigung neu ergänzte Fragen, die die Freizeit nicht sieht — bewusste Entscheidung (Design Notes, daten.md); Fix bräuchte neuen Lesepfad der Freigabe für Eltern.
    - `[low]` `[patch]` Entfernen einer Frage nach der Bestätigung verkleinert die Freigabe nicht, undokumentiert — Satz in `daten.md` ergänzt.
    - `[low]` `[reject]` Freigaben aus der Zeit vor 2-5 haben leere Fragen — betrifft nur Entwicklungsdatenbanken (Design Notes), kein Betrieb.
    - `[low]` `[reject]` Gelöschte Person in der Personenliste mit Link auf „nicht verfügbar“ — selten, gleiches Muster wie die Anmeldungen-Liste aus 2-3; Client kennt die Platzhalter-Kennung nicht, Fix bräuchte neues Vertragsfeld.
    - `[low]` `[patch]` „Einsicht bis“ doppelt berechnet, Absage nach Fensterende weicht ab — Eltern-Wert nimmt jetzt das frühere von Absage und Fensterende (wie `Freigabe.Beenden`), Test ergänzt.
    - `[low]` `[patch]` „Einsicht beendet am“ springt einen Tag gegenüber „Einsicht bis“ — `Anzeige.Einsicht` nutzt `BisEinschliesslich` auch für „beendet“, Seite rendert über `Anzeige.Einsicht`.
    - `[low]` `[reject]` Notizen an abgesagten Anmeldungen bleiben unerreichbar bis zur Frist — selten, Löschlauf entfernt sie; Fix bräuchte eigene Ansicht.
    - `[low]` `[patch]` Löschen meldet Erfolg auch bei 403/404 — Ergebnis wird ausgewertet, bUnit-Test ergänzt.
    - `[low]` `[patch]` Doppelte Fragen-Zuordnung und unnötige Abfrage bei Absage — Bestätigung nutzt `Freizeit.FragenFuer`, Abfrage nur beim Bestätigen.
    - `[false]` `[reject]` Veraltete Kennung lässt Eingang/Bestätigung abstürzen — AD-10: Kennungen werden nie entfernt; Auswahl wird beim Speichern gegen den Katalog geprüft, Zuordnungstest hält Enum und Katalog gleich.
    - `[low]` `[reject]` Datenauskunft nennt den Verfasser der Notiz nicht — Spec legt Freizeit, Datum, Text fest; schützt die Teammitglieder.
    - `[low]` `[reject]` Löschen einer Notiz hinterlässt keine Spur — „Vertrauen statt Rechte-Matrix“; Fix wäre neue Protokoll-Logik.
    - `[low]` `[reject]` Testlücken zu Notizen vor Bestätigung/nach Fenster — folgen aus dem abgelehnten ersten Befund (Verhalten wie festgelegt).
  - Edge Case Hunter
    - `[low]` `[patch]` Löschen meldet Erfolg bei `false` — Doppel, gemeinsam gepatcht.
    - `[low]` `[patch]` Notizen nach Abschluss (409) — kein Pipeline-Schreibschutz vorhanden; Handler prüfen jetzt `Gelaufen` wie die übrigen Slices (409 `freizeit-abgeschlossen`), HTTP-Test ergänzt.
    - `[low]` `[reject]` Link für gelöschte Person — Doppel, gleiche Begründung.
    - `[low]` `[reject]` Fehler der Personenliste macht den ganzen Pass unverfügbar — seltener 5xx, Fehlermeldung erscheint; Fix bräuchte getrennte Fehlerpfade.
    - `[low]` `[patch]` Absage nach Fensterende — Doppel, gemeinsam gepatcht.
    - `[low]` `[reject]` Alte Freigaben mit leeren Fragen — Doppel, gleiche Begründung.
    - `[false]` `[reject]` Unbekannte Kennung wirft in `FragenImEreignis` — Doppel, gleiche Widerlegung.
    - `[false]` `[reject]` Unbekannter Enum-Wert aus der Outbox — Erzeuger und Empfänger laufen im selben Prozess mit demselben Enum; kann nicht auftreten.
    - `[low]` `[patch]` Notiz an abgesagter Anmeldung per API anlegbar — Abgesagt ergibt jetzt 404, HTTP-Test ergänzt.
  - Verification Gap
    - `[low]` `[patch]` Sortierung nach Nachname ungetestet — Test mit Anna Zimmer ergänzt (Nachname- und Vornamenordnung widersprechen sich).
    - `[low]` `[patch]` Kommentar behauptet Pipeline-Schreibschutz nach Abschluss — Doppel zum Abschluss-Befund, gemeinsam gepatcht, Kommentar korrigiert.
  - Intent-Abgleich
    - `[false]` `[reject]` Keine 403-Tests — Leitung und Team sind beide berechtigt, eine andere Freizeit-Rolle gibt es nicht; 404-Fälle sind getestet (Design Notes).
    - `[false]` `[reject]` Token-Fälle nicht erneut getestet — kein neuer Token-Endpunkt; der Gast-Link bekam nur ein additives Feld, Token-Fälle bestehen aus 2-2/2-4.
    - `[false]` `[reject]` HTTP-Endpunkt statt Contracts-Schnittstelle (AD-8) — die Contracts-Schnittstelle ist für fremde Module; Personen liest hier eigene Daten, Muster Nachweis-Prüfung (Design Notes).
    - `[false]` `[reject]` FR-24b „vollständige“ Pässe vs. Begrenzung — FR-24 (Arch) und AD-8 begrenzen auf abgefragte Fragen.
    - `[false]` `[reject]` Notizen in der Datenauskunft gehen über FR-26 hinaus — Art. 15 DSGVO, im Spec festgelegt.
    - `[low]` `[reject]` Doku nur unter Mitarbeitende, kein Leitungs-Screenshot — Leitungsseite verlinkt; ein vierter Screenshot brächte wenig.
    - `[false]` `[reject]` Testgetrieben nicht am Diff erkennbar — Prozessangabe, kein Befund am Code.
    - `[false]` `[reject]` Kein Nachweis für Doku-Build — `npm run build` lief und läuft erneut in der Abschlussprüfung (Auto Run Result).

## Design Notes

- **Fragen per Ereignis, als Enum.** AD-8 verlangt die Kennungen in der Freigabe. Personen darf Freizeiten nicht lesen (kein Pfeil), also ist ein Ereignis der einzige Weg. AD-4 und der Architekturtest verbieten Strings in Ereignissen. Darum gibt es ein Enum im Events-Namensraum, dessen Namen die Kennungen spiegeln; eine Zuordnung per Namensregel plus Test reicht, keine zweite Tabelle.
- **Bei Bestätigung erneuert, danach fest.** So wirken Änderungen an der Auswahl bis zur Bestätigung. Spätere Änderungen erweitern keine bestehende Freigabe. Das passt zum festgeschriebenen Intervall (FR-24d) und verhindert, dass eine Freizeit nachträglich alte Antworten zu nie abgefragten Fragen sieht. Für den Fall, dass die Leitung später eine Frage ergänzt, ist der Ausweg analog (nachfragen). Die Doku sagt das. Bestehende Freigaben aus der Zeit vor dieser Story haben leere Fragen; betroffen sind nur Entwicklungsdatenbanken.
- **Endpunkt im Modul Personen statt Contracts-Schnittstelle.** Die Sicht ist reines Lesen von Personendaten im Freizeit-Kontext (Muster Nachweis-Prüfung). Freizeiten bleibt ohne Antworten. Der Client setzt Liste (Freizeiten) und Pass (Personen) zusammen (AD-2).
- **AD-30 „falsche Rolle → 403“** entfällt bei Personenliste, Pass und Notizen: Leitung und Team sind beide berechtigt, eine andere Freizeit-Rolle gibt es nicht. Geprüft werden Org-Admin ohne Rolle, Org-Überblick, Global Admin und fremde Organisation (404).
- **„Einsicht bis“ aus Freizeiten.** AD-17 nennt `Freizeiten.Contracts` als einzige Quelle; bei Absage ist es der Entscheidungszeitpunkt (wie `Freigabe.Beenden`). So braucht es keinen neuen Personen-Endpunkt für Eltern.
- **Team-Notizen mit dem Schlüssel der betroffenen Person.** AD-14: Daten mit Personenbezug nutzen deren Schlüssel. Wird die Person gelöscht, werden ihre Notizen gelöscht (lesbar wären sie ohnehin nicht mehr). Datenauskunft nach Art. 15 DSGVO für die betroffene Person.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler.
- Gezielt: `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class …`, `tests/Zeltlotse.Ablaeufe.Tests`, `tests/Zeltlotse.Freizeiten.Tests`, `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün.
- Einmal am Ende: `dotnet test` (ganze Lösung inkl. E2E und Doku-Screenshots) -- grün; danach `npm run build` in `doku/` -- fehlerfrei.
- Vor dem Commit: PNGs, die sich nur durch Rendering-Rauschen geändert haben (< 300 abweichende Pixel), per `git checkout` zurücksetzen.

## Auto Run Result

Status: done

**Umsetzung:**
- **Freigabe:** trägt jetzt die abgefragten Fragen (`personen.freigabe."Fragen"`). Sie kommen als Enum-Array `FreizeitpassFrage` in `AnmeldungEingegangen` und `AnmeldungBestaetigt`, werden beim Eingang gesetzt, bei der Bestätigung ersetzt und sind danach fest.
- **Sicht von Leitung und Team:** Neuer Personen-Endpunkt `GET /api/freizeiten/{id}/personen/freizeitpass/{personId}` mit den Zuständen sichtbar, abBestaetigung und beendet. Er liefert nur die freigegebenen Fragen; die Rolle kommt aus der eigenen Rollenliste; das Einsicht-Protokoll schreibt den neuen Zweck „Freizeitpass eingesehen“.
- **Freizeiten:**
  - Personenliste für Leitung und Team.
  - Team-Notizen (GET, POST, DELETE), verschlüsselt mit dem Schlüssel der betroffenen Person. Sie werden gelöscht, wenn die Person gelöscht wird, und im Löschlauf; sie erscheinen in der Datenauskunft. Nach dem Abschluss sind sie nur lesbar.
  - `EinsichtBis` an den Anmeldungen der Eltern und am Gast-Link.
- **Rechte:** neuer Aktionsschlüssel `freizeitpaesse-sehen`.
- **Oberfläche:**
  - neue Seiten „Freizeitpässe“ und Freizeitpass-Einsicht (mit Card „Team-Notizen“);
  - Knopf auf der Startseite der Freizeit;
  - „Einsicht bis / beendet am“ in Freizeitpass, Familie, Gast-Link und Gast-Freizeitpass;
  - neuer Baustein `DetailsListe`.
- **Demo, Doku, Screenshots:**
  - Demo-Daten: Die Fragen-Auswahl kommt vor den Anmeldungen; Mias Antworten und Leas Notiz kamen dazu.
  - Neue Doku-Seite `mitarbeitende/freizeitpaesse.md`, dazu Ergänzungen in `leitung/index`, `mitarbeitende/index`, `eltern/freizeitpass` und `daten.md`.
  - Drei neue Screenshot-Paare.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/{AnmeldungEingegangen,AnmeldungEntschieden,FreizeitpassFrage}.cs` – Fragen im Ereignis.
- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs`, Migration `FreigabeFragen` – Fragen in der Freigabe.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs` – Sicht von Leitung und Team, Protokoll.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{Personenliste,TeamNotizen}/*`, Migration `TeamNotizen`, `Datenschutz/FreizeitenDatenschutz.cs` – Liste, Notizen, Löschen und Auskunft.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/{AnmeldungenLesen,Entscheiden/AnmeldungEntscheiden}.cs`, `Domaene/{Anmeldung,Freizeit,FragenImEreignis}.cs` – EinsichtBis, Fragen beim Eingang und bei der Bestätigung.
- `src/Zeltlotse.Api.Contracts/…` – additive Verträge; `src/Zeltlotse.Ui/…` – Clients und Anzeige; `src/Zeltlotse.App/Features/Freizeiten/{Freizeitpaesse,FreizeitpassEinsicht}.razor`; `src/Zeltlotse.Public/Seiten/{GastLink,GastFreizeitpass}.razor`.
- `src/Zeltlotse.Api/Demo/*`, Tests (Api, Ablaeufe, Freizeiten, Personen, Ui, E2E, Architektur), `doku/…`.

**Review:** 34 Befunde (high 0, medium 0, low 23, false 11).
- Gepatcht (alle low):
  - Löschen meldet nur bei Erfolg „Notiz gelöscht.“;
  - „Einsicht bis“ nach einer späten Absage ist das frühere von Absage und Fensterende;
  - „Einsicht beendet am“ ohne Tagessprung;
  - eine Quelle für die Fragen bei der Bestätigung, keine Abfrage bei Absage;
  - Schreibschutz für Notizen nach dem Abschluss (409 `freizeit-abgeschlossen`);
  - 404 für Notizen an abgesagten Anmeldungen;
  - Test der Sortierung nach Nachname;
  - Doku-Satz zum Entfernen einer Frage.
- Zurückgestellt: nichts.
- Abgelehnt mit Begründung im Triage Log, unter anderem:
  - Notizen folgen nicht der Freigabe (so festgelegt);
  - kein Protokoll beim Lesen von Notizen;
  - Eltern sehen nach der Bestätigung ergänzte Fragen;
  - alte Freigaben ohne Fragen (nur Entwicklungsdatenbanken);
  - Link für gelöschte Personen (Muster aus 2-3);
  - keine 403-Fälle (keine falsche Freizeit-Rolle möglich).

**Follow-up-Review:** `false`. Gepatcht wurden 0 high, 0 medium und 8 low; ein konkretes ungeprüftes Risiko bleibt nicht.

**Prüfung:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- Voller Lauf `dotnet test` nach den Patches: 1588/1589. Rot war ein Architekturtest: Der Helfer `TeamNotizRegeln` hing vom schreibenden DbContext ab. Er nimmt jetzt `IQueryable<Freizeit>`. Danach ergab der erneute volle Lauf 1589/1589 grün, inklusive E2E und Doku-Screenshots.
- `npm run build` in `doku/` fehlerfrei (31 Seiten, nur die bekannte Sitemap-Warnung).
- 24 Screenshots mit höchstens 300 abweichenden Pixeln per `git checkout` zurückgesetzt. Echte Änderungen: Familie, Freizeit-Start, Freizeitpass, Freizeitpass-Fragen, Gast-Link, Gast-Freizeitpass, dazu sechs neue Bilder.

**Restrisiken:**
- Freigaben aus der Zeit vor dieser Story haben leere Fragen (nur Entwicklungsdatenbanken).
- Ergänzt die Leitung nach der Bestätigung eine Frage, sieht die Freizeit die Antwort nicht. Das ist dokumentiert.
- Das Notiz-Datum im Screenshot `freizeitpass-einsicht` ändert sich mit jedem Lauf.
