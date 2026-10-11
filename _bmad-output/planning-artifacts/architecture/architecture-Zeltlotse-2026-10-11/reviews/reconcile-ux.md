---
type: reconcile-review
subject: ARCHITECTURE-SPINE.md (Stand 2026-10-11)
against:
  - ../../../ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md
  - ../../../ux-designs/ux-Zeltlotse-2026-10-10/DESIGN.md
  - ../../../ux-designs/ux-Zeltlotse-2026-10-10/.memlog.md
created: 2026-10-11
---

# Abgleich Architektur-Gerüst ↔ UX-Spezifikation

## Urteil

Das Gerüst trägt die UX-Grundhaltung gut: keine Pop-ups, Autosave mit Rückgängig, Inline-Konflikthinweis, Offline-Umfang, Dunkel-Tokens, Druck per Browser, Push nach dem Web-Push-Standard. Bei **fünf Stellen ist es aber mit der UX unvereinbar oder macht einen UX-Ablauf unmöglich.** Das sind: Familienübersicht über mehrere Organisationen hinweg (gegen AD-6), Zugangslinks, die mehrfach gezeigt und gedruckt werden (gegen AD-11 „nur als Hash“), Push für den QR-Teilnehmer-Zugang (gegen AD-5 und AD-11), Login-Link und installierte PWA auf dem iPhone sowie Vorfall-Protokoll mit Speichern pro Feld (gegen AD-18 „nur anhängend“). Dazu kommen mehrere Lücken: Die Oberfläche bekommt Rollen, Phase und Module nirgends gesagt. Es fehlen eine vierte Phase „Nachbereitung“, ein Ladezeit- und Größenbudget für WebAssembly, ein Grund-Offline-Gerüst vor V6 und eine Regel für Live-Anzeige in Kanälen und Vorfall-Notizen.

Begriffe: **SSR** = Server-Side Rendering (der Server schickt fertiges HTML); **WASM** = WebAssembly (die App läuft als heruntergeladener Code im Browser); **PWA** = installierbare Web-App; **Service Worker** = Hintergrundskript im Browser für Offline-Zwischenspeicher und Push.

## Befunde nach Schwere

| Nr. | Schwere | Thema | Betroffene AD |
|---|---|---|---|
| U-1 | Kritisch | Familienübersicht / „Meine Freizeiten“ über mehrere Organisationen | AD-6, AD-12 |
| U-2 | Hoch | Anmeldelinks und QR-Link mehrfach zeigen, kopieren, drucken | AD-11 |
| U-3 | Hoch | Push und PWA-Hinweis im QR-Teilnehmer-Zugang | AD-5, AD-11, AD-21, AD-16 |
| U-4 | Hoch | Login-Link und installierte PWA (iPhone) | AD-12 |
| U-5 | Hoch | Vorfall-Protokoll: Speichern pro Feld vs. „nur anhängend“ | AD-18, AD-19 |
| U-6 | Hoch | Oberfläche kennt Rollen, Phase, Module und Freigabe-Stufe nicht | AD-12, AD-17, AD-20 |
| U-7 | Hoch | Gast-Anmeldung und Freizeitpass ohne Konto in statischem SSR | AD-5, AD-11 |
| U-8 | Mittel | Phase „Nachbereitung“ fehlt | AD-17, AD-18 |
| U-9 | Mittel | „Geändert, noch nicht mitgeteilt“ und „Alle informieren?“ über Modulgrenzen | AD-2, AD-4, AD-13 |
| U-10 | Mittel | Live-Anzeige: Kanäle, Vorfall-Notizen „sofort“ | AD-19 |
| U-11 | Mittel | Ladezeit/Größe WebAssembly, Prerendering, Ladeanzeige | AD-5 |
| U-12 | Mittel | Offline-Grundgerüst vor V6, Abgleich-Auslöser, Sitzungsdauer | AD-18, AD-12 |
| U-13 | Mittel | Autosave pro Feld vs. Zeilen-Versionsprüfung (`xmin`) | AD-19, Konvention „Schreiben“ |
| U-14 | Mittel | Öffentliche Bilder/Dokumente/Logo vs. „keine öffentlichen URLs“ | AD-15, AD-7 |
| U-15 | Mittel | Status-Abfragen im Cockpit vs. Einsicht-Protokoll | AD-8 |
| U-16 | Mittel | Erste-Hilfe-Entnahmen → Schäden & Verluste | AD-2, AD-4 |
| U-17 | Mittel | Schnellsuche nach Personen (auch offline) | AD-3, AD-7, AD-18 |
| U-18 | Mittel | Mandanten-Kontext: Herkunft in der Anfrage | AD-6 |
| U-19 | Niedrig | Zähler „neue Nachrichten“ braucht Lesestand | Datenbesitz Kanäle |
| U-20 | Niedrig | „Keine Berechtigung“ / „Modul aus“: einheitliche Antwort | AD-12, AD-20 |
| U-21 | Niedrig | Barrierefreiheit: Fokus, Ansagen, automatische Prüfung | Konventionen, Tests |
| U-22 | Niedrig | Statische Dateien (ELKW-CSS, Sarabun) selbst ausliefern | Konvention „Gestaltung“ |
| U-23 | Niedrig | Rechenvorschau im Client (Beitragsrechner, Gerichttausch) | AD-5, AD-13 |
| U-24 | Niedrig | Freizeitpass-Metadaten: „Stimmt das noch?“, Einwilligungsdatum | AD-14, Konvention „Schreiben“ |
| U-25 | Niedrig | Upload in zwei Schritten, Fotos am Handy | AD-15 |
| U-26 | Niedrig | QR-Code-Erzeugung und abgelaufene Links freundlich anzeigen | AD-11 |

