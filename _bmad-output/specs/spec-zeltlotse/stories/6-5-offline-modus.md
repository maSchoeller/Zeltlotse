---
title: 'Story 6-5: Offline-Modus'
type: 'feature'
created: '2026-10-21'
baseline_revision: '8075fb37356293fccce646aa6563abfadc9ad036'
status: 'done'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-10-vorfall-protokoll-und-vorfall-notiz.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Ohne Netz ist heute jede Seite der Web-App ausgegraut; im Zeltlager gilt dann nur der ausgedruckte Notfallordner (NFR-11). CAP-39/FR-65/AD-18 verlangen, dass Küchen-Tagesansicht, Notfallkarte und Sicherheitskette offline verschlüsselt lesbar sind, das Vorfall-Protokoll offline beschreibbar ist und die Daten nach Freizeitende vom Gerät verschwinden.

**Approach:** Ein Offline-Abgleich in der Web-App lädt für jede eigene, laufende Freizeit (Rolle Leitung oder Team) Sicherheitskette, Freizeit-Kopf, eine neue schlanke Notfallkarte (Personen-Endpunkt) und die Tagesansichten aller Tage und legt sie AES-GCM-verschlüsselt mit einem nicht exportierbaren WebCrypto-Schlüssel in IndexedDB ab, mit Ablauf = Freizeitende. Offline lesen die vier Seiten aus diesem Speicher; Vorfall-Einträge gehen offline in eine verschlüsselte Warteschlange und werden bei Verbindung idempotent gesendet (Server kann das schon, bekommt nur das Gerätedatum dazu).

## Boundaries & Constraints

**Always:**
- Server bleibt maßgeblich; Service Worker cacht weiter nur App-Dateien, nie API-Antworten; `/api/**` trägt `Cache-Control: no-store` (besteht, für den neuen Endpunkt mit Test belegt).
- Alles Gespeicherte (auch die Warteschlange) liegt nur verschlüsselt in IndexedDB; Schlüssel `extractable: false`, erzeugt auf dem Gerät, in derselben Datenbank abgelegt. Kein `localStorage` für diese Daten.
- Jeder Datensatz trägt `ablauf` = Freizeitende (Tag nach `LetzterTag`, 00:00 Europe/Berlin, als UTC-Millisekunden). Abgelaufenes wird beim App-Start, vor jedem Lesen und bei jedem Abgleich gelöscht – rein auf dem Gerät, auch offline.
- Abmelden (alle Wege über `AnmeldungAbschluss.AbmeldenAsync`, Kontolöschung in `MeineDaten`) und abgelaufene Sitzung (`AppSitzung.AbgelaufenAsync`, 401) löschen die ganze Offline-Datenbank samt Schlüssel.
- Antwortet die API beim Abgleich oder beim Senden der Warteschlange für eine Freizeit mit 403 oder 404, werden deren Offline-Daten samt wartender Einträge sofort gelöscht. Freizeiten, die nicht mehr als laufend mit eigener Rolle Leitung/Team in `GET /api/ich/freizeiten` stehen, werden ebenfalls gelöscht.
- Abgeglichen wird nur in Phase *läuft* (wie Notfall-Box und Eintrag „Notfall“), nur bei Verbindung: beim Start des Layouts mit Sitzung, nach dem Login und wenn die Verbindung zurückkommt. Danach wird `navigator.storage.persist()` angefragt.
- Tagesansichten nur, wenn Schalter Verpflegung an ist (Kontext); Notfallkarte nur mit Aktionsschlüssel `freizeitpaesse-sehen`.
- JS nur in `wwwroot/js/zeltlotse.js` hinter `IBrowser`; C#-Speicherdienst in der App ohne JSInterop. Neue Darstellungsbausteine zuerst in `Zeltlotse.Ui.Komponenten`. Oberfläche Deutsch ohne Du/Sie.
- Offline nutzbar sind nur: Küchen-Tagesansicht (lesen, Restetag-Haken deaktiviert), Notfallkarte (lesen), Notfall-Box, Sicherheitskette (lesen; „Notfallordner drucken“ ausgegraut) und Vorfall-Protokoll (schreiben). Alles andere bleibt „Offline nicht verfügbar.“.
- Testgetrieben (AD-30): Test vor Code; jeder neue oder geänderte Endpunkt mit HTTP-Tests gegen echtes PostgreSQL.

