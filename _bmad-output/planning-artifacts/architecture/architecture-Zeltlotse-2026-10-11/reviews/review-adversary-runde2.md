---
type: review
lens: adversary
round: 2
target: ../ARCHITECTURE-SPINE.md
focus: AD-5, AD-22, AD-24, AD-27, AD-28, AD-29, AD-30, Bauprinzip-Tabelle, Quellbaum
created: '2026-10-11'
---

# Gegner-Prüfung Runde 2 – Architektur-Gerüst Zeltlotse

## Urteil

Die neuen Entscheidungen legen die richtigen Werkzeuge fest (Mediator, Zustandsmaschine, Api.Contracts, Komponenten-Bibliothek), lassen aber offen, **wie diese Werkzeuge über Projektgrenzen hinweg zusammengesteckt werden**. Zwei Agenten, die jede AD wörtlich befolgen, bauen in mindestens 16 Fällen Teile, die nicht kompilieren, still Ereignisse verlieren oder sich gegenseitig blockieren. Die drei kritischen Löcher: Die öffentlichen Seiten kommen durch den zentralen Schutz gegen gefälschte Anfragen nicht hindurch (Gast-Anmeldung unmöglich). Mediator mit `internal`-Handlern in zehn Projekten ist ungeklärt. Und der Weg „Zustandsmaschine → Ereignis → Outbox“ ist doppelt auslegbar, wobei `xmin` als Versionsnummer im Umschlag nicht taugt.

**Methode:** Wie in Runde 1. Für jedes Loch werden zwei unabhängig bauende Einheiten konstruiert, die jede AD buchstabengetreu einhalten. Gezeigt wird, wo ihre Ergebnisse kollidieren. Löcher aus `review-adversary.md` (L1–L21), die das Gerüst geschlossen hat, werden nicht wiederholt. Was noch offen ist, steht am Ende.

**Schwere:** **kritisch** (Datenschutz-Verstoß, Datenverlust oder System läuft nicht zusammen), **hoch** (Fehlverhalten im Betrieb, teure Nacharbeit), **mittel** (Inkonsistenz, Reibung).

**Begriffe:** *Command* = Objekt, das eine schreibende Aktion beschreibt und genau einen Handler hat. *Pipeline* = Kette von Zwischenschritten, durch die jeder Command läuft (Validierung, Transaktion, Outbox). *Notification* = Mediator-Nachricht, die an beliebig viele Handler im selben Prozess geht. *Inbox* = Tabelle, in der ein Empfänger verarbeitete Ereignis-Kennungen festhält. *Antiforgery* = Schutz gegen gefälschte Anfragen von fremden Seiten (CSRF).

## Übersicht

| # | Loch | Einheiten | Schwere |
| --- | --- | --- | --- |
| R2-1 | Öffentliche Seiten → API: Antiforgery, Rate-Limiting und Prozessgrenze | Zeltlotse.Public · Zeltlotse.Api | kritisch |
| R2-2 | Mediator-Codegenerierung mit `internal`-Handlern in zehn Modul-Projekten | API-Host · jedes Modul | kritisch |
| R2-3 | Ereignis aus der Zustandsmaschine in die Outbox: zwei Wege, und `xmin` als Versionsnummer | Freizeiten (Entität) · SharedKernel (Pipeline) · Personen (Empfänger) | kritisch |
| R2-4 | Ereignis-Empfänger: Notification oder Command, Inbox pro Empfänger, Mandant | Outbox-Dienst · Personen · Organisationen | kritisch |
| R2-5 | Schreibschutz und Pipeline blockieren Ereignis-Empfänger | Sicherheit · Kanäle · SharedKernel-Pipeline | hoch |
| R2-6 | Autosave-PATCH: Form des Körpers und Zuschnitt der Commands | Zeltlotse.Ui (Autosave) · Programm · Küche | hoch |
| R2-7 | `Zeltlotse.Api.Contracts`: SharedKernel-Typen, Besitz, Routen unter `/api/ich`, Versionierung mit gecachter PWA | alle Module · Zeltlotse.App · Zeltlotse.Public | hoch |
| R2-8 | Validierung: Ort, Bibliothek, Fehlerschlüssel, Fachregel-Fehler | Programm · Küche · Zeltlotse.Ui · Zeltlotse.Public | hoch |
| R2-9 | Architekturtest „Endpunkt schreibt ohne Command“ ist nicht prüfbar | ArchitectureTests · jedes Modul | hoch |
| R2-10 | AD-28-Vorgänge fehlen im Ereignis-Fluss | Personen · Organisationen · Programm · Inventar · Konten | hoch |
| R2-11 | Testpflicht „fremde Organisation“ für `/api/ich`, öffentliche und QR-Endpunkte; 403 oder 404 | jedes Modul · Testprojekte | hoch |
| R2-12 | Logs: Tokens in URLs, Fehlerdetails aus PostgreSQL, Ort des Sicherheitsprotokolls | ServiceDefaults · Konten · Freizeiten · alle Module | hoch |
| R2-13 | Personen-Leseschnittstelle ohne lesenden Menschen (Hintergrund) und Einzelabrufe | Personen · Küche · Freizeiten | hoch |
| R2-14 | Komponenten-Bibliothek in statisch gerenderten Seiten; „nur aus Bausteinen“ vs. Feature-Komponenten | Zeltlotse.Ui.Komponenten · Zeltlotse.Public · Zeltlotse.App | mittel |
| R2-15 | Freizeit-Kontext-Endpunkt: Besitzer und Rechte-Ableitung im Client | Freizeiten · API-Host · Zeltlotse.Ui | mittel |
| R2-16 | Ereignis-Inhalte referenzieren Typen fremder Contracts | Sicherheit · Inventar · ArchitectureTests | mittel |

---

## R2-1 – Öffentliche Seiten → API: Antiforgery, Rate-Limiting und Prozessgrenze

**Schwere:** kritisch

**Einheit A – API-Host (Agent 1):** AD-25 wörtlich: „Jeder ändernde Endpunkt verlangt einen Antiforgery-Header. Das wird zentral im API-Host erzwungen.“ Rate-Limiting für die Gast-Anmeldung nach Client-IP. Der Agent baut eine Middleware, die jeden `POST/PATCH/PUT/DELETE` unter `/api` ohne gültigen Header mit `400` ablehnt.

