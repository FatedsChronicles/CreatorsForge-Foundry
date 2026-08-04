# ADR 0002: Use a thin CPHInline bridge and a host-neutral extension boundary

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

Streamer.bot supplies `CPH`, `args`, and the `CPHInline` lifecycle to compiled
C# sub-actions. A Foundry extension DLL needs those capabilities without
binding its whole implementation to Streamer.bot's large plugin-interface
surface.

The supplied 1.0.4 stable, 1.0.5-alpha.34, and 1.0.5-beta.1 installations
contain different plugin-interface binaries. All expose the small API surface
required by the compatibility bridge.

The Streamer.bot executables declare .NET Framework 4.7.2 in their
configuration, while each plugin interface targets .NET Framework 4.8.1.
Streamer.bot's official external-editor recipe also targets .NET Framework
4.8.1.

## Decision

Use generated `CPHInline` source as a thin host adapter. Keep extension
libraries independent from `Streamer.bot.Plugin.Interface` wherever practical.
Pass only explicit data and narrow callbacks or Foundry-owned interfaces across
the boundary.

For this spike:

- target the host-loaded probe assemblies at .NET Framework 4.8.1;
- keep their source compatible with C# 7.3;
- pass `args` as `IDictionary<string, object>`;
- pass CPH operations as narrow delegates;
- keep Streamer.bot types inside the generated bridge;
- compile the bridge against every supported physical plugin-interface binary.
- deploy the extension DLL and its runtime dependencies into the selected
  disposable Streamer.bot application directory for the currently proven
  workflow.

The framework and language floors for the eventual public extension SDK remain
subject to broader compatibility evidence.

## Alternatives considered

### Reference `IInlineInvokeProxy` throughout extension code

This provides direct access to every CPH method but couples user libraries to a
large, version-changing host assembly and its transitive dependencies.

### Pass CPH as `dynamic`

This avoids a compile-time host reference but moves method errors to runtime,
weakens diagnostics, and complicates testing.

### Reflect over CPH from the extension

This has similar runtime fragility to `dynamic` with more complex invocation
and error handling.

### Put all extension behavior in CPHInline

This avoids DLL loading but defeats the managed-extension, testing, packaging,
and reusable architecture objectives.

## Consequences

- Generated bridge code remains small and inspectable.
- Core extension logic can run against mocks without Streamer.bot assemblies.
- Foundry must generate and version adapter code deliberately.
- Each CPH capability exposed to an extension needs an explicit boundary.
- A compiler reference does not add its source directory to Streamer.bot's
  runtime assembly probing paths.
- The currently proven workflow places runtime assemblies beside
  `Streamer.bot.exe`; Foundry must preview this external modification and
  require explicit confirmation.
- A future bootstrap loader may allow runtime assemblies to live in a dedicated
  extension directory without changing the bridge contract.
- Direct access to the entire CPH API may be offered as an advanced,
  explicitly coupled mode later.

## Validation

- The `net481` probe and secondary dependency compile with zero warnings.
- Unit tests prove argument transfer, host callback invocation, output capture,
  and the dependency call.
- The conditional bridge compiles with zero warnings against all three supplied
  plugin-interface binaries.
- A compiler-only reference produced `FileNotFoundException` for the main probe
  on all three hosts.
- After the main probe and secondary dependency were deployed beside
  `Streamer.bot.exe`, all three hosts invoked the DLL successfully, passed
  arguments, completed CPH calls, and loaded the dependency.
- The product owner confirmed the stable, alpha, and beta runtime passes on
  2026-07-24.
