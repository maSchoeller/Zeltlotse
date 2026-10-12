---
title: 'Anmeldung über Anmeldelinks, als Gast und mit Gast-Link'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: '4bc2089887888a5246f37d59f5587435f79fa77a'
review_loop_iteration: 0
followup_review_recommended: true
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-1-freizeitseite-und-vertrauensseite.md'
warnings: ['oversized']
deferred:
  - summary: >-
      Erneutes Absenden des Gast-Formulars (Neu laden der Antwortseite, Doppelklick, Wiederholung nach Fehler) legt eine zweite Gast-Familie und eine zweite Anmeldung für dasselbe reale Kind an.
    evidence: |-
      Jedes Absenden von „anmelden“ bzw. „weiteres-kind-absenden“ legt zuerst eine neue Person an (GastAnlegenHandler, GastKindAnlegenHandler); die Doppel-Prüfung in AnmeldeSchritte.AnlegenAsync greift nur bei gleicher PersonId. Die Antwortseite rendert direkt im POST (gewollt, damit der Gast-Link nicht im Verlauf steht), daher sendet der Browser beim Neu laden erneut. Der Hinweis „{Vorname} ist schon angemeldet“ ist auf dem Gast-Weg nur über eine bestehende PersonId erreichbar. Eine Lösung braucht eine Produktentscheidung: Abgleich von Kind und Elternteil (widerspricht „keine automatische Zusammenführung von Profilen“) oder ein serverseitiger Einmal-Schlüssel je Formular. Die Leitung sieht Doppelte und kann ab Story 2-3 absagen.
    location: >-
      src/Zeltlotse.Public/Seiten/Anmelden.razor
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Über die Anmeldelinks aus Story 2-1 lässt sich noch niemand anmelden. Es fehlen die Anmeldung als Gast ohne Konto, Geschwister nacheinander, die digitale Bestätigung der Teilnahmebedingungen, „Konto aus diesen Daten erstellen“, der Gast-Link aus der Eingangsmail und die Anmeldung mit vorhandenem Konto, auch für Mitarbeitende (CAP-8, FR-16 bis FR-19, AD-11).

**Approach:**
- Das Modul Freizeiten bekommt die Entität `Anmeldung` (Status „eingegangen“). Sie legt `AnmeldungEingegangen` ab, und Personen legt daraufhin eine `Freigabe` der Stufe Leitung an (Ereignis-Fluss der Spine).
- **Gast-Anmeldung (nur Teilnehmende, nur Kinder unter 18):** Die öffentliche Seite `/freizeit/{token}/anmelden` ist ein klassisches Blazor-Formular. Sie setzt zwei API-Aufrufe zusammen (AD-2, AD-5):
  1. Personen legt ein Gast-Profil an: Elternteil ohne Konto mit Mail, Familie, Kind und einen `GastZugang`. Dessen Token ist der Gast-Link.
  2. Freizeiten legt mit Anmeldelink-Token und Gast-Token die Anmeldung an und schickt die Eingangsmail mit dem Gast-Link.
- Der Gast-Link `/gast/{token}` zeigt den Anmeldestatus. „Konto aus diesen Daten erstellen“ führt in die App: Login, danach übernimmt das Konto das Gast-Profil (Muster Profil-Übergabe).
- **Mit Konto (Teilnehmende und Mitarbeitende):** Die App-Seite `/app/anmeldung/{token}` meldet die eigene Person oder ein Kind der Familie an. Die Familienübersicht zeigt den Anmeldestatus.

## Boundaries & Constraints

**Always:**
- Testgetrieben: Für jeden neuen Endpunkt entstehen zuerst HTTP-Tests gegen echtes PostgreSQL mit den Pflichtfällen nach AD-30:
  - **Token-Endpunkte:** gültig; abgelaufen oder erneuert → 404; Token mit anderem Zweck (z. B. Anmeldelink als Gast-Token, Leitungs-Einladung) → 404; Token für Freizeit X wirkt nicht auf Freizeit Y.
  - **Persönliche Endpunkte (`/api/ich/…`):** die Person selbst; Elternteil der Familie; fremde Person, auch aus derselben Organisation → 404; die Antwort enthält nur Übersichtsfelder.
  - **Freizeit-Endpunkte:** Leitung berechtigt; Team → 403; fremde Organisation → 404.
- Durchlauf-Test für `AnmeldungEingegangen` → Freigabe (Stufe Leitung) mit sofort abgearbeiteter Outbox und Idempotenz. Playwright-Ende-zu-Ende-Test der Gast-Anmeldung in Handy-Größe (375×812) mit axe nach WCAG 2.2 AA (AD-30).
- Tokens nach `Zugangslink`: 32 Zufallsbytes, gespeichert wird nur der Hash. Der Gast-Link hat den Zweck `gast-link`. Fehlgeschlagene Einlösungen per POST schreiben `LinkEinloesungFehlgeschlagen`. Reine Ansichten (GET) schreiben nichts.
- Gültigkeit des Gast-Links: Er gilt, solange eine `Freigabe` einer Person seiner Familie läuft. Vor der ersten Freigabe gilt er 1 Tag ab dem Anlegen. Nach der Übernahme durch ein Konto gilt er nicht mehr.
- Alle anonymen Endpunkte tragen `.AllowAnonymous().Zugriffsbegrenzt()`. Den Mandanten setzt der Anmeldelink (AD-6). Der Mandantenfilter wird nur umgangen beim token-gebundenen Laden des Anmeldelinks und beim Lesen der Anmeldungen der Personen eines gültigen Gast-Links (Design Notes).
- Personendaten (Name, Geburtsdatum, Mail) liegen nur in Personen. Freizeiten speichert an der Anmeldung nur Kennungen, Art, Status und Zeitpunkte. Es liest Namen und Mail über `Personen.Contracts` (AD-7, AD-27).
- Ereignisse und Mail-Aufträge tragen nur Kennungen, Zahlen, Datum und Enums. Die Eingangsmail ist eine selbst angeforderte Mail: Sie geht direkt aus dem Endpunkt nach erfolgreichem Command, weil das Token nicht in die Outbox darf.
- Zeltlotse.Public verwendet nur statische Bausteine, `<form method="post">` mit `<AntiforgeryToken />` und `[SupplyParameterFromForm]`, kein Skript, kein `style=`, `noindex` auf allen Token-Seiten. Neue Darstellungsbausteine entstehen in `Zeltlotse.Ui.Komponenten` mit bUnit-Test.
- Texte auf Deutsch ohne Du/Sie, laut EXPERIENCE: „Anmeldung absenden“, „Anmeldung eingegangen: Die Leitung meldet sich in Kürze.“, „Konto aus diesen Daten erstellen?“.
- Löschen und Auskunft (AD-16):
  - `Anmeldung` gehört zu den Arbeitsdaten der Freizeit. Bei `PersonGeloescht` wird die Person durch einen Platzhalter ersetzt, und die Anmeldung steht in der Auskunft von Freizeiten.
  - `GastZugang` und `Freigabe` gehören der Person: Sie werden mit der Person gelöscht und stehen in der Auskunft von Personen.
  - Ist ein Gast-Link abgelaufen und wurde er nie übernommen, löscht der Löschlauf von Personen die Gast-Personen (Elternteil und Kinder der Familie) über den bestehenden Lösch-Weg (AD-7).