**Einheit B – Zeltlotse.Public (Agent 2):** AD-5 wörtlich: statisch auf dem Server gerendert, ohne WebAssembly, „holt ihre Daten über die API“. Die Gast-Anmeldung ist ein HTML-Formular. Der Agent hat zwei regelkonforme Wege:
- **B1:** Das Formular postet an den Public-Server, der per `HttpClient` den API-Endpunkt aufruft. Dieser Aufruf hat kein Sitzungs-Cookie und keinen Antiforgery-Header des Browsers → `400`. Alle Eltern kommen von derselben IP (der des Public-Servers) → das Rate-Limiting drosselt alle Eltern gemeinsam, sobald der Anmeldelink in der Gemeinde-WhatsApp-Gruppe landet.
- **B2:** Das Formular postet direkt an `/api/...`. Ein HTML-Formular ohne JavaScript kann keinen Header setzen und sendet `application/x-www-form-urlencoded` statt JSON → `400`/`415`.

**Zusätzlich offen:** Ob Public ein eigener Prozess ist (Aspire-Ressource plus Reverse Proxy für „eine Adresse“) oder ob der API-Host Public mit einbindet, sagt weder AD-5 noch AD-23 noch der Quellbaum. Agent 1 (AppHost) startet zwei Dienste und braucht einen Proxy, den kein Dokument nennt. Agent 2 (Api) bindet Public als Razor-Komponenten ein. Beide sind regelkonform.

**Kollision:** Die Gast-Anmeldung (Kernablauf NS-1, Playwright-Pflicht in AD-30) funktioniert in keiner Kombination. Der naheliegende „Fix“ eines Agenten ist eine Ausnahme vom Antiforgery-Zwang. Die schlägt dann im Architekturtest (AD-24, letzter Punkt) fehl oder wird dort ebenfalls ausgenommen. Damit fehlt der Schutz genau auf dem einzigen anonym erreichbaren Schreib-Endpunkt.

**Vorgeschlagene Regel (AD-5 ergänzen, AD-25 präzisieren):**
> **Public und API:** `Zeltlotse.Public` läuft im selben Prozess wie der API-Host (eine ASP.NET-Core-Anwendung, `/` für Public, `/api` für die API). Formulare der öffentlichen Seiten sind Blazor-SSR-Formulare mit dem eingebauten Antiforgery von Blazor. Der Public-Server ruft die API über einen typisierten Client aus `Zeltlotse.Ui` mit der Dienst-Kennung `PublicSeiten` auf. Der API-Host erkennt diese Kennung nur bei Aufrufen aus dem eigenen Prozess (Loopback oder In-Process-Handler). Für solche Aufrufe gilt der Antiforgery-Header als erfüllt. Die Rate-Limiting-Partition ist dann die vom Public-Server weitergereichte Client-IP. Jeder andere Aufrufer ohne Header bekommt `400`. Der Architekturtest prüft, dass außer dieser Dienst-Kennung keine Ausnahme vom Antiforgery-Zwang registriert ist.

---

## R2-2 – Mediator-Codegenerierung mit `internal`-Handlern in zehn Modul-Projekten

**Schwere:** kritisch

**Einheit A – API-Host (Agent 1):** AD-27: „Mediator (Codegenerierung)“. Der Agent bindet `Mediator.SourceGenerator` im Host ein und ruft `services.AddMediator(...)` einmal auf. Der Generator erzeugt dort Code, der jeden Handler **direkt** aufruft.

**Einheit B – Modul Küche (Agent 2):** Bauprinzip-Tabelle: „alles `internal` außer Modul- und Endpunkt-Registrierung“. Commands, Handler und Pipeline-Verhalten des Moduls sind `internal`.

**Kollision:**
- Code im Host kann `internal`-Typen eines anderen Projekts nicht aufrufen. Nach meinem Kenntnisstand meldet der Generator dann Fehler oder lässt die Handler weg; das muss ein kurzer Spike bestätigen. Agent 2 „löst“ es mit `public`-Handlern (gegen die Bauprinzip-Tabelle) oder mit `InternalsVisibleTo("Zeltlotse.Api")`. Agent 3 im Modul Finanzen bindet den Generator stattdessen im eigenen Modul ein und ruft `AddMediator` in seiner Modul-Registrierung auf.
- Zehn Module mit eigenem Generator registrieren zehnmal `IMediator`. Die Dependency Injection (DI, die automatische Bereitstellung von Diensten) liefert nur die zuletzt registrierte Umsetzung. Commands der anderen neun Module finden dann keinen Handler. Der Fehler zeigt sich erst zur Laufzeit, je nach Reihenfolge der Modul-Registrierung.
- Die Transaktions-Pipeline braucht den `DbContext` **des Moduls**, zu dem der Command gehört. Ein generisches Pipeline-Verhalten in SharedKernel weiß das nicht. Agent 1 löst den `DbContext` über ein Markierungs-Interface auf, Agent 2 über eine Basisklasse, Agent 3 öffnet die Transaktion im Handler selbst.

**Vorgeschlagene Regel (AD-27 ergänzen):**
> **Mediator-Verdrahtung:** `Mediator.SourceGenerator` wird ausschließlich im API-Host eingebunden, `AddMediator` wird genau einmal aufgerufen. Modul-Projekte referenzieren nur `Mediator.Abstractions` und erklären `[assembly: InternalsVisibleTo("Zeltlotse.Api")]` sowie das eigene Testprojekt. Commands, Handler und Validatoren bleiben `internal`. Jeder Command implementiert `IModulCommand<TDbContext>` (SharedKernel). Das Transaktions- und Outbox-Verhalten der Pipeline öffnet die Transaktion auf genau diesem `DbContext`. Handler öffnen nie selbst eine Transaktion. Ein Architekturtest prüft, dass kein Modul-Projekt `Mediator.SourceGenerator` referenziert und jeder Command `IModulCommand<>` mit dem `DbContext` des eigenen Moduls trägt. *Vor der Umsetzung per Spike prüfen, dass der Generator `internal`-Typen über `InternalsVisibleTo` erreicht; sonst `public` in einem Namensraum `*.Intern` mit Architekturtest gegen fremde Nutzung.*

---

## R2-3 – Ereignis aus der Zustandsmaschine in die Outbox: zwei Wege, und `xmin` als Versionsnummer

