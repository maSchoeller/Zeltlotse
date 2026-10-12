---
title: 'Freizeitseite und Vertrauensseite'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: '6a9c76225d3dde35f6282ebaf86c6ce5e52ce2b9'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Die Zugriffsbegrenzung der öffentlichen Freizeit- und Vertrauensseite (20 Aufrufe je Minute und Besucher-IP) könnte bei vielen Eltern hinter einer gemeinsamen IP greifen.
    evidence: |-
      Die öffentlichen GETs nutzen dieselbe Richtlinie wie die Login-Endpunkte (Zugriffsbegrenzung.cs, Partition je Besucher-IP, Standard 20/min). Ein Aufruf von Freizeitseite und Vertrauensseite kostet 2 Anfragen. Bei einem Elternabend im Gemeindehaus-WLAN oder hinter Carrier-NAT teilen sich viele Eltern eine IP. Die Intent-Regel verlangt `.Zugriffsbegrenzt()` ausdrücklich; eine eigene, großzügigere Richtlinie wäre eine Architekturentscheidung. Klären würde es: reale Zugriffsmuster (Aufrufe je IP und Minute beim Teilen eines Links) oder eine Entscheidung zu einer eigenen Richtlinie für anonyme Ansichten (AD-25).
    location: >-
      src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs:36
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Eine Freizeit hat noch keine öffentliche Seite. Eltern sehen weder Beschreibung noch Team noch „ausgebucht“, und es gibt keine Vertrauensseite, die in Klartext erklärt, welche Daten erhoben werden, wer sie sieht und wann sie verschwinden (CAP-7, FR-15, FR-66, FR-67).

**Approach:** Jede Freizeit bekommt ihre zwei Anmeldelinks (AD-11) als dauerhaft teilbare Tokens. Ein Anmeldelink öffnet die öffentliche Freizeitseite unter `/freizeit/{token}` und die Vertrauensseite unter `/freizeit/{token}/vertrauensseite`. Beide Seiten werden statisch auf dem Server gerendert (Zeltlotse.Public) und holen ihre Daten über zwei anonyme, zugriffsbegrenzte API-Endpunkte. Die Leitung pflegt Beschreibung, Ort und Höchstzahl Teilnehmende auf der neuen Seite „Einstellungen“ der Freizeit (Autosave). Dort sieht sie auch die Anmeldelinks und kann sie erneuern. Jede Person trägt ihre „Herkunft“ im Konto ein.

## Boundaries & Constraints

**Always:**
- Testgetrieben: Jeder neue Endpunkt bekommt zuerst HTTP-Tests gegen echtes PostgreSQL mit den Pflichtfällen nach AD-30.
  - Freizeit-Endpunkte: berechtigt, Team → 403, fremde Organisation oder ohne Rolle → 404.
  - Token-Endpunkte: gültig; abgelaufen (nach Freizeitende) oder erneuert → 404; Token mit anderem Zweck (z. B. Leitungs-Einladung) → 404; Token von X liest nur X.
- Tokens nach `Zugangslink` (32 Zufallsbytes, base64url):
  - Gespeichert wird der SHA-256-Hash mit Zweck `anmeldelink-teilnehmende` bzw. `anmeldelink-mitarbeitende`.
  - Zusätzlich liegt das Token verschlüsselt vor, damit es sich wieder anzeigen lässt. Die Verschlüsselung ist AES-256-GCM mit einem per HKDF aus dem Hauptschlüssel abgeleiteten Schlüssel; die Freizeit-Kennung und die Art stehen in den Zusatzdaten.
  - Gültig bis zum Freizeitende (`Freizeiten.Contracts`).
  - Erneuern macht das alte Token sofort ungültig.
- Anonyme Endpunkte: `.AllowAnonymous().Zugriffsbegrenzt()`. Den Mandanten setzen sie aus der über das Token gefundenen Freizeit (AD-6). Nie aus einer Angabe des Clients.
- Öffentliche Antworten enthalten vom Team nur Vorname, Initialen, Rolle und Herkunft. Kein Nachname, keine Mail, keine PersonId.
- Zeltlotse.Public ruft nur die API über `PublicApiTransport` und die typisierten Clients in Zeltlotse.Ui. Es verwendet nur statische Bausteine, kein Skript, kein Inline-`style`.
- Neue Darstellungsbausteine (Avatar-Personenliste) entstehen in `Zeltlotse.Ui.Komponenten` und bekommen einen bUnit-Test.
- Texte: Deutsch ohne Du/Sie, Fachbegriffe laut Glossar. „Freigabefenster“ und „Freizeitpass“ werden auf der Vertrauensseite beim ersten Auftreten erklärt.
- Die Vertrauensseite entsteht nur aus gespeicherten Einstellungen:
  - aus der Freizeit: Name, Daten, `FreigabeIntervallTage` und „Freigabe bis“;
  - aus der Organisation: Name, Löschfristen und Pflicht-Nachweise.
  Sie verspricht nichts, was das System nicht tut.
- Fertig erst mit Doku-Seiten, Demo-Daten, Screenshot-Tests (Handy und Laptop, mit axe) und fehlerfreiem Doku-Build (NFR-20).

