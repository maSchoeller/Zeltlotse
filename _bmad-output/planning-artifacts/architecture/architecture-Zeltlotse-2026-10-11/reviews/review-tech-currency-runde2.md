# Prüfbericht Technik-Aktualität – Runde 2

- **Gegenstand:** `ARCHITECTURE-SPINE.md`, neu hinzugekommene Entscheidungen AD-22, AD-27, AD-29, AD-30 und die Stack-Tabelle (neue Zeilen)
- **Stand der Prüfung:** 11.10.2026, per Websuche und Abruf der Primärquellen (NuGet, GitHub, Microsoft Learn), nicht aus Trainingswissen
- **Gerüst wurde nicht geändert.** Korrekturen sind Vorschläge.

## Gesamturteil

Alle neuen Bibliotheken sind aktuell, MIT- bzw. Microsoft-lizenziert und laufen unter .NET 10. Drei Stellen passen technisch aber nicht ganz zur Beschreibung: die Lebensdauer im Mediator, das fehlende Telemetry-Paket für die Schwärzung und die Interaktivität gemeinsamer Bausteine auf den statisch gerenderten öffentlichen Seiten. Sie sollten vor dem Bauen nachgeschärft werden.

## Übersicht

| Punkt | Urteil | Aktuelle Version (Stand 11.10.2026) | Quelle |
| --- | --- | --- | --- |
| Mediator.SourceGenerator / .Abstractions 3.0.x | bestätigt, mit Fallstricken | 3.0.2 stabil (22.03.2026); 3.1.0-rc.1 Vorabversion | https://www.nuget.org/packages/Mediator.SourceGenerator · https://github.com/martinothamar/Mediator |
| Microsoft.Extensions.Compliance.Redaction „10.0“ | bestätigt, Version präzisieren, Zusatzpaket fehlt | 10.10.0 (09.09.2026), Ziele net8/net9/net10 | https://www.nuget.org/packages/Microsoft.Extensions.Compliance.Redaction · https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction |
| Redaction mit `[LoggerMessage]` + Datenklassifizierung | bestätigt, mit Bedingungen | – | https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction |
| HMAC-Schwärzung | bestätigt, aber experimentell | – | wie oben |
| Razor Class Libraries für WASM und statisches SSR | passt nur eingeschränkt | – | https://learn.microsoft.com/aspnet/core/blazor/components/class-libraries-and-static-server-side-rendering?view=aspnetcore-10.0 · https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0 |
| bUnit 2.x | bestätigt | 2.11.3 (13.09.2026), Ziele net8/9/10/11 | https://www.nuget.org/packages/bunit |
| Microsoft.Playwright | bestätigt | 1.63.0 (23.09.2026) | https://www.nuget.org/packages/Microsoft.Playwright |
| Deque.AxeCore.Playwright | bestätigt | 4.13.0 (11.08.2026), MIT; zieht Deque.AxeCore.Commons (MPL-Lizenz) | https://www.nuget.org/packages/Deque.AxeCore.Playwright |
| FakeTimeProvider (Microsoft.Extensions.TimeProvider.Testing) | bestätigt | 10.10.0 (09.09.2026) | https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing |
| Konvention `Guid.CreateVersion7(TimeProvider)` (Nebenbefund, betrifft FakeTimeProvider-Tests) | unpassend | – | https://learn.microsoft.com/dotnet/api/system.guid.createversion7?view=net-10.0 |

## Befunde im Einzelnen

### 1. Mediator (AD-27) – bestätigt, Konfiguration und Lebensdauer festlegen

**Was stimmt:**

- 3.0.2 ist stabil, MIT-lizenziert und zielt auf .NET Standard 2.0 und .NET 8. Damit läuft es unter .NET 10.
- **Pipeline-Behaviors** werden unterstützt. Sie werden geordnet über `options.PipelineBehaviors` in `AddMediator` eingetragen und können offen generisch sein, eingeschränkt auf eine Schnittstelle (z. B. nur Commands). Die Reihenfolge Validierung → Transaktion → Outbox ist damit abbildbar.
- **Notifications mit mehreren Handlern** werden unterstützt und automatisch registriert. Standard ist `ForeachAwaitPublisher` (nacheinander), alternativ `TaskWhenAllPublisher` (parallel) oder ein eigener Publisher über `NotificationPublisherType`.
- Genau ein Handler je Request ist eingebaut: Mehrere Handler ergeben einen Compiler-Hinweis („Multiple request handlers found“), ein fehlender eine Warnung.

**Fallstricke bei mehreren Modul-Assemblies:**