**Never:** kein Offline-Schreiben außer Vorfall-Einträgen; kein Synchronisieren von Konflikten; keine Speicherung vor Phase *läuft*; keine Klartext-Personendaten in IndexedDB, localStorage oder Service-Worker-Cache; keine Drittanbieter-Bibliotheken (weder JS noch NuGet); keine Live-Aktualisierung; keine neuen Module.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Abgleich | Team-Mitglied online, Freizeit läuft, Verpflegung an | Speicher enthält Freizeit, Sicherheitskette, Notfallkarte, Tagesansicht je Tag; Einsicht-Protokoll je Person Zweck „Notfallkarte auf Gerät geladen“ (höchstens 1×/Tag) | Netzfehler mitten im Abgleich: bisher Gespeichertes bleibt |
| Flugmodus | nach Abgleich offline, App neu laden | Notfall-Box „Notfallhilfe öffnen“ → Sicherheitskette aus Speicher; Suche findet Person → Notfallkarte (Allergien, Medikamente, Krankheiten, Notfallkontakte); Tagesansicht zeigt Tage | nichts gespeichert: Notfall-Box mit „Offline gilt der ausgedruckte Notfallordner.“, Seiten zeigen „Auf diesem Gerät nicht gespeichert.“ |
| Vorfall offline | Text in „Was ist passiert?“, Feld verlassen | Feld leert sich, Eintrag erscheint als „wartet auf Verbindung“, Offline-Leiste „1 Eintrag wartet auf Verbindung“ | — |
| Wieder online | Warteschlange mit N Einträgen | je Eintrag `POST …/vorfaelle` mit Geräte-Kennung und `erfasstAm`; bei 201 entfernt; Leiste verschwindet, wenn leer und online | Netzfehler: bleibt; 409 `freizeit-abgeschlossen` oder 400: Eintrag entfernt; 403/404: Freizeit-Daten gelöscht |
| Doppelt senden | gleicher Eintrag zweimal | eine Zeile, beide 201 (besteht) | — |
| Gerätezeit | `erfasstAm` gesetzt | Server übernimmt `min(erfasstAm, jetzt)`; ohne Angabe Serverzeit | — |
| Freizeitende | Gerätedatum nach Ablauf, offline | Daten der Freizeit gelöscht, Seiten zeigen „Auf diesem Gerät nicht gespeichert.“ | — |
| Abmelden | Offline-Daten vorhanden | IndexedDB-Datenbank gelöscht | — |
| Notfallkarte HTTP | Leitung/Team | 200, nur bestätigte Personen mit gültiger Freigabe, nur Fragen `allergien`, `medikamente`, `krankheiten`, `notfallkontakte` soweit freigegeben, sortiert Nachname/Vorname | Org-Admin ohne Rolle 404; fremde Organisation 404; ohne Sitzung 401; Teilnehmer-Zugang (QR-Gast) kein Zugriff |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/Notfallordner.cs` -- Vorbild für den neuen Endpunkt: Rollen über `FreizeitpassZugriff.RollenAsync`, `JuengsteFreigabenAsync`, `Zustand(...) == Sichtbar`, Stufe Team, Protokoll nach dem Zusammenstellen, nicht für sich selbst.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/FreizeitpassEinsicht.cs:92` -- `FreizeitpassZugriff`, `FreizeitpassRechte.Einsicht` = [Leitung, Team].
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:105-109` -- Registrierung der Freizeitpass-Endpunkte (`.NurFreizeitRolle(...)`).
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Freizeitpass/Fragenkatalog.cs:101-110` -- Kennungen `Allergien`, `Medikamente`, `Notfallkontakte`, `Krankheiten`.
- `src/Modules/Personen/Zeltlotse.Personen/Zugriffsprotokoll/EinsichtProtokoll.cs:11` -- `EinsichtZweck`; Spiegel in `src/Zeltlotse.Api.Contracts/Personen/Zugriffsprotokoll.cs:14` (`ZugriffZweck`), Texte in `Datenschutz/PersonenAuskunft.cs:283` und `src/Zeltlotse.Ui/Texte/Anzeige.cs:204`.
- `src/Modules/Sicherheit/Zeltlotse.Sicherheit/Vorfaelle/VorfaelleBearbeiten.cs` -- Command/Validator/Handler/Endpunkt Vorfall anlegen; Idempotenz (`ON CONFLICT ("Id") DO NOTHING`) besteht; `ErfasstAm = uhr.GetUtcNow()` → hier Gerätezeit einbauen.
- `src/Zeltlotse.Api.Contracts/Sicherheit/Vorfaelle.cs` -- `VorfallEintragNeu(Id, PersonId, Art, Text)` um `DateTimeOffset? ErfasstAm` erweitern; `VorfallFelder`.
- `src/Zeltlotse.Api/Sicherheit/SicherheitsHeader.cs` -- `no-store` für `/api/**` (besteht).
- `tests/Zeltlotse.Api.Tests/VorfaelleTests.cs`, `NotfallordnerTests.cs`, `FreizeitpassSzenario.cs`, `EndpunktListeTests.cs:281`, `ZugriffsprotokollTests.cs`, `WebSicherheitTests.cs` -- HTTP-Testmuster (Rechte-Test je Endpunkt, Szenario mit Miriam/Tobias/Andrea, fremde Org), Endpunktliste nachziehen.
- `src/Zeltlotse.App/wwwroot/js/zeltlotse.js` -- einziges JS-Modul; IndexedDB/WebCrypto/`storage.persist` hier ergänzen.
- `src/Zeltlotse.App/Browser/IBrowser.cs`, `JsBrowser.cs` -- C#-Schnittstelle; `tests/Zeltlotse.Ui.Tests/NachgebildeterBrowser.cs` muss jede neue Methode nachbilden (Wörterbuch-Speicher mit Ablauf gegen `FakeTimeProvider`).
- `src/Zeltlotse.App/AppDienste.cs` -- DI-Registrierung neuer Dienste.
- `src/Zeltlotse.App/Verbindung/AppVerbindung.cs` -- `IVerbindung.Offline`, Ereignis `Geaendert` (Auslöser „wieder online“).
- `src/Zeltlotse.App/Layout/MainLayout.razor` -- `OfflineLeiste`, `NotfallBox` mit `NotfallSatzOffline`, `<OfflineBereich Offline=...>` um `@Body`, `NotfallLadenAsync`.
- `src/Zeltlotse.App/App.razor` -- Router; `routeData.PageType` für die Offline-Kennzeichnung der Seiten kaskadieren.
- `src/Zeltlotse.Ui.Komponenten/OfflineBereich.razor`, `OfflineLeiste`, `NotfallBox` -- Bausteine; Leiste braucht Zeile „N Einträge warten auf Verbindung“.
- `src/Zeltlotse.App/Features/Kueche/Tagesansicht.razor` -- lädt `KuecheApiClient.TagesansichtLadenAsync(id, tag)`; Restetag = `AutosaveAnkreuzfeld`.
- `src/Zeltlotse.App/Features/Sicherheit/Sicherheitskette.razor` -- lädt Kontext, `FreizeitAntwort`, Kette, Personenliste; bindet `Freizeiten/Schnellsuche.razor` (Treffer → Freizeitpass).
- `src/Zeltlotse.App/Features/Sicherheit/VorfallProtokoll.razor:175-190` -- `SpeichernAsync(art)`, Kennung `Guid.CreateVersion7()` je Art, `VorfallAnlegenErgebnis`.
- `src/Zeltlotse.App/Features/Konten/AnmeldungAbschluss.cs:51` -- `AbmeldenAsync`; `src/Zeltlotse.App/Sitzung/AppSitzung.cs` -- `AbgelaufenAsync`; `MeineDaten.razor:176` -- `Sitzung.Abmelden()` nach Löschung.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs` (~555 Notfallordner), `SicherheitApiClient.cs`, `KuecheApiClient.cs`, `FreizeitenApiClient.cs` -- typisierte Clients (403/404 → `null`).
- `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeiten.cs:52,66` -- `FreizeitAntwort` (Leitung, `LetzterTag`), `MeineFreizeit` (Phase, Rollen, `LetzterTag`).
- `tests/Zeltlotse.Ui.Tests/AppLayoutTests.cs:75-165`, `TagesansichtSeitenTests.cs`, `SicherheitsketteSeitenTests.cs` -- bUnit-Muster (`NachgebildeterHandler`, `JSInterop.Mode = Loose`).
- `tests/Zeltlotse.E2E.Tests/OfflineStartTests.cs`, `NotfallTests.cs` (laufende Freizeit über echte Endpunkte anlegen, `SetOfflineAsync`), `DokuScreenshotTests.cs:47` (`App_Offline`), `:1237` (Story 2-8), `:1413` (Vorfall), `BildAsync` → `doku/src/assets/screenshots/<name>-<geraet>.png`.
- `doku/src/content/docs/mitarbeitende/notfall.md`, `vorfall-protokoll.md`, `kueche/tagesansicht.md`, `erste-schritte/zeltlotse-oeffnen.md`, Seite `daten` -- Doku ergänzen; Sidebar wird automatisch erzeugt.
- `src/Zeltlotse.Api/Demo/DemoDaten.cs`, `DemoKatalog.cs` -- Demo-Freizeit liegt 2027 (Phase in Vorbereitung bei Systemuhr).

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Api.Tests/NotfallkarteTests.cs` (neu) -- zuerst: Inhalt/Filter/Sortierung, nur vier Kennungen, unbestätigt/abgelaufen fehlt, Protokoll Zweck neu 1×/Tag nicht für sich selbst, `Cache-Control: no-store`, Rechte (Leitung 200, Team 200, Org-Admin ohne Rolle 404, fremde Org 404, ohne Sitzung 401, QR-Gast-Sitzung aus Story 6-4 ohne Zugriff) -- AD-30.
- `src/Zeltlotse.Api.Contracts/Personen/Notfallkarte.cs` (neu) -- `NotfallkartePerson(Guid PersonId, string Vorname, string Nachname, IReadOnlyList<FreizeitpassFrage> Fragen)` -- Datenform wie Notfallordner.
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitpass/Notfallkarte.cs` (neu) + `PersonenModul.cs` -- `GET /api/freizeiten/{freizeitId}/personen/notfallkarte`, `.NurFreizeitRolle(FreizeitpassRechte.Einsicht)`; alle Personen mit jüngster Freigabe dieser Freizeit, Stufe Team, sichtbar; Fragen = Schnitt aus Freigabe und den vier Kennungen -- AD-18 „nur die nötigen Felder“.
- `EinsichtProtokoll.cs`, `Api.Contracts/Personen/Zugriffsprotokoll.cs`, `PersonenAuskunft.cs`, `Ui/Texte/Anzeige.cs` -- Zweck `NotfallkarteGeladen = 5`, Text „Notfallkarte aufs Gerät geladen“ -- Zugriffsprotokoll ehrlich (FR-13).
- `tests/Zeltlotse.Api.Tests/VorfaelleTests.cs` -- zuerst: `erfasstAm` in der Vergangenheit wird übernommen, in der Zukunft → Serverzeit, ohne → Serverzeit; danach `Vorfaelle.cs` + `VorfaelleBearbeiten.cs` umsetzen -- Zeitpunkt offline geschriebener Einträge stimmt.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- neuen Endpunkt eintragen.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs` -- `NotfallkarteLadenAsync` (403/404 → `null`); `SicherheitApiClient.VorfallAnlegenAsync` unterscheidet 403/404 als eigenes Ergebnis.
- `src/Zeltlotse.App/wwwroot/js/zeltlotse.js` -- `offlineSchreiben(schluessel, freizeitId, ablaufMs, text)`, `offlineLesen(schluessel)`, `offlineListe(praefix)`, `offlineEntfernen(schluessel)`, `offlineFreizeitLoeschen(freizeitId)`, `offlineAllesLoeschen()`, `offlineAufraeumen()`, `speicherHalten()`; DB `zeltlotse-offline`, AES-GCM 256 mit zufälligem IV je Datensatz -- AD-18.
- `src/Zeltlotse.App/Browser/IBrowser.cs`, `JsBrowser.cs`, `tests/Zeltlotse.Ui.Tests/NachgebildeterBrowser.cs` -- Methoden spiegeln; Nachbildung hält Ablauf gegen die Test-Uhr.
- `src/Zeltlotse.App/Offline/OfflineSpeicher.cs` (neu) -- typisierte JSON-Ablage je Freizeit (`{id}/freizeit`, `{id}/sicherheitskette`, `{id}/notfallkarte`, `{id}/tagesansicht/{yyyy-MM-dd}`, `vorfall/{freizeitId}/{eintragId}`), Ablauf aus `LetzterTag`, Liste gespeicherter Freizeiten.
- `src/Zeltlotse.App/Offline/OfflineAbgleich.cs` (neu) -- Abgleich nach Boundaries; Warteschlange senden, Zähler `Wartend` + Ereignis; Auslöser in `MainLayout`/Login; Registrierung in `AppDienste.cs`.
- `AnmeldungAbschluss.cs`, `AppSitzung.cs`, `MeineDaten.razor` -- Offline-Datenbank löschen.
- `src/Zeltlotse.App/App.razor`, `Offline/OfflineNutzbarAttribute.cs` (neu), `Layout/MainLayout.razor` -- Seiten mit `[OfflineNutzbar]` werden nicht ausgegraut; Notfall-Box offline mit Link „Notfallhilfe öffnen“ zur gespeicherten Freizeit (aktuelle Route, sonst erste gespeicherte nach Name), ohne Speicher alter Satz; Offline-Leiste auch online sichtbar, solange Einträge warten.
- `src/Zeltlotse.Ui.Komponenten/OfflineLeiste.razor` -- zweite Zeile „1 Eintrag wartet auf Verbindung“ / „N Einträge warten auf Verbindung“ (bUnit-Test).
- `Features/Kueche/Tagesansicht.razor`, `Features/Sicherheit/Sicherheitskette.razor`, `Features/Sicherheit/VorfallProtokoll.razor`, `Features/Freizeiten/Schnellsuche.razor` -- `[OfflineNutzbar]`; offline (oder bei Netzfehler) aus `OfflineSpeicher` lesen; Restetag und Drucken offline deaktiviert; Suche offline über Notfallkarte-Personen mit Treffer → Notfallkarte; Vorfall offline in Warteschlange, wartende Einträge sichtbar.
- `src/Zeltlotse.App/Features/Sicherheit/Notfallkarte.razor` (neu) -- Route `/freizeiten/{FreizeitId:guid}/notfallkarte/{PersonId:guid}`, `[OfflineNutzbar]`, liest immer aus dem Speicher; Name, vier Angaben (unbeantwortet „keine Angabe“), Links „Vorfall-Protokoll“ und „Zurück zur Sicherheitskette“; ohne Daten „Auf diesem Gerät nicht gespeichert.“.
- `tests/Zeltlotse.Ui.Tests/OfflineModusTests.cs` (neu) + bestehende Seiten-/Layout-Tests -- zuerst: Abgleich (nur laufend, Schalter, 403/404 löscht, nicht mehr gelistet löscht), Warteschlange (Erfolg, Netzfehler, 409, 403), Ablauf, Abmelden löscht, Layout/Seiten offline.
- `tests/Zeltlotse.E2E.Tests/OfflineModusTests.cs` (neu) -- laufende Freizeit wie `NotfallTests` aufbauen; Flugmodus: Notfallkarte und Tagesansicht öffnen sich, axe ohne Verstoß; IndexedDB enthält keinen Klartext (z. B. Medikament nicht im Rohinhalt); Vorfall offline → online → in Liste vom Server; Abmelden leert IndexedDB; Browser-Uhr nach Freizeitende → offline gelöscht.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Abschnitt Story 6-5 (eigene laufende Freizeit mit erfundenen Namen und eigener Leitungs-Mail, nicht Jonas): `offline-sicherheitskette`, `offline-notfallkarte`, `offline-vorfall-wartet`, `offline-tagesansicht`, je Handy und Laptop.
- `src/Zeltlotse.Api/Demo/*` -- nur falls für Doku nötig ergänzen (erfundene Daten); Begründung in Design Notes.
- `doku/src/content/docs/mitarbeitende/offline.md` (neu) + Verweise in `notfall.md`, `vorfall-protokoll.md`, `kueche/tagesansicht.md`, `daten` -- was offline geht, wann gespeichert und gelöscht wird; ohne Anrede.

