---
title: 'Anwender-Doku: Gerüst, Demo-Daten und Screenshot-Pipeline'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: 'b0d2627a9ebeec23022b00dd86fb3f84dbba20c7'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/rollen-und-rechte.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/glossar.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** NFR-20 und CAP-40 verlangen eine Anwender-Doku, die mit jeder Story mitwächst: Seiten nach Rolle und Aufgabe, Screenshots aus erfundenen Demo-Daten, automatisch von Playwright erzeugt, Build mit einem Befehl. Es gibt weder `doku/` noch Demo-Daten noch Screenshot-Tests, und die Doku-Seite der App-Hülle aus 0-3 steht aus.

**Approach:** Ein Astro-Starlight-Projekt unter `doku/` mit Gliederung nach Rolle, ELKW-Farben und der Seite „Was passiert mit meinen Daten“. Dazu ein Demo-Daten-Baustein im API-Host, der nur in `Development` und `Testing` einschaltbar ist, und Playwright-Screenshot-Tests (Kategorie `Doku`, Handy und Laptop), die die Bilder der heutigen Oberflächen nach `doku/src/assets/screenshots/` schreiben. Testgetrieben: Tests vor dem Code.

## Boundaries & Constraints

**Always:**
- **Doku-Projekt (`doku/`):**
  - Astro Starlight mit exakt fixierten Versionen in `package.json` (aktuelle stabile Versionen, zueinander passend; geprüft: `astro` 7.3.8, `@astrojs/starlight` 0.42.6) und eingechecktem `package-lock.json`. Skripte `dev`, `build`, `preview`. `npm ci` einmalig, danach baut `npm run build` die Seite nach `doku/dist/`.
  - Nur Deutsch: Starlight mit einer einzigen Sprache `de` als Wurzel, ohne Sprachwahl. Titel „Zeltlotse – Anleitung“.
  - Sprache wie in der App: ohne Du/Sie (keine Anrede), Fachbegriffe laut `glossar.md`.
  - ELKW-Farben (Farbwelt 09, Stil nahbar) über eine eigene CSS-Datei, die die Starlight-Farbvariablen setzt. Hell: Akzent `#307F91`, Akzent hell `#D6E6EA`/`#EAF2F4`, Text `#000000`, Fläche `#FFFFFF`. Dunkel: Grund `#0F1719`, Fläche `#1D2A2E`, Text `#EAF2F4`, Akzent `#83B4BF`. Schrift Sarabun (Dateien und `OFL.txt` aus `src/Zeltlotse.Ui.Komponenten/wwwroot/fonts/` nach `doku/` kopiert), Rückfall Arial. Text und Links erfüllen WCAG 2.2 AA (Kontrast ≥ 4,5:1).
  - Keine Ressourcen von Drittanbietern zur Laufzeit (keine Web-Fonts aus dem Netz, kein Analytics).
- **Gliederung (Seitenleiste in dieser Reihenfolge):**
  1. Startseite `index` – was Zeltlotse ist, Wegweiser nach Rolle.
  2. „Erste Schritte“ → „Zeltlotse öffnen“: Web-App im Browser öffnen und am Handy installieren, öffentliche Seiten ohne Installation, Offline-Ansicht mit Notfall-Box und „Notruf 112“; mit den Screenshots `app-start`, `app-offline` und `oeffentliche-startseite` jeweils Handy und Laptop.
  3. Je eine Gruppe „Eltern“, „Mitarbeitende“, „Leitung“, „Küche“, „Org-Admin“ (Ordner `eltern/`, `mitarbeitende/`, `leitung/`, `kueche/`, `org-admin/`) mit einer Überblicksseite `index`: wer die Rolle hat und was sie in Zeltlotse darf (Quelle `rollen-und-rechte.md`; Küche ist eine Aufgabe im Team, keine Rolle). Aufgaben-Seiten kommen mit den Fach-Stories.
  4. „Was passiert mit meinen Daten“ (`daten`): in Klartext Freigabe und Freigabefenster (Leitung ab Eingang der Anmeldung, Team ab Bestätigung, Ende nach dem Org-Intervall ab Ende des letzten Freizeittages), Einsicht und Zugriffsprotokoll, Nachweise (nur einmalige Einsicht, Organisation speichert nur die Bestätigung), Löschen (eigenes Konto, Fristen der Organisation, Daten nur in der EU).
