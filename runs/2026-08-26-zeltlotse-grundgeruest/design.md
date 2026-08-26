# Design — Zeltlotse: Grundgerüst

Grundlage: `requirements.md` dieses Laufs, `design-system.md`, `foundation.md`.
Alle Farb-, Abstands- und Zustandsregeln stammen aus `design-system.md` und
werden hier nicht wiederholt.

---

# Teil 1 — Erlebnis

## Haltung

Die Anwendung duzt. Sie ist ein ruhiges Arbeitswerkzeug, kein Portal: Kopfzeile
schmal, Arbeitsfläche breit, keine Einleitung über der Liste. Wer sich anmeldet,
sieht in der ersten Bildschirmhöhe das, wofür er gekommen ist.

## Rahmen

```
┌──────────────────────────────────────────────────────────────┐
│ [Logo] Zeltlotse    Ev. Kirchengemeinde Musterstadt ▾    [M] │  56px
├──────────────────────────────────────────────────────────────┤
│                                                              │
│   Arbeitsfläche, max. 1200px, zentriert                      │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

- **Links** Logo mit Wortmarke, führt immer zur Startseite.
- **Mitte** Name der aktuellen Organisation. Gehört man nur einer an, ist das
  reine Beschriftung ohne Aufklappen. Bei mehreren wird daraus ein Wechsler; die
  Liste zeigt die Organisationen alphabetisch, die aktuelle mit Auswahlbalken.
- **Rechts** Initialen des Kontos. Menü: Konto, Kennwort ändern, Abmelden. Für
  den Betreiber zusätzlich „Verwaltung".
- Unter 768px rückt der Organisationsname unter das Logo, die Kopfzeile wird
  zweizeilig. Keine eingeklappte Navigation — es gibt zu wenig zu verstecken.

## Bildschirme

| Adresse | Bildschirm | Wer |
|---|---|---|
| `/einrichtung` | Ersteinrichtung | jeder, solange kein GlobalAdmin existiert |
| `/anmelden` | Anmeldung | alle |
| `/einladung/{token}` | Einladung einlösen | eingeladene Person |
| `/` | Meine Freizeiten | jeder Angemeldete |
| `/o/{slug}` | Freizeiten der Organisation | Mitglieder der Organisation |
| `/o/{slug}/f/{id}` | Freizeit | wer der Freizeit zugeordnet ist, plus OrgAdmin |
| `/o/{slug}/team` | Mitglieder | OrgAdmin, Freizeitleitung |
| `/o/{slug}/papierkorb` | Papierkorb | OrgAdmin |
| `/verwaltung/organisationen` | Organisationen | GlobalAdmin |
| `/verwaltung/konten` | Konten | GlobalAdmin |
| `/konto` | eigenes Konto | jeder Angemeldete |

### Ersteinrichtung `/einrichtung`

Eine schmale Karte auf leerem Grund, keine Kopfzeile. Überschrift „Zeltlotse
einrichten", ein Satz zur Einordnung, drei Felder (E-Mail, Kennwort, Kennwort
wiederholen), eine primäre Schaltfläche. Nach dem Absenden ist man angemeldet
und landet in der Verwaltung. Existiert bereits ein GlobalAdmin, antwortet die
Adresse mit einer schlichten Seite „Zeltlotse ist bereits eingerichtet" und
einem Link zur Anmeldung — kein Formular, kein Hinweis auf den Betreiber.

### Meine Freizeiten `/`

Der häufigste Bildschirm. Liste aller Freizeiten, denen man zugeordnet ist,
organisationsübergreifend, absteigend nach Beginn (Freizeiten ohne Zeitraum
zuerst, weil sie in Vorbereitung sind).

```
Meine Freizeiten

