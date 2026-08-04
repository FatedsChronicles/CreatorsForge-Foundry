# Foundry Passthrough Filter

The Phase 9A functional sample is built against the pinned OBS 32.1.2 SDK. It
registers a synchronous video filter that deliberately passes rendering to the
next item in the filter chain.

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  sdk install obsstudio

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj `
  --obs "PATH TO DISPOSABLE OBS"
```

The test command inspects all required module exports, then uses an isolated
helper process to load the module and complete source create/destroy callbacks.

Install the generated ZIP into a disposable OBS instance, add **Foundry
Passthrough Filter** to a video source, save, restart OBS, and confirm the
filter remains attached without changing the source output.

This runtime check passed in disposable OBS 32.1.1 and 32.1.2 instances on
2026-07-26, including persistence after restart and clean module logs.