**Never:**
- Kein Anmeldeformular, kein „Anmelden“-Knopf, keine Anmeldung-Entität. Das kommt in 2-2, die Bestätigung in 2-3.
- Keine Profilfotos, keine Organisation-Logos, keine öffentlichen Dokumente. Dokumente kommen mit FR-43 in V3.
- Keine Fragen-Auswahl und kein Fragenkatalog. Das kommt in 2-4; die Vertrauensseite nennt den Freizeitpass dort nur allgemein.
- Kein Feld für die Höchstzahl Mitarbeitender.
- Keine Pop-ups, kein Kopieren per JavaScript in Public.
- Keine Ausnahme vom Mandantenfilter außer dem token-gebundenen Laden der Freizeit, wie bei `LeitungAnnehmen`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Freizeitseite | GET `/freizeit/{gültiges TN-Token}` | Name der Organisation oben. Kicker „Freizeit · 2. bis 13. August 2027“, H1 Name, Ort, Beschreibung. „Das Team“ mit Avatar (Initialen), Vorname, „Leitung · aus Musterstadt“. Link „Vertrauensseite“. | – |
| Ausgebucht | Höchstzahl gesetzt, bestätigte Teilnehmende ≥ Höchstzahl, TN-Token | Status „Ausgebucht“ (StatusArt.Offen) und der Satz „Anmelden ist trotzdem möglich, eine Zusage gibt es erst, wenn ein Platz frei wird.“ | – |
| Ausgebucht, Mitarbeitende | dieselbe Lage, MA-Token | kein „Ausgebucht“ (FR-16: Mitarbeitende können immer dazukommen) | – |
| Ohne Höchstzahl | Höchstzahl leer | nie ausgebucht | – |
| Ungültiger Link | unbekanntes, erneuertes, abgelaufenes oder fremdes Token | API 404. Public-Seite mit Status 404 und dem Text „Dieser Anmeldelink ist nicht mehr gültig.“ | kein Sicherheitsprotokoll-Eintrag (nur Ansicht, keine Einlösung) |
| Vertrauensseite | GET `/freizeit/{token}/vertrauensseite` | Abschnitte „Welche Daten erhoben werden“, „Wer die Daten sieht“, „Wann die Daten verschwinden“ mit konkreten Werten: Pflicht-Nachweise der Org, Freigabe-Datum, Intervall in Tagen, Löschfristen in Monaten. Link zurück zur Freizeitseite. | – |
| Einstellung ändert Vertrauensseite | Org-Admin ändert die Löschfrist von 12 auf 24 | Vertrauensseite zeigt „24 Monate“ | – |
| Autosave | PATCH `{version, alterWert, beschreibung}` | 200 mit neuer Version. Bei gleichem Feld mit abweichendem altem Wert: 409. | Validierung: Beschreibung ≤ 2000, Ort ≤ 100, Höchstzahl leer oder 1–1000 → 400 mit Feldfehler |
| Erneuern | POST erneuern, Art Teilnehmende | neuer Link wird angezeigt, der alte gibt 404 | Freizeit gelaufen → Schreibschutz der Pipeline |
| Herkunft | PATCH Profil `herkunft` | gespeichert, steht in der Datenauskunft, erscheint auf der Freizeitseite | > 60 Zeichen → 400 |

</intent-contract>

## Code Map

- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/Freizeit.cs` -- Aggregat ohne `Beschreibung`, `Ort`, `HoechstzahlTeilnehmende` und ohne `Version`. `FreizeitRolle` mit Leitung oder Team. `Phase()` und `Zeitpunkte()` sind vorhanden.
- `.../Domaene/FreizeitZeiten.cs:361-369` -- `Freizeitende`, `FreigabeGueltigBis`, `Phase`. Das sind die einzigen Zeitquellen.
- `.../Domaene/LeitungsEinladung.cs`, `.../Leitung/LeitungAnnehmen.cs:71-76` -- Vorbild für Token-Hash, das Suchen per Hash mit `IgnoreQueryFilters` und den Mandanten aus dem Token.
- `.../Verwaltung/FreizeitAnlegen.cs:122-170` -- Command, Validator und Handler. Hier entstehen künftig beide Anmeldelinks.
- `.../Verwaltung/FreizeitenVerwaltungEndpunkte.cs:313` -- `LeitungNamen.Async`, das Muster für Personennamen über `IPersonenNamen`.
- `.../Startseite/FreizeitEndpunkte.cs:140-161` -- Kontext-Endpunkt. Ein neuer Aktionsschlüssel gehört in `Erlaubt`.
- `.../Rechte/FreizeitRechte.cs` -- Rollenlisten. Neu ist `Einstellungen` mit nur Leitung.
- `.../FreizeitenModul.cs:46-65` -- `MapFreizeitenEndpoints`.
- `.../Daten/FreizeitenDbContext.cs:55-91` -- Modell. Neu sind die Tabelle `anmeldelink` und `Version` an `freizeit`. Migration mit der Design-Time-Fabrik.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Datenschutz/LoeschfristenEndpunkte.cs:38-160` -- **Vorbild für Autosave**: PATCH-Körper lesen, `FeldAendern`, Validator, 409.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/NachweisRichtlinie.cs:118-121` -- Pflicht-Nachweise und Vorgaben.
- `src/Modules/Organisationen/Zeltlotse.Organisationen.Contracts/` -- neue Lese-Schnittstelle für eine Organisation (Name, Löschfristen, Pflicht-Nachweisarten), mandantengefiltert. Registriert in `OrganisationenModul.cs:30-41`.
- `src/Modules/Personen/Zeltlotse.Personen/Profil/Person.cs`, `src/Zeltlotse.Api.Contracts/Personen/Profil.cs` -- `ProfilFeld` um `Herkunft` erweitern. Auch die Datenauskunft von Personen ergänzen.
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Namen/IPersonenNamen.cs` -- daneben eine neue Schnittstelle für Steckbriefe (Vorname, Nachname, Herkunft) je Menge von PersonIds.
- `src/Zeltlotse.SharedKernel/Zugangslinks/Zugangslink.cs`, `.../Datenschutz/Hauptschluessel.cs` -- Token-Bausteine. Neu ist ein Dienst, der Links mit dem Hauptschlüssel verschlüsselt (HKDF, Zweck „zugangslink“), registriert im API-Host.
- `src/Zeltlotse.SharedKernel/Datenschutz/PfadBereinigung.cs` -- Token-Segmente werden in Telemetrie und Browserfehlern schon geschwärzt. Die Route-Vorlage enthält keine Tokens.
- `src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs`, `src/Zeltlotse.SharedKernel/Web/Zugriffsbegrenzung.cs` -- `.Zugriffsbegrenzt()`.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeiten.cs`, `.../Gemeinsam/FreizeitKontext.cs:60-80` -- neue DTOs `Freizeitseite`, `Vertrauensseite`, `FreizeitseiteEinstellungen` und das Enum `FreizeitseiteFeld`. Aktionsschlüssel `FreizeitEinstellungen = "freizeit-einstellungen"`. Pflicht-Nachweise als deutsche Bezeichnungen (`string`), weil Freizeiten den Ordner Organisationen nicht nutzen darf.
- `src/Zeltlotse.Public/Seiten/`, `Layout/OeffentlichesLayout.razor`, `PublicSeiten.cs:72` -- neue Seiten `Freizeitseite.razor` und `Vertrauensseite.razor`. `OeffentlicherRahmen` bekommt den Organisationsnamen. `NichtVerfuegbar.razor` ist das Muster für eine 404 über das kaskadierte `HttpContext`.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs`, `UiRegistrierung.cs:19-25` -- Client-Methoden für die öffentlichen und die Einstellungs-Endpunkte.
- `src/Zeltlotse.Ui.Komponenten/` -- es gibt noch keine Avatar-Komponente. Die CSS-Klassen `elkw-people` und `elkw-avatar` stehen in `wwwroot/css/elkw-components.css:263-268`. `Status` und `Meldung` sind vorhanden.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` -- bekommt den Zugang zur neuen Seite `Einstellungen.razor` für die Leitung.
- `src/Zeltlotse.App/Features/Konten/Konto.razor` -- Profil-Autosave. Hier kommt das Feld „Herkunft“ hinzu.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs:16-20,147`, `DemoDaten.cs` -- Demo-Daten:
  - Die Demo-Freizeit hat einen Ort, der noch nicht gespeichert wird. Ergänzen: Beschreibung, Höchstzahl 40, Herkunft „Musterstadt“ für Jonas und Miriam.
  - Nur über Commands, idempotent.
  - Die Zähler in `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` anpassen.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs:21-29,71,135-190` -- Listen erweitern:
  - Die zwei neuen anonymen GETs kommen in `Anonym`, zusammen mit dem Nachweis, dass sie zugriffsbegrenzt sind.
  - Neue Routen mit `{freizeitId:guid}` kommen in die Freizeit-Liste.
  - Public-Routen dürfen den Parameter nicht `freizeitId` nennen.
- `tests/Zeltlotse.Api.Tests/{FreizeitKontextTests.cs:109-135,LeitungEinladungTests.cs:41-142,ClientAuslieferungTests.cs:43-90}`, `AnmeldeHelfer`, `OrgHelfer`, `FreizeitHelfer` -- Testmuster für 403/404, Token-Fälle und statisches HTML. Eine Team-Rolle für den 403-Fall per SQL-Insert in `freizeiten.freizeit_rolle` anlegen (`AnmeldeHelfer.AusfuehrenAsync`).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs:22-43,121-133,585-626`, `E2EHilfe.cs` -- Screenshot-Muster, `BildAsync`, `AxeAsync`, Login als Jonas.
- `doku/astro.config.mjs`, `doku/src/content/docs/{eltern,leitung,daten.md}`, `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` -- Doku-Struktur und `PflichtSeiten`.

## Tasks & Acceptance

