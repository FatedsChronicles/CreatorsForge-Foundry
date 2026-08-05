# ADR 0030: Crash-isolated generic preview host

- Status: Accepted
- Date: 2026-08-05

## Context

Phase 22A deliberately avoided executing or hosting project content. Richer
preview workflows need a lifecycle boundary that can survive a renderer crash,
hang, or malformed result before provider-specific runtime adapters are added.

## Decision

Foundry launches a dedicated one-generation preview-host process. The desktop
passes a bounded, sanitized structural frame through owned JSON request and
result files. It enforces an eight-second timeout, kills the complete process
tree when necessary, captures bounded logs, validates the returned frame, and
removes per-run protocol files.

The host protocol contains no project binary path or complete source text.
Phase 22B therefore proves lifecycle, rendering, refresh, and recovery without
silently widening Phase 22A's trust boundary. Browser, managed UI, and native
OBS adapters must use this isolation boundary in Phase 22C.

## Consequences

Renderer failure cannot terminate the Foundry desktop, and the last structural
frame remains available for diagnosis. Each refresh pays process-startup cost,
which is acceptable for the current debounced design workflow and can be
revisited only if profiling demonstrates a material problem.
