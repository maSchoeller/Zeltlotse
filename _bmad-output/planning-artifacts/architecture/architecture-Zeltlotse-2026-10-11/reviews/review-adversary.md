---
type: review
lens: adversary
target: ../ARCHITECTURE-SPINE.md
created: '2026-10-11'
---

# Gegner-Prüfung – Architektur-Gerüst Zeltlotse

## Urteil

Das Gerüst zieht die Modulgrenzen sauber, lässt aber offen, **wie Einheiten über diese Grenzen miteinander reden**: Es fehlen kanonische Referenzen (Person/Anmeldung/Konto), eine Regel, wer bei Folgeaktionen handelt (Aufruf oder Ereignis), die Quelle für Rollen im Freizeit-Kontext und die Herkunft des Mandanten-Kontexts außerhalb einer Benutzer-Anfrage. Außerdem fehlen zwei Pfeile, die fachlich zwingend sind (Küche → Freizeiten, Personen → Freizeiten bzw. Inventar → Freizeiten). Zwei Agenten, die jede AD wörtlich befolgen, bauen in mindestens 18 Fällen Teile, die nicht zusammenpassen.

**Methode:** Für jedes Loch werden zwei Einheiten (Module oder getrennt arbeitende Agenten) konstruiert, die jede AD buchstabengetreu einhalten. Gezeigt wird, wo ihre Ergebnisse kollidieren. Schwere: **kritisch** (Datenschutz-Verstoß, Datenverlust oder System läuft nicht zusammen), **hoch** (Fehlverhalten im Betrieb, teure Nacharbeit), **mittel** (Inkonsistenz, Reibung).

## Übersicht

| # | Loch | Einheiten | Schwere |
| --- | --- | --- | --- |
| L1 | Wie ein Mensch in einer Freizeit referenziert wird | Freizeiten · Finanzen · Sicherheit · Kanäle | kritisch |
| L2 | Personen kann die Freigabe nicht prüfen (fehlender Pfeil zu Freizeiten) | Personen · Freizeiten | kritisch |
| L3 | Wer kennt die Rollen einer Person im Freizeit-Kontext? | SharedKernel-Policy · Freizeiten · Organisationen · Inventar | kritisch |
| L4 | Mandanten-Kontext: Herkunft, Personen ohne `OrganisationId`, anonyme Seiten | Freizeiten · Personen · Programm | kritisch |
| L5 | Mandanten-Kontext in Ereignis-Empfängern und Hintergrunddiensten | Outbox-Empfänger aller Module | kritisch |
| L6 | Folgeaktionen: synchroner Aufruf oder Ereignis? (zwei Handelnde) | Freizeiten ↔ Personen / Organisationen | kritisch |
| L7 | Freigabe hat zwei Besitzer | Personen · Freizeiten | hoch |
| L8 | Ereignis-Reihenfolge (eingegangen → bestätigt → abgesagt → nachgerückt) | Freizeiten · Personen · Sicherheit · Küche | kritisch |
| L9 | Zeitzonen und Datumsgrenzen (Freigabefenster, „läuft“, QR-Ende) | Personen · Freizeiten · Kanäle · Web.Client | hoch |
| L10 | Löschung bei „Person gelöscht“ vs. gesetzliche Aufbewahrung | Finanzen · Sicherheit · Organisationen · Lösch-Dienst | kritisch |
| L11 | QR-Teilnehmerzugang: Identität ohne Person und ohne Konto | Freizeiten · Kanäle · Programm · Web | hoch |
| L12 | AD-20 und „Teilnehmer-Zugang“ (kein Modul, aber Schalter) | Kanäle · Freizeiten · Programm | hoch |
| L13 | Familie: zweiter Elternteil und Übergabe mit 18 | Personen · Freizeiten · Konten | hoch |
| L14 | Küche braucht Teilnehmerliste und Personenzahl (fehlender Pfeil) | Küche · Finanzen · Freizeiten | hoch |
| L15 | Autosave-PATCH: Form, Token, Granularität | alle Slices · Web.Client | hoch |
| L16 | Dateien: Besitzer bei Programmpunkt-Dokumenten und eingebundenen Org-Dokumenten | Freizeiten · Programm · Organisationen | hoch |
| L17 | Schreibschutz „gelaufen“ vs. Nachbereitung, Erstattung, Schäden | Finanzen · Programm · Inventar | hoch |
| L18 | Einsicht-Protokoll: Granularität bei Massen-Lesen | Küche · Sicherheit · Personen | mittel |
| L19 | Offline-Kopie überlebt den Entzug der Freigabe | Web.Client · Sicherheit · Küche | mittel |
| L20 | Clientseitig erzeugte Kennungen überall | alle Module | mittel |
| L21 | Fragenkatalog-Kennungen: Bedeutung und Versionierung | Personen · Küche · Freizeiten | mittel |

---

## L1 – Wie ein Mensch in einer Freizeit referenziert wird

**Schwere:** kritisch

**Einheit A – Freizeiten (Agent 1):** Legt `Anmeldung` mit `AnmeldungId` an. Leitung und Team speichert er als `KontoId`, weil die Rechteprüfung (AD-12) mit dem angemeldeten Konto arbeitet. Regelkonform: AD-3 verlangt nur „per Kennung“.

**Einheit B – Finanzen (Agent 2):** „Beitrag bezahlt“ (FR-60) speichert er pro `PersonId` (der Mensch zahlt), „wer abgehakt hat“ als `KontoId`. Belege „wer bezahlt hat“ als `PersonId`.

**Einheit C – Sicherheit (Agent 3):** Vorfall-Protokoll hängt an `AnmeldungId` (die Teilnahme ist der Kontext).

**Einheit D – Kanäle (Agent 4):** Push-Abo an `KontoId`; Autor einer Nachricht als `KontoId`.

**Kollision:**
- Gast-Anmeldung (FR-17) hat keine `KontoId` – Freizeiten kann Gast-Eltern nicht als Anmeldende führen; Kanäle kann einem Gast nie etwas zuordnen.
- Ein Kind, das zweimal mitfährt, hat zwei `AnmeldungId`s, aber eine `PersonId`. Sicherheit (AnmeldungId) und Finanzen (PersonId) lassen sich nur über Freizeiten zusammenführen; die Oberfläche muss pro Modul eine andere Kennung kennen.
- Person gelöscht (AD-16) kommt mit `PersonId`. Sicherheit und Kanäle finden ihre Zeilen damit nicht (AnmeldungId bzw. KontoId) – die Löschung läuft ins Leere.
- Eine Person mit zwei Konten-Zuständen (Gast → Konto, FR-17; Übergabe mit 18, FR-4) wechselt die `KontoId` bzw. erhält erstmals eine: alles, was mit `KontoId` geschlüsselt war, verwaist.

