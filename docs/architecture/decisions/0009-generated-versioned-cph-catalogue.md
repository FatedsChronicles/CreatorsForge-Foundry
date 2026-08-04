# ADR 0009: Generate a versioned CPH catalogue from verified interfaces

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

The supplied stable interface exposes hundreds of CPH methods and overloads.
Hand-maintaining signatures would introduce drift, while loading Streamer.bot
assemblies into the editor process would violate the product's safety boundary.
Completion and compatibility diagnostics also need exact profile differences.

## Decision

Generate catalogue v1 deterministically from the three supplied
`Streamer.bot.Plugin.Interface.dll` files using .NET's read-only
`MetadataLoadContext`.

Record interface SHA-256 fingerprints and exact availability on each overload.
Group overloads into methods and apply a reviewed documentation overlay for
well-established core APIs. Use conservative generated descriptions for
inventory entries without curated documentation.

Embed the JSON catalogue in the editor and build assemblies. Use one shared
revision for profile-filtered completion, overload help, local reference,
compatibility diagnostics, and package intermediate metadata.

Keep catalogue data versioned separately from application versions.
Regeneration requires explicit host paths and produces a content-derived
revision. Do not execute inspected assemblies or publish prerelease-derived
catalogue data without permission.

## Alternatives considered

- **Hand-maintained list:** Cannot reliably track hundreds of overloads and
  profile differences.
- **Reflect assemblies in the desktop process:** Current but unsafe and coupled
  to local installations.
- **Scrape documentation as the signature authority:** Adds meaning but can lag
  physical host interfaces.
- **Use only the stable interface:** Provides no accurate prerelease filtering.
- **Show every profile everywhere:** Moves incompatibility failures to the host.

## Consequences

- Catalogue signatures are evidence-based and reproducible.
- Behavioral documentation remains an explicit curated layer.
- The checked-in catalogue is approximately 650 KiB.
- New profiles require regeneration, review, and compatibility tests.
- Prerelease data carries a distribution-review obligation.

## Validation

- Tests lock profile inventory, curated documentation, completion filtering,
  signature help, deprecation, unknown methods, and profile diagnostics.
- Two generations from unchanged interfaces must produce the same file hash.
- Package IR records the catalogue revision.
- The desktop smoke gate materializes the CPH-enabled editor surface.
