# 0012: Pinned, verified OBS SDK cache

## Status

Accepted for Phase 9A on 2026-07-26.

## Decision

Foundry pins the initial OBS development SDK to 32.1.2 Windows x64. It creates
a local SDK from checksum-pinned official source and Windows release archives,
generates a matching MSVC import library, and exposes a Foundry-owned CMake
config implementing the official `OBS::libobs` target boundary.

SDK installation is explicit and separate from project builds. Builds never
download dependencies and never use libraries copied from an arbitrary OBS
installation. The SDK root can be overridden for CI through a dedicated
environment variable.

## Consequences

- build inputs are reproducible and independently verifiable;
- installed OBS applications remain runtime-test targets only;
- offline agents can reuse verified archives;
- the cache contains GPL-licensed OBS source material and is not redistributed
  inside Foundry plugin packages;
- adding an SDK version requires a new reviewed descriptor and compatibility
  evidence rather than silently following the newest OBS release.