**Vorschlag – neue AD-25 „Kanonische Referenzen“**
- **Binds:** all
- **Prevents:** dass Module denselben Menschen über unterschiedliche Kennungen referenzieren und Löschung, Auskunft und Zusammenführung ins Leere laufen.
- **Rule:** Ein Mensch wird außerhalb von *Konten* **immer** über `PersonId` referenziert, auch als Akteur („wer hat abgehakt“, Autor, Leitung, Team). `KontoId` erscheint nur in *Konten* und wird an der Endpunkt-Grenze über `Personen.Contracts` zu `PersonId` aufgelöst (`AktuellePersonId` im Anfrage-Kontext des SharedKernel). Die Teilnahme an einer Freizeit (Teilnehmende **und** Mitarbeitende) ist genau eine `Anmeldung` im Modul Freizeiten mit eindeutigem Paar (`FreizeitId`, `PersonId`). Freizeitbezogene Fakten über einen Menschen tragen **beide** Kennungen `FreizeitId` und `PersonId`; `AnmeldungId` wird außerhalb von Freizeiten nicht gespeichert. Jedes Ereignis über einen Menschen trägt `PersonId`.

---

## L2 – Personen kann die Freigabe nicht prüfen

**Schwere:** kritisch

**Einheit A – Personen:** AD-8 verlangt, dass Personen bei jedem Lesen die Freigabe prüft. Die Freigabe lautet (Person, Freizeit, Stufe). Um zu entscheiden, ob *dieser Leser* Team oder Leitung *dieser Freizeit* ist, muss Personen die Rollen bei Freizeiten erfragen. Der Pfeil PER → FZ existiert nicht (nur FZ → PER). Regelkonforme Lösung des Agenten: Personen.Contracts nimmt Parameter `leserStufe` entgegen und vertraut dem Aufrufer.

**Einheit B – Freizeiten / Sicherheit / Küche:** rufen `Personen.Contracts.LeseFreizeitpass(personId, freizeitId, leserStufe: Team)` auf – Sicherheit und Küche setzen `Team` pauschal, da sie selbst die Rolle nicht kennen (SIC → FZ ist erlaubt, KUE → FZ nicht).

**Kollision:** Die Freigabeprüfung in Personen wird zur Formsache: Der Aufrufer behauptet die Stufe. Küche kann jeden Freizeitpass jeder Freizeit lesen, sobald irgendeine Freigabe Stufe *Team* existiert. Die Alternative (Personen baut aus Freizeiten-Ereignissen eine eigene Kopie der Team-Listen) ist ebenfalls regelkonform – zwei Agenten wählen je eine Variante, und die Protokolle (wer hat gelesen) zeigen im einen Fall „Küche“, im anderen die Person.

**Vorschlag – neue AD-26 „Freigabeprüfung mit Zugriffsnachweis“**
- **Binds:** Personen, Freizeiten, Sicherheit, Küche, SharedKernel, AD-8
- **Prevents:** dass Personen dem Aufrufer die Rolle des Lesers glaubt.
- **Rule:** Personen führt eine eigene, nur durch Ereignisse von Freizeiten gepflegte Projektion `FreizeitRolle(FreizeitId, PersonId, Rolle, gültig ab, gültig bis)` (Ausnahme zu „kein Pfeil“ über AD-2: nur Ereignisse). Jeder Lese-Aufruf auf Freizeitpass-Daten trägt die `PersonId` des **lesenden Menschen** aus dem Anfrage-Kontext (nicht vom aufrufenden Modul gesetzt) und die `FreizeitId`; Personen entscheidet allein anhand seiner Projektion und der Freigabe. Ein Parameter wie `leserStufe` ist verboten (Architekturtest).

---

## L3 – Wer kennt die Rollen einer Person im Freizeit-Kontext?

**Schwere:** kritisch

**Einheit A – SharedKernel-Policy (Agent 1):** AD-12 verlangt eine zentrale Autorisierungs-Richtlinie in SharedKernel. SharedKernel darf keine Module kennen. Der Agent liest Rollen daher aus **Claims im Cookie**, die bei der Anmeldung gesetzt werden (`org:{id}:OrgAdmin`, `fz:{id}:Leitung`).

**Einheit B – Freizeiten (Agent 2):** Ernennt Leitung (FR-6) und bestätigt Mitarbeit; prüft Rechte über eine eigene Tabelle zum Anfragezeitpunkt.

**Einheit C – Inventar (Agent 3):** FR-38 – Leitungen sehen Anschaffungswünsche. Inventar darf nur ORG aufrufen, nicht FZ. Der Agent fragt Organisationen „ist Mitglied“ und lässt jedes Org-Mitglied durch.

**Kollision:**
- Claims im Cookie veralten: Wer nach der Anmeldung als Team bestätigt wird, hat bis zum nächsten Login keine Rechte; wer entfernt wird, behält sie bis zum Ablauf der Sitzung.
- Freizeiten prüft live, Programm über die Policy (Cookie) – dieselbe Person sieht Programm nicht, aber die Freizeit-Startseite schon.
- Inventar lässt Mitarbeitende ohne Leitungsrolle Anschaffungswünsche sehen (Verstoß gegen FR-38), weil der Pfeil INV → FZ fehlt.
- Offen: Darf ein Org-Admin Freizeit-Inhalte sehen? Die Rollentabelle schweigt; Org-Überblick darf es ausdrücklich nicht. Agent 1 gibt Org-Admins alles, Agent 2 nichts.

