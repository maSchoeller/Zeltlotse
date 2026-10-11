# Rubrik-Prüfung – Architektur-Gerüst Zeltlotse

- **Geprüft:** `../ARCHITECTURE-SPINE.md` (Stand 2026-10-11, status: draft)
- **Abgeglichen mit:** PRD `prd-Zeltlotse-2026-10-10/prd.md` (FR-1 bis FR-69, NFR-1 bis NFR-19)
- **Prüfdatum:** 2026-10-11

## Urteil

Das Gerüst ist gut geschnitten und knapp. Die meisten echten Divergenzpunkte (Modulgrenzen, Datenbesitz, Outbox, Mandantenfilter, Render-Modus, Verschlüsselung, Offline) sind fixiert. **Noch nicht baufähig** ist es aus zwei kritischen Gründen: Die Abhängigkeitspfeile decken mehrere PRD-Abläufe nicht ab, die von den ADs selbst verlangt werden. Außerdem ist offen, woher die zentrale Rechteprüfung die Rollen „im Kontext“ bezieht. Dazu kommen einige Lücken mit hoher Schwere: Ereignis-Inhalte, Mandanten-Kontext außerhalb von Anfragen, Migrationen/Outbox-Technik, Monitoring.

Zählung: 2 kritisch · 7 hoch · 10 mittel · 8 niedrig.

---

## Kritisch

### K-1 · Rollen-Datenquelle für die zentrale Rechteprüfung fehlt; Personen kann die Freigabe nicht prüfen
- **Ort:** AD-12, AD-8, Abhängigkeitsdiagramm (keine Kante PER → FZ)
- **Problem:** AD-12 verlangt eine zentrale Autorisierungs-Richtlinie in `SharedKernel`, die die Rolle „im konkreten Kontext (Organisation bzw. Freizeit)“ prüft. Die Rollendaten liegen aber in Organisationen (Mitgliedschaft, Org-Rollen) und Freizeiten (Leitung/Team), und `SharedKernel` darf keine Module kennen. Es ist nicht festgelegt, wie die Richtlinie an diese Daten kommt. Ebenso muss Personen nach AD-8 prüfen, ob der Lesende *Leitung* oder *Team* der Freizeit X ist (Stufe der Freigabe). Personen darf Freizeiten aber nicht aufrufen (kein Pfeil PER → FZ). Agenten werden das unterschiedlich lösen: Rolle als Parameter vom Aufrufer (unsicher), Claims beim Login (veraltet), eigene Replik pro Modul oder Pfeil-Verletzung.
- **Fix:** Neue AD „Rollen-Auflösung“: Eine Schnittstelle `IKontextRollen` in `SharedKernel` (z. B. `RollenIn(personId, organisationId | freizeitId)`), implementiert im Host durch Zusammenführen aus `Organisationen.Contracts` und `Freizeiten.Contracts` und pro Anfrage zwischengespeichert. Personen prüft die Freigabe-Stufe über diese Schnittstelle und nicht über einen direkten Aufruf. Alternativ: Personen hält eine per Ereignis gepflegte Replik „Freizeit-Rollen“ (PersonId, FreizeitId, Rolle). Eine der beiden Varianten muss verbindlich sein.

### K-2 · Abhängigkeitspfeile decken von den ADs selbst verlangte Aufrufe nicht ab
- **Ort:** Abhängigkeitsdiagramm, AD-2, AD-17, AD-20, Datenbesitz-Tabelle
- **Problem:** Mehrere Pflichtaufrufe laufen gegen oder neben die Pfeile:
  1. **Küche → Freizeiten fehlt.** AD-17 („Phase liefert ausschließlich `Freizeiten.Contracts`“, Schreibschutz in *jedem* freizeitbezogenen Modul) und AD-20 (Modul-Schalter besitzt Freizeiten; Küche muss mit `404` antworten) zwingen Küche dazu, Freizeiten zu fragen. Auch FR-46 (Skalierung auf die Personenzahl der Freizeit = Anmeldungen) und FR-48 (Allergen-Abgleich gegen *alle* Freizeitpässe, also die Liste der Angemeldeten) brauchen Freizeiten-Daten. Ob AD-2 transitiv über Programm gilt, ist nicht gesagt; ArchUnit nach AD-24 wird den Direktaufruf ablehnen.
  2. **Programm → Organisationen fehlt.** *Aufgaben-Vorlagen* gehören laut Tabelle Organisationen. FR-40 verlangt aber, dass neue Freizeiten sie übernehmen (Programm liest) und Aufgaben in eine Vorlage zurückgeschrieben werden (Programm schreibt in Organisationen).
  3. **Sicherheit → Inventar (FR-32)** ist nur per Ereignis möglich. Dann greift aber Befund H-1 (Inventar kann die Details nicht nachladen).
