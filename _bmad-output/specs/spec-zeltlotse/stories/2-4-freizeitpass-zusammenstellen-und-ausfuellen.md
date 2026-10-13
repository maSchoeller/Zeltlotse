---
title: 'Freizeitpass zusammenstellen und ausfüllen'
type: 'feature'
created: '2026-10-13'
status: 'done'
baseline_revision: 'f9b06c46951308cb6f271535b1a32118640a07d5'
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

**Problem:** Es gibt noch keinen Freizeitpass: keinen Fragenkatalog, keine Auswahl der Fragen durch die Leitung und keinen Weg, wie Eltern, Volljährige, Mitarbeitende oder Gäste Angaben wie Allergien, Medikamente und Notfallkontakte erfassen (CAP-11, CAP-12, FR-22, FR-23, AD-10, NFR-6, NFR-7).

**Approach:** Personen besitzt einen festen Fragenkatalog im Code (stabile Kennungen, fester Antworttyp) und die Antworten je Person und Frage, verschlüsselt mit dem Personen-Schlüssel. Weil Antworten der Person gehören und nicht an einer Freizeit hängen, sind sie bei der nächsten Freizeit von selbst vorausgefüllt; Antworten mit Bestätigung älter als 12 Monate tragen „Stimmt das noch?“. Freizeiten speichert nur die gewählten Kennungen je Zielgruppe (Teilnehmende, Mitarbeitende) und liefert sie an den Anmeldungen mit; die Clients setzen Fragenliste (Freizeiten) und Antworten (Personen) zusammen (AD-2).

## Boundaries & Constraints

**Always:**
- Katalog nur im Modul Personen (`Personen.Contracts`, statisch im Code, kein DB-Katalog). Freizeiten validiert die Auswahl dagegen (Pfeil Freizeiten → Personen). Eigene Fragen der Leitung gibt es nicht.
- Katalog (Reihenfolge = Anzeigereihenfolge; Allergien, Medikamente, Notfallkontakte stehen immer oben und nie zugeklappt):

  | Kennung | Frage | Antworttyp |
  |---|---|---|
  | `allergien` | Allergien und Unverträglichkeiten | Auswahl (mehrfach) aus den 14 EU-Hauptallergenen + Freitext „Sonstiges“ |
  | `medikamente` | Medikamente (Name, Dosierung, wann) | Freitext |
  | `notfallkontakte` | Notfallkontakte (Name, Telefon) | Freitext |
  | `ernaehrung` | Ernährung | Auswahl (mehrfach): `vegetarisch`, `vegan`, `kein-schweinefleisch` + Freitext „Sonstiges“ |
  | `krankheiten` | Krankheiten und Einschränkungen | Freitext |
  | `krankenversicherung` | Krankenversicherung | Freitext |
  | `tetanus` | Letzte Tetanus-Impfung | Datum |
  | `schwimmen` | Kann schwimmen | Ja/Nein |
  | `zecken-entfernen` | Zecken dürfen vom Team entfernt werden | Ja/Nein |
  | `hinweise` | Weitere Hinweise für das Team | Freitext |

  Allergen-Schlüssel: `gluten` Glutenhaltiges Getreide, `krebstiere` Krebstiere, `eier` Eier, `fisch` Fisch, `erdnuesse` Erdnüsse, `soja` Soja, `milch` Milch (inkl. Laktose), `schalenfruechte` Schalenfrüchte (Nüsse), `sellerie` Sellerie, `senf` Senf, `sesam` Sesam, `sulfite` Schwefeldioxid und Sulfite, `lupinen` Lupinen, `weichtiere` Weichtiere.
- Wire-Format aller Werte ist ein String (Autosave-Dienst): Ja/Nein `ja`/`nein`; Datum `yyyy-MM-dd`; Auswahl = Schlüssel mit Komma getrennt, vom Server in Katalogreihenfolge ohne Doppelte normalisiert; leer oder `null` = keine Angabe. Freitext höchstens 2000 Zeichen, Sonstiges höchstens 500. Gleiches Format für die Fragen-Auswahl der Freizeit (Kennungen mit Komma).
- Antwort-Zeile je (Person, Kennung) mit `[Besonders] Geschuetzt?` für Wert und Sonstiges, `BestaetigtAm`, `uint Version` (xmin). Jede Wertänderung und „Stimmt“ setzen `BestaetigtAm` auf jetzt. „Veraltet“ = beantwortet und `BestaetigtAm` < jetzt − 12 Monate; der Server berechnet es (`TimeProvider`).
- Autosave nach AD-19 (Konflikt nur bei abweichendem altem Wert desselben Felds; Person bzw. Freizeit unter Zeilensperre laden). Eine noch nicht vorhandene Antwort wird beim ersten PATCH angelegt (Version 0, alter Wert muss leer sein, sonst 409).
- Zugriff auf Antworten: die Person selbst und Eltern ihrer Familie (`IVertretung.DarfAnmeldenAsync`); fremde Person → 404. Gast: nur die Kinder des Gast-Tokens (`IGastZugaenge.PruefenAsync`), sonst 404. Personeneigen nach AD-16: Löschen in `EigeneDatenLoeschenAsync` (und damit im Gast-Löschlauf), Abschnitt „Freizeitpass“ in der Datenauskunft.
- Fragen-Auswahl je Freizeit nur durch die Leitung (`FreizeitRechte.Einstellungen`, Aktionsschlüssel `freizeit-einstellungen`); neue Freizeit = keine Fragen (kein Pflichtkern).
- API-Erweiterungen nur additiv (neue Felder mit Vorgabewert am Ende der Records).
- Test zuerst; jeder neue Endpunkt mit HTTP-Tests gegen echtes PostgreSQL inklusive Pflichtfälle AD-30; neue Routen in `EndpunktListeTests`.