**Vorschlag – AD-12 verschärfen**
- **Binds:** all, Rollentabelle im PRD
- **Prevents:** veraltete oder modulweise unterschiedlich ermittelte Rechte.
- **Rule:** Rollen werden **bei jeder Anfrage** aus dem besitzenden Modul ermittelt, nie aus Cookie-Claims (Cookie trägt nur `KontoId`). SharedKernel definiert die Schnittstelle `IKontextRollen` (`RollenInOrganisation(personId, orgId)`, `RollenInFreizeit(personId, freizeitId)`); implementiert wird sie im Host durch Aufruf von `Organisationen.Contracts` und `Freizeiten.Contracts` (pro Anfrage zwischengespeichert). Module prüfen ausschließlich über benannte Richtlinien (`FreizeitTeam`, `FreizeitLeitung`, `OrgAdmin`, `OrgUeberblick`, `LeitungIrgendeinerFreizeitDerOrg`). Org-Admin hat in einer Freizeit **keine** Inhaltsrechte, außer er ist dort Leitung oder Team (gleich wie Org-Überblick). Rollen-Ermittlung zählt nicht als Modul-Aufruf im Sinne von AD-2.

---

## L4 – Mandanten-Kontext: Herkunft, Personen ohne `OrganisationId`, anonyme Seiten

**Schwere:** kritisch

**Einheit A – Freizeiten:** Mandanten-Kontext = vom Benutzer gewählte „aktuelle Organisation“ (Header `X-Organisation` bzw. Auswahl in der Oberfläche). Globaler Filter `OrganisationId == ctx.OrganisationId`.

**Einheit B – Programm:** Mandanten-Kontext wird aus der Route abgeleitet (`/api/programm/freizeiten/{fzId}/...` → `OrganisationId` via `Freizeiten.Contracts`).

**Einheit C – Personen:** Keine `OrganisationId` (AD-7) → kein Filter (AD-6 bindet nur „organisationseigene Daten“). `Personen.Contracts.HoleProfil(personId)` liefert jedes Profil zur Kennung.

**Kollision:**
- Eine Person, die in Org A Team und in Org B Leitung ist, öffnet aus Org-A-Auswahl einen Link auf eine Freizeit von Org B: Freizeiten liefert `404`, Programm liefert Daten. Startseite der Freizeit (aus mehreren Endpunkten zusammengesetzt, AD-2) bricht halb.
- Anonyme Gast-Anmeldung (AD-5, öffentliche Seite) hat keinen Benutzer und keine Auswahl: Variante A setzt `ctx = null` → Filter `OrganisationId == null` liefert nichts, Insert scheitert oder speichert `null`. Ein Agent „löst“ das mit einem Sonderpfad ohne Filter.
- Personen ist ungefiltert und per Kennung adressierbar: Jedes Modul (und jeder Endpunkt, der `personId` durchreicht) kann Profile fremder Organisationen holen. NFR-2 ist für Profile nur durch Disziplin geschützt.

**Vorschlag – AD-6 verschärfen**
- **Binds:** alle Module, SharedKernel, NFR-2
- **Prevents:** unterschiedliche Quellen des Mandanten-Kontexts und ungefilterte Profile.
- **Rule:** Der Mandanten-Kontext wird **ausschließlich aus der adressierten Ressource** abgeleitet (Route enthält `orgId` oder `freizeitId`; `freizeitId` → `OrganisationId` über `Freizeiten.Contracts`), nie aus Benutzerauswahl oder Header. Öffentliche Endpunkte leiten ihn aus dem Zugangstoken (AD-11) ab. Fehlt der Kontext, gilt der Filter als „keine Zeilen“ und Schreiben wirft (kein `null`-Mandant). Für Personen gilt statt des Mandantenfilters ein **Zugriffsfilter**: Personen.Contracts gibt Profildaten nur heraus, wenn (a) der Leser die Person selbst ist, (b) sorgeberechtigt ist (Familie) oder (c) eine gültige Freigabe für die im Kontext stehende Freizeit besteht. Es gibt keinen Contracts-Aufruf „Profil per Kennung“ ohne Kontext.

---

## L5 – Mandanten-Kontext in Ereignis-Empfängern und Hintergrunddiensten

**Schwere:** kritisch

**Einheit A – Sicherheit (Empfänger von `FreizeitAbgeschlossen`):** Archiviert die Vorfall-Notiz. Der Outbox-Dienst läuft ohne HTTP-Anfrage → Mandanten-Kontext leer → globaler Filter liefert nichts. Der Agent darf `IgnoreQueryFilters` nicht nutzen (nur Lösch-Dienst, AD-6) und setzt deshalb den Kontext aus einer **Abfrage bei Freizeiten**.

**Einheit B – Organisationen (Empfänger von `MitarbeitBestaetigt`):** Ereignisse tragen nur Kennungen (AD-4). Der Agent interpretiert `OrganisationId` als zulässige Kennung im Ereignis und setzt den Kontext daraus.

**Kollision:** Zwei Verfahren, einer davon ein synchroner Rückruf entgegen der Pfeilrichtung aus einem Hintergrundkontext. Der Archivierungsjob von Sicherheit läuft still leer (nichts gefunden = Erfolg, Ereignis als verarbeitet markiert) – Vorfall-Notizen werden nie archiviert und mit dem Freigabefenster gelöscht.

**Vorschlag – neue AD-27 „Ereignis-Umschlag“**
- **Binds:** alle Module, Outbox, Lösch-Dienst
- **Prevents:** Empfänger ohne oder mit selbst ermitteltem Mandanten-Kontext.
- **Rule:** Jedes Ereignis wird in einem einheitlichen Umschlag aus SharedKernel gespeichert: `EreignisId`, `Typ`, `Version`, `OrganisationId` (falls organisationsbezogen), `AggregatId`, `AggregatVersion`, `ZeitpunktUtc`, `AusloesendePersonId`. Der Outbox-Dienst setzt den Mandanten-Kontext aus dem Umschlag, bevor er den Empfänger aufruft. Ein Empfänger, der „nichts gefunden“ hat, protokolliert das als Warnung (nur Kennungen, AD-22).

---

## L6 – Folgeaktionen: synchroner Aufruf oder Ereignis?

**Schwere:** kritisch

**Situation:** FR-7 (bestätigte Mitarbeit → Mitarbeitende der Organisation), FR-10 (Mitarbeit → Nachweis-Einsicht automatisch anfordern), AD-8 (Anmeldung eingegangen → Freigabe Stufe Leitung).

**Einheit A – Freizeiten:** FZ → ORG und FZ → PER sind erlaubte Pfeile. Der Agent ruft in `Anmeldungen/Bestaetigen/Handler` direkt `Organisationen.Contracts.FuegeMitarbeiterHinzu()` und `Personen.Contracts.ErteileFreigabe()` auf.

