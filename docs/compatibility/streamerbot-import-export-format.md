# Streamer.bot import/export format investigation

## Status

Representative exports have been captured and decoded from:

- Streamer.bot 1.0.4 stable;
- Streamer.bot 1.0.5-alpha.34;
- Streamer.bot 1.0.5-beta.1.

The captures contain the same compatibility-probe action, Set Argument
sub-action, Execute C# Code sub-action, and Test trigger. All nine
source-to-target import combinations were accepted by the disposable
instances.

## Export mechanism

Streamer.bot documentation describes an import code as UUEncoded. The observed
wire representation is:

```text
Base64 text
└── decoded binary
    ├── bytes 0-3: ASCII "SBAE"
    └── bytes 4+: GZip stream
        └── UTF-8 JSON payload
```

All three representative files use this envelope.

## Payload root

Every decoded payload contains:

```text
data
exportedFrom
meta
minimumVersion
version
```

`data` contains these collections:

```text
actions
queues
commands
websocketServers
websocketClients
timers
```

The representative fixture populated only `actions`.

## Version comparison

| Profile | Payload version | Exported from | Minimum version | Typed schema paths |
| --- | ---: | --- | --- | ---: |
| 1.0.4 stable | 23 | 1.0.4 | 1.0.0-alpha.1 | 57 |
| 1.0.5-alpha.34 | 24 | 1.0.5-alpha.34 | 1.0.0-alpha.1 | 57 |
| 1.0.5-beta.1 | 24 | 1.0.5-beta.1 | 1.0.0-alpha.1 | 57 |

For this fixture, all three payloads have the same 57 typed JSON schema paths.
Alpha and beta are structurally identical. After normalizing GUIDs, metadata,
bytecode, and machine paths, stable differs from 1.0.5 only by:

- payload version 23 instead of 24;
- `exportedFrom` and descriptive metadata;
- one additional absolute `System.dll` compiler reference.

## Compiler references

The exported Execute C# Code sub-action retains its compiler-reference list.
The captures contain machine-specific absolute framework paths:

| Profile | Absolute reference count |
| --- | ---: |
| 1.0.4 stable | 2 |
| 1.0.5-alpha.34 | 1 |
| 1.0.5-beta.1 | 1 |

The custom compatibility-probe reference is relative after the DLL is deployed
beside `Streamer.bot.exe`.

Foundry must not emit machine-specific framework paths. A package adapter must
classify references as:

- host-provided framework or Streamer.bot references;
- Foundry-packaged relative dependencies;
- unsupported absolute references requiring a diagnostic.

## Security review

The three decoded fixtures contain:

- no credential-like property names;
- no matches for common OAuth, bearer-token, API-key, password, or
  credential-in-URL patterns;
- absolute paths only in compiler references;
- opaque compiled bytecode alongside readable C# source.

Raw captures remain ignored by Git. Only normalized JSON and value-free
inspection reports are retained as repository fixtures.

The inspector:

- validates Base64, the `SBAE` signature, GZip, UTF-8, and JSON;
- limits import-code and decompressed sizes;
- does not execute imported code or bytecode;
- records SHA-256 hashes;
- replaces GUIDs, timestamps, absolute paths, and bytecode deterministically;
- emits a value-free schema inventory.

## Package-adapter requirements

1. Treat payload `version` as a required format discriminator.
2. Map Streamer.bot 1.0.4 exports to version 23.
3. Map the inspected 1.0.5 alpha and beta exports to version 24.
4. Reject unsupported future versions with a structured diagnostic.
5. Preserve unknown fields when reading and writing where practical.
6. Never execute imported code or bytecode during validation.
7. Reject, rewrite, or explicitly approve absolute machine paths.
8. Generate deterministic metadata and collection ordering.
9. Keep envelope encoding separate from version-specific JSON adapters.
10. Validate output by loading it into every declared target profile.
11. Treat prerelease provenance warnings separately from payload-version
    compatibility.
12. Preserve `exportedFrom` accurately so Streamer.bot can apply its own
    release-channel trust warning.

## Fixtures

Sanitized fixtures are stored under:

```text
experiments/StreamerBotCompatibility/captures/normalized/
```

Each profile contains:

- `normalized.json`;
- `inspection.json`.

## Import acceptance matrix

| Target host | 1.0.4 stable export | 1.0.5 alpha export | 1.0.5 beta export |
| --- | --- | --- | --- |
| 1.0.4 stable | Accepted | Accepted with prerelease warning | Accepted with prerelease warning |
| 1.0.5-alpha.34 | Accepted | Accepted | Accepted |
| 1.0.5-beta.1 | Accepted | Accepted | Accepted |

The product owner completed all nine checks on 2026-07-24.

When 1.0.4 stable loaded an alpha or beta capture, Streamer.bot warned that the
export originated from `1.0.5-alpha.34`, flagged it as potentially unstable,
and explained that prerelease exports may cause unsupported crashes or issues.
After explicit confirmation, the import completed normally.

This establishes:

- version-23 and version-24 payloads are reader-compatible across the three
  inspected hosts for this representative fixture;
- payload version 24 is not rejected by the 1.0.4 reader;
- the stable host uses export provenance to require confirmation for
  prerelease-origin content;
- Foundry must surface release-channel trust separately from structural format
  validation.

Broader actual import and re-export round trips for actions, commands, queues,
timers, and WebSocket definitions belong to the later package-adapter
validation slice.

## Primary documentation

- [Streamer.bot Import & Export](https://docs.streamer.bot/guide/core/import-export)
