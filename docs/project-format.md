# Foundry project format

## Status

Schema version 1, validation, and the first deterministic managed-build slice
were implemented on 2026-07-24. Optional versioned OBS design metadata was
added in Phase 9C, and optional template provenance was added in Phase 12A,
without changing the schema version.

The machine-readable schema is
[`schemas/projects/foundry-project-v1.schema.json`](../schemas/projects/foundry-project-v1.schema.json).
A working manifest is available at
[`samples/HelloFoundry/HelloFoundry.foundryproj`](../samples/HelloFoundry/HelloFoundry.foundryproj).

## Version 1 fields

| Field | Required | Meaning |
| --- | --- | --- |
| `schemaVersion` | Yes | Project schema version; currently `1` |
| `name` | Yes | Human-readable project name |
| `id` | Yes | Stable lowercase reverse-DNS project identifier |
| `version` | Yes | Semantic project version |
| `target.provider` | Yes | Target-provider identifier |
| `target.profile` | Yes | Explicit compatibility profile |
| `template` | No | Versioned project-template ID, revision, and captured parameters |
| `features.winForms` | No | Project intends to use WinForms |
| `features.mockRuntime` | No | Project intends to use the mock runtime |
| `managedBuild.targetFramework` | For `managedLibrary` | Currently `net481` |
| `managedBuild.languageVersion` | For `managedLibrary` | Currently `7.3` |
| `managedBuild.assemblyName` | For `managedLibrary` | Output assembly name |
| `managedBuild.sources` | For `managedLibrary` | Unique project-relative `.cs` paths |
| `nativeBuild` | For `obsPlugin` | C17, x64, cmake-msvc build inputs and `.c` sources |
| `obsPlugin` | For `obsPlugin` | Minimal ABI or pinned-libobs module metadata and callback contract |
| `obsPlugin.sdkVersion` | For `libobs-module-v1` | Pinned OBS SDK version; currently `32.1.2` |
| `obsPlugin.design` | No | Versioned OBS template, declared C source, component ID, and OBS-visible name |
| `cphInlineBridge.contract` | For `cphInlineBridge` | Currently `args-log-v1` |
| `cphInlineBridge.entryType` | For `cphInlineBridge` | Fully qualified static entry type |
| `cphInlineBridge.entryMethod` | For `cphInlineBridge` | Static entry method name |
| `targetDefinition` | For `streamerBotPackage` | Project-relative structured target JSON |
| `testDefinition` | No | Project-relative versioned test-definition JSON |
| `outputs` | Yes | Unique requested build outputs |

Supported output identifiers are:

- `managedLibrary`
- `cphInlineBridge`
- `streamerBotPackage`
- `obsPlugin`
- `obsPluginPackage`

Unknown properties are retained at the manifest, target, features, managed
build, and bridge levels. This supports forward-compatible inspection and
future read-modify-write workflows. Unknown output identifiers remain errors
because an older Foundry cannot truthfully claim to build them.

Managed source paths must remain beneath the project directory. Absolute paths,
parent traversal, non-C# files, and duplicate paths are validation errors.
The initial builder intentionally accepts only the Phase 1-proven
`net481`/C# 7.3 contract.

## Loading and trust boundary

The loader:

- accepts only `.foundryproj` files;
- resolves the input path but persists no machine-specific path;
- limits manifests to 1 MiB;
- parses strict UTF-8 JSON without comments or trailing commas;
- reports file, JSON path, and line/column information where available;
- observes cancellation during asynchronous I/O;
- loads project metadata only and never executes project source or binaries.

Schema migration is not implicit. The Phase 12D migration operation preserves a
backup and reports its source and target schema versions.

## Diagnostics

Diagnostics have a stable code, severity, message, optional location, and
optional suggested fix.

- `CFLxxxx` identifies manifest loading failures.
- `CFPxxxx` identifies project validation failures.

Validation currently covers schema version, identity, semantic version, target,
required outputs, supported output identifiers, duplicate outputs, and managed
build inputs.

Build failures use `CFBxxxx`. Compiler diagnostics retain their native stable
codes, such as `CS1002`, and include source line and column when available.

## CLI

