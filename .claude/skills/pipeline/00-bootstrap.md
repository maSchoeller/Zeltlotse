# Phase 0 — Bootstrap (first run only)

Runs once per project, at the start of phase 2, when `foundation.md` is missing —
the requirements from phase 1 inform every choice below.

1. **Stack** — propose a stack fitting the requirements; the user decides.
   Load the matching `presets/*.md` (e.g. `dotnet-cloud`, `dotnet-wpf`) and
   apply its conventions; extend `.editorconfig`/`.gitignore` for the stack.
2. **Design system** — generate this project's concrete UI rules and design
   tokens into `design-system.md`, sourced from the global UI skills
   (`personal-ui-brand`, `ux-interface-design`; `elkw-app-ui` for ELKW
   projects): color, type, states, and a binding spacing scale with layout
   ground rules — minimum gaps between elements, touch-target sizes, overflow
   behavior. Every margin/padding in the project comes from the scale.
3. **Architecture seed** — create `docs/architecture.md`: intended modules,
   boundaries, data flow. Phase 2 evolves it every run; it stays the single
   current picture of the system. Internal docs (guidelines, frameworks) live
   in `docs/`.
4. **Test infrastructure** — set up the stack's test runner so red → green works
   from day one; record the exact test command, and add a CI workflow running it
   on push and pull request — the integration gate needs something that can be
   green.
5. **Smoke-test method** — decide how a fresh agent will verify the running UI
   on this stack (web: browser tools against the dev server; desktop: define
   per stack). Write `docs/local-testing.md`: how to run and test locally,
   including the dev seed users the agent may sign in with — dev seeds only,
   never real credentials.
6. **Project frame** — create `run-local.ps1` as the single way to start the
   app locally (foundation.md's launch command is this script), and
   `user-docs/` with `get-started/` and `features/` — markdown-first,
   SSG-ready; an SSG gets wired in only when docs are actually published.

Write `foundation.md` at the repo root: stack, test command, launch command,
smoke-test method, pointer to `design-system.md`. Keep it under 30 lines — it is
a cache of decisions, not documentation.

Add the template remote for the retro flow:
`git remote add template https://github.com/maSchoeller/snowcap-template.git`.

Completion: `foundation.md` exists and a trivial red → green cycle has actually
run with the recorded test command.
