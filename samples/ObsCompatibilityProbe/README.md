# OBS Compatibility Probe

This is the smallest Foundry-owned native OBS module. It implements the
`module-load-v1` callback and relies on Foundry to generate the required OBS
module exports.

Build from the repository root:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- build `
  .\samples\ObsCompatibilityProbe\ObsCompatibilityProbe.foundryproj
```

The binary is written to `build/obs/bin` and the deterministic distribution
ZIP to `build/obs/package`.
