# Foundry v1 compatibility matrix

Candidate matrix updated 2026-08-04. “Verified” means both the applicable
automated Foundry checks and the stated real-host GUI acceptance have passed.
Automation and real-host evidence are listed separately because neither is a
substitute for the other.

| Provider | Exact supported host | Automated evidence | Real-host evidence | v1 status |
|---|---|---|---|---|
| Streamer.bot | 1.0.4 stable | Managed build, stable-v23 package validation, mock argument/event/CPH matrix, deterministic package regression | DLL installation, package import, compiler reference, C# compilation, action execution/log, health and deployment lifecycle | Verified |
| Streamer.bot | 1.0.5-alpha.34 | Managed build, stable-v23 package validation, mock argument/event/CPH matrix, deterministic package regression | DLL installation, package import, compiler reference, C# compilation, action execution/log, health and deployment lifecycle | Verified |
| Streamer.bot | 1.0.5-beta.1 | Managed build, stable-v23 package validation, mock argument/event/CPH matrix, deterministic package regression | DLL installation, package import, compiler reference, C# compilation, action execution/log, health and deployment lifecycle | Verified |
| Streamer.bot | 1.0.5-beta.6 | Exact CPH fingerprint, managed build, stable-v23 package validation, four-profile mock matrix, deterministic package regression | DLL installation, cross-version package import/compilation, action execution/log, health, update, repair, rollback protection, and uninstall | Verified |
| OBS Studio | **32.1.2 Windows x64** | Pinned 32.1.2 SDK build, PE/ABI inspection, crash-isolated module load and source create/destroy lifecycle, deterministic package regression | Plugin install, filter attachment and persistence, restart, clean shutdown, OBS-log inspection, health and deployment lifecycle | Verified |
| OBS Studio | **32.2.1 Windows x64** | Pinned 32.1.2 SDK build, exact runtime/ABI comparison, PE inspection, crash-isolated module load and source create/destroy lifecycle | Plugin install, Effect Filter attachment and persistence, restart, clean shutdown, log/health inspection, process blocking, update, repair, rollback, modified-file protection, and uninstall | Verified |

## Scope and limitations

- OBS Studio **32.1.2 and 32.2.1 Windows x64 are the exact OBS releases
  supported by Foundry v1**. The internal profile name `32.x-windows-x64` does
  not promise compatibility with other 32.x builds, other architectures,
  macOS, or Linux.
- Streamer.bot 1.0.5 alpha and beta are exact prerelease builds. Compatibility
  does not automatically extend to another 1.0.5 prerelease.
- Streamer.bot 1.0.4 displays a provenance warning when importing an export
  created by a 1.0.5 prerelease. Continuing the reviewed import was verified.
- Foundry emits the stable version-23 Streamer.bot package contract. Native
  version-24 prerelease command serialization is not a supported output format.
- The mock Streamer.bot matrix does not launch a host. Real-host rows require
  separate installation and execution in each exact application version.
- The isolated OBS harness protects Foundry from native crashes, but only the
  real OBS GUI gate proves scene persistence and clean application shutdown.

The machine-readable companion is [v1-matrix.json](v1-matrix.json). Evidence
for final release approval is recorded in the
[v1 acceptance checklist](../final-acceptance/acceptance-checklist.md).