**Acceptance Criteria:**
- Given ein Teammitglied hat eine laufende Freizeit online geöffnet, when das Gerät in den Flugmodus geht und die App neu lädt, then öffnen sich Sicherheitskette, Notfallkarte einer gesuchten Person und Küchen-Tagesansicht; alle anderen Bereiche zeigen „Offline nicht verfügbar.“.
- Given offline geschriebene Vorfall-Einträge, when die Verbindung zurückkommt, then sieht die Leitung sie mit dem Zeitpunkt des Schreibens, und die Offline-Leiste verschwindet.
- Given die Freizeit ist zu Ende oder die Person meldet sich ab, when die App startet (auch ohne Netz), then liegen keine Offline-Daten dieser Freizeit mehr auf dem Gerät.
- Given Story fertig, then `dotnet test` vollständig grün inklusive Architekturtests, Doku-Screenshots erzeugt, `npm run build` in `doku/` fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 36 findings — high 0, medium 7, low 27, false 2, maybe-false 0
- findings:
  - `[medium]` `[patch]` Blind: Offline-Eintrag gilt als wartend, obwohl `OfflineSchreibenAsync` Fehler schluckt (Quote, kein IndexedDB) – Text geht verloren — `VorfallEinreihenAsync` liest den Datensatz zurück und meldet sonst `false`; Text bleibt im Feld.
  - `[medium]` `[reject]` Blind: 401 beim Senden der Warteschlange löscht wartende Einträge — Intent verlangt ausdrücklich: abgelaufene Sitzung löscht die ganze Offline-Datenbank (Design Note „401 löscht mit“).
  - `[low]` `[reject]` Blind: Vom Server abgelehnte Einträge (400/409) verschwinden ohne Hinweis — Matrix „Wieder online“ schreibt Entfernen vor; Texte über 2000 Zeichen offline sind selten, ein Hinweis wäre zusätzliche Logik.
  - `[low]` `[reject]` Blind: `erfasstAm` ohne Untergrenze — Intent legt `min(erfasstAm, jetzt)` fest, Design Note schließt eine Untergrenze aus.
  - `[false]` `[reject]` Blind: Ohne `freizeitpaesse-sehen` kein Vorfall offline erreichbar — `FreizeitRechte.Freizeitpaesse` = Leitung und Team, also hat jede Rolle mit Offline-Daten den Schlüssel; Unbestätigte sind nicht in der Notfallkarte, offline ist das ohne Belang (gering, keine Änderung).
  - `[low]` `[reject]` Blind: Zwei Tabs können nach dem Abmelden neu anlegen — braucht laufenden Abgleich im zweiten Tab genau beim Abmelden; Abhilfe wäre tab-übergreifende Logik.
  - `[low]` `[patch]` Blind: Kein regelmäßiger Abgleich bei lange offener App; Doku-Satz übertreibt — Intent schließt Live-Aktualisierung aus; Doku-Satz in `offline.md` auf die echten Zeitpunkte korrigiert.
  - `[medium]` `[patch]` Blind: Vorfall-Protokoll bleibt nach Wiederverbinden im Offline-Stand, wenn nichts wartete — Neuladen, sobald online und (`_offline` oder eine vorher wartende Kennung fehlt).
  - `[low]` `[reject]` Blind: Notfallkarte liest auch online nur vom Gerät — Task schreibt „liest immer aus dem Speicher“ vor; die Seite ist nur über die Offline-Suche erreichbar.
  - `[low]` `[reject]` Blind: `FreizeitenAsync` entschlüsselt alle Datensätze — bei rund 20 Datensätzen je Freizeit vernachlässigbar.
  - `[low]` `[reject]` Blind: Notfallkarte-Endpunkt fragt je Person einzeln — Muster wie Notfallordner; bei 100 Personen lokal im Sekundenbereich, Umbau auf Sammelabfrage ist kein kleiner Fix.
  - `[false]` `[reject]` Blind: Gescheiterte Entschlüsselung löscht Daten auch bei kurzzeitigem Schlüsselfehler — Lesefehler werfen (Promise-Reject), liefern nicht `null`; ohne Schlüssel sind die Daten ohnehin nicht mehr lesbar.
  - `[medium]` `[patch]` Blind: Keine Tests für die Fehlerpfade — mit den Tests der Verification-Gap-Befunde ergänzt.
  - `[medium]` `[patch]` Verification Gap: Kontolöschung löscht Offline-Datenbank, nie geprüft — Test in `MeineDatenSeitenTests` ergänzt.
  - `[low]` `[patch]` Verification Gap: Text „Notfallkarte aufs Gerät geladen“ in Oberfläche und Auskunft nicht geprüft — Asserts ergänzt.
  - `[low]` `[patch]` Verification Gap: Abgleich-Auslöser im Layout für noch nicht gespeicherte laufende Freizeit ungetestet — bUnit-Test ergänzt.
  - `[medium]` `[patch]` Verification Gap: Generation-Schutz (kein Neuanlegen nach Abmelden) ungetestet — Test mit angehaltener Antwort ergänzt.
  - `[low]` `[patch]` Verification Gap: Netzfehler-Rückfall in Tagesansicht und Vorfall-Protokoll sowie Tageswechsel offline ungetestet — drei Tests ergänzt.
  - `[low]` `[patch]` Verification Gap: Online gescheitertes Speichern ohne gespeicherte Freizeit behält Text — Test ergänzt.
  - `[low]` `[reject]` Edge: Dauerhafter 5xx auf einen Eintrag blockiert den ganzen Abgleich — braucht einen Serverfehler nur für diesen Eintrag; Verhalten ist bewusst getestet (`Serverfehler_beim_Senden_…`).
  - `[low]` `[reject]` Edge: Abgelehnte Einträge (u. a. über 2000 Zeichen) still verworfen — gleiche Ursache wie oben, Intent schreibt Entfernen vor.
  - `[low]` `[reject]` Edge: Kopf wird vor der Kette geschrieben; Abbruch dazwischen → Box verweist auf „nicht gespeichert“ — Zielseite zeigt dann Notfallordner-Satz und 112; Kopf zuletzt schreiben ließe Gesundheitsdaten ohne Kopf zurück, die das Löschen nicht mehr gelisteter Freizeiten nicht findet.
  - `[low]` `[reject]` Edge: Notfall-Box zeigt nach Mitternacht des Freizeitendes noch die Freizeit — App müsste über das Freizeitende offline offen bleiben; Zielseite liest geprüft und zeigt dann „nicht gespeichert“.
  - `[low]` `[reject]` Edge: `erfasstAm` weit in der Vergangenheit — wie oben, Intent legt die Regel fest.
  - `[low]` `[patch]` Edge: Offline sagt der Hinweis „Jeder Eintrag ist sofort für die Leitung sichtbar“ — offline eigener Satz.
  - `[low]` `[reject]` Edge: Gespeichertes JSON älterer App-Version ohne Felder → Absturz — eine ältere Form gibt es nicht; Story legt das Format erstmals an.
  - `[low]` `[reject]` Edge: `AbgelaufenAsync` fängt andere Ausnahmen beim Löschen nicht — `JsBrowser` fängt `JSException`; andere Ausnahmen beim Löschen sind nicht gezeigt, Abhilfe wäre ein zusätzlicher Wächter.
  - `[medium]` `[patch]` Edge: Gleiche Anzahl wartender Einträge nach Senden und Neu-Einreihen → gesendeter Eintrag verschwindet — gleiche Ursache und gleicher Fix wie „Vorfall-Protokoll bleibt im Offline-Stand“ (Vergleich der Kennungen).
  - `[low]` `[reject]` Edge: Notfallkarte-Endpunkt ohne Obergrenze, Abfragen je Person — wie oben (Performance, kein kleiner Fix).
  - `[low]` `[reject]` Edge: Abbruch mitten im Protokollieren hinterlässt Einträge ohne Auslieferung — höchstens zu viel protokolliert; gleiches Muster wie Notfallordner.
  - `[low]` `[reject]` Edge: Layout startet Abgleich auch ohne Leitung/Team-Rolle erneut — nur beim Wechsel der Freizeit, Ergebnis ist ein leerer Abgleich; Abhilfe wäre zusätzlicher Wächter.
  - `[low]` `[reject]` Intent: Protokolltext „aufs Gerät“ statt „auf Gerät“ — gleiche Bedeutung; Tasks nennen „aufs Gerät geladen“.
  - `[low]` `[reject]` Intent: Notfallkarte-Seite online nur aus dem Speicher — wie oben, so vom Task vorgegeben.
  - `[low]` `[reject]` Intent: E2E kann Gerätezeit nicht von Serverzeit unterscheiden — Senden von `erfasstAm` per bUnit, Übernahme per HTTP-Test belegt.
  - `[low]` `[reject]` Intent: Löschpfade (403/404, nicht mehr gelistet, 401) nur gegen nachgebildeten Browser — dieselben JS-Funktionen laufen im E2E-Test (Freizeit-Löschen über Ablauf, Alles-Löschen über Abmelden).
  - `[low]` `[patch]` Intent: Zusätzlicher Layout-Auslöser nicht eigens geprüft — gleiche Ursache wie der Verification-Gap-Befund zum Layout-Auslöser.

