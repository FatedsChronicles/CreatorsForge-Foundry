# 0015: Assemble releases only from verified package IR

Status: Accepted for the unified release milestone on 2026-07-26.

## Context

Streamer.bot and OBS builds produce different artifacts, but testers need one
predictable release workflow. Scanning a build directory would risk including
temporary compiler output, stale files, or user-controlled links. A release
also needs reviewable installation instructions and a machine-readable hash
report.

## Decision

The release packager consumes only artifacts declared by the successful
package intermediate representation. It validates their containment, unique
portable paths, sizes, and SHA-256 values before copying them. It then adds a
canonical package-IR copy, provider-specific README, and versioned build
manifest. The completed ZIP is opened again and every declared payload entry
is rehashed.

Release output uses a fixed directory under the project `build` root. Foundry
refuses to replace it when any file-system link is present. Archive entry paths
are sorted and timestamps normalized.

## Consequences

- Both providers share one CLI and desktop release action.
- Undeclared and stale build files cannot enter a release accidentally.
- Modified output is rejected rather than silently re-inventoried.
- The release manifest does not include its own hash.
- Integrity verification remains distinct from runtime safety verification.
