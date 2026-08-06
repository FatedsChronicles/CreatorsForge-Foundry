# Creators Forge Foundry

Creators Forge Foundry is a local-first Windows development environment for content creators and developers building extensions for Streamer.bot and native plugins for OBS Studio.

Foundry brings project creation, source editing, code intelligence, testing, packaging, publishing, deployment, health monitoring, repair, rollback, and uninstall into one integrated desktop workspace. It is designed to make creator-tool development more approachable while retaining the structured, deterministic workflows expected from a professional software-development environment.

The repository includes a WPF desktop workspace and CLI. It contains the
versioned source-first project model, project creation and persistence, a
Roslyn-backed C# editor, managed builds, deterministic bridge generation,
structured diagnostics, Streamer.bot compatibility experiments, tests, and
architecture records.

## Prerequisites

- Windows 10 or later.
- .NET SDK 10.0.302. `global.json` permits later 10.0 patch releases.
- PowerShell 5.1 or later.
- CMake 3.20 or later and the Visual Studio C++ x64 toolchain for OBS projects.

## Why Foundry?

Streamer.bot and OBS Studio are powerful platforms, but developing extensions for them normally requires knowledge spread across several tools and workflows:

- C# and native C development
- Provider-specific APIs and lifecycle requirements
- Streamer.bot import/export formats
- OBS SDK and compiler configuration
- Manual DLL installation
- Runtime compatibility testing
- Package construction
- Version management
- Deployment recovery and rollback

Foundry combines these concerns into a source-first project system with guided desktop workflows, structured diagnostics, repeatable builds, and safety controls around installed files.

It does not hide the source code or replace the underlying platforms. Instead, it provides a consistent development environment around them.

## Core capabilities

### Unified project system

Foundry uses versioned `.foundryproj` manifests to describe:

- Project identity and semantic version
- Target provider and compatibility profile
- Managed or native build inputs
- Streamer.bot package definitions
- OBS plugin metadata and component design
- Generated CPHInline bridge configuration
- Test definitions and compatibility profiles
- Reusable components
- Publishing and signing configuration
- Optional non-running design-preview source and viewport
- Expected release outputs

Projects remain reviewable, portable, and suitable for source control.

Foundry validates manifests before building and reports structured diagnostics with stable error codes and actionable source locations.

### Streamer.bot extension development

Foundry supports creating and maintaining managed Streamer.bot extensions with:

- Streamer.bot extension and command-workflow templates
- Deterministic managed builds
- Generated `CPHInline` bridge code
- Structured actions, commands, queues, triggers, and sub-actions
- Stable version-23 package encoding and decoding
- Deterministic package IDs
- Import-code generation
- Package inspection and round-trip validation
- Compiler-reference guidance
- Runtime deployment health checks

The generated managed libraries target the compatibility requirements of the supported Streamer.bot installations rather than the framework used by the Foundry desktop application.

### OBS Studio plugin development

Foundry provides a pinned native development workflow for OBS Studio plugins, including:

- OBS module templates
- Passthrough video-filter templates
- Configurable video-filter templates
- Video input-source templates
- Encoded-output templates
- Lifecycle-safe create and destroy callbacks
- Pinned OBS SDK management
- CMake and MSVC toolchain integration
- Native build diagnostics
- Plugin ABI inspection
- Module-load verification
- Source registration and lifecycle testing
- OBS log inspection

The OBS Plugin Designer provides a structured interface for module and component metadata while keeping the generated C source visible and reviewable.

### Crash-isolated design preview

The Design Preview provides viewport-aware feedback for static HTML, WinForms
source, and declared OBS components. Phase 22A safely extracts a bounded,
hashed structural frame. Phase 22B sends only that sanitized frame to a
separate preview-host process for richer role-aware rendering. The desktop can
stop or restart the host, contains crashes and eight-second timeouts, exposes
bounded logs, and refreshes automatically when the selected source is saved.
Phase 22C supplies distinct static-web, WinForms, and OBS component adapters:
safe browser-like document chrome, Windows form/control composition, and OBS
program-canvas/properties composition. The adapters operate only on sanitized
design metadata; project assemblies, scripts, browser engines, libobs, and
plugin DLLs remain unexecuted.

