---
title: 'Story 6-1: Kanäle Team und Alle mit Push'
type: 'feature'
created: '2026-10-20'
baseline_revision: 'b6be44ba62e07f2ee77fc2bf7ff2ddaacfdad3ae'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/5-3-beitrag-bezahlt-abhaken.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Nach einem Wechsel des VAPID-Schlüssels bleiben bestehende Push-Abos dauerhaft stumm und werden nie abgeräumt.
    evidence: |-
      zeltlotse.js pushAbonnieren übernimmt ein bestehendes Abo ohne Vergleich des applicationServerKey; WebPushVersand wertet nur 404/410 als ungültig, 401/403 werden nur geloggt. Die Rotation des VAPID-Schlüssels ist in der Spine (Zurückgestellt) offen.
    location: >-
      src/Zeltlotse.App/wwwroot/js/zeltlotse.js, src/Zeltlotse.Api/Adapter/PushVersand.cs
    severity: low
  - summary: >-
      Push-Abos bleiben nach dem Abschluss der Freizeit oder nach dem Verlassen des Teams bis zur Org-Frist gespeichert.
    evidence: |-
      KanaeleLoeschlauf löscht erst nach der Frist; NachrichtPushAufbau filtert Nicht-Team nur beim Senden. Ein Empfänger für FreizeitAbgeschlossen (laut Ereignis-Fluss in Kanälen) fehlt noch und kommt mit Story 6-4.
    location: >-
      src/Modules/Kanaele/Zeltlotse.Kanaele/Datenschutz/KanaeleLoeschlauf.cs
    severity: medium
  - summary: >-
      Die Kette Push → Service Worker → Neuladen der Kanal-Seite und die Absicherung in eigeneAdresse sind nur mit Nachbildungen getestet.
    evidence: |-
      KanaeleSeitenTests ruft NachgebildeterBrowser.PushEingangAsync direkt auf; kein E2E-Test liefert einen Push aus (z. B. über CDP ServiceWorker.deliverPushMessage).
    location: >-
      src/Zeltlotse.App/wwwroot/service-worker*.js, src/Zeltlotse.App/wwwroot/js/zeltlotse.js
    severity: low
  - summary: >-
      Der Vertrag der Push-Nutzlast (titel, text, url) zwischen Server und Service Worker sowie die Push-Erkennung in zeltlotse.js sind durch keinen Test gebunden.
    evidence: |-
      PushVersand.cs serialisiert ein anonymes Objekt; service-worker*.js liest daten.titel/text/url, pushBeobachten erwartet typ 'push'. KanaeleTests prüfen nur den C#-Datensatz PushInhalt, die UI-Tests nutzen NachgebildeterBrowser, ein JS-Testaufbau fehlt. Ein Umbenennen bliebe unbemerkt. Erster billiger Schritt: die Serialisierung in eine testbare Methode ziehen und die Feldnamen in C# festnageln.
    location: >-
      src/Zeltlotse.Api/Adapter/PushVersand.cs, src/Zeltlotse.App/wwwroot/service-worker*.js, src/Zeltlotse.App/wwwroot/js/zeltlotse.js
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Das Team hat in Zeltlotse keinen Ort für Kurznachrichten. Absprachen laufen deshalb weiter über WhatsApp, und niemand bekommt eine Nachricht aufs Handy (CAP-35, FR-61, NFR-10, NS-9).

**Approach:** Das Modul Kanäle bekommt je Freizeit die zwei festen Kanäle „Team“ und „Alle“. Leitung und Team lesen und schreiben in beiden. Eine Nachricht in „Alle“ geht nur nach Inline-Bestätigung hinaus, und der Server verlangt die Bestätigung ebenfalls. Jede Nachricht geht als Web-Push mit eigenem VAPID-Schlüssel (AD-21) an die Geräte, die Push abonniert haben. Der Push läuft über die Outbox nach demselben Muster wie Mails (`IPushAuftrag` analog `IMailAuftrag`).

## Boundaries & Constraints

**Always:**
- Alle Endpunkte liegen unter `/api/freizeiten/{freizeitId}/kanaele/...` und tragen `.NurFreizeitRolle(Leitung, Team)`. Es gibt keinen Funktions-Schalter (AD-20: der Team-Kanal ist nie abschaltbar).
- Schreibende Aktionen sind Commands mit `IBenutzerCommand` und `IFreizeitCommand`. Bei *gelaufen* antworten sie mit 409 `freizeit-abgeschlossen`. `ToString()` enthält keine Personen-Kennungen (AD-22).
- Push entsteht nur aus dem bestätigten Absenden einer Nachricht und nur über die Outbox (AD-13). Der Push-Inhalt besteht aus Freizeitname, Kanal und Nachrichtentext (gekürzt), dazu die Ziel-URL der Kanal-Seite. Gesundheitsdaten gibt es dort nicht, Autor-Namen auch nicht (Kanäle hat keinen Pfeil zu Personen).
- Empfänger eines Push sind die Abos der Freizeit, deren Person aktuell Leitung oder Team ist (`IFreizeitTeamLesen`). Die Autorin oder der Autor selbst bekommt keinen Push. In 6-1 gilt das für „Team“ und „Alle“ gleich, denn Gast-Abos kommen erst mit 6-4.
- Das Push-Abo gehört Kanäle (AD-21). Es wird je (Freizeit, Endpunkt) einmal gespeichert und überschrieben, wenn es erneut gesendet wird. Als Endpunkt ist nur `https` zu einem bekannten Push-Dienst erlaubt (Schutz gegen SSRF, also dagegen, dass der Server Anfragen an beliebige Adressen schickt).
- Datenschutz nach AD-16:
  - Die Nachricht ist Arbeitsdaten der Freizeit. Bei `PersonGeloescht` wird die Autor-Kennung durch `GeloeschtePerson.Id` ersetzt, nach Ablauf der Org-Frist wird gelöscht.
  - Das Abo ist personeneigen. Bei `PersonGeloescht` wird es sofort gelöscht, im Löschlauf mit der Freizeit.
  - Die Datenauskunft nennt die eigenen Nachrichten und die Anzahl der eigenen Push-Abos.
