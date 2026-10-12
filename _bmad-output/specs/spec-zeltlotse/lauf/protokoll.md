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
| 2026-10-11 16:57 | 0-4 | done | nein (nicht empfohlen) | grün 487/487 / grün (9 Seiten) | 11bc38d | Sitzung hat nicht selbst committet, sondern nach Commit-Freigabe gefragt (CLAUDE.md-Regel); Orchestrator hat committet. npm audit: 10 mittlere Warnungen in Doku-Build-Abhängigkeiten; Demo-Freizeit mit festem Datum Aug. 2027 |
| 2026-10-11 18:36 | 1-1 | done | ja (done, keiner mehr) | grün 650/650 / grün (10 Seiten) | dd5bb54 | Regression erzeugt Offline-Screenshots mit Rendering-Rauschen neu (zurückgesetzt). Offen: Login-Mail-Begrenzung nur je IP, nicht je Adresse (vor Betrieb entscheiden) |
| 2026-10-11 20:14 | 1-2 | done | ja (done, keiner mehr) | grün 768/768 / grün (13 Seiten) | f541fef | Offen: abgelaufene Erst-Einladung einer Org nicht neu sendbar; alte Einladungen nie gelöscht (→ 1-7); nach Login-Link aus Mail muss der Einladungslink erneut geöffnet werden |
| 2026-10-11 21:37 | 1-3 | done | nein (nicht empfohlen) | grün 879/879 / grün (15 Seiten) | bb14ea1 | Offen: alte Leitungs-Einladungen nie gelöscht (→ 1-7); Screenshots mit echter Uhr (Demo-Phase kippt ab Aug. 2027); Datumsfelder zeigen engl. Platzhalter mm/dd/yyyy |
| 2026-10-12 06:26 | 1-4 | done | nein (nicht empfohlen) | grün 1023/1023 / grün (17 Seiten) | 2ac5564 | Lauf angehalten (Auto-Modus-Prüfung), nach Freigabe durch Marvin fortgesetzt; CLAUDE.md um Ausnahme für den autonomen Lauf ergänzt |
| 2026-10-12 09:25 | 1-5 | done | nein (nicht empfohlen) | grün 1150/1150 / grün (20 Seiten) | cc2b258 | –  |
| 2026-10-12 10:40 | 1-6 | done | nein (nicht empfohlen) | grün 1191/1191 / grün (21 Seiten) | 60b6472 | –  |
| 2026-10-12 12:39 | 1-7 | done | ja (done, keiner mehr) | grün 1256/1256 / grün (23 Seiten) | 6796ecb | –  |

## Entscheidungen im Lauf

Entscheidungen, die der Orchestrator bei blockierten Stories getroffen hat (nach den Leitprinzipien, ohne Rückfrage). Marvin prüft sie bei der Abnahme.

## Übersprungene Stories

Stories, die nach zwei Versuchen blockiert blieben: Grund, Patch-Datei unter `lauf/patches/`, betroffene Folge-Stories.
