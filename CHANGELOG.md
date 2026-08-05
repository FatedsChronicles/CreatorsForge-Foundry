# Changelog

All notable changes to Creators Forge Foundry are documented in this file.

## [Unreleased] - v0.1.0-beta.1

### Added

- Phase 21A guided Visual Studio C++ x64 setup with `vswhere` discovery,
  validated manual-root selection, persisted installation choice, and clear
  readiness details for every required MSVC tool.
- Phase 20C external-project onboarding with read-only folder analysis, exact
  managed/native build-input preview, provider/profile selection, safe
  `.foundryproj` sidecar creation, and immediate Solution Explorer opening.
- Phase 20A Solution Explorer foundation with hierarchical file-type badges,
  context-aware file/folder creation, automatic extension handling, immediate
  opening of new editable files, and explicit project-tree refresh.
- Safe built-in project items for C#, C++, C, headers, JSON, XML, HTML, CSS,
  JavaScript, TypeScript, Markdown, text, and CMake files, plus folders. Creation is
  constrained to the active project and never overwrites an existing item.
- Phase 20B Solution Explorer operations: rename (`F2`), protected drag-and-drop
  moves, confirmed recoverable deletion (`Delete`), reveal in Windows File
  Explorer, copy relative/full paths, and visible close buttons on every editor
  tab.
- Persisted Dark, Light, and System themes with live Windows-theme handling,
  High Contrast priority, readable interaction states, and SVG branding.
- A conventional Inno Setup 6 Windows installer with a stable product identity,
  Windows Apps / Add or Remove Programs registration, Start Menu integration,
  and an optional desktop shortcut.
- User-selectable installation directories with
  `C:\Program Files\Creators Forge\Foundry` as the clean-install default.
- Separate versioned setup and update `.exe` release assets generated from the
  same self-contained Windows x64 payload.
- The official GitHub Releases manifest as the default manual update source,
  while retaining local update manifests for offline testing and distribution.
- Explicit Stable and Prerelease update channels. Stable follows GitHub's latest
  stable release; Prerelease safely discovers published alpha, beta, and
  release-candidate manifests while excluding drafts.
- A **Restore Official GitHub Source** settings action that restores the
  canonical update-manifest URL without requiring users to paste it manually.
- A two-step **Stage Verified Update** and **Install Verified Update** desktop
  flow that verifies package size and SHA-256 before permitting execution.
- Optional Authenticode signing hooks for the setup executable and generated
  native uninstaller when a publisher signing command is supplied.
- A manual, draft-by-default GitHub Actions release workflow that runs the full
  regression gate, compiles and verifies the native payload, creates the tag and
  GitHub Release, and uploads the setup, updater, and update manifest assets.

### Changed

- The Development Toolchain selector now displays a compact Visual Studio name
  and MSVC version instead of the generated toolchain-record representation;
  the complete compiler path remains available in status details and a tooltip.
- OBS CMake builds now receive the selected Visual Studio installation through
  `CMAKE_GENERATOR_INSTANCE`, while pinned SDK generation uses `dumpbin.exe` and
  `lib.exe` from that same validated toolset.
- The Add Project Item dialog now renders plain item-type names and provides
  enough space for its complete automatic-extension and no-overwrite guidance.
- Dragging onto a file now treats that file's containing folder as the move
  destination, allowing items to be dropped alongside existing files.
- The Solution Explorer context menu uses a compact local item template without
  an unused icon/check gutter, eliminating the bright bracket-shaped dividers.
- Windows installation, upgrade, and uninstall now use a registered native
  setup workflow instead of requiring end users to run PowerShell scripts.
- Clean installations default to Program Files, while native upgrades retain
  the destination previously selected by the user.
- Receipt-backed installations created by the former PowerShell workflow are
  safely adopted in place instead of being duplicated or recursively deleted.
- The verified updater launches through Windows elevation only after explicit
  user confirmation; Foundry then closes so setup can replace application files.
