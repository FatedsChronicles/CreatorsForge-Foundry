# Creators Forge Foundry roadmap

This document is the authoritative implementation order for Foundry. Work must
follow these phases sequentially. A later phase may not be treated as complete
merely because one of its prerequisites or supporting features was delivered
early.

The provider-neutral release bundle implemented after Phase 9 is retained as
completed prerequisite work. It does not complete Phase 13.

## Completed phases

1. **Compatibility proof and format investigation** — Streamer.bot runtime
   bridge verification, import/export captures, and cross-version acceptance.
2. **Project model and build foundation** — `.foundryproj`, validation,
   diagnostics, CLI, managed build, package IR, and generated CPH bridge.
3. **Desktop shell and workspace** — WPF application, project/document state,
   recovery, build output, and diagnostics.
4. **Code intelligence** — Roslyn editor, profile-aware CPH catalogue,
   completion, signature help, documentation, and compatibility diagnostics.
5. **Snippets** — expansion and placeholder engine, browser, guided insertion,
   and verified method and workflow catalogues.
6. **Streamer.bot designer and exporter** — structured editor, stable-v23
   encoder/decoder, deterministic IDs, package viewer, schema, and round trip.
7. **Safe Streamer.bot deployment** — previewed installation and update,
   receipts, rollback, health checks, repair, redeploy, and runtime verification.
8. **OBS plugin foundation** — minimal native module, compatibility spike,
   native project/build/package model, and host verification.
9. **OBS SDK development workflow** — pinned SDK, native editor intelligence,
   plugin designer and templates, lifecycle-safe callbacks, and runtime gate.

10. **Safe OBS deployment** — installation discovery, reviewed DLL and data
    deployment, ownership receipts and backups, version and log health checks,
    process blocking, update, rollback, modified-file protection, repair, and
    uninstall, including full disposable-instance acceptance.

## Phase 11 — Testing and debugging

Phase 11A is implemented: versioned provider-neutral test definitions and
results, the Streamer.bot `args-log-v1` mock runtime, CPH argument/event
simulation, structured assertions, and the first `foundry test` vertical slice.
Phase 11B is implemented: PE ABI inspection, an OBS module/source callback
lifecycle harness, abnormal-exit reporting, timeouts, and a crash-isolated
native helper process. Phase 11C is implemented: one provider-neutral test
orchestrator, source-controlled compatibility profiles, per-runtime regression
cells, aggregate structured matrix results, and the `foundry test-matrix` CLI
flow. Phase 11D is implemented: the desktop Test Explorer performs fresh
single-profile or matrix runs, selects disposable OBS runtimes, supports
cancellation and result filtering, presents event/assertion/log details, and
navigates structured diagnostics into the editor. Phase 11E is implemented:
reviewed Streamer.bot and OBS semantic package goldens, complete repeated
artifact-set checks, package-IR comparisons, and fixed-time byte-identical
release archives. Phase 11 is complete.

A unified testing system for both target providers:

- Streamer.bot mock runtime implementation.
- CPH argument and event simulation.
- OBS callback and lifecycle test harness.
- Automated plugin ABI inspection.
- Crash-isolated native test process.
- Test explorer in the desktop app.
- Structured test results and diagnostics.
- Compatibility regression matrix.
- Golden package and deterministic-build tests.

Exit gate: representative Streamer.bot and OBS projects can be tested from the
desktop without risking the editor process; failures are isolated, structured,
repeatable, and mapped to the selected compatibility profile.

## Phase 12 — Project templates and reusable components

Phase 12A is implemented: a versioned seven-template catalogue, distinct
Streamer.bot extension and command starters, OBS module/filter/input/output
starters, guided author/description parameters, manifest provenance, and
lifecycle-safe generated output callbacks. Phase 12B is implemented: versioned
source-first managed/native components, collision-safe build-input installation,
component provenance, combined built-in/user/project snippet catalogues, safe
catalogue import, and completion/browser integration. Phase 12C is implemented:
portable versioned workspace files, mixed-provider project trees, explicit
active-project switching, safe project addition, startup selection, and
workspace-wide validation/build orchestration. Template interchange and
migration are implemented in Phase 12D through bounded source-only template
packages, parameterised import, explicit migration previews, fixed original
backups, schema-0 to schema-1 conversion, unknown-field preservation, and
atomic validated replacement. Phase 12 is complete.

- Streamer.bot extension templates.
- OBS source, filter, and output templates.
- Multi-project workspace support.
- Shared native and managed libraries.
- User-created snippet catalogues.
- Template parameter forms.
- Template import and export.
- Upgrade and migration support for older `.foundryproj` files.

## Phase 13 — Publishing and distribution

Phase 13 is implemented: project-owned publishing metadata, legal-file gates,
automatic and declared dependency inventory, strict publishing validation,
optional Authenticode signing, deterministic provider archives, a structured
publishing checklist, atomic semantic version updates, desktop and CLI publish
commands, and SHA-256 reproducibility reports. Phase 13 is complete.

- Release-ready package metadata editor.
- Changelogs and licence files.
- Dependency inventory.
- Package validation.
- Optional code-signing support.
- Streamer.bot and OBS distribution archives.
- Publishing checklist.
- Version bump and release commands.
- Reproducible release reports.

