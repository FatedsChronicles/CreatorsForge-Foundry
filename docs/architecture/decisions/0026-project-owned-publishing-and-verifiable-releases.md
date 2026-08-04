# ADR 0026: Project-owned publishing and verifiable releases

## Status

Accepted for Phase 13.

## Decision

Publishing metadata and declared dependencies belong in the source-controlled
project manifest. A strict publishing operation builds the project, evaluates
a structured checklist, consumes only verified package-IR artifacts, optionally
signs DLLs with an explicitly selected certificate, and produces a deterministic
archive plus an external reproducibility report.

Development release packaging remains available without completed publishing
metadata. Publishing is a distinct command with stronger gates.

## Consequences

- Missing legal files or stale changelog versions cannot silently ship.
- Streamer.bot and OBS distributions share one evidence model.
- Signing is opt-in and never changes certificate selection implicitly.
- Archive hashes can be compared without a circular in-archive hash.
- Foundry produces local artifacts only; a marketplace remains outside v1.