### 2026-10-22 — Review pass (Folgedurchlauf)
- verdicts: 35 findings — high 0, medium 6, low 27, false 2, maybe-false 0
- findings:
  - `[low]` `[reject]` Intent: Verschlüsselung, Ablaufgrenze und IV/AAD nur über einen E2E-Pfad im echten Browser — der E2E-Test prüft Rohinhalt ohne Klartext, einen Geräte-Schlüssel, Löschen nach Freizeitende und beim Abmelden; die Grenze 00:00 Berlin ist in C# (`Ablauf`) geprüft. Mehr Browser-Tests ohne benannten Fehler.
  - `[false]` `[reject]` Intent: Kein Test „falsche Rolle 403“ für die Notfallkarte — Leitung und Team sind die einzigen Freizeit-Rollen und beide dürfen lesen; eine Rolle mit 403 gibt es nicht (Design Note „Falsche Rolle 403“).
  - `[low]` `[reject]` Intent: Demo-Daten nicht erweitert — die Demo-Freizeit liegt 2027 und erreicht nie Phase *läuft*; die Screenshots bauen eine eigene laufende Freizeit über echte Endpunkte (Design Note „Nur Phase läuft“).
  - `[false]` `[reject]` Intent: Doku-Build im Diff nicht belegt — `npm run build` lief in der ersten Sitzung und in diesem Durchlauf fehlerfrei (69 Seiten).
  - `[low]` `[reject]` Intent: Protokolltext „aufs Gerät“ statt „auf Gerät“ — carried: gleiche Bedeutung; Tasks nennen „aufs Gerät geladen“.
  - `[low]` `[reject]` Intent: Jede 409 (nicht nur `freizeit-abgeschlossen`) entfernt den Eintrag — jede 409 ist endgültig (z. B. Kennung einer anderen Freizeit); sonst bliebe der Eintrag ewig hängen (Design Note „Warteschlange und Fehler“).
  - `[low]` `[reject]` Intent: 403/404 für einen einzelnen Tag löscht die ganze Freizeit — Intent: 403/404 „für eine Freizeit“ löscht deren Daten; der Abgleich fragt nur Tage zwischen erstem und letztem Tag ab, 404 kommt nur, wenn die Freizeit nicht mehr verfügbar ist.
  - `[low]` `[reject]` Intent: Layout stößt den Abgleich auch ohne Sitzung (bei Gespeichertem) und nach dem Laden einer laufenden Freizeit an — ohne Sitzung antwortet die API mit 401 und die Daten werden gelöscht, wie verlangt; der Zusatz-Auslöser ist getestet.
  - `[low]` `[reject]` Intent: Netzfehler bei `navigator.onLine` = true gilt als offline — so entworfen (Design Note: online gescheiterte Speicherung landet in der Warteschlange); im Zeltlager meldet der Browser schwaches Netz oft nicht.
  - `[low]` `[reject]` Intent: Notfallkarte-Seite liest online nur aus dem Speicher — carried: so vom Task vorgegeben.
  - `[low]` `[reject]` Intent: E2E prüft Kopf und Sicherheitskette nur indirekt — die Offline-Seiten, die daraus lesen, sind genau die Oberfläche, an der der Intent sie verlangt.
  - `[low]` `[reject]` Intent: E2E kann Gerätezeit nicht von Serverzeit unterscheiden — carried: Senden per bUnit, Übernahme per HTTP-Test belegt.
  - `[medium]` `[reject]` Blind: Offline geschriebene Einträge vom letzten Abend gehen um Mitternacht nach dem letzten Tag ungesendet verloren — Intent verlangt ausdrücklich: jeder Datensatz (auch die Warteschlange) trägt `ablauf` = Freizeitende und wird beim Start, vor jedem Lesen und bei jedem Abgleich gelöscht. Die Doku sagt das jetzt ausdrücklich (siehe Patch zur Doku).
  - `[low]` `[reject]` Blind: Jedes Wiederverbinden startet einen vollen Abgleich ohne Drosselung — Intent nennt „wenn die Verbindung zurückkommt“ als Auslöser; das Protokoll gilt höchstens 1×/Tag; eine Drosselung wäre zusätzliche Logik.
  - `[low]` `[reject]` Blind: Gescheiterte Schreibvorgänge im Abgleich unsichtbar, Notfall-Box meldet „gespeichert“ nach dem Kopf allein — carried: gleiche Stelle wie „Kopf wird vor der Kette geschrieben“; die Zielseite zeigt dann „nicht gespeichert“ mit Notfallordner-Satz und 112.
  - `[low]` `[reject]` Blind: Offline-Seiten zeigen nicht, wie alt die Daten sind — nicht verlangt; der Abgleich läuft bei jedem Öffnen und Wiederverbinden; ein Stand-Zeitpunkt wäre eine neue Funktion.
  - `[low]` `[patch]` Blind: Offline-Suche findet nur Personen mit Notfallkarte, Doku sagt „wie gewohnt“ — Satz in `offline.md` korrigiert („die Personen, deren Notfallkarte auf dem Gerät liegt“).
  - `[low]` `[reject]` Blind: Suchfeld fehlt ohne gespeicherte Notfallkarte, ohne Erklärung — Leitung und Team haben beide `freizeitpaesse-sehen`; tritt nur bei gescheitertem Schreiben auf.
  - `[medium]` `[patch]` Blind: Doku übertreibt Schlüssel („lässt sich nicht auslesen“) und Löschen („um Mitternacht, auch ohne Netz“) — `offline.md` und `daten.md` sagen jetzt: Schlüssel bleibt im Browser und lässt sich aus Zeltlotse heraus nicht weitergeben; ab Mitternacht nicht mehr angezeigt und beim nächsten Öffnen gelöscht; ungesendete Einträge gehen dabei verloren.
  - `[low]` `[reject]` Blind: Abmelden meldet Erfolg, auch wenn das Löschen blockiert oder scheitert — bei `onblocked` löscht der Browser, sobald das andere Fenster schließt; `JSException` beim Löschen ist nicht gezeigt; ein Wiederholungs-Merker wäre zusätzliche Logik (Restrisiko bleibt vermerkt).
  - `[low]` `[reject]` Blind: Die vier Fragen-Kennungen stehen doppelt (Server-Konstanten, Client-Literale) — die App darf `Personen.Contracts` nicht referenzieren; eine umbenannte Kennung bräche ohnehin alle gespeicherten Antworten; kein benannter Aufrufer, der abweicht.
  - `[low]` `[reject]` Blind: Notfallkarte schreibt je Abgleich N Protokoll-Aufrufe — höchstens 1×/Tag gespeichert; reine Performance, gleiches Muster wie die abgelehnte Einzelabfrage je Person.
  - `[low]` `[reject]` Blind: `erfasstAm` ohne Plausibilitätsprüfung — carried: Intent legt `min(erfasstAm, jetzt)` fest, keine Untergrenze.
  - `[low]` `[reject]` Blind: Testlücken (Ablauf der Warteschlange, 401 beim Senden, teilweises Schreiben, blockiertes Löschen, geändertes Ende) — einzeln in den übrigen Zeilen behandelt; das Ablaufdatum wird bei jedem Schreiben aus dem frischen `LetzterTag` berechnet.
  - `[medium]` `[reject]` Edge: 401 beim Senden der Warteschlange löscht wartende Einträge — carried: Intent verlangt, dass eine abgelaufene Sitzung die ganze Datenbank löscht.
  - `[medium]` `[reject]` Edge: Eintrag vom letzten Tag läuft vor dem Senden ab — gleiche Ursache wie der Blind-Befund oben; vom Intent so festgelegt, Doku ergänzt.
  - `[low]` `[reject]` Edge: Ein Eintrag mit dauerhaftem 5xx blockiert Warteschlange und Abgleich — carried: bewusst so getestet (`Serverfehler_beim_Senden_…`).
  - `[low]` `[reject]` Edge: Abgelehnte Einträge (400/409) verschwinden ohne Hinweis — carried: Matrix schreibt Entfernen vor.
  - `[low]` `[reject]` Edge: Während eines Abgleichs eingereihter Eintrag wird von `FreizeitLoeschenAsync` mit gelöscht — tritt nur auf, wenn die Freizeit nicht mehr verfügbar oder nicht mehr laufend gelistet ist, also genau dann, wenn der Intent wartende Einträge mit löschen lässt; dazu ein enges Zeitfenster.
  - `[low]` `[reject]` Edge: 403/404 für einen Tag löscht die ganze Freizeit — gleiche Ursache und Begründung wie der Intent-Befund dazu.
  - `[low]` `[reject]` Edge: `erfasstAm` weit in der Vergangenheit — carried: Intent legt die Regel fest.
  - `[medium]` `[reject]` Edge (Claim): Akzeptanzkriterium „Leitung sieht offline geschriebene Einträge“ gilt nicht in allen Fällen (401, Ablauf, 400/409, Löschen während des Abgleichs) — jeder dieser Wege ist vom Intent vorgegeben (siehe die Zeilen oben); die Doku nennt sie jetzt.
  - `[medium]` `[patch]` Verification Gap: `offlineFreizeitLoeschen` (Löschen je Freizeit über den Index) läuft nie im echten Browser — E2E-Schritt 9 ergänzt: eine fremde, nicht gelistete Freizeit mit Kopf, Notfallkarte und wartendem Eintrag wird beim nächsten Abgleich vollständig gelöscht, die eigene bleibt (`LaufendeFreizeit.FremdeFreizeitAnlegenJs`, `OhneFreizeitAsync`).
  - `[low]` `[patch]` Verification Gap: Netzfehler beim Tageswechsel der Tagesansicht (online geladen) ungetestet — bUnit-Test `Tagesansicht_online_geladen_dann_Tageswechsel_mit_Netzfehler_kommt_aus_dem_Speicher` ergänzt.
  - `[low]` `[reject]` Verification Gap (Other): Triage-Zeile „dieselben JS-Funktionen laufen im E2E-Test“ stimmt für das Löschen je Freizeit nicht — Korrektur wäre eine Änderung am Spec; die Lücke selbst ist mit E2E-Schritt 9 geschlossen.

