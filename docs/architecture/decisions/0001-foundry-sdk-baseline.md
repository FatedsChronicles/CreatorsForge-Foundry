# ADR 0001: Use .NET 10 for the Foundry tooling baseline

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Creators Forge Foundry is a new Windows desktop product. The development
machine has .NET SDK 10.0.302 and the .NET 10 Windows Desktop runtime installed.
The application needs a modern, supported C# and WPF toolchain.

The inspected Streamer.bot 1.0.4 stable, 1.0.5-alpha.34, and 1.0.5-beta.1
installations declare .NET Framework 4.7.2. That host constraint applies to
assemblies Streamer.bot loads, but it does not require the Foundry editor,
command-line tooling, or isolated helper processes to use the same runtime.

## Decision

Use .NET 10 and C# 14 for the Foundry tooling baseline. Pin SDK 10.0.302 in
`global.json`, allowing later 10.0 patch releases.

Do not treat this decision as the target-framework decision for generated
Streamer.bot extension DLLs. Resolve that separately through the Phase 1 bridge
and dependency-loading spike.

## Alternatives considered

- **.NET 9:** Available locally, but it has a shorter support horizon and offers
  no identified compatibility benefit for the standalone Foundry tooling.
- **.NET Framework 4.7.2 for all projects:** Matches the inspected Streamer.bot
  host, but would unnecessarily constrain the editor and tooling architecture.
- **Multi-target every project immediately:** Adds complexity before a proven
  consumer requires it.

## Consequences

- Contributors need a compatible .NET 10 SDK.
- The future WPF application can use the current Windows Desktop runtime.
- Contracts that cross into Streamer.bot must remain runtime-conscious.
- Phase 1 must determine whether generated libraries target .NET Framework
  4.7.2, .NET Standard, or a narrower compatible surface.

## Validation

- `dotnet --info` reported SDK 10.0.302 and Windows Desktop runtime 10.0.10.
- File version inspection reported Streamer.bot 1.0.4, 1.0.5-alpha.34, and
  1.0.5-beta.1.
- Each inspected `Streamer.bot.exe.config` declares .NET Framework 4.7.2.
