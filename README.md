# Creators Forge Foundry

Creators Forge Foundry is a Windows development environment for creating,
testing, packaging, and maintaining Streamer.bot extensions and native OBS
Studio plugins. The v1 candidate includes the Windows-x64 OBS SDK workflow,
native designer, package model, safe deployment, unified testing, publishing,
desktop installation, recovery, and final-acceptance process.

The repository includes a WPF desktop workspace and CLI. It contains the
versioned source-first project model, project creation and persistence, a
Roslyn-backed C# editor, managed builds, deterministic bridge generation,
structured diagnostics, Streamer.bot compatibility experiments, tests, and
architecture records.

The current implementation sequence and authoritative phase numbering are in
[docs/roadmap.md](docs/roadmap.md).

## Prerequisites

- Windows 10 or later.
- .NET SDK 10.0.302. `global.json` permits later 10.0 patch releases.
- PowerShell 5.1 or later.
- CMake 3.20 or later and the Visual Studio C++ x64 toolchain for OBS projects.

## Restore, build, and test

From the repository root:

```powershell
.\build.ps1
```

The command restores dependencies, builds the complete solution in Release
configuration, and runs all tests. Use `.\build.ps1 -Configuration Debug` for
a Debug build. Restore uses the repository-owned `NuGet.config`, so it does not
depend on machine-specific package-source configuration.

Validate or build the sample project through the CLI:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  validate .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\ObsCompatibilityProbe\ObsCompatibilityProbe.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  sdk install obsstudio

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  release .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  publish validate .\MyExtension\MyExtension.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  version .\MyExtension\MyExtension.foundryproj patch

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  publish .\MyExtension\MyExtension.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj `
  --obs "F:\OBS-Studio-32.1.2-Creator_Forge_Foundry"

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test-matrix .\samples\HelloFoundry\HelloFoundry.foundryproj

dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test-matrix .\samples\ObsPassthroughFilter\ObsPassthroughFilter.foundryproj `
  --obs "F:\OBS-Studio-32.1.2-Creator_Forge_Foundry"
```

The build command compiles the declared sources into `build/managed`, generates
`build/bridge/CPHInline.cs`, emits a verified stable-v23 import code under
`build/streamerbot`, and writes `build/package-ir.json` with relative artifact
paths and SHA-256 hashes. Commands exit with `0` for success, `1` when an
operation reports an error, `2` for invalid command usage, and `130` when
cancelled.

The `test` command performs a fresh build, simulates declared Streamer.bot
events and CPH arguments through the mock runtime, evaluates structured
assertions, and writes `build/test-results/latest.json`. See
[docs/testing-and-debugging.md](docs/testing-and-debugging.md).

OBS tests additionally inspect the native DLL ABI and run module load plus
source create/destroy callbacks inside a timeout-controlled helper process, so
a native crash cannot terminate Foundry.

The `test-matrix` command runs every declared compatibility profile through
the same provider-neutral orchestration, retains one result per runtime cell,
and writes `build/test-results/compatibility-matrix.json`.

In the desktop, **Build > Test Explorer** (`Ctrl+T`) runs the same single-test
and compatibility-matrix workflows. It filters results by text or outcome,
shows event arguments, assertions, logs and CPH calls, and navigates actionable
diagnostics back into the editor. OBS projects can select one or more saved
disposable installations before running.

The regression suite also compares reviewed Streamer.bot and OBS
package snapshots and proves unchanged package/release builds are byte
identical. See
[docs/golden-package-regressions.md](docs/golden-package-regressions.md).

Launch the desktop workspace:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.App
```

For a complete action-by-action guide to the desktop, including editor,
designer, testing, publishing, deployment, recovery, and update workflows, see
[the user training manual](docs/user-training-manual.md).

Create the self-contained Windows desktop package and update manifest:

```powershell
.\eng\desktop\package-desktop.ps1 -Version 1.0.0
```