## Design Notes

- **Nur Phase *läuft*:** AD-18 legt das Ende fest, nicht den Beginn. Gesundheitsdaten sollen nicht länger als nötig auf Geräten liegen; Notfall-Box und „Notfall“ gibt es ebenfalls nur in *läuft* (EXPERIENCE.md). Folge: Doku-Screenshots brauchen eine eigens angelegte laufende Freizeit (Muster `NotfallTests`), weil die Demo-Freizeit 2027 liegt; die Demo-Daten bleiben unberührt, die Bilder zeigen möglichst keine Tagesdaten.
- **Eigener Endpunkt statt Notfallordner:** Der Notfallordner liefert alle freigegebenen Fragen und verlangt Kennungen vom Client. Die Notfallkarte bestimmt die Personen selbst und liefert nur `allergien`, `medikamente`, `krankheiten`, `notfallkontakte` (CAP-15 plus Krankheiten als notfallrelevant). Eigener Protokoll-Zweck, damit Personen sehen, dass ihre Angaben auf ein Gerät geladen wurden.
- **Falsche Rolle 403:** Alle Freizeit-Rollen (Leitung, Team) dürfen lesen; eine Rolle in der Freizeit, die 403 ergäbe, gibt es nicht. Org-Admin ohne Freizeit-Rolle → 404 nach Hauskonvention (wie `NotfallordnerTests`).
- **Gerätezeit `erfasstAm`:** Optional, Server nimmt `min(erfasstAm, jetzt)`; eine Untergrenze gibt es nicht (Team darf ohnehin frei protokollieren). Ohne Feld unverändert Serverzeit.
- **Warteschlange und Fehler:** 400/409 sind endgültig → Eintrag entfernen, sonst bliebe er ewig hängen; Netzfehler → bleibt. Online gescheiterte Netz-Speicherung landet ebenfalls in der Warteschlange statt als Fehlertext.
- **Schlüssel in IndexedDB:** Nicht exportierbarer `CryptoKey` lässt sich strukturiert speichern; Löschen der Datenbank löscht Schlüssel und Daten zusammen. 401 löscht mit, weil die Daten an die Sitzung gebunden sind.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*Notfallkarte*"` und `*VorfaelleTests*` -- grün (gezielt während der Umsetzung)
- `dotnet test --project tests/Zeltlotse.Ui.Tests` und `dotnet test --project tests/Zeltlotse.ArchitectureTests` -- grün
- `dotnet test` (ganze Lösung, einmal am Ende) -- grün
- `dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-trait "Kategorie=Doku"` -- Screenshots `offline-*-handy.png`/`-laptop.png` erzeugt
- `npm run build` in `doku/` -- fehlerfrei

