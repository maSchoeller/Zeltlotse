# Snowcap project

Every development task — feature, bug, tweak — runs through the pipeline skill
(`.claude/skills/pipeline/SKILL.md`); load it before touching code.

## Principles

1. Brilliant UX/UI — the interface is the product; design before code.
2. Debt is taken deliberately and visibly, never silently — shortcuts go to the
   run's `debt.md` the moment they happen.
3. Small parallel branches — one task, one branch, one worktree, one run folder,
   one requirements pass; cuts split delivery only inside implementation, never
   requirements. Integrate fast, keep history linear (rebase + fast-forward).
4. Open questions get resolved, never silently answered — decisions missing from
   requirements/design go to the user; facts checkable in code or environment
   get checked.
5. YAGNI — build exactly what requirements and design demand, as the simplest
   solution that satisfies them; abstraction earns its place with the second
   concrete use.

## Conventions

- Each run lives in `runs/YYYY-MM-DD-slug/`: `requirements.md`, `design.md`,
  `debt.md` — written in German.
- Speak German with the user; harness files stay English.
- Every question to the user goes through the question tool (never a prose
  list) — in every phase and every skill, no exception.
- Stack, commands and smoke-test method live in `foundation.md` at the repo root
  (created by the pipeline's bootstrap on the first run).

## Template home

The snowcap template (`https://github.com/maSchoeller/snowcap-template`) is the
remote `template` in a derived project (bootstrap adds it) and `origin` in the
template repo itself, where no cherry-pick is needed. Retros land generalizable
learnings as isolated harness-only commits onto `template/main`, each with user
approval; project-specific ones stay here, improvements flow back the same way.
Budgets are hard and counted apart: this file ≤ 40, the pipeline core
(`pipeline/SKILL.md` + its phase files) ≤ 300, every other skill and preset
≤ 80 each, at most 6 skills.