- Die Oberfläche folgt EXPERIENCE.md, Kanal-Nachrichten:
  - Segmente „Team / Alle“, die neueste Nachricht steht unten.
  - „Senden“ geht in „Team“ direkt hinaus. In „Alle“ öffnet es die Inline-Bestätigung „An N Personen senden?“ mit „Ja, senden“ / „Nein“.
  - Die Push-Erlaubnis wird erst nach Tipp auf „Benachrichtigungen einschalten“ erfragt.
  - Es gibt Hinweise für „nicht möglich (iPhone ohne Home-Bildschirm)“ und „abgelehnt“.
  - Keine Zähler-Badges.
- Neue Darstellungsbausteine entstehen zuerst in `Zeltlotse.Ui.Komponenten`. JavaScript liegt nur in `wwwroot/js/zeltlotse.js` hinter `IBrowser`.

**Never:**
- Kein Kanal für Teilnehmende oder QR-Gäste, keine Gast-Abos (Story 6-4).
- Kein „Alle informieren?“ am Programmpunkt (Story 6-2), kein Ansagen-Zettel und keine Besprechungsnotizen (6-3).
- Keine Live-Aktualisierung per SignalR, keine Lesebestätigung, kein Bearbeiten oder Löschen von Nachrichten.
- Keine Mail als Ersatz für Push. Kein Firebase und kein Hersteller-SDK.
- Keine Änderung an ARCHITECTURE-SPINE.md: kein neuer Ereignis-Typ, kein neuer Aktionsschlüssel, kein neuer Pfeil.

## I/O & Edge-Case Matrix

| Szenario | Eingabe / Zustand | Erwartet | Fehler |
|---|---|---|---|
| Team-Nachricht | Team-Mitglied, POST `.../kanaele/team/nachrichten` `{text}` | 201 mit Nachricht. Je Abo eines anderen Teammitglieds wird nach der Zustellung der Outbox ein Push gesendet. | – |
| „Alle“ unbestätigt | POST `.../kanaele/alle/nachrichten` mit `bestaetigt: false` oder fehlend | 400, Feldfehler `bestaetigt`, nichts gespeichert, kein Push | Validierung |
| „Alle“ bestätigt | `bestaetigt: true` | 201, Push wie bei Team | – |
| Text leer oder zu lang | `text` leer, nur Leerzeichen oder > 1000 Zeichen | 400, Feldfehler `text` | Validierung |
| Unbekannter Kanal | `.../kanaele/foo/nachrichten` | 404 | – |
| Liste | GET `.../kanaele/{kanal}/nachrichten` | 200 `KanalAnsicht`: höchstens die neuesten 200 Nachrichten, älteste zuerst, dazu `personenAlle` (`IFreizeitPersonenzahlLesen.AnzahlAsync`). Der andere Kanal bleibt getrennt. | – |
| Rechte | Org-Admin ohne Rolle, fremde Organisation, unbekannte Freizeit / ohne Sitzung | 404 / 401 auf jeder Route | – |
| Gelaufen | Freizeit abgeschlossen; POST Nachricht oder Abo | 409 `freizeit-abgeschlossen`. GET bleibt 200. | – |
| Abo anlegen | POST `.../kanaele/push/abo` `{endpunkt, p256dh, auth}` | 204. Ein zweites Senden desselben Endpunkts (auch von einer anderen Person) aktualisiert das Abo statt es zu verdoppeln. | – |
| Abo-Endpunkt fremd | `http://...`, IP-Adresse, `localhost` oder ein Host außerhalb der Liste bekannter Push-Dienste | 400, Feldfehler `endpunkt` | Validierung |
| Abo abmelden | POST `.../kanaele/push/abo/abmelden` `{endpunkt}` | 204, Abo der eigenen Person gelöscht. Unbekannt ist ebenfalls 204. | – |
| Schlüssel | GET `.../kanaele/push/schluessel` | 200 `{oeffentlicherSchluessel}` (base64url, unkomprimierter P-256-Punkt) | – |
| Abo abgelaufen | Push-Dienst antwortet 404 oder 410 | Abo wird gelöscht. Die übrigen Empfänger erhalten den Push trotzdem. | Andere Fehler je Ziel: nur Typ loggen, weiter mit dem nächsten Ziel |
| Person aus Team entfernt | Abo vorhanden, Rolle weg | Kein Push an dieses Abo | – |

</intent-contract>

## Code Map

Wurzel: `src/` = S, `tests/` = T.

- `S/Modules/Kanaele/Zeltlotse.Kanaele/KanaeleModul.cs`: leere Hülle. `MapKanaeleEndpoints` gibt `api` zurück. Ist in ZeltlotseModule.cs (L26/L45), Program.cs (L63, Mediator) und ModulMigrationen.cs (L15) bereits registriert.
- `S/Modules/Kanaele/Zeltlotse.Kanaele/Daten/KanaeleDbContext.cs`: DbContext, LeseDbContext und Fabrik ohne Entitäten. Die Migration `Initial` legt nur inbox/outbox an.
- `S/Modules/Kanaele/Zeltlotse.Kanaele/Zeltlotse.Kanaele.csproj`: braucht noch Referenzen auf `Zeltlotse.Api.Contracts` und `Zeltlotse.Freizeiten.Contracts` (Vorbild: Finanzen.csproj L13-20).
- Vorbild Modul `S/Modules/Finanzen/Zeltlotse.Finanzen/`:
  - `FinanzenModul.cs` L28-80: Registrierung, `NurFreizeitRolle`, Datenschutz-Dienste.
  - `Belege/BelegAnlegen.cs`: Command, Validator, Handler, Endpunkt sowie das Ergebnis-Record `Erfolg | Abgeschlossen | NichtGefunden`.
  - `Belege/BelegeLaden.cs`: GET über den LeseDbContext.
  - `Daten/FinanzenDbContext.cs`: Modell und Fabrik.
  - `Datenschutz/FinanzenDatenschutz.cs` L197-218 (`PersonLoeschen`-Empfänger, Auskunft) und `Datenschutz/FinanzenLoeschlauf.cs`.