- Den **Source Generator nur im äußersten Projekt** einbinden (`Zeltlotse.Api`). In den Modul-Projekten steht nur `Mediator.Abstractions`. Laut README führt der Generator in mehreren Projekten desselben Auslieferungsartefakts zu Fehlern.
- Ohne die Option `Assemblies` durchsucht der Generator **alle referenzierten Assemblies**. Für Zeltlotse reicht das, weil der API-Host alle Module referenziert. Wer eingrenzen will, setzt `options.Assemblies = [typeof(AnmeldungenModul), …]`. Die Werte müssen zur Übersetzungszeit konstant sein und gehen nur über `AddMediator`.
- Generische Requests/Notifications (`Ereignis<T>`) unterstützt die Bibliothek **nicht**. Jedes Ereignis braucht einen eigenen konkreten Typ. Das passt zur Konvention „ein Typ je Ereignis, versioniert“.
- Testprojekte, die Handler ohne den API-Host aufrufen, brauchen selbst einen Generator-Verweis. Das ist erlaubt, weil sie eigene Artefakte sind. Einfacher ist es, über `WebApplicationFactory` den Host zu nutzen.

**Was nicht passt bzw. fehlt:**

- **Lebensdauer:** Standard ist `ServiceLifetime.Singleton`. Handler und Behaviors für Transaktion und Outbox brauchen aber den **pro Anfrage** gültigen `DbContext`. **Korrektur:** In AD-27 oder den Konventionen `ServiceLifetime = Scoped` festschreiben. (Das README empfiehlt Singleton aus Leistungsgründen, äußert sich zu `DbContext` aber nicht.)
- **Ereignis-Empfänger über „denselben Mechanismus“:** Mit dem Standard-Publisher laufen alle Empfänger eines Ereignisses nacheinander in einem Aufruf. Bei einer Inbox je Modul (Konvention *Outbox*) soll aber jeder Empfänger einzeln, wiederholbar und idempotent laufen. **Korrektur:** Festhalten, dass der Zustell-Dienst je Inbox-Eintrag genau einen Empfänger aufruft (eigener Publisher oder gezieltes Auflösen von `INotificationHandler<T>`), statt `Publish` für alle Empfänger gemeinsam.
- **Version:** „3.0.x“ ist richtig. 3.1 ist erst Release Candidate und bringt u. a. `CachingMode`. Empfehlung: auf 3.0.2 bleiben, 3.1 nach stabiler Freigabe prüfen.

### 2. Redaction und `[LoggerMessage]` (AD-22) – bestätigt, Paket und Bedingungen ergänzen

**Was stimmt:** Microsoft dokumentiert genau den beschriebenen Weg. Eine Datenklassifizierung wird als Attribut an Parameter einer `[LoggerMessage]`-Methode gehängt. `AddRedaction` ordnet Klassifizierungen Schwärzern zu. Der Standard-Schwärzer löscht den Wert (`ErasingRedactor`), der `HmacRedactor` ersetzt ihn durch einen HMAC-SHA256-Wert.

**Was fehlt bzw. zu beachten ist:**

- Die Schwärzung im Logger braucht zusätzlich **`Microsoft.Extensions.Telemetry`** und den Aufruf `builder.EnableRedaction()` beim Logging. Ohne das Paket wird nichts geschwärzt. **Korrektur:** Paket in die Stack-Tabelle aufnehmen (gleiche Versionslinie 10.10.x).
- Die Klassifizierungs-Attribute müssen an **jedem** Parameter stehen. Für ganze Objekte im Log ist `[LogProperties]` mit Attributen an den Eigenschaften nötig. Freier Text in Ausnahmemeldungen wird nicht geschwärzt. Die Regel „nur Kennungen“ bleibt also die eigentliche Absicherung, die Schwärzung ist das Sicherheitsnetz.
- **`HmacRedactor` ist experimentell** (Warnung `EXTEXP0002`, muss bewusst unterdrückt werden). Der Schlüssel muss Base64 mit mindestens 44 Zeichen sein und gehört in die Geheimnisverwaltung (AD-23). **Korrektur:** In AD-22 vermerken, dass HMAC experimentell ist, und Schlüssel samt `KeyId` als Geheimnis aufführen. Ohne HMAC fällt man auf reines Löschen zurück.
- **Version:** „10.0“ ist zu ungenau. Aktuell ist 10.10.0. Die Pakete aus `dotnet/extensions` erscheinen monatlich als 10.x und sind nicht an die Laufzeit-Version 10.0.x gebunden. **Korrektur:** „10.10.x (Linie dotnet/extensions)“ eintragen.

### 3. Razor Class Libraries in WASM und statischem SSR (AD-29) – passt nur eingeschränkt

**Was stimmt:** Eine Razor Class Library kann gleichzeitig von der eigenständigen WASM-App und von statisch gerenderten Seiten genutzt werden. Microsoft empfiehlt, in der Bibliothek **keinen** `@rendermode` zu setzen. In der eigenständigen WASM-App gibt es Render-Modi gar nicht, dort ist alles interaktiv.