- Native uninstall removes installer-owned application files and shortcuts but
  preserves projects, settings, recovery snapshots, and other user-owned data.

### Security

- Toolchain selection is scoped to Foundry build processes and never changes
  user or machine `PATH`; invalid saved roots stop before CMake with `CFB1011`.
- External-project adoption skips dependency/generated folders and directory
  links, caps scans at 10,000 files and 32 levels, refuses existing Foundry
  manifests, uses create-new sidecar writes, and rechecks sources after preview.
- Solution Explorer blocks rename/move/deletion of the project manifest, declared
  build/package/test inputs, folders containing those inputs, and items with
  open editor documents. Removal uses the Windows Recycle Bin rather than
  permanent deletion.
- Update packages must come from a local path or HTTPS endpoint and must match
  the release manifest's declared length and SHA-256 hash.
- A process-wide Foundry mutex prevents native uninstall while the application
  is running, while Windows Restart Manager handles files in use during update.
- Legacy installation adoption requires both the existing Foundry executable
  and its ownership receipt; no unreceipted directory is adopted or removed.

### Validation

- Phase 21A product-owner acceptance passed with Visual Studio Community 2022
  and 2026: discovery, manual selection, persistence, invalid-root refusal,
  setup health, OBS build integration, unchanged PATH, and readable selector
  labels were confirmed.
- Phase 21A passes all 250 automated tests and six desktop smoke cases with a
  zero-warning build; a real auto-discovered Visual Studio/CMake build produced
  the representative OBS plugin DLL, package ZIP, and package IR.
- Phase 20C product-owner acceptance passed for Streamer.bot and OBS external
  folder adoption, build-input preview, sidecar creation, source preservation,
  existing-project refusal, and stale-preview prevention.
- Phase 20C regression coverage verifies Streamer.bot and OBS adoption,
  deterministic discovery, byte-for-byte source preservation, ignored-folder
  handling, existing-manifest refusal, and preview/change race protection.
- Phase 20B product-owner acceptance passed for rename, drag-and-drop moves to
  folders and alongside files, protected Delete, path copying, File Explorer,
  closeable tabs, Add Project Item layout, and the cleaned context menu.
- The Phase 20B Release gate passes all 240 automated tests and managed, native,
  and multi-project desktop smoke tests with zero build warnings or errors.
- Phase 20A product-owner acceptance passed for Solution Explorer presentation,
  context-aware file and folder creation, automatic extensions, immediate file
  opening, duplicate protection, refresh, and multi-project targeting.
- Inno Setup 6.7.3 compiled the native `0.19.0-alpha.1` setup and updater
  executables with matching payload hashes and correct Windows product metadata.
- Product-owner acceptance passed installation to the default Program Files
  destination and a user-selected custom destination.
- The verified `0.19.0-alpha.1` to `0.19.0-alpha.2` in-place update retained the
  selected directory, settings, recovery state, and user projects.
- The GitHub-hosted release rehearsal and clean in-place update from
  `0.19.0-alpha.1` to `0.19.0-alpha.3` passed using the generated native updater.
- Phase 19C product-owner acceptance passed: alpha.5 discovered the published
  alpha.6 prerelease through Foundry, verified and installed it, then reported
  alpha.6 up to date on the Prerelease channel.
- Alpha.7 installed-build acceptance passed: **Restore Official GitHub Source**
  was visible and restored a local/custom manifest location to Foundry's
  canonical GitHub Releases URL correctly.
- GitHub Release automation now runs on the Node.js 24-compatible
  `actions/checkout@v7`, `actions/setup-dotnet@v6`, and
  `actions/upload-artifact@v7` action majors.
- Windows Installed Apps uninstall removed Foundry and its shortcuts without
  removing user-owned data or requiring a PowerShell script.
- The complete automated suite passes 223 tests with zero build warnings or
  errors and all six desktop smoke-test projects passing.
- Release automation regression tests enforce manual dispatch, least-privilege
  release permissions, duplicate tag/release rejection, and exact asset upload.

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