**Schwere:** kritisch

**Einheit A – Freizeiten (Entität, Agent 1):** AD-28: Die Methode `Anmeldung.Bestaetigen()` „erzeugt das Ereignis“. Der Agent hängt es an eine Liste `Ereignisse` der Entität und erwartet, dass jemand sie einsammelt.

**Einheit B – SharedKernel-Pipeline (Agent 2):** AD-27: „Die Pipeline übernimmt … Outbox.“ Der Agent baut einen Dienst `IOutbox.Hinzufuegen(ereignis)`, den Handler aufrufen. Die Pipeline speichert nach dem Handler, was im Dienst liegt.

**Einheit C – Programm (Agent 3):** Die Entitäts-Methode gibt das Ereignis als Rückgabewert zurück. Der Handler ruft `mediator.Publish(ereignis)` auf, weil Mediator dafür Notifications anbietet.

**Kollision:**
- A + B: Die Ereignisse in der Liste der Entität sammelt niemand ein. `AnmeldungBestaetigt` geht **still verloren**, die Freigabe Stufe Team entsteht nie. Kein Test eines einzelnen Moduls bemerkt das, erst der Durchlauf-Test (AD-30), falls er geschrieben ist.
- A mit einem Sammel-Mechanismus über den ChangeTracker und dazu ein Handler, der zusätzlich `IOutbox.Hinzufuegen` ruft: Das Ereignis landet doppelt mit zwei Ereignis-Kennungen. Die Inbox (AD-4) erkennt es nicht als Doppelung.
- C: `Publish` stellt sofort im Prozess zu, an der Outbox vorbei. Das verletzt AD-4 („gleiche Transaktion“), sieht aber regelkonform aus, weil AD-27 Mediator vorschreibt.
- **Versionsnummer im Umschlag:** AD-4 verlangt die „Versionsnummer des Datensatzes“, und Empfänger verwerfen ältere Versionen. Die einzige Versionsnummer im Gerüst ist `xmin` (AD-19). `xmin` ist die Kennung der schreibenden Transaktion. PostgreSQL vergibt sie beim ersten Schreiben, nicht beim Commit, und sie läuft nach 2³² über. Eine Transaktion mit kleinerer Kennung kann eine Zeile **nach** einer Transaktion mit größerer Kennung ändern. Dann trägt das neuere Ereignis die kleinere Version, und der Empfänger verwirft es als „älter“. Zwei Übergänge in derselben Transaktion haben dieselbe Version. Außerdem ist der neue `xmin` erst nach `SaveChanges` bekannt; das Outbox-Insert im selben `SaveChanges` kennt ihn nicht. Agent 1 nimmt `xmin`, Agent 2 einen eigenen Zähler. Die Empfänger vergleichen dann unvergleichbare Zahlen.

**Vorgeschlagene Regel (AD-28 und AD-4 ergänzen):**
> **Vom Übergang zur Outbox:** Eine Statusmethode legt das Ereignis in die Liste `Ereignisse` der Entität (Basisklasse `Aggregat` in SharedKernel) und erhöht dabei die Eigenschaft `long Folgenummer` der Entität um 1. Ausschließlich das Outbox-Verhalten der Pipeline sammelt nach dem Handler die Ereignisse aller geänderten `Aggregat`-Entitäten aus dem ChangeTracker ein und schreibt sie im selben `SaveChanges` in die Outbox. Die Versionsnummer im Umschlag ist die `Folgenummer`, nie `xmin`. `xmin` dient nur der Versionsprüfung beim Bearbeiten (AD-19). Handler rufen weder `IMediator.Publish` noch einen Outbox-Dienst direkt auf; ein Architekturtest prüft das.

---

## R2-4 – Ereignis-Empfänger: Notification oder Command, Inbox pro Empfänger, Mandant

**Schwere:** kritisch

**Einheit A – Outbox-Dienst (Agent 1):** AD-27: „Ereignis-Empfänger laufen über denselben Mechanismus.“ Der Agent liest Outbox-Zeilen und ruft `mediator.Publish(ereignis)` auf. Personen und Organisationen implementieren je einen `INotificationHandler<AnmeldungBestaetigt>`.

**Einheit B – Personen (Agent 2):** Liest „derselbe Mechanismus“ als „Command mit Pipeline“. Der Agent baut `FreigabeTeamErteilen : ICommand` und erwartet, dass der Outbox-Dienst diesen Command schickt. Die Inbox prüft er im Pipeline-Verhalten.

**Kollision:**
- `Publish` ruft beide Empfänger in einem Aufruf. Scheitert Organisationen nach erfolgreichem Personen-Teil, stellt der Dienst erneut zu, und **beide** laufen wieder. Die Inbox liegt je Modulschema, also muss sie pro Empfänger (Ereignis-Kennung + Empfänger) zählen. AD-4 sagt nur „Inbox nach Ereignis-Kennung“.
- Die Pipeline (Validierung, Transaktion, Inbox) greift bei Notifications je nach Mediator-Version gar nicht oder anders. Der Empfänger von Agent 1 läuft ohne Transaktion und ohne Inbox.
- Der Notification-Handler bekommt nur den Inhalt, nicht den Umschlag. Mandanten-Kontext (AD-6: „bei Ereignissen der Umschlag“) und Versionsnummer (AD-4) muss jemand vorher setzen. Agent 1 setzt den Kontext einmal für alle Empfänger, Agent 2 erwartet ihn im Command.
- Ein Notification-Handler in einem Modul ist wieder `internal` (R2-2).

**Vorgeschlagene Regel (AD-4 und AD-27 ergänzen):**
> **Empfänger-Mechanismus:** Ereignis-Empfänger sind keine Mediator-Notifications. Jeder Empfänger ist ein Command `<Folgeaktion>` mit dem Umschlag als Eigenschaft und genau einem Handler im Zielmodul. Er wird in der Modul-Registrierung mit `EmpfaengtEreignis<TEreignis, TCommand>()` angemeldet. Der Outbox-Dienst stellt jedes Ereignis **je Empfänger getrennt** zu: eigener DI-Scope, Mandanten-Kontext aus dem Umschlag, eigener Wiederholversuch. Das Inbox-Verhalten der Pipeline speichert (Ereignis-Kennung, Empfänger) und die höchste verarbeitete Folgenummer je (Datensatz, Empfänger) in derselben Transaktion wie die Folgeaktion. Ein Architekturtest prüft, dass jedes Paar (Ereignis, Empfänger) in der Tabelle *Ereignis-Fluss* steht und umgekehrt.