- Fertig ist die Story erst mit Doku-Seiten, Demo-Daten, Screenshot-Tests (Handy und Laptop, mit axe) und fehlerfreiem Doku-Build (NFR-20).

**Never:**
- Kein Bestätigen, kein Absagen, keine Mails zu Bestätigung oder Absage, keine Freigabe-Stufe Team, keine Zählung bestätigter Teilnehmender. Das kommt in 2-3.
- Kein Freizeitpass, keine Fragen, keine Elterninfo. Das kommt in 2-4 und 2-7. Keine Texte, die diese Funktionen versprechen.
- Keine Gast-Anmeldung für Mitarbeitende und keine für Volljährige (Design Notes). Kein Telefonfeld.
- Keine neue Zeile in der Spine-Tabelle „Ereignis-Fluss“ und keine Spine-Änderung. Für „Konto aus diesen Daten erstellen“ wird `ProfilUebergabeAngenommen` wiederverwendet.
- Keine Pop-ups, kein JavaScript in Public, kein Speichern von Elternangaben auf dem Gerät (nur versteckte Formularfelder innerhalb derselben Sitzung), keine automatische Zusammenführung von Profilen.
- Kein Schreiben fremder Module, keine Fremdschlüssel über Schemagrenzen.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Freizeitseite mit Knöpfen | GET `/freizeit/{TN-Token}` | Block-Knopf „Anmelden“ → `/freizeit/{token}/anmelden`, darunter Textlink „Mit Konto anmelden“ → `/app/anmeldung/{token}` | – |
| Freizeitseite Mitarbeitende | GET `/freizeit/{MA-Token}` | nur Knopf „Anmelden“ → `/app/anmeldung/{token}` | – |
| Gast-Formular | GET `/freizeit/{TN-Token}/anmelden` | H1 „Anmeldung“, Satz „Ohne Konto möglich. Gefragt wird nur das Nötigste.“. Abschnitte „Kind“ (Vorname*, Nachname, Geburtsdatum*) und „Elternteil“ (Vorname*, Nachname, E-Mail* mit Hinweis „Dorthin gehen Bestätigung oder Absage.“). Teilnahmebedingungen-Text der Freizeit, Ankreuzfeld „Teilnahmebedingungen gelesen und bestätigt“*, Link Vertrauensseite, Knopf „Anmeldung absenden“, Legende „* Pflichtfeld“. Bei ausgebuchter Freizeit zusätzlich oben Status „Ausgebucht“ mit dem Satz aus 2-1; Absenden bleibt möglich (FR-67) | MA-Token oder ungültig → 404-Seite „Dieser Anmeldelink ist nicht mehr gültig.“ |
| Gast absenden | gültige Angaben, Haken gesetzt | Personen legt Elternteil, Familie, Kind und GastZugang an. Freizeiten legt die Anmeldung „eingegangen“ an (Art Teilnehmende, `TeilnahmebedingungenBestaetigtAm`). Mail an den Elternteil, Betreff „Anmeldung eingegangen: {Freizeit}“, mit dem Link `{OeffentlicheAdresse}/gast/{gastToken}` und „gilt bis“. Die Antwortseite zeigt „Anmeldung für {Vorname}“, die Meldung „Anmeldung eingegangen: Die Leitung meldet sich in Kürze.“, Status „Eingegangen“, „Eine Mail mit dem Gast-Link geht an {mail}.“, Card „Konto aus diesen Daten erstellen?“ mit Knopf-Link „Konto erstellen“ → `/app/gast/{gastToken}/konto` und Knopf „Weiteres Kind anmelden“ | – |
| Pflichtangabe fehlt / Haken fehlt | z. B. ohne Geburtsdatum | Formular mit Eingaben, Fehlerübersicht oben, „Fehler: …“ am Feld. Es wird nichts angelegt. | API 400 mit Feldfehlern |
| Kind volljährig | Geburtsdatum ≥ 18 Jahre | Feldfehler am Geburtsdatum „Fehler: Volljährige melden sich mit Konto an.“ und Link „Mit Konto anmelden“ | 400 |
| Weiteres Kind | POST „Weiteres Kind anmelden“ (Gast-Token als verstecktes Feld) | Formular mit Elternangaben als Text (nicht editierbar) und leeren Kind-Feldern. Absenden legt das Kind in derselben Gast-Familie an und meldet es an. Mail wie oben mit demselben Gast-Link. | Gast-Token ungültig → 404-Seite „Dieser Link ist nicht mehr gültig.“ |
| Doppelt angemeldet | dieselbe Person, dieselbe Freizeit | 409 Problemcode `konflikt`. Seite: „Hinweis: {Vorname} ist für diese Freizeit schon angemeldet.“ | – |
| Freizeit vorbei | Anmeldelink nach Freizeitende | 404 | `LinkEinloesungFehlgeschlagen` beim POST |
| Gast-Link | GET `/gast/{gastToken}` | H1 „Anmeldungen“. Je Anmeldung: Vorname, Freizeit, Zeitraum, Status „Eingegangen“ (StatusArt.Offen). Zusätzlich „Gast-Link gilt bis {Datum}“ und die Card „Konto aus diesen Daten erstellen?“ | ungültig, abgelaufen, übernommen oder falscher Zweck → API 404, Seite „Dieser Link ist nicht mehr gültig.“ |
| Konto erstellen | angemeldet, POST `/api/ich/gastprofil/uebernehmen` `{gastToken}` | Wie bei der Profil-Übergabe wird das leere eigene Profil gelöscht, `ProfilUebergabeAngenommen(ElternPersonId, KontoId)` abgelegt, und der GastZugang gilt als übernommen. Danach zeigt die Familienübersicht das Kind mit seinem Anmeldestatus. | eigenes Profil nicht leer oder schon Elternteil → 409. Token ungültig → 404 + Protokoll |
| Mit Konto anmelden | `/app/anmeldung/{token}`, ohne Sitzung | Weiter zum Login und mit dem Rückweg zurück | – |
| Mit Konto, Teilnehmende | POST `/api/ich/anmeldungen` `{anmeldelinkToken, personId, teilnahmebedingungenBestaetigt}`, personId = Kind der eigenen Familie oder die Person selbst | 201, Anmeldung „eingegangen“, keine Mail. Die App zeigt die Meldung „Anmeldung eingegangen: …“. | fremde PersonId → 404. Haken fehlt → 400 |
| Mit Konto, Mitarbeitende | MA-Token, personId = selbst | Anmeldung Art Mitarbeitende, auch wenn die Freizeit ausgebucht ist | Kind als personId → 404 |
| Meine Anmeldungen | GET `/api/ich/anmeldungen` | Anmeldungen der Person und der Kinder ihrer Familie über Organisationen hinweg, nur `anmeldungId, personId, freizeitName, ersterTag, letzterTag, art, status` | – |
| Freigabe | nach `AnmeldungEingegangen` | `personen.freigabe` mit Person, Freizeit, Anmeldung, Stufe Leitung und „gültig bis“ = `FreigabeGueltigBis` der Freizeit, idempotent | – |
| Teilnahmebedingungen pflegen | PATCH Freizeitseite-Feld `teilnahmebedingungen` (Leitung) | gespeichert (Autosave, ≤ 4000 Zeichen), erscheint im Formular | Team → 403 |

