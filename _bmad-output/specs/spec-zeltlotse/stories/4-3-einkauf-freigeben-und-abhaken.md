---
title: 'Story 4-3: Einkauf freigeben und abhaken'
type: 'feature'
created: '2026-10-19'
status: 'done'
baseline_revision: 'd11fbba79b5a434feb0805b1419a108878525255'
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/planning-artifacts/architecture/architecture-Zeltlotse-2026-10-11/ARCHITECTURE-SPINE.md'
  - '{project-root}/_bmad-output/specs/spec-zeltlotse/stories/4-2-gerichte-vormerken-und-einkaufsliste.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Seit 4-2 gibt es eine berechnete Einkaufsliste. Die Küche kann aber noch nicht freigeben, was eingekauft werden soll, und wer einkauft, kann nichts abhaken (CAP-30, FR-51). Außerdem zeigt die Tausch-Vorschau bisher auch Posten, die schon gekauft sind (FR-50: „noch nicht gekaufte Posten“).

**Approach:**
- Jeder Posten der Einkaufsliste bekommt zwei Ja/Nein-Felder: `freigegeben` und `gekauft`.
  - Bei einem berechneten Posten (Gericht × Zutat) liegen sie in einer neuen Küche-Entität `PostenStand`.
  - Bei einem Hand-Posten liegen sie als zwei neue Spalten an `Einkaufsposten`.
- Beide Felder ändern sich per Autosave-PATCH (AD-19) an den bestehenden Posten-Routen. Für einen ganzen Tag gibt es eine Sammel-Freigabe.
- Neue Seite „Einkauf“ für die Einkaufenden: Sie zeigt nur freigegebene Posten, Abhaken ist ein Tipp. Der Seitenaufruf macht keinen Allergen-Abgleich und schreibt kein Protokoll.
- Die Tausch-Vorschau und das Übernehmen lassen gekaufte Posten außen vor.
- Dazu Doku, Demo-Daten und Screenshots.

## Boundaries & Constraints

**Always:**

*Endpunkte und Rechte (wie 4-1 und 4-2)*
- Neue Routen liegen unter `/api/freizeiten/{freizeitId}/kueche/…` und tragen `.NurFreizeitRolle(Mitarbeit).GehoertZuSchalter(Verpflegung)`.
- Commands tragen `IBenutzerCommand`, `ISchalterCommand` (Verpflegung) und `IModulCommand<KuecheDbContext>`.
- In der Phase Gelaufen gibt jedes Schreiben 409 `freizeit-abgeschlossen`. Lesen bleibt 200.
- Jede Änderung am Stand eines berechneten Postens (`freigegeben`, `gekauft`, auch die Tages-Freigabe) sperrt zuerst das Gericht (`GerichtSperrenAsync`, `FOR UPDATE`) und gibt ihm eine neue Version. Eine vorher geladene Tausch-Vorschau ist danach veraltet, genau wie bei Hand-Mengen in 4-2.

*Routen*

| Route | Zweck |
|---|---|
| `PATCH kueche/einkaufsliste/gerichte/{gerichtId}/zutaten/{zutatId}` | bisher nur `menge`; neu auch die Felder `freigegeben` und `gekauft` (Text `"true"`/`"false"`) |
| `PATCH kueche/einkaufsliste/posten/{postenId}` | neu auch die Felder `freigegeben` und `gekauft` |
| `POST kueche/einkaufsliste/freigabe` | Körper `{ tag: "JJJJ-MM-TT" \| null, freigegeben: bool }`: setzt `freigegeben` für alle Posten dieser Tagesgruppe, `tag: null` steht für die Hand-Posten ohne Tag; Antwort 204 |
| `GET kueche/einkauf` | Seite der Einkaufenden: Tage wie in der Einkaufsliste, aber nur Posten mit `freigegeben = true`, Tage ohne solche Posten fehlen; ohne Allergen-Übersicht und ohne Ersatzportionen |

*Regeln*
- Die Konfliktregel folgt AD-19. `alterWert` ist der aktuelle Wert als `"true"`/`"false"`. Weicht er ab, gibt es 409 `konflikt`. Ein anderer Wert als `true`/`false` ergibt 400 am Feld.
- Ein Posten ohne Stand-Zeile gilt als nicht freigegeben und nicht gekauft. Die Zeile entsteht beim ersten Ändern und ist eindeutig je (Gericht, Zutat). Sie verschwindet per FK-Cascade mit dem Gericht oder der Zutat.
- Die beiden Felder sind voneinander unabhängig. Es gibt keine erzwungene Reihenfolge, keinen automatischen Rückfall und keine Sperre für gekaufte Posten.
- Die Liste der Küche (`GET einkaufsliste`) und `GET einkauf` liefern je Posten `freigegeben` und `gekauft`.
- Tages-Freigabe:
  - Betroffen sind alle berechneten Posten der Gerichte, deren Mahlzeit an diesem Tag liegt, und alle Hand-Posten mit diesem Tag.
  - Ein Tag außerhalb der Freizeit oder in falscher Form ergibt 400 am Feld `tag`, ein fehlendes `freigegeben` 400 am Feld `freigegeben`.
  - Die Freigabe ist idempotent. `gekauft` bleibt unberührt.
