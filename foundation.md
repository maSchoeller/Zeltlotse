# Foundation — Zeltlotse

Zwischenspeicher für Entscheidungen. Details: `docs/architecture.md`, `design-system.md`, `docs/local-testing.md`, `docs/deployment.md`.

## Stack

- .NET 10 (SDK 10.0.100 per `global.json`), ASP.NET Core Minimal API,
  vertikale Scheiben nach Preset `dotnet-cloud`
- Blazor WebAssembly, eigenständig ausgeliefert
- Azure SQL Database + EF Core mit Row-Level-Security; Aspire 13.5 als
  lokale Orchestrierung (SQL-Server-Container)
- xUnit; Integrationstests via Testcontainers, Oberfläche via Playwright
- Produktiv: Azure Container Apps, Germany West Central; Infrastruktur als
  Bicep unter `infra/`, Deploy automatisiert per GitHub Action bei Push auf
  `main` (siehe `runs/2026-09-02-azure-produktivbetrieb/design.md`)

## Befehle

| Zweck | Befehl |
|---|---|
| Starten | `./run-local.ps1` |
| Testen | `dotnet test` |
| Bauen | `dotnet build` |

## Smoke-Test

Browser-Werkzeuge gegen die vom Aspire-Dashboard für `client` gemeldete
Adresse. Ablauf und Entwicklungskonten: `docs/local-testing.md`.

## Regeln

`design-system.md` ist verbindlich (Primärfarbe `#29447B`, Sarabun, feste Abstandsskala). Code englisch, Fachbegriffe deutsch; Oberfläche deutsch.
