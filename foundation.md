# Foundation — Zeltlotse

Zwischenspeicher für Entscheidungen. Details: `docs/architecture.md`, `design-system.md`, `docs/local-testing.md`.

## Stack

- .NET 10 (SDK 10.0.100 per `global.json`), ASP.NET Core Minimal API,
  vertikale Scheiben nach Preset `dotnet-cloud`
- Blazor WebAssembly, eigenständig ausgeliefert
- PostgreSQL + EF Core mit Row-Level-Security; Aspire 13.5 als Orchestrierung
- xUnit; Integrationstests via Testcontainers, Oberfläche via Playwright;
  Ziel: Azure Container Apps, Germany West Central

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
