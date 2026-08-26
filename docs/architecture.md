# Architektur — Zeltlotse

Dieses Dokument ist das jeweils aktuelle Bild des Systems. Jeder Lauf schreibt
seine Veränderung hierher zurück; es beschreibt nie einen Wunschzustand.

**Stand:** Lauf 2026-08-26-zeltlotse-grundgeruest (Bootstrap)

## Überblick

Eine mandantenfähige Webanwendung für die Freizeitplanung christlicher Träger.
Ein Träger ist ein Mandant; unter ihm leben Freizeiten. Getrennte Auslieferung
von Oberfläche und Schnittstelle, orchestriert durch .NET Aspire.

```
Browser
  │
  ├── app.zeltlotse.de   Zeltlotse.Client        Blazor WebAssembly, eigenständig
  │                                              (lokal: Aspire-DevServer)
  └── api.zeltlotse.de   Zeltlotse.Server        ASP.NET Core Minimal API
                              │
                              └── PostgreSQL     eine Datenbank, alle Mandanten
```

Beide Adressen liegen unter derselben registrierbaren Domain. Dadurch gilt das
Refresh-Cookie (`Domain=.zeltlotse.de`, `SameSite=Lax`) als Erstanbieter-Cookie
und ist von den Beschränkungen für Drittanbieter-Cookies nicht betroffen.

## Projekte

| Projekt | Rolle |
|---|---|
| `Zeltlotse.AppHost` | Aspire-Orchestrierung: Postgres, Server, Client |
| `Zeltlotse.ServiceDefaults` | Telemetrie, Health Checks, Service Discovery, Resilienz |
| `Zeltlotse.Server` | Schnittstelle, Autorisierung, Datenzugriff |
| `Zeltlotse.Client` | Blazor-WebAssembly-Oberfläche |
| `Zeltlotse.Core.<Feature>` | fachliche Scheibe, Typen `internal` |
| `Zeltlotse.Core.<Feature>.Contracts` | öffentliche Verträge einer Scheibe |
| `Zeltlotse.Components` | Razor Class Library, wächst je Bildschirm |

Scheiben kennen einander ausschließlich über `.Contracts`. Nur der Host
verweist auf Implementierungsprojekte; jede Scheibe registriert sich über eine
einzige `Add<Feature>()`-Erweiterung. Grenzverletzungen sind Compilerfehler —
genau das ist der Zweck.

## Mandantentrennung

Eine Datenbank für alle Mandanten. Jede mandantenbehaftete Tabelle trägt eine
`TenantId`. Zwei voneinander unabhängige Netze:

1. **EF-Core-Query-Filter** — der Regelfall im Anwendungscode.
2. **Row-Level-Security in PostgreSQL** — greift auch dann, wenn jemand den
   Filter umgeht oder vergisst.

Der Mandant wird aus dem Pfad `/o/{slug}` gelesen und gegen die Zuordnungen des
angemeldeten Nutzers geprüft, bevor irgendeine Abfrage läuft.

## Identität

ASP.NET Core Identity. Der Client hält ein kurzlebiges Zugriffstoken im
Arbeitsspeicher; das Erneuerungstoken liegt als HttpOnly-Cookie beim Server. Ein
Neuladen der Seite holt sich ein frisches Zugriffstoken über das Cookie.

## Autorisierung

Rollen sind additiv, es gilt das jeweils weitergehende Recht. Zwei
Zuordnungstabellen (Organisation, Freizeit), GlobalAdmin als Kennzeichen am
Nutzer. Es gibt keinen Durchgriff von oben: Der Betreiber sieht keine Inhalte.

## Noch nicht entschieden

Nichts. Offene Punkte werden hier festgehalten, sobald sie entstehen.
