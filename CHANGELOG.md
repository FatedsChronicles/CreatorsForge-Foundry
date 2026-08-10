# Changelog

All notable changes to Creators Forge Foundry are documented in this file.

## [Unreleased]

No changes yet. The next development sessions will begin after the stable
1.0.0 release baseline is tagged and published.

## [1.0.0] - 2026-08-10

Foundry 1.0.0 is the first stable release. It consolidates the accepted
release-candidate, private-alpha, compatibility, desktop-product, preview,
terminal, and stable-readiness increments described below.

### Added

- Phase 24B stable 1.0.0 release notes, approved product-EULA packaging,
  updated dependency notices, final acceptance guidance, and GitHub publication
  of independently verifiable v1 evidence.
- A dedicated stable release bundle containing the setup and updater
  executables, portable archive, update manifest, product licence, notices,
  compatibility evidence, samples, source inventory, and manifest hash.
- Phase 24A exact Streamer.bot 1.0.7 stable compatibility profile, generated
  CPH catalogue fingerprint, five-profile mock matrix, schema support, host
  discovery normalization, and disposable-host verification tooling.
- A pre-v1 compatibility refresh gate that revalidates the pinned OBS 32.1.2
  SDK output against the exact OBS Studio 32.2.1 Windows x64 runtime before
  stable release approval.
- Complete publishing defaults for newly created projects, including the form
  author, summary, package identity, editable MIT `LICENSE.txt`, and a
  versioned starter `CHANGELOG.md`.
- Automatic wrapping for `.txt`, `.md`, and `.markdown` editor documents.
- Phase 23 integrated PowerShell terminal in the resizable desktop tool area,
  with project-root startup, command history, bounded output, explicit
  start/stop/restart/clear controls, and **Ctrl+T** navigation.
- A non-elevated terminal process boundary with redirected standard streams,
  automatic project-change shutdown, and process-tree termination when the
  user stops the session or Foundry closes.
- Phase 22D opt-in executable previews: real staged HTML/CSS/JavaScript through
  disposable WebView2, real built WinForms capture in an isolated STA host, and
  real OBS module/source/property callback execution through libobs.
- Explicit live-mode warnings, disposable OBS runtime selection, captured PNG
  display, and structural fallback when build or executable preview fails.
- Phase 22C provider-specific isolated preview adapters for static-web
  documents, WinForms design models, and OBS components, with visible adapter
  identity and distinct browser, form, program-canvas, and properties layouts.
- Buildable Creator Goal Overlay and Streamer Control Panel visual samples plus
  a Visual Preview Samples workspace containing those projects and the OBS
  Configurable Filter.
- Phase 22B crash-isolated runtime preview hosting with explicit lifecycle
  states, an eight-second timeout, stop/restart recovery, bounded logs, and
  cleanup of per-run request/result files.
- Role-aware visual frames and debounced automatic refresh when the selected
  preview source is saved, while retaining an explicit manual refresh action.
- Phase 22A non-executing design preview foundation with optional project
  metadata, provider-aware eligibility, HD/Full HD/Compact/Portrait/custom
  viewports, source hashing, and structural surfaces for static HTML, WinForms,
  and OBS components.
- A **View > Design Preview...** command and **Preview** toolbar action with
  persisted per-project settings and explicit disable support.
- Phase 21C disposable native-build verification, which configures, compiles,
  links, checks, and removes a minimal x64 OBS module outside the open project.
  Results include timed stages, invoked commands, captured tool output, and
  stable `CFB1101`-`CFB1106` diagnostics.
- A **Use recommended tools** repair action and **Verify native build** action
  in the Development Toolchain window.
- Phase 21B consolidated native-toolchain readiness for CMake, Visual Studio
  C++ x64, Windows SDK headers/tools/libraries, target architecture, and the
  pinned OBS SDK, with per-component guidance and explicit remediation actions.
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

- Stable packaging now requires a clean tracked worktree and uses the approved
  root `LICENSE.md`; the v1 verifier checks the update manifest against the
  actual updater executable and confirms setup/updater payload identity.