- **Fix:** Pfeile `KUE --> FZ` und je nach Entscheidung `PRG --> ORG` ergänzen, oder *Aufgaben-Vorlage* in die Besitzliste von Programm verschieben (analog zur Programmbibliothek, die ebenfalls org-weit ist). Das ist die sauberere Variante. Für FR-32 entweder Pfeil `SIC --> INV` (Sicherheit trägt die Entnahme direkt über `Inventar.Contracts` ein) oder H-1 lösen. In AD-2 ausdrücklich festhalten: „Pfeile gelten nicht transitiv.“ Danach Diagramm, AD-2-Text und Mapping-Tabelle abgleichen.

---

## Hoch

### H-1 · Ereignisse „nur Kennungen“ und Rückrichtungsverbot machen Reaktionen gegen die Pfeile unmöglich
- **Ort:** AD-4, Konvention „Ereignisse“, AD-2
- **Problem:** Ereignisse sind der einzige Weg gegen die Pfeilrichtung. Die Konvention sagt „nur Kennungen als Inhalt“. Ein Empfänger, der gegen die Pfeile abonniert, darf den Absender nicht aufrufen und kann die Details deshalb nie nachladen. Beispiel FR-32: Inventar erhält „EntnahmeErfasst(id)“, darf aber Sicherheit nicht fragen, *was* entnommen wurde. Dasselbe gilt für FR-7 (Organisationen reagiert auf eine bestätigte Mitarbeit) und das Anlegen der Freigabe. AD-4 sagt außerdem „Kennungen, keine Personendaten“, die Konvention „nur Kennungen“. Die beiden Texte widersprechen sich.
- **Fix:** Regel vereinheitlichen: „Ereignisse tragen alle fachlichen Fakten, die Empfänger brauchen (Kennungen, Mengen, Daten, Statuswerte), aber **keine Personendaten** (Namen, Kontaktdaten, Freizeitpass-Antworten).“ Konvention und AD-4 identisch formulieren.

### H-2 · Mandanten-Kontext außerhalb einer angemeldeten Anfrage ist undefiniert
- **Ort:** AD-6
- **Problem:** Der globale Filter kommt „aus dem Mandanten-Kontext der Anfrage“, und `IgnoreQueryFilters` ist nur im Lösch-Dienst erlaubt. Es gibt aber weitere Ausführungswege ohne angemeldete Anfrage: Outbox-Zustellung und Ereignis-Handler, öffentliche SSR-Seiten (die Freizeitseite wird per Link ohne Anmeldung aufgerufen), der QR-Teilnehmerzugang und Gast-Anmeldungen. Außerdem fehlt die Regel, *woraus* der Mandant bei Personen mit Rollen in mehreren Organisationen bestimmt wird (Route? Ressource? Cookie?). Agenten werden Filter abschalten, Kontexte erfinden oder Handler scheitern lassen.
- **Fix:** In AD-6 ergänzen: (a) Der Mandant wird aus der adressierten Ressource bestimmt (Route-Parameter `organisationId` bzw. aus der Freizeit abgeleitet) und gegen die Rollen geprüft, nie aus einer „aktuellen Organisation“ im Cookie. (b) Ereignisse aus organisationseigenen Modulen tragen `OrganisationId`; der Outbox-Dienst setzt daraus den Kontext, bevor der Handler läuft. (c) Öffentliche Zugänge setzen den Kontext aus dem aufgelösten Token bzw. Slug. (d) Der Ausnahmekatalog für `IgnoreQueryFilters` bleibt geschlossen.

