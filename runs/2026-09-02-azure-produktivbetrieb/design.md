# Design — Zeltlotse produktiv auf Azure bringen

Maintenance-Lauf, kein UX-Konzept nötig (keine neue Oberfläche, kein neues
Nutzerverhalten). Dieses Dokument beschreibt, was angefasst wird und warum.

## Was sich ändert

- Neu: `infra/` (Bicep), `src/Zeltlotse.Server/Dockerfile`,
  `src/Zeltlotse.Client/wwwroot/staticwebapp.config.json`,
  `.github/workflows/deploy.yml`, `.github/workflows/cleanup-images.yml`.
- Geändert: `Zeltlotse.Core.Persistenz` (Provider, Interceptor, Migrationen),
  `Zeltlotse.Server/Program.cs` + `Startvorgang.cs`,
  `Zeltlotse.AppHost/AppHost.cs`,
  `tests/Zeltlotse.Server.Integration.Tests/DatenbankFixture.cs`,
  `Zeltlotse.ServiceDefaults/Extensions.cs` (Health-Endpunkt auch in
  Produktion), `docs/architecture.md`, `foundation.md`.

## Architektur-Delta

Produktionstopologie (ersetzt den bisherigen, rein lokalen Aspire-Aufbau):

```
Browser
  │
  ├── app.zeltlotse.de   Azure Static Web App          Blazor-WASM-Dateien, statisch
  └── api.zeltlotse.de   Container App "zeltlotse-server"   ASP.NET Core Minimal API
                              │
                              └── Azure SQL Database        eine Datenbank, alle Mandanten
                                  (Serverless General Purpose, Free-Tier,
                                   AAD-only-Auth — kein SQL-Passwort)
```

Der Client läuft nicht mehr als eigene Container App, sondern als **Azure
Static Web App** — ein eigener, kostenloser Azure-Dienst genau für solche
reinen Single-Page-Apps: eingebautes SPA-Routing (Fallback auf `index.html`
über `staticwebapp.config.json`, kein selbst gepflegtes nginx nötig),
automatisches kostenloses SSL-Zertifikat, 100 GB Bandbreite/Monat kostenlos.
Server läuft weiterhin als Container App (Germany West Central), Zugriff auf
die Datenbank über Managed Identity statt Passwort. Lokale Entwicklung bleibt
bei Aspire, jetzt mit SQL-Server-Container statt Postgres-Container —
Entwicklung und Produktion nutzen dieselbe Datenbank-Engine.

**Mandantentrennung** bleibt konzeptionell unverändert (zwei unabhängige
Netze: EF-Core-Query-Filter + Row-Level-Security), nur die
Row-Level-Security-Umsetzung wechselt von PostgreSQL-Policies auf SQL
Servers `CREATE SECURITY POLICY` + Prädikatfunktion. Die Übergabe der
sichtbaren Organisationen an die Datenbank wechselt von
`SELECT set_config(...)` auf `sp_set_session_context`.

Der Rest der Architektur (Identität, Autorisierung, die Ausnahme für
Einladungen) ist von diesem Lauf nicht betroffen.

## Persistenz-Portierung

1. **Pakete:** `Npgsql.EntityFrameworkCore.PostgreSQL` und
   `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` raus (Letzteres war ohnehin
   nie genutzt — `Program.cs` ruft `UseNpgsql` manuell auf, nicht die
   Aspire-Erweiterung). Rein: `Microsoft.EntityFrameworkCore.SqlServer`.
2. **`MandantInterceptor`:** `SET ROLE` + zwei
   `SELECT set_config(...)`-Aufrufe werden durch zwei
   `EXEC sp_set_session_context @key = N'…', @value = …, @read_only = 1`
   ersetzt (`tenant_ids`, `betreiber`). `SESSION_CONTEXT` ist an die
   physische Verbindung gebunden; ADO.NETs Connection-Pool setzt sie beim
   Wiederverwenden einer gepoolten Verbindung über `sp_reset_connection`
   automatisch zurück — ein neuer Interceptor-Aufruf bei jedem
   `ConnectionOpenedAsync` reicht, kein Leck zwischen Mandanten. Ein
   Integrationstest sichert das explizit ab (zwei Mandanten, dieselbe
   gepoolte Verbindung, keine Vermischung).
