# ADR 0004: Use versioned JSON project manifests and structured diagnostics

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Foundry needs a source-first project format that remains readable in Git,
supports future migration, and can be consumed by both a command-line tool and
the future desktop application. Imported manifests are untrusted data.

Callers also need to react to failures without parsing display text. Loading,
validation, build, editor, and package operations will eventually report
problems through the same Problems surface.

## Decision

Use UTF-8 JSON files with the `.foundryproj` extension. Every manifest contains
an integer `schemaVersion`; version 1 is described by a repository-owned JSON
Schema and immutable Core records.

Use strict, case-sensitive camel-case JSON. Reject comments, trailing commas,
incorrect field types, unsupported schema versions, and manifests larger than
1 MiB. Preserve unknown properties at extension points where practical.
Migration will be an explicit future operation, never an incidental side
effect of loading.

Represent actionable failures with a structured diagnostic containing:

- stable code;
- severity;
- plain-language message;
- optional file, JSON path, line, and column;
- optional suggested fix.

Reserve `CFLxxxx` for loading failures and `CFPxxxx` for project validation.
CLI exit codes describe the result category; they do not replace diagnostics.

## Alternatives considered

- **YAML:** More author-friendly for some users, but significantly more complex
  to parse safely and consistently and less suitable for a minimal first
  schema.
- **MSBuild XML as the primary manifest:** Strong for build configuration but
  couples the product model to MSBuild and is less approachable for visual
  authoring.
- **Throw exceptions for invalid projects:** Appropriate for programmer errors,
  but poor for untrusted user files containing multiple correctable problems.
- **Discard unknown fields:** Simpler, but risks destructive data loss when an
  older Foundry inspects and later rewrites a newer project.

## Consequences

- The Core model remains independent of WPF, Streamer.bot, and MSBuild.
- CLI and future UI consumers can share diagnostic behavior.
- Unknown output kinds are rejected even though unknown JSON properties are
  retained, because Foundry cannot build an output it does not understand.
- Exact migration and atomic-write behavior must be added before any command
  rewrites a project.
- JSON property names form a public persisted contract and require deliberate
  schema evolution.

## Validation

- The sample v1 manifest loads and validates through the real CLI.
- Tests cover valid loading, unknown-field preservation, malformed JSON
  locations, missing files, cancellation, semantic versions, output
  validation, CLI output, and exit codes.
- The complete repository build and test command includes the Core and CLI
  projects.