- **Screenshots:** Bilder liegen nur unter `doku/src/assets/screenshots/` als `<name>-handy.png` (375×812) und `<name>-laptop.png` (1280×800), helles Farbschema, `de-DE`, `Europe/Berlin`. Doku-Seiten binden sie mit Alternativtext über relative Markdown-Pfade ein; fehlt ein Bild, scheitert der Doku-Build.
- **Screenshot-Tests:** in `tests/Zeltlotse.E2E.Tests`, Klasse mit `[Trait("Kategorie", "Doku")]`, gegen den veröffentlichten Host der bestehenden Fixture `VeroeffentlichterHost`, mit eingeschalteten Demo-Daten. Sie laufen bei jedem `dotnet test` mit und überschreiben die Bilder. Jeder Test prüft vor dem Bild den sichtbaren Inhalt (wie `OfflineStartTests`) und danach, dass die Datei ein PNG in der erwarteten Breite ist.
- **Demo-Daten (Konvention „Anwender-Doku“ der Spine):**
  - Schalter `Zeltlotse:DemoDaten:Aktiv` (Vorgabe aus). Ist er an und die Umgebung weder `Development` noch `Testing`, bricht `Startpruefung` den Start mit einer Meldung ab, die den Schalter nennt.
  - Ist er an, spielt der Host nach den Migrationen die Demo-Daten ein; wiederholtes Einspielen (Neustart) ist unschädlich.
  - `DemoKatalog` im API-Host enthält die erfundenen Stammdaten: eine Organisation, eine Freizeit mit Ort und Daten, Personen mit Rolle (2 Leitung, 3 Team, 2 Eltern mit 3 Kindern, 1 Org-Admin). Alle Mail-Adressen enden auf `@example.org`, keine Telefonnummern, keine echten Namen von Organisationen.
  - `appsettings.Development.json` schaltet die Demo-Daten ein (Aspire lokal), die E2E-Fixture ebenso.
- Doku-Regeln als Architekturtest (Repo-Scan, ohne Node): keine Anrede (`du`, `dich`, `dein…`, `euch`, `euer`, `eure…`, `Ihnen`, `Ihr…`, `Ihre…` als ganzes Wort) in `doku/src/content/docs/**/*.md(x)`; die fünf Rollen-Ordner mit `index` und die Seite `daten` existieren.
- Test vor dem Code (AD-30). Paketversionen nur zentral, 0 Warnungen.

**Never:**
- Keine Fachseiten, Fach-Endpunkte oder Entitäten (kommen ab 1-1); die Demo-Daten schreiben in dieser Story noch nichts in Modul-Tabellen.
- Keine Screenshots von Hand, keine Bilder mit echten Personendaten, keine Bilder außerhalb von `doku/src/assets/screenshots/`.
- Kein zweiter Sprachzweig, keine Suche über Drittanbieter, keine Komponentenbibliothek außer Starlight.
- Keine Änderung an CSP, Oberfläche oder Verhalten der App und der öffentlichen Seiten.
- Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Demo an, Testing/Development | `Zeltlotse:DemoDaten:Aktiv=true`, Umgebung `Testing` oder `Development` | Host startet, `/health` 200; Einspielen läuft nach den Migrationen | zweiter Start mit derselben Datenbank ebenfalls 200 |
| Demo an, Betrieb | Schalter an, Umgebung `Production` | Start bricht ab, Meldung nennt `Zeltlotse:DemoDaten:Aktiv` | – |
| Demo aus | Schalter fehlt oder `false`, beliebige Umgebung | Host startet ohne Einspielen | – |
| Demo-Katalog | `DemoKatalog` | alle Mail-Adressen `@example.org`, Mail-Adressen eindeutig, Rollen in der geforderten Anzahl | – |
| Screenshot App-Start | `/app/` online, Handy und Laptop | „Noch keine Freizeit geöffnet.“ sichtbar → `app-start-handy.png` (Breite 375), `app-start-laptop.png` (Breite 1280) | – |
| Screenshot Offline | App einmal online geladen, dann offline neu geladen | Offline-Leiste und „Notruf 112“ sichtbar → `app-offline-handy.png`, `app-offline-laptop.png` | – |
| Screenshot Public | `/` | Überschrift „Zeltlotse“ sichtbar → `oeffentliche-startseite-handy.png`, `…-laptop.png` | – |
| Doku-Build | `npm ci` einmalig, dann `npm run build` in `doku/` | Exit-Code 0, `doku/dist/` mit allen Seiten der Gliederung | fehlendes Bild → Build scheitert |
| Anrede in der Doku | `.md`-Datei mit „Melde dich an“ | Architekturtest schlägt fehl und nennt Datei und Wort | – |