First launch performs local dependency checks. **Tools > Development
Toolchain** manages CMake/MSVC/OBS SDK readiness and supports offline SDK
archives. **Help > Check for Updates** uses a configured local or opt-in HTTPS
manifest, and **Tools > Recovery and Diagnostics** creates local reviewable
failure bundles. See [docs/desktop-product-completion.md](docs/desktop-product-completion.md)
and [docs/privacy-and-offline.md](docs/privacy-and-offline.md).

The desktop can create or open `.foundryproj` workspaces, edit syntax-highlighted
C# and native OBS sources in tabs, report live language diagnostics, format C# documents, navigate to source
definitions, run the existing build pipeline, remember recent projects and
layout, preserve dirty documents as recovery snapshots, edit Streamer.bot
actions/commands/queues structurally, and inspect decoded build packages.

New Project offers seven versioned provider templates with guided author and
description fields, including Streamer.bot extension/command starters and OBS
module, filter, input, and encoded-output starters. See
[docs/project-templates.md](docs/project-templates.md).

**Tools > Reusable Components** adds reviewed managed or native source modules
as deterministic project build inputs. The snippet browser also imports
user-created catalogues and automatically combines project catalogues from
`.foundry/snippets`; see
[docs/reusable-components.md](docs/reusable-components.md).

Open multiple Streamer.bot and OBS projects together with a portable
`.foundryworkspace`, switch the active project from the tree, or build every
member with **Build > Build Workspace**. See
[docs/multi-project-workspaces.md](docs/multi-project-workspaces.md).

Export or import reviewable source-only `.foundrytemplate` packages from the
File menu, and migrate schema-0 projects through an explicit backup-first flow.
See [docs/template-interchange-and-migration.md](docs/template-interchange-and-migration.md).

Editor shortcuts:

- `F12`: go to the source definition under the caret;
- `Ctrl+Alt+F`: format the current C# document;
- `Ctrl+Space` after `CPH.`: open profile-filtered CPH completion;
- `Ctrl+Space` after an `obs_` or `OBS_` prefix: open pinned libobs completion;
- `Ctrl+Shift+I`: open the searchable guided snippet browser;
- type a lowercase `cph.` prefix to open built-in snippet completion;
- `Tab` / `Shift+Tab`: move through placeholders after inserting a snippet;
- double-click a C#, C, or header diagnostic in Problems: navigate to its location.

Typing `CPH.` opens completion automatically. Typing `(` after a CPH method
opens signature and parameter help. **Code > CPH Method Reference** opens the
searchable local catalogue for the selected project profile.

In OBS projects, typing `obs_` or `OBS_` opens the pinned 32.1.2 native
catalogue. Function calls show signature and parameter help, `F12` opens the
verified SDK declaration in a read-only header tab, and **Code > OBS Native API
Reference** opens the searchable offline reference. See
[docs/native-editor.md](docs/native-editor.md).

**Build > OBS Plugin Designer** edits module and component metadata and can
generate a module starter, passthrough filter, configurable filter, or video
input source. It shows current and generated C side by side and requires an
explicit replacement confirmation. See
[docs/obs-plugin-designer.md](docs/obs-plugin-designer.md).

**Build > Build Release Package** performs a fresh validated build and creates
a provider-specific installation README, hashed build manifest, copied package
IR, verified payload directory, and ZIP under `build/release`. The same flow is
available as `foundry release`. See
[docs/release-workflow.md](docs/release-workflow.md).

**Build > Publishing Metadata** edits distribution identity, authors, legal
files, dependencies, version, and optional signing. **Validate Publishing**
shows the checklist; **Publish Release** creates the provider archive,
dependency inventory, signing evidence, and reproducibility report. See
[docs/publishing-and-distribution.md](docs/publishing-and-distribution.md).

**Code > Snippet Browser** searches compatible built-ins, validates guided
values, previews the resulting C#, and inserts it into the active document.
The bundled revision currently contains 20 method snippets and 10 defensive
workflow snippets.

**Build > Streamer.bot Designer** edits the target definition. **Build >
Package Viewer** inspects package IR artifacts and decodes the generated
Streamer.bot envelope after a build.

