---
title: 'Prüfbericht – Aktualität und Eignung der Technik'
reviewed: ../ARCHITECTURE-SPINE.md (Stack, AD-5, AD-6, AD-12, AD-18, AD-19, AD-21, AD-23, AD-24, Konventionen, Quellbaum)
basis: ../.memlog.md (Versionsstände vom 2026-10-11)
date: '2026-10-11'
method: Websuche und Abruf der Primärquellen (Microsoft Learn, nuget.org, postgresql.org, GitHub dotnet/aspnetcore, WebKit) – nicht aus Trainingswissen
---

# Prüfbericht: Aktualität und Eignung der Technik

## Urteil in einem Satz

Der Stack ist aktuell und tragfähig (alle Kernentscheidungen bestätigt). Es gibt aber **eine echte Passungslücke**: Für eine *Blazor Web App* gibt es in .NET 10 keine offizielle PWA- bzw. Offline-Unterstützung. AD-5 (Server-Prerendering, SSR-Seiten) und AD-18 (Offline-Start) passen ohne Zusatzregeln nicht zusammen. Außerdem sind mehrere im Gerüst bzw. Arbeitsprotokoll genannte Versionsstände inzwischen überholt.

Legende: **bestätigt** = passt und ist aktuell · **veraltet** = Versionsangabe überholt (Entscheidung bleibt richtig) · **unpassend** = Entscheidung passt technisch so nicht und braucht eine Regel oder Korrektur · **Hinweis** = passt, aber eine Präzisierung verhindert Fehler der KI-Agenten.

## Übersicht

| # | Punkt | Status | Gefundener Stand (11.10.2026) |
| --- | --- | --- | --- |
| 1 | .NET 10 LTS / C# 14 | veraltet (Patch) | 10.0.12 vom 08.09.2026, SDK 10.0.401, Support bis 14.11.2028 |
| 2 | Blazor Web App + Interactive WebAssembly + PWA/Offline | **unpassend** | Keine PWA-Option für Blazor Web App; Issue #48935 offen, Meilenstein „.NET 12 Planning“ |
| 3 | Prerendering (Folge von AD-5) | **unpassend** (ungeregelt) | Prerendering ist bei Interactive WebAssembly standardmäßig an |
| 4 | ASP.NET Core Identity – Login-Link | Hinweis | Kein eingebauter Login-Link; nur über eigenen Token-Weg |
| 5 | ASP.NET Core Identity – Passkeys | Hinweis (Chance) | Passkeys seit .NET 10 eingebaut (Blazor-Web-App-Vorlage) |
| 6 | EF Core 10 + Npgsql, `xmin` als Concurrency-Token | bestätigt + Hinweis | Npgsql.EFCore.PG 10.0.3 (10.07.2026); Syntax `uint` + `[Timestamp]`/`IsRowVersion()` |
| 7 | PostgreSQL 18 vs. 19 | bestätigt | 18.6 aktuell; 19 noch nicht erschienen (RC1 15.10., GA geplant 29.10.2026) |
| 8 | Aspire 13.x | veraltet | 13.6.1 vom 07.10.2026 (Protokoll nennt 13.3) |
| 9 | xunit.v3 3.x | **veraltet** | 4.0.2 vom 09.10.2026 (Major-Sprung 4.0 am 14.08.2026) |
| 10 | Testcontainers.PostgreSql 4.x | veraltet (Minor) | 4.16.0 vom 08.10.2026 (Protokoll nennt 4.13.0) |
| 11 | TngTech.ArchUnitNET (+ xUnitV3) 0.13.x | bestätigt (Minor) | 0.13.4 vom 20.08.2026 (Protokoll nennt 0.13.3) |
| 12 | `Guid.CreateVersion7()` | bestätigt | Seit .NET 9, in .NET 10 vorhanden; PostgreSQL 18 hat `uuidv7()` |
| 13 | `.slnx` | bestätigt | Standardformat von `dotnet new sln` ab .NET 10 |
| 14 | Web Push auf iOS (Home-Screen-PWA) | bestätigt + Hinweis | Nur installierte Home-Screen-Web-Apps; EU-Sperre 2024 zurückgenommen; Declarative Web Push ab iOS 18.4 |
| 15 | .NET-Web-Push-Bibliothek | Hinweis | `WebPush` 1.0.13 (net8, kompatibel net10) bzw. `Lib.Net.Http.WebPush` 3.3.1 (Stand 03/2025) |
| 16 | Offline-Speicher auf iOS (AD-18) | Hinweis | Speicher kann vom Browser geräumt werden; nur installierte App ist geschützter |