**Never:**
- Keine neuen Ereignisse, keine Änderung an `ARCHITECTURE-SPINE.md`, keine neuen Aktionsschlüssel oder Problemcodes.
- Keine Fragen-Kennungen in `Freigabe`, keine Sicht von Leitung/Team auf Antworten, kein „Einsicht bis“, kein Einsicht-Protokoll für Freizeitpässe (alles Story 2-5). Kein Cockpit-Hinweis „Freizeitpass fehlt noch“ und keine Erinnerung (2-6). Kein Küchen-Abgleich, keine Einwilligungen (Fotos).
- Keine Kopie von Antworten außerhalb von Personen; keine Antworttexte in Logs, Ereignissen oder Outbox.
- Keine Pop-ups; keine Drittanbieter-Komponenten; keine eigenen ELKW-Klassen außerhalb `Zeltlotse.Ui.Komponenten`.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehlerbehandlung |
|---|---|---|---|
| Leitung wählt Fragen | PATCH `/api/freizeiten/{id}/fragen` `{version, alterWert:"", teilnehmende:"medikamente,allergien,allergien"}` | 200, `teilnehmende:"allergien,medikamente"` | – |
| Unbekannte Kennung | `teilnehmende:"allergien,lieblingsfarbe"` | 400, Feldfehler `teilnehmende` | – |
| Gleichzeitig dieselbe Liste | zweiter PATCH mit veraltetem `alterWert` | 409 `konflikt` | Client „Inzwischen geändert – neu laden?“ |
| Team / Org-Admin ohne Rolle / fremde Org | GET oder PATCH `/fragen` | 403 / 404 / 404 | – |
| Erste Antwort | PATCH `/api/ich/freizeitpass/{emma}/antworten/allergien` `{version:0, alterWert:null, wert:"milch,eier"}` von Katrin | 200, `wert:"eier,milch"`, Spalte verschlüsselt (bytea, kein Klartext) | – |
| Fremde Person | dieselbe Anfrage von Daniel | 404 | – |
| Ungültiger Wert | `schwimmen` = `vielleicht`, `tetanus` = `31.02.`, Allergen `nuss` | 400 Feldfehler `wert` | – |
| Unbekannte Kennung im Pfad | `/antworten/lieblingsfarbe` | 404 | – |
| Nächste Freizeit | Emma bei zweiter Freizeit angemeldet | GET zeigt die vorhandenen Antworten | – |
| Veraltet | Uhr + 12 Monate + 1 Tag | `veraltet: true`; nach POST `…/bestaetigen` `false` | `bestaetigen` ohne Antwort → 404 |
| Gast liest/speichert | gültiges Gast-Token, Kind der Gast-Familie | GET 200; POST Formular speichert geänderte Werte, „Stimmt“-Haken bestätigt | – |
| Gast-Token-Fälle | abgelaufen; Anmeldelink-Token statt Gast-Token; Token der Familie A für Kind der Familie B | 404 | POST mit ungültigem Token schreibt `LinkEinloesungFehlgeschlagen` ins Sicherheitsprotokoll |
| Person gelöscht | Konto-Löschung oder Gast-Löschlauf | Antwort-Zeilen weg | – |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen.Contracts/` -- neu `Freizeitpass/Fragenkatalog.cs`: statischer Katalog (Kennung, Frage, Antworttyp, Optionen, MitSonstiges) plus `Normalisieren`/`Pruefen`-Helfer; von Freizeiten nutzbar.
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs` -- DbSet in Schreib- und Lese-Kontext + Mapping in `PersonenModell.Konfigurieren` (Tabelle `freizeitpass_antwort`, eindeutig PersonId+Kennung, `IsRowVersion`). `Geschuetzt` wird global zu bytea (`SharedKernel/Daten/ModulDbContextBasis.cs:30`).
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/Nachweis.cs:35` / `EinsichtPruefen.cs:138` -- Vorbild `[Besonders] Geschuetzt?` und `IVerschluesselung.VerschluesselnAsync(personId, …)`.
- `src/Modules/Personen/Zeltlotse.Personen/Profil/` (`Person.FeldAendern`, `ProfilFeldAendern`) und `src/Modules/Organisationen/Zeltlotse.Organisationen/Datenschutz/LoeschfristenEndpunkte.cs:38-160` -- Vorbild PATCH AD-19 mit Zeilensperre.
- `src/Modules/Personen/Zeltlotse.Personen/Gaeste/GastLesen.cs` -- `IGastZugaenge.PruefenAsync(token)` → `GastZugangInfo(ElternPersonId, Mail, KindPersonIds, GueltigBis)`; Gast-Endpunkt-Vorbild `POST /personen/oeffentlich/gaeste/{gastToken}/kinder` in `PersonenModul.cs`.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/PersonLoeschen.cs` (`EigeneDatenLoeschenAsync`), `PersonenAuskunft.cs` (Abschnitt wie `FreigabenAsync`) -- Löschen/Auskunft ergänzen.
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs` -- Endpunkte registrieren (`/ich/...` mit `.Organisationsuebergreifend()`, Gast anonym mit `.AllowAnonymous().Zugriffsbegrenzt()`).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Freizeit.cs` -- neue Spalten `FragenTeilnehmende`/`FragenMitarbeitende` (`text[]`, Vorgabe leer) + Änderungsmethode nach AD-19; Vorbild `FeldAendern(FreizeitseiteFeld, …)`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/FreizeitseiteBearbeiten.cs` -- Vorbild Slice (Command, Validator, Handler, Lade- und PATCH-Endpunkt) für neuen Slice `Fragen/`.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/AnmeldungenLesen.cs` (inkl. `GastLinkEndpunkt` Z. ~92) -- `GET /api/ich/anmeldungen` und Gast-Ansicht um Fragen ergänzen.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Freizeitseite/OeffentlicheEndpunkte.cs` -- Vertrauensseite um gewählte Fragen (Texte aus dem Katalog) ergänzen.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Rechte/FreizeitRechte.cs` -- `Einstellungen` = nur Leitung (wiederverwenden).
- `src/Zeltlotse.Api.Contracts/Freizeiten/{Anmeldungen.cs,Freizeitseite.cs}` -- `AnmeldungZeile`, `GastLinkAnmeldung` (bisher ohne `AnmeldungId`/`PersonId`), `Vertrauensseite` additiv erweitern; neu `Fragen.cs`. Neu `src/Zeltlotse.Api.Contracts/Personen/Freizeitpass.cs`.
- `src/Zeltlotse.Ui/Api/{PersonenApiClient,FreizeitenApiClient}.cs`, `src/Zeltlotse.Ui/Autosave/{AutosaveDienst.cs,AutosaveFeld.razor,AutosaveAnkreuzfeld.razor}`, `src/Zeltlotse.Ui/Texte/Anzeige.cs` -- Client-Methoden, neuer Autosave-Baustein nach Vorbild Ankreuzfeld.
- `src/Zeltlotse.Ui.Komponenten/` (`Ankreuzfeld`, `Feld`, `Status`, `Knopf`, `Card`, `LeerZustand`, `Fehleruebersicht`) -- neuer statisch tauglicher Baustein `Auswahlgruppe`.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- neue Card „Freizeitpass“; `Features/Personen/FamilieAnsicht.razor`, `Features/Freizeiten/AnmeldungMitKonto.razor` -- Einstiege.
- `src/Zeltlotse.Public/Seiten/GastLink.razor`, `Anmelden.razor` (Formular-Vorbild: `<form method="post">`, `<AntiforgeryToken />`, `[SupplyParameterFromForm]`, Antwort im POST rendern, leeres Formular bei ungültigem Link).
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs`, `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` -- Demo-Block 2-4.
- `tests/Zeltlotse.Api.Tests/` (`AnmeldungEntscheidenTests.cs` als Muster, Helfer `OrgHelfer`, `FreizeitHelfer`, `FamilienHelfer`, `AnmeldeHelfer`, `EndpunktListeTests.cs`, `DatenauskunftTests.cs`, `FreizeitseiteTests.cs`), `tests/Zeltlotse.Ui.Tests/` (`NachgebildeterHandler`, `AutosaveTests.cs`), `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Freizeit_Einstellungen` Z. ~579, `Gast_Link` Z. ~684).
- `doku/src/content/docs/` (`eltern/`, `leitung/`, `mitarbeitende/`, `daten.md`), `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (`PflichtSeiten`).