### H-3 · Besitz der Freigabe widersprüchlich formuliert; Ort des fixierten Intervalls offen
- **Ort:** AD-8 („Eine Freizeit hält nur eine Freigabe“), Datenbesitz-Tabelle (Freigabe → Personen), ER-Diagramm
- **Problem:** Der AD-Text lässt sich so lesen, dass Freizeiten die Freigabe speichert, die Tabelle ordnet sie Personen zu. Offen ist außerdem, wer das bei Anlage fixierte Freigabe-Intervall (FR-24d) speichert: Organisationen (Richtlinie) oder Freizeiten (Kopie)? Wer berechnet „gültig bis“ neu, wenn sich das Enddatum der Freizeit ändert? Wer legt die Freigabe an (Aufruf FZ → PER oder Ereignis), und wann wird sie von *Leitung* auf *Team* hochgestuft?
- **Fix:** AD-8 umformulieren: „Die Freigabe (Person, Freizeit, Stufe, gültig bis) besitzt Personen. Freizeiten legt sie bei Anmeldungseingang über `Personen.Contracts` an und stuft sie bei Bestätigung hoch. Das Intervall wird bei Anlage der Freizeit in Freizeiten fixiert. Ändert sich das Enddatum, ruft Freizeiten `Personen.Contracts` zur Neuberechnung auf.“

### H-4 · Datenbank-Migrationen pro Modul nicht geregelt
- **Ort:** AD-3, Konventionen
- **Problem:** Zehn `DbContext`s in einer Datenbank. Ohne Regel landen alle EF-Migrationshistorien in `public.__EFMigrationsHistory` und überschreiben sich gegenseitig, oder Agenten wählen unterschiedliche Wege (Migration beim Start, SQL-Skripte, `EnsureCreated`). Das ist ein klassischer Divergenzpunkt.
- **Fix:** Konvention ergänzen: „Jedes Modul hat eigene Migrationen mit der Historientabelle im eigenen Schema (`MigrationsHistoryTable("__ef_migrations", "<schema>")`). Angewendet werden sie vom Host beim Start (bzw. später durch einen Migrationsschritt im Deployment), nie durch `EnsureCreated`.“

### H-5 · Outbox/Inbox-Technik nicht festgelegt
- **Ort:** AD-4, SharedKernel („Outbox-Abstraktion“)
- **Problem:** Die Schreibregel (gleiche Transaktion, idempotent) steht. Offen sind: eine Outbox-Tabelle pro Modulschema (zwingend, weil die Transaktion nur einen `DbContext` umfasst) oder eine gemeinsame? Wo liegt die Idempotenz-Buchführung der Empfänger (Inbox pro Modul)? Eigenbau oder Bibliothek (Wolverine, MassTransit, …)? Ein Agent kann in einem Modul MassTransit einführen, ein anderer einen Eigenbau.
- **Fix:** Festhalten: „Outbox- und Inbox-Tabelle je Modulschema. Ein Zustell-Dienst im Host liest alle Outboxen. Eigenbau im SharedKernel, keine Messaging-Bibliothek.“ (Oder die Bibliothek mit Version im Stack nennen.)

