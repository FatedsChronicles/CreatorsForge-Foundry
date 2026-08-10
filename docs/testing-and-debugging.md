# Testing and debugging

Phase 11 builds one structured testing system for Streamer.bot extensions and
OBS plugins. Phase 11A establishes the shared contracts and Streamer.bot
vertical slice. Phase 11B adds the OBS callback/lifecycle adapter, PE ABI
inspection, and a crash-isolated native helper. The compatibility matrix and
provider-neutral orchestration arrive in Phase 11C. The desktop Test Explorer
arrives in Phase 11D. Phase 11E completes the phase with reviewed provider
package snapshots and byte-for-byte repeat-build and release tests.

## Project and test definitions

A project opts into source-controlled tests with:

```json
{
  "features": { "mockRuntime": true },
  "testDefinition": "tests/foundry-tests.json"
}
```

`testDefinition` must be a project-relative JSON path without parent
traversal. The versioned definition schema is
[`schemas/tests/foundry-test-definition-v1.schema.json`](../schemas/tests/foundry-test-definition-v1.schema.json).

Each test case declares a stable ID and name, a simulated event with arguments,
and structured assertions. Phase 11A supports `returnEquals`, `logContains`,
`logEquals`, `argumentEquals`, and `cphCallCount`. Event metadata remains in
the result and the argument dictionary is converted exactly into CLR values.

## Streamer.bot mock runtime

The initial runner supports the verified `args-log-v1` bridge contract. It
loads the freshly built managed assembly in a collectible load context and
requires this public static signature:

```csharp
bool Execute(IDictionary<string, object> arguments, Action<string> logInformation)
```

The mock records information logs as both readable messages and structured
`CPH.LogInfo` calls. Arguments remain inspectable after invocation. Entry-point
mismatches and extension exceptions become `CFT2xxx` errors.

The mock models the Foundry bridge boundary, not every internal behavior of a
real Streamer.bot host. Runtime compatibility gates remain authoritative.

## Running tests

Run a fresh build and all declared tests with:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test .\samples\HelloFoundry\HelloFoundry.foundryproj
```

The command prints one outcome per case and returns exit code `1` for failures
or errors. The complete result is written to
`build/test-results/latest.json`. It includes project/provider/profile
identity, timestamps, outcomes, simulated events, return values, logs, CPH
calls, expected and actual assertion values, and diagnostics. Its schema is
[`schemas/tests/foundry-test-result-v1.schema.json`](../schemas/tests/foundry-test-result-v1.schema.json).

## Compatibility regression matrices

The provider-neutral orchestrator selects the Streamer.bot mock adapter or the
crash-isolated OBS adapter from the project target. A test definition can pin
the profiles that must remain compatible:

```json
"profiles": ["1.0.4-stable", "1.0.5-alpha.34", "1.0.5-beta.1"]
```

Run the complete declared matrix with:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test-matrix .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test-matrix .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj `
  --obs "F:\OBS-Studio-32.1.2-Creator_Forge_Foundry"
```

Each profile/runtime cell gets an independent JSON result under
`build/test-results/matrix`. The aggregate is written to
`build/test-results/compatibility-matrix.json` using the
[`foundry-compatibility-matrix-v1` schema](../schemas/tests/foundry-compatibility-matrix-v1.schema.json).
The command fails if any cell fails or errors. Source control owns the expected
profiles; command-line OBS paths bind those expectations to disposable local
runtimes without storing machine-specific paths in a project.

Diagnostics use `CFT1xxx` for definition loading and validation, and `CFT2xxx`
for eligibility, invocation, and runtime errors.

## OBS ABI and lifecycle testing

OBS test cases use `obs-module-load` or `obs-source-lifecycle` events. A source
lifecycle event stores the expected OBS source ID in `event.name`. Supported
assertions are `abiExport`, `moduleLoadSucceeded`, `sourceRegistered`,
`sourceCreated`, and `sourceDestroyed`.

Run an OBS project against a disposable 32.x installation with:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj `
  --obs "F:\OBS-Studio-32.1.2-Creator_Forge_Foundry"
```

Foundry first parses the Portable Executable without loading it, verifies a
Windows x64 DLL, and inventories exports. The selected host must contain
`bin\64bit\obs64.exe` and match `32.x-windows-x64`.

Native execution occurs in `CreatorsForge.Foundry.NativeTestHost`, never in the
CLI or future desktop process. The helper starts libobs, opens and initializes
the module, enumerates source IDs, creates the declared source, releases it,
and waits behind OBS's destruction queue. It writes a structured result only
after shutdown succeeds.

An abnormal exit is reported with its hexadecimal process code as `CFT2102`.
A hung helper is terminated after a bounded timeout and reported as `CFT2103`.
Missing or malformed results cannot pass. This harness does not replace the
final disposable OBS GUI compatibility check.

The internal process protocol is versioned by
[`obs-native-host-request-v1.schema.json`](../schemas/tests/obs-native-host-request-v1.schema.json)
and
[`obs-native-host-result-v1.schema.json`](../schemas/tests/obs-native-host-result-v1.schema.json).

## Desktop Test Explorer

Open **Build > Test Explorer** or press `Ctrl+Shift+T`. Foundry saves and refreshes
the workspace, performs a fresh build, and then runs either the selected
profile or the complete declared compatibility matrix.

Streamer.bot projects run immediately through the mock runtime. OBS projects
show the disposable installations saved in Foundry settings; select one for
**Run Tests**, or one or more for **Run Matrix**. **Add OBS** can bind another
local disposable runtime without writing its machine-specific path into the
project.

The result table can be filtered by text and outcome. Text searches case names,
IDs, profiles, runtime versions, and diagnostics. Selecting a row displays its
event arguments, assertion expected/actual values, logs, CPH calls, duration,
and return value. The diagnostics pane combines build, runner, cell, and case
diagnostics. Double-click an actionable diagnostic, or choose **Open
Diagnostic**, to close the explorer and navigate the editor to its file and
line. Long native runs can be cancelled without blocking the desktop.

## Golden and deterministic regressions

Phase 11E checks reviewed semantic package snapshots for Streamer.bot and OBS,
then repeats unchanged package and fixed-time release builds byte for byte.
The policy and intentional update procedure are documented in
[`golden-package-regressions.md`](golden-package-regressions.md).

Phase 11 has no remaining implementation increments.