</intent-contract>

## Code Map

- `tests/Zeltlotse.E2E.Tests/VeroeffentlichterHost.cs` -- Fixture (Assembly-Fixture, `AssemblyInfo.cs:3`): `dotnet publish` nach Temp, PostgreSQL per `PostgresContainer`, Start der DLL mit Umgebungsvariablen (Z. 48–52: `ASPNETCORE_ENVIRONMENT=Testing`, `ASPNETCORE_URLS`, `ConnectionStrings__zeltlotse`, Haupt- und HMAC-Schlüssel); hier `Zeltlotse__DemoDaten__Aktiv=true` ergänzen. `Wurzel()` (Z. ~188, privat, sucht `Zeltlotse.slnx`) auf `internal` heben und für den Bildordner nutzen. Eigenschaften `Adresse`, `Browser`, `Ausgabe`.
- `tests/Zeltlotse.E2E.Tests/OfflineStartTests.cs` -- Vorbild: Kontext mit `ViewportSize`, `ColorScheme`, `Locale`, `TimezoneId`; Offline-Ablauf (online laden, `navigator.serviceWorker.ready`, neu laden bis `controller`, `SetOfflineAsync(true)`, neu laden); Locators `.zl-offline-leiste`, `.zl-notfall-box a[href='tel:112']`. Bisher keine `[Trait]` im Repo.
- `src/Zeltlotse.Api/Program.cs:70-73` -- `Startpruefung.Ausfuehren(...)`, dann `ModulMigrationen.AnwendenAsync()`; das Einspielen der Demo-Daten folgt direkt danach.
- `src/Zeltlotse.Api/Start/Startpruefung.cs` -- `TestUmgebung = "Testing"`; Hauptschlüssel- und HMAC-Prüfung laufen vor dem frühen `return` für Testing; die Demo-Prüfung gehört ebenfalls vor dieses `return`.
- `tests/Zeltlotse.Api.Tests/StartpruefungTests.cs` -- Muster: `new ZeltlotseApiFactory(await postgres.NeueDatenbankAsync()) { Umgebung = "Production" }`, `factory.Einstellungen[...]`, `StartScheitertAsync(factory)` aus `TestHelfer`.
- `src/Zeltlotse.Api/appsettings.Development.json` -- heute nur `Logging`; Abschnitt `Zeltlotse:DemoDaten:Aktiv` ergänzen.
- `src/Zeltlotse.App/Pages/Start.razor` (h1 „Zeltlotse“, „Noch keine Freizeit geöffnet.“), `src/Zeltlotse.App/Layout/MainLayout.razor` (Offline-Leiste, Notfall-Box, Offline-Bereich), `src/Zeltlotse.Public` Startseite `/` (h1 „Zeltlotse“) -- die heute fotografierbaren Oberflächen. Nur lesen.
- `src/Zeltlotse.Ui.Komponenten/wwwroot/css/elkw-tokens.css:278-294` (Farbwelt 09), `wwwroot/css/zeltlotse.css` (Dunkel-Tokens), `wwwroot/fonts/` (Sarabun + `OFL.txt`) -- Quelle der Doku-Farben und -Schrift. Nur lesen.
- `tests/Zeltlotse.ArchitectureTests/ClientTests.cs:162-170`, `Loesung.cs` (`Loesung.Wurzel`) -- Vorbild für den Repo-Scan der Doku-Regeln; bestehende Scans betreffen nur `src/`.
- `.gitignore` -- enthält schon `node_modules/`, `doku/dist/`, `doku/.astro/`; Screenshots werden eingecheckt.
- Umgebung geprüft: Node v26.5.1, npm 11.12.1, Registry erreichbar (`astro` 7.3.8, `@astrojs/starlight` 0.42.6), Docker läuft.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.ArchitectureTests/DokuTests.cs` -- ZUERST: Repo-Scan der Doku-Regeln (keine Anrede; Rollen-Ordner mit `index`; Seite `daten`) mit Negativ-Fall über eine Prüfmethode, die einen Text bzw. eine Dateiliste nimmt.
- `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` -- ZUERST: HTTP-Tests gegen echtes PostgreSQL für die Demo-Zeilen der Matrix (an/Testing, an/Development, zweimaliger Start, an/Production scheitert, aus) und der Katalog-Test.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- ZUERST: Screenshot-Tests (Trait `Kategorie=Doku`) für `app-start`, `app-offline`, `oeffentliche-startseite` je Handy und Laptop; schreibt nach `doku/src/assets/screenshots/`; `VeroeffentlichterHost.cs` -- Demo-Schalter, `Wurzel()` internal.
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `src/Zeltlotse.Api/Demo/DemoDaten.cs` -- Katalog und Einspielen (Einstiegspunkt für spätere Stories, heute ohne Schreibzugriff); `Start/Startpruefung.cs` -- Demo-Prüfung; `Program.cs` -- Einspielen nach den Migrationen; `appsettings.Development.json` -- Schalter an.
- `doku/` -- `package.json`, `package-lock.json`, `astro.config.mjs`, `tsconfig.json`, `src/content.config.ts`, `src/styles/elkw.css`, Schriften mit `OFL.txt`, `src/content/docs/` mit allen Seiten der Gliederung, `src/assets/screenshots/` (von den Tests erzeugt), kurze `README.md` (zwei Befehle).

**Acceptance Criteria:**
- Given ein Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (inklusive Architektur- und Doku-Screenshot-Tests) und die sechs PNG-Dateien liegen neu erzeugt unter `doku/src/assets/screenshots/`.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und danach `npm run build` laufen, then endet der Build ohne Fehler und `doku/dist/` enthält Startseite, „Zeltlotse öffnen“, die fünf Rollen-Seiten und „Was passiert mit meinen Daten“ mit eingebundenen Bildern.
- Given die gebaute Doku, when eine Seite im hellen und dunklen Farbschema geöffnet wird, then nutzt sie die ELKW-Farben, Sarabun und lädt keine Ressource von fremden Adressen.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 28 findings — high 0, medium 2, low 19, false 7, maybe-false 0
- findings:
  - `[low]` `[patch]` (edge) Anrede-Regex verpasst „dir“, „Sie“, kleines „ihr“ — „dir“ ergänzt samt Negativ-Fall; „Sie“ und „ihr“ sind mehrdeutig (auch „sie“/„ihr“ in der 3. Person) und bleiben draußen.
  - `[low]` `[reject]` (edge) „Ihr …“ am Satzanfang (3. Person) wird als Anrede gemeldet — selten, Umformulieren genügt; ein Fix bräuchte Satzanfang-Erkennung.
  - `[low]` `[patch]` (edge) `FehlendeSeiten` prüft die Endung mit Groß-/Kleinschreibung, `DokuDateien` ohne — auf `OrdinalIgnoreCase` umgestellt, Negativ-Fall mit `index.MD`.
  - `[false]` `[reject]` (edge) Nicht-boolescher Schalter („1“) stürzt ohne Nennung des Schalters ab — `GetValue<bool>` wirft „Failed to convert configuration value at 'Zeltlotse:DemoDaten:Aktiv'“, der Schalter wird also genannt.
  - `[low]` `[reject]` (edge) Absolute Links brechen bei Auslieferung unter einem Unterpfad — Hosting der Doku ist nicht festgelegt; Fix wäre `base` plus Umbau aller Links.
  - `[low]` `[patch]` (edge) „Erste Schritte“ listet die Seite von Hand, spätere Seiten fehlen in der Seitenleiste — auf `autogenerate` umgestellt.
  - `[low]` `[patch]` (edge) Behauptung „keine Anrede“ deckt „Sie“-Form nicht — gleiche Ursache wie die erste Zeile, „dir“ ergänzt, „Sie“ bleibt mehrdeutig.
  - `[low]` `[patch]` (blind) Anrede-Regex verpasst „dir“/„Sie“ — gleiche Ursache, mitbehoben.
  - `[low]` `[patch]` (blind) Pflichtseiten ohne Startseite und „Zeltlotse öffnen“ — beide in `PflichtSeiten` aufgenommen.
  - `[medium]` `[patch]` (blind) Doku-Build läuft nicht automatisch, Bildpfade ungeprüft — neue Architekturregel: jeder Screenshot-Link hat eine Datei, jede Datei wird verlinkt (Negativ-Fälle für Markdown und MDX).
  - `[low]` `[reject]` (blind) AC „keine fremden Ressourcen“ und ELKW-Farben ohne automatischen Test — setzt einen Node-Build in `dotnet test` voraus; manuell mit Playwright und axe geprüft (siehe Auto Run Result).
  - `[false]` `[reject]` (blind) Idempotenz-Test kann nicht scheitern — der Test heißt „startet ebenfalls“ und prüft genau das (`/health` 200); die irreführende Zähler-Prüfung ist über die vgap-Zeile entfernt.
  - `[low]` `[reject]` (blind) Feste Daten im Demo-Katalog altern (Freizeit August 2027) — der Katalog wird noch nirgends eingespielt; relative Daten brauchen eine Uhr als Parameter; die erste Story, die die Freizeit einspielt, entscheidet (Restrisiko).
  - `[low]` `[reject]` (blind) Offline-Screenshots ändern sich je Lauf (Uhrzeit) — heute ruft keine App-Seite die API, die Leiste zeigt nur „Offline“; wäre nur kosmetisch, Fix bräuchte Uhr-Einfrieren.
  - `[false]` `[reject]` (blind) Kein Abschnitt für Teilnehmende mit QR-Zugang — NFR-20a legt die Gruppen Eltern, Mitarbeitende, Leitung, Küche, Org-Admin fest; der QR-Zugang kommt mit V6.
  - `[low]` `[patch]` (blind) Daten-Seite lässt Punkte aus — Satz zu Gast-Anmeldungen ohne Konto ergänzt (AD-7); Verantwortlichkeit, Betroffenenrechte und Vorgabewert hängen an OP-6 bzw. Org-Richtlinien und bleiben offen.
  - `[low]` `[patch]` (blind) Installationsschritte nur für Android — iPhone-Schritt (Safari: Teilen → „Zum Home-Bildschirm“) ergänzt.
  - `[low]` `[reject]` (blind) Absolute Links, kein `site`/`base` — gleiche Ursache wie edge „Unterpfad“, verworfen.
  - `[low]` `[patch]` (blind) Keine Node-Version und keine Voraussetzungen im README — Node LTS (geprüft mit 26) und Screenshots aus `dotnet test` (Docker) ergänzt.
  - `[false]` `[reject]` (blind) CSS-Kommentare nennen zwei Quellen; Akzent auf hellem Akzent unter AA — DESIGN.md definiert die Dunkel-Tokens, `zeltlotse.css` setzt sie um (beides stimmt); auf `accent-low` steht `accent-high` (`#000000`), nicht der Akzent.
  - `[low]` `[patch]` (blind) Alternativtexte der öffentlichen Seite inhaltsleer; „Adresse der Organisation“ widerspricht AD-5 — Alternativtexte nennen Überschrift und Anmeldelink-Satz, Text sagt „Adresse von Zeltlotse“.
  - `[medium]` `[patch]` (vgap) Nichts prüft, dass Bild-Links und Screenshot-Dateien übereinstimmen — gleiche Ursache wie blind „Doku-Build“, mitbehoben.
  - `[low]` `[patch]` (vgap) Entfernen des Schalters aus `appsettings.Development.json` lässt keinen Test scheitern — neuer Test: Development ohne gesetzten Schalter → `Aktiv` und eingespielt.
  - `[low]` `[patch]` (vgap, other) Zweiter-Start-Test prüft einen Zähler je Host — Zähler-Prüfung entfernt, Kommentar: ab 1-1 Datenbankzeilen prüfen.
  - `[false]` `[reject]` (intent) Demo-Daten erscheinen nicht in den Screenshots — es gibt noch keine Oberfläche mit Daten; die Intent verlegt die Fachseiten in die folgenden Stories.
  - `[low]` `[patch]` (intent) Doku-Build, eingebundene Bilder nicht in Tests — gleiche Ursache wie blind „Doku-Build“; Link-Regel ergänzt, Build selbst bleibt Verifikationsbefehl.
  - `[false]` `[reject]` (intent) Nur Rollen-Achse, keine Aufgaben-Seiten — Aufgaben-Seiten sind Fachseiten der folgenden Stories (Intent).
  - `[false]` `[reject]` (intent) AD-30-Fälle 403/404/Token fehlen — 0-4 legt keinen Endpunkt dieser Arten an; Ersatz-HTTP-Tests gegen echtes PostgreSQL decken den Start ab.