A public Foundry marketplace is a later optional extension, not a v1
dependency.

## Phase 14 — Desktop product completion

Phase 14 is implemented: self-contained per-user packaging with safe installer
and receipt-guarded uninstaller, verified manual updates, first-run dependency
checks, unified OBS toolchain status with offline archives, expanded settings,
keyboard and high-contrast accessibility, consistent dark controls, corrected
Unicode display, local failure/recovery reporting, bounded large-project tests,
and an explicit offline/privacy boundary. Phase 14 is complete.

- Installer and uninstaller.
- Application update mechanism.
- First-run setup and dependency checks.
- OBS SDK and toolchain management interface.
- Improved project settings UI.
- Keyboard navigation and accessibility.
- Remaining theme and visual consistency work.
- Recovery and failure reporting.
- Large-project performance testing.
- Offline behaviour and privacy review.

## Phase 15 — Private alpha readiness

Phase 15 is implemented: invitation-only, separately hash-verified tester
bundles; prerelease-aware explicit updates; structured diagnostic bundle and
issue template; reviewed offline/privacy and crash-recovery procedures; dated
host compatibility evidence; guided tester onboarding and acceptance; and two
nontrivial, tested, publishable provider samples. Automated validation passed,
the remaining Test Explorer assertion defect was corrected in `0.15.0-alpha.3`,
and the product owner completed the invited-tester acceptance workflow,
including real-host verification on Streamer.bot 1.0.4 stable,
1.0.5-alpha.34, and 1.0.5-beta.1. Phase 15 is complete.

- Trusted internal distribution method for invited testers.
- Update strategy for private alpha builds.
- Issue-report template and diagnostic bundle.
- Privacy and data-handling statement.
- Crash recovery review.
- Published compatibility matrix.
- Tester onboarding guide.
- At least two representative nontrivial sample projects.

Exit gate: invited developers can install Foundry and complete the core build,
test, release, deployment, repair, and reporting workflows without developer
intervention.

## Phase 16 — Final acceptance and v1 release

Phase 16 release-candidate infrastructure is implemented: deterministic
desktop and provider archives, stable/RC release manifests and verification,
source inventory and installer provenance, an exact v1 compatibility policy,
a structured automated/manual acceptance report, clean-machine runbooks, and
the `1.0.0-rc.1` candidate. The automated gate passes. Phase 16 remains open
until clean-machine GUI acceptance is recorded and the product licence, source
commit/tag, and publisher-signing decision are approved; stable `1.0.0`
packaging refuses those unresolved release gates.

Complete clean-machine workflows for:

- Create a Streamer.bot extension.
- Edit, build, package, deploy, and verify it.
- Create an OBS plugin.
- Edit, build, package, deploy, and verify it.
- Update and repair both installations.
- Uninstall without damaging user-owned files.
- Reproduce identical release artifacts.
- Validate all supported Streamer.bot and OBS versions.

Exit gate: every supported end-to-end workflow passes on clean machines and the
result is ready to ship as Foundry v1.

## Phase 17 — v1 stabilization increments

Phase 17 contains the deliberately staged changes requested after final
acceptance. Each increment must preserve the Phase 16 regression baseline and
pass its own automated and manual gate before the next begins.

### Phase 17A — Streamer.bot 1.0.5-beta.6 investigation

Phase 17A completed on 2026-08-03. Product profile, schema, project creation,
test matrix, exact editor catalogue, exact-version probe tooling, catalogue
generation, four-host bridge builds, in-application runtime, deployment
lifecycle, normalized export capture, and four-host import/compilation passed.

Exit gate: beta.6 has an exact interface fingerprint and catalogue diff; the
DLL/CPHInline bridge, stable-v23 package import, mock matrix, runtime execution,
deployment health, and representative import/export checks pass without
regressing the retained stable, alpha.34, or beta.1 evidence.

### Phase 17B — OBS Studio 32.2.1 compatibility investigation

Phase 17B completed on 2026-08-04. Exact host fingerprinting, 32.1.2-pinned SDK
build, PE/ABI inspection, crash-isolated module load, filter create/destroy
lifecycle, and the retained 32.1.2 regression passed. The 32.2.1 `obs.dll`
surface adds two source-icon exports and removes none. Real-host filter
attachment, persistence, clean shutdown, log/health inspection, process
blocking, update, modified-file protection, repair, rollback, and uninstall
also passed.

Exit gate: the functional filter loads, attaches, persists after restart,
shuts down cleanly, produces a healthy log/deployment result, and completes
update, repair, rollback, modified-file protection, and uninstall on exact OBS
32.2.1 without regressing exact OBS 32.1.2.

## Phase 18 — Branding, themes, and visual accessibility

Phase 18 completed on 2026-08-04 after product-owner visual acceptance.
The supplied Creator Forge logo is embedded in the executable, windows, and
workspace header. Foundry provides persisted Dark, Light, and System themes,
live Windows preference handling, High Contrast priority, semantic dynamic
brushes, owned readable interaction states, and automated 4.5:1 normal-text
contrast checks. See `docs/branding-themes-accessibility.md`.

