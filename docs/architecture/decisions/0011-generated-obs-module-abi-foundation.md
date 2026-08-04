# 0011: Generated OBS module ABI foundation

## Status

Accepted for Phase 8 on 2026-07-25.

## Decision

Foundry represents OBS plugins as a distinct `obsstudio` target with a C17 x64
native build, `module-load-v1` callback contract, generated module exports,
and deterministic Windows package. The initial verified compatibility profile
is `32.x-windows-x64`, encoded against OBS 32.1.1.

The compatibility spike does not copy headers or libraries from an OBS
installation. Installed OBS builds are runtime hosts, not development SDKs.
Foundry generates only the documented module boundary and verifies the result
with the selected installation's own `obs.dll`.

## Consequences

- the minimal module can be compiled and loaded without downloading an SDK;
- Streamer.bot and OBS outputs cannot be mixed in one schema-v1 project;
- the ABI adapter is deterministic and reviewable;
- broader libobs APIs remain unavailable until a pinned SDK policy is added;
- runtime compatibility evidence is captured per OBS installation.