---

## U-1 – Familienübersicht über mehrere Organisationen (Kritisch)

- **UX-Stelle:** EXPERIENCE.md → „Einstieg nach Rolle“ (Eltern: Familienübersicht, je Kind eine Card mit *seinen Freizeiten* und Anmeldestatus); Flächen „Familienübersicht“, „Meine Freizeiten“ (Team); NS-3 Schritt 8 (dieselben Nachweise später für das EJW freigeben).
- **Widerspruch:** Die Anmeldungen besitzt das Modul Freizeiten, und dort trägt jede Zeile eine `OrganisationId`. Ein Kind kann bei CVJM und EJW angemeldet sein. Die Familienübersicht muss also Anmeldungen **über mehrere Mandanten hinweg** lesen. AD-6 erlaubt das Umgehen des Mandantenfilters aber nur dem Lösch-Dienst. Außerdem kennt AD-12 nur Rollen „im Kontext Organisation bzw. Freizeit“. Eltern haben in der Freizeit keine Rolle, ihr Recht kommt aus der **Familienbeziehung** (Modul Personen). Gleiches gilt für „Meine Freizeiten“ von Mitarbeitenden in mehreren Organisationen.
- **Fix:** In AD-6 einen zweiten, eng begrenzten Lesepfad zulassen: „Personenbezogene Sicht“. Dafür einen eigenen Endpunkt je Modul, z. B. `GET /api/freizeiten/meine-anmeldungen?personId=…`, der nach `PersonId` statt nach `OrganisationId` filtert. Er liefert nur Felder, die die Person ohnehin sehen darf (Freizeitname, Daten, Status, Kontakt der Leitung). Die Berechtigung prüft er über `Personen.Contracts` („ist Elternteil von bzw. ist selbst“). AD-12 bekommt einen dritten Kontext „Person/Familie“. Der Architekturtest aus AD-6 muss diese Ausnahme namentlich kennen; jede andere Verwendung von `IgnoreQueryFilters` bleibt verboten.

## U-2 – Zugangslinks mehrfach zeigen, kopieren, drucken (Hoch)

- **UX-Stelle:** Component Patterns „QR-Code (Teilnehmer-Zugang)“: Card mit QR-Code, Link als Text, „Drucken“ (A4-Aushang), „Link kopieren“ in den Einstellungen der Freizeit; Fläche „Einstellungen der Freizeit … Anmeldelinks“; NS-4 Schritt 3 (Anmeldelinks kopieren und später erneut verschicken).
- **Widerspruch:** AD-11 speichert jedes Token **nur als Hash**. Damit lässt sich ein Link nur ein einziges Mal anzeigen, nämlich beim Erzeugen. Öffnet die Leitung die Einstellungen später wieder, um den QR-Aushang erneut zu drucken oder den Anmeldelink zu kopieren, müsste ein neues Token entstehen. Das macht bereits gedruckte QR-Codes und verschickte Anmeldelinks ungültig.
- **Fix:** AD-11 nach Link-Art teilen:
  - *Einmal- oder Persönlich-Links* (Login-Link, Org-Einladung, Profil-Übergabe, Nachweis-Einsicht): weiter nur als Hash, einmalig gültig.
  - *Dauerhaft geteilte Links* (Anmeldelink Teilnehmende/Mitarbeitende, QR-Teilnehmer-Zugang, Elterninfo-Link, siehe U-7): Token zusätzlich **verschlüsselt** gespeichert (gleicher Schlüsselweg wie AD-14) oder per HMAC (Schlüssel-Prüfsumme) aus Kennung und Server-Geheimnis ableitbar. Nachgeschlagen wird weiter über den Hash. Dazu kommt ein bewusster Knopf „Link erneuern“, der den alten Link ungültig macht. In der UX ist er noch nicht vorgesehen; die UX sollte ihn ergänzen.

## U-3 – Push und PWA im QR-Teilnehmer-Zugang (Hoch)

- **UX-Stelle:** NS-10 Schritt 4–5 (Paul liest den Kanal „Alle“ und bekommt Push ohne Konto); State Patterns „Push nicht möglich (iPhone)“, „Push abgelehnt“; Responsive „Push … Erlaubnis erst nach Tipp auf ‚Benachrichtigungen einschalten‘“.
- **Widerspruch:**
  1. AD-5 liefert den QR-Zugang als **statisches SSR ohne Interaktivitäts-Laufzeit** aus. Ein Push-Abo braucht aber JavaScript: Service Worker registrieren, Erlaubnis abfragen, `PushManager.subscribe` aufrufen, Abo an den Server schicken.
  2. AD-11 sagt: „Der QR-Teilnehmerzugang erlaubt **nur Lesen**.“ Ein Push-Abo anzulegen ist ein Schreibzugriff.
  3. Auf dem iPhone geht Web-Push nur in einer zum Home-Bildschirm hinzugefügten Web-App. Deren Startadresse (`start_url` im Manifest) muss der QR-Link mit Token sein, nicht die angemeldete WASM-App. Das Gerüst kennt nur *ein* Manifest und *einen* Service Worker (`Web.Client/wwwroot/service-worker.js`). Registriert die QR-Seite diesen Service Worker, lädt das Kinder-Handy womöglich die ganze WASM-App in den Zwischenspeicher.
  4. Push-Abos ohne Konto haben keinen Datenbesitzer und keine Löschregel (AD-16).
