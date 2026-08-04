# ADR 0006: Generate a versioned deterministic CPHInline bridge

- Status: Accepted
- Date: 2026-07-24
- Owners: Creators Forge Foundry maintainers

## Context

ADR 0002 proved that Streamer.bot stable, alpha, and beta can invoke a thin
`CPHInline` adapter that passes `args` and narrow callbacks into a .NET
Framework 4.8.1 extension DLL. Phase 2 needs to turn that experiment into
reviewable generated output without coupling extension code to the full
Streamer.bot plugin interface.

The bridge is a public generated contract. Guessing entry points, using
reflection, or exposing `CPH` as `dynamic` would move errors to runtime and make
compatibility difficult to test.

## Decision

Add the `cphInlineBridge` output and require an explicit manifest declaration:

- versioned contract;
- fully qualified entry type;
- entry method.

The first contract is `args-log-v1`. It invokes a static method with this shape:

```csharp
bool Execute(
    IDictionary<string, object> arguments,
    Action<string> logInformation)
```

Generate `build/bridge/CPHInline.cs` as deterministic UTF-8 source without a
byte-order mark. The generated file:

- identifies itself as generated and overwritten on the next build;
- records the bridge contract;
- emits the normal `public class CPHInline` import form;
- emits an `EXTERNAL_EDITOR` form deriving from `CPHInlineBase`;
- passes `args` directly;
- adapts only `CPH.LogInfo` as `Action<string>`;
- invokes the exact validated type and method without reflection.

Require `managedLibrary`, the `streamerbot` provider, and the supported bridge
contract whenever `cphInlineBridge` is requested.

Add the generated source to the package intermediate representation as a
separately hashed `cphInlineBridge` artifact.

Before emitting the IR, compile the generated bridge in a second disposable
child build against the newly built extension DLL and a Foundry-owned minimal
`CPHInlineBase` stub. This validates the configured type and full method
signature without loading the extension assembly into the CLI process.

## Alternatives considered

- **Pass the complete CPH proxy:** Convenient, but couples extension code to a
  large version-changing host API.
- **Use `dynamic`:** Avoids a compile reference but loses compile-time method
  validation.
- **Discover the entry point by reflection:** Reduces manifest fields but moves
  signature and naming failures to runtime.
- **Generate only an import-form bridge:** Smaller output, but prevents the same
  source from participating in external-editor compatibility compilation.
- **Embed the bridge only inside package data:** Hides generated behavior from
  source review and Git-oriented workflows.

## Consequences

- Bridge evolution is explicit through contract identifiers.
- Extensions remain host-neutral but must implement the selected static
  contract exactly.
- Adding another CPH capability requires a new or deliberately extended
  contract and compatibility evidence.
- The generated bridge must be referenced alongside the managed DLL when
  compiled or imported into Streamer.bot.
- The initial bridge does not expose warning/error logging, argument writes,
  actions, globals, or other CPH calls.

## Validation

- A golden-file test locks the exact generated source.
- A .NET Framework 4.8.1/C# 7.3 compile fixture builds the golden bridge against
  a minimal `CPHInlineBase` surface on every repository build.
- Build tests verify bridge and package-IR byte determinism and independent
  SHA-256 calculation.
- Tests prove a missing or incompatible managed entry point produces structured
  compiler output plus `CFB0009` and no package IR.
- The Phase 1 bridge shape was already compiled against all three supplied
  Streamer.bot plugin-interface versions and passed runtime verification.
- Two completed sample builds produced identical SHA-256 values:
  - managed DLL:
    `0f000b2d5b79fe500b4d5f0db326571070bed219dde7aed01935957fd2b6e0e1`;
  - generated bridge:
    `5b55cd24f33e92c877b18d6992edf88a549215ddcd5533d3952d2f604dea4cea`;
  - package IR:
    `e121a68fc044ca1f43ae027e4adaef8112e237bba7b6f9ad9514505fef7820d1`.