**Execution:**
- `src/Zeltlotse.SharedKernel/Zugangslinks/` -- Dienst `ILinkSchutz` (Schützen und Lesen, AES-GCM mit HKDF-Ableitung, Zusatzdaten Bezug und Zweck) mit Unit-Tests in `tests/Zeltlotse.SharedKernel.Tests`. Registrierung im API-Host. -- Dauerhafte Links lassen sich verschlüsselt wieder anzeigen (AD-11).
- `tests/Zeltlotse.Api.Tests/FreizeitseiteTests.cs` (neu, zuerst) -- HTTP-Tests für alle Zeilen der Matrix: Pflichtfälle nach AD-30 für Einstellungen laden, PATCH und Erneuern sowie die Token-Fälle für beide öffentlichen GETs (mit `FakeTimeProvider` hinter das Freizeitende). Die Vertrauensseite ändert sich mit Löschfrist und Nachweis-Richtlinie. Das Public-HTML enthält die Inhalte, aber kein `<script` und kein `style=`. Ein ungültiges Token gibt eine 404-Seite mit dem Text. -- Testgetrieben.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/` -- `Freizeit` bekommt `Beschreibung`, `Ort`, `HoechstzahlTeilnehmende`, `Version` und `FeldAendern`. Neue Entität `Anmeldelink` (FreizeitId, OrganisationId, Art, Hash, verschlüsseltes Token, `Erneuern`). Beide Links entstehen in `FreizeitAnlegen`. Neue Slices `Freizeitseite/`:
  - Leitung: GET und PATCH `/api/freizeiten/{freizeitId}/freizeitseite`, POST `/api/freizeiten/{freizeitId}/anmeldelinks/{art}/erneuern`, alle mit `.NurFreizeitRolle(FreizeitRechte.Einstellungen)`.
  - Anonym: GET `/api/freizeiten/oeffentlich/{token}` und `/api/freizeiten/oeffentlich/{token}/vertrauensseite`.
  - Interne Schnittstelle `IBestaetigteTeilnehmende` mit der Umsetzung „0“ bis Story 2-3.
  - Migration.
  -- Kern der Story.
- `tests/Zeltlotse.Freizeiten.Tests/` -- Unit-Tests für Platzlage und Ausgebucht. Ein HTTP-Test mit ersetztem `IBestaetigteTeilnehmende` prüft „Ausgebucht“ in API und Public-HTML, für Teilnehmende ja und für Mitarbeitende nein. -- Ausgebucht ist ohne Anmeldungen sonst nicht prüfbar.
- `src/Modules/Organisationen/...` -- Contracts-Lesen „Organisation für die Vertrauensseite“ mit Tests. -- Datenquelle der Vertrauensseite.
- `src/Modules/Personen/...` -- Profilfeld `Herkunft` mit Migration, Validierung ≤ 60 Zeichen und Datenauskunft. Contracts-Steckbrief mit Tests. Der Test für das Profil-PATCH wird erweitert. -- Herkunft im Team (FR-15).
- `src/Zeltlotse.Api.Contracts/`, `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs` -- DTOs, Enum, Aktionsschlüssel und Client-Methoden. -- API-Verträge (AD-5).
- `src/Zeltlotse.Ui.Komponenten/PersonenListe.razor` (+ bUnit) -- statische Liste mit Avatar (Initialen, `aria-hidden`), Name und Zeile. -- Baustein „Avatar“ (AD-29).
- `src/Zeltlotse.Public/Seiten/Freizeitseite.razor`, `Vertrauensseite.razor` -- Seiten nach Matrix und Mock `key-gast-anmeldung.html:77-125,214-235`, ohne Anmelden-Knopf und Dokumente. `<PageTitle>` setzen. -- Öffentliche Oberfläche.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` (+ Zugang auf `FreizeitStart.razor` nur mit dem Aktionsschlüssel) -- Abschnitt „Freizeitseite“ mit Autosave-Feldern Beschreibung (mehrzeilig), Ort und Höchstzahl Teilnehmende. Abschnitt „Anmeldelinks“ mit beiden Links als Text, „Freizeitseite öffnen“ und „Neuen Link erzeugen“ über die Inline-Bestätigung („Der bisherige Link wird ungültig.“). Leer-Zustand und Fehlerzustände nach EXPERIENCE. -- Pflege durch die Leitung.
- `src/Zeltlotse.App/Features/Konten/Konto.razor` -- Autosave-Feld „Herkunft (Wohnort)“ mit Hinweis „Erscheint auf den Freizeitseiten, in denen die Person mitarbeitet.“
- `src/Zeltlotse.Api/Demo/` und `DemoDatenTests.cs` -- Demo-Werte wie in der Code Map.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- Listen und Zähler anpassen.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories für Handy und Laptop, jeweils mit axe:
  - `Freizeit_Einstellungen`: Jonas angemeldet, Einstellungen öffnen, Bild `freizeit-einstellungen`.
  - `Freizeitseite`: den Teilnehmenden-Link aus den Einstellungen lesen, öffnen, Bild `freizeitseite`.
  - `Vertrauensseite`: Bild `vertrauensseite`.
  - Kein Erneuern im Test, damit die Demo-Daten unverändert bleiben.
- `doku/src/content/docs/eltern/freizeitseite.md`, `leitung/freizeitseite-einrichten.md`, `daten.md`, Rollen-Index-Seiten, `DokuRegeln.PflichtSeiten` -- Doku-Seiten mit den Screenshots. Die Eltern-Seite beschreibt „ausgebucht“ als Text. Die Leitungs-Seite erklärt Felder, Links und das Erneuern. `daten.md` verweist auf die Vertrauensseite.

