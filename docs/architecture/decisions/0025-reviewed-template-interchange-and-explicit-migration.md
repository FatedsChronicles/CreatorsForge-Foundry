# ADR 0025: Reviewed template interchange and explicit migration

## Status

Accepted for Phase 12D.

## Decision

Project templates are bounded, reviewable JSON packages containing one current
manifest blueprint and allowlisted text files. Import parameterises identity and
profile fields, validates the complete manifest, and writes only into an empty
directory.

Project migration is never implicit. Each source schema requires a reviewed
migration path. Schema 0 to schema 1 creates a byte-preserving fixed backup,
preserves unknown root data, validates the candidate, and atomically replaces
the manifest only after those gates pass.

## Consequences

- Templates remain inspectable and cannot conceal executable binaries.
- Existing source projects can become reusable without a marketplace service.
- Opening a project has no surprising write side effects.
- A failed or disputed migration has a stable original backup.
- Future schema changes must add explicit migrations and regression fixtures.
