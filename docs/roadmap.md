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
