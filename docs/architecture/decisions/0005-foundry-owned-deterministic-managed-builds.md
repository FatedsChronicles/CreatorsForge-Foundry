# ADR 0005: Generate Foundry-owned projects for deterministic managed builds

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Phase 2 needs a command-line build that turns declared source into a managed
extension artifact without the desktop application. The Phase 1 compatibility
spike proved .NET Framework 4.8.1 and C# 7.3 across the supplied Streamer.bot
installations.

Building an arbitrary imported `.csproj` would permit custom MSBuild targets
and tasks to execute. It would also make output layout and reproducibility
dependent on project-specific configuration before Foundry has a dependency
and trust model.

Future Streamer.bot package adapters need a stable, platform-neutral inventory
of produced artifacts rather than direct access to incidental build folders.

## Decision

The first builder consumes explicit manifest inputs:

- target framework;
- C# language version;
- assembly name;
- project-relative C# source paths.

Accept only `net481` and C# 7.3 in this increment. Generate a Foundry-owned SDK
project beneath `build/obj/managed`, pin
`Microsoft.NETFramework.ReferenceAssemblies` 1.0.3, and invoke `dotnet build` in
a cancellable child process without a command shell.

Place final managed artifacts beneath `build/managed`. Enable deterministic and
continuous-integration compilation, embedded debug information, path mapping,
optimization, and warnings as errors. Clear only these fixed generated
directories before a build so stale output cannot be mistaken for success.

After compilation, emit `build/package-ir.json`. The package intermediate
representation contains no timestamp or absolute path. Each artifact records a
kind, normalized relative path, byte length, and SHA-256.

Report orchestration failures with `CFBxxxx` diagnostics. Parse compiler
locations into structured diagnostics while retaining native compiler codes.

## Alternatives considered

- **Build a user-authored `.csproj`:** Familiar and flexible, but immediately
  expands the code-execution surface to arbitrary MSBuild logic and weakens
  output control.
- **Invoke Roslyn in the CLI process:** Avoids MSBuild, but would load compiler
  extensions and untrusted build inputs into the long-lived tool process and
  duplicates framework reference resolution.
- **Copy files without hashes into a package folder:** Simpler, but provides no
  stable integrity contract for later package adapters or release reports.
- **Include build timestamps in the IR:** Useful for logs, but makes unchanged
  build products differ. Operational timestamps belong outside the
  deterministic package input.

## Consequences

- The sample can be built without a hand-authored extension project file.
- The builder runs separately from future editor UI code and supports
  cancellation by terminating the child process tree.
- The current build model does not yet support NuGet dependencies, local
  references, analyzers, source generators, WinForms, or alternative target
  frameworks.
- At the time of this decision, `cphInlineBridge` and `streamerBotPackage` had
  no producers. ADR 0006 subsequently adds the bridge producer;
  `streamerBotPackage` remains pending.
- Later dependency support requires provenance, copying, hashing, and trust
  rules before it expands the generated project.

## Validation

- The real CLI built the sample project twice.
- The pre-bridge sample produced identical managed DLL and package IR hashes
  across two runs. ADR 0006 records the current two-artifact sample evidence.
- Automated tests cover generated-project and IR determinism, missing inputs,
  compiler diagnostic parsing, artifact hashing, and CLI output.
