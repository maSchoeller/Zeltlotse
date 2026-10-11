---
title: 'Familienkonto und Übergabe mit 18'
type: 'feature'
created: '2026-10-11'
status: 'done'
baseline_revision: '74c4359d6b901befec2133886ee58f32244ac744'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-3-freizeit-anlegen-leitung-einsetzen-phasen.md'
warnings: [oversized]
deferred:
  - summary: >-
      Beim Übernehmen eines Profils zählen Org-Rollen der annehmenden Person nicht als „eigenes Profil“; ihre leere Person wird gelöscht und die Org-Rollen zeigen danach auf eine gelöschte PersonId.
    evidence: |-
      ProfilUebernehmenHandler prüft nur Namen, Freizeit-Rollen und Elternteil. OrgRolle liegt im Modul Organisationen (PersonId), und Personen hat keinen Pfeil zu Organisationen (AD-2); eine Org-Einladung lässt sich ohne Namen annehmen. Eine Prüfung braucht entweder einen neuen Pfeil Personen → Organisationen oder einen Empfänger von ProfilUebergabeAngenommen in Organisationen (neue Zeile im Ereignis-Fluss) – beides eine Änderung der Spine. Im Design Notes als Restrisiko benannt; die Doku-Seite profil-uebergeben.md nennt Org-Rollen nicht.
    location: >-
      src/Modules/Personen/Zeltlotse.Personen/Familien/ProfilUebernehmen.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Es gibt weder Familie noch Kinderprofile. Eltern können keine Kinder unter 18 verwalten, keinen zweiten Elternteil dazuholen (FR-3) und kein Profil an ein volljähriges Kind übergeben (FR-4, CAP-2, NS-11). Die Spine-Zeile `ProfilUebergabeAngenommen` (Personen → Konten) hat noch keinen Code, und die erneute Anmeldung vor der Profil-Übergabe (AD-25) fehlt.

**Approach:** Das Modul Personen bekommt Familie, Elternteile, Kinderprofile mit Geburtsdatum, Familien-Einladung (AD-11: einmal, 7 Tage, Mail-Bindung) und Profil-Übergabe (einmal, 14 Tage, Mail-Bindung) als Aggregat mit dem Ereignis `ProfilUebergabeAngenommen`. Konten verbindet als Empfänger das Konto der volljährigen Person mit dem Kinderprofil. Der API-Host verlangt vor dem Auslösen der Übergabe eine frische Anmeldung. Die Web-App bekommt die Seite „Familie“, eine Seite je Kind, „Familienkonto beitreten“ und „Profil übernehmen“. Gebaut wird testgetrieben mit HTTP-Tests gegen echtes PostgreSQL, Durchlauf-Test, Doku-Seiten und Screenshots.

## Boundaries & Constraints

**Always:**
- **Personen (Besitzer laut Spine), nichts davon ist `IOrganisationsEigen`:**
  - `Person` bekommt `Geburtsdatum` (`date?`) und `FamilieId` (`uuid?`, nur bei Kinderprofilen gesetzt, mit Index).
  - `Familie` hat `Id` und `AngelegtAm`.
  - `FamilienElternteil` hat `FamilieId` und `PersonId`. Der Schlüssel ist (Familie, Person), `PersonId` ist eindeutig: eine Person ist Elternteil in höchstens einer Familie, Patchwork ist nicht abgebildet. Eine Familie hat höchstens 2 Eltern mit gleichen Rechten (AD-7).
  - Eine Familie entsteht beim ersten „Kind hinzufügen“ oder „Elternteil einladen“. Die anfragende Person wird dabei ihr erster Elternteil.
- **Volljährigkeit an genau einer Stelle:** `geburtsdatum.AddYears(18) <= heute` (Kalendertag in Europe/Berlin, Uhr über `TimeProvider`). Der Server berechnet sie und liefert sie als `volljaehrig`. Der Client berechnet nie selbst.
- **Kind anlegen:** Command `KindAnlegen(ElternPersonId, Vorname, Nachname, Geburtsdatum)` legt die Familie an, falls sie fehlt, dazu die Person mit `FamilieId` und den Personen-Schlüssel (`PersonenSchluessel.Anlegen`) in derselben Transaktion.
  - Pflicht sind `vorname` (1–100 Zeichen, getrimmt) und `geburtsdatum`. `nachname` ist optional, höchstens 100 Zeichen.
  - Fehlertexte je Feld: „Vorname fehlt.“, „Geburtsdatum fehlt.“, „Das Geburtsdatum liegt in der Zukunft.“, „Kinder ab 18 legen ein eigenes Konto an.“ (wenn schon volljährig), Jahr vor 1900 → „Das Geburtsdatum ist ungültig.“
- **Kind bearbeiten:** nach AD-19 mit eigenem Enum `KindFeld { Vorname, Nachname, Geburtsdatum }` in `Api.Contracts/Personen/`. Die JSON-Namen sind `vorname`, `nachname` und `geburtsdatum` (Wert `yyyy-MM-dd`). Laden unter Zeilensperre wie `ProfilFeldAendern`. Es gelten dieselben Feldregeln außer der 18-Grenze; nach einer Änderung darf ein Kind volljährig sein.
- **Familien-Einladung (`FamilienEinladung`):** Aufbau wie `OrgEinladung`. Zweck `familien-einladung`, 7 Tage gültig, Felder `FamilieId`, `Mail` (normalisiert, `MailRegel`), `TokenHash`, `GueltigBis`, `EingeladenVon`. Den Status offen/angenommen/verworfen setzen nur Methoden. Eine neue Einladung verwirft ältere offene Einladungen derselben Familie.
  - Einladen bei schon 2 Eltern → `409` (`Problemcodes.Konflikt`).
  - Annehmen nur, wenn die Einladung gültig, offen und nicht abgelaufen ist **und** die Mail des angemeldeten Kontos (`IAngemeldeteMail`) passt. Sonst `404` ohne Änderung.
  - Ist die Person schon Elternteil derselben Familie → `200` ohne Änderung (die Einladung wird trotzdem angenommen). Ist sie Elternteil einer anderen Familie oder hat die Familie schon 2 Eltern → `409` ohne Änderung.
  - Zeilensperre (`FOR UPDATE`) auf Einladung und Familie.