## Auto Run Result

**Zusammenfassung:** Offline-Modus nach AD-18 umgesetzt. Neuer Endpunkt `GET /api/freizeiten/{id}/personen/notfallkarte` (Leitung/Team, nur vier Notfallfragen, Einsicht-Protokoll „Notfallkarte aufs Gerät geladen“ höchstens 1×/Tag). Vorfall-Einträge nehmen `erfasstAm` als `min(erfasstAm, jetzt)` an. In der Web-App gleicht `OfflineAbgleich` laufende Freizeiten in eine AES-GCM-verschlüsselte IndexedDB ab (Ablauf = Freizeitende). Tagesansicht, Sicherheitskette, Notfallkarte (neu) und Vorfall-Protokoll sind offline nutzbar. Vorfall-Einträge warten offline in einer Warteschlange. Abmelden, Kontolöschung und 401 löschen die Datenbank. Die Umsetzung lief über zwei Sitzungen (Zwischenstand-Commit `191548ba79dff8f85a8042e6816f916a28596f91`); die Baseline bleibt `8075fb3…`, damit der Review-Diff die ganze Story umfasst.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Personen/.../Freizeitpass/Notfallkarte.cs`, `PersonenModul.cs`, `EinsichtProtokoll.cs`, `PersonenAuskunft.cs`: neuer Endpunkt und Protokoll-Zweck.
- `src/Modules/Sicherheit/.../VorfaelleBearbeiten.cs`, `Api.Contracts/Sicherheit/Vorfaelle.cs`: Gerätezeit `erfasstAm`.
- `src/Zeltlotse.App/Offline/*`: Offline-Speicher, Abgleich mit Warteschlange, Kennzeichnung `[OfflineNutzbar]`.
- `src/Zeltlotse.App/wwwroot/js/zeltlotse.js`, `Browser/IBrowser.cs`, `JsBrowser.cs`: IndexedDB und WebCrypto hinter `IBrowser`.
- `Layout/MainLayout.razor`, `App.razor`, `OfflineLeiste.razor`: Notfall-Box mit „Notfallhilfe öffnen“, offline nutzbare Seiten, Zeile mit wartenden Einträgen.
- `Features/Kueche/Tagesansicht.razor`, `Features/Sicherheit/{Sicherheitskette,Notfallkarte,VorfallProtokoll}.razor`, `Freizeiten/Schnellsuche.razor`: Lesen aus dem Speicher, Warteschlange.
- `AnmeldungAbschluss.cs`, `AppSitzung.cs`, `MeineDaten.razor`: Offline-Datenbank löschen.
- Tests: `Api.Tests/NotfallkarteTests.cs`, `VorfaelleTests.cs`, `Ui.Tests/OfflineModusTests.cs` (+ Nachbildung des Browsers), `E2E.Tests/OfflineModusTests.cs`, `LaufendeFreizeit.cs`, `DokuScreenshotTests.cs` (Abschnitt Story 6-5), `E2EHilfe.cs` (Login je Mail-Adresse nacheinander).
- Doku: neue Seite `mitarbeitende/offline.md`, ergänzt `notfall.md`, `vorfall-protokoll.md`, `kueche/tagesansicht.md`, `erste-schritte/zeltlotse-oeffnen.md`, `daten.md`; 8 Screenshots `offline-*-{handy,laptop}.png`. Demo-Daten unverändert (eigene laufende Freizeit im Screenshot-Test, siehe Design Notes).

**Review:** 36 Befunde (high 0, medium 7, low 27, false 2).
- Korrigiert (patch): Rücklesen nach dem Einreihen (kein stiller Textverlust); Vorfall-Protokoll lädt nach Wiederverbinden neu (Vergleich der Kennungen); eigener Offline-Hinweis im Vorfall-Protokoll; Doku-Satz zum Abgleichzeitpunkt; neue Tests für Kontolöschung, Protokolltext in Oberfläche und Auskunft, Layout-Auslöser, Generation-Schutz beim Abmelden, Netzfehler-Rückfall (Tagesansicht, Vorfall), Tageswechsel offline sowie gescheitertes Speichern ohne gespeicherte Freizeit.
- Abgelehnt: alle übrigen Befunde mit Begründung im Review Triage Log. Die meisten sind vom Intent so vorgegeben (401 löscht alles, 400/409 entfernt Einträge, keine Untergrenze für `erfasstAm`, Notfallkarte liest immer aus dem Speicher, keine Live-Aktualisierung) oder geringfügig und selten.
- Zurückgestellt: keine.
- Patches nach Stufe: medium 6 Einträge (Rücklesen, Neuladen Vorfall-Protokoll, Fehlerpfad-Tests, Kontolöschung-Test, Generation-Test, Gruppe Kennungsvergleich), low 7.

**Folge-Review empfohlen: ja.** Konkretes Risiko: Das Neuladen des Vorfall-Protokolls nach dem Wiederverbinden und das Rücklesen in `VorfallEinreihenAsync` sind nur gegen den nachgebildeten Browser (bUnit) geprüft, nicht im echten Browser.

**Verifikation:**
- Gezielt: `NotfallkarteTests` und `VorfaelleTests` 34/34; Ui.Tests 924/924, nach den Patches 933/933; ArchitectureTests 136/136.
- Am Ende je Testprojekt: Ablaeufe 10, Api 1081, Architecture 136, Finanzen 65, Freizeiten 93, Inventar 34, Konten 26, Kueche 119, Organisationen 58, Personen 157, Programm 109, SharedKernel 134, Sicherheit 36, Ui 933 – alle grün.
- E2E inklusive Doku-Screenshots: 208/208 grün.
- `npm run build` in `doku/`: fehlerfrei (69 Seiten).
- Die Screenshots aus früheren Stories, die der Lauf nur mit Rauschen neu erzeugt hat (Uhrzeit, Datum, QR-Codes, Tokens), sind zurückgesetzt.

**Restrisiken:**
- Wartende Einträge gehen bei einer abgelaufenen Sitzung (401) mit verloren; so vom Intent gewollt.
- Der Löschpfad, der im Browser blockiert wird (zweites offenes Fenster), ist nicht eigens getestet.
- Mehrere Tabs teilen den Generation-Schutz nicht.

### Folgedurchlauf 2026-10-22

**Zusammenfassung:** Frischer Review mit vier Layern über den ganzen Story-Diff seit `8075fb37356293fccce646aa6563abfadc9ad036`. Kein Produktionscode geändert; ergänzt wurden ein Browser-Test, ein bUnit-Test und genauere Doku-Sätze.

**Geänderte Dateien:**
- `tests/Zeltlotse.E2E.Tests/OfflineModusTests.cs`: neuer Schritt 9 prüft im echten Browser das Löschen einer nicht mehr gelisteten Freizeit samt wartendem Eintrag.
- `tests/Zeltlotse.E2E.Tests/LaufendeFreizeit.cs`: Hilfen `FremdeFreizeitAnlegenJs` und `OhneFreizeitAsync`.
- `tests/Zeltlotse.Ui.Tests/OfflineModusTests.cs`: Test für den Tageswechsel der Tagesansicht bei Netzfehler nach dem Online-Laden.
- `doku/src/content/docs/mitarbeitende/offline.md`, `doku/src/content/docs/daten.md`: Suche offline, Schlüssel, Löschen nach Freizeitende und Verlust ungesendeter Einträge genau beschrieben.

**Review:** 35 Befunde (high 0, medium 6, low 27, false 2).
- Korrigiert (patch): medium 2 (E2E-Löschpfad, Doku zu Schlüssel und Löschen), low 2 (Doku-Satz zur Suche, bUnit-Test Tageswechsel).
- Zurückgestellt: keine.
- Abgelehnt: 31, jeweils mit Grund im Review Triage Log. Die wichtigsten: Verlust wartender Einträge bei 401 und nach Freizeitende sowie Entfernen bei 400/409 sind vom Intent so vorgegeben (jetzt in der Doku genannt); kein 403-Test, weil keine Freizeit-Rolle 403 bekommt; Demo-Daten bleiben, weil die Demo-Freizeit nie *läuft*.

**Folge-Review empfohlen: nein.** In diesem Folgedurchlauf wurde kein `high` korrigiert. Das Risiko aus der ersten Runde (Löschpfade nur gegen den nachgebildeten Browser) ist für das Löschen je Freizeit jetzt im echten Browser belegt.

**Verifikation:**
- `dotnet test --project tests/Zeltlotse.Ui.Tests -- --filter-class "*OfflineModusTests"`: 44/44 grün.
- `dotnet test --project tests/Zeltlotse.E2E.Tests -- --filter-class "*OfflineModusTests"`: 1/1 grün (echter Browser, 2:30 min).
- `npm run build` in `doku/`: fehlerfrei (69 Seiten).
- Kein Komplettlauf der Lösung: Kein Produktionscode wurde geändert, nur Tests und Doku; Screenshots unverändert.

**Restrisiken:**
- Vorfall-Einträge, die bis Mitternacht nach dem letzten Tag oder vor einer abgelaufenen Sitzung nicht gesendet sind, gehen verloren (Intent-Vorgabe, jetzt dokumentiert).
- Ein blockiertes Löschen der Datenbank beim Abmelden (zweites Fenster offen) ist weiter nicht eigens getestet.
