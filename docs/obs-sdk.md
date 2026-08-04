# Pinned OBS SDK

## Contract

Phase 9A pins the Windows x64 development contract to OBS Studio 32.1.2. The
machine-readable descriptor is
[`eng/obs-sdk/obs-sdk-32.1.2.json`](../eng/obs-sdk/obs-sdk-32.1.2.json).

Foundry follows the official plugin-template link model: CMake resolves the
`libobs` package and links the plugin to `OBS::libobs`. It does not treat an
installed OBS application as a development SDK.

The SDK manager downloads two official release assets:

- the 32.1.2 source archive for matching libobs headers;
- the 32.1.2 Windows x64 archive for the matching `obs.dll` export surface.

Both archives must match the pinned SHA-256 values before extraction. Foundry
then generates an x64 MSVC import library from the official DLL exports and a
small `libobsConfig.cmake` package. The SDK is a local development cache and is
not included in plugin packages.

## Commands

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  sdk status obsstudio

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  sdk install obsstudio
```

The desktop equivalent is **Tools > OBS SDK Manager**. Installation is always
explicit because it downloads approximately 170 MB and invokes the installed
Visual Studio x64 `dumpbin` and librarian tools.

The default cache is:

```text
%LOCALAPPDATA%/Creators Forge Foundry/sdk/obsstudio/32.1.2/
|-- sdk-manifest.json
|-- sources/libobs/
|-- bin/x64/obs.dll
|-- lib/x64/obs.def
|-- lib/x64/obs.lib
`-- cmake/
    |-- libobsConfig.cmake
    `-- libobsConfigVersion.cmake
```

Set `CREATORS_FORGE_FOUNDRY_SDK_CACHE` to override the cache root for CI or an
offline build agent. `sdk install` also accepts `--archives <directory>` to use
previously downloaded archives; their hashes are still mandatory.

## Project integration

SDK-backed projects declare:

```json
{
  "obsPlugin": {
    "contract": "libobs-module-v1",
    "apiVersion": "32.1.2",
    "sdkVersion": "32.1.2"
  }
}
```

Foundry generates the official `OBS_DECLARE_MODULE()` boundary, calls the
project's configured load symbol, adds `find_package(libobs 32.1.2 EXACT)`, and
links `OBS::libobs`. A missing or corrupt SDK produces `CFB1010` before CMake is
started. MSVC and linker messages are converted into structured diagnostics
with native codes such as `C2065` and `LNK1104`.

## Functional sample

[`samples/ObsPassthroughFilter`](../samples/ObsPassthroughFilter) registers a
real `OBS_SOURCE_TYPE_FILTER` using `obs_source_info`. Its render callback uses
`obs_source_skip_video_filter`, leaving the source output unchanged.

The automated host probe confirmed:

- the 32.1.2 SDK module links and loads;
- `obs_open_module` and `obs_init_module` succeed;
- `dev.creatorsforge.passthrough-filter` appears in the registered source IDs;
- the module built with the 32.1.2 SDK initializes under the installed 32.1.1
  libobs host.

On 2026-07-26, the disposable OBS 32.1.1 and 32.1.2 runtime gate passed. The
filter could be added to a video source, remained attached after OBS was saved
and restarted, and produced no module errors in the OBS log. This completes
the Phase 9A pinned SDK compatibility gate.
