---
title: 'Anmeldungen bestätigen und absagen'
type: 'feature'
created: '2026-10-12'
status: 'done'
baseline_revision: '859297c666dc3d1d802419a05eccd1206204ceca'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/SPEC.md'
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/planning-artifacts/ux-designs/ux-Zeltlotse-2026-10-10/EXPERIENCE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/2-2-anmeldung-ueber-anmeldelinks-als-gast-und-mit-gast-link.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Anmeldungen aus Story 2-2 bleiben für immer „eingegangen“. Die Leitung kann nicht bestätigen oder absagen, niemand bekommt Bestätigungs- oder Absage-Mail, „ausgebucht“ zählt nie, Mitarbeitende werden nie Team der Freizeit und nie Mitglied der Organisation, und Eltern sehen nicht, wie sie absagen können (CAP-9, CAP-3, FR-7, FR-20, FR-21, FR-67).

**Approach:**
- Die Leitung bekommt die App-Seite „Anmeldungen“ (`/app/freizeiten/{id}/anmeldungen`) mit Platz-Balken und Listen für Teilnehmende und Mitarbeitende. Je Zeile gibt es „Bestätigen“ oder „Absage eintragen“ (Inline-Bestätigung).
- `Anmeldung.Bestaetigen()` und `Anmeldung.Absagen()` legen `AnmeldungBestaetigt` bzw. `AnmeldungAbgesagt` ab (Spine-Zeilen bestehen schon). Sie merken außerdem einen Mail-Auftrag für die Outbox vor.
- Personen setzt daraufhin die Freigabe auf Stufe Team oder beendet sie. Organisationen legt bei Mitarbeitenden eine `Mitgliedschaft` an.
- Bestätigte Mitarbeitende bekommen in Freizeiten die Freizeit-Rolle Team. Eine Absage entzieht sie wieder.
- Für Absagen trägt die Leitung einen Kontakt (Mail, Telefon) in den Einstellungen ein. Familienübersicht und Gast-Link zeigen ihn zusammen mit dem Status.

## Boundaries & Constraints

**Always:**
- **Testgetrieben:** Zuerst entstehen die HTTP-Tests gegen echtes PostgreSQL mit den Pflichtfällen nach AD-30. Freizeit-Endpunkte: Leitung berechtigt; Team → 403; Org-Admin derselben Organisation ohne Rolle, fremde Organisation und unbekannte Freizeit → 404. Persönliche und Token-Endpunkte, die erweitert werden: Für `/api/ich/anmeldungen` und `/api/freizeiten/gast/{token}` gelten die bestehenden Pflichtfälle weiter, und neue Felder werden in der Antwortform geprüft.
- **Durchlauf-Tests** mit sofort abgearbeiteter Outbox und doppelter Zustellung (Idempotenz):
  - `AnmeldungBestaetigt` → Personen (Freigabe Stufe Team) und Organisationen (Mitgliedschaft, nur Mitarbeitende);
  - `AnmeldungAbgesagt` → Personen (Freigabe beendet);
  - `FreizeitRolleGeaendert` Team und Entzug → Freizeit-Rollenliste in Personen.
- **Status nur über Methoden** (AD-28):
  - Eingegangen → Bestätigt;
  - Eingegangen oder Bestätigt → Abgesagt.
  - Jeder andere Übergang ist ein 409 mit Problemcode `konflikt`. Abgesagt ist endgültig.
  - Bestätigen und Absagen sperren wie das Anlegen mit `AnmeldeSchritte.Sperre(freizeitId, personId)`, damit doppelte Klicks kein zweites Ereignis erzeugen.
- **Mails** (AD-13): Bestätigungs- und Absage-Mail gehen nach dem Knopfdruck der Leitung über die Outbox. Der Auftrag `AnmeldungEntschiedenMail(AnmeldungId, Entscheidung)` trägt nur Kennung und Enum. Adresse, Namen und Text entstehen erst beim Versand. Empfänger:
  - Person mit Konto → deren Konto-Mail;
  - sonst der zuerst eingetragene Elternteil der Familie mit Konto;
  - sonst die Mail des Gast-Zugangs der Familie;
  - ohne Adresse oder bei Platzhalter-Person → keine Mail (`null`).
- **Eine Absage** beendet die Freigabe sofort (`GueltigBis = jetzt`, die Zeile bleibt) und entzieht eine Team-Rolle, die durch die Bestätigung entstanden ist. Eine Leitungs-Rolle bleibt unberührt.
- **Kontakt für Absagen:** neue Freizeitseite-Felder `KontaktMail` (≤ 254, muss `@` enthalten) und `KontaktTelefon` (≤ 40), gepflegt per Autosave in den Einstellungen (nur Leitung).
- **Personendaten** bleiben in Personen. Freizeiten liest Namen über `IPersonenNamen` und Mail-Empfänger über einen neuen Vertrag in `Personen.Contracts` (Pfeil Freizeiten → Personen). Ereignisse tragen nur Kennungen, Enums und Datum.
- **Löschen und Auskunft** (AD-16):
  - In Freizeiten nennt die Auskunft zusätzlich Status und „entschieden am“.
  - `Mitgliedschaft` ist personeneigen: Sie wird bei `PersonGeloescht` gelöscht und erscheint in der Auskunft von Organisationen („Mitarbeitende von {Organisation} seit {Datum}“).
- **Texte** auf Deutsch ohne Du/Sie. Status-Wort mit Quadrat: Eingegangen = offen, Bestätigt = Erfolg, Abgesagt = Fehler.
- **Fertig erst mit** Doku-Seite unter Leitung, angepassten Seiten für Eltern und Mitarbeitende, Demo-Daten, Screenshot-Tests (Handy und Laptop mit axe) und fehlerfreiem Doku-Build (NFR-20).

