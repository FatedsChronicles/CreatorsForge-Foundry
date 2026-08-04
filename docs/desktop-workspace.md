# Desktop workspace

Phase 3 introduces the first Windows desktop shell for Creators Forge Foundry.
It is a WPF application targeting `net10.0-windows`; the underlying workspace
services target plain `net10.0` and do not depend on WPF.

## Current workflow

The desktop supports:

- creating a minimal, buildable Streamer.bot project;
- opening a `.foundryproj` directly or from the recent-project list;
- browsing a bounded project tree;
- opening supported text files in document tabs;
- dirty-state tracking, Save, Save All, and close confirmation;
- invoking the Phase 2 build and viewing Problems and Build output;
- changing the default project directory and recovery interval;
- resizing the shell panels, persisting that layout, and resetting it;
- writing recovery snapshots for dirty documents and restoring a newer
  snapshot when the document is reopened.

New projects use the `managedLibrary` and `cphInlineBridge` outputs with the
`args-log-v1` bridge contract. Their generated starter entry point therefore
passes the existing build and bridge-signature gate without additional edits.

## Local state

User state is stored under:

```text
%LOCALAPPDATA%\Creators Forge\Foundry\
  settings.json
  recent-projects.json
  recovery\
```

Settings and recent lists are written through a temporary file and an atomic
replace. Invalid state files do not prevent startup: Foundry falls back to safe
defaults and reports a warning in Problems. Recovery filenames are SHA-256
identifiers derived from normalized full document paths, so project filenames
are not copied into the state directory.

Document reads and writes are constrained to the project directory. Existing
reparse points are rejected at the workspace persistence boundary, documents
larger than 4 MiB are not loaded into the editor, and project-tree enumeration
is bounded and ignores generated and source-control directories.

## Verification

The automated Phase 3 gate creates a project, opens its generated source,
edits and saves it, reopens the project, and verifies the exact persisted text.
Additional tests cover path confinement, recent-project corruption, settings
fallback and bounds, and recovery round trips. The application also exposes
`--smoke-test`, which initializes desktop state and exits without displaying a
window for build-machine startup verification.

## Deferred work

This slice deliberately uses WPF split panels. Phase 4A has since replaced the
platform text control with the Roslyn-backed C# editor described in
[csharp-editor.md](csharp-editor.md). A docking framework, external file change
detection, command palette, multi-project solutions, and visual design/runtime
views belong to later roadmap slices.