### H-6 · Monitoring/Alarmierung als Dimension ausgelassen; Schlüssel-Backup fehlt
- **Ort:** AD-23, Zurückgestellt, Struktur-Gerüst („ServiceDefaults: Telemetrie“)
- **Problem:** Umgebungen, Deployment-Form und Anbieter sind entschieden bzw. zurückgestellt. **Monitoring, Log-Ziel und Alarmierung** kommen nirgends vor, weder entschieden noch zurückgestellt noch als offene Frage. Gerade bei Outbox-Mails (Bestätigungen, Login-Links) fallen stille Fehler auf, wenn niemand sie sieht. Bei Backups fehlt der Hinweis, dass der Schlüssel aus AD-14 getrennt gesichert werden muss. Geht er verloren, sind alle Freizeitpässe und Nachweise unlesbar.
- **Fix:** Eine Zeile unter „Zurückgestellt“ (oder als AD): „Monitoring/Alarmierung: OpenTelemetry über ServiceDefaults, Ziel offen; Pflicht vor der ersten echten Freizeit: Alarm bei Outbox-Rückstau und fehlgeschlagenem Lösch-Lauf.“ Bei der Backup-Zeile ergänzen: „inkl. getrennter Sicherung des Verschlüsselungsschlüssels; Wiederherstellung einmal geprobt.“

### H-7 · Datenauskunft und Lösch-Lauf: Ort des Vertrags und des Aggregators offen
- **Ort:** AD-16
- **Problem:** „Jedes Modul implementiert den Vertrag *Datenauskunft*“, aber in welchem Projekt dieser Vertrag liegt und wer die Teilergebnisse zusammenführt, ist offen. Liegt er in `Personen.Contracts` und Personen sammelt ein, verstößt das gegen AD-2, weil Personen ein Basis-Modul ist und Fachmodule nicht aufrufen darf. Der Aufruf „mit den Fristen der Organisation“ hat keine definierte Schnittstelle.
- **Fix:** „Die Schnittstellen `IDatenauskunft` und `ILoeschlauf` liegen in `SharedKernel`. Jedes Modul registriert seine Implementierung. Der Host (Export-Endpunkt FR-14 und Lösch-Dienst) ruft alle Implementierungen auf und führt die Ergebnisse zusammen.“ AD-24 um die Prüfung „jedes Modul mit Personendaten implementiert beide“ ergänzen.

---

## Mittel

### M-1 · Service Worker darf Daten unverschlüsselt zwischenspeichern
- **Ort:** AD-18
- **Problem:** Erlaubt sind nur verschlüsselte IndexedDB-Daten. Eine verbreitete PWA-Vorlage cacht aber auch API-Antworten im Cache Storage, und dort lägen Gesundheitsdaten im Klartext. Außerdem ist offen, wer löscht, wenn die App nach dem Ende der Freizeit nie wieder geöffnet wird.
- **Fix:** „Der Service Worker cacht nur statische Dateien, nie `/api/*`. Jeder Offline-Datensatz trägt ein Ablaufdatum. Beim App-Start und bei jedem Lesen wird Abgelaufenes gelöscht. Der WebCrypto-Schlüssel wird mit dem letzten Datensatz verworfen.“

### M-2 · Identität von QR-Teilnehmenden und Gästen; Push auf statischen Seiten
- **Ort:** AD-5, AD-11, AD-12, AD-21
- **Problem:** QR-Teilnehmende haben kein Identity-Konto. Ihre Sitzungsart (eigenes Cookie-Schema? Token pro Anfrage?) ist offen. AD-5 liefert den QR-Zugang als statisches SSR „ohne Interaktivitäts-Laufzeit“. FR-61 verlangt aber Push für diese Gruppe, und dafür braucht es Service Worker und JavaScript für das Push-Abo.
- **Fix:** In AD-12 ergänzen: „QR-Zugang und Gast-Link erzeugen eine eigene, nur lesende Cookie-Sitzung (eigenes Authentifizierungsschema, Claims: Freizeit, Zweck, Ablauf).“ In AD-5: „Auf öffentlichen Seiten ist schmales JavaScript ohne Blazor-Laufzeit erlaubt (Push-Abo, Service-Worker-Registrierung).“

### M-3 · Format der Geheimtexte ohne Schlüsselversion
- **Ort:** AD-14, Zurückgestellt „Schlüsselrotation“
- **Problem:** Die Rotation ist zurückgestellt. Wird das Geheimtext-Format aber nicht jetzt mit Schlüsselkennung festgelegt, lassen sich alte und neue Daten später nicht unterscheiden. Dann ist eine Migration aller Daten nötig.
- **Fix:** „Verschlüsselte Werte tragen ein Präfix mit Schlüssel-Version. Verfahren: AES-256-GCM mit Envelope-Verschlüsselung (Datenschlüssel pro Person, Hauptschlüssel in der Geheimnisverwaltung).“ Danach kann die Rotation zurückgestellt bleiben.