- **Fix:**
  - AD-5 ergänzen: Öffentliche Seiten dürfen **kleines, eigenständiges JavaScript** ohne Blazor-Laufzeit enthalten (Push-Knopf, Kopieren, Fokus). Für den QR-Zugang gibt es ein **eigenes Manifest und einen eigenen Service Worker mit eigenem Gültigkeitsbereich** (z. B. Pfad `/t/{token}/`), die nichts von der WASM-App vorab laden.
  - AD-11 präzisieren: Der QR-Zugang liest Freizeitdaten nur; als einzige Schreibaktion darf er ein Push-Abo anlegen und löschen.
  - Datenbesitz Kanäle: „Push-Abo (Person **oder** QR-Zugang)“. Ein Abo am QR-Zugang verfällt mit dem Ende des Zugangs und wird vom Lösch-Dienst entfernt.

## U-4 – Login-Link und installierte PWA (Hoch)

- **UX-Stelle:** Foundation/Responsive (PWA-Installation ist Teil des Onboardings für Mitarbeitende, NS-3 Schritt 3); Push am iPhone nur in der installierten Web-App; Fläche „Login“ (Login-Link aus Mail als Standard).
- **Lücke:** Auf iOS hat eine zum Home-Bildschirm hinzugefügte Web-App **eigene Cookies und einen eigenen Speicher**, getrennt von Safari. Ein Login-Link aus der Mail öffnet sich in Safari. Die installierte App bleibt also abgemeldet. Die Cookie-Sitzung aus AD-12 kommt dort nie an. Damit funktionieren auf dem iPhone weder Team-Push noch Offline-Daten.
- **Fix:** AD-12 ergänzen: Die Login-Mail enthält neben dem Link einen **kurzen Einmal-Code** (z. B. 6–8 Zeichen, gleiche Regeln wie AD-11: Zweck, Ablauf, Hash, begrenzte Fehlversuche). Die Login-Seite der App bietet „Code eingeben“ an. Das optionale Passwort bleibt die zweite Möglichkeit. Den Ablauf einmal auf echtem iPhone prüfen und in die Onboarding-Schritte der UX aufnehmen.

## U-5 – Vorfall-Protokoll: Speichern pro Feld vs. „nur anhängend“ (Hoch)

- **UX-Stelle:** Component Patterns „Vorfall-Protokoll“: „Speichert jede Angabe beim Verlassen des Feldes; jeder Eintrag ist sofort für die Leitung sichtbar“; NS-6 Schritt 5; State „Offline-Eintrag wartet“.
- **Widerspruch:** Ein Eintrag hat mehrere Felder (was passiert ist, Medikament, Uhrzeit, Pflaster). Wird jedes Feld beim Verlassen gespeichert, entsteht ein Eintrag mit dem ersten Feld und wird danach **verändert**. AD-18 erlaubt offline aber „ausschließlich anhängend“. AD-19 verlangt für bearbeitbare Entitäten Versionsprüfung, die offline nicht funktioniert. Unklar ist auch, ob ein Vorfall-Eintrag online nachträglich bearbeitet werden darf. Für Nachweisbarkeit wäre das heikel.
- **Fix:** Festlegen, dass das Vorfall-Protokoll **aus unveränderlichen Feld-Einträgen** besteht. Jedes „Verlassen des Feldes“ hängt einen Eintrag an: Vorfall-Kennung (vom Client erzeugt), Feld, Wert, Zeit, Autor, Eintrags-Kennung für Idempotenz. Die Anzeige zeigt je Feld den neuesten Wert; der Verlauf bleibt erhalten. „Rückgängig“ ist dann ebenfalls ein angehängter Eintrag. Das Vorfall-Protokoll aus AD-19 ausnehmen. „N Einträge warten auf Verbindung“ zählt dann Feld-Einträge oder gruppiert nach Vorfall. Das mit der UX abstimmen.

## U-6 – Die Oberfläche kennt Rollen, Phase und Module nicht (Hoch)

- **UX-Stelle:** „Sichtbarkeit nach Rolle“ (Nicht-Erlaubtes wird **nicht gezeigt**, kein gesperrter Knopf); „Modul aus → Bereich erscheint nicht“; Navigation abhängig von Rolle (Eltern ohne Navigation, Team ohne Einstellungen); Notfall-Eintrag nur zwischen Start- und Enddatum; „gelaufen“ → Bearbeiten-Knöpfe fehlen; „Freizeitpass sichtbar ab Bestätigung“; „Einsicht bis TT.MM.JJJJ“; „Startseite der zuletzt geöffneten Freizeit“.
- **Lücke:** AD-12 sagt nur, dass der Server prüft und Ausblenden nie die Sicherung ist. Wie die WASM-Oberfläche erfährt, *was sie zeigen soll*, ist nicht festgelegt. Ohne Festlegung baut jedes Modul eigene „darf ich?“-Abfragen, oder die Oberfläche errät Rechte. Das widerspricht dem Zweck des Gerüsts, Konsistenz zwischen getrennt gebauten Teilen herzustellen.
- **Fix:** Einen **Kontext-Endpunkt** als Konvention aufnehmen: `GET /api/freizeiten/{id}/kontext` liefert eigene Rollen, eingeschaltete Module (AD-20), Phase (siehe U-8), Schreibschutz und die eigene Freigabe-Stufe; dazu `GET /api/konten/ich` mit Organisationen, Freizeiten, Familien-Kindern und der zuletzt geöffneten Freizeit. Er wird geladen beim Öffnen und beim Zurückkehren in die App (wie AD-19). Die Oberfläche blendet ausschließlich anhand dieser Antwort aus. Die serverseitige Prüfung bleibt unverändert.