**Einheit B – Organisationen / Personen:** AD-4 sagt „Reaktionen über Modulgrenzen laufen über Ereignisse“. Die Agenten abonnieren `AnmeldungBestaetigt` und legen Mitgliedschaft bzw. Freigabe an.

**Kollision:**
- Doppelte Mitgliedschaften/Freigaben oder Eindeutigkeitsfehler, die den Ereignis-Empfänger dauerhaft scheitern lassen.
- Der synchrone Weg schreibt in zwei Schemata mit zwei `DbContext`s – zwei Transaktionen. Stürzt der Prozess dazwischen ab, ist die Anmeldung bestätigt, aber ohne Freigabe – genau der Fall, den AD-4 verhindern soll.

**Vorschlag – AD-4 verschärfen**
- **Binds:** all
- **Prevents:** zwei Handelnde für dieselbe Folgeaktion und schreibende Aufrufe über Schemagrenzen ohne gemeinsame Transaktion.
- **Rule:** Synchrone Contracts-Aufrufe sind **nur lesend** (Abfragen) oder stoßen eine eigenständige Benutzeraktion im Zielmodul an, deren Transaktion allein im Zielmodul liegt (kein Schreiben in zwei Modulen in einer Anfrage). Jede **Folgeaktion** eines Zustandswechsels ist ein Ereignis-Empfänger im Zielmodul. Für jede Folgeaktion legt die Tabelle *Ereignis-Fluss* im Gerüst genau einen Empfänger fest (z. B. `AnmeldungEingegangen` → Personen: Freigabe Leitung; `AnmeldungBestaetigt` → Personen: Freigabe Team, Organisationen: Mitgliedschaft Mitarbeitende, Personen: Nachweis-Einsicht anfordern; `AnmeldungAbgesagt` → Personen: Freigabe entziehen).

---

## L7 – Freigabe hat zwei Besitzer

**Schwere:** hoch

**Einheit A – Freizeiten:** AD-8: „Eine Freizeit hält nur eine *Freigabe* (Person, Freizeit, Stufe, gültig bis).“ Der Agent legt Tabelle `freizeiten.freigabe` an.

**Einheit B – Personen:** Datenbesitz-Tabelle: Personen besitzt Freigabe; ER-Diagramm: `PERSON ||--o{ FREIGABE : erteilt`. Der Agent legt `personen.freigabe` an.

**Kollision:** Zwei Tabellen, zwei Wahrheiten über „gültig bis“; FR-24e (Eltern sehen, bis wann die Freizeit Einsicht hat) zeigt die Personen-Version, die Prüfung beim Lesen nutzt evtl. die Freizeiten-Version. Gegen AD-3 („zwei Besitzer“) – aber beide Agenten können eine AD zitieren.

**Vorschlag – AD-8 präzisieren**
- **Rule (Ergänzung):** Die Freigabe besitzt **ausschließlich** Personen. „Hält“ in AD-8 heißt: Freizeiten kennt keine Freigabe-Zeilen, sondern löst sie über Ereignisse aus (L6) und fragt den Stand bei Bedarf über `Personen.Contracts` ab. „Gültig bis“ wird nicht gespeichert, sondern beim Prüfen aus `Freizeit-Ende` (Projektion aus Freizeiten-Ereignissen) + fixiertem Intervall berechnet – damit verschiebt eine Datumsänderung der Freizeit das Fenster ohne Nachpflege.

---

## L8 – Ereignis-Reihenfolge

**Schwere:** kritisch

**Situation:** Leitung bestätigt eine Anmeldung, sagt kurz darauf ab (Fehlklick) und bestätigt erneut; oder: Absage und Nachrücken (FR-21) innerhalb einer Minute.

**Einheit A – Outbox-Dienst:** Stellt parallel zu und wiederholt bei Fehlern mit Backoff (regelkonform: AD-4 fordert nur Zustellung).

**Einheit B – Personen (Empfänger):** Idempotent über `EreignisId` (AD-4). Verarbeitet `AnmeldungBestaetigt#2` vor `AnmeldungAbgesagt#1`, weil dieses einen Wiederholversuch brauchte.

**Kollision:** Endzustand in Personen: Freigabe entzogen, obwohl die Anmeldung bestätigt ist – oder umgekehrt: Team sieht weiter den Freizeitpass einer abgesagten Person (Datenschutz-Verstoß). Idempotenz schützt nur vor Doppelungen, nicht vor Vertauschung. Gleiches für Küche (Allergenliste) und Sicherheit (Notfallordner).

**Vorschlag – AD-4 ergänzen (Reihenfolge)**
- **Prevents:** veraltete Zustände durch vertauschte Ereignisse.
- **Rule:** Ereignisse tragen `AggregatId` und streng steigende `AggregatVersion` (L5). Der Outbox-Dienst stellt pro Aggregat **in Reihenfolge** zu (ein Ereignis eines Aggregats erst, wenn das vorige verarbeitet ist). Empfänger speichern die zuletzt verarbeitete Version pro Aggregat und verwerfen ältere. Zustandsereignisse über Anmeldungen tragen den **vollständigen neuen Zustand** (`Status`, `Rolle`) als Kennungs-/Enum-Werte, nicht nur „etwas hat sich geändert“.

---

## L9 – Zeitzonen und Datumsgrenzen

**Schwere:** hoch

**Einheit A – Personen (Freigabefenster):** FR-24c: „ab Ende des letzten Freizeittages“. Der Agent rechnet `EndDatum (date) + Intervall` → `date`, vergleicht mit `DateOnly.FromDateTime(timeProvider.GetUtcNow())`.

**Einheit B – Freizeiten (Phase):** „läuft“ ab Startdatum: `StartDatum <= heute` mit heute in Europe/Berlin. QR-Zugang endet „mit der Freizeit“ = Phase „gelaufen“ (Leitung schließt ab).

**Einheit C – Kanäle / Web.Client (QR, Offline):** „Endet mit der Freizeit“ = `EndDatum 23:59:59 UTC`. Offline-Daten (AD-18) werden mit „Ende der Freizeit“ gelöscht = beim Wechsel auf „gelaufen“.