**Never:**
- Keine Warteliste und kein automatisches Nachrücken; Nachrücken heißt: Die Leitung bestätigt eine eingegangene Anmeldung. Kein Absage-Knopf für Eltern oder Mitarbeitende.
- Kein Sperren des Bestätigens bei voller Freizeit (die Leitung entscheidet; der Balken zeigt die Überschreitung).
- Keine Mail ohne Knopfdruck der Leitung, keine Mail mit Token aus der Outbox, keine Mail an beide Elternteile.
- Keine Spine-Änderung, keine neue Zeile im Ereignis-Fluss. Keine neue Org-Rolle in `OrgRolle`, keine Änderung an `/api/ich/organisationen`.
- Kein Telefonfeld am Profil, keine Konto-Mail der Leitung als Kontakt.
- Kein Freizeitpass, kein Cockpit, kein Nachweis-Status auf der Anmeldungen-Seite (spätere Stories).
- Kein Schreiben fremder Module, keine Fremdschlüssel über Schemagrenzen, kein JavaScript und kein Pop-up.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Liste lesen | GET `/api/freizeiten/{id}/anmeldungen` (Leitung) | `{hoechstzahlTeilnehmende, bestaetigteTeilnehmende, anmeldungen[]}`. Jede Zeile: `anmeldungId, personId, vorname, nachname, art, status, eingegangenAm, entschiedenAm?`, sortiert nach `eingegangenAm`. Bei einer Platzhalter-Person Vorname „Gelöschte Person“ | Team → 403; ohne Rolle, fremde Org, unbekannte Freizeit → 404 |
| Bestätigen Teilnehmende | POST `…/anmeldungen/{aid}/bestaetigen`, Status Eingegangen | 204. Status Bestätigt, `EntschiedenAm`, `AnmeldungBestaetigt` und Mail-Auftrag. Nach der Zustellung: Freigabe Stufe 2 (Team) und eine Mail an Katrin, Betreff „Anmeldung bestätigt: {Freizeit}“, Text „{Vorname} ist bei {Freizeit} dabei.“. Keine Mitgliedschaft | – |
| Bestätigen Mitarbeitende | MA-Anmeldung von Jonas | Wie oben, Mail-Text „{Vorname} arbeitet bei {Freizeit} mit.“. Dazu `freizeiten.freizeit_rolle` Team, Personen-Rollenliste Team, Nachweis-Einsicht angelegt, `organisationen.mitgliedschaft` (Org, Jonas). `/api/ich/freizeiten` von Jonas zeigt die Freizeit, `FreizeitStart` lädt als Team | – |
| Mitarbeitende ist schon Leitung | MA-Anmeldung einer Leitung bestätigen | Bestätigt, Mitgliedschaft angelegt, Rolle bleibt Leitung (kein Team-Ereignis) | – |
| Absage | POST `…/absagen`, Status Eingegangen oder Bestätigt | 204. Status Abgesagt, `AnmeldungAbgesagt` und Mail „Anmeldung abgesagt: {Freizeit}“ mit Kontakt der Leitung, falls hinterlegt. Freigabe `GueltigBis` = Zeitpunkt der Zustellung. Bei bestätigten Mitarbeitenden: Team-Rolle entfernt, `FreizeitRolleGeaendert(…, Entzogen)`, Personen-Rollenzeile gelöscht. Mitgliedschaft bleibt | – |
| Ungültiger Übergang | Bestätigen bei Bestätigt/Abgesagt, Absagen bei Abgesagt | keine Änderung, kein Ereignis, keine Mail | 409 `konflikt` |
| Fremde Anmeldung | `aid` gehört zu Freizeit Y, Route Freizeit X (gleiche Leitung) | keine Änderung | 404 |
| Ausgebucht | Höchstzahl 1, eine Teilnehmende bestätigt | Freizeitseite zeigt „Ausgebucht“ (`IBestaetigteTeilnehmende` zählt bestätigte Teilnehmende). Mitarbeitende zählen nicht. Bestätigen einer weiteren bleibt möglich (204) | – |
| Gast nach Bestätigung | Gast-Kind bestätigt | Mail an die Gast-Mail. Gast-Link-Seite zeigt Status „Bestätigt“ und „Absage nur über die Leitung: {Mail} · {Telefon}“ | – |
| Gast nach Absage | einzige Anmeldung der Gast-Familie abgesagt | Mail an die Gast-Mail. Der Gast-Link gilt nicht mehr (404). Der Löschlauf löscht die Gast-Familie | – |
| Meine Anmeldungen | GET `/api/ich/anmeldungen` | Zeilen zusätzlich mit `kontaktMail?, kontaktTelefon?`. Status Eingegangen, Bestätigt oder Abgesagt | bestehende Pflichtfälle |
| Kontakt pflegen | PATCH Freizeitseite-Feld `kontaktMail` / `kontaktTelefon` (Leitung) | gespeichert, leerer Wert löscht | ohne `@` oder zu lang → 400; Team → 403 |
| App-Seite | `/app/freizeiten/{id}/anmeldungen` (Leitung) | H1 „Anmeldungen“. Balken „Plätze“ mit Zahl „{n} von {max}“ und Statuswort belegt, ausgebucht (Warnung) oder überbelegt (Fehler); ohne Höchstzahl „{n} bestätigt“. Abschnitte „Teilnehmende“ und „Mitarbeitende“ als Datentabelle (Name, Eingegangen, Status, Aktion). Eingegangen: Knopf „Bestätigen“ und Inline-Bestätigung „Absage eintragen“ mit Satz „Absage für {Vorname} eintragen? Eine Absage-Mail geht raus.“ und „Ja, Absage eintragen“. Bestätigt: nur die Absage. Danach Meldung „{Vorname} ist bestätigt. Eine Mail geht raus.“ bzw. „Absage für {Vorname} eingetragen. Eine Mail geht raus.“, und die Liste lädt neu. Leer: „Noch keine Anmeldungen.“ | 403/404 → `NichtVerfuegbar`; 409 → Meldung „Diese Anmeldung wurde schon entschieden.“ und Neuladen |
| Familienübersicht | Katrin, Emma bestätigt | Status „Bestätigt“. Unter jeder nicht abgesagten Anmeldung „Absage nur über die Leitung:“ mit `mailto:`- und `tel:`-Link; ohne Kontakt „Absage nur über die Leitung der Freizeit.“ | – |