## U-7 – Gast-Anmeldung und Freizeitpass ohne Konto (Hoch)

- **UX-Stelle:** Fläche „Anmeldung Teilnehmende“ (Schritte, „Weiteres Kind anmelden“ übernimmt Elternangaben *nur in derselben Sitzung, auf dem Gerät wird nichts gespeichert*, „Konto aus diesen Daten erstellen?“ mit einem Tipp); NS-1 Schritt 5 („Freizeitpass für Paul ausfüllen“ direkt nach der Gast-Anmeldung); Fläche „Elterninfo per Link“ (FR-68, ohne Konto).
- **Lücken und Widersprüche:**
  1. AD-5 macht diese Seiten zu statischem SSR. Mehrschrittformular, Fehlerübersicht und „Weiteres Kind“ brauchen dann Formular-Posts mit **Zwischenstand auf dem Server**. Wo dieser Stand liegt (verschlüsseltes verstecktes Feld per Data Protection, Sitzung im Server-Speicher, Entwurf in der DB), ist offen. Er enthält Personendaten.
  2. Ob ein Gast den **Freizeitpass ohne Konto** ausfüllen kann, ist offen. Wenn ja, wäre das eine öffentliche Seite mit Speichern pro Feld (Autosave), und das geht in statischem SSR nicht. Sie bräuchte außerdem einen eigenen Zugangslink.
  3. Der **Elterninfo-Link** fehlt in der Link-Liste von AD-11 und in der Liste öffentlicher Seiten in AD-5.
  4. „Ein Tipp, und das Familienkonto steht“: Unklar ist, ob das Konto sofort angemeldet ist oder erst nach Bestätigung per Login-Link aus der Mail.
- **Fix:** In AD-5 ergänzen: Zwischenstand mehrschrittiger öffentlicher Formulare als **Data-Protection-geschütztes verstecktes Feld**. Damit gibt es keinen Server-Zustand und nichts bleibt im Gerätespeicher; das passt zu „auf dem Gerät wird nichts gespeichert“. Entscheiden und festhalten: Freizeitpass **nur angemeldet** (empfohlen; „Konto erstellen“ verschickt sofort den Login-Link bzw. -Code aus U-4) **oder** über einen zweckgebundenen Freizeitpass-Link nach AD-11 in der WASM-App. Den Elterninfo-Link in AD-5 und AD-11 aufnehmen.

## U-8 – Phase „Nachbereitung“ fehlt (Mittel)

- **UX-Stelle:** „Startseite der Freizeit wechselt mit der Phase“: vier Phasen (Vorbereitung, Während, **Nachbereitung** ab dem Tag nach dem Enddatum bei Status „läuft“, Abgeschlossen). Notfall-Box und Notfall-Eintrag verschwinden in der Nachbereitung.
- **Lücke:** AD-17 kennt nur die drei Status und sagt, „die Phase“ liefert `Freizeiten.Contracts`. Die vierte, abgeleitete Phase ist nicht benannt. Offen ist auch, ob die Offline-Daten (AD-18: „mit dem Ende … der Freizeit gelöscht“) am Enddatum oder erst beim Abschluss gelöscht werden.
- **Fix:** AD-17 um eine abgeleitete **Phase** mit vier Werten erweitern: `Vorbereitung | Laeuft | Nachbereitung | Gelaufen`. Sie wird nach Kalendertag in Europe/Berlin berechnet und gehört zum Kontext-Endpunkt (U-6). In AD-18 festlegen, dass die Offline-Notfalldaten mit Beginn der Nachbereitung gelöscht werden, spätestens beim Ende des Freigabefensters.

## U-9 – „Alle informieren?“ und „Geändert, noch nicht mitgeteilt“ (Mittel)

- **UX-Stelle:** NS-9; Component Patterns „Wochenraster“; State „Änderung nicht mitgeteilt“: Der Hinweis bleibt am Programmpunkt, *bis jemand informiert oder ausblendet*, auch wenn die Seite ohne Antwort verlassen wurde.
- **Lücke:** Der Hinweis ist **geteilter, gespeicherter Zustand** am Programmpunkt, nicht nur ein Zustand in der Oberfläche. Programm darf Kanäle nicht aufrufen (AD-2), und Kanäle hängt nicht von Programm ab. Wer den Merker setzt und löscht, ist offen. Zwei getrennte Aufrufe aus der Oberfläche (Nachricht senden, dann Merker löschen) können halb gelingen.
- **Fix:** Programm besitzt den Merker `Mitteilung offen` am Programmpunkt. Er wird **serverseitig beim Ändern der Uhrzeit gesetzt**, nicht erst bei „Nein“. Die Oberfläche sendet nach „Ja, informieren“ die Nachricht an Kanäle mit Bezug `ProgrammpunktId`. Kanäle schreibt das Ereignis `NachrichtGesendet(Bezug)`, Programm abonniert es und löscht den Merker. „Ausblenden“ ist ein eigener PATCH in Programm. Damit gilt AD-13 weiter: Gesendet wird nur auf ausdrückliche Bestätigung.

## U-10 – Live-Anzeige: Kanäle und Vorfall-Notizen (Mittel)