Exit gate: branding is visible and sharp; Dark, Light, and System themes remain
readable across the workspace and every dialog; the selection persists across
restart; Windows theme and High Contrast changes are respected; hover,
selection, disabled, error, and keyboard-focus states remain distinguishable.

## Phase 19 — Native Windows installer and updater

Phase 19 completed on 2026-08-04. Automated validation passed with 220 tests,
the native setup and updater compiled successfully, and product-owner manual
acceptance passed for the default destination, a custom destination, verified
in-place update, and Windows uninstall with user-data preservation.

Phase 19 replaces the developer-oriented PowerShell installation path with a
native Windows setup and update experience. It uses one stable product identity
for first install, custom-location install, upgrade, repair, and uninstall.

- Native `.exe` setup with a user-selectable destination.
- Default destination `C:\Program Files\Creators Forge\Foundry`.
- Add/Remove Programs registration and native uninstaller.
- Stable upgrade identity that retains a previously selected destination.
- Separate setup and update `.exe` release assets from one verified payload.
- GitHub Releases as the default manual update source.
- Explicit Stable and Prerelease channels; prerelease discovery excludes drafts
  and requires a published `foundry-update.json` asset.
- Size and SHA-256 verification before an updater can launch.
- Explicit elevation, running-process protection, and no silent installation.
- User settings, recovery snapshots, and projects preserved by uninstall.
- Installer/update packaging and regression tests.

Exit gate: a clean machine can install to the default or a custom directory,
launch Foundry, update in place from the GitHub-backed update dialog, retain its
chosen location and user data, and uninstall through Windows without executing
a PowerShell script.

Phase 19C acceptance additionally requires a private-alpha build on the
Prerelease channel to discover a newer published prerelease through **Check for
Updates**, stage it with matching size and SHA-256, install it in place, and
preserve the selected installation directory and user data.

Phase 19C channel acceptance passed on 2026-08-04: alpha.5 discovered, verified,
and installed alpha.6 from the published Prerelease channel and subsequently
reported alpha.6 up to date. The official source can be restored from Settings
without manually entering its URL.

The alpha.7 installed-build follow-up also passed: **Restore Official GitHub
Source** was visible and replaced a local/custom manifest location with the
canonical GitHub Releases manifest URL.

## Phase 20 — Solution Explorer-style project pane

Phase 20 turns the original read-only project listing into a familiar,
accessible project-management surface while preserving Foundry's source-first
and provider-neutral workspace model.

### Phase 20A — Explorer foundation and new items

Phase 20A completed on 2026-08-05 after automated validation and product-owner
manual acceptance passed.

- Hierarchical type badges for managed, native, web, data, metadata, and folder
  entries.
- Add and Refresh controls in the pane header.
- Right-click actions that target the selected project or folder.
- Safe creation of C#, C++, C, header, JSON, XML, HTML, CSS, JavaScript,
  TypeScript, Markdown, text, CMake, and folder items.
- Automatic default extensions, no-overwrite behavior, project-boundary and
  reparse-point protection, tree synchronization, and immediate opening of new
  editable files.

Phase 20A automated gate: focused workspace tests, Release desktop build, the
full repository regression gate, and desktop smoke tests pass with no warnings
or errors.

Manual acceptance (passed 2026-08-05):

1. Open one Streamer.bot project and one OBS project in turn.
2. Select the project root, choose **Add**, create a folder, and confirm it
   appears immediately.
3. Select that folder and create representative C#, C++, JSON, HTML, and
   Markdown files without typing extensions.
4. Confirm every file appears under the selected folder with the correct badge
   and opens in an editor tab.
5. Try to create the same item again and confirm Foundry reports `CFW1103`
   without changing the existing file.
6. Create a file outside Foundry, choose **Refresh**, and confirm it appears.
7. In a multi-project workspace, right-click a project/folder and confirm the
   item is created only in that active project.

Exit gate: all automated checks and the manual acceptance above pass without
regressing project open, edit, build, test, package, or deployment workflows.

### Phase 20B — Explorer operations and document-tab closing

Phase 20B completed on 2026-08-05 after the automated gate and product-owner
acceptance passed, including the final themed context-menu separator cleanup.

- Rename files and folders from the context menu or with `F2`.
- Move unreferenced files and folders by dragging them onto another folder in
  the active project.
- Move unreferenced items to the Windows Recycle Bin after confirmation from
  the context menu or with `Delete`.
- Reveal items in Windows File Explorer and copy project-relative (`Ctrl+C`) or
  full Windows paths.
- Protect the project manifest, declared sources/definitions/publishing files,
  containing folders, and items with open documents from unsafe mutation.
- Display a close button on each editor tab and retain dirty-document
  save/discard/cancel protection.

Phase 20B automated gate: the Release build passes with no warnings or errors,
all 240 tests pass, and managed, native, and multi-project desktop smoke tests
pass.

Manual acceptance (passed 2026-08-05):

1. Create an unreferenced file and folder, rename both using the menu and `F2`,
   and confirm the tree refreshes immediately.
