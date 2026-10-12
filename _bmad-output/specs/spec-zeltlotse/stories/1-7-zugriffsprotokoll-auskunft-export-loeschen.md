---
title: 'Zugriffsprotokoll, Auskunft, Export, Löschen'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: 'b8e7e0c9fd10d23fdacaaa89925f544f286e8b13'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/1-6-warnung-und-erinnerung-bei-nachweisen.md'
warnings: [oversized]
deferred:
  - summary: >-
      Einladungen an die Mail-Adresse einer gelöschten Person behalten die Adresse bis 30 Tage nach ihrem Ablauf.
    evidence: |-
      PersonGeloescht trägt nur die PersonId; OrgPersonLoeschen, FreizeitPersonLoeschen und PersonLoeschen ersetzen nur „eingeladen von“. Org-, Leitungs- und Familien-Einladungen an die Mail der Person (auch angenommene) löscht erst der Lösch-Lauf. Lösung: Mail im Empfänger über IKontoLesen nachschlagen ist nach dem Löschen des Kontos nicht mehr möglich; braucht eine eigene Entscheidung (z. B. Einladung beim Annehmen an die PersonId binden).
    location: >-
      src/Modules/Organisationen/Zeltlotse.Organisationen/Datenschutz/OrganisationenDatenschutz.cs
    severity: medium
  - summary: >-
      Die Datenauskunft nennt keine Einladungen an die eigene Mail, keine Login-Anfragen und keine selbst gestarteten Profil-Übergaben.
    evidence: |-
      KontenAuskunft, OrganisationenAuskunft, FreizeitenAuskunft und PersonenAuskunft lesen nur Zeilen mit PersonId; Einladungen und Login-Anfragen hängen an der Mail. Die Architekturregel prüft nur, dass es eine Auskunft gibt, nicht deren Vollständigkeit.
    location: >-
      src/Modules/*/Datenschutz/*Auskunft*
    severity: medium
  - summary: >-
      Outbox- und Inbox-Tabellen behalten die PersonId in zugestellten Ereignissen (auch in PersonGeloescht selbst).
    evidence: |-
      ModulDbContext.OutboxSchreibenUndSpeichernAsync schreibt DatensatzId und Inhalt mit Kennungen; zugestellte Zeilen werden nirgends gelöscht (schon vor 1-7). Braucht eine feste Frist für zugestellte Outbox-Zeilen im Lösch-Lauf.
    location: >-
      src/Zeltlotse.SharedKernel/Daten/ModulDbContext.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Personen können nicht sehen, wer Einsicht in ihre Daten hatte, keine Auskunft und keinen Export abrufen und ihr Konto nicht löschen (FR-13, FR-14). Kein Modul löscht bisher irgendetwas: es gibt weder `PersonGeloescht` noch den Vertrag *Datenauskunft* noch den täglichen Lösch-Dienst mit Org-Fristen (AD-16, NFR-5). Alte Login-Anfragen und Einladungen wachsen unbegrenzt (zurückgestellt aus 1-1, 1-2, 1-3).

**Approach:**
- Personen bekommt ein Einsicht-Protokoll (AD-8) und den Endpunkt dafür. Das Protokoll wird heute beim Prüfen von Nachweisen geschrieben und ist so gebaut, dass Freizeitpass-Zugriffe (V2) später nur anhängen.
- `SharedKernel` bekommt zwei Verträge: `IDatenauskunft` (jedes Modul mit Personenbezug) und `ILoeschlauf` (jedes Modul mit Fristen). Der API-Host setzt daraus die Auskunft zusammen und führt den täglichen Lösch-Dienst aus.
- Konto löschen ist ein Command in Personen. Es löscht die personeneigenen Daten sofort und vernichtet den Personen-Schlüssel. Danach entsteht `PersonGeloescht`, und Konten, Organisationen und Freizeiten wenden ihre Lösch-Klasse an.
- Organisationen bekommt eine Löschfristen-Richtlinie mit Vorgabewerten. Der Org-Admin pflegt sie per Autosave.
- In der Web-App gibt es eine Unterseite „Zugriffsprotokoll und Meine Daten“ unter `konto/daten` und eine Card „Löschfristen“ auf der Organisationsseite. Doku, Demo-Daten und Screenshots werden entsprechend erweitert.

## Boundaries & Constraints

**Always:**

- **Einsicht-Protokoll (Personen, AD-8).**
  - Entität `EinsichtProtokollEintrag`: `Id`, `BetroffenePersonId`, `LesendePersonId`, `OrganisationId?`, `FreizeitId?`, `Zweck` (Enum `EinsichtZweck { NachweiseEingesehen = 1 }`, später additiv erweitert), `Am` (timestamptz) und `Tag` (date, Europe/Berlin).
  - Die Entität ist nicht organisationseigen, denn sie gehört der betroffenen Person.
  - Eindeutiger Index auf (`LesendePersonId`, `BetroffenePersonId`, `Tag`, `Zweck`): höchstens ein Eintrag pro Tag. Ein Konflikt gilt als Erfolg (`ON CONFLICT DO NOTHING` oder ein abgefangener Unique-Fehler).
  - Geschrieben wird über einen internen Command `EinsichtProtokollieren`. Er ist kein `IBenutzerCommand`, weil er eine Lese-Folge ist und auch nach Abschluss laufen muss.
  - Auslöser sind diese beiden Endpunkte, jeweils für die betroffene Person der Einsicht (`OrganisationId` und `FreizeitId` aus der Einsicht):
    - Detail einer Einsicht für die prüfende Person, `GET …/personen/nachweise/einsichten/{einsichtId}` (Freizeit- und Org-Variante in `PruefEndpunkte.cs`);
    - deren Datei-Abruf.
  - Fehlgeschlagene Rechteprüfungen schreiben nichts.
- **Endpunkte.** Persönliche Endpunkte stehen mit `.Organisationsuebergreifend()` in der Liste von `EndpunktListeTests`; Export und Löschen zusätzlich in deren Liste für erneute Anmeldung.

  | # | Endpunkt | Zugang | Ergebnis |
  |---|---|---|---|
  | 1 | `GET /api/ich/zugriffsprotokoll` (Personen) | angemeldet | `200`. Einträge der Person selbst und der Kinder in ihrer Familie, neueste zuerst. Felder: `Am`, `BetroffenePersonId`, `BetroffenerName`, `LesendePersonId`, `LesenderName` (Name, bei Platzhalter `null`), `OrganisationId?`, `FreizeitId?`, `Zweck`. Fremde Einträge sind nie enthalten. |
  | 2 | `GET /api/ich/daten` (API-Host, Ordner `Betrieb/` der Api.Contracts) | angemeldet + `.ErneuteAnmeldung()` | `200 DatenauskunftAntwort(ErstelltAm, Abschnitte[Titel, Zeilen[Feld, Wert]])`, nur die eigene Person. Schreibt `SicherheitsEreignisArt.Export` ins Sicherheitsprotokoll. Ist die Anmeldung älter als 10 Minuten, kommt `401`. |
  | 3 | `POST /api/ich/profil/loeschen` (Personen) | angemeldet + `.ErneuteAnmeldung()` + Antiforgery | `204`. Meldet die Sitzung ab (Cookie gelöscht). Schreibt `SicherheitsEreignisArt.Loeschung`. |
  | 4 | `GET /api/organisationen/{orgId}/loeschfristen` | `.NurOrgRolle(OrgAdmin)` | `200 LoeschfristenAntwort(Version, FreizeitDatenMonate, NachweisBestaetigungMonate)`. Org-Überblick bekommt `403`, eine fremde Organisation `404`. |
  | 5 | `PATCH /api/organisationen/{orgId}/loeschfristen` | wie 4 | AD-19 mit Enum `LoeschfristFeld {FreizeitDatenMonate, NachweisBestaetigungMonate}`, Werte 1–120. Ungültige Werte geben `400` am Feld, ein Konflikt `409`. Aktionsschlüssel `loeschfristen-verwalten` (additiv in `Gemeinsam/`). |

- **Datenauskunft.**
  - Vertrag: `IDatenauskunft.AuskunftAsync(Guid personId, CancellationToken) → IReadOnlyList<AuskunftAbschnitt(Titel, Zeilen[Feld, Wert])>` in `SharedKernel/Datenschutz/`.
  - Umgesetzt wird er in jedem Modul mit Personenbezug, lesend über den `LeseDbContext` und mit `IgnoreQueryFilters()` nur mit Filter auf die `PersonId` (Ausnahme laut AD-6). Die Abschnitte stehen in der Reihenfolge, in der die Module registriert werden.
  - Inhalt je Modul (Werte deutsch formatiert über dd.MM.yyyy):
    - Konten: „Konto“ mit Mail-Adresse, angelegt am und Passwort festgelegt ja/nein.
    - Personen:
      - „Profil“;
      - „Familie“ mit Elternteilen und Kindern;
      - „Nachweise“ mit Art, Ausstellungsdatum und hochgeladen am;
      - „Einsicht-Anfragen“ mit Datum, Status, prüfender Person und entschlüsseltem Hinweis;
      - „Erinnerungen“;
      - „Zugriffsprotokoll“.
    - Organisationen: „Rollen in Organisationen“ und „Nachweis-Bestätigungen“, jeweils mit dem Namen der Organisation.
    - Freizeiten: „Mitarbeit in Freizeiten“ mit Name der Freizeit und Rolle.
    - API-Host: „Sicherheitsprotokoll“ mit Art und Zeitpunkt.
  - Leere Abschnitte entfallen.
  - Migration in `betrieb`: Index auf `PersonId` im Sicherheitsprotokoll.
- **Konto löschen (Command `PersonLoeschen` in Personen, Lösch-Klassen nach AD-16).**
  - `Person` erbt künftig von `Aggregat` (Migration für `Folgenummer`). Das Ereignis `PersonGeloescht(Guid PersonId)` steht in `Personen.Contracts.Events`. Die Outbox-Stufe sammelt auch Aggregate im Zustand `Deleted` ein; das ist zu prüfen und bei Bedarf zu ergänzen.
  - Für die Person selbst löscht Personen in derselben Transaktion:
    - Person;
    - Personen-Schlüssel (Crypto-Shredding);
    - Nachweise samt Dateien (`IGeschuetzteDateien.LoeschenAsync`);
    - eigene Nachweis-Einsichten mit Posten;
    - Erinnerungen, die sie betreffen;
    - eigene Profil-Übergaben;
    - Kopie der Freizeit-Rollen;
    - Protokolleinträge, die sie betreffen;
    - ihre Elternteil-Zeilen.
  - Bezüge in Daten anderer werden durch den Platzhalter `GeloeschtePerson.Id` ersetzt. Das ist eine feste Guid in `SharedKernel`. Gemeint sind `PruefendePersonId` und `AngefragtVon` fremder Einsichten, `von` an Erinnerungen und `LesendePersonId` im Protokoll.
  - **Kinder:** Ist die Person einziger Elternteil, werden alle Kinderprofile der Familie auf dieselbe Weise gelöscht, je mit eigenem `PersonGeloescht`. Anschließend werden Familie und ihre offenen Einladungen gelöscht. Gibt es einen zweiten Elternteil, bleiben die Kinder bei ihm.
  - Empfänger von `PersonGeloescht`, je ein Command mit `IgnoreQueryFilters()` gefiltert auf die `PersonId`:
    - Konten löscht Konto und Login-Anfragen zur Mail. Alle Sitzungen enden dadurch über `KontoPruefung`.
    - Organisationen löscht Org-Rollen und die Nachweis-Bestätigungen der Person und ersetzt `GeprueftVon` durch den Platzhalter.
    - Freizeiten löscht die Freizeit-Rollen der Person. Es gibt dafür kein `FreizeitRolleGeaendert`, weil Personen seine Kopie schon gelöscht hat.
  - Das Sicherheitsprotokoll bleibt: Es enthält nur Kennungen und hat eine feste Frist von 365 Tagen.
  - Eine spätere Anmeldung mit derselben Mail beginnt mit einem neuen, leeren Konto und Profil.
- **Löschfristen (Organisationen, NFR-5).**
  - Entität `Loeschfristen`: eine Zeile je Organisation, `OrganisationId` als Schlüssel, `FreizeitDatenMonate` (Vorgabe 12), `NachweisBestaetigungMonate` (Vorgabe 12) und `Version` (`xmin`), organisationseigen.
  - Die Zeile entsteht im Command „Organisation anlegen“. Die Migration ergänzt sie für bestehende Organisationen.
  - Das Bearbeiten folgt dem Muster der Nachweis-Richtlinie (`NachweisRichtlinie.cs`, Feld-Enum, AD-19).
- **Lösch-Dienst (API-Host).**
  - `LoeschDienst : BackgroundService` läuft täglich nach dem Muster von `SicherheitsprotokollFristDienst`. Die Logik steckt in einem testbaren Singleton `Loeschlauf.LaufAsync(ct)`.
  - `Loeschlauf` liest über neue lesende Contracts:
    - `Organisationen.Contracts`: Fristen aller Organisationen;
    - `Freizeiten.Contracts`: Freizeiten mit `OrganisationId` und Freizeitende laut AD-17.
  - Daraus baut er `LoeschAuftrag(Jetzt, FreizeitenMitAbgelaufenenDaten: Guid[], NachweisBestaetigungFristen: (OrganisationId, Monate)[])`. Eine Freizeit gehört hinein, wenn Freizeitende + `FreizeitDatenMonate` ≤ jetzt.
  - Anschließend ruft er jedes registrierte `ILoeschlauf` in eigenem DI-Scope auf. Ein Fehler in einem Modul wird geloggt (`[LoggerMessage]`, nur Kennungen) und hält die anderen Module nicht auf.
  - Jedes Modul setzt `ILoeschlauf` als interne Klasse um, die nur `ISender` nutzt und einen internen Command schickt. So gilt die Regel „nur Handler schreiben“ unverändert. Fristen je Modul:
    - Konten: Login-Anfragen, deren Ablauf mehr als 1 Tag zurückliegt.
    - Personen:
      - Familien-Einladungen und Profil-Übergaben, deren Ablauf mehr als 30 Tage zurückliegt;
      - Nachweis-Erinnerungen zu `FreizeitenMitAbgelaufenenDaten`.
    - Organisationen:
      - Org-Einladungen, deren Ablauf mehr als 30 Tage zurückliegt;
      - Nachweis-Bestätigungen mit `GueltigBis` + Org-Frist ≤ heute.
    - Freizeiten: Leitungs-Einladungen, deren Ablauf mehr als 30 Tage zurückliegt.
  - `ZeltlotseApiFactory` entfernt den `LoeschDienst` standardmäßig, so wie die anderen Hintergrunddienste.
- **Web-App.**
  - **Konto-Seite:** eine neue Card „Zugriffsprotokoll und Meine Daten“ nach „Nachweise“, mit dem Satz „Wer wann Einsicht hatte, Auskunft, Export und Konto löschen.“ und dem Knopf (Outline, Ansichtswechsel) „Meine Daten öffnen“ auf `konto/daten`.
  - **Seite `konto/daten`** (`Features/Konten/MeineDaten.razor`): h1 „Zugriffsprotokoll und Meine Daten“, darunter drei h2-Abschnitte.
    1. **„Zugriffsprotokoll“:** `Datentabelle` mit den Spalten „Wann“, „Wer“, „Organisation“, „Was“ („Nachweise eingesehen“) und „Betrifft“.
       - Organisationsnamen holt die Seite über `GET /api/ich/organisationen/namen`.
       - Fehlt ein Name, steht „Gelöschte Person“.
       - Ist das Protokoll leer, erscheint `LeerZustand` „Bisher hatte niemand Einsicht.“
    2. **„Auskunft und Export“:** Satz „Für Auskunft und Export ist eine frische Anmeldung nötig (höchstens 10 Minuten alt); sonst führt Zeltlotse zuerst zur Anmeldung.“ und Knopf „Auskunft anzeigen“.
       - Ein `401` führt über den bestehenden Rückweg zur Anmeldung und zurück auf `konto/daten`.
       - Danach erscheint je Abschnitt ein h3 mit einer Tabelle aus Feld und Wert.
       - Darunter stehen die Knöpfe „Als PDF speichern“ (Druck über den Browser, Druck-CSS blendet App-Leiste, Navigation und Knöpfe aus) und „Als CSV herunterladen“.
       - Die CSV erzeugt `Zeltlotse.Ui` aus der geladenen Auskunft (`AuskunftCsv`): Spalten `Bereich;Feld;Wert`, UTF-8 mit BOM, Werte in Anführungszeichen mit verdoppelten `"`. Werte, die mit `= + - @` beginnen, bekommen ein `'` davor. Dateiname `zeltlotse-meine-daten-JJJJ-MM-TT.csv`.
       - Neue Methoden `DruckenAsync()` und `DateiSpeichernAsync(name, mime, inhalt)` in `IBrowser`, `JsBrowser` und `zeltlotse.js` (Blob, Objekt-URL, Link-Klick, CSP-konform). Das Test-Double wird ergänzt.
    3. **„Konto löschen“:** Satz „Löscht Konto, Profil, Nachweise und Zugriffsprotokoll endgültig. Profile von Kindern ohne zweiten Elternteil werden mitgelöscht. Bestätigungen, die Organisationen behalten, bleiben ohne Namen.“
       - `InlineBestaetigung`: Auslöser „Konto löschen“, Satz „Konto und alle eigenen Daten endgültig löschen?“, Ja-Text „Ja, endgültig löschen“.
       - Nach `204` meldet die Seite die Sitzung ab und zeigt `Meldung` Erfolg „Gelöscht: Das Konto ist gelöscht.“
       - Bei einem Fehler erscheint der bestehende Fehlertext.
  - **Organisationsseite:** Card „Löschfristen“ unter der Richtlinie für Nachweise, nur mit Aktionsschlüssel. Sie enthält zwei `AutosaveFeld` (Zahl):
    - „Daten einer Freizeit (Monate nach Freizeitende)“
    - „Abgelaufene Nachweis-Bestätigungen (Monate nach Ablauf)“
  - API-Methoden werden in `PersonenApiClient`, `OrganisationenApiClient` und `BetriebApiClient` ergänzt.
- **Demo-Daten:** Miriam Zeltner hat die Nachweise von Jonas Hüttner eingesehen. Das entsteht als Protokolleintrag über den Command, idempotent, mit der Organisation „Jugendwerk Musterstadt“. Die Löschfristen haben Vorgabewerte. Die `DemoDatenTests`-Zähler werden angepasst.
- **Doku:**
  - Neue Seiten:
    - `erste-schritte/meine-daten.md`: Zugriffsprotokoll ansehen, Auskunft, PDF/CSV, Konto löschen, erneute Anmeldung.
    - `org-admin/loeschfristen.md`.
  - `daten.md` wird an die echte Oberfläche angepasst und um die Fristen ergänzt:
    - Login-Anfragen 1 Tag;
    - Einladungen und Übergaben 30 Tage nach Ablauf;
    - Löschfristen der Organisation mit Vorgabe 12 Monate;
    - Sicherheitsprotokoll 365 Tage;
    - was beim Löschen ohne Namen bleibt.
  - Die Index-Seiten der Rollen verlinken die neuen Seiten. Die neuen Seiten stehen in den Pflichtseiten von `DokuRegeln`.
- **Tests zuerst** nach AD-30. Jede Zeile der I/O-Matrix hat einen gelaufenen Test.

**Never:**

- Keine PDF-Bibliothek und kein serverseitiges PDF: Die PDF entsteht über Druck-CSS (Konvention „Druck“).
- Kein Schein-Löschen und kein „gelöscht“-Kennzeichen.
- Keine neuen Pfeile, keine neuen Zeilen im Ereignis-Fluss und keine neuen Problemcodes.
- Keine Aufbewahrungs-Frist für Belege und Vorfall-Notizen: Sie kommt mit dem ersten Datum ihrer Klasse.
- Keine Frist für personeneigene Daten und das Einsicht-Protokoll.
- Keine Löschung von Freizeit-Rollen per Frist.
- Keine Auskunft und kein Löschen für Kinder einzeln.
- Kein Freizeitpass-Protokoll (V2).
- Keine Pop-ups. Der Druckdialog des Browsers gilt nicht als App-Pop-up.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Protokoll schreiben | Miriam (prüfende Person) öffnet die Einsicht in Jonas' Nachweise zweimal am selben Tag und ruft eine Datei ab | genau 1 Eintrag (Jonas, Miriam, Org, Freizeit, `NachweiseEingesehen`) | Konflikt am Index = Erfolg |
| Protokoll nächster Tag | gleiche Einsicht am Folgetag (`FakeTimeProvider`) | 2. Eintrag | – |
| Protokoll fremd | eine nicht berechtigte Person ruft die Einsicht auf | `403`/`404` wie bisher, kein Eintrag | – |
| Protokoll lesen | Jonas `GET /api/ich/zugriffsprotokoll` | sein Eintrag mit Miriams Name | – |
| Protokoll Kind | Eintrag betrifft ein Kind von Katrin; Katrin und der zweite Elternteil lesen | Eintrag mit „Betrifft“ = Kind; Daniel (andere Familie) sieht ihn nicht | – |
| Auskunft frisch | Jonas angemeldet vor 2 Min., `GET /api/ich/daten` | `200` mit Abschnitten Konto, Profil, Nachweise, Rollen/Mitarbeit; Sicherheitsprotokoll `Export` | – |
| Auskunft alt | Anmeldung vor 11 Min. | `401` | Client → Anmeldung mit Rückweg |
| Auskunft anonym | ohne Cookie | `401` | – |
| Löschen frisch | Jonas mit Nachweisen, Org-Rolle, Leitung, Protokolleinträgen als Leser bei anderen | `204`, Cookie weg. Nach der Outbox: Person, Schlüssel, Nachweise und Dateien, Konto, Org-Rolle, Freizeit-Rollen und eigene Bestätigungen gelöscht; fremde Einträge zeigen den Platzhalter; Sicherheitsprotokoll `Loeschung` | – |
| Löschen alt / ohne Antiforgery | Anmeldung vor 11 Min. bzw. ohne Header | `401` bzw. `400`, nichts gelöscht | – |
| Löschen Elternteil allein | Katrin ohne zweiten Elternteil | Kinder Paul und Emma gelöscht, Familie gelöscht | – |
| Löschen mit zweitem Elternteil | Familie mit zwei Eltern, einer löscht | Kinder und Familie bleiben beim anderen | – |
| Nach Löschen | alte Sitzung `GET /api/ich/konto`; neue Anmeldung mit derselben Mail | `401`; neues leeres Profil | – |
| Fristen lesen/ändern | Org-Admin GET/PATCH | `200`, neue Version | `409` bei Konflikt am selben Feld |
| Fristen falsch | Wert 0 oder 121 | `400` am Feld | – |
| Fristen Rollen | Org-Überblick; Org-Admin einer fremden Org | `403`; `404` | – |
| Lauf Login-Anfragen | Ablauf vor 25 h bzw. vor 23 h | erste gelöscht, zweite bleibt | – |
| Lauf Einladungen | Org-, Leitungs- und Familien-Einladung sowie Übergabe mit Ablauf vor 31 bzw. 29 Tagen | 31 gelöscht, 29 bleibt | – |
| Lauf Freizeitdaten | Erinnerung zu einer Freizeit, Ende vor 13 Mon. (Org A, Frist 12) und vor 2 Mon. (Org B, Frist 1) bzw. vor 11 Mon. (Org A) | beide ersten gelöscht, dritte bleibt | – |
| Lauf Bestätigungen | `GueltigBis` vor 13 bzw. 11 Monaten, Frist 12 | erste gelöscht, zweite bleibt | – |
| Lauf Fehler | ein `ILoeschlauf` wirft | andere Module laufen trotzdem, Fehler geloggt | – |

</intent-contract>

## Code Map

- `src/Modules/Personen/Zeltlotse.Personen/PersonenModul.cs:48-76` -- Endpunkt-Gruppen `/ich/profil` usw.; Muster `.ErneuteAnmeldung()` (Z. 60); Registrierung (`VerschicktMail`, Z. 35).
- `src/Modules/Personen/Zeltlotse.Personen/Profil/Person.cs:11` -- `Person` ist noch kein `Aggregat` → erben lassen.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/NachweisEinsicht.cs:42-53` -- `OrganisationId`, `FreizeitId?`, `AngefragtVon?`, `PruefendePersonId?` (Quelle für Protokoll und Platzhalter).
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/PruefEndpunkte.cs:42-53,104ff` -- `Detail` und `Datei` (Freizeit- und Org-Variante) → `EinsichtProtokollieren` senden.
- `src/Modules/Personen/Zeltlotse.Personen/Nachweise/EinsichtFreigeben.cs`, `NachweisErinnerung.cs` -- Muster für Command-Slice, Ergebnis-Hierarchie und Handler.
- `src/Modules/Personen/Zeltlotse.Personen/Daten/PersonenDbContext.cs` -- DbSets und Modell; neue Tabelle `einsicht_protokoll`; Migration `PersonenDbContext`.
- `src/Modules/Personen/Zeltlotse.Personen/Schluessel/PersonenSchluessel*.cs` -- Schlüssel; Vernichten nur im Handler (Schreib-Kontext).
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Events/` -- `PersonGeloescht` hier anlegen (Muster `NachweisEingesehen`).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/NachweisRichtlinie.cs:13-93` -- Muster für Richtlinie, Vorgaben, Feld ändern; `NachweisBestaetigung` (Z. 93).
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/NachweisCommands.cs:92`, `OrganisationenModul.cs:30` -- Empfänger-Muster `EmpfaengtEreignis<,>`.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/Nachweise/NachweisEndpunkte.cs:149` -- `OrganisationsNamenEndpunkt` (Namen für den Client).
- `src/Modules/Konten/Zeltlotse.Konten/Domaene/Konto.cs`, `Sitzungen/SitzungAusstellen.cs`, `Sitzungen/KontoPruefung.cs`, `Profil/KontoMitProfilVerbinden.cs` -- Konto, Abmelden und Empfänger-Muster in Konten.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Domaene/FreizeitRolle.cs:20` -- Freizeit-Rolle; `Freizeiten.Contracts` liefert Freizeitende (AD-17).
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContext.cs:11,33-40` -- Outbox-Stufe (`Entries<Aggregat>()`, `Deleted` prüfen); Kommentar „Lösch-Dienst folgt“ aktualisieren.
- `src/Zeltlotse.SharedKernel/Daten/ModulDbContextBasis.cs:58-61` -- Mandantenfilter; Umgehung nur per `IgnoreQueryFilters()` in Auskunft und Löschen.
- `src/Zeltlotse.SharedKernel/Sicherheitsprotokoll/ISicherheitsprotokoll.cs` -- `Export = 5`, `Loeschung = 6` vorhanden.
- `src/Zeltlotse.SharedKernel/Anmeldung/{ErneuteAnmeldung,Sitzung}.cs`, `src/Zeltlotse.Api/Rechte/RechtePruefung.cs:46,91-97` -- Frische 10 Min. → `401`.
- `src/Zeltlotse.SharedKernel/Dateien/` -- `IGeschuetzteDateien.LoeschenAsync(praefix, kennung)`.
- `src/Zeltlotse.Api/Betrieb/{SicherheitsprotokollFristDienst,BetriebRegistrierung}.cs` -- Vorlage für den Lösch-Dienst (PeriodicTimer, `TimeProvider`, LoggerMessage).
- `src/Zeltlotse.Api/Demo/{DemoDaten,DemoKatalog}.cs` -- Demo-Seed über Commands, idempotent.
- `src/Zeltlotse.Api.Contracts/{Personen,Organisationen,Betrieb,Gemeinsam}/` -- DTOs (string-Enums wie `Personen/Nachweise.cs`).
- `src/Zeltlotse.App/Features/Konten/Konto.razor:50-55` -- Card einfügen; `Features/Personen/NachweiseAnsicht.razor:66` (Muster `InlineBestaetigung`).
- `src/Zeltlotse.App/Browser/{IBrowser,JsBrowser}.cs`, `wwwroot/js/zeltlotse.js`, `src/Zeltlotse.App/Sitzung/AppSitzung.cs:60` -- JS-Schnittstelle; Rückweg bei `401`.
- `src/Zeltlotse.Ui/Api/*ApiClient.cs`, `UiRegistrierung.cs:20-25`, `Texte/Anzeige.cs` -- Clients und Datumsformat.
- `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs:118-128` -- Hintergrunddienste entfernen; `Uhr`, `OutboxJetztZustellenAsync`.
- `tests/Zeltlotse.Api.Tests/{AnmeldeHelfer,OrgHelfer,FreizeitHelfer,FamilienHelfer,NachweisHelfer}.cs`, `ProfilUebergabeTests.cs:98-160` (Frische mit `Uhr.Advance`), `EndpunktListeTests.cs:34-52,124,192`, `SicherheitsprotokollTests.cs`.
- `tests/Zeltlotse.Ablaeufe.Tests/NachweisEingesehenTests.cs` -- Muster Durchlauf-Test.
- `tests/Zeltlotse.ArchitectureTests/{AbhaengigkeitsRegeln.cs:24-117,EreignisFlussTests.cs,DokuRegeln.cs}` -- Regeln; Kommentar Z. 24 aktualisieren.
- `tests/Zeltlotse.Ui.Tests/{AnmeldeSeitenTests.cs:275-380,NachgebildeterBrowser.cs,NachweisWarnungSeitenTests.cs:30-45}` -- bUnit-Muster.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` (`Konto` ~106, `Nachweis_Erinnern` ~471), `E2EHilfe.cs` -- Screenshot-Muster.
- `doku/src/content/docs/{daten.md,erste-schritte/,org-admin/,eltern/index.md,mitarbeitende/index.md}` -- Doku.

## Tasks & Acceptance

**Execution:**

1. **Tests ZUERST (rot):**
   - `tests/Zeltlotse.Api.Tests/ZugriffsprotokollTests.cs` -- Zeilen 1–5 der Matrix, plus anonym → `401`.
   - `tests/Zeltlotse.Api.Tests/DatenauskunftTests.cs` -- frisch, alt und anonym. Prüft, dass keine fremden Daten enthalten sind und dass `Export` protokolliert wird.
   - `tests/Zeltlotse.Api.Tests/KontoLoeschenTests.cs` -- alle Matrixzeilen „Löschen“ und „Nach Löschen“, mit S3-Container für die Dateien.
   - `tests/Zeltlotse.Api.Tests/LoeschfristenTests.cs` -- Vorgaben beim Anlegen einer Organisation, GET/PATCH, `400`, `409`, `403`, `404`.
   - `tests/Zeltlotse.Api.Tests/LoeschlaufTests.cs` -- alle Matrixzeilen „Lauf …“ mit `FakeTimeProvider`. Ruft `Loeschlauf.LaufAsync` direkt auf. Ein werfendes Test-`ILoeschlauf` wird über die Factory-Dienste registriert.
   - `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- neue Endpunkte und die Liste für erneute Anmeldung.
   - `tests/Zeltlotse.Ablaeufe.Tests/PersonGeloeschtTests.cs` -- Durchlauf über alle drei Empfänger mit sofort abgearbeiteter Outbox und Inbox-Zählung.
   - `tests/Zeltlotse.ArchitectureTests/` -- neue Regel nach AD-16:
     - Jedes Modul, dessen Entitäten eine Eigenschaft `PersonId` tragen, enthält eine Umsetzung von `IDatenauskunft`.
     - Jedes solche Modul außer Personen meldet einen Empfänger für `PersonGeloescht` an.
     - Dazu Negativ-Fixture nach dem bestehenden Muster.
   - `tests/Zeltlotse.Ui.Tests/MeineDatenSeitenTests.cs` -- Card auf der Konto-Seite, Protokoll-Tabelle, Leer-Zustand, Auskunft mit Abschnitten, CSV (BOM, Quoting, Formel-Schutz), Druck, Löschen mit Inline-Bestätigung → POST und Meldung, Löschfristen-Card.
2. **SharedKernel:** `src/Zeltlotse.SharedKernel/Datenschutz/{IDatenauskunft,ILoeschlauf,LoeschAuftrag,GeloeschtePerson}.cs` -- Verträge und Platzhalter.
3. **Personen:**
   - `src/Modules/Personen/...` -- Protokoll-Entität, Command, Endpunkt 1, Aufruf in `PruefEndpunkte`;
   - `PersonLoeschen` mit Endpunkt 3;
   - `Person : Aggregat`;
   - `PersonGeloescht`;
   - Auskunft;
   - Lösch-Lauf;
   - Migration.
4. **Konten, Organisationen, Freizeiten:**
   - Empfänger von `PersonGeloescht`, Auskunft und Lösch-Lauf.
   - In Organisationen zusätzlich `Loeschfristen` (Entität, Anlegen, Endpunkte 4/5) und Contracts zum Lesen der Fristen.
   - In Freizeiten Contracts zum Lesen von Freizeitende und Organisation.
   - Migrationen.
5. **API-Host:**
   - `src/Zeltlotse.Api/Datenschutz/` -- Endpunkt 2, `Loeschlauf`, `LoeschDienst`, Auskunft für das Sicherheitsprotokoll;
   - Registrierung;
   - Migration Index `betrieb`;
   - Kommentar in `ModulDbContext`.
6. **Contracts und Clients:** `src/Zeltlotse.Api.Contracts`, `src/Zeltlotse.Ui` -- DTOs, Aktionsschlüssel, Client-Methoden, `AuskunftCsv`.
7. **Web-App:** `src/Zeltlotse.App` -- `MeineDaten.razor`, Card auf der Konto-Seite, Card „Löschfristen“, `IBrowser`/`JsBrowser`/`zeltlotse.js`, Druck-CSS. Neue Stile entstehen in `Zeltlotse.Ui.Komponenten`.
8. **Testinfrastruktur:** `tests/Zeltlotse.Testing/ZeltlotseApiFactory.cs` -- den `LoeschDienst` entfernen.
9. **Demo-Daten:** `src/Zeltlotse.Api/Demo/` -- Protokolleintrag Miriam → Jonas. `DemoDatenTests` anpassen.
10. **Screenshots:** `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- `Meine_Daten` (Bilder `meine-daten-zugriffsprotokoll` und `meine-daten-auskunft`, Jonas), `Konto_Loeschen` (Inline-Bestätigung offen, NICHT bestätigen) und `Loeschfristen` (Andrea). Je Handy und Laptop mit axe.
11. **Doku:** `doku/src/content/docs/` -- neue Seiten, `daten.md`, Index-Seiten; `DokuRegeln`-Pflichtseiten.

**Acceptance Criteria:**

- Given der Stand nach der Umsetzung, when `dotnet test` läuft, then sind alle Tests grün, inklusive Architektur-, Durchlauf-, bUnit- und E2E-Tests, und die neuen Screenshots sind geschrieben.
- Given die Demo-Daten und Jonas mit frischer Anmeldung, when er Konto → „Meine Daten öffnen“ wählt, then sieht er Miriams Einsicht im Zugriffsprotokoll und nach „Auskunft anzeigen“ seine Daten mit den Knöpfen für PDF und CSV.
- Given eine Person mit Konto, Nachweisen und Rollen, when sie ihr Konto löscht und die Outbox zugestellt ist, then enthält keine Tabelle eines Moduls mehr ihre `PersonId`, außer dem Sicherheitsprotokoll. Bezüge in fremden Daten tragen den Platzhalter.
- Given der Doku-Stand, when `npm run build` in `doku/` läuft, then endet er ohne Fehler, und `erste-schritte/meine-daten/` und `org-admin/loeschfristen/` liegen in `doku/dist/`.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 35 findings — high 0, medium 7, low 19, false 9, maybe-false 0
- findings:
  - `[low]` `[reject]` (Intent) Zugriffsprotokoll protokolliert nur die Nachweis-Einsicht, nicht Status-Listen oder Rollenlisten — Listen liefern keine Inhalte. AD-8 protokolliert nur Inhaltszugriffe; „Vollständigkeit“ ohne Inhalte wird ausdrücklich nicht protokolliert. Weitere Zwecke kommen mit dem Freizeitpass (V2).
  - `[false]` `[reject]` (Intent) Die Frist „Daten einer Freizeit“ löscht heute nur Erinnerungen — kein Fehlverhalten: Das sind heute die einzigen Arbeitsdaten mit Personenbezug. Doku (`loeschfristen.md`, `daten.md`) nennt das offen.
  - `[low]` `[reject]` (Intent) Keine Auskunft und kein Export für Kinder — Eltern sehen die Daten der Kinder bereits im Familienkonto. Seltener Fall; die Lösung bräuchte einen neuen Parameter und neue Rechteprüfung.
  - `[low]` `[patch]` (Intent) Export im echten Browser ungetestet (JS-Download) — Playwright-Test `Meine_Daten` löst den CSV-Download aus und prüft den Dateinamen.
  - `[low]` `[reject]` (Intent) Hosted Service `LoeschDienst` selbst ungetestet — dasselbe Muster wie `SicherheitsprotokollFristDienst`; die Logik steckt in `Loeschlauf` und ist getestet.
  - `[false]` `[reject]` (Intent) Keine Token-Fälle — die Story fügt keinen Token- oder QR-Endpunkt hinzu; AD-30 verlangt Token-Fälle nur für diese Art.
  - `[false]` `[reject]` (Intent) Doku-Build und „Test zuerst“ nicht belegbar — Doku-Build lief in der Verifikation fehlerfrei (23 Seiten), `dotnet test` 1248/1248 grün.
  - `[low]` `[patch]` (Intent) `eltern/index.md` verspricht „bis wann die Freizeit Einsicht hat“ auf der verlinkten Seite — Satz umformuliert, der Teil „bis wann“ entfällt.
  - `[medium]` `[patch]` (Blind) Nachweis-Dateien werden vor dem Commit gelöscht — Löschen nach dem Commit im Endpunkt, je Datei best-effort mit Log nur über Kennungen.
  - `[medium]` `[defer]` (Blind) Einladungen an die Mail-Adresse der gelöschten Person behalten die Adresse bis 30 Tage nach Ablauf — `PersonGeloescht` trägt nur die `PersonId`; der Empfänger kennt die Mail nicht ohne neuen Lesepfad.
  - `[medium]` `[defer]` (Blind) Auskunft ohne Einladungen an die eigene Mail, Login-Anfragen und selbst gestartete Übergaben — real, aber Erweiterung mehrerer Module.
  - `[low]` `[reject]` (Blind) Keine Auskunft für Kinder — wie Intent-Befund oben.
  - `[false]` `[reject]` (Blind) Lösch-Dienst ohne Sperre gegen parallele Läufe — ein Prozess (AD-1, AD-23); Löschungen sind idempotent. Der Start-Lauf folgt dem Muster des Sicherheitsprotokoll-Dienstes.
  - `[low]` `[reject]` (Blind) Liste der abgelaufenen Freizeiten wächst täglich — Leistung erst bei großem Bestand; die Lösung bräuchte Markierungen (Speziallogik).
  - `[low]` `[reject]` (Blind) Verkürzte Frist löscht ohne Warnung — der Org-Admin ändert bewusst selbst; Doku nennt die Wirkung. Eine Warnung wäre neue UI-Logik.
  - `[low]` `[reject]` (Blind) Bestätigung nennt die Kinder nicht namentlich — der Satz über dem Knopf nennt das Mitlöschen. Der Text ist in der Spec festgelegt.
  - `[low]` `[reject]` (Blind) Tabelle zeigt keine Freizeit-Spalte — Spalten laut Spec; die Freizeit ist gespeichert und erscheint, sobald Freizeitpass-Zugriffe (V2) dazukommen.
  - `[low]` `[patch]` (Blind) CSV-Formelschutz ohne Tab/CR — `\t` und `\r` ergänzt, mit Testfällen.
  - `[false]` `[reject]` (Blind) `Version` in Löschfristen-PATCH ungenutzt — AD-19 entscheidet den Konflikt über den alten Wert desselben Felds; wie bei der Nachweis-Richtlinie.
  - `[low]` `[patch]` (Blind) Doku sagt, andere Geräte sind sofort abgemeldet — umformuliert auf „kurz danach“.
  - `[low]` `[patch]` (Edge) Dateien vor Commit gelöscht — gleiche Ursache wie der Blind-Befund; mit ihm behoben.
  - `[low]` `[patch]` (Edge) Speicherfehler mitten in der Datei-Schleife — gleiche Ursache; Löschen nach dem Commit, best-effort.
  - `[low]` `[reject]` (Edge) Konto ohne Profil lässt sich nicht löschen — die App legt das Profil direkt nach der Anmeldung an; seltener Fall, Lösung bräuchte einen eigenen Konten-Pfad.
  - `[low]` `[reject]` (Edge) Neue Anmeldung vor der Outbox-Zustellung — Fenster von Sekunden; `PersonGeloescht` löscht das Konto danach trotzdem (Restrisiko notiert).
  - `[low]` `[patch]` (Edge) `version` als Text im PATCH → 500 — Prüfung auf Zahl vor `TryGetUInt32`, Test ergänzt.
  - `[low]` `[patch]` (Edge) CSV-Formelschutz Tab/CR — gleiche Ursache wie der Blind-Befund.
  - `[false]` `[reject]` (Edge) Organisation fehlt in den Fristen — `LoeschfristenLesen` liefert für jede Organisation eine Zeile (Vorgabe, wenn die Zeile fehlt); Freizeiten ohne Organisation gibt es nicht.
  - `[false]` `[reject]` (Edge) `AnmeldungNoetig` beim Löschen ohne Rückmeldung — der Sitzungs-Handler leitet bei jedem `401` zur Anmeldung (bUnit-Test „Auskunft mit alter Anmeldung“ belegt den Weg).
  - `[low]` `[reject]` (Edge) Eigene Einsicht landet im eigenen Protokoll — sehr selten (Org-Admin prüft sich selbst) und harmlos.
  - `[false]` `[reject]` (Edge) `PersonId` null → 500 — angemeldete Sitzungen tragen immer den PersonId-Claim; ohne Sitzung greift `401` vorher.
  - `[false]` `[reject]` (Edge) `Version` nie verglichen — wie oben, AD-19.
  - `[medium]` `[defer]` (Edge) Outbox- und Inbox-Tabellen behalten die `PersonId` in zugestellten Ereignissen — besteht schon vorher: Es gibt keine Bereinigung zugestellter Outbox-Zeilen. Braucht eine eigene Frist für die Outbox.
  - `[medium]` `[patch]` (Verifikation) Platzhalter in Freizeiten/Organisationen ungetestet — Test „Andrea löscht ihr Konto“ prüft `AngelegtVon`/`EingeladenVon` und die Spaltensuche.
  - `[medium]` `[patch]` (Verifikation) Bestätigungs-Frist nicht getrennt getestet — Test mit zwei Organisationen und verschiedenen Fristen.
  - `[medium]` `[patch]` (Verifikation) Auskunft „Familie“, „Erinnerungen“ und Hinweis ungetestet — Test mit Elternteil, Kind, Erinnerung und abgelehntem Nachweis.

## Design Notes

- **PDF über Druck:** Die Spine verbietet PDF-Bibliotheken und druckt über Druck-CSS. Deshalb ist „Als PDF speichern“ der Druckdialog des Browsers mit dem Ziel „Als PDF speichern“. Die Doku erklärt das.
- **Ein Auskunft-Endpunkt:** CSV und PDF entstehen im Client aus derselben Auskunft. So braucht es nur einen Endpunkt mit erneuter Anmeldung, und die Protokollierung als Export ist eindeutig. Der Endpunkt liegt im API-Host, weil nur er alle `IDatenauskunft` kennt (AD-2: Kein Modul sammelt Daten anderer Module ein).
- **Erneute Anmeldung:** Das bestehende `401` führt über den Rückweg zur Anmeldung. Danach drückt die Person „Auskunft anzeigen“ bzw. „Konto löschen“ noch einmal. Ein eigener Problemcode würde die Spine ändern.
- **Lösch-Dienst ohne Regeländerung:** `ILoeschlauf`-Umsetzungen schicken nur einen internen Command. Damit bleibt der Schreib-Kontext bei den Handlern (AD-27). Die Auskunft liest nur über den `LeseDbContext`. Die Kommentare „folgen mit ihren Stories“ werden darauf angepasst.
- **Lösch-Klassen heute:**
  - personeneigen: Profil, Schlüssel, Nachweise, eigene Einsichten, Übergaben, Protokoll über die Person;
  - Arbeitsdaten mit Org-Frist: Nachweis-Erinnerungen;
  - Org-Frist ab Ablauf: Nachweis-Bestätigungen.
  - Eigene Bestätigungen löscht Organisationen beim Löschen der Person, weil sie ohne die Person nichts belegen. Ein Platzhalter würde zudem mit dem Schlüssel (Org, Person, Art) kollidieren.
  - Freizeit-Rollen werden nur beim Löschen der Person entfernt, nicht per Frist: Sie sind die Struktur der Freizeit, keine Arbeitsdaten.
- **Feste technische Fristen:** Login-Anfragen 1 Tag, Einladungen und Übergaben 30 Tage nach Ablauf. Das sind Zugangslinks ohne fachlichen Wert. Eine Org-Richtlinie wäre hier Speziallogik.
- **Kinder beim Löschen:** Kinderprofile ohne Elternteil hätten keinen Besitzer (AD-7: „Profil ohne Konto … wird gelöscht“). Deshalb werden sie mitgelöscht; die Inline-Bestätigung nennt das.
- **Restrisiken:**
  - Löscht sich der letzte Org-Admin oder die einzige Leitung, bleibt die Organisation bzw. Freizeit ohne sie. Der Global Admin kann die Organisation neu besetzen (1-2).
  - Crypto-Shredding in Backups (zurückgestellt aus 0-2) bleibt offen bis zur Hosting-Wahl.
- **Beispiel CSV:** `﻿"Bereich";"Feld";"Wert"` / `"Profil";"Vorname";"Jonas"` / `"Notiz";"Wert";"'=SUMME(1)"`.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- expected: 0 Warnungen, 0 Fehler
- `dotnet test` -- expected: alle Tests grün, Screenshots neu geschrieben
- `npm ci` und `npm run build` in `doku/` -- expected: Exit-Code 0, Seiten `erste-schritte/meine-daten/` und `org-admin/loeschfristen/` in `doku/dist/`

## Auto Run Result

Status: done

**Zusammenfassung:**
- Zugriffsprotokoll (AD-8) in Personen: Einträge beim Prüfen von Nachweisen, höchstens einer pro Tag. `GET /api/ich/zugriffsprotokoll` zeigt die Einträge für die Person und ihre Kinder.
- Datenauskunft (`IDatenauskunft`) in Konten, Personen, Organisationen, Freizeiten und dem Sicherheitsprotokoll. `GET /api/ich/daten` verlangt eine frische Anmeldung und schreibt `Export` ins Sicherheitsprotokoll.
- Konto löschen (`POST /api/ich/profil/loeschen`):
  - Personen löscht die personeneigenen Daten sofort und vernichtet den Schlüssel (Crypto-Shredding).
  - `PersonGeloescht` geht an Konten, Organisationen und Freizeiten. Bezüge in Daten anderer bekommen den Platzhalter `GeloeschtePerson.Id`.
  - Ist die Person einziger Elternteil, werden die Kinder mitgelöscht.
  - Die Dateien werden erst nach dem Commit gelöscht.
- Löschfristen-Richtlinie je Organisation (Vorgabe 12 Monate) mit GET/PATCH nach AD-19.
- Täglicher `LoeschDienst`/`Loeschlauf`: `ILoeschlauf` je Modul schickt nur einen internen Command. Feste technische Fristen für Login-Anfragen und Einladungen.
- Web-App:
  - Card und Seite `konto/daten`: Protokoll, Auskunft, PDF über den Druck, CSV, Konto löschen mit Inline-Bestätigung.
  - Card „Löschfristen“ auf der Organisationsseite.
- Doku:
  - neue Seiten `erste-schritte/meine-daten`, `org-admin/loeschfristen`;
  - `daten.md` mit den Fristen;
  - Rollenseiten verlinkt.
- Demo-Daten: Miriam → Jonas. Neue Screenshots je Handy und Laptop: Zugriffsprotokoll, Auskunft, Konto löschen, Löschfristen.

**Geänderte Dateien (Auswahl):**
- `src/Zeltlotse.SharedKernel/Datenschutz/*` – Verträge `IDatenauskunft`, `ILoeschlauf`, `LoeschAuftrag`, `TechnischeFristen`, `GeloeschtePerson`.
- `src/Modules/Personen/.../Zugriffsprotokoll/*`, `.../Datenschutz/*`, `Profil/Person.cs` (Aggregat), `Nachweise/PruefEndpunkte.cs` – Protokoll, Löschen, Auskunft, Lösch-Lauf.
- `src/Modules/{Konten,Organisationen,Freizeiten}/.../Datenschutz/*` – Empfänger von `PersonGeloescht`, Auskunft, Lösch-Lauf; Löschfristen samt Endpunkten und Contracts.
- `src/Zeltlotse.Api/Datenschutz/*` – Auskunft-Endpunkt, `Loeschlauf`, `LoeschDienst`; Migrationen in Personen, Organisationen, betrieb.
- `src/Zeltlotse.App/Features/Konten/MeineDaten.razor`, `Konto.razor`, `Organisationen/OrganisationAnsicht.razor`, `wwwroot/js/zeltlotse.js`, `Ui/Texte/AuskunftCsv.cs`, Druck-CSS.
- Tests:
  - Api: Zugriffsprotokoll, Datenauskunft, KontoLoeschen, Loeschfristen, Loeschlauf;
  - Durchlauf `PersonGeloescht`;
  - Architekturregel AD-16 mit Negativ-Fixture;
  - bUnit `MeineDatenSeitenTests`;
  - E2E-Screenshots mit CSV-Download.
- `doku/` – zwei neue Seiten, `daten.md`, Rollen-Indexseiten.

**Review:** 35 Befunde (high 0, medium 7, low 19, false 9).
- Korrigiert (patch): 4 medium und 5 low.
  - Medium:
    - Dateien erst nach dem Commit löschen;
    - Test für die Platzhalter in Freizeiten und Organisationen;
    - getrennter Test der Bestätigungs-Frist;
    - Test der Auskunft-Abschnitte Familie, Erinnerungen und Hinweis.
  - Low:
    - CSV-Schutz für Tab und CR;
    - `version` als Text → 400;
    - E2E-Test des CSV-Downloads;
    - zwei Doku-Formulierungen.
- Zurückgestellt (deferred): 3 medium, siehe Frontmatter `deferred`:
  - Mail-Adressen in Einladungen an gelöschte Personen;
  - Auskunft ohne mail-gebundene Daten;
  - Outbox behält Kennungen.
- Abgelehnt: alle anderen mit Begründung im Review Triage Log.

**Folge-Review empfohlen:** ja. Es wurden 4 medium-Befunde korrigiert, 0 high. Ungeprüftes Risiko: der Lösch-Ablauf über Modulgrenzen. Gemeint sind die Dateien, die jetzt nach dem Commit best-effort gelöscht werden, und das Fenster zwischen dem Löschen in Personen und der Outbox-Zustellung. Ein frischer Blick auf diesen Ablauf lohnt sich.

**AD-30:**
- Löschfristen: berechtigt, 403 für Org-Überblick, 404 für eine fremde Organisation.
- Persönliche Endpunkte (`/api/ich/...`): die Person selbst und Elternteile; fremde Einträge sind nie enthalten.
- Keine Token-Endpunkte in dieser Story.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Warnungen, 0 Fehler.
- `dotnet test`: 1253/1253 grün, inklusive Architektur-, Durchlauf-, bUnit- und E2E-Tests mit axe.
- `npm ci` und `npm run build` in `doku/`: 23 Seiten, `erste-schritte/meine-daten/` und `org-admin/loeschfristen/` liegen in `dist/`.
- Matrix-Audit: Jede Zeile der I/O-Matrix hat einen gelaufenen, grünen Test.
- `app-offline-*`-Screenshots waren nur Rendering-Rauschen und sind zurückgesetzt. `nachweis-richtlinie-laptop` hat sich echt geändert (neue Card darunter).

**Restrisiken:**
- Bis die Outbox zugestellt ist (Sekunden), bestehen Konto und andere Sitzungen noch.
- Löscht sich eine gebundene prüfende Person, bleibt eine offene Einsicht am Platzhalter hängen.
- Letzter Org-Admin oder einzige Leitung kann sich löschen.
- Crypto-Shredding in Backups bleibt offen bis zur Hosting-Wahl.
- Siehe auch `deferred`.