- Tausch:
  - Die Vorschau zeigt in Vorher nur nicht gekaufte Posten. `Tauschvorschau` bekommt dazu `schonGekauft` (Anzahl der gekauften Posten des alten Rezepts).
  - Übernehmen (mit anderem Rezept) löscht alle Stand-Zeilen des Gerichts. Die Posten des neuen Rezepts sind also offen und nicht freigegeben. Hand-Mengen wandern wie in 4-2.
  - Mit gleichem Rezept bleibt alles, wie es ist.
- `GET einkauf` ruft `AbgleichAsync` nicht auf. Es entsteht kein Eintrag im Zugriffsprotokoll.

*Oberfläche und Doku*
- Bausteine nur aus `Ui.Komponenten`/`Ui`. Für Ja/Nein gibt es `AutosaveAnkreuzfeld`. Deutsch ohne Anrede, Handy und Laptop, keine Pop-ups. Ein Ladefehler erscheint nicht als leere Seite.
- Gelaufen: nur lesbar, ohne Ankreuzfelder und ohne Knöpfe.
- Testgetrieben: Der Test entsteht vor dem Code.

**Never:**
- Kein Vermerk, wer wann abgehakt hat (FR-60 gilt nur für „Beitrag bezahlt“), also keine Personendaten in Küche und keine Eigenschaft `PersonId`.
- Keine Tagesansicht, kein Restetag (4-4), kein Offline-Modus, keine Summen über Gerichte hinweg.
- Kein neuer Problemcode, kein Aktionsschlüssel, keine Ereignisse, kein `[ErlaubtNachAbschluss]`, kein Fremdschlüssel über Schemagrenzen. Seed-Daten nie per Tabellen-Insert.
- Keine neue Rolle: Einkaufen ist eine Aufgabe im Team (Leitung und Team).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Posten freigeben | PATCH Mehl (Pfannkuchen) `freigegeben` `"true"`, `alterWert` `"false"` | 200 `{version, freigegeben:"true"}`; Liste und `GET einkauf` zeigen Mehl freigegeben | `alterWert` `"true"` → 409 `konflikt` |
| Abhaken | PATCH `gekauft` `"true"` auf freigegebenem Posten | `GET einkauf` zeigt ihn mit `gekauft` true | Wert „ja“ → 400 am Feld `gekauft` |
| Hand-Posten | PATCH Äpfel `freigegeben`/`gekauft` | wie berechnete Posten | fremder Posten → 404 |
| Tag freigeben | POST `freigabe` `{tag:"2027-08-03", freigegeben:true}` | alle Posten des Tags freigegeben, zweiter Aufruf ändert nichts, `gekauft` unverändert | `tag` 2027-09-01 → 400 `tag`; ohne `freigegeben` → 400 |
| Freigabe zurück | `{tag:"2027-08-03", freigegeben:false}` | `GET einkauf` ohne diesen Tag; gekaufte bleiben gekauft | – |
| Ohne Tag | `{tag:null, freigegeben:true}` | nur Hand-Posten ohne Tag | – |
| Nichts freigegeben | keine Freigabe | `GET einkauf` liefert keine Tage | – |
| Kein Protokoll | `GET einkauf` bei Freizeit mit Allergie-Frage | keine neuen Einträge im Zugriffsprotokoll | – |
| Tausch-Vorschau | Pfannkuchen, Mehl gekauft, → Chili | Vorher ohne Mehl, `schonGekauft` 1 | – |
| Tausch übernehmen | wie oben, „Übernehmen“ | alle Chili-Posten offen und nicht freigegeben, keine Stand-Zeile des Gerichts übrig | – |
| Veraltet | Vorschau geladen, danach Posten abgehakt, dann Tauschen | 409 `konflikt` | – |
| Gleichzeitig | zwei PATCH `gekauft` auf neuen Posten ohne Stand-Zeile; Tages-Freigabe parallel zu PATCH | je genau eine Zeile, keine 500 (einer 200, der andere 200 oder 409) | – |
| Modul aus, Gelaufen, fremd | wie 4-2 | 404; 409 `freizeit-abgeschlossen` bei PATCH und POST, `GET einkauf` 200; 404 | – |
| Löschlauf | Freizeit-Daten abgelaufen | Stand-Zeilen weg | – |

</intent-contract>

## Code Map