2. Attempt to rename an item to an existing name and confirm `CFW1114` appears
   without overwriting either item.
3. Attempt to rename or remove the `.foundryproj`, a declared source, and a
   folder containing a declared source; confirm Foundry blocks each operation.
4. Open an unreferenced file and confirm rename/removal is blocked until its tab
   is closed.
5. Drag unreferenced files and folders onto a destination folder and onto a file
   within that folder; confirm both place the moved item inside that folder and
   refresh the tree. Confirm declared/open items, cross-project moves,
   self/descendant folder moves, and destination collisions are refused.
6. Delete an unreferenced item using `Delete`, confirm it disappears, then
   restore it from Windows Recycle Bin and refresh the tree.
7. Confirm **Show in File Explorer** selects a file and opens a selected folder;
   verify both **Copy Relative Path** and **Copy Full Path**.
8. Confirm Add Project Item shows only readable type names and its complete
   automatic-extension/no-overwrite guidance is visible.
9. Close clean and dirty tabs using their visible close buttons; exercise Save,
   Don't Save, and Cancel and confirm each result.
10. Repeat representative checks in Dark, Light, and System themes and in a
   multi-project workspace.

Exit gate: the complete automated gate and all ten manual checks pass without
regressing build inputs, document recovery, or multi-project targeting.

### Phase 20C — External-project onboarding

Phase 20C completed on 2026-08-05 after the automated gate and product-owner
manual acceptance passed.

- Analyze an existing source folder before making any change.
- Preview the exact provider-compatible `.cs` or `.c` build inputs.
- Select the Streamer.bot or OBS Studio target and compatibility profile.
- Create a single schema-v1 `.foundryproj` sidecar with create-new semantics.
- Open the adopted project immediately in Solution Explorer while retaining all
  other files as editable project content.
- Skip generated/dependency directories, directory links, and excessively deep
  trees; cap analysis at 10,000 files.
- Refuse existing Foundry projects and folders changed after preview.

Automated coverage verifies deterministic discovery, ignored-directory
handling, Streamer.bot and OBS manifests, byte-for-byte source preservation,
existing-sidecar protection, and preview/change race protection.

Manual acceptance (passed 2026-08-05):

1. Copy a small existing C# project to a disposable folder and note the hashes
   of its existing files.
2. Choose **File → Adopt Existing Folder…**, select the folder, and confirm the
   preview lists its `.cs` files while other files are described separately.
3. Select Streamer.bot and `1.0.5-beta.6`, adopt it, and confirm one
   `.foundryproj` appears and the project opens in Solution Explorer.
4. Recheck the original file hashes and confirm none changed; build the managed
   library or review any source-level compatibility diagnostics.
5. Repeat with a disposable OBS C project that implements
   `foundry_obs_plugin_load`, selecting `32.x-windows-x64`; confirm the native
   sources appear in the saved manifest and the project opens.
6. Select a folder that already contains a `.foundryproj` and confirm Foundry
   directs you to open it without creating or overwriting anything.
7. Preview a disposable folder and change its `.cs`/`.c` files before choosing
   **Adopt**. Confirm Foundry prevents adoption with the action disabled; the
   service-level race guard also returns `CFW0509` if a stale preview is
   submitted programmatically.

Exit gate: the complete automated gate and all seven manual checks pass without
changing any pre-existing external-project file.

## Phase 21 — Guided development toolchain setup

Phase 21 replaces fixed Visual Studio directory assumptions with a guided,
version-independent setup flow. It is staged so discovery and selection can be
accepted before broader remediation and SDK management changes.

### Phase 21A — Visual Studio C++ discovery and selection

Phase 21A completed on 2026-08-05 after the automated gate and product-owner
manual acceptance passed, including the corrected readable toolchain selector.

- Discover installations through `vswhere` using the C++ x64 workload ID.
- Fall back to dynamically enumerated Visual Studio roots under both Program
  Files locations without hard-coding a Visual Studio year or edition.
- Validate `cl.exe`, `link.exe`, `lib.exe`, `dumpbin.exe`, and `VsDevCmd.bat` and
  select the newest installed MSVC x64 toolset.
- Allow a user-selected Visual Studio installation root with clear validation.
- Persist the selection and display it in toolchain and first-run health checks.
- Pass the selected instance to CMake and use its exact SDK utility paths.
- Never modify user or machine environment variables.
- Stop invalid selected-instance builds with structured diagnostic `CFB1011`.

The focused automated gate covers complete/incomplete roots, newest-toolset
selection, persisted settings, health reporting, CMake instance propagation,
and invalid-selection blocking. Manual acceptance is defined in
[guided-toolchain-setup.md](guided-toolchain-setup.md).

The implementation gate passes all 250 automated tests and all six desktop
smoke cases with zero build warnings or errors. A real native build also
auto-discovered the installed Visual Studio toolchain and produced the OBS
Configurable Filter DLL, provider package, and package IR successfully.

### Planned Phase 21 increments