3. **Row-Level-Security in T-SQL:** eine `SCHEMABINDING`-Inline-Tabellenfunktion
   liest `SESSION_CONTEXT(N'tenant_ids')` (kommagetrennt, über `STRING_SPLIT`
   ausgewertet) und wird per `CREATE SECURITY POLICY` mit
   `ADD FILTER PREDICATE` und `ADD BLOCK PREDICATE` an jede mandantenbehaftete
   Tabelle gehängt — fachlich identisch zur bisherigen Postgres-Policy,
   andere Syntax. Die zweite Ausnahme (`app.betreiber`) bekommt eine eigene
   Prädikatfunktion, wie bisher nur auf der Einladungstabelle.

   **Abweichung von der ursprünglichen Annahme (während der Implementierung
   entdeckt):** In PostgreSQL reichte für den Wartungsmodus (Migrationen,
   Aufräumdienst) das Unterlassen von `SET ROLE` — die Verbindung blieb auf
   ihrer privilegierten Basisrolle, die von Natur aus nicht der
   Richtlinie unterlag. SQL Server unter Azure-AD-Auth kennt keine solche
   privilegierte Basisrolle: jede Verbindung läuft unter derselben,
   eingeschränkten Identität. Der Wartungsmodus setzt deshalb explizit
   dieselbe `system_bypass`-Sitzungsvariable wie `SystemDatenbank`, statt nur
   das Setzen der Mandanten-Variablen zu unterlassen.
4. **Migrationen:** die 4 bestehenden Postgres-Migrationen werden nicht
   einzeln portiert, sondern durch eine frische
   `dotnet ef migrations add InitialSqlServer` ersetzt — das Schema ist
   klein und enthält nichts Postgres-Spezifisches (keine `jsonb`-Spalten,
   keine Arrays, keine Erweiterungen). Die alten Migrationen bleiben als
   Referenz unter einem eigenen Unterordner erhalten, laufen aber nicht mehr.
5. **`EntwurfszeitFabrik`:** Design-Zeit-Connection-String auf SQL Server
   umgestellt (lokale SQL-Server-Instanz oder LocalDB reicht, da nur der
   Provider für `dotnet ef` zählt).
6. **`DatenbankFixture`:** `Testcontainers.PostgreSql` → `Testcontainers.MsSql`
   (offizielles Modul, Image `mcr.microsoft.com/mssql/server`).
7. **`AppHost.cs`:** `builder.AddPostgres(...)` → `builder.AddSqlServer(...)`
   (`Aspire.Hosting.SqlServer`), damit lokale Entwicklung zur Produktion
   passt.
8. **`Zeltlotse.ServiceDefaults/Extensions.cs`:** Health-Endpunkte (`/health`,
   `/alive`) werden nicht mehr nur in `IsDevelopment()` gemappt, sondern
   immer — Container Apps braucht sie für Liveness/Readiness-Probes auch in
   Produktion.

## Migrations-Ausführung beim Deploy

`Startvorgang.VorbereitenAsync` verliert den unbedingten
`Database.MigrateAsync()`-Aufruf beim Start (behebt die in `debt.md`
vermerkte Wettlauf-Gefahr an der Wurzel, statt sie auf die neue Datenbank zu
übertragen). Stattdessen bekommt `Zeltlotse.Server` einen Kommandozeilen-Zweig
(z.B. `--migrate-only`), der nur die Migration ausführt und beendet — genutzt
von einem `Microsoft.App/jobs`-Container-Apps-Job, den der Deploy-Workflow vor
jedem Rollout einmalig startet und auf Erfolg wartet. Erst nach erfolgreicher
Migration bekommt die neue Server-Revision Datenverkehr. Schlägt die
Migration fehl, bricht der Workflow ab; die alte Revision läuft unverändert
weiter (kein automatischer Rollback, siehe Nicht-Ziele in requirements.md).

