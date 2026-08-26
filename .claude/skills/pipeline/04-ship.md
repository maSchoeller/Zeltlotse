# Phase 4 — Ship

## 1. Smoke test — fresh eyes

Dispatch a subagent given only `requirements.md`, `design.md`, and the launch
and smoke-test method from `foundation.md` — never the implementation
conversation. It starts the app, walks every acceptance criterion through the
real UI at mobile and desktop widths, and checks spacing, overlap, clipping,
contrast and touch targets against `design-system.md`. Self-built controls get
real input — click, Tab, Enter, Esc, each twice; a broken one often works once.
Report pass/fail with evidence per criterion and screen. Failures go back to
phase 3; only a green smoke test reaches the integration gate. A criterion that
comes back red a second time stops the run and becomes a question to the user:
the design may be wrong, or the criterion — a third attempt rarely is the fix.

User-visible changes ship with documentation, and the gate stays shut without
it: `user-docs/` guides a person through the task ("click here, watch out for
that") rather than listing features, and every screen it names carries a
reproducibly generated screenshot. Refresh `docs/local-testing.md` when seeds
or processes changed. Patches and internal changes skip this.

## 2. Rebase and verify

1. Rebase the feature branch onto the current `main`; conflicts are resolved
   here in the branch, never on main.
2. Run the full test suite on the rebased state. Red → fix in the branch and
   rerun; only green proceeds.

## 3. Retro — before merge, while still on the branch

Read the run's `debt.md`, and diff `.claude` against `main` — after a rebase
across someone else's harness change, a conflict announces itself, a silent
loss does not. If it has entries, offer the user a retro now and
walk the entries together — per entry decide: fix now, keep as known debt, or
turn into a learning. Any fix-now work happens on the branch and re-enters
step 2's test run before the gate proceeds. What is kept rather than fixed
moves dated into the repo-root `debt.md`: the run folder is never opened again,
so debt left there is silent by the next run — and that root file is what a
maintenance run reads for its input.

Two tests decide where a learning goes. What can only fire in one stack belongs
in that `presets/*.md`, never in a phase file. And a learning becomes a harness
rule on its second run, not its first — until then it lives in this repo's
CLAUDE.md or the root `debt.md`. A retro judges generality right after the pain.

- **Project learnings** → this repo's CLAUDE.md or foundation.md.
- **Generalizable learnings** (would improve every future project) → commit the
  harness change alone, touching only harness files, and give it a
  `Snowcap-Learning: <slug>` trailer — `git log --grep` then answers in either
  direction whether a repo already carries that learning. It travels to the
  template in step 4, never before: an abandoned branch must not leave its
  learning behind in the template.

The budget is hard, each part counted apart so the number is reproducible:
CLAUDE.md ≤ 40, the pipeline core (`pipeline/SKILL.md` plus its phase files)
≤ 300, every other skill and every preset ≤ 80 each, at most six skills — a file
that loads only when its case arises is not charged to the core. Measure with
`.claude/check-budget.ps1`; a number used as an acceptance criterion without a
written way to measure it gets measured differently next run. A retro may shrink
the harness; growth requires displacement — name what the new line replaces.

## 4. Integration gate

The gate is local first: the project's own test and check commands from
`foundation.md` run green before anything is pushed. Green is the command's
exit code, and it only survives if nothing swallows it — do not pipe the test
command, or preserve the status (`$LASTEXITCODE`, `set -o pipefail`). The
summary line is the cross-check, never the source. A remote CI decides on top
of that, never instead of it; a remote that cannot run is not a reason to leave
the gate open.

Then push the branch and open a pull request, merged by rebase or fast-forward
— never squash, never a merge commit. History stays linear, and the kickstart's
prune veto ("no commits beyond `main`") clears only for a branch whose commits
sit in `main` by hash; a squashed branch keeps its worktree forever. No remote,
or the user waves it through? Fast-forward into `main` and push that instead —
the same CI covers pushes to `main`. Delete the branch; the worktree goes when
the next kickstart prunes it. Runs last, after the retro has settled — nothing
about the run changes once this step starts.

Last, with `main` carrying the run: after the user approved the exact wording,
fetch the `template` remote, cherry-pick the learning commit onto
`template/main` in a temporary worktree, and push.
