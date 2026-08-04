# 0020: Keep the desktop Test Explorer provider-neutral

Status: Accepted for Phase 11D on 2026-07-27.

## Context

The desktop needs rich test execution and inspection without creating a second
testing implementation or loading native OBS modules into the editor process.
Streamer.bot and OBS require different runtime inputs, but their cases,
outcomes, diagnostics, and matrix cells share the Phase 11 contracts.

## Decision

The Test Explorer builds the current saved workspace and delegates execution to
the provider-neutral orchestrator and compatibility matrix runner. OBS runtime
paths are selected from user settings or added locally at run time. Native work
continues to execute in the crash-isolated helper.

The testing layer projects single runs and matrices into one searchable desktop
entry model. The WPF dialog owns only interaction state: filters, selection,
cancellation, details, and a diagnostic navigation request handed back to the
main editor.

## Consequences

- CLI and desktop executions use identical provider behavior and result files.
- Native faults remain isolated from the WPF process.
- Result filtering and matrix flattening are testable without starting WPF.
- Source navigation remains owned by the main workspace editor.
- Machine-specific OBS paths remain outside source-controlled projects.