### M-4 · Wer besitzt Mail-Adressen und löst sie beim Versand auf?
- **Ort:** AD-4, AD-7, AD-13, SharedKernel (Mail-Adapter)
- **Problem:** Ereignisse tragen keine Personendaten. Der Mail-Versand muss die Adresse also bei der Zustellung auflösen. Nicht festgelegt ist, ob die Adresse bei Konten (Login) oder Personen (Profil, auch für Gäste ohne Konto) liegt und welches Modul die Mail erzeugt. Jedes Modul baut sonst eigenen Versand.
- **Fix:** „Kontakt-Mail-Adresse besitzt Personen (gilt auch für Gäste). Konten hält nur die Login-Kennung. Mails erzeugt das auslösende Modul als Outbox-Eintrag ‚MailAnPerson(personId, Vorlage, Parameter)‘. Der Zustell-Dienst löst die Adresse über `Personen.Contracts` auf.“

### M-5 · AD-24: Schema-Prüfung mit ArchUnitNET nicht durchsetzbar
- **Ort:** AD-24
- **Problem:** ArchUnitNET prüft Typ- und Assembly-Abhängigkeiten. Ob ein `DbContext` per `ToTable("x", "fremdesSchema")` oder per Roh-SQL auf ein fremdes Schema zugreift, erkennt es nicht. Die erlaubten Pfeile brauchen außerdem eine einzige maschinenlesbare Quelle.
- **Fix:** Für das Schema einen Test ergänzen, der für jeden `DbContext` das EF-Modell lädt und prüft, dass alle Entitäten im eigenen Schema liegen. Optional (Stufe 2) eigene DB-Rollen pro Modul mit Rechten nur auf das eigene Schema. Die Pfeilliste als Konstante im Architekturtest-Projekt pflegen und im Gerüst darauf verweisen.

### M-6 · Hintergrunddienste bei mehreren Instanzen
- **Ort:** AD-23, Bauprinzip („ein Server-Prozess“)
- **Problem:** Outbox- und Lösch-Dienst laufen im App-Container. Bei einem zweiten Container (Rolling Update, Skalierung) laufen sie doppelt.
- **Fix:** „Genau eine laufende Instanz“ festschreiben, oder: „Hintergrunddienste sichern sich per PostgreSQL-Advisory-Lock ab; Outbox-Zeilen werden mit `FOR UPDATE SKIP LOCKED` geholt.“

### M-7 · Concurrency-Token pro Zeile bei Autosave pro Feld
- **Ort:** AD-19, Konvention „Schreiben“
- **Problem:** `xmin` gilt für die ganze Zeile. Speichern zwei Teammitglieder verschiedene Felder desselben Programmpunkts, bekommt der zweite fälschlich einen `409`. Ohne Regel lösen Agenten das unterschiedlich: Token ignorieren, Token pro Feld oder automatisch erneut versuchen.
- **Fix:** Entscheiden und festschreiben, z. B.: „PATCH sendet Feldname, alten Wert und neuen Wert. Der Server meldet `409` nur, wenn der aktuelle Wert dieses Feldes vom alten Wert abweicht. `xmin` sichert ganze Formulare/Detailansichten.“ Oder bewusst festhalten, dass falsche Konflikte hingenommen werden.

### M-8 · Schutz gegen Cross-Site-Request-Forgery bei Cookie-Sitzung
- **Ort:** AD-12
- **Problem:** Bei einer Cookie-Sitzung braucht jeder schreibende Endpunkt einen Schutz gegen Anfragen, die eine fremde Seite im Namen des Nutzers auslöst (Cross-Site-Request-Forgery, CSRF). Ohne Vorgabe sichern manche Minimal-API-Endpunkte das ab, andere nicht.
- **Fix:** „Cookie `SameSite=Strict`. Zusätzlich verlangt eine zentrale Endpunkt-Gruppe für alle `/api/*`-Schreibaufrufe einen Antiforgery-Header. Öffentliche Formulare nutzen das Antiforgery-Token von ASP.NET Core.“