---

## 1. .NET 10 LTS / C# 14 – veraltet (nur Patch-Stand)

- **Gefunden:** Neueste Version 10.0.12 vom 08.09.2026, SDK 10.0.401, Phase „active“, Supportende 14.11.2028. Das Gerüst (Stack) und das Protokoll nennen 10.0.11 vom 11.08.2026. Der nächste Patch-Dienstag ist der 13.10.2026.
- **.NET 11:** RC1 seit 08.09.2026 mit Go-Live-Lizenz; Veröffentlichung zur .NET Conf am 10.–12.11.2026 angekündigt. Die Wahl von .NET 10 (LTS) statt .NET 11 bleibt richtig.
- **Quellen:** https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json · https://visualstudiomagazine.com/articles/2026/09/14/net-11-rc1-gets-go-live-support-ahead-of-november-launch.aspx · https://devblogs.microsoft.com/dotnet/dotnet-conf-2026/
- **Korrektur:** In der Stack-Tabelle „aktuell 10.0.11“ durch „10.0.x, beim Anlegen neuesten Patch nehmen (Stand 11.10.2026: 10.0.12)“ ersetzen. Eine `global.json` mit `rollForward: latestFeature` festlegen. C# 14 ist die Sprachversion von .NET 10 und bleibt richtig.

## 2. Blazor Web App mit Interactive WebAssembly als PWA / offline – **unpassend**

- **Gefunden:**
  - Die PWA-Vorlage gibt es in .NET 10 **nur** für die eigenständige Blazor-WebAssembly-App (`dotnet new blazorwasm --pwa`). Für die *Blazor Web App* gibt es keine PWA-Option. Das GitHub-Issue dotnet/aspnetcore#48935 („Consider adding PWA option to the Blazor Web App template“) ist offen. Es wurde am 10.06.2026 auf „.NET 12 Planning“ verschoben. Laut Kommentar vom 28.07.2026 gibt es eine SDK-Änderung, die aber noch nicht veröffentlicht ist. Issue #65254 („Problems when setting up Blazor Web App (.NET 10) with PWA“) wurde als Duplikat geschlossen.
  - Der Service Worker der Vorlage beantwortet *jede* Navigationsanfrage mit dem zwischengespeicherten `/index.html`. In einer Blazor Web App gibt es kein statisches `index.html`. Die Startseite ist die vom Server gerenderte Komponente `App.razor`. Laut Microsoft-Doku muss man den Service Worker selbst anpassen, sonst fängt er auch server-gerenderte Seiten ab („Interference with server-rendered pages“).
  - Weitere bekannte Stolpersteine: #59289 (PWA + `MapStaticAssets` erzeugt falsche Endpunktdatei, behoben 09/2025), #60895 (Umbau von reinem WASM auf gemischtes Hosting zerstört die Offline-Funktion), #68535 (offen: Service Worker, wenn die Startseite über eine Umleitung erreicht wird – bei Login-Weiterleitungen relevant).
  - Offline und Anmeldung: Ohne Netz kann sich niemand anmelden. Die App braucht einen eigenen Weg, offline mit dem zuletzt angemeldeten Nutzer weiterzuarbeiten. In einer Blazor Web App kommt der Anmeldezustand im WebAssembly-Teil normalerweise vom Server (Zustand wird beim Prerendering mitgegeben). Offline fehlt dieser Zustand.
