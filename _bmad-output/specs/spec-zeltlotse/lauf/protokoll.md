# Lauf-Protokoll – autonomer Bau der Zeltlotse-MVP

Gepflegt vom Orchestrator. Eine Zeile je Durchlauf, nur anhängen. Maßgeblich für den Stand einer Story ist der `status` in `stories/<id>-*.md`; dieses Protokoll ist der Überblick und der Wiedereinstieg nach einer Kontext-Verdichtung.

Branch: `mvp` · Start: 2026-10-11 12:30 · Ende: –

## Durchläufe

| Zeit | Story | Ergebnis | Folgedurchlauf | Regression (Tests / Doku-Build) | Commit | Bemerkung |
|---|---|---|---|---|---|---|
| 2026-10-11 12:30 | 0-1 | Abbruch (Umgebung) | – | – | – | Claude-CLI: „OAuth session expired and could not be refreshed“; Story-Sitzung startet nicht. Lauf gestoppt, nach erneuter Anmeldung (`claude` → `/login`) fortsetzen. |
| 2026-10-11 13:31 | 0-1 | done | ja (done, keiner mehr) | grün 102/102 / entfällt | eebea36 | Neustart nach CLI-Anmeldung; Folge-Review: Lese-Kontext DB-seitig schreibgeschützt |
| 2026-10-11 14:47 | 0-2 | done | ja (done, keiner mehr) | grün 255/255 / entfällt | ea47879 | Offen für 0-3: Antiforgery-Header vs. Formularfeld, Rate-Limit 20/min/IP im Lager-WLAN; Backup-Restore stellt gelöschte Personenschlüssel wieder her (zurückgestellt) |
| 2026-10-11 16:18 | 0-3 | done | ja (done, keiner mehr) | grün 452/452 / entfällt | 2be3eeb | Doku-Seite und Screenshots der App-Hülle folgen mit 0-4; offen für 1-1: Offline-Markierung nach Netzfehler, Autosave-Verhalten bei 400/403/404 |

## Entscheidungen im Lauf

Entscheidungen, die der Orchestrator bei blockierten Stories getroffen hat (nach den Leitprinzipien, ohne Rückfrage). Marvin prüft sie bei der Abnahme.

## Übersprungene Stories

Stories, die nach zwei Versuchen blockiert blieben: Grund, Patch-Datei unter `lauf/patches/`, betroffene Folge-Stories.