Run:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  validate <project.foundryproj>

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build <project.foundryproj>

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test <project.foundryproj>
```

Exit codes:

| Code | Meaning |
| --- | --- |
| `0` | Manifest loaded and validated successfully |
| `1` | One or more loading or validation errors |
| `2` | Invalid command usage |
| `130` | Operation cancelled |

Diagnostics are written to standard error. A successful validation summary is
written to standard output, allowing scripts to distinguish results reliably.

## Managed build output

`foundry build` validates the project, clears only its fixed generated
directories, writes a Foundry-owned MSBuild project, and starts `dotnet build`
as a cancellable child process without a command shell.

```text
build/
├── bridge/
│   └── CPHInline.cs
├── managed/
│   └── <assemblyName>.dll
├── obj/
│   └── managed/
│       └── Foundry.Managed.csproj
└── package-ir.json
```

The generated project pins the .NET Framework reference-assembly package,
enables deterministic and continuous-integration build settings, embeds debug
information, maps project paths, and treats warnings as errors. It contains
only Foundry-owned targets and explicit source items; this increment does not
execute a user-authored `.csproj`.

Building remains an explicit trust boundary. Compilation can load the .NET SDK
and compiler infrastructure and must never run inside the future desktop
editor process.

## CPHInline bridge

Requesting `cphInlineBridge` also requires `managedLibrary`, a `streamerbot`
target provider, and an explicit bridge declaration:

```json
{
  "cphInlineBridge": {
    "contract": "args-log-v1",
    "entryType": "MyExtension.EntryPoint",
    "entryMethod": "Execute"
  }
}
```

The `args-log-v1` entry point contract is:

```csharp
public static bool Execute(
    IDictionary<string, object> arguments,
    Action<string> logInformation)
```

The generated bridge passes Streamer.bot `args` directly and exposes only
`CPH.LogInfo` through the callback. It includes the normal Streamer.bot import
form and an `EXTERNAL_EDITOR` form deriving from `CPHInlineBase`. Generated
source contains an overwrite warning and is UTF-8 without a byte-order mark.

Before emitting the package IR, Foundry compiles the bridge in a second child
build against the newly produced extension DLL and a Foundry-owned minimal CPH
stub. This verifies the configured entry type and complete method signature
without loading the user assembly into the CLI process.

The bridge is deterministic, reviewable source. The package IR records it as
`cphInlineBridge` at `bridge/CPHInline.cs` with its own size and SHA-256.

## Package intermediate representation

`package-ir.json` is timestamp-free and platform-neutral. It records:

- schema version;
- project ID, name, and semantic version;
- target provider, profile, framework, and CPH catalogue revision;
- each produced artifact's kind, forward-slash relative path, byte length, and
  lowercase SHA-256.

It is an inventory for future target-specific adapters, not a Streamer.bot
export. Repeated unchanged sample builds produce byte-identical managed DLL and
package IR files.

Its machine-readable contract is
[`schemas/packages/package-ir-v1.schema.json`](../schemas/packages/package-ir-v1.schema.json).

## Streamer.bot package output

`streamerBotPackage` requires `managedLibrary`, `cphInlineBridge`, the
`streamerbot` provider, one of the three verified target profiles, and a safe
`targetDefinition` path. The builder validates the structured definition,
generates and contract-checks the bridge, emits a deterministic stable-v23
import code, decodes it for structural round-trip verification, and records the
package and verification report in the package IR.

See [streamerbot-designer-exporter.md](streamerbot-designer-exporter.md) for
the definition model, desktop workflow, adapter boundary, and output layout.

## OBS Studio plugin output

`obsPlugin` and `obsPluginPackage` use the `obsstudio` provider and verified
`32.x-windows-x64` profile. They produce a native DLL, deterministic OBS
distribution ZIP, and package IR inventory. See
[obs-plugin-foundation.md](obs-plugin-foundation.md) for the ABI contract,
output layout, compatibility probe, and current limits.

SDK-backed modules use `libobs-module-v1`, `apiVersion` 32.1.2, and
`sdkVersion` 32.1.2. Project builds require a verified local cache and never
download the SDK implicitly. See [obs-sdk.md](obs-sdk.md).

SDK projects may persist an `obsPlugin.design` block. Its `template` is one of
the versioned Phase 9C template IDs, `source` must also appear in
`nativeBuild.sources`, and `componentId` is a stable lowercase OBS source ID.
Older and hand-authored projects may omit the block. See
[obs-plugin-designer.md](obs-plugin-designer.md).