- **Warum das für Zeltlotse zählt:** AD-18 verlangt einen Offline-*Start* von Küchenansicht, Notfallkarte, Sicherheitskette und Vorfall-Protokoll. Das gilt auch nach einem Neustart des Handys ohne Netz. AD-5 legt aber fest, dass der Host die App ausliefert (SSR-Seiten, Prerendering ist Standard). Ohne zusätzliche Regeln öffnet die App offline nicht. Das ist kein V6-Detail: Render-Modus und Service-Worker-Strategie werden mit V1 festgelegt.
- **Quellen:** https://learn.microsoft.com/aspnet/core/blazor/progressive-web-app/?view=aspnetcore-10.0 · https://github.com/dotnet/aspnetcore/issues/48935 · https://github.com/dotnet/aspnetcore/issues?q=is%3Aissue+PWA+%22Blazor+Web+App%22
- **Empfohlene Korrektur (AD-5 und AD-18 ergänzen):**
  1. **App-Bereich ohne Prerendering:** Die angemeldete App läuft unter einem festen Pfadpräfix (z. B. `/app/...`) als `InteractiveWebAssembly` **mit `prerender: false`**, auch für `HeadOutlet`. Öffentliche Seiten und `/Account/*` (Identity) bleiben statisches SSR mit `[ExcludeFromInteractiveRouting]`.
  2. **Eigener Service Worker statt Vorlage:** Der Service Worker liefert nur für Navigationen unter `/app/` eine **statische App-Hülle** aus dem Cache. Die Hülle ist eine Seite in `wwwroot`, die `blazor.web.js` und den WASM-Teil lädt. Alle anderen Navigationen (öffentliche Seiten, `/Account/*`, Login-Link-Ziele) gehen nur übers Netz. `/api/*` wird nie aus dem Cache bedient. Die Liste der zu cachenden Dateien kommt aus `ServiceWorkerAssetsManifest` im `Web.Client`-Projekt; Fingerprinting von `MapStaticAssets` beachten.
  3. **Offline-Anmeldezustand:** Die App merkt sich den zuletzt angemeldeten Nutzer (nur Kennung + Rollen, verschlüsselt) in IndexedDB, damit sie offline starten kann. Beim Abmelden wird das gelöscht. Online gilt immer das Cookie (AD-12 bleibt unverändert).
  4. **Früher Durchstich (Spike) in V1:** Bevor weitere Module gebaut werden, einmal zeigen: veröffentlichte App installieren, Flugmodus an, App neu starten, eine Testseite öffnen. Dabei auf Android Chrome **und** iOS Safari (Home-Screen) testen. Auslöser in „Zurückgestellt“ aufnehmen: „Wenn .NET 12 die PWA-Option für Blazor Web App bringt, eigene Lösung ersetzen.“
  5. Alternative, falls der Spike scheitert: den Offline-Teil (V6) als kleine eigenständige Blazor-WebAssembly-PWA (oder statische Seite mit JS) unter `/offline` bauen. Diese Variante ist offiziell unterstützt. Sie greift nur auf die Offline-Daten in IndexedDB zu und nimmt Vorfall-Einträge zum späteren Senden an.

## 3. Prerendering als Folge von AD-5 – **unpassend** (ungeregelt)

- **Gefunden:** Interactive WebAssembly rendert in einer Blazor Web App **standardmäßig zuerst auf dem Server**. Die Komponenten aus `Web.Client` laufen also einmal im Server-Prozess. Dienste, die nur im Client registriert sind (z. B. `HttpClient`), lösen dort einen Fehler aus. Ein Abschalten wirkt nur am obersten Render-Modus (`<Routes @rendermode=...>`); die Einstellung bei Kind-Komponenten wird ignoriert.
- **Folgen für das Gerüst:** (a) AD-5 sagt „Web.Client spricht nur über HTTP-Endpunkte“. Beim Prerendering liefe derselbe Code im Server-Prozess und müsste dort dieselben Endpunkte per HTTP an sich selbst aufrufen, mit Cookie-Weitergabe. Das ist fehleranfällig. (b) Mandanten-Kontext und Freigabe-Protokoll (AD-8) würden beim Prerendering zusätzlich ausgelöst; Einsichten würden doppelt protokolliert. (c) Offline siehe Punkt 2.
- **Quellen:** https://learn.microsoft.com/aspnet/core/blazor/components/prerender?view=aspnetcore-10.0 · https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0#static-ssr-pages-in-an-interactive-app · https://learn.microsoft.com/aspnet/core/blazor/call-web-api?view=aspnetcore-10.0
- **Empfohlene Korrektur:** In AD-5 als Regel aufnehmen: „Der App-Bereich nutzt `InteractiveWebAssemblyRenderMode(prerender: false)` global am `Routes`-Element. Kein Prerendering von App-Seiten.“ Damit gibt es genau einen Ort, an dem App-Code läuft (Browser). Die Ladezeit vor dem ersten Bild deckt eine schlichte Lade-Hülle ab; danach kommt die App aus dem Service-Worker-Cache.

## 4. ASP.NET Core Identity – Login-Link als Standard – Hinweis