**Küche (Stand 4-2)**
- `src/Modules/Kueche/Zeltlotse.Kueche/KuecheModul.cs:54-65`: Routengruppe `kueche`, `Mitarbeit`. Die neuen Routen `GET /einkauf` und `POST /einkaufsliste/freigabe` kommen hierher.
- `Einkauf/Gericht.cs`: `Gericht`, `MengeVonHand`. Daneben entsteht `PostenStand` (Id, OrganisationId, FreizeitId, GerichtId, ZutatId, Freigegeben, Gekauft, Version).
- `Einkauf/Einkaufsposten.cs`: `Wert`/`FeldAendern`/`Vergleichswert` je `EinkaufspostenFeld`. Dazu kommen die Fälle `Freigegeben` und `Gekauft`.
- `Einkauf/EinkaufslisteSlices.cs`:
  - `HandMengeAendernHandler` (Gericht sperren, Version per `IsModified` erzwingen, ~:126) bekommt die Zweige für die neuen Felder.
  - Validator: `MengeFehler` gilt nur für `menge`.
  - Endpunkt: `FeldPatch.Lesen<HandMengeFeld>` liefert schon das Feld (bisher ignoriert `_`).
  - `EinkaufspostenFeldAendern*` (~:332) und `EinkaufTage` (:22, Tagesprüfung).
- `Einkauf/EinkaufLesen.cs`:
  - `EinkaufslisteLadenEndpunkt` ist das Muster für `EinkaufLadenEndpunkt`.
  - `TauschvorschauEndpunkt` filtert Vorher auf nicht gekauft und füllt `schonGekauft`.
  - `GerichtFehler` (Feldfehler).
- `Einkauf/EinkaufAbfrage.cs`: `GerichteAsync` lädt zusätzlich die Stand-Zeilen in `GerichtDaten`. `AbgleichAsync` wird von `GET einkauf` nicht aufgerufen.
- `Einkauf/EinkaufRechnen.cs`: `RechenPosten` und `AlsApi` (beide Varianten) tragen `Freigegeben`/`Gekauft`. `Tage(...)` bleibt die eine Gruppierungsregel, `GET einkauf` filtert danach.
- `Einkauf/GerichtSlices.cs:199-248` `GerichtTauschenHandler`: Stand-Zeilen löschen wie die Hand-Mengen (:235-237), nur bei anderem Rezept.
- `Daten/KuecheDbContext.cs`:
  - DbSets in beiden Kontexten;
  - `KuecheModell` (Muster `MengeVonHand`, eindeutiger Index, Cascade);
  - `GerichtSperrenAsync`/`PostenSperrenAsync` (:55-69).
  - Neue Migration `EinkaufFreigabe` (Tabelle `posten_stand`, zwei Spalten `einkaufsposten` mit Default false).
- `Datenschutz/KuecheLoeschlauf.cs`: `PostenStand` vor den Gerichten löschen.
- `Rezepte/KuecheWerte.cs`: `FeldPatch.Lesen`, `KuecheProbleme`, `TagLesen`/`TagFehler`.

**Contracts**
- `src/Zeltlotse.Api.Contracts/Kueche/Einkaufsliste.cs`:
  - `EinkaufslistenPosten` erweitern um `Freigegeben`, `Gekauft` (ans Ende);
  - `HandMengeFeld`/`HandMengeFelder` und `EinkaufspostenFeld`/`EinkaufspostenFelder` erweitern um `Freigegeben` (`"freigegeben"`) und `Gekauft` (`"gekauft"`), Doku-Kommentare anpassen;
  - neu `EinkaufFreigabe(string? Tag, bool? Freigegeben)` und `Einkauf(IReadOnlyList<EinkaufsTag> Tage)` mit leeren `Ersatzportionen`.
- `Essensplan.cs`: `Tauschvorschau` bekommt `int SchonGekauft`. Enums brauchen je `[JsonConverter(typeof(JsonStringEnumConverter<…>))]`, neue Enums gibt es keine.

**Oberfläche**
- `src/Zeltlotse.App/Features/Kueche/Einkaufsliste.razor`:
  - Spalten `Spalten` :166-173 bekommen „Freigegeben“ (`AutosaveAnkreuzfeld`, Pfad je Posten `KuecheApiClient.HandMengePfad` bzw. `PostenPfad`, Id je Posten eindeutig) und „Gekauft“ (Text „gekauft“ oder leer).
  - Tages-Card :78-87: Zeile „N von M freigegeben · K gekauft“ und Knöpfe „Ganzen Tag freigeben“ (wenn ein Posten nicht freigegeben ist) bzw. „Freigabe zurücknehmen“ (wenn einer freigegeben ist).
  - `Abgeschlossen` :152 zeigt nur Text.
  - `NachSpeichernAsync` :250 und `NeuLadenAsync` :253 als Muster.
- `src/Zeltlotse.Ui/Autosave/AutosaveAnkreuzfeld.razor`: Ja/Nein mit Autosave, „Gespeichert · Rückgängig“, 409 → „Inzwischen geändert – neu laden?“.
- `src/Zeltlotse.Ui.Komponenten/Ankreuzfeld.razor`. Falls die Tipp-Fläche am Handy kleiner als 44 px ist, CSS in `Ui.Komponenten` nach ELKW-Tokens ergänzen.
- `KuecheReiter.razor`: vierter Eintrag „Einkauf“ (`Aktiv == "einkauf"`). `KuecheTexte.cs` bekommt `EinkaufZiel` neben `EinkaufslisteZiel` (:40).
- `src/Zeltlotse.Ui/Api/KuecheApiClient.cs`: `EinkaufLadenAsync` (Muster `EinkaufslisteLadenAsync` :122, 404 → null), `FreigabeAsync` (Muster `PostenLoeschenAsync` :179 mit `KuecheAktionErgebnis`), Pfad-Helfer :46-67.