- Mail-Muster, das für Push gespiegelt wird:
  - `S/Zeltlotse.SharedKernel/Mail/MailAuftraege.cs`: `IMailAuftrag`, `IMailAufbau<T>`, `VerschicktMail<,>`, `MailAuftraege.Empfaenger`.
  - `S/Zeltlotse.SharedKernel/Aggregat.cs`: `MailVormerken`.
  - `S/Zeltlotse.SharedKernel/Daten/ModulDbContext.cs` L37-88: Outbox-Einträge mit fester Zustellung für Mails.
  - `S/Zeltlotse.SharedKernel/Outbox/OutboxZustellung.cs` L153-168: `MailZustellenAsync`.
  - `S/Zeltlotse.SharedKernel/Mail/IMailVersand.cs`, `S/Zeltlotse.Api/Adapter/MailVersand.cs` (`AddMailVersand`, Program.cs L40).
  - Beispiel: `S/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/Entscheiden/AnmeldungEntschiedenMail.cs`.
- Rechte:
  - `S/Zeltlotse.SharedKernel/Rechte/Rechtemerkmale.cs`.
  - `S/Zeltlotse.Api/Rechte/RechtePruefung.cs`: liefert 404 vor 403 und setzt den Mandanten.
  - `S/Zeltlotse.SharedKernel/Pipeline/SchreibschutzStufe.cs`.
- Lesende Schnittstellen von Freizeiten (Pfeil Kanäle → Freizeiten ist erlaubt): `IFreizeitTeamLesen.TeamAsync`, `IFreizeitNamenLesen.NamenAsync` (Push-Titel), `IFreizeitPersonenzahlLesen.AnzahlAsync` (N der Bestätigung).
- Start und Konfiguration:
  - `S/Zeltlotse.Api/Start/Startpruefung.cs` L28-45: Pflichtprüfung der Einstellungen.
  - `S/Zeltlotse.AppHost/AppHost.cs`: geheime Parameter nach dem Muster `hauptschluessel`, mit `base64Schluessel` und Zusatz „=“.
  - `Directory.Packages.props`: Lib.Net.Http.WebPush fehlt noch.
  - `S/Zeltlotse.Api/Sicherheit/SicherheitsHeader.cs`: Die CSP braucht keine Änderung.
- Client:
  - `S/Zeltlotse.App/Features/Finanzen/Beitraege.razor` und `FinanzenTexte.cs` L136 `NamenLadenAsync`: Seitenmuster und Autor-Namen aus Personenliste und Team.
  - `S/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor` L208-211: Knopf-Einstieg.
  - `S/Zeltlotse.Ui/Api/FinanzenApiClient.cs` und `UiRegistrierung.cs` L28: Muster für den API-Client.
  - `S/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor`, `Segmente.razor`, die CSS-Klasse `.elkw-avatar` (elkw-components.css L263) und `PersonenListe.razor` als Vorbild für Initialen.
  - `S/Zeltlotse.App/wwwroot/js/zeltlotse.js`, `Browser/IBrowser.cs` und `JsBrowser.cs`: einzige JS-Schnittstelle.
  - `S/Zeltlotse.App/wwwroot/service-worker.js` und `service-worker.published.js`: haben noch keinen Handler für `push` und `notificationclick`.
- Tests:
  - `T/Zeltlotse.Api.Tests/BeitraegeTests.cs`: Rechte-Tabelle (404/401), Fälle für Gelaufen, Auskunft und Löschlauf.
  - `FreizeitpassSzenario.cs`: Miriam ist Leitung, Tobias Team, Andrea Org-Admin ohne Rolle; Org B mit Freizeit Z.
  - `AnmeldeHelfer.cs`, `TestHelfer.cs`, `EndpunktListeTests.cs` L335ff.
  - `T/Zeltlotse.Testing/ZeltlotseApiFactory.cs`: `Einstellungen`, `ZusatzDienste`, `OutboxJetztZustellenAsync`.
  - `T/Zeltlotse.ArchitectureTests/TypRegelnTests.cs` L137-160 (Felder von Mail-Aufträgen), `DokuRegeln.cs` (`PflichtSeiten`), `DokuTests.cs` L81-82, `DatenschutzTests.cs`, `LoeschPflicht*`.
  - `T/Zeltlotse.Ui.Tests/FinanzenSeitenTests.cs`: `NachgebildeterHandler` und `NachgebildeterBrowser`.
- Doku und Demo:
  - `S/Zeltlotse.Api/Demo/DemoDaten.cs` L136-199 und L919-944 (`BeitraegeAsync`) sowie `DemoKatalog.cs`.
  - `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` L2486-2512 (Muster) und `BildAsync` L2699.
  - `doku/src/content/docs/finanzen/beitraege.md` (Seitenmuster), `mitarbeitende/index.md` (Stichpunkt „Kanäle“ ohne Link), `daten.md` L80-88.

## Tasks & Acceptance

**Execution (testgetrieben: zuerst der Test, dann der Code):**
- `T/Zeltlotse.Api.Tests/KanaeleTests.cs` (neu): HTTP-Tests nach der Matrix gegen PostgreSQL.
  - Rechte-Tabelle über alle fünf Routen.
  - Der Push wird über einen aufzeichnenden `IPushVersand` (eingehängt über `ZusatzDienste`) und `OutboxJetztZustellenAsync` geprüft.
  - Fälle: Gelaufen, `PersonGeloescht`, Auskunft, Löschlauf, Abo-Validierung, 410 räumt das Abo ab.
  - Dazu die Einträge in `EndpunktListeTests.cs`.
- `S/Zeltlotse.SharedKernel/Push/` (neu): `IPushAuftrag`, `IPushAufbau<T>` und die übrigen Push-Typen.
  - `IPushAufbau<T>` liefert `PushSendung(PushInhalt, Ziele)` oder `null` und hat zusätzlich `UngueltigeZieleAsync(ids)`.
  - Dazu `PushInhalt(Titel, Text, Url)`, `PushZiel(Id, Endpunkt, P256dh, Auth)`, `IPushVersand.SendenAsync(ziel, inhalt) → PushErgebnis {Zugestellt, ZielUngueltig}` und `VerschicktPush<,>()`.
  - Anpassen: `Aggregat.PushVormerken`, ModulDbContext (feste Zustellung „Zeltlotse.Push“) und OutboxZustellung (`PushZustellenAsync` schickt je Ziel einzeln; ein Fehler bei einem Ziel wird nur mit Typ geloggt).
  - Dazu SharedKernel-Tests sowie die Prüfung der Feld-Typen in TypRegelnTests für `IPushAuftrag`.
