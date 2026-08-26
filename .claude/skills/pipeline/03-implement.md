# Phase 3 — Implement (TDD)

Strict red → green → refactor, using the test command from `foundation.md`:

1. Pick the next acceptance criterion or design slice.
2. **Red** — write the failing test first; run it; watch it fail for the right
   reason.
3. **Green** — the smallest change that passes.
4. **Refactor** — with green tests, remove duplication and sharpen names;
   design.md is the target shape.

Repeat until every acceptance criterion has a passing test.

A UI slice turns green only after you have looked at it: launch the app, take
screenshots at mobile and desktop widths, and check spacing, overlap, and
clipping against `design-system.md` — every margin/padding comes from its
spacing scale. Fix what the screenshot shows before moving on.

A self-built interactive control (dialog, menu, popover) is driven by hand in
the real browser before any screen uses it — mouse and Tab/Enter/Esc, each twice
in a row. Framework-level controls fail in ways unit tests cannot see.

Fixing a security defect? Sweep for siblings of the same shape and fix them
too — a duplicate carries no findings, so a patched hole reopens one slice over.

Scope is fixed by requirements.md and design.md: an idea beyond them becomes a
debt.md proposal for the retro, and a gap in them becomes a question to the
user. Facts about APIs, behavior, or environment get verified — read the
source, run the code.

Reality may disagree with the plan: then update design.md and log the deviation
in debt.md. A shortcut under pressure, a skipped edge case, a test written after
the code — one line in debt.md, immediately.

Completion: every acceptance criterion covered by a green test and the full
suite green in the worktree.