</intent-contract>

## Code Map

Abkürzungen: **Frz** = `src/Modules/Freizeiten/Zeltlotse.Freizeiten`, **FrzC** = `…/Zeltlotse.Freizeiten.Contracts`, **Per** = `src/Modules/Personen/Zeltlotse.Personen`, **Org** = `src/Modules/Organisationen/Zeltlotse.Organisationen`.

- `Frz/Domaene/Anmeldung.cs`:
  - Enum `AnmeldeStatus` (Z.~16, nur `Eingegangen = 1`) um `Bestaetigt = 2, Abgesagt = 3` erweitern.
  - Methoden `Bestaetigen(jetzt)` und `Absagen(jetzt)` geben `bool` zurück. Sie setzen `EntschiedenAm`, legen das Ereignis ab und rufen `MailVormerken`.
  - Fabrik `Eingehen` (Z.~50) ist das Vorbild.
- `FrzC/Events/AnmeldungEingegangen.cs` -- Vorbild für die neuen Ereignisse im selben `.Events`-Namensraum:
  - `AnmeldungBestaetigt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art)`;
  - `AnmeldungAbgesagt(Guid AnmeldungId, Guid FreizeitId, Guid PersonId, AnmeldungArt Art)`.
  - Die Namen müssen exakt den Spine-Zeilen entsprechen.
- `FrzC/Events/FreizeitRolleGeaendert.cs` -- `FreizeitRolleArt` um `Entzogen = 3` erweitern.
- `Frz/Domaene/Freizeit.cs:89-99` -- `LeitungEinsetzen` ist das Muster für neue Methoden:
  - `TeamEinsetzen(personId, jetzt)` gibt `false` zurück, wenn die Person schon eine Rolle hat. Der Schlüssel ist (Freizeit, Person).
  - `TeamEntziehen(personId)` entfernt nur Team.
  - Beide legen `FreizeitRolleGeaendert` ab und brauchen geladene `Rollen`.
- `Frz/Domaene/FreizeitRolle.cs` -- Entität `Vergeben(...)`.
- `Frz/Anmeldungen/AnmeldungAbsenden.cs` -- `AnmeldeSchritte.Sperre` (Advisory Lock) und das Muster für Commands (`IModulCommand<FreizeitenDbContext>`, `IBenutzerCommand`) und Endpunkte.
- `Frz/Anmeldungen/AnmeldungenLesen.cs`:
  - `AnmeldungWerte.Status` (Z.25-29) erweitern.
  - `MeineAnmeldungenEndpunkt` und `GastLinkEndpunkt` liefern die Kontaktfelder mit.
- `Frz/Anmeldungen/EingangsMail.cs` -- Stil der Mail-Texte (Betreff als Konstante, `OeffentlicheAdresse.Lesen`).
- `Per/Nachweise/NachweisErinnerung.cs` -- Referenz für Mail-Aufträge:
  - `record … : IMailAuftrag`, `MailVormerken`, `IMailAufbau<T>`;
  - Registrierung `services.VerschicktMail<…>()` in `PersonenModul.cs:44`.
  - Neu ist `AnmeldungEntschiedenMail` mit Aufbau in Freizeiten. Das Enum `Entscheidung { Bestaetigt = 1, Abgesagt = 2 }` liegt neben dem Auftrag.
- `src/Zeltlotse.SharedKernel/Mail/MailAuftraege.cs` -- `MailInhalt(An, Betreff, Text)`: genau eine Adresse.
- `Frz/Freizeitseite/OeffentlicheEndpunkte.cs:18-30` -- Den Stub `KeineBestaetigtenTeilnehmenden` durch eine echte Zählung ersetzen (Teilnehmende, Bestätigt, unter Mandantenfilter). Registriert ist er in `FreizeitenModul.cs:139`.
- `Frz/Freizeitseite/FreizeitseiteBearbeiten.cs`, `src/Zeltlotse.Api.Contracts/Freizeiten/Freizeitseite.cs` -- `FreizeitseiteFeld` um `KontaktMail` und `KontaktTelefon` erweitern. Felder an `Freizeit`, Validator.
- `Frz/FreizeitenModul.cs:155-190` -- `freizeit.MapGet("/anmeldungen")` und die zwei POSTs mit `.NurFreizeitRolle(FreizeitRechte.Anmeldungen)`.
- `Frz/Rechte/FreizeitRechte.cs`:
  - `Anmeldungen = [Leitung]` und `Erlaubt` (Z.173-187) ergänzen.
  - Neuer Schlüssel `Aktionsschluessel.AnmeldungenBearbeiten = "anmeldungen-bearbeiten"` in `src/Zeltlotse.Api.Contracts/Gemeinsam/FreizeitKontext.cs:55-85`.
- `Frz/Daten/FreizeitenDbContext.cs` -- Spalten `EntschiedenAm`, `kontakt_mail`, `kontakt_telefon`. Migration `AnmeldungEntscheidung` in `Frz/Migrationen/` (nach `20261012130853_Anmeldung`).
- `Frz/Datenschutz/FreizeitenDatenschutz.cs` -- In der Auskunft Status und „entschieden am“ ergänzen. Die Switches werfen bei unbekanntem Wert.
- `Per/Freigaben/Freigabe.cs`:
  - `FreigabeStufe.Team = 2` ergänzen; `Stufe` und `GueltigBis` werden `private set`.
  - Methoden `ZuTeam()` und `Beenden(jetzt)`.
  - Neue Empfänger `FreigabeTeamSetzen` (AnmeldungBestaetigt) und `FreigabeBeenden` (AnmeldungAbgesagt) nach dem Muster `FreigabeAnlegen`. Ohne Freigabe-Zeile tun sie nichts.
  - Registrierung in `Per/PersonenModul.cs:42-44`.