**Acceptance Criteria:**
- Given die Demo-Daten, when Jonas die Einstellungen der Freizeit öffnet und den Teilnehmenden-Link aufruft, then zeigt die Freizeitseite ohne Login Organisation, Zeitraum, Ort, Beschreibung, Jonas und Miriam als „Leitung · aus Musterstadt“ und einen Link zur Vertrauensseite.
- Given eine Organisation mit Pflicht-Nachweis Führungszeugnis und 12 Monaten Löschfrist, when die Vertrauensseite geladen wird, then nennt sie Führungszeugnis, „12 Monate“ und das Datum „Freigabe bis“ der Freizeit. Nach Änderung der Einstellungen zeigt sie die neuen Werte.
- Given ein Team-Mitglied der Freizeit, when es Einstellungen lädt, ändert oder einen Link erneuert, then antwortet die API mit 403, und in der App fehlt der Zugang.
- Given `dotnet test` und `npm run build` in `doku/`, when beide laufen, then sind sie fehlerfrei, und die sechs neuen Screenshots existieren und sind verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 30 findings — high 0, medium 4, low 14, false 12, maybe-false 0
- findings:
  - `[false]` `[reject]` (Intent) „Ausgebucht“ kann in Produktion noch nicht erscheinen, nur mit Testersatz geprüft — Design Notes: Bestätigungen gibt es erst ab 2-3; Anzeige, Regel und Höchstzahl sind gebaut und über HTTP mit ersetzter Zählung geprüft.
  - `[false]` `[reject]` (Intent) „Bild“ als Initialen statt Foto — EXPERIENCE, Baustein Avatar: „Initialen“; Mock zeigt Initialen.
  - `[false]` `[reject]` (Intent) „Welche Daten erhoben werden“ ist fester Text — Es gibt noch keine Fragen-Auswahl (2-4); der Satz gibt die feste Regel NFR-7/FR-22 wieder, die Vertrauensseite verspricht nichts darüber hinaus.
  - `[false]` `[reject]` (Intent) Öffentlich nur über Token statt fester Adresse — EXPERIENCE: Freizeitseite „erreicht über Anmeldelink“; AD-11.
  - `[false]` `[reject]` (Intent) Anmeldelinks werden schon in 2-1 gebaut — nötig, damit die Seite erreichbar ist; Entscheidung in Design Notes.
  - `[false]` `[reject]` (Intent) 429 nicht per HTTP an den neuen Endpunkten geprüft — Die Richtlinie „zugriffsbegrenzt“ ist in WebSicherheitTests mit echtem 429 geprüft; EndpunktListeTests prüft, dass beide GETs sie tragen.
  - `[false]` `[reject]` (Intent) Doku-Build nicht belegt — `npm run build` in doku/ lief mit Exit 0 (25 Seiten).
  - `[medium]` `[patch]` (Verification) Zweig „Link fehlt → erzeugen“ in AnmeldelinkErneuernHandler ungetestet — Test ergänzt (Link per SQL gelöscht, erzeugen, öffentlich öffnen).
  - `[low]` `[patch]` (Verification) Abfangen von CryptographicException beim Laden der Einstellungen ungetestet — Test ergänzt (Chiffretext verfälscht).
  - `[medium]` `[patch]` (Verification) Filter „ohne Vorname fehlt“ im öffentlichen Team ungetestet — Test ergänzt (Rolle für namenlose Person per SQL).
  - `[false]` `[reject]` (Blind) PATCH-Version wird nicht geprüft — Gleiches Muster wie Löschfristen (AD-19): Konflikt nur bei abweichendem altem Wert desselben Felds; die Version wird zurückgegeben.
  - `[low]` `[patch]` (Blind) Abgeschlossene Freizeit: 409 erscheint als „Inzwischen geändert“ — KonfliktText an den Autosave-Feldern gesetzt.
  - `[low]` `[reject]` (Blind) Links bleiben nach Freizeitende sichtbar und erneuerbar — In der Nachbereitung teilt niemand mehr Anmeldelinks; ein Zustand „abgelaufen“ wäre ein neuer Zweig.
  - `[low]` `[reject]` (Blind) „Noch kein Link“ obwohl alter Link nach Wechsel des Hauptschlüssels noch gilt — Nur nach Schlüsselwechsel (Rotation zurückgestellt); Lösung bräuchte ein neues Feld.
  - `[low]` `[patch]` (Blind) Zustand „Noch kein Link / Link erzeugen“ fehlt in der Doku — Satz in leitung/freizeitseite-einrichten.md ergänzt.
  - `[false]` `[reject]` (Blind) Doku beschreibt „Ausgebucht“ als funktionierend — Die beschriebene Regel stimmt; ohne bestätigte Teilnehmende ist die Bedingung schlicht nicht erfüllt.
  - `[false]` `[reject]` (Blind) Vertrauensseite enthält Sätze ohne Einstellung — „keine Kopie“ gilt nach AD-9, „nur ausgewählte Fragen“ nach NFR-7; beides ist Systemregel, kein Versprechen darüber hinaus.
  - `[low]` `[patch]` (Blind) Initiale des Nachnamens trotz „Nachnamen stehen dort nicht“; Hinweistext zur Herkunft holprig — Doku-Texte und Hinweis angepasst.
  - `[low]` `[patch]` (Blind) Ungrammatischer Satz zur Zugriffsbegrenzung in daten.md — umformuliert.
  - `[low]` `[reject]` (Blind) Kein Knopf „Link kopieren“ — neue Funktion; der Link steht als markierbarer Text da, wie die Doku sagt.
  - `[low]` `[reject]` (Blind) Erfolgsmeldung nach dem Erneuern weit oben, ohne Fokus — Meldung hat role="status" und wird angesagt; Fokuslenkung fehlt app-weit gleich, eigene JS-Lösung wäre mehr als eine direkte Korrektur.
  - `[false]` `[reject]` (Blind) Höchstzahl „05“ gegen „5“ als falscher Konflikt — Der Client schickt den gespeicherten Wert als alten Wert zurück; so ein Unterschied entsteht im Ablauf nicht.
  - `[low]` `[patch]` (Blind) Initialen brechen bei Surrogatpaaren — StringInfo-Textelement verwendet.
  - `[low]` `[patch]` (Edge) 409 bei abgeschlossener Freizeit falsch gemeldet — gleiche Ursache und Korrektur wie oben (KonfliktText).
  - `[low]` `[reject]` (Edge) „Link erzeugen“ ersetzt ohne Rückfrage einen gültigen Link nach Schlüsselwechsel — siehe oben; nur nach Schlüsselwechsel, als low abgelehnt (gleiche Ursache).
  - `[medium]` `[patch]` (Edge) 429 der API wird auf der Freizeitseite zur 500-Seite — Hinweis mit Status 429 statt Ausnahme, Test ergänzt.
  - `[medium]` `[patch]` (Edge) dasselbe auf der Vertrauensseite — gleiche Korrektur.
  - `[low]` `[patch]` (Edge) Erneuern doppelt bestätigbar — Abbruch, solange eine Übertragung läuft.
  - `[low]` `[patch]` (Edge) Initialen mit Surrogatpaar — gleiche Korrektur wie oben.
  - `[false]` `[reject]` (Edge) Demo-Einspielen bricht bei abgeschlossener Demo-Freizeit ab — Es gibt keinen Code, der AbgeschlossenAm setzt; die Demo-Freizeit liegt 2027.