- **UX-Stelle:** State „Neue Vorfall-Notiz: Neue Einträge erscheinen **sofort** oben in der Liste“; Vorfall-Protokoll „für die Leitung sofort sichtbar“; Kanal-Nachrichten (Chat-artig, neueste unten).
- **Widerspruch:** AD-19 schließt Live-Aktualisierung aus („lädt beim Öffnen bzw. beim Zurückkehren neu“). Eine geöffnete Kanal-Seite zeigt neue Nachrichten dann erst nach Neuladen, obwohl gleichzeitig ein Push eintrifft.
- **Fix:** Entweder in der UX festhalten, dass „sofort“ heißt: *ohne Freigabeschritt, sichtbar beim nächsten Laden*. Oder in AD-19 eine schmale Ausnahme aufnehmen: **Abfragen in festem Takt (Polling) nur auf geöffneter Kanal-Seite und Leitungs-Startseite** (z. B. alle 30 s, nur bei sichtbarem Tab). Zusätzlich gibt der Service Worker eingehende Push-Nachrichten an offene Fenster weiter, die dann neu laden. SignalR bleibt zurückgestellt.

## U-11 – Ladezeit und Größe der WebAssembly-App (Mittel)

- **UX-Stelle:** Grundhaltung „schnell an die Information“; NS-6 „In Sekunden sieht er Allergien …“; Handy zuerst für Notfall, Küche, Kanäle; State „Laden: Text ‚Wird geladen …‘ mit `aria-busy`; kein Spinner“; DESIGN.md „Toasts, Spinner … gesperrt“.
- **Lücke:** Das Gerüst legt kein Budget für Download und Start der WASM-App fest. Im Lager mit schlechtem Netz und bei der ersten Nutzung am Handy ist das entscheidend. Offen sind außerdem: Wird die App auf dem Server vorgerendert (Prerendering; das stünde im Konflikt mit „Web.Client spricht nur über HTTP“ und mit Offline)? Werden selten gebrauchte Module (Küche, Finanzen) bei Bedarf nachgeladen (Lazy Loading)? Die Blazor-Vorlage zeigt beim Start eine Kreis-Animation, die die UX verbietet.
- **Fix:** Konvention ergänzen:
  - Größenbudget, z. B. Erstdownload komprimiert ≤ 5 MB, Brotli-Kompression, IL-Trimming (Entfernen ungenutzten Codes), keine AOT-Kompilierung ohne Messung; Küche und Finanzen als nachladbare Assemblies.
  - Start bei Folgeaufrufen aus dem Service-Worker-Zwischenspeicher, auch ohne Netz.
  - **Kein Prerendering** für die angemeldete App.
  - Eigene Startanzeige „Wird geladen …“ in `index.html` statt der Vorlage.
  - Messziel auf einem Mittelklasse-Android: Kaltstart aus dem Zwischenspeicher bis zur Startseite ≤ 3 s.
  - Öffentliche SSR-Seiten laden keine WASM-Dateien (gilt schon nach AD-5; als prüfbare Regel festhalten).

## U-12 – Offline-Grundgerüst vor V6, Abgleich und Sitzungsdauer (Mittel)

- **UX-Stelle:** Offline-Leiste „global“, sofort bei Verbindungsverlust; „Offline – zuletzt aktualisiert HH:MM“; „Offline nicht verfügbar.“ in ausgegrauten Bereichen; State „Offline vor V6“ (die Sicherheitskette verweist offline auf den Notfallordner); Notfall-Box offline nutzbar; „Keine zeitgesteuerten Abläufe, die Eingaben verwerfen“.
- **Lücken:**
  1. AD-18 ordnet *alles* Offline V6 zu. Offline-Leiste, ausgegraute Bereiche, Notfall-Box mit „Notruf 112“ und der Verweis auf den Notfallordner setzen aber voraus, dass die **App-Hülle schon vor V6 offline startet**. Sonst zeigt der Browser seine eigene Fehlerseite.
  2. Wann die Offline-Daten auf das Gerät geladen werden und wann die Warteschlange gesendet wird, ist offen. Das Background-Sync-API (Hintergrund-Abgleich) fehlt auf iOS.
  3. Sitzungsdauer: Läuft das Cookie im Lager ab, kommen Warteschlange und Autosave mit 401 zurück. Ein neuer Login per Mail-Link ohne Netz ist nicht möglich.
- **Fix:**
  - AD-18 teilen: **ab V1** Service Worker mit App-Hülle, Offline-Erkennung, Offline-Leiste und Notfall-Box mit 112-Link; **V6** verschlüsselte Daten und Warteschlange.
  - Abgleich-Auslöser festlegen: beim Öffnen der App, beim Ereignis `online`, beim Zurückkehren in die App; Laden der Offline-Daten in der Phase „Läuft“ bei jedem Öffnen mit Netz; Zeitstempel „zuletzt aktualisiert“ speichern.
  - AD-12 ergänzen: gleitende Sitzung mit langer Laufzeit für angemeldete Geräte (mindestens Dauer einer Freizeit, z. B. 30 Tage). Bei 401 bleiben Eingabe und Warteschlange erhalten, und die Oberfläche bietet die Anmeldung an Ort und Stelle an (Code aus U-4).

## U-13 – Autosave pro Feld vs. Versionsprüfung pro Zeile (Mittel)

