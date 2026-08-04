# ADR 0013: Use pinned offline intelligence for the first native editor

## Status

Accepted for Phase 9B on 2026-07-26.

## Context

The Foundry project and build contract pins OBS Studio 32.1.2, while developer
machines may contain different OBS installations and optional native language
servers. Editor suggestions must not vary with PATH, network availability, or
an unrelated system SDK.

## Decision

Bundle a versioned, curated libobs catalogue in the UI-independent Editor
assembly. Use it for completion, signature help, documentation, compatibility
diagnostics, and mapping symbols to verified Phase 9A SDK headers. Treat MSVC
build output as the complete native compiler diagnostic source.

Only open definition headers beneath the integrity-checked SDK cache and expose
them as read-only documents. Do not load arbitrary compilation databases or
execute a language server in Phase 9B.

## Consequences

- Native assistance is immediate, deterministic, offline, and profile-aware.
- Suggestions exactly match the Foundry-owned SDK baseline.
- The initial catalogue is curated rather than the entire libobs surface.
- Full semantic refactoring, arbitrary CMake projects, and clangd compilation
  database support remain possible later without changing the project format.
