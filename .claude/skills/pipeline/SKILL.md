---
name: pipeline
description: Development pipeline for this project. Use whenever the user describes something to build, change, or fix — a feature idea, a bug report, a task. Every development task enters here.
---

# Pipeline

Every task runs branch-isolated through numbered phases. Phase files sit next to
this one; load each phase file only when that phase starts. Every rule here was
paid for — `git log -p .claude/` carries the incident that bought it. Read that
before deleting or bending one.

## Triage

Estimate the blast radius, tell the user your call, let them override:

- **Feature** — new behavior, UI, or architecture involved → full pipeline:
  phases 1 → 2 → 3 → 4.
- **Patch** — bugfix, typo, refactor without new behavior → short path:
  kickstart, then phases 3 → 4 (no interview, no design document).
- **Cut of an already-grilled feature** (a "Teil B"/"Schnitt B" whose
  decisions were already settled in an earlier run's interview) → kickstart,
  then phases 2 → 3 → 4. Splitting a feature into delivery cuts is a decision
  for the design/implementation phase, never a reason to re-interview —
  `requirements.md` of the later run cites the earlier decisions instead of
  re-asking for them.
- **Maintenance** — consolidation, cleanup, process and tooling work with no
  new end-user behavior → full chain 1 → 2 → 3 → 4. The stakeholder is the
  developer and the input the root `debt.md`, not a user's wish; `design.md`
  may shrink to "what gets touched and why" when no new behavior appears.

## Kickstart (every run)

0. Prune stale worktrees — every entry of `git worktree list` **and** every
   directory under the worktree root, since git forgets pruned ones the disk
   still holds. Remove only what passes all three vetoes: no commits beyond
   `main`, a clean tree (`git status --porcelain` empty, untracked files
   included), and no lock. A veto skips that worktree and names it; it never
   aborts the kickstart, and never use `--force` — an uncommitted run has died
   this way.
1. No git repo yet → `git init` + initial commit on `main`.
2. The run folder is `runs/YYYY-MM-DD-slug/` with today's date. No scan of
   sibling worktrees needed — two runs on one day differ in the slug.
3. Already in a dedicated worktree on its own branch? Stay there — the run
   folder carries the date, the branch name is free. Otherwise
   `git worktree add ../<repo>-worktrees/<slug> -b feat/<slug>` — work
   exclusively inside that worktree from here on, and beware absolute paths:
   with several checkouts of one repo they point at the wrong tree just as well.
4. Create `runs/YYYY-MM-DD-slug/` containing an empty `debt.md`.

## Phases

| Phase | File | Produces |
|---|---|---|
| 1 Requirements | `01-requirements.md` | `requirements.md` |
| 2 Design | `02-design.md` | `design.md` (first run: bootstrap first) |
| 3 Implement | `03-implement.md` | code + green tests |
| 4 Ship | `04-ship.md` | merged run, retro |

## debt.md — in every phase

The moment a shortcut is taken, a decision deferred, or process friction felt,
append one dated line to the run's `debt.md`. Debt is taken deliberately and
visibly, never silently. The run's file is a working note; at the retro what
outlives the run moves to the repo-root `debt.md`, which is what a maintenance
run reads.