- **21B:** consolidated CMake, Windows SDK, architecture, and pinned OBS SDK
  readiness with guided remediation completed on 2026-08-05 after automated
  validation and product-owner manual acceptance passed. The desktop persists
  an exact validated CMake executable,
  displays five actionable readiness rows, opens Visual Studio Installer and
  the official CMake download only on request, refreshes checks in place, and
  stops invalid saved CMake selections with `CFB1012`.

  The implementation gate passes all 256 automated tests and all six desktop
  smoke cases with zero warnings or errors. Windows SDK 10.0.26100.0 passes the
  real readiness contract, and the OBS Configurable Filter real build produces
  its DLL, package ZIP, and package IR.
- **21C:** richer build diagnostics, repair/reselect actions, and complete
  disposable OBS build acceptance completed on 2026-08-05 after product-owner
  verification passed. The Development Toolchain window can apply
  recommended selections and run a minimal OBS configure/compile/link check in
  an owned temporary workspace. Timed stages and stable `CFB1101`-`CFB1106`
  diagnostics expose commands, exit codes, captured output, remediation, and
  cleanup status without touching project sources or global environment state.
  The implementation gate passes all 259 automated tests and all six desktop
  smoke cases with a zero-warning solution build. Runtime acceptance confirmed
  readiness, temporary preparation, CMake configure, native compile/link,
  configuration-specific DLL discovery, cleanup, and readable result text.

## Phase 22 - Visual design and runtime preview

Phase 22 provides visual feedback before deployment while keeping user code
outside the Foundry editor process.

### Phase 22A - Preview foundation and design surface

Implemented and accepted by the product owner on 2026-08-05:

- optional schema-v1 `preview` metadata with explicit enablement, kind, source,
  and bounded viewport;
- provider-aware source eligibility for static web, WinForms, and OBS component
  structure;
- a themed desktop design window with viewport presets and persistence;
- bounded static source analysis, SHA-256 traceability, and stable diagnostics;
- no assembly loading, browser hosting, script execution, build, or deployment.

Manual acceptance is defined in [design-preview.md](design-preview.md).
The implementation gate passed all 267 automated tests and all desktop smoke
cases with a zero-warning solution build.

### Phase 22B - Crash-isolated runtime preview and refresh

Implemented and accepted by the product owner on 2026-08-05:

- a dedicated `CreatorsForge.Foundry.PreviewHost` process that receives only
  bounded, hashed structural frame data rather than a project assembly;
- explicit starting, running, completed, failed, timed-out, and stopped states;
- an eight-second host timeout, process-tree termination, restart, and stable
  `CFW2310`-`CFW2313` recovery diagnostics;
- richer role-aware rendering for actions, inputs, chrome, headings, media,
  OBS canvases, badges, and general panels;
- manual refresh plus debounced automatic refresh whenever the selected source
  file is saved;
- in-window lifecycle controls and bounded runtime logs;
- cleanup of isolated request/result files after each run.

The Phase 22B host does not load project assemblies, execute JavaScript, or
invoke native plugin code. Phase 22C builds provider-specific visual composition
on that same isolation boundary. Manual acceptance is defined in
[runtime-preview.md](runtime-preview.md).
The implementation gate passes all 271 automated tests and all seven sample
project/workspace desktop smoke cases with a zero-warning solution build.

### Phase 22C - Provider adapters and visual samples

Implemented and accepted by the product owner on 2026-08-06:

- bounded adapter descriptors emitted by structural analysis without complete
  source text or binary paths;
- a static-web adapter with safe browser-like chrome, semantic roles, and an
  explicit scripts-blocked state;
- a WinForms adapter with form chrome and native-control composition without
  loading the managed assembly;
- an OBS component adapter with program-canvas and properties composition
  without loading libobs or a plugin DLL;
- visible adapter identity and generation information in Design Preview;
- buildable Creator Goal Overlay and Streamer Control Panel samples plus the
  existing OBS Configurable Filter, collected in a visual-preview workspace;
- conditional `System.Drawing` and `System.Windows.Forms` framework references
  for managed projects declaring `features.winForms`.

Manual acceptance is defined in [provider-preview-adapters.md](provider-preview-adapters.md).
The implementation gate passes all 279 automated tests and all ten sample
project/workspace desktop smoke cases with a zero-warning release build.
All twelve provider-adapter manual acceptance checks passed on 2026-08-06.

### Phase 22D - Executable provider preview runtimes

Implemented and accepted by the product owner on 2026-08-06:

- explicit, session-only live execution with structural preview retained as the
  non-executing default;
- bounded web-content staging and real HTML/CSS/JavaScript rendering through a
  disposable WebView2 profile with network, navigation, permission, and popup
  denial;
- deterministic WinForms build, copied-assembly loading in the isolated STA
  host, and live PNG capture;
- deterministic OBS build and real libobs module, source lifecycle, and
  properties-callback execution in the crash-isolated native host;
- bounded PNG/result sizes, linked-path rejection, timeouts, process-tree
  termination, restart/stop, logs, fallback, and owned-run cleanup.

Manual acceptance is defined in
[executable-preview-runtimes.md](executable-preview-runtimes.md).
The implementation gate passes all 283 automated tests and all ten sample
project/workspace desktop smoke cases with a zero-warning release build. A real
OBS 32.1.2 run also passed module load, source create/destroy, and the
configurable filter's `enabled` property callback.