- The GitHub stable-release workflow now records one publication timestamp,
  runs the deterministic v1 packager and verifier, uses curated 1.0.0 release
  notes, and requires an explicit release-owner decision before an unsigned
  stable build can proceed.
- **Ctrl+T** now opens Integrated Terminal. Test Explorer moves to
  **Ctrl+Shift+T** so both tools retain a direct keyboard command.
- Terminal commands are Base64-transported into the persistent PowerShell
  runspace and explicitly rendered as text so object, native-command, and error
  output appears immediately without executing inside the Foundry process.
- OBS executable preview now offers only supported 32.1.2/32.2.1 runtimes,
  prefers the project API match instead of the oldest discovered installation,
  and reports module/open/init/source flags when lifecycle execution fails.
- Managed projects with `features.winForms` now receive deterministic
  `System.Drawing` and `System.Windows.Forms` framework references so their
  declared UI source builds as well as previews.
- Design Preview selectors now show concise kind and viewport names, select the
  inferred source visibly, and choose the HD preset for a new 1280x720 preview
  instead of displaying generated record representations or a blank source.
- Disposable native verification now locates the exact probe DLL throughout
  CMake's owned build tree, including configuration-specific `Release`
  subdirectories, and uses ASCII result separators to prevent mojibake in the
  verification dialog.
- OBS builds now persist and invoke the exact validated `cmake.exe`; invalid or
  removed selections stop before configuration with diagnostic `CFB1012`.
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

- Executable preview rejects linked or escaping paths, bounds staged files and
  PNG/result payloads, blocks browser network/navigation/permissions/popups,
  and never loads project code in the Foundry desktop process. Structural mode
  remains the non-executing default.
- Phase 22C adapter descriptors are limited to 12 metadata entries with bounded
  keys and values. Provider adapters never receive project binary paths or full
  source text and never embed a browser engine, load managed project code,
  initialize libobs, or load a native plugin.
- Phase 22B sends only bounded visual-frame data to a separate process, never a
  project binary path or complete source text. The host is time-limited,
  process-tree isolated, output-bounded, and removes its protocol files after
  every run.
- Design preview source resolution is project-confined and limited to 1 MiB and
  48 bounded elements. Phase 22A never loads a project assembly, hosts a browser,
  executes JavaScript, invokes native code, builds, or deploys.
- Native toolchain verification uses a uniquely named system-temporary
  workspace, validates ownership before recursive cleanup, never writes to the
  open project, and never changes global environment variables.
- CMake and Visual Studio choices remain process-scoped. Visual Studio Installer
  and the official CMake download page open only after explicit button actions,
  and no PATH or system environment variable is modified.
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

- Phase 24A automated checks pass: five exact Streamer.bot bridge builds, the
  1.0.7 CPH fingerprint and unchanged 512-method/564-overload surface, the
  five-profile mock matrix, package/publishing validation, OBS 32.2.1 and
  retained 32.1.2 source lifecycle, 295 tests, and all desktop smoke cases.
- Product-owner acceptance passed in disposable Streamer.bot 1.0.7 and OBS
  Studio 32.2.1 hosts, including runtime and recoverable deployment checks.
- Phase 24A final desktop acceptance passed for immediate profile population,
  friendly project-template display, publishing author/default documents,
  clean publishing validation, and wrapped text/Markdown editing.
- Phase 23 product-owner acceptance passed on 2026-08-10. All thirteen manual
  checks passed, including **Ctrl+T** navigation, visible command output,
  Up/Down history, project-root switching, cancellation, process cleanup, and
  Dark, Light, and System theme readability.
- Phase 23 passes all 289 automated tests and all ten representative desktop
  smoke cases with a zero-warning release build. Focused integration coverage
  executes a real PowerShell command in the project root, replaces sessions
  across project roots, rejects missing roots, and terminates a running child
  process tree.
- Phase 22D product-owner acceptance passed on 2026-08-06, including all live
  static-web, WinForms, and OBS execution checks, lifecycle recovery, supported
  OBS runtime selection, and process cleanup.
- Phase 22D passes all 283 automated tests and all ten representative desktop
  smoke cases with a zero-warning release build. Real integration coverage
  executes JavaScript while proving outbound requests are blocked, captures the
  built WinForms sample, rejects path escape, contains malformed host output,
  and verifies disposable cleanup.
  The supplied OBS 32.1.2 runtime also loaded the configurable-filter DLL,
  registered/created/destroyed its source, and returned its live `enabled`
  property callback without a module failure.