**Tests, Demo, Doku**
- `tests/Zeltlotse.Api.Tests/EinkaufslisteTests.cs`: Helfer :43-99, `AlleRouten` :389, Rechte :410/:427, Gelaufen :364, Gleichzeitigkeit :271, Löschlauf :463. `KuecheTestHelfer.cs` (`HandMengeAsync` :53, `EinkaufslisteAsync` :59).
- `EndpunktListeTests.cs:292-331`: Küche-Routen alphabetisch, Zähler 18 → 20.
- `tests/Zeltlotse.Kueche.Tests/EinkaufTests.cs`: Fachtests (Einkaufsposten-Felder :271, Tausch :194-256, Mandantenfilter :374, PersonId-Check :364).
- `tests/Zeltlotse.Ui.Tests/EssensplanSeitenTests.cs`: Einkaufsliste ab :321 (`ListeJson` :324, `ListeRendern` :372), `NachgebildeterHandler`.
- `src/Zeltlotse.Api/Demo/DemoDaten.cs:169-170`, `GerichteAsync` :641-700. `DemoKatalog.cs:67-86`. `tests/Zeltlotse.Api.Tests/DemoDatenTests.cs:312-330`.
- `tests/Zeltlotse.E2E.Tests/DokuScreenshotTests.cs`: Abschnitt 4-2 ab :2135 (`Einkaufsliste` :2201), `BildAsync` :2407, `Geraete` :21.
- `doku/src/content/docs/kueche/`: `einkaufsliste.md` („Noch nicht dabei“ :71-73), `essensplan.md` (Tausch :38, „Noch nicht dabei“ :66-68), `index.md` (:11-22). `tests/Zeltlotse.ArchitectureTests/DokuRegeln.cs:16-27` `PflichtSeiten`, Anrede-Regel.
- `MigrationsReihenfolgeTests` braucht keine Änderung.

## Tasks & Acceptance

**Execution:**

*Tests zuerst*
- `tests/Zeltlotse.Kueche.Tests/EinkaufTests.cs`: Fachtests für
  - `Einkaufsposten.FeldAendern` mit `freigegeben`/`gekauft` samt Konflikt;
  - `PostenStand`;
  - Tage mit Feldern, Filter „nur freigegeben“, Vorher ohne gekaufte;
  - Mandantenfilter und „keine PersonId“ für `PostenStand`.
- `tests/Zeltlotse.Api.Tests/EinkaufFreigabeTests.cs` (neu), gegen echtes PostgreSQL:
  - alle Zeilen der I/O-Matrix;
  - Rechte für `GET einkauf`, `POST freigabe` und die PATCH-Felder: Leitung/Team 2xx; Org-Admin ohne Rolle, fremde Organisation, fremde Freizeit, unbekannte Freizeit 404; anonym 401; Schalter aus 404; Gelaufen 409 beim Schreiben, `GET einkauf` 200;
  - Tausch-Fälle, Gleichzeitigkeit, Löschlauf, kein Protokoll.
- `EinkaufslisteTests.cs`/`GerichteTests.cs` nur anpassen, wo sich Antworten ändern.
- `EndpunktListeTests.cs`: zwei neue Routen, Zähler 20.
- `tests/Zeltlotse.Ui.Tests/EinkaufSeitenTests.cs` (neu, Muster `EssensplanSeitenTests`), bUnit für
  - Einkauf: Tage, Abhaken sendet PATCH `gekauft`, Leer-Zustand, Gelaufen ohne Ankreuzfelder, Ladefehler, nicht verfügbar;
  - Einkaufsliste: Spalte „Freigegeben“, Tages-Knöpfe senden `POST freigabe` und laden neu, Zählzeile;
  - Tausch-Vorschau mit `schonGekauft`;
  - Reiter mit vier Einträgen.

*Server*
- `PostenStand` samt Modell, DbSets, Migration `EinkaufFreigabe`, Spalten an `Einkaufsposten`.
- PATCH-Zweige für beide Felder. Neue Slices `EinkaufFreigeben` (Command, Validator, Handler, Endpunkt) und `EinkaufLadenEndpunkt`.
- Tausch-Anpassungen in Vorschau und Handler, Löschlauf, Contracts wie in der Code Map.
- Tages-Freigabe:
  - Die Mahlzeiten des Tags kommen über `EinkaufAbfrage.MahlzeitenAsync`.
  - Die Gerichte des Tags werden nach Id geordnet gesperrt, damit es keinen Deadlock gibt.
  - Die Hand-Posten des Tags werden per `FOR UPDATE` gesperrt.

