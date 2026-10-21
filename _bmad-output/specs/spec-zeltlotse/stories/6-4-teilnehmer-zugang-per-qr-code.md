---
title: 'Story 6-4: Teilnehmer-Zugang per QR-Code'
type: 'feature'
created: '2026-10-21'
baseline_revision: '2a0183fb6559feca3fbeab9a0ddfaf4a3c377149'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/6-1-kanaele-team-und-alle-mit-push.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/3-7-module-pro-freizeit-schalten.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Auf dem iPhone fehlt der Web-App vom Home-Bildschirm vielleicht das Gast-Cookie aus Safari, dann kommt der Gast dort nicht an Push.
    evidence: |-
      iOS trennt den Speicher von Home-Bildschirm-Web-Apps von Safari. Ob es Cookies beim Hinzufügen übernimmt, hängt von der iOS-Version ab. Klären lässt sich das nur am echten Gerät. Falls nicht: Link oder Code in der installierten App einlösbar machen.
    location: >-
      src/Zeltlotse.App/Features/Konten/Anmelden.razor, src/Zeltlotse.App/Features/Teilnehmer/TeilnehmerEinloesen.razor
    severity: medium (unverified)
  - summary: >-
      Die Fehlerzweige der QR-Card in den Einstellungen (Kopieren scheitert, 409 beim Erneuern, 404) sind nicht per bUnit getestet.
    evidence: |-
      NachgebildeterBrowser.KopierenErlaubt = false wird nirgends gesetzt; die Seitentests decken nur den Erfolg ab.
    location: >-
      src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor
    severity: low
  - summary: >-
      Die Zugriffsbegrenzung ist ein gemeinsames Budget je IP für alle begrenzten Routen; eine Scan-Welle im Lager-WLAN kann auch die Anmeldung der Leitung im selben Netz für bis zu eine Minute mit 429 sperren.
    evidence: |-
      Eine Richtlinie „zugriffsbegrenzt“ mit festem Fenster von 20 Anfragen je Minute und Partition nur nach IP (src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs). Dieselbe Partition teilen Einlösen, Login-Link, Passwort-Anmeldung und GET /api/betrieb/antiforgery. Die Teilung gab es schon vor 6-4 (AD-25); 6-4 bringt nur einen weiteren, gehäuften Nutzer. Abhilfe bräuchte eine eigene Partition oder Grenze für das Einlösen, also eine Entscheidung an AD-25. Bis dahin lässt sich AnfragenJeMinute im Betrieb erhöhen.
    location: >-
      src/Zeltlotse.Api/Sicherheit/Zugriffsbegrenzung.cs, src/Modules/Freizeiten/Zeltlotse.Freizeiten/FreizeitenModul.cs:157
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Teilnehmende auf der Freizeit haben keinen Weg in Zeltlotse. Sie wissen nicht, wann es Essen gibt, finden die Hausordnung nicht und bekommen Nachrichten aus dem Kanal „Alle“ nicht aufs Handy (CAP-38, FR-64, NS-10, AD-11, AD-12, AD-21).

**Approach:** Ist das Modul Teilnehmer-Zugang an, erzeugt die Leitung in den Einstellungen einen dauerhaft geteilten QR-Link (Modul Freizeiten, Muster Anmeldelink) und druckt ihn als Aushang. Wer ihn scannt, landet in der Web-App unter `/app/teilnehmer/{token}`. Ein Tipp tauscht das Token per POST gegen ein eigenes Gast-Cookie (nur Freizeit, Geräte-Kennung, Stempel des Links). Mit diesem Cookie liest der Gast nur Wochenübersicht (Zeiten und Titel), Infos (Dokumente „Eltern und Teilnehmende“) und den Kanal „Alle“. Schreiben darf er nur das eigene Push-Abo. Der Zugang endet mit dem Freizeitende, beim Erneuern des Links und beim Abschalten des Moduls.

## Boundaries & Constraints

**Always:**
- **QR-Link (Freizeiten):** Entität `QrZugang : IOrganisationsEigen` mit `FreizeitId` (eindeutig), `TokenHash`, `TokenGeschuetzt`, `Stempel` (Guid, neu bei jedem Erneuern) und `ErzeugtAm`. Sie folgt dem Muster `Anmeldelink`: `Zugangslink.NeuesToken/TokenHash` mit Zweck `"qr-zugang"`, `ILinkSchutz` mit Bezug FreizeitId und Cascade an der Freizeit. Adresse: `{OeffentlicheAdresse}/app/teilnehmer/{token}`.
  - Die Matrix des QR-Codes erzeugt der Server mit dem Paket **QRCoder** (MIT, aktuelle stabile Version, nur im Modul Freizeiten), Fehlerkorrektur M, mit Ruhezone. Die API liefert sie als Zeilen aus `'1'`/`'0'`. Clients dürfen keine Fremdpakete nutzen (ClientRegeln).
- **Gast-Sitzung (API-Host):** ein zweites Cookie-Schema `ZeltlotseGast` mit dem Cookie `__Host-Zeltlotse-Gast` (HttpOnly, Secure, Lax, Path=/).
  - Es ist persistent mit `ExpiresUtc` = Freizeitende und gleitet nicht.
  - Claims: `freizeit`, `geraet` (neue Guid je Einlösung) und `stempel`.
  - Es ist nie das Default-Schema. Die normale Sitzung und die Antiforgery-Bindung bleiben unberührt; der Gast ist für sie anonym.
- **Rechtemerkmal `.NurQrGast()`** (SharedKernel `Rechtemerkmale`) mit Auswertung in `RechtePruefung`:
  - Gast-Schema authentifizieren, dann `IQrZugangLesen.PruefenAsync(freizeitId, stempel)` (Freizeiten.Contracts, sucht ohne Mandantenfilter).
  - Gültig ist der Zugang nur, wenn der Link mit diesem Stempel existiert, der Schalter TeilnehmerZugang an ist und `jetzt < Freizeitende`.
  - Dann wird der Mandant gesetzt und der scoped `IQrGast { FreizeitId, GeraeteId }` (SharedKernel, Muster `IAngemeldet`) gefüllt.
  - In jedem anderen Fall gibt es **404, nie 401**.
  - Ein Endpunkt mit `NurQrGast` und einem weiteren Rechtemerkmal ist ein Konfigurationsfehler (`InvalidOperationException`).
- **Endpunkte:**

  | # | Route | Modul | Recht | Ergebnis |
  |---|---|---|---|---|
  | 1 | `GET /api/freizeiten/{freizeitId}/qr-zugang` | Freizeiten | `FreizeitRechte.Einstellungen` + `GehoertZuSchalter(TeilnehmerZugang)` | `200 QrZugangAnsicht(Adresse, Zeilen)` oder `200` mit `null`-Feldern, wenn noch kein Link existiert. Auch bei gelaufen. |
  | 2 | `POST …/qr-zugang/erneuern` | Freizeiten | wie #1, `IBenutzerCommand`+`IFreizeitCommand`, `FOR UPDATE` | `200 QrZugangAnsicht`. Gelaufen → `409 freizeit-abgeschlossen` |
  | 3 | `POST /api/teilnehmer/einloesen` `{token}` | Freizeiten | anonym, `.Zugriffsbegrenzt()`, Antiforgery | `204` + Gast-Cookie. Sonst `404` und `LinkEinloesungFehlgeschlagen` (ohne Token, AD-22) |
  | 4 | `GET /api/teilnehmer` | Freizeiten | NurQrGast | `200 TeilnehmerFreizeit(Name, ErsterTag, LetzterTag)` |
  | 5 | `GET /api/teilnehmer/infos` und `GET …/infos/{dokumentId:guid}` | Freizeiten | NurQrGast | Dokumente mit `ElternUndTeilnehmende` (Id, Titel, Dateiart) bzw. die Datei (Muster `ElterninfoZugriff`/`DokumentAnsicht.DateiAsync`) |
  | 6 | `GET /api/teilnehmer/programm` | Programm | NurQrGast | `200 TeilnehmerWoche(ErsterTag, LetzterTag, Punkte[Tag, Beginn, Titel])`. Liest nur, kein `MahlzeitenSicherstellen` |
  | 7 | `GET /api/teilnehmer/kanal` | Kanäle | NurQrGast | `200` neueste 200 Nachrichten in „Alle“ als `TeilnehmerNachricht(Id, Text, GesendetAm)`, ohne Autor |
  | 8 | `GET /api/teilnehmer/push/schluessel`, `POST …/push/abo`, `POST …/push/abo/abmelden` | Kanäle | NurQrGast | wie die Team-Routen aus 6-1 (gleicher Validator mit SSRF-Schutz), aber für `GastPushAbo` |