**Kollision:**
- Zwischen 00:00 und 02:00 Berliner Zeit (Sommer) weichen „heute“ in UTC und Berlin ab: Freigabe endet zwei Stunden zu früh oder zu spät; Phase „läuft“ beginnt in Freizeiten und Programm zu unterschiedlichen Zeiten.
- „Ende der Freizeit“ hat drei Bedeutungen (letzter Tag Berliner Mitternacht, 23:59 UTC, Abschluss durch Leitung). Schließt die Leitung nie ab, bleibt der QR-Zugang nach Variante B unbegrenzt gültig; nach Variante C endet er am letzten Abend um 01:59 Berliner Zeit – mitten in der Abschlussnacht.
- Intervall-Einheit offen (Tage? Monate? `interval`?): „+ 1 Monat“ ab 31. Juli.

**Vorschlag – neue AD-28 „Zeitpunkte der Freizeit“**
- **Binds:** Freizeiten, Personen, Kanäle, Sicherheit, Küche, Web.Client, FR-24, FR-45, FR-64, FR-65
- **Prevents:** abweichende Grenzen für Freigabe, Phase, QR-Zugang und Offline-Löschung.
- **Rule:** `Freizeiten.Contracts` liefert als einzige Quelle die Zeitpunkte einer Freizeit als `DateTimeOffset` (UTC), berechnet in Europe/Berlin: `Beginn` = Startdatum 00:00, `Ende` = Tag **nach** dem letzten Freizeittag 00:00, `FreigabeEnde` = `Ende` + Intervall (Intervall in ganzen Tagen gespeichert). „Läuft“ = `Beginn ≤ jetzt < Abschluss` bzw. bis zum Abschluss. QR-Zugang und Offline-Daten enden bei `Ende` **oder** Abschluss, je nachdem was früher kommt; spätestens `Ende` + 1 Tag, auch ohne Abschluss. Kein Modul vergleicht `date`-Werte mit „heute“ selbst.

---

## L10 – Löschung bei „Person gelöscht“ vs. gesetzliche Aufbewahrung

**Schwere:** kritisch

**Einheit A – Finanzen:** AD-16: auf „Person gelöscht“ reagieren, endgültig löschen, „nie nur als gelöscht markiert“. Der Agent löscht alle Belege, bei denen die Person „bezahlt hat“, samt Foto.

**Einheit B – Finanzen (anderer Agent):** Löscht nichts, weil Belege „organisationseigene Daten“ mit eigener Org-Frist sind (NFR-5 nennt Belege ausdrücklich); er ersetzt den Namen durch „gelöschte Person“ – was AD-16 („nie nur markiert“) zu verbieten scheint.

**Einheit C – Sicherheit:** Löscht bei „Person gelöscht“ die archivierte Vorfall-Notiz, die nach FR-31 gerade für Nachfragen von Eltern/Versicherung bleiben soll.

**Kollision:** Belege sind für gemeinnützige Träger steuerlich aufbewahrungspflichtig (Abgabenordnung, i. d. R. 10 Jahre); Soll-Ist-Vergleich (FR-59) kippt nachträglich, wenn ein Beleg verschwindet. Vorfall-Notizen sind Haftungsdokumente. Ein Konto-Löschwunsch eines Teammitglieds vernichtet beides – oder beide Agenten bauen gegensätzlich, und die Datenauskunft (FR-14) meldet bei einem Modul Daten, die beim anderen schon weg sind. Ebenso offen: Einsicht-Protokoll, Nachweis-Bestätigung bei der Organisation, Mitgliedschaft.

**Vorschlag – AD-16 verschärfen**
- **Binds:** all, FR-14, FR-31, FR-58, NFR-5
- **Prevents:** Vernichtung aufbewahrungspflichtiger Daten durch einen Löschwunsch und gegensätzliche Auslegung von „endgültig löschen“.
- **Rule:** Jedes Modul ordnet jede personenbezogene Entität genau einer Klasse zu (Tabelle im Gerüst): **(1) Personeneigen** – wird bei „Person gelöscht“ sofort gelöscht (Profil, Freizeitpass, Nachweise, Push-Abos, Nachrichten-Autor wird `PersonId`-los). **(2) Aufbewahrungspflichtig der Organisation** – bleibt bis zur Org-Frist; bei „Person gelöscht“ wird der Personenbezug durch einen festen Platzhalter ersetzt (Anonymisieren = endgültiges Entfernen des Bezugs, kein Lösch-Kennzeichen); Freitext-Felder werden geleert, sofern sie nicht selbst Inhalt der Pflicht sind (Belege, Vorfall-Notiz, Nachweis-Bestätigung, Einsicht-Protokoll). **(3) Freizeit-Arbeitsdaten** – folgen der Org-Frist ab Freizeit-Ende. Die Datenauskunft nennt Klasse (2) mit Löschdatum. Gesetzliche Mindestfristen gibt der Betreiber als Untergrenze der Org-Fristen vor.

---

## L11 – QR-Teilnehmerzugang: Identität ohne Person und ohne Konto

**Schwere:** hoch

**Einheit A – Freizeiten:** besitzt QR-Zugang (Datenbesitz). Token nach AD-11, Zweck „QR“, Ablauf „mit Freizeit“. Ein Token pro Freizeit (am Infobrett).

**Einheit B – Kanäle:** liest Kanal „Alle“ für QR-Nutzer, verwaltet Push-Abos (AD-21). AD-12: Sitzung über Identity-Cookie. Der Agent legt für QR-Nutzer einen anonymen Identity-Benutzer an (Konto ohne Person) – verletzt AD-7 nicht ausdrücklich.

**Einheit C – Programm:** Wochenübersicht. AD-5: QR-Teilnehmerzugang ist eine öffentliche, statische Server-Seite **ohne** Interaktivitäts-Laufzeit. Der Agent rendert die Wochenübersicht serverseitig mit dem Token in der URL bei jedem Aufruf.

**Kollision:**
- Push (FR-61) braucht Service Worker + Abo – mit einer rein statischen Seite (AD-5) und einem Abo, das an ein Konto (Kanäle) gebunden ist, geht das nicht ohne Sonderweg.
- Zwei Identitätsmodelle für dieselben Nutzer: Kanäle mit Wegwerf-Konten (füllen Konten mit Hunderten anonymer Konten pro Freizeit), Programm mit Token in jeder URL (landet in Logs/Verlauf – AD-22 betrifft nur Personendaten, nicht Geheimnisse).
- Ein Token pro Freizeit ist ein Gruppengeheimnis: ein abfotografierter QR-Code reicht für Außenstehende; Widerruf trifft alle.