---

## R2-5 – Schreibschutz und Pipeline blockieren Ereignis-Empfänger

**Schwere:** hoch

**Einheit A – SharedKernel-Pipeline (Agent 1):** AD-17: „Bei *gelaufen* lehnt jedes Modul Schreibzugriffe für diese Freizeit ab.“ Der Agent macht daraus ein Pipeline-Verhalten für alle Commands mit `FreizeitId`. Das ist zentral, und AD-27 legt Querschnittsprüfungen in die Pipeline.

**Einheit B – Sicherheit und Kanäle (Agent 2):** Empfänger von `FreizeitAbgeschlossen` laufen nach R2-4 als Command. Das Ereignis entsteht genau mit dem Wechsel auf *gelaufen*.

**Kollision:** Der Empfänger „Vorfall-Notizen archivieren“ fällt unter die Ausnahme „Archivierung“, „Gast-Abos beenden“ in Kanäle aber nicht. Die Pipeline lehnt ab, der Outbox-Dienst wiederholt endlos, und die Gast-Abos bleiben bestehen (AD-21: „verfällt mit dem QR-Zugang“). Dasselbe gilt für Empfänger von `PersonGeloescht` in einer gelaufenen Freizeit: Der Lösch-Dienst ist ausgenommen, die Ereignis-Empfänger nicht. Ebenso für AD-20: Agent 1 prüft auch den Funktions-Schalter in der Pipeline, obwohl AD-20 sagt, dass Empfänger weiterlaufen.

**Vorgeschlagene Regel (AD-17 und AD-20 präzisieren):**
> **Geltungsbereich:** Schreibschutz (AD-17) und Funktions-Schalter (AD-20) prüft ausschließlich die Pipeline für Commands mit dem Merkmal `IBenutzerCommand`, also für Commands, die ein Endpunkt auslöst. Ereignis-Empfänger und Lösch-Dienst tragen dieses Merkmal nie. Die Ausnahmen in AD-17 (Erstattungsvermerk, Anschaffungswunsch) werden mit `[ErlaubtNachAbschluss]` am Command erklärt; der Architekturtest kennt die erlaubte Liste namentlich.

---

## R2-6 – Autosave-PATCH: Form des Körpers und Zuschnitt der Commands

**Schwere:** hoch

**Einheit A – Zeltlotse.Ui, Autosave-Baustein (Agent 1):** Konvention „Ein PATCH pro Feld (`{ "feld": wert, "version": n }`)“. Der Agent liest `feld` als Platzhalter für den Feldnamen und sendet `{ "titel": "Geländespiel", "version": 7 }`.

**Einheit B – Programm (Agent 2):** Liest `feld` wörtlich und erwartet `{ "feld": "titel", "wert": "…", "version": 7 }`. AD-27 verlangt einen Command pro schreibender Aktion. Der Agent baut einen generischen Command `ProgrammpunktFeldAendern(Id, string Feld, JsonElement Wert, uint Version)` mit `switch` über den Feldnamen.

**Einheit C – Küche (Agent 3):** Ein Command pro Feld (`RezeptTitelAendern`, `RezeptPortionenAendern`), Route pro Feld (`PATCH /rezepte/{id}/titel`), Antwort mit der ganzen Entität.

**Kollision:**
- Der eine gemeinsame Autosave-Baustein (AD-29) passt höchstens zu einem der Module.
- Der generische Command von Agent 2 nimmt jeden Feldnamen an, auch `status`. Das umgeht AD-28 („Einen Setter für den Status gibt es nicht“), ohne dass ein Test es bemerkt.
- Was die Antwort enthält (nur `version` oder die ganze Entität) und welchen Wert „gespeichert“ anzeigt, wenn der Server normalisiert (z. B. Leerzeichen entfernt), ist offen.
- Noch offen aus L15: `xmin` gilt pro Zeile. Zwei Teammitglieder, die **verschiedene** Felder desselben Programmpunkts bearbeiten, bekommen sich gegenseitig ständig `409`.

**Vorgeschlagene Regel (Konvention „Autosave“ zur Regel in AD-19 erheben):**
> **Autosave-Form:** `PATCH /api/.../<ressource>/{id}` mit dem Körper `{ "version": n, "<feldname in camelCase>": wert }`, genau eine Eigenschaft außer `version`. Antwort `200` mit `{ "version": n, "<feldname>": gespeicherter Wert }`. Pro bearbeitbarer Ressource gibt es genau einen Command `<Ressource>FeldAendern` mit einem geschlossenen Enum der bearbeitbaren Felder. Das Enum liegt in `Zeltlotse.Api.Contracts` und wird je Feld validiert. Statusfelder stehen nie in diesem Enum. Statuswechsel sind `POST /api/.../<ressource>/{id}/<übergang>` mit eigenem Command. `409` gibt es nur, wenn sich **dasselbe Feld** seit der mitgeschickten Version geändert hat (Feldversion oder Vergleich mit dem mitgeschickten alten Wert). Andernfalls wird angewendet.

---

## R2-7 – `Zeltlotse.Api.Contracts`: SharedKernel-Typen, Besitz, Routen unter `/api/ich`, Versionierung

**Schwere:** hoch

**Einheit A – Freizeiten (Agent 1):** Verwendet im Modul streng typisierte Kennungen aus SharedKernel (`FreizeitId`, `PersonId`; Bauprinzip: „Kennungen“ liegen in SharedKernel) und nimmt sie auch in die DTOs (Datenformen) in `Api.Contracts/Freizeiten/` auf. Dafür referenziert `Api.Contracts` SharedKernel.

**Einheit B – Zeltlotse.App (Agent 2):** Referenziert nach AD-5 nur `Api.Contracts`. Über die neue Abhängigkeit landen SharedKernel samt Outbox, EF Core und Verschlüsselung im WebAssembly-Paket. Der Architekturtest (AD-24) prüft nur „Client referenziert kein Modul-Projekt“, SharedKernel ist keins. Der Test bleibt grün.