## Design Notes

- **Demo-Daten ohne Entitäten:** Es gibt noch keine Organisation, Freizeit oder Person in einem Modul. Diese Story legt deshalb nur Schalter, Startprüfung, Einspielpunkt und den erfundenen Katalog an. Ab 1-1 ergänzt jede Fach-Story in `DemoDaten` die Commands ihres Moduls (über Mediator, AD-27; die Commands sind über `InternalsVisibleTo("Zeltlotse.Api")` erreichbar) und macht sie idempotent, etwa „nur anlegen, wenn die Demo-Organisation fehlt“. Ein Seed, der direkt in Tabellen schreibt, ist verboten (AD-27).
- **Screenshots bei jedem Lauf:** NFR-20c verlangt „bei jedem Lauf“. Darum laufen die Doku-Tests im normalen `dotnet test`, nicht in einem eigenen Schritt. Gezielt laufen sie mit dem Trait-Filter von xUnit v3 (`dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-trait "Kategorie=Doku"`; den genauen Aufruf hält das Auto Run Result fest).
- **Breite statt Pixelvergleich:** Die Tests prüfen Inhalt und Bildbreite, keinen Pixelvergleich; Uhrzeit-Texte (Offline-Leiste) dürfen sich ändern.
- **„Was passiert mit meinen Daten“ schon jetzt:** NFR-20a fordert die Seite fest. Sie beschreibt die verbindlichen Regeln aus SPEC und Spine (Freigabe, Einsicht, Löschen), nicht einzelne Bildschirme; Screenshots kommen mit den Fach-Stories.
- **Rollen-Überblick statt leerer Aufgaben-Seiten:** Jede Rollen-Gruppe hat eine Überblicksseite mit echtem Inhalt (Rolle und Rechte). Leere „folgt“-Seiten gibt es nicht.
- **AD-30-Pflichtfälle (Hinweis des Aufrufers):** 0-4 legt keinen HTTP-Endpunkt an, weder Organisation/Freizeit noch persönlich noch Token. Die Fälle „falsche Rolle 403“, „fremde Organisation 404“ und Token-Fälle haben keinen Gegenstand. An ihrer Stelle prüfen HTTP-Tests gegen echtes PostgreSQL den Start mit und ohne Demo-Daten in allen Umgebungen.
- **Starlight-Farben:** Starlight steuert Farben über `--sl-color-accent-low`, `--sl-color-accent`, `--sl-color-accent-high`, `--sl-color-white` … `--sl-color-black` und `--sl-font`, getrennt für `:root` (dunkel, Starlights Vorgabe) und `:root[data-theme='light']`. Die Werte kommen nur aus den oben genannten ELKW-Tokens.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, sechs PNG-Dateien unter `doku/src/assets/screenshots/` neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten der Gliederung in `doku/dist/`