- **UX-Stelle:** Interaction Primitives „Automatisch speichern beim Verlassen des Feldes … Rückgängig“; Material-Status „ein Tipp setzt den Status, speichert sofort“; „Ich übernehme“ als Ein-Tipp-Umschalter; State „Speichern gescheitert … Erneut versuchen“.
- **Lücke:** `xmin` gilt für die ganze Zeile. Bearbeiten zwei Teammitglieder verschiedene Felder desselben Programmpunkts, bekommt der zweite einen `409`-Konflikt, obwohl nichts überschrieben würde. Bei Speichern pro Feld und Teamarbeit passiert das häufig. „Rückgängig = erneuter PATCH mit altem Wert“ braucht außerdem das **neue** Concurrency-Token aus der Antwort des ersten PATCH. Und „Ich übernehme“ als Umschalter verträgt keine Konflikte, wenn zwei gleichzeitig tippen.
- **Fix:** Konvention „Schreiben“ präzisieren:
  - Jeder PATCH liefert das neue Token zurück; Rückgängig nutzt es.
  - Bei `409` vergleicht der Server **feldgenau**: Der Client schickt den vorher gesehenen Feldwert mit; ist nur ein anderes Feld geändert, wird ohne Konflikt übernommen.
  - Umschalter („Ich übernehme“, „Ich bringe das mit“, Abhaken) sind eigene Befehle mit Ziel-Zustand (`übernehmen`/`abgeben`), keine Feld-PATCHes; sie sind idempotent.

## U-14 – Öffentliche Bilder, Dokumente, Logo (Mittel)

- **UX-Stelle:** Fläche „Freizeitseite (öffentlich)“: Team **mit Bild und Herkunft**, öffentliche Dokumente (FR-43, drei Sichtbarkeiten); „Öffentliche Seiten … oben Name (und Logo) der Organisation“; Teilnehmer-Zugang „allgemeine Infos“ (Hausordnung).
- **Widerspruch/Lücke:** AD-15 kennt „keine öffentlichen URLs; nur App-Endpunkt mit Rechteprüfung“. Ein anonymer Besucher der Freizeitseite hat aber keine Rolle. Teambilder und Herkunft sind Personendaten der Mitarbeitenden (Modul Personen, AD-7). Für ihre Veröffentlichung fehlt eine Einwilligung bzw. ein Freigabe-Typ.
- **Fix:** AD-15 ergänzen: Ausgabe-Endpunkte akzeptieren als Berechtigung auch ein **gültiges Link-Token** (Anmeldelink, QR-Zugang) für Dateien mit Sichtbarkeit „öffentlich“ bzw. „Teilnehmende“. Das Org-Logo ist ausdrücklich öffentlich. AD-8 bzw. Datenbesitz Personen ergänzen: Einwilligung „auf der Freizeitseite gezeigt werden (Bild, Herkunft)“ je Mitarbeitende und Freizeit; ohne Einwilligung nur der Vorname.

## U-15 – Status-Abfragen im Cockpit vs. Einsicht-Protokoll (Mittel)

- **UX-Stelle:** Leitungs-Cockpit „Freizeitpass fehlt / unvollständig“, „3 Freizeitpässe unvollständig“; Personenliste mit Status; Familienübersicht „Freizeitpass prüfen“; Zugriffsprotokoll „Wer hatte wann Einsicht“.
- **Lücke:** AD-8 protokolliert *jedes Lesen von Freizeitpass-Daten*. Das Cockpit fragt den Status aller Personen bei jedem Öffnen ab. Ohne Unterscheidung füllt sich das Zugriffsprotokoll der Eltern mit Einträgen, die keine Einsicht in Inhalte waren. Das Protokoll wird damit für Eltern wertlos und für die Leitung irreführend.
- **Fix:** In AD-8 zwei Lesearten trennen: **Status** (vollständig/unvollständig/veraltet, ohne Inhalte, nicht protokolliert oder nur gesammelt) und **Inhalt** (protokolliert, je Person und Sitzung höchstens einmal pro Zeitfenster, z. B. 1 h). `Personen.Contracts` bietet dafür eine Massen-Statusabfrage für eine Liste von Personen und Fragen-Kennungen an.

## U-16 – Erste-Hilfe-Entnahmen landen unter Schäden & Verluste (Mittel)

- **UX-Stelle:** NS-6 Schritt 6 „Entnahmen aus dem Erste-Hilfe-Kasten landen **von selbst** unter ‚Schäden & Verluste‘“; Fläche „Schäden & Verluste … Erste-Hilfe-Entnahmen“.
- **Lücke:** Das Vorfall-Protokoll besitzt Sicherheit, Schäden besitzt Inventar. Sicherheit darf Inventar nicht aufrufen (keine Kante in AD-2). Der Weg über Ereignisse ist nicht beschrieben. Ereignisse tragen nur Kennungen (AD-4); Inventar braucht aber Artikel und Menge, ohne Personenbezug. Offline erfasste Entnahmen kommen erst nach dem Abgleich an.
- **Fix:** Entnahme als **eigenes Feld bzw. eigenen Eintragstyp** im Vorfall-Protokoll mit Artikel-Kennung (Inventar) und Menge. Ereignis `ErsteHilfeEntnahmeErfasst(EntnahmeId, FreizeitId, InventarArtikelId, Menge)` ohne Personenbezug, das Inventar abonniert. In der Anforderungstabelle des Gerüsts unter V2/V3 eintragen.

## U-17 – Schnellsuche nach Personen (Mittel)