**Einheit C – Personen (Agent 3) und Freizeiten (Agent 1) für AD-26:** Beide bauen personenbezogene Endpunkte. Konvention: „Personenbezogene Endpunkte unter `/api/ich/...`“, ohne Modul-Segment. Beide registrieren `GET /api/ich/freizeiten` (Personen: „Freizeiten meiner Kinder“, Freizeiten: „meine Anmeldungen“). Beide legen `AnmeldungUebersicht` an, eins in `Api.Contracts/Personen/`, eins in `Api.Contracts/Freizeiten/`.

**Kollision:**
- WebAssembly-Paket wächst um Server-Bibliotheken, oder Agent 2 kopiert die Kennungs-Typen.
- Gleiche Route zweimal → `AmbiguousMatchException`, aber erst beim ersten Aufruf zur Laufzeit.
- Gemeinsame Enums wie `Phase`, `Rolle` und `AnmeldeStatus` definiert jedes Modul selbst. Der Freizeit-Kontext in `Zeltlotse.Ui` kennt drei verschiedene `Phase`-Typen.
- **Versionierung:** Der Service Worker (AD-18) hält die alte App-Version im Lager tagelang. Nach einem Update des Servers spricht sie mit einer API, deren Datenformen umbenannt sind. `Api.Contracts` hat keine Regel zur Kompatibilität.

**Vorgeschlagene Regel (Bauprinzip und AD-5 ergänzen):**
> **Api.Contracts:** Das Projekt referenziert nichts außer der .NET-Basisbibliothek; Kennungen sind dort `Guid`. Jedes Modul schreibt nur in seinen Ordner `Api.Contracts/<Modul>/` mit dem Namensraum `Zeltlotse.Api.Contracts.<Modul>`. Gemeinsame Typen (`Phase`, `Rolle`, Fehlercodes, Freizeit-Kontext) liegen in `Api.Contracts/Gemeinsam/` und ändern sich nur mit einer Änderung dieses Gerüsts. Personenbezogene Endpunkte liegen unter `/api/ich/<modul>/...`. Änderungen an der API sind nur additiv: Ein Feld wird erst entfernt oder umbenannt, wenn die vorige App-Version nicht mehr im Umlauf sein kann. Jede API-Antwort trägt `X-Zeltlotse-Version`; die App bietet bei Abweichung „Neu laden“ an. Der Architekturtest prüft, dass `Api.Contracts` keine Projekt-Referenz hat und kein Modul Typen aus dem Ordner eines anderen Moduls verwendet.

---

## R2-8 – Validierung: Ort, Bibliothek, Fehlerschlüssel, Fachregel-Fehler

**Schwere:** hoch

**Einheit A – Programm (Agent 1):** Nutzt die in .NET 10 eingebaute Validierung von Minimal APIs (`AddValidation()`) mit DataAnnotations an den Request-DTOs in `Api.Contracts`. Ungültige Anfragen enden vor dem Command mit `400`, Fehlerschlüssel `Titel` (Eigenschaftsname).

**Einheit B – Küche (Agent 2):** Konvention „Endpunkt, Handler und Validierung im Slice“ und AD-27 „Pipeline übernimmt Validierung“. Validator am `internal` Command (z. B. FluentValidation), Fehlerschlüssel `RezeptTitel` (Command-Eigenschaft). Fachregel-Verstöße wirft die Entität als Ausnahme, und die wird zu `500`.

**Einheit C – Zeltlotse.Ui und Zeltlotse.Public:** Zeigen Feldfehler direkt am Feld (Konvention „Fehlerform“) und ordnen sie über den JSON-Namen `titel` zu.

**Kollision:** Kein Fehlerschlüssel passt zum Feld der Oberfläche, und die Fehler erscheinen nicht am Feld. Public kann die Validatoren aus B (`internal`, Server) nicht für Hinweise vor dem Absenden nutzen. Ungültige Statusübergänge (AD-28: „lehnen ab“) liefern je nach Agent `400`, `409`, `422` oder `500`. Die Bibliothek ist nicht im Stack festgelegt.

**Vorgeschlagene Regel (AD-27 ergänzen):**
> **Validierung:** Formprüfungen (Pflicht, Länge, Format) stehen als Validator am Command im Slice und laufen in der Pipeline. Bibliothek: *im Stack festlegen*, keine zweite. Fehlerschlüssel in der ProblemDetails-Antwort sind die JSON-Namen (camelCase) des Request-DTOs aus `Api.Contracts`, nie Command-Eigenschaften. Abgelehnte Statusübergänge und andere Fachregeln enden in `409` mit dem Problemtyp `https://zeltlotse/probleme/<code>`; die Codes stehen in `Api.Contracts/Gemeinsam`. Die eingebaute Minimal-API-Validierung wird nicht verwendet.

---

## R2-9 – Architekturtest „Endpunkt schreibt ohne Command“ ist nicht prüfbar

**Schwere:** hoch

**Einheit A – Modul Inventar (Agent 1):** Schreibt Endpunkte als Lambdas direkt in `MapInventarEndpoints()`. Lesend nutzt er nach AD-27 den eigenen `DbContext`. Ein Lese-Endpunkt „Artikel öffnen“ setzt nebenbei `ZuletztAngesehen` und ruft `SaveChangesAsync`.

**Einheit B – ArchitectureTests (Agent 2):** ArchUnitNET prüft Typen und Abhängigkeiten. Lambdas werden zu vom Compiler erzeugten Klassen (`<>c`). Lesende Endpunkte dürfen vom `DbContext` abhängen (AD-27), also unterscheidet eine Abhängigkeitsregel Lesen und Schreiben nicht. Der Agent prüft „Klassen mit Namen `*Endpunkt` rufen kein `SaveChanges`“. Inline-Lambdas fallen durch, ebenso `ExecuteUpdateAsync`, das zusätzlich an der Versionsprüfung und der Outbox vorbeigeht.

**Kollision:** Der Test aus AD-24 ist grün, obwohl Endpunkte schreiben. Ein dritter Agent baut eine „Abfrage-Klasse“, die schreibt. Auch sie prüft niemand.