**Build > Deploy / Manage Installation** opens the provider-specific safe
deployment workflow. Streamer.bot projects manage their hashed extension DLL;
OBS projects manage the verified module DLL and package data files. Both use
reviewed plans, explicit confirmation, ownership receipts, recoverable backups,
update, repair, rollback, and uninstall, and both refuse to remove modified
files. OBS mutations are additionally blocked while the selected OBS instance
is running.

The same dialog automatically checks deployment health: installed and project
versions, receipt validity, missing or modified DLLs, package drift, host
version changes, and the per-installation import/reference/compile/runtime
completion checklist. Health findings lead directly to reviewed repair,
redeploy, update, rollback, or uninstall actions.

For OBS projects, health also inspects installed and project versions, package
drift, the OBS executable version, and the newest post-install OBS log. See
[docs/safe-obs-deployment.md](docs/safe-obs-deployment.md).

## Repository layout

```text
src/                         Product source
tests/                       Automated tests
samples/                     Reviewable example projects
schemas/                     Published JSON schemas
docs/architecture/decisions/ Architecture decision records
docs/glossary.md             Shared product vocabulary
```

Read [CONTRIBUTING.md](CONTRIBUTING.md) before making a change. The product and
engineering direction is described in the repository documentation.

## v1 final acceptance

Foundry is at the v1 release-candidate gate. Start with the
[final-acceptance guide](docs/final-acceptance/README.md), complete the
[acceptance checklist](docs/final-acceptance/acceptance-checklist.md), and use
the [v1 release runbook](docs/release/v1-release.md). The exact supported hosts
and the distinction between automated and real-host evidence are published in
the [v1 compatibility matrix](docs/compatibility/v1-matrix.md).

The Phase 16 automation entry points are
`eng/release/invoke-final-acceptance.ps1`, `eng/release/package-v1.ps1`, and
`eng/release/verify-v1-release.ps1`. Their structured outputs follow the final
acceptance and v1 release-manifest schemas under `schemas/product`.

The private-alpha documents remain as historical acceptance evidence, not as
the current distribution instructions.

## Compatibility note

The Foundry application toolchain currently targets .NET 10. This does not
decide the target framework of extension DLLs loaded by Streamer.bot. The
inspected executables configure .NET Framework 4.7.2, while their
plugin-interface assemblies target .NET Framework 4.8.1. The bridge and
extension runtime strategy has now been proven across the supplied stable,
alpha, and beta installations. Representative package exports from all three
versions were also accepted by all three importers; Streamer.bot 1.0.4 displays
a confirmation warning before importing prerelease-origin exports.

The original compatibility spike and its manual verification gate are documented
in
[docs/compatibility/streamerbot-phase-1-spike.md](docs/compatibility/streamerbot-phase-1-spike.md).

The import/export envelope, captured schema versions, and package-adapter
constraints are documented in
[docs/compatibility/streamerbot-import-export-format.md](docs/compatibility/streamerbot-import-export-format.md).

The `.foundryproj` v1 contract and validation command are documented in
[docs/project-format.md](docs/project-format.md).

The Phase 3 desktop workflow and local state contract are documented in
[docs/desktop-workspace.md](docs/desktop-workspace.md).

The Phase 4A C# editor architecture and behavior are documented in
[docs/csharp-editor.md](docs/csharp-editor.md).

The Phase 4B catalogue, profile filtering, and CPH diagnostics are documented
in [docs/cph-catalogue.md](docs/cph-catalogue.md).

The Phase 5A snippet manifest, built-in library, expansion behavior, and
verification rules are documented in [docs/snippets.md](docs/snippets.md).

The Phase 6 structured model, stable-v23 exporter, deterministic ID contract,
and desktop tools are documented in
[docs/streamerbot-designer-exporter.md](docs/streamerbot-designer-exporter.md).

The Phase 7A deployment trust boundary, receipts, backups, rollback, and
uninstall behavior are documented in
[docs/safe-deployment.md](docs/safe-deployment.md).
