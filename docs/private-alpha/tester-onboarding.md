# Private alpha tester onboarding

## Before starting

- Use Windows 10 or later on an x64 computer.
- Keep Streamer.bot and OBS testing disposable; do not begin with a production
  streaming installation.
- CMake, Visual Studio C++ tools, and the pinned OBS SDK are needed only for OBS
  development. The first-run screen identifies anything missing.
- Obtain the release ZIP and manifest SHA-256 through the two approved invitation
  channels. Do not continue if the values do not verify.

## Install and first run

1. Extract the private-alpha release ZIP.
2. In PowerShell, run `verify-private-alpha.ps1` with the separately supplied
   `-ExpectedManifestSha256` value.
3. Extract the verified `CreatorsForge-Foundry-...-win-x64.zip` and run
   `install-foundry.ps1`.
4. Launch Foundry from the Start menu and complete the first-run checks.
5. Open `samples/PrivateAlphaSamples.foundryworkspace` from the private-alpha
   bundle. Start with **StreamerBot Creator Toolkit**.

## Core acceptance journey

For each sample, use Foundry to validate and build, run its tests and
compatibility matrix, inspect the package, and create its provider release.
Deploy only to a disposable host. Verify health, deliberately exercise repair,
then rollback and uninstall. OBS must be closed during deployment changes.

Use **Tools > Recovery and Diagnostics** to create a bundle. Open the ZIP,
complete `issue-report.md`, and review every file before choosing to share it.
Finish with the [acceptance checklist](acceptance-checklist.md).

Report problems using [issue reporting](issue-reporting.md). Diagnostic codes,
the exact Foundry version, and the exact host version are more useful than a
screenshot alone.