**Vorgeschlagene Regel (AD-27 und AD-24 ergänzen):**
> **Lesen technisch erzwingen:** Jedes Modul registriert neben dem schreibenden `<Modul>DbContext` einen `<Modul>LeseDbContext` (NoTracking, `SaveChanges*` wirft). Endpunkte und Abfrage-Klassen erhalten nur den Lese-Kontext; den schreibenden Kontext erhalten nur Command-Handler. Endpunkte sind benannte statische Methoden in `internal static class <Fall>Endpunkt`, keine Inline-Lambdas. Der Architekturtest prüft: Kein Typ außer Command-Handlern und Lösch-/Auskunfts-Diensten hängt vom schreibenden `DbContext` ab, und niemand ruft `ExecuteUpdate`/`ExecuteDelete` außerhalb des Lösch-Dienstes auf.

---

## R2-10 – AD-28-Vorgänge fehlen im Ereignis-Fluss

**Schwere:** hoch

AD-28 nennt als Vorgänge mit Status Anmeldung, **Einsicht-Anfrage**, **Profil-Übergabe** und **Material**. Nur die Anmeldung steht in der Tabelle *Ereignis-Fluss*. AD-28 sagt aber: „Modulübergreifende Abläufe sind Ereignis-Ketten nach der Tabelle“.

**Einheit A – Organisationen (Agent 1):** Die prüfende Person klickt „eingesehen“. Der Agent speichert die Nachweis-Bestätigung (AD-9) über einen eigenen Endpunkt in Organisationen.

**Einheit B – Personen (Agent 2):** Führt die Einsicht-Anfrage als Zustandsmaschine (`Angefragt → Eingesehen → Abgelaufen`) und setzt „eingesehen“ über einen eigenen Endpunkt in Personen.

**Einheit C – Programm und Inventar:** Material in Programm hat den Status „reserviert“, die Reservierung besitzt Inventar. Programm darf Inventar lesen, aber nicht schreiben. Ein Ereignis `ReservierungAngelegt` steht nicht in der Tabelle. Agent 3 setzt „reserviert“ im Programm-Endpunkt **vor** dem Inventar-Aufruf, Agent 4 nie.

**Kollision:** Die Oberfläche muss zwei Endpunkte nacheinander aufrufen, ohne gemeinsame Transaktion. Bricht das Netz dazwischen ab, zeigt Personen „eingesehen“, Organisationen hat keine Bestätigung, und die Mitarbeit ist blockiert. Für die Profil-Übergabe gilt dasselbe zwischen Personen und Konten (wer legt das Konto an?).

**Vorgeschlagene Regel (Tabelle *Ereignis-Fluss* vervollständigen, AD-28 ergänzen):**
> Jeder in AD-28 genannte Vorgang hat genau einen Besitzer des Status und seine Zeilen im *Ereignis-Fluss*, mindestens: `NachweisEingesehen` (Personen) → Organisationen: Bestätigung speichern; `ProfilUebergabeAngenommen` (Personen) → Konten: Besitz am Konto setzen; `ReservierungGeaendert` (Inventar) → Programm: Materialstatus. Ein Statusfeld, das von einem anderen Modul abhängt, ändert sich nur durch einen Empfänger aus dieser Tabelle. Jeder Typ in einem `*.Contracts.Events`-Namensraum muss in der Tabelle stehen (Architekturtest, siehe R2-4).

---

## R2-11 – Testpflicht „fremde Organisation“ für `/api/ich`, öffentliche und QR-Endpunkte; 403 oder 404

**Schwere:** hoch

**Einheit A – Freizeiten, `/api/ich/anmeldungen` (Agent 1):** AD-30 wörtlich: Jeder Endpunkt hat den Pflichtfall „fremde Organisation sieht nichts“. Der Agent schreibt einen Test, der von einer Person in Org B die Anmeldung bei Org A abruft und eine leere Liste erwartet. Der Endpunkt soll nach AD-26 aber gerade über Organisationen hinweg liefern. Damit der Test grün wird, filtert der Agent nach Mandant, und die Familienübersicht ist kaputt.

**Einheit B – Personen, gleicher Endpunkt-Typ (Agent 2):** Lässt den Pflichtfall weg, weil er nicht passt. Ein Test, dass eine **fremde Person** nichts sieht, fehlt dann.

**Einheit C – öffentliche Endpunkte und QR-Endpunkte:** „Nicht berechtigt (falsche Rolle)“ gibt es bei anonymen Anfragen nicht. Agent 3 testet nur „berechtigt“.

**Zusätzlich:** Ob „nicht berechtigt“ `403` oder `404` liefert, legt das Gerüst nicht fest (AD-20 nutzt `404`). Agent 1 liefert `403` und verrät damit, dass die Freizeit existiert. Agent 2 liefert `404`. Die Tests und der Client-Umgang mit Fehlern unterscheiden sich.

**Vorgeschlagene Regel (AD-30 präzisieren):**
> **Pflichtfälle je Endpunkt-Art:**
> - Organisation/Freizeit: berechtigt; falsche Rolle im eigenen Mandanten → `403`; fremde Organisation → `404`.
> - `/api/ich/...` (AD-26): selbst; Elternteil der Familie; fremde Person (auch in derselben Organisation) → `404`; die Antwort enthält keine Inhaltsfelder einer Freizeit (Test auf die erlaubte Feldliste).
> - Token-Endpunkte (öffentlich, QR): gültiges Token; abgelaufenes oder erneuertes Token → `404`; Token mit anderem Zweck → `404`; Token für Freizeit X liest Freizeit Y nicht.
>
> AD-26-Endpunkte tragen die Metadaten `.Organisationsuebergreifend()`; der Architekturtest vergleicht sie mit der Liste im Gerüst.

---

## R2-12 – Logs: Tokens in URLs, Fehlerdetails aus PostgreSQL, Ort des Sicherheitsprotokolls

**Schwere:** hoch

**Einheit A – Konten und Freizeiten (Agent 1):** AD-11: „Der Aufruf eines Links zeigt nur eine Seite“. Das Token steht im Pfad (`/anmelden/{token}`, `/elterninfo/{token}`). AD-22 verbietet Namen und Antworten in Logs, aber keine Geheimnisse.

**Einheit B – ServiceDefaults (Agent 2):** Standard-Instrumentierung von OpenTelemetry für ASP.NET Core und `HttpClient`. Sie zeichnet `url.path` bzw. `url.full` auf. Der Aufruf von Public an die API (R2-1) trägt das Token ebenfalls in der URL. Die Browser-Fehlermeldung (Konvention „Logging“: „Seite“) schickt die volle URL.