### M-9 · AD-11 ohne gemeinsame Umsetzung
- **Ort:** AD-11
- **Problem:** Ein Muster für sechs Link-Arten in vier Modulen. Ohne gemeinsame Komponente entstehen trotzdem vier Umsetzungen (Hash-Verfahren, Zeitvergleich, Einmal-Verwendung).
- **Fix:** „Erzeugen und Prüfen über `IZugangsToken` aus `SharedKernel` (SHA-256-Hash, Zweck als Pflichtfeld, Ablauf über `TimeProvider`, optional einmalig). Jedes Modul speichert seine Token im eigenen Schema.“

### M-10 · Schreibschutz „gelaufen“ kollidiert mit nachlaufender Finanz- und Inventararbeit
- **Ort:** AD-17 im Verhältnis zu FR-58/59 (erstattet), FR-37/38 (Rückgabe, Schäden)
- **Problem:** Erstattungen und Schadensmeldungen fallen typischerweise *nach* dem Abschluss an. Agenten, die AD-17 streng umsetzen, sperren sie. Andere bauen eigene Ausnahmen.
- **Fix:** Ausnahmeliste in AD-17 ausdrücklich abschließen. Entweder um „Erstattet-Vermerk (Finanzen)“ und „Schaden/Verlust (Inventar)“ erweitern, oder festhalten: „Leitung schließt erst nach Abrechnung ab.“ (Rückfrage an das PRD.)

---

## Niedrig

### N-1 · FR-14 „Export als PDF“ vs. Konvention „keine PDF-Bibliothek“
- **Ort:** Konvention „Druck“, Mapping-Zeile FR-14
- **Fix:** Ergänzen: „PDF-Export = Druckansicht des Browsers (‚Als PDF speichern‘); CSV serverseitig.“

### N-2 · FR-62 („Alle informieren?“) und FR-43c (Dokumentsuche) ohne Weg
- **Ort:** Mapping-Tabelle, AD-2
- **Problem:** Programm darf Kanäle nicht aufrufen. Die Suche über Freizeit-Dokumente und Org-Dokumente liegt in zwei Modulen.
- **Fix:** Je ein Satz: „FR-62: Die Oberfläche ruft nach Bestätigung den Kanäle-Endpunkt auf.“ „FR-43c: Die Oberfläche fragt Freizeiten und Organisationen ab und führt die Treffer zusammen; Suche nur über Titel/Metadaten, nicht über Dateiinhalte.“

### N-3 · NFR-12 bis NFR-17 nicht zugeordnet
- **Ort:** Mapping-Tabelle, Frontmatter `binds: NFR-1..NFR-19`
- **Fix:** Zeile ergänzen: „NFR-12–17 (Bedienbarkeit) → UX-Dokumente, keine Architektur-Invariante; berührt AD-19 und Konvention ‚Schreiben‘.“

### N-4 · Aufblähung: UI-Text und UX-Vorgaben im Gerüst
- **Ort:** AD-19 (Hinweistext „Inzwischen geändert – neu laden?“), Konventionen „Gestaltung“ und „Sprache der Oberfläche“, AD-9 (Berechnung des Ablaufdatums ist Fachlogik eines Moduls)
- **Fix:** UI-Text streichen (nur „Hinweis an Ort und Stelle laut EXPERIENCE.md“). Gestaltungszeile auf „UI-Regeln: DESIGN.md/EXPERIENCE.md; keine Drittanbieter-Komponentenbibliothek“ kürzen. Den Halbsatz zum Ablaufdatum in AD-9 kann man behalten, er ist aber keine Invariante zwischen Modulen.