- **UX-Stelle:** Component Patterns „Schnellsuche“ (ab dem ersten Buchstaben, Treffer öffnet den Freizeitpass; auf Startseite, Personen, App-Leiste und Sicherheitskette); NS-6 („Le…“ im Notfall, auch offline); „Personen“ am Handy bündelt Anmeldungen und Freizeitpässe.
- **Lücke:** Die Anforderungstabelle sagt „Suche in Freizeiten“. Namen liegen aber in Personen (AD-7), Freizeiten hält nur Kennungen. Eine Server-Abfrage pro Tastendruck über zwei Module ist am Handy im Lager zu langsam und offline unmöglich.
- **Fix:** Festlegen: Die Oberfläche lädt beim Öffnen der Freizeit **einmal die Personenliste** (Kennung, Name, Rolle, Anmeldestatus; zusammengesetzt aus Freizeiten und Personen) und filtert lokal. Offline (V6) ist diese Liste Teil der verschlüsselten Notfallkarte. In der Anforderungstabelle FR-28 so umformulieren.

## U-18 – Mandanten-Kontext: woher in der Anfrage? (Mittel)

- **UX-Stelle:** Wechsel von Organisation und Freizeit über eine eigene Seite; mehrere Organisationen pro Person; Wortmarke führt zur „aktuellen Freizeit“.
- **Lücke:** AD-6 setzt den Filter „aus dem Mandanten-Kontext der Anfrage“, legt aber nicht fest, woher der Kontext kommt (Pfad, Header, Cookie). Ein Cookie-basierter „aktueller Mandant“ führt bei zwei Browser-Tabs in verschiedenen Organisationen zu falschen Daten.
- **Fix:** Konvention: Organisations- bzw. Freizeit-Kennung **steht immer im Pfad** (`/api/freizeiten/{freizeitId}/…`, `/api/organisationen/{orgId}/…`). Der Mandanten-Kontext wird daraus abgeleitet und gegen die Mitgliedschaft geprüft. Die zuletzt geöffnete Freizeit ist nur eine Komfort-Angabe aus `GET /api/konten/ich` (U-6).

## U-19 – Zähler „neue Nachrichten“ braucht einen Lesestand (Niedrig)

- **UX-Stelle:** Seitennavigation „Anzahl … bei Kanäle (neue Nachrichten)“ als reiner Text.
- **Lücke:** Der Datenbesitz von Kanäle kennt keinen Lesestand je Person und Kanal.
- **Fix:** „Lesestand (Person, Kanal, zuletzt gelesen)“ in den Datenbesitz von Kanäle aufnehmen; Löschung über AD-16.

## U-20 – Einheitliche Antwort bei „nicht erlaubt“ und „Modul aus“ (Niedrig)

- **UX-Stelle:** State „Keine Berechtigung: Nicht gezeigt; ein direkter Link führt zur eigenen Startseite mit dem Hinweis ‚Diese Seite ist nicht verfügbar.‘“; „Modul aus“.
- **Lücke:** AD-20 antwortet mit `404`. Für fehlende Rechte (AD-12) ist kein Statuscode festgelegt; die Oberfläche bräuchte für beide Fälle dasselbe Verhalten.
- **Fix:** Konvention: Fehlende Rechte im Kontext ebenfalls als `404` (verrät nicht, dass es die Ressource gibt). Die Oberfläche behandelt `404` bei Navigation zentral mit Weiterleitung zur Startseite und Meldung. `403` nur für „angemeldet, aber Aktion nicht erlaubt“ innerhalb einer sichtbaren Seite.

## U-21 – Barrierefreiheit WCAG 2.2 AA als prüfbare Regel (Niedrig)

- **UX-Stelle:** Accessibility Floor (Fokus auf H1 nach Ansichtswechsel, Fokus nach Inline-Bestätigung auf das Ergebnis, `role="status"`/`alert`, Sprunglink, 24×24 px, 320 px ohne seitliches Scrollen, `aria-sort`, `aria-invalid`).
- **Lücke:** Das Gerüst nennt WCAG nicht. Es gibt keine gemeinsamen Bausteine für die wiederkehrenden Muster und keine automatische Prüfung. Bei Agenten-Bau entstehen sonst zehn verschiedene Inline-Bestätigungen.
- **Fix:** Konventionen ergänzen:
  - **Gemeinsame Blazor-Bausteine in `Web.Client`** für Inline-Bestätigung, Speicher-Hinweis, Meldung, Offline-Leiste, Status, Balken mit Statuswort, Feld mit Fehler. Module bauen diese nicht selbst.
  - `FocusOnNavigate` auf H1 im Router.
  - Gleiche Bausteine als SSR-taugliche Varianten für öffentliche Seiten.
  - Automatische Prüfung mit axe-core (Prüfwerkzeug für Barrierefreiheit) in End-zu-End-Tests (Playwright) bei 375 px und 1440 px, hell und dunkel.

## U-22 – Statische Dateien selbst ausliefern (Niedrig)

- **UX-Stelle:** DESIGN.md Typografie „Sarabun, lokal eingebunden … wichtig für den Offline-Betrieb“; Dunkel-Tokens; Druckbausteine.
- **Lücke:** Die Konvention „Gestaltung“ nennt die ELKW-CSS, sagt aber nicht, dass CSS und Schrift **im Repository liegen und selbst ausgeliefert werden** (kein CDN, kein Google Fonts). Das ist wichtig für Offline, für Datenschutz (keine Drittanbieter-Aufrufe auf öffentlichen Seiten) und für die SSR-Seiten.
- **Fix:** Konvention ergänzen: ELKW-CSS (eingefrorene Version), Zeltlotse-Dunkel-Tokens und Sarabun unter `Web/wwwroot` (gemeinsam für SSR und WASM), vom Service Worker vorab zwischengespeichert; keine externen Ressourcen. Dunkelmodus nur per `prefers-color-scheme`, kein Schalter und kein gespeicherter Zustand.