- `Directory.Packages.props`, `S/Zeltlotse.Api/Zeltlotse.Api.csproj`, `S/Zeltlotse.Api/Adapter/PushVersand.cs` (neu): `WebPushVersand` mit Lib.Net.Http.WebPush (aktuelle stabile Version).
  - Einstellung `Zeltlotse:Push:VapidSchluessel` (base64, 32 Byte, privater P-256-Schlüssel). Der öffentliche Schlüssel wird daraus abgeleitet.
  - Der Subject-Wert ist `Zeltlotse:OeffentlicheAdresse`.
  - Push-Dienst antwortet 404/410 → `ZielUngueltig`.
  - Die Prüfung kommt in `Startpruefung`, ein Testwert in `ZeltlotseApiFactory.Einstellungen` und im E2E-Host, ein Parameter `vapid-schluessel` in AppHost.cs.
- `S/Modules/Kanaele/Zeltlotse.Kanaele/` (Slices, Daten, Migration `Kanaele`, Datenschutz):
  - Entitäten:
    - `Nachricht : Aggregat, IOrganisationsEigen` mit FreizeitId, Kanal (`Team=1, Alle=2`), AutorPersonId, Text und GesendetAm. Beim Anlegen ruft sie `PushVormerken(new NachrichtPush(Id))` auf.
    - `PushAbo` mit FreizeitId, PersonId, Endpunkt (eindeutig je Freizeit), P256dh, Auth und AngelegtAm.
  - `NachrichtPushAufbau` liest Nachricht, Abos, Team und Freizeitnamen und kürzt den Text auf 150 Zeichen.
  - Endpunkte gemäß Matrix.
  - Datenschutz: Empfänger für `PersonGeloescht`, `IDatenauskunft`, `ILoeschlauf`.
  - Tests der Command-Merkmale in einem neuen `T/Zeltlotse.Kanaele.Tests` sind optional. Die HTTP-Tests sind Pflicht.
- `S/Zeltlotse.Api.Contracts/Kanaele/*.cs`: `KanalAnsicht`, `KanalNachricht(Id, AutorPersonId, Text, GesendetAm)`, `NachrichtNeu(Text, Bestaetigt)`, `PushAboNeu`, `PushAbmelden`, `PushSchluessel`, Enum `Kanal`.
- `S/Zeltlotse.Ui/Api/KanaeleApiClient.cs` und `UiRegistrierung.cs`: typisierter Client.
- `S/Zeltlotse.Ui.Komponenten/KanalNachrichten.razor` (neu) mit bUnit-Test: Liste mit Avatar-Initialen, Name, Uhrzeit (Europe/Berlin) und Text.
- `S/Zeltlotse.App/Features/Kanaele/Kanaele.razor` (neu, Route `/freizeiten/{id}/kanaele?kanal=alle`):
  - Segmente, Liste, Eingabefeld, „Senden“ bzw. die Inline-Bestätigung in „Alle“.
  - Push-Knopf mit den Zuständen.
  - Lädt neu, wenn ein Push eingeht.
  - Bei gelaufen gibt es kein Eingabefeld.
  - Dazu ein Knopf „Kanäle“ in FreizeitStart.razor und bUnit-Tests.
