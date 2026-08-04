# ADR 0003: Use versioned Streamer.bot export adapters

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Streamer.bot import/export files use a common encoded envelope but include a
numeric JSON payload version. Representative exports from 1.0.4 stable use
payload version 23. Representative exports from 1.0.5 alpha and beta use
payload version 24.

The inspected fixture has the same typed schema paths in all three versions,
but that does not guarantee that other exportable item types or future versions
remain identical. Execute C# exports retain Base64-encoded UTF-8 source in the
property named `byteCode` and may include machine-specific compiler references.

## Decision

Separate the package system into:

1. a common Base64, `SBAE`, GZip, and UTF-8 JSON envelope codec;
2. a version-dispatch layer;
3. explicit versioned payload adapters;
4. a normalized Foundry package intermediate representation.

Initially recognize:

- payload version 23 for Streamer.bot 1.0.4;
- payload version 24 for the inspected Streamer.bot 1.0.5 alpha and beta
  profiles.

The first writer targets payload version 23 and declares
`exportedFrom: "1.0.4"`. Native version-24 writing remains disabled because
populated alpha and beta command objects use different build-specific
obfuscated property names. Both supplied prerelease builds can import the
stable-v23 output.

Readers must reject unknown future payload versions with a structured
diagnostic. They should preserve unknown JSON fields when practical. Validation
must never load or execute imported source, bytecode, or assemblies.

Payload compatibility and release-channel provenance are separate concerns.
Foundry must retain `exportedFrom`, identify stable versus prerelease origins,
and require an explicit trust decision when a stable target receives
prerelease-origin content.

Writers must remove machine-specific paths and emit only host-provided
references or packaged relative dependencies.

## Alternatives considered

### Use one unversioned JSON model

This is simpler initially but risks silently accepting or emitting incompatible
fields when Streamer.bot changes its payload version.

### Treat exports as opaque strings

This preserves Streamer.bot output but prevents Foundry from validating
cross-references, dependencies, compatibility, security, and deterministic
output.

### Couple envelope decoding to each payload version

The observed envelope is identical across all three captures. Duplicating it in
every adapter would add unnecessary implementation and test surface.

## Consequences

- Format-version support is explicit and testable.
- The common envelope codec can be hardened independently.
- New Streamer.bot versions require evidence and an adapter decision.
- Unknown fields need a preservation strategy in the future project model.
- Imported source remains untrusted and is never executed during inspection.
- Export generation requires profile-specific golden files and import checks.
- Structurally compatible content may still require a prerelease trust warning.

## Validation

- All three representative exports decode as Base64, `SBAE`, GZip, and UTF-8
  JSON.
- Version 23 and version 24 fixtures each expose 57 identical typed schema paths
  for the representative action.
- Normalized alpha and beta payloads differ only in source-version metadata.
- Stable additionally differs by payload version and one framework reference.
- Credential-pattern scans found no sensitive values in the captured fixtures.
- The export inspector and normalizer are covered by automated tests.
- All nine source-to-target import combinations were accepted.
- Streamer.bot 1.0.4 accepted version-24 alpha and beta payloads after warning
  that prerelease-origin exports may be unstable and unsupported.
- Populated Phase 6 captures confirmed the stable queue, command, action,
  trigger, Set Argument, and Execute C# contracts.
- Generated stable-v23 exports use deterministic IDs, omit machine paths,
  decode after generation, and pass structural round-trip tests.
