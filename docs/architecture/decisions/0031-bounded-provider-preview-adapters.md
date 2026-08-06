# ADR 0031: Bounded provider-specific preview adapters

- Status: Accepted
- Date: 2026-08-06

## Context

The generic Phase 22B renderer proved process isolation, lifecycle control, and
failure containment, but did not provide visual language specific to web
overlays, Windows forms, or OBS components.

## Decision

Structural analysis emits a bounded adapter descriptor alongside its sanitized
visual elements. PreviewHost validates the descriptor and selects one of three
provider adapters. Each adapter composes provider-recognizable chrome and roles
from that model. Unknown descriptors use the generic renderer.

Adapters do not receive full source text or binary paths. They do not embed a
browser, load a managed project assembly, initialize libobs, or load a plugin.
This keeps Phase 22C within the accepted Phase 22B trust boundary.

## Consequences

Creators receive more representative pre-deployment feedback and buildable
sample projects without exposing the Foundry editor to project code. Exact
runtime fidelity remains host-dependent and must not be implied by these design
models.