- **Profil-Übergabe (`ProfilUebergabe`, erbt `Aggregat`):** Felder `Id`, `FamilieId`, `PersonId` (Kind), `Mail`, `TokenHash` (Zweck `profil-uebergabe`), `GueltigBis` (+14 Tage), `EingeladenVon`, Status über Methoden.
  - **Auslösen:** nur ein Elternteil der Familie des Kindes (sonst `404`) und nur bei volljährigem Kind (sonst `409`). Eine neue Übergabe verwirft ältere offene Übergaben desselben Kindes.
  - **Annehmen:** Token gültig, offen, nicht abgelaufen und Mail des Kontos passt, sonst `404`. Hat die angemeldete Person ein eigenes, nicht leeres Profil (Vor- oder Nachname gesetzt, eine Zeile in `freizeit_rolle` oder Elternteil einer Familie) → `409`.
  - **Wirkung des Annehmens, alles in einer Transaktion:** Kind-`FamilieId` = `null` (die Eltern verlieren den Zugriff); die leere eigene Person der angemeldeten Person wird samt Personen-Schlüssel gelöscht; die Übergabe legt das Ereignis `Events.ProfilUebergabeAngenommen(Guid PersonId, Guid KontoId)` ab (in `Personen.Contracts.Events`).
- **Konten (Empfänger, AD-4):**
  - Command `KontoMitProfilVerbinden` (`IEreignisEmpfaenger`, kein `IBenutzerCommand`), angemeldet mit `EmpfaengtEreignis<ProfilUebergabeAngenommen, KontoMitProfilVerbinden>()`.
  - `Konto.ProfilUebernehmen(personId)` setzt `PersonId` und einen neuen `SitzungsStempel`, damit alte Sitzungen ungültig werden. Hat das Konto die `PersonId` schon, passiert nichts.
  - `Konto.PersonId` bekommt dafür einen privaten Setter statt `init`.
- **Erneute Anmeldung (AD-25), hier nur für die Profil-Übergabe:**
  - Die Sitzung bekommt den Claim `zeltlotse:angemeldet` (Zeitpunkt der Anmeldung). `SitzungAusstellen` setzt ihn bei jeder Anmeldung auf „jetzt“; das Neu-Ausstellen nach „Passwort festlegen“ übernimmt den alten Wert.
  - `IAngemeldet` bekommt `AngemeldetAm` (`DateTimeOffset?`).
  - Das SharedKernel-Merkmal `.ErneuteAnmeldung()` prüft `RechtePruefung`: fehlt der Zeitpunkt oder ist er älter als 10 Minuten (`Sitzung.FrischeDauer`, `TimeProvider`) → `401` ProblemDetails. Der Client führt dann wie bei jedem 401 zur Anmeldung mit Rückweg.
- **Mails** gehen direkt aus dem Endpunkt nach dem Command raus (AD-13-Ausnahme, Muster 1-2), Deutsch ohne Anrede. Mail und Token stehen nie in Logs.
  - Familien-Einladung: Betreff „Einladung ins Familienkonto“, Link `<OeffentlicheAdresse>/app/familie/einladung/<token>`, Hinweis „gilt 7 Tage“ und „Zum Annehmen mit dieser Mail-Adresse anmelden.“
  - Übergabe: Betreff „Profil in Zeltlotse übernehmen“, Vorname des Kindes, Link `<OeffentlicheAdresse>/app/uebergabe/<token>`, Hinweis „gilt 14 Tage“ und derselbe Anmeldehinweis.
- **Endpunkte (Datenformen in `Api.Contracts/Personen/`, Datum als `DateOnly`):**

  | Endpunkt | Zugang | Ergebnis |
  |---|---|---|
  | `GET /api/ich/familie` | angemeldet, `.Organisationsuebergreifend()` | `200 {eltern: [{personId, vorname, nachname}], kinder: [{personId, version, vorname, nachname, geburtsdatum, volljaehrig, uebergabe: {mail, gueltigBis}?}], einladungen: [{id, mail, gueltigBis}]}`. Nur die eigene Familie, Kinder nach Geburtsdatum, nur offene und gültige Einladungen bzw. Übergaben. Ohne Familie sind alle Listen leer. |
  | `POST /api/ich/familie/kinder` `{vorname, nachname, geburtsdatum}` | wie oben | `201 {personId}`; `400` je Feld |
  | `PATCH /api/ich/familie/kinder/{personId:guid}` | wie oben, nur Elternteil der Familie des Kindes | AD-19 `200 {version, <feld>}`, `409` `konflikt`, `400`; fremdes Kind → `404` |
  | `POST /api/ich/familie/einladungen` `{mail}` | wie oben | `202 {id}` und Mail; `400`; `409` bei 2 Eltern |
  | `POST /api/ich/familie/kinder/{personId:guid}/uebergabe` `{mail}` | wie oben, `.ErneuteAnmeldung()` | `202 {id}` und Mail; `400`; `404` fremdes Kind; `409` noch nicht volljährig |
  | `POST /api/personen/familie/einladungen/annehmen` `{token}` | angemeldet, `.Zugriffsbegrenzt()` | `200 {}`; `404`; `409` |
  | `POST /api/personen/uebergabe/annehmen` `{token}` | angemeldet, `.Zugriffsbegrenzt()` | `200 {personId}`; `404`; `409` |

- **Sicherheitsprotokoll:**
  - `Rollenvergabe` nach angenommener Familien-Einladung (Bezug: die Einladung).
  - Neue Art `ProfilUebergabe = 7` (nur anhängen) nach angenommener Übergabe (Person: das Kind, Bezug: die Übergabe).
  - `LinkEinloesungFehlgeschlagen` bei jedem `404` der beiden Annehmen-Endpunkte.
- **Demo-Daten (nur Commands, idempotent):**
  - `DemoKatalog`: Paul Muster wird volljährig mit Geburtsdatum `2008-03-14`.
  - Für jede Demo-Person mit Rolle `Eltern` werden die Kinder ihrer Familie per `KindAnlegen` angelegt: Katrin bekommt Paul und Emma, Daniel bekommt Mia. Gibt es in der Familie schon ein Kind mit gleichem Vornamen, wird es übersprungen.