## Infrastruktur (`infra/`, Bicep)

Flach gehalten, sieben Module, kein Subscription-übergreifendes Setup:

- `main.bicep` (Subscription-Scope): Resource Group + Modul-Aufrufe.
- `log-analytics.bicep`: `Microsoft.OperationalInsights/workspaces`
  (30 Tage Aufbewahrung).
- `container-apps-env.bicep`: `Microsoft.App/managedEnvironments`.
- `sql.bicep`: `Microsoft.Sql/servers` (AAD-only-Auth, kein SQL-Login) +
  `Microsoft.Sql/servers/databases` (`GP_S_Gen5_2`, `useFreeLimit: true`,
  `freeLimitExhaustionBehavior: 'AutoPause'`) + Firewall-Regel
  `AllowAllWindowsAzureIps` (Container Apps hat ohne NAT Gateway keine feste
  ausgehende IP; vertretbar, weil AAD-only-Auth kein erratbares Passwort
  bietet).
- `identities.bicep`: eine User-Assigned Managed Identity für den Server
  (DB-Zugriff); der Client braucht keine, da Static Web Apps ohne
  Registry-Pull auskommt.
- `key-vault.bicep`: Azure Key Vault (RBAC-Autorisierung) mit zwei Secrets
  (JWT-Signierschlüssel, ghcr-Token) und einer Rollenzuweisung
  ("Key Vault Secrets User") für die Server-Identität. Nachtrag aus dem
  Retro dieses Laufs — siehe unten, Abschnitt "Secrets".
- `container-app-server.bicep`: `Microsoft.App/containerApps`, inkl. des
  Migrations-Jobs als `Microsoft.App/jobs` (Trigger-Typ `Manual`) neben der
  Server-App. Zusätzlich `Microsoft.App/containerApps/hostnames` +
  verwaltetes Zertifikat für `api.`.
- `static-web-app.bicep`: `Microsoft.Web/staticSites` (Free-Tier-SKU) für den
  Client, inkl. Custom-Domain-Bindung für `app.` (eigenes,
  Static-Web-Apps-internes Verfahren, unabhängig von Container Apps).

Die Rechtevergabe der Server-Identity auf die Datenbank
(`CREATE USER … FROM EXTERNAL PROVIDER` + Rollenmitgliedschaft) läuft nicht
über eine zusätzliche, kostenpflichtige Bicep-`deploymentScript`-Ressource,
sondern als eigener Schritt im Deploy-Workflow (`sqlcmd`, per OIDC-Token
authentifiziert) — weniger deklarativ, dafür ohne zusätzliche Azure-Ressource.

## Dockerfile

Nur noch für den Server: `Zeltlotse.Server/Dockerfile`, zweistufig,
`sdk:10.0` → `aspnet:10.0`, Port 8080, `ASPNETCORE_URLS=http://+:8080`. Der
Client braucht kein Docker-Image mehr — Static Web Apps deployt den
`dotnet publish`-Output direkt.

`staticwebapp.config.json` im Client-Projekt sorgt für das SPA-Fallback
(`navigationFallback` → `/index.html`). Die produktive API-Adresse wird beim
Build in `wwwroot/appsettings.json` eingesetzt (einfacher Ersetzungsschritt
im Workflow, unkritisch bei nur einer Umgebung).

## GitHub-Actions-Workflows

**`deploy.yml`** (Push auf `main`): bestehendes Build/Test aus `build.yml`
(als wiederverwendbarer `workflow_call`, um Duplikat zu vermeiden) →
Server-Image bauen und zu `ghcr.io` pushen; Client per
`dotnet publish` bauen → OIDC-Login (`azure/login@v2`,
`permissions: id-token: write`) → Bicep-Deployment
(`az deployment sub create` gegen `infra/main.bicep`) → SQL-Rechtevergabe
per `sqlcmd`-Schritt → Migrations-Job starten und auf Erfolg warten →
Server-Container-App auf den neuen Image-Tag aktualisieren → Client per
`Azure/static-web-apps-deploy@v1` ausrollen (authentifiziert über das
Static-Web-Apps-Deployment-Token, siehe Secrets unten — der einzige
gespeicherte Azure-Zugangsdaten-Baustein in der gesamten Pipeline).

