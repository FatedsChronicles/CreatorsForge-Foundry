# Desktop product completion

Phase 14 turns the development shell into an installable, diagnosable desktop
product while preserving Foundry's source-first and offline-first boundaries.

## Install and uninstall

Create a self-contained Windows x64 package:

```powershell
.\eng\desktop\package-desktop.ps1 -Version 1.0.0
```

The package contains the application and reviewed installer and uninstaller
scripts. Installation is per-user, refuses to run while Foundry is open, stages
files before replacement, keeps a rollback copy until success, writes an
ownership receipt, and creates a Start Menu shortcut. Uninstall requires that
receipt and preserves user data unless `-RemoveUserData` is supplied.

Packaging also emits `foundry-update.json`, containing the version, package
location, size, SHA-256, and publication time. Update staging verifies both size
and hash before exposing the package.

## First-run and toolchain health

First launch checks supported Windows, .NET 10, writable local storage, CMake,
Visual Studio C++ tools, and the pinned OBS SDK. Only desktop/runtime checks are
required for Streamer.bot work; native tools are marked optional until needed.
Setup can be reopened from **Tools > Run Setup Checks**.

**Tools > Development Toolchain** shows CMake, MSVC, and OBS SDK status. The SDK
can be downloaded explicitly or installed from an offline archive folder.

## Updates, recovery, accessibility, and performance

Settings separates workspace, update, and privacy options. Update checks are
manual, support local manifests, require opt-in for HTTPS, and never install
silently. **Tools > Recovery and Diagnostics** lists local recovery/failure
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
