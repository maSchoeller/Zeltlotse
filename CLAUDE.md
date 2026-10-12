# Zeltlotse

## Arbeitsweise

- Wenn du mit einer Aufgabe fertig bist und es nicht committete Änderungen gibt, frage am Ende immer mit dem Frage-Tool (AskUserQuestion), ob du committen und pushen sollst.
- Ausnahme autonomer Lauf (Start-Prompt in `_bmad-output/specs/spec-zeltlotse/lauf/start-prompt.md`): Hier fragst du nicht.
  - Story-Sitzungen mit `bmad-build-auto` committen am Ende selbst auf `mvp`, ohne zu pushen, und hinterlassen einen sauberen Arbeitsstand.
  - Der Orchestrator committet und pusht laut Start-Prompt auf `mvp`. Dazu gehören Protokoll-Einträge und das Zurücksetzen von Screenshots, die ein Testlauf nur mit Rendering-Rauschen neu erzeugt hat.