**`cleanup-images.yml`** (wöchentlich, `schedule`): behält die letzten 10
Versionen des Server-Images (`zeltlotse-server`) in der GitHub Container
Registry, löscht ältere.

Einmalig von Hand einzurichten (nicht Teil des Workflows): Azure-AD-App-
Registrierung mit föderierter Anmeldeinformation
(Issuer `https://token.actions.githubusercontent.com`,
Subject `repo:maSchoeller/Zeltlotse:ref:refs/heads/main`), Rollenzuweisung
auf die Resource Group, sowie die GitHub-Repo-Variablen
`AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID`.

## Secrets

**Nachtrag aus dem Retro (2026-09-02):** Ursprünglich waren
Container-Apps-eigene Secrets statt Azure Key Vault vorgesehen (kleiner,
weniger Ressourcen). Im Retro wurde das bewusst revidiert: JWT-Signierschlüssel
und ghcr-Token liegen jetzt in Azure Key Vault (`key-vault.bicep`,
RBAC-Autorisierung) und werden per Key-Vault-Referenz in die Container App
eingebunden — die Server-Identität bekommt dafür die Rolle
"Key Vault Secrets User" auf dem Vault. Die SQL-Verbindung selbst enthält
weiterhin dank AAD-Auth kein Passwort und ist kein Secret.

Die Rechtevergabe der Server-Identität auf die SQL-Datenbank
(`CREATE USER … FROM EXTERNAL PROVIDER`) bleibt bewusst ein Schritt im
GitHub-Actions-Workflow statt einer Bicep-`deploymentScript`-Ressource:
Eine deklarative Umsetzung wäre technisch möglich (eine Managed Identity
darf laut Microsoft-Dokumentation als Azure-AD-Verwalter der SQL-Datenbank
eingesetzt werden), müsste aber `sqlcmd` zur Laufzeit in einem isolierten
Azure-Container nachinstallieren — genauso ungetestet und fragil wie der
jetzige Workflow-Schritt, nur an einer schwerer zu kontrollierenden Stelle.
Kein Sicherheitsgewinn, nur verlagertes Risiko — deshalb bewusst nicht
umgesetzt.

Einzige Ausnahme vom sonst durchgängigen OIDC-Prinzip (kein gespeichertes
Azure-Zugangsdatum): das Static-Web-Apps-Deployment-Token, als GitHub-Secret
hinterlegt. Es ist auf genau diese eine Ressource beschränkt (kein
Subscription-weiter Zugriff) — der von Microsoft empfohlene und übliche Weg
für diesen Dienst, echtes OIDC ist dafür nicht vorgesehen.

## Sequenzierung (erster Deploy)

1. Build+Test.
2. Images bauen und pushen.
3. Bicep-Deployment **ohne** Custom-Domain-Bindung (Server-Container-App und
   Static Web App bekommen zunächst nur ihre Azure-Standardadressen).
4. Manuell: DNS-Einträge bei `zeltlotse.de` setzen — CNAME `api` +
   `asuid`-TXT-Verifizierung für die Container App, CNAME `app` für die
   Static Web App (dort reicht bei einer Subdomain der CNAME allein, ohne
   zusätzliche TXT-Prüfung).
5. Zweiter Bicep-Deployment-Lauf ergänzt beide Custom-Domain-Bindungen
   (`container-app-server.bicep`, `static-web-app.bicep`).
6. Ab dann: jeder weitere Push durchläuft nur noch den vollen, unveränderten
   Workflow — kein Sonderfall mehr.

Der initiale JWT-Signierschlüssel wird vor dem allerersten Rollout einmalig
per `az containerapp secret set` gesetzt.

## Entscheidungen mit ihrem Preis

Siehe `debt.md` dieses Laufs für die dazugehörigen Einträge (Firewall-Regel,
Secrets statt Key Vault, Rechtevergabe per Skript statt IaC, Image-Retention
nach fester Anzahl, zweiphasiger erster Deploy).