- `Per/Gaeste/GastZugang.cs:51-59`, `GastFamilie.cs:16-26` -- Die Gültigkeit ist das Maximum von `GueltigBis`. Mit `Beenden` endet der Gast-Link ohne weitere Änderung.
- `Per/Freizeitrollen/FreizeitRolleUebernehmen.cs` -- `Entzogen` löscht die Zeile. Team legt sie an oder setzt sie, wie heute einschließlich `EinsichtSicherstellen`.
- `Per/Familien/Familie.cs:27-37` (`FamilienElternteil`), `GastZugang.Mail`, `IKontoLesen.MailDerPersonAsync` (`src/Modules/Konten/Zeltlotse.Konten.Contracts/Konten/IKontoLesen.cs`):
  - Daraus entsteht der neue Vertrag `src/Modules/Personen/Zeltlotse.Personen.Contracts/Anmeldung/IAnmeldeKontakt.cs` mit `Task<string?> MailAsync(Guid personId, CancellationToken)` (Regel unter Always).
  - Umsetzung in `Per/Gaeste/` oder `Per/Familien/`.
  - Der Vertrag liest über Organisationen hinweg; der Zweck ist der Mail-Versand.
- `Org/OrganisationenModul.cs:35-36`, `Org/Nachweise/NachweisCommands.cs` (`NachweisBestaetigungSpeichern`) -- Muster für den Empfänger `MitgliedschaftAnlegen`:
  - Er nimmt den Umschlag-Mandanten, ignoriert Teilnehmende und ist idempotent.
  - `Org/Zeltlotse.Organisationen.csproj` braucht eine Referenz auf `Freizeiten.Contracts`. Ereignis-Typen sind von den Pfeilen ausgenommen (`AbhaengigkeitsRegeln.cs:129`).
  - Neue Entität `Org/Domaene/Mitgliedschaft.cs` (`IOrganisationsEigen`, Schlüssel (Org, Person), `SeitAm`).
  - DbSet und Migration `Mitgliedschaft` in `Org/Migrationen/`, Löschung in `OrgPersonLoeschen`, Auskunft im Datenschutz von Organisationen.
- `src/Zeltlotse.Api.Contracts/Freizeiten/Anmeldungen.cs`:
  - `AnmeldungStatus` um `Bestaetigt, Abgesagt` erweitern.
  - Neu: `AnmeldungenUebersicht`, `LeitungAnmeldungZeile`.
  - `AnmeldungZeile` und `GastLinkAnmeldung` bekommen `KontaktMail?` und `KontaktTelefon?`.
- `src/Zeltlotse.Ui/Api/FreizeitenApiClient.cs` -- `AnmeldungenAsync`, `BestaetigenAsync`, `AbsagenAsync`.
- `src/Zeltlotse.Ui/Texte/Anzeige.cs:117-126` -- `Status` und `AnmeldungStatusArt` erweitern.
- `src/Zeltlotse.App/Features/Personen/NachweisePruefen.razor` -- Muster für die Leitungsliste: `Datentabelle`, `LeerZustand`, `Ladezustand`, `NichtVerfuegbar`, `Meldung`. Die Bausteine `Balken`, `InlineBestaetigung` und `Status` liegen in `src/Zeltlotse.Ui.Komponenten/`.
- `src/Zeltlotse.App/Features/Freizeiten/FreizeitStart.razor:~52` -- Knopf „Anmeldungen“ neben „Nachweise prüfen“, gesteuert über `Darf(…AnmeldungenBearbeiten)`.
- `src/Zeltlotse.App/Features/Freizeiten/Einstellungen.razor` -- Abschnitt „Kontakt für Absagen“ mit zwei Autosave-Feldern und dem Hinweis „Erscheint bei Eltern und Mitarbeitenden neben dem Anmeldestatus.“
- `src/Zeltlotse.App/Features/Personen/FamilieAnsicht.razor:~221-226` und `src/Zeltlotse.Public/Seiten/GastLink.razor` -- Status und Kontaktzeile.
- `tests/Zeltlotse.Api.Tests/FreizeitAnmeldungTests.cs` und die Helfer `AnmeldeHelfer`, `FreizeitHelfer`, `OrgHelfer`, `FamilienHelfer`:
  - Muster für den Aufbau. Team per SQL-Insert in `freizeiten.freizeit_rolle` (Rolle 2).
  - Mail-Prüfung: `OutboxJetztZustellenAsync`, dann `Mails.An(...)`. Outbox-Inhalt ohne `@` und ohne Namen (Muster `NachweisWarnungTests.cs:160-200`).
  - 403/404-Matrix nach `FreizeitseiteTests.cs:118-130`.