- **Gefunden:** Identity hat in .NET 10 **keinen eingebauten Login-Link** („Magic Link“). Üblich ist ein eigener Token-Anbieter (`AddTokenProvider`, `GenerateUserTokenAsync`/`VerifyUserTokenAsync`). Diese Identity-Tokens werden standardmäßig *nicht gespeichert*, sondern über Data Protection signiert. Sie gelten einen Tag. Einmaligkeit erreicht man nur, indem man den Security-Stamp ändert.
- **Konflikt mit AD-11:** AD-11 verlangt ein eigenes Muster: zufälliges Token, nur als Hash gespeichert, mit Zweck und Ablauf. Das ist besser (einmalig, widerrufbar, prüfbar), aber eben *nicht* der Identity-Token-Weg. Ohne Klarstellung bauen Agenten wahrscheinlich den Identity-Weg aus Tutorials.
- **Quellen:** https://andrewlock.net/implementing-custom-token-providers-for-passwordless-authentication-in-asp-net-core-identity · https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/security/authentication/accconfirm.md
- **Empfohlene Korrektur:** In AD-12 ergänzen: „Login-Link nutzt das Token-Muster aus AD-11 (nicht die Identity-Token-Anbieter). Nach erfolgreicher Prüfung: Token entwerten, dann `SignInManager.SignInAsync`. Das Einlösen erfolgt per POST auf einer SSR-Seite, nicht per GET.“ Grund für POST: Mail-Scanner öffnen Links automatisch und würden sonst das Token verbrauchen oder eine Anmeldung auslösen.

## 5. ASP.NET Core Identity – Passkeys – Hinweis (Chance)

- **Gefunden:** .NET 10 bringt eingebaute **Passkeys** (WebAuthn) in Identity mit. Die Blazor-Web-App-Vorlage mit „Individual Accounts“ enthält sie bereits (Identity-Schema `Version3`). Grenzen: nur als erster Faktor, keine Prüfung der Geräte-Bescheinigung (Attestation) ab Werk, HTTPS ist Pflicht.
- **Quellen:** https://learn.microsoft.com/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0 · https://learn.microsoft.com/aspnet/core/security/authentication/passkeys/blazor?view=aspnetcore-10.0
- **Empfohlene Korrektur:** In AD-12 bzw. OP-1 als Option vermerken: „Login-Link (Standard) + optional Passkey statt Passwort“. Passkeys sind für Eltern am Handy einfacher als Passwörter und schützen vor Phishing. Mindestens das Identity-Schema `Version3` von Anfang an anlegen, damit später keine Migration der Identity-Tabellen nötig ist.

## 6. EF Core 10 + Npgsql, `xmin` als Concurrency-Token – bestätigt + Hinweis

