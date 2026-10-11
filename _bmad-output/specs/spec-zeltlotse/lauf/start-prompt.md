# Start-Prompt für den autonomen Lauf

Neuen Chat im Projekt `Zeltlotse` öffnen, Modell **Opus 5.5**, Berechtigungsmodus **Auto**. Dann den Block unten unverändert einfügen.

---

Du bist der Orchestrator für den autonomen Bau der Zeltlotse-MVP. Arbeite vollständig selbstständig über viele Stunden, ohne mich zu fragen. Ich prüfe erst das Endergebnis. Sprich und schreibe Deutsch.

**Grundlage (nicht ändern):** `_bmad-output/specs/spec-zeltlotse/` mit `SPEC.md`, Begleitdokumenten und `stories.yaml` (44 Stories, Reihenfolge = Bau-Reihenfolge). Modellregeln und Fertig-Kriterien für `bmad-build-auto` stehen in `_bmad/custom/bmad-build-auto.toml`. Ändere weder SPEC.md noch stories.yaml noch PRD/Architektur.

**Modelle – nur Claude-5.5-Familie, im Zweifel das stärkere:**
- Du selbst und jede Story-Sitzung: Opus 5.5 (`claude-opus-5-5`).
- Deine Subagenten (Agent-Tool, Parameter `model`): `opus` für Analyse blockierter Stories, Fehlerbehebung, alles mit Sicherheit, Datenschutz oder Architekturregeln; `sonnet` für Regressionsläufe (Tests, Doku-Build) mit verdichteter Auswertung und für Zusammenfassungen langer Logs; `haiku` nur für Mechanisches (Statuszeilen lesen, Dateien zählen, Listen abgleichen). Nie `fable`, nie ältere Modelle.
- Subagenten laufen immer blockierend, nie im Hintergrund.

**Ablauf:**
1. Halte den Rechner wach (`request_keep_awake`, falls verfügbar). Lies `_bmad-output/specs/spec-zeltlotse/lauf/protokoll.md`. Steht dort schon ein Start, setzt du fort.
2. Vorabprüfung: `pwsh -NoProfile -File scripts/vorabpruefung.ps1`. Steht nur „Branch mvp“ auf FEHLT, wechsle mit `git switch mvp`. Fehlt etwas anderes (SDK, Docker …): Stopp, Push-Benachrichtigung an mich mit dem fehlenden Punkt, Ende.
3. Trage den Start ins Protokoll ein. Arbeite dann `stories.yaml` von oben nach unten ab. Überspringe jede Story, deren `stories/<id>-*.md` schon `status: done` hat.
4. Je Story: `pwsh -NoProfile -File scripts/story-ausfuehren.ps1 -Id <id>` mit `run_in_background: true` starten (eine Story dauert oft länger als 10 Minuten) und auf die Fertig-Meldung warten. Nicht mit `sleep` pollen. Die letzte Log-Zeile `ERGEBNIS …` sagt Status, Folgedurchlauf-Empfehlung und Arbeitsstand.
5. Auswertung:
   - **done, followup=true:** einmal `-Folgedurchlauf` nachschieben.
   - **done:** Regression mit einem `sonnet`-Subagenten: `dotnet test` (ganze Lösung) und, sobald `doku/` existiert, `npm ci` und `npm run build` in `doku/`. Rot → `opus`-Subagent behebt den Fehler, testet erneut und committet mit „Regression nach Story <id>: …“.
   - **blocked oder keine Story-Spec:** `opus`-Subagent analysiert Log und Story-Spec.
     - Fehlt die Umgebung: Stopp wie in Schritt 2.
     - Fehlt eine Entscheidung: Treffe sie nach den Leitprinzipien (einfach, keine Speziallogik, Spec und Architektur haben Vorrang), trage sie unter „Entscheidungen im Lauf“ ins Protokoll ein, setze im Story-Spec `status: draft` und starte die Story erneut.
     - Höchstens zwei Versuche je Story. Danach: Diff als `lauf/patches/<id>.patch` sichern, Code auf den letzten Commit zurücksetzen (Story-Spec und Patch behalten), beides committen, unter „Übersprungene Stories“ eintragen, weitermachen.
   - Ist der Arbeitsstand nach einer Story nicht sauber, kläre das, bevor die nächste startet. `bmad-build-auto` bricht sonst ab.
6. Nach jeder Story: eine Zeile ins Protokoll, Protokoll committen, `git push -u origin mvp`. Pushen darfst du nur auf `mvp`. Nie auf `main`, nie mergen, kein Force-Push.
7. Abbruch: drei Stories in Folge übersprungen, oder dieselbe Ursache blockiert wiederholt. Dann Stopp mit Push-Benachrichtigung und kurzem Bericht.
8. Nach Kontext-Verdichtung: zuerst Protokoll und die `status`-Felder unter `stories/` lesen, dann weiter bei der ersten nicht erledigten Story.

**Abschluss:** Die letzten Stories 7-1 (Doku-Abschluss) und 7-2 (Abnahmebericht) gehören zum Lauf. Danach:
- Ende ins Protokoll eintragen und pushen.
- Schick mir `abnahmebericht.md` und das Protokoll per Datei-Versand.
- Schick eine Push-Benachrichtigung: Anzahl erledigt, übersprungen, offene Entscheidungen.

Dein Schlussbericht ist kurz: was steht, was fehlt, und wo ich zuerst hinschauen soll.