### 2026-10-12 — Review pass
- verdicts: 27 findings — high 0, medium 0, low 11, false 15, maybe-false 1
- findings:
  - `[low]` `[patch]` (Verification) Abgeschlossener Zustand der Einstellungen-Seite in keinem bUnit-Test gerendert — zwei Tests ergänzt: Phase „Gelaufen“ zeigt den Hinweis, ein 409 beim PATCH zeigt den Text statt „Inzwischen geändert“, ein 409 beim Erneuern zeigt den Hinweis statt „nicht verfügbar“.
  - `[low]` `[patch]` (Verification) Aufteilen der Beschreibung in Absätze auf der Freizeitseite ungeprüft — HTML-Test mit zweizeiliger Beschreibung und Leerzeile: zwei `<p>`, kein leeres.
  - `[false]` `[reject]` (Intent) carried „Ausgebucht“ nur mit Testersatz geprüft — Design Notes: Bestätigungen gibt es erst ab 2-3. Regel und Anzeige sind über HTTP mit ersetzter Zählung geprüft.
  - `[low]` `[patch]` (Intent) „Vertrauensseite zeigt 24 Monate“ nur im JSON geprüft, nicht im HTML — Der Test lädt nach der Änderung die Seite und prüft „24 Monate nach dem“ und „Erste Hilfe“.
  - `[false]` `[reject]` (Intent) carried Vertrauensseite verspricht Verhalten späterer Stories — Die Sätze geben Systemregeln wieder (NFR-7, AD-9), siehe vorherigen Pass.
  - `[false]` `[reject]` (Intent) 409 aus dem Handler statt „Schreibschutz der Pipeline“ — Einen Pipeline-Schreibschutz gibt es noch nicht, AD-17 ist nur in den Handlern umgesetzt. Die 409 tritt ein und ist über HTTP getestet. Das Restrisiko ist bereits vermerkt.
  - `[false]` `[reject]` (Intent) 404-Seite im HTML nur mit unbekanntem Token geprüft — Beide Seiten führen jede Null-Antwort der API durch denselben `NotFound()`-Zweig. Alle Token-Fälle sind an der API geprüft.
  - `[false]` `[reject]` (Intent) carried Doku-Build nicht belegt — `npm run build` in doku/ lief auch in diesem Pass mit Exit 0.
  - `[low]` `[reject]` (Edge) Zahl-Eingabe mit ungültigem Text (badInput) leert die Höchstzahl — Chrome lässt nur Ziffern und e/+/- zu, das kommt im Alltag selten vor. Das Speichern ist an „Gespeichert · Rückgängig“ sichtbar. Die Lösung bräuchte einen neuen Parameter (inputmode) am Feld-Baustein.
  - `[false]` `[reject]` (Edge) carried Höchstzahl „040“ gegen „40“ als falscher Konflikt — Der Client schickt den gespeicherten Wert zurück, so ein Unterschied entsteht im Ablauf nicht.
  - `[false]` `[reject]` (Edge) carried PATCH-Version wird nicht geprüft — Muster nach AD-19 wie bei den Löschfristen.
  - `[false]` `[reject]` (Edge) NotFoundPage zeigt bei jeder 404 „Anmeldelink nicht mehr gültig“ — Nur Freizeitseite und Vertrauensseite rufen `NotFound()` auf. Unbekannte Adressen erreichen den Router nicht: MapRazorComponents bildet nur @page-Routen ab, und es gibt kein Re-Execute.
  - `[false]` `[reject]` (Edge) Hinweistext zur Herkunft weicht vom Wortlaut der Spec ab — Bewusst im vorherigen Pass umformuliert (Zeile „Hinweistext zur Herkunft holprig“). Die Bedeutung ist gleich, es entsteht kein Schaden.
  - `[low]` `[reject]` (Blind) Felder einer abgeschlossenen Freizeit bleiben bearbeitbar — Oben steht der Hinweis „abgeschlossen und nur lesbar“, ein Speicherversuch zeigt denselben Text. Sperren bräuchte einen neuen Parameter am Autosave-Baustein.
  - `[low]` `[reject]` (Blind) carried Links nach Freizeitende sichtbar und erneuerbar — siehe vorherigen Pass. Eine Freizeit vor ihrem Ende abschließen ist im Code nicht möglich, weil nichts AbgeschlossenAm setzt.
  - `[low]` `[reject]` (Blind) carried Unlesbarer Link erscheint als „Noch kein Link“ — tritt nur nach einem Schlüsselwechsel auf, siehe vorherigen Pass.
  - `[false]` `[reject]` (Blind) Öffentliche Seite unterscheidet den Link für Mitarbeitende nicht — Design Notes: Beide Arten öffnen dieselbe Freizeitseite, die Matrix unterscheidet nur bei „Ausgebucht“.
  - `[low]` `[patch]` (Blind) Token-Seiten ohne Schutz vor Suchmaschinen — `<meta name="robots" content="noindex">` steht jetzt auf Freizeitseite und Vertrauensseite und ist im HTML-Test geprüft. Der Teil zum Cache trifft nicht zu: Die Seiten haben weder Last-Modified noch ETag, deshalb cachen Browser sie nicht heuristisch.
  - `[maybe-false]` `[defer]` (Blind) Zugriffsbegrenzung von 20 Aufrufen je Minute und IP zu streng für breit geteilte Links — Der Intent verlangt `.Zugriffsbegrenzt()`. Ob Eltern hinter einer gemeinsamen IP die Grenze erreichen, klären nur reale Zugriffsmuster. Zurückgestellt als medium (unverified).
  - `[false]` `[reject]` (Blind) Herkunft ohne Wahl je Freizeit, Hinweis in dritter Person — Texte ohne Du/Sie sind Vorgabe. Die Herkunft ist freiwillig, und der Hinweis nennt, wo sie erscheint.
  - `[false]` `[reject]` (Blind) carried Version im PATCH ungenutzt — siehe oben.
  - `[false]` `[reject]` (Blind) carried Höchstzahl als Text verglichen — siehe oben.
  - `[low]` `[reject]` (Blind) Erneuern hinterlässt keine Spur (wer, wann) — wäre eine neue Funktion (Protokoll oder Anzeige „erzeugt am“), die der Intent nicht verlangt.
  - `[false]` `[reject]` (Blind) carried Feste Versprechen auf der Vertrauensseite — siehe oben.
  - `[false]` `[reject]` (Blind) Sortierung mit CurrentCulture — Das ist dieselbe Konvention wie im übrigen Code. Die Reihenfolge ist je Server stabil und alphabetisch, Nutzende sehen kein falsches Ergebnis.
  - `[low]` `[reject]` (Blind) carried Kein Knopf „Link kopieren“ — wäre eine neue Funktion, siehe vorherigen Pass.
  - `[low]` `[patch]` (Blind) Fehlende Tests — Der abgeschlossene Zustand der Einstellungen ist mit den Tests aus der ersten Zeile abgedeckt. Für die übrigen Punkte zeigt der Code keinen Fehler: Team-Reihenfolge, Min(Rolle), gleichzeitiges Erneuern und Erneuern in der Nachbereitung.