Representative visual projects are available in
`samples/VisualPreviewSamples.foundryworkspace`.

## Integrated editors

### Roslyn-powered C# editor

The managed editor provides:

- C# syntax highlighting
- Roslyn diagnostics
- Source formatting
- Definition navigation
- Profile-aware `CPH` completion
- Signature and parameter help
- Local API documentation
- Streamer.bot compatibility diagnostics
- Guided snippet insertion
- Placeholder navigation

The bundled catalogue includes verified method snippets and defensive workflow snippets for common creator automation scenarios.

### Native OBS editor

The native editor provides:

- C and header-file editing
- Pinned libobs API completion
- Function signature help
- OBS constant and symbol completion
- Definition navigation into verified SDK headers
- Read-only SDK reference tabs
- Native compiler diagnostics
- Searchable offline OBS API documentation

## Testing and debugging

Foundry uses a provider-neutral testing system so Streamer.bot extensions and OBS plugins can be tested through a consistent workflow.

### Streamer.bot testing

The Streamer.bot mock runtime supports:

- Simulated commands, rewards, and test events
- CPH argument injection
- Captured log calls
- Captured CPH method calls
- Return-value assertions
- Log-content assertions
- Argument assertions
- CPH call-count assertions
- Compatibility matrices across supported profiles

Mock-runtime testing does not replace real Streamer.bot acceptance. Foundry separately documents and tracks runtime installation, import, compilation, and execution in each supported host.

### OBS testing

Native OBS testing includes:

- PE and ABI inspection
- Required module-export verification
- Crash-isolated module loading
- Source registration checks
- Source creation and destruction
- Timeout-controlled helper processes
- Abnormal-exit and native-crash reporting
- Compatibility-matrix results

Native modules are loaded in a separate test process so a plugin crash cannot terminate the Foundry editor.

### Desktop Test Explorer

The Test Explorer can:

- Run individual project tests
- Run complete compatibility matrices
- Select disposable OBS installations
- Filter cases by name or outcome
- Display event arguments
- Inspect assertions and actual values
- Show captured logs and CPH calls
- Navigate diagnostics back into source files
- Retain structured JSON test results

## Deterministic builds and packages

Foundry treats reproducibility as a first-class feature.

Build outputs use deterministic directory layouts and include:

- Managed or native binaries
- Generated bridge source
- Provider-specific package artifacts
- Package intermediate representation
- Relative artifact paths
- SHA-256 hashes
- Build and publishing diagnostics

The regression suite compares reviewed semantic package snapshots and verifies that repeated builds from identical inputs produce identical artifact sets. Fixed-time release tests verify byte-identical archives.

## Safe deployment

Foundry provides reviewed deployment workflows for both providers.

Before changing an installation, Foundry produces a preview showing the proposed operation and affected files. Applying the operation requires explicit confirmation.

### Streamer.bot deployment

Foundry can:

- Discover or select Streamer.bot installations
- Preview DLL installation and updates
- Install the managed extension DLL
- Generate and copy import code
- Record installation ownership
- Track compiler-reference and runtime-verification steps
- Compare installed and project versions
- Detect missing or modified DLLs
- Repair or redeploy owned files
- Roll back to the previous receipt
- Uninstall Foundry-owned files

### OBS deployment

Foundry can:

- Discover or select OBS installations
- Verify the supported OBS version
- Preview DLL and plugin-data changes
- Install native module files
- Record ownership receipts
- Create recoverable backups
- Compare installed and project versions
- Detect missing or modified files
- Inspect post-install OBS logs
- Repair, update, roll back, or uninstall
- Block changes while OBS is running