- **Web-App** (nur Bausteine aus Ui.Komponenten, Ordner `Features/Personen/`, Client-Methoden in `PersonenApiClient`, Datumstexte über `Anzeige.Datum`):
  - **`/familie`:** h1 „Familie“ und Link „Zur Startseite“.
    - Je Kind eine Card (Titel `Anzeige.Name`) mit „Geboren am TT.MM.JJJJ“ und Knopf (Outline) „Angaben ändern“ → `familie/kinder/{id}`.
    - Bei offener Übergabe: „Übergabe verschickt an <mail>, gültig bis TT.MM.JJJJ“.
    - Nur bei `volljaehrig`: Feld „Mail-Adresse von <Vorname>“ und `InlineBestaetigung`. Auslöser „Profil an <Vorname> übergeben“, Satz „Profil an <Vorname> übergeben? Danach hat das Familienkonto keinen Zugriff mehr.“, Knopf „Ja, übergeben“. Danach „Gespeichert: Die Übergabe ist verschickt.“, ein Feldfehler am Feld, `409` → „Fehler: <Vorname> ist noch nicht volljährig.“
    - Ohne Kinder: `LeerZustand` „Noch keine Kinder im Familienkonto.“
    - Card „Kind hinzufügen“: Felder „Vorname“, „Nachname“, „Geburtsdatum“ (`type=date`), Pflichtfelder mit „*“ und Legende; Knopf „Kind hinzufügen“; danach „Gespeichert: Das Kind ist hinzugefügt.“ und die Liste neu geladen.
    - Card „Eltern“: Datentabelle mit Namen und offenen Einladungen.
    - Bei weniger als 2 Eltern: Card „Zweiten Elternteil hinzufügen“ (Feld „Mail-Adresse“, Knopf „Einladung schicken“, danach „Gespeichert: Die Einladung ist verschickt.“, `409` → „Fehler: Das Familienkonto hat schon zwei Eltern.“).
  - **`/familie/kinder/{PersonId:guid}`:** Daten aus `GET /api/ich/familie`. h1 Name des Kindes, `AutosaveFeld` „Vorname“, „Nachname“ und „Geburtsdatum“ (`Typ="date"`) an den PATCH-Pfad, Link „Zurück zur Familie“. Kind nicht in der Familie → `NichtVerfuegbar`.
  - **`/familie/einladung/{Token}`:** h1 „Familienkonto beitreten“, Erklärtext, Knopf „Einladung annehmen“ (POST erst beim Klick) → `familie`.
    - `404` → „Fehler: Diese Einladung ist nicht mehr gültig oder gilt für eine andere Mail-Adresse.“
    - `409` → „Fehler: Dieses Konto gehört schon zu einer anderen Familie, oder das Familienkonto hat schon zwei Eltern.“
    - Rückweg nach der Anmeldung wie bei `/einladung/{token}`.
  - **`/uebergabe/{Token}`:** h1 „Profil übernehmen“, Erklärtext, Knopf „Profil übernehmen“ (POST erst beim Klick).
    - Erfolg → „Gespeichert: Das Profil ist übernommen. Zum Abschluss neu anmelden.“ mit Knopf „Neu anmelden“ (abmelden, dann `anmelden`).
    - `404` → „Dieser Link ist nicht mehr gültig. Die Eltern können die Übergabe erneut auslösen.“
    - `409` → „Fehler: Dieses Konto hat schon ein eigenes Profil. Zum Übernehmen mit einer Mail-Adresse ohne eigenes Profil anmelden.“
  - **Start:** Ist die Person Elternteil, steht vor den Freizeit-Cards eine Card „Familie“ mit den Namen der Kinder (oder „Noch keine Kinder im Familienkonto.“) und dem Knopf „Familie öffnen“ → `familie`.
  - **Konto:** Knopf (Outline) „Familienkonto öffnen“ → `familie`.
- **Tests (AD-30), Test vor dem Code:** Personen-Fachlogik (Volljährigkeit mit `FakeTimeProvider` am Tag vor bzw. am 18. Geburtstag in Berlin, Einladung, Übergabe, Annehmen-Regeln), Konten-Empfänger, HTTP-Tests für jede Matrix-Zeile, Durchlauf-Test `ProfilUebergabeAngenommen`, bUnit für Client und Seiten, Doku-Screenshots (Handy, Laptop).
- **Doku:**
  - Neu `eltern/familienkonto.md`: Kind hinzufügen, Angaben ändern, zweiten Elternteil einladen und Einladung annehmen; Bilder `familie`, `kind-angaben`, `familie-einladung`.
  - Neu `eltern/profil-uebergeben.md`: ab 18, erneute Anmeldung nach 10 Minuten, Mail, was das Kind tut, Link abgelaufen, ohne Übergabe eigenes Konto; Bild `profil-uebernehmen`.
  - Anpassen: `eltern/index.md` (Links), `daten.md` (Abschnitt „Familie und Übergabe“: Geburtsdatum, Eltern sehen Kinder nur bis zur Übergabe, Einladung und Übergabe speichern die Mail-Adresse, das leere eigene Profil wird beim Übernehmen gelöscht), `erste-schritte/anmelden.md` bzw. Konto-Text, falls der Bildtext betroffen ist.