- `tests/Zeltlotse.Ablaeufe.Tests/AnmeldungEingegangenTests.cs` -- Vorlage für die Durchlauf-Tests (SQL auf `personen.freigabe`, `personen.inbox`, `…outbox_zustellung`).
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs:~143-185` -- Die drei neuen Routen kommen in die Freizeit-Routenliste (Kommentar „Story 2-3“).
- `tests/Zeltlotse.Freizeiten.Tests/FreizeitseiteTests.cs:21` -- Der Kommentar zum Stub wird hinfällig.
- `tests/Zeltlotse.Ui.Tests/NachweisSeitenTests.cs`, `NachgebildeterHandler` -- Muster für bUnit.
- `src/Zeltlotse.Api/Demo/{DemoKatalog,DemoDaten}.cs` (`AnmeldungAsync` Z.420-451) und `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs` -- Zähler anpassen (`freizeiten.outbox`, `freizeit_rolle`, `anmeldung`, `freigabe` und weitere).
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- Muster `Nachweise_Pruefen` mit `PruefListeAsync` (Miriam, `PruefMail`) und `BildAsync`. Screenshots dürfen die Demo-Daten nicht ändern.
- `doku/src/content/docs/leitung/`:
  - Die neue Seite `anmeldungen.md` braucht die Sidebar-Reihenfolge der bestehenden Seiten.
  - `leitung/index.md:16` verlinkt sie.
  - Weitere Anpassungen: `eltern/kind-anmelden.md:21,37`, `eltern/freizeitseite.md:27`, `mitarbeitende/anmelden.md:8`, `leitung/freizeitseite-einrichten.md:27,41`, `daten.md:17-18`.
  - `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs` `PflichtSeiten` bekommt `leitung/anmeldungen`.

## Tasks & Acceptance

**Execution:**
- `tests/Zeltlotse.Api.Tests/AnmeldungEntscheidenTests.cs` (neu, zuerst) -- Testgetrieben. HTTP-Tests für alle Matrixzeilen:
  - 403/404-Pflichtfälle für die drei Endpunkte und das PATCH;
  - Übergänge und 409; Anmeldung einer fremden Freizeit 404;
  - Mail-Betreff, Mail-Text und Empfänger (Konto, Elternteil, Gast, Kontakt in der Absage-Mail); Outbox ohne Adresse und Namen;
  - Zählung „ausgebucht“; Kontaktfelder in `/api/ich/anmeldungen` und im Gast-Link;
  - Gast-Link 404 nach Absage; Team-Rolle und `/api/ich/freizeiten` nach Bestätigung und Absage von Mitarbeitenden;
  - Auskunft von Freizeiten und Organisationen.
- `tests/Zeltlotse.Ablaeufe.Tests/{AnmeldungBestaetigtTests,AnmeldungAbgesagtTests}.cs` (neu, zuerst) -- Testgetrieben. Freigabe Stufe 2 bzw. beendet, Mitgliedschaft nur bei Mitarbeitenden, Rollenliste in Personen, Inbox-Idempotenz.
- `src/Modules/Freizeiten/...` -- Domäne, Ereignisse, Mail-Auftrag und -Aufbau, Slices `Anmeldungen/Entscheiden/` (Liste, Bestätigen, Absagen), Zählung, Kontaktfelder, Rechte, Auskunft, Migration -- Kern.
- `src/Modules/Personen/...` -- `FreigabeStufe.Team`, Freigabe-Methoden, zwei Empfänger, `Entzogen` in der Rollenliste, `IAnmeldeKontakt` und seine Umsetzung, Tests in `tests/Zeltlotse.Personen.Tests` (Empfängerregel für Mails, Beenden mit künstlicher Uhr) -- Freigabe gehört Personen (AD-8).
- `src/Modules/Organisationen/...` -- `Mitgliedschaft`, Empfänger, Projektreferenz, Löschung, Auskunft, Migration -- FR-7.
- `src/Zeltlotse.Api.Contracts/…`, `src/Zeltlotse.Ui/…` -- DTOs, Enums, Aktionsschlüssel, Client-Methoden, `Anzeige` -- AD-5.
- `src/Zeltlotse.App/Features/Freizeiten/Anmeldungen.razor` (neu), `FreizeitStart.razor`, `Einstellungen.razor`, `Features/Personen/FamilieAnsicht.razor`, `src/Zeltlotse.Public/Seiten/GastLink.razor` -- Oberfläche laut Matrix.
- `tests/Zeltlotse.Ui.Tests/` -- bUnit für die Seite „Anmeldungen“ (Balken-Zustände, Bestätigen, Inline-Absage, 409, Leer, 403), die Kontaktzeile der Familienübersicht und die Client-Methoden.
- `tests/Zeltlotse.Api.Tests/EndpunktListeTests.cs` -- neue Routen eintragen.
- `src/Zeltlotse.Api/Demo/` und `DemoDatenTests.cs` -- Demo-Daten (idempotent, über Commands):
  - Kontakt `zeltlager@example.org`, Telefon `0123 456789`;
  - Daniel meldet Mia an, Miriam bestätigt;
  - Lea meldet sich über den Mitarbeitenden-Link an, Miriam bestätigt; Lea wird Team und Mitglied;
  - Tobias meldet sich als Mitarbeitender an und bleibt eingegangen;
  - Emma bleibt eingegangen.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs` -- neue Theories für Handy und Laptop mit axe:
  - `anmeldungen` (Miriam, Liste mit allen drei Status);
  - `anmeldung-absage` (Inline-Bestätigung bei Tobias aufgeklappt, nicht bestätigt);
  - `kontakt-fuer-absagen` (Einstellungen, Abschnitt Kontakt).
  - Bestehende Familien- und Gast-Link-Screenshots zeigen jetzt die Kontaktzeile.
- `doku/src/content/docs/leitung/anmeldungen.md` (neu) und die Anpassungen laut Code Map, `DokuRegeln.PflichtSeiten` -- NFR-20.

**Acceptance Criteria:**
- Given die Demo-Daten und Miriam als Leitung, when sie „Anmeldungen“ öffnet, then sieht sie den Balken „1 von 40“ Plätzen, Emma „Eingegangen“, Mia „Bestätigt“, Lea „Bestätigt“ und Tobias „Eingegangen“, und Bestätigen bzw. „Absage eintragen“ nur an den erlaubten Zeilen.
- Given eine eingegangene Anmeldung von Emma, when die Leitung bestätigt und die Outbox zustellt, then bekommt Katrin genau eine Mail „Anmeldung bestätigt: …“, und ihre Familienübersicht zeigt „Bestätigt“ mit dem Kontakt der Leitung.
- Given bestätigte Mitarbeitende, when die Leitung absagt, then lädt die Person die Freizeit nicht mehr (404), `/api/ich/freizeiten` enthält sie nicht mehr, und die Mitgliedschaft bleibt.
- Given ein Team-Mitglied, when es die Anmeldungen der Freizeit lesen oder entscheiden will, then antwortet die API mit 403, und die App zeigt keinen Knopf „Anmeldungen“.
- Given `dotnet test` und `npm run build` in `doku/`, when beide laufen, then sind sie fehlerfrei, und die sechs neuen Screenshots existieren und sind verlinkt.