**Manual checks (if no CLI):**
- `doku/dist/` im Browser öffnen (oder `npm run preview`): Seitenleiste in der Reihenfolge der Gliederung, Screenshots sichtbar, helles und dunkles Schema in ELKW-Farben.

## Auto Run Result

Status: done

**Zusammenfassung:** Anwender-Doku nach NFR-20 und CAP-40, ohne Fachfunktion:
- `doku/`: Astro Starlight (astro 7.3.8, @astrojs/starlight 0.42.6, Lockfile eingecheckt), nur Deutsch, Titel „Zeltlotse – Anleitung“, ELKW-Farben (Farbwelt 09, hell und dunkel) mit Sarabun. Seitenleiste: Startseite, „Erste Schritte → Zeltlotse öffnen“ (mit allen sechs Screenshots), Überblicksseiten für Eltern, Mitarbeitende, Leitung, Küche und Org-Admin, „Was passiert mit meinen Daten“. Build: einmal `npm ci`, dann `npm run build`.
- Demo-Daten im API-Host: Schalter `Zeltlotse:DemoDaten:Aktiv`, nur in Development und Testing erlaubt (sonst bricht `Startpruefung` den Start ab), Einspielpunkt nach den Migrationen und erfundener `DemoKatalog` (Jugendwerk Musterstadt, Zeltlager Sommer 2027, 11 Personen, Mails nur `@example.org`). Geschrieben wird noch nichts; ab 1-1 ergänzen die Fach-Stories ihre Commands.
- Screenshot-Pipeline: `DokuScreenshotTests` (Trait `Kategorie=Doku`) fotografiert App-Start, App offline und die öffentliche Startseite in Handy (375×812) und Laptop (1280×800) und schreibt nach `doku/src/assets/screenshots/`. Gezielter Aufruf: `dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-trait "Kategorie=Doku"`.
- Architekturtests für die Doku: keine Anrede, Pflichtseiten vorhanden, jeder Screenshot-Link hat eine Datei und jede Datei ist verlinkt.