## Design Notes

Entscheidungen im autonomen Lauf. Spec und Architektur lassen sie offen; gewählt ist jeweils die einfachste Lösung.

- **Adresse = Anmeldelink.** EXPERIENCE nennt den Anmeldelink als Zugang zur Freizeitseite. AD-11 legt die Links fest (mehrfach teilbar, bis Freizeitende, an die Freizeit gebunden, erneuerbar). 2-1 legt die Links deshalb schon an. Story 2-2 hängt nur Formular und Absenden an (`/freizeit/{token}/anmelden`). Beide Arten öffnen dieselbe Freizeitseite.
- **Ungültige Links** zeigen nur „Dieser Anmeldelink ist nicht mehr gültig.“ Zu einem unbekannten Token ist keine Leitung bekannt, deshalb fehlt ein Kontakt. Ein Aufruf ist eine Ansicht, keine Einlösung. Deshalb entsteht kein Eintrag im Sicherheitsprotokoll; gegen Durchprobieren schützt die Zugriffsbegrenzung.
- **„Bild“ im Team = Avatar mit Initialen** (EXPERIENCE, Baustein Avatar: „Initialen“; Mock). Profilfotos bräuchten öffentliche Datei-Auslieferung (AD-15) und stehen nicht in den UX-Flächen. Öffentlich erscheint nur der Vorname, wie im Mock (Datensparsamkeit).
- **Herkunft** ist ein freiwilliges Profilfeld der Person (Leitprinzip 7: jeder pflegt seinen Bereich). Ohne Herkunft steht nur die Rolle da.
- **Ausgebucht** = bestätigte Teilnehmende ≥ Höchstzahl (FR-67: „bis die Leitung jemanden bestätigt“). Bestätigungen gibt es erst ab 2-3. Bis dahin liefert `IBestaetigteTeilnehmende` 0, und 2-3 setzt die echte Zählung ein.
- **Einstellungen nur für die Leitung**, wie die Fläche „Einstellungen der Freizeit“ in EXPERIENCE es vorsieht.
- **Verschlüsselung der Links** mit einem aus dem Hauptschlüssel abgeleiteten Schlüssel, weil Links keiner Person gehören. Personen-Schlüssel (AD-14) passen hier nicht.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- erwartet: 0 Fehler, 0 Warnungen.
- `dotnet test` (aus dem Repo-Wurzelordner) -- erwartet: alles grün, einschließlich Architektur-, E2E- und Doku-Tests.
- `npm run build` in `doku/` -- erwartet: Exit 0.

**Manual checks (if no CLI):**
- Die sechs PNGs `freizeitseite-*`, `vertrauensseite-*` und `freizeit-einstellungen-*` in `doku/src/assets/screenshots/` zeigen die Inhalte.

## Auto Run Result

Status: done

**Umsetzung:** Jede Freizeit hat zwei Anmeldelinks (Teilnehmende, Mitarbeitende) als dauerhaft teilbare Tokens: gespeichert als Hash und zusätzlich verschlüsselt mit einem aus dem Hauptschlüssel abgeleiteten Schlüssel; erneuerbar, gültig bis Freizeitende. Ein Link öffnet die öffentliche Freizeitseite `/freizeit/{token}` (Organisation, Zeitraum, Ort, Beschreibung, Team mit Initialen, Vorname, Rolle, Herkunft, „Ausgebucht“ nur beim Link für Teilnehmende) und die Vertrauensseite `/freizeit/{token}/vertrauensseite`, die aus Freigabefenster, Löschfristen und Pflicht-Nachweisen erzeugt wird. Die Leitung pflegt Beschreibung, Ort und Höchstzahl auf der neuen Seite „Einstellungen“ per Autosave und erneuert dort Links. Jede Person trägt ihre Herkunft im Konto ein.

