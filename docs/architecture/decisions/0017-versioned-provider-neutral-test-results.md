# 0017: Use versioned provider-neutral test definitions and results

Status: Accepted for Phase 11A on 2026-07-27.

## Context

Streamer.bot and OBS execute very different code, but the desktop, CLI, and
compatibility matrix need one stable way to describe cases, assertions,
outcomes, and diagnostics. Provider-specific console output would be difficult
to review, automate, and migrate.

## Decision

Foundry uses source-controlled, versioned JSON test definitions referenced by
`.foundryproj`. Runners translate provider-specific events and calls into a
shared structured result while retaining provider and profile identity.

The first adapter implements the Streamer.bot `args-log-v1` boundary with exact
argument conversion and recorded `CPH.LogInfo` calls. Results are generated
under the project build directory and are never treated as source.

## Consequences

- CLI and future desktop tests share definitions and results.
- Expected and actual values remain machine-readable.
- New assertion semantics require version-compatible loader changes.
- Mock success does not replace host compatibility testing.
- Native isolation and OBS lifecycle semantics remain separate Phase 11 work.