## Phase 23 - Integrated terminal

Phase 23 adds a PowerShell terminal to the existing resizable desktop tool
area without allowing command execution inside the Foundry application
process.

Implemented and accepted by the product owner on 2026-08-10:

- a dedicated Terminal tab and **View > Integrated Terminal** command with
  **Ctrl+T** keyboard navigation, with Test Explorer moved to
  **Ctrl+Shift+T**;
- explicit start, stop, restart, and clear controls plus Up/Down command
  history;
- non-elevated Windows PowerShell with redirected input, output, and error
  streams;
- active-project-root startup and automatic session shutdown when the active
  project changes;
- bounded visible output so long-running tools cannot grow the desktop buffer
  without limit;
- process-tree termination when the user stops the terminal or Foundry closes;
- theme-aware controls and named keyboard-accessible terminal elements.

Manual acceptance is defined in [integrated-terminal.md](integrated-terminal.md).
The implementation gate passes all 289 automated tests and all ten
representative desktop smoke cases with a zero-warning release build. Focused
coverage executes real PowerShell commands and verifies working-directory,
restart, invalid-root, and child-process-tree cleanup behavior.
All thirteen manual acceptance checks passed on 2026-08-10, including keyboard
navigation, command execution and output, command history, project switching,
stop/restart and child-process cleanup, and theme readability.

## Phase 24 - Stable v1.0.0 readiness and release

### Phase 24A - Current-host compatibility refresh

Phase 24A is the mandatory compatibility gate before stable release work. It
adds an exact Streamer.bot `1.0.7-stable` project/test profile, regenerates the
CPH catalogue from the official 1.0.7 interface assembly, expands the mock
matrix and host verification script, and re-runs the retained OBS Studio
32.2.1 ABI/source-lifecycle regression with the pinned 32.1.2 SDK.

The automated gate passed on 2026-08-10: the bridge compiled against all five
exact Streamer.bot hosts, the five-profile mock matrix and representative
publishing validation passed, OBS module/source lifecycle checks passed on
32.2.1 and the retained 32.1.2 host, and the complete release build passed 295
tests plus all desktop smoke cases with zero warnings. Product-owner host
acceptance passed on 2026-08-10 in disposable Streamer.bot 1.0.7 and OBS Studio
32.2.1 installations. The final desktop acceptance also passed: initial
compatibility profiles, friendly template names, author-to-publishing metadata,
generated MIT licence and versioned changelog, publishing validation, and
wrapped prose documents all behaved as expected. **Phase 24A is complete.**

### Phase 24B - Stable release readiness and publication

Phase 24B freezes the accepted source as version `1.0.0`, finalizes the product
changelog and release notes, confirms the approved root EULA and third-party
inventory, hardens deterministic v1 packaging and independent verification,
and extends GitHub Release automation with an explicit publisher-signing
decision. The release workflow creates `v1.0.0` only after the clean committed
source, complete regression gate, stable package, hashes, and release assets
all pass. The clean `1.0.0-rc.2` candidate and unsigned-publication waiver were
accepted by the product owner on 2026-08-10. Phase 24B is complete pending only
the automated publication of the accepted commit as tag and stable GitHub
Release `v1.0.0`.

## Phase 25 - Visual Streamer.bot extension builder

### Phase 25A - Streamer.bot import-to-project foundation

Phase 25A adds a non-executing importer for verified Streamer.bot payload-v23
and payload-v24 exports. The desktop accepts pasted import code or a file,
shows the decoded provenance, entity inventory, editable and preserved items,
C# sources, references, paths, and blocking security findings before any
project files are written.

Imported projects use Streamer.bot definition schema v2 and may be package-only:
they do not invent a managed DLL or CPHInline bridge. Execute C# bodies are
confined beneath `streamerbot/code`, remain outside managed build inputs, and
are re-embedded as text without execution. A separate preservation sidecar
retains opaque nodes, unknown fields, original ordering, source GUIDs, payload
version, and `exportedFrom` provenance. Same-format export patches supported
edits into a clone of that payload; unknown versions remain analysis-only and
cross-version conversion is blocked when opaque content exists.

The importer assigns no licence to third-party work and creates no invented
behavioral tests. Publishing therefore remains blocked until the developer
reviews the imported workflow, supplies an authorised licence, resolves
machine-specific paths, and adds appropriate tests. Automated envelope,
adapter, migration, preservation, package-only build, C# re-embedding,
credential-redaction, and desktop smoke coverage form the implementation gate.

Product-owner acceptance passed on 2026-08-10 with representative exports from
Streamer.bot 1.0.4, 1.0.5-alpha.34, 1.0.5-beta.1, 1.0.5-beta.6, and 1.0.7.
The verified payload-v23 and payload-v24 adapters completed the required real
third-party-style multi-action decode, project creation, supported-field and
C# editing, path-resolution, deterministic same-format re-export, exact-host
import, compile, execution, persistence, and opaque-content preservation
checks. The dark-mode designer and guided compiler-reference repair follow-up
also passed. **Phase 25A is complete.**

### Phase 25B - Expanded Streamer.bot action model and toggles

