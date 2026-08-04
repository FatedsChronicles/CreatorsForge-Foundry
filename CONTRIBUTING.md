# Contributing to Creators Forge Foundry

## Working principles

- Work in small, demonstrable vertical slices.
- Keep generic domain code independent of Streamer.bot and OBS integrations.
- Treat imported projects, source, packages, and binaries as untrusted.
- Never execute user extension code in the main editor process.
- Keep persisted formats readable, versioned, and reviewable in Git.
- Use relative paths in persisted project data.
- Do not commit generated build output, secrets, or machine-specific paths.
- Preserve cancellation for build, indexing, process, and other I/O operations.
- Represent failures as structured diagnostics when callers need to act on them.

## Before editing

1. Read the relevant brief section and architecture decision records.
2. Inspect the implementation and tests in the affected area.
3. Identify any compatibility or public-format assumptions.
4. Keep unrelated redesigns out of the change.

## Validation

Run the repository build from its root:

```powershell
.\build.ps1
```

A change is ready only when the affected tests pass, the solution builds
without warnings, documentation reflects user-visible behaviour, and the diff
contains no unintended binaries or local paths.

## C# conventions

- Follow `.editorconfig` and the repository-wide MSBuild properties.
- Use nullable reference types.
- Prefer immutable domain values and explicit validation at boundaries.
- Avoid speculative interfaces or layers without a current consumer.
- Keep UI logic out of view code where practical.
- Include tests for observable behaviour and important failure paths.

## Architecture decisions

Material decisions affecting compatibility, security boundaries, persisted
formats, or major dependencies require an ADR in
`docs/architecture/decisions`. Copy `0000-template.md`, assign the next number,
and record the evidence and consequences.