## Spec Change Log

## Review Triage Log

### 2026-10-12 — Review pass
- verdicts: 30 findings — high 0, medium 0, low 26, false 4, maybe-false 0
- findings:
  - `[low]` `[patch]` (Verification) Hinweis „Eine Absage geht nur über die Leitung …“ in der Bestätigungs-Mail ungetestet — Test um beide Wortlaute ergänzt (mit und ohne Kontakt), fehlenden Punkt ergänzt.
  - `[low]` `[patch]` (Verification) Gast-Link verbirgt den Kontakt bei abgesagter Anmeldung ungetestet — HTTP-Test: Gast-Familie mit zwei Kindern, eines abgesagt, Link bleibt gültig, Kontakt nur bei der nicht abgesagten Anmeldung.
  - `[low]` `[patch]` (Edge) `SingleAsync` nach der Sperre ergibt 500, wenn die Anmeldung dazwischen verschwindet — `SingleOrDefaultAsync` mit `NichtGefunden`.
  - `[low]` `[reject]` (Edge) Gleichzeitige Personen-Löschung ohne Sperre trägt die echte PersonId ins Ereignis — nur bei gleichzeitiger Konto-Löschung im selben Augenblick; Folge ist eine Mitgliedschaftszeile nur mit Kennung; Fix bräuchte eine Sperre im Lösch-Empfänger.
  - `[low]` `[reject]` (Edge) Entscheiden nach Freizeitende/Abschluss möglich — `AbgeschlossenAm` setzt diese Version nirgends; der Schreibschutz kommt mit dem Abschluss (AD-17, AD-28-Pipeline); in der Nachbereitung ist eine Entscheidung harmlos.
  - `[low]` `[patch]` (Edge) Telefon ohne Ziffern ergibt leeren `tel:`-Link — `AbsageKontakt` zeigt dann reinen Text; Validator verlangt mindestens eine Ziffer.
  - `[low]` `[patch]` (Edge) Telefon ohne Ziffernprüfung — gruppiert mit dem vorigen.
  - `[low]` `[patch]` (Edge) Kontakt-Mail nur mit „@“ geprüft — jetzt `MailAdresse.IstGueltig` wie bei Einladungen.
  - `[low]` `[reject]` (Edge) Absage-Mail an Gäste geht verloren, wenn der Löschlauf vor der Zustellung läuft — Outbox stellt in Sekunden zu, Löschlauf täglich; nur bei stundenlangem Mail-Ausfall; Fix bräuchte modulübergreifendes Warten (wie in 2-2 verworfen).
  - `[false]` `[reject]` (Edge) Mail-Aufbau wirft, wenn die Freizeit fehlt — Freizeiten werden nie gelöscht, solange Anmeldungen bestehen; Mandant kommt aus dem Umschlag derselben Organisation.
  - `[low]` `[reject]` (Blind) Erneute Anmeldung nach Absage meldet „schon angemeldet“ — so in den Design Notes festgelegt (Abgesagt endgültig); eigener Text bräuchte neuen Zweig.
  - `[low]` `[reject]` (Blind) Absage-Mail an Gäste kann verloren gehen — wie Edge-Befund oben.
  - `[low]` `[reject]` (Blind) Freigabe endet zur Zustellung statt zum Absagezeitpunkt — so in der Matrix festgelegt; Abstand Sekunden.
  - `[low]` `[patch]` (Blind) Doku und Code beschreiben den Mail-Empfänger unterschiedlich — einheitlicher Wortlaut in `eltern/kind-anmelden.md` und `leitung/anmeldungen.md`.
  - `[low]` `[reject]` (Blind) Nach Absage bleibt die automatisch angelegte Nachweis-Einsicht — sie zeigt nichts ohne Freigabe der Person; Aufräumen wäre neue Logik in Personen.
  - `[low]` `[patch]` (Blind) Kontaktfelder kaum geprüft — gruppiert mit den Edge-Befunden zur Prüfung.
  - `[low]` `[reject]` (Blind) „Eine Mail geht raus“ auch ohne Empfänger — nur bei gelöschten Personen; die Seite kennt den Empfänger nicht; Fix bräuchte neues Antwortfeld.
  - `[low]` `[reject]` (Blind) Wer entschieden hat, wird nicht gespeichert, Datum nicht gezeigt — von Matrix und FR nicht verlangt; neues Feld mit Personenbezug wäre zusätzliche Lösch-Logik.
  - `[low]` `[reject]` (Blind) Entscheiden nach Freizeitende — wie Edge-Befund oben.
  - `[low]` `[reject]` (Blind) Knöpfe je Zeile ohne Personennamen im zugänglichen Namen — Zeilenkontext der Datentabelle ordnet zu, axe-Prüfung grün; ein Name im Auslöser bräuchte einen neuen Parameter der Inline-Bestätigung.
  - `[low]` `[patch]` (Blind) Doku nennt „Ohne Höchstzahl“ nicht — ergänzt.
  - `[low]` `[patch]` (Blind) Szenarien ohne Test: Gast-Familie mit einer abgesagten von zwei Anmeldungen — gruppiert mit dem Gast-Link-Test; gelöschte Person und zweiter Elternteil: Liste mit Platzhalter getestet, ein Empfänger ist Design-Entscheidung.
  - `[low]` `[patch]` (Blind) Demo-Katalog führt Emmas Anmeldung doppelt (`MitKontoAngemeldet`) — entfernt, Tests lesen `Anmeldungen[0]`.
  - `[low]` `[patch]` (Blind) Fehlender Punkt in der Bestätigungs-Mail — gruppiert mit dem ersten Verification-Befund.
  - `[low]` `[reject]` (Intent) Mail nur an einen Empfänger — Design-Entscheidung (`MailInhalt` mit genau einer Adresse); beide Eltern sehen den Status.
  - `[low]` `[reject]` (Intent) Kontakt der Leitung als Feld der Freizeit statt Profil — Design-Entscheidung (kein Telefon am Profil, keine Konto-Mail ohne Zutun).
  - `[false]` `[reject]` (Intent) Mitgliedschaft nur in der Datenschicht, „weitere Freizeit verlangt nur Bestätigung“ ungeprüft — einen Schritt „Organisation beitreten“ gibt es nicht; jede Mitarbeit braucht ohnehin nur die Bestätigung der Leitung, das ist getestet.
  - `[false]` `[reject]` (Intent) Token-Fälle nicht neu getestet — die Pflichtfälle der unveränderten Token-Endpunkte bestehen aus 2-2 und laufen weiter; neu ist nur das Ende durch Absage, das getestet ist.
  - `[low]` `[reject]` (Intent) Screenshot-Tests blenden Gast-Zeilen anderer Tests aus — nur Bildstabilität; die Seite wird vorher vollständig mit axe geprüft; eine eigene Demo-Instanz je Test wäre deutlich aufwendiger.
  - `[false]` `[reject]` (Intent) Testgetrieben im Diff nicht sichtbar — Prozessangabe, keine Fehlwirkung im Code.

