# Lokal starten und prüfen

## Voraussetzungen

- .NET SDK 10.0.100 (durch `global.json` festgelegt)
- Docker Desktop — der AppHost startet PostgreSQL als Container
- Node.js — nur für die Playwright-Browser nötig

## Starten

```powershell
./run-local.ps1
```

Der Aspire-AppHost bringt PostgreSQL, Schnittstelle und Oberfläche hoch und
öffnet sein Dashboard. Die Adressen der einzelnen Dienste stehen dort — sie
wechseln zwischen Läufen, also immer im Dashboard nachsehen statt Ports zu raten.

## Tests

```bash
dotnet test
```

Läuft die gesamte Solution. Integrationstests starten ihre eigene PostgreSQL
über Testcontainers; dafür muss Docker laufen. Es wird keine bereits laufende
Datenbank benutzt und keine bestehende verändert.

## Oberfläche prüfen (Smoke-Test)

Der Smoke-Test läuft über die Browser-Werkzeuge gegen die vom Dashboard
gemeldete Adresse der Oberfläche:

1. `./run-local.ps1` starten und im Dashboard warten, bis `client` grün ist.
2. Adresse von `client` öffnen.
3. Anmelden mit einem der unten genannten Entwicklungskonten.
4. Den Weg gehen, den der Lauf verändert hat — mindestens: anmelden,
   Organisation öffnen, Freizeit anlegen.
5. Konsolen- und Netzwerkmeldungen auf Fehler durchsehen.

## Entwicklungskonten

Nur in der Umgebung `Development` vorhanden, per Seed angelegt. Diese Konten
existieren produktiv nicht und dürfen dort nie entstehen.

Kennwort überall: `Entwicklung!1`

| Konto | Name | Rolle |
|---|---|---|
| `betreiber@zeltlotse.local` | Bea Betreiber | GlobalAdmin, gehört zu keiner Organisation |
| `leitung@zeltlotse.local` | Lena Leitner | Organisationsleitung Musterstadt |
| `freizeit@zeltlotse.local` | Frieder Falk | Freizeitleitung Sommerfreizeit, dazu Mitglied im Bezirksjugendwerk |
| `team@zeltlotse.local` | Timo Teichmann | Mitarbeit Sommerfreizeit |

## Zurücksetzen

Die Entwicklungsdaten entstehen nur in einer leeren Datenbank. Wer sie
verschmutzt hat und einen sauberen Stand braucht — etwa für reproduzierbare
Bilder oder eine Prüfung — löscht den Datenträger:

```powershell
docker volume rm zeltlotse.apphost-3c34764b2f-postgres-data
```

Beim nächsten `./run-local.ps1` migriert und seedet die Anwendung neu.

## Bilder für die Anleitung

```bash
dotnet run --project tools/Zeltlotse.Screenshots
```

Erwartet eine laufende Anwendung und schreibt nach `user-docs/bilder/`.