**Einheit C – ein beliebiges Modul (Agent 3):** Schaltet zur Fehlersuche `Include Error Detail=true` (Npgsql) oder `EnableSensitiveDataLogging` ein. Eine Verletzung der Eindeutigkeit loggt dann `Key (mail)=(eltern@…) already exists`, und die ProblemDetails-Antwort enthält die Ausnahme-Nachricht.

**Sicherheitsprotokoll:** AD-22 verlangt „eine eigene Tabelle“, aber nicht, in welchem Schema. AD-3 erlaubt nur Schreiben ins eigene Schema. Agent 1 schreibt Rollenvergaben aus Freizeiten in `konten.sicherheitsprotokoll`, und der Architekturtest „DbContext berührt fremdes Schema“ schlägt fehl. Agent 4 legt in jedem Modul eine eigene Tabelle an, und eine Gesamtsicht gibt es nicht.

**Kollision:** Gültige Zugangslinks liegen beim Hoster in der Telemetrie, Mail-Adressen in Fehlerlogs. Das Sicherheitsprotokoll ist entweder regelwidrig oder zersplittert.

**Vorgeschlagene Regel (AD-22 ergänzen):**
> **Geheimnisse und Rohdaten:** Telemetrie zeichnet nur die Routen-Vorlage (`http.route`) auf, nie den Rohpfad oder Abfrageparameter. Token-Segmente werden vor dem Export durch `***` ersetzt; das gilt auch für die vom Browser gemeldete Seite. `Include Error Detail` und `EnableSensitiveDataLogging` sind außerhalb der Tests verboten (Prüfung beim Start). Unerwartete Ausnahmen gehen nur mit Typ und Trace-ID in Log und Antwort. **Sicherheitsprotokoll:** Es liegt im Schema `betrieb` und gehört dem API-Host, nicht einem Modul. Module schreiben nur über `ISicherheitsprotokoll` aus SharedKernel. Die Architekturregel zu fremden Schemata nimmt genau dieses Schema für diesen Dienst aus.

---

## R2-13 – Personen-Leseschnittstelle ohne lesenden Menschen (Hintergrund) und Einzelabrufe

**Schwere:** hoch

**Einheit A – Personen (Agent 1):** AD-8 und Gate-Fix: Die Leseschnittstelle prüft den **lesenden Menschen** aus dem Anfrage-Kontext gegen die eigene Rollenliste. Ohne lesende Person wirft sie eine Ausnahme.

**Einheit B – Küche (Agent 2):** AD-13: „Berechnungen wie Einkaufsliste laufen automatisch.“ Der Agent berechnet die Allergen-Mengen in einem Ereignis-Empfänger, etwa nach einer Änderung an einer Mahlzeit. Dort gibt es keinen lesenden Menschen. Die Berechnung scheitert, oder Agent 2 setzt eine Dienst-Person ein, die Personen nicht kennt.

**Einheit C – Freizeiten, Teilnehmerliste (Agent 3):** AD-27: „Personendaten immer über die Leseschnittstelle“. Der Agent ruft sie pro Zeile auf, mit 80 Abrufen und 80 Protokoll-Prüfungen pro Seitenaufruf.

**Kollision:** Automatische Berechnungen sind entweder unmöglich oder umgehen die Prüfung. Listen sind langsam, oder Agent 3 kopiert Namen in Freizeiten (gegen AD-8/AD-27).

**Vorgeschlagene Regel (AD-8 ergänzen):**
> **Zwecke ohne Menschen:** Leseschnittstellen von `Personen.Contracts` nehmen immer eine Menge von `PersonId` und eine `FreizeitId` entgegen. Zwecke ohne menschlichen Leser (z. B. „Allergen-Mengen“) sind eigene, benannte Methoden. Sie liefern nur abgeleitete Merkmale (Kennungen, Anzahlen), nie Antworttexte, und protokollieren mit Zweck und ohne lesende Person. Alle anderen Methoden verlangen die lesende Person aus dem Anfrage-Kontext und werfen ohne sie.

---

## R2-14 – Komponenten-Bibliothek in statisch gerenderten Seiten; „nur aus Bausteinen“ vs. Feature-Komponenten

**Schwere:** mittel

**Einheit A – Zeltlotse.Ui.Komponenten (Agent 1):** Baut die Inline-Bestätigung, die untere Navigation und die Feld-Komponente mit `@onclick` und eigenem Zustand. In der WebAssembly-App funktioniert das.

**Einheit B – Zeltlotse.Public (Agent 2):** AD-5: statisch gerendert, ohne Interaktivität. AD-29: Seiten setzen sich nur aus diesen Bausteinen zusammen. In der Gast-Anmeldung reagiert die Inline-Bestätigung nicht, weil `@onclick` bei statischem Rendern nichts tut.

**Einheit C – Zeltlotse.App, Feature Küche (Agent 3):** Konvention „Web-App-Aufbau“: modulspezifische Komponenten liegen im Feature-Ordner. AD-29 sagt: „Ein neues Oberflächenelement entsteht zuerst dort“ (in der Bibliothek). Agent 3 legt die Karte „Küchen-Tagesansicht“ im Feature an, Agent 4 eine ähnliche in der Bibliothek, mit einfachen Parametern statt Datenformen.

**Kollision:** Dieselben Bausteine verhalten sich in den beiden Clients verschieden, ohne dass es ein Test zeigt. Die bUnit-Tests laufen interaktiv. Wo „ein neues Element“ hingehört, entscheidet jeder Agent selbst. Auch „Seiten nur aus Bausteinen“ prüft niemand.

**Vorgeschlagene Regel (AD-29 präzisieren):**
> **Zwei Arten von Bausteinen:** Jeder Baustein der Bibliothek ist entweder *statisch tauglich* (ohne Ereignis-Handler, Interaktion nur über HTML-Formulare und Links) oder *interaktiv* und als solcher gekennzeichnet (`[NurInteraktiv]`). Zeltlotse.Public verwendet nur statisch taugliche Bausteine (Architekturtest). Die Bibliothek enthält nur datenfreie Darstellungsbausteine. Feature-Ordner dürfen sie zu fachlichen Komponenten zusammensetzen, aber keine eigenen ELKW-CSS-Klassen oder Stile verwenden. Ein Test sucht nach `class="elkw-` außerhalb von `Zeltlotse.Ui.Komponenten`.