*Oberfläche*
- `Einkaufsliste.razor`: Spalten, Zählzeile, Tages-Knöpfe, nur lesbar in Gelaufen.
- `Einkauf.razor` (neu, `/freizeiten/{id}/kueche/einkauf`):
  - Dachzeile, h1 „Einkauf“, `KuecheReiter Aktiv="einkauf"`;
  - je Tag eine Card mit Liste: je Posten ein `AutosaveAnkreuzfeld` (Feld `gekauft`, Label „Mehl 500 g · Pfannkuchen · Frühstück“ bzw. ohne Gericht);
  - Leer-Zustand „Noch nichts zum Einkaufen freigegeben.“ mit Link zur Einkaufsliste;
  - Gelaufen: Text „gekauft“/„offen“ statt Kästchen;
  - Muster wie `Einkaufsliste.razor`.
- `Essensplan.razor` (Tausch-Vergleich): wenn `schonGekauft > 0`, der Satz „N schon gekaufte Posten stehen nicht im Vergleich.“
- `KuecheReiter.razor`, `KuecheTexte.cs`, `KuecheApiClient.cs`.

*Demo, Doku, Screenshots*
- Demo-Daten:
  - `DemoKatalog.cs`: Freigabe Tag 03.08.2027 ganz, danach Hand-Posten „Äpfel“ wieder nicht freigegeben; gekauft „Mehl“ und „Milch“ der Pfannkuchen.
  - `DemoDaten.GerichteAsync` (oder ein eigener Schritt danach) setzt das nur über Commands und idempotent: vorher den aktuellen Wert prüfen.
  - `DemoDatenTests` entsprechend anpassen.
- `DokuScreenshotTests.cs`: Abschnitt „Story 4-3“ mit `einkauf` (Seite der Einkaufenden), je Handy und Laptop, `AxeAsync`, nur lesend. `einkaufsliste-*` entsteht mit den neuen Spalten neu.
- Doku:
  - neu `kueche/einkauf.md` („Einkaufen: freigeben und abhaken“): Wer, Weg, Posten und ganze Tage freigeben, Freigabe zurücknehmen, abhaken mit einem Tipp, Rückgängig, Konflikt, was beim Gerichttausch mit gekauften und freigegebenen Posten passiert, geänderte Personenzahl bei gekauften Posten (die Küche prüft die Menge), nach dem Abschluss, kein Vermerk, wer abgehakt hat, „Noch nicht dabei“ (Tagesansicht, Restetag, offline), Screenshots;
  - ergänzt: `einkaufsliste.md` (Spalten, „Noch nicht dabei“), `essensplan.md` (Tausch nur nicht gekaufte Posten, „Noch nicht dabei“), `index.md`;
  - `DokuRegeln.PflichtSeiten` um `kueche/einkauf` erweitern.

**Acceptance Criteria:**
- Given die Küche hat Dienstag 03.08. freigegeben, when ein Teammitglied am Handy „Einkauf“ öffnet und bei „Mehl“ tippt, then ist Mehl mit einem Tipp abgehakt und zeigt „Gespeichert · Rückgängig“, and in der Einkaufsliste der Küche steht bei Mehl „gekauft“.
- Given nur Pfannkuchen-Posten sind freigegeben, when die Seite „Einkauf“ lädt, then erscheinen nur diese Posten, keine Ersatzportionen und keine Allergen-Übersicht.
- Given Mehl ist gekauft, when die Küche Pfannkuchen gegen Chili tauschen will, then fehlt Mehl in „Vorher“, and die Vorschau nennt einen schon gekauften Posten.
- Given Verpflegung ist aus oder die Freizeit gelaufen, when „Einkauf“ geöffnet wird, then „Diese Seite ist nicht verfügbar.“ bzw. Hinweis „nur lesbar“ ohne Ankreuzfelder.
- Given die Story ist fertig, when `dotnet test` und `npm run build` in `doku/` laufen, then sind alle Tests grün, die Screenshots `einkauf-*` (neu) und `einkaufsliste-*` erzeugt und der Doku-Build fehlerfrei.

## Spec Change Log

## Review Triage Log

