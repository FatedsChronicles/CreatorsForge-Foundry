# Desktop product completion

Phase 14 turns the development shell into an installable, diagnosable desktop
product while preserving Foundry's source-first and offline-first boundaries.

## Install and uninstall

Create a self-contained Windows x64 package:

```powershell
.\eng\desktop\package-desktop.ps1 -Version 1.0.0
```

Phase 19 packages a native Windows setup executable using Inno Setup 6. The
interactive wizard defaults to `C:\Program Files\Creators Forge\Foundry` but
allows the end user to choose another location. Its stable product identity
retains that choice during upgrades, registers Foundry in Windows Apps / Add or
Remove Programs, creates an optional desktop shortcut and a Start Menu shortcut,
and blocks uninstall while Foundry is running. The native uninstaller removes
only installer-owned application files; projects, settings, and recovery data
remain untouched.

When setup detects the receipt-backed Phase 14 installation at the old default
per-user location, it adopts that directory instead of creating a duplicate or
deleting it. Clean installations still default to Program Files. All later
native updates retain whichever directory the user selected or migrated from.

Packaging emits separate `CreatorsForge-Foundry-<version>-Setup.exe` and
`CreatorsForge-Foundry-<version>-Update.exe` assets from the same payload, plus
`foundry-update.json`. The manifest contains the updater location, size,
SHA-256, publication time, and GitHub release-notes location. Update staging
verifies both size and hash before enabling **Install Verified Update**.

Install Inno Setup 6 on a packaging workstation before running the command:

```powershell
winget install --id JRSoftware.InnoSetup
```

The older PowerShell scripts remain as historical Phase 14 evidence and are no
longer included in the end-user package.

### Phase 19 acceptance evidence

Product-owner acceptance completed on 2026-08-04 using the generated
`0.19.0-alpha.1` setup and `0.19.0-alpha.2` updater. The following checks passed:

- installation to the default directory;
- installation to a user-selected custom directory;
- verified in-place update with the selected directory retained;
- native Windows uninstall; and
- preservation of user-owned projects, settings, and recovery data.

The complete automated gate also passed with 220 tests, zero build warnings or
errors, and all managed, native, and multi-project desktop smoke tests.

GitHub Release creation and upload of the setup, updater, and update manifest
are automated by the guarded manual workflow documented in
[github-release-automation.md](github-release-automation.md).

The Phase 19C channel increment passes 223 automated tests with zero build
warnings or errors. Product-owner acceptance also passed: alpha.5 discovered,
verified, and installed the published alpha.6 release entirely through Foundry's
Prerelease channel, then reported alpha.6 up to date. Settings provides a
one-click action to restore the canonical GitHub source after local/offline use.
The installed alpha.7 build passed the corresponding visual and functional
acceptance check: the action was visible and restored the canonical URL exactly.

## First-run and toolchain health

First launch checks supported Windows, .NET 10, writable local storage, CMake,
Visual Studio C++ tools, and the pinned OBS SDK. Only desktop/runtime checks are
required for Streamer.bot work; native tools are marked optional until needed.
Setup can be reopened from **Tools > Run Setup Checks**.

**Tools > Development Toolchain** shows CMake, MSVC, and OBS SDK status. It uses
`vswhere` to discover current Visual Studio instances, validates the complete
x64 C++ toolset, supports a guided installation-root selection, and persists
the chosen instance. OBS builds receive that instance directly; Foundry never
changes the global `PATH`. The SDK can be downloaded explicitly or installed
from an offline archive folder. See
[guided-toolchain-setup.md](guided-toolchain-setup.md).

## Updates, recovery, accessibility, and performance

Settings separates workspace, update, and privacy options. Update checks are
manual, default to the official GitHub Releases manifest, support explicit
Stable and Prerelease channels plus channel-neutral local/custom manifests,
require opt-in for HTTPS, and never install silently. Stable follows GitHub's
latest stable release. Prerelease includes published alpha, beta, release
candidate, and stable releases but excludes drafts. **Tools >
Recovery and Diagnostics** lists local recovery/failure
evidence and creates a reviewable bundle.

Core regions expose automation names, menu access keys remain available,
`Ctrl+,` opens Settings, `F1` opens setup checks, and Windows high-contrast
colours replace the Foundry palette when high contrast is active. Project-tree
enumeration runs off the UI thread, skips generated trees and reparse points,
and is capped at 10,000 entries and 32 levels; a 2,000-file regression fixture
guards responsiveness.

See [privacy-and-offline.md](privacy-and-offline.md) for the reviewed data and
network boundary.

Private-alpha product bundles add invitation-channel verification, tester
guidance, published compatibility evidence, and representative samples without
changing these Phase 14 installation and privacy boundaries. See
[private-alpha/README.md](private-alpha/README.md).