## U-23 – Rechenvorschau im Client (Niedrig)

- **UX-Stelle:** Beitragsrechner „Regler aktualisieren den Balken sofort“ (FR-57); Gerichttausch mit Vorher/Nachher-Vorschau und „Übernehmen“/„Anpassen“ (FR-50).
- **Lücke:** `Web.Client` darf nur `Contracts` referenzieren. Eine sofortige Neuberechnung beim Schieben braucht die Rechenregel im Client. Für den Gerichttausch fehlt ein Muster „Vorschau ohne Speichern, dann bestätigen“.
- **Fix:** Reine Rechenfunktionen ohne Datenzugriff dürfen in `*.Contracts` liegen (z. B. `Finanzen.Contracts.Beitragsrechnung`) und werden von Server und Client gemeinsam genutzt. Konvention „Vorschau“: `POST …/vorschau` rechnet ohne Speichern; `POST …/uebernehmen` mit dem Stand der Vorschau (Concurrency-Token) übernimmt (AD-13).

## U-24 – Freizeitpass-Metadaten bei Verschlüsselung (Niedrig)

- **UX-Stelle:** State „Veraltete Angabe … älter als 12 Monate: ‚Stimmt das noch?‘“ mit „Stimmt“ (Datum neu); Einwilligungen mit Datum der Erteilung und „widerrufen am“ (NFR-6).
- **Lücke:** AD-14 verschlüsselt die Antworten. Für die 12-Monats-Prüfung und Statusabfragen (U-15) braucht es je Antwort ein **unverschlüsseltes „zuletzt bestätigt am“**. Einwilligungen brauchen einen Nachweis-Verlauf, obwohl die Konvention „Schreiben“ sagt: „kein Server-Verlauf“.
- **Fix:** Datenmodell Personen: Antwort = verschlüsselter Wert + Klartext `bestaetigtAm`. Einwilligung als **Ereignisliste** (erteilt/widerrufen mit Zeit) statt überschreibbarem Schalter, ausgenommen von „kein Server-Verlauf“.

## U-25 – Upload in zwei Schritten und Fotos am Handy (Niedrig)

- **UX-Stelle:** „Datei hochladen … scheitert der Upload, bleibt der Eintrag ohne Datei gespeichert“, „Erneut versuchen“; Beleg per Handyfoto im Lager.
- **Lücke:** Das setzt voraus, dass der Eintrag und die Datei getrennt gespeichert werden. AD-15 sagt das nicht. Große Kamerafotos bei schlechtem Netz scheitern häufig.
- **Fix:** Konvention: Erst den Eintrag anlegen (PATCH/POST), dann die Datei an `…/{id}/datei` hochladen. Der Client verkleinert Fotos vor dem Hochladen (z. B. längste Kante 2000 px, JPEG). Obergrenze für die Dateigröße festlegen.

## U-26 – QR-Code-Erzeugung und abgelaufene Links (Niedrig)

- **UX-Stelle:** QR-Code-Card (schwarz auf Weiß, auch im Dunkelmodus, A4-Druck); „Teilnehmer-Zugang beendet: ‚Diese Freizeit ist zu Ende.‘“; „Dieser Anmeldelink ist nicht mehr gültig“ bzw. „Diese Freizeit ist zu Ende“ mit Kontakt der Leitung.
- **Lücke:** Offen ist, wo der QR-Code entsteht (Bibliothek im Server oder im Client). Für die freundlichen Texte muss ein abgelaufenes Token von einem unbekannten unterscheidbar bleiben. Das heißt: Der Hash-Datensatz bleibt nach Ablauf bis zur Löschfrist bestehen.
- **Fix:** QR als **SVG auf dem Server** erzeugen (eine .NET-Bibliothek, in der Stack-Tabelle nachtragen), damit die Darstellung in App, Druck und Dunkelmodus gleich ist. AD-11 ergänzen: Abgelaufene Tokens bleiben mit Status „abgelaufen“ erhalten, bis die Freizeit nach AD-16 gelöscht wird.

---

## Vom Gerüst bereits gut getragen (keine Änderung nötig)

- Keine Pop-ups/Dialoge, keine Drittanbieter-Komponentenbibliothek (Konvention „Gestaltung“).
- Konflikt-Hinweis an Ort und Stelle (AD-19) passt zu „Inzwischen geändert – neu laden?“.
- Fehlerform ProblemDetails mit Feldfehlern → Inline-Fehler und Fehlerübersicht.
- „Nichts geht ohne Menschen raus“ (AD-13), Mails und Push über die Outbox.
- Offline-Umfang (AD-18) deckt sich mit den State Patterns; die verschlüsselte lokale Ablage passt zu FR-65.
- Abgeschaltete Module ohne Menüpunkt und ohne Endpunkt (AD-20).
- Schreibschutz „gelaufen“ (AD-17) passt zu „nur lesbar“.
- Druck per Browser-CSS mit ELKW-Druckbausteinen, keine PDF-Bibliothek.
- Push ohne Gesundheitsdaten (AD-21) passt zu ruhigen Benachrichtigungen.