</intent-contract>

## Code Map

Abkürzungen: **Frz** = `src/Modules/Freizeiten/Zeltlotse.Freizeiten`, **Per** = `src/Modules/Personen/Zeltlotse.Personen`.

- `Frz/Domaene/Anmeldelink.cs` -- `LinkArt` (Z.6), Zwecke, `Platzlage` (Z.79). Bleibt mehrfach nutzbar, ohne Zustandsänderung beim Absenden.
- `Frz/Freizeitseite/Anmeldelinks.cs:121-143` -- `AnmeldelinkSuche.FindenAsync`: Token → Link + Freizeit, setzt den Mandanten, gibt nach Freizeitende `null`. Für den POST das Muster mit Schreib-Kontext aus `Frz/Leitung/LeitungAnnehmen.cs:31-76`: Handler, `IBenutzerCommand`, `LinkEinloesungFehlgeschlagen`. `AnmeldelinkErzeugen.Adresse` (Z.33).
- `Frz/Freizeitseite/OeffentlicheEndpunkte.cs` -- Vorlage für anonyme Token-Endpunkte. `IBestaetigteTeilnehmende` bleibt unverändert (2-3).
- `Frz/Freizeitseite/FreizeitseiteBearbeiten.cs`, `Api.Contracts/Freizeiten/Freizeitseite.cs` -- `FreizeitseiteFeld` um `Teilnahmebedingungen` erweitern. Feld an `Freizeit`, Validator ≤ 4000.
- `Frz/Domaene/Freizeit.cs` -- `Aggregat`, `EreignisAblegen`, `Zeitpunkte(jetzt).FreigabeGueltigBis`. Vorbild für Statusmethoden: `LeitungEinsetzen` (Z.~83).
- `Frz/Daten/FreizeitenDbContext.cs:46-93` -- `FreizeitenModell`. Neu: Tabelle `anmeldung` mit eindeutigem Index `(FreizeitId, PersonId)`, Spalte `teilnahmebedingungen`. Migration über die Design-Time-Fabrik am Dateiende, Ordner `Migrationen/`.
- `Frz/FreizeitenModul.cs:29-74` -- Registrierung. `oeffentlich`-Gruppe (Z.62); `/ich/freizeiten` (Z.74) als Muster für `.Organisationsuebergreifend()`.
- `Frz/Datenschutz/FreizeitenDatenschutz.cs:19-42` -- `FreizeitPersonLoeschen` und `FreizeitenAuskunft` um Anmeldungen erweitern.
- `Frz/Leitung/LeitungsMail.cs` -- Vorlage für die direkte Mail (`IMailVersand`, `OeffentlicheAdresse.Lesen`, Betreff als Konstante).
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/FreizeitRolleGeaendert.cs` -- Vorbild für `AnmeldungEingegangen(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art, DateTimeOffset FreigabeGueltigBis)`. Das Enum liegt im selben `.Events`-Namensraum. Der Name muss exakt der Spine-Zeile entsprechen (`EreignisFlussTests`).
- `Per/Profil/Person.cs` -- `Person.Kind(...)` (Z.43), `Person.Anlegen`, `Loeschen()` (Z.225). Elternteil ohne Konto über `Person.Anlegen` plus Namen.
- `Per/Familien/KindAnlegen.cs:19-60`, `KindRegeln`, `Volljaehrigkeit`, `FamilienSperre.SicherstellenAsync` -- Kind, Familie und `PersonenSchluessel.Anlegen` in einer Transaktion. Diese Regeln wiederverwenden.
- `Per/Familien/ProfilUebernehmen.cs:19-80`, `ProfilUebergabe.cs:85`, `FamilieEndpunkte.cs:240` -- Muster für „Gast-Profil übernehmen“: leeres eigenes Profil löschen, `ProfilUebergabeAngenommen` ablegen; Konten verbindet über `KontoMitProfilVerbinden`.
- `Per/Freizeitrollen/FreizeitRolleUebernehmen.cs:17-80` -- Schablone für den Empfänger `FreigabeAnlegen`: Mandant aus `Umschlag.OrganisationId`, `IOrganisationsEigen`, `IEreignisEmpfaenger`. Registrierung in `Per/PersonenModul.cs:37-39`.
- `Per/Datenschutz/PersonenAuskunft.cs`, `PersonenLoeschlauf.cs`, Person-Löschung -- GastZugang und Freigabe in Auskunft und Löschung aufnehmen. Abgelaufene, nicht übernommene Gast-Zugänge löschen ihre Personen.
- `src/Modules/Personen/Zeltlotse.Personen.Contracts/Namen/` -- daneben ein neuer Ordner `Anmeldung/` mit:
  - `IGastZugaenge.PruefenAsync(token)` → `GastZugangInfo(Guid ElternPersonId, string Mail, IReadOnlyList<Guid> KindPersonIds, DateTimeOffset GueltigBis)?`;
  - `IVertretung.DarfAnmeldenAsync(Guid angemeldetePersonId, Guid personId)` → selbst oder Kind unter 18 der eigenen Familie.
- `src/Zeltlotse.SharedKernel/Zugangslinks/Zugangslink.cs:17-24`, `Sicherheitsprotokoll/ISicherheitsprotokoll.cs`, `Anmeldung/IAngemeldet.cs` -- Token, Protokoll, aktuelle Person.
- `src/Zeltlotse.Api/Sicherheit/AntiforgeryZwang.cs:36-42` -- akzeptiert das Formularfeld-Token für Blazor-Formulare außerhalb von `/api`. Nicht ändern.
- `src/Zeltlotse.Public/Seiten/Freizeitseite.razor` -- Muster: `OeffentlicherRahmen`, `Navigation.NotFound()`, `ZuVieleAufrufe.Status429`, `<HeadContent>` noindex. Der Kommentar „Anmelden kommt mit Story 2-2“ (Z.43) wird ersetzt. `PublicSeiten.cs:31` registriert die Clients. `Transport/PublicTransportHandler.cs` reicht keine Sitzung durch, deshalb liegt die Anmeldung mit Konto in der App.
- `src/Zeltlotse.Ui/Api/{FreizeitenApiClient,PersonenApiClient}.cs`, `ApiFehler.FeldFehlerAsync` -- neue Client-Methoden und Feldfehler.
- `src/Zeltlotse.Ui.Komponenten/` -- statisch: `Feld`, `Ankreuzfeld`, `Knopf` (`Href`), `Meldung`, `Status`, `Card`, `Dachzeile`. Interaktiv (`[NurInteraktiv]`, nicht in Public): `Formular`, `Auswahl`, `InlineBestaetigung`. Neu ist eine statische `Fehleruebersicht` (Liste mit Sprunglinks zu den Feld-Ids) mit bUnit-Test.
- `src/Zeltlotse.App/Features/Personen/FamilieAnsicht.razor` -- Kommentar Z.7-8 „Freizeiten und Anmeldestatus kommen mit der Anmeldung“. Hier kommen die Anmeldungen je Kind-Card und „Eigene Anmeldungen“ hinzu.
- `src/Zeltlotse.App/Features/Konten/AnmeldungAbschluss.cs`, `Sitzung/AppSitzung` (`RueckwegNehmen`) -- Rückweg nach dem Login für `/app/anmeldung/{token}` und `/app/gast/{token}/konto`.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- Autosave-Feld „Teilnahmebedingungen“ (mehrzeilig), Hinweis „Erscheint bei der Anmeldung vor dem Absenden.“
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs`, `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs:84-235` -- Demo: Teilnahmebedingungen-Text; Katrin meldet Emma mit Konto an (über Command, idempotent). Zähler anpassen (`freizeiten.outbox`, neue Tabellen).
- `tests/Zeltlotse.Testing/MailOrdner.cs` (Regex `LoginMailMuster`) -- Gast-Link `/gast/<token>` ergänzen. Factory: `OutboxJetztZustellenAsync`, `Uhr`, `TestAbsenderIp`.
- `tests/Zeltlotse.Api.Tests/{FreizeitseiteTests.cs,ClientAuslieferungTests.cs:392-430,EndpunktListeTests.cs,AnmeldeHelfer.cs,FreizeitHelfer.cs}` -- Muster. Public-Formular-POST: GET, `__RequestVerificationToken` und `_handler` aus dem HTML ziehen, `FormUrlEncodedContent` mit demselben Client und `TestAbsenderIp` senden. EndpunktListe: `Anonym`, `oeffentlich.Count` (heute 2), `Organisationsuebergreifend`, Freizeit-Routen.
- `tests/Zeltlotse.Ablaeufe.Tests/FreizeitRolleGeaendertTests.cs` -- Vorlage für den Durchlauf-Test.
- `tests/Zeltlotse.ArchitectureTests/{EreignisFlussTests,LoeschPflichtTests,ClientTests,DokuRegeln,DokuTests:55-70}.cs` -- Regeln. `PflichtSeiten` und die Negativliste wachsen mit.
- `tests/Zeltlotse.E2E.Tests/{LoginTests,DokuScreenshotTests,E2EHilfe}.cs` -- Muster für Handy mit axe; `FreizeitseiteAsync` (Z.~634) liest den Demo-Link.
- `doku/src/content/docs/eltern/{index,freizeitseite,familienkonto}.md`, `mitarbeitende/index.md`, `leitung/freizeitseite-einrichten.md`, `daten.md` -- Doku-Struktur. `freizeitseite.md` enthält den Satz „Das Anmeldeformular selbst kommt …“, der ersetzt wird.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Api.Tests/AnmeldungTests.cs` (neu, zuerst) -- HTTP-Tests für alle Zeilen der Matrix: Pflichtfälle nach AD-30 für jeden neuen Endpunkt, Token-Fälle mit `FakeTimeProvider` (Freizeitende, Gast-Link nach 1 Tag ohne Freigabe, nach Freigabe-Ende, nach Übernahme), Mail-Inhalt, Public-HTML (Formular-POST mit Antiforgery-Feld, Fehlerübersicht, Antwortseite, Weiteres Kind, Gast-Link-Seite, kein `<script`/`style=`, `noindex`), Protokolleinträge. -- Testgetrieben.
- `tests/Zeltlotse.Ablaeufe.Tests/AnmeldungEingegangenTests.cs` (neu, zuerst) -- Anmeldung → Freigabe Stufe Leitung mit `GueltigBis`, Inbox-Idempotenz.
- `src/Modules/Freizeiten/...` -- `Anmeldung : Aggregat, IOrganisationsEigen` (Id, OrganisationId, FreizeitId, PersonId, Art, Status Eingegangen, EingegangenAm, TeilnahmebedingungenBestaetigtAm; Fabrik legt `AnmeldungEingegangen` ab). Dazu:
  - Slices `Anmeldungen/`: Gast-POST unter `oeffentlich/{token}/anmeldungen`, `POST` und `GET /api/ich/anmeldungen`, `GET /api/freizeiten/gast/{gastToken}`;
  - Feld `Teilnahmebedingungen`;
  - Datenschutz;
  - Migration;
  - Contracts-Event und Enum.
  -- Kern.
- `src/Modules/Personen/...` -- `GastZugang` (Id, ElternPersonId, Mail normalisiert, TokenHash, AngelegtAm, UebernommenAm?) und `Freigabe` (IOrganisationsEigen; PersonId, FreizeitId, AnmeldungId, Stufe, GueltigBis). Slices:
  - `POST /api/personen/oeffentlich/gaeste` (Elternteil, Familie, Kind, Zugang; Antwort `{gastToken, kindPersonId}`);
  - `POST /api/personen/oeffentlich/gaeste/{gastToken}/kinder`;
  - `POST /api/ich/gastprofil/uebernehmen`;
  - Empfänger `FreigabeAnlegen`;
  - Contracts `IGastZugaenge`, `IVertretung`;
  - Auskunft, Löschung, Löschlauf, Migration.
  Tests in `tests/Zeltlotse.Personen.Tests` (Gültigkeitsregel mit künstlicher Uhr, Mandantenfilter `Freigabe`). -- Gast-Daten gehören Personen (AD-7).
- `src/Zeltlotse.Api.Contracts/{Freizeiten,Personen}/` und `src/Zeltlotse.Ui/Api/` -- DTOs (`GastAnlegen`, `GastAngelegt`, `GastAnmeldungAbsenden`, `AnmeldungAbsenden`, `AnmeldungZeile`, `GastLinkAnsicht`), Enums `AnmeldungArt`/`AnmeldungStatus`, Client-Methoden. -- AD-5.
- `src/Zeltlotse.Ui.Komponenten/Fehleruebersicht.razor` (+ bUnit) -- statische Fehlerübersicht mit `role="alert"` und Sprunglinks.
- `src/Zeltlotse.Public/Seiten/{Freizeitseite,Anmelden,GastLink,GastLinkUngueltig}.razor` -- Knöpfe auf der Freizeitseite. Formular nach Matrix mit drei Formularnamen (`anmelden`, `weiteres-kind`, `weiteres-kind-absenden`) und gemeinsamen versteckten Feldern. Die Antwortseite wird direkt im POST gerendert (keine Weiterleitung, damit der Gast-Link nicht im Verlauf steht). 400 → Feldfehler, 409 → Hinweis, 429 → `ZuVieleAufrufe`. `<PageTitle>` und noindex. -- Öffentliche Oberfläche.
- `src/Zeltlotse.App/Features/Freizeiten/AnmeldungMitKonto.razor` (`/anmeldung/{Token}`) -- Login-Pflicht mit Rückweg. Freizeit über das öffentliche GET. Auswahl (Teilnehmende: Kinder unter 18 der Familie und „Selbst“; Mitarbeitende: nur „Selbst“ als Text), Teilnahmebedingungen, Ankreuzfeld, „Anmeldung absenden“, danach Meldung und Link zur Familienübersicht. 404 → „Dieser Anmeldelink ist nicht mehr gültig.“
- `src/Zeltlotse.App/Features/Personen/GastProfilUebernehmen.razor` (`/gast/{Token}/konto`) -- Login-Pflicht mit Rückweg. Text „Die Angaben aus der Gast-Anmeldung kommen in dieses Konto.“, Knopf „Angaben übernehmen“, danach zur Familienübersicht. 409 → Hinweis „Dieses Konto hat schon eigene Angaben. Die Gast-Anmeldung bleibt über den Gast-Link erreichbar.“
- `src/Zeltlotse.App/Features/Personen/FamilieAnsicht.razor` und `Einstellungen.razor` -- Anmeldestatus je Kind und eigene Anmeldungen (Status-Wort mit Quadrat), Feld „Teilnahmebedingungen“.
- `tests/Zeltlotse.Ui.Tests/` -- bUnit für die neuen App-Seiten (Auswahl, Fehler, 404, 409) und `Fehleruebersicht`.
- `src/Zeltlotse.Api/Demo/` und `DemoDatenTests.cs` -- Demo-Werte laut Code Map.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs`, `tests/Zeltlotse.Testing/MailOrdner.cs` -- Listen, Zähler, Link-Muster.
- `tests/Zeltlotse.E2E.Tests/GastAnmeldungTests.cs` (neu) -- Handy 375×812:
  1. Demo-Link öffnen, „Anmelden“ tippen.
  2. Absenden ohne Angaben: Fehlerübersicht erscheint, axe prüft.
  3. Ausfüllen, Haken setzen, „Anmeldung absenden“: „Anmeldung eingegangen“ erscheint, axe prüft.
  4. Den Gast-Link aus der Mail öffnen: Status „Eingegangen“, axe prüft.
  5. Keine CSP-Meldung.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories für Handy und Laptop mit axe: `anmeldung-formular`, `anmeldung-eingegangen` und `gast-link` (fester Gast „Lina Beispiel“, `lina.eltern@example.org`) sowie `anmeldung-mit-konto` (Katrin, ohne Absenden). Die Familienübersicht zeigt jetzt Emmas Status.