- **Gefunden:** `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (10.07.2026), Ziel net10.0, benötigt EF Core ≥ 10.0.4. Version 11.0.0-rc.1.1 gehört zu .NET 11 und ist **nicht** zu nehmen. Die Syntax hat sich seit Npgsql 7 geändert: Die alte Methode `UseXminAsConcurrencyToken()` ist veraltet. Richtig ist eine `uint`-Eigenschaft mit `[Timestamp]` oder `.IsRowVersion()`. Sie wird automatisch auf die Systemspalte `xmin` abgebildet; eine Migration ist nicht nötig.
- **Quellen:** https://www.npgsql.org/efcore/modeling/concurrency.html · https://npgsql.org/efcore/release-notes/7.0.html · https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL
- **Empfohlene Korrektur:**
  - In AD-19 die Syntax festschreiben: „`public uint Version { get; set; }` mit `IsRowVersion()`; kein `UseXminAsConcurrencyToken`.“ So greifen Agenten nicht auf veraltete Tutorials zurück.
  - Konvention „Autosave pro Feld = ein PATCH“: Jeder PATCH ändert `xmin`. Die Antwort muss das **neue** Token zurückgeben, und die Oberfläche muss es übernehmen. Sonst bekommt ein Nutzer beim zweiten Feld einen 409-Konflikt mit sich selbst. Das gehört als Regel in die Konvention „Schreiben“.
  - Bekannte Einschränkung: `xmin` ist eine Transaktionsnummer. Sie kann überlaufen und ist bei mehreren Änderungen in einer Transaktion ungenau. Für Zeltlotse ist das unkritisch; bewusst so akzeptieren.

## 7. PostgreSQL 18 vs. 19 – bestätigt

- **Gefunden:** Unterstützt: 18 (aktueller Stand 18.6, Erstveröffentlichung 25.09.2025, Support bis 14.11.2030). PostgreSQL 19 ist **nicht erschienen**: Beta 4 am 24.09.2026, RC1 geplant am 15.10.2026, endgültige Version geplant am 29.10.2026. Mit 18 zu starten ist richtig. Ein späterer Wechsel auf 19 ist ein normales Major-Upgrade.
- **Zusatz:** Aspire hat das Standard-Abbild für PostgreSQL mit 13.4 auf **18** angehoben. Das Datenverzeichnis hat sich geändert (`/var/lib/postgresql` statt `/var/lib/postgresql/data`); ältere Volumes von PostgreSQL 17 starten nicht.
- **Quellen:** https://www.postgresql.org/support/versioning/ · https://www.postgresql.org/message-id/915517c4-d0ee-49f9-bbeb-c683ef2b9d3f%40postgresql.org · https://aspire.dev/reference/container-images/
- **Empfohlene Korrektur:** Im AppHost und in Testcontainers das Abbild ausdrücklich auf `postgres:18` (bzw. `18.6`) festlegen, damit lokal, im Test und im Betrieb dieselbe Version läuft (AD-23).

## 8. Aspire 13.x – veraltet

- **Gefunden:** `Aspire.Hosting.AppHost` 13.6.1 vom 07.10.2026 (13.6.0 am 29.09.2026). Das Protokoll nennt 13.3 als neueste Version. „13.x“ im Stack bleibt richtig.
- **Quellen:** https://www.nuget.org/packages/Aspire.Hosting.AppHost · https://devblogs.microsoft.com/aspire/category/releases/
- **Korrektur:** Im Protokoll den Stand auf 13.6.1 aktualisieren. Im Stack „13.x (Stand 11.10.2026: 13.6.1)“ angeben.

## 9. xunit.v3 3.x – **veraltet** (Major-Version)

- **Gefunden:** `xunit.v3` **4.0.2** vom 09.10.2026. Version 4.0.0 erschien am 14.08.2026. Neu: Native AOT, volle Parallelisierung. Gestrichen: Microsoft Testing Platform v1 und Mono. Brüche betreffen vor allem Erweiterungs-Schnittstellen. Der Stack und die Konvention „Tests“ nennen 3.x.
- **Verträglichkeit:** `TngTech.ArchUnitNET.xUnitV3` 0.13.4 hängt nur von `xunit.v3.assert` (≥ 1.1.0) ab und sollte damit auch mit 4.x laufen. Das ist im Spike zu bestätigen.
- **Quellen:** https://www.nuget.org/packages/xunit.v3 · https://xunit.net/releases/v3/4.0.0
- **Empfohlene Korrektur:** Stack auf „xunit.v3 4.x“ ändern. In `global.json` den Test-Runner auf Microsoft Testing Platform festlegen (`"test": { "runner": "Microsoft.Testing.Platform" }`), passend zu xunit.v3 4 und `dotnet test` in .NET 10.

## 10. Testcontainers.PostgreSql 4.x – veraltet (Minor)

- **Gefunden:** 4.16.0 vom 08.10.2026, enthält net10.0. Das Protokoll nennt 4.13.0. Die Doku übergibt das Abbild ausdrücklich an den Builder: `new PostgreSqlBuilder("postgres:…")`. Das Beispiel nutzt noch `postgres:15.1`.
- **Quellen:** https://www.nuget.org/packages/Testcontainers.PostgreSql · https://dotnet.testcontainers.org/modules/postgres/
- **Korrektur:** Stand im Protokoll aktualisieren. Konvention „Tests“ ergänzen: „Abbild immer `postgres:18` angeben, nie den Standard aus Beispielen übernehmen.“

## 11. TngTech.ArchUnitNET (+ xUnitV3) 0.13.x – bestätigt

- **Gefunden:** `TngTech.ArchUnitNET.xUnitV3` 0.13.4 vom 20.08.2026 (Ziele netstandard2.0 und net472, auf net10.0 nutzbar). Das Protokoll nennt 0.13.3.
- **Quelle:** https://www.nuget.org/packages/TngTech.ArchUnitNET.xUnitV3
- **Korrektur:** Nur den Stand im Protokoll auf 0.13.4 setzen.

## 12. `Guid.CreateVersion7()` – bestätigt

- **Gefunden:** Seit .NET 9 vorhanden, in .NET 10 unverändert (RFC 9562, Zeit aus `DateTimeOffset.UtcNow`). Npgsql erzeugt seit EF 9 für `Guid`-Schlüssel ohnehin UUID v7 auf der Client-Seite. PostgreSQL 18 hat zusätzlich die eingebaute Funktion `uuidv7()`.
- **Quellen:** https://learn.microsoft.com/dotnet/api/system.guid.createversion7?view=net-10.0 · https://www.npgsql.org/efcore/release-notes/9.0.html · https://neon.com/postgresql/postgresql-18/uuidv7-support
- **Hinweis:** Die Konvention „Zeit“ verlangt eine „Uhr nur über `TimeProvider`“. `Guid.CreateVersion7()` ohne Parameter liest die Systemuhr direkt. Für testbare Kennungen `Guid.CreateVersion7(timeProvider.GetUtcNow())` vorschreiben. Im Browser (Web.Client) funktioniert dieselbe Methode in WebAssembly.

## 13. `.slnx` – bestätigt

- **Gefunden:** Ab .NET 10 erzeugt `dotnet new sln` standardmäßig `.slnx` (unterstützt seit SDK 9.0.200); alle großen Werkzeuge unterstützen es.
- **Quelle:** https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default
- **Korrektur:** keine.

## 14. Web Push auf iOS-Safari für Home-Screen-PWAs – bestätigt + Hinweis

- **Gefunden:**
  - Web Push auf iPhone/iPad funktioniert **nur für installierte Home-Screen-Web-Apps**, nicht im normalen Safari-Tab. Die Erlaubnis muss durch eine Nutzeraktion (Tippen) angefragt werden.
  - Die 2024 für die EU angekündigte Abschaltung von Home-Screen-Web-Apps (iOS 17.4) hat Apple **zurückgenommen**. Seitdem ist keine neue Einschränkung bekannt; eine ausdrückliche Bestätigung für iOS 26 fand sich nicht.
  - Seit iOS 18.4 gibt es **Declarative Web Push**: Benachrichtigungen funktionieren auch ohne Service Worker, rückwärtsverträglich zum Standard-Web-Push. Das macht die Zustellung auf iOS robuster.
- **Quellen:** https://webkit.org/?p=16535 (Meet Declarative Web Push) · https://notificare.com/blog/2024/03/08/apple-reverse-decision-to-remove-pwa/ · https://learn.microsoft.com/aspnet/core/blazor/progressive-web-app/push-notifications?view=aspnetcore-10.0
- **Empfohlene Korrektur:** AD-21 ergänzen: „Push setzt auf iOS die installierte Home-Screen-App voraus. Die Oberfläche erklärt das beim Einschalten von Push und fragt die Erlaubnis nur nach einem Tippen an. Die Nutzlast enthält Titel und Text im deklarativen Format, damit iOS auch ohne Service Worker anzeigen kann.“ In EXPERIENCE.md bzw. der Hilfe einen Installations-Hinweis für iPhone vorsehen.

## 15. .NET-Web-Push-Bibliotheken – Hinweis (Wahl ist zurückgestellt)

- **Gefunden:**
  - `WebPush` (web-push-csharp) 1.0.13 vom 28.04.2026, Ziele net8.0/netstandard2.1/net48, mit net10.0 verträglich. Hängt aber von `Portable.BouncyCastle` 1.9.0 ab, das seit 10/2021 nicht mehr gepflegt wird.
  - `Lib.Net.Http.WebPush` 3.3.1 vom 09.03.2025, Ziele bis net6.0 (läuft auf net10.0). Unter net5/net6 nutzt es die eingebaute Kryptografie von .NET, ohne BouncyCastle. Es bringt einen VAPID-Client und ein ASP.NET-Core-Muster für einen Push-Dienst mit. Neuere Versionen sind seit über 1,5 Jahren nicht erschienen.
- **Quellen:** https://www.nuget.org/packages/WebPush · https://www.nuget.org/packages/Lib.Net.Http.WebPush · https://www.nuget.org/packages/Portable.BouncyCastle
- **Empfohlene Korrektur:** Die Zurückstellung ist richtig. Den Auslöser um ein Kriterium ergänzen: „Bevorzugt `Lib.Net.Http.WebPush` (keine BouncyCastle-Abhängigkeit unter net6+). Vor dem Einsatz `dotnet list package --vulnerable --include-transitive` prüfen.“ .NET 10 prüft beim `restore` auch indirekte Abhängigkeiten auf Schwachstellen. Notfalls lässt sich RFC 8291/8292 mit `ECDiffieHellman`, `HKDF` und `AesGcm` aus .NET selbst in einem Adapter bauen.

## 16. Offline-Speicher auf iOS (AD-18) – Hinweis

- **Gefunden:** WebKit räumt per Skript geschriebenen Speicher (IndexedDB) von Websites, die eine Weile nicht genutzt wurden (7-Tage-Regel in Safari). Home-Screen-Web-Apps sollten laut WebKit davon ausgenommen sein bzw. ihre Tage selbst zählen. Fehlerberichte zeigen aber Datenverlust auch bei installierten Apps. Eine aktuelle Bestätigung für iOS 26 fand sich nicht.
- **Quellen:** https://webkit.org/?p=14403 (Updates to Storage Policy) · https://bugs.webkit.org/show_bug.cgi?id=237350
- **Empfohlene Korrektur:** AD-18 ergänzen: „Offline-Daten nur in der installierten App; beim Einrichten `navigator.storage.persist()` anfragen; die App zeigt an, wann die Offline-Daten zuletzt geladen wurden, und lädt sie bei jedem Öffnen mit Netz neu.“ Für die Lagerleitung: Offline-Paket am Vorabend bzw. bei Abfahrt aktiv laden lassen.

---

## Weitere kleine Befunde

- **Identity-Seiten und Service Worker:** Die Identity-Seiten der Vorlage (`/Account/*`) sind statisches SSR. Der eigene Service Worker muss sie (wie öffentliche Seiten) vom Navigations-Fallback ausnehmen (siehe Punkt 2). Sonst wird offline bzw. bei schwachem Netz die App-Hülle statt der Anmeldeseite geliefert.
- **Alte App-Versionen:** Eine offline-fähige PWA läuft beim Nutzer oft noch eine Version zurück. Die Microsoft-Doku warnt: Keine rückwärts-inkompatiblen API-Änderungen ausliefern, solange alte Clients unterwegs sind. Das ist relevant für die Konvention „Ereignisse versioniert“ und für `/api/*`. Empfehlung: In die Konventionen aufnehmen, dass Endpunkte nur abwärtskompatibel erweitert werden. Bei Bruch prüft der Client seine Version und fordert zum Neuladen auf.
- **.NET 10 `restore`:** NuGet prüft seit .NET 10 auch indirekte Pakete auf bekannte Schwachstellen (Breaking Change). Mit `TreatWarningsAsErrors` kann das den Build stoppen. Das passt zu „Architekturtests bei jedem Build“, sollte aber bewusst eingestellt werden.

## Zusammenfassung der empfohlenen Änderungen am Gerüst

1. **AD-5:** App-Bereich unter festem Pfad, global `InteractiveWebAssembly` mit `prerender: false`; öffentliche Seiten und `/Account/*` statisches SSR mit `[ExcludeFromInteractiveRouting]`.
2. **AD-18:** Eigener Service Worker mit App-Hülle nur für den App-Pfad, Offline-Anmeldezustand, `storage.persist()`, Durchstich (Spike) in V1 auf Android und iOS; Rückfall-Option: eigenständige WASM-PWA unter `/offline`. Auslöser „PWA-Option für Blazor Web App (.NET 12)“ in „Zurückgestellt“.
3. **AD-12:** Login-Link über das AD-11-Muster, Einlösen per POST; Passkeys als Option; Identity-Schema `Version3`.
4. **AD-19 + Konvention „Schreiben“:** `uint Version` + `IsRowVersion()`; jede PATCH-Antwort liefert das neue Token.
5. **Stack:** .NET 10.0.12 / SDK 10.0.401, Aspire 13.6.1, **xunit.v3 4.x**, Testcontainers 4.16.0, ArchUnitNET 0.13.4, Npgsql 10.0.3; PostgreSQL-Abbild ausdrücklich `postgres:18` in AppHost und Tests.
6. **AD-21:** iOS nur als Home-Screen-App, Erlaubnis nach Tippen, deklaratives Push-Format; Bibliotheks-Kriterium „ohne BouncyCastle“.