**Vorschlag – neue AD-29 „Gast-Sitzung für den Teilnehmer-Zugang“**
- **Binds:** Freizeiten, Kanäle, Programm, Web, Web.Client, FR-61, FR-64, AD-5, AD-11, AD-12
- **Prevents:** Wegwerf-Konten, Tokens in URLs und unterschiedliche Prüfwege.
- **Rule:** Der QR-Code öffnet eine SSR-Seite, die das Token **einmal** gegen ein eigenes Cookie-Schema `Teilnehmerzugang` tauscht (Claim nur `FreizeitId` + zufällige `GeraeteId`, kein Konto, keine Person). Ablauf des Cookies = QR-Ende nach AD-28. Die Leitung kann das QR-Token erneuern, was alle Gast-Sitzungen ungültig macht. Die Teilnehmer-Ansicht ist ein eigener, schmaler Bereich der WebAssembly-App mit Service Worker (Ausnahme zu AD-5 für Push). Push-Abos von Gästen hängen an `GeraeteId` + `FreizeitId` und werden bei QR-Ende gelöscht. Endpunkte, die Gäste bedienen, verlangen die Richtlinie `TeilnehmerzugangLesen(freizeitId)` und sind ausschließlich lesend.

---

## L12 – AD-20 und „Teilnehmer-Zugang“

**Schwere:** hoch

**Einheit A – Kanäle:** AD-20 bindet „Kanäle (Teilnehmer-Zugang)“: Ist das zuschaltbare Modul abgeschaltet, antworten die Endpunkte mit `404`. Der Agent liest den Schalter „Teilnehmer-Zugang“ und schaltet **alle** Kanäle-Endpunkte ab – auch den Team-Kanal.

**Einheit B – Programm:** Teilnehmer-Zugang betrifft die Wochenübersicht, Programm steht aber nicht in AD-20. Der Agent liefert die Wochenübersicht für QR-Gäste unabhängig vom Schalter.

**Einheit C – Küche:** Abgeschaltet → Endpunkte `404`. Ereignis-Empfänger (z. B. `ProgrammpunktVerschoben`) laufen weiter oder nicht? Der eine Agent verwirft Ereignisse, solange abgeschaltet; nach Einschalten fehlen Vormerkungen. Ein anderer löscht beim Abschalten die Daten.

**Kollision:** AD-1 kennt zehn Module, „Teilnehmer-Zugang“ ist keins – trotzdem ist es ein Schalter. Team-Kanal fällt aus, QR-Gäste sehen Programm trotz Abschaltung, Küche verliert Daten.

**Vorschlag – AD-20 präzisieren**
- **Rule:** Freizeiten besitzt **Funktions-Schalter** (nicht Module): `Verpflegung` → alle Endpunkte von Küche; `Finanzen` → alle Endpunkte von Finanzen; `Teilnehmerzugang` → ausschließlich die Gast-Sitzung (AD-29) und die Gast-Richtlinie; Kanäle, Programm, Freizeiten bleiben für angemeldete Rollen unberührt. Abgeschaltet heißt: Endpunkte `404`, Daten bleiben erhalten, Ereignis-Empfänger verarbeiten weiter. Ein Schalter-Wechsel ist ein Ereignis (`FunktionGeschaltet`).

---

## L13 – Familie: zweiter Elternteil und Übergabe mit 18

**Schwere:** hoch

**Einheit A – Freizeiten:** Die Anmeldung speichert „angemeldet von“ (`KontoId` des Elternteils, oder Gast-Mail). Status, Elterninfo (FR-68) und Bestätigungs-Mail gehen an diese Kennung.

**Einheit B – Personen:** Familie mit zwei Elternteilen (FR-3). Einladung des zweiten Elternteils: AD-11 nennt diesen Link nicht. Der Agent baut eine eigene Einladung (kurzer Code, 6 Ziffern, per Mail), regelkonform, weil AD-11 abschließend aufzählt.

**Kollision:**
- Der zweite Elternteil sieht Freizeitpass und Profil des Kindes (Personen), aber nicht den Anmeldestatus und nicht die Elterninfo (Freizeiten kennt nur den ersten).
- Nach der Übergabe mit 18 (FR-4) sehen die Eltern laut Personen nichts mehr; Freizeiten schickt weiter Mails an „angemeldet von“ und zeigt den Eltern die Anmeldung.
- Ein sechsstelliger Code unterläuft die 128-Bit-Regel von AD-11.

**Vorschlag – AD-7 und AD-11 ergänzen**
- **Rule (AD-7):** Wer für eine Person handeln darf (sie selbst, Sorgeberechtigte), entscheidet ausschließlich `Personen.Contracts.DarfHandelnFuer(handelndePersonId, personId)`. Freizeiten speichert keine „angemeldet von“-Beziehung als Recht, nur als Protokoll; Sichtbarkeit von Anmeldestatus, Elterninfo und Empfänger von Mails werden zum Zeitpunkt der Nutzung über diese Abfrage bestimmt. Gast-Anmeldungen erhalten eine Zugangs-Mail mit Token (AD-11) statt Konto.
- **Rule (AD-11):** Die Liste ist nicht abschließend; **jeder** Link oder Code, der Zugang gewährt (auch Familien-Einladung, Gast-Statuslink, Nachweis-Einsicht), folgt dem Muster. Kurze Codes sind verboten.

---

## L14 – Küche braucht Teilnehmerliste und Personenzahl

**Schwere:** hoch

**Einheit A – Küche:** Darf nur PRG und PER aufrufen (Diagramm). Für die Skalierung (FR-46) und den Allergen-Abgleich (FR-48) braucht sie die Personen der Freizeit. Der Agent abonniert Anmelde-Ereignisse und führt eine eigene Liste (regelkonform: Ereignisse darf jedes Modul abonnieren).

**Einheit B – Finanzen:** FIN → FZ ist erlaubt. Der Agent fragt `Freizeiten.Contracts.AnzahlBestaetigt(freizeitId)` – zählt nur bestätigte Teilnehmende.

**Einheit C – Personen:** bietet `Freigaben(freizeitId)` an; ein dritter Küchen-Agent nutzt das als Teilnehmerliste.