## Design Notes

Entscheidungen im autonomen Lauf (einfachste Lösung, Spec und Spine lassen sie offen):

- **Kontakt der Leitung als Feld der Freizeit:** FR-21 verlangt Mail und Telefon der Leitung. Ein Telefon gibt es am Profil nicht, und die Konto-Mail der Leitung ohne deren Zutun zu zeigen wäre ein Datenschutzproblem. Die Leitung trägt deshalb selbst ein, was Eltern sehen sollen, so wie die Teilnahmebedingungen aus 2-2.
- **Eine Mail, ein Empfänger:** `MailInhalt` kennt genau eine Adresse. Beide Elternteile sehen den Status in der Familienübersicht. Ein Mail-Versand an beide bräuchte einen Umbau des Mail-Kerns.
- **Bestätigen ist direkt, Absagen inline bestätigt:** So steht es in den UX-Pflichtfällen für die Inline-Bestätigung. Der Knopf „Bestätigen“ ist selbst die ausdrückliche Aktion nach AD-13.
- **Abgesagt ist endgültig:** Eine neue Anmeldung derselben Person zu derselben Freizeit bleibt durch den eindeutigen Index ausgeschlossen („schon angemeldet“). Das ist einfacher als eine Reaktivierung, und die Leitung entscheidet ohnehin selbst.
- **Freigabe beenden statt löschen:** `GueltigBis = jetzt`. Die Zeile bleibt für Auskunft und Idempotenz. Eine erneute Zustellung von `AnmeldungEingegangen` legt nichts neu an. Der Gast-Link endet damit automatisch (Maximum der Freigaben). Der Gast erfährt die Absage aus der Mail.
- **Team-Rolle aus der Bestätigung:** „Rechte entstehen erst mit der Bestätigung“ (AD-11) gilt für Mitarbeitende als Freizeit-Rolle Team. Das geschieht in Freizeiten selbst, in derselben Transaktion; Personen erfährt es wie bei der Leitung über `FreizeitRolleGeaendert`. Für den Entzug bekommt das bestehende Ereignis den Enum-Wert `Entzogen`. Es entsteht keine neue Spine-Zeile.
- **Mitgliedschaft ohne Oberfläche:** FR-7 verlangt nur, dass die Person Mitarbeitende der Organisation wird. Sichtbar ist die Mitgliedschaft in der Auskunft. Eine Mitgliederliste für Org-Admins ist nicht verlangt. Eine Absage beendet die Mitgliedschaft nicht.
- **Volle Freizeit:** Das Bestätigen wird nicht gesperrt. Der Balken zeigt „überbelegt“, die Leitung entscheidet (keine Speziallogik).

Entscheidungen bei der Umsetzung:

- **Spaltennamen** folgen der Schreibweise der bestehenden Tabellen (`EntschiedenAm`, `KontaktMail`, `KontaktTelefon`), nicht `kontakt_mail`.
- **„Zuerst eingetragener Elternteil“** heißt: das zuerst angelegte Profil unter den Eltern der Familie. `familien_elternteil` hat keinen Zeitstempel; eine neue Spalte wäre eine Datenänderung ohne Mehrwert.
- **Statuswörter des Balkens** stehen großgeschrieben wie alle Statuswörter („Belegt“, „Ausgebucht“, „Überbelegt“); ohne Höchstzahl lautet es „Ohne Höchstzahl“ zur Zahl „{n} bestätigt“.
- **Leere Abschnitte:** Hat ein Abschnitt (Teilnehmende oder Mitarbeitende) keine Anmeldung, zeigt er „Noch keine Anmeldungen.“.
- **Platzhalter:** Eine Anmeldung einer gelöschten Person lässt sich weiter entscheiden; sie bekommt keine Mail, keine Team-Rolle und keine Mitgliedschaft.
- **Demo-Mails:** Die Bestätigungen von Mia und Lea laufen über den Command und verschicken beim Einspielen je eine Mail (an Daniel und Lea).

## Verification

**Commands:**
- `dotnet build Zeltlotse.slnx --no-incremental` -- erwartet: 0 Fehler, 0 Warnungen.
- `dotnet test` (Repo-Wurzel) -- erwartet: alles grün, einschließlich Architektur-, Ablauf-, E2E- und Doku-Tests.
- `npm run build` in `doku/` -- erwartet: Exit 0.