- `doku/src/content/docs/eltern/kind-anmelden.md` (neu: Gast, Geschwister, Teilnahmebedingungen, Gast-Link, Konto erstellen, mit Konto), `mitarbeitende/anmelden.md` (neu), Anpassungen an `eltern/{index,freizeitseite,familienkonto}.md`, `leitung/freizeitseite-einrichten.md` (Teilnahmebedingungen), `daten.md` (Gast-Link-Gültigkeit, Löschen von Gast-Profilen), `DokuRegeln.PflichtSeiten` + `DokuTests`. -- NFR-20.

**Acceptance Criteria:**
- Given die Demo-Daten und ein Handy, when ein Elternteil über den Teilnehmenden-Link ohne Konto ein Kind anmeldet, then erscheint „Anmeldung eingegangen“, die Leitung hat eine Freigabe der Stufe Leitung für das Kind, und an die Mail geht ein Gast-Link, der den Status „Eingegangen“ zeigt.
- Given eine Gast-Anmeldung, when „Weiteres Kind anmelden“ ohne erneute Elternangaben abgeschickt wird, then gehören beide Kinder zur selben Gast-Familie und sind über denselben Gast-Link sichtbar.
- Given ein Gast-Link, when ein angemeldetes, leeres Konto ihn mit „Angaben übernehmen“ einlöst, then zeigt die Familienübersicht dieses Kontos die Kinder mit Status „Eingegangen“, und der Gast-Link liefert 404.
- Given ein Gast-Link ohne Übernahme, when das Freigabefenster aller seiner Anmeldungen endet und der Löschlauf läuft, then sind Gast-Personen, Gast-Zugang und Freigaben gelöscht, und die Anmeldungen tragen den Platzhalter.
- Given `dotnet test` und `npm run build` in `doku/`, when beide laufen, then sind sie fehlerfrei, und die acht neuen Screenshots existieren und sind verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 42 findings — high 0, medium 9, low 27, false 6, maybe-false 0
- findings:
  - `[medium]` `[defer]` (Blind) Doppelte Gast-Anmeldung beim erneuten Absenden, Hinweis „schon angemeldet“ auf dem Gast-Weg unerreichbar — real; jede Lösung (Profilabgleich oder Einmal-Schlüssel) braucht eine Produktentscheidung, Abgleich widerspricht „keine automatische Zusammenführung“; in `deferred` eingetragen.
  - `[low]` `[reject]` (Blind) Zwei Aufrufe ohne Fehlerplan, verwaiste Gast-Profile/Kinder — real, aber selten (zweiter Aufruf scheitert direkt nach dem ersten); Design Notes nehmen verwaiste Gast-Profile bewusst hin; Lösung bräuchte Ausgleichslogik.
  - `[low]` `[reject]` (Blind) Eingangsmail ohne Fehlerbehandlung — gleiches Muster wie alle selbst angeforderten Mails (Login, Einladungen, Übergabe); Mailfehler selten; Lösung fügt Fehlerzweig hinzu.
  - `[low]` `[reject]` (Blind) Kein Weg, einen verlorenen Gast-Link neu zu bekommen; angezeigte Mail aus verstecktem Feld — Zusatzfunktion außerhalb der Story; die Mail geht an die gespeicherte Adresse, das versteckte Feld betrifft nur die eigene Anzeige.
  - `[low]` `[reject]` (Blind) Anonymer Gast-Endpunkt ohne Bindung an einen Anmeldelink, Mail an beliebige Adressen — so im Approach festgelegt (Schritt 1 ohne Token); die Mail verlangt einen gültigen Anmeldelink, Endpunkte sind zugriffsbegrenzt, verwaiste Gäste fallen nach 1 Tag weg.
  - `[false]` `[reject]` (Blind) Gast-Token nicht an Freizeit/Organisation gebunden — der Gast-Link gilt laut Design Notes für die Gast-Familie, nicht für eine Freizeit; AD-30 „X wirkt nicht auf Y“ betrifft den Anmeldelink, und der ist gebunden (Test `Der_Link_von_X_meldet_nur_fuer_X_an…`).
  - `[medium]` `[patch]` (Blind) Löschlauf liest Gültigkeit vor der Sperre und prüft nicht neu; Outbox-Verzug — gruppiert mit Edge 3; Fix: nach der Sperre `FOR UPDATE` auf den Gast-Zugang, `Reload`, Übernahme und Gültigkeit neu prüfen (`PersonLoeschen.cs`). Outbox-Verzug über 1 Tag siehe Edge 5.
  - `[low]` `[reject]` (Blind) „Konto erstellen“ verlangt Neu-Anmelden, Token wird erst beim Klick geprüft — Neu-Anmelden ist nötig, weil Konten das Konto erst über das Ereignis mit dem Profil verbindet; danach führt der Rückweg zur Familienübersicht; ungültige Links sind der Ausnahmefall.
  - `[low]` `[patch]` (Blind) Demo-Daten werfen nach dem Ende der Demo-Freizeit — gruppiert mit Edge 9; Fix: `DemoDaten.AnmeldungAsync` überspringt die Anmeldung statt zu werfen.
  - `[low]` `[reject]` (Blind) Gast-Zugang bleibt nach der Übernahme bestehen — gehört der Person, steht in der Auskunft und wird mit ihr gelöscht; Aufräumen bräuchte neue Regel.
  - `[low]` `[reject]` (Blind) Gast-Token im API-Pfad — gleiches Muster wie die Anmeldelink-Endpunkte aus 2-1; die öffentliche Adresse `/gast/{token}` trägt das Token ohnehin (AD-11).
  - `[low]` `[patch]` (Blind) Lücken der Seite „Mit Konto“ — Fix: Hinweis bei eigener Doppelanmeldung lautet jetzt „Die eigene Anmeldung für diese Freizeit besteht schon.“ statt „Die Person …“; fehlender Vertrauensseiten-Link und Rollenprüfung für Mitarbeitende nicht verlangt.
  - `[low]` `[reject]` (Blind) Spec Change Log leer trotz Abweichungen — Fix wäre eine Spec-Änderung.
  - `[false]` `[reject]` (Blind) Fremde Skript-Änderung im Diff — `scripts/story-ausfuehren.ps1` liegt im eigenen Commit 557f007 nach der Baseline, nicht in dieser Story.
  - `[low]` `[reject]` (Blind) Familienübersicht scheitert ganz, wenn `/api/ich/anmeldungen` scheitert — im Alltag unwahrscheinlich; Fix bräuchte eigenen Fehlerzweig.
  - `[low]` `[patch]` (Blind) Toter Rückfall `personen.Count == 0` in `FreizeitenAuskunft` — entfernt.
  - `[low]` `[reject]` (Edge) Mailfehler nach Commit ergibt 500 — wie Blind 3.
  - `[medium]` `[defer]` (Edge) Doppeltes Absenden / Fehler nach `GastAnlegen` erzeugt doppelte Familien — gruppiert mit Blind 1.
  - `[medium]` `[patch]` (Edge) Übernahme zwischen Lesen und Sperre im Löschlauf löscht übernommenes Profil — real (Löschlauf sperrte die Zugangszeile nicht); Fix wie Blind 7.
  - `[low]` `[patch]` (Edge) Sperre der Übernahme auf `PersonId` statt `ElternPersonId` — die Sperre der eigenen Person ist richtig, der Kommentar war missverständlich; Kommentar korrigiert, Serialisierung mit dem Löschlauf läuft über die Zeilensperre.
  - `[low]` `[reject]` (Edge) Löschlauf nach 1 Tag, während `AnmeldungEingegangen` noch in der Outbox liegt — die Outbox stellt in Sekunden zu; ein Verzug über 1 Tag ist kein Alltagsfall; Fix bräuchte modulübergreifendes Lesen.
  - `[low]` `[reject]` (Edge) Übernahme, während die Freigabe einer eigenen Anmeldung noch in der Outbox liegt — Zeitfenster von Sekunden; Fix bräuchte neuen Vertrag.
  - `[low]` `[reject]` (Edge) Gast-Kind wird während der Gültigkeit volljährig und lässt sich anmelden — die Seiten legen immer ein neues Kind an (Volljährigkeit geprüft); nur über einen gebastelten API-Aufruf mit bekannter Kind-Kennung erreichbar.
  - `[false]` `[reject]` (Edge) Eltern sehen Anmeldungen volljähriger Kinder — volljährige Kinder ohne Übergabe gehören weiter zur Familie (FR-4, Übergabe mit 18); die Familienübersicht zeigt sie ebenso.
  - `[low]` `[patch]` (Edge) Demo-Daten werfen nach Freizeitende — Fix wie Blind 9.
  - `[low]` `[reject]` (Edge) Familienübersicht scheitert ganz bei Fehler der Anmeldungen — wie Blind 15.
  - `[low]` `[reject]` (Edge) Ein Fehler blockiert alle Gast-Löschungen im selben Command — gleiches Muster wie `PersonenFristenLoeschen`; selten; Fix bräuchte Aufteilung in Einzel-Transaktionen.
  - `[low]` `[reject]` (Edge, Claim) Volljährig gewordenes Kind über den Gast-Weg — wie Edge 7.
  - `[low]` `[reject]` (Edge, Claim) Löschung vor Eintreffen der Freigabe — wie Edge 5.
  - `[medium]` `[patch]` (Verification) Kein Negativ-Test, dass der Löschlauf übernommene Gast-Profile verschont — Test im Übernahme-Fall ergänzt (Löschlauf nach Freigabe-Ende, Eltern, Kind und Zugang bleiben).
  - `[medium]` `[patch]` (Verification) `AnmeldungenLoeschen` ungetestet — `LoeschlaufTests.Nachweis_Erinnerungen_…` legt je Freizeit eine Anmeldung an und prüft, dass nur die von a11 bleibt.
  - `[medium]` `[patch]` (Verification) Teilindex für den Platzhalter ungetestet — Auskunft-/Lösch-Test meldet zusätzlich Katrin selbst an und erwartet zwei Platzhalter-Anmeldungen in X.
  - `[medium]` `[patch]` (Verification) Abschnitt „Gast-Link“ der Auskunft ungeprüft — im Übernahme-Test geprüft (Mail-Adresse, „In ein Konto übernommen am“, kein Token).
  - `[medium]` `[defer]` (Intent a) „Doppelt angemeldet“ auf der Seite nur über Test-Umleitung erreichbar — gruppiert mit Blind 1.
  - `[low]` `[reject]` (Intent b) Formular-POST nach Freizeitende schreibt kein `LinkEinloesungFehlgeschlagen` — der API-POST schreibt es (getestet); die Seite ruft die API dann gar nicht auf; Fix bräuchte künstlichen Aufruf.
  - `[low]` `[reject]` (Intent c) Schritt 1 nicht an den Anmeldelink gebunden — wie Blind 5.
  - `[low]` `[reject]` (Intent d) „Danach zeigt die Familienübersicht“ erst nach Neu-Anmelden — wie Blind 8.
  - `[false]` `[reject]` (Intent e) Mandantenfilter auch an weiteren Stellen umgangen — personenbezogene Lesepfade (AD-26), Löschen und Auskunft (AD-16); die Intent-Regel betrifft die anonymen Token-Wege.
  - `[low]` `[reject]` (Intent f) „gilt bis“ in Mail und Gast-Link-Seite weichen kurz ab — nur bis zur Outbox-Zustellung (Sekunden).
  - `[low]` `[reject]` (Intent g) Durchlauf-Test stellt nicht dieselbe Nachricht zweimal zu — gleiches Muster wie `FreizeitRolleGeaendertTests`; Wiederzustellung ist im Personen-Test geprüft.
  - `[false]` `[reject]` (Intent h) „Fremde Organisation → 404“ für das PATCH fehlt — besteht seit 2-1 (`Team_bekommt_403_ohne_Rolle_fremde_Organisation_und_unbekannte_Freizeit_404`) für denselben Endpunkt.
  - `[false]` `[reject]` (Intent i) Texte erwähnen die Zusage — der Ausgebucht-Satz ist von der Matrix aus 2-1 verlangt; der Demo-Text ist Inhalt der Leitung und verspricht keine ausgeschlossene Funktion.