Phase 25B expands definition schema v3 and the structured designer around the
wire fields verified in retained payload-v23 and payload-v24 exports. Actions
now retain groups, random selection, pending/history exclusions, concurrency,
always-run state, enabled state, and ordered weighted sub-actions. Commands
retain bot/internal-message filtering and source masks, while queues and the
core entities can carry developer-facing descriptions.

The designer provides explicit controls for these options, action and
sub-action duplication, ordered movement, and a profile-aware Validation tab.
The same central diagnostic policy runs during builds and detects unsafe
weights, invalid or conflicting aliases, empty source masks, unverified
profile features, ambiguous action names, unlinked commands, unused queues,
and concurrent workflows that appear to depend on sequential arguments.

Clean stable-v23 packages and preserved v23/v24 imports patch these fields and
the edited action/sub-action ordering deterministically. Foundry still does not
invent native sub-action type numbers: wider native action/trigger catalogues,
resource binding, and native-to-C# conversions remain later Phase 25 work.
Automated adapter, migration, diagnostic, ordering, golden-package, desktop,
and complete regression coverage form the Phase 25B implementation gate.

Product-owner manual acceptance passed on 2026-08-11. The expanded action
toggles, weighted ordering, command and queue settings, duplication and move
operations, shared validation, deterministic output, and retained v23/v24
round trips behaved as expected. PR #21 merged the accepted implementation as
commit `39bf48b`. **Phase 25B is complete.**

### Phase 25C - Schema-driven operation catalogues and palette

Phase 25C establishes the extensible catalogue boundary for Streamer.bot
triggers and native sub-actions. A versioned JSON catalogue describes stable
Foundry operation IDs, category and display metadata, verified Streamer.bot
native type mappings, supported profiles, output capabilities, reusable typed
fields, arguments consumed and produced, and documentation provenance.

The first reviewed catalogue is intentionally small: Command trigger type 401,
Test trigger type 702, and Set Argument sub-action type 123. These are the only
general-purpose mappings currently proven by retained payload-v23 and
payload-v24 captures. Foundry must preserve other imported types read-only and
must not expose guessed native mappings.

The Streamer.bot Designer adds a searchable, categorized, profile-filtered
palette. Selecting an operation generates its parameter form from catalogue
field definitions and shows native output mode, compatibility, and known
runtime arguments. The central validation pipeline checks catalogue identity,
profile availability, and native type integrity before export. Additional
catalogue entries can be added later only with representative capture evidence,
adapter fixtures, and exact-host acceptance.

Product-owner manual acceptance passed on 2026-08-11, including the focused
friendly Command selector correction. PR #22 merged the accepted catalogue,
palette, compatibility validation, runtime-argument display, and documentation
as commit `09c0773`. **Phase 25C is complete.**

### Phase 25D - Importer experience and friendly source navigation

Phase 25D refines the import-to-project workflow without changing the safe
SBAE decoding boundary:

- **Extension-agnostic file input:** the file picker explicitly lists
  `.txt`, `.sb`, `.streamerbot`, and arbitrary developer-defined extensions.
  File contents, not the filename suffix, remain the security and format
  authority, so users do not need to rename an export to `.txt`.
- **Drag and drop:** the import-code area accepts one local file, visibly
  indicates the drop target, reads it with the same bounded text path as **Load
  from file**, and retains paste support. Multiple files, folders, shortcuts,
  remote URLs, and oversized content are rejected clearly.
- **Synchronized creation defaults:** changing Project Name updates the
  suggested package ID slug and destination folder name until the user manually
  edits either derived value. Manual package/folder choices will never be
  overwritten silently, and reset-to-suggestion actions are available.
- **Friendly imported-code labels:** Solution Explorer displays the action
  name and an ordered operation label for extracted Execute C# sources. Stable
  confined ID-based paths remain on
  disk so renames cannot break relationships, preservation mappings, or
  deterministic re-export.

The implementation uses one strict-UTF-8, size-limited, local-file reader for
picker and drop operations. It rejects folders, shortcuts, multiple drops,
invalid text, and oversized files before analysis. Friendly tree names are
display-only aliases with real relative paths available as tooltips and path
commands; definition-owned C# paths and containing folders are protected from
rename, move, and deletion. Automated arbitrary-extension, invalid-input,
naming, stable-label, full-regression, and desktop smoke coverage form the
implementation gate.

Product-owner manual acceptance passed on 2026-08-11. PR #23 merged the
accepted importer experience as commit `ad858ba`. **Phase 25D is complete.**

### Phase 25E - Resources and portability foundation

Phase 25E introduces Streamer.bot definition schema v4 and deterministically
migrates definitions from schemas v1-v3. Structured resource requirements use
stable IDs, supported resource types, required/optional state, explicit
portability classifications, validation rules, and stable bindings to actions,
commands, queues, triggers, and sub-actions.

The Streamer.bot Designer includes a Resources workspace. Central validation
rejects missing relationships, invalid URLs and patterns, absolute machine
paths incorrectly marked portable, and credential-like values without printing
secret content. It warns about unused resources, absolute paths, and required
destination configuration. Imported absolute Execute C# references become
explicit local-file requirements while the established export safety gate is
retained.