### N-5 · Technik-Stand (Oktober 2026)
- **Ort:** Stack
- **Befund:** Insgesamt plausibel. .NET 10 ist LTS (November 2025); .NET 11 (STS) erscheint erst im November 2026, die Wahl von LTS ist richtig. PostgreSQL 18 (September 2025) ist gut; 19 ist ggf. frisch erschienen, 18 bleibt die vernünftige Wahl. Aspire 13, xunit.v3 3.x, Testcontainers 4.x, Npgsql EF 10 und ArchUnitNET 0.13 passen zeitlich. Die Patchnummer „10.0.11“ ist eine nicht überprüfte Momentaufnahme. ASP.NET Core Identity bringt keinen fertigen Login-Link mit, das wird ein eigener Token-Provider.
- **Fix:** Patchnummer durch „10.0.x (aktueller Patch)“ ersetzen. Bei AD-12 „Login-Link über eigenen Identity-Token-Provider nach AD-11“ ergänzen.

### N-6 · Data-Protection-Schlüssel beim Container-Betrieb
- **Ort:** AD-23
- **Problem:** Ohne dauerhaft gespeicherten Data-Protection-Schlüsselbund werden nach jedem Neustart alle Sitzungen und Antiforgery-Token ungültig.
- **Fix:** „Data-Protection-Schlüssel in PostgreSQL (eigenes Schema `host`) oder in der Geheimnisverwaltung.“

### N-7 · Zeitzone für die Phasenberechnung
- **Ort:** AD-17, Konvention „Zeit“
- **Fix:** „‚Läuft‘ wird für den Kalendertag in Europe/Berlin berechnet (Start- und Enddatum als `date`).“

### N-8 · Mermaid-Diagramme
- **Ort:** alle drei Diagramme
- **Befund:** Syntaktisch gültig (`flowchart TD/LR` mit Subgraphs und beschrifteten Kanten, `erDiagram` mit gültigen Kardinalitäten). Zehn Knoten im Abhängigkeitsdiagramm entsprechen den zehn Modulen aus AD-1. Inhaltlich weichen sie an den in K-2 genannten Stellen von den Regeln ab. Im ER-Diagramm fehlt der Bezug ANMELDUNG → PERSON, und *Aufgabe*/*Aufgaben-Vorlage* (Besitz-Konflikt K-2) fehlen ganz.
- **Fix:** Nach K-2 Pfeile ergänzen. Im ER-Diagramm `PERSON ||--o{ ANMELDUNG` und Aufgabe/Vorlage ergänzen.

---

## Checkliste – Kurzbewertung

| # | Kriterium | Ergebnis |
| --- | --- | --- |
| 1 | Echte Divergenzpunkte fixiert, keiner ausgelassen | Weitgehend. Es fehlen Rollen-Auflösung (K-1), Migrationen (H-4), Outbox-Technik (H-5), Mandanten-Kontext ohne Anfrage (H-2), Mail-Besitz (M-4), CSRF (M-8) |
| 2 | AD-Regeln durchsetzbar und wirksam | Meist. AD-2/17/20 widersprechen sich über die fehlenden Pfeile (K-2); AD-24 nur teilweise prüfbar (M-5); AD-4 vs. Konvention (H-1) |
| 3 | Zurückgestelltes ohne Divergenzrisiko | Überwiegend. Schlüsselrotation braucht jetzt das Format (M-3); Backup ohne Schlüssel-Backup (H-6) |
| 4 | Technik aktuell | Ja (N-5) |
| 5 | FR/NFR abgedeckt | Weitgehend. Lücken bei FR-32, FR-40, FR-46/48, FR-61 (QR-Push), FR-62, FR-43c, NFR-12–17 |
| 6 | Jede Dimension entschieden, zurückgestellt oder offen | Monitoring/Alarmierung fehlt ganz (H-6); Rest abgedeckt |
| 7 | Keine Aufblähung, Diagramme gültig und konsistent | Wenig Aufblähung (N-4); Diagramme gültig, aber nicht deckungsgleich mit den Regeln (K-2, N-8) |