**Kollision:** Drei verschiedene „Personenzahlen“ (mit/ohne Team, mit/ohne eingegangene Anmeldungen); Einkaufsliste und Verpflegungs-Kalkulation (FR-55, „alle Personen“) passen nicht zusammen. Küchen-Kopie der Liste ist anfällig für L8.

**Vorschlag – AD-2 ergänzen**
- **Rule:** Pfeil **KUE → FZ** ergänzen. `Freizeiten.Contracts` ist die einzige Quelle für Teilnehmerliste und `Personenzahl(freizeitId, Stichtag)` = bestätigte Teilnehmende + bestätigte Mitarbeitende (FR-46 „jede Person zählt gleich“, FR-55 „alle Personen“). Kein Modul führt eine eigene Teilnehmerliste.

---

## L15 – Autosave-PATCH: Form, Token, Granularität

**Schwere:** hoch

**Einheit A – Programm (Agent 1):** PATCH mit JSON Merge Patch (`{ "titel": "…" }`), Concurrency-Token im Header `If-Match: "<xmin>"`, Antwort `200` mit ganzer Entität.

**Einheit B – Küche (Agent 2):** PATCH mit JSON Patch (RFC 6902, `[{op, path, value}]`), Token im Body (`version`), Antwort `204` + neuer Token im `ETag`.

**Einheit C – Personen (Freizeitpass):** Antworten verschlüsselt (AD-14) als ein Blob pro Person → jede Feldänderung schreibt den Blob neu.

**Kollision:**
- Web.Client braucht pro Modul eigenen Autosave-Code; ein gemeinsamer Baustein ist unmöglich.
- `xmin` gilt pro Zeile: Zwei Teammitglieder ändern **verschiedene** Felder desselben Programmpunkts → 409-Hinweis ständig, obwohl nichts überschrieben würde. Bei einem Freizeitpass-Blob kollidieren zwei Elternteile bei jedem Feld.
- „Rückgängig = PATCH mit altem Wert“: Mit welchem Token? Nach einem fremden Zwischenstand überschreibt Rückgängig dessen Änderung oder scheitert.
- `null` in Merge Patch heißt „löschen“ – Agent 2 kennt dafür `remove`.

**Vorschlag – Konvention „Schreiben“ zur AD-30 erheben**
- **Binds:** alle Slices, Web.Client, AD-19
- **Rule:** Autosave = `PATCH /api/<modul>/<ressource>/{id}` mit Content-Type `application/merge-patch+json` (RFC 7396), **genau ein** Feld pro Aufruf, Token im Header `If-Match`, Antwort `200` mit `ETag` (neuer Token) und dem gespeicherten Feldwert. `null` heißt Feld leeren. Konflikt `409` nur, wenn das **gleiche Feld** seit dem Token geändert wurde (Feld-Versionen bzw. Vergleich des alten Werts: Client schickt `If-Match` + bisherigen Wert); sonst wird angewendet. Rückgängig nutzt den Token der eigenen letzten Antwort. Verschlüsselte Freizeitpass-Antworten werden **pro Frage** einzeln gespeichert, nicht als Blob.

---

## L16 – Dateien: Besitzer bei Programmpunkt-Dokumenten und eingebundenen Org-Dokumenten

**Schwere:** hoch

**Einheit A – Freizeiten:** besitzt „Dokumente“ (Datenbesitz). FR-43a: Dokumente am Kontext Freizeit **oder Programmpunkt**. Der Agent speichert `ProgrammpunktId` als Kennung und liefert aus.

**Einheit B – Programm:** besitzt Programmpunkte; FR-41 kopiert Programmpunkte samt Material und Aufgaben aus der Bibliothek. Der Agent legt Anhänge im eigenen Präfix ab, weil die Bibliothek (Programm) Anhänge mitnehmen muss.

**Einheit C – Organisationen:** Org-Dokumente werden „eingebunden statt kopiert“ (FR-43b). AD-15: Ausgeliefert nur über den Endpunkt des **besitzenden** Moduls. QR-Gäste (Sichtbarkeit „Eltern und Teilnehmende“) sollen eingebundene Org-Dokumente sehen – Organisationen kennt aber keine Freizeit-Rollen und keine Gast-Sitzung.

**Kollision:** Zwei Ablageorte für Programmpunkt-Dokumente; die Dokument-Suche (FR-43c) findet jeweils nur die Hälfte. Org-Dokumente sind für Eltern/QR-Gäste entweder nicht erreichbar oder Organisationen baut eine eigene Prüfung von Freizeit-Rechten (Pfeil ORG → FZ fehlt).

**Vorschlag – AD-15 präzisieren**
- **Rule:** Alle Dokumente einer Freizeit (auch am Programmpunkt) besitzt Freizeiten; Programm hält keine Dateien. Die Programmbibliothek nimmt Dokumente nur als Verweis mit. Eine Einbindung eines Org-Dokuments ist eine Zeile in Freizeiten (`OrgDokumentId`, Sichtbarkeit); ausgeliefert wird über den Endpunkt von Freizeiten, der die Rechte prüft und die Datei über `Organisationen.Contracts.OeffneDokument(id)` als Strom holt (FZ → ORG ist erlaubt). Datei-Schlüssel: `<modul>/<OrganisationId|PersonId>/<UUIDv7>`, nie Dateinamen. Nachweise und Belege liefern `Content-Disposition: attachment`, Prüfung der Dateiart serverseitig.

---

## L17 – Schreibschutz „gelaufen“ vs. Nachbereitung

**Schwere:** hoch

**Einheit A – Freizeiten:** Leitung schließt am Abreisetag ab → „gelaufen“ (FR-45).

**Einheit B – Finanzen:** AD-17: bei „gelaufen“ jeden Schreibzugriff ablehnen. Erstattungen werden aber erst Wochen später überwiesen und „erstattet“ gesetzt (FR-58, FR-59 „kein Abrechnungsabschluss“).

**Einheit C – Programm / Inventar:** Aufgaben der Phase **Nachbereitung** (FR-39) können nicht mehr abgehakt werden; Schäden aus dem Ausräumen (FR-38) nicht erfasst. Ein anderer Inventar-Agent sieht Inventar als organisationsbezogen und lässt Schreiben zu.