### 2026-10-19 — Review pass
- verdicts: 29 findings — high 0, medium 3, low 18, false 8, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: „Ganzen Tag freigeben“ hat kein Rückgängig, „Freigabe zurücknehmen“ macht auch Einzelauswahl rückgängig — bewusst einfache Tagesaktion (Spec); Rückgängig oder Bestätigung bräuchte neuen Zustand bzw. neuen Dialogzweig.
  - `[false]` `[reject]` Blind: Cascade löscht den Gekauft-Stand beim Entfernen von Gericht oder Zutat — gewollt: der Posten selbst verschwindet mit; kein Posten zeigt danach einen falschen Stand.
  - `[low]` `[patch]` Blind: Rezeptänderung nach Freigabe/Kauf nicht dokumentiert — `einkauf.md` Abschnitt „…oder ein Rezept ändert“ ergänzt.
  - `[low]` `[patch]` Blind: Ersatzportionen erreichen die Einkaufenden nicht — Satz in `einkauf.md`: Küche ergänzt dafür eigene Posten und gibt sie frei.
  - `[low]` `[reject]` Blind: Jeder Haken macht eine offene Tausch-Vorschau veraltet — gewollt (Design Notes): Vorschau beruht auf dem Gekauft-Stand; Neuladen ist richtig.
  - `[low]` `[reject]` Blind: 44-px-Regel gilt app-weit und nicht in `.zl-auswahl` — größere Tippflächen am Handy sind überall erwünscht; die Einkaufsseite nutzt `.zl-auswahl` nicht.
  - `[low]` `[patch]` Blind: Beschriftung „Mehl freigegeben“ doppeldeutig bei zwei Gerichten — Label nutzt jetzt `PostenText` („Mehl 800 g · Pfannkuchen · Frühstück freigegeben“); bUnit-Test.
  - `[medium]` `[patch]` Blind/Edge: Ankreuzfeld ohne @key bzw. veralteter Haken nach Tages-Freigabe oder „Neu laden“, weil (Wert, Version) gleich bleibt (Version 0) — @key aus Id und Ladezähler auf beiden Seiten; bUnit-Tests für Repro und Neu laden.
  - `[low]` `[reject]` Blind: Einkaufsseite ohne Fortschritt und ohne Selbstaktualisierung — neue Funktion; Doku nennt jetzt, dass die Seite sich nicht selbst aktualisiert.
  - `[low]` `[patch]` Blind: Hand-Posten-Kästchen senden `HandMengeFelder.*` — jetzt `EinkaufspostenFelder.*` bei `PostenPfad`.
  - `[low]` `[patch]` Blind: Zweite Konfliktprüfung in `StandAendernAsync` wirkungslos — Regel nur noch in `PostenStand.FeldAendern` mit `alterWert`; Gericht-Version nur bei echter Änderung.
  - `[false]` `[reject]` Blind: Roh-SQL mit festen Namen, Mandantenfilter ungetestet — gleiches Muster wie `GerichtSperrenAsync` (4-2); EF setzt den Query-Filter um `FromSql`, Mandantenfilter-Tests der Küche decken das Muster.
  - `[low]` `[patch]` Blind: Konfliktsatz in `einkauf.md` unklar — neu formuliert (abgehakt, Haken entfernt, Freigabe zurückgenommen).
  - `[false]` `[reject]` Blind: „Ohne Tag“ könnte berechnete Posten ohne Tag enthalten — jede Mahlzeit hat einen Tag (`FreizeitMahlzeit.Tag`), Gerichte ohne bekannte Mahlzeit fallen in `GerichteAsync` weg.
  - `[low]` `[patch]` Verification-Gap: Löschen von Zutat/Rezept mit Stand-Zeile ungetestet — API-Test `Zutat_oder_Rezept_mit_Stand_loeschen_204_…` ergänzt.
  - `[low]` `[patch]` Verification-Gap: „Neu laden“ nach Konflikt auf der Einkaufsseite ungetestet — bUnit-Test `Konflikt_beim_Abhaken_Neu_laden_…` ergänzt.
  - `[low]` `[patch]` Verification-Gap: PATCH mit unverändertem Wert gibt dem Gericht eine neue Version — mit dem Umbau von `StandAendernAsync` erledigt (Version nur nach erfolgreicher Änderung).
  - `[medium]` `[patch]` Edge: veralteter Freigegeben-Haken nach Tages-Freigabe — siehe @key-Eintrag.
  - `[medium]` `[patch]` Edge: veralteter Gekauft-Haken nach „Neu laden“ auf der Einkaufsseite — siehe @key-Eintrag.
  - `[low]` `[reject]` Edge: Hand-Posten mit Tag außerhalb der (verkürzten) Freizeit → Tages-Freigabe 400 — sehr selten (Datumsänderung, vgl. zurückgestellter 4-2-Befund); Fix bräuchte Sonderzweig.
  - `[low]` `[reject]` Edge: 400 der Tages-Freigabe zeigt nur allgemeinen Fehlertext — gleiche seltene Ursache; eigener Ergebnisfall wäre neue Fläche.
  - `[false]` `[reject]` Edge: Tages-Card nur mit Ersatzportionen zeigt „0 von 0“ — Ersatzportionen entstehen nur aus Zutaten mit Allergenen, also gibt es dann auch Posten.
  - `[false]` `[reject]` Edge: Gekaufter, nicht mehr freigegebener Posten verschwindet von der Einkaufsseite — gewollt und dokumentiert (Design Notes, `einkauf.md`).
  - `[low]` `[reject]` Edge: Gericht getauscht, während die Einkaufsseite offen ist → PATCH 404 → „Nicht gespeichert“ — selten; Fix im gemeinsamen `AutosaveDienst` würde das Verhalten aller Seiten ändern; Seite neu öffnen hilft.
  - `[low]` `[reject]` Edge: Demo-Seed nach Teilfehler überspringt das Zurücknehmen der Äpfel-Freigabe — nur bei abgebrochenem Seed in Entwicklung; Demo wird dann neu aufgesetzt.
  - `[false]` `[reject]` Edge (Claim): Gleichnamige, gekaufte Zutat erscheint nach dem Tausch wieder offen — gewollt und dokumentiert (neue Menge kann größer sein).
  - `[false]` `[reject]` Intent: keine 403- und Token-Tests — keine Route ist mit falscher Rolle als 403 erreichbar (AD-12: ohne Freizeit-Rolle 404, getestet), keine Token-Route; Design Notes.
  - `[false]` `[reject]` Intent: Doku nicht unter einer Rolle — `kueche/` ist laut NFR-20 der Bereich „Küche“ (Rolle und Aufgabe); `DokuRegeln.PflichtSeiten` ergänzt.
  - `[low]` `[reject]` Intent: Ein-Tipp-Ablauf nicht als E2E-Interaktion — per bUnit und HTTP-Test belegt; Screenshot-Tests bleiben bewusst nur lesend, damit der Demo-Stand stabil bleibt.

