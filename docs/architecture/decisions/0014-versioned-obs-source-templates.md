# ADR 0014: Generate complete native source files from versioned OBS templates

## Status

Accepted for Phase 9C on 2026-07-26.

## Context

Native callback structures are easy to start incorrectly and difficult to
merge safely into arbitrary C. Foundry needs useful starters without silently
rewriting hand-authored code or making generated output machine-dependent.

## Decision

Represent the selected starter with `obsPlugin.design` and version every
template ID. Generate the complete selected `.c` file deterministically from
validated module and component metadata. Always show current and proposed
source and require explicit confirmation before replacement.

Keep template generation UI-independent in the Workspaces assembly. Validate
the resulting manifest at the persistence boundary and use the pinned SDK build
pipeline as the compiler authority.

## Consequences

- Generated starters are reproducible and reviewable.
- Existing and hand-authored projects remain valid.
- Reapplying a template intentionally replaces that source file instead of
  attempting an unsafe structural merge.
- Multi-component composition and syntax-aware C transformations can be added
  later with new design schema fields and template revisions.
