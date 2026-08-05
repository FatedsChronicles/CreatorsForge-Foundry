# ADR 0029: Non-executing structural design preview

- Status: Accepted
- Date: 2026-08-05
- Owners: Creators Forge Foundry maintainers

## Context

Creators need visual feedback before deployment, but loading user assemblies or
scripts into the Foundry desktop would let a malformed project freeze or crash
the editor. A full runtime preview also requires process isolation, lifecycle
management, and provider-specific host contracts that should not be hidden
inside a first UI increment.

## Decision

Phase 22A persists an optional preview declaration and produces a provider-neutral
structural design model from one bounded project source. HTML scripts are
ignored, WinForms source is inspected as text, and OBS uses persisted design
metadata. No project binary or script executes. A future Phase 22B runtime view
must use a separate crash-isolated process.

## Alternatives considered

- Hosting project controls inside WPF was rejected because user code would run
  in the editor process.
- Embedding a browser for HTML was deferred because script, navigation, and
  network isolation require a separate security contract.
- Requiring deployment for every visual check preserves isolation but does not
  meet the rapid design-feedback goal.

## Consequences

The Phase 22A surface is safe, deterministic, and useful for structure and
viewport review, but it is intentionally not pixel-perfect or interactive.
Runtime fidelity is deferred to the isolated host. The optional schema-v1 field
remains forward compatible and does not alter build inputs or artifacts.

## Validation

Automated tests cover provider eligibility, source confinement and bounds,
script exclusion, WinForms layout extraction, forward-compatible persistence,
disable behavior, and desktop XAML construction.