Builds produce a deterministic `streamerBotPortabilityReport` package artifact
that contains classifications, counts, and bindings but deliberately omits
suggested values. Phase 25E does not invent resource fields in the Streamer.bot
wire format or alter preserved opaque content. Manual acceptance is defined in
[streamerbot-resources-portability.md](streamerbot-resources-portability.md).
The implementation gate passes a zero-warning Release build, all 323 automated
tests, and all ten representative desktop smoke projects.

Product-owner manual acceptance passed on 2026-08-11. PR #24 merged the
accepted resource workspace, portability diagnostics, package report, and
dark-theme follow-up as commit `6ae8981`. **Phase 25E is complete.**

### Phase 25F - Execute C# authoring and verified native conversion

Phase 25F makes Execute C# a first-class, source-controlled Streamer.bot
sub-action. The Designer can create a confined manual C# source beneath
`streamerbot/code/<action-id>/<subaction-id>.cs`, save the definition, and
navigate directly into the existing Roslyn editor. These files remain text;
Foundry never executes imported or authored C# during design, validation, or
packaging, and they are not added to managed DLL build inputs.

The first native-to-C# conversion is deliberately limited to the retained,
verified Set Argument type 123 mapping. Foundry shows the complete inert source
before conversion, emits deterministic escaped `CPH.SetArgument(...)` code,
and records generator revision, source entity, and source hash in definition
schema v5. Auto Type conversions are blocked until native coercion semantics
are proven equivalent. Generated files are written once and never silently
regenerated; later edits are labelled Detached and preserved as authoritative
manual source.

Both source-authored stable-v23 packages and imported preserved v23/v24
packages deterministically re-embed the confined source as Execute C# type
99999 while retaining IDs, order, enabled state, weights, references, and
same-format provenance. Broader conversions require representative capture
evidence and exact-host verification. Manual acceptance is defined in
[streamerbot-csharp-authoring.md](streamerbot-csharp-authoring.md).

The acceptance follow-up separates source-only and managed project intent.
**Streamer.bot C# action package** creates a package-only Designer project with
no managed DLL, bridge sub-action, or invented managed-entry test. The managed
command-workflow template retains the bridge. The Actions grid also reuses
existing group and queue choices, shows the resolved Queue Name, and keeps Group
editable for intentional overrides. Standard text-edit context menus now use
the active Foundry theme throughout the Build pane and Designer grids.

Product-owner manual acceptance passed on 2026-08-12. Execute C# creation, Set
Argument conversion, source-only packaging, managed bridge separation, group
and queue suggestions, dark-theme menus, save/reopen, deterministic build, and
Streamer.bot import checks passed. The acceptance follow-up also removed a
re-entrant Actions-grid refresh that could lock the Designer UI. **Phase 25F is
complete.**

### Phase 25G - Command groups and command organization

Phase 25G adds first-class Streamer.bot command grouping to the Commands tab.
Before changing the source model, representative exports will establish the
exact command-group wire fields, IDs, ordering, and empty-group behavior for
each supported payload adapter. Foundry will not infer undocumented fields.

The next Streamer.bot definition schema will add the verified group relationship
to commands with deterministic migration. Imported commands retain their source
wire IDs and group relationships; newly authored groups receive deterministic
identities derived from the project identity wherever the verified wire format
requires an ID. Unsupported or adapter-specific group data remains preserved
read-only under the existing opaque-data rules.

The Commands tab will provide a friendly **Group** selector populated from
groups already used by other commands, while remaining editable so a creator
can add a new group or intentionally replace a suggestion. Blank means the
command is ungrouped. Rename, duplicate, save/reopen, diagnostics, and Solution
Explorer labels must retain the correct relationships without exposing IDs in
place of names.

Import, validation, package IR, and the verified v23/v24 encoders will carry
command grouping deterministically. Acceptance requires grouped and ungrouped
multi-command fixtures, import/edit/re-export preservation, stable IDs and
ordering, zero changes to opaque nodes, and successful import into every exact
Streamer.bot host claimed by the adapter, currently 1.0.4, 1.0.5-alpha.34,
1.0.5-beta.1, 1.0.5-beta.6, and 1.0.7. The full Foundry regression suite must
continue to pass with no OBS regressions.

Implementation uses definition schema v6 and the verified text-valued
`data.commands[].group` property documented by Streamer.bot. The Commands tab
offers existing groups through a case-insensitive editable selector, preserves
group membership when duplicating an editable command, and represents an empty
selection as an ungrouped command. Generated v23 and preserved v23/v24 packages
carry the value without changing source IDs, ordering, trigger relationships,
or opaque nodes. Manual acceptance is defined in
[streamerbot-command-groups.md](streamerbot-command-groups.md).

Product-owner manual acceptance passed on 2026-08-12 for command group
creation, suggestions, persistence, deterministic packaging, and supported-host
imports. The follow-up keeps the Designer open after Save, adds Ctrl+S inside
the Designer, and exposes Ctrl+Shift+D from the workspace. Product-owner
confirmation of those workflow adjustments passed on 2026-08-12. **Phase 25G
is complete.**
