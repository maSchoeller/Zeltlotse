# Architektur — Zeltlotse

Dieses Dokument ist das jeweils aktuelle Bild des Systems. Jeder Lauf schreibt
seine Veränderung hierher zurück; es beschreibt nie einen Wunschzustand.

**Stand:** Lauf 2026-08-26-zeltlotse-grundgeruest

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

## Die eine Ausnahme

Eine Einladung wird eingelöst, bevor jemand angemeldet ist — es gibt also keine
Organisationsliste, aus der sich die Row-Level-Security speisen könnte, und die
Datenbank verweigert selbst die Einladung, die geprüft werden soll.

Dafür öffnet `SystemDatenbank` eine eigene, kurzlebige Verbindung ohne
Mandantenschranke. Sie gilt für genau drei Zugriffe: das Nachschlagen der
Einladung, das Anlegen der Freizeitzuordnung und das Abhaken der Einladung. Die
laufende Anfrage behält ihren Schutz.

Zweite Ausnahme, in der Richtlinie selbst: Der Betreiber gehört zu keiner
Organisation, muss aber Leitungen einladen. Allein die Einladungstabelle kennt
dafür ein zweites Recht (`app.betreiber`). Freizeiten und Zuordnungen bleiben
ihm auch auf Datenbankebene verschlossen.

## Noch nicht entschieden

Nichts. Offene Punkte werden hier festgehalten, sobald sie entstehen.
