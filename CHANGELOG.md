# Changelog

All notable changes to Creators Forge Foundry are documented in this file.

## [Unreleased] - v0.1.0-beta.1

### Added

- Dark / Light / System mode 
- Branding Logo
- Native Windows setup and updater executables with a selectable installation
  directory and a default of `C:\Program Files\Creators Forge\Foundry`.
- GitHub Releases as the default manual update source, with verified staged
  updater launch from the desktop.

### Changed

- Windows installation, upgrade, and uninstall now use a registered native
  setup workflow instead of requiring end users to run PowerShell scripts.

## [Unreleased] — changes since 0.1.0-rc.1

No later release-candidate number has been assigned yet. This section describes
the current development build relative to the packaged `0.1.0-rc.1` baseline.

### Added

- Exact Streamer.bot `1.0.5-beta.6` project, schema, editor catalogue, snippet,
  test-matrix, build, package, and deployment support.
- Exact-version Streamer.bot host probing and CPH catalogue generation across
  `1.0.4`, `1.0.5-alpha.34`, `1.0.5-beta.1`, and `1.0.5-beta.6`.
- A sanitized representative beta.6 import/export fixture and four-host
  cross-import/compilation evidence.
- Exact OBS Studio `32.2.1` Windows x64 runtime support while retaining the
  backward-compatible pinned OBS `32.1.2` SDK.
- A central OBS runtime allow-list used consistently by deployment and the
  crash-isolated native test runner.
- OBS `32.1.2` to `32.2.1` ABI comparison and regression coverage. The newer
  runtime adds `obs_source_get_dark_icon` and `obs_source_get_light_icon` and
  removes no exported `obs.dll` symbols.

### Changed

- Streamer.bot installation discovery now reads and matches the executable's
  exact product version, including prerelease suffixes, instead of relying on a
  folder name or choosing another `1.0.5` prerelease.
- The Streamer.bot compatibility matrix now contains four exact runtime cells.
- The OBS `32.x-windows-x64` project profile now resolves only the explicitly
  verified `32.1.2` and `32.2.1` runtimes; it does not imply support for every
  OBS 32.x release.
- Final-acceptance and compatibility documentation now distinguish the pinned
  OBS build SDK from the exact runtime versions verified by Foundry.

### Fixed

- New acceptance projects now include a test definition, preventing `CFT2002`
  when they are first opened in Test Explorer.
- Generated OBS filter projects register the source correctly so installed
  filters appear in OBS Studio's Effect Filters list.
- Publishing metadata version edits persist and remain synchronized with the
  project manifest and rebuilt package IR, preventing stale-version deployment
  failures such as `CFD1003`.
- Product uninstall now removes the installed application through a detached
  cleanup step instead of attempting to delete the directory containing the
  running uninstaller.
- Streamer.bot deployment selects the correct exact prerelease installation
  when multiple `1.0.5` hosts are present.
- Streamer.bot rollback rejects a backup whose recorded identity or hash no
  longer matches the preceding receipt, preventing restoration of unrelated or
  stale deployment state.
- Receipt-backed uninstall continues to protect modified files while reliably
  removing Foundry-owned DLLs and receipts after a reviewed retry or repair.

### Compatibility verified

- Streamer.bot `1.0.5-beta.6`: deterministic bridge build, exact CPH catalogue,
  stable-v23 package, four-cell mock matrix, DLL install, import, compiler
  reference, compilation, execution/logging, update, repair, rollback,
  modified-file protection, uninstall, and cross-import into every supported
  Streamer.bot host passed.
- OBS Studio `32.2.1` Windows x64: pinned-SDK build, PE/ABI inspection,
  crash-isolated module load, filter create/destroy, installation, Effect
  Filter attachment, restart persistence, clean shutdown, log inspection,
  process blocking, update, repair, rollback, modified-file protection, health,
  and uninstall passed.
- Retained Streamer.bot `1.0.4`, `1.0.5-alpha.34`, `1.0.5-beta.1`, and OBS
  Studio `32.1.2` regression gates continue to pass.

### Validation

- The complete automated solution regression suite passes: 213 tests.
- Streamer.bot beta.6 and OBS 32.2.1 were tested in separate disposable host
  installations; existing stable, alpha, beta.1, and OBS 32.1.2 evidence was
  retained rather than relabelled.
- Real-host deployment lifecycle checks confirmed safe update, repair,
  rollback, modified-file protection, and uninstall on both newly supported
  host versions.

## [0.1.0-rc.1] — 2026-07-29

### Added

- Versioned `.foundryproj`, `.foundryworkspace`, template, snippet, component,
  package, test-result, compatibility, update, and deployment contracts.
- WPF desktop workspace and CLI for creating, editing, validating, building,
  testing, packaging, publishing, deploying, repairing, rolling back, and
  uninstalling Streamer.bot extensions and OBS Studio plugins.
- Roslyn-backed C# editing and pinned offline OBS 32.1.2 native code
  intelligence, completion, signature help, navigation, and diagnostics.
- Streamer.bot designer, deterministic CPHInline bridge, stable-v23 exporter,
  package viewer, verified snippets, and mock argument/event/CPH testing.
- OBS module, source, filter, input, and output templates with lifecycle-safe
  callbacks, ABI inspection, and a crash-isolated native lifecycle harness.
- Provider-neutral Test Explorer, compatibility matrices, semantic golden
  packages, and deterministic-build regression tests.
- Receipt-backed safe deployment for both providers, including preview,
  process checks, health inspection, update, repair, rollback, modified-file
  protection, and uninstall.
- Release metadata, dependency inventory, optional code signing, deterministic
  provider archives, publishing checklists, and reproducibility reports.
- Per-user desktop installation, explicit verified updates, first-run and
  toolchain checks, recovery, local diagnostic bundles, high-contrast support,
  offline-first behaviour, and privacy controls.
- Representative Streamer.bot and OBS sample projects plus private-alpha and
  final-acceptance guidance.

### Supported hosts

- Streamer.bot `1.0.4` stable.
- Streamer.bot `1.0.5-alpha.34`.
- Streamer.bot `1.0.5-beta.1`.
- OBS Studio `32.1.2` on Windows x64.

### Release status

RC1 established the Phase 16 regression baseline. Stable `0.1.0` remains
unreleased until the remaining stabilization increments and release gates are
completed.

[Unreleased]: docs/roadmap.md#phase-17--v1-stabilization-increments
[0.1.0-rc.1]: docs/release/v0.1.0-release.md