┌────────────────────────────────────────────────────────────┐
│ Sommerfreizeit 2027                              Offen     │
│ Ev. Kirchengemeinde Musterstadt · 26.07.–09.08.2027 · Wald │
├────────────────────────────────────────────────────────────┤
│ Konfi-Wochenende                                 Offen     │
│ Bezirksjugendwerk · Zeitraum offen                         │
└────────────────────────────────────────────────────────────┘
```

Zeile 1: Name (Kartentitel), rechts der Status als Abzeichen. Zeile 2:
Organisation, Zeitraum, Ort — in `--zl-text-secondary`, mit Mittelpunkt
getrennt, Fehlendes wird weggelassen statt als Strich gezeigt. Die ganze Zeile
führt zur Freizeit.

**Leer:** „Du bist noch keiner Freizeit zugeordnet. Sobald dich jemand zu einer
Freizeit hinzufügt, erscheint sie hier." Keine Schaltfläche — ein
Freizeitmitarbeiter kann hier nichts tun, und ein Knopf, der nichts darf, ist
eine Lüge. Wer OrgAdmin ist, sieht stattdessen „Freizeit anlegen".

### Freizeiten der Organisation `/o/{slug}`

Kopfbereich: Überschrift „Freizeiten", rechts die primäre Schaltfläche „Freizeit
anlegen" (nur OrgAdmin). Darunter dieselbe Liste wie oben, ohne
Organisationsnamen — der steht in der Kopfzeile. Geschlossene Freizeiten stehen
weiter unten und ruhiger gesetzt.

Unter 20 Freizeiten: keine Suche, keine Blätterung, keine Filter. Das ist kein
Versäumnis, sondern die Größenordnung.

### Freizeit anlegen (Dialog)

Ausgelöst durch „Freizeit anlegen". Ein kleiner Dialog, 480px breit:

```
┌──────────────────────────────────┐
│ Freizeit anlegen              X  │
│                                  │
│ Name                             │
│ [Sommerfreizeit 2027__________]  │  <- Fokus liegt hier
│                                  │
│ Zeitraum (optional)              │
│ [von ______] [bis ______]        │
│                                  │
│ Ort (optional)                   │
│ [_____________________________]  │
│                                  │
│         [Abbrechen] [ Anlegen ]  │
└──────────────────────────────────┘
```

Der Fokus steht im Namensfeld, `Enter` legt an, `Esc` bricht ab. Zeitraum und Ort
tragen sichtbar „(optional)" — der Weg „Name eintippen, Enter" ist damit ein
Handgriff, ohne die anderen Felder zu verstecken. Ein Enddatum vor dem
Startdatum ist der einzige Fehler, den der Dialog kennt. Nach dem Anlegen
schließt er, die neue Freizeit erscheint oben in der Liste und ist kurz mit
`--zl-primary-tint` hinterlegt.

### Freizeit `/o/{slug}/f/{id}`

Überschrift ist der Name, direkt an Ort und Stelle bearbeitbar (Klick auf den
Namen macht ihn zum Feld). Darunter zwei Karten nebeneinander, unter 900px
untereinander:

- **Eckdaten** — Zeitraum, Ort, Status. Jede Zeile einzeln bearbeitbar; wer nur
  Leserecht hat, sieht dieselbe Karte ohne Bearbeitungsflächen.
- **Team** — Liste der zugeordneten Personen mit Rolle. Freizeitleitung und
  OrgAdmin sehen „Person hinzufügen": ein Feld, das bestehende Mitglieder der
  Organisation vorschlägt, und darunter „Neue Person einladen".

Der Statuswechsel Offen/Geschlossen ist eine Umschaltfläche in der
Eckdaten-Karte, nicht in einem Menü versteckt. Löschen liegt als tertiäre Aktion
unten rechts, in `--zl-error`, mit einer Rückfrage, die den Namen nennt und die
30 Tage erwähnt.

### Mitglieder `/o/{slug}/team`

Tabelle: Name, E-Mail, Rolle, Beitritt. Offene Einladungen stehen oben
abgesetzt, mit verbleibender Gültigkeit und einer Schaltfläche „Link kopieren".
Denn es gibt keinen Mailversand — der Link **muss** kopierbar sein. Das ist die
eigentliche Funktion dieses Bildschirms, keine Nebensache.

Beim Einladen erscheint der Link sofort in einem Dialog, groß, mit
Kopier-Rückmeldung. Der Dialog sagt klar: „Gib diesen Link persönlich weiter. Er
ist 14 Tage gültig und funktioniert genau einmal."

### Papierkorb `/o/{slug}/papierkorb`

Liste gelöschter Freizeiten mit Löschdatum und verbleibenden Tagen („noch 22
Tage"). Je Zeile „Wiederherstellen". Kein endgültiges Leeren von Hand — die
Frist erledigt das, und ein Knopf dafür lädt nur zu Unfällen ein.

### Verwaltung (Betreiber)

`/verwaltung/organisationen`: Tabelle mit Name, Adresse, Organisationsleitung,
Anzahl Mitglieder, Zustand. Die Primäraktion „Organisation aufnehmen" öffnet
einen Dialog mit dem Namen — die Adresse wird live darunter gezeigt („wird zu
`/o/ev-kirchengemeinde-musterstadt`") und ist nach dem Anlegen unveränderlich.
Anschließend fordert der Bildschirm zum nächsten Schritt auf: „Lade jetzt die
Organisationsleitung ein."

Liegt ein Löschantrag vor, ist die Zeile mit `--zl-warning-tint` hinterlegt und
trägt „Löschung beantragt am …" samt der Aktion „Löschung ausführen". Die
Rückfrage verlangt das Abtippen des Organisationsnamens — der einzige Ort in der
Anwendung, an dem das gerechtfertigt ist.

`/verwaltung/konten`: Konten mit E-Mail, Zustand, letzter Anmeldung. Sperren und
Entsperren. **Keine** Spalte, die verrät, in welchen Organisationen jemand ist —
das wäre genau der Einblick, den der Betreiber nicht haben soll.

### Löschantrag stellen (OrgAdmin)

Unter `/o/{slug}` am Seitenende, unauffällig: „Diese Organisation löschen
lassen". Führt auf eine eigene Seite, die erklärt, was passiert (alles, 30 Tage
Papierkorb, danach endgültig), und den Antrag absendet. Danach steht dort
dauerhaft ein Hinweis mit Rücknahmemöglichkeit.

## Zustände

**Laden.** Listen zeigen drei Platzhalterzeilen in `--zl-surface-quiet`, erst ab
300ms. Dialoge zeigen beim Absenden einen Ladezustand auf der Schaltfläche, die
dabei ihre Breite behält.

**Fehler.** Netzwerk- und Serverfehler erscheinen als ruhige Leiste über der
Arbeitsfläche: „Das hat nicht geklappt. Versuche es noch einmal." plus
Wiederholen. Keine technischen Meldungen, keine roten Flächen.

**Kein Zugriff.** Wer eine Adresse öffnet, für die ihm die Zuordnung fehlt,
bekommt „Diese Seite gehört zu einer Organisation, zu der du nicht gehörst." mit
Link zur Startseite — nie eine Andeutung, ob es die Organisation gibt.

**Abgelaufene Sitzung.** Schlägt die Erneuerung fehl, landet man auf `/anmelden`
mit dem Hinweis „Deine Sitzung ist abgelaufen" und kehrt nach der Anmeldung
genau dorthin zurück, wo man war.

## Tastatur

Vollständige Bedienbarkeit ohne Maus. Die Fokusreihenfolge folgt der
Leserichtung. Dialoge fangen den Fokus, `Esc` schließt, beim Schließen kehrt der
Fokus auf die auslösende Schaltfläche zurück. In der Freizeitenliste führt
`Enter` auf einer fokussierten Zeile in die Freizeit.

---

# Teil 2 — Architektur

Ergänzt `docs/architecture.md`; dort steht das Gesamtbild, hier die Herleitung.

## Scheiben

| Projekt | Inhalt |
|---|---|
| `Zeltlotse.Core.Konten` | Identity, Anmeldung, Token, Einladungen, Ersteinrichtung |
| `Zeltlotse.Core.Organisationen` | Organisation, Slug, Mitgliedschaft, Löschantrag, Papierkorb |
| `Zeltlotse.Core.Freizeiten` | Freizeit, Zuordnung, Status, Papierkorb |
| `Zeltlotse.Core.Persistenz` | Entitäten, `ZeltlotseDbContext`, Mandantenfilter, RLS, Migrationen, Rechteauskunft, Slug-Auflösung |

Jede Scheibe hat ein `.Contracts`-Projekt und eine `Add<Scheibe>()`-Erweiterung;
ihre übrigen Typen sind `internal`. Der Client verweist direkt auf die
`.Contracts` — dieselben Typen auf beiden Seiten, keine Codeerzeugung.

Die Entitäten liegen in `Persistenz`, nicht in den Scheiben — anders entsteht
ein Zirkel zwischen Kontext und Scheiben (siehe debt.md). Die Scheiben behalten
Endpunkte, Regeln und Prüfungen.

`Persistenz` ist bewusst ein gemeinsamer Unterbau statt dreier getrennter
Kontexte: eine Datenbank, eine Migrationshistorie, ein Ort für die
Mandantenregeln. **Preis:** Jede Scheibe hängt daran; eine Scheibe lässt sich
nicht ohne diesen Unterbau herauslösen. Bei drei Scheiben auf einer Datenbank
ist das billiger als drei Migrationsstände, die auseinanderlaufen.

## Datenmodell

```
Nutzer             Id, EMail, IstGlobalAdmin, Gesperrt, ... (Identity)
Organisation       Id, Name, Slug (eindeutig), GeloeschtAm?, LoeschungBeantragtAm?
OrgMitgliedschaft  NutzerId, OrganisationId, Rolle: OrgAdmin | Mitglied
Freizeit           Id, TenantId, Name, Beginn?, Ende?, Ort?, Status, GeloeschtAm?
FreizeitZuordnung  NutzerId, FreizeitId, Rolle: Leitung | Mitarbeiter
Einladung          Id, TenantId, EMail, Rolle, Ziel, TokenHash, GueltigBis, EingeloestAm?
```

`Beginn` und `Ende` sind `DateOnly` — eine Freizeit hat Tage, keine Uhrzeiten.
Der Einladungstoken liegt nur als Hash in der Datenbank; der Klartext existiert
genau einmal, im Dialog beim Erzeugen.

## Mandantentrennung

Zwei unabhängige Netze, weil eines davon irgendwann vergessen wird:

1. **EF-Core-Query-Filter** auf jeder mandantenbehafteten Entität, gespeist aus
   einem `ITenantKontext` (scoped, aus der Adresse gefüllt).
2. **Row-Level-Security** in PostgreSQL. Ein Verbindungs-Interceptor setzt nach
   dem Öffnen `app.tenant_id`; die Richtlinien vergleichen dagegen.

**Preis:** Jede Verbindung trägt eine Sitzungsvariable, und der Interceptor muss
mit dem Verbindungspool zusammenpassen. Dafür ist ein vergessener Filter ein
leeres Ergebnis statt eines Datenlecks — bei Daten Minderjähriger später der
Unterschied zwischen Fehler und Meldepflicht.

Der Mandant wird aus `/o/{slug}` gelesen, bevor eine Abfrage läuft, und gegen
die Zuordnungen des Angemeldeten geprüft. Kein Slug in der Adresse heißt: kein
Mandantenkontext, mandantenbehaftete Abfragen sind dort gesperrt. Die Startseite
ist die bewusste Ausnahme — sie fragt über alle Zuordnungen des Nutzers, nicht
über einen Mandanten.

## Autorisierung

Ein `IBerechtigung`-Dienst beantwortet je Anfrage: Was darf dieser Nutzer bei
dieser Organisation, bei dieser Freizeit? Er liest die beiden Zuordnungstabellen
einmal pro Anfrage und legt das Ergebnis für deren Dauer ab. Richtlinien
(`OrganisationLesen`, `OrganisationVerwalten`, `FreizeitLesen`,
`FreizeitVerwalten`) greifen darauf zu.

Rollen sind additiv: Das Ergebnis ist die Vereinigung aller zutreffenden Rechte.
OrgAdmin erhält Leserecht auf alle Freizeiten seiner Organisation und
Schreibrecht auf deren Eckdaten; inhaltliche Schreibrechte kommen ausschließlich
über eine Freizeitzuordnung.

**Preis:** Eine zusätzliche Abfrage je Anfrage. Bei 30 gleichzeitigen Nutzern
belanglos — und Rechte, die in Claims stecken, sind nach jeder Rollenänderung
falsch, bis sich jemand neu anmeldet.

## Identität

ASP.NET Core Identity mit eigenen Endpunkten:

- `POST /auth/anmelden` — Zugriffstoken (15 Minuten) im Rumpf,
  Erneuerungstoken als HttpOnly-Cookie (`Domain=.zeltlotse.de`,
  `SameSite=Lax`, `Secure`, 30 Tage).
- `POST /auth/erneuern` — liest das Cookie, gibt ein neues Zugriffstoken aus und
  dreht dabei das Erneuerungstoken.
- `POST /auth/abmelden` — entwertet das Erneuerungstoken und löscht das Cookie.

Der Client hält das Zugriffstoken ausschließlich im Arbeitsspeicher. Beim Start
und bei einer `401`-Antwort ruft ein `DelegatingHandler` einmalig
`/auth/erneuern` auf und wiederholt die Anfrage.

**Fallstrick, bewusst behandelt:** Der Startaufruf „bin ich angemeldet?" darf
eine `401`-Antwort nicht als abgelaufene Sitzung deuten — dort ist sie die
richtige Antwort. Dieser Aufruf bekommt einen eigenen Weg ohne
Wiederholungslogik. Ebenso muss der Testdoppel für diesen Handler beide Formen
von Misserfolg kennen: die Statusantwort und die geworfene Ausnahme.

Schreibende Aufrufe verlangen zusätzlich einen Kopfzeilenwert
(`X-Zeltlotse-Anfrage: 1`). Ein Formular von fremder Seite kann diesen nicht
setzen; damit ist die Erneuerung über das Cookie nicht fremdauslösbar.

## Aufräumen

Ein `BackgroundService` im Server läuft täglich, entfernt endgültig, was länger
als 30 Tage als gelöscht markiert ist, und protokolliert Anzahl und Dauer. Er
ruft dafür zwei Methoden der beiden Scheiben direkt auf — keine gemeinsame
Schnittstelle, solange es genau zwei Aufrufer gibt.

## Datenfluss beim Anlegen einer Freizeit

```
Dialog ---POST /o/{slug}/freizeiten---> Endpunkt
                                          | Mandant aus Slug
                                          | Richtlinie OrganisationVerwalten
                                          v
                                        Freizeiten-Scheibe
                                          | Name pruefen (nicht leer, <= 120)
                                          v
                                        Persistenz (TenantId gesetzt, RLS aktiv)
                                          v
Liste <----------- FreizeitDto ---------- 201
```

## Entscheidungen und ihr Preis

| Entscheidung | Preis |
|---|---|
| Eine Datenbank, `TenantId` überall | Disziplin bei jeder Abfrage; RLS als zweites Netz nötig |
| Gemeinsames `Persistenz`-Projekt | Scheiben sind nicht einzeln herauslösbar |
| Rechte je Anfrage aus der Datenbank | eine Abfrage mehr, dafür nie veraltete Rechte |
| Zugriffstoken nur im Arbeitsspeicher | Erneuerung bei jedem Neuladen; dafür kein Token im Browserspeicher |
| Getrennte Auslieferung, eine Domain | Domain und CORS nötig, bevor produktiv angemeldet werden kann |
| Migration beim Start | genau eine Instanz, sonst Wettlauf |
| Kein Mailversand | „Link kopieren" ist Kernfunktion, nicht Beiwerk |
| Keine Suche, keine Blätterung | bricht jenseits von etwa 100 Freizeiten je Organisation |
