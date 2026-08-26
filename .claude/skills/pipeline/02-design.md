# Phase 2 — Design

First run (no `foundation.md`): run `00-bootstrap.md` now, then return here.

UX before architecture — the experience dictates the structure, never the
reverse.

## UX concept

Ground every choice in `design-system.md`. Design the flows a user actually
walks: screen by screen, including empty, loading and error states, keyboard
paths, and each screen's primary action. Where a decision belongs to the user
(tone, layout alternatives, prioritization), ask via the question tool instead
of assuming. Sketch key screens in words or ASCII — concrete enough that
phase 3 has no UI decisions left to make.

## Architecture

Read `docs/architecture.md` first; design this feature as a deliberate evolution of
it and write the delta back — the file stays the single current picture of the
system. Design deep modules behind small interfaces; place seams where tests
will need them. YAGNI sets the boundary: the simplest structure that satisfies
the requirements — an abstraction enters only with its second concrete use. For
every trade-off, write down the deliberate choice and its price — a debt taken
knowingly gets its line in `debt.md` now.

## Output

`runs/NNN-slug/design.md`, German: UX concept (flows, states, screens),
architecture (modules, interfaces, data flow), decisions with their prices.

Completion: pitch the design to the user instead of handing them the document —
a few German sentences: what they will see and feel, the key screens, the one
architectural bet, the trade-offs taken. The user approves on the pitch;
design.md stays as reference. Ambiguity left in design.md is debt — resolve it
or log it.