- Phase 22C product-owner acceptance passed on 2026-08-06, covering all twelve
  static-web, WinForms, and OBS adapter checks, live refresh, isolation logs,
  lifecycle controls, sample builds, and host cleanup.
- Phase 22C passes all 279 automated tests and all ten representative
  project/workspace desktop smoke cases with a zero-warning release build.
  Focused coverage runs each real provider adapter, verifies distinct visual
  roles and the no-load boundary, validates all three sample projects, and
  builds both new managed visual samples.
- Phase 22B product-owner acceptance passed on 2026-08-05, including runtime
  lifecycle transitions, retained frames after Stop, restart generations,
  automatic/manual refresh, bounded logs, responsive close, and readable
  deployment installation selectors.
- Phase 22B passes all 271 automated tests and all seven project/workspace
  desktop smoke cases with a zero-warning solution build. Focused coverage
  executes the real isolated host, verifies restart generations, contains a
  forced timeout, and reports a missing host through stable diagnostics.
- Phase 22A passes all 267 automated tests and six desktop smoke cases with a
  zero-warning solution build. Focused coverage verifies preview eligibility,
  bounded UTF-8 source reads, HTML script exclusion, WinForms layout extraction,
  missing/oversized source diagnostics, disable behavior, forward-compatible
  persistence, and construction of the themed preview window.
- Phase 21C product-owner acceptance passed on 2026-08-05: the installed
  Visual Studio, CMake, Windows SDK, and pinned OBS SDK completed readiness,
  disposable preparation, configure, compile/link, nested DLL discovery, and
  owned-workspace cleanup successfully. The result dialog also confirmed the
  ASCII-only status formatting correction.
- Phase 21C passes all 259 automated tests and six desktop smoke cases with a
  zero-warning solution build. Focused tests cover successful disposable
  compilation, readiness blocking, captured configure failures, exact x64 SDK
  arguments, expected-artifact inspection, and owned-workspace cleanup.
- Phase 21B product-owner acceptance passed, including the consolidated
  Development Toolchain and first-run displays for CMake 4.4.2, Visual Studio
  Community 2026/MSVC 14.51.36231, Windows SDK 10.0.26100.0 x64, native x64,
  and pinned OBS SDK 32.1.2 readiness.
- Phase 21B passes all 256 automated tests and six desktop smoke cases with a
  zero-warning build; Windows SDK 10.0.26100.0 passes the real x64 readiness
  contract and the representative OBS native build succeeds.
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

### Compatibility stabilization since 0.1.0-rc.1

The following host-compatibility work was completed after the packaged
`0.1.0-rc.1` baseline and is included in stable 1.0.0.

#### Added

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

#### Changed

- Streamer.bot installation discovery now reads and matches the executable's
  exact product version, including prerelease suffixes, instead of relying on a
  folder name or choosing another `1.0.5` prerelease.
- The Streamer.bot compatibility matrix now contains four exact runtime cells.
- The OBS `32.x-windows-x64` project profile now resolves only the explicitly
  verified `32.1.2` and `32.2.1` runtimes; it does not imply support for every
  OBS 32.x release.
- Final-acceptance and compatibility documentation now distinguish the pinned
  OBS build SDK from the exact runtime versions verified by Foundry.

#### Fixed

- The completed PreviewHost can now be explicitly stopped while preserving its
  last frame, and deployment installation selectors show concise version/path
  labels instead of generated `InstallationChoice` record text.
- Preview-host failures and timeouts now remain outside the Foundry desktop and
  report stable `CFW2310`-`CFW2313` recovery diagnostics while preserving the
  last safe structural frame.
- **Ctrl+Shift+P** now opens Design Preview as advertised by the View menu.
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

#### Compatibility verified

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

#### Validation

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

[Unreleased]: https://github.com/FatedsChronicles/CreatorsForge-Foundry/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/tag/v1.0.0
[0.1.0-rc.1]: docs/release/v0.1.0-release.md