- **Nur lesen:** Jeder `NurQrGast`-Endpunkt ist ein GET. Ausnahme sind nur die zwei POSTs aus #8; ein Test in `EndpunktListeTests` prüft das. Die Gast-Antworten enthalten keine PersonId, keine Version und keine Übernahme-Daten. Nur #3 ist zugriffsbegrenzt, weil viele Kinder hinter einer IP im Lager-WLAN die Lese-Routen sonst sprengen.
- **Gast-Push (Kanäle):**
  - Eigene Entität `GastPushAbo` (OrganisationId, FreizeitId, GeraeteId, Endpunkt eindeutig je Freizeit, P256dh, Auth, AngelegtAm). Sie hat keine PersonId, deshalb gibt es keine Überschneidung mit Team-Abos. Abmelden löscht nach (GeraeteId, Endpunkt).
  - `Nachricht.Senden` merkt in „Alle“ zusätzlich `NachrichtGastPush(Id)` vor. Dessen `IPushAufbau` sendet an die Gast-Abos der Freizeit, aber nur wenn der Schalter an ist und `jetzt < Freizeitende` (`IFreizeitSchalterLesen`, `IFreizeitZeitpunkte`). Die URL ist `/app/teilnehmer`. Gleicher Inhalt wie beim Team, ungültige Ziele werden abgeräumt.
  - Empfänger `GastAbosBeenden : IEreignisEmpfaenger<…, FreizeitAbgeschlossen>` (kein `IBenutzerCommand`) löscht alle Gast-Abos **und** die Team-Abos dieser Freizeit. Das schließt den zurückgestellten Fund aus 6-1. Die Spine-Tabelle enthält das Paar schon.
  - Der Löschlauf löscht Gast-Abos mit den übrigen Kanal-Daten der Freizeit.
