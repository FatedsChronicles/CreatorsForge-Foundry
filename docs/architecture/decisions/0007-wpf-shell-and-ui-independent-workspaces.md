# ADR 0007: Use a WPF shell over UI-independent workspace services

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Phase 3 needs a dependable Windows desktop workspace without coupling project
persistence to window controls. The product roadmap includes Windows-specific
WinForms design and isolated runtime tooling, so the shell must interoperate
well with the Windows desktop stack. At the same time, create, open, save,
settings, recents, and recovery behavior need fast automated coverage without
UI automation.

## Decision

Build the desktop shell with WPF on `net10.0-windows`, supporting Windows 10 or
later. Use a small view-model layer with observable state and event handlers at
the window boundary; do not introduce an external MVVM or docking dependency in
this slice.

Keep workspace behavior in `CreatorsForge.Foundry.Workspaces`, targeting plain
`net10.0`. This library owns:

- project creation and validated opening;
- bounded project-tree construction;
- project-confined document persistence;
- recent projects and user settings;
- autosave recovery snapshots;
- structured `CFW` diagnostics for recoverable failures.

The WPF project composes those services with the Phase 2 build orchestrator and
owns dialogs, close decisions, keyboard gestures, timers, and layout capture.

Persist user state beneath `%LOCALAPPDATA%\Creators Forge\Foundry`. Use atomic
writes and tolerate corrupt settings, recents, or recovery data so local state
cannot prevent the application from starting.

## Alternatives considered

- **WinUI 3:** Modern Windows surface, but adds packaging and runtime complexity
  before the source-first workflow is proven.
- **Avalonia:** Strong cross-platform option, but the near-term product and
  WinForms designer/runtime requirements are Windows-specific.
- **External MVVM and docking frameworks immediately:** Useful for a mature IDE,
  but unnecessary dependencies for the first reliability gate.
- **Put persistence in code-behind:** Faster initially, but difficult to test
  and reuse from future commands or recovery tooling.

## Consequences

- The desktop is intentionally Windows-only while core, build, and workspace
  logic remain UI-independent.
- Phase 3 layout supports resizable persistent panels, not arbitrary docking.
- Workspace workflows can be verified in ordinary unit tests.
- A later editor or docking framework can replace the views without redefining
  project persistence.
- WPF event handlers remain responsible for user-interaction decisions such as
  save prompts; business failures remain structured diagnostics.

## Validation

- The complete solution builds with warnings treated as errors.
- Workspace tests cover create, open, edit, save, reopen, path confinement,
  settings, recents, and recovery.
- The desktop `--smoke-test` path initializes settings and recents without
  opening a window.