**Dateien:**
- `doku/` – Starlight-Projekt (Konfiguration, `elkw.css`, Schriften mit `OFL.txt`, Favicon, neun Seiten, sechs Screenshots, README).
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs`, `DemoDaten.cs` – Katalog und Einspielpunkt.
- `src/Zeltlotse.Api/Start/Startpruefung.cs` – Demo-Daten nur in Development/Testing.
- `src/Zeltlotse.Api/Program.cs` – Registrierung und Einspielen nach den Migrationen.
- `src/Zeltlotse.Api/appsettings.Development.json` – Schalter an.
- `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` (neu), `TestHelfer.cs` (`StartScheitertAsync` hierher verschoben), `StartpruefungTests.cs`.
- `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs`, `DokuTests.cs` (neu).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (neu), `VeroeffentlichterHost.cs` (Demo-Schalter, `Wurzel()` internal).

**Review:** 28 Befunde (high 0, medium 2, low 19, false 7). Gepatcht: 1 medium-Eintrag (Abgleich Screenshot-Links und Dateien; zwei Zeilen gleicher Ursache) und 10 low-Einträge (Anrede „dir“, Endung ohne Groß-/Kleinschreibung, Pflichtseiten, `autogenerate` für „Erste Schritte“, Development-Test ohne gesetzten Schalter, Zähler-Prüfung im Zweiter-Start-Test, Gast-Satz auf der Daten-Seite, iPhone-Schritt, Alternativtexte und „Adresse von Zeltlotse“, README-Voraussetzungen). Nichts zurückgestellt. Verworfen mit Begründung im Triage Log: Satzanfang-„Ihr“, Unterpfad/absolute Links (2×), automatische Prüfung fremder Ressourcen, feste Katalog-Daten, Uhrzeit in Offline-Screenshots sowie alle `false`-Befunde (nicht-boolescher Schalter, Idempotenz-Testname, QR-Teilnehmende, CSS-Quellen, Demo-Daten in Bildern, Aufgaben-Achse, AD-30).

**Folge-Review empfohlen:** nein (gepatcht: high 0, medium 1, low 10).

**AD-30 (Hinweis des Aufrufers):** 0-4 legt keinen HTTP-Endpunkt an; die Fälle „falsche Rolle 403“, „fremde Organisation 404“ und Token-Fälle haben keinen Gegenstand. Stattdessen prüfen HTTP-Tests gegen echtes PostgreSQL den Start mit und ohne Demo-Daten in Testing, Development, Staging und Production. Tests entstanden vor dem Code.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 487 von 487 grün, inklusive Architektur-, bUnit-, Playwright- und Doku-Screenshot-Tests; sechs PNG-Dateien neu geschrieben.
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 9 Seiten. Mit entferntem Screenshot scheitert der Build mit `ImageNotFound` (Exit 1).
- Manuell (Implementierung): gebaute Doku mit Playwright und axe hell/dunkel, Handy/Laptop geprüft: 0 Verstöße gegen WCAG 2.2 AA, Schrift Sarabun, keine Anfragen an fremde Adressen.

**Restrisiken:**
- `npm audit` meldet 10 moderate Befunde in Build-Abhängigkeiten von Starlight (expressive-code); der angebotene Fix stuft Starlight herab.
- Der Doku-Build selbst läuft nicht in `dotnet test`; die Link-Regel deckt nur Screenshot-Pfade ab.
- `--sl-color-accent-high` hell ist `#000000` statt eines Akzent-Tons (Kontrast auf `#D6E6EA`).
- Feste Daten im Demo-Katalog (Freizeit August 2027): die erste Story, die die Freizeit einspielt, sollte relative Daten oder eine feste Demo-Uhr festlegen.
- Screenshots werden bei jedem `dotnet test` neu geschrieben; Git zeigt Änderungen nur bei abweichendem Inhalt.

**Abschluss-Commit:** nicht erstellt. Die Berechtigungsprüfung der Sitzung hat `git commit` abgelehnt („Unrequested Commit“). Alle Änderungen liegen unversioniert im Arbeitsbaum auf `mvp`; Commit (ohne Push) durch den Orchestrator oder nach Freigabe durch Marvin.