- `S/Zeltlotse.App/wwwroot/js/zeltlotse.js`, `IBrowser`, `JsBrowser`, `NachgebildeterBrowser`: Push-Status, Abonnieren, Abmelden und das Beobachten eingehender Pushes.
- `S/Zeltlotse.App/wwwroot/service-worker*.js`: `push` zeigt die Benachrichtigung und meldet sie an offene Fenster; `notificationclick` öffnet die URL.
- `S/Zeltlotse.Api/Demo/DemoDaten.cs`, `DemoKatalog.cs` und DemoDatenTests: Nachrichten in Team und Alle von den Demo-Personen.
- `T/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: Bilder `kanaele-team` und `kanaele-alle` (mit offener Inline-Bestätigung), jeweils Handy und Laptop, mit axe-Prüfung.
- `doku/src/content/docs/mitarbeitende/kanaele.md` (neu), `mitarbeitende/index.md` (Link), `daten.md` (Abschnitt Kanäle), `DokuRegeln.PflichtSeiten` und `DokuTests`.

**Acceptance Criteria:**
- Gegeben eine Freizeit mit Leitung Miriam (Abo) und Team Tobias: Schreibt Tobias in „Team“ und wird die Outbox zugestellt, dann erhält genau Miriams Abo einen Push mit Freizeitname, „Team“, Text und URL `/app/freizeiten/{id}/kanaele`.
- Gegeben die Kanal-Seite „Alle“ am Handy: Tippt man auf „Senden“, öffnet sich an Ort und Stelle „An N Personen senden?“. Erst „Ja, senden“ schickt die Nachricht ab, „Nein“ schickt nichts.
- Gegeben ein Gerät ohne Push-Unterstützung oder mit verweigerter Erlaubnis: Beim Öffnen der Seite erscheint der passende Hinweis aus EXPERIENCE.md, und die Kanäle bleiben lesbar und beschreibbar.
- Gegeben die fertige Story, dann laufen `dotnet test` inklusive der Architekturtests grün, die Screenshots `kanaele-*-handy.png` und `kanaele-*-laptop.png` werden erzeugt und `npm run build` in `doku/` läuft fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-20 — Review pass
- verdicts: 30 findings — high 0, medium 4, low 24, false 1, maybe-false 1
- findings:
  - `[low]` `[defer]` Blind: Nach einem Wechsel des VAPID-Schlüssels bleiben alte Abos dauerhaft stumm (Client vergleicht den Schlüssel nicht, 401/403 räumt nicht ab) — Die Rotation ist in der Spine zurückgestellt; die Lösung braucht ein Rotationsverfahren und gehört dorthin.
  - `[medium]` `[patch]` Blind: Der HttpClient für Push hat kein kurzes Timeout, und die Ziele werden nacheinander gesendet — Timeout am benannten Client gesetzt.
  - `[low]` `[reject]` Blind: Bei einem kurzen Ausfall des Push-Dienstes geht der Push verloren — So in den Design Notes festgelegt, damit niemand einen Push doppelt bekommt; ein Wiederholen je Ziel bräuchte zusätzlichen Zustand.
  - `[low]` `[patch]` Blind: „Ausschalten“ löscht nur das Abo der aktuellen Freizeit — Abmelden löscht jetzt alle Abos der Person zu diesem Endpunkt (im Mandanten).
  - `[medium]` `[defer]` Blind: Abos bleiben nach dem Abschluss oder nach dem Verlassen des Teams bis zur Frist liegen — Das Löschen beim Abschluss braucht einen Empfänger für `FreizeitAbgeschlossen`. Den sieht der Ereignis-Fluss für Kanäle vor, er entsteht mit 6-4. Gesendet wird an diese Abos nicht mehr.
  - `[low]` `[reject]` Blind: Das N in „An N Personen senden?“ zählt Teilnehmende mit, die heute nichts bekommen — Das N folgt bewusst EXPERIENCE.md („An 60 Personen“). Mit 6-4 lesen Teilnehmende den Kanal „Alle“.
  - `[low]` `[reject]` Blind: Keine Obergrenze für Abos und keine Begrenzung der Sendehäufigkeit — Nur angemeldetes Team (Vertrauensprinzip); eine Begrenzung bräuchte neue Regeln, und das Risiko im Alltag ist gering.
  - `[low]` `[patch]` Blind: Kein Test bindet `KanalFelder.GeloeschtePerson` an den Wert des Servers (dazu der ungefilterte Index) — Test ergänzt; den Index-Filter verworfen, weil er kaum Nutzen hat.
  - `[low]` `[reject]` Blind: Die Datenauskunft nennt die Freizeit der Nachricht nicht — Dafür bräuchte die Auskunft Freizeitnamen über Mandanten hinweg; das gleiche Muster wie in den anderen Modulen.
  - `[low]` `[reject]` Blind: Beim Wechsel des Kanals wird das Abo erneut gesendet und alles neu geladen — Das ist idempotent und harmlos.
  - `[low]` `[patch]` Blind: Ein Entwurf aus „Team“ bleibt beim Wechsel nach „Alle“ stehen — Der Text wird beim Wechsel des Kanals geleert.
  - `[low]` `[reject]` Blind: Der Push-Code ist in beiden Service-Worker-Dateien kopiert, außerdem fehlt ein `tag` — Zwei Worker-Dateien sind das Muster der PWA-Vorlage; ein `tag` würde frühere Nachrichten verdecken.
  - `[medium]` `[patch]` Gap: Den echten Adapter `WebPushVersand` führt kein Test aus — Tests mit nachgebildetem HTTP-Handler ergänzt (404/410, 500, fremder Endpunkt, keine Weiterleitung, Kopfzeilen).
  - `[low]` `[patch]` Gap: Kein Test mit einem Abo in einer zweiten Freizeit derselben Organisation — Test ergänzt.
  - `[low]` `[defer]` Gap: Die Kette Push → Service Worker → Neuladen der Seite ist nur mit einer Nachbildung getestet — Dafür bräuchte der E2E-Host eine Push-Zustellung über das DevTools-Protokoll, also neue Test-Infrastruktur.
  - `[false]` `[reject]` Intent: Für die Kanal-Routen gibt es keinen 403-Test — `FreizeitRolle` kennt nur Leitung und Team, und beide sind erlaubt. Wer keine Rolle hat, bekommt 404 (RechtePruefung). Der 403-Zweig ist über `/test/nur-team` getestet. Token-Endpunkte kommen nicht hinzu.
  - `[low]` `[reject]` Intent: Screenshots zeigen nur zwei Zustände — Beide Kernflächen sind auf Handy und Laptop abgedeckt, die übrigen Zustände per bUnit.
  - `[low]` `[patch]` Intent: Im Bereich Leitung fehlt ein Verweis auf die Kanäle — Verweis auf der Leitungs-Seite ergänzt.
  - `[medium]` `[patch]` Edge: Nach dem Abmelden oder einem Personenwechsel behält das Gerät das Push-Abo — Beim Abmelden wird das Push-Abo im Browser beendet.
  - `[low]` `[reject]` Edge: Scheitert das stille Erneuern, zeigt die Seite trotzdem „an“ — Selten; beim nächsten Öffnen wird es erneut versucht. Eine Korrektur bräuchte zusätzliche Zustände.
  - `[low]` `[reject]` Edge: Lehnt der Server das Abo ab, steht der Status auf „An“ und zugleich eine Fehlermeldung — Die Fehlermeldung ist sichtbar; ein Rückbau bräuchte einen weiteren Zweig.
  - `[low]` `[defer]` Edge: Ein Abo mit einem anderen VAPID-Schlüssel wird nie abgeräumt — gleiche Ursache wie der erste Fund (Rotation zurückgestellt).
  - `[low]` `[patch]` Edge: Das Kürzen kann ein Surrogatpaar (Emoji) teilen — Beim Kürzen wird ein Surrogatpaar nicht mehr geteilt; Test ergänzt.
  - `[low]` `[reject]` Edge: Ein Abbruch mitten in der Schleife über die Ziele führt beim Wiederholen zu doppelten Pushes — Passiert nur beim Herunterfahren; eine Korrektur bräuchte Zustand je Ziel.
  - `[low]` `[reject]` Edge: Wettlauf zwischen Neuladen nach einem Push und Kanalwechsel — Selten, und der nächste Ladevorgang korrigiert es.
  - `[low]` `[reject]` Edge: Wird die Seite vor `PushBeobachtenAsync` entsorgt, bleibt die Beobachtung hängen — Selten, ohne Folgen für die Daten.
  - `[maybe-false]` `[reject]` Edge: Eine JSException wird nicht abgefangen — Die JS-Funktionen fangen ihre Fehler selbst ab und liefern dann null bzw. einen Status. Prüfen ließe sich das nur mit einem fehlschlagenden Laden des Moduls; das ist höchstens low.
  - `[low]` `[reject]` Edge: Ohne https-Adresse fehlt „sub“, und Apple lehnt den Push ab — Im Betrieb ist die öffentliche Adresse https; ein Push-Dienst lehnt localhost ohnehin ab.
  - `[low]` `[reject]` Edge: Bricht das Einspielen der Demo-Daten mitten ab, fehlen danach Nachrichten — Gleiches Muster der Idempotenz wie bei den anderen Schritten der Demo-Daten.
  - `[low]` `[reject]` Edge: „sub“ fehlt bei nicht-https entgegen dem Spec — Ein VAPID-„sub“ muss mailto oder https sein; ein nicht-https-Wert wäre ungültig.

### 2026-10-20 — Review pass (Folgedurchlauf)
- verdicts: 33 findings — high 0, medium 4, low 27, false 2, maybe-false 0
- findings:
  - `[low]` `[defer]` Intent A: Die Push-Zustellung bis aufs Gerät ist nur mit Nachbildungen getestet (PushAufzeichnung, Handler, NachgebildeterBrowser) — carried: gleicher Befund wie der zurückgestellte zur Kette Push → Service Worker → Neuladen.
  - `[low]` `[defer]` Intent B: Die Browser-Erkennung in JS (iPhone, Permissions-API, „abgelehnt“) ist nur über nachgebildete Status-Werte getestet — gleiche Ursache wie Gap 3 (kein JS-Testaufbau); mit dem neuen Eintrag in `deferred` zurückgestellt.
  - `[low]` `[reject]` Intent C: Zustände wie „gelaufen“ und „aus dem Team entfernt“ entstehen in den Tests per SQL — Geprüft wird die Reaktion von Kanäle; die Wege dorthin sind im Modul Freizeiten getestet. Kein Fehler für Nutzer.
  - `[false]` `[reject]` Intent D: Abmelden reicht über die Matrix hinaus (alle Freizeiten der Organisation, 409 bei gelaufen) — Die Matrix sagt „Abo der eigenen Person gelöscht“ und „POST Nachricht oder Abo“ → 409; beides hält. Die Ausweitung stammt aus dem Patch des ersten Durchlaufs.
  - `[low]` `[reject]` Intent E: Das Konto-Abmelden beendet das Push-Abo im Browser — carried: bewusster Patch des ersten Durchlaufs (Edge „Gerät behält das Abo“), damit ein Gerät keine Nachrichten der abgemeldeten Person zeigt.
  - `[low]` `[reject]` Intent F: N der Bestätigung zählt mehr Personen als heute Push bekommen — carried: folgt EXPERIENCE.md, mit 6-4 lesen Teilnehmende mit.
  - `[low]` `[reject]` Intent G: Push-JS steht auch in den Service-Worker-Dateien — Ein Service Worker muss eine eigene Datei sein; die Regel „nur zeltlotse.js hinter IBrowser“ betrifft das JS-Interop der Blazor-Seiten.
  - `[low]` `[reject]` Edge: Lehnt der Server das Abo ab, bleibt der Browser abonniert — carried: wie im ersten Durchlauf (Fehlermeldung sichtbar, ein Rückbau bräuchte einen weiteren Zweig).
  - `[low]` `[reject]` Edge: Das Ergebnis des stillen Erneuerns wird ignoriert — carried.
  - `[low]` `[reject]` Edge: Wettlauf beim schnellen Kanalwechsel — carried.
  - `[low]` `[reject]` Edge: Entsorgung während `PushBeobachtenAsync` — carried.
  - `[low]` `[reject]` Edge: Ein NUL-Zeichen im Text führt in PostgreSQL zu 500 statt 400 — Real, aber im Alltag tippt niemand ein NUL-Zeichen; die Korrektur wäre eine zusätzliche Validierungsregel.
  - `[low]` `[reject]` Edge: Abbruch mitten in der Ziel-Schleife führt zu doppelten Pushes — carried.
  - `[low]` `[reject]` Edge: Ohne https-Adresse fehlt „sub“ — carried.
  - `[low]` `[reject]` Edge: Das Ergebnis von `AbmeldenAsync` wird ignoriert, Server-Zeilen bleiben bis zum nächsten 410 — Schon als Restrisiko vermerkt; eine Rückmeldung bräuchte einen zusätzlichen Fehlerzweig, und das Gerät ist trotzdem abgemeldet.
  - `[low]` `[reject]` Edge (claim): „sub“ ist nicht immer `OeffentlicheAdresse` — carried: ein VAPID-„sub“ muss mailto oder https sein.
  - `[low]` `[reject]` Blind: Ein gescheiterter Push wird nicht wiederholt — carried.
  - `[medium]` `[patch]` Blind: Ziele gehen nacheinander mit je 10 s Timeout hinaus und blockieren die Outbox — carried: Timeout im ersten Durchlauf gesetzt; kein erneuter Patch.
  - `[low]` `[defer]` Blind: Ein Wechsel des VAPID-Schlüssels wird nie behandelt — carried (steht bereits in `deferred`).
  - `[low]` `[reject]` Blind: Das stille Erneuern zeigt trotz Fehler „eingeschaltet“ — carried.
  - `[low]` `[reject]` Blind: „An N Personen“ übertreibt die Reichweite — carried.
  - `[low]` `[reject]` Blind: Keine Begrenzung von Senden und Abos — carried.
  - `[low]` `[reject]` Blind: Abmelden und Ausschalten lassen Server-Zeilen liegen, die Doku stelle das als sofortiges Aufräumen dar — Die Doku sagt nur, dass die Benachrichtigungen auf dem Gerät enden, und daten.md beschreibt das Abräumen beim nächsten Versand; beides stimmt. Das Liegenbleiben ist als Restrisiko vermerkt.
  - `[low]` `[patch]` Blind: Die Grenze von 200 Nachrichten steht nirgends in der Doku — Satz in `mitarbeitende/kanaele.md` ergänzt (neueste 200 je Kanal, ältere bleiben gespeichert).
  - `[low]` `[reject]` Blind: Der Push-Code steht doppelt in beiden Service-Worker-Dateien — carried.
  - `[low]` `[reject]` Blind: `notificationclick` öffnet ein neues Fenster, wenn keine Adresse genau passt — Ein Umbau auf `navigate` bräuchte weitere Zweige für nicht gesteuerte Fenster und ist ohne JS-Testaufbau nicht prüfbar; die Folge ist nur ein zusätzliches Fenster.
  - `[low]` `[reject]` Blind: Wer das Team verlassen hat, erscheint als „Ohne Namen“ — Selten; die Namen kommen bewusst aus der Freizeit (Kanäle hat keinen Pfeil zu Personen), ein weiterer Weg zu Namen wäre neue Logik.
  - `[low]` `[reject]` Blind: In „Alle“ kommt die Rückfrage vor der Prüfung des Textes — Ein leeres Absenden ist selten, der Fehler steht danach am Feld; eine Vorprüfung im Client wäre ein zusätzlicher Zweig.
  - `[false]` `[reject]` Blind: `ON CONFLICT … WHERE OrganisationId` meldet bei fremder Organisation Erfolg, ohne zu speichern — Der Konflikt-Schlüssel ist (FreizeitId, Endpunkt); eine Freizeit gehört genau einer Organisation, und die RechtePruefung lässt nur die eigene zu. Ein Konflikt über Organisationen hinweg kann nicht entstehen.
  - `[low]` `[reject]` Blind: Fehlende Tests (401/403/429, Aufräumen beim Abmelden, Service Worker, 200er-Grenze, „Ohne Namen“) — Sammelbefund; jeder Punkt hat in diesem Log eine eigene Zeile mit Entscheidung.
  - `[medium]` `[patch]` Gap: Dass der registrierte Push-Client keinen Weiterleitungen folgt, prüft kein Test — `Registrierung_…` prüft jetzt über `IHttpMessageHandlerFactory` den innersten Handler des registrierten Clients (`SocketsHttpHandler`, `AllowAutoRedirect = false`).
  - `[medium]` `[patch]` Gap: VAPID-„sub“, „aud“ und der Schlüssel im Authorization-Header sind nicht geprüft — Neuer Test `VAPID_traegt_die_oeffentliche_Adresse_als_sub_…` liest `t=` (JWT) und `k=` aus und prüft sub, aud und den öffentlichen Schlüssel.
  - `[medium]` `[defer]` Gap: Der Vertrag der Push-Nutzlast zwischen Server und Service Worker ist ungeprüft — Kein JS-Testaufbau; als neuer Eintrag in `deferred` zurückgestellt, mit einem billigen ersten Schritt.

## Design Notes

- **Push wie Mail statt als Ereignis:** Ein eigenes Ereignis `NachrichtGesendet` hätte eine neue Zeile in der Tabelle *Ereignis-Fluss* der Spine verlangt. Die Outbox kennt mit Mails schon ein Muster „Versand nach außen mit fester Zustellung“. `IPushAuftrag` spiegelt es und erfüllt AD-13 (nur über die Outbox) ohne Änderung der Architektur.
- **Zustellung je Ziel ohne Wiederholung des Ganzen:** Würde der ganze Auftrag wiederholt, sobald ein Ziel scheitert, bekämen die anderen Empfänger den Push doppelt. Deshalb wird jedes Ziel einzeln versucht. Ein Fehler bei einem Ziel wird mit Typ geloggt, und das Ziel ist damit erledigt. Push-Dienste puffern selbst, deshalb bleibt das Team trotzdem verlässlich erreichbar.
- **403 nicht erreichbar:** Alle Endpunkte erlauben Leitung und Team. Wer keine Freizeit-Rolle hat, bekommt 404 (RechtePruefung). Den 403-Zweig deckt der vorhandene Test-Endpunkt `/test/nur-team` ab, wie schon bei Beiträgen. Token-Fälle gibt es nicht, weil kein Token-Endpunkt hinzukommt. QR folgt in 6-4.
- **Server prüft die Bestätigung:** `bestaetigt: true` ist für „Alle“ Pflicht. So kann kein Client ohne ausdrückliche Bestätigung an alle senden (AD-13).
- **Namen im Client:** Die Antwort enthält nur `AutorPersonId`. Den Namen bestimmt der Client wie in Finanzen über Personenliste und Team. Für `GeloeschtePerson.Id` steht dort „Gelöschte Person“.
- **VAPID aus einem Geheimnis:** Der private Schlüssel ist 32 Byte Zufall (base64), der öffentliche wird abgeleitet. So erzeugt Aspire ihn wie den Hauptschlüssel, und es gibt kein zweites Geheimnis, das nicht dazu passt.
- **Erlaubte Push-Hosts:** Endungen `fcm.googleapis.com`, `push.services.mozilla.com`, `push.apple.com`, `notify.windows.com`. Andere Hosts sind nicht erlaubt (Schutz gegen SSRF).

## Verification

**Commands:**
- `dotnet test tests/Zeltlotse.Api.Tests --filter-class *KanaeleTests*` (oder die passende MTP-Filter-Syntax), dazu `dotnet test tests/Zeltlotse.ArchitectureTests`, `tests/Zeltlotse.SharedKernel.Tests` und `tests/Zeltlotse.Ui.Tests`. Erwartet: grün.
- Am Ende einmal: `dotnet test` für die ganze Lösung inklusive der E2E-Tests der Kategorie Doku, danach `npm run build` in `doku/`. Erwartet: grün und fehlerfrei, die neuen PNGs liegen unter `doku/src/assets/screenshots/`.

## Auto Run Result

Status: done

**Umgesetzt:** Kanäle „Team“ und „Alle“ je Freizeit für Leitung und Team, mit Web-Push über die Outbox.
- „Alle“ verlangt die Inline-Bestätigung „An N Personen senden?“, und der Server verlangt `bestaetigt: true`.
- Push hat ein eigenes Muster `IPushAuftrag`, gespiegelt vom Mail-Auftrag. Gesendet wird je Ziel einzeln; Abos, die der Push-Dienst nicht mehr kennt (404/410), werden abgeräumt.
- VAPID: Aus der Geheimnisverwaltung kommt nur der private Schlüssel, der öffentliche wird daraus abgeleitet.
- Datenschutz nach AD-16: Empfänger für `PersonGeloescht`, Datenauskunft und Lösch-Lauf.
- Oberfläche mit Benachrichtigungs-Card (Hinweise für iPhone, „abgelehnt“ und „nicht möglich“) und Service-Worker-Handlern.
- Doku-Seite `mitarbeitende/kanaele.md`, Demo-Nachrichten, Screenshots `kanaele-team` und `kanaele-alle` (Handy und Laptop).

**Dateien (Auswahl):**
- `src/Modules/Kanaele/**`: Entitäten, Slices, Migration, Datenschutz.
- `src/Zeltlotse.SharedKernel/Push/*`, `Aggregat.cs`, `ModulDbContext.cs`, `OutboxZustellung.cs`: Push-Aufträge, Zustellung je Ziel, VAPID, erlaubte Endpunkte.
- `src/Zeltlotse.Api/Adapter/PushVersand.cs`: Web-Push-Adapter (Timeout 10 s, keine Weiterleitungen). Dazu `Startpruefung.cs`, `AppHost.cs` und die Demo-Daten.
- `src/Zeltlotse.Api.Contracts/Kanaele/Kanaele.cs`, `src/Zeltlotse.Ui/Api/KanaeleApiClient.cs`, `src/Zeltlotse.Ui.Komponenten/KanalNachrichten.razor`.
- `src/Zeltlotse.App/Features/Kanaele/*`, `Browser/*`, `wwwroot/js/zeltlotse.js`, `service-worker*.js`, `Features/Konten/AnmeldungAbschluss.cs` (beim Abmelden endet das Push-Abo), `FreizeitStart.razor` (Knopf „Kanäle“).
- Tests:
  - neu: `KanaeleTests`, `WebPushVersandTests`, `PushAuftragTests`, `KanaeleSeitenTests`;
  - erweitert: `EndpunktListeTests`, `StartpruefungTests`, `DemoDatenTests`, `TypRegelnTests`, `LoeschPflichtTests`, `DokuTests` und `DokuRegeln`, `PersonGeloeschtTests`, E2E-Screenshots.
- Doku: `mitarbeitende/kanaele.md` (neu), `mitarbeitende/index.md`, `leitung/index.md`, `leitung/freizeit-abschliessen.md`, `daten.md`.

**Review:** 30 Funde.
- 9 Patches (Details im Triage-Log):
  - Timeout für den Push-Client;
  - Abmelden löscht den Endpunkt in allen Freizeiten der Organisation;
  - Test, der den Platzhalter an den Wert des Servers bindet;
  - der Entwurf geht beim Kanalwechsel nicht mit;
  - Tests für den echten Adapter;
  - Test mit zwei Freizeiten einer Organisation;
  - Leitungs-Verweis in der Doku;
  - beim Abmelden endet das Push-Abo;
  - kein geteiltes Surrogatpaar beim Kürzen.
- 3 zurückgestellt (VAPID-Rotation, Abos bis zur Frist, E2E-Kette des Service Workers).
- 18 verworfen, jeweils mit Grund im Triage-Log.
- Nach dem Patch fiel ein Architekturtest aus, weil `AnmeldungAbschluss` von JSInterop abhing. Die Behandlung der JS-Fehler steckt jetzt in `JsBrowser.PushAbmeldenAsync`.

**Folge-Review empfohlen:** ja. Gepatcht wurden 3 medium und 6 low. Offenes Risiko: Echte Push-Zustellung über die Push-Dienste der Browser (FCM, Mozilla, Apple) und die Kette Push → Service Worker → Neuladen sind nur mit Nachbildungen geprüft.

**Verifikation:**
- `dotnet test` je Testprojekt ist grün:
  - Api 1022; Architektur 136; Ui 811; SharedKernel 134;
  - Ablaeufe 10; Finanzen 65; Freizeiten 93; Inventar 34;
  - Konten 26; Kueche 119; Organisationen 58; Personen 157;
  - Programm 91; Sicherheit 36;
  - E2E inklusive Doku-Screenshots 191.
- `npm run build` in `doku/` fehlerfrei (67 Seiten, nur der bekannte Sitemap-Hinweis).
- 34 ältere Screenshots hatten nur Rendering-Rauschen und sind zurückgesetzt.

**Restrisiken:**
- Ein Push-Abo fremder Freizeiten bleibt nach dem Abmelden des Geräts auf dem Server, bis der nächste Push ein 410 liefert.
- Die Erkennung von iPhone und iPad läuft über den User-Agent.

### Folgedurchlauf 2026-10-20

- **Review:** 33 Funde von vier Prüfern (Blind, Edge, Verifikationslücken, Intent); 17 davon carried aus dem ersten Durchlauf.
- **Patches in diesem Durchlauf (2 medium, 1 low), nur Tests und Doku:**
  - `tests/Zeltlotse.Api.Tests/WebPushVersandTests.cs`: Der Test der Registrierung prüft den Handler, den der registrierte Client wirklich nutzt (keine Weiterleitungen). Ein neuer Test prüft VAPID-„sub“, „aud“ und den Schlüssel im Authorization-Header.
  - `doku/src/content/docs/mitarbeitende/kanaele.md`: Hinweis auf die neuesten 200 Nachrichten je Kanal.
- **Zurückgestellt:** neu der Vertrag der Push-Nutzlast und die JS-Erkennung (medium, kein JS-Testaufbau); carried die VAPID-Rotation und die Service-Worker-Kette.
- **Verworfen:** 25 Funde, jeweils mit Grund im Triage-Log (2 davon false).
- **Folge-Review empfohlen:** nein. Im Folgedurchlauf wurde kein high-Fund gepatcht; gepatcht 0 high, 2 medium, 1 low.
- **Verifikation:** `WebPushVersandTests` 11/11 grün; `tests/Zeltlotse.ArchitectureTests` 136/136 grün; `npm run build` in `doku/` fehlerfrei (67 Seiten, nur der bekannte Sitemap-Hinweis). Produktcode blieb unverändert, deshalb kein erneuter Komplettlauf mit E2E.
- **Restrisiken:** wie oben, dazu die ungeprüfte Nutzlast zwischen Server und Service Worker.