## Design Notes

- **Zwei Ja/Nein-Felder statt eines Status.** `AutosaveAnkreuzfeld` gibt es schon (Werte `"true"`/`"false"`, Konflikt nach AD-19). Freigeben (Küche) und Abhaken (Einkaufende) sind damit dasselbe Muster.
  - Die Seite „Einkauf“ zeigt alles mit `freigegeben`. Nimmt die Küche die Freigabe eines gekauften Postens zurück, verschwindet er nur von dieser Seite.
  - Es gibt keine Übergangsregeln und keine Speziallogik.
- **Posten-Stand als eigene Zeile.** Die Liste wird weiter berechnet (4-2, AD-13). Gespeichert wird nur, was Menschen entscheiden: Hand-Menge und Stand.
  - `MengeVonHand` bleibt unverändert, weil dort eine Menge Pflicht ist.
  - Hand-Posten sind schon Zeilen und bekommen die zwei Spalten direkt.
- **Tausch.** FR-50 meint die noch nicht gekauften Posten. Gekauftes ist da und steht nicht zur Wahl.
  - Mit dem alten Rezept verschwinden dessen Posten samt Stand.
  - Freigabe und gekauft werden nicht auf gleichnamige Zutaten übertragen: Die neue Menge kann größer sein, und ein übertragenes „gekauft“ würde Fehlmengen verstecken. Die Küche gibt das neue Gericht bewusst frei.
  - Hand-Mengen wandern wie bisher.
- **Gericht-Version bei jeder Stand-Änderung.** Das ist dasselbe Muster wie bei der Hand-Menge in 4-2. Eine Vorschau, die auf einem anderen Gekauft-Stand beruht, endet beim Übernehmen in 409.
- **Eigene Lese-Route für die Einkaufenden.** Sonst würde jeder Aufruf der Einkaufsseite Allergie-Antworten entschlüsseln und protokollieren, obwohl die Einkaufenden das nicht brauchen (Datensparsamkeit, AD-8).
- **Kein Vermerk, wer abgehakt hat.** UX verlangt das nur, wo das PRD es fordert (FR-60, Beitrag bezahlt). So bleibt die Küche frei von Personendaten.
- **Rechte.** Wie in 4-1 und 4-2 gibt es keine 403- und keine Token-Fälle. Alle Routen gelten für Leitung und Team, ohne Rolle gibt es 404 (AD-12). Küche und Einkauf sind Aufgaben, keine Rollen.
- **Gekauft trotz neuer Menge.** Ändert sich die Personenzahl, bleibt ein gekaufter Posten gekauft. Die Doku sagt, dass die Küche dann die Menge prüft. Eine automatische Rücknahme wäre Speziallogik.

## Verification

**Commands:**
- `dotnet test --project tests/Zeltlotse.Kueche.Tests`, `dotnet test --project tests/Zeltlotse.Api.Tests -- --filter-class "*EinkaufFreigabe*" --filter-class "*Einkaufsliste*" --filter-class "*Gerichte*" --filter-class "*EndpunktListe*" --filter-class "*DemoDaten*"`: während der Arbeit grün.
- `dotnet test --project tests/Zeltlotse.ArchitectureTests` und `tests/Zeltlotse.Ui.Tests`: grün.
- Am Ende einmal alle Testprojekte inklusive E2E (Screenshots erzeugt, Rauschen auf HEAD zurückgesetzt): grün.
- `npm run build` in `doku/`: fehlerfrei.

## Auto Run Result

Status: done