## Design Notes

Entscheidungen im autonomen Lauf. Spec und Architektur lassen sie offen; gewählt ist jeweils die einfachste Lösung.

- **Zwei Aufrufe statt eines.** Eine Anmeldung schreibt Personendaten (Personen) und die Anmeldung (Freizeiten). Module rufen einander nur lesend auf (AD-4), deshalb setzt die öffentliche Seite beide Aufrufe zusammen (AD-2, wie `ProfilSicherstellen` nach dem Login). Freizeiten vertraut keiner `PersonId` vom Client. Es prüft die Personen über das Gast-Token (`IGastZugaenge`) bzw. über die Sitzung (`IVertretung`). Bricht der zweite Aufruf ab, bleibt ein Gast-Profil ohne Anmeldung. Es verfällt nach 1 Tag und wird gelöscht.
- **Gast-Link gehört Personen.** Die Tabelle Datenbesitz nennt ihn nicht. Personen kann ihn prüfen (Pfeil Freizeiten → Personen) und braucht ihn in 2-4 für den Freizeitpass. Seine Gültigkeit folgt der Freigabe („bis Ende des Freigabefensters“, AD-11). Das passt zu AD-7: Ein Profil ohne Konto und ohne laufende Freigabe wird gelöscht. Er gilt für die ganze Gast-Familie, damit Geschwister denselben Link haben.
- **Freigabe schon jetzt, aber nur Stufe Leitung.** Die Spine-Zeile `AnmeldungEingegangen` → Personen besteht. Ohne Freigabe wäre die Gültigkeit des Gast-Links nicht bestimmbar. Stufe Team, Fragen-Kennungen und Sichtregeln folgen in 2-3 bis 2-5.
- **Status-Lesen über den Gast-Link** liest Anmeldungen der geprüften Personen ohne Mandantenfilter. Das ist der personenbezogene Lesepfad (AD-26), nur mit dem Gast-Link statt der Sitzung. Er liefert nur Übersichtsfelder.
- **Gast nur für Kinder unter 18 bei Teilnehmenden.** Mitarbeitende brauchen ein Konto (NS-3; Rechte und Nachweise hängen am Konto). Volljährige besitzen ihre Daten selbst (FR-18) und melden sich mit Konto an. Ein Konto anzulegen ist dabei nur ein Login-Link.
- **Pflichtfelder (NFR-12):** Kind: Vorname, Geburtsdatum (wie `KindAnlegen`). Elternteil: Vorname, E-Mail. Nachnamen sind freiwillig. Kein Telefon (Datensparsamkeit, noch kein Nutzer).
- **Ein Formular statt zwei Schritte.** Ohne Skript bräuchte ein Schritt-Formular Zwischenstände in versteckten Feldern. Ein einseitiges Formular ist einfacher, und die Schritt-Leiste ist laut EXPERIENCE nur für mehrteilige Formulare gedacht.
- **Teilnahmebedingungen** sind ein Textfeld der Freizeit (Leitung, Einstellungen), das im Formular steht. Die Bestätigung speichert `TeilnahmebedingungenBestaetigtAm`. Ist das Feld leer, steht dort der Satz „Für diese Freizeit sind keine eigenen Teilnahmebedingungen hinterlegt.“. Dokumente kommen in 2-7.
- **„Konto erstellen“ verwendet `ProfilUebergabeAngenommen` wieder.** Die Folgeaktion in Konten ist dieselbe: das Konto mit einem bestehenden Profil verbinden. Es gibt also keine neue Spine-Zeile. Die Regel „eigenes Profil muss leer sein“ verhindert ein automatisches Zusammenführen.
- **Mails:** Die Eingangsmail an Gäste ist eine selbst angeforderte Mail (AD-13), direkt aus dem Endpunkt. Wer sich mit Konto anmeldet, bekommt keine Mail und sieht den Status in der App.
- **Geschwister in derselben Sitzung:** Das Gast-Token reist als verstecktes Feld im POST. Es landet nicht in Adresse, Verlauf oder Speicher des Geräts. Die Antwortseite rendert direkt im POST, ohne Weiterleitung.

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- erwartet: 0 Fehler, 0 Warnungen.
- `dotnet test` (aus dem Repo-Wurzelordner) -- erwartet: alles grün, einschließlich Architektur-, Ablauf-, E2E- und Doku-Tests.
- `npm run build` in `doku/` -- erwartet: Exit 0.