**Never:**
- Keine Anmeldung von Kindern zu Freizeiten, kein Freizeitpass, keine Elterninfo (2-x). Keine Familienübersicht mit Freizeiten oder Anmeldestatus (kommt mit 2-2/2-3).
- Kein Löschen von Kindern oder Familien, kein Austreten von Eltern, kein Zurückziehen von Einladungen oder Übergaben, keine Patchwork-Familien, kein Konto für Kinder unter 18.
- Kein automatisches Zusammenführen von Profilen mit Inhalt (AD-7). Keine erneute Anmeldung für Export oder Löschung (1-7).
- Keine neuen Problemcodes in `Gemeinsam/`, kein Pfeil Konten → Personen außer dem Ereignis-Typ, keine Änderung der Ereignis-Fluss-Tabelle.
- Keine Mail-Adressen fremder Personen in Antworten außer der eigenen Familie, keine Kinderprofile in fremden Familien.
- Keine Pop-ups, kein Token in Browser-Speicher oder Query-String, keine Lockerung von CSP, Antiforgery oder Cookie-Richtlinie. Kein Commit auf `main`, kein Push.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Kind hinzufügen | Person ohne Familie, `{Emma, Muster, 2017-09-02}` | `201`; Familie mit Person als Elternteil; Kind mit Schlüssel; `GET` zeigt Emma, `volljaehrig=false` | leerer Vorname, fehlendes/zukünftiges Datum, Jahr 1899, schon 18, 101 Zeichen → `400` je Feld |
| Volljährigkeit | Geburtsdatum 2008-10-12, Uhr 2026-10-11 23:59 bzw. 2026-10-12 00:00 Berlin | `false` bzw. `true` | – |
| Persönlich | Elternteil, zweiter Elternteil, Elternteil anderer Familie, Jonas (gleiche Organisation) | `GET` nur die eigene Familie und nur Vertragsfelder; PATCH/Übergabe am fremden Kind → `404` | – |
| Kind bearbeiten | PATCH Vorname mit passendem / abweichendem `alterWert`; Geburtsdatum ändern | `200` / `409` `konflikt` / `200` | leer, ungültiges Datum → `400` |
| Einladen | Familie mit 1 Elternteil | `202`, Mail mit `/app/familie/einladung/` | 2 Eltern → `409`; Mail ungültig → `400` |
| Einladung annehmen | angemeldet mit eingeladener Mail | `200`; zweiter Elternteil sieht dieselben Kinder; `Rollenvergabe` protokolliert | – |
| Einladung Sonderfälle | schon Elternteil derselben Familie / einer anderen Familie / Familie inzwischen voll | `200` ohne Änderung / `409` / `409` | – |
| Token-Fälle | abgelaufen (+7 Tage +1 Min. bzw. +14 Tage +1 Min.), zweites Annehmen, verworfen, Token einer Org-Einladung, Login-Token, Familien-Token an der Übergabe (und umgekehrt), unbekannt, Formfehler, andere Mail | jeweils `404`, `LinkEinloesungFehlgeschlagen`, nichts geändert | – |
| Übergabe auslösen | Kind volljährig, Anmeldung jünger als 10 Min. | `202`, Mail mit `/app/uebergabe/`; ältere offene Übergabe verworfen | Kind 17 → `409`; Anmeldung 11 Min. alt → `401` |
| Übergabe annehmen | Paul meldet sich mit neuer Mail an (leeres Profil) und nimmt an | `200 {personId: Paul}`; Kind ohne Familie; leere Person und ihr Schlüssel gelöscht; Ereignis in `personen.outbox`; `ProfilUebergabe` protokolliert; Eltern sehen Paul nicht mehr (`GET`, `PATCH` → `404`) | eigenes Profil mit Namen / Freizeit-Rolle / Elternteil → `409` |
| Durchlauf | Annehmen, Outbox zustellen (zweimal) | `konten.konto.PersonId` = Paul, neuer Stempel; alte Sitzung → `401`; nach neuer Anmeldung liefert `GET /api/ich/profil` Pauls Namen | – |
| Demo-Daten | Start mit Demo zweimal | 12 Personen und Schlüssel, 2 Familien, 2 Elternteile, 3 Kinder, nichts doppelt | – |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen/Profil/Person.cs` -- `Person` (kein Aggregat, privater Konstruktor, `Anlegen(personId, jetzt)` Z.26, `FeldAendern` Z.39 mit Konfliktregel). Hier kommen `Geburtsdatum`, `FamilieId` und Methoden für Kind und Übergabe dazu. `Profil/ProfilFeldAendern.cs:61-64` zeigt das Muster der Zeilensperre (`FromSql "SELECT *, xmin … FOR UPDATE"`). `Profil/ProfilEndpunkte.cs:24` liest die `PersonId` aus `IAngemeldet` (Vorbild für die Endpunkte). `Profil/ProfilSicherstellen.cs` legt Person und Schlüssel an (Vorbild `KindAnlegen`; `hauptschluessel.NeuenPersonenSchluesselUmhuellen(personId)`).
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs:45-73` -- `PersonenModell.Konfigurieren`, Schema `personen`, Tabellen `person` (`Version` `IsRowVersion`), `personen_schluessel`, `freizeit_rolle`. Neue Tabellen und Migration hier (letzte: `20261011184533_FreizeitRollenListe`).
- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:24-41` -- Registrierung (`EmpfaengtEreignis` Z.29), Gruppe `/ich/profil` mit `.Organisationsuebergreifend()`. Hier neue Gruppen `/ich/familie` und `/personen/...`. Der einzige öffentliche Typ bleibt `PersonenModul`.
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Events/EventsNamensraum.cs` -- leerer Namensraum; hier kommt `ProfilUebergabeAngenommen` hin (Vorbild `Freizeiten.Contracts/Events/FreizeitRolleGeaendert.cs`: `public sealed record`).
- `src/Modules/Personen/Zeltlotse.Personen/Freizeitrollen/FreizeitRolleUebernehmen.cs` -- Vorbild für einen Empfänger-Command (`IEreignisEmpfaenger<TSelf, TEreignis>`, `static Aus(Umschlag, e)`).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Domaene/OrgEinladung.cs` und `Einladungen/NeueEinladung.cs` (`EinladungAusstellen`), `Einladungen/EinladungAnnehmen.cs:166-226` (`EinladungSperre.PerTokenHashAsync`, Protokoll bei Fehler), `Einladungen/EinladungsMail.cs` und `Verwaltung/OrganisationenEndpunkte.cs` (Mail direkt nach dem Command, `202`) -- Muster für Einladung und Übergabe. `Freizeiten/.../FreizeitEndpunkte.cs:15-35` nutzt `IAngemeldeteMail` (so wird hier angenommen).
- `src/Zeltlotse.SharedKernel/Zugangslinks/` -- `Zugangslink.NeuesToken()`, `TokenHash(zweck, token)`, `KannTokenSein`; `MailAdresse`, `MailRegel.MailAdresse()`. `Aggregat.cs` (`EreignisAblegen`, `Folgenummer`). `Sicherheitsprotokoll/ISicherheitsprotokoll.cs` (Enum: hier `ProfilUebergabe = 7` anhängen). `Anmeldung/Sitzung.cs` (Claims, `Prinzipal`, `Lesen`, `Dauer`) und `Anmeldung/IAngemeldet.cs` -- Claim und `AngemeldetAm` ergänzen. `Web/Organisationsuebergreifend.cs` -- Vorbild für das Merkmal `.ErneuteAnmeldung()`.
- `src/Modules/Konten/Zeltlotse.Konten/Domaene/Konto.cs:18` -- `PersonId` ist `private init`. Der Index auf `PersonId` ist eindeutig (`Daten/KontenDbContext.cs:41-49`). `KontenModul.cs` hat noch keinen Empfänger. `Sitzungen/SitzungAusstellen.cs:10` (SignIn) und `Passwort/*` (Neu-Ausstellen behält `angemeldet`). Ein Verweis auf `Zeltlotse.Personen.Contracts` ist erlaubt (`ProjektDateiRegeln.cs:16-28`); `AbhaengigkeitsRegeln.cs:119-126` erlaubt Ereignis-Typen ohne Pfeil.
- `src/Zeltlotse.Api/Sicherheit/SitzungsRegistrierung.cs:42-73` -- Prüfung je Anfrage vergleicht `PersonId` und Stempel (`KontoPruefung.cs:11`); `HttpAngemeldet` liefert `AngemeldetAm`. `src/Zeltlotse.Api/Rechte/RechtePruefung.cs:28-79` -- hier die Prüfung für `.ErneuteAnmeldung()` (401 ProblemDetails, `TimeProvider`).
- `src/Zeltlotse.Api/Demo/DemoKatalog.cs:22-35,64` (Pauls Geburtsdatum, `DemoPerson.Familie`/`Geburtsdatum`) und `Demo/DemoDaten.cs` (Schleife über Personen mit Mail Z.26, Commands über `ISender` in eigenem Scope) -- Kinder per `KindAnlegen` einspielen.
- `src/Zeltlotse.Api.Contracts/Personen/Profil.cs` -- Vorbild für die Datenformen; neu `Familie.cs` mit `KindFeld`/`KindFelder` (JSON-Namen, `MaximaleLaenge`). `Gemeinsam/Problemcodes.cs` -- nur `Konflikt` verwenden.
- `src/Zeltlotse.Ui/Api/PersonenApiClient.cs` -- erweitern. Muster für Ergebnis-Records mit Feldfehlern und `LadenAsync<T>` in `OrganisationenApiClient.cs:58-66,101-113`; `ApiFehler.FeldFehlerAsync`. `Texte/Anzeige.cs` (`Datum`, `Name`).
- `src/Zeltlotse.App/Pages/Start.razor:56-66` (Laden, Cards), `Features/Konten/Konto.razor` (`AutosaveFeld`, Outline-Knopf), `Features/Organisationen/EinladungOeffnen.razor:35-58` (POST beim Klick, Fehler), `OrganisationAnsicht.razor:73-87` (Einladungsformular), `Features/Organisationen/NichtVerfuegbar.razor`, `Sitzung/AppSitzung.cs:60-71` (Rückweg bei 401), `Features/Konten/AnmeldungAbschluss.cs` (`AbmeldenAsync`, `IstAktionsfehler`, `FehlerText`), `_Imports.razor` (neuer Namensraum `Features.Personen`).
- `src/Zeltlotse.Ui.Komponenten/InlineBestaetigung.razor` (`AusloeserText`, `Satz`, `JaText`, `Bestaetigt`), `Card`, `Datentabelle`/`Spalte<T>`, `Feld` (`Typ="date"`), `Formular`, `Knopf`, `Meldung`, `LeerZustand`, `Ladezustand` -- reichen aus, keine neuen Bausteine.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- Liste `Organisationsuebergreifend` um die fünf `/api/ich/familie…`-Endpunkte ergänzen; zwei neue Tests „Annehmen … angemeldet und zugriffsbegrenzt“ wie bei Org und Leitung. Helfer: `AnmeldeHelfer` (`AngemeldeterClientAsync`, `PostAsync`, `SendenAsync`, `WertAsync`, `ProtokollAsync`, `PersonIdAsync`), `OrgHelfer`, `FreizeitHelfer` (Vorbild für einen `FamilienHelfer`). `DemoDatenTests.cs:215-267,345-373` (Zählungen 9 → 12 usw.).
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` (`Uhr` ab 2026-07-01 08:00Z, `OutboxJetztZustellenAsync`) und `MailOrdner.cs` (`LoginMailMuster.Link` um `familie/einladung|uebergabe` ergänzen).
- `tests/Zeltlotse.Personen.Tests/` (Fixture `PostgresContainer`, Commands über `ISender`), `tests/Zeltlotse.Konten.Tests/KontenCommandTests.cs`, `tests/Zeltlotse.Ablaeufe.Tests/FreizeitRolleGeaendertTests.cs` (Vorbild Durchlauf), `tests/Zeltlotse.ArchitectureTests/EreignisFlussTests.cs` (die Spine-Zeile ist schon da).
- `tests/Zeltlotse.Ui.Tests/FreizeitenSeitenTests.cs:40-52,324-420` -- Muster für Setup, POST beim Klick, 404-Meldung, 401-Rückweg und Start.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Muster `Einladung`/`Leitung_Einladung` (Seite mit Platzhalter-Token ohne Anmeldung), `E2EHilfe.PerLinkAnmeldenAsync`, `BildAsync`, `AxeAsync`. Katrin (`katrin.muster@example.org`) ist Elternteil in den Demo-Daten.
- `doku/src/content/docs/eltern/index.md`, `daten.md`, `org-admin/einladung-annehmen.md` (Seitenaufbau „So geht es“, Bilder, `:::tip`). `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs`: jedes Bild verlinkt, keine Anrede.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Personen.Tests/` -- ZUERST: `FamilienTests` (Kind anlegen mit Schlüssel, Validierung, Volljährigkeit an der Berliner Tagesgrenze, Einladung: Ablauf, einmal, Verwerfen, Mail-Bindung, 2 Eltern, andere Familie; Übergabe: Volljährigkeit, Verwerfen, Annehmen mit Löschen der leeren Person, Ereignis einmal, `409`-Fälle; Nebenläufigkeit beim gleichzeitigen Annehmen).
- `tests/Zeltlotse.Konten.Tests/` -- ZUERST: Empfänger `KontoMitProfilVerbinden` (setzt `PersonId`, neuer Stempel, idempotent, Registrierung über `EreignisKatalog`).
- `tests/Zeltlotse.Api.Tests/` -- ZUERST: `FamilieTests`, `FamilienEinladungTests`, `ProfilUebergabeTests` für jede Matrix-Zeile (401 inkl. erneute Anmeldung, 404, 409, Token-Fälle, Protokoll, Mail-Inhalt), `EndpunktListeTests`, `DemoDatenTests`; Sitzung: Claim `angemeldet` bleibt nach „Passwort festlegen“ erhalten.
- `tests/Zeltlotse.Ablaeufe.Tests/` -- ZUERST: Durchlauf `ProfilUebergabeAngenommen` über die echten Endpunkte.
- `tests/Zeltlotse.Ui.Tests/` -- ZUERST: Client-Methoden; Seiten Familie (Cards, Leer-Zustand, Kind hinzufügen, Einladen, Übergabe mit Inline-Bestätigung und Fehlern), Kind-Seite (Autosave, nicht verfügbar), Einladung beitreten und Profil übernehmen (POST erst beim Klick, 404/409, Rückweg bei 401, Neu anmelden), Start mit Familie-Card, Konto-Knopf.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Bilder `familie` (Katrin), `kind-angaben` (Katrin, Emma), `familie-einladung` und `profil-uebernehmen` (Seiten mit Platzhalter-Token), je Handy und Laptop, mit axe.
- `src/Zeltlotse.SharedKernel/` -- Claim, `IAngemeldet.AngemeldetAm`, `.ErneuteAnmeldung()`, `Sitzung.FrischeDauer`, Protokoll-Art.
- `src/Modules/Personen/` -- Domäne, Modell, Migration, Commands, Endpunkte, Mails, Ereignis in Contracts.
- `src/Modules/Konten/` -- Verweis auf Personen.Contracts, `Konto.ProfilUebernehmen`, Empfänger, Registrierung, Claim beim Ausstellen.
- `src/Zeltlotse.Api/` -- `HttpAngemeldet`, `RechtePruefung`, Demo-Daten.
- `src/Zeltlotse.Api.Contracts/Personen/` -- Datenformen und `KindFeld`.
- `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` -- Client, Seiten unter `Features/Personen/`, Start, Konto, `_Imports.razor`.
- `doku/` -- neue Seiten und Anpassungen laut Always; Screenshots aus den Tests.

**Acceptance Criteria:**
- Given ein frischer Klon mit Docker, when `dotnet test` läuft, then sind alle Tests grün (Architektur, Personen, Konten, Abläufe, API, Ui, E2E) und die Screenshots `familie`, `kind-angaben`, `familie-einladung`, `profil-uebernehmen` (Handy, Laptop) sind geschrieben.
- Given Katrin (Demo) meldet sich an, when sie auf der Startseite „Familie öffnen“ tippt, then sieht sie Paul und Emma; nur bei Paul steht „Profil an Paul übergeben“.
- Given Katrin übergibt Pauls Profil an eine neue Mail-Adresse, when Paul den Link öffnet, sich mit dieser Mail anmeldet, „Profil übernehmen“ tippt und sich neu anmeldet, then gehört sein Konto zu Pauls Profil (Name Paul Muster im Konto), und Katrins Familienseite zeigt Paul nicht mehr.
- Given die Screenshots liegen vor, when in `doku/` `npm ci` und `npm run build` laufen, then endet der Build ohne Fehler und die neuen Eltern-Seiten zeigen die Bilder.

## Spec Change Log

## Review Triage Log

### 2026-10-11 — Review pass
- verdicts: 33 findings — high 0, medium 4, low 20, false 9, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind) Volljährigkeit wird beim Übernehmen nicht erneut geprüft; nach Auslösen und Zurücksetzen des Geburtsdatums bekäme ein Kind unter 18 das Profil — Annehmen behandelt ein nicht mehr volljähriges Kind wie einen ungültigen Link (404, Protokoll), Test ergänzt.
  - `[medium]` `[defer]` (blind) Org-Rollen zählen beim Übernehmen nicht als eigenes Profil — Personen hat keinen Pfeil zu Organisationen (AD-2); Fix braucht eine Spine-Änderung, siehe `deferred`.
  - `[low]` `[reject]` (blind) Erneutes Einspielen der Demo-Daten nach einer echten Übernahme von Paul legt ihn doppelt an — tritt nur auf, wenn jemand in der Entwicklung Paul übernimmt und neu startet; Fix wäre eine zusätzliche Abfrage über Übergaben.
  - `[low]` `[reject]` (blind) Kurzes Fenster nach dem Übernehmen, in dem die alte Sitzung gilt und `POST /api/ich/profil` die gelöschte Person neu anlegen kann — der Zustelldienst läuft im Takt von 2 Sekunden, die Seite bietet nur „Neu anmelden“; Folge wäre ein leeres verwaistes Profil ohne Konto, das der Lösch-Dienst (1-7) erfasst. Fix wären zusätzliche Wächter.
  - `[low]` `[patch]` (blind) Gleichzeitiges Annehmen einer Einladung und „Kind hinzufügen“ derselben Person endet in 500 statt 409 — Annehmen-Handler nehmen jetzt dieselbe Sperre je Person wie `KindAnlegen`.
  - `[low]` `[reject]` (blind) Auslösen der Übergabe steht nicht im Sicherheitsprotokoll — AD-22 nennt Anmeldungen, fehlgeschlagene Link-Einlösungen, Rollenvergaben, Exporte und Löschungen; Annehmen und Fehlversuche sind protokolliert.
  - `[false]` `[reject]` (blind) Eltern behalten volljährige Kinder ohne Ende — FR-4 legt genau das fest („Ohne Übergabe legt das Kind … ein eigenes Konto an“); Löschfristen kommen mit 1-7.
  - `[low]` `[reject]` (blind) Erneute Anmeldung antwortet mit schlichtem 401, die eingetippte Mail-Adresse geht verloren — bewusste Entscheidung in Design Notes; ein eigener Problemcode verlangt eine Spine-Änderung (AD-5).
  - `[low]` `[patch]` (blind) „Ja, übergeben“ lässt sich doppelt auslösen — Sperre während der laufenden Übertragung ergänzt.
  - `[low]` `[patch]` (blind) Doku stellt die 18-Grenze als harte Regel dar, obwohl ein späteres Geburtsdatum volljährig machen darf — Hinweis in `eltern/familienkonto.md` ergänzt.
  - `[low]` `[reject]` (blind) Kein Fremdschlüssel `person.FamilieId` → `familie` und `profil_uebergabe.PersonId` → `person` — Kinder und Familien lassen sich in dieser Story nicht löschen; Fix wäre eine weitere Migration ohne erreichbaren Fehlerfall.
  - `[low]` `[reject]` (edge) Alte Sitzung nach Übernahme legt Person neu an, zweite Übernahme verwaist — gleiche Ursache wie blind „kurzes Fenster“, verworfen.
  - `[medium]` `[defer]` (edge) Org-Rollen beim Übernehmen ignoriert — gleiche Ursache wie blind, zurückgestellt.
  - `[low]` `[patch]` (edge) Annehmen zweier Familien-Einladungen bzw. mit `KindAnlegen` gleichzeitig → 500 — gleiche Ursache wie blind, Sperre je Person.
  - `[low]` `[patch]` (edge) Übernehmen gleichzeitig mit `KindAnlegen` derselben Person lässt eine Elternteil-Zeile der gelöschten Person zurück — gleiche Sperre je Person im Übernehmen-Handler.
  - `[low]` `[reject]` (edge) Demo-Daten nach Übernahme doppelt — gleiche Lage wie blind, verworfen.
  - `[low]` `[reject]` (edge) Einladung an die eigene Mail-Adresse oder die des zweiten Elternteils — harmlos (Annehmen ergibt 200 ohne Änderung); Fix wäre neue Validierung.
  - `[low]` `[patch]` (edge) Doppeltes Bestätigen der Übergabe — gleiche Ursache wie blind, gepatcht.
  - `[low]` `[reject]` (edge) Unvollständiges Datum ergibt „Geburtsdatum fehlt.“ — ein Datumsfeld liefert bei unvollständiger Eingabe keinen Wert, der Text trifft zu; Fix wäre Client-Logik.
  - `[low]` `[reject]` (edge) Neue Anmeldung vor der Zustellung zeigt ein leeres Profil — Takt des Zustelldienstes 2 Sekunden, eine Anmeldung per Mail dauert länger; gleiche Ursache wie blind „kurzes Fenster“.
  - `[low]` `[patch]` (verification-gap) Sitzung ohne Claim `zeltlotse:angemeldet` gegen die erneute Anmeldung ungetestet — Test ergänzt.
  - `[low]` `[patch]` (verification-gap) Ausblenden abgelaufener Einladungen und Übergaben in `GET /api/ich/familie` ungetestet — Zusicherungen in den Fällen „abgelaufen“ ergänzt.
  - `[medium]` `[defer]` (verification-gap, other) Org-Rollen beim Übernehmen ignoriert — gleiche Ursache wie blind, zurückgestellt.
  - `[false]` `[reject]` (intent) Keine Tests für 403 und fremde Organisation — kein neuer Endpunkt hat einen Organisations- oder Freizeit-Kontext; AD-30 verlangt für persönliche Endpunkte Selbst/Elternteil/fremde Person, alle getestet.
  - `[false]` `[reject]` (intent) Anmelde-Pflicht der Annehmen-Endpunkte nur über Metadaten — `Jeder_angemeldete_Endpunkt_antwortet_ohne_Sitzung_mit_401_ProblemDetails` ruft jeden Endpunkt ohne Sitzung auf.
  - `[false]` `[reject]` (intent) „Ohne Übergabe eigenes Konto“ nur als Doku — das ist der bestehende Weg der Anmeldung (1-1), keine neue Funktion.
  - `[false]` `[reject]` (intent) „Kinder unter 18“ als Grenze für das Verwalten — FR-4 lässt das Profil bis zur Übergabe bei den Eltern; die Grenze gilt beim Anlegen und als Bedingung der Übergabe.
  - `[false]` `[reject]` (intent) Kein Browser-Test des ganzen Ablaufs — AD-30 verlangt Ende-zu-Ende-Tests nur für Gast-Anmeldung, Login und Notfall; HTTP-, Durchlauf- und bUnit-Tests decken ihn ab.
  - `[false]` `[reject]` (intent) Doku-Build nicht im Diff belegt — `npm run build` lief mit Exit-Code 0 (Verification).
  - `[false]` `[reject]` (intent) Test vor dem Code nicht sichtbar — Prozessaussage.
  - `[low]` `[reject]` (intent) Die Seite „Profil übernehmen“ für das Kind steht unter Eltern — die Doku kennt keinen Rollen-Ordner für volljährige Teilnehmende (`DokuRegeln.RollenOrdner`), die Eltern-Seite verlinkt den Ablauf vollständig.
  - `[low]` `[patch]` (intent) AD-30 „Token für X wirkt nicht auf Y“ für die Übergabe nicht ausdrücklich getestet — Test mit zwei volljährigen Kindern ergänzt.
  - `[false]` `[reject]` (intent) Änderungen an SharedKernel und Sitzung über die Story hinaus — verlangt von AD-25 (erneute Anmeldung vor Profil-Übergabe) und AD-4.

## Design Notes

- **Kind hinzufügen von Hand:** UX zeigt die Familienübersicht, sagt aber nicht, wie Kinder hineinkommen; mit der Anmeldung (2-2) kommt später „Konto aus Daten erstellen“. Ohne eigenes Hinzufügen wäre FR-3 vor V2 nicht nutzbar. Pflicht sind nur Vorname und Geburtsdatum (NFR-12); das Geburtsdatum braucht es für die 18-Grenze.
- **Startseite:** Eltern bekommen eine Card „Familie“ statt einer eigenen rollenabhängigen Startseite. Das ist einfach und bricht die Startseiten anderer Rollen nicht. Die volle Familienübersicht mit Freizeiten und Status kommt mit der Anmeldung.
- **Übernehmen nur mit leerem Profil:** AD-7 verbietet automatisches Zusammenführen. Das Kind meldet sich mit seiner Mail an; dabei entsteht ein leeres Profil, das beim Annehmen gelöscht wird. Hat das Konto schon ein Profil mit Inhalt (die Person ist inzwischen selbst aktiv), gibt es `409`, und FR-4 gilt („legt ein eigenes Konto an“). Org-Rollen sieht Personen nicht (kein Pfeil); ein Konto mit Org-Rolle, aber ohne Namen ist ein Restrisiko.
- **Erneute Anmeldung jetzt:** AD-25 bindet die Profil-Übergabe, die in dieser Story entsteht. Ein Anmelde-Zeitpunkt in der Sitzung und ein Merkmal am Endpunkt sind die kleinste Lösung; 1-7 nutzt dasselbe Merkmal für Export und Löschen. 10 Minuten reichen für „anmelden und sofort übergeben“. Die Antwort `401` nutzt den bestehenden Weg zur Anmeldung mit Rückweg, ohne neuen Problemcode.
- **409 mit `konflikt`:** AD-27 verlangt für Fachregeln `409` mit einem Code aus `Gemeinsam/`. Neue Codes erfordern eine Änderung der Spine, darum der vorhandene Code `konflikt`; die Seiten unterscheiden die Fälle über den Endpunkt.
- **Demo:** Paul wird volljährig (2008-03-14), damit Screenshot und Doku den Übergabe-Knopf zeigen (NS-11); Emma und Mia bleiben Kinder.
- **AD-30-Pflichtfälle:** persönlich: Elternteil, zweiter Elternteil, fremde Person (auch aus derselben Organisation) → `404`, nur Vertragsfelder. Token: gültig, abgelaufen, verworfen oder benutzt → `404`, anderer Zweck → `404`, die Übergabe für Kind X wirkt nicht auf Y. „Falsche Rolle 403“ und „fremde Organisation 404“ haben keinen Gegenstand, weil kein Endpunkt einen Organisations- oder Freizeit-Kontext hat.
- **29. Februar:** `AddYears(18)` ergibt in Nicht-Schaltjahren den 28. Februar; das ist die einfachste Regel ohne Sonderfall.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `eltern/familienkonto/` und `eltern/profil-uebergeben/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:** Familienkonto und Übergabe mit 18 (CAP-2, FR-3, FR-4, NS-11):
- **Personen:** Familie, Elternteile (höchstens 2, Person in höchstens einer Familie), Kinderprofile mit Geburtsdatum, Volljährigkeit an einer Stelle (Kalendertag Europe/Berlin), Familien-Einladung (einmal, 7 Tage, Mail-Bindung), Profil-Übergabe als Aggregat (einmal, 14 Tage, Mail-Bindung) mit Ereignis `ProfilUebergabeAngenommen`. Übernehmen nur mit leerem eigenem Profil; das leere Profil wird samt Schlüssel gelöscht, das Kind verlässt die Familie. Sieben Endpunkte laut Spec, Mails direkt aus dem Endpunkt.
- **Konten:** Empfänger `KontoMitProfilVerbinden` (Konto bekommt die Person des Kindes und einen neuen Sitzungsstempel).
- **Querschnitt (AD-25):** Anmelde-Zeitpunkt in der Sitzung, Merkmal `.ErneuteAnmeldung()` (10 Minuten) an „Profil übergeben“, Neu-Ausstellen nach „Passwort festlegen“ behält den Zeitpunkt; Sicherheitsprotokoll-Art `ProfilUebergabe = 7`.
- **Web-App:** Seiten „Familie“, Angaben eines Kindes (Autosave), „Familienkonto beitreten“, „Profil übernehmen“; Card „Familie“ auf der Startseite für Eltern, Knopf „Familienkonto öffnen“ im Konto.
- **Demo-Daten:** Katrin mit Paul (volljährig, 2008-03-14) und Emma, Daniel mit Mia.
- **Doku:** neue Seiten `eltern/familienkonto.md`, `eltern/profil-uebergeben.md`; Anpassungen `eltern/index.md`, `daten.md` (Abschnitt „Familie und Übergabe“), `erste-schritte/anmelden.md`, `erste-schritte/zeltlotse-oeffnen.md`; Screenshots `familie`, `kind-angaben`, `familie-einladung`, `profil-uebernehmen` (Handy, Laptop).

**Abweichungen (begründet):** Die Demo legt Paul unter 18 an und setzt danach sein Geburtsdatum per `KindFeldAendern` (nur Commands; `KindAnlegen` lehnt Volljährige zu Recht ab). `PostgresContainer` schließt ruhende Verbindungen nach 10 Sekunden, weil die größere Testmenge sonst „too many clients“ auslöste.

**Dateien:**
- `src/Modules/Personen/` – `Familien/` (Domäne, Commands, Endpunkte, Mails, Sperren, Volljährigkeit), `Profil/Person.cs`, Modell, Migration `FamilieUndUebergabe`, Ereignis in Contracts, Registrierung.
- `src/Modules/Konten/` – `Konto.ProfilUebernehmen`, `Profil/KontoMitProfilVerbinden.cs`, Sitzung mit Anmelde-Zeitpunkt, Verweis auf Personen.Contracts.
- `src/Zeltlotse.SharedKernel/` – Sitzungs-Claim, `IAngemeldet.AngemeldetAm`, `.ErneuteAnmeldung()`, Protokoll-Art.
- `src/Zeltlotse.Api/` – `RechtePruefung` (401 ohne frische Anmeldung), `HttpAngemeldet`, Demo-Daten und -Katalog.
- `src/Zeltlotse.Api.Contracts/Personen/Familie.cs` – Datenformen, `KindFeld`.
- `src/Zeltlotse.Ui/`, `src/Zeltlotse.App/` – Client-Methoden, Seiten unter `Features/Personen/`, Start, Konto.
- `tests/` – `Personen.Tests/FamilienTests`, `Konten.Tests/ProfilUebernahmeTests`, `Api.Tests` (`FamilieTests`, `FamilienEinladungTests`, `ProfilUebergabeTests`, `FamilienHelfer`, Endpunktliste, Demo-Daten, Sicherheitsprotokoll), `Ablaeufe.Tests/ProfilUebergabeAngenommenTests`, `Ui.Tests/FamilieSeitenTests`, E2E-Screenshots, `Testing` (`MailOrdner`, `PostgresContainer`).
- `doku/` – Seiten und Screenshots.

**Review:** 33 Befunde (high 0, medium 4, low 20, false 9). Gepatcht (7 Einträge): medium 1 – Volljährigkeit beim Übernehmen erneut geprüft; low 6 – Sperre je Person beim Annehmen und Übernehmen, Schutz vor doppeltem „Ja, übergeben“, Doku-Hinweis zum späteren Geburtsdatum, Tests für Sitzungen ohne Anmelde-Zeitpunkt, für ausgeblendete abgelaufene Einladungen/Übergaben und für „Token für X wirkt nicht auf Y“. Zurückgestellt: Org-Rollen beim Übernehmen (Spine-Änderung nötig, siehe `deferred`). Verworfen mit Begründung im Review Triage Log: Demo nach echter Übernahme, kurzes Sitzungsfenster nach dem Übernehmen, Protokoll beim Auslösen, schlichtes 401, fehlende Fremdschlüssel, Einladung an eigene Mail, unvollständiges Datum, Doku-Ort der Kind-Seite sowie alle `false`-Befunde.

**AD-30 (Hinweis des Aufrufers):** Jeder neue Endpunkt hat HTTP-Tests gegen echtes PostgreSQL. Persönlich: Elternteil, zweiter Elternteil, Elternteil einer anderen Familie und Person derselben Organisation (fremdes Kind → 404, nur Vertragsfelder). Token-Fälle für Einladung und Übergabe: abgelaufen, zweites Annehmen, verworfen, Org-Einladungs-, Login- und jeweils anderes Familien-Token, unbekannt, Formfehler, kein Token, andere Mail → 404 mit Protokolleintrag; Übergabe für X wirkt nicht auf Y. Ohne Sitzung → 401 (alle Endpunkte), ohne frische Anmeldung → 401. „Falsche Rolle 403“ und „fremde Organisation 404“ haben keinen Gegenstand, weil kein neuer Endpunkt einen Organisations- oder Freizeit-Kontext hat.

**Folge-Review empfohlen:** nein – gepatcht: high 0, medium 1, low 6.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 1023 von 1023 grün (Architektur, SharedKernel, Konten, Personen, Organisationen, Freizeiten, Abläufe, API, Ui, E2E inklusive Doku-Screenshots).
- `npm ci` und `npm run build` in `doku/`: Exit-Code 0, 17 Seiten, `eltern/familienkonto/` und `eltern/profil-uebergeben/` gebaut.
- Jede Zeile der I/O-Matrix hat einen gelaufenen Test (Matrix-Audit).

**Restrisiken:**
- Siehe `deferred`: Org-Rollen einer annehmenden Person ohne Namen gehen beim Übernehmen verloren.
- Familien-Einladungen und Profil-Übergaben (mit Mail-Adresse) werden bis zum Lösch-Dienst (Story 1-7) nicht gelöscht, wie die Einladungen aus 1-2/1-3.
- Zwischen Übernehmen und Zustellung des Ereignisses (Takt 2 Sekunden) gilt die alte Sitzung noch; ein Neuladen in diesem Fenster kann ein leeres, verwaistes Profil anlegen.
- Erneute Anmeldung nach 401 verliert die eingetippte Mail-Adresse der Übergabe; die Doku beschreibt den Weg.
- Die Screenshots laufen gegen die echte Uhr.