**Umgesetzt:** Die Küche gibt Posten der Einkaufsliste einzeln (Ankreuzfeld „Freigegeben“) oder als ganzen Tag frei („Ganzen Tag freigeben“ / „Freigabe zurücknehmen“, Zählzeile je Tag). Die neue Seite „Einkauf“ zeigt nur Freigegebenes; Abhaken ist ein Tipp mit Autosave. Freigabe und Gekauft sind zwei unabhängige Ja/Nein-Felder: bei berechneten Posten in der neuen Entität `PostenStand`, bei Hand-Posten als Spalten. Die Einkaufsseite macht keinen Allergen-Abgleich und schreibt kein Zugriffsprotokoll. Die Tausch-Vorschau zeigt nur nicht gekaufte Posten und nennt die Zahl der schon gekauften; Übernehmen verwirft den Stand des Gerichts. Kein Vermerk, wer abgehakt hat.

**Geänderte Dateien (Auswahl):**
- `src/Modules/Kueche/Zeltlotse.Kueche/Einkauf/PostenStand.cs` (neu), `EinkaufslisteSlices.cs` (PATCH-Felder, Tages-Freigabe), `EinkaufLesen.cs` (`GET einkauf`, Vorschau), `EinkaufRechnen.cs`, `EinkaufAbfrage.cs`, `Einkaufsposten.cs`, `GerichtSlices.cs` (Tausch), `Daten/KuecheDbContext.cs` (Sperren, Modell), Migration `20261019102133_EinkaufFreigabe`, `KuecheLoeschlauf.cs`, `KuecheModul.cs` (2 Routen), `Rezepte/KuecheWerte.cs` (Ja/Nein).
- `src/Zeltlotse.Api.Contracts/Kueche/Einkaufsliste.cs`, `Essensplan.cs`: Felder, `EinkaufFreigabe`, `Einkauf`, `SchonGekauft`.
- `src/Zeltlotse.App/Features/Kueche/Einkauf.razor` (neu), `Einkaufsliste.razor`, `Essensplan.razor`, `KuecheReiter.razor` (vier Reiter), `KuecheTexte.cs`; `Ui/Api/KuecheApiClient.cs`; `Ui/Autosave/AutosaveAnkreuzfeld.razor` (Callback `Gespeichert`); CSS-Tippfläche 44 px.
- Demo: `DemoKatalog.cs`/`DemoDaten.cs` Schritt `EinkaufAsync` (03.08. freigegeben, Äpfel zurückgenommen, Mehl und Milch gekauft).
- Doku: neu `kueche/einkauf.md`; ergänzt `einkaufsliste.md`, `essensplan.md`, `index.md`, `rezepte.md`; Screenshots `einkauf-*` neu, `einkaufsliste-*`, `essensplan-*`, `gericht-tauschen-*`, `rezepte-*` aktualisiert.
- Tests: `Api.Tests/EinkaufFreigabeTests.cs`, `Kueche.Tests/EinkaufFreigabeTests.cs`, `Ui.Tests/EinkaufSeitenTests.cs` (neu); angepasst `EndpunktListeTests` (20 Küche-Routen), `DemoDatenTests`, `EinkaufslisteTests`, `GerichteTests`, `DokuRegeln`/`DokuTests`, E2E-Abschnitt „Story 4-3“.

**Review:** 29 Befunde (medium 3, low 18, false 8). Gepatcht: veraltete Ankreuzfelder nach vollständigem Neuladen (@key mit Ladezähler, medium, eine Ursache für 3 Befunde), eindeutige Beschriftung „… freigegeben“, richtige Feldkonstanten für Hand-Posten, AD-19-Konfliktregel nur in `PostenStand.FeldAendern` (Gericht-Version nur bei echter Änderung), Doku (Konfliktsatz, Rezeptänderung, Ersatzportionen als eigene Posten), Tests für Zutat/Rezept löschen mit Stand und für „Neu laden“ nach Konflikt (alle low). Zurückgestellt: keine. Zurückgewiesen: alle übrigen mit Begründung im Review Triage Log.

**Folge-Review:** nicht empfohlen (`false`) — gepatcht wurde 1 Eintrag medium und 8 low, kein high.

**Verifikation:**
- Gezielt nach der Umsetzung: Kueche 106/106, Architektur 135/135, Ui 721/721, Api-Filter (EinkaufFreigabe, Einkaufsliste, Gerichte, EndpunktListe) 64/64; Fall-Matrix vollständig durch gelaufene Tests abgedeckt.
- Komplettlauf nach den Review-Patches: alle 14 Testprojekte grün, 2647/2647 inklusive E2E (Screenshots erzeugt).
- `npm run build` in `doku/`: fehlerfrei, 59 Seiten.
- 41 Screenshots, die der Lauf nur mit Rendering-Rauschen neu erzeugt hatte, auf HEAD zurückgesetzt.

**Restrisiken:**
- Gleichzeitiges Löschen einer Zutat während einer Tages-Freigabe kann selten einen FK-Fehler (500) auslösen (wie in 4-2 akzeptiert).
- Hand-Posten mit einem Tag außerhalb einer nachträglich verkürzten Freizeit: Tages-Freigabe dieses Tags gibt 400 (zurückgewiesen, low).
- Wird ein Gericht getauscht, während die Einkaufsseite offen ist, zeigt Abhaken „Nicht gespeichert“ statt „neu laden“ (zurückgewiesen, low).