### Ownership protection

Deployment operations are receipt-based. Foundry refuses to silently delete or overwrite a file that no longer matches the file it installed.

User-owned configuration, Streamer.bot actions, OBS scenes, and unrelated installation files remain outside Foundry’s ownership boundary.

## Project templates and reusable components

Foundry includes seven versioned project templates spanning both providers.

Template creation supports guided parameters such as:

- Project identity
- Author
- Description
- Target provider
- Compatibility profile
- OBS component name and ID

Reusable managed and native components can be installed as explicit source build inputs. Component provenance is recorded in the project manifest, and collision checks prevent existing project files from being silently replaced.

Users can also create and import custom snippet catalogues. Project-specific catalogues under `.foundry/snippets` are combined with the built-in catalogue.

## Multi-project workspaces

Portable `.foundryworkspace` files can group Streamer.bot and OBS projects together.

The desktop supports:

- Mixed-provider workspaces
- Active-project switching
- Startup-project selection
- Workspace-wide validation
- Workspace-wide builds
- Shared project navigation
- Portable relative project paths

## Template interchange and migration

Source-only `.foundrytemplate` packages can be exported, reviewed, shared, and imported with guided parameters.

Older project manifests can be upgraded through an explicit migration workflow that provides:

- Migration inspection
- Planned-change previews
- Backup-first replacement
- Schema validation
- Unknown-field preservation
- Atomic project replacement

## Publishing and distribution

Foundry includes publishing workflows for release-ready provider packages.

Publishing metadata can describe:

- Package identity
- Semantic version
- Summary and authors
- Licence file
- Changelog
- Tags
- Homepage and repository
- Runtime, library, and tool dependencies
- Optional Windows code-signing settings

Publishing validation checks the project build, provider archive, legal files, changelog version, dependency inventory, compatibility evidence, and signing configuration.

Successful publishing produces:

- Provider distribution archive
- Package manifest
- Dependency inventory
- Signing evidence
- Reproducibility report
- SHA-256 hashes
- Release checklist results

A public Foundry marketplace is a possible future extension, not a dependency of v1.

## Desktop product features

The WPF desktop application includes:

- First-run setup and dependency checks
- Recent-project history
- Configurable project locations
- Tabbed source editing
- Project and workspace trees
- Problems, build-output, and console panels
- Searchable snippet and API browsers
- Package viewer
- Streamer.bot Designer
- OBS Plugin Designer
- Test Explorer
- Deployment management
- Publishing metadata editor
- Toolchain management
- Keyboard navigation
- High-contrast and accessibility support
- Recovery snapshots for unsaved documents
- Local diagnostic and failure bundles
- Manual, verified application updates
- Receipt-guarded installation and uninstall

The self-contained desktop package installs per user and does not require administrative installation into system directories.

## Privacy and offline operation

Foundry is designed to operate locally.

It has:

- No telemetry
- No advertising identifiers
- No analytics
- No account requirement
- No automatic diagnostic upload
- No automatic source-code upload

Projects, settings, build output, recovery snapshots, deployment receipts, test results, update packages, and failure reports remain on the local computer.

Network access is disabled by default. It is used only after the user enables it and explicitly starts an operation such as an OBS SDK download or update check.

Diagnostic bundles are created only on request and can be reviewed before sharing. Project source and recovery text are not automatically included.

Uninstall preserves local settings and recovery information by default. User data is removed only when the explicit removal option is supplied.

## Supported v1 hosts

Foundry v1 has been developed and verified against these exact hosts:

| Provider | Supported host |
|---|---|
| Streamer.bot | 1.0.4 Stable |
| Streamer.bot | 1.0.5-alpha.34 |
| Streamer.bot | 1.0.5-beta.1 |
| OBS Studio | 32.1.2 on Windows x64 |

Important compatibility boundaries:

- OBS Studio 32.1.2 Windows x64 is the only exact OBS release currently included in the v1 support matrix.
- The internal `32.x-windows-x64` profile does not imply support for every OBS 32.x release.
- Streamer.bot prerelease compatibility applies to the exact tested alpha and beta builds.
- Foundry emits the stable version-23 Streamer.bot package contract.
- Automated compatibility tests supplement rather than replace real-host runtime verification.

See the repository compatibility matrix for the distinction between automated and real-host evidence.

## Prerequisites

Building Foundry from source requires:

- Windows 10 or later, x64
- .NET SDK 10.0.302 or a permitted later .NET 10 patch
- PowerShell 5.1 or later
- CMake 3.20 or later for OBS projects
- Visual Studio C++ x64 Build Tools for OBS projects

The pinned OBS SDK can be managed through Foundry’s toolchain interface and supports reviewed offline archives.

## Build from source

Clone the repository and run:

```powershell
.\build.ps1
```

This restores repository-defined dependencies, builds the complete solution in Release configuration, and runs the automated test suite.

For a Debug build:

```powershell
.\build.ps1 -Configuration Debug
```

Launch the desktop application:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.App
```

## Command-line interface

The Foundry CLI supports validation, builds, testing, compatibility matrices, releases, publishing, version updates, and OBS SDK management.

Examples:

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  validate .\samples\HelloFoundry\HelloFoundry.foundryproj
```

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  build .\samples\HelloFoundry\HelloFoundry.foundryproj
```

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  test-matrix .\samples\HelloFoundry\HelloFoundry.foundryproj
```

```powershell
dotnet run --project .\src\CreatorsForge.Foundry.Cli -- `
  publish .\samples\HelloFoundry\HelloFoundry.foundryproj
```

## Repository structure

```text
src/                         Application and library source
tests/                       Automated test projects
samples/                     Reviewable example projects
schemas/                     Published JSON schemas
eng/                         Build, packaging, acceptance, and release tooling
experiments/                 Compatibility investigations and probes
docs/                        Product and engineering documentation
docs/architecture/decisions/ Architecture decision records
```

## Documentation

The repository includes documentation covering:

- Project manifest format
- Desktop workspace
- Managed C# editor
- CPH API catalogue
- Native OBS editor
- Snippet system
- Streamer.bot designer and exporter
- OBS Plugin Designer
- Testing and debugging
- Safe Streamer.bot deployment
- Safe OBS deployment
- Project templates
- Reusable components
- Multi-project workspaces
- Template import, export, and migration
- Publishing and distribution
- Offline behaviour and privacy
- Compatibility evidence
- Final acceptance
- Release procedures
- User training

## Project status

The planned v1 engineering phases are implemented. Foundry is currently undergoing the final clean-machine acceptance process covering:

- Installation and first-run setup
- Application updates
- Streamer.bot creation, build, deployment, and execution
- OBS plugin creation, build, deployment, persistence, and shutdown
- Deployment update, repair, rollback, and uninstall
- Modified-file protection
- Recovery behaviour
- User-data preservation
- Reproducible release artifacts
- Final licence and publisher-trust decisions

Until these release-owner gates are complete, repository builds should be treated as release candidates rather than the final stable v1 release.

## Contributing

Please read `CONTRIBUTING.md` before submitting a change.

Contributions should preserve Foundry’s core principles:

- Source-first and reviewable projects
- Explicit compatibility profiles
- Deterministic builds
- Structured diagnostics
- Safe deployment boundaries
- Lifecycle-correct native code
- Offline-capable workflows
- User-controlled data and diagnostics
- No silent modification of unowned files

## Creators Forge

Creators Forge Foundry is built to help creators move from an automation idea to a tested, deployable extension without losing visibility or control over the underlying code.

Whether the target is a Streamer.bot workflow, a managed extension library, an OBS video filter, a native input source, or a reusable creator-development component, Foundry provides one structured path from project creation to runtime verification.