**Manual checks (if no CLI):**
- Die acht neuen PNGs in `doku/src/assets/screenshots/` zeigen Formular, Bestätigung, Gast-Link und die Seite „Mit Konto anmelden“.

## Auto Run Result

Status: done

**Umgesetzt:** Anmeldung über die Anmeldelinks aus 2-1.
- Gast-Anmeldung ohne Konto für Kinder unter 18: öffentliches Formular ohne Skript, zwei API-Aufrufe (Personen legt Gast-Familie und Gast-Zugang an, Freizeiten die Anmeldung „eingegangen“), Eingangsmail mit Gast-Link.
- Geschwister über „Weiteres Kind anmelden“, digitale Bestätigung der Teilnahmebedingungen (neues Feld der Freizeit, Autosave der Leitung).
- Gast-Link `/gast/{token}` mit Status; „Konto aus diesen Daten erstellen“ übernimmt das Gast-Profil über `ProfilUebergabeAngenommen`.
- Anmeldung mit Konto für Teilnehmende und Mitarbeitende in der App; Anmeldestatus in der Familienübersicht.
- `AnmeldungEingegangen` → Freigabe der Stufe Leitung in Personen; Auskunft, Löschen und Löschlauf (abgelaufene, nie übernommene Gast-Profile; Anmeldungen nach der Frist).
- Demo-Daten, Doku-Seiten `eltern/kind-anmelden.md` und `mitarbeitende/anmelden.md`, acht neue Screenshots.

