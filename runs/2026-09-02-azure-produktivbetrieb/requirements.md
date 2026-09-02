# Requirements — Zeltlotse produktiv auf Azure bringen

## Einstufung

Maintenance-Lauf: reine Infrastruktur-/Deployment-Arbeit, keine neue
Nutzeroberfläche, kein neues Nutzerverhalten. Stakeholder ist der Entwickler
(Marvin), nicht ein Endnutzer-Wunsch. Sprache bleibt technisch.

## Problem

Zeltlotse läuft bisher ausschließlich lokal (Aspire-Orchestrierung für die
Entwicklung). Es existiert weder eine produktive Umgebung noch ein
automatisierter Weg dorthin: keine Dockerfiles, keine Infrastructure-as-Code,
kein Deploy-Workflow. `foundation.md` legt das Zielsystem bereits fest,
umgesetzt ist davon nichts.

## Ziel

Zeltlotse läuft erreichbar unter `https://app.zeltlotse.de` (Client) und
`https://api.zeltlotse.de` (Server) auf Azure Container Apps in der Region
Germany West Central. Jeder Push auf `main` deployt automatisch über eine
GitHub Action. Die gesamte Infrastruktur ist als Code (Bicep) hinterlegt und
reproduzierbar.

Im Zuge dessen wechselt die Datenbank von PostgreSQL zu Azure SQL Database,
weil Azure SQL im Gegensatz zu Azure Database for PostgreSQL einen
dauerhaften (nicht zeitlich befristeten) kostenlosen Tarif bietet. Das
schließt die Portierung der bestehenden Mandantentrennung
(Row-Level-Security — eine Datenbankfunktion, die Zeilen einer Tabelle
automatisch auf die passende Organisation einschränkt, direkt in der
Datenbank erzwungen statt nur im Anwendungscode) von PostgreSQL- auf
SQL-Server-Syntax mit ein.

## Bereits getroffene Entscheidungen (nicht erneut zu klären)

- **Region/Compute:** Azure Container Apps, Germany West Central (aus
  `foundation.md`).
- **Azure-Subscription:** neue, dedizierte Subscription nur für Zeltlotse.
- **Domain:** `zeltlotse.de`, vorhanden, DNS selbst verwaltbar. `app.` und
  `api.` als Subdomains (nötig, damit das Refresh-Cookie funktioniert).
- **Umgebungen:** nur Produktion, kein Staging.
- **Container Registry:** GitHub Container Registry (ghcr.io), kostenlos.
- **Image-Aufbewahrung:** automatische Aufräumregel wird jetzt eingerichtet
  (z.B. nur die letzten N Images pro Anwendung behalten — genaue Anzahl ist
  Design-Entscheidung).
- **Datenbank:** Azure SQL Database, Serverless General Purpose, dauerhafter
  Free-Tier (100.000 vCore-Sekunden + 32 GB Speicher/Monat).
- **GitHub↔Azure-Authentifizierung:** OIDC (kein gespeicherter
  Azure-Schlüssel als GitHub-Secret).
- **Secrets:** Azure Key Vault für JWT-Signierschlüssel und ghcr-Token, per
  Key-Vault-Referenz in die Container App eingebunden (Nachtrag aus dem
  Retro: ursprünglich waren Container-Apps-eigene Secrets vorgesehen).
- **Migrationsausführung:** als einmaliger Schritt im Deploy-Workflow, nicht
  mehr beim App-Start (behebt die in `debt.md` vermerkte Wettlauf-Gefahr bei
  mehreren Instanzen).
- **Migrationsfehler beim Deploy:** Abbruch, alte Version bleibt aktiv, keine
  Automatik für Rollback.
- **Free-Tier-Kontingent aufgebraucht:** Risiko wird bewusst in Kauf
  genommen, keine Warn-Automatik — Eintrag in `debt.md`.
- **Erster Deploy:** DNS-Bindung und initialer JWT-Signierschlüssel
  (Schlüssel zum Signieren der Anmelde-Tokens) werden einmalig manuell
  gesetzt, nicht automatisiert erzeugt.

Ausführliche technische Marschroute (Bicep-Ressourcen, Dockerfile-Aufbau,
Persistenz-Portierung, Workflow-Schritte, Sequenzierung) siehe der genehmigte
Plan: `C:\Users\micro\.claude\plans\ich-m-chte-den-zeltlotse-zazzy-brook.md`.
Design-Phase arbeitet diesen zu `design.md` aus.

## Akzeptanzkriterien

- `https://app.zeltlotse.de` liefert die Blazor-Oberfläche aus, erreichbar
  per Browser.
- `https://api.zeltlotse.de/health` antwortet erfolgreich.
- Ein Push auf `main` löst automatisch Build, Test, Image-Push, Infrastruktur-
  Deploy, Datenbank-Migration und Container-Apps-Update aus, ohne manuellen
  Eingriff (abgesehen vom einmaligen Erst-Setup: DNS-Eintrag, initialer
  JWT-Schlüssel, Azure-AD-App-Registrierung für OIDC).
- Die Mandantentrennung funktioniert auf Azure SQL identisch zur bisherigen
  PostgreSQL-Umsetzung (durch Integrationstests gegen einen echten
  SQL-Server-Testcontainer nachgewiesen, nicht nur gegen eine gemockte
  Datenbank).
- `dotnet build` und `dotnet test` bleiben grün.
- Lokale Entwicklung (`run-local.ps1`) läuft weiterhin, jetzt gegen SQL
  Server statt PostgreSQL, damit Entwicklung und Produktion nicht
  auseinanderlaufen.

## Nicht-Ziele

- Kein Staging/Test-Environment in diesem Lauf.
- Keine automatische Warnung bei Free-Tier-Erschöpfung.
- Kein automatischer Rollback bei fehlgeschlagener Migration.
- Keine neuen fachlichen Funktionen für Zeltlotse selbst — reine
  Infrastruktur- und Persistenz-Migrationsarbeit.