## Tasks & Acceptance

**Execution:**
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs` -- Katalog laut Tabelle mit Prüf- und Normalisierhilfen; Unit-Tests in `tests/Zeltlotse.Personen.Tests` (Kennungen eindeutig und kebab-case, 14 Allergene, Normalisieren, ungültige Werte) -- eine Quelle für alle.
- `src/Zeltlotse.Api.Contracts/Personen/Freizeitpass.cs` -- Antwort-Formen: GET liefert je Katalogfrage `kennung, frage, art, optionen[{wert,text}], mitSonstiges, wert, sonstiges, version, veraltet`; Feld-Enum `wert`/`sonstiges`; Formular-Körper für den Gast (`antworten[{kennung,wert,sonstiges}]`, `bestaetigt[kennung]`).
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/` -- Entität `FreizeitpassAntwort`, Commands `FreizeitpassAntwortAendern` (Upsert nach AD-19), `FreizeitpassAntwortBestaetigen`, `FreizeitpassFormularSpeichern` (Gast, mehrere Antworten in einer Transaktion), Lese-Abfrage mit Entschlüsselung und `veraltet`; Endpunkte `GET /api/ich/freizeitpass/{personId}`, `PATCH /api/ich/freizeitpass/{personId}/antworten/{kennung}`, `POST /api/ich/freizeitpass/{personId}/antworten/{kennung}/bestaetigen`, `GET`+`POST /api/personen/oeffentlich/gaeste/{gastToken}/freizeitpass/{personId}`; Migration im Ordner `Migrationen/` -- Kern von FR-23.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/{PersonLoeschen,PersonenAuskunft}.cs` -- Antworten löschen und auskunften (entschlüsselt, Kennung, Wert, bestätigt am).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Fragen/` + `Domaene/Freizeit.cs` + `src/Zeltlotse.Api.Contracts/Freizeiten/Fragen.cs` -- `GET`/`PATCH /api/freizeiten/{freizeitId}/fragen` (Leitung) mit `version`, `teilnehmende`, `mitarbeitende` und Katalog (Kennung, Frage); Migration `freizeiten` -- FR-22.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/AnmeldungenLesen.cs`, `Freizeitseite/OeffentlicheEndpunkte.cs` -- `AnmeldungZeile.Fragen`, `GastLinkAnmeldung.AnmeldungId/PersonId/Fragen` (Fragen nach Art der Anmeldung), `Vertrauensseite.FragenTeilnehmende/FragenMitarbeitende` (Fragetexte).
- `src/Zeltlotse.Ui.Komponenten/Auswahlgruppe.razor` -- `fieldset`+`legend`, Optionen als Kästchen (mehrfach) oder Optionsknöpfe (einfach), statisch tauglich wie `Ankreuzfeld`; bUnit-Tests.
- `src/Zeltlotse.Ui/Autosave/AutosaveAuswahlgruppe.razor` -- `[NurInteraktiv]`, nutzt `AutosaveDienst` und `SpeicherHinweis` wie `AutosaveAnkreuzfeld`, sendet die Auswahl als Komma-Liste; bUnit-Tests (gespeichert, Rückgängig, 409, gescheitert).
- `src/Zeltlotse.Ui/Api/*ApiClient.cs` -- Methoden und Pfad-Helfer für die neuen Endpunkte.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- Card „Freizeitpass“ mit zwei `AutosaveAuswahlgruppe` („Fragen für Teilnehmende“, „Fragen für Mitarbeitende“) und dem Satz „Ohne Auswahl fragt der Freizeitpass nichts ab.“
- `src/Zeltlotse.App/Features/Personen/Freizeitpass.razor` (Route `/anmeldungen/{AnmeldungId:guid}/freizeitpass`) -- lädt `MeineAnmeldungen` (Fragen, Person, Freizeit) und den Pass der Person; zeigt nur die Fragen der Freizeit in Katalogreihenfolge: Freitext `AutosaveFeld` mehrzeilig, Datum `AutosaveFeld Typ="date"`, Ja/Nein und Auswahl `AutosaveAuswahlgruppe`, Sonstiges `AutosaveFeld`; veraltet: Status Warnung „Stimmt das noch?“ mit Knopf „Stimmt“; keine Fragen: Leer-Zustand „Diese Freizeit fragt keine Angaben ab.“; unbekannte Anmeldung: Seite „nicht verfügbar“.
- `src/Zeltlotse.App/Features/Personen/FamilieAnsicht.razor`, `Features/Freizeiten/AnmeldungMitKonto.razor` -- je Anmeldung (Kinder und eigene) Link „Freizeitpass“; Status Warnung „Freizeitpass prüfen“, wenn eine abgefragte Frage unbeantwortet oder veraltet ist; nach dem Absenden Link „Freizeitpass für <Vorname> ausfüllen“.
- `src/Zeltlotse.Public/Seiten/GastFreizeitpass.razor` (Route `/gast/{Token}/freizeitpass/{AnmeldungId:guid}`) + Link je Anmeldung auf `GastLink.razor` -- klassisches Formular mit allen Fragen der Anmeldung, Haken „Stimmt noch“ bei veralteten Angaben, „Speichern“, Fehlerübersicht, `noindex`; nach dem Speichern „Freizeitpass gespeichert.“ im POST-Ergebnis.
- `src/Zeltlotse.Api/Demo/*` -- Miriam wählt für Teilnehmende `allergien, medikamente, notfallkontakte, ernaehrung, krankheiten, tetanus, schwimmen, zecken-entfernen`, für Mitarbeitende `allergien, medikamente, notfallkontakte, ernaehrung` (nur wenn leer); Katrin beantwortet für Emma `allergien` = `milch`, `notfallkontakte`, `schwimmen` = `ja` (nur wenn noch keine Antwort); `DemoDatenTests` anpassen.
- `tests/Zeltlotse.Api.Tests/` -- `FragenAuswahlTests.cs`, `FreizeitpassTests.cs`, `GastFreizeitpassTests.cs` nach Matrix und ACs; `EndpunktListeTests`, `DatenauskunftTests`, `FreizeitseiteTests` (Vertrauensseite) und Tests der Anmeldungs-Lesepfade erweitern; Löschen in Konto-Löschung und Gast-Löschlauf.
- `tests/Zeltlotse.Ui.Tests/` -- Seiten-Tests für Freizeitpass (Typen, veraltet + Stimmt, leer, 404), Einstellungen-Card, Familie-Hinweis.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories (Handy, Laptop, axe): `freizeitpass-fragen` (Miriam, Einstellungen-Card), `freizeitpass` (Katrin, Emmas Pass), `gast-freizeitpass` (Gast-Formular nach Gast-Anmeldung); betroffene vorhandene Screenshots neu erzeugen.
- `doku/src/content/docs/eltern/freizeitpass.md`, `leitung/freizeitpass-fragen.md` (neu, in `DokuRegeln.PflichtSeiten`), Ergänzungen in `eltern/index.md`, `eltern/kind-anmelden.md`, `leitung/index.md`, `mitarbeitende/anmelden.md`, `daten.md` (Antworten gehören der Person, vorausgefüllt, „Stimmt das noch?“, nur abgefragte Fragen) -- NFR-20.

**Acceptance Criteria:**
- Given eine neue Freizeit, when die Leitung die Einstellungen öffnet, then sind keine Fragen gewählt und die Vertrauensseite sagt, dass der Freizeitpass derzeit nichts abfragt; nach der Auswahl nennt sie die gewählten Fragen je Zielgruppe.
- Given Fragen für Teilnehmende und andere für Mitarbeitende, when eine Teilnehmer- bzw. Mitarbeiter-Anmeldung über `GET /api/ich/anmeldungen` oder den Gast-Link gelesen wird, then trägt sie genau die Fragen ihrer Zielgruppe.
- Given Katrin am Handy, when sie von der Familienübersicht Emmas Freizeitpass öffnet und Angaben ändert, then sieht sie nur die Fragen dieser Freizeit, Allergien, Medikamente und Notfallkontakte zuerst, und jede Änderung speichert beim Verlassen des Feldes.
- Given Emma hat Antworten aus einer früheren Anmeldung, when sie bei einer zweiten Freizeit angemeldet ist, then sind diese Antworten im Pass vorausgefüllt; ist eine Bestätigung älter als 12 Monate, trägt sie „Stimmt das noch?“, und „Stimmt“ entfernt den Hinweis.
- Given eine Gast-Anmeldung, when der Gast über den Gast-Link den Freizeitpass öffnet, ausfüllt und speichert, then liegen die Antworten verschlüsselt bei der Person des Kindes, und nach einer Konto-Übernahme sind sie im Konto vorausgefüllt.
- Given die Datenbank, when Antworten gespeichert sind, then stehen Wert und Sonstiges nur verschlüsselt (bytea) in `personen.freizeitpass_antwort`.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then sind alle Tests grün (inkl. Architektur), die drei neuen Screenshot-Paare existieren und sind verlinkt, und der Doku-Build ist fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-13 — Review pass
- verdicts: 36 findings — high 0, medium 2, low 27, false 7, maybe-false 0 (Doppel derselben Stelle und desselben Befunds teilen Verdikt und Route)
- findings:
  - Intent-Abgleich
    - `[low]` `[reject]` „Nur abgefragte Fragen“ nur im Client, Personen nimmt jede Katalogfrage an — Daten der Person über sich selbst; Personen darf Freizeiten nicht lesen (AD-2), eine Server-Grenze bräuchte Ereignis/Spine-Änderung; Sicht der Freizeit begrenzt 2-5. Kommt im Alltag nur über manipulierte Anfragen vor.
    - `[low]` `[patch]` Doku `eltern/freizeitpass.md` beschreibt die Sicht von Leitung/Team, als gäbe es sie schon — Text auf die Regel umformuliert, ohne nicht gebaute Ansicht zu versprechen.
    - `[false]` `[reject]` AD-30 „erneuertes Token“ und „Freizeit X liest Y nicht“ fehlen — Gast-Links haben kein Erneuern; das Token ist familiengebunden, der Fall „Familie A liest Kind von B“ ist getestet (`GastFreizeitpassTests`).
    - `[low]` `[reject]` Vorausfüllen nicht End-zu-End getestet — HTTP-Test mit zwei Freizeiten belegt es; ein E2E-Fall bräuchte eine zweite Demo-Freizeit.
    - `[false]` `[reject]` „Stimmt das noch?“ fehlt im Screenshot — bewusste Entscheidung (Design Notes, echte Uhr), durch HTTP- und bUnit-Tests belegt.
    - `[false]` `[reject]` Doku-Build nicht belegt — `npm run build` lief fehlerfrei (Umsetzung und erneut in der Abschlussprüfung, siehe Auto Run Result).
    - `[medium]` `[patch]` ~20 Screenshots nur mit Rendering-/Datumsrauschen verändert — per Pixelvergleich geprüft; Bilder mit < 200 abweichenden Pixeln vor dem Commit zurückgesetzt, echte Änderungen (Familie, Gast-Link, Kontakt für Absagen/Einstellungen, Vertrauensseite) bleiben.
    - `[false]` `[reject]` Zusätze außerhalb des Intents (Familie-Hinweis, Gast-Formular, Vertragsfelder) — Teil des Story-Specs (FR-17, NS-1/NS-2, EXPERIENCE Kind-Card).
  - Blind Hunter
    - `[low]` `[reject]` Auswahlgruppe zeigt bei abgeschlossener Freizeit „Inzwischen geändert“ statt Abschluss-Text — Abschluss ist bis Story 3-8 nicht über die Oberfläche erreichbar; Fix bräuchte neuen Parameter.
    - `[low]` `[reject]` Ja/Nein lässt sich nicht auf „keine Angabe“ zurücksetzen — selten nötig, Rückgängig vorhanden; Fix bräuchte zusätzliche Option.
    - `[low]` `[reject]` „Stimmt das noch?“ bleibt nach einer Änderung bis zum Neuladen sichtbar — Server bestätigt korrekt, nur Anzeige; Fix bräuchte Rückmeldekanal im Autosave-Baustein.
    - `[low]` `[reject]` Validierung vor Token-/Familienprüfung: 400 statt 404, kein Sicherheitsprotokoll bei ungültigem Token mit ungültigen Werten — Pipeline-Reihenfolge ist AD-27; 400 verrät nichts über Daten; Rate-Limit gilt. Fix bräuchte Umbau der Prüfreihenfolge.
    - `[low]` `[reject]` Tetanus-Datum in der Zukunft akzeptiert — Plausibilität, keine Datenschutz- oder Sicherheitsfolge; Fix bräuchte Uhr im Katalog.
    - `[medium]` `[patch]` Datenauskunft „Freizeitpass“ zeigt Kennungen und Rohwerte — auf Fragetexte und Optionstexte umgestellt.
    - `[low]` `[patch]` Ein fehlgeschlagenes Laden eines Passes legt die Familienübersicht lahm — Laden je Pass abgefangen, ohne Pass kein Hinweis.
    - `[low]` `[patch]` Längengrenzen doppelt definiert (Api.Contracts und Katalog) — Test bindet die Konstanten aneinander. (Fehlender 2000-Zeichen-Hinweis: Teil desselben Eintrags, nicht umgesetzt, Server meldet am Feld.)
    - `[low]` `[patch]` Link „Freizeitpass“ auch ohne abgefragte Fragen — Familie und Gast-Link zeigen ihn nur noch bei Fragen (Familie: nur bei geladenem Pass).
    - `[low]` `[patch]` Magischer Text „Die Person“ in `AnmeldungMitKonto.razor` — entfernt, unbekannter Vorname ergibt „Freizeitpass ausfüllen“.
  - Edge Case Hunter
    - `[low]` `[reject]` Abschluss-Text der Auswahlgruppe — Doppel zu Blind Hunter, gleiche Begründung.
    - `[low]` `[reject]` Ja/Nein nicht leerbar — Doppel, gleiche Begründung.
    - `[low]` `[patch]` Task.WhenAll in der Familienübersicht — Doppel, gemeinsam gepatcht.
    - `[low]` `[patch]` Link für volljähriges Kind in der Familie führt auf „nicht verfügbar“ — `IVertretung.PersonenAsync` liefert alle Kinder; Link jetzt nur bei geladenem Pass.
    - `[low]` `[reject]` Datenauskunft der Eltern enthält Antworten volljähriger, noch nicht übergebener Kinder — gleicher Umfang wie der übrige Familienteil der Auskunft (Kinder bis zur Übergabe); die Angaben stammen von den Eltern selbst.
    - `[low]` `[patch]` Fehler beim Nachladen des Vornamens zeigt nach erfolgreicher Anmeldung das Fehlerbanner — Namenssuche abgefangen.
    - `[low]` `[reject]` Gast speichert per manipuliertem POST nicht abgefragte Fragen — Doppel zum Intent-Befund, gleiche Begründung.
    - `[low]` `[reject]` Tetanus in der Zukunft — Doppel.
    - `[false]` `[reject]` Pass einer abgesagten Anmeldung per direkter URL bearbeitbar — die Antworten gehören der Person und hängen an keiner Freizeit; die Freizeit sieht nach Absage nichts (Freigabe beendet).
    - `[false]` `[reject]` Gespeicherte, später entfernte Kennungen — AD-10: Kennungen werden nie entfernt, nur neu vergeben; geschrieben wird nur Geprüftes.
    - `[false]` `[reject]` Demo-Einspielung wirft bei Phase Gelaufen — Gelaufen setzt nur der ausdrückliche Abschluss, den die Demo nie auslöst.
    - `[low]` `[reject]` Auswahl speichert beim Klick statt beim Verlassen — für Kästchen und Optionsknöpfe ist Speichern beim Klick das übliche Verhalten (wie `AutosaveAnkreuzfeld`).
  - Verification Gap
    - `[low]` `[patch]` Geleerte Antwort nach 12 Monaten nicht als veraltet / bestaetigen 404 ungetestet — Test ergänzt.
    - `[low]` `[patch]` Autosave „Sonstiges“ auf der App-Seite ungetestet — bUnit-Test ergänzt.
    - `[low]` `[patch]` Knopftext nach Selbst-Anmeldung ungetestet — bUnit-Test ergänzt.
    - `[low]` `[reject]` Abschluss-Text der Auswahlgruppe — Doppel.
    - `[low]` `[patch]` Fehlerbanner nach erfolgreicher Anmeldung — Doppel, gemeinsam gepatcht.
    - `[low]` `[patch]` `Freizeit.FragenFuer` ungenutzt, zweite Quelle für die Zuordnung — eine Quelle hergestellt.

## Design Notes

- **Antworten an der Person, nicht an der Freizeit.** Eine Zeile je (Person, Frage) macht das Vorausfüllen (FR-23) trivial und hält AD-8 („keine Kopie, Durchgriff“) ein. Personen darf Freizeiten nicht lesen (kein Pfeil); darum liefert Freizeiten die gewählten Kennungen an den Anmeldungen, und der Client setzt Fragenliste und Antworten zusammen (AD-2). Personen nimmt Antworten auf jede Katalogfrage an: es sind Daten der Person über sich selbst, die Oberfläche fragt nur das Abgefragte (NFR-7). Die Begrenzung der Sicht für Leitung und Team auf abgefragte Fragen (und damit die Fragen-Kennungen in `Freigabe`, AD-8) ist Sache von 2-5; 2-4 legt dafür nur stabile Kennungen und die Auswahl je Freizeit an.
- **Keine neuen Ereignisse.** Die Auswahl lebt in Freizeiten und wird beim Lesen mitgeliefert; damit braucht es keine Zeile in der Spine-Tabelle „Ereignis-Fluss“.
- **String-Kennungen und Komma-Listen.** Kennungen sind kebab-case-Strings (stabil, lesbar, ohne Komma). Komma-Listen passen zum vorhandenen Autosave-Dienst, der nur String-Werte kennt; der Server normalisiert, damit der Konfliktvergleich auf dem alten Wert stabil ist.
- **12 Monate** = `BestaetigtAm < jetzt.AddMonths(-12)` in UTC; keine Org-Einstellung (Spec nennt einen festen Wert).
- **Gast speichert per Formular.** Public ist statisch (AD-29); daher ein POST mit allen Antworten der Seite: nur geänderte Werte werden geschrieben (neues `BestaetigtAm`), unveränderte nur bei Haken „Stimmt noch“. Letzter Schreiber gewinnt; das ist bei einer Familie mit einem Gast-Link vertretbar.
- **„Stimmt das noch?“ im Screenshot entfällt.** Demo-Daten entstehen mit der echten Uhr; eine 12 Monate alte Bestätigung lässt sich nur mit Speziallogik erzeugen. Der Zustand ist durch HTTP-Tests (FakeTimeProvider) und bUnit-Tests belegt und in der Doku beschrieben.
- **Volljährige Kinder vor der Übergabe:** Zugriff folgt `IVertretung.DarfAnmeldenAsync` (wie die Anmeldung); keine eigene Regel.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- 0 Warnungen, 0 Fehler.
- `dotnet test --project tests/Zeltlotse.Api.Tests` (während der Umsetzung mit `-- --filter-class …`), `dotnet test --project tests/Zeltlotse.Personen.Tests`, `dotnet test --project tests/Zeltlotse.Ui.Tests`, `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- grün.
- Einmal am Ende: `dotnet test` (ganze Lösung inkl. E2E, Kategorie Doku erzeugt Screenshots) -- grün; danach in `doku/`: `npm run build` -- fehlerfrei.
- Vor dem Commit: nur Rendering-Rauschen veränderte PNGs (`app-offline-*`, `vertrauensseite-laptop`, sofern inhaltlich unverändert) per `git checkout` zurücksetzen.

## Auto Run Result

Status: done

**Umsetzung:** Fester Fragenkatalog (10 Fragen, 14 EU-Allergene) in `Personen.Contracts`; Antworten je Person und Frage verschlüsselt in `personen.freizeitpass_antwort` mit Autosave (AD-19), „Stimmt“ und serverseitig berechnetem „veraltet“ (12 Monate); Gast-Formular über den Gast-Link; Fragen-Auswahl je Zielgruppe an der Freizeit (`GET/PATCH /api/freizeiten/{id}/fragen`, nur Leitung) und mitgeliefert an Anmeldungen, Gast-Link und Vertrauensseite; Löschen und Datenauskunft; App-Seite Freizeitpass, Card in den Einstellungen, Einstiege in Familienübersicht und nach der Anmeldung; Demo-Daten, drei neue Screenshot-Paare, zwei neue Doku-Seiten plus Ergänzungen.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs` – Katalog, Prüfen, Normalisieren.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/*` – Entität, Commands, Endpunkte; Migration `Freizeitpass`.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/{PersonLoeschen,PersonenAuskunft}.cs` – Löschen, lesbare Auskunft.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{Domaene/Freizeit.cs,Fragen/*,Anmeldungen/AnmeldungenLesen.cs,Freizeitseite/OeffentlicheEndpunkte.cs}` – Auswahl, Lesepfade; Migration `FragenAuswahl`.
- `src/Zeltlotse.Api.Contracts/{Personen/Freizeitpass.cs,Freizeiten/Fragen.cs,…}` – additive Verträge.
- `src/Zeltlotse.Ui.Komponenten/Auswahlgruppe.razor`, `src/Zeltlotse.Ui/Autosave/AutosaveAuswahlgruppe.razor`, API-Clients.
- `src/Zeltlotse.App/Features/{Personen/Freizeitpass.razor,Personen/FamilieAnsicht.razor,Freizeiten/Einstellungen.razor,Freizeiten/AnmeldungMitKonto.razor}`; `src/Zeltlotse.Public/Seiten/{GastFreizeitpass,GastLink,Vertrauensseite}.razor`.
- `src/Zeltlotse.Api/Demo/*`, Tests (Api, Personen, Ui, E2E, Architektur), `doku/…` (neu `eltern/freizeitpass.md`, `leitung/freizeitpass-fragen.md`).

**Review:** 36 Befunde (high 0, medium 2, low 27, false 7). Gepatcht: lesbare Datenauskunft, Screenshot-Rauschen zurückgesetzt, robuste Familienübersicht (Laden je Pass, Link nur mit Fragen und zugänglichem Pass), Link auf dem Gast-Link nur mit Fragen, Namenssuche nach der Anmeldung abgefangen (ohne magischen Text), ungenutztes `FragenFuer` entfernt, Doku-Absatz „Wer die Angaben sieht“, neue Tests (geleerte Antwort, Sonstiges-Autosave, Knopftext bei Selbst-Anmeldung, Längenkonstanten). Nichts zurückgestellt. Abgelehnt mit Begründung im Triage Log: u. a. Server-Grenze „nur abgefragte Fragen“ (AD-2, Sicht kommt mit 2-5), Abschluss-Text der Auswahlgruppe (Abschluss erst ab 3-8 erreichbar), Ja/Nein nicht leerbar, „Stimmt das noch?“ bleibt bis Neuladen, 400 vor 404 durch Pipeline-Reihenfolge, Tetanus in der Zukunft, Auskunft volljähriger Kinder vor Übergabe.

**Follow-up-Review:** `false`. Gepatcht wurden 2 mittlere Befunde (Datenauskunft, Screenshots) und 11 geringe; beide mittleren sind durch Tests bzw. Pixelvergleich abgesichert, ein konkretes ungeprüftes Risiko bleibt nicht.

**Prüfung:** `dotnet build Zeltlotse.slnx --no-incremental` 0 Warnungen/0 Fehler; gezielte Läufe (Api 26 neue Tests, Ui, Personen, Architektur 133) grün; nach den Patches voller Lauf `dotnet test` 1532/1532 grün inkl. E2E und Doku-Screenshots; `npm run build` in `doku/` fehlerfrei (nur die bekannte Sitemap-Warnung). Screenshots mit < 300 abweichenden Pixeln (Datum/Rendering) per `git checkout` zurückgesetzt; echte Änderungen: Familie, Gast-Link, Kontakt für Absagen (Einstellungen), Vertrauensseite, sechs neue Bilder.

**Restrisiken:** Personen nimmt Antworten auf jede Katalogfrage an (nur die Oberfläche begrenzt); Begrenzung der Freizeit-Sicht auf abgefragte Fragen und Fragen-Kennungen in `Freigabe` sind Aufgabe von 2-5. „Stimmt das noch?“ hat keinen Screenshot (echte Uhr der Demo-Daten).