---

## R2-15 – Freizeit-Kontext-Endpunkt: Besitzer und Rechte-Ableitung im Client

**Schwere:** mittel

**Einheit A – Freizeiten (Agent 1):** AD-29 verlangt „genau einen Kontext-Endpunkt je Freizeit“. Der Agent baut `GET /api/freizeiten/{id}/kontext` im Modul Freizeiten. Die Rollen liest er aus der eigenen Rollentabelle, die Org-Rollen nicht.

**Einheit B – API-Host (Agent 2):** Bauprinzip-Tabelle: Der API-Host enthält den „Mandanten- und Freizeit-Kontext“. Der Agent baut `GET /api/kontext/freizeit/{id}` im Host aus `IKontextRollen` (mit Org-Rollen). Eine Ausnahme vom Mandanten-Filter braucht er dafür nicht, aber er ist kein Modul und hat kein Testprojekt nach Konvention.

**Einheit C – Zeltlotse.Ui (Agent 3):** Leitet aus den Rollen ab, welche Knöpfe angezeigt werden (`Rolle == Leitung` → „Bestätigen“). Dabei gibt es eine zweite Rechtelogik neben den Richtlinien des Servers.

**Kollision:** Zwei Endpunkte mit verschiedenen Rollen (Org-Admin fehlt bei A). Die Oberfläche zeigt Knöpfe, die der Server mit `403` ablehnt, oder versteckt erlaubte Aktionen, sobald sich eine Richtlinie ändert.

**Vorgeschlagene Regel (AD-29 präzisieren):**
> **Freizeit-Kontext:** Den Endpunkt `GET /api/freizeiten/{freizeitId}/kontext` besitzt das Modul Freizeiten. Er nutzt dieselbe Auflösung wie der API-Host (`IKontextRollen`, Phase, Schalter) und liefert zusätzlich `erlaubt: [Aktionsschlüssel]`, berechnet aus denselben benannten Richtlinien wie die Endpunkte. Der Client leitet Rechte nie selbst aus Rollen ab. Die Aktionsschlüssel stehen in `Api.Contracts/Gemeinsam`.

---

## R2-16 – Ereignis-Inhalte referenzieren Typen fremder Contracts

**Schwere:** mittel

**Einheit A – Sicherheit (Agent 1):** Gibt `ErsteHilfeEntnommen` eine Eigenschaft vom Typ `Sicherheit.Contracts.MaterialEintragDto`, der in den lesenden Contracts schon existiert.

**Einheit B – Inventar (Agent 2):** Empfängt das Ereignis (AD-2: Ereignisse darf jedes Modul abonnieren). Pfeil INV → SIC gibt es nicht.

**Kollision:** Der Architekturtest „Contracts-Schnittstelle gegen die Pfeile“ meldet Inventar, weil es von einem Typ aus `Sicherheit.Contracts` außerhalb von `.Events` abhängt. Lockert Agent 3 die Prüfung auf Projekt-Ebene, fallen echte Verstöße durch.

**Vorgeschlagene Regel (AD-4 ergänzen):**
> **Ereignis-Typen:** Ereignisse im Namensraum `<Modul>.Contracts.Events` bestehen nur aus `Guid`, Zahlen, Datumswerten, Enums und Unter-Typen desselben Namensraums. Der Architekturtest prüft AD-2-Pfeile auf Typ-Ebene: Abhängigkeiten auf `*.Contracts.Events` sind immer erlaubt, alle anderen Typen aus `*.Contracts` nur entlang der Pfeile.

---

## Noch offen aus Runde 1

| Loch | Stand |
| --- | --- |
| L15 (Konflikt bei verschiedenen Feldern derselben Zeile) | offen; in R2-6 mit aufgenommen |
| L19 (Offline-Kopie überlebt Entzug der Freigabe) | offen: AD-18 löscht erst bei Freizeitende oder Abmelden, nicht bei `403`/`404` oder nach einer Höchstdauer ohne Abgleich |
| L20 (clientseitige Kennungen) | teilweise: AD-18 regelt nur das Vorfall-Protokoll; „auch vom Client erzeugbar“ in der Konvention ist weiter für alle Entitäten offen |
| L21 (Antworttyp der Katalogfragen, Allergen-Liste) | offen: AD-10 nennt keinen Antworttyp. Der Allergen-Abgleich der Küche kann Freitext nicht vergleichen |

## Zusammenfassung der vorgeschlagenen Ergänzungen

- **AD-5/AD-25:** Public im selben Prozess, Dienst-Kennung für Public → API, IP-Weitergabe für Rate-Limiting (R2-1).
- **AD-27:** Generator nur im Host, `InternalsVisibleTo`, `IModulCommand<TDbContext>`, Lese-DbContext, Validierung mit festen Fehlerschlüsseln (R2-2, R2-8, R2-9).
- **AD-28/AD-4:** `Aggregat`-Basisklasse mit `Folgenummer`, Einsammeln nur durch die Pipeline, Empfänger als Command je Empfänger mit Inbox (Ereignis, Empfänger), Ereignis-Typen nur aus einfachen Werten, vollständige Tabelle *Ereignis-Fluss* (R2-3, R2-4, R2-10, R2-16).
- **AD-17/AD-20:** gelten nur für `IBenutzerCommand` (R2-5).
- **AD-19/Autosave:** exakte Form des Körpers, ein Command pro Ressource mit Feld-Enum, Konflikt nur bei demselben Feld (R2-6).
- **Api.Contracts:** ohne Referenzen, Ordner-Besitz, `Gemeinsam/`, `/api/ich/<modul>`, nur additive Änderungen mit Versions-Header (R2-7).
- **AD-30:** Pflichtfälle je Endpunkt-Art, `403` vs. `404` (R2-11).
- **AD-22:** Routen-Vorlage statt Rohpfad, Verbot von Fehlerdetails, Sicherheitsprotokoll im Schema `betrieb` (R2-12).
- **AD-8:** Mengen-Abfragen, Zwecke ohne Menschen (R2-13).
- **AD-29:** statisch tauglich vs. interaktiv, Besitz des Kontext-Endpunkts, `erlaubt`-Liste (R2-14, R2-15).