**Kollision:** Entweder schließt die Leitung nie ab (dann greifen QR-Ende/Offline-Löschung nach L9 nicht), oder Nachbereitung und Abrechnung sind unmöglich. Module legen die Ausnahme unterschiedlich aus.

**Vorschlag – AD-17 ergänzen**
- **Rule:** Bei „gelaufen“ bleiben schreibbar: Finanzen (Feld „erstattet“, Belege nachtragen), Aufgaben der Phase Nachbereitung, Schadens-/Verlusterfassung in Inventar mit Bezug auf die Freizeit, Lösch-Dienst, Archivierung. Alles andere lehnt mit `409` und Problemtyp `freizeit-gelaufen` ab. Die Prüfung erfolgt über eine gemeinsame Richtlinie aus SharedKernel (`SchreibenErlaubt(freizeitId, Bereich)`), nicht pro Modul.

---

## L18 – Einsicht-Protokoll: Granularität bei Massen-Lesen

**Schwere:** mittel

**Einheit A – Küche:** Einkaufsliste wird bei jeder Änderung neu berechnet (FR-48) und liest dabei alle Freizeitpässe – Personen protokolliert jedes Mal pro Person („Küche hat gelesen“).

**Einheit B – Sicherheit:** Notfallordner-Druck liest gebündelt; protokolliert einmal pro Druck für die Freizeit, nicht pro Person.

**Kollision:** Eltern sehen im Protokoll (FR-13) Hunderte Einträge „Küche“ ohne Menschen dahinter, aber den Ausdruck nicht. Das Protokoll ist für den Zweck (wer hatte Einsicht) wertlos.

**Vorschlag – AD-8 ergänzen**
- **Rule:** Protokolliert wird pro (lesende Person, betroffene Person, Freizeit, Zweck, Tag) **einmal**; Zwecke: `Ansicht`, `Druck`, `Offline-Kopie`, `Allergen-Abgleich`. Automatische Berechnungen ohne menschlichen Leser protokollieren mit Zweck und ohne lesende Person; sie dürfen nur abgeleitete Merkmale (z. B. Allergen-Kennungen) erhalten, nie die Antworttexte.

---

## L19 – Offline-Kopie überlebt den Entzug der Freigabe

**Schwere:** mittel

**Einheit A – Web.Client:** AD-18: Offline-Daten werden mit Ende des Freigabefensters bzw. der Freizeit gelöscht.

**Einheit B – Freizeiten:** Ein Teammitglied wird während der Freizeit entfernt, oder eine Anmeldung wird abgesagt.

**Kollision:** Das Gerät behält Notfallkarte und Allergien bis zum Freizeit-Ende, ohne je zu erfahren, dass die Berechtigung weg ist (keine Live-Aktualisierung, AD-19). Ein entferntes Teammitglied hat Gesundheitsdaten bis zu zwei Wochen offline.

**Vorschlag – AD-18 ergänzen**
- **Rule:** Offline-Daten tragen einen Ablauf von höchstens 24 Stunden ab letztem erfolgreichem Abgleich; die App erneuert sie bei jeder Online-Phase und löscht sie, wenn der Server `403`/`404` liefert. Der Server liefert Offline-Pakete nur mit den Mindestangaben (Name, Notfallkontakt, Allergie-/Medikamenten-Kennungen).

---

## L20 – Clientseitig erzeugte Kennungen überall

**Schwere:** mittel

**Einheit A – Programm-Agent:** Konvention „auch clientseitig erzeugbar“ → Client schickt `id` beim Anlegen jeder Entität.

**Einheit B – Freizeiten-Agent:** erzeugt Kennungen nur serverseitig und ignoriert Client-IDs.

**Kollision:** Ein Client kann eine bekannte Kennung eines fremden Mandanten senden; der Eindeutigkeitsfehler verrät deren Existenz. Wiederholte Anfragen (Netz weg) erzeugen bei B Dubletten, bei A nicht. Die Oberfläche braucht pro Modul unterschiedliches Verhalten.

**Vorschlag – Konvention schärfen**
- **Rule:** Clientseitige Kennungen sind nur für Anlege-Aufrufe erlaubt, die idempotent sein müssen (Vorfall-Einträge offline, Autosave neu angelegter Zeilen). Der Server prüft, dass die Kennung UUIDv7 mit Zeitstempel ±24 h ist, und behandelt „existiert bereits im eigenen Mandanten mit gleichem Inhalt“ als Erfolg, „existiert anderswo“ als `409` ohne Detail.

---

## L21 – Fragenkatalog-Kennungen: Bedeutung und Versionierung

**Schwere:** mittel

**Einheit A – Personen:** pflegt den Katalog, korrigiert den Text der Frage `allergien` zu „Allergien und Unverträglichkeiten“ unter gleicher Kennung.

**Einheit B – Küche:** wertet für den Allergen-Abgleich die Antworten aus – erwartet eine strukturierte Liste von Allergen-Kennungen (14 EU-Allergene), Personen speichert Freitext.

**Kollision:** Allergen-Abgleich (FR-48) kann Freitext nicht mit den Rezept-Allergenen (FR-46) vergleichen; Textänderungen ändern rückwirkend die Bedeutung alter Antworten (FR-23 „Stimmt das noch?“ greift nicht).

**Vorschlag – AD-10 ergänzen**
- **Rule:** Jede Katalogfrage hat Kennung, Version und **Antworttyp** (`ja/nein`, `Text`, `Auswahl aus fester Liste`). Allergien und Sonderkost sind `Auswahl` aus der in `Personen.Contracts` veröffentlichten Allergen-Liste, die auch Küche für Rezepte verwendet. Eine inhaltliche Änderung erzeugt eine neue Version; Antworten auf eine ältere Version gelten als „Stimmt das noch?“.

---

## Fehlende Pfeile und Tabellen (Zusammenfassung)

- Pfeile ergänzen: **KUE → FZ** (L14); Rollenermittlung über den Host statt Pfeil für **INV/PER → FZ** (L2, L3).
- Neue Tabelle im Gerüst: **Ereignis-Fluss** (Ereignis → genau ein Empfänger je Folgeaktion, L6).
- Neue Tabelle im Gerüst: **Lösch-Klassen** pro Entität (L10).
- Präzisierungen: Freigabe nur bei Personen (L7); „Teilnehmer-Zugang“ als Funktions-Schalter (L12).
