# OBS Studio plugin foundation

## Phase 8 scope

Phase 8 establishes the smallest native OBS module, a deterministic Foundry
build/package contract, and a compatibility probe. The verified profile is
`32.x-windows-x64`. OBS Studio 32.1.1 passed the automated libobs probe and
32.1.2 passed the disposable full-application load check.

The official OBS module contract requires `OBS_DECLARE_MODULE()`-equivalent
exports and an `obs_module_load` implementation. Foundry's `module-load-v1`
adapter owns the required ABI exports and calls a project-defined portable C
symbol. This keeps the compatibility probe independent of an SDK checkout
while preserving the documented OBS module boundary:

- <https://docs.obsproject.com/plugins>
- <https://docs.obsproject.com/reference-modules>
- <https://github.com/obsproject/obs-plugintemplate>

## Project contract

An OBS project uses:

```json
{
  "target": { "provider": "obsstudio", "profile": "32.x-windows-x64" },
  "nativeBuild": {
    "language": "c17",
    "architecture": "x64",
    "toolchain": "cmake-msvc",
    "sources": ["src/plugin.c"]
  },
  "obsPlugin": {
    "contract": "module-load-v1",
    "moduleName": "my-plugin",
    "entrySymbol": "foundry_obs_plugin_load",
    "displayName": "My Plugin",
    "author": "Creator",
    "description": "My native OBS plugin.",
    "apiVersion": "32.1.1"
  },
  "outputs": ["obsPlugin", "obsPluginPackage"]
}
```

`module-load-v1` requires this project callback:

```c
#include <stdbool.h>

bool foundry_obs_plugin_load(void)
{
    return true;
}
```

Foundry generates `obs_module_ver`, `obs_module_set_pointer`,
`obs_module_load`, and reviewable name/author/description exports. The module
encodes the 32.1.1 API contract and is compiled as C17 x64 with reproducible
MSVC flags and warnings as errors.

## Outputs

```text
build/
|-- obj/obs/                 generated adapter and CMake build tree
|-- obs/bin/<module>.dll     native module
|-- obs/package/<module>-<version>-windows-x64.zip
`-- package-ir.json
```

The deterministic ZIP contains `obs-plugins/64bit/<module>.dll` and
`foundry-package.json`. The package IR records `nativeObsPlugin` and
`obsPluginPackage` artifacts with sizes and SHA-256 hashes, framework
`native-c17-windows-x64`, and the OBS API version.

## Compatibility procedure

Build the checked-in sample:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- build `
  .\samples\ObsCompatibilityProbe\ObsCompatibilityProbe.foundryproj
```

Run the read-only host probe for every supplied installation:

```powershell
.\experiments\ObsStudioCompatibility\Invoke-ObsCompatibilityProbe.ps1 `
  -PluginPath .\samples\ObsCompatibilityProbe\build\obs\bin\creators-forge-compatibility-probe.dll `
  -ObsRoot "C:\Program Files\obs-studio" `
  -ReportPath .\experiments\ObsStudioCompatibility\captures\obs-32.1.1.json
```

Passing evidence is:

- host version is inside `32.x-windows-x64`;
- required ABI exports exist and module API reports 32.1.1;
- the direct load callback succeeds;
- the installation's `obs.dll` returns `obsOpenResult: 0`;
- `obsInitSucceeded` is `true`.

### Captured compatibility evidence

| OBS version | Check | Result |
| --- | --- | --- |
| 32.1.1 x64 | Windows loader, required exports, `obs_open_module`, `obs_init_module` | Passed |
| 32.1.2 x64 | Disposable OBS GUI and application log; no module load failure | Passed |

The 32.1.2 result completes the Phase 8 GUI runtime gate for the current
compatibility slice. Foundry did not alter a production OBS installation.

## Deliberate limits

The pinned SDK now exposes libobs to native builds, but Foundry does not yet
provide guided source/output/encoder templates, localization tooling, Qt frontend integration, OBS deployment receipts,
signing, or cross-platform packaging.

## Phase 9A extension

The pinned SDK policy and first functional passthrough filter are now
implemented. See [obs-sdk.md](obs-sdk.md) for acquisition, cache integrity,
CMake integration, native diagnostics, and runtime evidence. The Phase 8
`module-load-v1` contract remains available for ABI regression projects;
new desktop OBS projects use `libobs-module-v1` and SDK 32.1.2.

The Phase 9A disposable-runtime gate passed on 2026-07-26 in OBS 32.1.1 and
32.1.2: the functional filter remained attached after save and restart, with
no module errors in either OBS log.

## Phase 9B extension

The native editor now provides pinned libobs completion, signature and
parameter help, searchable documentation, compatibility diagnostics, and
read-only definition navigation into the verified SDK. See
[native-editor.md](native-editor.md).

## Phase 9C extension

**Build > OBS Plugin Designer** now provides structured module/component
metadata, four versioned deterministic source templates, side-by-side current
and generated previews, explicit overwrite confirmation, and validated
persistence. See [obs-plugin-designer.md](obs-plugin-designer.md).