**Geänderte Dateien (Auswahl):**
- `src/Zeltlotse.SharedKernel/Zugangslinks/LinkSchutz.cs`, `Datenschutz/Hauptschluessel.cs` – Verschlüsselung dauerhafter Links (HKDF + AES-GCM).
- `src/Modules/Freizeiten/.../Domaene/{Freizeit,Anmeldelink}.cs`, `Freizeitseite/*`, Migration `Freizeitseite` – Felder, Links, Platzlage, Endpunkte (Leitung und anonym).
- `src/Modules/Organisationen/...Vertrauensseite/*` – Lese-Schnittstelle für Name, Löschfristen, Pflicht-Nachweise.
- `src/Modules/Personen/...` und Migration `Herkunft` – Profilfeld Herkunft, Steckbrief-Schnittstelle, Datenauskunft.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeitseite.cs`, `Gemeinsam/*` – DTOs, Aktionsschlüssel, Problemcode `freizeit-abgeschlossen`.
- `src/Zeltlotse.Public/Seiten/{Freizeitseite,Vertrauensseite,AnmeldelinkUngueltig,ZuVieleAufrufe}.razor` – öffentliche Seiten.
- `src/Zeltlotse.Ui.Komponenten/{PersonenListe,Dachzeile}.razor`, `Feld`/`AutosaveFeld` mehrzeilig – Bausteine.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor`, `FreizeitStart.razor`, `Konten/Konto.razor` – App-Oberfläche.
- `src/Zeltlotse.Api/Demo/*` – Demo-Werte und Links.
- Tests in Api-, Freizeiten-, Organisationen-, Personen-, SharedKernel-, Ui-, Architektur- und E2E-Projekten.
- `doku/`: neue Seiten `eltern/freizeitseite.md`, `leitung/freizeitseite-einrichten.md`, ergänzte Übersichten und `daten.md`, sechs neue Screenshots.

**Review:** 30 Befunde (0 high, 4 medium, 14 low, 12 false). 10 Korrekturen umgesetzt (medium: Zweig „Link erzeugen“ getestet, Team-Filter ohne Vorname getestet, 429 auf beiden öffentlichen Seiten als Hinweis statt 500; low: Test für unlesbaren Link, KonfliktText bei abgeschlossener Freizeit, doppeltes Erneuern verhindert, Initialen mit Textelementen, drei Doku-/Hinweistexte). Nichts zurückgestellt. Abgelehnt mit Begründung im Triage-Log: u. a. Links nach Freizeitende, „Noch kein Link“ nach Schlüsselwechsel, Kopier-Knopf, Fokus nach Erneuern. Zusätzlich vor dem Review entfernt: der nicht zutreffende Satz „Jede Einsicht steht im Zugriffsprotokoll der Person.“ auf der Vertrauensseite.

**Folge-Review empfohlen:** ja – 3 medium-Einträge gepatcht (patched: high 0, medium 3, low 7). Ungeprüftes Risiko: Die nachträglich eingebaute 429-Behandlung der öffentlichen Seiten (neue Seite `ZuVieleAufrufe`, Statuscode-Setzen im statischen Rendering) wurde nach dem Review von keiner unabhängigen Ebene mehr angesehen.

**Verifikation:** `dotnet build Zeltlotse.slnx --no-incremental` 0 Warnungen/0 Fehler; `dotnet test` 1328/1328 grün (inkl. Architektur, E2E und Doku-Screenshots); `npm run build` in `doku/` fehlerfrei (25 Seiten). Screenshot `freizeitseite-handy.png` von Hand angesehen. Screenshots mit reinem Rendering-Rauschen (`app-offline-*`, `meine-daten-auskunft-laptop`) zurückgesetzt.

**Restrisiken:** „Ausgebucht“ erscheint erst, wenn Story 2-3 die echte Zählung bestätigter Teilnehmender anstelle von `KeineBestaetigtenTeilnehmenden` einsetzt. Nach einem Wechsel des Hauptschlüssels lassen sich gespeicherte Links nicht mehr anzeigen (die alten Links gelten aber weiter, bis sie erneuert werden). Der Schreibschutz für abgeschlossene Freizeiten liegt in den Handlern, nicht in der Pipeline (AD-17 dort noch nicht gebaut). Die Vertrauensseite beschreibt die Sichtbarkeitsregeln des Freizeitpasses (FR-24), die erst mit Story 2-5 vollständig durchgesetzt werden.

### Folgedurchlauf 2026-10-12

**Umsetzung:** Review-Folgedurchlauf ohne neue Funktion. Ergänzt wurden fehlende Tests und `noindex` auf den öffentlichen Seiten.

**Geänderte Dateien:**
- `src/Zeltlotse.Public/Seiten/Freizeitseite.razor`, `Vertrauensseite.razor` – `<meta name="robots" content="noindex">`, damit Suchmaschinen weder Team-Vornamen noch Adressen mit Token aufnehmen.
- `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs` – zwei bUnit-Tests für den abgeschlossenen Zustand der Einstellungen (Hinweis, 409 beim Speichern und beim Erneuern).
- `tests/Zeltlotse.Api.Tests/FreizeitseiteTests.cs` – prüft die Absätze der Beschreibung, `noindex` und „24 Monate“ im HTML der Vertrauensseite nach einer Änderung der Löschfrist.

**Review:** 27 Befunde (0 high, 0 medium, 11 low, 15 false, 1 maybe-false). 4 Korrekturen umgesetzt, alle low: Tests für den abgeschlossenen Zustand, Absätze, „24 Monate“ im HTML, noindex. Zurückgestellt: die Zugriffsbegrenzung für breit geteilte Links (medium, ungeprüft). Abgelehnt mit Begründung im Triage-Log, unter anderem: Felder bei abgeschlossener Freizeit sperren, badInput im Zahlfeld, Spur beim Erneuern, NotFoundPage, Sortierung.

**Folge-Review empfohlen:** nein – Folgedurchlauf ohne gepatchten high-Befund (gepatcht: high 0, medium 0, low 4).

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 1330/1330 grün, einschließlich Architektur-, E2E- und Doku-Screenshot-Tests.
- `npm run build` in `doku/`: Exit 0 (25 Seiten).
- Die Screenshots der Story blieben unverändert, weil `noindex` nicht sichtbar ist.
- Drei Screenshots mit reinem Rendering-Rauschen (`app-offline-*`, `meine-daten-auskunft-laptop`) zurückgesetzt.

**Restrisiken:** wie im ersten Durchlauf. Neu ist die zurückgestellte Frage, ob 20 Aufrufe je Minute und IP für öffentlich geteilte Anmeldelinks reichen.