**Was nicht passt:**

- Im **statischen SSR** laufen **keine Ereignis-Handler** (`@onclick`, `@bind`-Aktualisierung usw.). Ausnahme ist nur `@onsubmit` von Formularen. `Zeltlotse.Public` hat laut AD-5 kein WebAssembly, also auch kein Interactive WebAssembly/Auto.
- Damit funktionieren auf den öffentlichen Seiten **nicht**: der Autosave-Baustein (PATCH pro Feld, „gespeichert“, Rückgängig, 409-Hinweis), die Inline-Bestätigung mit Klick und alles, was Zustand im Browser hält. AD-29 bindet aber `Zeltlotse.Public` an diese Bausteine, und die Gast-Anmeldung liegt in `Zeltlotse.Public`.
- **Korrektur:** In AD-29 festhalten:
  1. Bausteine in `Zeltlotse.Ui.Komponenten` sind „render-modus-neutral“: Sie funktionieren ohne Ereignis-Handler (Links, normale Formulare) und erweitern sich nur in der Web-App um Interaktion (Microsoft-Ansatz „progressive enhancement“, Prüfung über `RendererInfo.IsInteractive`).
  2. Der Autosave-Baustein ist **nur für die Web-App**. Die Gast-Anmeldung in `Zeltlotse.Public` ist ein klassisches Formular mit `[SupplyParameterFromForm]`, `FormName` und Antiforgery-Token, abgeschickt am Ende.
  3. bUnit-Tests (AD-30) prüfen jeden Baustein sowohl ohne als auch mit Interaktion.
- **Nebenpunkt (AD-24):** „hängt nur von ASP.NET Core Components ab“ sollte ausdrücklich `Microsoft.AspNetCore.Components.Web` (und Forms) einschließen. Formular- und Eingabe-Komponenten liegen dort, und die RCL-Vorlage referenziert genau dieses Paket. Sonst schlägt der Architekturtest fälschlich an.

### 4. bUnit 2.x (AD-30) – bestätigt

2.11.3 ist aktuell (13.09.2026) und zielt auf net8.0, net9.0, net10.0 und net11.0. Der net10-Zweig verlangt Microsoft.AspNetCore.Components ≥ 10.0.10, was zur Laufzeit 10.0.12 passt. bUnit läuft mit xUnit (auch v3). Zum Wechsel von 1.x gibt es eine Upgrade-Anleitung auf bunit.dev, hier aber nicht relevant (Neubeginn). **Korrektur:** „2.x“ kann bleiben, besser „2.11.x“.

### 5. Playwright und axe (AD-30) – bestätigt

- Microsoft.Playwright 1.63.0 (23.09.2026), .NET Standard 2.0, läuft unter .NET 10.
- Deque.AxeCore.Playwright 4.13.0 (11.08.2026) ist stabil und MIT-lizenziert. Es zieht `Deque.AxeCore.Commons` mit **MPL-Lizenz** nach. Das ist nur im Testprojekt unproblematisch. Die neueren `-alpha`-Builds nicht verwenden.
- Für WCAG 2.2 AA die axe-Regeln mit den Tags `wcag2a`, `wcag2aa`, `wcag21aa`, `wcag22aa` aufrufen. Axe deckt nur einen Teil der Kriterien automatisch ab, Tastatur und Fokus brauchen eigene Schritte im Test.
- **Korrektur:** Statt „aktuelle stabile Version beim Anlegen“ die konkreten Versionen 1.63.x / 4.13.x eintragen.

### 6. FakeTimeProvider (AD-30) – bestätigt; Nebenbefund zu UUID v7

- `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 ist aktuell und enthält `FakeTimeProvider`. **Korrektur:** In die Stack-Tabelle aufnehmen (fehlt dort bisher).
- **Nebenbefund (Konventionen, Zeile „Kennungen“):** `Guid.CreateVersion7(TimeProvider)` gibt es nicht. .NET 10 kennt nur `CreateVersion7()` und `CreateVersion7(DateTimeOffset)`. **Korrektur:** `Guid.CreateVersion7(timeProvider.GetUtcNow())` schreiben. Nur so greift die künstliche Uhr auch bei den Kennungen.

## Vorgeschlagene Ergänzungen der Stack-Tabelle

| Name | Version |
| --- | --- |
| Mediator.SourceGenerator / Mediator.Abstractions | 3.0.2 (Generator nur in `Zeltlotse.Api`) |
| Microsoft.Extensions.Compliance.Redaction | 10.10.x |
| Microsoft.Extensions.Telemetry (neu) | 10.10.x |
| Microsoft.Extensions.TimeProvider.Testing (neu) | 10.10.x |
| bUnit | 2.11.x |
| Microsoft.Playwright | 1.63.x |
| Deque.AxeCore.Playwright | 4.13.x |