**Dateien (Auswahl):**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/*`, `Domaene/Anmeldung.cs`, Migration `Anmeldung` -- Anmeldung, Endpunkte, Eingangsmail.
- `src/Modules/Personen/Zeltlotse.Personen/Gaeste/*`, `Freigaben/Freigabe.cs`, Migration `GastUndFreigabe` -- Gast-Zugang, Gast-Endpunkte, Übernahme, Freigabe-Empfänger, Verträge `IGastZugaenge`/`IVertretung`.
- `src/Modules/Personen/Zeltlotse.Personen/Datenschutz/*`, `Freizeiten/Datenschutz/FreizeitenDatenschutz.cs` -- Auskunft, Löschen, Löschlauf.
- `src/Zeltlotse.Public/Seiten/{Anmelden,GastLink,GastLinkUngueltig,Freizeitseite,AnmeldelinkUngueltig}.razor` -- öffentliche Oberfläche.
- `src/Zeltlotse.App/Features/{Freizeiten/AnmeldungMitKonto,Personen/GastProfilUebernehmen,Personen/FamilieAnsicht,Freizeiten/Einstellungen}.razor` -- App-Oberfläche.
- `src/Zeltlotse.Ui.Komponenten/{Fehleruebersicht,StatusListe}.razor` -- neue statische Bausteine.
- Tests: `FreizeitAnmeldungTests`, `GastAnmeldungSeitenTests`, `AnmeldungEingegangenTests`, `GastZugangTests`, `FreizeitAnmeldungSeitenTests`, E2E `GastAnmeldungTests`, Screenshot-Tests.

**Review (42 Befunde: hoch 0, mittel 9, gering 27, falsch 6):**
- Patches (gepatchte Einträge: mittel 5, gering 5):
  - Löschlauf für Gast-Profile sperrt nach der Advisory-Sperre die Zeile des Gast-Zugangs (`FOR UPDATE`), lädt ihn neu und prüft Übernahme und Gültigkeit erneut (Wettlauf mit „Konto erstellen“).
  - Neue Tests: Löschlauf verschont übernommene Gast-Profile; Abschnitt „Gast-Link“ in der Auskunft; zwei Platzhalter-Anmeldungen derselben Freizeit; Anmeldungen fallen mit der Frist der Organisation.
  - Demo-Daten überspringen Emmas Anmeldung, wenn der Link nicht mehr gilt, statt den Start abzubrechen.
  - Hinweis bei eigener Doppelanmeldung; Kommentar zur Sperre in der Übernahme; toter Rückfall in der Auskunft entfernt.
- Zurückgestellt: doppelte Gast-Anmeldung durch erneutes Absenden (braucht Produktentscheidung, siehe `deferred`).
- Verworfen: alle übrigen Befunde mit Begründung im Review Triage Log.

**Folge-Review empfohlen: ja.** Zwei oder mehr mittlere Befunde wurden gepatcht. Ungeprüftes Risiko: Die neue Sperr-und-Neuprüf-Logik im Löschlauf für Gast-Profile (`PersonLoeschen.cs`) ist nur sequenziell getestet, nicht unter echter Gleichzeitigkeit mit der Übernahme.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Fehler, 0 Warnungen (vor und nach den Patches).
- `dotnet test`: alle Tests grün, 0 fehlgeschlagen (vor und nach den Patches), einschließlich Architektur-, Ablauf-, E2E- und Doku-Tests.
- `npm run build` in `doku/`: 27 Seiten, fehlerfrei (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: alle 17 Zeilen durch gelaufene, grüne Tests abgedeckt.
- Screenshots `app-offline-*` und `vertrauensseite-laptop` mit reinem Rendering-Rauschen zurückgesetzt.

**Restrisiken:**
- Doppelte Gast-Anmeldungen beim erneuten Absenden (zurückgestellt).
- Selbst angeforderte Eingangsmail ohne Wiederholung bei Mailfehlern (gleiches Muster wie alle Token-Mails).
- Nach der Übernahme ist ein Neu-Anmelden nötig, bevor die Familienübersicht die Kinder zeigt.