**Manual checks (if no CLI):**
- Die sechs neuen PNGs in `doku/src/assets/screenshots/` zeigen die Liste, die aufgeklappte Absage und den Kontakt-Abschnitt.

## Auto Run Result

Status: done

**Umgesetzt:** Die Leitung bestätigt Anmeldungen oder trägt Absagen ein.
- Neue App-Seite „Anmeldungen“: Platz-Balken, Tabellen für Teilnehmende und Mitarbeitende, „Bestätigen“ und „Absage eintragen“ mit Inline-Bestätigung.
- Endpunkte nur für die Leitung: `GET /api/freizeiten/{id}/anmeldungen`, `POST …/bestaetigen`, `POST …/absagen`. Übergänge laufen über `Anmeldung.Bestaetigen/Absagen`; ein ungültiger Übergang gibt 409.
- `AnmeldungBestaetigt` → Personen setzt die Freigabe auf Stufe Team; Organisationen legt für Mitarbeitende eine `Mitgliedschaft` an (FR-7). `AnmeldungAbgesagt` → Personen beendet die Freigabe.
- Bestätigte Mitarbeitende werden Team der Freizeit. Eine Absage entzieht die Rolle wieder (`FreizeitRolleArt.Entzogen`).
- Bestätigungs- und Absage-Mail gehen über die Outbox (`AnmeldungEntschiedenMail`). Den Empfänger bestimmt `IAnmeldeKontakt`.
- „Ausgebucht“ zählt jetzt die bestätigten Teilnehmenden.
- Neu: Kontakt für Absagen (Mail, Telefon) in den Einstellungen. Familienübersicht und Gast-Link zeigen ihn neben dem Status.
- Demo-Daten erweitert, Doku-Seite `leitung/anmeldungen.md` neu, sechs neue Screenshots.

**Dateien (Auswahl):**
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten/Anmeldungen/Entscheiden/*`, `Domaene/{Anmeldung,Freizeit}.cs`, Migration `AnmeldungEntscheidung` -- Entscheiden, Liste, Mail-Auftrag, Zählung, Kontaktfelder.
- `src/Modules/Freizeiten/Zeltlotse.Freizeiten.Contracts/Events/AnmeldungEntschieden.cs` -- die neuen Ereignisse.
- `src/Modules/Personen/Zeltlotse.Personen/Freigaben/Freigabe.cs`, `Gaeste/AnmeldeKontaktLesen.cs`, `Freizeitrollen/FreizeitRolleUebernehmen.cs`, Contract `IAnmeldeKontakt` -- Freigabe Team/Beenden, Mail-Empfänger, Rollen-Entzug.
- `src/Modules/Organisationen/Zeltlotse.Organisationen/{Domaene/Mitgliedschaft,Mitglieder/MitgliedschaftAnlegen}.cs`, Migration `Mitgliedschaft` -- Mitgliedschaft, Löschen, Auskunft.
- `src/Zeltlotse.App/Features/Freizeiten/{Anmeldungen,FreizeitStart,Einstellungen}.razor`, `Features/Personen/FamilieAnsicht.razor`, `src/Zeltlotse.Public/Seiten/GastLink.razor`, `src/Zeltlotse.Ui.Komponenten/AbsageKontakt.razor` -- Oberfläche.
- Tests: `AnmeldungEntscheidenTests`, `AnmeldungBestaetigtTests`, `AnmeldungAbgesagtTests`, `AnmeldeKontaktTests`, `MitgliedschaftTests`, `AnmeldungenSeitenTests`, Screenshot-Theories.

**Review (30 Befunde: hoch 0, mittel 0, gering 26, falsch 4):**
- Patches (alle gering):
  - 404 statt 500, wenn eine Anmeldung zwischen Lesen und Sperre verschwindet.
  - Kontakt-Mail wird mit `MailAdresse.IstGueltig` geprüft, das Telefon braucht mindestens eine Ziffer, ohne Ziffern erscheint es als Text ohne `tel:`-Link.
  - Satzpunkt in den Mail-Texten.
  - Tests für die Absage-Zeile der Bestätigungs-Mail und für den Gast-Link mit einer abgesagten von zwei Anmeldungen.
  - Einheitlicher Doku-Wortlaut zum Mail-Empfänger; „Ohne Höchstzahl“ in der Doku.
  - Doppelter Demo-Eintrag `MitKontoAngemeldet` entfernt.
- Zurückgestellt: nichts.
- Verworfen: alle übrigen Befunde, Begründung im Review Triage Log.

**Folge-Review empfohlen: nein.** Gepatcht wurden nur geringe Befunde, kein hoher und kein mittlerer.

**Verifikation:**
- `dotnet build Zeltlotse.slnx --no-incremental`: 0 Fehler, 0 Warnungen.
- `dotnet test` nach den Patches: alle Tests grün, 0 fehlgeschlagen (einschließlich Architektur-, Ablauf-, bUnit-, E2E- und Doku-Screenshot-Tests).
- `npm run build` in `doku/`: 28 Seiten, fehlerfrei (nur die bekannte Sitemap-Warnung).
- Matrix-Audit: alle 14 Zeilen sind durch grüne Tests abgedeckt.
- Screenshots `anmeldungen-laptop` und `anmeldung-absage-handy` angesehen. `app-offline-*` und `vertrauensseite-laptop` waren nur Rendering-Rauschen und sind zurückgesetzt.

**Restrisiken:**
- Die Mail geht an genau einen Empfänger; ein zweiter Elternteil sieht den Status nur in der Familienübersicht.
- Eine Absage-Mail an Gäste kann verloren gehen, wenn der Mail-Versand länger ausfällt als bis zum nächsten Löschlauf.
- Entscheiden nach dem Abschluss der Freizeit wird erst mit dem Schreibschutz der Abschluss-Story gesperrt.
- Die Screenshot-Tests blenden Gast-Zeilen anderer Tests aus, damit die Bilder stabil bleiben.
