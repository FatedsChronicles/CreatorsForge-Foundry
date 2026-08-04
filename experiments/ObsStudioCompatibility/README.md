# OBS Studio compatibility spike

Phase 8 targets `32.x-windows-x64`. OBS 32.1.1 passed the automated Windows
loader and libobs open/init probe. On 2026-07-26, OBS 32.1.2 passed the
disposable full-application log check with no module load failure.

Phase 9A's SDK-backed passthrough filter subsequently passed the disposable
OBS 32.1.1 and 32.1.2 GUI test on 2026-07-26. The filter remained attached
after save and restart, and neither host reported a module error. The captured
manual result is in
`captures/obs-32.x-passthrough-filter-runtime.json`.

Phase 9C's four versioned designer templates all passed the real pinned-SDK
CMake/MSVC compilation gate on 2026-07-26. The result is captured in
`captures/phase-9c-template-builds.json`.

Build the sample, then run the read-only ABI probe:

```powershell
.\experiments\ObsStudioCompatibility\Invoke-ObsCompatibilityProbe.ps1 `
  -PluginPath .\samples\ObsCompatibilityProbe\build\obs\bin\creators-forge-compatibility-probe.dll `
  -ObsRoot "C:\Program Files\obs-studio" `
  -ReportPath .\experiments\ObsStudioCompatibility\captures\obs-32.1.1.json
```

This verifies the host profile, Windows loader, required module exports,
encoded OBS API version, and the module load callback. For a plugin that
registers a source, also pass its source ID:

```powershell
.\experiments\ObsStudioCompatibility\Invoke-ObsCompatibilityProbe.ps1 `
  -PluginPath .\path\to\my-plugin.dll `
  -ObsRoot "C:\Program Files\obs-studio" `
  -ExpectedSourceId "com.example.my-plugin.filter" `
  -ReportPath .\experiments\ObsStudioCompatibility\captures\my-plugin.json
```

With `-ExpectedSourceId`, the probe verifies registration, creates a source,
releases it, and waits behind libobs's deferred destruction queue. A successful
`sourceDestroyCompleted` result proves that both lifecycle callbacks completed
without a native crash. The automated probe still does not replace the final
disposable-OBS GUI check.