- **Web-App:**
  - Einstellungen: Card „Teilnehmer-Zugang“ (DESIGN.md QR-Code-Card) nur bei eingeschaltetem Modul. Sie erscheint nach dem Umschalten über `AutosaveAnkreuzfeld.Gespeichert` und zeigt:
    - Name der Freizeit, QR mittig schwarz auf Weiß (auch dunkel), den Link als Text;
    - die Knöpfe „Drucken“ (A4-Aushang über `NurBildschirm`/`DruckSeite`), „Link kopieren“ (neu `IBrowser.KopierenAsync`, Rückmeldung „Link kopiert.“) und „Link erneuern“ (`InlineBestaetigung`, Satz „Der bisherige QR-Code wird ungültig. Wer ihn schon nutzt, muss neu scannen.“);
    - ohne Link `LeerZustand` mit „QR-Code erzeugen“.
  - Neuer Baustein `QrCodeBild` in Ui.Komponenten (Parameter `IReadOnlyList<string> Zeilen`, `Bezeichnung`): ein `<svg>` mit einem `<path>` und `role="img"`, ohne `style=`.
  - Gastseiten mit `@layout TeilnehmerLayout` (AppRahmen und AppLeiste mit Freizeitname, ohne Konto-Link):
    - `/teilnehmer/{Token}`: Überschrift „Teilnehmer-Zugang“ und Knopf „Freizeit öffnen“. Erst der Knopf sendet #3 (AD-11: der Aufruf zeigt nur die Seite) und führt dann zu `/teilnehmer`.
    - `/teilnehmer`: Reiter „Woche / Infos / Alle“ (`?bereich=`).
      - Woche: Tag-Reiter am Handy, Spalten am Laptop, `Termin` ohne Href und Status.
      - Infos: Liste der Dokumente.
      - Alle: `KanalNachrichten` mit Name „Team“ und ohne Eingabefeld, dazu die Push-Card aus 6-1 als gemeinsames App-Fragment mit allen Hinweisen (iPhone, abgelehnt, nicht möglich).
    - Auf 404: „Diese Freizeit ist zu Ende.“ mit dem Zusatz „Wurde der QR-Code erneuert, hilft ein neuer Scan am Infobrett.“
    - `AppSitzung.AbgelaufenAsync` leitet auf Pfaden mit `teilnehmer` nie zur Anmeldung um.
  - Die Anmelden-Seite zeigt bei gültiger Gast-Sitzung (#4 = 200) eine Card „Teilnehmer-Zugang“ mit dem Knopf „Zum Teilnehmer-Zugang“. So kommen PWA-Starts vom Home-Bildschirm (start_url `/app/`) zurück.
- Testgetrieben (AD-30), HTTP-Tests gegen PostgreSQL:
  - #1/#2: Leitung 2xx, Team 403, ohne Rolle 404, fremde Organisation 404, Schalter aus 404, ohne Sitzung 401.
  - #3–#8 Token-Fälle: gültig; erneuertes Token und erneuerter Stempel (altes Cookie) → 404; Token mit anderem Zweck (Anmeldelink-Token) → 404; nach Freizeitende (`FakeTimeProvider`) → 404; Schalter aus → 404; Cookie für Freizeit X liest Y nicht (Y bekommt keine Daten, Antworten nur von X); ohne Gast-Cookie → 404; mit Team-Sitzung, aber ohne Gast-Cookie → 404; Gast an Team-Routen (`/api/freizeiten/{id}/kanaele/...`, `/programm`) → 401.

**Never:** Konto, Name oder Personendaten des Gasts; Programmdetails, Übernehmende, Material oder Aufgaben in der Gast-Ansicht; Kanal „Team“ für Gäste; Schreiben außer dem eigenen Push-Abo; Speichern des Klartext-Tokens im Cookie; QR-Erzeugung im Client oder per Fremdpaket in einem Client; neue Ereignis-Typen, Aktionsschlüssel, Problemcodes oder Spine-Änderung; Pop-ups; Commit auf `main`, Push.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehler |
|---|---|---|---|
| Erzeugen | Miriam #2, Modul an | `200 {adresse …/app/teilnehmer/<43 Zeichen>, zeilen[n×n]}`; #1 liefert dasselbe | – |
| Modul aus | #1/#2 | – | `404` |
| Einlösen | #3 mit gültigem Token | `204`, `Set-Cookie: __Host-Zeltlotse-Gast…; expires=<Freizeitende>`; #4–#7 `200` | – |
| Erneuert | #2 erneut | altes Token #3 `404`; altes Cookie #4 `404`; neues Token wirkt | – |
| Freizeitende | Uhr ≥ Ende des letzten Tages (00:00 Berlin) | #3 und #4–#8 `404` | – |
| Datenumfang | #6 mit übernommenem Programmpunkt, #7 mit Team-Nachricht | #6 nur Tag/Beginn/Titel; #7 nur „Alle“, keine `autorPersonId` | – |
| Push Gast | Gast-Abo, Tobias schreibt in „Alle“, Outbox zugestellt | Gast-Abo erhält Push mit URL `/app/teilnehmer`; in „Team“ kein Gast-Push | Schalter aus oder nach Ende: kein Gast-Push |
| Abschluss | `FreizeitAbgeschlossen` zugestellt | Gast- und Team-Abos dieser Freizeit gelöscht, andere Freizeit unberührt | – |
| Schreiben | Gast POST an andere Route | – | `401` (keine normale Sitzung) |
| Abo-Endpunkt fremd | #8 `http://…` | – | `400` am Feld `endpunkt` |

</intent-contract>

## Code Map

Wurzel: `src/` = S, `tests/` = T, Fk = `S/Modules/Freizeiten/Zeltlotse.Freizeiten`, Ka = `S/Modules/Kanaele/Zeltlotse.Kanaele`, SK = `S/Zeltlotse.SharedKernel`, Host = `S/Zeltlotse.Api`.

- Link-Muster:
  - `Fk/Domaene/Anmeldelink.cs` (Zweck L176-202, `Anlegen` L209, `Erneuern` L222).
  - `Fk/Freizeitseite/Anmeldelinks.cs`: `AnmeldelinkErzeugen` L17-35, `AnmeldelinkErneuernCommand`/Handler L56-110 (`FOR UPDATE`, Gelaufen), `AnmeldelinkSuche.FindenAsync` L119-155 (`IgnoreQueryFilters`, `mandant.Setzen`, `FreizeitZeiten.Freizeitende`).
  - EF-Mapping `Fk/Daten/FreizeitenDbContext.cs` L107-116. Migrationen `Fk/Migrationen/` (zuletzt `…_Module`).
  - `SK/Zugangslinks/Zugangslink.cs` und `LinkSchutz.cs`, `SK/Web/OeffentlicheAdresse.cs`, `SK/Web/Zugriffsbegrenzung.cs`.
- Schalter und Zeiten:
  - `Fk/Domaene/Freizeit.cs` (`Schalter()` L266) und `Fk/Domaene/FreizeitZeiten.cs` (internal, `Freizeitende` L28).
  - Von außen: `Freizeiten.Contracts/Zeitpunkte/IFreizeitZeitpunkte`, `Schalter/IFreizeitSchalterLesen` (Umsetzung `Fk/Rechte/FreizeitRechte.cs` L182) und `IFreizeitTageLesen`.
- Dokumente: `Fk/Dokumente/ElterninfoEndpunkte.cs` (`ElterninfoZugriff` L51-76 `Sichtbar`, Datei über `DokumentAnsicht.DateiAsync`, Gast-Endpunkt L86) und `Fk/FreizeitenModul.cs` L96-117 (anonyme Gruppen, Routen der Einstellungen).
- Abschluss: `Fk/Abschluss/FreizeitAbschliessen.cs`, `Freizeiten.Contracts/Events/FreizeitAbgeschlossen.cs`. Vorbild für den Empfänger: `S/Modules/Sicherheit/Zeltlotse.Sicherheit/SicherheitModul.cs` L31 und `Vorfaelle/VorfaelleBearbeiten.cs` L152-160.
- Rechte und Sitzung:
  - `SK/Rechte/Rechtemerkmale.cs`, `SK/Anmeldung/Sitzung.cs` (`IAngemeldet`).
  - `Host/Rechte/RechtePruefung.cs`: L28-101, Konfigurationsfehler L37/43, Schalter L96.
  - `Host/Sicherheit/SitzungsRegistrierung.cs` (Schema, Cookie, `OnValidatePrincipal` L42), `Host/Sicherheit/CookieRichtlinie.cs` (`__Host-` Pflicht), `Host/Sicherheit/WebSicherheit.cs` L32-37 (scoped Stände) und L77 (Middleware-Reihenfolge), `Host/Sicherheit/AntiforgeryZwang.cs`.
  - Vorbild für den Tausch Token → Cookie: `S/Modules/Konten/Zeltlotse.Konten/Anmelden/AnmeldeEndpunkte.cs` L73-103 und `Sitzungen/SitzungAusstellen.cs`. Sicherheitsprotokoll: `SK/Sicherheitsprotokoll/ISicherheitsprotokoll.cs`.
- Programm: `S/Modules/Programm/Zeltlotse.Programm/Programme/WochenrasterLaden.cs` (L19, Hilfe `ProgrammLesen.Sortiert` L38-69, das Schreiben über `MahlzeitenSicherstellen` **nicht** übernehmen), `ProgrammModul.cs` L47. Pfeil Programm → Freizeiten erlaubt (`T/Zeltlotse.ArchitectureTests/Loesung.cs` L24-43).
- Kanäle:
  - `Ka/KanaeleModul.cs` L29-61.
  - `Ka/Push/PushAbo.cs` und `PushAboSlices.cs` (Validator `PushEndpunkte.IstErlaubt`, Längen 65/16, Endpunkte L124-150, `AboSpeichernAsync` `ON CONFLICT` in `Ka/Daten/KanaeleDbContext.cs` L34-44/85-95).
  - `Ka/Nachrichten/Nachricht.cs` (`Senden` → `PushVormerken`), `NachrichtPush.cs` (`NachrichtPushAufbau` L205-223, `NachrichtPushText.Url`), `NachrichtenLaden.cs`.
  - `Ka/Datenschutz/KanaeleDatenschutz.cs` L17-35 (Empfänger-Muster) und `KanaeleLoeschlauf.cs`. Migrationen `Ka/Migrationen/` (zuletzt `…_Notizen`).
- Api.Contracts: `S/Zeltlotse.Api.Contracts/Freizeiten/` (`Dokumente.cs` `DokumentSichtbarkeit`, `Module.cs`), `Programm/Programm.cs`, `Kanaele/Kanaele.cs`. Jedes Modul schreibt nur in seinen Ordner.
- Client:
  - `S/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor`: Cards Anmeldelinks L132-152, Module L154-168, Laden L189-242, Erneuern L264-303.
  - `Features/Freizeiten/Notfallordner.razor`: Druckmuster L25-54/127.
  - `Features/Programm/Wochenraster.razor`: `Reiter` und `TagesSpalten` L62-66, `TagWaehlen` L191-200.
  - `Features/Kanaele/Kanaele.razor`: Push-Card L55-95, `PushPruefenAsync` L201, `EinschaltenAsync` L298, `AusschaltenAsync` L326, Beobachten. Dazu `KanaeleTexte.cs`.
  - `Layout/MainLayout.razor`, `Sitzung/AppSitzung.cs` L66-77, `Browser/IBrowser.cs`, `JsBrowser.cs`, `wwwroot/js/zeltlotse.js` (Exports L57/62). Die Anmelden-Seite steht unter `Features/Konten/`.
- Ui:
  - `S/Zeltlotse.Ui/Api/FreizeitenApiClient.cs`, `KanaeleApiClient.cs`, `ProgrammApiClient.cs`, `UiRegistrierung.cs`, `Transport/SitzungsHandler.cs`.
  - In Ui.Komponenten: `Termin`, `Reiter`, `TagesSpalten`, `KanalNachrichten`, `DruckSeite`, `NurBildschirm`, `InlineBestaetigung`, `AppRahmen`, `AppLeiste`, `Balken.razor` (SVG-Muster ohne style). CSS `wwwroot/css/zeltlotse.css` (Druckblock L240-255).
- Tests:
  - `T/Zeltlotse.Api.Tests/FreizeitseiteTests.cs`: Token-Fälle L196/223/285/300/318/549.
  - `FreizeitpassSzenario.cs` (X/Z, Miriam, Tobias, Andrea, Lea), `AnmeldeHelfer.cs` (Uhr = Echtzeit, `SendenAsync` mit Antiforgery), `KanaeleTests.cs` (`PushAufzeichnung`), `AbschlussTests.cs`, `DemoDatenTests.cs` L282-290 (Schalter-Erwartung ändert sich).
  - `EndpunktListeTests.cs`: Anonym-Liste L24-44, Freizeit-Routen L190-362, Kanäle-Zähler L381, 401-Test L432.
  - `T/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs` (Einstellungen ab L733), `KanaeleSeitenTests.cs`, `NachgebildeterBrowser.cs`, `AppLayoutTests.cs`, `KomponentenTests.cs`.
  - `T/Zeltlotse.ArchitectureTests/DokuRegeln.cs` (`PflichtSeiten`), `DokuTests.cs` (feste Fehlliste), `LoeschPflicht*`, `EreignisFlussTests.cs` (erwartet `Kanaele` schon als Ziel).
- Demo und Doku:
  - `Host/Demo/DemoDaten.cs`: `ModuleAsync` L604, Links in `FreizeitseiteAsync` L507-515. Dazu `DemoKatalog.cs` L29-31.
  - `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: `Freizeit_Einstellungen` L673-682 zählt exakt 2 „Freizeitseite öffnen“ und 2 „Neuen Link erzeugen“; `FreizeitseiteAsync` L2804 (zweiter Kontext ohne Sitzung); `BildAsync` L2871.
  - Doku-Seiten unter `doku/src/content/docs/`: `leitung/module-schalten.md` (L12-41 „kommt später“), `leitung/index.md` L25, `mitarbeitende/kanaele.md` L13/92, `mitarbeitende/programm.md` L95, `daten.md` L90-98, `eltern/index.md`.

## Tasks & Acceptance

**Execution (testgetrieben: zuerst der Test, dann der Code):**
- `T/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs` (neu): alle Fälle aus Matrix und Always. Push über `PushAufzeichnung` und `OutboxJetztZustellenAsync`, Abschluss als Durchlauf über die Outbox, Sicherheitsprotokoll bei gescheitertem Einlösen, Cookie-Attribute aus dem `Set-Cookie`. Dazu `EndpunktListeTests` anpassen: Anonym-Liste, Freizeit-Routen, Zähler, die Regel „NurQrGast nur GET außer #8“ und #3 zugriffsbegrenzt.
- `Directory.Packages.props`, `Fk/Zeltlotse.Freizeiten.csproj`: QRCoder.
- `Fk/Domaene/QrZugang.cs`, `Fk/TeilnehmerZugang/*.cs` (Erzeugen/Erneuern, Ansicht #1, Einlösen #3, #4, #5, `QrZugangLesen : IQrZugangLesen`), `Fk/Daten/FreizeitenDbContext.cs`, Migration `QrZugang`, `FreizeitenModul.cs`, `Freizeiten.Contracts/TeilnehmerZugang/IQrZugangLesen.cs`.
- SK: `Rechte/Rechtemerkmale.cs` (`NurQrGast`, `NurQrGastMetadaten`), `Rechte/IQrGast.cs`, `Anmeldung/GastSitzung.cs` (Schema- und Claim-Namen). Host: `Rechte/RechtePruefung.cs`, `Rechte/QrGastKontext.cs`, `Sicherheit/SitzungsRegistrierung.cs` (zweites Schema), `Sicherheit/WebSicherheit.cs`. Dazu ein SharedKernel-Test für den Konfigurationsfehler, falls das Muster dort liegt.
- `S/Modules/Programm/Zeltlotse.Programm/Teilnehmer/TeilnehmerWocheLaden.cs` + `ProgrammModul.cs`.
- Ka: `Push/GastPushAbo.cs`, `Push/GastPushSlices.cs`, `Nachrichten/NachrichtGastPush.cs`, `Nachrichten/TeilnehmerKanalLaden.cs`, `Datenschutz/GastAbosBeenden.cs`, `KanaeleLoeschlauf.cs`, `KanaeleDbContext.cs`, Migration `GastPush` und `KanaeleModul.cs`.
- `S/Zeltlotse.Api.Contracts/{Freizeiten,Programm,Kanaele}/Teilnehmer*.cs`: die Datenformen der Matrix.
- `S/Zeltlotse.Ui/Api/TeilnehmerApiClient.cs` (alle `/api/teilnehmer`-Aufrufe), `FreizeitenApiClient` (#1/#2) und `UiRegistrierung.cs`.
- `S/Zeltlotse.Ui.Komponenten/QrCodeBild.razor` und CSS (Card-Fläche weiß, Aushang-Größe im Druck) mit bUnit-Test (Pfad aus Zeilen, `role="img"`, `aria-label`).
- App: `Features/Freizeiten/Einstellungen.razor` (Card), `Features/Teilnehmer/{TeilnehmerEinloesen,TeilnehmerStart}.razor`, `TeilnehmerTexte.cs`, `Layout/TeilnehmerLayout.razor`, `Features/Kanaele/PushCard.razor` (aus `Kanaele.razor` herausgezogen, mit Pfaden für Team oder Gast), `Sitzung/AppSitzung.cs`, die Anmelden-Seite, `IBrowser`/`JsBrowser`/`zeltlotse.js` `KopierenAsync`, `NachgebildeterBrowser`. Dazu bUnit-Tests in `T/Zeltlotse.Ui.Tests/TeilnehmerSeitenTests.cs` und `FreizeitenSeitenTests`: Card nur bei Modul an, Drucken, Kopieren, Erneuern, 404-Text, keine Umleitung zur Anmeldung.
- `Host/Demo/DemoDaten.cs`, `DemoKatalog.cs`, `DemoDatenTests`:
  - Zeltlager mit Teilnehmer-Zugang an und QR-Link.
  - Die Bilder und Texte zu `module-schalten` nachziehen.
- `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`, jeweils Handy und Laptop mit axe:
  - `teilnehmer-zugang-qr` (Einstellungen-Card, Leitung Jonas);
  - `teilnehmer-woche`, `teilnehmer-infos`, `teilnehmer-alle` (zweiter Kontext ohne Sitzung: Link aus der Card, „Freizeit öffnen“).
- Doku: `doku/src/content/docs/leitung/teilnehmer-zugang.md` (neu)
  - Inhalt: einschalten, QR drucken und kopieren, erneuern; was Teilnehmende sehen und was nicht; Push und iPhone; Ende mit der Freizeit.
  - Links nachziehen in `leitung/index.md`, `leitung/module-schalten.md`, `mitarbeitende/kanaele.md`, `mitarbeitende/programm.md` und `eltern/index.md` (Hinweis für Eltern).
  - `daten.md`: Abschnitt Teilnehmer-Zugang (Cookie ohne Name, Geräte-Kennung, Gast-Abo endet mit der Freizeit).
  - `DokuRegeln.PflichtSeiten` und die Fehlliste in `DokuTests`.

**Acceptance Criteria:**
- Gegeben die Demo-Freizeit mit eingeschaltetem Teilnehmer-Zugang: Wird der Link aus der QR-Card in einem Browser ohne Konto geöffnet und „Freizeit öffnen“ getippt, dann zeigt die Seite die Wochenübersicht mit Zeiten und Titeln, unter „Infos“ den Infobrief und unter „Alle“ die Nachrichten ohne Eingabefeld, ohne jede Umleitung zur Anmeldung.
- Gegeben die Einstellungen am Handy: Tippt die Leitung „Drucken“, dann ruft die Seite den Druck auf, und im Druckbild stehen nur Freizeitname, QR-Code und Link.
- Gegeben die fertige Story: `dotnet test` inklusive der Architekturtests ist grün, die Screenshots `teilnehmer-*-handy.png` und `-laptop.png` sind erzeugt, und `npm run build` in `doku/` läuft fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: 31 findings — high 0, medium 7, low 19, false 4, maybe-false 1
- findings:
  - `[medium]` `[patch]` Blind: „Link erneuern“ stoppt den Push an alte Gast-Abos nicht, obwohl Spec und Doku das versprechen — Stempel am GastPushAbo gespeichert; der Gast-Push schreibt nur Abos mit dem aktuellen Stempel des QR-Links an. Test: erneuern, senden → kein Gast-Push.
  - `[maybe-false]` `[defer]` Blind: Auf dem iPhone hat die Web-App vom Home-Bildschirm einen eigenen Cookie-Speicher, deshalb kommt der Gast dort nie an Push — Klären ließe sich das nur am echten Gerät (übernimmt iOS beim Hinzufügen die Safari-Cookies?). Wenn ja: medium, unverifiziert.
  - `[low]` `[reject]` Blind: Ein Browser scannt nacheinander zwei Freizeiten, das alte Gast-Abo bleibt bis zum Ende der ersten Freizeit — Selten; die Korrektur bräuchte Abo-Verwaltung über Freizeiten hinweg. Es endet ohnehin mit dem Freizeitende.
  - `[low]` `[patch]` Blind: Bei ausgeschaltetem Modul sagt die Seite „zu Ende … neuer Scan hilft“ — Der Zusatz nennt jetzt auch das Ausschalten durch die Leitung.
  - `[low]` `[reject]` Blind: Mahlzeiten fehlen in der Wochenübersicht, bis das Team einmal den Wochenraster öffnet — Das Team plant das Programm vor der Freizeit. Würde das Lesen Mahlzeiten anlegen, schriebe ein Gast-GET; das verbietet die Spec.
  - `[low]` `[reject]` Blind: Die Card zeigt nach dem Freizeitende keinen Hinweis „beendet“, Erneuern bleibt nach dem Abschluss sichtbar — Betrifft nur die Zeit nach der Freizeit. Erneuern endet mit 409 und dem bekannten Text. Ein eigener Zustand bräuchte neue Zweige.
  - `[low]` `[reject]` Blind: Der Aushang erklärt nicht, wofür der Code ist — Die Spec legt fest: Im Druckbild stehen nur Freizeitname, QR-Code und Link.
  - `[medium]` `[patch]` Blind: Die Zugriffsbegrenzung beim Einlösen trifft viele Kinder hinter einer IP, und die Seite zeigt dann nur einen allgemeinen Fehler — Die Begrenzung verlangt AD-25. Der Client zeigt bei 429 jetzt „Gerade wollen viele gleichzeitig hinein. Bitte in einer Minute noch einmal tippen.“
  - `[low]` `[reject]` Blind: Der Ablauf des Cookies folgt keiner späteren Datumsänderung der Freizeit — Selten (nur wenn das Ende während der Freizeit nach hinten verschoben wird). Ein neuer Scan hilft.
  - `[medium]` `[patch]` Blind: Der Sichtbarkeitsfilter und der Download unter Infos sind per HTTP ungetestet — Test ergänzt: Eltern-und-Teilnehmende-Dokument 200 mit Bytes, Team-Dokument und Dokument von Z 404.
  - `[low]` `[defer]` Blind: Die Fehlerzweige der QR-Card (Kopieren scheitert, 409, 404) sind nicht getestet — wie Verification-Gap 4. Nur Rückmeldetexte, sonst gute Abdeckung.
  - `[low]` `[reject]` Blind: Die Anmelden-Seite fragt bei jedem Besuch `GET /api/teilnehmer` — Eine Anfrage mit 404 schadet nicht. Ein Erkennen des Standalone-Modus wäre ein weiterer Zweig.
  - `[low]` `[patch]` Blind: Der Kommentar an GastPushAbo nennt das Erneuern nicht — mit dem ersten Fund korrigiert.
  - `[medium]` `[patch]` Edge: Einlösen hinter einer NAT mit mehr als 20 Kindern je Minute ergibt 429 — gleiche Ursache wie der Blind-Fund zur Zugriffsbegrenzung, gleicher Patch.
  - `[low]` `[patch]` Edge: Der Client behandelt 429 beim Einlösen nicht eigens — gleicher Patch.
  - `[low]` `[reject]` Edge: Erneuern in der Nachbereitung erzeugt einen QR-Code, der nie gilt — gleiche Gruppe wie „Card ohne Hinweis beendet“. Nach der Freizeit druckt niemand einen Aushang.
  - `[low]` `[reject]` Edge: GET qr-zugang nach dem Freizeitende zeigt einen toten Code ohne Hinweis — gleiche Gruppe.
  - `[low]` `[patch]` Edge: Dasselbe Gerät mit Team- und Gast-Abo bekommt jede „Alle“-Nachricht doppelt — Gast-Ziele mit einem Endpunkt, der schon als Team-Abo derselben Freizeit besteht, fallen weg. Test ergänzt.
  - `[low]` `[reject]` Edge: Nach einem neuen Scan (neue Geräte-Kennung) und einem gescheiterten Neuspeichern löscht „ausschalten“ das alte Abo nicht — Selten. Das Abo endet mit dem Freizeitende oder beim nächsten 410.
  - `[low]` `[reject]` Edge: Ein 5xx bei `GET qr-zugang` verdeckt die ganze Einstellungsseite — Das gleiche gemeinsame Laden gilt für alle Cards der Seite. Ein Fehler ist selten, und ein eigenes try wäre ein Sonderzweig.
  - `[medium]` `[patch]` Verification-Gap: Der Gast-Download unter `infos/{dokumentId}` ist ungetestet — gleiche Ursache wie der Blind-Fund zu Infos, gleicher Test.
  - `[low]` `[patch]` Verification-Gap: Die Tagwahl über `?tag=` ist ungetestet — bUnit-Fakt ergänzt (gültiger Tag, Rückfall).
  - `[low]` `[patch]` Verification-Gap: Die stille Erneuerung von PushCard beim Freizeitwechsel ist ungetestet — bUnit-Fakt ergänzt (neu rendern mit anderer Freizeit, zweiter POST).
  - `[low]` `[defer]` Verification-Gap: Die Zweige 409, 404 und gescheitertes Kopieren der QR-Card sind ungetestet — so vom Prüfer vorgeschlagen; geringe Wirkung.
  - `[false]` `[reject]` Intent: „Falsche Rolle 403“ fehlt an den Gast-Routen — Gast-Routen kennen keine Rolle. Die Spec verlangt dort 404 statt 401; der 403-Fall ist an #1/#2 getestet.
  - `[medium]` `[patch]` Intent: Für `infos/{dokumentId}` fehlt der 404-Fall für ein Dokument einer fremden Freizeit oder Organisation — mit dem Infos-Test abgedeckt (Dokument von Z).
  - `[medium]` `[patch]` Intent: Der Weg Dokument → Download ist nur per E2E und bUnit belegt — gleicher Test.
  - `[false]` `[reject]` Intent: „Test vor dem Code“ lässt sich am Diff nicht zeigen — Keine Aussage über einen Fehler; die Tests liegen vor.
  - `[false]` `[reject]` Intent: Dass Doku-Build und Komplettlauf durchgelaufen sind, zeigt der Diff nicht — Belegt die Verifikation unter Auto Run Result.
  - `[low]` `[reject]` Intent: Kein Screenshot von 404-Seite, Druckbild und Anmelden-Card — Laut Design Notes per bUnit und HTTP belegt. Die E2E-Uhr ist nicht einstellbar.
  - `[false]` `[reject]` Intent: Zusätze über die Kurzbeschreibung hinaus (Team-Abos beim Abschluss löschen, Anmelden-Card, Begrenzung nur beim Einlösen) — Steht so in der Spec; keine Abweichung.

### 2026-10-21 — Review pass (Folgedurchlauf)
- verdicts: 25 findings — high 0, medium 1, low 16, false 7, maybe-false 1
- findings:
  - `[maybe-false]` `[defer]` Blind: Die Web-App vom iPhone-Home-Bildschirm sieht das Gast-Cookie aus Safari nicht, der Gast kommt dort nicht an Push — carried: bereits zurückgestellt (deferred), Klärung nur am echten Gerät.
  - `[low]` `[reject]` Blind: Ein Gerät hält nur eine Gast-Sitzung; das alte Gast-Abo einer ersten Freizeit läuft weiter und lässt sich nicht ausschalten — carried: wie „Ein Browser scannt nacheinander zwei Freizeiten“; selten, endet mit dem Freizeitende.
  - `[low]` `[reject]` Blind: Der Cookie-Ablauf bleibt beim Einlösen stehen und folgt keiner späteren Datumsänderung — carried: selten, ein neuer Scan hilft.
  - `[medium]` `[defer]` Blind: Die Zugriffsbegrenzung teilt ein Budget je IP mit Login und Einladungen; eine Scan-Welle sperrt auch die Leitung im selben WLAN — bestätigt (eine Richtlinie, Partition nur nach IP, 20 je Minute, auch GET antiforgery zählt). Die Teilung ist vorbestehend (AD-25); die Abhilfe ist eine Entscheidung an AD-25. Zurückgestellt, Ausweg im Betrieb: AnfragenJeMinute.
  - `[low]` `[reject]` Blind: Jedes gescheiterte Einlösen schreibt ins Sicherheitsprotokoll, auch Ende, Schalter aus und erneuerter Link — Die Spec verlangt `LinkEinloesungFehlgeschlagen` für jeden Fehlschlag (AD-22), wie beim Anmeldelink; eine Änderung wiche von der Spec ab.
  - `[low]` `[reject]` Blind: Der Ende-Text deckt nicht jeden 404-Grund genau ab — Den Text legt die Spec fest, der Zusatz nennt schon Erneuern und Ausschalten (Patch des ersten Durchgangs). Eine Unterscheidung bräuchte einen eigenen Status, den die Design Notes bewusst ausschließen.
  - `[low]` `[patch]` Blind: Die Leitung kann Programmpunkte nicht vor Teilnehmenden verbergen; auch Team-Punkte und Überraschungen stehen mit Titel in der Woche — Das Verhalten folgt der Spec (Zeiten und Titel). Gepatcht ist die fehlende Warnung: `leitung/teilnehmer-zugang.md` hat jetzt den Absatz „Vor dem Einschalten prüfen“.
  - `[low]` `[patch]` Blind: Niemand erfährt, dass mit dem Einschalten auch die bisherigen Nachrichten in „Alle“ lesbar werden — gleiche Ursache (Sichtbarkeit nicht dokumentiert), gleicher Doku-Absatz (die neuesten 200, auch von vor dem Einschalten).
  - `[low]` `[reject]` Blind: Erzeugen und Erneuern des QR-Links schreiben nichts ins Sicherheitsprotokoll — Gleich wie beim Erneuern des Anmeldelinks; ein neuer Protokolltyp ist laut Spec nicht vorgesehen (keine neuen Ereignis-Typen).
  - `[false]` `[reject]` Blind: Der Name `GastAbosBeenden` verdeckt, dass auch Team-Abos gelöscht werden — Name und Verhalten verlangt die Spec ausdrücklich; die Doku (`freizeit-abschliessen`, `daten.md`) nennt das Löschen aller Abos.
  - `[low]` `[reject]` Blind: Zwei Abfragen je Gast-Anfrage und doppelte `GET /api/teilnehmer` je Seitenaufruf — bei Lager-Größen ohne belegten Schaden; Zusammenlegen wäre reine Optimierung.
  - `[low]` `[reject]` Edge: Ein Gerät mit Team-Abo, dessen Person kein Empfänger des Team-Push ist, bekommt auch keinen Gast-Push — selten (eigene Nachricht des Autors, ehemaliges Team-Mitglied im selben Browser); die Korrektur bräuchte die Empfängerliste des Team-Push im Gast-Aufbau.
  - `[low]` `[reject]` Edge: „Benachrichtigungen ausschalten“ auf der Gastseite beendet das Browser-Abo, das ein Team-Abo mitnutzt — selten (Team-Mitglied scannt den Aushang im selben Browser); ein Schutz bräuchte einen neuen Zweig in `PushCard`.
  - `[low]` `[reject]` Edge: Erneuern in der Nachbereitung erzeugt einen QR-Code, der nie gilt — carried.
  - `[low]` `[reject]` Edge: Lässt sich `TokenGeschuetzt` nicht entschlüsseln, endet `GET qr-zugang` mit 500 — gleich wie beim Anmeldelink (`AnmeldelinkErzeugen.Token`); ein verlorener Schlüsselring ist ein Betriebsfehler für alle Links.
  - `[low]` `[reject]` Edge: Liefert Erneuern 403/404 (Modul auf einem anderen Gerät ausgeschaltet), verschwindet die Card ohne Meldung — selten; der nächste Seitenaufruf zeigt den richtigen Stand.
  - `[low]` `[defer]` Verification-Gap: Die Zweige 409, „Kopieren scheitert“ und die Erfolgsmeldung der QR-Card sind per bUnit ungetestet — carried: bereits zurückgestellt, kein neuer Eintrag.
  - `[low]` `[patch]` Verification-Gap: Die Grenze von 200 Nachrichten im Gast-Kanal ist ungetestet — Test `Kanal_hoechstens_die_neuesten_200_aus_Alle_aelteste_zuerst` ergänzt (205 Nachrichten, erwartet Nr. 6 bis 205 in Reihenfolge); grün.
  - `[false]` `[reject]` Intent: `IQrGast`, `IQrZugangLesen` und `GastPushAbo` tragen über die Spec hinaus einen Stempel — dient „Der Zugang endet beim Erneuern“ auch für Push (Patch des ersten Durchgangs); die Spec verbietet keine weiteren Felder, kein Schaden.
  - `[false]` `[reject]` Intent: Der Gast-Push überspringt Geräte mit Team-Abo, das steht nicht in der Spec — bewusster Patch des ersten Durchgangs gegen doppelte Pushes; der Randfall ist oben unter Edge bewertet.
  - `[low]` `[patch]` Intent: Nach dem Wieder-Einschalten des Moduls gilt ein altes Cookie wieder (statt „endet“) — Das Verhalten folgt der Gültigkeitsregel der Spec (Stempel, Schalter an, vor dem Ende, bei jeder Anfrage geprüft). Gepatcht ist die Doku: „Ende mit der Freizeit“ sagt jetzt, dass der alte Code nach dem Wieder-Einschalten wieder gilt und nur Erneuern endgültig sperrt.
  - `[false]` `[reject]` Intent: `IstTeilnehmerPfad` prüft den Anfang des Pfads statt „enthält“ — Der Präfix ist die gemeinte Lesart (Gastseiten liegen unter `/teilnehmer`); kein anderer Pfad ist betroffen.
  - `[false]` `[reject]` Intent: Der 404-Text hat einen dritten Satz — bewusster Patch des ersten Durchgangs (Ausschalten), widerspricht dem Spec-Text nicht.
  - `[false]` `[reject]` Intent: „Cookie für X liest Y nicht“ ist nur über den Inhalt geprüft — Gast-Routen haben keine Freizeit im Pfad; ein Zugriff auf Y lässt sich gar nicht formulieren, der Inhaltstest ist die richtige Oberfläche.
  - `[false]` `[reject]` Intent: Demo-Daten mit Teilnehmer-Zugang liegen außerhalb der Intent — Definition of Done und Aufruf verlangen Demo-Daten für die neue Oberfläche.

## Design Notes

- **QR auf dem Server:** Die Paketregel verbietet Fremdpakete in Clients. Ein eigener Encoder (Reed-Solomon, Masken) wäre fehleranfällig und kaum prüfbar. Modul-Projekte dürfen Pakete nutzen (z. B. MailKit, Lib.Net.Http.WebPush). Die Matrix als Zeilen ist eine Datenform; das SVG baut `QrCodeBild` ohne CSP-Konflikt.
- **Eigenes Cookie-Schema, nie Default:** So bleiben die normale Sitzung, `IKontextRollen` und die Antiforgery-Bindung unverändert. Für alle bestehenden Endpunkte ist der Gast anonym (401). Nur `NurQrGast` liest das Gast-Cookie.
- **Stempel statt Token im Cookie:** Erneuern setzt einen neuen Stempel, deshalb verlieren alte Cookies sofort ihre Wirkung (AD-11 „alter Link ungültig“). Ein Schalter oder die Gültigkeit stehen nie im Cookie, sie werden bei jeder Anfrage geprüft (AD-20).
- **404 statt „zu Ende“-Status:** AD-30 verlangt 404 für abgelaufene Tokens. Ein eigener Status bräuchte Speziallogik. Die Gastseite deutet jedes 404 mit dem Satz aus EXPERIENCE.md und dem Hinweis auf den neuen Scan.
- **Ende = Freizeitende, nicht Abschluss:** Wie beim Anmeldelink endet der Zugang um 00:00 Berlin nach dem letzten Tag. Der Abschluss räumt danach nur noch die Abos ab.
- **Team-Abos beim Abschluss löschen:** Nach *gelaufen* kann niemand mehr senden. Das Löschen ist Datensparsamkeit und schließt den Fund aus 6-1, ohne neues Ereignis.
- **Kein „Zu Ende“-Screenshot:** Die Demo-Freizeit liegt in der Zukunft, und der E2E-Host hat keine künstliche Uhr. Der Text ist per bUnit und HTTP-Test belegt.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx` -- 0 Fehler
- `dotnet test --project tests/Zeltlotse.Api.Tests` mit Filter auf `TeilnehmerZugang|EndpunktListe|Kanaele|DemoDaten`, `tests/Zeltlotse.Ui.Tests`, `tests/Zeltlotse.SharedKernel.Tests`, `tests/Zeltlotse.ArchitectureTests` -- grün während der Umsetzung
- Am Ende einmal `dotnet test` (ganze Lösung inkl. E2E/Doku) -- grün, die neuen PNGs liegen in `doku/src/assets/screenshots/`. Danach `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

Status: done

**Umgesetzt:** Teilnehmer-Zugang per QR-Code (CAP-38, FR-64, AD-11, AD-12, AD-21).
- **QR-Link (Freizeiten):** Entität `QrZugang` (Hash, verschlüsseltes Token, Stempel; Muster Anmeldelink), QR-Matrix mit QRCoder auf dem Server (Fehlerkorrektur M, mit Ruhezone). `GET/POST …/qr-zugang[/erneuern]` nur für die Leitung und nur bei eingeschaltetem Modul.
- **Gast-Sitzung:** Einlösen per `POST /api/teilnehmer/einloesen` (anonym, zugriffsbegrenzt, Sicherheitsprotokoll bei Fehlschlag). Das Gast-Cookie `__Host-Zeltlotse-Gast` hat ein eigenes Schema (nie Default), gilt bis zum Freizeitende und trägt nur Freizeit, Geräte-Kennung und Stempel. Rechtemerkmal `.NurQrGast()`: der Host prüft bei jeder Anfrage Stempel, Schalter und Freizeitende, sonst 404.
- **Gast-Lesen:** Wochenübersicht (Programm: nur Tag, Beginn, Titel), Infos und Datei-Download (Freizeiten: nur „Eltern und Teilnehmende“), Kanal „Alle“ ohne Autor (Kanäle).
- **Gast-Push:** Entität `GastPushAbo` mit Stempel. Gast-Push für „Alle“ an `/app/teilnehmer`, nur mit aktuellem Stempel, eingeschaltetem Schalter und vor dem Freizeitende; Geräte, die schon ein Team-Abo der Freizeit haben, bekommen ihn nicht doppelt. Der Empfänger `GastAbosBeenden` für `FreizeitAbgeschlossen` löscht die Gast- und Team-Abos der Freizeit (schließt den zurückgestellten Fund aus 6-1). Der Löschlauf erfasst Gast-Abos.
- **Web-App:**
  - Card „Teilnehmer-Zugang“ in den Einstellungen (QR, Drucken als A4-Aushang, Link kopieren, Link erneuern).
  - Gastseiten `/teilnehmer/{token}` und `/teilnehmer` (Woche / Infos / Alle, Push-Card, Ende-Text, 429-Hinweis), eigenes Layout, keine Umleitung zur Anmeldung, Karte „Zum Teilnehmer-Zugang“ auf der Anmelden-Seite.
  - `PushCard` aus Kanäle herausgezogen; `IBrowser.KopierenAsync`.
- **Demo und Doku:** Das Demo-Zeltlager hat den Teilnehmer-Zugang an und einen QR-Link. Neue Seite `leitung/teilnehmer-zugang.md`; nachgezogen `module-schalten`, `leitung/index`, `freizeit-abschliessen`, `mitarbeitende/kanaele`, `mitarbeitende/programm`, `eltern/index`, `daten.md`. Acht neue Screenshots (`teilnehmer-zugang-qr`, `-woche`, `-infos`, `-alle`, jeweils Handy und Laptop). `module-schalten-*` wurde bewusst neu erzeugt.

**Dateien (Auswahl):**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/{Domaene/QrZugang.cs,TeilnehmerZugang/*,Migrationen/*_QrZugang*}` und `Freizeiten.Contracts/TeilnehmerZugang/IQrZugangLesen.cs`: QR-Link, Einlösen, Gast-Endpunkte, Prüfung.
- `src/Zeltlotse.SharedKernel/{Anmeldung/GastSitzung.cs,Rechte/IQrGast.cs,Rechte/Rechtemerkmale.cs}`, `src/Zeltlotse.Api/{Rechte/RechtePruefung.cs,Rechte/QrGastKontext.cs,Sicherheit/SitzungsRegistrierung.cs}`: Gast-Schema und Rechteprüfung.
- `src/Modules/Programm/Zeltlotse.Programm/Teilnehmer/TeilnehmerWocheLaden.cs`: Wochenübersicht für Gäste.
- `src/Modules/Kanaele/Zeltlotse.Kanaele/{Push/GastPush*,Nachrichten/NachrichtGastPush.cs,Nachrichten/TeilnehmerKanalLaden.cs,Datenschutz/GastAbosBeenden.cs,Migrationen/*_GastPush*}`: Gast-Abos, Gast-Push, Abschluss.
- `src/Zeltlotse.Api.Contracts/{Freizeiten,Programm,Kanaele}/Teilnehmer*.cs`, `src/Zeltlotse.Ui/Api/TeilnehmerApiClient.cs`, `src/Zeltlotse.Ui.Komponenten/QrCodeBild.razor`.
- `src/Zeltlotse.App/Features/{Teilnehmer/*,Kanaele/PushCard.razor,Freizeiten/Einstellungen.razor,Konten/Anmelden.razor}`, `Layout/TeilnehmerLayout.razor`, `Sitzung/AppSitzung.cs`.
- Tests: neu `TeilnehmerZugangTests` (20) und `TeilnehmerSeitenTests`; erweitert `EndpunktListeTests`, `DemoDatenTests`, `FreizeitenSeitenTests`, `KanaeleSeitenTests`, `KomponentenTests`, `AnmeldeSeitenTests`, `DokuRegeln`/`DokuTests` und die E2E-Doku-Screenshots.

**Review:** 31 Funde (medium 7, low 19, false 4, maybe-false 1); Details im Triage-Log.
- **Patches**, nach Ursache gruppiert:
  - medium:
    - nach dem Erneuern kein Push mehr an alte Gast-Abos (Stempel am Abo);
    - eigene Meldung bei 429 beim Einlösen;
    - HTTP-Test für den Infos-Filter und den Download.
  - low:
    - Ende-Text deckt auch das Ausschalten ab;
    - kein doppelter Push an ein Gerät mit Team- und Gast-Abo;
    - Kommentar an `GastPushAbo`;
    - bUnit-Tests für `?tag=` und für die stille Erneuerung beim Freizeitwechsel.
- **Zurückgestellt:**
  - iPhone: Cookie-Speicher der Home-Bildschirm-App (medium, unverifiziert);
  - Fehlerzweige der QR-Card ohne bUnit-Test (low).
- **Verworfen** (16, je mit Grund im Triage-Log): zwei Freizeiten in einem Browser; Mahlzeiten erst nach dem Öffnen des Rasters; Card nach dem Freizeitende; Erklärzeile im Aushang (Spec: nur Name, QR, Link); Cookie-Ablauf bei Datumsänderung; Gast-Anfrage auf der Anmelden-Seite; Erneuern in der Nachbereitung; toter QR nach dem Ende; Abmelden nach neuem Scan; 5xx verdeckt die Einstellungen; 403 an Gast-Routen (false); „Test vor dem Code“ (false); Laufbelege (false); fehlende Screenshots von 404, Druck und Anmelden-Card; Zusätze laut Spec (false).

**Folge-Review empfohlen:** ja. Gepatcht wurden 3 medium und 4 low, kein high. Das offene Risiko: Die echte Push-Zustellung an Gäste und der iPhone-Weg (Home-Bildschirm-App ohne Safari-Cookie) sind nur mit Nachbildungen geprüft, nicht auf einem Gerät.

**Verifikation:**
- Komplettlauf `dotnet test` nach den Patches:
  - grün: Architektur, SharedKernel, Ui, Ablaeufe, Finanzen, Freizeiten, Inventar, Konten, Kueche, Organisationen, Programm, Sicherheit.
  - Api: alle Tests liefen grün, gemeldet wurden aber Aufräumfehler, weil Docker die Container beim Entsorgen zu langsam entfernt hat (`PostgresContainer.DisposeAsync` mit Timeout). Danach wurden `TeilnehmerZugang`-, `EndpunktListe`-, `Kanaele`- und `DemoDaten`-Tests einzeln wiederholt: 79/79 grün.
  - Personen: ein Verbindungs-Timeout unter Last in `KuechenProfileTests`; einzeln wiederholt 4/4 grün.
  - E2E: im Komplettlauf scheiterte die Veröffentlichung des Test-Hosts unter Last. Einzeln: 205/205 grün, inklusive der Doku-Screenshots.
- `npm run build` in `doku/` fehlerfrei (68 Seiten, nur der bekannte Sitemap-Hinweis).
- Die übrigen veränderten Screenshots hatten nur Rendering-Rauschen (Zufallstoken, Port, Zeitstempel; pixelweise geprüft) und sind zurückgesetzt.

**Restrisiken:**
- iPhone-Weg zu Push für Gäste (siehe deferred).
- Die QR-Screenshots enthalten ein zufälliges Token und den Port des Test-Hosts und ändern sich bei jedem Lauf.
- Das Druckbild nutzt den CSS-Selektor `:has()`.
- Der Komplettlauf ist auf diesem Rechner nur mit Wiederholungen grün: Docker und Testcontainers laufen unter Last in Timeouts.

### Folgedurchlauf 2026-10-21

Status: done

**Review:** 25 Funde (medium 1, low 16, false 7, maybe-false 1, kein high); Details im Triage-Log.
- **Patches** (alle low):
  - neuer HTTP-Test für die Grenze von 200 Nachrichten im Gast-Kanal (`TeilnehmerZugangTests`);
  - Doku `leitung/teilnehmer-zugang.md`: Absatz „Vor dem Einschalten prüfen“ (alle Programmtitel und die bisherigen Nachrichten in „Alle“ sind sichtbar) und der Hinweis, dass nach dem Wieder-Einschalten der alte QR-Code wieder gilt und nur Erneuern endgültig sperrt.
- **Zurückgestellt:** gemeinsames Budget der Zugriffsbegrenzung je IP (medium, Entscheidung an AD-25). Weiter offen aus dem ersten Durchgang: iPhone-Home-Bildschirm und Fehlerzweige der QR-Card (beide carried).
- **Verworfen** (je mit Grund im Triage-Log): zwei Freizeiten in einem Browser (carried); Cookie-Ablauf bei Datumsänderung (carried); Protokoll-Rauschen beim Einlösen (Spec verlangt es); Ende-Text je Grund; kein Protokoll beim Erneuern; Name `GastAbosBeenden` (false); Anfragen je Seitenaufruf; Gerät mit Team-Abo ohne Team-Push; Ausschalten mit geteiltem Browser-Abo; Erneuern in der Nachbereitung (carried); nicht entschlüsselbares Token; Card verschwindet bei 403/404; Stempel-Felder, Dublettenregel, Pfad-Präfix, dritter Satz, X/Y-Test, Demo-Daten (alle false).

**Folge-Review empfohlen:** nein. Folgedurchlauf ohne gepatchten high-Fund (gepatcht: high 0, medium 0, low 3 Einträge); die Arbeit ist konvergiert.

**Dateien:**
- `tests/Zeltlotse.Api.Tests/TeilnehmerZugangTests.cs`: Test für die 200er-Grenze im Gast-Kanal.
- `doku/src/content/docs/leitung/teilnehmer-zugang.md`: Sichtbarkeit vor dem Einschalten, Wieder-Einschalten.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.Api.Tests` mit Filter `TeilnehmerZugangTests`: 21/21 grün.
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` (inkl. Doku-Regeln): 136/136 grün.
- `npm run build` in `doku/`: fehlerfrei, 68 Seiten (nur der bekannte Sitemap-Hinweis).
- Kein Produktivcode geändert, darum kein neuer Komplettlauf und keine neuen Screenshots; der Nachweis der Definition of Done aus dem ersten Durchgang bleibt gültig.

**Restrisiken:** wie im ersten Durchgang, dazu das geteilte Budget der Zugriffsbegrenzung im Lager-WLAN (deferred).
